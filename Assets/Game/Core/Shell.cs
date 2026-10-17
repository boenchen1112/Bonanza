// Shell routing and rules, ported from the web prototype's shell/nav.js
// (titleMenuRoute, rosterExitRoute, exitRoute, partyConfirmRoute), shell/roster.js
// (the lineup rule) and shell/freeplay.js (the rail clamps at its ends).
// Pure: state in, a Route out. GameHost maps a Route's view onto its screens.

using System;
using System.Collections.Generic;

namespace Bonanza.Core
{
    /// Where the shell goes next: the web's `{view, opts}`. Unused opts are null.
    public readonly struct Route : IEquatable<Route>
    {
        public readonly string View;
        /// roster: "party" | "free".
        public readonly string Mode;
        /// freeplay: the game to focus. play: the game to start.
        public readonly string Game;
        /// play: "party" when the round belongs to a party.
        public readonly string From;

        public Route(string view, string mode = null, string game = null, string from = null)
        { View = view; Mode = mode; Game = game; From = from; }

        public bool Equals(Route o) => View == o.View && Mode == o.Mode && Game == o.Game && From == o.From;
        public override bool Equals(object obj) => obj is Route r && Equals(r);
        public override int GetHashCode() => (View, Mode, Game, From).GetHashCode();
        public override string ToString() =>
            $"{View}{(Mode != null ? " mode=" + Mode : "")}{(Game != null ? " game=" + Game : "")}{(From != null ? " from=" + From : "")}";
    }

    public static class ShellNav
    {
        public const string Title = "title", Roster = "roster", Options = "options", FreePlay = "freeplay",
                            PartyHub = "party", Play = "play";

        /// Title menu confirm -> the next view. Items: "party", "free", "options".
        public static Route TitleMenuRoute(string id)
        {
            if (id == "party") return new Route(Roster, mode: "party");
            if (id == "free") return new Route(Roster, mode: "free");
            return new Route(Options);
        }

        /// A locked-in lineup -> the next view.
        public static Route RosterExitRoute(string mode) =>
            mode == "party" ? new Route(PartyHub) : new Route(FreePlay);

        /// Where a player leaving a round lands. Mid-party always wins: the party
        /// hub is the one consistent "next up" screen. Free Play goes back to the
        /// list focused on the game just played; anything else goes to the title.
        public static Route ExitRoute(bool inParty, string from, string gameId)
        {
            if (inParty) return new Route(PartyHub);
            if (from == "freeplay") return new Route(FreePlay, game: gameId);
            return new Route(Title);
        }

        /// The party hub's confirm: start the next round, or wrap up to the title.
        public static Route PartyConfirmRoute(bool done, string gameId) =>
            done ? new Route(Title) : new Route(Play, game: gameId, from: "party");

        /// One step along the Free Play rail. Clamped, not wrapped; `atEnd` is true
        /// when the press went nowhere (the web nudges and ticks instead).
        public static int CarouselStep(int index, int dir, int count, out bool atEnd)
        {
            int next = Math.Max(0, Math.Min(count - 1, index + Math.Sign(dir)));
            atEnd = next == index;
            return next;
        }
    }

    /// The party roster rule (web roster.js): seat 0 is always the one human, and
    /// at least one of the other three seats must hold a rival.
    public static class RosterRules
    {
        public const int Seats = 4;
        public const string NeedRival = "SEAT AT LEAST ONE RIVAL";
        public const string HumanSeat = "P1 IS YOU - SET THE RIVALS";

        /// Seat 0 is the human and cannot be changed.
        public static bool CanChange(int seat) => seat > 0 && seat < Seats;

        /// Rivals seated in slots 1..3. `slotTier[i]` is an index into Party.CpuTiers,
        /// or Party.CpuTiers.Length for OFF; slot 0 is ignored.
        public static int Rivals(IReadOnlyList<int> slotTier)
        {
            int n = 0;
            for (int i = 1; i < Seats && i < slotTier.Count; i++)
                if (slotTier[i] >= 0 && slotTier[i] < Party.CpuTiers.Length) n++;
            return n;
        }

        /// null if the lineup may start; otherwise the line to flash (and the miss sfx to play).
        public static string Check(IReadOnlyList<int> slotTier) => Rivals(slotTier) < 1 ? NeedRival : null;
    }

    /// The one-time "running slow" hint (web main.js governFrame + title.js). Save value:
    /// absent, "pending" once Graphics Auto has sat at its lowest level and still missed the
    /// frame budget twice, "shown" once the title has displayed it.
    public static class PerfHint
    {
        public const string Pending = "pending", Shown = "shown";
        public const int StrikesNeeded = 2;

        /// One over-budget report from the governor at its floor. Returns the new saved value.
        public static string OnFloorOverBudget(ref int strikes, string saved)
        {
            strikes++;
            return strikes >= StrikesNeeded && string.IsNullOrEmpty(saved) ? Pending : saved;
        }

        public static bool ShouldShow(string saved) => saved == Pending;
    }
}
