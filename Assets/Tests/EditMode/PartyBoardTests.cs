using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Bonanza.Core.Tests
{
    // The party board the hub and the winner screen draw: deltas, the order before the last round, ties, the stat line.
    public class PartyBoardTests
    {
        static PartySession Session(int n = 4)
        {
            var players = Enumerable.Range(0, n).Select(i => new PartyPlayer { Name = "P" + i, IsCpu = i > 0, Char = "bopp" }).ToList();
            return new PartySession(players, new List<string> { "a", "b", "c", "d" }, 1);
        }

        /// A fabricated round: places[i] is player i's place.
        static void Round(PartySession s, params int[] places)
        {
            var log = new PartyRoundLog { Game = s.CurrentGame, Rank = "A", Players = new List<PartyPost>() };
            for (int i = 0; i < places.Length; i++)
            {
                int pts = Party.PlacePoints[places[i] - 1];
                log.Players.Add(new PartyPost { Id = i, Place = places[i], Points = pts, Rank = i == 0 ? "B" : null });
                s.Players[i].Points += pts;
                if (places[i] == 1) s.Players[i].Wins++;
            }
            s.Scores.Add(log);
            s.Index++;
        }

        [Test]
        public void NoRoundYetMeansNoDeltaAndNoMovement()
        {
            var rows = PartyBoard.Build(Session());
            Assert.AreEqual(4, rows.Count);
            Assert.IsTrue(rows.All(r => r.Delta == 0 && r.PrevRank == r.Rank && r.PrevPoints == r.Points));
        }

        [Test]
        public void TheLastRoundShowsAsADeltaAndARowMove()
        {
            var s = Session();
            Round(s, 1, 2, 3, 4);
            Round(s, 4, 1, 2, 3);
            var rows = PartyBoard.Build(s);
            var p1 = rows.First(r => r.Id == 1);
            Assert.AreEqual(3, p1.Delta);
            Assert.AreEqual(5, p1.Points);
            Assert.AreEqual(2, p1.PrevPoints);
            Assert.AreEqual(0, p1.Rank);
            Assert.AreEqual(1, p1.PrevRank);   // second before, first now
            var p0 = rows.First(r => r.Id == 0);
            Assert.AreEqual(0, p0.Delta);
            Assert.AreEqual(3, p0.PrevPoints);
            Assert.AreEqual(0, p0.PrevRank);
        }

        [Test]
        public void LevelOnPointsAndWinsSharesAPlace()
        {
            var s = Session();
            Round(s, 1, 2, 3, 4);
            Round(s, 2, 1, 3, 4);
            var rows = PartyBoard.Build(s);
            Assert.IsTrue(rows[0].Tied && rows[1].Tied);
            Assert.AreEqual(1, rows[0].Place);
            Assert.AreEqual(1, rows[1].Place);
            Assert.AreEqual(3, rows[2].Place);
            Assert.IsFalse(rows[2].Tied);
        }

        [Test]
        public void StatLineNamesTheTopPlayersRoundWins()
        {
            var s = Session();
            Round(s, 1, 2, 3, 4); Round(s, 1, 2, 3, 4); Round(s, 2, 1, 3, 4);
            var rows = PartyBoard.Build(s);
            Assert.AreEqual("P0 TOOK 2 OF 3 ROUNDS", PartyBoard.StatLine(s, rows));
            Assert.AreEqual("B", PartyBoard.BestRank(s, out var game));
            Assert.AreEqual("a", game);
        }
    }
}
