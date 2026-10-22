// The hymn games' timing window: a fraction of the beat, not a fixed number of milliseconds.
//
// Judge.cs keeps fixed windows for the arcade games. The hymns are slow (60-116 bpm, with
// fermatas) and the player is listening, not reading, so a press claims a beat within
// Fraction of that beat's length, clamped so fast hymns stay as forgiving as the arcade
// window and slow ones never swallow half a beat. Perfect and great keep the arcade
// proportions (42 / 82 of 128) inside it. One constant to tune: Fraction.
//
// No UnityEngine here: pure C#, test-pinned.

using System;
using System.Collections.Generic;

namespace Bonanza.Core
{
    public static class BeatWindow
    {
        /// Half-width of the window as a fraction of the beat.
        public const double Fraction = 0.25;
        public const double MinMs = Windows.GoodMs;   // 128
        public const double MaxMs = 300;

        /// Half-width (ms) of the window for a beat `beatSeconds` long.
        public static double GoodMs(double beatSeconds)
        {
            double ms = Fraction * beatSeconds * 1000;
            return ms < MinMs ? MinMs : ms > MaxMs ? MaxMs : ms;
        }

        public static double PerfectMs(double beatSeconds) => GoodMs(beatSeconds) * (Windows.PerfectMs / Windows.GoodMs);
        public static double GreatMs(double beatSeconds) => GoodMs(beatSeconds) * (Windows.GreatMs / Windows.GoodMs);

        /// The grade of a press `errMs` off a beat `beatSeconds` long.
        public static Verdict VerdictFor(double errMs, double beatSeconds)
        {
            double a = Math.Abs(errMs);
            if (a <= PerfectMs(beatSeconds)) return Verdict.Perfect;
            if (a <= GreatMs(beatSeconds)) return Verdict.Great;
            if (a <= GoodMs(beatSeconds)) return Verdict.Good;
            return Verdict.Miss;
        }

        /// Length (s) of beat i of an ordered beat-time list: up to the next beat, or the
        /// previous beat's length for the last one (a tempo change shows at the next beat).
        public static double LengthAt(IReadOnlyList<double> times, int i)
        {
            if (times.Count < 2) return 0.5;
            if (i + 1 < times.Count) return times[i + 1] - times[i];
            return times[i] - times[i - 1];
        }
    }
}
