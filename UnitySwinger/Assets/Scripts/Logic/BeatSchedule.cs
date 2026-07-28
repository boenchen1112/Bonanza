using System.Collections.Generic;

namespace Swinger.Logic
{
    /// <summary>
    /// Expected-beat-time lookup, kept separate from audio playback so a later
    /// version can invert the relationship (conductor leads, audio follows)
    /// without touching the detector or scorer.
    /// </summary>
    public sealed class BeatScheduleConfig
    {
        public double Bpm { get; }
        // Defaults to 2/4 (v1 scope), but BeatSchedule below has no
        // 2/4-specific logic -- IsDownbeat()/MeasureBeat1Time() are
        // already generic on this value, so 3 (3/4) or 4 (4/4) work
        // without further changes (Build Plan v4 Phase C).
        public int BeatsPerMeasure { get; }

        public BeatScheduleConfig(double bpm = 80.0, int beatsPerMeasure = 2)
        {
            Bpm = bpm;
            BeatsPerMeasure = beatsPerMeasure;
        }
    }

    /// <summary>
    /// Precomputed list of expected beat timestamps for a session, in the same
    /// canonical CPU-clock domain the input reader thread stamps samples with.
    /// </summary>
    public sealed class BeatSchedule
    {
        public BeatScheduleConfig Config { get; }
        public double StartTime { get; }
        public double BeatInterval { get; }

        private readonly List<double> _timestamps;

        public BeatSchedule(BeatScheduleConfig config, double startTime, int numMeasures)
        {
            Config = config;
            StartTime = startTime;
            BeatInterval = 60.0 / config.Bpm;
            _timestamps = Build(numMeasures);
        }

        private List<double> Build(int numMeasures)
        {
            int totalBeats = numMeasures * Config.BeatsPerMeasure;
            var result = new List<double>(totalBeats);
            for (int i = 0; i < totalBeats; i++)
            {
                result.Add(StartTime + i * BeatInterval);
            }
            return result;
        }

        /// <summary>Expected canonical-clock timestamp for beatIndex (0-based, global).</summary>
        public double BeatTime(int beatIndex) => _timestamps[beatIndex];

        /// <summary>Expected timestamp of beat 1 (the scored downbeat) for a given measure.</summary>
        public double MeasureBeat1Time(int measureIndex) => BeatTime(measureIndex * Config.BeatsPerMeasure);

        public bool IsDownbeat(int beatIndex) => beatIndex % Config.BeatsPerMeasure == 0;

        public int Count => _timestamps.Count;

        public IReadOnlyList<double> AllTimestamps() => _timestamps;
    }
}
