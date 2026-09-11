using System.Text;

namespace ETABSModelDefinitionValidator.Excel
{
    /// <summary>
    /// Turns an ETABS column header ("Wall Thickness", "M11 Modifier", "Self Weight Multiplier")
    /// into a normalized PascalCase key ("WallThickness", "M11Modifier", "SelfWeightMultiplier")
    /// that readers match against, while the original header text is preserved alongside it on
    /// RawTable for diagnostics/traceability.
    /// </summary>
    public static class HeaderNormalizer
    {
        public static string Normalize(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                return string.Empty;
            }

            var words = header
                .Replace("%", "Pct")
                .Split(new[] { ' ', '-', '_', '/', '(', ')', '?', '.', ',', ':' }, System.StringSplitOptions.RemoveEmptyEntries);

            var sb = new StringBuilder();
            foreach (var word in words)
            {
                if (word.Length == 0) continue;
                // Capitalize the first character only; leave the rest as-is. This must work
                // uniformly for "Wall" -> "Wall", "m22" -> "M22", and "GUID" -> "GUID" alike -
                // a previous version special-cased digits/acronyms and silently lowercased
                // headers like "m22 Modifier", breaking column lookups. Never re-introduce that.
                sb.Append(char.ToUpperInvariant(word[0]));
                if (word.Length > 1) sb.Append(word.Substring(1));
            }

            return sb.ToString();
        }
    }
}
