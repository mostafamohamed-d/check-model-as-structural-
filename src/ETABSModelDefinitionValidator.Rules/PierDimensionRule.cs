using System;
using System.Collections.Generic;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Core;

namespace ETABSModelDefinitionValidator.Rules
{
    /// <summary>PIER-001/002: pier width and thickness must be equal top to bottom (no unintended taper).</summary>
    public sealed class PierDimensionRule : IValidationRule
    {
        public string RuleId => RuleIds.Pier001;
        public string Name => "Pier Top/Bottom Dimensions Equal";
        public string Category => Categories.PierProperties;
        public RuleType Type => RuleType.ModelIntegrity;
        public Severity DefaultSeverity => Severity.High;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.PierSectionProperties };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (!model.WasTableFound(KnownTables.PierSectionProperties))
            {
                yield return ValidationResult.Create(
                    RuleIds.Pier001, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                    "Table", KnownTables.PierSectionProperties, SourceLocation.Unknown,
                    message: "Required ETABS table was not found in the workbook. Rule could not be evaluated.");
                yield break;
            }

            foreach (var pier in model.PierProperties)
            {
                var widthDiff = pier.WidthTopMm - pier.WidthBottomMm;
                var widthStatus = Math.Abs(widthDiff) < 0.01 ? ValidationStatus.Pass : ValidationStatus.Fail;

                yield return ValidationResult.Create(
                    RuleIds.Pier001, Name, Category, Type, DefaultSeverity, widthStatus,
                    "PierProperty", pier.Pier, pier.Source, fieldName: "Width", story: pier.Story,
                    expected: $"{pier.WidthBottomMm} mm (Width Bottom)", actual: $"{pier.WidthTopMm} mm (Width Top)",
                    message: widthStatus == ValidationStatus.Pass
                        ? "Pier width is consistent top to bottom."
                        : $"Pier width differs top to bottom: Bottom={pier.WidthBottomMm} mm, Top={pier.WidthTopMm} mm, difference {(widthDiff > 0 ? "+" : "")}{widthDiff} mm.");

                var thickDiff = pier.ThicknessTopMm - pier.ThicknessBottomMm;
                var thickStatus = Math.Abs(thickDiff) < 0.01 ? ValidationStatus.Pass : ValidationStatus.Fail;

                yield return ValidationResult.Create(
                    RuleIds.Pier002, "Pier Top/Bottom Thickness Equal", Category, Type, DefaultSeverity, thickStatus,
                    "PierProperty", pier.Pier, pier.Source, fieldName: "Thickness", story: pier.Story,
                    expected: $"{pier.ThicknessBottomMm} mm (Thickness Bottom)", actual: $"{pier.ThicknessTopMm} mm (Thickness Top)",
                    message: thickStatus == ValidationStatus.Pass
                        ? "Pier thickness is consistent top to bottom."
                        : $"Pier thickness differs top to bottom: Bottom={pier.ThicknessBottomMm} mm, Top={pier.ThicknessTopMm} mm, difference {(thickDiff > 0 ? "+" : "")}{thickDiff} mm.");
            }
        }
    }
}
