// Swing Kings — the rules, with no rendering in them. Ported as-is from the
// web prototype's web/src/games/swingKings/rules.js (+ powerFromTiming from
// its index.js).
//
// Two axes:
//   RELEASE TIMING -> accuracy (perfect / great / good / whiff)  [NoteJudge]
//   POWER          -> tier     (bunt / line drive / home run)    [here]
//
// Tap mode (the only mode for now) earns power from TIMING: PERFECT sends it,
// GREAT is a line drive, GOOD a bunt. The hold-length curve (PowerForHold) is
// kept for the conducting modes that come later.

using System;
using System.Collections.Generic;

namespace Bonanza.Core
{
    public enum TierId { Bunt, Liner, Homer, Slam }

    public sealed class Tier
    {
        public TierId Id;
        public string Label;
        public uint Color;     // 0xRRGGBB
        public float Dist;     // how far the ball actually goes, world units
        public float Lift;

        public static readonly Tier Bunt = new Tier { Id = TierId.Bunt, Label = "BUNT", Color = 0x5ce1ff, Dist = 5.5f, Lift = 0.55f };
        public static readonly Tier Liner = new Tier { Id = TierId.Liner, Label = "LINE DRIVE", Color = 0xffd93d, Dist = 22f, Lift = 0.34f };
        public static readonly Tier Homer = new Tier { Id = TierId.Homer, Label = "HOME RUN!", Color = 0xff9a3a, Dist = 52f, Lift = 0.72f };
        public static readonly Tier Slam = new Tier { Id = TierId.Slam, Label = "GRAND SLAM!", Color = 0xfff6d8, Dist = 120f, Lift = 0.85f };
    }

    public enum Section { Lead, Teach, Play, Escalate, Finale, Demo }
    public enum PitchKind { Normal, Demo, Fast, Slam }

    /// One ball: leaves the machine at LaunchBeat, crosses the plate at TargetBeat.
    public class Pitch
    {
        public int Id;
        public double TargetBeat;
        public double Lead;
        public double LaunchBeat => TargetBeat - Lead;
        public double Ideal;      // ideal windup, beats (conducting modes)
        public double Apex;       // flight apex height, world units
        public PitchKind Kind;
        public Section Section;
        public bool Scored;
        public bool Finale;

        // runtime
        public bool Live;
        public bool Done;
        public Note Note;
    }

    public static class SwingRules
    {
        public const double Bpm = 124;
        public const int BeatsPerBar = 4;
        public const int LeadInBars = 2;                       // core/feel.js FEEL.leadInBars
        public const int LeadInBeats = LeadInBars * BeatsPerBar;
        public const int ScoredBars = 32;                      // teach 8 + play 16 + escalate 8
        public const double FinaleBeat = 128;                  // the grand slam lands here
        public const double EndBeat = FinaleBeat + 12;         // three bars of curtain call
        /// A ball that sails this far past the plate (u) is gone into the backstop.
        public const double SailPastU = 1.28;

        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
        static double Smoothstep(double t) { t = Clamp01(t); return t * t * (3 - 2 * t); }

        /// Windup length (beats) -> power 0..1. DEAD swallows a tap's keydown/up
        /// gap; smoothstep keeps the tiers meaningful; over-holding sags so
        /// "hold forever" is never optimal.
        public static double PowerForHold(double holdBeats, double ideal = 2)
        {
            const double DEAD = 0.1;
            if (!(holdBeats > DEAD)) return 0;
            double t = Clamp01((holdBeats - DEAD) / Math.Max(0.25, ideal - DEAD));
            double overStart = ideal * 1.8;
            double over = holdBeats > overStart ? Clamp01((holdBeats - overStart) / (ideal * 2.2)) : 0;
            return Clamp01(Smoothstep(t) - over * 0.55);
        }

        /// Timing -> power, stepped by verdict so the three tiers MEAN the three
        /// timing grades. Within each band power still slides a little.
        public static double PowerFromTiming(double pressTime, double targetTime)
        {
            double err = Math.Abs(pressTime - targetTime) * 1000;
            if (err <= Windows.PerfectMs) return 1;
            if (err <= Windows.GreatMs) return 0.62 - 0.2 * ((err - Windows.PerfectMs) / (Windows.GreatMs - Windows.PerfectMs));
            if (err <= Windows.GoodMs) return 0.24 - 0.14 * ((err - Windows.GreatMs) / (Windows.GoodMs - Windows.GreatMs));
            return 0;
        }

        public static Tier TierFor(double power, bool finale = false)
        {
            if (power >= 0.72) return finale ? Tier.Slam : Tier.Homer;
            if (power >= 0.28) return Tier.Liner;
            return Tier.Bunt;
        }

        /// Power's contribution to score. A bunt still pays; it just never wins.
        public static double PowerMul(double p) => 0.42 + 0.58 * Clamp01(p);

        /// Points for one judged pitch, and the most it could have been worth.
        public static (double got, double max) ScoreFor(Verdict verdict, double power, bool finale)
        {
            double mul = finale ? 2 : 1;
            return (Scoring.PointsFor(verdict) * PowerMul(power) * mul, Scoring.PointsFor(Verdict.Perfect) * mul);
        }

        /// Home runs carry a distance so forty homers aren't forty identical
        /// badges. Quantised to 5ft.
        public static int HomerFeet(double power, Verdict verdict, int combo)
        {
            double ft = 365 + power * 70 + (verdict == Verdict.Perfect ? 15 : 0) + (combo % 7) * 3;
            return (int)(Math.Round(ft / 5, MidpointRounding.AwayFromZero) * 5);
        }

        /// The chart. Nothing is asked of the player that is not already a
        /// moving object at least `lead` beats early.
        public static List<Pitch> BuildSchedule()
        {
            var o = new List<Pitch>();
            int id = 0;
            void Add(double target, double lead, Section section, PitchKind kind = PitchKind.Normal,
                     bool scored = true, double? ideal = null, double? apex = null, bool finale = false)
            {
                o.Add(new Pitch
                {
                    Id = id++,
                    TargetBeat = target,
                    Lead = lead,
                    Ideal = ideal ?? Clamp(lead, 1.2, 2.2),
                    Apex = apex ?? (lead >= 6 ? 7.0 : lead >= 2 ? 2.75 : 1.75),
                    Kind = kind,
                    Section = section,
                    Scored = scored,
                    Finale = finale,
                });
            }

            // lead-in: one demonstration, swung by the batter, not scored
            Add(-5, 2, Section.Demo, PitchKind.Demo, scored: false);

            // teach (bars 0-7): one pitch a bar, always on the downbeat
            for (int b = 0; b < 8; b++) Add(b * 4, 2, Section.Teach);

            // play (bars 8-15): one a bar, alternating downbeat / beat 3,
            // so the player reads the BALL rather than counting bars
            for (int b = 8; b < 16; b++) Add(b * 4 + (b % 2 == 1 ? 2 : 0), 2, Section.Play);
            // play (bars 16-23): two a bar — density, not speed
            for (int b = 16; b < 24; b++)
            {
                Add(b * 4, 2, Section.Play);
                Add(b * 4 + 2, 2, Section.Play);
            }

            // escalate (bars 24-29): faster pitches, landing on the offbeats
            for (int b = 24; b < 30; b++)
            {
                Add(b * 4 + 1.5, 1.5, Section.Escalate, PitchKind.Fast);
                Add(b * 4 + 3.5, 1.5, Section.Escalate, PitchKind.Fast);
            }

            // the grand slam: launched on bar 30, arrives two bars later
            Add(FinaleBeat, 8, Section.Finale, PitchKind.Slam, ideal: 6, apex: 4.6, finale: true);
            return o;
        }

        /// Which section a beat is in — drives crowd energy and music intensity.
        public static Section SectionAt(double beat)
        {
            if (beat < 0) return Section.Lead;
            if (beat < 32) return Section.Teach;
            if (beat < 96) return Section.Play;
            if (beat < FinaleBeat) return Section.Escalate;
            return Section.Finale;
        }
    }
}
