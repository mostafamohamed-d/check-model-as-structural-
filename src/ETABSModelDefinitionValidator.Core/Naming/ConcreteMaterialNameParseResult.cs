namespace ETABSModelDefinitionValidator.Core.Naming
{
    public sealed class ConcreteMaterialNameParseResult
    {
        public bool Success { get; set; }
        public double FcMpa { get; set; }
        public bool IsTimeDependentVariant { get; set; }
        public string RawName { get; set; }
        public string FailureReason { get; set; }

        public static ConcreteMaterialNameParseResult Failed(string rawName, string reason) => new ConcreteMaterialNameParseResult
        {
            Success = false,
            RawName = rawName,
            FailureReason = reason
        };
    }
}
