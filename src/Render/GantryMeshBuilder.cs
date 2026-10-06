using System.Collections.Generic;
using GantryTool.Data;
using UnityEngine;

namespace GantryTool.Render
{
    /// <summary>
    /// Builds the gantry structure mesh: two box posts plus a box (4-chord Warren) truss beam.
    /// Vertices are relative to <see cref="Gantry.Origin"/> (midpoint of the two ground points),
    /// so the mesh is drawn with a pure translation matrix.
    /// </summary>
    public static class GantryMeshBuilder
    {
        public const float BeamWidth = 1.0f;      // truss thickness across the road direction
        private const float PostSize = 0.6f;      // post size along the span
        private const float PostSink = 0.5f;      // posts start below ground so slopes don't show a gap
        private const float ChordSize = 0.18f;
        private const float WebSize = 0.09f;

        private static readonly List<Vector3> Verts = new List<Vector3>();
        private static readonly List<Vector3> Normals = new List<Vector3>();
        private static readonly List<Vector4> Tangents = new List<Vector4>();
        private static readonly List<Vector2> Uvs = new List<Vector2>();
        private static readonly List<int> Tris = new List<int>();

        public static float RoadClearance => Settings.Current.RoadClearance; // beam underside above highest road

        /// <summary>
        /// Beam underside height (world y): post height above the higher ground point, raised if needed to
        /// clear the highest road crossed (<paramref name="roadTopY"/> = NaN when there is no road).
        /// </summary>
        public static float BeamBottomY(Vector3 start, Vector3 end, float postHeight, float roadTopY)
        {
            float y = Mathf.Max(start.y, end.y) + postHeight;
            if (!float.IsNaN(roadTopY))
            {
                // Heads are mounted inside the truss, so the beam underside is the lowest point.
                y = Mathf.Max(y, roadTopY + RoadClearance);
            }

            return y;
        }

        /// <summary>
        /// Builds (or rebuilds into <paramref name="mesh"/>) the structure mesh with the beam underside at
        /// world height <paramref name="bottomY"/>. Main thread only.
        /// Returns the mesh; <paramref name="origin"/> is where to draw it.
        /// </summary>
        public static Mesh Build(Vector3 start, Vector3 end, float bottomY, float beamDepth, Mesh mesh, out Vector3 origin)
        {
            Verts.Clear();
            Normals.Clear();
            Tangents.Clear();
            Uvs.Clear();
            Tris.Clear();

            origin = (start + end) * 0.5f;

            Vector3 flat = end - start;
            flat.y = 0f;
            float span = flat.magnitude;
            Vector3 dir = span > 0.01f ? flat / span : Vector3.right;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);

            float topY = bottomY + beamDepth;

            Vector3 s = start - origin;
            Vector3 e = end - origin;
            float bottom = bottomY - origin.y;
            float top = topY - origin.y;

            // Posts: from below ground to the top of the beam.
            float postDepth = BeamWidth + 0.2f;
            AddBox(new Vector3(s.x, s.y - PostSink, s.z), new Vector3(s.x, top + 0.15f, s.z), dir, PostSize, postDepth);
            AddBox(new Vector3(e.x, e.y - PostSink, e.z), new Vector3(e.x, top + 0.15f, e.z), dir, PostSize, postDepth);

            // Truss runs between the inner faces of the posts.
            Vector3 b0 = new Vector3(s.x, 0f, s.z) + dir * (PostSize * 0.5f);
            Vector3 b1 = new Vector3(e.x, 0f, e.z) - dir * (PostSize * 0.5f);
            float trussLen = Mathf.Max(0.1f, span - PostSize);
            float halfW = BeamWidth * 0.5f - ChordSize * 0.5f;
            float chordBottom = bottom + ChordSize * 0.5f;
            float chordTop = top - ChordSize * 0.5f;

            // Four chords: front/back x top/bottom.
            for (int f = -1; f <= 1; f += 2)
            {
                Vector3 off = side * (halfW * f);
                AddBar(b0 + off + Vector3.up * chordBottom, b1 + off + Vector3.up * chordBottom, ChordSize, ChordSize, side);
                AddBar(b0 + off + Vector3.up * chordTop, b1 + off + Vector3.up * chordTop, ChordSize, ChordSize, side);
            }

            int panels = Mathf.Max(1, Mathf.RoundToInt(trussLen / Mathf.Max(0.5f, beamDepth)));
            for (int i = 0; i <= panels; i++)
            {
                Vector3 p = Vector3.Lerp(b0, b1, (float)i / panels);
                for (int f = -1; f <= 1; f += 2)
                {
                    Vector3 off = side * (halfW * f);

                    // Vertical web member on each face.
                    AddBar(p + off + Vector3.up * chordBottom, p + off + Vector3.up * chordTop, WebSize, WebSize, side);

                    // Alternating diagonals make the Warren pattern.
                    if (i < panels)
                    {
                        Vector3 q = Vector3.Lerp(b0, b1, (float)(i + 1) / panels);
                        bool rising = (i & 1) == 0;
                        Vector3 from = p + off + Vector3.up * (rising ? chordBottom : chordTop);
                        Vector3 to = q + off + Vector3.up * (rising ? chordTop : chordBottom);
                        AddBar(from, to, WebSize, WebSize, side);
                    }
                }

                // Cross ties joining front and back faces, top and bottom.
                AddBar(p - side * halfW + Vector3.up * chordBottom, p + side * halfW + Vector3.up * chordBottom, WebSize, WebSize, Vector3.up);
                AddBar(p - side * halfW + Vector3.up * chordTop, p + side * halfW + Vector3.up * chordTop, WebSize, WebSize, Vector3.up);
            }

            if (mesh == null)
            {
                mesh = new Mesh { name = "GantryStructure" };
            }
            else
            {
                mesh.Clear();
            }

            mesh.SetVertices(Verts);
            mesh.SetNormals(Normals);
            mesh.SetTangents(Tangents);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Vertical-ish box from a to b, sized along <paramref name="dir"/> and across it.</summary>
        private static void AddBox(Vector3 a, Vector3 b, Vector3 dir, float sizeAlong, float sizeAcross)
        {
            AddBar(a, b, sizeAcross, sizeAlong, dir);
        }

        /// <summary>
        /// Rectangular bar from a to b. <paramref name="w"/> is measured along the axis perpendicular to
        /// both the bar and <paramref name="upHint"/>; <paramref name="h"/> along the remaining axis.
        /// </summary>
        private static void AddBar(Vector3 a, Vector3 b, float w, float h, Vector3 upHint)
        {
            Vector3 axis = b - a;
            if (axis.sqrMagnitude < 1e-6f)
            {
                return;
            }

            axis.Normalize();
            Vector3 right = Vector3.Cross(upHint, axis);
            if (right.sqrMagnitude < 1e-6f)
            {
                right = Vector3.Cross(Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.forward, axis);
            }

            right.Normalize();
            Vector3 up = Vector3.Cross(axis, right).normalized;
            Vector3 r = right * (w * 0.5f);
            Vector3 u = up * (h * 0.5f);

            // Four long sides.
            AddQuad(a - r - u, b - r - u, b - r + u, a - r + u, -right, axis);
            AddQuad(a + r + u, b + r + u, b + r - u, a + r - u, right, axis);
            AddQuad(a - r + u, b - r + u, b + r + u, a + r + u, up, axis);
            AddQuad(a + r - u, b + r - u, b - r - u, a - r - u, -up, axis);

            // End caps.
            AddQuad(a + r - u, a - r - u, a - r + u, a + r + u, -axis, -right);
            AddQuad(b - r - u, b + r - u, b + r + u, b - r + u, axis, right);
        }

        /// <summary>Quad with a flat normal; winding is fixed up so the front face matches the normal.</summary>
        private static void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal, Vector3 tangent)
        {
            int i = Verts.Count;
            Verts.Add(p0);
            Verts.Add(p1);
            Verts.Add(p2);
            Verts.Add(p3);

            var t = new Vector4(tangent.x, tangent.y, tangent.z, 1f);
            for (int k = 0; k < 4; k++)
            {
                Normals.Add(normal);
                Tangents.Add(t);
            }

            Uvs.Add(new Vector2(0f, 0f));
            Uvs.Add(new Vector2(1f, 0f));
            Uvs.Add(new Vector2(1f, 1f));
            Uvs.Add(new Vector2(0f, 1f));

            // Unity front faces are clockwise seen from the front, which is the side
            // Vector3.Cross(p1 - p0, p2 - p0) points to (same rule RecalculateNormals uses).
            Vector3 geomNormal = Vector3.Cross(p1 - p0, p2 - p0);
            if (Vector3.Dot(geomNormal, normal) >= 0f)
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
