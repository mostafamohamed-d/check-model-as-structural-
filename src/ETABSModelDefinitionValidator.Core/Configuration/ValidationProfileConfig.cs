using System.Collections.Generic;

namespace ETABSModelDefinitionValidator.Core.Configuration
{
    /// <summary>
    /// Every engineering threshold used by the rule set, externalized from code so a project's
    /// modelling standard can be tuned without a rebuild. This is the deserialized form of
    /// config/default-validation-profile.json (or a project-specific override of it).
    /// </summary>
    public sealed class ValidationProfileConfig
    {
        public string ConfigVersion { get; set; } = "1.0";
        public string CodeProfile { get; set; } = "ACI318-19";

        public WallRulesConfig Walls { get; set; } = new WallRulesConfig();
        public SlabRulesConfig Slabs { get; set; } = new SlabRulesConfig();
        public ModalRulesConfig Modal { get; set; } = new ModalRulesConfig();
        public DiaphragmRulesConfig Diaphragm { get; set; } = new DiaphragmRulesConfig();
        public FrameRulesConfig Frames { get; set; } = new FrameRulesConfig();
        public LoadPatternRulesConfig Loads { get; set; } = new LoadPatternRulesConfig();
        public ColumnOverwriteRulesConfig ColumnOverwrites { get; set; } = new ColumnOverwriteRulesConfig();
        public RebarMaterialRulesConfig RebarMaterials { get; set; } = new RebarMaterialRulesConfig();
        public ConcreteMaterialRulesConfig ConcreteMaterials { get; set; } = new ConcreteMaterialRulesConfig();
        public GeometryRulesConfig Geometry { get; set; } = new GeometryRulesConfig();

        /// <summary>How results roll up into the run verdict (what blocks a PASS).</summary>
        public ReportingRulesConfig Reporting { get; set; } = new ReportingRulesConfig();
    }

    public sealed class WallRulesConfig
    {
        /// <summary>Which of the six modifiers WALL-003 enforces. Default is all six, matching
        /// the verified reference workbook (see docs/ValidationMatrix.md discrepancy #1).</summary>
        public List<string> CheckedModifiers { get; set; } = new List<string> { "f11", "f22", "f12", "m11", "m22", "m12" };
        public double DefaultModifierValue { get; set; } = 0.70;

        /// <summary>Wall name prefixes exempted from WALL-003 (WALL-004). Case-insensitive.</summary>
        public List<string> ExceptionPrefixes { get; set; } = new List<string> { "WT", "BW" };
    }

    public sealed class SlabModifierProfile
    {
        public List<string> NamePrefixes { get; set; } = new List<string>();
        public double M11 { get; set; }
        public double M22 { get; set; }
        public double M12 { get; set; }
    }

    public sealed class SlabRulesConfig
    {
        /// <summary>Keyed by classification name (PT, Ordinary, Stair, Ramp, Raft). Order matters:
        /// evaluated top-to-bottom, first name-prefix match wins, "Ordinary" should be last with
        /// an empty prefix list acting as the catch-all.</summary>
        public Dictionary<string, SlabModifierProfile> Classifications { get; set; } = new Dictionary<string, SlabModifierProfile>();

        public static SlabRulesConfig Default() => new SlabRulesConfig
        {
            Classifications = new Dictionary<string, SlabModifierProfile>
            {
                ["PT"] = new SlabModifierProfile { NamePrefixes = { "PT" }, M11 = 0.35, M22 = 0.35, M12 = 0.35 },
                ["Stair"] = new SlabModifierProfile { NamePrefixes = { "STAIR" }, M11 = 0.01, M22 = 0.01, M12 = 0.01 },
                ["Ramp"] = new SlabModifierProfile { NamePrefixes = { "RAMP" }, M11 = 0.01, M22 = 0.01, M12 = 0.01 },
                ["Raft"] = new SlabModifierProfile { NamePrefixes = { "RAFT" }, M11 = 1.0, M22 = 1.0, M12 = 1.0 },
                ["Ordinary"] = new SlabModifierProfile { NamePrefixes = { }, M11 = 0.25, M22 = 0.25, M12 = 0.25 }
            }
        };
    }

    public sealed class ModalRulesConfig
    {
        public double MinimumTargetRatioPercent { get; set; } = 95.0;
    }

    public sealed class DiaphragmRulesConfig
    {
        public string RequiredRigidityType { get; set; } = "Semi-Rigid";
    }

    public sealed class FrameRulesConfig
    {
        public bool RequireAutoMesh { get; set; } = true;
        public bool RequireIntegerEndOffsets { get; set; } = true;
        public double IntegerTolerance { get; set; } = 0.001;

        /// <summary>When true (default), FRAME-002 only checks frames identified as columns (via
        /// Concrete Column Overwrites) - beams routinely have non-integer, geometry-derived
        /// offsets and are not checked. Set false to check every frame regardless of type.</summary>
        public bool RestrictIntegerOffsetCheckToColumns { get; set; } = true;
    }

    public sealed class LoadPatternRulesConfig
    {
        public string DeadPatternTypeName { get; set; } = "Dead";
        public double RequiredSelfWeightMultiplier { get; set; } = 1.0;
    }

    public sealed class ColumnOverwriteRulesConfig
    {
        public string RequiredDesignSection { get; set; } = "Program Determined";
    }

    public sealed class RebarGradeProfile
    {
        public double FyMpa { get; set; }
        public double FuMpa { get; set; }
        public double FyeMpa { get; set; }
        public double FueMpa { get; set; }
    }

    public sealed class RebarMaterialRulesConfig
    {
        /// <summary>Keyed by material name as it appears in the workbook (e.g. "Fy420").</summary>
        public Dictionary<string, RebarGradeProfile> GradeProfiles { get; set; } = new Dictionary<string, RebarGradeProfile>();
        public double ToleranceMpa { get; set; } = 0.5;

        public static RebarMaterialRulesConfig Default() => new RebarMaterialRulesConfig
        {
            GradeProfiles = new Dictionary<string, RebarGradeProfile>
            {
                ["Fy420"] = new RebarGradeProfile { FyMpa = 420, FuMpa = 525, FyeMpa = 420, FueMpa = 525 },
                ["Fy500"] = new RebarGradeProfile { FyMpa = 500, FuMpa = 625, FyeMpa = 500, FueMpa = 625 }
            }
        };
    }

    public sealed class ConcreteMaterialRulesConfig
    {
        public double FcToleranceMpa { get; set; } = 0.5;
    }

    /// <summary>Thresholds of the geometry, connectivity, support and load-assignment checks
    /// (GEO-*, SUP-001, LOAD-003/004). Lengths are in mm, the units models are read in.</summary>
    public sealed class GeometryRulesConfig
    {
        /// <summary>GEO-001 / SUP-001: points closer than this are the same point / same elevation.
        /// 0 = exact match. The column plumb and continuity checks never use a tolerance.</summary>
        public double CoordinateToleranceMm { get; set; } = 0.0;

        /// <summary>GEO-004: two wall panels of one pier label touch when any of their corners are
        /// within this distance in plan. Meshed panels share corners that are only nominally identical,
        /// so an exact match would split every pier.</summary>
        public double PierAdjacencyToleranceMm { get; set; } = 20.0;

        /// <summary>SUP-001: a model whose file name contains any of these (case-insensitive) is a
        /// foundation model - the raft sits on springs, so no joint may be restrained.</summary>
        public List<string> FoundationModelNameTokens { get; set; } = new List<string> { "fnd" };

        /// <summary>GEO-002: a column or brace frame whose label starts with any of these (case-insensitive)
        /// is reported as a WARNING - ETABS labels an inclined frame "D" (brace), so a column drawn
        /// slightly off-vertical shows up here instead of in the column table.</summary>
        public List<string> BraceLabelPrefixes { get; set; } = new List<string> { "D" };
    }

    /// <summary>
    /// What a run must achieve to be reported PASS. The defaults are deliberately strict: a check
    /// that did not run, an exemption, or an empty table never counts as verified unless the
    /// approved project profile says so explicitly.
    /// </summary>
    public sealed class ReportingRulesConfig
    {
        /// <summary>WARNING results at or above this severity (Low, Medium, High, Critical) need an
        /// engineer's review before the run can pass. Lower-severity warnings give PASS_WITH_WARNINGS.</summary>
        public string ReviewSeverity { get; set; } = "High";

        /// <summary>When false (default) any EXEMPT result makes the run REVIEW_REQUIRED: an exemption
        /// means "not checked automatically", not "verified". Set true only when the project profile has
        /// an approved, documented basis for every exemption rule it uses.</summary>
        public bool AcceptExemptions { get; set; }

        /// <summary>Rule ids whose NOT_CHECKED results do not make the run INCOMPLETE (they are still
        /// listed). Default empty: every enabled rule is required.</summary>
        public List<string> OptionalRules { get; set; } = new List<string>();

        /// <summary>Table titles that may legitimately be read with zero records without needing review
        /// (e.g. a model with no diaphragms). Default empty.</summary>
        public List<string> AllowedEmptyTables { get; set; } = new List<string>();
    }
}
