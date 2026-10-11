using System;
using System.Linq;
using System.IO;
using ETABSModelDefinitionValidator.Core;
using ETABSModelDefinitionValidator.Core.Configuration;
using ETABSModelDefinitionValidator.Core.Logging;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;
using ETABSModelDefinitionValidator.EtabsApi;
using ETABSModelDefinitionValidator.Excel;
using ETABSModelDefinitionValidator.Reporting;
using ETABSModelDefinitionValidator.Rules;

namespace ETABSModelDefinitionValidator.UI
{
    /// <summary>
    /// Console runner (CI/scripting): validates an ETABS Excel export, plus read-only diagnostics
    /// against a running ETABS. Exit codes: 0 = PASS / PASS WITH WARNINGS, 2 = FAIL, 3 = INCOMPLETE,
    /// 4 = REVIEW REQUIRED, 1 = the run itself errored.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: ETABSModelDefinitionValidator.UI <path-to-etabs-export.xlsx> [config.json] [output.csv]");
                Console.WriteLine("       ETABSModelDefinitionValidator.UI --etabs-probe [pid] [\"Title;Title\"]   (read-only table-extraction diagnostic on a running ETABS)");
                Console.WriteLine("       ETABSModelDefinitionValidator.UI --etabs-unitcheck [pid]   (production read path; switches to N-mm-C and verifies the restore)");
                Console.WriteLine("       ETABSModelDefinitionValidator.UI --etabs-geocheck [pid]    (live run of the geometry/support/load-assignment checks; read-only)");
                return 1;
            }

            switch (args[0])
            {
                case "--etabs-probe": return Live(args, "Probe", Probe);
                case "--etabs-unitcheck": return Live(args, "Unit check", UnitCheck);
                case "--etabs-geocheck": return Live(args, "Geometry check", GeoCheck);
                default: return ValidateExcel(args);
            }
        }

        private static int ValidateExcel(string[] args)
        {
            var excelPath = args[0];
            var configPath = args.Length > 1 ? args[1] : null;
            var csvPath = args.Length > 2 ? args[2] : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(excelPath)) ?? ".", "validation-report.csv");
            var logger = new ConsoleValidationLogger();

            try
            {
                var config = configPath != null ? ConfigurationLoader.LoadFromFile(configPath) : ConfigurationLoader.LoadDefault();

                // Engineering validation of the profile BEFORE anything runs; errors stop the run (exit 1).
                foreach (var issue in ConfigurationValidator.EnsureValid(config)) logger.Warning("Profile: " + issue);

                logger.Info($"Loading '{excelPath}'...");
                var model = new ExcelModelDataProvider(excelPath, logger).GetModelData();

                foreach (var issue in model.ImportIssues)
                {
                    if (issue.Level == ImportIssueLevel.Error) logger.Error(issue.Message);
                    else if (issue.Level == ImportIssueLevel.Warning) logger.Warning(issue.Message);
                    else logger.Info(issue.Message);
                }
                if (model.UnrecognizedTables.Count > 0)
                {
                    logger.Warning($"{model.UnrecognizedTables.Count} table(s) in the workbook were not recognized and were not validated: {string.Join(", ", model.UnrecognizedTables)}");
                }

                var rules = RuleRegistry.AllRules;
                var results = new ValidationEngine(logger).Run(model, config, rules);
                RuleCatalog.Annotate(results, config);

                // Verdict from results + import completeness + profile policy - never results alone.
                var summary = ValidationSummary.From(results, model, rules, config.Reporting);
                summary.Assessment.CategoriesRun = summary.Assessment.CategoriesTotal = rules.Select(r => r.Category).Distinct().Count();

                ConsoleReportWriter.WriteSummary(summary);
                ConsoleReportWriter.WriteFailuresAndWarnings(results);
                CsvReportWriter.WriteToFile(results, csvPath, summary.Assessment);
                logger.Info($"Detailed CSV report written to '{csvPath}'.");

                return summary.Assessment.ExitCode;
            }
            catch (InvalidConfigurationException ex)
            {
                logger.Error(ex.Message);
                return 1;
            }
            catch (Exception ex)
            {
                logger.Error($"Validation run failed: {ex.Message}");
                return 1;
            }
        }

        // ---- live ETABS diagnostics --------------------------------------------------------------

        /// <summary>Attaches to the ETABS given by the optional pid argument and runs one diagnostic.</summary>
        private static int Live(string[] args, string name, Func<EtabsSession, string[], int> diagnostic)
        {
            try
            {
                foreach (var p in EtabsSession.FindRunningEtabs()) Console.WriteLine($"Running ETABS: pid {p.Id}  {p.MainWindowTitle}");
                var session = EtabsSession.Attach(args.Length > 1 ? int.Parse(args[1]) : (int?)null);
                Console.WriteLine($"Model: {session.ModelFilePath}");
                return diagnostic(session, args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{name} failed: {ex.Message}");
                return 1;
            }
        }

        private static int Probe(EtabsSession session, string[] args)
        {
            // Optional third argument: ';'-separated table titles to probe instead of the rule set's tables.
            var titles = args.Length > 2
                ? args[2].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                : RuleRegistry.AllRules.SelectMany(r => r.ApplicableTables).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Console.WriteLine(EtabsProbe.Run(session, titles));
            return 0;
        }

        /// <summary>Production extraction path incl. the temporary switch to N-mm-C; exit 3 if units were not restored.</summary>
        private static int UnitCheck(EtabsSession session, string[] args)
        {
            var before = session.PresentUnitsName();
            Console.WriteLine($"Units before : {before}");

            var model = new EtabsModelDataProvider(session, new[]
            {
                KnownTables.WallPropertyDefinitionsSpecified,
                KnownTables.MaterialPropertiesRebarData,
                KnownTables.MaterialPropertiesConcreteData,
                KnownTables.MaterialPropertiesBasicMechanicalProperties
            }).GetModelData();

            var after = session.PresentUnitsName();
            Console.WriteLine($"Units after  : {after}   -> {(after == before ? "RESTORED" : "NOT RESTORED")}");
            foreach (var w in model.WallProperties.Take(3)) Console.WriteLine($"Wall  {w.Name,-14} thickness = {w.ThicknessMm} (expect mm)");
            foreach (var r in model.RebarMaterials) Console.WriteLine($"Rebar {r.Name,-14} Fy = {r.FyMpa}  Fu = {r.FuMpa} (expect MPa)");
            foreach (var c in model.ConcreteMaterials) Console.WriteLine($"Conc  {c.Name,-14} Fc = {c.FcMpa}  E1 = {c.MechanicalProperties?.E1Mpa} (expect MPa)");
            foreach (var c in model.ConcreteMaterials.Where(c => c.MechanicalProperties != null))
                Console.WriteLine($"Mass  {c.Name,-14} UnitMass = {c.MechanicalProperties.UnitMassKgPerM3:G4}  UnitWeight = {c.MechanicalProperties.UnitWeightKnPerM3:G4} (raw, N-mm)");
            foreach (var r in new ConcretePropertyRule().Validate(model, ConfigurationLoader.LoadDefault()))
                Console.WriteLine($"MAT-CONC-002 {r.ObjectName,-14} {r.Status,-8} {r.Message}");
            foreach (var i in model.ImportIssues) Console.WriteLine($"[{i.Level}] {i.Message}");
            return after == before ? 0 : 3;
        }

        /// <summary>Geometry/connectivity/support/load-assignment checks on the live model. Never writes groups.</summary>
        private static int GeoCheck(EtabsSession session, string[] args)
        {
            var before = session.PresentUnitsName();
            var rules = RuleRegistry.AllRules.Where(r => FailureGroups.For(r.RuleId) != null).ToList();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var model = new EtabsModelDataProvider(session, rules.SelectMany(r => r.ApplicableTables).Distinct().ToList(),
                liveData: LiveData.LoadAssignments).GetModelData();
            Console.WriteLine($"Read in {watch.Elapsed.TotalSeconds:0.0} s: {model.PointObjects.Count:N0} joints, {model.WallObjects.Count:N0} walls, {model.FloorObjects.Count:N0} floors, " +
                              $"{model.FrameObjects.Count:N0} frames, {model.LinkObjects.Count:N0} links, {model.JointRestraints.Count:N0} restraints, base {model.BaseElevationMm} mm");
            var after = session.PresentUnitsName();
            Console.WriteLine($"Units {before} -> {after}: {(after == before ? "RESTORED" : "NOT RESTORED")}");
            foreach (var i in model.ImportIssues) Console.WriteLine($"  [{i.Level}] {i.Message}");

            watch.Restart();
            var results = new ValidationEngine(new NullValidationLogger()).RunParallel(model, ConfigurationLoader.LoadDefault(), rules);
            Console.WriteLine($"Rules ran in {watch.Elapsed.TotalSeconds:0.0} s");
            foreach (var g in results.GroupBy(r => r.RuleId))
            {
                Console.WriteLine($"{g.Key}: " + string.Join(", ", g.GroupBy(r => r.Status).Select(s => $"{s.Key} {s.Count():N0}")));
                foreach (var r in g.Where(r => r.Status != ValidationStatus.Pass).Take(3)) Console.WriteLine($"    {r.Status}: {r.Message}");
            }
            return after == before ? 0 : 3;
        }
    }
}
