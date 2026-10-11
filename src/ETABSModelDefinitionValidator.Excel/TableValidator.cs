using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ETABSModelDefinitionValidator.Core.Model;

namespace ETABSModelDefinitionValidator.Excel
{
    /// <summary>
    /// Checks one extracted table against its <see cref="TableSchema"/> before anything is
    /// populated: required columns present, every record has a key, required values are present
    /// and parse (no silent default), keys are unique where they must be. Returns the table
    /// reduced to the accepted records plus an import report. Source-agnostic (Excel or live ETABS).
    /// </summary>
    public static class TableValidator
    {
        public static (RawTable Accepted, TableImportReport Report) Validate(RawTable table, TableSchema schema)
        {
            var report = new TableImportReport { Title = table.TableTitle, RecordCount = table.Rows.Count };

            if (schema == null)
            {
                report.AcceptedCount = table.Rows.Count;
                report.State = table.Rows.Count == 0 ? TableImportState.ReadEmpty : TableImportState.Read;
                return (table, report);
            }

            var present = new HashSet<string>(table.Columns.Select(c => c.NormalizedKey), StringComparer.OrdinalIgnoreCase);
            report.MissingColumns.AddRange(schema.RequiredColumnKeys.Where(k => !present.Contains(k)));
            if (report.MissingColumns.Count > 0)
            {
                report.State = TableImportState.SchemaInvalid;
                report.Messages.Add($"Missing required column(s): {string.Join(", ", report.MissingColumns)}. Columns found: {string.Join(", ", table.Columns.Select(c => c.OriginalHeader))}.");
                return (Copy(table, new List<RawRow>()), report);
            }

            var accepted = new List<RawRow>();
            var keyed = new List<(RawRow Row, string Key)>();

            foreach (var row in table.Rows)
            {
                var key = Text(row, schema.KeyColumn);
                if (string.IsNullOrEmpty(key))
                {
                    if (schema.AllowContinuationRows)
                    {
                        accepted.Add(row);
                        continue;
                    }
                    report.Messages.Add($"Record {row.ExcelRowNumber}: blank {schema.KeyColumn}.");
                    continue;
                }

                var problems = schema.Columns.Select(c => CheckValue(row, c)).Where(p => p != null).ToList();
                if (problems.Count > 0)
                {
                    report.Messages.Add($"Record {row.ExcelRowNumber} '{key}': {string.Join("; ", problems)}.");
                    continue;
                }

                keyed.Add((row, key));
            }

            if (schema.UniqueKey)
            {
                var duplicates = new HashSet<string>(
                    keyed.GroupBy(k => k.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key),
                    StringComparer.OrdinalIgnoreCase);
                foreach (var d in duplicates)
                {
                    var rows = keyed.Where(k => string.Equals(k.Key, d, StringComparison.OrdinalIgnoreCase)).Select(k => k.Row.ExcelRowNumber);
                    report.Messages.Add($"'{d}' appears in records {string.Join(", ", rows)}; none of them was used (which one is right is unknown).");
                }
                keyed.RemoveAll(k => duplicates.Contains(k.Key));
            }

            accepted.AddRange(keyed.Select(k => k.Row));
            accepted.Sort((a, b) => a.ExcelRowNumber.CompareTo(b.ExcelRowNumber));
            report.AcceptedCount = accepted.Count;

            report.State =
                report.RecordCount == 0 ? TableImportState.ReadEmpty :
                report.AcceptedCount == 0 ? TableImportState.SchemaInvalid :
                report.AcceptedCount < report.RecordCount ? TableImportState.Partial :
                TableImportState.Read;
            if (report.State == TableImportState.SchemaInvalid) report.Messages.Insert(0, "Every record was rejected.");

            return (Copy(table, accepted), report);
        }

        private static string CheckValue(RawRow row, ColumnRule rule)
        {
            var raw = row.Get(rule.Key);
            var text = raw?.ToString()?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return rule.AllowBlank ? null : $"{rule.Key} is blank";
            }

            if (rule.AllowedValues != null && !rule.AllowedValues.Any(v => string.Equals(v, text, StringComparison.OrdinalIgnoreCase)))
            {
                return $"{rule.Key} '{text}' is not one of {string.Join("/", rule.AllowedValues)}";
            }

            if (!rule.Numeric) return null;

            if (!TryNumber(raw, out var value)) return $"{rule.Key} '{text}' is not a number";
            if (rule.Positive && !(value > 0)) return $"{rule.Key} = {value} must be > 0";
            if (rule.NonNegative && value < 0) return $"{rule.Key} = {value} must be >= 0";
            return null;
        }

        internal static bool TryNumber(object raw, out double value)
        {
            value = 0;
            if (raw is double d) { value = d; return !double.IsNaN(d) && !double.IsInfinity(d); }
            if (raw is bool) return false;
            var s = raw?.ToString()?.Trim();
            if (string.IsNullOrEmpty(s)) return false;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                !double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) return false;
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static string Text(RawRow row, string key) => key == null ? "(no key)" : row.Get(key)?.ToString()?.Trim();

        private static RawTable Copy(RawTable source, List<RawRow> rows)
        {
            var copy = new RawTable { TableTitle = source.TableTitle, WorksheetName = source.WorksheetName, RowsSkipped = source.RowsSkipped };
            copy.Columns.AddRange(source.Columns);
            copy.Rows.AddRange(rows);
            return copy;
        }
    }
}
