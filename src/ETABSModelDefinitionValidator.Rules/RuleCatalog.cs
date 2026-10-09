using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ETABSModelDefinitionValidator.Core;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.Rules
{
    /// <summary>Plain-language, engineer-facing description of one check. Pure documentation:
    /// nothing here affects what a rule decides.</summary>
    public sealed class RuleExplanation
    {
        public string RuleId { get; set; }
        public string Title { get; set; }
        public string Category { get; set; }
        public RuleType Type { get; set; }
        public string Table { get; set; }

        /// <summary>What is compared, in one or two sentences.</summary>
        public string WhatItChecks { get; set; }
        /// <summary>The engineering reason the check exists.</summary>
        public string WhyItMatters { get; set; }
        /// <summary>The value / condition that makes it PASS (thresholds come from the active profile).</summary>
        public string PassCondition { get; set; }
        /// <summary>What to do in ETABS when it does not pass.</summary>
        public string HowToFix { get; set; }
        /// <summary>Where the requirement comes from, stated honestly (code vs office standard).</summary>
        public string Basis { get; set; }
        /// <summary>Limits of the check / things a reviewing engineer should still judge.</summary>
        public string EngineerNote { get; set; }
    }

    /// <summary>
    /// Explanations for every RuleId the engine can emit (including IDs emitted as a sub-check of
    /// another rule class, e.g. PIER-002 and WALL-004). Built from the active profile so the
    /// thresholds shown are the ones actually used in the run.
    /// </summary>
    public static class RuleCatalog
    {
        private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        public static IReadOnlyList<RuleExplanation> All(ValidationProfileConfig c)
        {
            c = c ?? new ValidationProfileConfig();
            var slab = c.Slabs.Classifications;
            string Slab(string key, double fallback) => slab != null && slab.TryGetValue(key, out var p) ? N(p.M11) : N(fallback);
            var rebar = string.Join(", ", c.RebarMaterials.GradeProfiles.Select(g => $"{g.Key}: fy {N(g.Value.FyMpa)} / fu {N(g.Value.FuMpa)} MPa"));

            return new List<RuleExplanation>
            {
                new RuleExplanation
                {
                    RuleId = RuleIds.Wall001, Title = "Wall thickness matches its name", Category = Categories.WallProperties,
                    Type = RuleType.Consistency, Table = KnownTables.WallPropertyDefinitionsSpecified,
                    WhatItChecks = "Reads the thickness encoded in the wall property name (W300-C40 = 300 mm) and compares it with the thickness actually defined.",
                    WhyItMatters = "Drawings, quantity take-offs and reviewers all read the name. If the name says 300 but the model uses 350, the analysis, the design and the documents silently disagree.",
                    PassCondition = "Name-derived thickness equals the defined thickness.",
                    HowToFix = "Either rename the property to the real thickness or correct the thickness - decide which one is the design intent first.",
                    Basis = "Consistency check (not a code requirement).",
                    EngineerNote = "A name that cannot be parsed gives a WARNING, never a guess."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Wall002, Title = "Wall concrete grade matches its name", Category = Categories.WallProperties,
                    Type = RuleType.Consistency, Table = KnownTables.WallPropertyDefinitionsSpecified + " + " + KnownTables.MaterialPropertiesConcreteData,
                    WhatItChecks = "Reads the grade in the wall name (…-C40) and compares it with the grade of the material assigned to that wall property.",
                    WhyItMatters = "Wall strength drives stiffness (Ec), capacity and the design output. A C40-named wall assigned a C50 material is a wrong-material error that is invisible in the 3D view.",
                    PassCondition = "Grade in the name equals the assigned material's grade.",
                    HowToFix = "Assign the correct material to the wall property, or rename it.",
                    Basis = "Consistency check (not a code requirement).",
                    EngineerNote = "Only as reliable as the naming convention; confirm the material table itself with MAT-CONC-001."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Wall003, Title = "Wall stiffness modifiers", Category = Categories.WallProperties,
                    Type = RuleType.ProjectStandard, Table = KnownTables.WallPropertyDefinitionsSpecified,
                    WhatItChecks = $"Checks the property modifiers {string.Join(", ", c.Walls.CheckedModifiers)} of every ordinary wall.",
                    WhyItMatters = "Modifiers set how much of the gross stiffness the analysis uses. They directly change period, drift, and how load is shared between walls and frames.",
                    PassCondition = $"Every checked modifier = {N(c.Walls.DefaultModifierValue)}.",
                    HowToFix = "Reset the modifiers in Define > Section Properties > Wall Properties > Modify/Show > Property Modifiers.",
                    Basis = "Office standard. For reference, ACI 318 Table 6.6.3.1.1 gives 0.70 Ig for uncracked walls and 0.35 Ig for cracked walls - confirm the clause number in your edition.",
                    EngineerNote = "The ACI table reduces moment of inertia; reducing the membrane (f11/f22/f12) terms as well is an office choice and also affects axial stiffness and shear stiffness. Confirm that is intended. Walls expected to crack in service (basement, retaining) are normally handled separately - see WALL-004."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Wall004, Title = "Wall modifier exemption (water tank / basement walls)", Category = Categories.WallProperties,
                    Type = RuleType.ProjectStandard, Table = KnownTables.WallPropertyDefinitionsSpecified,
                    WhatItChecks = $"Identifies walls whose name starts with {string.Join(" or ", c.Walls.ExceptionPrefixes)} and exempts them from WALL-003.",
                    WhyItMatters = "These walls carry earth or fluid pressure and are designed as cracked, out-of-plane members, so the standard 0.70 value is not the right target for them.",
                    PassCondition = "Not a pass/fail test - the result is EXEMPT and the wall is not checked by WALL-003.",
                    HowToFix = "Nothing to fix. Make sure the modifiers on these walls were chosen deliberately, because no automatic check covers them.",
                    Basis = "Office standard (configurable list of prefixes).",
                    EngineerNote = "An exemption means 'not checked', not 'correct'. Review these walls by hand."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Slab001, Title = "Post-tensioned slab modifiers", Category = Categories.SlabProperties,
                    Type = RuleType.ProjectStandard, Table = KnownTables.SlabPropertyDefinitions,
                    WhatItChecks = "Slabs classified as PT by name prefix: checks m11, m22, m12 bending modifiers.",
                    WhyItMatters = "Slab bending stiffness controls vertical deflection, frame-slab interaction and the share of lateral load taken by slab-column frames.",
                    PassCondition = $"m11 = m22 = m12 = {Slab("PT", 0.35)}.",
                    HowToFix = "Edit the slab property modifiers (Define > Section Properties > Slab Properties > Modify/Show > Property Modifiers).",
                    Basis = "Office standard. ACI 318 has no PT-specific entry in its stiffness table.",
                    EngineerNote = "PT slabs are often analysed closer to gross/uncracked stiffness for service deflection and to cracked for strength. Make sure the same value is correct for both uses, and that the PT load-balancing model is consistent with it."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Slab002, Title = "Ordinary and raft slab modifiers", Category = Categories.SlabProperties,
                    Type = RuleType.ProjectStandard, Table = KnownTables.SlabPropertyDefinitions,
                    WhatItChecks = "Ordinary slabs (S…, D… drop panels and anything not matched by another prefix) and raft slabs (RAFT…): checks m11, m22, m12.",
                    WhyItMatters = "A flat slab modelled at full stiffness attracts too much lateral load and under-predicts deflection; a raft modelled with cracked stiffness changes soil-structure load sharing.",
                    PassCondition = $"Ordinary: {Slab("Ordinary", 0.25)}. Raft: {Slab("Raft", 1.0)}. (m11 = m22 = m12.)",
                    HowToFix = "Edit the slab property modifiers. If a slab is classified wrongly, fix its name prefix.",
                    Basis = "Ordinary: consistent with ACI 318 Table 6.6.3.1.1, flat plates and flat slabs 0.25 Ig (confirm clause in your edition). Raft: office modelling standard, still pending confirmation as a firm rule.",
                    EngineerNote = "Classification is by name prefix, so a mis-typed name changes the expected value. Rafts on a Winkler/spring model are normally uncracked for stiffness - check the cracked-section assumption used for the foundation design."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Slab003, Title = "Stair and ramp slab modifiers", Category = Categories.SlabProperties,
                    Type = RuleType.ProjectStandard, Table = KnownTables.SlabPropertyDefinitions,
                    WhatItChecks = "Slabs classified as stair or ramp: checks m11, m22, m12.",
                    WhyItMatters = "Stairs and ramps are inclined and would otherwise act as stiff braces or diaphragm links. A near-zero modifier removes that unintended stiffness while keeping their weight.",
                    PassCondition = $"Stair: {Slab("Stair", 0.01)}. Ramp: {Slab("Ramp", 0.01)}.",
                    HowToFix = "Edit the slab property modifiers on the stair/ramp property.",
                    Basis = "Office modelling standard (no code requirement).",
                    EngineerNote = "Stair flights and ramps then need to be designed outside the model, and the slabs they bear on must be loaded for their reactions."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Pier001, Title = "Pier width: top equals bottom", Category = Categories.PierProperties,
                    Type = RuleType.ModelIntegrity, Table = KnownTables.PierSectionProperties,
                    WhatItChecks = "Compares Width Bottom with Width Top of each pier section.",
                    WhyItMatters = "A pier is designed as one member per story. If top and bottom differ the section changes within the story, and the design section (and its reinforcement) is no longer well defined.",
                    PassCondition = "Width Top = Width Bottom.",
                    HowToFix = "Check the pier definition and the wall geometry it was created from; a mismatch normally means a mesh or drawing error.",
                    Basis = "Model integrity check.",
                    EngineerNote = "A tapered or stepped wall is legitimate. In that case split the pier or document the exception."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Pier002, Title = "Pier thickness: top equals bottom", Category = Categories.PierProperties,
                    Type = RuleType.ModelIntegrity, Table = KnownTables.PierSectionProperties,
                    WhatItChecks = "Compares Thickness Bottom with Thickness Top of each pier section.",
                    WhyItMatters = "Same reason as PIER-001, and thickness also changes the wall's slenderness and shear capacity (Vc depends on thickness).",
                    PassCondition = "Thickness Top = Thickness Bottom.",
                    HowToFix = "Correct the wall property assigned to the pier's panels so one thickness applies over the story.",
                    Basis = "Model integrity check.",
                    EngineerNote = "Same exception as PIER-001 for walls that deliberately change thickness mid-story."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Modal001, Title = "Modal Ritz target mass ratio", Category = Categories.Modal,
                    Type = RuleType.ProjectStandard, Table = KnownTables.ModalCaseDefinitionsRitz,
                    WhatItChecks = "Reads the target dynamic participation ratio of the Ritz modal case.",
                    WhyItMatters = "Response-spectrum results are only complete when enough mass is captured by the modes. ASCE 7 requires at least 90% of the actual mass in each horizontal direction; offices often ask for more.",
                    PassCondition = $"Target ratio >= {N(c.Modal.MinimumTargetRatioPercent)} %.",
                    HowToFix = "Open the Ritz modal case and raise the target dynamic participation ratio (and the maximum number of modes if needed).",
                    Basis = "Office standard (stricter than the 90% in ASCE 7 12.9.1.1).",
                    EngineerNote = "This checks the SETTING, not the achieved result. It does not read the Modal Participating Mass Ratios table, so a model can pass here and still fall short of 90%. Review the achieved ΣUX and ΣUY separately."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.MatRebar001, Title = "Rebar strengths match the grade", Category = Categories.Materials,
                    Type = RuleType.ProjectStandard, Table = KnownTables.MaterialPropertiesRebarData,
                    WhatItChecks = "Compares Fy, Fu, Fye and Fue of each rebar material with the configured profile for that grade name.",
                    WhyItMatters = "Rebar yield strength sets every flexural and shear capacity in the design. Wrong values produce wrong reinforcement without any visible warning.",
                    PassCondition = $"Within {N(c.RebarMaterials.ToleranceMpa)} MPa of: {rebar}.",
                    HowToFix = "Correct the rebar material (Define > Materials) or update the grade profile in the configuration if the project uses a different steel.",
                    Basis = "Office standard grade table (reference-model sourced) - not verified against ASTM/BS product standards.",
                    EngineerNote = "Fye/Fue are the expected strengths used for capacity design. Here they equal fy/fu, i.e. no overstrength factor is built in - apply overstrength in the design if the code requires it. Only grades named in the profile are checked."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.MatConc001, Title = "Concrete f'c matches grade name", Category = Categories.Materials,
                    Type = RuleType.Consistency, Table = KnownTables.MaterialPropertiesConcreteData,
                    WhatItChecks = "Reads the grade in the material name (C40/50 = fc 40 MPa) and compares it with the defined f'c.",
                    WhyItMatters = "f'c drives Ec, shear capacity and minimum reinforcement. A name that says C40 on a C32 material hides under-strength concrete.",
                    PassCondition = $"Defined f'c within {N(c.ConcreteMaterials.FcToleranceMpa)} MPa of the name-derived value.",
                    HowToFix = "Correct f'c on the material or rename it.",
                    Basis = "Consistency check.",
                    EngineerNote = "The first number of C40/50 is the cylinder strength. If your project quotes cube strength first, the name convention will not match."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.MatConc002, Title = "Concrete modulus of elasticity", Category = Categories.Materials,
                    Type = RuleType.Code, Table = KnownTables.MaterialPropertiesConcreteData + " + " + KnownTables.MaterialPropertiesBasicMechanicalProperties,
                    WhatItChecks = "Compares the material's E1 with Ec = 4700 * sqrt(f'c) (MPa).",
                    WhyItMatters = "Ec governs all deformation, period and force distribution between stiff and flexible members.",
                    PassCondition = "E1 within 5 % of 4700 * sqrt(f'c). A larger difference is a WARNING.",
                    HowToFix = "Set E1 to the formula value, or document why a project-specific Ec (e.g. from tests or a long-term modification) is used.",
                    Basis = "ACI 318-19, 19.2.2.1(b), normalweight concrete.",
                    EngineerNote = "Lightweight concrete and non-ACI code profiles are NOT_CHECKED, not passed. A deliberately modified Ec (e.g. for creep, or a stiffer value for drift) will warn - that is expected."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Load001, Title = "Dead load pattern self-weight multiplier", Category = Categories.Loads,
                    Type = RuleType.Code, Table = KnownTables.LoadPatternDefinitions,
                    WhatItChecks = $"Finds load patterns of type '{c.Loads.DeadPatternTypeName}' and reads their Self Weight Multiplier.",
                    WhyItMatters = "A multiplier below 1 under-counts structural weight; above 1 double-counts it. Both change every gravity load, base shear, seismic mass and foundation load.",
                    PassCondition = $"Self Weight Multiplier = {N(c.Loads.RequiredSelfWeightMultiplier)}.",
                    HowToFix = "Set the multiplier on the Dead pattern in Define > Load Patterns.",
                    Basis = "Physical requirement: self weight must equal the true weight.",
                    EngineerNote = "Only the Dead pattern is checked. It does not confirm that all OTHER patterns have a multiplier of 0 - a stray 1.0 on, say, a superimposed-dead pattern doubles the self weight."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Load002, Title = "Load case name matches its load name", Category = Categories.Loads,
                    Type = RuleType.Consistency, Table = KnownTables.LoadCaseDefinitionsLinearStatic,
                    WhatItChecks = "For each linear-static case, compares the case name with the load pattern it applies.",
                    WhyItMatters = "Combinations are built from case names. If case 'EX' really applies pattern 'EX-GF', results get combined under the wrong label.",
                    PassCondition = "Load name equals the case name (case-insensitive).",
                    HowToFix = "Change the load pattern the case applies, or rename the case. If the difference is intentional, ignore the warning.",
                    Basis = "Consistency check, reported as WARNING by project direction.",
                    EngineerNote = "A WARNING, not a FAIL: it needs a human decision. Cases with several loads are not covered."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Frame001, Title = "Frame auto mesh is on", Category = Categories.Frames,
                    Type = RuleType.ModelingStandard, Table = KnownTables.FrameAssignmentsFrameAutoMeshOptions,
                    WhatItChecks = "Reads the Auto Mesh option on frame assignments.",
                    WhyItMatters = "With auto mesh on, ETABS splits frames at intersecting members so load and stiffness transfer correctly at beam-to-beam and column-to-beam connections. Without it a beam can pass 'through' a girder with no connection.",
                    PassCondition = "Auto Mesh = Yes.",
                    HowToFix = "Select the frames and use Assign > Frame > Auto Mesh Options.",
                    Basis = "Office modelling standard.",
                    EngineerNote = "Intentionally unmeshed frames (e.g. a deliberate release or a transfer beam) will fail; handle them as documented exceptions."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Frame002, Title = "Column end offsets are whole numbers", Category = Categories.Frames,
                    Type = RuleType.ModelingStandard, Table = KnownTables.FrameAssignmentsEndLengthOffsets + " + " + KnownTables.ConcreteColumnOverwritesAci31819,
                    WhatItChecks = c.Frames.RestrictIntegerOffsetCheckToColumns
                        ? "For frames that appear in the column design overwrites (i.e. columns), checks that Offset I and Offset J are integers."
                        : "For every frame, checks that Offset I and Offset J are integers.",
                    WhyItMatters = "Column rigid-zone offsets come from the depth of the connecting beams/slabs. Fractional values like 949.7 usually mean the model was built from slightly off-grid geometry, which also produces sliver mesh and inconsistent clear lengths.",
                    PassCondition = $"|offset - round(offset)| < {N(c.Frames.IntegerTolerance)} for both ends.",
                    HowToFix = "Clean up the geometry (snap to grid, align beam depths) and re-run the auto offset, or correct the offset by hand.",
                    Basis = "Office modelling standard.",
                    EngineerNote = "Beams are skipped on purpose because their offsets are legitimately geometry-derived. The offset affects the clear length used for the column shear check and for slenderness, so fractions are worth a look but are rarely a safety issue by themselves."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Col001, Title = "Column design section is Program Determined", Category = Categories.DesignOverwrites,
                    Type = RuleType.ProjectStandard, Table = KnownTables.ConcreteColumnOverwritesAci31819,
                    WhatItChecks = "Reads the Design Section overwrite of each concrete column.",
                    WhyItMatters = "A manual design section replaces ETABS's own check and can hide an overstressed column or a section that no longer matches the model.",
                    PassCondition = $"Design Section = {c.ColumnOverwrites.RequiredDesignSection}.",
                    HowToFix = "Design > Concrete Frame Design > View/Revise Overwrites, and reset the Design Section.",
                    Basis = "Office standard.",
                    EngineerNote = "Check other overwrites too (effective length factors, unbraced length, live-load reduction) - they are not covered by this check."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Dia001, Title = "Diaphragm rigidity type", Category = Categories.Diaphragms,
                    Type = RuleType.ModelingStandard, Table = KnownTables.DiaphragmDefinitions,
                    WhatItChecks = "Reads the Rigidity Type of every diaphragm definition.",
                    WhyItMatters = "A rigid diaphragm forces all points on a floor to move together; a semi-rigid one lets the slab deform and carry its own in-plane forces. This changes how lateral load is shared between walls and frames.",
                    PassCondition = $"Rigidity Type = {c.Diaphragm.RequiredRigidityType}.",
                    HowToFix = "Define > Diaphragms > Modify/Show, and set the rigidity.",
                    Basis = "Office modelling standard.",
                    EngineerNote = "Semi-rigid is the safer choice for podiums, transfer floors and long or irregular plans; it also requires a refined slab mesh. Whichever is chosen must match the ASCE 7 12.3.1 assumptions of the design."
                }
            };
        }

        public static RuleExplanation Find(string ruleId, ValidationProfileConfig config)
        {
            return All(config).FirstOrDefault(e => string.Equals(e.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
