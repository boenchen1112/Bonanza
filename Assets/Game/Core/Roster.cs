// The roster and character-pick state machine, ported from the web prototype's shell/roster.js.
// Pure: actions in, an outcome out (the host plays the sfx and draws). Two stages the player
// drives, so no control ever means two things:
//   Lineup  up/down pick a seat, left/right set a rival (CPU easy / normal / hard / OFF), A continues.
//   Chars   arrows move the 4-column grid, A locks in for the current human, B undoes the last pick.
// then Cpu (the rivals pick for themselves, one per CpuStep seconds, from the seeded rng) and Go
// (A starts). Free Play skips Lineup: one human, one pick. A character can be taken only once.
// The pick only names the player and tints them; it never feeds a CPU's skill or the party rolls.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Bonanza.Core
{
    public enum RosterStage { Lineup, Chars, Cpu, Go }

    /// What the host should do for the press: the sfx names are the web roster's.
    public enum RosterEvent
    {
        None,
        Tick,      // 'ui'
        Miss,      // 'miss': a refused press; Flash may carry the line to show
        Lock,      // 'perfect': a human locked in a character
        CpuLock,   // 'great': a CPU took one
        Unlock,    // 'uiBack': a pick undone
        Leave,     // 'uiBack': back out to the title
        Start,     // 'fanfare': the lineup is final; build the players and leave via RosterExitRoute
    }

    public readonly struct RosterOutcome
    {
        public readonly RosterEvent Event;
        public readonly string Flash;
        public readonly int Seat;
        public RosterOutcome(RosterEvent e, string flash = null, int seat = -1) { Event = e; Flash = flash; Seat = seat; }
    }

    public sealed class RosterState
    {
        public const int Cols = 4;
        public const double CpuStep = 0.34;
        /// The rng seed the web gives the roster scene (main.js: `opts.seed ?? seedOverride ?? 0x5eed`).
        public const uint DefaultSeed = 0x5eed;
        /// Seat value for the human; 0..2 are Party.CpuTiers, Off the empty seat.
        public const int You = -1;
        public static readonly int Off = Party.CpuTiers.Length;

        public readonly string Mode;   // "party" | "free"
        public RosterStage Stage;
        public int Cursor, SeatSel, Active;
        public double CpuT;
        /// Per seat: You, a CpuTiers index, or Off.
        public readonly int[] Seat;
        /// Per seat: the locked-in character id, or null.
        public readonly string[] Picks = new string[RosterRules.Seats];

        public RosterState(string mode)
        {
            Mode = mode == "party" ? "party" : "free";
            Stage = Mode == "party" ? RosterStage.Lineup : RosterStage.Chars;
            // party defaults: one human, two normal CPUs, the fourth seat off. Free Play is solo.
            Seat = Mode == "party" ? new[] { You, 1, 1, Off } : new[] { You, Off, Off, Off };
        }

        public bool IsCpu(int seat) => Seat[seat] >= 0 && Seat[seat] < Party.CpuTiers.Length;
        public bool IsHuman(int seat) => Seat[seat] == You;
        public bool Taken(int castIndex) => Picks.Contains(Cast.All[castIndex].Id);
        public int TakenBy(int castIndex) => Array.IndexOf(Picks, Cast.All[castIndex].Id);

        int NextHuman(int after)
        {
            for (int i = after + 1; i < RosterRules.Seats; i++) if (IsHuman(i) && Picks[i] == null) return i;
            return -1;
        }

        int LastConfirmedHuman()
        {
            for (int i = RosterRules.Seats - 1; i >= 0; i--) if (IsHuman(i) && Picks[i] != null) return i;
            return -1;
        }

        bool CpuUnpicked() { for (int i = 0; i < RosterRules.Seats; i++) if (IsCpu(i) && Picks[i] == null) return true; return false; }

        /// The seat tiers in the shape RosterRules wants (slot 0 ignored).
        public IReadOnlyList<int> SlotTier => Seat.Select(t => t < 0 ? 0 : t).ToArray();

        /// One press: "up" "down" "left" "right" "a" "b" ("pause" counts as b).
        public RosterOutcome Input(string action)
        {
            if (action == "pause") action = "b";
            switch (Stage)
            {
                case RosterStage.Lineup: return InputLineup(action);
                case RosterStage.Chars: return InputChars(action);
                case RosterStage.Go: return InputGo(action);
                default: return default;   // the rivals are choosing: no control
            }
        }

        RosterOutcome InputLineup(string action)
        {
            int n = RosterRules.Seats;
            switch (action)
            {
                case "up": SeatSel = (SeatSel + n - 1) % n; return new RosterOutcome(RosterEvent.Tick);
                case "down": SeatSel = (SeatSel + 1) % n; return new RosterOutcome(RosterEvent.Tick);
                case "left":
                case "right":
                    // One controller, one human: every minigame is played by P1.
                    if (!RosterRules.CanChange(SeatSel)) return new RosterOutcome(RosterEvent.Miss, RosterRules.HumanSeat);
                    int cycle = Party.CpuTiers.Length + 1;
                    Seat[SeatSel] = (Seat[SeatSel] + (action == "right" ? 1 : -1) + cycle) % cycle;
                    return new RosterOutcome(RosterEvent.Tick);
                case "a":
                    string problem = RosterRules.Check(SlotTier);
                    if (problem != null) return new RosterOutcome(RosterEvent.Miss, problem);
                    Stage = RosterStage.Chars;
                    Active = NextHuman(-1);
                    if (Active < 0) Stage = RosterStage.Cpu;
                    return new RosterOutcome(RosterEvent.Tick);
                case "b": return new RosterOutcome(RosterEvent.Leave);
            }
            return default;
        }

        void MoveCursor(int d)
        {
            int n = Cast.All.Count;
            Cursor = ((Cursor + d) % n + n) % n;
        }

        RosterOutcome InputChars(string action)
        {
            switch (action)
            {
                case "left": MoveCursor(-1); return new RosterOutcome(RosterEvent.Tick);
                case "right": MoveCursor(1); return new RosterOutcome(RosterEvent.Tick);
                case "up": MoveCursor(-Cols); return new RosterOutcome(RosterEvent.Tick);
                case "down": MoveCursor(Cols); return new RosterOutcome(RosterEvent.Tick);
                case "a":
                    if (Taken(Cursor)) return new RosterOutcome(RosterEvent.Miss, null, TakenBy(Cursor));   // shake + miss
                    int seat = Active;
                    Picks[seat] = Cast.All[Cursor].Id;
                    int nxt = NextHuman(seat);
                    if (nxt >= 0) Active = nxt;
                    else if (CpuUnpicked()) { Stage = RosterStage.Cpu; CpuT = 0.2; }
                    else Stage = RosterStage.Go;
                    return new RosterOutcome(RosterEvent.Lock, null, seat);
                case "b":
                    int undone = LastConfirmedHuman();
                    if (undone < 0) return new RosterOutcome(RosterEvent.Leave);
                    Unlock(undone);
                    return new RosterOutcome(RosterEvent.Unlock, null, undone);
            }
            return default;
        }

        RosterOutcome InputGo(string action)
        {
            if (action == "a") return new RosterOutcome(RosterEvent.Start);
            if (action == "b")
            {
                int undone = LastConfirmedHuman();
                if (undone < 0) return new RosterOutcome(RosterEvent.Leave);
                Unlock(undone);
                return new RosterOutcome(RosterEvent.Unlock, null, undone);
            }
            return default;
        }

        void Unlock(int seat)
        {
            Picks[seat] = null;
            Active = seat;
            Stage = RosterStage.Chars;
        }

        /// Advance the rivals' picking. One CPU takes a random free character per CpuStep seconds.
        public RosterOutcome Update(double dt, Rng rng)
        {
            if (Stage != RosterStage.Cpu) return default;
            CpuT += dt;
            int next = -1;
            for (int i = 0; i < RosterRules.Seats; i++) if (IsCpu(i) && Picks[i] == null) { next = i; break; }
            if (next < 0) { Stage = RosterStage.Go; return default; }
            if (CpuT <= CpuStep) return default;
            CpuT = 0;
            Picks[next] = CpuPick(Picks, rng).Id;
            return new RosterOutcome(RosterEvent.CpuLock, null, next);
        }

        /// A uniform pick among the characters nobody holds (the first if, somehow, none is free).
        public static CastDef CpuPick(IReadOnlyCollection<string> taken, Rng rng)
        {
            var free = Cast.All.Where(c => !taken.Contains(c.Id)).ToList();
            return free.Count == 0 ? Cast.All[0] : free[(int)Math.Floor(rng.Next() * free.Count)];
        }

        static readonly Regex DefaultName = new Regex(@"^P\d$");

        /// The human goes by their character (BOPP beside ZIZZ, not "P1" beside ZIZZ) unless they set a
        /// name of their own; `stored` is the saved name for the seat (null/"P1".."P4" mean none).
        public static string HumanName(CastDef def, string stored) =>
            !string.IsNullOrEmpty(stored) && !DefaultName.IsMatch(stored) ? stored : def.Name;

        /// web startRun: one PartyPlayer per seated seat. An unpicked seat falls back to the cast in seat order.
        public List<PartyPlayer> BuildPlayers(Func<int, string> storedName = null)
        {
            var list = new List<PartyPlayer>();
            for (int i = 0; i < RosterRules.Seats; i++)
            {
                if (Seat[i] == Off) continue;
                var def = Cast.ById(Picks[i] ?? Cast.All[i % Cast.All.Count].Id);
                bool human = IsHuman(i);
                list.Add(new PartyPlayer
                {
                    Name = human ? HumanName(def, storedName?.Invoke(i)) : def.Name,
                    Char = def.Id,
                    IsCpu = !human,
                    CpuSkill = human ? 0 : Party.CpuTiers[Seat[i]].skill,
                    Palette = def.Color,
                    Slot = i,
                });
            }
            return list;
        }

        public Route ExitRoute => ShellNav.RosterExitRoute(Mode);

        /// The whole flow with no player: the human takes the cursor's character (the first), the
        /// rivals follow. Used by the autoplay harness and `?scene=party`.
        public static RosterState Auto(string mode, Rng rng)
        {
            var r = new RosterState(mode);
            if (r.Stage == RosterStage.Lineup) r.Input("a");
            r.Input("a");
            while (r.Stage == RosterStage.Cpu) r.Update(CpuStep + 0.01, rng);
            return r;
        }
    }
}
