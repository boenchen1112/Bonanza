using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Swinger.Logic
{
    // Per-swing session data: a decoupled-from-rendering holder for the
    // post-round summary (raw offsets, sharpness, wind-up consistency) that
    // also doubles as the session data for the mid-term feedback-loop goal.
    public sealed class SwingRecord
    {
        public int MeasureIndex;
        public double ExpectedTime;
        public double? IctusTime; // null if no qualifying ictus was detected
        public double? RawOffsetMs; // IctusTime - ExpectedTime, uncorrected, ms
        public double? CorrectedOffsetMs; // RawOffsetMs - calibration offset ms
        public string TimingTier; // "Perfect" | "Great" | "Good" | "Miss"
        public double? PeakMagnitude;
        public double? RiseDuration;
        public double? SharpnessRaw;
        public string SharpnessBucket; // "Low" | "Mid" | "High" | null
        public string HitTier; // "Bunt" | "Line Drive" | "Home Run" | "Miss"
    }

    public sealed class WindupSample
    {
        public int MeasureIndex;
        public double IctusTime;
    }

    public sealed class SessionLog
    {
        public List<SwingRecord> Swings { get; } = new List<SwingRecord>();
        public List<WindupSample> WindupSamples { get; } = new List<WindupSample>();

        public void RecordSwing(SwingRecord record) => Swings.Add(record);

        public void RecordWindup(WindupSample sample) => WindupSamples.Add(sample);

        public int OutCount() => Swings.Count(s => s.TimingTier == "Miss");

        public List<double> RawOffsetsMs() =>
            Swings.Where(s => s.RawOffsetMs.HasValue).Select(s => s.RawOffsetMs.Value).ToList();

        public List<double> SharpnessValues() =>
            Swings.Where(s => s.SharpnessRaw.HasValue).Select(s => s.SharpnessRaw.Value).ToList();

        // Variance of inter-beat-interval length across logged wind-up
        // ictuses -- purely observational in v1, not scored.
        public double? WindupIntervalVariance()
        {
            var times = WindupSamples.Select(w => w.IctusTime).OrderBy(t => t).ToList();
            if (times.Count < 3)
            {
                return null;
            }
            var intervals = new List<double>();
            for (int i = 0; i < times.Count - 1; i++)
            {
                intervals.Add(times[i + 1] - times[i]);
            }
            double mean = intervals.Average();
            return intervals.Select(x => (x - mean) * (x - mean)).Average();
        }

        private static string Num(double? v) => v.HasValue ? v.Value.ToString("R", CultureInfo.InvariantCulture) : "null";

        private static string Str(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string SwingJson(SwingRecord s)
        {
            return "{"
                + "\"measure_index\":" + s.MeasureIndex.ToString(CultureInfo.InvariantCulture) + ","
                + "\"expected_time\":" + s.ExpectedTime.ToString("R", CultureInfo.InvariantCulture) + ","
                + "\"ictus_time\":" + Num(s.IctusTime) + ","
                + "\"raw_offset_ms\":" + Num(s.RawOffsetMs) + ","
                + "\"corrected_offset_ms\":" + Num(s.CorrectedOffsetMs) + ","
                + "\"timing_tier\":" + Str(s.TimingTier) + ","
                + "\"peak_magnitude\":" + Num(s.PeakMagnitude) + ","
                + "\"rise_duration\":" + Num(s.RiseDuration) + ","
                + "\"sharpness_raw\":" + Num(s.SharpnessRaw) + ","
                + "\"sharpness_bucket\":" + Str(s.SharpnessBucket) + ","
                + "\"hit_tier\":" + Str(s.HitTier)
                + "}";
        }

        private static string WindupJson(WindupSample w)
        {
            return "{"
                + "\"measure_index\":" + w.MeasureIndex.ToString(CultureInfo.InvariantCulture) + ","
                + "\"ictus_time\":" + w.IctusTime.ToString("R", CultureInfo.InvariantCulture)
                + "}";
        }

        public void ExportJson(string path)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"swings\": [\n");
            for (int i = 0; i < Swings.Count; i++)
            {
                sb.Append("    ").Append(SwingJson(Swings[i]));
                sb.Append(i < Swings.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ],\n  \"windup_samples\": [\n");
            for (int i = 0; i < WindupSamples.Count; i++)
            {
                sb.Append("    ").Append(WindupJson(WindupSamples[i]));
                sb.Append(i < WindupSamples.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            File.WriteAllText(path, sb.ToString());
        }

        private static readonly string[] CsvFields =
        {
            "measure_index", "expected_time", "ictus_time", "raw_offset_ms",
            "corrected_offset_ms", "timing_tier", "peak_magnitude", "rise_duration",
            "sharpness_raw", "sharpness_bucket", "hit_tier",
        };

        private static string CsvCell(SwingRecord s, string field)
        {
            switch (field)
            {
                case "measure_index": return s.MeasureIndex.ToString(CultureInfo.InvariantCulture);
                case "expected_time": return s.ExpectedTime.ToString("R", CultureInfo.InvariantCulture);
                case "ictus_time": return s.IctusTime.HasValue ? s.IctusTime.Value.ToString("R", CultureInfo.InvariantCulture) : "";
                case "raw_offset_ms": return s.RawOffsetMs.HasValue ? s.RawOffsetMs.Value.ToString("R", CultureInfo.InvariantCulture) : "";
                case "corrected_offset_ms": return s.CorrectedOffsetMs.HasValue ? s.CorrectedOffsetMs.Value.ToString("R", CultureInfo.InvariantCulture) : "";
                case "timing_tier": return s.TimingTier ?? "";
                case "peak_magnitude": return s.PeakMagnitude.HasValue ? s.PeakMagnitude.Value.ToString("R", CultureInfo.InvariantCulture) : "";
                case "rise_duration": return s.RiseDuration.HasValue ? s.RiseDuration.Value.ToString("R", CultureInfo.InvariantCulture) : "";
                case "sharpness_raw": return s.SharpnessRaw.HasValue ? s.SharpnessRaw.Value.ToString("R", CultureInfo.InvariantCulture) : "";
                case "sharpness_bucket": return s.SharpnessBucket ?? "";
                case "hit_tier": return s.HitTier ?? "";
                default: return "";
            }
        }

        public void ExportCsv(string path)
        {
            if (Swings.Count == 0)
            {
                return;
            }
            var sb = new StringBuilder();
            sb.Append(string.Join(",", CsvFields)).Append("\n");
            foreach (var s in Swings)
            {
                sb.Append(string.Join(",", CsvFields.Select(f => CsvCell(s, f)))).Append("\n");
            }
            File.WriteAllText(path, sb.ToString());
        }
    }
}
