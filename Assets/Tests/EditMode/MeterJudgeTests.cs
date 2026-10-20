// Pins the Hymn Meter rules: the conducting pattern of each meter and the run judge
// (claiming beats, runs, the answer that counts, ranking). Pure C#, no hymn files:
// beat grids are built in memory at 120 bpm (one beat every 0.5 s).

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

        static MeterResult Run(List<MeterBeat> g, List<MeterPress> p) =>
            MeterJudge.Judge(g, 0, g[g.Count - 1].Time + 1, p);

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
        public void StartingOnTheDownbeatForOneBarIsValid()
        {
            var g = Grid(3, 4);
            var r = Run(g, Keys(g, 0, 3));
            Assert.IsTrue(r.Correct);
            Assert.AreEqual(0, r.StartBeat);
            Assert.AreEqual(g[0].Time, r.StartTime, 1e-9);
            Assert.AreEqual(1.0, r.Accuracy, 1e-9);
            Assert.AreEqual(0, r.FreeTicks);
        }

        [Test]
        public void StartingMidBarAndSpanningTheBarLineIsValid()
        {
            var g = Grid(3, 4);
            var r = Run(g, Keys(g, 1, 7)); // beats 2-4 then 1-4: seven keys
            Assert.IsTrue(r.Correct);
            Assert.AreEqual(1, r.StartBeat);
            Assert.AreEqual(1, r.Runs.Count);
            Assert.AreEqual(7, r.Runs[0].Length);
        }

        [Test]
        public void ARunThatEndsMidBarIsNotValid()
        {
            var g = Grid(3, 4);
            Assert.IsFalse(Run(g, Keys(g, 0, 2)).Correct);   // 1-3
            Assert.IsFalse(Run(g, Keys(g, 1, 3)).Correct);   // 2-4 ends on the bar end but is under a bar
            Assert.IsFalse(Run(g, Keys(g, 0, 0)).Correct);
            Assert.IsFalse(Run(g, new List<MeterPress>()).Correct);
        }

        [Test]
        public void ARunPastTheBarEndKeepsItsValidPrefixAndStart()
        {
            var g = Grid(3, 4);
            var r = Run(g, Keys(g, 0, 5)); // 1-4 then 1-2 and stop
            Assert.IsTrue(r.Correct);
            Assert.AreEqual(0, r.StartBeat);
            Assert.AreEqual(1, r.Runs.Count);
        }

        [Test]
        public void AWrongKeyBreaksTheRun()
        {
            var g = Grid(3, 4);
            var p = Keys(g, 0, 5);
            p[2].Action = "up"; // beat 3 should be "right"
            var r = Run(g, p);
            Assert.IsFalse(r.Correct); // 1-2, break, 4-6: nothing reaches a full bar ending on a bar end
            Assert.AreEqual(2, r.Runs.Count);
        }

        [Test]
        public void ASkippedBeatBreaksTheRun()
        {
            var g = Grid(3, 4);
            var p = Keys(g, 0, 7);
            p.RemoveAt(2); // beat 3 never pressed
            var r = Run(g, p);
            Assert.IsTrue(r.Correct); // beats 1-2 are cut off; the run restarts on beat 4 and runs to 8
            Assert.AreEqual(3, r.StartBeat);
            Assert.AreEqual(2, r.Runs.Count);
        }

        [Test]
        public void TheLastValidRunCounts()
        {
            var g = Grid(4, 4);
            var p = Keys(g, 0, 3);          // a valid bar...
            p.AddRange(Keys(g, 5, 11));     // ...skip beat 5, restart on beat 6: 6-12 is valid too
            var r = Run(g, p);
            Assert.IsTrue(r.Correct);
            Assert.AreEqual(5, r.StartBeat); // the later run replaces the earlier one
        }

        [Test]
        public void ALaterRestartThatNeverBecomesValidKeepsTheEarlierAnswer()
        {
            var g = Grid(4, 4);
            var p = Keys(g, 0, 3);
            p.AddRange(Keys(g, 6, 7).Take(1)); // a stray correct key after a gap
            var r = Run(g, p);
            Assert.IsTrue(r.Correct);
            Assert.AreEqual(0, r.StartBeat);
        }

        [Test]
        public void TheThreeFourPatternOnAFourFourHymnIsNotCorrect()
        {
            var g = Grid(3, 4);
            var three = new[] { "left", "right", "up" };
            var p = new List<MeterPress>();
            for (int i = 0; i < 12; i++) p.Add(new MeterPress { Action = three[i % 3], Time = g[i].Time });
            Assert.IsFalse(Run(g, p).Correct);
        }

        [Test]
        public void TheTwoBeatPatternWorks()
        {
            var g = Grid(3, 2);
            var r = Run(g, Keys(g, 0, 1));
            Assert.IsTrue(r.Correct);
            Assert.AreEqual(0, r.StartBeat);
            Assert.AreEqual("right", Meter.KeyFor(2, 0));
            Assert.IsFalse(Run(g, Keys(g, 0, 0)).Correct);
            // joining on the up beat and running through two bars
            Assert.IsTrue(Run(g, Keys(g, 1, 4)).Correct);
        }

        [Test]
        public void ThreeFourNeedsThreeKeysEndingOnTheThird()
        {
            var g = Grid(3, 3);
            Assert.IsTrue(Run(g, Keys(g, 0, 2)).Correct);
            Assert.IsFalse(Run(g, Keys(g, 0, 1)).Correct);
            Assert.IsTrue(Run(g, Keys(g, 2, 5)).Correct);
        }

        [Test]
        public void PressesThatClaimNoBeatAreFreeTicks()
        {
            var g = Grid(2, 4);
            var p = Keys(g, 0, 3);
            p.Add(new MeterPress { Action = "down", Time = g[3].Time + 0.25 }); // between beats
            p.Add(new MeterPress { Action = "up", Time = 0.1 });                 // before the music
            var r = Run(g, p);
            Assert.IsTrue(r.Correct);
            Assert.AreEqual(2, r.FreeTicks);
            Assert.AreEqual(1.0, r.Accuracy, 1e-9);
        }

        [Test]
        public void ADoublePressClaimsOneBeatAndTheSecondIsAFreeTick()
        {
            var g = Grid(2, 4);
            var p = Keys(g, 0, 3);
            p.Add(new MeterPress { Action = "down", Time = g[0].Time + 0.01 });
            var r = Run(g, p);
            Assert.IsTrue(r.Correct);
            Assert.AreEqual(1, r.FreeTicks);
        }

        [Test]
        public void AccuracyUsesTheJudgeWindowsOverTheCountedRun()
        {
            var g = Grid(2, 4);
            var p = Keys(g, 0, 3);
            p[1].Time += 0.060; // Great (600), the rest Perfect (1000)
            p[2].Time += 0.100; // Good (250)
            var r = Run(g, p);
            Assert.IsTrue(r.Correct);
            Assert.AreEqual((1000 + 600 + 250 + 1000) / 4000.0, r.Accuracy, 1e-9);
        }

        [Test]
        public void APressOutsideTheGoodWindowClaimsNothing()
        {
            var g = Grid(2, 4);
            var p = Keys(g, 0, 3);
            p[1].Time += 0.2; // 200 ms late: not on beat 2
            var r = Run(g, p);
            Assert.IsFalse(r.Correct);
            Assert.AreEqual(1, r.FreeTicks);
        }

        [Test]
        public void PressesOutsideTheAnswerWindowAreIgnored()
        {
            var g = Grid(3, 4);
            var p = Keys(g, 0, 3);
            // window closes after beat 3 (index 2): the fourth key never counts
            var r = MeterJudge.Judge(g, 0, g[2].Time + 0.1, p);
            Assert.IsFalse(r.Correct);
            Assert.AreEqual(0, r.FreeTicks);
        }

        static MeterResult Result(bool correct, int start, double acc) =>
            new MeterResult { Correct = correct, StartBeat = correct ? start : -1, Accuracy = acc };

        [Test]
        public void RankingIsCorrectThenEarlierStartThenAccuracy()
        {
            var late = Result(true, 8, 0.99);
            var earlyLoose = Result(true, 1, 0.50);
            var earlyTight = Result(true, 1, 0.90);
            var wrong = Result(false, 0, 1.0);
            var order = new[] { wrong, late, earlyLoose, earlyTight }
                .OrderBy(r => r, new MeterRanking()).ToList();
            Assert.AreSame(earlyTight, order[0]);
            Assert.AreSame(earlyLoose, order[1]);
            Assert.AreSame(late, order[2]);
            Assert.AreSame(wrong, order[3]);
            Assert.AreEqual(0, new MeterRanking().Compare(Result(false, 0, 0.2), Result(false, 0, 0.9)));
        }

        [Test]
        public void RankingWorksOnJudgedPlayers()
        {
            var g = Grid(3, 4);
            var fast = Run(g, Keys(g, 0, 3));
            var slow = Run(g, Keys(g, 4, 7));
            var wrongKey = Keys(g, 0, 3);
            wrongKey[0].Action = "up";
            var wrong = Run(g, wrongKey);
            var order = new[] { wrong, slow, fast }.OrderBy(r => r, new MeterRanking()).ToList();
            Assert.AreSame(fast, order[0]);
            Assert.AreSame(slow, order[1]);
            Assert.AreSame(wrong, order[2]);
        }
    }
}
