# Validation Matrix — v1

Source of truth for the 19 initial rules. Every entry here was verified against the reference
workbook `DCG-P01-B01-ULT-v22.3-Rev33-MNA.xlsx` (32 sheets) before implementation began, per the
"do not guess the schema" requirement. Column/table names below are the **literal ETABS export
names** found in that workbook — not assumptions from the original spec.

Two discrepancies were found between the original prompt and the verified data; both are called
out inline and resolved with an explicit, configurable default (see `config/default-validation-profile.json`).
If they don't match your actual project standard, they are one JSON edit away from being corrected.

## Table → Sheet name mapping (as found in the reference workbook)

| Semantic table name | Sheet name found in workbook |
|---|---|
| Wall Property Definitions - Specified | `Wall Property Def - Specified` |
| Slab Property Definitions | `Slab Property Definitions` |
| Pier Section Properties | `Pier Section Properties` |
| Modal Case Definitions - Ritz | `Modal Cases - Ritz` |
| Material Properties - Rebar Data | `Mat Prop - Rebar Data` |
| Material Properties - Concrete Data | `Mat Prop - Concrete Data` |
| Material Properties - Basic Mechanical Properties | `Mat Prop - Basic Mech Props` |
| Load Pattern Definitions | `Load Pattern Definitions` |
| Load Case Definitions - Linear Static | `Load Cases - Linear Static` |
| Frame Assignments - Frame Auto Mesh Options | `Frame Assigns - Frame Auto Mesh` |
| Frame Assignments - End Length Offsets | `Frame Assigns - End Len Offsets` |
| Diaphragm Definitions | `Diaphragm Definitions` |
| Concrete Column Overwrites - ACI 318-19 | `Conc Col Over ACI 318-19` |

Tables of the geometry / support / load-assignment checks (merged from the pier-validation tool).
Titles and headers verified live against ETABS 22.3 with `--etabs-probe` (no reference workbook yet):

| Semantic table name | Headers used |
|---|---|
| Point Object Connectivity | `UniqueName`, `Story`, `PointBay`, `X`, `Y`, `Z` |
| Wall Object Connectivity / Floor Object Connectivity | `UniqueName`, `Story`, `WallBay`/`FloorBay`, `UniquePt1..4` |
| Column Object Connectivity / Beam Object Connectivity | `Unique Name`, `Story`, `ColumnBay`/`BeamBay`, `UniquePtI`, `UniquePtJ` |
| Link Object Connectivity | `Name`, `Story`, `UniquePt1`, `UniquePt2` |
| Area Assignments - Pier Labels | `Story`, `Label`, `UniqueName`, `Pier Name` |
| Joint Assignments - Restraints | `Story`, `Label`, `UniqueName`, `UX`..`RZ` (Yes/No) |
| Area Load Assignments - Uniform Load Sets | `Story`, `Label`, `UniqueName`, `Load Set` |
| Tower and Base Story Definitions | `BSElev` (base elevation; lowest tower wins) |

ETABS returns error code 1 (no table) when one of these is empty — e.g. no restraints, no links,
no load sets. `Frame Loads Assignments - Distributed` and `Area Load Assignments - Uniform` fail
the same way even when loads exist (they are filtered by the load patterns selected for display),
so a live run reads those loads through the object API instead (`FrameObj.GetLoadDistributed` /
`AreaObj.GetLoadUniform` on group `All`), and replaces table coordinates with exact ones from
`PointObj.GetAllPoints` (tables round to display precision, 0.1 mm in a metre model — enough to
hide the 0.03–0.05 mm column offsets found in a real model).

Every sheet has the ETABS 3-row header pattern:
- Row 1: `TABLE:  <Table Title>` (title, spans columns, rest of row is `None`)
- Row 2: column headers (the identifiers the importer matches against)
- Row 3: units row (e.g. `mm`, `MPa`, `%`) — informational, not a data row
- Row 4+: data rows

The importer must detect the table by the Row 1 title (normalized, case/whitespace-insensitive),
not by sheet order or sheet name, since ETABS export ordering and sheet naming can vary between
projects (Section 6 of the master prompt).

## Rule matrix

| Rule ID | Table | Column(s) | Condition | Expected | Exceptions | Type | Severity |
|---|---|---|---|---|---|---|---|
| WALL-001 | Wall Property Def - Specified | `Name`, `Wall Thickness` | thickness parsed from name == defined thickness | name-derived value | unparsable name → WARNING | CONSISTENCY | FAIL |
| WALL-002 | Wall Property Def - Specified → Mat Prop - Concrete Data | `Name`, `Material` | grade parsed from name == material's Fc-derived grade | match | unparsable name → WARNING | CONSISTENCY | FAIL |
| WALL-003 | Wall Property Def - Specified | `f11/f22/f12/m11/m22/m12 Modifier` | all six == configured value | 0.70 (all six) | WT*, BW* name prefix → EXEMPT | PROJECT_STANDARD | FAIL |
| WALL-004 | (same table, exception logic for WALL-003) | `Name` | name starts with `WT` or `BW` | n/a | — | PROJECT_STANDARD | EXEMPT |
| SLAB-001 | Slab Property Definitions | `Name`, `m11/m22/m12 Modifier` | classified PT → all three == 0.35 | 0.35 | — | PROJECT_STANDARD | FAIL |
| SLAB-002 | Slab Property Definitions | `Name`, `m11/m22/m12 Modifier` | classified Ordinary → all three == 0.25 | 0.25 | — | PROJECT_STANDARD | FAIL |
| SLAB-003 | Slab Property Definitions | `Name`, `m11/m22/m12 Modifier` | classified Stair/Ramp → all three == 0.01 | 0.01 | — | PROJECT_STANDARD | FAIL |
| SLAB-004 | Slab Property Definitions | `Name`, `m11/m22/m12 Modifier` | classified Raft → all three == 1.0 | 1.0 | — | MODELING_STANDARD (pending confirmation, discrepancy #2) | FAIL |
| PIER-001 | Pier Section Properties | `Width Bottom`, `Width Top` | equal | equal | — | MODEL_INTEGRITY | FAIL |
| PIER-002 | Pier Section Properties | `Thickness Bottom`, `Thickness Top` | equal | equal | — | MODEL_INTEGRITY | FAIL |
| MODAL-001 | Modal Cases - Ritz | `Target Ratio` | >= configured threshold | 95.0 | — | PROJECT_STANDARD | FAIL |
| MAT-REBAR-001 | Mat Prop - Rebar Data | `Fy/Fu/Fye/Fue` | matches configured grade profile | grade-dependent | — | PROJECT_STANDARD (REFERENCE_MODEL sourced) | FAIL |
| MAT-CONC-001 | Mat Prop - Concrete Data | `Material`, `Fc` | grade parsed from name == Fc | match | unparsable name → WARNING | CONSISTENCY | FAIL |
| MAT-CONC-002 | Mat Prop - Concrete Data + Basic Mech Props | `Fc`, `E1`, `Unit Mass` (or `Unit Weight`) | E1 within 5% of EITHER ACI 318-19 19.2.2.1(a) `wc^1.5*0.043*sqrt(fc)` (wc 1440-2560 kg/m3, from the material density) OR (b) `4700*sqrt(fc)` (normalweight only) | either equation | lightweight: (a) only; no usable density and lightweight → NOT_CHECKED; non-ACI profile → NOT_CHECKED | CODE | WARNING |
| LOAD-001 | Load Pattern Definitions | `Type`, `Self Weight Multiplier` | Type == Dead (case-insensitive) → multiplier == 1.0 | 1.0 | non-Dead patterns not checked | CODE (self-weight physics) | FAIL |
| LOAD-002 | Load Cases - Linear Static | `Name`, `Load Name` | Load Name equals Name exactly | equal | — | CONSISTENCY | WARNING |
| FRAME-001 | Frame Assigns - Frame Auto Mesh | `Auto Mesh` | == Yes | Yes | — | MODELING_STANDARD | FAIL |
| FRAME-002 | Frame Assigns - End Len Offsets (+ Conc Col Over ACI 318-19 to identify columns) | `Offset I`, `Offset J` | integral within tolerance, columns only | whole number | beams (non-columns) | MODELING_STANDARD | FAIL |
| COL-001 | Conc Col Over ACI 318-19 | `Design Section` | == Program Determined | Program Determined | — | PROJECT_STANDARD | FAIL |
| DIA-001 | Diaphragm Definitions | `Rigidity Type` | == Semi-Rigid | Semi-Rigid | — | MODELING_STANDARD | FAIL |
| GEO-001 | Wall Object Connectivity + Point Object Connectivity | `UniquePt1..4`, `X/Y/Z` | 4 corners, 2 Z levels (2/2), top edge over bottom edge in plan | vertical quad | — (tolerance `Geometry.CoordinateToleranceMm`, 0) | MODELING_STANDARD | FAIL |
| GEO-002 | Column Object Connectivity + Point Object Connectivity | `UniquePtI/J`, `X/Y` | X and Y of both joints equal, no tolerance | dX = dY = 0 | joint missing → WARNING | MODEL_INTEGRITY | FAIL |
| GEO-003 | Column/Beam/Link/Wall/Floor Object Connectivity, Point Object Connectivity, Tower and Base Story Definitions | joint names | lower joint of each exactly plumb column = top joint of another plumb column (identity, not coordinates) | column below | founded at base level; tied by a link to another column | MODEL_INTEGRITY | FAIL |
| GEO-004 | Area Assignments - Pier Labels + Wall Object Connectivity + Point Object Connectivity | `Pier Name`, corners | panels of one (story, pier) form one connected group in plan | 1 group | adjacency within `Geometry.PierAdjacencyToleranceMm` (20) | MODEL_INTEGRITY | FAIL |
| SUP-001 | Joint Assignments - Restraints + Point Object Connectivity | `UX..RZ`, `Z` | restrained joint at the lowest joint Z | lowest floor | foundation model (`fnd` in file name) → no restraint at all; table absent → NOT_CHECKED | MODEL_INTEGRITY | FAIL |
| LOAD-003 | object API `FrameObj.GetLoadDistributed` (live only) | load type, direction | force: Gravity or Projected Gravity; no distributed moment at all | Gravity force only | Global Z fails deliberately; any distributed moment fails (user decision 2026-10-11); Excel runs → NOT_CHECKED | MODELING_STANDARD | FAIL |
| LOAD-004 | Wall Object Connectivity + `AreaObj.GetLoadUniform` (live only) + Area Load Assignments - Uniform Load Sets | direction, load set | no gravity uniform load, no load set on a wall | none | lateral loads pass; unreadable part → NOT_CHECKED | MODELING_STANDARD | FAIL |

Failing objects of GEO-001..004, SUP-001 and LOAD-003/004 can be written to the ETABS groups
`VChk_WallGeometry`, `VChk_ColumnPlumb`, `VChk_ColumnContinuity`, `VChk_PierContinuity`,
`VChk_Restraints`, `VChk_LineLoads`, `VChk_WallLoads` (same names as the pier-validation tool).
Each group is emptied first, then refilled, after the user confirms — the only model write the tool makes.

## Discrepancies found vs. the original spec (resolved with explicit config defaults)

### 1. WALL-003 scope
Spec text said "F11 modifier = 0.70, F22 modifier = 0.70" as the *minimum* requirement. The
reference workbook shows **all six** modifiers (f11, f22, f12, m11, m22, m12) set to 0.70 on
every ordinary wall (`W300-C40`, `W350-C40`, ... `W700-C56`). Implemented as: all six modifiers
checked against one configured value, default `0.70`. Configure `walls.checkedModifiers` if only
a subset should be enforced.

### 2. Slab classification gap — RAFT / mat foundation slabs
The reference workbook contains a slab category not mentioned in the spec: `RAFT 400-C50`,
`RAFT 600-C50`, `RAFT 800-C50`, `RAFT 2000-C50`, `RAFT 4300-C50` — all with every modifier
(f and m, all six) equal to `1.0`. Under the spec's 3-way classification (PT / Stair-Ramp /
Ordinary-catchall), these would incorrectly classify as "Ordinary" and fail SLAB-002 (expects
0.25). Added a 4th classification, `Raft`, matched by a `RAFT` name prefix, with its own
expected-modifier profile (default `1.0` for m11/m22/m12, unchecked f-modifiers). This is a
`MODELING_STANDARD` classification, not a code requirement — flagged `REFERENCE_PENDING` until
confirmed as a firm project rule.

### 3. FRAME-002 scope — columns only, not beams
Spec text implied every frame's end length offsets should be whole numbers. Per explicit user
feedback, this only holds for columns — beams routinely carry non-integer, geometry-derived
offsets (computed rigid-zone lengths from intersecting member widths), and flagging those is a
false positive, not a real issue. Implemented by cross-referencing each frame's `UniqueName`
against `Concrete Column Overwrites - ACI 318-19` (which lists only columns) rather than guessing
from the label prefix (`C...` vs `B...`) — that convention is common but not guaranteed across
projects. Configurable via `Frames.RestrictIntegerOffsetCheckToColumns` (default `true`); set
`false` to check every frame again.

## Naming convention (as observed — configurable, not hard-coded)

- Walls: `W<thickness><suffix>-C<grade>` (e.g. `W300-C40`), exceptions `WT<thickness>-C<grade>`
  (Water Tank), `BW<thickness>-C<grade>` (Basement Wall).
- Slabs: `<Prefix><thickness>-C<grade>` where prefix drives classification: `PT` → PT slab,
  `STAIR` → stair, `Ramp` → ramp, `RAFT ` (with space) → raft/mat, otherwise → ordinary
  (`S...`, `D...` drop panels also ordinary).
- Concrete material grade: `C<Fc>/<Fc2>` (e.g. `C40/50` → Fc = 40 MPa), optional `-TD` suffix for
  time-dependent variants (same Fc).

## Confirmed edge cases present in the reference workbook (used as test fixtures)

- `FRAME-002`: Frame `C143`, Story `ROOF LEVEL`, `Offset J = 949.7` — real FAIL case, matches the
  spec's example exactly.
- `LOAD-001`: Load pattern `Dead` (note: capitalization is `Dead`, not `DEAD`), Type = `Dead`,
  Self Weight Multiplier = `1` → PASS.
- `MODAL-001`: `P01-B01-Modal`, Target Ratio = `99` → PASS (>= 95 threshold).
- `DIA-001`: `D1`, `D2`, `D3` all `Semi-Rigid` in this reference model — no FAIL fixture present
  here; a synthetic FAIL case is needed in unit tests.
- `MAT-REBAR-001`: `Fy420` (Fy=420, Fu=525, Fye=420, Fue=525) and `Fy500` (Fy=500, Fu=625,
  Fye=500, Fue=625) — exact match to spec.
