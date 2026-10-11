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
        /// <summary>Counts plus the run verdict (computed with the import state, enabled rules and profile).</summary>
        public ValidationSummary Summary { get; set; }
        /// <summary>Profile warnings found before the run (errors would have stopped it).</summary>
        public IReadOnlyList<ConfigIssue> ConfigIssues { get; set; }
        /// <summary>Set only for live-ETABS runs: what was read, for "Save Extracted Tables".</summary>
        public IReadOnlyList<RawTable> ExtractedTables { get; set; }
        public IReadOnlyList<EtabsTableInfo> EtabsTables { get; set; }
        /// <summary>Live runs only: the ETABS process and model file the results were read from.
        /// Group write-back goes to exactly this model, never to whatever is connected later.</summary>
        public int? EtabsProcessId { get; set; }
        public string EtabsModelPath { get; set; }
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
                (tables, liveData) => new ExcelModelDataProvider(excelPath, logger ?? new NullValidationLogger(), tables),
                "Loading workbook...", config, enabledCategories, progress, cancellationToken, logger);
        }

        /// <summary>Same run, but the tables are read from the ETABS instance that is already open.
        /// Only the tables the enabled rules need are requested from ETABS.</summary>
        public async Task<ValidationRunResult> RunLiveAsync(
            ValidationProfileConfig config,
            IEnumerable<string> enabledCategories,
            IProgress<string> progress,
            CancellationToken cancellationToken,
            int? etabsProcessId = null,
            IValidationLogger logger = null)
        {
            EtabsSession attached = null;
            var run = await RunCoreAsync(
                (tables, liveData) =>
                {
                    progress?.Report("Attaching to running ETABS...");
                    var session = attached = EtabsSession.Attach(etabsProcessId);
                    progress?.Report($"Reading tables from '{session.ModelName}' (ETABS pid {session.ProcessId})...");
                    // Unrestricted (all categories) still means "every table the rule set uses".
                    return new EtabsModelDataProvider(session, tables ?? RuleRegistry.AllRules.SelectMany(r => r.ApplicableTables).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), logger, liveData);
                },
                null, config, enabledCategories, progress, cancellationToken, logger).ConfigureAwait(false);
            run.EtabsProcessId = attached?.ProcessId;
            run.EtabsModelPath = attached?.ModelFilePath;
            return run;
        }

        /// <summary>Writes the failing objects of a live run into their checks' ETABS groups (VChk_*),
        /// emptying each group first. Modifies the model - callers must confirm with the user. Refuses
        /// unless the run's own ETABS process still has the same model open: unique names are only
        /// meaningful in the model they were read from.</summary>
        public Task<List<GroupWriteResult>> WriteFailureGroupsAsync(ValidationRunResult run, IEnumerable<string> ruleIds)
        {
            if (run?.EtabsProcessId == null) throw new InvalidOperationException("Failure groups can only be written after a run on a live ETABS model.");
            var objectsByGroup = BuildFailureGroups(run.Results, ruleIds);
            return Task.Run(() =>
            {
                var session = EtabsSession.Attach(run.EtabsProcessId.Value);
                if (!string.Equals(session.ModelFilePath ?? "", run.EtabsModelPath ?? "", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"ETABS (pid {session.ProcessId}) now has '{session.ModelName}' open, not the model these results came from " +
                        $"('{System.IO.Path.GetFileName(run.EtabsModelPath)}'). Run the validation again before writing groups.");
                return session.WriteFailureGroups(objectsByGroup);
            });
        }

        /// <summary>Group name -> failing objects, for every rule in <paramref name="ruleIds"/> that has a
        /// failure group. Rules with no failures map to an empty set, so their old group gets emptied.</summary>
        public static Dictionary<string, EtabsObjectRefs> BuildFailureGroups(IEnumerable<ValidationResult> results, IEnumerable<string> ruleIds)
        {
            var map = new Dictionary<string, EtabsObjectRefs>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ruleIds)
            {
                var group = FailureGroups.For(id);
                if (group != null && !map.ContainsKey(group)) map[group] = new EtabsObjectRefs();
            }
            foreach (var r in results.Where(r => r.Status == ValidationStatus.Fail && r.Objects != null))
            {
                var group = FailureGroups.For(r.RuleId);
                if (group == null || !map.TryGetValue(group, out var refs)) continue;
                refs.Points.AddRange(r.Objects.Points);
                refs.Frames.AddRange(r.Objects.Frames);
                refs.Areas.AddRange(r.Objects.Areas);
                refs.Links.AddRange(r.Objects.Links);
            }
            return map;
        }

        private static Task<ValidationRunResult> RunCoreAsync(
            Func<IEnumerable<string>, LiveData, IModelDataProvider> createProvider,
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

                // Engineering validation of the profile before any data is read; errors stop the run.
                var configIssues = ConfigurationValidator.EnsureValid(config);
                foreach (var issue in configIssues) logger.Warning("Profile: " + issue);

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
                var liveData = effectiveRules.OfType<IRequiresLiveData>().Aggregate(LiveData.None, (all, r) => all | r.RequiredLiveData);
                var provider = createProvider(requiredTables, liveData);
                var model = provider.GetModelData();
                var live = provider as EtabsModelDataProvider;
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report($"Running {effectiveRules.Count} rule(s)...");
                var engine = new ValidationEngine(logger);
                var results = engine.RunParallel(model, config, effectiveRules, cancellationToken: cancellationToken);
                RuleCatalog.Annotate(results, config);

                // Verdict from results + import completeness + profile policy, with the run's scope.
                var summary = ValidationSummary.From(results, model, effectiveRules, config.Reporting);
                summary.Assessment.CategoriesRun = effectiveRules.Select(r => r.Category).Distinct().Count();
                summary.Assessment.CategoriesTotal = allRules.Select(r => r.Category).Distinct().Count();

                progress?.Report("Done.");
                return new ValidationRunResult
                {
                    Model = model, Results = results, Summary = summary, ConfigIssues = configIssues,
                    ExtractedTables = live?.ExtractedTables, EtabsTables = live?.AvailableTables
                };
            }, cancellationToken);
        }
    }
}
