// Hymn Meter — the conducting patterns. The player conducts the hymn with the arrow
// keys, one key per beat, and the key is tied to where the beat really sits in its bar
// (joining on beat 2 of a 4-beat bar means pressing "left"), so the player has to find
// the downbeat by ear.
//
// Patterns are by conducted beats per bar, not the written signature (6/8 conducted in
// two uses the 2-pattern). Actions are the InputMap strings.
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

        /// The key for a 0-based position in a bar, or null if either is out of range.
        public static string KeyFor(int beatsPerBar, int positionInBar)
        {
            var p = Pattern(beatsPerBar);
            if (p == null || positionInBar < 0 || positionInBar >= p.Length) return null;
            return p[positionInBar];
        }
    }
}
