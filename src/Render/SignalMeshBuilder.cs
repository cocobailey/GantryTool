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
        public static float HeadSize(float signalSize, float beamDepth)
        {
            return Mathf.Min(signalSize + Frame, beamDepth - 0.1f);
        }

        public static float HeadDepth => GantryMeshBuilder.BeamWidth + 2f * Proud;

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
            List<GantryLane> lanes, List<SignalOverride> overrides, float signalSize, Mesh mesh, bool blinkLayer = false)
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

            if (lanes == null || lanes.Count == 0)
            {
                return mesh;
            }

            Vector3 flat = end - start;
            flat.y = 0f;
            float span = flat.magnitude;
            Vector3 dir = span > 0.01f ? flat / span : Vector3.right;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);

            float size = HeadSize(signalSize, beamDepth);
            Rect housing = Inset(SignalTextures.TileRect(SignalTextures.HousingTile), 0.25f);

            for (int i = 0; i < lanes.Count; i++)
            {
                GantryLane lane = lanes[i];
                SignalSymbol minus, plus;
                SignalRules.Resolve(lanes, i, overrides, out minus, out plus);
                if (SignalRules.IsHidden(minus, plus))
                {
                    continue; // head removed by the player
                }

                Vector3 c = HeadCentre(start, end, beamBottomY, beamDepth, lane.Offset) - origin;
                Vector3 r = dir * (size * 0.5f);
                Vector3 u = Vector3.up * (size * 0.5f);
                Vector3 s = side * (HeadDepth * 0.5f);

                if (blinkLayer)
                {
                    Vector3 lift = side * BlinkLift;
                    if (Blinks(minus))
                    {
                        AddFace(c - s - lift, -side, r, u, SignalTextures.TileRect(SignalTextures.TileFor(minus)));
                    }

                    if (Blinks(plus))
                    {
                        AddFace(c + s + lift, side, r, u, SignalTextures.TileRect(SignalTextures.TileFor(plus)));
                    }

                    continue;
                }

                AddFace(c - s, -side, r, u, SignalTextures.TileRect(SignalTextures.TileFor(Blinks(minus) ? SignalSymbol.Blank : minus)));
                AddFace(c + s, side, r, u, SignalTextures.TileRect(SignalTextures.TileFor(Blinks(plus) ? SignalSymbol.Blank : plus)));

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
