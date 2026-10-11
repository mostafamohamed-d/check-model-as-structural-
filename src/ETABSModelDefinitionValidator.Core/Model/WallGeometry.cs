using System;
using System.Collections.Generic;
using System.Linq;

namespace ETABSModelDefinitionValidator.Core.Model
{
    /// <summary>Normalized row from "Area Assignments - Pier Labels": which pier an area object belongs to.</summary>
    public sealed class PierLabelAssignment : ModelObjectBase
    {
        public string Story { get; set; }
        /// <summary>The area object's label at its story (e.g. W1522).</summary>
        public string Label { get; set; }
        /// <summary>The area object's unique name - the name the ETABS API uses for it.</summary>
        public string UniqueName { get; set; }
        public string PierName { get; set; }
    }

    /// <summary>Normalized row from "Wall Object Connectivity" (or "Floor Object Connectivity"): the corner points of one area object.</summary>
    public sealed class WallObject : ModelObjectBase
    {
        public string UniqueName { get; set; }
        public string Story { get; set; }
        /// <summary>WallBay / FloorBay - the label ETABS shows on screen.</summary>
        public string Label { get; set; }
        public List<string> PointNames { get; } = new List<string>();

        public string Display => ObjectDisplay.For(Label, Story, UniqueName);
    }

    /// <summary>Normalized row from "Point Object Connectivity" (coordinates in mm).</summary>
    public sealed class PointObject : ModelObjectBase
    {
        public string UniqueName { get; set; }
        public string Story { get; set; }
        /// <summary>PointBay - the label ETABS shows on screen.</summary>
        public string Label { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public string Display => ObjectDisplay.For(Label, Story, UniqueName);

        public double PlanDistanceTo(PointObject other)
        {
            var dx = X - other.X;
            var dy = Y - other.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public double DistanceTo(PointObject other)
        {
            var dz = Z - other.Z;
            var plan = PlanDistanceTo(other);
            return Math.Sqrt(plan * plan + dz * dz);
        }
    }

    public enum FrameKind
    {
        Column,
        Beam
    }

    /// <summary>Normalized row from "Column Object Connectivity" or "Beam Object Connectivity". Which table
    /// the row came from is ETABS's own design orientation - a leaning column is still a column.</summary>
    public sealed class FrameObject : ModelObjectBase
    {
        public string UniqueName { get; set; }
        public string Story { get; set; }
        /// <summary>ColumnBay / BeamBay - the label ETABS shows on screen.</summary>
        public string Label { get; set; }
        public FrameKind Kind { get; set; }
        public string PointI { get; set; }
        public string PointJ { get; set; }

        public string Display => ObjectDisplay.For(Label, Story, UniqueName);
    }

    /// <summary>Normalized row from "Link Object Connectivity". Links carry only a name (ETABS has no
    /// separate label/unique name for them).</summary>
    public sealed class LinkObject : ModelObjectBase
    {
        public string Name { get; set; }
        public string Story { get; set; }
        public string Point1 { get; set; }
        public string Point2 { get; set; }

        /// <summary>The joint at the other end of the link, or null when <paramref name="point"/> is not an end of it.</summary>
        public string OtherEnd(string point)
        {
            if (string.Equals(Point1, point, StringComparison.Ordinal)) return Point2;
            if (string.Equals(Point2, point, StringComparison.Ordinal)) return Point1;
            return null;
        }
    }

    /// <summary>Normalized row from "Joint Assignments - Restraints".</summary>
    public sealed class JointRestraint : ModelObjectBase
    {
        private static readonly string[] Dofs = { "UX", "UY", "UZ", "RX", "RY", "RZ" };

        public string UniqueName { get; set; }
        public string Story { get; set; }
        public string Label { get; set; }
        /// <summary>UX, UY, UZ, RX, RY, RZ - true where the DOF is fixed.</summary>
        public bool[] Fixed { get; set; } = new bool[6];

        public bool IsRestrained => Fixed != null && Fixed.Any(f => f);

        public string Display => ObjectDisplay.For(Label, Story, UniqueName);

        public string RestraintText() => string.Join("+", Dofs.Where((d, i) => Fixed != null && i < Fixed.Length && Fixed[i]));
    }

    /// <summary>Normalized row from "Area Load Assignments - Uniform Load Sets".</summary>
    public sealed class AreaLoadSetAssignment : ModelObjectBase
    {
        public string UniqueName { get; set; }
        public string Story { get; set; }
        public string Label { get; set; }
        public string LoadSet { get; set; }
    }

    /// <summary>
    /// Direction code used by the CSI load APIs (GetLoadDistributed / GetLoadUniform): 1..3 local
    /// axes, 4..6 global X/Y/Z, 7..9 projected X/Y/Z, 10 gravity, 11 projected gravity.
    /// </summary>
    public static class LoadDirection
    {
        public const int Gravity = 10;
        public const int ProjectedGravity = 11;

        /// <summary>Only the two dedicated gravity codes count - a hand-signed global-Z load does not
        /// follow the model's gravity definition, so it is not a gravity load here.</summary>
        public static bool IsGravity(int dir) => dir == Gravity || dir == ProjectedGravity;

        public static string Describe(int dir)
        {
            switch (dir)
            {
                case 1: return "Local 1";
                case 2: return "Local 2";
                case 3: return "Local 3";
                case 4: return "Global X";
                case 5: return "Global Y";
                case 6: return "Global Z";
                case 7: return "Projected X";
                case 8: return "Projected Y";
                case 9: return "Projected Z";
                case Gravity: return "Gravity";
                case ProjectedGravity: return "Projected Gravity";
                default: return $"Dir code {dir}";
            }
        }
    }

    /// <summary>One distributed load on a frame object (read live from FrameObj.GetLoadDistributed; N and mm).</summary>
    public sealed class FrameDistributedLoad : ModelObjectBase
    {
        public string FrameName { get; set; }
        public string LoadPattern { get; set; }
        /// <summary>1 = force per unit length, 2 = moment per unit length.</summary>
        public int LoadType { get; set; }
        public string CoordinateSystem { get; set; }
        public int Direction { get; set; }
        public double Value1 { get; set; }
        public double Value2 { get; set; }
    }

    /// <summary>One uniform load on an area object (read live from AreaObj.GetLoadUniform; N and mm).</summary>
    public sealed class AreaUniformLoad : ModelObjectBase
    {
        public string AreaName { get; set; }
        public string LoadPattern { get; set; }
        public string CoordinateSystem { get; set; }
        public int Direction { get; set; }
        public double Value { get; set; }
    }

    /// <summary>One way of naming an object in messages - the way ETABS labels it, with the unique
    /// name alongside because that is what Select > Object Unique Name needs.</summary>
    public static class ObjectDisplay
    {
        public static string For(string label, string story, string uniqueName)
        {
            if (string.IsNullOrWhiteSpace(label)) return $"unique {uniqueName}";
            if (string.IsNullOrWhiteSpace(story)) return $"{label} (unique {uniqueName})";
            return $"{label} @ {story} (unique {uniqueName})";
        }
    }
}
