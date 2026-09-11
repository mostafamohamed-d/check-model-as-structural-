using System;
using System.Collections.Generic;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Naming;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Core;

namespace ETABSModelDefinitionValidator.Rules
{
    /// <summary>
    /// SLAB-001/002/003: m11/m22/m12 modifiers must match the profile for the slab's
    /// classification (PT, Ordinary, Stair, Ramp, Raft - see SlabPropertyNameParser, which is
    /// the single source of classification logic shared across all three rule IDs).
    /// </summary>
    public sealed class SlabModifierRule : IValidationRule
    {
        private readonly SlabPropertyNameParser _parser = new SlabPropertyNameParser();

        /// <summary>Identifies this rule in the registry/catalog. Individual results carry the
        /// classification-specific ID (SLAB-001/002/003/004) - see ClassificationToRuleId below.</summary>
        public string RuleId => RuleIds.Slab001;
        public string Name => "Slab Modifiers";
        public string Category => Categories.SlabProperties;
        public RuleType Type => RuleType.ProjectStandard;
        public Severity DefaultSeverity => Severity.Medium;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.SlabPropertyDefinitions };

        private static readonly Dictionary<string, string> ClassificationToRuleId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PT"] = RuleIds.Slab001,
            ["Ordinary"] = RuleIds.Slab002,
            ["Stair"] = RuleIds.Slab003,
            ["Ramp"] = RuleIds.Slab003,
            ["Raft"] = "SLAB-004" // not in the original 19, added for the Raft classification gap - see docs/ValidationMatrix.md
        };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (!model.WasTableFound(KnownTables.SlabPropertyDefinitions))
            {
                yield return ValidationResult.Create(
                    RuleIds.Slab001, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                    "Table", KnownTables.SlabPropertyDefinitions, SourceLocation.Unknown,
                    message: "Required ETABS table was not found in the workbook. Rule could not be evaluated.");
                yield break;
            }

            foreach (var slab in model.SlabProperties)
            {
                var parsed = _parser.Parse(slab.Name, config.Slabs);
                if (!parsed.Success)
                {
                    yield return ValidationResult.Create(
                        RuleIds.Slab002, Name, Category, Type, Severity.Low, ValidationStatus.Warning,
                        "SlabProperty", slab.Name, slab.Source, fieldName: "Name",
                        message: $"Slab property name '{slab.Name}' could not be classified: {parsed.FailureReason}");
                    continue;
                }

                if (!config.Slabs.Classifications.TryGetValue(parsed.Classification, out var profile))
                {
                    continue;
                }

                var ruleId = ClassificationToRuleId.TryGetValue(parsed.Classification, out var id) ? id : RuleIds.Slab002;
                var ruleType = parsed.Classification.Equals("Raft", StringComparison.OrdinalIgnoreCase)
                    ? RuleType.ModelingStandard
                    : Type;

                foreach (var (fieldName, actual, expected) in new[]
                         {
                             ("M11 Modifier", slab.M11Modifier, profile.M11),
                             ("M22 Modifier", slab.M22Modifier, profile.M22),
                             ("M12 Modifier", slab.M12Modifier, profile.M12)
                         })
                {
                    var status = Math.Abs(actual - expected) < 0.001 ? ValidationStatus.Pass : ValidationStatus.Fail;

                    yield return ValidationResult.Create(
                        ruleId, Name, Category, ruleType, DefaultSeverity, status,
                        "SlabProperty", slab.Name, slab.Source, fieldName: fieldName,
                        expected: expected.ToString("0.00"), actual: actual.ToString("0.00"),
                        message: status == ValidationStatus.Pass
                            ? $"{fieldName} matches the {parsed.Classification} slab standard ({expected})."
                            : $"{fieldName} is {actual}, expected {expected} for a {parsed.Classification} slab.");
                }
            }
        }
    }
}
