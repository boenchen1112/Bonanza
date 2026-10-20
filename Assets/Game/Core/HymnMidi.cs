// Hymn Meter — a minimal Standard MIDI File reader. Just what the hymn files need:
// format 0 and 1, running status, tempo (FF 51), time signature (FF 58), end of track,
// note on / note off (note-on with velocity 0 is a note-off). Sysex and every other meta
// event are skipped. Ticks become seconds through the tempo map, so a fermata-style tempo
// drop moves everything after it.
//
// The hymn files hold every verse back to back and repeat the tempo + time-signature
// block at each verse start. VerseEndTick is the start of the second block, so verse 1 is
// tick 0 to VerseEndTick (HymnGrid.cs cuts the beats and bars from that).
//
// No UnityEngine here: this assembly is pure C# so it stays test-pinned.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Bonanza.Core
{
    public sealed class HymnTempo
    {
        public int Tick;
        public int MicrosPerQuarter;
        public double Bpm => 60000000.0 / MicrosPerQuarter;
    }

    public sealed class HymnTimeSig
    {
        public int Tick;
        public int Numerator;
        public int Denominator;     // 4 for x/4, 8 for x/8 (decoded from the power of two)
    }

    public sealed class HymnNote
    {
        public int Track;           // index of the track in the file
        public int Channel;
        public int Pitch;
        public int Velocity;
        public int StartTick;
        public int EndTick;
    }

    public sealed class HymnMidi
    {
        /// Ticks per quarter note (the file's division).
        public int TicksPerQuarter { get; private set; }

        /// Tempo changes by tick, one per tick (the last one wins), always starting at
        /// tick 0 (120 bpm if the file never says, as the MIDI spec has it).
        public IReadOnlyList<HymnTempo> Tempos => _tempos;

        /// Time signatures by tick, one per tick, always starting at tick 0 (4/4 if the
        /// file never says).
        public IReadOnlyList<HymnTimeSig> TimeSignatures => _sigs;

        /// All notes, ordered by start tick (then track, then pitch). A note never closed
        /// ends with its track.
        public IReadOnlyList<HymnNote> Notes => _notes;

        /// The last tick of any track (its end-of-track event or last event).
        public int EndTick { get; private set; }

        /// Where verse 1 ends: the first later tick that repeats the file's first time
        /// signature (the repeated meta block of the next verse), else EndTick.
        public int VerseEndTick { get; private set; }

        readonly List<HymnTempo> _tempos = new List<HymnTempo>();
        readonly List<HymnTimeSig> _sigs = new List<HymnTimeSig>();
        readonly List<HymnNote> _notes = new List<HymnNote>();
        double[] _tempoSeconds;     // seconds at each tempo entry's tick

        HymnMidi() { }

        /// Seconds from tick 0, following the tempo map.
        public double SecondsAt(int tick)
        {
            int i = 0;
            for (int k = _tempos.Count - 1; k >= 0; k--)
                if (_tempos[k].Tick <= tick) { i = k; break; }
            double perTick = _tempos[i].MicrosPerQuarter / 1e6 / TicksPerQuarter;
            return _tempoSeconds[i] + (tick - _tempos[i].Tick) * perTick;
        }

        /// The same notes under another tempo map and time-signature list (HymnTempoMap.Apply). Same
        /// normalising as the file's own: one entry per tick, a default at tick 0, verse 1 found again.
        public HymnMidi WithMaps(IEnumerable<HymnTempo> tempos, IEnumerable<HymnTimeSig> sigs)
        {
            var m = new HymnMidi { TicksPerQuarter = TicksPerQuarter, EndTick = EndTick };
            m._notes.AddRange(_notes);
            m.Normalize(tempos.ToList(), sigs.ToList());
            return m;
        }

        /// Throws FormatException for anything that is not a readable SMF.
        public static HymnMidi Parse(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < 14 || data[0] != 'M' || data[1] != 'T' || data[2] != 'h' || data[3] != 'd')
                throw new FormatException("not a MIDI file");
            int headerLen = Int32At(data, 4);
            int format = Int16At(data, 8);
            int tracks = Int16At(data, 10);
            int division = Int16At(data, 12);
            if (format > 1) throw new FormatException("MIDI format " + format + " is not supported");
            if ((division & 0x8000) != 0 || division == 0) throw new FormatException("SMPTE timing is not supported");

            var m = new HymnMidi { TicksPerQuarter = division };
            var rawTempos = new List<HymnTempo>();
            var rawSigs = new List<HymnTimeSig>();
            int pos = 8 + headerLen, trackIndex = 0;
            while (trackIndex < tracks && pos + 8 <= data.Length)
            {
                int len = Int32At(data, pos + 4);
                int body = pos + 8;
                if (len < 0 || body + len > data.Length) throw new FormatException("truncated track chunk");
                bool isTrack = data[pos] == 'M' && data[pos + 1] == 'T' && data[pos + 2] == 'r' && data[pos + 3] == 'k';
                if (isTrack)
                {
                    int last = ParseTrack(data, body, body + len, trackIndex, m._notes, rawTempos, rawSigs);
                    if (last > m.EndTick) m.EndTick = last;
                    trackIndex++;
                }
                pos = body + len;
            }
            if (trackIndex == 0) throw new FormatException("no tracks");

            m._notes.Sort((a, b) =>
            {
                int c = a.StartTick.CompareTo(b.StartTick);
                if (c != 0) return c;
                c = a.Track.CompareTo(b.Track);
                return c != 0 ? c : a.Pitch.CompareTo(b.Pitch);
            });
            m.Normalize(rawTempos, rawSigs);
            return m;
        }

        void Normalize(List<HymnTempo> rawTempos, List<HymnTimeSig> rawSigs)
        {
            // stable by tick, one entry per tick (the last written wins)
            foreach (var t in rawTempos.Select((v, i) => new { v, i }).OrderBy(x => x.v.Tick).ThenBy(x => x.i).Select(x => x.v))
            {
                if (_tempos.Count > 0 && _tempos[_tempos.Count - 1].Tick == t.Tick) _tempos[_tempos.Count - 1] = t;
                else _tempos.Add(t);
            }
            if (_tempos.Count == 0 || _tempos[0].Tick > 0)
                _tempos.Insert(0, new HymnTempo { Tick = 0, MicrosPerQuarter = 500000 });
            foreach (var s in rawSigs.Select((v, i) => new { v, i }).OrderBy(x => x.v.Tick).ThenBy(x => x.i).Select(x => x.v))
            {
                if (_sigs.Count > 0 && _sigs[_sigs.Count - 1].Tick == s.Tick) _sigs[_sigs.Count - 1] = s;
                else _sigs.Add(s);
            }
            if (_sigs.Count == 0 || _sigs[0].Tick > 0)
                _sigs.Insert(0, new HymnTimeSig { Tick = 0, Numerator = 4, Denominator = 4 });

            _tempoSeconds = new double[_tempos.Count];
            for (int i = 1; i < _tempos.Count; i++)
                _tempoSeconds[i] = _tempoSeconds[i - 1] +
                    (_tempos[i].Tick - _tempos[i - 1].Tick) * (_tempos[i - 1].MicrosPerQuarter / 1e6 / TicksPerQuarter);

            VerseEndTick = EndTick;
            var first = _sigs[0];
            foreach (var s in _sigs)
            {
                if (s.Tick > 0 && s.Tick < EndTick && s.Numerator == first.Numerator && s.Denominator == first.Denominator)
                {
                    VerseEndTick = s.Tick;
                    break;
                }
            }
        }

        // Reads one track; returns its last tick.
        static int ParseTrack(byte[] d, int pos, int end, int track, List<HymnNote> notes,
                              List<HymnTempo> tempos, List<HymnTimeSig> sigs)
        {
            int tick = 0, running = 0;
            var open = new Dictionary<int, HymnNote>();     // (channel, pitch) of the sounding notes
            while (pos < end)
            {
                tick += (int)Vlq(d, ref pos, end);
                int status = Byte(d, pos, end);
                if (status >= 0x80) { pos++; if (status < 0xF0) running = status; }
                else
                {
                    if (running == 0) throw new FormatException("data byte without a status");
                    status = running;
                }

                if (status == 0xFF)
                {
                    int type = Byte(d, pos++, end);
                    int len = (int)Vlq(d, ref pos, end);
                    if (len < 0 || pos + len > end) throw new FormatException("truncated meta event");
                    if (type == 0x51 && len == 3)
                        tempos.Add(new HymnTempo { Tick = tick, MicrosPerQuarter = (d[pos] << 16) | (d[pos + 1] << 8) | d[pos + 2] });
                    else if (type == 0x58 && len >= 2)
                        sigs.Add(new HymnTimeSig { Tick = tick, Numerator = d[pos], Denominator = 1 << d[pos + 1] });
                    pos += len;
                    running = 0;
                    if (type == 0x2F) break;
                }
                else if (status == 0xF0 || status == 0xF7)
                {
                    int len = (int)Vlq(d, ref pos, end);
                    if (len < 0 || pos + len > end) throw new FormatException("truncated sysex");
                    pos += len;
                    running = 0;
                }
                else if (status >= 0xF1)
                {
                    throw new FormatException("unsupported status byte " + status);
                }
                else
                {
                    int kind = status & 0xF0, ch = status & 0x0F;
                    int a = Byte(d, pos++, end);
                    int b = (kind == 0xC0 || kind == 0xD0) ? 0 : Byte(d, pos++, end);
                    bool on = kind == 0x90 && b > 0;
                    bool off = kind == 0x80 || (kind == 0x90 && b == 0);
                    int key = ch * 128 + a;
                    if (on || off)
                    {
                        HymnNote n;
                        if (open.TryGetValue(key, out n)) { n.EndTick = tick; open.Remove(key); }
                    }
                    if (on)
                    {
                        var n = new HymnNote { Track = track, Channel = ch, Pitch = a, Velocity = b, StartTick = tick, EndTick = tick };
                        open[key] = n;
                        notes.Add(n);
                    }
                }
            }
            foreach (var n in open.Values) n.EndTick = tick;
            return tick;
        }

        static int Byte(byte[] d, int pos, int end)
        {
            if (pos >= end) throw new FormatException("truncated event");
            return d[pos];
        }

        static long Vlq(byte[] d, ref int pos, int end)
        {
            long v = 0;
            for (int i = 0; i < 4; i++)
            {
                int b = Byte(d, pos++, end);
                v = (v << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0) return v;
            }
            throw new FormatException("bad variable-length number");
        }

        static int Int32At(byte[] d, int p) => (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];
        static int Int16At(byte[] d, int p) => (d[p] << 8) | d[p + 1];
    }
}
