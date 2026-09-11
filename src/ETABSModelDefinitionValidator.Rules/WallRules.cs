using System;
using System.Collections.Generic;
using System.Linq;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Naming;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Core;

namespace ETABSModelDefinitionValidator.Rules
{
    /// <summary>WALL-001: the thickness encoded in the wall property name must match the defined thickness.</summary>
    public sealed class WallThicknessRule : IValidationRule
    {
        private readonly WallPropertyNameParser _parser = new WallPropertyNameParser();

        public string RuleId => RuleIds.Wall001;
        public string Name => "Wall Thickness Matches Name";
        public string Category => Categories.WallProperties;
        public RuleType Type => RuleType.Consistency;
        public Severity DefaultSeverity => Severity.High;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.WallPropertyDefinitionsSpecified };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (!model.WasTableFound(KnownTables.WallPropertyDefinitionsSpecified))
            {
                yield return NotChecked(model);
                yield break;
            }

            foreach (var wall in model.WallProperties)
            {
                var parsed = _parser.Parse(wall.Name);
                if (!parsed.Success)
                {
                    yield return ValidationResult.Create(
                        RuleId, Name, Category, Type, Severity.Low, ValidationStatus.Warning,
                        "WallProperty", wall.Name, wall.Source, fieldName: "Name",
                        message: $"Wall property name '{wall.Name}' could not be parsed for thickness verification: {parsed.FailureReason}",
                        technicalExplanation: "Name-based checks are skipped, not failed, when the naming convention doesn't match - see docs/ValidationMatrix.md naming convention section.");
                    continue;
                }

                var diff = wall.ThicknessMm - parsed.ThicknessMm;
                var status = Math.Abs(diff) < 0.01 ? ValidationStatus.Pass : ValidationStatus.Fail;

                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, DefaultSeverity, status,
                    "WallProperty", wall.Name, wall.Source, fieldName: "WallThickness",
                    expected: $"{parsed.ThicknessMm} mm (from name)", actual: $"{wall.ThicknessMm} mm",
                    message: status == ValidationStatus.Pass
                        ? "Wall thickness matches the thickness encoded in the property name."
                        : $"Wall thickness encoded in property name ({parsed.ThicknessMm} mm) does not match the defined ETABS wall thickness ({wall.ThicknessMm} mm), difference {(diff > 0 ? "+" : "")}{diff} mm.");
            }
        }

        private ValidationResult NotChecked(ModelData model) => ValidationResult.Create(
            RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
            "Table", KnownTables.WallPropertyDefinitionsSpecified, SourceLocation.Unknown,
            message: "Required ETABS table was not found in the workbook. Rule could not be evaluated.");
    }

    /// <summary>WALL-002: the material grade encoded in the wall property name must match the assigned material's Fc.</summary>
    public sealed class WallMaterialRule : IValidationRule
    {
        private readonly WallPropertyNameParser _parser = new WallPropertyNameParser();

        public string RuleId => RuleIds.Wall002;
        public string Name => "Wall Material Matches Name";
        public string Category => Categories.WallProperties;
        public RuleType Type => RuleType.Consistency;
        public Severity DefaultSeverity => Severity.High;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.WallPropertyDefinitionsSpecified, KnownTables.MaterialPropertiesConcreteData };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (!model.WasTableFound(KnownTables.WallPropertyDefinitionsSpecified))
            {
                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                    "Table", KnownTables.WallPropertyDefinitionsSpecified, SourceLocation.Unknown,
                    message: "Required ETABS table was not found in the workbook. Rule could not be evaluated.");
                yield break;
            }

            foreach (var wall in model.WallProperties)
            {
                var parsed = _parser.Parse(wall.Name);
                if (!parsed.Success)
                {
                    yield return ValidationResult.Create(
                        RuleId, Name, Category, Type, Severity.Low, ValidationStatus.Warning,
                        "WallProperty", wall.Name, wall.Source, fieldName: "Name",
                        message: $"Wall property name '{wall.Name}' could not be parsed for material verification: {parsed.FailureReason}");
                    continue;
                }

                var material = model.ConcreteMaterials.FirstOrDefault(m => string.Equals(m.Name, wall.Material, StringComparison.OrdinalIgnoreCase));
                if (material == null)
                {
                    yield return ValidationResult.Create(
                        RuleId, Name, Category, Type, Severity.High, ValidationStatus.NotChecked,
                        "WallProperty", wall.Name, wall.Source, fieldName: "Material",
                        actual: wall.Material,
                        message: $"Wall '{wall.Name}' references material '{wall.Material}' which was not found in the Concrete Material Data table.");
                    continue;
                }

                var status = Math.Abs(material.FcMpa - parsed.GradeFcMpa) < config.ConcreteMaterials.FcToleranceMpa
                    ? ValidationStatus.Pass
                    : ValidationStatus.Fail;

                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, DefaultSeverity, status,
                    "WallProperty", wall.Name, wall.Source, fieldName: "Material",
                    expected: $"C{parsed.GradeFcMpa} (from name)", actual: $"{wall.Material} (Fc={material.FcMpa} MPa)",
                    message: status == ValidationStatus.Pass
                        ? "Wall material matches the grade encoded in the property name."
                        : $"Wall property name implies grade C{parsed.GradeFcMpa}, but assigned material '{wall.Material}' has Fc={material.FcMpa} MPa.");
            }
        }
    }

    /// <summary>WALL-003 / WALL-004: ordinary walls must have the configured modifier value on
    /// every checked modifier; WT/BW-prefixed walls (Water Tank / Basement Wall) are exempt.</summary>
    public sealed class WallModifierRule : IValidationRule
    {
        private readonly WallPropertyNameParser _parser = new WallPropertyNameParser();

        public string RuleId => RuleIds.Wall003;
        public string Name => "Wall Modifiers";
        public string Category => Categories.WallProperties;
        public RuleType Type => RuleType.ProjectStandard;
        public Severity DefaultSeverity => Severity.Medium;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.WallPropertyDefinitionsSpecified };

        private static readonly Dictionary<string, Func<WallProperty, double>> ModifierAccessors = new Dictionary<string, Func<WallProperty, double>>(StringComparer.OrdinalIgnoreCase)
        {
            ["f11"] = w => w.F11Modifier,
            ["f22"] = w => w.F22Modifier,
            ["f12"] = w => w.F12Modifier,
            ["m11"] = w => w.M11Modifier,
            ["m22"] = w => w.M22Modifier,
            ["m12"] = w => w.M12Modifier
        };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (!model.WasTableFound(KnownTables.WallPropertyDefinitionsSpecified))
            {
                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                    "Table", KnownTables.WallPropertyDefinitionsSpecified, SourceLocation.Unknown,
                    message: "Required ETABS table was not found in the workbook. Rule could not be evaluated.");
                yield break;
            }

            var expected = config.Walls.DefaultModifierValue;

            foreach (var wall in model.WallProperties)
            {
                var parsed = _parser.Parse(wall.Name);
                var isExempt = parsed.Success && config.Walls.ExceptionPrefixes.Any(p => string.Equals(p, parsed.Prefix, StringComparison.OrdinalIgnoreCase));

                if (isExempt)
                {
                    yield return ValidationResult.Create(
                        RuleIds.Wall004, "Wall Modifier Exception", Category, RuleType.ProjectStandard, Severity.Low,
                        ValidationStatus.Exempt, "WallProperty", wall.Name, wall.Source,
                        message: $"'{wall.Name}' is exempt from the standard wall modifier requirement (prefix '{parsed.Prefix}' -> Water Tank / Basement Wall exception).");
                    continue;
                }

                foreach (var modifierKey in config.Walls.CheckedModifiers)
                {
                    if (!ModifierAccessors.TryGetValue(modifierKey, out var accessor))
                    {
                        continue;
                    }

                    var actual = accessor(wall);
                    var status = Math.Abs(actual - expected) < 0.001 ? ValidationStatus.Pass : ValidationStatus.Fail;

                    yield return ValidationResult.Create(
                        RuleId, Name, Category, Type, DefaultSeverity, status,
                        "WallProperty", wall.Name, wall.Source, fieldName: $"{modifierKey.ToUpperInvariant()} Modifier",
                        expected: expected.ToString("0.00"), actual: actual.ToString("0.00"),
                        message: status == ValidationStatus.Pass
                            ? $"{modifierKey.ToUpperInvariant()} modifier matches the project standard ({expected})."
                            : $"{modifierKey.ToUpperInvariant()} modifier is {actual}, expected {expected} per project modelling standard.");
                }
            }
        }
    }
}
