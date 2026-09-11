using System;
using System.Collections.Generic;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Core;

namespace ETABSModelDefinitionValidator.Rules
{
    /// <summary>COL-001: concrete column design section must be "Program Determined".</summary>
    public sealed class ColumnDesignSectionRule : IValidationRule
    {
        public string RuleId => RuleIds.Col001;
        public string Name => "Column Design Section";
        public string Category => Categories.DesignOverwrites;
        public RuleType Type => RuleType.ProjectStandard;
        public Severity DefaultSeverity => Severity.Medium;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.ConcreteColumnOverwritesAci31819 };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (!model.WasTableFound(KnownTables.ConcreteColumnOverwritesAci31819))
            {
                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                    "Table", KnownTables.ConcreteColumnOverwritesAci31819, SourceLocation.Unknown,
                    message: "Required ETABS table was not found in the workbook. Rule could not be evaluated.");
                yield break;
            }

            var expected = config.ColumnOverwrites.RequiredDesignSection;

            foreach (var column in model.ColumnDesignOverwrites)
            {
                var status = string.Equals(column.DesignSection, expected, StringComparison.OrdinalIgnoreCase)
                    ? ValidationStatus.Pass
                    : ValidationStatus.Fail;

                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, DefaultSeverity, status,
                    "Column", column.Label, column.Source, fieldName: "DesignSection", story: column.Story,
                    expected: expected, actual: column.DesignSection,
                    message: status == ValidationStatus.Pass
                        ? $"Column '{column.Label}' design section is '{expected}' as required."
                        : $"Column '{column.Label}' on story '{column.Story}' has design section '{column.DesignSection}', expected '{expected}'.");
            }
        }
    }
}
