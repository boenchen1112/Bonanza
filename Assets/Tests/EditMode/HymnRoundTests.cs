// Pins the Hymn Meter round: the ladder (seeded playlist and its relaxation), the timeline,
// the reveal flag, the CPU rolls, winners and the outcome. Pure C#, no hymn files: hymns are
// built in memory (TestHymns.cs). One optional test reads the real files when BONANZA_HYMNS
// points at a hymns folder (skipped else).
//
// Synthetic hymns are 120 bpm, so a beat is 0.5 s and a 4/4 bar 2 s. The fallback cut (no
// table lines) gives Easy 12 bars, Medium 8, Hard 4.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Bonanza.Core;

namespace Bonanza.Core.Tests
{
    public class HymnRoundTests
    {
        // ---- the ladder (pure) -----------------------------------------------

        static HymnCandidate Cand(string name, int pattern, bool pickup) =>
            new HymnCandidate { Name = name, Pattern = pattern, Pickup = pickup };

        static List<HymnCandidate> RichPool()
        {
            var p = new List<HymnCandidate>();
            for (int i = 0; i < 4; i++) p.Add(Cand("a4n" + i, 4, false));
            for (int i = 0; i < 3; i++) p.Add(Cand("a4p" + i, 4, true));
            for (int i = 0; i < 3; i++) p.Add(Cand("a3n" + i, 3, false));
            for (int i = 0; i < 3; i++) p.Add(Cand("a3p" + i, 3, true));
            for (int i = 0; i < 2; i++) p.Add(Cand("a2n" + i, 2, false));
            for (int i = 0; i < 2; i++) p.Add(Cand("a2p" + i, 2, true));
            return p;
        }

        [Test]
        public void SameSeedSamePlaylistAndSeedsVary()
        {
            var pool = RichPool();
            for (uint s = 0; s < 10; s++)
                CollectionAssert.AreEqual(HymnLadder.Select(pool, s), HymnLadder.Select(pool, s));
            var distinct = Enumerable.Range(0, 20).Select(s => string.Join(",", HymnLadder.Select(pool, (uint)s))).Distinct().Count();
            Assert.Greater(distinct, 10);
        }

        [Test]
        public void TheLadderIsTwoEasyTwoMediumTwoHard()
        {
            CollectionAssert.AreEqual(
                new[] { HymnLevel.Easy, HymnLevel.Easy, HymnLevel.Medium, HymnLevel.Medium, HymnLevel.Hard, HymnLevel.Hard },
                HymnLadder.SlotLevels);
            Assert.AreEqual(6, HymnLadder.Select(RichPool(), 3).Count);
        }

        [Test]
        public void RichPoolFollowsEveryRule()
        {
            var pool = RichPool();
            for (uint seed = 0; seed < 60; seed++)
            {
                var p = HymnLadder.Select(pool, seed).Select(i => pool[i]).ToList();
                string why = "seed " + seed + ": " + string.Join(" ", p.Select(h => h.Name));
                Assert.IsFalse(p[0].Pickup || p[1].Pickup, "easy pickup, " + why);
                Assert.AreEqual(p[0].Pattern, p[1].Pattern, "easy pattern, " + why);
                Assert.AreNotEqual(p[1].Pattern, p[2].Pattern, "medium change, " + why);
                Assert.AreEqual(p[2].Pattern, p[3].Pattern, "medium keep, " + why);
                Assert.AreNotEqual(p[3].Pattern, p[4].Pattern, "hard change, " + why);
                Assert.AreNotEqual(p[4].Pattern, p[5].Pattern, "hard change, " + why);
                Assert.IsTrue(p[4].Pickup && p[5].Pickup, "hard pickup, " + why);
                Assert.AreEqual(6, p.Distinct().Count(), "repeat, " + why);
            }
        }

        [Test]
        public void EasyPicksALeadHymnThatHasAPatternPartner()
        {
            var pool = new List<HymnCandidate>
            {
                Cand("A", 4, false), Cand("B", 3, false), Cand("C", 4, false),
                Cand("D", 4, true), Cand("E", 3, true), Cand("F", 2, true), Cand("G", 2, true),
            };
            for (uint seed = 0; seed < 40; seed++)
            {
                var p = HymnLadder.Select(pool, seed).Select(i => pool[i]).ToList();
                Assert.AreEqual(4, p[0].Pattern, "seed " + seed);
                Assert.AreEqual(4, p[1].Pattern, "seed " + seed);
                Assert.AreNotEqual(p[0].Name, p[1].Name);
            }
        }

        [Test]
        public void RelaxationDropsHardPickupFirst()
        {
            // no pickup hymn at all: hard still changes pattern every time, with no pickups
            var pool = new List<HymnCandidate>();
            foreach (int pat in new[] { 4, 4, 4, 3, 3, 3, 2, 2, 2 }) pool.Add(Cand("h" + pool.Count, pat, false));
            for (uint seed = 0; seed < 30; seed++)
            {
                var p = HymnLadder.Select(pool, seed).Select(i => pool[i]).ToList();
                Assert.AreNotEqual(p[3].Pattern, p[4].Pattern);
                Assert.AreNotEqual(p[4].Pattern, p[5].Pattern);
                Assert.AreEqual(6, p.Distinct().Count());
            }
        }

        [Test]
        public void RelaxationDropsThePatternRuleBeforeRepeatsAndRepeatsBeforeEasyPickups()
        {
            // one pattern only: no pattern rule can hold, but nothing repeats while the pool lasts
            var one = Enumerable.Range(0, 8).Select(i => Cand("x" + i, 4, i % 2 == 0)).ToList();
            for (uint seed = 0; seed < 20; seed++)
            {
                var p = HymnLadder.Select(one, seed).Select(i => one[i]).ToList();
                Assert.AreEqual(6, p.Distinct().Count());
                Assert.IsFalse(p[0].Pickup || p[1].Pickup);
            }

            // a single no-pickup hymn: easy repeats it rather than taking a pickup hymn
            var few = new List<HymnCandidate> { Cand("only", 4, false), Cand("p1", 3, true), Cand("p2", 2, true), Cand("p3", 4, true) };
            for (uint seed = 0; seed < 20; seed++)
            {
                var p = HymnLadder.Select(few, seed).Select(i => few[i]).ToList();
                Assert.AreEqual("only", p[0].Name);
                Assert.AreEqual("only", p[1].Name);
            }

            // all pickups: easy takes pickups instead of failing
            var all = Enumerable.Range(0, 6).Select(i => Cand("q" + i, 3 + i % 2, true)).ToList();
            Assert.AreEqual(6, HymnLadder.Select(all, 1).Count);
        }

        [Test]
        public void ASmallPoolRepeatsEvenly()
        {
            var two = new List<HymnCandidate> { Cand("a", 4, false), Cand("b", 3, false) };
            var picks = HymnLadder.Select(two, 5);
            Assert.AreEqual(6, picks.Count);
            Assert.AreEqual(3, picks.Count(i => i == 0));
            Assert.AreEqual(3, picks.Count(i => i == 1));
            var single = new List<HymnCandidate> { Cand("a", 4, false) };
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 0, 0, 0 }, HymnLadder.Select(single, 0));
            Assert.Throws<ArgumentException>(() => HymnLadder.Select(new List<HymnCandidate>(), 0));
        }

        // ---- real rounds on synthetic hymns ----------------------------------

        static HymnCandidate Real(string name, int num, int den, bool pickup, int bars = 20)
        {
            var midi = HymnMidi.Parse(SyntheticHymn.Build(num, den, bars, pickup));
            var grid = HymnGrid.Build(midi, 0);
            var row = HymnExcerptTable.Parse(name + " " + grid.Pattern + " " + (pickup ? "yes" : "no") + " - - yes").Rows.Single();
            return HymnCandidate.From(midi, grid, row);
        }

        static List<HymnCandidate> Pool() => new List<HymnCandidate>
        {
            Real("n44a", 4, 4, false), Real("n44b", 4, 4, false), Real("n34a", 3, 4, false), Real("n34b", 3, 4, false),
            Real("p44a", 4, 4, true), Real("p44b", 4, 4, true), Real("p34a", 3, 4, true), Real("p34b", 3, 4, true),
            Real("n68", 6, 8, false), Real("n24", 2, 4, false),
        };

        // Feed presses and clock in order: a press is delivered at its own time.
        static void Drive(HymnRound r, IEnumerable<MeterPress> presses, double from, double to, double dt = 0.01)
        {
            var queue = new Queue<MeterPress>(presses.OrderBy(p => p.Time));
            for (double t = from; t <= to; t += dt)
            {
                while (queue.Count > 0 && queue.Peek().Time <= t) { var p = queue.Dequeue(); r.Press(p.Action, p.Time); }
                r.Update(t);
            }
        }

        static HymnRound Started(uint seed = 7, int cpus = 0, double start = 10)
        {
            var r = new HymnRound(Pool(), seed, cpus);
            r.Start(start);
            return r;
        }

        [Test]
        public void ThePlaylistIsTheLadderOfThePool()
        {
            var pool = Pool();
            var r = new HymnRound(pool, 11, 0);
            CollectionAssert.AreEqual(HymnLadder.Select(pool, 11).Select(i => pool[i].Name), r.Playlist);
            CollectionAssert.AreEqual(r.Playlist, new HymnRound(Pool(), 11, 2).Playlist);
        }

        [Test]
        public void UncuttableCandidatesAreLeftOut()
        {
            var pool = Pool();
            pool.Insert(0, Cand("ghost", 4, false));
            var r = new HymnRound(pool, 3, 0);
            CollectionAssert.DoesNotContain(r.Playlist, "ghost");
            Assert.Throws<ArgumentException>(() => new HymnRound(new List<HymnCandidate> { Cand("ghost", 4, false) }, 0, 0));
        }

        [Test]
        public void ExcerptsLayBackToBackWithATwoBeatGap()
        {
            var r = Started(7, 0, 10);
            Assert.AreEqual(6, r.Slots.Count);
            Assert.AreEqual(10.0, r.Slots[0].StartTime, 1e-9);
            Assert.AreEqual(10.0, r.Slots[0].Beats[0].Time, 1e-9);
            for (int k = 0; k < 6; k++)
            {
                var s = r.Slots[k];
                Assert.AreEqual(HymnLadder.SlotLevels[k], s.Level);
                Assert.AreEqual(s.StartTime, s.Beats[0].Time, 1e-9);
                Assert.AreEqual(s.StartTime - Windows.GoodMs / 1000.0, s.WindowStart, 1e-9);
                double span = s.Excerpt.EndTime - s.Excerpt.StartTime;
                Assert.AreEqual(s.StartTime + span, s.WindowEnd, 1e-9);
                double beat = s.Beats[1].Time - s.Beats[0].Time;
                for (int i = 0; i < s.Beats.Count; i++)
                {
                    Assert.AreEqual(s.Excerpt.Beats[i].BarPosition, s.Beats[i].BarPosition);
                    Assert.AreEqual(s.Excerpt.Beats[i].BeatsPerBar, s.Beats[i].BeatsPerBar);
                    Assert.AreEqual(s.Excerpt.Beats[i].Time - s.Excerpt.StartTime + s.StartTime, s.Beats[i].Time, 1e-9);
                }
                if (k > 0) Assert.AreEqual(r.Slots[k - 1].WindowEnd + 2 * (r.Slots[k - 1].Beats[1].Time - r.Slots[k - 1].Beats[0].Time), s.StartTime, 1e-9);
            }
            Assert.AreEqual(r.Slots[5].WindowEnd, r.EndTime, 1e-9);
        }

        [Test]
        public void ResultIsNullUntilDone()
        {
            var r = new HymnRound(Pool(), 1, 0);
            Assert.IsNull(r.Result());
            r.Start(0);
            Assert.IsNull(r.Result());
            Drive(r, new MeterPress[0], 0, r.Slots[4].WindowEnd + 1);
            Assert.IsFalse(r.Done);
            Assert.IsNull(r.Result());
            Drive(r, new MeterPress[0], r.Slots[4].WindowEnd + 1, r.EndTime + 1);
            Assert.IsTrue(r.Done);
            Assert.IsNotNull(r.Result());
        }

        [Test]
        public void PerfectPressesAllCorrectRankS()
        {
            var r = Started(7, 0, 5);
            var presses = r.PerfectPresses();
            Assert.AreEqual(r.Slots.Sum(s => s.Beats.Count), presses.Count);
            Drive(r, presses, 5, r.EndTime + 1);
            Assert.IsTrue(r.Done);
            foreach (var s in r.Slots)
            {
                Assert.IsTrue(s.Judgement.Results[0].Correct, s.Name);
                Assert.AreEqual(0, s.Judgement.Results[0].StartBeat);
                Assert.AreEqual(0, s.Judgement.Winner);
            }
            var o = r.Result();
            Assert.AreEqual("S", o.Rank);
            Assert.AreEqual(6000, o.Score);
            Assert.AreEqual(1.0, o.Accuracy, 1e-9);
            Assert.AreEqual("WON", o.Extras[0].Key);
            Assert.AreEqual("6/6", o.Extras[0].Value);
            Assert.AreEqual("CORRECT", o.Extras[1].Key);
            Assert.AreEqual("6/6", o.Extras[1].Value);
        }

        [Test]
        public void TheWrongPatternIsNotCorrect()
        {
            var r = Started(7, 0, 0);
            // always "down": right only for the downbeat, never a full bar of it
            var presses = r.Slots.SelectMany(s => s.Beats.Select(b => new MeterPress { Action = "down", Time = b.Time })).ToList();
            Drive(r, presses, 0, r.EndTime + 1);
            Assert.IsTrue(r.Done);
            Assert.IsTrue(r.Slots.All(s => !s.Judgement.Results[0].Correct));
            Assert.IsTrue(r.Slots.All(s => s.Judgement.Winner == -1));
            var o = r.Result();
            Assert.AreEqual(0, o.Score);
            Assert.AreEqual("D", o.Rank);
            Assert.AreEqual("0/6", o.Extras[1].Value);
        }

        [Test]
        public void JoiningLateScoresLess()
        {
            var r = Started(7, 0, 0);
            var presses = new List<MeterPress>();
            for (int k = 0; k < 6; k++) presses.AddRange(r.PerfectPresses(k).Skip(k == 0 ? 2 : 0));
            Drive(r, presses, 0, r.EndTime + 1);
            var j = r.Slots[0].Judgement;
            Assert.IsTrue(j.Results[0].Correct);
            Assert.AreEqual(2, j.Results[0].StartBeat);
            double expected = 600 + 300 * (1 - 2.0 / (r.Slots[0].Beats.Count - 1)) + 100;
            Assert.AreEqual(expected, j.HumanPoints, 1e-9);
            Assert.AreEqual(Math.Round(5000 + expected, MidpointRounding.AwayFromZero), r.Result().Score);
            Assert.Less(r.Result().Score, 6000);
        }

        [Test]
        public void PointsFormula()
        {
            var first = new MeterResult { Correct = true, StartBeat = 0, Accuracy = 1 };
            var last = new MeterResult { Correct = true, StartBeat = 19, Accuracy = 0 };
            Assert.AreEqual(1000, HymnRound.PointsFor(first, 20), 1e-9);
            Assert.AreEqual(600, HymnRound.PointsFor(last, 20), 1e-9);
            Assert.AreEqual(0, HymnRound.PointsFor(new MeterResult(), 20));
        }

        [Test]
        public void EventsFireInOrderOncePerExcerpt()
        {
            var r = Started(7, 1, 0);
            var log = new List<string>();
            r.ExcerptStarted += s => log.Add("start" + s.Index);
            r.ExcerptRevealed += s => log.Add("reveal" + s.Index);
            r.ExcerptJudged += (s, j) => log.Add("judged" + s.Index);
            r.RoundDone += () => log.Add("done");
            Drive(r, r.PerfectPresses(), 0, r.EndTime + 1);
            var expected = new List<string>();
            for (int k = 0; k < 6; k++) expected.AddRange(new[] { "start" + k, "reveal" + k, "judged" + k });
            expected.Add("done");
            CollectionAssert.AreEqual(expected, log);
        }

        [Test]
        public void ABigClockJumpPlaysOutEveryExcerpt()
        {
            var r = Started(7, 2, 0);
            var log = new List<string>();
            r.ExcerptJudged += (s, j) => log.Add("judged" + s.Index);
            r.Update(r.EndTime + 100);
            Assert.IsTrue(r.Done);
            Assert.AreEqual(6, log.Count);
            Assert.IsNotNull(r.Result());
        }

        // ---- the reveal ------------------------------------------------------

        [Test]
        public void RevealComesAtTheFirstValidBarWhenEveryoneAnswered()
        {
            var r = Started(7, 0, 0);
            var reveals = new Dictionary<int, double>();
            r.ExcerptRevealed += s => reveals[s.Index] = s.RevealTime;
            Drive(r, r.PerfectPresses(), 0, r.EndTime + 1);
            foreach (var s in r.Slots)
            {
                double firstBarEnd = s.Beats[s.Beats[0].BeatsPerBar - 1].Time;
                Assert.GreaterOrEqual(reveals[s.Index], firstBarEnd - 1e-9, s.Name);
                Assert.Less(reveals[s.Index], firstBarEnd + 0.02, s.Name);
                Assert.Less(reveals[s.Index], s.WindowEnd - 1.0);
            }
        }

        [Test]
        public void RevealWaitsForTheWindowWhenTheHumanNeverAnswers()
        {
            var r = Started(7, 0, 0);
            var reveals = new Dictionary<int, double>();
            r.ExcerptRevealed += s => reveals[s.Index] = s.RevealTime;
            Drive(r, new MeterPress[0], 0, r.EndTime + 1);
            foreach (var s in r.Slots)
            {
                Assert.Greater(reveals[s.Index], s.WindowEnd);
                Assert.Less(reveals[s.Index], s.WindowEnd + 0.02);
            }
        }

        [Test]
        public void RevealWaitsForTheSlowestCpuOrAWrongOne()
        {
            for (uint seed = 0; seed < 8; seed++)
            {
                var r = Started(seed, 3, 0);
                Drive(r, r.PerfectPresses(), 0, r.EndTime + 1);
                foreach (var s in r.Slots)
                {
                    double human = s.Beats[s.Beats[0].BeatsPerBar - 1].Time;
                    double slowest = s.CpuRolls.Max(c => c.AnswerTime);
                    double expected = double.IsInfinity(slowest) ? s.WindowEnd : Math.Max(human, slowest);
                    Assert.GreaterOrEqual(s.RevealTime, expected - 1e-9, "seed " + seed + " " + s.Name);
                    Assert.Less(s.RevealTime, expected + 0.03, "seed " + seed + " " + s.Name);
                    if (double.IsInfinity(slowest)) Assert.IsTrue(s.RevealTime > s.WindowEnd);
                }
            }
        }

        [Test]
        public void PressesAreIgnoredBeforeStartAndForNonArrowKeys()
        {
            var r = new HymnRound(Pool(), 1, 0);
            Assert.IsFalse(r.Press("left", 0));
            r.Start(0);
            Assert.IsFalse(r.Press("a", 0.1));
            Assert.IsTrue(r.Press("up", 0.1));
        }

        // ---- the CPUs --------------------------------------------------------

        static List<MeterBeat> Beats(int bars, int bpb)
        {
            var g = new List<MeterBeat>();
            for (int i = 0; i < bars * bpb; i++)
                g.Add(new MeterBeat { Time = 1.0 + i * 0.5, BarPosition = i % bpb, BeatsPerBar = bpb });
            return g;
        }

        [Test]
        public void CpuRollsAreReproducibleBySeedAndPerPlayer()
        {
            string Dump(HymnRound r) => string.Join(";", r.Slots.SelectMany(s => s.CpuRolls.Select(c =>
                c.Result.Correct + "/" + c.Result.StartBeat + "/" + c.Result.Accuracy + "/" + c.AnswerTime)));
            var a = Started(5, 3, 0); a.Update(a.EndTime + 100);
            var b = Started(5, 3, 0); b.Update(b.EndTime + 100);
            var c = Started(6, 3, 0); c.Update(c.EndTime + 100);
            Assert.AreEqual(Dump(a), Dump(b));
            Assert.AreNotEqual(Dump(a), Dump(c));
            // the CPUs differ from each other
            Assert.IsTrue(a.Slots.Any(s => s.CpuRolls[0].Result.StartBeat != s.CpuRolls[1].Result.StartBeat
                                           || s.CpuRolls[0].Result.Accuracy != s.CpuRolls[1].Result.Accuracy));
        }

        [Test]
        public void CpuRollsStayInRangeAndFormAValidRun()
        {
            var beats = Beats(6, 4);
            int last = beats.Count - 1;
            foreach (HymnLevel level in Enum.GetValues(typeof(HymnLevel)))
                foreach (double skill in new[] { 0.0, 0.4, 0.63, 0.86, 1.0 })
                    for (uint seed = 0; seed < 60; seed++)
                    {
                        var roll = HymnCpu.Roll(beats, level, skill, new Rng(seed));
                        var r = roll.Result;
                        if (!r.Correct) { Assert.AreEqual(-1, r.StartBeat); Assert.IsTrue(double.IsPositiveInfinity(roll.AnswerTime)); continue; }
                        Assert.GreaterOrEqual(r.StartBeat, 0);
                        Assert.LessOrEqual(r.StartBeat, last - 3);
                        Assert.GreaterOrEqual(r.Accuracy, 0.3);
                        Assert.LessOrEqual(r.Accuracy, 0.98);
                        Assert.AreEqual(beats[r.StartBeat].Time, r.StartTime, 1e-9);
                        // the answer it claims is one MeterJudge would accept
                        var presses = beats.Skip(r.StartBeat).Select(b => new MeterPress { Action = Meter.KeyFor(b.BeatsPerBar, b.BarPosition), Time = b.Time });
                        var judged = MeterJudge.Judge(beats, 0, 100, presses);
                        Assert.IsTrue(judged.Correct);
                        Assert.AreEqual(r.StartBeat, judged.StartBeat);
                        Assert.GreaterOrEqual(roll.AnswerTime, r.StartTime + 1.5 - 1e-9);
                        Assert.LessOrEqual(roll.AnswerTime, beats[last].Time);
                    }
        }

        [Test]
        public void CpuSkillAndLevelShapeTheRolls()
        {
            var beats = Beats(12, 4);
            double Rate(HymnLevel l, double skill) => Enumerable.Range(0, 400).Count(s => HymnCpu.Roll(beats, l, skill, new Rng((uint)s)).Result.Correct) / 400.0;
            double Start(HymnLevel l, double skill) => Enumerable.Range(0, 400).Select(s => HymnCpu.Roll(beats, l, skill, new Rng((uint)s)).Result)
                .Where(r => r.Correct).Average(r => (double)r.StartBeat);
            Assert.Greater(Rate(HymnLevel.Easy, 0.9), Rate(HymnLevel.Easy, 0.2));
            Assert.Greater(Rate(HymnLevel.Easy, 0.6), Rate(HymnLevel.Hard, 0.6));
            Assert.Less(Start(HymnLevel.Easy, 0.6), Start(HymnLevel.Hard, 0.6));
            Assert.Less(Start(HymnLevel.Medium, 0.9), Start(HymnLevel.Medium, 0.2));
        }

        [Test]
        public void CpuCanNotAnswerWhenThereIsNoFullBar()
        {
            var roll = HymnCpu.Roll(Beats(1, 4).Take(3).ToList(), HymnLevel.Easy, 1, new Rng(1));
            Assert.IsFalse(roll.Result.Correct);
        }

        [Test]
        public void CpuResultsRankLikeTheHumans()
        {
            var beats = Beats(8, 3);
            var rolls = Enumerable.Range(0, 30).Select(s => HymnCpu.Roll(beats, HymnLevel.Medium, 0.7, new Rng((uint)s)).Result).ToList();
            var sorted = rolls.OrderBy(r => r, new MeterRanking()).ToList();
            for (int i = 1; i < sorted.Count; i++) Assert.LessOrEqual(new MeterRanking().Compare(sorted[i - 1], sorted[i]), 0);
            Assert.IsTrue(sorted[0].Correct);
        }

        // ---- winners ---------------------------------------------------------

        static MeterResult Res(bool correct, int start = -1, double acc = 0) =>
            new MeterResult { Correct = correct, StartBeat = correct ? start : -1, Accuracy = acc };

        [Test]
        public void WinnerIsTheBestByRankingEarlierIndexOnTies()
        {
            Assert.AreEqual(1, HymnRound.WinnerOf(new[] { Res(false), Res(true, 4, 0.5), Res(true, 6, 0.9) }));
            Assert.AreEqual(2, HymnRound.WinnerOf(new[] { Res(true, 4, 0.5), Res(true, 4, 0.6), Res(true, 4, 0.9) }));
            Assert.AreEqual(0, HymnRound.WinnerOf(new[] { Res(true, 4, 0.5), Res(true, 4, 0.5), Res(true, 5, 0.9) }), "tie: the human (index 0) wins");
            Assert.AreEqual(1, HymnRound.WinnerOf(new[] { Res(false), Res(true, 2, 0.4), Res(true, 2, 0.4) }), "tie between CPUs: the earlier one");
            Assert.AreEqual(-1, HymnRound.WinnerOf(new[] { Res(false), Res(false) }));
            Assert.AreEqual(-1, HymnRound.WinnerOf(new MeterResult[0]));
        }

        [Test]
        public void RoundWinnerMatchesTheResultsOfEveryPlayer()
        {
            for (uint seed = 0; seed < 5; seed++)
            {
                var r = Started(seed, 3, 0);
                Drive(r, r.PerfectPresses().Where((p, i) => i % 3 != 1), 0, r.EndTime + 1);   // a ragged human
                foreach (var s in r.Slots)
                {
                    var j = s.Judgement;
                    Assert.AreEqual(4, j.Results.Count);
                    Assert.AreEqual(HymnRound.WinnerOf(j.Results), j.Winner);
                    for (int c = 0; c < 3; c++) Assert.AreSame(s.CpuRolls[c].Result, j.Results[c + 1]);
                }
                var o = r.Result();
                Assert.AreEqual(r.Slots.Count(s => s.Judgement.Winner == 0) + "/6", o.Extras[0].Value);
            }
        }

        [Test]
        public void AFlawlessHumanOutranksCpusWhoJoinNoEarlierOrAreLessAccurate()
        {
            for (uint seed = 0; seed < 10; seed++)
            {
                var r = Started(seed, 2, 0);
                Drive(r, r.PerfectPresses(), 0, r.EndTime + 1);
                foreach (var s in r.Slots) Assert.AreEqual(0, s.Judgement.Winner, "seed " + seed + " " + s.Name);
            }
        }

        // ---- the real files (optional) ---------------------------------------

        [Test]
        public void RealHymnsMakeValidPlaylists()
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
                pool.Add(HymnCandidate.From(midi, HymnGrid.Build(midi, 0), row));
            }
            Assert.GreaterOrEqual(pool.Count, 6);

            for (uint seed = 0; seed < 10; seed++)
            {
                var round = new HymnRound(pool, seed, 2);
                var picks = HymnLadder.Select(pool, seed).Select(i => pool[i]).ToList();
                CollectionAssert.AreEqual(picks.Select(p => p.Name), round.Playlist);
                TestContext.Progress.WriteLine("seed {0}: {1}", seed,
                    string.Join(" ", picks.Select((p, i) => p.Name + "(" + p.Pattern + (p.Pickup ? "p" : "") + ")")));
                string why = "seed " + seed + ": " + string.Join(" ", picks.Select(p => p.Name));
                Assert.AreEqual(6, picks.Select(p => p.Name).Distinct().Count(), "repeat, " + why);
                Assert.IsFalse(picks[0].Pickup || picks[1].Pickup, "easy pickup, " + why);
                Assert.AreEqual(picks[0].Pattern, picks[1].Pattern, "easy pattern, " + why);
                Assert.AreNotEqual(picks[1].Pattern, picks[2].Pattern, "medium change, " + why);
                Assert.AreEqual(picks[2].Pattern, picks[3].Pattern, "medium keep, " + why);
                Assert.AreNotEqual(picks[3].Pattern, picks[4].Pattern, "hard change, " + why);
                Assert.AreNotEqual(picks[4].Pattern, picks[5].Pattern, "hard change, " + why);

                round.Start(0);
                Drive(round, round.PerfectPresses(), 0, round.EndTime + 1);
                Assert.IsTrue(round.Done, why);
                Assert.AreEqual("S", round.Result().Rank, why);
            }
        }
    }
}
