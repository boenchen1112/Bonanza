// Finale Fever, the tempo game: tap on every beat of a hymn while its tempo moves under you (gentle ramps, then ups
// and downs, then jumps and held notes), and in places with the conductor's baton hidden, keep the beat by ear. The
// rules live in Bonanza.Core (TempoRound, TempoCurve); this file is the wiring, the sound and the round's events.
// The stage (a concert hall seen from the orchestra: the maestro on his podium, the timpani section, the house) is
// FinaleFeverSet.cs, the HUD (tempo gauge, timing meter, hearts, line pips) FinaleFeverHud.cs. No rule logic lives there.
//
// The music is a rendered clip per excerpt (Tools/hymns/render-hymns.mjs: the hymn's melody laid on the excerpt's
// tempo curve, the very curve the round judges against), decoded in Load() and played through Host.PlayClip like
// Swing Kings' hymns. A build without the private hymn data faults cleanly ("hymn data missing").
//
// HOOKS for the visual work (all read-only, valid after Begin):
//   Round         the TempoRound (Slots, Hearts, BiasText(), ...)         ExcerptIndex   0..2, 3 after the last
//   Slot          the excerpt on screen (its Curve, Beats, Level, Name)   Beat           curve beats since the excerpt's first beat (fractional; negative in the count-in)
//   CurveBpm      the tempo as heard now                                  PulseHidden    true while the stroke that lands next has no baton: draw no beat cue at all
//   Hearts        hearts left (0 = SURVIVE)                               LastCall / LastVerdict / LastErrMs / TapSerial  the last tap (TapSerial counts up on every judged tap)
// The stage and the HUD read them in Update().

using System.Collections.Generic;
using System.Linq;
using Bonanza.Core;
using Bonanza.HudKit;
using Bonanza.Look;
using UnityEngine;

namespace Bonanza
{
    public sealed partial class FinaleFeverGame : Minigame
    {
        public override string Id => "finale-fever";
        public override string Title => "FINALE FEVER";
        public override string Blurb => "Stay on the beat while the tempo moves.";
        public override bool OwnsMusic => true;   // the hymns' tempo clips (HymnData), through Host.PlayClip
        protected override bool UseLook => true;
        protected override string PaletteName => PaletteFor(HymnLevel.Easy);
        public override bool UseCanvasHud => true;
        public override string Controls => "A hymn plays and the tempo moves: speeding up, slowing down, jumping, holding a note.\nSPACE / click / tap on EVERY beat. Each tap says EARLY, LATE or ON.\nSometimes the baton disappears: keep the beat by ear.\nThree hearts: a line you mostly miss costs one, a clean line wins one back.";

        // ------------------------------------------------------------ constants a human tunes by ear
        const double TapGain = 0.12;          // the tap tick's gain
        const double PreRoll = 0.5;           // the clip is handed to the music deck this long before its first beat
        const int LeadBeats = 4;              // count-in beats before the first excerpt (the later ones get TempoRound.GapBeats)
        const double ResultHold = 3.2;        // seconds of finale (the bow, the ovation, the lean gauge) before the results card

        static readonly Color Gold = Hex(0xffd93d), Red = Hex(0xff5d73);

        TempoRound _round;
        HymnData _data;
        readonly AudioClip[] _clips = new AudioClip[TempoRound.ExcerptCount];
        IReadOnlyList<int> _ids;
        bool _playing, _resultOut;
        double _now, _doneAt = -1;
        int _musicSlot = -1, _stoppedSlot = -1;
        int _tickCursor;
        readonly List<CountTick> _ticks = new List<CountTick>();
        bool _surviveShown;

        struct CountTick { public double Time; public int Accent; public bool Say; public string Text; public double Len; }

        // ------------------------------------------------------------ hooks

        public TempoRound Round => _round;
        public int ExcerptIndex { get; private set; }
        public TempoSlot Slot => _round != null && ExcerptIndex < _round.Slots.Count ? _round.Slots[ExcerptIndex] : null;
        /// Curve beats since the excerpt's first beat, fractional (negative through the count-in).
        public double Beat { get; private set; }
        public double CurveBpm { get; private set; }
        /// True while the stroke that lands next is hidden: the stage must show no beat cue (no baton, no pulse).
        public bool PulseHidden { get; private set; }
        public int Hearts => _round != null ? _round.Hearts : 3;
        public TapCall? LastCall { get; private set; }
        public Verdict LastVerdict { get; private set; }
        public double LastErrMs { get; private set; }
        public int TapSerial { get; private set; }

        /// The pause menu counts back in at the beat now playing.
        public override double SecondsPerBeat
        {
            get
            {
                var s = Slot;
                if (s == null || s.Curve == null) return 0.5;
                int i = Mathf.Clamp((int)System.Math.Floor(System.Math.Max(0, Beat)), 0, s.Curve.BeatCount - 1);
                return System.Math.Max(0.25, System.Math.Min(0.5, s.Curve.LengthOf(i)));   // never slower than half a second: the count back in stays short
            }
        }

        // The SFX chord lookup (Host.PlaySfx) wants a beat for the clock: beats counted through the round's excerpts.
        public override double MusicBeatAt(double time)
        {
            if (_round == null || _round.Slots.Count == 0) return time * 2;
            double beats = 0;
            foreach (var s in _round.Slots)
            {
                if (time < s.EndTime || s == _round.Slots[_round.Slots.Count - 1]) return beats + s.Curve.BeatAt(time - s.StartTime);
                beats += s.Curve.BeatCount;
            }
            return beats;
        }

        public override double MusicTimeAt(double beat)
        {
            if (_round == null || _round.Slots.Count == 0) return beat / 2;
            foreach (var s in _round.Slots)
            {
                if (beat < s.Curve.BeatCount || s == _round.Slots[_round.Slots.Count - 1]) return s.StartTime + s.Curve.TimeAt(beat);
                beat -= s.Curve.BeatCount;
            }
            return 0;
        }

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

        // ------------------------------------------------------------ contract

        public override void Load()
        {
            LoadHymns();   // first: a build without the private hymns fails here, before the stage is built
            BuildStage();
            BuildHud();
            Look.Fx.Prewarm();   // touch every FX material now: a first-use shader link mid-round would push a tap out of its window
            _round.Tapped += OnTapped;
            _round.Missed += OnMissed;
            _round.HeartLost += OnHeartLost;
            _round.HeartGained += OnHeartGained;
            _round.ExcerptStarted += OnExcerptStarted;
            _round.LineClosed += OnLineClosed;
            _round.RoundDone += OnRoundDone;
        }

        static string PaletteFor(HymnLevel level) => "finale-fever-" + level.ToString().ToLowerInvariant();

        /// The tempo pool, the three excerpts and their clips (decoded now, never mid-round).
        void LoadHymns()
        {
            _data = HymnData.Load();   // throws "hymn data missing: ..." for the host's fault path
            if (_data.Problems.Count > 0) Debug.LogWarning("[ERROR] hymn data: " + string.Join("; ", _data.Problems));
            if (_data.TempoPool.Count == 0)
                throw new System.InvalidOperationException("hymn data missing: no tempo clips (" + string.Join("; ", _data.TempoProblems) + "); run Tools/hymns/prepare-data.sh");
            if (_data.TempoProblems.Count > 0) Debug.LogWarning("[ERROR] hymn data: " + string.Join("; ", _data.TempoProblems));
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
            _round = new TempoRound(_data.TempoPool, seed, skills);
            for (int k = 0; k < TempoRound.ExcerptCount; k++)
            {
                string name = TempoRound.ClipName(_round.Playlist[k], TempoRound.SlotLevels[k]);
                _clips[k] = HymnData.Clip(name);
                if (_clips[k] == null) throw new System.InvalidOperationException("hymn data missing: clip " + name);
                _clips[k].LoadAudioData();
            }
        }

        public override void Begin(double now)
        {
            _round.Start(now);
            double lead = LeadBeats * _round.Slots[0].Curve.LengthOf(0);
            _round.Start(now + lead);   // the first excerpt waits for its count-in
            _now = now;
            _playing = true;
            BuildTicks();
            BeginStage();
            BeginHud();
            BuildShots();
        }

        public override void Press(string action, double time)
        {
            if (!_playing || action != "a") return;
            time += Save.OffsetMs / 1000.0;
            Host.PlaySfx("tick", time, gain: (float)TapGain);
            _round.Press(action, time);
            StagePressed();
        }

        public override void Tick(double now, float dt)
        {
            _now = now;
            _round.Update(now);
            DriveMusic(now);
            DriveTicks(now);
            Follow(now);
            UpdateScore();
            DriveShots(now);
        }

        public override Outcome Result()
        {
            if (!_round.Done) return null;
            if (_doneAt < 0) { _doneAt = _now; Host.PlaySfx(_round.Survived ? "impact" : "fanfare", _now); }
            if (_now - _doneAt < ResultHold) return null;
            _playing = false;
            _resultOut = true;
            return _round.Result(_ids);
        }

        /// The results card shows the tempo game's own numbers: taps ON, EARLY, LATE, beats missed and the average lean.
        public override List<KeyValuePair<string, string>> ResultsStats(Outcome o)
        {
            if (_round == null || !_round.Done) return null;
            int ms = Mathf.RoundToInt((float)System.Math.Abs(_round.BiasMs));
            string lean = ms == 0 ? "ON" : (_round.BiasMs < 0 ? "-" : "+") + ms + " MS";
            return new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("ON", _round.On.ToString()),
                new KeyValuePair<string, string>("EARLY", _round.Early.ToString()),
                new KeyValuePair<string, string>("LATE", _round.Late.ToString()),
                new KeyValuePair<string, string>("MISSED", _round.Miss.ToString()),
                new KeyValuePair<string, string>("AVG LEAN", lean),
            };
        }

        /// The card centred with a filled rank stamp, one pip per excerpt (gold clean, mint kept, coral a heart lost in it),
        /// and the round's lead / lag as an EARLY | ON | LATE gauge (ResultsCard's opt-in options).
        public override void ResultsStyle(Bonanza.HudKit.ResultsData d)
        {
            d.Centered = true; d.RankFilled = true; d.BadgeOnRank = true;
            d.DimAlpha = 0.34f;   // the stalls stay readable behind the card
            if (_round == null) return;
            // "vs the maestro": the maestro set every beat, so the comparison is beats set against beats followed (no invented scores)
            int total = _round.On + _round.Early + _round.Late + _round.Miss;
            if (total > 0)
            {
                d.Extras.Clear();
                d.Extras.Add(new KeyValuePair<string, string>("MAESTRO SET", total.ToString()));
                d.Extras.Add(new KeyValuePair<string, string>("YOU FOLLOWED", (total - _round.Miss).ToString()));
                d.Extras.Add(new KeyValuePair<string, string>("IN STEP", Mathf.RoundToInt(100f * _round.On / total) + "%"));
            }
            d.Pips = new List<Color>();
            foreach (var s in _round.Slots)
            {
                bool lost = false, missed = false;
                foreach (var l in s.Lines) { if (l.HeartDelta < 0) lost = true; if (l.Misses > 0) missed = true; }
                d.Pips.Add(lost ? Hex(0xff5d73) : missed ? Hex(0x7df0a0) : Hex(0xffd93d));
            }
            double good = BeatWindow.GoodMs(0.6);
            d.Lean = Mathf.Clamp((float)(_round.BiasMs / good), -1, 1);
            int ms = Mathf.RoundToInt((float)System.Math.Abs(_round.BiasMs));
            d.LeanLabel = ms == 0 ? "YOUR AVERAGE: DEAD ON THE BEAT" : _round.BiasMs < 0 ? $"YOUR AVERAGE: {ms} MS AHEAD OF THE BEAT" : $"YOUR AVERAGE: {ms} MS BEHIND THE BEAT";
        }

        public override IEnumerable<KeyValuePair<string, double>> TestChart()
        {
            bool die = ShotMode("ffdie");
            if (!ShotMode("ffmiss") && !die)
            {
                foreach (var p in _round.PerfectPresses()) yield return new KeyValuePair<string, double>(p.Action, p.Time);
                yield break;
            }
            // Capture only (?ffmiss in the URL; ?ffdie drops all of the first excerpt so the hearts run out, never in a gate): a sloppy bot that leans EARLY / LATE and drops whole
            // lines, so the heart loss, the red room, SURVIVE and a heart won back can be photographed.
            foreach (var s in _round.Slots)
                foreach (var b in s.Beats)
                {
                    bool drop = die ? (s.Index == 0 || (s.Index == 1 && b.Line == 0))
                        : (s.Index == 0 && b.Line == 1) || (s.Index == 1 && (b.Line == 0 || b.Line == 2)) || (s.Index == 2 && b.Line == 1);
                    if (drop) continue;
                    double good = BeatWindow.GoodMs(b.Length) / 1000.0;
                    double off = b.Index % 3 == 1 ? -0.5 * good : b.Index % 3 == 2 ? 0.45 * good : 0;
                    yield return new KeyValuePair<string, double>("a", b.Time + off);
                }
        }

        protected override void OnDestroy()
        {
            foreach (var c in _clips) if (c != null) c.UnloadAudioData();
            DestroyStage();
            base.OnDestroy();
        }

        // ------------------------------------------------------------ the round's clock, sound

        /// The count-in ticks: LeadBeats before the first excerpt, GapBeats before each later one, at that excerpt's
        /// own opening tempo (so the new tempo is heard before the first tap).
        void BuildTicks()
        {
            _ticks.Clear();
            _tickCursor = 0;
            foreach (var s in _round.Slots)
            {
                int n = s.Index == 0 ? LeadBeats : TempoRound.GapBeats;
                double len = s.Curve.LengthOf(0);
                for (int j = n; j >= 1; j--)
                    _ticks.Add(new CountTick { Time = s.StartTime - j * len, Accent = n - j == 0 ? 0 : 1, Say = s.Index == 0, Text = s.Index == 0 ? j.ToString() : null, Len = len });
            }
        }

        void DriveTicks(double now)
        {
            while (_tickCursor < _ticks.Count && _ticks[_tickCursor].Time <= now + 0.15)
            {
                var t = _ticks[_tickCursor++];
                Host.CountTick(t.Time, t.Accent);
                if (t.Say && t.Time > now - 0.2) Ui.Countdown(t.Text, 0.28f, 2.9f, (float)System.Math.Max(0.3, t.Len - 0.04), Hex(0xfff2b0));   // lasts until the next tick, so it never hides the tempo
            }
        }

        /// Each excerpt's clip is handed to the deck PreRoll before its first beat: beat 0 sounds at the slot's start
        /// (the file holds `lead` seconds of silence and the graph's own offset before it), and it stops after the last beat.
        void DriveMusic(double now)
        {
            int k = _musicSlot + 1;
            if (k < _round.Slots.Count && now >= _round.Slots[k].StartTime - PreRoll)
            {
                var s = _round.Slots[k];
                Host.PlayClip(_clips[k], s.StartTime, _data.BeatOffset + _data.TempoLead(s.Clip));
                _musicSlot = k;
            }
            if (_musicSlot >= 0 && _stoppedSlot < _musicSlot && now > _round.Slots[_musicSlot].EndTime + 0.05)
            {
                _stoppedSlot = _musicSlot;
                Host.MusicStop(0.4f);
            }
        }

        /// Which excerpt is on screen, where its curve is, and whether the baton shows.
        void Follow(double now)
        {
            if (_round.Slots.Count == 0) return;
            int k = 0;
            while (k < _round.Slots.Count - 1 && now >= _round.Slots[k].EndTime) k++;
            ExcerptIndex = k;
            var s = _round.Slots[k];
            Beat = s.Curve.BeatAt(now - s.StartTime);
            CurveBpm = s.Curve.BpmAt(Beat);
            int landing = (int)System.Math.Floor(Beat) + 1;   // the stroke now in the air lands on this beat
            PulseHidden = landing >= 0 && landing < s.Beats.Count && s.Beats[landing].Hidden && now >= s.StartTime - 1e-9;
        }

        /// The palette the hall should be in now: the excerpt's level, red on the last heart (or none), hushed while the
        /// baton is hidden.
        string MoodPalette()
        {
            var s = Slot;
            int lvl = s != null ? (int)s.Level : 0;
            bool red = _round != null && _round.Started && Hearts <= 1 && !_round.Done;
            bool hush = PulseHidden && _playing && !_round.Done;
            return PaletteNames[lvl * 4 + (red ? 2 : 0) + (hush ? 1 : 0)];
        }

        static readonly string[] PaletteNames =
        {
            "finale-fever-easy", "finale-fever-easy-hush", "finale-fever-easy-red", "finale-fever-easy-red-hush",
            "finale-fever-medium", "finale-fever-medium-hush", "finale-fever-medium-red", "finale-fever-medium-red-hush",
            "finale-fever-hard", "finale-fever-hard-hush", "finale-fever-hard-red", "finale-fever-hard-red-hush",
        };

        // ------------------------------------------------------------ round events

        void OnExcerptStarted(TempoSlot slot)
        {
            StageExcerpt(slot);
            HudExcerpt(slot);
        }

        void OnTapped(TempoBeat b)
        {
            LastCall = b.Call;
            LastVerdict = b.Verdict;
            LastErrMs = b.ErrMs;
            TapSerial++;
            MusicJudged(true);
            StageTapped(b);
            HudTapped(b);
        }

        void OnMissed(TempoBeat b)
        {
            LastCall = null;
            LastVerdict = Verdict.Miss;
            TapSerial++;
            MusicJudged(false);
            Host.PlaySfx("miss", _now);
            StageMissed(b);
            HudMissed(b);
        }

        void OnLineClosed(TempoLine line)
        {
            StageLine(line);
            HudLine(line);
        }

        void OnHeartLost(int left)
        {
            Host.PlaySfx(left == 0 ? "impact" : "miss", _now);
            StageHeart(-1, left);
            HudHeart(-1, left);
            Shot("heartloss", 0.3);
            if (left == 1) Shot("red", 1.8);
            if (_round.Survived && left == 0 && !_surviveShown)
            {
                _surviveShown = true;
                Ui.Banner("SURVIVE!", "NO HEARTS LEFT - KEEP THE BEAT", Red, 1.6f, plate: true, y: 0.215f, scale: 0.7f);
                Shot("survive", 0.45);
                Shot("survivelater", 3.0);
            }
        }

        void OnHeartGained(int left)
        {
            Host.PlaySfx("coin", _now);
            StageHeart(+1, left);
            HudHeart(+1, left);
            Shot("heartgain", 0.35);
        }

        void OnRoundDone()
        {
            StageFinale();
            HudFinale();
            Shot("finale", 1.5);
            Shot("finalelate", 2.9);
        }

        void UpdateScore()
        {
            Ui.Score(Mathf.RoundToInt((float)(_round.Accuracy * 1000)));
            Ui.Combo(_round.Combo);
            Ui.Accuracy((float)_round.Accuracy);
        }

        void Update()
        {
            if (_round == null || !_round.Started || _round.Slots.Count == 0) return;
            float dt = LookRig.VisualDt(Time.deltaTime);
            UpdateStage(dt);
            UpdateHud(dt);
        }

        // ------------------------------------------------------------ capture marks (?ffshot in the URL only)
        //
        // The visual review photographs the moments that matter (count-in, a steady line, a ramp, a jump, a fermata, a
        // hidden stretch, a heart lost, SURVIVE, an excerpt change, the finale). The game prints "[FFSHOT] <tag>" at each
        // and the capture script screenshots on the line. Nothing is printed without the flag.

        struct ShotMark { public double T; public string Tag; }
        readonly List<ShotMark> _shots = new List<ShotMark>();
        bool _shotOn;

        static bool ShotMode(string flag)
        {
            string u = Application.absoluteURL;
            return !string.IsNullOrEmpty(u) && u.Contains(flag);
        }

        void Shot(string tag, double delay)
        {
            if (_shotOn) _shots.Add(new ShotMark { T = _now + delay, Tag = tag + "-" + ExcerptIndex });
        }

        void BuildShots()
        {
            _shotOn = ShotMode("ffshot");
            if (!_shotOn) return;
            var s0 = _round.Slots[0];
            _shots.Add(new ShotMark { T = s0.StartTime - 2.75 * s0.Curve.LengthOf(0), Tag = "count" });
            foreach (var s in _round.Slots)
            {
                string L = s.Level.ToString().ToLowerInvariant();
                _shots.Add(new ShotMark { T = s.StartTime + s.Curve.TimeAt(5.3), Tag = L + "-steady" });
                if (s.Index > 0) _shots.Add(new ShotMark { T = s.StartTime - 1.2 * s.Curve.LengthOf(0), Tag = L + "-excerpt" });
                if (s.HiddenStretches.Count > 0)
                {
                    var h = s.HiddenStretches[0];
                    _shots.Add(new ShotMark { T = s.StartTime + s.Curve.TimeAt(h.First + 1.5), Tag = L + "-hidden" });
                }
                double at = 0;
                bool ramp = false, jump = false, ferm = false;
                foreach (var g in s.Curve.Segments)
                {
                    if (g.Kind == TempoKind.Ramp && !ramp && g.Beats >= 6 && at > 6) { ramp = true; _shots.Add(new ShotMark { T = s.StartTime + s.Curve.TimeAt(at + g.Beats * 0.6 + 0.3), Tag = L + "-ramp" }); }
                    if (g.Kind == TempoKind.Jump && !jump && at > 6) { jump = true; _shots.Add(new ShotMark { T = s.StartTime + s.Curve.TimeAt(at - 1.6), Tag = L + "-jumpahead" }); _shots.Add(new ShotMark { T = s.StartTime + s.Curve.TimeAt(at + 1.3), Tag = L + "-jump" }); }
                    if (g.Kind == TempoKind.Fermata && !ferm) { ferm = true; _shots.Add(new ShotMark { T = s.StartTime + s.Curve.TimeAt(at + 0.5), Tag = L + "-fermata" }); }
                    at += g.Beats;
                }
            }
        }

        void DriveShots(double now)
        {
            if (!_shotOn) return;
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                if (_shots[i].T > now) continue;
                Debug.Log("[FFSHOT] " + _shots[i].Tag);
                _shots.RemoveAt(i);
            }
        }

        // The HUD is on the canvas (UseCanvasHud, FinaleFeverHud.cs): no DrawHud.
    }
}
