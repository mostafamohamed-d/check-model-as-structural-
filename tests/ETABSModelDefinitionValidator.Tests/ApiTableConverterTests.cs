using System;
using System.Collections.Generic;
using System.Linq;
using ETABSModelDefinitionValidator.Core;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.EtabsApi;
using ETABSModelDefinitionValidator.Excel;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.Tests
{
    /// <summary>Layer 1 (unit): the live-ETABS path with synthetic arrays shaped like the
    /// GetTableForDisplayArray output. Does NOT prove ETABS returns these field names - that is a
    /// live-run check.</summary>
    public class ApiTableConverterTests
    {
        private static readonly string[] Keys = { "Name", "Material", "WallThickness", "m11Mod" };
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            ["Name"] = "Name", ["Material"] = "Material", ["WallThickness"] = "Wall Thickness", ["m11Mod"] = "m11 Modifier"
        };

        [Fact]
        public void Converts_flat_record_major_array_to_rows_with_normalized_headers()
        {
            var data = new[] { "W300-C40", "C40/50", "300", "0.7", "W350-C40", "C40/50", "350", "0.35" };

            var table = ApiTableConverter.Convert(KnownTables.WallPropertyDefinitionsSpecified, Keys, Names, 2, data);

            Assert.Equal(2, table.Rows.Count);
            Assert.Equal("W350-C40", table.Rows[1].GetString("Name"));
            Assert.Equal(350.0, table.Rows[1].GetDouble("WallThickness"));
            Assert.Equal(0.35, table.Rows[1].GetDouble("M11Modifier"));
            Assert.Equal(2, table.Rows[1].ExcelRowNumber);
        }

        [Fact]
        public void Blank_cells_are_absent_so_defaults_apply_like_excel()
        {
            var table = ApiTableConverter.Convert("T", Keys, Names, 1, new[] { "W300-C40", "C40/50", "300", "" });
            Assert.Null(table.Rows[0].Get("M11Modifier"));
            Assert.Equal(1.0, table.Rows[0].GetDouble("M11Modifier", 1));
        }

        [Fact]
        public void Inconsistent_array_length_is_an_error_not_a_silent_misalignment()
        {
            Assert.Throws<InvalidOperationException>(() =>
                ApiTableConverter.Convert("T", Keys, Names, 2, new[] { "a", "b", "c", "d", "e" }));
        }

        [Fact]
        public void Names_with_leading_zero_stay_text()
        {
            var table = ApiTableConverter.Convert("T", new[] { "Name" }, null, 1, new[] { "007" });
            Assert.Equal("007", table.Rows[0].GetString("Name"));
        }

        [Fact]
        public void Live_style_tables_produce_modeldata_and_rules_run_on_it()
        {
            // 0.35 m11 modifier on an ordinary wall: WALL-003 must FAIL; WALL-001 must PASS (300 mm).
            // Full set of wall columns - a table missing required columns is schema-invalid (see EX05).
            var keys = new[] { "Name", "Material", "WallThickness", "f11Mod", "f22Mod", "f12Mod", "m11Mod", "m22Mod", "m12Mod" };
            var names = new Dictionary<string, string>
            {
                ["Name"] = "Name", ["Material"] = "Material", ["WallThickness"] = "Wall Thickness",
                ["f11Mod"] = "f11 Modifier", ["f22Mod"] = "f22 Modifier", ["f12Mod"] = "f12 Modifier",
                ["m11Mod"] = "m11 Modifier", ["m22Mod"] = "m22 Modifier", ["m12Mod"] = "m12 Modifier"
            };
            var data = new[] { "W300-C40", "C40/50", "300", "0.7", "0.7", "0.7", "0.35", "0.7", "0.7" };
            var table = ApiTableConverter.Convert(KnownTables.WallPropertyDefinitionsSpecified, keys, names, 1, data);

            var model = ETABSModelDefinitionValidator.Excel.ExcelModelDataProvider.FromTables("test (live ETABS)", new List<RawTable> { table });

            Assert.Single(model.WallProperties);
            Assert.True(model.WasTableFound(KnownTables.WallPropertyDefinitionsSpecified));
            Assert.Contains(KnownTables.SlabPropertyDefinitions, model.MissingTables);

            var config = new ValidationProfileConfig();
            var thickness = new WallThicknessRule().Validate(model, config).Single();
            var modifiers = new WallModifierRule().Validate(model, config).ToList();

            Assert.Equal(ValidationStatus.Pass, thickness.Status);
            Assert.Contains(modifiers, r => r.Status == ValidationStatus.Fail);
        }
    }
}
