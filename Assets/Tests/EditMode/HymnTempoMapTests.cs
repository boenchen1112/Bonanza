// Pins the Hymn Meter tempo map: the editable timing of a hymn. The grid the game judges
// against and the beat map the audio renderer reads are built from the same MIDI + map, so
// these tests check that editing the map moves both, and that no map (or an untouched one)
// changes nothing. Hymns are built in memory (TestHymns.cs); one optional test reads the real
// files and their tempo maps when BONANZA_HYMNS points at the private hymns folder.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Bonanza.Core;

namespace Bonanza.Core.Tests
{
    public class HymnTempoMapTests
    {
        static HymnMidi Hymn(int num = 4, int den = 4, int bars = 20, bool pickup = false, double bpm = 120) =>
            HymnMidi.Parse(SyntheticHymn.Build(num, den, bars, pickup, 2, bpm));

        // ---- the file format ---------------------------------------------------

        const string Sample = @"
{
  // a comment on its own line
  ""hymn"": ""hymn_058"",   /* inline */
  ""scale"": 1.25,
  ""pattern"": 4,
  ""tempo"": [
    { ""tick"": 0, ""bpm"": 90 },
    { ""tick"": 960, ""bpm"": 30, ""x"": 2.0, },
  ],
  ""meter"": [ { ""tick"": 0, ""sig"": ""1/4"" }, { ""tick"": 480, ""sig"": ""4/4"" } ],
  ""levels"": { ""easy"": { ""scale"": 0.5 }, ""medium"": {} }
}";

        [Test]
        public void ParsesCommentsTrailingCommasAndEveryField()
        {
            var m = HymnTempoMap.Parse(Sample);
            Assert.AreEqual("hymn_058", m.Hymn);
            Assert.AreEqual(1.25, m.Scale);
            Assert.AreEqual(4, m.Pattern);
            Assert.AreEqual(2, m.Tempo.Count);
            Assert.AreEqual(960, m.Tempo[1].Tick);
            Assert.AreEqual(2.0, m.Tempo[1].X);
            Assert.AreEqual(1.0, m.Tempo[0].X);
            Assert.AreEqual(2, m.Meter.Count);
            Assert.AreEqual(1, m.Meter[0].Numerator);
            Assert.AreEqual(4, m.Meter[1].Denominator);
            Assert.AreEqual(0.5, m.LevelScale[(int)HymnLevel.Easy]);
            Assert.AreEqual(1.0, m.LevelScale[(int)HymnLevel.Medium]);
            Assert.AreEqual("easy", m.VariantKey(HymnLevel.Easy));
            Assert.AreEqual("", m.VariantKey(HymnLevel.Medium));
            Assert.AreEqual("", m.VariantKey(HymnLevel.Hard));
        }

        [TestCase("[]")]
        [TestCase("{ \"scale\": 0 }")]
        [TestCase("{ \"scale\": \"fast\" }")]
        [TestCase("{ \"pattern\": 5 }")]
        [TestCase("{ \"tempo\": [] }")]
        [TestCase("{ \"tempo\": [ { \"tick\": 480, \"bpm\": 100 } ] }")]
        [TestCase("{ \"tempo\": [ { \"tick\": 0, \"bpm\": 100 }, { \"tick\": 0, \"bpm\": 90 } ] }")]
        [TestCase("{ \"tempo\": [ { \"tick\": 0, \"bpm\": -3 } ] }")]
        [TestCase("{ \"tempo\": [ { \"tick\": 0, \"bpm\": 100, \"x\": 0 } ] }")]
        [TestCase("{ \"meter\": [ { \"tick\": 0, \"sig\": \"four\" } ] }")]
        [TestCase("{ \"levels\": { \"easy\": { \"scale\": -1 } } }")]
        [TestCase("{ \"scale\": 1")]
        [TestCase("{ \"scale\": 1 } junk")]
        public void BadMapsAreRefusedWithAMessage(string text)
        {
            string err;
            HymnTempoMap m;
            Assert.IsFalse(HymnTempoMap.TryParse(text, out m, out err));
            Assert.IsNotEmpty(err);
        }

        // ---- the default stays the default ------------------------------------

        [Test]
        public void NoMapAndAnUntouchedMapChangeNothing()
        {
            var midi = Hymn(pickup: true, bpm: 100);
            var plain = HymnGrid.Build(midi, 0);
            foreach (var map in new[] { HymnTempoMap.Identity(), HymnTempoMap.Parse("{ \"scale\": 1.0 }"),
                                         HymnTempoMap.Parse("{ \"tempo\": [ { \"tick\": 0, \"bpm\": 100.0 } ], \"levels\": { \"hard\": { \"scale\": 1 } } }") })
            {
                var applied = map.Apply(midi);
                var grid = HymnGrid.Build(applied, 0);
                Assert.AreEqual(plain.Beats.Count, grid.Beats.Count);
                for (int i = 0; i < plain.Beats.Count; i++)
                {
                    Assert.AreEqual(plain.Beats[i].Time, grid.Beats[i].Time, 0, "beat " + i);   // exact: the file's own microseconds are kept
                    Assert.AreEqual(plain.Beats[i].BarPosition, grid.Beats[i].BarPosition);
                }
                if (map.Tempo == null) Assert.AreEqual(HymnTempoMap.Fingerprint(midi), HymnTempoMap.Fingerprint(applied));
            }
        }

        // ---- editing moves the grid ------------------------------------------

        [Test]
        public void TheGlobalScaleSpeedsEveryBeat()
        {
            var midi = Hymn();
            var plain = HymnGrid.Build(midi, 0);
            var fast = HymnGrid.Build(HymnTempoMap.Parse("{ \"scale\": 2 }").Apply(midi), 0);
            for (int i = 0; i < plain.Beats.Count; i++) Assert.AreEqual(plain.Beats[i].Time / 2, fast.Beats[i].Time, 1e-5, "beat " + i);
            Assert.AreEqual(plain.Beats[3].BarPosition, fast.Beats[3].BarPosition);
        }

        [Test]
        public void ASegmentMultiplierMovesOnlyWhatComesAfterIt()
        {
            var midi = Hymn(bpm: 120);   // a beat is 0.5 s
            var plain = HymnGrid.Build(midi, 0);
            var map = HymnTempoMap.Parse("{ \"tempo\": [ { \"tick\": 0, \"bpm\": 120 }, { \"tick\": 3840, \"bpm\": 120, \"x\": 0.5 } ] }");   // 8 beats in, half speed
            var g = HymnGrid.Build(map.Apply(midi), 0);
            for (int i = 0; i < 8; i++) Assert.AreEqual(plain.Beats[i].Time, g.Beats[i].Time, 1e-9, "before " + i);
            for (int i = 9; i < g.Beats.Count; i++) Assert.AreEqual(4 + (plain.Beats[i].Time - 4) * 2, g.Beats[i].Time, 1e-5, "after " + i);
        }

        [Test]
        public void ATempoEditedToAFermataLengthensTheBeatsInIt()
        {
            var midi = Hymn(bpm: 120);
            var map = HymnTempoMap.Parse("{ \"tempo\": [ { \"tick\": 0, \"bpm\": 120 }, { \"tick\": 1920, \"bpm\": 30 }, { \"tick\": 2400, \"bpm\": 120 } ] }");
            var g = HymnGrid.Build(map.Apply(midi), 0);
            Assert.AreEqual(2.0, g.Beats[5].Time - g.Beats[4].Time, 1e-5);     // 30 bpm: a beat is 2 s
            Assert.AreEqual(0.5, g.Beats[6].Time - g.Beats[5].Time, 1e-5);     // back to 120 after 1 beat
        }

        [Test]
        public void ThePatternOverrideConductsA68InThree()
        {
            var midi = Hymn(6, 8);
            Assert.AreEqual(2, HymnGrid.Build(midi, 0).Pattern);
            var map = HymnTempoMap.Parse("{ \"pattern\": 3 }");
            var row = HymnExcerptTable.Parse("h 2 no - - yes").Rows.Single();
            var c = HymnCandidate.From(midi, map, row);
            Assert.AreEqual(3, c.Pattern);
            Assert.AreEqual(3, c.Grid.Pattern);
        }

        [Test]
        public void AMeterOverrideRedefinesThePickup()
        {
            var midi = Hymn(pickup: true);
            Assert.IsTrue(HymnGrid.Build(midi, 0).HasPickup);
            var map = HymnTempoMap.Parse("{ \"meter\": [ { \"tick\": 0, \"sig\": \"4/4\" }, { \"tick\": 7680, \"sig\": \"4/4\" } ] }");
            Assert.IsFalse(HymnGrid.Build(map.Apply(midi), 0).HasPickup);
        }

        // ---- per-excerpt (level) overrides ----------------------------------------

        [Test]
        public void ALevelScaleMakesThatLevelItsOwnVariantWithItsOwnClip()
        {
            var midi = Hymn(bpm: 120);
            var map = HymnTempoMap.Parse("{ \"levels\": { \"hard\": { \"scale\": 0.5 } } }");
            var row = HymnExcerptTable.Parse("hymn_x 4 no - - yes").Rows.Single();
            var c = HymnCandidate.From(midi, map, row);

            Assert.AreEqual("hymn_x", c.ClipFor(HymnLevel.Easy));
            Assert.AreEqual("hymn_x", c.ClipFor(HymnLevel.Medium));
            Assert.AreEqual("hymn_x.hard", c.ClipFor(HymnLevel.Hard));

            HymnExcerpt easy, hard;
            string err;
            Assert.IsTrue(c.TryCut(HymnLevel.Easy, out easy, out err), err);
            Assert.IsTrue(c.TryCut(HymnLevel.Hard, out hard, out err), err);
            double easyBeat = easy.Beats[1].Time - easy.Beats[0].Time, hardBeat = hard.Beats[1].Time - hard.Beats[0].Time;
            Assert.AreEqual(0.5, easyBeat, 1e-6);
            Assert.AreEqual(1.0, hardBeat, 1e-6);   // half speed

            var round = new HymnRound(new List<HymnCandidate> { c }, 3, 0);
            round.Start(10);
            foreach (var s in round.Slots)
                Assert.AreEqual(s.Level == HymnLevel.Hard ? "hymn_x.hard" : "hymn_x", s.Clip, "slot " + s.Index);
        }

        // ---- the audio's beat map ------------------------------------------------

        static List<object> Beats(Dictionary<string, object> variant) => (List<object>)variant["beats"];

        [Test]
        public void TheBeatMapIsTheGridAndFollowsEveryEdit()
        {
            var midi = Hymn(pickup: true, bpm: 100);
            string before = HymnBeatMap.ToJson("h", midi, HymnTempoMap.Identity(), 0);
            string after = HymnBeatMap.ToJson("h", midi, HymnTempoMap.Parse("{ \"tempo\": [ { \"tick\": 0, \"bpm\": 100 }, { \"tick\": 2400, \"bpm\": 100, \"x\": 1.5 } ] }"), 0);

            var a = (Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)MiniJson.Parse(before))["variants"])["base"];
            var b = (Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)MiniJson.Parse(after))["variants"])["base"];

            // the map is exactly the grid the game builds
            var grid = HymnGrid.Build(midi, 0);
            Assert.AreEqual(grid.Beats.Count, Beats(a).Count);
            for (int i = 0; i < grid.Beats.Count; i++)
            {
                var row = (List<object>)Beats(a)[i];
                Assert.AreEqual(grid.Beats[i].Tick, (double)row[0]);
                Assert.AreEqual(grid.Beats[i].Time, (double)row[1], 1e-8, "beat " + i);
                Assert.AreEqual(grid.Beats[i].Bar, (double)row[2]);
                Assert.AreEqual(grid.Beats[i].BarPosition, (double)row[3]);
                Assert.AreEqual(grid.Beats[i].BeatsPerBar, (double)row[4]);
            }

            // the edit shows up in the map as it does in the grid
            var edited = HymnGrid.Build(HymnTempoMap.Parse("{ \"tempo\": [ { \"tick\": 0, \"bpm\": 100 }, { \"tick\": 2400, \"bpm\": 100, \"x\": 1.5 } ] }").Apply(midi), 0);
            Assert.AreEqual(edited.Beats.Count, Beats(b).Count);
            bool moved = false;
            for (int i = 0; i < edited.Beats.Count; i++)
            {
                double t = (double)((List<object>)Beats(b)[i])[1];
                Assert.AreEqual(edited.Beats[i].Time, t, 1e-8, "edited beat " + i);
                if (Math.Abs(t - (double)((List<object>)Beats(a)[i])[1]) > 1e-6) moved = true;
            }
            Assert.IsTrue(moved, "the edit moved no beat");
            Assert.AreNotEqual(a["fingerprint"], b["fingerprint"]);
            Assert.AreEqual(a["fingerprint"], ((Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)MiniJson.Parse(
                HymnBeatMap.ToJson("h", midi, HymnTempoMap.Identity(), 0)))["variants"])["base"])["fingerprint"], "deterministic");
        }

        [Test]
        public void TheBeatMapListsAVariantPerRetimedLevelAndItsTempoIsWhatTheAudioPlays()
        {
            var midi = Hymn(bpm: 120);
            var map = HymnTempoMap.Parse("{ \"levels\": { \"easy\": { \"scale\": 2 } } }");
            var root = (Dictionary<string, object>)MiniJson.Parse(HymnBeatMap.ToJson("h", midi, map, 4));
            var vars = (Dictionary<string, object>)root["variants"];
            CollectionAssert.AreEquivalent(new[] { "base", "easy" }, vars.Keys);
            var easy = (Dictionary<string, object>)vars["easy"];
            Assert.AreEqual("h.easy", easy["clip"]);
            var tempo0 = (List<object>)((List<object>)easy["tempo"])[0];
            Assert.AreEqual(250000, (double)tempo0[1]);   // 120 bpm doubled = 250000 us per quarter
        }

        // ---- the round's outcome carries a Field ----------------------------------------

        [Test]
        public void ThePartyOutcomeHasAFieldByTotalPoints()
        {
            var midi = Hymn(bars: 30);
            var row = HymnExcerptTable.Parse("hymn_x 4 no - - yes").Rows.Single();
            var pool = new List<HymnCandidate>();
            for (int i = 0; i < 6; i++) pool.Add(HymnCandidate.From(midi, HymnTempoMap.Identity(), new HymnExcerptRow { Hymn = "hymn_" + i, Pattern = 4, Use = true }));
            var r = new HymnRound(pool, 5, new[] { 0.9, 0.2, 0.5 });
            r.Start(0);
            var queue = new Queue<MeterPress>(r.PerfectPresses());
            for (double t = 0; t <= r.EndTime + 1; t += 0.01)
            {
                while (queue.Count > 0 && queue.Peek().Time <= t) { var p = queue.Dequeue(); r.Press(p.Action, p.Time); }
                r.Update(t);
            }
            var o = r.Result(new[] { 2, 0, 1, 3 });
            Assert.IsNotNull(o.Field);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, o.Field.Select(f => f.Id));
            Assert.AreEqual(0, o.Field.Single(f => f.Id == 2).Place, "a flawless human wins");
            Assert.IsNull(r.Result().Field, "no lineup ids, no field");
            Assert.IsTrue(Outcome.CheckField(o.Field, out _));
        }

        // ---- the real files and their tempo maps (optional) ------------------------------

        [Test]
        public void RealHymnTempoMapsBuildAndDefaultToTheFilesOwnGrid()
        {
            string dir = Environment.GetEnvironmentVariable("BONANZA_HYMNS");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || !Directory.Exists(Path.Combine(dir, "tempo")))
                Assert.Ignore("BONANZA_HYMNS with a tempo/ folder not set: real hymn files are private and not part of this repo.");
            var table = HymnExcerptTable.Parse(File.ReadAllText(Path.Combine(dir, "excerpts.txt")));
            int n = 0;
            foreach (var row in table.UsedRows)
            {
                string mid = Path.Combine(dir, row.Hymn + ".mid"), tm = Path.Combine(dir, "tempo", row.Hymn + ".json");
                if (!File.Exists(mid) || !File.Exists(tm)) continue;
                var midi = HymnMidi.Parse(File.ReadAllBytes(mid));
                var map = HymnTempoMap.Parse(File.ReadAllText(tm));
                var c = HymnCandidate.From(midi, map, row);
                Assert.IsTrue(c.CanCut(), row.Hymn);
                if (map.Scale == 1 && map.LevelScale.All(s => s == 1) && map.Tempo != null && map.Tempo.All(t => t.X == 1))
                {
                    var plain = HymnGrid.Build(midi, row.Pattern);
                    Assert.AreEqual(plain.Beats.Count, c.Grid.Beats.Count, row.Hymn);
                    for (int i = 0; i < plain.Beats.Count; i++) Assert.AreEqual(plain.Beats[i].Time, c.Grid.Beats[i].Time, 2e-4, row.Hymn + " beat " + i);   // map bpm text vs file microseconds
                }
                n++;
            }
            Assert.Greater(n, 0);
        }
    }
}
