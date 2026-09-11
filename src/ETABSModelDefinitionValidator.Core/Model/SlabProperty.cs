namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>Normalized row from "Slab Property Definitions".</summary>
    public sealed class SlabProperty : ModelObjectBase
    {
        public string Name { get; set; }
        public string ModelingType { get; set; }
        public string PropertyType { get; set; }
        public string Material { get; set; }
        public double ThicknessMm { get; set; }

        public double F11Modifier { get; set; }
        public double F22Modifier { get; set; }
        public double F12Modifier { get; set; }
        public double M11Modifier { get; set; }
        public double M22Modifier { get; set; }
        public double M12Modifier { get; set; }
        public double V13Modifier { get; set; }
        public double V23Modifier { get; set; }
        public double MassModifier { get; set; }
        public double WeightModifier { get; set; }
    }
}
