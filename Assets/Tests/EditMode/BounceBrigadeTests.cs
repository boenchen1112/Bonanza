using System.Linq;
using NUnit.Framework;

namespace Bonanza.Core.Tests
{
    public class BounceBrigadeTests
    {
        [Test]
        public void ChartMatchesTheWebBuild()
        {
            var (plats, notes, finish, max) = BounceChart.Build();
            Assert.AreEqual(130, finish);
            Assert.AreEqual(138000, max);

            var gp = GoldenFile.Read("bounce-brigade-platforms.txt");
            Assert.AreEqual(gp.Count, plats.Count, "platforms");
            for (int i = 0; i < plats.Count; i++)
            {
                var g = gp[i]; var p = plats[i]; string at = $"platform {i}";
                Assert.AreEqual(g.Int("index"), p.Index, at);
                Assert.AreEqual(g.Num("beat"), p.Beat, at);
                Assert.AreEqual(g.Int("deg"), p.Deg, at);
                Assert.AreEqual(g.Str("kind"), p.Kind.ToString(), at);
                Assert.AreEqual(g.Num("roll"), p.Roll, at);
                Assert.AreEqual(g.Flag("scored"), p.Scored, at);
                Assert.AreEqual(g.Flag("charge"), p.Charge, at);
                Assert.AreEqual(g.Int("midi"), p.Midi, at);
                Assert.AreEqual(g.Int("color"), p.Color, at);
                Assert.AreEqual(g.Int("weight"), p.Weight, at);
                foreach (var (k, v) in new[] {
                    ("y", p.Y), ("tilt", p.Tilt), ("departBeat", p.DepartBeat), ("contactX", p.ContactX), ("departX", p.DepartX),
                    ("nextBeat", p.NextBeat), ("flightBeats", p.FlightBeats), ("anchorX", p.AnchorX), ("width", p.Width),
                    ("slope", p.Slope), ("endX", p.EndX), ("departY", p.DepartY), ("centerX", p.CenterX), ("centerY", p.CenterY) })
                    Assert.AreEqual(g.Num(k), v, 1e-12, $"{at} {k}");
            }

            var gn = GoldenFile.Read("bounce-brigade-notes.txt");
            Assert.AreEqual(gn.Count, notes.Count, "notes");
            for (int i = 0; i < notes.Count; i++)
            {
                var g = gn[i]; var n = notes[i]; string at = $"note {i}";
                Assert.AreEqual(g.Num("beat"), n.Beat, at);
                Assert.AreEqual(g.Str("action"), n.Action, at);
                Assert.AreEqual(g.Str("kind"), n.Kind, at);
                Assert.AreEqual(g.Int("platform"), n.Platform, at);
                Assert.AreEqual(g.Int("weight"), n.Weight, at);
            }
        }

        static BounceRound Play(System.Func<BounceNote, bool> skip = null)
        {
            var round = new BounceRound();
            round.Start(1);
            var presses = round.Live.Where(n => skip == null || !skip((BounceNote)n.Data)).Select(n => n.Time).ToList();
            int k = 0;
            for (double t = 1; !round.Done && t < 200; t += 1 / 60.0)
            {
                while (k < presses.Count && presses[k] <= t) { round.Press("a", t); k++; }
                round.Update(t);
            }
            return round;
        }

        [Test]
        public void PerfectRunClearsEveryJump()
        {
            var r = Play();
            var o = r.Result();
            Assert.NotNull(o);
            Assert.AreEqual(135, o.Perfect);
            Assert.AreEqual(0, o.Miss);
            Assert.AreEqual("S", o.Rank);
            Assert.AreEqual(1000, o.Score);
            Assert.AreEqual(5, r.ChargesCleared);
            Assert.IsTrue(r.FinishHit);
        }

        [Test]
        public void AMissedReleasePlungesButTheRoundGoesOn()
        {
            var r = Play(n => n.Kind == "release" && n.Beat == 18);
            Assert.IsTrue(r.Done);
            Assert.AreEqual(1, r.Misses);
            Assert.AreEqual(4, r.ChargesCleared);
            Assert.AreEqual("plunge", r.OutcomeFor(17));
            // Mid-flight off the ramp the bouncer dips toward the water, then lands.
            Assert.Less(r.MotionAt(18 + 2 * 0.48).Y, BounceChart.WaterY + 0.5);
            Assert.AreEqual(r.Platforms[18].Y, r.MotionAt(20).Y, 1e-9);
        }

        [Test]
        public void TheBouncerLandsOnEveryPlatformOnItsBeat()
        {
            var r = new BounceRound();
            r.Start(0);
            foreach (var p in r.Platforms.Where(p => p.Scored && !p.Charge && p.Kind != 'X'))
            {
                var m = r.MotionAt(p.Beat);
                Assert.AreEqual(p.Index, m.K, $"platform {p.Index}");
                Assert.AreEqual(p.Y, m.Y, 1e-9, $"platform {p.Index}");
                Assert.AreEqual(p.ContactX, m.X, 1e-9);
            }
        }

        [Test]
        public void StrayPressesAreGhostsNotPenalties()
        {
            var r = new BounceRound();
            r.Start(0);
            Assert.IsFalse(r.Press("a", r.Map.TimeAt(-4)));
            Assert.AreEqual(1, r.Ghosts);
            Assert.AreEqual(0, r.Misses);
            Assert.AreEqual(0, r.Points);
        }
    }
}
