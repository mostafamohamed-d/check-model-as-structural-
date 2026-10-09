using System;
using System.Collections.Generic;
using System.Linq;
using ETABSModelDefinitionValidator.Core.Logging;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Providers;
using ETABSModelDefinitionValidator.Excel;

namespace ETABSModelDefinitionValidator.EtabsApi
{
    /// <summary>
    /// Reads the tables the enabled rules need straight from a running ETABS model and returns the
    /// same ModelData the Excel importer produces, so every rule runs unchanged. Only the
    /// requested tables are read (one API call each); tables ETABS does not list under the
    /// expected name are reported as missing, never silently skipped.
    /// </summary>
    public sealed class EtabsModelDataProvider : IModelDataProvider
    {
        private readonly EtabsSession _session;
        private readonly IValidationLogger _logger;
        private readonly HashSet<string> _titles;

        /// <summary>Tables actually read by the last GetModelData call (for export/diagnostics).</summary>
        public IReadOnlyList<RawTable> ExtractedTables { get; private set; } = new List<RawTable>();
        /// <summary>Every table ETABS offers for the model.</summary>
        public IReadOnlyList<EtabsTableInfo> AvailableTables { get; private set; } = new List<EtabsTableInfo>();

        /// <param name="requiredTables">Table titles to read (e.g. the ApplicableTables of the enabled rules). Null = every known table.</param>
        public EtabsModelDataProvider(EtabsSession session, IEnumerable<string> requiredTables = null, IValidationLogger logger = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _logger = logger ?? new NullValidationLogger();
            _titles = new HashSet<string>(requiredTables ?? Core.KnownTables.RequiredForInitialRuleSet, StringComparer.OrdinalIgnoreCase);
        }

        public ModelData GetModelData()
        {
            var listingProblems = new List<string>();
            var available = _session.GetAvailableTables(listingProblems);
            AvailableTables = available ?? new List<EtabsTableInfo>();
            foreach (var p in listingProblems) _logger.Warning(p);

            // If ETABS refuses to list its tables, ask for each one directly by name (= its key in ETABS).
            var byName = new Dictionary<string, EtabsTableInfo>(StringComparer.OrdinalIgnoreCase);
            if (available != null) foreach (var t in available) byName[Normalize(t.Name)] = t;
            else foreach (var title in _titles) byName[Normalize(title)] = new EtabsTableInfo { Key = title, Name = title };

            var tables = new List<RawTable>();
            var notFound = new List<string>();
            var readErrors = new List<(string Title, string Error)>();

            using (_session.UnitsScope())
            {
                foreach (var title in _titles)
                {
                    if (!byName.TryGetValue(Normalize(title), out var info))
                    {
                        notFound.Add(title);
                        continue;
                    }

                    try
                    {
                        var raw = _session.ReadTable(info);
                        var table = ApiTableConverter.Convert(title, raw.FieldKeys, raw.FieldNames, raw.NumberRecords, raw.Data);
                        _logger.Info($"Read '{title}' from ETABS: {table.Rows.Count} record(s), columns: {string.Join(" | ", table.Columns.Select(c => c.OriginalHeader))}");
                        tables.Add(table);
                    }
                    catch (Exception ex)
                    {
                        readErrors.Add((title, ex.Message));
                    }
                }
            }

            ExtractedTables = tables;
            var model = ExcelModelDataProvider.FromTables($"{_session.ModelName} (live ETABS)", tables, _logger);

            // FromTables reports every known table it did not receive as "not found in the workbook".
            // Live, that is wrong twice over: there is no workbook, and tables of unticked categories
            // were deliberately not requested. Replace those generic messages with precise ones below,
            // and mark unrequested tables as skipped (same as the Excel path), not missing.
            model.ImportIssues.RemoveAll(i => i.Source?.WorksheetName == "(missing)");
            foreach (var title in model.MissingTables.Where(t => !_titles.Contains(t)).ToList())
            {
                model.MissingTables.Remove(title);
                model.SkippedTables.Add(title);
            }

            if (available == null)
            {
                model.ImportIssues.Add(new ImportIssue
                {
                    Level = ImportIssueLevel.Info,
                    Message = "ETABS would not list its tables (" + string.Join(" ", listingProblems) + "), so each table was requested directly by name.",
                    Source = new SourceLocation { WorkbookName = model.WorkbookName, WorksheetName = ApiTableConverter.WorksheetLabel, TableName = "(table list)", RowNumber = -1 }
                });
            }

            foreach (var title in notFound)
            {
                model.ImportIssues.Add(new ImportIssue
                {
                    Level = ImportIssueLevel.Warning,
                    Message = $"ETABS does not list a table named '{title}'. Rules depending on it will report NOT_CHECKED. " +
                              $"Tables ETABS offers with a similar name: {Similar(title, available)}",
                    Source = new SourceLocation { WorkbookName = model.WorkbookName, WorksheetName = ApiTableConverter.WorksheetLabel, TableName = title, RowNumber = -1 }
                });
            }
            foreach (var (title, error) in readErrors)
            {
                model.ImportIssues.Add(new ImportIssue
                {
                    Level = ImportIssueLevel.Error,
                    Message = $"Could not read table '{title}': {error} Rules depending on it will report NOT_CHECKED.",
                    Source = new SourceLocation { WorkbookName = model.WorkbookName, WorksheetName = ApiTableConverter.WorksheetLabel, TableName = title, RowNumber = -1 }
                });
            }

            return model;
        }

        private static string Normalize(string s) => new string((s ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

        private static string Similar(string title, IEnumerable<EtabsTableInfo> available)
        {
            var words = title.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3).ToList();
            var hits = available.Where(a => words.Any(w => a.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)).Take(8).Select(a => $"'{a.Name}'").ToList();
            return hits.Count == 0 ? "(none)" : string.Join(", ", hits);
        }
    }
}
