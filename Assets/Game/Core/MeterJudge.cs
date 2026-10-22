// Hymn Meter — the run judge. Presses plus a beat grid in, the player's answer out.
//
// Each press claims the nearest unclaimed beat within that beat's window (BeatWindow:
// a fraction of the beat; Scoring points per grade); a press that claims no beat is
// a free tick: counted, no effect. A press is correct when its action is the pattern key
// for the claimed beat's true bar position.
//
// A run is consecutive beats each claimed by a correct press. A wrong key, or a beat
// left unpressed inside the run, breaks it; there is no lockout, the player may restart
// whenever they like. A run is VALID once it is at least one full bar long and ends on
// the last beat of a bar. A run may start mid-bar and span the bar line. If it keeps
// going past a bar end it stays one run with the same start; if it then stops mid-bar,
// its answer is the prefix up to the last bar end that made it valid.
//
// The answer that counts is the LAST valid run when the window closes: a later restart
// replaces an earlier valid run, but a later restart that never becomes valid does not
// erase it.
//
// Ranking ("who first"): correct first, then the earlier start beat, then accuracy.
//
// No UnityEngine here: this assembly is pure C# so it stays test-pinned.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Bonanza.Core
{
    /// One beat of the grid: audio time, 0-based position in its bar, and the beats per
    /// bar of that bar (the grid does not assume one meter).
    public sealed class MeterBeat
    {
        public double Time;
        public int BarPosition;
        public int BeatsPerBar;
    }

    public sealed class MeterPress
    {
        public string Action = "";
        public double Time;
    }

    public sealed class MeterRun
    {
        public int StartBeat;      // grid index of the first beat
        public int Length;         // beats in the run as pressed
        public int ValidLength;    // beats up to the last bar end that satisfies the rule, 0 if none
        public bool Valid => ValidLength > 0;
        public double Accuracy;    // over the valid prefix (the whole run if never valid)
    }

    public sealed class MeterResult
    {
        public bool Correct;
        public int StartBeat = -1;       // grid index of the counted run's first beat, -1 if none
        public double StartTime;         // its audio time
        public double Accuracy;          // mean points / max over the counted run's presses
        public int FreeTicks;
        public readonly List<MeterRun> Runs = new List<MeterRun>();
    }

    public static class MeterJudge
    {
        /// Judge one player's presses over [windowStart, windowEnd] (audio seconds).
        /// Presses outside the window are ignored.
        public static MeterResult Judge(IReadOnlyList<MeterBeat> beats, double windowStart, double windowEnd,
                                        IEnumerable<MeterPress> presses)
        {
            var result = new MeterResult();
            int n = beats.Count;
            var claimedBy = new MeterPress[n];
            var errMs = new double[n];
            var times = beats.Select(b => b.Time).ToList();
            var len = new double[n];
            for (int i = 0; i < n; i++) len[i] = BeatWindow.LengthAt(times, i);

            foreach (var p in presses.Where(p => p.Time >= windowStart && p.Time <= windowEnd).OrderBy(p => p.Time))
            {
                int best = -1;
                double bestErr = double.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (claimedBy[i] != null) continue;
                    double err = Math.Abs(p.Time - beats[i].Time);
                    if (err <= BeatWindow.GoodMs(len[i]) / 1000.0 && err < bestErr) { best = i; bestErr = err; }
                }
                if (best < 0) { result.FreeTicks++; continue; }
                claimedBy[best] = p;
                errMs[best] = (p.Time - beats[best].Time) * 1000.0;
            }

            int start = -1;
            for (int i = 0; i <= n; i++)
            {
                bool ok = i < n && claimedBy[i] != null &&
                          claimedBy[i].Action == Meter.KeyFor(beats[i].BeatsPerBar, beats[i].BarPosition);
                if (ok)
                {
                    if (start < 0) start = i;
                    continue;
                }
                if (start >= 0) result.Runs.Add(MakeRun(beats, errMs, len, start, i - 1));
                start = -1;
            }

            var answer = result.Runs.LastOrDefault(r => r.Valid);
            if (answer != null)
            {
                result.Correct = true;
                result.StartBeat = answer.StartBeat;
                result.StartTime = beats[answer.StartBeat].Time;
                result.Accuracy = answer.Accuracy;
            }
            return result;
        }

        static MeterRun MakeRun(IReadOnlyList<MeterBeat> beats, double[] errMs, double[] len, int first, int last)
        {
            var run = new MeterRun { StartBeat = first, Length = last - first + 1 };
            int counted = run.Length;
            for (int i = last; i >= first; i--)
            {
                var b = beats[i];
                if (b.BarPosition == b.BeatsPerBar - 1 && i - first + 1 >= b.BeatsPerBar)
                {
                    run.ValidLength = i - first + 1;
                    counted = run.ValidLength;
                    break;
                }
            }
            double points = 0;
            for (int i = first; i < first + counted; i++)
                points += Scoring.PointsFor(BeatWindow.VerdictFor(errMs[i], len[i]));
            run.Accuracy = points / (counted * (double)Scoring.PointsFor(Verdict.Perfect));
            return run;
        }
    }

    /// Orders players best first: correct, then earlier start beat, then higher accuracy.
    /// Use with OrderBy / List.Sort; wrong players tie with each other.
    public sealed class MeterRanking : IComparer<MeterResult>
    {
        public int Compare(MeterResult a, MeterResult b)
        {
            if (a.Correct != b.Correct) return a.Correct ? -1 : 1;
            if (!a.Correct) return 0;
            int c = a.StartBeat.CompareTo(b.StartBeat);
            if (c != 0) return c;
            if (Math.Abs(a.Accuracy - b.Accuracy) < 1e-9) return 0;
            return b.Accuracy.CompareTo(a.Accuracy);
        }
    }
}
