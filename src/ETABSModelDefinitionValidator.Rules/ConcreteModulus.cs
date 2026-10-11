using System;
using ETABSModelDefinitionValidator.Core.Model;

namespace ETABSModelDefinitionValidator.Rules
{
    /// <summary>
    /// ACI 318-19 19.2.2.1 modulus of elasticity, both forms, plus recovery of the concrete
    /// density wc from the ETABS "Basic Mechanical Properties" row. Pure math - no ETABS.
    /// </summary>
    public static class ConcreteModulus
    {
        /// <summary>Eq. (a) validity range for wc, kg/m3 (ACI 318-19 19.2.2.1(a)).</summary>
        public const double MinWcKgPerM3 = 1440;
        public const double MaxWcKgPerM3 = 2560;

        private const double G = 9.80665;

        /// <summary>Eq. (a): Ec = wc^1.5 * 0.043 * sqrt(fc'), wc in kg/m3, fc' and Ec in MPa.</summary>
        public static double EquationA(double wcKgPerM3, double fcMpa) => Math.Pow(wcKgPerM3, 1.5) * 0.043 * Math.Sqrt(fcMpa);

        /// <summary>Eq. (b): Ec = 4700 * sqrt(fc') (MPa), normalweight concrete.</summary>
        public static double EquationB(double fcMpa) => 4700.0 * Math.Sqrt(fcMpa);

        /// <summary>
        /// Recovers wc (kg/m3) from the raw UnitMass / UnitWeight values. The table values are in
        /// whatever units the source used (live ETABS reads are N-mm: mass ~2.45e-9 N.s2/mm4,
        /// weight ~2.4e-5 N/mm3; an export in kN-m gives 2.45 t/m3 and 24 kN/m3). Each plausible unit
        /// interpretation is tried and the first that lands in a physical concrete range (1000-3000
        /// kg/m3) wins; mass is preferred over weight because wc in ACI is a density.
        /// </summary>
        public static bool TryGetDensity(ConcreteMechanicalProperties p, out double wcKgPerM3, out string how)
        {
            wcKgPerM3 = 0;
            how = null;
            if (p == null) return false;

            var massCandidates = new[]
            {
                (Factor: 1e12, Units: "N-s2/mm4"),   // N-mm-C
                (Factor: 1e3, Units: "t/m3 (kN-s2/m4)"), // kN-m-C
                (Factor: 1.0, Units: "kg/m3")
            };
            foreach (var c in massCandidates)
            {
                var wc = p.UnitMassKgPerM3 * c.Factor;
                if (Plausible(wc)) { wcKgPerM3 = wc; how = $"from Unit Mass {p.UnitMassKgPerM3:G4} {c.Units}"; return true; }
            }

            var weightCandidates = new[]
            {
                (Factor: 1e9 / G, Units: "N/mm3"),
                (Factor: 1e3 / G, Units: "kN/m3"),
                (Factor: 1.0 / G, Units: "N/m3")
            };
            foreach (var c in weightCandidates)
            {
                var wc = p.UnitWeightKnPerM3 * c.Factor;
                if (Plausible(wc)) { wcKgPerM3 = wc; how = $"from Unit Weight {p.UnitWeightKnPerM3:G4} {c.Units} / g"; return true; }
            }

            return false;
        }

        private static bool Plausible(double wc) => wc >= 1000 && wc <= 3000;
    }
}
