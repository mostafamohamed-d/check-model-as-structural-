using System.Collections.Generic;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.Rules
{
    /// <summary>
    /// Central, explicit list of every registered rule. New rules are added here and nowhere
    /// else has to change - adding a rule must not require touching existing rules (§40).
    /// </summary>
    public static class RuleRegistry
    {
        public static IReadOnlyList<IValidationRule> AllRules => new List<IValidationRule>
        {
            new WallThicknessRule(),
            new WallMaterialRule(),
            new WallModifierRule(),
            new SlabModifierRule(),
            new PierDimensionRule(),
            new ModalTargetRatioRule(),
            new RebarMaterialRule(),
            new ConcreteFcRule(),
            new ConcretePropertyRule(),
            new DeadSelfWeightRule(),
            new LoadCaseConsistencyRule(),
            new FrameAutoMeshRule(),
            new FrameEndOffsetRule(),
            new ColumnDesignSectionRule(),
            new DiaphragmRigidityRule()
        };
    }
}
