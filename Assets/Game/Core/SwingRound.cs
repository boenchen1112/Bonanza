// One round of Swing Kings, tap mode, with no rendering in it. This is the
// gameplay half of web/src/games/swingKings/index.js, ported as-is; the
// presentation (balls, batter, crowd, words) subscribes to the events.
//
// Flow: a pitch goes LIVE when its ball leaves the machine (LaunchBeat). A
// press is judged against the scored notes (NoteJudge); its power comes from
// how close it lands to the live pitch's contact time. A hit is DONE at once;
// a miss stays live until the ball sails past into the backstop.

using System;
using System.Collections.Generic;

namespace Bonanza.Core
{
    public struct ContactEvent
    {
        public Pitch Pitch;
        public Verdict Verdict;
        public double ErrMs;
        public double Power;
        public Tier Tier;
        public bool Foul;      // GOOD and late: swung behind it
        public bool IsDemo;
        public int Combo;
        public int HomerFeet;  // 0 unless a fair home run
    }

    public struct WhiffEvent
    {
        public Pitch Pitch;
        public bool Swung;         // WHIFF! (swung and missed) vs STRIKE! (never swung)
        public int Outs;           // 1..3; the third retires the side
        public bool SideRetired;
    }

    public sealed class SwingRecord
    {
        public double Beat;
        public Verdict Verdict;
        public double ErrMs;
        public double Power;
        public TierId Tier;
        public bool Finale;
    }

    public sealed class RoundResult
    {
        public int Score;
        public double Accuracy;
        public string Rank;
        public int Perfect, Great, Good, Miss, MaxCombo;
        public double BiasMs;
        public int Homers, Liners, Bunts, Fouls;
        public bool GrandSlam;
        public double AvgPower;
        public List<SwingRecord> Highlights;
    }

    public sealed class SwingRound
    {
        public readonly BeatMap Map = new BeatMap(SwingRules.Bpm);
        public readonly NoteJudge Judge;
        public readonly List<Pitch> Pitches = SwingRules.BuildSchedule();
        public readonly List<SwingRecord> Swings = new List<SwingRecord>();

        public double Points { get; private set; }
        public double MaxPoints { get; private set; }
        public int Outs { get; private set; }
        public bool Over { get; private set; }
        public bool FinaleDone { get; private set; }

        public int Homers, Liners, Bunts, Slams, Fouls;
        double _powerSum;
        int _powerN;

        // per-press scratch (set by Press before the judge fires Judged)
        double _pendingPower;
        bool _swinging;

        /// The ball left the machine.
        public event Action<Pitch> PitchLaunched;
        /// The ball sailed past the plate into the backstop.
        public event Action<Pitch> PitchPassed;
        public event Action<ContactEvent> Contact;
        public event Action<WhiffEvent> Whiff;
        /// A press that claimed no pitch: still a visible swing, costs nothing.
        public event Action<double> SwingAtNothing;
        /// Every judged scored pitch, hit or miss (for HUD score/combo/accuracy).
        public event Action<Verdict, double> Judged;

        public SwingRound(double offsetMs = 0)
        {
            Judge = new NoteJudge(offsetMs);
            Judge.Judged += OnJudged;
        }

        /// Start the transport: the lead-in's first beat lands at `audioTime`.
        public void Start(double audioTime)
        {
            Map.Start(audioTime, -SwingRules.LeadInBeats);
            var notes = new List<Note>();
            foreach (var p in Pitches)
            {
                if (!p.Scored) continue;
                var n = new Note { Time = Map.TimeAt(p.TargetBeat), Action = "a", Data = p };
                p.Note = n;
                notes.Add(n);
            }
            Judge.Load(notes);
        }

        public double BeatAt(double audioTime) => Map.BeatAt(audioTime);

        /// The pitch currently in flight, if any.
        public Pitch LivePitch()
        {
            foreach (var p in Pitches) if (p.Live && !p.Done) return p;
            return null;
        }

        /// Flight progress of a pitch at `beat`: 0 at the muzzle, 1 at the plate.
        public static double FlightU(Pitch p, double beat) => (beat - p.LaunchBeat) / p.Lead;

        /// A tap at audio time `time`: one press is one swing.
        public void Press(double time)
        {
            var live = LivePitch();
            _pendingPower = live != null ? SwingRules.PowerFromTiming(time, TargetTime(live)) : 0;

            _swinging = true;
            var r = Judge.Press("a", time);
            _swinging = false;

            if (r == null && !DemoHit(time)) SwingAtNothing?.Invoke(time);
        }

        double TargetTime(Pitch p) => p.Note != null ? p.Note.Time : Map.TimeAt(p.TargetBeat);

        /// Advance to audio time `now`. Call every frame, after input.
        public void Update(double now)
        {
            Judge.Update(now);
            double beat = Map.BeatAt(now);

            foreach (var p in Pitches)
            {
                if (p.Done) continue;
                if (beat < p.LaunchBeat - 1.15) break;   // schedule is launch-ordered

                double u = FlightU(p, beat);
                if (!p.Live)
                {
                    if (u < 0) continue;
                    p.Live = true;
                    PitchLaunched?.Invoke(p);
                }

                // The demo: the batter swings itself, on time, in front of the player.
                if (p.Kind == PitchKind.Demo && beat >= p.TargetBeat)
                {
                    DemoContact(p);
                    continue;
                }

                if (u >= SwingRules.SailPastU)
                {
                    p.Done = true;
                    PitchPassed?.Invoke(p);
                }
            }

            if (!Over && beat > SwingRules.EndBeat && Judge.Finished) Over = true;
        }

        bool DemoHit(double time)
        {
            var d = Pitches[0];
            if (d.Kind != PitchKind.Demo || d.Done || !d.Live) return false;
            if (Math.Abs(Map.BeatAt(time) - d.TargetBeat) > 0.45) return false;
            DemoContact(d);
            return true;
        }

        void DemoContact(Pitch d)
        {
            d.Done = true;
            Contact?.Invoke(new ContactEvent
            {
                Pitch = d, Verdict = Verdict.Perfect, Power = 1, Tier = Tier.Homer, IsDemo = true,
            });
        }

        void OnJudged(Note note, Verdict verdict, double errMs)
        {
            var p = (Pitch)note.Data;
            bool hit = verdict != Verdict.Miss;
            double power = hit ? _pendingPower : 0;
            var tier = SwingRules.TierFor(power, p.Finale);
            // GOOD is a bunt when early, a foul when late (swung behind it).
            bool foul = hit && verdict == Verdict.Good && errMs > 0;

            var s = SwingRules.ScoreFor(verdict, power, p.Finale);
            Points += s.got;
            MaxPoints += s.max;

            if (hit)
            {
                switch (tier.Id)
                {
                    case TierId.Bunt: Bunts++; break;
                    case TierId.Liner: Liners++; break;
                    case TierId.Homer: Homers++; break;
                    case TierId.Slam: Slams++; break;
                }
                if (foul) Fouls++;
                _powerSum += power;
                _powerN++;
            }
            Swings.Add(new SwingRecord
            {
                Beat = p.TargetBeat, Verdict = verdict, ErrMs = Math.Round(errMs * 10) / 10,
                Power = Math.Round(power * 100) / 100, Tier = tier.Id, Finale = p.Finale,
            });

            if (hit)
            {
                p.Done = true;
                int combo = Judge.Stats.Combo;
                Contact?.Invoke(new ContactEvent
                {
                    Pitch = p, Verdict = verdict, ErrMs = errMs, Power = power, Tier = tier, Foul = foul,
                    Combo = combo,
                    HomerFeet = tier.Id == TierId.Homer && !foul ? SwingRules.HomerFeet(power, verdict, combo) : 0,
                });
            }
            else
            {
                // Outs are a scoring tier, not an ejection: the third retires the
                // side, then the count resets and play goes on.
                Outs++;
                bool third = Outs >= 3;
                Whiff?.Invoke(new WhiffEvent { Pitch = p, Swung = _swinging, Outs = Outs, SideRetired = third });
                if (third) Outs = 0;
            }

            if (p.Finale) FinaleDone = true;
            Judged?.Invoke(verdict, errMs);
        }

        /// Seconds until the next pitch after `p` lands (Infinity for the last).
        public double GapAfter(Pitch p)
        {
            double next = double.PositiveInfinity;
            foreach (var q in Pitches)
                if (q.TargetBeat > p.TargetBeat && q.TargetBeat < next) next = q.TargetBeat;
            return (next - p.TargetBeat) * Map.Spb;
        }

        public RoundResult Result()
        {
            var st = Judge.Stats;
            double acc = Judge.Accuracy;
            var highlights = new List<SwingRecord>();
            foreach (var x in Swings)
                if (x.Tier == TierId.Slam || (x.Tier == TierId.Homer && x.Verdict == Verdict.Perfect)) highlights.Add(x);
            return new RoundResult
            {
                Score = (int)Math.Round(Points, MidpointRounding.AwayFromZero),
                Accuracy = acc,
                Rank = Scoring.RankFor(acc, st.Miss),
                Perfect = st.Perfect, Great = st.Great, Good = st.Good, Miss = st.Miss, MaxCombo = st.MaxCombo,
                BiasMs = Math.Round(Judge.Bias * 10) / 10,
                Homers = Homers + Slams, Liners = Liners, Bunts = Bunts, Fouls = Fouls,
                GrandSlam = Slams > 0,
                AvgPower = _powerN > 0 ? Math.Round(_powerSum / _powerN * 100) / 100 : 0,
                Highlights = highlights,
            };
        }
    }
}
