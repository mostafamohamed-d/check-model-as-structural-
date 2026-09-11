using System;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    /// <summary>Current filter criteria for the results grid. Null fields mean "no restriction
    /// on that field". Kept as a plain value holder so filtering is a cheap LINQ pass over the
    /// in-memory result list, not tied to any grid-control API.</summary>
    public sealed class ResultFilter
    {
        public ValidationStatus? Status { get; set; }
        public string Category { get; set; }
        public Severity? Severity { get; set; }
        public string SearchText { get; set; }

        public bool Matches(ValidationResult result)
        {
            if (Status.HasValue && result.Status != Status.Value) return false;
            if (!string.IsNullOrEmpty(Category) && !string.Equals(result.Category, Category, StringComparison.OrdinalIgnoreCase)) return false;
            if (Severity.HasValue && result.Severity != Severity.Value) return false;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var needle = SearchText.Trim();
                var haystack = $"{result.RuleId} {result.ObjectName} {result.Message} {result.Story}";
                if (haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }

            return true;
        }
    }
}
