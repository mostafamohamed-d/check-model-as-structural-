using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace ETABSModelDefinitionValidator.EtabsApi
{
    /// <summary>
    /// Read-only live diagnostic (integration layer): runs each table-extraction API call against a
    /// running ETABS and reports raw return codes, counts, field names, first record and timings.
    /// Never changes units or anything else in the model, so it is safe on a real project file.
    /// </summary>
    public static class EtabsProbe
    {
        public static string Run(EtabsSession session, IEnumerable<string> titles)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"ETABS folder : {session.EtabsFolder}");
            sb.AppendLine($"Process id   : {session.ProcessId}");
            sb.AppendLine($"Model        : {session.ModelFilePath}");
            sb.AppendLine($"Present units: {session.PresentUnitsName()}   (probe does NOT change units; values below are in these units)");
            sb.AppendLine();

            var problems = new List<string>();
            var sw = Stopwatch.StartNew();
            var available = session.GetAvailableTables(problems);
            sb.AppendLine($"[listing] {sw.ElapsedMilliseconds} ms - " + (available == null ? "FAILED" : $"{available.Count} table(s)"));
            foreach (var p in problems) sb.AppendLine("          " + p);
            sb.AppendLine();

            var byName = (available ?? new List<EtabsTableInfo>())
                .GroupBy(t => EtabsModelDataProvider.TableNameKey(t.Name)).ToDictionary(g => g.Key, g => g.First());

            int ok = 0, failed = 0;
            foreach (var title in titles)
            {
                var info = byName.TryGetValue(EtabsModelDataProvider.TableNameKey(title), out var found) ? found : new EtabsTableInfo { Key = title, Name = title };
                sb.AppendLine($"== {title}");
                sb.AppendLine($"   listed by ETABS : {(found != null ? $"yes (key '{found.Key}')" : available == null ? "unknown (listing failed) - trying the name as key" : "NO - trying the name as key")}");
                sw.Restart();
                try
                {
                    var raw = session.ReadTable(info);
                    var table = ApiTableConverter.Convert(title, raw.FieldKeys, raw.FieldNames, raw.NumberRecords, raw.Data);
                    sb.AppendLine($"   read            : OK, {raw.NumberRecords} record(s), {raw.FieldKeys.Length} field(s), {sw.ElapsedMilliseconds} ms");
                    sb.AppendLine($"   field names     : {(raw.FieldNames.Count == 0 ? "UNAVAILABLE - keys used as headers" : "available")}");
                    sb.AppendLine($"   columns         : {string.Join(" | ", table.Columns.Select(c => $"{c.OriginalHeader} -> {c.NormalizedKey}"))}");
                    if (table.Rows.Count > 0)
                    {
                        sb.AppendLine($"   first record    : {string.Join(" | ", table.Rows[0].Values.Select(kv => $"{kv.Key}={kv.Value}"))}");
                    }
                    ok++;
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"   read            : FAILED after {sw.ElapsedMilliseconds} ms - {ex.Message}");
                    failed++;
                }
                sb.AppendLine();
            }

            sb.AppendLine($"SUMMARY: {ok} table(s) read, {failed} failed, listing {(available == null ? "failed" : "ok")}.");
            if (available != null)
            {
                sb.AppendLine();
                sb.AppendLine($"All {available.Count} tables ETABS offers:");
                foreach (var t in available.OrderBy(t => t.Name)) sb.AppendLine($"   {t.Name}   [key: {t.Key}]");
            }
            return sb.ToString();
        }
    }
}
