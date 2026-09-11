namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>
    /// One row from "Modal Case Definitions - Ritz". ETABS repeats the case Name across
    /// multiple rows (one per Load Name / direction); each row is imported separately and
    /// carries its own Target Ratio, since that is what MODAL-001 evaluates.
    /// </summary>
    public sealed class ModalRitzCase : ModelObjectBase
    {
        public string Name { get; set; }
        public string LoadType { get; set; }
        public string LoadName { get; set; }
        public double? TargetRatioPercent { get; set; }
    }
}
