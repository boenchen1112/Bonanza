// uGUI building blocks shared by the canvas HUD and the results card: the display
// font (Titan One, SIL OFL, baked to Resources/Look/HudFont), procedural sprites
// (rounded rects, discs, rings, glows: nothing is imported, so nothing can be
// stripped from a build), a vertical gradient, and rect helpers.
//
// Sizes are in reference pixels of a 1280x720 canvas. The web sizes its whole UI off
// u = 1.72 vmin (12.38 px at 720 high), so the numbers below are u multiples.
// Everything here is ASCII-safe: the atlas holds U+0020..U+007E only.

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bonanza.HudKit
{
    public enum TextStyle
    {
        /// Outlined display face with a hard drop shadow (web font.js 'display'): verdicts, banners, big numbers.
        Display,
        /// Same, a touch more tracking (web 'title').
        Title,
        /// HUD readouts (web 'hud'): tighter tracking.
        Hud,
        /// Plain face with a soft shadow, for small labels and the hint bar (web: system-ui 800).
        Label,
    }

    public static class UiKit
    {
        // ---- web palette (shell/theme.js PAL, ui/styles.js)
        public static readonly Color Ink = Hex(0x080714), Panel = Hex(0x1b1940), Panel2 = Hex(0x141230),
            Dim = Hex(0xa9b0e0), Yellow = Hex(0xffd93d), Cyan = Hex(0x4dd6ff), Coral = Hex(0xff5d73),
            Green = Hex(0x9ee87a), Orange = Hex(0xff9f45), Violet = Hex(0xc08cff),
            LabelInk = Hex(0xe2e8ff), AccBlue = Hex(0x9fb2ff), Cream = Hex(0xfff6d8), Gold = Hex(0xffe58a),
            CountGold = Hex(0xffe58a), BannerSub = Hex(0xcfd3ff);

        public static Color Hex(uint rgb) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        public static Color RankColor(string rank) => rank switch
        {
            "S" => Yellow, "A" => Cyan, "B" => Green, "C" => Orange, "D" => Coral, _ => Dim,
        };

        // The project is linear: the canvas blends in linear space, the web in gamma space, so a translucent
        // dark panel reads lighter here than there. These return the alpha that gives the web's look on the
        // mid-tone backgrounds the HUD sits over (a = 1 - (1-a)^2.2 for dark fills; light washes need less).
        public static float DarkAlpha(float a) => 1 - Mathf.Pow(1 - a, 2.2f);
        public static float LightAlpha(float a) => a * 0.5f;

        /// The web's u at the 720 reference height.
        public const float U = 12.384f;

        // ------------------------------------------------------------ easing (web core/util.js)

        public static float Clamp01(float t) => t < 0 ? 0 : t > 1 ? 1 : t;
        public static float BackOut(float t, float s = 1.70158f) { t = Clamp01(t) - 1; return t * t * ((s + 1) * t + s) + 1; }
        public static float EaseOutCubic(float t) { t = Clamp01(t); return 1 - (1 - t) * (1 - t) * (1 - t); }

        // ------------------------------------------------------------ font

        static TMP_FontAsset _font;
        static Material _display, _label;
        static float _capRatio;

        public static TMP_FontAsset Font => _font ? _font : Load();

        static TMP_FontAsset Load()
        {
            _font = Resources.Load<TMP_FontAsset>("Look/HudFont");
            if (_font == null) { Debug.LogError("UiKit: Resources/Look/HudFont missing"); return null; }
            _display = Resources.Load<Material>("Look/HudFontDisplay");
            _label = Resources.Load<Material>("Look/HudFontLabel");
            if (_display == null) _display = _font.material;
            if (_label == null) _label = _font.material;
            // Cap height over em, from the face (Titan One caps are ~0.7 em).
            float cap = _font.faceInfo.capLine, pt = _font.faceInfo.pointSize;
            _capRatio = cap > 0 && pt > 0 ? cap / pt : 0.7f;
            return _font;
        }

        /// TMP font size (em, reference px) whose capital letters stand `capPx` tall.
        public static float FontSizeForCap(float capPx) { var f = Font; return capPx / _capRatio; }
        public static float CapRatio { get { var f = Font; return _capRatio; } }

        /// Web's BASH tracking (em) per style, as TMP's characterSpacing (1/100 em).
        static float Tracking(TextStyle s) => s switch { TextStyle.Title => 0f, TextStyle.Hud => -6f, TextStyle.Display => 0f, _ => 0f };

        public static TextMeshProUGUI Text(Transform parent, string name, TextStyle style, float capPx, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center, bool italic = true, bool upper = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = Font;
            t.fontSharedMaterial = style == TextStyle.Label ? _label : _display;
            t.fontSize = FontSizeForCap(capPx);
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.richText = true;
            t.characterSpacing = Tracking(style);
            var fs = FontStyles.Normal;
            if (italic && style != TextStyle.Label) fs |= FontStyles.Italic;
            if (upper) fs |= FontStyles.UpperCase;
            t.fontStyle = fs;
            t.extraPadding = false;
            SetFill(t, color, style != TextStyle.Label);
            t.rectTransform.sizeDelta = new Vector2(800, capPx * 2.4f);
            return t;
        }

        /// BASH's fill: a vertical gradient from the colour lightened 34% to the colour darkened 16%.
        public static void SetFill(TMP_Text t, Color c, bool gradient = true)
        {
            if (!gradient) { t.enableVertexGradient = false; t.color = c; return; }
            var top = Color.Lerp(c, Color.white, 0.34f); top.a = c.a;
            var bot = Color.Lerp(c, Color.black, 0.16f); bot.a = c.a;
            t.color = Color.white;
            t.enableVertexGradient = true;
            t.colorGradient = new VertexGradient(top, top, bot, bot);
        }

        // ------------------------------------------------------------ sprites

        static Sprite _pill, _disc, _ring, _soft;

        /// A 9-sliced rounded rect (corner radius 24 of 64 px). Use with Image.type = Sliced.
        public static Sprite Pill => _pill ? _pill : _pill = MakeRounded(64, 24);
        public static Sprite Disc => _disc ? _disc : _disc = MakeDisc(64, 0);
        /// A thin ring (outer edge at the disc, stroke 8% of the diameter).
        public static Sprite Ring => _ring ? _ring : _ring = MakeDisc(64, 0.08f);
        /// A radial glow: opaque in the middle, fading to nothing at the edge.
        public static Sprite Soft => _soft ? _soft : _soft = MakeSoft(64);

        static readonly System.Collections.Generic.Dictionary<int, Sprite> _rings = new System.Collections.Generic.Dictionary<int, Sprite>();

        /// A 9-sliced rounded outline with a stroke of `strokeTexPx` texture pixels (corner 24 of 64). On screen the
        /// stroke is strokeTexPx * cornerPx / 24, so pass wantedPx * 24 / cornerPx.
        public static Sprite PillRing(float strokeTexPx)
        {
            int key = Mathf.RoundToInt(strokeTexPx * 10);
            if (!_rings.TryGetValue(key, out var s)) _rings[key] = s = MakeRounded(64, 24, strokeTexPx);
            return s;
        }

        static Texture2D NewTex(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave,
            };
            return t;
        }

        static Sprite MakeRounded(int n, int r, float stroke = 0)
        {
            var tex = NewTex(n);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, n - r), cy = Mathf.Clamp(y + 0.5f, r, n - r);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float cov = Mathf.Clamp01(r - d + 0.5f);
                    if (stroke > 0)
                    {   // distance to the nearest edge of the rounded rect, from the inside
                        float ex = Mathf.Min(x + 0.5f, n - (x + 0.5f)), ey = Mathf.Min(y + 0.5f, n - (y + 0.5f));
                        float edge = (x + 0.5f < r || x + 0.5f > n - r) && (y + 0.5f < r || y + 0.5f > n - r) ? r - d : Mathf.Min(ex, ey);
                        cov *= Mathf.Clamp01(stroke - edge + 0.5f);
                    }
                    byte a = (byte)(cov * 255f);
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        }

        static Sprite MakeDisc(int n, float ringFrac)
        {
            var tex = NewTex(n);
            var px = new Color32[n * n];
            float R = n * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - R) * (x + 0.5f - R) + (y + 0.5f - R) * (y + 0.5f - R));
                    float a = Mathf.Clamp01(R - d + 0.5f);
                    if (ringFrac > 0) a *= Mathf.Clamp01(d - (R - ringFrac * n) + 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
        }

        static Sprite MakeSoft(int n)
        {
            var tex = NewTex(n);
            var px = new Color32[n * n];
            float R = n * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - R) * (x + 0.5f - R) + (y + 0.5f - R) * (y + 0.5f - R)) / R;
                    float a = Mathf.Clamp01(1 - d); a *= a;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
        }

        // ------------------------------------------------------------ rect helpers

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// Anchor to a point of the parent (0..1), pivot at `pivot`, offset in reference px (y up), fixed size.
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        public static Image Box(Transform parent, string name, Color c, Sprite sprite = null, bool sliced = false, float cornerPx = 0)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var im = go.GetComponent<Image>();
            im.sprite = sprite; im.color = c; im.raycastTarget = false;
            if (sliced && sprite != null)
            {
                im.type = Image.Type.Sliced;
                if (cornerPx > 0) im.pixelsPerUnitMultiplier = 24f / cornerPx;   // the sprite's corner is 24 px at 100 ppu
            }
            return im;
        }
    }

    /// A vertical two-colour gradient over a UI graphic (top colour at the rect's top edge).
    [RequireComponent(typeof(Graphic))]
    public sealed class UiGradient : BaseMeshEffect
    {
        public Color Top = Color.white, Bottom = Color.white;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var rt = (RectTransform)transform;
            float yMin = rt.rect.yMin, h = Mathf.Max(1e-3f, rt.rect.height);
            var v = new UIVertex();
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                var g = Color.Lerp(Bottom, Top, Mathf.Clamp01((v.position.y - yMin) / h));
                v.color = new Color32((byte)(v.color.r * g.r), (byte)(v.color.g * g.g), (byte)(v.color.b * g.b), (byte)(v.color.a * g.a));
                vh.SetUIVertex(v, i);
            }
        }

        public void Set(Color top, Color bottom) { Top = top; Bottom = bottom; if (graphic) graphic.SetVerticesDirty(); }
    }
}
