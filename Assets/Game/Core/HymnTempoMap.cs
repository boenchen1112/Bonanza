// Hymn Meter — the tempo map: the one editable place for a hymn's timing. The map is a small
// JSON file per hymn (hand-edited, // comments allowed) that sits beside the MIDI. The beat
// grid the game judges against (HymnGrid) and the audio the render script makes are BOTH
// built from the MIDI notes plus this map, so editing it moves the sound and the beats
// together.
//
//   {
//     "hymn": "hymn_058",
//     "scale": 1.0,                 // global tempo multiplier: 1.2 = 20% faster, everywhere
//     "pattern": 4,                 // conducted beats per bar; 0 or missing = detect from the meter
//     "tempo": [                    // missing = keep the file's own tempo changes
//       { "tick": 0,   "bpm": 100, "x": 1.0 },   // x = this segment's own multiplier (until the next entry)
//       { "tick": 960, "bpm": 20,  "x": 1.5 }
//     ],
//     "meter": [ { "tick": 0, "sig": "1/4" }, { "tick": 480, "sig": "4/4" } ],   // missing = keep the file's
//     "levels": { "easy": { "scale": 0.9 }, "medium": {}, "hard": {} }           // extra multiplier per excerpt level
//   }
//
// Effective bpm of a segment = bpm * x * scale * (the level's scale). A level whose scale is not
// 1 is its own variant (own grid, own rendered clip, named `<hymn>.<level>`). With no map, or an
// untouched one, the file's own microseconds-per-quarter are kept exactly (the default).
//
// No UnityEngine here: this assembly is pure C# so it stays test-pinned.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Bonanza.Core
{
    public sealed class HymnTempoEntry
    {
        public int Tick;
        public double Bpm;
        public double X = 1;
    }

    public sealed class HymnMeterEntry
    {
        public int Tick;
        public int Numerator;
        public int Denominator;
    }

    public sealed class HymnTempoMap
    {
        public string Hymn = "";
        public double Scale = 1;
        public int Pattern;                              // 0 = detect
        public List<HymnTempoEntry> Tempo;               // null = the file's own
        public List<HymnMeterEntry> Meter;               // null = the file's own
        public readonly double[] LevelScale = { 1, 1, 1 };   // by HymnLevel

        /// No override at all: Apply returns the file's own timing.
        public static HymnTempoMap Identity(string hymn = "") => new HymnTempoMap { Hymn = hymn };

        public static bool TryParse(string text, out HymnTempoMap map, out string error)
        {
            try { map = Parse(text); error = null; return true; }
            catch (FormatException e) { map = null; error = e.Message; return false; }
        }

        /// Throws FormatException with a message naming the field.
        public static HymnTempoMap Parse(string text)
        {
            var root = MiniJson.Parse(text) as Dictionary<string, object>;
            if (root == null) throw new FormatException("tempo map: the top level must be an object");
            var m = new HymnTempoMap();
            m.Hymn = Str(root, "hymn", "");
            m.Scale = Num(root, "scale", 1);
            if (!(m.Scale > 0 && m.Scale < 100)) throw new FormatException("tempo map: scale must be > 0, got " + m.Scale);
            m.Pattern = (int)Num(root, "pattern", 0);
            if (m.Pattern != 0 && (m.Pattern < 2 || m.Pattern > 4)) throw new FormatException("tempo map: pattern must be 0, 2, 3 or 4, got " + m.Pattern);

            object tempo;
            if (root.TryGetValue("tempo", out tempo) && tempo != null)
            {
                var list = tempo as List<object>;
                if (list == null || list.Count == 0) throw new FormatException("tempo map: tempo must be a non-empty array");
                m.Tempo = new List<HymnTempoEntry>();
                foreach (var o in list)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) throw new FormatException("tempo map: a tempo entry must be an object");
                    var e = new HymnTempoEntry { Tick = (int)Num(d, "tick", -1), Bpm = Num(d, "bpm", 0), X = Num(d, "x", 1) };
                    if (e.Tick < 0) throw new FormatException("tempo map: a tempo entry needs a tick >= 0");
                    if (!(e.Bpm > 0 && e.Bpm < 1000)) throw new FormatException("tempo map: tick " + e.Tick + ": bpm must be 0..1000, got " + e.Bpm);
                    if (!(e.X > 0 && e.X < 100)) throw new FormatException("tempo map: tick " + e.Tick + ": x must be > 0, got " + e.X);
                    m.Tempo.Add(e);
                }
                for (int i = 1; i < m.Tempo.Count; i++)
                    if (m.Tempo[i].Tick <= m.Tempo[i - 1].Tick) throw new FormatException("tempo map: tempo ticks must ascend (tick " + m.Tempo[i].Tick + ")");
                if (m.Tempo[0].Tick != 0) throw new FormatException("tempo map: the first tempo entry must be at tick 0");
            }

            object meter;
            if (root.TryGetValue("meter", out meter) && meter != null)
            {
                var list = meter as List<object>;
                if (list == null || list.Count == 0) throw new FormatException("tempo map: meter must be a non-empty array");
                m.Meter = new List<HymnMeterEntry>();
                foreach (var o in list)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) throw new FormatException("tempo map: a meter entry must be an object");
                    int tick = (int)Num(d, "tick", -1);
                    string sig = Str(d, "sig", "");
                    var parts = sig.Split('/');
                    int n, den;
                    if (tick < 0 || parts.Length != 2 || !int.TryParse(parts[0], out n) || !int.TryParse(parts[1], out den) || n <= 0 || den <= 0)
                        throw new FormatException("tempo map: a meter entry needs tick >= 0 and sig like \"4/4\", got tick " + tick + " sig \"" + sig + "\"");
                    m.Meter.Add(new HymnMeterEntry { Tick = tick, Numerator = n, Denominator = den });
                }
                for (int i = 1; i < m.Meter.Count; i++)
                    if (m.Meter[i].Tick <= m.Meter[i - 1].Tick) throw new FormatException("tempo map: meter ticks must ascend (tick " + m.Meter[i].Tick + ")");
            }

            object levels;
            if (root.TryGetValue("levels", out levels) && levels != null)
            {
                var d = levels as Dictionary<string, object>;
                if (d == null) throw new FormatException("tempo map: levels must be an object");
                foreach (HymnLevel lv in Enum.GetValues(typeof(HymnLevel)))
                {
                    object o;
                    if (!d.TryGetValue(lv.ToString().ToLowerInvariant(), out o) || o == null) continue;
                    var ld = o as Dictionary<string, object>;
                    if (ld == null) throw new FormatException("tempo map: levels." + lv.ToString().ToLowerInvariant() + " must be an object");
                    double s = Num(ld, "scale", 1);
                    if (!(s > 0 && s < 100)) throw new FormatException("tempo map: levels." + lv.ToString().ToLowerInvariant() + ".scale must be > 0, got " + s);
                    m.LevelScale[(int)lv] = s;
                }
            }
            return m;
        }

        static double Num(Dictionary<string, object> d, string key, double def)
        {
            object o;
            if (!d.TryGetValue(key, out o) || o == null) return def;
            if (!(o is double)) throw new FormatException("tempo map: " + key + " must be a number");
            return (double)o;
        }

        static string Str(Dictionary<string, object> d, string key, string def)
        {
            object o;
            if (!d.TryGetValue(key, out o) || o == null) return def;
            var s = o as string;
            if (s == null) throw new FormatException("tempo map: " + key + " must be a string");
            return s;
        }

        /// The clip/variant suffix of a level: "" when the level plays the base timing, else
        /// the level's lower-case name ("easy").
        public string VariantKey(HymnLevel level) =>
            Math.Abs(LevelScale[(int)level] - 1) < 1e-9 ? "" : level.ToString().ToLowerInvariant();

        /// The file under this map: the base timing for level null, else with the level's extra scale.
        public HymnMidi Apply(HymnMidi midi, HymnLevel? level = null)
        {
            double s = Scale * (level.HasValue ? LevelScale[(int)level.Value] : 1);
            var source = Tempo != null
                ? Tempo
                : midi.Tempos.Select(t => new HymnTempoEntry { Tick = t.Tick, Bpm = t.Bpm, X = 1 }).ToList();
            var tempos = new List<HymnTempo>();
            foreach (var e in source)
            {
                double factor = e.X * s;
                HymnTempo orig = null;
                foreach (var t in midi.Tempos) if (t.Tick == e.Tick) { orig = t; break; }
                int micros;
                if (orig != null && Math.Abs(factor - 1) < 1e-12 && Math.Abs(orig.Bpm - e.Bpm) < 1e-4) micros = orig.MicrosPerQuarter;   // untouched: the file's exact value
                else micros = Math.Max(1, (int)Math.Round(60000000.0 / (e.Bpm * factor)));
                tempos.Add(new HymnTempo { Tick = e.Tick, MicrosPerQuarter = micros });
            }
            IEnumerable<HymnTimeSig> sigs = Meter != null
                ? Meter.Select(e => new HymnTimeSig { Tick = e.Tick, Numerator = e.Numerator, Denominator = e.Denominator })
                : midi.TimeSignatures;
            return midi.WithMaps(tempos, sigs);
        }

        /// A short stable text of the timing that results, for "did it change" comparisons.
        public static string Fingerprint(HymnMidi m) =>
            string.Join(";", m.Tempos.Select(t => t.Tick.ToString(CultureInfo.InvariantCulture) + ":" + t.MicrosPerQuarter.ToString(CultureInfo.InvariantCulture)))
            + "|" + string.Join(";", m.TimeSignatures.Select(s => s.Tick + ":" + s.Numerator + "/" + s.Denominator));
    }
}
