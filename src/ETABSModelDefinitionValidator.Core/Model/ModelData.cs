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
        public List<FrameAutoMesh> FrameAutoMeshes { get; } = new List<FrameAutoMesh>();
        public List<FrameEndOffset> FrameEndOffsets { get; } = new List<FrameEndOffset>();
        public List<DiaphragmDefinition> DiaphragmDefinitions { get; } = new List<DiaphragmDefinition>();
        public List<ColumnDesignOverwrite> ColumnDesignOverwrites { get; } = new List<ColumnDesignOverwrite>();
        public List<PierLabelAssignment> PierLabelAssignments { get; } = new List<PierLabelAssignment>();
        public List<WallObject> WallObjects { get; } = new List<WallObject>();
        public List<PointObject> PointObjects { get; } = new List<PointObject>();
        public List<WallObject> FloorObjects { get; } = new List<WallObject>();
        public List<FrameObject> FrameObjects { get; } = new List<FrameObject>();
        public List<LinkObject> LinkObjects { get; } = new List<LinkObject>();
        public List<JointRestraint> JointRestraints { get; } = new List<JointRestraint>();
        public List<AreaLoadSetAssignment> AreaLoadSets { get; } = new List<AreaLoadSetAssignment>();

        /// <summary>Base level (mm) from "Tower and Base Story Definitions" - lowest tower base when there are several.</summary>
        public double? BaseElevationMm { get; set; }

        /// <summary>Full path of the ETABS model file, when known (live runs). Used to recognise foundation models.</summary>
        public string ModelFilePath { get; set; }

        /// <summary>Frame distributed and area uniform loads. ETABS will not return these as database
        /// tables (load-assignment tables are filtered by the patterns selected for display), so only a
        /// live run reads them, through the object API. Null when they were not read.</summary>
        public List<FrameDistributedLoad> FrameDistributedLoads { get; set; }
        public List<AreaUniformLoad> AreaUniformLoads { get; set; }

        /// <summary>Why the load assignments are not available (shown in NOT_CHECKED results).</summary>
        public string LoadAssignmentsNote { get; set; } = "Load assignments are read only from a live ETABS model (Excel exports do not include them in a usable form).";

        /// <summary>Tables detected in the source that were not recognized/mapped - surfaced, never silently dropped.</summary>
        public List<string> UnrecognizedTables { get; } = new List<string>();

        /// <summary>Tables expected by at least one registered rule but not found in the source.</summary>
        public List<string> MissingTables { get; } = new List<string>();

        /// <summary>Tables present in the workbook whose rows were deliberately not read because
        /// the current validation profile (enabled rule categories) doesn't need them - distinct
        /// from MissingTables, which means the table genuinely isn't in the workbook.</summary>
        public List<string> SkippedTables { get; } = new List<string>();

        public List<ImportIssue> ImportIssues { get; } = new List<ImportIssue>();

        /// <summary>Import outcome per table title (case-insensitive). Filled by the providers.</summary>
        public Dictionary<string, TableImportReport> TableReports { get; } =
            new Dictionary<string, TableImportReport>(System.StringComparer.OrdinalIgnoreCase);

        public TableImportReport GetTableReport(string tableName) =>
            tableName != null && TableReports.TryGetValue(tableName, out var r) ? r : null;

        /// <summary>
        /// True only when the table was read and is usable as evidence - NOT merely "present in the
        /// source". Missing, skipped, schema-invalid, failed and ambiguous tables are all false, so
        /// every rule that guards on this reports NOT_CHECKED instead of iterating an empty or
        /// half-filled collection. Without an import report (hand-built test models) it falls back
        /// to "not listed as missing".
        /// </summary>
        public bool WasTableFound(string tableName)
        {
            var report = GetTableReport(tableName);
            if (report != null) return report.IsUsable;
            return !MissingTables.Contains(tableName) && !SkippedTables.Contains(tableName);
        }

        /// <summary>Human-readable reason a table is not usable (for NOT_CHECKED messages), or null.</summary>
        public string TableProblem(string tableName)
        {
            var report = GetTableReport(tableName);
            if (report == null) return MissingTables.Contains(tableName) ? "not found in the source" : SkippedTables.Contains(tableName) ? "not read (category disabled)" : null;
            switch (report.State)
            {
                case TableImportState.Missing: return "not found in the source";
                case TableImportState.Skipped: return "not read (category disabled)";
                case TableImportState.SchemaInvalid: return "schema invalid" + (report.MissingColumns.Count > 0 ? $" - missing column(s) {string.Join(", ", report.MissingColumns)}" : " - no valid records");
                case TableImportState.FailedToRead: return "could not be read";
                case TableImportState.Ambiguous: return "appears more than once in the source";
                default: return null;
            }
        }
    }
}
