// Output-latency compensation, ported from web/src/core/clock.js refreshOutputLatency().
//
// The audio clock subtracts the device's output latency so "now" is when the player HEARS it. The
// browser's estimate wobbles, and applying a jump directly would jump the beat on screen and shift
// every press being judged, so while a round runs a new estimate is slewed in: ignored inside a
// 2 ms deadband, then moved by at most 2 ms per refresh (the caller refreshes once a second).
// Outside a round (menus, before the first start) the estimate is taken as is.

using System;

namespace Bonanza.Core
{
    public sealed class LatencySlew
    {
        public const double Deadband = 0.002;     // ignore wobble below this (seconds)
        public const double Step = 0.002;         // max change per refresh while running
        public const double MaxValid = 0.5;       // web: l >= 0 && l < 0.5 is a usable outputLatency

        /// The latency currently applied, seconds.
        public double Value { get; private set; }

        /// Seconds that came out of the last Resolve() call's input; for logs.
        public double Target { get; private set; }

        /// Browser reading -> the estimate to aim at. Negative or NaN means "not exposed".
        /// outputLatency when it is a sane number, else baseLatency (a floor, not the truth, but it
        /// beats zero), else 0. Same order as the web.
        public static double Resolve(double outputLatency, double baseLatency)
        {
            if (IsFinite(outputLatency) && outputLatency >= 0 && outputLatency < MaxValid) return outputLatency;
            return IsFinite(baseLatency) && baseLatency >= 0 ? baseLatency : 0;
        }

        static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        /// One refresh. `running`: a round is being played (slew); otherwise the target is taken at once.
        public double Refresh(double target, bool running)
        {
            Target = target;
            if (!running)
            {
                Value = target;
            }
            else
            {
                double d = target - Value;
                if (Math.Abs(d) > Deadband) Value += Math.Max(-Step, Math.Min(Step, d));
            }
            return Value;
        }

        /// Forget the applied value (tests, restarts).
        public void Reset(double value = 0) { Value = value; Target = value; }
    }
}
