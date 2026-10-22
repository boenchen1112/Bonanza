// Pins the Finale Fever tempo curves: segments (steady, ramp, jump, fermata) integrated once into
// a beat <-> time map, the seeded generator per hymn x level and its bounds, and the text dump the
// Mac renderer reads. Pure C#, no hymn files.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Bonanza.Core;

namespace Bonanza.Core.Tests
{
    public class TempoCurveTests
    {
        static TempoCurve Make(params TempoSegment[] segs) => new TempoCurve(segs, "t", HymnLevel.Easy, 1, segs[0].Bpm0);

        static IEnumerable<string> Names(int n) => Enumerable.Range(0, n).Select(i => "hymn_" + (i * 7 + 3).ToString("000"));

        // numeric reference: seconds = integral of 60 / bpm(beat) d beat, midpoint rule, fine grid
        static double Integrate(TempoCurve c, double to)
        {
            int steps = 200000;
            double h = to / steps, s = 0;
            for (int i = 0; i < steps; i++) s += 60.0 / c.BpmAt((i + 0.5) * h) * h;
            return s;
        }

        // ---- the segments ------------------------------------------------------

        [Test]
        public void SteadyTempoIsSixtyOverBpmPerBeat()
        {
            var c = Make(TempoSegment.Steady(8, 120));
            Assert.AreEqual(8, c.BeatCount);
            Assert.AreEqual(0, c.TimeAt(0), 1e-12);
            Assert.AreEqual(0.5, c.TimeAt(1), 1e-12);
            Assert.AreEqual(4.0, c.TimeAt(8), 1e-12);
            Assert.AreEqual(2.25, c.TimeAt(4.5), 1e-12);
            Assert.AreEqual(0.5, c.LengthOf(3), 1e-12);
        }

        [Test]
        public void ARampIntegratesTheTempoExactly()
        {
            var c = Make(TempoSegment.Ramp(8, 60, 120));
            double total = 60.0 * 8 / 60 * Math.Log(2);   // 60 L / (to - from) * ln(to / from)
            Assert.AreEqual(total, c.TimeAt(8), 1e-12);
            Assert.AreEqual(Integrate(c, 8), c.TimeAt(8), 1e-6);
            Assert.AreEqual(Integrate(c, 3.3), c.TimeAt(3.3), 1e-6);
            // faster at the end than at the start
            Assert.Greater(c.LengthOf(0), c.LengthOf(7));
            Assert.AreEqual(60, c.BpmAt(0), 1e-9);
            Assert.AreEqual(90, c.BpmAt(4), 1e-9);
            Assert.AreEqual(120, c.BpmAt(8), 1e-9);
        }

        [Test]
        public void ADownRampAndAFlatRampWork()
        {
            var down = Make(TempoSegment.Ramp(6, 120, 90));
            Assert.AreEqual(Integrate(down, 6), down.TimeAt(6), 1e-6);
            var flat = Make(TempoSegment.Ramp(4, 100, 100));
            Assert.AreEqual(2.4, flat.TimeAt(4), 1e-12);
        }

        [Test]
        public void AJumpChangesTheTempoAtTheBeat()
        {
            var c = Make(TempoSegment.Steady(4, 100), TempoSegment.Jump(4, 140));
            Assert.AreEqual(8, c.BeatCount);
            Assert.AreEqual(0.6, c.LengthOf(3), 1e-12);
            Assert.AreEqual(60.0 / 140, c.LengthOf(4), 1e-12);
            Assert.AreEqual(2.4 + 4 * 60.0 / 140, c.TimeAt(8), 1e-12);
            Assert.AreEqual(100, c.BpmAt(3.999), 1e-9);
            Assert.AreEqual(140, c.BpmAt(4), 1e-9);
        }

        [Test]
        public void AFermataStretchesOneBeat()
        {
            var c = Make(TempoSegment.Steady(2, 100), TempoSegment.Fermata(100, 2.5), TempoSegment.Steady(2, 100));
            Assert.AreEqual(5, c.BeatCount);
            Assert.AreEqual(0.6, c.LengthOf(1), 1e-12);
            Assert.AreEqual(1.5, c.LengthOf(2), 1e-12);
            Assert.AreEqual(0.6, c.LengthOf(3), 1e-12);
            Assert.AreEqual(0.6 * 4 + 1.5, c.TimeAt(5), 1e-12);
            Assert.AreEqual(40, c.BpmAt(2.5), 1e-9);   // heard as 100 / 2.5
        }

        [Test]
        public void BeatAtInvertsTimeAtOnEverySegment()
        {
            var c = Make(TempoSegment.Steady(3, 90), TempoSegment.Ramp(6, 90, 110), TempoSegment.Fermata(110, 2),
                         TempoSegment.Jump(4, 80), TempoSegment.Ramp(5, 80, 70));
            for (double b = 0; b <= c.BeatCount; b += 0.173)
                Assert.AreEqual(b, c.BeatAt(c.TimeAt(b)), 1e-9, "beat " + b);
            // strictly increasing
            double prev = -1;
            for (int i = 0; i <= c.BeatCount; i++) { Assert.Greater(c.TimeAt(i), prev); prev = c.TimeAt(i); }
        }

        [Test]
        public void OutsideTheCurveItKeepsTheEdgeTempo()
        {
            var c = Make(TempoSegment.Steady(4, 120));
            Assert.AreEqual(-0.5, c.TimeAt(-1), 1e-12);
            Assert.AreEqual(2.5, c.TimeAt(5), 1e-12);
            Assert.AreEqual(5, c.BeatAt(2.5), 1e-12);
        }

        [Test]
        public void TheBeatTableIsTheCurveIntegratedOnce()
        {
            var c = Make(TempoSegment.Steady(3, 90), TempoSegment.Ramp(6, 90, 110), TempoSegment.Fermata(110, 2));
            Assert.AreEqual(c.BeatCount + 1, c.Times.Count);
            for (int i = 0; i <= c.BeatCount; i++) Assert.AreEqual(c.TimeAt(i), c.Times[i], 1e-12);
        }

        // ---- the generator -----------------------------------------------------

        [Test]
        public void SameHymnAndLevelSameCurveAndTheyVary()
        {
            foreach (HymnLevel lv in Enum.GetValues(typeof(HymnLevel)))
                foreach (var name in Names(10))
                {
                    var a = TempoCurve.Generate(name, lv, 64, 96);
                    var b = TempoCurve.Generate(name, lv, 64, 96);
                    Assert.AreEqual(a.ToText(), b.ToText());
                }
            var texts = Names(30).Select(n => TempoCurve.Generate(n, HymnLevel.Medium, 64, 96).ToText()).Distinct().Count();
            Assert.Greater(texts, 25);
            Assert.AreNotEqual(TempoCurve.Generate("hymn_065", HymnLevel.Easy, 64, 96).ToText(), TempoCurve.Generate("hymn_065", HymnLevel.Hard, 64, 96).ToText());
            Assert.AreNotEqual(TempoCurve.SeedFor("hymn_065", HymnLevel.Easy), TempoCurve.SeedFor("hymn_065", HymnLevel.Medium));
        }

        [Test]
        public void EveryGeneratedCurveCoversTheBeatsWithMonotonicTime()
        {
            foreach (HymnLevel lv in Enum.GetValues(typeof(HymnLevel)))
                foreach (var name in Names(25))
                    foreach (int n in new[] { 16, 48, 64, 70 })
                    {
                        var c = TempoCurve.Generate(name, lv, n, 80);
                        Assert.AreEqual(n, c.BeatCount);
                        Assert.AreEqual(n, c.Segments.Sum(s => s.Beats), 1e-9);
                        Assert.AreEqual(0, c.Times[0], 1e-12);
                        for (int i = 1; i <= n; i++) Assert.Greater(c.Times[i], c.Times[i - 1], name + " " + lv);
                    }
        }

        [Test]
        public void EasyIsGentleRampsWithinFifteenPercent()
        {
            foreach (var name in Names(40))
            {
                var c = TempoCurve.Generate(name, HymnLevel.Easy, 64, 100);
                foreach (var s in c.Segments)
                {
                    Assert.That(s.Kind, Is.EqualTo(TempoKind.Steady).Or.EqualTo(TempoKind.Ramp), name);
                    foreach (double bpm in new[] { s.Bpm0, s.Bpm1 }) Assert.That(bpm, Is.InRange(85 - 1e-9, 115 + 1e-9), name);
                    if (s.Kind == TempoKind.Ramp) Assert.LessOrEqual(Math.Abs(s.Bpm1 - s.Bpm0), 12 + 1e-9, "gentle " + name);
                }
                Assert.IsTrue(c.Segments.Any(s => s.Kind == TempoKind.Ramp), name);
                Assert.AreEqual(100, c.Segments[0].Bpm0, 1e-9);
            }
        }

        [Test]
        public void MediumRampsUpAndDownWithinTwentyFivePercent()
        {
            foreach (var name in Names(40))
            {
                var c = TempoCurve.Generate(name, HymnLevel.Medium, 64, 100);
                bool up = false, down = false;
                foreach (var s in c.Segments)
                {
                    Assert.That(s.Kind, Is.EqualTo(TempoKind.Steady).Or.EqualTo(TempoKind.Ramp), name);
                    foreach (double bpm in new[] { s.Bpm0, s.Bpm1 }) Assert.That(bpm, Is.InRange(75 - 1e-9, 125 + 1e-9), name);
                    if (s.Kind == TempoKind.Ramp) { if (s.Bpm1 > s.Bpm0 + 1) up = true; if (s.Bpm1 < s.Bpm0 - 1) down = true; }
                }
                Assert.IsTrue(up && down, name);
            }
        }

        [Test]
        public void HardJumpsWithinThirtyFivePercentAndHasOneOrTwoFermatas()
        {
            var counts = new HashSet<int>();
            foreach (var name in Names(60))
            {
                var c = TempoCurve.Generate(name, HymnLevel.Hard, 64, 100);
                int fermatas = c.Segments.Count(s => s.Kind == TempoKind.Fermata);
                counts.Add(fermatas);
                Assert.That(fermatas, Is.InRange(1, 2), name);
                Assert.IsTrue(c.Segments.Any(s => s.Kind == TempoKind.Jump), name);
                double biggest = 0;
                for (int i = 0; i < c.Segments.Count; i++)
                {
                    var s = c.Segments[i];
                    Assert.AreNotEqual(TempoKind.Ramp, s.Kind, name);
                    Assert.That(s.Bpm0, Is.InRange(65 - 1e-9, 135 + 1e-9), name);
                    if (s.Kind == TempoKind.Fermata) { Assert.AreEqual(1, s.Beats); Assert.That(s.Stretch, Is.InRange(1.8, 2.6)); }
                    if (i > 0 && s.Kind != TempoKind.Fermata && c.Segments[i - 1].Kind != TempoKind.Fermata)
                        biggest = Math.Max(biggest, Math.Abs(s.Bpm0 - c.Segments[i - 1].Bpm1) / 100);
                }
                Assert.GreaterOrEqual(biggest, 0.12, "a real jump, " + name);
                Assert.LessOrEqual(biggest, 0.70 + 1e-9, name);
            }
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, counts);
        }

        [Test]
        public void FermatasLandOnTheGivenLineEnds()
        {
            var ends = new[] { 15, 31, 47, 63 };
            foreach (var name in Names(30))
            {
                var c = TempoCurve.Generate(name, HymnLevel.Hard, 64, 100, ends);
                double at = 0;
                foreach (var s in c.Segments)
                {
                    if (s.Kind == TempoKind.Fermata) CollectionAssert.Contains(ends, (int)at, name);
                    at += s.Beats;
                }
            }
            // without line data they are still on whole beats, away from the first four
            var c2 = TempoCurve.Generate("hymn_007", HymnLevel.Hard, 40, 100);
            double pos = 0;
            foreach (var s in c2.Segments) { if (s.Kind == TempoKind.Fermata) Assert.GreaterOrEqual(pos, 4); pos += s.Beats; }
        }

        [Test]
        public void ATinyExcerptStillGetsACurve()
        {
            foreach (HymnLevel lv in Enum.GetValues(typeof(HymnLevel)))
                foreach (int n in new[] { 1, 2, 3, 5 })
                {
                    var c = TempoCurve.Generate("x", lv, n, 90);
                    Assert.AreEqual(n, c.BeatCount);
                    Assert.AreEqual(n, c.Segments.Sum(s => s.Beats), 1e-9);
                }
        }

        // What the Mac renderer does with a dump, written from the dump text alone (no TempoCurve): the
        // seg lines are the exact definition, so a note at beat position x lands where the game puts it.
        static double RenderedTime(string dump, double x)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            double beat0 = 0, sec0 = 0;
            foreach (var line in dump.Split('\n').Where(l => l.StartsWith("seg ")))
            {
                var f = line.Split(' ');
                double len = double.Parse(f[2], inv);
                double a = double.Parse(f[3], inv);
                double b = f[1] == "ramp" ? double.Parse(f[4], inv) : a;
                double stretch = f[1] == "fermata" ? double.Parse(f[4], inv) : 1;
                double SecondsTo(double u) => f[1] == "ramp" && Math.Abs(b - a) > 1e-12
                    ? 60.0 * len / (b - a) * Math.Log((a + (b - a) * u / len) / a)
                    : u * 60.0 * stretch / a;
                if (x < beat0 + len) return sec0 + SecondsTo(x - beat0);
                beat0 += len; sec0 += SecondsTo(len);
            }
            return sec0;   // past the end: only the end itself is asked for
        }

        [Test]
        public void TheSegLinesAloneGiveTheRendererTheExactMap()
        {
            foreach (HymnLevel lv in Enum.GetValues(typeof(HymnLevel)))
                foreach (var name in Names(20))
                {
                    var c = TempoCurve.Generate(name, lv, 64, 60);
                    string dump = c.ToText();
                    for (double x = 0; x < 64; x += 0.0625)
                        Assert.AreEqual(c.TimeAt(x), RenderedTime(dump, x), 1e-9, name + " " + lv + " beat " + x);
                }
        }

        // ---- the dump ----------------------------------------------------------

        [Test]
        public void TheDumpIsOneLinePerBeatAndRoundTrips()
        {
            foreach (HymnLevel lv in Enum.GetValues(typeof(HymnLevel)))
                foreach (var name in Names(12))
                {
                    var c = TempoCurve.Generate(name, lv, 64, 93.5);
                    c.StartBar = 5; c.EndBar = 20;
                    string text = c.ToText();
                    Assert.AreEqual(65, text.Split('\n').Count(l => l.StartsWith("beat ")));
                    TempoCurve back;
                    string err;
                    Assert.IsTrue(TempoCurve.TryParse(text, out back, out err), err);
                    Assert.AreEqual(text, back.ToText());
                    Assert.AreEqual(name, back.Hymn);
                    Assert.AreEqual(lv, back.Level);
                    Assert.AreEqual(c.Seed, back.Seed);
                    Assert.AreEqual(93.5, back.BaseBpm, 1e-12);
                    Assert.AreEqual(5, back.StartBar);
                    Assert.AreEqual(20, back.EndBar);
                    Assert.AreEqual(c.Segments.Count, back.Segments.Count);
                    for (int i = 0; i <= c.BeatCount; i++) Assert.AreEqual(c.Times[i], back.Times[i], 1e-12);
                    for (double b = 0; b < 64; b += 0.37) Assert.AreEqual(c.TimeAt(b), back.TimeAt(b), 1e-12);
                }
        }

        [Test]
        public void TheBeatLinesCarryTheSecondsTheGameJudgesWith()
        {
            var c = TempoCurve.Generate("hymn_065", HymnLevel.Hard, 32, 96);
            var lines = c.ToText().Split('\n').Where(l => l.StartsWith("beat ")).ToList();
            for (int i = 0; i < lines.Count; i++)
            {
                var f = lines[i].Split(' ');
                Assert.AreEqual(i.ToString(), f[1]);
                Assert.AreEqual(c.Times[i], double.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture), 1e-9);
            }
        }

        [Test]
        public void TheDumpIgnoresCommentsAndBlankLinesAndRejectsNonsense()
        {
            var c = TempoCurve.Generate("hymn_065", HymnLevel.Medium, 24, 96);
            TempoCurve back;
            string err;
            Assert.IsTrue(TempoCurve.TryParse("# hello\n\n" + c.ToText().Replace("\n", "\r\n") + "\n# end\n", out back, out err), err);
            Assert.AreEqual(c.ToText(), back.ToText());

            Assert.IsFalse(TempoCurve.TryParse("", out back, out err));
            Assert.IsFalse(TempoCurve.TryParse("tempo-curve 9\n", out back, out err));
            Assert.IsFalse(TempoCurve.TryParse(c.ToText().Replace("seg ", "sag "), out back, out err));
            Assert.IsFalse(TempoCurve.TryParse(c.ToText().Replace("beats 24", "beats 25"), out back, out err), "beat count must agree with the segments");
            // a beat line that disagrees with the segments is a corrupt dump
            string bad = string.Join("\n", c.ToText().Split('\n').Select(l => l.StartsWith("beat 3 ") ? "beat 3 99.000000000 x" : l));
            Assert.IsFalse(TempoCurve.TryParse(bad, out back, out err));
            StringAssert.Contains("beat 3", err);
            Assert.Throws<FormatException>(() => TempoCurve.Parse("nope"));
        }
    }
}
