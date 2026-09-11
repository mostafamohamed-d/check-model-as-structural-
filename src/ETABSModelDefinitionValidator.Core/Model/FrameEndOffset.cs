namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>Normalized row from "Frame Assignments - End Length Offsets".</summary>
    public sealed class FrameEndOffset : ModelObjectBase
    {
        public string Story { get; set; }
        public string Label { get; set; }
        public string UniqueName { get; set; }
        public string OffsetOption { get; set; }
        public double OffsetIMm { get; set; }
        public double OffsetJMm { get; set; }
    }
}
