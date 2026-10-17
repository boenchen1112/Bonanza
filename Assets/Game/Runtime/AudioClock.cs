// The one clock. Every time value in the game is AUDIO time: seconds in the
// AudioSettings.dspTime domain. Frame time is never used for judgement.
//
// dspTime advances in audio-buffer steps, so it is read through a smoothed
// bridge from the input system's clock (InputState.currentTime), which is
// also the domain input events are stamped in. That gives a continuous "now"
// AND lets a press be converted to audio time exactly, instead of being
// rounded to the frame it was noticed on. (web/src/core/clock.js did the same
// with performance.now() and AudioContext.currentTime.)
//
// WebGL: the browser keeps audio suspended until the first click or tap, and
// dspTime stands still until then. Nothing may be scheduled against the clock
// before `Running` is true — the game gates its start on a press.

using Bonanza.Core;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace Bonanza
{
    public sealed class AudioClock : MonoBehaviour
    {
        const double Smoothing = 0.05;   // EMA weight for the input->audio offset
        const double Snap = 0.1;         // jump straight to a fresh offset past this

        double _offset;                  // audioTime ≈ inputTime + _offset
        bool _hasOffset;
        double _lastDsp = -1;
        double _dspStalledSince;
        int _moving;                     // consecutive frames dspTime advanced

        /// dspTime is advancing: the browser has unlocked audio.
        public bool Running => _moving >= 3;

        readonly LatencySlew _latency = new LatencySlew();

        /// The output latency currently subtracted from Now and ToAudioTime, seconds (web clock.js _outputLatency).
        public double Latency => _latency.Value;

        /// The estimate the applied latency is moving toward, seconds.
        public double LatencyTarget => _latency.Target;

        /// Take a fresh output-latency estimate (call about once a second). While `running` (a round is
        /// being played) it is slewed in: Now subtracts it, so a jump would jump the beat on screen.
        public double RefreshLatency(double target, bool running) => _latency.Refresh(target, running);

        /// Audio time on the dsp axis, continuous between audio-buffer steps, NOT latency-compensated:
        /// where the mixer is now. Scheduling (PlayScheduled) and file positions live on this axis.
        public double RawNow => _hasOffset ? InputState.currentTime + _offset : AudioSettings.dspTime;

        /// Current audio time, continuous between audio-buffer steps and latency-compensated: the moment
        /// the player HEARS right now (web clock.now()).
        public double Now => RawNow - _latency.Value;

        /// Raw dspTime — for AudioSource.PlayScheduled.
        public double DspNow => AudioSettings.dspTime;

        /// Convert an input-system timestamp (e.g. InputAction.CallbackContext.time)
        /// to audio time, latency-compensated like Now (web clock.toAudioTime).
        public double ToAudioTime(double inputTime) => (_hasOffset ? inputTime + _offset : AudioSettings.dspTime) - _latency.Value;

        /// Re-anchor the input->audio offset to dspTime right now. WebGL stalls dspTime while the listener is
        /// paused but the input clock runs on, so after a pause the old offset is off by the pause's length and
        /// the next Update would snap it: everything read through Now in between (and the pause length computed
        /// from it) would be wrong. Call it as the pause is lifted.
        public void Rebase()
        {
            if (_hasOffset) _offset = AudioSettings.dspTime - InputState.currentTime;
        }

        void Update()
        {
            double dsp = AudioSettings.dspTime;
            double inputNow = InputState.currentTime;
            if (dsp != _lastDsp)
            {
                // The freshest sample is the moment dspTime changes.
                double observed = dsp - inputNow;
                if (!_hasOffset || System.Math.Abs(observed - _offset) > Snap)
                {
                    _offset = observed;
                    _hasOffset = true;
                }
                else
                {
                    _offset += (observed - _offset) * Smoothing;
                }
                _lastDsp = dsp;
                _dspStalledSince = inputNow;
                if (_moving < 1000) _moving++;
            }
            else if (inputNow - _dspStalledSince > 0.25)
            {
                _moving = 0;   // suspended (tab hidden, audio locked)
            }
        }
    }
}
