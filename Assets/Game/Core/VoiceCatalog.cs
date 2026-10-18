// The games' own instrument voices (the web's `audio.voices.*` calls inside games/<game>/index.js) as baked clips:
// which clips each game asks for, named from its chart and its track's harmony so every clip is known up front.
// Tools/sfx/render-voices.mjs bakes the same set from the web's charts and audio graph (voices.json is its manifest);
// VoiceTests pins this catalog to that manifest, which cross-checks Core's charts against the web's.
//
// Swing Kings plays no voices of its own on the web (only sfx() calls), so it has none here.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Bonanza.Core
{
    /// Every level constant of the baked voices, in one place. A clip is baked at the web's exact per-call gain on
    /// the web's own graph (render-voices.mjs header: sfxBus = 1, loose voices at 0.75 / 0.85 of it), and plays at
    /// Sfx.Level (0.425, the music's gain ratio, see Sfx.cs) times its multiplier here. A multiplier exists only
    /// where a variant differs from the baked clip in gain alone: gain is linear in every voice, so
    /// "web gain / baked gain" is exact apart from the web's output compressor (threshold -8 dB), which the
    /// single voices never reach.
    public static class VoiceMix
    {
        /// Drumline call phrase on the finale: tom and rim gains x1.25 (web playCall `big`).
        public const float DrumFinaleCall = 1.25f;
        /// Drumline crash: baked at 0.16; the finale's is 0.30.
        public const float DrumCrashFinale = 0.30f / 0.16f;
        /// Drumline snare: baked at 0.42; a perfect hit adds 0.16 (web onJudged).
        public const float DrumSnarePerfect = 0.58f / 0.42f;
        /// Chomp lead (the singer): baked at the great gain 0.25.
        public const float ChompSingPerfect = 0.30f / 0.25f, ChompSingGood = 0.18f / 0.25f;
        /// Finale solo-tick rim: baked at the accent gain 0.24; the other ticks are 0.15.
        public const float FinaleRimPlain = 0.15f / 0.24f;
        /// Bounce hold and release layers: baked at a perfect (g = 1); web noteGainFor.
        public static float BounceGain(Verdict v) => v == Verdict.Perfect ? 1 : v == Verdict.Great ? 0.86f : 0.66f;
    }

    public static class VoiceCatalog
    {
        public static readonly string[] Games = { "drumline-dash", "bounce-brigade", "chomp-chorus", "finale-fever", "title" };

        /// The title's kick on every beat (web title.js onBeat -> audio.voices.kick), baked at gain 0.5 (the reel's);
        /// the menu plays it at 0.34 / 0.5. "title" is a shell screen, not a game: it only has this clip.
        public const string TitleKick = "title_kick";

        static int Ms(double seconds) => (int)Math.Floor(seconds * 1000 + 0.5);   // JS Math.round for positives
        static double Spb => 60 / BounceChart.Bpm;

        // ---- Drumline Dash
        public const int DrumToms = 5;
        public static string DrumCall(int k) => "dd_call_" + (k % DrumToms);
        public const string DrumCallHat = "dd_callhat", DrumHandoff = "dd_handoff", DrumCrash = "dd_crash", DrumFumble = "dd_fumble", DrumSnare = "dd_snare";
        /// A CPU racer's stumble (racer index 1..3).
        public static string DrumStumble(int racer) => "dd_stumble_" + racer;

        // ---- Bounce Brigade
        public static char VerdictCode(Verdict v) => v == Verdict.Perfect ? 'p' : v == Verdict.Great ? 'g' : 'o';
        /// A tap platform's note: web playPlatformNote with durBeats = max(0.5, flightBeats), the note's length clamped to 0.16..1.6 s.
        public static string BounceNote(Verdict v, int midi, double durBeats) => $"bb_note_{VerdictCode(v)}_{midi}_{Ms(BounceNoteSeconds(durBeats))}";
        public static double BounceNoteSeconds(double durBeats) => Math.Min(1.6, Math.Max(0.16, (durBeats == 0 ? 1 : durBeats) * Spb * 0.9));
        /// The note of a tap platform given its flight (the web passes max(0.5, flightBeats)).
        public static string BounceNote(Verdict v, BouncePlatform p) => BounceNote(v, p.Midi, Math.Max(0.5, p.FlightBeats));
        public static string BounceHold(int midi, double rollBeats) => $"bb_hold_{midi}_{Ms(rollBeats * Spb)}";
        public static string BounceRise(double rollBeats) => $"bb_rise_{Ms(rollBeats * Spb)}";
        public static string BounceRelease(int midi, bool huge) => $"bb_release_{midi}_{(huge ? 1 : 0)}";
        public const string BounceKick = "bb_kick", BounceBigCrash = "bb_bigcrash", BounceThud = "bb_thud", BounceSplash = "bb_splash";

        // ---- Chomp Chorus
        public static string ChompSing(int lane, int deg) => $"cc_sing_{lane}_{deg}";
        public static string ChompBell(int lane, int deg) => $"cc_bell_{lane}_{deg}";
        public static string ChompFinaleSing(int lane, int deg) => $"cc_fsing_{lane}_{deg}";
        public static string ChompSour(int lane, int deg) => $"cc_sour_{lane}_{deg}";
        public static string ChompBed(int lane, int deg) => $"cc_bed_{lane}_{deg}";
        public static string ChompGulp(int lane, bool big) => $"cc_gulp{(big ? "big" : "")}_{lane}";

        // ---- Finale Fever
        public const int FinaleTones = 3;   // the track's chord is a triad
        public static string FinaleSwing(int deg) => "ff_swing_" + deg;
        public static string FinaleChord(int deg, int tone) => $"ff_chord_{deg}_{tone % FinaleTones}";
        public static string FinaleCall(int deg, int tone, bool accent) => $"ff_call_{deg}_{tone % FinaleTones}_{(accent ? 'a' : 'n')}";
        public const string FinaleRim = "ff_rim";
        public static string FinaleAnswer(int deg, int i, bool solo) => $"ff_ans_{deg}_{i % FinaleTones}_{(solo ? 24 : 12)}";
        public static string FinalePluck(int deg, int i) => $"ff_pluck_{deg}_{i % FinaleTones}";

        /// The chord degrees of a track's progression, ascending: every chord a clip can be asked for on.
        public static int[] Degrees(Harmony h) => h.Prog.Distinct().OrderBy(d => d).ToArray();

        /// The chord degrees a Chomp finale note can be sung on: its press lands within the claim window around the
        /// note (a bit under half a beat either side at 140 BPM), so the bar can be the one before or after.
        public static int[] ChompFinaleDegrees()
        {
            var h = Harmony.ChompChorus;
            var set = new SortedSet<int>();
            foreach (var n in ChompChart.Build(0).Where(n => n.Finale))
                foreach (double d in new[] { -0.5, 0, 0.5, 1 })
                    set.Add(h.ChordDegAt((int)Math.Floor((n.Beat + d - h.StartBeat) / h.BeatsPerBar)));
            return set.ToArray();
        }

        static readonly Dictionary<string, List<string>> _cache = new Dictionary<string, List<string>>();

        /// Every clip of one game, in a stable order; empty for a game with no voices of its own (Swing Kings).
        public static IReadOnlyList<string> ClipsOf(string game)
        {
            if (_cache.TryGetValue(game, out var c)) return c;
            var o = new List<string>();
            switch (game)
            {
                case "drumline-dash":
                    for (int k = 0; k < DrumToms; k++) o.Add(DrumCall(k));
                    o.AddRange(new[] { DrumCallHat, DrumHandoff, DrumCrash, DrumFumble, DrumSnare });
                    for (int i = 1; i <= DrumlineRound.HouseCpus.Length; i++) o.Add(DrumStumble(i));
                    break;
                case "bounce-brigade": BounceClips(o); break;
                case "chomp-chorus": ChompClips(o); break;
                case "finale-fever": FinaleClips(o); break;
                case "title": o.Add(TitleKick); break;
            }
            _cache[game] = o = o.Distinct().ToList();
            return o;
        }

        public static IEnumerable<string> All() => Games.SelectMany(ClipsOf);

        static void BounceClips(List<string> o)
        {
            var (plats, _, _, _) = BounceChart.Build();
            var notes = new List<(int midi, double beats)>();
            foreach (var p in plats)
            {
                if (!p.Scored || p.Charge) continue;
                notes.Add((p.Midi, Math.Max(0.5, p.FlightBeats)));
            }
            notes.Add((plats[0].Midi, 1));   // the lead-in demo bounce plays the runway's pitch as a great, one beat long
            foreach (var (midi, beats) in notes)
                foreach (var v in new[] { Verdict.Perfect, Verdict.Great, Verdict.Good }) o.Add(BounceNote(v, midi, beats));
            foreach (var p in plats)
            {
                if (!p.Scored || !p.Charge) continue;
                o.Add(BounceHold(p.Midi, p.Roll));
                o.Add(BounceRise(p.Roll));
                o.Add(BounceRelease(p.Midi, p.Kind == 'F'));
            }
            o.AddRange(new[] { BounceKick, BounceBigCrash, BounceThud, BounceSplash });
        }

        static void ChompClips(List<string> o)
        {
            var fin = ChompFinaleDegrees();
            foreach (int deg in Degrees(Harmony.ChompChorus))
                for (int lane = 0; lane < 4; lane++)
                {
                    o.Add(ChompSing(lane, deg)); o.Add(ChompBell(lane, deg)); o.Add(ChompSour(lane, deg)); o.Add(ChompBed(lane, deg));
                    if (fin.Contains(deg)) o.Add(ChompFinaleSing(lane, deg));
                }
            for (int lane = 0; lane < 4; lane++) { o.Add(ChompGulp(lane, false)); o.Add(ChompGulp(lane, true)); }
        }

        static void FinaleClips(List<string> o)
        {
            var (notes, cues, _) = FeverChart.Build();
            var swing = cues.Any(c => c.Type == "call" && c.Kind == "swing");
            var chordTones = cues.Where(c => c.Type == "call" && c.Kind == "chord").Select(c => c.Tone % FinaleTones).Distinct().OrderBy(t => t).ToList();
            var noteTones = cues.Where(c => c.Type == "call" && c.Kind != "swing" && c.Kind != "chord")
                .Select(c => (tone: c.Tone % FinaleTones, accent: c.Accent)).Distinct().OrderBy(t => t.tone).ThenBy(t => t.accent).ToList();
            bool solo = notes.Any(n => n.Kind == "solo"), swingNote = notes.Any(n => n.Kind == "swing");
            foreach (int deg in Degrees(Harmony.FinaleFever))
            {
                if (swing) o.Add(FinaleSwing(deg));
                foreach (int t in chordTones) o.Add(FinaleChord(deg, t));
                foreach (var (tone, accent) in noteTones) o.Add(FinaleCall(deg, tone, accent));
                for (int i = 0; i < FinaleTones; i++)
                {
                    o.Add(FinaleAnswer(deg, i, false));
                    if (solo) o.Add(FinaleAnswer(deg, i, true));
                    if (swingNote) o.Add(FinalePluck(deg, i));
                }
            }
            o.Add(FinaleRim);
        }
    }
}
