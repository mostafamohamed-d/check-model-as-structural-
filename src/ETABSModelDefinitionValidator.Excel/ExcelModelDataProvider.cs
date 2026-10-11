using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using ETABSModelDefinitionValidator.Core;
using ETABSModelDefinitionValidator.Core.Logging;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Providers;

namespace ETABSModelDefinitionValidator.Excel
{
    /// <summary>
    /// Reads an ETABS Excel database export and normalizes it into ModelData. The workbook is
    /// opened and every table detected exactly once (§27 - never re-open per rule). Unknown
    /// tables are logged, not discarded; missing required tables are recorded on ModelData
    /// rather than silently treated as "nothing to check".
    /// </summary>
    public sealed class ExcelModelDataProvider : IModelDataProvider
    {
        private readonly string _filePath;
        private readonly IValidationLogger _logger;
        private readonly HashSet<string> _requiredTitles;

        public ExcelModelDataProvider(string filePath, IValidationLogger logger = null)
            : this(filePath, logger, restrictToTables: null)
        {
        }

        /// <param name="restrictToTables">When non-null, only these table titles are fully read -
        /// every other table detected in the workbook has its (potentially large) row scan
        /// skipped entirely. Callers derive this from the ApplicableTables of whichever rules are
        /// actually enabled for the run, so disabling a rule category also skips importing the
        /// tables only that category needed. Titles are matched case-insensitively.</param>
        public ExcelModelDataProvider(string filePath, IValidationLogger logger, IEnumerable<string> restrictToTables)
        {
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _logger = logger ?? new NullValidationLogger();
            _requiredTitles = restrictToTables == null ? null : new HashSet<string>(restrictToTables, StringComparer.OrdinalIgnoreCase);
        }

        public ModelData GetModelData()
        {
            if (!File.Exists(_filePath))
            {
                throw new FileNotFoundException($"ETABS Excel export not found: {_filePath}", _filePath);
            }

            var model = new ModelData { WorkbookName = Path.GetFileName(_filePath) };

            XLWorkbook workbook;
            try
            {
                workbook = new XLWorkbook(_filePath);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Could not open '{_filePath}' as an Excel workbook: {ex.Message}", ex);
            }

            using (workbook)
            {
                var detector = new TableDetector(_logger);
                var tables = detector.DetectTables(workbook, model.ImportIssues, _requiredTitles);
                PopulateModel(model, tables);
            }

            return model;
        }

        /// <summary>Builds ModelData from already-extracted tables, whatever their origin (an
        /// Excel export, or a live ETABS database-table read). This is the single place table
        /// titles are mapped to domain objects, so every source gets identical import behaviour.</summary>
        public static ModelData FromTables(string sourceName, IReadOnlyList<RawTable> tables, IValidationLogger logger = null)
        {
            var model = new ModelData { WorkbookName = sourceName };
            new ExcelModelDataProvider(sourceName, logger).PopulateModel(model, tables);
            return model;
        }

        private void PopulateModel(ModelData model, IReadOnlyList<RawTable> tables)
        {
            {
                // A title that appears twice is ambiguous: which copy is right is unknown, so neither is used.
                var groups = tables.GroupBy(t => t.TableTitle, StringComparer.OrdinalIgnoreCase).ToList();
                var byTitle = new Dictionary<string, RawTable>(StringComparer.OrdinalIgnoreCase);
                foreach (var g in groups)
                {
                    var copies = g.ToList();
                    if (copies.Count == 1)
                    {
                        byTitle[g.Key] = copies[0];
                        continue;
                    }

                    var report = new TableImportReport { Title = g.Key, State = TableImportState.Ambiguous, RecordCount = copies.Sum(c => c.Rows.Count) };
                    var where = string.Join(", ", copies.Select(c => $"'{c.WorksheetName}'"));
                    report.Messages.Add($"Table appears {copies.Count} times (worksheets {where}); neither copy was used.");
                    model.TableReports[g.Key] = report;
                    model.ImportIssues.Add(new ImportIssue
                    {
                        Level = ImportIssueLevel.Error,
                        Message = $"Table '{g.Key}' appears {copies.Count} times in the source (worksheets {where}). It was not used; rules depending on it report NOT_CHECKED.",
                        Source = new SourceLocation { WorkbookName = model.WorkbookName, WorksheetName = copies[0].WorksheetName, TableName = g.Key, RowNumber = -1 }
                    });
                }

                foreach (var table in byTitle.Values)
                {
                    if (table.RowsSkipped)
                    {
                        model.SkippedTables.Add(table.TableTitle);
                        model.TableReports[table.TableTitle] = new TableImportReport { Title = table.TableTitle, State = TableImportState.Skipped };
                    }
                }

                var recognized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                PopulateIfPresent(byTitle, KnownTables.WallPropertyDefinitionsSpecified, model, recognized,
                    (t, m) => PopulateWalls(t, m));
                PopulateIfPresent(byTitle, KnownTables.SlabPropertyDefinitions, model, recognized,
                    (t, m) => PopulateSlabs(t, m));
                PopulateIfPresent(byTitle, KnownTables.PierSectionProperties, model, recognized,
                    (t, m) => PopulatePiers(t, m));
                PopulateIfPresent(byTitle, KnownTables.ModalCaseDefinitionsRitz, model, recognized,
                    (t, m) => PopulateModalRitz(t, m));
                PopulateIfPresent(byTitle, KnownTables.MaterialPropertiesRebarData, model, recognized,
                    (t, m) => PopulateRebarMaterials(t, m));
                PopulateIfPresent(byTitle, KnownTables.MaterialPropertiesConcreteData, model, recognized,
                    (t, m) => PopulateConcreteMaterials(t, m));
                PopulateIfPresent(byTitle, KnownTables.MaterialPropertiesBasicMechanicalProperties, model, recognized,
                    (t, m) => PopulateConcreteMechanicalProperties(t, m));
                PopulateIfPresent(byTitle, KnownTables.LoadPatternDefinitions, model, recognized,
                    (t, m) => PopulateLoadPatterns(t, m));
                PopulateIfPresent(byTitle, KnownTables.LoadCaseDefinitionsLinearStatic, model, recognized,
                    (t, m) => PopulateLoadCasesLinearStatic(t, m));
                PopulateIfPresent(byTitle, KnownTables.FrameAssignmentsFrameAutoMeshOptions, model, recognized,
                    (t, m) => PopulateFrameAutoMesh(t, m));
                PopulateIfPresent(byTitle, KnownTables.FrameAssignmentsEndLengthOffsets, model, recognized,
                    (t, m) => PopulateFrameEndOffsets(t, m));
                PopulateIfPresent(byTitle, KnownTables.DiaphragmDefinitions, model, recognized,
                    (t, m) => PopulateDiaphragms(t, m));
                PopulateIfPresent(byTitle, KnownTables.ConcreteColumnOverwritesAci31819, model, recognized,
                    (t, m) => PopulateColumnOverwrites(t, m));
                PopulateIfPresent(byTitle, KnownTables.PointObjectConnectivity, model, recognized,
                    (t, m) => PopulatePoints(t, m));
                PopulateIfPresent(byTitle, KnownTables.WallObjectConnectivity, model, recognized,
                    (t, m) => PopulateAreaObjects(t, m, "WallBay", m.WallObjects));
                PopulateIfPresent(byTitle, KnownTables.FloorObjectConnectivity, model, recognized,
                    (t, m) => PopulateAreaObjects(t, m, "FloorBay", m.FloorObjects));
                PopulateIfPresent(byTitle, KnownTables.AreaAssignmentsPierLabels, model, recognized,
                    (t, m) => PopulatePierLabels(t, m));
                PopulateIfPresent(byTitle, KnownTables.ColumnObjectConnectivity, model, recognized,
                    (t, m) => PopulateFrames(t, m, "ColumnBay", FrameKind.Column));
                PopulateIfPresent(byTitle, KnownTables.BeamObjectConnectivity, model, recognized,
                    (t, m) => PopulateFrames(t, m, "BeamBay", FrameKind.Beam));
                PopulateIfPresent(byTitle, KnownTables.BraceObjectConnectivity, model, recognized,
                    (t, m) => PopulateFrames(t, m, "BraceBay", FrameKind.Brace));
                PopulateIfPresent(byTitle, KnownTables.LinkObjectConnectivity, model, recognized,
                    (t, m) => PopulateLinks(t, m));
                PopulateIfPresent(byTitle, KnownTables.JointAssignmentsRestraints, model, recognized,
                    (t, m) => PopulateRestraints(t, m));
                PopulateIfPresent(byTitle, KnownTables.AreaLoadAssignmentsUniformLoadSets, model, recognized,
                    (t, m) => PopulateAreaLoadSets(t, m));
                PopulateIfPresent(byTitle, KnownTables.TowerAndBaseStoryDefinitions, model, recognized,
                    (t, m) => PopulateBaseElevation(t, m));

                foreach (var required in KnownTables.RequiredForInitialRuleSet)
                {
                    if (!byTitle.ContainsKey(required) && model.GetTableReport(required)?.State != TableImportState.Ambiguous)
                    {
                        model.MissingTables.Add(required);
                        model.ImportIssues.Add(new ImportIssue
                        {
                            Level = ImportIssueLevel.Warning,
                            Message = $"Required ETABS table '{required}' was not found in the workbook. Rules depending on it will report NOT_CHECKED.",
                            Source = new SourceLocation { WorkbookName = model.WorkbookName, WorksheetName = "(missing)", TableName = required, RowNumber = -1 }
                        });
                    }
                }

                foreach (var title in byTitle.Keys)
                {
                    if (!recognized.Contains(title))
                    {
                        model.UnrecognizedTables.Add(title);
                        // Unrecognized outranks Skipped: no check ever reads this table, whatever the profile.
                        model.TableReports[title] = new TableImportReport { Title = title, State = TableImportState.Unrecognized, RecordCount = byTitle[title].Rows.Count };
                    }
                }
            }
        }

        private void PopulateIfPresent(
            Dictionary<string, RawTable> byTitle, string title, ModelData model, HashSet<string> recognized,
            Action<RawTable, ModelData> populate)
        {
            recognized.Add(title);

            if (model.TableReports.TryGetValue(title, out var existing) &&
                (existing.State == TableImportState.Ambiguous || existing.State == TableImportState.Skipped))
            {
                return; // already decided: not usable / deliberately not read
            }

            if (!byTitle.TryGetValue(title, out var table))
            {
                model.TableReports[title] = new TableImportReport { Title = title, State = TableImportState.Missing };
                return;
            }

            var (accepted, report) = TableValidator.Validate(table, TableSchemas.For(title));
            model.TableReports[title] = report;
            if (report.Messages.Count > 0 && report.State != TableImportState.Read && report.State != TableImportState.ReadEmpty)
            {
                model.ImportIssues.Add(new ImportIssue
                {
                    Level = report.IsUsable ? ImportIssueLevel.Warning : ImportIssueLevel.Error,
                    Message = $"Table '{title}' {report.State}: {report.AcceptedCount} of {report.RecordCount} record(s) accepted. " + string.Join(" ", report.Messages.Take(10)) +
                              (report.Messages.Count > 10 ? $" (+{report.Messages.Count - 10} more)" : "") +
                              (report.IsUsable ? " Rejected records were not validated." : " Rules depending on it report NOT_CHECKED."),
                    Source = new SourceLocation { WorkbookName = model.WorkbookName, WorksheetName = table.WorksheetName, TableName = title, RowNumber = -1 }
                });
            }

            if (!report.IsUsable) return;

            try
            {
                populate(accepted, model);
            }
            catch (Exception ex)
            {
                report.State = TableImportState.FailedToRead;
                report.Messages.Add("Parsing failed: " + ex.Message);
                model.ImportIssues.Add(new ImportIssue
                {
                    Level = ImportIssueLevel.Error,
                    Message = $"Failed to parse table '{title}': {ex.Message} Rules depending on it report NOT_CHECKED.",
                    Source = new SourceLocation { WorkbookName = model.WorkbookName, WorksheetName = table.WorksheetName, TableName = title, RowNumber = -1 }
                });
            }
        }

        private SourceLocation Loc(ModelData model, RawTable table, RawRow row) => new SourceLocation
        {
            WorkbookName = model.WorkbookName,
            WorksheetName = table.WorksheetName,
            TableName = table.TableTitle,
            RowNumber = row.ExcelRowNumber
        };

        private void PopulateWalls(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var name = row.GetString("Name");
                if (string.IsNullOrEmpty(name)) continue;

                model.WallProperties.Add(new WallProperty
                {
                    Source = Loc(model, table, row),
                    Name = name,
                    Material = row.GetString("Material"),
                    ThicknessMm = row.GetDouble("WallThickness"),
                    F11Modifier = row.GetDouble("F11Modifier", 1),
                    F22Modifier = row.GetDouble("F22Modifier", 1),
                    F12Modifier = row.GetDouble("F12Modifier", 1),
                    M11Modifier = row.GetDouble("M11Modifier", 1),
                    M22Modifier = row.GetDouble("M22Modifier", 1),
                    M12Modifier = row.GetDouble("M12Modifier", 1),
                });
            }
        }

        private void PopulateSlabs(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var name = row.GetString("Name");
                if (string.IsNullOrEmpty(name)) continue;

                model.SlabProperties.Add(new SlabProperty
                {
                    Source = Loc(model, table, row),
                    Name = name,
                    Material = row.GetString("Material"),
                    ThicknessMm = row.GetDouble("SlabThickness"),
                    F11Modifier = row.GetDouble("F11Modifier", 1),
                    F22Modifier = row.GetDouble("F22Modifier", 1),
                    F12Modifier = row.GetDouble("F12Modifier", 1),
                    M11Modifier = row.GetDouble("M11Modifier", 1),
                    M22Modifier = row.GetDouble("M22Modifier", 1),
                    M12Modifier = row.GetDouble("M12Modifier", 1),
                });
            }
        }

        private void PopulatePiers(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var pier = row.GetString("Pier");
                if (string.IsNullOrEmpty(pier)) continue;

                model.PierProperties.Add(new PierProperty
                {
                    Source = Loc(model, table, row),
                    Story = row.GetString("Story"),
                    Pier = pier,
                    WidthBottomMm = row.GetDouble("WidthBottom"),
                    ThicknessBottomMm = row.GetDouble("ThicknessBottom"),
                    WidthTopMm = row.GetDouble("WidthTop"),
                    ThicknessTopMm = row.GetDouble("ThicknessTop"),
                    Material = row.GetString("Material")
                });
            }
        }

        private void PopulateModalRitz(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var name = row.GetString("Name");
                if (string.IsNullOrEmpty(name)) continue;

                model.ModalRitzCases.Add(new ModalRitzCase
                {
                    Source = Loc(model, table, row),
                    Name = name,
                    LoadType = row.GetString("LoadType"),
                    LoadName = row.GetString("LoadName"),
                    TargetRatioPercent = row.GetNullableDouble("TargetRatio")
                });
            }
        }

        private void PopulateRebarMaterials(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var name = row.GetString("Material");
                if (string.IsNullOrEmpty(name)) continue;

                model.RebarMaterials.Add(new RebarMaterial
                {
                    Source = Loc(model, table, row),
                    Name = name,
                    FyMpa = row.GetDouble("Fy"),
                    FuMpa = row.GetDouble("Fu"),
                    FyeMpa = row.GetDouble("Fye"),
                    FueMpa = row.GetDouble("Fue")
                });
            }
        }

        private void PopulateConcreteMaterials(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var name = row.GetString("Material");
                if (string.IsNullOrEmpty(name)) continue;

                model.ConcreteMaterials.Add(new ConcreteMaterial
                {
                    Source = Loc(model, table, row),
                    Name = name,
                    FcMpa = row.GetDouble("Fc"),
                    IsLightweight = row.GetString("LtWtConc")?.Equals("Yes", StringComparison.OrdinalIgnoreCase) == true
                });
            }
        }

        private void PopulateConcreteMechanicalProperties(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var name = row.GetString("Material");
                if (string.IsNullOrEmpty(name)) continue;

                var props = new ConcreteMechanicalProperties
                {
                    Source = Loc(model, table, row),
                    Material = name,
                    UnitWeightKnPerM3 = row.GetDouble("UnitWeight"),
                    UnitMassKgPerM3 = row.GetDouble("UnitMass"),
                    E1Mpa = row.GetDouble("E1"),
                };

                var material = model.ConcreteMaterials.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
                if (material != null)
                {
                    material.MechanicalProperties = props;
                }
            }
        }

        private void PopulateLoadPatterns(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var name = row.GetString("Name");
                if (string.IsNullOrEmpty(name)) continue;

                model.LoadPatterns.Add(new LoadPattern
                {
                    Source = Loc(model, table, row),
                    Name = name,
                    Type = row.GetString("Type"),
                    SelfWeightMultiplier = row.GetDouble("SelfWeightMultiplier")
                });
            }
        }

        private void PopulateLoadCasesLinearStatic(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var name = row.GetString("Name");
                if (string.IsNullOrEmpty(name)) continue;

                model.LoadCasesLinearStatic.Add(new LoadCaseLinearStatic
                {
                    Source = Loc(model, table, row),
                    Name = name,
                    LoadType = row.GetString("LoadType"),
                    LoadName = row.GetString("LoadName"),
                });
            }
        }

        private void PopulateFrameAutoMesh(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var uniqueName = row.GetString("UniqueName");
                if (string.IsNullOrEmpty(uniqueName)) continue;

                model.FrameAutoMeshes.Add(new FrameAutoMesh
                {
                    Source = Loc(model, table, row),
                    Story = row.GetString("Story"),
                    Label = row.GetString("Label"),
                    UniqueName = uniqueName,
                    AutoMeshEnabled = row.GetYesNoBool("AutoMesh")
                });
            }
        }

        private void PopulateFrameEndOffsets(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var uniqueName = row.GetString("UniqueName");
                if (string.IsNullOrEmpty(uniqueName)) continue;

                model.FrameEndOffsets.Add(new FrameEndOffset
                {
                    Source = Loc(model, table, row),
                    Story = row.GetString("Story"),
                    Label = row.GetString("Label"),
                    UniqueName = uniqueName,
                    OffsetIMm = row.GetDouble("OffsetI"),
                    OffsetJMm = row.GetDouble("OffsetJ")
                });
            }
        }

        private void PopulateDiaphragms(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var name = row.GetString("Name");
                if (string.IsNullOrEmpty(name)) continue;

                model.DiaphragmDefinitions.Add(new DiaphragmDefinition
                {
                    Source = Loc(model, table, row),
                    Name = name,
                    RigidityType = row.GetString("RigidityType")
                });
            }
        }

        private void PopulateColumnOverwrites(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var uniqueName = row.GetString("UniqueName");
                if (string.IsNullOrEmpty(uniqueName)) continue;

                model.ColumnDesignOverwrites.Add(new ColumnDesignOverwrite
                {
                    Source = Loc(model, table, row),
                    Story = row.GetString("Story"),
                    Label = row.GetString("Label"),
                    UniqueName = uniqueName,
                    DesignSection = row.GetString("DesignSection"),
                });
            }
        }

        // Connectivity tables: headers verified against ETABS 22.3 (UniqueName, Story, <Kind>Bay, UniquePt...).

        private void PopulatePoints(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var uniqueName = row.GetString("UniqueName");
                if (string.IsNullOrEmpty(uniqueName)) continue;

                model.PointObjects.Add(new PointObject
                {
                    Source = Loc(model, table, row),
                    UniqueName = uniqueName,
                    Story = row.GetString("Story"),
                    Label = row.GetString("PointBay"),
                    X = row.GetDouble("X"),
                    Y = row.GetDouble("Y"),
                    Z = row.GetDouble("Z")
                });
            }
        }

        private void PopulateAreaObjects(RawTable table, ModelData model, string labelColumn, List<WallObject> target)
        {
            foreach (var row in table.Rows)
            {
                var uniqueName = row.GetString("UniqueName");
                if (string.IsNullOrEmpty(uniqueName)) continue;

                var area = new WallObject
                {
                    Source = Loc(model, table, row),
                    UniqueName = uniqueName,
                    Story = row.GetString("Story"),
                    Label = row.GetString(labelColumn)
                };
                // UniquePt1..UniquePtN; a triangle leaves the last one blank.
                for (var i = 1; ; i++)
                {
                    var key = "UniquePt" + i;
                    if (!table.Columns.Any(c => c.NormalizedKey == key)) break;
                    var point = row.GetString(key);
                    if (!string.IsNullOrEmpty(point)) area.PointNames.Add(point);
                }
                target.Add(area);
            }
        }

        private void PopulatePierLabels(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var uniqueName = row.GetString("UniqueName");
                if (string.IsNullOrEmpty(uniqueName)) continue;

                model.PierLabelAssignments.Add(new PierLabelAssignment
                {
                    Source = Loc(model, table, row),
                    Story = row.GetString("Story"),
                    Label = row.GetString("Label"),
                    UniqueName = uniqueName,
                    PierName = row.GetString("PierName")
                });
            }
        }

        private void PopulateFrames(RawTable table, ModelData model, string labelColumn, FrameKind kind)
        {
            foreach (var row in table.Rows)
            {
                var uniqueName = row.GetString("UniqueName");
                if (string.IsNullOrEmpty(uniqueName)) continue;

                model.FrameObjects.Add(new FrameObject
                {
                    Source = Loc(model, table, row),
                    UniqueName = uniqueName,
                    Story = row.GetString("Story"),
                    Label = row.GetString(labelColumn),
                    Kind = kind,
                    PointI = row.GetString("UniquePtI"),
                    PointJ = row.GetString("UniquePtJ")
                });
            }
        }

        private void PopulateLinks(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var name = row.GetString("Name");
                if (string.IsNullOrEmpty(name)) continue;

                model.LinkObjects.Add(new LinkObject
                {
                    Source = Loc(model, table, row),
                    Name = name,
                    Story = row.GetString("Story"),
                    Point1 = row.GetString("UniquePt1"),
                    Point2 = row.GetString("UniquePt2")
                });
            }
        }

        private void PopulateRestraints(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var uniqueName = row.GetString("UniqueName");
                if (string.IsNullOrEmpty(uniqueName)) continue;

                model.JointRestraints.Add(new JointRestraint
                {
                    Source = Loc(model, table, row),
                    UniqueName = uniqueName,
                    Story = row.GetString("Story"),
                    Label = row.GetString("Label"),
                    Fixed = new[] { "UX", "UY", "UZ", "RX", "RY", "RZ" }.Select(d => row.GetYesNoBool(d)).ToArray()
                });
            }
        }

        private void PopulateAreaLoadSets(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var uniqueName = row.GetString("UniqueName");
                if (string.IsNullOrEmpty(uniqueName)) continue;

                model.AreaLoadSets.Add(new AreaLoadSetAssignment
                {
                    Source = Loc(model, table, row),
                    UniqueName = uniqueName,
                    Story = row.GetString("Story"),
                    Label = row.GetString("Label"),
                    LoadSet = row.GetString("LoadSet")
                });
            }
        }

        private void PopulateBaseElevation(RawTable table, ModelData model)
        {
            var elevations = table.Rows.Select(r => r.GetNullableDouble("BSElev")).Where(v => v.HasValue).Select(v => v.Value).ToList();
            if (elevations.Count > 0) model.BaseElevationMm = elevations.Min();
        }
    }
}
