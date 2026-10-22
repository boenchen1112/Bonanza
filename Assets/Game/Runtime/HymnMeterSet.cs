// Bounce Brigade: the stage. A stair of columns over a sea of cloud, the sky changing with the level (dawn, day,
// vespers), a cloud choir behind the path, and the jumper (a little cantor with a halo) hopping one step per beat.
//
//   path     every excerpt lays one column per beat along +x, then a landing cloud and a lead-in cloud before the
//            next excerpt, so the climb is one continuous journey across the six. Hops: one per beat on the
//            excerpt's own grid, two between excerpts (landing, lead-in) spread over the gap.
//   steps    a column per beat whose capital stands at the beat's real height (heavier = lower: 1 3 2 3 / 1 3 3 /
//            1 3) and wears the beat's key colour with its arrow on the front. Until the reveal every capital waits at
//            one common height, plain ivory, wrapped in a cloud puff: the jumper hops on the puffs, so nothing tells a
//            downbeat from another beat. On the reveal a wave runs out from the jumper along the stair: each puff
//            lifts away behind the path while its column settles to its true height and takes its colour.
//   world    sky dome and light from the palette; a cloud-sea floor and three chunked cloud layers (the sea, towers,
//            far strata) that tile along x, a glory around the sun, and a choir of cloud singers recycled along
//            the path, bobbing on every beat alike and cheering a right answer.
//   camera   follows the jumper; on the reveal it pulls out to show the whole stair, then eases back in.
//
// Everything is built in Load (no physics, no Shader.Find: materials are copies of Resources/Look templates).
// Per frame: transforms, MeshBatch flushes and material floats only, no allocations.

using Bonanza.Core;
using Bonanza.Look;
using UnityEngine;

namespace Bonanza
{
    public sealed partial class HymnMeterGame
    {
        // ------------------------------------------------------------ layout

        const float Pitch = 1.62f;          // x per beat
        const float PuffTop = 0.72f;        // the veil puffs' top: the jumper's floor before the reveal
        const float HiddenTop = PuffTop - 0.3f;   // every capital waits here, one height for all, inside its cloud puff        // a column's capital while it waits under the sea
        const float SeaY = -0.95f;          // the cloud-sea floor
        const int Banks = 3;                // slot k's steps live in bank k % 3 (one behind, one in play, one ahead)
        const int Extra = 4;                // puffs per bank beyond the steps: landing, lead-in, second lead-in, home
        const float CapW = 1.16f, CapH = 0.48f;
        static float StepTop(int h) => 0.08f + (h - 1) * 0.62f;   // 1 -> 0.08, 2 -> 0.70, 3 -> 1.32

        struct Hop { public double T; public int Slot, P; }

        sealed class Bank
        {
            public int Slot = -1, N;
            public float X0;
            public bool Revealed;
            public float Age;   // scene seconds since the reveal began
            public int Origin;  // the step the wave starts from (the one under the jumper)
            public bool[] Arrived = new bool[0];
        }

        readonly Bank[] _banks = { new Bank(), new Bank(), new Bank() };
        Hop[] _hops = new Hop[0];
        int _hopN, _hopAt;
        readonly float[] _slotX = new float[HymnRound.ExcerptCount];
        int _cap;
        MeshBatch _cols, _caps, _glyphs, _puffs, _choir, _mouths;
        float[] _stepPop, _puffSquash;
        bool _batchDirty = true;

        readonly System.Collections.Generic.List<Object> _owned = new System.Collections.Generic.List<Object>();
        Material _veilMat, _cloudMat, _stoneMat, _capMat, _glyphMat, _seaMat, _farMat, _horizonMat, _haloMat, _gloryMat, _choirMat, _mouthMat, _blobMat;
        Transform _seaFloor, _glory;
        readonly Transform[][] _layers = new Transform[3][];
        static readonly float[] LayerW = { 48, 110, 220 };

        // the jumper
        MeshRenderer _jBlobR;
        Transform _jRoot, _jBody, _jHalo, _jWingL, _jWingR, _jMouth, _jBlob, _jEyes;
        MaterialPropertyBlock _blobMpb;
        TrailHandle _trail;
        float _jx, _jy, _jSurf, _jLand, _jSing, _cheer, _sad, _jPrevAir;
        int _lastLandHop = -1;

        // camera
        float _camX, _camY, _overview, _overviewHold, _finale;
        int _overviewSlot = -1;
        float _time, _sweep;
        Transform _sweepL, _sweepR;
        Material _sweepMat;

        // choir: singers recycled along x at a fixed world pitch
        const int ChoirN = 11;
        const float ChoirPitch = 8.5f, ChoirZ = 34f;
        float _choirOpen, _choirHop;

        // ------------------------------------------------------------ materials

        T Own<T>(T o) where T : Object { _owned.Add(o); return o; }

        Material Surf(Color c, float rim = 1, float pulse = 0.08f, float lift = 0, float bands = 3, float softness = 0.22f, float fog = 1, Texture2D map = null, Vector2? tiling = null, float mapStrength = 0)
        {
            var m = Own(new Material(EnvKit.ToonEnv));
            m.SetColor("_BaseColor", c);
            if (map) { m.SetTexture("_BaseMap", map); m.SetTextureScale("_BaseMap", tiling ?? Vector2.one); }
            m.SetFloat("_MapStrength", mapStrength);
            m.SetFloat("_Rim", rim); m.SetFloat("_Pulse", pulse); m.SetFloat("_Lift", lift); m.SetFloat("_Bands", bands);
            m.SetFloat("_Softness", softness);
            m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            m.SetFloat("_FogMul", fog);
            return m;
        }

        Material Add(float facing = 0, float fog = 0)
        {
            var m = Own(new Material(EnvKit.GlowAddMat));
            m.SetColor("_Color", Color.white);
            m.SetFloat("_Intensity", 1);
            m.SetFloat("_Facing", facing);
            m.SetFloat("_Ramp", 0);
            m.SetFloat("_FogMul", fog);
            return m;
        }

        MeshRenderer Part(string name, Transform parent, MeshBuilder b, Material m, Vector3 pos)
        {
            var r = EnvKit.Renderer(name, parent, Own(b.ToMesh(name)), m);
            r.transform.localPosition = pos;
            r.receiveShadows = false;
            return r;
        }

        /// A soft value-noise map for the cloud-sea floor (tiles; built once).
        static Texture2D NoiseTex(int n, uint seed)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "bb-cloudnoise" };
            var px = new Color32[n * n];
            var rng = new FxRng(seed);
            int g = 8;
            var grid = new float[g * g];
            for (int i = 0; i < grid.Length; i++) grid[i] = rng.Next();
            var grid2 = new float[16 * 16];
            for (int i = 0; i < grid2.Length; i++) grid2[i] = rng.Next();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = 0.65f * Smooth(grid, g, x * g / (float)n, y * g / (float)n) + 0.35f * Smooth(grid2, 16, x * 16f / n, y * 16f / n);
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt((0.72f + v * 0.28f) * 255), 0, 255);
                    px[y * n + x] = new Color32(b, b, b, 255);
                }
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        }

        static float Smooth(float[] grid, int g, float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = grid[((y0 % g) * g) + (x0 % g)], b = grid[((y0 % g) * g) + ((x0 + 1) % g)];
            float c = grid[(((y0 + 1) % g) * g) + (x0 % g)], d = grid[(((y0 + 1) % g) * g) + ((x0 + 1) % g)];
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        // ------------------------------------------------------------ build

        void BuildStage()
        {
            var cam = Camera.main;
            if (cam) { cam.nearClipPlane = 0.3f; cam.farClipPlane = 400f; }
            Look.Rig.SetPushGain(0.5f);
            Look.Rig.SetDriftGain(0.8f);
            var fx = Look.Fx;
            fx.SetGroundY(PuffTop);
            fx.SetFocus(new Vector3(0, 1, 0));

            _cloudMat = Surf(new Color(0.97f, 0.97f, 1f), rim: 1.1f, pulse: 0.03f, lift: 0.34f, bands: 3, softness: 0.4f);
            _veilMat = Surf(new Color(1f, 0.97f, 0.9f), rim: 1.7f, pulse: 0.14f, lift: 0.42f, bands: 3, softness: 0.35f, fog: 0.4f);
            _stoneMat = Surf(Color.white, rim: 0.7f, pulse: 0.05f, lift: 0.03f, bands: 3, softness: 0.3f);
            _capMat = Surf(Color.white, rim: 1.1f, pulse: 0.1f, lift: 0.2f, bands: 3, softness: 0.25f);
            _glyphMat = Add(0);
            var noise = Own(NoiseTex(128, 0xb0b0));
            _seaMat = Surf(new Color(0.9f, 0.89f, 0.97f), rim: 0.3f, pulse: 0.02f, lift: 0.08f, bands: 2, softness: 0.6f, map: noise, tiling: new Vector2(14, 6), mapStrength: 0.5f);
            _farMat = Surf(new Color(1f, 0.98f, 1f), rim: 1.1f, pulse: 0, lift: 0.3f, bands: 3, softness: 0.5f, fog: 0.75f);
            _horizonMat = Surf(new Color(1f, 0.97f, 0.97f), rim: 1.2f, pulse: 0, lift: 0.2f, bands: 2, softness: 0.7f, fog: 0.7f);
            _choirMat = Surf(Color.white, rim: 0.9f, pulse: 0.05f, lift: 0.18f, bands: 3, softness: 0.45f, fog: 1f);
            _mouthMat = Surf(Color.white, rim: 0, pulse: 0, lift: 0, bands: 1, softness: 0.2f, fog: 1f);

            BuildSea();
            BuildLayers();
            BuildGlory();
            BuildSweep();
            BuildSteps();
            BuildChoir();
            BuildJumper();
            fx.Prewarm();   // every FX material and the callout font now: a first-use shader link mid-round would push a press out of its window
        }

        void DestroyStage()
        {
            _trail?.Release();
            _trail = null;
            foreach (var o in _owned) if (o) Destroy(o);
            _owned.Clear();
            foreach (var b in new[] { _cols, _caps, _glyphs, _puffs, _choir, _mouths }) if (b != null && b.Mesh) Destroy(b.Mesh);
        }

        /// A cloud puff cluster with its top near y = 0: one wide body and four lobes. Vertex colour darkens toward
        /// the underside (a baked occlusion the toon bands then light).
        static void Puff(MeshBuilder b, Matrix4x4 m, float w, int ws = 10, int hs = 7, uint seed = 1, float tone = 1, int lobes = 4)
        {
            var rng = new FxRng(seed);
            int start = b.Count;
            b.Sphere(0.62f * w, ws, hs, m * Matrix4x4.Translate(new Vector3(0, -0.4f * w, 0)), Color.white, new Vector3(1.22f, 0.66f, 1.06f));
            for (int k = 0; k < lobes; k++)
            {
                float a = k * Mathf.PI * 2 / lobes + rng.Next() * 0.9f;
                float r = (0.36f + rng.Next() * 0.12f) * w;
                var c = new Vector3(Mathf.Cos(a) * 0.46f * w, -0.4f * w - rng.Next() * 0.1f * w, Mathf.Sin(a) * 0.36f * w);
                b.Sphere(r, ws - 2, hs - 1, m * Matrix4x4.Translate(c), Color.white, new Vector3(1.1f, 0.85f, 1));
            }
            Shade(b, start, tone);
        }

        static void Shade(MeshBuilder b, int start, float tone)
        {
            for (int v = start; v < b.Count; v++)
            {
                float k = Mathf.Lerp(0.74f, 1.02f, Mathf.Clamp01(b.N[v].y * 0.6f + 0.55f)) * tone;
                b.C[v] = new Color(k, k, k * 1.02f, 1);
            }
        }

        void BuildSea()
        {
            // The floor: one big soft quad that follows the camera along x (it is uniform, so following is invisible).
            var b = new MeshBuilder();
            b.FloorQuad(700, 260, Matrix4x4.identity, Color.white);
            var r = Part("CloudSea", transform, b, _seaMat, new Vector3(0, SeaY, 100));
            _seaFloor = r.transform;
        }

        /// Three cloud layers, each one chunk of width LayerW tiled three times around the camera: the sea's puffs
        /// (a low valley along the path, banks rising behind it), towers behind, and long far strata.
        void BuildLayers()
        {
            var rng = new FxRng(0x5ea5);
            for (int L = 0; L < 3; L++)
            {
                float W = LayerW[L];
                var b = new MeshBuilder();
                if (L == 0)
                {
                    for (int i = 0; i < 95; i++)
                    {
                        float x = rng.Next() * W, z = Mathf.Lerp(-12f, 26f, Mathf.Pow(rng.Next(), 0.85f));
                        float w, top;
                        if (Mathf.Abs(z) < 2.2f) { w = 0.8f + rng.Next() * 0.6f; top = SeaY + 0.15f + rng.Next() * 0.12f; }   // the valley under the path: low, so the columns stand tall
                        else if (z < 0) { w = 1.4f + rng.Next() * 1.6f; top = SeaY + 0.2f + Mathf.Clamp01((-z - 3) / 7) * 0.5f; }   // in front: below the stair
                        else { w = 1.5f + rng.Next() * 2.0f + z * 0.06f; top = SeaY + 0.3f + (z - 2.2f) * 0.09f + rng.Next() * 0.4f; }   // behind: banks rising away
                        Puff(b, Matrix4x4.TRS(new Vector3(x, top, z), Quaternion.Euler(0, rng.Next() * 360, 0), Vector3.one), w, 8, 5, (uint)(i + 11), 0.95f + rng.Next() * 0.07f, 3);
                    }
                }
                else if (L == 1)
                {
                    // a little rotunda on a far cloud: eight columns, a gilt dome (the climb's destination)
                    Rotunda(b, new Vector3(W * 0.58f, 3.2f, 52f), 2.4f);
                    Rotunda(b, new Vector3(W * 0.08f, 5.5f, 70f), 3.0f);
                    for (int i = 0; i < 16; i++)
                    {
                        float x = (i + rng.Next() * 0.6f) / 16 * W, z = 34 + rng.Next() * 30;
                        float h = 3 + rng.Next() * 7, w = 4 + rng.Next() * 4;
                        int n = 4 + (int)(rng.Next() * 3);
                        int start = b.Count;
                        for (int k = 0; k < n; k++)
                        {
                            float t = k / (float)(n - 1);
                            float rr = w * Mathf.Lerp(0.62f, 0.3f, t) * (0.85f + rng.Next() * 0.3f);
                            var c = new Vector3(x + (rng.Next() - 0.5f) * w * 0.7f, -1 + t * h, z + (rng.Next() - 0.5f) * 3);
                            b.Sphere(rr, 8, 5, Matrix4x4.Translate(c), Color.white, new Vector3(1.15f, 0.9f, 1));
                        }
                        Shade(b, start, 1);
                    }
                }
                else
                {
                    for (int i = 0; i < 12; i++)
                    {
                        float x = (i + rng.Next() * 0.5f) / 12 * W, z = 95 + rng.Next() * 35;
                        float h = 6 + rng.Next() * 10, w = 14 + rng.Next() * 12;
                        int start = b.Count;
                        for (int k = 0; k < 4; k++)
                        {
                            float t = k / 3f;
                            var c = new Vector3(x + (rng.Next() - 0.5f) * w, -2 + t * h * 0.7f, z + (rng.Next() - 0.5f) * 8);
                            b.Sphere(w * Mathf.Lerp(0.55f, 0.28f, t), 8, 4, Matrix4x4.Translate(c), Color.white, new Vector3(1.6f, 0.62f, 1));
                        }
                        Shade(b, start, 1);
                    }
                }
                var mesh = Own(b.ToMesh("CloudLayer" + L));
                mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + new Vector3(8, 4, 4));
                var mat = L == 0 ? _cloudMat : L == 1 ? _farMat : _horizonMat;
                _layers[L] = new Transform[3];
                for (int c = 0; c < 3; c++)
                {
                    var rr = EnvKit.Renderer("CloudLayer" + L + "_" + c, transform, mesh, mat);
                    rr.receiveShadows = false;
                    _layers[L][c] = rr.transform;
                }
            }
        }

        /// The glory: a soft halo of light around the sun and a few pale shafts falling from it (additive, fog-free,
        /// kept faint: the sky shader already draws the disc).
        void BuildGlory()
        {
            _gloryMat = Own(new Material(EnvKit.GlowMat));
            _gloryMat.SetColor("_Color", new Color(1f, 0.93f, 0.8f));
            _gloryMat.SetFloat("_Intensity", 0.14f);
            _gloryMat.SetFloat("_Billboard", 1);
            _gloryMat.SetFloat("_Core", 1.2f);
            var b = new MeshBuilder();
            b.Surface(1, 1, (u, v) => (new Vector3(u - 0.5f, v - 0.5f, 0), Vector3.back), Matrix4x4.identity, Color.white, Vector2.one);
            var r = Part("Glory", transform, b, _gloryMat, Vector3.zero);
            _glory = r.transform;
            _glory.localScale = new Vector3(55, 55, 1);
        }

        /// Two tall soft beams that run out from the jumper along the stair with the reveal's wave.
        void BuildSweep()
        {
            _sweepMat = Own(new Material(EnvKit.GlowMat));
            _sweepMat.SetColor("_Color", new Color(1f, 0.95f, 0.8f));
            _sweepMat.SetFloat("_Intensity", 0);
            _sweepMat.SetFloat("_Billboard", 1);
            _sweepMat.SetFloat("_Core", 1.4f);
            _sweepMat.SetVector("_Stretch", new Vector4(1, 1, 0, 0));
            var b = new MeshBuilder();
            b.Surface(1, 1, (u, v) => (new Vector3(u - 0.5f, v - 0.5f, 0), Vector3.back), Matrix4x4.identity, Color.white, Vector2.one);
            var mesh = Own(b.ToMesh("Sweep"));
            _sweepL = EnvKit.Renderer("SweepL", transform, mesh, _sweepMat).transform;
            _sweepR = EnvKit.Renderer("SweepR", transform, mesh, _sweepMat).transform;
            _sweepL.localScale = _sweepR.localScale = new Vector3(1.4f, 6f, 1);
            _sweepL.gameObject.SetActive(false); _sweepR.gameObject.SetActive(false);
        }

        void UpdateSweep(float dt)
        {
            bool on = _sweep > 0.01f && !Settings.ReduceMotion && _overviewSlot >= 0;
            if (_sweepL.gameObject.activeSelf != on) { _sweepL.gameObject.SetActive(on); _sweepR.gameObject.SetActive(on); }
            if (!on) { _sweep = 0; return; }
            var b = BankOf(_overviewSlot);
            if (b == null) { _sweep = 0; return; }
            float front = b.Age / WaveStep;   // steps the wave has travelled
            float x0 = b.X0 + b.Origin * Pitch;
            _sweepL.localPosition = new Vector3(Mathf.Max(b.X0, x0 - front * Pitch), 1.6f, -0.3f);
            _sweepR.localPosition = new Vector3(Mathf.Min(b.X0 + (b.N - 1) * Pitch, x0 + front * Pitch), 1.6f, -0.3f);
            _sweep = Mathf.Max(0, _sweep - dt * 0.9f);
            _sweepMat.SetFloat("_Intensity", 0.5f * Mathf.Sin(Mathf.Clamp01(_sweep) * Mathf.PI));
        }

        static void Rotunda(MeshBuilder b, Vector3 at, float k)
        {
            var ivory = new Color(1f, 0.95f, 0.86f);
            var gilt = new Color(1f, 0.78f, 0.36f);
            int start = b.Count;
            // its cloud: a wide cushion under the floor
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2;
                b.Sphere(1.3f * k, 10, 6, Matrix4x4.Translate(at + new Vector3(Mathf.Cos(a) * 1.1f * k, -0.75f * k, Mathf.Sin(a) * 0.8f * k)), Color.white, new Vector3(1.2f, 0.55f, 1));
            }
            b.Sphere(1.6f * k, 12, 7, Matrix4x4.Translate(at + new Vector3(0, -0.85f * k, 0)), Color.white, new Vector3(1.3f, 0.55f, 1.1f));
            Shade(b, start, 1);
            b.Shell(1.35f * k, 1.45f * k, 0.25f * k, 24, 0, Mathf.PI * 2, Matrix4x4.Translate(at + new Vector3(0, 0.12f * k, 0)), ivory, Vector2.one);
            b.Disc(1.35f * k, 24, Matrix4x4.Translate(at + new Vector3(0, 0.25f * k, 0)), ivory);
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2;
                b.Shell(0.09f * k, 0.11f * k, 1.4f * k, 8, 0, Mathf.PI * 2, Matrix4x4.Translate(at + new Vector3(Mathf.Cos(a) * 1.1f * k, 0.95f * k, Mathf.Sin(a) * 1.1f * k)), ivory, Vector2.one);
            }
            b.Shell(1.25f * k, 1.25f * k, 0.22f * k, 24, 0, Mathf.PI * 2, Matrix4x4.Translate(at + new Vector3(0, 1.75f * k, 0)), ivory, Vector2.one);
            int d = b.Count;
            b.Sphere(1.18f * k, 16, 8, Matrix4x4.Translate(at + new Vector3(0, 1.85f * k, 0)), gilt, new Vector3(1, 0.8f, 1));
            for (int v = d; v < b.Count; v++) if (b.P[v].y < at.y + 1.85f * k) b.P[v] = new Vector3(b.P[v].x, at.y + 1.85f * k, b.P[v].z);   // a dome: flatten the lower half into the drum
            b.Sphere(0.12f * k, 8, 6, Matrix4x4.Translate(at + new Vector3(0, 2.85f * k, 0)), gilt, Vector3.one);
        }

        void BuildSteps()
        {
            _cap = 8;
            foreach (int n in _beatCounts) _cap = Mathf.Max(_cap, n);
            int total = Banks * _cap;

            // column: shaft, a flared echinus and a gold astragal ring under the capital (the capital is its own batch)
            var col = new MeshBuilder();
            var ivory = new Color(0.93f, 0.89f, 0.82f);
            var gold = new Color(1.0f, 0.72f, 0.28f);
            col.Shell(0.36f, 0.42f, 6f, 12, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(0, -CapH - 0.16f - 3f, 0)), ivory, Vector2.one);
            col.Shell(0.5f, 0.37f, 0.14f, 12, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(0, -CapH - 0.07f, 0)), ivory * 1.04f, Vector2.one);
            col.Torus(0.4f, 0.045f, 4, 12, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(0, -CapH - 0.17f, 0), Quaternion.Euler(90, 0, 0), Vector3.one), gold);
            _cols = new MeshBatch("Columns", transform, col, total, _stoneMat);
            _cols.Renderer.receiveShadows = false;

            // capital: the coloured slab (tinted by the key), a lighter inset on top, a darker foot band
            var cap = new MeshBuilder();
            cap.Box(Matrix4x4.TRS(new Vector3(0, -CapH / 2, 0), Quaternion.identity, new Vector3(CapW, CapH - 0.06f, CapW)), Color.white);
            cap.Box(Matrix4x4.TRS(new Vector3(0, -0.015f, 0), Quaternion.identity, new Vector3(CapW - 0.08f, 0.04f, CapW - 0.08f)), new Color(1.18f, 1.18f, 1.18f));
            cap.Box(Matrix4x4.TRS(new Vector3(0, -CapH + 0.03f, 0), Quaternion.identity, new Vector3(CapW + 0.06f, 0.07f, CapW + 0.06f)), new Color(0.5f, 0.5f, 0.5f));
            _caps = new MeshBatch("Capitals", transform, cap, total, _capMat);
            _caps.Renderer.receiveShadows = false;

            // the key's arrow on the capital's front face (points +x; each instance turns it)
            var glyph = new MeshBuilder();
            Arrow(glyph, 0.17f);
            _glyphs = new MeshBatch("Glyphs", transform, glyph, total, _glyphMat);
            _glyphs.Renderer.receiveShadows = false;

            // the veil puffs (one per step) and per bank: landing, lead-in, second lead-in, home
            var puff = new MeshBuilder();
            Puff(puff, Matrix4x4.identity, 1.0f, 9, 6, 7, 1, 3);
            _puffs = new MeshBatch("Veil", transform, puff, Banks * (_cap + Extra), _veilMat);
            _puffs.Renderer.receiveShadows = false;

            _stepPop = new float[total];
            _puffSquash = new float[Banks * (_cap + Extra)];
            HideAll();
        }

        void HideAll()
        {
            var hide = Matrix4x4.TRS(new Vector3(0, -999, 0), Quaternion.identity, Vector3.zero);
            for (int i = 0; i < _cols.Matrix.Length; i++) { _cols.Matrix[i] = hide; _caps.Matrix[i] = hide; _glyphs.Matrix[i] = hide; _glyphs.Tint[i] = Color.black; }
            for (int i = 0; i < _puffs.Matrix.Length; i++) _puffs.Matrix[i] = hide;
            _cols.Flush(); _caps.Flush(); _glyphs.Flush(); _puffs.Flush();
        }

        /// A chunky arrow pointing +x in the XY plane, facing -z, `s` its half length.
        static void Arrow(MeshBuilder b, float s)
        {
            Vector2[] pts =
            {
                new Vector2(s, 0), new Vector2(s * 0.05f, -s * 0.9f), new Vector2(s * 0.05f, -s * 0.38f), new Vector2(-s, -s * 0.38f),
                new Vector2(-s, s * 0.38f), new Vector2(s * 0.05f, s * 0.38f), new Vector2(s * 0.05f, s * 0.9f),
            };
            int o = b.Count;
            foreach (var p in pts) { b.P.Add(new Vector3(p.x, p.y, 0)); b.N.Add(Vector3.back); b.C.Add(Color.white); b.UV.Add(Vector2.zero); }
            b.T.Add(o); b.T.Add(o + 1); b.T.Add(o + 6);
            b.T.Add(o + 2); b.T.Add(o + 3); b.T.Add(o + 4); b.T.Add(o + 2); b.T.Add(o + 4); b.T.Add(o + 5);
        }

        /// The cloud choir: a singer is a puffy body, a round head, two eyes and cheeks (one batch, vertex colours),
        /// and a mouth (its own batch: it opens on the beat).
        void BuildChoir()
        {
            var b = new MeshBuilder();
            int s0 = b.Count;
            // a cloud: one round body with three bumps on top, the face on its front
            b.Sphere(1.1f, 11, 7, Matrix4x4.Translate(new Vector3(0, 1.0f, 0)), Color.white, new Vector3(1.3f, 0.85f, 0.9f));
            b.Sphere(0.55f, 8, 5, Matrix4x4.Translate(new Vector3(-0.62f, 1.62f, 0.1f)), Color.white, Vector3.one);
            b.Sphere(0.68f, 8, 5, Matrix4x4.Translate(new Vector3(0.08f, 1.82f, 0.12f)), Color.white, Vector3.one);
            b.Sphere(0.5f, 8, 5, Matrix4x4.Translate(new Vector3(0.7f, 1.55f, 0.1f)), Color.white, Vector3.one);
            b.Sphere(0.55f, 8, 5, Matrix4x4.Translate(new Vector3(-1.15f, 0.55f, 0.1f)), Color.white, Vector3.one);
            b.Sphere(0.5f, 8, 5, Matrix4x4.Translate(new Vector3(1.18f, 0.5f, 0.1f)), Color.white, Vector3.one);
            Shade(b, s0, 1);
            var ink = new Color(0.05f, 0.04f, 0.09f);
            b.Sphere(0.1f, 8, 6, Matrix4x4.Translate(new Vector3(-0.3f, 1.25f, -0.93f)), ink, new Vector3(0.85f, 1.3f, 0.5f));
            b.Sphere(0.1f, 8, 6, Matrix4x4.Translate(new Vector3(0.3f, 1.25f, -0.93f)), ink, new Vector3(0.85f, 1.3f, 0.5f));
            var blush = new Color(1f, 0.5f, 0.6f);
            b.Sphere(0.14f, 8, 5, Matrix4x4.Translate(new Vector3(-0.55f, 1.04f, -0.86f)), blush, new Vector3(1.2f, 0.6f, 0.4f));
            b.Sphere(0.14f, 8, 5, Matrix4x4.Translate(new Vector3(0.55f, 1.04f, -0.86f)), blush, new Vector3(1.2f, 0.6f, 0.4f));
            // the hymnal, held open in both hands below the face: the camera sees its crimson boards in a shallow V
            var crimson = new Color(0.62f, 0.06f, 0.12f);
            var gilt = new Color(1f, 0.72f, 0.26f);
            foreach (var sx in new[] { -1f, 1f })
            {
                var q = Quaternion.Euler(-10, sx * 28, 0);
                b.Box(Matrix4x4.TRS(new Vector3(sx * 0.2f, 0.48f, -1.08f), q, new Vector3(0.42f, 0.5f, 0.05f)), crimson);
                b.Box(Matrix4x4.TRS(new Vector3(sx * 0.2f, 0.48f, -1.11f), q, new Vector3(0.12f, 0.12f, 0.02f)), gilt);
            }
            _choir = new MeshBatch("Choir", transform, b, ChoirN, _choirMat);
            _choir.Renderer.receiveShadows = false;
            var m = new MeshBuilder();
            m.Sphere(0.16f, 8, 5, Matrix4x4.identity, new Color(0.22f, 0.06f, 0.12f), new Vector3(1, 1, 0.45f));
            _mouths = new MeshBatch("ChoirMouths", transform, m, ChoirN, _mouthMat);
            _mouths.Renderer.receiveShadows = false;
        }

        /// The cantor: a bean in a navy robe with a white ruff, a face, two little wings and a gold halo.
        void BuildJumper()
        {
            _jRoot = new GameObject("Jumper").transform;
            _jRoot.SetParent(transform, false);
            _jBody = new GameObject("Body").transform;
            _jBody.SetParent(_jRoot, false);

            var skin = new Color(1f, 0.86f, 0.72f).linear;
            var robe = new Color(0.16f, 0.22f, 0.62f).linear;
            var trim = new Color(1f, 0.78f, 0.3f).linear;
            var white = new Color(0.97f, 0.97f, 1f).linear;
            var mat = Surf(Color.white, rim: 1.2f, pulse: 0.06f, lift: 0.04f, bands: 3, softness: 0.25f, fog: 0.6f);

            // body: a bean, skin above the ruff, robe below with a gold hem
            var b = new MeshBuilder();
            const int rad = 20, rows = 16;
            const float H = 0.86f, Wd = 0.62f;
            b.Surface(rad, rows, (u, v) =>
            {
                float a = u * Mathf.PI * 2;
                float y = H * (1 - Mathf.Cos(Mathf.PI * v)) * 0.5f;
                float r = Wd * 0.5f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Max(0.001f, v)), 0.6f) * (1.08f - 0.16f * v);
                var n = new Vector3(Mathf.Sin(a), (0.5f - v) * -1.1f, Mathf.Cos(a)).normalized;
                return (new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r), n);
            }, Matrix4x4.identity, Color.white, Vector2.one);
            for (int v = 0; v < b.Count; v++)
            {
                float y = b.P[v].y;
                b.C[v] = y > 0.5f ? skin : y > 0.085f ? robe : trim;
                if (y < 0.5f && y > 0.44f) b.C[v] = robe * 1.2f;
            }
            // ruff collar
            b.Torus(0.27f, 0.07f, 6, 24, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(0, 0.5f, 0), Quaternion.Euler(90, 0, 0), new Vector3(1, 1, 0.8f)), white);
            // a gold stole down the front
            b.Box(Matrix4x4.TRS(new Vector3(0, 0.28f, -0.3f), Quaternion.Euler(8, 0, 0), new Vector3(0.12f, 0.36f, 0.03f)), trim);
            // feet
            b.Sphere(0.08f, 8, 5, Matrix4x4.Translate(new Vector3(-0.12f, 0.02f, -0.06f)), robe * 0.6f, new Vector3(1, 0.6f, 1.4f));
            b.Sphere(0.08f, 8, 5, Matrix4x4.Translate(new Vector3(0.12f, 0.02f, -0.06f)), robe * 0.6f, new Vector3(1, 0.6f, 1.4f));
            Part("BodyMesh", _jBody, b, mat, Vector3.zero);

            // face: eyes with a catch light, cheeks
            var f = new MeshBuilder();
            var ink = new Color(0.03f, 0.03f, 0.06f);
            foreach (var sx in new[] { -1f, 1f })
            {
                f.Sphere(0.05f, 8, 6, Matrix4x4.Translate(new Vector3(sx * 0.1f, 0.68f, -0.255f)), ink, new Vector3(0.8f, 1.2f, 0.5f));
                f.Sphere(0.016f, 6, 4, Matrix4x4.Translate(new Vector3(sx * 0.1f + 0.012f, 0.7f, -0.282f)), new Color(3, 3, 3), Vector3.one);
                f.Sphere(0.05f, 8, 5, Matrix4x4.Translate(new Vector3(sx * 0.18f, 0.6f, -0.225f)), new Color(1f, 0.42f, 0.5f), new Vector3(1.3f, 0.7f, 0.4f));
            }
            _jEyes = Part("Face", _jBody, f, mat, Vector3.zero).transform;

            // mouth: opens as it sings
            var mo = new MeshBuilder();
            mo.Sphere(0.045f, 10, 6, Matrix4x4.identity, new Color(0.35f, 0.05f, 0.1f), new Vector3(1, 1, 0.5f));
            _jMouth = Part("Mouth", _jBody, mo, mat, new Vector3(0, 0.575f, -0.262f)).transform;

            // wings: little white leaves at the back
            var w = new MeshBuilder();
            w.Sphere(0.16f, 10, 6, Matrix4x4.Translate(new Vector3(0.14f, 0, 0)), white, new Vector3(1.4f, 0.75f, 0.3f));
            _jWingL = new GameObject("WingL").transform; _jWingL.SetParent(_jBody, false); _jWingL.localPosition = new Vector3(-0.2f, 0.42f, 0.2f);
            _jWingR = new GameObject("WingR").transform; _jWingR.SetParent(_jBody, false); _jWingR.localPosition = new Vector3(0.2f, 0.42f, 0.2f);
            var wm = Own(w.ToMesh("Wing"));
            EnvKit.Renderer("Wing", _jWingL, wm, mat).transform.localRotation = Quaternion.Euler(0, 0, 180);
            EnvKit.Renderer("Wing", _jWingR, wm, mat);

            // halo: a gold ring that floats over the head and lags a little
            _haloMat = Add(0.2f);
            _haloMat.SetColor("_Color", new Color(1f, 0.78f, 0.35f));
            _haloMat.SetFloat("_Intensity", 1.25f);
            var h = new MeshBuilder();
            h.Torus(0.2f, 0.028f, 6, 32, Mathf.PI * 2, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90, 0, 0), Vector3.one), Color.white);
            _jHalo = Part("Halo", _jRoot, h, _haloMat, new Vector3(0, 1.08f, 0)).transform;

            // contact shadow: a soft blob on whatever the jumper stands over
            _blobMat = Resources.Load<Material>("Look/Blob");
            var q = new GameObject("Blob", typeof(MeshFilter), typeof(MeshRenderer));
            q.transform.SetParent(transform, false);
            q.transform.localRotation = Quaternion.Euler(90, 0, 0);
            q.GetComponent<MeshFilter>().sharedMesh = GameHost.BuiltinMesh(PrimitiveType.Quad);
            var qr = q.GetComponent<MeshRenderer>();
            qr.sharedMaterial = _blobMat;
            qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            qr.receiveShadows = false;
            _jBlob = q.transform;
            _jBlobR = qr;
            _blobMpb = new MaterialPropertyBlock();

            _jRoot.localScale = Vector3.one * 1.3f;
            _jx = -3 * Pitch; _jy = PuffTop;
            _jRoot.localPosition = new Vector3(_jx, _jy, 0);
            _slotX[0] = 0;
            Frame(true);
        }

        // ------------------------------------------------------------ the path (Begin: the slots are known now)

        void BuildPath()
        {
            var slots = _round.Slots;
            float x = 0;
            for (int k = 0; k < slots.Count; k++) { _slotX[k] = x; x += (slots[k].Beats.Count + 2) * Pitch; }

            var list = new System.Collections.Generic.List<Hop>();
            var s0 = slots[0];
            double len0 = s0.Beats.Count > 1 ? s0.Beats[1].Time - s0.Beats[0].Time : 0.6;
            list.Add(new Hop { T = double.NegativeInfinity, Slot = 0, P = -3 });
            list.Add(new Hop { T = s0.Beats[0].Time - 2 * len0, Slot = 0, P = -2 });
            list.Add(new Hop { T = s0.Beats[0].Time - len0, Slot = 0, P = -1 });
            for (int k = 0; k < slots.Count; k++)
            {
                var s = slots[k];
                for (int i = 0; i < s.Beats.Count; i++) list.Add(new Hop { T = s.Beats[i].Time, Slot = k, P = i });
                double L = s.Beats[s.Beats.Count - 1].Time;
                double len = s.Beats.Count > 1 ? L - s.Beats[s.Beats.Count - 2].Time : 0.6;
                if (k + 1 < slots.Count)
                {
                    double F = slots[k + 1].Beats[0].Time;
                    list.Add(new Hop { T = L + (F - L) / 3, Slot = k, P = s.Beats.Count });
                    list.Add(new Hop { T = L + 2 * (F - L) / 3, Slot = k + 1, P = -1 });
                }
                else list.Add(new Hop { T = L + len, Slot = k, P = s.Beats.Count });
            }
            _hops = list.ToArray();
            _hopN = _hops.Length;
            _hopAt = 0;
            for (int k = 0; k < Banks && k < slots.Count; k++) Assign(k);
            Frame(true);
            _trail = Look.Fx.Trail(new Color(1f, 0.92f, 0.7f), 0.12f);
            int tris = 0, draws = 0;
            foreach (var f in GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = f.GetComponent<MeshRenderer>();
                if (f.sharedMesh == null || mr == null || !mr.enabled || !f.gameObject.activeInHierarchy) continue;
                tris += f.sharedMesh.triangles.Length / 3; draws++;
            }
            Debug.Log($"[HYMN] stage tris={tris} draws={draws} steps={Banks}x{_cap}");
        }

        void Assign(int slot)
        {
            var bk = _banks[slot % Banks];
            bk.Slot = slot;
            bk.N = _round.Slots[slot].Beats.Count;
            bk.X0 = _slotX[slot];
            bk.Revealed = false;
            bk.Age = 0;
            _batchDirty = true;
        }

        float X(int slot, int p) => _slotX[slot] + p * Pitch;

        Bank BankOf(int slot) { var b = _banks[slot % Banks]; return b.Slot == slot ? b : null; }

        /// 0..1 rise of a revealed step (the wave runs left to right).
        const float WaveStep = 0.055f;
        static float Rise(Bank b, int i) => b.Revealed ? Mathf.Clamp01((b.Age - Mathf.Abs(i - b.Origin) * WaveStep) / 0.6f) : 0;
        static float Lift(Bank b, int i) => b.Revealed ? Mathf.Clamp01((b.Age - Mathf.Abs(i - b.Origin) * WaveStep + 0.05f) / 0.9f) : 0;

        /// What the jumper lands on at (slot, p) right now.
        float Surface(int slot, int p)
        {
            var b = BankOf(slot);
            if (b == null || p < 0 || p >= b.N) return PuffTop;
            float r = Rise(b, p);
            if (r <= 0) return PuffTop;
            float top = StepTop(_round.Slots[slot].Beats[p].StepHeight);
            return Mathf.Lerp(PuffTop, top, Mathf.SmoothStep(0, 1, Mathf.Clamp01((r - 0.15f) / 0.6f)));
        }

        // ------------------------------------------------------------ events

        void StageRevealed(HymnSlot slot)
        {
            var b = BankOf(slot.Index);
            if (b == null) return;
            b.Revealed = true;
            b.Age = 0;
            b.Origin = Mathf.Clamp(Mathf.RoundToInt((_jx - b.X0) / Pitch), 0, b.N - 1);
            if (b.Arrived.Length < b.N) b.Arrived = new bool[_cap];
            System.Array.Clear(b.Arrived, 0, b.Arrived.Length);
            _sweep = 1;
            _overviewSlot = slot.Index;
            _overviewHold = Settings.ReduceMotion ? 0 : 2.6f;
            _choirOpen = 1;
            if (!Settings.ReduceMotion) Look.Rig.DoFlash(0.08f, new Color(1f, 0.97f, 0.9f));
        }

        void StageJudged(HymnSlot slot, HymnJudgement j)
        {
            var me = j.Results[0];
            var b = BankOf(slot.Index);
            if (me.Correct)
            {
                _cheer = 1;
                if (b != null && me.StartBeat >= 0)
                {
                    // the bar the player conducted rings once more
                    for (int i = me.StartBeat; i < me.StartBeat + me.Count && i < b.N; i++) _stepPop[(slot.Index % Banks) * _cap + i] = 1f - (i - me.StartBeat) * 0.12f;
                }
                var p = new Vector3(_jx, _jy + 1.4f, 0);
                Look.Fx.Burst(p, 14, new Color(1f, 0.85f, 0.4f), 3.2f, 0.1f, 0.6f, -3f);
            }
            else _sad = 1;
        }

        void RoundSummary()
        {
            _finale = 1;
            _cheer = Mathf.Max(_cheer, 0.6f);
            HudSummary();
        }

        // ------------------------------------------------------------ per frame

        void UpdateStage(float dt)
        {
            if (_jRoot == null || _round == null || !_round.Started) return;
            _time += dt;
            var slots = _round.Slots;

            // a new excerpt brings the one after it onto the bank two behind
            int cur = _round.Current;
            if (cur >= 2 && cur + 1 < slots.Count && BankOf(cur + 1) == null) Assign(cur + 1);

            foreach (var b in _banks) if (b.Revealed) b.Age += dt;

            UpdateJumper(dt);
            UpdateSteps(dt);
            UpdateSweep(dt);
            UpdateChoir(dt);
            UpdateWorld();
            Frame(false, dt);
            UpdateBeatLook();
        }

        void UpdateJumper(float dt)
        {
            double now = _now;
            while (_hopAt + 1 < _hopN && _hops[_hopAt + 1].T <= now) _hopAt++;
            var from = _hops[_hopAt];
            bool last = _hopAt + 1 >= _hopN;
            var to = last ? from : _hops[_hopAt + 1];

            double start = from.T, dur;
            if (double.IsNegativeInfinity(start))
            {
                // waiting at home: the first hop takes off one beat before it lands
                double len = to.T - _hops[Mathf.Min(_hopN - 1, _hopAt + 2)].T;
                dur = System.Math.Abs(len) > 1e-3 ? System.Math.Abs(len) : 0.6;
                start = to.T - dur;
            }
            else dur = last ? 1 : System.Math.Max(0.05, to.T - from.T);
            float u = last ? 1 : Mathf.Clamp01((float)((now - start) / dur));
            const float g = 0.18f;   // the share of the hop spent on the ground
            float x0 = X(from.Slot, from.P), x1 = X(to.Slot, to.P);
            float s0 = Surface(from.Slot, from.P), s1 = Surface(to.Slot, to.P);

            float x, y, sx = 1, sy = 1, lean = 0;
            bool waiting = now < start;
            if (last || u < g || waiting)
            {
                x = x0; y = s0;
                float tl = waiting ? 9 : u * (float)dur;   // seconds since landing
                float land = Mathf.Exp(-tl * 14f) * _jLand;
                float crouch = last || waiting ? 0 : Mathf.SmoothStep(0, 1, Mathf.Clamp01((u - g * 0.45f) / (g * 0.55f))) * 0.12f;
                float idle = (last || waiting) ? Mathf.Sin(_time * 3.2f) * 0.025f : 0;
                sy = 1 - land * 0.3f - crouch + idle;
                sx = 1 + land * 0.22f + crouch * 0.6f - idle * 0.5f;
                if (_hopAt != _lastLandHop && !waiting)
                {
                    _lastLandHop = _hopAt;
                    _jLand = 1;
                    OnLand(from, x0, s0);
                }
            }
            else
            {
                float a = (u - g) / (1 - g);
                x = Mathf.Lerp(x0, x1, a);
                float apex = 0.55f + 0.4f * Mathf.Abs(s1 - s0);
                y = Mathf.Lerp(s0, s1, a) + apex * 4 * a * (1 - a);
                float st = 0.16f * Mathf.Abs(1 - 2 * a);
                sy = 1 + st; sx = 1 - st * 0.45f;
                lean = -Mathf.Sin(a * Mathf.PI) * 9f;
            }
            if (Settings.ReduceMotion) { sx = 1 + (sx - 1) * 0.3f; sy = 1 + (sy - 1) * 0.3f; lean *= 0.3f; }

            // celebration: a little spin-hop of joy on a right answer, a droop on a wrong one
            _cheer = Mathf.Max(0, _cheer - dt * 0.9f);
            _sad = Mathf.Max(0, _sad - dt * 0.6f);
            float joy = Settings.ReduceMotion ? 0 : _cheer;
            _jx = x; _jy = y;
            _jRoot.localPosition = new Vector3(x, y, 0);
            _jRoot.localRotation = Quaternion.Euler(0, -18, lean);
            _jBody.localScale = new Vector3(sx, sy * (1 - _sad * 0.06f), sx);

            // singing: the mouth opens on every beat while an excerpt plays
            var shown = Shown;
            bool singing = shown != null && BeatIndex >= 0 && _now <= shown.WindowEnd;
            _jSing = singing ? Mathf.Exp(-BeatPhase * 3.5f) : Damp(_jSing, 0, 8, dt);
            float open = 0.35f + _jSing * 0.9f + joy * 0.6f;
            _jMouth.localScale = new Vector3(1 + joy * 0.3f - _sad * 0.2f, open * (1 - _sad * 0.4f), 1);

            // wings flap in the air
            float air = (u >= g && !last && !waiting) ? 1 : 0;
            _jPrevAir = Damp(_jPrevAir, air, 12, dt);
            float flap = Mathf.Sin(_time * 22f) * 28f * _jPrevAir + 12;
            _jWingL.localRotation = Quaternion.Euler(0, 30, -flap);
            _jWingR.localRotation = Quaternion.Euler(0, -30, flap);

            // halo lags the body a touch
            var hp = _jHalo.localPosition;
            float hy = 1.06f * sy + 0.04f;
            _jHalo.localPosition = new Vector3(0, Damp(hp.y, hy, 14, dt), 0);
            _jHalo.localRotation = Quaternion.Euler(-12, _time * 40f, 0);
            _haloMat.SetFloat("_Intensity", 1.1f + 0.35f * _jSing + joy * 0.8f);

            // contact shadow on the surface under the jumper
            float under = UnderSurface(x);
            float hgt = Mathf.Max(0, y - under);
            _jBlob.localPosition = new Vector3(x, under + 0.02f, 0);
            float br = 0.5f * (1 + Mathf.Min(hgt, 3) * 0.2f);
            _jBlob.localScale = new Vector3(br, br * 0.8f, 1);
            _blobMpb.SetFloat("_Strength", 0.5f / (1 + hgt * 0.8f));
            _jBlobR.SetPropertyBlock(_blobMpb);

            _trail?.Set(new Vector3(x - 0.05f, y + 0.42f * sy, 0.05f));
            _jLand = Damp(_jLand, 0, 2, dt);
        }

        /// The top of whatever is under x (a column, a puff, or the sea).
        float UnderSurface(float x)
        {
            for (int k = 0; k < Banks; k++)
            {
                var b = _banks[k];
                if (b.Slot < 0) continue;
                float rel = (x - b.X0) / Pitch;
                int p = Mathf.RoundToInt(rel);
                if (Mathf.Abs(rel - p) > 0.38f) continue;
                if (p >= -3 && p <= b.N) return Surface(b.Slot, p);
            }
            return SeaY;
        }

        void OnLand(Hop h, float x, float y)
        {
            var b = BankOf(h.Slot);
            var fx = Look.Fx;
            if (b != null)
            {
                int bank = h.Slot % Banks;
                if (h.P >= 0 && h.P < b.N && Rise(b, h.P) > 0.5f) _stepPop[bank * _cap + h.P] = 0.6f;
                else
                {
                    int pi = PuffIndex(bank, h.P, b.N);
                    if (pi >= 0) _puffSquash[pi] = 1;
                }
            }
            if (!Settings.ReduceMotion)
            {
                var c = new Color(1f, 1f, 1f, 1);
                fx.Ring(new Vector3(x, y + 0.03f, 0), c, 0.32f, 0.25f, 0.95f, 0.07f, 0.015f, 0.02f, 0.45f, Vector3.up);
            }
        }

        /// The puff instance for (bank, p): a step's veil puff, or one of the bank's landing / lead-in / home puffs.
        int PuffIndex(int bank, int p, int n)
        {
            int baseI = bank * (_cap + Extra);
            if (p >= 0 && p < n) return baseI + p;
            if (p == n) return baseI + _cap;
            if (p == -1) return baseI + _cap + 1;
            if (p == -2) return baseI + _cap + 2;
            if (p == -3) return baseI + _cap + 3;
            return -1;
        }

        void UpdateSteps(float dt)
        {
            var hide = Matrix4x4.TRS(new Vector3(0, -999, 0), Quaternion.identity, Vector3.zero);
            for (int k = 0; k < Banks; k++)
            {
                var b = _banks[k];
                int baseS = k * _cap, baseP = k * (_cap + Extra);
                if (b.Slot < 0)
                {
                    for (int i = 0; i < _cap; i++) { _cols.Matrix[baseS + i] = hide; _caps.Matrix[baseS + i] = hide; _glyphs.Matrix[baseS + i] = hide; }
                    for (int i = 0; i < _cap + Extra; i++) _puffs.Matrix[baseP + i] = hide;
                    continue;
                }
                var slot = _round.Slots[b.Slot];
                for (int i = 0; i < _cap; i++)
                {
                    int si = baseS + i;
                    _stepPop[si] = Mathf.Max(0, _stepPop[si] - dt * 2.4f);
                    if (i >= b.N) { _cols.Matrix[si] = hide; _caps.Matrix[si] = hide; _glyphs.Matrix[si] = hide; continue; }
                    var beat = slot.Beats[i];
                    float x = b.X0 + i * Pitch;
                    float r = Rise(b, i);
                    float target = StepTop(beat.StepHeight);
                    if (r >= 0.62f && i < b.Arrived.Length && !b.Arrived[i])
                    {
                        b.Arrived[i] = true;   // the column breaks the surface at its height: a ring in its key's colour
                        if (!Settings.ReduceMotion)
                            Look.Fx.Ring(new Vector3(b.X0 + i * Pitch, target + 0.02f, 0), KeyColor(Meter.KeyFor(beat.BeatsPerBar, beat.BarPosition) ?? "up"), 0.5f, 0.3f, 1.25f, 0.12f, 0.02f, 0.02f, 0.85f, Vector3.up);
                    }
                    float top;
                    if (r <= 0) top = HiddenTop;
                    else
                    {
                        float e = Settings.ReduceMotion ? 1 - (1 - r) * (1 - r) * (1 - r) : BackOut(r, 1.4f);
                        top = Mathf.LerpUnclamped(HiddenTop, target, e);
                    }
                    float pop = _stepPop[si];
                    top -= pop * 0.07f * Mathf.Sin(pop * Mathf.PI);
                    var pos = new Vector3(x, top, 0);
                    _cols.Matrix[si] = Matrix4x4.Translate(pos);
                    float grow = Mathf.Clamp01(r * 2.2f);   // a waiting capital is squat enough to sit inside its puff
                    _caps.Matrix[si] = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(Mathf.Lerp(0.74f, 1, grow) + pop * 0.05f, Mathf.Lerp(0.5f, 1, grow), Mathf.Lerp(0.74f, 1, grow) + pop * 0.05f));
                    string key = Meter.KeyFor(beat.BeatsPerBar, beat.BarPosition) ?? "up";
                    var kc = KeyColor(key).linear;
                    var ivory = new Color(0.93f, 0.89f, 0.82f).linear;
                    float colour = Mathf.Clamp01((r - 0.2f) / 0.5f);
                    var tint = Color.Lerp(ivory, kc, colour) * (1 + pop * 0.6f);
                    tint.a = 1;
                    _caps.Tint[si] = tint;
                    float rot = key == "right" ? 0 : key == "up" ? 90 : key == "left" ? 180 : -90;
                    _glyphs.Matrix[si] = Matrix4x4.TRS(pos + new Vector3(0, -CapH * 0.5f, -CapW * 0.5f - 0.012f), Quaternion.Euler(0, 0, rot), Vector3.one);
                    var gc = Color.Lerp(Color.white, kc, 0.18f) * (0.95f + pop * 0.8f) * colour;
                    gc.a = 1;
                    _glyphs.Tint[si] = gc;
                }
                // veil puffs
                for (int i = 0; i < _cap; i++)
                {
                    int pi = baseP + i;
                    _puffSquash[pi] = Mathf.Max(0, _puffSquash[pi] - dt * 3.5f);
                    if (i >= b.N) { _puffs.Matrix[pi] = hide; continue; }
                    float l = Lift(b, i);
                    if (l >= 1) { _puffs.Matrix[pi] = hide; continue; }
                    float sq = _puffSquash[pi];
                    float e = l * l;
                    var pos = new Vector3(b.X0 + i * Pitch + l * 0.3f, PuffTop - sq * 0.08f + e * 1.7f, l * 3.2f);
                    float k2 = (1 - l) * (1 + l * 0.7f);
                    float wob = Mathf.Sin(_time * 1.3f + i * 1.7f) * 0.015f;
                    _puffs.Matrix[pi] = Matrix4x4.TRS(pos, Quaternion.Euler(0, i * 47, 0), new Vector3((1.2f + sq * 0.1f + wob) * k2, (1.05f - sq * 0.16f) * k2, 1.2f * k2));
                }
                // landing, lead-ins, home
                for (int j = 0; j < Extra; j++)
                {
                    int pi = baseP + _cap + j;
                    _puffSquash[pi] = Mathf.Max(0, _puffSquash[pi] - dt * 3.5f);
                    int p = j == 0 ? b.N : -j;
                    bool on = j <= 1 || b.Slot == 0;
                    if (!on) { _puffs.Matrix[pi] = hide; continue; }
                    float sq = _puffSquash[pi];
                    float w = j == 0 ? 1.35f : 1f;
                    _puffs.Matrix[pi] = Matrix4x4.TRS(new Vector3(b.X0 + p * Pitch, PuffTop - sq * 0.08f, 0), Quaternion.Euler(0, j * 71 + 20, 0), new Vector3(w * (1 + sq * 0.1f), 1 - sq * 0.16f, w));
                }
            }
            _cols.Flush(); _caps.Flush(); _glyphs.Flush(); _puffs.Flush();
        }

        static float BackOut(float t, float s) { t = Mathf.Clamp01(t) - 1; return t * t * ((s + 1) * t + s) + 1; }

        void UpdateChoir(float dt)
        {
            var shown = Shown;
            bool singing = shown != null && BeatIndex >= 0 && _now <= shown.WindowEnd;
            float beat = singing ? Mathf.Exp(-BeatPhase * 3f) : 0;
            _choirOpen = Damp(_choirOpen, 0, 1.5f, dt);
            _choirHop = Damp(_choirHop, 0, 1.2f, dt);
            if (_cheer > 0.95f) _choirHop = 1;
            float reduce = Settings.ReduceMotion ? 0.3f : 1;
            int k0 = Mathf.FloorToInt(_camX / ChoirPitch) - ChoirN / 2;
            for (int j = 0; j < ChoirN; j++)
            {
                int k = k0 + j;
                uint h = (uint)(k * 2654435761u);
                float r1 = (h & 1023) / 1023f, r2 = ((h >> 10) & 1023) / 1023f, r3 = ((h >> 20) & 1023) / 1023f;
                float x = k * ChoirPitch + (r1 - 0.5f) * 1.6f;
                float z = ChoirZ + r2 * 6f;
                float s = 1.45f + r3 * 0.35f;
                float bob = beat * 0.12f * reduce;
                float hop = _choirHop * Mathf.Abs(Mathf.Sin(_time * 7f + r1 * 6)) * 0.6f * reduce;
                float sway = Mathf.Sin(_time * 1.4f + r2 * 6) * 4f * reduce;
                var pos = new Vector3(x, SeaY + 1.6f + (z - ChoirZ) * 0.14f + bob + hop, z);
                var rot = Quaternion.Euler(0, (r1 - 0.5f) * 30, sway);
                var m = Matrix4x4.TRS(pos, rot, new Vector3(s, s * (1 - beat * 0.04f * reduce), s));
                _choir.Matrix[j] = m;
                var tint = Color.Lerp(new Color(1, 1, 1), r3 > 0.5f ? new Color(1f, 0.93f, 0.86f) : new Color(0.9f, 0.92f, 1f), Mathf.Abs(r3 - 0.5f) * 2);
                _choir.Tint[j] = tint.linear;
                float open = singing ? 0.45f + beat * 0.6f : 0.25f;
                open = Mathf.Max(open, _choirOpen * 1.5f, _choirHop * 1.3f);
                _mouths.Matrix[j] = m * Matrix4x4.TRS(new Vector3(0, 0.98f, -0.95f), Quaternion.identity, new Vector3(1, open, 1));
                _mouths.Tint[j] = Color.white;
            }
            _choir.Flush(); _mouths.Flush();
        }

        void UpdateWorld()
        {
            var cam = Look.Rig?.Camera;
            if (!cam) return;
            var cp = cam.transform.position;
            _seaFloor.localPosition = new Vector3(cp.x, SeaY, 100);
            for (int L = 0; L < 3; L++)
            {
                float W = LayerW[L];
                float c0 = Mathf.Floor(cp.x / W) * W;
                for (int c = 0; c < 3; c++) _layers[L][c].localPosition = new Vector3(c0 + (c - 1) * W, L == 0 ? 0 : 0, 0);
            }
            var sun = Look.Palette.Live[PD.SunDir];
            var d = new Vector3(sun.x, sun.y, -sun.z).normalized;
            _glory.position = cp + d * 150f;
            _glory.rotation = Quaternion.LookRotation(d);
            var sc = Look.Palette.Live[PC.Sun];
            _gloryMat.SetColor("_Color", sc);
        }

        void UpdateBeatLook()
        {
            // the surfaces breathe on every beat alike (the bar is the player's to find)
            Look.BeatPulse = BeatIndex >= 0 && Shown != null && _now <= Shown.WindowEnd ? Mathf.Exp(-5f * BeatPhase) * 0.6f : 0;
            if (BeatIndex != _lastPushBeat && BeatIndex >= 0) { _lastPushBeat = BeatIndex; Look.Rig.Beat(1); }
        }
        int _lastPushBeat = -1;

        // ------------------------------------------------------------ camera

        const float CamDist = 13f, CamHeight = 0.75f, CamYaw = -0.24f, CamFov = 36f;

        void Frame(bool snap, float dt = 0)
        {
            float tx = _jx + 3.4f, ty = 0.95f + Mathf.Max(0, _jy - 1.3f) * 0.4f;
            if (snap) { _camX = tx; _camY = ty; }
            else { _camX = Damp(_camX, tx, 3.2f, dt); _camY = Damp(_camY, ty, 2.5f, dt); }

            // reveal: pull out to the whole stair, hold, ease back in
            float want = 0;
            if (_overviewSlot >= 0 && _overviewHold > 0) { want = 1; _overviewHold -= dt; }
            if (_finale > 0) want = 1;
            _overview = snap ? want : Damp(_overview, want, want > _overview ? 2.6f : 1.6f, dt);
            float dist = CamDist, height = CamHeight, x = _camX, y = _camY;
            int os = _finale > 0 ? _round.Slots.Count - 1 : _overviewSlot;
            if (os >= 0 && _overview > 0.001f)
            {
                int n = _round.Slots[os].Beats.Count;
                // up to 16 steps: the whole stair of a short excerpt, the stretch around the jumper on a long one
                int win = Mathf.Min(n, 16);
                int first = Mathf.Clamp(Mathf.RoundToInt((_jx - _slotX[os]) / Pitch) - 3, 0, n - win);
                if (_finale > 0) first = n - win;
                float span = (win - 1) * Pitch;
                float cx = _slotX[os] + (first * Pitch) + span * 0.5f;
                float half = span * 0.5f + 1.4f;
                float hfov = 2 * Mathf.Atan(Mathf.Tan(CamFov * 0.5f * Mathf.Deg2Rad) * 16f / 9f);
                float need = Mathf.Clamp(half / Mathf.Tan(hfov * 0.5f) * 1.02f, CamDist, 26f);
                float k = Mathf.SmoothStep(0, 1, _overview);
                x = Mathf.Lerp(_camX, cx + 0.8f, k);
                dist = Mathf.Lerp(CamDist, need, k);
                height = Mathf.Lerp(CamHeight, CamHeight + (need - CamDist) * 0.12f, k);
                y = Mathf.Lerp(_camY, 0.95f, k);
            }
            Look.Rig.Frame(new Vector3(x, y, 0.4f), dist, height, CamYaw, CamFov, 7f, snap);
        }
    }
}
