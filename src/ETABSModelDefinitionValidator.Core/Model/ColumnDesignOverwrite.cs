namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>Normalized row from "Concrete Column Overwrites - ACI 318-19".</summary>
    public sealed class ColumnDesignOverwrite : ModelObjectBase
    {
        public string Story { get; set; }
        public string Label { get; set; }
        public string UniqueName { get; set; }
        public string DesignType { get; set; }
        public string DesignSection { get; set; }
        public string FrameType { get; set; }
    }
}
