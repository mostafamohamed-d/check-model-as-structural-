using System.Collections.Generic;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;

namespace ETABSModelDefinitionValidator.Core.Validation
{
    /// <summary>
    /// One independently testable engineering check. Implementations must not read Excel or
    /// any other source directly - they see only the normalized ModelData, so the exact same
    /// rule can later run against a live ETABS-API-sourced model with no changes.
    /// </summary>
    public interface IValidationRule
    {
        string RuleId { get; }
        string Name { get; }
        string Category { get; }
        RuleType Type { get; }
        Severity DefaultSeverity { get; }

        /// <summary>Applicable normalized table(s), for documentation/UI filtering - not used for dispatch.</summary>
        IReadOnlyList<string> ApplicableTables { get; }

        IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config);
    }
}
