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
                var byTitle = tables
                    .GroupBy(t => t.TableTitle, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                foreach (var table in tables)
                {
                    if (table.RowsSkipped)
                    {
                        model.SkippedTables.Add(table.TableTitle);
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
                PopulateIfPresent(byTitle, KnownTables.FrameAssignmentsPropertyModifiers, model, recognized,
                    (t, m) => PopulateFramePropertyModifiers(t, m));
                PopulateIfPresent(byTitle, KnownTables.FrameAssignmentsFrameAutoMeshOptions, model, recognized,
                    (t, m) => PopulateFrameAutoMesh(t, m));
                PopulateIfPresent(byTitle, KnownTables.FrameAssignmentsEndLengthOffsets, model, recognized,
                    (t, m) => PopulateFrameEndOffsets(t, m));
                PopulateIfPresent(byTitle, KnownTables.DiaphragmDefinitions, model, recognized,
                    (t, m) => PopulateDiaphragms(t, m));
                PopulateIfPresent(byTitle, KnownTables.ConcreteColumnOverwritesAci31819, model, recognized,
                    (t, m) => PopulateColumnOverwrites(t, m));

                foreach (var required in KnownTables.RequiredForInitialRuleSet)
                {
                    if (!byTitle.ContainsKey(required))
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

                foreach (var table in tables)
                {
                    if (!recognized.Contains(table.TableTitle))
                    {
                        model.UnrecognizedTables.Add(table.TableTitle);
                    }
                }

            }

            return model;
        }

        private void PopulateIfPresent(
            Dictionary<string, RawTable> byTitle, string title, ModelData model, HashSet<string> recognized,
            Action<RawTable, ModelData> populate)
        {
            recognized.Add(title);
            if (byTitle.TryGetValue(title, out var table))
            {
                try
                {
                    populate(table, model);
                }
                catch (Exception ex)
                {
                    model.ImportIssues.Add(new ImportIssue
                    {
                        Level = ImportIssueLevel.Error,
                        Message = $"Failed to parse table '{title}': {ex.Message}",
                        Source = new SourceLocation { WorkbookName = model.WorkbookName, WorksheetName = table.WorksheetName, TableName = title, RowNumber = -1 }
                    });
                }
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
                    ModelingType = row.GetString("ModelingType"),
                    Material = row.GetString("Material"),
                    ThicknessMm = row.GetDouble("WallThickness"),
                    F11Modifier = row.GetDouble("F11Modifier", 1),
                    F22Modifier = row.GetDouble("F22Modifier", 1),
                    F12Modifier = row.GetDouble("F12Modifier", 1),
                    M11Modifier = row.GetDouble("M11Modifier", 1),
                    M22Modifier = row.GetDouble("M22Modifier", 1),
                    M12Modifier = row.GetDouble("M12Modifier", 1),
                    V13Modifier = row.GetDouble("V13Modifier", 1),
                    V23Modifier = row.GetDouble("V23Modifier", 1),
                    MassModifier = row.GetDouble("MassModifier", 1),
                    WeightModifier = row.GetDouble("WeightModifier", 1)
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
                    ModelingType = row.GetString("ModelingType"),
                    PropertyType = row.GetString("PropertyType"),
                    Material = row.GetString("Material"),
                    ThicknessMm = row.GetDouble("SlabThickness"),
                    F11Modifier = row.GetDouble("F11Modifier", 1),
                    F22Modifier = row.GetDouble("F22Modifier", 1),
                    F12Modifier = row.GetDouble("F12Modifier", 1),
                    M11Modifier = row.GetDouble("M11Modifier", 1),
                    M22Modifier = row.GetDouble("M22Modifier", 1),
                    M12Modifier = row.GetDouble("M12Modifier", 1),
                    V13Modifier = row.GetDouble("V13Modifier", 1),
                    V23Modifier = row.GetDouble("V23Modifier", 1),
                    MassModifier = row.GetDouble("MassModifier", 1),
                    WeightModifier = row.GetDouble("WeightModifier", 1)
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
                    G12Mpa = row.GetDouble("G12"),
                    PoissonRatio = row.GetDouble("U12"),
                    ThermalCoefficientPerC = row.GetDouble("A1")
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
                    IsAutoLoad = row.GetYesNoBool("IsAutoLoad"),
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
                    LoadScaleFactor = row.GetDouble("LoadSF", 1)
                });
            }
        }

        private void PopulateFramePropertyModifiers(RawTable table, ModelData model)
        {
            foreach (var row in table.Rows)
            {
                var uniqueName = row.GetString("UniqueName");
                if (string.IsNullOrEmpty(uniqueName)) continue;

                model.FramePropertyModifiers.Add(new FramePropertyModifier
                {
                    Source = Loc(model, table, row),
                    Story = row.GetString("Story"),
                    Label = row.GetString("Label"),
                    UniqueName = uniqueName,
                    AreaModifier = row.GetDouble("AreaModifier", 1),
                    As2Modifier = row.GetDouble("As2Modifier", 1),
                    As3Modifier = row.GetDouble("As3Modifier", 1),
                    JModifier = row.GetDouble("JModifier", 1),
                    I22Modifier = row.GetDouble("I22Modifier", 1),
                    I33Modifier = row.GetDouble("I33Modifier", 1),
                    MassModifier = row.GetDouble("MassModifier", 1),
                    WeightModifier = row.GetDouble("WeightModifier", 1)
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
                    OffsetOption = row.GetString("OffsetOption"),
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
                    DesignType = row.GetString("DesignType"),
                    DesignSection = row.GetString("DesignSection"),
                    FrameType = row.GetString("FrameType")
                });
            }
        }
    }
}
