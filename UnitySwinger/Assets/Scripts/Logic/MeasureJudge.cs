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
        // Grace period tick() waits past a measure's window_end before
        // judging. ictus_detector's POST_DROP_SEEK_MIN can legitimately take
        // up to MIN_SEEK_TIMEOUT_S (150ms) plus smoothing lag (~20ms at
        // typical rates) after the true local min before it finalizes and
        // returns the IctusEvent -- without this margin, a genuinely
        // in-window swing near the later half of the window can get judged
        // (and misfiled as the *next* measure's windup sample once it
        // finally arrives) before its own event is even delivered. Found via
        // real-hardware playtest (Python, Phase 0).
        public const double DefaultJudgeGraceS = 0.25;

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
            JudgeGraceS = judgeGraceS;
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

        // Call periodically with the current time. Returns a SwingRecord once
        // the current measure's window has closed AND JudgeGraceS has passed
        // -- the grace period gives the detector pipeline time to actually
        // deliver an in-window event before judging locks it out.
        public SwingRecord Tick(double now)
        {
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
