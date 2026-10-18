// Camera conducting, the pure part (web games/swingKings/index.js startGestureSource + camera.js). No UnityEngine.
//
// The browser tracks the palm and queues (performance.now() stamp, y) samples; the game drains them once a frame.
// This turns a sample's browser stamp into game time and feeds the same IctusDetector the mouse uses:
//
//   gameTime = gameNow - (perfNow - perfStamp)/1000 - Latency
//
// i.e. the sample is `age` seconds old when the game reads it (both stamps are performance.now(), so no clock
// epochs are compared), and a webcam frame is itself ~70 ms older than its stamp (capture + inference, web
// CAMERA_LATENCY). `gameNow` is the game's latency-compensated audio time, so this is the web's
// clock.toAudioTime(perfMs) - CAMERA_LATENCY. A hand that leaves the frame resets the detector.

using System;

namespace Bonanza.Core
{
    public sealed class CameraConductor
    {
        /// Webcam frames arrive about this late (capture + inference); taken off each sample (web CAMERA_LATENCY).
        public const double Latency = 0.07;
        /// A sample older than this when it is read (a paused game, a stalled tab) is dropped, and the detector
        /// restarts, so a stale half-stroke can never complete into a swing.
        public const double StaleAfter = 0.5;

        public readonly IctusDetector Detector = new IctusDetector();

        public static double SampleTime(double gameNow, double perfNowMs, double perfMs)
            => gameNow - (perfNowMs - perfMs) / 1000.0 - Latency;

        /// One tracked position, y in frame heights growing downward. True when it completes a raise or an ictus.
        public bool Feed(double gameNow, double perfNowMs, double perfMs, double y, out GestureEvent ev)
        {
            ev = default;
            if ((perfNowMs - perfMs) / 1000.0 > StaleAfter) { Detector.Reset(); return false; }
            return Detector.Feed(SampleTime(gameNow, perfNowMs, perfMs), y, out ev);
        }

        /// The hand left the frame (or the camera dropped out): forget the half-finished gesture.
        public void Lost() => Detector.Reset();
    }
}
