// Pins the hymn games' timing window: 25 % of the beat, clamped to 128..300 ms, with the
// arcade grade proportions inside it, and its use by the meter judge.

using System.Collections.Generic;
using NUnit.Framework;
using Bonanza.Core;

namespace Bonanza.Core.Tests
{
    public class BeatWindowTests
    {
        static double Spb(double bpm) => 60.0 / bpm;

        [Test]
        public void WindowIsAQuarterOfTheBeat()
        {
            Assert.AreEqual(0.25 * Spb(78) * 1000, BeatWindow.GoodMs(Spb(78)), 1e-9);    // ~192 ms
            Assert.AreEqual(0.25 * Spb(100) * 1000, BeatWindow.GoodMs(Spb(100)), 1e-9);  // 150 ms
            Assert.AreEqual(250, BeatWindow.GoodMs(Spb(60)), 1e-9);
        }

        [Test]
        public void WindowIsClamped()
        {
            Assert.AreEqual(128, BeatWindow.GoodMs(Spb(160)), 1e-9);     // 94 ms -> the arcade minimum
            Assert.AreEqual(300, BeatWindow.GoodMs(Spb(19.2)), 1e-9);    // a fermata never swallows a beat
        }

        [Test]
        public void GradesKeepTheArcadeProportions()
        {
            double b = Spb(60);   // good 250 ms
            Assert.AreEqual(Verdict.Perfect, BeatWindow.VerdictFor(250 * 42.0 / 128, b));
            Assert.AreEqual(Verdict.Great, BeatWindow.VerdictFor(-(250 * 42.0 / 128 + 1), b));
            Assert.AreEqual(Verdict.Good, BeatWindow.VerdictFor(250 * 82.0 / 128 + 1, b));
            Assert.AreEqual(Verdict.Good, BeatWindow.VerdictFor(-250, b));
            Assert.AreEqual(Verdict.Miss, BeatWindow.VerdictFor(251, b));
        }

        [Test]
        public void BeatLengthFollowsATempoChange()
        {
            var t = new List<double> { 0, 1, 2, 2.5, 3 };   // 60 bpm, then 120 bpm
            Assert.AreEqual(1, BeatWindow.LengthAt(t, 1), 1e-9);
            Assert.AreEqual(0.5, BeatWindow.LengthAt(t, 2), 1e-9);
            Assert.AreEqual(0.5, BeatWindow.LengthAt(t, 4), 1e-9);   // last beat: the previous length
        }

        [Test]
        public void MeterJudgeClaimsWithTheBeatWindow()
        {
            // One 4/4 bar at 60 bpm (1 s beats, 250 ms window), then the closing downbeat.
            var beats = new List<MeterBeat>();
            for (int i = 0; i < 5; i++) beats.Add(new MeterBeat { Time = 1 + i, BarPosition = i % 4, BeatsPerBar = 4 });
            // 200 ms late on every beat: outside the arcade 128 ms, inside 250 ms.
            var presses = new List<MeterPress>();
            string[] keys = { "down", "left", "right", "up" };
            for (int i = 0; i < 4; i++) presses.Add(new MeterPress { Action = keys[i], Time = 1 + i + 0.2 });
            presses.Add(new MeterPress { Action = "a", Time = 4.25 });
            var r = MeterJudge.Judge(beats, 4, 0, 10, presses);
            Assert.IsTrue(r.Correct);
            Assert.AreEqual(Scoring.PointsFor(Verdict.Good) / 1000.0, r.Accuracy, 1e-9);
        }
    }
}
