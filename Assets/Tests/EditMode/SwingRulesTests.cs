// Swing Kings rules. The tap-power tests are ported from the web prototype's
// web/tests/swingkings.test.mjs (a critic round once found every hit was a
// HOME RUN; this pins the stepped mapping). The downbeat round that uses them
// (balls, bombs, free bars, outs, score) is pinned in DownbeatRoundTests.cs; the web
// chart's schedule and round flow are gone with the old game.

using System.Linq;
using NUnit.Framework;
using Bonanza.Core;

namespace Bonanza.Core.Tests
{
    public class SwingRulesTests
    {
        static TierId TierAt(double ms) => SwingRules.TierFor(SwingRules.PowerFromTiming(10 + ms / 1000, 10)).Id;

        [Test]
        public void PerfectTimingEarlyOrLateIsAHomeRun()
        {
            foreach (var ms in new[] { 0, 20, -30, Windows.PerfectMs - 0.5, -(Windows.PerfectMs - 0.5) })
                Assert.AreEqual(TierId.Homer, TierAt(ms), $"{ms}ms");
        }

        [Test]
        public void GreatTimingIsALineDriveAcrossTheWholeBand()
        {
            foreach (var ms in new[] { Windows.PerfectMs + 1, 60, -70, Windows.GreatMs - 0.5 })
                Assert.AreEqual(TierId.Liner, TierAt(ms), $"{ms}ms");
        }

        [Test]
        public void GoodTimingIsABunt()
        {
            foreach (var ms in new[] { Windows.GreatMs + 1, 100, -120, Windows.GoodMs - 0.5 })
                Assert.AreEqual(TierId.Bunt, TierAt(ms), $"{ms}ms");
        }

        [Test]
        public void PowerNeverRisesAsTimingGetsWorse()
        {
            double prev = double.PositiveInfinity;
            for (double ms = 0; ms <= Windows.GoodMs + 20; ms += 2)
            {
                double p = SwingRules.PowerFromTiming(10 + ms / 1000, 10);
                Assert.LessOrEqual(p, prev + 1e-12, $"{ms}ms");
                prev = p;
            }
        }

        [Test]
        public void FinaleHomeRunIsAGrandSlam()
        {
            Assert.AreEqual(TierId.Slam, SwingRules.TierFor(1, finale: true).Id);
            Assert.AreEqual(TierId.Liner, SwingRules.TierFor(0.5, finale: true).Id);
        }

        [Test]
        public void HoldPowerTapIsZeroIdealIsFullAndOverHoldingSags()
        {
            Assert.AreEqual(0, SwingRules.PowerForHold(0.05, 2));
            Assert.AreEqual(1, SwingRules.PowerForHold(2, 2), 1e-9);
            Assert.Less(SwingRules.PowerForHold(8, 2), SwingRules.PowerForHold(2, 2));
        }
    }
}
