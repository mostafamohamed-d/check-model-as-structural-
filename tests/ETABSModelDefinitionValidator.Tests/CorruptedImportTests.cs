using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ClosedXML.Excel;
using ETABSModelDefinitionValidator.Core;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Excel;
using ETABSModelDefinitionValidator.Rules;
using ETABSModelDefinitionValidator.UI.WinForms;

namespace ETABSModelDefinitionValidator.Tests
{
    /// <summary>
    /// Finding 02: finding a table is not proof it was imported correctly. Deliberately corrupted
    /// workbooks are run through the FULL pipeline (Excel -> import -> rules -> verdict) and must be
    /// rejected or marked incomplete - never passed. A clean baseline proves the fixtures can pass.
    /// </summary>
    public class CorruptedImportTests : IDisposable
    {
        private readonly List<string> _files = new List<string>();
        private static readonly string[] WallHeaders = { "Name", "Material", "Wall Thickness", "f11 Modifier", "f22 Modifier", "f12 Modifier", "m11 Modifier", "m22 Modifier", "m12 Modifier" };

        public void Dispose()
        {
            foreach (var f in _files) { try { File.Delete(f); File.Delete(Path.ChangeExtension(f, ".csv")); } catch { } }
        }

        /// <summary>Workbook with a wall table (2 walls, all 0.70) and the concrete table WALL-002 needs.
        /// <paramref name="mutate"/> corrupts it.</summary>
        private string Workbook(Action<XLWorkbook> mutate = null)
        {
            var path = Path.Combine(Path.GetTempPath(), $"corrupt-{Guid.NewGuid():N}.xlsx");
            _files.Add(path);
            using (var wb = new XLWorkbook())
            {
                var w = wb.Worksheets.Add("Walls");
                w.Cell(1, 1).Value = "TABLE:  " + KnownTables.WallPropertyDefinitionsSpecified;
                for (var c = 0; c < WallHeaders.Length; c++) w.Cell(2, c + 1).Value = WallHeaders[c];
                w.Cell(3, 3).Value = "mm"; // units row (blank first cell)
                WallRow(w, 4, "W300-C40", 300);
                WallRow(w, 5, "W350-C40", 350);

                var m = wb.Worksheets.Add("Concrete");
                m.Cell(1, 1).Value = "TABLE:  " + KnownTables.MaterialPropertiesConcreteData;
                m.Cell(2, 1).Value = "Material"; m.Cell(2, 2).Value = "Fc"; m.Cell(2, 3).Value = "LtWtConc";
                m.Cell(3, 2).Value = "MPa";
                m.Cell(4, 1).Value = "C40/50"; m.Cell(4, 2).Value = 40; m.Cell(4, 3).Value = "No";

                mutate?.Invoke(wb);
                wb.SaveAs(path);
            }
            return path;
        }

        private static void WallRow(IXLWorksheet w, int row, string name, double t)
        {
            w.Cell(row, 1).Value = name; w.Cell(row, 2).Value = "C40/50"; w.Cell(row, 3).Value = t;
            for (var c = 4; c <= 9; c++) w.Cell(row, c).Value = 0.7;
        }

        private static ValidationRunResult Run(string path) =>
            new ValidationRunner().RunAsync(path, ConfigurationLoader.LoadDefault(), new[] { Categories.WallProperties }, null, CancellationToken.None).GetAwaiter().GetResult();

        private static TableImportReport Walls(ValidationRunResult run) => run.Model.GetTableReport(KnownTables.WallPropertyDefinitionsSpecified);

        // ---- baseline ----------------------------------------------------------------------------

        [Fact]
        public void Baseline_clean_workbook_passes_so_the_corruption_tests_are_meaningful()
        {
            var run = Run(Workbook());
            Assert.Equal(TableImportState.Read, Walls(run).State);
            Assert.Equal(RunVerdict.Pass, run.Summary.Verdict);
        }

        // ---- corruptions: every one must end INCOMPLETE (or worse), never PASS ----------------------

        [Fact]
        public void Renamed_header_makes_the_table_schema_invalid_and_the_run_incomplete()
        {
            var run = Run(Workbook(wb => wb.Worksheet("Walls").Cell(2, 3).Value = "Wall Thick"));

            Assert.Equal(TableImportState.SchemaInvalid, Walls(run).State);
            Assert.Contains("WallThickness", Walls(run).MissingColumns);
            Assert.Empty(run.Model.WallProperties);
            Assert.DoesNotContain(run.Results, r => r.RuleId == RuleIds.Wall001 && r.Status == ValidationStatus.Pass);
            Assert.Equal(RunVerdict.Incomplete, run.Summary.Verdict);
        }

        [Fact]
        public void Missing_required_column_is_schema_invalid()
        {
            var run = Run(Workbook(wb => wb.Worksheet("Walls").Column(7).Delete())); // m11 Modifier gone

            Assert.Equal(TableImportState.SchemaInvalid, Walls(run).State);
            Assert.Contains("M11Modifier", Walls(run).MissingColumns);
            Assert.Equal(RunVerdict.Incomplete, run.Summary.Verdict);
        }

        [Fact]
        public void Text_in_a_numeric_column_rejects_that_record_and_the_run_is_incomplete()
        {
            var run = Run(Workbook(wb => wb.Worksheet("Walls").Cell(5, 3).Value = "abc"));

            Assert.Equal(TableImportState.Partial, Walls(run).State);
            Assert.Equal(1, Walls(run).RejectedCount);
            Assert.DoesNotContain(run.Model.WallProperties, w => w.Name == "W350-C40"); // never imported as thickness 0
            Assert.Equal(RunVerdict.Incomplete, run.Summary.Verdict);
        }

        [Fact]
        public void Blank_modifier_is_rejected_not_silently_defaulted_to_1()
        {
            var run = Run(Workbook(wb => wb.Worksheet("Walls").Cell(4, 7).Clear()));

            Assert.Equal(TableImportState.Partial, Walls(run).State);
            Assert.DoesNotContain(run.Model.WallProperties, w => w.Name == "W300-C40");
            Assert.Contains(Walls(run).Messages, m => m.Contains("M11Modifier is blank"));
            Assert.Equal(RunVerdict.Incomplete, run.Summary.Verdict);
        }

        [Fact]
        public void Blank_record_key_is_rejected()
        {
            var run = Run(Workbook(wb => wb.Worksheet("Walls").Cell(5, 1).Clear()));
            Assert.Equal(TableImportState.Partial, Walls(run).State);
            Assert.Equal(RunVerdict.Incomplete, run.Summary.Verdict);
        }

        [Fact]
        public void Duplicate_record_key_rejects_both_copies()
        {
            var run = Run(Workbook(wb => wb.Worksheet("Walls").Cell(5, 1).Value = "W300-C40"));

            Assert.Equal(TableImportState.SchemaInvalid, Walls(run).State); // both records rejected -> nothing usable
            Assert.Empty(run.Model.WallProperties);
            Assert.Equal(RunVerdict.Incomplete, run.Summary.Verdict);
        }

        [Fact]
        public void Negative_thickness_is_rejected()
        {
            var run = Run(Workbook(wb => wb.Worksheet("Walls").Cell(4, 3).Value = -300));
            Assert.Equal(TableImportState.Partial, Walls(run).State);
            Assert.Equal(RunVerdict.Incomplete, run.Summary.Verdict);
        }

        [Fact]
        public void Same_table_twice_is_ambiguous_and_not_used()
        {
            var run = Run(Workbook(wb =>
            {
                var copy = wb.Worksheet("Walls").CopyTo("Walls (2)");
                copy.Cell(4, 3).Value = 999;
            }));

            Assert.Equal(TableImportState.Ambiguous, Walls(run).State);
            Assert.Empty(run.Model.WallProperties);
            Assert.Equal(RunVerdict.Incomplete, run.Summary.Verdict);
        }

        [Fact]
        public void Table_with_zero_records_is_empty_not_proof_of_no_problems()
        {
            var run = Run(Workbook(wb => { var w = wb.Worksheet("Walls"); w.Row(5).Delete(); w.Row(4).Delete(); }));

            Assert.Equal(TableImportState.ReadEmpty, Walls(run).State);
            Assert.NotEqual(RunVerdict.Pass, run.Summary.Verdict);
            Assert.NotEqual(RunVerdict.PassWithWarnings, run.Summary.Verdict);
        }

        [Fact]
        public void Missing_table_is_incomplete()
        {
            var run = Run(Workbook(wb => wb.Worksheet("Walls").Delete()));
            Assert.Equal(TableImportState.Missing, Walls(run).State);
            Assert.Equal(RunVerdict.Incomplete, run.Summary.Verdict);
        }

        // ---- regressions from the run on the real reference export (DCG-P01-B01-ULT-v22.3-Rev33) -----

        [Fact]
        public void Placeholder_material_with_zero_E1_is_legitimate_not_corruption()
        {
            // The reference export lists a 'Dummy' material with E1 = 0 in Basic Mechanical Properties.
            // That table holds every material; a zero modulus on a placeholder must not make the run incomplete.
            var table = new RawTable { TableTitle = KnownTables.MaterialPropertiesBasicMechanicalProperties, WorksheetName = "S" };
            foreach (var k in new[] { "Material", "E1", "UnitWeight", "UnitMass" }) table.Columns.Add((k, k, table.Columns.Count + 1));
            var row = new RawRow { ExcelRowNumber = 4 };
            row.Values["Material"] = "Dummy"; row.Values["E1"] = 0.0; row.Values["UnitWeight"] = 0.0; row.Values["UnitMass"] = 0.0;
            table.Rows.Add(row);

            var (_, report) = TableValidator.Validate(table, TableSchemas.For(table.TableTitle));
            Assert.Equal(TableImportState.Read, report.State);
        }

        [Fact]
        public void Rule_with_usable_but_empty_input_needs_review_not_incomplete_and_never_passes()
        {
            // A model with no walls: wall tables read fine with 0 records. Not missing evidence, not proof either.
            var model = new ModelData();
            model.TableReports[KnownTables.WallPropertyDefinitionsSpecified] = new TableImportReport { Title = KnownTables.WallPropertyDefinitionsSpecified, State = TableImportState.ReadEmpty };
            var rules = new IValidationRule[] { new WallThicknessRule() };
            var results = new ValidationEngine().Run(model, new ValidationProfileConfig(), rules);

            var verdict = RunAssessment.Evaluate(results, model, rules).Verdict;
            Assert.Equal(RunVerdict.ReviewRequired, verdict);
        }

        // ---- the five table states are distinguished -------------------------------------------------

        [Fact]
        public void Missing_skipped_empty_unrecognized_and_read_are_distinct_states()
        {
            var path = Workbook(wb =>
            {
                var d = wb.Worksheets.Add("Diaphragms"); // present, but its category is not enabled -> Skipped
                d.Cell(1, 1).Value = "TABLE:  " + KnownTables.DiaphragmDefinitions;
                d.Cell(2, 1).Value = "Name"; d.Cell(2, 2).Value = "Rigidity Type";
                d.Cell(3, 1).Value = "D1"; d.Cell(3, 2).Value = "Semi-Rigid";
                var u = wb.Worksheets.Add("Other"); // not mapped by any rule -> Unrecognized
                u.Cell(1, 1).Value = "TABLE:  Story Definitions";
                u.Cell(2, 1).Value = "Story"; u.Cell(3, 1).Value = "L1";
            });

            var model = new ExcelModelDataProvider(path, null, new[] { KnownTables.WallPropertyDefinitionsSpecified, KnownTables.MaterialPropertiesConcreteData }).GetModelData();

            Assert.Equal(TableImportState.Read, model.GetTableReport(KnownTables.WallPropertyDefinitionsSpecified).State);
            Assert.Equal(TableImportState.Skipped, model.GetTableReport(KnownTables.DiaphragmDefinitions).State);
            Assert.Equal(TableImportState.Unrecognized, model.GetTableReport("Story Definitions").State);
            Assert.Equal(TableImportState.Missing, model.GetTableReport(KnownTables.SlabPropertyDefinitions).State);
            Assert.False(model.WasTableFound(KnownTables.DiaphragmDefinitions)); // skipped is not evidence
            Assert.False(model.WasTableFound(KnownTables.SlabPropertyDefinitions));
        }

        // ---- console runner: missing tables cannot give exit code 0 ---------------------------------

        [Fact]
        public void Console_runner_exit_code_is_not_0_when_required_tables_are_missing()
        {
            var path = Workbook(); // walls + concrete only: every other rule's table is missing
            var csv = Path.ChangeExtension(path, ".csv");

            var exit = ETABSModelDefinitionValidator.UI.Program.Main(new[] { path, ConfigPath(), csv });

            Assert.Equal(3, exit); // INCOMPLETE
            var verdict = File.ReadAllText(Path.ChangeExtension(csv, ".verdict.txt"));
            Assert.Contains("INCOMPLETE", verdict);
            File.Delete(Path.ChangeExtension(csv, ".verdict.txt"));
        }

        internal static string ConfigPath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "config", "default-validation-profile.json"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir.FullName, "config", "default-validation-profile.json");
        }
    }
}
