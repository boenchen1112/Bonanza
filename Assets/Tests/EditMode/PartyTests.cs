using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Bonanza.Core.Tests
{
    public class PartyTests
    {
        static readonly string[] Ids = { "swing-kings", "chomp-chorus", "drumline-dash", "bounce-brigade", "finale-fever" };

        [Test]
        public void PlaylistMatchesTheWebBuild()
        {
            foreach (var g in GoldenFile.Read("party-playlist.txt"))
            {
                var list = Party.Playlist(Ids, g.Int("len"), new Rng((uint)g.Num("seed")));
                Assert.AreEqual(g.Str("games"), string.Join(",", list), $"seed {g.Num("seed")} len {g.Int("len")}");
                Assert.AreEqual(Party.FinaleId, list.Last(), "the finale closes every party");
            }
        }

        [Test]
        public void CpuRoundsMatchTheWebBuild()
        {
            foreach (var g in GoldenFile.Read("party-cpu.txt"))
            {
                var r = Party.CpuRound(g.Num("skill"), new Rng((uint)g.Num("seed")), g.Int("refScore"), g.Num("refAcc"));
                string at = $"skill {g.Num("skill")} seed {g.Num("seed")} ref {g.Int("refScore")}";
                Assert.AreEqual(g.Int("score"), r.Score, at);
                Assert.AreEqual(g.Num("accuracy"), r.Accuracy, 1e-12, at);
                Assert.AreEqual(g.Str("rank"), r.Rank, at);
            }
        }

        static Outcome Human(int score, string rank, double acc, List<FieldRow> field = null) =>
            new Outcome { Score = score, Rank = rank, Accuracy = acc, Field = field };

        [Test]
        public void AScriptedPartyMatchesTheWebBuild()
        {
            var s = new PartySession(new[]
            {
                new PartyPlayer { Name = "YOU" },
                new PartyPlayer { Name = "ZIZZ", IsCpu = true, CpuSkill = 0.40 },
                new PartyPlayer { Name = "KWARK", IsCpu = true, CpuSkill = 0.63 },
                new PartyPlayer { Name = "TUFF", IsCpu = true, CpuSkill = 0.86 },
            }, new List<string> { "chomp-chorus", "drumline-dash", "bounce-brigade", "finale-fever" }, 0x1234abcd);

            var field = new List<FieldRow> { new FieldRow { Id = 0, Place = 1 }, new FieldRow { Id = 1, Place = 3 }, new FieldRow { Id = 2, Place = 2 }, new FieldRow { Id = 3, Place = 0 } };
            foreach (var h in new[] { Human(820, "B", 0.86), Human(700, "A", 0.93, field), Human(1000, "S", 1), Human(450, "D", 0.6) })
                s.RecordRound(s.CurrentGame, h);

            var golden = GoldenFile.Read("party-session.txt");
            var rows = s.Scores.SelectMany(r => r.Players.Select(p => (r, p))).ToList();
            Assert.AreEqual(golden.Count, rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                var g = golden[i]; var (r, p) = rows[i]; string at = $"row {i}";
                Assert.AreEqual(g.Str("game"), r.Game, at);
                Assert.AreEqual(g.Flag("sim"), r.Sim, at);
                Assert.AreEqual(g.Int("id"), p.Id, at);
                if (g.Str("score") == "-") Assert.IsNull(p.Score, at);
                else { Assert.AreEqual(g.Int("score"), p.Score, at); Assert.AreEqual(g.Str("rank"), p.Rank, at); }
                Assert.AreEqual(g.Int("place"), p.Place, at);
                Assert.AreEqual(g.Int("points"), p.Points, at);
            }
            Assert.IsTrue(s.Done);
            Assert.AreEqual("8/1 0/0 5/0 11/3", string.Join(" ", s.Players.Select(p => $"{p.Points}/{p.Wins}")));
            CollectionAssert.AreEqual(new[] { 3 }, s.Winners);
        }

        [Test]
        public void LevelOnPointsAndWinsSharesTheCrown()
        {
            var places = Party.RankPlaces(new[] { (0, 5.0), (1, 7.0), (2, 5.0), (3, 1.0) }, r => r.Item1, r => r.Item2);
            Assert.AreEqual(1, places[1]);
            Assert.AreEqual(2, places[0]);
            Assert.AreEqual(2, places[2]);
            Assert.AreEqual(4, places[3]);
        }
    }
}
