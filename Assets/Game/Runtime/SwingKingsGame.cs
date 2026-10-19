// Swing Kings on the shared look kit (Look/), wired to SwingRound (Bonanza.Core).
// No rule logic lives here. The ballpark around the field is the environment
// kit (Look/Env: grass, arches, skyline, bunting, a beat-bouncing crowd in a
// stadium bowl, beams, light towers); the machine, the batter and the bat are
// still primitives standing in for art that comes later. The camera, the hit
// effects and the callouts are the real thing (CameraRig, Fx), driven like the
// web's swingKings/index.js: verdict burst, impact cone, speed lines, shake,
// punch zoom, a VISUAL hitstop, and the badge callout.
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
    public sealed partial class SwingKingsGame : Minigame
    {
        public override string Id => "swing-kings";
        public override string Title => "SWING KINGS";
        public override string Blurb => "One swing. One beat. Send it.";
        public override double SecondsPerBeat => 60 / SwingRules.Bpm;
        protected override bool UseLook => true;
        public override bool UseCanvasHud => true;
        bool _night;

        public override string Controls => CameraMode
            ? "RAISE your hand to the camera as the ball flies, then CONDUCT DOWN as it lands.\nThe stop of the stroke is the swing. On the beat = HOME RUN. Three misses retire the side.\nNo camera? The mouse takes over: HOLD it as the ball flies, then flick DOWN."
            : Conducting
            ? "HOLD the mouse (or SPACE) as the ball flies, then flick DOWN as it lands.\nThe stop of the stroke is the swing. On the beat = HOME RUN. Three misses retire the side."
            : "SPACE / click / tap as the ball reaches the plate.\nOn the beat = HOME RUN. Three misses retire the side.";

        // Mouse conducting (ADR 0004, web swingKings/index.js): the button down is the windup, a downward flick of
        // the pointer fires the swing at the ictus (the stroke's stop), and releasing the button swings too.
        // Camera conducting (ticket 28, web camera.js): the browser tracks the palm and the same detector finds the
        // stroke: a raise starts the windup, the stop of the downstroke swings. Where the browser has no camera API
        // (SwingInputEffective) or the camera fails, the mouse conducts instead. Tap is the default.
        static bool Conducting => Settings.SwingInputEffective != SwingInput.Tap;
        static bool CameraMode => Settings.SwingInputEffective == SwingInput.Camera;
        const string HintMouse = "<color=#ffe58a>HOLD</color> the mouse as the ball flies \u00B7 <color=#ffe58a>SWING DOWN</color> as it lands";
        const string HintCamera = "<color=#ffe58a>RAISE</color> your hand as the ball flies \u00B7 <color=#ffe58a>CONDUCT DOWN</color> as it lands";
        const float CameraLoadGrace = 20f;   // seconds of play to wait for a camera that is still loading or never answered the prompt
        bool _mouse;
        bool _cam;                           // the camera is the live conducting source (cleared when it falls back to the mouse)
        bool _camStarted;
        int _camToken;
        float _camWaited;
        readonly CameraConductor _camCond = new CameraConductor();
        readonly List<BonanzaCamera.Sample> _camBuf = new List<BonanzaCamera.Sample>();
        readonly PointerConductor _pointer = new PointerConductor();
        ConductBot _bot;                 // ?autoplay in mouse mode: a scripted conductor through the same detector
        ConductTrace _trace;
        Mesh _traceMesh;
        Material _traceMat;
        GameObject _traceGo;
        float _traceHold;                // seconds the ribbon keeps drawing after the release (the follow-through)
        Vector3[] _traceVerts;
        Color[] _traceCols;
        const float BatLen = 1.1f;

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
        Material _ballMat, _ballOutlineMat;

        // audio
        double _clickBeat;
        bool _finishShown;

        // HUD (canvas: Ui). Presentation only; it ages on real time.
        float _outsClear;               // seconds until the lamps reset after the side is retired
        bool _hintHidden;
        float _camPush, _camLift;

        sealed class Ball { public GameObject Go; public int PipCursor; public TrailHandle Trail; }
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
            _round.Judged += (v, err) =>
            {
                MusicJudged(v != Verdict.Miss);
                // The conducting check (Tools/mouse-check.mjs) reads these two lines.
                if (_mouse) Debug.Log($"[SWING] verdict={v.ToString().ToLowerInvariant()} err={err:F1} power={_round.LastPower:F2} hold={_round.LastHoldBeats:F2} pts={_round.Points:F0}");
            };
            _round.PitchLaunched += OnLaunched;
            _round.PitchPassed += OnPassed;
            _round.Contact += OnContact;
            _round.Whiff += OnWhiff;
            _round.SwingAtNothing += _ => { StartSwing(); Host.PlaySfx("swoosh", _now); };
            BuildTrace();
            // The camera is asked for now, while the rules card is up, so the permission prompt and the model load
            // happen before the first pitch. Never under ?autoplay: the harness conducts with the scripted bot.
            if (CameraMode && !Autoplay)
            {
                _cam = _camStarted = true;
                _camToken = BonanzaCamera.Start();
                Debug.Log("[CAMERA] start");
            }
        }

        public override void Begin(double now)
        {
            _round.Start(now);
            _clickBeat = -SwingRules.LeadInBeats;
            _playing = true;
            _now = now;
            // The HUD (web ui.hud + swingKings/hud.js): score block, outs lamps, the one-line instruction, the title card.
            Ui.ShowScoreboard();
            Ui.CornerScrims(true);   // the arches and bunting run into both top corners
            Ui.Outs(0);
            _mouse = Conducting;
            Ui.Hint(_cam ? HintCamera : _mouse
                ? HintMouse
                : "<color=#ffe58a>SPACE</color> / tap as the ball reaches the plate \u00B7 on the beat = <color=#ffe58a>HOME RUN</color>");
            _hintHidden = false;
            Ui.Banner("SWING KINGS", life: _mouse ? 2.2f : 1.5f);
            if (_mouse && Autoplay)
            {
                var scored = new List<Pitch>();
                foreach (var p in _round.Pitches) if (p.Scored) scored.Add(p);
                var targets = new double[scored.Count];
                var launches = new double[scored.Count];
                for (int i = 0; i < scored.Count; i++) { targets[i] = _round.Map.TimeAt(scored[i].TargetBeat); launches[i] = _round.Map.TimeAt(scored[i].LaunchBeat); }
                _bot = new ConductBot(targets, launches, now);
            }
        }

        public override void Press(string action, double time)
        {
            if (action != "a") return;
            if (!_mouse)
            {
                _round.Press(time);
                Host.PlaySfx("tick", time, gain: 0.05f);   // web beginWindup: the windup tick on every swing
                return;
            }
            if (_bot != null) return;                      // the scripted conductor plays; the host's autoplay taps are not used
            _pointer.Reset();
            BeginWindup(time);
        }

        public override void Release(string action, double time)
        {
            if (!_mouse || _bot != null || action != "a") return;
            CatchUp(time);                 // a stall can queue the button-up behind a stroke whose stop is not yet seen
            ReleaseSwing(time, "button");
        }

        public override void PointerMove(float y01, double time)
        {
            if (!_mouse || _cam || _bot != null || !_round.Holding) return;   // the camera, not the pointer, finds the stroke
            CatchUp(time);
            if (!_round.Holding) return;
            if (_pointer.Move(time, y01, out var ev)) OnGesture(ev);
        }

        /// Feed the resting samples a pointer that stopped moving owes the detector, up to audio time `t`.
        void CatchUp(double t)
        {
            if (!_round.Holding) return;
            while (_pointer.Update(t, out var ev))
            {
                OnGesture(ev);
                if (!_round.Holding) return;
            }
        }

        void OnGesture(GestureEvent ev)
        {
            if (_mouse) Debug.Log($"[CONDUCT] {ev.Kind.ToString().ToLowerInvariant()} t={ev.Time:F4} y={ev.Y:F3}");
            if (ev.Kind == GestureKind.Ictus) ReleaseSwing(ev.Time, "ictus");
        }

        // ------------------------------------------------------------ camera conducting

        /// Once a frame before the round advances: read what the tracker queued, in order, through the same detector
        /// path as the mouse. A raise starts the windup (unless one is on), an ictus during a windup swings.
        void CameraStep(double now)
        {
            var st = BonanzaCamera.Status;
            if (st == CameraStatus.Failed) { CameraFallBack(); return; }
            if (st == CameraStatus.Loading || st == CameraStatus.Idle)
            {
                _camWaited += Time.unscaledDeltaTime;
                if (_camWaited > CameraLoadGrace) CameraFallBack("no camera answer in " + CameraLoadGrace.ToString("0") + " s");
                return;
            }
            if (st != CameraStatus.Running) return;          // suspended: the page is hidden, the browser has the camera off
            _camBuf.Clear();
            BonanzaCamera.Drain(_camBuf);
            if (_camBuf.Count == 0) return;
            double perfNow = BonanzaCamera.PerfNow;
            foreach (var s in _camBuf)
            {
                if (s.Lost) { _camCond.Lost(); continue; }   // the hand left the frame: forget the half-made stroke
                if (_camCond.Feed(now, perfNow, s.PerfMs, s.Y, out var ev)) OnCameraGesture(ev);
            }
        }

        void OnCameraGesture(GestureEvent ev)
        {
            Debug.Log($"[CONDUCT] {ev.Kind.ToString().ToLowerInvariant()} t={ev.Time:F4} y={ev.Y:F3} src=camera");
            if (ev.Kind == GestureKind.Raise) { if (!_round.Holding) BeginWindup(ev.Time); }
            else if (_round.Holding) ReleaseSwing(ev.Time, "camera");
        }

        /// Permission denied, no device, no GPU for the model, or no answer: conduct with the mouse instead, and say so.
        void CameraFallBack(string why = null)
        {
            if (!_cam) return;
            _cam = false;
            Debug.Log("[CAMERA] fallback to mouse: " + (why ?? BonanzaCamera.Reason));
            StopCamera();
            Ui.Banner("NO CAMERA", "conduct with the mouse instead", new Color(1f, 0.9f, 0.54f), 1.8f);
            if (!_hintHidden) Ui.Hint(HintMouse);
        }

        void StopCamera()
        {
            if (!_camStarted) return;
            _camStarted = false;
            BonanzaCamera.Stop(_camToken);
            Debug.Log("[CAMERA] stop");
        }

        public override void OnResume() { _camCond.Lost(); }

        /// The button went down: the windup begins, and the ribbon starts drawing the gesture.
        void BeginWindup(double time)
        {
            _round.BeginWindup(time);
            _trace.Begin(BatTip.x, BatTip.y, BatTip.z);
            _traceHold = 0;
            Host.PlaySfx("tick", time, gain: 0.05f);       // web beginWindup
        }

        /// The release, or the ictus of a downstroke: swing. Power is the windup length when it was a real hold.
        void ReleaseSwing(double time) => ReleaseSwing(time, "bot");

        void ReleaseSwing(double time, string source)
        {
            if (!_round.ReleaseSwing(time)) return;
            Debug.Log($"[RELEASE] src={source} t={time:F4} hold={_round.LastHoldBeats:F2} power={_round.LastPower:F2}");
            _trace.SetPower(_round.LastPower);
            _traceHold = 0.26f;    // the release stroke is part of the gesture: keep drawing for a quarter second
        }

        Vector3 BatTip => _batPivot.TransformPoint(new Vector3(-BatLen, 0, 0));

        public override void Tick(double now, float dt)
        {
            double beatBefore = _round.BeatAt(_now);
            _now = now;
            if (_cam) CameraStep(now);
            if (_mouse) ConductStep(now);
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
                SetNight(night);
            }
            UpdateCrowd(beat);
            UpdateArming(beat);
            ScheduleClicks();
            // The song ends on a word, on the last bar's downbeat (web: GAME! and the fanfare).
            if (!_finishShown && beat >= SwingRules.EndBeat - 4) { _finishShown = true; Host.PlaySfx("fanfare", now); _env?.Crowd?.Wave(1, 2); }
            UpdatePitchBalls(beat);
            UpdateCamera(beat, dt);
            UpdateHud(beat);
        }

        /// The crowd's energy climbs with the round and the player's streak (web updateShow); the slam windup holds
        /// it high, and after the grand slam the stands wave every bar.
        void UpdateCrowd(double beat)
        {
            var crowd = _env?.Crowd;
            if (crowd == null) return;
            var section = SwingRules.SectionAt(beat);
            float baseE = section switch { Section.Lead => 0.28f, Section.Teach => 0.42f, Section.Play => 0.58f, Section.Escalate => 0.8f, Section.Finale => 1f, _ => 0.5f };
            float streak = Mathf.Clamp01(_round.Judge.Stats.Combo / 14f) * 0.25f;
            crowd.SetEnergy(Mathf.Clamp01(baseE + streak));
            if (beat >= SwingRules.FinaleBeat - 8 && beat < SwingRules.FinaleBeat) crowd.SetEnergy(0.95f);
            int whole = (int)System.Math.Floor(beat);
            if (beat >= SwingRules.FinaleBeat + 2 && whole != _curtainBeat)
            {
                _curtainBeat = whole;
                if (whole % 4 == 0) crowd.Wave(0.8f, 1.6f);
                crowd.SetEnergy(1);
            }
        }
        int _curtainBeat = int.MinValue;

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
            StopCamera();
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
            // web rig: target (-1.9, 1.95, 0), distance 12.6, height 2.15, yaw 0.055, fov 45. The rig owns the camera from here.
            Look.Rig.Frame(W(-1.9f, 1.95f, 0), 12.6f, 2.15f, 0.055f, 45, 2.4f, immediate: true);
            // The key's real shadow: the plate, the batter and the machine, inside a focus circle (web setShadowFocus).
            Look.ShadowFocus(W(-1.5f, 0, 0.1f), 9.2f);   // wider than the web's (-2.3, 6.8): the shadow now lies out to the side and must not fade before it ends

            BuildSet();
            BuildInfield();
            BuildMachine();
            BuildBatter();
            BuildPips();
            _ballMat = Mat(Hex(0xfffaf0), lift: 0.55f, rim: 1.2f, pulse: 0.3f);
            _ballOutlineMat = EnvKit.Surface(_env, Hex(0x1a1238), null, null, rim: 0, pulse: 0, bands: 1);
            _ballOutlineMat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);
            _ballOutlineMat.SetFloat("_ShadowRecv", 0);
            // Prewarm the pool (and the outline's program): three balls drawn once at load, then parked.
            var warm = new[] { AcquireBall(), AcquireBall(), AcquireBall() };
            foreach (var g in warm) { g.transform.position = new Vector3(0, -50, 0); FreeBall(g); }
            SetNight(false);
        }

        // The ballpark (web swingKings/world.js createWorld): the env kit's 'stage' preset (grass ground, arches,
        // the authored skyline, spotlights, floaters) plus bunting, a figure crowd in a stadium bowl, the authored
        // set edge, the infield dirt and two light towers that switch on with the night palette.
        Env _env;
        Material _bankMat;
        const float StandArc = Mathf.PI * 1.02f;

        void BuildSet()
        {
            _env = Look.CreateEnv(EnvPreset.Stage, o =>
            {
                o.Ground.Confetti = 0; o.Ground.Rings = false; o.Ground.EdgeRing = false;   // the track edge is SwingKingsSet's padded wall          // a ballpark, not a stage
                o.Ground.Map = "grass"; o.Ground.MapTiles = 7; o.Ground.Stripes = 16; o.Ground.StripeContrast = 0.3f;   // broad mow bands, like the web's
                o.Backdrop.Arches = 4; o.Backdrop.Radius = 24; o.Backdrop.Spacing = 7.5f; o.Backdrop.Z = 30;
                o.Backdrop.SkylineMesh = "sk-skyline";
                // Pale, bloomed hoops (the web's read white-gold in its bloom; the palette accent alone is mustard).
                o.Backdrop.ArchWhiten = 0.25f; o.Backdrop.Glow = 1.8f; o.Backdrop.SunSide = 0.35f;   // no bloom blow-out behind ACCURACY / OUTS
                // Floodlights: the cones hang from the light-tower heads and sweep the field, not the stands.
                o.Spotlights.Count = 4; o.Spotlights.Height = 17; o.Spotlights.Radius = 2.4f; o.Spotlights.Base = 0.035f; o.Spotlights.Flare = 0.05f;
                o.Spotlights.Anchors = new[] { TowerAt[0] + Vector3.up * BankY, TowerAt[0] + Vector3.up * BankY, TowerAt[1] + Vector3.up * BankY, TowerAt[1] + Vector3.up * BankY };
                o.Spotlights.Targets = new[] { new Vector3(PlatePos.x, 0, PlatePos.z), new Vector3(-1.5f, 0, -1.5f), new Vector3(MachinePos.x, 0, MachinePos.z), new Vector3(0, 0, 3) };
                o.Spotlights.Tint = Hex(0xfff0d6);
                // No floaters: a lone crystal in the sky over a ballpark read as a bug.
                o.Floaters.Count = 0;
            });
            // Calmer blade noise and a touch of lift: the web's turf reads as bright, clean bands.
            var turf = _env.Ground?.Top ? _env.Ground.Top.sharedMaterial : null;
            if (turf) { turf.SetFloat("_MapStrength", 0.62f); turf.SetFloat("_Lift", 0.06f); }   // the map carries the turf's breakup patches
            _env.AddBanners(new EnvBanners.Options { Count = 24, Radius = 13.5f, Y = 7.6f, Z = 3, Glow = 2.2f, Whiten = 0.12f, LineRadius = 0.018f, SizeJitter = 0.2f, Span = Mathf.PI * 0.95f });   // ends kept out of the HUD corners
            _env.AddCrowd(new EnvCrowd.Options
            {
                Style = EnvCrowd.FigureStyle.Figure, Stands = EnvCrowd.StandStyle.Bowl,
                Rows = 6, PerRow = 30, RowGrow = 3, Radius = 11.6f, RowDepth = 1.2f, RowRise = 0.634f,
                Arc = StandArc * 0.94f, BowlArc = StandArc, Center = new Vector3(0, 0.95f, 3), Scale = 1.22f, Seed = 0x5147,
                PaletteMix = 0.1f, BrightMin = 0.55f, BrightMax = 0.85f, Deepen = 1.35f, StandColor = Hex(0x5474ad), Energy = 0.28f,
                RoofTube = 0, Poses = true, Rim = 0.9f, FrontRowScale = 0.88f,
            });
            _env.AddMesh("sk-setedge", Color.white, rim: 0.5f, pulse: 0.06f);

            // Light towers frame the open (machine-side) edge the stands do not cover; their banks light at night.
            var towerMat = EnvKit.Surface(_env, Hex(0x262d3f), null, null, rim: 0.6f, pulse: 0.04f);
            _bankMat = EnvKit.Surface(_env, Hex(0x33302a), null, null, rim: 0.3f, pulse: 0.04f);
            var shafts = new MeshBuilder(); var banks = new MeshBuilder();
            foreach (var tz in new[] { 3.5f, -9.5f })
            {
                var at = W(-13, 0, tz);
                shafts.Shell(0.18f, 0.32f, 8.6f, 8, 0, Mathf.PI * 2, Matrix4x4.Translate(at + new Vector3(0, 4.3f, 0)), Color.white, Vector2.one);
                // The bank faces the field (yaw toward the plate), so the lit face is what the camera sees at night.
                var face = Quaternion.LookRotation((new Vector3(PlatePos.x, 0, PlatePos.z) - at).normalized);
                banks.Box(Matrix4x4.TRS(at + new Vector3(0, BankY, 0), face, new Vector3(1.7f, 1.0f, 0.28f)), Color.white);
                shafts.Box(Matrix4x4.TRS(at + new Vector3(0, BankY - 0.62f, 0), face, new Vector3(1.9f, 0.08f, 0.5f)), Color.white);   // catwalk
            }
            EnvKit.Static(_env, "TowerShafts", transform, shafts, towerMat);
            EnvKit.Static(_env, "TowerBanks", transform, banks, _bankMat);
        }

        /// Dark steel banks by day, a lit glow at night: "later in the set" changes the set, not just the grade.
        void SetNight(bool on)
        {
            if (!_bankMat) return;
            _bankMat.SetColor("_BaseColor", on ? Hex(0xfff3d0) : Hex(0x33302a));
            _bankMat.SetFloat("_Lift", on ? 2.6f : 0);
            _nightGoal = on ? 1 : 0;
        }

        /// 0 = coiled back, 1 = follow-through done (and back to rest).
        void PoseBat(float t)
        {
            float a = t >= 1 ? 60 : Mathf.Lerp(60, -150, Mathf.SmoothStep(0, 1, t));
            _batPivot.localRotation = Quaternion.Euler(0, a, 0);
        }

        void StartSwing() { _swingT = 0; StartSwingArc(); Cue(BatterCue.Swing); }

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
            UpdateFinaleShow(dt);
            UpdateBat(dt);
            UpdateSet(dt, _round != null ? _round.BeatAt(_now) : 0);
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
                2.15f, 0.055f + Mathf.Sin((float)beat * 0.11f) * 0.012f, 45, 2.4f);
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
                go.transform.localScale = Vector3.one * 0.34f;   // web: radius 0.2
                // A thin dark outline (inverted hull) so the white ball separates from a pale sky, cream stands and chalk.
                var hull = MeshObject(PrimitiveType.Sphere, _ballOutlineMat);
                hull.name = "Outline";
                hull.transform.SetParent(go.transform, false);
                hull.transform.localScale = Vector3.one * 1.16f;
                var hr = hull.GetComponent<MeshRenderer>();
                hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; hr.receiveShadows = false;
                Look.Blob(go.transform, 0.3f, 0.85f, 0, 0.6f);   // a firm shadow that stays readable at flight height
            }
            go.SetActive(true);
            return go;
        }

        void FreeBall(GameObject go) { go.SetActive(false); _ballPool.Push(go); }

        void OnLaunched(Pitch p)
        {
            if (_mouse && p.Scored) Debug.Log($"[LAUNCH] id={p.Id} eta={_round.Map.TimeAt(p.TargetBeat) - _now:F4}");
            var b = new Ball { Go = AcquireBall() };
            b.Go.transform.position = Muzzle;
            b.Go.transform.localScale = Vector3.one * 0.34f;
            b.Trail = Look.Fx.Trail(Hex(0x7fdcff) * 1.25f, 0.13f);   // a saturated cyan comet tail: the ball reads against sky and stands
            // Half-beat markers along the flight path: the ball is the metronome.
            SetPips(p);
            _pitchBalls[p] = b;
            FireMachine();
            // The machine's launch tick, and the demo swing's windup tick with it (web update(): tick 0.16, beginWindup 0.05).
            Host.PlaySfx("tick", _now, gain: 0.16f);
            if (p.Kind == PitchKind.Demo) Host.PlaySfx("tick", _now, gain: 0.05f);
        }


        void OnPassed(Pitch p)
        {
            if (!_pitchBalls.TryGetValue(p, out var b)) return;
            // Sailed past into the backstop net, with a puff.
            Look.Fx.Burst(b.Go.transform.position, 8, Hex(0x9fd8ff), 3, life: 0.4f);
            _netHit = 1;
            b.Trail?.Release();
            FreeBall(b.Go);
            ClearPips(p);
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
                b.Go.transform.localScale = Vector3.one * (0.34f * (1 + 0.22f * Mathf.Clamp01((float)u)));   // swells a little on approach
                b.Go.transform.localRotation = Quaternion.Euler((float)beat * 400f, 0, (float)beat * 250f);
                b.Trail?.Set(pos);
                int steps = Mathf.Min(PipMax, Mathf.Max(2, (int)System.Math.Round(p.Lead * 2)));
                while (b.PipCursor < steps && u >= (double)(b.PipCursor + 1) / steps - 0.02)
                {
                    PopPip(b.PipCursor, p);
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
            if (_pitchBalls.TryGetValue(p, out var pb)) { go = pb.Go; trail = pb.Trail; _pitchBalls.Remove(p); }
            else go = AcquireBall();
            ClearPips(p);

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
            Cue(p.Finale ? BatterCue.Finale : big ? BatterCue.BigHit : BatterCue.Contact, t.Id);
            if (big) _flinch = 1;   // PITCH-O watches it go
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
            o.MilestoneColor = Hex(0xffb347); o.MilestoneLift = 0.75f;   // gold waves; "xN" above the flash
            var fx = Look.Fx;
            fx.PlayVerdict(c.Verdict, Contact, o);
            fx.Impact(Contact, dirV, tierColor, big ? 24 : 12, 13, scale: scale);
            if (big && !c.IsDemo)
            {
                fx.SpeedLines(Contact, tierColor, roomy ? 16 : 8, 3.0f);
                // The home-run burst: ONE thick gold-orange shock ring out and gone in a quarter second, a hot
                // starburst flare on the bat, and short rays all round (the cone above carries the aim).
                fx.Ring(Contact, Hex(0xff8a1a), 0.25f, 0.25f * scale, 2.1f * scale, 0.55f, 0.12f, wobble: 0.002f, overlay: true);
                fx.Flare(Contact, Hex(0xffc860), 0.8f * scale, 0.18f, 0.4f, 2.2f);
                fx.Impact(Contact, Vector3.up, Hex(0xfff0c0), roomy ? 18 : 10, 17, 0.2f, 1f, scale * 0.8f);
                // The grand slam's shockwave and its echo reach well below the bat: overlay rings the field can't slice flat.
                if (p.Finale)
                {
                    fx.Ring(Contact, tierColor, 0.5f, 0.4f, 4.2f * scale, 0.2f, wobble: 0.004f, overlay: true);
                    fx.Ring(Contact, Hex(0xffd35a), 0.7f, 0.2f, 2.6f * scale, 0.14f, wobble: 0.004f, overlay: true);
                }
            }
            var rig = Look.Rig;
            if (!c.IsDemo) rig.Shake(big ? 0.12f : 0.05f, new Vector3(dir.x, 0.5f, 0));
            _env?.Crowd?.Hype(big ? 1.0f : t.Id == TierId.Liner ? 0.5f : 0.25f);
            if (big) _env?.Crowd?.Wave(1, 2.1f);

            // --- sound (web connect()): the hit, its verdict in the chord, a coin on a big one, the fanfare on the slam
            Host.PlaySfx("impact", _now);
            if (!c.IsDemo) Host.PlaySfx(SfxTheory.VerdictSfx(c.Verdict), _now + 0.01);
            if (big) Host.PlaySfx("coin", _now + 0.06);
            if (p.Finale) Host.PlaySfx("fanfare", _now + 0.1);

            var f = Feel.ForCombo(c.Verdict, c.Combo);
            if (!c.IsDemo)
            {
                float hs = Mathf.Min(Feel.HitstopMax * 1.6f, f.Hitstop * (big ? 1.5f : 1f) * (p.Finale ? 2.6f : 1f));
                // The freeze ends well before the next unjudged note arrives, and is visual only.
                Look.Hitstop(Feel.HitstopFor(hs, _round.Judge.Notes, _round.Map.TimeAt(p.TargetBeat)));
                rig.PunchZoom(1 + (big ? 0.1f : 0.03f) * (p.Finale ? 2.0f : 1f));   // a real FOV kick on a home run
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
                _finaleShow = 0;   // and the sky over the stands fills with fireworks and a slow confetti shower
            }
        }

        void OnWhiff(WhiffEvent w)
        {
            if (w.Swung) StartSwing();
            Cue(w.SideRetired ? BatterCue.SideRetired : w.Swung ? BatterCue.Whiff : BatterCue.Strike);
            if (_outsClear > 0) { _outsClear = 0; Ui.Outs(0); }   // a new out while SIDE RETIRED still holds the lamps: the new inning starts now
            Ui.Outs(w.Outs);
            Host.PlaySfx("miss", _now);
            _env?.Crowd?.Deflate(w.SideRetired ? 1.4f : 0.9f);
            if (w.SideRetired) _env?.Crowd?.Wave(0.55f, 1.4f);
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
                        if (_nightK > 0.5f) Firework(h.P);
                    }
                    h.Trail?.Release();
                    FreeBall(h.Go);
                    _hitBalls.RemoveAt(i);
                }
            }
        }

        /// Late innings: a home run that lands sets off a firework over the stands (web t70: the sky full of colour).
        void Firework(Vector3 landing)
        {
            var fx = Look.Fx;
            var at = new Vector3(landing.x * 0.6f + 2, 8.5f + (_fireworks % 3) * 0.7f, landing.z + 4);
            var col = Hex(Feel.Party[_fireworks % Feel.Party.Length]);
            _fireworks++;
            fx.Burst(at, 60, col, 11f, 0.42f, 1.1f, -2.5f);
            fx.Ring(at, col, 0.7f, 0.6f, 6.5f, 0.16f, 0.025f, 0.01f, 0.85f);
            fx.Flare(at, col, 2.4f, 0.25f);
            fx.Confetti(at, 60, null, 6.5f, 0.2f);
        }
        int _fireworks;

        /// After the grand slam: fireworks over the stands every ~0.6 s and big confetti drifting down in the sky,
        /// through the curtain call (web: the late frame full of colour).
        void UpdateFinaleShow(float dt)
        {
            if (_finaleShow < 0) return;
            float before = _finaleShow;
            _finaleShow += dt;
            for (int i = 0; i < FinaleShots.Length; i++)
            {
                float t = FinaleShots[i];
                if (before >= t || _finaleShow < t) continue;
                var at = new Vector3(-7 + (i * 5.3f) % 15, 8 + (i % 3) * 1.1f, 10 + (i % 2) * 4);
                var col = Hex(Feel.Party[i % Feel.Party.Length]);
                var fx = Look.Fx;
                fx.Burst(at, 60, col, 11f, 0.42f, 1.1f, -2.5f);
                fx.Ring(at, col, 0.7f, 0.6f, 6f, 0.16f, 0.025f, 0.01f, 0.85f);
                fx.Flare(at, col, 2.4f, 0.25f);
                fx.Confetti(at + Vector3.up * 2, 50, null, 3.5f, 0.6f, 0.5f, 3.2f);
            }
            if (_finaleShow > FinaleShots[FinaleShots.Length - 1] + 0.1f) _finaleShow = -1;
        }
        float _finaleShow = -1;
        static readonly float[] FinaleShots = { 0.35f, 0.9f, 1.5f, 2.1f, 2.8f, 3.4f, 4.1f, 4.8f, 5.6f, 6.4f };

        /// The conducting input, once a frame before the round advances.
        void ConductStep(double now)
        {
            if (_bot != null) _bot.Advance(now, BeginWindup, ReleaseSwing);
            else if (_round.Holding)
            {
                if (Host.InputLogging) Debug.Log($"[CONDUCT] frame now={now:F4} last={_pointer.LastTime:F4} state={_pointer.Detector.Current}");
                CatchUp(now);
            }   // a pointer that has stopped sends no events: the detector is fed its resting position
        }

        // ------------------------------------------------------------ the conducting trace (web trace.js)

        void BuildTrace()
        {
            _trace = new ConductTrace();
            _traceMesh = new Mesh { name = "ConductTrace" };
            _traceMesh.MarkDynamic();
            _traceMat = Mat(Color.white, lift: 1f, rim: 0, pulse: 0, bands: 1);
            _traceGo = MeshObject(PrimitiveType.Cube, _traceMat);
            _traceGo.name = "ConductTrace";
            _traceGo.GetComponent<MeshFilter>().sharedMesh = _traceMesh;
            var mr = _traceGo.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = false;
            _traceVerts = new Vector3[ConductTrace.MaxSamples * 2];
            _traceCols = new Color[ConductTrace.MaxSamples * 2];
        }

        void UpdateTrace(float dt)
        {
            var mr = _traceGo.GetComponent<MeshRenderer>();
            if (!_mouse) { mr.enabled = false; return; }
            if (_round.Holding) _trace.Push(dt, BatTip.x, BatTip.y, BatTip.z, _round.WindupPower(_now));
            else if (_traceHold > 0)
            {
                _traceHold -= dt;
                _trace.Push(dt, BatTip.x, BatTip.y, BatTip.z, _round.LastPower);
                if (_traceHold <= 0) _trace.Release();
            }
            _trace.Update(dt);
            int n = _trace.Count;
            if (n < 2 || (!_trace.Live && _trace.Fade <= 0)) { mr.enabled = false; return; }

            var cam = Camera.main;
            Vector3 camF = cam ? cam.transform.forward : Vector3.forward;
            for (int i = 0; i < n; i++)
            {
                int i0 = Mathf.Max(0, i - 1), i1 = Mathf.Min(n - 1, i + 1);
                var seg = new Vector3(_trace.X[i1] - _trace.X[i0], _trace.Y[i1] - _trace.Y[i0], _trace.Z[i1] - _trace.Z[i0]);
                if (seg.sqrMagnitude < 1e-9f) seg = Vector3.up;
                var perp = Vector3.Cross(seg, camF);
                if (perp.sqrMagnitude < 1e-9f) perp = Vector3.up;
                perp.Normalize();
                float u = n > 1 ? i / (float)(n - 1) : 1f;
                float hw = (float)_trace.HalfWidth(u);
                var c = new Vector3(_trace.X[i], _trace.Y[i], _trace.Z[i]);
                _traceVerts[i * 2] = c + perp * hw;
                _traceVerts[i * 2 + 1] = c - perp * hw;
            }
            var tris = new int[(n - 1) * 12];
            for (int s = 0; s < n - 1; s++)
            {
                int a = s * 2, b = a + 1, c2 = a + 2, d = a + 3, o = s * 12;
                tris[o] = a; tris[o + 1] = b; tris[o + 2] = c2; tris[o + 3] = b; tris[o + 4] = d; tris[o + 5] = c2;
                tris[o + 6] = a; tris[o + 7] = c2; tris[o + 8] = b; tris[o + 9] = b; tris[o + 10] = c2; tris[o + 11] = d;   // back faces: the ribbon reads from both sides
            }
            _traceMesh.Clear();
            var verts = new Vector3[n * 2];
            System.Array.Copy(_traceVerts, verts, n * 2);
            _traceMesh.vertices = verts;
            _traceMesh.triangles = tris;
            _traceMesh.RecalculateBounds();
            ConductTrace.RampColor(_trace.Power, out float r, out float g, out float bl);
            _traceMat.SetColor("_BaseColor", new Color(r, g, bl, 1));
            mr.enabled = true;
        }

        protected override void OnDestroy()
        {
            StopCamera();
            if (_traceMesh) Destroy(_traceMesh);
            base.OnDestroy();
        }

        bool _wasCoiled;

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
            if (coiled && !_wasCoiled) Cue(BatterCue.Coil);
            _wasCoiled = coiled;
            float rest = coiled ? 75 : 60;
            // Conducting: the bat winds back further the longer the button is held, so the ribbon traces the coil.
            if (_mouse && _round.Holding) rest = 70 + 55 * (float)_round.WindupPower(_now);
            if (_swingT < 1) PoseBat(_swingT);
            else _batPivot.localRotation = Quaternion.Slerp(_batPivot.localRotation, Quaternion.Euler(0, rest, 0), 1 - Mathf.Exp(-dt * 12));
            UpdateTrace(dt);
        }

        // The HUD is on the canvas (UseCanvasHud): no DrawHud.
    }
}
