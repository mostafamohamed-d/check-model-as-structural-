using System.Collections.Generic;
using System.Linq;
using ETABSModelDefinitionValidator.Core;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.Tests
{
    /// <summary>
    /// Finding 01 (report trustworthiness): a run may only be PASS when every required check
    /// completed, nothing failed, and no high-severity warning / exemption / empty table is unresolved.
    /// </summary>
    public class RunVerdictTests
    {
        private static ValidationResult R(string ruleId, ValidationStatus status, Severity severity = Severity.Medium) =>
            ValidationResult.Create(ruleId, ruleId, "Cat", RuleType.ModelingStandard, severity, status, "Obj", "o1", SourceLocation.Unknown, message: $"{ruleId} {status}");

        private static List<ValidationResult> TenPasses() => Enumerable.Range(0, 10).Select(i => R("WALL-001", ValidationStatus.Pass)).ToList();

        // ---- ACCEPTANCE: zero failures + one required NOT_CHECKED cannot pass -------------------------

        [Fact]
        public void Acceptance_zero_failures_and_one_required_NOT_CHECKED_is_not_PASS_and_exit_code_is_not_0()
        {
            // Real rules on a real model: 10 walls that pass WALL-001, diaphragm table missing.
            var model = new ModelData();
            for (var i = 0; i < 10; i++)
                model.WallProperties.Add(new WallProperty { Name = $"W{300 + i * 50}-C40", ThicknessMm = 300 + i * 50, Source = TestHelpers.Loc("Walls") });
            model.TableReports[KnownTables.WallPropertyDefinitionsSpecified] = new TableImportReport { Title = KnownTables.WallPropertyDefinitionsSpecified, State = TableImportState.Read, RecordCount = 10, AcceptedCount = 10 };
            model.TableReports[KnownTables.DiaphragmDefinitions] = new TableImportReport { Title = KnownTables.DiaphragmDefinitions, State = TableImportState.Missing };
            model.MissingTables.Add(KnownTables.DiaphragmDefinitions);

            var rules = new IValidationRule[] { new WallThicknessRule(), new DiaphragmRigidityRule() };
            var results = new ValidationEngine().Run(model, new ValidationProfileConfig(), rules);

            Assert.Equal(10, results.Count(r => r.Status == ValidationStatus.Pass));
            Assert.Equal(0, results.Count(r => r.Status == ValidationStatus.Fail));
            Assert.Single(results, r => r.RuleId == RuleIds.Dia001 && r.Status == ValidationStatus.NotChecked);

            var summary = ValidationSummary.From(results, model, rules, new ReportingRulesConfig());
            Assert.NotEqual(RunVerdict.Pass, summary.Verdict);
            Assert.Equal(RunVerdict.Incomplete, summary.Verdict);
            Assert.NotEqual(0, summary.Assessment.ExitCode);
            Assert.Equal(3, summary.Assessment.ExitCode);
            Assert.Contains(summary.Assessment.IncompleteReasons, r => r.Contains("DIA-001"));
            Assert.Contains(summary.Assessment.IncompleteReasons, r => r.Contains(KnownTables.DiaphragmDefinitions));
        }

        [Fact]
        public void Acceptance_results_only_summary_also_never_passes_with_NOT_CHECKED()
        {
            var results = TenPasses();
            results.Add(R(RuleIds.Dia001, ValidationStatus.NotChecked));

            var summary = ValidationSummary.From(results);

            Assert.Equal(RunVerdict.Incomplete, summary.Verdict);
            Assert.NotEqual(0, summary.Assessment.ExitCode);
        }

        // ---- the rest of the verdict contract ---------------------------------------------------------

        [Fact]
        public void All_pass_is_PASS_exit_0()
        {
            var a = RunAssessment.Evaluate(TenPasses());
            Assert.Equal(RunVerdict.Pass, a.Verdict);
            Assert.Equal(0, a.ExitCode);
        }

        [Fact]
        public void Optional_rule_NOT_CHECKED_does_not_block_but_is_still_counted()
        {
            var results = TenPasses();
            results.Add(R(RuleIds.Load003, ValidationStatus.NotChecked));
            var a = RunAssessment.Evaluate(results, policy: new ReportingRulesConfig { OptionalRules = { RuleIds.Load003 } });
            Assert.Equal(RunVerdict.Pass, a.Verdict);
            Assert.Equal(1, ValidationSummary.From(results).NotCheckedCount);
        }

        [Fact]
        public void Exempt_needs_review_by_default_and_passes_only_when_the_profile_accepts_exemptions()
        {
            var results = TenPasses();
            results.Add(R(RuleIds.Wall004, ValidationStatus.Exempt, Severity.Low));

            Assert.Equal(RunVerdict.ReviewRequired, RunAssessment.Evaluate(results).Verdict);
            Assert.Equal(4, RunAssessment.Evaluate(results).ExitCode);
            Assert.Equal(RunVerdict.Pass, RunAssessment.Evaluate(results, policy: new ReportingRulesConfig { AcceptExemptions = true }).Verdict);
        }

        [Fact]
        public void High_severity_warning_needs_review_low_severity_warning_passes_with_warnings()
        {
            var high = TenPasses(); high.Add(R(RuleIds.Dia001, ValidationStatus.Warning, Severity.High));
            var low = TenPasses(); low.Add(R(RuleIds.Load002, ValidationStatus.Warning, Severity.Low));

            Assert.Equal(RunVerdict.ReviewRequired, RunAssessment.Evaluate(high).Verdict);
            Assert.Equal(RunVerdict.PassWithWarnings, RunAssessment.Evaluate(low).Verdict);
            Assert.Equal(0, RunAssessment.Evaluate(low).ExitCode);
            // Profile can lower the bar: every warning needs review.
            Assert.Equal(RunVerdict.ReviewRequired, RunAssessment.Evaluate(low, policy: new ReportingRulesConfig { ReviewSeverity = "Low" }).Verdict);
        }

        [Fact]
        public void Fail_wins_but_the_incompleteness_is_still_reported()
        {
            var results = TenPasses();
            results.Add(R(RuleIds.Wall003, ValidationStatus.Fail));
            results.Add(R(RuleIds.Dia001, ValidationStatus.NotChecked));

            var a = RunAssessment.Evaluate(results);
            Assert.Equal(RunVerdict.Fail, a.Verdict);
            Assert.Equal(2, a.ExitCode);
            Assert.NotEmpty(a.IncompleteReasons);
        }

        [Fact]
        public void No_results_at_all_is_incomplete()
        {
            Assert.Equal(RunVerdict.Incomplete, RunAssessment.Evaluate(new List<ValidationResult>()).Verdict);
        }

        [Fact]
        public void Partial_required_table_makes_an_all_pass_run_incomplete()
        {
            var model = new ModelData();
            model.TableReports[KnownTables.WallPropertyDefinitionsSpecified] = new TableImportReport
            { Title = KnownTables.WallPropertyDefinitionsSpecified, State = TableImportState.Partial, RecordCount = 11, AcceptedCount = 10 };

            var a = RunAssessment.Evaluate(TenPasses(), model, new IValidationRule[] { new WallThicknessRule() });

            Assert.Equal(RunVerdict.Incomplete, a.Verdict);
            Assert.Contains(a.IncompleteReasons, r => r.Contains("10 of 11"));
        }

        [Fact]
        public void Empty_required_table_needs_review_unless_allowed_by_the_profile()
        {
            var model = new ModelData();
            model.TableReports[KnownTables.DiaphragmDefinitions] = new TableImportReport { Title = KnownTables.DiaphragmDefinitions, State = TableImportState.ReadEmpty };
            var rules = new IValidationRule[] { new DiaphragmRigidityRule() };

            Assert.Equal(RunVerdict.ReviewRequired, RunAssessment.Evaluate(TenPasses(), model, rules).Verdict);
            Assert.Equal(RunVerdict.Pass, RunAssessment.Evaluate(TenPasses(), model, rules,
                new ReportingRulesConfig { AllowedEmptyTables = { KnownTables.DiaphragmDefinitions } }).Verdict);
        }

        [Fact]
        public void Import_error_makes_the_run_incomplete()
        {
            var model = new ModelData();
            model.ImportIssues.Add(new ImportIssue { Level = ImportIssueLevel.Error, Message = "Table X failed" });
            Assert.Equal(RunVerdict.Incomplete, RunAssessment.Evaluate(TenPasses(), model).Verdict);
        }

        [Fact]
        public void Skipped_table_of_a_disabled_category_does_not_block_but_one_needed_by_an_enabled_rule_does()
        {
            var model = new ModelData();
            model.TableReports[KnownTables.DiaphragmDefinitions] = new TableImportReport { Title = KnownTables.DiaphragmDefinitions, State = TableImportState.Skipped };

            // Diaphragm rule NOT enabled -> its skipped table is irrelevant.
            Assert.Equal(RunVerdict.Pass, RunAssessment.Evaluate(TenPasses(), model, new IValidationRule[] { new WallThicknessRule() }).Verdict);
            // Diaphragm rule enabled -> a skipped table it needs is missing evidence.
            Assert.Equal(RunVerdict.Incomplete, RunAssessment.Evaluate(TenPasses(), model, new IValidationRule[] { new DiaphragmRigidityRule() }).Verdict);
        }

        [Theory]
        [InlineData(RunVerdict.Pass, 0)]
        [InlineData(RunVerdict.PassWithWarnings, 0)]
        [InlineData(RunVerdict.Fail, 2)]
        [InlineData(RunVerdict.Incomplete, 3)]
        [InlineData(RunVerdict.ReviewRequired, 4)]
        public void Exit_code_contract(RunVerdict verdict, int code)
        {
            Assert.Equal(code, RunAssessment.ExitCodeFor(verdict));
        }

        // ---- engine guard: silence is never success ------------------------------------------------

        private sealed class SilentRule : IValidationRule
        {
            public string RuleId => "TEST-SILENT";
            public string Name => "Silent";
            public string Category => "Test";
            public RuleType Type => RuleType.ModelingStandard;
            public Severity DefaultSeverity => Severity.Medium;
            public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.DiaphragmDefinitions };
            public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config) { yield break; }
        }

        [Fact]
        public void A_rule_that_produces_nothing_is_reported_NOT_CHECKED_in_both_engines()
        {
            var model = new ModelData();
            var serial = new ValidationEngine().Run(model, new ValidationProfileConfig(), new[] { new SilentRule() });
            var parallel = new ValidationEngine().RunParallel(model, new ValidationProfileConfig(), new[] { new SilentRule() });

            foreach (var results in new[] { serial, parallel })
            {
                var r = Assert.Single(results);
                Assert.Equal(ValidationStatus.NotChecked, r.Status);
                Assert.Contains("produced no results", r.Message);
            }
            Assert.Equal(RunVerdict.Incomplete, RunAssessment.Evaluate(serial).Verdict);
        }

        [Fact]
        public void NOT_CHECKED_for_an_unusable_table_states_the_real_import_reason()
        {
            var model = new ModelData();
            var report = new TableImportReport { Title = KnownTables.DiaphragmDefinitions, State = TableImportState.SchemaInvalid };
            report.MissingColumns.Add("RigidityType");
            model.TableReports[KnownTables.DiaphragmDefinitions] = report;

            var r = new ValidationEngine().Run(model, new ValidationProfileConfig(), new[] { new DiaphragmRigidityRule() }).Single();

            Assert.Equal(ValidationStatus.NotChecked, r.Status);
            Assert.Contains("schema invalid", r.Message);
            Assert.Contains("RigidityType", r.Message);
        }
    }
}
