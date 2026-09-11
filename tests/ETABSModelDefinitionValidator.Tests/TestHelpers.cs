using ETABSModelDefinitionValidator.Core.Model;

namespace ETABSModelDefinitionValidator.Tests
{
    internal static class TestHelpers
    {
        public static SourceLocation Loc(string table, int row = 4) => new SourceLocation
        {
            WorkbookName = "test.xlsx",
            WorksheetName = "TestSheet",
            TableName = table,
            RowNumber = row
        };
    }
}
