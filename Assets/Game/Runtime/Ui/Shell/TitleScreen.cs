// The title screen: the BONANZA lockup, three designed buttons, the records line and the attract card, in uGUI over the
// shared backdrop. A pure view: GameHost owns the focus, the attract reel and the music beat and pushes them in `View`.

using Bonanza.Core;
using Bonanza.HudKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bonanza.Shell
{
    public struct TitleView
    {
        public int Focus;
        public bool Attract;
        public string GameTitle, GameBlurb;
        public float Progress;       // 0..1 through the reel step
        public double Beat;          // title music beats (0 when off grid)
        public float BeatPulse;      // 1 on the beat, decaying
        public string Records1, Records2, Records3;
        public bool PerfTip;
        public int GameIndex, GameCount;
    }

    public sealed class TitleScreen : ShellScreen
    {
        public TitleView View;

        const string Word = "BONANZA";
        const float LogoCap = 104f;

        sealed class Letter { public RectTransform Rt; public TextMeshProUGUI Front, Back; public float X; }
        readonly System.Collections.Generic.List<Letter> _letters = new System.Collections.Generic.List<Letter>();
        RectTransform _logo, _tagRow, _stack, _foot, _attract, _tip, _plaque;
        CanvasGroup _stackGroup, _attractGroup, _footGroup, _tipGroup;
        Image _logoGlow, _progress, _promptDot;
        TextMeshProUGUI _tagline, _rec1, _rec2, _hint, _gameName, _blurb, _prompt, _tipHead, _tipBody, _gameNum;
        Image[] _pips;

        sealed class Btn
        {
            public float OutlineW, OutlineSet = -1; public RectTransform Rt, Inner; public Image Fill, Ring, Glow, Shadow; public TextMeshProUGUI Label;
            public RectTransform Chip; public Image ChipFill; public TextMeshProUGUI ChipText; public UiGradient Grad;
            public float Focus, Hover, Press;
        }
        readonly System.Collections.Generic.List<Btn> _btns = new System.Collections.Generic.List<Btn>();
        static readonly string[] Keys = { "SPACE", "SPACE", "SPACE" };

        static readonly Color Gold = UiKit.Hex(0xffd93d), Ember = UiKit.Hex(0xff8a2a), Plum = UiKit.Hex(0x1a0a30), Rose = UiKit.Hex(0xff6bd6);

        protected override void Build(RectTransform root)
        {
            // logo group
            _logo = UiKit.Rect("Logo", root);
            UiKit.Place(_logo, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 186), new Vector2(900, 200));
            _logoGlow = UiKit.Box(_logo, "Glow", new Color(1f, 0.68f, 0.25f, 0.5f), UiKit.Soft);
            UiKit.Place(_logoGlow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100, 330));

            float total = 0; var widths = new float[Word.Length];
            for (int i = 0; i < Word.Length; i++)
            {
                var probe = UiKit.Text(_logo, "probe", TextStyle.Display, LogoCap, Color.white);
                var v = probe.GetPreferredValues(Word[i].ToString());
                widths[i] = v.x; total += v.x;
                Object.Destroy(probe.gameObject);
            }
            float x = -total / 2;
            for (int i = 0; i < Word.Length; i++)
            {
                var l = new Letter();
                l.Rt = UiKit.Rect("L" + i, _logo);
                UiKit.Place(l.Rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(widths[i] + 40, LogoCap * 2.6f));
                l.X = x + widths[i] / 2;
                // the extrusion: a dark ember copy stacked under the face
                l.Back = UiKit.Text(l.Rt, "Back", TextStyle.Display, LogoCap, Ember);
                l.Back.text = Word[i].ToString();
                UiKit.SetFill(l.Back, Color.Lerp(Ember, Plum, 0.55f), false);
                l.Back.rectTransform.anchoredPosition = new Vector2(0, -9);
                l.Front = UiKit.Text(l.Rt, "Front", TextStyle.Display, LogoCap, Gold);
                l.Front.text = Word[i].ToString();
                foreach (var t in new[] { l.Back, l.Front }) { t.rectTransform.sizeDelta = new Vector2(widths[i] + 40, LogoCap * 2.6f); }
                _letters.Add(l);
                x += widths[i];
            }

            _tagRow = UiKit.Rect("Tagline", root);
            UiKit.Place(_tagRow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 54), new Vector2(900, 40));
            _tagline = UiKit.Text(_tagRow, "Text", TextStyle.Label, 17f, UiKit.Cream, italic: false);
            _tagline.text = "TAP THE BEAT.  WIN THE PARTY.";
            _tagline.characterSpacing = 9f;
            UiKit.Place(_tagline.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 40));
            for (int s = -1; s <= 1; s += 2)
            {
                var line = UiKit.Box(_tagRow, "Rule", new Color(1f, 0.85f, 0.4f, 0.55f), UiKit.Pill, true, 24);
                UiKit.Place(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(s * 305, 0), new Vector2(90, 3));
            }

            // button stack
            _stack = UiKit.Rect("Buttons", root);
            UiKit.Place(_stack, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -88), new Vector2(520, 260));
            _stackGroup = _stack.gameObject.AddComponent<CanvasGroup>();
            string[] names = { "PARTY", "FREE PLAY", "OPTIONS" };
            for (int i = 0; i < 3; i++) _btns.Add(MakeButton(i, names[i], new Vector2(0, 84 - i * 70)));

            // records plaque + key hint
            _foot = UiKit.Rect("Foot", root);
            UiKit.Place(_foot, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 70), new Vector2(760, 100));
            _footGroup = _foot.gameObject.AddComponent<CanvasGroup>();
            var plate = UiKit.Box(_foot, "Plaque", new Color(0.05f, 0.03f, 0.12f, 0.97f), UiKit.Pill, true, 24);
            _plaque = plate.rectTransform;
            UiKit.Place(_plaque, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(430, 60));
            var edge = UiKit.Box(_plaque, "Edge", new Color(1, 0.95f, 0.8f, 0.14f), UiKit.PillRing(4), true, 24);
            UiKit.Stretch(edge.rectTransform);
            _rec1 = UiKit.Text(_plaque, "R1", TextStyle.Label, 15f, UiKit.Cream, italic: false);
            UiKit.Place(_rec1.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(420, 26));
            _rec1.characterSpacing = 3f;
            _rec2 = UiKit.Text(_plaque, "R2", TextStyle.Label, 15f, Gold, italic: false);
            UiKit.Place(_rec2.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(420, 26));
            _rec2.characterSpacing = 3f;
            var hintPlate = UiKit.Box(_foot, "HintPlate", new Color(0.05f, 0.03f, 0.12f, 0.95f), UiKit.Pill, true, 24);
            UiKit.Place(hintPlate.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(500, 32));
            _hint = UiKit.Text(hintPlate.rectTransform, "Hint", TextStyle.Label, 14f, UiKit.Cream, italic: false);
            UiKit.Place(_hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(490, 30));
            _hint.characterSpacing = 3f;
            _hint.text = "UP / DOWN  CHOOSE        SPACE  SELECT";

            // attract card: the game on the left, where it sits in the reel on the right
            _attract = UiKit.Rect("Attract", root);
            UiKit.Place(_attract, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -92), new Vector2(760, 280));
            _attractGroup = _attract.gameObject.AddComponent<CanvasGroup>();
            var card = UiKit.Box(_attract, "Card", new Color(0.05f, 0.03f, 0.12f, 0.9f), UiKit.Pill, true, 24);
            UiKit.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 22), new Vector2(700, 190));
            var cardRing = UiKit.Box(card.rectTransform, "Ring", new Color(1, 0.9f, 0.54f, 0.65f), UiKit.PillRing(6), true, 24);
            UiKit.Stretch(cardRing.rectTransform);
            _gameName = UiKit.Text(card.rectTransform, "Game", TextStyle.Display, 34f, Gold, TextAlignmentOptions.Left);
            UiKit.Place(_gameName.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -22), new Vector2(430, 80));
            _blurb = UiKit.Text(card.rectTransform, "Blurb", TextStyle.Label, 17f, UiKit.Cream, TextAlignmentOptions.TopLeft, italic: false, upper: false);
            _blurb.textWrappingMode = TextWrappingModes.Normal;
            UiKit.Place(_blurb.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -104), new Vector2(430, 56));
            var div = UiKit.Box(card.rectTransform, "Div", new Color(1, 0.9f, 0.54f, 0.3f), UiKit.Pill, true, 24);
            UiKit.Place(div.rectTransform, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-190, 8), new Vector2(3, 110));
            var gl = UiKit.Text(card.rectTransform, "GL", TextStyle.Label, 13f, UiKit.Cream, italic: false);
            gl.characterSpacing = 5f; gl.text = "NOW SHOWING";
            UiKit.Place(gl.rectTransform, new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(-100, -24), new Vector2(170, 24));
            _gameNum = UiKit.Text(card.rectTransform, "Num", TextStyle.Display, 40f, Gold);
            UiKit.Place(_gameNum.rectTransform, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-100, 4), new Vector2(170, 100));
            _pips = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                _pips[i] = UiKit.Box(card.rectTransform, "Pip" + i, Color.white, UiKit.Disc);
                UiKit.Place(_pips[i].rectTransform, new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-100 + (i - 2) * 22, 44), new Vector2(12, 12));
            }
            var barBg = UiKit.Box(card.rectTransform, "BarBg", new Color(1, 1, 1, 0.16f), UiKit.Pill, true, 24);
            UiKit.Place(barBg.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(630, 8));
            _progress = UiKit.Box(barBg.rectTransform, "Bar", Gold, UiKit.Pill, true, 24);
            UiKit.Stretch(_progress.rectTransform);
            _prompt = UiKit.Text(_attract, "Prompt", TextStyle.Display, 24f, UiKit.Cream);
            UiKit.Place(_prompt.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 4), new Vector2(600, 64));
            _prompt.text = "PRESS ANY BUTTON";

            // perf tip
            _tip = UiKit.Rect("PerfTip", root);
            UiKit.Place(_tip, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(700, 260));
            _tipGroup = _tip.gameObject.AddComponent<CanvasGroup>();
            var tipBg = UiKit.Box(_tip, "Bg", new Color(12 / 255f, 8 / 255f, 32 / 255f, UiKit.DarkAlpha(0.92f)), UiKit.Pill, true, 24);
            UiKit.Stretch(tipBg.rectTransform);
            var tipRing = UiKit.Box(tipBg.rectTransform, "Ring", new Color(1, 0.9f, 0.54f, 0.8f), UiKit.PillRing(6), true, 24);
            UiKit.Stretch(tipRing.rectTransform);
            _tipHead = UiKit.Text(_tip, "Head", TextStyle.Display, 26f, Gold);
            UiKit.Place(_tipHead.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -26), new Vector2(660, 70));
            _tipHead.text = "RUNNING SLOW ON THIS MACHINE";
            _tipBody = UiKit.Text(_tip, "Body", TextStyle.Label, 14f, Color.white, italic: false, upper: false);
            _tipBody.textWrappingMode = TextWrappingModes.Normal;
            UiKit.Place(_tipBody.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -26), new Vector2(600, 140));
            _tipBody.text = "Graphics are already at their lowest. Try: power mode Best performance, play plugged in, update your graphics driver, and set your browser to High performance in the system graphics settings.\n\n(any key to close)";

            foreach (var b in _btns) Buttons.Add(b.Rt);
        }

        Btn MakeButton(int i, string label, Vector2 pos)
        {
            var b = new Btn();
            b.Rt = UiKit.Rect("Btn" + i, _stack);
            UiKit.Place(b.Rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(430, 60));
            b.Inner = UiKit.Rect("Inner", b.Rt); UiKit.Stretch(b.Inner);
            b.Shadow = UiKit.Box(b.Inner, "Shadow", new Color(0, 0, 0, 0.5f), UiKit.Pill, true, 24);
            UiKit.Place(b.Shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -7), new Vector2(436, 64));
            b.Glow = UiKit.Box(b.Inner, "Glow", new Color(1f, 0.8f, 0.3f, 0), UiKit.Soft);
            UiKit.Place(b.Glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 170));
            b.Fill = UiKit.Box(b.Inner, "Fill", Color.white, UiKit.Pill, true, 24);
            UiKit.Stretch(b.Fill.rectTransform);
            b.Grad = b.Fill.gameObject.AddComponent<UiGradient>();
            b.Ring = UiKit.Box(b.Inner, "Ring", new Color(1, 0.9f, 0.54f, 0.6f), UiKit.PillRing(5), true, 24);
            UiKit.Stretch(b.Ring.rectTransform);
            b.Label = UiKit.Text(b.Inner, "Label", TextStyle.Display, 25f, Color.white);
            b.Label.text = label;
            b.OutlineW = b.Label.fontMaterial.GetFloat(ShaderUtilities.ID_OutlineWidth);
            UiKit.Place(b.Label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(400, 64));
            b.Chip = UiKit.Rect("Chip", b.Inner);
            UiKit.Place(b.Chip, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(76, 30));
            b.ChipFill = UiKit.Box(b.Chip, "Fill", new Color(0.1f, 0.04f, 0.2f, 0.85f), UiKit.Pill, true, 24);
            UiKit.Stretch(b.ChipFill.rectTransform);
            b.ChipText = UiKit.Text(b.Chip, "T", TextStyle.Label, 11f, Gold, italic: false);
            b.ChipText.characterSpacing = 3f;
            b.ChipText.text = Keys[i];
            UiKit.Place(b.ChipText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(76, 30));
            return b;
        }

        static float Damp(float a, float b, float rate, float dt) => Mathf.Lerp(a, b, 1 - Mathf.Exp(-rate * dt));

        public override void Tick(float dt, ShellInput input)
        {
            bool red = input.Reduced;
            float amp = red ? 0.35f : 1f;
            var v = View;
            float shown = Shown, age = Age;
            Group.alpha = UiKit.Clamp01(shown * 1.6f);

            // small screens: the logo shrinks, the menu block and the records grow so the text stays readable
            float comp = Mathf.Clamp01((600f - Screen.height) / 240f);
            // ---- logo: letters drop in with a stagger, then ride the beat
            float a1 = v.Attract ? 1 : 0;
            _attractAmt = Damp(_attractAmt, a1, red ? 40 : 6, dt);
            float lscale = Mathf.Lerp(1f, 0.74f, _attractAmt) * Mathf.Lerp(1f, 0.82f, comp);
            double bar = v.Beat;
            _logo.anchoredPosition = new Vector2(0, Mathf.Lerp(Mathf.Lerp(186, 150, comp), 210, _attractAmt) + (1 - UiKit.EaseOutCubic(shown)) * 60);
            for (int i = 0; i < _letters.Count; i++)
            {
                var l = _letters[i];
                float enter = red ? shown : UiKit.BackOut((age - 0.05f - i * 0.055f) / 0.5f);
                enter = Mathf.Min(enter, shown > 0 ? 1.2f : 0);
                double sc = 1, lift = 0, tilt = 0;
                if (v.Beat > 0) TitleBeat.Letter(v.Beat, i, amp, out sc, out lift, out tilt);
                // on a short screen the letters stay one height: no per-letter bounce, lift, tilt or bob
                float calm = 1 - comp;
                sc = 1 + (sc - 1) * calm; lift *= calm; tilt *= calm;
                float idle = red ? 0 : Mathf.Sin(Time.unscaledTime * 1.6f + i * 0.7f) * 2.2f * calm;
                float drop = (1 - enter) * 160f;
                l.Rt.anchoredPosition = new Vector2(l.X * lscale, -(float)lift * 1.2f * lscale + idle - drop);
                l.Rt.localScale = Vector3.one * ((float)sc * lscale) * (0.6f + 0.4f * Mathf.Clamp01(enter));
                l.Rt.localRotation = Quaternion.Euler(0, 0, -(float)tilt + (red ? 0 : Mathf.Sin(Time.unscaledTime * 1.2f + i) * 0.8f * calm));
                // the shine: a bright band sweeping along the word every few seconds
                float sweep = red ? -9 : Mathf.Repeat(Time.unscaledTime * 0.55f, 3.4f) * 3f - 3f;
                float k = Mathf.Exp(-Mathf.Pow((i - sweep) * 0.9f, 2));
                var face = Color.Lerp(Gold, Color.white, k * 0.85f);
                UiKit.SetFill(l.Front, face);
            }
            _logo.localScale = Vector3.one * (v.Beat > 0 ? 1f + ((float)TitleBeat.LogoScale(v.Beat, amp) - 1f) * (1 - comp) : 1f);
            _logoGlow.color = new Color(1f, 0.62f + 0.12f * v.BeatPulse, 0.25f, (0.34f + 0.16f * v.BeatPulse * amp) * UiKit.Clamp01(shown));

            _tagRow.anchoredPosition = new Vector2(0, Mathf.Lerp(54, 96, _attractAmt));
            var tg = _tagRow.GetComponent<CanvasGroup>() ?? _tagRow.gameObject.AddComponent<CanvasGroup>();
            tg.alpha = UiKit.Clamp01((age - 0.5f) * 3f) * (1 - _attractAmt * 0.0f) * (red ? shown : 1) * (1 - UiKit.Clamp01((comp - 0.4f) * 5f));   // no room for it on a short screen

            // ---- buttons
            _stack.localScale = Vector3.one * Mathf.Lerp(1f, 1.1f, comp);
            _stack.anchoredPosition = new Vector2(0, Mathf.Lerp(-88, -70, comp));
            _foot.localScale = Vector3.one * Mathf.Lerp(1f, 1.7f, comp);
            _foot.anchoredPosition = new Vector2(0, Mathf.Lerp(70, 20, comp));
            _attract.localScale = Vector3.one * Mathf.Lerp(1f, 1.2f, comp);
            _stackGroup.alpha = (1 - _attractAmt) * UiKit.Clamp01((age - 0.15f) * 3f) * (red ? shown : 1);
            _stackGroup.blocksRaycasts = false;
            for (int i = 0; i < _btns.Count; i++)
            {
                var b = _btns[i];
                bool foc = v.Focus == i && !v.Attract;
                b.Focus = Damp(b.Focus, foc ? 1 : 0, red ? 40 : 16, dt);
                b.Hover = Damp(b.Hover, input.Hover == i ? 1 : 0, red ? 40 : 18, dt);
                b.Press = Damp(b.Press, input.Pressed && input.Hover == i ? 1 : 0, red ? 60 : 30, dt);
                float hov = b.Hover * (1 - b.Focus);
                float enter = red ? 1 : UiKit.BackOut((age - 0.2f - i * 0.08f) / 0.45f);
                b.Rt.anchoredPosition = new Vector2((1 - Mathf.Min(1, enter)) * -80f, 84 - i * 70);
                float pulse = foc && !red ? 1 + v.BeatPulse * 0.025f : 1;
                b.Inner.localScale = Vector3.one * ((1 + 0.06f * b.Focus + 0.03f * hov) * (1 - 0.07f * b.Press) * pulse);
                // rest: deep indigo; hover: lifted violet with a cyan rim; focus: gold. Press only darkens the pressed button.
                Color top = Color.Lerp(new Color(0.11f, 0.06f, 0.27f, 0.96f), new Color(0.15f, 0.09f, 0.34f, 0.97f), hov);
                Color bot = Color.Lerp(new Color(0.05f, 0.02f, 0.14f, 0.96f), new Color(0.07f, 0.03f, 0.18f, 0.97f), hov);
                top = Color.Lerp(top, UiKit.Hex(0xffe27a), b.Focus); bot = Color.Lerp(bot, UiKit.Hex(0xff9a2e), b.Focus);
                float dark = 1 - 0.38f * b.Press;
                top = new Color(top.r * dark, top.g * dark, top.b * dark, top.a); bot = new Color(bot.r * dark, bot.g * dark, bot.b * dark, bot.a);
                b.Grad.Set(top, bot);
                Color rim = Color.Lerp(new Color(1f, 0.95f, 0.78f, 0.78f), new Color(0.55f, 0.95f, 1f, 1f), hov);
                b.Ring.color = Color.Lerp(rim, new Color(1, 0.98f, 0.82f, 1f), b.Focus);
                b.Shadow.color = new Color(0, 0, 0, 0.5f * (1 - 0.6f * b.Press));
                b.Label.color = Color.Lerp(Color.white, UiKit.Hex(0x2a0f45), b.Focus);
                float ow = b.OutlineW * Mathf.Lerp(1f, 0.35f, b.Focus);   // a thin outline on the gold fill: no smear
                if (Mathf.Abs(ow - b.OutlineSet) > 0.004f) { b.OutlineSet = ow; b.Label.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, ow); }
                b.Glow.color = new Color(1f, 0.75f, 0.25f, 0.55f * b.Focus * (0.8f + 0.2f * v.BeatPulse));
                b.Chip.localScale = Vector3.one * Mathf.Lerp(0.001f, 1f, b.Focus);
            }

            // ---- foot
            _footGroup.alpha = (1 - _attractAmt) * UiKit.Clamp01((age - 0.5f) * 3f) * (red ? shown : 1);
            _rec1.text = string.IsNullOrEmpty(v.Records3) ? (v.Records1 ?? "") : v.Records1 + "   -   " + v.Records3; _rec2.text = v.Records2 ?? "";

            // ---- attract card
            _attractGroup.alpha = _attractAmt * _attractAmt;
            if (_attractAmt > 0.01f)
            {
                _gameName.text = v.GameTitle ?? "";
                _blurb.text = v.GameBlurb ?? "";
                _gameNum.text = (v.GameIndex + 1) + " / " + Mathf.Max(1, v.GameCount);
                for (int pi = 0; pi < _pips.Length; pi++)
                    _pips[pi].color = pi == v.GameIndex ? Gold : new Color(1, 1, 1, 0.28f);
                var pr = _progress.rectTransform;
                pr.anchorMin = new Vector2(0, 0); pr.anchorMax = new Vector2(Mathf.Clamp01(v.Progress), 1); pr.offsetMin = pr.offsetMax = Vector2.zero;
                float pa = red ? 1 : 0.8f + 0.2f * v.BeatPulse;
                _prompt.color = new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, pa);
                _prompt.rectTransform.localScale = Vector3.one * (red ? 1 : 1 + 0.04f * v.BeatPulse);
            }

            // ---- perf tip
            _tipGroup.alpha = Damp(_tipGroup.alpha, v.PerfTip ? 1 : 0, 14, dt);
            _tip.gameObject.SetActive(_tipGroup.alpha > 0.01f || v.PerfTip);
        }

        float _attractAmt;
    }
}
