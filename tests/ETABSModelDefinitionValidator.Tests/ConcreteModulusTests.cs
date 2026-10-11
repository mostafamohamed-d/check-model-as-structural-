using System;
using System.Linq;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.Tests
{
    /// <summary>
    /// MAT-CONC-002 with both ACI 318-19 19.2.2.1 equations. Expected values are hand calculations,
    /// and the E1 values used are the ones read live from the project model
    /// DCH-ME1-ULT-v22.3-Rev29-ABS 1 (ETABS 22.3): C50 -> 36872, C56 -> 39022, C65 -> 42041 MPa,
    /// unit mass 2.45e-9 N-s2/mm4 (= 2450 kg/m3).
    /// </summary>
    public class ConcreteModulusTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly ConcretePropertyRule _rule = new ConcretePropertyRule();

        private static bool Near(double actual, double expected, double relTol = 1e-4) =>
            Math.Abs(actual - expected) <= Math.Max(Math.Abs(expected) * relTol, 1e-9);

        private ValidationResult Run(double fc, double e1, double unitMass = 0, double unitWeight = 0, bool lightweight = false)
        {
            var model = new ModelData();
            var material = new ConcreteMaterial { Name = $"C{fc}", FcMpa = fc, IsLightweight = lightweight, Source = SourceLocation.Unknown };
            material.MechanicalProperties = new ConcreteMechanicalProperties { Material = material.Name, E1Mpa = e1, UnitMassKgPerM3 = unitMass, UnitWeightKnPerM3 = unitWeight };
            model.ConcreteMaterials.Add(material);
            return _rule.Validate(model, _config).Single();
        }

        // --- the two equations, hand-checked ------------------------------------------------

        [Fact]
        public void Equation_b_hand_check()
        {
            // 4700 * sqrt(50) = 4700 * 7.0711 = 33234
            Assert.True(Near(ConcreteModulus.EquationB(50), 33234.0, 1e-4));
        }

        [Theory]
        [InlineData(50, 36872)] // 2450^1.5 = 121268.6 ; *0.043 = 5214.55 ; *sqrt(50)=7.0711 -> 36872
        [InlineData(56, 39022)]
        [InlineData(65, 42041)]
        public void Equation_a_reproduces_the_live_ETABS_E1_values(double fc, double etabsE1)
        {
            Assert.True(Near(ConcreteModulus.EquationA(2450, fc), etabsE1, 1e-4), $"fc={fc}: {ConcreteModulus.EquationA(2450, fc):0.0}");
        }

        // --- density recovery across units -------------------------------------------------

        [Theory]
        [InlineData(2.45e-9, 0, 2450)]      // live read, N-mm: unit mass N-s2/mm4
        [InlineData(2.45, 0, 2450)]         // export in kN-m: t/m3
        [InlineData(2450, 0, 2450)]         // kg/m3
        [InlineData(0, 2.40263e-5, 2450)]   // only unit weight, N/mm3 (24.0263 kN/m3 / g)
        [InlineData(0, 24.0263, 2450)]      // only unit weight, kN/m3
        public void Density_is_recovered_whatever_the_units(double mass, double weight, double expectedWc)
        {
            var ok = ConcreteModulus.TryGetDensity(new ConcreteMechanicalProperties { UnitMassKgPerM3 = mass, UnitWeightKnPerM3 = weight }, out var wc, out _);
            Assert.True(ok);
            Assert.True(Near(wc, expectedWc, 1e-3), $"wc={wc}");
        }

        [Fact]
        public void No_density_when_both_fields_are_zero()
        {
            Assert.False(ConcreteModulus.TryGetDensity(new ConcreteMechanicalProperties(), out _, out _));
        }

        // --- the rule: pass if either equation matches -------------------------------------

        [Theory]
        [InlineData(50, 36872)]
        [InlineData(56, 39022)]
        [InlineData(65, 42041)]
        public void Live_model_materials_pass_through_equation_a(double fc, double e1)
        {
            var r = Run(fc, e1, unitMass: 2.45e-9);
            Assert.Equal(ValidationStatus.Pass, r.Status);
            Assert.Contains("Eq. (a)", r.Message);
            Assert.Contains("2450", r.Message);
        }

        [Fact]
        public void E1_equal_to_equation_b_passes_through_equation_b()
        {
            var r = Run(50, 33234, unitMass: 2.45e-9);
            Assert.Equal(ValidationStatus.Pass, r.Status);
            Assert.Contains("Eq. (b)", r.Message);
        }

        [Fact]
        public void E1_far_from_both_warns_and_reports_both_differences()
        {
            var r = Run(50, 28000, unitMass: 2.45e-9); // (b) 33234 -> 15.7 %, (a) 36872 -> 24.1 %
            Assert.Equal(ValidationStatus.Warning, r.Status);
            Assert.Contains("15.7%", r.Message);
            Assert.Contains("24.1%", r.Message);
        }

        [Fact]
        public void Tolerance_boundary_is_inclusive_at_5_percent_of_the_nearer_equation()
        {
            var atLimit = 33234.0 * 0.951;  // 4.9 % below (b), well below (a)
            var past = 33234.0 * 0.945;     // 5.5 % below (b)
            Assert.Equal(ValidationStatus.Pass, Run(50, atLimit).Status);
            Assert.Equal(ValidationStatus.Warning, Run(50, past).Status);
        }

        [Fact]
        public void Between_the_two_equations_passes_if_within_5_percent_of_either()
        {
            // 35000: (b) 33234 -> +5.3 % ; (a) 36872 -> -5.1 % -> neither -> WARNING
            Assert.Equal(ValidationStatus.Warning, Run(50, 35000, unitMass: 2.45e-9).Status);
            // 35500: (a) -> -3.7 % -> PASS
            Assert.Equal(ValidationStatus.Pass, Run(50, 35500, unitMass: 2.45e-9).Status);
        }

        [Fact]
        public void Lightweight_uses_equation_a_only()
        {
            // wc 1800: 1800^1.5 = 76367.5 ; *0.043 = 3283.8 ; *sqrt(35)=5.9161 -> 19427 MPa
            var r = Run(35, 19427, unitMass: 1800, lightweight: true);
            Assert.Equal(ValidationStatus.Pass, r.Status);
            Assert.Contains("Eq. (a)", r.Message);

            var tooStiff = Run(35, 4700 * Math.Sqrt(35), unitMass: 1800, lightweight: true); // (b) value must NOT pass lightweight
            Assert.Equal(ValidationStatus.Warning, tooStiff.Status);
        }

        [Fact]
        public void Lightweight_without_density_is_not_checked_not_passed()
        {
            var r = Run(35, 20000, lightweight: true);
            Assert.Equal(ValidationStatus.NotChecked, r.Status);
        }

        [Fact]
        public void Density_outside_equation_a_range_falls_back_to_b_only()
        {
            // wc 2600 > 2560: (a) not applicable; E1 = (a)-value would only match (a) -> WARNING
            var eqAOutOfRange = ConcreteModulus.EquationA(2600, 50);
            var r = Run(50, eqAOutOfRange, unitMass: 2600);
            Assert.Equal(ValidationStatus.Warning, r.Status);
            Assert.Contains("outside 1440-2560", r.TechnicalExplanation);
        }

        [Fact]
        public void Without_density_behaves_like_before_equation_b_only()
        {
            Assert.Equal(ValidationStatus.Pass, Run(40, 4700 * Math.Sqrt(40)).Status);
            Assert.Equal(ValidationStatus.Warning, Run(40, 36872).Status);
        }

        [Fact]
        public void Code_basis_cites_both_clauses()
        {
            var r = Run(50, 36872, unitMass: 2.45e-9);
            Assert.True(r.CodeBasis.IsVerified);
            Assert.Contains("19.2.2.1(a) and (b)", r.CodeBasis.ChapterOrSection);
        }
    }
}
