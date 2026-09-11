namespace ETABSModelDefinitionValidator.Core.Validation
{
    /// <summary>The outcome of evaluating one rule against one object.</summary>
    public enum ValidationStatus
    {
        Pass,
        Warning,
        Fail,
        Exempt,

        /// <summary>The rule could not be evaluated - required table/field missing, or data
        /// too ambiguous to check reliably. Never silently reported as Pass.</summary>
        NotChecked
    }
}
