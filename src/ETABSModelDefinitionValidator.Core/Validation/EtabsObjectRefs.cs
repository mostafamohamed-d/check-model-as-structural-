using System.Collections.Generic;

namespace ETABSModelDefinitionValidator.Core.Validation
{
    /// <summary>
    /// The ETABS unique names behind one finding, kept per object type so the failing objects can be
    /// assigned to an ETABS group and selected in the model. Labels are not enough - the API addresses
    /// objects by unique name only.
    /// </summary>
    public sealed class EtabsObjectRefs
    {
        public List<string> Points { get; } = new List<string>();
        public List<string> Frames { get; } = new List<string>();
        public List<string> Areas { get; } = new List<string>();
        public List<string> Links { get; } = new List<string>();

        public int Count => Points.Count + Frames.Count + Areas.Count + Links.Count;
    }
}
