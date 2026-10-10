// Tempo map: beat <-> audio time. The pure half of web/src/core/clock.js.
// Every time value in this game is AUDIO time in seconds; the Unity-side
// clock supplies "now" and this converts it to beats.

namespace Bonanza.Core
{
    public sealed class BeatMap
    {
        public double Bpm { get; private set; }
        double _origin;          // audio time at which BeatAtOrigin occurs
        double _beatAtOrigin;

        public BeatMap(double bpm = 120) { Bpm = bpm; }

        /// Seconds per beat.
        public double Spb => 60.0 / Bpm;

        /// Start the transport so that `startBeat` lands at `audioTime`.
        public void Start(double audioTime, double startBeat = 0)
        {
            _origin = audioTime;
            _beatAtOrigin = startBeat;
        }

        /// Change tempo without breaking the current beat phase.
        public void SetBpm(double bpm, double atAudioTime)
        {
            _beatAtOrigin = BeatAt(atAudioTime);
            _origin = atAudioTime;
            Bpm = bpm;
        }

        public double BeatAt(double audioTime) => _beatAtOrigin + (audioTime - _origin) / Spb;
        public double TimeAt(double beat) => _origin + (beat - _beatAtOrigin) * Spb;

        /// 0..1 position within the current beat. Never `x % 1`: the transport
        /// runs NEGATIVE beats through the lead-in and % keeps the sign.
        public static double Frac(double beat) => beat - System.Math.Floor(beat);
    }
}
