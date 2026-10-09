using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Logging;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Providers;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.EtabsApi;
using ETABSModelDefinitionValidator.Excel;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    public sealed class ValidationRunResult
    {
        public ModelData Model { get; set; }
        public IReadOnlyList<ValidationResult> Results { get; set; }
        /// <summary>Set only for live-ETABS runs: what was read, for "Save Extracted Tables".</summary>
        public IReadOnlyList<RawTable> ExtractedTables { get; set; }
        public IReadOnlyList<EtabsTableInfo> EtabsTables { get; set; }
    }

    /// <summary>
    /// Orchestrates one end-to-end validation run (load -> import -> validate) off the UI
    /// thread, from either an Excel export or a running ETABS model. Has no WinForms dependency -
    /// it's plain async/Task orchestration over the existing engine, so it's unit-testable on its
    /// own and reusable by any future host. Rules themselves stay synchronous (they're cheap CPU
    /// work); only the whole pipeline runs on a background thread via Task.Run.
    /// </summary>
    public sealed class ValidationRunner
    {
        public Task<ValidationRunResult> RunAsync(
            string excelPath,
            ValidationProfileConfig config,
            IEnumerable<string> enabledCategories,
            IProgress<string> progress,
            CancellationToken cancellationToken,
            IValidationLogger logger = null)
        {
            return RunCoreAsync(
                tables => new ExcelModelDataProvider(excelPath, logger ?? new NullValidationLogger(), tables),
                "Loading workbook...", config, enabledCategories, progress, cancellationToken, logger);
        }

        /// <summary>Same run, but the tables are read from the ETABS instance that is already open.
        /// Only the tables the enabled rules need are requested from ETABS.</summary>
        public Task<ValidationRunResult> RunLiveAsync(
            ValidationProfileConfig config,
            IEnumerable<string> enabledCategories,
            IProgress<string> progress,
            CancellationToken cancellationToken,
            int? etabsProcessId = null,
            IValidationLogger logger = null)
        {
            return RunCoreAsync(
                tables =>
                {
                    progress?.Report("Attaching to running ETABS...");
                    var session = EtabsSession.Attach(etabsProcessId);
                    progress?.Report($"Reading tables from '{session.ModelName}' (ETABS pid {session.ProcessId})...");
                    // Unrestricted (all categories) still means "every table the rule set uses".
                    return new EtabsModelDataProvider(session, tables ?? RuleRegistry.AllRules.SelectMany(r => r.ApplicableTables).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), logger);
                },
                null, config, enabledCategories, progress, cancellationToken, logger);
        }

        private static Task<ValidationRunResult> RunCoreAsync(
            Func<IEnumerable<string>, IModelDataProvider> createProvider,
            string loadingMessage,
            ValidationProfileConfig config,
            IEnumerable<string> enabledCategories,
            IProgress<string> progress,
            CancellationToken cancellationToken,
            IValidationLogger logger)
        {
            return Task.Run(() =>
            {
                logger = logger ?? new NullValidationLogger();
                cancellationToken.ThrowIfCancellationRequested();

                var enabledSet = enabledCategories == null
                    ? null
                    : new HashSet<string>(enabledCategories, StringComparer.OrdinalIgnoreCase);

                var allRules = RuleRegistry.AllRules;
                var effectiveRules = enabledSet == null
                    ? allRules
                    : allRules.Where(r => enabledSet.Contains(r.Category)).ToList();

                // Restricting import to only the tables the enabled rules actually need is what
                // makes disabling a category skip its import cost, not just its rule-execution
                // cost - for Excel via restrictToTables, for ETABS by never requesting the table.
                IEnumerable<string> requiredTables = enabledSet == null
                    ? null
                    : effectiveRules.SelectMany(r => r.ApplicableTables).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                if (loadingMessage != null) progress?.Report(loadingMessage);
                var provider = createProvider(requiredTables);
                var model = provider.GetModelData();
                var live = provider as EtabsModelDataProvider;
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report($"Running {effectiveRules.Count} rule(s)...");
                var engine = new ValidationEngine(logger);
                var results = engine.RunParallel(model, config, effectiveRules, cancellationToken: cancellationToken);

                progress?.Report("Done.");
                return new ValidationRunResult { Model = model, Results = results, ExtractedTables = live?.ExtractedTables, EtabsTables = live?.AvailableTables };
            }, cancellationToken);
        }
    }
}
