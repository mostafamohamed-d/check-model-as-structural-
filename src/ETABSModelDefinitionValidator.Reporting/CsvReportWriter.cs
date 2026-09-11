using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.Reporting
{
    /// <summary>Exports the full detailed result set to CSV - every field an engineer needs to
    /// find and understand an issue, including full source traceability.</summary>
    public static class CsvReportWriter
    {
        private static readonly string[] Headers =
        {
            "RuleId", "RuleName", "Category", "RuleType", "Severity", "Status",
            "ObjectType", "ObjectName", "Story",
            "Workbook", "Worksheet", "Table", "SourceRow",
            "FieldName", "ExpectedValue", "ActualValue", "Message", "CodeBasis"
        };

        public static void WriteToFile(IReadOnlyList<ValidationResult> results, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Headers.Select(Escape)));

            foreach (var r in results)
            {
                var fields = new[]
                {
                    r.RuleId, r.RuleName, r.Category, r.RuleType.ToString(), r.Severity.ToString(), r.Status.ToString(),
                    r.ObjectType, r.ObjectName, r.Story,
                    r.Source?.WorkbookName, r.Source?.WorksheetName, r.Source?.TableName, r.Source?.RowNumber.ToString(CultureInfo.InvariantCulture),
                    r.FieldName, r.ExpectedValue, r.ActualValue, r.Message, r.CodeBasis?.ToString()
                };
                sb.AppendLine(string.Join(",", fields.Select(Escape)));
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var needsQuoting = value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r");
            var escaped = value.Replace("\"", "\"\"");
            return needsQuoting ? $"\"{escaped}\"" : escaped;
        }
    }
}
