namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>Normalized row from "Load Pattern Definitions".</summary>
    public sealed class LoadPattern : ModelObjectBase
    {
        public string Name { get; set; }
        public bool IsAutoLoad { get; set; }
        public string Type { get; set; }
        public double SelfWeightMultiplier { get; set; }
    }
}
