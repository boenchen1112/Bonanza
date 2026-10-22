// Swing Kings, downbeat by ear: a round of three hymn excerpts (Easy, Medium, Hard) with no
// rendering in it. The presentation (balls, bombs, batter, crowd, words) subscribes to the events.
//
// THE GAME. Every beat of an excerpt sends one pitch, and they all look the same: same arc, same
// flight, each crossing the plate exactly on its beat. A pitch on bar position 0 (the downbeat)
// is a real BALL, every other beat's pitch is a BOMB dressed as a ball (the grid's BarPosition
// decides, so pickup hymns and 6/8 conducted in two come out right). The player swings on the
// downbeats only, by ear:
//   ball, swung      contact: the timing grade sets verdict and power (perfect = home run, then
//                    line drive, then bunt), the same tiers as before, scaled to the beat window
//   bomb, swung      EXPLODES: a strike, the combo is broken, nothing else
//   ball, let go     strike looking
//   bomb, let go     FIZZLES past the plate, harmlessly
//   swing at nothing no pitch in reach: a visible swing, no cost
// Three strikes retire the side; the count resets and play goes on. There is no count-in (a click
// would give the downbeat away) and the first FreeBars bars of each excerpt are free: they can be
// swung at (and show the ball or the bomb) but nothing scores and nothing costs an out.
//
// TIMING. A swing claims the NEAREST unclaimed pitch inside the shared hymn window (BeatWindow:
// a quarter of that beat's length, 128..300 ms). Grades and power scale inside it the same way.
// Pitches launch Lead beats before their beat, one Lead for every pitch, so a ball and a bomb
// are identical in the air. The pitch kind is hidden (DownbeatPitch.Revealed) until the bomb is
// swung at or fizzles.
//
// THE ROUND. Selection (Select) is a pure function of pool and seed: Easy a hymn without a
// pickup, Hard prefers one, no hymn twice, Medium and Hard prefer a different meter from the
// excerpt before. Each excerpt is the span from the first of the table's last three lines to the
// end bar (about 3-4 lines); without table lines it is the last SpanBars full bars of verse 1.
// Excerpt k+1 begins GapBeats beats after excerpt k's last bar. Times are round (audio) time:
// excerpt k's file time plus its Shift, so the clip plays from file time 0 at Shift.
// Clock is the round's beat axis (one beat per pitch, a gap of GapBeats beats between excerpts),
// piecewise linear through the pitch times, so tempo changes and fermatas just work.
//
// SCORE. Points are SwingRules.ScoreFor over the scored balls. Accuracy is verdict-weighted over
// the scored balls and the scored bombs swung at (a bomb swung is a miss); rank is Scoring.RankFor.
//
// CPUS (parties). Each CPU rolls per excerpt from a seed of (round seed, excerpt, cpu), at Start:
// whether it finds the downbeat, and from which bar; before that it guesses. Always the same
// number of draws, so a roll never depends on the others. The outcome carries a Field when the
// party lineup ids are given, like HymnRound.
//
// No UnityEngine here: this assembly is pure C# so it stays test-pinned.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Bonanza.Core
{
    public enum DownbeatKind { Ball, Bomb }

    /// One pitch: a ball on the downbeat, a bomb on any other beat. Time and LaunchTime are audio time.
    public sealed class DownbeatPitch : Pitch
    {
        public int Slot;                // excerpt index
        public int Index;               // beat within the excerpt
        public int Bar, BarPosition, BeatsPerBar;
        public double Time;             // crosses the plate
        public double LaunchTime;       // leaves the machine (Lead beats earlier on the beat axis)
        public double BeatSeconds;      // this beat's length: the window is a fraction of it
        public bool IsBall;
        public bool IsFree;             // inside the free bars: nothing scores, nothing costs an out
        public DownbeatKind Type => IsBall ? DownbeatKind.Ball : DownbeatKind.Bomb;
        public bool IsBomb => !IsBall;
        /// Half-width of the swing window, ms.
        public double WindowMs => BeatWindow.GoodMs(BeatSeconds);
        /// False for a bomb until it is swung at (explodes) or fizzles; the visuals draw it as a ball until then.
        public bool Revealed;
        public bool Swung;              // a swing claimed it
        public bool Resolved;           // swung at, or past its window
    }

    public struct ExplodeEvent
    {
        public DownbeatPitch Pitch;
        public double ErrMs;
        public bool Free;               // in the free bars: no strike
        public int Outs;                // 1..3 after this strike, 0 when free
        public bool SideRetired;
    }

    /// One excerpt on the round's timeline.
    public sealed class DownbeatSlot
    {
        public int Index;
        public HymnLevel Level;
        public string Name = "";
        public string Clip = "";                 // the rendered clip that plays it (HymnCandidate.ClipFor)
        public int Pattern;
        public bool Pickup;
        public int StartBar, EndBar;
        public double Shift;                     // add to a file time to get round time
        public double StartTime;                 // round time of the first beat
        public double EndTime;                   // round time of the end of the last bar
        public readonly List<DownbeatPitch> Pitches = new List<DownbeatPitch>();
    }

    /// The round's beat axis: beat number <-> audio time, piecewise linear through the pitch times.
    public sealed class DownbeatClock
    {
        readonly double[] _time, _beat;

        public DownbeatClock(IReadOnlyList<double> times, IReadOnlyList<double> beats)
        {
            _time = times.ToArray();
            _beat = beats.ToArray();
        }

        int Seg(double v, double[] axis)
        {
            int lo = 0, hi = axis.Length - 2;
            if (hi < 0) return -1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (axis[mid] <= v) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        public double BeatAt(double time)
        {
            int i = Seg(time, _time);
            if (i < 0) return _beat.Length == 1 ? _beat[0] + (time - _time[0]) * 2 : time * 2;
            return _beat[i] + (time - _time[i]) * (_beat[i + 1] - _beat[i]) / (_time[i + 1] - _time[i]);
        }

        public double TimeAt(double beat)
        {
            int i = Seg(beat, _beat);
            if (i < 0) return _time.Length == 1 ? _time[0] + (beat - _beat[0]) / 2 : beat / 2;
            return _time[i] + (beat - _beat[i]) * (_time[i + 1] - _time[i]) / (_beat[i + 1] - _beat[i]);
        }

        /// Seconds per beat at audio time `time`.
        public double SpbAt(double time)
        {
            int i = Seg(time, _time);
            return i < 0 ? 0.5 : (_time[i + 1] - _time[i]) / (_beat[i + 1] - _beat[i]);
        }
    }

    /// A CPU's rolled excerpt.
    public sealed class DownbeatCpuRoll
    {
        public double Points;
        public double Accuracy;
        public int Hits, Misses;
        /// Scored bar from which the CPU has the downbeat, int.MaxValue when it never finds it.
        public int LockBar = int.MaxValue;
    }

    public static class DownbeatCpu
    {
        /// Roll one CPU's excerpt on its pitches. Always draws 2 + 2 per scored pitch.
        public static DownbeatCpuRoll Roll(IReadOnlyList<DownbeatPitch> pitches, HymnLevel level, double skill, Rng rng)
        {
            double rFind = rng.Next(), rLock = rng.Next();
            var roll = new DownbeatCpuRoll();

            double penalty = level == HymnLevel.Easy ? 0 : level == HymnLevel.Medium ? 0.06 : 0.14;
            double pFind = Clamp(0.5 + 0.45 * skill - penalty, 0.05, 0.98);
            var bars = pitches.Where(p => !p.IsFree).Select(p => p.Bar).Distinct().OrderBy(b => b).ToList();
            double baseDelay = level == HymnLevel.Easy ? 0.02 : level == HymnLevel.Medium ? 0.08 : 0.16;
            double frac = Clamp(baseDelay + (1 - skill) * 0.3 + (rLock - 0.5) * 0.2, 0, 0.9);
            if (rFind < pFind && bars.Count > 0) roll.LockBar = bars[(int)Math.Floor(frac * bars.Count)];

            double pPerfect = Clamp(0.1 + 0.7 * skill, 0.1, 0.8), pGreat = 0.25;
            double slip = 0.04 * (1 - skill);
            int points = 0, judged = 0;
            foreach (var p in pitches)
            {
                if (p.IsFree) continue;
                double a = rng.Next(), b = rng.Next();
                bool locked = p.Bar >= roll.LockBar;
                Verdict v;
                if (locked)
                {
                    if (p.IsBall) v = b < slip ? Verdict.Miss : a < pPerfect ? Verdict.Perfect : a < pPerfect + pGreat ? Verdict.Great : Verdict.Good;
                    else { if (b >= slip) continue; v = Verdict.Miss; }          // a bomb is swung only by a slip
                }
                else
                {
                    if (b >= 0.5) { if (p.IsBall) v = Verdict.Miss; else continue; }   // guessing: about every other beat
                    else v = p.IsBall ? Verdict.Good : Verdict.Miss;
                }
                judged++;
                if (v == Verdict.Miss) roll.Misses++; else roll.Hits++;
                if (!p.IsBall) continue;
                double power = v == Verdict.Perfect ? 1 : v == Verdict.Great ? 0.5 : v == Verdict.Good ? 0.17 : 0;
                roll.Points += SwingRules.ScoreFor(v, power, false).got;
                points += Scoring.PointsFor(v);
            }
            roll.Accuracy = judged == 0 ? 1 : points / (judged * (double)Scoring.PointsFor(Verdict.Perfect));
            return roll;
        }

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }

    public sealed class DownbeatRound
    {
        public const int ExcerptCount = 3;
        public const int FreeBars = 2;
        public const int GapBeats = 2;
        /// Beats from launch to the plate, the same for every pitch.
        public const double Lead = 1;
        public const double Apex = 1.75;
        /// Without table lines an excerpt is the last SpanBars full bars of verse 1.
        public const int SpanBars = 16;
        /// A pitch is gone into the backstop this far past the plate (flight u), as in SwingRound.
        public const double SailPastU = SwingRules.SailPastU;

        public static readonly HymnLevel[] Levels = { HymnLevel.Easy, HymnLevel.Medium, HymnLevel.Hard };

        public readonly uint Seed;
        public readonly int CpuCount;
        public readonly IReadOnlyList<string> Playlist;
        public readonly List<DownbeatSlot> Slots = new List<DownbeatSlot>();
        public readonly List<DownbeatPitch> Pitches = new List<DownbeatPitch>();
        public readonly JudgeStats Stats = new JudgeStats();
        public DownbeatClock Clock { get; private set; }
        public bool Started { get; private set; }
        public bool Over { get; private set; }

        public double Points { get; private set; }
        public double MaxPoints { get; private set; }
        public int Outs { get; private set; }
        public int Combo => Stats.Combo;
        public int Homers, Liners, Bunts, Fouls, Bombs;
        /// The CPUs' rolls per excerpt, [excerpt][cpu], set by Start.
        public DownbeatCpuRoll[][] CpuRolls { get; private set; } = new DownbeatCpuRoll[0][];

        /// Player latency calibration, ADDED to swing times (as NoteJudge).
        public double OffsetMs;

        /// An excerpt's first pitch left the machine.
        public event Action<DownbeatSlot> ExcerptStarted;
        public event Action<Pitch> PitchLaunched;
        /// A ball or a bomb that was not hit sailed past the plate into the backstop.
        public event Action<Pitch> PitchPassed;
        public event Action<ContactEvent> Contact;
        /// A ball let go: strike looking (WhiffEvent.Swung is false).
        public event Action<WhiffEvent> Whiff;
        public event Action<ExplodeEvent> Explode;
        /// A bomb let go: it shows itself and is gone, no effect.
        public event Action<DownbeatPitch> Fizzle;
        /// A swing that claimed no pitch: still a visible swing, costs nothing.
        public event Action<double> SwingAtNothing;
        /// Every judged scored pitch that was swung at or let go as a ball (a fizzle is not judged).
        public event Action<Verdict, double> Judged;

        readonly double[] _skills;
        readonly List<HymnCandidate> _picked = new List<HymnCandidate>();
        readonly List<Cut> _cuts = new List<Cut>();
        int _first;
        int _slotsStarted;
        double _errSum;
        int _errN;
        double _powerSum;
        int _powerN;

        // the conducting path (a windup that a downstroke's ictus or the release swings)
        public bool Holding { get; private set; }
        public double HoldStart { get; private set; }
        public double LastHoldBeats { get; private set; }
        public double LastPower { get; private set; }

        /// Pool candidates that cannot be cut are dropped before the selection runs. cpuSkills:
        /// one skill 0..1 per CPU, null or empty for free play.
        public DownbeatRound(IReadOnlyList<HymnCandidate> pool, uint seed, IReadOnlyList<double> cpuSkills = null, double offsetMs = 0)
        {
            Seed = seed;
            OffsetMs = offsetMs;
            _skills = cpuSkills == null ? new double[0] : cpuSkills.ToArray();
            CpuCount = _skills.Length;
            var playable = pool.Where(CanCut).ToList();
            if (playable.Count == 0) throw new ArgumentException("no hymn in the pool can be cut");
            foreach (int i in Select(playable, seed))
                _picked.Add(playable[i]);
            for (int k = 0; k < ExcerptCount; k++)
            {
                Cut cut;
                string err;
                TryCut(_picked[k], Levels[k], out cut, out err);
                _cuts.Add(cut);
            }
            Playlist = _picked.Select(c => c.Name).ToList();
        }

        /// CPUs at the NORMAL tier.
        public DownbeatRound(IReadOnlyList<HymnCandidate> pool, uint seed, int cpuCount)
            : this(pool, seed, Enumerable.Repeat(Party.CpuTiers[1].skill, Math.Max(0, cpuCount)).ToArray()) { }

        // ------------------------------------------------------------ selection and cut

        /// Pool indices of the three excerpts (Easy, Medium, Hard). Same pool and seed, same list.
        /// No hymn twice (the least used are taken when the pool is smaller than three); then, each only
        /// if some hymn still passes it: Easy has no pickup, Hard has one, Medium and Hard change the
        /// meter from the excerpt before.
        public static List<int> Select(IReadOnlyList<HymnCandidate> pool, uint seed)
        {
            if (pool == null || pool.Count == 0) throw new ArgumentException("the hymn pool is empty");
            var rng = new Rng(seed ^ 0x6a09e667u);
            var used = new int[pool.Count];
            var picks = new List<int>();
            for (int slot = 0; slot < ExcerptCount; slot++)
            {
                var set = Enumerable.Range(0, pool.Count).ToList();
                int least = set.Min(i => used[i]);
                set = set.Where(i => used[i] == least).ToList();
                if (slot > 0)
                {
                    int prev = pool[picks[slot - 1]].Pattern;
                    set = Soft(set, i => pool[i].Pattern != prev);
                }
                if (slot == 0) set = Soft(set, i => !pool[i].Pickup);
                if (slot == 2) set = Soft(set, i => pool[i].Pickup);
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

        /// One excerpt cut out of verse 1, in file time.
        sealed class Cut
        {
            public List<HymnBeat> Beats = new List<HymnBeat>();
            public int StartBar, EndBar;
            public double StartTime, EndTime;
        }

        /// True when all three levels cut.
        public static bool CanCut(HymnCandidate c)
        {
            foreach (var level in Levels)
            {
                Cut cut;
                string err;
                if (!TryCut(c, level, out cut, out err)) return false;
            }
            return true;
        }

        /// The bar range of a candidate's excerpt at a level, for the table and the tests.
        public static bool TrySpan(HymnCandidate c, HymnLevel level, out int startBar, out int endBar)
        {
            Cut cut;
            string err;
            bool ok = TryCut(c, level, out cut, out err);
            startBar = ok ? cut.StartBar : -1;
            endBar = ok ? cut.EndBar : -1;
            return ok;
        }

        static bool TryCut(HymnCandidate cand, HymnLevel level, out Cut cut, out string error)
        {
            cut = null;
            error = cand.Name + ": no hymn data";
            var c = cand.For(level);
            if (c.Midi == null || c.Grid == null) return false;
            var grid = c.Grid;
            var row = cand.Row;
            int endBar = row != null && row.End.HasValue ? row.End.Value : grid.LastFullBar;
            if (endBar < 0) { error = cand.Name + ": no full bar in verse 1"; return false; }
            int startBar;
            if (row != null && row.Lines != null) startBar = row.Lines[0];
            else
            {
                int firstFull = int.MaxValue;
                foreach (var b in grid.Bars) if (b.Full) { firstFull = b.Number; break; }
                if (firstFull == int.MaxValue) { error = cand.Name + ": no full bar in verse 1"; return false; }
                startBar = Math.Max(firstFull, endBar - SpanBars + 1);
            }
            var sb = grid.Bar(startBar);
            var eb = grid.Bar(endBar);
            if (sb == null) { error = cand.Name + ": verse 1 has no bar " + startBar; return false; }
            if (eb == null) { error = cand.Name + ": verse 1 has no bar " + endBar; return false; }
            if (endBar < startBar) { error = cand.Name + ": end bar " + endBar + " is before start bar " + startBar; return false; }
            int first = sb.FirstBeat, last = eb.FirstBeat + eb.BeatCount - 1;
            if (last < first) { error = cand.Name + ": the excerpt has no beats"; return false; }
            var x = new Cut { StartBar = startBar, EndBar = endBar };
            for (int i = first; i <= last; i++) x.Beats.Add(grid.Beats[i]);
            x.StartTime = grid.Beats[first].Time;
            x.EndTime = c.Midi.SecondsAt(eb.EndTick);
            cut = x;
            return true;
        }

        // ------------------------------------------------------------ the timeline

        /// Lay the excerpts out from `audioTime` (the first beat of the first excerpt): no count-in.
        public void Start(double audioTime)
        {
            Slots.Clear();
            Pitches.Clear();
            var times = new List<double>();
            var beats = new List<double>();
            double t = audioTime, g = 0;
            int id = 0;
            for (int k = 0; k < ExcerptCount; k++)
            {
                var x = _cuts[k];
                var cand = _picked[k];
                double shift = t - x.StartTime;
                var slot = new DownbeatSlot
                {
                    Index = k, Level = Levels[k], Name = cand.Name, Clip = cand.ClipFor(Levels[k]),
                    Pattern = cand.For(Levels[k]).Grid.Pattern, Pickup = cand.For(Levels[k]).Grid.HasPickup,
                    StartBar = x.StartBar, EndBar = x.EndBar,
                    Shift = shift, StartTime = t, EndTime = x.EndTime + shift,
                };
                var local = x.Beats.Select(b => b.Time + shift).ToList();
                for (int i = 0; i < x.Beats.Count; i++)
                {
                    var b = x.Beats[i];
                    bool ball = b.BarPosition == 0;
                    bool free = b.Bar - x.StartBar < FreeBars;
                    var p = new DownbeatPitch
                    {
                        Id = id++, TargetBeat = g + i, Lead = Lead, Apex = Apex, Kind = PitchKind.Normal, Scored = !free,
                        Slot = k, Index = i, Bar = b.Bar, BarPosition = b.BarPosition, BeatsPerBar = b.BeatsPerBar,
                        Time = local[i], BeatSeconds = BeatWindow.LengthAt(local, i),
                        IsBall = ball, IsFree = free, Revealed = ball,
                    };
                    slot.Pitches.Add(p);
                    Pitches.Add(p);
                    times.Add(p.Time);
                    beats.Add(p.TargetBeat);
                }
                Slots.Add(slot);
                double firstLen = local.Count > 1 ? local[1] - local[0] : 0.5;
                t = slot.EndTime + GapBeats * firstLen;
                g += x.Beats.Count + GapBeats;
            }
            Clock = new DownbeatClock(times, beats);
            foreach (var p in Pitches) p.LaunchTime = Clock.TimeAt(p.TargetBeat - Lead);

            CpuRolls = new DownbeatCpuRoll[ExcerptCount][];
            for (int k = 0; k < ExcerptCount; k++)
            {
                CpuRolls[k] = new DownbeatCpuRoll[CpuCount];
                for (int c = 0; c < CpuCount; c++)
                {
                    uint s = Seed ^ unchecked((uint)(k + 1) * 0x9e3779b1u) ^ unchecked((uint)(c + 1) * 0x85ebca6bu);
                    CpuRolls[k][c] = DownbeatCpu.Roll(Slots[k].Pitches, Levels[k], _skills[c], new Rng(s));
                }
            }

            Stats.Perfect = Stats.Great = Stats.Good = Stats.Miss = Stats.Combo = Stats.MaxCombo = Stats.Score = 0;
            Stats.Errors.Clear();
            Points = MaxPoints = 0;
            Outs = 0;
            Homers = Liners = Bunts = Fouls = Bombs = 0;
            _errSum = _powerSum = 0;
            _errN = _powerN = 0;
            _first = 0;
            _slotsStarted = 0;
            Holding = false;
            LastPower = LastHoldBeats = 0;
            Started = true;
            Over = false;
        }

        /// The round's last moment: the end of the last excerpt.
        public double EndTime => Slots.Count > 0 ? Slots[Slots.Count - 1].EndTime : 0;

        public double BeatAt(double audioTime) => Clock.BeatAt(audioTime);

        /// Flight progress of a pitch at `beat`: 0 at the muzzle, 1 at the plate.
        public static double FlightU(Pitch p, double beat) => (beat - p.LaunchBeat) / p.Lead;

        /// The excerpt being played: the one whose first pitch has launched last. Null before Start.
        public DownbeatSlot CurrentSlot => _slotsStarted > 0 ? Slots[_slotsStarted - 1] : null;

        /// The pitch currently in flight (the earliest launched and not yet hit or past), if any.
        public Pitch LivePitch()
        {
            foreach (var p in Pitches) if (p.Live && !p.Done) return p;
            return null;
        }

        /// The scored balls, in order: what a perfect player swings at.
        public IEnumerable<DownbeatPitch> ScoredBalls() => Pitches.Where(p => p.IsBall && !p.IsFree);

        /// One swing exactly on every scored downbeat: the autoplay's presses (audio time).
        public List<double> PerfectPresses() => ScoredBalls().Select(p => p.Time - OffsetMs / 1000.0).ToList();

        // ------------------------------------------------------------ swings

        /// A tap at audio time `time`: one press is one swing. Power is earned by timing.
        public DownbeatPitch Press(double time)
        {
            LastHoldBeats = 0;
            return Swing(time);
        }

        public void BeginWindup(double time)
        {
            Holding = true;
            HoldStart = time;
        }

        /// The windup so far, beats.
        public double HoldBeatsAt(double now) => Holding && Clock != null ? Math.Max(0, (now - HoldStart) / Clock.SpbAt(now)) : 0;

        /// The release, or the ictus of a downstroke: swing. False when no windup was held. The hold is only
        /// a gesture: power is earned by timing, as for a tap.
        public bool ReleaseSwing(double time)
        {
            if (!Holding) return false;
            Holding = false;
            LastHoldBeats = Math.Max(0, (time - HoldStart) / Clock.SpbAt(time));
            Swing(time);
            return true;
        }

        /// A swing at audio time `time`. Claims the nearest unclaimed pitch inside its window and returns it,
        /// or null (SwingAtNothing) when nothing is in reach.
        public DownbeatPitch Swing(double time)
        {
            LastPower = 0;
            if (!Started || Over) return null;
            double t = time + OffsetMs / 1000.0;
            DownbeatPitch best = null;
            double bestAbs = double.PositiveInfinity;
            for (int i = _first; i < Pitches.Count; i++)
            {
                var p = Pitches[i];
                if ((p.Time - t) * 1000 > BeatWindow.MaxMs) break;   // ordered by time: the rest are further away
                if (p.Resolved) continue;
                double abs = Math.Abs((t - p.Time) * 1000);
                if (abs > p.WindowMs) continue;
                if (abs < bestAbs) { best = p; bestAbs = abs; }
            }
            if (best == null) { SwingAtNothing?.Invoke(time); return null; }
            Claim(best, (t - best.Time) * 1000);
            return best;
        }

        /// Power 0..1 for a swing `errMs` off a beat `beatSeconds` long: SwingRules.PowerFromTiming with the beat's
        /// window laid over the arcade one, so perfect is the same share of the window whatever the tempo.
        public static double PowerFor(double errMs, double beatSeconds)
        {
            double scaled = Math.Abs(errMs) * (Windows.GoodMs / BeatWindow.GoodMs(beatSeconds));
            return SwingRules.PowerFromTiming(Math.Max(0, scaled - 1e-9) / 1000.0, 0);
        }

        void Claim(DownbeatPitch p, double errMs)
        {
            p.Swung = true;
            p.Resolved = true;
            p.Done = true;
            p.Revealed = true;
            if (p.IsBall) Hit(p, errMs); else Boom(p, errMs);
        }

        void Hit(DownbeatPitch p, double errMs)
        {
            var verdict = BeatWindow.VerdictFor(errMs, p.BeatSeconds);
            double power = verdict == Verdict.Perfect ? 1 : PowerFor(errMs, p.BeatSeconds);
            var tier = SwingRules.TierFor(power);
            bool foul = verdict == Verdict.Good && errMs > 0;     // GOOD is a bunt when early, a foul when late
            LastPower = power;
            int combo = Stats.Combo;
            if (!p.IsFree)
            {
                var s = SwingRules.ScoreFor(verdict, power, false);
                Points += s.got;
                MaxPoints += s.max;
                Commit(verdict, errMs);
                combo = Stats.Combo;
                switch (tier.Id)
                {
                    case TierId.Bunt: Bunts++; break;
                    case TierId.Liner: Liners++; break;
                    case TierId.Homer: Homers++; break;
                }
                if (foul) Fouls++;
                _powerSum += power;
                _powerN++;
            }
            Contact?.Invoke(new ContactEvent
            {
                Pitch = p, Verdict = verdict, ErrMs = errMs, Power = power, Tier = tier, Foul = foul, Combo = combo,
                HomerFeet = tier.Id == TierId.Homer && !foul ? SwingRules.HomerFeet(power, verdict, combo) : 0,
            });
            if (!p.IsFree) Judged?.Invoke(verdict, errMs);
        }

        void Boom(DownbeatPitch p, double errMs)
        {
            var e = new ExplodeEvent { Pitch = p, ErrMs = errMs, Free = p.IsFree };
            if (!p.IsFree)
            {
                Bombs++;
                Commit(Verdict.Miss, errMs);
                e.Outs = Strike(out e.SideRetired);
            }
            Explode?.Invoke(e);
            if (!p.IsFree) Judged?.Invoke(Verdict.Miss, errMs);
        }

        /// A strike: the count goes up, the third retires the side and starts the count over.
        int Strike(out bool retired)
        {
            Outs++;
            int outs = Outs;
            retired = Outs >= 3;
            if (retired) Outs = 0;
            return outs;
        }

        void Commit(Verdict v, double errMs)
        {
            var s = Stats;
            switch (v)
            {
                case Verdict.Perfect: s.Perfect++; break;
                case Verdict.Great: s.Great++; break;
                case Verdict.Good: s.Good++; break;
                default: s.Miss++; break;
            }
            s.Score += Scoring.PointsFor(v);
            if (v != Verdict.Miss)
            {
                s.Combo++;
                s.MaxCombo = Math.Max(s.MaxCombo, s.Combo);
                s.Errors.Add(errMs);
                _errSum += errMs;
                _errN++;
            }
            else s.Combo = 0;
        }

        // ------------------------------------------------------------ the clock

        /// Advance to audio time `now`. Call every frame, after input.
        public void Update(double now)
        {
            if (!Started || Over) return;
            for (int i = _first; i < Pitches.Count; i++)
            {
                var p = Pitches[i];
                if (p.Done) { if (i == _first) _first++; continue; }
                if (now < p.LaunchTime) break;                      // launch-ordered: the rest have not left yet
                if (!p.Live)
                {
                    p.Live = true;
                    if (p.Index == 0 && p.Slot >= _slotsStarted) { _slotsStarted = p.Slot + 1; ExcerptStarted?.Invoke(Slots[p.Slot]); }
                    PitchLaunched?.Invoke(p);
                }
                if (!p.Resolved && now * 1000 > p.Time * 1000 + p.WindowMs) Expire(p);
                if (p.Resolved && FlightU(p, Clock.BeatAt(now)) >= SailPastU)
                {
                    p.Done = true;
                    PitchPassed?.Invoke(p);
                }
            }
            if (_first >= Pitches.Count) Over = true;
        }

        /// The window closed on a pitch nobody swung at.
        void Expire(DownbeatPitch p)
        {
            p.Resolved = true;
            p.Revealed = true;
            if (p.IsBomb) { Fizzle?.Invoke(p); return; }
            if (p.IsFree) return;
            Commit(Verdict.Miss, p.WindowMs);
            MaxPoints += SwingRules.ScoreFor(Verdict.Perfect, 1, false).max;
            bool retired;
            int outs = Strike(out retired);
            Whiff?.Invoke(new WhiffEvent { Pitch = p, Swung = false, Outs = outs, SideRetired = retired });
            Judged?.Invoke(Verdict.Miss, p.WindowMs);
        }

        // ------------------------------------------------------------ result

        /// Verdict-weighted accuracy over the scored balls and the scored bombs swung at.
        public double Accuracy
        {
            get
            {
                var s = Stats;
                int total = s.Perfect + s.Great + s.Good + s.Miss;
                return total == 0 ? 1 : (double)s.Score / (total * Scoring.PointsFor(Verdict.Perfect));
            }
        }

        public double BiasMs => _errN == 0 ? 0 : _errSum / _errN;

        /// `playerIds`: the party lineup ids, human first then the CPUs in the order given to the round;
        /// with them the outcome carries a Field (places by total points, equal totals share a place).
        public Outcome Result(IReadOnlyList<int> playerIds = null)
        {
            var o = Outcome.Build(Points, Accuracy, Scoring.RankFor(Accuracy, Stats.Miss), Stats, BiasMs, null);
            o.Extra("HOME RUNS", Homers).Extra("LINE DRIVES", Liners).Extra("BUNTS", Bunts)
             .Extra("FOULS", Fouls).Extra("BOMBS", Bombs);
            if (playerIds != null && playerIds.Count == CpuCount + 1)
            {
                var totals = Totals();
                o.Field = new List<FieldRow>();
                for (int i = 0; i < totals.Length; i++)
                    o.Field.Add(new FieldRow { Id = playerIds[i], Place = totals.Count(x => x > totals[i] + 1e-9) });
            }
            return o;
        }

        /// Each player's points over the whole round (human first).
        public double[] Totals()
        {
            var t = new double[CpuCount + 1];
            t[0] = Points;
            for (int k = 0; k < CpuRolls.Length; k++)
                for (int c = 0; c < CpuCount; c++)
                    t[c + 1] += CpuRolls[k][c].Points;
            return t;
        }
    }
}
