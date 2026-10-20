// Hymn Meter — the per-hymn excerpt table (hymns/excerpts.txt, parsed from text) and the
// excerpt cut for a level.
//
// Table: `#` starts a comment; blank lines are skipped; each row is six whitespace-separated
// columns: hymn pattern pickup lines end use. pattern = 2, 3 or 4 (or "-" for an unused
// hymn), pickup = yes/no, lines = three ascending bar numbers (the starts of the last
// three lines of verse 1, earliest first) or "-", end = a bar number or "-", use = yes/no.
// A malformed row is skipped and reported in Errors ("line N: ..."); parsing never throws,
// so the game can log one [ERROR] line.
//
// Cut: Easy starts at lines[0], Medium at lines[1], Hard at lines[2] (the last line); every
// level ends at the end of bar `end` (inclusive) of verse 1. Fallback when the table has
// "-": the last line is the last 4 FULL bars of verse 1 (a pickup or cut-short verse-end
// bar is not full), so with end bar E the starts are E-11 (Easy, 12 bars), E-7 (Medium, 8)
// and E-3 (Hard, 4), clamped to the first bar; `end` "-" means the last full bar. Lines
// given with end "-" end at the last full bar; end given with lines "-" counts back from it.
//
// Times are absolute: seconds from the file start, the same clock the beats use, so
// the beats can go straight into MeterJudge.Judge with the excerpt's window. A caller that
// plays the excerpt on its own clock subtracts StartTime.
//
// No UnityEngine here: this assembly is pure C# so it stays test-pinned.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Bonanza.Core
{
    public enum HymnLevel { Easy, Medium, Hard }

    public sealed class HymnExcerptRow
    {
        public string Hymn = "";
        public int Pattern;         // 0 when the table has "-"
        public bool Pickup;
        public int[] Lines;         // three ascending bar numbers, null when "-"
        public int? End;            // null when "-"
        public bool Use;
        public int Line;            // 1-based line in the text
    }

    public sealed class HymnExcerptTable
    {
        public readonly List<HymnExcerptRow> Rows = new List<HymnExcerptRow>();
        public readonly List<string> Errors = new List<string>();

        public IEnumerable<HymnExcerptRow> UsedRows => Rows.Where(r => r.Use);

        public HymnExcerptRow Find(string hymn) => Rows.FirstOrDefault(r => r.Hymn == hymn);

        public static HymnExcerptTable Parse(string text)
        {
            var t = new HymnExcerptTable();
            if (string.IsNullOrEmpty(text)) return t;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                string s = lines[n];
                int hash = s.IndexOf('#');
                if (hash >= 0) s = s.Substring(0, hash);
                s = s.Trim();
                if (s.Length == 0) continue;
                string err;
                var row = ParseRow(s, n + 1, out err);
                if (row != null && t.Rows.Any(r => r.Hymn == row.Hymn)) { row = null; err = "duplicate hymn " + s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)[0]; }
                if (row == null) t.Errors.Add("line " + (n + 1) + ": " + err);
                else t.Rows.Add(row);
            }
            return t;
        }

        static HymnExcerptRow ParseRow(string s, int lineNo, out string err)
        {
            err = null;
            var c = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (c.Length != 6) { err = "expected 6 columns (hymn pattern pickup lines end use), got " + c.Length; return null; }
            var row = new HymnExcerptRow { Hymn = c[0], Line = lineNo };

            bool yes;
            if (c[1] != "-")
            {
                int p;
                if (!int.TryParse(c[1], out p) || p < 2 || p > 4) { err = "pattern must be 2, 3, 4 or -, got " + c[1]; return null; }
                row.Pattern = p;
            }
            if (!YesNo(c[2], out yes)) { err = "pickup must be yes or no, got " + c[2]; return null; }
            row.Pickup = yes;

            if (c[3] != "-")
            {
                var parts = c[3].Split(',');
                var v = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    if (!int.TryParse(parts[i], out v[i]) || v[i] < 0) { err = "lines must be bar numbers, got " + c[3]; return null; }
                if (v.Length != 3) { err = "lines needs three bar numbers, got " + c[3]; return null; }
                if (!(v[0] < v[1] && v[1] < v[2])) { err = "lines must ascend, got " + c[3]; return null; }
                row.Lines = v;
            }
            if (c[4] != "-")
            {
                int e;
                if (!int.TryParse(c[4], out e) || e < 0) { err = "end must be a bar number or -, got " + c[4]; return null; }
                row.End = e;
            }
            if (row.Lines != null && row.End.HasValue && row.End.Value < row.Lines[2]) { err = "end " + row.End + " is before the last line start " + row.Lines[2]; return null; }

            if (!YesNo(c[5], out yes)) { err = "use must be yes or no, got " + c[5]; return null; }
            row.Use = yes;
            if (row.Use && row.Pattern == 0) { err = "a hymn in use needs a pattern"; return null; }
            return row;
        }

        static bool YesNo(string s, out bool value)
        {
            value = s == "yes";
            return s == "yes" || s == "no";
        }
    }

    /// A note of the excerpt, in file seconds.
    public sealed class HymnTimedNote
    {
        public int Track;
        public int Channel;
        public int Pitch;
        public int Velocity;
        public double Start;
        public double End;
    }

    public sealed class HymnExcerpt
    {
        public HymnLevel Level;
        public int StartBar;
        public int EndBar;
        public double StartTime;        // the first beat of the start bar
        public double EndTime;          // the end of the end bar (cut at the end of verse 1)
        public double WindowStart;      // StartTime less the GOOD window, so a slightly early first press counts
        public double WindowEnd;        // = EndTime
        /// Every beat from the start bar to the end bar: what MeterJudge.Judge takes.
        public List<MeterBeat> Beats = new List<MeterBeat>();
        /// The notes that START inside [StartTime, EndTime), for playback.
        public List<HymnTimedNote> Notes = new List<HymnTimedNote>();

        /// Cut the excerpt of `row` for `level` out of verse 1. False (with a message) when
        /// the table names bars the file does not have.
        public static bool TryCut(HymnMidi midi, HymnGrid grid, HymnExcerptRow row, HymnLevel level,
                                  out HymnExcerpt excerpt, out string error)
        {
            excerpt = null;
            error = null;
            int firstBar = grid.Bars.Count > 0 ? grid.Bars[0].Number : 1;
            int endBar = row.End ?? grid.LastFullBar;
            if (endBar < 0) { error = row.Hymn + ": no full bar in verse 1"; return false; }

            int startBar;
            if (row.Lines != null) startBar = row.Lines[(int)level];
            else
            {
                int bars = level == HymnLevel.Hard ? 4 : level == HymnLevel.Medium ? 8 : 12;
                startBar = Math.Max(firstBar, endBar - bars + 1);
            }

            var sb = grid.Bar(startBar);
            var eb = grid.Bar(endBar);
            if (sb == null) { error = row.Hymn + ": verse 1 has no bar " + startBar; return false; }
            if (eb == null) { error = row.Hymn + ": verse 1 has no bar " + endBar; return false; }
            if (endBar < startBar) { error = row.Hymn + ": end bar " + endBar + " is before start bar " + startBar; return false; }

            var x = new HymnExcerpt { Level = level, StartBar = startBar, EndBar = endBar };
            int first = sb.FirstBeat, last = eb.FirstBeat + eb.BeatCount - 1;
            for (int i = first; i <= last; i++) x.Beats.Add(grid.Beats[i].ToMeterBeat());
            x.StartTime = grid.Beats[first].Time;
            x.EndTime = midi.SecondsAt(eb.EndTick);
            x.WindowStart = x.StartTime - Windows.GoodMs / 1000.0;
            x.WindowEnd = x.EndTime;

            int startTick = grid.Beats[first].Tick, endTick = eb.EndTick;
            foreach (var n in midi.Notes)
            {
                if (n.StartTick < startTick || n.StartTick >= endTick) continue;
                x.Notes.Add(new HymnTimedNote
                {
                    Track = n.Track, Channel = n.Channel, Pitch = n.Pitch, Velocity = n.Velocity,
                    Start = midi.SecondsAt(n.StartTick), End = midi.SecondsAt(n.EndTick),
                });
            }
            excerpt = x;
            return true;
        }
    }
}
