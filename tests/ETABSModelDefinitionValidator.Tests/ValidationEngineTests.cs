using System.Collections.Generic;
using System.Linq;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using Xunit;

namespace ETABSModelDefinitionValidator.Tests
{
    /// <summary>A minimal rule whose behavior the test controls directly, so these tests exercise
    /// ValidationEngine's dispatch/isolation/merge logic without depending on any real rule.</summary>
    file sealed class FakeRule : IValidationRule
    {
        private readonly System.Func<ModelData, ValidationProfileConfig, IEnumerable<ValidationResult>> _validate;

        public FakeRule(string ruleId, string category, System.Func<ModelData, ValidationProfileConfig, IEnumerable<ValidationResult>> validate)
        {
            RuleId = ruleId;
            Category = category;
            _validate = validate;
        }

        public string RuleId { get; }
        public string Name => RuleId;
        public string Category { get; }
        public RuleType Type => RuleType.BestPractice;
        public Severity DefaultSeverity => Severity.Low;
        public IReadOnlyList<string> ApplicableTables => new string[0];

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config) => _validate(model, config);
    }

    public class ValidationEngineTests
    {
        private static ValidationResult Result(string ruleId) => ValidationResult.Create(
            ruleId, ruleId, "Cat", RuleType.BestPractice, Severity.Low, ValidationStatus.Pass,
            "Obj", ruleId, SourceLocation.Unknown);

        [Fact]
        public void RunParallel_ProducesSameResultSetAsRun()
        {
            var model = new ModelData();
            var config = ConfigurationLoader.LoadDefault();
            var rules = new List<IValidationRule>
            {
                new FakeRule("R1", "A", (m, c) => new[] { Result("R1") }),
                new FakeRule("R2", "A", (m, c) => new[] { Result("R2"), Result("R2b") }),
                new FakeRule("R3", "B", (m, c) => System.Linq.Enumerable.Empty<ValidationResult>())
            };

            var engine = new ValidationEngine();
            var sequential = engine.Run(model, config, rules).Select(r => r.RuleId).OrderBy(x => x).ToList();
            var parallel = engine.RunParallel(model, config, rules).Select(r => r.RuleId).OrderBy(x => x).ToList();

            Assert.Equal(sequential, parallel);
        }

        [Fact]
        public void RunParallel_PreservesOriginalRuleOrderInOutput()
        {
            var model = new ModelData();
            var config = ConfigurationLoader.LoadDefault();
            var rules = new List<IValidationRule>
            {
                new FakeRule("R1", "A", (m, c) => new[] { Result("R1") }),
                new FakeRule("R2", "A", (m, c) => new[] { Result("R2") }),
                new FakeRule("R3", "A", (m, c) => new[] { Result("R3") })
            };

            var results = new ValidationEngine().RunParallel(model, config, rules);

            Assert.Equal(new[] { "R1", "R2", "R3" }, results.Select(r => r.RuleId));
        }

        [Fact]
        public void RunParallel_IsolatesAThrowingRule_WithoutAffectingOthers()
        {
            var model = new ModelData();
            var config = ConfigurationLoader.LoadDefault();
            var rules = new List<IValidationRule>
            {
                new FakeRule("Good1", "A", (m, c) => new[] { Result("Good1") }),
                new FakeRule("Bad", "A", (m, c) => throw new System.InvalidOperationException("boom")),
                new FakeRule("Good2", "A", (m, c) => new[] { Result("Good2") })
            };

            var results = new ValidationEngine().RunParallel(model, config, rules);

            Assert.Equal(3, results.Count);
            Assert.Equal(ValidationStatus.Pass, results.Single(r => r.RuleId == "Good1").Status);
            Assert.Equal(ValidationStatus.Pass, results.Single(r => r.RuleId == "Good2").Status);
            var badResult = results.Single(r => r.RuleId == "Bad");
            Assert.Equal(ValidationStatus.NotChecked, badResult.Status);
            Assert.Contains("boom", badResult.TechnicalExplanation);
        }

        [Fact]
        public void RunParallel_RespectsEnabledCategoriesFilter()
        {
            var model = new ModelData();
            var config = ConfigurationLoader.LoadDefault();
            var rules = new List<IValidationRule>
            {
                new FakeRule("R1", "A", (m, c) => new[] { Result("R1") }),
                new FakeRule("R2", "B", (m, c) => new[] { Result("R2") })
            };

            var results = new ValidationEngine().RunParallel(model, config, rules, enabledCategories: new[] { "A" });

            Assert.Single(results);
            Assert.Equal("R1", results[0].RuleId);
        }
    }
}
