# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A QA/QC tool for ETABS models: reads an ETABS Excel database export, normalizes tables into an in-memory `ModelData`, runs configurable validation rules, and reports PASS/WARNING/FAIL/EXEMPT/NOT_CHECKED with traceability to the source workbook row. See `README.md`, `docs/Architecture.md`, `docs/ValidationMatrix.md` (rule-by-rule matrix, incl. documented spec-vs-reference-workbook discrepancies) and `docs/Roadmap.md`.

## Commands

All projects target `net472` (.NET Framework 4.7.2, set in `Directory.Build.props`); solution is `ETABSModelDefinitionValidator.slnx`.

```powershell
dotnet build
dotnet test
dotnet test --filter "FullyQualifiedName~WallRuleTests"          # one test class
dotnet test --filter "FullyQualifiedName~WallRuleTests.SomeTest"  # one test

# console runner (CI/scripting); exit 0 = PASS/EXEMPT, 2 = FAIL, 1 = run error
dotnet run --project src\ETABSModelDefinitionValidator.UI -- <export.xlsx> [config.json] [output.csv]
# live ETABS diagnostics (attach to a running ETABS 21/22; optional pid)
dotnet run --project src\ETABSModelDefinitionValidator.UI -- --etabs-probe [pid]      # read-only: per-table return codes, fields, first record
dotnet run --project src\ETABSModelDefinitionValidator.UI -- --etabs-unitcheck [pid]  # production read path; switches to N-mm-C and verifies restore
# desktop UI
dotnet run --project src\ETABSModelDefinitionValidator.UI.WinForms
```

## Architecture

Pipeline: Excel export → `Excel` (`TableDetector` → `ExcelModelDataProvider : IModelDataProvider`) → `Core.Model.ModelData` → `Rules` (`RuleRegistry.AllRules` run by `Core.Validation.ValidationEngine`, in parallel) → `ValidationResult`s → `Reporting` (console/CSV) or the WinForms grid.

Second source, same pipeline: `EtabsApi` reads tables from an already-running ETABS 21/22 (`EtabsSession` loads `ETABSv1.dll` by reflection from the running process's folder — no compile-time reference; `ApiTableConverter` turns `GetTableForDisplayArray` output into the same `RawTable`; `ExcelModelDataProvider.FromTables` maps it to `ModelData`). The WinForms UI is ETABS-only (no Excel picker): pick a running instance, Connect, then Run Validation calls `ValidationRunner.RunLiveAsync` with that process id. Excel import remains for the console runner and tests. The session switches the model to N-mm-C while reading and restores the user's units.

Key constraints:
- **`Rules` references only `Core`** — never `Excel`/ClosedXML. Rules see just `ModelData` + `ValidationProfileConfig`, so a future live-ETABS-API provider can reuse them unchanged.
- **Tables are found by the `TABLE:  <Title>` text in row 1, never by sheet name/order.** Canonical titles live in `Core/KnownTables.cs` and must be verified against a real export. Unknown tables go to `ModelData.UnrecognizedTables`; absent known tables go to `MissingTables`, and dependent rules must return `NOT_CHECKED` with a message (never silent skip or PASS). The importer skips tables no enabled rule needs.
- **Thresholds are never hard-coded in rules**: they live in `ValidationProfileConfig` ↔ `config/default-validation-profile.json`. `ConfigurationLoader` uses `ObjectCreationHandling.Replace` deliberately (Newtonsoft otherwise appends to list defaults, duplicating entries).
- **Name parsing is centralized** in `Core/Naming` (wall/slab/concrete-material parsers); slab classification (PT/Ordinary/Stair/Ramp/Raft) is resolved once there. Unparseable names → WARNING, not guessed values.
- Every model object carries a `SourceLocation` (set once by the importer) which flows into `ValidationResult.Source`.

## Adding things

- **New rule**: add a `RuleIds` constant, implement `IValidationRule` in `src/...Rules/`, register in `RuleRegistry.AllRules`, add tests (PASS, FAIL, boundary, exception, missing-data → NOT_CHECKED), add thresholds to both `ValidationProfileConfig` and the default JSON profile, document in `docs/ValidationMatrix.md`. Classify it (CODE, PROJECT_STANDARD, MODELING_STANDARD, CONSISTENCY, MODEL_INTEGRITY, BEST_PRACTICE).
- **New ETABS table**: confirm exact title/headers in a real workbook, add to `KnownTables`, add a model class under `Core/Model/`, add a `PopulateX` method in `ExcelModelDataProvider`, update the ValidationMatrix table mapping.

Tests (`tests/ETABSModelDefinitionValidator.Tests`, xUnit) use in-memory `ModelData` fixtures (`TestHelpers.cs`), one class per rule group.
