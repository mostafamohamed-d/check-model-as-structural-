using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using ETABSModelDefinitionValidator.Excel;
using Xunit;

namespace ETABSModelDefinitionValidator.Tests
{
    public class ExcelModelDataProviderTests : IDisposable
    {
        private readonly string _path;

        public ExcelModelDataProviderTests()
        {
            _path = Path.Combine(Path.GetTempPath(), $"etabs-test-{Guid.NewGuid():N}.xlsx");
            BuildFixtureWorkbook(_path);
        }

        public void Dispose()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        private static void BuildFixtureWorkbook(string path)
        {
            using var workbook = new XLWorkbook();

            var walls = workbook.Worksheets.Add("Walls");
            walls.Cell(1, 1).Value = "TABLE:  Wall Property Definitions - Specified";
            walls.Cell(2, 1).Value = "Name"; walls.Cell(2, 2).Value = "Wall Thickness";
            walls.Cell(3, 1).Value = ""; walls.Cell(3, 2).Value = "mm";
            walls.Cell(4, 1).Value = "W300-C40"; walls.Cell(4, 2).Value = 300;

            var diaphragms = workbook.Worksheets.Add("Diaphragms");
            diaphragms.Cell(1, 1).Value = "TABLE:  Diaphragm Definitions";
            diaphragms.Cell(2, 1).Value = "Name"; diaphragms.Cell(2, 2).Value = "Rigidity Type";
            diaphragms.Cell(3, 1).Value = ""; diaphragms.Cell(3, 2).Value = "";
            diaphragms.Cell(4, 1).Value = "D1"; diaphragms.Cell(4, 2).Value = "Semi-Rigid";

            workbook.SaveAs(path);
        }

        [Fact]
        public void NoRestriction_PopulatesEveryKnownTablePresent()
        {
            var provider = new ExcelModelDataProvider(_path);

            var model = provider.GetModelData();

            Assert.Single(model.WallProperties);
            Assert.Single(model.DiaphragmDefinitions);
            Assert.Empty(model.SkippedTables);
        }

        [Fact]
        public void Restriction_SkipsRowReadingForExcludedTables_ButKeepsIncludedOnes()
        {
            var provider = new ExcelModelDataProvider(_path, logger: null,
                restrictToTables: new[] { Core.KnownTables.DiaphragmDefinitions });

            var model = provider.GetModelData();

            Assert.Empty(model.WallProperties);
            Assert.Single(model.DiaphragmDefinitions);
            Assert.Contains(Core.KnownTables.WallPropertyDefinitionsSpecified, model.SkippedTables);
            Assert.DoesNotContain(Core.KnownTables.DiaphragmDefinitions, model.SkippedTables);
        }

        [Fact]
        public void Restriction_DoesNotReportSkippedTablesAsMissing()
        {
            var provider = new ExcelModelDataProvider(_path, logger: null,
                restrictToTables: new[] { Core.KnownTables.DiaphragmDefinitions });

            var model = provider.GetModelData();

            Assert.DoesNotContain(Core.KnownTables.WallPropertyDefinitionsSpecified, model.MissingTables);
        }
    }
}
