// Party mode bookkeeping, ported as-is from the web prototype's
// web/src/shell/state.js (partyPlaylist, cpuRound, session, rankPlaces).
//
// The minigames are single-player, so CPU rivals do not really play: each
// posts an accuracy centred on its skill with a seeded wobble, on the score
// scale the human's own run just set. A game that actually raced the lineup
// (Drumline Dash) reports a Field instead, and that order is the round.
// Places earn 3/2/1/0 points; points then round wins decide the party.

using System;
using System.Collections.Generic;
using System.Linq;

namespace RhythmGame.Core
{
    public sealed class PartyPlayer
    {
        public int Id;
        public string Name;
        public bool IsCpu;
        public double CpuSkill;
        public int Points, Wins;
    }

    public sealed class PartyPost
    {
        public int Id;
        public int? Score;
        public string Rank;
        public double Accuracy;
        public int Place, Points;
    }

    public sealed class PartyRoundLog
    {
        public string Game;
        public int Score;
        public string Rank;
        public List<PartyPost> Players;
        public bool Sim;
    }

    public static class Party
    {
        /// The closing game. A party always ends on it, whatever the shuffle says.
        public const string FinaleId = "finale-fever";
        public static readonly int[] PlacePoints = { 3, 2, 1, 0 };

        /// Roster slot types: (label, skill). Slot 0 is always the human.
        public static readonly (string label, double skill)[] CpuTiers = { ("EASY", 0.40), ("NORMAL", 0.63), ("HARD", 0.86) };

        /// `len` of `ids`, shuffled by the seeded rng, finale held for last.
        public static List<string> Playlist(IList<string> ids, int len, Rng rng)
        {
            var rest = ids.Where(id => id != FinaleId).ToList();
            for (int i = rest.Count - 1; i > 0; i--)
            {
                int j = (int)Math.Floor(rng.Next() * (i + 1));
                (rest[i], rest[j]) = (rest[j], rest[i]);
            }
            bool hasFinale = ids.Contains(FinaleId);
            int n = Math.Max(1, Math.Min(len, ids.Count));
            var list = rest.Take(hasFinale ? n - 1 : n).ToList();
            if (hasFinale) list.Add(FinaleId);
            return list;
        }

        static double JsRound(double v) => Math.Floor(v + 0.5);

        /// A CPU's round on the human's score scale (score grows ~ accuracy squared).
        public static PartyPost CpuRound(double skill, Rng rng, int refScore, double refAccuracy)
        {
            double wobble = (rng.Next() + rng.Next() - 1) * 0.17;
            double accuracy = Math.Min(0.995, Math.Max(0.2, 0.42 + skill * 0.56 + wobble));
            double refAcc = Math.Max(0.2, refAccuracy);
            double scale = refScore > 0 ? refScore / (refAcc * refAcc) : 10000;
            int score = (int)Math.Max(10, JsRound(scale * accuracy * accuracy / 10) * 10);
            int misses = accuracy >= 0.98 && rng.Next() < skill * 0.6 ? 0 : 1;
            return new PartyPost { Score = score, Accuracy = JsRound(accuracy * 1000) / 1000, Rank = Scoring.RankFor(accuracy, misses) };
        }

        /// Competition ranking: id -> 1-based place, higher value first, equal
        /// values share a place and the next is skipped (1, 1, 3).
        public static Dictionary<int, int> RankPlaces<T>(IEnumerable<T> rows, Func<T, int> id, Func<T, double> value)
        {
            var sorted = rows.OrderByDescending(value).ToList();   // stable, like Array.sort
            var o = new Dictionary<int, int>();
            for (int i = 0; i < sorted.Count; i++)
            {
                var r = sorted[i];
                o[id(r)] = i > 0 && value(sorted[i - 1]) == value(r) ? o[id(sorted[i - 1])] : i + 1;
            }
            return o;
        }
    }

    public sealed class PartySession
    {
        public readonly List<PartyPlayer> Players;
        public readonly List<string> Games;
        public readonly uint Seed;
        public int Index;
        public int Length => Games.Count;
        public readonly List<PartyRoundLog> Scores = new List<PartyRoundLog>();
        public List<int> Winners;

        public PartySession(IEnumerable<PartyPlayer> players, List<string> games, uint seed)
        {
            Players = players.Select((p, i) => { p.Id = i; p.Points = 0; p.Wins = 0; return p; }).ToList();
            Games = games;
            Seed = seed;
        }

        public string CurrentGame => Index < Games.Count ? Games[Index] : null;
        public bool Done => Index >= Length;
        public int HumanId => Players.FirstOrDefault(p => !p.IsCpu)?.Id ?? -1;

        public void RecordRound(string gameId, Outcome result)
        {
            int human = HumanId;
            var field = result.Field;
            bool inField = field != null && Players.All(p => field.Any(f => f.Id == p.Id));
            var posted = Players.Select(p =>
            {
                if (p.Id == human)
                    return new PartyPost { Id = p.Id, Score = result.Score, Rank = result.Rank ?? "D", Accuracy = result.Accuracy };
                if (inField) return new PartyPost { Id = p.Id };
                uint s = Seed ^ unchecked((uint)(Index + 1) * 0x9e3779b1u) ^ unchecked((uint)(p.Id + 1) * 0x85ebca6bu);
                var c = Party.CpuRound(p.IsCpu ? p.CpuSkill : 0.63, new Rng(s), result.Score, result.Accuracy);
                c.Id = p.Id;
                return c;
            }).ToList();

            var places = inField
                ? Party.RankPlaces(posted, r => r.Id, r => -field.First(f => f.Id == r.Id).Place)
                : Party.RankPlaces(posted, r => r.Id, r => r.Score ?? 0);
            foreach (var r in posted)
            {
                r.Place = places[r.Id];
                r.Points = r.Place - 1 < Party.PlacePoints.Length ? Party.PlacePoints[r.Place - 1] : 0;
                var p = Players.First(q => q.Id == r.Id);
                p.Points += r.Points;
                if (r.Place == 1) p.Wins++;
            }
            Scores.Add(new PartyRoundLog { Game = gameId, Score = result.Score, Rank = result.Rank, Players = posted, Sim = !inField });
            Index++;
            if (Done && Players.Count > 0)
            {
                var sp = StandingPlaces();
                Winners = Standings().Where(p => sp[p.Id] == 1).Select(p => p.Id).ToList();
            }
        }

        /// Best first; ties broken by wins, then id (for a stable order only).
        public List<PartyPlayer> Standings() =>
            Players.OrderByDescending(p => p.Points).ThenByDescending(p => p.Wins).ThenBy(p => p.Id).ToList();

        /// id -> place. Points then wins decide; level on both is a shared place.
        public Dictionary<int, int> StandingPlaces() => Party.RankPlaces(Players, p => p.Id, p => p.Points * 1000 + p.Wins);
    }
}
