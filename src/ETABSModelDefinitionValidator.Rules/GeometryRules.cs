using System;
using System.Collections.Generic;
using System.Linq;
using ETABSModelDefinitionValidator.Core;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.Rules
{
    /// <summary>ETABS groups the failing objects of each check are written to - the same names the
    /// pier-validation tool and the Excel validator use, so every tool lands in the same groups.</summary>
    public static class FailureGroups
    {
        private static readonly Dictionary<string, string> ByRule = new Dictionary<string, string>
        {
            [RuleIds.Geo001] = "VChk_WallGeometry",
            [RuleIds.Geo002] = "VChk_ColumnPlumb",
            [RuleIds.Geo003] = "VChk_ColumnContinuity",
            [RuleIds.Geo004] = "VChk_PierContinuity",
            [RuleIds.Sup001] = "VChk_Restraints",
            [RuleIds.Load003] = "VChk_LineLoads",
            [RuleIds.Load004] = "VChk_WallLoads"
        };

        /// <summary>Group name for a rule, or null when the rule's findings are not model objects.</summary>
        public static string For(string ruleId) => ruleId != null && ByRule.TryGetValue(ruleId, out var g) ? g : null;
    }

    /// <summary>Shared plumbing for the object-level checks.</summary>
    internal static class GeometryCheck
    {
        /// <summary>One NOT_CHECKED result per required table that is missing; empty when all are there.</summary>
        public static List<ValidationResult> Missing(IValidationRule rule, ModelData model, params string[] tables)
        {
            return tables.Where(t => !model.WasTableFound(t)).Select(t => RuleResults.TableNotUsable(rule, t)).ToList();
        }

        public static Dictionary<string, PointObject> Points(ModelData model)
        {
            var map = new Dictionary<string, PointObject>(StringComparer.Ordinal);
            foreach (var p in model.PointObjects) map[p.UniqueName] = p;
            return map;
        }

        public static string Joint(Dictionary<string, PointObject> points, string name)
        {
            if (string.IsNullOrEmpty(name)) return "(none)";
            return points.TryGetValue(name, out var p) ? p.Display : $"unique {name}";
        }

        public static ValidationResult Result(IValidationRule rule, ValidationStatus status, string objectType, string objectName,
            string story, SourceLocation source, string message, EtabsObjectRefs objects, string expected = null, string actual = null)
        {
            var r = ValidationResult.Create(rule.RuleId, rule.Name, rule.Category, rule.Type, rule.DefaultSeverity, status,
                objectType, objectName, source, expected: expected, actual: actual, message: message, story: story);
            r.Objects = objects;
            return r;
        }

        public static EtabsObjectRefs Refs(IEnumerable<string> areas = null, IEnumerable<string> frames = null,
            IEnumerable<string> points = null, IEnumerable<string> links = null)
        {
            var refs = new EtabsObjectRefs();
            if (areas != null) refs.Areas.AddRange(areas.Where(n => !string.IsNullOrEmpty(n)).Distinct());
            if (frames != null) refs.Frames.AddRange(frames.Where(n => !string.IsNullOrEmpty(n)).Distinct());
            if (points != null) refs.Points.AddRange(points.Where(n => !string.IsNullOrEmpty(n)).Distinct());
            if (links != null) refs.Links.AddRange(links.Where(n => !string.IsNullOrEmpty(n)).Distinct());
            return refs;
        }

        public static string NameOf(string label, string uniqueName) => string.IsNullOrWhiteSpace(label) ? uniqueName : label;

        public static Dictionary<string, string> PierByArea(ModelData model)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var a in model.PierLabelAssignments)
                if (HasPier(a.PierName)) map[a.UniqueName] = a.PierName;
            return map;
        }

        public static bool HasPier(string pier) => !string.IsNullOrWhiteSpace(pier) && !pier.Equals("None", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// GEO-001: a wall panel ETABS can mesh and design predictably is a plain vertical quad - exactly
    /// 4 corner joints at exactly 2 elevations (2 on each), with the top edge directly above the
    /// bottom edge in plan. Triangles left from trimmed openings, warped/stepped panels and sloped or
    /// plan-offset panels give unreliable pier forces and meshing.
    /// </summary>
    public sealed class WallGeometryRule : IValidationRule
    {
        public string RuleId => RuleIds.Geo001;
        public string Name => "Wall geometry (4 points, 2 Z levels, plan match)";
        public string Category => Categories.Geometry;
        public RuleType Type => RuleType.ModelingStandard;
        public Severity DefaultSeverity => Severity.Medium;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.WallObjectConnectivity, KnownTables.PointObjectConnectivity, KnownTables.AreaAssignmentsPierLabels };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            var missing = GeometryCheck.Missing(this, model, KnownTables.WallObjectConnectivity, KnownTables.PointObjectConnectivity);
            if (missing.Count > 0) return missing;

            var tol = Math.Max(config.Geometry.CoordinateToleranceMm, 1e-9);
            var points = GeometryCheck.Points(model);
            var piers = GeometryCheck.PierByArea(model);
            var results = new List<ValidationResult>();

            foreach (var wall in model.WallObjects)
            {
                var problems = new List<string>();
                var corners = new List<PointObject>();
                foreach (var name in wall.PointNames)
                {
                    if (points.TryGetValue(name, out var p)) corners.Add(p);
                    else problems.Add($"corner joint {name} is not in '{KnownTables.PointObjectConnectivity}'");
                }

                if (wall.PointNames.Count != 4)
                    problems.Add($"{wall.PointNames.Count} corner point(s) instead of 4");

                var levels = GroupElevations(corners, tol);
                if (levels.Count != 2)
                    problems.Add($"corners sit at {levels.Count} distinct Z level(s) instead of 2 ({string.Join(", ", levels.Select(l => l.Z.ToString("0.###")))} mm)");
                else if (corners.Count == 4 && (levels[0].Points.Count != 2 || levels[1].Points.Count != 2))
                    problems.Add($"corner points split {levels[0].Points.Count}/{levels[1].Points.Count} between the two Z levels instead of 2/2");

                if (levels.Count == 2 && levels[0].Points.Count == levels[1].Points.Count && !PlanFootprintsMatch(levels[0].Points, levels[1].Points, tol))
                    problems.Add("top edge is not vertically above the bottom edge in plan (sloped or plan-offset panel)");

                var pier = piers.TryGetValue(wall.UniqueName, out var pn) ? $" [pier {pn}]" : "";
                results.Add(GeometryCheck.Result(this,
                    problems.Count == 0 ? ValidationStatus.Pass : ValidationStatus.Fail,
                    "Wall", GeometryCheck.NameOf(wall.Label, wall.UniqueName), wall.Story, wall.Source,
                    problems.Count == 0
                        ? $"Wall {wall.Display} is a vertical quad: 4 corners on 2 levels, top edge above bottom edge.{pier}"
                        : $"Wall {wall.Display}: {string.Join("; ", problems)}.{pier}",
                    GeometryCheck.Refs(areas: new[] { wall.UniqueName }),
                    expected: "4 corners, 2 Z levels (2/2), top above bottom",
                    actual: $"{wall.PointNames.Count} corners, {levels.Count} Z level(s)"));
            }

            return results;
        }

        private sealed class ZLevel
        {
            public double Z;
            public readonly List<PointObject> Points = new List<PointObject>();
        }

        private static List<ZLevel> GroupElevations(List<PointObject> corners, double tol)
        {
            var levels = new List<ZLevel>();
            foreach (var c in corners.OrderBy(c => c.Z))
            {
                var hit = levels.FirstOrDefault(l => Math.Abs(l.Z - c.Z) <= tol);
                if (hit == null) levels.Add(hit = new ZLevel { Z = c.Z });
                hit.Points.Add(c);
            }
            return levels;
        }

        /// <summary>Every point of one edge has a partner at the same plan location on the other (order independent).</summary>
        private static bool PlanFootprintsMatch(List<PointObject> a, List<PointObject> b, double tol)
        {
            var unmatched = new List<PointObject>(b);
            foreach (var p in a)
            {
                var i = unmatched.FindIndex(q => p.PlanDistanceTo(q) <= tol);
                if (i < 0) return false;
                unmatched.RemoveAt(i);
            }
            return unmatched.Count == 0;
        }
    }

    /// <summary>
    /// GEO-002: both joints of every ETABS column share the same X and Y - plain equality, no
    /// tolerance. dX/dY are reported so the size of the error is visible, but never decide the verdict.
    /// "Column" is ETABS's design orientation (the Column Object Connectivity table), not geometry:
    /// this check exists to find columns that are NOT vertical, and a geometric definition would hide them.
    /// </summary>
    public sealed class ColumnPlumbRule : IValidationRule
    {
        public string RuleId => RuleIds.Geo002;
        public string Name => "Column plumb (both joints at the same X and Y)";
        public string Category => Categories.Geometry;
        public RuleType Type => RuleType.ModelIntegrity;
        public Severity DefaultSeverity => Severity.High;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.ColumnObjectConnectivity, KnownTables.PointObjectConnectivity };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            var missing = GeometryCheck.Missing(this, model, KnownTables.ColumnObjectConnectivity, KnownTables.PointObjectConnectivity);
            if (missing.Count > 0) return missing;

            var points = GeometryCheck.Points(model);
            var results = new List<ValidationResult>();

            foreach (var column in model.FrameObjects.Where(f => f.Kind == FrameKind.Column))
            {
                var refs = GeometryCheck.Refs(frames: new[] { column.UniqueName }, points: new[] { column.PointI, column.PointJ });
                var name = GeometryCheck.NameOf(column.Label, column.UniqueName);

                if (!points.TryGetValue(column.PointI ?? "", out var i) || !points.TryGetValue(column.PointJ ?? "", out var j))
                {
                    results.Add(GeometryCheck.Result(this, ValidationStatus.Warning, "Column", name, column.Story, column.Source,
                        $"Column {column.Display}: an end joint ({column.PointI} / {column.PointJ}) is not in '{KnownTables.PointObjectConnectivity}', so plumbness could not be checked.", refs));
                    continue;
                }

                var bottom = i.Z <= j.Z ? i : j;
                var top = i.Z <= j.Z ? j : i;
                // Plain equality. No tolerance: same X and same Y, or it fails.
                if (i.X == j.X && i.Y == j.Y)
                {
                    results.Add(GeometryCheck.Result(this, ValidationStatus.Pass, "Column", name, column.Story, column.Source,
                        $"Column {column.Display} is plumb: both joints at X = {i.X:0.###}, Y = {i.Y:0.###} mm.", refs,
                        expected: "dX = 0, dY = 0", actual: "dX = 0, dY = 0"));
                    continue;
                }

                var dx = Math.Abs(j.X - i.X);
                var dy = Math.Abs(j.Y - i.Y);
                results.Add(GeometryCheck.Result(this, ValidationStatus.Fail, "Column", name, column.Story, column.Source,
                    $"Column {column.Display} is not plumb: bottom joint {bottom.Display} at Z = {bottom.Z:0.###} and top joint {top.Display} at Z = {top.Z:0.###} " +
                    $"do not share the same X and Y: dX = {dx:0.####} mm, dY = {dy:0.####} mm.", refs,
                    expected: "dX = 0, dY = 0", actual: $"dX = {dx:0.####}, dY = {dy:0.####} mm"));
            }

            return results;
        }
    }

    /// <summary>
    /// GEO-003: every column lands on a column below. The lower joint of every exactly-plumb column
    /// must BE the top joint of another column (joint identity, never coordinates - two distinct joints
    /// at the same spot are not connected in ETABS). Exceptions: the column is founded at base level,
    /// or a link at its lower joint ties it to another column. No tolerance anywhere.
    /// </summary>
    public sealed class ColumnContinuityRule : IValidationRule
    {
        private const double Eps = 1e-6; // a double-comparison epsilon (mm), not a tolerance

        public string RuleId => RuleIds.Geo003;
        public string Name => "Column continuity (every column lands on a column below)";
        public string Category => Categories.Geometry;
        public RuleType Type => RuleType.ModelIntegrity;
        public Severity DefaultSeverity => Severity.High;
        public IReadOnlyList<string> ApplicableTables => new[]
        {
            KnownTables.ColumnObjectConnectivity, KnownTables.PointObjectConnectivity, KnownTables.BeamObjectConnectivity,
            KnownTables.LinkObjectConnectivity, KnownTables.WallObjectConnectivity, KnownTables.FloorObjectConnectivity,
            KnownTables.TowerAndBaseStoryDefinitions
        };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            var missing = GeometryCheck.Missing(this, model, KnownTables.ColumnObjectConnectivity, KnownTables.PointObjectConnectivity);
            if (missing.Count > 0) return missing;

            var points = GeometryCheck.Points(model);
            var baseZ = model.BaseElevationMm ?? (model.PointObjects.Count > 0 ? model.PointObjects.Min(p => p.Z) : 0);
            var baseText = model.BaseElevationMm.HasValue ? "base level" : "lowest joint (no base level table)";
            var noLinks = !model.WasTableFound(KnownTables.LinkObjectConnectivity);

            var all = model.FrameObjects.Where(f => f.Kind == FrameKind.Column)
                .Select(c => new Col(c, points)).Where(c => c.HasJoints).ToList();
            var columns = all.Where(c => c.IsPlumb).ToList();
            var index = new JointIndex(model, columns, all);
            var results = new List<ValidationResult>();

            foreach (var column in columns)
            {
                var f = column.Frame;
                var refs = GeometryCheck.Refs(frames: new[] { f.UniqueName });
                var name = GeometryCheck.NameOf(f.Label, f.UniqueName);
                var joint = column.Bottom.UniqueName;

                if (column.Bottom.Z <= baseZ + Eps)
                {
                    results.Add(GeometryCheck.Result(this, ValidationStatus.Pass, "Column", name, f.Story, f.Source,
                        $"Column {f.Display} is founded at {baseText} (Z = {baseZ:0.###} mm).", refs));
                    continue;
                }

                var below = index.TopsAt(joint).FirstOrDefault(c => c != column);
                if (below != null)
                {
                    results.Add(GeometryCheck.Result(this, ValidationStatus.Pass, "Column", name, f.Story, f.Source,
                        $"Column {f.Display} lands on column {below.Frame.Display} at joint {column.Bottom.Display}.", refs));
                    continue;
                }

                var (link, far, linked) = index.LinkedColumn(joint, column);
                if (linked != null)
                {
                    results.Add(GeometryCheck.Result(this, ValidationStatus.Pass, "Column", name, f.Story, f.Source,
                        $"Column {f.Display} is tied by link '{link.Name}' to column {linked.Frame.Display}.", refs));
                    continue;
                }

                var parts = new List<string>
                {
                    $"Column {f.Display} starts at joint {column.Bottom.Display}, Z = {column.Bottom.Z:0.###} " +
                    $"({column.Bottom.Z - baseZ:0.###} mm above {baseText} Z = {baseZ:0.###}); that joint is not the top of any other column"
                };
                if (link != null)
                    parts.Add($"a link ('{link.Name}') is attached here, but its far end (joint {GeometryCheck.Joint(points, far)}) does not belong to another column");

                var coincident = columns.FirstOrDefault(c => c != column && c.Top.UniqueName != joint && c.Top.DistanceTo(column.Bottom) <= Eps);
                if (coincident != null)
                {
                    parts.Add($"the column below, {coincident.Frame.Display}, tops out at joint {coincident.Top.Display} while this column starts at joint " +
                              $"{column.Bottom.Display} - two separate joints {coincident.Top.DistanceTo(column.Bottom):0.####} mm apart, so the two columns are NOT connected");
                    refs.Points.Add(coincident.Top.UniqueName);
                }

                foreach (var leaning in index.AnyColumnEndsAt(joint).Where(c => c != column && !c.IsPlumb))
                    parts.Add($"{leaning.Frame.Display} meets this joint but was not counted as a column below: plan offset {leaning.PlanOffset:0.####} mm - a column must be exactly plumb (see {RuleIds.Geo002})");

                foreach (var rising in index.BottomsAt(joint).Where(c => c != column))
                    parts.Add($"column {rising.Frame.Display} also STARTS at this joint (it rises to Z = {rising.Top.Z:0.###}, so it does not carry this column) - two columns starting at one joint is a duplicate");

                var alsoHere = index.Describe(joint, f.UniqueName);
                parts.Add(string.IsNullOrEmpty(alsoHere) ? "nothing else is connected at this joint" : "also connected at this joint: " + alsoHere);
                if (noLinks) parts.Add($"('{KnownTables.LinkObjectConnectivity}' was not available, so link ties could not be considered)");

                results.Add(GeometryCheck.Result(this, ValidationStatus.Fail, "Column", name, f.Story, f.Source,
                    string.Join("; ", parts) + ".", refs, expected: "Lower joint = top joint of a column below, a link to a column, or base level",
                    actual: "No column below"));
            }

            var skipped = all.Count - columns.Count;
            if (columns.Count == 0)
            {
                results.Add(GeometryCheck.Result(this, ValidationStatus.Pass, "Model", "(all columns)", null, SourceLocation.Unknown,
                    skipped == 0 ? "The model has no columns - nothing to check." : $"No exactly plumb column to check ({skipped} leaning column(s) are reported by {RuleIds.Geo002}).", null));
            }
            return results;
        }

        private sealed class Col
        {
            public readonly FrameObject Frame;
            public readonly PointObject Bottom, Top;
            public bool HasJoints => Bottom != null && Top != null;
            public double PlanOffset => HasJoints ? Bottom.PlanDistanceTo(Top) : 0;
            public bool IsPlumb => HasJoints && PlanOffset <= Eps && Top.Z - Bottom.Z > Eps;

            public Col(FrameObject frame, Dictionary<string, PointObject> points)
            {
                Frame = frame;
                points.TryGetValue(frame.PointI ?? "", out var i);
                points.TryGetValue(frame.PointJ ?? "", out var j);
                if (i == null || j == null) return;
                Bottom = i.Z <= j.Z ? i : j;
                Top = i.Z <= j.Z ? j : i;
            }
        }

        private sealed class JointIndex
        {
            private readonly Dictionary<string, List<Col>> _tops, _bottoms, _anyEnds;
            private readonly Dictionary<string, List<LinkObject>> _links;
            private readonly Dictionary<string, List<FrameObject>> _frames;
            private readonly Dictionary<string, List<WallObject>> _areas;

            /// <param name="columns">Exactly plumb columns - the only ones that count as carrying another.</param>
            /// <param name="all">Every column, so a leaning one meeting the joint can be named.</param>
            public JointIndex(ModelData model, List<Col> columns, List<Col> all)
            {
                _tops = Group(columns, c => new[] { c.Top.UniqueName });
                _bottoms = Group(columns, c => new[] { c.Bottom.UniqueName });
                _anyEnds = Group(all, c => new[] { c.Bottom.UniqueName, c.Top.UniqueName });
                _links = Group(model.LinkObjects, l => new[] { l.Point1, l.Point2 });
                _frames = Group(model.FrameObjects, fr => new[] { fr.PointI, fr.PointJ });
                _areas = Group(model.WallObjects.Concat(model.FloorObjects), a => a.PointNames);
            }

            private static Dictionary<string, List<T>> Group<T>(IEnumerable<T> items, Func<T, IEnumerable<string>> joints)
            {
                var map = new Dictionary<string, List<T>>(StringComparer.Ordinal);
                foreach (var item in items)
                    foreach (var j in joints(item))
                    {
                        if (string.IsNullOrEmpty(j)) continue;
                        if (!map.TryGetValue(j, out var list)) map[j] = list = new List<T>();
                        if (!list.Contains(item)) list.Add(item);
                    }
                return map;
            }

            private static List<T> At<T>(Dictionary<string, List<T>> map, string joint) =>
                joint != null && map.TryGetValue(joint, out var list) ? list : new List<T>();

            public List<Col> TopsAt(string joint) => At(_tops, joint);
            public List<Col> BottomsAt(string joint) => At(_bottoms, joint);
            public IEnumerable<Col> EndsAt(string joint) => TopsAt(joint).Concat(BottomsAt(joint)).Distinct();
            public List<Col> AnyColumnEndsAt(string joint) => At(_anyEnds, joint);

            /// <summary>A link at this joint whose far end is an end joint of another column (below or alongside).</summary>
            public (LinkObject Link, string Far, Col Column) LinkedColumn(string joint, Col own)
            {
                LinkObject first = null;
                string firstFar = null;
                foreach (var link in At(_links, joint))
                {
                    var far = link.OtherEnd(joint);
                    if (string.IsNullOrEmpty(far)) continue;
                    if (first == null) { first = link; firstFar = far; }
                    var column = EndsAt(far).FirstOrDefault(c => c != own);
                    if (column != null) return (link, far, column);
                }
                return (first, firstFar, null);
            }

            /// <summary>Everything else meeting the joint, so a column landing on a beam or a wall is obvious without opening the model.</summary>
            public string Describe(string joint, string ownName)
            {
                var parts = new List<string>();
                var frames = At(_frames, joint).Where(f => f.UniqueName != ownName).Select(f => $"{f.Kind.ToString().ToLowerInvariant()} {f.Display}").ToList();
                if (frames.Count > 0) parts.Add($"{frames.Count} frame(s) [{string.Join(", ", frames.Take(4))}{(frames.Count > 4 ? ", ..." : "")}]");
                var areas = At(_areas, joint).Select(a => a.Display).ToList();
                if (areas.Count > 0) parts.Add($"{areas.Count} area(s) [{string.Join(", ", areas.Take(4))}{(areas.Count > 4 ? ", ..." : "")}]");
                return string.Join(", ", parts);
            }
        }
    }

    /// <summary>
    /// GEO-004: all wall panels sharing one pier label at one story form a single connected wall run.
    /// If not, the label has been re-used on two or more unrelated wall segments, and ETABS will
    /// integrate forces across them as if they were one pier.
    /// </summary>
    public sealed class PierContinuityRule : IValidationRule
    {
        public string RuleId => RuleIds.Geo004;
        public string Name => "Pier labels (one connected wall run per label)";
        public string Category => Categories.Geometry;
        public RuleType Type => RuleType.ModelIntegrity;
        public Severity DefaultSeverity => Severity.High;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.AreaAssignmentsPierLabels, KnownTables.WallObjectConnectivity, KnownTables.PointObjectConnectivity };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            var missing = GeometryCheck.Missing(this, model, KnownTables.AreaAssignmentsPierLabels, KnownTables.WallObjectConnectivity, KnownTables.PointObjectConnectivity);
            if (missing.Count > 0) return missing;

            var tol = config.Geometry.PierAdjacencyToleranceMm;
            var points = GeometryCheck.Points(model);
            var walls = model.WallObjects.GroupBy(w => w.UniqueName, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var results = new List<ValidationResult>();

            var groups = model.PierLabelAssignments
                .Where(a => GeometryCheck.HasPier(a.PierName) && walls.ContainsKey(a.UniqueName))
                .GroupBy(a => (a.Story, a.PierName))
                .OrderBy(g => g.Key.PierName, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Key.Story, StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var panels = group.Select(a => walls[a.UniqueName]).Distinct().ToList();
                var corners = panels.Select(p => p.PointNames.Where(points.ContainsKey).Select(n => points[n]).ToList()).ToList();
                var refs = GeometryCheck.Refs(areas: panels.Select(p => p.UniqueName));
                var source = group.First().Source;

                var parent = Enumerable.Range(0, panels.Count).ToArray();
                int Find(int x) => parent[x] == x ? x : (parent[x] = Find(parent[x]));
                for (var i = 0; i < panels.Count; i++)
                    for (var j = i + 1; j < panels.Count; j++)
                        if (Find(i) != Find(j) && corners[i].Any(a => corners[j].Any(b => a.PlanDistanceTo(b) <= tol)))
                            parent[Find(i)] = Find(j);

                var components = Enumerable.Range(0, panels.Count).GroupBy(Find).ToList();
                if (components.Count == 1)
                {
                    results.Add(GeometryCheck.Result(this, ValidationStatus.Pass, "Pier", group.Key.PierName, group.Key.Story, source,
                        $"Pier '{group.Key.PierName}' @ {group.Key.Story}: {panels.Count} wall panel(s) form one connected wall run.", refs,
                        expected: "1 connected wall run", actual: "1"));
                    continue;
                }

                var runs = components.Select(c => string.Join(", ", c.Select(i => panels[i].Display)));
                results.Add(GeometryCheck.Result(this, ValidationStatus.Fail, "Pier", group.Key.PierName, group.Key.Story, source,
                    $"Pier label '{group.Key.PierName}' @ {group.Key.Story} is split across {components.Count} disconnected wall groups: [{string.Join("] | [", runs)}].", refs,
                    expected: "1 connected wall run", actual: components.Count.ToString()));
            }

            if (results.Count == 0)
                results.Add(GeometryCheck.Result(this, ValidationStatus.Pass, "Model", "(all piers)", null, SourceLocation.Unknown,
                    "No wall panel carries a pier label - nothing to check.", null));
            return results;
        }
    }
}
