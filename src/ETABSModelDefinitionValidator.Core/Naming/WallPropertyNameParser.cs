using System.Text.RegularExpressions;

namespace ETABSModelDefinitionValidator.Core.Naming
{
    /// <summary>
    /// Parses ETABS wall property names of the observed form &lt;Prefix&gt;&lt;ThicknessMm&gt;-C&lt;GradeMpa&gt;
    /// (e.g. "W300-C40", "WT350-C56", "BW350-C40"). This convention was verified against the
    /// reference workbook, not assumed - see docs/ValidationMatrix.md. If a project uses a
    /// different convention, this is the one place to change it; no rule needs to change.
    /// </summary>
    public sealed class WallPropertyNameParser
    {
        private static readonly Regex Pattern = new Regex(
            @"^(?<prefix>[A-Za-z]+)[\s\-]*(?<thickness>\d+(?:\.\d+)?)-C(?<grade>\d+(?:\.\d+)?)(?:[/\-].*)?$",
            RegexOptions.Compiled);

        public WallNameParseResult Parse(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return WallNameParseResult.Failed(name, "Name is empty.");
            }

            var match = Pattern.Match(name.Trim());
            if (!match.Success)
            {
                return WallNameParseResult.Failed(name, $"Name does not match the expected <Prefix><Thickness>-C<Grade> convention.");
            }

            return new WallNameParseResult
            {
                Success = true,
                RawName = name,
                Prefix = match.Groups["prefix"].Value,
                ThicknessMm = double.Parse(match.Groups["thickness"].Value),
                GradeFcMpa = double.Parse(match.Groups["grade"].Value)
            };
        }
    }
}
