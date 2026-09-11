namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>Normalized row from "Frame Assignments - Property Modifiers". Not yet driving a
    /// rule in v1 (no explicit requirement in the initial 19), but captured for future model
    /// integrity / cross-table checks.</summary>
    public sealed class FramePropertyModifier : ModelObjectBase
    {
        public string Story { get; set; }
        public string Label { get; set; }
        public string UniqueName { get; set; }
        public double AreaModifier { get; set; }
        public double As2Modifier { get; set; }
        public double As3Modifier { get; set; }
        public double JModifier { get; set; }
        public double I22Modifier { get; set; }
        public double I33Modifier { get; set; }
        public double MassModifier { get; set; }
        public double WeightModifier { get; set; }
    }
}
