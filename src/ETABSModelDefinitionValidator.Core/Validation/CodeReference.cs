namespace ETABSModelDefinitionValidator.Core.Validation
{
    /// <summary>
    /// Citation for a rule whose expected value is dictated by a design code. Per the project
    /// brief: never fabricate a clause number. When a code basis is claimed but not yet verified
    /// against the actual code text, IsVerified must be false and the rule must say so in its
    /// message rather than presenting an invented citation as authoritative.
    /// </summary>
    public sealed class CodeReference
    {
        public string Code { get; set; }
        public string Edition { get; set; }
        public string ChapterOrSection { get; set; }
        public bool IsVerified { get; set; }

        public static CodeReference ReferencePending(string code, string edition) => new CodeReference
        {
            Code = code,
            Edition = edition,
            ChapterOrSection = "REFERENCE PENDING",
            IsVerified = false
        };

        public override string ToString() =>
            IsVerified ? $"{Code} {Edition} §{ChapterOrSection}" : $"{Code} {Edition} (REFERENCE PENDING)";
    }
}
