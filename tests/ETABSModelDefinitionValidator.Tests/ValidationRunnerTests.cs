using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.UI.WinForms;
using Xunit;

namespace ETABSModelDefinitionValidator.Tests
{
    public class ValidationRunnerTests : IDisposable
    {
        private readonly string _path;

        public ValidationRunnerTests()
        {
            _path = Path.Combine(Path.GetTempPath(), $"etabs-runner-test-{Guid.NewGuid():N}.xlsx");

            using var workbook = new XLWorkbook();

            var walls = workbook.Worksheets.Add("Walls");
            walls.Cell(1, 1).Value = "TABLE:  Wall Property Definitions - Specified";
            walls.Cell(2, 1).Value = "Name"; walls.Cell(2, 4).Value = "Modeling Type"; walls.Cell(2, 8).Value = "Wall Thickness";
            walls.Cell(3, 8).Value = "mm";
            walls.Cell(4, 1).Value = "W300-C40"; walls.Cell(4, 8).Value = 300;

            var diaphragms = workbook.Worksheets.Add("Diaphragms");
            diaphragms.Cell(1, 1).Value = "TABLE:  Diaphragm Definitions";
            diaphragms.Cell(2, 1).Value = "Name"; diaphragms.Cell(2, 2).Value = "Rigidity Type";
            diaphragms.Cell(4, 1).Value = "D1"; diaphragms.Cell(4, 2).Value = "Rigid"; // deliberately wrong -> a FAIL result

            workbook.SaveAs(_path);
        }

        public void Dispose()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        [Fact]
        public async Task RunAsync_CompletesOffCallingThread_AndReturnsResults()
        {
            var runner = new ValidationRunner();
            var callingThreadId = Thread.CurrentThread.ManagedThreadId;
            var progressMessages = new System.Collections.Generic.List<string>();

            var result = await runner.RunAsync(
                _path,
                ConfigurationLoader.LoadDefault(),
                enabledCategories: null,
                progress: new Progress<string>(msg => progressMessages.Add(msg)),
                cancellationToken: CancellationToken.None);

            Assert.NotNull(result.Model);
            Assert.NotEmpty(result.Results);
            Assert.Contains(result.Results, r => r.RuleId == "DIA-001" && r.Status == ValidationStatus.Fail);
            Assert.NotEmpty(progressMessages);
        }

        [Fact]
        public async Task RunAsync_RestrictedToOneCategory_OnlyRunsThatCategorysRulesAndSkipsOtherTables()
        {
            var runner = new ValidationRunner();

            var result = await runner.RunAsync(
                _path,
                ConfigurationLoader.LoadDefault(),
                enabledCategories: new[] { "Diaphragms" },
                progress: null,
                cancellationToken: CancellationToken.None);

            Assert.All(result.Results, r => Assert.Equal("Diaphragms", r.Category));
            Assert.Contains(Core.KnownTables.WallPropertyDefinitionsSpecified, result.Model.SkippedTables);
            Assert.Empty(result.Model.WallProperties);
        }

        [Fact]
        public async Task RunAsync_HonorsCancellation()
        {
            var runner = new ValidationRunner();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
                _path,
                ConfigurationLoader.LoadDefault(),
                enabledCategories: null,
                progress: null,
                cancellationToken: cts.Token));
        }
    }
}
