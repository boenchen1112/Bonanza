// The crowd and its stands (web render/env/crowd.js for the kit's risers + blob spectators, chars/crowd.js for
// the ballpark's figure rows). Hundreds of spectators in ONE draw: every figure is baked into one static mesh
// and the Bonanza/Crowd vertex shader bounces it on the beat (see Crowd.shader), so a frame costs a dozen
// material floats and no vertex writes. The crowd is a metronome the player sees without looking at it, and it
// reacts: Hype (combo milestone: higher, faster, a wave), Deflate (a miss: the stands sag), Wave, Cheer.
//
// Figures are baked in a shuffled order, so the quality ladder trims the crowd (RenderQuality.CrowdBudget) by
// shortening the index range: evenly thinned, no rebuild. Colours mix a pastel set with the palette's crowd
// colours (live: the palette slots are material colours), and back rows read darker (aerial perspective).

using UnityEngine;
using UnityEngine.Rendering;

namespace Bonanza.Look
{
    public sealed class EnvCrowd : EnvPiece
    {
        public enum FigureStyle { Blob, Figure }
        public enum StandStyle { None, Risers, Bowl }

        public sealed class Options
        {
            public FigureStyle Style = FigureStyle.Blob;
            public StandStyle Stands = StandStyle.Risers;
            public int Rows = 3, PerRow = 44, RowGrow = 0;
            public float Radius = 18.1f, RowDepth = 2.2f, RowRise = 2.4f, Arc = Mathf.PI * 1.05f, Scale = 1;
            /// Arc centre (Unity space). The web's z = -3 is Unity's +3.
            public Vector3 Center = new Vector3(0, 0.08f, 3);
            /// Beats of phase swept across the arc (0 = everyone on the beat together).
            public float Sweep = 0;
            /// 0 = the pastel set, 1 = the palette's crowd colours.
            public float PaletteMix = 1;
            /// Brightness range of a figure (random per figure).
            public float BrightMin = 0.75f, BrightMax = 1.0f;
            /// Saturation push on the pastel set (power on linear colour; 1 = as authored).
            public float Deepen = 1;
            public float Energy = 0.45f;
            // Risers (Stands = Risers): open shells per row behind the figures.
            public float RiserY = -0.9f;
            // Bowl (Stands = Bowl): a sloped seat shell, a front wall and a roof ring (the ballpark).
            public float BowlTop = 18.4f, BowlBottom = 11f, BowlHeight = 3.9f, BowlY = 2.55f, BowlArc = Mathf.PI * 1.02f;
            public float WallRadius = 11f, WallHeight = 1.35f, WallY = 0.28f;
            public float RoofRadius = 18.6f, RoofY = 7.4f, RoofTube = 0.22f;
            public Color? StandColor;
            public Color RoofColor = new Color(0.11f, 0.14f, 0.21f);
            public string SeatMap = "seats";
            public uint Seed = 0xc0ffee;
            /// Figures only: give each spectator one of three arm poses (low V that swings, arms up that pump, a
            /// clap overhead), animated in the vertex shader. Off = every figure in the authored low V.
            public bool Poses;
            /// Rim multiplier override for the figures (null = the style's default).
            public float? Rim;
            /// Scale of the front row relative to the rest (the row nearest the camera reads oversized).
            public float FrontRowScale = 1;
        }

        static readonly uint[] Pastel = { 0xff7a5c, 0x6fc9e8, 0xd98ae0, 0xa9d98a, 0xffc86a, 0x8a95e0, 0xff9ab5, 0x9aa6bd, 0xffe08a, 0x7fe0c8 };

        public readonly Options O;
        public int Count { get; }
        readonly Mesh _mesh;
        readonly Material _mat, _stand, _roof;
        readonly int _indexPerFigure;
        float _energy, _energyTarget, _hype, _sag, _sagGoal, _cap = 1, _waveT = -1, _waveSpeed = 1.6f, _waveAmt, _glow;
        int _shownFigures = -1;
        float _glowBase;

        static readonly int IdBeat = Shader.PropertyToID("_Beat"), IdEighths = Shader.PropertyToID("_Eighths"),
            IdJump = Shader.PropertyToID("_JumpH"), IdLean = Shader.PropertyToID("_LeanA"), IdSag = Shader.PropertyToID("_Sag"),
            IdWaveT = Shader.PropertyToID("_WaveT"), IdWaveAmt = Shader.PropertyToID("_WaveAmt"), IdGlow = Shader.PropertyToID("_Glow"),
            IdMix = Shader.PropertyToID("_PalMix"), IdArms = Shader.PropertyToID("_Arms");
        static readonly int[] IdSlot = { Shader.PropertyToID("_C0"), Shader.PropertyToID("_C1"), Shader.PropertyToID("_C2"), Shader.PropertyToID("_C3"), Shader.PropertyToID("_C4") };

        public EnvCrowd(Env env, Options o)
        {
            O = o;
            _energy = _energyTarget = o.Energy;
            Root = new GameObject("Crowd");
            Root.transform.SetParent(env.Root, false);
            var t = Root.transform;
            var rng = new FxRng(o.Seed);

            // ---- stands
            if (o.Stands == StandStyle.Risers)
            {
                _stand = EnvKit.Surface(env, Color.white, null, null, rim: 0.55f, pulse: 0.05f, bands: 2, twoSided: true);
                var b = new MeshBuilder();
                for (int r = 0; r < o.Rows; r++)
                {
                    float rad = o.Radius - 0.6f + r * o.RowDepth, h = 1.5f + r * 1.5f;
                    b.Shell(rad, rad, h, 56, -o.Arc / 2, o.Arc,
                        Matrix4x4.Translate(new Vector3(o.Center.x, o.RiserY + h / 2 - 0.4f + r * 0.9f, o.Center.z)), Color.white, Vector2.one, inward: true);
                }
                EnvKit.Static(env, "Risers", t, b, _stand).receiveShadows = false;
            }
            else if (o.Stands == StandStyle.Bowl)
            {
                _stand = EnvKit.Surface(env, Color.white, o.SeatMap, new Vector2(24, 1), rim: 0.35f, pulse: 0.04f, bands: 3, twoSided: true);
                var b = new MeshBuilder();
                b.Shell(o.BowlTop, o.BowlBottom, o.BowlHeight, 56, -o.BowlArc / 2, o.BowlArc,
                    Matrix4x4.Translate(new Vector3(0, o.BowlY, o.Center.z)), Color.white, new Vector2(1, 1), inward: true);
                b.Shell(o.WallRadius, o.WallRadius, o.WallHeight, 56, -o.BowlArc / 2, o.BowlArc,
                    Matrix4x4.Translate(new Vector3(0, o.WallY, o.Center.z)), new Color(0.82f, 0.82f, 0.86f, 1), new Vector2(24, 0.12f), inward: true);
                EnvKit.Static(env, "Stands", t, b, _stand).receiveShadows = false;
                if (o.RoofTube > 0)   // 0 = no roof ring (a truss over the far stands reads as a bar across the horizon)
                {
                    _roof = EnvKit.Surface(env, o.RoofColor, null, null, rim: 0.8f, pulse: 0.05f, bands: 2);
                    b = new MeshBuilder();
                    b.Torus(o.RoofRadius, o.RoofTube, 8, 64, o.BowlArc, Matrix4x4.TRS(new Vector3(0, o.RoofY, o.Center.z), Quaternion.Euler(90, 0, 0), Vector3.one),
                        Color.white, Mathf.PI / 2 - o.BowlArc / 2);
                    EnvKit.Static(env, "Roof", t, b, _roof).receiveShadows = false;
                }
            }

            // ---- the figures
            var tpl = new MeshBuilder();
            var armSide = new System.Collections.Generic.List<float>();
            if (o.Style == FigureStyle.Figure) FigureTemplate(tpl, armSide); else BlobTemplate(tpl);
            bool poses = o.Poses && o.Style == FigureStyle.Figure;
            int n = 0;
            for (int r = 0; r < o.Rows; r++) n += o.PerRow + r * o.RowGrow;
            Count = n;
            // Layout first, then bake in a shuffled order (budget trimming thins evenly).
            var root = new Vector4[n]; var prm = new Vector4[n]; var slot = new Vector4[n]; var tint = new Color[n];
            int k = 0;
            for (int r = 0; r < o.Rows; r++)
            {
                int cnt = o.PerRow + r * o.RowGrow;
                float rad = o.Radius + r * o.RowDepth;
                float y = o.Center.y + r * o.RowRise;
                for (int i = 0; i < cnt; i++, k++)
                {
                    float u = cnt > 1 ? i / (float)(cnt - 1) : 0.5f;
                    float a = (u - 0.5f) * o.Arc + (rng.Next() - 0.5f) * (o.Arc / cnt) * 0.8f;
                    float rr = rad + (rng.Next() - 0.5f) * 0.3f;
                    float yaw = a + Mathf.PI + (rng.Next() - 0.5f) * 0.25f;
                    root[k] = new Vector4(o.Center.x + Mathf.Sin(a) * rr, y + (rng.Next() - 0.5f) * 0.06f, o.Center.z + Mathf.Cos(a) * rr, yaw);
                    // Phase: mostly on the beat (a crowd claps together), a few stragglers, plus the optional sweep.
                    float ph = (rng.Next() - 0.5f) * 0.17f + (rng.Next() < 0.08f ? rng.Next() * 0.6f : 0) + u * o.Sweep + r * o.Sweep * 0.1f;
                    prm[k] = new Vector4(ph, u, (0.86f + rng.Next() * 0.34f) * o.Scale * (r == 0 ? o.FrontRowScale : 1), 0.7f + rng.Next() * 0.6f);
                    float dim = 1 - r * 0.055f;
                    float bright = Mathf.Lerp(o.BrightMin, o.BrightMax, rng.Next()) * dim;
                    // Pose: 0 low V (most), 1 arms up, 2 clapping overhead. Drawn after the colour so the rng stream
                    // (and so every figure's colour and place) is the same with poses on or off.
                    slot[k] = new Vector4((k * 7) % 5, bright, 0, 0);
                    var pc = Palette.Hex(Pastel[(int)(rng.Next() * Pastel.Length) % Pastel.Length]).linear;
                    // Deepen the pastel a little (gamma on linear): small bodies at a distance read washed out otherwise.
                    tint[k] = new Color(Mathf.Pow(pc.r, o.Deepen), Mathf.Pow(pc.g, o.Deepen), Mathf.Pow(pc.b, o.Deepen), 1);
                    if (poses) { float pr = Hash01((uint)k * 2654435761u ^ o.Seed); slot[k].z = pr < 0.55f ? 0 : pr < 0.8f ? 1 : 2; }
                }
            }
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = n - 1; i > 0; i--) { int j = (int)(rng.Next() * (i + 1)) % (i + 1); (order[i], order[j]) = (order[j], order[i]); }

            var mb = new MeshBuilder { UV0x = new System.Collections.Generic.List<Vector4>(), UV1x = new System.Collections.Generic.List<Vector4>(), UV2x = new System.Collections.Generic.List<Vector4>() };
            int tv = tpl.Count;
            for (int q = 0; q < n; q++)
            {
                int f = order[q];
                int baseV = mb.Count;
                for (int v = 0; v < tv; v++)
                {
                    mb.P.Add(tpl.P[v]); mb.N.Add(tpl.N[v]);
                    var shade = tpl.C[v].r;   // template colour: 1 on the body, dark on the eyes
                    var c = tint[f]; c.a = shade;
                    mb.C.Add(c);
                    var sl = slot[f];
                    sl.w = poses && v < armSide.Count ? armSide[v] : 0;
                    mb.UV0x.Add(root[f]); mb.UV1x.Add(prm[f]); mb.UV2x.Add(sl);
                }
                for (int i = 0; i < tpl.T.Count; i++) mb.T.Add(tpl.T[i] + baseV);
            }
            _indexPerFigure = tpl.T.Count;
            _mesh = env.Own(mb.ToMesh("Crowd"));
            // The vertex shader moves every figure to its root: the bounds are the roots' (the baked positions are
            // figure-local, near the origin), plus the room a figure jumps and leans into.
            var bnd = new Bounds(new Vector3(root[0].x, root[0].y, root[0].z), Vector3.zero);
            for (int i = 1; i < n; i++) bnd.Encapsulate(new Vector3(root[i].x, root[i].y, root[i].z));
            bnd.Expand(new Vector3(3, 4, 3));
            _mesh.bounds = bnd;
            _mat = env.Mat(EnvKit.CrowdMat);
            _mat.SetFloat(IdMix, o.PaletteMix);
            // Blobs: the kit's punchy rim and two bands (web env crowd). Figures: soft, smooth shading like the web's
            // Lambert crowd, so 200 small bodies read as rounded people rather than flat stickers.
            if (o.Style == FigureStyle.Blob) { _mat.SetFloat("_Rim", 1.6f); _mat.SetFloat("_Bands", 2); _mat.SetFloat("_Lift", 0.04f); }
            else { _mat.SetFloat("_Rim", 0.55f); _mat.SetFloat("_Bands", 3); _mat.SetFloat("_Softness", 0.5f); _mat.SetFloat("_Lift", 0); }
            if (o.Rim.HasValue) _mat.SetFloat("_Rim", o.Rim.Value);
            _glowBase = o.Style == FigureStyle.Blob ? 0.12f : 0.03f;
            EnvKit.Renderer("CrowdFigures", t, _mesh, _mat).receiveShadows = false;
            OnQuality();
        }

        // ------------------------------------------------------------ templates (feet at y = 0, facing +z)

        /// chars/crowd.js figureGeometry: body, head, two stubby arms in a low V, two dark eye bumps. ~190 tris.
        /// `armSide` gets one entry per vertex: -1 / +1 on the left / right arm, 0 elsewhere (the pose channel).
        static void FigureTemplate(MeshBuilder b, System.Collections.Generic.List<float> armSide)
        {
            var white = Color.white;
            b.Capsule(0.17f, 0.30f, 2, 7, Matrix4x4.Translate(new Vector3(0, 0.30f, 0)), white);
            b.Sphere(0.155f, 8, 5, Matrix4x4.Translate(new Vector3(0, 0.62f, 0)), white, Vector3.one);
            while (armSide.Count < b.Count) armSide.Add(0);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                b.Capsule(0.055f, 0.26f, 1, 4, Matrix4x4.TRS(new Vector3(sx * 0.20f, 0.42f, 0), Quaternion.Euler(0, 0, sx * 0.72f * Mathf.Rad2Deg), Vector3.one), white);
                while (armSide.Count < b.Count) armSide.Add(sx);
            }
            var eye = new Color(0.12f, 0.12f, 0.12f, 1);
            for (int sx = -1; sx <= 1; sx += 2)
                b.Sphere(0.026f, 5, 3, Matrix4x4.Translate(new Vector3(sx * 0.052f, 0.655f, 0.148f)), eye, new Vector3(1, 1, 0.6f));
        }

        /// env/crowd.js crowdFigureGeometry: a teardrop blob with two dark eye lenses that shade like decals.
        static void BlobTemplate(MeshBuilder b)
        {
            const float A = 0.42f, B = 0.42f * 1.25f;
            b.Sphere(A, 8, 6, Matrix4x4.Translate(new Vector3(0, B, 0)), Color.white, new Vector3(1, 1.25f, 1));
            const float ex = 0.14f, ey = 0.17f;
            float ez = A * Mathf.Sqrt(1 - (ex / A) * (ex / A) - (ey / B) * (ey / B));
            var eye = new Color(0.03f, 0.03f, 0.03f, 1);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var n = new Vector3(sx * ex / (A * A), ey / (B * B), ez / (A * A)).normalized;
                int start = b.Count;
                b.Sphere(0.075f, 6, 4, Matrix4x4.TRS(new Vector3(sx * ex, B + ey, ez), Quaternion.FromToRotation(Vector3.forward, n), Vector3.one), eye, new Vector3(1, 1.15f, 0.45f));
                for (int i = start; i < b.Count; i++) b.N[i] = n;   // decal shading: the toon terminator never cuts the dot
            }
        }

        // ------------------------------------------------------------ reactions (web chars/crowd.js)

        /// Baseline excitement, 0..1. Raise it as a round escalates.
        public void SetEnergy(float v) => _energyTarget = Mathf.Clamp01(v);
        public float Energy => _energy;

        /// Combo milestone: go wild, and send a wave for the big ones.
        public void Hype(float amount = 0.6f)
        {
            _hype = Mathf.Max(_hype, Mathf.Clamp(amount, 0, 1.2f));
            _sagGoal = 0; _cap = 1;
            if (amount >= 0.55f) Wave(Mathf.Min(1, amount));
        }

        /// Everyone up at once, hard (web env crowd.cheer): finishes and big moments.
        public void Cheer(float strength = 1.4f) { Hype(Mathf.Min(1.2f, strength * 0.8f)); _glow = Mathf.Max(_glow, strength); }

        /// A miss: the stands sag, silence you can see.
        public void Deflate(float amount = 1)
        {
            _sagGoal = Mathf.Max(_sagGoal, Mathf.Clamp01(amount));
            _hype = 0;
            _cap = Mathf.Min(_cap, 0.30f);
        }

        /// A travelling wave through the stands.
        public void Wave(float amount = 1, float speed = 1.6f)
        {
            if (Settings.ReduceMotion) return;
            if (_waveT >= 0 && _waveT < 1.6f) return;   // let a crest on the arc finish first
            _waveT = 0; _waveAmt = Mathf.Clamp01(amount); _waveSpeed = speed;
        }

        public override void Pulse(float strength, int beatIndex) => _glow = Mathf.Max(_glow, strength * 0.5f);

        public override void OnQuality()
        {
            int show = Mathf.Clamp(Mathf.RoundToInt(Count * RenderQuality.CrowdBudget), 1, Count);
            if (show == _shownFigures) return;
            _shownFigures = show;
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, show * _indexPerFigure, MeshTopology.Triangles),
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        }

        public override void Update(float dt, double beat, float time, Palette pal)
        {
            _cap = EnvKit.Damp(_cap, 1, 1.2f, dt);
            float goal = Mathf.Min(_cap, Mathf.Clamp01(_energyTarget + _hype));
            _energy = EnvKit.Damp(_energy, goal, goal < _energy ? 8 : 3.2f, dt);
            _hype = EnvKit.Damp(_hype, 0, 0.9f, dt);
            _sagGoal = EnvKit.Damp(_sagGoal, 0, 2.6f, dt);
            _sag = EnvKit.Damp(_sag, _sagGoal, 14, dt);
            _glow = EnvKit.Damp(_glow, 0, 5, dt);
            if (_waveT >= 0)
            {
                _waveT += dt * _waveSpeed;
                if (_waveT > 2.2f) { _waveT = -1; _waveAmt = 0; }
            }
            bool calm = Settings.ReduceMotion;
            float e = _energy;
            float eighths = e > 0.72f ? Smootherstep((e - 0.72f) / 0.28f) : 0;
            float jump = (0.10f + e * 0.42f) * (1 - _sag * 0.9f) * (calm ? 0.3f : 1);
            float lean = (0.10f + e * 0.16f) * (calm ? 0.4f : 1);
            _mat.SetFloat(IdBeat, (float)(beat % 1024.0));
            _mat.SetFloat(IdEighths, calm ? 0 : eighths);
            _mat.SetFloat(IdJump, jump);
            _mat.SetFloat(IdLean, lean);
            _mat.SetFloat(IdSag, _sag);
            _mat.SetFloat(IdWaveT, _waveT);
            _mat.SetFloat(IdWaveAmt, _waveAmt);
            _mat.SetFloat(IdGlow, _glowBase + _glow * 0.25f);
            _mat.SetFloat(IdArms, calm ? 0.35f : Mathf.Clamp01(e * 1.25f + _hype * 0.5f) * (1 - _sag));
            var cr = pal.Crowd;
            if (cr != null && cr.Length > 0)
                for (int i = 0; i < IdSlot.Length; i++) _mat.SetColor(IdSlot[i], cr[i % cr.Length]);
            if (_stand)
            {
                if (O.Stands == StandStyle.Risers) _stand.SetColor("_BaseColor", EnvKit.Mix(pal[PC.Band], pal[PC.Fog], 0.2f));
                else _stand.SetColor("_BaseColor", O.StandColor ?? EnvKit.Mix(pal[PC.BandAlt], pal[PC.Band], 0.4f));
            }
        }

        /// A night or mood grade on the whole crowd (linear multiplier on the figures' colour; white = as lit).
        public void SetTint(Color c) => _mat.SetColor("_BaseColor", c);

        static float Hash01(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16; return (x & 0xffffff) / 16777216f; }

        static float Smootherstep(float t) { t = Mathf.Clamp01(t); return t * t * t * (t * (t * 6 - 15) + 10); }
    }
}
