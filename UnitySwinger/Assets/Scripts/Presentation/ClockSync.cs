using System.Diagnostics;
using UnityEngine;

namespace Swinger.Presentation
{
    // Single clock-domain design (Build Plan v3 Phase 3): the CPU clock is
    // canonical -- samples and beat schedules live there. AudioSettings.dspTime
    // (sample-accurate, drives when clicks are actually heard) is mapped to the
    // canonical domain via a measured offset, sampled at startup and re-checked
    // periodically for drift. Never mix domains implicitly.
    //
    // Calibration is NOT a synchronous busy-loop: dspTime only advances once
    // per audio buffer callback (on a separate thread), so spinning inside a
    // single call on the main thread can complete before a single real tick
    // happens, silently degrading to one low-quality sample. Callers must
    // spread calibration across several real frames -- see Calibrate().
    public sealed class ClockSync
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private double _lastDspSeen = double.NegativeInfinity;
        private double _sum;
        private int _taken;

        public double StartupOffset { get; private set; }
        public bool IsCalibrated { get; private set; }

        // Canonical CPU-clock "now", seconds, double precision.
        public double CanonicalNow() => _stopwatch.Elapsed.TotalSeconds;

        // Call once per frame (e.g. from a calibration coroutine) until it
        // returns true. Only counts a sample when dspTime has actually ticked
        // forward since the last call, so repeated calls within the same
        // audio buffer window do not bias the average.
        public bool TickCalibration(int samplesNeeded = 8)
        {
            double dsp = AudioSettings.dspTime;
            if (dsp != _lastDspSeen)
            {
                _sum += dsp - CanonicalNow();
                _lastDspSeen = dsp;
                _taken++;
            }
            if (_taken >= samplesNeeded)
            {
                StartupOffset = _sum / _taken;
                IsCalibrated = true;
                return true;
            }
            return false;
        }

        // Converts a canonical-domain timestamp to the dspTime domain for
        // AudioSource.PlayScheduled, using the offset measured at startup
        // (not re-measured live) -- that is precisely what this phase's exit
        // criterion is checking the validity of over a long run.
        public double ToDspTime(double canonicalTime) => canonicalTime + StartupOffset;

        // Re-measures the current offset without changing StartupOffset, so
        // callers can log how far the live offset has drifted from the one
        // actually used for scheduling.
        public double MeasureCurrentOffset() => AudioSettings.dspTime - CanonicalNow();
    }
}
