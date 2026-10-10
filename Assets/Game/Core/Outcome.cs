// The round result contract, ported from web/src/core/result.js.
//
// `Accuracy` is HIT QUALITY in every game: verdict-weighted, 0..1, the number
// printed beside PERFECT/GREAT/GOOD/MISS. A game whose score is something else
// (Drumline's race points) keeps that in `Score`, never in `Accuracy`.
//
// `Field` only from a game that actually raced the lineup: one (Id, Place) per
// competitor, places starting at 0 and never past the field size. Null means
// "the party simulates the CPU rounds".

using System;
using System.Collections.Generic;

namespace Bonanza.Core
{
    public struct FieldRow
    {
        public int Id;       // lineup slot, -1 for a house extra
        public int Place;    // 0 = first; shared places are a dead heat
    }

    public sealed class Outcome
    {
        public int Score;
        public double Accuracy;
        public string Rank;
        public int Perfect, Great, Good, Miss, MaxCombo;
        public double BiasMs;
        /// Game-specific stats, in display order ("HOME RUNS" -> "3"). Also logged.
        public readonly List<KeyValuePair<string, string>> Extras = new List<KeyValuePair<string, string>>();
        public List<FieldRow> Field;

        public Outcome Extra(string key, object value)
        {
            Extras.Add(new KeyValuePair<string, string>(key, Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)));
            return this;
        }

        /// The judge's counts, accuracy and the standard rank.
        public static Outcome FromJudge(NoteJudge judge, double score)
        {
            var s = judge.Stats;
            double acc = judge.Accuracy;
            return Build(score, acc, Scoring.RankFor(acc, s.Miss), s, judge.Bias, null);
        }

        /// roundResult(): the only way a game builds its result.
        public static Outcome Build(double score, double accuracy, string rank, JudgeStats s, double biasMs, List<FieldRow> field)
        {
            if (double.IsNaN(score) || double.IsInfinity(score)) score = 0;
            if (double.IsNaN(accuracy) || double.IsInfinity(accuracy)) accuracy = 0;
            accuracy = accuracy < 0 ? 0 : accuracy > 1 ? 1 : accuracy;
            if (!CheckField(field, out _)) field = null;
            return new Outcome
            {
                Score = (int)Math.Round(score, MidpointRounding.AwayFromZero),
                Accuracy = accuracy,
                Rank = rank,
                Perfect = s?.Perfect ?? 0, Great = s?.Great ?? 0, Good = s?.Good ?? 0, Miss = s?.Miss ?? 0,
                MaxCombo = s?.MaxCombo ?? 0,
                BiasMs = Math.Round(biasMs * 10) / 10,
                Field = field,
            };
        }

        public static bool CheckField(List<FieldRow> field, out string why)
        {
            why = null;
            if (field == null) return true;
            if (field.Count == 0) { why = "field must be non-empty or null"; return false; }
            int min = int.MaxValue, max = int.MinValue;
            foreach (var r in field)
            {
                if (r.Place < 0) { why = "every field row needs a place >= 0"; return false; }
                min = Math.Min(min, r.Place);
                max = Math.Max(max, r.Place);
            }
            if (min != 0) { why = "places must start at 0"; return false; }
            if (max >= field.Count) { why = "a place is beyond the size of the field"; return false; }
            return true;
        }

        /// One log line: "score=.. rank=.. accuracy=.. perfect=.. ... key=value".
        public string LogLine()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"score={Score} rank={Rank} accuracy={Accuracy.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)} ");
            sb.Append($"perfect={Perfect} great={Great} good={Good} miss={Miss} maxCombo={MaxCombo} biasMs={BiasMs.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            foreach (var kv in Extras) sb.Append(' ').Append(kv.Key.Replace(' ', '_').ToLowerInvariant()).Append('=').Append(kv.Value.Replace(' ', '_'));
            if (Field != null)
            {
                sb.Append(" field=");
                for (int i = 0; i < Field.Count; i++) sb.Append(i > 0 ? "," : "").Append(Field[i].Id).Append(':').Append(Field[i].Place);
            }
            return sb.ToString();
        }
    }
}
