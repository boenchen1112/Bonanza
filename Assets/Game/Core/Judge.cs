// Timing judgement. Ported as-is from the web prototype's web/src/core/judge.js.
//
// Windows are in milliseconds, not beats: human timing precision is roughly
// tempo-independent, so the windows are too. Generous at the outer edge,
// tight at the centre — almost nobody outright misses a note they tried to
// hit, but PERFECT still has to be earned.
//
// No UnityEngine here: this assembly is pure C# so it stays test-pinned.

using System;
using System.Collections.Generic;

namespace RhythmGame.Core
{
    public enum Verdict { Miss = 0, Good = 1, Great = 2, Perfect = 3 }

    public static class Windows
    {
        public const double PerfectMs = 42;
        public const double GreatMs = 82;
        public const double GoodMs = 128;
        /// Beyond this a press belongs to no note at all: it is swallowed rather
        /// than burning the note, so mashing between notes doesn't destroy a run.
        public const double ClaimMs = 165;
    }

    public static class Scoring
    {
        public static int PointsFor(Verdict v)
        {
            switch (v)
            {
                case Verdict.Perfect: return 1000;
                case Verdict.Great: return 600;
                case Verdict.Good: return 250;
                default: return 0;
            }
        }

        public static Verdict VerdictFor(double errMs)
        {
            double a = Math.Abs(errMs);
            if (a <= Windows.PerfectMs) return Verdict.Perfect;
            if (a <= Windows.GreatMs) return Verdict.Great;
            if (a <= Windows.GoodMs) return Verdict.Good;
            return Verdict.Miss;
        }

        /// Party-game ranks: generous, but S is rare enough to be a moment.
        public static string RankFor(double accuracy, int missCount)
        {
            if (missCount == 0 && accuracy >= 0.98) return "S";
            if (accuracy >= 0.92) return "A";
            if (accuracy >= 0.80) return "B";
            if (accuracy >= 0.65) return "C";
            return "D";
        }
    }

    /// A note the player is expected to hit.
    public sealed class Note
    {
        public double Time;            // audio time, seconds
        public string Action = "a";
        public object Data;
        public bool Judged;
        public bool Hit;
        public Verdict Verdict;
        public double ErrMs;
    }

    public struct Judgement
    {
        public Note Note;
        public Verdict Verdict;
        public double ErrMs;
    }

    public sealed class JudgeStats
    {
        public int Perfect, Great, Good, Miss, Combo, MaxCombo, Score;
        public readonly List<double> Errors = new List<double>();

        public int CountOf(Verdict v)
        {
            switch (v)
            {
                case Verdict.Perfect: return Perfect;
                case Verdict.Great: return Great;
                case Verdict.Good: return Good;
                default: return Miss;
            }
        }
    }

    /// Matches presses to notes and expires notes that were never hit.
    ///
    /// The matching rule matters more than the windows: a press claims the
    /// NEAREST unjudged note within the claim window, not simply the next one.
    /// With dense charts the naive rule makes a slightly-late press eat the
    /// following note and cascade the whole run into failure.
    public sealed class NoteJudge
    {
        readonly List<Note> _notes = new List<Note>();
        int _cursor;

        /// Player latency calibration, ADDED to press times (a player who
        /// presses late needs a negative offset).
        public double OffsetMs;
        public JudgeStats Stats { get; private set; } = new JudgeStats();
        public event Action<Note, Verdict, double> Judged;

        public NoteJudge(double offsetMs = 0) { OffsetMs = offsetMs; }

        public IReadOnlyList<Note> Notes => _notes;

        public NoteJudge Load(IEnumerable<Note> notes)
        {
            _notes.Clear();
            foreach (var n in notes)
            {
                n.Judged = false;
                n.Hit = false;
                _notes.Add(n);
            }
            // Stable sort by time (List.Sort is not stable).
            var ordered = new List<Note>(_notes);
            ordered.Sort((a, b) =>
            {
                int c = a.Time.CompareTo(b.Time);
                return c != 0 ? c : _notes.IndexOf(a).CompareTo(_notes.IndexOf(b));
            });
            _notes.Clear();
            _notes.AddRange(ordered);
            _cursor = 0;
            Stats = new JudgeStats();
            return this;
        }

        public bool Finished => _cursor >= _notes.Count;

        public int Remaining
        {
            get
            {
                int n = 0;
                for (int i = _cursor; i < _notes.Count; i++) if (!_notes[i].Judged) n++;
                return n;
            }
        }

        /// Offer a press. Returns the judgement, or null if no note claimed it.
        public Judgement? Press(string action, double time)
        {
            double t = time + OffsetMs / 1000.0;
            double claim = Windows.ClaimMs / 1000.0;

            int best = -1;
            double bestErr = double.PositiveInfinity;
            for (int i = _cursor; i < _notes.Count; i++)
            {
                var n = _notes[i];
                if (n.Time - t > claim) break; // sorted: everything after is further away
                if (n.Judged) continue;
                if (n.Action != action) continue;
                double err = t - n.Time;
                if (Math.Abs(err) > claim) continue;
                if (Math.Abs(err) < Math.Abs(bestErr)) { best = i; bestErr = err; }
            }
            if (best < 0) return null;

            var note = _notes[best];
            double errMs = bestErr * 1000.0;
            return Commit(note, Scoring.VerdictFor(errMs), errMs);
        }

        /// Expire notes whose window has fully passed. Call every frame.
        public void Update(double now)
        {
            double claim = Windows.ClaimMs / 1000.0;
            for (int i = _cursor; i < _notes.Count; i++)
            {
                var n = _notes[i];
                if (n.Time + claim > now) break;
                if (!n.Judged) Commit(n, Verdict.Miss, claim * 1000.0);
            }
            while (_cursor < _notes.Count && _notes[_cursor].Judged) _cursor++;
        }

        Judgement Commit(Note note, Verdict verdict, double errMs)
        {
            note.Judged = true;
            note.Verdict = verdict;
            note.ErrMs = errMs;
            note.Hit = verdict != Verdict.Miss;

            var s = Stats;
            switch (verdict)
            {
                case Verdict.Perfect: s.Perfect++; break;
                case Verdict.Great: s.Great++; break;
                case Verdict.Good: s.Good++; break;
                default: s.Miss++; break;
            }
            s.Score += Scoring.PointsFor(verdict);
            if (note.Hit)
            {
                s.Combo++;
                s.MaxCombo = Math.Max(s.MaxCombo, s.Combo);
                s.Errors.Add(errMs);
            }
            else
            {
                s.Combo = 0;
            }
            Judged?.Invoke(note, verdict, errMs);
            return new Judgement { Note = note, Verdict = verdict, ErrMs = errMs };
        }

        /// Accuracy 0..1 weighted by verdict value.
        public double Accuracy
        {
            get
            {
                var s = Stats;
                int total = s.Perfect + s.Great + s.Good + s.Miss;
                if (total == 0) return 1;
                return (double)s.Score / (total * Scoring.PointsFor(Verdict.Perfect));
            }
        }

        /// Mean signed error in ms: tells a player they are consistently rushing.
        public double Bias
        {
            get
            {
                var e = Stats.Errors;
                if (e.Count == 0) return 0;
                double sum = 0;
                foreach (var x in e) sum += x;
                return sum / e.Count;
            }
        }
    }
}
