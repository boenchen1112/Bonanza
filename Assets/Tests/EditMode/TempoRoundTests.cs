// Pins the Finale Fever tempo round: the three-excerpt selection and cut, the timeline on the curves,
// one tap per beat judged in the shared window against the CURVED beat lengths, EARLY / LATE / ON,
// free extra taps, bias, hidden pulse, hearts per line and SURVIVE, the outcome, the CPU rolls and the
// perfect presses. Pure C#; hymns are built in memory (TestHymns.cs). One optional test reads the real
// files when BONANZA_HYMNS points at a hymns folder (skipped else).
//
// Synthetic hymns here are 72 bpm (a beat is 0.833 s, window 208 ms), 4/4, 24 bars; the fallback cut
// is the last 16 full bars = 64 beats in four lines. A round is 3 x 64 = 192 beats.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Bonanza.Core;

namespace Bonanza.Core.Tests
{
    public class TempoRoundTests
    {
        // ---- helpers -----------------------------------------------------------

        static HymnCandidate Hymn(string name, int num = 4, int den = 4, bool pickup = false, int bars = 24, double bpm = 72, string cols = "- -")
        {
            var midi = HymnMidi.Parse(SyntheticHymn.Build(num, den, bars, pickup, 2, bpm));
            var grid = HymnGrid.Build(midi, 0);
            var row = HymnExcerptTable.Parse(name + " " + grid.Pattern + " " + (pickup ? "yes" : "no") + " " + cols + " yes").Rows.Single();
            return HymnCandidate.From(midi, grid, row);
        }

        static List<HymnCandidate> Pool(int n = 6) => Enumerable.Range(0, n).Select(i => Hymn("h" + i, bpm: 66 + 3 * i)).ToList();

        static TempoRound Started(uint seed = 7, int cpus = 0, double start = 10, List<HymnCandidate> pool = null)
        {
            var r = new TempoRound(pool ?? Pool(), seed, cpus);
            r.Start(start);
            return r;
        }

        // Presses and the clock go in time order: a press is delivered at its own time.
        static void Drive(TempoRound r, IEnumerable<MeterPress> presses, double to, double dt = 0.01)
        {
            var queue = new Queue<MeterPress>(presses.OrderBy(p => p.Time));
            for (double t = r.Slots[0].StartTime - 1; t <= to; t += dt)
            {
                while (queue.Count > 0 && queue.Peek().Time <= t) { var p = queue.Dequeue(); r.Press(p.Action, p.Time); }
                r.Update(t);
            }
        }

        static IEnumerable<MeterPress> AllBeats(TempoRound r, Func<TempoBeat, bool> keep, double shiftMs = 0) =>
            r.AllBeats.Where(keep).Select(b => new MeterPress { Action = "a", Time = b.Time + shiftMs / 1000.0 });

        // ---- the notes on the curve (what the Mac render plays) ---------------------

        [Test]
        public void TheExcerptKeepsItsBeatTicksAndNotes()
        {
            TempoExcerpt x;
            string err;
            Assert.IsTrue(TempoExcerpt.TryCut(Hymn("a"), out x, out err), err);
            Assert.AreEqual(x.Beats.Count + 1, x.BeatTicks.Count);
            for (int i = 1; i < x.BeatTicks.Count; i++) Assert.AreEqual(480, x.BeatTicks[i] - x.BeatTicks[i - 1], "4/4 in four: a quarter per beat (incl. the closing edge)");
            Assert.AreEqual(64, x.Notes.Count, "one synthetic note per quarter inside the excerpt");
            Assert.IsTrue(x.Notes.All(n => n.StartTick >= x.BeatTicks[0] && n.StartTick < x.BeatTicks[x.BeatTicks.Count - 1]));
            Assert.AreEqual(0, x.BeatOfTick(x.BeatTicks[0]), 1e-12);
            Assert.AreEqual(5.5, x.BeatOfTick(x.BeatTicks[5] + 240), 1e-12, "half way through a beat");
            Assert.AreEqual(64, x.BeatOfTick(x.BeatTicks[64]), 1e-12);
        }

        [Test]
        public void NotesLandOnTheCurveFromItsSegmentsNotFromItsBeatLines()
        {
            TempoExcerpt x;
            string err;
            Assert.IsTrue(TempoExcerpt.TryCut(Hymn("a"), out x, out err), err);
            foreach (HymnLevel level in Enum.GetValues(typeof(HymnLevel)))
            {
                var curve = x.CurveFor(level);
                var score = x.Score(curve);
                Assert.AreEqual(x.Notes.Count, score.Count);
                foreach (var n in score)
                {
                    Assert.AreEqual(curve.TimeAt(n.StartBeat), n.Start, 1e-12);
                    Assert.AreEqual(curve.TimeAt(n.EndBeat), n.End, 1e-12);
                    Assert.AreEqual(n.StartBeat, Math.Round(n.StartBeat), 1e-9, "the synthetic notes sit on beats");
                    Assert.AreEqual(curve.Times[(int)Math.Round(n.StartBeat)], n.Start, 1e-9, "on a beat the segment time is the beat line");
                    Assert.Greater(n.End, n.Start);
                }
            }
        }

        // ---- the cut ------------------------------------------------------------

        [Test]
        public void TheFallbackCutIsTheLastSixteenFullBarsInFourLines()
        {
            TempoExcerpt x;
            string err;
            Assert.IsTrue(TempoExcerpt.TryCut(Hymn("a"), out x, out err), err);
            Assert.AreEqual(9, x.StartBar);
            Assert.AreEqual(24, x.EndBar);
            Assert.AreEqual(64, x.Beats.Count);
            CollectionAssert.AreEqual(new[] { 0, 16, 32, 48 }, x.LineStarts);
            CollectionAssert.AreEqual(new[] { 15, 31, 47, 63 }, x.LineEnds);
            Assert.AreEqual(72, x.BaseBpm, 0.01);
            Assert.AreEqual(0, x.Beats[0].BarPosition);
            Assert.AreEqual(4, x.Beats[0].BeatsPerBar);
        }

        [Test]
        public void TableLinesGiveTheCutAndTheLines()
        {
            TempoExcerpt x;
            string err;
            Assert.IsTrue(TempoExcerpt.TryCut(Hymn("a", cols: "6,12,18 22"), out x, out err), err);
            Assert.AreEqual(6, x.StartBar);
            Assert.AreEqual(22, x.EndBar);
            Assert.AreEqual(17 * 4, x.Beats.Count);
            CollectionAssert.AreEqual(new[] { 0, 24, 48 }, x.LineStarts);   // lines start at bars 6, 12, 18
            CollectionAssert.AreEqual(new[] { 23, 47, 67 }, x.LineEnds);
        }

        [Test]
        public void AShortHymnStartsAtItsFirstFullBarAndPickupsAreLeftOut()
        {
            TempoExcerpt x;
            string err;
            Assert.IsTrue(TempoExcerpt.TryCut(Hymn("s", bars: 10), out x, out err), err);
            Assert.AreEqual(1, x.StartBar);
            Assert.AreEqual(10, x.EndBar);
            CollectionAssert.AreEqual(new[] { 0, 16, 32 }, x.LineStarts);
            Assert.IsTrue(TempoExcerpt.TryCut(Hymn("p", pickup: true), out x, out err), err);
            Assert.AreEqual(9, x.StartBar);
            Assert.AreEqual(0, x.Beats[0].BarPosition);
        }

        [Test]
        public void ThreeFourAndSixEightCutByTheirConductedBeats()
        {
            TempoExcerpt x;
            string err;
            Assert.IsTrue(TempoExcerpt.TryCut(Hymn("w", 3, 4), out x, out err), err);
            Assert.AreEqual(48, x.Beats.Count);
            Assert.AreEqual(3, x.Beats[0].BeatsPerBar);
            Assert.IsTrue(TempoExcerpt.TryCut(Hymn("j", 6, 8), out x, out err), err);
            Assert.AreEqual(32, x.Beats.Count);   // two conducted beats per bar
        }

        [Test]
        public void ACandidateWithoutDataCannotBeCut()
        {
            TempoExcerpt x;
            string err;
            Assert.IsFalse(TempoExcerpt.TryCut(new HymnCandidate { Name = "none", Pattern = 4 }, out x, out err));
            StringAssert.Contains("none", err);
        }

        [Test]
        public void TheCurveOfAHymnAtALevelIsTheGeneratorsWithItsLineEnds()
        {
            var h = Hymn("hymn_065", bpm: 96);
            TempoExcerpt x;
            string err;
            Assert.IsTrue(TempoExcerpt.TryCut(h, out x, out err), err);
            foreach (HymnLevel lv in Enum.GetValues(typeof(HymnLevel)))
            {
                var c = x.CurveFor(lv);
                Assert.AreEqual(TempoCurve.Generate("hymn_065", lv, 64, x.BaseBpm, x.LineEnds).ToText(), c.ToText().Replace("bars 9 24\n", ""));
                Assert.AreEqual(9, c.StartBar);
                Assert.AreEqual(24, c.EndBar);
                Assert.AreEqual(64, c.BeatCount);
            }
        }

        // ---- the selection ------------------------------------------------------

        [Test]
        public void ThreeDifferentHymnsEasyMediumHardAndSeedsVary()
        {
            CollectionAssert.AreEqual(new[] { HymnLevel.Easy, HymnLevel.Medium, HymnLevel.Hard }, TempoRound.SlotLevels);
            var pool = Pool(8);
            for (uint s = 0; s < 30; s++)
            {
                var pick = TempoPlaylist.Select(pool, s);
                Assert.AreEqual(3, pick.Count);
                Assert.AreEqual(3, pick.Distinct().Count());
                CollectionAssert.AreEqual(pick, TempoPlaylist.Select(pool, s));
            }
            Assert.Greater(Enumerable.Range(0, 30).Select(s => string.Join(",", TempoPlaylist.Select(pool, (uint)s))).Distinct().Count(), 10);
        }

        [Test]
        public void ASmallPoolRepeatsAndAnEmptyOneThrows()
        {
            Assert.AreEqual(3, TempoPlaylist.Select(Pool(2), 1).Count);
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, TempoPlaylist.Select(Pool(1), 1));
            Assert.Throws<ArgumentException>(() => TempoPlaylist.Select(new List<HymnCandidate>(), 1));
            Assert.Throws<ArgumentException>(() => new TempoRound(new List<HymnCandidate> { new HymnCandidate { Name = "x", Pattern = 4 } }, 1, 0));
        }

        [Test]
        public void HymnsThatCannotBeCutAreLeftOutOfTheDraw()
        {
            var pool = Pool(4);
            pool.Insert(0, new HymnCandidate { Name = "nodata", Pattern = 4 });
            for (uint s = 0; s < 20; s++)
                Assert.IsFalse(new TempoRound(pool, s, 0).Playlist.Contains("nodata"));
        }

        // ---- the timeline ---------------------------------------------------------

        [Test]
        public void ThreeExcerptsOnTheirCurvesBackToBackWithATwoBeatGap()
        {
            var r = Started(5, start: 10);
            Assert.AreEqual(3, r.Slots.Count);
            Assert.AreEqual(10, r.Slots[0].StartTime, 1e-12);
            for (int k = 0; k < 3; k++)
            {
                var s = r.Slots[k];
                Assert.AreEqual(HymnLevel.Easy + k, s.Level);
                Assert.AreEqual(64, s.Beats.Count);
                var curve = s.Curve;
                for (int i = 0; i < 64; i++)
                {
                    Assert.AreEqual(s.StartTime + curve.Times[i], s.Beats[i].Time, 1e-9);
                    Assert.AreEqual(curve.LengthOf(i), s.Beats[i].Length, 1e-9);
                    Assert.AreEqual(k, s.Beats[i].Slot);
                    Assert.AreEqual(i, s.Beats[i].Index);
                    Assert.AreEqual(i % 4, s.Beats[i].BarPosition);
                }
                Assert.AreEqual(s.StartTime + curve.Duration, s.EndTime, 1e-9);
                if (k > 0) Assert.AreEqual(r.Slots[k - 1].EndTime + TempoRound.GapBeats * s.Curve.LengthOf(0), s.StartTime, 1e-9);
            }
            Assert.AreEqual(r.Slots[2].EndTime, r.EndTime, 1e-12);
            Assert.AreEqual(192, r.AllBeats.Count);
        }

        [Test]
        public void EachSlotPlaysTheFixedCurveOfItsHymnAndLevel()
        {
            var r = Started(11);
            for (int k = 0; k < 3; k++)
            {
                var s = r.Slots[k];
                var again = TempoCurve.Generate(s.Name, s.Level, 64, s.Excerpt.BaseBpm, s.Excerpt.LineEnds);
                Assert.AreEqual(again.Times.Count, s.Curve.Times.Count);
                for (int i = 0; i < again.Times.Count; i++) Assert.AreEqual(again.Times[i], s.Curve.Times[i], 1e-12);
                Assert.AreEqual(s.Name + "." + s.Level.ToString().ToLowerInvariant() + ".tempo", s.Clip);
                Assert.AreEqual(s.Clip, TempoRound.ClipName(s.Name, s.Level));
            }
            // the round seed picks the hymns, never the curve
            var other = Started(12, pool: Pool());
            foreach (var s in other.Slots)
            {
                var same = r.Slots.FirstOrDefault(t => t.Name == s.Name && t.Level == s.Level);
                if (same != null) Assert.AreEqual(same.Curve.ToText(), s.Curve.ToText());
            }
        }

        [Test]
        public void TheCurvesCarryTheirKindOfTempoMotion()
        {
            var r = Started(3);
            Assert.IsTrue(r.Slots[0].Curve.Segments.Any(s => s.Kind == TempoKind.Ramp));
            Assert.IsTrue(r.Slots[1].Curve.Segments.Any(s => s.Kind == TempoKind.Ramp));
            Assert.IsTrue(r.Slots[2].Curve.Segments.Any(s => s.Kind == TempoKind.Jump));
            Assert.IsTrue(r.Slots[2].Curve.Segments.Any(s => s.Kind == TempoKind.Fermata));
        }

        // ---- judging -----------------------------------------------------------------

        [Test]
        public void OnTheBeatTapsThroughRampJumpAndFermataAreAllPerfectAndOn()
        {
            var r = Started();
            Drive(r, r.PerfectPresses(), r.EndTime + 2);
            Assert.IsTrue(r.Done);
            var o = r.Result();
            Assert.AreEqual(192, o.Perfect);
            Assert.AreEqual(0, o.Miss + o.Good + o.Great);
            Assert.AreEqual("S", o.Rank);
            Assert.AreEqual(1.0, o.Accuracy, 1e-12);
            Assert.AreEqual(0, o.BiasMs, 1e-12);
            Assert.AreEqual(192, o.MaxCombo);
            Assert.AreEqual(3, r.Hearts);
            Assert.IsFalse(r.Survived);
            Assert.IsTrue(r.AllBeats.All(b => b.Call == TapCall.On));
            Assert.AreEqual(0, r.ExtraTaps);
        }

        [Test]
        public void EarlyLateOnBySignAndThePerfectGrade()
        {
            var r = Started();
            // (beat, offset as a function of that beat's own perfect / good half-widths in ms, call, grade)
            var cases = new (int beat, Func<double, double, double> err, TapCall call, Verdict v)[]
            {
                (5, (p, g) => 0, TapCall.On, Verdict.Perfect),
                (6, (p, g) => p - 1, TapCall.On, Verdict.Perfect),
                (7, (p, g) => -(p - 1), TapCall.On, Verdict.Perfect),
                (8, (p, g) => p + 1, TapCall.Late, Verdict.Great),
                (9, (p, g) => -(p + 1), TapCall.Early, Verdict.Great),
                (10, (p, g) => g - 1, TapCall.Late, Verdict.Good),
                (11, (p, g) => -(g - 1), TapCall.Early, Verdict.Good),
            };
            var seen = new List<TempoBeat>();
            r.Tapped += seen.Add;
            r.Update(r.Slots[0].StartTime);
            foreach (var c in cases)
            {
                var beat = r.Slots[0].Beats[c.beat];
                double err = c.err(BeatWindow.PerfectMs(beat.Length), BeatWindow.GoodMs(beat.Length));
                Assert.IsTrue(r.Press("a", beat.Time + err / 1000), "beat " + c.beat);
                Assert.AreEqual(c.call, beat.Call, "beat " + c.beat);
                Assert.AreEqual(c.v, beat.Verdict, "beat " + c.beat);
                Assert.AreEqual(err, beat.ErrMs, 1e-6);
                r.Update(beat.Time + 0.5 * beat.Length);
            }
            CollectionAssert.AreEqual(cases.Select(c => c.beat), seen.Select(s => s.Index));
            Assert.AreEqual(3, r.On);
            Assert.AreEqual(2, r.Early);
            Assert.AreEqual(2, r.Late);
        }

        [Test]
        public void ThePerfectGradeFollowsTheCurvedBeatNotTheMidiTempo()
        {
            var r = Started(9);
            // the same absolute error is ON on a long (slow) beat and LATE on a short (fast) one
            var beats = r.AllBeats.Where(b => b.Slot == 2).ToList();
            var slow = beats.OrderByDescending(b => b.Length).First();
            var fast = beats.OrderBy(b => b.Length).First();
            Assert.Greater(BeatWindow.PerfectMs(slow.Length), BeatWindow.PerfectMs(fast.Length) + 1);
            double e = (BeatWindow.PerfectMs(slow.Length) + BeatWindow.PerfectMs(fast.Length)) / 2;
            r.Update(Math.Min(slow.Time, fast.Time) - 1);
            foreach (var b in new[] { slow, fast }.OrderBy(x => x.Time))
                Assert.IsTrue(r.Press("a", b.Time + e / 1000));
            Assert.AreEqual(TapCall.On, slow.Call);
            Assert.AreEqual(TapCall.Late, fast.Call);
        }

        [Test]
        public void TheClaimWindowIsTheBeatsOwnFractionSoAFermataBeatReachesFurther()
        {
            var r = Started(9);
            var beats = r.AllBeats.Where(b => b.Slot == 2).ToList();
            var slow = beats.OrderByDescending(b => b.Length).First();
            var fast = beats.OrderBy(b => b.Length).First();
            double gs = BeatWindow.GoodMs(slow.Length), gf = BeatWindow.GoodMs(fast.Length);
            Assert.Greater(gs, gf + 10);
            double e = (gs + gf) / 2;
            Assert.IsFalse(r.Press("a", fast.Time - e / 1000), "outside the fast beat's window");
            Assert.AreEqual(1, r.ExtraTaps);
            Assert.IsTrue(r.Press("a", slow.Time - e / 1000), "inside the slow beat's window");
            Assert.AreEqual(TapCall.Early, slow.Call);
            Assert.IsFalse(fast.Judged);
        }

        [Test]
        public void ExtraTapsClaimNothingAndCostNothing()
        {
            var r = Started();
            r.Update(r.Slots[0].StartTime);
            var b0 = r.Slots[0].Beats[0];
            var b1 = r.Slots[0].Beats[1];
            Assert.IsTrue(r.Press("a", b0.Time));
            Assert.IsFalse(r.Press("a", b0.Time + 0.01), "a second tap on a judged beat is extra");
            Assert.IsFalse(r.Press("a", (b0.Time + b1.Time) / 2), "between two beats");
            Assert.AreEqual(2, r.ExtraTaps);
            Assert.IsFalse(r.Press("left", b1.Time), "only a is a tap");
            Assert.IsFalse(r.Press("b", b1.Time));
            Assert.AreEqual(2, r.ExtraTaps);
            Assert.IsFalse(b1.Judged);
            // all the rest played perfectly, extras and all: still S
            var rest = r.AllBeats.Skip(1).Select(b => new MeterPress { Action = "a", Time = b.Time });
            Drive2(r, rest);
            Assert.AreEqual("S", r.Result().Rank);
            Assert.AreEqual(1.0, r.Result().Accuracy, 1e-12);
            Assert.AreEqual(0, r.Result().Miss);
        }

        // continue a round already in flight
        static void Drive2(TempoRound r, IEnumerable<MeterPress> presses)
        {
            var queue = new Queue<MeterPress>(presses.OrderBy(p => p.Time));
            for (double t = r.Slots[0].StartTime; t <= r.EndTime + 2; t += 0.01)
            {
                while (queue.Count > 0 && queue.Peek().Time <= t) { var p = queue.Dequeue(); r.Press(p.Action, p.Time); }
                r.Update(t);
            }
        }

        [Test]
        public void MashingEveryFewHundredthsOfASecondIsNotAGoodStrategy()
        {
            var r = Started(4);
            var mash = new List<MeterPress>();
            for (double t = r.Slots[0].StartTime - 0.3; t < r.EndTime; t += 0.07) mash.Add(new MeterPress { Action = "a", Time = t });
            Drive(r, mash, r.EndTime + 2);
            Assert.IsTrue(r.Done);
            Assert.Less(r.Result().Accuracy, 0.5, "the first tap in a window claims the beat, early");
            Assert.AreNotEqual("S", r.Result().Rank);
            Assert.Greater(r.ExtraTaps, 500);
        }

        [Test]
        public void NothingBeforeTheStartOrAfterTheEndCounts()
        {
            var r = new TempoRound(Pool(), 1, 0);
            Assert.IsFalse(r.Press("a", 1));
            Assert.AreEqual(0, r.ExtraTaps);
            Assert.IsNull(r.Result());
            r.Start(10);
            Drive(r, r.PerfectPresses(), r.EndTime + 2);
            Assert.IsFalse(r.Press("a", r.EndTime + 3));
            Assert.AreEqual(0, r.ExtraTaps);
        }

        [Test]
        public void MissedBeatsAreMissesAndTheRoundStillEnds()
        {
            var r = Started();
            Drive(r, new MeterPress[0], r.EndTime + 2);
            Assert.IsTrue(r.Done);
            var o = r.Result();
            Assert.AreEqual(192, o.Miss);
            Assert.AreEqual("D", o.Rank);
            Assert.AreEqual(0, o.Accuracy, 1e-12);
            Assert.AreEqual(0, o.BiasMs);
            Assert.IsTrue(r.Survived);
            Assert.AreEqual(0, r.Hearts);
            Assert.IsTrue(r.AllBeats.All(b => b.Judged && !b.Hit));
        }

        [Test]
        public void SkippingEveryOtherBeatMissesHalfOfThem()
        {
            var r = Started();
            Drive(r, AllBeats(r, b => b.Index % 2 == 0), r.EndTime + 2);
            var o = r.Result();
            Assert.AreEqual(96, o.Miss);
            Assert.AreEqual(96, o.Perfect);
            Assert.AreEqual(0.5, o.Accuracy, 1e-12);
        }

        [Test]
        public void BiasIsTheMeanSignedErrorOverTheHitsOnly()
        {
            var late = Started();
            Drive(late, AllBeats(late, b => true, 20), late.EndTime + 2);
            Assert.AreEqual(20, late.Result().BiasMs, 0.06);
            Assert.AreEqual(20, late.BiasMs, 1e-6);
            Assert.IsTrue(late.Result().Extras.Any(e => e.Key == "BIAS" && e.Value == "LATE 20 ms"));

            var early = Started();
            Drive(early, AllBeats(early, b => true, -35), early.EndTime + 2);
            Assert.AreEqual(-35, early.Result().BiasMs, 0.06);
            Assert.IsTrue(early.Result().Extras.Any(e => e.Key == "BIAS" && e.Value == "EARLY 35 ms"));

            // missed beats do not pull the mean towards zero; half early, half late cancels out
            var mixed = Started();
            Drive(mixed, AllBeats(mixed, b => b.Index % 4 != 3, 0).Select((p, i) => new MeterPress { Action = "a", Time = p.Time + (i % 2 == 0 ? 0.03 : -0.03) }), mixed.EndTime + 2);
            Assert.AreEqual(0, mixed.Result().BiasMs, 0.1);
            Assert.AreEqual(48, mixed.Result().Miss);

            var onTime = Started();
            Drive(onTime, onTime.PerfectPresses(), onTime.EndTime + 2);
            Assert.IsTrue(onTime.Result().Extras.Any(e => e.Key == "BIAS" && e.Value == "ON 0 ms"));
        }

        [Test]
        public void TheResultCarriesTheTempoStatsInOrder()
        {
            var r = Started();
            var presses = AllBeats(r, b => !(b.Slot == 0 && b.Index == 3), 0).Concat(AllBeats(r, b => b.Slot == 0 && b.Index == 40, 60));
            Drive(r, presses.Concat(new[] { new MeterPress { Action = "a", Time = r.Slots[0].Beats[1].Time + 0.4 } }), r.EndTime + 2);
            var o = r.Result();
            var keys = o.Extras.Select(e => e.Key).ToList();
            CollectionAssert.AreEqual(new[] { "TIER", "HEARTS", "BIAS", "ON", "EARLY", "LATE", "MISSED", "EXTRA TAPS" }, keys);
            Assert.AreEqual("1", o.Extras.Single(e => e.Key == "MISSED").Value);
            Assert.AreEqual("2", o.Extras.Single(e => e.Key == "EXTRA TAPS").Value);
            Assert.AreEqual("191", o.Extras.Single(e => e.Key == "ON").Value);
            Assert.IsTrue(o.LogLine().Contains("bias=") && o.LogLine().Contains("extra_taps="));
        }

        [Test]
        public void EventsAnnounceTapsMissesExtrasLinesAndHearts()
        {
            var r = Started();
            var taps = new List<TempoBeat>();
            var misses = new List<TempoBeat>();
            var lines = new List<TempoLine>();
            int extras = 0;
            r.Tapped += taps.Add;
            r.Missed += misses.Add;
            r.LineClosed += lines.Add;
            r.ExtraTap += t => extras++;
            Drive(r, AllBeats(r, b => !(b.Slot == 0 && b.Index == 3)).Concat(new[] { new MeterPress { Action = "a", Time = r.Slots[0].StartTime + 0.2 } }), r.EndTime + 2);
            Assert.AreEqual(191, taps.Count);
            Assert.AreEqual(1, misses.Count);
            Assert.AreEqual(3, misses[0].Index);
            Assert.AreEqual(12, lines.Count);
            Assert.AreEqual(1, extras);
            Assert.IsTrue(lines.Select(l => l.Slot.Index).SequenceEqual(new[] { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2 }));
        }

        // ---- hidden pulse ------------------------------------------------------------

        [Test]
        public void HiddenStretchesGrowWithTheLevelAndAreDeterministic()
        {
            var counts = new int[3];
            var longest = new int[3];
            foreach (var name in Enumerable.Range(0, 30).Select(i => "hymn_" + i))
                foreach (HymnLevel lv in Enum.GetValues(typeof(HymnLevel)))
                {
                    TempoExcerpt x;
                    string err;
                    Assert.IsTrue(TempoExcerpt.TryCut(Hymn(name), out x, out err), err);
                    var a = HiddenPulse.Plan(name, lv, x.Beats);
                    CollectionAssert.AreEqual(a, HiddenPulse.Plan(name, lv, x.Beats));
                    var stretches = HiddenPulse.Stretches(a);
                    Assert.GreaterOrEqual(stretches.Count, 1, name + " " + lv);
                    counts[(int)lv] += a.Count(h => h);
                    foreach (var s in stretches)
                    {
                        Assert.GreaterOrEqual(s.First, TempoRound.HiddenLeadBeats, "the player locks on first");
                        Assert.Less(s.Last, 64 - 4 + 0, "the baton is back for the last beats");
                        Assert.AreEqual(0, x.Beats[s.First].BarPosition, "starts on a bar line");
                        Assert.AreEqual(0, (s.Last + 1) % 4, "ends on a bar line");
                        longest[(int)lv] = Math.Max(longest[(int)lv], s.Last - s.First + 1);
                    }
                    for (int i = 1; i < stretches.Count; i++) Assert.GreaterOrEqual(stretches[i].First - stretches[i - 1].Last - 1, 4, "visible beats between stretches");
                }
            Assert.Less(counts[0], counts[1]);
            Assert.Less(counts[1], counts[2]);
            Assert.LessOrEqual(longest[0], 8);       // easy: short
            Assert.GreaterOrEqual(longest[2], 12);   // hard: long
        }

        [Test]
        public void TheFlagIsOnEachBeatOfTheRound()
        {
            var r = Started(2);
            for (int k = 0; k < 3; k++)
            {
                var plan = HiddenPulse.Plan(r.Slots[k].Name, r.Slots[k].Level, r.Slots[k].Excerpt.Beats);
                CollectionAssert.AreEqual(plan, r.Slots[k].Beats.Select(b => b.Hidden));
                CollectionAssert.AreEqual(HiddenPulse.Stretches(plan).Select(s => s.First), r.Slots[k].HiddenStretches.Select(s => s.First));
            }
            Assert.IsTrue(r.AllBeats.Any(b => b.Hidden));
            Assert.IsTrue(r.AllBeats.Any(b => !b.Hidden));
        }

        [Test]
        public void HiddenBeatsAreJudgedLikeAnyOther()
        {
            var r = Started(2);
            Drive(r, r.PerfectPresses(), r.EndTime + 2);
            Assert.AreEqual(192, r.Result().Perfect);
        }

        // ---- hearts --------------------------------------------------------------------

        static bool InLine(TempoBeat b, int slot, int line) => b.Slot == slot && b.Line == line;

        [Test]
        public void ALineWithTooManyMissesCostsAHeart()
        {
            // a line of 16 beats loses a heart at 4 misses (a quarter), not at 3
            Assert.AreEqual(4, TempoRound.LoseMisses(16));
            Assert.AreEqual(3, TempoRound.LoseMisses(12));
            Assert.AreEqual(2, TempoRound.LoseMisses(4));
            Assert.AreEqual(2, TempoRound.LoseMisses(1));

            var three = Started();
            Drive(three, AllBeats(three, b => !(InLine(b, 0, 1) && b.Index % 5 == 0 && b.Index < 31)), three.EndTime + 2);   // beats 20,25,30: 3 misses
            Assert.AreEqual(3, three.Result().Miss);
            Assert.AreEqual(3, three.Hearts);

            var four = Started();
            var lost = new List<int>();
            four.HeartLost += lost.Add;
            Drive(four, AllBeats(four, b => !(InLine(b, 0, 1) && b.Index % 4 == 0)), four.EndTime + 2);   // 16,20,24,28
            Assert.AreEqual(4, four.Result().Miss);
            CollectionAssert.AreEqual(new[] { 2 }, lost);
            Assert.AreEqual(3, four.Hearts, "the clean lines after it buy it back");
            Assert.IsFalse(four.Survived);
        }

        [Test]
        public void ACleanLineBuysAHeartBackAndAGoodOneDoesNot()
        {
            var r = Started();
            var log = new List<string>();
            r.HeartLost += h => log.Add("-" + h);
            r.HeartGained += h => log.Add("+" + h);
            var presses = AllBeats(r, b =>
                !(InLine(b, 0, 0) && b.Index < 4)                      // line 0: four misses, -1
                && !(InLine(b, 0, 2) && b.Index < 36));                // line 2: 4 misses, -1 again
            // line 1 clean (+1), line 3 has one Good beat in an otherwise clean line (no change)
            var list = presses.Select(p => p).ToList();
            var b3 = r.Slots[0].Beats[50];
            list.RemoveAll(p => Math.Abs(p.Time - b3.Time) < 1e-9);
            list.Add(new MeterPress { Action = "a", Time = b3.Time + BeatWindow.GreatMs(b3.Length) / 1000 + 0.005 });   // a Good
            Drive(r, list, r.EndTime + 2);
            // line 0 loses (2), line 1 clean (3), line 2 loses (2), line 3 neither, then the other slots are clean (3)
            Assert.AreEqual("-2,+3,-2,+3", string.Join(",", log));
            Assert.AreEqual(3, r.Hearts);
        }

        [Test]
        public void LosingAllThreeHeartsIsSurviveAndCapsTheRankAtB()
        {
            var r = Started();
            var log = new List<string>();
            r.HeartLost += h => log.Add("-" + h);
            // the first three lines of the first excerpt each lose 4 beats: everything else perfect
            Drive(r, AllBeats(r, b => !(b.Slot == 0 && b.Line < 3 && b.Index % 16 < 4)), r.EndTime + 2);
            var o = r.Result();
            Assert.AreEqual(12, o.Miss);
            Assert.AreEqual(180.0 / 192, o.Accuracy, 1e-12);   // would be an A
            Assert.AreEqual("-2,-1,-0", string.Join(",", log).Replace("-0", "-0"));
            Assert.IsTrue(r.Survived);
            Assert.AreEqual("B", o.Rank);
            Assert.AreEqual("SURVIVOR", o.Extras.Single(e => e.Key == "TIER").Value);
            Assert.AreEqual(3, r.Hearts, "clean lines afterwards refill the hearts, the cap stays");
        }

        [Test]
        public void SurvivingDoesNotLiftAWorseRank()
        {
            var r = Started();
            Drive(r, AllBeats(r, b => b.Index % 3 == 0), r.EndTime + 2);
            Assert.AreEqual("D", r.Result().Rank);
        }

        [Test]
        public void AFlawlessRoundIsAMetronomeAndAnyMissIsSteady()
        {
            var r = Started();
            Drive(r, r.PerfectPresses(), r.EndTime + 2);
            Assert.AreEqual("METRONOME", r.Result().Extras.Single(e => e.Key == "TIER").Value);
            var s = Started();
            Drive(s, AllBeats(s, b => b.Index != 7), s.EndTime + 2);
            Assert.AreEqual("STEADY", s.Result().Extras.Single(e => e.Key == "TIER").Value);
        }

        // ---- the perfect presses -----------------------------------------------------------

        [Test]
        public void PerfectPressesAreOneAPerBeatAtTheCurvedTimesAndRankS()
        {
            var r = Started(21);
            var p = r.PerfectPresses();
            Assert.AreEqual(192, p.Count);
            Assert.IsTrue(p.All(x => x.Action == "a"));
            for (int i = 0; i < p.Count; i++) Assert.AreEqual(r.AllBeats[i].Time, p[i].Time, 1e-12);
            Assert.AreEqual(64, r.PerfectPresses(1).Count);
            Assert.AreEqual(r.Slots[1].Beats[0].Time, r.PerfectPresses(1)[0].Time, 1e-12);
            Drive(r, p, r.EndTime + 2);
            Assert.AreEqual("S", r.Result().Rank);
            Assert.IsNull(Started(21).Result());
        }

        [Test]
        public void EverySeedPlaysThroughToAnSWithItsOwnPerfectPresses()
        {
            for (uint seed = 0; seed < 25; seed++)
            {
                var r = Started(seed, pool: Pool(8));
                Drive(r, r.PerfectPresses(), r.EndTime + 2, 0.02);
                Assert.IsTrue(r.Done, "seed " + seed);
                Assert.AreEqual("S", r.Result().Rank, "seed " + seed);
            }
        }

        [Test]
        public void TheCurrentSlotAndExcerptEventsFollowTheClock()
        {
            var r = Started();
            var started = new List<int>();
            r.ExcerptStarted += s => started.Add(s.Index);
            int done = 0;
            r.RoundDone += () => done++;
            Assert.AreEqual(-1, r.Current);
            r.Update(r.Slots[0].StartTime - 0.5);
            Assert.AreEqual(-1, r.Current);
            r.Update(r.Slots[0].StartTime);
            Assert.AreEqual(0, r.Current);
            r.Update(r.Slots[1].StartTime + 0.1);
            Assert.AreEqual(1, r.Current);
            r.Update(r.Slots[2].StartTime + 0.1);
            Assert.AreEqual(2, r.Current);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, started);
            Assert.IsFalse(r.Done);
            r.Update(r.EndTime + 1);
            Assert.IsTrue(r.Done);
            Assert.AreEqual(1, done);
            r.Update(r.EndTime + 5);
            Assert.AreEqual(1, done);
        }

        // ---- the CPUs ---------------------------------------------------------------------------

        [Test]
        public void CpuRollsAreSeededAndDoNotDependOnTheOtherCpus()
        {
            var a = new TempoRound(Pool(), 5, new[] { 0.4, 0.86 });
            var b = new TempoRound(Pool(), 5, new[] { 0.4, 0.86 });
            var solo = new TempoRound(Pool(), 5, new[] { 0.4 });
            foreach (var r in new[] { a, b, solo }) { r.Start(0); r.Update(r.EndTime); }
            for (int k = 0; k < 3; k++)
            {
                Assert.AreEqual(2, a.Slots[k].CpuRolls.Length);
                for (int c = 0; c < 2; c++) CollectionAssert.AreEqual(a.Slots[k].CpuRolls[c].ErrMs, b.Slots[k].CpuRolls[c].ErrMs);
                CollectionAssert.AreEqual(a.Slots[k].CpuRolls[0].ErrMs, solo.Slots[k].CpuRolls[0].ErrMs);
                CollectionAssert.AreNotEqual(a.Slots[k].CpuRolls[0].ErrMs, a.Slots[k].CpuRolls[1].ErrMs);
                Assert.AreEqual(64, a.Slots[k].CpuRolls[0].Verdicts.Length);
            }
        }

        [Test]
        public void ASkilledCpuScoresMoreThanAWeakOneOnAverage()
        {
            double weak = 0, strong = 0;
            for (uint seed = 0; seed < 20; seed++)
            {
                var r = new TempoRound(Pool(), seed, new[] { Party.CpuTiers[0].skill, Party.CpuTiers[2].skill });
                r.Start(0);
                r.Update(r.EndTime);
                foreach (var s in r.Slots) { weak += s.CpuRolls[0].Points; strong += s.CpuRolls[1].Points; }
            }
            Assert.Greater(strong, weak * 1.2);
            // the strong CPU hits most beats and the weak one misses some, but neither is perfect
            Assert.Less(strong, 20 * 3 * 64 * 1000);
        }

        [Test]
        public void ThePartyFieldRanksTheHumanAgainstTheCpus()
        {
            var r = new TempoRound(Pool(), 3, new[] { 0.63, 0.63 });
            r.Start(0);
            Drive(r, r.PerfectPresses(), r.EndTime + 2);
            var o = r.Result(new[] { 10, 11, 12 });
            Assert.IsNotNull(o.Field);
            string why;
            Assert.IsTrue(Outcome.CheckField(o.Field, out why), why);
            Assert.AreEqual(0, o.Field.Single(f => f.Id == 10).Place, "a perfect human beats the CPUs");
            Assert.IsNull(Started().Result(new[] { 1 }), "not done");

            var lone = new TempoRound(Pool(), 3, 0);
            lone.Start(0);
            Drive(lone, lone.PerfectPresses(), lone.EndTime + 2);
            Assert.AreEqual(1, lone.Result(new[] { 1 }).Field.Count);
            Assert.AreEqual(0, lone.Result(new[] { 1 }).Field[0].Place);
            Assert.IsNull(lone.Result().Field);

            var none = new TempoRound(Pool(), 3, new[] { 0.63 });
            none.Start(0);
            Drive(none, none.PerfectPresses(), none.EndTime + 2);
            Assert.IsNull(none.Result().Field);
            Assert.IsNull(none.Result(new[] { 1 }).Field, "the ids must match the lineup");
        }

        [Test]
        public void ARoundIsReplayableFromTheSameSeed()
        {
            var a = Started(33, cpus: 2);
            var b = Started(33, cpus: 2);
            CollectionAssert.AreEqual(a.Playlist, b.Playlist);
            CollectionAssert.AreEqual(a.AllBeats.Select(x => x.Time), b.AllBeats.Select(x => x.Time));
            CollectionAssert.AreEqual(a.AllBeats.Select(x => x.Hidden), b.AllBeats.Select(x => x.Hidden));
        }

        // ---- real hymns ----------------------------------------------------------------------------

        [Test]
        public void RealHymnsMakeValidTempoRounds()
        {
            string dir = Environment.GetEnvironmentVariable("BONANZA_HYMNS");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                Assert.Ignore("BONANZA_HYMNS not set: real hymn files are private and not part of this repo.");
            string tablePath = Path.Combine(dir, "excerpts.txt");
            if (!File.Exists(tablePath)) Assert.Ignore("no excerpts.txt in BONANZA_HYMNS");

            var table = HymnExcerptTable.Parse(File.ReadAllText(tablePath));
            var pool = new List<HymnCandidate>();
            foreach (var row in table.UsedRows)
            {
                string path = Path.Combine(dir, row.Hymn + ".mid");
                if (!File.Exists(path)) continue;
                string tm = Path.Combine(dir, "tempo", row.Hymn + ".json");
                var midi = HymnMidi.Parse(File.ReadAllBytes(path));
                var map = File.Exists(tm) ? HymnTempoMap.Parse(File.ReadAllText(tm)) : HymnTempoMap.Identity(row.Hymn);
                pool.Add(HymnCandidate.From(midi, map, row));
            }
            Assert.GreaterOrEqual(pool.Count, 6);

            foreach (var c in pool)
            {
                TempoExcerpt x;
                string err;
                Assert.IsTrue(TempoExcerpt.TryCut(c, out x, out err), err);
                foreach (HymnLevel lv in Enum.GetValues(typeof(HymnLevel)))
                {
                    var curve = x.CurveFor(lv);
                    TestContext.Progress.WriteLine("{0} {1}: {2} beats, bars {3}-{4}, base {5:0.#} bpm, {6:0.#} s, {7}", c.Name, lv, x.Beats.Count, x.StartBar, x.EndBar,
                        x.BaseBpm, curve.Duration, string.Join(" ", curve.Segments.Select(s => s.Kind.ToString()[0] + "" + s.Beats)));
                    Assert.AreEqual(x.Beats.Count, curve.BeatCount);
                    TempoCurve back;
                    Assert.IsTrue(TempoCurve.TryParse(curve.ToText(), out back, out err), err);
                    Assert.AreEqual(curve.ToText(), back.ToText());
                    var hidden = HiddenPulse.Plan(c.Name, lv, x.Beats);
                    Assert.GreaterOrEqual(HiddenPulse.Stretches(hidden).Count, 1, c.Name + " " + lv);
                }
            }

            for (uint seed = 0; seed < 10; seed++)
            {
                var r = new TempoRound(pool, seed, 2);
                r.Start(0);
                string why = "seed " + seed + ": " + string.Join(" ", r.Playlist);
                Assert.AreEqual(3, r.Playlist.Distinct().Count(), why);
                Drive(r, r.PerfectPresses(), r.EndTime + 2, 0.02);
                Assert.IsTrue(r.Done, why);
                Assert.AreEqual("S", r.Result().Rank, why);
            }
        }
    }
}
