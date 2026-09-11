namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>Normalized row from "Material Properties - Basic Mechanical Properties" (concrete materials).</summary>
    public sealed class ConcreteMechanicalProperties : ModelObjectBase
    {
        public string Material { get; set; }
        public double UnitWeightKnPerM3 { get; set; }
        public double UnitMassKgPerM3 { get; set; }
        public double E1Mpa { get; set; }
        public double G12Mpa { get; set; }
        public double PoissonRatio { get; set; }
        public double ThermalCoefficientPerC { get; set; }
    }
}
