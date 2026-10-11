using System.Collections.Generic;
using System.Linq;
using ETABSModelDefinitionValidator.Core;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Rules;
using Xunit;

namespace ETABSModelDefinitionValidator.Tests
{
    public class SupportAndLoadAssignmentRuleTests
    {
        private readonly ValidationProfileConfig _config = ConfigurationLoader.LoadDefault();

        private List<ValidationResult> Run(IValidationRule rule, ModelData model) => rule.Validate(model, _config).ToList();

        private static Geo Restrained(params (string Name, double Z)[] joints)
        {
            var g = new Geo().Point("low", 0, 0, -3000);
            foreach (var (name, z) in joints)
            {
                g.Point(name, 0, 0, z);
                g.Model.JointRestraints.Add(new JointRestraint { UniqueName = name, Label = name, Story = "S", Fixed = new[] { true, true, true, false, false, false } });
            }
            return g;
        }

        // --- SUP-001 joint restraints --------------------------------------------------------

        [Fact]
        public void Restraints_Pass_OnLowestFloor()
        {
            var r = Run(new JointRestraintRule(), Restrained(("1", -3000)).Model).Single();
            Assert.Equal(ValidationStatus.Pass, r.Status);
        }

        [Fact]
        public void Restraints_Fail_AboveLowestFloor()
        {
            var r = Run(new JointRestraintRule(), Restrained(("1", 3500)).Model).Single();
            Assert.Equal(ValidationStatus.Fail, r.Status);
            Assert.Contains("UX+UY+UZ", r.Message);
            Assert.Equal(new[] { "1" }, r.Objects.Points);
        }

        [Fact]
        public void Restraints_Boundary_ToleranceApplies()
        {
            var g = Restrained(("1", -2999));
            Assert.Equal(ValidationStatus.Fail, Run(new JointRestraintRule(), g.Model).Single().Status);
            _config.Geometry.CoordinateToleranceMm = 1;
            Assert.Equal(ValidationStatus.Pass, Run(new JointRestraintRule(), g.Model).Single().Status);
        }

        [Fact]
        public void Restraints_FoundationModel_FailsEvenOnLowestFloor()
        {
            var g = Restrained(("1", -3000));
            g.Model.ModelFilePath = @"C:\Projects\Tower-FND-Rev3.EDB";
            var r = Run(new JointRestraintRule(), g.Model).Single();
            Assert.Equal(ValidationStatus.Fail, r.Status);
            Assert.Contains("foundation model", r.Message);
        }

        [Fact]
        public void Restraints_NotChecked_WhenTableMissing()
        {
            var g = new Geo().Point("1", 0, 0, 0);
            g.Model.MissingTables.Add(KnownTables.JointAssignmentsRestraints);
            Assert.Equal(ValidationStatus.NotChecked, Run(new JointRestraintRule(), g.Model).Single().Status);
        }

        // --- LOAD-003 line load direction ----------------------------------------------------

        [Fact]
        public void LineLoads_PassGravity_FailGlobalZ()
        {
            var model = new ModelData
            {
                FrameDistributedLoads = new List<FrameDistributedLoad>
                {
                    new FrameDistributedLoad { FrameName = "B1", LoadPattern = "SDL", LoadType = 1, Direction = LoadDirection.Gravity, Value1 = 5, Value2 = 5 },
                    new FrameDistributedLoad { FrameName = "B2", LoadPattern = "SDL", LoadType = 1, Direction = 6, Value1 = -5, Value2 = -5 }
                }
            };
            var results = Run(new LineLoadDirectionRule(), model);
            Assert.Equal(ValidationStatus.Pass, results[0].Status);
            Assert.Equal(ValidationStatus.Fail, results[1].Status);
            Assert.Contains("Global Z", results[1].Message);
            Assert.Equal(new[] { "B2" }, results[1].Objects.Frames);
        }

        /// <summary>Live case (BHAD-EB-V22.3-REV75, beam B107): SIDL = 2.4 kN/m Gravity force plus a 3 kN·m/m
        /// moment about axis 1 (Dir 1, CSys Global). The force passes; the moment fails as a moment, not as
        /// a "wrong direction" force, and even a moment reported in the Gravity direction fails.</summary>
        [Fact]
        public void LineLoads_DistributedMoments_AlwaysFail_WithAMomentSpecificMessage()
        {
            var model = new ModelData
            {
                FrameDistributedLoads = new List<FrameDistributedLoad>
                {
                    new FrameDistributedLoad { FrameName = "948", LoadPattern = "SIDL", LoadType = 1, CoordinateSystem = "Global", Direction = 10, Value1 = 2.4, Value2 = 2.4 },
                    new FrameDistributedLoad { FrameName = "948", LoadPattern = "SIDL", LoadType = 2, CoordinateSystem = "Global", Direction = 1, Value1 = 3000, Value2 = 3000 },
                    new FrameDistributedLoad { FrameName = "948", LoadPattern = "SIDL", LoadType = 2, CoordinateSystem = "Global", Direction = 10, Value1 = 3000, Value2 = 3000 }
                }
            };

            var results = Run(new LineLoadDirectionRule(), model);

            Assert.Equal(ValidationStatus.Pass, results[0].Status);
            Assert.All(results.Skip(1), r =>
            {
                Assert.Equal(ValidationStatus.Fail, r.Status);
                Assert.Contains("MOMENT", r.Message);
                Assert.DoesNotContain("must be Gravity", r.Message);
                Assert.Equal("No distributed moment load", r.ExpectedValue);
            });
        }

        [Fact]
        public void LineLoads_NotChecked_WhenLoadsNotRead()
        {
            Assert.Equal(ValidationStatus.NotChecked, Run(new LineLoadDirectionRule(), new ModelData()).Single().Status);
        }

        [Fact]
        public void LoadRules_DeclareTheyNeedLiveLoads()
        {
            Assert.Equal(LiveData.LoadAssignments, new LineLoadDirectionRule().RequiredLiveData);
            Assert.Equal(LiveData.LoadAssignments, new WallLoadRule().RequiredLiveData);
        }

        // --- LOAD-004 wall loads -------------------------------------------------------------

        private static ModelData Walls()
        {
            var g = new Geo().RectWall("W1", "L1", 0, 1000, 0, 0, 3500).RectWall("W2", "L1", 2000, 3000, 0, 0, 3500).RectWall("W3", "L1", 4000, 5000, 0, 0, 3500);
            g.Model.AreaUniformLoads = new List<AreaUniformLoad>
            {
                new AreaUniformLoad { AreaName = "W1", LoadPattern = "SDL", Direction = LoadDirection.Gravity, Value = 0.002 },
                new AreaUniformLoad { AreaName = "W2", LoadPattern = "SOIL", Direction = 4, Value = 0.01 } // lateral: legitimate
            };
            g.Model.AreaLoadSets.Add(new AreaLoadSetAssignment { UniqueName = "W3", LoadSet = "T16" });
            return g.Model;
        }

        [Fact]
        public void WallLoads_FailGravityAndLoadSet_PassLateral()
        {
            var results = Run(new WallLoadRule(), Walls());
            Assert.Equal(ValidationStatus.Fail, results.Single(r => r.ObjectName == "WW1").Status);
            Assert.Equal(ValidationStatus.Pass, results.Single(r => r.ObjectName == "WW2").Status);
            var w3 = results.Single(r => r.ObjectName == "WW3");
            Assert.Equal(ValidationStatus.Fail, w3.Status);
            Assert.Contains("T16", w3.Message);
        }

        [Fact]
        public void WallLoads_LoadSetTableMissing_StillChecksLoads_AndSaysSo()
        {
            var model = Walls();
            model.MissingTables.Add(KnownTables.AreaLoadAssignmentsUniformLoadSets);
            var results = Run(new WallLoadRule(), model);
            Assert.Contains(results, r => r.Status == ValidationStatus.NotChecked && r.ObjectName == KnownTables.AreaLoadAssignmentsUniformLoadSets);
            Assert.Equal(ValidationStatus.Fail, results.Single(r => r.ObjectName == "WW1").Status);
        }

        [Fact]
        public void WallLoads_NotChecked_WhenNeitherLoadsNorLoadSetsAvailable()
        {
            var model = new Geo().RectWall("W1", "L1", 0, 1000, 0, 0, 3500).Model;
            model.MissingTables.Add(KnownTables.AreaLoadAssignmentsUniformLoadSets);
            Assert.Equal(ValidationStatus.NotChecked, Run(new WallLoadRule(), model).Single().Status);
        }
    }
}
