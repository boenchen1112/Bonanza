// Swing Kings on the shared look kit (Look/): a placeholder stage wired to
// SwingRound (Bonanza.Core). No rule logic lives here. Every mesh below is a
// primitive standing in for art that comes later; the camera, the hit effects
// and the callouts are the real thing (CameraRig, Fx), driven like the web's
// swingKings/index.js: verdict burst, impact cone, speed lines, shake, punch
// zoom, a VISUAL hitstop, and the badge callout.
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
        public override bool UseCanvasHud => true;
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

        // HUD (canvas: Ui). Presentation only; it ages on real time.
        float _outsClear;               // seconds until the lamps reset after the side is retired
        bool _hintHidden;
        float _camPush, _camLift;

        sealed class Ball { public GameObject Go; public List<GameObject> Pips = new List<GameObject>(); public int PipCursor; public TrailHandle Trail; }
        sealed class HitBall { public GameObject Go; public Vector3 P, V; public float T, Life, GravMul; public bool Bounce, Slam, Land; public TrailHandle Trail; public Color Color; }

        // ------------------------------------------------------------ contract

        public override double MusicBeatAt(double time) => _round.Map.BeatAt(time);
        public override double MusicTimeAt(double beat) => _round.Map.TimeAt(beat);

        public override void Load()
        {
            BuildStage();
            var fx = Look.Fx;
            fx.SetGroundY(0);
            fx.SetFocus(W(-1.6f, 1.6f, 0));
            fx.Prewarm();   // touch every FX material and the callout font now: a first-use shader link mid-round would push a press out of its window
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
            // The HUD (web ui.hud + swingKings/hud.js): score block, outs lamps, the one-line instruction, the title card.
            Ui.ShowScoreboard();
            Ui.Outs(0);
            Ui.Hint("<color=#ffe58a>SPACE</color> / tap as the ball reaches the plate \u00B7 on the beat = <color=#ffe58a>HOME RUN</color>");
            _hintHidden = false;
            Ui.Banner("SWING KINGS", life: 1.5f);
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
            Look.TickBeat(beat);
            bool night = SwingRules.SectionAt(beat) is Section.Escalate or Section.Finale;
            if (night != _night)
            {
                _night = night;
                Look.SetPalette(night ? "swing-kings-night" : "swing-kings", night ? 2.4f : 0.8f);
            }
            ScheduleClicks();
            UpdatePitchBalls(beat);
            UpdateCamera(beat, dt);
            UpdateHud(beat);
        }

        /// The readouts follow the judge; the instruction bar goes once the teach section is over (web: beat 16).
        void UpdateHud(double beat)
        {
            Ui.Score(Mathf.RoundToInt((float)_round.Points));
            Ui.Combo(_round.Judge.Stats.Combo);
            Ui.Accuracy((float)_round.Judge.Accuracy);
            if (!_hintHidden && beat >= 16) { _hintHidden = true; Ui.HideHint(); }
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
            // web rig: target (-1.9, 1.95, 0), distance 12.6, yaw 0.055, fov 45; the height keeps the lowered
            // stand-in framing (2.15 - 0.9) so the set sits where it has been. The rig owns the camera from here.
            Look.Rig.Frame(W(-1.9f, 1.95f, 0), 12.6f, 2.15f - 0.9f, 0.055f, 45, 2.4f, immediate: true);
            // The key's real shadow: the plate, the batter and the machine, inside a focus circle (web setShadowFocus).
            Look.ShadowFocus(W(-1.5f, 0, 0.1f), 9.2f);   // wider than the web's (-2.3, 6.8): the shadow now lies out to the side and must not fade before it ends

            // Palette-tracked surfaces follow the day -> night swap.
            var groundMat = Mat(Color.white, rim: 0.25f, pulse: 0.05f); Look.Track(groundMat, PC.Ground);
            Prim(PrimitiveType.Plane, new Vector3(-2, 0, 0), new Vector3(14, 1, 14), groundMat);
            var dirtMat = Mat(Color.white, rim: 0.3f, pulse: 0.06f); Look.Track(dirtMat, PC.Dirt);
            Prim(PrimitiveType.Cylinder, PlatePos + new Vector3(-0.2f, -0.012f, 0), new Vector3(3.2f, 0.01f, 2.4f), dirtMat);
            Prim(PrimitiveType.Cylinder, MachinePos + new Vector3(0, -1.54f, 0), new Vector3(2.4f, 0.01f, 2.4f), dirtMat);
            var plateMat = Mat(Color.white, rim: 0.4f); Look.Track(plateMat, PC.Plate);
            Cast(Prim(PrimitiveType.Cube, PlatePos, new Vector3(0.5f, 0.02f, 0.5f), plateMat));
            var wallMat = Mat(Color.white, rim: 0.3f); Look.Track(wallMat, PC.Band);
            Prim(PrimitiveType.Cube, new Vector3(-2, 1.2f, 11), new Vector3(60, 2.4f, 0.6f), wallMat);
            var standMat = Mat(Color.white, rim: 0.3f); Look.Track(standMat, PC.BandAlt);
            Prim(PrimitiveType.Cube, new Vector3(-2, 3.6f, 17), new Vector3(60, 7.2f, 10), standMat);
            var metalMat = Mat(Color.white); Look.Track(metalMat, PC.Metal);
            var machine = Cast(Prim(PrimitiveType.Cube, MachinePos, new Vector3(1.2f, 1.2f, 1.0f), metalMat));
            // Pads top out at y~0.02: lay the blobs just above them. Both bodies sit on their pad, so
            // heightOffset is the body centre's height and they get full strength.
            Look.Blob(machine.transform, 1.0f, 0.55f, groundY: 0.03f, heightOffset: MachinePos.y - 0.03f);
            var batterMat = Mat(Color.white); Look.Track(batterMat, PC.Batter);
            var batter = Cast(Prim(PrimitiveType.Capsule, BatterPos + Vector3.up * 0.9f, new Vector3(0.6f, 0.9f, 0.6f), batterMat));
            Look.Blob(batter.transform, 0.85f, 0.7f, groundY: 0.03f, heightOffset: 0.87f);

            _batPivot = new GameObject("BatPivot").transform;
            _batPivot.SetParent(transform, false);
            _batPivot.position = BatterPos + new Vector3(-0.2f, 1.25f, 0);
            Cast(Prim(PrimitiveType.Cube, new Vector3(-0.55f, 0, 0), new Vector3(1.1f, 0.07f, 0.07f), new Color(0.8f, 0.7f, 0.5f), _batPivot));
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
            // Presentation time: 0 during a hitstop, so the struck ball, the bat and the callouts hang for a few
            // frames. Pitch balls ride the audio beat and judging never reads this, so a freeze can not move a window.
            float dt = LookRig.VisualDt(Time.deltaTime);
            if (!_playing) Look.BeatPulse = 0;
            UpdateHitBalls(dt);
            UpdateBat(dt);
            // "SIDE RETIRED" holds all three lamps, then the count starts over (web _outsClear).
            if (_outsClear > 0)
            {
                _outsClear -= Time.deltaTime;
                if (_outsClear <= 0) Ui.Outs(0);
            }
        }

        /// The web's camera: eases to a target that leans in on a hit (camPush), lifts to follow the grand slam up, and
        /// sits a little closer in the night half. The rig adds the beat push, shake and punch zoom on top.
        void UpdateCamera(double beat, float dt)
        {
            bool night = SwingRules.SectionAt(beat) is Section.Escalate or Section.Finale;
            const double FinaleBeat = SwingRules.FinaleBeat;
            var slam = _round.Pitches[_round.Pitches.Count - 1];
            bool slamHold = beat >= FinaleBeat - 8 && beat < FinaleBeat;
            float lift = 0;
            float? flying = null;
            if (_pitchBalls.TryGetValue(slam, out var sb) && sb.Go.activeSelf) flying = sb.Go.transform.position.y;
            else foreach (var h in _hitBalls) if (h.Slam) { flying = h.P.y; break; }
            if ((slamHold || beat < FinaleBeat + 4) && flying.HasValue && beat >= FinaleBeat - 8)
                lift = Mathf.Min(3.2f, Mathf.Max(0, flying.Value - 2.6f));   // capped: a grand slam leaves the park
            _camLift = Damp(_camLift, lift, 3.4f, dt);
            _camPush = Damp(_camPush, 0, 3.2f, dt);
            Look.Rig.Frame(
                W(-1.9f + _camPush * 0.9f, 1.95f + _camPush * 0.15f + _camLift * 0.42f, 0),
                (slamHold ? 12.2f : night ? 12.0f : 12.6f) - _camPush * 0.5f + _camLift * 0.7f,
                2.15f - 0.9f, 0.055f + Mathf.Sin((float)beat * 0.11f) * 0.012f, 45, 2.4f);
        }

        /// Seconds from this pitch's target to the next pitch's (the space its feedback has).
        float GapAfter(Pitch p)
        {
            double next = double.PositiveInfinity;
            foreach (var q in _round.Pitches) if (q.TargetBeat > p.TargetBeat && q.TargetBeat < next) next = q.TargetBeat;
            return (float)((next - p.TargetBeat) * _round.Map.Spb);
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

        /// web: ui.popup at y 0.26, 1.4x (1.2x and 0.44 s for the last beat, PLAY BALL!).
        void SetCount(string s)
        {
            bool go = s == "PLAY BALL!";
            Ui.Countdown(s, 0.26f, go ? 1.2f : 1.4f, go ? 0.44f : 0.42f);
        }

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
                go = Cast(MeshObject(PrimitiveType.Sphere, _ballMat));
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
            b.Trail = Look.Fx.Trail(Hex(0xbfeaff), 0.075f);
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
            // Sailed past into the backstop net, with a puff.
            Look.Fx.Burst(b.Go.transform.position, 8, Hex(0x9fd8ff), 3, life: 0.4f);
            b.Trail?.Release();
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
                b.Trail?.Set(pos);
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
            GameObject go; TrailHandle trail = null;
            if (_pitchBalls.TryGetValue(p, out var pb)) { go = pb.Go; trail = pb.Trail; ClearPips(pb); _pitchBalls.Remove(p); }
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
            var tierColor = Hex(t.Color);
            if (trail == null) trail = Look.Fx.Trail(tierColor, big ? 0.15f : 0.09f);
            else { trail.SetColor(tierColor); trail.SetWidth(big ? 0.15f : 0.09f); }
            _hitBalls.Add(new HitBall
            {
                Go = go, P = Contact, V = v, Life = land > 0 ? land : (big ? 3.4f : T + 0.35f),
                GravMul = t.Id == TierId.Slam ? 0.45f : t.Id == TierId.Homer ? 0.6f : 1f,
                Bounce = t.Id == TierId.Bunt, Slam = t.Id == TierId.Slam, Land = land > 0, Trail = trail, Color = tierColor,
            });

            // --- feedback ------------------------------------------------------------------
            // How long until the next pitch lands: in the dense sections full-size rings and long words stacked up
            // and caged the batter, hiding the next ball. Feedback scales to the space it has.
            float gap = GapAfter(p);
            bool roomy = gap > 1.4f;
            var dirV = new Vector3(dir.x * 0.65f, 0.72f + t.Lift * 0.3f, dir.z * 0.4f);
            float scale = t.Id == TierId.Slam ? 2.0f : big ? 1.35f : t.Id == TierId.Liner ? 1.0f : 0.78f;
            // One word per hit: the badge below carries the timing grade as its second line, so the verdict text is
            // off, and so is the FX layer's own flash (it washed the frame milky): the game shakes the camera itself.
            var o = VerdictOpts.Default;
            o.Dir = dirV; o.Combo = c.Combo; o.Scale = scale; o.GroundY = 0; o.Text = false; o.Stage = false; o.Rings = false;
            var fx = Look.Fx;
            fx.PlayVerdict(c.Verdict, Contact, o);
            fx.Impact(Contact, dirV, tierColor, big ? 24 : 12, 13, scale: scale);
            if (big && !c.IsDemo)
            {
                fx.SpeedLines(Contact, tierColor, roomy ? 16 : 8, 3.0f);
                // The grand slam's shockwave and its echo reach well below the bat: overlay rings the field can't slice flat.
                if (p.Finale)
                {
                    fx.Ring(Contact, tierColor, 0.5f, 0.4f, 4.2f * scale, 0.2f, wobble: 0.004f, overlay: true);
                    fx.Ring(Contact, Hex(0xffd35a), 0.7f, 0.2f, 2.6f * scale, 0.14f, wobble: 0.004f, overlay: true);
                }
            }
            var rig = Look.Rig;
            if (!c.IsDemo) rig.Shake(big ? 0.12f : 0.05f, new Vector3(dir.x, 0.5f, 0));

            var f = Feel.ForCombo(c.Verdict, c.Combo);
            if (!c.IsDemo)
            {
                float hs = Mathf.Min(Feel.HitstopMax * 1.6f, f.Hitstop * (big ? 1.5f : 1f) * (p.Finale ? 2.6f : 1f));
                // The freeze ends well before the next unjudged note arrives, and is visual only.
                Look.Hitstop(Feel.HitstopFor(hs, _round.Judge.Notes, _round.Map.TimeAt(p.TargetBeat)));
                rig.PunchZoom(1 + (big ? 0.07f : 0.03f) * (p.Finale ? 2.4f : 1f));
            }
            _camPush = big ? 1 : 0.45f;

            // --- callout: a badge, tier word big, timing grade small -----------------------------
            string grade = c.Verdict == Verdict.Perfect ? "PERFECT" : c.Verdict == Verdict.Great ? "GREAT" : "GOOD";
            string word = c.IsDemo ? "LIKE THIS!" : c.Foul ? "FOUL!" : t.Label;
            string sub = c.IsDemo ? null : grade + (c.HomerFeet > 0 ? $" \u00B7 {c.HomerFeet} FT" : "");
            float life = p.Finale ? 2.4f : c.IsDemo ? 0.7f : Mathf.Min(big ? 1.25f : 0.95f, Mathf.Max(0.55f, gap * 0.8f));
            // Right of the batter, off the incoming pitch's arc (it comes down from the machine on the left).
            Look.Fx.ClearBadges();
            Look.Fx.Callout(word, sub, Contact + new Vector3(1.15f, 1.75f, 0), tierColor,
                size: t.Id == TierId.Slam ? 1.75f : big ? 1.05f : 0.8f, life: life, rise: (big ? 1.5f : 0.9f) * (life / (big ? 1.25f : 0.95f)));

            if (p.Finale)
            {
                rig.DoFlash(Feel.FinaleFlash, Hex(0xfff3cf));
                rig.Shake(Feel.FinaleShake, new Vector3(-1, 0.5f, 0));
                fx.Confetti(Contact + new Vector3(2, 2, 0), 140, null, 9, 1);
            }
        }

        void OnWhiff(WhiffEvent w)
        {
            if (w.Swung) StartSwing();
            if (_outsClear > 0) { _outsClear = 0; Ui.Outs(0); }   // a new out while SIDE RETIRED still holds the lamps: the new inning starts now
            Ui.Outs(w.Outs);
            var fx = Look.Fx;
            var o = VerdictOpts.Default;
            o.Combo = 0; o.GroundY = 0; o.Text = false; o.Stage = false;
            fx.PlayVerdict(Verdict.Miss, Contact, o);
            float gap = GapAfter(w.Pitch);
            float life = Mathf.Min(0.9f, Mathf.Max(0.5f, gap * 0.7f));
            var miss = Hex(0xff9999);
            if (w.SideRetired)
            {
                float retire = Mathf.Max(1.2f, Mathf.Min(1.8f, gap * 0.9f));
                _outsClear = retire;
                fx.ClearBadges();
                fx.Callout("SIDE RETIRED!", "3 OUTS", Contact + new Vector3(1.1f, 1.6f, 0), Hex(0xff4d6a), 1.05f, retire, 0.3f);
                Look.Rig.Shake(0.08f, new Vector3(0, -1, 0));
            }
            else { fx.ClearBadges(); fx.Callout(w.Swung ? "WHIFF!" : "STRIKE!", null, Contact + new Vector3(1.0f, 1.3f, 0), w.Swung ? Hex(0xff5d8a) : Hex(0xff8a3c), 0.8f, life, 0.4f); }
            _camPush = 0.25f;
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
                h.Trail?.Set(h.P);
                if (h.T >= h.Life)
                {
                    if (h.Land)
                    {
                        // Caught in the stands: a pop of confetti.
                        Look.Fx.Confetti(h.P + Vector3.up * 0.3f, 36, null, 5, 1);
                        Look.Fx.Burst(h.P, 10, h.Color, 4, life: 0.45f);
                    }
                    h.Trail?.Release();
                    FreeBall(h.Go);
                    _hitBalls.RemoveAt(i);
                }
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

        // The HUD is on the canvas (UseCanvasHud): no DrawHud.
    }
}
