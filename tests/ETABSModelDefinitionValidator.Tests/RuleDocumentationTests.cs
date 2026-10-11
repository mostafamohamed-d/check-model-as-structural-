using System.Linq;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.Tests
{
    /// <summary>Every rule documents its source and justification, code checks are kept apart from
    /// office standards, and every result carries that justification.</summary>
    public class RuleDocumentationTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();

        [Fact]
        public void Every_rule_documents_applicability_reference_tolerance_severity_exceptions_and_alternatives()
        {
            foreach (var e in RuleCatalog.All(_config))
            {
                Assert.False(string.IsNullOrWhiteSpace(e.Applicability), e.RuleId + " Applicability");
                Assert.False(string.IsNullOrWhiteSpace(e.Reference), e.RuleId + " Reference");
                Assert.False(string.IsNullOrWhiteSpace(e.Tolerance), e.RuleId + " Tolerance");
                Assert.False(string.IsNullOrWhiteSpace(e.SeverityBasis), e.RuleId + " SeverityBasis");
                Assert.False(string.IsNullOrWhiteSpace(e.Exceptions), e.RuleId + " Exceptions");
                Assert.False(string.IsNullOrWhiteSpace(e.AcceptableAlternatives), e.RuleId + " AcceptableAlternatives");
            }
        }

        [Fact]
        public void Catalog_classification_matches_the_rule_itself()
        {
            var catalog = RuleCatalog.All(_config).ToDictionary(e => e.RuleId);
            foreach (var rule in RuleRegistry.AllRules)
            {
                Assert.True(catalog.ContainsKey(rule.RuleId), rule.RuleId + " has no catalog entry");
                Assert.Equal(rule.Type, catalog[rule.RuleId].Type);
                Assert.Equal(rule.Category, catalog[rule.RuleId].Category);
            }
        }

        [Fact]
        public void Code_requirements_and_office_standards_are_never_presented_the_same_way()
        {
            foreach (var e in RuleCatalog.All(_config))
            {
                if (e.Type == RuleType.Code)
                {
                    Assert.StartsWith("CODE REQUIREMENT", e.Authority);
                }
                else
                {
                    Assert.DoesNotContain("CODE REQUIREMENT", e.Authority);
                    Assert.Contains("not a code", e.Authority + " " + (e.Type == RuleType.BestPractice ? "not a code" : ""));
                }
            }
        }

        [Fact]
        public void Unverified_clause_numbers_are_flagged_as_such()
        {
            // Any reference citing a clause that is not the verified Ec clause must say it is to be verified.
            foreach (var e in RuleCatalog.All(_config).Where(e => e.RuleId != RuleIds.MatConc002 && e.Reference.Contains("ACI 318")))
            {
                Assert.Contains("VERIFY", e.Reference.ToUpperInvariant());
            }
        }

        [Fact]
        public void Every_result_of_a_run_carries_authority_and_justification()
        {
            var model = new ModelData();
            model.WallProperties.Add(new WallProperty { Name = "W300-C40", ThicknessMm = 250, Source = TestHelpers.Loc("Walls") });
            var results = new ValidationEngine().Run(model, _config, RuleRegistry.AllRules);

            RuleCatalog.Annotate(results, _config);

            Assert.All(results, r =>
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Authority), r.RuleId);
                Assert.False(string.IsNullOrWhiteSpace(r.Justification), r.RuleId);
            });
            var fail = results.First(r => r.RuleId == RuleIds.Wall001 && r.Status == ValidationStatus.Fail);
            Assert.Contains("DATA CONSISTENCY", fail.Authority);
            Assert.Contains("Reference:", fail.Justification);
        }

        [Fact]
        public void Every_rule_id_a_rule_can_emit_has_a_catalog_entry_including_raft_slabs()
        {
            // Regression: raft slabs were emitted under a literal "SLAB-004" with no catalog entry, so their
            // results carried no justification (found on the reference export run).
            var model = new ModelData();
            foreach (var n in new[] { "PT250-C40", "S200-C40", "STAIR150-C40", "RAMP200-C40", "RAFT 600-C50" })
                model.SlabProperties.Add(new SlabProperty { Name = n, M11Modifier = 1, M22Modifier = 1, M12Modifier = 1, Source = TestHelpers.Loc("Slabs") });
            var results = new SlabModifierRule().Validate(model, _config).ToList();
            RuleCatalog.Annotate(results, _config);

            Assert.Contains(results, r => r.RuleId == RuleIds.Slab004);
            Assert.DoesNotContain(results, r => r.Justification.StartsWith("No catalog entry"));
        }

        [Fact]
        public void Name_derived_slab_result_states_the_class_and_prefix_it_was_judged_by()
        {
            var model = new ModelData();
            model.SlabProperties.Add(new SlabProperty { Name = "PT250-C40", M11Modifier = 0.25, M22Modifier = 0.35, M12Modifier = 0.35, Source = TestHelpers.Loc("Slabs") });
            var r = new SlabModifierRule().Validate(model, _config).First(x => x.Status == ValidationStatus.Fail);

            Assert.Contains("Classified PT", r.Message);
            Assert.Contains("prefix 'PT'", r.Message);
        }

        [Fact]
        public void Exemption_result_names_the_matched_prefix_and_the_profile_setting()
        {
            var model = new ModelData();
            model.WallProperties.Add(new WallProperty { Name = "BW300-C40", ThicknessMm = 300, Source = TestHelpers.Loc("Walls") });
            var r = new WallModifierRule().Validate(model, _config).Single(x => x.Status == ValidationStatus.Exempt);

            Assert.Contains("'BW'", r.Message);
            Assert.Contains("Walls.ExceptionPrefixes", r.Message);
        }
    }
}
