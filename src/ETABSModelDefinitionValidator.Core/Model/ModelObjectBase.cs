namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>
    /// Base for every normalized ETABS object. Carries the source location so any rule that
    /// fails on this object can report exactly where it came from.
    /// </summary>
    public abstract class ModelObjectBase
    {
        public SourceLocation Source { get; set; } = SourceLocation.Unknown;
    }
}
