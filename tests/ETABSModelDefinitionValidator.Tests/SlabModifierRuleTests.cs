using System.Linq;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;
using Xunit;
using static ETABSModelDefinitionValidator.Tests.TestHelpers;

namespace ETABSModelDefinitionValidator.Tests
{
    public class SlabModifierRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly SlabModifierRule _rule = new SlabModifierRule();

        private static SlabProperty Slab(string name, double m) => new SlabProperty
        {
            Name = name, M11Modifier = m, M22Modifier = m, M12Modifier = m, Source = Loc("Slab")
        };

        [Theory]
        [InlineData("PT250-C40", 0.35, RuleIds.Slab001)]
        [InlineData("S200-C40", 0.25, RuleIds.Slab002)]
        [InlineData("STAIR200-C40", 0.01, RuleIds.Slab003)]
        [InlineData("Ramp-250-C40", 0.01, RuleIds.Slab003)]
        public void Pass_WhenModifiersMatchClassificationProfile(string name, double modifier, string expectedRuleId)
        {
            var model = new ModelData();
            model.SlabProperties.Add(Slab(name, modifier));

            var results = _rule.Validate(model, _config).ToList();

            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.Equal(ValidationStatus.Pass, r.Status));
            Assert.All(results, r => Assert.Equal(expectedRuleId, r.RuleId));
        }

        [Fact]
        public void Fail_WhenOrdinarySlabUsesPTModifier()
        {
            var model = new ModelData();
            model.SlabProperties.Add(Slab("S200-C40", 0.35));

            var results = _rule.Validate(model, _config).ToList();

            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.Equal(ValidationStatus.Fail, r.Status));
        }

        [Fact]
        public void RaftSlabsGetOwnModifierOneProfile_NotOrdinary()
        {
            var model = new ModelData();
            model.SlabProperties.Add(Slab("RAFT 2000-C50", 1.0));

            var results = _rule.Validate(model, _config).ToList();

            Assert.Equal(3, results.Count);
            Assert.All(results, r => Assert.Equal(ValidationStatus.Pass, r.Status));
        }

        [Fact]
        public void Warning_WhenSlabNameCannotBeClassified()
        {
            var model = new ModelData();
            model.SlabProperties.Add(Slab("???", 0.25));

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Warning, results[0].Status);
        }

        [Fact]
        public void NotChecked_WhenTableIsMissing()
        {
            var model = new ModelData();
            model.MissingTables.Add(Core.KnownTables.SlabPropertyDefinitions);

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.NotChecked, results[0].Status);
        }
    }
}
