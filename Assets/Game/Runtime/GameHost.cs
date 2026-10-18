// The host: the one clock, the one input map, the audio-unlock gate, the
// menus, pause, the results screen and routing between minigames. The web
// build split this across main.js and shell/ (title, freeplay, play host,
// pause, results, state); here it is one MonoBehaviour in the one scene.
//
// Minigames are components the host adds to a fresh child GameObject and
// destroys when the round ends (see Minigame.cs for the contract).
//
// Autoplay (for verification): `?autoplay` in the WebGL URL, `-autoplay` on
// the command line, or BONANZA_AUTOPLAY=1 in the Editor process. Which game:
// `?game=<id>` / `-game <id>` / BONANZA_GAME (default swing-kings). Autoplay
// presses every TestChart() entry through the same Press() path and logs
// "[RESULT] game=<id> ..." at the end. `?autoplay&party` plays a whole party
// with the default roster, logging "[PARTY] ..." per round and
// "[PARTY-DONE] ..." at the end.
//
// URL parameters (Core/UrlParams.cs parses them; the web main.js ones keep their names): `?scene=<id>`
// (title, freeplay|menu, roster, party, options, a game id, play&game=<id>), `?game=<id>`, `?quality=`,
// `?scale=<n>` (render scale 0.5-2), `?perf` (the perf HUD; the ` key toggles it), `?swingInput=tap|mouse|camera`,
// `?seed=<n>`. An unknown scene or game id logs one "[ERROR] unknown ..." line and lands on the title.
//
// Faults: whatever a game throws (Load, Begin, a press, Tick, Result, the HUD, its own Update) is caught here.
// The host logs exactly one "[ERROR] ..." line (a console warning, not an error: the gates read the line itself),
// keeps it in LastError, destroys the half-built game and goes to the title. If no game was involved (the
// title or another shell screen failed) it stops on a plain error text instead of looping (Mode.Dead).
// `?game=fault` (only with `?autoplay` or `?debug`) is a game whose Load throws, for Tools/fault-check.mjs.

using System;
using System.Collections.Generic;
using System.Linq;
using Bonanza.Core;
using Bonanza.HudKit;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Bonanza
{
    [RequireComponent(typeof(AudioClock))]
    public sealed class GameHost : MonoBehaviour
    {
        // ------------------------------------------------------------ registry

        public sealed class Entry
        {
            public string Id, Title, Blurb;
            public Type Type;
        }

        /// Every playable minigame, in menu order (the web registry order: tools that walk it rely on it). A game not listed here is
        /// unreachable, including by the autoplay check.
        public static readonly List<Entry> Games = new List<Entry>
        {
            new Entry { Id = "swing-kings", Title = "SWING KINGS", Blurb = "One swing. One beat. Send it.", Type = typeof(SwingKingsGame) },
            new Entry { Id = "drumline-dash", Title = "DRUMLINE DASH", Blurb = "Hear it. Hit it back.", Type = typeof(DrumlineDashGame) },
            new Entry { Id = "bounce-brigade", Title = "BOUNCE BRIGADE", Blurb = "The platforms are the tune.", Type = typeof(BounceBrigadeGame) },
            new Entry { Id = "chomp-chorus", Title = "CHOMP CHORUS", Blurb = "Four singers, one of them is you.", Type = typeof(ChompChorusGame) },
            new Entry { Id = "finale-fever", Title = "FINALE FEVER", Blurb = "It only gets faster.", Type = typeof(FinaleFeverGame) },
        };

        public static Entry Find(string id) => Games.Find(e => e.Id == id);

        /// Test-only: never in Games (so never in the menu or a party); reachable only by `?game=fault`.
        static readonly Entry FaultEntry = new Entry { Id = "fault", Title = "FAULT", Blurb = "Test only: Load throws.", Type = typeof(FaultGame) };

        static Entry EntryFor(string id) => Find(id) ?? (id == "fault" ? FaultEntry : null);

        /// The last fault the host caught (null if none). Web `lastError`.
        public string LastError { get; private set; }

        // ------------------------------------------------------------ state

        /// URP Lit, referenced from the scene so the build keeps its shader.
        [SerializeField] Material greybox;
        public Material Greybox => greybox;

        enum Mode { Gate, Title, Menu, Options, Roster, PartyHub, Rules, Playing, Paused, Results, Dead }

        AudioClock _clock;
        Mode _mode = Mode.Gate;
        public bool Autoplay { get; private set; }

        /// The canvas HUD (uGUI + TextMeshPro): score, banners, countdown, results card. Games that set
        /// Minigame.UseCanvasHud draw on it; the shell screens and the other games still use IMGUI.
        public GameHud Ui { get; private set; }
        SceneTarget _boot = new SceneTarget(SceneKind.Title);   // where the gate leads (`?scene=` / `?game=`)
        bool _debug;                                            // `?autoplay` or `?debug`: test-only ids are honoured
        uint? _seedOverride;                                    // `?seed=<n>`
        bool _autoParty;
        int _partySeed = -1;   // `party=<n>`: seed the party as if n had been played before

        // ------------------------------------------------------------ party

        const int PartyLength = 4;   // web DEFAULT_OPTIONS.partyLength
        PartySession _party;
        int _titleIndex, _optIndex;
        /// The open roster screen (null elsewhere), and its rng (web: the scene's makeRng(opts.seed ?? 0x5eed)).
        RosterState _roster;
        Rng _rosterRng;
        /// Free Play's solo pick (web session.players for mode 'free'); null until a roster step ran.
        public IList<PartyPlayer> FreeCast { get; private set; }
        bool _confirmReset;
        float _autoWait;
        bool _perfTip;

        /// The party's lineup while a party runs; null in free play.
        public IList<PartyPlayer> Lineup => _party?.Players;
        Minigame _game;
        GameObject _gameGo;   // the game's root, set before its components exist so a failed build can be torn down
        string _phase = "";   // what the host was doing when a fault hit: load, begin, tick, result, finish, gui
        Entry _entry;
        /// How the open round was reached: "freeplay" (the list, or a direct `?game=`) or "party".
        string _from;
        Outcome _outcome;
        bool _newScore, _newRank;
        int _menuIndex;
        double _pausedTotal, _pausedAt;
        int _pauseIndex;

        readonly List<KeyValuePair<string, double>> _autoChart = new List<KeyValuePair<string, double>>();
        int _autoCursor;

        // ------------------------------------------------------------ input

        enum PressKind { Down, Up, Move }
        struct PressEvent { public string Action; public double Audio; public PressKind Kind; public float Y; }
        readonly Queue<PressEvent> _presses = new Queue<PressEvent>();
        readonly List<InputAction> _actions = new List<InputAction>();
        public static readonly string[] Actions = InputMap.GameActions;

        // What is down right now (web Input._held). The pointer button counts as "a".
        readonly HeldActions _held = new HeldActions();
        readonly StickDpad _stick = new StickDpad();
        bool _pointerDown;
        float _pointerY;
        bool _inputLog;
        public bool InputLogging => _inputLog;
        int _blurSeen;

        /// True while the action is down (keys, pad, pointer). Cleared by window blur.
        public bool IsHeld(string action) => _held.IsHeld(action);
        /// True while the pointer button (mouse, touch) is down.
        public bool PointerHeld => _pointerDown;
        /// The pointer's height at its last sample: 0 at the top of the screen, 1 at the bottom (web clientY / innerHeight).
        public float PointerY => _pointerY;

        static string HeldName(string action) => action == "click" ? "a" : action;

        void InputLog(string what, string action, double audio)
        {
            if (_inputLog) Debug.Log($"[INPUT] {what} {action} t={audio:F4}");
        }

        void InputDown(string action, double audio)
        {
            _held.Down(HeldName(action));
            if (action == "click")
            {
                _pointerDown = true;
                var ptr = Pointer.current;
                if (ptr != null) PointerSample(ptr.position.ReadValue().y, audio);
            }
            _presses.Enqueue(new PressEvent { Action = action, Audio = audio, Kind = PressKind.Down });
            InputLog("down", action, audio);
        }

        void InputUp(string action, double audio, string why = "up")
        {
            if (action == "pause") return;
            if (!_held.Up(HeldName(action))) return;   // already released (blur, or a second key of the same action)
            if (action == "click") _pointerDown = false;
            _presses.Enqueue(new PressEvent { Action = HeldName(action), Audio = audio, Kind = PressKind.Up });
            InputLog(why, action, audio);
        }

        void PointerSample(float screenY, double audio)
        {
            _pointerY = Screen.height > 0 ? Mathf.Clamp01(1f - screenY / Screen.height) : 0;
            _presses.Enqueue(new PressEvent { Action = "move", Audio = audio, Kind = PressKind.Move, Y = _pointerY });
            if (_inputLog) Debug.Log($"[INPUT] move y={_pointerY:F3} t={audio:F4}");
        }

        /// Window blur: release everything held, or the player comes back to a stuck key.
        void ReleaseAll()
        {
            double t = _clock.Now;
            foreach (var a in _held.ReleaseAll())
            {
                _presses.Enqueue(new PressEvent { Action = a, Audio = t, Kind = PressKind.Up });
                InputLog("blur", a, t);
            }
            _pointerDown = false;
            _stick.Reset();
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus) ReleaseAll();
        }

        /// One physical control -> one action. The callback's timestamp is when the press happened, not the
        /// frame it was noticed on. Key auto-repeat never reaches a Button action.
        void BindControl(string action, string path)
        {
            var act = new InputAction(action, InputActionType.Button);
            act.AddBinding(path);
            act.performed += ctx => InputDown(action, _clock.ToAudioTime(ctx.time));
            act.canceled += ctx => InputUp(action, _clock.ToAudioTime(ctx.time));
            act.Enable();
            _actions.Add(act);
        }

        // A web KeyboardEvent.code as an Input System path: KeyZ -> z, ArrowLeft -> leftArrow, Space -> space.
        static string KeyPath(string code)
        {
            if (code.StartsWith("Key")) return "<Keyboard>/" + char.ToLowerInvariant(code[3]);
            if (code.StartsWith("Arrow")) return "<Keyboard>/" + char.ToLowerInvariant(code[5]) + code.Substring(6) + "Arrow";
            return "<Keyboard>/" + char.ToLowerInvariant(code[0]) + code.Substring(1);
        }

        static string PadPath(int button)
        {
            switch (button)
            {
                case 0: return "<Gamepad>/buttonSouth";
                case 1: return "<Gamepad>/buttonEast";
                case 2: return "<Gamepad>/buttonWest";
                case 3: return "<Gamepad>/buttonNorth";
                case 9: return "<Gamepad>/start";
                case 12: return "<Gamepad>/dpad/up";
                case 13: return "<Gamepad>/dpad/down";
                case 14: return "<Gamepad>/dpad/left";
                case 15: return "<Gamepad>/dpad/right";
            }
            return null;
        }

        void BindInput()
        {
            foreach (var kv in InputMap.Keys) BindControl(kv.Value, KeyPath(kv.Key));
            foreach (var kv in InputMap.PadButtons) BindControl(kv.Value, PadPath(kv.Key));
            BindControl("click", "<Mouse>/leftButton");
            BindControl("click", "<Touchscreen>/primaryTouch/press");
            // Pointer height while the button is held.
            var move = new InputAction("pointer", InputActionType.PassThrough);
            move.AddBinding("<Pointer>/position");
            move.performed += ctx =>
            {
                if (_pointerDown) PointerSample(ctx.ReadValue<Vector2>().y, _clock.ToAudioTime(ctx.time));
            };
            move.Enable();
            _actions.Add(move);
            // The perf HUD toggle (web main.js: the ` key). Not a game action.
            var perf = new InputAction("perf", InputActionType.Button);
            perf.AddBinding("<Keyboard>/backquote");
            perf.performed += ctx => TogglePerf();
            perf.Enable();
            _actions.Add(perf);
        }

        /// Gamepad state has no event timestamps worth trusting: the stick is read on the frame (web pollGamepads).
        void PollPad()
        {
            var pad = Gamepad.current;
            if (pad == null) return;
            var v = pad.leftStick.ReadValue();
            double t = _clock.ToAudioTime(InputState.currentTime);
            _stick.Update(v.x, -v.y, (a, down) => { if (down) InputDown(a, t); else InputUp(a, t); });
        }

        // ------------------------------------------------------------ clock

        /// Game time: audio time minus time spent paused. Everything a game
        /// judges or schedules is in this domain.
        public double Now => _mode == Mode.Paused ? _pausedAt - _pausedTotal : _clock.Now - _pausedTotal;
        public bool ClockRunning => _clock.Running;

        /// dspTime for a game-time instant, for AudioSource.PlayScheduled.
        /// Game time is the HEARD axis (latency-compensated, like the web): a sound scheduled at dsp time T is
        /// heard at T plus the device latency, which is when the game clock reads T. So game time g is scheduled
        /// at dsp g (+ pauses), and the latency must not enter here: it uses the raw, uncompensated clock.
        public double DspAt(double gameTime) => gameTime + _pausedTotal - _clock.RawNow + _clock.DspNow;

        /// Game time on the dsp axis (not latency-compensated): where the mixer is. File positions
        /// (AudioSource.timeSamples) are read on this axis.
        public double NowRaw => Now + _clock.Latency;

        // ------------------------------------------------------------ sound

        AudioSource[] _voices;
        float _sfxVol = Settings.DefaultSfx;

        void OnSettingsChanged() => _sfxVol = Settings.Sfx;
        int _voiceNext;

        /// Play a clip at game time `t` (immediately if t is past).
        public void PlayAt(AudioClip clip, double t, float volume = 0.35f, float pitch = 1, float pan = 0)
        {
            var src = _voices[_voiceNext++ % _voices.Length];
            src.Stop();
            src.clip = clip;
            src.volume = volume * _sfxVol;
            src.pitch = pitch;
            src.panStereo = pan;
            double dsp = DspAt(t);
            if (dsp > _clock.DspNow + 0.005) src.PlayScheduled(dsp);
            else src.Play();
        }

        public void Play(AudioClip clip, float volume = 0.35f, float pitch = 1, float pan = 0) => PlayAt(clip, 0, volume, pitch, pan);

        // The web's SFX kit (audio/index.js SFX), baked per chord (Sfx.cs). Every web SFX reads the chord the music sits on.
        Sfx _sfx;

        /// A web SFX in the round's key: `sfx` is perfect/great/good/miss/impact/count/combo/fanfare/coin/tick/swoosh,
        /// `t` the game time it sounds (immediately when past), `arg` the count beat or combo level, `gain` the
        /// web's per-call gain (tick only). The chord is the one the round's music is on when called.
        public void PlaySfx(string sfx, double t, int arg = 0, float gain = 1, float pan = 0)
        {
            if (_sfx == null || _game == null || _entry == null) return;
            var h = Harmony.ForContext(_entry.Id);
            // The web reads the chord at the moment of the call (key()), not at the scheduled time, so a cluster of sounds shares one chord.
            int deg = h == null ? 0 : h.ChordDegAt(h.BarAt(_game.MusicBeatAt(Now)));
            _sfx.Play(_entry.Id, sfx, deg, t, arg, gain, pan);
        }

        /// A game's own instrument voice, baked from the web (VoiceCatalog names the clips, VoiceMix the multipliers),
        /// at game time `t`. Not chord-locked here: the game picks the clip for the chord it wants.
        public void PlayVoice(string clip, double t, float gain = 1)
        {
            if (_sfx != null) _sfx.PlayVoice(clip, t, gain);
        }

        /// A menu or results sound on the title / results music's chord (web shell sfx(ctx, name)). With no shell
        /// music running the web falls back to a C major key: the title's first chord here.
        public void ShellSfx(string sfx, double delay = 0)
        {
            if (_sfx == null) return;
            string ctx = "title"; int deg = 0;
            if (_music.ShellClock(out var id, out double zero, out double spb))
            {
                var h = Harmony.ForContext(id);
                if (h != null && SfxCatalog.Supports(id, sfx)) { ctx = id; deg = h.ChordDegAt(h.BarAt((Now - zero) / spb)); }
            }
            _sfx.Play(ctx, sfx, deg, Now + delay, whilePaused: _mode == Mode.Paused);
        }

        /// The shared count-in tick (web audio sfx 'count'): chord-locked to the game's key. accent 0 is the downbeat.
        public void CountTick(double t, int accent) => PlaySfx("count", t, accent);

        /// What the title screen shows (reel, beat); the host feeds it, the drawing reads it.
        public TitleState Title { get; } = new TitleState(Games.Select(g => g.Id).ToList());
        bool _titleWas;

        /// The title's kick on every beat and its tick on every fourth (web title.js onBeat), scheduled ahead on the
        /// title music's own grid, and the beat the logo bounces on.
        long _beatDone = long.MinValue;
        void TitleBeats()
        {
            if (!_music.ShellClock(out var id, out double zero, out double spb) || id != "title") { Title.SetBeat(0, false); return; }
            double beat = (Now - zero) / spb;
            Title.SetBeat(beat, true);
            long n = (long)System.Math.Floor(beat) + 1;
            double at = zero + n * spb;
            if (n >= 0 && n != _beatDone && at - Now < 0.12 && at > Now)
            {
                _beatDone = n;
                _sfx?.PlayVoice(VoiceCatalog.TitleKick, at, TitleBeat.KickGain(Title.Attract) / TitleBeat.KickReel);
                if (n % TitleBeat.TickEvery == 0) _sfx?.Play("title", "tick", 0, at, 0, 0.12f);
            }
        }

        /// One title frame of the reel: its timer, and the ui sound on each change (web title.js update()).
        void TitleReelFrame()
        {
            switch (Title.Reel.Update(Time.unscaledDeltaTime, false))
            {
                case ReelEvent.Entered: ShellSfx("ui"); Debug.Log($"[SHELL] reel=enter game={Title.CurrentGameId}"); break;
                case ReelEvent.Stepped: ShellSfx("ui"); Debug.Log($"[SHELL] reel=step game={Title.CurrentGameId} step={Title.Reel.Step}"); break;
            }
        }

        /// A title press through the reel; true when the reel took it (woke it, or started it).
        bool TitleReelPress(string action)
        {
            switch (Title.Reel.Press(action, false))
            {
                case ReelPress.Left: ShellSfx("uiBack"); Debug.Log("[SHELL] reel=leave"); return true;
                case ReelPress.Entered: ShellSfx("ui"); Debug.Log($"[SHELL] reel=enter game={Title.CurrentGameId}"); return true;
            }
            return false;
        }

        // ------------------------------------------------------------ music

        MusicPlayer _music;

        /// A judged note by the human player, for the adaptive music level.
        public void MusicJudge(bool hit) => _music.OnJudge(hit);

        /// Duck the music under something (1 = full): the drumline call.
        public void MusicDuck(float v) => _music.SetDuck(v);

        /// Fade the music out (Chomp Chorus ends its track under the last note).
        public void MusicStop(float fade) => _music.Stop(fade);

        /// The title theme runs under every menu screen and the party hub.
        void EnsureTitleMusic()
        {
            if (_music.Current != "title") _music.PlayShell("title");
        }

        // ------------------------------------------------------------ setup

        static readonly Dictionary<PrimitiveType, Mesh> _meshes = new Dictionary<PrimitiveType, Mesh>();

        /// Built-in meshes, not GameObject.CreatePrimitive: that adds a collider,
        /// and the WebGL build strips the unused physics module.
        public static Mesh BuiltinMesh(PrimitiveType t)
        {
            if (!_meshes.TryGetValue(t, out var mesh))
            {
                string file = t switch
                {
                    PrimitiveType.Cube => "Cube.fbx",
                    PrimitiveType.Sphere => "New-Sphere.fbx",
                    PrimitiveType.Capsule => "New-Capsule.fbx",
                    PrimitiveType.Plane => "New-Plane.fbx",
                    PrimitiveType.Cylinder => "New-Cylinder.fbx",
                    _ => "Quad.fbx",
                };
                _meshes[t] = mesh = Resources.GetBuiltinResource<Mesh>(file);
            }
            return mesh;
        }

        void Awake()
        {
            Application.targetFrameRate = 60;
#if UNITY_EDITOR
            // The CLI drives an unfocused Editor; without this Play Mode stalls.
            Application.runInBackground = true;
#endif
            _clock = GetComponent<AudioClock>();
            Ui = GameHud.Create(transform);
            var args = Environment.GetCommandLineArgs();
            string url = Application.absoluteURL ?? "";
            var uo = UrlParams.Parse(url);
            Autoplay = uo.Autoplay || Array.IndexOf(args, "-autoplay") >= 0
                       || Environment.GetEnvironmentVariable("BONANZA_AUTOPLAY") == "1";
            _debug = Autoplay || uo.Debug;
            string gameArg = uo.Game;
            int gi = Array.IndexOf(args, "-game");
            if (gameArg == null && gi >= 0 && gi + 1 < args.Length) gameArg = args[gi + 1];
            if (gameArg == null) gameArg = Environment.GetEnvironmentVariable("BONANZA_GAME");
            _seedOverride = uo.Seed;
            // `?autoplay&party`: the bot plays a whole party with the default roster.
            _partySeed = uo.PartySeed;
            _autoParty = uo.Party || Array.IndexOf(args, "-party") >= 0
                         || Environment.GetEnvironmentVariable("BONANZA_PARTY") == "1";
            _boot = ResolveBoot(uo.Scene, gameArg);
            Settings.Migrate();
            AudioListener.volume = 1;   // the music and SFX levels are applied at their own sources
            _sfxVol = Settings.Sfx;
            Settings.Changed += OnSettingsChanged;
            Application.logMessageReceived += OnLogMessage;
            // `?quality=auto|high|medium|low` pins the Graphics setting for this run (test harness), like the web.
            // `?scale=<n>` pins the render scale on top of it.
            GraphicsSetting forced = default;
            bool pin = uo.Quality != null && Enum.TryParse(uo.Quality, true, out forced);
            if (uo.Scale.HasValue) RenderQuality.OverrideScale(uo.Scale.Value);
            RenderQuality.Apply(pin ? forced : Settings.Graphics);
            if (uo.SwingInputName != null && Enum.TryParse(uo.SwingInputName, true, out SwingInput si)) Settings.SwingInputOverride = si;
            if (uo.Perf) { _perf = true; Debug.Log("[PERF] hud=on"); }
            if (uo.Scale.HasValue || uo.SwingInputName != null || uo.Seed.HasValue)
                Debug.Log($"[URL] applied scale={(uo.Scale.HasValue ? RenderQuality.Level.Scale.ToString("0.##") : "-")} swingInput={uo.SwingInputName ?? "-"} seed={(uo.Seed.HasValue ? uo.Seed.Value.ToString() : "-")}");
            if (uo.Ignored.Count > 0) Debug.Log("[URL] ignored: " + string.Join("; ", uo.Ignored));

            _inputLog = uo.InputLog || Environment.GetEnvironmentVariable("BONANZA_INPUTLOG") == "1";
            BindInput();
            BonanzaBlur.Install();
            _blurSeen = BonanzaBlur.Count();
            BonanzaBrowser.Install();
            Settings.CameraInputAvailable = BonanzaCamera.Supported;   // a camera API (getUserMedia) in this browser; false in the Editor
            BonanzaBrowser.Poll();
            // `?latencySlew=fast` (test harness only): refresh the output latency every 0.1 s instead of 1 s,
            // so the 2 ms/refresh slew converges in seconds. The slew itself (deadband, step) is unchanged.
            _latencyEvery = uo.LatencyFast ? 0.1f : 1f;

            _music = gameObject.AddComponent<MusicPlayer>();
            _music.Init(this);
            _music.Prepare("title");
            _music.Prepare("results");

            _voices = new AudioSource[40];   // baked voice tails ring for seconds; a reused source is cut (PlayAt)
            for (int i = 0; i < _voices.Length; i++)
            {
                _voices[i] = gameObject.AddComponent<AudioSource>();
                _voices[i].playOnAwake = false;
            }
            _sfx = new Sfx { Log = _debug };
            _sfx.Init(this, gameObject);
        }

        void OnDestroy()
        {
            Settings.Changed -= OnSettingsChanged;
            Application.logMessageReceived -= OnLogMessage;
            foreach (var a in _actions) a.Dispose();
        }

        /// Where the gate leads: `?scene=` wins over `?game=`; an autoplay run with neither plays swing-kings.
        /// An id nothing registers is reported once and the run lands on the title.
        SceneTarget ResolveBoot(string scene, string game)
        {
            var ids = Games.Select(e => e.Id).ToList();
            if (scene != null)
            {
                var t = SceneIds.Resolve(scene, ids, _debug, game);
                if (t.Kind != SceneKind.Unknown) return t;
                BootError($"unknown scene '{scene}'");
                return new SceneTarget(SceneKind.Title);
            }
            if (game != null)
            {
                string g = SceneIds.ResolveGame(game, ids, _debug);
                if (g != null) return new SceneTarget(SceneKind.Game, g);
                BootError($"unknown game id '{game}'");
                return new SceneTarget(SceneKind.Title);
            }
            return Autoplay && !_autoParty ? new SceneTarget(SceneKind.Game, "swing-kings") : new SceneTarget(SceneKind.Title);
        }

        void BootError(string msg)
        {
            LastError = msg;
            Debug.LogWarning("[ERROR] " + msg);
        }

        /// Show the screen a boot target names.
        void Enter(SceneTarget t)
        {
            switch (t.Kind)
            {
                case SceneKind.FreePlay: GoRoute(new Route(ShellNav.FreePlay)); break;
                case SceneKind.Roster: GoRoute(new Route(ShellNav.Roster, mode: "party")); break;
                case SceneKind.Party: StartParty(); break;
                case SceneKind.Options: GoRoute(new Route(ShellNav.Options)); break;
                case SceneKind.Game: _from = "freeplay"; Open(EntryFor(t.Game)); break;
                default: _mode = Mode.Title; break;
            }
        }

        // ------------------------------------------------------------ routing

        void Open(Entry e)
        {
            CloseGame();
            Ui.Clear();
            _entry = e;
            _outcome = null;
            Debug.Log($"[SHELL] open game={e.Id} from={_from ?? "-"}");
            _phase = "load";
            _gameGo = new GameObject(e.Id);
            _game = (Minigame)_gameGo.AddComponent(e.Type);
            _game.Bind(this, Seed(e.Id));
            _game.Load();
            _music.Prepare(e.Id);   // decode now; the rules card waits for it
            _sfx.Prepare(e.Id);     // ... and for the game's sound effects
            _startRequested = false;
            _rulesWait = 0;
            _mode = Mode.Rules;
        }

        uint Seed(string id) => _seedOverride ?? Party.GameSeed(id);

        void Begin()
        {
            _phase = "begin";
            _pausedTotal = 0;
            _game.Begin(Now + 0.6);
            var g = _game;
            _music.PlayGame(_entry.Id, g.MusicTimeAt(MusicTracks.GameStartBeat),
                () => g.MusicBeatAt(Now) - MusicTracks.GameStartBeat);
            _autoChart.Clear();
            _autoCursor = 0;
            if (Autoplay)
            {
                _autoChart.AddRange(_game.TestChart());
                _autoChart.Sort((x, y) => x.Value.CompareTo(y.Value));
            }
            _mode = Mode.Playing;
        }

        void CloseGame()
        {
            Ui.Clear();
            if (_gameGo != null) Destroy(_gameGo);
            _gameGo = null;
            _game = null;
            AudioListener.pause = false; Time.timeScale = 1;
            foreach (var v in _voices) v.Stop();
            _music.SetDuck(1);
            if (_music.Current != "title") _music.Stop(0.25f);
        }

        void Finish(Outcome o)
        {
            _phase = "finish";
            _outcome = o;
            _mode = Mode.Results;
            _autoWait = 2.5f;
            _resultsT = 0;
            _stung = false;
            _music.PlayShell("results");
            ShellSfx("ui");
            Debug.Log($"[RESULT] game={_entry.Id} {o.LogLine()} autoplay={Autoplay}");
            Save.Record(_entry.Id, o, out _newScore, out _newRank);
            if (_party != null && _party.CurrentGame == _entry.Id)
            {
                _party.RecordRound(_entry.Id, o);
                var r = _party.Scores[_party.Scores.Count - 1];
                Debug.Log($"[PARTY] round={_party.Index}/{_party.Length} game={r.Game} sim={(r.Sim ? 1 : 0)} "
                          + string.Join(" ", r.Players.Select(p => $"{_party.Players[p.Id].Name}={p.Place}:+{p.Points}")));
                if (_party.Done && _party.Winners.Any(id => !_party.Players[id].IsCpu)) Save.AddWin();
                if (_party.Done)
                    Debug.Log($"[PARTY-DONE] winners={string.Join(",", _party.Winners.Select(id => _party.Players[id].Name))} "
                              + string.Join(" ", _party.Players.Select(p => $"{p.Name}={p.Points}/{p.Wins}")));
            }
            ShowResultsCard(o);
        }

        static readonly Color[] SeatColors = { new Color(1f, 0.85f, 0.24f), new Color(0.3f, 0.84f, 1f), new Color(1f, 0.48f, 0.85f), new Color(0.62f, 0.91f, 0.48f) };

        /// The canvas results card (rank stamp, score count-up, stats, party placings); IMGUI keeps only the click buttons.
        void ShowResultsCard(Outcome o)
        {
            Ui.Clear();
            var d = new ResultsData
            {
                Title = _game.Title, Rank = o.Rank, Score = o.Score, Accuracy = (float)o.Accuracy,
                Perfect = o.Perfect, Great = o.Great, Good = o.Good, Miss = o.Miss, MaxCombo = o.MaxCombo,
            };
            foreach (var kv in o.Extras) d.Extras.Add(kv);
            // One sticker, the rank first. A first-ever D is a record, not worth a sticker.
            if ((_newScore || _newRank) && o.Rank != "D" && o.Score > 0) d.Badge = _newRank ? "NEW RANK!" : "NEW BEST!";
            if (_party != null)
            {
                var round = _party.Scores.Count > 0 ? _party.Scores[_party.Scores.Count - 1] : null;
                if (round != null && round.Game == _entry.Id)
                {
                    d.Party = new List<PartyRow>();
                    foreach (var p in round.Players.OrderBy(p => p.Place))
                    {
                        bool tied = round.Players.Count(q => q.Place == p.Place) > 1;
                        var who = _party.Players[p.Id];
                        d.Party.Add(new PartyRow
                        {
                            Place = (tied ? "T-" : "") + PlaceName[p.Place - 1], Name = who.Name, Points = p.Points,
                            Color = SeatColors[p.Id % SeatColors.Length], You = !who.IsCpu,
                        });
                    }
                }
                d.Prompts.Add(new KeyValuePair<string, string>(Autoplay ? "" : "SPACE", Autoplay ? "autoplay" : "standings"));
            }
            else if (Autoplay) d.Prompts.Add(new KeyValuePair<string, string>("", "autoplay"));
            else
            {
                d.Prompts.Add(new KeyValuePair<string, string>("SPACE", "play again"));
                d.Prompts.Add(new KeyValuePair<string, string>("ESC", "menu"));
            }
            Ui.Results.Show(d);
        }

        /// Seat the roster and shuffle the playlist; the finale always closes it.
        void StartParty(List<PartyPlayer> players = null)
        {
            // No roster step (autoplay, `?scene=party`, `?party=<n>`): the default lineup picks for itself.
            players ??= RosterState.Auto("party", new Rng(_seedOverride ?? RosterState.DefaultSeed)).BuildPlayers(Save.HumanName);
            int n = Save.BeginParty();   // the seed counter and the career stat both move on
            if (_partySeed >= 0) n = _partySeed;
            _partySeed = -1;
            var rng = new Rng(unchecked(0x5eedu + (uint)n * 0x9e3779b9u));
            var games = Party.Playlist(Games.Select(e => e.Id).ToList(), PartyLength, rng);
            _party = new PartySession(players, games, (uint)(rng.Next() * 4294967296.0));
            Debug.Log($"[PARTY] start games={string.Join(",", games)} players={string.Join(",", players.Select(p => p.Name))}");
            EnterHub();
        }

        void EnterHub()
        {
            _autoWait = 1.5f;
            _mode = Mode.PartyHub;
            ShellSfx(_party.Done ? "fanfare" : "ui");
            Debug.Log($"[SHELL] hub index={_party.Index} game={_party.CurrentGame ?? "-"}");
        }

        void EndParty()
        {
            _party = null;
            CloseGame();
            _mode = Mode.Title;
        }

        /// Leave a round for wherever the player came from (web nav.js exitRoute). In a party that is
        /// the hub: the round is not recorded, the party goes on and offers the same round again.
        void LeaveRound()
        {
            var r = ShellNav.ExitRoute(_party != null, _from, _entry?.Id);
            Debug.Log($"[SHELL] leave-round game={_entry?.Id} -> {r.View} party-index={(_party != null ? _party.Index.ToString() : "-")}");
            CloseGame();
            _presses.Clear();
            GoRoute(r);
        }

        /// Show the screen a Route names.
        void GoRoute(Route r)
        {
            switch (r.View)
            {
                case ShellNav.PartyHub: EnterHub(); break;
                case ShellNav.FreePlay:
                    if (r.Game != null) { int i = Games.FindIndex(e => e.Id == r.Game); if (i >= 0) _menuIndex = i; }
                    _mode = Mode.Menu;
                    break;
                case ShellNav.Roster:
                    _roster = new RosterState(r.Mode);
                    _rosterRng = new Rng(_seedOverride ?? RosterState.DefaultSeed);
                    _rosterFlash = null;
                    _mode = Mode.Roster;
                    Debug.Log($"[SHELL] roster mode={_roster.Mode} stage={_roster.Stage.ToString().ToLowerInvariant()}");
                    break;
                case ShellNav.Options: _optIndex = 0; _confirmReset = false; _mode = Mode.Options; break;
                default: _mode = Mode.Title; break;
            }
        }

        void Pause()
        {
            _pausedAt = _clock.Now;
            _mode = Mode.Paused;
            Debug.Log($"[SHELL] pause game={_entry.Id}");
            _pauseIndex = 0;
            _resumeLeft = 0;
            AudioListener.pause = true; Time.timeScale = 0;   // freezes visual-only Update() animation
            ShellSfx("uiBack");
            _game.OnPause();
            _music.OnPause();
        }

        /// Beats of count-in after a pause (web play.js RESUME_BEATS): three is
        /// the shortest that reads as a count.
        const int ResumeBeats = 3;
        float _resumeLeft, _resumeSpb;

        void CountBackIn()
        {
            _resumeSpb = (float)_game.SecondsPerBeat;
            _resumeLeft = ResumeBeats * _resumeSpb;
        }

        void Resume()
        {
            _resumeLeft = 0;
            AudioListener.pause = false; Time.timeScale = 1;
            _clock.Rebase();
            _pausedTotal += _clock.Now - _pausedAt;
            _mode = Mode.Playing;
            Debug.Log($"[SHELL] resume game={_entry.Id}");
            _game.OnResume();
            _music.OnResume();
        }

        // ------------------------------------------------------------ loop

        // ------------------------------------------------------------ browser bridge

        float _latencyEvery = 1f, _nextLatencyAt;
        double _loggedLatency = -1, _loggedTarget = -1;
        bool _audioSeenRunning, _audioLost;
        float _audioLostSince = -1;

        /// Output latency once a second (web main.js setInterval; only while the context runs), the OS
        /// reduced-motion flag, and the audio-context watch.
        void UpdateBrowser()
        {
            if (BonanzaBrowser.Poll()) Settings.NotifyChanged();
            if (!BonanzaBrowser.Available) return;

            var st = BonanzaBrowser.AudioState();
            float t = Time.unscaledTime;
            if (st == BrowserAudioState.Running)
            {
                _audioSeenRunning = true;
                _audioLostSince = -1;
                if (_audioLost) { _audioLost = false; Debug.Log("[AUDIO] resumed"); }
            }
            else if (_audioSeenRunning && st != BrowserAudioState.Unknown && !_audioLost)
            {
                // Unlocked once and not running now: the tab went to sleep or the OS took the output
                // (an iOS call, a Bluetooth switch). Audio stays dead until a gesture resumes it, so say so.
                if (_audioLostSince < 0) _audioLostSince = t;
                else if (t - _audioLostSince > 0.3f)
                {
                    _audioLost = true;
                    Debug.Log("[AUDIO] lost state=" + st);
                    if (_mode == Mode.Playing) Pause();   // the clock stalls with the context; do not play on
                }
            }

            if (st == BrowserAudioState.Running && t >= _nextLatencyAt)
            {
                _nextLatencyAt = t + _latencyEvery;
                double target = LatencySlew.Resolve(BonanzaBrowser.OutputLatency(), BonanzaBrowser.BaseLatency());
                double v = _clock.RefreshLatency(target, _mode == Mode.Playing);
                if (v != _loggedLatency || target != _loggedTarget)
                {
                    _loggedLatency = v; _loggedTarget = target;
                    Debug.Log($"[LATENCY] applied={v * 1000:F1}ms target={target * 1000:F1}ms out={BonanzaBrowser.OutputLatency() * 1000:F1}ms base={BonanzaBrowser.BaseLatency() * 1000:F1}ms ctx={BonanzaBrowser.AudioSource()}");
                }
            }
        }

        void Update()
        {
            if (_mode == Mode.Dead) { _presses.Clear(); return; }
            try
            {
                if (_externalFault != null)
                {
                    // A game's own Update / LateUpdate threw (Unity logged it; we only see the log).
                    string msg = _externalFault;
                    _externalFault = null;
                    if (_gameGo != null) { Fault("update", msg, null); return; }
                }
                UpdateMode();
                PerfSample();
                LogScreen();
            }
            catch (Exception e) { Fault(_phase, e); }
        }

        void UpdateMode()
        {
            UpdateBrowser();
            if (_audioLost) _presses.Clear();   // the gesture that resumes the audio is not a menu or game press
            PollPad();
            int blurs = BonanzaBlur.Count();
            if (blurs != _blurSeen) { _blurSeen = blurs; ReleaseAll(); }
            // Only a round wants releases and pointer samples; every screen takes presses.
            if (_mode != Mode.Playing) DropNonPresses();

            if (_mode != Mode.Title) _titleWas = false;
            switch (_mode)
            {
                case Mode.Gate:
                    // WebGL audio unlocks on the first press; nothing starts
                    // before dspTime is moving.
                    if (_presses.Count > 0) _gatePressed = true;
                    _presses.Clear();
                    if ((_gatePressed || Autoplay) && _clock.Running)
                    {
                        if (_autoParty) StartParty();
                        else Enter(_boot);
                    }
                    break;

                case Mode.Title:
                    if (!_titleWas) { _titleWas = true; Title.Reset(); _beatDone = long.MinValue; }
                    if (!_perfTip && PerfHint.ShouldShow(Settings.Store.GetString(RenderQuality.KeyPerfHint, "")))
                    {
                        // Shown once: the title marks it shown as it raises it (web title.js).
                        Settings.Store.SetString(RenderQuality.KeyPerfHint, PerfHint.Shown); Settings.Store.Flush();
                        _perfTip = true;
                    }
                    while (_presses.Count > 0)
                    {
                        var p = _presses.Dequeue();
                        bool tip = _perfTip;
                        _perfTip = false;                              // any key closes the tip, and is not also a menu press
                        if (TitleReelPress(p.Action) || tip) continue; // a press that wakes the reel is consumed, never a menu press
                        if (p.Action == "up" || p.Action == "left") { _titleIndex = (_titleIndex + TitleItems.Length - 1) % TitleItems.Length; ShellSfx("ui"); }
                        else if (p.Action == "down" || p.Action == "right") { _titleIndex = (_titleIndex + 1) % TitleItems.Length; ShellSfx("ui"); }
                        else if (p.Action == "a") { TitleChoice(_titleIndex); break; }
                    }
                    _presses.Clear();
                    if (_mode == Mode.Title) { TitleReelFrame(); TitleBeats(); }
                    break;

                case Mode.Menu:
                    while (_presses.Count > 0)
                    {
                        var p = _presses.Dequeue();
                        if (p.Action == "up" || p.Action == "left") MenuStep(-1);
                        else if (p.Action == "down" || p.Action == "right") MenuStep(1);
                        else if (p.Action == "a") { _from = "freeplay"; ShellSfx("fanfare"); Open(Games[_menuIndex]); break; }
                        else if (InputMap.IsBack(p.Action)) { ShellSfx("uiBack"); _mode = Mode.Title; break; }
                    }
                    _presses.Clear();
                    break;

                case Mode.Options:
                    while (_presses.Count > 0)
                    {
                        var p = _presses.Dequeue();
                        if (p.Action == "click") continue;
                        if (_confirmReset)
                        {
                            // Anything but SPACE cancels.
                            if (p.Action == "a") { Save.ResetProgress(Games.Select(e => e.Id)); ShellSfx("fanfare"); }
                            else ShellSfx("uiBack");
                            _confirmReset = false;
                            continue;
                        }
                        if (p.Action == "up") { _optIndex = (_optIndex + OptionRows.Length - 1) % OptionRows.Length; ShellSfx("ui"); }
                        else if (p.Action == "down") { _optIndex = (_optIndex + 1) % OptionRows.Length; ShellSfx("ui"); }
                        else if (p.Action == "left" || p.Action == "right") AdjustOption(_optIndex, p.Action == "right" ? 1 : -1);
                        else if (p.Action == "a") OptionChoice(_optIndex);
                        else if (InputMap.IsBack(p.Action)) { ShellSfx("uiBack"); _mode = Mode.Title; break; }
                    }
                    _presses.Clear();
                    break;

                case Mode.Roster:
                    while (_presses.Count > 0 && _mode == Mode.Roster)
                    {
                        var p = _presses.Dequeue();
                        string act = InputMap.IsBack(p.Action) ? "b" : p.Action;
                        RosterPress(act);
                    }
                    _presses.Clear();
                    if (_mode == Mode.Roster && _roster != null) ApplyRoster(_roster.Update(Time.unscaledDeltaTime, _rosterRng));
                    if (_rosterFlash != null && (_rosterFlashT -= Time.unscaledDeltaTime) <= 0) _rosterFlash = null;
                    if (_rosterShakeT > 0) _rosterShakeT -= Time.unscaledDeltaTime;
                    break;

                case Mode.PartyHub:
                    bool next = Autoplay && (_autoWait -= Time.deltaTime) <= 0;
                    while (_presses.Count > 0)
                    {
                        var p = _presses.Dequeue();
                        if (Autoplay) continue;
                        if (p.Action == "a") next = true;
                        else if (InputMap.IsBack(p.Action)) { ShellSfx("uiBack"); EndParty(); break; }
                    }
                    _presses.Clear();
                    if (next && _mode == Mode.PartyHub)
                    {
                        if (!Autoplay) ShellSfx("fanfare");
                        var go = ShellNav.PartyConfirmRoute(_party.Done, _party.CurrentGame);
                        if (go.View == ShellNav.Title) { if (!Autoplay) EndParty(); }
                        else { _from = go.From; Open(Find(go.Game)); }
                    }
                    break;

                case Mode.Rules:
                    _startRequested |= Autoplay;
                    while (_presses.Count > 0)
                    {
                        var p = _presses.Dequeue();
                        if (InputMap.IsBack(p.Action)) { LeaveRound(); break; }
                        _startRequested = true;
                    }
                    _presses.Clear();
                    if (_startRequested && _mode == Mode.Rules && _clock.Running)
                    {
                        // The round's music has to be decoded before it starts: the
                        // count-in is two bars of it.
                        _rulesWait += Time.unscaledDeltaTime;
                        bool ready = _music.Ready(_entry.Id) && _sfx.Ready;
                        if (!ready && _rulesWait < MusicLoadTimeout) break;
                        if (!ready) Debug.LogError($"[MUSIC] {_entry.Id} not decoded after {MusicLoadTimeout}s; starting without it");
                        Begin();
                    }
                    break;

                case Mode.Playing:
                    TickGame();
                    break;

                case Mode.Paused:
                    if (_resumeLeft > 0)
                    {
                        // Counting back in: the round stays frozen until the count ends.
                        _resumeLeft -= Time.unscaledDeltaTime;
                        bool reopen = false;
                        while (_presses.Count > 0) if (InputMap.IsBack(_presses.Dequeue().Action)) reopen = true;
                        if (reopen) _resumeLeft = 0;
                        else if (_resumeLeft <= 0) Resume();
                        break;
                    }
                    while (_presses.Count > 0)
                    {
                        var p = _presses.Dequeue();
                        if (InputMap.IsBack(p.Action)) { ShellSfx("uiBack"); CountBackIn(); break; }
                        int np = PauseItems.Length;
                        if (p.Action == "up") { _pauseIndex = (_pauseIndex + np - 1) % np; ShellSfx("ui"); }
                        if (p.Action == "down") { _pauseIndex = (_pauseIndex + 1) % np; ShellSfx("ui"); }
                        if (p.Action == "a") { PauseChoice(_pauseIndex); break; }
                    }
                    _presses.Clear();
                    break;

                case Mode.Results:
                    _resultsT += Time.unscaledDeltaTime;
                    Ui.Results.Reveal(_resultsT);
                    if (!_stung && _resultsT >= StampAt) { _stung = true; RankSting(_outcome.Rank); }
                    if (_resultsT < ResultsLock)
                    {
                        // A held or late tap from the round must not skip the card.
                        _presses.Clear();
                        break;
                    }
                    if (_party != null)
                    {
                        // A party round has no retry: on to the hub.
                        bool on = Autoplay && (_autoWait -= Time.deltaTime) <= 0;
                        while (_presses.Count > 0)
                        {
                            var p = _presses.Dequeue();
                            if (Autoplay || (p.Action != "a" && !InputMap.IsBack(p.Action))) continue;
                            if (_resultsT < RevealEnd) _resultsT = RevealEnd;   // the first press finishes the reveal
                            else { on = true; ShellSfx("ui"); }
                        }
                        if (on) ToHub();
                        break;
                    }
                    while (_presses.Count > 0)
                    {
                        var p = _presses.Dequeue();
                        if (Autoplay) continue;
                        if (_resultsT < RevealEnd && !InputMap.IsBack(p.Action)) { _resultsT = RevealEnd; continue; }
                        if (p.Action == "a") { ShellSfx("ui"); Open(_entry); break; }                       // again
                        if (InputMap.IsBack(p.Action) || p.Action == "left") { ShellSfx("ui"); LeaveRound(); break; }
                    }
                    _presses.Clear();
                    break;
            }

            if (_mode == Mode.Title || _mode == Mode.Menu || _mode == Mode.Options || _mode == Mode.Roster || _mode == Mode.PartyHub)
                EnsureTitleMusic();
            _music.Tick(_mode == Mode.Paused);
            RenderQuality.Tick(Time.unscaledDeltaTime, GraphicsPhase());
        }

        FramePhase GraphicsPhase()
        {
            switch (_mode)
            {
                case Mode.Gate: return FramePhase.Loading;
                case Mode.Playing: return _clock.Running ? FramePhase.Playing : FramePhase.Paused;
                case Mode.Paused: return FramePhase.Paused;
                default: return FramePhase.Between;
            }
        }

        void DropNonPresses()
        {
            int n = _presses.Count;
            for (int i = 0; i < n; i++)
            {
                var p = _presses.Dequeue();
                if (p.Kind == PressKind.Down) _presses.Enqueue(p);
            }
        }

        bool _gatePressed, _startRequested;
        float _rulesWait;
        const float MusicLoadTimeout = 6;

        void ToHub()
        {
            _presses.Clear();
            CloseGame();
            EnterHub();
        }

        // Pause. RESTART is there in a party too: a game's seed is fixed per game id and the CPU rolls are
        // seeded per party / round index / slot (PartySession.RecordRound), and the index only moves when a
        // round is recorded, so playing it again re-rolls nothing. QUIT leaves the round unrecorded: in a
        // party that is back to the hub with the same round offered again, otherwise the Free Play list.
        string[] PauseItems => _party != null ? new[] { "RESUME", "RESTART", "QUIT TO HUB" } : new[] { "RESUME", "RESTART", "QUIT TO MENU" };

        void PauseChoice(int i)
        {
            var item = PauseItems[i];
            ShellSfx(item == "QUIT TO HUB" || item == "QUIT TO MENU" ? "uiBack" : "ui");   // web pause.js / play.js
            if (item == "RESUME") CountBackIn();
            else if (item == "RESTART")
            {
                var e = _entry;
                Debug.Log($"[SHELL] restart game={e.Id} party-index={(_party != null ? _party.Index.ToString() : "-")}");
                CloseGame(); Open(e);
            }
            else LeaveRound();
        }

        // ------------------------------------------------------------ title, options, roster

        static readonly string[] TitleItems = { "PARTY", "FREE PLAY", "OPTIONS" };
        static readonly string[] TitleIds = { "party", "free", "options" };

        void TitleChoice(int i)
        {
            _titleIndex = i;
            ShellSfx("fanfare");
            var r = ShellNav.TitleMenuRoute(TitleIds[i]);
            GoRoute(r);
        }

        /// One step down the Free Play list: clamped at the ends, with a tick where the press goes nowhere.
        void MenuStep(int dir)
        {
            _menuIndex = ShellNav.CarouselStep(_menuIndex, dir, Games.Count, out bool atEnd);
            ShellSfx(atEnd ? "tick" : "ui");   // web freeplay.js: a press at the end of the rail still ticks
        }

        // The roster screen (web roster.js). The rules live in Core/Roster.cs; this maps its outcome to
        // sound, the flash line and the way out.
        string _rosterFlash;
        float _rosterFlashT, _rosterShakeT;
        int _rosterShakeCard = -1;
        string _rosterLoggedStage;

        void RosterPress(string act)
        {
            var o = _roster.Input(act);
            if (o.Event == RosterEvent.Miss && o.Flash == null && act == "a")
            {
                _rosterShakeT = 0.16f; _rosterShakeCard = _roster.Cursor;   // a taken card shakes
                Debug.Log($"[SHELL] roster taken char={Cast.All[_roster.Cursor].Id}");
            }
            ApplyRoster(o);
        }

        void ApplyRoster(RosterOutcome o)
        {
            switch (o.Event)
            {
                case RosterEvent.Tick: ShellSfx("ui"); break;
                case RosterEvent.Miss: ShellSfx("miss"); if (o.Flash != null) Debug.Log($"[SHELL] roster miss flash={o.Flash}"); break;
                case RosterEvent.Lock: ShellSfx("perfect"); Debug.Log($"[SHELL] roster pick seat={o.Seat} char={_roster.Picks[o.Seat]}"); break;
                case RosterEvent.CpuLock: ShellSfx("great"); Debug.Log($"[SHELL] roster pick seat={o.Seat} char={_roster.Picks[o.Seat]} cpu"); break;
                case RosterEvent.Unlock: ShellSfx("uiBack"); Debug.Log($"[SHELL] roster unpick seat={o.Seat}"); break;
                case RosterEvent.Leave: ShellSfx("uiBack"); _roster = null; _mode = Mode.Title; return;
                case RosterEvent.Start: StartFromRoster(); return;
            }
            if (o.Flash != null) { _rosterFlash = o.Flash; _rosterFlashT = 0.9f; }
            string st = _roster.Stage.ToString().ToLowerInvariant();
            if (st != _rosterLoggedStage) { _rosterLoggedStage = st; Debug.Log($"[SHELL] roster stage={st}"); }
        }

        /// web roster.js startRun: seat the players, then leave for the party hub or the Free Play list.
        void StartFromRoster()
        {
            var players = _roster.BuildPlayers(Save.HumanName);
            var route = _roster.ExitRoute;
            ShellSfx("fanfare");
            bool party = _roster.Mode == "party";
            _roster = null;
            _rosterLoggedStage = null;
            if (party) StartParty(players);
            else { FreeCast = players; Debug.Log($"[SHELL] freecast char={players[0].Char} name={players[0].Name}"); GoRoute(route); }
        }

        static readonly string[] SeatKinds = { "CPU easy", "CPU normal", "CPU hard", "EMPTY" };
        static string SeatLabel(RosterState r, int seat) => r.Seat[seat] == RosterState.You ? "YOU" : SeatKinds[r.Seat[seat]];

        // The web options.js rows, in its order.
        enum Opt { Music, Sfx, Offset, Reduce, Graphics, Swing, Reset, Back }
        static readonly string[] OptionRows =
            { "MUSIC VOLUME", "SFX VOLUME", "TIMING OFFSET", "REDUCE MOTION", "GRAPHICS", "SWING KINGS INPUT", "RESET PROGRESS", "BACK" };

        void AdjustOption(int row, int dir)
        {
            switch ((Opt)row)
            {
                case Opt.Music: Settings.Music = Settings.StepPct(Settings.Music, dir); break;
                case Opt.Sfx: Settings.Sfx = Settings.StepPct(Settings.Sfx, dir); break;
                case Opt.Offset: Settings.OffsetMs = Settings.StepOffset(Settings.OffsetMs, dir); break;
                case Opt.Reduce: Settings.ReduceMotionOption = !Settings.ReduceMotionOption; break;
                case Opt.Graphics:
                    Settings.Graphics = Settings.Cycle(Settings.Graphics, dir);
                    RenderQuality.Apply(Settings.Graphics);   // live, so the difference shows before leaving
                    break;
                case Opt.Swing: Settings.SwingInputChoice = Settings.Cycle(Settings.SwingInputChoice, dir); break;
                default: return;
            }
            ShellSfx("ui");   // the web's ui sound; also lets the SFX level be heard
        }

        void OptionChoice(int row)
        {
            _optIndex = row;
            switch ((Opt)row)
            {
                case Opt.Reset: _confirmReset = true; ShellSfx("uiBack"); break;
                case Opt.Back: ShellSfx("uiBack"); _mode = Mode.Title; break;
                case Opt.Reduce: case Opt.Graphics: case Opt.Swing: AdjustOption(row, 1); break;
            }
        }

        string OptionValue(int row)
        {
            switch ((Opt)row)
            {
                case Opt.Music: return Mathf.RoundToInt(Settings.Music * 100) + "%";
                case Opt.Sfx: return Mathf.RoundToInt(Settings.Sfx * 100) + "%";
                case Opt.Offset: { float v = Settings.OffsetMs; return (v > 0 ? "+" : "") + v.ToString("0") + "ms"; }
                case Opt.Reduce: return Settings.ReduceMotionOption ? "ON" : Settings.ReduceMotion ? "ON (SYSTEM)" : "OFF";
                case Opt.Graphics: return Settings.Graphics.ToString().ToUpperInvariant();
                case Opt.Swing:
                    switch (Settings.SwingInputChoice)
                    {
                        case SwingInput.Mouse: return "MOUSE CONDUCT";
                        case SwingInput.Camera: return Settings.CameraInputAvailable ? "CAMERA CONDUCT" : "CAMERA CONDUCT (N/A)";
                        default: return "TAP";
                    }
                case Opt.Reset: return _confirmReset ? "scores, ranks, stats" : "";
            }
            return "";
        }

        void TickGame()
        {
            _phase = "tick";
            double now = Now;
            if (Autoplay)
            {
                // Pressed on the frame the note arrives, like a player with
                // perfect timing and a 60 Hz eye.
                while (_autoCursor < _autoChart.Count && _autoChart[_autoCursor].Value <= now)
                {
                    _presses.Enqueue(new PressEvent { Action = _autoChart[_autoCursor].Key, Audio = now + _pausedTotal });
                    _autoCursor++;
                }
            }
            while (_presses.Count > 0)
            {
                var p = _presses.Dequeue();
                if (p.Kind == PressKind.Move) { _game.PointerMove(p.Y, p.Audio - _pausedTotal); continue; }
                if (p.Kind == PressKind.Up) { _game.Release(p.Action, p.Audio - _pausedTotal); continue; }
                if (p.Action == "pause") { if (!Autoplay) { Pause(); return; } continue; }
                // Mouse and touch are the "a" button in play.
                _game.Press(HeldName(p.Action), p.Audio - _pausedTotal);
            }
            _game.Tick(now, Time.deltaTime);
            _phase = "result";
            var o = _game.Result();
            if (o != null) Finish(o);
        }

        // ------------------------------------------------------------ faults

        string _externalFault;
        bool _recovering;
        Mode _loggedMode = Mode.Gate;

        void OnLogMessage(string condition, string stack, LogType type)
        {
            if (type == LogType.Exception && _gameGo != null && _externalFault == null && _mode != Mode.Dead)
                _externalFault = condition;
        }

        /// Log the shell screen the player is on when it changes (the gates read these lines).
        void LogScreen()
        {
            if (_mode == _loggedMode) return;
            _loggedMode = _mode;
            switch (_mode)
            {
                case Mode.Title: Debug.Log("[SHELL] screen=title"); break;
                case Mode.Menu: Debug.Log("[SHELL] screen=menu"); break;
                case Mode.Options: Debug.Log("[SHELL] screen=options"); break;
                case Mode.Roster: Debug.Log("[SHELL] screen=roster"); break;
                case Mode.Dead: Debug.Log("[SHELL] screen=dead"); break;
            }
        }

        void Fault(string phase, Exception e) => Fault(phase, e.GetType().Name + ": " + e.Message, e.ToString());

        /// One fault: one "[ERROR]" line (a warning in the console: a Debug.LogError would also trip the page-error
        /// check of every gate; the gates match the line instead), LastError, and back to the title with no game left
        /// behind. A fault with no game involved is the shell itself failing: stop on an error text, do not loop.
        void Fault(string phase, string msg, string detail)
        {
            if (_mode == Mode.Dead || _recovering) return;
            _recovering = true;
            bool inGame = _gameGo != null;
            string id = inGame ? (_entry?.Id ?? "?") : null;
            LastError = (inGame ? id + " " : "") + phase + ": " + msg;
            Debug.LogWarning("[ERROR] " + LastError);
            if (detail != null) Debug.Log("[FAULT] " + detail);
            try
            {
                if (!inGame) { Die(); return; }
                _gameGo.SetActive(false);   // off the screen this frame; Destroy is end of frame
                _party = null;
                _presses.Clear();
                CloseGame();
                _mode = Mode.Title;
                Debug.Log("[SHELL] title after-error");
            }
            catch (Exception) { Die(); }
            finally { _recovering = false; LogScreen(); }
        }

        /// The shell failed and there is nothing safe to go back to: stop here, say so.
        void Die()
        {
            try { CloseGame(); } catch (Exception) { }
            _mode = Mode.Dead;
        }

        GUIStyle _deadStyle;

        void DrawDead()
        {
            var c = GUI.color;
            GUI.color = new Color(0.02f, 0.02f, 0.04f, 1);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (_deadStyle == null)
                _deadStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Screen.height / 28), alignment = TextAnchor.MiddleCenter, wordWrap = true };
            GUI.Label(new Rect(Screen.width * 0.1f, 0, Screen.width * 0.8f, Screen.height),
                "BONANZA STOPPED\n\n" + (LastError ?? "unknown error") + "\n\nReload the page to try again.", _deadStyle);
            GUI.color = c;
        }

        // ------------------------------------------------------------ perf HUD

        bool _perf;
        int _perfFrames;
        float _perfSum, _perfMax;
        string _perfText = "perf: measuring";
        GUIContent _perfContent;
        GUIStyle _perfStyle;

        void TogglePerf()
        {
            _perf = !_perf;
            _perfFrames = 0; _perfSum = 0; _perfMax = 0;
            Debug.Log(_perf ? "[PERF] hud=on" : "[PERF] hud=off");
        }

        /// Counters per frame (no allocation); the text is rebuilt twice a second.
        void PerfSample()
        {
            if (!_perf) return;
            float dt = Time.unscaledDeltaTime;
            _perfFrames++; _perfSum += dt;
            if (dt > _perfMax) _perfMax = dt;
            if (_perfSum < 0.5f) return;
            var l = RenderQuality.Level;
            _perfText = string.Format("fps {0:0}   frame {1:0.0} ms   worst {2:0.0} ms\ngraphics {3:0.##}x {4} {5} level {6}",
                _perfFrames / _perfSum, 1000f * _perfSum / _perfFrames, 1000f * _perfMax,
                l.Scale, l.Tier.ToString().ToLowerInvariant(),
                RenderQuality.Auto ? "(auto: " + RenderQuality.Reason + ")" : "(" + RenderQuality.Reason + ")", RenderQuality.LadderIndex);
            if (_perfContent != null) _perfContent.text = _perfText;
            _perfFrames = 0; _perfSum = 0; _perfMax = 0;
        }

        void DrawPerf()
        {
            if (_perfContent == null)
            {
                _perfContent = new GUIContent(_perfText);
                _perfStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
                _perfStyle.normal.textColor = new Color(0.79f, 1f, 0.69f);
            }
            var r = new Rect(8, Screen.height - 52, 420, 44);
            var c = GUI.color;
            GUI.color = new Color(0.02f, 0.03f, 0.04f, 0.78f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = c;
            GUI.Label(new Rect(r.x + 8, r.y, r.width - 8, r.height), _perfContent, _perfStyle);
        }

        // ------------------------------------------------------------ screens

        void OnGUI()
        {
            try
            {
                if (_mode == Mode.Dead) { DrawDead(); return; }
                _phase = "gui";
                DrawScreens();
                if (_perf && Event.current.type == EventType.Repaint) DrawPerf();
            }
            catch (Exception e) { if (_mode != Mode.Dead) Fault("gui", e); }
        }

        void DrawScreens()
        {
            // A canvas-HUD game plays with no IMGUI pass at all.
            if (_mode == Mode.Playing && _game != null && _game.UseCanvasHud) return;
            Hud.Begin();
            float s = Hud.S;
            if (_audioLost)
            {
                // The OS took the audio output (or the tab slept). Nothing else is clickable until a tap resumes it.
                Hud.Dim(0.85f);
                Hud.Center(0.34f, "AUDIO PAUSED", Hud.Big, Hud.Gold, 120);
                Hud.Center(0.55f, "TAP TO RESUME AUDIO", Hud.Mid, Color.white);
                return;
            }
            switch (_mode)
            {
                case Mode.Gate:
                    Hud.Dim(0.85f);
                    Hud.Center(0.3f, "BONANZA", Hud.Big, Hud.Gold, 120);
                    Hud.Center(0.55f, "CLICK / TAP / SPACE TO START", Hud.Mid, Color.white);
                    if (_gatePressed && !_clock.Running)
                        Hud.Center(0.68f, "INPUT OK - WAITING FOR THE AUDIO CLOCK (is sound output available?)", Hud.Mid, Hud.Gold);
                    break;

                case Mode.Menu:
                    Hud.Dim(0.85f);
                    Hud.Center(0.08f, "FREE PLAY", Hud.Big, Hud.Gold, 100);
                    for (int i = 0; i < Games.Count; i++)
                    {
                        var e = Games[i];
                        var r = new Rect(Screen.width * 0.25f, Screen.height * 0.26f + i * 80 * s, Screen.width * 0.5f, 70 * s);
                        bool sel = i == _menuIndex;
                        Hud.Box(r, sel ? new Color(1, 0.8f, 0.3f, 0.35f) : new Color(1, 1, 1, 0.08f));
                        var card = Records.Card(Save.Get(e.Id));
                        Hud.Text(new Rect(r.x + 16 * s, r.y, r.width, r.height * 0.6f), e.Title, Hud.Mid, sel ? Hud.Gold : Color.white, TextAnchor.MiddleLeft);
                        Hud.Text(new Rect(r.x + 16 * s, r.y + r.height * 0.5f, r.width, r.height * 0.5f), e.Blurb, Hud.Small, new Color(1, 1, 1, 0.8f), TextAnchor.MiddleLeft);
                        // The record: rank, best score, plays (web freeplay.js card).
                        Hud.Text(new Rect(r.x, r.y, r.width - 16 * s, r.height * 0.6f), $"RANK {card.Rank}   BEST {card.Best}", Hud.Small, sel ? Hud.Gold : Color.white, TextAnchor.MiddleRight);
                        Hud.Text(new Rect(r.x, r.y + r.height * 0.5f, r.width - 16 * s, r.height * 0.5f), card.Plays, Hud.Small, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleRight);
                        if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { _menuIndex = i; _from = "freeplay"; ShellSfx("fanfare"); Open(e); }
                    }
                    Hud.Center(0.9f, "UP/DOWN to choose   SPACE / click to play   ESC / X: back", Hud.Small, new Color(1, 1, 1, 0.7f), 40);
                    break;

                case Mode.Title:
                    Hud.Dim(0.85f);
                    // A click wakes the reel and is consumed (never also an item); any other click only resets the idle timer.
                    if (Event.current.type == EventType.MouseDown)
                    {
                        if (Title.Attract) { TitleReelPress("a"); Event.current.Use(); }
                        else Title.Reel.Press("click", false);
                    }
                    DrawTitleLogo(s);
                    for (int i = 0; i < TitleItems.Length; i++)
                        if (MenuRow(i, 0.4f, TitleItems[i], null, i == _titleIndex, s) && !Title.Attract) TitleChoice(i);
                    var st = Save.Stats;
                    var statLines = Records.StatsLines(st.Rounds, Records.BestOverall(Games.Select(e => new KeyValuePair<string, GameRecord>(e.Title, Save.Get(e.Id)))));
                    Hud.Center(0.74f, statLines[0] + "\n" + statLines[1], Hud.Small, Color.white, 80);
                    if (st.Rounds > 0)
                        Hud.Center(0.82f, $"{st.Notes} NOTES   {st.Parties} PARTIES   {st.Wins} WINS", Hud.Small, new Color(1, 1, 1, 0.7f), 40);
                    Hud.Center(0.9f, "UP/DOWN to choose   SPACE / click to select", Hud.Small, new Color(1, 1, 1, 0.7f), 40);
                    if (_perfTip)
                    {
                        Hud.Dim(0.6f);
                        Hud.Center(0.2f, "RUNNING SLOW ON THIS MACHINE", Hud.Mid, Hud.Gold, 60);
                        Hud.Center(0.3f, "Graphics are already at their lowest. Try: power mode Best performance,\nplay plugged in, update your graphics driver, and set your browser to\nHigh performance in the system graphics settings.\n\n(any key to close)", Hud.Small, Color.white, 160);
                    }
                    if (Title.Attract) DrawTitleReel(s);
                    break;

                case Mode.Options:
                    Hud.Dim(0.85f);
                    Hud.Center(0.08f, "OPTIONS", Hud.Big, Hud.Gold, 100);
                    for (int i = 0; i < OptionRows.Length; i++)
                    {
                        bool isReset = i == (int)Opt.Reset;
                        string label = isReset && _confirmReset ? "RESET ALL PROGRESS?" : OptionRows[i];
                        bool grey = i == (int)Opt.Swing && Settings.SwingInputChoice == SwingInput.Camera && !Settings.CameraInputAvailable;
                        if (MenuRow(i, 0.21f, label, OptionValue(i), i == _optIndex, s, 50, 44, grey, 0.6f))
                        {
                            if (_confirmReset) { if (isReset) { Save.ResetProgress(Games.Select(e => e.Id)); ShellSfx("fanfare"); } else ShellSfx("uiBack"); _confirmReset = false; }
                            else OptionChoice(i);
                        }
                    }
                    if (_confirmReset) Hud.Center(0.8f, "SPACE: reset     any other key: cancel", Hud.Small, new Color(1, 0.6f, 0.6f, 0.9f), 40);
                    else if (_optIndex == (int)Opt.Graphics)
                        Hud.Center(0.8f, $"Now drawing at {RenderQuality.Level.Scale:0.##}x, {RenderQuality.Level.Tier.ToString().ToUpperInvariant()}{(RenderQuality.Auto ? " (auto: " + RenderQuality.Reason + ")" : "")}", Hud.Small, new Color(1, 1, 1, 0.7f), 40);
                    else if (_optIndex == (int)Opt.Swing)
                        Hud.Center(0.8f, "Tap is the default. Mouse: hold the button as the ball flies, flick the pointer DOWN as it lands.\n" + (Settings.CameraInputAvailable ? "Camera: raise your hand as the ball flies, conduct DOWN as it lands. Asks for your webcam when a round starts; nothing is recorded or sent." : "Camera conducting needs a browser with webcam access; here it conducts with the mouse."), Hud.Small, new Color(1, 1, 1, 0.7f), 80);
                    else if (_optIndex == (int)Opt.Offset)
                        Hud.Center(0.8f, "Negative offset = your presses land late.\nPress late and see GOOD more than PERFECT? Try -20 ms steps.", Hud.Small, new Color(1, 1, 1, 0.7f), 80);
                    Hud.Center(0.9f, "UP/DOWN choose   LEFT/RIGHT adjust   ESC / X back", Hud.Small, new Color(1, 1, 1, 0.7f), 40);
                    break;

                case Mode.Roster: DrawRoster(s); break;

                case Mode.PartyHub:
                    Hud.Dim(0.88f);
                    DrawPartyHub(s);
                    break;

                case Mode.Rules:
                    Hud.Dim(0.7f);
                    Hud.Center(0.22f, _game.Title, Hud.Big, Hud.Gold, 120);
                    Hud.Center(0.42f, _game.Blurb, Hud.Mid, Color.white);
                    Hud.Center(0.55f, _game.Controls, Hud.Small, Color.white, 80);
                    Hud.Center(0.75f, "PRESS TO START   (ESC / X: back)", Hud.Mid, Hud.Gold);
                    break;

                case Mode.Playing:
                    _game.DrawHud(s);
                    break;

                case Mode.Paused:
                    if (!_game.UseCanvasHud) _game.DrawHud(s);
                    if (_resumeLeft > 0)
                    {
                        int n = Mathf.Max(1, Mathf.CeilToInt(_resumeLeft / _resumeSpb));
                        Hud.Center(0.38f, n.ToString(), Hud.Big, Hud.Gold, 140);
                        break;
                    }
                    Hud.Dim(0.6f);
                    Hud.Center(0.2f, "PAUSED", Hud.Big, Hud.Gold, 120);
                    string[] items = PauseItems;
                    for (int i = 0; i < items.Length; i++)
                    {
                        var r = new Rect(Screen.width * 0.35f, Screen.height * 0.42f + i * 70 * s, Screen.width * 0.3f, 60 * s);
                        Hud.Box(r, i == _pauseIndex ? new Color(1, 0.8f, 0.3f, 0.35f) : new Color(1, 1, 1, 0.08f));
                        Hud.Text(r, items[i], Hud.Mid, i == _pauseIndex ? Hud.Gold : Color.white);
                        if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { PauseChoice(i); break; }
                    }
                    break;

                case Mode.Results:
                    DrawResults(s);
                    break;
            }
        }

        // Results reveal (web shell/results.js): input lock, score count-up,
        // stats, rank stamp with its sting, then the party placings.
        const float ResultsLock = 0.7f, CountAt = 0.3f, CountS = 0.9f, StatsAt = 0.45f,
                    StampAt = 1.3f, PartyAt = 1.65f, RevealEnd = 2.4f;
        float _resultsT;
        bool _stung;

        /// Each rank has its own sting, S the full fanfare down to a D's slide off the chord (web results.js RANK_STING),
        /// on the results track's chord.
        void RankSting(string rank) => ShellSfx(SfxTheory.RankSting(rank));

        /// The card is on the canvas (HudKit/ResultsCard); IMGUI keeps the AGAIN / MENU click targets for mouse and touch.
        void DrawResults(float s)
        {
            if (_party != null || _resultsT < ResultsLock || Autoplay) return;
            var again = new Rect(Screen.width * 0.25f, Screen.height * 0.9f, Screen.width * 0.2f, 50 * s);
            var menu = new Rect(Screen.width * 0.55f, Screen.height * 0.9f, Screen.width * 0.2f, 50 * s);
            Hud.Box(again, new Color(1, 1, 1, 0.12f)); Hud.Text(again, "AGAIN", Hud.Small, Color.white);
            Hud.Box(menu, new Color(1, 1, 1, 0.12f)); Hud.Text(menu, "MENU", Hud.Small, Color.white);
            if (GUI.Button(again, GUIContent.none, GUIStyle.none)) Open(_entry);
            else if (GUI.Button(menu, GUIContent.none, GUIStyle.none)) LeaveRound();
        }

        /// One selectable row: label left, value right. Row i sits at y0 + i rows. True if clicked.
        // The roster screen, IMGUI placeholders until the visual phase: a header, the 4x2 character grid,
        // the four seats, a hint. Same stages and wording as the web.
        void DrawRoster(float s)
        {
            if (_roster == null) return;
            var r = _roster;
            Hud.Dim(0.85f);
            string head, sub, hint;
            switch (r.Stage)
            {
                case RosterStage.Lineup: head = "PICK YOUR RIVALS"; sub = "you are P1 - UP/DOWN pick a seat - LEFT/RIGHT CPU easy / normal / hard / off"; hint = "LEFT/RIGHT seat type   SPACE continue   X back"; break;
                case RosterStage.Chars: head = "CHOOSE YOUR CHARACTER"; sub = "move with the arrows - SPACE to lock in - X to go back"; hint = "ARROWS choose   SPACE lock in   X back"; break;
                case RosterStage.Cpu: head = "CPU IS CHOOSING..."; sub = "they always take the good one"; hint = "CPU choosing..."; break;
                default:
                    head = r.Mode == "party" ? "LINEUP SET - PRESS SPACE" : "READY - PRESS SPACE";
                    sub = r.Mode == "party" ? "four games, one crown" : "pick a minigame next";
                    hint = r.Mode == "party" ? "SPACE start the party   X back" : "SPACE pick a game   X back"; break;
            }
            Hud.Center(0.04f, head, Hud.Big, Hud.Gold, 100);
            if (_rosterFlash != null) Hud.Center(0.17f, _rosterFlash, Hud.Small, new Color(1, 0.45f, 0.4f), 40);
            else Hud.Center(0.17f, sub, Hud.Small, new Color(1, 1, 1, 0.8f), 40);

            // character grid: 4 columns, 2 rows
            bool gridOn = r.Stage == RosterStage.Chars;
            float cw = 190 * s, ch = 84 * s, gap = 12 * s;
            float gx = Screen.width * 0.5f - (4 * cw + 3 * gap) / 2, gy = Screen.height * 0.26f;
            for (int i = 0; i < Cast.All.Count; i++)
            {
                var c = Cast.All[i];
                var rect = new Rect(gx + (i % RosterState.Cols) * (cw + gap) + (i == _rosterShakeCard && _rosterShakeT > 0 ? Mathf.Sin(_rosterShakeT * 90f) * 6 * s : 0), gy + (i / RosterState.Cols) * (ch + gap), cw, ch);
                var col = new Color(((c.Color >> 16) & 255) / 255f, ((c.Color >> 8) & 255) / 255f, (c.Color & 255) / 255f);
                bool taken = r.Taken(i), on = gridOn && i == r.Cursor;
                float dim = gridOn ? (taken ? 0.3f : 1f) : 0.45f;
                Hud.Box(new Rect(rect.x - (on ? 4 * s : 0), rect.y - (on ? 4 * s : 0), rect.width + (on ? 8 * s : 0), rect.height + (on ? 8 * s : 0)), on ? new Color(1, 1, 1, 0.9f) : new Color(1, 1, 1, 0.1f));
                Hud.Box(rect, new Color(col.r * dim * 0.8f, col.g * dim * 0.8f, col.b * dim * 0.8f, 1));
                Hud.Text(rect, c.Name, Hud.Mid, new Color(1, 1, 1, taken ? 0.5f : 1f), TextAnchor.MiddleCenter);
                if (taken) Hud.Text(new Rect(rect.x, rect.y + rect.height * 0.62f, rect.width, rect.height * 0.38f), "P" + (r.TakenBy(i) + 1), Hud.Small, Hud.Gold, TextAnchor.MiddleCenter);
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && r.Stage == RosterStage.Chars) { r.Cursor = i; RosterPress("a"); }
            }
            if (gridOn) Hud.Center(0.62f, Cast.All[r.Cursor].Name + " - " + Cast.All[r.Cursor].Trait, Hud.Small, Color.white, 40);

            // the four seats
            float sw = 220 * s, sh = 72 * s, sy = Screen.height * 0.7f;
            float sx = Screen.width * 0.5f - (4 * sw + 3 * gap) / 2;
            for (int i = 0; i < RosterRules.Seats; i++)
            {
                if (r.Mode == "free" && i > 0) continue;   // Free Play is solo: no rival seats to show
                var rect = new Rect(sx + i * (sw + gap), sy, sw, sh);
                bool off = r.Seat[i] == RosterState.Off;
                bool active = (r.Stage == RosterStage.Lineup && i == r.SeatSel) || (r.Stage == RosterStage.Chars && i == r.Active);
                Hud.Box(rect, active ? new Color(1, 0.8f, 0.3f, 0.35f) : new Color(1, 1, 1, off ? 0.04f : 0.1f));
                Hud.Text(new Rect(rect.x + 10 * s, rect.y + 4 * s, rect.width, rect.height * 0.5f), "P" + (i + 1) + "  " + (r.Picks[i] != null ? Cast.ById(r.Picks[i]).Name : "-"), Hud.Small, off ? new Color(1, 1, 1, 0.4f) : Color.white, TextAnchor.MiddleLeft);
                Hud.Text(new Rect(rect.x + 10 * s, rect.y + rect.height * 0.5f, rect.width, rect.height * 0.45f), SeatLabel(r, i) + (r.Picks[i] != null ? "  READY!" : ""), Hud.Small, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleLeft);
                if (r.Picks[i] != null)
                {
                    var c = Cast.ById(r.Picks[i]);
                    Hud.Box(new Rect(rect.xMax - 12 * s, rect.y, 12 * s, rect.height), new Color(((c.Color >> 16) & 255) / 255f, ((c.Color >> 8) & 255) / 255f, (c.Color & 255) / 255f));
                }
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && r.Stage == RosterStage.Lineup)
                {
                    r.SeatSel = i;
                    if (i > 0) RosterPress("right"); else RosterPress("left");
                }
            }
            if (r.Stage == RosterStage.Go && GUI.Button(new Rect(0, 0, Screen.width, Screen.height * 0.26f), GUIContent.none, GUIStyle.none)) RosterPress("a");
            Hud.Center(0.9f, hint, Hud.Small, new Color(1, 1, 1, 0.7f), 40);
        }

        /// The wordmark: each letter pops on the title music's beat, lagging a little behind its neighbour (web title.js).
        void DrawTitleLogo(float s)
        {
            const string word = "BONANZA";
            double amp = Settings.ReduceMotion ? 0.35 : 1;
            double beat = Title.Beat;
            var widths = new float[word.Length];
            float total = 0;
            for (int i = 0; i < word.Length; i++) { widths[i] = Hud.Big.CalcSize(new GUIContent(word[i].ToString())).x + 4 * s; total += widths[i]; }
            float x = (Screen.width - total) / 2, y = Screen.height * 0.14f, h = 120 * s;
            var pivot = new Vector2(Screen.width / 2f, y + h / 2);
            var saved = GUI.matrix;
            float logo = (float)TitleBeat.LogoScale(beat, amp);
            GUIUtility.ScaleAroundPivot(new Vector2(logo, logo), pivot);
            var outer = GUI.matrix;
            for (int i = 0; i < word.Length; i++)
            {
                TitleBeat.Letter(beat, i, amp, out double sc, out double lift, out double tilt);
                var r = new Rect(x, y + (float)lift * s, widths[i], h);
                var c = new Vector2(r.center.x, r.center.y);
                GUI.matrix = outer;
                GUIUtility.ScaleAroundPivot(new Vector2((float)sc, (float)sc), c);
                GUIUtility.RotateAroundPivot((float)tilt, c);
                Hud.Text(r, word[i].ToString(), Hud.Big, Hud.Gold);
                x += widths[i];
            }
            GUI.matrix = saved;
        }

        /// Placeholder attract card: the game on show, how far through its hold, and the way out. The visual phase draws
        /// the real card from Title.CurrentGameId and Title.StepProgress.
        void DrawTitleReel(float s)
        {
            var e = Find(Title.CurrentGameId);
            if (e == null) return;
            Hud.Dim(0.55f);
            float pulse = Title.BeatPulse;
            Hud.Center(0.34f, e.Title, Hud.Big, Hud.Gold, 120);
            Hud.Center(0.48f, e.Blurb, Hud.Mid, Color.white, 60);
            var bar = new Rect(Screen.width * 0.3f, Screen.height * 0.6f, Screen.width * 0.4f, 10 * s);
            Hud.Box(bar, new Color(1, 1, 1, 0.2f));
            Hud.Box(new Rect(bar.x, bar.y, bar.width * Title.StepProgress, bar.height), Hud.Gold);
            Hud.Center(0.9f, "PRESS ANY BUTTON", Hud.Mid, new Color(1, 1, 1, 0.45f + 0.55f * pulse), 60);
        }

        bool MenuRow(int i, float y0, string label, string value, bool sel, float s, float pitch = 80, float height = 66, bool grey = false, float width = 0.44f)
        {
            var r = new Rect(Screen.width * (0.5f - width / 2), Screen.height * y0 + i * pitch * s, Screen.width * width, height * s);
            Hud.Box(r, sel ? new Color(1, 0.8f, 0.3f, 0.35f) : new Color(1, 1, 1, 0.08f));
            Hud.Text(new Rect(r.x + 18 * s, r.y, r.width, r.height), label, Hud.Mid, sel ? Hud.Gold : Color.white, TextAnchor.MiddleLeft);
            if (!string.IsNullOrEmpty(value))
                Hud.Text(new Rect(r.x, r.y, r.width - 18 * s, r.height), value, Hud.Small, grey ? new Color(1, 1, 1, 0.4f) : new Color(1, 1, 1, 0.85f), TextAnchor.MiddleRight);
            return i >= 0 && GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        static readonly string[] PlaceName = { "1ST", "2ND", "3RD", "4TH" };

        /// Between rounds: the standings and what's up next. After the last
        /// round: the winner and a round-by-round table.
        void DrawPartyHub(float s)
        {
            if (_party == null) return;
            var places = _party.StandingPlaces();
            var standings = _party.Standings();
            var last = _party.Scores.Count > 0 ? _party.Scores[_party.Scores.Count - 1] : null;
            if (_party.Done)
            {
                Hud.Center(0.04f, "PARTY COMPLETE", Hud.Small, Color.white, 40);
                var head = _party.Headline();
                Hud.Center(0.1f, head.Title, Hud.Big, Hud.Gold, 100);
                if (head.Tiebreak != null) Hud.Center(0.25f, head.Tiebreak, Hud.Mid, Color.white, 50);
            }
            else
            {
                var e = Find(_party.CurrentGame);
                bool finale = _party.Index == _party.Length - 1;
                Hud.Center(0.04f, (finale ? "FINAL ROUND   " : "ROUND ") + $"{_party.Index + 1} OF {_party.Length}", Hud.Small, Color.white, 40);
                Hud.Center(0.1f, e.Title, Hud.Big, Hud.Gold, 100);
                Hud.Center(0.25f, e.Blurb, Hud.Mid, Color.white, 50);
                if (last != null)
                {
                    var top = last.Players.Where(p => p.Place == 1).Select(p => _party.Players[p.Id].Name).ToList();
                    Hud.Center(0.31f, $"{Find(last.Game).Title}: {string.Join(" & ", top)} {(top.Count > 1 ? "tied it" : "took it")}", Hud.Small, new Color(1, 1, 1, 0.8f), 40);
                }
            }

            // Standings: place, name, points (and what the last round paid).
            for (int i = 0; i < standings.Count; i++)
            {
                var p = standings[i];
                int pl = places[p.Id];
                bool tied = standings.Count(q => places[q.Id] == pl) > 1;
                var row = last?.Players.First(q => q.Id == p.Id);
                string pts = $"{p.Points} PTS" + (standings.Any(q => q != p && q.Points == p.Points) ? $" / {p.Wins} WIN{(p.Wins == 1 ? "" : "S")}" : "");
                if (!_party.Done && row != null) pts += $"   +{row.Points}";
                MenuRow(i, 0.4f, $"{(tied ? "T-" : "")}{PlaceName[pl - 1]}   {p.Name}{(p.IsCpu ? "" : "  (YOU)")}", pts, !p.IsCpu, s);
            }

            if (_party.Done)
            {
                // Round-by-round table: one line per game.
                var sb = new System.Text.StringBuilder();
                foreach (var r in _party.Scores)
                {
                    sb.Append(Find(r.Game).Title).Append(r.Sim ? "*" : "").Append(":  ");
                    sb.Append(string.Join("   ", r.Players.Select(q => $"{_party.Players[q.Id].Name} +{q.Points}"))).Append('\n');
                }
                if (_party.Scores.Any(r => r.Sim)) sb.Append("* solo game: CPU rivals played off-screen at their skill level");
                Hud.Center(0.4f + (standings.Count * 80 * s + 10 * s) / Screen.height, sb.ToString(), Hud.Small, new Color(1, 1, 1, 0.85f), 160);
            }
            Hud.Center(0.93f, Autoplay ? "(autoplay)" : _party.Done ? "SPACE: done" : "SPACE: start   ESC: quit party", Hud.Mid, Hud.Gold, 50);
        }
    }
}
