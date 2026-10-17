// Bounce Brigade in grey boxes: the platforms are the tune. A piano roll you
// run across — x is time, height is pitch, colour is pitch class, width is
// note length. Tap as you land; on a ramp, tap as you meet it and again as
// you leave it to clear the chasm. Rules live in Bonanza.Core (BounceRound);
// this is stage, bouncer, camera, sound and HUD only. Web source:
// games/bounceBrigade/index.js.
//
// No verdict jingle on a landing on purpose: the note IS the feedback, and
// accuracy changes its timbre (perfect adds an octave), so the tune audibly
// gets better as you do.
//
// Coordinates: the web build's (x, y, z) is (x, y, -z) here.

using System.Collections.Generic;
using Bonanza.Core;
using UnityEngine;

namespace Bonanza
{
    public sealed class BounceBrigadeGame : Minigame
    {
        public override string Id => "bounce-brigade";
        public override string Title => "BOUNCE BRIGADE";
        public override string Blurb => "The platforms are the tune.";
        public override double SecondsPerBeat => 60 / BounceChart.Bpm;
        public override string Controls => "Tap SPACE / click as you land on each platform.\nLong ramps: tap as you meet the ramp AND as you leave it to clear the gap.\nMiss and you splash, then scramble on. You only lose the note.";

        const float CamDistance = 15.6f, CamHeight = 2.35f, CamYaw = -0.17f, CamLead = 5.4f;
        const int Window = 26;
        static readonly Color Gold = Hex(0xffd93d), Foam = Hex(0xbdfff2), Red = Hex(0xff5d73);

        BounceRound _round;
        readonly Callouts _say = new Callouts();
        double _now, _lastBeat = -99;
        bool _playing, _finishShown, _demoBounced;
        /// True from the press to the release of the button (web bounceBrigade `held`): the coil on a charge pad.
        bool _held;
        double _holdStart = -1e9;
        public bool Held => _held;
        public double HoldStart => _holdStart;

        sealed class Slab { public Transform Pivot; public Material Top, Body; public Color C; public float Pop, Glow; }
        readonly List<Slab> _slabs = new List<Slab>();
        Transform _bouncer, _cam, _water;
        Material _waterMat;
        readonly bool[] _splashed = new bool[256];
        float _launchFlash, _camAnchorY, _camY;
        Vector3 _camPos;

        // ------------------------------------------------------------ contract

        public override double MusicBeatAt(double time) => _round.Map.BeatAt(time);
        public override double MusicTimeAt(double beat) => _round.Map.TimeAt(beat);

        public override void Load()
        {
            _round = new BounceRound(Save.OffsetMs);
            _round.Judged += OnJudged;
            _round.FinishReached += OnFinish;
            _cam = Camera.main.transform;

            _waterMat = Mat(Hex(0x1aa6c8));
            _water = Prim(PrimitiveType.Plane, new Vector3(0, (float)BounceChart.WaterY, 20), new Vector3(90, 1, 34), _waterMat).transform;

            var post = Mat(new Color(0.25f, 0.22f, 0.3f));
            foreach (var p in _round.Platforms)
            {
                var c = Hex((uint)p.Color);
                var s = new Slab { C = c, Top = Mat(c), Body = Mat(c * 0.75f) };
                s.Pivot = new GameObject($"Platform {p.Index}").transform;
                s.Pivot.SetParent(transform, false);
                // Pivot at the top-surface centre, raked by the tilt: web writeSlab().
                s.Pivot.localPosition = new Vector3((float)(p.AnchorX + System.Math.Cos(p.Tilt) * p.Width * 0.5), (float)(p.AnchorY + System.Math.Sin(p.Tilt) * p.Width * 0.5), 0);
                s.Pivot.localRotation = Quaternion.Euler(0, 0, (float)(p.Tilt * Mathf.Rad2Deg));
                float w = (float)p.Width, th = (float)BounceChart.SlabThick, d = (float)BounceChart.SlabDepth;
                Prim(PrimitiveType.Cube, new Vector3(0, -th / 2, 0), new Vector3(w, th, d), s.Body, s.Pivot);
                Prim(PrimitiveType.Cube, new Vector3(0, 0.02f, 0), new Vector3(w * 0.94f, 0.06f, d * 0.86f), s.Top, s.Pivot);
                // Stilt into the water.
                float postH = Mathf.Max(0.4f, (float)(p.CenterY - BounceChart.SlabThick - BounceChart.WaterY + 0.5));
                Prim(PrimitiveType.Cylinder, new Vector3((float)p.CenterX, (float)(p.CenterY - BounceChart.SlabThick) - postH / 2, p.Kind == 'R' ? 0.15f : -0.05f),
                    new Vector3(0.3f, postH / 2, 0.3f), post);
                _slabs.Add(s);
            }

            // Finish gate: an arch of three boxes and a banner.
            var f = _round.Finish;
            float gx = (float)f.ContactX + 1.2f, gy = (float)f.Y;
            var gate = Mat(Gold);
            Prim(PrimitiveType.Cube, new Vector3(gx - 4.4f, gy + 2.2f, 0), new Vector3(0.5f, 4.6f, 0.5f), gate);
            Prim(PrimitiveType.Cube, new Vector3(gx + 4.4f, gy + 2.2f, 0), new Vector3(0.5f, 4.6f, 0.5f), gate);
            Prim(PrimitiveType.Cube, new Vector3(gx, gy + 4.5f, 0.35f), new Vector3(7f, 1.3f, 0.5f), Hex(0xff5d73));

            _bouncer = new GameObject("Bouncer").transform;
            _bouncer.SetParent(transform, false);
            Prim(PrimitiveType.Capsule, new Vector3(0, 0.6f, 0), new Vector3(0.8f, 0.6f, 0.8f), Hex(0x2fd6c0), _bouncer);
            Prim(PrimitiveType.Sphere, new Vector3(0.18f, 1.05f, -0.28f), Vector3.one * 0.28f, Color.white, _bouncer);   // eye, facing the run

            Prewarm();
            _camAnchorY = _camY = (float)_round.Platforms[0].Y;
            PlaceCamera(_round.MotionAt(BounceChart.RunwayBeat), 1, true);
        }

        public override void Begin(double now)
        {
            _round.Start(now);
            _now = now;
            _playing = true;
            _say.Banner(Title, 1.5f, Blurb);
        }

        public override void Press(string action, double time)
        {
            if (!_playing || action != "a") return;
            if (!_held) { _held = true; _holdStart = time; }
            if (!_round.Press(action, time)) Host.Play(TickClip, 0.1f);
        }

        // In tap mode the release note is an ordinary press (the second tap), so there is nothing to judge
        // on release; the flag only tells the stage the button is no longer down.
        public override void Release(string action, double time)
        {
            if (action == "a") _held = false;
        }

        public override void OnResume() { _held = Host.IsHeld("a"); }

        public override void Tick(double now, float dt)
        {
            _now = now;
            double beat = _round.BeatAt(now);
            for (int b = (int)System.Math.Floor(_lastBeat) + 1; b <= System.Math.Floor(beat); b++)
            {
                string s = CountIn.TextFor(b, -(int)BounceChart.RunwayBeat);
                if (s != null) _say.Count(s);
                if (b < 0 && b >= BounceChart.RunwayBeat) Host.CountTick(_round.Map.TimeAt(b), CountIn.Accent(b));
                if (b == 96) _say.Banner("SPRINT!", 1.0f, "eighths — do not stop", Gold);
            }
            _lastBeat = beat;
            _round.Update(now);

            // The free demonstration bounce off the runway.
            if (!_demoBounced && beat >= -1)
            {
                _demoBounced = true;
                PlayNote(_round.Platforms[0], Verdict.Great, now + 0.01);
                _slabs[0].Pop = 0.7f;
            }
        }

        public override Outcome Result()
        {
            var o = _round.Result();
            if (o != null) _playing = false;
            return o;
        }

        public override IEnumerable<KeyValuePair<string, double>> TestChart()
        {
            foreach (var n in _round.Live) yield return new KeyValuePair<string, double>(n.Action, n.Time);
        }

        // ------------------------------------------------------------ round events

        void OnJudged(Note note, BounceNote n, Verdict v, double errMs)
        {
            var p = _round.Platforms[n.Platform];
            double at = System.Math.Max(note.Time + errMs / 1000, _now + 0.002);   // mistiming is heard
            var m = _round.MotionAt(_round.BeatAt(_now));
            var pos = new Vector3((float)m.X, Mathf.Max((float)m.Y, (float)BounceChart.WaterY + 0.6f) + 1.4f, 0);
            bool hit = v != Verdict.Miss;
            MusicJudged(hit);
            bool big = p.Kind == 'X' || (p.Kind == 'F' && n.Kind == "release");
            _say.Verdict(v, pos, big ? 1.5f : n.Kind == "release" ? 1.2f : 0.9f);

            if (n.Kind == "contact")
            {
                if (hit)
                {
                    if (p.Charge) PlaySustain(p, v, at);
                    else PlayNote(p, v, at);
                    _slabs[p.Index].Pop = v == Verdict.Perfect ? 1 : v == Verdict.Great ? 0.8f : 0.55f;
                }
                else Thud(at);
            }
            else
            {
                if (hit) { PlayRelease(p, v, at, p.Kind == 'F'); _launchFlash = 1; }
                else Thud(at);
            }
        }

        void OnFinish()
        {
            bool hit = _round.FinishHit;
            _say.Banner(hit ? "FINISH!" : "FINISH", 2.0f, hit ? "the tune is yours" : "you got there", hit ? Gold : new Color(0.81f, 0.83f, 1));
            for (int k = 0; k < 4; k++) Host.PlayAt(Fanfare(k), _now + 0.02 + k * 0.1, 0.13f);
        }

        // ------------------------------------------------------------ sound (placeholder synth)

        static float Gain(Verdict v) => v == Verdict.Perfect ? 1 : v == Verdict.Great ? 0.86f : 0.66f;
        static AudioClip Pluck(int midi, float bright) => Tones.Blip(Tones.Midi(midi), 0.34f, bright, Wave.Triangle);
        static AudioClip Bell(int midi) => Tones.Blip(Tones.Midi(midi), 0.5f, 7, Wave.Sine);
        static AudioClip Lead(int midi) => Tones.Blip(Tones.Midi(midi), 0.9f, 2.2f, Wave.Square);
        static AudioClip Kick => Tones.Blip(110, 0.26f, 14, Wave.Sine, 0.45f);
        static AudioClip Spring => Tones.Blip(2600, 0.03f, 80, Wave.Noise);
        static AudioClip ThudClip => Tones.Blip(90, 0.12f, 20, Wave.Noise, 0.6f);
        static AudioClip Fanfare(int k) => Tones.Blip(Tones.Midi(74 + new[] { 0, 4, 7, 12 }[k]), 0.4f, 5, Wave.Square);
        static AudioClip TickClip => Tones.Blip(2400, 0.02f, 120);

        /// A platform's note, scheduled at the press so mistiming is audible;
        /// a perfect adds the octave on top, an okay is dull.
        void PlayNote(BouncePlatform p, Verdict v, double at)
        {
            float g = Gain(v);
            Host.PlayAt(Pluck(p.Midi, v == Verdict.Perfect ? 6 : v == Verdict.Great ? 8 : 12), at, 0.34f * g, 1, 0.06f);
            if (v != Verdict.Good) Host.PlayAt(Pluck(p.Midi + 12, 12), at + 0.014, 0.12f * g);
            if (v == Verdict.Perfect) Host.PlayAt(Bell(p.Midi + 12), at + 0.02, 0.1f);
            Host.PlayAt(Spring, at, 0.08f * g);
        }

        void PlaySustain(BouncePlatform p, Verdict v, double at)
        {
            float g = Gain(v);
            Host.PlayAt(Lead(p.Midi), at, 0.16f * g);
            Host.PlayAt(Pluck(p.Midi, 8), at, 0.2f * g);
            Host.PlayAt(Spring, at, 0.09f);
        }

        void PlayRelease(BouncePlatform p, Verdict v, double at, bool huge)
        {
            float g = Gain(v);
            Host.PlayAt(Bell(p.Midi + 12), at, 0.15f * g);
            Host.PlayAt(Pluck(p.Midi + 19, 6), at, 0.13f * g);
            Host.PlayAt(Kick, at, 0.5f);
            if (huge) Host.PlayAt(Fanfare(3), at + 0.05, 0.12f);
        }

        void Thud(double at) => Host.PlayAt(ThudClip, at, 0.2f);

        /// Built before the round: synthesising mid-round stalls a frame.
        void Prewarm()
        {
            var seen = new HashSet<int>();
            foreach (var p in _round.Platforms)
                if (seen.Add(p.Midi))
                {
                    foreach (var b in new[] { 6f, 8f, 12f }) Pluck(p.Midi, b);
                    Pluck(p.Midi + 12, 12); Bell(p.Midi + 12); Lead(p.Midi); Pluck(p.Midi + 19, 6);
                }
            for (int k = 0; k < 4; k++) Fanfare(k);
            _ = Kick; _ = Spring; _ = ThudClip; _ = TickClip;
        }

        // ------------------------------------------------------------ stage

        void PlaceCamera(BounceMotion m, float k, bool snap)
        {
            var target = new Vector3((float)m.X + CamLead, _camY + 1.05f, 0);
            float d = CamDistance + (_round.Finished ? 3.4f : 0);
            var pos = target + new Vector3(Mathf.Sin(CamYaw) * d, CamHeight, -Mathf.Cos(CamYaw) * d);
            _camPos = snap ? pos : Vector3.Lerp(_camPos, pos, k);
            _cam.position = _camPos;
            _cam.LookAt(target);
            Camera.main.fieldOfView = 50;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _say.Update(dt);
            if (_round == null) return;
            double beat = _playing ? _round.BeatAt(_now) : BounceChart.RunwayBeat;
            var m = _round.MotionAt(beat);
            var p = _round.Platforms[m.K];

            // Splash on a failed charge.
            if (m.Air && m.Outcome == "plunge" && !_splashed[m.K] && m.Y <= BounceRound.SplashY + 0.25)
            {
                _splashed[m.K] = true;
                Host.Play(ThudClip, 0.22f, 0.7f);
                _say.Say("SPLASH!", new Vector3((float)m.X, (float)BounceChart.WaterY + 1, 0), Foam, 0.8f);
            }

            _bouncer.localPosition = new Vector3((float)m.X, (float)m.Y, 0);
            _bouncer.localRotation = Quaternion.Euler(0, 0, (float)((m.Spin + m.Lean) * Mathf.Rad2Deg));
            _launchFlash = Damp(_launchFlash, 0, 7, dt);
            float squash = !m.Air ? 1 - 0.12f * Mathf.Clamp01(1 - Phase(beat) * 4) : 1 + 0.08f * _launchFlash;
            _bouncer.localScale = new Vector3(1 / Mathf.Sqrt(squash), squash, 1 / Mathf.Sqrt(squash));

            // Platforms near the cursor: pop on a landing, swell the beat before contact.
            int lo = Mathf.Max(0, m.K - 4), hi = Mathf.Min(_slabs.Count - 1, m.K + Window);
            for (int i = lo; i <= hi; i++)
            {
                var s = _slabs[i];
                var q = _round.Platforms[i];
                s.Pop = Damp(s.Pop, 0, 9, dt);
                double lead = q.Beat - beat;
                float want = 0;
                if (lead > 0 && lead < 1.15) want = Mathf.SmoothStep(0, 1, 1 - (float)(lead / 1.15));
                else if (lead <= 0 && beat < q.DepartBeat) want = 1;
                s.Glow = Damp(s.Glow, want, 14, dt);
                float heat = Mathf.Clamp01(s.Glow * 0.55f + s.Pop * 0.6f);
                s.Top.SetColor("_BaseColor", Color.Lerp(s.C, Color.white, heat * 0.55f));
                s.Pivot.localScale = new Vector3(1, 1 - s.Pop * 0.4f, 1 + s.Pop * 0.16f);
            }

            // Follow the melody contour, not the ballistic arc.
            var next = _round.Platforms[Mathf.Min(_round.Platforms.Count - 1, m.K + 1)];
            var ahead = _round.Platforms[Mathf.Min(_round.Platforms.Count - 1, m.K + 3)];
            _camAnchorY = Damp(_camAnchorY, (float)(p.Y + next.Y + ahead.Y) / 3, 2.4f, dt);
            _camY = Damp(_camY, _camAnchorY * 0.82f + (float)m.Y * 0.18f, 5.5f, dt);
            PlaceCamera(m, 1 - Mathf.Exp(-6f * dt), false);

            _water.localPosition = new Vector3((float)m.X, (float)BounceChart.WaterY, 20);
            _waterMat.SetColor("_BaseColor", Color.Lerp(Hex(0x1aa6c8), Hex(0x3fc6e0), 0.5f + 0.5f * Mathf.Sin((float)beat * Mathf.PI)));
        }

        // ------------------------------------------------------------ HUD

        public override void DrawHud(float s)
        {
            if (_round == null) return;
            var st = _round.Judge.Stats;
            Hud.Text(new Rect(16 * s, 12 * s, 600 * s, 200 * s),
                $"SCORE  {System.Math.Round(_round.Accuracy * 1000)}\nCOMBO  {st.Combo}\nJUMPS  {_round.ChargesCleared}/{_round.ChargeCount}", Hud.Left, Color.white);
            // Progress along the run.
            double beat = _playing ? _round.BeatAt(_now) : BounceChart.RunwayBeat;
            float u = Mathf.Clamp01((float)(beat / _round.Finish.Beat));
            float w = 360 * s, x0 = Screen.width / 2f - w / 2, y = 18 * s;
            Hud.Box(new Rect(x0, y, w, 8 * s), new Color(1, 1, 1, 0.18f));
            Hud.Box(new Rect(x0, y, w * u, 8 * s), Foam);
            _say.Draw();
        }
    }
}
