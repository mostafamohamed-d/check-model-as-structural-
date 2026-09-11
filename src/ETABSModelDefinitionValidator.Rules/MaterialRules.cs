using System;
using System.Collections.Generic;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Naming;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Core;

namespace ETABSModelDefinitionValidator.Rules
{
    /// <summary>MAT-REBAR-001: rebar Fy/Fu/Fye/Fue must match the configured grade profile for the material name.</summary>
    public sealed class RebarMaterialRule : IValidationRule
    {
        public string RuleId => RuleIds.MatRebar001;
        public string Name => "Rebar Material Strength Properties";
        public string Category => Categories.Materials;
        public RuleType Type => RuleType.ProjectStandard;
        public Severity DefaultSeverity => Severity.Critical;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.MaterialPropertiesRebarData };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (!model.WasTableFound(KnownTables.MaterialPropertiesRebarData))
            {
                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                    "Table", KnownTables.MaterialPropertiesRebarData, SourceLocation.Unknown,
                    message: "Required ETABS table was not found in the workbook. Rule could not be evaluated.");
                yield break;
            }

            var tolerance = config.RebarMaterials.ToleranceMpa;

            foreach (var rebar in model.RebarMaterials)
            {
                if (!config.RebarMaterials.GradeProfiles.TryGetValue(rebar.Name, out var profile))
                {
                    yield return ValidationResult.Create(
                        RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                        "RebarMaterial", rebar.Name, rebar.Source,
                        message: $"No reference grade profile is configured for rebar material '{rebar.Name}'. Add it to RebarMaterials.GradeProfiles to enable this check.");
                    continue;
                }

                foreach (var (field, actual, expected) in new[]
                         {
                             ("Fy", rebar.FyMpa, profile.FyMpa),
                             ("Fu", rebar.FuMpa, profile.FuMpa),
                             ("Fye", rebar.FyeMpa, profile.FyeMpa),
                             ("Fue", rebar.FueMpa, profile.FueMpa)
                         })
                {
                    var status = Math.Abs(actual - expected) < tolerance ? ValidationStatus.Pass : ValidationStatus.Fail;

                    yield return ValidationResult.Create(
                        RuleId, Name, Category, Type, DefaultSeverity, status,
                        "RebarMaterial", rebar.Name, rebar.Source, fieldName: field,
                        expected: $"{expected} MPa", actual: $"{actual} MPa",
                        message: status == ValidationStatus.Pass
                            ? $"{field} matches the reference grade profile ({expected} MPa)."
                            : $"{field} is {actual} MPa, expected {expected} MPa per the reference grade profile for '{rebar.Name}'.");
                }
            }
        }
    }

    /// <summary>MAT-CONC-001: the Fc value encoded in the concrete material name must match the defined Fc.</summary>
    public sealed class ConcreteFcRule : IValidationRule
    {
        private readonly ConcreteMaterialNameParser _parser = new ConcreteMaterialNameParser();

        public string RuleId => RuleIds.MatConc001;
        public string Name => "Concrete Fc Matches Material Name";
        public string Category => Categories.Materials;
        public RuleType Type => RuleType.Consistency;
        public Severity DefaultSeverity => Severity.Critical;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.MaterialPropertiesConcreteData };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (!model.WasTableFound(KnownTables.MaterialPropertiesConcreteData))
            {
                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                    "Table", KnownTables.MaterialPropertiesConcreteData, SourceLocation.Unknown,
                    message: "Required ETABS table was not found in the workbook. Rule could not be evaluated.");
                yield break;
            }

            foreach (var material in model.ConcreteMaterials)
            {
                var parsed = _parser.Parse(material.Name);
                if (!parsed.Success)
                {
                    yield return ValidationResult.Create(
                        RuleId, Name, Category, Type, Severity.Low, ValidationStatus.Warning,
                        "ConcreteMaterial", material.Name, material.Source, fieldName: "Name",
                        message: $"Material name '{material.Name}' could not be parsed for Fc verification: {parsed.FailureReason}");
                    continue;
                }

                var status = Math.Abs(material.FcMpa - parsed.FcMpa) < config.ConcreteMaterials.FcToleranceMpa
                    ? ValidationStatus.Pass
                    : ValidationStatus.Fail;

                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, DefaultSeverity, status,
                    "ConcreteMaterial", material.Name, material.Source, fieldName: "Fc",
                    expected: $"{parsed.FcMpa} MPa (from name)", actual: $"{material.FcMpa} MPa",
                    message: status == ValidationStatus.Pass
                        ? "Fc matches the grade encoded in the material name."
                        : $"Material name implies Fc={parsed.FcMpa} MPa, but defined Fc={material.FcMpa} MPa.");
            }
        }
    }

    /// <summary>
    /// MAT-CONC-002: broader concrete property checks. v1 implements only the one property with
    /// a verifiable, code-derived closed-form relationship (modulus of elasticity per ACI 318-19
    /// Eq. 19.2.2.1.b, Ec = 4700*sqrt(Fc') for normalweight concrete) so it is not fabricating a
    /// requirement. Every other ETABS material field (creep, shrinkage, stress-strain curve
    /// shape, etc.) is intentionally NOT_CHECKED / REFERENCE_PENDING until a verified source
    /// (code clause or confirmed project standard) is supplied - see master prompt §45.
    /// </summary>
    public sealed class ConcretePropertyRule : IValidationRule
    {
        public string RuleId => RuleIds.MatConc002;
        public string Name => "Concrete Material Properties";
        public string Category => Categories.Materials;
        public RuleType Type => RuleType.Code;
        public Severity DefaultSeverity => Severity.Medium;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.MaterialPropertiesConcreteData, KnownTables.MaterialPropertiesBasicMechanicalProperties };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (!model.WasTableFound(KnownTables.MaterialPropertiesConcreteData))
            {
                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                    "Table", KnownTables.MaterialPropertiesConcreteData, SourceLocation.Unknown,
                    message: "Required ETABS table was not found in the workbook. Rule could not be evaluated.");
                yield break;
            }

            var isAci = config.CodeProfile != null && config.CodeProfile.StartsWith("ACI", StringComparison.OrdinalIgnoreCase);
            var codeBasis = isAci
                ? new CodeReference { Code = "ACI 318-19", Edition = "2019", ChapterOrSection = "19.2.2.1(b)", IsVerified = true }
                : CodeReference.ReferencePending(config.CodeProfile ?? "(unspecified)", "-");

            foreach (var material in model.ConcreteMaterials)
            {
                if (material.MechanicalProperties == null)
                {
                    yield return ValidationResult.Create(
                        RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                        "ConcreteMaterial", material.Name, material.Source, fieldName: "E1",
                        message: $"No Basic Mechanical Properties row found for material '{material.Name}'; E1 could not be checked.");
                    continue;
                }

                if (!isAci || material.IsLightweight)
                {
                    yield return ValidationResult.Create(
                        RuleId, Name, Category, Type, Severity.Low, ValidationStatus.NotChecked,
                        "ConcreteMaterial", material.Name, material.Source, fieldName: "E1",
                        message: material.IsLightweight
                            ? $"'{material.Name}' is lightweight concrete; the normalweight Ec formula does not apply and no lightweight formula is configured yet."
                            : $"No verified Ec formula is configured for code profile '{config.CodeProfile}'.",
                        codeBasis: CodeReference.ReferencePending(config.CodeProfile ?? "(unspecified)", "-"));
                    continue;
                }

                var expectedE = 4700.0 * Math.Sqrt(material.FcMpa);
                var actualE = material.MechanicalProperties.E1Mpa;
                var percentDiff = expectedE == 0 ? 0 : Math.Abs(actualE - expectedE) / expectedE * 100.0;
                var status = percentDiff <= 5.0 ? ValidationStatus.Pass : ValidationStatus.Warning;

                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, DefaultSeverity, status,
                    "ConcreteMaterial", material.Name, material.Source, fieldName: "E1",
                    expected: $"{expectedE:0} MPa (ACI 318-19 Ec = 4700*sqrt(Fc'))", actual: $"{actualE:0} MPa",
                    message: status == ValidationStatus.Pass
                        ? $"E1 ({actualE:0} MPa) is within 5% of the ACI 318-19 normalweight Ec formula ({expectedE:0} MPa)."
                        : $"E1 ({actualE:0} MPa) differs from the ACI 318-19 normalweight Ec formula ({expectedE:0} MPa) by {percentDiff:0.0}% - verify this is an intentional project override.",
                    codeBasis: codeBasis);
            }
        }
    }
}
