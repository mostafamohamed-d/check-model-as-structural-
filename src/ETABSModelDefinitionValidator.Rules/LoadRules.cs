using System;
using System.Collections.Generic;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Core;

namespace ETABSModelDefinitionValidator.Rules
{
    /// <summary>LOAD-001: the Dead load pattern's Self Weight Multiplier must be 1.0. Other
    /// pattern types are intentionally not checked here (no defined project rule for them).</summary>
    public sealed class DeadSelfWeightRule : IValidationRule
    {
        public string RuleId => RuleIds.Load001;
        public string Name => "Dead Load Self-Weight Multiplier";
        public string Category => Categories.Loads;
        public RuleType Type => RuleType.Code;
        public Severity DefaultSeverity => Severity.Critical;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.LoadPatternDefinitions };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (!model.WasTableFound(KnownTables.LoadPatternDefinitions))
            {
                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                    "Table", KnownTables.LoadPatternDefinitions, SourceLocation.Unknown,
                    message: "Required ETABS table was not found in the workbook. Rule could not be evaluated.");
                yield break;
            }

            var expected = config.Loads.RequiredSelfWeightMultiplier;
            var codeBasis = new CodeReference { Code = "Physical requirement", Edition = "-", ChapterOrSection = "Self-weight must equal 1x true weight", IsVerified = true };

            foreach (var pattern in model.LoadPatterns)
            {
                if (!string.Equals(pattern.Type, config.Loads.DeadPatternTypeName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var status = Math.Abs(pattern.SelfWeightMultiplier - expected) < 0.001 ? ValidationStatus.Pass : ValidationStatus.Fail;

                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, DefaultSeverity, status,
                    "LoadPattern", pattern.Name, pattern.Source, fieldName: "SelfWeightMultiplier",
                    expected: expected.ToString("0.0"), actual: pattern.SelfWeightMultiplier.ToString("0.0"),
                    message: status == ValidationStatus.Pass
                        ? $"Dead load pattern '{pattern.Name}' has the required self-weight multiplier of {expected}."
                        : $"Dead load pattern '{pattern.Name}' has self-weight multiplier {pattern.SelfWeightMultiplier}, expected {expected}.",
                    codeBasis: codeBasis);
            }
        }
    }

    /// <summary>LOAD-002: for linear-static load cases, the referenced Load Name should equal
    /// the case Name exactly. A mismatch is a WARNING, not a FAIL, per explicit project
    /// direction - per the spec's own example, case "EX" with Load Name "EX-GF" (a suffixed
    /// variant) still WARNs even though it shares the "EX" prefix; only an exact match PASSes.</summary>
    public sealed class LoadCaseConsistencyRule : IValidationRule
    {
        public string RuleId => RuleIds.Load002;
        public string Name => "Load Case Name Consistency";
        public string Category => Categories.Loads;
        public RuleType Type => RuleType.Consistency;
        public Severity DefaultSeverity => Severity.Low;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.LoadCaseDefinitionsLinearStatic };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (!model.WasTableFound(KnownTables.LoadCaseDefinitionsLinearStatic))
            {
                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                    "Table", KnownTables.LoadCaseDefinitionsLinearStatic, SourceLocation.Unknown,
                    message: "Required ETABS table was not found in the workbook. Rule could not be evaluated.");
                yield break;
            }

            foreach (var loadCase in model.LoadCasesLinearStatic)
            {
                if (string.IsNullOrEmpty(loadCase.LoadName))
                {
                    continue;
                }

                var matches = string.Equals(loadCase.LoadName, loadCase.Name, StringComparison.OrdinalIgnoreCase);
                var status = matches ? ValidationStatus.Pass : ValidationStatus.Warning;

                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, DefaultSeverity, status,
                    "LoadCase", loadCase.Name, loadCase.Source, fieldName: "LoadName",
                    expected: loadCase.Name, actual: loadCase.LoadName,
                    message: matches
                        ? $"Load case '{loadCase.Name}' references load name '{loadCase.LoadName}', matching the case name."
                        : $"Load case '{loadCase.Name}' references load name '{loadCase.LoadName}', which does not exactly match the case name - verify this is intentional.");
            }
        }
    }
}
