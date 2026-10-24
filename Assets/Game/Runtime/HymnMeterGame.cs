// Bounce Brigade (the game id and name stay): hear a hymn's last lines, find the downbeat, conduct it.
// The jumper hops on every beat by itself. Answer with the arrow keys, one per beat, starting on the beat
// you think is the DOWNBEAT with the first key of the meter's pattern: DOWN starts 4 boxes (down left right
// up), LEFT starts 3 (left right up), RIGHT starts 2 (right up). Each following on-beat key fills the next
// box; a wrong key or a missed beat empties them. Once the boxes are full every beat lights one box (box 1 on
// the beat you claimed), looping with the music. BACKSPACE (b: X, K) clears; ENTER (a: Space, Z, J) submits a
// full answer and locks it. Right = the hymn's pattern AND box 1 on a true downbeat. The steps under the
// jumper (heavier = lower: 4 beats 1 3 2 3, 3 beats 1 3 3, 2 beats 1 3) hide under cloud that lifts at the
// halfway point of the excerpt or once everyone has submitted; a submission after that ranks below all earlier
// ones and scores a quarter.
//
// The rules live in Bonanza.Core (Meter, MeterAnswer, HymnRound, ...); this is the wiring. The stage (sky,
// cloud sea, the columns, the jumper, the cloud choir, the camera) is HymnMeterSet.cs, the canvas HUD (answer
// boxes, the reveal gauge, per-excerpt results) is HymnMeterHud.cs.
// The grid is built from the hymn's MIDI + tempo map (HymnData) and the audio was rendered from the same
// pair (Tools/hymns), so the beats you hear are the beats judged. A build without the private hymn data
// (a fresh clone) fails Load() with "hymn data missing" and the host returns to the title.
//
// Hooks for the visual side: Round / CurrentSlot / Shown / BeatIndex / BeatPhase / BarPosition / BeatsPerBar /
// StepHeight (the shown beat's, Shown.Beats[i].StepHeight for any beat) / Revealed / Answer (the player's boxes:
// Count, Filled, KeyAt(i), Full, Locked, SubmitTime, StartBeat) / LitBox (the box that flickers now, -1 if none),
// and the step colours by pattern key (KeyColor).
//
// Nothing on screen may tell a downbeat from another beat before the reveal: the jumper's hops, the camera's beat
// push and the choir's bob are the same on every beat until the cloud lifts.
//
// Time: everything is game time (the audio clock); presses arrive in game time.

using System.Collections.Generic;
using System.Linq;
using Bonanza.Core;
using UnityEngine;

namespace Bonanza
{
    public sealed partial class HymnMeterGame : Minigame
    {
        public override string Id => "bounce-brigade";
        public override string Title => "BOUNCE BRIGADE";
        public override string Blurb => "Find the downbeat. Conduct the hymn.";
        public override string Controls => "Six hymn excerpts. Listen: where does the bar start?\nOn the DOWNBEAT press the first key of the pattern, then one key per beat:\n4 beats: DOWN LEFT RIGHT UP   -   3 beats: LEFT RIGHT UP   -   2 beats: RIGHT UP\nA wrong key or a missed beat empties the boxes. BACKSPACE clears them.\nWhen they are full press ENTER to lock your answer. The earlier you are right, the more you score.";
        public override bool OwnsMusic => true;
        protected override bool UseLook => true;
        public override bool UseCanvasHud => true;
        protected override string PaletteName => PaletteFor(HymnLevel.Easy);

        const double PreRoll = 0.5;      // the clip starts this long before the first beat (the hymn leading in)
        const double ResultHold = 3.0;   // the last answer and the six-excerpt summary stay on screen this long before the results card

        // ------------------------------------------------------------ hooks for the visual side

        public HymnRound Round => _round;
        /// The excerpt being played (or about to be), null before Begin and after the last.
        public HymnSlot CurrentSlot => _round != null && _round.Current >= 0 && _round.Current < _round.Slots.Count ? _round.Slots[_round.Current] : null;
        /// The excerpt whose steps are on screen: the one being played, and after its window the one just played until
        /// the next begins (so its answer and reveal stay visible through the gap).
        public HymnSlot Shown => _round != null && _layoutSlot >= 0 && _layoutSlot < _round.Slots.Count ? _round.Slots[_layoutSlot] : null;
        /// Index into Shown.Beats of the latest beat that has sounded, -1 before the first.
        public int BeatIndex { get; private set; } = -1;
        /// 0..1 from that beat to the next.
        public float BeatPhase { get; private set; }
        public int BarPosition => BeatIndex >= 0 && Shown != null ? Shown.Beats[BeatIndex].BarPosition : -1;
        public int BeatsPerBar => BeatIndex >= 0 && Shown != null ? Shown.Beats[BeatIndex].BeatsPerBar : 0;
        /// The cloud is off the shown excerpt's steps.
        public bool Revealed => Shown != null && Shown.Revealed;
        /// Step height of the latest beat that has sounded, 1 = lowest = heaviest (4 beats 1 3 2 3, 3 beats 1 3 3, 2 beats 1 3), 0 before the first.
        public int StepHeight => BeatIndex >= 0 && Shown != null ? Shown.Beats[BeatIndex].StepHeight : 0;
        /// The player's boxes on the shown excerpt (Count, Filled, KeyAt, Full, Locked, SubmitTime, StartBeat), null before the first.
        public MeterAnswer Answer => Shown?.Answer;
        /// The box that lights at the current time once the boxes are full (box 1 on the beat claimed as the downbeat), -1 if none.
        public int LitBox => Answer != null ? Answer.LitBox(_now) : -1;

        /// A step's (and an answer box's) colour for a pattern key: down amber, left azure, right rose, up green.
        public static Color KeyColor(string key) => key == "down" ? Hex(0xffb21e) : key == "left" ? Hex(0x2f9bff) : key == "right" ? Hex(0xff4f9a) : Hex(0x3fd46c);

        /// The palette of a level: Easy dawn, Medium full day, Hard vespers dusk.
        static string PaletteFor(HymnLevel l) => l == HymnLevel.Easy ? "bounce-brigade-easy" : l == HymnLevel.Medium ? "bounce-brigade-medium" : "bounce-brigade-hard";

        // ------------------------------------------------------------ state

        HymnData _data;
        HymnRound _round;
        readonly AudioClip[] _clips = new AudioClip[HymnRound.ExcerptCount];
        readonly int[] _beatCounts = new int[HymnRound.ExcerptCount];
        IReadOnlyList<int> _ids;
        int _musicSlot = -1, _stoppedSlot = -1, _layoutSlot = -1;
        double _now, _doneAt = -1;
        bool _playing, _resultOut;
        static readonly string[] KeyOrder = { "down", "left", "right", "up" };

        public override double SecondsPerBeat
        {
            get
            {
                var s = CurrentSlot ?? (_round != null && _round.Slots.Count > 0 ? _round.Slots[0] : null);
                // the pause menu counts back in at this tempo: a hymn's own beat when quick, never slower than half a second
                return s != null && s.Beats.Count > 1 ? System.Math.Max(0.25, System.Math.Min(0.5, s.Beats[1].Time - s.Beats[0].Time)) : 0.5;
            }
        }

        // The SFX chord lookup (Host.PlaySfx) wants a beat for the clock; the hymns have no fixed one.
        public override double MusicBeatAt(double time) => time * 2;
        public override double MusicTimeAt(double beat) => beat / 2;

        // ------------------------------------------------------------ contract

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
            _round = new HymnRound(_data.Pool, seed, skills);
            _round.ExcerptStarted += OnStarted;
            _round.ExcerptRevealed += OnRevealed;
            _round.ExcerptJudged += OnJudged;

            // Prewarm: every clip of the round is decoded before the rules card lets go (Load, never mid-round).
            var meters = new List<string>();
            for (int k = 0; k < HymnRound.ExcerptCount; k++)
            {
                var cand = _data.Pool.First(c => c.Name == _round.Playlist[k]);
                var level = HymnLadder.SlotLevels[k];
                string name = cand.ClipFor(level);
                _clips[k] = HymnData.Clip(name);
                if (_clips[k] == null) throw new System.InvalidOperationException("hymn data missing: clip " + name);
                _clips[k].LoadAudioData();
                // The stage is sized for the longest excerpt now, so nothing is built mid-round.
                _beatCounts[k] = cand.TryCut(level, out var cut, out _) && cut != null ? cut.Beats.Count : 48;
                meters.Add($"{cand.Name}:{cand.Pattern}{(cand.Pickup ? "p" : "")}:{level.ToString().ToLowerInvariant()}:{_beatCounts[k]}");
            }
            Debug.Log("[HYMN] playlist " + string.Join(" ", meters));

            BuildStage();
            BuildHud();
        }

        public override void Begin(double now)
        {
            _round.Start(now + 2.0);   // a short silence before the first hymn leads in
            _now = now;
            _playing = true;
            BuildPath();
            BeginHud();
        }

        public override void Press(string action, double time)
        {
            if (!_playing || _round.Done) return;
            double t = time + Save.OffsetMs / 1000.0;
            var ans = CurrentSlot?.Answer;
            int before = ans != null ? ans.Filled : 0;
            bool wasLocked = ans != null && ans.Locked;
            if (!_round.Press(action, t)) return;   // not an arrow, clear or submit: nothing to answer with
            HudPressed(action, ans, before, wasLocked);
            int ki = System.Array.IndexOf(KeyOrder, action);
            if (ki < 0) return;   // clear and submit make no tick
            Host.PlaySfx("tick", time, gain: 0.12f);
        }

        public override void Tick(double now, float dt)
        {
            _now = now;
            _round.Update(now);
            DriveMusic(now);
            UpdateBeat(now);
        }

        public override Outcome Result()
        {
            if (_round == null || !_round.Done) return null;
            if (_doneAt < 0) { _doneAt = _now; RoundSummary(); }
            if (_now - _doneAt < ResultHold) return null;
            _playing = false;
            _resultOut = true;
            return _round.Result(_ids);
        }

        /// The results card shows this game's own numbers, not the judge's (it has no PERFECT/GREAT/GOOD or combo):
        /// bars found, firsts (a party only), the share of the early bonus kept, the average time to lock a right
        /// answer, and the longest run of right answers.
        public override List<KeyValuePair<string, string>> ResultsStats(Outcome o)
        {
            if (_round == null || !_round.Done) return null;
            int found = 0, firsts = 0, streak = 0, best = 0, locks = 0;
            double got = 0, ceil = 0, lockSum = 0;
            foreach (var s in _round.Slots)
            {
                var j = s.Judgement;
                if (j == null) continue;
                var me = j.Results[0];
                got += j.HumanPoints; ceil += HymnRound.CeilingPoints(s);
                if (me.Correct)
                {
                    found++; streak++; best = System.Math.Max(best, streak);
                    if (j.Winner == 0) firsts++;
                    if (!double.IsInfinity(me.SubmitTime)) { lockSum += me.SubmitTime - s.WindowStart; locks++; }
                }
                else streak = 0;
            }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var list = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("BARS FOUND", $"{found}/{_round.Slots.Count}") };
            if (_round.CpuCount > 0) list.Add(new KeyValuePair<string, string>("FIRSTS", $"{firsts}/{_round.Slots.Count}"));
            list.Add(new KeyValuePair<string, string>("BONUS KEPT", ceil > 0 ? Mathf.RoundToInt((float)(100 * got / ceil)) + "%" : "-"));
            list.Add(new KeyValuePair<string, string>("AVG LOCK", locks > 0 ? (lockSum / locks).ToString("0.0", inv) + "S" : "-"));
            list.Add(new KeyValuePair<string, string>("STREAK", best.ToString(inv)));
            return list;
        }

        /// The card centred, a filled rank stamp with the sticker pinned to it, and the six excerpts as pips (gold
        /// first, mint right, coral wrong, grey no answer), like the strip's.
        public override void ResultsStyle(Bonanza.HudKit.ResultsData d)
        {
            d.Centered = true; d.RankFilled = true; d.BadgeOnRank = true; d.BadgeRibbon = true;
            if (_round == null) return;
            d.Pips = new List<Color>();
            foreach (var s in _round.Slots) d.Pips.Add(PipColor(s));
        }

        public override IEnumerable<KeyValuePair<string, double>> TestChart()
        {
            foreach (var p in _round.PerfectPresses()) yield return new KeyValuePair<string, double>(p.Action, p.Time);
        }

        protected override void OnDestroy()
        {
            foreach (var c in _clips) if (c != null) c.UnloadAudioData();
            DestroyStage();
            base.OnDestroy();
        }

        // ------------------------------------------------------------ music

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
            if (_musicSlot >= 0 && _stoppedSlot < _musicSlot && now > _round.Slots[_musicSlot].WindowEnd + 0.05)
            {
                _stoppedSlot = _musicSlot;
                Host.MusicStop(0.4f);
            }
        }

        // ------------------------------------------------------------ round events

        void OnStarted(HymnSlot slot)
        {
            _layoutSlot = slot.Index;
            Debug.Log($"[HYMN] start {slot.Index} {slot.Name} pattern={slot.Pattern} level={slot.Level} beats={slot.Beats.Count}");
            if (Look.Palette.Name != PaletteFor(slot.Level)) Look.SetPalette(PaletteFor(slot.Level), 2.2f);
            HudStarted(slot);
        }

        void OnRevealed(HymnSlot slot)
        {
            Debug.Log($"[HYMN] reveal {slot.Index}");
            StageRevealed(slot);
            HudRevealed(slot);
        }

        void OnJudged(HymnSlot slot, HymnJudgement j)
        {
            var me = j.Results[0];
            Debug.Log($"[HYMN] judged {slot.Index} correct={me.Correct} post={me.PostReveal} winner={j.Winner} points={j.HumanPoints:0}");
            if (!me.Correct) Host.PlaySfx("miss", _now + 0.01);
            MusicJudged(me.Correct);
            // the sky turns toward the next excerpt's level during the gap, so it has arrived when the hymn begins
            if (slot.Index + 1 < _round.Slots.Count) Look.SetPalette(PaletteFor(_round.Slots[slot.Index + 1].Level), 2.2f);
            StageJudged(slot, j);
            HudJudged(slot, j);
        }

        void UpdateBeat(double now)
        {
            var slot = Shown;
            int idx = -1;
            float phase = 0;
            if (slot != null && slot.Beats.Count > 0)
            {
                var beats = slot.Beats;
                for (int i = beats.Count - 1; i >= 0; i--) if (beats[i].Time <= now) { idx = i; break; }
                if (idx >= 0)
                {
                    double next = idx + 1 < beats.Count ? beats[idx + 1].Time : beats[idx].Time + (idx > 0 ? beats[idx].Time - beats[idx - 1].Time : 0.5);
                    phase = (float)Mathf.Clamp01((float)((now - beats[idx].Time) / System.Math.Max(1e-3, next - beats[idx].Time)));
                }
            }
            BeatIndex = idx;
            BeatPhase = phase;
        }

        void Update()
        {
            if (_round == null) return;
            float dt = Time.deltaTime;
            UpdateStage(dt);
            UpdateHud(dt);
        }
    }
}
