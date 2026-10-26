// Bounce Brigade: the canvas HUD (uGUI + TextMeshPro on a canvas of its own, under the host's GameHud so the
// results card and banners stay on top). ASCII text only; the arrows are a procedural sprite.
//
//   top       the host's scoreboard (score, accuracy so far: points over the most the judged excerpts could give) in the
//             corners; in the middle the six excerpts as pips (upcoming, playing, right, first, wrong) with the level,
//             and the EARLY BONUS gauge: the points still on offer drain across the answer window, with the cloud's
//             reveal marked at its halfway point (after it an answer is worth a quarter).
//   bottom    the answer: a plate of boxes (4, 3 or 2 once the first key sets them) that fill with the keys, light one
//             per beat once full (box 1 on the beat you called the downbeat), shake empty on a wrong key or a missed
//             beat, and turn gold when locked; a state line over them; under them the key legend (DOWN 4 / LEFT 3 /
//             RIGHT 2 beats, ENTER lock, BACKSPACE clear) whose keys light as they are pressed.
//   results   one banner per excerpt (RIGHT / NOT QUITE / NO ANSWER, the points, the meter), the rivals' order under it
//             in a party, and at the end the six pips large with the tally.
//
// Presentation only: it reads the round and the audio time the game hands it, never judges anything.

using Bonanza.Core;
using Bonanza.HudKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bonanza
{
    public sealed partial class HymnMeterGame
    {
        const float RefW = 1280, RefH = 720;
        const float BoxS = 54, BoxGap = 9;

        Canvas _canvas;
        CanvasGroup _hudGroup;
        float _hudAlpha;

        sealed class BoxView
        {
            public RectTransform Rt; public Image Fill, Rim, Glow, Arrow; public UiGradient Grad; public TextMeshProUGUI Num;
            public float Pop, Shake, Lit, Show;
        }
        readonly BoxView[] _boxes = new BoxView[4];
        RectTransform _plate, _boxRow;
        Image _plateFill, _plateRim;
        TextMeshProUGUI _stateText;
        float _plateW, _break, _lockFlash, _clearFlash;
        int _shownCount = 4, _lastFilled;
        bool _lastLocked;

        sealed class KeyCap { public RectTransform Rt; public Image Fill, Rim, Icon; public TextMeshProUGUI Label; public float Flash; public Color Color; }
        readonly KeyCap[] _keys = new KeyCap[6];   // down, left, right, up, enter, backspace

        // excerpt pips + level
        readonly Image[] _pips = new Image[HymnRound.ExcerptCount], _pipRims = new Image[HymnRound.ExcerptCount];
        RectTransform _pipRow;
        TextMeshProUGUI _hymnLabel, _levelLabel;
        Image _levelChip;
        float _summary, _stripPulse;
        RectTransform _strip;

        // bonus gauge
        RectTransform _gauge;
        Image _gaugeFill, _gaugeTrack, _gaugeLate, _cloudIcon;
        TextMeshProUGUI _gaugeLabel, _gaugeMark;
        CanvasGroup _gaugeGroup;

        // rival chips (party)
        RectTransform _chipRow;
        CanvasGroup _chipGroup;
        readonly RectTransform[] _chips = new RectTransform[4];
        readonly Image[] _chipFill = new Image[4], _chipDot = new Image[4];
        readonly TextMeshProUGUI[] _chipText = new TextMeshProUGUI[4];
        float _chipLife;

        static Sprite _arrowSprite, _cloudSprite;
        static readonly Color Gold = UiKit.Gold, Coral = UiKit.Coral, Mint = UiKit.Hex(0x7df0a0), Ink = UiKit.Hex(0x0c0820);
        static readonly Color[] SeatColors = { new Color(1f, 0.85f, 0.24f), new Color(0.3f, 0.84f, 1f), new Color(1f, 0.48f, 0.85f), new Color(0.62f, 0.91f, 0.48f) };

        // ------------------------------------------------------------ sprites

        static Sprite ArrowSprite()
        {
            if (_arrowSprite) return _arrowSprite;
            Vector2[] poly =
            {
                new Vector2(0.4f, 0), new Vector2(0.02f, -0.36f), new Vector2(0.02f, -0.15f), new Vector2(-0.4f, -0.15f),
                new Vector2(-0.4f, 0.15f), new Vector2(0.02f, 0.15f), new Vector2(0.02f, 0.36f),
            };
            _arrowSprite = SdfSprite(96, (x, y) => PolySd(poly, new Vector2(x, y)), "bb-arrow");
            return _arrowSprite;
        }

        static Sprite CloudSprite()
        {
            if (_cloudSprite) return _cloudSprite;
            _cloudSprite = SdfSprite(64, (x, y) =>
            {
                float d = Mathf.Min(Vector2.Distance(new Vector2(x, y), new Vector2(-0.18f, -0.06f)) - 0.17f,
                          Mathf.Min(Vector2.Distance(new Vector2(x, y), new Vector2(0.04f, 0.05f)) - 0.22f,
                                    Vector2.Distance(new Vector2(x, y), new Vector2(0.24f, -0.07f)) - 0.15f));
                float flat = -0.2f - y;   // a flat underside
                return Mathf.Max(d, flat);
            }, "bb-cloud");
            return _cloudSprite;
        }

        /// A white sprite from a signed distance (in units of the sprite's width, negative inside), anti-aliased.
        static Sprite SdfSprite(int n, System.Func<float, float, float> sd, string name)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = sd((x + 0.5f) / n - 0.5f, (y + 0.5f) / n - 0.5f) * n;
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - d) * 255);
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
        }

        static float PolySd(Vector2[] v, Vector2 p)
        {
            float d = float.MaxValue; bool inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                var e = v[i] - v[j]; var w = p - v[j];
                var b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, b.sqrMagnitude);
                if ((v[i].y > p.y) != (v[j].y > p.y) && p.x < (v[j].x - v[i].x) * (p.y - v[i].y) / (v[j].y - v[i].y) + v[i].x) inside = !inside;
            }
            d = Mathf.Sqrt(d);
            return inside ? -d : d;
        }

        static float ArrowAngle(string key) => key == "right" ? 0 : key == "up" ? 90 : key == "left" ? 180 : -90;

        // ------------------------------------------------------------ build

        void BuildHud()
        {
            var go = new GameObject("HymnHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 19;
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(RefW, RefH);
            sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            sc.matchWidthOrHeight = 1;
            _hudGroup = go.AddComponent<CanvasGroup>();
            _hudGroup.alpha = 0;
            _hudGroup.blocksRaycasts = false; _hudGroup.interactable = false;
            var root = (RectTransform)go.transform;

            BuildTop(root);
            BuildAnswer(root);
            BuildLegend(root);
            BuildChips(root);
            BuildCallout(root);
        }

        static TextMeshProUGUI Label(Transform parent, string name, string text, float cap, Color c, TextAlignmentOptions align = TextAlignmentOptions.Center, float spacing = 8, TextStyle style = TextStyle.Label)
        {
            var t = UiKit.Text(parent, name, style, cap, c, align, italic: style != TextStyle.Label);
            t.characterSpacing = spacing;
            t.text = text;
            return t;
        }

        static void Centre(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }

        void BuildTop(RectTransform root)
        {
            // the excerpt strip: "HYMN 3 / 6", the level chip, six pips
            var strip = UiKit.Rect("Excerpts", root);
            _strip = strip;
            UiKit.Place(strip, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(360, 64));
            var back = UiKit.Box(strip, "Back", new Color(0.05f, 0.03f, 0.14f, UiKit.DarkAlpha(0.42f)), UiKit.Pill, true, 20);
            UiKit.Stretch(back.rectTransform);
            _hymnLabel = Label(strip, "Hymn", "HYMN 1 / 6", 12f, UiKit.LabelInk, TextAlignmentOptions.MidlineRight, 10);
            Centre(_hymnLabel.rectTransform, new Vector2(-28, 14), new Vector2(200, 20));
            _hymnLabel.rectTransform.pivot = new Vector2(1, 0.5f);
            _levelChip = UiKit.Box(strip, "LevelChip", Mint, UiKit.Pill, true, 10);
            Centre(_levelChip.rectTransform, new Vector2(44, 14), new Vector2(92, 20));
            _levelLabel = Label(_levelChip.transform, "Level", "EASY", 10f, Ink, TextAlignmentOptions.Midline, 10);
            UiKit.Stretch(_levelLabel.rectTransform);
            _pipRow = UiKit.Rect("Pips", strip);
            Centre(_pipRow, new Vector2(0, -12), new Vector2(6 * 26, 22));
            for (int i = 0; i < HymnRound.ExcerptCount; i++)
            {
                var rim = UiKit.Box(_pipRow, "PipRim" + i, new Color(1, 1, 1, 0.5f), UiKit.Ring);
                Centre(rim.rectTransform, new Vector2((i - 2.5f) * 26, 0), new Vector2(18, 18));
                var pip = UiKit.Box(rim.transform, "Pip", new Color(1, 1, 1, 0.12f), UiKit.Disc);
                Centre(pip.rectTransform, Vector2.zero, new Vector2(12, 12));
                _pips[i] = pip; _pipRims[i] = rim;
            }

            // the EARLY BONUS gauge
            _gauge = UiKit.Rect("Gauge", root);
            UiKit.Place(_gauge, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -88), new Vector2(380, 30));
            _gaugeGroup = _gauge.gameObject.AddComponent<CanvasGroup>();
            // a dark pill under the whole row, so both end labels read at full contrast over any sky
            var gback = UiKit.Box(_gauge, "Back", new Color(0.05f, 0.03f, 0.14f, UiKit.DarkAlpha(0.5f)), UiKit.Pill, true, 14);
            Centre(gback.rectTransform, new Vector2(4, -2), new Vector2(380 + 2 * 150, 28));
            _gaugeLabel = Label(_gauge, "Label", "EARLY BONUS", 9.5f, Color.white, TextAlignmentOptions.MidlineRight, 10);
            Centre(_gaugeLabel.rectTransform, new Vector2(-196, -2), new Vector2(160, 16));
            _gaugeLabel.rectTransform.pivot = new Vector2(1, 0.5f);
            _gaugeTrack = UiKit.Box(_gauge, "Track", new Color(0.05f, 0.03f, 0.14f, UiKit.DarkAlpha(0.6f)), UiKit.Pill, true, 7);
            Centre(_gaugeTrack.rectTransform, new Vector2(0, -2), new Vector2(380, 14));
            _gaugeLate = UiKit.Box(_gaugeTrack.transform, "Late", new Color(0.55f, 0.5f, 0.75f, 0.25f), UiKit.Pill, true, 7);
            var lr = _gaugeLate.rectTransform; lr.anchorMin = new Vector2(0.5f, 0); lr.anchorMax = new Vector2(1, 1); lr.offsetMin = new Vector2(2, 2); lr.offsetMax = new Vector2(-2, -2);
            _gaugeFill = UiKit.Box(_gaugeTrack.transform, "Fill", Gold, UiKit.Pill, true, 5);
            var fr = _gaugeFill.rectTransform; fr.anchorMin = new Vector2(0, 0); fr.anchorMax = new Vector2(1, 1); fr.offsetMin = new Vector2(3, 3); fr.offsetMax = new Vector2(-3, -3);
            fr.gameObject.AddComponent<UiGradient>().Set(Color.Lerp(Gold, Color.white, 0.4f), Color.Lerp(Gold, UiKit.Orange, 0.5f));
            _cloudIcon = UiKit.Box(_gauge, "Cloud", Color.white, CloudSprite());
            Centre(_cloudIcon.rectTransform, new Vector2(0, -2), new Vector2(34, 34));
            _gaugeMark = Label(_gauge, "Mark", "CLOUD LIFTS", 9.5f, Color.white, TextAlignmentOptions.MidlineLeft, 10);
            Centre(_gaugeMark.rectTransform, new Vector2(200, -2), new Vector2(160, 16));
            _gaugeMark.rectTransform.pivot = new Vector2(0, 0.5f);
        }

        void BuildAnswer(RectTransform root)
        {
            _plate = UiKit.Rect("Answer", root);
            UiKit.Place(_plate, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(4 * BoxS + 3 * BoxGap + 36, BoxS + 42));
            _plateFill = UiKit.Box(_plate, "Fill", new Color(0.04f, 0.03f, 0.13f, UiKit.DarkAlpha(0.42f)), UiKit.Pill, true, 22);
            UiKit.Stretch(_plateFill.rectTransform);
            _plateRim = UiKit.Box(_plate, "Rim", new Color(1, 1, 1, 0.16f), UiKit.PillRing(2f * 24f / 22f), true, 22);
            UiKit.Stretch(_plateRim.rectTransform);
            _stateText = Label(_plate, "State", "", 10.5f, UiKit.Cream, TextAlignmentOptions.Midline, 10);
            _stateText.rectTransform.anchorMin = _stateText.rectTransform.anchorMax = new Vector2(0.5f, 1);
            _stateText.rectTransform.pivot = new Vector2(0.5f, 1);
            _stateText.rectTransform.anchoredPosition = new Vector2(0, -8); _stateText.rectTransform.sizeDelta = new Vector2(600, 20);
            _boxRow = UiKit.Rect("Boxes", _plate);
            _boxRow.anchorMin = _boxRow.anchorMax = new Vector2(0.5f, 0); _boxRow.pivot = new Vector2(0.5f, 0);
            _boxRow.anchoredPosition = new Vector2(0, 12); _boxRow.sizeDelta = new Vector2(4 * BoxS + 3 * BoxGap, BoxS);
            for (int i = 0; i < 4; i++)
            {
                var v = new BoxView();
                v.Rt = UiKit.Rect("Box" + i, _boxRow);
                Centre(v.Rt, Vector2.zero, new Vector2(BoxS, BoxS));
                v.Rt.anchorMin = v.Rt.anchorMax = new Vector2(0.5f, 0.5f);
                v.Glow = UiKit.Box(v.Rt, "Glow", new Color(1, 1, 1, 0), UiKit.Soft);
                Centre(v.Glow.rectTransform, Vector2.zero, new Vector2(BoxS * 2.1f, BoxS * 2.1f));
                v.Fill = UiKit.Box(v.Rt, "Fill", new Color(1, 1, 1, 0.08f), UiKit.Pill, true, 16);
                UiKit.Stretch(v.Fill.rectTransform);
                v.Grad = v.Fill.gameObject.AddComponent<UiGradient>();
                v.Rim = UiKit.Box(v.Rt, "Rim", new Color(1, 1, 1, 0.3f), UiKit.PillRing(2.5f * 24f / 16f), true, 16);
                UiKit.Stretch(v.Rim.rectTransform);
                v.Arrow = UiKit.Box(v.Rt, "Arrow", Color.white, ArrowSprite());
                Centre(v.Arrow.rectTransform, Vector2.zero, new Vector2(BoxS * 0.86f, BoxS * 0.86f));
                v.Num = Label(v.Rt, "Num", (i + 1).ToString(), 9f, new Color(1, 1, 1, 0.55f), TextAlignmentOptions.TopLeft, 0);
                v.Num.rectTransform.anchorMin = v.Num.rectTransform.anchorMax = v.Num.rectTransform.pivot = new Vector2(0, 1);
                v.Num.rectTransform.anchoredPosition = new Vector2(8, -6); v.Num.rectTransform.sizeDelta = new Vector2(20, 14);
                v.Show = 1;
                _boxes[i] = v;
            }
            _plateW = 4 * BoxS + 3 * BoxGap + 40;
        }

        void BuildLegend(RectTransform root)
        {
            var row = UiKit.Rect("Legend", root);
            UiKit.Place(row, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(1000, 30));
            // the beat keys start a bar of 4, 3 or 2 beats; UP is the bar's last beat in every pattern
            string[] keys = { "down", "left", "right", "up", null, null };
            string[] labels = { "4 BEATS", "3 BEATS", "2 BEATS", "LAST BEAT", "LOCK", "CLEAR" };
            string[] caps = { null, null, null, null, "ENTER", "BACKSPACE" };
            float[] widths = { 112, 112, 112, 128, 118, 154 };
            float total = 0; foreach (var w in widths) total += w; total += 10 * (widths.Length - 1) + 18;
            float x = -total / 2;
            for (int i = 0; i < 6; i++)
            {
                if (i == 4) x += 18;   // a little air between the beat keys and the commands
                var k = new KeyCap();
                k.Rt = UiKit.Rect("Key" + i, row);
                Centre(k.Rt, new Vector2(x + widths[i] / 2, 0), new Vector2(widths[i], 28));
                k.Color = keys[i] != null ? KeyColor(keys[i]) : (i == 4 ? Gold : UiKit.Dim);
                k.Fill = UiKit.Box(k.Rt, "Fill", new Color(0.04f, 0.03f, 0.13f, UiKit.DarkAlpha(0.55f)), UiKit.Pill, true, 14);
                UiKit.Stretch(k.Fill.rectTransform);
                k.Rim = UiKit.Box(k.Rt, "Rim", new Color(k.Color.r, k.Color.g, k.Color.b, 0.55f), UiKit.PillRing(2f * 24f / 14f), true, 14);
                UiKit.Stretch(k.Rim.rectTransform);
                float lx;
                if (keys[i] != null)
                {
                    var capBox = UiKit.Box(k.Rt, "Cap", k.Color, UiKit.Pill, true, 9);
                    Centre(capBox.rectTransform, new Vector2(-widths[i] / 2 + 18, 0), new Vector2(24, 20));
                    k.Icon = UiKit.Box(capBox.transform, "Arrow", Ink, ArrowSprite());
                    Centre(k.Icon.rectTransform, Vector2.zero, new Vector2(22, 22));
                    k.Icon.rectTransform.localRotation = Quaternion.Euler(0, 0, ArrowAngle(keys[i]));
                    lx = -widths[i] / 2 + 36;
                }
                else
                {
                    var capBox = UiKit.Box(k.Rt, "Cap", k.Color, UiKit.Pill, true, 9);
                    float cw = caps[i].Length * 8.2f + 14;
                    Centre(capBox.rectTransform, new Vector2(-widths[i] / 2 + 6 + cw / 2, 0), new Vector2(cw, 20));
                    var ct = Label(capBox.transform, "CapText", caps[i], 8f, Ink, TextAlignmentOptions.Midline, 6);
                    UiKit.Stretch(ct.rectTransform);
                    lx = -widths[i] / 2 + 12 + cw;
                }
                k.Label = Label(k.Rt, "Label", labels[i], 9f, UiKit.LabelInk, TextAlignmentOptions.MidlineLeft, 8);
                k.Label.rectTransform.anchorMin = k.Label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                k.Label.rectTransform.pivot = new Vector2(0, 0.5f);
                k.Label.rectTransform.anchoredPosition = new Vector2(lx, 0); k.Label.rectTransform.sizeDelta = new Vector2(120, 18);
                _keys[i] = k;
                x += widths[i] + 10;
            }
        }

        void BuildChips(RectTransform root)
        {
            _chipRow = UiKit.Rect("Rivals", root);
            // the rivals' order stacks down the right edge under the accuracy readout, clear of the jumper and the stair
            UiKit.Place(_chipRow, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-14, -84), new Vector2(160, 4 * 34));
            _chipGroup = _chipRow.gameObject.AddComponent<CanvasGroup>();
            _chipGroup.alpha = 0;
            for (int i = 0; i < 4; i++)
            {
                var rt = UiKit.Rect("Chip" + i, _chipRow);
                Centre(rt, Vector2.zero, new Vector2(156, 28));
                _chipFill[i] = UiKit.Box(rt, "Fill", new Color(0.04f, 0.03f, 0.13f, UiKit.DarkAlpha(0.7f)), UiKit.Pill, true, 14);
                UiKit.Stretch(_chipFill[i].rectTransform);
                _chipDot[i] = UiKit.Box(rt, "Dot", Color.white, UiKit.Disc);
                Centre(_chipDot[i].rectTransform, new Vector2(-62, 0), new Vector2(14, 14));
                _chipText[i] = Label(rt, "Text", "", 9.5f, Color.white, TextAlignmentOptions.MidlineLeft, 8);
                _chipText[i].rectTransform.anchorMin = _chipText[i].rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                _chipText[i].rectTransform.pivot = new Vector2(0, 0.5f);
                _chipText[i].rectTransform.anchoredPosition = new Vector2(-50, 0); _chipText[i].rectTransform.sizeDelta = new Vector2(130, 18);
                _chips[i] = rt;
                rt.gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------ events

        float _bannerLife;

        /// The host's banner (one per slot: a new one replaces the one on screen); the bonus gauge steps aside while it shows.
        void Banner(string text, string sub, Color color, float life, bool plate = false, float y = 0.2f, float scale = 0.72f)
        {
            Ui.Banner(text, sub, color, life, plate, y, scale);
            _bannerLife = life;
        }

        void BeginHud()
        {
            _hudAlpha = 0;
            Ui.ShowScoreboard();
            Banner(Title, Blurb, Gold, 1.6f, y: 0.3f, scale: 1);
        }

        void HudStarted(HymnSlot slot)
        {
            // no banner over the stair: the strip announces the excerpt (it pops, the level chip flares)
            _stripPulse = 1;
            _chipLife = 0;
        }

        void HudRevealed(HymnSlot slot) { }

        void HudJudged(HymnSlot slot, HymnJudgement j)
        {
            var me = j.Results[0];
            int pts = Mathf.RoundToInt((float)j.HumanPoints);
            string meter = me.Submitted && me.Count == slot.Pattern ? $"{slot.Pattern} BEATS - BUT BOX 1 MISSED THE DOWNBEAT" : $"THE BAR HAD {slot.Pattern} BEATS";
            if (me.Correct)
            {
                string sub = me.PostReveal ? $"+{pts}  -  AFTER THE REVEAL: 1/4" : j.Winner == 0 ? $"+{pts}  -  FIRST!" : $"+{pts}";
                Callout(me.PostReveal ? "RIGHT" : "RIGHT!", sub, me.PostReveal ? UiKit.Cream : Gold, 1.5f);
            }
            else
            {
                Callout(me.Submitted ? "NOT QUITE" : "NO ANSWER", meter, me.Submitted ? Coral : UiKit.Dim, 1.5f);
            }
            var t = _round.Totals();
            Ui.Score(Mathf.RoundToInt((float)t[0]));
            double got = 0, ceil = 0;
            foreach (var s in _round.Slots) if (s.Judgement != null) { got += s.Judgement.HumanPoints; ceil += HymnRound.CeilingPoints(s); }
            Ui.Accuracy(ceil > 0 ? Mathf.Clamp01((float)(got / ceil)) : 1);

            // the rivals, in the order they finished (a party only)
            int n = j.Results.Count;
            if (n > 1)
            {
                var order = new int[n];
                for (int i = 0; i < n; i++) order[i] = i;
                var rank = new MeterRanking();
                System.Array.Sort(order, (a, b) => { int c = rank.Compare(j.Results[a], j.Results[b]); return c != 0 ? c : a.CompareTo(b); });
                int shown = Mathf.Min(4, n);
                float w = 130, gap = 10, x0 = -(shown * w + (shown - 1) * gap) / 2 + w / 2;
                for (int c = 0; c < 4; c++)
                {
                    bool on = c < shown;
                    _chips[c].gameObject.SetActive(on);
                    if (!on) continue;
                    int who = order[c];
                    var r = j.Results[who];
                    _chips[c].anchoredPosition = new Vector2(0, 51 - c * 34f);
                    string name = who == 0 ? "YOU" : CpuName(who);
                    string place = r.Correct ? (c == 0 ? "1ST" : c == 1 ? "2ND" : c == 2 ? "3RD" : "4TH") : r.Submitted ? "WRONG" : "-";
                    _chipText[c].text = $"{name}  {place}";
                    _chipText[c].color = r.Correct ? Color.white : new Color(1, 1, 1, 0.55f);
                    _chipDot[c].color = SeatFor(who);
                    _chipFill[c].color = who == 0 ? new Color(0.16f, 0.12f, 0.34f, UiKit.DarkAlpha(0.8f)) : new Color(0.04f, 0.03f, 0.13f, UiKit.DarkAlpha(0.7f));
                }
                _chipLife = 2.4f;
            }
        }

        string CpuName(int resultIndex)
        {
            if (Host.Lineup == null) return "CPU " + resultIndex;
            int k = 0;
            foreach (var p in Host.Lineup)
                if (p.IsCpu && ++k == resultIndex) return string.IsNullOrEmpty(p.Name) ? "CPU " + resultIndex : p.Name.ToUpperInvariant();
            return "CPU " + resultIndex;
        }

        Color SeatFor(int resultIndex)
        {
            if (_ids != null && resultIndex < _ids.Count) return SeatColors[_ids[resultIndex] % SeatColors.Length];
            return SeatColors[resultIndex % SeatColors.Length];
        }

        void HudSummary()
        {
            int right = 0, won = 0;
            foreach (var s in _round.Slots) { if (s.Judgement == null) continue; if (s.Judgement.Results[0].Correct) right++; if (s.Judgement.Winner == 0) won++; }
            string sub = _round.CpuCount > 0 ? $"FIRST ON {won} OF {HymnRound.ExcerptCount}" : right == HymnRound.ExcerptCount ? "EVERY BAR FOUND" : "";
            Callout($"{right} / {HymnRound.ExcerptCount} RIGHT", sub, right >= 5 ? Gold : right >= 3 ? UiKit.Cream : Coral, 2.6f);
            _summary = 0.001f;
        }

        void HudPressed(string action, MeterAnswer ans, int before, bool wasLocked)
        {
            int ki = action == "down" ? 0 : action == "left" ? 1 : action == "right" ? 2 : action == "up" ? 3 : action == InputMap.A ? 4 : action == InputMap.B ? 5 : -1;
            if (ki >= 0) _keys[ki].Flash = 1;
            if (ans == null) return;
            if (action == InputMap.B && before > 0 && ans.Filled == 0) { _clearFlash = 1; _lastFilled = 0; return; }
            if (action == InputMap.A && ans.Locked && !wasLocked) { _lockFlash = 1; return; }
            if (ans.Filled > before && ans.Filled > 0) _boxes[ans.Filled - 1].Pop = 1;
        }

        static Color PipColor(HymnSlot s)
        {
            var j = s.Judgement;
            if (j == null) return new Color(1, 1, 1, 0.12f);
            var me = j.Results[0];
            return me.Correct ? (j.Winner == 0 && !me.PostReveal ? Gold : Mint) : me.Submitted ? Coral : new Color(0.55f, 0.55f, 0.7f);
        }

        static Color LevelColor(HymnLevel l) => l == HymnLevel.Easy ? Mint : l == HymnLevel.Medium ? Gold : Coral;

        // ------------------------------------------------------------ per frame

        void UpdateHud(float dt)
        {
            if (_canvas == null) return;
            float real = Time.unscaledDeltaTime;
            bool reduce = Settings.ReduceMotion;
            bool live = _round.Started && !_resultOut;
            _hudAlpha = Damp(_hudAlpha, live ? 1 : 0, live ? 5 : 9, real);
            _hudGroup.alpha = _hudAlpha;
            if (!_round.Started) return;

            var slot = Shown ?? CurrentSlot;
            var ans = slot?.Answer;

            // ---- excerpt strip
            int cur = Mathf.Clamp(_round.Current, 0, HymnRound.ExcerptCount - 1);
            var ss = _round.Slots[cur];
            if (cur != _stripShown)
            {
                _stripShown = cur;
                _hymnLabel.text = $"HYMN {cur + 1} / {HymnRound.ExcerptCount}";
                _levelLabel.text = ss.Level.ToString().ToUpperInvariant();
            }
            _stripPulse = Mathf.Max(0, _stripPulse - real * 1.4f);
            float sp = reduce ? 0 : Mathf.Sin(Mathf.Clamp01(1 - _stripPulse) * Mathf.PI) * _stripPulse;
            _strip.localScale = Vector3.one * (1 + sp * 0.12f);
            _levelChip.color = Color.Lerp(LevelColor(ss.Level), Color.white, _stripPulse * 0.5f);
            if (_summary > 0) _summary = Mathf.Min(1, _summary + real * 2.5f);
            float sumK = reduce ? (_summary > 0 ? 1 : 0) : UiKit.EaseOutCubic(_summary);
            _pipRow.localScale = Vector3.one * (1 + sumK * 0.5f);
            for (int i = 0; i < HymnRound.ExcerptCount; i++)
            {
                var j = i < _round.Slots.Count ? _round.Slots[i].Judgement : null;
                Color c; float rimA;
                if (j != null)
                {
                    var me = j.Results[0];
                    c = me.Correct ? (j.Winner == 0 && !me.PostReveal ? Gold : Mint) : me.Submitted ? Coral : new Color(0.55f, 0.55f, 0.7f);
                    rimA = 0.9f;
                }
                else if (i == _round.Current)
                {
                    float pulse = reduce ? 0.6f : 0.45f + 0.35f * Mathf.Exp(-3.5f * BeatPhase);
                    c = new Color(1, 1, 1, pulse); rimA = 1;
                }
                else { c = new Color(1, 1, 1, 0.1f); rimA = 0.35f; }
                _pips[i].color = c;
                _pipRims[i].color = new Color(1, 1, 1, rimA);
            }

            // ---- bonus gauge
            if (slot != null)
            {
                float span = (float)(slot.WindowEnd - slot.WindowStart);
                double tNow = ans != null && ans.Locked ? ans.SubmitTime : _now;
                float left = span > 0 ? Mathf.Clamp01(1 - (float)((tNow - slot.WindowStart) / span)) : 0;
                if (_now < slot.WindowStart) left = 1;
                var fr = _gaugeFill.rectTransform;
                fr.anchorMax = new Vector2(Mathf.Max(0.02f, left), 1);
                bool locked = ans != null && ans.Locked;
                bool late = slot.Revealed && !locked;
                var fc = locked ? Mint : late ? new Color(0.62f, 0.58f, 0.82f) : Gold;
                _gaugeFill.color = Color.Lerp(fc, Color.white, _lockFlash * 0.6f);
                _gaugeFill.enabled = left > 0.01f;
                _gaugeMark.text = locked ? (ans.SubmitTime > _round.RevealAt(slot) ? "LOCKED - 1/4" : "LOCKED") : slot.Revealed ? "REVEALED - 1/4" : "CLOUD LIFTS";
                _gaugeMark.color = locked ? Mint : slot.Revealed ? new Color(0.9f, 0.86f, 1f) : Color.white;
                // the reveal marker sits at the halfway point of the track: it trembles as the bonus drains toward it,
                // and when the cloud lifts on the stage it drifts up off the bar and fades
                _cloudLift = slot.Revealed ? Mathf.Min(1, _cloudLift + real * 1.4f) : 0;
                float near = Mathf.Clamp01(1 - Mathf.Abs(left - 0.5f) * 8f);
                float tremble = reduce || slot.Revealed ? 0 : Mathf.Sin(Time.unscaledTime * 30f) * 1.5f * near;
                float bob = reduce ? 0 : Mathf.Sin(Time.unscaledTime * 2.2f) * 1.2f;
                float lift = reduce ? (_cloudLift > 0 ? 1 : 0) : UiKit.EaseOutCubic(_cloudLift);
                _cloudIcon.color = new Color(1, 1, 1, slot.Revealed ? Mathf.Lerp(1, 0.3f, lift) : 1);
                _cloudIcon.rectTransform.anchoredPosition = new Vector2(tremble + lift * 10f, -2 + bob + lift * 22f);
                _cloudIcon.rectTransform.localScale = Vector3.one * (1 + near * 0.15f + lift * 0.2f);
                _gaugeGroup.alpha = Damp(_gaugeGroup.alpha, _bannerLife > 0.15f ? 0 : _now > slot.WindowEnd + 0.2 ? 0.35f : 1, 10, real);
            }

            _bannerLife = Mathf.Max(0, _bannerLife - real);

            // ---- answer boxes
            _lockFlash = Mathf.Max(0, _lockFlash - real * 2.2f);
            _clearFlash = Mathf.Max(0, _clearFlash - real * 3f);
            if (ans != null)
            {
                if (_lastFilled > 0 && ans.Filled == 0 && _clearFlash <= 0 && !ans.Locked) _break = 1;   // a wrong key or a missed beat
                _lastFilled = ans.Filled;
            }
            _break = Mathf.Max(0, _break - real * 1.6f);
            int count = ans != null && ans.Count > 0 ? ans.Count : 4;
            if (ans != null && ans.Count == 0 && _break > 0) count = _shownCount;
            _shownCount = count;
            int lit = ans != null ? ans.LitBox(_now) : -1;
            bool full = ans != null && ans.Full, lockedNow = ans != null && ans.Locked;
            if (lockedNow && !_lastLocked) _lockFlash = 1;
            _lastLocked = lockedNow;
            float rowW = count * BoxS + (count - 1) * BoxGap;
            float shake = reduce ? 0 : Mathf.Sin(_break * 40f) * 7f * _break;
            for (int i = 0; i < 4; i++)
            {
                var v = _boxes[i];
                v.Show = Damp(v.Show, i < count ? 1 : 0, 12, real);
                v.Pop = Mathf.Max(0, v.Pop - real * 4f);
                v.Lit = Damp(v.Lit, i == lit ? 1 : 0, i == lit ? 30 : 9, real);
                bool on = v.Show > 0.02f;
                if (v.Rt.gameObject.activeSelf != on) v.Rt.gameObject.SetActive(on);
                if (!on) continue;
                float x = -rowW / 2 + BoxS / 2 + i * (BoxS + BoxGap);
                v.Rt.anchoredPosition = new Vector2(x + shake, 0);
                float pop = reduce ? 0 : v.Pop * 0.16f + v.Lit * 0.1f;
                v.Rt.localScale = Vector3.one * v.Show * (1 + pop);
                string key = ans?.KeyAt(i);
                if (key != null)
                {
                    var kc = KeyColor(key);
                    var c = full ? Color.Lerp(Color.Lerp(kc, Ink, 0.3f), kc, v.Lit) : kc;
                    if (lockedNow) c = Color.Lerp(c, Gold, 0.15f + 0.4f * _lockFlash);
                    c.a = 1;
                    v.Fill.color = Color.white;
                    v.Grad.Set(Color.Lerp(c, Color.white, 0.25f), Color.Lerp(c, Color.black, 0.15f));
                    v.Rim.color = lockedNow ? Gold : new Color(1, 1, 1, 0.35f + 0.6f * v.Lit);
                    v.Arrow.enabled = true;
                    v.Arrow.color = Color.white;
                    v.Arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, ArrowAngle(key));
                    v.Glow.color = new Color(kc.r, kc.g, kc.b, 0.55f * v.Lit + 0.3f * v.Pop);
                    v.Num.color = new Color(1, 1, 1, 0.85f);
                }
                else
                {
                    var c = _break > 0 ? Color.Lerp(new Color(1, 1, 1, 0.08f), new Color(Coral.r, Coral.g, Coral.b, 0.5f), _break) : new Color(1, 1, 1, 0.07f);
                    if (_clearFlash > 0) c = Color.Lerp(c, new Color(1, 1, 1, 0.4f), _clearFlash);
                    v.Fill.color = Color.white;
                    v.Grad.Set(c, new Color(c.r, c.g, c.b, c.a * 0.6f));
                    v.Rim.color = _break > 0 ? Color.Lerp(new Color(1, 1, 1, 0.28f), Coral, _break) : new Color(1, 1, 1, 0.28f);
                    v.Arrow.enabled = false;
                    v.Glow.color = new Color(1, 1, 1, 0);
                    v.Num.color = new Color(1, 1, 1, i == 0 ? 0.75f : 0.4f);
                }
            }
            float plateW = Mathf.Max(rowW + 40, _stateW + 56);
            _plateW = Damp(_plateW, Mathf.Max(plateW, 300), 12, real);
            _plate.sizeDelta = new Vector2(_plateW, _plate.sizeDelta.y);
            _plateRim.color = lockedNow ? Color.Lerp(new Color(Gold.r, Gold.g, Gold.b, 0.7f), Color.white, _lockFlash) : _break > 0 ? Color.Lerp(new Color(1, 1, 1, 0.16f), Coral, _break) : new Color(1, 1, 1, 0.16f);
            int code = StateCode(slot, ans);
            if (code != _stateShown) { _stateShown = code; _stateText.text = StateLine(slot, ans); _stateText.ForceMeshUpdate(); _stateW = _stateText.preferredWidth; }
            _stateText.color = lockedNow ? Gold : _break > 0.3f ? Coral : UiKit.Cream;

            // ---- legend
            for (int i = 0; i < 6; i++)
            {
                var k = _keys[i];
                k.Flash = Mathf.Max(0, k.Flash - real * 5f);
                bool hint = (i == 4 && full && !lockedNow) || (i == 5 && ans != null && ans.Filled > 0 && !lockedNow);
                bool dim = lockedNow || (i < 3 && ans != null && ans.Filled > 0) || (i == 3 && (ans == null || ans.Filled == 0 || full)) || (i == 4 && !full) || (i == 5 && (ans == null || ans.Filled == 0));
                float glow = Mathf.Max(k.Flash, hint ? 0.45f + 0.35f * Mathf.Exp(-3.5f * BeatPhase) : 0);
                var kc = k.Color;
                k.Rim.color = new Color(kc.r, kc.g, kc.b, (dim ? 0.25f : 0.6f) + 0.4f * glow);
                k.Fill.color = Color.Lerp(new Color(0.04f, 0.03f, 0.13f, UiKit.DarkAlpha(0.55f)), new Color(kc.r * 0.45f, kc.g * 0.45f, kc.b * 0.45f, 0.85f), glow);
                k.Label.color = new Color(1, 1, 1, dim ? 0.45f : 0.9f);
                k.Rt.localScale = Vector3.one * (1 + (reduce ? 0 : k.Flash * 0.08f));
            }

            UpdateCallout(real);

            // ---- rival chips
            _chipLife = Mathf.Max(0, _chipLife - real);
            _chipGroup.alpha = Damp(_chipGroup.alpha, _chipLife > 0 ? 1 : 0, 10, real);
        }

        int _stripShown = -1, _stateShown = -1;

        // ------------------------------------------------------------ the verdict callout
        // Its own slot beside the answer plate (the gauge stays visible): a compact dark pill with the verdict and
        // its points, popping in with an overshoot, held, then snapping away (scale, never a fade over the scenery).
        RectTransform _callRt;
        Image _callFill, _callRim;
        TextMeshProUGUI _callMain, _callSub;
        float _callAge = -1, _callHold;
        const float CallIn = 0.16f, CallOut = 0.14f;

        void BuildCallout(RectTransform root)
        {
            _callRt = UiKit.Rect("Callout", root);
            // beside the answer plate, in the band under the stair: never over the choir or the jumper
            UiKit.Place(_callRt, new Vector2(0.5f, 0), new Vector2(0, 0.5f), new Vector2(196, 118), new Vector2(360, 64));
            _callFill = UiKit.Box(_callRt, "Fill", new Color(0.04f, 0.03f, 0.13f, UiKit.DarkAlpha(0.82f)), UiKit.Pill, true, 22);
            UiKit.Stretch(_callFill.rectTransform);
            _callRim = UiKit.Box(_callRt, "Rim", Gold, UiKit.PillRing(2.2f * 24f / 22f), true, 22);
            UiKit.Stretch(_callRim.rectTransform);
            _callMain = UiKit.Text(_callRt, "Main", TextStyle.Title, 24f, Gold, TextAlignmentOptions.Center);
            UiKit.Place(_callMain.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 8), new Vector2(600, 40));
            _callSub = Label(_callRt, "Sub", "", 9.5f, UiKit.Gold, TextAlignmentOptions.Center, 8);
            UiKit.Place(_callSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -19), new Vector2(600, 18));
            _callRt.gameObject.SetActive(false);
        }

        void Callout(string text, string sub, Color color, float hold)
        {
            UiKit.SetFill(_callMain, color);
            _callMain.text = text;
            _callSub.text = sub ?? "";
            _callSub.color = Color.Lerp(color, Color.white, 0.35f);
            _callRim.color = new Color(color.r, color.g, color.b, 0.9f);
            _callMain.ForceMeshUpdate(); _callSub.ForceMeshUpdate();
            float w = Mathf.Max(_callMain.preferredWidth, _callSub.preferredWidth) + 64;
            _callRt.sizeDelta = new Vector2(Mathf.Max(220, w), string.IsNullOrEmpty(sub) ? 48 : 64);
            _callAge = 0; _callHold = hold;
            _callRt.gameObject.SetActive(true);
        }

        void UpdateCallout(float real)
        {
            if (_callAge < 0) return;
            _callAge += real;
            float end = CallIn + _callHold + CallOut;
            if (_callAge >= end) { _callAge = -1; _callRt.gameObject.SetActive(false); return; }
            bool calm = Settings.ReduceMotion;
            float sc;
            if (_callAge < CallIn) sc = calm ? 1 : UiKit.BackOut(_callAge / CallIn, 2.2f);
            else if (_callAge > CallIn + _callHold) sc = calm ? 1 : 1 - UiKit.EaseOutCubic((_callAge - CallIn - _callHold) / CallOut);
            else sc = 1;
            _callRt.localScale = new Vector3(sc, sc, 1);
        }
        float _stateW, _cloudLift;

        /// What the state line says, as a number, so the text is rebuilt only when it changes.
        int StateCode(HymnSlot slot, MeterAnswer a)
        {
            if (slot == null || a == null) return 0;
            if (_now < slot.WindowStart) return 1;
            if (a.Locked) return a.SubmitTime > _round.RevealAt(slot) ? 2 : 3;
            if (_now > slot.WindowEnd) return 4;
            if (_break > 0.3f) return 5;
            if (a.Full) return 6;
            if (a.Filled > 0) return 100 + a.Filled * 10 + a.Count;
            return 7;
        }

        string StateLine(HymnSlot slot, MeterAnswer a)
        {
            if (slot == null || a == null) return "";
            if (_now < slot.WindowStart) return "LISTEN...";
            if (a.Locked) return a.SubmitTime > _round.RevealAt(slot) ? "LOCKED AFTER THE REVEAL" : "LOCKED IN";
            if (_now > slot.WindowEnd) return "TIME";
            if (_break > 0.3f) return "START AGAIN ON A DOWNBEAT";
            if (a.Full) return "ENTER TO LOCK  -  BACKSPACE TO CLEAR";
            if (a.Filled > 0) return $"{a.Filled} OF {a.Count}  -  ONE KEY PER BEAT";
            return "FIRST KEY ON THE DOWNBEAT";
        }
    }
}
