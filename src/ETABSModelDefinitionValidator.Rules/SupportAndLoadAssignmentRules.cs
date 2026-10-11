using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ETABSModelDefinitionValidator.Core;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.Rules
{
    /// <summary>
    /// SUP-001: supports belong at the lowest level of the model and nowhere else. A restraint left on
    /// an upper joint silently drains load out of the structure - one of the few modelling errors that
    /// makes a model look better than reality. Foundation models are stricter: the raft sits on
    /// springs, so no joint may be restrained at all. "Lowest level" is the lowest joint elevation in
    /// the model (handles pits and stepped foundations), not the story table.
    /// </summary>
    public sealed class JointRestraintRule : IValidationRule
    {
        public string RuleId => RuleIds.Sup001;
        public string Name => "Joint restraints (lowest floor only)";
        public string Category => Categories.Supports;
        public RuleType Type => RuleType.ModelIntegrity;
        public Severity DefaultSeverity => Severity.High;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.JointAssignmentsRestraints, KnownTables.PointObjectConnectivity };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            var missing = GeometryCheck.Missing(this, model, KnownTables.PointObjectConnectivity);
            if (missing.Count > 0) return missing;

            var fileName = Path.GetFileName(model.ModelFilePath ?? model.WorkbookName ?? "");
            var token = config.Geometry.FoundationModelNameTokens?.FirstOrDefault(t => !string.IsNullOrEmpty(t) && fileName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            var foundation = token != null;

            if (!model.WasTableFound(KnownTables.JointAssignmentsRestraints))
            {
                // ETABS returns no restraint table at all when no joint is restrained, so absence is
                // ambiguous: it cannot be told apart from a read failure.
                return new[]
                {
                    ValidationResult.Create(RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                        "Table", KnownTables.JointAssignmentsRestraints, SourceLocation.Unknown,
                        message: $"ETABS returned no '{KnownTables.JointAssignmentsRestraints}' table. Either no joint is restrained" +
                                 (foundation ? " (expected in this foundation model)" : "") + " or the table could not be read - check Display > Show Tables.")
                };
            }

            var points = GeometryCheck.Points(model);
            var tol = config.Geometry.CoordinateToleranceMm;
            var minZ = model.PointObjects.Count > 0 ? model.PointObjects.Min(p => p.Z) : 0;
            var results = new List<ValidationResult>();

            foreach (var joint in model.JointRestraints.Where(j => j.IsRestrained))
            {
                var refs = GeometryCheck.Refs(points: new[] { joint.UniqueName });
                var name = GeometryCheck.NameOf(joint.Label, joint.UniqueName);
                if (!points.TryGetValue(joint.UniqueName, out var p))
                {
                    results.Add(GeometryCheck.Result(this, ValidationStatus.Warning, "Joint", name, joint.Story, joint.Source,
                        $"Restrained joint {joint.Display} is not in '{KnownTables.PointObjectConnectivity}', so its elevation is unknown.", refs));
                    continue;
                }

                string fail = null;
                if (foundation)
                    fail = $"Joint {joint.Display} is restrained ({joint.RestraintText()}) at Z = {p.Z:0.###} mm. This is a foundation model ('{token}' in the model name): " +
                           "the raft is carried by springs, so no joint may be restrained.";
                else if (p.Z - minZ > tol)
                    fail = $"Joint {joint.Display} is restrained ({joint.RestraintText()}) at Z = {p.Z:0.###} mm, {p.Z - minZ:0.###} mm above the lowest floor " +
                           $"(Z = {minZ:0.###} mm). Supports are allowed on the lowest floor only.";

                results.Add(GeometryCheck.Result(this, fail == null ? ValidationStatus.Pass : ValidationStatus.Fail, "Joint", name, joint.Story, joint.Source,
                    fail ?? $"Joint {joint.Display} is restrained ({joint.RestraintText()}) on the lowest floor (Z = {p.Z:0.###} mm).", refs,
                    expected: foundation ? "No restraint (foundation model)" : $"Z = {minZ:0.###} mm (lowest floor)", actual: $"Z = {p.Z:0.###} mm"));
            }

            if (results.Count == 0)
                results.Add(GeometryCheck.Result(this, ValidationStatus.Pass, "Model", "(all joints)", null, SourceLocation.Unknown,
                    "No joint is restrained.", null));
            return results;
        }
    }

    /// <summary>
    /// LOAD-003: frame distributed loads represent gravity actions (wall loads on beams, finishes,
    /// parapets), so every one must be assigned in the Gravity direction. Global Z fails on purpose,
    /// downward or not: a gravity load must follow the model's gravity definition.
    /// </summary>
    public sealed class LineLoadDirectionRule : IValidationRule, IRequiresLiveData
    {
        public string RuleId => RuleIds.Load003;
        public string Name => "Line loads (Gravity direction only)";
        public string Category => Categories.Loads;
        public RuleType Type => RuleType.ModelingStandard;
        public Severity DefaultSeverity => Severity.High;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.BeamObjectConnectivity, KnownTables.ColumnObjectConnectivity };
        public LiveData RequiredLiveData => LiveData.LoadAssignments;

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (model.FrameDistributedLoads == null)
            {
                return new[] { LoadsNotRead(this, model) };
            }

            var frames = model.FrameObjects.GroupBy(f => f.UniqueName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var results = new List<ValidationResult>();

            foreach (var load in model.FrameDistributedLoads)
            {
                frames.TryGetValue(load.FrameName ?? "", out var frame);
                var display = frame?.Display ?? $"unique {load.FrameName}";
                var isMoment = load.LoadType == 2;
                var dir = LoadDirection.Describe(load.Direction);
                // A distributed moment can never act "in Gravity", so it always fails - by project decision
                // moment loads are not allowed on frames. Say so plainly: ETABS's default frame-load display
                // shows forces only, so an engineer told "must be Gravity" looks for a force that isn't there.
                var ok = !isMoment && LoadDirection.IsGravity(load.Direction);
                string message;
                if (ok) message = $"Line load '{load.LoadPattern}' on {display} acts in {dir}.";
                else if (isMoment)
                    message = $"Distributed MOMENT load '{load.LoadPattern}' on {display} about '{dir}' (coordinate system '{load.CoordinateSystem}', " +
                              $"value {load.Value1:0.###}/{load.Value2:0.###} N·mm/mm) - distributed moment loads are not allowed on frames. " +
                              "Check it in ETABS via the frame's Assignments/Loads information (moment loads are not drawn with the default force display).";
                else
                    message = $"Line load '{load.LoadPattern}' on {display} is assigned in direction '{dir}' (coordinate system '{load.CoordinateSystem}', " +
                              $"value {load.Value1:0.###}/{load.Value2:0.###} N/mm) - must be Gravity.";

                results.Add(GeometryCheck.Result(this, ok ? ValidationStatus.Pass : ValidationStatus.Fail,
                    frame?.Kind.ToString() ?? "Frame", GeometryCheck.NameOf(frame?.Label, load.FrameName), frame?.Story, frame?.Source ?? load.Source,
                    message, GeometryCheck.Refs(frames: new[] { load.FrameName }),
                    expected: isMoment ? "No distributed moment load" : "Gravity",
                    actual: isMoment ? $"Moment about {dir}" : dir));
            }

            if (results.Count == 0)
                results.Add(GeometryCheck.Result(this, ValidationStatus.Pass, "Model", "(all frames)", null, SourceLocation.Unknown, "No frame carries a distributed load.", null));
            return results;
        }

        internal static ValidationResult LoadsNotRead(IValidationRule rule, ModelData model) =>
            ValidationResult.Create(rule.RuleId, rule.Name, rule.Category, rule.Type, Severity.Medium, ValidationStatus.NotChecked,
                "Model", "(load assignments)", SourceLocation.Unknown, message: model.LoadAssignmentsNote);
    }

    /// <summary>
    /// LOAD-004: gravity load belongs on the floor that spans onto a wall, not on the wall itself. A
    /// gravity area load on a vertical shell is spread over the wall's own surface - an invisible
    /// overload with no relation to any tributary area. A uniform load set on a wall is the same
    /// mistake by another route. Lateral area loads (soil, water, wind) are legitimate and pass.
    /// </summary>
    public sealed class WallLoadRule : IValidationRule, IRequiresLiveData
    {
        public string RuleId => RuleIds.Load004;
        public string Name => "Wall loads (no gravity area load, no load set)";
        public string Category => Categories.Loads;
        public RuleType Type => RuleType.ModelingStandard;
        public Severity DefaultSeverity => Severity.High;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.WallObjectConnectivity, KnownTables.AreaLoadAssignmentsUniformLoadSets, KnownTables.AreaAssignmentsPierLabels };
        public LiveData RequiredLiveData => LiveData.LoadAssignments;

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            var missing = GeometryCheck.Missing(this, model, KnownTables.WallObjectConnectivity);
            if (missing.Count > 0) return missing;

            var haveLoads = model.AreaUniformLoads != null;
            var haveSets = model.WasTableFound(KnownTables.AreaLoadAssignmentsUniformLoadSets);
            if (!haveLoads && !haveSets) return new[] { LineLoadDirectionRule.LoadsNotRead(this, model) };

            var loads = (model.AreaUniformLoads ?? new List<AreaUniformLoad>()).ToLookup(l => l.AreaName, StringComparer.Ordinal);
            var sets = model.AreaLoadSets.Where(s => !string.IsNullOrWhiteSpace(s.LoadSet) && !s.LoadSet.Equals("None", StringComparison.OrdinalIgnoreCase))
                .ToLookup(s => s.UniqueName, StringComparer.Ordinal);
            var piers = GeometryCheck.PierByArea(model);
            var results = new List<ValidationResult>();

            if (!haveLoads) results.Add(LineLoadDirectionRule.LoadsNotRead(this, model));
            if (!haveSets)
                results.Add(ValidationResult.Create(RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                    "Table", KnownTables.AreaLoadAssignmentsUniformLoadSets, SourceLocation.Unknown,
                    message: $"'{KnownTables.AreaLoadAssignmentsUniformLoadSets}' was not available (ETABS returns none when no load set is assigned), so load sets on walls were not checked."));

            foreach (var wall in model.WallObjects)
            {
                var problems = new List<string>();
                var gravity = loads[wall.UniqueName].Where(l => LoadDirection.IsGravity(l.Direction)).ToList();
                if (gravity.Count > 0)
                    problems.Add($"carries a Gravity-direction area load ({string.Join(", ", gravity.Select(l => $"{l.LoadPattern} [{LoadDirection.Describe(l.Direction)}] = {l.Value:0.######} N/mm²"))}) - gravity load belongs on the floor, not on the wall");
                var wallSets = sets[wall.UniqueName].Select(s => s.LoadSet).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (wallSets.Count > 0)
                    problems.Add($"has a uniform load set assigned ({string.Join(", ", wallSets)}) - load sets must not be assigned to wall elements");

                var pier = piers.TryGetValue(wall.UniqueName, out var pn) ? $" [pier {pn}]" : "";
                results.Add(GeometryCheck.Result(this, problems.Count == 0 ? ValidationStatus.Pass : ValidationStatus.Fail,
                    "Wall", GeometryCheck.NameOf(wall.Label, wall.UniqueName), wall.Story, wall.Source,
                    problems.Count == 0 ? $"Wall {wall.Display} carries no gravity area load and no load set.{pier}"
                                        : $"Wall {wall.Display} {string.Join("; ", problems)}.{pier}",
                    GeometryCheck.Refs(areas: new[] { wall.UniqueName }),
                    expected: "No gravity area load, no load set", actual: problems.Count == 0 ? "None" : $"{gravity.Count} gravity load(s), {wallSets.Count} load set(s)"));
            }

            return results;
        }
    }
}
