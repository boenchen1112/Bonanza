using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Bonanza.Core.Tests
{
    // The title attract reel (web shell/title.js) and beat (shell/theme.js beatPulse, title.js update()),
    // pinned to Assets/Tests/EditMode/Golden/title-*.txt (Tools/golden/dump-charts.mjs).
    public class TitleReelTests
    {
        [Test]
        public void ConstantsAreTheWebs()
        {
            var c = new Dictionary<string, double>();
            foreach (var r in GoldenFile.Read("title-reel-consts.txt")) c[r.Str("k")] = r.Num("v");
            Assert.AreEqual(c["IDLE_TO_ATTRACT"], TitleReel.IdleToAttract);
            Assert.AreEqual(c["ATTRACT_HOLD"], TitleReel.AttractHold);
            Assert.AreEqual(c["SHELL_BPM"], TitleBeat.WebShellBpm);
            Assert.AreEqual(c["GAMES"], 5);
        }

        [Test]
        public void ReelFollowsTheRegistryOrderOfTheGames()
        {
            var rows = GoldenFile.Read("title-reel-games.txt");
            Assert.AreEqual(rows.Count, 5);
            for (int i = 0; i < rows.Count; i++) Assert.AreEqual(rows[i].Str("id"), Bonanza.GameHost.Games[i].Id, "reel game " + i);
        }

        [Test]
        public void ScriptedTimelineMatchesTheWebFrameForFrame()
        {
            var script = new Dictionary<int, string>
            {
                [1500] = "a", [2100] = "a", [2200] = "b", [2300] = "pause", [2400] = "pause", [2450] = "up",
                [2500] = "left", [2600] = "a", [2601] = "confirm", [3500] = "b", [3600] = "unconfirm",
            };
            var reel = new TitleReel(5);
            bool confirm = false;
            const double dt = 1.0 / 60;
            var got = new List<string>();
            void Log(int frame, string kind) => got.Add($"{frame} {kind} {reel.GameIndex} {reel.Step} {(reel.Attract ? "attract" : "menu")}");
            for (int frame = 0; frame < 6000; frame++)
            {
                if (script.TryGetValue(frame, out var act))
                {
                    if (act == "confirm") confirm = true;
                    else if (act == "unconfirm") confirm = false;
                    else
                    {
                        var r = reel.Press(act, confirm);
                        Log(frame, r == ReelPress.Left ? "leave" : r == ReelPress.Ignored ? "ignored" : r == ReelPress.Entered ? "enter" : "menu");
                    }
                }
                var ev = reel.Update(dt, confirm);
                if (ev == ReelEvent.Entered) Log(frame, "enter");
                else if (ev == ReelEvent.Stepped) Log(frame, "step");
            }
            var want = new List<string>();
            foreach (var r in GoldenFile.Read("title-reel.txt"))
                want.Add($"{r.Int("frame")} {r.Str("kind")} {r.Int("idx")} {r.Int("step")} {r.Str("mode")}");
            CollectionAssert.AreEqual(want, got);
        }

        [Test]
        public void IdleTimerGatesTheReelAndAPressResetsIt()
        {
            var reel = new TitleReel(5);
            double t = 0;
            while (t < 8.4) { Assert.AreEqual(ReelEvent.None, reel.Update(0.1, false)); t += 0.1; }
            Assert.IsFalse(reel.Attract);
            Assert.AreEqual(ReelPress.Menu, reel.Press("down", false));
            Assert.AreEqual(0, reel.Idle);
            for (int i = 0; i < 84; i++) reel.Update(0.1, false);
            Assert.IsFalse(reel.Attract, "8.4 s since the press: not yet");
            Assert.AreEqual(ReelEvent.Entered, reel.Update(0.2, false));
            Assert.IsTrue(reel.Attract);
        }

        [Test]
        public void ReelStepsEveryHoldAndWrapsTheCatalog()
        {
            var reel = new TitleReel(5);
            Assert.AreEqual(ReelPress.Entered, reel.Press("pause", false));
            var seen = new List<int> { reel.GameIndex };
            double sinceStep = 0;
            for (int i = 0; i < 60 * 40; i++)
            {
                sinceStep += 1.0 / 60;
                if (reel.Update(1.0 / 60, false) == ReelEvent.Stepped) { seen.Add(reel.GameIndex); Assert.AreEqual(TitleReel.AttractHold, sinceStep, 0.03); sinceStep = 0; }
            }
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 0, 1, 2, 3, 4, 0, 1, 2 }, seen);
        }

        [Test]
        public void ProgressRunsZeroToOneAndAPressLeavesWithoutPassingThrough()
        {
            var reel = new TitleReel(5);
            Assert.AreEqual(0, reel.Progress);
            reel.Press("b", false);
            reel.Update(1.6, false);
            Assert.AreEqual(0.5, reel.Progress, 1e-9);
            Assert.AreEqual(ReelPress.Left, reel.Press("a", false), "consumed: the menu never sees it");
            Assert.IsFalse(reel.Attract);
            Assert.AreEqual(0, reel.Progress);
            Assert.AreEqual(ReelPress.Menu, reel.Press("a", false));
        }

        [Test]
        public void ConfirmingBlocksTheReelAndIgnoresBack()
        {
            var reel = new TitleReel(5);
            for (int i = 0; i < 200; i++) Assert.AreNotEqual(ReelEvent.Entered, reel.Update(0.1, true));
            Assert.AreEqual(ReelPress.Ignored, reel.Press("b", true));
        }

        [Test]
        public void BeatMathMatchesTheWeb()
        {
            foreach (var r in GoldenFile.Read("title-beat.txt"))
            {
                double beat = r.Num("beat"); int i = r.Int("i");
                Assert.AreEqual(r.Num("pulse6"), TitleBeat.Pulse(beat, 6), 1e-12, "pulse " + beat);
                Assert.AreEqual(r.Num("down"), TitleBeat.Pulse(beat / 4, 2.4), 1e-12, "down " + beat);
                TitleBeat.Letter(beat, i, 1, out double scale, out double lift, out double tilt);
                Assert.AreEqual(r.Num("scale"), scale, 1e-12, $"scale {beat} {i}");
                Assert.AreEqual(r.Num("lift"), lift, 1e-12, $"lift {beat} {i}");
                Assert.AreEqual(r.Num("tilt"), tilt, 1e-12, $"tilt {beat} {i}");
            }
            Assert.AreEqual(0.5f, TitleBeat.KickGain(true));
            Assert.AreEqual(0.34f, TitleBeat.KickGain(false));
        }
    }
}
