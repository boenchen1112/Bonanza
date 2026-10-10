using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;

namespace Bonanza.Core.Tests
{
    public class DrumlineDashTests
    {
        static List<double> Beats(string s) => s.Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToList();

        [Test]
        public void ChartMatchesTheWebBuild()
        {
            var golden = GoldenFile.Read("drumline-dash-chart.txt");
            var chart = DrumChart.Build();
            Assert.AreEqual(golden.Count, chart.Count);
            for (int i = 0; i < chart.Count; i++)
            {
                var g = golden[i]; var p = chart[i];
                Assert.AreEqual(g.Int("index"), p.Index);
                Assert.AreEqual(g.Str("section"), p.Section);
                Assert.AreEqual(g.Int("bars"), p.Bars);
                Assert.AreEqual(g.Num("callBeat"), p.CallBeat);
                Assert.AreEqual(g.Num("respBeat"), p.RespBeat);
                Assert.AreEqual(g.Num("endBeat"), p.EndBeat);
                Assert.AreEqual(g.Flag("finale"), p.Finale);
                CollectionAssert.AreEqual(Beats(g.Str("callBeats")), p.CallBeats, $"phrase {i} call");
                CollectionAssert.AreEqual(Beats(g.Str("noteBeats")), p.NoteBeats, $"phrase {i} notes");
            }
            Assert.AreEqual(96000, DrumChart.MaxPoints(chart));
            Assert.AreEqual(156, DrumChart.EndBeat(chart));
            Assert.AreEqual(39, DrumChart.DurationBars(chart));
        }

        /// Plays every response note on time; returns the round after it ends.
        static DrumlineRound PlayPerfect(List<(int phrase, int racer, double adv, bool stumbled)> log = null)
        {
            var round = new DrumlineRound();
            if (log != null)
                round.PhraseClosed += (p, q) =>
                {
                    for (int i = 1; i < round.Racers.Count; i++)
                        log.Add((p.Index, i, round.Racers[i].LastAdvance, round.Racers[i].Stumbled));
                };
            round.Start(5);
            var notes = round.Phrases.SelectMany(p => p.NoteBeats).Select(b => round.Map.TimeAt(b)).ToList();
            // Step the clock at 60 Hz, pressing each note on the first frame at or after it.
            int k = 0;
            for (double t = 5; t < round.Map.TimeAt(DrumChart.EndBeat(round.Phrases)) && !round.Finished; t += 1 / 60.0)
            {
                while (k < notes.Count && notes[k] <= t) { round.Press(t); k++; }
                round.Update(t);
            }
            return round;
        }

        [Test]
        public void CpuRaceMatchesTheWebBuild()
        {
            var golden = GoldenFile.Read("drumline-dash-race.txt");
            var log = new List<(int phrase, int racer, double adv, bool stumbled)>();
            PlayPerfect(log);
            Assert.AreEqual(golden.Count, log.Count);
            for (int i = 0; i < log.Count; i++)
            {
                Assert.AreEqual(golden[i].Int("phrase"), log[i].phrase);
                Assert.AreEqual(golden[i].Int("racer"), log[i].racer);
                Assert.AreEqual(golden[i].Num("advance"), log[i].adv, 1e-12, $"row {i}");
                Assert.AreEqual(golden[i].Flag("stumbled"), log[i].stumbled, $"row {i}");
            }
        }

        [Test]
        public void PerfectRunWinsWithRankS()
        {
            var round = PlayPerfect();
            var o = round.Result();
            Assert.NotNull(o);
            int notes = round.Phrases.Sum(p => p.NoteBeats.Count);
            Assert.AreEqual(notes, o.Perfect);
            Assert.AreEqual(0, o.Miss);
            Assert.AreEqual("S", o.Rank);
            Assert.AreEqual(1000, o.Score);
            Assert.AreEqual(1, round.You.Place);
        }

        [Test]
        public void MashingCostsPointsAndGround()
        {
            var round = new DrumlineRound();
            round.Start(0);
            // Into the first response bar (beats 0..4), hammer sixteenths.
            double t0 = round.Map.TimeAt(-0.5);
            round.Update(t0);
            Assert.AreEqual(DrumMode.Call, round.Mode);
            for (double b = 0; b < 4; b += 0.25)
            {
                double t = round.Map.TimeAt(b);
                round.Press(t);
                round.Update(t);
            }
            Assert.Greater(round.Fumbles, 0);
            round.Update(round.Map.TimeAt(4.01));
            Assert.AreEqual(0.30, round.You.Progress, 1e-9, "all advance burned: only the floor is left");
        }

        [Test]
        public void PressesInTheLeadInAreFree()
        {
            var round = new DrumlineRound();
            round.Start(0);
            round.Update(round.Map.TimeAt(-6));
            Assert.IsFalse(round.Press(round.Map.TimeAt(-6)));
            Assert.AreEqual(0, round.Fumbles);
        }

        [Test]
        public void APartyRacesOnlyItsOwnLineup()
        {
            var lineup = new System.Collections.Generic.List<PartyPlayer>
            {
                new PartyPlayer { Id = 0, Name = "YOU" },
                new PartyPlayer { Id = 1, Name = "ZIZZ", IsCpu = true, CpuSkill = 0.40 },
                new PartyPlayer { Id = 2, Name = "TUFF", IsCpu = true, CpuSkill = 0.86 },
            };
            var round = new DrumlineRound(0xd12b, 0, lineup);
            Assert.AreEqual(3, round.Racers.Count, "the spare lane stays empty");
            Assert.AreEqual("ZIZZ", round.Racers[1].Name);
            Assert.AreEqual(0.58 + 0.37 * 0.40, round.Racers[1].Skill, 1e-12);
            Assert.AreEqual(0.28 - 0.23 * 0.86, round.Racers[2].StumbleChance, 1e-12);

            round.Start(0);
            for (double t = 0; !round.Finished && t < 200; t += 1 / 60.0) round.Update(t);
            var o = round.Result();
            Assert.NotNull(o.Field, "a valid field");
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, o.Field.Select(f => f.Id));
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, o.Field.Select(f => f.Place));
        }
    }
}
