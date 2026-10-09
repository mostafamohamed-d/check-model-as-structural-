using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ETABSModelDefinitionValidator.Excel;

namespace ETABSModelDefinitionValidator.EtabsApi
{
    /// <summary>Saves the tables read from ETABS as plain CSV (one file per table, original ETABS
    /// column headers) plus a list of every table ETABS offers, so the extraction can be inspected
    /// and compared with an Excel export without opening ETABS again.</summary>
    public static class ExtractedTableWriter
    {
        public static IReadOnlyList<string> WriteAll(string folder, IEnumerable<RawTable> tables, IEnumerable<EtabsTableInfo> available = null)
        {
            Directory.CreateDirectory(folder);
            var written = new List<string>();
            var utf8 = new UTF8Encoding(true); // BOM so Excel opens it correctly

            foreach (var table in tables)
            {
                var path = Path.Combine(folder, Sanitize(table.TableTitle) + ".csv");
                File.WriteAllText(path, ToCsv(table), utf8);
                written.Add(path);
            }

            if (available != null)
            {
                var path = Path.Combine(folder, "_available-tables.csv");
                File.WriteAllText(path, "Key,Name\r\n" + string.Join("\r\n", available.Select(a => Escape(a.Key) + "," + Escape(a.Name))) + "\r\n", utf8);
                written.Add(path);
            }

            return written;
        }

        public static string ToCsv(RawTable table)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", table.Columns.Select(c => Escape(c.OriginalHeader))));
            foreach (var row in table.Rows)
            {
                sb.AppendLine(string.Join(",", table.Columns.Select(c =>
                    Escape(row.Values.TryGetValue(c.NormalizedKey, out var v) ? System.Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : ""))));
            }
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        private static string Sanitize(string name) =>
            new string(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
    }
}
