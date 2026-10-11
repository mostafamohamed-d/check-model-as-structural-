using System.Collections.Generic;
using System.Linq;
using ETABSModelDefinitionValidator.Core;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.EtabsApi;
using ETABSModelDefinitionValidator.Excel;
using Xunit;

namespace ETABSModelDefinitionValidator.Tests
{
    /// <summary>The connectivity/assignment tables, using the field names ETABS 22.3 actually returned
    /// (probed live with --etabs-probe), mapped through the same path both sources use.</summary>
    public class ConnectivityTableImportTests
    {
        private static RawTable T(string title, string[] fields, params string[] data) =>
            ApiTableConverter.Convert(title, fields, fields.ToDictionary(f => f, f => f), data.Length / fields.Length, data);

        [Fact]
        public void Maps_points_walls_columns_links_piers_restraints_loadsets_and_base()
        {
            var tables = new List<RawTable>
            {
                T(KnownTables.PointObjectConnectivity, new[] { "UniqueName", "Is Auto Point", "Story", "PointBay", "IsSpecial", "X", "Y", "Z", "GUID" },
                    "27925", "No", "L44", "15731", "No", "65944.7", "85747.7", "174800", "g"),
                T(KnownTables.WallObjectConnectivity, new[] { "UniqueName", "Story", "WallBay", "UniquePt1", "UniquePt2", "UniquePt3", "UniquePt4", "Perimeter", "Area", "GUID" },
                    "64926", "L44", "W1522", "6462", "71977", "71978", "", "1", "1", "g"),
                T(KnownTables.ColumnObjectConnectivity, new[] { "Unique Name", "Story", "ColumnBay", "UniquePtI", "UniquePtJ", "Length", "GUID" },
                    "15726", "L43", "C65", "32594", "32655", "3500", "g"),
                T(KnownTables.LinkObjectConnectivity, new[] { "Name", "Story", "UniquePt1", "UniquePt2", "GUID" }, "K1", "B1", "50", "51", "g"),
                T(KnownTables.AreaAssignmentsPierLabels, new[] { "Story", "Label", "UniqueName", "Pier Name" }, "L44", "W1522", "64926", "T-CW14A_2"),
                T(KnownTables.JointAssignmentsRestraints, new[] { "Story", "Label", "UniqueName", "UX", "UY", "UZ", "RX", "RY", "RZ" },
                    "B2", "124", "448", "Yes", "Yes", "Yes", "No", "No", "No"),
                T(KnownTables.AreaLoadAssignmentsUniformLoadSets, new[] { "Story", "Label", "UniqueName", "Load Set" }, "L44", "F22322", "61112", "T16"),
                T(KnownTables.TowerAndBaseStoryDefinitions, new[] { "Tower", "Color", "GUID", "Notes", "BSName", "BSElev", "BSColor" }, "T1", "Y", "g", "", "B2", "-7475", "Y")
            };

            var model = ExcelModelDataProvider.FromTables("live", tables);

            var p = model.PointObjects.Single();
            Assert.Equal(("27925", "15731", 174800.0), (p.UniqueName, p.Label, p.Z));
            var w = model.WallObjects.Single();
            Assert.Equal(new[] { "6462", "71977", "71978" }, w.PointNames); // blank 4th corner = triangle
            Assert.Equal("W1522", w.Label);
            var c = model.FrameObjects.Single();
            Assert.Equal((FrameKind.Column, "C65", "32594", "32655"), (c.Kind, c.Label, c.PointI, c.PointJ));
            Assert.Equal(("50", "51"), (model.LinkObjects.Single().Point1, model.LinkObjects.Single().Point2));
            Assert.Equal("T-CW14A_2", model.PierLabelAssignments.Single().PierName);
            Assert.Equal("UX+UY+UZ", model.JointRestraints.Single().RestraintText());
            Assert.Equal("T16", model.AreaLoadSets.Single().LoadSet);
            Assert.Equal(-7475.0, model.BaseElevationMm);
            Assert.DoesNotContain(KnownTables.WallObjectConnectivity, model.UnrecognizedTables);
        }

        /// <summary>Regression (BHAD-EB-V22.3-REV75, live): floor F2206 has unique name "15E0" and a
        /// load set; wall W1858 has unique name "15". Pre-parsing cells as numbers turned "15E0" into
        /// 15, so LOAD-004 reported the floor's load set on the wall.</summary>
        [Fact]
        public void Unique_names_that_look_like_numbers_are_not_rewritten_so_a_floor_load_set_never_lands_on_a_wall()
        {
            var tables = new List<RawTable>
            {
                T(KnownTables.WallObjectConnectivity, new[] { "UniqueName", "Story", "WallBay", "UniquePt1", "UniquePt2", "UniquePt3", "UniquePt4", "Perimeter", "Area", "GUID" },
                    "15", "Story1", "W1858", "1", "2", "3", "4", "1", "1", "g"),
                T(KnownTables.AreaLoadAssignmentsUniformLoadSets, new[] { "Story", "Label", "UniqueName", "LoadSet" },
                    "GROUND FLOOR", "F2206", "15E0", "05-Residential-Type 1")
            };

            var model = ExcelModelDataProvider.FromTables("live", tables);

            Assert.Equal("15E0", model.AreaLoadSets.Single().UniqueName);
            Assert.Equal("15", model.WallObjects.Single().UniqueName);
        }

        [Fact]
        public void Numeric_cells_kept_as_text_still_read_as_numbers_including_exponent_form()
        {
            var table = T("T", new[] { "Name", "A1" }, "15E0", "9.9E-06");

            Assert.Equal("15E0", table.Rows[0].GetString("Name"));
            Assert.Equal(9.9E-06, table.Rows[0].GetDouble("A1"), 12);
        }
    }
}
