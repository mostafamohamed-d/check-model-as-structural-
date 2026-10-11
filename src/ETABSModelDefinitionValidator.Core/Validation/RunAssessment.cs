using System;
using System.Collections.Generic;
using System.Linq;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;

namespace ETABSModelDefinitionValidator.Core.Validation
{
    /// <summary>Verdict for a whole run, worst first. Distinct from the per-result ValidationStatus.</summary>
    public enum RunVerdict
    {
        /// <summary>Every required check ran, nothing failed, no warnings, no exemptions/empties needing review.</summary>
        Pass,
        /// <summary>As Pass, but with warnings below the review severity.</summary>
        PassWithWarnings,
        /// <summary>Everything ran and nothing failed, but an engineer must decide: high-severity warnings,
        /// exemptions, or required tables read with zero records.</summary>
        ReviewRequired,
        /// <summary>Some required evidence is missing: a check did not run (NOT_CHECKED), a required table is
        /// missing/unusable/partial, or the import reported an error. Cannot pass.</summary>
        Incomplete,
        /// <summary>At least one check failed.</summary>
        Fail
    }

    /// <summary>
    /// Rolls results + import state + profile policy into one verdict with the reasons behind it.
    /// PASS requires ALL of: every required check completed, no FAIL, no unresolved warning at or above
    /// the review severity, no unaccepted exemption, no required table missing/unusable/partial/empty,
    /// no import error. Precedence: Fail &gt; Incomplete &gt; ReviewRequired &gt; PassWithWarnings &gt; Pass;
    /// the reasons list keeps every blocking item, so a failed run still shows that it was also incomplete.
    /// </summary>
    public sealed class RunAssessment
    {
        public RunVerdict Verdict { get; private set; }
        public List<string> FailReasons { get; } = new List<string>();
        public List<string> IncompleteReasons { get; } = new List<string>();
        public List<string> ReviewReasons { get; } = new List<string>();

        /// <summary>Categories enabled for the run / registered in total (scope shown with the verdict). 0 = unknown.</summary>
        public int CategoriesRun { get; set; }
        public int CategoriesTotal { get; set; }

        public int ExitCode => ExitCodeFor(Verdict);

        /// <summary>Process exit code contract (console runner). 1 is reserved for "the run itself errored".</summary>
        public static int ExitCodeFor(RunVerdict verdict)
        {
            switch (verdict)
            {
                case RunVerdict.Pass:
                case RunVerdict.PassWithWarnings: return 0;
                case RunVerdict.Fail: return 2;
                case RunVerdict.Incomplete: return 3;
                case RunVerdict.ReviewRequired: return 4;
                default: return 3;
            }
        }

        public static string Label(RunVerdict verdict)
        {
            switch (verdict)
            {
                case RunVerdict.Pass: return "PASS";
                case RunVerdict.PassWithWarnings: return "PASS WITH WARNINGS";
                case RunVerdict.ReviewRequired: return "REVIEW REQUIRED";
                case RunVerdict.Incomplete: return "INCOMPLETE";
                default: return "FAIL";
            }
        }

        /// <param name="results">All results of the run.</param>
        /// <param name="model">Import outcome (table states, import issues). Null = results-only assessment.</param>
        /// <param name="rulesRun">The enabled rules; their ApplicableTables are the run's required tables. Null = unknown.</param>
        /// <param name="policy">Profile reporting policy; null = strict defaults.</param>
        public static RunAssessment Evaluate(
            IReadOnlyList<ValidationResult> results,
            ModelData model = null,
            IEnumerable<IValidationRule> rulesRun = null,
            ReportingRulesConfig policy = null)
        {
            policy = policy ?? new ReportingRulesConfig();
            results = results ?? new List<ValidationResult>();
            var a = new RunAssessment();
            var rules = rulesRun?.ToList();
            var reviewSeverity = ParseSeverity(policy.ReviewSeverity);
            var optional = new HashSet<string>(policy.OptionalRules ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var allowedEmpty = new HashSet<string>(policy.AllowedEmptyTables ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            // --- FAIL ---------------------------------------------------------------------------------
            foreach (var g in results.Where(r => r.Status == ValidationStatus.Fail).GroupBy(r => r.RuleId).OrderBy(g => g.Key))
            {
                a.FailReasons.Add($"{g.Key}: {g.Count()} failed item(s).");
            }

            // --- INCOMPLETE: nothing ran, checks not evaluated, unusable/partial tables, import errors --
            if (results.Count == 0)
            {
                a.IncompleteReasons.Add("The run produced no results - nothing was checked.");
            }

            foreach (var g in results.Where(r => r.Status == ValidationStatus.NotChecked && !optional.Contains(r.RuleId))
                                     .GroupBy(r => r.RuleId).OrderBy(g => g.Key))
            {
                var first = g.First().Message;
                // Usable tables with nothing applicable is not missing evidence, but it is not proof either.
                if (g.All(r => r.FieldName == ValidationEngine.NoApplicableObjects))
                    a.ReviewReasons.Add($"{g.Key}: no applicable objects in the source - confirm the model really has none.");
                else
                    a.IncompleteReasons.Add($"{g.Key}: {g.Count()} item(s) NOT CHECKED - {first}");
            }

            if (model != null)
            {
                var required = rules?.SelectMany(r => r.ApplicableTables ?? Array.Empty<string>())
                                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                               ?? model.TableReports.Values.Where(t => t.State != TableImportState.Skipped && t.State != TableImportState.Unrecognized)
                                    .Select(t => t.Title).ToList();

                foreach (var title in required.OrderBy(t => t))
                {
                    var report = model.GetTableReport(title);
                    if (report == null)
                    {
                        if (model.MissingTables.Contains(title)) a.IncompleteReasons.Add($"Table '{title}': not found in the source.");
                        continue;
                    }
                    if (report.IsIncomplete)
                    {
                        a.IncompleteReasons.Add($"Table '{title}': {report.State}" +
                                                (report.RecordCount > 0 ? $" ({report.AcceptedCount} of {report.RecordCount} record(s) accepted)" : "") +
                                                (report.MissingColumns.Count > 0 ? $", missing column(s) {string.Join(", ", report.MissingColumns)}" : "") + ".");
                    }
                    else if (report.State == TableImportState.Skipped)
                    {
                        a.IncompleteReasons.Add($"Table '{title}': needed by an enabled check but not read.");
                    }
                    else if (report.State == TableImportState.ReadEmpty && !allowedEmpty.Contains(title))
                    {
                        a.ReviewReasons.Add($"Table '{title}' was read with 0 records - confirm the model really has none (checks on it had nothing to evaluate).");
                    }
                }

                foreach (var issue in model.ImportIssues.Where(i => i.Level == ImportIssueLevel.Error))
                {
                    a.IncompleteReasons.Add("Import error: " + issue.Message);
                }
            }

            // --- REVIEW: high-severity warnings, exemptions ---------------------------------------------
            foreach (var g in results.Where(r => r.Status == ValidationStatus.Warning && r.Severity >= reviewSeverity)
                                     .GroupBy(r => r.RuleId).OrderBy(g => g.Key))
            {
                a.ReviewReasons.Add($"{g.Key}: {g.Count()} warning(s) at severity {reviewSeverity} or above need an engineer's decision.");
            }

            if (!policy.AcceptExemptions)
            {
                foreach (var g in results.Where(r => r.Status == ValidationStatus.Exempt).GroupBy(r => r.RuleId).OrderBy(g => g.Key))
                {
                    a.ReviewReasons.Add($"{g.Key}: {g.Count()} exempt item(s) were not checked automatically and need manual sign-off.");
                }
            }

            a.Verdict =
                a.FailReasons.Count > 0 ? RunVerdict.Fail :
                a.IncompleteReasons.Count > 0 ? RunVerdict.Incomplete :
                a.ReviewReasons.Count > 0 ? RunVerdict.ReviewRequired :
                results.Any(r => r.Status == ValidationStatus.Warning) ? RunVerdict.PassWithWarnings :
                RunVerdict.Pass;
            return a;
        }

        /// <summary>Parses a profile severity name; unknown values fall back to the strictest useful setting (Low = every warning needs review).
        /// ConfigurationValidator rejects unknown names before a run, so the fallback is only a safety net.</summary>
        public static Severity ParseSeverity(string name) =>
            Enum.TryParse(name, true, out Severity s) && Enum.IsDefined(typeof(Severity), s) ? s : Severity.Low;
    }
}
