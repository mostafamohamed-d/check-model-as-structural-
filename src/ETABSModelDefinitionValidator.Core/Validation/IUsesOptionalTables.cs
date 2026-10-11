using System;
using System.Collections.Generic;
using System.Linq;

namespace ETABSModelDefinitionValidator.Core.Validation
{
    /// <summary>
    /// Optional companion to <see cref="IValidationRule"/>: tables the rule reads when the source has
    /// them, but whose absence is normal (ETABS lists only tables holding data - a model without
    /// braces has no Brace Object Connectivity). Unlike ApplicableTables they are never required by
    /// the run verdict, so their absence cannot make a run INCOMPLETE.
    /// </summary>
    public interface IUsesOptionalTables
    {
        IReadOnlyList<string> OptionalTables { get; }
    }

    public static class RuleTables
    {
        /// <summary>Every table worth requesting from the source for these rules: required + optional.</summary>
        public static List<string> ToRead(IEnumerable<IValidationRule> rules) =>
            rules.SelectMany(r => (r.ApplicableTables ?? Array.Empty<string>())
                    .Concat((r as IUsesOptionalTables)?.OptionalTables ?? Array.Empty<string>()))
                 .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
