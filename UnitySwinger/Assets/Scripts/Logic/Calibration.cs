using System.Collections.Generic;
using System.Linq;

namespace Swinger.Logic
{
    // Persistent calibration offset/seed computation (pure logic only).
    // Calibration never runs automatically -- the game only *loads* a saved
    // offset on startup; running calibration itself is a Settings-menu
    // action offered after a round. File I/O (JSON persistence under
    // Application.persistentDataPath) is intentionally NOT here -- this
    // assembly is UnityEngine-free by design (Build Plan v3 Phase 2); the
    // persistence wrapper lives in the presentation layer (Phase 3).
    public sealed class CalibrationResult
    {
        public double OffsetS;
        public List<double> SharpnessSeed;
        public bool FromDefault;

        public CalibrationResult(double offsetS, List<double> sharpnessSeed = null, bool fromDefault = false)
        {
            OffsetS = offsetS;
            SharpnessSeed = sharpnessSeed ?? new List<double>();
            FromDefault = fromDefault;
        }
    }

    public static class Calibration
    {
        // Measured once per platform (Section 4a / Phase 0). This is the
        // Python dev-machine placeholder carried over only as a starting
        // constant -- do NOT import the Python calibration.json's offset_s
        // into the Unity build (Phase 3 note): it measures the pygame +
        // Bluetooth stack's latency, not Unity's. The Unity build must start
        // from its own measured default until the player calibrates.
        public const double DefaultOffsetS = 0.05;

        public const int PracticeMeasures = 10; // 8-12 per spec; also seeds the sharpness reference
        public const double OutlierBoundBeats = 0.5; // discard candidate offsets larger than half a beat

        // Given the closest ictus (or null if missed) per practice measure,
        // compute the mean offset, discarding outliers beyond half a beat.
        // fallbackOffsetS is used when no candidate survives (e.g. the caller's
        // currently-loaded offset, or DefaultOffsetS) -- the pure-logic port
        // takes this as a parameter instead of re-reading a file, since this
        // assembly has no file I/O.
        public static CalibrationResult ComputeOffset(
            IReadOnlyList<double?> practiceIctusTimes,
            IReadOnlyList<double> practiceExpectedTimes,
            double beatIntervalS,
            double fallbackOffsetS = DefaultOffsetS)
        {
            var candidates = new List<double>();
            int n = practiceIctusTimes.Count;
            for (int i = 0; i < n; i++)
            {
                double? ictusT = practiceIctusTimes[i];
                if (!ictusT.HasValue)
                {
                    continue;
                }
                double expectedT = practiceExpectedTimes[i];
                double delta = ictusT.Value - expectedT;
                if (System.Math.Abs(delta) <= beatIntervalS * OutlierBoundBeats)
                {
                    candidates.Add(delta);
                }
            }

            if (candidates.Count == 0)
            {
                return new CalibrationResult(fallbackOffsetS, fromDefault: true);
            }

            double offsetS = candidates.Average();
            return new CalibrationResult(offsetS);
        }
    }
}
