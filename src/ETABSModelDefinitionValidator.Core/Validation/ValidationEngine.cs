using System;
using System.Collections.Generic;
using System.Linq;
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
    }
}
