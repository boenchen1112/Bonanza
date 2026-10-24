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

        /// The burst itself, when the shown casing's moment is over: a billowing orange fireball with a hot heart, embers,
        /// a shock ring, casing and shell debris, then a dark smoke cloud that rolls up and back, off the next pitch's path.
        void BombBurst(Vector3 at)
        {
            var fx = Look.Fx;
            float calm = Calm ? 0.5f : 1;
            // warm core, not white: flares under the bloom's blow-out
            fx.Flare(at, Hex(0xffb347) * 0.9f, 1.6f, 0.2f, 0.4f, 2.0f);
            fx.Flare(at, Hex(0xff6a1a) * 0.7f, 2.4f, 0.34f, 0.3f, 1.6f);
            // the fireball: orange puffs billowing out, a yellow heart that burns out first
            var f = _fire.Begin();
            f.Pos(at).Dir(new Vector3(0.1f, 1, -0.1f)); f.Cone = 1; f.Jitter = 0.12f;
            f.Color(new Color(0.85f, 0.2f, 0.03f)); f.Size = 1.25f; f.Life = 0.5f; f.Speed = 4.2f;
            _fire.Emit(Calm ? 6 : 12);
            f.Color(new Color(0.8f, 0.42f, 0.08f)); f.Size = 0.85f; f.Life = 0.3f; f.Speed = 2.4f;
            _fire.Emit(Calm ? 3 : 6);
            // embers thrown wide, and a few slower sparks that hang in the fire
            fx.Burst(at, Mathf.RoundToInt(22 * calm), Hex(0xffd27a), 10f, 0.09f, 0.8f, -12f);
            fx.Burst(at, Mathf.RoundToInt(12 * calm), Hex(0xff7a1a), 4.5f, 0.2f, 0.4f, -1.5f);
            fx.Impact(at, new Vector3(0.2f, 1, -0.2f), Hex(0xff9a3a), Mathf.RoundToInt(14 * calm), 13, 0.13f, 1f);
            fx.Ring(at, Hex(0xffa040), 0.34f, 0.4f, 3.2f, 0.32f, 0.03f, 0.02f, 0.9f);
            // casing debris: black shards and a few brass bits, and the rest of the shell
            var e = _debris.Begin();
            e.Pos(at); e.Cone = 1; e.Speed = 7.5f; e.Size = 0.11f;
            foreach (var c in Casing) { e.Color(c); _debris.Emit(Calm ? 3 : 7); }
            e.Color(ShellWhite); e.Size = 0.08f; _debris.Emit(Calm ? 3 : 6);
            // smoke: a dark cloud that rolls up and drifts back toward the net (the next pitch comes in under it)
            var s = _smoke.Begin();
            s.Pos(at + new Vector3(0.1f, 0.15f, 0.1f)).Dir(new Vector3(0.35f, 1, 0.45f)); s.Cone = 0.55f; s.Speed = 2.8f; s.Jitter = 0.2f;
            s.Grav = 1.3f; s.Drag = 2.4f; s.Fade = PFade.Hold;
            s.Color(new Color(0.11f, 0.1f, 0.13f)); s.Alpha = 1f; s.Size = 0.95f; s.Life = 1.4f; s.S0 = 0.45f; s.S1 = 2.6f;
            _smoke.Emit(Calm ? 5 : 11);
            s.Color(new Color(0.3f, 0.28f, 0.32f)); s.Alpha = 0.85f; s.Size = 0.75f; s.Life = 1.1f; s.Speed = 2.0f;
            _smoke.Emit(Calm ? 3 : 6);
            Look.Rig.DoFlash(Calm ? 0.04f : 0.09f, Hex(0xffb070));   // a warm blink of the whole frame, under the white-out
            Look.Rig.Shake(Calm ? 0.04f : 0.12f, new Vector3(0.3f, 1f, 0));
            Look.Hitstop(0.045f);
        }

        // ------------------------------------------------------------ fizzle

        /// A bomb let go: the ball becomes the casing where it is (shell flakes off) and drops past the plate.
        void BombFizzle(Vector3 at)
        {
            var d = AcquireDud();
            d.Live = true; d.Boom = false; d.T = 0; d.Life = 0.8f; d.P = at; d.Bounced = false; d.Scale = 0.46f;
            // a hop out past the plate in front of the batter (toward the camera), so the dud is seen, not hidden by him
            d.V = new Vector3(1.5f, 1.8f, -2.6f);
            d.Axis = Vector3.Cross(Vector3.up, new Vector3(d.V.x, 0, d.V.z)).normalized; d.Spin = 480;
            d.NextSpark = 0;
            d.Go.position = at; d.Go.localScale = Vector3.one * d.Scale; d.Go.rotation = Quaternion.Euler(0, 0, -30);
            d.SparkMat.SetFloat(IdIntensity, 2.2f);
            // the disguise falls off: a puff of white shell flakes
            var e = _debris.Begin();
            e.Pos(at).Dir(new Vector3(-0.3f, 1, 0)); e.Cone = 0.8f; e.Speed = 3f; e.Size = 0.075f; e.Life = 0.5f; e.Grav = -10;
            e.Color(ShellWhite); _debris.Emit(Calm ? 4 : 8);
        }

        bool Calm => Settings.ReduceMotion;

        /// Per frame (presentation time): the duds roll and sputter, the shown casings burst, and the alpha batches draw.
        void UpdateBombs(float dt)
        {
            if (_smoke == null) return;
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
                    // out: a small grey puff and a wisp, the casing sinks away
                    var s = _smoke.Begin();
                    s.Pos(d.P + Vector3.up * 0.1f).Dir(Vector3.up); s.Cone = 0.7f; s.Speed = 1.0f; s.Jitter = 0.08f;
                    s.Color(new Color(0.6f, 0.6f, 0.66f)); s.Alpha = 0.75f; s.Size = 0.45f; s.Life = 0.8f; s.S0 = 0.4f; s.S1 = 1.7f; s.Fade = PFade.Hold;
                    _smoke.Emit(Calm ? 2 : 5);
                    Look.Fx.Burst(d.P + Vector3.up * 0.2f, Calm ? 2 : 5, Hex(0xb8bcc8), 1.6f, 0.07f, 0.35f, -2f);
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
