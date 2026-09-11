using System.Collections.Generic;

namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>
    /// The normalized, in-memory representation of an ETABS model definition export. This is
    /// the single object every validation rule operates on - never an Excel worksheet, never a
    /// COM object. Populated once by an IModelDataProvider (Excel today, ETABS API later) and
    /// then handed to every rule, so a large model is only parsed once.
    /// </summary>
    public sealed class ModelData
    {
        public string WorkbookName { get; set; }

        public List<WallProperty> WallProperties { get; } = new List<WallProperty>();
        public List<SlabProperty> SlabProperties { get; } = new List<SlabProperty>();
        public List<PierProperty> PierProperties { get; } = new List<PierProperty>();
        public List<ModalRitzCase> ModalRitzCases { get; } = new List<ModalRitzCase>();
        public List<RebarMaterial> RebarMaterials { get; } = new List<RebarMaterial>();
        public List<ConcreteMaterial> ConcreteMaterials { get; } = new List<ConcreteMaterial>();
        public List<LoadPattern> LoadPatterns { get; } = new List<LoadPattern>();
        public List<LoadCaseLinearStatic> LoadCasesLinearStatic { get; } = new List<LoadCaseLinearStatic>();
        public List<FramePropertyModifier> FramePropertyModifiers { get; } = new List<FramePropertyModifier>();
        public List<FrameAutoMesh> FrameAutoMeshes { get; } = new List<FrameAutoMesh>();
        public List<FrameEndOffset> FrameEndOffsets { get; } = new List<FrameEndOffset>();
        public List<DiaphragmDefinition> DiaphragmDefinitions { get; } = new List<DiaphragmDefinition>();
        public List<ColumnDesignOverwrite> ColumnDesignOverwrites { get; } = new List<ColumnDesignOverwrite>();

        /// <summary>Tables detected in the source that were not recognized/mapped - surfaced, never silently dropped.</summary>
        public List<string> UnrecognizedTables { get; } = new List<string>();

        /// <summary>Tables expected by at least one registered rule but not found in the source.</summary>
        public List<string> MissingTables { get; } = new List<string>();

        public List<ImportIssue> ImportIssues { get; } = new List<ImportIssue>();

        public bool WasTableFound(string tableName) => !MissingTables.Contains(tableName);
    }
}
