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

        /// Chord tones at `bar` (bar 0 = the loop's first bar; negative bars wrap).
        public int[] ChordAtBar(int bar)
        {
            int i = ((bar % Prog.Length) + Prog.Length) % Prog.Length;
            return Theory.Chord(Root, Scale, Prog[i], Shape);
        }

        public static readonly Harmony SwingKings = new Harmony { Root = Theory.Note("F", 2), Scale = Theory.Scales["mixolydian"], Shape = new[] { 0, 2, 4, 6 }, Prog = new[] { 0, 0, 5, 5, 3, 3, 4, 4, 0, 0, 5, 5, 3, 4, 3, 4 } };
        public static readonly Harmony ChompChorus = new Harmony { Root = Theory.Note("A", 2), Scale = Theory.Scales["dorian"], Shape = new[] { 0, 2, 4, 6 }, Prog = new[] { 0, 0, 3, 3, 0, 0, 3, 3, 0, 0, 3, 3, 6, 6, 3, 4 } };
        public static readonly Harmony DrumlineDash = new Harmony { Root = Theory.Note("C", 2), Scale = Theory.Scales["minor"], Shape = new[] { 0, 2, 4 }, Prog = new[] { 0, 0, 0, 0, 5, 5, 5, 5, 3, 3, 3, 3, 4, 4, 6, 6 } };
        public static readonly Harmony FinaleFever = new Harmony { Root = Theory.Note("E", 2), Scale = Theory.Scales["harmonicMinor"], Shape = new[] { 0, 2, 4 }, Prog = new[] { 0, 0, 0, 0, 5, 5, 4, 4, 0, 0, 3, 3, 5, 5, 4, 4 } };
        public static readonly Harmony BounceBrigade = new Harmony { Root = Theory.Note("D", 2), Scale = Theory.Scales["major"], Shape = new[] { 0, 2, 4 }, Prog = new[] { 0, 0, 5, 5, 3, 3, 4, 4, 0, 0, 5, 5, 3, 4, 0, 4 } };
    }
}
