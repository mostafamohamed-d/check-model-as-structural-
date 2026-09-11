namespace ETABSModelDefinitionValidator.Core.Validation
{
    /// <summary>
    /// How serious a FAIL/WARNING is, independent of Status. Two rules can both FAIL, but one
    /// (a code-required material strength) is Critical while another (a naming inconsistency)
    /// is Low - the report must be able to sort/filter on this separately from Status.
    /// </summary>
    public enum Severity
    {
        Low,
        Medium,
        High,
        Critical
    }
}
