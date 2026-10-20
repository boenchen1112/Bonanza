// Hymn Meter — the conducted beat grid of verse 1. One entry per conducted beat, with its
// time, its 0-based position in the bar, the beats per bar, and its bar number.
//
// The conducted beat is the table's `pattern` (2, 3 or 4 beats per bar) applied to the bar
// length of the working time signature: unit = bar ticks / pattern. So 4/4 in four is a
// quarter, 3/4 in three a quarter, 2/4 in two a quarter, 6/8 in two a dotted quarter (720
// ticks at 480 per quarter), 3/2 in three a half note.
//
// Bars are numbered in verse 1 only. A leading 1/4 bar (pickup, 弱起拍) is bar 0 and the
// first full bar is bar 1; without a pickup the first bar is bar 1. Pickup beats sit at the
// END of the bar (a 1/4 pickup in 3/4 is position 2, in 4/4 position 3). A bar the verse
// cuts short (the next verse's pickup follows it) simply has fewer beats at their true
// positions. A shortened verse-end bar re-signed 3/4 in a 4/4 hymn, or 2/4 in a 3/4 hymn,
// is the same case: it keeps BeatsPerBar of the hymn and has fewer beats, it is NOT a
// meter change. Any other signature change inside verse 1 (hymn 023's mixed meter) gets
// that signature's own beat count at the same unit, which the table marks as unused.
//
// No UnityEngine here: this assembly is pure C# so it stays test-pinned.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Bonanza.Core
{
    public sealed class HymnBeat
    {
        public int Tick;
        public double Time;         // seconds from the file start
        public int Bar;             // verse 1 numbering: pickup 0, first full bar 1
        public int BarPosition;     // 0-based
        public int BeatsPerBar;

        public MeterBeat ToMeterBeat() => new MeterBeat { Time = Time, BarPosition = BarPosition, BeatsPerBar = BeatsPerBar };
    }

    public sealed class HymnBar
    {
        public int Number;
        public int StartTick;
        public int EndTick;         // end of the bar, cut at the end of verse 1
        public int FirstBeat;       // grid index
        public int BeatCount;
        public int BeatsPerBar;
        public bool Full;           // every beat of the bar exists (false for the pickup and a cut-short bar)
    }

    public sealed class HymnGrid
    {
        public IReadOnlyList<HymnBeat> Beats => _beats;
        public IReadOnlyList<HymnBar> Bars => _bars;
        public bool HasPickup { get; private set; }
        public int Pattern { get; private set; }            // conducted beats per bar of the working meter
        public int Numerator { get; private set; }          // the working time signature
        public int Denominator { get; private set; }
        public int UnitTicks { get; private set; }          // one conducted beat
        public int VerseEndTick { get; private set; }

        /// The highest-numbered bar that has all its beats, -1 if none.
        public int LastFullBar { get; private set; } = -1;

        readonly List<HymnBeat> _beats = new List<HymnBeat>();
        readonly List<HymnBar> _bars = new List<HymnBar>();

        HymnGrid() { }

        /// The bar with this verse-1 number, or null.
        public HymnBar Bar(int number)
        {
            if (_bars.Count == 0) return null;
            int i = number - _bars[0].Number;
            return i >= 0 && i < _bars.Count ? _bars[i] : null;
        }

        /// Conducted beats per bar for a written signature: compound meters (6/8, 9/8,
        /// 12/8) conduct in dotted beats, everything else in its numerator.
        public static int DetectPattern(int numerator, int denominator) =>
            denominator == 8 && numerator >= 6 && numerator % 3 == 0 ? numerator / 3 : numerator;

        /// Throws FormatException when the file cannot be conducted in `pattern`.
        /// pattern 0 = use the pattern detected from the working signature.
        public static HymnGrid Build(HymnMidi midi, int pattern = 0)
        {
            int tpq = midi.TicksPerQuarter;
            int verseEnd = midi.VerseEndTick;
            var sigs = midi.TimeSignatures.Where(s => s.Tick < verseEnd).ToList();
            if (sigs.Count == 0) throw new FormatException("verse 1 is empty");

            var g = new HymnGrid { VerseEndTick = verseEnd };
            int mainIdx = 0;
            if (sigs[0].Numerator == 1 && sigs.Count > 1 && sigs[1].Tick == BarTicks(tpq, sigs[0]))
            {
                g.HasPickup = true;
                mainIdx = 1;
            }
            var main = sigs[mainIdx];
            int mainBar = BarTicks(tpq, main);
            g.Numerator = main.Numerator;
            g.Denominator = main.Denominator;
            if (pattern <= 0) pattern = DetectPattern(main.Numerator, main.Denominator);
            if (pattern < 2 || pattern > 4) throw new FormatException("cannot conduct " + main.Numerator + "/" + main.Denominator + " in " + pattern);
            if (mainBar % pattern != 0) throw new FormatException(main.Numerator + "/" + main.Denominator + " does not divide into " + pattern + " beats");
            g.Pattern = pattern;
            int unit = mainBar / pattern;
            g.UnitTicks = unit;

            if (g.HasPickup)
            {
                // k beats at the end of the pickup bar, the last one a unit before the first full bar
                int pickupLen = sigs[1].Tick;
                int k = Math.Max(1, Math.Min(pattern - 1, (pickupLen + unit / 2) / unit));
                var bar = new HymnBar { Number = 0, StartTick = 0, EndTick = main.Tick, FirstBeat = 0, BeatCount = k, BeatsPerBar = pattern };
                for (int j = 0; j < k; j++)
                    g.AddBeat(midi, Math.Max(0, main.Tick - (k - j) * unit), 0, pattern - k + j, pattern);
                g._bars.Add(bar);
            }

            int number = 1;
            for (int i = mainIdx; i < sigs.Count; i++)
            {
                var sig = sigs[i];
                int segStart = sig.Tick;
                int segEnd = i + 1 < sigs.Count ? sigs[i + 1].Tick : verseEnd;
                int barTicks = BarTicks(tpq, sig);
                bool sameAsMain = sig.Numerator == main.Numerator && sig.Denominator == main.Denominator;
                bool shortened = !sameAsMain && i == sigs.Count - 1 && barTicks < mainBar;
                int bpb = sameAsMain || shortened ? pattern : Math.Max(1, (int)Math.Round((double)barTicks / unit));
                int beatsInBar = shortened ? (barTicks + unit - 1) / unit : bpb;

                for (int barStart = segStart; barStart < segEnd; barStart += barTicks)
                {
                    var bar = new HymnBar
                    {
                        Number = number,
                        StartTick = barStart,
                        EndTick = Math.Min(barStart + barTicks, segEnd),
                        FirstBeat = g._beats.Count,
                        BeatsPerBar = bpb,
                    };
                    for (int j = 0; j < beatsInBar; j++)
                    {
                        int tick = barStart + j * unit;
                        if (tick >= segEnd) break;
                        g.AddBeat(midi, tick, number, j, bpb);
                    }
                    bar.BeatCount = g._beats.Count - bar.FirstBeat;
                    bar.Full = bar.BeatCount == bpb && !shortened;
                    if (bar.Full) g.LastFullBar = number;
                    g._bars.Add(bar);
                    number++;
                }
            }
            return g;
        }

        void AddBeat(HymnMidi midi, int tick, int bar, int position, int beatsPerBar) =>
            _beats.Add(new HymnBeat { Tick = tick, Time = midi.SecondsAt(tick), Bar = bar, BarPosition = position, BeatsPerBar = beatsPerBar });

        static int BarTicks(int tpq, HymnTimeSig s)
        {
            int whole = tpq * 4 * s.Numerator;
            if (s.Denominator <= 0 || whole % s.Denominator != 0) throw new FormatException("odd time signature " + s.Numerator + "/" + s.Denominator);
            return whole / s.Denominator;
        }
    }
}
