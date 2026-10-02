using NUnit.Framework;

namespace RhythmGame.Core.Tests
{
    public class RecordsTests
    {
        static Outcome Run(int score, string rank, double acc, int combo) =>
            new Outcome { Score = score, Rank = rank, Accuracy = acc, MaxCombo = combo, Perfect = 3, Great = 2, Good = 1, Miss = 4 };

        [Test]
        public void EachFieldKeepsItsOwnBest()
        {
            var r = Records.Submit(default, Run(500, "A", 0.8, 10), out bool score, out bool rank);
            Assert.IsTrue(score); Assert.IsTrue(rank);
            Assert.AreEqual((500, "A", 0.8, 1, 10), (r.Score, r.Rank, r.Accuracy, r.Plays, r.MaxCombo));

            // Lower score, better rank and accuracy: only those move.
            r = Records.Submit(r, Run(400, "S", 0.9, 5), out score, out rank);
            Assert.IsFalse(score); Assert.IsTrue(rank);
            Assert.AreEqual((500, "S", 0.9, 2, 10), (r.Score, r.Rank, r.Accuracy, r.Plays, r.MaxCombo));

            // Higher score, worse rank: the S stays.
            r = Records.Submit(r, Run(600, "B", 0.7, 20), out score, out rank);
            Assert.IsTrue(score); Assert.IsFalse(rank);
            Assert.AreEqual((600, "S", 0.9, 3, 20), (r.Score, r.Rank, r.Accuracy, r.Plays, r.MaxCombo));

            // Equal rank is not a new rank.
            Records.Submit(r, Run(1, "S", 0, 0), out _, out rank);
            Assert.IsFalse(rank);
        }

        [Test]
        public void AZeroScoreIsNotANewBestButItsRankCounts()
        {
            var r = Records.Submit(default, Run(0, "D", 0, 0), out bool score, out bool rank);
            Assert.IsFalse(score, "web: score > prev.score with prev 0");
            Assert.IsTrue(rank);
            Assert.AreEqual("D", r.Rank);
            Assert.AreEqual(1, r.Plays);
        }

        [Test]
        public void ARoundCountsItsJudgedHitsAsNotes()
        {
            var s = Records.AddRound(default, Run(0, "D", 0, 0));
            s = Records.AddRound(s, Run(0, "D", 0, 0));
            Assert.AreEqual(2, s.Rounds);
            Assert.AreEqual(12, s.Notes, "perfect+great+good, never misses");
        }
    }
}
