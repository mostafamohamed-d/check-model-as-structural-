using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.Reporting
{
    /// <summary>Prints the summary + failing/warning detail to the console. Used by the console
    /// runner today; the WPF dashboard (Phase 7) will render the same ValidationSummary/ValidationResult
    /// data instead of re-deriving it.</summary>
    public static class ConsoleReportWriter
    {
        public static void WriteSummary(IReadOnlyList<ValidationResult> results, TextWriter writer = null)
        {
            writer = writer ?? Console.Out;
            var summary = ValidationSummary.From(results);

            writer.WriteLine();
            writer.WriteLine($"Overall Model Status: {summary.OverallStatus}");
            writer.WriteLine();
            writer.WriteLine($"PASS         {summary.PassCount}");
            writer.WriteLine($"WARNING      {summary.WarningCount}");
            writer.WriteLine($"FAIL         {summary.FailCount}");
            writer.WriteLine($"EXEMPT       {summary.ExemptCount}");
            writer.WriteLine($"NOT_CHECKED  {summary.NotCheckedCount}");
            writer.WriteLine($"TOTAL        {summary.TotalCount}");
            writer.WriteLine();
            writer.WriteLine("By category:");
            foreach (var cat in summary.ByCategory.Values.OrderBy(c => c.Category))
            {
                writer.WriteLine($"  {cat.Category,-20} PASS={cat.PassCount,-5} WARN={cat.WarningCount,-5} FAIL={cat.FailCount,-5} EXEMPT={cat.ExemptCount,-5} NOT_CHECKED={cat.NotCheckedCount}");
            }
        }

        public static void WriteFailuresAndWarnings(IReadOnlyList<ValidationResult> results, TextWriter writer = null)
        {
            writer = writer ?? Console.Out;
            var problems = results.Where(r => r.Status == ValidationStatus.Fail || r.Status == ValidationStatus.Warning)
                .OrderBy(r => r.RuleId);

            writer.WriteLine();
            writer.WriteLine("Failures and warnings:");
            foreach (var r in problems)
            {
                writer.WriteLine();
                writer.WriteLine($"{r.Status}  {r.RuleId}  {r.Category}");
                writer.WriteLine($"  Object:   {r.ObjectType} '{r.ObjectName}'" + (string.IsNullOrEmpty(r.Story) ? "" : $" (Story: {r.Story})"));
                writer.WriteLine($"  Source:   {r.Source}");
                if (r.FieldName != null) writer.WriteLine($"  Field:    {r.FieldName}");
                if (r.ExpectedValue != null) writer.WriteLine($"  Expected: {r.ExpectedValue}");
                if (r.ActualValue != null) writer.WriteLine($"  Actual:   {r.ActualValue}");
                writer.WriteLine($"  Message:  {r.Message}");
            }
        }
    }
}
