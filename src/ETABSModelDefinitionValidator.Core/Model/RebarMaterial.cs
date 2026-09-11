namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>Normalized row from "Material Properties - Rebar Data".</summary>
    public sealed class RebarMaterial : ModelObjectBase
    {
        public string Name { get; set; }
        public double FyMpa { get; set; }
        public double FuMpa { get; set; }
        public double FyeMpa { get; set; }
        public double FueMpa { get; set; }
    }
}
