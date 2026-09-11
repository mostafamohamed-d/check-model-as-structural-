using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Logging;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Excel;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.UI.WinForms
{
    public sealed class ValidationRunResult
    {
        public ModelData Model { get; set; }
        public IReadOnlyList<ValidationResult> Results { get; set; }
    }

    /// <summary>
    /// Orchestrates one end-to-end validation run (load -> import -> validate) off the UI
    /// thread. Has no WinForms dependency - it's plain async/Task orchestration over the
    /// existing engine, so it's unit-testable on its own and reusable by any future host.
    /// Rules themselves stay synchronous (they're cheap CPU work); only the whole pipeline runs
    /// on a background thread via Task.Run, which is what keeps a UI responsive without forcing
    /// every rule to become async for no benefit.
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
                // cost - see ExcelModelDataProvider's restrictToTables parameter.
                IEnumerable<string> requiredTables = enabledSet == null
                    ? null
                    : effectiveRules.SelectMany(r => r.ApplicableTables).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                progress?.Report("Loading workbook...");
                var provider = new ExcelModelDataProvider(excelPath, logger, requiredTables);
                var model = provider.GetModelData();
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report($"Running {effectiveRules.Count} rule(s)...");
                var engine = new ValidationEngine(logger);
                var results = engine.RunParallel(model, config, effectiveRules, cancellationToken: cancellationToken);

                progress?.Report("Done.");
                return new ValidationRunResult { Model = model, Results = results };
            }, cancellationToken);
        }
    }
}
