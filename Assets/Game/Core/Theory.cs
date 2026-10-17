// Music theory primitives, ported from web/src/audio/theory.js. Degrees are
// SCALE degrees, not semitones: a melody written as degrees is diatonic by
// construction, and the same data transposes to any mode by swapping a scale.

using System;
using System.Collections.Generic;

namespace Bonanza.Core
{
    public static class Theory
    {
        public static readonly Dictionary<string, int[]> Scales = new Dictionary<string, int[]>
        {
            ["major"] = new[] { 0, 2, 4, 5, 7, 9, 11 },
            ["minor"] = new[] { 0, 2, 3, 5, 7, 8, 10 },
            ["dorian"] = new[] { 0, 2, 3, 5, 7, 9, 10 },
            ["phrygian"] = new[] { 0, 1, 3, 5, 7, 8, 10 },
            ["lydian"] = new[] { 0, 2, 4, 6, 7, 9, 11 },
            ["mixolydian"] = new[] { 0, 2, 4, 5, 7, 9, 10 },
            ["harmonicMinor"] = new[] { 0, 2, 3, 5, 7, 8, 11 },
            ["majorPent"] = new[] { 0, 2, 4, 7, 9 },
            ["minorPent"] = new[] { 0, 3, 5, 7, 10 },
        };

        static readonly Dictionary<string, int> Semi = new Dictionary<string, int>
        {
            ["C"] = 0, ["C#"] = 1, ["Db"] = 1, ["D"] = 2, ["D#"] = 3, ["Eb"] = 3, ["E"] = 4, ["F"] = 5,
            ["F#"] = 6, ["Gb"] = 6, ["G"] = 7, ["G#"] = 8, ["Ab"] = 8, ["A"] = 9, ["A#"] = 10, ["Bb"] = 10, ["B"] = 11,
        };

        /// MIDI note for a name + octave. Note("F", 2) = 41.
        public static int Note(string name, int octave) => 12 * (octave + 1) + Semi[name];

        public static double Mtof(double m) => 440 * Math.Pow(2, (m - 69) / 12);

        /// Scale degree -> MIDI; degrees outside the scale wrap by octave.
        public static int Degree(int rootMidi, int[] scale, int deg)
        {
            int n = scale.Length;
            int oct = (int)Math.Floor((double)deg / n);
            return rootMidi + oct * 12 + scale[deg - oct * n];
        }

        /// Pull a MIDI note onto the nearest chord tone, in any octave (web theory.js snapToChord). Ties keep
        /// the first candidate found, chord order then octave order, as the web does.
        public static int SnapToChord(int midi, int[] chord)
        {
            int best = midi;
            double bestD = double.PositiveInfinity;
            foreach (int c in chord)
            {
                int k = (int)Math.Floor((midi - c) / 12.0 + 0.5);   // JS Math.round: halves go up
                for (int o = k - 1; o <= k + 1; o++)
                {
                    int cand = c + o * 12;
                    double d = Math.Abs(cand - midi);
                    if (d < bestD) { bestD = d; best = cand; }
                }
            }
            return best;
        }

        /// Ascending chord tones starting at or above `fromMidi` (web theory.js arpUp).
        public static int[] ArpUp(int[] chord, int fromMidi, int count)
        {
            var sorted = (int[])chord.Clone();
            Array.Sort(sorted);
            var o = new List<int>();
            int oct = (int)Math.Ceiling((fromMidi - sorted[0]) / 12.0);
            int i = 0, guard = 0;
            while (o.Count < count && guard++ < 64)
            {
                int m = sorted[i] + oct * 12;
                if (m >= fromMidi - 0.5) o.Add(m);
                i++;
                if (i >= sorted.Length) { i = 0; oct++; }
            }
            return o.ToArray();
        }

        /// Diatonic chord stacked in scale thirds from `chordDeg`.
        public static int[] Chord(int rootMidi, int[] scale, int chordDeg, int[] shape)
        {
            var o = new int[shape.Length];
            for (int i = 0; i < shape.Length; i++) o[i] = Degree(rootMidi, scale, chordDeg + shape[i]);
            return o;
        }
    }

    /// A track's harmony, as data (web/src/audio/music/*.js: root, scale, shape, prog).
    public sealed class Harmony
    {
        public int Root;
        public int[] Scale;
        public int[] Shape;
        public int[] Prog;          // chord degree per bar, looping
        public int BeatsPerBar = 4;
        /// Bars in the track (its loop, or its length when it does not loop); 0 = the progression's length.
        public int Bars;
        /// Game beat at which the track's bar 0 starts (web `atBeat`, MusicTracks.GameStartBeat).
        public double StartBeat = -8;

        /// Chord tones at `bar` (bar 0 = the loop's first bar; negative bars wrap).
        public int[] ChordAtBar(int bar)
        {
            int i = ((bar % Prog.Length) + Prog.Length) % Prog.Length;
            return Theory.Chord(Root, Scale, Prog[i], Shape);
        }

        /// Chord degree at `bar` counted from the track's start: the web player's chordDegAt(barOfBeat(beat)).
        public int ChordDegAt(int bar)
        {
            int bars = Bars > 0 ? Bars : Prog.Length;
            int b = ((bar % bars) + bars) % bars;
            return Prog[b % Prog.Length];
        }

        /// The bar of the track that game beat `gameBeat` falls in (web player barOfBeat).
        public int BarAt(double gameBeat) => (int)Math.Floor((gameBeat - StartBeat) / BeatsPerBar);

        public int[] ChordOfDeg(int chordDeg) => Theory.Chord(Root, Scale, chordDeg, Shape);

        /// The harmony of a track by id: the five games, "title" and "results"; null for anything else.
        public static Harmony ForContext(string id)
        {
            switch (id)
            {
                case "swing-kings": return SwingKings;
                case "chomp-chorus": return ChompChorus;
                case "drumline-dash": return DrumlineDash;
                case "finale-fever": return FinaleFever;
                case "bounce-brigade": return BounceBrigade;
                case "title": return Title;
                case "results": return Results;
                default: return null;
            }
        }

        public static readonly Harmony SwingKings = new Harmony { Bars = 16, Root = Theory.Note("F", 2), Scale = Theory.Scales["mixolydian"], Shape = new[] { 0, 2, 4, 6 }, Prog = new[] { 0, 0, 5, 5, 3, 3, 4, 4, 0, 0, 5, 5, 3, 4, 3, 4 } };
        public static readonly Harmony ChompChorus = new Harmony { Bars = 16, Root = Theory.Note("A", 2), Scale = Theory.Scales["dorian"], Shape = new[] { 0, 2, 4, 6 }, Prog = new[] { 0, 0, 3, 3, 0, 0, 3, 3, 0, 0, 3, 3, 6, 6, 3, 4 } };
        public static readonly Harmony DrumlineDash = new Harmony { Bars = 16, Root = Theory.Note("C", 2), Scale = Theory.Scales["minor"], Shape = new[] { 0, 2, 4 }, Prog = new[] { 0, 0, 0, 0, 5, 5, 5, 5, 3, 3, 3, 3, 4, 4, 6, 6 } };
        public static readonly Harmony FinaleFever = new Harmony { Bars = 16, Root = Theory.Note("E", 2), Scale = Theory.Scales["harmonicMinor"], Shape = new[] { 0, 2, 4 }, Prog = new[] { 0, 0, 0, 0, 5, 5, 4, 4, 0, 0, 3, 3, 5, 5, 4, 4 } };
        public static readonly Harmony Title = new Harmony { Bars = 8, StartBeat = 0, Root = Theory.Note("C", 2), Scale = Theory.Scales["major"], Shape = new[] { 0, 2, 4 }, Prog = new[] { 0, 3, 5, 4, 0, 3, 1, 4 } };
        public static readonly Harmony Results = new Harmony { Bars = 4, StartBeat = 0, Root = Theory.Note("C", 2), Scale = Theory.Scales["major"], Shape = new[] { 0, 2, 4 }, Prog = new[] { 3, 4, 0, 0 } };
        public static readonly Harmony BounceBrigade = new Harmony { Bars = 16, Root = Theory.Note("D", 2), Scale = Theory.Scales["major"], Shape = new[] { 0, 2, 4 }, Prog = new[] { 0, 0, 5, 5, 3, 3, 4, 4, 0, 0, 5, 5, 3, 4, 0, 4 } };
    }
}
