// Per-game records and career stats (web shell/state.js profile.submit).
// Each field keeps its own best: a later run with a lower score can still
// set a better rank or accuracy.

using System;
using System.Collections.Generic;

namespace Bonanza.Core
{
    public struct GameRecord
    {
        public int Score;
        public string Rank;      // null until a ranked run
        public double Accuracy;
        public int Plays;
        public int MaxCombo;
    }

    public struct BestGame
    {
        public string Name, Rank;
        public int Score;
        public double Accuracy;
    }

    public struct CardRecord
    {
        public string Rank, Best, Plays;
    }

    public struct CareerStats
    {
        public int Rounds, Parties, Wins, Notes;
    }

    public static class Records
    {
        public static readonly string[] RankOrder = { "S", "A", "B", "C", "D" };

        /// Fold a finished run into a record. `newRank` follows the web build:
        /// true on the first ranked run, or when the rank beats the stored one.
        public static GameRecord Submit(GameRecord prev, Outcome o, out bool newScore, out bool newRank)
        {
            newScore = o.Score > prev.Score;
            string rank = string.IsNullOrEmpty(o.Rank) ? null : o.Rank;
            newRank = rank != null && (prev.Rank == null
                      || Array.IndexOf(RankOrder, rank) < Array.IndexOf(RankOrder, prev.Rank));
            return new GameRecord
            {
                Score = Math.Max(prev.Score, o.Score),
                Rank = newRank || prev.Rank == null ? (rank ?? prev.Rank) : prev.Rank,
                Accuracy = Math.Max(prev.Accuracy, o.Accuracy),
                Plays = prev.Plays + 1,
                MaxCombo = Math.Max(prev.MaxCombo, o.MaxCombo),
            };
        }

        /// Thousands separators (web theme.js fmtScore).
        public static string FormatScore(int n)
        {
            string s = Math.Max(0, n).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var o = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && (s.Length - i) % 3 == 0) o.Append(',');
                o.Append(s[i]);
            }
            return o.ToString();
        }

        static int RankIdx(string r) { int i = Array.IndexOf(RankOrder, r); return i < 0 ? RankOrder.Length : i; }

        /// The player's best game by rank, then accuracy, never by score: each game keeps its own
        /// scale (web title.js bestOverall). Games are given in registry order and the first of
        /// equals wins. Null if nothing has been played.
        public static BestGame? BestOverall(IEnumerable<KeyValuePair<string, GameRecord>> records)
        {
            BestGame? best = null;
            foreach (var kv in records)
            {
                var r = kv.Value;
                if (r.Plays <= 0) continue;
                var cand = new BestGame { Name = kv.Key, Rank = r.Rank ?? "-", Score = r.Score, Accuracy = r.Accuracy };
                if (best == null || RankIdx(cand.Rank) < RankIdx(best.Value.Rank)
                    || (RankIdx(cand.Rank) == RankIdx(best.Value.Rank) && cand.Accuracy > best.Value.Accuracy))
                    best = cand;
            }
            return best;
        }

        /// The title's career lines (web title.js), ASCII only for the IMGUI font.
        public static string[] StatsLines(int rounds, BestGame? best) => new[]
        {
            $"{rounds} ROUND{(rounds == 1 ? "" : "S")} PLAYED",
            best.HasValue ? $"BEST: {best.Value.Name} - {best.Value.Rank} - {FormatScore(best.Value.Score)}" : "NO RECORDS YET",
        };

        /// A Free Play card's record row (web freeplay.js): rank, best score, plays.
        public static CardRecord Card(GameRecord r) => new CardRecord
        {
            Rank = r.Plays > 0 ? (r.Rank ?? "-") : "-",
            Best = r.Plays > 0 ? FormatScore(r.Score) : "-",
            Plays = r.Plays > 0 ? $"{r.Plays} play{(r.Plays > 1 ? "s" : "")}" : "never played",
        };

        /// A round counts its judged hits (perfect + great + good) as notes.
        public static CareerStats AddRound(CareerStats s, Outcome o)
        {
            s.Rounds++;
            s.Notes += o.Perfect + o.Great + o.Good;
            return s;
        }
    }
}
