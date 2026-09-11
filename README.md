# ETABS Model Definition Validator

A professional QA/QC tool for ETABS model definitions. It reads an ETABS Excel database export,
normalizes the relevant tables into a strongly-typed in-memory model, runs a configurable set of
engineering validation rules against it, and produces a PASS/WARNING/FAIL/EXEMPT/NOT_CHECKED
report with full traceability back to the source workbook.

This is a validation engine first, a UI second. See [docs/Architecture.md](docs/Architecture.md)
and [docs/ValidationMatrix.md](docs/ValidationMatrix.md) for how it's built and why.

## What it checks (v1 - 19 rules)

| Area | Rules |
|---|---|
| Walls | thickness/material match the property name; modifier values; WT/BW exceptions |
| Slabs | m11/m22/m12 modifiers per classification (PT, Ordinary, Stair, Ramp, Raft) |
| Piers | top/bottom width and thickness consistency |
| Modal | Ritz target dynamic mass participation ratio |
| Materials | rebar Fy/Fu/Fye/Fue vs. grade profile; concrete Fc vs. name; Ec vs. ACI 318-19 formula |
| Loads | Dead pattern self-weight multiplier; load case/load name consistency |
| Frames | auto mesh enabled; end length offsets are whole numbers |
| Design | concrete column design section = Program Determined |
| Diaphragms | rigidity type |

Every rule is classified as CODE, PROJECT_STANDARD, MODELING_STANDARD, CONSISTENCY,
MODEL_INTEGRITY, or BEST_PRACTICE - not everything is a code requirement, and the report says so.
See [docs/ValidationMatrix.md](docs/ValidationMatrix.md) for the full rule-by-rule matrix,
including two documented discrepancies between the original spec and the verified reference
workbook.

## Solution layout

```
src/
  ETABSModelDefinitionValidator.Core/       domain model, IValidationRule, ValidationEngine, config, naming parsers
  ETABSModelDefinitionValidator.Excel/      ClosedXML-based importer: table detection -> normalized ModelData
  ETABSModelDefinitionValidator.Rules/      the 19 rule implementations + registry
  ETABSModelDefinitionValidator.Reporting/  console summary + CSV export
  ETABSModelDefinitionValidator.UI/         console runner - CI/automation-friendly entry point
  ETABSModelDefinitionValidator.UI.WinForms/ desktop dashboard: file picker, profile checkboxes, progress/cancel, summary + filterable grid, CSV export
tests/
  ETABSModelDefinitionValidator.Tests/      xUnit tests, one class per rule group, against in-memory fixtures
config/
  default-validation-profile.json           every threshold used by the rule set
```

Rules depend only on `Core` (never on `Excel`) - the same rules will run unchanged against a
future ETABS-API-sourced `ModelData` (`IModelDataProvider`), per the Phase 8 roadmap item.

## Build & run

Requires the .NET SDK (targets `net472` - .NET Framework 4.7.2, opens normally in Visual Studio).

```powershell
dotnet build
dotnet test

# console (CI/scripting)
dotnet run --project src\ETABSModelDefinitionValidator.UI -- <path-to-etabs-export.xlsx> [config.json] [output.csv]

# desktop UI
dotnet run --project src\ETABSModelDefinitionValidator.UI.WinForms
```

Console exit code `0` = overall PASS/EXEMPT, `2` = overall FAIL, `1` = the run itself errored (bad
file, config, etc.). A CSV with every result (rule, category, status, object, story, source
location, expected/actual, message) is written alongside the console summary; the WinForms UI
exports the same CSV format via its Export button, for whatever subset the current grid filter
shows.

## Adding a new rule

1. Add a `RuleIds` constant.
2. Implement `IValidationRule` (see any file in `src/.../Rules/`) - operate only on `ModelData` +
   `ValidationProfileConfig`, never on Excel/ClosedXML types.
3. Register it in `RuleRegistry.AllRules`.
4. Add a test class covering PASS, FAIL, a boundary case, an exception case (if applicable), and
   a missing-data (`NOT_CHECKED`) case.
5. Add any new thresholds to `ValidationProfileConfig` and `config/default-validation-profile.json`.
6. Document it in `docs/ValidationMatrix.md`.

No existing rule needs to change.

## Adding a new ETABS table

1. Confirm the exact table title (as it appears after `TABLE:` in the export) and column headers
   against a real workbook - never assume.
2. Add the title as a constant in `Core/KnownTables.cs`.
3. Add a normalized domain model class under `Core/Model/`.
4. Add a `PopulateX` method in `ExcelModelDataProvider`.
5. Update `docs/ValidationMatrix.md`'s table mapping.

## Status

Foundation, Excel import, the 19 initial rules, a console runner, parallel rule execution,
import table-skipping, and a WinForms desktop UI are all in place. See
[docs/Roadmap.md](docs/Roadmap.md) for what's next (broader material validation, model integrity
rules, Excel/PDF report export, and eventual ETABS API live validation).
