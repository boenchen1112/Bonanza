namespace Swinger.Logic
{
    public sealed class GameConfig
    {
        public BeatScheduleConfig ScheduleConfig;
        public int MissesToOut = 3;
        public int MaxMeasures = 500; // generous upper bound; game ends on outs first

        public GameConfig(BeatScheduleConfig scheduleConfig = null, int missesToOut = 3, int maxMeasures = 500)
        {
            ScheduleConfig = scheduleConfig ?? new BeatScheduleConfig();
            MissesToOut = missesToOut;
            MaxMeasures = maxMeasures;
        }
    }

    // Per-measure WINDUP -> EXPECTING_ICTUS -> JUDGE state machine. Pure
    // logic, no UnityEngine dependency, so it is unit-testable with synthetic
    // IctusEvent streams -- driven for real by a MonoBehaviour's Update loop
    // in the presentation layer (Build Plan v3 Phase 3), not from here.
    public sealed class MeasureJudge
    {
        // Grace period Tick() waits past a measure's window_end before
        // ruling a Miss -- only reached when no ictus locked in-window at
        // all; a locked event is judged immediately (G1,
        // Bug_Audit_2026-07-28.md / Swinger_Build_Plan_v4.md Phase A: this
        // was the exact Python G1 bug, forked into Unity unfixed). Derived
        // from the detector's own worst-case delivery lag rather than a
        // flat constant (G2), and clamped to HalfBeatWindowS in the
        // constructor below so it can never silently break judging at high
        // BPM. Device rate measured at ~66Hz in real captures (F3,
        // reviews/Bug_Audit_2026-07-17.md).
        public const double MeasuredDeviceRateHz = 66.0;
        private const double SmoothingLagS = (IctusDetector.DefaultSmoothingWindow - 1) / 2.0 / MeasuredDeviceRateHz;
        private const double OneSampleMarginS = 1.0 / MeasuredDeviceRateHz;
        public const double DefaultJudgeGraceS = IctusDetector.DefaultMinSeekTimeoutS + SmoothingLagS + OneSampleMarginS;

        public BeatSchedule Schedule { get; }
        public double CalibrationOffsetS { get; }
        public SharpnessReference SharpnessRef { get; }
        public double HalfBeatWindowS { get; }
        public double JudgeGraceS { get; }

        public int MeasureIndex { get; private set; }
        public int Misses { get; private set; }
        public System.Collections.Generic.List<SwingRecord> FinishedMeasures { get; } = new System.Collections.Generic.List<SwingRecord>();
        public System.Collections.Generic.List<WindupSample> WindupSamples { get; } = new System.Collections.Generic.List<WindupSample>();

        private IctusEvent _lockedEvent;

        public MeasureJudge(BeatSchedule schedule, double calibrationOffsetS, SharpnessReference sharpnessRef, double judgeGraceS = DefaultJudgeGraceS)
        {
            Schedule = schedule;
            CalibrationOffsetS = calibrationOffsetS;
            SharpnessRef = sharpnessRef;
            HalfBeatWindowS = schedule.BeatInterval / 2.0;
            // Clamp (G2): an unclamped grace larger than the inter-window
            // gap lets it extend past the *next* measure's window opening,
            // and SubmitIctus() always tests against the current
            // MeasureIndex's window -- genuinely in-window swings for the
            // next measure would get filed as wind-up samples and lost.
            if (judgeGraceS > HalfBeatWindowS)
            {
                // System.Diagnostics, not UnityEngine.Debug -- this assembly
                // stays engine-free by design (see class comment above).
                System.Diagnostics.Debug.WriteLine(
                    $"[MeasureJudge] judgeGraceS ({judgeGraceS * 1000:F0}ms) exceeds HalfBeatWindowS " +
                    $"({HalfBeatWindowS * 1000:F0}ms) at this tempo; clamping. Grace can no longer fully " +
                    "cover the detector's worst-case delivery lag.");
            }
            JudgeGraceS = System.Math.Min(judgeGraceS, HalfBeatWindowS);
        }

        // Centered on the calibration-corrected beat-1 time, not raw beat1,
        // so the window stays symmetric around where the player is actually
        // expected to land once the offset is applied.
        private (double lo, double hi) WindowBounds(int measureIndex)
        {
            double center = Schedule.MeasureBeat1Time(measureIndex) + CalibrationOffsetS;
            return (center - HalfBeatWindowS, center + HalfBeatWindowS);
        }

        public void SubmitIctus(IctusEvent evt)
        {
            var (lo, hi) = WindowBounds(MeasureIndex);
            if (evt.Timestamp >= lo && evt.Timestamp <= hi)
            {
                if (_lockedEvent == null)
                {
                    _lockedEvent = evt;
                }
            }
            else
            {
                WindupSamples.Add(new WindupSample { MeasureIndex = MeasureIndex, IctusTime = evt.Timestamp });
            }
        }

        // Call periodically with the current time. Judges immediately once
        // an event has locked in-window (G1): SubmitIctus() locks only the
        // *first* in-window event and ignores everything after, so the
        // outcome is already fully determined the moment _lockedEvent is
        // set -- waiting for windowEnd + grace after a lock only delays the
        // result, it can't change it. That wait is still needed for the
        // no-event case: only there does the grace period matter, to give
        // the detector pipeline time to actually deliver an in-window event
        // before ruling a Miss.
        public SwingRecord Tick(double now)
        {
            if (_lockedEvent != null)
            {
                var lockedRecord = Judge();
                MeasureIndex++;
                _lockedEvent = null;
                return lockedRecord;
            }

            var (_, windowEnd) = WindowBounds(MeasureIndex);
            if (now < windowEnd + JudgeGraceS)
            {
                return null;
            }
            var record = Judge();
            MeasureIndex++;
            _lockedEvent = null;
            return record;
        }

        private SwingRecord Judge()
        {
            double beat1 = Schedule.MeasureBeat1Time(MeasureIndex);
            var evt = _lockedEvent;

            if (evt == null)
            {
                var missRecord = new SwingRecord
                {
                    MeasureIndex = MeasureIndex,
                    ExpectedTime = beat1,
                    IctusTime = null,
                    RawOffsetMs = null,
                    CorrectedOffsetMs = null,
                    TimingTier = "Miss",
                    PeakMagnitude = null,
                    RiseDuration = null,
                    SharpnessRaw = null,
                    SharpnessBucket = null,
                    HitTier = "Miss",
                };
                Misses++;
                FinishedMeasures.Add(missRecord);
                return missRecord;
            }

            double rawOffsetS = evt.Timestamp - beat1;
            double correctedOffsetS = rawOffsetS - CalibrationOffsetS;
            string tier = Scoring.TimingTier(correctedOffsetS * 1000);

            double? maxDeriv = evt.MaxDerivative != 0.0 ? evt.MaxDerivative : (double?)null;
            double sharpRaw = Scoring.RawSharpness(evt.PeakMagnitude, evt.RiseDuration, maxDeriv);
            string bucket = null;
            string hit = "Miss";
            if (tier != "Miss")
            {
                bucket = SharpnessRef.Bucket(sharpRaw);
                SharpnessRef.Add(sharpRaw);
                hit = Scoring.HitTier(tier, bucket);
            }
            else
            {
                Misses++;
            }

            var record = new SwingRecord
            {
                MeasureIndex = MeasureIndex,
                ExpectedTime = beat1,
                IctusTime = evt.Timestamp,
                RawOffsetMs = rawOffsetS * 1000,
                CorrectedOffsetMs = correctedOffsetS * 1000,
                TimingTier = tier,
                PeakMagnitude = evt.PeakMagnitude,
                RiseDuration = evt.RiseDuration,
                SharpnessRaw = sharpRaw,
                SharpnessBucket = bucket,
                HitTier = hit,
            };
            FinishedMeasures.Add(record);
            return record;
        }

        public bool IsOut(int missesToOut) => Misses >= missesToOut;
    }
}
