using System.Linq;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;
using Xunit;
using static ETABSModelDefinitionValidator.Tests.TestHelpers;

namespace ETABSModelDefinitionValidator.Tests
{
    public class RebarMaterialRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly RebarMaterialRule _rule = new RebarMaterialRule();

        [Fact]
        public void Pass_WhenFy420MatchesConfiguredProfile()
        {
            var model = new ModelData();
            model.RebarMaterials.Add(new RebarMaterial { Name = "Fy420", FyMpa = 420, FuMpa = 525, FyeMpa = 420, FueMpa = 525, Source = Loc("Rebar") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Equal(4, results.Count);
            Assert.All(results, r => Assert.Equal(ValidationStatus.Pass, r.Status));
        }

        [Fact]
        public void Fail_WhenFuDeviatesFromProfile()
        {
            var model = new ModelData();
            model.RebarMaterials.Add(new RebarMaterial { Name = "Fy500", FyMpa = 500, FuMpa = 600, FyeMpa = 500, FueMpa = 625, Source = Loc("Rebar") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Equal(ValidationStatus.Fail, results.Single(r => r.FieldName == "Fu").Status);
            Assert.Equal(3, results.Count(r => r.Status == ValidationStatus.Pass));
        }

        [Fact]
        public void NotChecked_WhenNoGradeProfileConfiguredForMaterial()
        {
            var model = new ModelData();
            model.RebarMaterials.Add(new RebarMaterial { Name = "FyCustom", FyMpa = 450, Source = Loc("Rebar") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.NotChecked, results[0].Status);
        }
    }

    public class ConcreteFcRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly ConcreteFcRule _rule = new ConcreteFcRule();

        [Theory]
        [InlineData("C40/50", 40, ValidationStatus.Pass)]
        [InlineData("C40/50-TD", 40, ValidationStatus.Pass)]
        [InlineData("C56/70", 56, ValidationStatus.Pass)]
        [InlineData("C40/50", 45, ValidationStatus.Fail)]
        public void EvaluatesFcAgainstNameGrade(string name, double fc, ValidationStatus expected)
        {
            var model = new ModelData();
            model.ConcreteMaterials.Add(new ConcreteMaterial { Name = name, FcMpa = fc, Source = Loc("Conc") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(expected, results[0].Status);
        }

        [Fact]
        public void Warning_WhenMaterialNameCannotBeParsed()
        {
            var model = new ModelData();
            model.ConcreteMaterials.Add(new ConcreteMaterial { Name = "Concrete-A", FcMpa = 40, Source = Loc("Conc") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Warning, results[0].Status);
        }
    }

    public class ConcretePropertyRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly ConcretePropertyRule _rule = new ConcretePropertyRule();

        [Fact]
        public void Pass_WhenE1MatchesAciFormulaWithinTolerance()
        {
            var model = new ModelData();
            var material = new ConcreteMaterial { Name = "C40/50", FcMpa = 40, Source = Loc("Conc") };
            material.MechanicalProperties = new ConcreteMechanicalProperties { Material = "C40/50", E1Mpa = 4700 * System.Math.Sqrt(40) };
            model.ConcreteMaterials.Add(material);

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Pass, results[0].Status);
        }

        [Fact]
        public void NotChecked_WhenMechanicalPropertiesRowIsMissing()
        {
            var model = new ModelData();
            model.ConcreteMaterials.Add(new ConcreteMaterial { Name = "C40/50", FcMpa = 40, Source = Loc("Conc") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.NotChecked, results[0].Status);
        }

        [Fact]
        public void NotChecked_WithReferencePendingBasis_WhenCodeProfileIsNotAci()
        {
            var config = ConfigurationLoader.LoadDefault();
            config.CodeProfile = "Eurocode2";

            var model = new ModelData();
            var material = new ConcreteMaterial { Name = "C40/50", FcMpa = 40, Source = Loc("Conc") };
            material.MechanicalProperties = new ConcreteMechanicalProperties { Material = "C40/50", E1Mpa = 35000 };
            model.ConcreteMaterials.Add(material);

            var results = _rule.Validate(model, config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.NotChecked, results[0].Status);
            Assert.False(results[0].CodeBasis.IsVerified);
        }
    }
}
