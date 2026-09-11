using System.Linq;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;
using Xunit;
using static ETABSModelDefinitionValidator.Tests.TestHelpers;

namespace ETABSModelDefinitionValidator.Tests
{
    public class PierDimensionRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly PierDimensionRule _rule = new PierDimensionRule();

        [Fact]
        public void Pass_WhenTopAndBottomDimensionsAreEqual()
        {
            var model = new ModelData();
            model.PierProperties.Add(new PierProperty
            {
                Pier = "B01-CW1A", Story = "ROOF",
                WidthBottomMm = 3900, WidthTopMm = 3900,
                ThicknessBottomMm = 400, ThicknessTopMm = 400,
                Source = Loc("Pier")
            });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal(ValidationStatus.Pass, r.Status));
        }

        [Fact]
        public void Fail_WhenWidthDiffersTopToBottom()
        {
            var model = new ModelData();
            model.PierProperties.Add(new PierProperty
            {
                Pier = "CW1C", Story = "GROUND",
                WidthBottomMm = 4870, WidthTopMm = 6345,
                ThicknessBottomMm = 400, ThicknessTopMm = 400,
                Source = Loc("Pier")
            });

            var results = _rule.Validate(model, _config).ToList();

            var width = results.Single(r => r.RuleId == RuleIds.Pier001);
            Assert.Equal(ValidationStatus.Fail, width.Status);

            var thickness = results.Single(r => r.RuleId == RuleIds.Pier002);
            Assert.Equal(ValidationStatus.Pass, thickness.Status);
        }

        [Fact]
        public void Fail_WhenThicknessDiffersTopToBottom()
        {
            var model = new ModelData();
            model.PierProperties.Add(new PierProperty
            {
                Pier = "P1", Story = "L1",
                WidthBottomMm = 3000, WidthTopMm = 3000,
                ThicknessBottomMm = 300, ThicknessTopMm = 350,
                Source = Loc("Pier")
            });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Equal(ValidationStatus.Fail, results.Single(r => r.RuleId == RuleIds.Pier002).Status);
        }

        [Fact]
        public void NotChecked_WhenTableIsMissing()
        {
            var model = new ModelData();
            model.MissingTables.Add(Core.KnownTables.PierSectionProperties);

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.NotChecked, results[0].Status);
        }
    }
}
