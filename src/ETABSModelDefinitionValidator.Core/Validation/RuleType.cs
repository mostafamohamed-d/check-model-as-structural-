namespace ETABSModelDefinitionValidator.Core.Validation
{
    /// <summary>
    /// Classifies WHY a rule exists - critical distinction per the project brief: not every
    /// modelling convention is a code requirement. The reporting layer must surface this so an
    /// engineer can tell "the code demands this" apart from "our office standard demands this".
    /// </summary>
    public enum RuleType
    {
        Code,
        ProjectStandard,
        ModelingStandard,
        Consistency,
        ModelIntegrity,
        BestPractice
    }
}
