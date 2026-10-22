// Pins the Swing Kings downbeat round (Core/DownbeatRound): which beat is a ball and which a bomb,
// the free bars, the swing window and the tiers, explode / strike looking / fizzle, the outs, the
// score and rank, the timeline (no count-in, tempo changes), the selection and the CPU rolls.
// Pure C#, no hymn files: hymns are built in memory (TestHymns.cs). One optional test reads the real
// files when BONANZA_HYMNS points at a hymns folder (skipped else).
//
// Synthetic hymns are 120 bpm, so a beat is 0.5 s (the window is the 128 ms floor: perfect 42 ms,
// great 82 ms). The fallback cut (no table lines) is the last 16 full bars of verse 1.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Bonanza.Core;

namespace Bonanza.Core.Tests
{
    public class DownbeatRoundTests
    {
        // ---- builders ----------------------------------------------------------

        static HymnCandidate Hymn(string name, int num, int den, bool pickup, int bars = 20, string lines = "-", string end = "-", double bpm = 120)
        {
            var midi = HymnMidi.Parse(SyntheticHymn.Build(num, den, bars, pickup, 2, bpm));
            var grid = HymnGrid.Build(midi, 0);
            var row = HymnExcerptTable.Parse(name + " " + grid.Pattern + " " + (pickup ? "yes" : "no") + " " + lines + " " + end + " yes").Rows.Single();
            return HymnCandidate.From(midi, grid, row);
        }

        static List<HymnCandidate> Pool() => new List<HymnCandidate>
        {
            Hymn("n44", 4, 4, false), Hymn("n34", 3, 4, false), Hymn("p44", 4, 4, true),
        };

        const double Start = 10;

        static DownbeatRound Round(uint seed = 5, int cpus = 0, List<HymnCandidate> pool = null, double offsetMs = 0)
        {
            var r = new DownbeatRound(pool ?? Pool(), seed, Enumerable.Repeat(0.63, cpus).ToArray(), offsetMs);
            r.Start(Start);
            return r;
        }

        static DownbeatRound Single(HymnCandidate c)
        {
            var r = new DownbeatRound(new List<HymnCandidate> { c }, 1);
            r.Start(Start);
            return r;
        }

        // Feed swings and the clock in order: a swing is delivered at its own time.
        static void Drive(DownbeatRound r, IEnumerable<double> swings, double to, double dt = 0.01, double from = Start - 3)
        {
            var queue = new Queue<double>(swings.OrderBy(s => s));
            for (double t = from; t <= to; t += dt)
            {
                while (queue.Count > 0 && queue.Peek() <= t) r.Swing(queue.Dequeue());
                r.Update(t);
            }
        }

        static void Finish(DownbeatRound r, IEnumerable<double> swings) => Drive(r, swings, r.EndTime + 3);

        static List<DownbeatPitch> ScoredBalls(DownbeatRound r) => r.Pitches.Where(p => p.IsBall && !p.IsFree).ToList();
        static List<DownbeatPitch> ScoredBombs(DownbeatRound r) => r.Pitches.Where(p => p.IsBomb && !p.IsFree).ToList();

        // ---- ball or bomb ----------------------------------------------------------

        [Test]
        public void ABallSitsOnBarPositionZeroAndEveryOtherBeatIsABomb()
        {
            foreach (var hymn in new[] { Hymn("a", 4, 4, false), Hymn("b", 3, 4, false), Hymn("c", 2, 4, false), Hymn("d", 6, 8, false) })
            {
                var r = Single(hymn);
                foreach (var p in r.Pitches)
                {
                    Assert.AreEqual(p.BarPosition == 0, p.IsBall, hymn.Name + " bar " + p.Bar + " pos " + p.BarPosition);
                    Assert.AreEqual(p.IsBall ? DownbeatKind.Ball : DownbeatKind.Bomb, p.Type);
                    Assert.AreEqual(p.IsBall, p.Revealed, "a bomb is hidden until it is swung at or fizzles");
                }
                int bars = r.Slots[0].EndBar - r.Slots[0].StartBar + 1;
                Assert.AreEqual(bars, r.Slots[0].Pitches.Count(p => p.IsBall), hymn.Name + ": one ball a bar");
                Assert.AreEqual(hymn.Pattern, r.Slots[0].Pitches[0].BeatsPerBar);
            }
        }

        [Test]
        public void SixEightInTwoHasABallEveryTwoBeats()
        {
            var r = Single(Hymn("six", 6, 8, false));
            var bars = r.Slots[0].Pitches.Select(p => p.BarPosition).Take(6).ToArray();
            CollectionAssert.AreEqual(new[] { 0, 1, 0, 1, 0, 1 }, bars);
            Assert.AreEqual(2, r.Slots[0].Pattern);
        }

        [Test]
        public void APickupHymnsWeakBeatIsABombAndTheNextBarlineABall()
        {
            // an excerpt that starts on the pickup bar: its one beat sits at the END of the bar (position 3 of 4)
            var r = Single(Hymn("pick", 4, 4, true, 10, "0,2,4", "8"));
            var s = r.Slots[0];
            Assert.IsTrue(s.Pickup);
            Assert.AreEqual(0, s.Pitches[0].Bar);
            Assert.AreEqual(3, s.Pitches[0].BarPosition);
            Assert.IsTrue(s.Pitches[0].IsBomb);
            Assert.AreEqual(1, s.Pitches[1].Bar);
            Assert.AreEqual(0, s.Pitches[1].BarPosition);
            Assert.IsTrue(s.Pitches[1].IsBall);
            // the beat before every ball is a bomb too, so the pickup does not give the downbeat away
            Assert.IsTrue(s.Pitches.Where((p, i) => i > 0 && p.IsBall).All(p => s.Pitches[p.Index - 1].IsBomb));
        }

        // ---- free bars -----------------------------------------------------------

        [Test]
        public void TheFirstTwoBarsOfEveryExcerptAreFree()
        {
            var r = Round();
            foreach (var s in r.Slots)
            {
                foreach (var p in s.Pitches)
                {
                    bool free = p.Bar - s.StartBar < DownbeatRound.FreeBars;
                    Assert.AreEqual(free, p.IsFree, s.Name + " bar " + p.Bar);
                    Assert.AreEqual(!free, p.Scored);
                }
                Assert.IsTrue(s.Pitches.Take(2 * s.Pitches[0].BeatsPerBar).All(p => p.IsFree));
            }
        }

        [Test]
        public void FreeBarsCostAndScoreNothing()
        {
            var r = Single(Hymn("a", 4, 4, false));
            var freeBall = r.Pitches.First(p => p.IsBall && p.IsFree);
            var freeBomb = r.Pitches.First(p => p.IsBomb && p.IsFree);
            int contacts = 0, explodes = 0, judged = 0, whiffs = 0;
            ExplodeEvent boom = default;
            r.Contact += c => contacts++;
            r.Explode += e => { explodes++; boom = e; };
            r.Judged += (v, e) => judged++;
            r.Whiff += w => whiffs++;
            // swing at the free ball and the free bomb, let the other free pitches go by
            Drive(r, new[] { freeBall.Time, freeBomb.Time }, r.Pitches.First(p => !p.IsFree).Time - 0.3);
            Assert.AreEqual(1, contacts);
            Assert.AreEqual(1, explodes);
            Assert.IsTrue(boom.Free);
            Assert.AreEqual(0, boom.Outs);
            Assert.AreEqual(0, judged);
            Assert.AreEqual(0, whiffs, "a free ball let go is not a strike");
            Assert.AreEqual(0, r.Outs);
            Assert.AreEqual(0, r.Points);
            Assert.AreEqual(0, r.Stats.Perfect + r.Stats.Great + r.Stats.Good + r.Stats.Miss);
            Assert.IsTrue(freeBomb.Revealed);
        }

        // ---- swings -----------------------------------------------------------------

        [Test]
        public void TheTimingGradeSetsTheTier()
        {
            var r = Single(Hymn("a", 4, 4, false));
            var balls = ScoredBalls(r);
            var got = new List<ContactEvent>();
            r.Contact += got.Add;
            // perfect (early and late), great, good early (bunt), good late (foul bunt)
            double[] offs = { 0, -0.03, 0.035, 0.06, -0.07, -0.11, 0.11 };
            Drive(r, offs.Select((o, i) => balls[i].Time + o), balls[offs.Length].Time - 0.3);
            Assert.AreEqual(offs.Length, got.Count);
            Assert.AreEqual(TierId.Homer, got[0].Tier.Id);
            Assert.AreEqual(TierId.Homer, got[1].Tier.Id);
            Assert.AreEqual(TierId.Homer, got[2].Tier.Id);
            Assert.AreEqual(Verdict.Perfect, got[0].Verdict);
            Assert.AreEqual(TierId.Liner, got[3].Tier.Id);
            Assert.AreEqual(TierId.Liner, got[4].Tier.Id);
            Assert.AreEqual(Verdict.Great, got[3].Verdict);
            Assert.AreEqual(TierId.Bunt, got[5].Tier.Id);
            Assert.IsFalse(got[5].Foul);
            Assert.AreEqual(TierId.Bunt, got[6].Tier.Id);
            Assert.IsTrue(got[6].Foul, "good and late is a foul");
            Assert.AreEqual(1, got[0].Power, 1e-9);
            Assert.Greater(got[0].Power, got[3].Power);
            Assert.Greater(got[3].Power, got[5].Power);
            Assert.AreEqual(0, got.Count(c => c.Verdict == Verdict.Miss));
            Assert.Greater(got[0].HomerFeet, 0);
            Assert.AreEqual(0, got[3].HomerFeet);
        }

        [Test]
        public void PowerAndGradesScaleWithTheBeat()
        {
            // a slow hymn (60 bpm, 1 s beats): the window is 250 ms and perfect is the same share of it
            Assert.AreEqual(1, DownbeatRound.PowerFor(80, 1.0), 1e-9);              // 80 of 250 ms = 32 of 128 ms: perfect
            Assert.Less(DownbeatRound.PowerFor(80, 0.5), 1);                        // but 80 ms on a 128 ms window is great
            Assert.AreEqual(Verdict.Perfect, BeatWindow.VerdictFor(80, 1.0));
            Assert.AreEqual(Verdict.Great, BeatWindow.VerdictFor(80, 0.5));
            Assert.AreEqual(SwingRules.PowerFromTiming(0.05, 0), DownbeatRound.PowerFor(50, 0.5), 1e-9, "at the floor it is the arcade curve");
            double prev = double.PositiveInfinity;
            for (double ms = 0; ms <= 250; ms += 5)
            {
                double p = DownbeatRound.PowerFor(ms, 1.0);
                Assert.LessOrEqual(p, prev + 1e-12);
                prev = p;
            }
        }

        [Test]
        public void ABombSwungAtExplodesAsAStrikeAndBreaksTheCombo()
        {
            var r = Single(Hymn("a", 4, 4, false));
            var balls = ScoredBalls(r);
            var bomb = ScoredBombs(r).First(b => b.Time > balls[1].Time);
            var booms = new List<ExplodeEvent>();
            var judged = new List<Verdict>();
            r.Explode += booms.Add;
            r.Judged += (v, e) => judged.Add(v);
            Assert.IsFalse(bomb.Revealed);
            Drive(r, new[] { balls[0].Time, balls[1].Time, bomb.Time }, bomb.Time + 0.2);
            Assert.AreEqual(1, booms.Count);
            Assert.AreSame(bomb, booms[0].Pitch);
            Assert.IsFalse(booms[0].Free);
            Assert.AreEqual(1, booms[0].Outs);
            Assert.AreEqual(1, r.Outs);
            Assert.IsTrue(bomb.Revealed);
            Assert.IsTrue(bomb.Done);
            Assert.AreEqual(2, r.Stats.MaxCombo);
            Assert.AreEqual(0, r.Combo, "the explosion breaks the combo");
            Assert.AreEqual(1, r.Stats.Miss);
            CollectionAssert.AreEqual(new[] { Verdict.Perfect, Verdict.Perfect, Verdict.Miss }, judged);
            Assert.AreEqual(2000, r.Points, 1e-9, "a bomb costs no points, only the accuracy and a strike");
        }

        [Test]
        public void ABallLetGoIsAStrikeLooking()
        {
            var r = Single(Hymn("a", 4, 4, false));
            var ball = ScoredBalls(r)[0];
            var whiffs = new List<WhiffEvent>();
            var passed = new List<Pitch>();
            var judged = new List<Verdict>();
            r.Whiff += whiffs.Add;
            r.PitchPassed += passed.Add;
            r.Judged += (v, e) => judged.Add(v);
            Drive(r, new double[0], ball.Time + ball.WindowMs / 1000 - 0.01);
            Assert.AreEqual(0, whiffs.Count, "still inside the window");
            Drive(r, new double[0], ball.Time + 0.6, from: ball.Time + ball.WindowMs / 1000 - 0.01);
            Assert.AreEqual(1, whiffs.Count);
            Assert.IsFalse(whiffs[0].Swung);
            Assert.AreEqual(1, whiffs[0].Outs);
            Assert.AreSame(ball, whiffs[0].Pitch);
            Assert.AreEqual(1, r.Stats.Miss);
            Assert.AreEqual(Verdict.Miss, judged[0]);
            Assert.Contains(ball, passed, "it sails on into the backstop");
        }

        [Test]
        public void ABombLetGoFizzlesAndCostsNothing()
        {
            var r = Single(Hymn("a", 4, 4, false));
            var bomb = ScoredBombs(r)[0];
            var fizzled = new List<DownbeatPitch>();
            int judged = 0, booms = 0, whiffs = 0;
            var passed = new List<Pitch>();
            r.Explode += e => booms++;
            r.Whiff += w => whiffs++;
            r.Fizzle += fizzled.Add;
            r.Judged += (v, e) => judged++;
            r.PitchPassed += passed.Add;
            Drive(r, new double[0], bomb.Time + 0.05);
            Assert.IsFalse(bomb.Revealed, "hidden while it is in reach");
            Drive(r, new double[0], bomb.Time + 0.7, from: bomb.Time + 0.05);
            CollectionAssert.Contains(fizzled, bomb);
            Assert.IsTrue(bomb.Revealed);
            Assert.AreEqual(0, booms, "a bomb let go never explodes");
            Assert.AreEqual(whiffs, judged, "only the balls let go were judged, the fizzles were not");
            Assert.AreEqual(whiffs, r.Outs, "and only they cost an out");
            Assert.AreEqual(whiffs, r.Stats.Miss);
            Assert.Contains(bomb, passed);
        }

        [Test]
        public void ThreeStrikesRetireTheSideAndTheCountStartsOver()
        {
            var r = Single(Hymn("a", 4, 4, false));
            var whiffs = new List<WhiffEvent>();
            r.Whiff += whiffs.Add;
            Finish(r, new double[0]);
            Assert.GreaterOrEqual(whiffs.Count, 4);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 1 }, whiffs.Take(4).Select(w => w.Outs));
            CollectionAssert.AreEqual(new[] { false, false, true, false }, whiffs.Take(4).Select(w => w.SideRetired));
            Assert.IsTrue(r.Over, "a round with no swings still ends");
        }

        [Test]
        public void StrikesFromBombsAndFromLookingShareTheCount()
        {
            var r = Single(Hymn("a", 4, 4, false));
            var balls = ScoredBalls(r);
            var bombs = ScoredBombs(r);
            var outs = new List<int>();
            r.Whiff += w => outs.Add(w.Outs);
            r.Explode += e => outs.Add(e.Outs);
            // bomb, ball let go, bomb: the third is the side retired
            Drive(r, new[] { bombs[0].Time, bombs[3].Time }, balls[1].Time + 0.5);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, outs.Take(3));
            Assert.AreEqual(0, r.Outs);
        }

        [Test]
        public void ASwingBetweenPitchesIsASwingAtNothing()
        {
            var r = Single(Hymn("a", 4, 4, false));
            var p = ScoredBalls(r)[0];
            int nothing = 0;
            r.SwingAtNothing += _ => nothing++;
            Drive(r, new[] { p.Time + 0.25 }, p.Time + 0.3);   // halfway to the next beat: out of both windows
            Assert.AreEqual(1, nothing);
            Assert.AreEqual(1, r.Stats.Miss, "only the ball that was let go");
            Assert.AreEqual(0, r.Bombs);
            Assert.IsFalse(p.Swung);
            Assert.IsFalse(r.Pitches[p.Index + 1].Swung);
        }

        [Test]
        public void ASwingClaimsTheNearestUnclaimedPitchAndEachPitchOnce()
        {
            var r = Single(Hymn("a", 4, 4, false));
            var balls = ScoredBalls(r);
            var b = balls[0];
            var prev = r.Pitches[b.Index - 1];
            var next = r.Pitches[b.Index + 1];
            // 60 ms late of the ball: nearer to it than to the bomb after
            Assert.AreSame(b, r.Swing(b.Time + 0.06));
            // a second swing near the same ball claims nothing (its neighbours are 400+ ms away)
            int nothing = 0;
            r.SwingAtNothing += _ => nothing++;
            Assert.IsNull(r.Swing(b.Time + 0.07));
            Assert.AreEqual(1, nothing);
            Assert.IsFalse(prev.Swung);
            Assert.IsFalse(next.Swung);
            // and a swing just before the beat after claims that bomb
            Assert.AreSame(next, r.Swing(next.Time - 0.03));
        }

        [Test]
        public void PressingBeforeTheLaunchOrAfterTheEndClaimsNothing()
        {
            var r = Single(Hymn("a", 4, 4, false));
            Assert.IsNull(r.Swing(Start - 5));
            Drive(r, new double[0], r.EndTime + 3);
            Assert.IsTrue(r.Over);
            Assert.IsNull(r.Swing(Start + 3));
        }

        [Test]
        public void TheLatencyOffsetIsAddedToSwingTimes()
        {
            var r = Round(5, 0, null, -40);   // a player who swings 40 ms late needs -40
            var b = ScoredBalls(r)[0];
            ContactEvent c = default;
            r.Contact += e => c = e;
            r.Swing(b.Time + 0.04);
            Assert.AreEqual(0, c.ErrMs, 1e-6);
            Assert.AreEqual(Verdict.Perfect, c.Verdict);
        }

        // ---- the windup path (mouse / camera) ---------------------------------------

        [Test]
        public void ADownstrokeSwingsOnlyAfterAWindupAndPowerIsEarnedByTiming()
        {
            var r = Single(Hymn("a", 4, 4, false));
            var b = ScoredBalls(r)[0];
            Assert.IsFalse(r.ReleaseSwing(b.Time), "no windup, no swing");
            Assert.AreEqual(0, r.Stats.Perfect);
            r.BeginWindup(b.Time - 0.8);
            Assert.IsTrue(r.Holding);
            Assert.IsTrue(r.ReleaseSwing(b.Time + 0.02));
            Assert.IsFalse(r.Holding);
            Assert.AreEqual(1, r.Stats.Perfect);
            Assert.AreEqual(1, r.LastPower, 1e-9);
            Assert.AreEqual(1.64, r.LastHoldBeats, 1e-6);
            // the long hold did not change the hit: a held and a tapped perfect are the same swing
            var r2 = Single(Hymn("a", 4, 4, false));
            r2.Press(ScoredBalls(r2)[0].Time + 0.02);
            Assert.AreEqual(r.Points, r2.Points, 1e-9);
        }

        [Test]
        public void ScoredBallsAreNumberedFromOneAndTheRoundHasNoClockBeforeStart()
        {
            var r = Round();
            CollectionAssert.AreEqual(Enumerable.Range(1, ScoredBalls(r).Count), ScoredBalls(r).Select(p => p.Ordinal));
            Assert.IsTrue(r.Pitches.Where(p => p.IsBomb || p.IsFree).All(p => p.Ordinal == 0));
            var fresh = new DownbeatRound(Pool(), 1);
            Assert.AreEqual(0, fresh.BeatAt(5));
            Assert.AreEqual(0, fresh.Pitches.Count);
            Assert.IsNull(fresh.Swing(5), "a swing before Start claims nothing");
        }

        [Test]
        public void TheWindupGaugeFillsOverAFewBeatsAndPowerIgnoresIt()
        {
            var r = Single(Hymn("a", 4, 4, false));
            Assert.AreEqual(0, r.WindupPower(Start));
            r.BeginWindup(Start);
            Assert.AreEqual(1, r.WindupPower(Start + 5));
            Assert.AreEqual(0.5, r.WindupPower(Start + DownbeatRound.WindupBeats * 0.5 / 2), 1e-9);
        }

        // ---- score, accuracy and rank -----------------------------------------------

        [Test]
        public void PerfectPressesAreOneSwingOnEveryScoredDownbeatAndRankS()
        {
            for (uint seed = 0; seed < 8; seed++)
            {
                var r = Round(seed);
                var presses = r.PerfectPresses();
                Assert.AreEqual(ScoredBalls(r).Count, presses.Count);
                CollectionAssert.AreEqual(ScoredBalls(r).Select(p => p.Time), presses);
                Finish(r, presses);
                Assert.IsTrue(r.Over);
                var res = r.Result();
                Assert.AreEqual("S", res.Rank, "seed " + seed);
                Assert.AreEqual(0, res.Miss);
                Assert.AreEqual(presses.Count, res.Perfect);
                Assert.AreEqual(1.0, res.Accuracy, 1e-9);
                Assert.AreEqual(presses.Count * 1000, res.Score);
                Assert.AreEqual(presses.Count, res.MaxCombo);
                Assert.AreEqual(0, r.Outs);
                Assert.AreEqual(presses.Count, r.Homers);
                Assert.AreEqual(0, res.BiasMs, 1e-9);
            }
        }

        [Test]
        public void ASwingAtABombCountsAsAMissInTheAccuracy()
        {
            var r = Round(3);
            var presses = r.PerfectPresses();
            int n = presses.Count;
            var bomb = ScoredBombs(r)[5];
            Finish(r, presses.Concat(new[] { bomb.Time }));
            var res = r.Result();
            Assert.AreEqual(n, res.Perfect);
            Assert.AreEqual(1, res.Miss);
            Assert.AreEqual(n / (double)(n + 1), res.Accuracy, 1e-9);
            Assert.AreNotEqual("S", res.Rank);
            Assert.AreEqual(n * 1000, res.Score);
            Assert.AreEqual(1, r.Bombs);
        }

        [Test]
        public void NeverSwingingIsAllStrikesAndRankD()
        {
            var r = Round(2);
            int balls = ScoredBalls(r).Count;
            Finish(r, new double[0]);
            var res = r.Result();
            Assert.AreEqual(balls, res.Miss);
            Assert.AreEqual("D", res.Rank);
            Assert.AreEqual(0, res.Score);
            Assert.IsTrue(r.Over);
        }

        [Test]
        public void TheResultNamesItsExtras()
        {
            var res = Round().Result();
            CollectionAssert.AreEqual(new[] { "HOME RUNS", "LINE DRIVES", "BUNTS", "FOULS", "BOMBS" }, res.Extras.Select(e => e.Key));
        }

        // ---- the timeline ---------------------------------------------------------------

        [Test]
        public void ThereIsNoCountInTheFirstBeatIsTheStartTime()
        {
            var r = Round();
            Assert.AreEqual(Start, r.Slots[0].StartTime, 1e-9);
            Assert.AreEqual(Start, r.Pitches[0].Time, 1e-9);
            Assert.AreEqual(3, r.Slots.Count);
            // nothing of the round exists before the first pitch has launched, a beat early
            Assert.AreEqual(Start - 0.5, r.Pitches[0].LaunchTime, 1e-9);
            Assert.IsNull(r.CurrentSlot);
            var started = new List<int>();
            r.ExcerptStarted += s => started.Add(s.Index);
            Drive(r, new double[0], r.EndTime + 3);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, started);
        }

        [Test]
        public void EveryPitchFliesTheSameArcLeadBeatsBeforeItsBeat()
        {
            var r = Round();
            foreach (var p in r.Pitches)
            {
                Assert.AreEqual(DownbeatRound.Lead, p.Lead);
                Assert.AreEqual(DownbeatRound.Apex, p.Apex);
                Assert.AreEqual(PitchKind.Normal, p.Kind);
                Assert.AreEqual(p.TargetBeat - DownbeatRound.Lead, p.LaunchBeat, 1e-12);
                Assert.AreEqual(p.LaunchTime, r.Clock.TimeAt(p.LaunchBeat), 1e-9);
                Assert.AreEqual(0, DownbeatRound.FlightU(p, p.LaunchBeat), 1e-12);
                Assert.AreEqual(1, DownbeatRound.FlightU(p, p.TargetBeat), 1e-12);
            }
            // at one tempo a ball and a bomb fly for exactly as long
            var flights = r.Slots[0].Pitches.Select(p => Math.Round(p.Time - p.LaunchTime, 9)).Distinct().ToList();
            Assert.AreEqual(1, flights.Count);
            Assert.AreEqual(0.5, flights[0], 1e-9);
        }

        [Test]
        public void ExcerptsLayBackToBackWithATwoBeatGapAndAnUnbrokenBeatAxis()
        {
            var r = Round(9);
            for (int k = 0; k + 1 < r.Slots.Count; k++)
            {
                var a = r.Slots[k];
                var b = r.Slots[k + 1];
                double beat = a.Pitches[1].Time - a.Pitches[0].Time;
                Assert.AreEqual(a.EndTime + DownbeatRound.GapBeats * beat, b.StartTime, 1e-9);
                Assert.AreEqual(b.StartTime, b.Pitches[0].Time, 1e-9);
                Assert.AreEqual(a.Pitches[a.Pitches.Count - 1].TargetBeat + 1 + DownbeatRound.GapBeats, b.Pitches[0].TargetBeat, 1e-9);
            }
            for (int i = 1; i < r.Pitches.Count; i++)
            {
                Assert.Greater(r.Pitches[i].Time, r.Pitches[i - 1].Time);
                Assert.AreEqual(r.Pitches[i].Time, r.Clock.TimeAt(r.Pitches[i].TargetBeat), 1e-9);
                Assert.AreEqual(r.Pitches[i].TargetBeat, r.Clock.BeatAt(r.Pitches[i].Time), 1e-9);
            }
            // the last bar of an excerpt ends where the gap starts
            Assert.AreEqual(r.Slots[2].EndTime, r.EndTime);
        }

        [Test]
        public void TheCutIsTheLastSixteenFullBarsOrTheTablesLastThreeLines()
        {
            int s, e;
            Assert.IsTrue(DownbeatRound.TrySpan(Hymn("a", 4, 4, false, 20), HymnLevel.Easy, out s, out e));
            Assert.AreEqual(5, s); Assert.AreEqual(20, e);
            Assert.IsTrue(DownbeatRound.TrySpan(Hymn("a", 4, 4, false, 20), HymnLevel.Hard, out s, out e));
            Assert.AreEqual(5, s, "the same span at every level");
            Assert.IsTrue(DownbeatRound.TrySpan(Hymn("short", 4, 4, false, 8), HymnLevel.Easy, out s, out e));
            Assert.AreEqual(1, s); Assert.AreEqual(8, e);
            Assert.IsTrue(DownbeatRound.TrySpan(Hymn("lines", 4, 4, false, 20, "9,13,17", "20"), HymnLevel.Hard, out s, out e));
            Assert.AreEqual(9, s); Assert.AreEqual(20, e);
            Assert.IsTrue(DownbeatRound.TrySpan(Hymn("end", 4, 4, false, 20, "-", "12"), HymnLevel.Medium, out s, out e));
            Assert.AreEqual(1, s); Assert.AreEqual(12, e);
            // a pickup hymn's pickup bar is not a full bar: the span starts at bar 1
            Assert.IsTrue(DownbeatRound.TrySpan(Hymn("pk", 4, 4, true, 12), HymnLevel.Easy, out s, out e));
            Assert.AreEqual(1, s);
            // a table naming bars the file does not have cannot be cut
            Assert.IsFalse(DownbeatRound.TrySpan(Hymn("bad", 4, 4, false, 20, "9,13,17", "40"), HymnLevel.Easy, out s, out e));
        }

        [Test]
        public void ATempoChangeMovesTheBeatsTheWindowsAndTheLaunches()
        {
            // 4/4, 20 bars, 120 bpm that halves to 60 bpm at bar 11 (tick 40 * 480): beats 0.5 s, then 1 s
            var smf = new SmfBuilder();
            var meta = smf.AddTrack();
            var notes = smf.AddTrack();
            meta.Tempo(0, 120).Tempo(10 * 4 * 480, 60).Sig(0, 4, 4);
            for (int k = 0; k < 80 * 2; k++) notes.Note(k * 480, k * 480 + 470, 60 + (k % 12));
            meta.Tempo(80 * 480, 120).Sig(80 * 480, 4, 4).EndAt(160 * 480);
            var midi = HymnMidi.Parse(smf.Build());
            var grid = HymnGrid.Build(midi, 0);
            var row = HymnExcerptTable.Parse("tempo 4 no - 20 yes").Rows.Single();
            var r = Single(HymnCandidate.From(midi, grid, row));

            var fast = r.Pitches.First(p => !p.IsFree && p.IsBall && p.BeatSeconds < 0.6);
            var slow = r.Pitches.First(p => !p.IsFree && p.IsBall && p.BeatSeconds > 0.9);
            Assert.AreEqual(0.5, fast.BeatSeconds, 1e-6);
            Assert.AreEqual(1.0, slow.BeatSeconds, 1e-6);
            Assert.AreEqual(128, fast.WindowMs, 1e-6, "fast beats keep the arcade floor");
            Assert.AreEqual(250, slow.WindowMs, 1e-6, "slow beats get a quarter of the beat");
            // the launch is a beat before, in that beat's own tempo
            Assert.AreEqual(fast.Time - 0.5, fast.LaunchTime, 1e-6);
            var later = r.Pitches.First(p => p.Bar == slow.Bar + 1 && p.BarPosition == 0);
            Assert.AreEqual(later.Time - 1.0, later.LaunchTime, 1e-6);
            Assert.AreEqual(slow.Time - 0.5, slow.LaunchTime, 1e-6, "the beat before the change is still a fast one");
            // 200 ms late: inside the slow window (a bunt, a foul), out of the fast one
            Assert.IsNull(r.Swing(fast.Time + 0.2));
            var hit = r.Swing(slow.Time + 0.2);
            Assert.AreSame(slow, hit);
            Assert.AreEqual(1, r.Stats.Good);
            // the beat axis follows: BeatAt is linear inside a tempo and bends at the change
            Assert.AreEqual(slow.TargetBeat + 0.5, r.Clock.BeatAt(slow.Time + 0.5), 1e-6);
            Assert.AreEqual(fast.TargetBeat + 0.5, r.Clock.BeatAt(fast.Time + 0.25), 1e-6);
            Assert.AreEqual(1.0, r.Clock.SpbAt(slow.Time + 0.1), 1e-6);
        }

        [Test]
        public void StartingAgainResetsTheRound()
        {
            var r = Round(4);
            Finish(r, r.PerfectPresses());
            Assert.IsTrue(r.Over);
            r.Start(20);
            Assert.IsFalse(r.Over);
            Assert.AreEqual(0, r.Points);
            Assert.AreEqual(0, r.Stats.Perfect);
            Assert.AreEqual(20, r.Pitches[0].Time, 1e-9);
            Drive(r, r.PerfectPresses(), r.EndTime + 3, from: 17);
            Assert.AreEqual("S", r.Result().Rank);
        }

        // ---- selection ---------------------------------------------------------------------

        static HymnCandidate Cand(string name, int pattern, bool pickup) => new HymnCandidate { Name = name, Pattern = pattern, Pickup = pickup };

        static List<HymnCandidate> Rich()
        {
            var p = new List<HymnCandidate>();
            for (int i = 0; i < 4; i++) p.Add(Cand("a4n" + i, 4, false));
            for (int i = 0; i < 3; i++) p.Add(Cand("a4p" + i, 4, true));
            for (int i = 0; i < 3; i++) p.Add(Cand("a3n" + i, 3, false));
            for (int i = 0; i < 2; i++) p.Add(Cand("a3p" + i, 3, true));
            for (int i = 0; i < 2; i++) p.Add(Cand("a2n" + i, 2, false));
            for (int i = 0; i < 2; i++) p.Add(Cand("a2p" + i, 2, true));
            return p;
        }

        [Test]
        public void TheSelectionIsEasyMediumHardAndFollowsItsRules()
        {
            CollectionAssert.AreEqual(new[] { HymnLevel.Easy, HymnLevel.Medium, HymnLevel.Hard }, DownbeatRound.Levels);
            var pool = Rich();
            for (uint seed = 0; seed < 60; seed++)
            {
                var picks = DownbeatRound.Select(pool, seed);
                CollectionAssert.AreEqual(picks, DownbeatRound.Select(pool, seed));
                Assert.AreEqual(3, picks.Count);
                Assert.AreEqual(3, picks.Distinct().Count(), "no repeats, seed " + seed);
                var c = picks.Select(i => pool[i]).ToList();
                Assert.IsFalse(c[0].Pickup, "easy has no pickup, seed " + seed);
                Assert.IsTrue(c[2].Pickup, "hard has a pickup, seed " + seed);
                Assert.AreNotEqual(c[0].Pattern, c[1].Pattern, "medium changes the meter, seed " + seed);
                Assert.AreNotEqual(c[1].Pattern, c[2].Pattern, "hard changes the meter, seed " + seed);
            }
            Assert.Greater(Enumerable.Range(0, 30).Select(s => string.Join(",", DownbeatRound.Select(pool, (uint)s))).Distinct().Count(), 10);
        }

        [Test]
        public void ASmallPoolRelaxesTheRulesInsteadOfFailing()
        {
            var noPickup = new List<HymnCandidate> { Cand("a", 4, false), Cand("b", 4, false) };
            var picks = DownbeatRound.Select(noPickup, 1);
            Assert.AreEqual(3, picks.Count);                       // two hymns, three slots: the least used come back
            Assert.AreEqual(2, picks.Distinct().Count());
            Assert.AreEqual(3, DownbeatRound.Select(new List<HymnCandidate> { Cand("only", 3, true) }, 7).Count);
            Assert.Throws<ArgumentException>(() => DownbeatRound.Select(new List<HymnCandidate>(), 0));
        }

        [Test]
        public void UncuttableCandidatesAreLeftOutOfTheRound()
        {
            var pool = Pool();
            pool.Insert(0, Cand("ghost", 4, false));
            CollectionAssert.DoesNotContain(new DownbeatRound(pool, 3).Playlist, "ghost");
            Assert.Throws<ArgumentException>(() => new DownbeatRound(new List<HymnCandidate> { Cand("ghost", 4, false) }, 0));
            Assert.AreEqual(3, Round().Playlist.Count);
            CollectionAssert.AreEqual(Round(5).Playlist, new DownbeatRound(Pool(), 5).Playlist);
        }

        [Test]
        public void TheSlotsNameTheClipOfTheirLevel()
        {
            var r = Round(5);
            for (int k = 0; k < 3; k++)
            {
                Assert.AreEqual(DownbeatRound.Levels[k], r.Slots[k].Level);
                Assert.AreEqual(r.Playlist[k], r.Slots[k].Name);
                Assert.AreEqual(r.Playlist[k], r.Slots[k].Clip);
            }
        }

        // ---- the CPUs ---------------------------------------------------------------------------

        [Test]
        public void CpuRollsAreSeededAndDoNotDependOnEachOther()
        {
            var a = Round(11, 3);
            var b = Round(11, 3);
            for (int k = 0; k < 3; k++)
                for (int c = 0; c < 3; c++)
                {
                    Assert.AreEqual(a.CpuRolls[k][c].Points, b.CpuRolls[k][c].Points);
                    Assert.AreEqual(a.CpuRolls[k][c].LockBar, b.CpuRolls[k][c].LockBar);
                }
            // the first CPU rolls the same with one rival or three
            var one = Round(11, 1);
            for (int k = 0; k < 3; k++) Assert.AreEqual(a.CpuRolls[k][0].Points, one.CpuRolls[k][0].Points);
            Assert.AreNotEqual(Round(12, 3).Totals().Skip(1).Sum(), a.Totals().Skip(1).Sum());
        }

        [Test]
        public void SkilledCpusScoreBetterThanWeakOnes()
        {
            double weak = 0, strong = 0;
            for (uint seed = 0; seed < 40; seed++)
            {
                var r = new DownbeatRound(Pool(), seed, new[] { 0.40, 0.86 });
                r.Start(Start);
                weak += r.Totals()[1];
                strong += r.Totals()[2];
            }
            Assert.Greater(strong, weak * 1.2);
        }

        [Test]
        public void TheOutcomeCarriesAFieldForAParty()
        {
            var r = Round(6, 2);
            Finish(r, r.PerfectPresses());
            var o = r.Result(new[] { 0, 3, 5 });
            Assert.IsNotNull(o.Field);
            Assert.AreEqual(3, o.Field.Count);
            CollectionAssert.AreEqual(new[] { 0, 3, 5 }, o.Field.Select(f => f.Id));
            Assert.AreEqual(0, o.Field[0].Place, "a perfect round wins");
            Assert.AreEqual(0, o.Field.Min(f => f.Place));
            Assert.IsNull(r.Result().Field, "free play has no field");
            Assert.IsNull(r.Result(new[] { 0 }).Field, "a lineup of the wrong size is ignored");
        }

        // ---- the real files (optional) --------------------------------------------------------

        [Test]
        public void RealHymnsMakeValidDownbeatRounds()
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
                var midi = HymnMidi.Parse(File.ReadAllBytes(path));
                string tempo = Path.Combine(dir, "tempo", row.Hymn + ".json");
                var map = File.Exists(tempo) ? HymnTempoMap.Parse(File.ReadAllText(tempo)) : HymnTempoMap.Identity(row.Hymn);
                pool.Add(HymnCandidate.From(midi, map, row));
            }
            Assert.GreaterOrEqual(pool.Count, 6);

            foreach (var c in pool)
                foreach (var level in DownbeatRound.Levels)
                {
                    int s, e;
                    Assert.IsTrue(DownbeatRound.TrySpan(c, level, out s, out e), c.Name + " " + level);
                    TestContext.Progress.WriteLine("{0} {1}: bars {2}-{3}", c.Name, level, s, e);
                }

            for (uint seed = 0; seed < 10; seed++)
            {
                var round = new DownbeatRound(pool, seed, 2);
                string why = "seed " + seed + ": " + string.Join(" ", round.Playlist);
                Assert.AreEqual(3, round.Playlist.Distinct().Count(), "repeat, " + why);
                round.Start(5);
                foreach (var p in round.Pitches) Assert.AreEqual(p.BarPosition == 0, p.IsBall, why);
                Assert.IsTrue(round.Slots.All(s => s.Pitches.Count >= 16), why);
                Drive(round, round.PerfectPresses(), round.EndTime + 3, from: 2);
                Assert.IsTrue(round.Over, why);
                Assert.AreEqual("S", round.Result().Rank, why);
                Assert.AreEqual(0, round.Stats.Miss, why);
            }
        }
    }
}
