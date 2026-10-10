// Per-game records and career stats (web shell/state.js profile.submit).
// Each field keeps its own best: a later run with a lower score can still
// set a better rank or accuracy.

using System;

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

        /// A round counts its judged hits (perfect + great + good) as notes.
        public static CareerStats AddRound(CareerStats s, Outcome o)
        {
            s.Rounds++;
            s.Notes += o.Perfect + o.Great + o.Good;
            return s;
        }
    }
}
