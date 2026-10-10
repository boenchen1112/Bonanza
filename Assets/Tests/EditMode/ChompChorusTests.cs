using System.Collections.Generic;
using NUnit.Framework;

namespace Bonanza.Core.Tests
{
    public class ChompChorusTests
    {
        [Test]
        public void APartysCpusSingTheOtherVoicesInOrder()
        {
            var lineup = new List<PartyPlayer>
            {
                new PartyPlayer { Id = 0, Name = "ZIZZ", IsCpu = true, CpuSkill = 0.40 },
                new PartyPlayer { Id = 1, Name = "YOU" },
                new PartyPlayer { Id = 2, Name = "TUFF", IsCpu = true, CpuSkill = 0.86 },
            };
            var r = ChompRound.ForLineup(lineup);
            Assert.AreEqual(1, r.Human, "the human's lineup slot");
            CollectionAssert.AreEqual(new[] { 0.40, 0, 0.86, 0.8 }, r.CpuSkill, "an empty voice sings at 0.8");

            var solo = ChompRound.ForLineup(null);
            Assert.AreEqual(0, solo.Human);
            CollectionAssert.AreEqual(new[] { 0, 0.8, 0.8, 0.8 }, solo.CpuSkill);
        }

        [TestCase(0), TestCase(1), TestCase(2), TestCase(3)]
        public void ChartMatchesTheWebBuild(int human)
        {
            var golden = GoldenFile.Read($"chomp-chorus-h{human}.txt");
            var chart = ChompChart.Build(human);
            Assert.AreEqual(golden.Count, chart.Count, "note count");
            for (int i = 0; i < chart.Count; i++)
            {
                var g = golden[i]; var n = chart[i];
                string at = $"note {i}";
                Assert.AreEqual(g.Num("beat"), n.Beat, 1e-12, at);
                Assert.AreEqual(g.Int("lane"), n.Lane, at);
                Assert.AreEqual(g.Flag("intro"), n.Intro, at);
                Assert.AreEqual(g.Int("run"), n.Run, at);
                Assert.AreEqual(g.Int("runStep"), n.RunStep, at);
                Assert.AreEqual(g.Flag("finale"), n.Finale, at);
                Assert.AreEqual(g.Num("lead"), n.Lead, 1e-12, at);
                Assert.AreEqual(g.Num("holdBeats"), n.HoldBeats, 1e-12, at);
            }
        }

        [Test]
        public void CpuRollsMatchTheWebBuild()
        {
            var golden = GoldenFile.Read("chomp-chorus-cpu.txt");
            var skill = new[] { 0, 0.8, 0.8, 0.8 };
            // Rolled in chart order; compare before the due-time sort by keying on (beat, lane).
            var rolls = ChompRound.RollCpu(ChompChart.Build(0), 0, skill, 0xc40f, null);
            var byKey = new Dictionary<(double, int), CpuNote>();
            foreach (var r in rolls) byKey[(r.Src.Beat, r.Src.Lane)] = r;
            Assert.AreEqual(golden.Count, rolls.Count);
            foreach (var g in golden)
            {
                var r = byKey[(g.Num("beat"), g.Int("lane"))];
                Assert.AreEqual(g.Str("verdict"), r.Verdict.ToString().ToLowerInvariant(), $"beat {g.Num("beat")} lane {g.Int("lane")}");
                Assert.AreEqual(g.Num("errMs"), r.ErrMs, 1e-9);
            }
        }

        [Test]
        public void PerfectHumanRunScoresS()
        {
            var round = new ChompRound(2);
            round.Start(10);
            int humanNotes = GoldenFile.Read("chomp-chorus-h2.txt").FindAll(g => g.Int("lane") == 2).Count;
            Assert.AreEqual(70, humanNotes, "lane 2 is 70 notes in the web build (lane 0 is 68)");
            foreach (var n in round.Judge.Notes) round.Press(n.Time);
            round.Update(round.Map.TimeAt(ChompChart.EndBeat) + 0.01);
            var o = round.Result();
            Assert.NotNull(o);
            Assert.AreEqual("S", o.Rank);
            Assert.AreEqual(1000, o.Score);
            Assert.AreEqual(humanNotes, o.Perfect);
            Assert.AreEqual(0, o.Miss);
        }

        [Test]
        public void AMissedLaneDropsOutUntilItsNextHit()
        {
            var round = new ChompRound(0);
            round.Start(0);
            var notes = round.Judge.Notes;
            round.Update(notes[0].Time + 0.2);           // first note expires
            Assert.IsFalse(round.Alive[0]);
            round.Press(notes[1].Time);
            Assert.IsTrue(round.Alive[0]);
        }
    }
}
