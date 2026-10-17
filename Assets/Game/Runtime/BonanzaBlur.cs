// Window blur in a WebGL player. Unity's own focus callback does not reliably follow the page's
// window blur (an iframe, devtools, an alert), and the web build releases every held input on it.
// The jslib counts blur and visibility-hidden events; GameHost polls the counter once a frame.

using System.Runtime.InteropServices;

namespace Bonanza
{
    public static class BonanzaBlur
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void Bonanza_BlurInstall();
        [DllImport("__Internal")] static extern int Bonanza_BlurCount();
        public static void Install() => Bonanza_BlurInstall();
        public static int Count() => Bonanza_BlurCount();
#else
        public static void Install() { }
        public static int Count() => 0;
#endif
    }
}
