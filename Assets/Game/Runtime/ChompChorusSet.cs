// Chomp Chorus: the hand-built stage (web chompChorus/props.js + the singers), everything the game is ABOUT.
//
//   riser     one stage joining the four pedestals (web: a bench), an art-deco front, a lit lip and footlights.
//   pedestals deco drums in each lane's colour that put four very different builds' HEADS on one line (the tall
//             one stands above it: up is up), each with an air gauge window across its front whose rails breathe with its voice.
//   singers   four procedural jelly-bean singers (round, wide, tall, small: the builds carry the lane mapping),
//             with eyes, brows, cheeks and a mouth. They INFLATE as they gulp (volume-preserving squash), open
//             an O while gulping and sing wide, raise their arms on the note, and go dim and droopy when their
//             voice has dropped out of the chord.
//   telegraph the web's five channels: a breath ring CONVERGING on the head (radius = time to the note), an air
//             gauge FILLING across the pedestal's front (a bar chart), the singer inflating, colour + position + the key
//             glyph on the player's pedestal, and a chord BRIDGE linking the pedestals of lanes that fire together.
//   beams     a column of light leaving the head when a note is sung (full height on a chord), faint while alive.
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
            public float Blink = -1, NextBlink, Lean, Bounce, Rest, Roll;
        }

        Env _env;
        readonly Singer[] _singers = new Singer[N];
        MeshBatch _rings, _beams, _bridges, _bands;
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
                o.Ground.Radius = 17.6f; o.Ground.Map = "cc-floor"; o.Ground.MapTiles = 9; o.Ground.Confetti = 0;   // loose paper read as stray slivers
                o.Ground.Rings = false;
                o.Backdrop.Arches = 3; o.Backdrop.Radius = 17; o.Backdrop.Z = 9; o.Backdrop.Spacing = 5.5f;   // the outer two only ever showed as clipped corner arcs
                o.Backdrop.Glow = 1.1f; o.Backdrop.ArchWhiten = 0.12f;
                o.Backdrop.Skyline = 30; o.Backdrop.SkylineZ = 44; o.Backdrop.SkylineSpread = 130; o.Backdrop.WindowGlow = 1.5f; o.Backdrop.SkylineFogMul = 0.45f;
                o.Crowd.Style = EnvCrowd.FigureStyle.Figure; o.Crowd.Stands = EnvCrowd.StandStyle.Risers; o.Crowd.Poses = true;
                o.Crowd.Rows = 4; o.Crowd.PerRow = 34; o.Crowd.RowGrow = 2; o.Crowd.Radius = 18.4f; o.Crowd.RowDepth = 2.0f; o.Crowd.RowRise = 1.45f;
                o.Crowd.Scale = 2.05f; o.Crowd.Arc = Mathf.PI * 1.0f; o.Crowd.PaletteMix = 0.35f; o.Crowd.BrightMin = 0.62f; o.Crowd.BrightMax = 0.95f;
                o.Crowd.Deepen = 1.25f; o.Crowd.Energy = 0.35f; o.Crowd.Rim = 1.0f; o.Crowd.Center = new Vector3(0, 0.08f, 3); o.Crowd.Seed = 0xc40f;
                // Saturated, close-feeling house: less fog on the figures, a fourth pose, and steps built to each row's
                // feet so the lit nosings run under the rows instead of through their faces.
                o.Crowd.FogMul = 0.55f; o.Crowd.PoseKinds = 4; o.Crowd.RisersFollowRows = true; o.Crowd.BrightMin = 0.56f; o.Crowd.BrightMax = 0.78f; o.Crowd.Rim = 0.75f;   // a shade darker than the singers: they read first
                o.Floaters.Count = 0;   // orbiting solids only ever showed as clipped shapes in the top corners
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
            for (int i = 0; i < N; i++)
            {
                _bridgeY = Mathf.Max(_bridgeY, SingerTop(i) + 0.12f);
                var c = _laneLin[i]; float m = Mathf.Max(0.05f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
                _beamHue[i] = new Color(Mathf.Pow(c.r / m, 1.8f), Mathf.Pow(c.g / m, 1.8f), Mathf.Pow(c.b / m, 1.8f));
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
                // a reflection of the pedestal: starts at the riser's foot and runs toward the camera, a little narrower
                // than the drum (it shrank from 1.9 x 4.6: less additive overdraw near the lens)
                r.transform.localPosition = new Vector3(ChompChart.X[i] * 1.06f, 0.015f, -2.75f);
                r.transform.localRotation = Quaternion.Euler(90, 0, 0);
                r.transform.localScale = new Vector3(1.5f, 2.9f, 1);
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

        // Pedestal radii: neighbours sit 2.24 apart, so nothing may reach past 1.1 (wider drums interpenetrated at the
        // middle seam and drew a dark bow-tie of crossing back faces there).
        const float PlinthR = 1.08f, DrumTopR = 0.9f, DrumBotR = 1.0f, CapR = 0.98f;

        /// The four deco drums, one merged mesh (vertex colours carry each lane's paint), plus the player's monitor sign
        /// (key glyph + YOU), its deck ring and the bobbing marker over their singer.
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
                b.Shell(PlinthR - 0.04f, PlinthR, 0.1f, 32, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(x, RiserTop + 0.05f, 0)), dark, new Vector2(8, 0.1f));
                b.Disc(PlinthR - 0.04f, 32, Matrix4x4.Translate(new Vector3(x, RiserTop + 0.1f, 0)), dark, 0.4f);
                b.Shell(DrumTopR, DrumBotR, h, 32, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(x, RiserTop + h / 2, 0)), mid, new Vector2(6, 1));
                b.Shell(CapR, CapR - 0.03f, 0.06f, 32, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(x, top - 0.11f, 0)), dark, new Vector2(8, 0.1f));
                // cap: an edge, a bevel and the top
                b.Shell(CapR, CapR, 0.1f, 40, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(x, top - 0.03f, 0)), cap, new Vector2(8, 0.1f));
                b.Torus(CapR - 0.05f, 0.05f, 6, 48, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(x, top + 0.02f, 0), Quaternion.Euler(90, 0, 0), Vector3.one), rimC);
                b.Disc(CapR - 0.05f, 40, Matrix4x4.Translate(new Vector3(x, top + 0.02f, 0)), cap, 0.4f);
                b.Disc(0.7f, 40, Matrix4x4.Translate(new Vector3(x, top + 0.024f, 0)), Color.Lerp(cap, Color.white, 0.12f) * 0.9f, 0.3f);
                // the air gauge's track: a dark glass window across the drum's front, under the collar
                float y0 = GaugeY0(i), r0 = GaugeR(i);
                var ink = c * 0.07f + new Color(0.012f, 0.008f, 0.025f); ink.a = 1;
                b.Shell(r0 + 0.004f, r0 + 0.004f, GaugeH, 28, Mathf.PI - GaugeArc, GaugeArc * 2, Matrix4x4.Translate(new Vector3(x, y0 + GaugeH / 2, 0)), ink, new Vector2(0.01f, 0.01f));
            }
            Part("Pedestals", transform, b, mat, Vector3.zero, cast: false);
            // The gauge's two rails (top and bottom edge of the window), lit in the lane colour: the old LED band's job
            // (alive, the beat, the note) on the frame of the gauge.
            foreach (var y in new[] { -0.012f, GaugeH + 0.012f })
            {
                var pts = new Vector3[25];
                for (int k = 0; k < pts.Length; k++) { float a = Mathf.Lerp(-GaugeArc, GaugeArc, k / (pts.Length - 1f)); pts[k] = new Vector3(Mathf.Sin(a) * 1.012f, y, -Mathf.Cos(a) * 1.012f); }
                band.Tube(pts, 0.017f, 5, Matrix4x4.identity, Color.white);
            }
            _bands = new MeshBatch("GaugeRails", transform, band, N, GlowAdd(0.6f, 0));
            _bands.Renderer.receiveShadows = false;
            for (int i = 0; i < N; i++)
            {
                float r = GaugeR(i);
                _bands.Matrix[i] = Matrix4x4.TRS(new Vector3(ChompChart.X[i], GaugeY0(i), 0), Quaternion.identity, new Vector3(r, 1, r));
                _bands.Tint[i] = Color.black;
            }
            _bands.Flush();

            // The player's monitor: an upright sign on the floor in front of their pedestal, facing the camera (tilted
            // back just enough to meet its line of sight), its top edge lit in the lane colour. It carries the key glyph,
            // a big outlined arrow (the font is ASCII only), and a YOU plate; a white ring on the deck around the
            // pedestal makes it "the ringed voice".
            int hu = _round.Human;
            float hx = ChompChart.X[hu];
            var signMat = EnvKit.Surface(_env, Color.white, "cc-velvet", new Vector2(2, 1), rim: 0.9f, pulse: 0.08f, bands: 3, mapStrength: 0.25f);
            b = new MeshBuilder();
            var strip = new MeshBuilder();
            var q = SignRot;
            var n = q * Vector3.back;
            var c0 = SignCenter(hu);
            b.Box(Matrix4x4.TRS(c0, q, new Vector3(SignW, SignH, SignD)), new Color(0.075f, 0.055f, 0.11f));
            // the screen: a raised darker panel on the face, and two little feet
            b.Box(Matrix4x4.TRS(c0 + n * (SignD / 2 + 0.008f), q, new Vector3(SignW - 0.1f, SignH - 0.1f, 0.016f)), new Color(0.02f, 0.016f, 0.04f));
            foreach (var sx in new[] { -1f, 1f })
                b.Box(Matrix4x4.TRS(new Vector3(c0.x + sx * (SignW / 2 - 0.12f), 0.03f, c0.z + 0.06f), Quaternion.identity, new Vector3(0.14f, 0.06f, 0.3f)), new Color(0.05f, 0.04f, 0.08f));
            // the glyph's backing: the same arrow, larger and near-black, so the lit arrow keeps a crisp edge under bloom
            Arrow(b, hu, SignPoint(hu, 0.08f) + n * 0.02f, GlyphSize * 1.28f, q, new Color(0.01f, 0.008f, 0.02f));
            var topL = c0 + q * new Vector3(-SignW / 2 + 0.03f, SignH / 2, -SignD / 2);
            var topR = c0 + q * new Vector3(SignW / 2 - 0.03f, SignH / 2, -SignD / 2);
            strip.Tube(new[] { topL, topR }, 0.026f, 5, Matrix4x4.identity, _laneLin[hu] * 1.6f);
            Part("Monitor", transform, b, signMat, Vector3.zero);
            _stripMat = GlowAdd(0.5f, 0);
            Part("MonitorStrip", transform, strip, _stripMat, Vector3.zero).receiveShadows = false;

            _glyphMat = GlowAdd(0, 0);
            b = new MeshBuilder();
            Arrow(b, hu, SignPoint(hu, 0.08f) + n * 0.035f, GlyphSize, q, Color.white);
            Part("KeyGlyph", transform, b, _glyphMat, Vector3.zero).receiveShadows = false;
            _youMat = GlowAdd(0.5f, 0);
            b = new MeshBuilder();
            b.Torus(PlinthR + 0.08f, 0.03f, 5, 72, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(hx, RiserTop + 0.01f, 0), Quaternion.Euler(90, 0, 0), Vector3.one), Color.white);
            Part("YouRing", transform, b, _youMat, Vector3.zero).receiveShadows = false;

            var font = Resources.Load<TMP_FontAsset>("Look/CalloutFont");
            var go = new GameObject("YouTag", typeof(TextMeshPro));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = SignPoint(hu, -0.14f) + n * 0.03f;
            go.transform.localRotation = q;
            var t = go.GetComponent<TextMeshPro>();
            if (font) t.font = font;
            t.text = "YOU";
            t.fontSize = 1.3f;
            t.characterSpacing = 10;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.color = Color.white;
            t.rectTransform.sizeDelta = new Vector2(SignW, 0.3f);
            var tr = go.GetComponent<MeshRenderer>();
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.receiveShadows = false;

            // The marker over the player's singer: a chunky down-pointing chevron, white with a lane-coloured core,
            // self-lit, bobbing on the beat.
            b = new MeshBuilder();
            Chevron(b, 0.25f, 0.3f, 0, new Color(0.95f, 0.95f, 0.95f), 0.01f);
            Chevron(b, 0.16f, 0.19f, 0.065f, _laneLin[hu] * 1.1f + new Color(0.08f, 0.08f, 0.08f), -0.01f);
            var mk = EnvKit.Surface(_env, Color.white, null, null, rim: 0.2f, pulse: 0, lift: 0.75f, bands: 1, twoSided: true);
            mk.SetFloat("_ShadowRecv", 0);
            _marker = Part("YouMarker", transform, b, mk, Vector3.zero).transform;
            _marker.GetComponent<MeshRenderer>().receiveShadows = false;
            _markerY = SingerTop(hu) + 0.66f;   // room under it for the lane's word (WordY)
            _marker.localPosition = new Vector3(hx, _markerY, -0.35f);
        }

        Transform _marker;
        float _markerY;
        /// The lane's verdict word: over the singer's head, under the YOU marker's tip.
        float WordY(int i) => SingerTop(i) + 0.27f;

        // The monitor sign: width, height, depth, back-tilt; the glyph's half size.
        // On the boards in front of the marquee, low enough that it never covers the pedestal's base (it sat on the
        // riser's lip and hid the drum).
        const float SignW = 0.96f, SignH = 0.46f, SignD = 0.12f, SignTilt = 17f, GlyphSize = 0.12f;
        Material _stripMat;
        static Quaternion SignRot => Quaternion.Euler(SignTilt, 0, 0);
        static Vector3 SignCenter(int i) => new Vector3(ChompChart.X[i] * 0.88f, SignH / 2 + 0.04f, -1.95f);   // a little in toward the centre: inside the frame with room
        /// A point on the sign's face, `up` metres above its centre.
        static Vector3 SignPoint(int i, float up) => SignCenter(i) + SignRot * new Vector3(0, up, -SignD / 2);

        /// Top of the singer's silhouette, accessory included (beams leave from above it, the marker floats over it).
        static readonly float[] AccTop = { 0.36f, 0.12f, 0.13f, 0.08f };
        float SingerTop(int i) => _pedTop[i] + 0.025f + BodyH[i] + AccTop[i];

        /// A flat down-pointing triangle in XY (facing -z): `w` half width, `h` height, tip at (0, y0), `z` offset.
        static void Chevron(MeshBuilder b, float w, float h, float y0, Color c, float z)
        {
            int o = b.Count;
            foreach (var v in new[] { new Vector3(-w, y0 + h, z), new Vector3(w, y0 + h, z), new Vector3(0, y0, z) })
            { b.P.Add(v); b.N.Add(Vector3.back); b.C.Add(c); b.UV.Add(Vector2.zero); }
            b.Tri(o, o + 1, o + 2);
        }

        /// A chunky arrow (web glyphAtlas: 0 left, 1 down, 2 up, 3 right) as geometry, facing the camera.
        static void Arrow(MeshBuilder b, int lane, Vector3 at, float size, Quaternion face, Color col)
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
            foreach (var p in pts) { b.P.Add(at + face * (q * new Vector3(p.x, p.y, 0))); b.N.Add(face * Vector3.back); b.C.Add(col); b.UV.Add(Vector2.zero); }
            // head (0,1,6), shaft (2,3,4,5)
            b.T.Add(o); b.T.Add(o + 1); b.T.Add(o + 6);
            b.T.Add(o + 2); b.T.Add(o + 3); b.T.Add(o + 4); b.T.Add(o + 2); b.T.Add(o + 4); b.T.Add(o + 5);
        }

        // The air gauge: a dark glass window wrapped across the front of each drum, under the cap (off the singer's
        // silhouette), framed by two lit rails. The breath FILLS it left to right in the lane colour, a white-hot
        // leading edge, up to a white tick at the right end where the note lands; it drains on the release. The level
        // is the time left to the note (web props.js air pipe, as a bar).
        const int GaugeSeg = 22;
        const float GaugeH = 0.13f, GaugeArc = 68f * Mathf.Deg2Rad;
        /// Bottom of the gauge window: its top sits just under the collar.
        float GaugeY0(int i) => _pedTop[i] - 0.15f - GaugeH;
        /// The drum's radius at the window (the drum tapers), plus a hair so it sits proud of the paint.
        float GaugeR(int i)
        {
            float h = Mathf.Max(0.14f, _pedTop[i] - RiserTop), y = GaugeY0(i) + GaugeH * 0.5f;
            return Mathf.Lerp(DrumBotR, DrumTopR, Mathf.Clamp01((y - RiserTop) / h)) + 0.01f;
        }

        // Chord bridges: an arc over the heads from one singer to the next of the chord, as open tube segments (no
        // overlapping end caps: additive joints would bead), colour graded from one voice to the other.
        const int BridgeSeg = 12, Sparks = 3;
        MeshBatch _gauge, _ticks, _sparks, _capPools, _marqueeRefl;

        /// Breath rings, air gauges, voice beams, chord bridges and their sparks, cap pools, marquee reflections:
        /// additive families, drawn at black from here on so they link now (web props.js: InstancedMeshes, colour
        /// toward black to fade).
        void BuildGlowFamilies()
        {
            var tpl = new MeshBuilder();
            tpl.Torus(1, 0.05f, 6, 56, Mathf.PI * 2, Matrix4x4.identity, Color.white);
            _rings = new MeshBatch("BreathRings", transform, tpl, N, GlowAdd(0.55f, 0));

            // gauge fill: one slice of the window per segment (unit radius, centred on the front), rotated into place
            float dA = GaugeArc * 2 / GaugeSeg;
            tpl = new MeshBuilder();
            tpl.Shell(1.016f, 1.016f, GaugeH * 0.86f, 3, Mathf.PI - dA / 2, dA, Matrix4x4.Translate(new Vector3(0, GaugeH / 2, 0)), Color.white, Vector2.one);
            _gauge = new MeshBatch("GaugeFill", transform, tpl, N * GaugeSeg, GlowAdd(0, 0));
            // the target tick: a white bar standing across the window's right end
            tpl = new MeshBuilder();
            tpl.Capsule(0.022f, GaugeH + 0.03f, 2, 6, Matrix4x4.Translate(new Vector3(0, GaugeH / 2, -1.03f)), Color.white);
            _ticks = new MeshBatch("GaugeTicks", transform, tpl, N, GlowAdd(0.3f, 0));
            for (int i = 0; i < N; i++)
            {
                float r = GaugeR(i);
                _ticks.Matrix[i] = Matrix4x4.TRS(new Vector3(ChompChart.X[i], GaugeY0(i), 0), Quaternion.Euler(0, -(GaugeArc - dA * 0.5f) * Mathf.Rad2Deg, 0), new Vector3(r, 1, r));
            }

            // Beams: a column of light rising from ABOVE the singer (never over its face), set behind it, widening as it
            // climbs (web: the voice's cone opens upward out of the frame), fading in over its first eighth and only slowly
            // toward the top (vertex colours carry the profile, so the material needs no ramp). Soft edges (facing).
            tpl = new MeshBuilder();
            tpl.Surface(20, 10, (u, v) =>
            {
                float a = u * Mathf.PI * 2, r = Mathf.Lerp(0.26f, 0.52f, v);
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                return (d * r + Vector3.up * v, new Vector3(d.x, -0.3f, d.z).normalized);
            }, Matrix4x4.identity, Color.white, Vector2.one);
            for (int k = 0; k < tpl.Count; k++)
            {
                float v = tpl.P[k].y;
                float prof = Mathf.SmoothStep(0, 1, v / 0.12f) * Mathf.Pow(1 - v, 0.7f);
                tpl.C[k] = Color.white * prof;
            }
            _beams = new MeshBatch("VoiceBeams", transform, tpl, N, GlowAdd(0.8f, 0));

            tpl = new MeshBuilder();
            tpl.Shell(0.1f, 0.1f, 1, 10, 0, Mathf.PI * 2, Matrix4x4.Rotate(Quaternion.Euler(0, 0, 90)), Color.white, Vector2.one);
            _bridges = new MeshBatch("ChordBridges", transform, tpl, (N - 1) * BridgeSeg, GlowAdd(0.55f, 0));
            tpl = new MeshBuilder();
            tpl.Sphere(0.11f, 10, 6, Matrix4x4.identity, Color.white, Vector3.one);
            _sparks = new MeshBatch("BridgeSparks", transform, tpl, (N - 1) * Sparks, GlowAdd(0.4f, 0));

            // Cap pools: a soft lane-coloured pool of light on each cap around the singer's feet, breathing on the beat.
            tpl = new MeshBuilder();
            tpl.Disc(1, 32, Matrix4x4.identity, Color.white);
            for (int k = 0; k < tpl.Count; k++) { float r = new Vector2(tpl.P[k].x, tpl.P[k].z).magnitude; tpl.C[k] = Color.white * Mathf.Pow(1 - Mathf.Clamp01(r), 1.6f); }
            _capPools = new MeshBatch("CapPools", transform, tpl, N, GlowAdd(0, 0));
            for (int i = 0; i < N; i++) _capPools.Matrix[i] = Matrix4x4.TRS(new Vector3(ChompChart.X[i], _pedTop[i] + 0.035f, 0), Quaternion.identity, new Vector3(0.92f, 1, 0.92f));

            // The lacquered floor mirrors the marquee: a soft streak under every bulb, running toward the camera.
            tpl = new MeshBuilder();
            tpl.Surface(4, 6, (u, v) => (new Vector3((u - 0.5f) * 0.16f, 0, -v * 1.1f), Vector3.up), Matrix4x4.identity, Color.white, Vector2.one);
            for (int k = 0; k < tpl.Count; k++)
            {
                float u = tpl.P[k].x / 0.08f, v = -tpl.P[k].z / 1.1f;
                tpl.C[k] = Color.white * (Mathf.Max(0, 1 - u * u) * Mathf.Pow(1 - v, 1.8f) * Mathf.SmoothStep(0, 1, v / 0.08f + 0.3f));
            }
            _marqueeRefl = new MeshBatch("MarqueeReflections", transform, tpl, MarqueeN, GlowAdd(0, 0));
            for (int k = 0; k < MarqueeN; k++) { var p = MarqueeAt(k); _marqueeRefl.Matrix[k] = Matrix4x4.Translate(new Vector3(p.x, 0.012f, -1.34f)); }

            foreach (var m in new[] { _rings, _gauge, _ticks, _beams, _bridges, _sparks, _capPools, _marqueeRefl })
            {
                m.Renderer.receiveShadows = false;
                for (int i = 0; i < m.Tint.Length; i++)
                {
                    m.Tint[i] = Color.black;
                    if (m != _ticks && m != _capPools && m != _marqueeRefl) m.Matrix[i] = Matrix4x4.TRS(Hidden, Quaternion.identity, Vector3.one * 0.001f);
                }
                m.Flush();
            }
        }

        // ------------------------------------------------------------ singers

        Singer BuildSinger(int i)
        {
            float w = BodyW[i], h = BodyH[i];
            var s = new Singer { W = w, H = h };
            var lane = _laneLin[i];
            Color belly = Color.Lerp(lane, Color.white, 0.3f), dark = lane * 0.32f, white = new Color(0.98f, 0.97f, 0.95f);
            dark.a = 1;
            // a strong rim (1.25 -> 1.6) so the silhouette holds against the pastel house (an ink-hull outline cost ~7 ms
            // a frame under SwiftShader: the singers are a big share of the frame)
            s.Mat = EnvKit.Surface(_env, Color.white, null, null, rim: 1.6f, pulse: 0.1f, bands: 3);
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
        /// wide = a beret (a bow tie on the face read as a moustache with the open mouth), tall = a quiff, small = a bow.
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
                    // a squashed, tilted beret in deep plum with a little stalk, sitting back on the crown
                    var beret = new Color(0.16f, 0.05f, 0.2f);
                    var q = Quaternion.Euler(-8, 0, -13);
                    b.Sphere(0.3f, 18, 10, Matrix4x4.TRS(new Vector3(0.07f, h * 0.975f, 0.04f), q, Vector3.one), beret, new Vector3(1.18f, 0.3f, 1.05f));
                    b.Sphere(0.04f, 8, 6, Matrix4x4.Translate(new Vector3(0.05f, h * 0.975f + 0.1f, 0.04f)), beret * 1.4f, new Vector3(1, 1.3f, 1));
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

        // Each singer has its own body language (degrees): arms at rest (out from the side, forward), and on the note
        // (left, right). round = open arms; wide = hands on hips; tall = one arm conducting; small = hands clasped in front.
        static readonly float[] RestOut = { 20, 40, 8, 14 }, RestFwd = { 0, -26, 0, 46 };   // +fwd = toward the camera
        static readonly float[] SingL = { 105, 95, 150, 115 }, SingR = { 105, 95, 55, 115 };

        void UpdateSinger(int i, float dt, float t, double beat, bool finaleHold)
        {
            var s = _singers[i];
            var l = _lanes[i];
            bool calm = Settings.ReduceMotion;
            float puff = l.Gulp * l.Armed;
            // Curtain call (a finale that landed): every singer is back up, cheering, the dropped voices included.
            float alive = Mathf.Max(l.AliveT, _curtain);
            float sing = Mathf.Max(Mathf.Clamp01(l.Sing), _curtain * (0.62f + 0.22f * Mathf.Sin(t * 7 + i * 1.7f)));
            float ph = Phase(beat);
            bool hold = finaleHold && _round.Alive[i];
            float dead = 1 - alive;
            // Resting: alive, nothing coming, nothing being sung: eyes close, a content smile.
            bool resting = alive > 0.6f && l.Armed < 0.05f && sing < 0.12f && _playing && !hold;
            s.Rest = Damp(s.Rest, resting ? 1 : 0, resting ? 2.2f : 14, dt);

            // Breath and beat: an idle breath on a slow sine, a squash-and-stretch on every beat while alive.
            float breath = Mathf.Sin(t * 2.1f + i * 1.3f) * 0.012f;
            float beatSq = _playing && beat >= 0 ? Mathf.Exp(-9f * ph) * (calm ? 0.015f : 0.06f) * alive : 0;
            float rebound = _playing && beat >= 0 && !calm ? Mathf.Exp(-6f * Mathf.Abs(ph - 0.22f) * 3) * 0.025f * alive : 0;
            // Inflate (volume-preserving: wide grows as tall shrinks a little), stretch on the note; a dead voice
            // wilts: shorter, wider at the bottom, slumped.
            float wide = 1 + puff * 0.26f + breath + beatSq - rebound - sing * 0.06f + dead * 0.08f;
            float tall = 1 + puff * 0.06f - beatSq * 0.9f + rebound * 1.2f + sing * 0.15f - dead * 0.16f;
            if (hold) { tall += 0.05f + Mathf.Sin(t * 9 + i) * 0.015f; wide -= 0.03f; }
            float wob = calm ? 0 : l.Flinch * Mathf.Sin(t * 46) * 0.06f;
            s.Body.localScale = new Vector3(wide + wob, tall - wob, wide * (1 + puff * 0.08f));
            // lean back into the note, slump forward and sideways when the voice is out; a slow sway on the bar
            float sway = calm ? 0 : Mathf.Sin((float)beat * Mathf.PI * 0.5f + i) * 2.2f * alive;
            float lean = -sing * 7f + dead * 17f + puff * -4f - (hold ? 6 : 0);
            s.Lean = Damp(s.Lean, lean, 10, dt);
            s.Roll = Damp(s.Roll, dead * (i % 2 == 0 ? 7 : -7), 6, dt);
            s.Body.localRotation = Quaternion.Euler(s.Lean, 0, sway + s.Roll);

            // Arms: each singer's own rest pose, drawn up as the breath is taken, flung up on the note (each its own
            // way), straight up through the held finale, hanging limp when out.
            float restOut = Mathf.Lerp(RestOut[i], 4, dead), restFwd = Mathf.Lerp(RestFwd[i], 12, dead);
            float upL = SingL[i], upR = SingR[i];
            float outL = restOut + puff * 30 + sing * (upL - restOut);
            float outR = restOut + puff * 30 + sing * (upR - restOut);
            float fwd = restFwd * (1 - Mathf.Max(puff, sing)) - puff * 15 + sing * 10;
            if (hold) { outL = Mathf.Max(outL, 150 + Mathf.Sin(t * 9 + i) * 8); outR = Mathf.Max(outR, 150 + Mathf.Sin(t * 9 + i + 1) * 8); fwd = 0; }
            float flap = calm ? 0 : Mathf.Sin(t * 3 + i) * 4 * alive;
            if (i == 2 && sing > 0.2f && !calm) outL += Mathf.Sin(t * 14) * 12 * sing;   // the conductor beats time
            s.ArmL.localRotation = Quaternion.Euler(fwd, 0, -outL - flap);
            s.ArmR.localRotation = Quaternion.Euler(fwd, 0, outR + flap);

            // Eyes: blink now and then; wide on the gulp; squeezed shut with joy on the note; closed at rest; heavy
            // lids when out.
            s.NextBlink -= dt;
            if (s.NextBlink <= 0) { s.Blink = 0; s.NextBlink = 2.2f + Mathf.Repeat(i * 1.7f + t * 0.37f, 2.6f); }
            float blink = 1;
            if (s.Blink >= 0) { s.Blink += dt / 0.14f; blink = Mathf.Abs(1 - 2 * Mathf.Clamp01(s.Blink)); if (s.Blink >= 1) s.Blink = -1; }
            float open = Mathf.Lerp(0.34f, 1, alive) * (1 + puff * 0.2f) * (1 - sing * 0.55f) * Mathf.Max(0.08f, blink);
            open = Mathf.Lerp(open, 0.09f, s.Rest * 0.92f);
            if (hold) open = Mathf.Min(open, 0.3f);
            open = Mathf.Max(0.08f, open);
            float es = 1 + puff * 0.14f;
            s.EyeL.localScale = new Vector3(es, open * es, es);
            s.EyeR.localScale = new Vector3(es, open * es, es);
            // Brows: up on the breath, sad tilt when out, a flinch knits them, relaxed at rest.
            float browUp = puff * 0.06f + sing * 0.035f - dead * 0.03f - l.Flinch * 0.02f - s.Rest * 0.01f;
            float tilt = dead * 22 - l.Flinch * 14 + puff * -8;
            s.BrowL.localPosition = new Vector3(s.BrowL.localPosition.x, s.BrowY + browUp, s.BrowL.localPosition.z);
            s.BrowR.localPosition = new Vector3(s.BrowR.localPosition.x, s.BrowY + browUp, s.BrowR.localPosition.z);
            s.BrowL.localRotation = Quaternion.Euler(0, 0, -tilt);
            s.BrowR.localRotation = Quaternion.Euler(0, 0, tilt);

            // Mouth: a small smile at rest, a small round O while gulping (cheeks puffed), wide open singing, a big
            // round O held through the finale, a small flat line when out.
            float mo = Mathf.Max(puff * 0.45f, sing);
            float mx = Mathf.Lerp(0.9f, 0.55f, Mathf.Clamp01(puff * 2 - sing)) * (1 + sing * 0.35f) * Mathf.Lerp(0.7f, 1, alive);
            float my = 0.22f + mo * 1.3f - dead * 0.1f;
            if (hold) { mx = 0.95f; my = 1.75f + Mathf.Sin(t * 9 + i) * 0.08f; }
            s.Mouth.localScale = new Vector3(mx, Mathf.Max(0.1f, my), 1);
            s.Cheeks.localScale = Vector3.one * (1 + puff * 0.75f);

            // A dead voice goes grey (pale and still lit: a resting voice, not a hole in the picture); a voice that sings
            // flushes a little brighter.
            var c = Color.Lerp(new Color(0.8f, 0.8f, 0.86f), Color.white, alive) * (1 + sing * 0.12f);
            c.a = 1;
            s.Mat.SetColor("_BaseColor", c);
            s.Mat.SetFloat("_Desat", dead * 0.82f);
            s.Mat.SetFloat("_Lift", sing * 0.1f + dead * 0.28f);
        }

        /// The glow families and the riser/pedestal lights (web props.update), plus the YOU marker.
        void UpdateProps(float dt, double beat, float t, bool finaleHold)
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
                    _marqueeRefl.Renderer.enabled = rich;
                    if (_env.Spotlights?.Root) _env.Spotlights.Root.SetActive(rich);
                }
            }
            float bp = Phase(beat);
            bool calm = Settings.ReduceMotion;
            float beatK = beat >= 0 && _playing ? Mathf.Exp(-6f * bp) : 0;
            _chordFx = Damp(_chordFx, 0, 2.2f, dt);
            _column = Mathf.Max(0, _column - dt / 1.1f);   // the chord column lasts about a second
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
                    _rings.Tint[i] = c * ((0.22f + 0.95f * g * g) * armed * 1.9f);
                }
                else if (sing > 0.01f)
                {
                    float r = 0.6f + (1 - sing) * 2.6f;
                    _rings.Matrix[i] = Matrix4x4.TRS(ringAt, tiltQ, new Vector3(r, r, 1));
                    _rings.Tint[i] = c * (sing * 1.1f);
                }
                else { _rings.Matrix[i] = Matrix4x4.TRS(Hidden, Quaternion.identity, Vector3.one * 0.001f); _rings.Tint[i] = Color.black; }

                // gauge: the window fills left to right over the breath (full on the note), drains on the release; the
                // leading slice runs white-hot, the tick flashes white when the gauge is full.
                float fill = Mathf.Max(g * armed, sing * sing);
                float lit = fill * GaugeSeg, gr = GaugeR(i), gy = GaugeY0(i);
                var lc = c / Mathf.Max(0.05f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));   // lane hue at full value: a clear level, never clipped
                for (int k = 0; k < GaugeSeg; k++)
                {
                    int idx = i * GaugeSeg + k;
                    float on = Mathf.Clamp01(lit - k);
                    if (on > 0.004f)
                    {
                        float a = Mathf.Lerp(-GaugeArc, GaugeArc, (k + 0.5f) / GaugeSeg);
                        _gauge.Matrix[idx] = Matrix4x4.TRS(new Vector3(x, gy, 0), Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), new Vector3(gr, 1, gr));
                        bool head = lit - k < 1.5f && fill < 0.999f;
                        var gc = head ? Color.Lerp(lc, Color.white, 0.6f) : lc * (0.62f + 0.18f * k / GaugeSeg);
                        _gauge.Tint[idx] = gc * on * (0.75f + 0.25f * armed);
                    }
                    else { _gauge.Matrix[idx] = Matrix4x4.TRS(Hidden, Quaternion.identity, Vector3.one * 0.001f); _gauge.Tint[idx] = Color.black; }
                }
                float full = Mathf.Clamp01((g * armed - 0.9f) / 0.1f);
                _ticks.Tint[i] = Color.Lerp(new Color(0.5f, 0.5f, 0.5f), new Color(1.6f, 1.6f, 1.6f), Mathf.Max(full, sing * 0.6f));

                // beam: the sung note leaving above the singer, behind it, plus a faint resting glow while alive. The tint
                // is the lane hue at full value, deepened (_beamHue: added over the pink sky a pastel hue reads white), its
                // peak capped, a chord / the finale's payoff stretches every live voice's beam into a full-height column.
                float rest = alive * (0.12f + 0.05f * Mathf.Sin(bp * Mathf.PI * 2));
                float sb = finaleHold ? Mathf.Max(Mathf.Min(sing, 0.7f), 0.45f) : sing;
                float col = _column * (alive > 0.5f && (sing > 0.05f || finaleHold) ? 1 : 0);
                // (no resting glow column: four always-on additive columns cost fill on weak GPUs for little read)
                float bb = Mathf.Max(sb, col);
                if (bb > 0.006f)
                {
                    float h = 0.9f + 3.6f * sb + rest * 1.0f + col * 4.5f;
                    float wsc = 0.8f + sb * 0.55f + col * 0.35f;
                    _beams.Matrix[i] = Matrix4x4.TRS(new Vector3(x, SingerTop(i) + 0.04f, 0.3f), Quaternion.identity, new Vector3(wsc, h, wsc));
                    _beams.Tint[i] = _beamHue[i] * Mathf.Min(1.35f + 0.25f * col, (0.12f + 1.1f * sb + 0.7f * col) * (0.35f + 0.65f * alive));
                }
                else { _beams.Matrix[i] = Matrix4x4.TRS(Hidden, Quaternion.identity, Vector3.one * 0.001f); _beams.Tint[i] = Color.black; }

                // pedestal band: a low glow while alive, bright on the note, a beat breath
                _poolMats[i].SetFloat("_Intensity", 0.05f + alive * (0.06f + beatK * (calm ? 0.02f : 0.06f)) + sing * 0.22f + g * armed * 0.06f);
                _bands.Tint[i] = c * (0.25f + alive * (0.55f + beatK * (calm ? 0.1f : 0.35f)) + sing * 1.4f + g * armed * 0.6f);
                // cap pool: the spot on the singer, breathing on the beat, flaring on the note
                _capPools.Tint[i] = c * (0.05f + alive * (0.1f + beatK * (calm ? 0.04f : 0.14f)) + sing * 0.32f + _chordFx * 0.15f);
            }

            // chord bridges: arc over the heads between consecutive lanes that fire on the same beat, colour graded,
            // with sparks travelling along them toward the later voice.
            int seg = 0, prev = -1, br = 0;
            if (_chordN >= 2)
                for (int i = 0; i < N; i++)
                {
                    if (_chordFlag[i] == 0) continue;
                    if (prev >= 0 && br < N - 1)
                    {
                        float g = Mathf.Max(_lanes[prev].Gulp * _lanes[prev].Armed, _lanes[i].Gulp * _lanes[i].Armed);
                        float k = 0.22f + 0.4f * g * g;   // two voices' arcs and sparks overlap additively: kept low, never white
                        // every arc leaves from one common line over all the heads, in one plane behind them: the chain
                        // reads as one garland of the chord instead of loops crossing at different heights
                        var A = new Vector3(ChompChart.X[prev], _bridgeY, 0.25f);
                        var B = new Vector3(ChompChart.X[i], _bridgeY, 0.25f);
                        float arc = 0.28f + 0.06f * Mathf.Abs(B.x - A.x);
                        for (int q = 0; q < BridgeSeg; q++, seg++)
                        {
                            float u0 = q / (float)BridgeSeg, u1 = (q + 1) / (float)BridgeSeg;
                            Vector3 p0 = BridgeAt(A, B, arc, u0), p1 = BridgeAt(A, B, arc, u1), d = p1 - p0;
                            _bridges.Matrix[seg] = Matrix4x4.TRS((p0 + p1) * 0.5f, Quaternion.FromToRotation(Vector3.right, d.normalized), new Vector3(d.magnitude, 1.35f, 1.35f));
                            float fade = Mathf.SmoothStep(0, 1, Mathf.Min(u0, 1 - u1) * 10 + 0.15f);   // melt into the heads
                            _bridges.Tint[seg] = Color.Lerp(_laneLin[prev], _laneLin[i], (u0 + u1) * 0.5f) * (k * fade);
                        }
                        for (int q = 0; q < Sparks; q++)
                        {
                            float u = Mathf.Repeat(t * 0.75f + q / (float)Sparks, 1);
                            int si = br * Sparks + q;
                            _sparks.Matrix[si] = Matrix4x4.TRS(BridgeAt(A, B, arc, u), Quaternion.identity, Vector3.one * (1.2f + g * 0.7f));
                            var sc = Color.Lerp(Color.Lerp(_laneLin[prev], _laneLin[i], u), Color.white, 0.5f);
                            _sparks.Tint[si] = sc * ((0.2f + 0.4f * g) * Mathf.Sin(Mathf.PI * u));
                        }
                        br++;
                    }
                    prev = i;
                }
            for (; seg < _bridges.Tint.Length; seg++) { _bridges.Matrix[seg] = Matrix4x4.TRS(Hidden, Quaternion.identity, Vector3.one * 0.001f); _bridges.Tint[seg] = Color.black; }
            for (int si = br * Sparks; si < _sparks.Tint.Length; si++) { _sparks.Matrix[si] = Matrix4x4.TRS(Hidden, Quaternion.identity, Vector3.one * 0.001f); _sparks.Tint[si] = Color.black; }

            _rings.Flush(); _gauge.Flush(); _ticks.Flush(); _beams.Flush(); _bridges.Flush(); _sparks.Flush(); _bands.Flush(); _capPools.Flush();

            // The player's marks: the glyph and the floor ring pulse with their own breath; the marker bobs on the beat.
            int hu = _round.Human;
            var hl = _lanes[hu];
            _youPulse = Damp(_youPulse, 0, 6, dt);
            float kk = 0.9f + hl.Gulp * hl.Armed * 1.0f + Mathf.Clamp01(hl.Sing) * 0.8f + _youPulse * 0.6f;
            _glyphMat.SetColor("_Color", _laneLin[hu] * Mathf.Min(1.5f, 0.95f * kk) + new Color(0.2f, 0.2f, 0.2f) * Mathf.Min(1.3f, kk));
            _youMat.SetColor("_Color", new Color(1, 1, 1) * (0.55f + 0.5f * kk * 0.5f));
            float bob = calm ? 0 : (beat >= 0 && _playing ? Mathf.Exp(-7f * bp) * 0.1f : Mathf.Sin(t * 3) * 0.04f);
            _marker.localPosition = new Vector3(ChompChart.X[hu], _markerY + bob + hl.Gulp * hl.Armed * 0.12f, -0.35f);
            float ms = 1 + _youPulse * 0.25f;
            _marker.localScale = new Vector3(ms, ms, 1);
            var pal = Look.Palette.Live;
            float pulse = Look.BeatPulse;
            _lipMat.SetColor("_Color", pal[PC.GroundRim].linear * (0.9f + pulse * (calm ? 0.1f : 0.6f)));
            // marquee chase: every third bulb lit, stepping on the eighth notes; all on through the finale and a chord
            int step = beat >= 0 && _playing ? (int)System.Math.Floor(beat * 2) : 0;
            bool allOn = beat >= ChompChart.FinaleBeat - 3.2 || _chordFx > 0.3f;
            var warm = new Color(1f, 0.78f, 0.5f) * 3.2f;
            for (int q = 0; q < MarqueeN; q++)
            {
                bool on = allOn || calm || ((q + step) % 3 == 0);
                _marquee.Tint[q] = warm * (on ? 1 + pulse * 0.4f : 0.16f);
                _marqueeRefl.Tint[q] = _marquee.Tint[q] * 0.07f;
            }
            _marquee.Flush(); _marqueeRefl.Flush();
            _stripMat.SetFloat("_Intensity", 1 + pulse * 0.5f);
        }

        float _bridgeY;
        readonly Color[] _beamHue = new Color[N];

        static Vector3 BridgeAt(Vector3 a, Vector3 b, float arc, float u) => Vector3.Lerp(a, b, u) + Vector3.up * (Mathf.Sin(Mathf.PI * u) * arc);

        float _chordFx, _column, _curtain;

        // Framing (reworked from the web's 1.05 / 6.9 / 1.15 / 48): aimed higher and a little tighter, so the stage
        // sits lower in the frame: less dead floor under the riser, the whole skyline over the crowd, the monitor sign
        // inside the frame with room to spare at 16:9 and 16:10.
        // Round 3: aimed higher still and a touch closer (the floor under the marquee was a quarter of the frame).
        static readonly Vector3 CamTarget = new Vector3(0, 1.8f, 0);
        const float CamDist = 7.3f, CamHeight = 0.95f, CamFov = 46;

        /// The camera: the frame, then the finale's slow push and the ending's pull-back (rig.frame calls), plus a
        /// small lean toward the human's lane on their notes and a punch-in on a big chord.
        void UpdateCamera(double beat, float dt)
        {
            _camPush = Damp(_camPush, 0, 3.0f, dt);
            if (beat >= ChompChart.FinaleEnd)
                Look.Rig.Frame(CamTarget + new Vector3(0, 0.1f, 0), CamDist + 0.5f, CamHeight + 0.1f, 0, CamFov, 1.4f);
            else if (beat >= ChompChart.FinaleBeat - 3.2)
                Look.Rig.Frame(CamTarget + new Vector3(0, 0.15f, 0), CamDist - 0.35f, CamHeight + 0.08f, 0, CamFov, 1.1f);
            else
            {
                float hx = ChompChart.X[_round.Human];
                float drift = Mathf.Sin((float)beat * 0.07f) * 0.012f;
                Look.Rig.Frame(CamTarget + new Vector3(hx * 0.04f * _camPush, _camPush * 0.03f, 0), CamDist - _camPush * 0.18f, CamHeight, drift, CamFov, 2.6f);
            }
        }
    }
}
