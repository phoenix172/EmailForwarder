using System.Data;
using MailKit;
using MailKit.Net.Pop3;
using MailKit.Net.Smtp;
using MimeKit;

namespace EmailForwarder;

public class EmailForwarder : IAsyncDisposable
{
    private readonly ReceiverSettings _receiverSettings;
    private readonly SenderSettings _senderSettings;
    private readonly ILogger<EmailForwarder> _logger;

    private ForwardedEmailsCollection _forwardedEmails;
    private SmtpClient? _smtpClient;
    private Pop3Client? _pop3Client;

    public EmailForwarder(EmailForwardingRule forwardingRule, 
        ReceiverSettings receiverSettings, 
        SenderSettings senderSettings,
        ILogger<EmailForwarder> logger)
    {
        ForwardingRule = forwardingRule;
        _receiverSettings = receiverSettings;
        _senderSettings = senderSettings;
        _logger = logger;

        
    }

    public EmailForwardingRule ForwardingRule { get; set; }

    public async Task Init(CancellationToken token)
    {
        _forwardedEmails = new ForwardedEmailsCollection(File.Open(ForwardingRule.SanitizedIdentifier, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        _pop3Client = await GetPop3Client(token);
        _smtpClient = await GetSmtpClient(token);
        await _pop3Client.DisconnectAsync(false, token);
        await _smtpClient.DisconnectAsync(false, token);
    }

    private async Task<Pop3Client> GetPop3Client(CancellationToken token)
    {
        _pop3Client ??= new Pop3Client();
        _pop3Client.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
        await _pop3Client.ConnectAsync(_receiverSettings.Pop3Server, _receiverSettings.Pop3Port, _receiverSettings.UseSsl,
            token);
        _logger.LogInformation(ForwardingRule.SourceEmailPassword);
        await _pop3Client.AuthenticateAsync(ForwardingRule.SourceEmailAddress, ForwardingRule.SourceEmailPassword, token);

        _logger.LogInformation("Pop3Client has initialized successfully");
        return _pop3Client;
    }

    private async Task<SmtpClient> GetSmtpClient(CancellationToken token)
    {
        _smtpClient ??= new SmtpClient();
        _smtpClient.ServerCertificateValidationCallback = (sender, certificate, chain, errors) => true;
        //client.ServerCertificateValidationCallback = (sender, certificate, certChainType, errors) => true;
        await _smtpClient.ConnectAsync(_senderSettings.SmtpServer, _senderSettings.SmtpPort, _senderSettings.UseSsl, token);
        //_logger.LogInformation(ForwardingRule.SourceEmailPassword);
        await _smtpClient.AuthenticateAsync(ForwardingRule.SourceEmailAddress, ForwardingRule.SourceEmailPassword, token);

        _logger.LogInformation("SmtpClient has initialized successfully");
        return _smtpClient;
    }

    public async Task ForwardMessages(CancellationToken token)
    {
        try
        {
            var client = await GetPop3Client(token);
            await ForwardMessagesCore(token,client);
            await client.DisconnectAsync(false, token);
        }
        catch(Exception ex)
        {
            _logger.LogError($"Failed to receive messages for '{ForwardingRule.SourceEmailAddress}'");
            _logger.LogInformation("Reinitializing Pop3Client");

            await DisposePop3Client();
            _pop3Client = await GetPop3Client(token);
        }
    }

    private async Task ForwardMessagesCore(CancellationToken token, Pop3Client client)
    {
        _logger.LogInformation($"Begin forward emails from mailbox '{ForwardingRule.SourceEmailAddress}'");

        var messageIds = await client.GetMessageUidsAsync(token);
        var unforwardedMessageIds = messageIds.Select((x, i) => (MessageId: x, MessageIndex: i))
            .Where(x => !_forwardedEmails.HasBeenForwarded(x.MessageId));
        var unforwardedMessageIndices = unforwardedMessageIds.Select(x => x.MessageIndex).ToList();

        List<(MimeMessage Message, string MessageId)> messages =
            (await client.GetMessagesAsync(unforwardedMessageIndices, token))
            .Zip(unforwardedMessageIds.Select(x => x.MessageId)).ToList();

        _logger.LogInformation($"Messages to be forwarded: {messages.Count}");

        if (messages.Count == 0)
        {
            _logger.LogInformation($"No pending messages for {ForwardingRule.SourceEmailAddress}");
        }
        else
        {
            var sentMessages = await SendMessages(messages, token);

            _logger.LogInformation($"Messages forwarded");

            await _forwardedEmails.AddForwarded(sentMessages);

            _logger.LogInformation(
                $"Marking {sentMessages.Count} messages as forwarded for '{ForwardingRule.SourceEmailAddress}'");
        }
    }

    async Task<IList<string>> SendMessages(IList<(MimeMessage Message, string MessageId)> messages, CancellationToken token)
    {
        try
        {
            var client = await GetSmtpClient(token);
            var result =  await SendMessagesCore(messages, client);
            await client.DisconnectAsync(false, token);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error sending messages for {ForwardingRule.SourceEmailAddress}", ex);

            _logger.LogInformation("Reinitializing SmtpClient");
            await DisposeSmtpClient();
            _smtpClient = await GetSmtpClient(token);
            return new List<string>();
        }
    }

    private async Task<IList<string>> SendMessagesCore(IList<(MimeMessage Message, string MessageId)> messages, SmtpClient client)
    {
        _logger.LogInformation($"Begin sending messages for {ForwardingRule.SourceEmailAddress}");

        _logger.LogInformation($"Authentication successful for {ForwardingRule.SourceEmailAddress}");

        var sentMessageIds = new List<string>();
        for (int i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            try
            {
                await SendMessage(message.Message, client);
                sentMessageIds.Add(message.MessageId);
                _logger.LogInformation(
                    $"Message with subject '{message.Message.Subject}' originally sent by {message.Message.Sender.Address} to {ForwardingRule.SourceEmailAddress}. Forwarding to {ForwardingRule.DestinationEmailAddress}");
            }
            catch(Exception ex)
            {
                _logger.LogError(ex,
                    $"Error forwarding message subject '{message.Message.Subject}' and id '{message.MessageId}' from '{message.Message.Sender}'");
            }
        }

        return sentMessageIds;
    }

    async Task SendMessage(MimeMessage message, SmtpClient client)
    {
        var originalSender = message.Sender ?? message.From.Mailboxes.First();

        message.From.Clear();
        message.Sender = originalSender;
        message.From.Add(new MailboxAddress(originalSender.Name + " " + originalSender.Address, ForwardingRule.SourceEmailAddress));

        message.ReplyTo.Clear();
        message.ReplyTo.Add(originalSender);

        await client.SendAsync(message, message.From.Mailboxes.First(), new[] { new MailboxAddress(ForwardingRule.Name, ForwardingRule.DestinationEmailAddress) });
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeSmtpClient();
        await DisposePop3Client();
        await DisposeForwardedEmailsStore();
    }

    private async Task DisposeForwardedEmailsStore()
    {
        _logger.LogInformation("Disposing forwarded emails store");
        await (_forwardedEmails?.DisposeAsync()).AwaitIfNotNull();

        _logger.LogInformation("Dispose complete");
    }

    private async Task DisposeSmtpClient()
    {
        _logger.LogInformation("Disposing SmtpClient");
        try
        {
            await (_smtpClient?.DisconnectAsync(true)).AwaitIfNotNull();
        }
        finally
        {
            _smtpClient?.Dispose();
            _smtpClient = null;
        }
    }

    private async Task DisposePop3Client()
    {
        _logger.LogInformation("Disposing Pop3Client");
        try
        {
            await (_pop3Client?.DisconnectAsync(true)).AwaitIfNotNull();
        }
        finally
        {
            _pop3Client?.Dispose();
            _pop3Client = null;
        }
    }
}