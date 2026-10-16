// Player settings (web shell/state.js DEFAULT_OPTIONS + shell/options.js ranges).
//
// The logic is pure (a store interface, clamping, steps, cycling, migration) so
// EditMode tests pin it; the PlayerPrefs store is the only Unity dependency.
// Keys keep the "rg:" prefix of Save.cs, and "rg:offsetMs" is unchanged, so an
// existing save loads as it was. The single "rg:volume" of the earlier build is
// migrated once into the music and SFX values.
//
// Nothing here touches the chart-timing path; OffsetMs is read exactly as before.

using System;
using UnityEngine;

namespace Bonanza
{
    public interface ISettingsStore
    {
        bool Has(string key);
        float GetFloat(string key, float def);
        void SetFloat(string key, float v);
        string GetString(string key, string def);
        void SetString(string key, string v);
        void Delete(string key);
        void Flush();
    }

    /// PlayerPrefs maps to IndexedDB in a WebGL build and never throws.
    public sealed class PlayerPrefsStore : ISettingsStore
    {
        public bool Has(string key) => PlayerPrefs.HasKey(key);
        public float GetFloat(string key, float def) => PlayerPrefs.GetFloat(key, def);
        public void SetFloat(string key, float v) => PlayerPrefs.SetFloat(key, v);
        public string GetString(string key, string def) => PlayerPrefs.GetString(key, def);
        public void SetString(string key, string v) => PlayerPrefs.SetString(key, v);
        public void Delete(string key) => PlayerPrefs.DeleteKey(key);
        public void Flush() => PlayerPrefs.Save();
    }

    public enum GraphicsSetting { Auto, High, Medium, Low }
    public enum SwingInput { Tap, Mouse, Camera }

    public static class Settings
    {
        // ---- web DEFAULT_OPTIONS and options.js ranges
        public const float DefaultMusic = 0.75f, DefaultSfx = 0.85f;
        public const float PctStep = 0.05f;
        // Wide enough for Bluetooth output (150-300 ms the browser does not report).
        public const float OffsetMin = -300, OffsetMax = 300, OffsetStep = 5;

        const string Prefix = "rg:";
        public const string KeyMusic = Prefix + "music", KeySfx = Prefix + "sfx", KeyOffset = Prefix + "offsetMs",
            KeyReduce = Prefix + "reduceMotion", KeyGraphics = Prefix + "graphics", KeySwing = Prefix + "swingInput",
            KeyLegacyVolume = Prefix + "volume";

        /// Swapped by tests.
        public static ISettingsStore Store = new PlayerPrefsStore();

        /// Camera conducting is not built yet (ticket 16): the value can be chosen and is kept,
        /// but SwingInputEffective falls back to Tap while this is false.
        public static bool CameraInputAvailable = false;

        // ------------------------------------------------------------ pure helpers

        public static float QuantizePct(float v) => Mathf.Clamp01(Mathf.Round(v / PctStep) * PctStep);

        public static float StepPct(float v, int dir) => QuantizePct(v + Math.Sign(dir) * PctStep);

        public static float ClampOffset(float v) => Mathf.Clamp(v, OffsetMin, OffsetMax);

        public static float StepOffset(float v, int dir) => ClampOffset(Mathf.Round(v / OffsetStep) * OffsetStep + Math.Sign(dir) * OffsetStep);

        /// Next value of an enum cycling with wrap, either direction (web choice rows).
        public static T Cycle<T>(T v, int dir) where T : struct, Enum
        {
            var all = (T[])Enum.GetValues(typeof(T));
            int i = Array.IndexOf(all, v);
            return all[((i + Math.Sign(dir)) % all.Length + all.Length) % all.Length];
        }

        /// Old single master volume -> (music, sfx). The old build played the music at a fixed
        /// 0.5 and the SFX at their own level, both under that master; the new music gain is
        /// scaled so these defaults sound as before: SFX keeps the old value, music keeps the
        /// web's default ratio to it (0.75 : 0.85).
        public static void MigrateVolume(float oldVolume, out float music, out float sfx)
        {
            sfx = QuantizePct(oldVolume);
            music = QuantizePct(oldVolume * DefaultMusic / DefaultSfx);
        }

        /// Run once at startup (idempotent): folds a legacy "rg:volume" into music/sfx.
        public static void Migrate()
        {
            var s = Store;
            if (!s.Has(KeyLegacyVolume)) return;
            if (!s.Has(KeyMusic) && !s.Has(KeySfx))
            {
                MigrateVolume(s.GetFloat(KeyLegacyVolume, DefaultSfx), out float m, out float f);
                s.SetFloat(KeyMusic, m);
                s.SetFloat(KeySfx, f);
            }
            s.Delete(KeyLegacyVolume);
            s.Flush();
        }

        static float ReadPct(string key, float def)
        {
            float v = Store.GetFloat(key, def);
            return float.IsNaN(v) ? def : QuantizePct(v);
        }

        static T ReadEnum<T>(string key, T def) where T : struct, Enum
        {
            return Enum.TryParse(Store.GetString(key, ""), true, out T v) && Enum.IsDefined(typeof(T), v) ? v : def;
        }

        // ------------------------------------------------------------ values

        public static event Action Changed;
        static void Touch() { Store.Flush(); Changed?.Invoke(); }

        /// 0..1, music bus level.
        public static float Music
        {
            get => ReadPct(KeyMusic, DefaultMusic);
            set { Store.SetFloat(KeyMusic, QuantizePct(value)); Touch(); }
        }

        /// 0..1, sound-effects level.
        public static float Sfx
        {
            get => ReadPct(KeySfx, DefaultSfx);
            set { Store.SetFloat(KeySfx, QuantizePct(value)); Touch(); }
        }

        /// Timing calibration in ms, ADDED to press times.
        public static float OffsetMs
        {
            get { float v = Store.GetFloat(KeyOffset, 0); return float.IsNaN(v) ? 0 : ClampOffset(v); }
            set { Store.SetFloat(KeyOffset, ClampOffset(value)); Touch(); }
        }

        /// Tone down shake, flashes and camera moves. Read by the look/FX/camera code.
        /// (The web also honours the OS prefers-reduced-motion; the Unity build does not read it.)
        public static bool ReduceMotion
        {
            get => Store.GetFloat(KeyReduce, 0) > 0.5f;
            set { Store.SetFloat(KeyReduce, value ? 1 : 0); Touch(); }
        }

        public static GraphicsSetting Graphics
        {
            get => ReadEnum(KeyGraphics, GraphicsSetting.Auto);
            set { Store.SetString(KeyGraphics, value.ToString().ToLowerInvariant()); Touch(); }
        }

        /// The chosen Swing Kings input, as saved.
        public static SwingInput SwingInputChoice
        {
            get => ReadEnum(KeySwing, SwingInput.Tap);
            set { Store.SetString(KeySwing, value.ToString().ToLowerInvariant()); Touch(); }
        }

        /// The input Swing Kings should use now: the choice, or Tap when it is not available yet.
        public static SwingInput SwingInputEffective
        {
            get { var c = SwingInputChoice; return c == SwingInput.Camera && !CameraInputAvailable ? SwingInput.Tap : c; }
        }

        /// Options' defaults (everything but progress).
        public static void ResetAll()
        {
            foreach (var k in new[] { KeyMusic, KeySfx, KeyOffset, KeyReduce, KeyGraphics, KeySwing, KeyLegacyVolume }) Store.Delete(k);
            Touch();
        }
    }
}
