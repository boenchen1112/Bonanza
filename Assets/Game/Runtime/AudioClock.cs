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

using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace RhythmGame
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

        /// Current audio time, continuous between audio-buffer steps.
        public double Now => _hasOffset ? InputState.currentTime + _offset : AudioSettings.dspTime;

        /// Raw dspTime — for AudioSource.PlayScheduled.
        public double DspNow => AudioSettings.dspTime;

        /// Convert an input-system timestamp (e.g. InputAction.CallbackContext.time)
        /// to audio time.
        public double ToAudioTime(double inputTime) => _hasOffset ? inputTime + _offset : AudioSettings.dspTime;

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
