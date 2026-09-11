using System;
using System.Collections.Generic;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Core;

namespace ETABSModelDefinitionValidator.Rules
{
    /// <summary>DIA-001: every diaphragm definition must use the required rigidity type (default Semi-Rigid). Checks every diaphragm found, not a fixed known-name list.</summary>
    public sealed class DiaphragmRigidityRule : IValidationRule
    {
        public string RuleId => RuleIds.Dia001;
        public string Name => "Diaphragm Rigidity";
        public string Category => Categories.Diaphragms;
        public RuleType Type => RuleType.ModelingStandard;
        public Severity DefaultSeverity => Severity.High;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.DiaphragmDefinitions };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (!model.WasTableFound(KnownTables.DiaphragmDefinitions))
            {
                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                    "Table", KnownTables.DiaphragmDefinitions, SourceLocation.Unknown,
                    message: "Required ETABS table was not found in the workbook. Rule could not be evaluated.");
                yield break;
            }

            var expected = config.Diaphragm.RequiredRigidityType;

            foreach (var diaphragm in model.DiaphragmDefinitions)
            {
                var status = string.Equals(diaphragm.RigidityType, expected, StringComparison.OrdinalIgnoreCase)
                    ? ValidationStatus.Pass
                    : ValidationStatus.Fail;

                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, DefaultSeverity, status,
                    "Diaphragm", diaphragm.Name, diaphragm.Source, fieldName: "RigidityType",
                    expected: expected, actual: diaphragm.RigidityType,
                    message: status == ValidationStatus.Pass
                        ? $"Diaphragm '{diaphragm.Name}' is {expected} as required."
                        : $"Diaphragm '{diaphragm.Name}' is '{diaphragm.RigidityType}', expected '{expected}'.");
            }
        }
    }
}
