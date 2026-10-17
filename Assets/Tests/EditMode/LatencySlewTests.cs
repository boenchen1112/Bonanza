using NUnit.Framework;

namespace Bonanza.Core.Tests
{
    public class LatencySlewTests
    {
        const double Nan = double.NaN;

        [Test] public void Resolve_prefers_output_latency_then_base_then_zero()
        {
            Assert.AreEqual(0.06, LatencySlew.Resolve(0.06, 0.01), 1e-12);
            Assert.AreEqual(0.0, LatencySlew.Resolve(0.0, 0.01), 1e-12);      // a reported zero is a value
            Assert.AreEqual(0.01, LatencySlew.Resolve(-1, 0.01), 1e-12);      // not exposed
            Assert.AreEqual(0.01, LatencySlew.Resolve(Nan, 0.01), 1e-12);
            Assert.AreEqual(0.01, LatencySlew.Resolve(0.5, 0.01), 1e-12);     // out of range
            Assert.AreEqual(0.01, LatencySlew.Resolve(2, 0.01), 1e-12);
            Assert.AreEqual(0.0, LatencySlew.Resolve(-1, -1), 1e-12);
            Assert.AreEqual(0.0, LatencySlew.Resolve(Nan, double.PositiveInfinity), 1e-12);
        }

        [Test] public void Not_running_takes_the_target_at_once()
        {
            var s = new LatencySlew();
            Assert.AreEqual(0.06, s.Refresh(0.06, false), 1e-12);
            Assert.AreEqual(0.01, s.Refresh(0.01, false), 1e-12);
        }

        [Test] public void Running_ignores_wobble_inside_the_deadband()
        {
            var s = new LatencySlew();
            s.Reset(0.040);
            Assert.AreEqual(0.040, s.Refresh(0.0415, true), 1e-12);
            Assert.AreEqual(0.040, s.Refresh(0.0385, true), 1e-12);
            Assert.AreEqual(0.040, s.Refresh(0.0419, true), 1e-12);
        }

        [Test] public void Running_moves_two_ms_per_refresh_toward_the_target()
        {
            var s = new LatencySlew();
            Assert.AreEqual(0.002, s.Refresh(0.060, true), 1e-12);
            Assert.AreEqual(0.004, s.Refresh(0.060, true), 1e-12);
            for (int i = 2; i < 40; i++) s.Refresh(0.060, true);
            Assert.That(s.Value, Is.InRange(0.058 - 1e-9, 0.060 + 1e-9));      // 58 ms in 29 steps; the last 2 ms sits on the deadband edge
            // and back down
            for (int i = 0; i < 40; i++) s.Refresh(0.0, true);
            Assert.LessOrEqual(s.Value, 0.0021);                               // stops inside the deadband
        }

        [Test] public void Steps_are_capped_then_settle_in_the_deadband()
        {
            var s = new LatencySlew();
            s.Reset(0.010);
            Assert.AreEqual(0.012, s.Refresh(0.0125, true), 1e-12);           // 2.5 ms away: one 2 ms step
            Assert.AreEqual(0.012, s.Refresh(0.0125, true), 1e-12);           // 0.5 ms left is inside the deadband
        }
    }
}
