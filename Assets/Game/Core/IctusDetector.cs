// The conducting stroke (web games/swingKings/gesture.js createIctusDetector), no UnityEngine.
//
// Feed it (time, y) with y growing DOWNWARD in screen heights. It reports
//   Raise  the hand lifted clear of where it rested: the windup begins
//   Ictus  the sharp DECELERATION that ends a fast downward stroke: the downbeat. Not the fastest moment
//          of the stroke and not its start, the instant it stops. The ictus fires the swing and its time is
//          what the judge scores; stroke shape and size are not scored.
// Times are whatever the caller passes (audio time in the game), never read from a clock here.

using System;

namespace Bonanza.Core
{
    public enum GestureKind { Raise, Ictus }

    public struct GestureEvent
    {
        public GestureKind Kind;
        public double Time;
        public double Y;
    }

    public sealed class IctusDetector
    {
        public enum State { Rest, Raised, Striking }

        // web defaults
        public const double DefaultRiseMin = 0.06;     // lift (screen heights) that counts as a raise
        public const double DefaultVDown = 1.0;        // downward speed (heights/s) that starts a stroke
        public const double DefaultMinStroke = 0.08;   // stroke length that counts as a swing
        public const double DefaultDecel = 0.3;        // ictus when speed falls below this x the stroke's peak
        public const double DefaultRefractory = 0.15;  // seconds after an ictus before another can fire

        readonly double _riseMin, _vDown, _minStroke, _decel, _refractory;

        State _state = State.Rest;
        double _anchor;            // lowest point (largest y) the hand rested at
        bool _hasAnchor;
        double _top;               // highest point of the current windup
        double _peakV;
        bool _hasLast;
        double _lastT, _lastY, _vy;
        double _quietUntil = double.NegativeInfinity;

        public IctusDetector(double riseMin = DefaultRiseMin, double vDown = DefaultVDown, double minStroke = DefaultMinStroke,
            double decel = DefaultDecel, double refractory = DefaultRefractory)
        {
            _riseMin = riseMin; _vDown = vDown; _minStroke = minStroke; _decel = decel; _refractory = refractory;
        }

        public State Current => _state;

        /// Drop a half-finished gesture. The refractory period survives, like the web's.
        public void Reset()
        {
            _state = State.Rest; _hasAnchor = false; _hasLast = false; _vy = 0; _peakV = 0;
        }

        /// One position. Returns true and fills `ev` when it completes a raise or an ictus.
        public bool Feed(double t, double y, out GestureEvent ev)
        {
            ev = default;
            if (!_hasLast) { _hasLast = true; _lastT = t; _lastY = y; _anchor = y; _hasAnchor = true; return false; }
            double dt = t - _lastT;
            if (dt <= 0) return false;
            double raw = (y - _lastY) / dt;
            // Light smoothing: kills pixel jitter without delaying the stop by more than a frame
            // (the ictus is a sudden change, and must stay sudden).
            _vy = _vy * 0.35 + raw * 0.65;
            _lastT = t; _lastY = y;

            switch (_state)
            {
                case State.Rest:
                    _anchor = Math.Max(_anchor, y);
                    if (t >= _quietUntil && y < _anchor - _riseMin)
                    {
                        _state = State.Raised;
                        _top = y;
                        ev = new GestureEvent { Kind = GestureKind.Raise, Time = t, Y = y };
                        return true;
                    }
                    return false;

                case State.Raised:
                    _top = Math.Min(_top, y);
                    if (_vy > _vDown && y > _top + 0.01) { _state = State.Striking; _peakV = _vy; return false; }
                    // Lowered slowly back to rest: that was not a windup after all.
                    if (y > _anchor - _riseMin * 0.25 && _vy < _vDown) { _state = State.Rest; _anchor = y; }
                    return false;

                case State.Striking:
                    _peakV = Math.Max(_peakV, _vy);
                    if (_vy < _peakV * _decel)
                    {
                        double stroke = y - _top;
                        _state = State.Rest;
                        _anchor = y;
                        if (stroke < _minStroke) { _state = State.Raised; _top = Math.Min(_top, y); return false; }
                        _quietUntil = t + _refractory;
                        ev = new GestureEvent { Kind = GestureKind.Ictus, Time = t, Y = y };
                        return true;
                    }
                    return false;
            }
            return false;
        }
    }

    /// The mouse wrapper around the detector (web swingKings/index.js startPointerConduct + update()): moves
    /// feed it while the button is held, and a pointer that has stopped sends no events, so once it has been
    /// still for 40 ms (longer than any gap between moves mid-stroke) the resting position is fed again,
    /// stamped one frame after the last real move, which is when the hand actually stopped, not 40 ms later
    /// when we noticed. Without it the detector would never see the deceleration that IS the ictus.
    public sealed class PointerConductor
    {
        public const double StillAfter = 0.04;
        public const double StillStep = 1.0 / 60;

        public readonly IctusDetector Detector;
        bool _has;
        double _t, _y;

        public PointerConductor(IctusDetector detector = null) { Detector = detector ?? new IctusDetector(); }

        public void Reset() { Detector.Reset(); _has = false; }

        /// The last stamped sample time (a move, or the resting sample fed after it).
        public double LastTime => _t;

        /// A pointer move at game/audio time `t`, y in screen heights (0 top, 1 bottom).
        public bool Move(double t, double y, out GestureEvent ev)
        {
            _has = true; _t = t; _y = y;
            return Detector.Feed(t, y, out ev);
        }

        /// Once per frame at `now`. At 60 fps this is one resting sample per frame, exactly the web's; when frames
        /// are slow it catches up (several samples, one per 1/60 s, still stamped from the last move) so a slow
        /// frame rate cannot delay the ictus's timestamp or the swing. Call it again until it returns false.
        public bool Update(double now, out GestureEvent ev)
        {
            ev = default;
            while (_has && now - _t > StillAfter)
            {
                _t += StillStep;
                if (Detector.Feed(_t, _y, out ev)) return true;
            }
            return false;
        }
    }
}
