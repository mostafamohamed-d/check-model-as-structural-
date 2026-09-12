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

        // The rule now only checks frames it can confirm are columns (cross-referenced via
        // Concrete Column Overwrites), so every fixture that expects a check to run must also
        // register the same UniqueName as a column there.
        private static ModelData ModelWithOffsets(double i, double j)
        {
            var model = new ModelData();
            model.FrameEndOffsets.Add(new FrameEndOffset { Story = "ROOF LEVEL", Label = "C143", UniqueName = "7515", OffsetIMm = i, OffsetJMm = j, Source = Loc("Offsets") });
            model.ColumnDesignOverwrites.Add(new ColumnDesignOverwrite { Story = "ROOF LEVEL", Label = "C143", UniqueName = "7515", Source = Loc("ColOver") });
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

        [Fact]
        public void NotChecked_WhenColumnOverwritesTableIsMissing()
        {
            var model = new ModelData();
            model.FrameEndOffsets.Add(new FrameEndOffset { Story = "L1", Label = "C1", UniqueName = "1", OffsetIMm = 0, OffsetJMm = 949.7, Source = Loc("Offsets") });
            model.MissingTables.Add(Core.KnownTables.ConcreteColumnOverwritesAci31819);

            var results = _rule.Validate(model, _config).ToList();

            Assert.Single(results);
            Assert.Equal(ValidationStatus.NotChecked, results[0].Status);
        }

        [Fact]
        public void Beam_WithNonIntegerOffset_IsNotChecked_OnlyColumnsAre()
        {
            var model = new ModelData();
            // Beam B117 - not present in ColumnDesignOverwrites, so it's not a column.
            model.FrameEndOffsets.Add(new FrameEndOffset { Story = "ROOF LEVEL", Label = "B117", UniqueName = "4666", OffsetIMm = 350, OffsetJMm = 275.1, Source = Loc("Offsets") });
            // Column C143 - present in ColumnDesignOverwrites, so it is checked.
            model.FrameEndOffsets.Add(new FrameEndOffset { Story = "ROOF LEVEL", Label = "C143", UniqueName = "7515", OffsetIMm = 0, OffsetJMm = 949.7, Source = Loc("Offsets") });
            model.ColumnDesignOverwrites.Add(new ColumnDesignOverwrite { Story = "ROOF LEVEL", Label = "C143", UniqueName = "7515", Source = Loc("ColOver") });

            var results = _rule.Validate(model, _config).ToList();

            Assert.All(results, r => Assert.Equal("C143", r.ObjectName));
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void RestrictIntegerOffsetCheckToColumns_False_ChecksBeamsToo()
        {
            var config = ConfigurationLoader.LoadDefault();
            config.Frames.RestrictIntegerOffsetCheckToColumns = false;

            var model = new ModelData();
            model.FrameEndOffsets.Add(new FrameEndOffset { Story = "ROOF LEVEL", Label = "B117", UniqueName = "4666", OffsetIMm = 350, OffsetJMm = 275.1, Source = Loc("Offsets") });

            var results = _rule.Validate(model, config).ToList();

            Assert.Equal(2, results.Count);
            Assert.Equal(ValidationStatus.Fail, results.Single(r => r.FieldName == "Offset J").Status);
        }
    }
}
