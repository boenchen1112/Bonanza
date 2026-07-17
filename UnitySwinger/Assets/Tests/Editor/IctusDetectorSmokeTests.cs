using System.Collections.Generic;
using NUnit.Framework;
using Swinger.Logic;

namespace Swinger.Logic.Tests
{
    // Port of tests/test_ictus_detector_smoke.py and
    // tests/test_ictus_intensity_invariance.py. Synthetic-trace smoke tests
    // only -- do NOT stand in for golden-trace parity against real captures
    // (see GoldenTraceParityTests) or hardware-gated Phase 4 validation.
    public class IctusDetectorSmokeTests
    {
        private static List<(double t, double mag)> SyntheticSwingTrace(
            int numSwings, double sampleRateHz, double swingGapS,
            double peakMag, double riseS, double fallS, double baseline = 20.0)
        {
            double dt = 1.0 / sampleRateHz;
            var samples = new List<(double, double)>();
            double t = 0.0;
            for (int i = 0; i < numSwings; i++)
            {
                double idleEnd = t + swingGapS;
                while (t < idleEnd)
                {
                    samples.Add((t, baseline));
                    t += dt;
                }
                double riseStart = t;
                double riseEnd = t + riseS;
                while (t < riseEnd)
                {
                    double frac = (t - riseStart) / riseS;
                    samples.Add((t, baseline + frac * (peakMag - baseline)));
                    t += dt;
                }
                double fallStart = t;
                double fallEnd = t + fallS;
                while (t < fallEnd)
                {
                    double frac = (t - fallStart) / fallS;
                    samples.Add((t, peakMag - frac * (peakMag - baseline)));
                    t += dt;
                }
            }
            double paddingEnd = t + 0.3;
            while (t < paddingEnd)
            {
                samples.Add((t, baseline));
                t += dt;
            }
            return samples;
        }

        [Test]
        public void SyntheticTenSwings_ProducesOneEventPerSwing()
        {
            var trace = SyntheticSwingTrace(
                numSwings: 10, sampleRateHz: 150.0, swingGapS: 1.0,
                peakMag: 800.0, riseS: 0.07, fallS: 0.06);

            var events = IctusDetector.DetectIctuses(trace);

            Assert.AreEqual(10, events.Count,
                "expected 10 ictus events, one per synthetic swing, no false positives from baseline noise");
        }

        private static (List<(double t, double mag)> samples, double riseStart) BuildSingleSwing(
            double sampleRateHz, double peakMag, double riseS, double fallS,
            double baseline = 20.0, double preIdleS = 0.3, double postIdleS = 0.3)
        {
            double dt = 1.0 / sampleRateHz;
            var samples = new List<(double, double)>();
            double t = 0.0;
            while (t < preIdleS)
            {
                samples.Add((t, baseline));
                t += dt;
            }
            double riseStart = t;
            while (t < riseStart + riseS)
            {
                double frac = (t - riseStart) / riseS;
                samples.Add((t, baseline + frac * (peakMag - baseline)));
                t += dt;
            }
            double fallStart = t;
            while (t < fallStart + fallS)
            {
                double frac = (t - fallStart) / fallS;
                samples.Add((t, peakMag - frac * (peakMag - baseline)));
                t += dt;
            }
            double postIdleEnd = t + postIdleS;
            while (t < postIdleEnd)
            {
                samples.Add((t, baseline));
                t += dt;
            }
            return (samples, riseStart);
        }

        [Test]
        public void DetectedOffset_IsStableAcrossSyntheticIntensities()
        {
            double sampleRate = 150.0;
            double riseS = 0.03, fallS = 0.06;
            var intensities = new Dictionary<string, double>
            {
                { "soft", 300.0 },
                { "medium", 550.0 },
                { "hard", 800.0 },
            };

            var offsetsFromRiseStart = new Dictionary<string, double>();
            foreach (var kv in intensities)
            {
                var (samples, riseStart) = BuildSingleSwing(sampleRate, kv.Value, riseS, fallS);
                var detector = new IctusDetector();
                var events = new List<IctusEvent>();
                foreach (var (t, mag) in samples)
                {
                    var ev = detector.ProcessSample(t, mag);
                    if (ev != null) events.Add(ev);
                }
                Assert.AreEqual(1, events.Count, $"{kv.Key}: expected 1 event");
                offsetsFromRiseStart[kv.Key] = events[0].Timestamp - riseStart;
            }

            double spreadMs = 0;
            double min = double.MaxValue, max = double.MinValue;
            foreach (var v in offsetsFromRiseStart.Values)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            spreadMs = (max - min) * 1000;

            Assert.Less(spreadMs, 15.0,
                $"detected timestamp shifts {spreadMs:F2}ms with intensity -- exactly the coupling Section 3 warns against");
        }
    }
}
