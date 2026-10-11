using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.Rules
{
    /// <summary>Result builders shared by every rule.</summary>
    internal static class RuleResults
    {
        /// <summary>NOT_CHECKED because a table the rule needs is not usable. The engine replaces the
        /// message with the precise import reason (missing, schema invalid, failed...) when it knows it.</summary>
        public static ValidationResult TableNotUsable(IValidationRule rule, string table) => ValidationResult.Create(
            rule.RuleId, rule.Name, rule.Category, rule.Type, Severity.Medium, ValidationStatus.NotChecked,
            "Table", table, SourceLocation.Unknown,
            message: $"Required ETABS table '{table}' was not found. Rule could not be evaluated.");
    }
}
