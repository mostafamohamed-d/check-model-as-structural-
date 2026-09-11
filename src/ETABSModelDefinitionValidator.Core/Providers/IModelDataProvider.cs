using ETABSModelDefinitionValidator.Core.Model;

namespace ETABSModelDefinitionValidator.Core.Providers
{
    /// <summary>
    /// Produces a normalized ModelData from some source. Validation rules never depend on this
    /// interface's implementations directly - only on ModelData. This is what lets the same
    /// rule engine later run against a live ETABS-API connection instead of an Excel export,
    /// per the master prompt's Phase 8 requirement, without any rule code changing.
    /// </summary>
    public interface IModelDataProvider
    {
        ModelData GetModelData();
    }
}
