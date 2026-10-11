using System.Collections.Generic;

namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>
    /// What happened to one source table during import. "The table was found" is not the same as
    /// "the table was imported correctly", so every table a rule may depend on gets one of these
    /// and rules only treat <see cref="TableImportReport.IsUsable"/> tables as evidence.
    /// </summary>
    public enum TableImportState
    {
        /// <summary>Read, schema valid, every record accepted.</summary>
        Read,
        /// <summary>Read and schema valid, but the table has zero records. Not proof of "no problems".</summary>
        ReadEmpty,
        /// <summary>Read, but some records were rejected (blank key, corrupt or blank required value, duplicate key).
        /// Rules run on the accepted records; the run cannot be complete.</summary>
        Partial,
        /// <summary>Required column(s) missing, or every record rejected. Not usable.</summary>
        SchemaInvalid,
        /// <summary>Reading or parsing the table threw. Not usable.</summary>
        FailedToRead,
        /// <summary>The same table title appeared more than once in the source. Not usable - which copy is right is unknown.</summary>
        Ambiguous,
        /// <summary>Not present in the source.</summary>
        Missing,
        /// <summary>Present but deliberately not read because no enabled rule needs it.</summary>
        Skipped,
        /// <summary>Present in the source but not mapped to anything (never validated).</summary>
        Unrecognized
    }

    public sealed class TableImportReport
    {
        public string Title { get; set; }
        public TableImportState State { get; set; }

        /// <summary>Records in the source table (data rows, excluding title/header/units rows).</summary>
        public int RecordCount { get; set; }
        /// <summary>Records that passed schema/value validation and were handed to the populate step.</summary>
        public int AcceptedCount { get; set; }
        public int RejectedCount => RecordCount - AcceptedCount;

        public List<string> MissingColumns { get; } = new List<string>();
        public List<string> Messages { get; } = new List<string>();

        /// <summary>True when rules may evaluate this table. Partial is usable (the accepted records are
        /// real) but still makes the run incomplete; ReadEmpty is usable but needs review.</summary>
        public bool IsUsable => State == TableImportState.Read || State == TableImportState.ReadEmpty || State == TableImportState.Partial;

        /// <summary>True when the table being in this state means some required evidence is missing.</summary>
        public bool IsIncomplete =>
            State == TableImportState.Partial || State == TableImportState.SchemaInvalid ||
            State == TableImportState.FailedToRead || State == TableImportState.Ambiguous ||
            State == TableImportState.Missing;

        public override string ToString() =>
            $"{Title}: {State}, {AcceptedCount}/{RecordCount} record(s) accepted" +
            (MissingColumns.Count > 0 ? $", missing column(s): {string.Join(", ", MissingColumns)}" : "");
    }
}
