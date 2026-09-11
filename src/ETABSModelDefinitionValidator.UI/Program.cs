using System;
using System.IO;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Logging;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.Excel;
using ETABSModelDefinitionValidator.Reporting;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.UI
{
    /// <summary>
    /// Console runner: the v1 end-to-end entry point. Load Excel -> normalize -> run all
    /// registered rules -> print summary -> export CSV. A full WPF dashboard is Phase 7
    /// (see docs/Roadmap.md) - this is deliberately not that, per the "don't start with a large
    /// UI" instruction; it exists so the engine can be exercised and verified today.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: ETABSModelDefinitionValidator.UI <path-to-etabs-export.xlsx> [config.json] [output.csv]");
                return 1;
            }

            var excelPath = args[0];
            var configPath = args.Length > 1 ? args[1] : null;
            var csvPath = args.Length > 2 ? args[2] : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(excelPath)) ?? ".", "validation-report.csv");

            var logger = new ConsoleValidationLogger();

            try
            {
                var config = configPath != null
                    ? ConfigurationLoader.LoadFromFile(configPath)
                    : ConfigurationLoader.LoadDefault();

                logger.Info($"Loading '{excelPath}'...");
                var provider = new ExcelModelDataProvider(excelPath, logger);
                var model = provider.GetModelData();

                foreach (var issue in model.ImportIssues)
                {
                    if (issue.Level == Core.Model.ImportIssueLevel.Error) logger.Error(issue.Message);
                    else if (issue.Level == Core.Model.ImportIssueLevel.Warning) logger.Warning(issue.Message);
                    else logger.Info(issue.Message);
                }

                if (model.UnrecognizedTables.Count > 0)
                {
                    logger.Warning($"{model.UnrecognizedTables.Count} table(s) in the workbook were not recognized and were not validated: {string.Join(", ", model.UnrecognizedTables)}");
                }

                var engine = new ValidationEngine(logger);
                var results = engine.Run(model, config, RuleRegistry.AllRules);

                ConsoleReportWriter.WriteSummary(results);
                ConsoleReportWriter.WriteFailuresAndWarnings(results);

                CsvReportWriter.WriteToFile(results, csvPath);
                logger.Info($"Detailed CSV report written to '{csvPath}'.");

                var summary = ValidationSummary.From(results);
                return summary.OverallStatus == ValidationStatus.Fail ? 2 : 0;
            }
            catch (Exception ex)
            {
                logger.Error($"Validation run failed: {ex.Message}");
                return 1;
            }
        }
    }
}
