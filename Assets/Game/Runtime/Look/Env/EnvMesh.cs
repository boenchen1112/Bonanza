// Procedural meshes for the environment kit (web render/env builds its set from three.js primitives; Unity has
// no torus, open cylinder arc, cone or octahedron built in, and CreatePrimitive drags physics into the player).
//
// MeshBuilder accumulates parts at build time (Load only: it allocates freely) and bakes them into one Mesh.
// MeshBatch is the per-frame half: N copies of one template, each with its own matrix and colour, written
// into one dynamic mesh (one draw call, like an InstancedMesh) from arrays allocated once.
//
// Everything here is Unity space: +y up, +z away from the camera (the web's -z).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bonanza.Look
{
    public sealed class MeshBuilder
    {
        public readonly List<Vector3> P = new List<Vector3>();
        public readonly List<Vector3> N = new List<Vector3>();
        public readonly List<Color> C = new List<Color>();   // LINEAR colour (rgb may exceed 1); the env shaders use it as is
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<int> T = new List<int>();
        // Optional extra channels (the crowd's per-figure data). Filled for every vertex once any is used.
        public List<Vector4> UV0x, UV1x, UV2x;

        public int Count => P.Count;

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (P.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(P);
            m.SetNormals(N);
            m.SetColors(C);
            if (UV0x != null) m.SetUVs(0, UV0x); else m.SetUVs(0, UV);
            if (UV1x != null) m.SetUVs(1, UV1x);
            if (UV2x != null) m.SetUVs(2, UV2x);
            m.SetTriangles(T, 0);
            m.RecalculateBounds();
            m.UploadMeshData(false);
            return m;
        }

        int V(Matrix4x4 m, Vector3 p, Vector3 n, Color c, Vector2 uv)
        {
            P.Add(m.MultiplyPoint3x4(p));
            N.Add(m.MultiplyVector(n).normalized);
            C.Add(c);
            UV.Add(uv);
            return P.Count - 1;
        }

        /// Adds a triangle wound so its front face agrees with the vertex normals (Unity: Cross(b-a, c-a) points
        /// out of the front face). Degenerate triangles (poles) are skipped.
        public void Tri(int a, int b, int c)
        {
            var g = Vector3.Cross(P[b] - P[a], P[c] - P[a]);
            if (g.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(g, N[a] + N[b] + N[c]) >= 0) { T.Add(a); T.Add(b); T.Add(c); }
            else { T.Add(a); T.Add(c); T.Add(b); }
        }

        void Quad(int a, int b, int c, int d, bool flip) { Tri(a, b, c); Tri(a, c, d); }

        /// A grid of (cols+1) x (rows+1) vertices from f(u, v) -> position, normal; quads wound so the normal faces out.
        public void Surface(int cols, int rows, System.Func<float, float, (Vector3 p, Vector3 n)> f, Matrix4x4 m, Color c, Vector2 uvScale, bool flip = false)
        {
            int start = P.Count;
            for (int j = 0; j <= rows; j++)
                for (int i = 0; i <= cols; i++)
                {
                    float u = i / (float)cols, v = j / (float)rows;
                    var (p, n) = f(u, v);
                    V(m, p, n, c, new Vector2(u * uvScale.x, v * uvScale.y));
                }
            int w = cols + 1;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    int a = start + j * w + i;
                    Quad(a, a + w, a + w + 1, a + 1, flip);
                }
        }

        /// Torus in the local XY plane (a ring facing -z, the camera), major radius R, tube r, swept over `arc`
        /// radians starting at angle a0 (0 = +x, counter-clockwise toward +y).
        public void Torus(float R, float r, int radial, int tubular, float arc, Matrix4x4 m, Color c, float a0 = 0)
        {
            Surface(tubular, radial, (u, v) =>
            {
                float a = a0 + u * arc, b = v * Mathf.PI * 2;
                var centre = new Vector3(Mathf.Cos(a) * R, Mathf.Sin(a) * R, 0);
                var n = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a) * Mathf.Cos(b), Mathf.Sin(b));
                return (centre + n * r, n);
            }, m, c, new Vector2(arc * R, 1), true);
        }

        /// Open (or capped-free) cylinder / cone frustum around +y, centred on y = 0, swept over [a0, a0 + arc]
        /// where angle 0 points at +z (away from the camera) and grows toward +x. Normals face out; `inward`
        /// flips them (a bowl seen from inside).
        public void Shell(float rTop, float rBot, float h, int seg, float a0, float arc, Matrix4x4 m, Color c, Vector2 uvScale, bool inward = false)
        {
            float slope = (rBot - rTop) / h;
            Surface(seg, 1, (u, v) =>
            {
                float a = a0 + u * arc;
                float r = Mathf.Lerp(rBot, rTop, v);
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var n = new Vector3(d.x, slope, d.z).normalized;
                return (d * r + Vector3.up * (v - 0.5f) * h, inward ? -n : n);
            }, m, c, uvScale, inward);
        }

        /// Flat disc in XZ facing +y, UVs planar over the disc (0..uvScale across the diameter).
        public void Disc(float r, int seg, Matrix4x4 m, Color c, float uvScale = 1)
        {
            int centre = V(m, Vector3.zero, Vector3.up, c, new Vector2(0.5f, 0.5f) * uvScale);
            int first = P.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2;
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                V(m, p, Vector3.up, c, new Vector2(0.5f + p.x / (2 * r), 0.5f + p.z / (2 * r)) * uvScale);
            }
            for (int i = 0; i < seg; i++) Tri(centre, first + i + 1, first + i);
        }

        /// Cone with its apex at the origin opening down -y to radius r at depth h; uv.y = 1 at the apex.
        public void Cone(float r, float h, int seg, Matrix4x4 m, Color c)
        {
            float k = r / h;
            Surface(seg, 4, (u, v) =>
            {
                float a = u * Mathf.PI * 2;
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                float y = -(1 - v) * h;
                var n = new Vector3(d.x, k, d.z).normalized;
                return (d * (-y * k) + Vector3.up * y, n);
            }, m, c, Vector2.one);
        }

        public void Sphere(float r, int wSeg, int hSeg, Matrix4x4 m, Color c, Vector3 scale)
        {
            Surface(wSeg, hSeg, (u, v) =>
            {
                float a = u * Mathf.PI * 2, b = v * Mathf.PI;
                var n = new Vector3(-Mathf.Cos(a) * Mathf.Sin(b), -Mathf.Cos(b), Mathf.Sin(a) * Mathf.Sin(b));
                var p = Vector3.Scale(n * r, scale);
                var nn = new Vector3(n.x / scale.x, n.y / scale.y, n.z / scale.z).normalized;
                return (p, nn);
            }, m, c, Vector2.one, true);
        }

        /// Capsule along +y: radius r, straight section `len` (centred on the origin).
        public void Capsule(float r, float len, int capSeg, int radial, Matrix4x4 m, Color c)
        {
            int rows = capSeg * 2 + 1;
            Surface(radial, rows, (u, v) =>
            {
                float a = u * Mathf.PI * 2;
                int row = Mathf.RoundToInt(v * rows);
                float y, ring, ny;
                if (row <= capSeg)
                {
                    float b = (row / (float)capSeg) * Mathf.PI * 0.5f;   // bottom pole -> equator
                    ring = Mathf.Sin(b); ny = -Mathf.Cos(b); y = -len / 2 + ny * r;
                }
                else
                {
                    float b = ((row - capSeg - 1) / (float)capSeg) * Mathf.PI * 0.5f;   // equator -> top pole
                    ring = Mathf.Cos(b); ny = Mathf.Sin(b); y = len / 2 + ny * r;
                }
                var n = new Vector3(-Mathf.Cos(a) * ring, ny, Mathf.Sin(a) * ring);
                return (new Vector3(n.x * r, y, n.z * r), n.normalized);
            }, m, c, Vector2.one, true);
        }

        /// Unit box (side 1, centred), flat faces.
        public void Box(Matrix4x4 m, Color c)
        {
            for (int f = 0; f < 6; f++)
            {
                var n = f switch { 0 => Vector3.right, 1 => Vector3.left, 2 => Vector3.up, 3 => Vector3.down, 4 => Vector3.forward, _ => Vector3.back };
                var t1 = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                var t2 = Vector3.Cross(n, t1);
                var o = n * 0.5f;
                int a = V(m, o - t1 * 0.5f - t2 * 0.5f, n, c, new Vector2(0, 0));
                int b = V(m, o + t1 * 0.5f - t2 * 0.5f, n, c, new Vector2(1, 0));
                int cc = V(m, o + t1 * 0.5f + t2 * 0.5f, n, c, new Vector2(1, 1));
                int d = V(m, o - t1 * 0.5f + t2 * 0.5f, n, c, new Vector2(0, 1));
                Quad(a, b, cc, d, false);
            }
        }

        /// Octahedron of radius 1, flat-shaded (each face its own normal).
        public void Octahedron(Matrix4x4 m, Color c)
        {
            var v = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            int[] f = { 0, 2, 4, 0, 5, 2, 0, 3, 5, 0, 4, 3, 1, 4, 2, 1, 2, 5, 1, 5, 3, 1, 3, 4 };
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = v[f[i]], b = v[f[i + 1]], cc = v[f[i + 2]];
                var n = Vector3.Cross(b - a, cc - a).normalized;
                if (Vector3.Dot(n, a + b + cc) < 0) n = -n;
                int ia = V(m, a, n, c, Vector2.zero), ib = V(m, b, n, c, Vector2.right), ic = V(m, cc, n, c, Vector2.up);
                Tri(ia, ib, ic);
            }
        }

        /// A tube through a polyline (bunting lines, cables).
        public void Tube(Vector3[] pts, float r, int radial, Matrix4x4 m, Color c)
        {
            int n = pts.Length;
            Surface(radial, n - 1, (u, v) =>
            {
                int i = Mathf.Clamp(Mathf.RoundToInt(v * (n - 1)), 0, n - 1);
                var t = (pts[Mathf.Min(n - 1, i + 1)] - pts[Mathf.Max(0, i - 1)]).normalized;
                var side = Vector3.Cross(t, Vector3.up);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
                side.Normalize();
                var up = Vector3.Cross(side, t);
                float a = u * Mathf.PI * 2;
                var nn = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                return (pts[i] + nn * r, nn);
            }, m, c, Vector2.one);
        }

        /// One triangle, double-sided by material (bunting flag): pivot at the top edge's middle.
        public void Flag(float w, float h, Matrix4x4 m, Color c)
        {
            int a = V(m, new Vector3(-w, 0, 0), Vector3.back, c, new Vector2(0, 1));
            int b = V(m, new Vector3(w, 0, 0), Vector3.back, c, new Vector2(1, 1));
            int d = V(m, new Vector3(0, -h, 0), Vector3.back, c, new Vector2(0.5f, 0));
            Tri(a, b, d);
        }

        /// Flat quad in XZ facing +y (floor confetti).
        public void FloorQuad(float w, float d, Matrix4x4 m, Color c)
        {
            int a = V(m, new Vector3(-w / 2, 0, -d / 2), Vector3.up, c, new Vector2(0, 0));
            int b = V(m, new Vector3(-w / 2, 0, d / 2), Vector3.up, c, new Vector2(0, 1));
            int cc = V(m, new Vector3(w / 2, 0, d / 2), Vector3.up, c, new Vector2(1, 1));
            int e = V(m, new Vector3(w / 2, 0, -d / 2), Vector3.up, c, new Vector2(1, 0));
            Quad(a, b, cc, e, false);
        }
    }

    /// N copies of a template mesh in one dynamic mesh: set Matrix[i] and Color[i], then Flush() once a frame.
    /// Arrays are allocated once; Flush allocates nothing.
    public sealed class MeshBatch
    {
        public readonly Matrix4x4[] Matrix;
        public readonly Color[] Tint;   // linear, may exceed 1 (a beat pop brightens past white)
        public readonly Mesh Mesh;
        public readonly MeshRenderer Renderer;
        readonly Vector3[] _tp, _tn, _p, _n;
        readonly Color[] _tc, _c;
        readonly int _verts;

        public MeshBatch(string name, Transform parent, MeshBuilder template, int count, Material mat)
        {
            _tp = template.P.ToArray(); _tn = template.N.ToArray(); _tc = template.C.ToArray();
            _verts = _tp.Length;
            int total = _verts * count;
            _p = new Vector3[total]; _n = new Vector3[total]; _c = new Color[total];
            var uv = new Vector2[total];
            var tri = new int[template.T.Count * count];
            for (int i = 0; i < count; i++)
            {
                for (int v = 0; v < _verts; v++) uv[i * _verts + v] = template.UV[v];
                for (int t = 0; t < template.T.Count; t++) tri[i * template.T.Count + t] = template.T[t] + i * _verts;
            }
            Matrix = new Matrix4x4[count];
            Tint = new Color[count];
            for (int i = 0; i < count; i++) { Matrix[i] = Matrix4x4.identity; Tint[i] = Color.white; }
            Mesh = new Mesh { name = name };
            if (total > 65000) Mesh.indexFormat = IndexFormat.UInt32;
            Mesh.MarkDynamic();
            Mesh.vertices = _p; Mesh.normals = _n; Mesh.colors = _c; Mesh.uv = uv;
            Mesh.triangles = tri;
            Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 400f);
            Renderer = EnvKit.Renderer(name, parent, Mesh, mat);
        }

        public void Flush()
        {
            for (int i = 0; i < Matrix.Length; i++)
            {
                var m = Matrix[i];
                var t = Tint[i];
                int o = i * _verts;
                for (int v = 0; v < _verts; v++)
                {
                    _p[o + v] = m.MultiplyPoint3x4(_tp[v]);
                    _n[o + v] = m.MultiplyVector(_tn[v]);
                    var tc = _tc[v];
                    _c[o + v] = new Color(tc.r * t.r, tc.g * t.g, tc.b * t.b, 1);
                }
            }
            Mesh.SetVertices(_p);
            Mesh.SetNormals(_n);
            Mesh.SetColors(_c);
            Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 400f);
        }
    }
}
