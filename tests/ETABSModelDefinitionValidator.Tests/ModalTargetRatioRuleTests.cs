using System.Linq;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;
using Xunit;
using static ETABSModelDefinitionValidator.Tests.TestHelpers;

namespace ETABSModelDefinitionValidator.Tests
{
    public class ModalTargetRatioRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly ModalTargetRatioRule _rule = new ModalTargetRatioRule();

        private static ModelData ModelWithRatio(double? ratio)
        {
            var model = new ModelData();
            model.ModalRitzCases.Add(new ModalRitzCase { Name = "Modal", LoadName = "Accel UX", TargetRatioPercent = ratio, Source = Loc("Modal") });
            return model;
        }

        [Theory]
        [InlineData(99.0, ValidationStatus.Pass)]
        [InlineData(95.0, ValidationStatus.Pass)] // boundary: exactly the threshold must pass (>=, not >)
        [InlineData(95.1, ValidationStatus.Pass)]
        [InlineData(94.9, ValidationStatus.Fail)]
        public void EvaluatesAgainstNinetyFivePercentThreshold(double ratio, ValidationStatus expected)
        {
            var results = _rule.Validate(ModelWithRatio(ratio), _config).ToList();

            Assert.Single(results);
            Assert.Equal(expected, results[0].Status);
        }

        [Fact]
        public void NotChecked_WhenTargetRatioIsMissing()
        {
            var results = _rule.Validate(ModelWithRatio(null), _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.NotChecked, results[0].Status);
        }

        [Fact]
        public void NotChecked_WhenTableIsMissing()
        {
            var model = new ModelData();
            model.MissingTables.Add(Core.KnownTables.ModalCaseDefinitionsRitz);

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.NotChecked, results[0].Status);
        }
    }
}
