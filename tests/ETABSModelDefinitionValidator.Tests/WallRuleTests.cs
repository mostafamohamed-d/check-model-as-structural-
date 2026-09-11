using System.Linq;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;
using Xunit;
using static ETABSModelDefinitionValidator.Tests.TestHelpers;

namespace ETABSModelDefinitionValidator.Tests
{
    public class WallThicknessRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly WallThicknessRule _rule = new WallThicknessRule();

        [Fact]
        public void Pass_WhenNameThicknessMatchesDefinedThickness()
        {
            var model = new ModelData();
            model.WallProperties.Add(new WallProperty { Name = "W300-C40", ThicknessMm = 300, Source = Loc("Wall") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Pass, results[0].Status);
        }

        [Fact]
        public void Fail_WhenNameThicknessDoesNotMatchDefinedThickness()
        {
            var model = new ModelData();
            model.WallProperties.Add(new WallProperty { Name = "W350-C40", ThicknessMm = 400, Source = Loc("Wall") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Fail, results[0].Status);
            Assert.Equal("350 mm (from name)", results[0].ExpectedValue);
            Assert.Equal("400 mm", results[0].ActualValue);
        }

        [Fact]
        public void Warning_WhenNameCannotBeParsed()
        {
            var model = new ModelData();
            model.WallProperties.Add(new WallProperty { Name = "WallA", ThicknessMm = 300, Source = Loc("Wall") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Warning, results[0].Status);
        }

        [Fact]
        public void NotChecked_WhenTableIsMissing()
        {
            var model = new ModelData();
            model.MissingTables.Add(Core.KnownTables.WallPropertyDefinitionsSpecified);

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.NotChecked, results[0].Status);
        }
    }

    public class WallMaterialRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly WallMaterialRule _rule = new WallMaterialRule();

        private static ModelData ModelWith(string wallName, string materialName, double fc)
        {
            var model = new ModelData();
            model.WallProperties.Add(new WallProperty { Name = wallName, Material = materialName, ThicknessMm = 300, Source = Loc("Wall") });
            model.ConcreteMaterials.Add(new ConcreteMaterial { Name = materialName, FcMpa = fc, Source = Loc("Conc") });
            return model;
        }

        [Fact]
        public void Pass_WhenMaterialFcMatchesNameGrade()
        {
            var results = _rule.Validate(ModelWith("W300-C40", "C40/50-TD", 40), _config).ToList();
            Assert.Single(results);
            Assert.Equal(ValidationStatus.Pass, results[0].Status);
        }

        [Fact]
        public void Fail_WhenMaterialFcDoesNotMatchNameGrade()
        {
            var results = _rule.Validate(ModelWith("W300-C40", "C56/70-TD", 56), _config).ToList();
            Assert.Single(results);
            Assert.Equal(ValidationStatus.Fail, results[0].Status);
        }

        [Fact]
        public void NotChecked_WhenReferencedMaterialIsMissing()
        {
            var model = new ModelData();
            model.WallProperties.Add(new WallProperty { Name = "W300-C40", Material = "Ghost", ThicknessMm = 300, Source = Loc("Wall") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.NotChecked, results[0].Status);
        }
    }

    public class WallModifierRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly WallModifierRule _rule = new WallModifierRule();

        private static WallProperty OrdinaryWall(string name, double modifier) => new WallProperty
        {
            Name = name,
            F11Modifier = modifier,
            F22Modifier = modifier,
            F12Modifier = modifier,
            M11Modifier = modifier,
            M22Modifier = modifier,
            M12Modifier = modifier,
            Source = Loc("Wall")
        };

        [Fact]
        public void Pass_WhenAllSixModifiersMatchStandard()
        {
            var model = new ModelData();
            model.WallProperties.Add(OrdinaryWall("W300-C40", 0.70));

            var results = _rule.Validate(model, _config).ToList();

            Assert.Equal(6, results.Count);
            Assert.All(results, r => Assert.Equal(ValidationStatus.Pass, r.Status));
        }

        [Fact]
        public void Fail_WhenAnyModifierDeviatesFromStandard()
        {
            var model = new ModelData();
            var wall = OrdinaryWall("W300-C40", 0.70);
            wall.M22Modifier = 1.0;
            model.WallProperties.Add(wall);

            var results = _rule.Validate(model, _config).ToList();

            Assert.Equal(6, results.Count);
            Assert.Equal(ValidationStatus.Fail, results.Single(r => r.FieldName == "M22 Modifier").Status);
            Assert.Equal(5, results.Count(r => r.Status == ValidationStatus.Pass));
        }

        [Theory]
        [InlineData("WT350-C56")]
        [InlineData("BW350-C40")]
        public void Exempt_ForWaterTankAndBasementWallPrefixes(string name)
        {
            var model = new ModelData();
            model.WallProperties.Add(OrdinaryWall(name, 1.0)); // deliberately non-standard - must still be exempt

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Exempt, results[0].Status);
            Assert.Equal(RuleIds.Wall004, results[0].RuleId);
        }

        [Fact]
        public void ConfigDrivesWhichModifiersAreChecked()
        {
            var config = ConfigurationLoader.LoadDefault();
            config.Walls.CheckedModifiers = new System.Collections.Generic.List<string> { "f11" };

            var model = new ModelData();
            model.WallProperties.Add(OrdinaryWall("W300-C40", 0.70));

            var results = _rule.Validate(model, config).ToList();

            Assert.Single(results);
            Assert.Equal("F11 Modifier", results[0].FieldName);
        }
    }
}
