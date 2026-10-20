// Shared synthetic hymn builders for the tests: a Standard MIDI File writer and a hymn
// generator. Everything is built in memory; no real hymn data lives in this repo.

using System;
using System.Collections.Generic;
using System.Linq;

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

    public static class SyntheticHymn
    {
        const int Q = 480;

        /// A hymn of `verses` verses, `bars` full bars of `num`/`den` per verse, written the
        // way the real files are: the tempo + time-signature block repeats at each verse
        // start. A pickup adds a leading 1/4 bar (then the working signature at tick 480).
        // `shortSig` re-signs the last bar of each verse as that signature (the shortened
        // verse-end bar); that bar then lasts `shortTicks` and the next block follows it.
        public static byte[] Build(int num, int den, int bars, bool pickup = false, int verses = 2,
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
    }
}
