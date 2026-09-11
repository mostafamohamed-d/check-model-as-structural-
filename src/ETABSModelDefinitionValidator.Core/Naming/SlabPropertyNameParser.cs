using System;
using System.Linq;
using System.Text.RegularExpressions;
using ETABSModelDefinitionValidator.Core.Configuration;

namespace ETABSModelDefinitionValidator.Core.Naming
{
    /// <summary>
    /// Parses ETABS slab property names and resolves the engineering classification (PT,
    /// Ordinary, Stair, Ramp, Raft, ...) from the SAME parse pass - centralizing classification
    /// here means every slab rule (SLAB-001/002/003) shares one source of truth instead of each
    /// re-implementing name sniffing. Convention verified against the reference workbook:
    /// &lt;Prefix&gt;[space|dash]&lt;ThicknessMm&gt;-C&lt;GradeMpa&gt; - prefixes observed include
    /// "PT", "D", "S", "STAIR", "Ramp", "RAFT " (with a trailing space).
    /// </summary>
    public sealed class SlabPropertyNameParser
    {
        private static readonly Regex Pattern = new Regex(
            @"^(?<prefix>[A-Za-z]+)[\s\-]*(?<thickness>\d+(?:\.\d+)?)-C(?<grade>\d+(?:\.\d+)?)(?:[/\-].*)?$",
            RegexOptions.Compiled);

        public SlabNameParseResult Parse(string name, SlabRulesConfig config)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return SlabNameParseResult.Failed(name, "Name is empty.");
            }

            var match = Pattern.Match(name.Trim());
            if (!match.Success)
            {
                return SlabNameParseResult.Failed(name, "Name does not match the expected <Prefix><Thickness>-C<Grade> convention.");
            }

            var prefix = match.Groups["prefix"].Value;

            var classification = config.Classifications
                .FirstOrDefault(kv => kv.Value.NamePrefixes.Any(p => string.Equals(p, prefix, StringComparison.OrdinalIgnoreCase)))
                .Key;

            if (classification == null)
            {
                classification = config.Classifications
                    .FirstOrDefault(kv => kv.Value.NamePrefixes.Count == 0).Key ?? "Ordinary";
            }

            return new SlabNameParseResult
            {
                Success = true,
                RawName = name,
                Prefix = prefix,
                ThicknessMm = double.Parse(match.Groups["thickness"].Value),
                GradeFcMpa = double.Parse(match.Groups["grade"].Value),
                Classification = classification
            };
        }
    }
}
