using System.Dynamic;

namespace EmailForwarder;

public record EmailForwardingRule(string SourceEmailAddress, string SourceEmailPassword, string Name,
    string DestinationEmailAddress)
{
    public string SanitizedIdentifier { get; } =
        (SourceEmailAddress + DestinationEmailAddress)
        .Replace(string.Join("", Path.GetInvalidFileNameChars()), "_");
};

public record ReceiverSettings(string Pop3Server, int Pop3Port, bool UseSsl);

public record SenderSettings(string SmtpServer, int SmtpPort, bool UseSsl);
