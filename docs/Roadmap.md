# Roadmap

v1 (this codebase) implements Phases 1-3 of the master plan plus a minimal console runner:
foundation, Excel import, the 19 initial rules, a rule engine, CSV export. Everything below is
intentionally not yet built - listed here instead of half-implemented, per the "no half-finished
implementations" principle.

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
  `ValidationEngine.Run` already accepts an `enabledCategories` filter; a profile picker UI is
  what's missing.

## Phase 7 - UI
A WPF desktop dashboard: file picker, profile/code-profile selection, progress, summary
dashboard, filterable results grid (status/category/rule/severity/story/object/table), detail
pane per result. The current `ETABSModelDefinitionValidator.UI` console runner exists only to
exercise the engine end-to-end - it is deliberately not this.

## Phase 8 - ETABS API live validation
Implement `IModelDataProvider` against the ETABS API (`EtabsApiModelDataProvider`) so the exact
same `RuleRegistry` rules run against a live open model instead of an Excel export, with
optional object selection/highlighting in ETABS for a failing result. No rule code should need
to change - this is the entire reason rules only ever see `ModelData`.

## Explicitly out of scope until requested
Automatic model correction. This is a validation/QA-QC tool; per the master prompt, any future
"fix it for me" capability requires an explicit preview/confirm/audit-log flow and is a separate
feature, not an extension of validation.
