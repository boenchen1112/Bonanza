using NUnit.Framework;
using Swinger.Logic;

namespace Swinger.Logic.Tests
{
    // Port of tests/test_measure_judge_smoke.py and tests/test_full_loop_probe.py.
    // Deferred from the Phase 2 task since both exercise MeasureJudge, which
    // Build Plan v3 assigns to Phase 3.
    public class MeasureJudgeTests
    {
        [Test]
        public void SyntheticMeasures_ProduceCorrectTiers()
        {
            var config = new BeatScheduleConfig(80.0);
            var schedule = new BeatSchedule(config, 0.0, numMeasures: 4);
            var judge = new MeasureJudge(schedule, calibrationOffsetS: 0.0, sharpnessRef: new SharpnessReference());
            // tick() waits window_end + JudgeGraceS before judging -- ticks in
            // this test need to clear that grace period too, not just window_end.
            double pastWindow = schedule.BeatInterval / 2.0 + MeasureJudge.DefaultJudgeGraceS + 0.001;

            // Measure 0: dead-on-time, sharp swing -> expect Perfect
            double beat1M0 = schedule.MeasureBeat1Time(0);
            judge.SubmitIctus(new IctusEvent(beat1M0 + 0.01, 800.0, 0.05));
            var r0 = judge.Tick(beat1M0 + pastWindow);
            Assert.IsNotNull(r0);
            Assert.AreEqual("Perfect", r0.TimingTier);

            // Measure 1: no swing at all -> Miss
            SwingRecord r1 = null;
            while (r1 == null)
            {
                double beat1M1 = schedule.MeasureBeat1Time(1);
                r1 = judge.Tick(beat1M1 + pastWindow);
            }
            Assert.AreEqual("Miss", r1.TimingTier);
            Assert.AreEqual("Miss", r1.HitTier);

            // Measure 2: late but inside window (300ms late, half-beat window
            // is 375ms) -> Miss (timing) but not out-of-window.
            double beat1M2 = schedule.MeasureBeat1Time(2);
            judge.SubmitIctus(new IctusEvent(beat1M2 + 0.3, 800.0, 0.05));
            var r2 = judge.Tick(beat1M2 + pastWindow);
            Assert.AreEqual("Miss", r2.TimingTier);
            Assert.IsNotNull(r2.IctusTime);

            Assert.AreEqual(2, judge.Misses);
        }

        [Test]
        public void FullLoopProbe_500Measures_NoIndexErrorAndRealWindupVariance()
        {
            var config = new GameConfig();
            var schedule = new BeatSchedule(config.ScheduleConfig, startTime: 0.0, numMeasures: config.MaxMeasures);
            var judge = new MeasureJudge(schedule, calibrationOffsetS: 0.0, sharpnessRef: new SharpnessReference());
            var sessionLog = new SessionLog();

            int measureIndex = 0;
            while (measureIndex < config.MaxMeasures)
            {
                double beat1 = schedule.MeasureBeat1Time(measureIndex);
                // On-time, sharp swing every measure -> never an out.
                judge.SubmitIctus(new IctusEvent(beat1 + 0.005, 25000.0, 0.05));
                // Beat-2 rebound wind-up ictus, well outside the scoring
                // window -- this is what the old blocking-wait bug used to
                // systematically lose (F4).
                double beat2 = schedule.MeasureBeat1Time(measureIndex) + schedule.BeatInterval;
                judge.SubmitIctus(new IctusEvent(beat2, 5000.0, 0.03));

                var record = judge.Tick(beat1 + schedule.BeatInterval * 2); // past both beats of the measure
                Assert.IsNotNull(record);
                sessionLog.Swings.Add(record);
                foreach (var w in judge.WindupSamples)
                {
                    sessionLog.WindupSamples.Add(w);
                }
                judge.WindupSamples.Clear();

                measureIndex = judge.MeasureIndex;
            }

            Assert.AreEqual(config.MaxMeasures, measureIndex,
                "A4 guard: caller stops at max_measures without IndexError");
            Assert.AreEqual(0, judge.Misses);

            var variance = sessionLog.WindupIntervalVariance();
            Assert.IsNotNull(variance, "F4 regression: wind-up samples not reaching session_log");
        }
    }
}
