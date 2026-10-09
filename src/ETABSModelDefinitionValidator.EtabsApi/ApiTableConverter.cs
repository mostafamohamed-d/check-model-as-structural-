using System;
using System.Collections.Generic;
using System.Globalization;
using ETABSModelDefinitionValidator.Excel;

namespace ETABSModelDefinitionValidator.EtabsApi
{
    /// <summary>
    /// Pure conversion of what cDatabaseTables.GetTableForDisplayArray returns (a flat,
    /// record-major string array) into the same RawTable the Excel importer produces, so the
    /// existing Populate* logic is reused untouched. No ETABS dependency - unit-testable.
    /// </summary>
    public static class ApiTableConverter
    {
        public const string WorksheetLabel = "ETABS API";

        /// <param name="fieldKeys">FieldsKeysIncluded as returned by the API (column order of tableData).</param>
        /// <param name="fieldNames">Field key -> display name (from GetAllFieldsInTable). The display
        /// name is what the Excel export prints as the column header, so normalizing it gives the
        /// same keys the readers already use. Falls back to the key itself if no name is known.</param>
        public static RawTable Convert(
            string tableTitle, IReadOnlyList<string> fieldKeys, IReadOnlyDictionary<string, string> fieldNames,
            int numberRecords, IReadOnlyList<string> tableData)
        {
            var table = new RawTable { TableTitle = tableTitle, WorksheetName = WorksheetLabel };
            var columnCount = fieldKeys.Count;

            if (columnCount == 0 && numberRecords > 0)
            {
                throw new InvalidOperationException($"Table '{tableTitle}' returned {numberRecords} record(s) but no fields.");
            }
            if ((long)numberRecords * columnCount != tableData.Count)
            {
                throw new InvalidOperationException(
                    $"Table '{tableTitle}': {numberRecords} record(s) x {columnCount} field(s) does not match the {tableData.Count} values returned.");
            }

            var normalized = new string[columnCount];
            for (var c = 0; c < columnCount; c++)
            {
                var header = fieldNames != null && fieldNames.TryGetValue(fieldKeys[c], out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name : fieldKeys[c];
                normalized[c] = HeaderNormalizer.Normalize(header);
                table.Columns.Add((normalized[c], header, c + 1));
            }

            for (var r = 0; r < numberRecords; r++)
            {
                var row = new RawRow { ExcelRowNumber = r + 1 }; // record number; there is no worksheet row
                for (var c = 0; c < columnCount; c++)
                {
                    var text = tableData[r * columnCount + c];
                    if (string.IsNullOrEmpty(text) || normalized[c].Length == 0) continue; // blank cell == absent, as in Excel
                    row.Values[normalized[c]] = ParseCell(text);
                }
                table.Rows.Add(row);
            }

            return table;
        }

        /// <summary>Numbers become double (as ClosedXML gives for numeric cells); everything else stays
        /// text. Invariant culture - the API always formats numbers invariantly. Text with a leading
        /// zero ("007") is kept as text so a name is never silently rewritten.</summary>
        internal static object ParseCell(string text)
        {
            var t = text.Trim();
            if (t.Length > 1 && t[0] == '0' && char.IsDigit(t[1])) return t;
            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? (object)d : t;
        }
    }
}
