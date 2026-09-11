using System;
using System.Collections.Generic;
using ClosedXML.Excel;
using ETABSModelDefinitionValidator.Core.Logging;
using ETABSModelDefinitionValidator.Core.Model;

namespace ETABSModelDefinitionValidator.Excel
{
    /// <summary>
    /// Detects ETABS tables by their "TABLE:  &lt;Title&gt;" title row, never by sheet order or
    /// sheet name (export order/naming is not stable across projects - see master prompt §6).
    /// Reads the confirmed ETABS export layout: title row, header row, units row, data rows.
    /// </summary>
    public sealed class TableDetector
    {
        private readonly IValidationLogger _logger;

        public TableDetector(IValidationLogger logger = null)
        {
            _logger = logger ?? new NullValidationLogger();
        }

        public List<RawTable> DetectTables(XLWorkbook workbook, List<ImportIssue> issues)
        {
            var tables = new List<RawTable>();

            foreach (var worksheet in workbook.Worksheets)
            {
                var usedRange = worksheet.RangeUsed();
                if (usedRange == null)
                {
                    continue;
                }

                var lastRow = usedRange.LastRow().RowNumber();
                var lastCol = usedRange.LastColumn().ColumnNumber();

                var titleCell = worksheet.Cell(1, 1).GetString();
                if (!titleCell.TrimStart().StartsWith("TABLE:", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ImportIssue
                    {
                        Level = ImportIssueLevel.Warning,
                        Message = $"Worksheet '{worksheet.Name}' does not start with a 'TABLE:' title row and was skipped.",
                        Source = new SourceLocation { WorksheetName = worksheet.Name, TableName = "(unrecognized)", RowNumber = 1 }
                    });
                    continue;
                }

                var title = titleCell.Substring(titleCell.IndexOf(':') + 1).Trim();
                title = System.Text.RegularExpressions.Regex.Replace(title, @"\s{2,}", " ");

                const int headerRowNumber = 2;
                var table = new RawTable { TableTitle = title, WorksheetName = worksheet.Name };

                for (var col = 1; col <= lastCol; col++)
                {
                    var header = worksheet.Cell(headerRowNumber, col).GetString();
                    if (string.IsNullOrWhiteSpace(header)) continue;
                    table.Columns.Add((HeaderNormalizer.Normalize(header), header, col));
                }

                if (table.Columns.Count == 0)
                {
                    issues.Add(new ImportIssue
                    {
                        Level = ImportIssueLevel.Warning,
                        Message = $"Table '{title}' in worksheet '{worksheet.Name}' has no recognizable header row and was skipped.",
                        Source = new SourceLocation { WorksheetName = worksheet.Name, TableName = title, RowNumber = headerRowNumber }
                    });
                    continue;
                }

                var dataStartRow = headerRowNumber + 1;
                var firstDataCandidate = worksheet.Cell(dataStartRow, 1).GetString();
                if (string.IsNullOrWhiteSpace(firstDataCandidate))
                {
                    // Units row detected (blank first column) - skip it.
                    dataStartRow++;
                }

                for (var row = dataStartRow; row <= lastRow; row++)
                {
                    var firstCellValue = worksheet.Cell(row, 1).GetString();
                    var rowHasAnyData = false;
                    var rawRow = new RawRow { ExcelRowNumber = row };

                    foreach (var (normalizedKey, _, colIndex) in table.Columns)
                    {
                        var cell = worksheet.Cell(row, colIndex);
                        if (cell.IsEmpty()) continue;

                        object value = cell.DataType == XLDataType.Boolean ? (object)cell.GetBoolean()
                            : cell.DataType == XLDataType.Number ? (object)cell.GetDouble()
                            : cell.GetString();

                        if (!rawRow.Values.ContainsKey(normalizedKey))
                        {
                            rawRow.Values[normalizedKey] = value;
                        }
                        rowHasAnyData = true;
                    }

                    if (!rowHasAnyData && string.IsNullOrWhiteSpace(firstCellValue))
                    {
                        continue;
                    }

                    table.Rows.Add(rawRow);
                }

                tables.Add(table);
                _logger.Info($"Detected table '{title}' in worksheet '{worksheet.Name}' with {table.Rows.Count} row(s).");
            }

            return tables;
        }
    }
}
