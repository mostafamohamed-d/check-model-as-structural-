namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>Normalized row from "Material Properties - Concrete Data".</summary>
    public sealed class ConcreteMaterial : ModelObjectBase
    {
        public string Name { get; set; }
        public double FcMpa { get; set; }
        public bool IsLightweight { get; set; }

        /// <summary>Joined in from "Material Properties - Basic Mechanical Properties" by Material name, when present.</summary>
        public ConcreteMechanicalProperties MechanicalProperties { get; set; }
    }
}
