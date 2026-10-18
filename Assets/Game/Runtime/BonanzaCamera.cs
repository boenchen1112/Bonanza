// Camera conducting bridge (Plugins/BonanzaCamera.jslib + WebGLTemplates/Bonanza/bonanza-camera.js). The browser
// opens the webcam, runs MediaPipe's hand landmarker and queues the palm's position; the game polls the queue.
// Outside a WebGL player (Editor, tests) there is no camera: Supported is false and every call does nothing, so
// Settings.CameraInputAvailable stays false and the camera option conducts with the mouse.

using System.Runtime.InteropServices;

namespace Bonanza
{
    public enum CameraStatus { Idle = 0, Loading = 1, Running = 2, Failed = 3, Suspended = 4 }

    public static class BonanzaCamera
    {
        /// One queued entry: a palm position (Lost = false) or "the hand left the frame" (Lost = true).
        public struct Sample { public bool Lost; public double PerfMs, Y, X; }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int Bonanza_CameraSupported();
        [DllImport("__Internal")] static extern int Bonanza_CameraStart();
        [DllImport("__Internal")] static extern void Bonanza_CameraStop(int token);
        [DllImport("__Internal")] static extern int Bonanza_CameraStatus();
        [DllImport("__Internal")] static extern System.IntPtr Bonanza_CameraReason();
        [DllImport("__Internal")] static extern void Bonanza_CameraSetPreview(int on);
        [DllImport("__Internal")] static extern double Bonanza_CameraNow();
        [DllImport("__Internal")] static extern int Bonanza_CameraPoll(double[] buf, int max);

        public static bool Supported => Bonanza_CameraSupported() != 0;
        public static int Start() => Bonanza_CameraStart();
        public static void Stop(int token) => Bonanza_CameraStop(token);
        public static CameraStatus Status => (CameraStatus)Bonanza_CameraStatus();
        public static string Reason => Marshal.PtrToStringUTF8(Bonanza_CameraReason());
        public static void SetPreview(bool on) => Bonanza_CameraSetPreview(on ? 1 : 0);
        /// performance.now() in ms, the clock Sample.PerfMs is stamped in.
        public static double PerfNow => Bonanza_CameraNow();
        static int Poll(double[] buf, int max) => Bonanza_CameraPoll(buf, max);
#else
        public static bool Supported => false;
        public static int Start() => 0;
        public static void Stop(int token) { }
        public static CameraStatus Status => CameraStatus.Failed;
        public static string Reason => "not a WebGL player";
        public static void SetPreview(bool on) { }
        public static double PerfNow => 0;
        static int Poll(double[] buf, int max) => 0;
#endif

        public const int MaxPerPoll = 64;
        static readonly double[] _buf = new double[MaxPerPoll * 4];

        /// Move the queued samples into `into` (appended, oldest first). Call once a frame.
        public static void Drain(System.Collections.Generic.List<Sample> into)
        {
            int n;
            do
            {
                n = Poll(_buf, MaxPerPoll);
                for (int i = 0; i < n; i++)
                    into.Add(new Sample { Lost = _buf[i * 4] != 0, PerfMs = _buf[i * 4 + 1], Y = _buf[i * 4 + 2], X = _buf[i * 4 + 3] });
            } while (n == MaxPerPoll);
        }
    }
}
