// Which notes the web's SFX kit (web/src/audio/index.js `SFX`) plays on a key, with no engine types, so
// tests can pin them against the web's own output (Golden/sfx-notes.txt, sfx-keys.txt; Tools/golden/dump-sfx.mjs).
// The audio is baked offline by Tools/sfx/render-sfx.mjs, one clip per (context, chord) the web can be on;
// SfxCatalog names those clips, SfxTheory says what is in them.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bonanza.Core
{
    /// The chord and scale the music is sitting on (web player.scaleInfo()).
    public sealed class SfxKey
    {
        public string Context;
        public int Root;
        public int[] Scale;
        public int ChordDeg;
        public int[] Chord;

        public static SfxKey Of(string context, int chordDeg)
        {
            var h = Harmony.ForContext(context) ?? throw new ArgumentException("unknown context " + context);
            return new SfxKey { Context = context, Root = h.Root, Scale = h.Scale, ChordDeg = chordDeg, Chord = h.ChordOfDeg(chordDeg) };
        }

        /// The key on game beat `gameBeat` of a context (web key() at that beat). A game's track starts at
        /// MusicTracks.GameStartBeat; the shell tracks count from their own start.
        public static SfxKey At(string context, double gameBeat)
        {
            var h = Harmony.ForContext(context) ?? throw new ArgumentException("unknown context " + context);
            return Of(context, h.ChordDegAt(h.BarAt(gameBeat)));
        }
    }

    /// One voice call inside an SFX: the synth voice, when (ms after the SFX starts) and its notes (none for drums).
    public struct SfxEvent
    {
        public string Voice;
        public int Ms;
        public int[] Midis;
        public override string ToString() => Voice + "@" + Ms + (Midis != null && Midis.Length > 0 ? ":" + string.Join("+", Midis) : "");
    }

    public static class SfxTheory
    {
        /// SFX whose notes depend on the chord.
        public static readonly string[] ChordSfx = { "perfect", "great", "good", "miss", "impact", "ui", "uiBack" };

        static SfxEvent E(string voice, int ms, params int[] midis) => new SfxEvent { Voice = voice, Ms = ms, Midis = midis };

        static int Deg(SfxKey k, int d) => Theory.Degree(k.Root, k.Scale, d);

        static readonly int C5 = Theory.Note("C", 5), G4 = Theory.Note("G", 4), C4 = Theory.Note("C", 4),
                            E5 = Theory.Note("E", 5), A3 = Theory.Note("A", 3);

        /// The voice calls of `sfx` on `key`, in the web's call order. `arg` is the count-in beat or the combo level.
        public static List<SfxEvent> Events(string sfx, SfxKey k, int arg = 0)
        {
            var o = new List<SfxEvent>();
            switch (sfx)
            {
                case "perfect":
                {
                    var arp = Theory.ArpUp(k.Chord, C5, 3);
                    o.Add(E("hit", 0));
                    for (int i = 0; i < arp.Length; i++) o.Add(E("pluck", i * 42, arp[i]));
                    o.Add(E("bell", 90, arp[2] + 12));
                    break;
                }
                case "great":
                {
                    var arp = Theory.ArpUp(k.Chord, C5, 2);
                    o.Add(E("hit", 0));
                    for (int i = 0; i < arp.Length; i++) o.Add(E("pluck", i * 50, arp[i]));
                    break;
                }
                case "good":
                    o.Add(E("pluck", 0, Theory.SnapToChord(G4, k.Chord)));
                    o.Add(E("hit", 0));
                    break;
                case "miss":
                    o.Add(E("fall", 0, Theory.SnapToChord(C4, k.Chord) + 1));
                    o.Add(E("hit", 0));
                    break;
                case "tick": o.Add(E("hit", 0)); break;
                case "ui": o.Add(E("pluck", 0, Theory.SnapToChord(E5, k.Chord))); break;
                case "uiBack": o.Add(E("pluck", 0, Theory.SnapToChord(A3, k.Chord))); break;
                case "count":
                {
                    bool accent = arg == 0;
                    o.Add(E("pluck", 0, Deg(k, accent ? 14 : 11)));
                    if (accent) o.Add(E("hit", 0));
                    break;
                }
                case "combo":
                {
                    int m = Deg(k, 9 + Math.Min(Math.Max(arg, 0), 9));
                    o.Add(E("bell", 0, m));
                    o.Add(E("pluck", 50, m + 12));
                    break;
                }
                case "fanfare":
                {
                    var seq = new[] { (0, 7), (90, 9), (180, 11), (300, 14) };
                    foreach (var (dt, d) in seq)
                    {
                        int m = Deg(k, d);
                        o.Add(E("brass", dt, m));
                        o.Add(E("pluck", dt, m + 12));
                    }
                    o.Add(E("crash", 0));
                    break;
                }
                case "swoosh": o.Add(E("riser", 0)); break;
                case "impact":
                    o.Add(E("kick", 0));
                    o.Add(E("stab", 0, k.Chord.Select(m => m + 12).ToArray()));
                    break;
                case "coin":
                {
                    int m = Deg(k, 14);
                    o.Add(E("bell", 0, m));
                    o.Add(E("bell", 70, m + 5));
                    break;
                }
                default: throw new ArgumentException("unknown sfx " + sfx);
            }
            return o;
        }

        public static string Describe(string sfx, SfxKey k, int arg = 0) => string.Join(",", Events(sfx, k, arg));

        /// Rank -> sting (web shell/results.js RANK_STING).
        public static string RankSting(string rank)
        {
            switch (rank)
            {
                case "S": return "fanfare";
                case "A": return "perfect";
                case "B": return "great";
                case "C": return "good";
                default: return "miss";
            }
        }

        /// The verdict's SFX name (web sfx(verdict)): the judge's verdict word is the SFX name.
        public static string VerdictSfx(Verdict v)
        {
            switch (v)
            {
                case Verdict.Perfect: return "perfect";
                case Verdict.Great: return "great";
                case Verdict.Good: return "good";
                default: return "miss";
            }
        }
    }

    /// One baked clip.
    public sealed class SfxClip
    {
        public string Name;
        public string Context;      // null: no key (tick, swoosh)
        public string Sfx;
        public int ChordDeg = -1;   // chord-dependent clips
        public int Arg = -1;        // count beat (0 accent, 1 other) or combo level
    }

    /// The clips the build ships and which of them each context loads. Tools/sfx/render-sfx.mjs holds the same
    /// matrix (USE); SfxCatalogTests pins this table to the manifest it writes (Assets/Game/Audio/Sfx/sfx.json).
    public static class SfxCatalog
    {
        static readonly string[] CoreSfx = { "perfect", "great", "good", "miss", "impact" };

        sealed class Use
        {
            public string[] Chord;
            public string[] Fixed = new string[0];
            public bool Count;
            public int[] Combo = new int[0];
        }

        static readonly Dictionary<string, Use> Matrix = new Dictionary<string, Use>
        {
            ["swing-kings"] = new Use { Chord = CoreSfx, Fixed = new[] { "fanfare", "coin" }, Count = true },
            ["bounce-brigade"] = new Use { Chord = new[] { "miss" }, Fixed = new[] { "fanfare" }, Count = true },
            ["chomp-chorus"] = new Use { Chord = CoreSfx, Fixed = new[] { "fanfare" }, Count = true },
            ["drumline-dash"] = new Use { Chord = new[] { "perfect", "great", "good", "miss" }, Fixed = new[] { "fanfare" }, Count = true },
            ["finale-fever"] = new Use { Chord = CoreSfx, Fixed = new[] { "fanfare", "coin" }, Count = true, Combo = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 } },
            ["title"] = new Use { Chord = new[] { "ui", "uiBack", "perfect", "great", "miss" }, Fixed = new[] { "fanfare" } },
            ["results"] = new Use { Chord = new[] { "ui", "perfect", "great", "good", "miss" } },   // its fanfare is the title's: same root and scale
        };

        public const string Tick = "tick", Swoosh = "swoosh";
        /// Gain the tick clip was baked at (web sfx('tick') default); callers scale to the web's per-call gain.
        public const float TickGain = 0.12f;

        public static IEnumerable<string> Contexts => Matrix.Keys;

        public static bool Supports(string context, string sfx)
        {
            if (sfx == "tick" || sfx == "swoosh") return true;
            if (!Matrix.TryGetValue(context, out var u)) return false;
            if (sfx == "count") return u.Count;
            if (sfx == "combo") return u.Combo.Length > 0;
            return u.Chord.Contains(sfx) || u.Fixed.Contains(sfx) || (sfx == "fanfare" && context == "results");
        }

        /// The clip a (context, sfx) plays on a chord degree; `arg` is the count beat (accent when 0) or the combo level.
        /// Null when the context does not ship it.
        public static string ClipName(string context, string sfx, int chordDeg, int arg = 0)
        {
            if (sfx == "tick" || sfx == "swoosh") return sfx;
            if (!Supports(context, sfx)) return null;
            if (sfx == "count") return context + "_count_" + (arg == 0 ? 0 : 1);
            if (sfx == "combo") return context + "_combo_" + Math.Min(Math.Max(arg, 0), 9);
            if (sfx == "fanfare" || sfx == "coin") return (context == "results" ? "title" : context) + "_" + sfx;
            return context + "_" + sfx + "_" + chordDeg;
        }

        /// Every clip of one context (the set its screen or game loads), in a stable order.
        public static List<SfxClip> ClipsOf(string context)
        {
            var o = new List<SfxClip>();
            var u = Matrix[context];
            var h = Harmony.ForContext(context);
            var degs = h.Prog.Distinct().OrderBy(d => d).ToArray();
            foreach (var sfx in u.Chord)
                foreach (int d in degs)
                    o.Add(new SfxClip { Name = context + "_" + sfx + "_" + d, Context = context, Sfx = sfx, ChordDeg = d });
            foreach (var sfx in u.Fixed)
                o.Add(new SfxClip { Name = context + "_" + sfx, Context = context, Sfx = sfx, ChordDeg = h.Prog[0] });
            if (u.Count)
                for (int n = 0; n < 2; n++) o.Add(new SfxClip { Name = context + "_count_" + n, Context = context, Sfx = "count", ChordDeg = h.Prog[0], Arg = n });
            foreach (int lv in u.Combo)
                o.Add(new SfxClip { Name = context + "_combo_" + lv, Context = context, Sfx = "combo", ChordDeg = h.Prog[0], Arg = lv });
            return o;
        }

        /// All clips of the build: the keyless ones, then every context.
        public static List<SfxClip> All()
        {
            var o = new List<SfxClip> { new SfxClip { Name = Tick, Sfx = Tick }, new SfxClip { Name = Swoosh, Sfx = Swoosh } };
            foreach (var c in Matrix.Keys) o.AddRange(ClipsOf(c));
            return o;
        }
    }
}
