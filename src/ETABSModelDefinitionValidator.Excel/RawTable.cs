using System.Collections.Generic;

namespace ETABSModelDefinitionValidator.Excel
{
    /// <summary>One data row from a detected ETABS table, keyed by normalized header name, with
    /// the raw cell value and the Excel row number preserved for traceability.</summary>
    public sealed class RawRow
    {
        public int ExcelRowNumber { get; set; }
        public Dictionary<string, object> Values { get; } = new Dictionary<string, object>();

        public object Get(string columnKey) => Values.TryGetValue(columnKey, out var v) ? v : null;

        public string GetString(string columnKey)
        {
            var v = Get(columnKey);
            return v?.ToString()?.Trim();
        }

        public double GetDouble(string columnKey, double defaultValue = 0)
        {
            var v = Get(columnKey);
            if (v == null) return defaultValue;
            if (v is double d) return d;
            return double.TryParse(v.ToString(), out var parsed) ? parsed : defaultValue;
        }

        public double? GetNullableDouble(string columnKey)
        {
            var v = Get(columnKey);
            if (v == null) return null;
            if (v is double d) return d;
            return double.TryParse(v.ToString(), out var parsed) ? parsed : (double?)null;
        }

        public bool GetYesNoBool(string columnKey, bool defaultValue = false)
        {
            var s = GetString(columnKey);
            if (string.IsNullOrEmpty(s)) return defaultValue;
            if (s.Equals("Yes", System.StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("No", System.StringComparison.OrdinalIgnoreCase)) return false;
            if (bool.TryParse(s, out var b)) return b;
            return defaultValue;
        }
    }

    /// <summary>A whole detected ETABS table: its title (semantic table name), the worksheet it
    /// came from, its normalized column headers, and every data row.</summary>
    public sealed class RawTable
    {
        public string TableTitle { get; set; }
        public string WorksheetName { get; set; }

        /// <summary>Normalized-header, original-header, and the 1-based worksheet column number it came from.</summary>
        public List<(string NormalizedKey, string OriginalHeader, int ColumnIndex)> Columns { get; } = new List<(string, string, int)>();

        public List<RawRow> Rows { get; } = new List<RawRow>();
    }
}
