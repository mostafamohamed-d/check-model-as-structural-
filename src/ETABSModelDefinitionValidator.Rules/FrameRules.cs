using System;
using System.Collections.Generic;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Core;

namespace ETABSModelDefinitionValidator.Rules
{
    /// <summary>FRAME-001: every frame must have Auto Mesh enabled.</summary>
    public sealed class FrameAutoMeshRule : IValidationRule
    {
        public string RuleId => RuleIds.Frame001;
        public string Name => "Frame Auto Mesh Enabled";
        public string Category => Categories.Frames;
        public RuleType Type => RuleType.ModelingStandard;
        public Severity DefaultSeverity => Severity.Medium;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.FrameAssignmentsFrameAutoMeshOptions };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (!config.Frames.RequireAutoMesh)
            {
                yield break;
            }

            if (!model.WasTableFound(KnownTables.FrameAssignmentsFrameAutoMeshOptions))
            {
                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                    "Table", KnownTables.FrameAssignmentsFrameAutoMeshOptions, SourceLocation.Unknown,
                    message: "Required ETABS table was not found in the workbook. Rule could not be evaluated.");
                yield break;
            }

            foreach (var frame in model.FrameAutoMeshes)
            {
                var status = frame.AutoMeshEnabled ? ValidationStatus.Pass : ValidationStatus.Fail;

                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, DefaultSeverity, status,
                    "Frame", frame.Label, frame.Source, fieldName: "AutoMesh", story: frame.Story,
                    expected: "Yes", actual: frame.AutoMeshEnabled ? "Yes" : "No",
                    message: status == ValidationStatus.Pass
                        ? $"Frame '{frame.Label}' (unique name {frame.UniqueName}) has Auto Mesh enabled."
                        : $"Frame '{frame.Label}' (unique name {frame.UniqueName}) on story '{frame.Story}' does not have Auto Mesh enabled.");
            }
        }
    }

    /// <summary>FRAME-002: end length offsets (I and J) must be whole numbers, within a configurable tolerance.</summary>
    public sealed class FrameEndOffsetRule : IValidationRule
    {
        public string RuleId => RuleIds.Frame002;
        public string Name => "Frame End Length Offsets Are Whole Numbers";
        public string Category => Categories.Frames;
        public RuleType Type => RuleType.ModelingStandard;
        public Severity DefaultSeverity => Severity.Low;
        public IReadOnlyList<string> ApplicableTables => new[] { KnownTables.FrameAssignmentsEndLengthOffsets };

        public IEnumerable<ValidationResult> Validate(ModelData model, ValidationProfileConfig config)
        {
            if (!config.Frames.RequireIntegerEndOffsets)
            {
                yield break;
            }

            if (!model.WasTableFound(KnownTables.FrameAssignmentsEndLengthOffsets))
            {
                yield return ValidationResult.Create(
                    RuleId, Name, Category, Type, Severity.Medium, ValidationStatus.NotChecked,
                    "Table", KnownTables.FrameAssignmentsEndLengthOffsets, SourceLocation.Unknown,
                    message: "Required ETABS table was not found in the workbook. Rule could not be evaluated.");
                yield break;
            }

            var tolerance = config.Frames.IntegerTolerance;

            foreach (var offset in model.FrameEndOffsets)
            {
                foreach (var (end, value) in new[] { ("I", offset.OffsetIMm), ("J", offset.OffsetJMm) })
                {
                    var isIntegral = Math.Abs(value - Math.Round(value)) < tolerance;
                    var status = isIntegral ? ValidationStatus.Pass : ValidationStatus.Fail;

                    yield return ValidationResult.Create(
                        RuleId, Name, Category, Type, DefaultSeverity, status,
                        "Frame", offset.Label, offset.Source, fieldName: $"Offset {end}", story: offset.Story,
                        expected: "Whole number", actual: value.ToString("0.####"),
                        message: status == ValidationStatus.Pass
                            ? $"Offset {end} ({value}) is a whole number."
                            : $"Frame '{offset.Label}' on story '{offset.Story}': Offset {end} = {value}, which is not a whole number.");
                }
            }
        }
    }
}
