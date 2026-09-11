namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>Normalized row from "Load Case Definitions - Linear Static".</summary>
    public sealed class LoadCaseLinearStatic : ModelObjectBase
    {
        public string Name { get; set; }
        public string LoadType { get; set; }
        public string LoadName { get; set; }
        public double LoadScaleFactor { get; set; }
    }
}
