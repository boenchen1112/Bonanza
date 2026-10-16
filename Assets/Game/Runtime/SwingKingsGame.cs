// Swing Kings, playable in grey boxes. The "attach a controller" step of the
// pipeline: a placeholder stage wired to SwingRound (Bonanza.Core). No rule
// logic lives here, and no visual decisions — every mesh below is a primitive
// standing in for art that comes later.
//
// Runs under GameHost (Minigame contract): the host owns the start gate,
// autoplay, pause and the results screen.
//
// Layout and flight maths are the web version's (swingKings/world.js LAYOUT,
// index.js sample()/connect()), with three.js z flipped to Unity's.

using System.Collections.Generic;
using Bonanza.Core;
using Bonanza.Look;
using UnityEngine;

namespace Bonanza
{
    public sealed class SwingKingsGame : Minigame
    {
        public override string Id => "swing-kings";
        public override string Title => "SWING KINGS";
        public override string Blurb => "One swing. One beat. Send it.";
        public override double SecondsPerBeat => 60 / SwingRules.Bpm;
        protected override bool UseLook => true;
        bool _night;

        public override string Controls => "SPACE / click / tap as the ball reaches the plate.\nOn the beat = HOME RUN. Three misses retire the side.";

        // ------------------------------------------------------------ layout
        // web LAYOUT (x, y, z) -> Unity (x, y, -z)
        static Vector3 W(float x, float y, float z) => new Vector3(x, y, -z);
        static readonly Vector3 Muzzle = W(-6.38f, 1.91f, -0.25f);
        static readonly Vector3 Contact = W(2.3f, 1.25f, 0.25f);
        static readonly Vector3 MachinePos = W(-7.4f, 1.55f, -0.25f);
        static readonly Vector3 BatterPos = W(3.05f, 0f, 0.55f);
        static readonly Vector3 PlatePos = W(2.55f, 0.02f, 0.25f);
        const float Grav = 15.5f;

        SwingRound _round;
        bool _playing;
        double _now;

        // stage
        Transform _batPivot;
        float _swingT = 1;               // 0..1 through the follow-through; 1 = at rest
        readonly Dictionary<Pitch, Ball> _pitchBalls = new Dictionary<Pitch, Ball>();
        readonly List<HitBall> _hitBalls = new List<HitBall>();
        readonly Stack<GameObject> _ballPool = new Stack<GameObject>();
        Material _ballMat, _pipMat;

        // audio
        double _clickBeat;

        // HUD
        readonly List<Callout> _callouts = new List<Callout>();
        string _countText;
        float _countLife;

        sealed class Ball { public GameObject Go; public List<GameObject> Pips = new List<GameObject>(); public int PipCursor; }
        sealed class HitBall { public GameObject Go; public Vector3 P, V; public float T, Life, GravMul; public bool Bounce; }
        sealed class Callout { public string Text; public Vector3 World; public float Life, Age; public Color Color; }

        // ------------------------------------------------------------ contract

        public override double MusicBeatAt(double time) => _round.Map.BeatAt(time);
        public override double MusicTimeAt(double beat) => _round.Map.TimeAt(beat);

        public override void Load()
        {
            BuildStage();
            _round = new SwingRound(Save.OffsetMs);
            _round.Judged += (v, _) => MusicJudged(v != Verdict.Miss);
            _round.PitchLaunched += OnLaunched;
            _round.PitchPassed += OnPassed;
            _round.Contact += OnContact;
            _round.Whiff += OnWhiff;
            _round.SwingAtNothing += _ => StartSwing();
        }

        public override void Begin(double now)
        {
            _round.Start(now);
            _clickBeat = -SwingRules.LeadInBeats;
            _playing = true;
            _now = now;
            Say("SWING KINGS", 1.4f);
        }

        public override void Press(string action, double time)
        {
            if (action == "a") _round.Press(time);
        }

        public override void Tick(double now, float dt)
        {
            double beatBefore = _round.BeatAt(_now);
            _now = now;
            _round.Update(now);
            double beat = _round.BeatAt(now);
            CountIn(beatBefore, beat);
            // Beat flash on the audio clock, and the dusk -> night swap when the fast section starts.
            Look.BeatPulse = beat < 0 ? 0 : Mathf.Exp(-5f * Phase(beat));
            bool night = SwingRules.SectionAt(beat) is Section.Escalate or Section.Finale;
            if (night != _night)
            {
                _night = night;
                Look.SetPalette(night ? "swing-kings-night" : "swing-kings", night ? 2.4f : 0.8f);
            }
            ScheduleClicks();
            UpdatePitchBalls(beat);
        }

        public override Outcome Result()
        {
            if (!_round.Over) return null;
            _playing = false;
            var r = _round.Result();
            return Outcome.Build(r.Score, r.Accuracy, r.Rank, _round.Judge.Stats, _round.Judge.Bias, null)
                .Extra("HOME RUNS", r.Homers).Extra("LINE DRIVES", r.Liners).Extra("BUNTS", r.Bunts)
                .Extra("FOULS", r.Fouls).Extra("GRAND SLAM", r.GrandSlam ? "YES" : "NO");
        }

        public override IEnumerable<KeyValuePair<string, double>> TestChart()
        {
            foreach (var n in _round.Judge.Notes) yield return new KeyValuePair<string, double>("a", n.Time);
        }

        // ------------------------------------------------------------ stage

        void BuildStage()
        {
            var cam = Camera.main;
            // web rig: target (-1.9, 1.95, 0), distance 12.6, height 2.15, yaw 0.055, fov 45
            var target = W(-1.9f, 1.95f, 0);
            cam.transform.position = target + new Vector3(Mathf.Sin(0.055f) * 12.6f, 2.15f - 0.9f, -Mathf.Cos(0.055f) * 12.6f);
            cam.transform.LookAt(target);
            cam.fieldOfView = 45;

            // Palette-tracked surfaces follow the day -> night swap.
            var groundMat = Mat(Color.white, rim: 0, pulse: 0.04f); Look.Track(groundMat, PC.Ground);
            Prim(PrimitiveType.Plane, new Vector3(-2, 0, 0), new Vector3(14, 1, 14), groundMat);
            var dirtMat = Mat(Color.white, rim: 0, pulse: 0.04f); Look.Track(dirtMat, PC.Dirt);
            Prim(PrimitiveType.Cylinder, PlatePos + new Vector3(-0.2f, -0.012f, 0), new Vector3(3.2f, 0.01f, 2.4f), dirtMat);
            Prim(PrimitiveType.Cylinder, MachinePos + new Vector3(0, -1.54f, 0), new Vector3(2.4f, 0.01f, 2.4f), dirtMat);
            var plateMat = Mat(Color.white, rim: 0.4f); Look.Track(plateMat, PC.Plate);
            Prim(PrimitiveType.Cube, PlatePos, new Vector3(0.5f, 0.02f, 0.5f), plateMat);
            var wallMat = Mat(Color.white, rim: 0.3f); Look.Track(wallMat, PC.Band);
            Prim(PrimitiveType.Cube, new Vector3(-2, 1.2f, 11), new Vector3(60, 2.4f, 0.6f), wallMat);
            var standMat = Mat(Color.white, rim: 0.3f); Look.Track(standMat, PC.BandAlt);
            Prim(PrimitiveType.Cube, new Vector3(-2, 3.6f, 17), new Vector3(60, 7.2f, 10), standMat);
            var metalMat = Mat(Color.white); Look.Track(metalMat, PC.Metal);
            var machine = Prim(PrimitiveType.Cube, MachinePos, new Vector3(1.2f, 1.2f, 1.0f), metalMat);
            // Pads top out at y~0.02: lay the blobs just above them. Both bodies sit on their pad, so
            // heightOffset is the body centre's height and they get full strength.
            Look.Blob(machine.transform, 1.0f, 0.55f, groundY: 0.03f, heightOffset: MachinePos.y - 0.03f);
            var batterMat = Mat(Color.white); Look.Track(batterMat, PC.Batter);
            var batter = Prim(PrimitiveType.Capsule, BatterPos + Vector3.up * 0.9f, new Vector3(0.6f, 0.9f, 0.6f), batterMat);
            Look.Blob(batter.transform, 0.85f, 0.7f, groundY: 0.03f, heightOffset: 0.87f);

            _batPivot = new GameObject("BatPivot").transform;
            _batPivot.SetParent(transform, false);
            _batPivot.position = BatterPos + new Vector3(-0.2f, 1.25f, 0);
            Prim(PrimitiveType.Cube, new Vector3(-0.55f, 0, 0), new Vector3(1.1f, 0.07f, 0.07f), new Color(0.8f, 0.7f, 0.5f), _batPivot);
            PoseBat(1);

            _ballMat = Mat(Color.white, lift: 0.35f, rim: 1.2f, pulse: 0.3f);
            _pipMat = Mat(new Color(0.75f, 0.92f, 1f), lift: 0.6f);
        }

        /// 0 = coiled back, 1 = follow-through done (and back to rest).
        void PoseBat(float t)
        {
            float a = t >= 1 ? 60 : Mathf.Lerp(60, -150, Mathf.SmoothStep(0, 1, t));
            _batPivot.localRotation = Quaternion.Euler(0, a, 0);
        }

        void StartSwing() => _swingT = 0;

        /// The count-in ticks over the lead-in (the web has no click after it; the
        /// music carries the beat), scheduled a little ahead on the audio clock itself.
        void ScheduleClicks()
        {
            double horizon = _round.BeatAt(_now + 0.2);
            while (_clickBeat <= horizon && _clickBeat < 0)
            {
                double at = _round.Map.TimeAt(_clickBeat);
                if (at > _now) Host.CountTick(at, Core.CountIn.Accent((int)_clickBeat));
                _clickBeat += 1;
            }
        }

        // ------------------------------------------------------------ visuals (run on through results)

        void Update()
        {
            float dt = Time.deltaTime;
            if (!_playing) Look.BeatPulse = 0;
            UpdateHitBalls(dt);
            UpdateBat(dt);
            for (int i = _callouts.Count - 1; i >= 0; i--)
            {
                _callouts[i].Age += dt;
                if (_callouts[i].Age >= _callouts[i].Life) _callouts.RemoveAt(i);
            }
            _countLife -= dt;
        }

        /// "3, 2, 1, PLAY BALL!" on the last four lead-in beats (web round.js countIn).
        void CountIn(double before, double beat)
        {
            for (int b = (int)System.Math.Floor(before) + 1; b <= System.Math.Floor(beat); b++)
            {
                string s = Core.CountIn.TextFor(b, SwingRules.LeadInBeats, 3, "PLAY BALL!");
                if (s != null) SetCount(s);
            }
        }

        void SetCount(string s) { _countText = s; _countLife = 0.44f; }

        // ------------------------------------------------------------ pitches

        Vector3 Sample(Pitch p, double u)
        {
            float uf = (float)u;
            var x = Mathf.LerpUnclamped(Muzzle.x, Contact.x, uf);
            var y = Mathf.LerpUnclamped(Muzzle.y, Contact.y, uf) + 4 * (float)p.Apex * uf * (1 - uf);
            var z = Mathf.LerpUnclamped(Muzzle.z, Contact.z, uf) - Mathf.Sin(Mathf.PI * Mathf.Clamp01(uf)) * 0.45f;
            return new Vector3(x, y, z);
        }

        GameObject AcquireBall()
        {
            var go = _ballPool.Count > 0 ? _ballPool.Pop() : null;
            if (go == null)
            {
                go = MeshObject(PrimitiveType.Sphere, _ballMat);
                go.transform.localScale = Vector3.one * 0.24f;
                Look.Blob(go.transform, 0.2f, 0.5f);
            }
            go.SetActive(true);
            return go;
        }

        void FreeBall(GameObject go) { go.SetActive(false); _ballPool.Push(go); }

        void OnLaunched(Pitch p)
        {
            var b = new Ball { Go = AcquireBall() };
            // Half-beat markers along the flight path: the ball is the metronome.
            int steps = Mathf.Max(2, (int)System.Math.Round(p.Lead * 2));
            for (int i = 1; i <= steps; i++)
            {
                var pip = MeshObject(PrimitiveType.Sphere, _pipMat);
                pip.transform.position = Sample(p, (double)i / steps);
                pip.transform.localScale = Vector3.one * (i == steps ? 0.16f : 0.09f);
                b.Pips.Add(pip);
            }
            _pitchBalls[p] = b;
        }

        void ClearPips(Ball b)
        {
            foreach (var pip in b.Pips) if (pip) Destroy(pip);
            b.Pips.Clear();
        }

        void OnPassed(Pitch p)
        {
            if (!_pitchBalls.TryGetValue(p, out var b)) return;
            FreeBall(b.Go);
            ClearPips(b);
            _pitchBalls.Remove(p);
        }

        void UpdatePitchBalls(double beat)
        {
            foreach (var kv in _pitchBalls)
            {
                var p = kv.Key; var b = kv.Value;
                double u = SwingRound.FlightU(p, beat);
                var pos = Sample(p, System.Math.Min(u, 1.6));
                if (u > 1) pos.y = Mathf.Max(0.16f, pos.y - (float)(u - 1) * 1.4f);
                b.Go.transform.position = pos;
                int steps = b.Pips.Count;
                while (b.PipCursor < steps && u >= (double)(b.PipCursor + 1) / steps - 0.02)
                {
                    if (b.Pips[b.PipCursor]) b.Pips[b.PipCursor].SetActive(false);
                    b.PipCursor++;
                }
            }
        }

        // ------------------------------------------------------------ results of a swing

        void OnContact(ContactEvent c)
        {
            StartSwing();
            var p = c.Pitch;
            GameObject go;
            if (_pitchBalls.TryGetValue(p, out var b)) { go = b.Go; ClearPips(b); _pitchBalls.Remove(p); }
            else go = AcquireBall();

            // web connect(): the ball leaves the bat
            var t = c.Tier;
            float dist = t.Dist * (c.Foul ? 0.45f : 1);
            float T = t.Id == TierId.Bunt ? 0.9f : t.Id == TierId.Liner ? 1.5f : 2.6f;
            var dir = W(-1, 0, c.Foul ? 1.5f : -0.42f).normalized;
            var v = new Vector3(dir.x * dist / T, t.Lift * dist / T + 0.5f * Grav * T * 0.42f, dir.z * dist / T);
            float land = 0;
            if (t.Id == TierId.Homer && !c.Foul && !c.IsDemo)
            {
                // A home run lands IN the stands, in frame.
                const float T2 = 1.55f; float g2 = Grav * 0.6f;
                float lx = 2.4f + (c.Combo % 5) * 0.6f, lz = -14.0f - (c.Combo % 3) * 0.8f;
                float ly = 1.9f + Mathf.Max(0, Mathf.Sqrt(lx * lx + (lz + 3) * (lz + 3)) - 11.6f) * 0.53f;
                var L = W(lx, ly, lz);
                v = new Vector3((L.x - Contact.x) / T2, (L.y - Contact.y + 0.5f * g2 * T2 * T2) / T2, (L.z - Contact.z) / T2);
                land = T2;
            }
            bool big = t.Id == TierId.Homer || t.Id == TierId.Slam;
            _hitBalls.Add(new HitBall
            {
                Go = go, P = Contact, V = v, Life = land > 0 ? land : (big ? 3.4f : T + 0.35f),
                GravMul = t.Id == TierId.Slam ? 0.45f : t.Id == TierId.Homer ? 0.6f : 1f,
                Bounce = t.Id == TierId.Bunt,
            });

            string grade = c.Verdict == Verdict.Perfect ? "PERFECT" : c.Verdict == Verdict.Great ? "GREAT" : "GOOD";
            string word = c.IsDemo ? "LIKE THIS!"
                : (c.Foul ? "FOUL!" : t.Label) + "\n" + grade + (c.HomerFeet > 0 ? $" - {c.HomerFeet} FT" : "");
            Say(word, p.Finale ? 2.4f : 0.9f, Contact + new Vector3(1.15f, 1.75f, 0), Hex(t.Color));
        }

        void OnWhiff(WhiffEvent w)
        {
            if (w.Swung) StartSwing();
            if (w.SideRetired) Say("SIDE RETIRED!\n3 OUTS", 1.5f, Contact + new Vector3(1.1f, 1.6f, 0), Color.white);
            else Say(w.Swung ? "WHIFF!" : "STRIKE!", 0.8f, Contact + new Vector3(1.0f, 1.3f, 0), new Color(1, 0.6f, 0.6f));
        }

        void UpdateHitBalls(float dt)
        {
            for (int i = _hitBalls.Count - 1; i >= 0; i--)
            {
                var h = _hitBalls[i];
                h.T += dt;
                h.V.y -= Grav * dt * h.GravMul;
                h.P += h.V * dt;
                if (h.Bounce && h.P.y < 0.19f) { h.P.y = 0.19f; h.V.y *= -0.35f; h.V.x *= 0.7f; }
                h.Go.transform.position = h.P;
                if (h.T >= h.Life) { FreeBall(h.Go); _hitBalls.RemoveAt(i); }
            }
        }

        void UpdateBat(float dt)
        {
            if (_swingT < 1) _swingT = Mathf.Min(1, _swingT + dt / 0.28f);
            // Coil back while a pitch is in the air and about to land.
            bool coiled = false;
            if (_playing && _swingT >= 1)
            {
                var live = _round.LivePitch();
                if (live != null)
                {
                    double toGo = _round.Map.TimeAt(live.TargetBeat) - _now;
                    coiled = toGo < _round.Map.Spb * 2 && toGo > -0.2;
                }
            }
            if (_swingT < 1) PoseBat(_swingT);
            else _batPivot.localRotation = Quaternion.Slerp(_batPivot.localRotation, Quaternion.Euler(0, coiled ? 75 : 60, 0), 1 - Mathf.Exp(-dt * 12));
        }

        // ------------------------------------------------------------ HUD

        void Say(string text, float life, Vector3? world = null, Color? color = null)
        {
            _callouts.Add(new Callout
            {
                Text = text, Life = life, World = world ?? new Vector3(float.NaN, 0, 0), Color = color ?? Color.white,
            });
        }

        public override void DrawHud(float s)
        {
            if (_round == null) return;
            var st = _round.Judge.Stats;
            string outs = $"{_round.Outs}/3";   // ASCII: the WebGL built-in font has no ●○
            Hud.Text(new Rect(16 * s, 12 * s, 600 * s, 200 * s),
                $"SCORE  {Mathf.RoundToInt((float)_round.Points)}\nCOMBO  {st.Combo}\nACC  {_round.Judge.Accuracy * 100:0.0}%\nOUTS  {outs}", Hud.Left, Color.white);
            if (_playing && _round.BeatAt(_now) < 16)
                Hud.Text(new Rect(0, Screen.height - 60 * s, Screen.width, 40 * s),
                    "SPACE / click / tap as the ball reaches the plate - on the beat = HOME RUN", Hud.Small, new Color(1, 1, 1, 0.85f));

            if (_countLife > 0 && _countText != null)
                Hud.Center(0.12f, _countText, Hud.Big, Hud.Gold);

            var cam = Camera.main;
            foreach (var c in _callouts)
            {
                float a = Mathf.Clamp01((c.Life - c.Age) / 0.2f);
                var col = new Color(c.Color.r, c.Color.g, c.Color.b, a);
                if (float.IsNaN(c.World.x)) Hud.Center(0.3f, c.Text, Hud.Big, col, 120);
                else Hud.AtWorld(cam, c.World + Vector3.up * (c.Age * 0.6f), c.Text, Hud.Mid, col);
            }
        }
    }
}
