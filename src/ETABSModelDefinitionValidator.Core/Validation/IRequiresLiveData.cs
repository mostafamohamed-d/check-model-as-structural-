using System;

namespace ETABSModelDefinitionValidator.Core.Validation
{
    /// <summary>Data a rule needs that ETABS does not hand out as a database table, so a live
    /// provider has to read it through the object API.</summary>
    [Flags]
    public enum LiveData
    {
        None = 0,
        /// <summary>Frame distributed loads and area uniform loads.</summary>
        LoadAssignments = 1
    }

    /// <summary>Optional companion to <see cref="IValidationRule"/>: declares live-only data the rule reads,
    /// so a run only pays for those API calls when such a rule is enabled.</summary>
    public interface IRequiresLiveData
    {
        LiveData RequiredLiveData { get; }
    }
}
