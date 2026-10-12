using System.Collections.Generic;
using GantryTool.Detect;
using UnityEngine;

namespace GantryTool.Data
{
    /// <summary>
    /// One placed gantry. Start/End are the clicked ground points (y = terrain height at placement).
    /// Everything else (mesh, lanes, signals) is derived and rebuilt when <see cref="MeshDirty"/> is set.
    /// </summary>
    public sealed class Gantry
    {
        public int Id;
        public Vector3 Start;
        public Vector3 End;
        public float PostHeight;
        public float BeamDepth;

        // Per-gantry overrides (saved in the extension block). NaN / 0 = follow the global option, which is how
        // every gantry from 1.0 loads, so their look doesn't change.
        public float ClearanceOverride = float.NaN;
        public float SignalSizeOverride = float.NaN;
        public byte Style;

        public float Clearance => float.IsNaN(ClearanceOverride) ? Settings.Current.RoadClearance : ClearanceOverride;
        public float SignalSize => float.IsNaN(SignalSizeOverride) ? Settings.Current.SignalSize : SignalSizeOverride;

        // Derived render state; only touched on the main thread.
        public Mesh Mesh;
        public Mesh SignalMesh;
        public Mesh BlinkMesh;
        public Vector3 Origin;
        public Bounds Bounds;
        public bool MeshDirty = true;

        // Derived from the last lane scan (main thread).
        public readonly List<GantryLane> Lanes = new List<GantryLane>();
        public float RoadTopY = float.NaN;
        public float BeamBottomY;

        /// <summary>Manual per-lane symbol choices. Guarded by GantryManager.Lock (saved on the sim thread).</summary>
        public readonly List<SignalOverride> Overrides = new List<SignalOverride>();

        /// <summary>Only the signal faces changed (override edit); the truss and lanes are still valid.</summary>
        public bool SignalsDirty;

        public float Span
        {
            get
            {
                Vector3 d = End - Start;
                d.y = 0f;
                return d.magnitude;
            }
        }

        public override string ToString()
        {
            return "#" + Id + " span=" + Span.ToString("F1") + "m start=" + Start + " end=" + End;
        }
    }
}
