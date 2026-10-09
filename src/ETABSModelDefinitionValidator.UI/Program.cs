using System;
using System.Linq;
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
                Console.WriteLine("       ETABSModelDefinitionValidator.UI --etabs-probe [pid]   (read-only table-extraction diagnostic on a running ETABS)");
                return 1;
            }

            if (args[0] == "--etabs-unitcheck")
            {
                // Live integration check of the PRODUCTION extraction path, including the temporary
                // switch to N-mm-C and the restore. Changes present units only for the duration.
                try
                {
                    var session = EtabsApi.EtabsSession.Attach(args.Length > 1 ? int.Parse(args[1]) : (int?)null);
                    Console.WriteLine($"Model: {session.ModelFilePath}");
                    Console.WriteLine(session.DiagnoseListingForms());
                    var before = session.PresentUnitsName();
                    Console.WriteLine($"Units before : {before}");

                    var provider = new EtabsApi.EtabsModelDataProvider(session, new[]
                    {
                        Core.KnownTables.WallPropertyDefinitionsSpecified,
                        Core.KnownTables.MaterialPropertiesRebarData,
                        Core.KnownTables.MaterialPropertiesConcreteData,
                        Core.KnownTables.MaterialPropertiesBasicMechanicalProperties
                    });
                    var model = provider.GetModelData();

                    var after = session.PresentUnitsName();
                    Console.WriteLine($"Units after  : {after}   -> {(after == before ? "RESTORED" : "NOT RESTORED")}");
                    foreach (var w in model.WallProperties.Take(3)) Console.WriteLine($"Wall  {w.Name,-14} thickness = {w.ThicknessMm} (expect mm)");
                    foreach (var r in model.RebarMaterials) Console.WriteLine($"Rebar {r.Name,-14} Fy = {r.FyMpa}  Fu = {r.FuMpa} (expect MPa)");
                    foreach (var c in model.ConcreteMaterials) Console.WriteLine($"Conc  {c.Name,-14} Fc = {c.FcMpa}  E1 = {c.MechanicalProperties?.E1Mpa} (expect MPa)");
                    foreach (var i in model.ImportIssues) Console.WriteLine($"[{i.Level}] {i.Message}");
                    return after == before ? 0 : 3;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unit check failed: {ex.Message}");
                    return 1;
                }
            }

            if (args[0] == "--etabs-probe")
            {
                try
                {
                    var running = EtabsApi.EtabsSession.FindRunningEtabs();
                    foreach (var p in running) Console.WriteLine($"Running ETABS: pid {p.Id}  {p.MainWindowTitle}");
                    var session = EtabsApi.EtabsSession.Attach(args.Length > 1 ? int.Parse(args[1]) : (int?)null);
                    Console.WriteLine();
                    Console.WriteLine(EtabsApi.EtabsProbe.Run(session, System.Linq.Enumerable.ToList(System.Linq.Enumerable.Distinct(System.Linq.Enumerable.SelectMany(RuleRegistry.AllRules, r => r.ApplicableTables), StringComparer.OrdinalIgnoreCase))));
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Probe failed: {ex.Message}");
                    return 1;
                }
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
