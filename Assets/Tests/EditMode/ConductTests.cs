// Swing Kings mouse conducting. The detector is pinned against traces recorded from the web's gesture.js
// (Tools/golden/dump-charts.mjs -> Golden/swing-ictus*.txt); the rest pins the hold-and-release path of
// SwingRound, the scripted conductor, and the ribbon's data.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Bonanza.Core;

namespace Bonanza.Core.Tests
{
    public class ConductTests
    {
        const double Eps = 1e-9;

        static void CheckEvent(GoldenRow r, bool fired, GestureEvent ev, string what)
        {
            Assert.AreEqual(r.Has("ev"), fired, what);
            if (!fired) return;
            Assert.AreEqual(r.Str("ev") == "raise" ? GestureKind.Raise : GestureKind.Ictus, ev.Kind, what);
            Assert.AreEqual(r.Num("evT"), ev.Time, Eps, what);
        }

        [Test]
        public void Detector_matches_the_web_traces_sample_for_sample()
        {
            var rows = GoldenFile.Read("swing-ictus.txt");
            Assert.Greater(rows.Count, 1500);
            string cur = null; IctusDetector d = null;
            int ictus = 0;
            foreach (var r in rows)
            {
                if (r.Str("scenario") != cur)
                {
                    cur = r.Str("scenario");
                    d = cur == "strict" ? new IctusDetector(0.2, 2, 0.2, 0.5, 0.4) : new IctusDetector();
                }
                string what = $"{cur} t={r.Str("t")}";
                if (r.Str("op") == "reset") { d.Reset(); Assert.AreEqual(r.Str("state"), d.Current.ToString().ToLowerInvariant(), what); continue; }
                bool fired = d.Feed(r.Num("t"), r.Num("y"), out var ev);
                CheckEvent(r, fired, ev, what);
                if (fired) Assert.AreEqual(r.Num("evY"), ev.Y, Eps, what);
                if (fired && ev.Kind == GestureKind.Ictus) ictus++;
                Assert.AreEqual(r.Str("state"), d.Current.ToString().ToLowerInvariant(), what);
            }
            Assert.AreEqual(10, ictus, "the recorded traces hold ten ictuses between them");
        }

        [Test]
        public void Pointer_conductor_feeds_a_still_pointer_like_the_web()
        {
            var rows = GoldenFile.Read("swing-ictus-pointer.txt");
            var pc = new PointerConductor();
            bool ictus = false;
            foreach (var r in rows)
            {
                GestureEvent ev; bool fired;
                if (r.Str("op") == "m") fired = pc.Move(r.Num("t"), r.Num("y"), out ev);
                else fired = pc.Update(r.Num("now"), out ev);
                CheckEvent(r, fired, ev, $"{r.Str("op")} now={r.Str("now")}");
                ictus |= fired && ev.Kind == GestureKind.Ictus;
            }
            Assert.IsTrue(ictus, "the stop with no more events is still detected");
        }

        [Test]
        public void Pointer_conductor_stamps_the_same_ictus_at_a_slow_frame_rate()
        {
            var rows = GoldenFile.Read("swing-ictus-pointer.txt");
            double Run(double fps)
            {
                var pc = new PointerConductor();
                double ictus = double.NaN, last = 0;
                foreach (var r in rows.Where(x => x.Str("op") == "m")) { if (pc.Move(r.Num("t"), r.Num("y"), out var e) && e.Kind == GestureKind.Ictus) ictus = e.Time; last = r.Num("t"); }
                for (double now = last; now < last + 1.5 && double.IsNaN(ictus); now += 1 / fps)
                    while (pc.Update(now, out var e)) if (e.Kind == GestureKind.Ictus) { ictus = e.Time; break; }
                return ictus;
            }
            double at60 = Run(60);
            Assert.IsFalse(double.IsNaN(at60));
            Assert.AreEqual(at60, Run(10), 1e-9, "10 fps");
            Assert.AreEqual(at60, Run(4), 1e-9, "4 fps");
        }

        [Test]
        public void Scripted_conduct_gives_an_ictus_exactly_on_the_contact_time()
        {
            foreach (double target in new[] { 3.0, 7.123456, 12.5 })
            {
                var pc = new PointerConductor();
                var ev = new List<GestureEvent>();
                double t = target - 1.2;
                while (true)
                {
                    t = ConductScript.NextSample(t, target);
                    if (pc.Move(t, ConductScript.Y(t - target), out var e)) ev.Add(e);
                    if (t >= target) break;
                }
                var ict = ev.Where(e => e.Kind == GestureKind.Ictus).ToList();
                Assert.AreEqual(1, ict.Count, $"target {target}");
                Assert.AreEqual(target, ict[0].Time, 1e-9, $"target {target}");
            }
        }

        static SwingRound ConductedRound(out int swings)
        {
            var r = new SwingRound();
            r.Start(1.0);
            var scored = r.Pitches.Where(p => p.Scored).ToList();
            var bot = new ConductBot(scored.Select(p => r.Map.TimeAt(p.TargetBeat)).ToArray(),
                                     scored.Select(p => r.Map.TimeAt(p.LaunchBeat)).ToArray(), 1.0);
            int n = 0;
            for (double t = 1.0; t < r.Map.TimeAt(SwingRules.EndBeat + 2); t += 1.0 / 60)
            {
                bot.Advance(t, tt => r.BeginWindup(tt), tt => { r.ReleaseSwing(tt); n++; });
                r.Update(t);
            }
            swings = n;
            return r;
        }

        [Test]
        public void A_scripted_conduct_through_the_detector_plays_an_S_rank_round()
        {
            var r = ConductedRound(out int swings);
            Assert.IsTrue(r.Over);
            var res = r.Result();
            Assert.AreEqual(45, swings);
            Assert.AreEqual("S", res.Rank);
            Assert.AreEqual(0, res.Miss);
            Assert.AreEqual(45, res.Perfect);
            Assert.IsTrue(res.GrandSlam, "the windup lasted the whole flight, so the finale is a slam");
            Assert.Greater(res.AvgPower, 0.7);
        }

        [Test]
        public void Release_after_a_real_hold_takes_power_from_the_hold_not_the_timing()
        {
            var r = new SwingRound();
            r.Start(1.0);
            var p = r.Pitches.First(x => x.Scored);
            double target = r.Map.TimeAt(p.TargetBeat);
            double spb = r.Map.Spb;
            r.Update(r.Map.TimeAt(p.LaunchBeat) + 0.01);
            r.BeginWindup(target - 0.2 * spb);                 // 0.2 beat hold: a real hold, but a short one
            r.Update(target - 0.01);
            r.ReleaseSwing(target);                             // dead on time
            Assert.AreEqual(SwingRules.PowerForHold(0.2, p.Ideal), r.LastPower, 1e-12);
            Assert.Less(r.LastPower, 0.28, "a perfectly timed short hold is a bunt, not a home run");
            Assert.AreEqual(1, r.Judge.Stats.Perfect);
        }

        [Test]
        public void A_hold_under_a_tenth_of_a_beat_is_timed_like_a_tap()
        {
            var r = new SwingRound();
            r.Start(1.0);
            var p = r.Pitches.First(x => x.Scored);
            double target = r.Map.TimeAt(p.TargetBeat);
            r.Update(target - 0.01);
            r.BeginWindup(target - 0.03);
            r.ReleaseSwing(target);
            Assert.AreEqual(1.0, r.LastPower, 1e-12);
            Assert.IsFalse(r.Holding);
            Assert.IsFalse(r.ReleaseSwing(target + 0.5), "a second release with no windup does nothing");
        }

        [Test]
        public void Tap_press_is_unchanged_by_the_windup_path()
        {
            var r = new SwingRound();
            r.Start(1.0);
            var p = r.Pitches.First(x => x.Scored);
            double target = r.Map.TimeAt(p.TargetBeat);
            r.Update(target - 0.01);
            r.Press(target + 0.05);
            Assert.AreEqual(SwingRules.PowerFromTiming(target + 0.05, target), r.LastPower, 1e-12);
            Assert.IsFalse(r.Holding);
        }

        [Test]
        public void Trace_keeps_the_whole_gesture_and_thins_it_when_full()
        {
            var t = new ConductTrace();
            t.Begin(0, 0, 0);
            for (int i = 0; i < 2000; i++) t.Push(1.0 / 60, i * 0.01f, 0, 0, i / 2000.0);
            Assert.LessOrEqual(t.Count, ConductTrace.MaxSamples);
            Assert.Greater(t.Count, ConductTrace.MaxSamples / 2 - 1);
            Assert.Greater(t.X[t.Count - 1], t.X[1], "samples stay in order, the head is the newest");
            Assert.AreEqual(1.0, t.Fade);
            t.Release();
            t.Update(0.2);
            Assert.Less(t.Fade, 1.0);
            t.Update(1.0);
            Assert.AreEqual(0, t.Fade);
            float head = t.X[t.Count - 1];
            t.Push(1, 99, 5, 5, 1);   // ignored once released
            Assert.AreEqual(head, t.X[t.Count - 1]);
        }

        [Test]
        public void Trace_colour_runs_cold_to_white_hot()
        {
            ConductTrace.RampColor(0, out float r0, out float g0, out float b0);
            ConductTrace.RampColor(1, out float r1, out float g1, out float b1);
            Assert.Greater(b0, r0, "cold is blue");
            Assert.Greater(r1, 0.95f);
            Assert.Greater(g1, 0.9f);
            ConductTrace.RampColor(0.55, out float rm, out float gm, out _);
            Assert.AreEqual(1f, rm, 0.01f);   // 0xffd93d
            Assert.AreEqual(0xd9 / 255f, gm, 0.01f);
        }
    }
}
