using System;
using System.Collections.Generic;
using GantryTool.Data;
using UnityEngine;

namespace GantryTool.Render
{
    /// <summary>
    /// Builds the gantry structure mesh: box posts (where they stand depends on its GantryShape) plus a box (4-chord
    /// Warren) truss beam.
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
        private const float BraceSize = 0.16f;    // strut from a lone post up to the beam
        private const float BraceDrop = 1.3f;     // how far below the beam it meets the post
        private const float BraceReach = 2.4f;    // how far along the beam it meets it

        // Tube looks. Sizes follow real structures: a monotube's pipe is well over half a metre across.
        private const int TubeSides = 14;
        private const float SingleTubeRadius = 0.3f;
        private const float CurvedTubeRadius = SingleTubeRadius;
        private const float DoubleTubeRadius = 0.13f;
        private const float BracingRadius = 0.05f;
        private const float RoundPostRadius = 0.26f;
        private const float CurveBend = 3.2f;
        private const float TieEvery = 7f;        // double tubes: uprights along the beam between the end bracing

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
            return BeamBottomY(start, end, postHeight, roadTopY, RoadClearance);
        }

        /// <summary>As above, with an explicit clearance (per-gantry setting).</summary>
        public static float BeamBottomY(Vector3 start, Vector3 end, float postHeight, float roadTopY, float clearance)
        {
            float y = Mathf.Max(start.y, end.y) + postHeight;
            if (!float.IsNaN(roadTopY))
            {
                // Heads are mounted inside the truss, so the beam underside is the lowest point.
                y = Mathf.Max(y, roadTopY + clearance);
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
            return Build(start, end, bottomY, beamDepth, mesh, out origin, GantryShape.Span, 0f);
        }

        /// <summary>The ground height where a post stands.</summary>
        public static float GroundAt(Vector3 p)
        {
            return TerrainManager.instance.SampleRawHeightSmooth(p);
        }

        /// <summary>As above for any kind of gantry; <paramref name="postAlong"/> is a median T's post, from the start.</summary>
        public static Mesh Build(Vector3 start, Vector3 end, float bottomY, float beamDepth, Mesh mesh, out Vector3 origin, byte type, float postAlong, byte look = GantryShape.BoxTruss)
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

            if (look != GantryShape.BoxTruss)
            {
                AddLook(look, type, s, e, start, dir, side, span, bottom, top, postAlong, origin);
                return Finish(mesh);
            }

            // Posts: from below ground to the top of the beam.
            float postDepth = BeamWidth + 0.2f;
            bool atStart = GantryShape.PostAtStart(type), atEnd = GantryShape.PostAtEnd(type);
            if (atStart)
            {
                AddBox(new Vector3(s.x, s.y - PostSink, s.z), new Vector3(s.x, top + 0.15f, s.z), dir, PostSize, postDepth);
            }

            if (atEnd)
            {
                AddBox(new Vector3(e.x, e.y - PostSink, e.z), new Vector3(e.x, top + 0.15f, e.z), dir, PostSize, postDepth);
            }

            // A lone post holds the beam out with struts.
            bool reach = span > 2f * BraceReach;
            if (type == GantryShape.CantileverStart && reach)
            {
                AddBrace(s, dir, bottom);
            }
            else if (type == GantryShape.CantileverEnd && reach)
            {
                AddBrace(e, -dir, bottom);
            }
            else if (type == GantryShape.MedianT)
            {
                Vector3 p = s + dir * postAlong;
                float ground = GroundAt(start + dir * postAlong) - origin.y;
                p.y = ground;
                AddBox(new Vector3(p.x, ground - PostSink, p.z), new Vector3(p.x, top + 0.15f, p.z), dir, PostSize, postDepth);
                if (postAlong > BraceReach)
                {
                    AddBrace(p, -dir, bottom);
                }

                if (span - postAlong > BraceReach)
                {
                    AddBrace(p, dir, bottom);
                }
            }

            // Truss runs between the inner faces of the posts; a free end stops level with its ground point.
            float startInset = atStart ? PostSize * 0.5f : 0f, endInset = atEnd ? PostSize * 0.5f : 0f;
            Vector3 b0 = new Vector3(s.x, 0f, s.z) + dir * startInset;
            Vector3 b1 = new Vector3(e.x, 0f, e.z) - dir * endInset;
            float trussLen = Mathf.Max(0.1f, span - startInset - endInset);
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

            return Finish(mesh);
        }

        private static Mesh Finish(Mesh mesh)
        {
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

        /// <summary>The structure in anything but the box truss.</summary>
        private static void AddLook(byte look, byte type, Vector3 s, Vector3 e, Vector3 start, Vector3 dir, Vector3 side, float span,
            float bottom, float top, float postAlong, Vector3 origin)
        {
            float mid = (bottom + top) * 0.5f;
            Vector3 flatS = new Vector3(s.x, 0f, s.z);
            Func<float, float, Vector3> at = (along, y) => flatS + dir * along + Vector3.up * y;

            if (look == GantryShape.CurvedTube && type != GantryShape.MedianT)
            {
                var line = new List<Vector3>();
                foreach (Vector2 p in GantryShape.CurvedPath(type, span, mid, s.y - PostSink, e.y - PostSink, CurveBend))
                {
                    line.Add(at(p.x, p.y));
                }

                AddPipe(line, CurvedTubeRadius, side);
                return;
            }

            // The beam.
            switch (look)
            {
                case GantryShape.SingleTube:
                    AddPipe(at(0f, mid), at(span, mid), SingleTubeRadius, side);
                    break;
                case GantryShape.CurvedTube:
                    AddPipe(at(0f, mid), at(span, mid), CurvedTubeRadius, side);
                    break;
                default:
                    AddChordsAndBracing(look, type, span, postAlong, bottom, top, at, side);
                    break;
            }

            // The posts.
            float postTop = look == GantryShape.SingleTube || look == GantryShape.CurvedTube ? mid + 0.35f : top + 0.15f;
            if (GantryShape.PostAtStart(type))
            {
                AddPipe(at(0f, s.y - PostSink), at(0f, postTop), RoundPostRadius, side);
            }

            if (GantryShape.PostAtEnd(type))
            {
                AddPipe(at(span, e.y - PostSink), at(span, postTop), RoundPostRadius, side);
            }

            if (type == GantryShape.MedianT)
            {
                float ground = GroundAt(start + dir * postAlong) - origin.y;
                AddPipe(at(postAlong, ground - PostSink), at(postAlong, postTop), RoundPostRadius, side);
            }
        }

        /// <summary>Two runs along the beam, one above the other, with the bracing that ties them together.</summary>
        private static void AddChordsAndBracing(byte look, byte type, float span, float postAlong, float bottom, float top, Func<float, float, Vector3> at, Vector3 side)
        {
            bool truss = look == GantryShape.FlatTruss;
            float radius = truss ? ChordSize * 0.6f : DoubleTubeRadius;
            float low = bottom + radius, high = top - radius;
            AddPipe(at(0f, low), at(span, low), radius, side);
            AddPipe(at(0f, high), at(span, high), radius, side);

            float depth = top - bottom;
            if (truss)
            {
                // Braced panel by panel.
                int panels = Mathf.Max(1, Mathf.RoundToInt(span / Mathf.Max(0.5f, depth)));
                for (int i = 0; i < panels; i++)
                {
                    float a = span * i / panels, b = span * (i + 1) / panels;
                    bool rising = (i & 1) == 0;
                    AddPipe(at(a, low), at(a, high), BracingRadius, side);
                    AddPipe(at(a, rising ? low : high), at(b, rising ? high : low), BracingRadius, side);
                }

                AddPipe(at(span, low), at(span, high), BracingRadius, side);
                return;
            }

            // Double tubes: braced beside each post, where the load comes in, with plain uprights between.
            float bay = Mathf.Min(depth * 1.6f, span * 0.25f);
            if (GantryShape.PostAtStart(type))
            {
                AddBay(0f, bay, low, high, at, side);
            }

            if (GantryShape.PostAtEnd(type))
            {
                AddBay(span, -bay, low, high, at, side);
            }

            if (type == GantryShape.MedianT)
            {
                AddBay(postAlong, Mathf.Min(bay, span - postAlong), low, high, at, side);
                AddBay(postAlong, -Mathf.Min(bay, postAlong), low, high, at, side);
            }

            int ties = Mathf.Max(1, Mathf.RoundToInt(span / TieEvery));
            for (int i = 0; i <= ties; i++)
            {
                float x = span * i / ties;
                if (x > bay && x < span - bay && !(type == GantryShape.MedianT && Mathf.Abs(x - postAlong) < bay))
                {
                    AddPipe(at(x, low), at(x, high), BracingRadius, side);
                }
                else if ((i == 0 && !GantryShape.PostAtStart(type)) || (i == ties && !GantryShape.PostAtEnd(type)))
                {
                    AddPipe(at(x, low), at(x, high), BracingRadius, side); // a free end
                }
            }
        }

        /// <summary>One braced bay beside a post: an upright, and a diagonal back to the post.</summary>
        private static void AddBay(float post, float length, float low, float high, Func<float, float, Vector3> at, Vector3 side)
        {
            AddPipe(at(post + length, low), at(post + length, high), BracingRadius, side);
            AddPipe(at(post, low), at(post + length, high), BracingRadius, side);
        }

        private static void AddPipe(Vector3 a, Vector3 b, float radius, Vector3 side)
        {
            AddPipe(new List<Vector3> { a, b }, radius, side);
        }

        /// <summary>A round pipe along a line that stays in one plane, <paramref name="side"/> being square to it. Ends are capped.</summary>
        private static void AddPipe(List<Vector3> line, float radius, Vector3 side)
        {
            int first = Verts.Count;
            for (int i = 0; i < line.Count; i++)
            {
                Vector3 along = (line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 up = Vector3.Cross(along, side).normalized;
                for (int k = 0; k <= TubeSides; k++)
                {
                    float a = 2f * Mathf.PI * k / TubeSides;
                    Vector3 n = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    Verts.Add(line[i] + n * radius);
                    Normals.Add(n);
                    Tangents.Add(new Vector4(along.x, along.y, along.z, 1f));
                    Uvs.Add(new Vector2((float)k / TubeSides, i));
                }
            }

            int ring = TubeSides + 1;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                for (int k = 0; k < TubeSides; k++)
                {
                    int p0 = first + i * ring + k, p1 = p0 + 1, p2 = p0 + ring + 1, p3 = p0 + ring;
                    AddFacing(p0, p1, p2);
                    AddFacing(p0, p2, p3);
                }
            }

            AddCap(line[0], (line[0] - line[1]).normalized, side, radius);
            AddCap(line[line.Count - 1], (line[line.Count - 1] - line[line.Count - 2]).normalized, side, radius);
        }

        private static void AddCap(Vector3 centre, Vector3 outward, Vector3 side, float radius)
        {
            Vector3 up = Vector3.Cross(outward, side).normalized;
            var t = new Vector4(side.x, side.y, side.z, 1f);
            int c = Verts.Count;
            Verts.Add(centre);
            Normals.Add(outward);
            Tangents.Add(t);
            Uvs.Add(Vector2.zero);
            for (int k = 0; k <= TubeSides; k++)
            {
                float a = 2f * Mathf.PI * k / TubeSides;
                Verts.Add(centre + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius);
                Normals.Add(outward);
                Tangents.Add(t);
                Uvs.Add(Vector2.zero);
            }

            for (int k = 0; k < TubeSides; k++)
            {
                AddFacing(c, c + 1 + k, c + 2 + k);
            }
        }

        /// <summary>A triangle wound to face the way its first corner's normal points (same rule as AddQuad).</summary>
        private static void AddFacing(int a, int b, int c)
        {
            Vector3 geom = Vector3.Cross(Verts[b] - Verts[a], Verts[c] - Verts[a]);
            Tris.Add(a);
            Tris.Add(Vector3.Dot(geom, Normals[a]) >= 0f ? b : c);
            Tris.Add(Vector3.Dot(geom, Normals[a]) >= 0f ? c : b);
        }

        /// <summary>Strut from below the beam on a post at <paramref name="post"/>, reaching up to the beam on each face.</summary>
        private static void AddBrace(Vector3 post, Vector3 toward, float bottom)
        {
            Vector3 side = new Vector3(-toward.z, 0f, toward.x);
            float halfW = BeamWidth * 0.5f - ChordSize * 0.5f;
            Vector3 foot = new Vector3(post.x, bottom - BraceDrop, post.z);
            Vector3 head = new Vector3(post.x, bottom + ChordSize * 0.5f, post.z) + toward * BraceReach;
            for (int f = -1; f <= 1; f += 2)
            {
                Vector3 off = side * (halfW * f);
                AddBar(foot + off, head + off, BraceSize, BraceSize, side);
            }
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
