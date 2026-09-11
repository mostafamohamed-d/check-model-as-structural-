using System.Linq;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;
using Xunit;
using static ETABSModelDefinitionValidator.Tests.TestHelpers;

namespace ETABSModelDefinitionValidator.Tests
{
    public class DiaphragmRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly DiaphragmRigidityRule _rule = new DiaphragmRigidityRule();

        [Fact]
        public void Pass_WhenSemiRigid()
        {
            var model = new ModelData();
            model.DiaphragmDefinitions.Add(new DiaphragmDefinition { Name = "D1", RigidityType = "Semi-Rigid", Source = Loc("Dia") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Pass, results[0].Status);
        }

        [Fact]
        public void Fail_WhenRigid()
        {
            var model = new ModelData();
            model.DiaphragmDefinitions.Add(new DiaphragmDefinition { Name = "D3", RigidityType = "Rigid", Source = Loc("Dia") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Fail, results[0].Status);
        }

        [Fact]
        public void ChecksEveryDiaphragmFound_NotJustKnownNames()
        {
            var model = new ModelData();
            model.DiaphragmDefinitions.Add(new DiaphragmDefinition { Name = "CUSTOM-99", RigidityType = "Rigid", Source = Loc("Dia") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Fail, results[0].Status);
        }

        [Fact]
        public void NotChecked_WhenTableIsMissing()
        {
            var model = new ModelData();
            model.MissingTables.Add(Core.KnownTables.DiaphragmDefinitions);

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.NotChecked, results[0].Status);
        }
    }
}
