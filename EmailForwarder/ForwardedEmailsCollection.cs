using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EmailForwarder
{
    public class ForwardedEmailsCollection 
    {
        private readonly FileStream _storage;
        private readonly HashSet<string> _forwardedEmails;

        public ForwardedEmailsCollection(FileStream storage)
        {
            _storage = storage;
            using var streamReader = new StreamReader(storage, leaveOpen:true);
            string[] array = {};
            try
            {
                array = JsonSerializer.Deserialize<string[]>(streamReader.ReadToEnd()) ?? array;
            }
            catch
            {
                    
            }
            _forwardedEmails = new HashSet<string>(array);
        }

        public bool HasBeenForwarded(string messageId)
        {
            return _forwardedEmails.Contains(messageId);
        }

        public async Task AddForwarded(IEnumerable<string> messageIds)
        {
            foreach (var id in messageIds)
            {
                _forwardedEmails.Add(id);
            }

            string serialized = JsonSerializer.Serialize(_forwardedEmails);
            _storage.SetLength(0);
            await using StreamWriter writer = new StreamWriter(_storage, leaveOpen: true);
            await writer.WriteLineAsync(serialized);
            await writer.FlushAsync();
        }
        
        public async ValueTask DisposeAsync()
        {
            await _storage.FlushAsync();
            await _storage.DisposeAsync();
        }
    }
}
