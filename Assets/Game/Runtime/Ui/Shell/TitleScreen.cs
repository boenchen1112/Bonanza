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
        public string[] Items;
    }

    public sealed class TitleScreen : ShellScreen
    {
        public TitleView View;

        const string Word = "BONANZA";
        const float LogoCap = 104f;

        sealed class Letter { public RectTransform Rt; public TextMeshProUGUI Front, Back; public float X; }
        readonly System.Collections.Generic.List<Letter> _letters = new System.Collections.Generic.List<Letter>();
        RectTransform _logo, _tagRow, _stack, _foot, _attract, _tip;
        CanvasGroup _stackGroup, _attractGroup, _footGroup, _tipGroup;
        Image _logoGlow, _progress, _promptDot;
        TextMeshProUGUI _tagline, _rec1, _rec2, _rec3, _hint, _gameName, _blurb, _prompt, _tipHead, _tipBody;

        sealed class Btn
        {
            public RectTransform Rt, Inner; public Image Fill, Ring, Glow; public TextMeshProUGUI Label;
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
            UiKit.Place(_stack, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -98), new Vector2(520, 260));
            _stackGroup = _stack.gameObject.AddComponent<CanvasGroup>();
            string[] names = { "PARTY", "FREE PLAY", "OPTIONS" };
            for (int i = 0; i < 3; i++) _btns.Add(MakeButton(i, names[i], new Vector2(0, 84 - i * 76)));

            // records + hint foot
            _foot = UiKit.Rect("Foot", root);
            UiKit.Place(_foot, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(760, 112));
            _footGroup = _foot.gameObject.AddComponent<CanvasGroup>();
            var recPill = UiKit.Box(_foot, "Pill", new Color(12 / 255f, 8 / 255f, 32 / 255f, UiKit.DarkAlpha(0.62f)), UiKit.Pill, true, 24);
            UiKit.Place(recPill.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(520, 72));
            var recRing = UiKit.Box(recPill.rectTransform, "Ring", new Color(1, 0.9f, 0.54f, 0.5f), UiKit.PillRing(5), true, 24);
            UiKit.Stretch(recRing.rectTransform);
            _rec1 = UiKit.Text(recPill.rectTransform, "R1", TextStyle.Label, 13f, UiKit.Cream, italic: false);
            UiKit.Place(_rec1.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -13), new Vector2(500, 26));
            _rec1.characterSpacing = 5f;
            _rec2 = UiKit.Text(recPill.rectTransform, "R2", TextStyle.Label, 13f, Gold, italic: false);
            UiKit.Place(_rec2.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 13), new Vector2(500, 26));
            _rec2.characterSpacing = 5f;
            _rec3 = UiKit.Text(_foot, "R3", TextStyle.Label, 12f, new Color(1, 1, 1, 0.7f), italic: false);
            UiKit.Place(_rec3.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -78), new Vector2(700, 22));
            _rec3.characterSpacing = 4f;
            _hint = UiKit.Text(_foot, "Hint", TextStyle.Label, 11f, new Color(1, 1, 1, 0.55f), italic: false);
            UiKit.Place(_hint.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(700, 22));
            _hint.characterSpacing = 4f;
            _hint.text = "UP / DOWN CHOOSE      SPACE SELECT";

            // attract card
            _attract = UiKit.Rect("Attract", root);
            UiKit.Place(_attract, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -92), new Vector2(760, 280));
            _attractGroup = _attract.gameObject.AddComponent<CanvasGroup>();
            var card = UiKit.Box(_attract, "Card", new Color(12 / 255f, 8 / 255f, 32 / 255f, UiKit.DarkAlpha(0.7f)), UiKit.Pill, true, 24);
            UiKit.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(640, 170));
            var cardRing = UiKit.Box(card.rectTransform, "Ring", new Color(1, 0.9f, 0.54f, 0.65f), UiKit.PillRing(6), true, 24);
            UiKit.Stretch(cardRing.rectTransform);
            _gameName = UiKit.Text(card.rectTransform, "Game", TextStyle.Display, 38f, Gold);
            UiKit.Place(_gameName.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -22), new Vector2(620, 90));
            _blurb = UiKit.Text(card.rectTransform, "Blurb", TextStyle.Label, 15f, UiKit.Cream, italic: false, upper: false);
            UiKit.Place(_blurb.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -16), new Vector2(580, 30));
            var barBg = UiKit.Box(card.rectTransform, "BarBg", new Color(1, 1, 1, 0.14f), UiKit.Pill, true, 24);
            UiKit.Place(barBg.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 20), new Vector2(380, 8));
            _progress = UiKit.Box(barBg.rectTransform, "Bar", Gold, UiKit.Pill, true, 24);
            UiKit.Stretch(_progress.rectTransform);
            _prompt = UiKit.Text(_attract, "Prompt", TextStyle.Display, 22f, Color.white);
            UiKit.Place(_prompt.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(600, 60));
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
            UiKit.Place(b.Rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(430, 64));
            b.Inner = UiKit.Rect("Inner", b.Rt); UiKit.Stretch(b.Inner);
            b.Glow = UiKit.Box(b.Inner, "Glow", new Color(1f, 0.8f, 0.3f, 0), UiKit.Soft);
            UiKit.Place(b.Glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 170));
            b.Fill = UiKit.Box(b.Inner, "Fill", Color.white, UiKit.Pill, true, 24);
            UiKit.Stretch(b.Fill.rectTransform);
            b.Grad = b.Fill.gameObject.AddComponent<UiGradient>();
            b.Ring = UiKit.Box(b.Inner, "Ring", new Color(1, 0.9f, 0.54f, 0.6f), UiKit.PillRing(5), true, 24);
            UiKit.Stretch(b.Ring.rectTransform);
            b.Label = UiKit.Text(b.Inner, "Label", TextStyle.Display, 25f, Color.white);
            b.Label.text = label;
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

            // ---- logo: letters drop in with a stagger, then ride the beat
            float a1 = v.Attract ? 1 : 0;
            _attractAmt = Damp(_attractAmt, a1, red ? 40 : 6, dt);
            float lscale = Mathf.Lerp(1f, 0.74f, _attractAmt);
            double bar = v.Beat;
            _logo.anchoredPosition = new Vector2(0, Mathf.Lerp(186, 210, _attractAmt) + (1 - UiKit.EaseOutCubic(shown)) * 60);
            for (int i = 0; i < _letters.Count; i++)
            {
                var l = _letters[i];
                float enter = red ? shown : UiKit.BackOut((age - 0.05f - i * 0.055f) / 0.5f);
                enter = Mathf.Min(enter, shown > 0 ? 1.2f : 0);
                double sc = 1, lift = 0, tilt = 0;
                if (v.Beat > 0) TitleBeat.Letter(v.Beat, i, amp, out sc, out lift, out tilt);
                float idle = red ? 0 : Mathf.Sin(Time.unscaledTime * 1.6f + i * 0.7f) * 2.2f;
                float drop = (1 - enter) * 160f;
                l.Rt.anchoredPosition = new Vector2(l.X * lscale, -(float)lift * 1.2f * lscale + idle - drop);
                l.Rt.localScale = Vector3.one * ((float)sc * lscale) * (0.6f + 0.4f * Mathf.Clamp01(enter));
                l.Rt.localRotation = Quaternion.Euler(0, 0, -(float)tilt + (red ? 0 : Mathf.Sin(Time.unscaledTime * 1.2f + i) * 0.8f));
                // the shine: a bright band sweeping along the word every few seconds
                float sweep = red ? -9 : Mathf.Repeat(Time.unscaledTime * 0.55f, 3.4f) * 3f - 3f;
                float k = Mathf.Exp(-Mathf.Pow((i - sweep) * 0.9f, 2));
                var face = Color.Lerp(Gold, Color.white, k * 0.85f);
                UiKit.SetFill(l.Front, face);
            }
            _logo.localScale = Vector3.one * (v.Beat > 0 ? (float)TitleBeat.LogoScale(v.Beat, amp) : 1f);
            _logoGlow.color = new Color(1f, 0.62f + 0.12f * v.BeatPulse, 0.25f, (0.34f + 0.16f * v.BeatPulse * amp) * UiKit.Clamp01(shown));

            _tagRow.anchoredPosition = new Vector2(0, Mathf.Lerp(54, 96, _attractAmt));
            var tg = _tagRow.GetComponent<CanvasGroup>() ?? _tagRow.gameObject.AddComponent<CanvasGroup>();
            tg.alpha = UiKit.Clamp01((age - 0.5f) * 3f) * (1 - _attractAmt * 0.0f) * (red ? shown : 1);

            // ---- buttons
            _stackGroup.alpha = (1 - _attractAmt) * UiKit.Clamp01((age - 0.15f) * 3f) * (red ? shown : 1);
            _stackGroup.blocksRaycasts = false;
            for (int i = 0; i < _btns.Count; i++)
            {
                var b = _btns[i];
                bool foc = v.Focus == i && !v.Attract;
                b.Focus = Damp(b.Focus, foc ? 1 : 0, red ? 40 : 16, dt);
                b.Hover = Damp(b.Hover, input.Hover == i ? 1 : 0, red ? 40 : 18, dt);
                b.Press = Damp(b.Press, input.Pressed && input.Hover == i ? 1 : 0, red ? 60 : 30, dt);
                float emph = Mathf.Max(b.Focus, b.Hover * 0.75f);
                float enter = red ? 1 : UiKit.BackOut((age - 0.2f - i * 0.08f) / 0.45f);
                b.Rt.anchoredPosition = new Vector2((1 - Mathf.Min(1, enter)) * -80f, 84 - i * 76);
                float pulse = foc && !red ? 1 + v.BeatPulse * 0.025f : 1;
                b.Inner.localScale = Vector3.one * ((1 + 0.06f * emph) * (1 - 0.05f * b.Press) * pulse);
                Color top = Color.Lerp(new Color(0.16f, 0.09f, 0.34f, 0.82f), UiKit.Hex(0xffe27a), emph);
                Color bot = Color.Lerp(new Color(0.08f, 0.04f, 0.2f, 0.82f), UiKit.Hex(0xff9a2e), emph);
                b.Grad.Set(top, bot);
                b.Ring.color = Color.Lerp(new Color(1, 0.9f, 0.54f, 0.35f), new Color(1, 0.98f, 0.8f, 1f), emph);
                UiKit.SetFill(b.Label, Color.Lerp(Color.white, UiKit.Hex(0x2a0f45), emph), false);
                b.Label.color = Color.Lerp(Color.white, UiKit.Hex(0x2a0f45), emph);
                b.Glow.color = new Color(1f, 0.75f, 0.25f, 0.55f * b.Focus * (0.8f + 0.2f * v.BeatPulse) + 0.2f * b.Hover);
                b.Chip.localScale = Vector3.one * Mathf.Lerp(0.001f, 1f, b.Focus);
            }

            // ---- foot
            _footGroup.alpha = (1 - _attractAmt) * UiKit.Clamp01((age - 0.5f) * 3f) * (red ? shown : 1);
            _rec1.text = v.Records1 ?? ""; _rec2.text = v.Records2 ?? "";
            _rec3.text = v.Records3 ?? ""; _rec3.gameObject.SetActive(!string.IsNullOrEmpty(v.Records3));

            // ---- attract card
            _attractGroup.alpha = _attractAmt * _attractAmt;
            if (_attractAmt > 0.01f)
            {
                _gameName.text = v.GameTitle ?? "";
                _blurb.text = v.GameBlurb ?? "";
                var pr = _progress.rectTransform;
                pr.anchorMin = new Vector2(0, 0); pr.anchorMax = new Vector2(Mathf.Clamp01(v.Progress), 1); pr.offsetMin = pr.offsetMax = Vector2.zero;
                float pa = red ? 1 : 0.55f + 0.45f * v.BeatPulse;
                _prompt.color = new Color(1, 1, 1, pa);
                _prompt.rectTransform.localScale = Vector3.one * (red ? 1 : 1 + 0.04f * v.BeatPulse);
            }

            // ---- perf tip
            _tipGroup.alpha = Damp(_tipGroup.alpha, v.PerfTip ? 1 : 0, 14, dt);
            _tip.gameObject.SetActive(_tipGroup.alpha > 0.01f || v.PerfTip);
        }

        float _attractAmt;
    }
}
