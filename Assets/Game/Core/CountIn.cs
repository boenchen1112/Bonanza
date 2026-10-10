// The count-in, ported from web/src/core/round.js countIn(). What is shared is
// the DRIVING (which word this beat says); the look stays each game's own.

namespace Bonanza.Core
{
    public static class CountIn
    {
        /// What lead-in beat `b` (negative through the lead-in) says, or null.
        ///   beats  how many beats of lead-in the transport runs
        ///   from   highest number spoken ("4 3 2 1")
        ///   go     what the last beat says instead of "1" (null: just numbers)
        ///   goOnDownbeat  numbers run to 1 and `go` lands on beat 0 instead
        public static string TextFor(int b, int beats = 8, int from = 4, string go = null, bool goOnDownbeat = false)
        {
            if (b == 0 && go != null && goOnDownbeat) return go;
            if (b >= 0 || b < -beats) return null;
            int n = -b;
            int shift = go != null && !goOnDownbeat ? 1 : 0;
            if (n == 1 && shift == 1) return go;
            if (n - shift >= 1 && n <= from + shift) return (n - shift).ToString();
            return null;
        }

        /// The count SFX's accent index for beat `b`: ((b % 4) + 4) % 4, never b % 4.
        public static int Accent(int b) => ((b % 4) + 4) % 4;
    }
}
