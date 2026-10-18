// The eight cast members, ported as-is from the web prototype's shell/castData.js (CAST).
// Pure data: the roster, the party hub and the results read the same table, so a
// character's name and colour cannot drift between screens. Colours are 0xRRGGBB.

using System.Collections.Generic;

namespace Bonanza.Core
{
    public sealed class CastDef
    {
        public readonly string Id, Name, Trait, Shape, Crest;
        public readonly int Color, Accent;
        public CastDef(string id, string name, int color, int accent, string trait, string shape, string crest)
        { Id = id; Name = name; Color = color; Accent = accent; Trait = trait; Shape = shape; Crest = crest; }
    }

    public static class Cast
    {
        /// Web order: the grid reads left to right, top to bottom in this order.
        public static readonly IReadOnlyList<CastDef> All = new[]
        {
            new CastDef("bopp", "BOPP", 0xffd93d, 0xff9f45, "All rhythm, no brakes.", "round", "antenna"),
            new CastDef("zizz", "ZIZZ", 0x4dd6ff, 0x7aa6ff, "Runs on static and spite.", "spike", "bolt"),
            new CastDef("kwark", "KWARK", 0xff5d73, 0xff9f45, "Beak first, ask later.", "beak", "plume"),
            new CastDef("tuff", "TUFF", 0x9ee87a, 0x39d4b4, "Built like a downbeat.", "block", "horns"),
            new CastDef("mimo", "MIMO", 0xc08cff, 0xff7ad9, "Two beats ahead, always.", "tall", "cap"),
            new CastDef("nibb", "NIBB", 0xff9f45, 0xffd93d, "Small. Loud. Everywhere.", "tiny", "antenna"),
            new CastDef("glub", "GLUB", 0x39d4b4, 0x4dd6ff, "Wobbles exactly on time.", "blob", "fin"),
            new CastDef("fizz", "FIZZ", 0xff7ad9, 0xc08cff, "Sparkles on the offbeat.", "star", "plume"),
        };

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Id == id) return i;
            return -1;
        }

        /// web charById: an unknown id falls back to the first character.
        public static CastDef ById(string id) { int i = IndexOf(id); return All[i < 0 ? 0 : i]; }
    }
}
