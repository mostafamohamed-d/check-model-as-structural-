using System;
using System.Collections.Generic;
using System.Linq;

// Same namespace/name as the real interop enum, because EtabsSession looks up "ETABSv1.eUnits"
// in whatever assembly it is given. Values match ETABSv1.dll (N_mm_C = 9).
namespace ETABSv1
{
    public enum eUnits { lb_in_F = 1, lb_ft_F = 2, kip_in_F = 3, kip_ft_F = 4, kN_mm_C = 5, kN_m_C = 6, kgf_mm_C = 7, kgf_m_C = 8, N_mm_C = 9, N_m_C = 10 }
}

namespace ETABSModelDefinitionValidator.Tests
{
    /// <summary>One table as the fake ETABS holds it: field keys, display names, and records.</summary>
    public sealed class FakeTable
    {
        public string Key;
        public string Name;
        public string[] FieldKeys;
        public string[] FieldNames;
        public List<string[]> Records = new List<string[]>();
    }

    /// <summary>
    /// Stand-in for ETABSv1.cDatabaseTables with the same method signatures, so EtabsSession's
    /// reflection calls exercise the real code paths. Each failure mode seen (or plausible) on a
    /// real ETABS can be switched on independently.
    /// </summary>
    public sealed class FakeDatabaseTables
    {
        public readonly List<FakeTable> Tables = new List<FakeTable>();
        public FakeSapModel Owner;

        public int GetAllTablesReturn;               // 1 reproduces the reported "GetAllTables returned error code 1"
        public int GetAvailableTablesReturn;
        public bool RejectNullArrays;                // some interop builds refuse a null by-ref SAFEARRAY
        public int GetAllFieldsReturn;
        public readonly HashSet<string> FailDisplayFor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string ThrowOnDisplayFor;

        public readonly List<string> Calls = new List<string>();
        public readonly List<ETABSv1.eUnits> UnitsDuringDisplayCalls = new List<ETABSv1.eUnits>();

        public int GetAllTables(ref int NumberTables, ref string[] TableKey, ref string[] TableName, ref int[] ImportType, ref bool[] IsEmpty)
        {
            Calls.Add("GetAllTables");
            if (RejectNullArrays && (TableKey == null || TableName == null)) return 1;
            if (GetAllTablesReturn != 0) return GetAllTablesReturn;
            NumberTables = Tables.Count;
            TableKey = Tables.Select(t => t.Key).ToArray();
            TableName = Tables.Select(t => t.Name).ToArray();
            ImportType = new int[Tables.Count];
            IsEmpty = Tables.Select(t => t.Records.Count == 0).ToArray();
            return 0;
        }

        public int GetAvailableTables(ref int NumberTables, ref string[] TableKey, ref string[] TableName, ref int[] ImportType)
        {
            Calls.Add("GetAvailableTables");
            if (RejectNullArrays && (TableKey == null || TableName == null)) return 1;
            if (GetAvailableTablesReturn != 0) return GetAvailableTablesReturn;
            NumberTables = Tables.Count;
            TableKey = Tables.Select(t => t.Key).ToArray();
            TableName = Tables.Select(t => t.Name).ToArray();
            ImportType = new int[Tables.Count];
            return 0;
        }

        public int GetAllFieldsInTable(string TableKey, ref int TableVersion, ref int NumberFields, ref string[] FieldKey, ref string[] FieldName,
            ref string[] Description, ref string[] UnitsString, ref bool[] IsImportable)
        {
            Calls.Add("GetAllFieldsInTable:" + TableKey);
            if (GetAllFieldsReturn != 0) return GetAllFieldsReturn;
            var t = Find(TableKey);
            if (t == null) return 1;
            NumberFields = t.FieldKeys.Length;
            FieldKey = t.FieldKeys;
            FieldName = t.FieldNames;
            Description = new string[t.FieldKeys.Length];
            UnitsString = new string[t.FieldKeys.Length];
            IsImportable = new bool[t.FieldKeys.Length];
            return 0;
        }

        public int GetTableForDisplayArray(string TableKey, ref string[] FieldKeyList, string GroupName, ref int TableVersion,
            ref string[] FieldsKeysIncluded, ref int NumberRecords, ref string[] TableData)
        {
            Calls.Add("GetTableForDisplayArray:" + TableKey);
            UnitsDuringDisplayCalls.Add(Owner.Units);
            if (string.Equals(TableKey, ThrowOnDisplayFor, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("COM failure (simulated)");
            if (FailDisplayFor.Contains(TableKey)) return 1;
            if (RejectNullArrays && FieldsKeysIncluded == null) return 1;
            var t = Find(TableKey);
            if (t == null) return 1;
            FieldsKeysIncluded = t.FieldKeys;
            NumberRecords = t.Records.Count;
            TableData = t.Records.SelectMany(r => r).ToArray();
            return 0;
        }

        private FakeTable Find(string key) => Tables.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Stand-in for ETABSv1.cSapModel: the DatabaseTables property plus present units.</summary>
    public sealed class FakeSapModel
    {
        public FakeSapModel(ETABSv1.eUnits startUnits = ETABSv1.eUnits.kN_m_C)
        {
            Units = startUnits;
            DatabaseTables = new FakeDatabaseTables { Owner = this };
        }

        public FakeDatabaseTables DatabaseTables { get; }
        public ETABSv1.eUnits Units;
        public int SetPresentUnitsCalls;

        public ETABSv1.eUnits GetPresentUnits() => Units;

        public int SetPresentUnits(ETABSv1.eUnits units)
        {
            SetPresentUnitsCalls++;
            Units = units;
            return 0;
        }
    }
}
