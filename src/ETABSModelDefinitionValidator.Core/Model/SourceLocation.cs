namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>
    /// Traceability back to the exact origin of a piece of imported data, so an engineer can
    /// locate it in the source export. Never derived from Excel cell addresses at the rule
    /// level - only the importer knows about cells; everything downstream sees this instead.
    /// </summary>
    public sealed class SourceLocation
    {
        public string WorkbookName { get; set; }
        public string WorksheetName { get; set; }
        public string TableName { get; set; }
        public int RowNumber { get; set; }

        public static SourceLocation Unknown => new SourceLocation
        {
            WorkbookName = "(unknown)",
            WorksheetName = "(unknown)",
            TableName = "(unknown)",
            RowNumber = -1
        };

        public override string ToString()
        {
            return $"{WorksheetName} / {TableName} / row {RowNumber}";
        }
    }
}
