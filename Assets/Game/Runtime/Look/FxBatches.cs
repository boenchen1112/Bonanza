// The pooled VFX families (web render/fx/pool.js, rings.js, trails.js).
//
// Each family is one dynamic mesh of CPU-built quads and one draw call. Every
// array is allocated once at construction; emitting and updating allocate
// nothing (a GC pause on a downbeat is a missed note). Vertices are written in
// world space, camera-facing quads use the camera's right/up each frame.
// No physics, no Shader.Find: materials come from Resources/Look.

using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bonanza.Look
{
    [StructLayout(LayoutKind.Sequential)]
    struct FxVert
    {
        public Vector3 P; public Color32 C; public Vector2 UV; public Vector4 Prm;
    }

    /// Mulberry32, seeded: replays are identical.
    sealed class FxRng
    {
        uint _s;
        public FxRng(uint seed) { _s = seed; }
        public float Next()
        {
            _s += 0x6D2B79F5u;
            uint t = _s;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return ((t ^ (t >> 14)) & 0xFFFFFFu) / 16777216f;
        }
    }

    /// A dynamic mesh of `max` quads (or a strip layout) and its renderer.
    sealed class QuadMesh
    {
        public readonly Mesh Mesh;
        public readonly FxVert[] V;
        public readonly MeshRenderer Renderer;
        public readonly int Max;
        int _lastIndexCount = -1;

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
        };

        const MeshUpdateFlags Fast = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices
            | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;

        /// `vertsPerItem` 4 for quads; `indices` is the full index list.
        public QuadMesh(string name, Transform parent, Material mat, int maxVerts, ushort[] indices, int order)
        {
            Max = maxVerts;
            V = new FxVert[maxVerts];
            Mesh = new Mesh { name = name };
            Mesh.MarkDynamic();
            Mesh.SetVertexBufferParams(maxVerts, Layout);
            Mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt16);
            Mesh.SetIndexBufferData(indices, 0, 0, indices.Length, Fast);
            Mesh.subMeshCount = 1;
            Mesh.SetSubMesh(0, new SubMeshDescriptor(0, 0, MeshTopology.Triangles), Fast);
            Mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = Mesh;
            Renderer = go.GetComponent<MeshRenderer>();
            Renderer.sharedMaterial = mat;
            Renderer.shadowCastingMode = ShadowCastingMode.Off;
            Renderer.receiveShadows = false;
            Renderer.lightProbeUsage = LightProbeUsage.Off;
            Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Renderer.sortingOrder = order;
            Renderer.enabled = false;
        }

        public static ushort[] QuadIndices(int quads)
        {
            var idx = new ushort[quads * 6];
            for (int q = 0; q < quads; q++)
            {
                int v = q * 4, o = q * 6;
                idx[o] = (ushort)v; idx[o + 1] = (ushort)(v + 1); idx[o + 2] = (ushort)(v + 2);
                idx[o + 3] = (ushort)v; idx[o + 4] = (ushort)(v + 2); idx[o + 5] = (ushort)(v + 3);
            }
            return idx;
        }

        public void Quad(int q, Vector3 p, Vector3 ax, Vector3 ay, Color32 c, Vector4 rect, Vector4 prm)
        {
            int i = q * 4;
            var hx = ax * 0.5f; var hy = ay * 0.5f;
            V[i].P = p - hx - hy; V[i].UV = new Vector2(rect.x, rect.y);
            V[i + 1].P = p + hx - hy; V[i + 1].UV = new Vector2(rect.x + rect.z, rect.y);
            V[i + 2].P = p + hx + hy; V[i + 2].UV = new Vector2(rect.x + rect.z, rect.y + rect.w);
            V[i + 3].P = p - hx + hy; V[i + 3].UV = new Vector2(rect.x, rect.y + rect.w);
            for (int k = 0; k < 4; k++) { V[i + k].C = c; V[i + k].Prm = prm; }
        }

        /// Upload the first `verts` vertices and draw `indexCount` indices (0 hides the family).
        public void Flush(int verts, int indexCount)
        {
            if (indexCount <= 0) { if (Renderer.enabled) Renderer.enabled = false; _lastIndexCount = 0; return; }
            Mesh.SetVertexBufferData(V, 0, 0, verts, 0, Fast);
            if (indexCount != _lastIndexCount)
            {
                Mesh.SetSubMesh(0, new SubMeshDescriptor(0, indexCount, MeshTopology.Triangles) { firstVertex = 0, vertexCount = verts, bounds = Mesh.bounds }, Fast);
                _lastIndexCount = indexCount;
            }
            if (!Renderer.enabled) Renderer.enabled = true;
        }

        public void Destroy() { if (Renderer) Object.Destroy(Renderer.gameObject); if (Mesh) Object.Destroy(Mesh); }
    }

    static class FxColor
    {
        public static Color32 C32(float r, float g, float b, float a) =>
            new Color32((byte)(Mathf.Clamp01(r) * 255f + 0.5f), (byte)(Mathf.Clamp01(g) * 255f + 0.5f), (byte)(Mathf.Clamp01(b) * 255f + 0.5f), (byte)(Mathf.Clamp01(a) * 255f + 0.5f));
        public static float EaseOutCubic(float t) { t = Mathf.Clamp01(t); return 1 - (1 - t) * (1 - t) * (1 - t); }
        public static float EaseOutQuint(float t) { t = Mathf.Clamp01(t); float u = 1 - t; return 1 - u * u * u * u * u; }
        public static float BackOut(float t, float s = 1.70158f) { t = Mathf.Clamp01(t) - 1; return t * t * ((s + 1) * t + s) + 1; }
    }

    /// Camera axes for this frame's billboards.
    struct CamAxes { public Vector3 R, U, F, P; }

    // ------------------------------------------------------------------ particles

    enum PMode { Billboard, Stretch, Tumble, Ground }
    enum PFade { QuadOut, Linear, Pop, Late, Hold }

    /// Emission parameters. One reused instance per batch: `Begin()` resets it to the
    /// family defaults, set what differs, then `Emit(n)`.
    sealed class EmitParams
    {
        public float X, Y, Z, Dx, Dy = 1, Dz, Cone = 1, Speed = 6, SpeedVar = 0.45f, Size = 0.15f, SizeVar = 0.35f, Aspect = 1;
        public float S0 = 1, S1 = 0.05f, Life = 0.5f, LifeVar = 0.3f, Grav = -9, Drag = 2.2f, Turb;
        public float R = 1, G = 1, B = 1, Alpha = 1, Spin, SpinVar = 8, Stretch, Tumble, Jitter;
        public PFade Fade = PFade.QuadOut;

        public void CopyFrom(EmitParams d)
        {
            X = d.X; Y = d.Y; Z = d.Z; Dx = d.Dx; Dy = d.Dy; Dz = d.Dz; Cone = d.Cone; Speed = d.Speed; SpeedVar = d.SpeedVar;
            Size = d.Size; SizeVar = d.SizeVar; Aspect = d.Aspect; S0 = d.S0; S1 = d.S1; Life = d.Life; LifeVar = d.LifeVar;
            Grav = d.Grav; Drag = d.Drag; Turb = d.Turb; R = d.R; G = d.G; B = d.B; Alpha = d.Alpha; Spin = d.Spin; SpinVar = d.SpinVar;
            Stretch = d.Stretch; Tumble = d.Tumble; Jitter = d.Jitter; Fade = d.Fade;
        }

        public EmitParams Color(Color c, float mul = 1) { R = c.r * mul; G = c.g * mul; B = c.b * mul; return this; }
        public EmitParams Pos(Vector3 p) { X = p.x; Y = p.y; Z = p.z; return this; }
        public EmitParams Dir(Vector3 d)
        {
            float l = d.magnitude; if (l < 1e-6f) { Dx = 0; Dy = 1; Dz = 0; return this; }
            Dx = d.x / l; Dy = d.y / l; Dz = d.z / l; return this;
        }
    }

    sealed class ParticleBatch
    {
        readonly int _max;
        readonly PMode _mode;
        readonly FxRng _rng;
        readonly Vector4 _rect;
        readonly QuadMesh _mesh;
        public readonly EmitParams P = new EmitParams();
        readonly EmitParams _defaults = new EmitParams();

        readonly float[] x, y, z, vx, vy, vz, life, maxLife, size, s0, s1, aspect, rot, spin, grav, drag, turb;
        readonly float[] r, g, b, alpha, ax, ay, az, wx, wy, wz, stretch, phase;
        readonly byte[] fade;
        readonly int[] free, alive;
        int _freeCount, _aliveCount;

        public int Alive => _aliveCount;
        /// Tumble only: a piece that falls below this height lands (lies flat, stops, fades within half a second).
        /// -inf = no floor (the default).
        public float Floor = float.NegativeInfinity;

        public ParticleBatch(string name, Transform parent, Material mat, Vector4 rect, PMode mode, int max, FxRng rng, int order, System.Action<EmitParams> defaults)
        {
            _max = max; _mode = mode; _rng = rng; _rect = rect;
            _mesh = new QuadMesh(name, parent, mat, max * 4, QuadMesh.QuadIndices(max), order);
            defaults?.Invoke(_defaults);
            float[] F() => new float[max];
            x = F(); y = F(); z = F(); vx = F(); vy = F(); vz = F(); life = F(); maxLife = F(); size = F(); s0 = F(); s1 = F(); aspect = F();
            rot = F(); spin = F(); grav = F(); drag = F(); turb = F(); r = F(); g = F(); b = F(); alpha = F();
            ax = F(); ay = F(); az = F(); wx = F(); wy = F(); wz = F(); stretch = F(); phase = F();
            fade = new byte[max]; free = new int[max]; alive = new int[max];
            for (int i = 0; i < max; i++) free[i] = max - 1 - i;
            _freeCount = max;
        }

        public EmitParams Begin() { P.CopyFrom(_defaults); return P; }

        int Alloc()
        {
            int s;
            if (_freeCount > 0) s = free[--_freeCount];
            else { s = alive[0]; alive[0] = alive[--_aliveCount]; }   // recycle the oldest
            alive[_aliveCount++] = s;
            return s;
        }

        public void Emit(int n)
        {
            var p = P;
            for (int k = 0; k < n; k++)
            {
                int i = Alloc();
                float th = _rng.Next() * Mathf.PI * 2;
                float ph = Mathf.Acos(2 * _rng.Next() - 1);
                float sph = Mathf.Sin(ph);
                float dx = p.Dx + (sph * Mathf.Cos(th) - p.Dx) * p.Cone;
                float dy = p.Dy + (sph * Mathf.Sin(th) - p.Dy) * p.Cone;
                float dz = p.Dz + (Mathf.Cos(ph) - p.Dz) * p.Cone;
                float dl = Mathf.Sqrt(dx * dx + dy * dy + dz * dz); if (dl < 1e-6f) dl = 1;
                float sp = p.Speed * (1 + (_rng.Next() * 2 - 1) * p.SpeedVar);
                vx[i] = dx / dl * sp; vy[i] = dy / dl * sp; vz[i] = dz / dl * sp;
                x[i] = p.X + (_rng.Next() * 2 - 1) * p.Jitter; y[i] = p.Y + (_rng.Next() * 2 - 1) * p.Jitter; z[i] = p.Z + (_rng.Next() * 2 - 1) * p.Jitter;
                life[i] = 0;
                maxLife[i] = Mathf.Max(0.02f, p.Life * (1 + (_rng.Next() * 2 - 1) * p.LifeVar));
                size[i] = p.Size * (1 + (_rng.Next() * 2 - 1) * p.SizeVar);
                s0[i] = p.S0; s1[i] = p.S1; aspect[i] = p.Aspect;
                rot[i] = _rng.Next() * Mathf.PI * 2;
                spin[i] = (_rng.Next() * 2 - 1) * p.SpinVar + p.Spin;
                grav[i] = p.Grav; drag[i] = p.Drag; turb[i] = p.Turb;
                r[i] = p.R; g[i] = p.G; b[i] = p.B; alpha[i] = p.Alpha;
                stretch[i] = p.Stretch;
                phase[i] = _rng.Next() * Mathf.PI * 2;
                if (_mode == PMode.Tumble)
                {
                    ax[i] = _rng.Next() * Mathf.PI * 2; ay[i] = _rng.Next() * Mathf.PI * 2; az[i] = _rng.Next() * Mathf.PI * 2;
                    float w = p.Tumble != 0 ? p.Tumble : 9;
                    wx[i] = (_rng.Next() * 2 - 1) * w; wy[i] = (_rng.Next() * 2 - 1) * w; wz[i] = (_rng.Next() * 2 - 1) * w * 0.6f;
                }
                fade[i] = (byte)p.Fade;
            }
        }

        public void KillAll()
        {
            _aliveCount = 0; _freeCount = _max;
            for (int i = 0; i < _max; i++) free[i] = _max - 1 - i;
            _mesh.Flush(0, 0);
        }

        static float FadeAt(byte kind, float t)
        {
            switch ((PFade)kind)
            {
                case PFade.Linear: return 1 - t;
                case PFade.Pop: return Mathf.Sin(Mathf.PI * Mathf.Clamp01(t));
                case PFade.Late: return t < 0.55f ? 1 : 1 - (t - 0.55f) / 0.45f;
                case PFade.Hold: return t < 0.35f ? 1 : Mathf.Pow(1 - (t - 0.35f) / 0.65f, 1.6f);
                default: { float u = 1 - t; return u * u; }
            }
        }

        /// Integrate and rewrite the quads. Runs every frame including dt == 0 (a hitstop): the impact
        /// frame is a hitstop frame, and a particle that waits for the freeze to end arrives late.
        public void Update(float dt, in CamAxes cam, float time, bool prewarm)
        {
            int q = 0;
            for (int k = 0; k < _aliveCount; k++)
            {
                int i = alive[k];
                if (dt > 0)
                {
                    float nl = life[i] + dt;
                    if (nl >= maxLife[i]) { alive[k] = alive[--_aliveCount]; free[_freeCount++] = i; k--; continue; }
                    life[i] = nl;
                    vy[i] += grav[i] * dt;
                    float d = 1 - Mathf.Min(0.95f, drag[i] * dt);
                    vx[i] *= d; vy[i] *= d; vz[i] *= d;
                    if (turb[i] != 0)
                    {
                        float ph = phase[i] + time * 3.1f;
                        vx[i] += Mathf.Sin(ph) * turb[i] * dt; vz[i] += Mathf.Cos(ph * 1.37f) * turb[i] * dt;
                    }
                    x[i] += vx[i] * dt; y[i] += vy[i] * dt; z[i] += vz[i] * dt;
                    rot[i] += spin[i] * dt;
                    if (_mode == PMode.Tumble) { ax[i] += wx[i] * dt; ay[i] += wy[i] * dt; az[i] += wz[i] * dt; }
                    if (_mode == PMode.Tumble && y[i] < Floor && grav[i] != 0)
                    {
                        // Landed: flat on the boards (ax = 90 deg, az = 0 puts both quad axes in XZ), still, gone soon.
                        y[i] = Floor; vx[i] = vy[i] = vz[i] = 0; grav[i] = 0; turb[i] = 0;
                        wx[i] = wy[i] = wz[i] = 0; ax[i] = Mathf.PI * 0.5f; az[i] = 0;
                        maxLife[i] = Mathf.Min(maxLife[i], life[i] + 0.45f);
                    }
                }

                float t = life[i] / maxLife[i];
                float sc = size[i] * (s0[i] + (s1[i] - s0[i]) * FxColor.EaseOutCubic(t));
                float a = alpha[i] * FadeAt(fade[i], t);
                var pos = new Vector3(x[i], y[i], z[i]);
                Vector3 axv, ayv;
                switch (_mode)
                {
                    case PMode.Tumble:
                    {
                        // Confetti: shrink and fade over the last 40% of its life; melt away when it drifts close to the
                        // lens and never fill more than a sliver of the frame (a piece falling past the camera read as a
                        // huge brown card).
                        if (t > 0.6f) { float kf = (t - 0.6f) / 0.4f; sc *= 1 - kf * 0.65f; a *= 1 - kf * kf; }
                        float depth = Vector3.Dot(pos - cam.P, cam.F);
                        a *= Mathf.Clamp01((depth - 1.8f) / 2.7f);
                        sc = Mathf.Min(sc, Mathf.Max(0, depth) * 0.032f);
                        float cx = Mathf.Cos(ax[i]), sx = Mathf.Sin(ax[i]), cy = Mathf.Cos(ay[i]), sy = Mathf.Sin(ay[i]), cz = Mathf.Cos(az[i]), sz = Mathf.Sin(az[i]);
                        float w = sc * aspect[i], h = sc;
                        axv = new Vector3(cz * cy, sz * cy, -sy) * w;
                        ayv = new Vector3(cz * sy * sx - sz * cx, sz * sy * sx + cz * cx, cy * sx) * h;
                        // Two-sided "lit" paper: a face turned to the camera catches the light, an edge-on one dims.
                        var nrm = Vector3.Cross(axv, ayv);
                        float nl = nrm.magnitude;
                        float face = nl > 1e-8f ? Mathf.Abs(Vector3.Dot(nrm, cam.F)) / nl : 1;
                        float lit = 0.62f + 0.38f * face;
                        _mesh.Quad(q++, pos, axv, ayv, FxColor.C32(r[i] * lit, g[i] * lit, b[i] * lit, a), _rect, Vector4.zero);
                        continue;
                    }
                    case PMode.Ground:
                    {
                        float c = Mathf.Cos(rot[i]), s = Mathf.Sin(rot[i]);
                        axv = new Vector3(c, 0, s) * (sc * aspect[i]);
                        ayv = new Vector3(-s, 0, c) * sc;
                        break;
                    }
                    default:
                    {
                        float c, s, w = sc * aspect[i], h = sc;
                        if (_mode == PMode.Stretch)
                        {
                            var v = new Vector3(vx[i], vy[i], vz[i]);
                            float sr = Vector3.Dot(v, cam.R), su = Vector3.Dot(v, cam.U);
                            float m = Mathf.Sqrt(sr * sr + su * su);
                            if (m > 1e-4f) { c = sr / m; s = su / m; } else { c = 1; s = 0; }
                            w = sc * aspect[i] * (1 + v.magnitude * stretch[i]);
                        }
                        else { c = Mathf.Cos(rot[i]); s = Mathf.Sin(rot[i]); }
                        axv = (cam.R * c + cam.U * s) * w;
                        ayv = (-cam.R * s + cam.U * c) * h;
                        break;
                    }
                }
                _mesh.Quad(q++, pos, axv, ayv, FxColor.C32(r[i], g[i], b[i], a), _rect, Vector4.zero);
            }
            if (prewarm && q == 0)
                _mesh.Quad(q++, new Vector3(0, -500, 0), cam.R * 0.01f, cam.U * 0.01f, FxColor.C32(0, 0, 0, 0.01f), _rect, Vector4.zero);
            _mesh.Flush(q * 4, q * 6);
        }

        public void Destroy() => _mesh.Destroy();
    }

    // ---------------------------------------------------------------------- rings

    sealed class RingBatch
    {
        readonly int _max;
        readonly FxRng _rng;
        readonly QuadMesh _mesh;
        readonly float[] x, y, z, life, maxLife, from, to, th0, th1, wob, seed, r, g, b, alpha, spin;
        readonly Quaternion[] q;
        readonly bool[] billboard;
        readonly int[] free, alive;
        int _freeCount, _aliveCount;
        public int Alive => _aliveCount;

        public RingBatch(string name, Transform parent, Material mat, int max, FxRng rng, int order)
        {
            _max = max; _rng = rng;
            _mesh = new QuadMesh(name, parent, mat, max * 4, QuadMesh.QuadIndices(max), order);
            float[] F() => new float[max];
            x = F(); y = F(); z = F(); life = F(); maxLife = F(); from = F(); to = F(); th0 = F(); th1 = F(); wob = F(); seed = F();
            r = F(); g = F(); b = F(); alpha = F(); spin = F();
            q = new Quaternion[max]; billboard = new bool[max];
            free = new int[max]; alive = new int[max];
            for (int i = 0; i < max; i++) free[i] = max - 1 - i;
            _freeCount = max;
        }

        int Alloc()
        {
            int s;
            if (_freeCount > 0) s = free[--_freeCount];
            else { s = alive[0]; alive[0] = alive[--_aliveCount]; }
            alive[_aliveCount++] = s;
            return s;
        }

        /// `normal` null = a camera-facing billboard.
        public void Spawn(Vector3 pos, Color color, float lifeS, float fromR, float toR, float thick0, float thick1, float wobble, float alp, Vector3? normal, float spinRate)
        {
            int i = Alloc();
            x[i] = pos.x; y[i] = pos.y; z[i] = pos.z;
            life[i] = 0; maxLife[i] = lifeS; from[i] = fromR; to[i] = toR; th0[i] = thick0; th1[i] = thick1; wob[i] = wobble;
            seed[i] = _rng.Next() * 6.283f;
            r[i] = color.r; g[i] = color.g; b[i] = color.b; alpha[i] = alp; spin[i] = spinRate;
            if (normal.HasValue) { q[i] = Quaternion.FromToRotation(Vector3.forward, normal.Value.normalized); billboard[i] = false; }
            else billboard[i] = true;
        }

        public void KillAll()
        {
            _aliveCount = 0; _freeCount = _max;
            for (int i = 0; i < _max; i++) free[i] = _max - 1 - i;
            _mesh.Flush(0, 0);
        }

        public void Update(float dt, in CamAxes cam, Quaternion camRot, bool prewarm)
        {
            int n = 0;
            for (int k = 0; k < _aliveCount; k++)
            {
                int i = alive[k];
                if (dt > 0)
                {
                    float nl = life[i] + dt;
                    if (nl >= maxLife[i]) { alive[k] = alive[--_aliveCount]; free[_freeCount++] = i; k--; continue; }
                    life[i] = nl;
                }
                float t = Mathf.Clamp01(life[i] / maxLife[i]);
                float rad = from[i] + (to[i] - from[i]) * FxColor.EaseOutQuint(t);   // fast on frame one, then coasting
                float thick = th0[i] + (th1[i] - th0[i]) * FxColor.EaseOutCubic(t);
                float a = alpha[i] * (1 - t) * (1 - t);
                float scale = rad * 2.4f;                                           // radius01 is fixed at 0.42 of the quad
                Quaternion rq = billboard[i] ? camRot : q[i];
                if (spin[i] != 0) rq = Quaternion.AngleAxis(spin[i] * life[i] * Mathf.Rad2Deg, rq * Vector3.forward) * rq;
                var axv = rq * Vector3.right * scale; var ayv = rq * Vector3.up * scale;
                _mesh.Quad(n++, new Vector3(x[i], y[i], z[i]), axv, ayv, FxColor.C32(r[i], g[i], b[i], a), new Vector4(0, 0, 1, 1),
                    new Vector4(0.42f, thick, wob[i] * (0.35f + t * 1.2f), seed[i]));
            }
            if (prewarm && n == 0)
                _mesh.Quad(n++, new Vector3(0, -500, 0), cam.R * 0.01f, cam.U * 0.01f, FxColor.C32(0, 0, 0, 0.01f), new Vector4(0, 0, 1, 1), new Vector4(0.42f, 0.1f, 0, 0));
            _mesh.Flush(n * 4, n * 6);
        }

        public void Destroy() => _mesh.Destroy();
    }

    // --------------------------------------------------------------------- trails

    /// A ribbon trail in use: feed it Set(pos) each frame and Release() when the object stops.
    public sealed class TrailHandle
    {
        internal TrailBatch Owner; internal int Index;
        public void Set(Vector3 p) => Owner?.SetHead(Index, p);
        public void SetColor(Color c) => Owner?.SetColor(Index, c);
        public void SetWidth(float w) => Owner?.SetWidth(Index, w);
        public void Release() => Owner?.Release(Index);
    }

    sealed class TrailBatch
    {
        readonly int _max, _segs;
        readonly float _sampleDt;
        readonly QuadMesh _mesh;
        readonly Vector4 _solid;
        readonly Vector3[] hist, head, prev;
        readonly int[] count;
        readonly float[] acc, width, r, g, b, fade;
        readonly bool[] active, dying, moved;
        readonly TrailHandle[] _handles;
        const float MaxRibbonLen = 3.5f;   // world units a ribbon may span at full brightness

        public TrailBatch(Transform parent, Material mat, Vector4 solidRect, int max, int segs, float sampleHz, int order)
        {
            _max = max; _segs = segs; _sampleDt = 1f / sampleHz; _solid = solidRect;
            var idx = new ushort[max * (segs - 1) * 6];
            for (int t = 0; t < max; t++)
            {
                int bse = t * segs * 2, io = t * (segs - 1) * 6;
                for (int s = 0; s < segs - 1; s++)
                {
                    int a = bse + s * 2, o = io + s * 6;
                    idx[o] = (ushort)a; idx[o + 1] = (ushort)(a + 1); idx[o + 2] = (ushort)(a + 2);
                    idx[o + 3] = (ushort)(a + 1); idx[o + 4] = (ushort)(a + 3); idx[o + 5] = (ushort)(a + 2);
                }
            }
            _mesh = new QuadMesh("Trails", parent, mat, max * segs * 2, idx, order);
            hist = new Vector3[max * segs]; head = new Vector3[max]; prev = new Vector3[max]; count = new int[max];
            acc = new float[max]; width = new float[max]; r = new float[max]; g = new float[max]; b = new float[max]; fade = new float[max];
            active = new bool[max]; dying = new bool[max]; moved = new bool[max];
            _handles = new TrailHandle[max];
            for (int i = 0; i < max; i++) _handles[i] = new TrailHandle { Owner = this, Index = i };
        }

        public TrailHandle Acquire(Color c, float w)
        {
            for (int i = 0; i < _max; i++)
            {
                if (active[i]) continue;
                active[i] = true; dying[i] = false; count[i] = 0; acc[i] = 0; fade[i] = 1; moved[i] = false; width[i] = w;
                r[i] = c.r; g[i] = c.g; b[i] = c.b;
                return _handles[i];
            }
            return null;
        }

        internal void SetColor(int i, Color c) { r[i] = c.r; g[i] = c.g; b[i] = c.b; }
        internal void SetWidth(int i, float w) { width[i] = w; }

        internal void SetHead(int i, Vector3 p)
        {
            if (!moved[i]) { prev[i] = p; moved[i] = true; }
            head[i] = p;
        }

        internal void Release(int i) { if (active[i]) dying[i] = true; }

        void Clear(int i)
        {
            int vb = i * _segs * 2;
            for (int v = 0; v < _segs * 2; v++) _mesh.V[vb + v].C.a = 0;
        }

        void Push(int i, Vector3 p)
        {
            int bse = i * _segs;
            for (int s = _segs - 1; s > 0; s--) hist[bse + s] = hist[bse + s - 1];
            hist[bse] = p;
            if (count[i] < _segs) count[i]++;
        }

        public void Update(float dt, in CamAxes cam)
        {
            bool any = false;
            var cf = cam.F;
            for (int i = 0; i < _max; i++)
            {
                if (!active[i]) continue;
                if (dying[i])
                {
                    fade[i] -= dt * 4.5f;   // ~220 ms tail after release
                    if (fade[i] <= 0) { active[i] = false; dying[i] = false; Clear(i); continue; }
                }
                if (dt > 0 && moved[i])
                {
                    acc[i] += dt;
                    int steps = 0;
                    while (acc[i] >= _sampleDt && steps < 4)
                    {
                        acc[i] -= _sampleDt; steps++;
                        float u = Mathf.Clamp01(1 - acc[i] / Mathf.Max(dt, 1e-5f));
                        Push(i, Vector3.LerpUnclamped(prev[i], head[i], u));
                    }
                    prev[i] = head[i];
                }
                int n = count[i];
                if (n < 2) { Clear(i); continue; }
                any = true;
                int hb = i * _segs, vb = i * _segs * 2;
                float w = width[i], glob = fade[i];
                // A ball at grand-slam speed stretches the 0.31 s ribbon over ~10 world units: a hard-edged beam across the
                // frame. The web hides it behind the stands; here the ribbon's brightness is spread over its length instead.
                float len = 0;
                for (int s = 1; s < n; s++) len += (hist[hb + s] - hist[hb + s - 1]).magnitude;
                glob *= Mathf.Clamp(MaxRibbonLen / Mathf.Max(len, 1e-3f), 0.22f, 1f);
                for (int s = 0; s < _segs; s++)
                {
                    int si = Mathf.Min(s, n - 1);
                    var p = hist[hb + si];
                    int j = Mathf.Min(si + 1, n - 1);
                    var tg = hist[hb + j] - p;
                    if (si == j && si > 0) tg = p - hist[hb + si - 1];
                    var side = Vector3.Cross(tg, cf);
                    float sl = side.magnitude;
                    side = sl > 1e-5f ? side / sl : Vector3.right;
                    float age = s / (float)(_segs - 1);
                    float hw = w * (1 - age) * (1 - age * 0.35f) * 0.5f;
                    float a = glob * (1 - age) * (1 - age) * (s < n ? 1 : 0);
                    var c = FxColor.C32(r[i], g[i], b[i], a);
                    var uv = new Vector2(_solid.x + _solid.z * 0.5f, _solid.y + _solid.w * 0.5f);
                    _mesh.V[vb + s * 2].P = p + side * hw; _mesh.V[vb + s * 2].C = c; _mesh.V[vb + s * 2].UV = uv;
                    _mesh.V[vb + s * 2 + 1].P = p - side * hw; _mesh.V[vb + s * 2 + 1].C = c; _mesh.V[vb + s * 2 + 1].UV = uv;
                }
            }
            if (any) _mesh.Flush(_max * _segs * 2, _max * (_segs - 1) * 6);
            else _mesh.Flush(0, 0);
        }

        public void KillAll()
        {
            for (int i = 0; i < _max; i++) { active[i] = false; dying[i] = false; count[i] = 0; Clear(i); }
            _mesh.Flush(0, 0);
        }

        public void Destroy() => _mesh.Destroy();
    }

    // -------------------------------------------------------------------- plates

    /// Rounded dark plates behind callout words (an SDF quad: aspect and stroke colour ride in uv1).
    sealed class PlateBatch
    {
        readonly QuadMesh _mesh;
        public PlateBatch(Transform parent, Material mat, int max, int order) =>
            _mesh = new QuadMesh("Plates", parent, mat, max * 4, QuadMesh.QuadIndices(max), order);

        public int Begin() => 0;
        public void Plate(int n, Vector3 pos, Vector3 right, Vector3 up, float w, float h, Color fill, float alpha, Color stroke)
            => _mesh.Quad(n, pos, right * w, up * h, FxColor.C32(fill.r, fill.g, fill.b, alpha), new Vector4(0, 0, 1, 1), new Vector4(w / h, stroke.r, stroke.g, stroke.b));
        public void Flush(int n) => _mesh.Flush(n * 4, n * 6);
        public void Destroy() => _mesh.Destroy();
    }
}
