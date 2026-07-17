using System.Collections.Generic;

namespace Swinger.Logic
{
    // Turns a raw gyro-magnitude stream into discrete, timestamped ictus
    // events -- the direction-change/deceleration point right after the
    // swing peak, not the peak itself.
    public sealed class IctusEvent
    {
        public double Timestamp;
        public double PeakMagnitude;
        public double RiseDuration;
        // Max per-sample positive smoothed-magnitude delta/sec seen during the
        // RISING state. Preferred sharpness input at device sample rates below
        // ~100Hz, where RiseDuration quantizes to 1-2 samples and
        // peak/rise_duration becomes unstable (F2).
        public double MaxDerivative;

        public IctusEvent(double timestamp, double peakMagnitude, double riseDuration, double maxDerivative = 0.0)
        {
            Timestamp = timestamp;
            PeakMagnitude = peakMagnitude;
            RiseDuration = riseDuration;
            MaxDerivative = maxDerivative;
        }
    }

    // Streaming state machine. Feed samples one at a time via ProcessSample.
    // Thresholds mirror ictus_detector.py exactly; do not retune here -- retuning
    // happens in Phase 4/5 against real Unity captures, not during the port.
    public sealed class IctusDetector
    {
        private enum State { Armed, Rising, PostDropSeekMin, Refractory }

        public const int DefaultSmoothingWindow = 4;
        public const double DefaultRiseRateThreshold = 7500.0;
        public const double DefaultPeakMinThreshold = 200.0;
        public const double DefaultDropFraction = 0.6;
        public const double DefaultDropWindowS = 0.08;
        public const double DefaultRefractoryS = 0.175;
        public const double DefaultRisingTimeoutS = 0.5;
        public const double DefaultMinSeekTimeoutS = 0.15;

        private readonly int _smoothingWindow;
        private readonly double _riseRateThreshold;
        private readonly double _peakMinThreshold;
        private readonly double _dropFraction;
        private readonly double _dropWindowS;
        private readonly double _refractoryS;
        private readonly double _risingTimeoutS;
        private readonly double _minSeekTimeoutS;

        private readonly Queue<double> _smoothBuf = new Queue<double>();
        private double? _prevSmoothed;
        private double? _prevT;
        private State _state = State.Armed;

        private double _riseStartT;
        private double _peakMag;
        private double _peakT;
        private double _maxDerivative;
        private double _seekingMinVal;
        private double _seekingMinT;
        private double _seekEnteredT;
        private double _refractoryUntil;

        public IctusDetector(
            int smoothingWindow = DefaultSmoothingWindow,
            double riseRateThreshold = DefaultRiseRateThreshold,
            double peakMinThreshold = DefaultPeakMinThreshold,
            double dropFraction = DefaultDropFraction,
            double dropWindowS = DefaultDropWindowS,
            double refractoryS = DefaultRefractoryS,
            double risingTimeoutS = DefaultRisingTimeoutS,
            double minSeekTimeoutS = DefaultMinSeekTimeoutS)
        {
            _smoothingWindow = smoothingWindow;
            _riseRateThreshold = riseRateThreshold;
            _peakMinThreshold = peakMinThreshold;
            _dropFraction = dropFraction;
            _dropWindowS = dropWindowS;
            _refractoryS = refractoryS;
            _risingTimeoutS = risingTimeoutS;
            _minSeekTimeoutS = minSeekTimeoutS;
        }

        private double Smooth(double rawMag)
        {
            _smoothBuf.Enqueue(rawMag);
            if (_smoothBuf.Count > _smoothingWindow)
            {
                _smoothBuf.Dequeue();
            }
            double sum = 0.0;
            foreach (double v in _smoothBuf)
            {
                sum += v;
            }
            return sum / _smoothBuf.Count;
        }

        public IctusEvent ProcessSample(double t, double rawMag)
        {
            double mag = Smooth(rawMag);
            IctusEvent evt = null;

            if (_state == State.Refractory)
            {
                if (t >= _refractoryUntil)
                {
                    _state = State.Armed;
                }
                _prevSmoothed = mag;
                _prevT = t;
                return null;
            }

            double? dt = _prevT.HasValue ? t - _prevT.Value : (double?)null;
            double? rate = null;
            if (_prevSmoothed.HasValue && dt.HasValue && dt.Value > 0)
            {
                rate = (mag - _prevSmoothed.Value) / dt.Value;
            }

            if (_state == State.Armed)
            {
                if (rate.HasValue && rate.Value > _riseRateThreshold)
                {
                    _state = State.Rising;
                    _riseStartT = t;
                    _peakMag = mag;
                    _peakT = t;
                    _maxDerivative = rate.Value;
                }
            }
            else if (_state == State.Rising)
            {
                if (t - _riseStartT > _risingTimeoutS)
                {
                    _state = State.Armed;
                }
                else
                {
                    if (rate.HasValue && rate.Value > _maxDerivative)
                    {
                        _maxDerivative = rate.Value;
                    }
                    if (mag > _peakMag)
                    {
                        _peakMag = mag;
                        _peakT = t;
                    }
                    else if (_peakMag >= _peakMinThreshold && mag <= _peakMag * _dropFraction)
                    {
                        if (t - _peakT <= _dropWindowS)
                        {
                            _state = State.PostDropSeekMin;
                            _seekingMinVal = mag;
                            _seekingMinT = t;
                            _seekEnteredT = t;
                        }
                        else
                        {
                            _state = State.Armed;
                        }
                    }
                }
            }
            else if (_state == State.PostDropSeekMin)
            {
                bool timedOut = (t - _seekEnteredT) > _minSeekTimeoutS;
                if (mag <= _seekingMinVal && !timedOut)
                {
                    _seekingMinVal = mag;
                    _seekingMinT = t;
                }
                else
                {
                    evt = new IctusEvent(
                        timestamp: _seekingMinT,
                        peakMagnitude: _peakMag,
                        riseDuration: _peakT - _riseStartT,
                        maxDerivative: _maxDerivative);
                    _refractoryUntil = t + _refractoryS;
                    _state = State.Refractory;
                }
            }

            _prevSmoothed = mag;
            _prevT = t;
            return evt;
        }

        public static List<IctusEvent> DetectIctuses(IEnumerable<(double t, double mag)> samples, IctusDetector detector = null)
        {
            IctusDetector d = detector != null ? detector : new IctusDetector();
            var events = new List<IctusEvent>();
            foreach (var sample in samples)
            {
                var ev = d.ProcessSample(sample.t, sample.mag);
                if (ev != null)
                {
                    events.Add(ev);
                }
            }
            return events;
        }
    }
}
