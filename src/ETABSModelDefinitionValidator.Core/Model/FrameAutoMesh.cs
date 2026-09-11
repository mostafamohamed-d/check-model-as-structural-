namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>Normalized row from "Frame Assignments - Frame Auto Mesh Options".</summary>
    public sealed class FrameAutoMesh : ModelObjectBase
    {
        public string Story { get; set; }
        public string Label { get; set; }
        public string UniqueName { get; set; }
        public bool AutoMeshEnabled { get; set; }
    }
}
