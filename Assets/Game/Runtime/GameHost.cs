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

using System;
using System.Collections.Generic;
using System.Linq;
using Bonanza.Core;
using UnityEngine;
using UnityEngine.InputSystem;

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

        /// Every playable minigame, in menu order. A game not listed here is
        /// unreachable, including by the autoplay check.
        public static readonly List<Entry> Games = new List<Entry>
        {
            new Entry { Id = "swing-kings", Title = "SWING KINGS", Blurb = "One swing. One beat. Send it.", Type = typeof(SwingKingsGame) },
            new Entry { Id = "chomp-chorus", Title = "CHOMP CHORUS", Blurb = "Four singers, one of them is you.", Type = typeof(ChompChorusGame) },
            new Entry { Id = "drumline-dash", Title = "DRUMLINE DASH", Blurb = "Hear it. Hit it back.", Type = typeof(DrumlineDashGame) },
            new Entry { Id = "bounce-brigade", Title = "BOUNCE BRIGADE", Blurb = "The platforms are the tune.", Type = typeof(BounceBrigadeGame) },
            new Entry { Id = "finale-fever", Title = "FINALE FEVER", Blurb = "It only gets faster.", Type = typeof(FinaleFeverGame) },
        };

        public static Entry Find(string id) => Games.Find(e => e.Id == id);

        // ------------------------------------------------------------ state

        /// URP Lit, referenced from the scene so the build keeps its shader.
        [SerializeField] Material greybox;
        public Material Greybox => greybox;

        enum Mode { Gate, Title, Menu, Options, Roster, PartyHub, Rules, Playing, Paused, Results }

        AudioClock _clock;
        Mode _mode = Mode.Gate;
        public bool Autoplay { get; private set; }
        string _autoGame;
        bool _autoParty;
        int _partySeed = -1;   // `party=<n>`: seed the party as if n had been played before

        // ------------------------------------------------------------ party

        const int PartyLength = 4;   // web DEFAULT_OPTIONS.partyLength
        static readonly string[] CastNames = { "YOU", "ZIZZ", "KWARK", "TUFF" };
        /// Roster slots 1..3: index into Party.CpuTiers, or CpuTiers.Length for OFF.
        /// The web roster's party default: two NORMAL rivals and the last seat empty.
        readonly int[] _slotTier = { 0, 1, 1, 3 };
        PartySession _party;
        int _rosterIndex, _titleIndex, _optIndex;
        bool _confirmReset;
        float _autoWait;

        /// The party's lineup while a party runs; null in free play.
        public IList<PartyPlayer> Lineup => _party?.Players;
        Minigame _game;
        Entry _entry;
        Outcome _outcome;
        bool _newScore, _newRank;
        int _menuIndex;
        double _pausedTotal, _pausedAt;
        int _pauseIndex;

        readonly List<KeyValuePair<string, double>> _autoChart = new List<KeyValuePair<string, double>>();
        int _autoCursor;

        // ------------------------------------------------------------ input

        struct PressEvent { public string Action; public double Audio; }
        readonly Queue<PressEvent> _presses = new Queue<PressEvent>();
        readonly List<InputAction> _actions = new List<InputAction>();
        public static readonly string[] Actions = { "a", "left", "down", "up", "right" };

        InputAction Bind(string name, params string[] paths)
        {
            var act = new InputAction(name, InputActionType.Button);
            foreach (var p in paths) act.AddBinding(p);
            // The callback's timestamp is when the press happened, not the
            // frame it was noticed on.
            act.performed += ctx => _presses.Enqueue(new PressEvent { Action = name, Audio = _clock.ToAudioTime(ctx.time) });
            act.Enable();
            _actions.Add(act);
            return act;
        }

        // ------------------------------------------------------------ clock

        /// Game time: audio time minus time spent paused. Everything a game
        /// judges or schedules is in this domain.
        public double Now => _mode == Mode.Paused ? _pausedAt - _pausedTotal : _clock.Now - _pausedTotal;
        public bool ClockRunning => _clock.Running;

        /// dspTime for a game-time instant, for AudioSource.PlayScheduled.
        public double DspAt(double gameTime) => gameTime + _pausedTotal - _clock.Now + _clock.DspNow;

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

        /// The shared count-in tick (web audio sfx 'count').
        public void CountTick(double t, int accent) =>
            PlayAt(Tones.Blip(accent == 0 ? 1568 : 1175, 0.05f, 50), t, 0.3f);

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
            var args = Environment.GetCommandLineArgs();
            string url = Application.absoluteURL ?? "";
            Autoplay = url.Contains("autoplay") || Array.IndexOf(args, "-autoplay") >= 0
                       || Environment.GetEnvironmentVariable("BONANZA_AUTOPLAY") == "1";
            _autoGame = QueryParam(url, "game");
            int gi = Array.IndexOf(args, "-game");
            if (_autoGame == null && gi >= 0 && gi + 1 < args.Length) _autoGame = args[gi + 1];
            if (_autoGame == null) _autoGame = Environment.GetEnvironmentVariable("BONANZA_GAME");
            if (_autoGame == null || Find(_autoGame) == null) _autoGame = Autoplay || _autoGame != null ? "swing-kings" : null;
            // `?autoplay&party`: the bot plays a whole party with the default roster.
            string partyArg = QueryParam(url, "party");
            if (int.TryParse(partyArg, out int ps) && ps >= 0) _partySeed = ps;
            _autoParty = partyArg != null || Array.IndexOf(args, "-party") >= 0
                         || Environment.GetEnvironmentVariable("BONANZA_PARTY") == "1";
            Settings.Migrate();
            AudioListener.volume = 1;   // the music and SFX levels are applied at their own sources
            _sfxVol = Settings.Sfx;
            Settings.Changed += OnSettingsChanged;
            // `?quality=auto|high|medium|low` pins the Graphics setting for this run (test harness), like the web.
            string q = QueryParam(url, "quality");
            GraphicsSetting forced = default;
            bool pin = q != null && Enum.TryParse(q, true, out forced);
            RenderQuality.Apply(pin ? forced : Settings.Graphics);

            Bind("a", "<Keyboard>/space", "<Keyboard>/enter", "<Keyboard>/j", "<Gamepad>/buttonSouth");
            Bind("click", "<Mouse>/leftButton", "<Touchscreen>/primaryTouch/press");
            Bind("left", "<Keyboard>/leftArrow", "<Keyboard>/a", "<Gamepad>/dpad/left", "<Gamepad>/buttonWest");
            Bind("down", "<Keyboard>/downArrow", "<Keyboard>/s", "<Gamepad>/dpad/down");
            Bind("up", "<Keyboard>/upArrow", "<Keyboard>/w", "<Gamepad>/dpad/up", "<Gamepad>/buttonNorth");
            Bind("right", "<Keyboard>/rightArrow", "<Keyboard>/d", "<Gamepad>/dpad/right", "<Gamepad>/buttonEast");
            Bind("pause", "<Keyboard>/escape", "<Keyboard>/p", "<Gamepad>/start");

            _music = gameObject.AddComponent<MusicPlayer>();
            _music.Init(this);
            _music.Prepare("title");
            _music.Prepare("results");

            _voices = new AudioSource[16];
            for (int i = 0; i < _voices.Length; i++)
            {
                _voices[i] = gameObject.AddComponent<AudioSource>();
                _voices[i].playOnAwake = false;
            }
        }

        void OnDestroy()
        {
            Settings.Changed -= OnSettingsChanged;
            foreach (var a in _actions) a.Dispose();
        }

        static string QueryParam(string url, string key)
        {
            int q = url.IndexOf('?');
            if (q < 0) return null;
            foreach (var part in url.Substring(q + 1).Split('&', '#'))
            {
                var kv = part.Split('=');
                if (kv[0] == key) return kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
            }
            return null;
        }

        // ------------------------------------------------------------ routing

        void Open(Entry e)
        {
            CloseGame();
            _entry = e;
            _outcome = null;
            var go = new GameObject(e.Id);
            _game = (Minigame)go.AddComponent(e.Type);
            _game.Bind(this, Seed(e.Id));
            _game.Load();
            _music.Prepare(e.Id);   // decode now; the rules card waits for it
            _startRequested = false;
            _rulesWait = 0;
            _mode = Mode.Rules;
        }

        /// FNV-1a: a seed that is the same in every build (string.GetHashCode is not guaranteed to be).
        static uint Seed(string id)
        {
            uint h = 2166136261;
            foreach (char c in id) { h ^= c; h = unchecked(h * 16777619); }
            return h;
        }

        void Begin()
        {
            _pausedTotal = 0;
            _game.Begin(Now + 0.6);
            var g = _game;
            _music.PlayGame(_entry.Id, g.MusicTimeAt(MusicTracks.GameStartBeat),
                () => g.MusicBeatAt(Now) - MusicTracks.GameStartBeat,
                // the pitch lags the call by an audio buffer or two: aim a little ahead
                g.MusicFollowsTempo ? (Func<double>)(() => g.MusicTempo(g.MusicBeatAt(Now + 0.04))) : null);
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
            if (_game != null) Destroy(_game.gameObject);
            _game = null;
            AudioListener.pause = false; Time.timeScale = 1;
            foreach (var v in _voices) v.Stop();
            _music.SetDuck(1);
            if (_music.Current != "title") _music.Stop(0.25f);
        }

        void Finish(Outcome o)
        {
            _outcome = o;
            _mode = Mode.Results;
            _autoWait = 2.5f;
            _resultsT = 0;
            _stung = false;
            _music.PlayShell("results");
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
        }

        /// Seat the roster and shuffle the playlist; the finale always closes it.
        void StartParty()
        {
            var players = new List<PartyPlayer> { new PartyPlayer { Name = CastNames[0] } };
            for (int i = 1; i < 4; i++)
                if (_slotTier[i] < Party.CpuTiers.Length)
                    players.Add(new PartyPlayer { Name = CastNames[i], IsCpu = true, CpuSkill = Party.CpuTiers[_slotTier[i]].skill });
            int n = _partySeed >= 0 ? _partySeed : Save.Parties;
            _partySeed = -1;
            Save.Parties = Save.Parties + 1;
            var rng = new Rng(unchecked(0x5eedu + (uint)n * 0x9e3779b9u));
            var games = Party.Playlist(Games.Select(e => e.Id).ToList(), PartyLength, rng);
            _party = new PartySession(players, games, (uint)(rng.Next() * 4294967296.0));
            Debug.Log($"[PARTY] start games={string.Join(",", games)} players={string.Join(",", players.Select(p => p.Name))}");
            _autoWait = 1.5f;
            _mode = Mode.PartyHub;
        }

        void EndParty()
        {
            _party = null;
            CloseGame();
            _mode = Mode.Title;
        }

        /// Leave a round for wherever the player came from.
        void BackOut()
        {
            if (_party != null) EndParty();
            else { CloseGame(); _mode = Mode.Menu; }
        }

        void Pause()
        {
            _pausedAt = _clock.Now;
            _mode = Mode.Paused;
            _pauseIndex = 0;
            _resumeLeft = 0;
            AudioListener.pause = true; Time.timeScale = 0;   // freezes visual-only Update() animation
            _game.OnPause();
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
            _pausedTotal += _clock.Now - _pausedAt;
            _mode = Mode.Playing;
            AudioListener.pause = false; Time.timeScale = 1;
            _game.OnResume();
            _music.OnResume();
        }

        // ------------------------------------------------------------ loop

        void Update()
        {
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
                        else if (_autoGame != null) Open(Find(_autoGame));
                        else _mode = Mode.Title;
                    }
                    break;

                case Mode.Title:
                    while (_presses.Count > 0)
                    {
                        var p = _presses.Dequeue();
                        if (p.Action == "up" || p.Action == "left") _titleIndex = (_titleIndex + TitleItems.Length - 1) % TitleItems.Length;
                        else if (p.Action == "down" || p.Action == "right") _titleIndex = (_titleIndex + 1) % TitleItems.Length;
                        else if (p.Action == "a") { TitleChoice(_titleIndex); break; }
                    }
                    _presses.Clear();
                    break;

                case Mode.Menu:
                    while (_presses.Count > 0)
                    {
                        var p = _presses.Dequeue();
                        if (p.Action == "up" || p.Action == "left") _menuIndex = (_menuIndex + Games.Count - 1) % Games.Count;
                        else if (p.Action == "down" || p.Action == "right") _menuIndex = (_menuIndex + 1) % Games.Count;
                        else if (p.Action == "a") { Open(Games[_menuIndex]); break; }
                        else if (p.Action == "pause") { _mode = Mode.Title; break; }
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
                            if (p.Action == "a") Save.ResetProgress(Games.Select(e => e.Id));
                            _confirmReset = false;
                            continue;
                        }
                        if (p.Action == "up") _optIndex = (_optIndex + OptionRows.Length - 1) % OptionRows.Length;
                        else if (p.Action == "down") _optIndex = (_optIndex + 1) % OptionRows.Length;
                        else if (p.Action == "left" || p.Action == "right") AdjustOption(_optIndex, p.Action == "right" ? 1 : -1);
                        else if (p.Action == "a") OptionChoice(_optIndex);
                        else if (p.Action == "pause") { _mode = Mode.Title; break; }
                    }
                    _presses.Clear();
                    break;

                case Mode.Roster:
                    while (_presses.Count > 0)
                    {
                        var p = _presses.Dequeue();
                        if (p.Action == "up") _rosterIndex = (_rosterIndex + 3) % 4;
                        else if (p.Action == "down") _rosterIndex = (_rosterIndex + 1) % 4;
                        else if ((p.Action == "left" || p.Action == "right") && _rosterIndex < 3) CycleSlot(_rosterIndex + 1, p.Action == "right" ? 1 : -1);
                        else if (p.Action == "a") { if (_rosterIndex < 3) CycleSlot(_rosterIndex + 1, 1); else { StartParty(); break; } }
                        else if (p.Action == "pause") { _mode = Mode.Title; break; }
                    }
                    _presses.Clear();
                    break;

                case Mode.PartyHub:
                    bool next = Autoplay && (_autoWait -= Time.deltaTime) <= 0;
                    while (_presses.Count > 0)
                    {
                        var p = _presses.Dequeue();
                        if (Autoplay) continue;
                        if (p.Action == "a") next = true;
                        else if (p.Action == "pause") { EndParty(); break; }
                    }
                    _presses.Clear();
                    if (next && _mode == Mode.PartyHub)
                    {
                        if (_party.Done) { if (!Autoplay) EndParty(); }
                        else Open(Find(_party.CurrentGame));
                    }
                    break;

                case Mode.Rules:
                    _startRequested |= Autoplay;
                    while (_presses.Count > 0)
                    {
                        var p = _presses.Dequeue();
                        if (p.Action == "pause") { BackOut(); break; }
                        _startRequested = true;
                    }
                    _presses.Clear();
                    if (_startRequested && _mode == Mode.Rules && _clock.Running)
                    {
                        // The round's music has to be decoded before it starts: the
                        // count-in is two bars of it.
                        _rulesWait += Time.unscaledDeltaTime;
                        bool ready = _music.Ready(_entry.Id);
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
                        while (_presses.Count > 0) if (_presses.Dequeue().Action == "pause") reopen = true;
                        if (reopen) _resumeLeft = 0;
                        else if (_resumeLeft <= 0) Resume();
                        break;
                    }
                    while (_presses.Count > 0)
                    {
                        var p = _presses.Dequeue();
                        if (p.Action == "pause") { CountBackIn(); break; }
                        int np = PauseItems.Length;
                        if (p.Action == "up") _pauseIndex = (_pauseIndex + np - 1) % np;
                        if (p.Action == "down") _pauseIndex = (_pauseIndex + 1) % np;
                        if (p.Action == "a") { PauseChoice(_pauseIndex); break; }
                    }
                    _presses.Clear();
                    break;

                case Mode.Results:
                    _resultsT += Time.unscaledDeltaTime;
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
                            if (Autoplay || p.Action != "a") continue;
                            if (_resultsT < RevealEnd) _resultsT = RevealEnd;   // the first press finishes the reveal
                            else on = true;
                        }
                        if (on) ToHub();
                        break;
                    }
                    while (_presses.Count > 0)
                    {
                        var p = _presses.Dequeue();
                        if (Autoplay) continue;
                        if (_resultsT < RevealEnd && p.Action != "pause") { _resultsT = RevealEnd; continue; }
                        if (p.Action == "a") { Open(_entry); break; }                       // again
                        if (p.Action == "pause" || p.Action == "left") { CloseGame(); _mode = Mode.Menu; break; }
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

        bool _gatePressed, _startRequested;
        float _rulesWait;
        const float MusicLoadTimeout = 6;

        void ToHub()
        {
            _presses.Clear();
            CloseGame();
            _autoWait = 1.5f;
            _mode = Mode.PartyHub;
        }

        // Pause: in a party there is no restart (it would re-roll a round); quit ends the party.
        string[] PauseItems => _party != null ? new[] { "RESUME", "QUIT PARTY" } : new[] { "RESUME", "RESTART", "QUIT TO MENU" };

        void PauseChoice(int i)
        {
            var item = PauseItems[i];
            if (item == "RESUME") CountBackIn();
            else if (item == "RESTART") { var e = _entry; CloseGame(); Open(e); }
            else BackOut();
        }

        // ------------------------------------------------------------ title, options, roster

        static readonly string[] TitleItems = { "PARTY", "FREE PLAY", "OPTIONS" };

        void TitleChoice(int i)
        {
            _titleIndex = i;
            if (i == 0) { _rosterIndex = 3; _mode = Mode.Roster; }
            else if (i == 1) _mode = Mode.Menu;
            else { _optIndex = 0; _confirmReset = false; _mode = Mode.Options; }
        }

        void CycleSlot(int slot, int dir)
        {
            int n = Party.CpuTiers.Length + 1;
            _slotTier[slot] = (_slotTier[slot] + dir + n) % n;
        }

        static string SlotLabel(int tier) => tier < Party.CpuTiers.Length ? "CPU " + Party.CpuTiers[tier].label : "OFF";

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
                case Opt.Reduce: Settings.ReduceMotion = !Settings.ReduceMotion; break;
                case Opt.Graphics:
                    Settings.Graphics = Settings.Cycle(Settings.Graphics, dir);
                    RenderQuality.Apply(Settings.Graphics);   // live, so the difference shows before leaving
                    break;
                case Opt.Swing: Settings.SwingInputChoice = Settings.Cycle(Settings.SwingInputChoice, dir); break;
                default: return;
            }
            Play(Tones.Blip(880, 0.04f, 60), 0.3f);   // the web's ui sound; also lets the SFX level be heard
        }

        void OptionChoice(int row)
        {
            _optIndex = row;
            switch ((Opt)row)
            {
                case Opt.Reset: _confirmReset = true; break;
                case Opt.Back: _mode = Mode.Title; break;
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
                case Opt.Reduce: return Settings.ReduceMotion ? "ON" : "OFF";
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
                if (p.Action == "pause") { if (!Autoplay) { Pause(); return; } continue; }
                // Mouse and touch are the "a" button in play.
                _game.Press(p.Action == "click" ? "a" : p.Action, p.Audio - _pausedTotal);
            }
            _game.Tick(now, Time.deltaTime);
            var o = _game.Result();
            if (o != null) Finish(o);
        }

        // ------------------------------------------------------------ screens

        void OnGUI()
        {
            Hud.Begin();
            float s = Hud.S;
            switch (_mode)
            {
                case Mode.Gate:
                    Hud.Dim(0.85f);
                    Hud.Center(0.3f, "BONANZA", Hud.Big, Hud.Gold, 120);
                    Hud.Center(0.55f, "CLICK / TAP / SPACE TO START", Hud.Mid, Color.white);
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
                        string best = Save.Best(e.Id);
                        Hud.Text(new Rect(r.x + 16 * s, r.y, r.width, r.height * 0.6f), e.Title, Hud.Mid, sel ? Hud.Gold : Color.white, TextAnchor.MiddleLeft);
                        Hud.Text(new Rect(r.x + 16 * s, r.y + r.height * 0.5f, r.width, r.height * 0.5f), e.Blurb + (best != null ? "   BEST " + best : ""), Hud.Small, new Color(1, 1, 1, 0.8f), TextAnchor.MiddleLeft);
                        if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { _menuIndex = i; Open(e); }
                    }
                    Hud.Center(0.9f, "UP/DOWN to choose   SPACE / click to play   ESC: back", Hud.Small, new Color(1, 1, 1, 0.7f), 40);
                    break;

                case Mode.Title:
                    Hud.Dim(0.85f);
                    Hud.Center(0.14f, "BONANZA", Hud.Big, Hud.Gold, 120);
                    for (int i = 0; i < TitleItems.Length; i++)
                        if (MenuRow(i, 0.4f, TitleItems[i], null, i == _titleIndex, s)) TitleChoice(i);
                    var st = Save.Stats;
                    if (st.Rounds > 0)
                        Hud.Center(0.78f, $"{st.Rounds} ROUNDS   {st.Notes} NOTES   {st.Parties} PARTIES   {st.Wins} WINS", Hud.Small, new Color(1, 1, 1, 0.7f), 40);
                    Hud.Center(0.9f, "UP/DOWN to choose   SPACE / click to select", Hud.Small, new Color(1, 1, 1, 0.7f), 40);
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
                            if (_confirmReset) { if (isReset) Save.ResetProgress(Games.Select(e => e.Id)); _confirmReset = false; }
                            else OptionChoice(i);
                        }
                    }
                    if (_confirmReset) Hud.Center(0.8f, "SPACE: reset     any other key: cancel", Hud.Small, new Color(1, 0.6f, 0.6f, 0.9f), 40);
                    else if (_optIndex == (int)Opt.Graphics)
                        Hud.Center(0.8f, $"Now drawing at {RenderQuality.Level.Scale:0.##}x, {RenderQuality.Level.Tier.ToString().ToUpperInvariant()}{(RenderQuality.Auto ? " (auto: " + RenderQuality.Reason + ")" : "")}", Hud.Small, new Color(1, 1, 1, 0.7f), 40);
                    else if (_optIndex == (int)Opt.Swing)
                        Hud.Center(0.8f, "Tap is the default. Mouse and camera conducting arrive later; until then taps are used.", Hud.Small, new Color(1, 1, 1, 0.7f), 40);
                    else if (_optIndex == (int)Opt.Offset)
                        Hud.Center(0.8f, "Negative offset = your presses land late.\nPress late and see GOOD more than PERFECT? Try -20 ms steps.", Hud.Small, new Color(1, 1, 1, 0.7f), 80);
                    Hud.Center(0.9f, "UP/DOWN choose   LEFT/RIGHT adjust   ESC back", Hud.Small, new Color(1, 1, 1, 0.7f), 40);
                    break;

                case Mode.Roster:
                    Hud.Dim(0.85f);
                    Hud.Center(0.08f, "PARTY", Hud.Big, Hud.Gold, 100);
                    Hud.Center(0.2f, $"{PartyLength} rounds, the finale last. 3 / 2 / 1 / 0 points by place.", Hud.Small, Color.white, 40);
                    MenuRow(-1, 0.34f + 80 * s / Screen.height, CastNames[0], "HUMAN", false, s);
                    for (int i = 1; i < 4; i++)
                        if (MenuRow(i - 1, 0.34f + 80 * s / Screen.height, CastNames[i], SlotLabel(_slotTier[i]), _rosterIndex == i - 1, s))
                        { _rosterIndex = i - 1; CycleSlot(i, 1); }
                    if (MenuRow(3, 0.34f + 80 * s / Screen.height, "START PARTY", null, _rosterIndex == 3, s)) StartParty();
                    Hud.Center(0.9f, "UP/DOWN choose   LEFT/RIGHT change a rival   ESC back", Hud.Small, new Color(1, 1, 1, 0.7f), 40);
                    break;

                case Mode.PartyHub:
                    Hud.Dim(0.88f);
                    DrawPartyHub(s);
                    break;

                case Mode.Rules:
                    Hud.Dim(0.7f);
                    Hud.Center(0.22f, _game.Title, Hud.Big, Hud.Gold, 120);
                    Hud.Center(0.42f, _game.Blurb, Hud.Mid, Color.white);
                    Hud.Center(0.55f, _game.Controls, Hud.Small, Color.white, 80);
                    Hud.Center(0.75f, "PRESS TO START   (ESC: back)", Hud.Mid, Hud.Gold);
                    break;

                case Mode.Playing:
                    _game.DrawHud(s);
                    break;

                case Mode.Paused:
                    _game.DrawHud(s);
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
                    _game.DrawHud(s);
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

        /// Each rank has its own sting, S the full fanfare down to a D's slide off the chord.
        void RankSting(string rank)
        {
            (float at, int midi)[] notes = rank switch
            {
                "S" => new[] { (0f, 72), (0.09f, 76), (0.18f, 79), (0.3f, 84) },
                "A" => new[] { (0f, 72), (0.05f, 76), (0.1f, 79) },
                "B" => new[] { (0f, 72), (0.05f, 76) },
                "C" => new[] { (0f, 67) },
                _ => new[] { (0f, 61), (0.12f, 58) },
            };
            foreach (var (at, midi) in notes)
                PlayAt(Tones.Blip(Tones.Midi(midi), at == 0.3f ? 0.6f : 0.18f, 8, rank == "S" ? Wave.Square : Wave.Sine), Now + 0.02 + at, 0.18f);
        }

        void DrawResults(float s)
        {
            var r = _outcome;
            float t = _resultsT;
            Hud.Dim(0.65f);
            Hud.Center(0.1f, _game.Title, Hud.Mid, Color.white, 60);
            if (t >= StampAt) Hud.Center(0.18f, $"RANK {r.Rank}", Hud.Big, Hud.Gold, 120);
            float k = Mathf.Clamp01((t - CountAt) / CountS);
            int shown = Mathf.RoundToInt(r.Score * (1 - Mathf.Pow(1 - k, 3)));
            var lines = new System.Text.StringBuilder();
            // One sticker, the rank first. A first-ever D is a record, not worth a sticker.
            bool sticker = t >= StampAt + 0.25f && (_newScore || _newRank) && r.Rank != "D" && r.Score > 0;
            lines.Append($"SCORE {shown}   ACCURACY {r.Accuracy * 100:0.0}%{(sticker ? (_newRank ? "   NEW RANK!" : "   NEW BEST!") : "")}\n");
            if (t >= StatsAt)
            {
                lines.Append($"PERFECT {r.Perfect}   GREAT {r.Great}   GOOD {r.Good}   MISS {r.Miss}   MAX COMBO {r.MaxCombo}\n");
                int n = 0;
                foreach (var kv in r.Extras)
                    lines.Append(kv.Key).Append(' ').Append(kv.Value).Append(++n % 3 == 0 ? "\n" : "   ");
            }
            Hud.Center(0.36f, lines.ToString(), Hud.Mid, Color.white, 220);
            if (_party != null)
            {
                var round = _party.Scores.Count > 0 ? _party.Scores[_party.Scores.Count - 1] : null;
                if (round != null && round.Game == _entry.Id && t >= PartyAt)
                {
                    var rows = round.Players.OrderBy(p => p.Place).Select(p =>
                    {
                        bool tied = round.Players.Count(q => q.Place == p.Place) > 1;
                        var who = _party.Players[p.Id];
                        return $"{(tied ? "T-" : "")}{PlaceName[p.Place - 1]} {who.Name}{(who.IsCpu ? "" : " (YOU)")} +{p.Points}";
                    });
                    Hud.Center(0.68f, string.Join("     ", rows), Hud.Small, Color.white, 40);
                }
                if (t >= ResultsLock) Hud.Center(0.82f, Autoplay ? "(autoplay)" : "SPACE: standings", Hud.Mid, Hud.Gold, 60);
                return;
            }
            if (t < ResultsLock) return;
            Hud.Center(0.82f, Autoplay ? "(autoplay)" : "SPACE: play again   ESC: menu", Hud.Mid, Hud.Gold, 60);
            if (!Autoplay)
            {
                var again = new Rect(Screen.width * 0.25f, Screen.height * 0.9f, Screen.width * 0.2f, 50 * s);
                var menu = new Rect(Screen.width * 0.55f, Screen.height * 0.9f, Screen.width * 0.2f, 50 * s);
                Hud.Box(again, new Color(1, 1, 1, 0.12f)); Hud.Text(again, "AGAIN", Hud.Small, Color.white);
                Hud.Box(menu, new Color(1, 1, 1, 0.12f)); Hud.Text(menu, "MENU", Hud.Small, Color.white);
                if (GUI.Button(again, GUIContent.none, GUIStyle.none)) Open(_entry);
                else if (GUI.Button(menu, GUIContent.none, GUIStyle.none)) { CloseGame(); _mode = Mode.Menu; }
            }
        }

        /// One selectable row: label left, value right. Row i sits at y0 + i rows. True if clicked.
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
                var wins = _party.Winners.Select(id => _party.Players[id].Name).ToList();
                Hud.Center(0.04f, "PARTY COMPLETE", Hud.Small, Color.white, 40);
                Hud.Center(0.1f, wins.Count > 1 ? string.Join(" & ", wins) + " SHARE THE CROWN!" : wins[0] + " WINS!", Hud.Big, Hud.Gold, 100);
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
