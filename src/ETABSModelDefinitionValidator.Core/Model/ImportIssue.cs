namespace ETABSModelDefinitionValidator.Core.Model
{
    public enum ImportIssueLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// A problem encountered while importing/normalizing the source data - malformed row,
    /// unrecognized table, unparseable name, etc. Kept separate from ValidationResult because
    /// these are import-time facts about the data, not the outcome of an engineering rule.
    /// </summary>
    public sealed class ImportIssue
    {
        public ImportIssueLevel Level { get; set; }
        public string Message { get; set; }
        public SourceLocation Source { get; set; }

        public override string ToString() => $"[{Level}] {Message} ({Source})";
    }
}
