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
                yield return RuleResults.TableNotUsable(this, KnownTables.MaterialPropertiesRebarData);
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
                yield return RuleResults.TableNotUsable(this, KnownTables.MaterialPropertiesConcreteData);
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
    /// MAT-CONC-002: concrete modulus of elasticity against ACI 318-19 19.2.2.1, BOTH forms:
    ///   (a) Ec = wc^1.5 * 0.043 * sqrt(fc')  for 1440 to 2560 kg/m3 (wc from the material's own unit mass/weight)
    ///   (b) Ec = 4700 * sqrt(fc')            normalweight concrete
    /// E1 passes when it is within tolerance of EITHER equation - the code permits both, and ETABS's
    /// own default concrete uses (a) with the material density. Only the modulus is checked: every
    /// other ETABS material field (creep, shrinkage, curve shape...) stays unchecked until a
    /// verified source is supplied - see master prompt §45.
    /// </summary>
    public sealed class ConcretePropertyRule : IValidationRule
    {
        /// <summary>Allowed difference between E1 and the code value, percent.</summary>
        public const double TolerancePercent = 5.0;

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
                yield return RuleResults.TableNotUsable(this, KnownTables.MaterialPropertiesConcreteData);
                yield break;
            }

            var isAci = config.CodeProfile != null && config.CodeProfile.StartsWith("ACI", StringComparison.OrdinalIgnoreCase);
            var codeBasis = new CodeReference { Code = "ACI 318-19", Edition = "2019", ChapterOrSection = "19.2.2.1(a) and (b)", IsVerified = true };

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

                if (!isAci)
                {
                    yield return ValidationResult.Create(
                        RuleId, Name, Category, Type, Severity.Low, ValidationStatus.NotChecked,
                        "ConcreteMaterial", material.Name, material.Source, fieldName: "E1",
                        message: $"No verified Ec formula is configured for code profile '{config.CodeProfile}'.",
                        codeBasis: CodeReference.ReferencePending(config.CodeProfile ?? "(unspecified)", "-"));
                    continue;
                }

                var fc = material.FcMpa;
                var actualE = material.MechanicalProperties.E1Mpa;
                var notes = new List<string>();

                // Eq. (a): needs the density, and only applies within its wc range.
                double? eqA = null;
                var hasWc = ConcreteModulus.TryGetDensity(material.MechanicalProperties, out var wc, out var how);
                if (!hasWc)
                {
                    notes.Add("Eq. (a) not evaluated: the material's unit mass/weight could not be read as a concrete density.");
                }
                else if (wc < ConcreteModulus.MinWcKgPerM3 || wc > ConcreteModulus.MaxWcKgPerM3)
                {
                    notes.Add($"Eq. (a) not applicable: wc = {wc:0} kg/m3 ({how}) is outside {ConcreteModulus.MinWcKgPerM3:0}-{ConcreteModulus.MaxWcKgPerM3:0} kg/m3.");
                }
                else
                {
                    eqA = ConcreteModulus.EquationA(wc, fc);
                }

                // Eq. (b): normalweight concrete only.
                double? eqB = null;
                if (material.IsLightweight) notes.Add("Eq. (b) not applicable: material is flagged lightweight.");
                else eqB = ConcreteModulus.EquationB(fc);

                if (eqA == null && eqB == null)
                {
                    yield return ValidationResult.Create(
                        RuleId, Name, Category, Type, Severity.Low, ValidationStatus.NotChecked,
                        "ConcreteMaterial", material.Name, material.Source, fieldName: "E1",
                        actual: $"{actualE:0} MPa",
                        message: $"Neither ACI 318-19 Ec equation could be applied to '{material.Name}'. " + string.Join(" ", notes),
                        codeBasis: codeBasis);
                    continue;
                }

                double Diff(double expected) => expected <= 0 ? double.PositiveInfinity : Math.Abs(actualE - expected) / expected * 100.0;
                var diffA = eqA.HasValue ? Diff(eqA.Value) : double.PositiveInfinity;
                var diffB = eqB.HasValue ? Diff(eqB.Value) : double.PositiveInfinity;
                var bestIsA = diffA <= diffB;
                var bestDiff = Math.Min(diffA, diffB);
                var status = bestDiff <= TolerancePercent ? ValidationStatus.Pass : ValidationStatus.Warning;

                var options = new List<string>();
                var comparison = new List<string>();
                if (eqA.HasValue)
                {
                    options.Add($"Eq.(a) {eqA.Value:0} MPa (wc {wc:0} kg/m3)");
                    comparison.Add($"Eq. (a) {eqA.Value:0} MPa, diff {diffA:0.0}%");
                }
                if (eqB.HasValue)
                {
                    options.Add($"Eq.(b) {eqB.Value:0} MPa");
                    comparison.Add($"Eq. (b) {eqB.Value:0} MPa, diff {diffB:0.0}%");
                }

                var matched = bestIsA
                    ? $"Eq. (a) wc^1.5 x 0.043 x sqrt(fc) with wc = {wc:0} kg/m3 ({eqA.Value:0} MPa)"
                    : $"Eq. (b) 4700 x sqrt(fc) ({eqB.Value:0} MPa)";

                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, DefaultSeverity, status,
                    "ConcreteMaterial", material.Name, material.Source, fieldName: "E1",
                    expected: string.Join(" or ", options) + $", +/-{TolerancePercent:0}%",
                    actual: $"{actualE:0} MPa",
                    message: status == ValidationStatus.Pass
                        ? $"E1 ({actualE:0} MPa) matches ACI 318-19 {matched} within {bestDiff:0.0}%."
                        : $"E1 ({actualE:0} MPa) differs from every applicable ACI 318-19 Ec equation by more than {TolerancePercent:0}% ({string.Join("; ", comparison)}) - verify this is an intentional project override.",
                    technicalExplanation: $"fc = {fc:0.##} MPa. {string.Join("; ", comparison)}." +
                                          (hasWc ? $" Density {how}." : "") +
                                          (notes.Count > 0 ? " " + string.Join(" ", notes) : ""),
                    codeBasis: codeBasis);
            }
        }
    }
}
