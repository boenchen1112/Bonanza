// Hymn Meter — the beat map of a hymn, written as JSON for the audio render script
// (Tools/hymns/render-hymns.mjs). The conducted grid comes from HymnGrid, the same code the
// game runs, built from the MIDI plus the tempo map; the render then checks its own
// tick-to-seconds against these beat times, so the audio and the grid cannot drift apart
// unnoticed.
//
// One entry per variant: "base" and one per level the tempo map re-times ("easy", ...).
// Beats are [tick, seconds, bar, position in bar, beats per bar] over verse 1, `tempo` the
// effective tempo changes (tick, microseconds per quarter) after the map, `notes` the count of
// notes that start in verse 1 (the renderer checks its own MIDI read against it).
//
// No UnityEngine here: this assembly is pure C# so it stays test-pinned.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Bonanza.Core
{
    public static class HymnBeatMap
    {
        public static string ToJson(string hymn, HymnMidi file, HymnTempoMap map, int tablePattern)
        {
            int pattern = map.Pattern > 0 ? map.Pattern : tablePattern;
            var sb = new StringBuilder();
            sb.Append("{\n  \"hymn\": ").Append(Q(hymn)).Append(",\n  \"ticksPerQuarter\": ").Append(file.TicksPerQuarter).Append(",\n  \"variants\": {");
            var keys = new List<string> { "" };
            foreach (HymnLevel level in new[] { HymnLevel.Easy, HymnLevel.Medium, HymnLevel.Hard })
                if (map.VariantKey(level).Length > 0) keys.Add(map.VariantKey(level));
            for (int k = 0; k < keys.Count; k++)
            {
                HymnLevel? level = null;
                if (keys[k].Length > 0) level = (HymnLevel)Enum.Parse(typeof(HymnLevel), keys[k], true);
                var m = map.Apply(file, level);
                var grid = HymnGrid.Build(m, pattern);
                if (k > 0) sb.Append(',');
                sb.Append("\n    ").Append(Q(keys[k].Length == 0 ? "base" : keys[k])).Append(": {");
                sb.Append("\n      \"clip\": ").Append(Q(keys[k].Length == 0 ? hymn : hymn + "." + keys[k]));
                sb.Append(",\n      \"fingerprint\": ").Append(Q(Hash(HymnTempoMap.Fingerprint(m) + "|p" + grid.Pattern)));
                sb.Append(",\n      \"pattern\": ").Append(grid.Pattern);
                sb.Append(",\n      \"pickup\": ").Append(grid.HasPickup ? "true" : "false");
                sb.Append(",\n      \"verseEndTick\": ").Append(m.VerseEndTick);
                sb.Append(",\n      \"notes\": ").Append(m.Notes.Count(n => n.StartTick < m.VerseEndTick));
                sb.Append(",\n      \"verseEndSeconds\": ").Append(N(m.SecondsAt(m.VerseEndTick)));
                sb.Append(",\n      \"tempo\": [").Append(string.Join(", ", m.Tempos.Select(t => "[" + t.Tick + ", " + t.MicrosPerQuarter + "]"))).Append(']');
                sb.Append(",\n      \"beats\": [");
                for (int i = 0; i < grid.Beats.Count; i++)
                {
                    var b = grid.Beats[i];
                    sb.Append(i > 0 ? ",\n        " : "\n        ")
                      .Append('[').Append(b.Tick).Append(", ").Append(N(b.Time)).Append(", ").Append(b.Bar).Append(", ")
                      .Append(b.BarPosition).Append(", ").Append(b.BeatsPerBar).Append(']');
                }
                sb.Append("\n      ]\n    }");
            }
            sb.Append("\n  }\n}\n");
            return sb.ToString();
        }

        static string N(double v) => v.ToString("0.#########", CultureInfo.InvariantCulture);

        static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// FNV-1a, 8 hex digits.
        static string Hash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h = unchecked(h * 16777619); }
            return h.ToString("x8");
        }
    }
}
