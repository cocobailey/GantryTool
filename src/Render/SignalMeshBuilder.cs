using System.Collections.Generic;
using GantryTool.Data;
using GantryTool.Detect;
using UnityEngine;

namespace GantryTool.Render
{
    /// <summary>
    /// Builds one mesh holding every signal head of a gantry. Heads are mounted in the truss like real
    /// lane control signals: centred over their lane, vertically centred on the beam, with both faces
    /// standing just proud of the truss sides. The face whose normal is -side is seen by traffic heading
    /// +side ("Minus" face), the other is the "Plus" face. What each face shows comes from
    /// <see cref="SignalRules.Resolve"/>.
    /// </summary>
    public static class SignalMeshBuilder
    {
        public static float DefaultSignalSize => Settings.Current.SignalSize; // LED face edge length (m)
        public const float Frame = 0.2f;              // housing is this much larger than the face
        public const float Proud = 0.04f;             // faces stand this far outside the truss sides
        private const float BlinkLift = 0.01f;        // blink layer sits this far in front of its blank face
        private const float BeaconScale = 0.26f;      // beacon box edge, as a fraction of the head size
        private const float RampBeaconAlong = 0.32f;  // ramp sign beacons: centre offset from the middle, fraction of size

        /// <summary>Symbols drawn in the separate blink layer (shown/hidden by the renderer).</summary>
        public static bool Blinks(SignalSymbol s)
        {
            return s == SignalSymbol.MergeLeft || s == SignalSymbol.MergeRight;
        }

        private static readonly List<Vector3> Verts = new List<Vector3>();
        private static readonly List<Vector3> Normals = new List<Vector3>();
        private static readonly List<Vector4> Tangents = new List<Vector4>();
        private static readonly List<Vector2> Uvs = new List<Vector2>();
        private static readonly List<int> Tris = new List<int>();

        /// <summary>Head edge length for a beam: the face size, shrunk if the truss is too shallow.</summary>
        /// <summary>
        /// Where a head sits along the beam (metres from the start post) and its box size: centred over its lane
        /// plus any nudge, sized from its own override or the gantry's size, kept inside the span.
        /// </summary>
        public static void HeadLayout(List<GantryLane> lanes, int laneIndex, List<SignalOverride> overrides,
            float gantrySignalSize, float beamDepth, float span, out float along, out float size)
        {
            float nudge = 0f, own = 0f;
            int idx = SignalRules.FindOverride(lanes, laneIndex, overrides);
            if (idx >= 0)
            {
                nudge = overrides[idx].Nudge;
                own = overrides[idx].Size;
            }

            // A head's own size is the player's call and may exceed the truss; the gantry-wide size keeps its
            // truss limit so existing gantries look the same.
            size = own > 0f ? Mathf.Max(0.3f, own + Frame) : HeadSize(gantrySignalSize, beamDepth);
            float half = size * 0.5f;
            along = span > size ? Mathf.Clamp(lanes[laneIndex].Offset + nudge, half, span - half) : span * 0.5f;
        }

        /// <summary>Where an extra head sits along the beam and its box size (see <see cref="ExtraHead"/>).</summary>
        public static void ExtraLayout(List<GantryLane> lanes, ExtraHead h, float gantrySignalSize, float beamDepth, float span,
            out float along, out float size)
        {
            size = h.Size > 0f ? Mathf.Max(0.3f, h.Size + Frame) : HeadSize(gantrySignalSize, beamDepth);
            int lane = SignalRules.LinkedLane(lanes, h);
            float target = lane >= 0 ? lanes[lane].Offset + h.Nudge : h.Along;
            float half = size * 0.5f;
            along = span > size ? Mathf.Clamp(target, half, span - half) : span * 0.5f;
        }

        /// <summary>Layout of any head (<see cref="HeadRef"/> numbering).</summary>
        public static void HeadLayout(List<GantryLane> lanes, List<SignalOverride> overrides, List<ExtraHead> extras, int head,
            float gantrySignalSize, float beamDepth, float span, out float along, out float size)
        {
            if (!HeadRef.IsExtra(head))
            {
                HeadLayout(lanes, head, overrides, gantrySignalSize, beamDepth, span, out along, out size);
                return;
            }

            int i = extras != null ? HeadRef.IndexOf(extras, HeadRef.ExtraId(head)) : -1;
            if (i < 0)
            {
                size = HeadSize(gantrySignalSize, beamDepth);
                along = span * 0.5f;
                return;
            }

            ExtraLayout(lanes, extras[i], gantrySignalSize, beamDepth, span, out along, out size);
        }

        /// <summary>The road speed (km/h) under a head, for the Canadian beacon; 0 for an unlinked extra head.</summary>
        private static float HeadLaneKmh(List<GantryLane> lanes, List<ExtraHead> extras, int head)
        {
            int lane = head;
            if (HeadRef.IsExtra(head))
            {
                int i = extras != null ? HeadRef.IndexOf(extras, HeadRef.ExtraId(head)) : -1;
                lane = i >= 0 ? SignalRules.LinkedLane(lanes, extras[i]) : -1;
            }

            return lane >= 0 && lane < lanes.Count ? lanes[lane].SpeedKmh : 0f;
        }

        private static readonly List<int> Heads = new List<int>();

        public static float HeadSize(float signalSize, float beamDepth)
        {
            return Mathf.Min(signalSize + Frame, beamDepth - 0.1f);
        }

        public static float HeadDepth => GantryMeshBuilder.BeamWidth + 2f * Proud;

        /// <summary>
        /// Height of a head's centre. Boxes that fit are centred in the truss; taller ones stand on its bottom edge
        /// and rise above it, so they never reduce the road clearance.
        /// </summary>
        public static float HeadCentreY(float beamBottomY, float beamDepth, float size)
        {
            return Mathf.Max(beamBottomY + beamDepth * 0.5f, beamBottomY + 0.05f + size * 0.5f);
        }

        /// <summary>World-space centre of a head of <paramref name="size"/> at <paramref name="offset"/> along the beam.</summary>
        public static Vector3 HeadCentre(Vector3 start, Vector3 end, float beamBottomY, float beamDepth, float offset, float size)
        {
            Vector3 c = HeadCentre(start, end, beamBottomY, beamDepth, offset);
            c.y = HeadCentreY(beamBottomY, beamDepth, size);
            return c;
        }

        /// <summary>World-space centre of the head over a lane (main thread).</summary>
        public static Vector3 HeadCentre(Vector3 start, Vector3 end, float beamBottomY, float beamDepth, float offset)
        {
            Vector3 flat = end - start;
            flat.y = 0f;
            float span = flat.magnitude;
            Vector3 dir = span > 0.01f ? flat / span : Vector3.right;
            Vector3 c = new Vector3(start.x, 0f, start.z) + dir * offset;
            c.y = beamBottomY + beamDepth * 0.5f;
            return c;
        }

        /// <summary>
        /// Builds (or clears and rebuilds into <paramref name="mesh"/>) the signal mesh. Vertices are relative
        /// to <paramref name="origin"/>, the same origin as the structure mesh. Main thread only.
        /// With <paramref name="blinkLayer"/> false: every head, with blinking symbols drawn as Blank.
        /// With it true: only the blinking faces, lifted slightly in front, no housings.
        /// </summary>
        public static Mesh Build(Vector3 start, Vector3 end, Vector3 origin, float beamBottomY, float beamDepth,
            List<GantryLane> lanes, List<SignalOverride> overrides, List<ExtraHead> extras, float signalSize, Mesh mesh,
            SignAtlas atlas, bool blinkLayer = false)
        {
            Verts.Clear();
            Normals.Clear();
            Tangents.Clear();
            Uvs.Clear();
            Tris.Clear();

            if (mesh == null)
            {
                mesh = new Mesh { name = "GantrySignals" };
            }
            else
            {
                mesh.Clear();
            }

            if (lanes == null)
            {
                return mesh;
            }

            Vector3 flat = end - start;
            flat.y = 0f;
            float span = flat.magnitude;
            Vector3 dir = span > 0.01f ? flat / span : Vector3.right;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);

            Rect housing = Inset(atlas.TileRect(SignAtlas.HousingTile), 0.25f);

            // Lane heads, then the heads the player added (those stay even with no road underneath).
            HeadRef.All(lanes, extras, Heads);
            foreach (int head in Heads)
            {
                FaceSign minus, plus;
                SignalRules.ResolveHead(lanes, overrides, extras, head, out minus, out plus, atlas.Style);
                if (SignalRules.IsHidden(minus.Symbol, plus.Symbol))
                {
                    continue; // head removed by the player
                }

                float along, size;
                HeadLayout(lanes, overrides, extras, head, signalSize, beamDepth, span, out along, out size);
                float laneKmh = HeadLaneKmh(lanes, extras, head);
                Vector3 c = HeadCentre(start, end, beamBottomY, beamDepth, along, size) - origin;
                Vector3 r = dir * (size * 0.5f);
                Vector3 u = Vector3.up * (size * 0.5f);
                Vector3 s = side * (HeadDepth * 0.5f);

                if (blinkLayer)
                {
                    Vector3 lift = side * BlinkLift;
                    if (Blinks(minus.Symbol))
                    {
                        AddFace(c - s - lift, -side, r, u, atlas.TileRect(atlas.TileOf(minus)));
                    }

                    if (Blinks(plus.Symbol))
                    {
                        AddFace(c + s + lift, side, r, u, atlas.TileRect(atlas.TileOf(plus)));
                    }

                    if (Lowered(minus, laneKmh, atlas))
                    {
                        AddBeacon(c, -side, dir, size, atlas, true);
                    }

                    if (Lowered(plus, laneKmh, atlas))
                    {
                        AddBeacon(c, side, dir, size, atlas, true);
                    }

                    continue;
                }

                if (HasBeacon(minus, atlas))
                {
                    AddBeacon(c, -side, dir, size, atlas, false);
                }

                if (HasBeacon(plus, atlas))
                {
                    AddBeacon(c, side, dir, size, atlas, false);
                }

                if (minus.Symbol == SignalSymbol.RampMetered)
                {
                    AddRampBeacons(c, -side, dir, size, atlas);
                }

                if (plus.Symbol == SignalSymbol.RampMetered)
                {
                    AddRampBeacons(c, side, dir, size, atlas);
                }

                FaceSign blank = new FaceSign(SignalSymbol.Blank, 0);
                AddFace(c - s, -side, r, u, atlas.TileRect(atlas.TileOf(Blinks(minus.Symbol) ? blank : minus)));
                AddFace(c + s, side, r, u, atlas.TileRect(atlas.TileOf(Blinks(plus.Symbol) ? blank : plus)));

                // Housing: top, bottom, two ends.
                AddFace(c + u, Vector3.up, r, s, housing);
                AddFace(c - u, Vector3.down, r, s, housing);
                AddFace(c + r, dir, s, u, housing);
                AddFace(c - r, -dir, s, u, housing);
            }

            mesh.SetVertices(Verts);
            mesh.SetNormals(Normals);
            mesh.SetTangents(Tangents);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Speed faces in a style with beacons (Canada) get a beacon box on top.</summary>
        private static bool HasBeacon(FaceSign face, SignAtlas atlas)
        {
            return SignStyles.HasBeacon(atlas.Style) && face.Symbol == SignalSymbol.SpeedLimit;
        }

        /// <summary>
        /// Lit lenses of every linked ramp metering face, one mesh per face: submesh 0 = one beacon, 1 = the other, so the renderer can wig-wag them while the face's meter is metering. Replaces the
        /// meshes in <paramref name="beacons"/>. Main thread only.
        /// </summary>
        public static void BuildRampBeacons(Vector3 start, Vector3 end, Vector3 origin, float beamBottomY, float beamDepth,
            List<GantryLane> lanes, List<SignalOverride> overrides, List<ExtraHead> extras, float signalSize, SignAtlas atlas,
            List<RampBeacon> beacons)
        {
            for (int i = 0; i < beacons.Count; i++)
            {
                Object.Destroy(beacons[i].Mesh);
            }

            beacons.Clear();
            if (lanes == null)
            {
                return;
            }

            Vector3 flat = end - start;
            flat.y = 0f;
            float span = flat.magnitude;
            Vector3 dir = span > 0.01f ? flat / span : Vector3.right;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);

            HeadRef.All(lanes, extras, Heads);
            foreach (int head in Heads)
            {
                FaceSign minus, plus;
                SignalRules.ResolveHead(lanes, overrides, extras, head, out minus, out plus, atlas.Style);
                if (SignalRules.IsHidden(minus.Symbol, plus.Symbol))
                {
                    continue;
                }

                float along, size;
                HeadLayout(lanes, overrides, extras, head, signalSize, beamDepth, span, out along, out size);
                Vector3 c = HeadCentre(start, end, beamBottomY, beamDepth, along, size) - origin;
                if (minus.Symbol == SignalSymbol.RampMetered && minus.Value != 0)
                {
                    beacons.Add(new RampBeacon { Node = minus.Value, Mesh = BuildRampLenses(c, -side, dir, size, atlas) });
                }

                if (plus.Symbol == SignalSymbol.RampMetered && plus.Value != 0)
                {
                    beacons.Add(new RampBeacon { Node = plus.Value, Mesh = BuildRampLenses(c, side, dir, size, atlas) });
                }
            }
        }

        private static Mesh BuildRampLenses(Vector3 headCentre, Vector3 normal, Vector3 dir, float size, SignAtlas atlas)
        {
            Verts.Clear();
            Normals.Clear();
            Tangents.Clear();
            Uvs.Clear();
            Tris.Clear();

            AddBeacon(headCentre + dir * (size * RampBeaconAlong), normal, dir, size, atlas, true);
            int split = Tris.Count;
            AddBeacon(headCentre - dir * (size * RampBeaconAlong), normal, dir, size, atlas, true);

            var mesh = new Mesh { name = "GantryRampBeacons" };
            mesh.SetVertices(Verts);
            mesh.SetNormals(Normals);
            mesh.SetTangents(Tangents);
            mesh.SetUVs(0, Uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(Tris.GetRange(0, split), 0);
            mesh.SetTriangles(Tris.GetRange(split, Tris.Count - split), 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The ramp sign's two beacon boxes on top of the head (static layer: housings and unlit lenses).</summary>
        private static void AddRampBeacons(Vector3 headCentre, Vector3 normal, Vector3 dir, float size, SignAtlas atlas)
        {
            AddBeacon(headCentre + dir * (size * RampBeaconAlong), normal, dir, size, atlas, false);
            AddBeacon(headCentre - dir * (size * RampBeaconAlong), normal, dir, size, atlas, false);
        }

        /// <summary>The beacon flashes while the sign shows less than the road's own limit.</summary>
        private static bool Lowered(FaceSign face, float laneKmh, SignAtlas atlas)
        {
            if (!HasBeacon(face, atlas) || laneKmh <= 0f)
            {
                return false;
            }

            return SpeedUnits.Display(face.Value, atlas.Mph) < SpeedUnits.Display(Mathf.RoundToInt(laneKmh), atlas.Mph);
        }

        /// <summary>
        /// A small beacon box sitting on top of the head, on the half facing <paramref name="normal"/>. Static layer:
        /// housing and an unlit lens. Blink layer: only the lit lens, just in front.
        /// </summary>
        private static void AddBeacon(Vector3 headCentre, Vector3 normal, Vector3 dir, float size, SignAtlas atlas, bool lit)
        {
            float edge = size * BeaconScale;
            float depth = HeadDepth * 0.5f;
            Vector3 c = headCentre + Vector3.up * (size * 0.5f + edge * 0.5f) + normal * (depth * 0.5f);
            Vector3 r = dir * (edge * 0.5f);
            Vector3 u = Vector3.up * (edge * 0.5f);
            Vector3 s = normal * (depth * 0.5f);

            if (lit)
            {
                AddFace(c + s + normal * BlinkLift, normal, r, u, atlas.TileRect(atlas.LampOnTile));
                return;
            }

            Rect housing = Inset(atlas.TileRect(SignAtlas.HousingTile), 0.25f);
            AddFace(c + s, normal, r, u, atlas.TileRect(atlas.LampOffTile));
            AddFace(c - s, -normal, r, u, housing);
            AddFace(c + u, Vector3.up, r, s, housing);
            AddFace(c + r, dir, s, u, housing);
            AddFace(c - r, -dir, s, u, housing);
        }

        private static Rect Inset(Rect rect, float fraction)
        {
            float dx = rect.width * fraction;
            float dy = rect.height * fraction;
            return new Rect(rect.x + dx, rect.y + dy, rect.width - 2f * dx, rect.height - 2f * dy);
        }

        /// <summary>
        /// Face centred at <paramref name="centre"/> facing <paramref name="normal"/>, spanning ±a × ±b
        /// (both perpendicular to the normal). The texture reads upright and unmirrored to a viewer looking
        /// at the face: u runs along the viewer's right, v along world up (or along b for horizontal faces).
        /// </summary>
        private static void AddFace(Vector3 centre, Vector3 normal, Vector3 a, Vector3 b, Rect uv)
        {
            // Viewer looks along -normal; Unity's right = Cross(up, forward).
            Vector3 upRef = Mathf.Abs(normal.y) > 0.9f ? b.normalized : Vector3.up;
            Vector3 right = Vector3.Cross(upRef, -normal).normalized;
            Vector3 up = Vector3.Cross(-normal, right).normalized;

            float halfRight = Mathf.Abs(Vector3.Dot(a, right)) + Mathf.Abs(Vector3.Dot(b, right));
            float halfUp = Mathf.Abs(Vector3.Dot(a, up)) + Mathf.Abs(Vector3.Dot(b, up));
            Vector3 rr = right * halfRight;
            Vector3 uu = up * halfUp;

            int i = Verts.Count;
            Verts.Add(centre - rr - uu);
            Verts.Add(centre + rr - uu);
            Verts.Add(centre + rr + uu);
            Verts.Add(centre - rr + uu);

            var t = new Vector4(right.x, right.y, right.z, 1f);
            for (int k = 0; k < 4; k++)
            {
                Normals.Add(normal);
                Tangents.Add(t);
            }

            Uvs.Add(new Vector2(uv.xMin, uv.yMin));
            Uvs.Add(new Vector2(uv.xMax, uv.yMin));
            Uvs.Add(new Vector2(uv.xMax, uv.yMax));
            Uvs.Add(new Vector2(uv.xMin, uv.yMax));

            // Clockwise as seen from the front (same rule as GantryMeshBuilder.AddQuad).
            Vector3 geom = Vector3.Cross(Verts[i + 1] - Verts[i], Verts[i + 2] - Verts[i]);
            if (Vector3.Dot(geom, normal) >= 0f)
            {
                Tris.Add(i); Tris.Add(i + 1); Tris.Add(i + 2);
                Tris.Add(i); Tris.Add(i + 2); Tris.Add(i + 3);
            }
            else
            {
                Tris.Add(i); Tris.Add(i + 2); Tris.Add(i + 1);
                Tris.Add(i); Tris.Add(i + 3); Tris.Add(i + 2);
            }
        }
    }
}
