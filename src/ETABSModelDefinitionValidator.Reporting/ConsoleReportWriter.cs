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
        /// <summary>Results-only summary (cannot see import completeness). Prefer the ValidationSummary overload.</summary>
        public static void WriteSummary(IReadOnlyList<ValidationResult> results, TextWriter writer = null) =>
            WriteSummary(ValidationSummary.From(results), writer);

        public static void WriteSummary(ValidationSummary summary, TextWriter writer = null)
        {
            writer = writer ?? Console.Out;
            WriteVerdict(summary.Assessment, writer);
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

        /// <summary>The verdict, its scope and every reason that blocks a PASS.</summary>
        public static void WriteVerdict(RunAssessment a, TextWriter writer = null, int maxReasons = 25)
        {
            writer = writer ?? Console.Out;
            writer.WriteLine();
            writer.WriteLine($"RUN VERDICT: {RunAssessment.Label(a.Verdict)}   (exit code {a.ExitCode})");
            if (a.CategoriesTotal > 0)
            {
                writer.WriteLine($"Scope: {a.CategoriesRun} of {a.CategoriesTotal} check categories enabled" +
                                 (a.CategoriesRun < a.CategoriesTotal ? " - the verdict covers only these." : "."));
            }
            WriteReasons(writer, "Failed", a.FailReasons, maxReasons);
            WriteReasons(writer, "Incomplete - required evidence missing", a.IncompleteReasons, maxReasons);
            WriteReasons(writer, "Needs engineer review", a.ReviewReasons, maxReasons);
        }

        private static void WriteReasons(TextWriter writer, string heading, List<string> reasons, int max)
        {
            if (reasons.Count == 0) return;
            writer.WriteLine($"{heading}:");
            foreach (var r in reasons.Take(max)) writer.WriteLine("  - " + r);
            if (reasons.Count > max) writer.WriteLine($"  ... and {reasons.Count - max} more");
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
