// Hymn Meter — the round: a ladder of six hymn excerpts, the CPUs, the reveal flag and the
// outcome. The round shape is the other games' rounds: Start(audioTime) / Press / Update / Done /
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
// THE REVEAL. An excerpt reveals at the halfway point of its answer window, or as soon as every
// player has submitted (the human has locked an answer, right or wrong; every CPU's submit time
// has passed), whichever is first (RevealAt). A submission after that moment is flagged PostReveal.
// The flag is a function of the submit times, not of when Update ran.
//
// CPUS. Each CPU rolls per excerpt with Rng seeded from (round seed, slot, cpu), at the
// excerpt's start (so the reveal can know when it submits): correct or not (by level and skill),
// a submit time (later for harder levels and lower skill, never before it could have filled its
// boxes) and an accuracy. A wrong CPU submits a wrong answer; a CPU that cannot fill a bar
// never submits. The result is a MeterResult, ranked by MeterRanking like the human's.
//
// WINNER. Best by MeterRanking (correct > pre-reveal > earlier Submit > accuracy); a tie goes to
// the earlier list index (the human is index 0, the CPUs follow in order). Nobody correct: no
// winner (-1).
//
// SCORE. A correct excerpt is worth 600 + 300 * earliness + 100 * accuracy, earliness = 1 at the
// window start .. 0 at its end by Submit time; times 0.25 when PostReveal; a wrong or missing
// answer 0. Round accuracy (for the rank) is score / the sum of the excerpts' CeilingPoints (what
// a perfect answer submitted the moment its boxes are full is worth), wrong excerpts as misses, so
// the perfect presses rank S although no answer can be worth the full 1000 (the window opens
// before the first beat).
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
        /// The clip this candidate's timing is rendered to (the hymn's name, or `<hymn>.<level>` for a level variant).
        public string Clip = "";
        /// Per HymnLevel: a candidate with that level's own timing (tempo map level scale), null where the level
        /// plays the base timing. Null when the hymn has no level overrides.
        public HymnCandidate[] Variants;

        public static HymnCandidate From(HymnMidi midi, HymnGrid grid, HymnExcerptRow row) =>
            new HymnCandidate { Name = row.Hymn, Pattern = grid.Pattern, Pickup = grid.HasPickup, Midi = midi, Grid = grid, Row = row, Clip = row.Hymn };

        /// A hymn under its tempo map: the base timing, plus a variant for each level the map re-times.
        /// The map's pattern wins over the table's, 0 detects it.
        public static HymnCandidate From(HymnMidi file, HymnTempoMap map, HymnExcerptRow row)
        {
            int pattern = map.Pattern > 0 ? map.Pattern : row.Pattern;
            var baseMidi = map.Apply(file);
            var c = From(baseMidi, HymnGrid.Build(baseMidi, pattern), row);
            foreach (HymnLevel level in new[] { HymnLevel.Easy, HymnLevel.Medium, HymnLevel.Hard })
            {
                string key = map.VariantKey(level);
                if (key.Length == 0) continue;
                var m = map.Apply(file, level);
                var v = From(m, HymnGrid.Build(m, pattern), row);
                v.Clip = row.Hymn + "." + key;
                if (c.Variants == null) c.Variants = new HymnCandidate[3];
                c.Variants[(int)level] = v;
            }
            return c;
        }

        /// The candidate whose timing a level plays.
        public HymnCandidate For(HymnLevel level) => Variants != null && Variants[(int)level] != null ? Variants[(int)level] : this;

        /// The clip a level plays.
        public string ClipFor(HymnLevel level) => For(level).Clip;

        public bool TryCut(HymnLevel level, out HymnExcerpt excerpt, out string error)
        {
            excerpt = null;
            error = Name + ": no hymn data";
            var c = For(level);
            if (c.Midi == null || c.Grid == null) return false;
            var row = Row ?? new HymnExcerptRow { Hymn = Name, Pattern = Pattern, Pickup = Pickup, Use = true };
            return HymnExcerpt.TryCut(c.Midi, c.Grid, row, level, out excerpt, out error);
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

    /// A CPU's rolled excerpt: its Result carries the submit time (+infinity when it never submits).
    public sealed class HymnCpuRoll
    {
        public MeterResult Result;
    }

    public static class HymnCpu
    {
        /// A CPU cannot submit sooner than this after the beat that fills its last box.
        public const double Reaction = 0.15;

        /// Grid index of the first true downbeat that has a whole pattern of beats after it, -1 if none.
        public static int FirstDownbeat(IReadOnlyList<MeterBeat> beats, int pattern)
        {
            for (int i = 0; i + pattern <= beats.Count; i++)
                if (beats[i].BarPosition == 0 && beats[i].BeatsPerBar == pattern) return i;
            return -1;
        }

        /// Roll one CPU's excerpt on `beats` and its window (any time base). Always draws the same four numbers.
        public static HymnCpuRoll Roll(IReadOnlyList<MeterBeat> beats, int pattern, double windowStart, double windowEnd,
                                       HymnLevel level, double skill, Rng rng)
        {
            double rCorrect = rng.Next(), rTime = rng.Next(), w1 = rng.Next(), w2 = rng.Next();
            var roll = new HymnCpuRoll { Result = new MeterResult() };

            int start = FirstDownbeat(beats, pattern);
            if (start < 0) return roll;

            double penalty = level == HymnLevel.Easy ? 0 : level == HymnLevel.Medium ? 0.06 : 0.14;
            double pCorrect = Clamp(0.5 + 0.45 * skill - penalty, 0.05, 0.98);
            bool correct = rCorrect < pCorrect;

            double baseDelay = level == HymnLevel.Easy ? 0.02 : level == HymnLevel.Medium ? 0.08 : 0.16;
            double frac = Clamp(baseDelay + (1 - skill) * 0.3 + (rTime - 0.5) * 0.2, 0, 0.9);
            double earliest = beats[start + pattern - 1].Time + Reaction;
            double submit = Math.Min(windowEnd, Math.Max(earliest, windowStart + frac * (windowEnd - windowStart)));
            double accuracy = Clamp(0.45 + 0.5 * skill + (w1 + w2 - 1) * 0.12, 0.3, 0.98);
            accuracy = Math.Floor(accuracy * 1000 + 0.5) / 1000;

            var r = roll.Result;
            r.Submitted = true;
            r.SubmitTime = submit;
            r.Accuracy = accuracy;
            r.Correct = correct;
            if (correct)
            {
                r.Count = pattern;
                r.StartBeat = start;
                r.StartTime = beats[start].Time;
            }
            else r.Count = pattern % 3 + 2;   // 4 -> 3, 3 -> 4, 2 -> 4: never the hymn's own
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
        public string Clip = "";                 // the rendered clip that plays it (HymnCandidate.ClipFor)
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
        public double RevealTime;                // round time of the update that revealed it (the exact moment is HymnRound.RevealAt)
        /// The human's boxes for this excerpt (made by Start): the model the HUD reads, fed by HymnRound.Press.
        public MeterAnswer Answer;
        /// Halfway through the answer window: the reveal at the latest.
        public double Halfway => (WindowStart + WindowEnd) / 2;
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
        bool _slotStarted;

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
                    Index = k, Level = HymnLadder.SlotLevels[k], Name = _picked[k].Name, Clip = _picked[k].ClipFor(HymnLadder.SlotLevels[k]),
                    Pattern = _picked[k].Pattern, Pickup = _picked[k].Pickup,
                    Excerpt = x, Shift = shift, StartTime = t,
                    WindowStart = x.WindowStart + shift, WindowEnd = x.WindowEnd + shift,
                };
                foreach (var b in x.Beats)
                    slot.Beats.Add(new MeterBeat { Time = b.Time + shift, BarPosition = b.BarPosition, BeatsPerBar = b.BeatsPerBar });
                slot.Answer = new MeterAnswer(slot.Beats, slot.Pattern, slot.WindowStart, slot.WindowEnd);
                Slots.Add(slot);
                double beat = x.Beats.Count > 1 ? x.Beats[1].Time - x.Beats[0].Time : 0.5;
                t = slot.WindowEnd + GapBeats * beat;
            }
            EndTime = Slots[ExcerptCount - 1].WindowEnd;
            Started = true;
            Done = false;
            Current = 0;
            _slotStarted = false;
        }

        /// Delay after the fill of the last box at which the perfect presses press Enter.
        public const double PerfectEnterDelay = 0.02;

        /// The perfect presses (action, time) for one excerpt: the first downbeat's key and the pattern's
        /// keys on the beats, then Enter ("a") at once.
        public List<MeterPress> PerfectPresses(int slot)
        {
            var s = Slots[slot];
            var list = new List<MeterPress>();
            int start = HymnCpu.FirstDownbeat(s.Beats, s.Pattern);
            if (start < 0) return list;
            var keys = Meter.Pattern(s.Pattern);
            for (int k = 0; k < s.Pattern; k++) list.Add(new MeterPress { Action = keys[k], Time = s.Beats[start + k].Time });
            list.Add(new MeterPress { Action = InputMap.A, Time = s.Beats[start + s.Pattern - 1].Time + PerfectEnterDelay });
            return list;
        }

        /// The perfect presses of the whole round, for the autoplay.
        public List<MeterPress> PerfectPresses() => Slots.SelectMany((s, i) => PerfectPresses(i)).ToList();

        /// Record the human's key for the excerpt being played: an arrow into the boxes, "b" clears,
        /// "a" submits. False when it is none of those or the round is not running.
        public bool Press(string action, double time)
        {
            if (!Started || Done || Current < 0 || Current >= Slots.Count) return false;
            var answer = Slots[Current].Answer;
            if (action == InputMap.A) answer.Submit(time);
            else if (action == InputMap.B) answer.Clear();
            else if (Array.IndexOf(Meter.Pattern(4), action) >= 0) answer.Press(action, time);
            else return false;
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
                slot.Answer.Update(now);
                if (!slot.Revealed && now >= RevealAt(slot)) Reveal(slot, now);
                if (now <= slot.WindowEnd) return;
                if (!slot.Revealed) Reveal(slot, now);
                Judge(slot);
                Current++;
                _slotStarted = false;
                if (Current >= ExcerptCount) { Done = true; RoundDone?.Invoke(); }
            }
        }

        /// Round time at which the excerpt reveals: halfway through its window, or when the last
        /// player submits if every player does so sooner. Needs the CPU rolls, so it is exact once
        /// the excerpt has started; the human's Submit moves it earlier as it happens.
        public double RevealAt(HymnSlot slot)
        {
            double all = slot.Answer.SubmitTime;
            foreach (var c in slot.CpuRolls)
                if (c.Result.SubmitTime > all) all = c.Result.SubmitTime;   // +infinity if anyone never submits
            return Math.Min(slot.Halfway, all);
        }

        void BeginSlot(HymnSlot slot)
        {
            _slotStarted = true;
            slot.CpuRolls = new HymnCpuRoll[CpuCount];
            for (int c = 0; c < CpuCount; c++)
            {
                uint s = Seed ^ unchecked((uint)(slot.Index + 1) * 0x9e3779b1u) ^ unchecked((uint)(c + 1) * 0x85ebca6bu);
                slot.CpuRolls[c] = HymnCpu.Roll(slot.Beats, slot.Pattern, slot.WindowStart, slot.WindowEnd, slot.Level, _skills[c], new Rng(s));
            }
            ExcerptStarted?.Invoke(slot);
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
            var human = slot.Answer.Result();
            j.Results.Add(human);
            foreach (var c in slot.CpuRolls) j.Results.Add(c.Result);
            double revealAt = RevealAt(slot);
            foreach (var r in j.Results) r.PostReveal = r.Submitted && r.SubmitTime > revealAt;
            j.Winner = WinnerOf(j.Results);
            j.HumanPoints = PointsFor(human, slot.WindowStart, slot.WindowEnd);
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

        /// 600 + 300 * earliness + 100 * accuracy for a correct excerpt (earliness 1 at the window start,
        /// 0 at its end, by Submit time), a quarter of that after the reveal, else 0.
        public static double PointsFor(MeterResult r, double windowStart, double windowEnd)
        {
            if (!r.Correct) return 0;
            double span = windowEnd - windowStart;
            double earliness = span > 0 ? 1 - (r.SubmitTime - windowStart) / span : 1;
            earliness = earliness < 0 ? 0 : earliness > 1 ? 1 : earliness;
            double points = 600 + 300 * earliness + 100 * r.Accuracy;
            return r.PostReveal ? points * PostRevealFactor : points;
        }

        public const double PostRevealFactor = 0.25;
        public const double MaxExcerptPoints = 1000;

        /// The most an excerpt can be worth: a perfect answer submitted the moment its boxes are full on
        /// the first downbeat.
        public static double CeilingPoints(HymnSlot slot)
        {
            int start = HymnCpu.FirstDownbeat(slot.Beats, slot.Pattern);
            if (start < 0) return MaxExcerptPoints;
            var best = new MeterResult { Correct = true, Accuracy = 1, SubmitTime = slot.Beats[start + slot.Pattern - 1].Time };
            return PointsFor(best, slot.WindowStart, slot.WindowEnd);
        }

        /// `playerIds`: the party lineup ids, human first then the CPUs in the order given to the round;
        /// with them the outcome carries a Field (places by total points, equal totals share a place).
        public Outcome Result(IReadOnlyList<int> playerIds = null)
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
            double ceiling = Slots.Sum(CeilingPoints);
            double acc = ceiling > 0 ? Math.Min(1, score / ceiling) : 0;
            var stats = new JudgeStats { Miss = wrong };
            var o = Outcome.Build(score, acc, Scoring.RankFor(acc, wrong), stats, 0, null);
            o.Extra("WON", $"{won}/{Slots.Count}")
             .Extra("CORRECT", $"{correct}/{Slots.Count}");
            if (playerIds != null && playerIds.Count == CpuCount + 1)
            {
                var totals = Totals();
                o.Field = new List<FieldRow>();
                for (int i = 0; i < totals.Length; i++)
                    o.Field.Add(new FieldRow { Id = playerIds[i], Place = totals.Count(t => t > totals[i] + 1e-9) });
            }
            return o;
        }

        /// Each player's points over the whole round (human first).
        public double[] Totals()
        {
            var t = new double[CpuCount + 1];
            foreach (var s in Slots)
                if (s.Judgement != null)
                    for (int i = 0; i < t.Length && i < s.Judgement.Results.Count; i++)
                        t[i] += PointsFor(s.Judgement.Results[i], s.WindowStart, s.WindowEnd);
            return t;
        }
    }
}
