using System;
using System.Collections.Generic;
using System.Linq;

namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>One column a table must have, and what a valid value in it looks like.</summary>
    public sealed class ColumnRule
    {
        /// <summary>Normalized header key (HeaderNormalizer form, e.g. "WallThickness").</summary>
        public string Key { get; set; }
        public bool Numeric { get; set; }
        /// <summary>Numeric value must be &gt; 0 (thicknesses, strengths, moduli).</summary>
        public bool Positive { get; set; }
        /// <summary>Numeric value must be &gt;= 0 (modifiers, offsets, ratios).</summary>
        public bool NonNegative { get; set; }
        /// <summary>A blank cell is acceptable (the populate step has an explicit meaning for blank).</summary>
        public bool AllowBlank { get; set; }
        /// <summary>When set, the text must be one of these (case-insensitive).</summary>
        public string[] AllowedValues { get; set; }

        public static ColumnRule Text(string key, bool allowBlank = false) => new ColumnRule { Key = key, AllowBlank = allowBlank };
        public static ColumnRule Number(string key, bool positive = false, bool nonNegative = false, bool allowBlank = false) =>
            new ColumnRule { Key = key, Numeric = true, Positive = positive, NonNegative = nonNegative, AllowBlank = allowBlank };
        public static ColumnRule OneOf(string key, params string[] values) => new ColumnRule { Key = key, AllowedValues = values };
    }

    /// <summary>
    /// The minimum a table must contain for the rules that read it to be trustworthy: the columns
    /// they read, which of those must hold a parseable value, and which column identifies a record.
    /// Column keys are the normalized ETABS headers, verified against the ETABS 22.3 live read
    /// (--etabs-probe on DCH-ME1-ULT-v22.3-Rev29) and the reference Excel export.
    /// Optional columns the populate step tolerates (V13 modifier, Notes, GUID...) are NOT listed.
    /// </summary>
    public sealed class TableSchema
    {
        public string Title { get; set; }
        /// <summary>Column identifying a record. Blank key = rejected record, unless continuation rows are allowed.</summary>
        public string KeyColumn { get; set; }
        /// <summary>Table legitimately has rows with a blank key that continue the previous record
        /// (e.g. a load case applying several loads). Such rows are not rejected or key-checked.</summary>
        public bool AllowContinuationRows { get; set; }
        /// <summary>Each key may appear only once (definition tables, per-object assignment tables).</summary>
        public bool UniqueKey { get; set; } = true;
        public List<ColumnRule> Columns { get; set; } = new List<ColumnRule>();

        public IEnumerable<string> RequiredColumnKeys => new[] { KeyColumn }.Concat(Columns.Select(c => c.Key)).Where(k => k != null).Distinct();
    }

    public static class TableSchemas
    {
        private static readonly string[] Modifiers = { "F11Modifier", "F22Modifier", "F12Modifier", "M11Modifier", "M22Modifier", "M12Modifier" };

        public static readonly IReadOnlyDictionary<string, TableSchema> All = Build();

        public static TableSchema For(string title) => title != null && All.TryGetValue(title, out var s) ? s : null;

        private static Dictionary<string, TableSchema> Build()
        {
            var list = new List<TableSchema>
            {
                new TableSchema
                {
                    Title = KnownTables.WallPropertyDefinitionsSpecified, KeyColumn = "Name",
                    Columns = new List<ColumnRule> { ColumnRule.Text("Material"), ColumnRule.Number("WallThickness", positive: true) }
                        .Concat(Modifiers.Select(m => ColumnRule.Number(m, nonNegative: true))).ToList()
                },
                new TableSchema
                {
                    Title = KnownTables.SlabPropertyDefinitions, KeyColumn = "Name",
                    // Thickness may be blank for some slab types (e.g. membrane/deck) - not used by the modifier check.
                    Columns = new List<ColumnRule> { ColumnRule.Number("SlabThickness", nonNegative: true, allowBlank: true) }
                        .Concat(new[] { "M11Modifier", "M22Modifier", "M12Modifier" }.Select(m => ColumnRule.Number(m, nonNegative: true))).ToList()
                },
                new TableSchema
                {
                    Title = KnownTables.PierSectionProperties, KeyColumn = "Pier", UniqueKey = false, // one row per pier per story
                    Columns = { ColumnRule.Text("Story"), ColumnRule.Number("WidthBottom", nonNegative: true), ColumnRule.Number("ThicknessBottom", nonNegative: true),
                                ColumnRule.Number("WidthTop", nonNegative: true), ColumnRule.Number("ThicknessTop", nonNegative: true) }
                },
                new TableSchema
                {
                    // One row per load applied; the name may be repeated or blank on following rows.
                    Title = KnownTables.ModalCaseDefinitionsRitz, KeyColumn = "Name", AllowContinuationRows = true, UniqueKey = false,
                    Columns = { ColumnRule.Number("TargetRatio", nonNegative: true, allowBlank: true) }
                },
                new TableSchema
                {
                    Title = KnownTables.MaterialPropertiesRebarData, KeyColumn = "Material",
                    Columns = { ColumnRule.Number("Fy", positive: true), ColumnRule.Number("Fu", positive: true), ColumnRule.Number("Fye", positive: true), ColumnRule.Number("Fue", positive: true) }
                },
                new TableSchema
                {
                    Title = KnownTables.MaterialPropertiesConcreteData, KeyColumn = "Material",
                    Columns = { ColumnRule.Number("Fc", positive: true), ColumnRule.OneOf("LtWtConc", "Yes", "No") }
                },
                new TableSchema
                {
                    // Lists EVERY material (steel, rebar, concrete, dummy/rigid placeholders...). Zero E1 or mass is
                    // legitimate for placeholder materials (the reference export has 'Dummy' with E1 = 0), so only
                    // "present, numeric, not negative" is enforced here; MAT-CONC-002 judges concrete E1 values.
                    Title = KnownTables.MaterialPropertiesBasicMechanicalProperties, KeyColumn = "Material",
                    Columns = { ColumnRule.Number("E1", nonNegative: true), ColumnRule.Number("UnitWeight", nonNegative: true), ColumnRule.Number("UnitMass", nonNegative: true) }
                },
                new TableSchema
                {
                    Title = KnownTables.LoadPatternDefinitions, KeyColumn = "Name",
                    Columns = { ColumnRule.Text("Type"), ColumnRule.Number("SelfWeightMultiplier") }
                },
                new TableSchema
                {
                    Title = KnownTables.LoadCaseDefinitionsLinearStatic, KeyColumn = "Name", AllowContinuationRows = true, UniqueKey = false,
                    Columns = { ColumnRule.Text("LoadName", allowBlank: true) }
                },
                new TableSchema
                {
                    Title = KnownTables.FrameAssignmentsFrameAutoMeshOptions, KeyColumn = "UniqueName",
                    Columns = { ColumnRule.Text("Story"), ColumnRule.OneOf("AutoMesh", "Yes", "No") }
                },
                new TableSchema
                {
                    Title = KnownTables.FrameAssignmentsEndLengthOffsets, KeyColumn = "UniqueName",
                    Columns = { ColumnRule.Text("Story"), ColumnRule.Number("OffsetI", nonNegative: true), ColumnRule.Number("OffsetJ", nonNegative: true) }
                },
                new TableSchema
                {
                    Title = KnownTables.DiaphragmDefinitions, KeyColumn = "Name",
                    Columns = { ColumnRule.Text("RigidityType") }
                },
                new TableSchema
                {
                    Title = KnownTables.ConcreteColumnOverwritesAci31819, KeyColumn = "UniqueName",
                    Columns = { ColumnRule.Text("Story"), ColumnRule.Text("DesignSection") }
                }
            };

            return list.ToDictionary(s => s.Title, StringComparer.OrdinalIgnoreCase);
        }
    }
}
