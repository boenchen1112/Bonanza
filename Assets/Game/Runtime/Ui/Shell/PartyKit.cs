// Shared pieces of the two party screens (hub and winner): character faces cut from the roster's portraits, the
// rounded pill buttons, a small art tile per game, a crown and a trophy drawn from the kit's rounded shapes.

using Bonanza.Core;
using Bonanza.HudKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bonanza.Shell
{
    static class PartyKit
    {
        public static readonly Color Gold = UiKit.Hex(0xffd93d), Coral = UiKit.Hex(0xff7a6b), Plate = new Color(0.05f, 0.03f, 0.12f, 0.93f),
            Ink = UiKit.Hex(0x2a0f45);

        public static Color ColorOf(int c) => new Color(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f);
        public static float Damp(float a, float b, float rate, float dt) => Mathf.Lerp(a, b, 1 - Mathf.Exp(-rate * dt));
        public static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        public static Color PlaceColor(int place) => place switch
        {
            1 => UiKit.Hex(0xffd93d), 2 => UiKit.Hex(0xd5dcff), 3 => UiKit.Hex(0xe0995a), _ => UiKit.Hex(0x8a84bd),
        };
        public static string Ordinal(int p) => p == 1 ? "1ST" : p == 2 ? "2ND" : p == 3 ? "3RD" : p + "TH";

        public static Color GameColor(string id) => id switch
        {
            "swing-kings" => UiKit.Hex(0xffb73d),
            "drumline-dash" => UiKit.Hex(0x4dd6ff),
            "bounce-brigade" => UiKit.Hex(0xc08cff),
            "chomp-chorus" => UiKit.Hex(0x9ee87a),
            "finale-fever" => UiKit.Hex(0xff7ad9),
            _ => UiKit.Hex(0x9fb2ff),
        };

        // ------------------------------------------------------------------ faces

        public sealed class Face
        {
            public RectTransform Rt; public Image Fill, Ring; public RawImage Img; public TextMeshProUGUI Initial;
        }

        /// A round portrait: the baked bust cropped to a disc, a coloured ring, the character's initial until it bakes.
        public static Face MakeFace(RectTransform parent, string name, float size)
        {
            var f = new Face();
            f.Fill = UiKit.Box(parent, name, new Color(1, 1, 1, 0.1f), UiKit.Disc);
            f.Rt = f.Fill.rectTransform;
            UiKit.Place(f.Rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            var mask = f.Fill.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = true;
            var go = new GameObject("Img", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            go.transform.SetParent(f.Rt, false);
            f.Img = go.GetComponent<RawImage>(); f.Img.raycastTarget = false; f.Img.color = new Color(1, 1, 1, 0);
            UiKit.Stretch((RectTransform)go.transform);
            f.Img.uvRect = new Rect(0.15f, 0.2f, 0.7f, 0.7f);
            f.Initial = UiKit.Text(f.Rt, "Initial", TextStyle.Display, size * 0.42f, new Color(1, 1, 1, 0.6f));
            UiKit.Place(f.Initial.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(size, size));
            f.Ring = UiKit.Box(parent, name + "Ring", Color.white, UiKit.Ring);
            UiKit.Place(f.Ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            return f;
        }

        /// Move a face (and its ring) with its owner, then fill it from the cast.
        public static void SetFace(Face f, string castId, CastStage cast, Vector2 pos, float size, float alpha)
        {
            int ci = Cast.IndexOf(castId);
            if (ci < 0) ci = 0;
            var def = Cast.All[ci];
            var col = ColorOf(def.Color);
            f.Rt.anchoredPosition = pos; f.Rt.sizeDelta = new Vector2(size, size);
            var rr = f.Ring.rectTransform;
            rr.anchorMin = f.Rt.anchorMin; rr.anchorMax = f.Rt.anchorMax; rr.pivot = f.Rt.pivot;
            rr.anchoredPosition = pos; rr.sizeDelta = new Vector2(size, size);
            var tex = cast != null ? cast.Portrait(ci) : null;
            f.Img.texture = tex;
            f.Img.color = new Color(1, 1, 1, tex != null ? alpha : 0);
            f.Fill.color = A(Color.Lerp(col, Color.black, 0.45f), alpha);
            f.Ring.color = A(col, alpha);
            f.Initial.text = def.Name.Substring(0, 1);
            f.Initial.color = new Color(1, 1, 1, tex != null ? 0 : 0.7f * alpha);
            f.Initial.fontSize = UiKit.FontSizeForCap(size * 0.42f);
        }

        // ------------------------------------------------------------------ pills

        public sealed class Pill
        {
            public RectTransform Rt, Inner; public Image Fill, Ring, Glow, Shadow; public UiGradient Grad;
            public TextMeshProUGUI Label; public RectTransform Chip; public TextMeshProUGUI ChipText;
            public float Focus, Hover, Press;
        }

        public static Pill MakePill(RectTransform root, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, float cap, Vector2 chipSize)
        {
            var b = new Pill();
            b.Rt = UiKit.Rect(name, root);
            UiKit.Place(b.Rt, anchor, pivot, pos, size);
            b.Inner = UiKit.Rect("Inner", b.Rt); UiKit.Stretch(b.Inner);
            b.Shadow = UiKit.Box(b.Inner, "Shadow", new Color(0, 0, 0, 0.5f), UiKit.Pill, true, 24);
            UiKit.Place(b.Shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -6), size + new Vector2(6, 4));
            b.Glow = UiKit.Box(b.Inner, "Glow", new Color(1f, 0.8f, 0.3f, 0), UiKit.Soft);
            UiKit.Place(b.Glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size * new Vector2(1.7f, 2.6f));
            b.Fill = UiKit.Box(b.Inner, "Fill", Color.white, UiKit.Pill, true, 24);
            UiKit.Stretch(b.Fill.rectTransform);
            b.Grad = b.Fill.gameObject.AddComponent<UiGradient>();
            b.Ring = UiKit.Box(b.Inner, "Ring", new Color(1, 0.9f, 0.54f, 0.6f), UiKit.PillRing(5), true, 24);
            UiKit.Stretch(b.Ring.rectTransform);
            b.Label = UiKit.Text(b.Inner, "Label", TextStyle.Display, cap, Color.white);
            UiKit.Place(b.Label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-chipSize.x * 0.5f - 4, 1), new Vector2(size.x - chipSize.x - 24, size.y + 10));
            b.Chip = UiKit.Rect("Chip", b.Inner);
            UiKit.Place(b.Chip, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), chipSize);
            var cf = UiKit.Box(b.Chip, "Fill", new Color(0.1f, 0.04f, 0.2f, 0.85f), UiKit.Pill, true, 24);
            UiKit.Stretch(cf.rectTransform);
            b.ChipText = UiKit.Text(b.Chip, "T", TextStyle.Label, 11f, Gold, italic: false);
            b.ChipText.characterSpacing = 3f;
            UiKit.Place(b.ChipText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, chipSize);
            return b;
        }

        public static void DriveButton(Pill b, ShellInput input, int idx, bool primary, string label, string key, float dt, bool red, float enterT, bool chips)
        {
            b.Label.text = label;
            b.ChipText.text = key;
            b.Chip.gameObject.SetActive(chips && !string.IsNullOrEmpty(key));
            bool hov = input.Hover == idx;
            b.Hover = Damp(b.Hover, hov ? 1 : 0, red ? 40 : 18, dt);
            b.Press = Damp(b.Press, hov && input.Pressed ? 1 : 0, red ? 60 : 30, dt);
            b.Focus = Damp(b.Focus, primary ? 1 : 0, red ? 40 : 14, dt);
            float enter = red ? 1 : UiKit.BackOut(enterT / 0.45f);
            float pulse = primary && !red ? 1 + 0.018f * Mathf.Sin(Time.unscaledTime * 4f) : 1;
            b.Inner.localScale = Vector3.one * ((1 + 0.04f * b.Hover) * (1 - 0.06f * b.Press) * Mathf.Clamp01(enter) * pulse);
            Color top = Color.Lerp(new Color(0.11f, 0.06f, 0.27f, 0.96f), new Color(0.15f, 0.09f, 0.34f, 0.97f), b.Hover);
            Color bot = Color.Lerp(new Color(0.05f, 0.02f, 0.14f, 0.96f), new Color(0.07f, 0.03f, 0.18f, 0.97f), b.Hover);
            top = Color.Lerp(top, UiKit.Hex(0xffe27a), b.Focus); bot = Color.Lerp(bot, UiKit.Hex(0xff9a2e), b.Focus);
            float dark = 1 - 0.38f * b.Press;
            b.Grad.Set(new Color(top.r * dark, top.g * dark, top.b * dark, top.a), new Color(bot.r * dark, bot.g * dark, bot.b * dark, bot.a));
            Color rim = Color.Lerp(new Color(1, 0.95f, 0.78f, 0.7f), new Color(0.55f, 0.95f, 1f, 1f), b.Hover);
            b.Ring.color = Color.Lerp(rim, new Color(1, 0.98f, 0.82f, 1f), b.Focus);
            b.Label.color = Color.Lerp(Color.white, Ink, b.Focus);
            b.Glow.color = new Color(1f, 0.75f, 0.25f, 0.5f * b.Focus);
            b.Shadow.color = new Color(0, 0, 0, 0.5f * (1 - 0.6f * b.Press));
        }

        // ------------------------------------------------------------------ shapes

        public static Image Bar(RectTransform parent, string name, Color c, Vector2 pos, Vector2 size, float rot = 0, Sprite sprite = null)
        {
            var im = UiKit.Box(parent, name, c, sprite ?? UiKit.Pill, sprite == null, Mathf.Min(size.x, size.y) * 0.5f);
            UiKit.Place(im.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);
            im.rectTransform.localRotation = Quaternion.Euler(0, 0, rot);
            return im;
        }

        public static Image Dot(RectTransform parent, string name, Color c, Vector2 pos, float size, Sprite sprite = null)
        {
            var im = UiKit.Box(parent, name, c, sprite ?? UiKit.Disc);
            UiKit.Place(im.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            return im;
        }

        /// A little art tile for a game: its colour, with a mark drawn from the kit's shapes.
        public static RectTransform MakeGameTile(RectTransform parent, string name, string id, float size)
        {
            var tile = UiKit.Rect(name, parent);
            UiKit.Place(tile, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            var col = GameColor(id);
            var sh = UiKit.Box(tile, "Shadow", new Color(0, 0, 0, 0.45f), UiKit.Pill, true, size * 0.2f);
            UiKit.Place(sh.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -size * 0.04f), new Vector2(size + 4, size + 4));
            var fill = UiKit.Box(tile, "Fill", Color.white, UiKit.Pill, true, size * 0.2f);
            UiKit.Stretch(fill.rectTransform);
            fill.gameObject.AddComponent<UiGradient>().Set(Color.Lerp(col, Color.white, 0.3f), Color.Lerp(col, UiKit.Hex(0x2a0f45), 0.45f));
            var ring = UiKit.Box(tile, "Ring", new Color(1, 1, 1, 0.55f), UiKit.PillRing(5), true, size * 0.2f);
            UiKit.Stretch(ring.rectTransform);
            var mark = UiKit.Rect("Mark", tile);
            UiKit.Place(mark, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100, 100));
            mark.localScale = Vector3.one * (size / 130f);
            var ink = UiKit.Hex(0x2a0f45); var white = new Color(1, 1, 1, 0.95f);
            switch (id)
            {
                case "swing-kings":
                    Bar(mark, "Bat", white, new Vector2(-6, -4), new Vector2(86, 16), 42);
                    Bar(mark, "Knob", ink, new Vector2(-38, -36), new Vector2(14, 14), 42);
                    Dot(mark, "Ball", white, new Vector2(30, 30), 24);
                    Dot(mark, "Seam", ink, new Vector2(30, 30), 6);
                    break;
                case "drumline-dash":
                    Bar(mark, "Body", white, new Vector2(0, -14), new Vector2(76, 46));
                    Bar(mark, "Top", ink, new Vector2(0, 8), new Vector2(76, 16));
                    Bar(mark, "StickA", white, new Vector2(-14, 34), new Vector2(60, 8), 24);
                    Bar(mark, "StickB", white, new Vector2(14, 34), new Vector2(60, 8), -24);
                    break;
                case "bounce-brigade":
                    float[] h = { 34, 56, 78, 56, 34 };
                    for (int k = 0; k < 5; k++) Bar(mark, "Bar" + k, k == 2 ? white : new Color(1, 1, 1, 0.7f), new Vector2(-40 + k * 20, -22 + h[k] * 0.5f - 10), new Vector2(13, h[k]));
                    Dot(mark, "Ball", ink, new Vector2(0, 38), 18);
                    Dot(mark, "Shine", white, new Vector2(-3, 41), 6);
                    break;
                case "chomp-chorus":
                    for (int k = 0; k < 3; k++)
                    {
                        float s = k == 1 ? 46 : 36;
                        Dot(mark, "Head" + k, white, new Vector2(-38 + k * 38, k == 1 ? 6 : -4), s);
                        Dot(mark, "Mouth" + k, ink, new Vector2(-38 + k * 38, (k == 1 ? 6 : -4) - 6), k == 1 ? 18 : 14);
                    }
                    Dot(mark, "NoteA", white, new Vector2(-18, 44), 12); Bar(mark, "StemA", white, new Vector2(-12, 56), new Vector2(4, 22));
                    Dot(mark, "NoteB", white, new Vector2(26, 44), 12); Bar(mark, "StemB", white, new Vector2(32, 56), new Vector2(4, 22));
                    break;
                case "finale-fever":
                    Bar(mark, "StarA", white, Vector2.zero, new Vector2(54, 54), 0, null).sprite = null;
                    Bar(mark, "StarB", white, Vector2.zero, new Vector2(54, 54), 45, null).sprite = null;
                    Dot(mark, "Core", ink, Vector2.zero, 20);
                    Dot(mark, "SparkA", white, new Vector2(-40, 36), 10); Dot(mark, "SparkB", white, new Vector2(42, -34), 8);
                    break;
                default:
                    Dot(mark, "Dot", white, Vector2.zero, 60);
                    break;
            }
            return tile;
        }

        /// A crown: gold band, three points with jewels.
        public static RectTransform MakeCrown(RectTransform parent, string name)
        {
            var rt = UiKit.Rect(name, parent);
            UiKit.Place(rt, new Vector2(0.5f, 1), new Vector2(0.5f, 0), Vector2.zero, new Vector2(80, 56));
            var gold = UiKit.Hex(0xffd93d); var deep = UiKit.Hex(0xff9a2e);
            for (int k = -1; k <= 1; k++)
            {
                var sp = Bar(rt, "Spike" + k, Color.Lerp(gold, Color.white, 0.2f), new Vector2(k * 24, 22 + (k == 0 ? 6 : 0)), new Vector2(k == 0 ? 26 : 22, k == 0 ? 26 : 22), 45, null);
                sp.sprite = null; sp.type = Image.Type.Simple;
            }
            var band = Bar(rt, "Band", Color.white, new Vector2(0, 10), new Vector2(76, 22));
            band.gameObject.AddComponent<UiGradient>().Set(Color.Lerp(gold, Color.white, 0.35f), deep);
            var rim = UiKit.Box(band.rectTransform, "Rim", new Color(1, 1, 1, 0.5f), UiKit.PillRing(4), true, 11);
            UiKit.Stretch(rim.rectTransform);
            Dot(rt, "GemL", UiKit.Hex(0xff5d73), new Vector2(-24, 38), 9);
            Dot(rt, "GemC", UiKit.Hex(0x4dd6ff), new Vector2(0, 46), 11);
            Dot(rt, "GemR", UiKit.Hex(0x9ee87a), new Vector2(24, 38), 9);
            Dot(rt, "GemBand", UiKit.Hex(0xc08cff), new Vector2(0, 10), 10);
            return rt;
        }

        /// A trophy cup with handles and a base.
        public static RectTransform MakeTrophy(RectTransform parent, string name, float size)
        {
            var rt = UiKit.Rect(name, parent);
            UiKit.Place(rt, new Vector2(0.5f, 1), new Vector2(0.5f, 0), Vector2.zero, new Vector2(100, 110));
            rt.localScale = Vector3.one * (size / 110f);
            var gold = UiKit.Hex(0xffd93d); var deep = UiKit.Hex(0xe58a1f);
            var hl = UiKit.Box(rt, "HandleL", Color.Lerp(gold, deep, 0.4f), UiKit.PillRing(10), true, 22);
            UiKit.Place(hl.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-38, 62), new Vector2(40, 44));
            var hr = UiKit.Box(rt, "HandleR", Color.Lerp(gold, deep, 0.4f), UiKit.PillRing(10), true, 22);
            UiKit.Place(hr.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(38, 62), new Vector2(40, 44));
            var stem = Bar(rt, "Stem", Color.Lerp(gold, deep, 0.5f), new Vector2(0, 28), new Vector2(16, 24));
            var foot = Bar(rt, "Foot", Color.white, new Vector2(0, 12), new Vector2(54, 22));
            foot.gameObject.AddComponent<UiGradient>().Set(Color.Lerp(gold, deep, 0.5f), UiKit.Hex(0x8a4a12));
            var cup = Bar(rt, "Cup", Color.white, new Vector2(0, 66), new Vector2(62, 62));
            cup.gameObject.AddComponent<UiGradient>().Set(Color.Lerp(gold, Color.white, 0.4f), deep);
            var rim = Bar(rt, "Lip", Color.Lerp(gold, Color.white, 0.5f), new Vector2(0, 95), new Vector2(72, 14));
            Bar(rt, "Shine", new Color(1, 1, 1, 0.75f), new Vector2(-16, 66), new Vector2(7, 34));
            Dot(rt, "Star", UiKit.Hex(0xfff6d8), new Vector2(6, 62), 16);
            return rt;
        }
    }
}
