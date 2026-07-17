using System;
using UnityEngine;

namespace Swinger.Presentation
{
    // Shared procedural click-sound generator (decaying sine), matching
    // metronome.py's _make_click_sound -- no external audio asset needed.
    public static class ClickSoundFactory
    {
        public static AudioClip MakeClickClip(double freqHz, double durationS = 0.05, float volume = 0.5f)
        {
            const int sampleRate = 44100;
            int nSamples = (int)(sampleRate * durationS);
            var data = new float[nSamples * 2]; // stereo interleaved
            for (int i = 0; i < nSamples; i++)
            {
                double t = i / (double)sampleRate;
                double envelope = Math.Exp(-t / (durationS / 4.0));
                float sample = (float)(volume * envelope * Math.Sin(2 * Math.PI * freqHz * t));
                data[i * 2] = sample;
                data[i * 2 + 1] = sample;
            }
            var clip = AudioClip.Create($"click_{freqHz}", nSamples, 2, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
