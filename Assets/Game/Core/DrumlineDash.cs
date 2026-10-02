// Drumline Dash — the rules, with no rendering in them. Ported as-is from the
// web prototype's web/src/games/drumlineDash/ (chart.js + the gameplay half of
// index.js). 132 bpm, one button.
//
// The drum major plays a one-bar rhythm; you play it back in the next bar. It
// is a race: three CPU marchers advance a seeded amount per phrase, you advance
// by how well you answered. A press that claims no note inside a phrase is a
// fumble: it burns 500 points (x2 in the finale) and 0.18 of the bar's advance,
// so mashing scores worse than playing nothing.

using System;
using System.Collections.Generic;
using System.Linq;

namespace RhythmGame.Core
{
    public sealed class DrumPhrase
    {
        public int Index;
        public string Section;
        public int Bars;
        public double CallBeat, RespBeat, EndBeat;
        public int[][] Slots;
        public List<double> CallBeats, NoteBeats;
        public bool Finale;
    }

    public static class DrumChart
    {
        public const double Bpm = 132;
        public const double SlotBeats = 0.25;
        public const int BeatsPerBar = 4;
        public const int LeadBeats = 8;       // FEEL.leadInBars * 4
        public const int OutroBeats = 8;

        public static readonly int[][] Teach =
        {
            new[] { 0, 4, 8 }, new[] { 0, 4, 8, 12 }, new[] { 0, 2, 4, 8 }, new[] { 0, 4, 6, 12 },
        };
        public static readonly int[][] Play =
        {
            new[] { 0, 4, 6, 8, 12 }, new[] { 0, 2, 4, 8, 10 }, new[] { 0, 4, 8, 10, 12 }, new[] { 0, 2, 6, 8, 12 },
            new[] { 0, 4, 6, 10, 12 }, new[] { 0, 2, 4, 6, 8 }, new[] { 0, 4, 8, 12, 14 }, new[] { 0, 4, 6, 8, 10 },
        };
        public static readonly int[][] Escalate =
        {
            new[] { 0, 2, 6, 8, 12 }, new[] { 2, 4, 8, 10, 12 }, new[] { 0, 6, 8, 10, 12 }, new[] { 0, 2, 4, 10, 12 },
            new[] { 2, 6, 8, 12, 14 },
        };
        /// Two bars, the same bar twice: the length sells the ending.
        public static readonly int[][] FinaleBars = { new[] { 0, 4, 6, 12 }, new[] { 0, 4, 6, 12 } };

        /// Verdict -> the fraction of a note's advance it is worth in the race.
        public static double AdvanceWeight(Verdict v) => v switch
        {
            Verdict.Perfect => 1, Verdict.Great => 0.82, Verdict.Good => 0.5, _ => 0,
        };

        /// Phrase k: the major calls for `bars` bars, then you answer. The first
        /// call lands inside the lead-in (beats -4..-1).
        public static List<DrumPhrase> Build()
        {
            var singles = new List<(int[] slots, string section)>();
            foreach (var s in Teach) singles.Add((s, "teach"));
            foreach (var s in Play) singles.Add((s, "play"));
            foreach (var s in Escalate) singles.Add((s, "escalate"));

            var phrases = new List<DrumPhrase>();
            void Push(int index, string section, int[][] slots, double callBeat, bool finale)
            {
                int bars = slots.Length;
                double resp = callBeat + bars * BeatsPerBar;
                List<double> Spread(double b0)
                {
                    var o = new List<double>();
                    for (int b = 0; b < bars; b++) foreach (var s in slots[b]) o.Add(b0 + b * BeatsPerBar + s * SlotBeats);
                    return o;
                }
                phrases.Add(new DrumPhrase
                {
                    Index = index, Section = section, Bars = bars, CallBeat = callBeat, RespBeat = resp,
                    EndBeat = resp + bars * BeatsPerBar, Slots = slots,
                    CallBeats = Spread(callBeat), NoteBeats = Spread(resp), Finale = finale,
                });
            }
            for (int k = 0; k < singles.Count; k++) Push(k, singles[k].section, new[] { singles[k].slots }, 8 * k - 4, false);
            Push(singles.Count, "finale", FinaleBars, 8 * singles.Count - 4, true);
            return phrases;
        }

        public static double EndBeat(List<DrumPhrase> p) => p[p.Count - 1].EndBeat + OutroBeats;
        public static int DurationBars(List<DrumPhrase> p) => (int)Math.Ceiling(EndBeat(p) / BeatsPerBar);

        public static double MaxPoints(List<DrumPhrase> phrases, double perNote = 1000)
        {
            double t = 0;
            foreach (var p in phrases) t += p.NoteBeats.Count * perNote * (p.Finale ? 2 : 1);
            return t;
        }
    }

    public sealed class Racer
    {
        public int Index;
        public string Name;
        public bool IsCpu;
        public double Skill, StumbleChance;
        public double Progress;
        public double XTarget;
        public int Place;
        public bool Stumbled;     // last phrase
        public double LastAdvance;
        public int PlayerId = -1; // party lineup slot; -1 for a house drummer
    }

    public enum DrumMode { Lead, Call, Resp }

    public sealed class DrumlineRound
    {
        public const double MashPoints = 500;
        public const double MashAdvance = 0.18;

        /// The house field: beatable, but only by playing well.
        public static readonly (string name, double skill, double stumble)[] HouseCpus =
        {
            ("PICCOLO", 0.70, 0.16), ("TENOR", 0.80, 0.12), ("CRASH", 0.90, 0.08),
        };

        public readonly BeatMap Map = new BeatMap(DrumChart.Bpm);
        public readonly NoteJudge Judge;
        public readonly List<DrumPhrase> Phrases = DrumChart.Build();
        public readonly List<Racer> Racers = new List<Racer>();
        public Racer You => Racers[0];
        public readonly double MaxPoints;

        public int PhraseIdx { get; private set; }
        public DrumPhrase Phrase => PhraseIdx < Phrases.Count ? Phrases[PhraseIdx] : null;
        public DrumMode Mode { get; private set; } = DrumMode.Lead;
        public bool Finished { get; private set; }

        public int Perfect, Great, Good, Miss, Combo, MaxCombo, Fumbles;
        public double Points;
        public readonly List<double> Errors = new List<double>();

        double _barWeight;
        int _barNotes, _barExtras;
        int _armedIdx = -1;
        readonly Rng _rng;

        public event Action<DrumPhrase> CallStarted, RespStarted;
        /// (phrase, your quality 0..1) after everyone has moved.
        public event Action<DrumPhrase, double> PhraseClosed;
        public event Action<Note, Verdict, double> Judged;
        public event Action Fumbled;

        /// `lineup` is the party's players (null in free play). The human marches
        /// in lane one; the party's CPU rivals take the next lanes at a pace set
        /// by their skill; a party races only its own lineup, so spare lanes stay
        /// empty. With no rivals seated, the house field races.
        public DrumlineRound(uint seed = 0xd12b, double offsetMs = 0, IList<PartyPlayer> lineup = null)
        {
            _rng = new Rng(seed);
            MaxPoints = DrumChart.MaxPoints(Phrases);
            var hero = lineup?.FirstOrDefault(p => !p.IsCpu);
            Racers.Add(new Racer { Index = 0, Name = "YOU", PlayerId = hero?.Id ?? -1 });
            var rivals = lineup?.Where(p => p.IsCpu).Take(HouseCpus.Length).ToList() ?? new List<PartyPlayer>();
            for (int k = 0; k < HouseCpus.Length; k++)
            {
                if (k < rivals.Count)
                {
                    var p = rivals[k];
                    Racers.Add(new Racer { Index = k + 1, Name = p.Name, IsCpu = true, Skill = 0.58 + 0.37 * p.CpuSkill, StumbleChance = 0.28 - 0.23 * p.CpuSkill, PlayerId = p.Id });
                }
                else if (rivals.Count == 0)
                    Racers.Add(new Racer { Index = k + 1, Name = HouseCpus[k].name, IsCpu = true, Skill = HouseCpus[k].skill, StumbleChance = HouseCpus[k].stumble });
            }
            for (int i = 0; i < Racers.Count; i++) Racers[i].Place = i + 1;
            Judge = new NoteJudge(offsetMs);
            Judge.Judged += OnJudged;
        }

        public void Start(double audioTime) => Map.Start(audioTime, -DrumChart.LeadBeats);

        public double BeatAt(double t) => Map.BeatAt(t);

        /// Returns true if the press claimed a note. An unclaimed press inside a
        /// phrase is a fumble (costs points and advance); in the lead-in it is free.
        public bool Press(double time)
        {
            if (Finished) return false;
            if (Judge.Press("a", time) != null) return true;
            var p = Phrase;
            if (p == null || Mode == DrumMode.Lead) return false;
            double mul = p.Finale ? 2 : 1;
            Fumbles++;
            _barExtras++;
            Points = Math.Max(0, Points - MashPoints * mul);
            Combo = 0;
            Fumbled?.Invoke();
            return false;
        }

        public void Update(double now)
        {
            double beat = Map.BeatAt(now);
            // A phrase that ends this frame and the next that starts this frame
            // land on the same frame: at 132 bpm they are the same instant.
            int guard = 0;
            while (guard++ < 4)
            {
                var p = Phrase;
                if (p == null) { if (!Finished) Finish(); break; }
                // Armed a full bar early: presses are routed before Update, so
                // loading on the response downbeat would drop a slightly early hit.
                if (_armedIdx != p.Index && beat >= p.CallBeat - 0.02) Arm(p);
                var next = beat < p.CallBeat ? DrumMode.Lead : beat < p.RespBeat ? DrumMode.Call : DrumMode.Resp;
                if (next != Mode)
                {
                    Mode = next;
                    if (next == DrumMode.Call) CallStarted?.Invoke(p);
                    if (next == DrumMode.Resp) RespStarted?.Invoke(p);
                }
                if (beat < p.EndBeat) break;
                Close(p);
                PhraseIdx++;
                Mode = DrumMode.Lead;
            }
            Judge.Update(now);
        }

        void Arm(DrumPhrase p)
        {
            _armedIdx = p.Index;
            Judge.Load(p.NoteBeats.Select(b => new Note { Time = Map.TimeAt(b), Action = "a", Data = p }));
            _barWeight = 0;
            _barNotes = p.NoteBeats.Count;
            _barExtras = 0;
        }

        void OnJudged(Note note, Verdict v, double errMs)
        {
            var p = (DrumPhrase)note.Data;
            double mul = p != null && p.Finale ? 2 : 1;
            switch (v)
            {
                case Verdict.Perfect: Perfect++; break;
                case Verdict.Great: Great++; break;
                case Verdict.Good: Good++; break;
                default: Miss++; break;
            }
            Points += Scoring.PointsFor(v) * mul;
            _barWeight += DrumChart.AdvanceWeight(v);
            if (v != Verdict.Miss)
            {
                Combo++;
                MaxCombo = Math.Max(MaxCombo, Combo);
                Errors.Add(errMs);
            }
            else Combo = 0;
            Judged?.Invoke(note, v, errMs);
        }

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// The bar is over: everybody moves, and you can see who moved further.
        void Close(DrumPhrase p)
        {
            double mul = p.Finale ? 2 : 1;
            double q = Clamp01((_barNotes > 0 ? _barWeight / _barNotes : 0) - MashAdvance * _barExtras);
            You.LastAdvance = (0.30 + 0.70 * q) * mul;
            You.Progress += You.LastAdvance;
            for (int i = 1; i < Racers.Count; i++)
            {
                var r = Racers[i];
                double a = r.Skill + (_rng.Next() - 0.5) * 0.18;
                r.Stumbled = _rng.Next() < r.StumbleChance;
                if (r.Stumbled) a = 0.22;
                r.LastAdvance = a * mul;
                r.Progress += r.LastAdvance;
            }
            Restand();
            PhraseClosed?.Invoke(p, q);
        }

        /// Relative X + standings. tanh keeps the whole field on screen forever.
        void Restand()
        {
            double mean = Racers.Average(r => r.Progress);
            foreach (var r in Racers) r.XTarget = Math.Tanh((r.Progress - mean) * 0.55) * 5.1;
            int k = 1;
            foreach (var r in Racers.OrderByDescending(r => r.Progress)) r.Place = k++;   // stable, like JS sort
        }

        void Finish()
        {
            Finished = true;
            Restand();
        }

        /// Hit quality 0..1, verdict-weighted: what `accuracy` means in every game.
        public double HitQuality
        {
            get
            {
                int n = Perfect + Great + Good + Miss;
                if (n == 0) return 0;
                return (Perfect * 1000.0 + Great * 600 + Good * 250) / (n * 1000.0);
            }
        }

        public double Score01 => Clamp01(Points / MaxPoints);

        public Outcome Result()
        {
            if (!Finished) return null;
            var stats = new JudgeStats { Perfect = Perfect, Great = Great, Good = Good, Miss = Miss, Combo = Combo, MaxCombo = MaxCombo };
            double bias = Errors.Count > 0 ? Errors.Average() : 0;
            double hit = HitQuality;
            // Field places are 0-based; the party uses them as the round's order.
            var field = Racers.Select(r => new FieldRow { Id = r.PlayerId, Place = r.Place - 1 }).ToList();
            var o = Outcome.Build(Score01 * 1000, hit, Scoring.RankFor(hit, Miss), stats, bias, field);
            int place = You.Place;
            o.Extra("PLACE", place + new[] { "st", "nd", "rd", "th" }[place - 1])
             .Extra("FUMBLES", Fumbles.ToString());
            foreach (var r in Racers.OrderBy(r => r.Place)) o.Extra("#" + r.Place, r.Name);
            return o;
        }
    }
}
