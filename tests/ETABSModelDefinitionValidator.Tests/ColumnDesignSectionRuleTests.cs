using System.Linq;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;
using Xunit;
using static ETABSModelDefinitionValidator.Tests.TestHelpers;

namespace ETABSModelDefinitionValidator.Tests
{
    public class ColumnDesignSectionRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly ColumnDesignSectionRule _rule = new ColumnDesignSectionRule();

        [Fact]
        public void Pass_WhenDesignSectionIsProgramDetermined()
        {
            var model = new ModelData();
            model.ColumnDesignOverwrites.Add(new ColumnDesignOverwrite { Story = "L1", Label = "C1", UniqueName = "1", DesignSection = "Program Determined", Source = Loc("Col") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Pass, results[0].Status);
        }

        [Fact]
        public void Fail_WhenDesignSectionIsAnExplicitSection()
        {
            var model = new ModelData();
            model.ColumnDesignOverwrites.Add(new ColumnDesignOverwrite { Story = "L1", Label = "C1", UniqueName = "1", DesignSection = "C600X600", Source = Loc("Col") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Fail, results[0].Status);
        }

        [Fact]
        public void NotChecked_WhenTableIsMissing()
        {
            var model = new ModelData();
            model.MissingTables.Add(Core.KnownTables.ConcreteColumnOverwritesAci31819);

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.NotChecked, results[0].Status);
        }
    }
}
