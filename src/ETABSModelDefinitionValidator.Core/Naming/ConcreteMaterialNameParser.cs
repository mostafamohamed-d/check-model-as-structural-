using System.Text.RegularExpressions;

namespace ETABSModelDefinitionValidator.Core.Naming
{
    /// <summary>
    /// Parses ETABS concrete material names of the observed form C&lt;Fc&gt;/&lt;Fc2&gt;[-TD]
    /// (e.g. "C40/50", "C56/70", "C40/50-TD" for the Eurocode-2 time-dependent variant). Fc is
    /// the first number; the second (cube strength or similar) is not currently used by any
    /// rule but is captured for future MAT-CONC-002 expansion.
    /// </summary>
    public sealed class ConcreteMaterialNameParser
    {
        private static readonly Regex Pattern = new Regex(
            @"^C(?<fc>\d+(?:\.\d+)?)/(?<fc2>\d+(?:\.\d+)?)(?<td>-TD)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public ConcreteMaterialNameParseResult Parse(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return ConcreteMaterialNameParseResult.Failed(name, "Name is empty.");
            }

            var match = Pattern.Match(name.Trim());
            if (!match.Success)
            {
                return ConcreteMaterialNameParseResult.Failed(name, "Name does not match the expected C<Fc>/<Fc2>[-TD] convention.");
            }

            return new ConcreteMaterialNameParseResult
            {
                Success = true,
                RawName = name,
                FcMpa = double.Parse(match.Groups["fc"].Value),
                IsTimeDependentVariant = match.Groups["td"].Success
            };
        }
    }
}
