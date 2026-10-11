using System;
using System.Collections.Generic;
using System.Linq;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.Core.Configuration
{
    public enum ConfigIssueLevel { Warning, Error }

    public sealed class ConfigIssue
    {
        public ConfigIssueLevel Level { get; set; }
        /// <summary>JSON path of the setting, e.g. "Slabs.Classifications.PT.M11".</summary>
        public string Path { get; set; }
        public string Message { get; set; }
        public override string ToString() => $"[{Level}] {Path}: {Message}";
    }

    /// <summary>Thrown when a profile fails engineering validation; carries every issue found.</summary>
    public sealed class InvalidConfigurationException : Exception
    {
        public IReadOnlyList<ConfigIssue> Issues { get; }

        public InvalidConfigurationException(IReadOnlyList<ConfigIssue> issues)
            : base("The validation profile is not valid:\n" + string.Join("\n", issues.Where(i => i.Level == ConfigIssueLevel.Error).Select(i => "  " + i)))
        {
            Issues = issues;
        }
    }

    /// <summary>
    /// Engineering validation of a profile before any rule runs. Valid JSON is not the same as a valid
    /// profile: a negative tolerance, a slab prefix claimed by two classes, a code edition the rules
    /// were not written for or a mistyped severity would all silently change verdicts. Errors block
    /// the run; warnings are reported and the run continues.
    /// </summary>
    public static class ConfigurationValidator
    {
        /// <summary>Code profiles whose clause references the rules were written and verified against.
        /// Exact match: "ACI318-14" is NOT accepted - the cited clauses (e.g. 19.2.2.1) are 318-19.</summary>
        public static readonly IReadOnlyList<string> SupportedCodeProfiles = new[] { "ACI318-19" };
        public static readonly IReadOnlyList<string> SupportedConfigVersions = new[] { "1.0" };

        private static readonly string[] ShellModifiers = { "f11", "f22", "f12", "m11", "m22", "m12" };
        private static readonly string[] RigidityTypes = { "Rigid", "Semi-Rigid" };

        public static IReadOnlyList<ConfigIssue> Validate(ValidationProfileConfig c)
        {
            var issues = new List<ConfigIssue>();
            void Error(string path, string msg) => issues.Add(new ConfigIssue { Level = ConfigIssueLevel.Error, Path = path, Message = msg });
            void Warn(string path, string msg) => issues.Add(new ConfigIssue { Level = ConfigIssueLevel.Warning, Path = path, Message = msg });

            if (c == null) { Error("(root)", "Profile is empty."); return issues; }

            if (!SupportedConfigVersions.Contains(c.ConfigVersion)) Error("ConfigVersion", $"'{c.ConfigVersion}' is not supported (supported: {string.Join(", ", SupportedConfigVersions)}).");
            if (!SupportedCodeProfiles.Contains(c.CodeProfile, StringComparer.OrdinalIgnoreCase))
                Error("CodeProfile", $"'{c.CodeProfile}' is not supported. The code-based checks cite ACI 318-19 clauses; supported: {string.Join(", ", SupportedCodeProfiles)}.");

            // Null sections (e.g. "Walls": null in the JSON) would crash or silently skip rules.
            var sections = new (string Name, object Value)[]
            {
                ("Walls", c.Walls), ("Slabs", c.Slabs), ("Modal", c.Modal), ("Diaphragm", c.Diaphragm), ("Frames", c.Frames),
                ("Loads", c.Loads), ("ColumnOverwrites", c.ColumnOverwrites),
                ("RebarMaterials", c.RebarMaterials), ("ConcreteMaterials", c.ConcreteMaterials), ("Geometry", c.Geometry), ("Reporting", c.Reporting)
            };
            foreach (var s in sections.Where(s => s.Value == null)) Error(s.Name, "Section is missing or null.");
            if (issues.Any(i => i.Level == ConfigIssueLevel.Error && i.Message.StartsWith("Section"))) return issues;

            // --- Walls -----------------------------------------------------------------------------
            var mods = c.Walls.CheckedModifiers ?? new List<string>();
            if (mods.Count == 0) Error("Walls.CheckedModifiers", "At least one modifier must be checked.");
            foreach (var m in mods.Where(m => !ShellModifiers.Contains(m, StringComparer.OrdinalIgnoreCase)))
                Error("Walls.CheckedModifiers", $"'{m}' is not a shell modifier ({string.Join(", ", ShellModifiers)}).");
            foreach (var d in mods.GroupBy(m => m, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                Error("Walls.CheckedModifiers", $"'{d.Key}' is listed more than once.");
            ModifierValue("Walls.DefaultModifierValue", c.Walls.DefaultModifierValue, Error, Warn);
            Prefixes("Walls.ExceptionPrefixes", c.Walls.ExceptionPrefixes, allowEmptyList: true, Error);

            // --- Slabs -----------------------------------------------------------------------------
            var classes = c.Slabs.Classifications ?? new Dictionary<string, SlabModifierProfile>();
            if (classes.Count == 0) Error("Slabs.Classifications", "No slab classifications defined.");
            var catchAll = classes.Where(k => k.Value == null || k.Value.NamePrefixes == null || k.Value.NamePrefixes.Count == 0).Select(k => k.Key).ToList();
            if (catchAll.Count != 1)
                Error("Slabs.Classifications", $"Exactly one classification must have no name prefixes (the catch-all for ordinary slabs); found {catchAll.Count}" +
                                               (catchAll.Count > 0 ? $": {string.Join(", ", catchAll)}" : "") + ".");
            var owners = new List<(string Prefix, string Class)>();
            foreach (var kv in classes)
            {
                var path = $"Slabs.Classifications.{kv.Key}";
                if (kv.Value == null) { Error(path, "Classification is null."); continue; }
                ModifierValue(path + ".M11", kv.Value.M11, Error, Warn);
                ModifierValue(path + ".M22", kv.Value.M22, Error, Warn);
                ModifierValue(path + ".M12", kv.Value.M12, Error, Warn);
                Prefixes(path + ".NamePrefixes", kv.Value.NamePrefixes, allowEmptyList: true, Error);
                foreach (var p in kv.Value.NamePrefixes ?? new List<string>()) if (!string.IsNullOrWhiteSpace(p)) owners.Add((p.Trim(), kv.Key));
            }
            // SlabPropertyNameParser compares the WHOLE letter prefix of the name (e.g. "PT" from "PT250-C40")
            // for equality, taking the first class that lists it - and dictionary order is not a contract.
            // So the same prefix in two classes is ambiguous. ("P" and "PT" are distinct and do not collide.)
            foreach (var g in owners.GroupBy(o => o.Prefix, StringComparer.OrdinalIgnoreCase).Where(g => g.Select(o => o.Class).Distinct().Count() > 1))
                Error("Slabs.Classifications", $"Prefix '{g.Key}' is claimed by {string.Join(" and ", g.Select(o => $"'{o.Class}'").Distinct())} - the class of a slab named '{g.Key}...' would be undefined.");
            foreach (var p in owners.Where(o => !o.Prefix.All(char.IsLetter)))
                Error($"Slabs.Classifications.{p.Class}.NamePrefixes", $"'{p.Prefix}' can never match: the name parser takes only the leading letters of a slab name as its prefix.");

            // --- Modal -----------------------------------------------------------------------------
            var ratio = c.Modal.MinimumTargetRatioPercent;
            if (!(ratio > 0 && ratio <= 100)) Error("Modal.MinimumTargetRatioPercent", $"{ratio} must be in (0, 100] %.");
            else if (ratio < 90) Warn("Modal.MinimumTargetRatioPercent", $"{ratio} % is below the 90 % modal mass participation ASCE 7 requires (12.9.1.1 - verify in your edition).");

            // --- Diaphragm / frames / loads / overwrites -------------------------------------------------
            if (!RigidityTypes.Contains(c.Diaphragm.RequiredRigidityType, StringComparer.OrdinalIgnoreCase))
                Error("Diaphragm.RequiredRigidityType", $"'{c.Diaphragm.RequiredRigidityType}' is not an ETABS rigidity type ({string.Join(", ", RigidityTypes)}).");
            if (!(c.Frames.IntegerTolerance > 0 && c.Frames.IntegerTolerance < 0.5))
                Error("Frames.IntegerTolerance", $"{c.Frames.IntegerTolerance} must be > 0 and < 0.5 (a tolerance of 0.5 or more accepts every value).");
            if (string.IsNullOrWhiteSpace(c.Loads.DeadPatternTypeName)) Error("Loads.DeadPatternTypeName", "Must name the ETABS load pattern type for dead load (normally 'Dead').");
            if (!(c.Loads.RequiredSelfWeightMultiplier > 0)) Error("Loads.RequiredSelfWeightMultiplier", $"{c.Loads.RequiredSelfWeightMultiplier} must be > 0.");
            else if (Math.Abs(c.Loads.RequiredSelfWeightMultiplier - 1.0) > 1e-9)
                Warn("Loads.RequiredSelfWeightMultiplier", $"{c.Loads.RequiredSelfWeightMultiplier}: self weight is physically 1.0 x the real weight; any other value under- or double-counts it.");
            if (string.IsNullOrWhiteSpace(c.ColumnOverwrites.RequiredDesignSection)) Error("ColumnOverwrites.RequiredDesignSection", "Must not be blank.");

            // --- Rebar -----------------------------------------------------------------------------
            var grades = c.RebarMaterials.GradeProfiles ?? new Dictionary<string, RebarGradeProfile>();
            if (grades.Count == 0) Error("RebarMaterials.GradeProfiles", "No rebar grade profiles defined; MAT-REBAR-001 would have nothing to compare against.");
            foreach (var kv in grades)
            {
                var path = $"RebarMaterials.GradeProfiles.{kv.Key}";
                var g = kv.Value;
                if (g == null) { Error(path, "Profile is null."); continue; }
                if (!(g.FyMpa > 0)) Error(path + ".FyMpa", $"{g.FyMpa} must be > 0.");
                if (!(g.FuMpa >= g.FyMpa)) Error(path + ".FuMpa", $"Fu {g.FuMpa} MPa is below Fy {g.FyMpa} MPa - tensile strength cannot be below yield.");
                if (!(g.FyeMpa >= g.FyMpa)) Warn(path + ".FyeMpa", $"Expected yield Fye {g.FyeMpa} MPa is below Fy {g.FyMpa} MPa.");
                if (!(g.FueMpa >= g.FyeMpa)) Error(path + ".FueMpa", $"Expected tensile Fue {g.FueMpa} MPa is below Fye {g.FyeMpa} MPa.");
            }
            var minFy = grades.Values.Where(g => g != null && g.FyMpa > 0).Select(g => g.FyMpa).DefaultIfEmpty(420).Min();
            if (!(c.RebarMaterials.ToleranceMpa >= 0) || c.RebarMaterials.ToleranceMpa > 0.05 * minFy)
                Error("RebarMaterials.ToleranceMpa", $"{c.RebarMaterials.ToleranceMpa} MPa must be >= 0 and <= 5 % of the lowest Fy ({0.05 * minFy:0.#} MPa).");

            // --- Concrete --------------------------------------------------------------------------
            if (!(c.ConcreteMaterials.FcToleranceMpa >= 0 && c.ConcreteMaterials.FcToleranceMpa <= 2))
                Error("ConcreteMaterials.FcToleranceMpa", $"{c.ConcreteMaterials.FcToleranceMpa} MPa must be in [0, 2] MPa (grades differ by 5 MPa or more).");

            // --- Geometry --------------------------------------------------------------------------
            if (c.Geometry.CoordinateToleranceMm < 0) Error("Geometry.CoordinateToleranceMm", "Must be >= 0.");
            if (c.Geometry.PierAdjacencyToleranceMm < 0) Error("Geometry.PierAdjacencyToleranceMm", "Must be >= 0.");
            // An empty prefix would match every frame label and flag every column as a brace.
            if (c.Geometry.BraceLabelPrefixes != null && c.Geometry.BraceLabelPrefixes.Any(string.IsNullOrWhiteSpace))
                Error("Geometry.BraceLabelPrefixes", "Prefixes must not be blank - a blank prefix matches every frame label.");

            // --- Reporting -------------------------------------------------------------------------
            if (!Enum.TryParse(c.Reporting.ReviewSeverity ?? "", true, out Severity sev) || !Enum.IsDefined(typeof(Severity), sev) ||
                int.TryParse(c.Reporting.ReviewSeverity, out _))
                Error("Reporting.ReviewSeverity", $"'{c.Reporting.ReviewSeverity}' is not a severity ({string.Join(", ", Enum.GetNames(typeof(Severity)))}).");
            if (c.Reporting.AcceptExemptions) Warn("Reporting.AcceptExemptions", "Exempt items will not require review. Make sure every exemption rule in this profile has a documented, approved basis.");
            if (c.Reporting.OptionalRules != null && c.Reporting.OptionalRules.Count > 0)
                Warn("Reporting.OptionalRules", $"NOT_CHECKED results of {string.Join(", ", c.Reporting.OptionalRules)} will not make the run incomplete.");

            return issues;
        }

        /// <summary>Validates and throws <see cref="InvalidConfigurationException"/> on any error.</summary>
        public static IReadOnlyList<ConfigIssue> EnsureValid(ValidationProfileConfig config)
        {
            var issues = Validate(config);
            if (issues.Any(i => i.Level == ConfigIssueLevel.Error)) throw new InvalidConfigurationException(issues);
            return issues;
        }

        private static void ModifierValue(string path, double v, Action<string, string> error, Action<string, string> warn)
        {
            if (double.IsNaN(v) || !(v > 0)) error(path, $"{v} must be > 0 (a zero stiffness modifier makes the element a mechanism; use a small value such as 0.01).");
            else if (v > 1.0) warn(path, $"{v} is above 1.0 (stiffer than the gross section) - unusual for a cracked-section modifier.");
        }

        private static void Prefixes(string path, List<string> prefixes, bool allowEmptyList, Action<string, string> error)
        {
            if (prefixes == null) { if (!allowEmptyList) error(path, "List is missing."); return; }
            foreach (var p in prefixes.Where(p => string.IsNullOrWhiteSpace(p))) error(path, "Contains a blank prefix (it would match every name).");
            foreach (var d in prefixes.Where(p => !string.IsNullOrWhiteSpace(p)).GroupBy(p => p.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                error(path, $"'{d.Key}' is listed more than once.");
        }
    }
}
