// Finale Fever (tempo game) — the tempo curve of one excerpt: how fast the beats go, beat by beat.
//
// A curve is a list of segments laid over the excerpt's conducted beats (beat i is the i-th beat of
// the excerpt, 0-based; the curve starts at beat 0 = second 0):
//   Steady    Beats at Bpm0.
//   Ramp      Beats during which the tempo moves linearly in beats from Bpm0 to Bpm1.
//   Jump      Beats at a new tempo Bpm0 (the change is instant, on the beat the segment starts at).
//   Fermata   exactly ONE beat, played Stretch times as long as it would be at Bpm0 (a held note).
// Beat <-> time is integrated once up front, exactly (a ramp has the closed form
// t(x) = 60 / k * ln(bpm(x) / Bpm0) with k = (Bpm1 - Bpm0) / Beats), never per frame like the web
// Finale Fever did. Like RampMap (FinaleFever.cs) that makes every beat time known before the round
// and lets the audio be rendered ahead from the very same map.
//
// The generator is a pure function of (hymn name, level, beat count, base bpm, line ends): the same
// hymn x level always has the same curve, so the Mac can render each clip once. Rng seeded from the
// hymn name and the level. Bounds are relative to the hymn's own tempo at the excerpt start:
//   Easy    gentle ramps (each at most 12% of the base) and steady stretches, within +-15%.
//   Medium  ramps up AND down (8-25% each), within +-25%.
//   Hard    jumps (15-35% off the base, never fewer than 12% from the last level) and 1-2 fermatas
//           (1.8-2.6 times the beat) on the last beat of a line; no ramps. Within +-35%.
//
// THE DUMP (ToText / Parse): plain text, whitespace separated, `#` starts a comment, one `beat` line
// per beat plus one for the end:
//     tempo-curve 1
//     hymn hymn_065
//     level Medium
//     seed 123456789
//     base-bpm 96
//     bars 5 20                      (excerpt start and end bar, only when known)
//     beats 64
//     seg steady 4 96                (kind beats bpm)
//     seg ramp 12 96 110.4           (kind beats from-bpm to-bpm)
//     seg jump 8 84                  (kind beats bpm)
//     seg fermata 1 84 2.2           (kind 1 bpm stretch)
//     beat 0 0.000000000 96          (beat seconds bpm-as-heard; the last line is `beat N seconds end`)
// The seg lines are the definition, the beat lines their integral; Parse rebuilds the curve from the
// seg lines and rejects a dump whose beat lines disagree (more than 1 ms), so a hand-edited or
// truncated file cannot silently play differently from how it was rendered.
// The beat lines are exact AT the beats only: a renderer that places a note between two beats must
// evaluate the seg lines (steady/jump/fermata: u * 60 * stretch / bpm; ramp: 60 * len / (to - from) *
// ln((from + (to - from) * u / len) / from), u = beats into the segment), not interpolate the beat lines
// (a linear guess is up to ~3 ms off inside a steep ramp beat). TempoCurveTests pins that recipe.
//
// No UnityEngine here: this assembly is pure C# so it stays test-pinned.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Bonanza.Core
{
    public enum TempoKind { Steady, Ramp, Jump, Fermata }

    public sealed class TempoSegment
    {
        public TempoKind Kind;
        public double Beats;
        public double Bpm0, Bpm1;     // Ramp: from, to. Other kinds: Bpm0 = Bpm1 = the tempo (a fermata: the tempo it stretches)
        public double Stretch = 1;    // Fermata only

        public static TempoSegment Steady(double beats, double bpm) => new TempoSegment { Kind = TempoKind.Steady, Beats = beats, Bpm0 = bpm, Bpm1 = bpm };
        public static TempoSegment Ramp(double beats, double from, double to) => new TempoSegment { Kind = TempoKind.Ramp, Beats = beats, Bpm0 = from, Bpm1 = to };
        public static TempoSegment Jump(double beats, double bpm) => new TempoSegment { Kind = TempoKind.Jump, Beats = beats, Bpm0 = bpm, Bpm1 = bpm };
        public static TempoSegment Fermata(double bpm, double stretch) => new TempoSegment { Kind = TempoKind.Fermata, Beats = 1, Bpm0 = bpm, Bpm1 = bpm, Stretch = stretch };

        /// The tempo as heard `x` beats into the segment.
        public double BpmAt(double x)
        {
            if (Kind == TempoKind.Ramp) return Bpm0 + (Bpm1 - Bpm0) * (Beats > 0 ? x / Beats : 0);
            return Kind == TempoKind.Fermata ? Bpm0 / Stretch : Bpm0;
        }

        /// Seconds from the segment's start to `x` beats into it.
        public double SecondsTo(double x)
        {
            if (Kind == TempoKind.Ramp)
            {
                double k = (Bpm1 - Bpm0) / Beats;
                if (Math.Abs(k) < 1e-12) return x * 60.0 / Bpm0;
                return 60.0 / k * Math.Log((Bpm0 + k * x) / Bpm0);
            }
            return x * 60.0 / BpmAt(0);
        }

        /// Beats into the segment after `s` seconds (inverse of SecondsTo).
        public double BeatsAfter(double s)
        {
            if (Kind == TempoKind.Ramp)
            {
                double k = (Bpm1 - Bpm0) / Beats;
                if (Math.Abs(k) < 1e-12) return s * Bpm0 / 60.0;
                return Bpm0 * (Math.Exp(k * s / 60.0) - 1) / k;
            }
            return s * BpmAt(0) / 60.0;
        }
    }

    public sealed class TempoCurve
    {
        public readonly string Hymn;
        public readonly HymnLevel Level;
        public readonly uint Seed;
        public readonly double BaseBpm;
        /// The excerpt's bars, for the dump (0 = not set).
        public int StartBar, EndBar;
        public IReadOnlyList<TempoSegment> Segments => _segs;
        public int BeatCount { get; }
        /// Seconds of beat i, i = 0..BeatCount (the last is the end of the final beat).
        public IReadOnlyList<double> Times => _times;
        public double Duration => _times[BeatCount];

        readonly TempoSegment[] _segs;
        readonly double[] _beatStart;     // beat at which each segment starts
        readonly double[] _secStart;      // seconds at which each segment starts
        readonly double[] _times;

        public TempoCurve(IEnumerable<TempoSegment> segments, string hymn, HymnLevel level, uint seed, double baseBpm)
        {
            _segs = segments.ToArray();
            if (_segs.Length == 0) throw new ArgumentException("a tempo curve needs at least one segment");
            Hymn = hymn ?? "";
            Level = level;
            Seed = seed;
            BaseBpm = baseBpm;
            _beatStart = new double[_segs.Length + 1];
            _secStart = new double[_segs.Length + 1];
            for (int i = 0; i < _segs.Length; i++)
            {
                var s = _segs[i];
                if (!(s.Beats > 0)) throw new ArgumentException("segment " + i + ": beats must be > 0");
                if (!(s.Bpm0 > 0 && s.Bpm1 > 0)) throw new ArgumentException("segment " + i + ": bpm must be > 0");
                if (s.Kind == TempoKind.Fermata && (s.Beats != 1 || !(s.Stretch >= 1))) throw new ArgumentException("segment " + i + ": a fermata is one beat, stretched by 1 or more");
                _beatStart[i + 1] = _beatStart[i] + s.Beats;
                _secStart[i + 1] = _secStart[i] + s.SecondsTo(s.Beats);
            }
            double len = _beatStart[_segs.Length];
            BeatCount = (int)Math.Floor(len + 1e-9);
            if (Math.Abs(len - BeatCount) > 1e-9) throw new ArgumentException("a tempo curve covers a whole number of beats, got " + len);
            _times = new double[BeatCount + 1];
            for (int i = 0; i <= BeatCount; i++) _times[i] = TimeAt(i);
        }

        /// Seconds of beat `beat` (fractions allowed). Before beat 0 and after the end the edge tempo goes on.
        public double TimeAt(double beat)
        {
            if (beat <= 0) return beat * 60.0 / _segs[0].BpmAt(0);
            int n = _segs.Length;
            if (beat >= _beatStart[n]) return _secStart[n] + (beat - _beatStart[n]) * 60.0 / _segs[n - 1].BpmAt(_segs[n - 1].Beats);
            int i = SegmentOfBeat(beat);
            return _secStart[i] + _segs[i].SecondsTo(beat - _beatStart[i]);
        }

        /// The beat at `seconds` (inverse of TimeAt).
        public double BeatAt(double seconds)
        {
            int n = _segs.Length;
            if (seconds <= 0) return seconds * _segs[0].BpmAt(0) / 60.0;
            if (seconds >= _secStart[n]) return _beatStart[n] + (seconds - _secStart[n]) * _segs[n - 1].BpmAt(_segs[n - 1].Beats) / 60.0;
            int lo = 0, hi = n - 1;
            while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (_secStart[mid] <= seconds) lo = mid; else hi = mid - 1; }
            return _beatStart[lo] + _segs[lo].BeatsAfter(seconds - _secStart[lo]);
        }

        /// The tempo (bpm) as heard at `beat`; a fermata beat reads slow.
        public double BpmAt(double beat)
        {
            int n = _segs.Length;
            if (beat <= 0) return _segs[0].BpmAt(0);
            if (beat >= _beatStart[n]) return _segs[n - 1].BpmAt(_segs[n - 1].Beats);
            int i = SegmentOfBeat(beat);
            return _segs[i].BpmAt(beat - _beatStart[i]);
        }

        /// Seconds from beat i to beat i + 1.
        public double LengthOf(int i) => _times[i + 1] - _times[i];

        int SegmentOfBeat(double beat)
        {
            int lo = 0, hi = _segs.Length - 1;
            while (lo < hi) { int mid = (lo + hi + 1) >> 1; if (_beatStart[mid] <= beat) lo = mid; else hi = mid - 1; }
            return lo;
        }

        // ---- the generator ---------------------------------------------------------

        public static uint SeedFor(string hymn, HymnLevel level)
        {
            uint h = 2166136261;
            foreach (char c in hymn ?? "") { h ^= c; h = unchecked(h * 16777619); }
            return unchecked(h ^ (uint)(((int)level + 1) * 0x9e3779b1u));
        }

        public const double HardMinStretch = 1.8, HardMaxStretch = 2.6;

        /// The fixed curve of `hymn` at `level` for an excerpt of `beatCount` beats whose tempo at the start
        /// is `baseBpm`. `lineEnds`: the beat index of the last beat of each line (where a fermata may sit);
        /// null or empty = any beat from the fifth to the second-last.
        public static TempoCurve Generate(string hymn, HymnLevel level, int beatCount, double baseBpm, IReadOnlyList<int> lineEnds = null)
        {
            if (beatCount < 1) throw new ArgumentException("an excerpt needs at least one beat");
            if (!(baseBpm > 0)) throw new ArgumentException("base bpm must be > 0");
            uint seed = SeedFor(hymn, level);
            var rng = new Rng(seed);
            var segs = level == HymnLevel.Easy ? Gentle(rng, beatCount, 0.15, 0.05, 0.12, 8, 16, 2, 6, false)
                     : level == HymnLevel.Medium ? Gentle(rng, beatCount, 0.25, 0.08, 0.25, 8, 14, 0, 4, true)
                     : Jumpy(rng, beatCount);
            // relative levels to bpm
            foreach (var s in segs) { s.Bpm0 *= baseBpm; s.Bpm1 *= baseBpm; }
            if (level == HymnLevel.Hard) segs = WithFermatas(rng, segs, beatCount, lineEnds);
            return new TempoCurve(segs, hymn, level, seed, baseBpm);
        }

        // Segments are built at relative tempo (1 = the base).

        static List<TempoSegment> Gentle(Rng rng, int n, double bound, double minMove, double maxMove, int rampMin, int rampMax, int holdMin, int holdMax, bool alternate)
        {
            var segs = new List<TempoSegment>();
            double cur = 1, pos = 0;
            int dir = rng.Sign();
            segs.Add(TempoSegment.Steady(Math.Min(n, rng.Int(4, 8)), 1));
            pos = segs[0].Beats;
            while (pos < n)
            {
                double mag = rng.Range(minMove, maxMove);
                if (!alternate) dir = rng.Sign();
                double target = cur + dir * mag;
                if (Math.Abs(target - cur) < minMove || target > 1 + bound || target < 1 - bound) { dir = -dir; target = cur + dir * mag; }
                target = Math.Max(1 - bound, Math.Min(1 + bound, target));
                if (Math.Abs(target - cur) < minMove * 0.5) { dir = -dir; target = Math.Max(1 - bound, Math.Min(1 + bound, cur + dir * mag)); }
                int len = rng.Int(rampMin, rampMax);
                int run = Math.Min(len, (int)(n - pos));
                double end = cur + (target - cur) * run / len;
                segs.Add(TempoSegment.Ramp(run, cur, end));
                cur = end; pos += run; dir = -dir;
                if (pos >= n) break;
                int hold = Math.Min(rng.Int(holdMin, holdMax), (int)(n - pos));
                if (hold > 0) { segs.Add(TempoSegment.Steady(hold, cur)); pos += hold; }
            }
            return segs;
        }

        static List<TempoSegment> Jumpy(Rng rng, int n)
        {
            var segs = new List<TempoSegment>();
            double cur = 1;
            int first = Math.Min(n, rng.Int(4, 8));
            segs.Add(TempoSegment.Steady(first, 1));
            double pos = first;
            while (pos < n)
            {
                double next = rng.Sign() * rng.Range(0.15, 0.35);
                next += 1;
                if (Math.Abs(next - cur) < 0.12) next = 2 - next;
                int len = Math.Min(rng.Int(3, 10), (int)(n - pos));
                segs.Add(TempoSegment.Jump(len, next));
                cur = next; pos += len;
            }
            return segs;
        }

        static List<TempoSegment> WithFermatas(Rng rng, List<TempoSegment> segs, int n, IReadOnlyList<int> lineEnds)
        {
            var cand = new List<int>();
            if (lineEnds != null) foreach (int e in lineEnds) if (e >= 0 && e < n && !cand.Contains(e)) cand.Add(e);
            if (cand.Count == 0) for (int b = 4; b < n - 1; b++) cand.Add(b);
            if (cand.Count == 0) cand.Add(n - 1);
            int want = Math.Min(rng.Int(1, 2), cand.Count);
            var chosen = new List<int>();
            for (int k = 0; k < want; k++)
            {
                var free = cand.Where(c => chosen.All(x => Math.Abs(x - c) > 1)).ToList();
                if (free.Count == 0) break;
                chosen.Add(rng.Pick(free));
            }
            foreach (int p in chosen.OrderByDescending(x => x))
            {
                double stretch = Math.Round(rng.Range(HardMinStretch, HardMaxStretch), 2);
                double at = 0;
                for (int i = 0; i < segs.Count; i++)
                {
                    var s = segs[i];
                    if (p >= at && p < at + s.Beats)
                    {
                        var repl = new List<TempoSegment>();
                        double pre = p - at, post = at + s.Beats - p - 1;
                        if (pre > 0) repl.Add(new TempoSegment { Kind = s.Kind, Beats = pre, Bpm0 = s.Bpm0, Bpm1 = s.Bpm1 });
                        repl.Add(TempoSegment.Fermata(s.Bpm0, stretch));
                        if (post > 0) repl.Add(TempoSegment.Steady(post, s.Bpm0));
                        segs.RemoveAt(i);
                        segs.InsertRange(i, repl);
                        break;
                    }
                    at += s.Beats;
                }
            }
            return segs;
        }

        // ---- the dump --------------------------------------------------------------

        static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string S(double v) => v.ToString("0.000000000", CultureInfo.InvariantCulture);

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.Append("tempo-curve 1\n");
            sb.Append("hymn ").Append(Hymn.Replace(' ', '_')).Append('\n');
            sb.Append("level ").Append(Level).Append('\n');
            sb.Append("seed ").Append(Seed.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("base-bpm ").Append(N(BaseBpm)).Append('\n');
            if (StartBar > 0 || EndBar > 0) sb.Append("bars ").Append(StartBar).Append(' ').Append(EndBar).Append('\n');
            sb.Append("beats ").Append(BeatCount).Append('\n');
            foreach (var s in _segs)
            {
                sb.Append("seg ").Append(s.Kind.ToString().ToLowerInvariant()).Append(' ').Append(N(s.Beats)).Append(' ');
                switch (s.Kind)
                {
                    case TempoKind.Ramp: sb.Append(N(s.Bpm0)).Append(' ').Append(N(s.Bpm1)); break;
                    case TempoKind.Fermata: sb.Append(N(s.Bpm0)).Append(' ').Append(N(s.Stretch)); break;
                    default: sb.Append(N(s.Bpm0)); break;
                }
                sb.Append('\n');
            }
            for (int i = 0; i <= BeatCount; i++)
            {
                sb.Append("beat ").Append(i).Append(' ').Append(S(_times[i])).Append(' ');
                sb.Append(i < BeatCount ? (60.0 / LengthOf(i)).ToString("0.###", CultureInfo.InvariantCulture) : "end").Append('\n');
            }
            return sb.ToString();
        }

        public static TempoCurve Parse(string text)
        {
            TempoCurve c;
            string err;
            if (!TryParse(text, out c, out err)) throw new FormatException("tempo curve: " + err);
            return c;
        }

        public static bool TryParse(string text, out TempoCurve curve, out string error)
        {
            curve = null;
            error = null;
            if (string.IsNullOrWhiteSpace(text)) { error = "empty"; return false; }
            var segs = new List<TempoSegment>();
            var beatLines = new List<KeyValuePair<int, double>>();
            string hymn = "";
            HymnLevel level = HymnLevel.Easy;
            uint seed = 0;
            double baseBpm = 0;
            int startBar = 0, endBar = 0, beats = -1;
            bool header = false;
            var inv = CultureInfo.InvariantCulture;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                string l = lines[n];
                int hash = l.IndexOf('#');
                if (hash >= 0) l = l.Substring(0, hash);
                var f = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length == 0) continue;
                string at = "line " + (n + 1) + ": ";
                double d1, d2, d3;
                try
                {
                    switch (f[0])
                    {
                        case "tempo-curve":
                            if (f.Length != 2 || f[1] != "1") { error = at + "unsupported version"; return false; }
                            header = true;
                            break;
                        case "hymn": hymn = f[1]; break;
                        case "level":
                            if (!Enum.TryParse(f[1], false, out level)) { error = at + "unknown level " + f[1]; return false; }
                            break;
                        case "seed": seed = uint.Parse(f[1], inv); break;
                        case "base-bpm": baseBpm = double.Parse(f[1], inv); break;
                        case "bars": startBar = int.Parse(f[1], inv); endBar = int.Parse(f[2], inv); break;
                        case "beats": beats = int.Parse(f[1], inv); break;
                        case "seg":
                            d1 = double.Parse(f[2], inv);
                            switch (f[1])
                            {
                                case "steady": segs.Add(TempoSegment.Steady(d1, double.Parse(f[3], inv))); break;
                                case "jump": segs.Add(TempoSegment.Jump(d1, double.Parse(f[3], inv))); break;
                                case "ramp": d2 = double.Parse(f[3], inv); d3 = double.Parse(f[4], inv); segs.Add(TempoSegment.Ramp(d1, d2, d3)); break;
                                case "fermata": d2 = double.Parse(f[3], inv); d3 = double.Parse(f[4], inv); segs.Add(TempoSegment.Fermata(d2, d3)); break;
                                default: error = at + "unknown segment " + f[1]; return false;
                            }
                            break;
                        case "beat":
                            beatLines.Add(new KeyValuePair<int, double>(int.Parse(f[1], inv), double.Parse(f[2], inv)));
                            break;
                        default: error = at + "unknown line " + f[0]; return false;
                    }
                }
                catch (Exception e) when (e is FormatException || e is IndexOutOfRangeException || e is OverflowException)
                {
                    error = at + "cannot read '" + l.Trim() + "'";
                    return false;
                }
            }
            if (!header) { error = "missing the tempo-curve header"; return false; }
            if (segs.Count == 0) { error = "no segments"; return false; }
            TempoCurve c;
            try { c = new TempoCurve(segs, hymn, level, seed, baseBpm) { StartBar = startBar, EndBar = endBar }; }
            catch (ArgumentException e) { error = e.Message; return false; }
            if (beats != c.BeatCount) { error = "beats " + beats + " does not match the segments (" + c.BeatCount + ")"; return false; }
            if (beatLines.Count != c.BeatCount + 1) { error = "expected " + (c.BeatCount + 1) + " beat lines, got " + beatLines.Count; return false; }
            foreach (var kv in beatLines)
                if (kv.Key < 0 || kv.Key > c.BeatCount || Math.Abs(kv.Value - c.Times[kv.Key]) > 0.001)
                { error = "beat " + kv.Key + " is at " + S(kv.Value) + " s but the segments give " + (kv.Key >= 0 && kv.Key <= c.BeatCount ? S(c.Times[kv.Key]) : "nothing"); return false; }
            curve = c;
            return true;
        }
    }
}
