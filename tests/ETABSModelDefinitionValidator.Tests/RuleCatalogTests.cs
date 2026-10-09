using System.Linq;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.Tests
{
    public class RuleCatalogTests
    {
        private static readonly string[] AllRuleIds = typeof(RuleIds)
            .GetFields()
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue())
            .ToArray();

        [Fact]
        public void Every_rule_id_has_an_explanation()
        {
            var catalog = RuleCatalog.All(new ValidationProfileConfig()).Select(e => e.RuleId).ToList();

            foreach (var id in AllRuleIds)
            {
                Assert.Contains(id, catalog);
            }
            Assert.Equal(catalog.Count, catalog.Distinct().Count());
        }

        [Fact]
        public void Every_explanation_is_fully_written()
        {
            foreach (var e in RuleCatalog.All(new ValidationProfileConfig()))
            {
                Assert.False(string.IsNullOrWhiteSpace(e.Title), e.RuleId);
                Assert.False(string.IsNullOrWhiteSpace(e.WhatItChecks), e.RuleId);
                Assert.False(string.IsNullOrWhiteSpace(e.WhyItMatters), e.RuleId);
                Assert.False(string.IsNullOrWhiteSpace(e.PassCondition), e.RuleId);
                Assert.False(string.IsNullOrWhiteSpace(e.HowToFix), e.RuleId);
                Assert.False(string.IsNullOrWhiteSpace(e.Basis), e.RuleId);
            }
        }

        [Fact]
        public void Thresholds_shown_come_from_the_active_profile()
        {
            var config = new ValidationProfileConfig();
            config.Walls.DefaultModifierValue = 0.55;
            config.Modal.MinimumTargetRatioPercent = 90;

            Assert.Contains("0.55", RuleCatalog.Find(RuleIds.Wall003, config).PassCondition);
            Assert.Contains("90", RuleCatalog.Find(RuleIds.Modal001, config).PassCondition);
        }
    }
}
