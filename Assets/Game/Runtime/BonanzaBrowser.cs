// Browser bridge (Plugins/BonanzaBrowser.jslib): WebAudio context state and output latency, and the OS
// prefers-reduced-motion flag. Outside a WebGL player (Editor, tests) every call answers "nothing
// special": audio running, latency not exposed, motion not reduced.

using System.Runtime.InteropServices;

namespace Bonanza
{
    public enum BrowserAudioState { Unknown = 0, Running = 1, Suspended = 2, Closed = 3 }

    public static class BonanzaBrowser
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void Bonanza_BrowserInstall();
        [DllImport("__Internal")] static extern int Bonanza_AudioState();
        [DllImport("__Internal")] static extern int Bonanza_AudioSource();
        [DllImport("__Internal")] static extern double Bonanza_OutputLatency();
        [DllImport("__Internal")] static extern double Bonanza_BaseLatency();
        [DllImport("__Internal")] static extern void Bonanza_AudioResume();
        [DllImport("__Internal")] static extern int Bonanza_PrefersReducedMotion();
        [DllImport("__Internal")] static extern int Bonanza_ReducedMotionVersion();

        public static bool Available => true;
        public static void Install() => Bonanza_BrowserInstall();
        public static BrowserAudioState AudioState() => (BrowserAudioState)Bonanza_AudioState();
        /// 0 not found, 1 Unity's own context object, 2 the template's constructor hook.
        public static int AudioSource() => Bonanza_AudioSource();
        /// Seconds; negative when the browser does not expose it.
        public static double OutputLatency() => Bonanza_OutputLatency();
        public static double BaseLatency() => Bonanza_BaseLatency();
        public static void ResumeAudio() => Bonanza_AudioResume();
        static bool ReadReducedMotion() => Bonanza_PrefersReducedMotion() != 0;
        public static int ReducedMotionVersion() => Bonanza_ReducedMotionVersion();
#else
        public static bool Available => false;
        public static void Install() { }
        public static BrowserAudioState AudioState() => BrowserAudioState.Running;
        public static int AudioSource() => 0;
        public static double OutputLatency() => -1;
        public static double BaseLatency() => -1;
        public static void ResumeAudio() { }
        static bool ReadReducedMotion() => false;
        public static int ReducedMotionVersion() => 0;
#endif

        /// The OS "reduce motion" flag, cached: Settings.ReduceMotion is read many times a frame, a call
        /// across to JS each time would not be free. GameHost.Update calls Poll() once a frame.
        public static bool ReducedMotion { get; private set; }

        static int _reduceVer = -1;

        /// Refresh the cached flag; true when it changed (the caller then raises Settings.Changed).
        public static bool Poll()
        {
            int v = ReducedMotionVersion();
            if (v == _reduceVer && _reduceVer >= 0) return false;
            bool was = ReducedMotion;
            ReducedMotion = ReadReducedMotion();
            _reduceVer = v;
            return ReducedMotion != was;
        }
    }
}
