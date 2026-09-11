using System.Collections.Generic;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Core;

namespace ETABSModelDefinitionValidator.Rules
{
    /// <summary>MODAL-001: Ritz target dynamic mass participation ratio must be >= the configured threshold (default 95%).</summary>
    public sealed class ModalTargetRatioRule : IValidationRule
    {
        public string RuleId => RuleIds.Modal001;
        public string Name => "Modal Target Dynamic Mass Participation";
        public string Category => Categories.Modal;
        public RuleType Type => RuleType.ProjectStandard;
        public Severity DefaultSeverity => Severity.High;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.ModalCaseDefinitionsRitz };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (!model.WasTableFound(KnownTables.ModalCaseDefinitionsRitz))
            {
                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                    "Table", KnownTables.ModalCaseDefinitionsRitz, SourceLocation.Unknown,
                    message: "Required ETABS table was not found in the workbook. Rule could not be evaluated.");
                yield break;
            }

            var threshold = config.Modal.MinimumTargetRatioPercent;

            foreach (var modalCase in model.ModalRitzCases)
            {
                if (modalCase.TargetRatioPercent == null)
                {
                    yield return ValidationResult.Create(
                        RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                        "ModalRitzCase", $"{modalCase.Name} / {modalCase.LoadName}", modalCase.Source, fieldName: "TargetRatio",
                        message: $"No Target Ratio value present for load '{modalCase.LoadName}' in case '{modalCase.Name}'.");
                    continue;
                }

                var status = modalCase.TargetRatioPercent.Value >= threshold ? ValidationStatus.Pass : ValidationStatus.Fail;

                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, DefaultSeverity, status,
                    "ModalRitzCase", $"{modalCase.Name} / {modalCase.LoadName}", modalCase.Source, fieldName: "TargetRatio",
                    expected: $">= {threshold}%", actual: $"{modalCase.TargetRatioPercent.Value}%",
                    message: status == ValidationStatus.Pass
                        ? $"Target dynamic mass participation ratio {modalCase.TargetRatioPercent.Value}% meets the {threshold}% minimum."
                        : $"Target dynamic mass participation ratio {modalCase.TargetRatioPercent.Value}% is below the {threshold}% minimum for load '{modalCase.LoadName}'.");
            }
        }
    }
}
