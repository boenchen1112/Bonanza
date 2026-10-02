// Bounce Brigade — the rules, with no rendering in them. Ported as-is from the
// web prototype's web/src/games/bounceBrigade/ (chart.js + the gameplay half of
// index.js). 118 bpm, one button.
//
// The level is the melody: a platform is a note. Its onset beat is where it
// sits along the track (x = beat * UnitsPerBeat, since the bouncer moves at a
// constant speed), its pitch is its height and colour, its length is its
// width. Charge ramps are the melody's half notes: tap as you meet the ramp,
// tap again as you leave it two beats later (tap mode; the web build deferred
// the hold gesture the same way). There is no fail-out: a miss costs the note.

using System;
using System.Collections.Generic;
using System.Linq;

namespace RhythmGame.Core
{
    public sealed class BouncePlatform
    {
        public int Index;
        public double Beat;
        public int Deg;
        public char Kind;        // 'R' runway, 't' tap, 'c' charge, 'F' finale charge, 'X' finish
        public double Roll;
        public bool Scored, Charge;
        public int Midi, Color, Weight;
        public double Y, Tilt, DepartBeat, ContactX, DepartX, NextBeat, FlightBeats;
        public double AnchorX, AnchorY, Width, Slope, EndX, DepartY, CenterX, CenterY;
    }

    public sealed class BounceNote
    {
        public double Beat;
        public string Action = "a";
        public string Kind;      // "contact" | "release"
        public int Platform, Weight;
    }

    public static class BounceChart
    {
        public const double Bpm = 118;
        public const int BeatsPerBar = 4;
        public const double UnitsPerBeat = 3.1;
        public const double BaseY = 1.15, YPerDegree = 0.40;
        public const int MinDegree = 3;
        public const int MelodyOctave = 24;
        public const double WaterY = -3.4;
        public const double ChargeBeats = 2;
        public const double RampTilt = 0.092;
        public const double TapWidth = 1.42, SlabDepth = 2.25, SlabThick = 0.34, FinishWidth = 11.0;
        public const int LeadInBars = 2;                       // FEEL.leadInBars
        public const double RunwayBeat = -LeadInBars * BeatsPerBar;   // -8
        public const double RunwayRoll = 7;                             // depart at beat -1
        public const double EndBeat = 132;

        public static readonly int RootMidi = Theory.Note("D", 2);

        public static double DegToY(int deg) => BaseY + (deg - MinDegree) * YPerDegree;
        public static int MidiForDeg(int deg) => Theory.Degree(RootMidi, Theory.Scales["major"], deg) + MelodyOctave;
        public static double XAtBeat(double beat) => beat * UnitsPerBeat;

        /// Hue per scale degree, boomwhacker-style (D red .. C# violet).
        public static readonly int[] ColorForDegree = { 0xff5d73, 0xff9a3c, 0xffd93d, 0x9ee87a, 0x4dd6ff, 0x6f8bff, 0xc08bff };

        static int RoundHalfUp(double v) => (int)Math.Floor(v + 0.5);   // JS Math.round

        public static int Lighten(int hex, double t)
        {
            int r = (hex >> 16) & 255, g = (hex >> 8) & 255, b = hex & 255;
            return (RoundHalfUp(r + (255 - r) * t) << 16) | (RoundHalfUp(g + (255 - g) * t) << 8) | RoundHalfUp(b + (255 - b) * t);
        }

        public static int ColorForDeg(int deg)
        {
            int pc = ((deg % 7) + 7) % 7;
            int oct = (int)Math.Floor(deg / 7.0);
            return Lighten(ColorForDegree[pc], Math.Max(0, Math.Min(0.42, (oct - 1) * 0.19)));
        }

        /// [beat, degree, kind]. Sections: teach bars 0-7, play 8-23, escalate
        /// 24-29 (eighths at a sprint), finale: one enormous charged jump.
        public static readonly (double beat, int deg, char kind)[] ScoreLine =
        {
            (0, 5, 't'), (1, 7, 't'), (2, 9, 't'), (3, 7, 't'),
            (4, 12, 't'), (5, 9, 't'), (6, 7, 't'), (7, 5, 't'),
            (8, 3, 't'), (9, 5, 't'), (10, 7, 't'), (11, 10, 't'),
            (12, 12, 't'), (13, 10, 't'), (14, 7, 't'), (15, 5, 't'),
            (16, 11, 'c'),
            (20, 8, 't'), (21, 6, 't'), (22, 4, 't'), (23, 6, 't'),
            (24, 7, 't'), (25, 9, 't'), (26, 11, 't'), (27, 9, 't'),
            (28, 11, 't'), (29, 9, 't'), (30, 7, 't'), (31, 4, 't'),

            (32, 12, 't'), (32.5, 11, 't'), (33.5, 9, 't'), (35, 7, 't'),
            (36, 5, 't'), (37.5, 7, 't'), (38.5, 9, 't'), (39.5, 11, 't'),
            (40, 12, 't'), (41, 10, 't'), (42.5, 12, 't'), (43, 10, 't'),
            (44, 11, 'c'),
            (48, 11, 't'), (49, 9, 't'), (50, 7, 't'), (51, 9, 't'),
            (52, 11, 't'), (52.5, 13, 't'), (53.5, 11, 't'), (55, 8, 't'),
            (56, 7, 't'), (57, 9, 't'), (58, 11, 't'), (59, 12, 't'),
            (60, 9, 'c'),
            (64, 12, 't'), (65, 9, 't'), (66, 7, 't'), (67, 5, 't'),
            (68, 7, 't'), (68.5, 9, 't'), (69.5, 12, 't'), (71, 9, 't'),
            (72, 10, 't'), (73, 12, 't'), (74, 10, 't'), (74.5, 7, 't'), (75.5, 5, 't'),
            (76, 3, 't'), (77, 5, 't'), (78, 7, 't'), (79, 10, 't'),
            (80, 11, 't'), (80.5, 13, 't'), (81.5, 11, 't'), (82.5, 8, 't'), (83.5, 6, 't'),
            (84, 11, 'c'),
            (88, 14, 't'), (89, 12, 't'), (90, 11, 't'), (91, 9, 't'),
            (92, 7, 't'), (93, 9, 't'), (94, 11, 't'), (95, 12, 't'),

            (96, 12, 't'), (96.5, 11, 't'), (97, 9, 't'), (97.5, 7, 't'),
            (98, 9, 't'), (99, 12, 't'),
            (100, 14, 't'), (100.5, 12, 't'), (101, 11, 't'), (101.5, 9, 't'),
            (102, 7, 't'), (102.5, 9, 't'), (103, 11, 't'), (103.5, 12, 't'),
            (104, 14, 't'), (104.5, 12, 't'), (105, 10, 't'), (105.5, 12, 't'),
            (106, 14, 't'), (106.5, 12, 't'), (107, 10, 't'), (107.5, 7, 't'),
            (108, 11, 't'), (108.5, 13, 't'), (109, 11, 't'), (109.5, 8, 't'),
            (110, 6, 't'), (110.5, 8, 't'), (111, 11, 't'), (111.5, 13, 't'),
            (112, 14, 't'), (112.5, 11, 't'), (113, 9, 't'), (113.5, 11, 't'),
            (114, 12, 't'), (114.5, 14, 't'), (115, 12, 't'), (115.5, 11, 't'),
            (116, 13, 't'), (116.5, 11, 't'), (117, 8, 't'), (117.5, 6, 't'),

            (118, 11, 'F'),
            (124, 7, 'X'),
        };

        public static (List<BouncePlatform> platforms, List<BounceNote> notes, int finishIndex, double maxPoints) Build()
        {
            var rows = ScoreLine.OrderBy(r => r.beat).ToList();   // stable, like Array.sort
            var plats = new List<BouncePlatform>
            {
                new BouncePlatform { Index = 0, Beat = RunwayBeat, Deg = rows[0].deg, Kind = 'R', Roll = RunwayRoll, Scored = false },
            };
            for (int i = 0; i < rows.Count; i++)
            {
                var (beat, deg, kind) = rows[i];
                plats.Add(new BouncePlatform
                {
                    Index = i + 1, Beat = beat, Deg = deg, Kind = kind,
                    Roll = kind == 'c' || kind == 'F' ? ChargeBeats : kind == 'X' ? 8 : 0,
                    Scored = true,
                });
            }

            for (int i = 0; i < plats.Count; i++)
            {
                var p = plats[i];
                var next = i + 1 < plats.Count ? plats[i + 1] : null;
                p.Midi = MidiForDeg(p.Deg);
                p.Y = DegToY(p.Deg);
                p.Color = ColorForDeg(p.Deg);
                p.Charge = p.Kind == 'c' || p.Kind == 'F';
                p.Tilt = p.Charge ? RampTilt : 0;
                p.DepartBeat = p.Beat + p.Roll;
                p.ContactX = XAtBeat(p.Beat);
                p.DepartX = XAtBeat(p.DepartBeat);
                p.NextBeat = next != null ? next.Beat : p.Beat + p.Roll;
                p.FlightBeats = next != null ? p.NextBeat - p.DepartBeat : 0;
                p.Weight = p.Kind == 'X' || p.Kind == 'F' ? 2 : 1;

                if (p.Kind == 'R') { p.AnchorX = p.ContactX; p.Width = p.Roll * UnitsPerBeat + 1.0; }
                else if (p.Charge) { p.AnchorX = p.ContactX - 0.6; p.Width = p.Roll * UnitsPerBeat + 1.5; }
                else if (p.Kind == 'X') { p.AnchorX = p.ContactX - 1.6; p.Width = FinishWidth; }
                else { p.AnchorX = p.ContactX - TapWidth / 2; p.Width = TapWidth; }
                p.AnchorY = p.Y;
                p.Slope = Math.Tan(p.Tilt);
                p.EndX = p.AnchorX + Math.Cos(p.Tilt) * p.Width;
                p.DepartY = p.Y + p.Slope * (p.DepartX - p.ContactX);
                p.CenterX = p.AnchorX + Math.Cos(p.Tilt) * p.Width * 0.5;
                p.CenterY = p.AnchorY + Math.Sin(p.Tilt) * p.Width * 0.5;
            }

            var notes = new List<BounceNote>();
            foreach (var p in plats)
            {
                if (!p.Scored) continue;
                notes.Add(new BounceNote { Beat = p.Beat, Kind = "contact", Platform = p.Index, Weight = p.Weight });
                if (p.Charge) notes.Add(new BounceNote { Beat = p.DepartBeat, Kind = "release", Platform = p.Index, Weight = p.Weight });
            }
            notes = notes.OrderBy(n => n.Beat).ToList();

            double maxPoints = notes.Sum(n => 1000.0 * n.Weight);
            int finishIndex = plats.FindIndex(p => p.Kind == 'X');
            return (plats, notes, finishIndex, maxPoints);
        }
    }

    /// Where the bouncer is at a beat — pure, recomputed each frame, so a late
    /// judgement just reshapes the arc from that frame on.
    public struct BounceMotion
    {
        public int K;
        public double X, Y, Spin, Lean, U;
        public bool Air;
        public string Outcome;   // "clean" | "stumble" | "plunge"
    }

    public sealed class BounceRound
    {
        public const double OutroSeconds = 2.6;

        public readonly BeatMap Map = new BeatMap(BounceChart.Bpm);
        public readonly NoteJudge Judge;
        public readonly List<BouncePlatform> Platforms;
        public readonly List<BounceNote> Notes;
        public readonly int FinishIndex;
        public readonly double MaxPoints;
        public readonly List<Note> Live = new List<Note>();
        /// Per platform: null until judged.
        public readonly Verdict?[] ContactVerdict, ReleaseVerdict;

        public double Points;
        public int Misses, Ghosts, BestCombo;
        public bool Finished { get; private set; }
        public bool Done { get; private set; }
        public double MotionStopBeat = double.PositiveInfinity;
        double _finishTime;
        int _cursor;

        public event Action<Note, BounceNote, Verdict, double> Judged;
        public event Action FinishReached;

        public BounceRound(double offsetMs = 0)
        {
            (Platforms, Notes, FinishIndex, MaxPoints) = BounceChart.Build();
            ContactVerdict = new Verdict?[Platforms.Count];
            ReleaseVerdict = new Verdict?[Platforms.Count];
            Judge = new NoteJudge(offsetMs);
            Judge.Judged += OnJudged;
        }

        public void Start(double audioTime)
        {
            Map.Start(audioTime, BounceChart.RunwayBeat);
            Live.Clear();
            foreach (var n in Notes) Live.Add(new Note { Time = Map.TimeAt(n.Beat), Action = n.Action, Data = n });
            Judge.Load(Live);
        }

        public double BeatAt(double t) => Map.BeatAt(t);
        public BouncePlatform Finish => Platforms[FinishIndex];
        public bool FinishHit => ContactVerdict[FinishIndex] is Verdict v && v != Verdict.Miss;

        /// A press that claims nothing is a ghost: a tick, never a penalty.
        public bool Press(string action, double time)
        {
            if (Done || action != "a") return false;
            if (Judge.Press("a", time) != null) return true;
            Ghosts++;
            return false;
        }

        public void Update(double now)
        {
            if (Done) return;
            Judge.Update(now);
            double beat = Map.BeatAt(now);
            if (!Finished && beat >= Finish.Beat)
            {
                Finished = true;
                _finishTime = now;
                MotionStopBeat = Finish.Beat + 1.3;
                FinishReached?.Invoke();
            }
            if (Finished && now - _finishTime > OutroSeconds && Judge.Finished) Done = true;
            if (beat > BounceChart.EndBeat + 4) Done = true;
        }

        void OnJudged(Note note, Verdict v, double errMs)
        {
            var n = (BounceNote)note.Data;
            Points += Scoring.PointsFor(v) * n.Weight;
            if (v == Verdict.Miss) Misses++;
            BestCombo = Math.Max(BestCombo, Judge.Stats.Combo);
            if (n.Kind == "contact") ContactVerdict[n.Platform] = v;
            else ReleaseVerdict[n.Platform] = v;
            Judged?.Invoke(note, n, v, errMs);
        }

        public double Accuracy => Clamp01(Points / MaxPoints);

        // ---- motion ---------------------------------------------------------

        public int PlatformIndexAt(double beat)
        {
            while (_cursor + 1 < Platforms.Count && Platforms[_cursor + 1].Beat <= beat) _cursor++;
            while (_cursor > 0 && Platforms[_cursor].Beat > beat) _cursor--;
            return _cursor;
        }

        /// What happened on the way OFF platform k. Unjudged reads as optimistic.
        public string OutcomeFor(int k)
        {
            var p = Platforms[k];
            if (!p.Scored) return "clean";
            if (p.Charge) return ReleaseVerdict[k] == Verdict.Miss ? "plunge" : "clean";
            return ContactVerdict[k] == Verdict.Miss ? "stumble" : "clean";
        }

        public const double SplashY = BounceChart.WaterY + 0.35;

        public BounceMotion MotionAt(double beat)
        {
            int k = PlatformIndexAt(beat);
            var p = Platforms[k];
            double bx = Math.Min(beat, MotionStopBeat) * BounceChart.UnitsPerBeat;
            var m = new BounceMotion { K = k, X = bx, Outcome = "clean" };

            if (beat < p.DepartBeat || p.FlightBeats <= 0)
            {
                double u = p.Roll > 0 ? Clamp01((beat - p.Beat) / p.Roll) : 0;
                double y = p.Y + p.Slope * (bx - p.ContactX);
                if (p.Kind == 'R') y += 0.24 * Hop(Frac(beat));
                if (p.Charge) y -= 0.46 * Smootherstep(Clamp01(u * 1.25));
                if (p.Kind == 'X') y += 0.30 * Hop(Frac(beat));
                m.Y = y;
                m.U = u;
                m.Lean = p.Charge ? -0.22 * Smootherstep(u) : 0;
                return m;
            }

            double t0 = p.DepartBeat, t1 = p.NextBeat;
            double T = Math.Max(0.05, t1 - t0);
            double uu = Clamp01((beat - t0) / T);
            double y0 = p.DepartY, y1 = Platforms[k + 1].Y;
            string outcome = OutcomeFor(k);
            m.Air = true;
            m.U = uu;
            m.Outcome = outcome;

            if (outcome == "plunge")
            {
                const double s = 0.48;
                if (uu < s) m.Y = Lerp(y0, SplashY, EaseInCubic(uu / s));
                else
                {
                    double w = (uu - s) / (1 - s);
                    m.Y = Lerp(SplashY, y1, 1 - (1 - w) * (1 - w)) + 0.55 * Hop(w);
                }
                m.Spin = -Math.PI * 2.6 * uu;
                return m;
            }
            if (outcome == "stumble")
            {
                double ap = (0.5 + 0.75 * T) * 0.5;
                m.Y = Lerp(y0, y1, uu) + ap * Hop(uu) - 1.15 * Math.Exp(-7 * uu) * (1 - uu);
                m.Spin = -Math.PI * 1.2 * EaseOutCubic(uu);
                return m;
            }
            double apex = 0.55 + 0.95 * T;
            m.Y = Lerp(y0, y1, uu) + apex * Hop(uu);
            if (p.Charge) m.Spin = -Math.PI * 2 * (T >= 3 ? 2 : 1) * EaseOutQuint(uu * 0.92);
            else m.Lean = -0.30 * (1 - 2 * uu);
            return m;
        }

        public static double Frac(double x) => x - Math.Floor(x);   // never x % 1: beats run negative
        static double Hop(double u) => 4 * u * (1 - u);
        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        static double Lerp(double a, double b, double t) => a + (b - a) * t;
        static double Smootherstep(double t) { t = Clamp01(t); return t * t * t * (t * (t * 6 - 15) + 10); }
        static double EaseInCubic(double t) => Math.Pow(Clamp01(t), 3);
        static double EaseOutCubic(double t) => 1 - Math.Pow(1 - Clamp01(t), 3);
        static double EaseOutQuint(double t) => 1 - Math.Pow(1 - Clamp01(t), 5);

        // ---- result ---------------------------------------------------------

        public int ChargesCleared => Platforms.Count(p => p.Charge && ReleaseVerdict[p.Index] is Verdict v && v != Verdict.Miss);
        public int ChargeCount => Platforms.Count(p => p.Charge);

        public Outcome Result()
        {
            if (!Done) return null;
            double acc = Accuracy;
            var s = Judge.Stats;
            var stats = new JudgeStats { Perfect = s.Perfect, Great = s.Great, Good = s.Good, Miss = s.Miss, Combo = s.Combo, MaxCombo = Math.Max(BestCombo, s.MaxCombo) };
            var o = Outcome.Build(acc * 1000, acc, Scoring.RankFor(acc, Misses), stats, Judge.Bias, null);
            o.Extra("JUMPS", $"{ChargesCleared}/{ChargeCount}")
             .Extra("SPLASHES", ChargeCount - ChargesCleared)
             .Extra("FINISH", FinishHit ? "LANDED" : "SCRAMBLED")
             .Extra("GHOSTS", Ghosts);
            return o;
        }
    }
}
