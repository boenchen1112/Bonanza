// Ported from the web prototype's web/tests/judge.test.mjs. Timing bugs are
// invisible — the game still runs, it just feels wrong — so they are pinned.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RhythmGame.Core;

namespace RhythmGame.Core.Tests
{
    public class JudgeTests
    {
        static List<Note> Notes(IEnumerable<double> times, string action = "a") =>
            times.Select(t => new Note { Time = t, Action = action }).ToList();

        [Test]
        public void VerdictBoundariesAreInclusiveAndSymmetric()
        {
            Assert.AreEqual(Verdict.Perfect, Scoring.VerdictFor(0));
            Assert.AreEqual(Verdict.Perfect, Scoring.VerdictFor(Windows.PerfectMs));
            Assert.AreEqual(Verdict.Perfect, Scoring.VerdictFor(-Windows.PerfectMs));
            Assert.AreEqual(Verdict.Great, Scoring.VerdictFor(Windows.PerfectMs + 0.001));
            Assert.AreEqual(Verdict.Great, Scoring.VerdictFor(Windows.GreatMs));
            Assert.AreEqual(Verdict.Good, Scoring.VerdictFor(Windows.GoodMs));
            Assert.AreEqual(Verdict.Miss, Scoring.VerdictFor(Windows.GoodMs + 0.001));
            foreach (var ms in new double[] { 10, 41, 43, 81, 83, 127, 129 })
                Assert.AreEqual(Scoring.VerdictFor(ms), Scoring.VerdictFor(-ms), $"asymmetry at {ms}ms");
        }

        [Test]
        public void PressClaimsTheNearestUnjudgedNoteNotTheNextOne()
        {
            var j = new NoteJudge().Load(Notes(new[] { 1.0, 1.5, 2.0 }));
            var r = j.Press("a", 1.09).Value;
            Assert.AreEqual(1.0, r.Note.Time, "should claim note at 1.0, not 1.5");
            Assert.AreEqual(Verdict.Good, r.Verdict);
            var r2 = j.Press("a", 1.5).Value;
            Assert.AreEqual(1.5, r2.Note.Time);
            Assert.AreEqual(Verdict.Perfect, r2.Verdict);
        }

        [Test]
        public void PressNearerTheFollowingNoteClaimsTheFollowingNote()
        {
            var j = new NoteJudge().Load(Notes(new[] { 1.0, 1.2 }));
            Assert.AreEqual(1.2, j.Press("a", 1.16).Value.Note.Time);
        }

        [Test]
        public void PressOutsideEveryClaimWindowIsSwallowed()
        {
            var j = new NoteJudge().Load(Notes(new[] { 1.0, 2.0 }));
            Assert.IsNull(j.Press("a", 1.5), "mash between notes must not burn one");
            Assert.AreEqual(Verdict.Perfect, j.Press("a", 2.0).Value.Verdict);
            Assert.AreEqual(0, j.Stats.Miss);
        }

        [Test]
        public void NoteIsNeverJudgedTwice()
        {
            var j = new NoteJudge().Load(Notes(new[] { 1.0 }));
            Assert.IsNotNull(j.Press("a", 1.0));
            Assert.IsNull(j.Press("a", 1.01));
            Assert.AreEqual(1, j.Stats.Perfect);
        }

        [Test]
        public void WrongActionPressDoesNotClaimANote()
        {
            var j = new NoteJudge().Load(Notes(new[] { 1.0 }, "left"));
            Assert.IsNull(j.Press("a", 1.0));
            Assert.IsNotNull(j.Press("left", 1.0));
        }

        [Test]
        public void NotesExpireToMissOnlyAfterTheFullClaimWindow()
        {
            var j = new NoteJudge().Load(Notes(new[] { 1.0 }));
            j.Update(1.0 + Windows.ClaimMs / 1000 - 0.001);
            Assert.AreEqual(0, j.Stats.Miss, "must still be claimable at the edge");
            j.Update(1.0 + Windows.ClaimMs / 1000 + 0.001);
            Assert.AreEqual(1, j.Stats.Miss);
        }

        [Test]
        public void UpdateNeverSkipsANoteWhenSeveralExpireAtOnce()
        {
            var j = new NoteJudge().Load(Notes(new double[] { 1, 2, 3, 4, 5 }));
            j.Update(99);
            Assert.AreEqual(5, j.Stats.Miss);
            Assert.IsTrue(j.Finished);
        }

        [Test]
        public void ComboBreaksOnAMissAndMaxComboIsRetained()
        {
            var j = new NoteJudge().Load(Notes(new double[] { 1, 2, 3, 4 }));
            j.Press("a", 1); j.Press("a", 2); j.Press("a", 3);
            Assert.AreEqual(3, j.Stats.Combo);
            j.Update(99);
            Assert.AreEqual(0, j.Stats.Combo);
            Assert.AreEqual(3, j.Stats.MaxCombo);
        }

        [Test]
        public void CalibrationOffsetShiftsTheWholeWindow()
        {
            var late = new NoteJudge(-60).Load(Notes(new[] { 1.0 }));
            var r = late.Press("a", 1.06).Value;
            Assert.AreEqual(Verdict.Perfect, r.Verdict);
            Assert.Less(Math.Abs(r.ErrMs), 1e-6);
        }

        [Test]
        public void AccuracyAndBiasReportWhatAPlayerNeedsToHear()
        {
            var j = new NoteJudge().Load(Notes(new double[] { 1, 2, 3, 4 }));
            foreach (var t in new double[] { 1, 2, 3, 4 }) j.Press("a", t + 0.02);
            Assert.AreEqual(4, j.Stats.Perfect);
            Assert.Less(Math.Abs(j.Bias - 20), 0.001, "bias must surface the rush/drag");
            Assert.AreEqual(1, j.Accuracy);
        }

        [Test]
        public void RankThresholdsSDemandsACleanRun()
        {
            Assert.AreEqual("S", Scoring.RankFor(1.0, 0));
            Assert.AreEqual("A", Scoring.RankFor(1.0, 1), "one miss must cost the top rank");
            Assert.AreEqual("A", Scoring.RankFor(0.93, 0));
            Assert.AreEqual("D", Scoring.RankFor(0.5, 9));
        }

        [Test]
        public void EmptyChartIsAccuracyOneNotNaN()
        {
            var j = new NoteJudge().Load(new List<Note>());
            Assert.AreEqual(1, j.Accuracy);
            Assert.AreEqual(0, j.Bias);
            Assert.IsTrue(j.Finished);
        }

        [Test]
        public void DenseSixteenthsDoNotMisAssignUnderJitter()
        {
            const double step = 0.1;
            var times = Enumerable.Range(0, 32).Select(i => 1 + i * step).ToList();
            var j = new NoteJudge().Load(Notes(times));
            long seed = 1;
            double Rand() { seed = (seed * 1103515245 + 12345) & 0x7fffffff; return (double)seed / 0x7fffffff - 0.5; }
            foreach (var t in times) j.Press("a", t + Rand() * 0.03);
            j.Update(99);
            Assert.AreEqual(0, j.Stats.Miss, "no note orphaned");
            Assert.AreEqual(32, j.Stats.Perfect + j.Stats.Great + j.Stats.Good);
        }
    }
}
