using System.Linq;
using NUnit.Framework;

namespace Bonanza.Core.Tests
{
    // The cast table, the character-pick rules and the CPU pick order (web castData.js, roster.js).
    public class RosterTests
    {
        static string Dec(string s) => s.Replace('~', ' ');

        static int SeatOf(string t) => t == "you" ? RosterState.You : t == "off" ? RosterState.Off : t == "cpu-easy" ? 0 : t == "cpu-norm" ? 1 : 2;

        [Test]
        public void CastMatchesTheWebTable()
        {
            var rows = GoldenFile.Read("cast.txt");
            Assert.AreEqual(8, Cast.All.Count);
            Assert.AreEqual(rows.Count, Cast.All.Count);
            foreach (var g in rows)
            {
                var c = Cast.All[g.Int("index")];
                Assert.AreEqual(g.Str("id"), c.Id);
                Assert.AreEqual(g.Str("name"), c.Name);
                Assert.AreEqual(g.Int("color"), c.Color, c.Id);
                Assert.AreEqual(g.Int("accent"), c.Accent, c.Id);
                Assert.AreEqual(g.Str("shape"), c.Shape);
                Assert.AreEqual(g.Str("crest"), c.Crest);
                Assert.AreEqual(Dec(g.Str("trait")), c.Trait);
            }
        }

        [Test]
        public void CpuPicksAndPlayersMatchTheWebBuild()
        {
            foreach (var g in GoldenFile.Read("roster-picks.txt"))
            {
                var lineup = g.Str("lineup").Split(',');
                var r = new RosterState("party");
                for (int i = 0; i < 4; i++) r.Seat[i] = SeatOf(lineup[i]);
                r.Stage = RosterStage.Chars;
                r.Active = 0;
                var rng = new Rng((uint)g.Num("seed"));
                string at = $"{g.Str("lineup")} seed {g.Num("seed")} human {g.Str("humans")}";
                r.Cursor = int.Parse(g.Str("humans"));
                Assert.AreEqual(RosterEvent.Lock, r.Input("a").Event, at);
                var order = new System.Collections.Generic.List<int>();
                for (int guard = 0; r.Stage == RosterStage.Cpu && guard < 20; guard++)
                {
                    var o = r.Update(RosterState.CpuStep + 0.01, rng);
                    if (o.Event == RosterEvent.CpuLock) order.Add(o.Seat);
                }
                Assert.AreEqual(RosterStage.Go, r.Stage, at);
                Assert.AreEqual(g.Str("order"), order.Count == 0 ? "-" : string.Join(",", order), at);
                Assert.AreEqual(g.Str("picks"), string.Join(",", r.Picks.Select(p => p ?? "-")), at);
                var players = r.BuildPlayers().Select(p => $"{p.Slot}:{p.Name}:{p.Char}:{p.Palette}:{(p.IsCpu ? 1 : 0)}:{p.CpuSkill.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                Assert.AreEqual(g.Str("players"), string.Join(",", players), at);
            }
        }

        [Test]
        public void ACharacterCannotBeTakenTwice()
        {
            var r = new RosterState("free");
            r.Picks[1] = "bopp";   // somebody already holds the cursor's character
            var o = r.Input("a");
            Assert.AreEqual(RosterEvent.Miss, o.Event);
            Assert.AreEqual(1, o.Seat, "the holder, so the card can shake");
            Assert.IsNull(r.Picks[0]);
            Assert.AreEqual(RosterStage.Chars, r.Stage);
        }

        [Test]
        public void TheGridIsFourColumnsAndWraps()
        {
            var r = new RosterState("free");
            r.Input("down"); Assert.AreEqual(4, r.Cursor);
            r.Input("down"); Assert.AreEqual(0, r.Cursor);
            r.Input("left"); Assert.AreEqual(7, r.Cursor);
            r.Input("right"); Assert.AreEqual(0, r.Cursor);
            r.Input("up"); Assert.AreEqual(4, r.Cursor);
        }

        [Test]
        public void PartyStartsInTheLineupWithTheWebDefaults()
        {
            var r = new RosterState("party");
            Assert.AreEqual(RosterStage.Lineup, r.Stage);
            CollectionAssert.AreEqual(new[] { RosterState.You, 1, 1, RosterState.Off }, r.Seat);
            Assert.AreEqual(RosterEvent.Miss, r.Input("left").Event, "seat 0 is the human");
            Assert.AreEqual(RosterRules.HumanSeat, r.Input("left").Flash);
            r.Input("down");
            r.Input("right"); Assert.AreEqual(2, r.Seat[1]);   // normal -> hard
            r.Input("right"); Assert.AreEqual(RosterState.Off, r.Seat[1]);
            r.Input("right"); Assert.AreEqual(0, r.Seat[1]);   // off -> easy
        }

        [Test]
        public void ThereMustBeARivalToContinue()
        {
            var r = new RosterState("party");
            r.Seat[1] = r.Seat[2] = RosterState.Off;
            var o = r.Input("a");
            Assert.AreEqual(RosterEvent.Miss, o.Event);
            Assert.AreEqual(RosterRules.NeedRival, o.Flash);
            Assert.AreEqual(RosterStage.Lineup, r.Stage);
        }

        [Test]
        public void BUndoesTheLastHumanPickThenLeaves()
        {
            var r = new RosterState("party");
            r.Input("a");                                  // lineup -> chars
            Assert.AreEqual(RosterStage.Chars, r.Stage);
            r.Input("right"); r.Input("a");                // lock zizz; the rivals pick next
            Assert.AreEqual("zizz", r.Picks[0]);
            Assert.AreEqual(RosterStage.Cpu, r.Stage);
            Assert.AreEqual(RosterEvent.None, r.Input("b").Event, "no control while the rivals choose");
            var rng = new Rng(RosterState.DefaultSeed);
            while (r.Stage == RosterStage.Cpu) r.Update(0.4, rng);
            Assert.AreEqual(RosterStage.Go, r.Stage);
            var o = r.Input("b");
            Assert.AreEqual(RosterEvent.Unlock, o.Event);
            Assert.IsNull(r.Picks[0]);
            Assert.AreEqual(RosterStage.Chars, r.Stage);
            Assert.AreEqual(RosterEvent.Leave, r.Input("b").Event, "nothing left to undo");
        }

        [Test]
        public void FreePlayIsOneHumanOnePick()
        {
            var r = new RosterState("free");
            Assert.AreEqual(RosterStage.Chars, r.Stage);
            Assert.AreEqual(RosterEvent.Lock, r.Input("a").Event);
            Assert.AreEqual(RosterStage.Go, r.Stage, "no rivals to wait for");
            Assert.AreEqual(RosterEvent.Start, r.Input("a").Event);
            Assert.AreEqual("freeplay", r.ExitRoute.View);
            var p = r.BuildPlayers();
            Assert.AreEqual(1, p.Count);
            Assert.AreEqual("BOPP", p[0].Name);
            Assert.IsFalse(p[0].IsCpu);
        }

        [Test]
        public void TheHumanIsNamedAfterTheirCharacterUnlessTheyChoseAName()
        {
            var zizz = Cast.ById("zizz");
            Assert.AreEqual("ZIZZ", RosterState.HumanName(zizz, null));
            Assert.AreEqual("ZIZZ", RosterState.HumanName(zizz, "P1"));
            Assert.AreEqual("ZIZZ", RosterState.HumanName(zizz, "P4"));
            Assert.AreEqual("MAYA", RosterState.HumanName(zizz, "MAYA"));
            Assert.AreEqual("BOPP", Cast.ById("nope").Name, "unknown id falls back to the first");
        }

        [Test]
        public void AutoFlowSeatsEveryone()
        {
            var r = RosterState.Auto("party", new Rng(RosterState.DefaultSeed));
            Assert.AreEqual(RosterStage.Go, r.Stage);
            var p = r.BuildPlayers();
            Assert.AreEqual(3, p.Count);
            Assert.AreEqual(3, p.Select(x => x.Char).Distinct().Count());
            Assert.AreEqual("party", r.ExitRoute.View);
        }
    }
}
