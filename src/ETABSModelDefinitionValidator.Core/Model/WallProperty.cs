namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>Normalized row from "Wall Property Definitions - Specified".</summary>
    public sealed class WallProperty : ModelObjectBase
    {
        public string Name { get; set; }
        public string Material { get; set; }
        public double ThicknessMm { get; set; }

        public double F11Modifier { get; set; }
        public double F22Modifier { get; set; }
        public double F12Modifier { get; set; }
        public double M11Modifier { get; set; }
        public double M22Modifier { get; set; }
        public double M12Modifier { get; set; }
    }
}
