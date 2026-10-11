using System;
using System.IO;
using System.Linq;
using System.Threading;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.UI.WinForms;

namespace ETABSModelDefinitionValidator.Tests
{
    /// <summary>A structurally valid JSON file is not necessarily an engineering-valid profile.</summary>
    public class ConfigurationValidationTests : IDisposable
    {
        private readonly string _tmp = Path.Combine(Path.GetTempPath(), $"profile-{Guid.NewGuid():N}.json");
        public void Dispose() { if (File.Exists(_tmp)) File.Delete(_tmp); }

        private static ValidationProfileConfig Valid() => ConfigurationLoader.LoadDefault();

        private static void AssertError(ValidationProfileConfig c, string path)
        {
            var issues = ConfigurationValidator.Validate(c);
            Assert.Contains(issues, i => i.Level == ConfigIssueLevel.Error && i.Path.StartsWith(path));
            Assert.Throws<InvalidConfigurationException>(() => ConfigurationValidator.EnsureValid(c));
        }

        [Fact]
        public void Blank_brace_label_prefix_is_rejected_because_it_would_match_every_frame()
        {
            var c = Valid(); c.Geometry.BraceLabelPrefixes = new System.Collections.Generic.List<string> { "D", " " };
            AssertError(c, "Geometry.BraceLabelPrefixes");
        }

        [Fact]
        public void Default_brace_label_prefix_is_D()
        {
            Assert.Equal(new[] { "D" }, Valid().Geometry.BraceLabelPrefixes);
            Assert.Equal(new[] { "D" }, ConfigurationLoader.LoadFromFile(CorruptedImportTests.ConfigPath()).Geometry.BraceLabelPrefixes);
        }

        // ---- the profiles we ship are valid --------------------------------------------------------

        [Fact]
        public void Built_in_default_profile_is_valid()
        {
            Assert.DoesNotContain(ConfigurationValidator.Validate(Valid()), i => i.Level == ConfigIssueLevel.Error);
        }

        [Fact]
        public void Shipped_json_profile_loads_strictly_and_is_valid()
        {
            var c = ConfigurationLoader.LoadFromFile(CorruptedImportTests.ConfigPath());
            Assert.DoesNotContain(ConfigurationValidator.Validate(c), i => i.Level == ConfigIssueLevel.Error);
        }

        // ---- malformed files ----------------------------------------------------------------------

        [Fact]
        public void Unknown_or_mistyped_setting_is_rejected_not_ignored()
        {
            File.WriteAllText(_tmp, "{ \"Modal\": { \"MinimumTargetRatioPrecent\": 50 } }");
            var ex = Assert.Throws<InvalidDataException>(() => ConfigurationLoader.LoadFromFile(_tmp));
            Assert.Contains("MinimumTargetRatioPrecent", ex.Message);
        }

        [Fact]
        public void Malformed_json_is_rejected_with_the_file_named()
        {
            File.WriteAllText(_tmp, "{ \"Walls\": { \"DefaultModifierValue\": 0.7, ");
            var ex = Assert.Throws<InvalidDataException>(() => ConfigurationLoader.LoadFromFile(_tmp));
            Assert.Contains(_tmp, ex.Message);
        }

        [Fact]
        public void Null_section_is_an_error()
        {
            File.WriteAllText(_tmp, "{ \"Walls\": null }");
            AssertError(ConfigurationLoader.LoadFromFile(_tmp), "Walls");
        }

        // ---- engineering validity -----------------------------------------------------------------

        [Fact] public void Unsupported_code_profile() { var c = Valid(); c.CodeProfile = "ACI318-14"; AssertError(c, "CodeProfile"); }
        [Fact] public void Unknown_review_severity() { var c = Valid(); c.Reporting.ReviewSeverity = "Highh"; AssertError(c, "Reporting.ReviewSeverity"); }
        [Fact] public void Numeric_review_severity_is_not_accepted() { var c = Valid(); c.Reporting.ReviewSeverity = "7"; AssertError(c, "Reporting.ReviewSeverity"); }
        [Fact] public void Negative_rebar_tolerance() { var c = Valid(); c.RebarMaterials.ToleranceMpa = -1; AssertError(c, "RebarMaterials.ToleranceMpa"); }
        [Fact] public void Rebar_tolerance_so_large_it_accepts_another_grade() { var c = Valid(); c.RebarMaterials.ToleranceMpa = 80; AssertError(c, "RebarMaterials.ToleranceMpa"); }
        [Fact] public void Integer_tolerance_of_half_accepts_everything() { var c = Valid(); c.Frames.IntegerTolerance = 0.5; AssertError(c, "Frames.IntegerTolerance"); }
        [Fact] public void Zero_wall_modifier() { var c = Valid(); c.Walls.DefaultModifierValue = 0; AssertError(c, "Walls.DefaultModifierValue"); }
        [Fact] public void Unknown_wall_modifier_name() { var c = Valid(); c.Walls.CheckedModifiers.Add("m33"); AssertError(c, "Walls.CheckedModifiers"); }
        [Fact] public void Fu_below_Fy() { var c = Valid(); c.RebarMaterials.GradeProfiles["Fy420"].FuMpa = 400; AssertError(c, "RebarMaterials.GradeProfiles.Fy420.FuMpa"); }
        [Fact] public void Missing_rebar_profiles() { var c = Valid(); c.RebarMaterials.GradeProfiles.Clear(); AssertError(c, "RebarMaterials.GradeProfiles"); }
        [Fact] public void Unknown_rigidity_type() { var c = Valid(); c.Diaphragm.RequiredRigidityType = "SemiRigid"; AssertError(c, "Diaphragm.RequiredRigidityType"); }
        [Fact] public void Modal_ratio_above_100() { var c = Valid(); c.Modal.MinimumTargetRatioPercent = 120; AssertError(c, "Modal.MinimumTargetRatioPercent"); }

        [Fact]
        public void Modal_ratio_below_90_is_a_warning_not_an_error()
        {
            var c = Valid(); c.Modal.MinimumTargetRatioPercent = 85;
            var issues = ConfigurationValidator.Validate(c);
            Assert.Contains(issues, i => i.Level == ConfigIssueLevel.Warning && i.Path == "Modal.MinimumTargetRatioPercent");
            Assert.DoesNotContain(issues, i => i.Level == ConfigIssueLevel.Error);
        }

        [Fact]
        public void Slab_prefix_claimed_by_two_classes()
        {
            var c = Valid(); c.Slabs.Classifications["Ramp"].NamePrefixes.Add("PT");
            AssertError(c, "Slabs.Classifications");
        }

        [Fact]
        public void Prefix_that_only_starts_another_is_fine_because_the_parser_matches_whole_prefixes()
        {
            // SlabPropertyNameParser takes the whole letter prefix ("PT" from "PT250-C40") and compares for equality.
            var c = Valid(); c.Slabs.Classifications["Stair"].NamePrefixes.Add("P");
            Assert.DoesNotContain(ConfigurationValidator.Validate(c), i => i.Level == ConfigIssueLevel.Error);
        }

        [Fact]
        public void Prefix_with_digits_can_never_match_and_is_rejected()
        {
            var c = Valid(); c.Slabs.Classifications["PT"].NamePrefixes.Add("PT2");
            AssertError(c, "Slabs.Classifications.PT.NamePrefixes");
        }

        [Fact]
        public void Saved_profile_round_trips_through_the_strict_loader()
        {
            ConfigurationLoader.SaveToFile(Valid(), _tmp);
            var loaded = ConfigurationLoader.LoadFromFile(_tmp);
            Assert.DoesNotContain(ConfigurationValidator.Validate(loaded), i => i.Level == ConfigIssueLevel.Error);
            Assert.Equal(Valid().Walls.DefaultModifierValue, loaded.Walls.DefaultModifierValue);
        }

        [Fact]
        public void Exactly_one_catch_all_slab_class_is_required()
        {
            var none = Valid(); none.Slabs.Classifications["Ordinary"].NamePrefixes.Add("S");
            AssertError(none, "Slabs.Classifications");

            var two = Valid(); two.Slabs.Classifications["Raft"].NamePrefixes.Clear();
            AssertError(two, "Slabs.Classifications");
        }

        [Fact]
        public void Blank_exemption_prefix_would_exempt_every_wall()
        {
            var c = Valid(); c.Walls.ExceptionPrefixes.Add(" ");
            AssertError(c, "Walls.ExceptionPrefixes");
        }

        // ---- the gate is enforced by the runner, not just available ------------------------------

        [Fact]
        public void Runner_refuses_to_run_with_an_invalid_profile()
        {
            var c = Valid(); c.CodeProfile = "Eurocode2";
            var ex = Assert.Throws<InvalidConfigurationException>(() =>
                new ValidationRunner().RunAsync("does-not-matter.xlsx", c, null, null, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Contains(ex.Issues, i => i.Path == "CodeProfile");
        }
    }
}
