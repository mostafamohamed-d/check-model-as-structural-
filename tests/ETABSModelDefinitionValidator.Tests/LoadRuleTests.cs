using System.Linq;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;
using Xunit;
using static ETABSModelDefinitionValidator.Tests.TestHelpers;

namespace ETABSModelDefinitionValidator.Tests
{
    public class DeadLoadRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly DeadSelfWeightRule _rule = new DeadSelfWeightRule();

        [Fact]
        public void Pass_WhenDeadPatternHasSelfWeightMultiplierOfOne()
        {
            var model = new ModelData();
            model.LoadPatterns.Add(new LoadPattern { Name = "Dead", Type = "Dead", SelfWeightMultiplier = 1, Source = Loc("LP") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Pass, results[0].Status);
        }

        [Fact]
        public void Fail_WhenDeadPatternSelfWeightMultiplierIsNotOne()
        {
            var model = new ModelData();
            model.LoadPatterns.Add(new LoadPattern { Name = "Dead", Type = "Dead", SelfWeightMultiplier = 0, Source = Loc("LP") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Fail, results[0].Status);
        }

        [Fact]
        public void NonDeadPatterns_AreNotEvaluated()
        {
            var model = new ModelData();
            model.LoadPatterns.Add(new LoadPattern { Name = "LL", Type = "Live", SelfWeightMultiplier = 0, Source = Loc("LP") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Empty(results);
        }
    }

    public class LoadCaseConsistencyTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly LoadCaseConsistencyRule _rule = new LoadCaseConsistencyRule();

        [Fact]
        public void Pass_WhenLoadNameEqualsCaseName()
        {
            var model = new ModelData();
            model.LoadCasesLinearStatic.Add(new LoadCaseLinearStatic { Name = "EX", LoadName = "EX", Source = Loc("LC") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Pass, results[0].Status);
        }

        [Fact]
        public void Warning_NotFail_WhenLoadNameDiffersFromCaseName()
        {
            var model = new ModelData();
            model.LoadCasesLinearStatic.Add(new LoadCaseLinearStatic { Name = "EX", LoadName = "EX-GF", Source = Loc("LC") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Warning, results[0].Status);
        }
    }
}
