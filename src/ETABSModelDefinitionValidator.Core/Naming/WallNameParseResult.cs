namespace ETABSModelDefinitionValidator.Core.Naming
{
    public sealed class WallNameParseResult
    {
        public bool Success { get; set; }
        public string Prefix { get; set; }
        public double ThicknessMm { get; set; }
        public double GradeFcMpa { get; set; }
        public string RawName { get; set; }
        public string FailureReason { get; set; }

        public static WallNameParseResult Failed(string rawName, string reason) => new WallNameParseResult
        {
            Success = false,
            RawName = rawName,
            FailureReason = reason
        };
    }
}
