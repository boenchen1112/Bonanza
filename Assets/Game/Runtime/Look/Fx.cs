// The pooled effects system (web render/fx/index.js), one per LookRig and shared
// by every game. The one call that matters is Verdict(): it composes the whole
// layered response to a judged note -- contact flare, directional shard cone,
// inner and outer shockwaves, converging speed lines, spark spray, ground bloom,
// callout, confetti, echo -- with the timing offsets and per-verdict silhouettes
// of web fx/recipes.js (three acts across ~350 ms; PERFECT, GREAT, GOOD and MISS
// differ in SHAPE, not size, so they read from a single glance at a single frame).
//
// Everything is preallocated: families are CPU-built dynamic meshes (FxBatches),
// layers scheduled for later live in a fixed queue, nothing allocates per burst.
// Counts scale with RenderQuality.FxBudget; Low drops decals, streaks and echoes.
// No physics, no Shader.Find. Aged on presentation time: pass Update the
// VisualClock dt and a hitstop freezes the lot (the impact frame still draws).

using Bonanza.Core;
using TMPro;
using UnityEngine;

namespace Bonanza.Look
{
    public struct VerdictOpts
    {
        public Vector3? Dir;          // hit direction; the shard cone and inner ring orient to it
        public int Combo;             // drives escalation
        public float Scale;           // whole-effect size multiplier
        public Color? Color;          // override the verdict colour
        public float GroundY;         // floor height for the decal and ground wave (NaN = derive)
        public bool Text, Stage, Rings;
        public static VerdictOpts Default => new VerdictOpts { Scale = 1, GroundY = float.NaN, Text = true, Stage = true, Rings = true };
    }

    public sealed class Fx
    {
        enum L { Core, Shards, RingIn, Streaks, Spray, RingOut, Decal, Text, Confetti, Echo, Puff, SagRing, MilestoneWave }

        struct Layer { public L Id; public float Delay; public Layer(L id, float d) { Id = id; Delay = d; } }

        static readonly Layer[] Perfect =
        {
            new Layer(L.Core, 0), new Layer(L.Shards, 0), new Layer(L.RingIn, 0), new Layer(L.Streaks, 0.012f), new Layer(L.Spray, 0.055f),
            new Layer(L.Decal, 0.070f), new Layer(L.RingOut, 0.090f), new Layer(L.Text, 0.130f), new Layer(L.Confetti, 0.160f), new Layer(L.Echo, 0.220f),
        };
        static readonly Layer[] Great =
        {
            new Layer(L.Core, 0), new Layer(L.Shards, 0), new Layer(L.RingIn, 0), new Layer(L.Streaks, 0.020f), new Layer(L.Spray, 0.060f),
            new Layer(L.RingOut, 0.110f), new Layer(L.Text, 0.140f),
        };
        static readonly Layer[] Good = { new Layer(L.Core, 0), new Layer(L.RingIn, 0), new Layer(L.Spray, 0.070f), new Layer(L.Text, 0.150f) };
        static readonly Layer[] Miss = { new Layer(L.Puff, 0), new Layer(L.SagRing, 0.050f), new Layer(L.Text, 0.180f) };

        static Layer[] Recipe(Verdict v) => v switch { Verdict.Perfect => Perfect, Verdict.Great => Great, Verdict.Good => Good, _ => Miss };

        sealed class Rec
        {
            public bool On; public float T; public L Layer; public int K; public Verdict V;
            public Vector3 Pos, Dir; public Color Color; public float Scale, Size, Count, Gy; public bool HasRings;
        }

        const int Pending = 160;

        readonly LookRig _look;
        readonly CameraRig _rig;
        readonly Transform _root;
        readonly FxRng _rng = new FxRng(0xfeed);
        readonly ParticleBatch _flare, _sparks, _shards, _streaks, _confetti, _smoke, _decals;
        readonly RingBatch _rings, _overlayRings;
        readonly TrailBatch _trails;
        readonly CalloutPool _callouts;
        readonly Rec[] _pend = new Rec[Pending];
        readonly Rec _imm = new Rec();
        readonly Material[] _mats;
        readonly Texture2D _atlas;
        int _cursor, _prewarm;
        float _time;
        Vector3 _focus = new Vector3(0, 0.55f, 0);
        float? _groundY;
        int _combo;
        CamAxes _axes;

        /// Verdict colour (palette-tinted); wired by the LookRig.
        public System.Func<Verdict, Color> VerdictColor;

        static float Mul => RenderQuality.FxBudget;
        static bool Low => RenderQuality.Level.Tier == QualityTier.Low;
        static float ConfettiMul => Mul >= 1 ? 1f : Mul > 0.5f ? 0.65f : 0.35f;
        int N(float baseCount, float countScale) => Mathf.Max(1, Mathf.RoundToInt(baseCount * Mul * countScale));

        public Fx(Transform parent, LookRig look, CameraRig rig)
        {
            _look = look; _rig = rig;
            _root = new GameObject("Fx").transform;
            _root.SetParent(parent, false);
            _atlas = FxSprites.Atlas;

            Material Make(string res, int src, int dst, int ztest, int queue = -1)
            {
                var src0 = Resources.Load<Material>("Look/" + res);
                if (src0 == null) { Debug.LogError("Fx: Resources/Look/" + res + " missing"); return null; }
                var m = new Material(src0);
                if (m.HasProperty("_MainTex")) m.mainTexture = _atlas;
                if (src >= 0) { m.SetFloat("_SrcBlend", src); m.SetFloat("_DstBlend", dst); }
                if (ztest >= 0) m.SetFloat("_ZTest", ztest);
                if (queue > 0) m.renderQueue = queue;
                return m;
            }
            var add = Make("FxSprite", 5, 1, 4, 3001);
            var alpha = Make("FxSprite", 5, 10, 4, 3000);
            var ring = Make("FxRing", -1, 0, 4, 3002);
            var ringO = Make("FxRing", -1, 0, 8, 3003);
            var plate = Resources.Load<Material>("Look/FxPlate");
            var font = Resources.Load<TMP_FontAsset>("Look/CalloutFont");
            if (font == null) Debug.LogError("Fx: Resources/Look/CalloutFont missing");
            var text = font != null ? font.material : null;
            _mats = new[] { add, alpha, ring, ringO };

            System.Func<FxSprites.Cell_, Vector4> rect = FxSprites.Rect;
            int cap = 1;   // (capacities are the web's)
            _confetti = new ParticleBatch("Confetti", _root, alpha, rect(FxSprites.Cell_.Confetti), PMode.Tumble, 320 * cap, _rng, 10, e =>
            { e.Grav = -7.5f; e.Drag = 1.1f; e.Turb = 2.6f; e.Size = 0.17f; e.SizeVar = 0.3f; e.Aspect = 1.6f; e.Life = 1.5f; e.LifeVar = 0.35f; e.S0 = 1; e.S1 = 1; e.Fade = PFade.Linear; e.Tumble = 11; });
            _decals = new ParticleBatch("Decals", _root, add, rect(FxSprites.Cell_.Scorch), PMode.Ground, 48, _rng, 11, e =>
            { e.Grav = 0; e.Drag = 0; e.Speed = 0; e.SpeedVar = 0; e.SpinVar = 0.4f; e.Size = 1.5f; e.SizeVar = 0.15f; e.S0 = 0.5f; e.S1 = 1.35f; e.Life = 0.95f; e.LifeVar = 0.1f; e.Alpha = 0.5f; e.Fade = PFade.Hold; });
            _smoke = new ParticleBatch("Smoke", _root, add, rect(FxSprites.Cell_.Smoke), PMode.Billboard, 128, _rng, 12, e =>
            { e.Grav = -1.1f; e.Drag = 3.4f; e.Size = 0.5f; e.SizeVar = 0.4f; e.Life = 0.75f; e.LifeVar = 0.3f; e.S0 = 0.5f; e.S1 = 1.9f; e.Fade = PFade.Pop; e.Alpha = 0.33f; e.SpinVar = 1.2f; e.Speed = 1.6f; });
            _sparks = new ParticleBatch("Sparks", _root, add, rect(FxSprites.Cell_.Sparkle), PMode.Billboard, 512, _rng, 13, e =>
            { e.Grav = -9; e.Drag = 2.4f; e.Size = 0.15f; e.SpinVar = 14; e.Life = 0.5f; });
            _shards = new ParticleBatch("Shards", _root, add, rect(FxSprites.Cell_.Shard), PMode.Stretch, 256, _rng, 14, e =>
            { e.Grav = -6; e.Drag = 3.2f; e.Size = 0.16f; e.SizeVar = 0.4f; e.Life = 0.3f; e.LifeVar = 0.4f; e.Stretch = 0.085f; e.S0 = 1; e.S1 = 0.12f; e.SpinVar = 0; });
            _streaks = new ParticleBatch("Streaks", _root, add, rect(FxSprites.Cell_.Streak), PMode.Stretch, 128, _rng, 15, e =>
            { e.Grav = 0; e.Drag = 0; e.Size = 0.14f; e.SizeVar = 0.35f; e.Life = 0.13f; e.LifeVar = 0.2f; e.Stretch = 0.055f; e.S0 = 1; e.S1 = 0.5f; e.Fade = PFade.Pop; e.SpinVar = 0; e.SpeedVar = 0.15f; });
            _flare = new ParticleBatch("Flares", _root, add, rect(FxSprites.Cell_.Star6), PMode.Billboard, 64, _rng, 16, e =>
            { e.Grav = 0; e.Drag = 0; e.Speed = 0; e.SpeedVar = 0; e.SpinVar = 1.4f; e.Size = 0.55f; e.SizeVar = 0.1f; e.S0 = 0.35f; e.S1 = 1.9f; e.Life = 0.15f; e.LifeVar = 0.15f; e.Fade = PFade.Late; });
            _rings = new RingBatch("Rings", _root, ring, 48, _rng, 17);
            _overlayRings = new RingBatch("RingsOverlay", _root, ringO, 32, _rng, 18);
            _trails = new TrailBatch(_root, add, rect(FxSprites.Cell_.Solid), 8, 28, 90, 9);
            _callouts = new CalloutPool(_root, 24, plate, text, font);

            for (int i = 0; i < Pending; i++) _pend[i] = new Rec();
        }

        // ------------------------------------------------------------- configuration

        public void SetFocus(Vector3 p) => _focus = p;
        public void SetGroundY(float y) => _groundY = y;
        public void SetCombo(int c) => _combo = c;

        /// Touch every material and the callout font once (a frame or two) so no shader link or font upload
        /// lands mid-round: the first-use link of an FX shader costs 150-270 ms and would push a press out of its window.
        public void Prewarm()
        {
            _prewarm = 3;
            _callouts.Prewarm(_look.PrewarmPoint(), Camera.main);
        }

        Color VColor(Verdict v) => VerdictColor != null ? VerdictColor(v) : Feel.Hex(Feel.BaseColor(v));

        // ------------------------------------------------------------- layers

        Vector3 OrientedNormal(Vector3 d)
        {
            // A disc perpendicular to the hit is the honest impact ring, and vanishes when the hit points at the
            // camera: bias the plane toward the viewer so it can never be edge-on.
            var n = d * 0.55f - _axes.F * 0.85f;
            return n.sqrMagnitude < 1e-6f ? -_axes.F : n.normalized;
        }

        void Fire(Rec r)
        {
            var v = r.V;
            Feel.Burst(v, out int bShards, out int bSparks, out int bStreaks, out int bConfetti);
            float sc = r.Scale, sz = r.Size;
            switch (r.Layer)
            {
                // ACT 1 --------------------------------------------------------------
                case L.Core:
                {
                    // Two flares, different sizes and durations, so even the first frame has a foreground and a background.
                    var e = _flare.Begin();
                    e.Pos(r.Pos).Color(Feel.HeatUp(r.Color, 0.45f));
                    e.Size = 0.52f * sc * sz; e.Life = 0.16f; e.S0 = 0.3f; e.S1 = 2.1f;
                    _flare.Emit(1);
                    e.Color(Feel.Hex(Feel.Hot));
                    e.Size = 0.30f * sc * sz; e.Life = 0.095f; e.S0 = 0.55f; e.S1 = 2.8f;
                    _flare.Emit(1);
                    break;
                }
                case L.Shards:
                {
                    var e = _shards.Begin();
                    e.Pos(r.Pos).Dir(r.Dir).Color(Feel.HeatUp(r.Color, 0.25f));
                    e.Cone = 0.30f;                       // tight enough that the AIM is readable
                    e.Speed = 11.5f * sc; e.Size = 0.165f * sz;
                    _shards.Emit(N(bShards, r.Count));
                    break;
                }
                case L.RingIn:
                    _rings.Spawn(r.Pos, Feel.HeatUp(r.Color, 0.5f), 0.19f, 0.22f * sc, 1.5f * sc * sz, 0.34f, 0.055f, 0.012f, 1, OrientedNormal(r.Dir), 0);
                    break;
                case L.Streaks:
                {
                    // Converging speed lines: spawned on a ring and aimed inward so they land on the hit point at the
                    // end of their life: they visibly COLLAPSE rather than merely shrink.
                    int count = Low ? 0 : N(bStreaks, r.Count);
                    if (count <= 0) break;
                    SpeedLinesAt(r.Pos, Feel.HeatUp(r.Color, 0.35f), count, 2.5f * sc, 0.135f * sz);
                    break;
                }
                // ACT 2 --------------------------------------------------------------
                case L.Spray:
                {
                    var e = _sparks.Begin();
                    e.Pos(r.Pos).Dir(r.Dir).Color(r.Color);
                    e.Jitter = 0.12f; e.Cone = 0.78f;     // biased toward the hit, but nearly round
                    e.Speed = 7 * sc; e.Size = 0.155f * sz;
                    _sparks.Emit(N(bSparks, r.Count));
                    break;
                }
                case L.RingOut:
                {
                    float reach = Feel.RingReach(v) * sc * sz;
                    _overlayRings.Spawn(r.Pos, r.Color, 0.44f + r.K * 0.06f, 0.55f * sc, reach * (1 + r.K * 0.28f), 0.22f, 0.028f, 0.035f, 0.9f - r.K * 0.18f, null, 0);
                    if (r.K == 0 && v == Verdict.Perfect)
                        // ... and one lying flat on the floor, so the wave is visibly a thing moving through the world
                        _rings.Spawn(new Vector3(r.Pos.x, r.Gy + 0.03f, r.Pos.z), r.Color, 0.6f, 0.4f * sc, reach * 1.25f, 0.16f, 0.02f, 0.05f, 0.55f, Vector3.up, 0);
                    break;
                }
                case L.Decal:
                {
                    if (Low) break;
                    var e = _decals.Begin();
                    e.Pos(new Vector3(r.Pos.x, r.Gy + 0.02f, r.Pos.z)).Color(r.Color);
                    e.Size = 1.5f * sc * sz; e.Life = 0.95f; e.Alpha = 0.45f;
                    _decals.Emit(1);
                    break;
                }
                // ACT 3 --------------------------------------------------------------
                case L.Text:
                {
                    bool miss = v == Verdict.Miss;
                    _callouts.Spawn(Feel.Label(v), null, r.Pos + Vector3.up * 0.5f, r.Color, (miss ? 0.44f : 0.5f) * sz,
                        miss ? 0.95f : 0.78f, miss ? -0.55f : 1.15f, miss ? -0.22f : 0, miss ? -0.55f : 0, false);
                    break;
                }
                case L.Confetti:
                {
                    int bse = r.K == 1 ? 44 : bConfetti;
                    int total = Mathf.RoundToInt(N(bse, r.Count) * ConfettiMul);
                    if (total <= 0) break;
                    ConfettiAt(r.Pos, total, null, 7.2f * sc, new Vector3(r.Dir.x * 0.4f, 1, r.Dir.z * 0.4f), sz * 0.17f);
                    break;
                }
                case L.Echo:
                {
                    if (Low) break;
                    float reach = Feel.RingReach(v) * sc * sz;
                    _overlayRings.Spawn(r.Pos, Feel.HeatUp(r.Color, 0.6f), 0.58f, reach * 0.45f, reach * 1.55f, 0.055f, 0.012f, 0.055f, 0.34f, null, 0);
                    break;
                }
                // MISS ---------------------------------------------------------------
                case L.Puff:
                {
                    var e = _smoke.Begin();
                    e.Pos(r.Pos).Color(Feel.HeatUp(r.Color, 0.15f), 0.75f);
                    e.Jitter = 0.14f; e.Cone = 1; e.Speed = 1.7f;
                    _smoke.Emit(N(bSparks, r.Count));
                    // a couple of sad sparks that just fall out of the puff
                    var s = _sparks.Begin();
                    s.Pos(r.Pos).Color(r.Color, 0.8f);
                    s.Cone = 1; s.Speed = 1.6f; s.SpeedVar = 0.7f; s.Grav = -11; s.Drag = 1.2f; s.Size = 0.1f; s.Life = 0.6f;
                    _sparks.Emit(Mathf.Max(2, Mathf.RoundToInt(4 * Mul)));
                    break;
                }
                case L.SagRing:
                    // Contracting, thickening, heavily distorted: the shape of something deflating.
                    _overlayRings.Spawn(r.Pos, r.Color, 0.5f, 1.5f * sc, 0.3f, 0.08f, 0.30f, 0.095f, 0.6f, null, 0);
                    break;
                case L.MilestoneWave:
                {
                    int k = r.K;
                    _overlayRings.Spawn(r.Pos, Feel.Hex(Feel.Combo), 0.5f + k * 0.08f, 0.3f + k * 0.5f, 2.4f + k * 1.5f, 0.16f - k * 0.03f, 0.02f, 0.012f + k * 0.003f, 0.85f - k * 0.2f, null, 0);
                    var e = _flare.Begin();
                    e.Pos(r.Pos).Color(Feel.Hex(Feel.Hot));
                    e.Size = (0.45f + k * 0.25f) * sc; e.Life = 0.22f + k * 0.05f; e.S0 = 0.4f; e.S1 = 2.0f + k * 0.4f;
                    _flare.Emit(1);
                    break;
                }
            }
        }

        Rec Schedule(L layer, float delay, Rec src, int k)
        {
            Rec r = null;
            for (int i = 0; i < Pending; i++)
            {
                var c = _pend[(_cursor + i) % Pending];
                if (!c.On) { r = c; _cursor = (_cursor + i + 1) % Pending; break; }
            }
            if (r == null) { r = _pend[_cursor]; _cursor = (_cursor + 1) % Pending; }   // overwrite the oldest, never drop a layer
            Copy(r, src, layer, k); r.On = true; r.T = _time + delay;
            return r;
        }

        static void Copy(Rec r, Rec s, L layer, int k)
        {
            r.Layer = layer; r.K = k; r.V = s.V; r.Pos = s.Pos; r.Dir = s.Dir; r.Color = s.Color; r.Scale = s.Scale; r.Size = s.Size; r.Count = s.Count; r.Gy = s.Gy;
        }

        // ------------------------------------------------------------- verdict

        readonly Rec _v = new Rec();

        /// THE entry point: one call per judged note; everything else is composed.
        public void PlayVerdict(Verdict verdict, Vector3 pos, VerdictOpts o)
        {
            if (o.Scale <= 0) o.Scale = 1;
            _combo = o.Combo;
            var f = Feel.ForCombo(verdict, _combo);
            var rec = _v;
            rec.V = verdict; rec.Pos = pos;
            // Default aim: up and out. A hit with no direction still has to look like it SENT something.
            rec.Dir = o.Dir.HasValue && o.Dir.Value.sqrMagnitude > 1e-8f ? o.Dir.Value.normalized : new Vector3(0.42f, 0.88f, -0.22f).normalized;
            rec.Color = o.Color ?? Feel.HeatUp(VColor(verdict), f.Hot);
            rec.Scale = o.Scale; rec.Size = f.SizeScale * o.Scale; rec.Count = f.CountScale;
            rec.Gy = !float.IsNaN(o.GroundY) ? o.GroundY : (_groundY ?? pos.y - 1.6f);

            foreach (var layer in Recipe(verdict))
            {
                if (layer.Id == L.Text && !o.Text) continue;
                // `Rings = false`: the big outward wave and its echo. In a game whose subject stands AT the impact
                // point they draw wobbly loops through the character; the core flash and shards still sell the hit.
                if (!o.Rings && (layer.Id == L.RingOut || layer.Id == L.Echo)) continue;
                if (layer.Delay <= 0)
                {
                    // Act 1 fires INLINE, on the same frame as the press: queuing it would cost a frame of latency.
                    Copy(_imm, rec, layer.Id, 0);
                    Fire(_imm);
                }
                else Schedule(layer.Id, layer.Delay, rec, 0);
            }
            // Combo escalation adds PHYSICAL layers: extra shockwaves trailing the first, each wider, later and fainter.
            for (int k = 1; o.Rings && k <= f.ExtraRings; k++) Schedule(L.RingOut, 0.09f + k * 0.055f, rec, k);

            if (o.Stage && _rig != null)
            {
                _rig.Shake(f.Shake, new Vector3(rec.Dir.x, rec.Dir.y * 0.5f, 0));
                if (f.Flash > 0) _rig.DoFlash(f.Flash, rec.Color);
            }
            if (verdict != Verdict.Miss && Feel.IsMilestone(_combo)) Milestone(_combo, pos);
        }

        /// The combo-milestone flourish: three ring waves, a flare, a confetti shower, a word.
        public void Milestone(int combo, Vector3 pos)
        {
            int tier = Feel.ComboTier(combo);
            var rec = _v;
            rec.V = Verdict.Perfect; rec.Pos = pos; rec.Color = Feel.Hex(Feel.Combo);
            rec.Scale = 1 + tier * 0.06f; rec.Size = 1 + tier * 0.08f; rec.Count = 1 + tier * 0.22f;
            rec.Gy = _groundY ?? pos.y - 1.6f; rec.Dir = Vector3.up;
            float[] delays = { 0, 0.075f, 0.15f };
            for (int i = 0; i < delays.Length; i++) Schedule(L.MilestoneWave, delays[i], rec, i);
            Schedule(L.Confetti, 0.06f, rec, 1);
            _callouts.Spawn("x" + combo, null, pos + new Vector3(-0.45f, 0.85f, 0), Feel.Hex(Feel.Combo), 0.5f + tier * 0.045f, 0.95f, 0.4f, 0, 0, false);
            if (_rig != null) { _rig.Shake(Feel.MilestoneShake * (1 + tier * 0.08f), Vector3.up); _rig.DoFlash(Feel.MilestoneFlash); }
        }

        // ------------------------------------------------------------- primitives

        /// Radial burst of sparks.
        public void Burst(Vector3 pos, int count = 18, Color? color = null, float speed = 6, float size = 0.14f, float life = 0.5f, float gravity = -9, Vector3? dir = null, float cone = 1)
        {
            var e = _sparks.Begin();
            e.Pos(pos);
            if (dir.HasValue) e.Dir(dir.Value);
            e.Cone = dir.HasValue ? Mathf.Clamp01(cone) : 1;
            e.Speed = speed; e.Size = size; e.Life = life; e.Grav = gravity;
            e.Color(color ?? Color.white);
            _sparks.Emit(Mathf.Max(1, Mathf.RoundToInt(count * Mul)));
        }

        /// Expanding shock ring: the single most legible "you hit it" cue. `normal` null = a billboard.
        public void Ring(Vector3 pos, Color color, float life = 0.42f, float from = 0.35f, float to = 3.2f, float thick0 = 0.24f, float thick1 = 0.035f,
                         float wobble = 0.03f, float alpha = 1, Vector3? normal = null, float spin = 0, bool overlay = false)
            => (overlay ? _overlayRings : _rings).Spawn(pos, color, life, from, to, thick0, thick1, wobble, alpha, normal, spin);

        /// Tumbling confetti.
        public void Confetti(Vector3 pos, int count = 60, Color[] colors = null, float speed = 7.5f, float up = 1)
        {
            int total = Mathf.Max(1, Mathf.RoundToInt(count * Mul * ConfettiMul));
            ConfettiAt(pos, total, colors, speed, new Vector3(0, up, 0), 0.17f);
        }

        void ConfettiAt(Vector3 pos, int total, Color[] colors, float speed, Vector3 dir, float size)
        {
            int n = colors != null ? colors.Length : Feel.Party.Length;
            int per = Mathf.Max(1, Mathf.RoundToInt(total / (float)n));
            var e = _confetti.Begin();
            e.Pos(pos).Dir(dir);
            e.Jitter = 0.2f; e.Cone = 0.62f; e.Speed = speed; e.Size = size;
            for (int i = 0; i < n; i++)
            {
                e.Color(colors != null ? colors[i] : Feel.Hex(Feel.Party[i]));
                _confetti.Emit(per);
            }
        }

        /// Directional impact cone: the primitive behind a hit that has an AIM.
        public void Impact(Vector3 pos, Vector3 dir, Color color, int count = 16, float speed = 11, float size = 0.16f, float cone = 0.3f, float scale = 1)
        {
            var e = _shards.Begin();
            e.Pos(pos).Dir(dir).Color(color);
            e.Cone = cone; e.Speed = speed * scale; e.Size = size * scale;
            _shards.Emit(Mathf.Max(1, Mathf.RoundToInt(count * Mul)));
        }

        /// Speed lines converging on a point.
        public void SpeedLines(Vector3 pos, Color color, int count = 12, float radius = 2.5f, float size = 0.135f)
        {
            if (Low) return;
            SpeedLinesAt(pos, color, Mathf.Max(1, Mathf.RoundToInt(count * Mul)), radius, size);
        }

        void SpeedLinesAt(Vector3 pos, Color color, int count, float radius, float size)
        {
            const float life = 0.135f;
            var e = _streaks.Begin();
            e.Color(color); e.Life = life; e.Size = size; e.Cone = 0;
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2 + _rng.Next() * 0.5f;
                float rad = radius * (0.82f + _rng.Next() * 0.36f);
                var o = _axes.R * Mathf.Cos(a) + _axes.U * Mathf.Sin(a);
                e.Pos(pos + o * rad);
                e.Dx = -o.x; e.Dy = -o.y; e.Dz = -o.z;
                e.Speed = rad / life;
                _streaks.Emit(1);
            }
        }

        /// Ground scorch / impact bloom that fades.
        public void Decal(Vector3 pos, Color color, float size = 1.5f, float life = 0.95f, float alpha = 0.45f)
        {
            if (Low) return;
            var e = _decals.Begin();
            e.Pos(pos).Color(color); e.Size = size; e.Life = life; e.Alpha = alpha;
            _decals.Emit(1);
        }

        public void Flare(Vector3 pos, Color color, float size = 0.5f, float life = 0.16f, float from = 0.35f, float to = 2.0f)
        {
            var e = _flare.Begin();
            e.Pos(pos).Color(color); e.Size = size; e.Life = life; e.S0 = from; e.S1 = to;
            _flare.Emit(1);
        }

        /// A ribbon trail: feed it `Set(pos)` each frame and `Release()` when the object stops. Null when all are busy.
        public TrailHandle Trail(Color color, float width = 0.16f) => _trails.Acquire(color, width);

        /// A word in the world (the web's popText): outlined, no plate.
        public void PopWord(string word, Vector3 pos, Color color, float size = 0.5f, float life = 0.78f, float rise = 1.15f, float tilt = 0, float spin = 0)
            => _callouts.Spawn(word, null, pos, color, size, life, rise, tilt, spin, false);

        /// Remove the badges still on screen (a new badge replaces them rather than stacking).
        public void ClearBadges() => _callouts.KillBadges();

        /// A badge callout: a dark rounded plate, a big word and an optional smaller line under it (Swing Kings' tier badge).
        /// `size` is the web sprite scale (0.8 small, 1.05 big, 1.75 grand slam): the plate is 2.93 x size world units wide.
        public void Callout(string word, string sub, Vector3 pos, Color color, float size = 1f, float life = 1.05f, float rise = 1.1f)
            => _callouts.Spawn(word, sub, pos, color, size, life, rise, 0, 0, true);

        // ------------------------------------------------------------- frame

        /// Per frame, AFTER the camera rig. `dt` is presentation time (0 during a hitstop, and still called then).
        /// `realDt` ages the callouts: a word that spawns on the hit must not sit frozen mid-pop through the hitstop.
        public void Update(float dt, float realDt, Camera cam)
        {
            if (!cam) return;
            _time += dt;
            for (int i = 0; i < Pending; i++)
            {
                var r = _pend[i];
                if (!r.On || r.T > _time) continue;
                r.On = false;
                Fire(r);
            }
            var tr = cam.transform;
            _axes = new CamAxes { R = tr.right, U = tr.up, F = tr.forward };
            bool pw = _prewarm > 0;
            if (pw) _prewarm--;
            _confetti.Update(dt, _axes, _time, pw);
            _decals.Update(dt, _axes, _time, pw);
            _smoke.Update(dt, _axes, _time, pw);
            _sparks.Update(dt, _axes, _time, pw);
            _shards.Update(dt, _axes, _time, pw);
            _streaks.Update(dt, _axes, _time, pw);
            _flare.Update(dt, _axes, _time, pw);
            _rings.Update(dt, _axes, tr.rotation, pw);
            _overlayRings.Update(dt, _axes, tr.rotation, pw);
            _trails.Update(dt, _axes);
            _callouts.Update(realDt, cam, _axes.R, _axes.U, pw);
        }

        /// Live particles, for the budget check in tests and the perf log.
        public int LiveCount => _confetti.Alive + _decals.Alive + _smoke.Alive + _sparks.Alive + _shards.Alive + _streaks.Alive + _flare.Alive + _rings.Alive + _overlayRings.Alive;

        public void Clear()
        {
            _confetti.KillAll(); _decals.KillAll(); _smoke.KillAll(); _sparks.KillAll(); _shards.KillAll(); _streaks.KillAll(); _flare.KillAll();
            _rings.KillAll(); _overlayRings.KillAll(); _trails.KillAll(); _callouts.KillAll();
            for (int i = 0; i < Pending; i++) _pend[i].On = false;
        }

        public void Destroy()
        {
            _confetti.Destroy(); _decals.Destroy(); _smoke.Destroy(); _sparks.Destroy(); _shards.Destroy(); _streaks.Destroy(); _flare.Destroy();
            _rings.Destroy(); _overlayRings.Destroy(); _trails.Destroy(); _callouts.Destroy();
            foreach (var m in _mats) if (m) Object.Destroy(m);
            if (_root) Object.Destroy(_root.gameObject);
        }
    }
}
