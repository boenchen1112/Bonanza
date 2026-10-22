// Hymn Meter — the conducting patterns. The player conducts the hymn with the arrow
// keys, one key per beat, and the key is tied to where the beat really sits in its bar
// (joining on beat 2 of a 4-beat bar means pressing "left"), so the player has to find
// the downbeat by ear.
//
// Patterns are by conducted beats per bar, not the written signature (6/8 conducted in
// two uses the 2-pattern). Actions are the InputMap strings. The first key of each pattern
// (down, left, right) names the pattern, so the first key an answer starts with sets its
// length. The steps of the stage encode the weight of each beat: heavier = lower.
//
// No UnityEngine here: this assembly is pure C# so it stays test-pinned.

namespace Bonanza.Core
{
    public static class Meter
    {
        static readonly string[] Two = { "right", "up" };
        static readonly string[] Three = { "left", "right", "up" };
        static readonly string[] Four = { "down", "left", "right", "up" };

        /// The keys of one bar, in order, or null for an unsupported beat count.
        public static string[] Pattern(int beatsPerBar)
        {
            switch (beatsPerBar)
            {
                case 2: return Two;
                case 3: return Three;
                case 4: return Four;
                default: return null;
            }
        }

        /// The pattern a first key starts (down 4, left 3, right 2), 0 for any other action.
        public static int PatternStartedBy(string action)
        {
            if (action == Four[0]) return 4;
            if (action == Three[0]) return 3;
            if (action == Two[0]) return 2;
            return 0;
        }

        /// Step height of a 0-based position in a bar, 1 = lowest = heaviest: 4 beats 1 3 2 3,
        /// 3 beats 1 3 3, 2 beats 1 3. 0 if either is out of range.
        public static int StepHeight(int beatsPerBar, int positionInBar)
        {
            if (positionInBar < 0 || positionInBar >= beatsPerBar) return 0;
            switch (beatsPerBar)
            {
                case 2: return positionInBar == 0 ? 1 : 3;
                case 3: return positionInBar == 0 ? 1 : 3;
                case 4: return positionInBar == 0 ? 1 : positionInBar == 2 ? 2 : 3;
                default: return 0;
            }
        }

        /// The key for a 0-based position in a bar, or null if either is out of range.
        public static string KeyFor(int beatsPerBar, int positionInBar)
        {
            var p = Pattern(beatsPerBar);
            if (p == null || positionInBar < 0 || positionInBar >= p.Length) return null;
            return p[positionInBar];
        }
    }
}
