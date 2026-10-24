// Swing Kings: the bombs. Every pitch flies as the same white ball (AcquireBall: one mesh, one material, one trail
// colour, the same arc and spin), so the eye can never tell a bomb from the downbeat's ball; only the ear can. A
// bomb shows itself only once its fate is sealed:
//
//   explode   swung at: the white shell cracks off a black bomb for a few frames, then a warm core, a fireball,
//             embers, a shock ring, shell and casing debris and a column of dark smoke. One callout (the strike).
//   fizzle    let go: past the plate the shell flakes off, the bomb drops, bounces and rolls toward the backstop with
//             its fuse sputtering, then goes out in a small grey puff. A dud: no bang, no word (it happens on most beats).
//
// The smoke and the debris are this game's own two ALPHA particle batches on the shared FX atlas (smoke must darken
// the frame, not glow: the shared smoke family is additive). Built in Load, drawn once there (prewarm rule); per
// frame work writes transforms and material floats only.

using System.Collections.Generic;
using Bonanza.Core;
using Bonanza.Look;
using UnityEngine;

namespace Bonanza
{
    public sealed partial class SwingKingsGame
    {
        /// A bomb on the field after its reveal: a fizzling dud, or (Boom) the casing shown for a few frames before it bursts.
        sealed class Dud
        {
            public Transform Go, Spark;
            public Material SparkMat;
            public Vector3 P, V, Axis;
            public float T, Life, Spin, NextSpark, Scale;
            public bool Bounced, Boom, Live;
        }

        const int DudMax = 4;
        readonly List<Dud> _duds = new List<Dud>();
        ParticleBatch _smoke, _debris, _fire;
        Material _smokeMat, _fireMat;
        readonly FxRng _bombRng = new FxRng(0xb0b5);
        static readonly Color BombBody = new Color(0.075f, 0.07f, 0.12f), ShellWhite = new Color(1f, 0.97f, 0.9f);
        static readonly Color FuseSpark = new Color(1f, 0.72f, 0.3f);
        static readonly Color[] Casing = { new Color(0.06f, 0.06f, 0.1f), new Color(0.12f, 0.11f, 0.18f), new Color(0.75f, 0.58f, 0.25f) };

        /// The bomb casing (navy-black body, brass cap, a fuse with a lit tip), the dud pool and the two alpha batches.
        void BuildBombs()
        {
            var b = new MeshBuilder();
            Color body = new Color(0.07f, 0.07f, 0.11f), brass = Lin(0xc9a24a), fuse = Lin(0xd8c7a6);
            b.Sphere(0.5f, 18, 12, Matrix4x4.identity, body, Vector3.one);
            // a pale highlight band: the toon rim alone left the body a black hole against the night stands
            b.Shell(0.505f, 0.505f, 0.05f, 24, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(0, -0.06f, 0)), Lin(0x4a4d6a), Vector2.one);
            b.Shell(0.17f, 0.19f, 0.14f, 14, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(0, 0.5f, 0)), brass, Vector2.one);
            b.Disc(0.17f, 14, Matrix4x4.Translate(new Vector3(0, 0.57f, 0)), brass);
            b.Tube(new[] { new Vector3(0, 0.55f, 0), new Vector3(0.04f, 0.68f, 0), new Vector3(0.13f, 0.76f, 0), new Vector3(0.22f, 0.78f, 0) }, 0.035f, 5, Matrix4x4.identity, fuse);
            var mesh = _env.Own(b.ToMesh("Bomb"));
            var mat = EnvKit.Surface(_env, Color.white, null, null, rim: 0.9f, pulse: 0.04f, bands: 3);
            for (int i = 0; i < DudMax; i++)
            {
                var go = new GameObject("Bomb" + i).transform;
                go.SetParent(transform, false);
                var r = EnvKit.Renderer("Casing", go, mesh, mat);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                var sm = Glow("FuseSpark", go, new Vector3(0.24f, 0.8f, 0), 1.1f, FuseSpark, true, null, 2.4f);
                sm.SetFloat(IdIntensity, 1.4f);
                var d = new Dud { Go = go, Spark = go.Find("FuseSpark"), SparkMat = sm };
                go.position = new Vector3(0, -50, 0);
                go.gameObject.SetActive(false);   // (its toon and glow programs are linked by the set already)
                _duds.Add(d);
            }
            foreach (var d in _duds) Look.Blob(d.Go, 0.35f, 0.8f, 0, 0.6f);

            // Alpha particles: smoke that darkens, and casing/shell debris that keeps its colour on a bright sky.
            var tpl = Resources.Load<Material>("Look/FxSprite");
            _smokeMat = new Material(tpl) { mainTexture = FxSprites.Atlas, renderQueue = 3004 };
            _smokeMat.SetFloat("_SrcBlend", 5); _smokeMat.SetFloat("_DstBlend", 10); _smokeMat.SetFloat("_ZTest", 4);
            var fxRoot = new GameObject("BombFx").transform;
            fxRoot.SetParent(transform, false);
            _smoke = new ParticleBatch("BombSmoke", fxRoot, _smokeMat, FxSprites.Rect(FxSprites.Cell_.Smoke), PMode.Billboard, 96, _bombRng, 20, e =>
            { e.Grav = 0.9f; e.Drag = 2.6f; e.Size = 0.55f; e.SizeVar = 0.3f; e.Life = 1.1f; e.LifeVar = 0.3f; e.S0 = 0.45f; e.S1 = 1.8f; e.Fade = PFade.QuadOut; e.Alpha = 0.7f; e.SpinVar = 1.0f; e.Speed = 2.2f; });
            _debris = new ParticleBatch("BombDebris", fxRoot, _smokeMat, FxSprites.Rect(FxSprites.Cell_.Confetti), PMode.Tumble, 96, _bombRng, 21, e =>
            { e.Grav = -14f; e.Drag = 0.6f; e.Size = 0.1f; e.SizeVar = 0.4f; e.Aspect = 1.2f; e.Life = 0.75f; e.LifeVar = 0.25f; e.S0 = 1; e.S1 = 1; e.Fade = PFade.Late; e.Tumble = 14; e.Speed = 6; });
            _debris.Floor = 0.02f;
            // Fire: the same soft puff sprite, additive, for the fireball's billowing body (sparks alone read as a firework).
            _fireMat = new Material(tpl) { mainTexture = FxSprites.Atlas, renderQueue = 3005 };
            _fireMat.SetFloat("_SrcBlend", 5); _fireMat.SetFloat("_DstBlend", 1); _fireMat.SetFloat("_ZTest", 4);
            _fire = new ParticleBatch("BombFire", fxRoot, _fireMat, FxSprites.Rect(FxSprites.Cell_.Smoke), PMode.Billboard, 48, _bombRng, 22, e =>
            { e.Grav = 1.5f; e.Drag = 6f; e.Size = 0.9f; e.SizeVar = 0.3f; e.Life = 0.38f; e.LifeVar = 0.3f; e.S0 = 0.35f; e.S1 = 1.9f; e.Fade = PFade.QuadOut; e.Alpha = 1f; e.SpinVar = 2f; e.Speed = 3.4f; });
            _bombPrewarm = 3;
        }

        int _bombPrewarm;

        Dud AcquireDud()
        {
            Dud pick = null;
            foreach (var d in _duds) if (!d.Live) { pick = d; break; }
            if (pick == null)
            {
                // all busy: recycle the oldest (it is nearly gone anyway)
                pick = _duds[0];
                foreach (var d in _duds) if (d.T > pick.T) pick = d;
            }
            pick.Go.gameObject.SetActive(true);
            return pick;
        }

        // ------------------------------------------------------------ explode

        /// A bomb swung at. The casing shows for ~70 ms where the bat met it, then it bursts (UpdateBombs).
        void BombBlast(Vector3 at, bool free)
        {
            var d = AcquireDud();
            d.Live = true; d.Boom = true; d.T = 0; d.Life = 0.07f; d.P = at; d.V = Vector3.zero; d.Scale = 0.42f;
            d.Axis = new Vector3(0.3f, 0.2f, 1).normalized; d.Spin = 0;
            d.Go.position = at; d.Go.localScale = Vector3.one * d.Scale; d.Go.rotation = Quaternion.Euler(0, 0, -20);
            d.SparkMat.SetFloat(IdIntensity, 2.2f);
            // the disguise cracks off first: white shell flakes away from the bat
            var e = _debris.Begin();
            e.Pos(at).Dir(new Vector3(-0.6f, 0.7f, 0)); e.Cone = 0.9f; e.Speed = 4.5f; e.Size = 0.075f; e.Life = 0.6f;
            e.Color(ShellWhite); _debris.Emit(Calm ? 6 : 12);
            Look.Fx.Flare(at, Hex(0xfff0d0) * 0.6f, 0.5f, 0.08f, 0.4f, 1.2f);
        }

        /// The burst itself, when the shown casing's moment is over: a stylised toon blast. An orange-red core under a
        /// small bright cap, a dark purple-grey cartoon smoke puff in two bands, two or three chunky shards. Nothing here
        /// is white or additive over the crowd (the flares are clamped), and it is gone within one beat.
        void BombBurst(Vector3 at)
        {
            var fx = Look.Fx;
            bool soft = LowFx;
            float k = soft ? 0.5f : 1;
            // clamped warm flares: orange-red, never a white-out
            fx.Flare(at, Hex(0xff7a2a) * 0.5f, 1.3f * (soft ? 0.7f : 1), 0.16f, 0.4f, 1.6f);
            // the core: orange-red puffs under a smaller bright cap
            var f = _fire.Begin();
            f.Pos(at).Dir(new Vector3(0.1f, 1, -0.1f)); f.Cone = 1; f.Jitter = 0.1f;
            f.Color(new Color(0.8f, 0.17f, 0.04f)); f.Size = 1.05f; f.Life = 0.32f; f.Speed = 2.8f;
            _fire.Emit(soft ? 4 : 8);
            f.Pos(at + Vector3.up * 0.18f); f.Color(new Color(0.85f, 0.55f, 0.1f)); f.Size = 0.6f; f.Life = 0.2f; f.Speed = 1.2f;   // the cap: bright yellow-orange, not white
            _fire.Emit(soft ? 1 : 3);
            fx.Burst(at, Mathf.RoundToInt(10 * k), Hex(0xff9a3a), 7f, 0.09f, 0.45f, -10f);
            // chunky shards: casing pieces, one brass, thrown wide
            var e = _debris.Begin();
            e.Pos(at); e.Cone = 1; e.Speed = 7f; e.Size = 0.34f; e.Life = 0.8f;
            foreach (var c in Casing) { e.Color(c); _debris.Emit(1); }
            e.Size = 0.26f; e.Color(Casing[0]); _debris.Emit(soft ? 0 : 1);
            // smoke: a purple-grey puff in three flat bands (a deep base, a mid band, a pale cap rolling over them)
            var s = _smoke.Begin();
            s.Pos(at + new Vector3(0.1f, 0.05f, 0.1f)).Dir(new Vector3(0.3f, 1, 0.4f)); s.Cone = 0.5f; s.Speed = 2.0f; s.Jitter = 0.18f;
            s.Grav = 0.8f; s.Drag = 2.6f; s.Fade = PFade.Hold;
            s.Color(new Color(0.2f, 0.15f, 0.28f)); s.Alpha = 1f; s.Size = 1.25f; s.Life = 0.95f; s.S0 = 0.55f; s.S1 = 1.9f;
            _smoke.Emit(soft ? 3 : 6);
            s.Pos(at + new Vector3(0.1f, 0.4f, 0.1f)); s.Color(new Color(0.42f, 0.36f, 0.52f)); s.Alpha = 1f; s.Size = 0.95f; s.Life = 0.85f; s.Speed = 1.5f;
            _smoke.Emit(soft ? 2 : 5);
            s.Pos(at + new Vector3(0.05f, 0.75f, 0.1f)); s.Color(new Color(0.62f, 0.58f, 0.7f)); s.Alpha = 0.95f; s.Size = 0.7f; s.Life = 0.7f; s.Speed = 1.1f;
            _smoke.Emit(soft ? 1 : 3);
            // the batter is sooted and dizzy for a moment
            _sootT = 0.9f; _sootStar = 0;
            Look.Rig.DoFlash(soft ? 0.02f : 0.05f, Hex(0xff8a50));   // a brief warm blink, never white
            Look.Rig.Shake(Calm ? 0.04f : 0.1f, new Vector3(0.3f, 1f, 0));
            Look.Hitstop(0.05f);
        }

        float _sootT, _sootStar;
        Color _batterBase;

        /// Soot on the capsule batter (charcoal, fading back) and gold stars wheeling over his head.
        void UpdateSoot(float dt)
        {
            if (_batterMat == null) return;
            if (_sootT <= 0) return;
            _sootT -= dt;
            _sootStar -= dt;
            if (_sootStar <= 0 && !Calm)
            {
                _sootStar = 0.14f;
                float a = Time.time * 9f;
                var head = BatterPos + new Vector3(Mathf.Cos(a) * 0.35f, 2.15f, Mathf.Sin(a) * 0.2f);
                var st = _debris.Begin();
                st.Pos(head).Dir(Vector3.up); st.Cone = 0.2f; st.Speed = 0.6f; st.Size = 0.14f; st.Life = 0.45f; st.Color(new Color(1f, 0.82f, 0.25f));
                _debris.Emit(1);
            }
        }

        /// The soot colour goes on after the look rig has pushed the palette colour for the frame (execution order), and
        /// the frame the soot ends puts the palette colour back.
        void LateUpdate()
        {
            if (_batterMat == null || !(_sootT > 0 || _sootOn)) return;
            _batterBase = Look.Palette.Live[PC.Batter];
            float k = Mathf.Clamp01(_sootT / 0.9f);
            _sootOn = _sootT > 0;
            _batterMat.SetColor("_BaseColor", _sootOn ? Color.Lerp(_batterBase, new Color(0.07f, 0.06f, 0.08f), Mathf.Min(1, k * 2.2f)) : _batterBase);
        }
        bool _sootOn;

        // ------------------------------------------------------------ fizzle

        /// A bomb let go: as it crosses the plate in front of the batter the ball turns into the casing (fuse sparks, shell
        /// flakes), drops, bounces and rolls in the foreground and goes out with a teal DODGED puff. Under 0.4 s, so it is
        /// done before the next pitch's arc comes through.
        void BombFizzle(Vector3 at)
        {
            var d = AcquireDud();
            // the ball has flown on past the plate by the time it is let go: reveal the bomb AT the plate, in front of the batter
            at = new Vector3(PlatePos.x - 0.15f, 0.6f, PlatePos.z - 0.35f);
            d.Live = true; d.Boom = false; d.T = 0; d.Life = 0.38f; d.P = at; d.Bounced = false; d.Scale = 0.46f;
            // a hop out past the plate toward the camera, so the dud is seen in the foreground, not hidden by the batter
            d.V = new Vector3(0.3f, 1.3f, -3.4f);
            d.Axis = Vector3.Cross(Vector3.up, new Vector3(d.V.x, 0, d.V.z)).normalized; d.Spin = 480;
            d.NextSpark = 0;
            d.Go.position = at; d.Go.localScale = Vector3.one * d.Scale; d.Go.rotation = Quaternion.Euler(0, 0, -30);
            d.SparkMat.SetFloat(IdIntensity, 2.2f);
            // the disguise falls off: a puff of white shell flakes
            var e = _debris.Begin();
            e.Pos(at).Dir(new Vector3(-0.3f, 1, 0)); e.Cone = 0.8f; e.Speed = 3f; e.Size = 0.075f; e.Life = 0.35f; e.Grav = -10;
            e.Color(ShellWhite); _debris.Emit(Calm ? 4 : 8);
        }

        static readonly Color DodgeTeal = new Color(0.25f, 0.95f, 0.8f);

        /// The dud is out: a small teal puff, a soft ring and a tiny word.
        void DodgedPuff(Vector3 p)
        {
            var s = _smoke.Begin();
            s.Pos(p + Vector3.up * 0.1f).Dir(Vector3.up); s.Cone = 0.7f; s.Speed = 0.9f; s.Jitter = 0.08f;
            s.Color(new Color(0.3f, 0.8f, 0.7f)); s.Alpha = 0.7f; s.Size = 0.4f; s.Life = 0.45f; s.S0 = 0.4f; s.S1 = 1.5f; s.Fade = PFade.Hold;
            _smoke.Emit(Calm ? 2 : 4);
            Look.Fx.Ring(p + Vector3.up * 0.12f, DodgeTeal * 0.6f, 0.4f, 0.25f, 1.6f, 0.1f, 0.01f, 0.01f, 0.5f);
            Look.Fx.Callout("DODGED", null, p + new Vector3(0.1f, 0.5f, 0), DodgeTeal, 0.5f, 0.55f, 0.4f);
        }

        bool Calm => Settings.ReduceMotion;
        /// Reduced motion or a lowered quality tier: smaller, dimmer flashes and fewer particles.
        bool LowFx => Settings.ReduceMotion || RenderQuality.FxBudget < 0.99f;

        /// Per frame (presentation time): the duds roll and sputter, the shown casings burst, and the alpha batches draw.
        void UpdateBombs(float dt)
        {
            if (_smoke == null) return;
            UpdateSoot(dt);
            for (int i = 0; i < _duds.Count; i++)
            {
                var d = _duds[i];
                if (!d.Live) continue;
                d.T += dt;
                if (d.Boom)
                {
                    // the casing swells a touch, then goes
                    d.Go.localScale = Vector3.one * d.Scale * (1 + d.T / d.Life * 0.35f);
                    if (d.T >= d.Life) { BombBurst(d.P); Park(d); }
                    continue;
                }
                // a dud: falls, bounces once, rolls on and slows, the fuse spitting sparks
                d.V.y -= Grav * dt;
                d.P += d.V * dt;
                const float R = 0.17f;
                if (d.P.y < R)
                {
                    d.P.y = R;
                    if (!d.Bounced && d.V.y < -1.2f) { d.V.y *= -0.32f; d.Bounced = true; }
                    else d.V.y = 0;
                    d.V.x *= 1 - Mathf.Min(1, 2.6f * dt); d.V.z *= 1 - Mathf.Min(1, 2.6f * dt);
                }
                d.Go.position = d.P;
                // it skids and rocks with the fuse kept up (a rolled-over fuse hid the spark): a cartoon dud
                float rock = Mathf.Sin(d.T * 26f) * 22f * Mathf.Clamp01(1.2f - d.T / d.Life);
                d.Go.rotation = Quaternion.AngleAxis(rock, d.Axis) * Quaternion.Euler(0, 0, -12);
                float k = d.T / d.Life;
                float flick = 0.55f + 0.45f * Mathf.PerlinNoise(d.T * 40f, i * 3.1f);
                d.SparkMat.SetFloat(IdIntensity, (k < 0.7f ? 2.4f : 2.4f * (1 - (k - 0.7f) / 0.3f)) * flick);
                d.Spark.localScale = Vector3.one * (1.0f + 0.9f * flick);
                if (d.T >= d.NextSpark && k < 0.78f)
                {
                    // the fuse sputters: a spit of sparks every few frames, then it coughs and goes out
                    d.NextSpark = d.T + 0.05f;
                    Look.Fx.Burst(d.Spark.position, Calm ? 1 : 4, FuseSpark, 3.2f, 0.1f, 0.3f, -7f, Vector3.up, 0.7f);
                }
                if (d.T >= d.Life)
                {
                    // out: a teal DODGED puff, the casing sinks away
                    DodgedPuff(d.P);
                    Park(d);
                    continue;
                }
                if (k > 0.82f) d.Go.localScale = Vector3.one * d.Scale * Mathf.Max(0, 1 - (k - 0.82f) / 0.18f);
            }

            var cam = Camera.main;
            if (!cam) return;
            var tr = cam.transform;
            var axes = new CamAxes { R = tr.right, U = tr.up, F = tr.forward, P = tr.position };
            bool pw = _bombPrewarm > 0;
            if (pw) _bombPrewarm--;
            _smoke.Update(dt, axes, Time.time, pw);
            _debris.Update(dt, axes, Time.time, pw);
            _fire.Update(dt, axes, Time.time, pw);
        }

        void Park(Dud d)
        {
            d.Live = false;
            d.Go.position = new Vector3(0, -50, 0);
            d.Go.gameObject.SetActive(false);   // (a parked casing's blob shadow would still lie on the field)
            d.Go.localScale = Vector3.one * d.Scale;
            d.SparkMat.SetFloat(IdIntensity, 0);
        }

        void DestroyBombs()
        {
            _smoke?.Destroy(); _debris?.Destroy(); _fire?.Destroy();
            if (_smokeMat) Destroy(_smokeMat);
            if (_fireMat) Destroy(_fireMat);
        }
    }
}
