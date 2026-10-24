// The party standings as a screen shows them: each player's points before and after the last round (so the hub can
// count the delta in and slide the rows), places with ties, and the wrap-up's stat line. Pure view data derived from
// a PartySession; nothing here changes a rule.

using System.Collections.Generic;
using System.Linq;

namespace Bonanza.Core
{
    public sealed class PartyBoardRow
    {
        public int Id;
        public string Name, Char;
        public bool IsCpu;
        public int Points, Wins;
        /// 1-based place now, shared on a tie.
        public int Place;
        public bool Tied;
        /// Before the last recorded round (equal to the current values when there is none).
        public int PrevPoints, PrevPlace, PrevRank;
        /// Points the last recorded round paid this player.
        public int Delta;
        /// This player's place in the last recorded round (0 when none).
        public int LastPlace;
        /// Row index now (0 = top) and before the last round.
        public int Rank;
    }

    public static class PartyBoard
    {
        /// Rows in standing order (best first).
        public static List<PartyBoardRow> Build(PartySession s)
        {
            var last = s.Scores.Count > 0 ? s.Scores[s.Scores.Count - 1] : null;
            var places = s.StandingPlaces();
            var rows = s.Standings().Select((p, i) =>
            {
                var post = last?.Players.FirstOrDefault(q => q.Id == p.Id);
                int delta = post?.Points ?? 0;
                bool won = post != null && post.Place == 1;
                return new PartyBoardRow
                {
                    Id = p.Id, Name = p.Name, Char = p.Char, IsCpu = p.IsCpu,
                    Points = p.Points, Wins = p.Wins, Place = places[p.Id],
                    Tied = s.Players.Count(q => places[q.Id] == places[p.Id]) > 1,
                    PrevPoints = p.Points - delta, Delta = delta,
                    LastPlace = post?.Place ?? 0, Rank = i,
                    PrevPlace = 0, PrevRank = i,
                };
            }).ToList();
            // order and places as they stood before the last round
            var before = rows.Select(r => (r, wins: r.Wins - (r.LastPlace == 1 ? 1 : 0))).ToList();
            var prevPlaces = Party.RankPlaces(before, b => b.r.Id, b => b.r.PrevPoints * 1000 + b.wins);
            var prevOrder = before.OrderByDescending(b => b.r.PrevPoints).ThenByDescending(b => b.wins).ThenBy(b => b.r.Id).ToList();
            foreach (var r in rows)
            {
                r.PrevPlace = prevPlaces[r.Id];
                r.PrevRank = prevOrder.FindIndex(b => b.r.Id == r.Id);
            }
            return rows;
        }

        /// "BOPP TOOK 3 OF 4 ROUNDS": the top player's round wins.
        public static string StatLine(PartySession s, IList<PartyBoardRow> rows)
        {
            if (rows.Count == 0) return "";
            var top = rows[0];
            int n = s.Scores.Count;
            return $"{top.Name} TOOK {top.Wins} OF {n} ROUND{(n == 1 ? "" : "S")}";
        }

        /// The human's best rank across the rounds ("S" best .. "D"), or null when there is none.
        public static string BestRank(PartySession s, out string game)
        {
            string order = "SABCD";
            string best = null; game = null;
            int human = s.HumanId;
            foreach (var r in s.Scores)
            {
                var post = r.Players.FirstOrDefault(p => p.Id == human);
                if (post?.Rank == null) continue;
                int at = order.IndexOf(post.Rank);
                if (at < 0) continue;
                if (best == null || at < order.IndexOf(best)) { best = post.Rank; game = r.Game; }
            }
            return best;
        }
    }
}
