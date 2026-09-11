using ETABSModelDefinitionValidator.Core.Model;

namespace ETABSModelDefinitionValidator.Core.Validation
{
    /// <summary>
    /// One rule's verdict on one object. Every field needed for the detailed report view lives
    /// here so the reporting layer never has to reach back into the model or the rule.
    /// </summary>
    public sealed class ValidationResult
    {
        public string RuleId { get; set; }
        public string RuleName { get; set; }
        public string Category { get; set; }
        public RuleType RuleType { get; set; }
        public Severity Severity { get; set; }
        public ValidationStatus Status { get; set; }

        public string ObjectType { get; set; }
        public string ObjectName { get; set; }
        public string Story { get; set; }

        public SourceLocation Source { get; set; } = SourceLocation.Unknown;

        public string FieldName { get; set; }
        public string ExpectedValue { get; set; }
        public string ActualValue { get; set; }

        public string Message { get; set; }
        public string TechnicalExplanation { get; set; }

        public CodeReference CodeBasis { get; set; }

        public static ValidationResult Create(
            string ruleId, string ruleName, string category, RuleType ruleType, Severity severity,
            ValidationStatus status, string objectType, string objectName, SourceLocation source,
            string fieldName = null, string expected = null, string actual = null,
            string message = null, string technicalExplanation = null, string story = null,
            CodeReference codeBasis = null)
        {
            return new ValidationResult
            {
                RuleId = ruleId,
                RuleName = ruleName,
                Category = category,
                RuleType = ruleType,
                Severity = severity,
                Status = status,
                ObjectType = objectType,
                ObjectName = objectName,
                Story = story,
                Source = source ?? SourceLocation.Unknown,
                FieldName = fieldName,
                ExpectedValue = expected,
                ActualValue = actual,
                Message = message,
                TechnicalExplanation = technicalExplanation,
                CodeBasis = codeBasis
            };
        }
    }
}
