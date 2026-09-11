using System.Collections.Generic;
using System.Linq;

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

        public ValidationStatus OverallStatus =>
            FailCount > 0 ? ValidationStatus.Fail :
            WarningCount > 0 ? ValidationStatus.Warning :
            ValidationStatus.Pass;

        public static ValidationSummary From(IReadOnlyList<ValidationResult> results)
        {
            var summary = new ValidationSummary { TotalCount = results.Count };

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
