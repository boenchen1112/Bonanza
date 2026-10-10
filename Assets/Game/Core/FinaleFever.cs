// Finale Fever — the rules, with no rendering in them. Ported from the web
// prototype's web/src/games/finaleFever/ (chart.js + the gameplay half of index.js).
//
// A conductor-boss states a phrase, you answer it one bar later, while the
// tempo ramps 150 -> 187.5 bpm. The last four bars are a solo with no
// telegraph, then one finale note worth four. Hearts are scored per phrase and
// never eject: losing all three drops you into SURVIVE, which caps the rank at B.
//
// One deliberate difference from the web build (Docs/decisions.jsonl,
// "finale-tempo-ramp"): the web calls setBpm every frame, so beat<->time depends
// on the frame rate and note times are re-derived every frame. Here the same
// curve is integrated once into RampMap, so every note time is exact up front
// and audio can be scheduled ahead like the other games.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Bonanza.Core
{
    /// Beat <-> time for a tempo that is a smooth function of the beat.
    /// Lead-in (beat <= 0) runs at Bpm0; after that dt/dbeat = 60 / BpmAt(beat).
    public sealed class RampMap
    {
        const double Step = 1.0 / 16;     // table resolution in beats
        readonly Func<double, double> _bpmAt;
        readonly double _bpm0;
        readonly double[] _table;         // seconds from beat 0 at k * Step
        double _origin, _startBeat;

        public RampMap(Func<double, double> bpmAt, double bpm0, double lastBeat)
        {
            _bpmAt = bpmAt;
            _bpm0 = bpm0;
            int n = (int)Math.Ceiling(lastBeat / Step) + 1;
            _table = new double[n + 1];
            for (int k = 1; k <= n; k++) _table[k] = _table[k - 1] + Segment((k - 1) * Step, k * Step);
        }

        double Spb(double beat) => 60.0 / (beat <= 0 ? _bpm0 : _bpmAt(beat));

        /// Simpson's rule for seconds between beats a and b (0 <= a <= b, smooth integrand).
        double Segment(double a, double b) => (b - a) / 6 * (Spb(a) + 4 * Spb((a + b) / 2) + Spb(b));

        public void Start(double audioTime, double startBeat)
        {
            _origin = audioTime;
            _startBeat = startBeat;
        }

        public double BpmAt(double beat) => beat <= 0 ? _bpm0 : _bpmAt(beat);

        /// Seconds from beat 0 to `beat` (negative before it).
        double FromZero(double beat)
        {
            if (beat <= 0) return beat * 60.0 / _bpm0;
            int k = (int)Math.Floor(beat / Step);
            if (k >= _table.Length - 1)
            {
                k = _table.Length - 1;
                return _table[k] + (beat - k * Step) * Spb(k * Step);   // past the table: last tempo
            }
            return _table[k] + Segment(k * Step, beat);
        }

        public double TimeAt(double beat) => _origin + FromZero(beat) - FromZero(_startBeat);

        public double BeatAt(double time)
        {
            double s = time - _origin + FromZero(_startBeat);
            if (s <= 0) return s * _bpm0 / 60.0;
            // Bracket in the table, then Newton inside the segment.
            int lo = 0, hi = _table.Length - 1;
            if (s >= _table[hi]) return hi * Step + (s - _table[hi]) / Spb(hi * Step);
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (_table[mid] <= s) lo = mid; else hi = mid; }
            double b = lo * Step + (s - _table[lo]) / Spb(lo * Step);
            for (int i = 0; i < 4; i++) b -= (FromZero(b) - s) / Spb(b);
            return b;
        }
    }

    public sealed class FeverNote
    {
        public double Beat;
        public string Action;      // a | left | right
        public string Kind;        // tap | swing | chord | solo | finale
        public int Lane;           // -1 left, 0 centre, 1 right
        public int Telegraph;      // beats of orb flight (0 = none)
        public string Section;
        public int Weight;
        public string PatName;
    }

    public sealed class FeverCue
    {
        public double Beat;
        public string Type;        // call | pulse
        public string Kind;
        public bool Accent;
        public int Tone, Of;
    }

    public sealed class FeverPhrase
    {
        public int Idx0, Idx1;
        public double CallBeat0, AnsBeat0, EndBeat;
        public string Section, PatName;
        public bool Solo, Finale;
    }

    public static class FeverChart
    {
        public const double Bpm0 = 150;
        public const double Ramp = 1.25;
        public const int BeatsPerBar = 4;
        public const int LeadBeats = 8;
        public const int DuelPhrases = 23;
        public const int SoloBar = DuelPhrases * 2;
        public const int SoloBars = 4;
        public const int EndBar = SoloBar + SoloBars;
        public const double EndBeat = EndBar * BeatsPerBar;   // 200: the finale note

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// Eases in, then leans: never a step the player can point at.
        public static double BpmAt(double beat)
        {
            double s = Clamp01(beat / EndBeat);
            return Bpm0 * (1 + (Ramp - 1) * (0.25 * s + 0.75 * s * s));
        }

        public static int Telegraph(string section) => section switch
        {
            "teach" => 4, "play" => 3, "escalate" => 2, _ => 0,
        };

        public static string Label(string section) => section switch
        {
            "teach" => "WARM-UP", "play" => "DUEL", "escalate" => "FEVER", _ => "SOLO",
        };

        // (beat offset, kind) — "tap" unless noted.
        static (double b, string kind)[] T(params double[] b) => b.Select(x => (x, "tap")).ToArray();
        static readonly Dictionary<string, (double b, string kind)[]> P = new Dictionary<string, (double, string)[]>
        {
            ["swing"] = new[] { (0.0, "swing"), (2.0, "swing") },
            ["hook"] = T(0, 1, 1.5, 2, 3, 3.5),
            ["hookEnd"] = T(0, 1, 1.5, 2, 3),
            ["echoA"] = T(0, 1, 2, 2.5),
            ["echoB"] = T(0, 0.5, 1.5, 3),
            ["echoC"] = T(0.5, 1, 2, 2.5, 3),
            ["groove"] = T(0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5),
            ["groove2"] = T(0, 0.5, 1, 2, 2.5, 3),
            ["groove3"] = T(0, 0.5, 1.5, 2, 3, 3.5),
            ["chord"] = new[] { (0.0, "chord"), (1.5, "tap"), (2.0, "chord"), (3.0, "tap") },
            ["chordMix"] = new[] { (0.0, "tap"), (1.0, "chord"), (2.0, "tap"), (2.5, "tap"), (3.0, "chord") },
            ["rush"] = new[] { (0.0, "tap"), (0.5, "tap"), (1.0, "tap"), (2.0, "chord"), (3.0, "tap"), (3.5, "tap") },
            ["rush2"] = new[] { (0.0, "tap"), (0.5, "tap"), (1.5, "tap"), (2.0, "tap"), (3.0, "chord"), (3.5, "tap") },
        };

        /// Six to teach, fourteen to play, three to panic. `hook` recurs every four.
        public static readonly (string pat, string section)[] Run =
        {
            ("swing", "teach"), ("echoA", "teach"), ("groove", "teach"),
            ("hook", "teach"), ("echoB", "teach"), ("groove2", "teach"),
            ("hook", "play"), ("echoC", "play"), ("groove2", "play"), ("chord", "play"),
            ("echoA", "play"), ("hook", "play"), ("groove3", "play"), ("chordMix", "play"),
            ("echoB", "play"), ("hook", "play"), ("groove2", "play"), ("echoC", "play"),
            ("chordMix", "play"), ("hook", "play"),
            ("rush", "escalate"), ("chordMix", "escalate"), ("rush2", "escalate"),
        };

        public static (List<FeverNote> notes, List<FeverCue> cues, List<FeverPhrase> phrases) Build()
        {
            var notes = new List<FeverNote>();
            var cues = new List<FeverCue>();
            var phrases = new List<FeverPhrase>();

            for (int i = 0; i < Run.Length; i++)
            {
                var (patName, section) = Run[i];
                var pat = P[patName];
                int tele = Telegraph(section);
                double call0 = i * 2 * BeatsPerBar, ans0 = call0 + BeatsPerBar;
                int idx0 = notes.Count;
                double prevOff = -99;
                for (int k = 0; k < pat.Length; k++)
                {
                    var (off, kind) = pat[k];
                    // Full swings only on notes with room around them.
                    bool accent = off - prevOff >= 0.75;
                    prevOff = off;
                    cues.Add(new FeverCue { Beat = call0 + off, Type = "call", Kind = kind, Accent = accent, Tone = k, Of = pat.Length });
                    if (kind == "chord")
                    {
                        notes.Add(new FeverNote { Beat = ans0 + off, Action = "left", Kind = kind, Lane = -1, Telegraph = tele, Section = section, Weight = 1, PatName = patName });
                        notes.Add(new FeverNote { Beat = ans0 + off, Action = "right", Kind = kind, Lane = 1, Telegraph = tele, Section = section, Weight = 1, PatName = patName });
                    }
                    else notes.Add(new FeverNote { Beat = ans0 + off, Action = "a", Kind = kind, Lane = 0, Telegraph = tele, Section = section, Weight = 1, PatName = patName });
                }
                phrases.Add(new FeverPhrase { Idx0 = idx0, Idx1 = notes.Count, CallBeat0 = call0, AnsBeat0 = ans0, EndBeat = ans0 + BeatsPerBar, Section = section, PatName = patName });
            }

            // The solo: four bars of the hook, no call, no orbs. The last bar drops
            // its final eighth so the phrase resolves onto the finale note.
            for (int b = 0; b < SoloBars; b++)
            {
                double bar0 = (SoloBar + b) * BeatsPerBar;
                var pat = P[b == SoloBars - 1 ? "hookEnd" : "hook"];
                int idx0 = notes.Count;
                foreach (var (off, _) in pat)
                {
                    notes.Add(new FeverNote { Beat = bar0 + off, Action = "a", Kind = "solo", Lane = 0, Telegraph = 0, Section = "solo", Weight = 2, PatName = "hook" });
                    cues.Add(new FeverCue { Beat = bar0 + off, Type = "pulse", Kind = "solo", Accent = off == 0, Tone = 0, Of = 1 });
                }
                phrases.Add(new FeverPhrase { Idx0 = idx0, Idx1 = notes.Count, CallBeat0 = bar0, AnsBeat0 = bar0, EndBeat = bar0 + BeatsPerBar, Section = "solo", PatName = "hook", Solo = true });
            }

            int f = notes.Count;
            notes.Add(new FeverNote { Beat = EndBeat, Action = "a", Kind = "finale", Lane = 0, Telegraph = 0, Section = "solo", Weight = 4, PatName = "finale" });
            phrases.Add(new FeverPhrase { Idx0 = f, Idx1 = notes.Count, CallBeat0 = EndBeat, AnsBeat0 = EndBeat, EndBeat = EndBeat + 1, Section = "solo", PatName = "finale", Solo = true, Finale = true });

            // Stable, like JS sort.
            return (notes.OrderBy(n => n.Beat).ToList(), cues.OrderBy(c => c.Beat).ToList(), phrases);
        }

        public static string SectionAt(double beat)
        {
            if (beat < 0) return "teach";
            int bar = (int)Math.Floor(beat / BeatsPerBar);
            if (bar >= SoloBar) return "solo";
            return Run[Math.Min(bar / 2, Run.Length - 1)].section;
        }

        public static double ProgressAt(double beat) => Clamp01(beat / EndBeat);
    }

    public sealed class FeverRound
    {
        public const double OutroSeconds = 2.6;

        public readonly RampMap Map = new RampMap(FeverChart.BpmAt, FeverChart.Bpm0, FeverChart.EndBeat + 16);
        public readonly NoteJudge Judge;
        public readonly List<FeverNote> Notes;
        public readonly List<FeverCue> Cues;
        public readonly List<FeverPhrase> Phrases;
        /// The judge note for chart note i (same order).
        public readonly List<Note> Live = new List<Note>();

        public int Hearts { get; private set; } = 3;
        public bool Survived { get; private set; }
        public int Perfect, Great, Good, Miss, Combo, MaxCombo, Ghosts;
        public double Raw, Max;
        public bool FinaleJudged { get; private set; }
        public Verdict? FinaleVerdict { get; private set; }
        public bool Done { get; private set; }
        double _endAt;
        int _phraseCursor;

        public event Action<Note, FeverNote, Verdict, double> Judged;
        public event Action<int> HeartLost, HeartGained;   // hearts left
        public event Action Ghost;

        public FeverRound(double offsetMs = 0)
        {
            (Notes, Cues, Phrases) = FeverChart.Build();
            Judge = new NoteJudge(offsetMs);
            Judge.Judged += OnJudged;
        }

        public void Start(double audioTime)
        {
            Map.Start(audioTime, -FeverChart.LeadBeats);
            Live.Clear();
            foreach (var n in Notes) Live.Add(new Note { Time = Map.TimeAt(n.Beat), Action = n.Action, Data = n });
            Judge.Load(Live);
        }

        public double BeatAt(double t) => Map.BeatAt(t);
        public double Accuracy => Max > 0 ? Math.Min(1, Raw / Max) : 1;

        /// up = left, down = right; 'a' falls back to the nearest lane note, so
        /// nobody is locked out of a chord for using one thumb.
        public bool Press(string action, double time)
        {
            if (Done) return false;
            if (action == "up") action = "left";
            if (action == "down") action = "right";
            if (action != "a" && action != "left" && action != "right") return false;
            var r = Judge.Press(action, time);
            if (r == null && action == "a") r = Judge.Press("left", time) ?? Judge.Press("right", time);
            if (r != null) return true;
            Ghosts++;
            Ghost?.Invoke();
            return false;
        }

        public void Update(double now)
        {
            if (Done) return;
            double beat = Map.BeatAt(now);
            Judge.Update(now);
            ClosePhrases(beat);
            if (FinaleJudged) { if (now >= _endAt) Done = true; }
            else if (beat > FeverChart.EndBeat + 2.5)
            {
                // Never hang the round if the finale note was never committed.
                FinaleJudged = true;
                _endAt = now + OutroSeconds;
            }
            _now = now;
        }
        double _now;

        void OnJudged(Note note, Verdict v, double errMs)
        {
            var n = (FeverNote)note.Data;
            Raw += Scoring.PointsFor(v) * n.Weight;
            Max += 1000.0 * n.Weight;
            switch (v)
            {
                case Verdict.Perfect: Perfect++; break;
                case Verdict.Great: Great++; break;
                case Verdict.Good: Good++; break;
                default: Miss++; break;
            }
            if (v == Verdict.Miss) Combo = 0;
            else { Combo++; if (Combo > MaxCombo) MaxCombo = Combo; }
            if (n.Kind == "finale")
            {
                FinaleJudged = true;
                FinaleVerdict = v;
                _endAt = Math.Max(note.Time, _now) + OutroSeconds;
            }
            Judged?.Invoke(note, n, v, errMs);
        }

        /// Hearts per PHRASE, never per note: a lost phrase costs one, a clean
        /// one (every note perfect or great) buys one back.
        void ClosePhrases(double beat)
        {
            while (_phraseCursor < Phrases.Count && beat >= Phrases[_phraseCursor].EndBeat)
            {
                var p = Phrases[_phraseCursor++];
                if (p.Finale) continue;
                int miss = 0, top = 0, n = 0;
                for (int i = p.Idx0; i < p.Idx1; i++)
                {
                    var note = Live[i];
                    if (!note.Judged) continue;
                    n++;
                    if (note.Verdict == Verdict.Miss) miss++;
                    else if (note.Verdict == Verdict.Perfect || note.Verdict == Verdict.Great) top++;
                }
                if (n == 0) continue;
                if (miss >= (p.Solo ? 3 : 2)) LoseHeart();
                else if (miss == 0 && top == n) GainHeart();
            }
        }

        void LoseHeart()
        {
            if (Hearts <= 0) { HeartLost?.Invoke(0); return; }   // already surviving; nothing left to take
            Hearts--;
            if (Hearts == 0) Survived = true;
            HeartLost?.Invoke(Hearts);
        }

        void GainHeart()
        {
            if (Hearts >= 3) return;
            Hearts++;
            HeartGained?.Invoke(Hearts);
        }

        public string Tier => Survived ? "SURVIVOR" : Miss == 0 ? "MAESTRO" : "DUELLIST";

        public Outcome Result()
        {
            if (!Done) return null;
            double acc = Accuracy;
            string rank = Scoring.RankFor(acc, Miss);
            // Losing every heart is not an ejection, but it is not an S either.
            if (Survived && (rank == "S" || rank == "A")) rank = "B";
            var stats = new JudgeStats { Perfect = Perfect, Great = Great, Good = Good, Miss = Miss, Combo = Combo, MaxCombo = MaxCombo };
            var o = Outcome.Build(acc * 1000, acc, rank, stats, Judge.Bias, null);
            o.Extra("TIER", Tier)
             .Extra("HEARTS", Hearts)
             .Extra("FINALE", (FinaleVerdict ?? Verdict.Miss).ToString().ToUpperInvariant())
             .Extra("TOP BPM", (int)Math.Round(FeverChart.Bpm0 * FeverChart.Ramp, MidpointRounding.AwayFromZero))
             .Extra("GHOSTS", Ghosts);
            return o;
        }
    }
}
