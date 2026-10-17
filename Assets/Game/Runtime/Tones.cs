// Placeholder sounds, synthesised once into AudioClips at load. WebGL has no
// OnAudioFilterRead, so nothing is generated live; clips are scheduled with
// PlayScheduled on the audio clock. The web's SFX kit (verdicts, impact, count-in,
// menu sounds, stings) is baked offline and played by Sfx; what is left here are the
// games' own voices (a platform's pluck, a drum, a singer) that have no baked version.

using System.Collections.Generic;
using UnityEngine;

namespace Bonanza
{
    public enum Wave { Sine, Square, Triangle, Noise }

    public static class Tones
    {
        static readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();

        static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;

        /// A decaying blip. `sweep` multiplies the pitch across the clip (1 = steady).
        public static AudioClip Blip(float hz, float seconds = 0.04f, float decay = 60, Wave wave = Wave.Sine, float sweep = 1, float gain = 1)
        {
            string key = $"{hz}:{seconds}:{decay}:{wave}:{sweep}:{gain}";
            if (_cache.TryGetValue(key, out var c)) return c;
            int rate = Rate;
            int n = Mathf.Max(1, (int)(rate * seconds));
            var data = new float[n];
            var rng = new Core.Rng(0x5eed);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float f = hz * Mathf.Pow(sweep, t / seconds);
                phase += f / rate;
                float p = (float)(phase - System.Math.Floor(phase));
                float s = wave switch
                {
                    Wave.Square => p < 0.5f ? 0.6f : -0.6f,
                    Wave.Triangle => 4 * Mathf.Abs(p - 0.5f) - 1,
                    Wave.Noise => (float)rng.Next() * 2 - 1,
                    _ => Mathf.Sin(2 * Mathf.PI * p),
                };
                float attack = Mathf.Clamp01(t / 0.003f);
                data[i] = s * attack * Mathf.Exp(-t * decay) * gain;
            }
            c = AudioClip.Create(key, n, 1, rate, false);
            c.SetData(data, 0);
            _cache[key] = c;
            return c;
        }

        /// Equal-tempered frequency of a MIDI note.
        public static float Midi(float note) => 440f * Mathf.Pow(2, (note - 69) / 12f);
    }
}
