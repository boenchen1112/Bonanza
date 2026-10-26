// Bounce Brigade: the stage. A stair of marble columns rising out of a sea of cloud, the sky changing with the level
// (dawn, day, vespers), a cloud choir on a bank behind the path, and the jumper (a little cantor with a halo) hopping
// one step per beat.
//
//   path     every excerpt lays one column per beat along +x, then a landing cloud and a lead-in cloud before the
//            next excerpt, so the climb is one continuous journey across the six. Hops: one per beat on the
//            excerpt's own grid, two between excerpts (landing, lead-in) spread over the gap.
//   steps    a fluted column per beat (gold band, echinus, a cloud skirt where it leaves the sea) carrying a plate
//            at the beat's real height (heavier = lower: 1 3 2 3 / 1 3 3 / 1 3) in the beat's key colour with its
//            arrow on the front. Until the reveal no plate is shown: every column ends in one glowing veil cloud at a
//            common height and the jumper hops on the veils, so nothing tells a downbeat from another beat. On the
//            reveal a wipe runs left to right: each veil bursts into sparkles, its plate pops up with an overshoot,
//            flashes its colour under a shaft of light; the downbeat plates keep a gold shaft so the bars land.
//   world    sky dome and light from the palette (playfield clouds brightest, far clouds and fog a value step
//            darker); a cloud-sea floor and three chunked cloud layers that tile along x, rotundas whose lanterns
//            light at vespers, stars at vespers, a glory around the sun, and a choir of cloud singers on one tier,
//            bobbing on every beat alike, singing and cheering a right answer.
//   camera   follows the jumper close (it fills about a sixth of the frame); on the reveal it pulls out to show
//            the stair, then eases back in.
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
        const float PuffTop = 0.72f;        // the veil clouds' top: the jumper's floor before the reveal
        const float SeaY = -0.95f;          // the cloud-sea floor
        const float SkirtY = SeaY + 0.32f;  // where a column leaves the sea (its cloud skirt)
        const int Banks = 3;                // slot k's steps live in bank k % 3 (one behind, one in play, one ahead)
        const int Extra = 4;                // puffs per bank beyond the steps: landing, lead-in, second lead-in, home
        const float CapW = 1.16f, CapH = 0.42f;
        const float JumperScale = 1.32f;
        // The step pieces are pooled: only the steps within PoolRange of the camera are drawn (at most Pool of them),
        // the per-step state stays indexed by bank and beat.
        const int Pool = 30;
        const int VeilPool = 18;   // veils are only on screen before a reveal, when the camera is close: fewer are needed
        const float PoolRange = 22f;
        static float StepTop(int h) => 0.08f + (h - 1) * 0.62f;   // 1 -> 0.08, 2 -> 0.70, 3 -> 1.32

        struct Hop { public double T; public int Slot, P; }

        sealed class Bank
        {
            public int Slot = -1, N;
            public float X0;
            public bool Revealed;
            public float Age;   // scene seconds since the reveal began
            public float Step = 0.06f;   // seconds between neighbouring steps of the wipe
            public bool[] Arrived = new bool[0], Burst = new bool[0];
        }

        readonly Bank[] _banks = { new Bank(), new Bank(), new Bank() };
        Hop[] _hops = new Hop[0];
        int _hopN, _hopAt;
        readonly float[] _slotX = new float[HymnRound.ExcerptCount];
        int _cap;
        MeshBatch _cols, _caps, _glyphs, _puffs, _veilGlow, _skirts, _rays, _choir, _mouths;
        float[] _stepPop, _puffSquash, _stepFlash;
        bool _batchDirty = true;

        readonly System.Collections.Generic.List<Object> _owned = new System.Collections.Generic.List<Object>();
        Material _veilMat, _cloudMat, _stoneMat, _capMat, _glyphMat, _seaMat, _farMat, _horizonMat, _haloMat, _gloryMat, _choirMat, _mouthMat, _blobMat,
                 _veilGlowMat, _rayMat, _lanternMat, _starMat, _markMat, _ledgeMat;
        readonly Transform[] _ledge = new Transform[3];
        Transform _seaFloor, _glory, _stars, _mark;
        readonly Transform[][] _layers = new Transform[3][];
        static readonly float[] LayerW = { 48, 110, 220 };
        float _night, _markGlow;

        // the jumper
        MeshRenderer _jBlobR;
        Transform _jRoot, _jBody, _jHalo, _jWingL, _jWingR, _jMouth, _jBlob, _jEyes;
        MaterialPropertyBlock _blobMpb;
        TrailHandle _trail;
        float _jx, _jy, _jSurf, _jLand, _jSing, _cheer, _sad, _jPrevAir, _haloBounce, _haloY = 1.1f;
        int _lastLandHop = -1;

        // camera
        float _camX, _camY, _overview, _overviewHold, _finale;
        int _overviewSlot = -1;
        float _time, _sweep;
        Transform _sweepR;
        Material _sweepMat;

        // choir: singers recycled along x at a fixed world pitch, on one tier behind the stair
        const int ChoirN = 8;
        const float ChoirPitch = 8.4f, ChoirZ = 26f, ChoirY = SeaY + 2.5f;
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

        Material GlowCard(Color c, float intensity, float core = 1.2f)
        {
            var m = Own(new Material(EnvKit.GlowMat));
            m.SetColor("_Color", c);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Billboard", 0);
            m.SetFloat("_Core", core);
            m.SetVector("_Stretch", new Vector4(1, 1, 0, 0));
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
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt((0.55f + v * 0.45f) * 255), 0, 255);
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
            PostState.EdgeAA = 1;   // soften the clouds' silhouettes (composite edge smoothing, this game only)
            var fx = Look.Fx;
            fx.SetGroundY(PuffTop);
            fx.SetFocus(new Vector3(0, 1, 0));

            // Playfield clouds take the palette's cloud-top tint (GroundAlt), far clouds and the sea a darker tone
            // (Band, BandAlt): the stair's band is always the brightest value in the frame.
            _cloudMat = Surf(Color.white, rim: 0.9f, pulse: 0.03f, lift: 0.13f, bands: 3, softness: 0.45f);
            _veilMat = Surf(Color.white, rim: 2.3f, pulse: 0.12f, lift: 0.46f, bands: 3, softness: 0.55f, fog: 0.3f);   // a soft rim of light
            _stoneMat = Surf(Color.white, rim: 0.9f, pulse: 0.04f, lift: 0.02f, bands: 3, softness: 0.18f);
            _stoneMat.SetFloat("_ShadowRecv", 0);   // fluted shafts would stripe with acne; the plates and clouds take the shadow
            _capMat = Surf(Color.white, rim: 1.1f, pulse: 0.1f, lift: 0.16f, bands: 3, softness: 0.2f);
            _glyphMat = Add(0);
            var noise = Own(NoiseTex(128, 0xb0b0));
            _seaMat = Surf(Color.white, rim: 0.3f, pulse: 0.02f, lift: 0.04f, bands: 2, softness: 0.6f, map: noise, tiling: new Vector2(14, 6), mapStrength: 0.6f);
            _farMat = Surf(Color.white, rim: 2.0f, pulse: 0, lift: 0.06f, bands: 3, softness: 0.35f, fog: 0.85f);   // silhouettes with a rim of light
            _horizonMat = Surf(Color.white, rim: 1.6f, pulse: 0, lift: 0.12f, bands: 2, softness: 0.6f, fog: 1f);   // atmospheric falloff
            _ledgeMat = Surf(Color.white, rim: 1.2f, pulse: 0.02f, lift: 0.02f, bands: 3, softness: 0.45f, fog: 0);
            _choirMat = Surf(Color.white, rim: 1.6f, pulse: 0.05f, lift: 0.32f, bands: 3, softness: 0.55f, fog: 0.45f);
            _mouthMat = Surf(Color.white, rim: 0, pulse: 0, lift: 0, bands: 1, softness: 0.2f, fog: 0.6f);
            Look.Track(_cloudMat, PC.GroundAlt, 0.96f);
            Look.Track(_choirMat, PC.GroundAlt, 1f);
            Look.Track(_seaMat, PC.Band, 0.95f);
            Look.Track(_farMat, PC.Band, 1.05f);
            Look.Track(_horizonMat, PC.BandAlt, 1.1f);
            Look.Track(_ledgeMat, PC.Band, 1.0f);

            BuildSea();
            BuildLayers();
            BuildLedge();
            BuildGlory();
            BuildStars();
            BuildSweep();
            BuildSteps();
            BuildChoir();
            BuildJumper();
            // the key's real shadow around the jumper (High / Medium): the jumper and the columns cast it
            Look.ShadowFocus(new Vector3(0, 0.5f, 0), 9f);
            LookRig.ShadowCaster(_jRoot.gameObject);
            _cols.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _caps.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            fx.Prewarm();   // every FX material and the callout font now: a first-use shader link mid-round would push a press out of its window
        }

        void DestroyStage()
        {
            PostState.EdgeAA = 0;
            _trail?.Release();
            _trail = null;
            foreach (var o in _owned) if (o) Destroy(o);
            _owned.Clear();
            foreach (var b in new[] { _cols, _caps, _glyphs, _puffs, _veilGlow, _skirts, _rays, _choir, _mouths }) if (b != null && b.Mesh) Destroy(b.Mesh);
        }

        /// A cloud puff cluster with its top near y = 0: one wide body and some lobes. Vertex colour darkens toward
        /// the underside (a baked occlusion the toon bands then light): the underside reads as the cloud's shadow.
        static void Puff(MeshBuilder b, Matrix4x4 m, float w, int ws = 10, int hs = 7, uint seed = 1, float tone = 1, int lobes = 4)
        {
            var rng = new FxRng(seed);
            int start = b.Count;
            b.Sphere(0.62f * w, ws, hs, m * Matrix4x4.Translate(new Vector3(0, -0.4f * w, 0)), Color.white, new Vector3(1.22f, 0.7f, 1.06f));
            for (int k = 0; k < lobes; k++)
            {
                float a = k * Mathf.PI * 2 / lobes + rng.Next() * 0.9f;
                float r = (0.36f + rng.Next() * 0.12f) * w;
                var c = new Vector3(Mathf.Cos(a) * 0.46f * w, -0.4f * w - rng.Next() * 0.1f * w, Mathf.Sin(a) * 0.36f * w);
                b.Sphere(r, ws - 2, hs - 1, m * Matrix4x4.Translate(c), Color.white, new Vector3(1.1f, 0.85f, 1));
            }
            Shade(b, start, tone);
        }

        static void Shade(MeshBuilder b, int start, float tone, float floor = 0.52f)
        {
            for (int v = start; v < b.Count; v++)
            {
                float k = Mathf.Lerp(floor, 1.02f, Mathf.SmoothStep(0, 1, Mathf.Clamp01(b.N[v].y * 0.65f + 0.5f))) * tone;
                b.C[v] = new Color(k, k, k * 1.03f, 1);
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
        /// (kept low under and in front of the stair, a bank rising behind it that the choir stands on), towers and
        /// rotundas behind, and long far strata.
        void BuildLayers()
        {
            var rng = new FxRng(0x5ea5);
            var lanterns = new MeshBuilder();
            for (int L = 0; L < 3; L++)
            {
                float W = LayerW[L];
                var b = new MeshBuilder();
                if (L == 0)
                {
                    for (int i = 0; i < 20; i++)
                    {
                        float x = rng.Next() * W, z;
                        float w, top;
                        int kind = 1 + i % 2;   // nothing in front of the stair: the near field stays the plain sea
                        if (kind == 0) { z = -2.2f - rng.Next() * 2.6f; w = 1.0f + rng.Next() * 1.0f; top = SeaY + 0.05f + rng.Next() * 0.2f; }   // in front: small and low, below the stair (never near the lens)
                        else if (kind == 1) { z = 3f + rng.Next() * 6f; w = 1.2f + rng.Next() * 1.4f; top = SeaY + 0.12f + rng.Next() * 0.25f; }   // just behind: low, so the step row stands clear
                        else { z = ChoirZ + 1.5f + rng.Next() * 5f; w = 2.6f + rng.Next() * 2.4f; top = ChoirY + 0.05f + rng.Next() * 0.4f; }   // the choir's bank
                        Puff(b, Matrix4x4.TRS(new Vector3(x, top, z), Quaternion.Euler(0, rng.Next() * 360, 0), Vector3.one), w, 9, 6, (uint)(i + 11), 0.97f + rng.Next() * 0.05f, 3);
                    }
                }
                else if (L == 1)
                {
                    // little rotundas on far clouds: columns, a gilt dome, and a lantern glow inside (lit at vespers)
                    Rotunda(b, lanterns, new Vector3(W * 0.58f, 1.9f, 60f), 2.4f);
                    Rotunda(b, lanterns, new Vector3(W * 0.08f, 2.6f, 80f), 3.0f);
                    for (int i = 0; i < 10; i++)
                    {
                        float x = (i + rng.Next() * 0.6f) / 10 * W, z = 38 + rng.Next() * 28;
                        float h = 3 + rng.Next() * 4.5f, w = 4 + rng.Next() * 4;   // lower banks: a tower never hangs as loose blobs above the horizon
                        int n = 4 + (int)(rng.Next() * 3);
                        int start = b.Count;
                        for (int k = 0; k < n; k++)
                        {
                            float t = k / (float)(n - 1);
                            float rr = w * Mathf.Lerp(0.62f, 0.3f, t) * (0.85f + rng.Next() * 0.3f);
                            var c = new Vector3(x + (rng.Next() - 0.5f) * w * 0.4f, -1.5f + t * h, z + (rng.Next() - 0.5f) * 3);
                            b.Sphere(rr, 9, 6, Matrix4x4.Translate(c), Color.white, new Vector3(1.15f, 0.9f, 1));
                        }
                        Shade(b, start, 0.78f + rng.Next() * 0.36f, 0.45f);   // three tints of bank: some lit, some in shade
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
                            b.Sphere(w * Mathf.Lerp(0.55f, 0.28f, t), 9, 5, Matrix4x4.Translate(c), Color.white, new Vector3(1.6f, 0.62f, 1));
                        }
                        Shade(b, start, 1, 0.5f);
                    }
                    // the farthest layer: faint cloud towers on the horizon, almost lost in the air
                    for (int i = 0; i < 4; i++)
                    {
                        float x = (i + rng.Next() * 0.6f) / 4 * W, z = 175 + rng.Next() * 25;
                        float h = 22 + rng.Next() * 16, w = 10 + rng.Next() * 8;
                        int start = b.Count;
                        for (int k = 0; k < 4; k++)
                        {
                            float t = k / 3f;
                            b.Sphere(w * Mathf.Lerp(0.7f, 0.35f, t), 8, 5, Matrix4x4.Translate(new Vector3(x + (rng.Next() - 0.5f) * w * 0.5f, -4 + t * h, z)), Color.white, new Vector3(1.1f, 0.8f, 1));
                        }
                        Shade(b, start, 1.05f, 0.6f);
                    }
                }
                var mesh = Own(b.ToMesh("CloudLayer" + L));
                mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + new Vector3(8, 4, 4));
                var mat = L == 0 ? _cloudMat : L == 1 ? _farMat : _horizonMat;
                Mesh lmesh = L == 1 ? Own(lanterns.ToMesh("Lanterns")) : null;
                if (L == 1)
                {
                    _lanternMat = Add(1.2f, 0);
                    _lanternMat.SetColor("_Color", new Color(1f, 0.62f, 0.26f));
                    _lanternMat.SetFloat("_Intensity", 0);
                }
                _layers[L] = new Transform[3];
                for (int c = 0; c < 3; c++)
                {
                    var rr = EnvKit.Renderer("CloudLayer" + L + "_" + c, transform, mesh, mat);
                    rr.receiveShadows = false;
                    _layers[L][c] = rr.transform;
                    if (lmesh != null) EnvKit.Renderer("Lanterns", rr.transform, lmesh, _lanternMat);
                }
            }
        }

        /// A near cloud ledge across the bottom of the frame, in shadow (the darker band tone): it frames the stair
        /// from below and fills the empty foreground. One 48-unit chunk tiled three times like the sea's puffs.
        void BuildLedge()
        {
            var rng = new FxRng(0x1ed9e);
            var b = new MeshBuilder();
            for (int i = 0; i < 11; i++)
            {
                float x = (i + rng.Next() * 0.7f) / 11 * LayerW[0];
                float z = -4.2f - rng.Next() * 1.6f, w = 1.15f + rng.Next() * 0.9f;
                Puff(b, Matrix4x4.TRS(new Vector3(x, SeaY + 0.1f + rng.Next() * 0.2f, z), Quaternion.Euler(0, rng.Next() * 360, 0), Vector3.one), w, 10, 6, (uint)(i + 501), 0.85f, 4);
            }
            var mesh = Own(b.ToMesh("Ledge"));
            mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + new Vector3(8, 4, 4));
            for (int c = 0; c < 3; c++)
            {
                var r = EnvKit.Renderer("Ledge_" + c, transform, mesh, _ledgeMat);
                r.receiveShadows = false;
                _ledge[c] = r.transform;
            }
        }

        /// The glory: a soft halo of light around the sun (additive, fog-free, kept faint: the sky draws the disc).
        void BuildGlory()
        {
            _gloryMat = Own(new Material(EnvKit.GlowMat));
            _gloryMat.SetColor("_Color", new Color(1f, 0.93f, 0.8f));
            _gloryMat.SetFloat("_Intensity", 0.07f);
            _gloryMat.SetFloat("_Billboard", 1);
            _gloryMat.SetFloat("_Core", 1.4f);
            var b = new MeshBuilder();
            b.Surface(1, 1, (u, v) => (new Vector3(u - 0.5f, v - 0.5f, 0), Vector3.back), Matrix4x4.identity, Color.white, Vector2.one);
            var r = Part("Glory", transform, b, _gloryMat, Vector3.zero);
            _glory = r.transform;
            _glory.localScale = new Vector3(40, 40, 1);
        }

        /// The first stars of vespers: tiny additive points on the upper sky, camera-locked, lit only on Hard.
        void BuildStars()
        {
            var rng = new FxRng(0x57a2);
            var b = new MeshBuilder();
            for (int i = 0; i < 140; i++)
            {
                float az = (rng.Next() - 0.5f) * 2.4f;     // mostly in front of the camera
                float el = Mathf.Lerp(0.07f, 0.42f, Mathf.Pow(rng.Next(), 0.9f));
                var d = new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
                float s = 0.35f + rng.Next() * rng.Next() * 1.1f;
                float k = 0.4f + rng.Next() * 0.6f;
                b.Octahedron(Matrix4x4.TRS(d * 260f, Quaternion.Euler(rng.Next() * 90, rng.Next() * 90, 0), Vector3.one * s), new Color(k, k * 0.95f, k * 1.05f));
            }
            _starMat = Add(0, 0);
            _starMat.SetColor("_Color", new Color(1f, 0.95f, 0.85f));
            _starMat.SetFloat("_Intensity", 0);
            var r = Part("Stars", transform, b, _starMat, Vector3.zero);
            _stars = r.transform;
        }

        /// One tall soft beam that runs along the stair with the reveal's wipe.
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
            _sweepR = EnvKit.Renderer("Sweep", transform, mesh, _sweepMat).transform;
            _sweepR.localScale = new Vector3(1.6f, 7f, 1);
            _sweepR.gameObject.SetActive(false);
        }

        void UpdateSweep(float dt)
        {
            bool on = _sweep > 0.01f && !Settings.ReduceMotion && _overviewSlot >= 0;
            if (_sweepR.gameObject.activeSelf != on) _sweepR.gameObject.SetActive(on);
            if (!on) { _sweep = 0; return; }
            var b = BankOf(_overviewSlot);
            if (b == null) { _sweep = 0; return; }
            float front = b.Age / b.Step;   // steps the wipe has travelled
            _sweepR.localPosition = new Vector3(b.X0 + Mathf.Min(b.N - 1, front) * Pitch, 1.8f, -0.4f);
            _sweep = Mathf.Max(0, _sweep - dt * 0.8f);
            _sweepMat.SetFloat("_Intensity", 0.45f * Mathf.Sin(Mathf.Clamp01(_sweep) * Mathf.PI));
        }

        static void Rotunda(MeshBuilder b, MeshBuilder glow, Vector3 at, float k)
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
            Shade(b, start, 1, 0.45f);
            b.Shell(1.35f * k, 1.45f * k, 0.25f * k, 24, 0, Mathf.PI * 2, Matrix4x4.Translate(at + new Vector3(0, 0.12f * k, 0)), ivory, Vector2.one);
            b.Disc(1.35f * k, 24, Matrix4x4.Translate(at + new Vector3(0, 0.25f * k, 0)), ivory);
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2;
                b.Shell(0.09f * k, 0.11f * k, 1.4f * k, 8, 0, Mathf.PI * 2, Matrix4x4.Translate(at + new Vector3(Mathf.Cos(a) * 1.1f * k, 0.95f * k, Mathf.Sin(a) * 1.1f * k)), ivory, Vector2.one);
                // a lantern hung between each pair of columns
                float a2 = (i + 0.5f) / 8f * Mathf.PI * 2;
                glow.Sphere(0.13f * k, 8, 6, Matrix4x4.Translate(at + new Vector3(Mathf.Cos(a2) * 1.1f * k, 1.35f * k, Mathf.Sin(a2) * 1.1f * k)), Color.white, Vector3.one);
            }
            // the lit interior: a warm core between the columns
            glow.Sphere(0.85f * k, 12, 8, Matrix4x4.Translate(at + new Vector3(0, 0.95f * k, 0)), new Color(0.55f, 0.5f, 0.45f), new Vector3(1, 0.9f, 1));
            b.Shell(1.25f * k, 1.25f * k, 0.22f * k, 24, 0, Mathf.PI * 2, Matrix4x4.Translate(at + new Vector3(0, 1.75f * k, 0)), ivory, Vector2.one);
            int d = b.Count;
            b.Sphere(1.18f * k, 16, 8, Matrix4x4.Translate(at + new Vector3(0, 1.85f * k, 0)), gilt, new Vector3(1, 0.8f, 1));
            for (int v = d; v < b.Count; v++) if (b.P[v].y < at.y + 1.85f * k) b.P[v] = new Vector3(b.P[v].x, at.y + 1.85f * k, b.P[v].z);   // a dome: flatten the lower half into the drum
            b.Sphere(0.12f * k, 8, 6, Matrix4x4.Translate(at + new Vector3(0, 2.85f * k, 0)), gilt, Vector3.one);
        }

        /// A light cone in the XY plane facing -z: half width w0 at its foot (y = 0), w1 at the top (y = h). Seven
        /// columns of vertices whose colour falls from white in the middle to black at both edges (soft sides); uv.y is
        /// 1 at the foot and 0 at the top (GlowAdd's ramp fades it upward).
        static void RayCone(MeshBuilder b, float w0, float w1, float h)
        {
            const int cols = 6, rows = 4;
            int o = b.Count;
            for (int j = 0; j <= rows; j++)
                for (int i = 0; i <= cols; i++)
                {
                    float u = i / (float)cols, v = j / (float)rows;
                    float half = Mathf.Lerp(w0, w1, v);
                    float k = 1 - Mathf.Abs(u * 2 - 1);
                    k = k * k * (3 - 2 * k);
                    b.P.Add(new Vector3((u * 2 - 1) * half, v * h, 0)); b.N.Add(Vector3.back);
                    b.C.Add(new Color(k, k, k, 1)); b.UV.Add(new Vector2(u, 1 - v));
                }
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    int a = o + j * (cols + 1) + i;
                    b.T.Add(a); b.T.Add(a + cols + 1); b.T.Add(a + 1);
                    b.T.Add(a + 1); b.T.Add(a + cols + 1); b.T.Add(a + cols + 2);
                }
        }

        /// A soft glow card in the XY plane (uv 0..1), centred, facing -z.
        static void Card(MeshBuilder b, Matrix4x4 m)
        {
            b.Surface(1, 1, (u, v) => (new Vector3(u - 0.5f, v - 0.5f, 0), Vector3.back), m, Color.white, Vector2.one);
        }

        void BuildSteps()
        {
            _cap = 8;
            foreach (int n in _beatCounts) _cap = Mathf.Max(_cap, n);
            int total = Banks * _cap;

            // column (its origin is the plate's top): abacus, echinus, a gold astragal, a fluted shaft that fades from
            // lit ivory under the capital to a shadowed foot down in the cloud, and a second gold band.
            var col = new MeshBuilder();
            var ivory = new Color(0.95f, 0.9f, 0.82f);
            var gold = new Color(1.0f, 0.7f, 0.24f);
            float y0 = -CapH;
            col.Box(Matrix4x4.TRS(new Vector3(0, y0 - 0.05f, 0), Quaternion.identity, new Vector3(0.98f, 0.1f, 0.98f)), ivory * 1.02f);
            col.Shell(0.47f, 0.35f, 0.18f, 14, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(0, y0 - 0.19f, 0)), ivory, Vector2.one);
            col.Torus(0.37f, 0.05f, 5, 16, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(0, y0 - 0.31f, 0), Quaternion.Euler(90, 0, 0), Vector3.one), gold);
            float shaftTop = y0 - 0.31f, shaftLen = 7.5f;
            int s0 = col.Count;
            col.Surface(24, 5, (u, v) =>
            {
                float a = u * Mathf.PI * 2;
                const float flutes = 12, depth = 0.026f;
                v = v * v;   // rows packed under the capital, where the light falls off
                float r = 0.35f - depth * (0.5f + 0.5f * Mathf.Cos(a * flutes)) + 0.02f * v;   // a touch of entasis toward the foot
                float dr = depth * 0.5f * flutes * Mathf.Sin(a * flutes);
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var t = new Vector3(Mathf.Cos(a), 0, -Mathf.Sin(a));
                var n = (d * r - t * dr).normalized;
                return (d * r + Vector3.up * (shaftTop - v * shaftLen), n);
            }, Matrix4x4.identity, ivory, Vector2.one);
            for (int v = s0; v < col.Count; v++)
            {
                float below = shaftTop - col.P[v].y;
                float k = Mathf.Lerp(1f, 0.42f, Mathf.SmoothStep(0, 1, Mathf.Clamp01(below / 3.2f)));
                col.C[v] = new Color(ivory.r * k, ivory.g * k, ivory.b * k * 1.06f, 1);
            }
            col.Torus(0.37f, 0.035f, 4, 16, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(0, shaftTop - 0.75f, 0), Quaternion.Euler(90, 0, 0), Vector3.one), gold * 0.85f);
            _cols = new MeshBatch("Columns", transform, col, Pool, _stoneMat);

            // plate: the coloured slab (tinted by the key), a lighter inset on top, a darker foot band, gold corner trim
            var cap = new MeshBuilder();
            cap.Box(Matrix4x4.TRS(new Vector3(0, -CapH / 2, 0), Quaternion.identity, new Vector3(CapW, CapH - 0.06f, CapW)), Color.white);
            cap.Box(Matrix4x4.TRS(new Vector3(0, -0.015f, 0), Quaternion.identity, new Vector3(CapW - 0.1f, 0.04f, CapW - 0.1f)), new Color(1.22f, 1.22f, 1.22f));
            cap.Box(Matrix4x4.TRS(new Vector3(0, -CapH + 0.03f, 0), Quaternion.identity, new Vector3(CapW + 0.06f, 0.07f, CapW + 0.06f)), new Color(0.45f, 0.45f, 0.45f));
            _caps = new MeshBatch("Plates", transform, cap, Pool, _capMat);

            // the key's arrow on the plate's front face (points +x; each instance turns it)
            var glyph = new MeshBuilder();
            Arrow(glyph, 0.16f);
            _glyphs = new MeshBatch("Glyphs", transform, glyph, Pool, _glyphMat);
            _glyphs.Renderer.receiveShadows = false;

            // the shaft of light a plate rises in: a soft cone facing the camera, bright at the plate and fading to
            // nothing at its top and at both sides (vertex colour across, the material's ramp up)
            var ray = new MeshBuilder();
            RayCone(ray, 0.5f, 1.15f, 3.2f);
            _rayMat = Add(0, 0.3f);
            _rayMat.SetFloat("_Ramp", 1);
            _rays = new MeshBatch("Rays", transform, ray, Pool, _rayMat);

            // the veil clouds (one per step) and per bank: landing, lead-in, second lead-in, home
            var puff = new MeshBuilder();
            int pv = puff.Count;
            Puff(puff, Matrix4x4.identity, 1.0f, 15, 9, 7, 1, 3);
            // a cumulus crown: round lobes on top, so a veil reads as a cloud, not a bun (finer meshes: they sit in front)
            puff.Sphere(0.34f, 12, 8, Matrix4x4.Translate(new Vector3(-0.26f, -0.38f, -0.05f)), Color.white, Vector3.one);
            puff.Sphere(0.4f, 12, 8, Matrix4x4.Translate(new Vector3(0.12f, -0.36f, 0.02f)), Color.white, Vector3.one);
            Shade(puff, pv, 1, 0.74f);   // a veil glows: a lighter underside than the sea's clouds
            _puffs = new MeshBatch("Veil", transform, puff, VeilPool + Banks * Extra, _veilMat);
            _puffs.Renderer.receiveShadows = true;
            // and a soft halo behind each veil cloud: the veils glow and their silhouettes melt into the air
            var halo = new MeshBuilder();
            Card(halo, Matrix4x4.identity);
            _veilGlowMat = GlowCard(new Color(1f, 0.97f, 0.92f), 0.42f, 0.9f);
            _veilGlow = new MeshBatch("VeilGlow", transform, halo, VeilPool, _veilGlowMat);

            // the cloud skirt where each column leaves the sea
            var skirt = new MeshBuilder();
            int k0 = skirt.Count;
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2 + 0.3f;
                skirt.Sphere(0.42f + (i % 2) * 0.08f, 9, 6, Matrix4x4.Translate(new Vector3(Mathf.Cos(a) * 0.55f, -0.12f * (i % 2), Mathf.Sin(a) * 0.45f)), Color.white, new Vector3(1.2f, 0.72f, 1f));
            }
            Shade(skirt, k0, 1, 0.45f);
            _skirts = new MeshBatch("Skirts", transform, skirt, Pool, _cloudMat);
            _skirts.Renderer.receiveShadows = true;

            _stepPop = new float[total];
            _stepFlash = new float[total];
            _puffSquash = new float[Banks * (_cap + Extra)];
            HideAll();
        }

        void HideAll()
        {
            var hide = Matrix4x4.TRS(new Vector3(0, -999, 0), Quaternion.identity, Vector3.zero);
            for (int i = 0; i < Pool; i++)
            {
                _cols.Matrix[i] = hide; _caps.Matrix[i] = hide; _glyphs.Matrix[i] = hide; _glyphs.Tint[i] = Color.black;
                _rays.Matrix[i] = hide; _rays.Tint[i] = Color.black; _skirts.Matrix[i] = hide;
            }
            for (int i = 0; i < _veilGlow.Matrix.Length; i++) _veilGlow.Matrix[i] = hide;
            for (int i = 0; i < _puffs.Matrix.Length; i++) _puffs.Matrix[i] = hide;
            _cols.Flush(); _caps.Flush(); _glyphs.Flush(); _puffs.Flush(); _rays.Flush(); _skirts.Flush(); _veilGlow.Flush();
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

        /// The cloud choir: a singer is a puffy body, a round head, two eyes and cheeks, an open crimson hymnal (one
        /// batch, vertex colours), and a mouth (its own batch: it opens with the music).
        void BuildChoir()
        {
            var b = new MeshBuilder();
            int s0 = b.Count;
            // a cloud: one round body with three bumps on top, the face on its front
            b.Sphere(1.1f, 18, 12, Matrix4x4.Translate(new Vector3(0, 1.0f, 0)), Color.white, new Vector3(1.3f, 0.85f, 0.9f));
            b.Sphere(0.55f, 12, 8, Matrix4x4.Translate(new Vector3(-0.62f, 1.62f, 0.1f)), Color.white, Vector3.one);
            b.Sphere(0.68f, 14, 9, Matrix4x4.Translate(new Vector3(0.08f, 1.82f, 0.12f)), Color.white, Vector3.one);
            b.Sphere(0.5f, 12, 8, Matrix4x4.Translate(new Vector3(0.7f, 1.55f, 0.1f)), Color.white, Vector3.one);
            b.Sphere(0.55f, 8, 5, Matrix4x4.Translate(new Vector3(-1.15f, 0.55f, 0.1f)), Color.white, Vector3.one);
            b.Sphere(0.5f, 8, 5, Matrix4x4.Translate(new Vector3(1.18f, 0.5f, 0.1f)), Color.white, Vector3.one);
            Shade(b, s0, 1, 0.5f);
            var ink = new Color(0.05f, 0.04f, 0.09f);
            b.Sphere(0.15f, 8, 6, Matrix4x4.Translate(new Vector3(-0.32f, 1.27f, -0.93f)), ink, new Vector3(0.85f, 1.3f, 0.5f));
            b.Sphere(0.15f, 8, 6, Matrix4x4.Translate(new Vector3(0.32f, 1.27f, -0.93f)), ink, new Vector3(0.85f, 1.3f, 0.5f));
            b.Sphere(0.035f, 5, 4, Matrix4x4.Translate(new Vector3(-0.3f, 1.33f, -0.99f)), new Color(3, 3, 3), Vector3.one);
            b.Sphere(0.035f, 5, 4, Matrix4x4.Translate(new Vector3(0.34f, 1.33f, -0.99f)), new Color(3, 3, 3), Vector3.one);
            var blush = new Color(1f, 0.45f, 0.58f);
            b.Sphere(0.19f, 8, 5, Matrix4x4.Translate(new Vector3(-0.58f, 1.04f, -0.86f)), blush, new Vector3(1.2f, 0.6f, 0.4f));
            b.Sphere(0.19f, 8, 5, Matrix4x4.Translate(new Vector3(0.58f, 1.04f, -0.86f)), blush, new Vector3(1.2f, 0.6f, 0.4f));
            // the hymnal, held open in both hands below the face: crimson boards and cream pages in a shallow V
            var crimson = new Color(0.62f, 0.06f, 0.12f);
            var gilt = new Color(1f, 0.72f, 0.26f);
            var page = new Color(1f, 0.96f, 0.86f);
            foreach (var sx in new[] { -1f, 1f })
            {
                var q = Quaternion.Euler(-14, sx * 24, 0);
                b.Box(Matrix4x4.TRS(new Vector3(sx * 0.24f, 0.46f, -1.1f), q, new Vector3(0.5f, 0.58f, 0.05f)), crimson);
                b.Box(Matrix4x4.TRS(new Vector3(sx * 0.23f, 0.47f, -1.14f), q, new Vector3(0.42f, 0.5f, 0.03f)), page);
                b.Box(Matrix4x4.TRS(new Vector3(sx * 0.24f, 0.2f, -1.12f), q, new Vector3(0.5f, 0.05f, 0.06f)), gilt);
            }
            // each singer rides its own little cloud (the choir drifts along with the view)
            Puff(b, Matrix4x4.TRS(new Vector3(0, 0.32f, 0.1f), Quaternion.identity, new Vector3(1, 0.7f, 1)), 2.3f, 13, 8, 77, 0.95f, 4);
            _choir = new MeshBatch("Choir", transform, b, ChoirN, _choirMat);
            _choir.Renderer.receiveShadows = false;
            var m = new MeshBuilder();
            m.Sphere(0.17f, 10, 6, Matrix4x4.identity, new Color(0.24f, 0.05f, 0.1f), new Vector3(1, 1, 0.45f));
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

            var skin = new Color(1f, 0.84f, 0.7f).linear;
            var robe = new Color(0.14f, 0.2f, 0.66f).linear;
            var trim = new Color(1f, 0.76f, 0.26f).linear;
            var white = new Color(0.97f, 0.97f, 1f).linear;
            var mat = Surf(Color.white, rim: 1.3f, pulse: 0.06f, lift: 0.04f, bands: 3, softness: 0.22f, fog: 0.3f);
            mat.SetFloat("_ShadowRecv", 0);   // a curved body casts but does not receive (no acne on the bean)

            // body: a bean, skin above the ruff, robe below with a gold hem
            var b = new MeshBuilder();
            const int rad = 24, rows = 18;
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
                f.Sphere(0.055f, 8, 6, Matrix4x4.Translate(new Vector3(sx * 0.1f, 0.68f, -0.255f)), ink, new Vector3(0.8f, 1.25f, 0.5f));
                f.Sphere(0.018f, 6, 4, Matrix4x4.Translate(new Vector3(sx * 0.1f + 0.014f, 0.705f, -0.284f)), new Color(3, 3, 3), Vector3.one);
                f.Sphere(0.05f, 8, 5, Matrix4x4.Translate(new Vector3(sx * 0.18f, 0.6f, -0.225f)), new Color(1f, 0.42f, 0.5f), new Vector3(1.3f, 0.7f, 0.4f));
            }
            _jEyes = Part("Face", _jBody, f, mat, Vector3.zero).transform;

            // mouth: opens as it sings
            var mo = new MeshBuilder();
            mo.Sphere(0.045f, 10, 6, Matrix4x4.identity, new Color(0.35f, 0.05f, 0.1f), new Vector3(1, 1, 0.5f));
            _jMouth = Part("Mouth", _jBody, mo, mat, new Vector3(0, 0.575f, -0.262f)).transform;

            // wings: little white leaves at the back
            var w = new MeshBuilder();
            w.Sphere(0.13f, 10, 6, Matrix4x4.Translate(new Vector3(0.11f, 0, 0)), white * 0.9f, new Vector3(1.3f, 0.75f, 0.3f));   // small and cream: folded close, never a pale ghost against the sky
            _jWingL = new GameObject("WingL").transform; _jWingL.SetParent(_jBody, false); _jWingL.localPosition = new Vector3(-0.2f, 0.42f, 0.2f);
            _jWingR = new GameObject("WingR").transform; _jWingR.SetParent(_jBody, false); _jWingR.localPosition = new Vector3(0.2f, 0.42f, 0.2f);
            var wm = Own(w.ToMesh("Wing"));
            EnvKit.Renderer("Wing", _jWingL, wm, mat).transform.localRotation = Quaternion.Euler(0, 0, 180);
            EnvKit.Renderer("Wing", _jWingR, wm, mat);

            // halo: a gold ring that floats over the head, lags a little and bounces on every landing
            _haloMat = Add(0.2f);
            _haloMat.SetColor("_Color", new Color(1f, 0.78f, 0.35f));
            _haloMat.SetFloat("_Intensity", 1.25f);
            var h = new MeshBuilder();
            h.Torus(0.2f, 0.03f, 6, 32, Mathf.PI * 2, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(90, 0, 0), Vector3.one), Color.white);
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

            // the step mark: a warm light pool on the step the jumper stands on (it flares on each landing, the same on every beat)
            var mk = new MeshBuilder();
            Card(mk, Matrix4x4.identity);
            _markMat = GlowCard(new Color(1f, 0.86f, 0.55f), 0.5f, 1.1f);
            var mr = Part("StepMark", transform, mk, _markMat, Vector3.zero);
            _mark = mr.transform;
            _mark.localRotation = Quaternion.Euler(90, 0, 0);
            _mark.localScale = new Vector3(1.9f, 1.9f, 1);

            _jRoot.localScale = Vector3.one * JumperScale;
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
            // no hop trail: at the apex it read as a smear
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
            bk.Step = Mathf.Min(0.07f, 1.5f / Mathf.Max(1, bk.N));
            _batchDirty = true;
        }

        float X(int slot, int p) => _slotX[slot] + p * Pitch;

        Bank BankOf(int slot) { var b = _banks[slot % Banks]; return b.Slot == slot ? b : null; }

        /// 0..1 rise of a revealed step's plate (the wipe runs left to right); Lift is its veil bursting just before.
        static float Rise(Bank b, int i) => b.Revealed ? Mathf.Clamp01((b.Age - i * b.Step - 0.08f) / 0.5f) : 0;
        static float Lift(Bank b, int i) => b.Revealed ? Mathf.Clamp01((b.Age - i * b.Step) / 0.22f) : 0;

        /// What the jumper lands on at (slot, p) right now.
        float Surface(int slot, int p)
        {
            var b = BankOf(slot);
            if (b == null || p < 0 || p >= b.N) return PuffTop;
            float r = Rise(b, p);
            if (r <= 0) return PuffTop;
            float top = StepTop(_round.Slots[slot].Beats[p].StepHeight);
            return Mathf.Lerp(PuffTop, top, Mathf.SmoothStep(0, 1, Mathf.Clamp01((r - 0.1f) / 0.6f)));
        }

        // ------------------------------------------------------------ events

        void StageRevealed(HymnSlot slot)
        {
            var b = BankOf(slot.Index);
            if (b == null) return;
            b.Revealed = true;
            b.Age = 0;
            if (b.Arrived.Length < _cap) { b.Arrived = new bool[_cap]; b.Burst = new bool[_cap]; }
            System.Array.Clear(b.Arrived, 0, b.Arrived.Length);
            System.Array.Clear(b.Burst, 0, b.Burst.Length);
            _sweep = 1;
            _overviewSlot = slot.Index;
            _overviewHold = Settings.ReduceMotion ? 0 : 2.6f;
            _choirOpen = 1;
            if (!Settings.ReduceMotion) Look.Rig.DoFlash(0.06f, new Color(1f, 0.97f, 0.9f));
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
                var p = new Vector3(_jx, _jy + 1.2f * JumperScale, 0);
                Look.Fx.Burst(p, 16, new Color(1f, 0.85f, 0.4f), 3.4f, 0.12f, 0.6f, -3f);
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
            UpdateWorld(dt);
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
                sy = 1 - land * 0.32f - crouch + idle;
                sx = 1 + land * 0.24f + crouch * 0.6f - idle * 0.5f;
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
                float apex = 0.7f + 0.4f * Mathf.Abs(s1 - s0);
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
            float flap = Mathf.Sin(_time * 11f) * 14f * _jPrevAir + 6;   // a slow, small flap (a fast one aliased into a ghost wing)
            _jWingL.localRotation = Quaternion.Euler(0, 55, -flap);
            _jWingR.localRotation = Quaternion.Euler(0, -55, flap);

            // halo lags the body and bounces on each landing
            _haloBounce = Mathf.Max(0, _haloBounce - dt * 3.2f);
            float hb = Settings.ReduceMotion ? 0 : Mathf.Sin(_haloBounce * Mathf.PI * 2.2f) * _haloBounce * 0.12f;
            var hp = _jHalo.localPosition;
            float hy = 0.9f * sy + 0.06f;
            _haloY = hy;   // rides the head (no lag: it must never leave it during a hop)
            _jHalo.localPosition = new Vector3(0, _haloY + hb * 0.5f, -0.03f);
            // a fixed tilt toward the camera (it looks a little down at the jumper): always an open ring, never an edge-on bar
            _jHalo.localRotation = Quaternion.Euler(-38, 18, 0);
            _haloMat.SetFloat("_Intensity", 1.1f + 0.35f * _jSing + joy * 0.8f + _haloBounce * 0.5f);

            // contact shadow and the step mark on the surface under the jumper
            float under = UnderSurface(x);
            float hgt = Mathf.Max(0, y - under);
            _jBlob.localPosition = new Vector3(x, under + 0.02f, 0);
            float br = 0.5f * JumperScale * (1 + Mathf.Min(hgt, 3) * 0.2f);
            _jBlob.localScale = new Vector3(br, br * 0.8f, 1);
            _blobMpb.SetFloat("_Strength", 0.6f / (1 + hgt * 0.8f));
            _jBlobR.SetPropertyBlock(_blobMpb);
            _markGlow = Mathf.Max(0, _markGlow - dt * 2.5f);
            _mark.localPosition = new Vector3(x, under + 0.035f, 0);
            _markMat.SetFloat("_Intensity", (0.22f + 0.6f * _markGlow) / (1 + hgt * 1.5f));

            _trail?.Set(new Vector3(x - 0.05f, y + 0.42f * sy * JumperScale, 0.05f));
            _jLand = Damp(_jLand, 0, 2, dt);
        }

        /// The top of whatever is under x (a plate, a veil cloud, or the sea).
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
            _markGlow = 1;
            _haloBounce = 1;
            if (!Settings.ReduceMotion)
            {
                // a landing puff: a ring and a few soft cloud wisps kicked out sideways
                fx.Ring(new Vector3(x, y + 0.03f, 0), new Color(1f, 0.97f, 0.9f), 0.32f, 0.3f, 1.25f, 0.08f, 0.015f, 0.02f, 0.45f, Vector3.up);
                fx.Burst(new Vector3(x, y + 0.06f, 0), 6, new Color(1f, 0.98f, 0.95f), 1.6f, 0.13f, 0.32f, -1.5f, Vector3.up, 1.4f);
            }
        }

        /// The puff instance for (bank, p): a step's veil cloud, or one of the bank's landing / lead-in / home puffs.
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

        static readonly Color Ivory = new Color(0.95f, 0.9f, 0.82f);
        static readonly Color RayGold = new Color(1f, 0.78f, 0.36f);

        void UpdateSteps(float dt)
        {
            var hide = Matrix4x4.TRS(new Vector3(0, -999, 0), Quaternion.identity, Vector3.zero);
            bool calm = Settings.ReduceMotion;
            var shown = Shown;
            bool singing = shown != null && BeatIndex >= 0 && _now <= shown.WindowEnd;
            float beatBob = singing && !calm ? Mathf.Exp(-BeatPhase * 4f) : 0;   // every veil bobs on every beat alike
            var cam = Look.Rig?.Camera;
            float camX = cam ? cam.transform.position.x : _camX;
            int n = 0, nv = 0;   // pooled step and veil instances used this frame
            for (int k = 0; k < Banks; k++)
            {
                var b = _banks[k];
                int baseS = k * _cap, baseP = k * (_cap + Extra);
                for (int j = 0; j < Extra; j++) _puffs.Matrix[VeilPool + k * Extra + j] = hide;
                if (b.Slot < 0) continue;
                var slot = _round.Slots[b.Slot];
                for (int i = 0; i < b.N; i++)
                {
                    int si = baseS + i, pi = baseP + i;
                    _stepPop[si] = Mathf.Max(0, _stepPop[si] - dt * 2.4f);
                    _stepFlash[si] = Mathf.Max(0, _stepFlash[si] - dt * 1.6f);
                    _puffSquash[pi] = Mathf.Max(0, _puffSquash[pi] - dt * 3.5f);
                    float x = b.X0 + i * Pitch;
                    bool near = Mathf.Abs(x - camX) < PoolRange;
                    var beat = slot.Beats[i];
                    float r = Rise(b, i), l = Lift(b, i);
                    float target = StepTop(beat.StepHeight);
                    string key = Meter.KeyFor(beat.BeatsPerBar, beat.BarPosition) ?? "up";
                    bool down = beat.BarPosition == 0;
                    if (l > 0 && i < b.Burst.Length && !b.Burst[i])
                    {
                        b.Burst[i] = true;   // the veil bursts into sparkles
                        if (!calm && near)
                        {
                            Look.Fx.Burst(new Vector3(x, PuffTop - 0.1f, -0.2f), 10, new Color(1f, 0.95f, 0.75f), 3.2f, 0.1f, 0.55f, -2f);
                            Look.Fx.Burst(new Vector3(x, PuffTop - 0.15f, -0.2f), 5, Color.white, 1.4f, 0.2f, 0.35f, 0.5f);
                        }
                    }
                    if (r > 0 && i < b.Arrived.Length && !b.Arrived[i])
                    {
                        b.Arrived[i] = true;   // the plate pops out of its cloud: a flash and a ring in its key's colour (gold on a downbeat)
                        _stepFlash[si] = 1;
                        if (!calm && near)
                            Look.Fx.Ring(new Vector3(x, target + 0.02f, 0), down ? new Color(1f, 0.8f, 0.3f) : KeyColor(key), 0.55f, 0.3f, down ? 1.6f : 1.2f, 0.12f, 0.02f, 0.02f, 0.85f, Vector3.up);
                    }
                    if (!near) continue;

                    // the veil: until its burst it sits on its column, glowing softly and bobbing with the beat
                    if (l < 1 && nv < VeilPool)
                    {
                        float sq = _puffSquash[pi];
                        float burst = calm ? (1 - l) : (1 + l * 0.45f) * (1 - l * l);   // swells, then is gone
                        // shapes vary (width, depth and turn from a hash of the step, never its height); the drift and the
                        // breath on each beat are one and the same for every veil
                        uint vh = (uint)((b.Slot * 131 + i) * 2654435761u);
                        float vw = 0.96f + ((vh >> 8) & 255) / 255f * 0.2f, vd = 0.94f + ((vh >> 16) & 255) / 255f * 0.18f;
                        float breath = Mathf.Sin(_time * 1.3f) * 0.02f;
                        var vp = new Vector3(x, PuffTop - sq * 0.08f + beatBob * 0.035f + l * 0.25f, 0);
                        _puffs.Matrix[nv] = Matrix4x4.TRS(vp, Quaternion.Euler(0, (vh & 255) * 1.41f, 0), new Vector3((1.06f * vw + sq * 0.1f + breath) * burst, (1.0f - sq * 0.16f + beatBob * 0.05f + breath * 0.5f) * burst, 1.06f * vd * burst));
                        _veilGlow.Matrix[nv] = Matrix4x4.TRS(new Vector3(x, PuffTop - 0.3f, 0.7f), Quaternion.identity, Vector3.one * 2.3f * burst);
                        float gk = 0.75f + beatBob * 0.25f + l * 1.5f;
                        _veilGlow.Tint[nv] = new Color(gk, gk, gk, 1);
                        nv++;
                    }
                    // before its reveal a step draws nothing but its veil: no plate, no column, no skirt (nothing to measure)
                    if (r <= 0 || n >= Pool) continue;
                    int q = n++;

                    // the column: its top follows the plate (under the veil before the reveal)
                    float top;
                    if (r <= 0) top = PuffTop - 0.2f;
                    else
                    {
                        float e = calm ? 1 - (1 - r) * (1 - r) * (1 - r) : BackOut(r, 2.2f);
                        top = Mathf.LerpUnclamped(PuffTop - 0.2f, target, e);
                    }
                    float pop = _stepPop[si];
                    top -= pop * 0.07f * Mathf.Sin(pop * Mathf.PI);
                    var pos = new Vector3(x, top, 0);
                    _cols.Matrix[q] = Matrix4x4.Translate(pos);
                    float tv = 0.94f + 0.06f * Mathf.Sin(i * 2.3f + b.Slot);   // a little variety in the marble
                    _cols.Tint[q] = new Color(tv, tv, tv * 0.99f, 1);
                    _skirts.Matrix[q] = Matrix4x4.TRS(new Vector3(x, SkirtY, 0.05f), Quaternion.Euler(0, i * 67 + b.Slot * 31, 0), Vector3.one * (0.92f + 0.12f * Mathf.Sin(i * 1.7f)));
                    _skirts.Tint[q] = Color.white;

                    if (r <= 0)
                    {
                        _caps.Matrix[q] = hide; _glyphs.Matrix[q] = hide; _rays.Matrix[q] = hide;
                        continue;
                    }
                    // the plate: pops up with an overshoot, scaling in, flashing its colour
                    var kc = KeyColor(key).linear;
                    float grow = calm ? 1 : BackOut(Mathf.Clamp01(r * 1.6f), 2.6f);
                    float sc = Mathf.LerpUnclamped(0.35f, 1, grow) + pop * 0.05f;
                    _caps.Matrix[q] = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(sc, Mathf.LerpUnclamped(0.5f, 1, grow), sc));
                    float flash = _stepFlash[si];
                    var tint = kc * (1 + pop * 0.6f + flash * 1.3f);
                    tint.a = 1;
                    _caps.Tint[q] = tint;
                    float rot = key == "right" ? 0 : key == "up" ? 90 : key == "left" ? 180 : -90;
                    _glyphs.Matrix[q] = Matrix4x4.TRS(pos + new Vector3(0, -CapH * 0.5f * sc, -CapW * 0.5f * sc - 0.012f), Quaternion.Euler(0, 0, rot), Vector3.one * sc);
                    var gc = Color.Lerp(Color.white, kc, 0.12f) * (1.05f + pop * 0.8f);
                    gc.a = 1;
                    _glyphs.Tint[q] = gc;
                    // the shaft of light: every plate's flares with the pop; a downbeat keeps a gold one (the bar lands)
                    float steady = down ? 0.3f : 0;
                    float rk = Mathf.Max(flash, steady) + pop * 0.4f;
                    var rc = down ? RayGold : Color.Lerp(kc, Color.white, 0.35f);
                    _rays.Matrix[q] = rk > 0.01f ? Matrix4x4.TRS(pos + new Vector3(0, -0.05f, 0.2f), Quaternion.identity, new Vector3(1, 0.6f + 0.4f * Mathf.Clamp01(r * 1.5f), 1)) : hide;
                    _rays.Tint[q] = new Color(rc.r * rk, rc.g * rk, rc.b * rk, 1);
                }
                // landing, lead-ins, home
                for (int j = 0; j < Extra; j++)
                {
                    int pi = baseP + _cap + j;
                    _puffSquash[pi] = Mathf.Max(0, _puffSquash[pi] - dt * 3.5f);
                    int p = j == 0 ? b.N : -j;
                    bool on = j <= 1 || b.Slot == 0;
                    if (!on) continue;
                    float sq = _puffSquash[pi];
                    float w = j == 0 ? 1.45f : 1.15f;
                    _puffs.Matrix[VeilPool + k * Extra + j] = Matrix4x4.TRS(new Vector3(b.X0 + p * Pitch, PuffTop - sq * 0.08f, 0), Quaternion.Euler(0, j * 71 + 20, 0), new Vector3(w * (1 + sq * 0.1f), 1.05f - sq * 0.16f, w));
                }
            }
            for (int q = n; q < Pool; q++) { _cols.Matrix[q] = hide; _caps.Matrix[q] = hide; _glyphs.Matrix[q] = hide; _rays.Matrix[q] = hide; _skirts.Matrix[q] = hide; }
            for (int q = nv; q < VeilPool; q++) { _puffs.Matrix[q] = hide; _veilGlow.Matrix[q] = hide; }
            _cols.Flush(); _caps.Flush(); _glyphs.Flush(); _puffs.Flush(); _rays.Flush(); _skirts.Flush(); _veilGlow.Flush();
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
            // The choir rides its clouds along with the view, in two wings either side of the jumper's line of sight,
            // so a singer never stands behind the jumper: the step row always has clear air behind it.
            var cam = Look.Rig?.Camera;
            var cp = cam ? cam.transform.position : new Vector3(_camX, 2, -10);
            float sight = cp.x + (_jx - cp.x) * (ChoirZ - cp.z) / Mathf.Max(1f, 0 - cp.z);   // where the eye-to-jumper line meets the choir
            for (int j = 0; j < ChoirN; j++)
            {
                int k = j;
                uint h = (uint)(k * 2654435761u);
                float r1 = (h & 1023) / 1023f, r2 = ((h >> 10) & 1023) / 1023f, r3 = ((h >> 20) & 1023) / 1023f;
                int size = (int)(((uint)k * 7u + 3u) % 3u);   // three sizes: alto, soprano, little treble
                float s = (size == 0 ? 1.55f : size == 1 ? 1.25f : 0.95f) * 1.35f;
                int side = j % 2 == 0 ? -1 : 1, rank = j / 2;
                float x = sight + side * (9.5f + rank * ChoirPitch + (r1 - 0.5f) * 1.2f);
                float z = ChoirZ + (r2 - 0.5f) * 1.5f;
                float bob = beat * 0.14f * reduce;
                float hop = _choirHop * Mathf.Abs(Mathf.Sin(_time * 7f + r1 * 6)) * 0.6f * reduce;
                float sway = Mathf.Sin(_time * (1.1f + r3 * 0.6f) + r2 * 6) * 6f * reduce;   // each singer its own sway
                float lean = Mathf.Sin(_time * (0.7f + r1 * 0.4f) + r3 * 5) * 7f * reduce;
                var pos = new Vector3(x, ChoirY + bob + hop, z);
                var rot = Quaternion.Euler(0, (r1 - 0.5f) * 24 + lean, sway);
                var m = Matrix4x4.TRS(pos, rot, new Vector3(s, s * (1 - beat * 0.04f * reduce), s));
                _choir.Matrix[j] = m;
                var tint = Color.Lerp(new Color(1, 1, 1), r3 > 0.5f ? new Color(1f, 0.92f, 0.84f) : new Color(0.88f, 0.9f, 1f), Mathf.Abs(r3 - 0.5f) * 2);
                _choir.Tint[j] = tint.linear;
                // the mouths sing with the music: open on every beat, a little apart from each other
                float open = singing ? 0.4f + beat * 0.75f + 0.12f * Mathf.Sin(_time * 9f + r2 * 6) : 0.22f;
                open = Mathf.Max(open, _choirOpen * 1.5f, _choirHop * 1.3f);
                _mouths.Matrix[j] = m * Matrix4x4.TRS(new Vector3(0, 0.98f, -0.96f), Quaternion.identity, new Vector3(1, open, 1));
                _mouths.Tint[j] = Color.white;
            }
            _choir.Flush(); _mouths.Flush();
        }

        void UpdateWorld(float dt)
        {
            var cam = Look.Rig?.Camera;
            if (!cam) return;
            var cp = cam.transform.position;
            _seaFloor.localPosition = new Vector3(cp.x, SeaY, 100);
            for (int L = 0; L < 3; L++)
            {
                float W = LayerW[L];
                float c0 = Mathf.Floor(cp.x / W) * W;
                for (int c = 0; c < 3; c++) _layers[L][c].localPosition = new Vector3(c0 + (c - 1) * W, 0, 0);
            }
            float l0 = Mathf.Floor(cp.x / LayerW[0]) * LayerW[0];
            for (int c = 0; c < 3; c++) _ledge[c].localPosition = new Vector3(l0 + (c - 1) * LayerW[0], 0, 0);
            var sun = Look.Palette.Live[PD.SunDir];
            var d = new Vector3(sun.x, sun.y, -sun.z).normalized;
            _glory.position = cp + d * 150f;
            _glory.rotation = Quaternion.LookRotation(d);
            _gloryMat.SetColor("_Color", Look.Palette.Live[PC.Sun]);
            _stars.position = cp;

            // vespers: the stars come out and the rotundas' lanterns light
            var lv = (Shown ?? CurrentSlot)?.Level ?? HymnLevel.Easy;
            _night = Damp(_night, lv == HymnLevel.Hard ? 1 : 0, 0.9f, dt);
            float tw = 0.85f + 0.15f * Mathf.Sin(_time * 1.7f);
            _starMat.SetFloat("_Intensity", _night * 2.2f * tw);
            _lanternMat.SetFloat("_Intensity", 0.15f + _night * 2.4f);

            Look.MoveShadowFocus(new Vector3(_jx + 1.5f, 0.5f, 0));
        }

        void UpdateBeatLook()
        {
            // the surfaces breathe on every beat alike (the bar is the player's to find)
            Look.BeatPulse = BeatIndex >= 0 && Shown != null && _now <= Shown.WindowEnd ? Mathf.Exp(-5f * BeatPhase) * 0.6f : 0;
            if (BeatIndex != _lastPushBeat && BeatIndex >= 0) { _lastPushBeat = BeatIndex; Look.Rig.Beat(1); }
        }
        int _lastPushBeat = -1;

        // ------------------------------------------------------------ camera

        const float CamDist = 10.2f, CamHeight = 1.05f, CamYaw = -0.2f, CamFov = 36f;

        void Frame(bool snap, float dt = 0)
        {
            float tx = _jx + 2.9f, ty = 1.15f + Mathf.Max(0, _jy - 1.3f) * 0.45f;
            if (snap) { _camX = tx; _camY = ty; }
            else { _camX = Damp(_camX, tx, 3.4f, dt); _camY = Damp(_camY, ty, 2.5f, dt); }

            // reveal: pull out to show the stair, hold, ease back in
            float want = 0;
            if (_overviewSlot >= 0 && _overviewHold > 0) { want = 1; _overviewHold -= dt; }
            if (_finale > 0) want = 1;
            _overview = snap ? want : Damp(_overview, want, want > _overview ? 2.6f : 1.6f, dt);
            float dist = CamDist, height = CamHeight, x = _camX, y = _camY;
            int os = _finale > 0 ? _round.Slots.Count - 1 : _overviewSlot;
            if (os >= 0 && _overview > 0.001f)
            {
                int n = _round.Slots[os].Beats.Count;
                // up to 12 steps: the whole stair of a short excerpt, the stretch around the jumper on a long one
                int win = Mathf.Min(n, 12);
                int first = Mathf.Clamp(Mathf.RoundToInt((_jx - _slotX[os]) / Pitch) - 2, 0, n - win);
                if (_finale > 0) first = n - win;
                float span = (win - 1) * Pitch;
                float cx = _slotX[os] + (first * Pitch) + span * 0.5f;
                float half = span * 0.5f + 1.4f;
                float hfov = 2 * Mathf.Atan(Mathf.Tan(CamFov * 0.5f * Mathf.Deg2Rad) * 16f / 9f);
                float need = Mathf.Clamp(half / Mathf.Tan(hfov * 0.5f) * 1.02f, CamDist, 20f);
                float k = Mathf.SmoothStep(0, 1, _overview);
                x = Mathf.Lerp(_camX, cx + 0.6f, k);
                dist = Mathf.Lerp(CamDist, need, k);
                height = Mathf.Lerp(CamHeight, CamHeight + (need - CamDist) * 0.12f, k);
                y = Mathf.Lerp(_camY, 1.0f, k);
            }
            Look.Rig.Frame(new Vector3(x, y, 0.4f), dist, height, CamYaw, CamFov, 7f, snap);
        }
    }
}
