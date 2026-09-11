# Architecture

```
ETABS Excel export
        |
        v
ExcelWorkbookReader (ClosedXML)
        |
        v
TableDetector            -- finds tables by "TABLE:  <Title>" text, never by sheet order/name
        |
        v
ExcelModelDataProvider    -- implements Core.Providers.IModelDataProvider
        |
        v
ModelData                 -- the ONLY thing rules ever see (Core.Model)
        |
        v
RuleRegistry.AllRules -> ValidationEngine.Run(model, config, rules)
        |
        v
IReadOnlyList<ValidationResult>
        |
   +----+----+
   v         v
ConsoleReportWriter   CsvReportWriter
```

## The one hard boundary

`ETABSModelDefinitionValidator.Rules` references `ETABSModelDefinitionValidator.Core` and
nothing else. It has no reference to `Excel`, ClosedXML, or any file-format library. Every rule
implements `IValidationRule.Validate(ModelData model, ValidationProfileConfig config)` and reads
only those two objects.

This is what makes Phase 8 (a future `EtabsApiModelDataProvider` reading a live ETABS model
instead of Excel) possible without touching a single rule: swap the `IModelDataProvider`
implementation, run the same `RuleRegistry.AllRules` against the `ModelData` it produces.

## Traceability

Every normalized object (`WallProperty`, `SlabProperty`, ...) carries a `SourceLocation`
(workbook, worksheet, table, row) set once by the importer. Every `ValidationResult` carries the
`SourceLocation` of the object it evaluated. Nothing downstream of the importer ever needs to
know about Excel cell addresses - see `ModelObjectBase` and `ValidationResult.Source`.

## Table detection, not sheet position

`TableDetector` reads row 1 of every worksheet looking for a `TABLE:` prefix, extracts the title
text after it, reads row 2 as headers (normalized via `HeaderNormalizer` into PascalCase keys),
skips a units row if the next row's first cell is blank, and reads everything after as data.
`KnownTables` (in `Core`, not `Excel` - rules need to reference table names too, e.g. for
"table missing" checks) holds the canonical title strings, verified against a real reference
export rather than assumed. A table detected but not in `KnownTables` is logged to
`ModelData.UnrecognizedTables`, never silently dropped; a `KnownTables` entry not found in the
workbook is logged to `ModelData.MissingTables`, and every rule that depends on it returns
`NOT_CHECKED` with an explicit message rather than skipping silently or reporting PASS.

## Configuration

`ValidationProfileConfig` (in `Core.Configuration`) holds every threshold - wall modifier value,
slab classification profiles, modal ratio minimum, rebar grade profiles, etc. - deserialized from
`config/default-validation-profile.json` via `ConfigurationLoader`. Rules read thresholds from
this object; nothing is hard-coded in rule bodies. See `ConfigurationLoader`'s
`ObjectCreationHandling.Replace` setting - required because Newtonsoft.Json's default behavior
appends to (rather than replaces) list-typed properties that already carry a default value from
their property initializer, which silently duplicated the wall modifier checklist during initial
testing against the reference workbook.

## Naming/classification parsers

`Core.Naming` (`WallPropertyNameParser`, `SlabPropertyNameParser`, `ConcreteMaterialNameParser`)
is the single place ETABS naming conventions are parsed. Slab classification (PT / Ordinary /
Stair / Ramp / Raft) is resolved here once and shared by every slab rule, rather than each rule
re-implementing name sniffing. An unparseable name produces a `WARNING`, never a fabricated value.
