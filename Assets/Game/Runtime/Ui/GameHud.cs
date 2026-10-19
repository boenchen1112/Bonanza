// The canvas HUD: uGUI + TextMeshPro, Screen Space Overlay, scaled against a 1280x720
// reference so it keeps the web DOM HUD's proportions at any 16:9 size (web ui/index.js
// + ui/styles.js + swingKings/hud.js). The host owns one (GameHost.Ui); a game opts in
// (Minigame.UseCanvasHud) and calls the API below instead of drawing IMGUI in DrawHud:
//
//   Ui.ShowScoreboard()                  score, combo and accuracy appear (hidden until asked for)
//   Ui.Score(n) / Combo(n) / Accuracy(a) the readouts; Score counts up
//   Ui.Outs(n, total)                    lamps pill (Swing Kings: three outs)
//   Ui.Hint(rich) / HideHint()           the instruction bar
//   Ui.Banner(text, sub, color, life)    big centred title ("SWING KINGS", "GAME!")
//   Ui.Countdown(text, ...)              a count-in beat ("3", "2", "1", "PLAY BALL!")
//   Ui.Popup(text, color, x, y, ...)     a word at a screen position (x, y in 0..1 from the top left)
//   Ui.Voices(colors, human) / Voice(i, alive, sing)   a row of voice dots at the bottom (Chomp Chorus)
//   Ui.PopupAt(cam, world, ...)          the same, anchored to a world point
//   Ui.Clear()                           everything away (between rounds)
//
// All of it is presentation: it ages on UNSCALED real time (like the web UI, which ages on
// real elapsed time), never reads the audio clock, and so can not move a judging window.
// Settings.ReduceMotion turns off pops, rises, pulses and the stamp slam: things fade only.
// Every string is ASCII (the font atlas holds U+0020..U+007E and the middle dot).

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bonanza.HudKit
{
    public sealed class GameHud : MonoBehaviour
    {
        public const float RefW = 1280, RefH = 720;

        // ---- web layout (px at the 720 reference; u = 12.384)
        const float PadL = 14.9f, PadT = 13.0f, PadR = 14.9f;

        Canvas _canvas;
        CanvasScaler _scaler;
        RectTransform _root;

        // scoreboard
        CanvasGroup _board;
        readonly Image[] _scrims = new Image[2];
        TextMeshProUGUI _scoreLabel, _score, _combo, _comboLabel, _accLabel, _acc;
        RectTransform _comboRt;
        CanvasGroup _comboGroup;
        float _scoreShown; int _scoreTarget = -1, _scoreDrawn = -1;
        int _comboValue, _comboDrawn = -1; float _comboPulse = 1;
        int _accDrawn = -1;
        bool _boardOn;

        // outs
        RectTransform _outsRt;
        Image _outsFill, _outsBorder;
        TextMeshProUGUI _outsLabel;
        readonly List<Lamp> _lamps = new List<Lamp>();
        int _outsShown, _outsTotal = 3;
        float _outsFresh, _lampPop;
        int _lampPopIndex = -1;
        CanvasGroup _outsGroup;

        // hint
        RectTransform _hintRt;
        TextMeshProUGUI _hintText;
        Image _hintFill, _hintBorder;
        CanvasGroup _hintGroup;
        float _hintFade = 1; bool _hintHiding;

        // voice dots (Chomp Chorus: one per singer, dark when that voice has dropped out)
        RectTransform _voicesRt;
        CanvasGroup _voicesGroup;
        readonly List<VoiceDot> _voices = new List<VoiceDot>();
        sealed class VoiceDot { public RectTransform Rt; public Image Fill, Glow, Ring, Shadow; public Color Color; }

        // transient layers
        RectTransform _popLayer;
        readonly List<Pop> _pops = new List<Pop>();
        BannerView _banner;
        RectTransform _top;   // above everything the HUD owns (results card sits in it)

        sealed class Lamp { public RectTransform Rt; public Image Fill, Ring, Glow; public bool On; }

        sealed class Pop
        {
            public RectTransform Rt; public TextMeshProUGUI Text; public CanvasGroup Group;
            public bool Live; public float Age, Life, Scale, Rise; public Vector2 Pos; public bool Banner;
        }

        sealed class BannerView
        {
            public RectTransform Rt; public TextMeshProUGUI Main, Sub; public CanvasGroup Group;
            public bool Live; public float Age, Life;
        }

        // ------------------------------------------------------------ build

        public static GameHud Create(Transform parent)
        {
            var go = new GameObject("GameHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);
            var hud = go.AddComponent<GameHud>();
            hud.Build();
            return hud;
        }

        void Build()
        {
            _canvas = GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 20;
            _scaler = GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(RefW, RefH);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 1;   // web sizes off vmin: the height in landscape
            _root = (RectTransform)transform;

            BuildBoard();
            BuildOuts();
            BuildHint();
            _voicesRt = UiKit.Rect("Voices", _root);
            UiKit.Place(_voicesRt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, RefH * 0.032f), new Vector2(200, 26));
            _voicesGroup = _voicesRt.gameObject.AddComponent<CanvasGroup>();
            _popLayer = UiKit.Rect("Popups", _root); UiKit.Stretch(_popLayer);
            for (int i = 0; i < 8; i++) _pops.Add(MakePop());
            _banner = MakeBanner();
            _top = UiKit.Rect("Top", _root); UiKit.Stretch(_top);
            Clear();
        }

        // Positions below are the web's, measured off its 1280x720 capture (see Captures/h05-*).

        void BuildBoard()
        {
            var rt = UiKit.Rect("Scoreboard", _root); UiKit.Stretch(rt);
            _board = rt.gameObject.AddComponent<CanvasGroup>();

            // Corner scrims (opt-in per game, CornerScrims): soft dark pools behind the two top corners, so the
            // readouts sit on a calm patch even when a busy, bloomed set runs into the corners.
            for (int i = 0; i < 2; i++)
            {
                var sc = UiKit.Box(rt, i == 0 ? "ScrimL" : "ScrimR", new Color(0.03f, 0.02f, 0.1f, 0.72f), UiKit.Soft);
                var srt = sc.rectTransform;
                srt.anchorMin = srt.anchorMax = new Vector2(i, 1); srt.pivot = new Vector2(0.5f, 0.5f);
                srt.anchoredPosition = new Vector2(i == 0 ? 80 : -80, -50); srt.sizeDelta = new Vector2(620, 380);
                sc.raycastTarget = false;
                sc.gameObject.SetActive(false);
                _scrims[i] = sc;
            }

            _scoreLabel = Label(rt, "ScoreLabel", "SCORE", 10.65f, UiKit.LabelInk, TextAlignmentOptions.TopLeft, 14f);
            TopLeft(_scoreLabel.rectTransform, PadL, PadT - 1.5f, 200, 14);
            _score = UiKit.Text(rt, "Score", TextStyle.Hud, 2.35f * UiKit.U, Color.white, TextAlignmentOptions.BaselineLeft);
            TopLeft(_score.rectTransform, 24, 66f, 400, 0);

            var combo = UiKit.Rect("Combo", rt);
            _comboRt = combo; _comboGroup = combo.gameObject.AddComponent<CanvasGroup>();
            TopLeft(combo, 22.7f, 117.1f, 400, 0);
            combo.pivot = new Vector2(0, 0.5f);
            _combo = UiKit.Text(combo, "ComboN", TextStyle.Hud, 2.0f * UiKit.U, UiKit.Yellow, TextAlignmentOptions.BaselineLeft);
            _combo.rectTransform.anchorMin = _combo.rectTransform.anchorMax = new Vector2(0, 0.5f);
            _combo.rectTransform.pivot = new Vector2(0, 0.5f);
            _combo.rectTransform.anchoredPosition = Vector2.zero; _combo.rectTransform.sizeDelta = new Vector2(300, 0);
            _comboLabel = Label(combo, "ComboLabel", "COMBO", 10.2f, new Color(1, 1, 1, 0.72f), TextAlignmentOptions.BaselineLeft, 16f);
            _comboLabel.rectTransform.anchorMin = _comboLabel.rectTransform.anchorMax = new Vector2(0, 0.5f);
            _comboLabel.rectTransform.pivot = new Vector2(0, 0.5f);
            _comboLabel.rectTransform.sizeDelta = new Vector2(200, 0);

            _accLabel = Label(rt, "AccLabel", "ACCURACY", 10.65f, UiKit.LabelInk, TextAlignmentOptions.TopRight, 14f);
            TopRight(_accLabel.rectTransform, PadR, PadT - 1.5f, 220, 14);
            _acc = UiKit.Text(rt, "Acc", TextStyle.Hud, 1.75f * UiKit.U, UiKit.AccBlue, TextAlignmentOptions.BaselineRight);
            TopRight(_acc.rectTransform, 22, 56.9f, 300, 0);
        }

        void BuildOuts()
        {
            var rt = UiKit.Rect("Outs", _root);
            _outsRt = rt; _outsGroup = rt.gameObject.AddComponent<CanvasGroup>();
            // right: 3.2% (41 px), top: 12.5% (90 px), 30.7 px tall
            UiKit.Place(rt, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-RefW * 0.032f, -RefH * 0.125f), new Vector2(143, 30.7f));
            _outsBorder = UiKit.Box(rt, "Border", new Color(1, 1, 1, UiKit.LightAlpha(0.14f)), UiKit.PillRing(2.1f * 24f / 15.3f), true, 15.35f);
            UiKit.Stretch(_outsBorder.rectTransform);
            _outsFill = UiKit.Box(rt, "Fill", new Color(12 / 255f, 8 / 255f, 32 / 255f, UiKit.DarkAlpha(0.55f)), UiKit.Pill, true, 15.35f);
            UiKit.Stretch(_outsFill.rectTransform);
            _outsFill.transform.SetAsFirstSibling();
            _outsLabel = Label(rt, "OutsLabel", "OUTS", 14.7f, UiKit.LabelInk, TextAlignmentOptions.MidlineLeft, 14f);
            _outsLabel.rectTransform.anchorMin = _outsLabel.rectTransform.anchorMax = new Vector2(0, 0.5f);
            _outsLabel.rectTransform.pivot = new Vector2(0, 0.5f);
            _outsLabel.rectTransform.sizeDelta = new Vector2(160, 20);
            SetLamps(3);
        }

        void SetLamps(int n)
        {
            foreach (var l in _lamps) if (l.Rt) Destroy(l.Rt.gameObject);
            _lamps.Clear();
            _outsTotal = n;
            for (int i = 0; i < n; i++)
            {
                var rt = UiKit.Rect("Lamp" + i, _outsRt);
                rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(15.4f, 15.4f);
                var glow = UiKit.Box(rt, "Glow", new Color(1, 0x5d / 255f, 0x73 / 255f, 0), UiKit.Soft);
                var gr = glow.rectTransform; gr.anchorMin = gr.anchorMax = new Vector2(0.5f, 0.5f); gr.sizeDelta = new Vector2(36, 36);
                var fill = UiKit.Box(rt, "Fill", UiKit.Hex(0x2a3550), UiKit.Disc); UiKit.Stretch(fill.rectTransform);
                var ring = UiKit.Box(rt, "Ring", new Color(1, 1, 1, 0.25f), UiKit.Ring); UiKit.Stretch(ring.rectTransform);
                _lamps.Add(new Lamp { Rt = rt, Fill = fill, Ring = ring, Glow = glow });
            }
            LayoutOuts();
        }

        void LayoutOuts()
        {
            _outsLabel.ForceMeshUpdate();
            float em = 14.7f, gap = em * 0.5f, pad = em * 0.8f;
            // letter-spacing adds a trailing gap after the last glyph in CSS: keep it, as the web does
            float lw = _outsLabel.preferredWidth;
            _outsLabel.rectTransform.anchoredPosition = new Vector2(pad, 0);
            float x = pad + lw + gap + 7.7f;
            for (int i = 0; i < _lamps.Count; i++)
            {
                _lamps[i].Rt.anchoredPosition = new Vector2(x, 0);
                x += 15.4f + gap;
            }
            float w = x - gap + pad;
            _outsRt.sizeDelta = new Vector2(w, _outsRt.sizeDelta.y);
        }

        void BuildHint()
        {
            var rt = UiKit.Rect("Hint", _root);
            _hintRt = rt; _hintGroup = rt.gameObject.AddComponent<CanvasGroup>();
            UiKit.Place(rt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, RefH * 0.065f), new Vector2(764, 44));
            _hintFill = UiKit.Box(rt, "Fill", new Color(12 / 255f, 8 / 255f, 32 / 255f, UiKit.DarkAlpha(0.66f)), UiKit.Pill, true, 22f);
            UiKit.Stretch(_hintFill.rectTransform);
            _hintBorder = UiKit.Box(rt, "Border", new Color(1, 229 / 255f, 138 / 255f, 0.7f), UiKit.PillRing(2f * 24f / 22f), true, 22f);
            UiKit.Stretch(_hintBorder.rectTransform);
            _hintText = Label(rt, "HintText", "", 19.84f, UiKit.Cream, TextAlignmentOptions.Midline, 6f, upper: false);
            UiKit.Stretch(_hintText.rectTransform);
        }

        Pop MakePop()
        {
            var rt = UiKit.Rect("Pop", _popLayer);
            var g = rt.gameObject.AddComponent<CanvasGroup>();
            var t = UiKit.Text(rt, "Word", TextStyle.Display, 3.1f * UiKit.U, Color.white, TextAlignmentOptions.Center);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f); t.rectTransform.sizeDelta = new Vector2(900, 120);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(10, 10);
            rt.gameObject.SetActive(false);
            return new Pop { Rt = rt, Text = t, Group = g };
        }

        BannerView MakeBanner()
        {
            var rt = UiKit.Rect("Banner", _root);
            var g = rt.gameObject.AddComponent<CanvasGroup>();
            UiKit.Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
            var main = UiKit.Text(rt, "Main", TextStyle.Title, 5.2f * UiKit.U, Color.white, TextAlignmentOptions.Center);
            main.rectTransform.anchorMin = main.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            main.rectTransform.pivot = new Vector2(0.5f, 0.5f); main.rectTransform.sizeDelta = new Vector2(1200, 140);
            var sub = Label(rt, "Sub", "", 1.15f * UiKit.U, UiKit.BannerSub, TextAlignmentOptions.Center, 10f);
            sub.rectTransform.anchorMin = sub.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            sub.rectTransform.pivot = new Vector2(0.5f, 1f); sub.rectTransform.sizeDelta = new Vector2(1000, 24);
            sub.rectTransform.anchoredPosition = new Vector2(0, -44);
            rt.gameObject.SetActive(false);
            return new BannerView { Rt = rt, Main = main, Sub = sub, Group = g };
        }

        // ------------------------------------------------------------ text helpers

        /// Plain label; `em` is the web's CSS font size in reference px (Titan One's cap is ~0.7 em, system-ui's ~0.72).
        static TextMeshProUGUI Label(Transform parent, string name, string text, float em, Color color, TextAlignmentOptions align, float spacing, bool upper = true)
        {
            var t = UiKit.Text(parent, name, TextStyle.Label, em * 0.72f, color, align, italic: false, upper: upper);
            t.characterSpacing = spacing;
            t.text = text;
            return t;
        }

        static void TopLeft(RectTransform rt, float x, float yFromTop, float w, float h)
            => UiKit.Place(rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -yFromTop), new Vector2(w, h));

        static void TopRight(RectTransform rt, float x, float yFromTop, float w, float h)
            => UiKit.Place(rt, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-x, -yFromTop), new Vector2(w, h));

        // ------------------------------------------------------------ API

        /// Everything away and the readouts back to zero (between rounds).
        public void Clear()
        {
            _boardOn = false;
            _board.alpha = 0;
            CornerScrims(false);
            _scoreTarget = -1; _scoreDrawn = -1; _scoreShown = 0; _comboDrawn = -1; _accDrawn = -1; _comboValue = 0;
            _comboGroup.alpha = 0;
            _outsGroup.alpha = 0; _outsShown = 0; ApplyLamps();
            _hintGroup.alpha = 0; _hintHiding = false; _hintFade = 1;
            _voicesGroup.alpha = 0;
            foreach (var p in _pops) { p.Live = false; p.Rt.gameObject.SetActive(false); }
            _banner.Live = false; _banner.Rt.gameObject.SetActive(false);
            if (_results != null) _results.Hide();
        }

        /// Soft dark scrims behind the top-left and top-right readouts (off by default; Clear turns them off).
        public void CornerScrims(bool on)
        {
            foreach (var sc in _scrims) if (sc) sc.gameObject.SetActive(on);
        }

        /// Show the score, combo and accuracy block (hidden until a game asks for it).
        public void ShowScoreboard(bool on = true)
        {
            _boardOn = on;
            _board.alpha = on ? 1 : 0;
            if (on) { Score(0, instant: true); Combo(0); Accuracy(1); }
        }

        /// Score, counting up to `n` unless `instant`. Presentation only.
        public void Score(int n, bool instant = false)
        {
            _scoreTarget = n;
            if (instant || Settings.ReduceMotion) { _scoreShown = n; DrawScore(n); }
        }

        void DrawScore(int n)
        {
            if (n == _scoreDrawn) return;
            _scoreDrawn = n;
            _score.SetText("{0}", n);
        }

        /// The combo counter: hidden below 2, pulses when it grows.
        public void Combo(int n)
        {
            if (n == _comboValue && _comboDrawn >= 0) return;
            bool grew = n > _comboValue;
            _comboValue = n;
            _comboDrawn = n;
            if (n >= 2)
            {
                _comboGroup.alpha = 1;
                _combo.SetText("{0}", n);
                _combo.ForceMeshUpdate();
                // the web's number cell carries a trailing space (advance .34 em + tracking), then a .42u gap
                float w = _combo.preferredWidth;
                _comboLabel.rectTransform.anchoredPosition = new Vector2(w + 0.37f * 2.0f * UiKit.U + 0.42f * UiKit.U, 5.5f);
                if (grew && !Settings.ReduceMotion) _comboPulse = 0;
            }
            else _comboGroup.alpha = 0;
        }

        /// Accuracy 0..1, shown as 0.0%..100.0%.
        public void Accuracy(float a)
        {
            int k = Mathf.Clamp(Mathf.RoundToInt(a * 1000f), 0, 1000);
            if (k == _accDrawn) return;
            _accDrawn = k;
            _acc.SetText("{0:1}%", k / 10f);
        }

        /// Outs lamps: `spent` of `total` lit. Dropping back to 0 from a full set says NEW INNING for a moment.
        public void Outs(int spent, int total = 3)
        {
            if (!_boardOn && _outsGroup.alpha < 0.5f) _outsGroup.alpha = 1;
            else _outsGroup.alpha = 1;
            if (total != _outsTotal) SetLamps(total);
            spent = Mathf.Clamp(spent, 0, total);
            if (spent > _outsShown && !Settings.ReduceMotion) { _lampPopIndex = spent - 1; _lampPop = 0; }
            if (spent == 0 && _outsShown >= total)
            {
                _outsLabel.text = "NEW INNING"; _outsFresh = 1.8f;
                _outsLabel.color = UiKit.Gold; _outsBorder.color = new Color(1, 229 / 255f, 138 / 255f, 0.8f);
                LayoutOuts();
            }
            else if (spent > 0 && _outsFresh > 0) EndFresh();
            _outsShown = spent;
            ApplyLamps();
        }

        void EndFresh()
        {
            _outsFresh = 0;
            _outsLabel.text = "OUTS"; _outsLabel.color = UiKit.LabelInk; _outsBorder.color = new Color(1, 1, 1, UiKit.LightAlpha(0.14f));
            LayoutOuts();
        }

        void ApplyLamps()
        {
            for (int i = 0; i < _lamps.Count; i++)
            {
                bool on = i < _outsShown;
                var l = _lamps[i];
                l.On = on;
                l.Fill.color = on ? UiKit.Coral : UiKit.Hex(0x2a3550);
                l.Ring.color = new Color(1, 1, 1, on ? 0.55f : 0.25f);
                l.Glow.color = new Color(UiKit.Coral.r, UiKit.Coral.g, UiKit.Coral.b, on ? 0.75f : 0f);
            }
        }

        /// The instruction bar. Rich text allowed: "<color=#ffe58a>SPACE</color> / tap ...".
        public void Hint(string rich)
        {
            _hintText.text = rich;
            _hintText.ForceMeshUpdate();
            float w = _hintText.preferredWidth + 2 * 1.2f * 19.84f + 4;
            _hintRt.sizeDelta = new Vector2(w, 44);
            _hintHiding = false; _hintFade = 1; _hintGroup.alpha = 1;
        }

        /// Fade the bar out (the web hides it once the teach section is over).
        public void HideHint() { _hintHiding = true; }

        /// Big centred title. `life` seconds, popping in and shrinking out (web banner()).
        public void Banner(string text, string sub = null, Color? color = null, float life = 1.6f)
        {
            var b = _banner;
            b.Live = true; b.Age = 0; b.Life = Mathf.Max(0.05f, life);
            UiKit.SetFill(b.Main, color ?? Color.white);
            b.Main.text = text;
            b.Sub.text = sub ?? "";
            b.Sub.gameObject.SetActive(!string.IsNullOrEmpty(sub));
            b.Rt.gameObject.SetActive(true);
            ApplyBanner(b);
        }

        /// One count-in beat. The default is the shared look (web countdown(): a centred 0.5 s banner);
        /// Swing Kings prints up top, smaller (web: popup at y = 0.26).
        public void Countdown(string text, float y = 0.26f, float scale = 1.4f, float life = 0.42f, Color? color = null)
            => Popup(text, color ?? UiKit.CountGold, 0.5f, y, scale, life);

        /// A word at a screen position (x, y in 0..1 from the top left), popping in with overshoot and
        /// drifting up (web popup() 'verdict'). `scale` multiplies the web's 3.1u cap height.
        public void Popup(string text, Color color, float x, float y, float scale = 1, float life = 0.7f)
        {
            Pop p = null;
            foreach (var q in _pops) if (!q.Live) { p = q; break; }
            if (p == null)
            {   // recycle the oldest
                float worst = -1;
                foreach (var q in _pops) { float f = q.Age / q.Life; if (f > worst) { worst = f; p = q; } }
            }
            p.Live = true; p.Age = 0; p.Life = Mathf.Max(0.05f, life); p.Scale = scale; p.Rise = 46;
            p.Pos = new Vector2(x * RefW, -y * RefH);
            UiKit.SetFill(p.Text, color);
            p.Text.text = text;
            p.Rt.gameObject.SetActive(true);
            ApplyPop(p);
        }

        /// Popup anchored to a world point as seen from `cam` (nothing if it is behind the camera).
        public bool PopupAt(Camera cam, Vector3 world, string text, Color color, float scale = 1, float life = 0.7f)
        {
            if (cam == null) return false;
            var vp = cam.WorldToViewportPoint(world);
            if (vp.z < 0) return false;
            Popup(text, color, vp.x, 1 - vp.y, scale, life);
            return true;
        }

        // ------------------------------------------------------------ voice dots

        /// A row of voice dots at the bottom centre (web chompChorus _mountVoiceDots): one per colour, the human's
        /// with a white outline. Drive each with Voice() every frame.
        public void Voices(Color[] colors, int human)
        {
            foreach (var d in _voices) if (d.Rt) Destroy(d.Rt.gameObject);
            _voices.Clear();
            const float D = 26, Gap = 18;
            float w = colors.Length * D + (colors.Length - 1) * Gap;
            _voicesRt.sizeDelta = new Vector2(w, D);
            for (int i = 0; i < colors.Length; i++)
            {
                var rt = UiKit.Rect("Voice" + i, _voicesRt);
                rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(D, D); rt.anchoredPosition = new Vector2(D / 2 + i * (D + Gap), 0);
                var c = colors[i];
                var glow = UiKit.Box(rt, "Glow", new Color(c.r, c.g, c.b, 0.6f), UiKit.Soft);
                glow.rectTransform.anchorMin = glow.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); glow.rectTransform.sizeDelta = new Vector2(D * 2.2f, D * 2.2f);
                var shadow = UiKit.Box(rt, "Shadow", new Color(0, 0, 0, 0.5f), UiKit.Disc);
                UiKit.Stretch(shadow.rectTransform); shadow.rectTransform.anchoredPosition = new Vector2(0, -2);
                var fill = UiKit.Box(rt, "Fill", c, UiKit.Disc); UiKit.Stretch(fill.rectTransform);
                Image ring = null;
                if (i == human)
                {
                    ring = UiKit.Box(rt, "Ring", Color.white, UiKit.Ring);
                    ring.rectTransform.anchorMin = ring.rectTransform.anchorMax = new Vector2(0.5f, 0.5f); ring.rectTransform.sizeDelta = new Vector2(D + 14, D + 14);
                }
                _voices.Add(new VoiceDot { Rt = rt, Fill = fill, Glow = glow, Ring = ring, Shadow = shadow, Color = c });
            }
            _voicesGroup.alpha = 1;
        }

        /// One voice's state: alive 0..1 (fades and shrinks the dot), sing 0..1 (a pop as the note is sung).
        public void Voice(int i, float alive, float sing)
        {
            if (i < 0 || i >= _voices.Count) return;
            var d = _voices[i];
            float a = 0.18f + 0.82f * alive;
            float s = (0.72f + 0.28f * alive) * (1 + (Settings.ReduceMotion ? 0 : sing * 0.5f));
            d.Fill.rectTransform.localScale = d.Shadow.rectTransform.localScale = d.Glow.rectTransform.localScale = Vector3.one * s;
            d.Fill.color = new Color(d.Color.r, d.Color.g, d.Color.b, a);
            d.Shadow.color = new Color(0, 0, 0, 0.5f * a);
            d.Glow.color = new Color(d.Color.r, d.Color.g, d.Color.b, 0.55f * alive * (0.6f + 0.4f * sing));
        }

        // ------------------------------------------------------------ results

        ResultsCard _results;

        /// The results card (dim, rank stamp, score count-up, stats). Created on first use.
        public ResultsCard Results => _results != null ? _results : _results = ResultsCard.Create(_top);

        // ------------------------------------------------------------ frame

        int _frame;

        void Update()
        {
            float dt = Mathf.Min(0.25f, Time.unscaledDeltaTime);
            // Match the canvas to the window's shape: height in landscape (the web's vmin), width when it is taller than wide.
            _scaler.matchWidthOrHeight = Screen.width >= Screen.height ? 1f : 0f;
            bool calm = Settings.ReduceMotion;

            // score count-up: ease toward the target, finishing on it
            if (_scoreTarget >= 0 && !Mathf.Approximately(_scoreShown, _scoreTarget))
            {
                _scoreShown = Mathf.Lerp(_scoreShown, _scoreTarget, 1 - Mathf.Exp(-12f * dt));
                if (Mathf.Abs(_scoreShown - _scoreTarget) < 0.6f) _scoreShown = _scoreTarget;
                DrawScore(Mathf.RoundToInt(_scoreShown));
            }

            // combo pulse: 1.35 -> 1 with a back-out over 0.28 s
            if (_comboPulse < 1)
            {
                _comboPulse = Mathf.Min(1, _comboPulse + dt / 0.28f);
                _comboRt.localScale = Vector3.one * (1.35f - 0.35f * UiKit.BackOut(_comboPulse));
            }
            else _comboRt.localScale = Vector3.one;

            // outs lamps pop (scale 1.45 for 0.16 s) and the NEW INNING label times out
            if (_lampPopIndex >= 0)
            {
                _lampPop += dt;
                for (int i = 0; i < _lamps.Count; i++)
                    _lamps[i].Rt.localScale = Vector3.one * (i == _lampPopIndex && _lampPop < 0.16f ? 1.45f : 1f);
                if (_lampPop >= 0.16f) _lampPopIndex = -1;
            }
            if (_outsFresh > 0) { _outsFresh -= dt; if (_outsFresh <= 0) EndFresh(); }

            if (_hintHiding && _hintFade > 0) { _hintFade = Mathf.Max(0, _hintFade - dt / 0.6f); _hintGroup.alpha = _hintFade; }

            foreach (var p in _pops) if (p.Live) { p.Age += dt; if (p.Age >= p.Life) { p.Live = false; p.Rt.gameObject.SetActive(false); } else ApplyPop(p); }
            if (_banner.Live) { _banner.Age += dt; if (_banner.Age >= _banner.Life) { _banner.Live = false; _banner.Rt.gameObject.SetActive(false); } else ApplyBanner(_banner); }
            if (_results != null) _results.Tick(dt, calm);
            _frame++;
        }

        void ApplyPop(Pop p)
        {
            float t = Mathf.Clamp01(p.Age / p.Life);
            bool calm = Settings.ReduceMotion;
            float s = calm ? p.Scale : UiKit.BackOut(t / 0.22f) * (1 + 0.06f * UiKit.EaseOutCubic(t)) * p.Scale;
            float rise = calm ? 0 : UiKit.EaseOutCubic(t) * p.Rise;
            p.Rt.anchoredPosition = p.Pos + new Vector2(0, rise);
            p.Rt.localScale = Vector3.one * s;
            p.Group.alpha = t > 0.72f ? 1 - (t - 0.72f) / 0.28f : (calm ? Mathf.Clamp01(t / 0.1f) : 1);
        }

        void ApplyBanner(BannerView b)
        {
            float t = Mathf.Clamp01(b.Age / b.Life);
            float inT = Mathf.Clamp01(t / 0.18f), outT = Mathf.Clamp01((t - 0.78f) / 0.22f);
            bool calm = Settings.ReduceMotion;
            float s = calm ? 1 : UiKit.BackOut(inT) * (1 - outT * 0.2f);
            b.Rt.localScale = Vector3.one * s;
            b.Group.alpha = (calm ? Mathf.Clamp01(t / 0.1f) : 1) * (1 - outT);
        }
    }
}
