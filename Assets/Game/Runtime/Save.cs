// Save state (web shell/state.js + util.js Save): a record per game (best
// score, rank, accuracy and combo, each kept on its own; play count), career
// stats, and settings. PlayerPrefs maps to IndexedDB in a WebGL build and never
// throws, so a private window just starts fresh.

using Bonanza.Core;
using UnityEngine;

namespace Bonanza
{
    public static class Save
    {
        const string Prefix = "rg:";

        static readonly string[] RecordKeys = { ":best", ":rank", ":acc", ":combo", ":plays" };

        public static GameRecord Get(string gameId)
        {
            string k = Prefix + gameId;
            string rank = PlayerPrefs.GetString(k + ":rank", "");
            double.TryParse(PlayerPrefs.GetString(k + ":acc", "0"), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double acc);
            return new GameRecord
            {
                Score = PlayerPrefs.GetInt(k + ":best", 0),
                Rank = rank == "" || rank == "-" ? null : rank,
                Accuracy = acc,
                Plays = PlayerPrefs.GetInt(k + ":plays", 0),
                MaxCombo = PlayerPrefs.GetInt(k + ":combo", 0),
            };
        }

        /// Fold a finished round into the game's record and the career stats.
        public static void Record(string gameId, Outcome o, out bool newScore, out bool newRank)
        {
            var r = Records.Submit(Get(gameId), o, out newScore, out newRank);
            string k = Prefix + gameId;
            PlayerPrefs.SetInt(k + ":best", r.Score);
            PlayerPrefs.SetString(k + ":rank", r.Rank ?? "");
            PlayerPrefs.SetString(k + ":acc", r.Accuracy.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            PlayerPrefs.SetInt(k + ":combo", r.MaxCombo);
            PlayerPrefs.SetInt(k + ":plays", r.Plays);
            SetStats(Records.AddRound(Stats, o));
        }

        /// "12345  A  98.5%" or null if never played.
        public static string Best(string gameId)
        {
            var r = Get(gameId);
            if (r.Plays == 0) return null;
            return $"{r.Score}  {r.Rank ?? "-"}  {r.Accuracy * 100:0.0}%";
        }

        public static CareerStats Stats => new CareerStats
        {
            Rounds = PlayerPrefs.GetInt(Prefix + "rounds", 0),
            Parties = PlayerPrefs.GetInt(Prefix + "parties", 0),
            Wins = PlayerPrefs.GetInt(Prefix + "wins", 0),
            Notes = PlayerPrefs.GetInt(Prefix + "notes", 0),
        };

        static void SetStats(CareerStats s)
        {
            PlayerPrefs.SetInt(Prefix + "rounds", s.Rounds);
            PlayerPrefs.SetInt(Prefix + "parties", s.Parties);
            PlayerPrefs.SetInt(Prefix + "wins", s.Wins);
            PlayerPrefs.SetInt(Prefix + "notes", s.Notes);
            PlayerPrefs.Save();
        }

        /// The human won (or shared) a party.
        public static void AddWin() { var s = Stats; s.Wins++; SetStats(s); }

        /// Player timing calibration in ms, ADDED to press times. Kept here for the timing
        /// path; the Options rows live in Settings.
        public static float OffsetMs
        {
            get => Settings.OffsetMs;
            set => Settings.OffsetMs = value;
        }

        /// Parties started, ever: seeds each party's shuffle and CPU rolls.
        public static int Parties
        {
            get => PlayerPrefs.GetInt(Prefix + "parties", 0);
            set { PlayerPrefs.SetInt(Prefix + "parties", value); PlayerPrefs.Save(); }
        }

        /// Options' RESET PROGRESS: records and career stats. Settings survive it.
        public static void ResetProgress(System.Collections.Generic.IEnumerable<string> gameIds)
        {
            foreach (var id in gameIds)
                foreach (var k in RecordKeys) PlayerPrefs.DeleteKey(Prefix + id + k);
            SetStats(default);
        }
    }
}
