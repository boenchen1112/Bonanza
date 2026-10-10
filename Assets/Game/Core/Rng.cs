// Deterministic PRNG (mulberry32), bit-exact with web/src/core/util.js makeRng.
// Never System.Random or UnityEngine.Random in anything that affects gameplay:
// autoplay replays a run and the numbers must come out the same.

namespace Bonanza.Core
{
    public sealed class Rng
    {
        uint _a;

        public Rng(uint seed = 0x9e3779b9) { _a = seed; }

        /// 0 <= x < 1
        public double Next()
        {
            unchecked
            {
                _a += 0x6d2b79f5;
                uint t = _a;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        public double Range(double lo, double hi) => lo + Next() * (hi - lo);
        public int Int(int lo, int hi) => (int)System.Math.Floor(Range(lo, hi + 1));
        public T Pick<T>(System.Collections.Generic.IReadOnlyList<T> arr) => arr[(int)System.Math.Floor(Next() * arr.Count)];
        public int Sign() => Next() < 0.5 ? -1 : 1;
    }
}
