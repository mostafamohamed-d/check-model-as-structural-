using System;
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

            return rules
                .Where(rule => enabledSet == null || enabledSet.Contains(rule.Category))
                .SelectMany(rule => Execute(rule, model, config))
                .ToList();
        }

        /// <summary>Runs one rule in isolation: a throwing rule becomes a NOT_CHECKED result instead of
        /// aborting the run, and every rule's output goes through <see cref="Finalize"/>.</summary>
        private List<ValidationResult> Execute(IValidationRule rule, ModelData model, ValidationProfileConfig config)
        {
            try
            {
                var ruleResults = Finalize(rule, rule.Validate(model, config)?.ToList(), model);
                _logger.Info($"Rule {rule.RuleId} ({rule.Name}) produced {ruleResults.Count} result(s).");
                return ruleResults;
            }
            catch (Exception ex)
            {
                _logger.Error($"Rule {rule.RuleId} ({rule.Name}) threw an unhandled exception: {ex.Message}");
                return new List<ValidationResult>
                {
                    ValidationResult.Create(
                        rule.RuleId, rule.Name, rule.Category, rule.Type, Severity.High,
                        ValidationStatus.NotChecked, objectType: "Rule", objectName: rule.RuleId,
                        source: SourceLocation.Unknown,
                        message: "Rule execution failed with an unhandled exception; see log for details.",
                        technicalExplanation: ex.ToString())
                };
            }
        }

        /// <summary>
        /// Same contract as Run, but independent rules execute in parallel - safe because every
        /// IValidationRule.Validate is a pure read-only function over ModelData (no mutation, no
        /// dependency on another rule's results). Exception isolation matches Run exactly: one
        /// rule throwing still yields a single NotChecked result for that rule, never aborts the
        /// others. Results are merged back in the original rule order (not completion order) so
        /// output stays reproducible/diffable across runs, matching Run's behavior.
        /// </summary>
        /// <summary>
        /// Post-processing applied to every rule's output, so no rule can report silence as success:
        /// (1) a rule that produced no results gets an explicit NOT_CHECKED result - an empty collection
        /// is never evidence that there are no problems; (2) NOT_CHECKED results raised because a table
        /// is unusable get the precise import reason (schema invalid, failed, ambiguous...) instead of a
        /// generic "not found".
        /// </summary>
        /// <summary>FieldName marker on the engine's NOT_CHECKED result for a rule whose tables were usable
        /// but held nothing applicable. RunAssessment treats it as REVIEW REQUIRED, not INCOMPLETE.</summary>
        public const string NoApplicableObjects = "(no applicable objects)";

        internal static List<ValidationResult> Finalize(IValidationRule rule, List<ValidationResult> results, ModelData model)
        {
            results = results ?? new List<ValidationResult>();

            if (results.Count == 0)
            {
                var tables = rule.ApplicableTables ?? new string[0];
                var states = model == null ? "" : string.Join("; ", tables.Select(t =>
                {
                    var report = model.GetTableReport(t);
                    return report == null ? $"'{t}'" : $"'{t}' {report.State}, {report.AcceptedCount} record(s)";
                }));
                // Usable tables but nothing to check = the model may genuinely have no such objects:
                // marked for REVIEW (an engineer confirms). Otherwise the evidence is missing: INCOMPLETE.
                // Requires positive evidence (an import report saying the table was read) - never inferred.
                var tablesUsable = model != null && tables.Count > 0 && tables.All(t => model.GetTableReport(t)?.IsUsable == true);
                results.Add(ValidationResult.Create(
                    rule.RuleId, rule.Name, rule.Category, rule.Type, Severity.Medium,
                    ValidationStatus.NotChecked, objectType: "Rule", objectName: rule.RuleId,
                    source: SourceLocation.Unknown,
                    fieldName: tablesUsable ? NoApplicableObjects : null,
                    message: "The check produced no results, so nothing was evaluated - " +
                             (tablesUsable
                                 ? "its tables were read but contain no applicable objects, or the check is switched off in the profile. Confirm the model really has none before relying on this run."
                                 : "the source has no usable data for it, or the check is switched off in the profile.") +
                             (states.Length > 0 ? $" Tables: {states}." : "")));
                return results;
            }

            if (model != null)
            {
                foreach (var r in results.Where(r => r.Status == ValidationStatus.NotChecked && r.ObjectType == "Table"))
                {
                    var problem = model.TableProblem(r.ObjectName);
                    if (problem != null) r.Message = $"Required ETABS table '{r.ObjectName}' {problem}. Rule could not be evaluated.";
                }
            }

            return results;
        }

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

            // One slot per rule, written only by that rule's iteration: no locking, original rule order kept.
            var resultsByRule = new List<ValidationResult>[ruleList.Count];
            Parallel.For(0, ruleList.Count, new ParallelOptions { CancellationToken = cancellationToken },
                i => resultsByRule[i] = Execute(ruleList[i], model, config));

            return resultsByRule.SelectMany(r => r).ToList();
        }
    }
}
