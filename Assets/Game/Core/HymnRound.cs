// Hymn Meter — the round: a ladder of six hymn excerpts, the CPUs, the reveal flag and the
// outcome. The round shape matches BounceRound: Start(audioTime) / Press / Update / Done /
// Result().
//
// THE LADDER (a pure function of pool and seed, HymnLadder.Select). Six slots, in order:
// Easy, Easy, Medium, Medium, Hard, Hard. What each slot wants:
//   Easy    only hymns without a pickup; slot 1 shares slot 0's pattern (so slot 0 prefers a
//           hymn that has a no-pickup partner of its pattern).
//   Medium  the meter changes every other excerpt: slot 2 differs from slot 1 (a change),
//           slot 3 keeps slot 2's pattern. Pickups allowed.
//   Hard    the pattern differs from the previous excerpt every time; hymns with a pickup
//           are preferred.
//   All     no hymn twice in a round.
// When the pool cannot satisfy all of that the rules are relaxed in a fixed order, the least
// important first: (1) Hard's pickup preference, (2) the pattern rule of the slot (and slot 0's
// partner preference), (3) no repeats (then the least used hymns are taken), (4) Easy's no
// pickup. Each rule is applied only if some hymn still passes it, so a rule never empties the
// choice and the ladder never fails; the pick among what is left is one Rng draw per slot.
//
// THE TIMELINE (Start). Excerpt k begins at the slot start; its beats are re-based so the
// excerpt's first beat is at the slot start (file time minus the excerpt's StartTime, plus
// the slot start). The window is the excerpt's, re-based: from a GOOD window before the first
// beat to the end of the last bar. Between excerpts there is a gap of GapBeats (2) beats of
// that excerpt's own tempo (twice its first beat interval) after its window closes; the
// next excerpt begins then. The first excerpt begins at the Start time (the host
// owns any count-in). The round is done when the last window has closed and been judged.
//
// THE REVEAL. Revealed turns true for an excerpt once every player has a valid answer
// (human: MeterJudge on the presses so far is Correct; CPU: its answer time has passed, and a
// CPU rolled wrong never answers), or when the window closes, whichever first.
//
// CPUS. Each CPU rolls per excerpt with Rng seeded from (round seed, slot, cpu), at the
// excerpt's start (so the reveal can know when it answers): correct or not (by level and
// skill), a start beat from which a valid run to the end exists (later for harder levels and
// lower skill), and an accuracy. The result is a MeterResult, ranked by MeterRanking like the
// human's.
//
// WINNER. Best by MeterRanking; a tie goes to the earlier list index (the human is index 0,
// the CPUs follow in order). Nobody correct: no winner (-1).
//
// SCORE. A correct excerpt is worth 600 + 300 * earliness + 100 * accuracy, earliness =
// 1 - start / (beats - 1) clamped to 0..1; a wrong one 0. Round accuracy is score / max for
// the rank (Scoring.RankFor, wrong excerpts as misses).
//
// No UnityEngine here: this assembly is pure C# so it stays test-pinned.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Bonanza.Core
{
    /// One hymn the round may draw from. Name, Pattern and Pickup drive the ladder; Midi,
    /// Grid and Row (null in pure ladder tests) are what the excerpt cut needs.
    public sealed class HymnCandidate
    {
        public string Name = "";
        public int Pattern;
        public bool Pickup;
        public HymnMidi Midi;
        public HymnGrid Grid;
        public HymnExcerptRow Row;

        public static HymnCandidate From(HymnMidi midi, HymnGrid grid, HymnExcerptRow row) =>
            new HymnCandidate { Name = row.Hymn, Pattern = grid.Pattern, Pickup = grid.HasPickup, Midi = midi, Grid = grid, Row = row };

        public bool TryCut(HymnLevel level, out HymnExcerpt excerpt, out string error)
        {
            excerpt = null;
            error = Name + ": no hymn data";
            if (Midi == null || Grid == null) return false;
            var row = Row ?? new HymnExcerptRow { Hymn = Name, Pattern = Pattern, Pickup = Pickup, Use = true };
            return HymnExcerpt.TryCut(Midi, Grid, row, level, out excerpt, out error);
        }

        /// True when all three levels cut.
        public bool CanCut()
        {
            foreach (HymnLevel level in Levels)
            {
                HymnExcerpt x;
                string err;
                if (!TryCut(level, out x, out err)) return false;
            }
            return true;
        }

        static readonly HymnLevel[] Levels = { HymnLevel.Easy, HymnLevel.Medium, HymnLevel.Hard };
    }

    public static class HymnLadder
    {
        public static readonly HymnLevel[] SlotLevels =
            { HymnLevel.Easy, HymnLevel.Easy, HymnLevel.Medium, HymnLevel.Medium, HymnLevel.Hard, HymnLevel.Hard };

        /// Pool indices of the six excerpts, in play order. Same pool and seed, same list.
        public static List<int> Select(IReadOnlyList<HymnCandidate> pool, uint seed)
        {
            if (pool == null || pool.Count == 0) throw new ArgumentException("the hymn pool is empty");
            var rng = new Rng(seed);
            var used = new int[pool.Count];
            var picks = new List<int>();

            for (int slot = 0; slot < SlotLevels.Length; slot++)
            {
                var level = SlotLevels[slot];
                var set = Enumerable.Range(0, pool.Count).ToList();

                if (level == HymnLevel.Easy) set = Soft(set, i => !pool[i].Pickup);          // 4: relaxed last

                set = Soft(set, i => used[i] == 0);                                          // 3
                int least = set.Min(i => used[i]);                                           // repeats: least used first
                set = set.Where(i => used[i] == least).ToList();

                if (slot == 0)
                {
                    set = Soft(set, i => Enumerable.Range(0, pool.Count).Any(j =>            // 2: a partner exists
                        j != i && !pool[j].Pickup && pool[j].Pattern == pool[i].Pattern));
                }
                else
                {
                    int prev = pool[picks[slot - 1]].Pattern;
                    bool change = slot == 2 || slot >= 4;                                    // slot 1 and 3 keep the pattern
                    set = Soft(set, i => (pool[i].Pattern != prev) == change);               // 2
                }

                if (level == HymnLevel.Hard) set = Soft(set, i => pool[i].Pickup);           // 1: relaxed first

                int pick = rng.Pick(set);
                used[pick]++;
                picks.Add(pick);
            }
            return picks;
        }

        static List<int> Soft(List<int> set, Func<int, bool> rule)
        {
            var kept = set.Where(rule).ToList();
            return kept.Count > 0 ? kept : set;
        }
    }

    /// A CPU's rolled excerpt.
    public sealed class HymnCpuRoll
    {
        public MeterResult Result;
        public double AnswerTime = double.PositiveInfinity;   // when its answer first becomes valid, never if wrong
    }

    public static class HymnCpu
    {
        /// Roll one CPU's excerpt on `beats` (any time base). Always draws the same four numbers.
        public static HymnCpuRoll Roll(IReadOnlyList<MeterBeat> beats, HymnLevel level, double skill, Rng rng)
        {
            double rCorrect = rng.Next(), rStart = rng.Next(), w1 = rng.Next(), w2 = rng.Next();
            var roll = new HymnCpuRoll { Result = new MeterResult() };

            int last = -1;
            for (int i = beats.Count - 1; i >= 0; i--)
                if (beats[i].BarPosition == beats[i].BeatsPerBar - 1) { last = i; break; }
            int maxStart = last < 0 ? -1 : last - beats[last].BeatsPerBar + 1;
            if (maxStart < 0) return roll;

            double penalty = level == HymnLevel.Easy ? 0 : level == HymnLevel.Medium ? 0.06 : 0.14;
            double pCorrect = Clamp(0.5 + 0.45 * skill - penalty, 0.05, 0.98);
            if (rCorrect >= pCorrect) return roll;

            double baseDelay = level == HymnLevel.Easy ? 0.02 : level == HymnLevel.Medium ? 0.08 : 0.16;
            double frac = Clamp(baseDelay + (1 - skill) * 0.3 + (rStart - 0.5) * 0.2, 0, 0.9);
            int start = Math.Min(maxStart, (int)Math.Floor(frac * maxStart + 0.5));
            double accuracy = Clamp(0.45 + 0.5 * skill + (w1 + w2 - 1) * 0.12, 0.3, 0.98);
            accuracy = Math.Floor(accuracy * 1000 + 0.5) / 1000;

            int answer = start;
            for (int e = start; e <= last; e++)
                if (beats[e].BarPosition == beats[e].BeatsPerBar - 1 && e - start + 1 >= beats[e].BeatsPerBar) { answer = e; break; }

            var r = roll.Result;
            r.Correct = true;
            r.StartBeat = start;
            r.StartTime = beats[start].Time;
            r.Accuracy = accuracy;
            r.Runs.Add(new MeterRun { StartBeat = start, Length = last - start + 1, ValidLength = last - start + 1, Accuracy = accuracy });
            roll.AnswerTime = beats[answer].Time;
            return roll;
        }

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// One excerpt on the round's timeline.
    public sealed class HymnSlot
    {
        public int Index;
        public HymnLevel Level;
        public string Name = "";
        public int Pattern;
        public bool Pickup;
        public HymnExcerpt Excerpt;              // the cut, in file time
        public double Shift;                     // add to a file time to get round time
        public double StartTime;                 // round time of the first beat
        public double WindowStart, WindowEnd;    // round time
        /// Re-based beats; BarPosition / BeatsPerBar are what the visuals colour floor tiles by.
        public List<MeterBeat> Beats = new List<MeterBeat>();
        /// Per player (human first), set when the excerpt is judged.
        public HymnJudgement Judgement;
        public bool Revealed;
        public double RevealTime;                // round time of the update that revealed it
        internal List<MeterPress> Presses = new List<MeterPress>();
        /// The CPUs' rolls for this excerpt, in player order after the human (set when it starts).
        public HymnCpuRoll[] CpuRolls = new HymnCpuRoll[0];
    }

    public sealed class HymnJudgement
    {
        public HymnSlot Slot;
        public List<MeterResult> Results = new List<MeterResult>();   // [0] = human
        public int Winner = -1;                                       // index into Results, -1 if nobody was correct
        public double HumanPoints;
    }

    public sealed class HymnRound
    {
        public const int GapBeats = 2;
        public const int ExcerptCount = 6;

        public readonly uint Seed;
        public readonly int CpuCount;
        public readonly IReadOnlyList<string> Playlist;
        public readonly List<HymnSlot> Slots = new List<HymnSlot>();
        public bool Done { get; private set; }
        public bool Started { get; private set; }
        /// Index of the excerpt being played, ExcerptCount once done, -1 before it starts.
        public int Current { get; private set; } = -1;
        public double EndTime { get; private set; }

        public event Action<HymnSlot> ExcerptStarted;
        public event Action<HymnSlot> ExcerptRevealed;
        public event Action<HymnSlot, HymnJudgement> ExcerptJudged;
        public event Action RoundDone;

        readonly double[] _skills;
        readonly List<HymnCandidate> _picked = new List<HymnCandidate>();
        readonly List<HymnExcerpt> _cuts = new List<HymnExcerpt>();
        bool _slotStarted, _humanDirty, _humanValid;

        /// Pool candidates that cannot be cut are dropped before the ladder runs. cpuSkills:
        /// one skill 0..1 per CPU, null or empty for free play.
        public HymnRound(IReadOnlyList<HymnCandidate> pool, uint seed, IReadOnlyList<double> cpuSkills = null)
        {
            Seed = seed;
            _skills = cpuSkills == null ? new double[0] : cpuSkills.ToArray();
            CpuCount = _skills.Length;
            var playable = pool.Where(c => c.CanCut()).ToList();
            if (playable.Count == 0) throw new ArgumentException("no hymn in the pool can be cut");
            foreach (int i in HymnLadder.Select(playable, seed))
                _picked.Add(playable[i]);
            for (int k = 0; k < ExcerptCount; k++)
            {
                HymnExcerpt x;
                string err;
                _picked[k].TryCut(HymnLadder.SlotLevels[k], out x, out err);
                _cuts.Add(x);
            }
            Playlist = _picked.Select(c => c.Name).ToList();
        }

        /// CPUs at the NORMAL tier.
        public HymnRound(IReadOnlyList<HymnCandidate> pool, uint seed, int cpuCount)
            : this(pool, seed, Enumerable.Repeat(Party.CpuTiers[1].skill, Math.Max(0, cpuCount)).ToArray()) { }

        public void Start(double audioTime)
        {
            Slots.Clear();
            double t = audioTime;
            for (int k = 0; k < ExcerptCount; k++)
            {
                var x = _cuts[k];
                double shift = t - x.StartTime;
                var slot = new HymnSlot
                {
                    Index = k, Level = HymnLadder.SlotLevels[k], Name = _picked[k].Name,
                    Pattern = _picked[k].Pattern, Pickup = _picked[k].Pickup,
                    Excerpt = x, Shift = shift, StartTime = t,
                    WindowStart = x.WindowStart + shift, WindowEnd = x.WindowEnd + shift,
                };
                foreach (var b in x.Beats)
                    slot.Beats.Add(new MeterBeat { Time = b.Time + shift, BarPosition = b.BarPosition, BeatsPerBar = b.BeatsPerBar });
                Slots.Add(slot);
                double beat = x.Beats.Count > 1 ? x.Beats[1].Time - x.Beats[0].Time : 0.5;
                t = slot.WindowEnd + GapBeats * beat;
            }
            EndTime = Slots[ExcerptCount - 1].WindowEnd;
            Started = true;
            Done = false;
            Current = 0;
            _slotStarted = false;
            _humanDirty = false;
            _humanValid = false;
        }

        /// The perfect presses (action, time) for one excerpt, from its first beat.
        public List<MeterPress> PerfectPresses(int slot) =>
            Slots[slot].Beats.Select(b => new MeterPress { Action = Meter.KeyFor(b.BeatsPerBar, b.BarPosition), Time = b.Time }).ToList();

        /// The perfect presses of the whole round, for the autoplay.
        public List<MeterPress> PerfectPresses() => Slots.SelectMany((s, i) => PerfectPresses(i)).ToList();

        /// Record the human's key for the excerpt being played. False when it cannot count.
        public bool Press(string action, double time)
        {
            if (!Started || Done || Current < 0 || Current >= Slots.Count) return false;
            if (Array.IndexOf(Meter.Pattern(4), action) < 0) return false;   // not an arrow key
            Slots[Current].Presses.Add(new MeterPress { Action = action, Time = time });
            _humanDirty = true;
            return true;
        }

        public void Update(double now)
        {
            if (!Started || Done) return;
            while (!Done)
            {
                var slot = Slots[Current];
                if (!_slotStarted)
                {
                    if (now < slot.StartTime) return;
                    BeginSlot(slot);
                }
                if (!slot.Revealed && AllAnswered(slot, now)) Reveal(slot, now);
                if (now <= slot.WindowEnd) return;
                if (!slot.Revealed) Reveal(slot, now);
                Judge(slot);
                Current++;
                _slotStarted = false;
                if (Current >= ExcerptCount) { Done = true; RoundDone?.Invoke(); }
            }
        }

        void BeginSlot(HymnSlot slot)
        {
            _slotStarted = true;
            _humanDirty = false;
            _humanValid = false;
            slot.CpuRolls = new HymnCpuRoll[CpuCount];
            for (int c = 0; c < CpuCount; c++)
            {
                uint s = Seed ^ unchecked((uint)(slot.Index + 1) * 0x9e3779b1u) ^ unchecked((uint)(c + 1) * 0x85ebca6bu);
                slot.CpuRolls[c] = HymnCpu.Roll(slot.Beats, slot.Level, _skills[c], new Rng(s));
            }
            ExcerptStarted?.Invoke(slot);
        }

        bool AllAnswered(HymnSlot slot, double now)
        {
            if (_humanDirty)
            {
                _humanDirty = false;
                if (!_humanValid && MeterJudge.Judge(slot.Beats, slot.WindowStart, slot.WindowEnd, slot.Presses).Correct) _humanValid = true;
            }
            if (!_humanValid) return false;
            foreach (var c in slot.CpuRolls)
                if (!(c.AnswerTime <= now)) return false;
            return true;
        }

        void Reveal(HymnSlot slot, double now)
        {
            slot.Revealed = true;
            slot.RevealTime = now;
            ExcerptRevealed?.Invoke(slot);
        }

        void Judge(HymnSlot slot)
        {
            var j = new HymnJudgement { Slot = slot };
            var human = MeterJudge.Judge(slot.Beats, slot.WindowStart, slot.WindowEnd, slot.Presses);
            j.Results.Add(human);
            foreach (var c in slot.CpuRolls) j.Results.Add(c.Result);
            j.Winner = WinnerOf(j.Results);
            j.HumanPoints = PointsFor(human, slot.Beats.Count);
            slot.Judgement = j;
            ExcerptJudged?.Invoke(slot, j);
        }

        /// Index of the best result by MeterRanking, the earlier index on a tie; -1 if nobody is correct.
        public static int WinnerOf(IReadOnlyList<MeterResult> results)
        {
            var ranking = new MeterRanking();
            int best = 0;
            for (int i = 1; i < results.Count; i++)
                if (ranking.Compare(results[i], results[best]) < 0) best = i;
            return results.Count > 0 && results[best].Correct ? best : -1;
        }

        /// 600 + 300 * earliness + 100 * accuracy for a correct excerpt, else 0.
        public static double PointsFor(MeterResult r, int beatCount)
        {
            if (!r.Correct) return 0;
            double earliness = beatCount > 1 ? 1 - r.StartBeat / (double)(beatCount - 1) : 1;
            earliness = earliness < 0 ? 0 : earliness > 1 ? 1 : earliness;
            return 600 + 300 * earliness + 100 * r.Accuracy;
        }

        public const double MaxExcerptPoints = 1000;

        public Outcome Result()
        {
            if (!Done) return null;
            double score = 0;
            int won = 0, correct = 0;
            foreach (var s in Slots)
            {
                score += s.Judgement.HumanPoints;
                if (s.Judgement.Winner == 0) won++;
                if (s.Judgement.Results[0].Correct) correct++;
            }
            int wrong = Slots.Count - correct;
            double acc = score / (Slots.Count * MaxExcerptPoints);
            var stats = new JudgeStats { Miss = wrong };
            var o = Outcome.Build(score, acc, Scoring.RankFor(acc, wrong), stats, 0, null);
            o.Extra("WON", $"{won}/{Slots.Count}")
             .Extra("CORRECT", $"{correct}/{Slots.Count}");
            return o;
        }
    }
}
