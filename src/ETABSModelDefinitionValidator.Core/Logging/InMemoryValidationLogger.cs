using System;
using System.Collections.Generic;

namespace ETABSModelDefinitionValidator.Core.Logging
{
    public sealed class LogEntry
    {
        public DateTime TimestampUtc { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }

        public override string ToString() => $"[{TimestampUtc:HH:mm:ss}] {Level,-7} {Message}";
    }

    /// <summary>Collects every log entry so the UI can show "why didn't this rule run" diagnostics.</summary>
    public sealed class InMemoryValidationLogger : IValidationLogger
    {
        public List<LogEntry> Entries { get; } = new List<LogEntry>();

        public void Debug(string message) => Add("DEBUG", message);
        public void Info(string message) => Add("INFO", message);
        public void Warning(string message) => Add("WARN", message);
        public void Error(string message) => Add("ERROR", message);

        private void Add(string level, string message) =>
            Entries.Add(new LogEntry { TimestampUtc = DateTime.UtcNow, Level = level, Message = message });
    }
}
