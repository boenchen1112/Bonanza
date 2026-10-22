// Pins the Hymn Meter rules: the conducting pattern of each meter and the answer boxes
// (downbeat first, fill, break, flicker, clear, submit, lock, ranking). Pure C#, no hymn
// files: beat grids are built in memory at 120 bpm (one beat every 0.5 s, window 128 ms).

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Bonanza.Core;

namespace Bonanza.Core.Tests
{
    public class MeterJudgeTests
    {
        const double Step = 0.5;

        // `bars` bars of `bpb` beats, first beat at t = 1 s, positions 0-based.
        static List<MeterBeat> Grid(int bars, int bpb)
        {
            var g = new List<MeterBeat>();
            for (int i = 0; i < bars * bpb; i++)
                g.Add(new MeterBeat { Time = 1.0 + i * Step, BarPosition = i % bpb, BeatsPerBar = bpb });
            return g;
        }

        // Correct keys for beats [from, to] inclusive, pressed `offset` s off the beat.
        static List<MeterPress> Keys(List<MeterBeat> g, int from, int to, double offset = 0)
        {
            var p = new List<MeterPress>();
            for (int i = from; i <= to; i++)
                p.Add(new MeterPress { Action = Meter.KeyFor(g[i].BeatsPerBar, g[i].BarPosition), Time = g[i].Time + offset });
            return p;
        }

        static MeterAnswer Ans(List<MeterBeat> g, int pattern) => new MeterAnswer(g, pattern, 0, g[g.Count - 1].Time + 1);

        // Press the pattern's keys on `pattern` beats from beat `start` (claiming it as the downbeat, true or not).
        static MeterAnswer Fill(List<MeterBeat> g, int pattern, int start, int hymnPattern = 0)
        {
            var a = Ans(g, hymnPattern > 0 ? hymnPattern : pattern);
            var keys = Meter.Pattern(pattern);
            for (int k = 0; k < pattern; k++) a.Press(keys[k], g[start + k].Time);
            return a;
        }

        static MeterResult Run(List<MeterBeat> g, int pattern, List<MeterPress> p) =>
            MeterJudge.Judge(g, pattern, 0, g[g.Count - 1].Time + 1, p);

        static MeterPress Enter(double t) => new MeterPress { Action = "a", Time = t };

        [Test]
        public void PatternsFollowTheConductedBeats()
        {
            CollectionAssert.AreEqual(new[] { "right", "up" }, Meter.Pattern(2));
            CollectionAssert.AreEqual(new[] { "left", "right", "up" }, Meter.Pattern(3));
            CollectionAssert.AreEqual(new[] { "down", "left", "right", "up" }, Meter.Pattern(4));
            Assert.AreEqual("left", Meter.KeyFor(3, 0));
            Assert.AreEqual("down", Meter.KeyFor(4, 0));
            Assert.AreEqual("up", Meter.KeyFor(4, 3));
            Assert.IsNull(Meter.KeyFor(4, 4));
            Assert.IsNull(Meter.KeyFor(5, 0));
            Assert.IsNull(Meter.Pattern(7));
        }

        [Test]
        public void StepHeightsFollowTheWeightOfTheBeat()
        {
            CollectionAssert.AreEqual(new[] { 1, 3, 2, 3 }, Enumerable.Range(0, 4).Select(i => Meter.StepHeight(4, i)));
            CollectionAssert.AreEqual(new[] { 1, 3, 3 }, Enumerable.Range(0, 3).Select(i => Meter.StepHeight(3, i)));
            CollectionAssert.AreEqual(new[] { 1, 3 }, Enumerable.Range(0, 2).Select(i => Meter.StepHeight(2, i)));
            Assert.AreEqual(0, Meter.StepHeight(4, 4));
            Assert.AreEqual(0, Meter.StepHeight(5, 0));
            var b = new MeterBeat { Time = 0, BarPosition = 2, BeatsPerBar = 4 };
            Assert.AreEqual(2, b.StepHeight);
        }

        [TestCase("down", 4), TestCase("left", 3), TestCase("right", 2)]
        public void TheFirstKeyOfEachPatternSetsTheBoxCount(string key, int boxes)
        {
            var g = Grid(3, 4);
            var a = Ans(g, boxes);
            Assert.IsTrue(a.Press(key, g[0].Time));
            Assert.AreEqual(boxes, a.Count);
            Assert.AreEqual(1, a.Filled);
            Assert.AreEqual(key, a.KeyAt(0));
            Assert.IsNull(a.KeyAt(1));
            Assert.AreEqual(0, a.StartBeat);
            Assert.IsFalse(a.Full);
        }

        [Test]
        public void AWrongFirstKeyIsIgnored()
        {
            var g = Grid(3, 4);
            var a = Ans(g, 4);
            Assert.IsFalse(a.Press("up", g[0].Time));
            Assert.AreEqual(0, a.Count);
            Assert.AreEqual(0, a.Filled);
            Assert.AreEqual(-1, a.StartBeat);
            Assert.IsFalse(a.Press("a", g[0].Time));   // not an arrow: the Press method only takes arrows
            Assert.AreEqual(0, a.Filled);
        }

        [Test]
        public void AFirstKeyOffTheBeatIsIgnored()
        {
            var g = Grid(3, 4);
            var a = Ans(g, 4);
            Assert.IsFalse(a.Press("down", g[0].Time + 0.25));   // halfway between beats
            Assert.IsFalse(a.Press("down", 0.1));                 // before the music, outside the window
            Assert.AreEqual(0, a.Count);
        }

        [Test]
        public void FollowingOnBeatKeysFillTheNextBoxes()
        {
            var g = Grid(3, 4);
            var a = Fill(g, 4, 0);
            Assert.IsTrue(a.Full);
            Assert.AreEqual(4, a.Count);
            for (int i = 0; i < 4; i++) Assert.AreEqual(Meter.Pattern(4)[i], a.KeyAt(i));
            Assert.AreEqual(1.0, a.Accuracy, 1e-9);
            Assert.IsFalse(a.Locked);
        }

        [Test]
        public void AWrongKeyEmptiesTheBoxes()
        {
            var g = Grid(3, 4);
            var a = Ans(g, 4);
            a.Press("down", g[0].Time);
            a.Press("left", g[1].Time);
            a.Press("up", g[2].Time);   // right was due
            Assert.AreEqual(0, a.Filled);
            Assert.AreEqual(0, a.Count);
            Assert.AreEqual(-1, a.StartBeat);
            // and it starts again on the next downbeat
            foreach (var p in Keys(g, 4, 7)) a.Press(p.Action, p.Time);
            Assert.IsTrue(a.Full);
            Assert.AreEqual(4, a.StartBeat);
        }

        [Test]
        public void AMissedBeatEmptiesTheBoxesOnPressAndOnUpdate()
        {
            var g = Grid(3, 4);
            // seen by the next press: beat 2 (index 1) never pressed, a press on beat 3 (index 2) empties
            var a = Ans(g, 4);
            a.Press("down", g[0].Time);
            a.Press("up", g[2].Time);
            Assert.AreEqual(0, a.Filled);
            // seen by the clock: the beat's window plus the grace has passed
            var b = Ans(g, 4);
            b.Press("down", g[0].Time);
            b.Update(g[1].Time + 0.128 + MeterAnswer.LateGrace - 0.01);
            Assert.AreEqual(1, b.Filled, "still inside the grace");
            b.Update(g[1].Time + 0.128 + MeterAnswer.LateGrace + 0.01);
            Assert.AreEqual(0, b.Filled);
        }

        [Test]
        public void AFullAnswerDoesNotBreakOnLaterBeats()
        {
            var g = Grid(3, 4);
            var a = Fill(g, 4, 0);
            a.Update(g[8].Time);
            Assert.IsTrue(a.Full);
            Assert.IsFalse(a.Press("down", g[4].Time));   // more keys do nothing
            Assert.AreEqual(4, a.Filled);
        }

        [Test]
        public void ADoublePressOnOneBeatIsAFreeTick()
        {
            var g = Grid(3, 4);
            var a = Ans(g, 4);
            a.Press("down", g[0].Time);
            Assert.IsFalse(a.Press("left", g[0].Time + 0.03));   // same beat again: ignored, nothing breaks
            Assert.AreEqual(1, a.Filled);
            Assert.IsTrue(a.Press("left", g[1].Time));
            Assert.AreEqual(2, a.Filled);
        }

        [Test]
        public void TheLitBoxFollowsTheBeatsSinceTheClaimedDownbeat()
        {
            var g = Grid(3, 4);
            var a = Fill(g, 4, 0);
            Assert.AreEqual(3, a.LitBox(g[3].Time + 0.01));
            Assert.AreEqual(0, a.LitBox(g[4].Time + 0.01), "bar line: back to box 1");
            Assert.AreEqual(1, a.LitBox(g[5].Time + 0.2));
            Assert.AreEqual(3, a.LitBox(g[7].Time));
            Assert.AreEqual(0, a.LitBox(g[8].Time));
            Assert.AreEqual(2, a.LitBox(g[10].Time + 0.4));
        }

        [Test]
        public void TheLitBoxCountsFromWhereTheDownbeatWasClaimed()
        {
            var g = Grid(4, 4);
            // claim beat 2 (a mid-bar beat) as the downbeat: box 1 lights there, not on the true bar line
            var a = Fill(g, 4, 2);
            Assert.AreEqual(2, a.StartBeat);
            Assert.AreEqual(3, a.LitBox(g[5].Time + 0.01));
            Assert.AreEqual(0, a.LitBox(g[6].Time + 0.01));
            Assert.AreEqual(2, a.LitBox(g[8].Time + 0.01));   // the true bar line lights box 3
        }

        [Test]
        public void NothingLightsBeforeTheBoxesAreFull()
        {
            var g = Grid(3, 4);
            var a = Ans(g, 4);
            Assert.AreEqual(-1, a.LitBox(g[0].Time));
            a.Press("down", g[0].Time);
            a.Press("left", g[1].Time);
            Assert.AreEqual(-1, a.LitBox(g[2].Time));
        }

        [Test]
        public void ThreeAndTwoBoxFlickerLoopWithTheirCount()
        {
            var g3 = Grid(4, 3);
            var a3 = Fill(g3, 3, 0);
            Assert.AreEqual(2, a3.LitBox(g3[2].Time));
            Assert.AreEqual(0, a3.LitBox(g3[3].Time));
            Assert.AreEqual(1, a3.LitBox(g3[7].Time));
            var g2 = Grid(4, 2);
            var a2 = Fill(g2, 2, 0);
            Assert.AreEqual(1, a2.LitBox(g2[1].Time));
            Assert.AreEqual(0, a2.LitBox(g2[2].Time));
            Assert.AreEqual(1, a2.LitBox(g2[5].Time));
        }

        [Test]
        public void ClearEmptiesAndTheBoxesRefill()
        {
            var g = Grid(3, 4);
            var a = Fill(g, 4, 0);
            Assert.IsTrue(a.Clear());
            Assert.AreEqual(0, a.Filled);
            Assert.AreEqual(0, a.Count);
            Assert.AreEqual(-1, a.LitBox(g[5].Time));
            foreach (var p in Keys(g, 4, 7)) a.Press(p.Action, p.Time);
            Assert.IsTrue(a.Full);
            Assert.AreEqual(4, a.StartBeat);
            // clear half way through, too
            var b = Ans(g, 4);
            b.Press("down", g[0].Time);
            Assert.IsTrue(b.Clear());
            Assert.AreEqual(0, b.Filled);
        }

        [Test]
        public void SubmitWorksOnlyWhenFullThenLocks()
        {
            var g = Grid(3, 4);
            var a = Ans(g, 4);
            Assert.IsFalse(a.Submit(g[0].Time));
            a.Press("down", g[0].Time);
            Assert.IsFalse(a.Submit(g[0].Time + 0.1), "not full");
            Assert.IsFalse(a.Locked);
            foreach (var p in Keys(g, 1, 3)) a.Press(p.Action, p.Time);
            Assert.IsTrue(a.Submit(g[3].Time + 0.1));
            Assert.IsTrue(a.Locked);
            Assert.AreEqual(g[3].Time + 0.1, a.SubmitTime, 1e-9);
            // locked: no resubmit, no clear, later keys ignored
            Assert.IsFalse(a.Submit(g[4].Time));
            Assert.AreEqual(g[3].Time + 0.1, a.SubmitTime, 1e-9);
            Assert.IsFalse(a.Clear());
            Assert.IsFalse(a.Press("down", g[4].Time));
            Assert.IsTrue(a.Full);
            Assert.AreEqual(0, a.StartBeat);
            Assert.AreEqual(0, a.LitBox(g[4].Time + 0.01), "the flicker goes on after the lock");
        }

        [Test]
        public void SubmitOutsideTheAnswerWindowIsIgnored()
        {
            var g = Grid(2, 4);
            var a = new MeterAnswer(g, 4, 0, g[3].Time + 0.1);
            foreach (var p in Keys(g, 0, 3)) a.Press(p.Action, p.Time);
            Assert.IsFalse(a.Submit(g[3].Time + 0.5));
            Assert.IsFalse(a.Locked);
        }

        [Test]
        public void TheRightPatternOnTheDownbeatIsCorrect()
        {
            var g = Grid(3, 4);
            var a = Fill(g, 4, 4);
            Assert.IsTrue(a.MatchesHymn);
            a.Submit(g[7].Time + 0.1);
            var r = a.Result();
            Assert.IsTrue(r.Correct);
            Assert.IsTrue(r.Submitted);
            Assert.AreEqual(4, r.StartBeat);
            Assert.AreEqual(g[4].Time, r.StartTime, 1e-9);
            Assert.AreEqual(4, r.Count);
            Assert.AreEqual(g[7].Time + 0.1, r.SubmitTime, 1e-9);
            Assert.AreEqual(1.0, r.Accuracy, 1e-9);
        }

        [Test]
        public void TheRightPatternOnTheWrongBeatIsNotCorrect()
        {
            var g = Grid(3, 4);
            var a = Fill(g, 4, 1);   // down left right up, but box 1 sat on beat 2 of the bar
            Assert.IsTrue(a.Full);
            Assert.IsFalse(a.MatchesHymn);
            a.Submit(g[4].Time + 0.1);
            var r = a.Result();
            Assert.IsFalse(r.Correct);
            Assert.IsTrue(r.Submitted, "it is an answer, just a wrong one");
        }

        [Test]
        public void ADifferentPatternIsNotCorrect()
        {
            var g = Grid(3, 4);
            var a = Fill(g, 3, 0, hymnPattern: 4);   // the 3-pattern on a 4/4 hymn
            Assert.IsTrue(a.Full);
            a.Submit(g[2].Time + 0.1);
            Assert.IsFalse(a.Result().Correct);
        }

        [Test]
        public void ANeverSubmittedAnswerIsNotAnAnswer()
        {
            var g = Grid(3, 4);
            var r = Fill(g, 4, 0).Result();
            Assert.IsFalse(r.Correct);
            Assert.IsFalse(r.Submitted);
            Assert.IsTrue(double.IsPositiveInfinity(r.SubmitTime));
            Assert.AreEqual(-1, r.StartBeat);
        }

        [Test]
        public void APickupHymnCountsTheTrueDownbeatNotTheFirstBeat()
        {
            // a 3/4 hymn that opens on a pickup: the grid starts on beat 3 of a bar, the downbeat is index 1
            var g = new List<MeterBeat>();
            for (int i = 0; i < 10; i++)
                g.Add(new MeterBeat { Time = 1.0 + i * Step, BarPosition = (i + 2) % 3, BeatsPerBar = 3 });
            Assert.AreEqual(2, g[0].BarPosition);
            Assert.AreEqual(0, g[1].BarPosition);
            Assert.IsTrue(Fill(g, 3, 1).MatchesHymn);
            Assert.IsFalse(Fill(g, 3, 0).MatchesHymn);   // starting on the pickup beat with "left" is not the downbeat
        }

        [Test]
        public void AccuracyIsTheMeanGradeOverTheFilledBoxes()
        {
            var g = Grid(2, 4);
            var p = Keys(g, 0, 3);
            p[1].Time += 0.060; // Great (600), the rest Perfect (1000)
            p[2].Time += 0.100; // Good (250)
            var a = Ans(g, 4);
            foreach (var k in p) a.Press(k.Action, k.Time);
            Assert.AreEqual((1000 + 600 + 250 + 1000) / 4000.0, a.Accuracy, 1e-9);
        }

        [Test]
        public void ThePressesClaimBeatsWithTheBeatWindow()
        {
            var g = Grid(2, 4);
            var a = Ans(g, 4);
            Assert.IsTrue(a.Press("down", g[0].Time + 0.120));    // inside 128 ms
            Assert.IsFalse(a.Press("left", g[1].Time + 0.2));     // 200 ms late: no beat claimed, and beat 2 went by
            Assert.AreEqual(0, a.Filled);
        }

        [Test]
        public void PressesOutsideTheAnswerWindowAreIgnored()
        {
            var g = Grid(3, 4);
            var a = new MeterAnswer(g, 4, g[1].Time - 0.1, g[2].Time + 0.1);
            Assert.IsFalse(a.Press("down", g[0].Time));   // before the window
            Assert.IsTrue(a.Press("down", g[1].Time));
            Assert.IsTrue(a.Press("left", g[2].Time));
            Assert.IsFalse(a.Press("right", g[3].Time));   // after it
            Assert.AreEqual(2, a.Filled);
        }

        [Test]
        public void ThePureFormReplaysKeysClearAndEnter()
        {
            var g = Grid(4, 4);
            var p = Keys(g, 0, 1);                 // down left ...
            p.Add(new MeterPress { Action = "b", Time = g[1].Time + 0.1 });   // ... clear
            p.AddRange(Keys(g, 4, 7));             // start again on the next bar line
            p.Add(Enter(g[7].Time + 0.05));
            var r = Run(g, 4, p);
            Assert.IsTrue(r.Correct);
            Assert.AreEqual(4, r.StartBeat);
            // an unsorted list is replayed in time order
            p.Reverse();
            Assert.IsTrue(Run(g, 4, p).Correct);
            // no Enter, no answer
            Assert.IsFalse(Run(g, 4, Keys(g, 0, 3)).Submitted);
            // Enter before the boxes are full does nothing, and the answer can still be finished
            var early = Keys(g, 0, 2);
            early.Add(Enter(g[2].Time + 0.05));
            early.AddRange(Keys(g, 3, 3));
            early.Add(Enter(g[3].Time + 0.05));
            var e = Run(g, 4, early);
            Assert.IsTrue(e.Correct);
            Assert.AreEqual(g[3].Time + 0.05, e.SubmitTime, 1e-9);
        }

        static MeterResult Result(bool correct, double submit, double acc, bool post = false) =>
            new MeterResult { Correct = correct, Submitted = true, SubmitTime = submit, StartBeat = correct ? 0 : -1, Accuracy = acc, PostReveal = post };

        [Test]
        public void RankingIsCorrectThenPreRevealThenEarlierEnterThenAccuracy()
        {
            var lateButBefore = Result(true, 30, 0.99);
            var earlyLoose = Result(true, 5, 0.50);
            var earlyTight = Result(true, 5, 0.90);
            var postFast = Result(true, 2, 1.0, post: true);
            var wrong = Result(false, 1, 1.0);
            var order = new[] { wrong, postFast, lateButBefore, earlyLoose, earlyTight }
                .OrderBy(r => r, new MeterRanking()).ToList();
            Assert.AreSame(earlyTight, order[0]);
            Assert.AreSame(earlyLoose, order[1]);
            Assert.AreSame(lateButBefore, order[2]);
            Assert.AreSame(postFast, order[3]);
            Assert.AreSame(wrong, order[4]);
            Assert.AreEqual(0, new MeterRanking().Compare(Result(false, 3, 0.2), Result(false, 9, 0.9)));
            Assert.Less(new MeterRanking().Compare(Result(true, 9, 0.5, post: true), Result(false, 1, 1)), 0, "a late right answer still beats a wrong one");
        }

        [Test]
        public void RankingWorksOnJudgedPlayers()
        {
            var g = Grid(3, 4);
            MeterResult Player(int from, double enterAfter, bool wrongKey = false)
            {
                var p = Keys(g, from, from + 3);
                if (wrongKey) p[0].Action = "up";
                p.Add(Enter(g[from + 3].Time + enterAfter));
                return Run(g, 4, p);
            }
            var fast = Player(0, 0.05);
            var slow = Player(4, 0.05);
            var wrong = Player(0, 0.05, wrongKey: true);
            var order = new[] { wrong, slow, fast }.OrderBy(r => r, new MeterRanking()).ToList();
            Assert.AreSame(fast, order[0]);
            Assert.AreSame(slow, order[1]);
            Assert.AreSame(wrong, order[2]);
        }
    }
}
