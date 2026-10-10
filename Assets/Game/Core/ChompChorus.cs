// Chomp Chorus — the rules, with no rendering in them. Ported as-is from the
// web prototype's web/src/games/chompChorus/ (chart.js + the gameplay half of
// index.js). 140 bpm, four singers in a row, one of them is the player's.
//
// Every singer is a voice in a four-part chord. They gulp air on the offbeat
// (the telegraph) and sing on the beat. The human sings ONE lane; any lane key
// or the main button presses it. The three CPU voices roll their performance up
// front from their skill (seeded, so a replay is identical) and play it out on
// time. A voice that misses drops out of the chord until it next lands a note.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Bonanza.Core
{
    public sealed class ChompNote
    {
        public double Beat;
        public int Lane;
        public bool Intro;         // first note of a lane in the TEACH section
        public int Run = -1;       // sixteenth-run tag, -1 if not in a run
        public int RunStep = -1;
        public bool Finale;
        public double Lead;        // telegraph length in beats; 0 = default (2)
        public double HoldBeats;

        /// Doubled from 1 beat: the tempo was too fast to react to the breath ring.
        public double LeadOrDefault => Lead > 0 ? Lead : 2;
    }

    public static class ChompChart
    {
        public const double Bpm = 140;
        public const int LeadBeats = 8;           // FEEL.leadInBars * 4
        public const int PlayBars = 40;
        public const double FinaleBeat = 156;
        public const double FinaleEnd = 160;
        public const double EndBeat = 164;

        /// Lane i: action, and stereo pan (left is low, right is high).
        public static readonly string[] Actions = { "left", "down", "up", "right" };
        public static readonly float[] Pan = { -0.62f, -0.21f, 0.21f, 0.62f };
        public static readonly float[] X = { -3.35f, -1.12f, 1.12f, 3.35f };

        public static int LaneOf(string action) => Array.IndexOf(Actions, action);

        /// Escalation, cashed out in note data:
        ///   bars  0-7  TEACH   one lane at a time, the human's first
        ///   bars  8-11 WALK    every lane, one note per beat, sweeping
        ///   bars 12-15 PAIRS   bars 16-19 WEAVE   bars 20-23 TRIADS
        ///   bars 24-27 RUNS    bars 28-31 PEAK    bars 32-35 DRIVE
        ///   bars 36-38 BUILD   (bar 38 beats 2-4 silent)   bar 39 FINALE, worth double
        public static List<ChompNote> Build(int humanLane = 0)
        {
            var notes = new List<ChompNote>();
            void Add(double beat, int[] lanes, Action<ChompNote> extra = null)
            {
                foreach (var l in lanes)
                {
                    var n = new ChompNote { Beat = beat, Lane = l };
                    extra?.Invoke(n);
                    notes.Add(n);
                }
            }
            double B(int bar, double beat = 0) => bar * 4 + beat;
            int[] L(params int[] x) => x;

            // TEACH
            for (int bar = 0; bar < 8; bar++)
            {
                int lane = (humanLane + (bar >> 1)) % 4;
                bool first = bar % 2 == 0;
                Add(B(bar, 0), L(lane), first ? n => n.Intro = true : (Action<ChompNote>)null);
                Add(B(bar, 2), L(lane));
                if (!first) Add(B(bar, 3), L(lane));
            }

            // WALK
            for (int bar = 8; bar < 12; bar++)
            {
                bool rev = bar % 2 == 1;
                for (int k = 0; k < 4; k++) Add(B(bar, k), L(rev ? 3 - k : k));
            }

            // PAIRS
            var pairs = new[]
            {
                new (double, int[])[] { (0, L(0, 2)), (1, L(1)), (2, L(1, 3)), (3, L(2)) },
                new (double, int[])[] { (0, L(1, 3)), (1, L(2)), (2, L(0, 2)), (3, L(1)) },
                new (double, int[])[] { (0, L(0, 3)), (1, L(1)), (2, L(1, 2)), (3, L(3)) },
                new (double, int[])[] { (0, L(1, 2)), (1, L(0)), (2, L(0, 3)), (3, L(3)) },
            };
            for (int bar = 12; bar < 16; bar++) foreach (var (beat, lanes) in pairs[bar - 12]) Add(B(bar, beat), lanes);

            // WEAVE
            var weave = new[]
            {
                new (double, int[])[] { (0, L(0, 3)), (1, L(1)), (1.5, L(2)), (2, L(1, 2)), (3, L(3)), (3.5, L(0)) },
                new (double, int[])[] { (0, L(1, 2)), (1, L(3)), (1.5, L(0)), (2, L(0, 3)), (3, L(2)), (3.5, L(1)) },
                new (double, int[])[] { (0, L(0, 1)), (1, L(2)), (1.5, L(3)), (2, L(2, 3)), (3, L(1)), (3.5, L(0)) },
                new (double, int[])[] { (0, L(1, 3)), (1, L(0)), (1.5, L(2)), (2, L(0, 2)), (3, L(3)), (3.5, L(1)) },
            };
            for (int bar = 16; bar < 20; bar++) foreach (var (beat, lanes) in weave[bar - 16]) Add(B(bar, beat), lanes);

            // TRIADS
            var triad = new[]
            {
                new (double, int[])[] { (0, L(0, 1, 2)), (1.5, L(3)), (2, L(1, 2, 3)), (3.5, L(0)) },
                new (double, int[])[] { (0, L(0, 2, 3)), (1.5, L(1)), (2, L(0, 1, 3)), (3.5, L(2)) },
                new (double, int[])[] { (0, L(1, 2, 3)), (1.5, L(0)), (2, L(0, 1, 2)), (3.5, L(3)) },
                new (double, int[])[] { (0, L(0, 1, 3)), (1.5, L(2)), (2, L(0, 2, 3)), (3.5, L(1)) },
            };
            for (int bar = 20; bar < 24; bar++) foreach (var (beat, lanes) in triad[bar - 20]) Add(B(bar, beat), lanes);

            // RUNS: four sixteenths sweeping the row, telegraphed as one wave.
            void Run(int bar, double beat, int[] order, int tag)
            {
                for (int k = 0; k < 4; k++)
                {
                    int step = k;
                    Add(B(bar, beat + k * 0.25), L(order[k]), n => { n.Run = tag; n.RunStep = step; });
                }
            }
            for (int bar = 24; bar < 28; bar++)
            {
                bool up = bar % 2 == 0;
                Add(B(bar, 0), L(0, 3));
                Run(bar, 1, up ? L(0, 1, 2, 3) : L(3, 2, 1, 0), bar * 2);
                Add(B(bar, 2), L(1, 2));
                Run(bar, 3, up ? L(3, 2, 1, 0) : L(0, 1, 2, 3), bar * 2 + 1);
            }

            // PEAK
            for (int bar = 28; bar < 32; bar++)
            {
                if (bar % 2 == 0)
                {
                    Add(B(bar, 0), L(0, 1, 2, 3));
                    Run(bar, 1, L(0, 1, 2, 3), bar * 2);
                    Add(B(bar, 2), L(0, 2));
                    Run(bar, 3, L(3, 2, 1, 0), bar * 2 + 1);
                }
                else
                {
                    Add(B(bar, 0), L(1, 2));
                    Add(B(bar, 1), L(0));
                    Add(B(bar, 1.5), L(3));
                    Add(B(bar, 2), L(0, 3));
                    Add(B(bar, 3), L(1));
                    Add(B(bar, 3.5), L(2));
                }
            }

            // DRIVE
            Add(B(32, 0), L(0, 1, 2)); Add(B(32, 1.5), L(3)); Add(B(32, 2), L(1, 2, 3)); Add(B(32, 3.5), L(0));
            Run(33, 0, L(0, 1, 2, 3), 66); Add(B(33, 1), L(0, 3)); Run(33, 2, L(3, 2, 1, 0), 67); Add(B(33, 3), L(1, 2));
            Add(B(34, 0), L(0, 1)); Add(B(34, 1), L(2, 3)); Add(B(34, 2), L(0, 2)); Add(B(34, 3), L(1, 3));
            var sweep = L(0, 1, 2, 3, 3, 2, 1, 0);
            for (int k = 0; k < 8; k++) Add(B(35, k * 0.5), L(sweep[k]));

            // BUILD: bar 38 beat 1 lands, then three beats of silence.
            Add(B(36, 0), L(0, 1, 2)); Add(B(36, 2), L(1, 2, 3));
            Add(B(37, 0), L(0, 3)); Add(B(37, 1), L(1, 2)); Add(B(37, 2), L(0, 3)); Add(B(37, 3), L(1, 2));
            Add(B(38, 0), L(0, 1, 2, 3));

            // FINALE: four beats of telegraph, held through the last bar.
            Add(FinaleBeat, L(0, 1, 2, 3), n => { n.Finale = true; n.Lead = 4; n.HoldBeats = FinaleEnd - FinaleBeat; });

            // JS sort by (beat, lane) — stable, as Array.prototype.sort is.
            return notes.OrderBy(n => n.Beat).ThenBy(n => n.Lane).ToList();
        }
    }

    /// A CPU singer's pre-rolled note.
    public sealed class CpuNote
    {
        public ChompNote Src;
        public double Time;
        public Verdict Verdict;
        public double ErrMs;
        public double DueAt;
        public bool Judged;
    }

    public sealed class LaneStat { public int Perfect, Great, Good, Miss, Notes; }

    public sealed class ChompRound
    {
        public readonly BeatMap Map = new BeatMap(ChompChart.Bpm);
        public readonly NoteJudge Judge;
        public readonly int Human;
        public readonly string HumanAction;
        public readonly List<ChompNote> Chart;
        public readonly double[] CpuSkill = new double[4];
        public List<CpuNote> CpuNotes { get; private set; }
        public readonly LaneStat[] Lanes = { new LaneStat(), new LaneStat(), new LaneStat(), new LaneStat() };
        public readonly bool[] Alive = { true, true, true, true };
        public int FinaleHits { get; private set; }
        public double MaxPoints { get; }
        public bool Over { get; private set; }
        readonly uint _seed;
        int _cpuAt;

        /// Every judged note, the human's and the CPUs': (lane, verdict, errMs, note, cpu).
        public event Action<int, Verdict, double, ChompNote, bool> LaneJudged;

        /// A party lineup's voices (web humanLaneFor + load): the human sings
        /// their lineup slot's lane (lane 0 with no lineup), and the lineup's
        /// CPUs sing the other lanes in order. A lane with no CPU left sings at 0.8.
        public static ChompRound ForLineup(IList<PartyPlayer> players, uint seed = 0xc40f, double offsetMs = 0)
        {
            int human = 0;
            var skill = new double[4];
            if (players != null)
            {
                int hi = -1;
                for (int i = 0; i < players.Count && hi < 0; i++) if (!players[i].IsCpu) hi = i;
                human = hi < 0 ? 0 : hi % 4;
            }
            var cpus = new List<PartyPlayer>();
            if (players != null) foreach (var p in players) if (p.IsCpu) cpus.Add(p);
            for (int i = 0, k = 0; i < 4; i++)
            {
                if (i == human) continue;
                skill[i] = k < cpus.Count ? cpus[k++].CpuSkill : 0.8;
            }
            return new ChompRound(human, skill, seed, offsetMs);
        }

        /// cpuSkill: per-lane skill for the CPU voices (the human's entry is ignored); null = 0.8 each.
        public ChompRound(int humanLane = 0, double[] cpuSkill = null, uint seed = 0xc40f, double offsetMs = 0)
        {
            Human = humanLane;
            HumanAction = ChompChart.Actions[humanLane];
            for (int i = 0; i < 4; i++) CpuSkill[i] = i == humanLane ? 0 : Clamp01(cpuSkill?[i] ?? 0.8);
            _seed = seed;
            Chart = ChompChart.Build(humanLane);
            foreach (var n in Chart) if (n.Lane == Human) MaxPoints += Scoring.PointsFor(Verdict.Perfect) * (n.Finale ? 2 : 1);
            Judge = new NoteJudge(offsetMs);
            Judge.Judged += (note, v, err) => OnJudged((ChompNote)note.Data, v, err, false);
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        public void Start(double audioTime)
        {
            Map.Start(audioTime, -ChompChart.LeadBeats);
            var mine = new List<Note>();
            foreach (var n in Chart)
                if (n.Lane == Human) mine.Add(new Note { Time = Map.TimeAt(n.Beat), Action = HumanAction, Data = n });
            Judge.Load(mine);
            CpuNotes = RollCpu(Chart, Human, CpuSkill, _seed, Map);
            _cpuAt = 0;
        }

        /// The CPU voices' performance, rolled up front exactly as the web build
        /// does (same rng draws, same order), then ordered by when they land.
        public static List<CpuNote> RollCpu(List<ChompNote> chart, int human, double[] skill, uint seed, BeatMap map)
        {
            var rnd = new Rng(seed);
            var list = new List<CpuNote>();
            foreach (var n in chart)
            {
                if (n.Lane == human) continue;
                double s = skill[n.Lane];
                bool miss = rnd.Next() < 0.02 + (1 - s) * 0.22;
                double u1 = rnd.Next();
                double u2 = rnd.Next();
                double g = Math.Sqrt(-2 * Math.Log(1 - u1)) * Math.Cos(2 * Math.PI * u2);
                double errMs = miss ? Windows.ClaimMs : g * (10 + (1 - s) * 50);
                double time = map != null ? map.TimeAt(n.Beat) : n.Beat;
                list.Add(new CpuNote
                {
                    Src = n, Time = time, ErrMs = errMs,
                    Verdict = miss ? Verdict.Miss : Scoring.VerdictFor(errMs),
                    DueAt = time + Math.Max(0, errMs) / 1000,
                });
            }
            return list.OrderBy(c => c.DueAt).ToList();   // stable, like JS sort
        }

        public double BeatAt(double t) => Map.BeatAt(t);

        /// Any lane key or the main button sings the human's voice. False if no note claimed it.
        public bool Press(double time) => Judge.Press(HumanAction, time) != null;

        public void Update(double now)
        {
            Judge.Update(now);
            while (_cpuAt < CpuNotes.Count && CpuNotes[_cpuAt].DueAt <= now)
            {
                var c = CpuNotes[_cpuAt++];
                c.Judged = true;
                OnJudged(c.Src, c.Verdict, c.ErrMs, true);
            }
            if (!Over && Map.BeatAt(now) >= ChompChart.EndBeat) Over = true;
        }

        void OnJudged(ChompNote n, Verdict v, double errMs, bool cpu)
        {
            var st = Lanes[n.Lane];
            st.Notes++;
            switch (v)
            {
                case Verdict.Perfect: st.Perfect++; break;
                case Verdict.Great: st.Great++; break;
                case Verdict.Good: st.Good++; break;
                default: st.Miss++; break;
            }
            Alive[n.Lane] = v != Verdict.Miss;
            if (n.Finale && v != Verdict.Miss) FinaleHits++;
            LaneJudged?.Invoke(n.Lane, v, errMs, n, cpu);
        }

        public double Points
        {
            get
            {
                double p = 0;
                foreach (var note in Judge.Notes)
                    if (note.Judged) p += Scoring.PointsFor(note.Verdict) * (((ChompNote)note.Data).Finale ? 2 : 1);
                return p;
            }
        }

        public static double LaneAccuracy(LaneStat q) =>
            q.Notes == 0 ? 1 : (q.Perfect * 1000.0 + q.Great * 600 + q.Good * 250) / (q.Notes * 1000.0);

        public Outcome Result()
        {
            if (!Over) return null;
            double score = 1000 * Points / Math.Max(1, MaxPoints);
            var o = Outcome.FromJudge(Judge, score);
            var mine = Lanes[Human];
            o.Extra("YOUR VOICE", $"{Math.Round(LaneAccuracy(mine) * 100)}%")
             .Extra("FINALE", $"{FinaleHits}/4")
             .Extra("LANE", ChompChart.Actions[Human].ToUpperInvariant());
            for (int i = 0; i < 4; i++)
                if (i != Human) o.Extra("CPU " + ChompChart.Actions[i].ToUpperInvariant(), $"{Math.Round(LaneAccuracy(Lanes[i]) * 100)}%");
            return o;
        }
    }
}
