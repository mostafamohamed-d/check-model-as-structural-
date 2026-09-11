namespace ETABSModelDefinitionValidator.Core.Naming
{
    public sealed class SlabNameParseResult
    {
        public bool Success { get; set; }
        public string Prefix { get; set; }
        public double ThicknessMm { get; set; }
        public double GradeFcMpa { get; set; }
        public string RawName { get; set; }
        public string FailureReason { get; set; }

        /// <summary>Resolved classification key (matches ValidationProfileConfig.Slabs.Classifications), e.g. "PT", "Ordinary".</summary>
        public string Classification { get; set; }

        public static SlabNameParseResult Failed(string rawName, string reason) => new SlabNameParseResult
        {
            Success = false,
            RawName = rawName,
            FailureReason = reason
        };
    }
}
