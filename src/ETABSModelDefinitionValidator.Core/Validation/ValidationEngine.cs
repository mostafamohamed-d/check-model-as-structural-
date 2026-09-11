using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Logging;
using ETABSModelDefinitionValidator.Core.Model;

namespace ETABSModelDefinitionValidator.Core.Validation
{
    /// <summary>
    /// Runs a set of rules against one ModelData and collects results. One failing rule never
    /// aborts the run - each rule is isolated so a bug in one check can't hide the results of
    /// the others.
    /// </summary>
    public sealed class ValidationEngine
    {
        private readonly IValidationLogger _logger;

        public ValidationEngine(IValidationLogger logger = null)
        {
            _logger = logger ?? new NullValidationLogger();
        }

        public IReadOnlyList<ValidationResult> Run(
            ModelData model,
            ValidationProfileConfig config,
            IEnumerable<IValidationRule> rules,
            IEnumerable<string> enabledCategories = null)
        {
            var enabledSet = enabledCategories == null
                ? null
                : new HashSet<string>(enabledCategories, StringComparer.OrdinalIgnoreCase);

            var results = new List<ValidationResult>();

            foreach (var rule in rules)
            {
                if (enabledSet != null && !enabledSet.Contains(rule.Category))
                {
                    continue;
                }

                try
                {
                    var ruleResults = rule.Validate(model, config)?.ToList() ?? new List<ValidationResult>();
                    results.AddRange(ruleResults);
                    _logger.Info($"Rule {rule.RuleId} ({rule.Name}) produced {ruleResults.Count} result(s).");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Rule {rule.RuleId} ({rule.Name}) threw an unhandled exception: {ex.Message}");
                    results.Add(ValidationResult.Create(
                        rule.RuleId, rule.Name, rule.Category, rule.Type, Severity.High,
                        ValidationStatus.NotChecked, objectType: "Rule", objectName: rule.RuleId,
                        source: SourceLocation.Unknown,
                        message: "Rule execution failed with an unhandled exception; see log for details.",
                        technicalExplanation: ex.ToString()));
                }
            }

            return results;
        }

        /// <summary>
        /// Same contract as Run, but independent rules execute in parallel - safe because every
        /// IValidationRule.Validate is a pure read-only function over ModelData (no mutation, no
        /// dependency on another rule's results). Exception isolation matches Run exactly: one
        /// rule throwing still yields a single NotChecked result for that rule, never aborts the
        /// others. Results are merged back in the original rule order (not completion order) so
        /// output stays reproducible/diffable across runs, matching Run's behavior.
        /// </summary>
        public IReadOnlyList<ValidationResult> RunParallel(
            ModelData model,
            ValidationProfileConfig config,
            IEnumerable<IValidationRule> rules,
            IEnumerable<string> enabledCategories = null,
            CancellationToken cancellationToken = default)
        {
            var enabledSet = enabledCategories == null
                ? null
                : new HashSet<string>(enabledCategories, StringComparer.OrdinalIgnoreCase);

            var ruleList = rules
                .Where(r => enabledSet == null || enabledSet.Contains(r.Category))
                .ToList();

            var resultsByRule = new ConcurrentDictionary<int, List<ValidationResult>>();

            Parallel.For(0, ruleList.Count, new ParallelOptions { CancellationToken = cancellationToken }, i =>
            {
                var rule = ruleList[i];
                try
                {
                    var ruleResults = rule.Validate(model, config)?.ToList() ?? new List<ValidationResult>();
                    resultsByRule[i] = ruleResults;
                    _logger.Info($"Rule {rule.RuleId} ({rule.Name}) produced {ruleResults.Count} result(s).");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Rule {rule.RuleId} ({rule.Name}) threw an unhandled exception: {ex.Message}");
                    resultsByRule[i] = new List<ValidationResult>
                    {
                        ValidationResult.Create(
                            rule.RuleId, rule.Name, rule.Category, rule.Type, Severity.High,
                            ValidationStatus.NotChecked, objectType: "Rule", objectName: rule.RuleId,
                            source: SourceLocation.Unknown,
                            message: "Rule execution failed with an unhandled exception; see log for details.",
                            technicalExplanation: ex.ToString())
                    };
                }
            });

            var results = new List<ValidationResult>();
            for (var i = 0; i < ruleList.Count; i++)
            {
                results.AddRange(resultsByRule[i]);
            }

            return results;
        }
    }
}
