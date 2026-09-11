using System.Linq;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;
using Xunit;
using static ETABSModelDefinitionValidator.Tests.TestHelpers;

namespace ETABSModelDefinitionValidator.Tests
{
    public class FrameAutoMeshRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly FrameAutoMeshRule _rule = new FrameAutoMeshRule();

        [Fact]
        public void Pass_WhenAutoMeshIsEnabled()
        {
            var model = new ModelData();
            model.FrameAutoMeshes.Add(new FrameAutoMesh { Story = "L1", Label = "B1", UniqueName = "101", AutoMeshEnabled = true, Source = Loc("Mesh") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Pass, results[0].Status);
        }

        [Fact]
        public void Fail_WhenAutoMeshIsDisabled()
        {
            var model = new ModelData();
            model.FrameAutoMeshes.Add(new FrameAutoMesh { Story = "L1", Label = "B1", UniqueName = "101", AutoMeshEnabled = false, Source = Loc("Mesh") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.Fail, results[0].Status);
        }

        [Fact]
        public void NotChecked_WhenTableIsMissing()
        {
            var model = new ModelData();
            model.MissingTables.Add(Core.KnownTables.FrameAssignmentsFrameAutoMeshOptions);

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.NotChecked, results[0].Status);
        }
    }

    public class FrameOffsetRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();
        private readonly FrameEndOffsetRule _rule = new FrameEndOffsetRule();

        private static ModelData ModelWithOffsets(double i, double j) => Model(i, j);

        private static ModelData Model(double i, double j)
        {
            var model = new ModelData();
            model.FrameEndOffsets.Add(new FrameEndOffset { Story = "ROOF LEVEL", Label = "C143", UniqueName = "7515", OffsetIMm = i, OffsetJMm = j, Source = Loc("Offsets") });
            return model;
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1200)]
        [InlineData(700, 0)]
        public void Pass_WhenBothOffsetsAreWholeNumbers(double i, double j)
        {
            var results = _rule.Validate(ModelWithOffsets(i, j), _config).ToList();

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal(ValidationStatus.Pass, r.Status));
        }

        [Fact]
        public void Fail_ForKnownNonIntegerCase_C143OffsetJ949Point7()
        {
            var results = _rule.Validate(ModelWithOffsets(0, 949.7), _config).ToList();

            var offsetJ = results.Single(r => r.FieldName == "Offset J");
            Assert.Equal(ValidationStatus.Fail, offsetJ.Status);
            Assert.Equal(ValidationStatus.Pass, results.Single(r => r.FieldName == "Offset I").Status);
        }

        [Fact]
        public void Fail_ForNonIntegerOffset_700Point5()
        {
            var results = _rule.Validate(ModelWithOffsets(700.5, 0), _config).ToList();

            Assert.Equal(ValidationStatus.Fail, results.Single(r => r.FieldName == "Offset I").Status);
        }

        [Fact]
        public void Pass_WithinConfiguredTolerance()
        {
            var config = ConfigurationLoader.LoadDefault();
            config.Frames.IntegerTolerance = 0.01;

            var results = _rule.Validate(ModelWithOffsets(700.005, 0), config).ToList();

            Assert.Equal(ValidationStatus.Pass, results.Single(r => r.FieldName == "Offset I").Status);
        }

        [Fact]
        public void NotChecked_WhenTableIsMissing()
        {
            var model = new ModelData();
            model.MissingTables.Add(Core.KnownTables.FrameAssignmentsEndLengthOffsets);

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.NotChecked, results[0].Status);
        }
    }
}
