namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>Normalized row from "Diaphragm Definitions".</summary>
    public sealed class DiaphragmDefinition : ModelObjectBase
    {
        public string Name { get; set; }
        public string RigidityType { get; set; }
    }
}
