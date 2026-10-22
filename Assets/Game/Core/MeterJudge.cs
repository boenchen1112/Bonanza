// Hymn Meter — the answer boxes. Presses plus a beat grid in, the player's answer out.
//
// THE LOOP. The first key must be a pattern's first key (down = 4 boxes, left = 3, right = 2)
// pressed on a beat (BeatWindow: the press claims the nearest beat within a fraction of that
// beat; Scoring points per grade); it sets the box count and claims that beat as the downbeat.
// Each following key, on the next beat, fills the next box with the pattern's next key. A wrong
// key, or a beat left unpressed (its window and LateGrace passed), empties the boxes; the player
// starts again on a downbeat. A press that claims no beat (or a beat already claimed) is a free
// tick: no effect. Once the boxes are full more keys do nothing and every beat lights one box:
// (beats since the claimed downbeat) mod count, so box 1 lights on the beat the player claimed.
// Clear empties (also a full answer); Submit works only when full and then locks the answer:
// no resubmit, no clear, later keys ignored.
//
// CORRECT = the submitted pattern is the hymn's pattern AND box 1 sat on a true downbeat (bar
// position 0). Accuracy = mean Scoring points of the beat grades over the boxes / the perfect
// points.
//
// Ranking ("who first"): correct, then answered before the reveal, then the earlier Submit time,
// then accuracy. HymnRound decides what pre-reveal means (PostReveal).
//
// MeterAnswer is the incremental model the runtime queries every frame (no allocation after the
// constructor); MeterJudge.Judge replays a press list through it for tests and CPUs.
//
// No UnityEngine here: this assembly is pure C# so it stays test-pinned.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Bonanza.Core
{
    /// One beat of the grid: audio time, 0-based position in its bar, and the beats per
    /// bar of that bar (the grid does not assume one meter).
    public sealed class MeterBeat
    {
        public double Time;
        public int BarPosition;
        public int BeatsPerBar;
        /// Step height of this beat, 1 = lowest = heaviest (Meter.StepHeight), 0 if the bar is no pattern.
        public int StepHeight => Meter.StepHeight(BeatsPerBar, BarPosition);
    }

    /// A key the player pressed: an arrow, "b" (clear) or "a" (submit), at an audio time.
    public sealed class MeterPress
    {
        public string Action = "";
        public double Time;
    }

    public sealed class MeterResult
    {
        public bool Correct;             // submitted, the hymn's pattern, box 1 on a true downbeat
        public bool Submitted;           // Submit was pressed on a full answer (right or wrong)
        public double SubmitTime = double.PositiveInfinity;   // audio time of the Submit, never if not submitted
        public bool PostReveal;          // submitted after the reveal (set by the round)
        public int Count;                // boxes of the submitted answer, 0 if none
        public int StartBeat = -1;       // grid index of the beat claimed as the downbeat, -1 if not submitted
        public double StartTime;         // its audio time
        public double Accuracy;          // mean points / max over the boxes
    }

    /// One player's boxes for one excerpt. Feed it Press / Clear / Submit as the keys come (in time
    /// order) and Update(now) each frame; read the state below.
    public sealed class MeterAnswer
    {
        /// How long past a beat's window the clock waits before it calls the beat missed, so a key
        /// delivered a frame late is not lost to Update. A Press at its own time is exact.
        public const double LateGrace = 0.1;

        readonly IReadOnlyList<MeterBeat> _beats;
        readonly double[] _len;      // beat lengths
        readonly double[] _good;     // half windows, seconds
        readonly double _windowStart, _windowEnd;
        readonly string[] _keys = new string[4];
        int _count, _filled, _start = -1, _last = -1;
        double _points, _submitTime = double.PositiveInfinity;
        bool _locked;

        /// `pattern`: the hymn's beats per bar (4, 3 or 2). Keys and Submit outside
        /// [windowStart, windowEnd] (audio seconds) are ignored.
        public MeterAnswer(IReadOnlyList<MeterBeat> beats, int pattern, double windowStart, double windowEnd)
        {
            _beats = beats;
            Pattern = pattern;
            _windowStart = windowStart;
            _windowEnd = windowEnd;
            int n = beats.Count;
            var times = new double[n];
            for (int i = 0; i < n; i++) times[i] = beats[i].Time;
            _len = new double[n];
            _good = new double[n];
            for (int i = 0; i < n; i++)
            {
                _len[i] = BeatWindow.LengthAt(times, i);
                _good[i] = BeatWindow.GoodMs(_len[i]) / 1000.0;
            }
        }

        /// The hymn's pattern (beats per bar).
        public int Pattern { get; }
        /// Boxes of this answer: 0 until a first key is in, then 4, 3 or 2.
        public int Count => _count;
        /// Boxes filled so far.
        public int Filled => _filled;
        public bool Full => _count > 0 && _filled == _count;
        public bool Locked => _locked;
        /// Audio time of the Submit, +infinity until locked.
        public double SubmitTime => _submitTime;
        /// Grid index of the beat claimed as the downbeat (box 1), -1 when empty.
        public int StartBeat => _start;
        /// Mean grade of the filled boxes, 0..1.
        public double Accuracy => _filled == 0 ? 0 : _points / (_filled * (double)Scoring.PointsFor(Verdict.Perfect));
        /// The key in box `i` (0-based): "down" "left" "right" "up", null if not filled.
        public string KeyAt(int i) => i >= 0 && i < _filled ? _keys[i] : null;

        /// Full, of the hymn's pattern, and box 1 on a true downbeat (before and without Submit).
        public bool MatchesHymn => Full && _count == Pattern && _beats[_start].BarPosition == 0;

        /// The box that lights at audio time `now`: (beats since the claimed downbeat) mod count once
        /// full, -1 before. Keeps going after the lock.
        public int LitBox(double now)
        {
            if (!Full) return -1;
            int cur = -1;
            for (int i = _beats.Count - 1; i >= 0; i--) if (_beats[i].Time <= now) { cur = i; break; }
            return cur < _start ? -1 : (cur - _start) % _count;
        }

        /// An arrow key at `time`. True if the boxes changed.
        public bool Press(string action, double time)
        {
            if (_locked || time < _windowStart || time > _windowEnd) return false;
            Advance(time, 0);
            if (Full) return false;   // full: more keys do nothing
            int beat = Claim(time);
            if (beat < 0 || beat <= _last) return false;   // a free tick
            if (_filled > 0 && beat != _start + _filled) Empty();
            if (_filled == 0)
            {
                int pattern = Meter.PatternStartedBy(action);
                if (pattern == 0) return false;
                _count = pattern;
                _start = beat;
                Fill(action, beat, time);
                return true;
            }
            _last = beat;
            if (action == Meter.KeyFor(_count, _filled)) Fill(action, beat, time);
            else Empty();
            return true;
        }

        /// Empty the boxes. False if there was nothing to empty or the answer is locked.
        public bool Clear()
        {
            if (_locked || _filled == 0) return false;
            Empty();
            _last = -1;
            return true;
        }

        /// Lock a full answer at `time`. False (nothing happens) if it is not full, already locked, or outside the window.
        public bool Submit(double time)
        {
            if (_locked || !Full || time < _windowStart || time > _windowEnd) return false;
            _locked = true;
            _submitTime = time;
            return true;
        }

        /// The clock: a half-filled answer whose next beat is long past is emptied.
        public void Update(double now) => Advance(now, LateGrace);

        /// What this answer is worth to the round. Correct needs the Submit.
        public MeterResult Result()
        {
            var r = new MeterResult { Submitted = _locked, SubmitTime = _submitTime };
            if (!_locked) return r;
            r.Correct = MatchesHymn;
            r.Count = _count;
            r.StartBeat = _start;
            r.StartTime = _beats[_start].Time;
            r.Accuracy = Accuracy;
            return r;
        }

        void Fill(string action, int beat, double time)
        {
            _keys[_filled++] = action;
            _last = beat;
            _points += Scoring.PointsFor(BeatWindow.VerdictFor((time - _beats[beat].Time) * 1000.0, _len[beat]));
        }

        void Empty()
        {
            _count = 0;
            _filled = 0;
            _start = -1;
            _points = 0;
        }

        // The beat the next key is due on has run out of window: the run is broken.
        void Advance(double now, double grace)
        {
            if (_locked || _filled == 0 || _filled >= _count) return;
            int next = _start + _filled;
            if (next >= _beats.Count || now > _beats[next].Time + _good[next] + grace) Empty();
        }

        // The nearest beat within its window, -1 if none.
        int Claim(double time)
        {
            int best = -1;
            double bestErr = double.PositiveInfinity;
            for (int i = 0; i < _beats.Count; i++)
            {
                double err = Math.Abs(time - _beats[i].Time);
                if (err <= _good[i] && err < bestErr) { best = i; bestErr = err; }
            }
            return best;
        }
    }

    public static class MeterJudge
    {
        /// Replay one player's presses (arrows, "b" = clear, "a" = submit) over [windowStart, windowEnd]
        /// (audio seconds) on the hymn's pattern. Presses are taken in time order.
        public static MeterResult Judge(IReadOnlyList<MeterBeat> beats, int pattern, double windowStart, double windowEnd,
                                        IEnumerable<MeterPress> presses)
        {
            var answer = new MeterAnswer(beats, pattern, windowStart, windowEnd);
            foreach (var p in presses.OrderBy(p => p.Time))
            {
                if (p.Action == InputMap.A) answer.Submit(p.Time);
                else if (p.Action == InputMap.B) answer.Clear();
                else answer.Press(p.Action, p.Time);
            }
            return answer.Result();
        }
    }

    /// Orders players best first: correct, then answered before the reveal, then the earlier Submit
    /// time, then higher accuracy. Use with OrderBy / List.Sort; wrong players tie with each other.
    public sealed class MeterRanking : IComparer<MeterResult>
    {
        public int Compare(MeterResult a, MeterResult b)
        {
            if (a.Correct != b.Correct) return a.Correct ? -1 : 1;
            if (!a.Correct) return 0;
            if (a.PostReveal != b.PostReveal) return a.PostReveal ? 1 : -1;
            int c = a.SubmitTime.CompareTo(b.SubmitTime);
            if (c != 0) return c;
            if (Math.Abs(a.Accuracy - b.Accuracy) < 1e-9) return 0;
            return b.Accuracy.CompareTo(a.Accuracy);
        }
    }
}
