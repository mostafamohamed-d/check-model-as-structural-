using System.Collections.Generic;

namespace ETABSModelDefinitionValidator.Core.Validation
{
    public sealed class ValidationSummary
    {
        public int PassCount { get; private set; }
        public int WarningCount { get; private set; }
        public int FailCount { get; private set; }
        public int ExemptCount { get; private set; }
        public int NotCheckedCount { get; private set; }
        public int TotalCount { get; private set; }

        public Dictionary<string, CategorySummary> ByCategory { get; } = new Dictionary<string, CategorySummary>();

        /// <summary>
        /// The run verdict and the reasons for it. Replaces the former OverallStatus, which returned
        /// PASS whenever there were no FAIL/WARNING results - even if checks had NOT_CHECKED results
        /// or required tables were missing. Use the overload with the model, enabled rules and profile
        /// so import completeness is part of the verdict.
        /// </summary>
        public RunAssessment Assessment { get; private set; }

        public RunVerdict Verdict => Assessment.Verdict;

        /// <summary>Results-only summary: still never PASS with NOT_CHECKED results or unaccepted exemptions,
        /// but blind to import state. Prefer the full overload for a run verdict.</summary>
        public static ValidationSummary From(IReadOnlyList<ValidationResult> results) => From(results, null, null, null);

        public static ValidationSummary From(
            IReadOnlyList<ValidationResult> results,
            Model.ModelData model,
            IEnumerable<IValidationRule> rulesRun,
            Configuration.ReportingRulesConfig policy)
        {
            var summary = new ValidationSummary { TotalCount = results.Count };
            summary.Assessment = RunAssessment.Evaluate(results, model, rulesRun, policy);

            foreach (var result in results)
            {
                switch (result.Status)
                {
                    case ValidationStatus.Pass: summary.PassCount++; break;
                    case ValidationStatus.Warning: summary.WarningCount++; break;
                    case ValidationStatus.Fail: summary.FailCount++; break;
                    case ValidationStatus.Exempt: summary.ExemptCount++; break;
                    case ValidationStatus.NotChecked: summary.NotCheckedCount++; break;
                }

                if (!summary.ByCategory.TryGetValue(result.Category, out var cat))
                {
                    cat = new CategorySummary { Category = result.Category };
                    summary.ByCategory[result.Category] = cat;
                }
                cat.Add(result.Status);
            }

            return summary;
        }
    }

    public sealed class CategorySummary
    {
        public string Category { get; set; }
        public int PassCount { get; private set; }
        public int WarningCount { get; private set; }
        public int FailCount { get; private set; }
        public int ExemptCount { get; private set; }
        public int NotCheckedCount { get; private set; }

        public void Add(ValidationStatus status)
        {
            switch (status)
            {
                case ValidationStatus.Pass: PassCount++; break;
                case ValidationStatus.Warning: WarningCount++; break;
                case ValidationStatus.Fail: FailCount++; break;
                case ValidationStatus.Exempt: ExemptCount++; break;
                case ValidationStatus.NotChecked: NotCheckedCount++; break;
            }
        }
    }
}
