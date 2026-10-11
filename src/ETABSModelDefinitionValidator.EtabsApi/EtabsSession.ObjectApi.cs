using System;
using System.Collections.Generic;
using System.Linq;
using ETABSModelDefinitionValidator.Core.Model;
using ETABSModelDefinitionValidator.Core.Validation;

namespace ETABSModelDefinitionValidator.EtabsApi
{
    /// <summary>Result of writing one failure group into the model.</summary>
    public sealed class GroupWriteResult
    {
        public string GroupName { get; set; }
        public int Assigned { get; set; }
        public int Cleared { get; set; }
        public int NotFound { get; set; }

        public override string ToString()
        {
            var text = $"{GroupName}: {Assigned} object(s) assigned";
            if (Cleared > 0) text += $", {Cleared} old assignment(s) removed";
            if (NotFound > 0) text += $", {NotFound} not found in the model";
            return text;
        }
    }

    /// <summary>
    /// Object-API reads (bulk, one call each) for data the database tables cannot give, and the only
    /// write the tool makes: failure groups. Signatures as used by the pier-validation tool against
    /// ETABSv1 22; bound by reflection like the rest of the session.
    /// </summary>
    public sealed partial class EtabsSession
    {
        private const int GroupColorRed = 255;

        /// <summary>Exact joint coordinates (present units) from PointObj.GetAllPoints. Database tables
        /// round coordinates for display, which would hide the small offsets the plumb check looks for.</summary>
        public Dictionary<string, (double X, double Y, double Z)> ReadPointCoordinates()
        {
            var r = Call(Get(_sapModel, "PointObj"), "GetAllPoints", 0, new string[0], new double[0], new double[0], new double[0], "Global");
            Check(r, "PointObj.GetAllPoints");
            var names = (string[])r.Args[1] ?? new string[0];
            var x = (double[])r.Args[2]; var y = (double[])r.Args[3]; var z = (double[])r.Args[4];
            var map = new Dictionary<string, (double, double, double)>(StringComparer.Ordinal);
            for (var i = 0; i < Math.Min((int)r.Args[0], names.Length); i++) map[names[i]] = (x[i], y[i], z[i]);
            return map;
        }

        /// <summary>Every frame distributed load in the model (group "All"), present units.</summary>
        public List<FrameDistributedLoad> ReadFrameDistributedLoads()
        {
            var r = Call(Get(_sapModel, "FrameObj"), "GetLoadDistributed", "All", 0, new string[0], new string[0], new int[0], new string[0], new int[0],
                new double[0], new double[0], new double[0], new double[0], new double[0], new double[0], ItemType("Group"));
            Check(r, "FrameObj.GetLoadDistributed(All)");
            var names = (string[])r.Args[2] ?? new string[0];
            var list = new List<FrameDistributedLoad>();
            for (var i = 0; i < Math.Min((int)r.Args[1], names.Length); i++)
            {
                list.Add(new FrameDistributedLoad
                {
                    FrameName = names[i],
                    LoadPattern = At<string>(r.Args[3], i),
                    LoadType = At<int>(r.Args[4], i),
                    CoordinateSystem = At<string>(r.Args[5], i),
                    Direction = At<int>(r.Args[6], i),
                    Value1 = At<double>(r.Args[11], i),
                    Value2 = At<double>(r.Args[12], i)
                });
            }
            return list;
        }

        /// <summary>Every area uniform load in the model (group "All"), present units.</summary>
        public List<AreaUniformLoad> ReadAreaUniformLoads()
        {
            var r = Call(Get(_sapModel, "AreaObj"), "GetLoadUniform", "All", 0, new string[0], new string[0], new string[0], new int[0], new double[0], ItemType("Group"));
            Check(r, "AreaObj.GetLoadUniform(All)");
            var names = (string[])r.Args[2] ?? new string[0];
            var list = new List<AreaUniformLoad>();
            for (var i = 0; i < Math.Min((int)r.Args[1], names.Length); i++)
            {
                list.Add(new AreaUniformLoad
                {
                    AreaName = names[i],
                    LoadPattern = At<string>(r.Args[3], i),
                    CoordinateSystem = At<string>(r.Args[4], i),
                    Direction = At<int>(r.Args[5], i),
                    Value = At<double>(r.Args[6], i)
                });
            }
            return list;
        }

        /// <summary>
        /// Writes each check's failing objects into its group (red), after emptying the group, so a
        /// group left in the model never shows an earlier run's failures. A group whose check now has
        /// no failures is emptied if it exists and not created otherwise. THIS MODIFIES THE MODEL.
        /// </summary>
        public List<GroupWriteResult> WriteFailureGroups(IReadOnlyDictionary<string, EtabsObjectRefs> objectsByGroup)
        {
            var groupDef = Get(_sapModel, "GroupDef");
            var list = Call(groupDef, "GetNameList", 0, new string[0]);
            var existing = new HashSet<string>((string[])list.Args[1] ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var objects = ItemType("Objects");
            var results = new List<GroupWriteResult>();

            foreach (var entry in objectsByGroup)
            {
                var refs = entry.Value ?? new EtabsObjectRefs();
                var exists = existing.Contains(entry.Key);
                if (refs.Count == 0 && !exists) continue;

                var result = new GroupWriteResult { GroupName = entry.Key };
                if (exists) result.Cleared = ClearGroup(groupDef, entry.Key, objects);
                else DefineGroup(groupDef, entry.Key);

                Assign("PointObj", refs.Points, entry.Key, objects, result);
                Assign("FrameObj", refs.Frames, entry.Key, objects, result);
                Assign("AreaObj", refs.Areas, entry.Key, objects, result);
                Assign("LinkObj", refs.Links, entry.Key, objects, result);
                results.Add(result);
            }

            try { Call(Get(_sapModel, "View"), "RefreshView", 0, false); } catch (Exception) { /* cosmetic only */ }
            return results;
        }

        private void DefineGroup(object groupDef, string name)
        {
            // SetGroup has a long tail of optional flags; pass their declared defaults, colour red.
            var m = FindMethod(groupDef, "SetGroup") ?? throw new MissingMethodException("GroupDef.SetGroup not found in this ETABS API version.");
            var ps = m.GetParameters();
            var args = new object[ps.Length];
            args[0] = name;
            for (var i = 1; i < ps.Length; i++)
                args[i] = i == 1 && ps[i].ParameterType == typeof(int) ? GroupColorRed
                    : ps[i].HasDefaultValue ? ps[i].DefaultValue
                    : ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
            Check(Call(groupDef, "SetGroup", args), $"GroupDef.SetGroup('{name}')");
        }

        /// <summary>Removes every current assignment of the group, object by object (redefining a group does not clear it).</summary>
        private int ClearGroup(object groupDef, string name, object objects)
        {
            var r = Call(groupDef, "GetAssignments", name, 0, new int[0], new string[0]);
            if (r.Return is int code && code != 0) return 0;
            var types = (int[])r.Args[2] ?? new int[0];
            var names = (string[])r.Args[3] ?? new string[0];
            var cleared = 0;
            for (var i = 0; i < Math.Min(types.Length, names.Length); i++)
            {
                // CSI object type codes: 1 point, 2 frame, 5 area, 7 link.
                var api = types[i] == 1 ? "PointObj" : types[i] == 2 ? "FrameObj" : types[i] == 5 ? "AreaObj" : types[i] == 7 ? "LinkObj" : null;
                if (api != null && SetGroupAssign(api, names[i], name, true, objects) == 0) cleared++;
            }
            return cleared;
        }

        private void Assign(string api, IEnumerable<string> names, string group, object objects, GroupWriteResult result)
        {
            foreach (var n in names.Distinct(StringComparer.Ordinal))
            {
                if (SetGroupAssign(api, n, group, false, objects) == 0) result.Assigned++;
                else result.NotFound++;
            }
        }

        private int SetGroupAssign(string api, string objectName, string group, bool remove, object objects)
        {
            try
            {
                var r = Call(Get(_sapModel, api), "SetGroupAssign", objectName, group, remove, objects);
                return r.Return is int code ? code : 0;
            }
            catch (InvalidOperationException) { return -1; }
        }

        private object ItemType(string name) => Enum.Parse(_api.GetType("ETABSv1.eItemType", true), name);

        private static T At<T>(object array, int i) => array is T[] a && i < a.Length ? a[i] : default(T);
    }
}
