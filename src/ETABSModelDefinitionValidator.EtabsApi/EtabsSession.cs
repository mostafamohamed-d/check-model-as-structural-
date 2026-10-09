using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ETABSModelDefinitionValidator.EtabsApi
{
    /// <summary>One table the running ETABS model can provide.</summary>
    public sealed class EtabsTableInfo
    {
        public string Key { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// The only code that touches the ETABS API. Attaches to an ALREADY RUNNING ETABS (never
    /// starts one - that would take a second licence and could hang), loading ETABSv1.dll from
    /// that process's own install folder so ETABS 21 and 22 both work from one build. Calls go
    /// through reflection because the two ETABSv1.dll versions are different assembly identities.
    /// </summary>
    public sealed class EtabsSession
    {
        private const string ProgId = "CSI.ETABS.API.ETABSObject";

        private readonly Assembly _api;
        private readonly object _sapModel;
        private readonly object _tables;

        public string ModelFilePath { get; }
        public string ModelName => string.IsNullOrEmpty(ModelFilePath) ? "(unsaved model)" : Path.GetFileName(ModelFilePath);
        public string EtabsFolder { get; }
        public int ProcessId { get; }

        private EtabsSession(Assembly api, object sapModel, string modelPath, string folder, int pid)
        {
            _api = api;
            _sapModel = sapModel;
            _tables = Get(sapModel, "DatabaseTables");
            ModelFilePath = modelPath;
            EtabsFolder = folder;
            ProcessId = pid;
        }

        /// <summary>Test seam: wraps an object that exposes the same members as ETABSv1.cSapModel.
        /// <paramref name="api"/> must contain a type named ETABSv1.eUnits.</summary>
        internal static EtabsSession FromSapModel(Assembly api, object sapModel, string modelPath) =>
            new EtabsSession(api, sapModel, modelPath, "(test)", 0);

        /// <summary>Running ETABS processes, newest first.</summary>
        public static IReadOnlyList<Process> FindRunningEtabs()
        {
            return Process.GetProcessesByName("ETABS").OrderByDescending(SafeStart).ToList();
        }

        private static DateTime SafeStart(Process p) { try { return p.StartTime; } catch { return DateTime.MinValue; } }

        /// <summary>Attaches to a running ETABS. With several instances open, pass the process id; by default the most recently started one is used and the caller is told which.</summary>
        public static EtabsSession Attach(int? processId = null)
        {
            var running = FindRunningEtabs();
            if (running.Count == 0)
            {
                throw new InvalidOperationException("ETABS is not running. Open ETABS and the model you want to validate, then try again.");
            }

            var process = processId.HasValue ? running.FirstOrDefault(p => p.Id == processId.Value) : running[0];
            if (process == null) throw new InvalidOperationException($"No running ETABS with process id {processId}.");

            string exePath;
            try { exePath = process.MainModule.FileName; }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not read the install folder of ETABS (pid {process.Id}): {ex.Message}. Run this tool with the same privileges as ETABS and as a 64-bit process.", ex);
            }

            var folder = Path.GetDirectoryName(exePath);
            var dll = Path.Combine(folder, "ETABSv1.dll");
            if (!File.Exists(dll)) throw new FileNotFoundException($"ETABSv1.dll not found next to ETABS.exe ({folder}).", dll);

            var api = Assembly.LoadFrom(dll);
            var helper = Activator.CreateInstance(api.GetType("ETABSv1.Helper", true));
            var helperType = api.GetType("ETABSv1.cHelper", true);

            // Prefer the process-specific attach; older builds only offer GetObject (the registered instance).
            object etabsObject;
            var byProcess = helperType.GetMethod("GetObjectProcess");
            try
            {
                etabsObject = byProcess != null
                    ? byProcess.Invoke(helper, new object[] { ProgId, process.Id })
                    : helperType.GetMethod("GetObject").Invoke(helper, new object[] { ProgId });
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException($"Could not attach to the running ETABS (pid {process.Id}): {ex.InnerException?.Message ?? ex.Message}", ex.InnerException ?? ex);
            }
            if (etabsObject == null) throw new InvalidOperationException($"ETABS (pid {process.Id}) did not return an API object.");

            var sapModel = Get(etabsObject, "SapModel");
            if (sapModel == null) throw new InvalidOperationException("ETABS returned no SapModel - is a model open?");

            var path = (string)Call(sapModel, "GetModelFilename", true).Return;
            return new EtabsSession(api, sapModel, path, folder, process.Id);
        }

        /// <summary>Every table ETABS can display for this model (key + name).</summary>
        /// <summary>Returns null (not an exception) when this ETABS build refuses to list its tables -
        /// both listing calls have been seen to return 1 on some models/versions. Callers then
        /// request tables directly by name, which is also the table key in ETABS.</summary>
        public IReadOnlyList<EtabsTableInfo> GetAvailableTables(ICollection<string> diagnostics = null)
        {
            // Empty arrays rather than null: some ETABSv1 builds reject a null by-ref SAFEARRAY.
            var attempts = new (string Method, Func<object[]> Args)[]
            {
                ("GetAllTables", () => new object[] { 0, new string[0], new string[0], new int[0], new bool[0] }),
                ("GetAvailableTables", () => new object[] { 0, new string[0], new string[0], new int[0] }),
                ("GetAllTables", () => new object[] { 0, null, null, null, null }),
            };

            foreach (var (method, makeArgs) in attempts)
            {
                try
                {
                    var r = Call(_tables, method, makeArgs());
                    if (r.Return is int code && code != 0)
                    {
                        diagnostics?.Add($"DatabaseTables.{method} returned error code {code}.");
                        continue;
                    }
                    var keys = (string[])r.Args[1] ?? new string[0];
                    var names = (string[])r.Args[2] ?? new string[0];
                    var count = Math.Min((int)r.Args[0], Math.Min(keys.Length, names.Length));
                    var list = new List<EtabsTableInfo>();
                    for (var i = 0; i < count; i++) list.Add(new EtabsTableInfo { Key = keys[i], Name = names[i] });
                    if (list.Count > 0) return list;
                    diagnostics?.Add($"DatabaseTables.{method} returned no tables.");
                }
                catch (Exception ex)
                {
                    diagnostics?.Add($"DatabaseTables.{method} failed: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>Reads one table in full. Values come back in the model's present units - use <see cref="UnitsScope"/>.</summary>
        public RawTableData ReadTable(EtabsTableInfo table)
        {
            // Field display names are a nicety (they match the Excel headers); if ETABS won't give
            // them, the field keys are used as headers instead rather than failing the table.
            var nameByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var f = Call(_tables, "GetAllFieldsInTable", table.Key, 0, 0, new string[0], new string[0], new string[0], new string[0], new bool[0]);
                if (!(f.Return is int fc && fc != 0))
                {
                    var fieldKeys = (string[])f.Args[3] ?? new string[0];
                    var fieldNames = (string[])f.Args[4] ?? new string[0];
                    for (var i = 0; i < Math.Min(fieldKeys.Length, fieldNames.Length); i++) nameByKey[fieldKeys[i]] = fieldNames[i];
                }
            }
            catch (InvalidOperationException) { /* fall back to keys */ }

            // Empty FieldKeyList = all fields; empty group name = whole model. Try the empty-array
            // form first, then null, because builds differ in which one they accept.
            CallResult d = null;
            var codes = new List<string>();
            foreach (var fieldList in new[] { new string[0], null })
            {
                d = Call(_tables, "GetTableForDisplayArray", table.Key, fieldList, "", 0, new string[0], 0, new string[0]);
                if (!(d.Return is int dc && dc != 0)) break;
                codes.Add(d.Return.ToString());
                d = null;
            }
            if (d == null)
            {
                throw new InvalidOperationException(
                    $"DatabaseTables.GetTableForDisplayArray('{table.Key}') returned error code(s) {string.Join(", ", codes)}. " +
                    "Check the table exists in ETABS (Display > Show Tables) for this model.");
            }

            return new RawTableData
            {
                FieldKeys = (string[])d.Args[4] ?? new string[0],
                FieldNames = nameByKey,
                NumberRecords = (int)d.Args[5],
                Data = (string[])d.Args[6] ?? new string[0]
            };
        }

        /// <summary>Diagnostic only (read-only): the exact call the first version made - GetAllTables
        /// with null by-ref arrays - so its return code can be compared with the empty-array form.</summary>
        public string DiagnoseListingForms()
        {
            string Try(object[] args)
            {
                try { var r = Call(_tables, "GetAllTables", args); return $"returned {r.Return}, NumberTables={r.Args[0]}"; }
                catch (Exception ex) { return "threw: " + ex.Message; }
            }
            return "GetAllTables(null arrays)  " + Try(new object[] { 0, null, null, null, null }) + "\n" +
                   "GetAllTables(empty arrays) " + Try(new object[] { 0, new string[0], new string[0], new int[0], new bool[0] });
        }

        /// <summary>Name of the model's current present units (read-only).</summary>
        public string PresentUnitsName()
        {
            try { return Call(_sapModel, "GetPresentUnits").Return?.ToString() ?? "(unknown)"; }
            catch (Exception ex) { return "(could not read: " + ex.Message + ")"; }
        }

        /// <summary>Switches the model to N-mm-C so values match the Excel export (mm, MPa) and restores the user's units on Dispose.</summary>
        public IDisposable UnitsScope()
        {
            var unitsType = _api.GetType("ETABSv1.eUnits", true);
            var original = Call(_sapModel, "GetPresentUnits").Return;
            var target = Enum.Parse(unitsType, "N_mm_C");
            if (!original.Equals(target)) Check(Call(_sapModel, "SetPresentUnits", target), "SapModel.SetPresentUnits(N_mm_C)");
            return new Restore(() =>
            {
                if (!original.Equals(target)) Call(_sapModel, "SetPresentUnits", original);
            });
        }

        // ---- reflection helpers ------------------------------------------------------------

        private sealed class CallResult { public object Return; public object[] Args; public string Method; }

        private static CallResult Call(object target, string method, params object[] args)
        {
            var type = target.GetType();
            var m = type.GetMethod(method) ?? type.GetInterfaces().Select(i => i.GetMethod(method)).FirstOrDefault(x => x != null)
                ?? throw new MissingMethodException($"{target.GetType().Name}.{method} not found in this ETABS API version.");
            try
            {
                var ret = m.Invoke(target, args); // by-ref/out parameters are written back into args
                return new CallResult { Return = ret, Args = args, Method = method };
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException($"ETABS API call {method} failed: {ex.InnerException?.Message ?? ex.Message}", ex.InnerException ?? ex);
            }
        }

        private static void Check(CallResult r, string what)
        {
            if (r.Return is int code && code != 0) throw new InvalidOperationException($"{what} returned error code {code}.");
        }

        private static object Get(object target, string property)
        {
            var type = target.GetType();
            var p = type.GetProperty(property) ?? type.GetInterfaces().Select(i => i.GetProperty(property)).FirstOrDefault(x => x != null);
            return p?.GetValue(target, null);
        }

        private sealed class Restore : IDisposable
        {
            private readonly Action _action;
            public Restore(Action action) { _action = action; }
            public void Dispose() => _action();
        }
    }

    /// <summary>Raw output of one GetTableForDisplayArray call plus the field display names.</summary>
    public sealed class RawTableData
    {
        public string[] FieldKeys { get; set; }
        public IReadOnlyDictionary<string, string> FieldNames { get; set; }
        public int NumberRecords { get; set; }
        public string[] Data { get; set; }
    }
}
