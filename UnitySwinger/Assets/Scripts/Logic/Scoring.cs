using System;
using System.Collections.Generic;
using System.Linq;

namespace Swinger.Logic
{
    // Timing/sharpness tiers and the timing x sharpness -> hit-tier lookup
    // table. Kept separate from the batting-loop state machine and from
    // calibration offset logic so this same skeleton can be reused by other
    // minigames in the larger set -- those swap the tier-table labels, not
    // this logic.
    public static class Scoring
    {
        // Timing tier windows (ms). Boundaries live inside the judging window,
        // which is strictly wider -- keep the two constants explicitly separate
        // so tuning one never silently changes the other.
        public const double PerfectMs = 50.0;
        public const double GreatMs = 100.0;
        public const double GoodMs = 150.0;

        // correctedOffsetMs is null when no ictus was detected in the window.
        public static string TimingTier(double? correctedOffsetMs)
        {
            if (!correctedOffsetMs.HasValue)
            {
                return "Miss";
            }
            double absMs = Math.Abs(correctedOffsetMs.Value);
            if (absMs <= PerfectMs) return "Perfect";
            if (absMs <= GreatMs) return "Great";
            if (absMs <= GoodMs) return "Good";
            return "Miss";
        }

        // Fallback sharpness range used only before the player has ever
        // calibrated (fresh install on the shipped default offset). Derived
        // from real hardware captures -- see scoring.py for the full
        // provenance note. Do not retune during the port.
        public static readonly (double Low, double High) FallbackSharpnessLowHigh = (60000.0, 420000.0);

        public static double RawSharpness(double peakMagnitude, double riseDuration, double? maxDerivative = null)
        {
            if (maxDerivative.HasValue)
            {
                return maxDerivative.Value;
            }
            if (riseDuration <= 0)
            {
                return 0.0;
            }
            return peakMagnitude / riseDuration;
        }

        // Timing tier (rows) x sharpness bucket (columns) -> hit tier.
        private static readonly Dictionary<(string Timing, string Sharpness), string> HitTable =
            new Dictionary<(string, string), string>
            {
                { ("Perfect", "Low"), "Bunt" },
                { ("Perfect", "Mid"), "Line Drive" },
                { ("Perfect", "High"), "Home Run" },
                { ("Great", "Low"), "Bunt" },
                { ("Great", "Mid"), "Line Drive" },
                { ("Great", "High"), "Home Run" },
                { ("Good", "Low"), "Bunt" },
                { ("Good", "Mid"), "Bunt" },
                { ("Good", "High"), "Line Drive" },
            };

        // Sharpness never causes a Miss or contributes to outs -- it only
        // affects hit quality. A Miss on timing is a Miss regardless of
        // sharpness.
        public static string HitTier(string timing, string sharpnessBucket)
        {
            if (timing == "Miss")
            {
                return "Miss";
            }
            if (sharpnessBucket != null && HitTable.TryGetValue((timing, sharpnessBucket), out string tier))
            {
                return tier;
            }
            return "Bunt";
        }
    }

    // Per-player reference distribution for percentile bucketing. Seeded from
    // calibration swings, then rolled forward with live gameplay swings so it
    // does not go stale or rest on only a handful of points.
    public sealed class SharpnessReference
    {
        public List<double> Values { get; }
        public int MaxSize { get; }

        public SharpnessReference(List<double> values = null, int maxSize = 200)
        {
            Values = values ?? new List<double>();
            MaxSize = maxSize;
        }

        public void Add(double value)
        {
            Values.Add(value);
            if (Values.Count > MaxSize)
            {
                Values.RemoveAt(0);
            }
        }

        public string Bucket(double value)
        {
            if (Values.Count < 3)
            {
                double low = Scoring.FallbackSharpnessLowHigh.Low;
                double high = Scoring.FallbackSharpnessLowHigh.High;
                double span = high - low;
                if (value <= low + span / 3) return "Low";
                if (value <= low + 2 * span / 3) return "Mid";
                return "High";
            }
            var sorted = Values.OrderBy(v => v).ToList();
            int n = sorted.Count;
            double rank = sorted.Count(v => v <= value) / (double)n;
            if (rank <= 1.0 / 3.0) return "Low";
            if (rank <= 2.0 / 3.0) return "Mid";
            return "High";
        }
    }
}
