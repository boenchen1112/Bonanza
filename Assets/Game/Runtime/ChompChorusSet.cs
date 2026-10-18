// Chomp Chorus: the hand-built stage (web chompChorus/props.js + the singers), everything the game is ABOUT.
//
//   riser     one stage joining the four pedestals (web: a bench), an art-deco front, a lit lip and footlights.
//   pedestals deco drums in each lane's colour that put four very different builds' HEADS on one line (the tall
//             one stands above it: up is up), each with an LED band that breathes with its voice.
//   singers   four procedural jelly-bean singers (round, wide, tall, small: the builds carry the lane mapping),
//             with eyes, brows, cheeks and a mouth. They INFLATE as they gulp (volume-preserving squash), open
//             an O while gulping and sing wide, raise their arms on the note, and go dim and droopy when their
//             voice has dropped out of the chord.
//   telegraph the web's five channels: a breath ring CONVERGING on the head (radius = time to the note), an air
//             pipe FILLING beside the pedestal (a bar chart), the singer inflating, colour + position + the key
//             glyph on the player's pedestal, and a chord BRIDGE linking the pedestals of lanes that fire together.
//   beams     a cone of light leaving the head when a note is sung, a faint halo while the voice is alive.
//
// Built once in Load (no physics, no Shader.Find; materials are copies of the Look templates in Resources, owned
// by the env). The glow families are MeshBatches drawn from Load on at black (black is free transparency in an
// additive family), so every program links before the round. Per frame: transforms, batch flushes and material
// floats, no allocations. web +z (toward the camera) is Unity -z.

using Bonanza.Core;
using Bonanza.Look;
using TMPro;
using UnityEngine;

namespace Bonanza
{
    public sealed partial class ChompChorusGame
    {
        // ------------------------------------------------------------ layout
        // build per lane: round, wide, tall, small (web chart.js LANES[i].build)
        static readonly float[] BodyW = { 1.26f, 1.66f, 0.94f, 0.82f };
        static readonly float[] BodyH = { 1.36f, 1.06f, 2.0f, 0.96f };
        /// The tall singer stands this far above the head line (web: its pedestal floors out at 0.10).
        static readonly float[] HeadLift = { 0, 0, 0.28f, 0 };
        const float FaceK = 0.66f;          // eye line as a fraction of the body height
        const float DepthK = 0.86f;         // bean cross-section: a little flatter front to back
        const float RiserTop = 0.3f;

        sealed class Singer
        {
            public Transform Root, Body, Face, ArmL, ArmR, EyeL, EyeR, BrowL, BrowR, Mouth, Cheeks;
            public Material Mat;
            public float W, H, EyeY, BrowY, MouthY;
            public float Blink = -1, NextBlink, Lean, Bounce;
        }

        Env _env;
        readonly Singer[] _singers = new Singer[N];
        MeshBatch _rings, _pipes, _beams, _bridges, _bands;
        Material _glyphMat, _youMat, _lipMat;
        readonly Material[] _poolMats = new Material[N];
        readonly Renderer[] _pools = new Renderer[N];
        QualityTier _tier = QualityTier.High;
        int _propFrames;
        Mesh _glowQuad;
        readonly float[] _pedTop = new float[N], _headY = new float[N];
        readonly Color[] _laneLin = new Color[N];
        readonly int[] _chordFlag = new int[N];
        int _chordN;
        float _youPulse, _camPush;
        static readonly Vector3 Hidden = new Vector3(0, -999, 0);

        // ------------------------------------------------------------ helpers

        MeshRenderer Part(string name, Transform parent, MeshBuilder b, Material m, Vector3 pos, bool cast = false)
        {
            var r = EnvKit.Renderer(name, parent, _env.Own(b.ToMesh(name)), m);
            r.transform.localPosition = pos;
            if (cast) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return r;
        }

        Material GlowAdd(float facing, float ramp, float fog = 0)
        {
            var m = _env.Mat(EnvKit.GlowAddMat);
            m.SetColor("_Color", Color.white);
            m.SetFloat("_Intensity", 1);
            m.SetFloat("_Facing", facing);
            m.SetFloat("_Ramp", ramp);
            m.SetFloat("_FogMul", fog);
            return m;
        }

        /// Bean body profile at s in [0,1] bottom->top: (height, radius).
        static Vector2 Bean(float s, float w, float h)
        {
            float y = h * (1 - Mathf.Cos(Mathf.PI * s)) * 0.5f;
            float r = w * 0.5f * Mathf.Pow(Mathf.Sin(Mathf.PI * s), 0.62f) * (1.06f - 0.14f * s);
            return new Vector2(y, r);
        }

        /// The body's radius at height y (inverse of the profile, by bisection: build time only).
        static float RadiusAt(float y, float w, float h)
        {
            float lo = 0, hi = 1;
            for (int k = 0; k < 24; k++) { float m = (lo + hi) * 0.5f; if (Bean(m, w, h).x < y) lo = m; else hi = m; }
            return Bean((lo + hi) * 0.5f, w, h).y;
        }

        /// A point on the camera-side surface of the bean at (x, y), and its normal.
        static Vector3 FacePoint(float x, float y, float w, float h, out Vector3 n)
        {
            float r = RadiusAt(y, w, h);
            float xx = Mathf.Clamp(x, -r * 0.98f, r * 0.98f);
            float z = -DepthK * Mathf.Sqrt(Mathf.Max(0.0001f, r * r - xx * xx));
            n = new Vector3(xx / (r * r), 0.12f, z / (DepthK * DepthK * r * r)).normalized;
            return new Vector3(xx, y, z);
        }

        // ------------------------------------------------------------ build

        void BuildStage()
        {
            for (int i = 0; i < N; i++) _laneLin[i] = Hex(LaneHex[i]).linear;
            // web: field preset (ground, backdrop, crowd, floaters) on the chomp-chorus palette.
            _env = Look.CreateEnv(EnvPreset.Field, o =>
            {
                o.Ground.Radius = 17.6f; o.Ground.Map = "cc-floor"; o.Ground.MapTiles = 9; o.Ground.Confetti = 70;
                o.Ground.Rings = false;
                o.Backdrop.Arches = 5; o.Backdrop.Radius = 17; o.Backdrop.Z = 9; o.Backdrop.Spacing = 5.5f;
                o.Backdrop.Glow = 1.1f; o.Backdrop.ArchWhiten = 0.12f;
                o.Backdrop.Skyline = 30; o.Backdrop.SkylineZ = 44; o.Backdrop.SkylineSpread = 130; o.Backdrop.WindowGlow = 1.5f; o.Backdrop.SkylineFogMul = 0.45f;
                o.Crowd.Style = EnvCrowd.FigureStyle.Figure; o.Crowd.Stands = EnvCrowd.StandStyle.Risers; o.Crowd.Poses = true;
                o.Crowd.Rows = 4; o.Crowd.PerRow = 34; o.Crowd.RowGrow = 2; o.Crowd.Radius = 18.4f; o.Crowd.RowDepth = 2.0f; o.Crowd.RowRise = 1.45f;
                o.Crowd.Scale = 2.05f; o.Crowd.Arc = Mathf.PI * 1.0f; o.Crowd.PaletteMix = 0.35f; o.Crowd.BrightMin = 0.62f; o.Crowd.BrightMax = 0.95f;
                o.Crowd.Deepen = 1.25f; o.Crowd.Energy = 0.35f; o.Crowd.Rim = 1.0f; o.Crowd.Center = new Vector3(0, 0.08f, 3); o.Crowd.Seed = 0xc40f;
                o.Floaters.Count = 12; o.Floaters.InnerR = 20; o.Floaters.OuterR = 32; o.Floaters.MinY = 6; o.Floaters.MaxY = 16; o.Floaters.Size = 0.8f;
            });
            // Concert spots: one soft cone from the rig above each singer, the beat flares half of them.
            var anchors = new Vector3[N]; var targets = new Vector3[N];
            for (int i = 0; i < N; i++) { anchors[i] = new Vector3(ChompChart.X[i] * 1.25f, 9.5f, 2.2f); targets[i] = new Vector3(ChompChart.X[i], 0.3f, 0); }
            _env.AddSpotlights(new EnvSpotlights.Options
            {
                Count = N, Anchors = anchors, Targets = targets, Height = 9.6f, Radius = 1.7f, Sweep = 0.05f,
                Base = 0.028f, Flare = 0.05f, Tint = Hex(0xffe6ff),
            });

            for (int i = 0; i < N; i++)
            {
                float faceY = BodyH[i] * FaceK;
                _pedTop[i] = Mathf.Clamp(HeadLine - faceY, 0.5f, 1.0f) + HeadLift[i];
                _headY[i] = _pedTop[i] + faceY;
            }
            BuildRiser();
            BuildPedestals();
            BuildGlowFamilies();
            for (int i = 0; i < N; i++) _singers[i] = BuildSinger(i);
        }

        /// The stage the four stand on (web: a 9.6 x 0.22 x 2.4 bench), with an art-deco front, a lit lip and a row of
        /// footlights, so the row reads as one show rather than four floating discs.
        void BuildRiser()
        {
            var deck = EnvKit.Surface(_env, Hex(0x3a1a5c), "cc-floor", new Vector2(3, 1), rim: 0.6f, pulse: 0.08f, bands: 3, mapStrength: 0.45f);
            var b = new MeshBuilder();
            b.Box(Matrix4x4.TRS(new Vector3(0, RiserTop / 2, 0.25f), Quaternion.identity, new Vector3(10.6f, RiserTop, 3.0f)), Color.white);
            // rounded ends
            foreach (var s in new[] { -1f, 1f })
            {
                b.Shell(1.5f, 1.5f, RiserTop, 24, s < 0 ? Mathf.PI : 0, Mathf.PI, Matrix4x4.Translate(new Vector3(s * 5.3f, RiserTop / 2, 0.25f)), Color.white, Vector2.one);
                b.Disc(1.5f, 24, Matrix4x4.Translate(new Vector3(s * 5.3f, RiserTop, 0.25f)), Color.white, 0.6f);
            }
            Part("Riser", transform, b, deck, Vector3.zero);
            // The deco skirt on the camera face, a shade lighter, its pattern carrying the eye along the row.
            var deco = EnvKit.Surface(_env, Hex(0x55287f), "cc-deco", new Vector2(14, 1), rim: 0.5f, pulse: 0.1f, bands: 3, mapStrength: 0.85f);
            b = new MeshBuilder();
            b.Surface(1, 1, (u, v) => (new Vector3((u - 0.5f) * 10.6f, v * (RiserTop - 0.06f) + 0.02f, -1.26f), Vector3.back), Matrix4x4.identity, Color.white, new Vector2(1, 1));
            Part("RiserFront", transform, b, deco, Vector3.zero);

            // Lit lip: a neon line along the front edge of the deck, in the palette's ground rim colour.
            _lipMat = GlowAdd(0.7f, 0);
            b = new MeshBuilder();
            b.Tube(new[] { new Vector3(-5.3f, RiserTop + 0.005f, -1.25f), new Vector3(5.3f, RiserTop + 0.005f, -1.25f) }, 0.028f, 6, Matrix4x4.identity, Color.white);
            foreach (var s in new[] { -1f, 1f })
            {
                var arc = new Vector3[17];
                for (int k = 0; k < arc.Length; k++)
                {
                    float a = -Mathf.PI / 2 + s * Mathf.PI / 2 * k / (arc.Length - 1);
                    arc[k] = new Vector3(s * 5.3f + Mathf.Cos(a) * 1.5f * s, RiserTop + 0.005f, 0.25f + Mathf.Sin(a) * 1.5f);
                }
                b.Tube(arc, 0.028f, 6, Matrix4x4.identity, Color.white);
            }
            Part("RiserLip", transform, b, _lipMat, Vector3.zero).receiveShadows = false;

            // Marquee: a row of bulbs in brass sockets along the deco skirt that chases on the eighth notes.
            var brass = EnvKit.Surface(_env, Hex(0xc9a24a), null, null, rim: 1.2f, pulse: 0.1f, bands: 3);
            var sockets = new MeshBuilder();
            var bulb = new MeshBuilder();
            bulb.Sphere(0.042f, 8, 6, Matrix4x4.identity, Color.white, Vector3.one);
            _marquee = new MeshBatch("Marquee", transform, bulb, MarqueeN, GlowAdd(0.25f, 0));
            _marquee.Renderer.receiveShadows = false;
            for (int k = 0; k < MarqueeN; k++)
            {
                var p = MarqueeAt(k);
                sockets.Disc(0.06f, 10, Matrix4x4.TRS(p + new Vector3(0, 0, 0.012f), Quaternion.Euler(-90, 0, 0), Vector3.one), Color.white);
                _marquee.Matrix[k] = Matrix4x4.Translate(p);
                _marquee.Tint[k] = Color.black;
            }
            _marquee.Flush();
            Part("MarqueeSockets", transform, sockets, brass, Vector3.zero).receiveShadows = false;

            // The floor in front of the stage is lacquered: each voice throws a long soft reflection of its colour
            // toward the camera (additive glow cards lying on the boards, brighter as the singer sings).
            for (int i = 0; i < N; i++)
            {
                var m = _env.Mat(EnvKit.GlowMat);
                m.SetColor("_Color", Hex(LaneHex[i]));
                m.SetFloat("_Intensity", 0);
                m.SetFloat("_Billboard", 0);
                m.SetFloat("_Core", 0.6f);
                var r = EnvKit.Renderer("FloorGlow" + i, transform, GlowQuad(), m);
                r.receiveShadows = false;
                r.transform.localPosition = new Vector3(ChompChart.X[i] * 1.15f, 0.015f, -3.1f);
                r.transform.localRotation = Quaternion.Euler(90, 0, 0);
                r.transform.localScale = new Vector3(1.9f, 4.6f, 1);
                _poolMats[i] = m;
                _pools[i] = r;
            }
            var turf = _env.Ground?.Top ? _env.Ground.Top.sharedMaterial : null;
            if (turf) { turf.SetFloat("_Rim", 0.06f); turf.SetFloat("_MapStrength", 0.7f); }
        }

        const int MarqueeN = 29;
        MeshBatch _marquee;
        static Vector3 MarqueeAt(int k) => new Vector3(-5.04f + 10.08f * k / (MarqueeN - 1), RiserTop * 0.5f + 0.01f, -1.275f);

        /// A unit glow card (XY, uv 0..1) for the Bonanza/Glow renderers.
        Mesh GlowQuad()
        {
            if (_glowQuad) return _glowQuad;
            var b = new MeshBuilder();
            b.Surface(1, 1, (u, v) => (new Vector3(u - 0.5f, v - 0.5f, 0), Vector3.back), Matrix4x4.identity, Color.white, Vector2.one);
            return _glowQuad = _env.Own(b.ToMesh("GlowQuad"));
        }

        /// The four deco drums, one merged mesh (vertex colours carry each lane's paint), plus the player's key glyph,
        /// its floor ring and the YOU tag.
        void BuildPedestals()
        {
            var mat = EnvKit.Surface(_env, Color.white, "cc-deco", new Vector2(7, 1), rim: 1.1f, pulse: 0.12f, bands: 3, mapStrength: 0.75f);
            var b = new MeshBuilder();
            var band = new MeshBuilder();
            for (int i = 0; i < N; i++)
            {
                float x = ChompChart.X[i], top = _pedTop[i];
                var c = _laneLin[i];
                Color dark = c * 0.3f, mid = c * 0.55f, cap = c * 1.0f, rimC = Color.Lerp(c, Color.white, 0.35f);
                dark.a = mid.a = cap.a = rimC.a = 1;
                float h = Mathf.Max(0.14f, top - RiserTop);
                // plinth, drum, a raised collar under the cap
                b.Shell(1.22f, 1.26f, 0.1f, 32, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(x, RiserTop + 0.05f, 0)), dark, new Vector2(8, 0.1f));
                b.Shell(1.0f, 1.14f, h, 32, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(x, RiserTop + h / 2, 0)), mid, new Vector2(6, 1));
                b.Shell(1.12f, 1.08f, 0.06f, 32, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(x, top - 0.11f, 0)), dark, new Vector2(8, 0.1f));
                // cap: an edge, a bevel and the top
                b.Shell(1.12f, 1.12f, 0.1f, 40, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(x, top - 0.03f, 0)), cap, new Vector2(8, 0.1f));
                b.Torus(1.07f, 0.05f, 6, 48, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(x, top + 0.02f, 0), Quaternion.Euler(90, 0, 0), Vector3.one), rimC);
                b.Disc(1.07f, 40, Matrix4x4.Translate(new Vector3(x, top + 0.02f, 0)), cap, 0.4f);
                b.Disc(0.78f, 40, Matrix4x4.Translate(new Vector3(x, top + 0.024f, 0)), Color.Lerp(cap, Color.white, 0.12f) * 0.9f, 0.3f);
                // the LED band template (one per lane, tinted live)
            }
            Part("Pedestals", transform, b, mat, Vector3.zero, cast: false);
            band.Torus(1.0f, 0.032f, 5, 64, Mathf.PI * 2, Matrix4x4.Rotate(Quaternion.Euler(90, 0, 0)), Color.white);
            _bands = new MeshBatch("PedestalBands", transform, band, N, GlowAdd(0.6f, 0));
            _bands.Renderer.receiveShadows = false;
            for (int i = 0; i < N; i++)
            {
                // on the drum, just under the collar
                float h = Mathf.Max(0.14f, _pedTop[i] - RiserTop), y = _pedTop[i] - 0.2f;
                float r = Mathf.Lerp(1.14f, 1.0f, Mathf.Clamp01((y - RiserTop) / h)) + 0.012f;
                _bands.Matrix[i] = Matrix4x4.TRS(new Vector3(ChompChart.X[i], y, 0), Quaternion.identity, new Vector3(r, 1, r));
                _bands.Tint[i] = Color.black;
            }
            _bands.Flush();

            // The player's stage monitor: a floor wedge in front of their pedestal, its top edge lit in the lane colour,
            // carrying the key glyph (web: the human lane only) and the YOU tag; a white ring on the deck around their
            // pedestal makes it "the ringed voice".
            int hu = _round.Human;
            float hx = ChompChart.X[hu];
            var wedgeMat = EnvKit.Surface(_env, Color.white, "cc-velvet", new Vector2(2, 1), rim: 0.9f, pulse: 0.08f, bands: 3, mapStrength: 0.25f);
            b = new MeshBuilder();
            var strip = new MeshBuilder();
            for (int i = hu; i == hu; i++)
            {
                var at = WedgeAt(i);
                Wedge(b, at, WedgeW, WedgeD, WedgeHb, WedgeHf, new Color(0.075f, 0.055f, 0.11f));
                // the grille: a slightly raised darker panel on the slope
                var n = WedgeNormal();
                var mid = WedgeSlope(at, 0.5f);
                b.Box(Matrix4x4.TRS(mid + n * 0.012f, Quaternion.LookRotation(-n, Vector3.up) * Quaternion.identity, new Vector3(WedgeW * 0.86f, WedgeSlopeLen * 0.8f, 0.02f)), new Color(0.03f, 0.025f, 0.05f));
                strip.Tube(new[] { at + new Vector3(-WedgeW / 2 + 0.02f, WedgeHb + 0.01f, WedgeD / 2 - 0.02f), at + new Vector3(WedgeW / 2 - 0.02f, WedgeHb + 0.01f, WedgeD / 2 - 0.02f) }, 0.022f, 5, Matrix4x4.identity, _laneLin[i] * 1.6f);
            }
            Part("Monitors", transform, b, wedgeMat, Vector3.zero);
            _stripMat = GlowAdd(0.5f, 0);
            Part("MonitorStrips", transform, strip, _stripMat, Vector3.zero).receiveShadows = false;

            _glyphMat = GlowAdd(0, 0);
            b = new MeshBuilder();
            {
                var at = WedgeAt(hu);
                var n = WedgeNormal();
                Arrow(b, hu, WedgeSlope(at, 0.62f) + n * 0.03f, 0.15f, Quaternion.FromToRotation(Vector3.back, n));
            }
            Part("KeyGlyph", transform, b, _glyphMat, Vector3.zero).receiveShadows = false;
            _youMat = GlowAdd(0.5f, 0);
            b = new MeshBuilder();
            b.Torus(1.42f, 0.03f, 5, 72, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(hx, RiserTop + 0.01f, 0), Quaternion.Euler(90, 0, 0), Vector3.one), Color.white);
            Part("YouRing", transform, b, _youMat, Vector3.zero).receiveShadows = false;

            var font = Resources.Load<TMP_FontAsset>("Look/CalloutFont");
            var go = new GameObject("YouTag", typeof(TextMeshPro));
            go.transform.SetParent(transform, false);
            {
                var n = WedgeNormal();
                go.transform.localPosition = WedgeSlope(WedgeAt(hu), 0.2f) + n * 0.03f;
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.back, n);
            }
            var t = go.GetComponent<TextMeshPro>();
            if (font) t.font = font;
            t.text = "YOU";
            t.fontSize = 0.8f;
            t.characterSpacing = 14;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.color = Color.white;
            t.rectTransform.sizeDelta = new Vector2(1.2f, 0.3f);
            var tr = go.GetComponent<MeshRenderer>();
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;
        }

        // floor monitor wedge: width, depth, back and front heights
        const float WedgeW = 0.86f, WedgeD = 0.52f, WedgeHb = 0.36f, WedgeHf = 0.09f;
        static readonly float WedgeSlopeLen = Mathf.Sqrt(WedgeD * WedgeD + (WedgeHb - WedgeHf) * (WedgeHb - WedgeHf));
        Material _stripMat;
        static Vector3 WedgeAt(int i) => new Vector3(ChompChart.X[i], 0, -1.7f);
        static Vector3 WedgeNormal() => new Vector3(0, WedgeD, -(WedgeHb - WedgeHf)).normalized;
        static Vector3 WedgeSlope(Vector3 at, float t) => at + new Vector3(0, Mathf.Lerp(WedgeHf, WedgeHb, t), Mathf.Lerp(-WedgeD / 2, WedgeD / 2, t));

        /// A closed prism: rectangular foot, vertical back (hb) and front (hf), a sloped top facing the camera.
        static void Wedge(MeshBuilder b, Vector3 at, float w, float d, float hb, float hf, Color c)
        {
            float x0 = -w / 2, x1 = w / 2, z0 = -d / 2, z1 = d / 2;
            Vector3 P(float x, float y, float z) => at + new Vector3(x, y, z);
            void Face(Vector3 a, Vector3 bb, Vector3 cc, Vector3 dd)
            {
                var n = Vector3.Cross(bb - a, cc - a).normalized;
                int o = b.Count;
                foreach (var v in new[] { a, bb, cc, dd }) { b.P.Add(v); b.N.Add(n); b.C.Add(c); }
                b.UV.Add(new Vector2(0, 0)); b.UV.Add(new Vector2(1, 0)); b.UV.Add(new Vector2(1, 1)); b.UV.Add(new Vector2(0, 1));
                b.T.Add(o); b.T.Add(o + 1); b.T.Add(o + 2); b.T.Add(o); b.T.Add(o + 2); b.T.Add(o + 3);
            }
            // front (facing -z), back (+z), slope (up/-z), sides
            Face(P(x1, 0, z0), P(x0, 0, z0), P(x0, hf, z0), P(x1, hf, z0));
            Face(P(x0, 0, z1), P(x1, 0, z1), P(x1, hb, z1), P(x0, hb, z1));
            Face(P(x1, hf, z0), P(x0, hf, z0), P(x0, hb, z1), P(x1, hb, z1));
            foreach (var x in new[] { x0, x1 })
            {
                int o = b.Count;
                var n = new Vector3(Mathf.Sign(x), 0, 0);
                foreach (var v in new[] { P(x, 0, z0), P(x, 0, z1), P(x, hb, z1), P(x, hf, z0) }) { b.P.Add(v); b.N.Add(n); b.C.Add(c); b.UV.Add(Vector2.zero); }
                b.Tri(o, o + 1, o + 2); b.Tri(o, o + 2, o + 3);
            }
        }

        /// A chunky arrow (web glyphAtlas: 0 left, 1 down, 2 up, 3 right) as geometry, facing the camera.
        static void Arrow(MeshBuilder b, int lane, Vector3 at, float size, Quaternion face)
        {
            float rot = lane switch { 0 => 180, 1 => -90, 2 => 90, _ => 0 };
            var q = Quaternion.Euler(0, 0, rot);
            float s = size;
            Vector2[] pts =
            {
                new Vector2(s, 0), new Vector2(s * 0.1f, -s * 0.85f), new Vector2(s * 0.1f, -s * 0.34f), new Vector2(-s, -s * 0.34f),
                new Vector2(-s, s * 0.34f), new Vector2(s * 0.1f, s * 0.34f), new Vector2(s * 0.1f, s * 0.85f),
            };
            int o = b.Count;
            foreach (var p in pts) { b.P.Add(at + face * (q * new Vector3(p.x, p.y, 0))); b.N.Add(face * Vector3.back); b.C.Add(Color.white); b.UV.Add(Vector2.zero); }
            // head (0,1,6), shaft (2,3,4,5)
            b.T.Add(o); b.T.Add(o + 1); b.T.Add(o + 6);
            b.T.Add(o + 2); b.T.Add(o + 3); b.T.Add(o + 4); b.T.Add(o + 2); b.T.Add(o + 4); b.T.Add(o + 5);
        }

        /// Breath rings, air pipes (housing + fill), voice beams, chord bridges: four additive families, drawn at black
        /// from here on so they link now (web props.js: InstancedMeshes, colour toward black to fade).
        void BuildGlowFamilies()
        {
            var tpl = new MeshBuilder();
            tpl.Torus(1, 0.05f, 6, 56, Mathf.PI * 2, Matrix4x4.identity, Color.white);
            _rings = new MeshBatch("BreathRings", transform, tpl, N, GlowAdd(0.55f, 0));

            // Pipes: a glass tube (its silhouette glows faintly in the lane colour) between a brass foot and a brass cap,
            // always there, and the glowing fill rising inside it.
            var brass = EnvKit.Surface(_env, Hex(0xd6aa52), null, null, rim: 1.3f, pulse: 0.1f, bands: 3);
            var b = new MeshBuilder();
            var glass = new MeshBuilder();
            for (int i = 0; i < N; i++)
            {
                var p = PipeBase(i);
                float hMax = PipeMax(i);
                b.Shell(0.25f, 0.29f, 0.1f, 18, 0, Mathf.PI * 2, Matrix4x4.Translate(p + Vector3.up * 0.05f), Color.white, Vector2.one);
                b.Torus(0.235f, 0.04f, 6, 22, Mathf.PI * 2, Matrix4x4.TRS(p + Vector3.up * 0.1f, Quaternion.Euler(90, 0, 0), Vector3.one), Color.white);
                b.Torus(0.235f, 0.04f, 6, 22, Mathf.PI * 2, Matrix4x4.TRS(p + Vector3.up * (hMax + 0.09f), Quaternion.Euler(90, 0, 0), Vector3.one), Color.white);
                b.Sphere(0.24f, 16, 8, Matrix4x4.Translate(p + Vector3.up * (hMax + 0.1f)), Color.white, new Vector3(1, 0.32f, 1));
                b.Sphere(0.06f, 8, 6, Matrix4x4.Translate(p + Vector3.up * (hMax + 0.2f)), Color.white * 1.2f, Vector3.one);
                int g0 = glass.Count;
                glass.Shell(0.22f, 0.22f, hMax, 18, 0, Mathf.PI * 2, Matrix4x4.Translate(p + Vector3.up * (0.1f + hMax / 2)), _laneLin[i] * 0.55f + new Color(0.12f, 0.12f, 0.14f), Vector2.one);
            }
            Part("PipeBrass", transform, b, brass, Vector3.zero);
            var glassMat = GlowAdd(-2.2f, 0, 0.2f);
            Part("PipeGlass", transform, glass, glassMat, Vector3.zero).receiveShadows = false;
            tpl = new MeshBuilder();
            tpl.Shell(0.165f, 0.165f, 1, 16, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(0, 0.5f, 0)), Color.white, Vector2.one);
            tpl.Sphere(0.165f, 16, 6, Matrix4x4.Translate(new Vector3(0, 1, 0)), Color.white * 1.5f, new Vector3(1, 0.35f, 1));
            _pipes = new MeshBatch("PipeFill", transform, tpl, N, GlowAdd(0.8f, 0));

            // Beams: a cone of light widening DOWN to the head (web CylinderGeometry(0.06, 0.42)); uv.y = 1 at the head.
            tpl = new MeshBuilder();
            tpl.Surface(18, 6, (u, v) =>
            {
                float a = u * Mathf.PI * 2, r = Mathf.Lerp(0.42f, 0.06f, v);
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                return (d * r + Vector3.up * v, new Vector3(d.x, 0.36f, d.z).normalized);
            }, Matrix4x4.identity, Color.white, Vector2.one);
            for (int k = 0; k < tpl.UV.Count; k++) tpl.UV[k] = new Vector2(tpl.UV[k].x, 1 - tpl.UV[k].y);
            _beams = new MeshBatch("VoiceBeams", transform, tpl, N, GlowAdd(0.7f, 1));

            tpl = new MeshBuilder();
            tpl.Capsule(0.055f, 1, 3, 10, Matrix4x4.Rotate(Quaternion.Euler(0, 0, 90)), Color.white);
            _bridges = new MeshBatch("ChordBridges", transform, tpl, N - 1, GlowAdd(0.7f, 0));

            foreach (var m in new[] { _rings, _pipes, _beams, _bridges })
            {
                m.Renderer.receiveShadows = false;
                for (int i = 0; i < m.Tint.Length; i++) { m.Tint[i] = Color.black; m.Matrix[i] = Matrix4x4.TRS(Hidden, Quaternion.identity, Vector3.one * 0.001f); }
                m.Flush();
            }
        }

        /// The pipe stands on the cap's front-right edge, off the singer's belly (web: dead centre in front).
        Vector3 PipeBase(int i) => new Vector3(ChompChart.X[i] + 0.74f, _pedTop[i] + 0.03f, -0.76f);
        float PipeMax(int i) => Mathf.Max(0.55f, _headY[i] - _pedTop[i] - 0.05f);

        // ------------------------------------------------------------ singers

        Singer BuildSinger(int i)
        {
            float w = BodyW[i], h = BodyH[i];
            var s = new Singer { W = w, H = h };
            var lane = _laneLin[i];
            Color belly = Color.Lerp(lane, Color.white, 0.3f), dark = lane * 0.32f, white = new Color(0.98f, 0.97f, 0.95f);
            dark.a = 1;
            s.Mat = EnvKit.Surface(_env, Color.white, null, null, rim: 1.25f, pulse: 0.1f, bands: 3);
            s.Mat.SetFloat("_Softness", 0.3f);
            s.Mat.SetFloat("_ShadowRecv", 0);   // casts on its pedestal, never self-shadows (jagged acne on a curved toon body)
            var face = EnvKit.Surface(_env, Color.white, null, null, rim: 0.35f, pulse: 0.05f, lift: 0.12f, bands: 2);
            face.SetFloat("_ShadowRecv", 0);

            s.Root = new GameObject("Singer" + i).transform;
            s.Root.SetParent(transform, false);
            s.Root.localPosition = new Vector3(ChompChart.X[i], _pedTop[i] + 0.025f, 0);
            s.Root.localRotation = Quaternion.Euler(0, ChompChart.X[i] * 2.4f, 0);   // a slight turn in toward the centre
            s.Body = new GameObject("Body").transform;
            s.Body.SetParent(s.Root, false);

            // --- body: the bean, a lighter belly patch, feet
            var b = new MeshBuilder();
            b.Surface(28, 18, (u, v) =>
            {
                float a = u * Mathf.PI * 2, t = Mathf.Lerp(0.02f, 0.985f, v);
                var p = Bean(t, w, h); var p2 = Bean(t + 0.004f, w, h);
                var tn = new Vector2(p2.y - p.y, p2.x - p.x).normalized;   // (dr, dy)
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a) * DepthK);
                var n = new Vector3(Mathf.Sin(a) * tn.y, -tn.x, Mathf.Cos(a) * tn.y / DepthK);
                return (d * p.y + Vector3.up * p.x, n.normalized);
            }, Matrix4x4.identity, lane, Vector2.one);
            // belly: front and low, a soft cream oval baked into the vertex colours
            for (int k = 0; k < b.Count; k++)
            {
                var p = b.P[k];
                float r = RadiusAt(Mathf.Clamp(p.y, 0.01f, h * 0.99f), w, h);
                float front = Mathf.Clamp01(-p.z / Mathf.Max(0.01f, r * DepthK));
                float ex = p.x / (w * 0.34f), ey = (p.y - h * 0.3f) / (h * 0.24f);
                float m = Mathf.SmoothStep(1.0f, 0.75f, Mathf.Sqrt(ex * ex + ey * ey)) * Mathf.SmoothStep(0.55f, 0.85f, front);
                b.C[k] = Color.Lerp(lane, belly, m);
            }
            b.Disc(RadiusAt(h * 0.012f, w, h) + 0.01f, 20, Matrix4x4.TRS(new Vector3(0, h * 0.006f, 0), Quaternion.Euler(180, 0, 0), Vector3.one), lane);
            // feet: two dark bumps peeking out at the front
            foreach (var sx in new[] { -1f, 1f })
                b.Sphere(0.13f, 12, 8, Matrix4x4.Translate(new Vector3(sx * w * 0.22f, 0.04f, -RadiusAt(0.1f, w, h) * DepthK * 0.75f)), dark, new Vector3(Mathf.Max(0.9f, w * 0.9f), 0.55f, 1.25f));
            Accessory(b, i, w, h, lane, dark);
            Part("Bean", s.Body, b, s.Mat, Vector3.zero, cast: true);

            // --- arms: a stubby arm ending in a white mitten, pivot at the shoulder
            float shY = h * 0.42f, shR = RadiusAt(shY, w, h);
            float armLen = Mathf.Clamp(h * 0.34f, 0.24f, 0.42f);
            b = new MeshBuilder();
            b.Capsule(0.075f, armLen, 3, 10, Matrix4x4.Translate(new Vector3(0, -armLen / 2, 0)), lane);
            b.Sphere(0.105f, 12, 8, Matrix4x4.Translate(new Vector3(0, -armLen - 0.04f, 0)), white, new Vector3(1, 0.95f, 0.9f));
            var armMesh = _env.Own(b.ToMesh("Arm"));
            for (int k = 0; k < 2; k++)
            {
                float sx = k == 0 ? -1 : 1;
                var arm = new GameObject(k == 0 ? "ArmL" : "ArmR").transform;
                arm.SetParent(s.Body, false);
                arm.localPosition = new Vector3(sx * (shR - 0.04f), shY, -0.02f);
                var r = EnvKit.Renderer("Mesh", arm, armMesh, s.Mat);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (k == 0) s.ArmL = arm; else s.ArmR = arm;
            }

            // --- face: eyes (white, pupil, two catch-lights), brows, cheeks, mouth (cavity + tongue)
            s.Face = new GameObject("Face").transform;
            s.Face.SetParent(s.Body, false);
            float eyeR = Mathf.Clamp(w * 0.13f, 0.085f, 0.13f);
            float eyeX = Mathf.Clamp(w * 0.19f, 0.12f, 0.2f);
            s.EyeY = h * FaceK;
            b = new MeshBuilder();
            b.Sphere(eyeR, 16, 12, Matrix4x4.identity, white, new Vector3(0.9f, 1.15f, 0.55f));
            b.Sphere(eyeR * 0.56f, 14, 10, Matrix4x4.Translate(new Vector3(0, -eyeR * 0.12f, -eyeR * 0.36f)), new Color(0.05f, 0.03f, 0.08f), new Vector3(0.95f, 1.15f, 0.5f));
            b.Sphere(eyeR * 0.2f, 8, 6, Matrix4x4.Translate(new Vector3(-eyeR * 0.22f, eyeR * 0.2f, -eyeR * 0.58f)), new Color(2.4f, 2.4f, 2.4f), Vector3.one);
            b.Sphere(eyeR * 0.1f, 6, 4, Matrix4x4.Translate(new Vector3(eyeR * 0.18f, -eyeR * 0.32f, -eyeR * 0.56f)), new Color(1.6f, 1.6f, 1.6f), Vector3.one);
            var eyeMesh = _env.Own(b.ToMesh("Eye"));
            for (int k = 0; k < 2; k++)
            {
                float sx = k == 0 ? -1 : 1;
                var p = FacePoint(sx * eyeX, s.EyeY, w, h, out var n);
                var e = new GameObject(k == 0 ? "EyeL" : "EyeR").transform;
                e.SetParent(s.Face, false);
                e.localPosition = p - n * eyeR * 0.18f;
                e.localRotation = Quaternion.LookRotation(new Vector3(-n.x, 0, -n.z).normalized, Vector3.up);   // local -z (the pupil) out along the face
                EnvKit.Renderer("Mesh", e, eyeMesh, face).receiveShadows = false;
                if (k == 0) s.EyeL = e; else s.EyeR = e;
            }
            // brows
            s.BrowY = s.EyeY + eyeR * 1.45f;
            b = new MeshBuilder();
            b.Capsule(0.028f, eyeR * 1.1f, 2, 6, Matrix4x4.Rotate(Quaternion.Euler(0, 0, 90)), dark * 0.6f);
            var browMesh = _env.Own(b.ToMesh("Brow"));
            for (int k = 0; k < 2; k++)
            {
                float sx = k == 0 ? -1 : 1;
                var p = FacePoint(sx * eyeX * 1.02f, s.BrowY, w, h, out var n);
                var br = new GameObject(k == 0 ? "BrowL" : "BrowR").transform;
                br.SetParent(s.Face, false);
                br.localPosition = p + n * 0.01f;
                EnvKit.Renderer("Mesh", br, browMesh, face).receiveShadows = false;
                if (k == 0) s.BrowL = br; else s.BrowR = br;
            }
            // cheeks: two blush ovals either side of the mouth, under a pivot at their midpoint (they puff from there)
            s.MouthY = s.EyeY - Mathf.Clamp(h * 0.16f, 0.13f, 0.2f);
            b = new MeshBuilder();
            var blush = Color.Lerp(new Color(1f, 0.35f, 0.5f), lane, 0.25f);
            float cheekY = (s.EyeY + s.MouthY) * 0.5f - 0.02f;
            var cheekPivot = FacePoint(0, cheekY, w, h, out _);
            foreach (var sx in new[] { -1f, 1f })
            {
                var p = FacePoint(sx * eyeX * 1.45f, cheekY, w, h, out var n);
                b.Sphere(eyeR * 0.75f, 12, 8, Matrix4x4.TRS(p - n * 0.01f - cheekPivot, Quaternion.LookRotation(n), Vector3.one), blush, new Vector3(1.25f, 0.8f, 0.3f));
            }
            s.Cheeks = new GameObject("Cheeks").transform;
            s.Cheeks.SetParent(s.Face, false);
            s.Cheeks.localPosition = cheekPivot;
            Part("Mesh", s.Cheeks, b, face, Vector3.zero).receiveShadows = false;
            // mouth
            {
                var p = FacePoint(0, s.MouthY, w, h, out var n);
                var m = new GameObject("Mouth").transform;
                m.SetParent(s.Face, false);
                m.localPosition = p - n * 0.02f;
                m.localRotation = Quaternion.LookRotation(new Vector3(-n.x, 0, -n.z).normalized, Vector3.up);
                float mw = Mathf.Clamp(w * 0.17f, 0.11f, 0.19f);
                b = new MeshBuilder();
                b.Sphere(mw, 18, 12, Matrix4x4.identity, new Color(0.16f, 0.03f, 0.08f), new Vector3(1, 0.62f, 0.3f));
                b.Sphere(mw * 0.62f, 12, 8, Matrix4x4.Translate(new Vector3(0, -mw * 0.3f, -mw * 0.12f)), new Color(1f, 0.38f, 0.48f), new Vector3(1, 0.5f, 0.4f));
                // upper lip line: a thin darker crescent so the closed mouth still reads as a smile
                Part("Mesh", m, b, face, Vector3.zero).receiveShadows = false;
                s.Mouth = m;
            }
            s.NextBlink = 1.5f + i * 0.9f;
            return s;
        }

        /// One personal touch per singer, baked into the body mesh (lane colours): round = an antenna bobble,
        /// wide = a bow tie, tall = a quiff, small = a bow.
        static void Accessory(MeshBuilder b, int i, float w, float h, Color lane, Color dark)
        {
            var gold = Hex(0xffd93d).linear; var pink = Hex(0xff6fae).linear; var ink = new Color(0.07f, 0.05f, 0.12f);
            switch (i)
            {
                case 0:
                    b.Tube(new[] { new Vector3(0, h * 0.97f, 0), new Vector3(0.04f, h + 0.14f, 0.02f), new Vector3(0.1f, h + 0.24f, 0.05f) }, 0.02f, 5, Matrix4x4.identity, dark);
                    b.Sphere(0.075f, 12, 8, Matrix4x4.Translate(new Vector3(0.11f, h + 0.28f, 0.05f)), gold, Vector3.one);
                    break;
                case 1:
                {
                    var p = FacePoint(0, h * 0.36f, w, h, out var n);
                    var q = Quaternion.LookRotation(n);
                    b.Sphere(0.05f, 10, 6, Matrix4x4.TRS(p - n * 0.02f, q, Vector3.one), ink, new Vector3(1, 1, 0.8f));
                    foreach (var sx in new[] { -1f, 1f })
                        b.Cone(0.09f, 0.16f, 8, Matrix4x4.TRS(p + new Vector3(sx * 0.02f, 0, 0) - n * 0.03f, Quaternion.Euler(0, 0, sx * 90), new Vector3(1, 1, 0.5f)), ink);
                    break;
                }
                case 2:
                    for (int k = 0; k < 4; k++)
                        b.Sphere(0.1f - k * 0.012f, 12, 8, Matrix4x4.Translate(new Vector3(0, h * 0.93f + k * 0.05f, -0.1f + k * 0.07f)), Color.Lerp(lane, dark, 0.45f), new Vector3(1.2f, 0.8f, 1.1f));
                    break;
                default:
                    foreach (var sx in new[] { -1f, 1f })
                        b.Sphere(0.075f, 10, 6, Matrix4x4.TRS(new Vector3(sx * 0.07f, h * 0.99f, 0), Quaternion.Euler(0, 0, sx * -25), Vector3.one), pink, new Vector3(1.3f, 0.75f, 0.6f));
                    b.Sphere(0.04f, 8, 6, Matrix4x4.Translate(new Vector3(0, h * 0.99f, 0)), pink * 0.8f, Vector3.one);
                    break;
            }
        }

        // ------------------------------------------------------------ per frame (presentation time)

        void UpdateSinger(int i, float dt, float t, double beat, bool finaleHold)
        {
            var s = _singers[i];
            var l = _lanes[i];
            bool calm = Settings.ReduceMotion;
            float puff = l.Gulp * l.Armed;
            float alive = l.AliveT;
            float sing = Mathf.Clamp01(l.Sing);
            float ph = Phase(beat);

            // Breath and beat: an idle breath on a slow sine, a little squash on every beat while alive.
            float breath = Mathf.Sin(t * 2.1f + i * 1.3f) * 0.012f;
            float beatSq = _playing && beat >= 0 ? Mathf.Exp(-9f * ph) * (calm ? 0.012f : 0.035f) * alive : 0;
            // Inflate (volume-preserving: wide grows as tall shrinks a little), stretch on the note.
            float wide = 1 + puff * 0.2f + breath + beatSq - sing * 0.06f;
            float tall = 1 + puff * 0.07f - beatSq * 0.8f + sing * 0.14f - (1 - alive) * 0.06f;
            float wob = calm ? 0 : l.Flinch * Mathf.Sin(t * 46) * 0.06f;
            s.Body.localScale = new Vector3(wide + wob, tall - wob, wide * (1 + puff * 0.06f));
            // lean back into the note, slump forward when the voice is out; a slow sway on the bar
            float sway = calm ? 0 : Mathf.Sin((float)beat * Mathf.PI * 0.5f + i) * 2.2f * alive;
            float lean = -sing * 7f + (1 - alive) * 9f + puff * -3f;
            s.Lean = Damp(s.Lean, lean, 10, dt);
            s.Body.localRotation = Quaternion.Euler(s.Lean, 0, sway);

            // Arms: hang a little out at rest, draw up as the breath is taken, fly up and out on the note.
            float armOut = 18 + puff * 30 + sing * 95 - (1 - alive) * 10;
            float armFwd = -puff * 15 + sing * 10;
            if (finaleHold && _round.Alive[i]) armOut = Mathf.Max(armOut, 120 + Mathf.Sin(t * 9 + i) * 8);
            float flap = calm ? 0 : Mathf.Sin(t * 3 + i) * 4;
            s.ArmL.localRotation = Quaternion.Euler(armFwd, 0, -armOut - flap);
            s.ArmR.localRotation = Quaternion.Euler(armFwd, 0, armOut + flap);

            // Eyes: blink now and then; wide on the gulp; happy squint on the note; heavy lids when out.
            s.NextBlink -= dt;
            if (s.NextBlink <= 0) { s.Blink = 0; s.NextBlink = 2.2f + Mathf.Repeat(i * 1.7f + t * 0.37f, 2.6f); }
            float blink = 1;
            if (s.Blink >= 0) { s.Blink += dt / 0.14f; blink = Mathf.Abs(1 - 2 * Mathf.Clamp01(s.Blink)); if (s.Blink >= 1) s.Blink = -1; }
            float open = Mathf.Lerp(0.38f, 1, alive) * (1 + puff * 0.15f) * (1 - sing * 0.5f) * Mathf.Max(0.08f, blink);
            open = Mathf.Max(0.08f, open);
            float es = 1 + puff * 0.1f;
            s.EyeL.localScale = new Vector3(es, open * es, es);
            s.EyeR.localScale = new Vector3(es, open * es, es);
            // Brows: up on the breath, sad tilt when out, a flinch knits them.
            float browUp = puff * 0.05f + sing * 0.03f - (1 - alive) * 0.02f - l.Flinch * 0.02f;
            float tilt = (1 - alive) * 18 - l.Flinch * 14 + puff * -6;
            s.BrowL.localPosition = new Vector3(s.BrowL.localPosition.x, s.BrowY + browUp, s.BrowL.localPosition.z);
            s.BrowR.localPosition = new Vector3(s.BrowR.localPosition.x, s.BrowY + browUp, s.BrowR.localPosition.z);
            s.BrowL.localRotation = Quaternion.Euler(0, 0, -tilt);
            s.BrowR.localRotation = Quaternion.Euler(0, 0, tilt);

            // Mouth: a small smile at rest, an O while gulping, wide open while singing (web: open = max(puff/2, sing)).
            float mo = Mathf.Max(puff * 0.5f, sing);
            float mx = Mathf.Lerp(0.9f, 0.62f, Mathf.Clamp01(puff * 2 - sing)) * (1 + sing * 0.35f) * Mathf.Lerp(0.75f, 1, alive);
            float my = 0.22f + mo * 1.25f - (1 - alive) * 0.08f;
            s.Mouth.localScale = new Vector3(mx, Mathf.Max(0.1f, my), 1);
            s.Cheeks.localScale = Vector3.one * (1 + puff * 0.45f);

            // A dead voice literally dims (and cools); a voice that sings flushes a little brighter.
            var c = Color.Lerp(new Color(0.52f, 0.54f, 0.68f), Color.white, alive) * (1 + sing * 0.12f);
            c.a = 1;
            s.Mat.SetColor("_BaseColor", c);
            s.Mat.SetFloat("_Desat", (1 - alive) * 0.75f);
            s.Mat.SetFloat("_Lift", sing * 0.1f);
        }

        /// The glow families and the riser/pedestal lights (web props.update), plus the YOU marker.
        void UpdateProps(float dt, double beat, float t)
        {
            // The Low tier (weak integrated GPUs) drops the screen-filling extras: the floor reflections and the
            // concert spots. Every telegraph channel stays.
            // They draw for the first frames whatever the tier (so their programs link before the round: prewarm), and
            // once the round runs they are only ever switched off, never back on (no shader link mid-round).
            var tier = RenderQuality.Level.Tier;
            _propFrames++;
            if (tier != _tier && _propFrames > 3)
            {
                bool rich = tier != QualityTier.Low;
                if (!rich || !_playing)
                {
                    _tier = tier;
                    for (int i = 0; i < N; i++) _pools[i].enabled = rich;
                    if (_env.Spotlights?.Root) _env.Spotlights.Root.SetActive(rich);
                }
            }
            float bp = Phase(beat);
            bool calm = Settings.ReduceMotion;
            for (int i = 0; i < N; i++)
            {
                var l = _lanes[i];
                float g = l.Gulp, armed = l.Armed, sing = Mathf.Clamp01(l.Sing), alive = l.AliveT;
                var c = _laneLin[i];
                float x = ChompChart.X[i];
                var ringAt = new Vector3(x, _headY[i] - 0.08f, -0.15f);
                var tiltQ = Quaternion.Euler(0.1f * Mathf.Rad2Deg, 0, 0);   // web rx -0.1 with z mirrored: the top leans away

                // ring: converges from 2.15 to 0.6 over the breath, brightening as it arrives; blows outward on release
                if (armed > 0.001f)
                {
                    float r = 2.15f - 1.55f * g, pop = 1 + Mathf.Max(0, g - 0.92f) * 3.2f;
                    _rings.Matrix[i] = Matrix4x4.TRS(ringAt, tiltQ, new Vector3(r * pop, r * pop, 1));
                    _rings.Tint[i] = c * ((0.22f + 0.95f * g * g) * armed * 2.2f);
                }
                else if (sing > 0.01f)
                {
                    float r = 0.6f + (1 - sing) * 2.6f;
                    _rings.Matrix[i] = Matrix4x4.TRS(ringAt, tiltQ, new Vector3(r, r, 1));
                    _rings.Tint[i] = c * (sing * 0.8f * 1.6f);
                }
                else { _rings.Matrix[i] = Matrix4x4.TRS(Hidden, Quaternion.identity, Vector3.one * 0.001f); _rings.Tint[i] = Color.black; }

                // pipe: fills over the breath, empties on release
                float fill = Mathf.Max(g * armed, sing * 0.55f);
                if (fill > 0.004f)
                {
                    float h = 0.04f + (PipeMax(i) - 0.04f) * fill;
                    _pipes.Matrix[i] = Matrix4x4.TRS(PipeBase(i) + Vector3.up * 0.1f, Quaternion.identity, new Vector3(1, h, 1));
                    _pipes.Tint[i] = c * ((0.30f + 0.85f * fill) * 1.5f);
                }
                else { _pipes.Matrix[i] = Matrix4x4.TRS(Hidden, Quaternion.identity, Vector3.one * 0.001f); _pipes.Tint[i] = Color.black; }

                // beam: the sung note leaving, plus a resting halo while the voice is alive
                float rest = alive * (0.16f + 0.06f * Mathf.Sin(bp * Mathf.PI * 2));
                float bb = Mathf.Max(sing, rest * 0.5f);
                if (bb > 0.006f)
                {
                    float h = 0.5f + 4.2f * sing * sing + rest * 1.4f;
                    float wsc = 0.8f + sing * 0.7f;
                    _beams.Matrix[i] = Matrix4x4.TRS(new Vector3(x, _headY[i] + BodyH[i] * (1 - FaceK) * 0.6f, 0), Quaternion.identity, new Vector3(wsc, h, wsc));
                    _beams.Tint[i] = c * ((0.16f + 1.35f * sing) * (0.35f + 0.65f * alive) * 2.4f);
                }
                else { _beams.Matrix[i] = Matrix4x4.TRS(Hidden, Quaternion.identity, Vector3.one * 0.001f); _beams.Tint[i] = Color.black; }

                // pedestal band: a low glow while alive, bright on the note, a beat breath
                float beatK = beat >= 0 && _playing ? Mathf.Exp(-6f * bp) : 0;
                _poolMats[i].SetFloat("_Intensity", 0.05f + alive * 0.07f + sing * 0.3f + g * armed * 0.08f);
                _bands.Tint[i] = c * (0.25f + alive * (0.55f + beatK * (calm ? 0.1f : 0.35f)) + sing * 1.6f + g * armed * 0.6f);
            }

            // chord bridges: link consecutive lanes that fire on the same beat
            int seg = 0, prev = -1;
            if (_chordN >= 2)
                for (int i = 0; i < N; i++)
                {
                    if (_chordFlag[i] == 0) continue;
                    if (prev >= 0 && seg < N - 1)
                    {
                        float x0 = ChompChart.X[prev], x1 = ChompChart.X[i];
                        float g = Mathf.Max(_lanes[prev].Gulp, _lanes[i].Gulp);
                        // along the caps' front edges, sloping from one cap to the other
                        Vector3 a = new Vector3(x0, _pedTop[prev] + 0.07f, -1.13f), e = new Vector3(x1, _pedTop[i] + 0.07f, -1.13f);
                        var d = e - a;
                        _bridges.Matrix[seg] = Matrix4x4.TRS((a + e) * 0.5f, Quaternion.FromToRotation(Vector3.right, d.normalized), new Vector3(d.magnitude, 1.3f, 1.3f));
                        _bridges.Tint[seg] = Color.Lerp(_laneLin[prev], _laneLin[i], 0.5f) * ((0.30f + 1.05f * g * g) * 0.5f);
                        seg++;
                    }
                    prev = i;
                }
            for (; seg < N - 1; seg++) { _bridges.Matrix[seg] = Matrix4x4.TRS(Hidden, Quaternion.identity, Vector3.one * 0.001f); _bridges.Tint[seg] = Color.black; }

            _rings.Flush(); _pipes.Flush(); _beams.Flush(); _bridges.Flush(); _bands.Flush();

            // The player's marks: the glyph and the floor ring pulse with their own breath.
            int hu = _round.Human;
            var hl = _lanes[hu];
            _youPulse = Damp(_youPulse, 0, 6, dt);
            float k = 0.9f + hl.Gulp * hl.Armed * 1.4f + Mathf.Clamp01(hl.Sing) * 1.2f + _youPulse;
            _glyphMat.SetColor("_Color", _laneLin[hu] * (1.2f * k) + new Color(0.15f, 0.15f, 0.15f) * k);
            _youMat.SetColor("_Color", new Color(1, 1, 1) * (0.55f + 0.5f * k * 0.5f));
            var pal = Look.Palette.Live;
            float pulse = Look.BeatPulse;
            _lipMat.SetColor("_Color", pal[PC.GroundRim].linear * (0.9f + pulse * (calm ? 0.1f : 0.6f)));
            // marquee chase: every third bulb lit, stepping on the eighth notes; all on through the finale
            int step = beat >= 0 && _playing ? (int)System.Math.Floor(beat * 2) : 0;
            bool allOn = beat >= ChompChart.FinaleBeat - 3.2;
            var warm = new Color(1f, 0.78f, 0.5f) * 3.2f;
            for (int q = 0; q < MarqueeN; q++)
            {
                bool on = allOn || calm || ((q + step) % 3 == 0);
                _marquee.Tint[q] = warm * (on ? 1 + pulse * 0.4f : 0.16f);
            }
            _marquee.Flush();
            _stripMat.SetFloat("_Intensity", 1 + pulse * 0.5f);
        }

        /// The web's camera: the frame, then the finale's slow push and the ending's pull-back (rig.frame calls), plus a
        /// small lean toward the human's lane on their notes.
        void UpdateCamera(double beat, float dt)
        {
            _camPush = Damp(_camPush, 0, 3.0f, dt);
            if (beat >= ChompChart.FinaleEnd)
                Look.Rig.Frame(new Vector3(0, 1.2f, 0), 7.4f, 1.2f, 0, 48, 1.4f);
            else if (beat >= ChompChart.FinaleBeat - 3.2)
                Look.Rig.Frame(new Vector3(0, 1.3f, 0), 6.7f, 1.25f, 0, 48, 1.1f);
            else
            {
                float hx = ChompChart.X[_round.Human];
                float drift = Mathf.Sin((float)beat * 0.07f) * 0.012f;
                Look.Rig.Frame(new Vector3(hx * 0.04f * _camPush, 1.05f + _camPush * 0.03f, 0), 6.9f - _camPush * 0.18f, 1.15f, drift, 48, 2.6f);
            }
        }
    }
}
