// A scripted conductor for the autoplay gate and the tests: the motion a player makes for one pitch, as a
// pointer height over time relative to the pitch's contact time T, so the REAL detector finds the ictus.
//
//   rest low  ->  lift over 0.4 s  ->  hold  ->  accelerating downstroke  ->  stops dead at the bottom
//
// The detector reports the ictus two samples after the stroke reaches the bottom (the smoothed speed has to fall),
// so the stop lands StopLead before T and the ictus lands on T when samples are on a T-anchored grid.

using System;

namespace Bonanza.Core
{
    public static class ConductScript
    {
        public const double Low = 0.7, Lift = 0.25;
        public const double Frame = 1.0 / 60;
        /// Seconds before T that the stroke reaches the bottom (two sample frames: the detector sees the stop one
        /// sample later and the speed fall below the threshold on the next).
        public const double StopLead = 2 * Frame;
        public const double StrokeLen = 0.12, HoldLen = 0.18, LiftLen = 0.4;

        /// Pointer height (0 top .. 1 bottom) at `u` seconds from the contact time.
        public static double Y(double u)
        {
            double stop = -StopLead;
            double strikeAt = stop - StrokeLen;       // downstroke starts
            double liftEnd = strikeAt - HoldLen;
            double liftStart = liftEnd - LiftLen;
            double high = Low - Lift;
            if (u <= liftStart) return Low;
            if (u < liftEnd) return Low - Lift * ((u - liftStart) / LiftLen);
            if (u < strikeAt) return high;
            if (u < stop) { double w = (u - strikeAt) / StrokeLen; return high + Lift * w * w; }
            return Low;
        }

        /// The first sample time after `t` on the 60 Hz grid anchored at the contact time `target`.
        public static double NextSample(double t, double target)
        {
            double m = Math.Floor((target - t) * 60 - 1e-9);
            if (m < 0) m = 0;
            return target - m * Frame;
        }
    }

    /// The scripted conductor as a stepper: for each pitch, press at its launch, then play the motion above into a
    /// real PointerConductor at 60 Hz samples anchored to the contact time. The swing is whatever the detector
    /// reports. Used by the autoplay gate (?autoplay&swingInput=mouse) and by the tests.
    public sealed class ConductBot
    {
        public readonly PointerConductor Conductor = new PointerConductor();
        readonly double[] _targets, _presses;
        int _k;                 // the pitch being conducted
        bool _pressed;
        double _t;              // last sample time
        double _lastIctus = double.NegativeInfinity;

        /// `targets`: contact times, ascending. `launches`: when each ball leaves the machine.
        public ConductBot(double[] targets, double[] launches, double startTime)
        {
            _targets = targets;
            _presses = new double[targets.Length];
            for (int i = 0; i < targets.Length; i++) _presses[i] = launches[i];
            _t = startTime;
        }

        public bool Done => _k >= _targets.Length;

        /// Play every sample up to `now`. `begin(time)` = the button goes down, `ictus(time)` = the detector fired.
        public void Advance(double now, Action<double> begin, Action<double> ictus)
        {
            while (_k < _targets.Length)
            {
                double target = _targets[_k];
                double s = ConductScript.NextSample(_t, target);
                if (target <= _t) { _k++; _pressed = false; continue; }
                if (s > now) return;
                if (!_pressed && s >= _presses[_k])
                {
                    // Just after the previous swing, never on the same sample.
                    if (s > _lastIctus + 0.02 || double.IsNegativeInfinity(_lastIctus))
                    {
                        _pressed = true;
                        Conductor.Reset();
                        begin(s);
                    }
                }
                _t = s;
                if (_pressed)
                {
                    if (Conductor.Move(s, ConductScript.Y(s - target), out var ev) && ev.Kind == GestureKind.Ictus)
                    {
                        _lastIctus = ev.Time;
                        ictus(ev.Time);
                        _k++; _pressed = false;
                    }
                }
            }
        }
    }
}
