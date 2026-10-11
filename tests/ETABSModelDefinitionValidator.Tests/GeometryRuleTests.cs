using System.Collections.Generic;
using System.Linq;
using ETABSModelDefinitionValidator.Core;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;
using ETABSModelDefinitionValidator.UI.WinForms;
using Xunit;
using static ETABSModelDefinitionValidator.Tests.TestHelpers;

namespace ETABSModelDefinitionValidator.Tests
{
    /// <summary>Tiny model builder: joints by name, then walls/columns/links between them (mm).</summary>
    internal sealed class Geo
    {
        public readonly ModelData Model = new ModelData();

        public Geo Point(string name, double x, double y, double z)
        {
            Model.PointObjects.Add(new PointObject { UniqueName = name, Label = "J" + name, Story = "S", X = x, Y = y, Z = z, Source = Loc(KnownTables.PointObjectConnectivity) });
            return this;
        }

        public Geo Wall(string name, string story, params string[] points)
        {
            var w = new WallObject { UniqueName = name, Label = "W" + name, Story = story, Source = Loc(KnownTables.WallObjectConnectivity) };
            w.PointNames.AddRange(points);
            Model.WallObjects.Add(w);
            return this;
        }

        public Geo Column(string name, string i, string j, string story = "S")
        {
            Model.FrameObjects.Add(new FrameObject { UniqueName = name, Label = "C" + name, Story = story, Kind = FrameKind.Column, PointI = i, PointJ = j, Source = Loc(KnownTables.ColumnObjectConnectivity) });
            return this;
        }

        public Geo Link(string name, string p1, string p2)
        {
            Model.LinkObjects.Add(new LinkObject { Name = name, Point1 = p1, Point2 = p2 });
            return this;
        }

        public Geo Pier(string area, string story, string pier)
        {
            Model.PierLabelAssignments.Add(new PierLabelAssignment { UniqueName = area, Story = story, PierName = pier, Source = Loc(KnownTables.AreaAssignmentsPierLabels) });
            return this;
        }

        /// <summary>A rectangular wall panel in the XZ plane at Y = y, from x0 to x1, z0 to z1.</summary>
        public Geo RectWall(string name, string story, double x0, double x1, double y, double z0, double z1)
        {
            Point(name + "a", x0, y, z0).Point(name + "b", x1, y, z0).Point(name + "c", x1, y, z1).Point(name + "d", x0, y, z1);
            return Wall(name, story, name + "a", name + "b", name + "c", name + "d");
        }
    }

    public class GeometryRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();

        private List<ValidationResult> Run(IValidationRule rule, ModelData model) => rule.Validate(model, _config).ToList();

        // --- GEO-001 wall geometry -----------------------------------------------------------

        [Fact]
        public void WallGeometry_Pass_ForVerticalRectangle()
        {
            var g = new Geo().RectWall("1", "L1", 0, 3000, 0, 0, 3500);
            var r = Assert.Single(Run(new WallGeometryRule(), g.Model));
            Assert.Equal(ValidationStatus.Pass, r.Status);
            Assert.Equal(new[] { "1" }, r.Objects.Areas);
        }

        [Fact]
        public void WallGeometry_Fail_ForTriangle()
        {
            var g = new Geo().Point("a", 0, 0, 0).Point("b", 3000, 0, 0).Point("c", 0, 0, 3500).Wall("1", "L1", "a", "b", "c");
            var r = Assert.Single(Run(new WallGeometryRule(), g.Model));
            Assert.Equal(ValidationStatus.Fail, r.Status);
            Assert.Contains("3 corner point(s) instead of 4", r.Message);
        }

        [Fact]
        public void WallGeometry_Fail_ForStepped_ThreeLevels()
        {
            var g = new Geo().Point("a", 0, 0, 0).Point("b", 3000, 0, 500).Point("c", 3000, 0, 3500).Point("d", 0, 0, 3500).Wall("1", "L1", "a", "b", "c", "d");
            Assert.Equal(ValidationStatus.Fail, Run(new WallGeometryRule(), g.Model).Single().Status);
        }

        [Fact]
        public void WallGeometry_Fail_WhenTopEdgeOffsetInPlan()
        {
            var g = new Geo().Point("a", 0, 0, 0).Point("b", 3000, 0, 0).Point("c", 3000, 200, 3500).Point("d", 0, 200, 3500).Wall("1", "L1", "a", "b", "c", "d");
            var r = Run(new WallGeometryRule(), g.Model).Single();
            Assert.Equal(ValidationStatus.Fail, r.Status);
            Assert.Contains("not vertically above", r.Message);
        }

        [Fact]
        public void WallGeometry_Boundary_TinyZDifference_FailsWithZeroTolerance_PassesWithTolerance()
        {
            var g = new Geo().Point("a", 0, 0, 0).Point("b", 3000, 0, 0.5).Point("c", 3000, 0, 3500).Point("d", 0, 0, 3500).Wall("1", "L1", "a", "b", "c", "d");
            Assert.Equal(ValidationStatus.Fail, Run(new WallGeometryRule(), g.Model).Single().Status);

            _config.Geometry.CoordinateToleranceMm = 1;
            Assert.Equal(ValidationStatus.Pass, Run(new WallGeometryRule(), g.Model).Single().Status);
        }

        [Fact]
        public void WallGeometry_NotChecked_WhenTableMissing()
        {
            var model = new ModelData();
            model.MissingTables.Add(KnownTables.WallObjectConnectivity);
            var r = Assert.Single(Run(new WallGeometryRule(), model));
            Assert.Equal(ValidationStatus.NotChecked, r.Status);
        }

        // --- GEO-002 column plumb ------------------------------------------------------------

        [Fact]
        public void ColumnPlumb_Pass_WhenSameXY()
        {
            var g = new Geo().Point("1", 100, 200, 0).Point("2", 100, 200, 3500).Column("C", "1", "2");
            Assert.Equal(ValidationStatus.Pass, Run(new ColumnPlumbRule(), g.Model).Single().Status);
        }

        [Fact]
        public void ColumnPlumb_Fail_OnAnyOffset_NoTolerance()
        {
            var g = new Geo().Point("1", 100, 200, 0).Point("2", 100.0001, 200, 3500).Column("C", "1", "2");
            _config.Geometry.CoordinateToleranceMm = 50; // must not matter
            var r = Run(new ColumnPlumbRule(), g.Model).Single();
            Assert.Equal(ValidationStatus.Fail, r.Status);
            Assert.Contains("C", r.Objects.Frames);
            Assert.Equal(new[] { "1", "2" }, r.Objects.Points);
        }

        [Fact]
        public void ColumnPlumb_Warning_WhenJointMissing()
        {
            var g = new Geo().Point("1", 0, 0, 0).Column("C", "1", "missing");
            Assert.Equal(ValidationStatus.Warning, Run(new ColumnPlumbRule(), g.Model).Single().Status);
        }

        [Fact]
        public void ColumnPlumb_NotChecked_WhenColumnTableMissing()
        {
            var model = new ModelData();
            model.MissingTables.Add(KnownTables.ColumnObjectConnectivity);
            Assert.Equal(ValidationStatus.NotChecked, Run(new ColumnPlumbRule(), model).Single().Status);
        }

        // --- GEO-003 column continuity -------------------------------------------------------

        private static Geo TwoStack() => new Geo()
            .Point("0", 0, 0, 0).Point("1", 0, 0, 3500).Point("2", 0, 0, 7000)
            .Column("lower", "0", "1").Column("upper", "1", "2");

        [Fact]
        public void ColumnContinuity_Pass_WhenStacked_AndFoundedAtBase()
        {
            var results = Run(new ColumnContinuityRule(), TwoStack().Model);
            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal(ValidationStatus.Pass, r.Status));
        }

        [Fact]
        public void ColumnContinuity_Fail_WhenUpperColumnMoved()
        {
            var g = new Geo().Point("0", 0, 0, 0).Point("1", 0, 0, 3500).Point("1b", 500, 0, 3500).Point("2", 500, 0, 7000)
                .Column("lower", "0", "1").Column("upper", "1b", "2");
            var r = Run(new ColumnContinuityRule(), g.Model).Single(x => x.ObjectName == "Cupper");
            Assert.Equal(ValidationStatus.Fail, r.Status);
            Assert.Contains("not the top of any other column", r.Message);
        }

        [Fact]
        public void ColumnContinuity_Fail_NamesCoincidentButSeparateJoints()
        {
            var g = new Geo().Point("0", 0, 0, 0).Point("1", 0, 0, 3500).Point("1dup", 0, 0, 3500).Point("2", 0, 0, 7000)
                .Column("lower", "0", "1").Column("upper", "1dup", "2");
            var r = Run(new ColumnContinuityRule(), g.Model).Single(x => x.ObjectName == "Cupper");
            Assert.Equal(ValidationStatus.Fail, r.Status);
            Assert.Contains("NOT connected", r.Message);
            Assert.Contains("1", r.Objects.Points);
        }

        [Fact]
        public void ColumnContinuity_Pass_WhenTiedDownByLink()
        {
            var g = new Geo().Point("0", 0, 0, 0).Point("1", 0, 0, 3500).Point("1b", 500, 0, 3500).Point("2", 500, 0, 7000)
                .Column("lower", "0", "1").Column("upper", "1b", "2").Link("K1", "1b", "1");
            Assert.All(Run(new ColumnContinuityRule(), g.Model), r => Assert.Equal(ValidationStatus.Pass, r.Status));
        }

        [Fact]
        public void ColumnContinuity_LeaningColumnBelow_DoesNotCarry()
        {
            var g = new Geo().Point("0", 10, 0, 0).Point("1", 0, 0, 3500).Point("2", 0, 0, 7000)
                .Column("lower", "0", "1").Column("upper", "1", "2");
            var r = Run(new ColumnContinuityRule(), g.Model).Single(x => x.ObjectName == "Cupper");
            Assert.Equal(ValidationStatus.Fail, r.Status);
            Assert.Contains("must be exactly plumb", r.Message);
        }

        [Fact]
        public void ColumnContinuity_UsesBaseElevationTable()
        {
            var g = new Geo().Point("1", 0, 0, -2000).Point("2", 0, 0, 1500).Point("x", 9, 9, -5000).Column("C", "1", "2");
            g.Model.BaseElevationMm = -2000;
            Assert.Equal(ValidationStatus.Pass, Run(new ColumnContinuityRule(), g.Model).Single().Status);

            g.Model.BaseElevationMm = null; // falls back to the lowest joint (-5000): no longer founded
            Assert.Equal(ValidationStatus.Fail, Run(new ColumnContinuityRule(), g.Model).Single().Status);
        }

        // --- GEO-004 pier continuity ---------------------------------------------------------

        [Fact]
        public void PierContinuity_Pass_WhenPanelsTouch()
        {
            var g = new Geo().RectWall("A", "L1", 0, 1000, 0, 0, 3500).RectWall("B", "L1", 1000, 2000, 0, 0, 3500)
                .Pier("A", "L1", "P1").Pier("B", "L1", "P1");
            var r = Run(new PierContinuityRule(), g.Model).Single();
            Assert.Equal(ValidationStatus.Pass, r.Status);
            Assert.Equal(2, r.Objects.Areas.Count);
        }

        [Fact]
        public void PierContinuity_Fail_WhenLabelReusedOnSeparateWalls()
        {
            var g = new Geo().RectWall("A", "L1", 0, 1000, 0, 0, 3500).RectWall("B", "L1", 5000, 6000, 0, 0, 3500)
                .Pier("A", "L1", "P1").Pier("B", "L1", "P1");
            var r = Run(new PierContinuityRule(), g.Model).Single();
            Assert.Equal(ValidationStatus.Fail, r.Status);
            Assert.Contains("split across 2", r.Message);
        }

        [Fact]
        public void PierContinuity_Boundary_GapWithinTolerance_Passes()
        {
            var g = new Geo().RectWall("A", "L1", 0, 1000, 0, 0, 3500).RectWall("B", "L1", 1019, 2000, 0, 0, 3500)
                .Pier("A", "L1", "P1").Pier("B", "L1", "P1");
            Assert.Equal(ValidationStatus.Pass, Run(new PierContinuityRule(), g.Model).Single().Status);
            _config.Geometry.PierAdjacencyToleranceMm = 10;
            Assert.Equal(ValidationStatus.Fail, Run(new PierContinuityRule(), g.Model).Single().Status);
        }

        [Fact]
        public void PierContinuity_SameLabelOnDifferentStories_IsTwoGroups()
        {
            var g = new Geo().RectWall("A", "L1", 0, 1000, 0, 0, 3500).RectWall("B", "L2", 5000, 6000, 0, 3500, 7000)
                .Pier("A", "L1", "P1").Pier("B", "L2", "P1").Pier("A", "L1", "None");
            Assert.All(Run(new PierContinuityRule(), g.Model), r => Assert.Equal(ValidationStatus.Pass, r.Status));
        }

        [Fact]
        public void PierContinuity_NotChecked_WhenPierTableMissing()
        {
            var model = new ModelData();
            model.MissingTables.Add(KnownTables.AreaAssignmentsPierLabels);
            Assert.Equal(ValidationStatus.NotChecked, Run(new PierContinuityRule(), model).Single().Status);
        }

        // --- Failure groups ------------------------------------------------------------------

        [Fact]
        public void FailureGroups_CollectOnlyFailures_AndKeepEmptyGroupsForChecksThatPassed()
        {
            var g = new Geo().RectWall("A", "L1", 0, 1000, 0, 0, 3500).RectWall("B", "L1", 5000, 6000, 0, 0, 3500)
                .Pier("A", "L1", "P1").Pier("B", "L1", "P1");
            var results = Run(new PierContinuityRule(), g.Model).Concat(Run(new WallGeometryRule(), g.Model)).ToList();

            var groups = ValidationRunner.BuildFailureGroups(results, new[] { RuleIds.Geo004, RuleIds.Geo001, RuleIds.Wall001 });

            Assert.Equal(new[] { "A", "B" }, groups["VChk_PierContinuity"].Areas.OrderBy(a => a));
            Assert.Equal(0, groups["VChk_WallGeometry"].Count);
            Assert.Equal(2, groups.Count); // WALL-001 has no group
        }
    }
}
