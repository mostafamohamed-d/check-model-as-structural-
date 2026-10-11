namespace ETABSModelDefinitionValidator.Core
{
    /// <summary>
    /// Canonical ETABS table titles, exactly as they appear after "TABLE:" in the source
    /// workbook (verified against the reference export - see docs/ValidationMatrix.md).
    /// Lives in Core (not Excel) because both the importer AND every rule need it - rules must
    /// never depend on the Excel project, since the same rules will later run against an
    /// ETABS-API-sourced ModelData with no Excel involved at all.
    /// </summary>
    public static class KnownTables
    {
        public const string WallPropertyDefinitionsSpecified = "Wall Property Definitions - Specified";
        public const string SlabPropertyDefinitions = "Slab Property Definitions";
        public const string PierSectionProperties = "Pier Section Properties";
        public const string ModalCaseDefinitionsRitz = "Modal Case Definitions - Ritz";
        public const string MaterialPropertiesRebarData = "Material Properties - Rebar Data";
        public const string MaterialPropertiesConcreteData = "Material Properties - Concrete Data";
        public const string MaterialPropertiesBasicMechanicalProperties = "Material Properties - Basic Mechanical Properties";
        public const string LoadPatternDefinitions = "Load Pattern Definitions";
        public const string LoadCaseDefinitionsLinearStatic = "Load Case Definitions - Linear Static";
        public const string FrameAssignmentsFrameAutoMeshOptions = "Frame Assignments - Frame Auto Mesh Options";
        public const string FrameAssignmentsEndLengthOffsets = "Frame Assignments - End Length Offsets";
        public const string DiaphragmDefinitions = "Diaphragm Definitions";
        public const string ConcreteColumnOverwritesAci31819 = "Concrete Column Overwrites - ACI 318-19";
        public const string AreaAssignmentsPierLabels = "Area Assignments - Pier Labels";
        public const string WallObjectConnectivity = "Wall Object Connectivity";
        public const string PointObjectConnectivity = "Point Object Connectivity";
        public const string FloorObjectConnectivity = "Floor Object Connectivity";
        public const string ColumnObjectConnectivity = "Column Object Connectivity";
        public const string BeamObjectConnectivity = "Beam Object Connectivity";
        public const string LinkObjectConnectivity = "Link Object Connectivity";
        public const string JointAssignmentsRestraints = "Joint Assignments - Restraints";
        public const string AreaLoadAssignmentsUniformLoadSets = "Area Load Assignments - Uniform Load Sets";
        public const string TowerAndBaseStoryDefinitions = "Tower and Base Story Definitions";

        public static readonly string[] RequiredForInitialRuleSet =
        {
            WallPropertyDefinitionsSpecified,
            SlabPropertyDefinitions,
            PierSectionProperties,
            ModalCaseDefinitionsRitz,
            MaterialPropertiesRebarData,
            MaterialPropertiesConcreteData,
            LoadPatternDefinitions,
            LoadCaseDefinitionsLinearStatic,
            FrameAssignmentsFrameAutoMeshOptions,
            FrameAssignmentsEndLengthOffsets,
            DiaphragmDefinitions,
            ConcreteColumnOverwritesAci31819,
            AreaAssignmentsPierLabels,
            WallObjectConnectivity,
            PointObjectConnectivity,
            FloorObjectConnectivity,
            ColumnObjectConnectivity,
            BeamObjectConnectivity,
            LinkObjectConnectivity,
            JointAssignmentsRestraints,
            AreaLoadAssignmentsUniformLoadSets,
            TowerAndBaseStoryDefinitions
        };
    }
}
