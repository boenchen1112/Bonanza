using System.Collections.Generic;
using NUnit.Framework;

namespace Bonanza.Core.Tests
{
    public class CoreContractTests
    {
        [Test]
        public void CountInSaysThreeTwoOneGo()
        {
            var said = new List<string>();
            for (int b = -8; b <= 0; b++) said.Add(CountIn.TextFor(b, 8, 3, "PLAY BALL!"));
            CollectionAssert.AreEqual(new string[] { null, null, null, null, "3", "2", "1", "PLAY BALL!", null }, said);
        }

        [Test]
        public void CountInGoOnDownbeat()
        {
            var said = new List<string>();
            for (int b = -5; b <= 0; b++) said.Add(CountIn.TextFor(b, 8, 4, "GO!", true));
            CollectionAssert.AreEqual(new[] { null, "4", "3", "2", "1", "GO!" }, said);
        }

        [Test]
        public void AccentNeverNegative()
        {
            Assert.AreEqual(3, CountIn.Accent(-1));
            Assert.AreEqual(0, CountIn.Accent(-4));
        }

        [Test]
        public void OutcomeClampsAccuracyAndDropsBadField()
        {
            var o = Outcome.Build(12.5, 1.7, "A", null, 0, new List<FieldRow> { new FieldRow { Id = 0, Place = 1 } });
            Assert.AreEqual(1.0, o.Accuracy);
            Assert.AreEqual(13, o.Score);
            Assert.IsNull(o.Field);
        }

        [Test]
        public void OutcomeKeepsDeadHeat()
        {
            var f = new List<FieldRow> { new FieldRow { Id = 0, Place = 0 }, new FieldRow { Id = 1, Place = 0 }, new FieldRow { Id = -1, Place = 2 } };
            Assert.IsTrue(Outcome.CheckField(f, out _));
            f.Add(new FieldRow { Id = 2, Place = 4 });
            Assert.IsFalse(Outcome.CheckField(f, out _));
        }
    }
}
