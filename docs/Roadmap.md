# Roadmap

v1 implemented Phases 1-3 of the master plan plus a minimal console runner: foundation, Excel
import, the 19 initial rules, a rule engine, CSV export. v1.1 added parallel rule execution,
table-skipping (Phase 6.5) and a WinForms desktop UI (Phase 7, done ahead of Phases 4-6 - the
user asked for it directly). Everything else below is intentionally not yet built - listed here
instead of half-implemented, per the "no half-finished implementations" principle.

## Phase 4 - Broader material validation
- Full concrete material property set (creep, shrinkage, stress-strain curve shape, nonlinear
  properties) - currently only Fc-vs-name and Ec-vs-ACI-formula are checked; everything else is
  intentionally `NOT_CHECKED` / `REFERENCE_PENDING` rather than guessed.
- PT/tendon material validation (strength, yield/ultimate, modulus, stress-strain) - no PT
  material table was present in the reference workbook, so the normalized model has no PT
  material type yet.
- Steel material validation, when a project model includes structural steel.

## Phase 5 - Model integrity rules
Duplicate property names/object IDs, missing referenced materials/properties, load case
referencing a nonexistent load pattern, load pattern unused by any load case, diaphragm
assignment referencing a nonexistent diaphragm, frame referencing a nonexistent section property,
zero-length frames, invalid/zero geometry. These need additional normalized tables not yet
imported (Frame Section Definitions, Area/Frame assignments beyond what v1 reads) - see
`docs/ValidationMatrix.md` for what's already detected-but-unmapped in the reference workbook
(18 additional table types were detected and logged as unrecognized in the smoke test).

## Phase 6 - Reporting
- Excel report export (styled workbook, not just CSV).
- PDF report export.
- Validation profiles (Materials Only / Loads Only / Model Integrity / ... subset selection) -
  done for the WinForms UI (category checkboxes drive both `ValidationEngine`'s
  `enabledCategories` and the Excel importer's table-skipping); a saved/named-profile picker
  (rather than checkboxes chosen fresh each run) is still open.

## Phase 6.5 - Performance
- `ValidationEngine.RunParallel`: independent rules (pure read-only functions over `ModelData`)
  execute via `Parallel.For` instead of a sequential `foreach`, with the same per-rule exception
  isolation as `Run` and results merged back in original rule order so output stays
  reproducible/diffable across runs. `Run` itself is unchanged and still used where determinism
  of a simple sequential trace matters more than wall-clock time.
- Table-skipping: `ExcelModelDataProvider`/`TableDetector` accept an optional required-table set
  (derived from the `ApplicableTables` of whichever rules are actually enabled) and skip the
  expensive per-row scan entirely for every other detected table - the dominant import cost on
  large sheets (thousands of rows), not just the cheaper post-scan population step. Skipped
  tables are tracked on `ModelData.SkippedTables`, distinct from genuinely-missing ones.
- Kept ClosedXML rather than switching to a streaming Excel reader - it already works, and a
  library swap would risk the well-tested `TableDetector`/header-normalization logic for a
  marginal win given table-skipping already addresses the actual scaling cost.

## Phase 7 - UI (done)
A WinForms desktop dashboard (`ETABSModelDefinitionValidator.UI.WinForms`): file picker,
category/profile selection, non-blocking run with progress and cancellation (`ValidationRunner`,
off the UI thread via `Task.Run` - rules stay synchronous, only the whole pipeline is
backgrounded), a summary dashboard (`SummaryPanel`, a thin view over `ValidationSummary`), a
filterable/sortable results grid handling 20,000+ rows via `DataGridView` virtual mode
(`ResultsGridView` - avoids allocating a real row/cell object per result, the actual scaling
cliff of a normally-bound grid), a detail pane per result (`ResultDetailPanel`), and CSV export
reusing `CsvReportWriter` directly. The console runner (`ETABSModelDefinitionValidator.UI`)
stays as the CI/automation-friendly entry point per Phase 8 below - nothing here replaces it.

Not yet done: Excel/PDF export from the WinForms UI (CSV only so far), a saved-profile picker,
live re-highlighting of a selected result's source cell in an embedded Excel view.

## Phase 8 - ETABS API live validation
Implement `IModelDataProvider` against the ETABS API (`EtabsApiModelDataProvider`) so the exact
same `RuleRegistry` rules run against a live open model instead of an Excel export, with
optional object selection/highlighting in ETABS for a failing result. No rule code should need
to change - this is the entire reason rules only ever see `ModelData`.

## Explicitly out of scope until requested
Automatic model correction. This is a validation/QA-QC tool; per the master prompt, any future
"fix it for me" capability requires an explicit preview/confirm/audit-log flow and is a separate
feature, not an extension of validation.
