// Swing Kings rules. The tap-power tests are ported from the web prototype's
// web/tests/swingkings.test.mjs (a critic round once found every hit was a
// HOME RUN; this pins the stepped mapping). The round tests pin the port of
// the tap-mode flow from web/src/games/swingKings/index.js.

using System.Linq;
using NUnit.Framework;
using RhythmGame.Core;

namespace RhythmGame.Core.Tests
{
    public class SwingRulesTests
    {
        static TierId TierAt(double ms) => SwingRules.TierFor(SwingRules.PowerFromTiming(10 + ms / 1000, 10)).Id;

        [Test]
        public void PerfectTimingEarlyOrLateIsAHomeRun()
        {
            foreach (var ms in new[] { 0, 20, -30, Windows.PerfectMs - 0.5, -(Windows.PerfectMs - 0.5) })
                Assert.AreEqual(TierId.Homer, TierAt(ms), $"{ms}ms");
        }

        [Test]
        public void GreatTimingIsALineDriveAcrossTheWholeBand()
        {
            foreach (var ms in new[] { Windows.PerfectMs + 1, 60, -70, Windows.GreatMs - 0.5 })
                Assert.AreEqual(TierId.Liner, TierAt(ms), $"{ms}ms");
        }

        [Test]
        public void GoodTimingIsABunt()
        {
            foreach (var ms in new[] { Windows.GreatMs + 1, 100, -120, Windows.GoodMs - 0.5 })
                Assert.AreEqual(TierId.Bunt, TierAt(ms), $"{ms}ms");
        }

        [Test]
        public void PowerNeverRisesAsTimingGetsWorse()
        {
            double prev = double.PositiveInfinity;
            for (double ms = 0; ms <= Windows.GoodMs + 20; ms += 2)
            {
                double p = SwingRules.PowerFromTiming(10 + ms / 1000, 10);
                Assert.LessOrEqual(p, prev + 1e-12, $"{ms}ms");
                prev = p;
            }
        }

        [Test]
        public void FinaleHomeRunIsAGrandSlam()
        {
            Assert.AreEqual(TierId.Slam, SwingRules.TierFor(1, finale: true).Id);
            Assert.AreEqual(TierId.Liner, SwingRules.TierFor(0.5, finale: true).Id);
        }

        [Test]
        public void HoldPowerTapIsZeroIdealIsFullAndOverHoldingSags()
        {
            Assert.AreEqual(0, SwingRules.PowerForHold(0.05, 2));
            Assert.AreEqual(1, SwingRules.PowerForHold(2, 2), 1e-9);
            Assert.Less(SwingRules.PowerForHold(8, 2), SwingRules.PowerForHold(2, 2));
        }

        [Test]
        public void ScheduleMatchesTheChart()
        {
            var s = SwingRules.BuildSchedule();
            Assert.AreEqual(1 + 8 + 8 + 16 + 12 + 1, s.Count);
            Assert.AreEqual(PitchKind.Demo, s[0].Kind);
            Assert.IsFalse(s[0].Scored);
            Assert.AreEqual(1, s.Count(p => p.Finale));
            Assert.AreEqual(SwingRules.FinaleBeat, s.Last().TargetBeat);
            Assert.AreEqual(SwingRules.FinaleBeat - 8, s.Last().LaunchBeat);
            // Every pitch is visible as a moving ball at least 1.5 beats early.
            Assert.IsTrue(s.All(p => p.Lead >= 1.5));
            // Launch order matches target order (the update loop relies on it).
            for (int i = 1; i < s.Count; i++) Assert.LessOrEqual(s[i - 1].LaunchBeat, s[i].LaunchBeat);
        }

        [Test]
        public void SectionsFollowTheBeat()
        {
            Assert.AreEqual(Section.Lead, SwingRules.SectionAt(-1));
            Assert.AreEqual(Section.Teach, SwingRules.SectionAt(0));
            Assert.AreEqual(Section.Play, SwingRules.SectionAt(32));
            Assert.AreEqual(Section.Escalate, SwingRules.SectionAt(96));
            Assert.AreEqual(Section.Finale, SwingRules.SectionAt(128));
        }

        // ------------------------------------------------------------ rounds

        /// Run a whole round at 60fps, pressing at `offsetMs` from every
        /// scored pitch (null = never press).
        static SwingRound Play(double? offsetMs, System.Action<SwingRound> hook = null)
        {
            var r = new SwingRound();
            hook?.Invoke(r);
            r.Start(1.0);
            var presses = offsetMs.HasValue
                ? r.Judge.Notes.Select(n => n.Time + offsetMs.Value / 1000).ToList()
                : new System.Collections.Generic.List<double>();
            int k = 0;
            for (double t = 1.0; t < r.Map.TimeAt(SwingRules.EndBeat + 2); t += 1.0 / 60)
            {
                while (k < presses.Count && presses[k] <= t) r.Press(presses[k++]);
                r.Update(t);
            }
            return r;
        }

        [Test]
        public void PerfectRunIsAnSRankWithAGrandSlam()
        {
            var r = Play(0);
            Assert.IsTrue(r.Over);
            var res = r.Result();
            Assert.AreEqual("S", res.Rank);
            Assert.AreEqual(0, res.Miss);
            Assert.AreEqual(45, res.Perfect);
            Assert.IsTrue(res.GrandSlam);
            Assert.AreEqual(44 + 1, res.Homers);   // 44 home runs + the grand slam
            // 44 perfects at 1000 + the finale at 2x.
            Assert.AreEqual(44 * 1000 + 2000, res.Score);
        }

        [Test]
        public void GreatRunIsAllLineDrives()
        {
            var r = Play(60);
            var res = r.Result();
            Assert.AreEqual(45, res.Great);
            Assert.AreEqual(45, res.Liners);
            Assert.IsFalse(res.GrandSlam);
        }

        [Test]
        public void LateGoodIsAFoulEarlyGoodIsNot()
        {
            Assert.AreEqual(45, Play(110).Result().Fouls);
            Assert.AreEqual(0, Play(-110).Result().Fouls);
        }

        [Test]
        public void NeverSwingingIsAllStrikesAndOutsRetireTheSide()
        {
            int strikes = 0, retired = 0, maxOuts = 0;
            var r = Play(null, round => round.Whiff += w =>
            {
                if (!w.Swung) strikes++;
                if (w.SideRetired) retired++;
                maxOuts = System.Math.Max(maxOuts, w.Outs);
            });
            Assert.AreEqual(45, strikes);
            Assert.AreEqual(15, retired);
            Assert.AreEqual(3, maxOuts);
            Assert.AreEqual("D", r.Result().Rank);
            Assert.IsTrue(r.Over, "a round with no presses still ends");
        }

        [Test]
        public void TheDemoPitchSwingsItselfAndIsNotScored()
        {
            int demos = 0;
            var r = Play(null, round => round.Contact += c => { if (c.IsDemo) demos++; });
            Assert.AreEqual(1, demos);
            Assert.AreEqual(45, r.Judge.Stats.Miss, "the demo is never a judged note");
        }

        [Test]
        public void PressBetweenPitchesIsASwingAtNothing()
        {
            int nothing = 0;
            var r = new SwingRound();
            r.SwingAtNothing += _ => nothing++;
            r.Start(1.0);
            double between = r.Map.TimeAt(1);   // pitches land on 0 and 4
            r.Update(between);
            int missesBefore = r.Judge.Stats.Miss;
            r.Press(between);
            Assert.AreEqual(1, nothing);
            Assert.AreEqual(missesBefore, r.Judge.Stats.Miss, "a stray swing must not burn a pitch");
        }
    }
}

