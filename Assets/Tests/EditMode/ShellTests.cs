using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Bonanza.Core.Tests
{
    // Shell flow in Core: routing (web nav.js + nav.test.mjs), the title's career lines, the party
    // wrap-up headline, the roster rule and the Free Play rail.
    public class ShellTests
    {
        static string Dec(string s) => s.Replace('~', ' ');

        // ------------------------------------------------------------ routes (nav.test.mjs)

        [Test]
        public void TitleMenuMapsEachItemToItsScreen()
        {
            Assert.AreEqual(new Route("roster", mode: "party"), ShellNav.TitleMenuRoute("party"));
            Assert.AreEqual(new Route("roster", mode: "free"), ShellNav.TitleMenuRoute("free"));
            Assert.AreEqual(new Route("options"), ShellNav.TitleMenuRoute("options"));
        }

        [Test]
        public void RosterExitSendsALockedInLineupToTheRightScreen()
        {
            Assert.AreEqual("party", ShellNav.RosterExitRoute("party").View);
            Assert.AreEqual("freeplay", ShellNav.RosterExitRoute("free").View);
        }

        [Test]
        public void ExitMidPartyAlwaysWins()
        {
            Assert.AreEqual("party", ShellNav.ExitRoute(true, "freeplay", "swing-kings").View);
            Assert.AreEqual("party", ShellNav.ExitRoute(true, "select", "swing-kings").View);
        }

        [Test]
        public void ExitFromFreePlayFocusesTheGameJustPlayed()
        {
            Assert.AreEqual(new Route("freeplay", game: "swing-kings"), ShellNav.ExitRoute(false, "freeplay", "swing-kings"));
        }

        [Test]
        public void ExitFromAnythingElseFallsBackToTitle()
        {
            Assert.AreEqual("title", ShellNav.ExitRoute(false, "select", null).View);
            Assert.AreEqual("title", ShellNav.ExitRoute(false, null, null).View);
        }

        [Test]
        public void PartyConfirmStartsTheNextRoundOrWrapsUp()
        {
            Assert.AreEqual(new Route("play", game: "chomp-chorus", from: "party"), ShellNav.PartyConfirmRoute(false, "chomp-chorus"));
            Assert.AreEqual(new Route("title"), ShellNav.PartyConfirmRoute(true, null));
        }

        static string Opts(Route r)
        {
            var o = new List<string>();
            if (r.Mode != null) o.Add("mode:" + r.Mode);
            if (r.Game != null) o.Add("game:" + r.Game);
            if (r.From != null) o.Add("from:" + r.From);
            return o.Count == 0 ? "-" : string.Join(",", o);
        }

        [Test]
        public void EveryRouteMatchesTheWebBuild()
        {
            var rows = GoldenFile.Read("shell-routes.txt");
            Assert.AreEqual(23, rows.Count);
            foreach (var g in rows)
            {
                var a = g.Str("args").Split(',');
                string arg(int i) => a[i] == "-" ? null : a[i];
                Route r;
                switch (g.Str("fn"))
                {
                    case "titleMenuRoute": r = ShellNav.TitleMenuRoute(arg(0)); break;
                    case "rosterExitRoute": r = ShellNav.RosterExitRoute(arg(0)); break;
                    case "exitRoute": r = ShellNav.ExitRoute(arg(0) == "1", arg(1), arg(2)); break;
                    case "partyConfirmRoute": r = ShellNav.PartyConfirmRoute(arg(0) == "1", arg(1)); break;
                    default: Assert.Fail(g.Str("fn")); return;
                }
                string at = g.Str("fn") + "(" + g.Str("args") + ")";
                Assert.AreEqual(g.Str("view"), r.View, at);
                Assert.AreEqual(g.Str("opts"), Opts(r), at);
            }
        }

        // ------------------------------------------------------------ records on the title and the cards

        static readonly string[] Registry = { "swing-kings", "drumline-dash", "bounce-brigade", "chomp-chorus", "finale-fever" };

        // The same scripted records as Tools/golden/dump-charts.mjs, case by case.
        static GameRecord R(int score, string rank, double acc, int plays) => new GameRecord { Score = score, Rank = rank, Accuracy = acc, Plays = plays };
        static readonly Dictionary<string, GameRecord>[] BestCases =
        {
            new Dictionary<string, GameRecord>(),
            new Dictionary<string, GameRecord> { ["swing-kings"] = R(1234567, "D", 0.5, 3) },
            new Dictionary<string, GameRecord> { ["swing-kings"] = R(99000, "D", 0.9, 2), ["chomp-chorus"] = R(400, "B", 0.7, 1) },
            new Dictionary<string, GameRecord> { ["swing-kings"] = R(5000, "A", 0.91, 1), ["bounce-brigade"] = R(700, "A", 0.95, 4), ["finale-fever"] = R(300, "A", 0.95, 2) },
            new Dictionary<string, GameRecord> { ["drumline-dash"] = R(0, null, 0, 1), ["finale-fever"] = R(10, "C", 0.4, 1) },
            new Dictionary<string, GameRecord> { ["swing-kings"] = R(12, "S", 1, 1), ["drumline-dash"] = R(10, "S", 1, 1) },
            new Dictionary<string, GameRecord> { ["chomp-chorus"] = R(999, "S", 0.99, 9), ["drumline-dash"] = R(5, "S", 0.999, 1) },
            new Dictionary<string, GameRecord> { ["swing-kings"] = R(500, "B", 0.8, 0), ["finale-fever"] = R(800, "C", 0.6, 2) },
        };

        [Test]
        public void BestOverallAndTheTitleLinesMatchTheWebBuild()
        {
            var rows = GoldenFile.Read("shell-best.txt");
            Assert.AreEqual(BestCases.Length, rows.Count);
            foreach (var g in rows)
            {
                var c = BestCases[g.Int("case")];
                var list = Registry.Select(id => new KeyValuePair<string, GameRecord>(id, c.TryGetValue(id, out var r) ? r : default));
                var best = Records.BestOverall(list);
                string at = "case " + g.Int("case");
                Assert.AreEqual(g.Str("best"), best?.Name ?? "-", at);
                Assert.AreEqual(g.Str("rank"), best?.Rank ?? "-", at);
                var lines = Records.StatsLines(g.Int("rounds"), best);
                Assert.AreEqual(Dec(g.Str("line1")), lines[0], at);
                Assert.AreEqual(Dec(g.Str("line2")), lines[1], at);
            }
        }

        [Test]
        public void BestIsByRankThenAccuracyNotScore()
        {
            // Swing Kings counts in thousands: a D there must not beat a B elsewhere.
            var best = Records.BestOverall(new[]
            {
                new KeyValuePair<string, GameRecord>("SWING KINGS", R(1234567, "D", 0.9, 5)),
                new KeyValuePair<string, GameRecord>("CHOMP CHORUS", R(400, "B", 0.7, 1)),
            });
            Assert.AreEqual("CHOMP CHORUS", best.Value.Name);
        }

        [Test]
        public void NoRecordsYet()
        {
            Assert.IsNull(Records.BestOverall(new KeyValuePair<string, GameRecord>[0]));
            CollectionAssert.AreEqual(new[] { "0 ROUNDS PLAYED", "NO RECORDS YET" }, Records.StatsLines(0, null));
            Assert.AreEqual("1 ROUND PLAYED", Records.StatsLines(1, null)[0]);
        }

        [Test]
        public void ScoreFormatMatchesTheWebBuild()
        {
            foreach (var g in GoldenFile.Read("shell-score-format.txt"))
                Assert.AreEqual(g.Str("text"), Records.FormatScore(g.Int("n")));
        }

        [Test]
        public void FreePlayCardShowsRankBestAndPlays()
        {
            var fresh = Records.Card(default);
            Assert.AreEqual(("-", "-", "never played"), (fresh.Rank, fresh.Best, fresh.Plays));
            var one = Records.Card(R(12345, "A", 0.9, 1));
            Assert.AreEqual(("A", "12,345", "1 play"), (one.Rank, one.Best, one.Plays));
            Assert.AreEqual("7 plays", Records.Card(R(1, "D", 0, 7)).Plays);
            Assert.AreEqual("-", Records.Card(R(1, null, 0, 1)).Rank);   // played, never ranked
        }

        [Test]
        public void TheRailClampsAtItsEnds()
        {
            Assert.AreEqual(0, ShellNav.CarouselStep(0, -1, 5, out bool end)); Assert.IsTrue(end);
            Assert.AreEqual(4, ShellNav.CarouselStep(4, 1, 5, out end)); Assert.IsTrue(end);
            Assert.AreEqual(3, ShellNav.CarouselStep(4, -1, 5, out end)); Assert.IsFalse(end);
            Assert.AreEqual(1, ShellNav.CarouselStep(0, 1, 5, out end)); Assert.IsFalse(end);
        }

        // ------------------------------------------------------------ party hub headline

        static PartySession Standing(string seats)
        {
            var parts = seats.Split(',').Select(x => x.Split('/')).ToList();
            string[] names = { "YOU", "ZIZZ", "KWARK", "TUFF" };
            var s = new PartySession(parts.Select((_, k) => new PartyPlayer { Name = names[k], IsCpu = k > 0 }), new List<string>(), 1);
            for (int k = 0; k < parts.Count; k++) { s.Players[k].Points = int.Parse(parts[k][0]); s.Players[k].Wins = int.Parse(parts[k][1]); }
            return s;
        }

        [Test]
        public void TheHeadlineAndTiebreakMatchTheWebBuild()
        {
            var rows = GoldenFile.Read("party-headline.txt");
            Assert.AreEqual(7, rows.Count);
            foreach (var g in rows)
            {
                var h = Standing(g.Str("seats")).Headline();
                string at = "case " + g.Int("case");
                Assert.AreEqual(Dec(g.Str("title")), h.Title, at);
                Assert.AreEqual(g.Has("note") ? Dec(g.Str("note")) : null, h.Tiebreak, at);
            }
        }

        [Test]
        public void TheTiebreakSentenceIsExact()
        {
            Assert.AreEqual("Level on 6 points - 2 round wins to 1 breaks the tie", Standing("6/2,6/1,3/0,1/0").Headline().Tiebreak);
            Assert.AreEqual("YOU & ZIZZ SHARE THE CROWN!", Standing("6/2,6/2,3/0,1/0").Headline().Title);
        }

        // ------------------------------------------------------------ roster rule

        [Test]
        public void ARosterNeedsAtLeastOneRival()
        {
            int off = Party.CpuTiers.Length;
            Assert.AreEqual(RosterRules.NeedRival, RosterRules.Check(new[] { 0, off, off, off }));
            Assert.AreEqual("SEAT AT LEAST ONE RIVAL", RosterRules.NeedRival);
            Assert.IsNull(RosterRules.Check(new[] { 0, 1, off, off }));
            Assert.IsNull(RosterRules.Check(new[] { 0, off, off, 2 }));
            Assert.IsNull(RosterRules.Check(new[] { 0, 1, 1, off }));   // the default lineup
            Assert.AreEqual(0, RosterRules.Rivals(new[] { 2, off, off, off }), "slot 0 is the human, whatever its entry");
        }

        [Test]
        public void SeatZeroIsAlwaysTheHuman()
        {
            Assert.IsFalse(RosterRules.CanChange(0));
            for (int i = 1; i < RosterRules.Seats; i++) Assert.IsTrue(RosterRules.CanChange(i));
            Assert.IsFalse(RosterRules.CanChange(RosterRules.Seats));
        }

        // ------------------------------------------------------------ quit and restart in a party

        static PartySession FreshParty() => new PartySession(new[]
        {
            new PartyPlayer { Name = "YOU" },
            new PartyPlayer { Name = "ZIZZ", IsCpu = true, CpuSkill = 0.40 },
            new PartyPlayer { Name = "KWARK", IsCpu = true, CpuSkill = 0.63 },
        }, new List<string> { "chomp-chorus", "bounce-brigade", "drumline-dash", "finale-fever" }, 0xfeed1234);

        static Outcome Run(int score, double acc) => new Outcome { Score = score, Rank = "A", Accuracy = acc };

        [Test]
        public void ARoundQuitFromPauseLeavesThePartyWhereItWas()
        {
            var s = FreshParty();
            // Quitting records nothing: the index, the games and the points do not move,
            // so the hub offers the same round again.
            Assert.AreEqual("chomp-chorus", s.CurrentGame);
            Assert.AreEqual(0, s.Index);
            Assert.AreEqual(0, s.Scores.Count);
            Assert.IsFalse(s.Done);
            Assert.IsTrue(s.Players.All(p => p.Points == 0 && p.Wins == 0));
        }

        [Test]
        public void RestartingARoundRollsNothingAgain()
        {
            // A game's seed is the hash of its id alone.
            Assert.AreEqual(312558936u, Party.GameSeed("swing-kings"));
            Assert.AreEqual(Party.GameSeed("chomp-chorus"), Party.GameSeed("chomp-chorus"));
            Assert.AreNotEqual(Party.GameSeed("chomp-chorus"), Party.GameSeed("drumline-dash"));

            // The CPUs' rolls come from (party seed, round index, slot) and the human's result.
            // Abandoned attempts change none of those: only RecordRound advances the index.
            var once = FreshParty();
            once.RecordRound(once.CurrentGame, Run(700, 0.9));

            var again = FreshParty();   // the same round played again from the same state
            again.RecordRound(again.CurrentGame, Run(700, 0.9));

            var a = once.Scores[0].Players; var b = again.Scores[0].Players;
            for (int i = 0; i < a.Count; i++)
                Assert.AreEqual((a[i].Score, a[i].Rank, a[i].Accuracy, a[i].Place, a[i].Points), (b[i].Score, b[i].Rank, b[i].Accuracy, b[i].Place, b[i].Points), "slot " + i);

            // And the rolls of slot k in round r do not depend on the other rounds having been played.
            var second = FreshParty();
            second.RecordRound(second.CurrentGame, Run(700, 0.9));
            second.RecordRound(second.CurrentGame, Run(650, 0.88));
            var direct = FreshParty();
            direct.Index = 1;
            direct.RecordRound(direct.CurrentGame, Run(650, 0.88));
            for (int i = 1; i < 3; i++)
                Assert.AreEqual(second.Scores[1].Players[i].Score, direct.Scores[0].Players[i].Score, "CPU " + i + " round 1");
        }

        // ------------------------------------------------------------ the one-time performance hint

        [Test]
        public void ThePerfHintIsPendingAfterTwoStrikesAtTheFloor()
        {
            int strikes = 0;
            string saved = "";
            saved = PerfHint.OnFloorOverBudget(ref strikes, saved);
            Assert.IsFalse(PerfHint.ShouldShow(saved));
            saved = PerfHint.OnFloorOverBudget(ref strikes, saved);
            Assert.AreEqual(PerfHint.Pending, saved);
            Assert.IsTrue(PerfHint.ShouldShow(saved));
        }

        [Test]
        public void AShownHintIsNotRaisedAgain()
        {
            int strikes = 5;
            Assert.AreEqual(PerfHint.Shown, PerfHint.OnFloorOverBudget(ref strikes, PerfHint.Shown));
            Assert.IsFalse(PerfHint.ShouldShow(PerfHint.Shown));
        }

        [Test]
        public void AutoRemembersItsLevelPerRenderer()
        {
            string a = GraphicsPolicy.LevelKey("Intel Iris Xe"), b = GraphicsPolicy.LevelKey("NVIDIA GeForce RTX 4070");
            Assert.AreNotEqual(a, b);
            Assert.IsTrue(a.StartsWith("rg:"));
            Assert.AreEqual(3, GraphicsPolicy.AutoStart("Intel Iris Xe", 3));
        }

        // ------------------------------------------------------------ save: the party seed counter

        static readonly string[] SaveKeys =
        {
            "rg:parties", Save.KeyPartySeed, "rg:rounds", "rg:wins", "rg:notes", GraphicsPolicy.LevelKey("test-gpu"),
            "rg:test-game:best", "rg:test-game:rank", "rg:test-game:acc", "rg:test-game:combo", "rg:test-game:plays",
        };
        Dictionary<string, int?> _prefs;

        [SetUp] public void SavePrefs()
        {
            _prefs = SaveKeys.ToDictionary(k => k, k => PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : (int?)null);
            foreach (var k in SaveKeys) PlayerPrefs.DeleteKey(k);
        }
        [TearDown] public void RestorePrefs()
        {
            foreach (var kv in _prefs) { if (kv.Value.HasValue) PlayerPrefs.SetInt(kv.Key, kv.Value.Value); else PlayerPrefs.DeleteKey(kv.Key); }
        }

        [Test]
        public void EachPartyGetsItsOwnSeedAndCountsInTheCareerStat()
        {
            Assert.AreEqual(0, Save.BeginParty());
            Assert.AreEqual(1, Save.BeginParty());
            Assert.AreEqual(2, Save.PartySeed);
            Assert.AreEqual(2, Save.Parties);
        }

        [Test]
        public void ResetProgressDoesNotRepeatAnEarlierSeed()
        {
            Save.BeginParty(); Save.BeginParty(); Save.BeginParty();
            Save.ResetProgress(new[] { "test-game" });
            Assert.AreEqual(0, Save.Parties, "the career stat resets");
            Assert.AreEqual(3, Save.BeginParty(), "the next party is not party 0 again");
            Assert.AreEqual(1, Save.Parties);
        }

        [Test]
        public void AnOlderSaveSuppliesTheFirstSeed()
        {
            PlayerPrefs.SetInt("rg:parties", 4);   // written by a build that had only the one counter
            Assert.AreEqual(4, Save.PartySeed);
            Assert.AreEqual(4, Save.BeginParty());
            Assert.AreEqual(5, Save.PartySeed);
            Assert.AreEqual(5, Save.Parties);
        }

        [Test]
        public void ResettingBeforeTheFirstNewPartyStillKeepsTheOldCount()
        {
            PlayerPrefs.SetInt("rg:parties", 4);
            Save.ResetProgress(new[] { "test-game" });
            Assert.AreEqual(4, Save.BeginParty());
        }

        [Test]
        public void ResetProgressKeepsTheRememberedGraphicsLevel()
        {
            PlayerPrefs.SetInt(GraphicsPolicy.LevelKey("test-gpu"), 3);
            Save.ResetProgress(new[] { "test-game" });
            Assert.AreEqual(3, PlayerPrefs.GetInt(GraphicsPolicy.LevelKey("test-gpu"), -1));
        }
    }
}
