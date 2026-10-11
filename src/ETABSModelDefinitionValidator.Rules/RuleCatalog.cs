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

        /// <summary>Who demands the check, derived from <see cref="Type"/>: a code requirement is never
        /// presented the same way as an office standard.</summary>
        public string Authority => RuleCatalog.AuthorityOf(Type);
        /// <summary>Which objects the check applies to (and which it never looks at).</summary>
        public string Applicability { get; set; }
        /// <summary>Code clause / standard / office document, with its verification status.</summary>
        public string Reference { get; set; }
        /// <summary>Numerical tolerance and why it is that value ("exact" when none).</summary>
        public string Tolerance { get; set; }
        /// <summary>Severity of a FAIL/WARNING and the reason for that severity.</summary>
        public string SeverityBasis { get; set; }
        /// <summary>Objects excluded or reported differently, and how.</summary>
        public string Exceptions { get; set; }
        /// <summary>Alternatives an engineer may accept instead of the stated condition, with justification.</summary>
        public string AcceptableAlternatives { get; set; }
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

            var list = new List<RuleExplanation>
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
                    EngineerNote = "In an ETABS shell wall the in-plane bending stiffness of the wall (what ACI's 0.70 Ig means for a shear wall) comes from the MEMBRANE terms f22/f11; m11/m22/m12 are out-of-plane plate bending. So f22/f11 are the modifiers that apply the ACI factor to the lateral system. Side effects to accept knowingly: f22 also reduces axial stiffness (ACI keeps 1.0 Ag) and f12 reduces in-plane shear stiffness, which ACI's table does not address. The table is for strength-level lateral analysis; for service-level drift ACI permits stiffer values (check 6.6.3.2.2 in your edition). Walls designed as cracked out-of-plane (basement, retaining, tank) are typically f = 1.0 with m = 0.35 - see WALL-004."
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
                    RuleId = RuleIds.Slab002, Title = "Ordinary slab modifiers", Category = Categories.SlabProperties,
                    Type = RuleType.ProjectStandard, Table = KnownTables.SlabPropertyDefinitions,
                    WhatItChecks = "Ordinary slabs (S…, D… drop panels and anything not matched by another class prefix): checks m11, m22, m12. Also used for slab names that cannot be classified (WARNING).",
                    WhyItMatters = "A flat slab modelled at full stiffness attracts too much lateral load and under-predicts deflection.",
                    PassCondition = $"m11 = m22 = m12 = {Slab("Ordinary", 0.25)}.",
                    HowToFix = "Edit the slab property modifiers. If a slab is classified wrongly, fix its name prefix.",
                    Basis = "Consistent with ACI 318 Table 6.6.3.1.1, flat plates and flat slabs 0.25 Ig (confirm clause in your edition).",
                    EngineerNote = "Classification is by name prefix, so a mis-typed name changes the expected value - the result message names the class and prefix applied."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Slab004, Title = "Raft slab modifiers", Category = Categories.SlabProperties,
                    Type = RuleType.ModelingStandard, Table = KnownTables.SlabPropertyDefinitions,
                    WhatItChecks = "Slabs classified as raft / mat foundation by name prefix: checks m11, m22, m12.",
                    WhyItMatters = "Raft bending stiffness controls how column and wall loads spread into the soil springs; a cracked raft redistributes reactions and settlements.",
                    PassCondition = $"m11 = m22 = m12 = {Slab("Raft", 1.0)}.",
                    HowToFix = "Edit the raft slab property modifiers, or fix the name prefix if the slab is not a raft.",
                    Basis = "Office modelling standard added for the Raft classification found in the reference workbook (docs/ValidationMatrix.md, discrepancy 2) - pending confirmation as a firm project rule.",
                    EngineerNote = "Rafts on a Winkler/spring model are normally analysed uncracked for stiffness - check that matches the cracked-section assumption used for the foundation design."
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
                    WhatItChecks = "Compares the material's E1 with both ACI 318-19 equations: (a) Ec = wc^1.5 x 0.043 x sqrt(f'c), using the material's own density wc (kg/m3); and (b) Ec = 4700 x sqrt(f'c). All in MPa.",
                    WhyItMatters = "Ec governs all deformation, period and force distribution between stiff and flexible members.",
                    PassCondition = $"E1 within {N(ConcretePropertyRule.TolerancePercent)} % of EITHER equation. Outside both is a WARNING. Eq. (a) only applies for wc 1440-2560 kg/m3; Eq. (b) only for normalweight concrete.",
                    HowToFix = "Set E1 to one of the code values (ETABS's default concrete uses Eq. (a) with the unit weight), or document why a project-specific Ec (tests, long-term modification) is used.",
                    Basis = "ACI 318-19, 19.2.2.1(a) and (b).",
                    EngineerNote = "Eq. (a) gives about 11 % more than Eq. (b) at wc = 2450 kg/m3, so the two can legitimately differ; either is code-compliant. wc is taken from the material's Unit Mass (or Unit Weight / g). If that includes reinforcement (e.g. 25 kN/m3) Eq. (a) will be slightly high. Non-ACI code profiles are NOT_CHECKED. A deliberately modified Ec (creep, cracked or stiffened for drift) will warn - that is expected."
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
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Geo001, Title = "Wall panels are plain vertical quads", Category = Categories.Geometry,
                    Type = RuleType.ModelingStandard, Table = $"{KnownTables.WallObjectConnectivity} + {KnownTables.PointObjectConnectivity}",
                    WhatItChecks = "For every wall object: it has exactly 4 corner joints, the corners sit at exactly 2 elevations (2 on each), and the top edge is directly above the bottom edge in plan.",
                    WhyItMatters = "Triangles left over from trimmed openings, warped panels with corners at 3-4 elevations, and sloped or plan-offset panels mesh unpredictably and give unreliable pier forces.",
                    PassCondition = $"4 corners, 2 Z levels split 2/2, top edge over bottom edge (coordinate tolerance {N(c.Geometry.CoordinateToleranceMm)} mm).",
                    HowToFix = "Redraw the panel as a rectangle between two story levels, or split a stepped panel into rectangles. The group VChk_WallGeometry selects every failing panel.",
                    Basis = "Office modelling standard.",
                    EngineerNote = "Deliberately sloped walls (e.g. a raking wall) fail by design - confirm they are intended and modelled with the correct local axes."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Geo002, Title = "Columns are plumb", Category = Categories.Geometry,
                    Type = RuleType.ModelIntegrity, Table = $"{KnownTables.ColumnObjectConnectivity} + {KnownTables.PointObjectConnectivity}",
                    WhatItChecks = "For every ETABS column (design orientation Column), both end joints must have exactly the same X and the same Y. dX and dY are reported but never decide the verdict.",
                    WhyItMatters = "A column that leans carries its load down a leaning path and adds a horizontal thrust no one designed for. Off-plumb columns usually come from snapping to the wrong grid at one level.",
                    PassCondition = "dX = 0 and dY = 0 (no tolerance). A live run uses exact joint coordinates from the ETABS API, not the rounded table values.",
                    HowToFix = "Move the off-grid joint back onto the column line. The group VChk_ColumnPlumb selects each failing column and its two joints.",
                    Basis = "Modelling integrity.",
                    EngineerNote = "\"Column\" means what ETABS calls a column, not a geometric test - so this finds leaning columns, and braces are never flagged. Intentionally raked columns fail by design."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Geo003, Title = "Every column lands on a column below", Category = Categories.Geometry,
                    Type = RuleType.ModelIntegrity, Table = $"{KnownTables.ColumnObjectConnectivity}, {KnownTables.LinkObjectConnectivity}, {KnownTables.PointObjectConnectivity}, {KnownTables.TowerAndBaseStoryDefinitions}",
                    WhatItChecks = "For every exactly plumb column: its lower joint must be the top joint of another plumb column. Connectivity is joint identity, never coordinates - two separate joints at the same spot are NOT connected and are reported as such.",
                    WhyItMatters = "A column moved at an upper level and never put back dumps its load into whatever slab or beam passes through the joint. The model still runs; the load path is simply wrong, and ETABS does not say so.",
                    PassCondition = "The lower joint is the top of a column below, or a link there ties it to another column, or it is at base level (Tower and Base Story Definitions; lowest joint when that table is missing). No tolerance.",
                    HowToFix = "Re-stack the column on the one below, merge coincident joints (Edit > Merge Joints), or tie a deliberately offset column down with a link. VChk_ColumnContinuity selects the failing columns (and the unconnected joint of a coincident pair).",
                    Basis = "Modelling integrity.",
                    EngineerNote = "Columns landing on a transfer beam or a wall are reported like any other break, with what was found at the joint - decide from the group in the model whether each is intended. Leaning columns are skipped here and reported by GEO-002."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Geo004, Title = "One pier label = one connected wall run", Category = Categories.Geometry,
                    Type = RuleType.ModelIntegrity, Table = $"{KnownTables.AreaAssignmentsPierLabels} + {KnownTables.WallObjectConnectivity} + {KnownTables.PointObjectConnectivity}",
                    WhatItChecks = "For every pier label at every story: all wall panels carrying it must touch each other in plan, directly or through other panels of the same label.",
                    WhyItMatters = "ETABS integrates pier forces over every panel with the label. A label re-used on two unrelated wall segments produces one pier force for two walls - a wrong design for both.",
                    PassCondition = $"All panels form one connected group, panels touching when any corners are within {N(c.Geometry.PierAdjacencyToleranceMm)} mm in plan.",
                    HowToFix = "Give each separate wall segment its own pier label (Assign > Shell > Pier Label). VChk_PierContinuity selects every panel of each split label.",
                    Basis = "Modelling integrity.",
                    EngineerNote = "If a label you know is split passes, its text may differ between the segments (trailing space, case) - ETABS then already treats them as two piers."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Sup001, Title = "Supports on the lowest floor only", Category = Categories.Supports,
                    Type = RuleType.ModelIntegrity, Table = $"{KnownTables.JointAssignmentsRestraints} + {KnownTables.PointObjectConnectivity}",
                    WhatItChecks = "Every restrained joint must be at the lowest joint elevation in the model. In a foundation model (file name containing " +
                                   string.Join(" or ", (c.Geometry.FoundationModelNameTokens ?? new List<string>()).Select(t => $"'{t}'")) + ") no joint may be restrained at all.",
                    WhyItMatters = "A restraint left on an upper joint silently drains load out of the structure: the columns above unload and the model equilibrates on a support that does not exist - it makes the model look better than reality.",
                    PassCondition = $"Restrained joint Z within {N(c.Geometry.CoordinateToleranceMm)} mm of the lowest joint; none at all in a foundation model.",
                    HowToFix = "Select the joint (group VChk_Restraints) and Assign > Joint > Restraints: clear all.",
                    Basis = "Modelling integrity.",
                    EngineerNote = "Springs are not checked. ETABS returns no restraint table when nothing is restrained, so that case shows NOT CHECKED rather than PASS."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Load003, Title = "Line loads act in the Gravity direction", Category = Categories.Loads,
                    Type = RuleType.ModelingStandard, Table = "FrameObj.GetLoadDistributed (live ETABS only)",
                    WhatItChecks = "Every frame distributed FORCE load must be assigned in direction Gravity or Projected Gravity, and no frame may carry a distributed MOMENT load.",
                    WhyItMatters = "Line loads in these models are gravity actions (walls on beams, finishes, parapets). One in global X/Y, global Z or a local axis is a wrong pick in the assign dialog and adds an action no load case accounts for. Distributed moments (e.g. torsion from an eccentric façade load) are not allowed by the office standard.",
                    PassCondition = "Force load with direction = Gravity or Projected Gravity. Global Z fails on purpose, downward or not - it does not follow the model's gravity definition. Any distributed moment load fails, whatever its direction.",
                    HowToFix = "Re-assign the load with direction Gravity (Assign > Frame Loads > Distributed). For a moment load, remove it or model the eccentricity another way. Moment loads are not drawn by ETABS's default frame-load display - check the frame's load information. VChk_LineLoads selects the frames.",
                    Basis = "Office modelling standard.",
                    EngineerNote = "Genuine lateral line loads (e.g. a horizontal barrier load) fail by design - confirm them case by case. ETABS will not export these loads as a table, so Excel runs report NOT CHECKED."
                },
                new RuleExplanation
                {
                    RuleId = RuleIds.Load004, Title = "No gravity load or load set on walls", Category = Categories.Loads,
                    Type = RuleType.ModelingStandard, Table = $"AreaObj.GetLoadUniform (live ETABS only) + {KnownTables.AreaLoadAssignmentsUniformLoadSets}",
                    WhatItChecks = "No wall object may carry a uniform area load in the Gravity direction, nor a uniform load set.",
                    WhyItMatters = "Gravity load belongs on the floor that spans onto the wall. On a vertical shell it is spread over the wall's own surface - a large, invisible overload with no relation to any tributary area, ending up in pier forces and the foundation.",
                    PassCondition = "No Gravity / Projected Gravity uniform load and no uniform load set on the wall. Lateral loads (soil, water, wind) pass.",
                    HowToFix = "Delete the load from the wall and apply it to the floor (Assign > Shell Loads). VChk_WallLoads selects the walls.",
                    Basis = "Office modelling standard.",
                    EngineerNote = "Load sets are read from their table; uniform loads only from a live model. Whichever part could not be read is reported as NOT CHECKED."
                }
            };

            var details = Details(c);
            foreach (var e in list)
            {
                if (!details.TryGetValue(e.RuleId, out var d)) continue;
                e.Applicability = d.Applicability;
                e.Reference = d.Reference;
                e.Tolerance = d.Tolerance;
                e.SeverityBasis = d.Severity;
                e.Exceptions = d.Exceptions;
                e.AcceptableAlternatives = d.Alternatives;
            }
            return list;
        }

        public static RuleExplanation Find(string ruleId, ValidationProfileConfig config)
        {
            return All(config).FirstOrDefault(e => string.Equals(e.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));
        }

        public static string AuthorityOf(RuleType type)
        {
            switch (type)
            {
                case RuleType.Code: return "CODE REQUIREMENT";
                case RuleType.ProjectStandard: return "OFFICE / PROJECT STANDARD (profile-configurable, not a code requirement)";
                case RuleType.ModelingStandard: return "MODELLING STANDARD (office practice, not a code requirement)";
                case RuleType.Consistency: return "DATA CONSISTENCY (name vs defined value, not a code requirement)";
                case RuleType.ModelIntegrity: return "MODEL INTEGRITY (geometry / load path, not a code clause)";
                default: return "BEST PRACTICE (advisory)";
            }
        }

        /// <summary>
        /// Stamps every result with where its requirement comes from (authority, reference, tolerance,
        /// applicability), so a reviewer can trace any FAIL without relying on a property name.
        /// Results of rules without a catalog entry are marked as such rather than left blank.
        /// </summary>
        public static void Annotate(IEnumerable<ValidationResult> results, ValidationProfileConfig config)
        {
            var byId = All(config).ToDictionary(e => e.RuleId, StringComparer.OrdinalIgnoreCase);
            foreach (var r in results)
            {
                if (!byId.TryGetValue(r.RuleId ?? "", out var e))
                {
                    r.Authority = AuthorityOf(r.RuleType);
                    r.Justification = "No catalog entry for this rule - see the rule source.";
                    continue;
                }
                r.Authority = e.Authority;
                r.Justification = $"Requirement: {e.PassCondition} Reference: {e.Reference} Tolerance: {e.Tolerance} Applies to: {e.Applicability}";
            }
        }

        /// <summary>Per-rule applicability, reference, tolerance, severity, exceptions and alternatives.
        /// Thresholds are read from the active profile so the text matches what the run used.</summary>
        private static Dictionary<string, (string Applicability, string Reference, string Tolerance, string Severity, string Exceptions, string Alternatives)> Details(ValidationProfileConfig c)
        {
            const string NotACode = "No code clause - ";
            var exPrefixes = string.Join(", ", c.Walls.ExceptionPrefixes ?? new List<string>());
            return new Dictionary<string, (string, string, string, string, string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                [RuleIds.Wall001] = (
                    "Every wall property whose name follows <prefix><thickness>-C<grade> (e.g. W300-C40).",
                    NotACode + "office naming convention (docs/ValidationMatrix.md, 'Naming convention').",
                    "Exact: thickness in the name and the defined thickness are both whole millimetres.",
                    "HIGH: a wrong thickness changes stiffness, self weight and capacity; LOW warning when the name cannot be parsed.",
                    "Names that do not follow the convention -> WARNING (unparseable), never a guessed value.",
                    "Renaming the property to the true thickness is equally acceptable to changing the thickness."),
                [RuleIds.Wall002] = (
                    "Every wall property whose name carries -C<grade> and whose material is a concrete material with a parseable grade.",
                    NotACode + "office naming convention.",
                    $"f'c within {N(c.ConcreteMaterials.FcToleranceMpa)} MPa (grades differ by 5 MPa or more).",
                    "HIGH: wrong material = wrong Ec and capacity; LOW warning when the name cannot be parsed.",
                    "Unparseable wall or material names -> WARNING.",
                    "Renaming is acceptable when the assigned material is the design intent."),
                [RuleIds.Wall003] = (
                    $"Every wall property except names starting {exPrefixes} (see WALL-004). Checked modifiers: {string.Join(", ", c.Walls.CheckedModifiers ?? new List<string>())}.",
                    "Office standard value. Related code basis: ACI 318-19 Table 6.6.3.1.1(a) - walls 0.70 Ig uncracked, 0.35 Ig cracked (strength-level lateral analysis) - REFERENCE TO VERIFY in your copy of the code.",
                    "Exact match to the profile value (modifiers are entered, not computed).",
                    "MEDIUM: changes the distribution of lateral load and the drift, not member capacity directly.",
                    $"Prefixes {exPrefixes} are EXEMPT (reported, not checked).",
                    "0.35 on walls shown by analysis to crack (ACI 'cracked' value); stiffer values for service-level drift where the code permits - document the basis in the project profile."),
                [RuleIds.Wall004] = (
                    $"Wall properties whose name starts with {exPrefixes}.",
                    NotACode + "office exemption list (Walls.ExceptionPrefixes).",
                    "n/a (classification by name prefix, case-insensitive).",
                    "LOW, status EXEMPT: no automatic verdict; needs manual sign-off unless the profile accepts exemptions (Reporting.AcceptExemptions).",
                    "This IS the exception rule; the matched prefix is the whole basis, so review the names.",
                    "Typical intended values: f = 1.0 with m = 0.35 for walls designed cracked out-of-plane."),
                [RuleIds.Slab001] = (
                    "Slab properties classified PT by name prefix (" + Prefixes(c, "PT") + ").",
                    NotACode + "office standard; ACI 318-19 Table 6.6.3.1.1(a) has no post-tensioned entry (REFERENCE TO VERIFY).",
                    "Exact match to the profile value.",
                    "MEDIUM: affects deflection and the slab's share of lateral load.",
                    "Unclassifiable names -> WARNING (SLAB-002 id).",
                    "Uncracked (1.0) for service deflection of PT slabs is common practice - set it in the profile if that is the project basis."),
                [RuleIds.Slab002] = (
                    "Ordinary slabs (no other class prefix matched); also slab names that cannot be classified.",
                    "ACI 318-19 Table 6.6.3.1.1(a) flat plates and flat slabs 0.25 Ig - REFERENCE TO VERIFY.",
                    "Exact match to the profile value (0.001).",
                    "MEDIUM; LOW warning when the name cannot be classified.",
                    "The class is decided by the name prefix; a mis-named slab is checked against the wrong value - the result message names the class and prefix applied.",
                    "Beam-supported two-way slabs may justify a different value; record it in the profile."),
                [RuleIds.Slab004] = (
                    "Slabs classified Raft (" + Prefixes(c, "Raft") + ").",
                    NotACode + "office modelling standard, pending confirmation (docs/ValidationMatrix.md discrepancy 2).",
                    "Exact match to the profile value (0.001).",
                    "MEDIUM: changes soil reaction distribution and raft moments.",
                    "None beyond the name-prefix classification.",
                    "A cracked raft stiffness justified by the foundation design, recorded in the profile."),
                [RuleIds.Slab003] = (
                    "Slabs classified Stair (" + Prefixes(c, "Stair") + ") or Ramp (" + Prefixes(c, "Ramp") + ").",
                    NotACode + "office modelling standard (removes unintended bracing by inclined slabs).",
                    "Exact match to the profile value.",
                    "MEDIUM.",
                    "Names with leading characters before the prefix (e.g. '...RAMP200') are not classified - WARNING.",
                    "Releasing the stair/ramp connections instead of reducing stiffness, if modelled explicitly."),
                [RuleIds.Pier001] = (
                    "Every pier, every story (Pier Section Properties).",
                    NotACode + "model integrity: a pier is designed as one section per story.",
                    "Exact (both values come from the same ETABS geometry).",
                    "HIGH: an ill-defined design section gives an unreliable pier design.",
                    "None.",
                    "Deliberately tapered/stepped walls: split the pier label or document the exception."),
                [RuleIds.Pier002] = (
                    "Every pier, every story.",
                    NotACode + "model integrity.",
                    "Exact.",
                    "HIGH: thickness drives slenderness and shear capacity.",
                    "None.",
                    "As PIER-001."),
                [RuleIds.Modal001] = (
                    "Every Ritz modal case.",
                    $"Office standard ({N(c.Modal.MinimumTargetRatioPercent)} %), stricter than ASCE 7 12.9.1.1 (90 % of actual mass) - REFERENCE TO VERIFY in the adopted edition.",
                    "None (>= comparison on the entered target).",
                    "HIGH: insufficient modal mass under-estimates response-spectrum forces.",
                    "Eigen modal cases are not checked. This checks the TARGET setting, not the achieved participation.",
                    "Achieved participation >= 90 % per direction (from the Modal Participating Mass Ratios table) satisfies the code even if the target differs."),
                [RuleIds.MatRebar001] = (
                    "Rebar materials whose name matches a grade profile (" + string.Join(", ", c.RebarMaterials.GradeProfiles.Keys) + "). Other rebar materials are not checked.",
                    NotACode + "office grade table taken from the reference model; NOT verified against ASTM A615/A706 minimum tensile strengths (A615 Gr 420: 620 MPa, A706 Gr 420: 550 MPa - verify).",
                    $"{N(c.RebarMaterials.ToleranceMpa)} MPa on each of Fy, Fu, Fye, Fue.",
                    "CRITICAL: fy sets every flexural and shear capacity.",
                    "Unlisted grade names are not evaluated.",
                    "Values meeting the product standard minima for the specified grade, with the grade table updated in the project profile."),
                [RuleIds.MatConc001] = (
                    "Concrete materials named C<fc>/<fcu> (optional -TD suffix).",
                    NotACode + "office naming convention.",
                    $"{N(c.ConcreteMaterials.FcToleranceMpa)} MPa.",
                    "CRITICAL: a mislabelled strength hides under-strength concrete in every check that reads the name.",
                    "Names with other suffixes (-C, -S&B, -CB) are not parsed -> WARNING.",
                    "Renaming the material when the defined f'c is the design intent."),
                [RuleIds.MatConc002] = (
                    "Concrete materials with a Basic Mechanical Properties row. Eq. (a) only for wc 1440-2560 kg/m3; Eq. (b) only for normalweight.",
                    "ACI 318-19 19.2.2.1(a) and (b) - verified clause.",
                    $"{N(ConcretePropertyRule.TolerancePercent)} % of the nearer applicable equation (E1 is an entered value; 5 % covers rounding of wc and fc).",
                    "MEDIUM (WARNING, not FAIL): a deliberate project Ec is legitimate but must be documented.",
                    "Non-ACI code profile, or lightweight without a usable density -> NOT CHECKED.",
                    "Test-based Ec (ACI permits Ec from tests) or a long-term/cracked Ec for a specific analysis, documented in the design basis."),
                [RuleIds.Load001] = (
                    $"Load patterns of type '{c.Loads.DeadPatternTypeName}'.",
                    "Physical requirement (self weight = 1.0 x actual weight); load factors are applied in combinations (ACI 318-19 5.3 - REFERENCE TO VERIFY), never in the pattern.",
                    "Exact.",
                    "CRITICAL: every gravity load, seismic mass and foundation load scales with it.",
                    "Other pattern types are not checked (a stray multiplier on them is not detected).",
                    "None - self weight modelled by other means (e.g. explicit loads) must then use 0 here and be documented."),
                [RuleIds.Load002] = (
                    "Linear static load cases with a load name.",
                    NotACode + "office naming consistency.",
                    "Exact (case-insensitive).",
                    "LOW (WARNING): may be intentional.",
                    "Cases with several loads are only compared on the first.",
                    "Any intentional mapping, recorded in the model notes."),
                [RuleIds.Frame001] = (
                    "Every frame object (Frame Auto Mesh assignments).",
                    NotACode + "office modelling standard.",
                    "Exact (Yes/No).",
                    "MEDIUM: an unmeshed frame may not connect to members framing into it.",
                    "None.",
                    "Manual division of the frame at every intersection."),
                [RuleIds.Frame002] = (
                    c.Frames.RestrictIntegerOffsetCheckToColumns ? "Columns only (frames listed in Concrete Column Overwrites)." : "Every frame.",
                    NotACode + "office modelling standard.",
                    $"{N(c.Frames.IntegerTolerance)} mm from the nearest whole number.",
                    "LOW: a cleanliness indicator, rarely a safety issue by itself.",
                    "Beams are excluded by default (geometry-derived offsets are legitimate).",
                    "Fractional offsets traced to genuine geometry (e.g. sloping beams)."),
                [RuleIds.Col001] = (
                    "Every concrete column with an ACI 318-19 design overwrite row.",
                    NotACode + "office standard.",
                    "Exact text match.",
                    "MEDIUM: a fixed design section can hide an overstressed column.",
                    "None.",
                    "A fixed section deliberately used for a check, removed before issue."),
                [RuleIds.Dia001] = (
                    "Every diaphragm definition.",
                    NotACode + "office modelling standard; the design must be consistent with the diaphragm assumption of ASCE 7 12.3.1 - REFERENCE TO VERIFY.",
                    "Exact text match.",
                    "HIGH: changes how lateral load is distributed between vertical elements.",
                    "None.",
                    "Rigid diaphragm where ASCE 7 permits it (e.g. concrete slab without significant irregularity), recorded in the design basis."),
                [RuleIds.Geo001] = (
                    "Every wall object (Wall Object Connectivity).",
                    NotACode + "office modelling standard.",
                    $"{N(c.Geometry.CoordinateToleranceMm)} mm on coordinates.",
                    "MEDIUM: affects mesh quality and pier force reliability.",
                    "None.",
                    "Deliberately sloped walls with correct local axes."),
                [RuleIds.Geo002] = (
                    "Every object ETABS designates as a column.",
                    NotACode + "model integrity (load path).",
                    "Exact: dX = dY = 0 (live runs use exact API coordinates).",
                    "HIGH: unintended lean adds unaccounted horizontal thrust.",
                    "Braces are never checked.",
                    "Intentionally raked columns, confirmed case by case."),
                [RuleIds.Geo003] = (
                    "Every exactly plumb column.",
                    NotACode + "model integrity (load path).",
                    "Exact: joint identity, never coordinates.",
                    "HIGH: a broken column stack sends load into elements not designed for it.",
                    "Columns at base level pass; leaning columns are left to GEO-002.",
                    "Columns landing on a designed transfer beam or wall, confirmed case by case."),
                [RuleIds.Geo004] = (
                    "Every pier label at every story.",
                    NotACode + "model integrity (pier force integration).",
                    $"{N(c.Geometry.PierAdjacencyToleranceMm)} mm corner adjacency in plan.",
                    "HIGH: one pier force for two walls gives a wrong design for both.",
                    "None.",
                    "None - separate segments need separate labels."),
                [RuleIds.Sup001] = (
                    "Every restrained joint.",
                    NotACode + "model integrity (supports).",
                    $"{N(c.Geometry.CoordinateToleranceMm)} mm on elevation.",
                    "HIGH: a spurious support removes load from the structure.",
                    "Springs are not checked; foundation models (file name tokens) must have no restraints at all.",
                    "Deliberate intermediate supports (e.g. a podium tied to a stiff neighbour), documented."),
                [RuleIds.Load003] = (
                    "Every frame distributed load (live ETABS only).",
                    NotACode + "office modelling standard.",
                    "Exact (direction name).",
                    "HIGH: a load in the wrong direction is an unaccounted action.",
                    "Excel sources -> NOT CHECKED (data not exported).",
                    "Genuine lateral line loads, confirmed case by case."),
                [RuleIds.Load004] = (
                    "Every wall object (live ETABS for uniform loads; load sets from their table).",
                    NotACode + "office modelling standard.",
                    "Exact (presence of a gravity load / load set).",
                    "HIGH: an invisible overload in pier and foundation forces.",
                    "Lateral loads on walls (soil, water, wind) pass.",
                    "None for gravity loads; they belong on the floors."),
            };
        }

        private static string Prefixes(ValidationProfileConfig c, string cls) =>
            c.Slabs.Classifications != null && c.Slabs.Classifications.TryGetValue(cls, out var p) && p?.NamePrefixes != null && p.NamePrefixes.Count > 0
                ? string.Join(", ", p.NamePrefixes) : "no prefix configured";
    }
}
