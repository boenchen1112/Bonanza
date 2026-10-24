// Swing Kings on the shared look kit (Look/), wired to DownbeatRound (Bonanza.Core): find the downbeat of a
// hymn by ear. Every beat sends one identical ball; the ones on the downbeat are real, the rest are bombs in
// disguise (revealed only when swung at or when they fizzle past the plate). No rule logic lives here.
// The ballpark around the field is the environment kit (Look/Env: grass, arches, skyline, bunting, a beat-bouncing crowd in a
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
using System.Linq;
using Bonanza.Core;
using Bonanza.Look;
using UnityEngine;

namespace Bonanza
{
    public sealed partial class SwingKingsGame : Minigame
    {
        public override string Id => "swing-kings";
        public override string Title => "SWING KINGS";
        public override string Blurb => "Find the downbeat. Hit the ball, dodge the bombs.";
        public override bool OwnsMusic => true;   // the hymns' rendered clips (HymnData), through Host.PlayClip
        public override double SecondsPerBeat
        {
            get
            {
                var s = _round?.CurrentSlot ?? (_round != null && _round.Slots.Count > 0 ? _round.Slots[0] : null);
                // the pause menu counts back in at this tempo: a hymn's own beat when quick, never slower than half a second
                return s != null && s.Pitches.Count > 1 ? System.Math.Max(0.25, System.Math.Min(0.5, s.Pitches[1].Time - s.Pitches[0].Time)) : 0.5;
            }
        }
        protected override bool UseLook => true;
        public override bool UseCanvasHud => true;
        bool _night;

        public override string Controls => CameraMode
            ? "Listen: a ball comes on EVERY beat, but only the DOWNBEAT (beat one of the bar) is real. The rest are BOMBS.\nRAISE your hand to the camera, then CONDUCT DOWN on the downbeat. The stop of the stroke is the swing.\nA bomb you swing at explodes: a strike. The first two bars of each hymn are free. Three strikes retire the side.\nNo camera? The mouse takes over: HOLD it, then flick DOWN."
            : Conducting
            ? "Listen: a ball comes on EVERY beat, but only the DOWNBEAT (beat one of the bar) is real. The rest are BOMBS.\nHOLD the mouse (or SPACE), then flick DOWN on the downbeat. The stop of the stroke is the swing.\nA bomb you swing at explodes: a strike. The first two bars of each hymn are free. Three strikes retire the side."
            : "Listen: a ball comes on EVERY beat, but only the DOWNBEAT (beat one of the bar) is real. The rest are BOMBS.\nSPACE / click / tap on the downbeat only. On the beat = HOME RUN.\nA bomb you swing at explodes: a strike. The first two bars of each hymn are free. Three strikes retire the side.";

        // Mouse conducting (ADR 0004, web swingKings/index.js): the button down is the windup, a downward flick of
        // the pointer fires the swing at the ictus (the stroke's stop), and releasing the button swings too.
        // Camera conducting (ticket 28, web camera.js): the browser tracks the palm and the same detector finds the
        // stroke: a raise starts the windup, the stop of the downstroke swings. Where the browser has no camera API
        // (SwingInputEffective) or the camera fails, the mouse conducts instead. Tap is the default.
        static bool Conducting => Settings.SwingInputEffective != SwingInput.Tap;
        static bool CameraMode => Settings.SwingInputEffective == SwingInput.Camera;
        const string HintMouse = "<color=#ffe58a>HOLD</color> the mouse \u00B7 <color=#ffe58a>SWING DOWN</color> on the downbeat only \u00B7 the other balls are bombs";
        const string HintCamera = "<color=#ffe58a>RAISE</color> your hand \u00B7 <color=#ffe58a>CONDUCT DOWN</color> on the downbeat only \u00B7 the other balls are bombs";
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

        DownbeatRound _round;
        bool _playing;
        double _now;

        // the hymns (as HymnMeterGame): one rendered clip per excerpt, played from the excerpt's Shift
        const double LeadIn = 2.0;       // seconds of silence before the first hymn leads in
        const double PreRoll = 0.5;      // the clip starts this long before the first beat
        const double ResultHold = 1.8;   // the last swing stays on screen this long before the results card
        HymnData _data;
        readonly AudioClip[] _clips = new AudioClip[DownbeatRound.ExcerptCount];
        IReadOnlyList<int> _ids;
        int _musicSlot = -1, _stoppedSlot = -1;
        double _doneAt = -1;

        // stage
        Transform _batPivot;
        float _swingT = 1;               // 0..1 through the follow-through; 1 = at rest
        readonly Dictionary<Pitch, Ball> _pitchBalls = new Dictionary<Pitch, Ball>();
        readonly List<HitBall> _hitBalls = new List<HitBall>();
        readonly Stack<GameObject> _ballPool = new Stack<GameObject>();
        Material _ballMat, _ballOutlineMat, _hitBallMat;

        bool _finishShown;
        DownbeatPitch _lastBall;

        // HUD (canvas: Ui). Presentation only; it ages on real time.
        float _dim;                     // eased stage-light dim while SIDE RETIRED holds
        float _finishPush;              // slow push-in on the batter through the finish
        double _fbSplitAt = -1, _fbLiveAt = -1; string _fbLevel;   // the free-bars counter and the live stinger (clocked off the bar line)
        float _outsClear;               // seconds until the lamps reset after the side is retired
        bool _hintHidden;
        float _camPush, _camLift;

        sealed class Ball { public GameObject Go; public int PipCursor; public TrailHandle Trail; }
        sealed class HitBall { public GameObject Go; public Vector3 P, V; public float T, Life, GravMul, Fade, DimK; public bool Bounce, Slam, Land, Dim; public TrailHandle Trail; public Color Color; }

        // ------------------------------------------------------------ contract

        // The SFX chord lookup (Host.PlaySfx) wants a beat for the clock: the round's beat axis once it runs.
        public override double MusicBeatAt(double time) => _round != null && _round.Clock != null ? _round.Clock.BeatAt(time) : time * 2;
        public override double MusicTimeAt(double beat) => _round != null && _round.Clock != null ? _round.Clock.TimeAt(beat) : beat / 2;

        public override bool AudioReady
        {
            get
            {
                foreach (var c in _clips)
                {
                    if (c == null) continue;
                    if (c.loadState == AudioDataLoadState.Loading || c.loadState == AudioDataLoadState.Unloaded) return false;
                }
                return true;
            }
        }

        public override void Load()
        {
            LoadHymns();   // first: a build without the private hymns fails here, before the stage is built
            // Every beat pulses alike: the set must never count the bar for the player (the downbeat is for the ear).
            Look.EvenBeats = true;
            BuildStage();
            var fx = Look.Fx;
            fx.SetGroundY(0);
            fx.SetFocus(W(-1.6f, 1.6f, 0));
            fx.SetConfettiFade(0.9f, 2.4f);   // paper belongs to the stands and the sky: it melts before it reaches the field
            fx.Prewarm();   // touch every FX material and the callout font now: a first-use shader link mid-round would push a press out of its window
            _round.Judged += (v, err) =>
            {
                MusicJudged(v != Verdict.Miss);
                // The conducting check (Tools/mouse-check.mjs) reads these two lines.
                if (_mouse) Debug.Log($"[SWING] verdict={v.ToString().ToLowerInvariant()} err={err:F1} power={_round.LastPower:F2} hold={_round.LastHoldBeats:F2} pts={_round.Points:F0}");
            };
            _round.ExcerptStarted += OnExcerptStarted;
            _round.PitchLaunched += OnLaunched;
            _round.PitchPassed += OnPassed;
            _round.Contact += OnContact;
            _round.Whiff += OnWhiff;
            _round.Explode += OnExplode;
            _round.Fizzle += OnFizzle;
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

        /// The hymn pool, the three excerpts and their clips (decoded now, never mid-round), as HymnMeterGame.Load.
        void LoadHymns()
        {
            _data = HymnData.Load();   // throws "hymn data missing: ..." for the host's fault path
            if (_data.Problems.Count > 0) Debug.LogWarning("[ERROR] hymn data: " + string.Join("; ", _data.Problems));
            List<double> skills = null;
            if (Host.Lineup != null)
            {
                skills = Host.Lineup.Where(p => p.IsCpu).Select(p => p.CpuSkill).ToList();
                var human = Host.Lineup.FirstOrDefault(p => !p.IsCpu);
                var ids = new List<int> { human != null ? human.Id : 0 };
                ids.AddRange(Host.Lineup.Where(p => p.IsCpu).Select(p => p.Id));
                _ids = ids;
            }
            // A new set of hymns each time: the game's seed, moved by how often it has been played.
            uint seed = unchecked((uint)(Rng.Next() * 4294967295.0) + (uint)Save.Get(Id).Plays * 0x9e3779b1u);
            _round = new DownbeatRound(_data.Pool, seed, skills, Save.OffsetMs);
            for (int k = 0; k < DownbeatRound.ExcerptCount; k++)
            {
                var cand = _data.Pool.First(c => c.Name == _round.Playlist[k]);
                string name = cand.ClipFor(DownbeatRound.Levels[k]);
                _clips[k] = HymnData.Clip(name);
                if (_clips[k] == null) throw new System.InvalidOperationException("hymn data missing: clip " + name);
                _clips[k].LoadAudioData();
            }
        }

        public override void Begin(double now)
        {
            _round.Start(now + LeadIn);   // no count-in: the downbeat is for the ear to find
            _lastBall = _round.ScoredBalls().LastOrDefault();
            _playing = true;
            _now = now;
            // The HUD (web ui.hud + swingKings/hud.js): score block, outs lamps, the one-line instruction, the title card.
            Ui.ShowScoreboard();
            Ui.CornerScrims(true);   // the arches and bunting run into both top corners
            Ui.OutMarks = true;
            Ui.Outs(0);
            _mouse = Conducting;
            Ui.Hint(_cam ? HintCamera : _mouse
                ? HintMouse
                : "<color=#ffe58a>SPACE</color> / tap on the <color=#ffe58a>DOWNBEAT</color> only \u00B7 the other balls are bombs");
            _hintHidden = false;
            Ui.Banner("SWING KINGS", life: _mouse ? 2.2f : 1.5f);
            if (_mouse && Autoplay)
            {
                // The scripted conductor swings at the scored downbeats only; it starts each windup 0.9 s before the ball
                // lands (one beat of flight is shorter than the stroke it has to play).
                var balls = _round.ScoredBalls().ToList();
                var targets = new double[balls.Count];
                var launches = new double[balls.Count];
                for (int i = 0; i < balls.Count; i++) { targets[i] = balls[i].Time; launches[i] = balls[i].Time - 0.9; }
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
            _now = now;
            if (_cam) CameraStep(now);
            if (_mouse) ConductStep(now);
            _round.Update(now);
            double beat = _round.BeatAt(now);
            DriveMusic(now);
            // Beat flash on the audio clock, and the dusk -> night swap when the fast section starts.
            Look.BeatPulse = beat < 0 ? 0 : Mathf.Exp(-5f * Phase(beat));
            Look.TickBeat(beat);
            bool night = Night;
            if (night != _night)
            {
                _night = night;
                Look.SetPalette(night ? "swing-kings-night" : "swing-kings", night ? 2.4f : 0.8f);
                SetNight(night);
            }
            UpdateCrowd(beat);
            UpdateArming(beat);
            // The last excerpt ends on a word (web: GAME! and the fanfare).
            if (!_finishShown && _round.Over) Finish();
            UpdatePitchBalls(beat);
            UpdateCamera(beat, dt);
            UpdateHud(beat);
        }

        /// The crowd's energy climbs with the excerpt's level and the player's streak (web updateShow).
        void UpdateCrowd(double beat)
        {
            var crowd = _env?.Crowd;
            if (crowd == null) return;
            var slot = _round.CurrentSlot;
            float baseE = slot == null ? 0.28f : slot.Level == HymnLevel.Easy ? 0.45f : slot.Level == HymnLevel.Medium ? 0.62f : 0.8f;
            float streak = Mathf.Clamp01(_round.Combo / 14f) * 0.25f;
            crowd.SetEnergy(Mathf.Clamp01(baseE + streak));
        }

        /// The last (Hard) excerpt plays under the night grade, the fast half of the old set.
        bool Night => _round.CurrentSlot != null && _round.CurrentSlot.Level == HymnLevel.Hard;

        /// The readouts follow the judge; the instruction bar goes once the teach section is over (web: beat 16).
        void UpdateHud(double beat)
        {
            Ui.Score(Mathf.RoundToInt((float)_round.Points));
            Ui.Combo(_round.Combo);
            Ui.Accuracy((float)_round.Accuracy);
            if (!_hintHidden && beat >= 16) { _hintHidden = true; Ui.HideHint(); }
        }

        public override Outcome Result()
        {
            if (!_round.Over) return null;
            if (_doneAt < 0) _doneAt = _now;
            if (_now - _doneAt < ResultHold) return null;
            _playing = false;
            StopCamera();
            return _round.Result(_ids);
        }

        public override IEnumerable<KeyValuePair<string, double>> TestChart()
        {
            foreach (var t in _round.PerfectPresses()) yield return new KeyValuePair<string, double>("a", t);
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
            BuildBombs();
            _ballMat = Mat(Hex(0xfffaf0), lift: 0.55f, rim: 1.2f, pulse: 0.3f);
            // A struck ball turns hot gold at once: with a pitch leaving on every beat, the only WHITE ball in the air
            // is the live pitch (the hit one is still climbing when the next is fired).
            _hitBallMat = Mat(Hex(0xffc94a), lift: 0.9f, rim: 1.4f, pulse: 0.2f);
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
                o.Ground.Map = "grass"; o.Ground.MapTiles = 9; o.Ground.Stripes = 16; o.Ground.StripeContrast = 0.3f;   // broad mow bands, like the web's
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
            if (turf) { turf.SetFloat("_MapStrength", 0.72f); turf.SetFloat("_Lift", 0.05f); }
            _turfMat = turf;   // the map carries the turf's clumps, cross-mow and breakup patches
            _env.AddBanners(new EnvBanners.Options { Count = 20, Radius = 13.5f, Y = 6.7f, Sag = 1.2f, Z = 3, Glow = 2.2f, Whiten = 0.12f, LineRadius = 0.018f, SizeJitter = 0.2f, Span = Mathf.PI * 0.66f });   // ends kept out of the top 130 px HUD corners
            _env.AddCrowd(new EnvCrowd.Options
            {
                Style = EnvCrowd.FigureStyle.Figure, Stands = EnvCrowd.StandStyle.Bowl,
                Rows = 6, PerRow = 30, RowGrow = 3, Radius = 11.6f, RowDepth = 1.2f, RowRise = 0.634f,
                Arc = StandArc * 0.94f, BowlArc = StandArc, Center = new Vector3(0, 0.95f, 3), Scale = 1.22f, Seed = 0x5147,
                PaletteMix = 0.1f, BrightMin = 0.55f, BrightMax = 0.85f, Deepen = 1.35f, StandColor = Hex(0x5474ad), Energy = 0.28f,
                RoofTube = 0, Poses = true, Rim = 0.9f, FrontRowScale = 0.88f,
            });
            _env.AddMesh("sk-setedge", Color.white, rim: 0.5f, pulse: 0.06f);
            // The authored skyline takes the (height) fog too, so the city sits back behind the bowl.
            var skyR = _env.Backdrop?.Root ? _env.Backdrop.Root.transform.Find("Skyline")?.GetComponent<Renderer>() : null;
            if (skyR) skyR.sharedMaterial.SetFloat("_FogMul", 0.55f);

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

        // ------------------------------------------------------------ visuals (run on through results)

        void Update()
        {
            // Presentation time: 0 during a hitstop, so the struck ball, the bat and the callouts hang for a few
            // frames. Pitch balls ride the audio beat and judging never reads this, so a freeze can not move a window.
            float dt = LookRig.VisualDt(Time.deltaTime);
            if (!_playing) Look.BeatPulse = 0;
            UpdateHitBalls(dt);
            UpdateBombs(dt);
            UpdateFinaleShow(dt);
            UpdateBat(dt);
            UpdateSet(dt, _round != null ? _round.BeatAt(_now) : 0);
            _dim = Damp(_dim, _outsClear > 0 ? 1 : 0, _outsClear > 0 ? 6f : 2.5f, Time.deltaTime);
            Look.Dim = Settings.ReduceMotion ? _dim * 0.5f : _dim;
            if (_finishShown) _finishPush = Mathf.Min(1, _finishPush + Time.deltaTime / 5f);
            if (_fbSplitAt > 0 && _now >= _fbSplitAt)
            {
                _fbSplitAt = -1;
                Ui.Banner("FREE BAR 2/2", "LISTEN FOR THE DOWNBEAT", new Color(0.62f, 0.9f, 1f), Mathf.Max(1f, (float)(_fbLiveAt - _now) * 0.8f), plate: true, y: 0.73f, scale: 0.5f);
            }
            if (_fbLiveAt > 0 && _now >= _fbLiveAt)
            {
                _fbLiveAt = -1;
                Ui.Banner("PLAY BALL!", null, Bonanza.HudKit.UiKit.Gold, 0.8f, plate: true, y: 0.72f, scale: 0.8f);
            }
            // "SIDE RETIRED" holds all three lamps, then the count starts over (web _outsClear).
            if (_outsClear > 0)
            {
                _outsClear -= Time.deltaTime;
                if (_outsClear <= 0) Ui.Outs(0);
            }
        }

        /// The web's camera: eases to a target that leans in on a hit (camPush) and sits a little closer in the night half. The rig adds the beat push, shake and punch zoom on top.
        void UpdateCamera(double beat, float dt)
        {
            bool night = Night;
            _camLift = Damp(_camLift, 0, 3.4f, dt);
            _camPush = Damp(_camPush, 0, 3.2f, dt);
            Look.Rig.Frame(
                W(-1.9f + _camPush * 0.9f + _finishPush * 0.5f, 1.95f + _camPush * 0.15f + _camLift * 0.42f, 0),
                (night ? 12.0f : 12.6f) - _camPush * 0.5f + _camLift * 0.7f - _finishPush * 2.2f * (Settings.ReduceMotion ? 0.4f : 1),
                2.15f, 0.055f + Mathf.Sin((float)beat * 0.11f) * 0.012f, 45, 2.4f);
        }

        /// Seconds from this pitch's target to the next BALL's (the space its feedback has). A ball comes on every
        /// downbeat, a beat or two to four apart: the bombs in between do not count, they are all alike.
        float GapAfter(Pitch p)
        {
            var d = p as DownbeatPitch;
            if (d == null) return 1;
            foreach (var q in _round.Pitches) if (q.IsBall && q.Time > d.Time + 1e-6) return (float)(q.Time - d.Time);
            return 2;
        }

        /// The clip of each excerpt starts PreRoll before its first beat (the hymn leading in) and stops after its last bar.
        void DriveMusic(double now)
        {
            if (_round.Slots.Count == 0) return;
            int k = _musicSlot + 1;
            if (k < _round.Slots.Count && now >= _round.Slots[k].StartTime - PreRoll)
            {
                // File time 0 sounds at the slot's Shift; the deck starts in the middle if that is already past.
                Host.PlayClip(_clips[k], _round.Slots[k].Shift, _data.BeatOffset);
                _musicSlot = k;
            }
            if (_musicSlot >= 0 && _stoppedSlot < _musicSlot && now > _round.Slots[_musicSlot].EndTime + 0.05)
            {
                _stoppedSlot = _musicSlot;
                Host.MusicStop(0.4f);
            }
        }

        /// A new hymn: the free bars start now (no count-in). The banner says so and fades on its own clock, a little
        /// before the scored bars begin and never on a bar line: its exit must not show where the downbeat is.
        void OnExcerptStarted(DownbeatSlot slot)
        {
            double firstScored = slot.EndTime;
            foreach (var q in slot.Pitches) if (!q.IsFree) { firstScored = q.Time; break; }
            float free = (float)(firstScored - _now);
            float life = Mathf.Max(2.2f, free * 0.72f + 0.13f);
            string level = slot.Level.ToString().ToUpperInvariant();
            // Low and small, off the ball's arc (the flight crosses the upper frame). The counter changes and the live
            // stinger lands at times with a per-hymn jitter, never on the bar line: they must not show where the downbeat is.
            float jit = ((slot.Index * 37) % 11) / 100f;
            _fbSplitAt = _now + free * (0.30 + jit); _fbLiveAt = _now + free * (0.62 + jit);
            Ui.Banner("FREE BAR 1/2", $"HYMN {slot.Index + 1}/{DownbeatRound.ExcerptCount} · {level} · LISTEN FOR THE DOWNBEAT",
                new Color(0.62f, 0.9f, 1f), Mathf.Max(1f, free * (0.30f + jit)), plate: true, y: 0.73f, scale: 0.5f);
            SkvLog($"ev=excerpt k={slot.Index} free={free:F2}");
        }

        /// The round is over: the last hymn ends on a word (web: GAME! and the fanfare), the crowd rises and the sky
        /// over the stands fills with fireworks through the hold before the results card.
        void Finish()
        {
            _finishShown = true;
            SkvLog("ev=over");
            Host.PlaySfx("fanfare", _now);
            _env?.Crowd?.Wave(1, 2);
            _env?.Crowd?.Hype(1);
            Look.Fx.ClearBadges();
            Ui.Banner("GAME!", null, Bonanza.HudKit.UiKit.Gold, 1.7f, plate: true, scale: 1.2f, y: 0.36f);
            if (_finaleShow < 0) _finaleShow = 0;
            Look.Fx.Confetti(new Vector3(-1, 8.5f, 12), 90, null, 4, 0.4f, 0.11f, 3.6f);
        }

        /// The shared card's opt-ins: centred, a filled rank tile in the rank colour with its glow, the sticker on it,
        /// and stat labels in near-white at a readable size.
        public override void ResultsStyle(Bonanza.HudKit.ResultsData d)
        {
            d.Centered = true; d.RankFilled = true; d.BadgeOnRank = true; d.StatLabelPx = 12f;
        }

        /// The results card's stat row: what this game is about (finding downbeats, dodging bombs), not the judge counts.
        public override List<KeyValuePair<string, string>> ResultsStats(Outcome o)
        {
            if (_round == null || !_round.Over) return null;
            int balls = 0, hit = 0, bombs = 0;
            foreach (var p in _round.Pitches)
            {
                if (p.IsFree) continue;
                if (p.IsBall) { balls++; if (p.Swung) hit++; }
                else bombs++;
            }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("BEATS", $"{hit}/{balls}"),
                new KeyValuePair<string, string>("HOMERS", _round.Homers.ToString(inv)),
                new KeyValuePair<string, string>("DODGED", $"{bombs - _round.Bombs}/{bombs}"),
                new KeyValuePair<string, string>("BOOMS", _round.Bombs.ToString(inv)),
                new KeyValuePair<string, string>("STREAK", _round.Stats.MaxCombo.ToString(inv)),
            };
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
                Look.Blob(go.transform, 0.4f, 1f, 0, 0.6f);   // a firm, wide shadow that stays readable on the turf at flight height
            }
            go.SetActive(true);
            return go;
        }

        void FreeBall(GameObject go)
        {
            go.SetActive(false);
            go.transform.localScale = Vector3.one * 0.34f;
            var r = go.GetComponent<MeshRenderer>();
            if (r.sharedMaterial != _ballMat) r.sharedMaterial = _ballMat;
            _ballPool.Push(go);
        }

        void OnLaunched(Pitch p)
        {
            // The check harnesses conduct on these: the scored downbeat balls only, numbered from 1 (a bomb never logs).
            var dp = (DownbeatPitch)p;
            if (_mouse && dp.Ordinal > 0) Debug.Log($"[LAUNCH] id={dp.Ordinal} eta={dp.Time - _now:F4}");
            // (capture harness only, ?inputlog: the kind is in the console, never on screen)
            SkvLog($"ev=launch ball={(dp.IsBall ? 1 : 0)} free={(dp.IsFree ? 1 : 0)} eta={dp.Time - _now:F4}");
            var b = new Ball { Go = AcquireBall() };
            b.Go.transform.position = Muzzle;
            b.Go.transform.localScale = Vector3.one * 0.34f;
            b.Trail = Look.Fx.Trail(PitchTrail, 0.14f);   // every pitch's warm comet tail from release (a hit ball's takes the tier colour)
            // A pitch leaves on every beat: the hit ball before it gets out of the way now, so only the live pitch reads
            // as a white ball. A grounder or a liner shrinks away at once; a home run flies on into the stands as a
            // small gold comet (its trail carries it), never a second white ball.
            foreach (var h in _hitBalls) if (h.Fade <= 0 && h.T > 0.08f) { if (h.Land) h.Dim = true; else h.Fade = 0.0001f; }
            // Half-beat markers along the flight path: the ball is the metronome.
            SetPips(p);
            _pitchBalls[p] = b;
            FireMachine();
            // The machine's launch tick: on every beat alike, so it is a soft thump under the hymn, not a metronome over it.
            Host.PlaySfx("tick", _now, gain: LaunchTickGain);
        }

        /// The launch tick's gain (web 0.16 with a pitch every other bar). A pitch now leaves on every beat of the hymn:
        /// at 0.16 the tick peaked within ~2 dB of the organ's RMS and read as a click track; 0.065 sits ~8 dB under it.
        const float LaunchTickGain = 0.065f;
        static readonly Color PitchTrail = new Color(1f, 0.914f, 0.69f) * 1.15f;


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
                double u = DownbeatRound.FlightU(p, beat);
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
            SkvLog($"ev=contact v={c.Verdict.ToString().ToLowerInvariant()} err={c.ErrMs:F1} tier={c.Tier.Id} foul={(c.Foul ? 1 : 0)}");
            // The grand finish: the round's last scored downbeat hit for a home run gets the old grand slam's show.
            bool fin = p == _lastBall && c.Tier.Id == TierId.Homer && !c.Foul;
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
                // A home run lands IN the stands, in frame, within about two beats (a pitch follows on every beat).
                float T2 = Mathf.Clamp((float)_round.Clock.SpbAt(_now) * 1.9f, 0.85f, 1.35f); float g2 = Grav * 0.6f;
                float lx = 2.4f + (c.Combo % 5) * 0.6f, lz = -14.0f - (c.Combo % 3) * 0.8f;
                float ly = 1.9f + Mathf.Max(0, Mathf.Sqrt(lx * lx + (lz + 3) * (lz + 3)) - 11.6f) * 0.53f;
                var L = W(lx, ly, lz);
                v = new Vector3((L.x - Contact.x) / T2, (L.y - Contact.y + 0.5f * g2 * T2 * T2) / T2, (L.z - Contact.z) / T2);
                land = T2;
            }
            bool big = t.Id == TierId.Homer || t.Id == TierId.Slam;
            Cue(fin ? BatterCue.Finale : big ? BatterCue.BigHit : BatterCue.Contact, t.Id);
            if (big) _flinch = 1;   // PITCH-O watches it go
            var tierColor = Hex(t.Color);
            go.GetComponent<MeshRenderer>().sharedMaterial = _hitBallMat;
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
            o.MilestoneColor = Hex(0xffb347);
            // No loops round the batter (the starburst below carries the aim), no paper on the field (the stands get the
            // confetti), flares clamped so stacked bursts never white out the plate, and the "xN" low and left of the
            // plate, clear of the badge (upper right) and the flash.
            o.NoLoops = true; o.NoConfetti = true; o.FlareGain = 0.62f; o.MilestoneOffset = new Vector3(-1.75f, -0.55f, -0.9f);
            var fx = Look.Fx;
            fx.PlayVerdict(c.Verdict, Contact, o);
            fx.Impact(Contact, dirV, tierColor, big ? 24 : 12, 13, scale: scale);
            if (big && !c.IsDemo)
            {
                fx.SpeedLines(Contact, tierColor, roomy ? 16 : 8, 3.0f);
                // The home-run burst: a directional starburst of long gold streaks thrown along the hit, a warm (not
                // white) flare on the bat that stays under the bloom's blow-out, and short rays all round.
                fx.Impact(Contact, dirV, Hex(0xffc24a), roomy ? 16 : 10, 26, 0.34f, 0.07f, scale);
                fx.Impact(Contact, dirV, Hex(0xff7a1a), roomy ? 10 : 6, 15, 0.26f, 0.16f, scale);
                fx.Flare(Contact, Hex(0xff9a3a) * 0.8f, 0.85f * scale, 0.2f, 0.4f, 2.0f);
                fx.Impact(Contact, Vector3.up, Hex(0xffe0a0), roomy ? 14 : 10, 15, 0.18f, 1f, scale * 0.7f);
                // one quick gold shockwave off the bat (short-lived: the next pitch is already on its way)
                fx.Ring(Contact, Hex(0xffc24a), 0.26f, 0.3f, 2.3f * scale, 0.2f, 0.03f, 0.01f, 0.8f, overlay: true);
                fx.Burst(Contact + dirV * 0.2f, roomy ? 14 : 10, Hex(0xffd35a), 9f, 0.12f, 0.5f, -6f, dirV, 0.5f);
                // The grand slam's shockwave and its echo reach well below the bat: overlay rings the field can't slice flat.
                if (fin)
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
            if (fin) Host.PlaySfx("fanfare", _now + 0.1);

            var f = Feel.ForCombo(c.Verdict, c.Combo);
            if (!c.IsDemo)
            {
                float hs = Mathf.Clamp(f.Hitstop * (big ? 1.9f : 1f) * (fin ? 2.6f : 1f), 0.04f, 0.06f);   // 40-60 ms
                // The freeze ends well before the next unjudged note arrives, and is visual only.
                var dpHit = p as DownbeatPitch;
                Look.Hitstop(Mathf.Max(0, Mathf.Min(hs, dpHit != null ? (float)(_round.Clock.SpbAt(dpHit.Time) * 0.4) : hs)));   // ends well before the next pitch lands
                rig.PunchZoom(1 + (big ? 0.12f : 0.05f) * (fin ? 1.5f : 1f));   // the camera kick (softened by reduced motion)
                if (big) _camLift = 1.5f;   // and the camera rises to follow the home run's arc for a beat
            }
            _camPush = big ? 1 : 0.45f;

            // --- callout: a badge, tier word big, timing grade small -----------------------------
            string grade = c.Verdict == Verdict.Perfect ? "PERFECT" : c.Verdict == Verdict.Great ? "GREAT" : "GOOD";
            string word = c.IsDemo ? "LIKE THIS!" : c.Foul ? "FOUL!" : t.Label;
            bool free = p is DownbeatPitch fp && fp.IsFree;   // in the free bars it counts for nothing
            string sub = c.IsDemo ? null : grade + (c.HomerFeet > 0 ? $" \u00B7 {c.HomerFeet} FT" : "") + (free ? " \u00B7 FREE" : "");
            float life = fin ? 2.4f : c.IsDemo ? 0.7f : Mathf.Min(big ? 1.25f : 0.95f, Mathf.Max(0.55f, gap * 0.8f));
            // Right of the batter, off the incoming pitch's arc (it comes down from the machine on the left).
            Look.Fx.ClearBadges();
            Look.Fx.Callout(word, sub, Contact + new Vector3(1.15f, 1.75f, 0), tierColor,
                size: t.Id == TierId.Slam ? 1.75f : big ? 1.05f : 0.8f, life: life, rise: (big ? 1.5f : 0.9f) * (life / (big ? 1.25f : 0.95f)));

            if (fin)
            {
                rig.DoFlash(Feel.FinaleFlash * 0.55f, Hex(0xffd9a0));   // a warm pulse, not a white-out over the batter
                rig.Shake(Feel.FinaleShake, new Vector3(-1, 0.5f, 0));
                fx.Confetti(new Vector3(-1, 8.5f, 12), 120, null, 4, 0.4f, 0.11f, 3.6f);   // a shower over the stands, not on the plate
                _finaleShow = 0;   // and the sky over the stands fills with fireworks and a slow confetti shower
            }
        }

        void OnWhiff(WhiffEvent w)
        {
            SkvLog($"ev={(w.SideRetired ? "retired" : "strike")} outs={w.Outs} swung={(w.Swung ? 1 : 0)}");
            Strike(w.Pitch, w.Outs, w.SideRetired, w.Swung, w.Swung ? "WHIFF!" : "STRIKE!", w.Swung ? null : "LOOKING - OUT " + w.Outs);
        }

        /// A strike, whatever its cause: a ball let go or a bomb swung at. Batter, lamps, crowd, burst and callout
        /// (one badge: a new one replaces whatever is up).
        void Strike(Pitch pitch, int outs, bool sideRetired, bool swung, string word, string sub = null, bool bomb = false)
        {
            if (swung) StartSwing();
            Cue(sideRetired ? BatterCue.SideRetired : swung ? BatterCue.Whiff : BatterCue.Strike);
            if (_outsClear > 0) { _outsClear = 0; Ui.Outs(0); }   // a new out while SIDE RETIRED still holds the lamps: the new inning starts now
            Ui.Outs(outs);
            Host.PlaySfx("miss", _now);
            _env?.Crowd?.Deflate(sideRetired ? 1.4f : 0.9f);
            if (sideRetired) _env?.Crowd?.Wave(0.55f, 1.4f);
            var fx = Look.Fx;
            // (no pink ring: the out stamps its red X into the OUTS pill, the bomb brings its own burst, and the ball
            // thuds into the net)
            if (sideRetired) Ui.OutsPulse();
            float gap = GapAfter(pitch);
            float life = Mathf.Min(0.9f, Mathf.Max(0.55f, gap * 0.7f));
            fx.ClearBadges();
            if (sideRetired)
            {
                float retire = Mathf.Max(1.2f, Mathf.Min(1.8f, gap * 0.9f));
                _outsClear = retire;
                fx.Callout("SIDE RETIRED!", bomb ? "BOMB! - OUT 3" : "OUT 3", Contact + new Vector3(1.1f, 1.6f, 0), Hex(0xff4d6a), 1.05f, retire, 0.3f);
                Look.Rig.Shake(0.08f, new Vector3(0, -1, 0));
            }
            else fx.Callout(word, sub, Contact + new Vector3(1.0f, 1.3f, 0), bomb ? Hex(0xff6a3a) : swung ? Hex(0xff5d8a) : Hex(0xff8a3c), bomb ? 0.9f : 0.8f, life, 0.4f);
            _camPush = 0.25f;
        }

        /// Takes the pitch's ball off the field (it is gone: hit, blown up) and returns where it was.
        Vector3 TakeBall(Pitch p)
        {
            var at = Contact;
            if (_pitchBalls.TryGetValue(p, out var b))
            {
                at = b.Go.transform.position;
                b.Trail?.Release();
                FreeBall(b.Go);
                _pitchBalls.Remove(p);
            }
            ClearPips(p);
            return at;
        }

        /// A bomb swung at: the ball blows up where the bat met it. In the free bars it is only a show (no strike).
        void OnExplode(ExplodeEvent e)
        {
            var at = TakeBall(e.Pitch);
            BombBlast(at, e.Free);
            SkvLog($"ev=explode free={(e.Free ? 1 : 0)} outs={e.Outs} retired={(e.SideRetired ? 1 : 0)}");
            Host.PlaySfx("impact", _now);
            Host.PlaySfx("impact", _now + 0.07, gain: 0.8f);   // the bang lands with the burst, after the crack of the bat
            if (e.Free)
            {
                StartSwing();
                Cue(BatterCue.Whiff);
                _env?.Crowd?.Deflate(0.5f);
                Look.Fx.ClearBadges();
                Look.Fx.Callout("BOMB!", "FREE BAR - NO OUT", Contact + new Vector3(1.0f, 1.3f, 0), Hex(0xffb347), 0.8f, 0.85f, 0.4f);
                return;
            }
            Strike(e.Pitch, e.Outs, e.SideRetired, true, "BOMB!", "OUT " + e.Outs, bomb: true);
        }

        /// A bomb let go: it shows itself past the plate and drops away as a dud (its ball is gone from the flight now).
        void OnFizzle(DownbeatPitch p)
        {
            SkvLog("ev=fizzle");
            if (!_pitchBalls.TryGetValue(p, out var b)) return;
            BombFizzle(b.Go.transform.position);
            b.Trail?.Release();
            FreeBall(b.Go);
            _pitchBalls.Remove(p);
            ClearPips(p);
        }

        /// The capture harness's event lines (only with ?inputlog): when a pitch leaves, what happened to it.
        void SkvLog(string s) { if (Host.InputLogging) Debug.Log("[SKV] " + s); }

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
                // Gone the moment it leaves the frame (or fading out under a new pitch): never two balls on screen.
                bool off = false;
                var cam = Camera.main;
                if (cam && h.T > 0.15f && !h.Slam)
                {
                    var vp = cam.WorldToViewportPoint(h.P);
                    off = vp.z < 0 || vp.x < -0.03f || vp.x > 1.03f || vp.y > 1.03f;
                }
                if (h.Fade > 0)
                {
                    h.Fade += dt;
                    h.Go.transform.localScale = Vector3.one * (0.34f * 0.78f * Mathf.Max(0, 1 - h.Fade / 0.12f));
                    if (h.Fade >= 0.12f) off = true;
                }
                else if (h.T < 0.3f && !h.Dim) h.Go.transform.localScale = Vector3.one * (0.34f * Mathf.Lerp(1, 0.78f, h.T / 0.3f));   // flying off: smaller at once
                else if (h.Dim)
                {
                    // a home run under the next pitch: a small gold comet head on its trail, not a white ball
                    h.DimK = Mathf.Min(1, h.DimK + dt / 0.12f);
                    h.Go.transform.localScale = Vector3.one * (0.34f * Mathf.Lerp(0.78f, 0.4f, h.DimK));
                }
                if (off && h.T < h.Life)
                {
                    h.Go.transform.localScale = Vector3.one * 0.34f;
                    h.Trail?.Release();
                    FreeBall(h.Go);
                    _hitBalls.RemoveAt(i);
                    continue;
                }
                if (h.T >= h.Life)
                {
                    if (h.Land)
                    {
                        // Caught in the stands: a pop of confetti.
                        Look.Fx.Confetti(h.P + Vector3.up * 0.3f, 36, null, 5, 1, 0.11f);
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
            // Over the skyline behind the arches, not over the stands.
            var at = new Vector3(Mathf.Clamp(landing.x * 0.8f, -12, 12), 10.6f + (_fireworks % 3) * 0.9f, 27 + (_fireworks % 2) * 3);
            var col = Hex(Feel.Party[_fireworks % Feel.Party.Length]);
            var col2 = Hex(Feel.Party[(_fireworks + 2) % Feel.Party.Length]);
            _fireworks++;
            fx.Shell(at, col.linear * 1.8f, col2.linear * 1.8f, 64, 15f, 0.7f);
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
                var at = new Vector3(-12 + (i * 6.7f) % 24, 10.4f + (i % 3) * 1.0f, 26 + (i % 2) * 4);
                var col = Hex(Feel.Party[i % Feel.Party.Length]);
                var col2 = Hex(Feel.Party[(i + 2) % Feel.Party.Length]);
                var fx = Look.Fx;
                fx.Shell(at, col.linear * 1.8f, col2.linear * 1.8f, 70, 15.5f, 0.72f);
                if (i % 2 == 0) fx.Confetti(new Vector3(-6 + (i * 4.1f) % 12, 9.5f, 12 + (i % 3) * 2), 26, null, 2.5f, 0.3f, 0.11f, 3.4f);
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
            foreach (var c in _clips) if (c != null) c.UnloadAudioData();
            Look.Fx?.ConfettiFadeOff();
            if (_traceMesh) Destroy(_traceMesh);
            DestroyBombs();
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
                var live = _round.LivePitch() as DownbeatPitch;
                if (live != null)
                {
                    // every pitch coils the batter alike: the coil must not give the downbeat away
                    double toGo = live.Time - _now;
                    coiled = toGo < _round.Clock.SpbAt(_now) * 1.5 && toGo > -0.2;
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
