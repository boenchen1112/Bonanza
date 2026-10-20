// Pins the hymn MIDI reader, the conducted beat grid and the excerpt cut. Pure C#, no
// hymn files: every MIDI file here is built in memory by SmfBuilder below. One optional
// test reads the real files when BONANZA_HYMNS points at a hymns folder (skipped else).
//
// Synthetic files use 480 ticks per quarter and 120 bpm unless a test says otherwise,
// so a quarter note is 0.5 s.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Bonanza.Core;

namespace Bonanza.Core.Tests
{
    /// Writes Standard MIDI File bytes: tracks of absolute-tick events.
    public sealed class SmfBuilder
    {
        public sealed class Track
        {
            internal readonly List<KeyValuePair<int, byte[]>> Events = new List<KeyValuePair<int, byte[]>>();
            internal int End = -1;

            void Add(int tick, params byte[] bytes) => Events.Add(new KeyValuePair<int, byte[]>(tick, bytes));

            public Track Tempo(int tick, double bpm)
            {
                int us = (int)Math.Round(60000000.0 / bpm);
                Add(tick, 0xFF, 0x51, 0x03, (byte)(us >> 16), (byte)(us >> 8), (byte)us);
                return this;
            }

            public Track Sig(int tick, int num, int den)
            {
                int dd = 0;
                while ((1 << dd) < den) dd++;
                Add(tick, 0xFF, 0x58, 0x04, (byte)num, (byte)dd, 24, 8);
                return this;
            }

            public Track On(int tick, int ch, int pitch, int vel = 90) { Add(tick, (byte)(0x90 | ch), (byte)pitch, (byte)vel); return this; }
            public Track Off(int tick, int ch, int pitch) { Add(tick, (byte)(0x80 | ch), (byte)pitch, 0); return this; }
            public Track Raw(int tick, params byte[] bytes) { Add(tick, bytes); return this; }
            public Track Note(int start, int end, int pitch, int ch = 0) { On(start, ch, pitch); Off(end, ch, pitch); return this; }
            public Track EndAt(int tick) { End = tick; return this; }
        }

        public int Tpq = 480;
        public int Format = 1;
        public bool RunningStatus;
        public bool VelocityZeroOff;   // write note-offs as note-on with velocity 0
        readonly List<Track> _tracks = new List<Track>();

        public Track AddTrack() { var t = new Track(); _tracks.Add(t); return t; }

        public byte[] Build()
        {
            var o = new List<byte>();
            o.AddRange(new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d', 0, 0, 0, 6 });
            o.Add(0); o.Add((byte)Format);
            o.Add((byte)(_tracks.Count >> 8)); o.Add((byte)_tracks.Count);
            o.Add((byte)(Tpq >> 8)); o.Add((byte)Tpq);
            foreach (var t in _tracks)
            {
                var body = new List<byte>();
                int prevTick = 0, status = 0;
                int last = t.Events.Count == 0 ? 0 : t.Events.Max(e => e.Key);
                foreach (var e in t.Events.OrderBy(e => e.Key))   // stable: insertion order inside a tick
                {
                    body.AddRange(Vlq(e.Key - prevTick));
                    prevTick = e.Key;
                    var b = e.Value;
                    bool channel = b[0] < 0xF0;
                    if (channel && VelocityZeroOff && (b[0] & 0xF0) == 0x80)
                        b = new byte[] { (byte)(0x90 | (b[0] & 0x0F)), b[1], 0 };
                    if (channel && RunningStatus && b[0] == status) body.AddRange(b.Skip(1));
                    else body.AddRange(b);
                    status = channel ? b[0] : 0;
                }
                int end = t.End >= 0 ? t.End : last;
                body.AddRange(Vlq(end - prevTick));
                body.AddRange(new byte[] { 0xFF, 0x2F, 0x00 });
                o.AddRange(new byte[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' });
                o.Add((byte)(body.Count >> 24)); o.Add((byte)(body.Count >> 16)); o.Add((byte)(body.Count >> 8)); o.Add((byte)body.Count);
                o.AddRange(body);
            }
            return o.ToArray();
        }

        static IEnumerable<byte> Vlq(int v)
        {
            var parts = new Stack<byte>();
            parts.Push((byte)(v & 0x7F));
            while ((v >>= 7) > 0) parts.Push((byte)((v & 0x7F) | 0x80));
            return parts;
        }
    }

    public class HymnMidiTests
    {
        const int Q = 480;

        // ---- synthetic hymns -------------------------------------------------

        // A hymn of `verses` verses, `bars` full bars of `num`/`den` per verse, written the
        // way the real files are: the tempo + time-signature block repeats at each verse
        // start. A pickup adds a leading 1/4 bar (then the working signature at tick 480).
        // `shortSig` re-signs the last bar of each verse as that signature (the shortened
        // verse-end bar); that bar then lasts `shortTicks` and the next block follows it.
        static byte[] Hymn(int num, int den, int bars, bool pickup = false, int verses = 2,
                           double bpm = 120, int shortNum = 0, int shortDen = 4)
        {
            var smf = new SmfBuilder();
            var meta = smf.AddTrack();
            var notes = smf.AddTrack();
            int barTicks = Q * 4 * num / den;
            int shortTicks = shortNum > 0 ? Q * 4 * shortNum / shortDen : 0;
            int verseTicks = (pickup ? Q : 0) + (bars - (shortNum > 0 ? 1 : 0)) * barTicks + shortTicks;
            for (int v = 0; v < verses; v++)
            {
                int t0 = v * verseTicks;
                meta.Tempo(t0, bpm);
                if (pickup) { meta.Sig(t0, 1, 4); meta.Sig(t0 + Q, num, den); }
                else meta.Sig(t0, num, den);
                if (shortNum > 0)
                    meta.Sig(t0 + (pickup ? Q : 0) + (bars - 1) * barTicks, shortNum, shortDen);
                // one note per quarter, pitch walks so notes are distinguishable
                for (int k = 0; k * Q < verseTicks; k++)
                    notes.Note(t0 + k * Q, t0 + k * Q + Q - 10, 60 + (k % 12));
            }
            meta.EndAt(verses * verseTicks);
            return smf.Build();
        }

        static HymnGrid Grid(byte[] bytes, int pattern = 0) => HymnGrid.Build(HymnMidi.Parse(bytes), pattern);

        // ---- the reader ------------------------------------------------------

        [Test]
        public void Format1ReadsTempoTrackAndNotes()
        {
            var smf = new SmfBuilder { Tpq = 480 };
            smf.AddTrack().Tempo(0, 100).Sig(0, 3, 4);
            smf.AddTrack().Note(0, 400, 64, 1).Note(480, 960, 67, 1);
            smf.AddTrack().On(0, 2, 40, 55).Off(1920, 2, 40);
            var m = HymnMidi.Parse(smf.Build());

            Assert.AreEqual(480, m.TicksPerQuarter);
            Assert.AreEqual(1, m.Tempos.Count);
            Assert.AreEqual(600000, m.Tempos[0].MicrosPerQuarter);
            Assert.AreEqual(100.0, m.Tempos[0].Bpm, 1e-6);
            Assert.AreEqual(1, m.TimeSignatures.Count);
            Assert.AreEqual(3, m.TimeSignatures[0].Numerator);
            Assert.AreEqual(4, m.TimeSignatures[0].Denominator);
            Assert.AreEqual(3, m.Notes.Count);
            var bass = m.Notes.Single(n => n.Pitch == 40);
            Assert.AreEqual(2, bass.Track);
            Assert.AreEqual(2, bass.Channel);
            Assert.AreEqual(55, bass.Velocity);
            Assert.AreEqual(0, bass.StartTick);
            Assert.AreEqual(1920, bass.EndTick);
            var second = m.Notes.Single(n => n.Pitch == 67);
            Assert.AreEqual(480, second.StartTick);
            Assert.AreEqual(960, second.EndTick);
            Assert.AreEqual(1, second.Track);
        }

        [Test]
        public void Format0SingleTrackWorks()
        {
            var smf = new SmfBuilder { Format = 0 };
            smf.AddTrack().Tempo(0, 90).Sig(0, 4, 4).Note(0, 480, 60);
            var m = HymnMidi.Parse(smf.Build());
            Assert.AreEqual(1, m.Notes.Count);
            Assert.AreEqual(90.0, m.Tempos[0].Bpm, 1e-3);
        }

        [Test]
        public void MissingTempoAndSignatureGetTheMidiDefaults()
        {
            var smf = new SmfBuilder();
            smf.AddTrack().Note(0, 480, 60);
            var m = HymnMidi.Parse(smf.Build());
            Assert.AreEqual(120.0, m.Tempos[0].Bpm, 1e-6);
            Assert.AreEqual(4, m.TimeSignatures[0].Numerator);
            Assert.AreEqual(4, m.TimeSignatures[0].Denominator);
            Assert.AreEqual(0.5, m.SecondsAt(480), 1e-9);
        }

        [Test]
        public void RunningStatusIsExpanded()
        {
            var smf = new SmfBuilder { RunningStatus = true };
            var t = smf.AddTrack();
            t.On(0, 0, 60).On(0, 0, 64).On(0, 0, 67);       // 2nd and 3rd drop their status byte
            t.Off(480, 0, 60).Off(480, 0, 64).Off(480, 0, 67);
            var m = HymnMidi.Parse(smf.Build());
            CollectionAssert.AreEqual(new[] { 60, 64, 67 }, m.Notes.Select(n => n.Pitch).OrderBy(p => p).ToArray());
            Assert.IsTrue(m.Notes.All(n => n.StartTick == 0 && n.EndTick == 480));
        }

        [Test]
        public void NoteOnWithVelocityZeroEndsTheNote()
        {
            var smf = new SmfBuilder { VelocityZeroOff = true };
            smf.AddTrack().Note(0, 240, 62).Note(240, 720, 62);
            var m = HymnMidi.Parse(smf.Build());
            Assert.AreEqual(2, m.Notes.Count);
            Assert.AreEqual(240, m.Notes[0].EndTick);
            Assert.AreEqual(240, m.Notes[1].StartTick);
            Assert.AreEqual(720, m.Notes[1].EndTick);
        }

        [Test]
        public void SysexAndOtherMetasAreSkipped()
        {
            var smf = new SmfBuilder();
            var t = smf.AddTrack();
            t.Raw(0, 0xFF, 0x03, 0x04, (byte)'T', (byte)'e', (byte)'s', (byte)'t');   // track name
            t.Raw(0, 0xF0, 0x03, 0x7E, 0x01, 0xF7);                                    // sysex
            t.Raw(0, 0xFF, 0x59, 0x02, 0x01, 0x00);                                    // key signature
            t.Raw(0, 0xC0, 0x05);                                                      // program change (1 data byte)
            t.Note(0, 480, 60);
            var m = HymnMidi.Parse(smf.Build());
            Assert.AreEqual(1, m.Notes.Count);
            Assert.AreEqual(60, m.Notes[0].Pitch);
        }

        [Test]
        public void GarbageIsAFormatException()
        {
            Assert.Throws<FormatException>(() => HymnMidi.Parse(new byte[] { 1, 2, 3 }));
            var bytes = Hymn(4, 4, 2);
            Assert.Throws<FormatException>(() => HymnMidi.Parse(bytes.Take(bytes.Length - 6).ToArray()));
        }

        // ---- ticks to seconds ------------------------------------------------

        [Test]
        public void TempoChangeMidFileChangesTheTimes()
        {
            var smf = new SmfBuilder();
            smf.AddTrack().Tempo(0, 120).Tempo(960, 60).Tempo(1440, 120).Sig(0, 4, 4).EndAt(4000);
            var m = HymnMidi.Parse(smf.Build());
            Assert.AreEqual(1.0, m.SecondsAt(960), 1e-9);      // 2 quarters at 120
            Assert.AreEqual(1.5, m.SecondsAt(1200), 1e-9);     // half a quarter at 60 = 0.5 s more
            Assert.AreEqual(2.0, m.SecondsAt(1440), 1e-9);     // 1 s for the 60 bpm quarter
            Assert.AreEqual(2.5, m.SecondsAt(1920), 1e-9);     // back at 120
        }

        [Test]
        public void TheLastTempoWinsWhenTwoShareATick()
        {
            var smf = new SmfBuilder();
            smf.AddTrack().Tempo(0, 120).Tempo(0, 60);
            var m = HymnMidi.Parse(smf.Build());
            Assert.AreEqual(1, m.Tempos.Count);
            Assert.AreEqual(1.0, m.SecondsAt(480), 1e-9);
        }

        // ---- the conducted grid ----------------------------------------------

        [Test]
        public void FourFourGridPositionsAndTimes()
        {
            var g = Grid(Hymn(4, 4, 3, verses: 1), 4);
            Assert.AreEqual(12, g.Beats.Count);
            Assert.IsFalse(g.HasPickup);
            for (int i = 0; i < 12; i++)
            {
                Assert.AreEqual(i % 4, g.Beats[i].BarPosition, "position " + i);
                Assert.AreEqual(4, g.Beats[i].BeatsPerBar);
                Assert.AreEqual(1 + i / 4, g.Beats[i].Bar, "bar " + i);
                Assert.AreEqual(i * 0.5, g.Beats[i].Time, 1e-9);
            }
            Assert.AreEqual(1, g.Bars[0].Number);
            Assert.AreEqual(3, g.LastFullBar);
        }

        [Test]
        public void ThreeFourWithAPickupPutsTheOneBeatAtTheBarEnd()
        {
            var g = Grid(Hymn(3, 4, 4, pickup: true), 3);
            Assert.IsTrue(g.HasPickup);
            var first = g.Beats[0];
            Assert.AreEqual(0, first.Bar);
            Assert.AreEqual(2, first.BarPosition);
            Assert.AreEqual(3, first.BeatsPerBar);
            Assert.AreEqual(0.0, first.Time, 1e-9);
            var down = g.Beats[1];
            Assert.AreEqual(1, down.Bar);
            Assert.AreEqual(0, down.BarPosition);
            Assert.AreEqual(0.5, down.Time, 1e-9);
            Assert.AreEqual(1 + 4 * 3, g.Beats.Count);            // pickup beat + 4 bars of 3
            Assert.AreEqual(0, g.Bars[0].Number);
            Assert.IsFalse(g.Bars[0].Full);
            Assert.AreEqual(4, g.LastFullBar);
        }

        [Test]
        public void FourFourPickupIsBarPositionThree()
        {
            var g = Grid(Hymn(4, 4, 3, pickup: true), 4);
            Assert.AreEqual(3, g.Beats[0].BarPosition);
            Assert.AreEqual(0, g.Beats[0].Bar);
            Assert.AreEqual(0, g.Beats[1].BarPosition);
        }

        [Test]
        public void SixEightConductedInTwoUsesDottedQuarterBeats()
        {
            var g = Grid(Hymn(6, 8, 3, verses: 1), 2);
            Assert.AreEqual(6, g.Beats.Count);
            Assert.AreEqual(720, g.Beats[1].Tick - g.Beats[0].Tick);
            Assert.AreEqual(0.75, g.Beats[1].Time, 1e-9);        // 720 ticks at 120 bpm
            CollectionAssert.AreEqual(new[] { 0, 1, 0, 1, 0, 1 }, g.Beats.Select(b => b.BarPosition).ToArray());
            Assert.IsTrue(g.Beats.All(b => b.BeatsPerBar == 2));
        }

        [Test]
        public void ThreeTwoConductedInThreeUsesHalfNotes()
        {
            var g = Grid(Hymn(3, 2, 2, verses: 1), 3);
            Assert.AreEqual(6, g.Beats.Count);
            Assert.AreEqual(960, g.Beats[1].Tick);
        }

        [Test]
        public void TwoFourIsTwoQuarterBeats()
        {
            var g = Grid(Hymn(2, 4, 3, verses: 1), 2);
            Assert.AreEqual(6, g.Beats.Count);
            Assert.AreEqual(480, g.Beats[1].Tick);
        }

        [Test]
        public void DetectionMatchesThePatternOfEachSignature()
        {
            Assert.AreEqual(4, Grid(Hymn(4, 4, 2)).Pattern);
            Assert.AreEqual(3, Grid(Hymn(3, 4, 2)).Pattern);
            Assert.AreEqual(2, Grid(Hymn(2, 4, 2)).Pattern);
            Assert.AreEqual(2, Grid(Hymn(6, 8, 2)).Pattern);
            Assert.AreEqual(3, Grid(Hymn(3, 2, 2)).Pattern);
        }

        [Test]
        public void ShortenedVerseEndBarKeepsTheFourFourMeter()
        {
            // bars 1-3 are 4/4, bar 4 is re-signed 3/4, then verse 2 starts with a pickup
            var g = Grid(Hymn(4, 4, 4, pickup: true, shortNum: 3), 4);
            Assert.IsTrue(g.Beats.All(b => b.BeatsPerBar == 4), "never a meter change");
            var last = g.Beats.Where(b => b.Bar == 4).ToList();
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, last.Select(b => b.BarPosition).ToArray());
            Assert.AreEqual(1 + 3 * 4 + 3, g.Beats.Count);
            Assert.IsFalse(g.Bars.Single(b => b.Number == 4).Full);
            Assert.AreEqual(3, g.LastFullBar);
            Assert.AreEqual(1 + 3 * 4 + 3, g.Beats.Count);
        }

        [Test]
        public void ShortenedVerseEndBarKeepsTheThreeFourMeter()
        {
            var g = Grid(Hymn(3, 4, 4, pickup: true, shortNum: 2), 3);
            Assert.IsTrue(g.Beats.All(b => b.BeatsPerBar == 3));
            CollectionAssert.AreEqual(new[] { 0, 1 }, g.Beats.Where(b => b.Bar == 4).Select(b => b.BarPosition).ToArray());
            Assert.AreEqual(3, g.LastFullBar);
        }

        [Test]
        public void VerseOneEndsAtTheRepeatedMetaBlock()
        {
            var m = HymnMidi.Parse(Hymn(4, 4, 4, verses: 3));
            Assert.AreEqual(4 * 1920, m.VerseEndTick);
            var g = HymnGrid.Build(m, 4);
            Assert.AreEqual(16, g.Beats.Count);                   // only verse 1
            Assert.AreEqual(4 * 1920, g.VerseEndTick);
        }

        [Test]
        public void VerseOneWithAPickupEndsAtTheSecondPickupBlock()
        {
            var m = HymnMidi.Parse(Hymn(3, 4, 3, pickup: true, verses: 2));
            Assert.AreEqual(480 + 3 * 1440, m.VerseEndTick);
        }

        [Test]
        public void ASingleVerseFileEndsAtTheLastEvent()
        {
            var m = HymnMidi.Parse(Hymn(4, 4, 2, verses: 1));
            Assert.AreEqual(2 * 1920, m.VerseEndTick);
        }

        [Test]
        public void BeatTimesFollowAFermataTempoDrop()
        {
            var smf = new SmfBuilder();
            smf.AddTrack().Tempo(0, 120).Sig(0, 4, 4).Tempo(960, 30).Tempo(1440, 120).EndAt(3840);
            var g = HymnGrid.Build(HymnMidi.Parse(smf.Build()), 4);
            Assert.AreEqual(0.0, g.Beats[0].Time, 1e-9);
            Assert.AreEqual(1.0, g.Beats[2].Time, 1e-9);           // tick 960
            Assert.AreEqual(3.0, g.Beats[3].Time, 1e-9);           // tick 1440 after a 2 s quarter at 30 bpm
            Assert.AreEqual(3.5, g.Beats[4].Time, 1e-9);
        }

        [Test]
        public void AnUnsupportedPatternIsRejected()
        {
            var m = HymnMidi.Parse(Hymn(4, 4, 2));
            Assert.Throws<FormatException>(() => HymnGrid.Build(m, 5));
            Assert.Throws<FormatException>(() => HymnGrid.Build(HymnMidi.Parse(Hymn(5, 4, 2)), 0));
        }

        // ---- the excerpt table -----------------------------------------------

        const string Table =
            "# header comment\n" +
            "#   more\n" +
            "\n" +
            "hymn_001   4       no     -         -   yes\n" +
            "hymn_002   3       yes    5,9,13    16  yes   # trailing comment\n" +
            "hymn_003   -       no     -         -   no\n" +
            "  hymn_004\t2   no   2,4,6   8   no\n";

        [Test]
        public void TableRowsParse()
        {
            var t = HymnExcerptTable.Parse(Table);
            Assert.IsEmpty(t.Errors);
            Assert.AreEqual(4, t.Rows.Count);
            var a = t.Find("hymn_001");
            Assert.AreEqual(4, a.Pattern);
            Assert.IsFalse(a.Pickup);
            Assert.IsNull(a.Lines);
            Assert.IsNull(a.End);
            Assert.IsTrue(a.Use);
            var b = t.Find("hymn_002");
            Assert.AreEqual(3, b.Pattern);
            Assert.IsTrue(b.Pickup);
            CollectionAssert.AreEqual(new[] { 5, 9, 13 }, b.Lines);
            Assert.AreEqual(16, b.End);
            var c = t.Find("hymn_003");
            Assert.AreEqual(0, c.Pattern);
            Assert.IsFalse(c.Use);
            Assert.AreEqual(2, t.Find("hymn_004").Pattern);
            Assert.IsNull(t.Find("nope"));
            CollectionAssert.AreEqual(new[] { "hymn_001", "hymn_002" }, t.UsedRows.Select(r => r.Hymn).ToArray());
        }

        [Test]
        public void MalformedRowsAreReportedNotThrown()
        {
            var t = HymnExcerptTable.Parse(
                "hymn_001 4 no - - yes\n" +
                "hymn_002 5 no - - yes\n" +              // pattern 5
                "hymn_003 4 maybe - - yes\n" +           // pickup
                "hymn_004 4 no 1,2 - yes\n" +            // two line starts
                "hymn_005 4 no 9,5,1 12 yes\n" +         // not ascending
                "hymn_006 4 no 1,5,9 7 yes\n" +          // end before the last line start
                "hymn_007 4 no - x yes\n" +              // end not a number
                "hymn_008 4 no - - perhaps\n" +          // use
                "hymn_009 4 no -\n" +                    // too few columns
                "hymn_010 - no - - yes\n" +              // used but no pattern
                "hymn_001 3 no - - yes\n" +              // duplicate
                "hymn_011 2 yes - 12 yes\n");            // fine: end alone
            Assert.AreEqual(2, t.Rows.Count);
            Assert.AreEqual(10, t.Errors.Count);
            StringAssert.StartsWith("line 2:", t.Errors[0]);
            StringAssert.StartsWith("line 11:", t.Errors[9]);
            Assert.IsNotNull(t.Find("hymn_011"));
            Assert.AreEqual(12, t.Find("hymn_011").End);
            Assert.IsNull(t.Find("hymn_011").Lines);
        }

        [Test]
        public void EmptyTextHasNoRowsAndNoErrors()
        {
            var t = HymnExcerptTable.Parse(null);
            Assert.IsEmpty(t.Rows);
            Assert.IsEmpty(t.Errors);
        }

        // ---- the cut ---------------------------------------------------------

        // 16 full 4/4 bars (bar 1 at 0 s), 2 s per bar.
        static HymnMidi Sixteen() => HymnMidi.Parse(Hymn(4, 4, 16));

        static HymnExcerpt Cut(HymnMidi m, HymnGrid g, string row, HymnLevel level)
        {
            var r = HymnExcerptTable.Parse(row).Rows.Single();
            string err;
            HymnExcerpt x;
            Assert.IsTrue(HymnExcerpt.TryCut(m, g, r, level, out x, out err), err);
            return x;
        }

        [Test]
        public void LevelsStartAtTheListedLinesAndEndAtTheEndBar()
        {
            var m = Sixteen();
            var g = HymnGrid.Build(m, 4);
            string row = "h 4 no 5,9,13 15 yes";
            var easy = Cut(m, g, row, HymnLevel.Easy);
            var med = Cut(m, g, row, HymnLevel.Medium);
            var hard = Cut(m, g, row, HymnLevel.Hard);

            Assert.AreEqual(5, easy.StartBar);
            Assert.AreEqual(9, med.StartBar);
            Assert.AreEqual(13, hard.StartBar);
            Assert.AreEqual(15, hard.EndBar);
            Assert.AreEqual(11 * 4, easy.Beats.Count);           // bars 5..15
            Assert.AreEqual(7 * 4, med.Beats.Count);
            Assert.AreEqual(3 * 4, hard.Beats.Count);
            Assert.AreEqual(0, hard.Beats[0].BarPosition);
            Assert.AreEqual(3, hard.Beats[hard.Beats.Count - 1].BarPosition);
            Assert.AreEqual(12 * 2.0, hard.StartTime, 1e-9);     // bar 13 starts after 12 bars of 2 s
            Assert.AreEqual(12 * 2.0 + 0.0, hard.Beats[0].Time, 1e-9);
            Assert.AreEqual(15 * 2.0, hard.EndTime, 1e-9);       // the end of bar 15
            Assert.AreEqual(hard.StartTime - Windows.GoodMs / 1000.0, hard.WindowStart, 1e-9);
            Assert.AreEqual(hard.EndTime, hard.WindowEnd, 1e-9);
        }

        [Test]
        public void FallbackIsTheLastFourFullBarsOfVerseOne()
        {
            var m = Sixteen();
            var g = HymnGrid.Build(m, 4);
            string row = "h 4 no - - yes";
            Assert.AreEqual(5, Cut(m, g, row, HymnLevel.Easy).StartBar);     // last 12 bars: 5..16
            Assert.AreEqual(9, Cut(m, g, row, HymnLevel.Medium).StartBar);   // last 8
            var hard = Cut(m, g, row, HymnLevel.Hard);
            Assert.AreEqual(13, hard.StartBar);                              // last 4
            Assert.AreEqual(16, hard.EndBar);
            Assert.AreEqual(16, hard.Beats.Count);
        }

        [Test]
        public void FallbackSkipsTheShortenedVerseEndBar()
        {
            // pickup + 15 full bars + a shortened 16th: the last full bar is 15
            var m = HymnMidi.Parse(Hymn(4, 4, 16, pickup: true, shortNum: 3));
            var g = HymnGrid.Build(m, 4);
            var hard = Cut(m, g, "h 4 yes - - yes", HymnLevel.Hard);
            Assert.AreEqual(12, hard.StartBar);
            Assert.AreEqual(15, hard.EndBar);
        }

        [Test]
        public void FallbackWithOnlyAnEndCountsBackFromIt()
        {
            var m = Sixteen();
            var g = HymnGrid.Build(m, 4);
            var hard = Cut(m, g, "h 4 no - 12 yes", HymnLevel.Hard);
            Assert.AreEqual(9, hard.StartBar);
            Assert.AreEqual(12, hard.EndBar);
        }

        [Test]
        public void ShortHymnsClampTheStartToTheFirstBar()
        {
            var m = HymnMidi.Parse(Hymn(4, 4, 6));
            var g = HymnGrid.Build(m, 4);
            var easy = Cut(m, g, "h 4 no - - yes", HymnLevel.Easy);
            Assert.AreEqual(1, easy.StartBar);
            Assert.AreEqual(6, easy.EndBar);
        }

        [Test]
        public void CutFailsOnBarsTheGridDoesNotHave()
        {
            var m = Sixteen();
            var g = HymnGrid.Build(m, 4);
            var r = HymnExcerptTable.Parse("h 4 no 5,9,13 40 yes").Rows.Single();
            HymnExcerpt x;
            string err;
            Assert.IsFalse(HymnExcerpt.TryCut(m, g, r, HymnLevel.Hard, out x, out err));
            Assert.IsNull(x);
            StringAssert.Contains("40", err);
        }

        [Test]
        public void ExcerptNotesAreTheOnesStartingInsideTheWindowInFileSeconds()
        {
            var m = Sixteen();
            var g = HymnGrid.Build(m, 4);
            var hard = Cut(m, g, "h 4 no - - yes", HymnLevel.Hard);     // bars 13..16 = 24 s .. 32 s
            Assert.AreEqual(16, hard.Notes.Count);                      // one note per quarter
            Assert.AreEqual(24.0, hard.Notes[0].Start, 1e-9);
            Assert.IsTrue(hard.Notes.All(n => n.Start >= hard.StartTime - 1e-9 && n.Start < hard.EndTime));
            Assert.AreEqual(60 + (48 % 12), hard.Notes[0].Pitch);
        }

        [Test]
        public void PickupBarCanOpenAnExcerpt()
        {
            var m = HymnMidi.Parse(Hymn(3, 4, 6, pickup: true));
            var g = HymnGrid.Build(m, 3);
            var x = Cut(m, g, "h 3 yes 0,2,4 6 yes", HymnLevel.Easy);
            Assert.AreEqual(0, x.StartBar);
            Assert.AreEqual(2, x.Beats[0].BarPosition);
            Assert.AreEqual(0.0, x.StartTime, 1e-9);
        }

        [Test]
        public void ExcerptBeatsWithAPerfectRightPatternPressListAreCorrect()
        {
            var m = Sixteen();
            var g = HymnGrid.Build(m, 4);
            var hard = Cut(m, g, "h 4 no - - yes", HymnLevel.Hard);
            var presses = hard.Beats.Select(b => new MeterPress { Action = Meter.KeyFor(b.BeatsPerBar, b.BarPosition), Time = b.Time }).ToList();
            var r = MeterJudge.Judge(hard.Beats, hard.WindowStart, hard.WindowEnd, presses);
            Assert.IsTrue(r.Correct);
            Assert.AreEqual(0, r.StartBeat);
            Assert.AreEqual(1.0, r.Accuracy, 1e-9);
        }

        [Test]
        public void JoiningMidBarFromTheExcerptStillCountsWithTheRightKeys()
        {
            var m = HymnMidi.Parse(Hymn(3, 4, 12, pickup: true));
            var g = HymnGrid.Build(m, 3);
            var x = Cut(m, g, "h 3 yes - - yes", HymnLevel.Hard);
            // start on the second beat of the first bar: left is wrong there, right is right
            var presses = x.Beats.Skip(1).Select(b => new MeterPress { Action = Meter.KeyFor(b.BeatsPerBar, b.BarPosition), Time = b.Time }).ToList();
            var r = MeterJudge.Judge(x.Beats, x.WindowStart, x.WindowEnd, presses);
            Assert.IsTrue(r.Correct);
            Assert.AreEqual(1, r.StartBeat);
        }

        // ---- the real files (optional) ---------------------------------------

        [Test]
        public void RealHymnsMatchTheExcerptTable()
        {
            string dir = Environment.GetEnvironmentVariable("BONANZA_HYMNS");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                Assert.Ignore("BONANZA_HYMNS not set: real hymn files are private and not part of this repo.");
            string tablePath = Path.Combine(dir, "excerpts.txt");
            if (!File.Exists(tablePath)) Assert.Ignore("no excerpts.txt in BONANZA_HYMNS");

            var table = HymnExcerptTable.Parse(File.ReadAllText(tablePath));
            Assert.IsEmpty(table.Errors, string.Join("; ", table.Errors));
            var failures = new List<string>();
            foreach (var row in table.UsedRows)
            {
                string path = Path.Combine(dir, row.Hymn + ".mid");
                if (!File.Exists(path)) { failures.Add(row.Hymn + ": file missing"); continue; }
                try
                {
                    var m = HymnMidi.Parse(File.ReadAllBytes(path));
                    var g = HymnGrid.Build(m, 0);
                    var problems = new List<string>();
                    if (g.Pattern != row.Pattern) problems.Add("pattern " + g.Pattern + " != " + row.Pattern);
                    if (g.HasPickup != row.Pickup) problems.Add("pickup " + g.HasPickup + " != " + row.Pickup);
                    if (m.VerseEndTick >= m.EndTick && m.TimeSignatures.Count > 1) problems.Add("verse 1 not separated");
                    foreach (HymnLevel level in Enum.GetValues(typeof(HymnLevel)))
                    {
                        HymnExcerpt x;
                        string err;
                        var fallback = new HymnExcerptRow { Hymn = row.Hymn, Pattern = row.Pattern, Pickup = row.Pickup, Use = true };
                        if (!HymnExcerpt.TryCut(m, g, fallback, level, out x, out err)) problems.Add(level + ": " + err);
                        else if (x.Beats.Count == 0 || x.Notes.Count == 0) problems.Add(level + ": empty excerpt");
                    }
                    TestContext.Progress.WriteLine("{0}: {1}/{2} conducted in {3}{4}, verse 1 = {5} bars, {6} beats, {7:0.0} s: {8}",
                        row.Hymn, g.Numerator, g.Denominator, g.Pattern, g.HasPickup ? " with pickup" : "",
                        g.Bars.Count, g.Beats.Count, m.SecondsAt(m.VerseEndTick),
                        problems.Count == 0 ? "ok" : string.Join("; ", problems));
                    if (problems.Count > 0) failures.Add(row.Hymn + ": " + string.Join("; ", problems));
                }
                catch (Exception e) { failures.Add(row.Hymn + ": " + e.Message); }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
