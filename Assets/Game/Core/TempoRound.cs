// Finale Fever (tempo game) — the round: three hymn excerpts on tempo curves, one tap per beat.
//
// THE ROUND. Easy, Medium, Hard: three different hymns from the shared pool (TempoPlaylist, a pure
// function of pool and seed), each cut to about four lines (TempoExcerpt) and played on the fixed
// curve of its hymn x level (TempoCurve.Generate, NOT the MIDI's own tempo). Start(audioTime) lays
// the excerpts back to back on the audio clock: excerpt k+1 begins GapBeats (2) of its own opening
// beats after excerpt k's last beat has run out (so the player hears the new tempo before the first
// tap); the first begins at audioTime (the host owns any count-in). Update / Press / Done / Result()
// are the other rounds' shape.
//
// ONE TAP PER BEAT. Press("a", time) claims the NEAREST unjudged beat whose window it is in; a window
// is BeatWindow (a quarter of THAT beat's length on the curve, so a held fermata beat or a slow ramp
// beat has a wider one, a fast beat after a jump a narrower one). The first tap inside a window
// claims the beat, so mashing earns only early Goods. A tap that claims nothing is EXTRA: counted
// (ExtraTaps, an extra stat on the result) but free, because the first-tap rule already makes spamming
// a bad strategy and a stray extra tap is a normal thing to do while finding a new tempo. Any other
// action is ignored. A beat nobody tapped is a miss once its window has closed.
//
// EARLY / LATE / ON. Per hit: ON when the grade is Perfect (BeatWindow's perfect share of the window,
// scaled to the curved beat), otherwise EARLY (tap before the beat) or LATE (after). Bias = the mean
// signed error in ms over the hits (negative = ahead of the beat, positive = behind); misses and
// extras do not enter it.
//
// HIDDEN PULSE. A per-beat flag (TempoBeat.Hidden) for the visuals: no baton on those beats. Easy has
// one short stretch (one bar of at least 4 beats), Medium two of about 6, Hard two of about 12; each
// starts and ends on a bar line, not in the first 8 beats nor the last 4, with at least 4 visible beats
// between two (HiddenPulse; deterministic per hymn x level). It changes nothing in the judging.
//
// HEARTS. Three, scored per LINE (the table's lines of the hymn; 4 bars each when it has none): a line
// that closes with at least LoseMisses(n) misses (a quarter of its beats, at least 2; FinaleFever's
// phrase rule scaled to a line) costs a heart, a clean one (no miss, every beat perfect or great)
// buys one back, up to three. Losing the last one is SURVIVE (sticky): the rank is capped at B.
//
// CPUs. Per excerpt, per CPU, a roll seeded from (round seed, slot, cpu) taken when the excerpt
// starts: for each beat a hit chance by skill and level, and a tap error spread by skill. The same
// number of draws whatever the outcome. They only score points (the party Field); they never tap.
//
// No UnityEngine here: this assembly is pure C# so it stays test-pinned.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Bonanza.Core
{
    public enum TapCall { Early, Late, On }

    /// The part of a hymn the tempo game plays: whole bars of verse 1, with the beats and the lines.
    public sealed class TempoExcerpt
    {
        public const int FallbackBars = 16;
        public const int LineBars = 4;

        public string Hymn = "";
        public int StartBar, EndBar;
        /// The hymn's own conducted tempo at the first beat (beats per minute): the base of every curve.
        public double BaseBpm;
        /// Every conducted beat of the excerpt, in file time (only BarPosition / BeatsPerBar matter here).
        public List<MeterBeat> Beats = new List<MeterBeat>();
        /// Beat index where each line starts, and where it ends (inclusive).
        public List<int> LineStarts = new List<int>();
        public List<int> LineEnds = new List<int>();
        /// MIDI tick of every beat of the excerpt plus one more: the end of the last bar (Beats.Count + 1 entries).
        public List<int> BeatTicks = new List<int>();
        /// The notes that START inside the excerpt (ticks), for the Mac render that plays them on the curve.
        public List<HymnNote> Notes = new List<HymnNote>();

        /// With the table's lines: from the first line's start to `end`, split at the lines. Without them:
        /// the last 16 full bars of verse 1 in lines of 4 bars (fewer if the hymn is short, never the
        /// pickup bar). False, with a message, when the data cannot give that.
        public static bool TryCut(HymnCandidate c, out TempoExcerpt excerpt, out string error)
        {
            excerpt = null;
            error = (c?.Name ?? "?") + ": no hymn data";
            if (c == null || c.Midi == null || c.Grid == null) return false;
            var grid = c.Grid;
            var row = c.Row;
            int firstFull = -1;
            foreach (var b in grid.Bars) if (b.Full) { firstFull = b.Number; break; }
            int end = row != null && row.End.HasValue ? row.End.Value : grid.LastFullBar;
            if (end < 0 || firstFull < 0) { error = c.Name + ": no full bar in verse 1"; return false; }

            int start = row != null && row.Lines != null ? row.Lines[0] : Math.Max(firstFull, end - FallbackBars + 1);
            var sb = grid.Bar(start);
            var eb = grid.Bar(end);
            if (sb == null) { error = c.Name + ": verse 1 has no bar " + start; return false; }
            if (eb == null) { error = c.Name + ": verse 1 has no bar " + end; return false; }
            if (end < start) { error = c.Name + ": end bar " + end + " is before start bar " + start; return false; }

            var x = new TempoExcerpt { Hymn = c.Name, StartBar = start, EndBar = end };
            int first = sb.FirstBeat, last = eb.FirstBeat + eb.BeatCount - 1;
            for (int i = first; i <= last; i++) x.Beats.Add(grid.Beats[i].ToMeterBeat());
            double interval = first + 1 < grid.Beats.Count ? grid.Beats[first + 1].Time - grid.Beats[first].Time : 0.5;
            x.BaseBpm = 60.0 / (interval > 0 ? interval : 0.5);
            for (int i = first; i <= last; i++) x.BeatTicks.Add(grid.Beats[i].Tick);
            x.BeatTicks.Add(eb.EndTick);
            foreach (var n in c.Midi.Notes)
                if (n.StartTick >= x.BeatTicks[0] && n.StartTick < eb.EndTick) x.Notes.Add(n);

            var lineBars = new List<int>();
            if (row != null && row.Lines != null) lineBars.AddRange(row.Lines);
            else for (int b = start; b <= end; b += LineBars) lineBars.Add(b);
            foreach (int bar in lineBars)
            {
                var lb = grid.Bar(bar);
                if (lb == null) { error = c.Name + ": verse 1 has no bar " + bar; return false; }
                x.LineStarts.Add(lb.FirstBeat - first);
            }
            for (int j = 0; j < x.LineStarts.Count; j++)
                x.LineEnds.Add(j + 1 < x.LineStarts.Count ? x.LineStarts[j + 1] - 1 : x.Beats.Count - 1);
            excerpt = x;
            error = null;
            return true;
        }

        /// The excerpt beat (fractional, 0 = its first beat) a MIDI tick falls on: linear in ticks between two conducted beats.
        public double BeatOfTick(int tick)
        {
            int n = BeatTicks.Count - 1;
            if (n < 1) return 0;
            int i = 0;
            while (i < n - 1 && BeatTicks[i + 1] <= tick) i++;
            int span = BeatTicks[i + 1] - BeatTicks[i];
            return i + (span > 0 ? (double)(tick - BeatTicks[i]) / span : 0);
        }

        /// The excerpt's notes laid on `curve`: every note at the curve's own time of its beat position (TimeAt on the
        /// segments, never an interpolation of the beat lines). This is what the Mac render plays.
        public List<TempoScoreNote> Score(TempoCurve curve)
        {
            var list = new List<TempoScoreNote>();
            foreach (var n in Notes)
            {
                double b0 = BeatOfTick(n.StartTick), b1 = BeatOfTick(n.EndTick);
                list.Add(new TempoScoreNote
                {
                    Track = n.Track, Pitch = n.Pitch, Velocity = n.Velocity,
                    StartBeat = b0, EndBeat = b1, Start = curve.TimeAt(b0), End = curve.TimeAt(b1),
                });
            }
            return list;
        }

        public int LineOf(int beat)
        {
            int line = 0;
            for (int j = 0; j < LineStarts.Count; j++) if (beat >= LineStarts[j]) line = j;
            return line;
        }

        /// The fixed curve of this hymn at `level` (fermatas on line ends), with the bars noted for the dump.
        public TempoCurve CurveFor(HymnLevel level)
        {
            var c = TempoCurve.Generate(Hymn, level, Beats.Count, BaseBpm, LineEnds);
            c.StartBar = StartBar;
            c.EndBar = EndBar;
            return c;
        }
    }

    /// One note of an excerpt on a tempo curve (seconds from the excerpt's first beat).
    public sealed class TempoScoreNote
    {
        public int Track, Pitch, Velocity;
        public double StartBeat, EndBeat, Start, End;
    }

    /// The three hymns of a round.
    public static class TempoPlaylist
    {
        /// Pool indices for Easy, Medium, Hard. No hymn twice while the pool lasts (then the least used
        /// are taken); one Rng draw per slot. Same pool and seed, same list.
        public static List<int> Select(IReadOnlyList<HymnCandidate> pool, uint seed)
        {
            if (pool == null || pool.Count == 0) throw new ArgumentException("the hymn pool is empty");
            var rng = new Rng(seed);
            var used = new int[pool.Count];
            var picks = new List<int>();
            for (int slot = 0; slot < TempoRound.SlotLevels.Length; slot++)
            {
                int least = used.Min();
                var set = Enumerable.Range(0, pool.Count).Where(i => used[i] == least).ToList();
                int pick = rng.Pick(set);
                used[pick]++;
                picks.Add(pick);
            }
            return picks;
        }
    }

    public sealed class HiddenRange
    {
        public int First, Last;     // beat indices, inclusive
    }

    /// Where the baton is hidden: whole bars, per hymn x level.
    public static class HiddenPulse
    {
        public const int LeadBeats = 8;      // visible at the start: the player locks on first
        public const int TailBeats = 4;      // and visible at the end
        public const int GapBeats = 4;       // visible beats between two stretches
        static readonly int[] Count = { 1, 2, 2 };
        static readonly int[] Target = { 4, 6, 12 };    // beats, rounded up to whole bars

        public static bool[] Plan(string hymn, HymnLevel level, IReadOnlyList<MeterBeat> beats)
        {
            int n = beats.Count;
            var plan = new bool[n];
            var rng = new Rng(TempoCurve.SeedFor(hymn, level) ^ 0x51ed270bu);
            int count = Count[(int)level], target = Target[(int)level];
            int lo = LeadBeats, hi = n - TailBeats;
            if (hi - lo < target) return plan;
            count = Math.Max(1, Math.Min(count, (hi - lo + GapBeats) / (target + GapBeats)));   // a short excerpt gets fewer
            double slice = (hi - lo) / (double)count;
            int prevEnd = -1000;
            for (int k = 0; k < count; k++)
            {
                int sLo = Math.Max(lo + (int)Math.Round(k * slice), prevEnd + GapBeats);
                int sHi = Math.Min(hi, lo + (int)Math.Round((k + 1) * slice));
                var starts = new List<int>();
                var ends = new Dictionary<int, int>();
                for (int s = sLo; s < sHi; s++)
                {
                    if (beats[s].BarPosition != 0) continue;
                    int e = s + 1;
                    while (e < n && !(beats[e].BarPosition == 0 && e - s >= target)) e++;
                    if (e > sHi) continue;
                    starts.Add(s);
                    ends[s] = e;
                }
                if (starts.Count == 0) continue;
                int start = rng.Pick(starts);
                for (int i = start; i < ends[start]; i++) plan[i] = true;
                prevEnd = ends[start];
            }
            return plan;
        }

        public static List<HiddenRange> Stretches(IReadOnlyList<bool> plan)
        {
            var list = new List<HiddenRange>();
            for (int i = 0; i < plan.Count; i++)
            {
                if (!plan[i]) continue;
                int j = i;
                while (j + 1 < plan.Count && plan[j + 1]) j++;
                list.Add(new HiddenRange { First = i, Last = j });
                i = j;
            }
            return list;
        }
    }

    /// One beat of one excerpt on the round's clock.
    public sealed class TempoBeat
    {
        public int Slot, Index, Line;
        public double Time;             // audio time of the beat
        public double Length;           // seconds to the next beat on the curve (the window follows it)
        public int BarPosition, BeatsPerBar;
        public bool Hidden;             // no baton on this beat
        public bool Judged, Hit;
        public Verdict Verdict;
        public double ErrMs;            // signed: negative = early (hits only)
        public TapCall Call;            // hits only
    }

    public sealed class TempoLine
    {
        public TempoSlot Slot;
        public int Index, First, Count;
        public bool Closed;
        public int Misses, Top;
        public int HeartDelta;          // -1 lost, +1 gained, 0 neither (set when it closes)
    }

    /// A CPU's rolled excerpt: its tap error and grade for every beat.
    public sealed class TempoCpuRoll
    {
        public double[] ErrMs = new double[0];
        public Verdict[] Verdicts = new Verdict[0];     // Miss = it did not tap that beat
        public int Points, Hits;
    }

    public static class TempoCpu
    {
        /// Always draws three numbers per beat.
        public static TempoCpuRoll Roll(IReadOnlyList<TempoBeat> beats, HymnLevel level, double skill, Rng rng)
        {
            double penalty = level == HymnLevel.Easy ? 0 : level == HymnLevel.Medium ? 0.04 : 0.10;
            double pHit = Clamp(0.55 + 0.42 * skill - penalty, 0.05, 0.99);
            double spread = Clamp(0.34 - 0.24 * skill, 0.05, 0.34);       // of the beat, either side
            var roll = new TempoCpuRoll { ErrMs = new double[beats.Count], Verdicts = new Verdict[beats.Count] };
            for (int i = 0; i < beats.Count; i++)
            {
                double rHit = rng.Next(), a = rng.Next(), b = rng.Next();
                double err = (a + b - 1) * spread * beats[i].Length * 1000;
                roll.ErrMs[i] = err;
                var v = rHit < pHit ? BeatWindow.VerdictFor(err, beats[i].Length) : Verdict.Miss;
                roll.Verdicts[i] = v;
                roll.Points += Scoring.PointsFor(v);
                if (v != Verdict.Miss) roll.Hits++;
            }
            return roll;
        }

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// One excerpt on the round's timeline.
    public sealed class TempoSlot
    {
        public int Index;
        public HymnLevel Level;
        public string Name = "";
        public string Clip = "";                 // TempoRound.ClipName: the audio the Mac renders from this curve
        public TempoExcerpt Excerpt;
        public TempoCurve Curve;
        public double StartTime;                 // round time of the first beat
        public double EndTime;                   // round time at which the last beat has run out
        public List<TempoBeat> Beats = new List<TempoBeat>();
        public List<TempoLine> Lines = new List<TempoLine>();
        public List<HiddenRange> HiddenStretches = new List<HiddenRange>();
        /// The CPUs' rolls, in player order after the human (set when the excerpt starts).
        public TempoCpuRoll[] CpuRolls = new TempoCpuRoll[0];
    }

    public sealed class TempoRound
    {
        public const int GapBeats = 2;
        public const int ExcerptCount = 3;
        public const int HiddenLeadBeats = HiddenPulse.LeadBeats;
        public const double LoseMissShare = 0.25;
        public static readonly HymnLevel[] SlotLevels = { HymnLevel.Easy, HymnLevel.Medium, HymnLevel.Hard };

        /// The audio a slot plays: `<hymn>.<level>.tempo`.
        public static string ClipName(string hymn, HymnLevel level) => hymn + "." + level.ToString().ToLowerInvariant() + ".tempo";

        /// Misses that cost a heart in a line of `beats` beats.
        public static int LoseMisses(int beats) => Math.Max(2, (int)Math.Ceiling(beats * LoseMissShare));

        public readonly uint Seed;
        public readonly int CpuCount;
        public readonly IReadOnlyList<string> Playlist;
        public readonly List<TempoSlot> Slots = new List<TempoSlot>();
        public IReadOnlyList<TempoBeat> AllBeats => _all;
        public bool Started { get; private set; }
        public bool Done { get; private set; }
        /// Index of the excerpt being played, ExcerptCount once done, -1 before it starts.
        public int Current { get; private set; } = -1;
        public double EndTime { get; private set; }

        public int Hearts { get; private set; } = 3;
        public bool Survived { get; private set; }
        public int Perfect, Great, Good, Miss, On, Early, Late, Combo, MaxCombo, ExtraTaps;
        public double Raw, Max;
        public double Accuracy => Max > 0 ? Math.Min(1, Raw / Max) : 1;
        /// Mean signed error (ms) over the hits: negative = ahead of the beat.
        public double BiasMs => _hits > 0 ? _errSum / _hits : 0;

        public event Action<TempoSlot> ExcerptStarted;
        public event Action<TempoBeat> Tapped;          // a hit, with its call
        public event Action<TempoBeat> Missed;
        public event Action<double> ExtraTap;           // time of a tap that claimed no beat
        public event Action<TempoLine> LineClosed;
        public event Action<int> HeartLost, HeartGained;   // hearts left
        public event Action RoundDone;

        readonly double[] _skills;
        readonly List<HymnCandidate> _picked = new List<HymnCandidate>();
        readonly List<TempoExcerpt> _cuts = new List<TempoExcerpt>();
        readonly List<TempoBeat> _all = new List<TempoBeat>();
        readonly List<TempoLine> _lines = new List<TempoLine>();
        int _cursor, _lineCursor, _started, _hits;
        double _errSum;

        /// Pool candidates that cannot be cut are dropped before the draw. cpuSkills: one skill 0..1 per
        /// CPU, null or empty for free play.
        public TempoRound(IReadOnlyList<HymnCandidate> pool, uint seed, IReadOnlyList<double> cpuSkills = null)
        {
            Seed = seed;
            _skills = cpuSkills == null ? new double[0] : cpuSkills.ToArray();
            CpuCount = _skills.Length;
            var playable = pool.Where(c => { TempoExcerpt x; string e; return TempoExcerpt.TryCut(c, out x, out e); }).ToList();
            if (playable.Count == 0) throw new ArgumentException("no hymn in the pool can be cut");
            foreach (int i in TempoPlaylist.Select(playable, seed)) _picked.Add(playable[i]);
            for (int k = 0; k < ExcerptCount; k++)
            {
                TempoExcerpt x;
                string err;
                TempoExcerpt.TryCut(_picked[k], out x, out err);
                _cuts.Add(x);
            }
            Playlist = _picked.Select(c => c.Name).ToList();
        }

        /// CPUs at the NORMAL tier.
        public TempoRound(IReadOnlyList<HymnCandidate> pool, uint seed, int cpuCount)
            : this(pool, seed, Enumerable.Repeat(Party.CpuTiers[1].skill, Math.Max(0, cpuCount)).ToArray()) { }

        public void Start(double audioTime)
        {
            Slots.Clear();
            _all.Clear();
            _lines.Clear();
            double t = audioTime;
            for (int k = 0; k < ExcerptCount; k++)
            {
                var x = _cuts[k];
                var level = SlotLevels[k];
                var curve = x.CurveFor(level);
                if (k > 0) t += GapBeats * curve.LengthOf(0);
                var slot = new TempoSlot
                {
                    Index = k, Level = level, Name = _picked[k].Name, Clip = ClipName(_picked[k].Name, level),
                    Excerpt = x, Curve = curve, StartTime = t, EndTime = t + curve.Duration,
                };
                var hidden = HiddenPulse.Plan(slot.Name, level, x.Beats);
                slot.HiddenStretches = HiddenPulse.Stretches(hidden);
                for (int i = 0; i < x.Beats.Count; i++)
                {
                    var b = new TempoBeat
                    {
                        Slot = k, Index = i, Line = x.LineOf(i), Time = t + curve.Times[i], Length = curve.LengthOf(i),
                        BarPosition = x.Beats[i].BarPosition, BeatsPerBar = x.Beats[i].BeatsPerBar, Hidden = hidden[i],
                    };
                    slot.Beats.Add(b);
                    _all.Add(b);
                }
                for (int j = 0; j < x.LineStarts.Count; j++)
                {
                    var line = new TempoLine { Slot = slot, Index = j, First = x.LineStarts[j], Count = x.LineEnds[j] - x.LineStarts[j] + 1 };
                    slot.Lines.Add(line);
                    _lines.Add(line);
                }
                Slots.Add(slot);
                t = slot.EndTime;
            }
            EndTime = Slots[ExcerptCount - 1].EndTime;
            Started = true;
            Done = false;
            Current = -1;
            Hearts = 3;
            Survived = false;
            Perfect = Great = Good = Miss = On = Early = Late = Combo = MaxCombo = ExtraTaps = 0;
            Raw = Max = _errSum = 0;
            _hits = _cursor = _lineCursor = _started = 0;
        }

        /// The perfect presses (action, time) for one excerpt: one a on every beat.
        public List<MeterPress> PerfectPresses(int slot) =>
            Slots[slot].Beats.Select(b => new MeterPress { Action = "a", Time = b.Time }).ToList();

        /// The perfect presses of the whole round, for the autoplay.
        public List<MeterPress> PerfectPresses() => Slots.SelectMany((s, i) => PerfectPresses(i)).ToList();

        /// A tap. True when it claimed a beat; false for an extra tap (counted, free) or a key that is not a.
        public bool Press(string action, double time)
        {
            if (!Started || Done || action != "a") return false;
            int best = -1;
            double bestErr = double.PositiveInfinity;
            double reach = BeatWindow.MaxMs / 1000.0;
            for (int i = _cursor; i < _all.Count; i++)
            {
                var b = _all[i];
                if (b.Time - time > reach) break;
                if (b.Judged) continue;
                double err = time - b.Time;
                if (Math.Abs(err) * 1000 > BeatWindow.GoodMs(b.Length)) continue;
                if (Math.Abs(err) < Math.Abs(bestErr)) { best = i; bestErr = err; }
            }
            if (best < 0)
            {
                ExtraTaps++;
                ExtraTap?.Invoke(time);
                return false;
            }
            Hit(_all[best], bestErr * 1000.0);
            CloseLines();
            return true;
        }

        public void Update(double now)
        {
            if (!Started || Done) return;
            while (_started < Slots.Count && now >= Slots[_started].StartTime) BeginSlot(Slots[_started++]);
            Current = _started - 1;
            for (int i = _cursor; i < _all.Count; i++)
            {
                var b = _all[i];
                if (b.Time > now) break;
                if (!b.Judged && now > b.Time + BeatWindow.GoodMs(b.Length) / 1000.0) MissBeat(b);
            }
            AdvanceCursor();
            CloseLines();
            if (_lineCursor >= _lines.Count && _cursor >= _all.Count)
            {
                Done = true;
                Current = ExcerptCount;
                RoundDone?.Invoke();
            }
        }

        void BeginSlot(TempoSlot slot)
        {
            slot.CpuRolls = new TempoCpuRoll[CpuCount];
            for (int c = 0; c < CpuCount; c++)
            {
                uint s = Seed ^ unchecked((uint)(slot.Index + 1) * 0x9e3779b1u) ^ unchecked((uint)(c + 1) * 0x85ebca6bu);
                slot.CpuRolls[c] = TempoCpu.Roll(slot.Beats, slot.Level, _skills[c], new Rng(s));
            }
            ExcerptStarted?.Invoke(slot);
        }

        void Hit(TempoBeat b, double errMs)
        {
            var v = BeatWindow.VerdictFor(errMs, b.Length);
            b.Judged = true;
            b.Hit = true;
            b.Verdict = v;
            b.ErrMs = errMs;
            b.Call = v == Verdict.Perfect ? TapCall.On : errMs < 0 ? TapCall.Early : TapCall.Late;
            switch (v)
            {
                case Verdict.Perfect: Perfect++; break;
                case Verdict.Great: Great++; break;
                default: Good++; break;
            }
            switch (b.Call)
            {
                case TapCall.On: On++; break;
                case TapCall.Early: Early++; break;
                default: Late++; break;
            }
            Raw += Scoring.PointsFor(v);
            Max += Scoring.PointsFor(Verdict.Perfect);
            Combo++;
            if (Combo > MaxCombo) MaxCombo = Combo;
            _hits++;
            _errSum += errMs;
            AdvanceCursor();
            Tapped?.Invoke(b);
        }

        void MissBeat(TempoBeat b)
        {
            b.Judged = true;
            b.Hit = false;
            b.Verdict = Verdict.Miss;
            Miss++;
            Max += Scoring.PointsFor(Verdict.Perfect);
            Combo = 0;
            Missed?.Invoke(b);
        }

        void AdvanceCursor()
        {
            while (_cursor < _all.Count && _all[_cursor].Judged) _cursor++;
        }

        /// Lines close in order, once every beat of one has been judged.
        void CloseLines()
        {
            while (_lineCursor < _lines.Count)
            {
                var line = _lines[_lineCursor];
                int misses = 0, top = 0;
                bool all = true;
                for (int i = line.First; i < line.First + line.Count; i++)
                {
                    var b = line.Slot.Beats[i];
                    if (!b.Judged) { all = false; break; }
                    if (!b.Hit) misses++;
                    else if (b.Verdict == Verdict.Perfect || b.Verdict == Verdict.Great) top++;
                }
                if (!all) return;
                _lineCursor++;
                line.Closed = true;
                line.Misses = misses;
                line.Top = top;
                if (misses >= LoseMisses(line.Count)) { line.HeartDelta = -1; LoseHeart(); }
                else if (misses == 0 && top == line.Count) { line.HeartDelta = Hearts < 3 ? 1 : 0; GainHeart(); }
                LineClosed?.Invoke(line);
            }
        }

        void LoseHeart()
        {
            if (Hearts <= 0) { HeartLost?.Invoke(0); return; }   // already surviving; nothing left to take
            Hearts--;
            if (Hearts == 0) Survived = true;
            HeartLost?.Invoke(Hearts);
        }

        void GainHeart()
        {
            if (Hearts >= 3) return;
            Hearts++;
            HeartGained?.Invoke(Hearts);
        }

        public string Tier => Survived ? "SURVIVOR" : Miss == 0 ? "METRONOME" : "STEADY";

        /// "EARLY 12 ms" / "LATE 12 ms" / "ON 0 ms": which way the player leaned, in whole milliseconds.
        public string BiasText()
        {
            int ms = (int)Math.Round(Math.Abs(BiasMs), MidpointRounding.AwayFromZero);
            return ms == 0 ? "ON 0 ms" : (BiasMs < 0 ? "EARLY " : "LATE ") + ms + " ms";
        }

        /// `playerIds`: the party lineup ids, human first then the CPUs in the order given to the round;
        /// with them the outcome carries a Field (places by total points, equal totals share a place).
        public Outcome Result(IReadOnlyList<int> playerIds = null)
        {
            if (!Done) return null;
            double acc = Accuracy;
            string rank = Scoring.RankFor(acc, Miss);
            // Losing every heart is not an ejection, but it is not an S either.
            if (Survived && (rank == "S" || rank == "A")) rank = "B";
            var stats = new JudgeStats { Perfect = Perfect, Great = Great, Good = Good, Miss = Miss, Combo = Combo, MaxCombo = MaxCombo };
            var o = Outcome.Build(acc * 1000, acc, rank, stats, BiasMs, null);
            o.Extra("TIER", Tier)
             .Extra("HEARTS", Hearts)
             .Extra("BIAS", BiasText())
             .Extra("ON", On)
             .Extra("EARLY", Early)
             .Extra("LATE", Late)
             .Extra("MISSED", Miss)
             .Extra("EXTRA TAPS", ExtraTaps);
            if (playerIds != null && playerIds.Count == CpuCount + 1)
            {
                var totals = Totals();
                o.Field = new List<FieldRow>();
                for (int i = 0; i < totals.Length; i++)
                    o.Field.Add(new FieldRow { Id = playerIds[i], Place = totals.Count(t => t > totals[i] + 1e-9) });
            }
            return o;
        }

        /// Each player's points over the round (human first).
        public double[] Totals()
        {
            var t = new double[CpuCount + 1];
            t[0] = Raw;
            foreach (var s in Slots)
                for (int c = 0; c < s.CpuRolls.Length && c < CpuCount; c++)
                    t[c + 1] += s.CpuRolls[c].Points;
            return t;
        }
    }
}
