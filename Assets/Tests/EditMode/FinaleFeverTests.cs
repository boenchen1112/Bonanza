using System.Linq;
using NUnit.Framework;

namespace RhythmGame.Core.Tests
{
    public class FinaleFeverTests
    {
        [Test]
        public void ChartMatchesTheWebBuild()
        {
            var (notes, cues, phrases) = FeverChart.Build();

            var gn = GoldenFile.Read("finale-fever-notes.txt");
            Assert.AreEqual(gn.Count, notes.Count, "notes");
            for (int i = 0; i < notes.Count; i++)
            {
                var g = gn[i]; var n = notes[i]; string at = $"note {i}";
                Assert.AreEqual(g.Num("beat"), n.Beat, at);
                Assert.AreEqual(g.Str("action"), n.Action, at);
                Assert.AreEqual(g.Str("kind"), n.Kind, at);
                Assert.AreEqual(g.Int("lane"), n.Lane, at);
                Assert.AreEqual(g.Int("telegraph"), n.Telegraph, at);
                Assert.AreEqual(g.Str("section"), n.Section, at);
                Assert.AreEqual(g.Int("weight"), n.Weight, at);
                Assert.AreEqual(g.Str("patName"), n.PatName, at);
            }

            var gc = GoldenFile.Read("finale-fever-cues.txt");
            Assert.AreEqual(gc.Count, cues.Count, "cues");
            for (int i = 0; i < cues.Count; i++)
            {
                var g = gc[i]; var c = cues[i]; string at = $"cue {i}";
                Assert.AreEqual(g.Num("beat"), c.Beat, at);
                Assert.AreEqual(g.Str("type"), c.Type, at);
                Assert.AreEqual(g.Str("kind"), c.Kind, at);
                Assert.AreEqual(g.Flag("accent"), c.Accent, at);
                Assert.AreEqual(g.Int("tone"), c.Tone, at);
                Assert.AreEqual(g.Int("of"), c.Of, at);
            }

            var gp = GoldenFile.Read("finale-fever-phrases.txt");
            Assert.AreEqual(gp.Count, phrases.Count, "phrases");
            for (int i = 0; i < phrases.Count; i++)
            {
                var g = gp[i]; var p = phrases[i]; string at = $"phrase {i}";
                Assert.AreEqual(g.Int("idx0"), p.Idx0, at);
                Assert.AreEqual(g.Int("idx1"), p.Idx1, at);
                Assert.AreEqual(g.Num("callBeat0"), p.CallBeat0, at);
                Assert.AreEqual(g.Num("ansBeat0"), p.AnsBeat0, at);
                Assert.AreEqual(g.Num("endBeat"), p.EndBeat, at);
                Assert.AreEqual(g.Str("section"), p.Section, at);
                Assert.AreEqual(g.Str("patName"), p.PatName, at);
                Assert.AreEqual(g.Flag("solo"), p.Solo, at);
                Assert.AreEqual(g.Flag("finale"), p.Finale, at);
            }
        }

        [Test]
        public void TempoCurveMatchesTheWebBuild()
        {
            foreach (var g in GoldenFile.Read("finale-fever-tempo.txt"))
            {
                double b = g.Num("beat");
                if (b > 0) Assert.AreEqual(g.Num("bpm"), FeverChart.BpmAt(b), 1e-12, $"bpm at {b}");
                Assert.AreEqual(g.Str("section"), FeverChart.SectionAt(b), $"section at {b}");
                Assert.AreEqual(g.Num("progress"), FeverChart.ProgressAt(b), 1e-12, $"progress at {b}");
            }
        }

        [Test]
        public void RampMapIntegratesTheCurve()
        {
            var map = new RampMap(FeverChart.BpmAt, FeverChart.Bpm0, FeverChart.EndBeat + 16);
            map.Start(3, -8);
            Assert.AreEqual(3, map.TimeAt(-8), 1e-12);
            Assert.AreEqual(3 + 8 * 0.4, map.TimeAt(0), 1e-12, "lead-in is square at 150");

            // Brute force: midpoint rule, 400k steps.
            int steps = 400000;
            double h = FeverChart.EndBeat / steps, s = 0;
            for (int i = 0; i < steps; i++) s += h * 60 / FeverChart.BpmAt((i + 0.5) * h);
            Assert.AreEqual(3 + 3.2 + s, map.TimeAt(FeverChart.EndBeat), 1e-7);

            foreach (var b in new[] { -7.5, -0.01, 0, 0.013, 17.3, 99.99, 150.25, 200, 210 })
                Assert.AreEqual(b, map.BeatAt(map.TimeAt(b)), 1e-9, $"round trip at {b}");

            // Local tempo at the top: 187.5 bpm.
            double d = 1e-4;
            Assert.AreEqual(60 / 187.5, (map.TimeAt(200 + d) - map.TimeAt(200 - d)) / (2 * d), 1e-6);
        }

        static FeverRound Play(System.Func<FeverNote, bool> skip = null)
        {
            var round = new FeverRound();
            round.Start(1);
            var presses = round.Live.Where(n => skip == null || !skip((FeverNote)n.Data)).Select(n => (n.Action, n.Time)).ToList();
            int k = 0;
            for (double t = 1; !round.Done && t < 200; t += 1 / 60.0)
            {
                while (k < presses.Count && presses[k].Time <= t) { round.Press(presses[k].Action, t); k++; }
                round.Update(t);
            }
            return round;
        }

        [Test]
        public void PerfectRunIsAMaestroS()
        {
            var r = Play();
            var o = r.Result();
            Assert.NotNull(o);
            Assert.AreEqual(155, o.Perfect);
            Assert.AreEqual(0, o.Miss);
            Assert.AreEqual("S", o.Rank);
            Assert.AreEqual(1000, o.Score);
            Assert.AreEqual("MAESTRO", r.Tier);
            Assert.AreEqual(Verdict.Perfect, r.FinaleVerdict);
            Assert.AreEqual(3, r.Hearts);
        }

        [Test]
        public void LosingEveryHeartSurvivesButCapsTheRank()
        {
            // Skip everything in the first twelve phrases: hearts run out, the round goes on.
            var r = Play(n => n.Beat < 12 * 8);
            var o = r.Result();
            Assert.NotNull(o, "never ejected");
            Assert.IsTrue(r.Survived);
            Assert.AreEqual("SURVIVOR", r.Tier);
            Assert.That(o.Rank, Is.Not.EqualTo("S").And.Not.EqualTo("A"));
            Assert.Greater(r.Hearts, 0, "clean phrases after that buy hearts back");
        }

        [Test]
        public void TheMainButtonTakesHalfAChord()
        {
            var r = new FeverRound();
            r.Start(0);
            var chord = r.Live.First(n => ((FeverNote)n.Data).Kind == "chord");
            Assert.IsTrue(r.Press("a", chord.Time));
            Assert.IsTrue(r.Press("a", chord.Time), "second press takes the other lane");
            Assert.IsFalse(r.Press("a", chord.Time), "third press is a ghost");
            Assert.AreEqual(1, r.Ghosts);
        }
    }
}
