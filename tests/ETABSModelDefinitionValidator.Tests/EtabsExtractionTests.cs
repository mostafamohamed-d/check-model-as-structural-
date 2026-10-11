using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using ETABSModelDefinitionValidator.Core;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.EtabsApi;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.Tests
{
    /// <summary>
    /// Layer 1 (unit, no ETABS): table extraction through the REAL EtabsSession reflection code,
    /// driven against FakeDatabaseTables. Proves call sequence, fallbacks, units handling and
    /// value mapping. Does NOT prove a real ETABS returns these names/keys - that is the live probe.
    /// </summary>
    public class EtabsExtractionTests
    {
        private const string Walls = KnownTables.WallPropertyDefinitionsSpecified;
        private const string Slabs = KnownTables.SlabPropertyDefinitions;
        private const double Tol = 1e-9; // values pass through as text -> double; no arithmetic, so exact to parse precision

        private static FakeSapModel ModelWithWallsAndSlabs(ETABSv1.eUnits units = ETABSv1.eUnits.kN_m_C)
        {
            var sap = new FakeSapModel(units);
            var wallKeys = new[] { "Name", "Material", "WallThickness", "f11Mod", "f22Mod", "f12Mod", "m11Mod", "m22Mod", "m12Mod" };
            var wallNames = new[] { "Name", "Material", "Wall Thickness", "f11 Modifier", "f22 Modifier", "f12 Modifier", "m11 Modifier", "m22 Modifier", "m12 Modifier" };
            var walls = new FakeTable { Key = Walls, Name = Walls, FieldKeys = wallKeys, FieldNames = wallNames };
            walls.Records.Add(new[] { "W300-C40", "C40/50", "300", "0.7", "0.7", "0.7", "0.7", "0.7", "0.7" });
            walls.Records.Add(new[] { "W350-C40", "C40/50", "350", "0.7", "0.7", "0.7", "0.35", "0.7", "0.7" }); // m11 wrong
            walls.Records.Add(new[] { "BW400-C40", "C40/50", "400", "1", "1", "1", "1", "1", "1" });             // exempt

            var slabs = new FakeTable
            {
                Key = Slabs, Name = Slabs,
                FieldKeys = new[] { "Name", "Material", "SlabThickness", "m11Mod", "m22Mod", "m12Mod" },
                FieldNames = new[] { "Name", "Material", "Slab Thickness", "m11 Modifier", "m22 Modifier", "m12 Modifier" }
            };
            slabs.Records.Add(new[] { "S200-C40", "C40/50", "200", "0.25", "0.25", "0.25" });

            sap.DatabaseTables.Tables.Add(walls);
            sap.DatabaseTables.Tables.Add(slabs);
            sap.DatabaseTables.Tables.Add(new FakeTable { Key = "Story Definitions", Name = "Story Definitions", FieldKeys = new[] { "Story" }, FieldNames = new[] { "Story" } });
            return sap;
        }

        private static (ModelData Model, EtabsModelDataProvider Provider) Extract(FakeSapModel sap, params string[] titles)
        {
            var session = EtabsSession.FromSapModel(typeof(ETABSv1.eUnits).Assembly, sap, @"C:\t\bench.EDB");
            var provider = new EtabsModelDataProvider(session, titles.Length == 0 ? new[] { Walls, Slabs } : titles);
            return (provider.GetModelData(), provider);
        }

        // EX-01 -------------------------------------------------------------------------------
        [Fact]
        public void EX01_values_reach_the_domain_model_exactly()
        {
            var (model, provider) = Extract(ModelWithWallsAndSlabs());

            Assert.Equal(3, model.WallProperties.Count);
            var w = model.WallProperties.Single(x => x.Name == "W350-C40");
            Assert.Equal("C40/50", w.Material);
            Assert.InRange(w.ThicknessMm, 350 - Tol, 350 + Tol);
            Assert.InRange(w.M11Modifier, 0.35 - Tol, 0.35 + Tol);
            Assert.InRange(w.F11Modifier, 0.70 - Tol, 0.70 + Tol);

            Assert.Single(model.SlabProperties);
            Assert.InRange(model.SlabProperties[0].ThicknessMm, 200 - Tol, 200 + Tol);

            Assert.Equal("bench.EDB (live ETABS)", model.WorkbookName);
            Assert.Equal(ApiTableConverter.WorksheetLabel, w.Source.WorksheetName);
            Assert.Equal(2, w.Source.RowNumber); // record number in ETABS, for traceability
            Assert.Equal(3, provider.AvailableTables.Count);
        }

        // EX-02: regression for the field report "DatabaseTables.GetAllTables returned error code 1"
        [Fact]
        public void EX02_GetAllTables_error_1_falls_back_to_GetAvailableTables_and_still_extracts()
        {
            var sap = ModelWithWallsAndSlabs();
            sap.DatabaseTables.GetAllTablesReturn = 1;

            var (model, _) = Extract(sap);

            Assert.Equal(3, model.WallProperties.Count);
            Assert.Equal(new[] { "GetAllTables", "GetAvailableTables" }, sap.DatabaseTables.Calls.Take(2));
            Assert.DoesNotContain(model.ImportIssues, i => i.Level == ImportIssueLevel.Error);
        }

        // EX-03
        [Fact]
        public void EX03_when_no_listing_works_tables_are_requested_directly_by_name()
        {
            var sap = ModelWithWallsAndSlabs();
            sap.DatabaseTables.GetAllTablesReturn = 1;
            sap.DatabaseTables.GetAvailableTablesReturn = 1;

            var (model, provider) = Extract(sap);

            Assert.Equal(3, model.WallProperties.Count);
            Assert.Single(model.SlabProperties);
            Assert.Empty(provider.AvailableTables);
            Assert.Contains(model.ImportIssues, i => i.Level == ImportIssueLevel.Info && i.Message.Contains("requested directly by name") && i.Message.Contains("error code 1"));
        }

        // EX-04
        [Fact]
        public void EX04_build_that_rejects_null_by_ref_arrays_is_handled()
        {
            var sap = ModelWithWallsAndSlabs();
            sap.DatabaseTables.RejectNullArrays = true;

            var (model, _) = Extract(sap);

            Assert.Equal(3, model.WallProperties.Count);
            Assert.Equal("GetAllTables", sap.DatabaseTables.Calls[0]); // first attempt already succeeds (empty arrays, not null)
            Assert.Equal(1, sap.DatabaseTables.Calls.Count(c => c.StartsWith("GetAllTables")));
        }

        // EX-05
        [Fact]
        public void EX05_field_names_unavailable_and_keys_do_not_match_schema_makes_the_table_unusable()
        {
            // Previously the modifiers silently defaulted to 1.0 here ("m11Mod" is not "M11Modifier"),
            // which could make WALL-003 report on values ETABS never gave. Now: schema invalid,
            // nothing populated, dependent checks NOT_CHECKED, run incomplete.
            var sap = ModelWithWallsAndSlabs();
            sap.DatabaseTables.GetAllFieldsReturn = 1;

            var (model, provider) = Extract(sap, Walls);

            Assert.Contains(provider.ExtractedTables.Single().Columns, c => c.OriginalHeader == "WallThickness");
            var report = model.GetTableReport(Walls);
            Assert.Equal(TableImportState.SchemaInvalid, report.State);
            Assert.Contains("M11Modifier", report.MissingColumns);
            Assert.Empty(model.WallProperties);
            Assert.False(model.WasTableFound(Walls));
            Assert.All(new WallModifierRule().Validate(model, new ValidationProfileConfig()), r => Assert.Equal(ValidationStatus.NotChecked, r.Status));
        }

        // EX-06
        [Fact]
        public void EX06_one_failing_table_is_isolated_and_its_rules_report_not_checked()
        {
            var sap = ModelWithWallsAndSlabs();
            sap.DatabaseTables.FailDisplayFor.Add(Slabs);

            var (model, _) = Extract(sap);

            Assert.Equal(3, model.WallProperties.Count);
            Assert.Contains(Slabs, model.MissingTables);
            Assert.Contains(model.ImportIssues, i => i.Level == ImportIssueLevel.Error && i.Message.Contains(Slabs) && i.Message.Contains("error code"));

            var slabResults = new SlabModifierRule().Validate(model, new ValidationProfileConfig()).ToList();
            Assert.All(slabResults, r => Assert.Equal(ValidationStatus.NotChecked, r.Status));
        }

        // EX-07
        [Fact]
        public void EX07_reads_happen_in_N_mm_C_and_the_users_units_are_restored()
        {
            var sap = ModelWithWallsAndSlabs(ETABSv1.eUnits.kip_ft_F);

            Extract(sap);

            Assert.All(sap.DatabaseTables.UnitsDuringDisplayCalls, u => Assert.Equal(ETABSv1.eUnits.N_mm_C, u));
            Assert.Equal(ETABSv1.eUnits.kip_ft_F, sap.Units);
            Assert.Equal(2, sap.SetPresentUnitsCalls); // switch + restore
        }

        // EX-08
        [Fact]
        public void EX08_units_are_restored_even_when_a_read_throws()
        {
            var sap = ModelWithWallsAndSlabs(ETABSv1.eUnits.kN_m_C);
            sap.DatabaseTables.ThrowOnDisplayFor = Walls;

            var (model, _) = Extract(sap);

            Assert.Equal(ETABSv1.eUnits.kN_m_C, sap.Units);
            Assert.Contains(model.ImportIssues, i => i.Level == ImportIssueLevel.Error && i.Message.Contains("COM failure (simulated)"));
            Assert.Single(model.SlabProperties); // the other table still came through
        }

        // EX-09
        [Fact]
        public void EX09_model_already_in_N_mm_C_is_not_touched()
        {
            var sap = ModelWithWallsAndSlabs(ETABSv1.eUnits.N_mm_C);
            Extract(sap);
            Assert.Equal(0, sap.SetPresentUnitsCalls);
        }

        // EX-10
        [Fact]
        public void EX10_only_requested_tables_are_read_one_display_call_each()
        {
            var sap = ModelWithWallsAndSlabs();

            Extract(sap, Walls);

            var displayCalls = sap.DatabaseTables.Calls.Where(c => c.StartsWith("GetTableForDisplayArray")).ToList();
            Assert.Equal(new[] { "GetTableForDisplayArray:" + Walls }, displayCalls);
            Assert.DoesNotContain(sap.DatabaseTables.Calls, c => c.Contains("Story Definitions"));
        }

        // EX-11
        [Fact]
        public void EX11_table_not_offered_by_ETABS_is_missing_with_similar_names_suggested()
        {
            var sap = ModelWithWallsAndSlabs();
            sap.DatabaseTables.Tables.RemoveAll(t => t.Key == Walls);
            sap.DatabaseTables.Tables.Add(new FakeTable { Key = "Wall Property Definitions", Name = "Wall Property Definitions", FieldKeys = new[] { "Name" }, FieldNames = new[] { "Name" } });

            var (model, _) = Extract(sap);

            Assert.Contains(Walls, model.MissingTables);
            var issue = model.ImportIssues.Single(i => i.Message.Contains($"'{Walls}'"));
            Assert.Contains("'Wall Property Definitions'", issue.Message);
        }

        // EX-12
        [Fact]
        public void EX12_table_name_match_ignores_case_and_spacing()
        {
            var sap = ModelWithWallsAndSlabs();
            sap.DatabaseTables.Tables.Single(t => t.Key == Walls).Name = "WALL PROPERTY DEFINITIONS -  SPECIFIED";

            var (model, _) = Extract(sap);

            Assert.Equal(3, model.WallProperties.Count);
        }

        // EX-13
        [Fact]
        public void EX13_numbers_parse_the_same_on_a_comma_decimal_windows_locale()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE"); // "0.7" would be 7 if parsed with this culture
                var (model, _) = Extract(ModelWithWallsAndSlabs(), Walls);
                Assert.InRange(model.WallProperties[0].F11Modifier, 0.70 - Tol, 0.70 + Tol);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        // EX-14: end to end through the rules on extracted data
        [Fact]
        public void EX14_rules_on_extracted_data_give_the_expected_verdicts()
        {
            var (model, _) = Extract(ModelWithWallsAndSlabs());
            var results = new WallModifierRule().Validate(model, new ValidationProfileConfig()).ToList();

            Assert.Contains(results, r => r.ObjectName == "W350-C40" && r.Status == ValidationStatus.Fail);
            Assert.DoesNotContain(results, r => r.ObjectName == "W300-C40" && r.Status == ValidationStatus.Fail);
            Assert.Contains(results, r => r.ObjectName == "BW400-C40" && r.Status == ValidationStatus.Exempt);
        }

        // EX-16: regression - tables of unticked categories must not be reported as missing
        [Fact]
        public void EX16_unrequested_tables_are_skipped_not_missing_and_no_workbook_wording()
        {
            var (model, _) = Extract(ModelWithWallsAndSlabs(), Walls);

            Assert.Empty(model.MissingTables);
            Assert.Contains(Slabs, model.SkippedTables);
            Assert.DoesNotContain(model.ImportIssues, i => i.Message.Contains("workbook"));
            Assert.DoesNotContain(model.ImportIssues, i => i.Level != ImportIssueLevel.Info);
        }

        // EX-17..19: corrupted live data gets the same treatment as a corrupted Excel export
        [Fact]
        public void EX17_live_text_in_numeric_field_rejects_the_record_and_marks_the_table_partial()
        {
            var sap = ModelWithWallsAndSlabs();
            sap.DatabaseTables.Tables.Single(t => t.Key == Walls).Records[1][2] = "abc"; // W350 thickness

            var (model, _) = Extract(sap, Walls);

            var report = model.GetTableReport(Walls);
            Assert.Equal(TableImportState.Partial, report.State);
            Assert.Equal(1, report.RejectedCount);
            Assert.DoesNotContain(model.WallProperties, w => w.Name == "W350-C40");
            Assert.Equal(RunVerdict.Incomplete, RunAssessment.Evaluate(
                new ValidationEngine().Run(model, new ValidationProfileConfig(), new[] { new WallThicknessRule() }), model, new[] { new WallThicknessRule() }).Verdict);
        }

        [Fact]
        public void EX18_live_blank_modifier_is_rejected_not_defaulted()
        {
            var sap = ModelWithWallsAndSlabs();
            sap.DatabaseTables.Tables.Single(t => t.Key == Walls).Records[0][6] = ""; // W300 m11

            var (model, _) = Extract(sap, Walls);

            Assert.Equal(TableImportState.Partial, model.GetTableReport(Walls).State);
            Assert.DoesNotContain(model.WallProperties, w => w.Name == "W300-C40");
        }

        [Fact]
        public void EX19_live_table_that_failed_to_read_is_FailedToRead_not_missing()
        {
            var sap = ModelWithWallsAndSlabs();
            sap.DatabaseTables.FailDisplayFor.Add(Slabs);

            var (model, _) = Extract(sap);

            Assert.Equal(TableImportState.FailedToRead, model.GetTableReport(Slabs).State);
            Assert.Equal(TableImportState.Read, model.GetTableReport(Walls).State);
            Assert.False(model.WasTableFound(Slabs));
        }

        // EX-20/21: regression for the live log "(unsaved model)" + every table -96 - the app had attached
        // to an ETABS still at the CSI Account Sign In screen. Connect must fail fast with one clear message.
        [Fact]
        public void EX20_instance_with_no_model_and_no_table_listing_is_refused_at_connect()
        {
            var sap = ModelWithWallsAndSlabs();
            sap.DatabaseTables.GetAllTablesReturn = 1;
            sap.DatabaseTables.GetAvailableTablesReturn = 1;
            var session = EtabsSession.FromSapModel(typeof(ETABSv1.eUnits).Assembly, sap, modelPath: "");

            var ex = Assert.Throws<InvalidOperationException>(() => session.EnsureModelReady("CSI Account Sign In for ETABS"));
            Assert.Contains("no model open", ex.Message);
            Assert.Contains("CSI Account Sign In", ex.Message);
        }

        [Fact]
        public void EX21_unsaved_model_that_still_lists_its_tables_is_accepted()
        {
            var session = EtabsSession.FromSapModel(typeof(ETABSv1.eUnits).Assembly, ModelWithWallsAndSlabs(), modelPath: "");
            session.EnsureModelReady("ETABS Ultimate 22.3.0 - Untitled"); // must not throw
        }

        // EX-15
        [Fact]
        public void EX15_empty_table_is_found_not_missing()
        {
            var sap = ModelWithWallsAndSlabs();
            sap.DatabaseTables.Tables.Single(t => t.Key == Slabs).Records.Clear();

            var (model, _) = Extract(sap);

            Assert.Empty(model.SlabProperties);
            Assert.DoesNotContain(Slabs, model.MissingTables);
        }
    }
}
