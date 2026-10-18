// Camera conducting: the stamp conversion and the feed into the shared ictus detector (Core/CameraConductor).

using System.Collections.Generic;
using NUnit.Framework;
using Bonanza.Core;

namespace Bonanza.Core.Tests
{
    public class CameraConductTests
    {
        const double Eps = 1e-9;

        [Test] public void Sample_time_is_the_age_and_the_camera_latency_before_now()
        {
            // read at game time 10 s, browser clock 5000 ms, sample stamped 4980 ms: 20 ms old, plus 70 ms of camera
            Assert.AreEqual(10 - 0.020 - 0.070, CameraConductor.SampleTime(10, 5000, 4980), Eps);
            Assert.AreEqual(10 - 0.070, CameraConductor.SampleTime(10, 5000, 5000), Eps);
        }

        [Test] public void Samples_older_than_half_a_second_are_dropped_and_restart_the_detector()
        {
            var c = new CameraConductor();
            Assert.IsFalse(c.Feed(10, 5000, 4990, 0.7, out _));
            Assert.IsFalse(c.Feed(10, 5000, 4400, 0.2, out _));          // 0.6 s old
            Assert.AreEqual(IctusDetector.State.Rest, c.Detector.Current);
            // the next fresh sample starts a new rest anchor instead of raising from the stale one
            Assert.IsFalse(c.Feed(10.02, 5020, 5010, 0.7, out _));
        }

        /// Plays the web mouse-check stroke through the conductor with the frames arriving `lateMs` after their stamp.
        static List<GestureEvent> Play(double contact, double lateMs, bool lostMidway = false)
        {
            var c = new CameraConductor();
            var evs = new List<GestureEvent>();
            // physical hand position at time p; the frame carrying it is stamped p + 70 ms (camera delay) and read
            // lateMs after that
            for (double p = contact - 1.2; p < contact + 0.2; p += ConductScript.Frame)
            {
                double y = ConductScript.Y(p - contact);
                double stamp = (p + CameraConductor.Latency) * 1000;     // browser ms, epoch arbitrary
                double read = stamp + lateMs;
                double gameNow = read / 1000.0 + 123.0;                   // game clock with its own epoch
                if (lostMidway && p > contact - 0.30 && p < contact - 0.25) { c.Lost(); continue; }
                if (c.Feed(gameNow, read, stamp, y, out var ev)) evs.Add(new GestureEvent { Kind = ev.Kind, Time = ev.Time - 123.0, Y = ev.Y });
            }
            return evs;
        }

        [Test] public void A_raise_then_a_stroke_gives_a_raise_and_an_ictus_at_the_contact_time()
        {
            foreach (double late in new[] { 0.0, 4.0, 12.0 })
            {
                var evs = Play(2.0, late);
                Assert.AreEqual(2, evs.Count, $"late={late}");
                Assert.AreEqual(GestureKind.Raise, evs[0].Kind);
                Assert.AreEqual(GestureKind.Ictus, evs[1].Kind);
                Assert.Less(evs[0].Time, evs[1].Time - 0.3, "the raise starts the windup well before the swing");
                // the stop is 2 frames before contact and the detector fires on the second sample after: within a frame
                Assert.AreEqual(2.0, evs[1].Time, 0.02, $"late={late}");
            }
        }

        [Test] public void A_lost_hand_mid_gesture_cancels_the_swing()
        {
            var evs = Play(2.0, 0, lostMidway: true);
            Assert.IsFalse(evs.Exists(e => e.Kind == GestureKind.Ictus), "after a loss the detector restarts, rest included");
        }
    }
}
