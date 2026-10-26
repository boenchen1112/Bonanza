// Finale Fever: the canvas HUD (uGUI + TextMeshPro on a canvas of its own, under the host's GameHud so the results
// card and banners stay on top). ASCII text only; arrows, hearts and the fermata are procedural sprites.
//
//   top       the host's scoreboard in the corners (score + combo left, accuracy right) with the three HEARTS under the
//             accuracy (they break on a lost line, refill on a clean one; the last one throbs; none = SURVIVE). In the
//             middle the PROGRAMME: the level chip, HYMN n / 3 and one pip per line of the excerpt (clean gold, kept mint,
//             patchy amber, lost coral; a line's verdict also flashes as the strip's title), and under it the TEMPO GAUGE:
//             the tempo as heard (bpm), what it is doing (FASTER / SLOWER / STEADY, JUMP IN 2, HOLD) and a ribbon of the
//             curve ahead with the beats as dots, jumps marked, fermatas as their sign. A hidden stretch is a dark BY EAR
//             band on the ribbon with no curve in it; while it plays the gauge shows no tempo at all.
//   bottom    the TIMING METER: EARLY | ON | LATE across a track scaled to the beat's own window, the last taps as ticks,
//             the newest as a big diamond, and the LEAN needle under it (the recent average, also in ms). Over it ONE
//             callout plate (EARLY / ON / LATE / MISS) that each tap replaces.
//   finale    the meter rises to the middle and grows: your average lead or lag over the whole round.
//
// Presentation only: it reads the round and the hooks, never judges anything.

using Bonanza.Core;
using Bonanza.HudKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bonanza
{
    public sealed partial class FinaleFeverGame
    {
        const float RefW = 1280, RefH = 720;
        const float TrackW = 360, TrackH = 14;

        Canvas _canvas;
        CanvasGroup _hudGroup;
        float _hudAlpha;

        static readonly Color Ink = UiKit.Hex(0x0c0820), Mint = UiKit.Hex(0x7df0a0), Amber = UiKit.Hex(0xffb04a);
        static readonly Color CallOn = UiKit.Hex(0xb6f27a), CallEarly = UiKit.Hex(0x6fd0ff), CallLate = UiKit.Hex(0xffa04a), CallMiss = UiKit.Hex(0xff5d73);

        // programme strip
        RectTransform _strip, _pipRow;
        Image _levelChip;
        TextMeshProUGUI _levelText, _stripTitle;
        readonly Image[] _pips = new Image[10], _pipRims = new Image[10];
        int _pipCount, _stripSlot = -1;
        float _stripPulse, _toastLife;
        string _toast;
        Color _toastColor;

        // tempo gauge
        RectTransform _tempo;
        TextMeshProUGUI _bpmText, _bpmUnit, _trendText, _trendSub;
        Image _trendIcon, _gaugeFill;
        TempoRibbon _ribbon;
        readonly Image[] _fermataMarks = new Image[3];
        int _bpmShown = -999, _trendShown = -1;
        double[] _bpmLo, _bpmHi;
        double[][] _fermBpm;

        // hearts
        sealed class HeartView { public RectTransform Rt; public Image Back, Fill, Glow, Shard; public float Pop, Break, Shard01; public bool Full = true; }
        readonly HeartView[] _hearts = new HeartView[3];
        RectTransform _surviveTag;
        TextMeshProUGUI _surviveText;
        float _surviveShow;

        // meter
        RectTransform _meter, _track, _needle, _diamond;
        Image _meterPlate, _meterRim, _diamondImg, _needleImg, _onZone;
        TextMeshProUGUI _leanText, _meterTitle, _earlyLbl, _lateLbl;
        readonly RectTransform[] _mticks = new RectTransform[10];
        readonly Image[] _tickImg = new Image[10];
        readonly float[] _tickX = new float[10], _tickAge = new float[10];
        readonly Color[] _tickCol = new Color[10];
        int _tickNext, _leanShown = int.MinValue;
        float _vigA, _diamondX, _diamondPop, _lean01, _leanMs, _finaleK;
        bool _anyTap;

        // callout (one slot)
        RectTransform _callout;
        Image _callPlate, _callRim;
        TextMeshProUGUI _callText, _callMs;
        float _callLife, _callPop;

        // rival chips (party)
        RectTransform _chipRow;
        CanvasGroup _chipGroup;
        readonly RectTransform[] _chips = new RectTransform[4];
        readonly Image[] _chipFill = new Image[4], _chipDot = new Image[4];
        readonly TextMeshProUGUI[] _chipText = new TextMeshProUGUI[4];
        float _chipLife;

        static Sprite _arrowSprite, _heartSprite, _fermataSprite, _triSprite;

        // ------------------------------------------------------------ sprites

        /// A white anti-aliased sprite from an inside test (4 x 4 samples a pixel), in units of the sprite's width
        /// (x, y in -0.5 .. 0.5, y up).
        static Sprite Coverage(int n, System.Func<float, float, bool> inside, string name)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int c = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            if (inside((x + (sx + 0.5f) / 4) / n - 0.5f, (y + (sy + 0.5f) / 4) / n - 0.5f)) c++;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(c * 255 / 16));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
        }

        static Sprite HeartSprite() => _heartSprite ? _heartSprite : _heartSprite = Coverage(96, (x, y) =>
        {
            float X = x * 2.6f, Y = y * 2.6f + 0.12f;
            float a = X * X + Y * Y - 1;
            return a * a * a - X * X * Y * Y * Y <= 0;
        }, "ff-heart");

        static Sprite ArrowSprite() => _arrowSprite ? _arrowSprite : _arrowSprite = Coverage(64, (x, y) =>
        {
            if (x > 0.02f) return Mathf.Abs(y) < (0.42f - x) * 1.05f;
            return x > -0.4f && Mathf.Abs(y) < 0.13f;
        }, "ff-arrow");

        static Sprite FermataSprite() => _fermataSprite ? _fermataSprite : _fermataSprite = Coverage(64, (x, y) =>
        {
            float yy = y + 0.2f;
            float r = Mathf.Sqrt(x * x / 1.0f + yy * yy / 0.75f);
            bool arc = yy > 0 && r < 0.44f && r > 0.33f + 0.06f * (1 - Mathf.Abs(x) / 0.44f);
            bool dot = x * x + (y + 0.12f) * (y + 0.12f) < 0.008f;
            return arc || dot;
        }, "ff-fermata");

        static Sprite TriSprite() => _triSprite ? _triSprite : _triSprite = Coverage(48, (x, y) => y < 0.38f && y > -0.42f && Mathf.Abs(x) < (0.38f - y) * 0.55f, "ff-tri");

        // ------------------------------------------------------------ build

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

        static Image Plate(RectTransform parent, float corner, float alpha = 0.62f)
        {
            var fill = UiKit.Box(parent, "Plate", new Color(0.03f, 0.02f, 0.06f, UiKit.DarkAlpha(alpha)), UiKit.Pill, true, corner);
            UiKit.Stretch(fill.rectTransform);
            return fill;
        }

        static Image Rim(RectTransform parent, float corner, Color c)
        {
            var rim = UiKit.Box(parent, "Rim", c, UiKit.PillRing(2f * 24f / corner), true, corner);
            UiKit.Stretch(rim.rectTransform);
            return rim;
        }

        void BuildHud()
        {
            var go = new GameObject("FinaleHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
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
            BuildVignette(root);
            BuildTopBar(root);
            BuildStrip(_bar);
            BuildTempo(_bar);
            BuildHearts(root);
            BuildMeter(root);
            BuildMarkers(root);
            BuildCallout(root);
            BuildChips(root);
        }

        RectTransform _bar, _youPlate, _earMark;
        Image _vignette, _earMarkImg, _bpmEar, _bandEar;
        TextMeshProUGUI _bandText;
        static Sprite _earSprite, _vigSprite;

        /// One bar across the top: the programme on the left, the tempo gauge on the right (one plate, 60 px).
        void BuildTopBar(RectTransform root)
        {
            _bar = UiKit.Rect("TopBar", root);
            UiKit.Place(_bar, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(900, 60));
            Plate(_bar, 22, 0.66f);
            Rim(_bar, 22, new Color(1f, 0.85f, 0.5f, 0.2f));
            var div = UiKit.Box(_bar, "Div", new Color(1, 1, 1, 0.14f));
            Centre(div.rectTransform, new Vector2(-202, 0), new Vector2(2, 40));
        }

        /// A soft red frame over the whole screen for the last heart (and SURVIVE): it breathes slowly, never on the beat.
        void BuildVignette(RectTransform root)
        {
            if (!_vigSprite) _vigSprite = VignetteSprite();
            _vignette = UiKit.Box(root, "RedRoom", new Color(0.9f, 0.05f, 0.1f, 0), _vigSprite);
            UiKit.Stretch(_vignette.rectTransform);
            _vignette.enabled = false;
        }

        static Sprite VignetteSprite()
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "ff-vignette" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2 - 1, v = (y + 0.5f) / n * 2 - 1;
                    float d = Mathf.Sqrt(u * u * 0.8f + v * v * 1.1f);
                    float a = Mathf.Clamp01((d - 0.55f) / 0.6f); a = a * a;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100);
        }

        /// An ear: a C-shaped rim with the inner curl (y up, facing right).
        static Sprite EarSprite() => _earSprite ? _earSprite : _earSprite = Coverage(64, (x, y) =>
        {
            float ex = x / 0.3f, ey = (y - 0.04f) / 0.42f;
            float r = ex * ex + ey * ey;
            bool outer = r < 1 && r > 0.5f && !(x > 0.12f && y < -0.02f && y > -0.3f);
            float lobe = (x - 0.02f) * (x - 0.02f) / 0.012f + (y + 0.33f) * (y + 0.33f) / 0.01f;
            float ix = (x + 0.02f) / 0.13f, iy = (y + 0.02f) / 0.2f;
            float ri = ix * ix + iy * iy;
            bool inner = ri < 1 && ri > 0.4f && x < 0.08f;
            return outer || lobe < 1 || inner;
        }, "ff-ear");

        /// World-anchored marks: the YOU plate over the player's drummer, the ear over the maestro during a hush.
        void BuildMarkers(RectTransform root)
        {
            _youPlate = UiKit.Rect("You", root);
            Centre(_youPlate, Vector2.zero, new Vector2(88, 32));
            var f = UiKit.Box(_youPlate, "Fill", UiKit.Hex(0xffd93d), UiKit.Pill, true, 12); UiKit.Stretch(f.rectTransform);
            Rim(_youPlate, 12, new Color(1, 1, 1, 0.9f));
            var t = Label(_youPlate, "Text", "YOU", 11f, Ink, TextAlignmentOptions.Midline, 12);
            UiKit.Stretch(t.rectTransform);
            var tri = UiKit.Box(_youPlate, "Point", UiKit.Hex(0xffd93d), TriSprite());
            UiKit.Place(tri.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, -4), new Vector2(16, 10));   // hangs below the plate, never over the word
            tri.rectTransform.localRotation = Quaternion.Euler(0, 0, 180);
            _earMark = UiKit.Rect("EarMark", root);
            Centre(_earMark, Vector2.zero, new Vector2(64, 64));
            var g = UiKit.Box(_earMark, "Glow", new Color(CallEarly.r, CallEarly.g, CallEarly.b, 0.35f), UiKit.Soft);
            Centre(g.rectTransform, Vector2.zero, new Vector2(110, 110));
            _earMarkImg = UiKit.Box(_earMark, "Ear", CallEarly, EarSprite());
            Centre(_earMarkImg.rectTransform, Vector2.zero, new Vector2(56, 56));
            _earMark.gameObject.SetActive(false);
        }

        /// A world point as canvas reference pixels (centre origin), or false when it is behind the camera.
        bool CanvasPoint(Vector3 world, out Vector2 p)
        {
            p = Vector2.zero;
            var cam = Look.Rig?.Camera;
            if (!cam) return false;
            var sp = cam.WorldToScreenPoint(transform.TransformPoint(world));
            if (sp.z <= 0) return false;
            float k = RefH / Mathf.Max(1, Screen.height);
            p = new Vector2((sp.x - Screen.width * 0.5f) * k, (sp.y - Screen.height * 0.5f) * k);
            return true;
        }

        void BuildStrip(RectTransform root)
        {
            _strip = UiKit.Rect("Programme", root);
            Centre(_strip, new Vector2(-326, 0), new Vector2(240, 60));
            _levelChip = UiKit.Box(_strip, "LevelChip", Mint, UiKit.Pill, true, 10);
            Centre(_levelChip.rectTransform, new Vector2(-70, 12), new Vector2(84, 20));
            _levelText = Label(_levelChip.transform, "Level", "EASY", 10f, Ink, TextAlignmentOptions.Midline, 10);
            UiKit.Stretch(_levelText.rectTransform);
            _stripTitle = Label(_strip, "Title", "HYMN 1 / 3", 11.5f, UiKit.Cream, TextAlignmentOptions.MidlineLeft, 8);
            Centre(_stripTitle.rectTransform, new Vector2(52, 12), new Vector2(148, 20));
            _stripTitle.enableAutoSizing = true; _stripTitle.fontSizeMax = _stripTitle.fontSize; _stripTitle.fontSizeMin = _stripTitle.fontSize * 0.6f;
            _stripTitle.overflowMode = TextOverflowModes.Truncate;
            _pipRow = UiKit.Rect("Lines", _strip);
            Centre(_pipRow, new Vector2(0, -13), new Vector2(230, 20));
            for (int i = 0; i < _pips.Length; i++)
            {
                var rim = UiKit.Box(_pipRow, "PipRim" + i, new Color(1, 1, 1, 0.45f), UiKit.PillRing(2f * 24f / 7f), true, 7);
                Centre(rim.rectTransform, Vector2.zero, new Vector2(30, 14));
                var pip = UiKit.Box(rim.transform, "Pip", new Color(1, 1, 1, 0.1f), UiKit.Pill, true, 5);
                Centre(pip.rectTransform, Vector2.zero, new Vector2(24, 8));
                _pips[i] = pip; _pipRims[i] = rim;
                rim.gameObject.SetActive(false);
            }
        }

        void BuildTempo(RectTransform root)
        {
            _tempo = UiKit.Rect("Tempo", root);
            Centre(_tempo, new Vector2(150, 0), new Vector2(600, 60));
            // the bpm, big
            _bpmText = Label(_tempo, "Bpm", "96", 26f, Color.white, TextAlignmentOptions.MidlineRight, 0, TextStyle.Display);
            Centre(_bpmText.rectTransform, new Vector2(-226, 8), new Vector2(110, 40));
            _bpmText.rectTransform.pivot = new Vector2(1, 0.5f);
            _bpmText.rectTransform.anchoredPosition = new Vector2(-218, 8);
            _bpmUnit = Label(_tempo, "Unit", "BPM", 8f, UiKit.LabelInk, TextAlignmentOptions.MidlineRight, 14);
            Centre(_bpmUnit.rectTransform, new Vector2(-218, -19), new Vector2(80, 14));
            _bpmUnit.rectTransform.pivot = new Vector2(1, 0.5f);
            _bpmUnit.rectTransform.anchoredPosition = new Vector2(-218, -19);
            _bpmEar = UiKit.Box(_tempo, "BpmEar", CallEarly, EarSprite());
            Centre(_bpmEar.rectTransform, new Vector2(-240, 6), new Vector2(38, 38));
            _bpmEar.enabled = false;
            // what the tempo is doing
            var div = UiKit.Box(_tempo, "Div", new Color(1, 1, 1, 0.14f));
            Centre(div.rectTransform, new Vector2(-204, 0), new Vector2(2, 40));
            _trendIcon = UiKit.Box(_tempo, "TrendIcon", Color.white, ArrowSprite());
            Centre(_trendIcon.rectTransform, new Vector2(-176, 6), new Vector2(34, 34));
            _trendText = Label(_tempo, "Trend", "STEADY", 12.5f, Color.white, TextAlignmentOptions.MidlineLeft, 6, TextStyle.Display);
            _trendText.rectTransform.anchorMin = _trendText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _trendText.rectTransform.pivot = new Vector2(0, 0.5f);
            _trendText.rectTransform.anchoredPosition = new Vector2(-152, 8); _trendText.rectTransform.sizeDelta = new Vector2(150, 26);
            _trendSub = Label(_tempo, "TrendSub", "A TEMPO", 7.5f, UiKit.LabelInk, TextAlignmentOptions.MidlineLeft, 10);
            _trendSub.rectTransform.anchorMin = _trendSub.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _trendSub.rectTransform.pivot = new Vector2(0, 0.5f);
            _trendSub.rectTransform.anchoredPosition = new Vector2(-152, -16); _trendSub.rectTransform.sizeDelta = new Vector2(150, 14);
            // the ribbon of the curve ahead
            var rib = UiKit.Rect("Ribbon", _tempo);
            Centre(rib, new Vector2(140, 0), new Vector2(290, 48));
            var back = UiKit.Box(rib, "Back", new Color(0, 0, 0, 0.35f), UiKit.Pill, true, 10);
            UiKit.Stretch(back.rectTransform);
            var rg = new GameObject("Curve", typeof(RectTransform), typeof(CanvasRenderer), typeof(TempoRibbon));
            rg.transform.SetParent(rib, false);
            _ribbon = rg.GetComponent<TempoRibbon>();
            _ribbon.raycastTarget = false;
            var rr = (RectTransform)rg.transform;
            rr.anchorMin = Vector2.zero; rr.anchorMax = Vector2.one; rr.offsetMin = new Vector2(6, 6); rr.offsetMax = new Vector2(-6, -6);
            // the BY EAR band's label: an ear and the words, placed over the band while it is in view
            _bandEar = UiKit.Box(rr, "BandEar", new Color(0.75f, 0.92f, 1f, 1), EarSprite());
            Centre(_bandEar.rectTransform, Vector2.zero, new Vector2(24, 24));
            _bandText = Label(rr, "BandText", "BY EAR", 7.5f, new Color(0.85f, 0.95f, 1f, 1), TextAlignmentOptions.MidlineLeft, 10);
            Centre(_bandText.rectTransform, Vector2.zero, new Vector2(80, 14));
            _bandText.rectTransform.pivot = new Vector2(0, 0.5f);
            _bandEar.gameObject.SetActive(false); _bandText.gameObject.SetActive(false);
            for (int i = 0; i < _fermataMarks.Length; i++)
            {
                _fermataMarks[i] = UiKit.Box(rr, "Fermata" + i, Color.white, FermataSprite());
                Centre(_fermataMarks[i].rectTransform, Vector2.zero, new Vector2(22, 22));
                _fermataMarks[i].gameObject.SetActive(false);
            }
        }

        void BuildHearts(RectTransform root)
        {
            var row = UiKit.Rect("Hearts", root);
            UiKit.Place(row, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -18), new Vector2(150, 44));
            for (int i = 0; i < 3; i++)
            {
                var h = new HeartView();
                h.Rt = UiKit.Rect("Heart" + i, row);
                h.Rt.anchorMin = h.Rt.anchorMax = h.Rt.pivot = new Vector2(1, 0.5f);
                h.Rt.anchoredPosition = new Vector2(-(2 - i) * 46, 0); h.Rt.sizeDelta = new Vector2(40, 40);
                h.Glow = UiKit.Box(h.Rt, "Glow", new Color(1, 0.3f, 0.35f, 0), UiKit.Soft);
                Centre(h.Glow.rectTransform, Vector2.zero, new Vector2(84, 84));
                h.Back = UiKit.Box(h.Rt, "Back", new Color(0.04f, 0.02f, 0.06f, 0.75f), HeartSprite());
                Centre(h.Back.rectTransform, Vector2.zero, new Vector2(46, 46));
                h.Fill = UiKit.Box(h.Rt, "Fill", Color.white, HeartSprite());
                Centre(h.Fill.rectTransform, Vector2.zero, new Vector2(38, 38));
                h.Fill.gameObject.AddComponent<UiGradient>().Set(UiKit.Hex(0xff8f9a), UiKit.Hex(0xe0203c));
                h.Shard = UiKit.Box(h.Rt, "Shard", UiKit.Hex(0xff5d73), HeartSprite());
                Centre(h.Shard.rectTransform, Vector2.zero, new Vector2(38, 38));
                h.Shard.gameObject.SetActive(false);
                _hearts[i] = h;
            }
            _surviveTag = UiKit.Rect("Survive", root);
            UiKit.Place(_surviveTag, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -66), new Vector2(150, 26));
            Plate(_surviveTag, 12, 0.8f).color = new Color(0.55f, 0.02f, 0.08f, 0.9f);
            Rim(_surviveTag, 12, new Color(1f, 0.5f, 0.5f, 0.6f));
            _surviveText = Label(_surviveTag, "Text", "SURVIVE", 10f, Color.white, TextAlignmentOptions.Midline, 14);
            UiKit.Stretch(_surviveText.rectTransform);
            _surviveTag.gameObject.SetActive(false);
        }

        void BuildMeter(RectTransform root)
        {
            _meter = UiKit.Rect("Meter", root);
            UiKit.Place(_meter, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(560, 50));
            _meterPlate = Plate(_meter, 24, 0.66f);
            _meterRim = Rim(_meter, 24, new Color(1, 1, 1, 0.14f));
            _meterTitle = Label(_meter, "Title", "TAP ON EVERY BEAT", 8.5f, UiKit.Cream, TextAlignmentOptions.Midline, 12);
            Centre(_meterTitle.rectTransform, new Vector2(0, 30), new Vector2(560, 16));
            _meterTitle.gameObject.SetActive(false);   // the state words ride on the lean line under the track
            _earlyLbl = Label(_meter, "Early", "EARLY", 9f, CallEarly, TextAlignmentOptions.MidlineRight, 10);
            Centre(_earlyLbl.rectTransform, new Vector2(-TrackW / 2 - 12, 6), new Vector2(80, 16));
            _earlyLbl.rectTransform.pivot = new Vector2(1, 0.5f); _earlyLbl.rectTransform.anchoredPosition = new Vector2(-TrackW / 2 - 12, 8);
            _lateLbl = Label(_meter, "Late", "LATE", 9f, CallLate, TextAlignmentOptions.MidlineLeft, 10);
            Centre(_lateLbl.rectTransform, new Vector2(TrackW / 2 + 12, 6), new Vector2(80, 16));
            _lateLbl.rectTransform.pivot = new Vector2(0, 0.5f); _lateLbl.rectTransform.anchoredPosition = new Vector2(TrackW / 2 + 12, 8);

            _track = UiKit.Rect("Track", _meter);
            Centre(_track, new Vector2(0, 8), new Vector2(TrackW, TrackH));
            var tb = UiKit.Box(_track, "Back", new Color(0.02f, 0.01f, 0.04f, 0.85f), UiKit.Pill, true, 9);
            UiKit.Stretch(tb.rectTransform);
            var el = UiKit.Box(_track, "EarlySide", new Color(CallEarly.r, CallEarly.g, CallEarly.b, 0.28f), UiKit.Pill, true, 9);
            el.rectTransform.anchorMin = new Vector2(0, 0); el.rectTransform.anchorMax = new Vector2(0.5f, 1); el.rectTransform.offsetMin = new Vector2(2, 2); el.rectTransform.offsetMax = new Vector2(0, -2);
            el.gameObject.AddComponent<UiGradient>();
            var lt = UiKit.Box(_track, "LateSide", new Color(CallLate.r, CallLate.g, CallLate.b, 0.28f), UiKit.Pill, true, 9);
            lt.rectTransform.anchorMin = new Vector2(0.5f, 0); lt.rectTransform.anchorMax = new Vector2(1, 1); lt.rectTransform.offsetMin = new Vector2(0, 2); lt.rectTransform.offsetMax = new Vector2(-2, -2);
            float great = (float)(Windows.GreatMs / Windows.GoodMs), perfect = (float)(Windows.PerfectMs / Windows.GoodMs);
            var gz = UiKit.Box(_track, "Great", new Color(1, 1, 1, 0.12f));
            Centre(gz.rectTransform, Vector2.zero, new Vector2(TrackW * great, TrackH - 4));
            _onZone = UiKit.Box(_track, "On", new Color(CallOn.r, CallOn.g, CallOn.b, 0.75f), UiKit.Pill, true, 6);
            Centre(_onZone.rectTransform, Vector2.zero, new Vector2(TrackW * perfect, TrackH - 2));
            var mid = UiKit.Box(_track, "Mid", new Color(1, 1, 1, 0.9f));
            Centre(mid.rectTransform, Vector2.zero, new Vector2(2, TrackH + 10));
            for (int i = 0; i < _mticks.Length; i++)
            {
                var t = UiKit.Box(_track, "Tick" + i, Color.white, UiKit.Pill, true, 2);
                Centre(t.rectTransform, Vector2.zero, new Vector2(4, TrackH + 8));
                _mticks[i] = t.rectTransform; _tickImg[i] = t;
                t.gameObject.SetActive(false);
            }
            var dia = UiKit.Box(_track, "Diamond", Color.white, UiKit.Pill, true, 4);
            Centre(dia.rectTransform, Vector2.zero, new Vector2(15, 15));
            dia.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            _diamond = dia.rectTransform; _diamondImg = dia;
            dia.gameObject.SetActive(false);
            // the lean needle under the track and its words
            var nd = UiKit.Box(_track, "Needle", Color.white, TriSprite());
            Centre(nd.rectTransform, new Vector2(0, -TrackH / 2 - 6), new Vector2(14, 12));
            _needle = nd.rectTransform; _needleImg = nd;
            _leanText = Label(_meter, "Lean", "", 8.5f, UiKit.LabelInk, TextAlignmentOptions.Midline, 10);
            Centre(_leanText.rectTransform, new Vector2(0, -15), new Vector2(520, 14));
        }

        void BuildCallout(RectTransform root)
        {
            _callout = UiKit.Rect("Callout", root);
            Centre(_callout, new Vector2(330, 0), new Vector2(200, 58));   // anchored each frame over the player's drummer (above the YOU plate)
            _callPlate = Plate(_callout, 24, 0.7f);
            _callRim = Rim(_callout, 24, Color.white);
            _callText = Label(_callout, "Word", "", 22f, Color.white, TextAlignmentOptions.Midline, 4, TextStyle.Display);
            Centre(_callText.rectTransform, new Vector2(0, 2), new Vector2(400, 44));
            _callMs = Label(_callout, "Ms", "", 18f, UiKit.LabelInk, TextAlignmentOptions.Midline, 8, TextStyle.Display);
            // the ms read-out lives INSIDE the plate, under the word (never on the YOU plate below it)
            _callMs.rectTransform.anchorMin = _callMs.rectTransform.anchorMax = new Vector2(0.5f, 0);
            _callMs.rectTransform.pivot = new Vector2(0.5f, 0);
            _callMs.rectTransform.anchoredPosition = new Vector2(0, 5); _callMs.rectTransform.sizeDelta = new Vector2(200, 26);
            _callout.gameObject.SetActive(false);
        }

        void BuildChips(RectTransform root)
        {
            _chipRow = UiKit.Rect("Rivals", root);
            UiKit.Place(_chipRow, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -80), new Vector2(600, 30));
            _chipGroup = _chipRow.gameObject.AddComponent<CanvasGroup>();
            _chipGroup.alpha = 0;
            for (int i = 0; i < 4; i++)
            {
                var rt = UiKit.Rect("Chip" + i, _chipRow);
                Centre(rt, Vector2.zero, new Vector2(140, 28));
                _chipFill[i] = UiKit.Box(rt, "Fill", new Color(0.04f, 0.03f, 0.08f, UiKit.DarkAlpha(0.72f)), UiKit.Pill, true, 14);
                UiKit.Stretch(_chipFill[i].rectTransform);
                _chipDot[i] = UiKit.Box(rt, "Dot", Color.white, UiKit.Disc);
                Centre(_chipDot[i].rectTransform, new Vector2(-54, 0), new Vector2(14, 14));
                _chipText[i] = Label(rt, "Text", "", 9f, Color.white, TextAlignmentOptions.MidlineLeft, 6);
                _chipText[i].rectTransform.anchorMin = _chipText[i].rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                _chipText[i].rectTransform.pivot = new Vector2(0, 0.5f);
                _chipText[i].rectTransform.anchoredPosition = new Vector2(-42, 0); _chipText[i].rectTransform.sizeDelta = new Vector2(110, 18);
                _chips[i] = rt;
                rt.gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------ events

        void BeginHud()
        {
            Ui.ShowScoreboard();
            Ui.ShowAccuracy(false);   // the score IS the accuracy here; the corner belongs to the hearts
            Ui.Banner(Title, "TAP EVERY BEAT - THE TEMPO WILL MOVE", Gold, 1.8f, plate: true, y: BannerY, scale: 0.7f);
            int n = _round.Slots.Count;
            _bpmLo = new double[n]; _bpmHi = new double[n]; _fermBpm = new double[n][];
            for (int k = 0; k < n; k++)
            {
                var s = _round.Slots[k];
                int bc = s.Curve.BeatCount;
                _fermBpm[k] = new double[bc + 1];
                double at = 0;
                foreach (var g in s.Curve.Segments)
                {
                    int i0 = (int)System.Math.Round(at);
                    if (g.Kind == TempoKind.Fermata && i0 < bc) _fermBpm[k][i0] = g.Bpm0;
                    at += g.Beats;
                }
                double lo = double.MaxValue, hi = double.MinValue;
                for (int i = 0; i <= bc * 2; i++)
                {
                    double v = DisplayBpm(k, i * 0.5);
                    lo = System.Math.Min(lo, v); hi = System.Math.Max(hi, v);
                }
                double pad = System.Math.Max(4, (hi - lo) * 0.18);
                _bpmLo[k] = lo - pad; _bpmHi[k] = hi + pad;
            }
        }

        /// The tempo the gauge draws at `beat` of slot k: a fermata reads at the tempo it holds, not its stretched beat.
        double DisplayBpm(int k, double beat)
        {
            var s = _round.Slots[k];
            int i = (int)System.Math.Floor(beat);
            if (i >= 0 && i < _fermBpm[k].Length && _fermBpm[k][i] > 0) return _fermBpm[k][i];
            return s.Curve.BpmAt(System.Math.Max(0, System.Math.Min(beat, s.Curve.BeatCount - 1e-6)));
        }

        void HudExcerpt(TempoSlot slot)
        {
            _stripPulse = 1;
            if (slot.Index > 0)
                Ui.Banner($"HYMN {slot.Index + 1} / {TempoRound.ExcerptCount}", slot.Level == HymnLevel.Medium ? "MEDIUM - UP AND DOWN" : "HARD - JUMPS AND HOLDS", LevelColor(slot.Level), 1.4f, plate: true, y: BannerY, scale: 0.7f);
        }

        void HudTapped(TempoBeat b)
        {
            float good = (float)BeatWindow.GoodMs(b.Length);
            float x = Mathf.Clamp((float)b.ErrMs / good, -1, 1);
            var c = b.Call == TapCall.On ? CallOn : b.Call == TapCall.Early ? CallEarly : CallLate;
            AddTick(x, c);
            _leanMs = _anyTap ? Mathf.Lerp(_leanMs, (float)b.ErrMs, 0.2f) : (float)b.ErrMs;
            _lean01 = _anyTap ? Mathf.Lerp(_lean01, x, 0.2f) : x;
            _anyTap = true;
            int ms = Mathf.RoundToInt((float)b.ErrMs);
            Callout(b.Call == TapCall.On ? "ON!" : b.Call == TapCall.Early ? "EARLY" : "LATE", c, b.Call == TapCall.On ? (ms == 0 ? "DEAD ON" : (ms > 0 ? "+" : "") + ms + " MS") : (ms > 0 ? "+" : "") + ms + " MS");
        }

        void HudMissed(TempoBeat b)
        {
            Callout("MISS", CallMiss, "");
        }

        void Callout(string word, Color c, string ms)
        {
            _callout.gameObject.SetActive(true);
            UiKit.SetFill(_callText, c);
            _callText.text = word;
            _callMs.text = ms;
            UiKit.SetFill(_callMs, c);
            _callText.ForceMeshUpdate();
            float w = Mathf.Max(150, _callText.preferredWidth + 56);
            bool hasMs = !string.IsNullOrEmpty(ms);
            _callout.sizeDelta = new Vector2(w, hasMs ? 84 : 58);
            _callText.rectTransform.anchoredPosition = new Vector2(0, hasMs ? 15 : 2);
            _callRim.color = new Color(c.r, c.g, c.b, 0.85f);
            _callPlate.color = new Color(c.r * 0.16f, c.g * 0.16f, c.b * 0.16f, 0.9f);
            _callLife = 0.75f;
            _callPop = 1;
        }

        void AddTick(float x, Color c)
        {
            int i = _tickNext; _tickNext = (_tickNext + 1) % _mticks.Length;
            _tickX[i] = x; _tickAge[i] = 0; _tickCol[i] = c;
            _mticks[i].gameObject.SetActive(true);
            _diamondX = x; _diamondPop = 1;
            _diamond.gameObject.SetActive(true);
            _diamondImg.color = c;
        }

        void HudLine(TempoLine line)
        {
            string what; Color c;
            if (line.HeartDelta < 0) { what = "HEART LOST"; c = CallMiss; }
            else if (line.Misses == 0 && line.Top == line.Count) { what = line.HeartDelta > 0 ? "HEART BACK" : "LINE CLEAN"; c = Gold; }
            else if (line.Misses == 0) { what = "LINE KEPT"; c = Mint; }
            else { what = $"{line.Misses} MISSED"; c = Amber; }
            _toast = what; _toastColor = c; _toastLife = 1.9f;
            _stripTitle.text = what;
            UiKit.SetFill(_stripTitle, c, false);
            _stripTitleCode = -2;
            ShowStandings();
        }

        int _stripTitleCode = -1;

        float _pulseA; Color _pulseCol;

        void HudHeart(int delta, int left)
        {
            _pulseA = 1; _pulseCol = delta < 0 ? new Color(0.9f, 0.05f, 0.08f) : new Color(1f, 0.8f, 0.3f);
            if (delta < 0)
            {
                int i = Mathf.Clamp(left, 0, 2);   // the heart that broke
                var h = _hearts[i];
                h.Full = false; h.Break = 1; h.Shard01 = 0.001f; h.Shard.gameObject.SetActive(true);
            }
            else
            {
                int i = Mathf.Clamp(left - 1, 0, 2);
                var h = _hearts[i];
                h.Full = true; h.Pop = 1;
            }
        }

        void HudFinale()
        {
            string tier = _round.Tier;
            string head = _round.Survived ? "YOU SURVIVED" : tier == "METRONOME" ? "BRAVISSIMO!" : "BRAVO!";
            string sub = tier == "METRONOME" ? "NOT ONE BEAT MISSED" : _round.Survived ? "THE RANK STOPS AT B" : $"{_round.Miss} BEATS MISSED";
            Ui.Banner(head, sub, _round.Survived ? CallMiss : Gold, 2.9f, plate: true, y: BannerY, scale: 0.75f);
            _finaleK = 0.001f;
            _meterTitle.text = "YOUR AVERAGE OVER THE ROUND";
            _leanMs = (float)_round.BiasMs;
            float good = (float)BeatWindow.GoodMs(0.6);
            _lean01 = Mathf.Clamp(_leanMs / good, -1, 1);
            _leanShown = int.MinValue;
            ShowStandings();
        }

        void ShowStandings()
        {
            if (_round.CpuCount == 0 || _ids == null) return;
            int n = _round.CpuCount + 1;
            var pts = _standPts;
            for (int i = 0; i < 4; i++) pts[i] = 0;
            pts[0] = _round.Raw;
            foreach (var s in _round.Slots)
                for (int c = 0; c < s.CpuRolls.Length && c < 3; c++)
                {
                    var r = s.CpuRolls[c];
                    for (int i = 0; i < s.Beats.Count && i < r.Verdicts.Length; i++)
                        if (s.Beats[i].Judged || _round.Done) pts[c + 1] += Scoring.PointsFor(r.Verdicts[i]);
                }
            for (int i = 0; i < 4; i++) _standOrder[i] = i;
            for (int a = 0; a < n; a++)
                for (int b = a + 1; b < n; b++)
                    if (pts[_standOrder[b]] > pts[_standOrder[a]] + 1e-9) { int t = _standOrder[a]; _standOrder[a] = _standOrder[b]; _standOrder[b] = t; }
            int shown = Mathf.Min(4, n);
            float w = 140, gap = 10, x0 = -(shown * w + (shown - 1) * gap) / 2 + w / 2;
            for (int c = 0; c < 4; c++)
            {
                bool on = c < shown;
                _chips[c].gameObject.SetActive(on);
                if (!on) continue;
                int who = _standOrder[c];
                _chips[c].anchoredPosition = new Vector2(x0 + c * (w + gap), 0);
                string name = who == 0 ? "YOU" : CpuName(who);
                _chipText[c].text = $"{(c == 0 ? "1ST" : c == 1 ? "2ND" : c == 2 ? "3RD" : "4TH")}  {name}";
                _chipDot[c].color = SeatColorFor(who);
                _chipFill[c].color = who == 0 ? new Color(0.22f, 0.16f, 0.06f, UiKit.DarkAlpha(0.82f)) : new Color(0.04f, 0.03f, 0.08f, UiKit.DarkAlpha(0.72f));
            }
            _chipLife = _round.Done ? 3.2f : 2.4f;
        }

        readonly double[] _standPts = new double[4];
        readonly int[] _standOrder = new int[4];

        string CpuName(int resultIndex)
        {
            if (Host.Lineup == null) return "CPU " + resultIndex;
            int k = 0;
            foreach (var p in Host.Lineup)
                if (p.IsCpu && ++k == resultIndex) return string.IsNullOrEmpty(p.Name) ? "CPU " + resultIndex : p.Name.ToUpperInvariant();
            return "CPU " + resultIndex;
        }

        internal double RibbonBpm(int k, double b) => DisplayBpm(k, b);
        internal bool RibbonJumpAt(int k, int i) => i >= 0 && i < _jumpAt[k].Length && _jumpAt[k][i];
        internal bool RibbonBarStart(int k, int i) => i >= 0 && i < _round.Slots[k].Beats.Count && _round.Slots[k].Beats[i].BarPosition == 0;

        static Color LevelColor(HymnLevel l) => l == HymnLevel.Easy ? UiKit.Hex(0xffd27a) : l == HymnLevel.Medium ? UiKit.Hex(0x7ff0e0) : UiKit.Hex(0xff5a4a);

        /// Banners sit above the maestro's hair (screen y 0.2 = 144 px), under the top bar.
        const float BannerY = 0.215f;

        // ------------------------------------------------------------ per frame

        void UpdateHud(float dt)
        {
            if (_canvas == null) return;
            float real = Time.unscaledDeltaTime;
            bool reduce = Settings.ReduceMotion;
            bool live = _round.Started && !_resultOut;
            _hudAlpha = _resultOut ? 0f : Damp(_hudAlpha, live ? 1 : 0, live ? 5 : 9, real);   // gone the frame the result card opens
            _hudGroup.alpha = _hudAlpha;
            var s = Slot;
            if (s == null) return;
            int k = s.Index;
            bool hidden = PulseHidden && _playing && !_round.Done;
            float hh = _hush;

            // ---- programme strip
            if (k != _stripSlot)
            {
                _stripSlot = k;
                _toastLife = 0;   // a new excerpt: the last line's verdict belongs to the one before
                _levelText.text = s.Level.ToString().ToUpperInvariant();
                _pipCount = Mathf.Min(_pips.Length, s.Lines.Count);
                float pw = 30, pg = 8, x0 = -(_pipCount * pw + (_pipCount - 1) * pg) / 2 + pw / 2;
                for (int i = 0; i < _pips.Length; i++)
                {
                    bool on = i < _pipCount;
                    _pipRims[i].gameObject.SetActive(on);
                    if (on) _pipRims[i].rectTransform.anchoredPosition = new Vector2(x0 + i * (pw + pg), 0);
                }
                _stripTitleCode = -1;
            }
            _toastLife = Mathf.Max(0, _toastLife - real);
            if (_toastLife <= 0 && _stripTitleCode != k)
            {
                _stripTitleCode = k;
                _stripTitle.text = $"HYMN {k + 1} / {TempoRound.ExcerptCount}";
                UiKit.SetFill(_stripTitle, UiKit.Cream, false);
            }
            _stripPulse = Mathf.Max(0, _stripPulse - real * 1.3f);
            float sp = reduce ? 0 : Mathf.Sin(Mathf.Clamp01(1 - _stripPulse) * Mathf.PI) * _stripPulse;
            _strip.localScale = Vector3.one * (1 + sp * 0.1f);
            _levelChip.color = Color.Lerp(LevelColor(s.Level), Color.white, _stripPulse * 0.5f);
            int curLine = Beat >= 0 ? s.Excerpt.LineOf(Mathf.Clamp((int)System.Math.Floor(Beat), 0, s.Beats.Count - 1)) : -1;
            for (int i = 0; i < _pipCount; i++)
            {
                var line = s.Lines[i];
                Color c; float rimA;
                if (line.Closed)
                {
                    c = line.HeartDelta < 0 ? CallMiss : line.Misses == 0 && line.Top == line.Count ? Gold : line.Misses == 0 ? Mint : Amber;
                    rimA = 0.9f;
                }
                else if (i == curLine) { c = new Color(1, 1, 1, 0.85f); rimA = 1; }
                else { c = new Color(1, 1, 1, 0.1f); rimA = 0.35f; }
                _pips[i].color = c;
                _pipRims[i].color = new Color(1, 1, 1, rimA);
            }

            // ---- tempo gauge
            double beat = Beat;
            int bi = (int)System.Math.Floor(beat);
            int bpm = Mathf.RoundToInt((float)DisplayBpm(k, System.Math.Max(0, beat)));
            int code = TrendCode(k, beat, out int inBeats);
            if (hidden) code = 99;
            if (!_playing || _round.Done) code = 98;
            int shownBpm = hidden ? -1 : bpm;
            if (shownBpm != _bpmShown)
            {
                _bpmShown = shownBpm;
                _bpmText.text = shownBpm < 0 ? "" : shownBpm.ToString();   // hidden: an ear, not a blank number
                _bpmEar.enabled = shownBpm < 0;
                _bpmUnit.text = shownBpm < 0 ? "LISTEN" : "BPM";
            }
            if (code * 10 + inBeats != _trendShown)
            {
                _trendShown = code * 10 + inBeats;
                ApplyTrend(code, inBeats);
            }
            _bpmText.color = Color.Lerp(Color.white, CallEarly, hh * 0.7f);
            _ribbon.Set(s.Curve, k, beat, _bpmLo[k], _bpmHi[k], s, this);
            // fermata signs on the ribbon (only where the curve is drawn)
            int fm = 0;
            if (_fermBpm != null)
                for (int i = Mathf.Max(0, bi - 2); i < Mathf.Min(s.Beats.Count, bi + 15) && fm < _fermataMarks.Length; i++)
                {
                    if (_fermBpm[k][i] <= 0 || s.Beats[i].Hidden) continue;
                    var p = _ribbon.PointAt(i + 0.5, DisplayBpm(k, i));
                    var m = _fermataMarks[fm++];
                    if (!m.gameObject.activeSelf) m.gameObject.SetActive(true);
                    m.rectTransform.anchoredPosition = p + new Vector2(0, 14);
                    m.color = UiKit.Gold;
                }
            for (int i = fm; i < _fermataMarks.Length; i++) if (_fermataMarks[i].gameObject.activeSelf) _fermataMarks[i].gameObject.SetActive(false);
            // the BY EAR band in view gets its ear and its words (at the band's visible start)
            bool band = false;
            foreach (var h in s.HiddenStretches)
            {
                double b0 = System.Math.Max(beat - 2, h.First), b1 = System.Math.Min(beat + 14, h.Last + 1);
                if (b1 - b0 < 1.2) continue;
                var p0 = _ribbon.PointAt(b0, 0);
                _bandEar.rectTransform.anchoredPosition = new Vector2(p0.x + 16, 0);
                _bandText.rectTransform.anchoredPosition = new Vector2(p0.x + 30, 0);
                _bandText.gameObject.SetActive(_ribbon.PointAt(b1, 0).x - p0.x > 90);
                band = true;
                break;
            }
            if (_bandEar.gameObject.activeSelf != band) _bandEar.gameObject.SetActive(band);
            if (!band && _bandText.gameObject.activeSelf) _bandText.gameObject.SetActive(false);
            // ---- hearts
            int hearts = Hearts;
            for (int i = 0; i < 3; i++)
            {
                var h = _hearts[i];
                bool full = i < hearts;
                if (full != h.Full && h.Break <= 0 && h.Pop <= 0) h.Full = full;
                h.Pop = Mathf.Max(0, h.Pop - real * 1.3f);
                h.Break = Mathf.Max(0, h.Break - real * 1.8f);
                float scale = 1;
                if (h.Pop > 0) scale = reduce ? 1 : 1 + 0.45f * Mathf.Sin(h.Pop * Mathf.PI) * h.Pop;
                if (h.Break > 0) scale = reduce ? 1 : 1 + 0.35f * h.Break;
                bool last = hearts == 1 && i == 0 && _playing && !_round.Done;
                if (last && !reduce) scale *= 1 + 0.09f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(_time * 7.2f)), 6);
                h.Rt.localScale = Vector3.one * scale;
                h.Fill.enabled = h.Full || h.Break > 0.5f;
                h.Fill.color = h.Break > 0 ? Color.Lerp(Color.white, new Color(1, 1, 1, 0.3f), 1 - h.Break) : Color.white;
                h.Back.color = h.Full ? new Color(0.04f, 0.02f, 0.06f, 0.8f) : new Color(0.25f, 0.2f, 0.3f, 0.55f);
                h.Glow.color = h.Pop > 0 && h.Break <= 0
                    ? new Color(1f, 0.85f, 0.45f, Mathf.Min(1, h.Pop * 1.6f))   // a heart won back: a gold burst
                    : new Color(1f, 0.25f, 0.3f, last ? 0.35f + 0.25f * Mathf.Sin(_time * 3.6f) : h.Pop * 0.6f + h.Break * 0.9f);
                h.Glow.rectTransform.localScale = Vector3.one * (1 + (reduce ? 0 : 1.6f * (h.Pop + h.Break)));
                if (h.Shard01 > 0)
                {
                    h.Shard01 += real * 1.6f;
                    float t = h.Shard01;
                    h.Shard.rectTransform.anchoredPosition = new Vector2(t * 22, -t * t * 90);
                    h.Shard.rectTransform.localRotation = Quaternion.Euler(0, 0, -t * 120);
                    h.Shard.color = new Color(1f, 0.36f, 0.42f, Mathf.Clamp01(1 - t));
                    if (t >= 1) { h.Shard01 = 0; h.Shard.gameObject.SetActive(false); }
                }
            }
            bool survive = _round.Survived && !_resultOut;
            _surviveShow = Damp(_surviveShow, survive ? 1 : 0, 6, real);
            bool sOn = _surviveShow > 0.02f;
            if (_surviveTag.gameObject.activeSelf != sOn) _surviveTag.gameObject.SetActive(sOn);
            if (sOn) _surviveTag.localScale = Vector3.one * (_surviveShow * (1 + (reduce ? 0 : 0.04f * Mathf.Sin(_time * 3f))));

            // ---- meter
            for (int i = 0; i < _mticks.Length; i++)
            {
                if (!_mticks[i].gameObject.activeSelf) continue;
                _tickAge[i] += real;
                float a = Mathf.Clamp01(1 - _tickAge[i] / 4f);
                if (a <= 0) { _mticks[i].gameObject.SetActive(false); continue; }
                _mticks[i].anchoredPosition = new Vector2(_tickX[i] * TrackW / 2, 0);
                var c = _tickCol[i]; c.a = 0.85f * a;
                _tickImg[i].color = c;
            }
            _diamondPop = Mathf.Max(0, _diamondPop - real * 3f);
            if (_diamond.gameObject.activeSelf)
            {
                _diamond.anchoredPosition = new Vector2(_diamondX * TrackW / 2, 0);
                _diamond.localScale = Vector3.one * (1 + (reduce ? 0 : _diamondPop * 0.6f));
            }
            bool showLean = _anyTap || _finaleK > 0;
            _needleImg.enabled = showLean;
            _needle.anchoredPosition = new Vector2(Damp(_needle.anchoredPosition.x, _lean01 * TrackW / 2, 8, real), -TrackH / 2 - 6);
            _needleImg.color = Mathf.Abs(_lean01) < 0.12f ? CallOn : _lean01 < 0 ? CallEarly : CallLate;
            int lean = Mathf.RoundToInt(_leanMs);
            int state = _finaleK > 0 ? 3 : hidden ? 2 : Beat < 0 && !_anyTap ? 1 : 0;
            int leanCode = (showLean ? lean : 100000) * 10 + state;
            if (leanCode != _leanShown)
            {
                _leanShown = leanCode;
                int a = Mathf.Abs(lean);
                string words = !showLean ? "" : Mathf.Abs(_lean01) < 0.12f ? $"CENTRED {(lean > 0 ? "+" : "")}{lean} MS" : lean < 0 ? $"EARLY {a} MS" : $"LATE {a} MS";
                _leanText.text = state == 3 ? (a == 0 ? "YOUR AVERAGE: DEAD ON THE BEAT" : (lean < 0 ? $"YOUR AVERAGE: AHEAD BY {a} MS" : $"YOUR AVERAGE: BEHIND BY {a} MS"))
                    : state == 2 ? "BY EAR - KEEP THE BEAT GOING" + (showLean ? "   LEAN " + words : "")
                    : state == 1 ? "TAP ON EVERY BEAT"
                    : "LEAN " + words;
                UiKit.SetFill(_leanText, state == 2 ? CallEarly : UiKit.LabelInk, false);
            }
            _meterRim.color = Color.Lerp(new Color(1, 1, 1, 0.14f), new Color(CallEarly.r, CallEarly.g, CallEarly.b, 0.6f), hh);
            if (_finaleK > 0)
            {
                _finaleK = Mathf.Min(1, _finaleK + real * 1.6f);
                float e = reduce ? 1 : UiKit.EaseOutCubic(_finaleK);
                _meterRim.color = Color.Lerp(new Color(1, 1, 1, 0.14f), new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.8f), e);
                _diamond.gameObject.SetActive(false);
            }

            // ---- world marks: YOU over the player's drummer, the callout above it, the ear over the maestro in a hush
            if (CanvasPoint(SeatAt[0] + new Vector3(0, 2.7f, 0), out var yp))
            {
                _youPlate.anchoredPosition = yp + new Vector2(0, reduce ? 0 : Mathf.Sin(_time * 2f) * 2f);
                _callout.anchoredPosition = yp + new Vector2(0, 98);
            }
            _youPlate.gameObject.SetActive(!_resultOut && _finaleK <= 0);
            bool ear = hh > 0.05f;
            if (_earMark.gameObject.activeSelf != ear) _earMark.gameObject.SetActive(ear);
            if (ear && CanvasPoint(PodiumAt + new Vector3(0.9f, PodiumH + 3.25f, 0), out var ep))
            {
                _earMark.anchoredPosition = ep;
                float br = reduce ? 0.85f : 0.7f + 0.3f * Mathf.Sin(_time * 3.1f);   // breathes on its own clock, not the beat
                _earMarkImg.color = new Color(CallEarly.r, CallEarly.g, CallEarly.b, hh * br);
                _earMark.localScale = Vector3.one * (0.85f + 0.15f * hh);
            }
            // the red room: a slow red frame on the last heart, deeper with none
            bool red = _playing && !_round.Done && Hearts <= 1;
            float vgoal = !red ? 0 : (Hearts == 0 ? 0.3f : 0.22f) + (reduce ? 0 : 0.06f * Mathf.Sin(_time * 2.4f));   // an edge frame only: the baton and the level palette stay readable
            _vigA = Damp(_vigA, vgoal, 3, real);
            // heart moments: a quick frame pulse (red on a loss, gold on a gain), on top of the red room
            _pulseA = Mathf.Max(0, _pulseA - real * (reduce ? 2.5f : 1.8f));
            float pa = _pulseA * (reduce ? 0.5f : 0.8f);
            _vignette.enabled = _vigA > 0.01f || pa > 0.01f;
            if (_vignette.enabled)
            {
                var vc = new Color(0.85f, 0.04f, 0.08f, _vigA);
                _vignette.color = pa > _vigA ? new Color(_pulseCol.r, _pulseCol.g, _pulseCol.b, pa) : vc;
            }

            // ---- callout (one slot: a new tap replaces it)
            if (_callLife > 0)
            {
                _callLife -= real;
                _callPop = Mathf.Max(0, _callPop - real * 6f);
                float a = Mathf.Clamp01(_callLife / 0.2f);
                float sc = reduce ? 1 : 1 + 0.18f * _callPop;
                _callout.localScale = Vector3.one * sc;
                _callText.alpha = a; _callMs.alpha = a;
                _callPlate.color = new Color(_callPlate.color.r, _callPlate.color.g, _callPlate.color.b, 0.9f * a);
                _callRim.color = new Color(_callRim.color.r, _callRim.color.g, _callRim.color.b, 0.85f * a);
                if (_callLife <= 0 || _finaleK > 0) _callout.gameObject.SetActive(false);
            }

            // ---- rival chips
            _chipLife = Mathf.Max(0, _chipLife - real);
            _chipGroup.alpha = Damp(_chipGroup.alpha, _chipLife > 0 ? 1 : 0, 10, real);
        }

        /// What the tempo is doing at `beat`: 0 steady, 1 faster, 2 slower, 3 jump coming (in `inBeats`), 4 just jumped,
        /// 5 hold now, 6 hold coming, 7 count-in.
        int TrendCode(int k, double beat, out int inBeats)
        {
            inBeats = 0;
            var s = _round.Slots[k];
            int i = (int)System.Math.Floor(beat);
            if (beat < 0) return 7;
            if (i >= s.Beats.Count) return 0;
            if (_fermata[k][i]) return 5;
            for (int j = i + 1; j <= i + 4 && j < s.Beats.Count; j++)
            {
                if (s.Beats[j].Hidden) break;
                if (_jumpAt[k][j]) { inBeats = j - i; return 3; }
                if (_fermata[k][j] && j - i <= 2) { inBeats = j - i; return 6; }
            }
            for (int j = i; j >= 0 && j > i - 2; j--) if (_jumpAt[k][j]) return 4;
            int r = _rampDir[k][i];
            return r > 0 ? 1 : r < 0 ? 2 : 0;
        }

        void ApplyTrend(int code, int n)
        {
            string word, sub; Color c; float rot = 0; Sprite icon = ArrowSprite();
            var s = Slot;
            int k = s.Index;
            switch (code)
            {
                case 1: word = "FASTER"; sub = "ACCELERANDO"; c = UiKit.Hex(0xffb04a); rot = 32; break;
                case 2: word = "SLOWER"; sub = "RITARDANDO"; c = UiKit.Hex(0x7fc8ff); rot = -32; break;
                case 3:
                {
                    int i = (int)System.Math.Floor(Beat);
                    double now = DisplayBpm(k, System.Math.Max(0, Beat)), then = DisplayBpm(k, i + n + 0.1);
                    bool up = then > now;
                    word = $"JUMP IN {n}"; sub = up ? "SUDDENLY FASTER" : "SUDDENLY SLOWER"; c = UiKit.Hex(0xff6a5a); rot = up ? 70 : -70; break;
                }
                case 4: word = "NEW TEMPO"; sub = "SUBITO"; c = UiKit.Hex(0xff6a5a); rot = 0; break;
                case 5: word = "HOLD..."; sub = "FERMATA - WAIT FOR IT"; c = UiKit.Gold; icon = FermataSprite(); break;
                case 6: word = n <= 1 ? "HOLD NEXT" : $"HOLD IN {n}"; sub = "FERMATA AHEAD"; c = UiKit.Gold; icon = FermataSprite(); break;
                case 7: word = "COUNT-IN"; sub = "OPENING TEMPO"; c = UiKit.Cream; break;
                case 98: word = "FINE"; sub = "THE END"; c = UiKit.Cream; break;
                case 99: word = "BY EAR"; sub = "NO BATON - LISTEN"; c = CallEarly; icon = null; break;
                default: word = "STEADY"; sub = "A TEMPO"; c = Color.white; break;
            }
            _trendText.text = word;
            UiKit.SetFill(_trendText, c);
            _trendSub.text = sub;
            _trendIcon.enabled = icon != null;
            _trendIcon.sprite = icon;
            _trendIcon.color = c;
            _trendIcon.rectTransform.localRotation = Quaternion.Euler(0, 0, icon == ArrowSprite() ? rot : 0);
            _trendText.rectTransform.anchoredPosition = new Vector2(icon == null ? -186 : -152, 8);
            _trendSub.rectTransform.anchoredPosition = new Vector2(icon == null ? -186 : -152, -18);
        }
    }

    /// The tempo ribbon: the curve over a window of beats (2 behind, 14 ahead) as a line with a soft fill, a dot per
    /// beat, red ticks where it jumps, and dark BY EAR bands with no curve in them where the pulse will be hidden. The
    /// playhead is the left edge's vertical line. Rebuilt every frame from the curve (no allocation: VertexHelper pools).
    public sealed class TempoRibbon : MaskableGraphic
    {
        const double Behind = 2, Ahead = 14;
        const int Samples = 64;
        TempoCurve _curve;
        TempoSlot _slot;
        FinaleFeverGame _game;
        int _k;
        double _beat, _lo, _hi;
        static readonly Color Line = new Color(1f, 0.9f, 0.6f, 1), Fill = new Color(1f, 0.8f, 0.4f, 0.28f), Band = new Color(0.1f, 0.25f, 0.45f, 0.75f),
            Dot = new Color(1, 1, 1, 0.95f), Jump = new Color(1f, 0.42f, 0.36f, 1), Head = new Color(1, 1, 1, 0.9f);

        internal void Set(TempoCurve curve, int k, double beat, double lo, double hi, TempoSlot slot, FinaleFeverGame game)
        {
            _curve = curve; _k = k; _beat = beat; _lo = lo; _hi = hi; _slot = slot; _game = game;
            SetVerticesDirty();
        }

        float X(double b) { var r = rectTransform.rect; return r.xMin + (float)((b - (_beat - Behind)) / (Behind + Ahead)) * r.width; }
        float Y(double bpm) { var r = rectTransform.rect; return r.yMin + 4 + (float)((bpm - _lo) / System.Math.Max(1e-3, _hi - _lo)) * (r.height - 8); }

        internal Vector2 PointAt(double b, double bpm) => new Vector2(X(b), Y(bpm));

        bool HiddenAt(double b)
        {
            int i = (int)System.Math.Floor(b);
            return i >= 0 && i < _slot.Beats.Count && _slot.Beats[i].Hidden;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_curve == null || _game == null) return;
            var r = rectTransform.rect;
            double b0 = _beat - Behind, b1 = _beat + Ahead;
            double end = _curve.BeatCount;
            // hidden bands
            foreach (var h in _slot.HiddenStretches)
            {
                double s = System.Math.Max(b0, h.First), e = System.Math.Min(b1, h.Last + 1);
                if (e <= s) continue;
                Quad(vh, new Vector2(X(s), r.yMin), new Vector2(X(e), r.yMax), Band);
                // a dashed top so the band reads as "the curve goes on, unseen"
                for (double d = s; d < e - 0.2; d += 0.5) Quad(vh, new Vector2(X(d), r.yMax - 3), new Vector2(X(System.Math.Min(e, d + 0.25)), r.yMax - 1), new Color(0.6f, 0.85f, 1f, 0.7f));
                // hatching: the band is a deliberate stretch (by ear), not missing data
                float xs = X(s), xe = X(e), slant = r.height * 0.55f;
                for (float x = xs - slant; x < xe; x += 9f)
                {
                    float x0 = Mathf.Max(xs, x), x1 = Mathf.Min(xe, x + 3f);
                    float t0 = Mathf.Max(xs, x + slant), t1 = Mathf.Min(xe, x + 3f + slant);
                    if (x1 <= x0 && t1 <= t0) continue;
                    int i = vh.currentVertCount;
                    var hc = new Color(0.55f, 0.8f, 1f, 0.22f);
                    vh.AddVert(new Vector3(x0, r.yMin), hc, Vector2.zero); vh.AddVert(new Vector3(t0, r.yMax - 4), hc, Vector2.zero);
                    vh.AddVert(new Vector3(Mathf.Max(t0, t1), r.yMax - 4), hc, Vector2.zero); vh.AddVert(new Vector3(Mathf.Max(x0, x1), r.yMin), hc, Vector2.zero);
                    vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
                }
            }
            // the curve: fill and line, skipping the hidden beats and the stretch past the end
            float prevX = 0, prevY = 0; bool have = false;
            for (int n = 0; n <= Samples; n++)
            {
                double b = b0 + (b1 - b0) * n / Samples;
                bool draw = b >= 0 && b <= end && !HiddenAt(b);
                if (!draw) { have = false; continue; }
                float x = X(b), y = Y(_game.RibbonBpm(_k, b));
                if (have)
                {
                    int i0 = vh.currentVertCount;
                    vh.AddVert(new Vector3(prevX, r.yMin), new Color32(255, 200, 100, 0), Vector2.zero);
                    vh.AddVert(new Vector3(prevX, prevY), Fill, Vector2.zero);
                    vh.AddVert(new Vector3(x, y), Fill, Vector2.zero);
                    vh.AddVert(new Vector3(x, r.yMin), new Color32(255, 200, 100, 0), Vector2.zero);
                    vh.AddTriangle(i0, i0 + 1, i0 + 2); vh.AddTriangle(i0, i0 + 2, i0 + 3);
                    Segment(vh, new Vector2(prevX, prevY), new Vector2(x, y), 2.6f, Line);
                }
                prevX = x; prevY = y; have = true;
            }
            // beat dots and jump ticks
            for (int i = (int)System.Math.Ceiling(System.Math.Max(0, b0)); i <= System.Math.Min(end - 1, b1); i++)
            {
                if (HiddenAt(i)) continue;
                float x = X(i), y = Y(_game.RibbonBpm(_k, i + 0.001));
                if (_game.RibbonJumpAt(_k, i)) Quad(vh, new Vector2(x - 1.5f, r.yMin + 2), new Vector2(x + 1.5f, r.yMax - 2), Jump);
                float d = _game.RibbonBarStart(_k, i) ? 3.4f : 2.2f;
                Quad(vh, new Vector2(x - d, y - d), new Vector2(x + d, y + d), Dot);
            }
            // the playhead
            float hx = X(_beat);
            Quad(vh, new Vector2(hx - 1, r.yMin), new Vector2(hx + 1, r.yMax), Head);
        }

        static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Color c)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(a.x, a.y), c, Vector2.zero);
            vh.AddVert(new Vector3(a.x, b.y), c, Vector2.zero);
            vh.AddVert(new Vector3(b.x, b.y), c, Vector2.zero);
            vh.AddVert(new Vector3(b.x, a.y), c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }

        static void Segment(VertexHelper vh, Vector2 a, Vector2 b, float w, Color c)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-6f) return;
            var n = new Vector2(-d.y, d.x).normalized * (w * 0.5f);
            int i = vh.currentVertCount;
            vh.AddVert(a - n, c, Vector2.zero); vh.AddVert(a + n, c, Vector2.zero);
            vh.AddVert(b + n, c, Vector2.zero); vh.AddVert(b - n, c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
