namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>Normalized row from "Pier Section Properties".</summary>
    public sealed class PierProperty : ModelObjectBase
    {
        public string Story { get; set; }
        public string Pier { get; set; }
        public double WidthBottomMm { get; set; }
        public double ThicknessBottomMm { get; set; }
        public double WidthTopMm { get; set; }
        public double ThicknessTopMm { get; set; }
        public string Material { get; set; }
    }
}
