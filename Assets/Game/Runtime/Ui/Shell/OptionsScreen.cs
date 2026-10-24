// Options in uGUI: grouped sections of real controls (two sliders, a stepper, three segmented toggles, a reset with an
// inline confirm) and BACK. A pure view: GameHost owns the focus, the keys and the settings; the screen only reports
// what the pointer did that a button click cannot say (a slider drag, a held stepper) through `DragRow`/`DragValue`
// and `TakeRepeat`, which the host applies to Settings.
//
// Buttons, in order: 0 music track, 1 sfx track, 2/3 offset - and +, 4/5 reduce OFF/ON, 6..9 graphics, 10..12 swing
// input, 13 reset (YES while confirming), 14 back, 15 keep (confirm only), 16..22 the row bodies (a click focuses
// the row; they sit last so a control on top wins the pointer).
//
// Row (keyboard) order is the web's: Music, Sfx, Offset, Reduce, Graphics, Swing, Reset, Back.

using Bonanza.HudKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Bonanza.Shell.PartyKit;

namespace Bonanza.Shell
{
    public struct OptionsView
    {
        public int Focus;                 // 0..7, the keyboard row
        public bool Confirm;              // the reset confirm step is up
        public float Music, Sfx;          // 0..1
        public float Offset;              // ms, -300..300
        public bool Reduce, ReduceSystem; // the saved option, and the OS flag alone
        public int Graphics;              // 0 auto, 1 high, 2 medium, 3 low
        public int Swing;                 // 0 tap, 1 mouse, 2 camera
        public bool CameraAvail;
        public string GfxLine;            // what the renderer is doing now
    }

    public sealed class OptionsScreen : ShellScreen
    {
        public OptionsView View;

        public const int MusicTrack = 0, SfxTrack = 1, OffMinus = 2, OffPlus = 3, ReduceBase = 4, GfxBase = 6, SwingBase = 10,
            ResetBtn = 13, BackBtn = 14, KeepBtn = 15, RowBase = 16, RowCount = 7;

        /// The row (keyboard order) a button belongs to.
        public static int RowOf(int button)
        {
            if (button >= RowBase) return button - RowBase;
            if (button <= SfxTrack) return button;
            if (button <= OffPlus) return 2;
            if (button < GfxBase) return 3;
            if (button < SwingBase) return 4;
            if (button < ResetBtn) return 5;
            if (button == BackBtn) return 7;
            return 6;
        }

        // ---- reported to the host
        public int DragRow { get; private set; } = -1;
        public float DragValue { get; private set; }
        /// A held offset stepper has begun repeating: its release must not also count as a click.
        public bool Repeated { get; private set; }
        int _repeat;
        public int TakeRepeat() { int r = _repeat; _repeat = 0; return r; }

        const float ColW = 430, RowH = 74, CtlW = 402, CtlH = 30;

        sealed class Row
        {
            public RectTransform Rt, Inner; public CanvasGroup Group;
            public Image Shadow, Glow, Fill, Ring; public UiGradient Grad;
            public TextMeshProUGUI Label, Value;
            public float Focus, Hover;
            public float X, Y;          // top-left in the screen root (y down)
        }
        sealed class Seg
        {
            public RectTransform Rt; public Image Fill, Ring; public UiGradient Grad; public TextMeshProUGUI Label;
            public float Sel, Hover, Press; public bool Lit;
        }
        sealed class Slider
        {
            public RectTransform Track; public Image Back, Fill, Knob, KnobRing; public float Hover, Press, Shown;
        }

        readonly Row[] _rows = new Row[RowCount];
        readonly Seg[] _segs = new Seg[13];   // indexed by button - 4 for 4..12; 0..8 used
        Slider _music, _sfx, _gauge;
        Pill _back;
        Pill _minus, _plus;
        RectTransform _head, _hintRt, _panel;
        CanvasGroup _headGroup, _hintGroup, _panelGroup;
        TextMeshProUGUI _title, _hint, _helpHead, _helpText;
        readonly TextMeshProUGUI[] _secs = new TextMeshProUGUI[4];
        readonly RectTransform[] _secRt = new RectTransform[4];
        readonly Image[] _secRule = new Image[4];
        // reset row
        RectTransform _resetBtn, _keepBtn; Image _resetFill, _resetRing, _keepFill, _keepRing; UiGradient _resetGrad, _keepGrad;
        TextMeshProUGUI _resetLabel, _keepLabel;
        float _resetHover, _resetPress, _keepHover, _keepPress, _confirmT;
        RectTransform _knobBar;
        int _drag = -1; int _holdBtn = -1; float _holdT, _repT;
        readonly RectTransform[] _ctl = new RectTransform[16];

        static readonly string[] RowNames = { "MUSIC VOLUME", "SFX VOLUME", "TIMING OFFSET", "REDUCE MOTION", "GRAPHICS", "SWING KINGS INPUT", "RESET PROGRESS" };
        static readonly string[] SecNames = { "AUDIO AND TIMING", "DISPLAY", "GAMEPLAY", "DATA" };

        protected override void Build(RectTransform root)
        {
            _head = UiKit.Rect("Head", root);
            UiKit.Place(_head, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(700, 76));
            _headGroup = _head.gameObject.AddComponent<CanvasGroup>();
            _title = UiKit.Text(_head, "Title", TextStyle.Display, 40f, Gold);
            _title.text = "OPTIONS";
            UiKit.Place(_title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(700, 62));
            for (int s = -1; s <= 1; s += 2)
            {
                var line = UiKit.Box(_head, "Rule", new Color(1f, 0.85f, 0.4f, 0.55f), UiKit.Pill, true, 24);
                UiKit.Place(line.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(s * 190, -32), new Vector2(110, 4));
            }

            // section headers
            for (int i = 0; i < 4; i++)
            {
                _secRt[i] = UiKit.Rect("Sec" + i, root);
                _secs[i] = UiKit.Text(_secRt[i], "T", TextStyle.Label, 13f, Gold, TextAlignmentOptions.Left, italic: false);
                _secs[i].characterSpacing = 5f; _secs[i].text = SecNames[i];
                _secRule[i] = UiKit.Box(_secRt[i], "Rule", new Color(1f, 0.85f, 0.4f, 0.35f), UiKit.Pill, true, 24);
            }

            // rows: left column music, sfx, offset; right column reduce, graphics, swing, reset
            float lx = -ColW - 10, rx = 10;      // left edges relative to the screen centre
            float[][] pos =
            {
                new[] { lx, 138f }, new[] { lx, 220f }, new[] { lx, 302f },
                new[] { rx, 138f }, new[] { rx, 220f }, new[] { rx, 332f }, new[] { rx, 446f },
            };
            for (int i = 0; i < RowCount; i++) { _rows[i] = MakeRow(root, i); _rows[i].X = pos[i][0]; _rows[i].Y = pos[i][1]; }

            // controls
            _music = MakeSlider(_rows[0]); _sfx = MakeSlider(_rows[1]);
            _ctl[MusicTrack] = _music.Track; _ctl[SfxTrack] = _sfx.Track;
            _minus = StepButton(_rows[2], "Minus", 14, false); _plus = StepButton(_rows[2], "Plus", 14 + CtlW - 44, true);
            _ctl[OffMinus] = _minus.Rt; _ctl[OffPlus] = _plus.Rt;
            _gauge = MakeSlider(_rows[2], true);
            string[] red = { "OFF", "ON" }, gfx = { "AUTO", "HIGH", "MEDIUM", "LOW" }, sw = { "TAP", "MOUSE", "CAMERA" };
            MakeSegs(_rows[3], ReduceBase, red); MakeSegs(_rows[4], GfxBase, gfx); MakeSegs(_rows[5], SwingBase, sw);
            MakeReset(_rows[6]);

            // help panel under the audio rows
            _panel = UiKit.Rect("Help", root);
            UiKit.Place(_panel, new Vector2(0.5f, 1), new Vector2(0, 1), new Vector2(lx, -392), new Vector2(ColW, 124));
            _panelGroup = _panel.gameObject.AddComponent<CanvasGroup>();
            var pb = UiKit.Box(_panel, "Plate", new Color(0.05f, 0.03f, 0.12f, 0.9f), UiKit.Pill, true, 24);
            UiKit.Stretch(pb.rectTransform);
            var pr = UiKit.Box(_panel, "Ring", new Color(1, 0.95f, 0.8f, 0.16f), UiKit.PillRing(5), true, 24);
            UiKit.Stretch(pr.rectTransform);
            _helpHead = UiKit.Text(_panel, "Head", TextStyle.Label, 12f, Gold, TextAlignmentOptions.Left, italic: false);
            _helpHead.characterSpacing = 4f;
            UiKit.Place(_helpHead.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -12), new Vector2(ColW - 36, 22));
            _helpText = UiKit.Text(_panel, "Text", TextStyle.Label, 13f, UiKit.Cream, TextAlignmentOptions.TopLeft, italic: false, upper: false);
            _helpText.textWrappingMode = TextWrappingModes.Normal;
            _helpText.overflowMode = TextOverflowModes.Overflow;
            UiKit.Place(_helpText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -36), new Vector2(ColW - 36, 82));

            _hintRt = UiKit.Rect("Hint", root);
            UiKit.Place(_hintRt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -580), new Vector2(560, 30));
            _hintGroup = _hintRt.gameObject.AddComponent<CanvasGroup>();
            var hp = UiKit.Box(_hintRt, "Plate", new Color(0.05f, 0.03f, 0.12f, 0.9f), UiKit.Pill, true, 24);
            UiKit.Stretch(hp.rectTransform);
            _hint = UiKit.Text(_hintRt, "Text", TextStyle.Label, 13f, UiKit.Cream, italic: false);
            _hint.characterSpacing = 3f; _hint.text = "UP / DOWN  CHOOSE     LEFT / RIGHT  ADJUST";
            UiKit.Place(_hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(550, 28));

            _back = MakePill(root, "Back", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-64, 14), new Vector2(210, 42), 16f, new Vector2(50, 24));
            _ctl[BackBtn] = _back.Rt;

            for (int i = 0; i < 16; i++) Buttons.Add(i == KeepBtn ? _keepBtn : _ctl[i]);
            for (int i = 0; i < RowCount; i++) Buttons.Add(_rows[i].Rt);
        }

        Row MakeRow(RectTransform root, int i)
        {
            var r = new Row();
            r.Rt = UiKit.Rect("Row" + i, root);
            UiKit.Place(r.Rt, new Vector2(0.5f, 1), new Vector2(0, 1), Vector2.zero, new Vector2(ColW, RowH));
            r.Group = r.Rt.gameObject.AddComponent<CanvasGroup>();
            r.Inner = UiKit.Rect("Inner", r.Rt); UiKit.Stretch(r.Inner);
            r.Shadow = UiKit.Box(r.Inner, "Shadow", new Color(0, 0, 0, 0.45f), UiKit.Pill, true, 24);
            UiKit.Place(r.Shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -6), new Vector2(ColW + 4, RowH + 2));
            r.Glow = UiKit.Box(r.Inner, "Glow", new Color(1, 0.8f, 0.3f, 0), UiKit.Soft);
            UiKit.Place(r.Glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ColW * 1.4f, RowH * 2.2f));
            r.Fill = UiKit.Box(r.Inner, "Fill", Color.white, UiKit.Pill, true, 24);
            UiKit.Stretch(r.Fill.rectTransform);
            r.Grad = r.Fill.gameObject.AddComponent<UiGradient>();
            r.Ring = UiKit.Box(r.Inner, "Ring", new Color(1, 0.95f, 0.8f, 0.18f), UiKit.PillRing(5), true, 24);
            UiKit.Stretch(r.Ring.rectTransform);
            r.Label = UiKit.Text(r.Inner, "Label", TextStyle.Label, 14f, UiKit.Cream, TextAlignmentOptions.Left, italic: false);
            r.Label.characterSpacing = 3f; r.Label.text = RowNames[i];
            r.Value = UiKit.Text(r.Inner, "Value", TextStyle.Display, 17f, Color.white, TextAlignmentOptions.Right);
            return r;
        }

        // a slider (or, `gauge`, the offset's centred gauge): the track rect is the hit area, the visuals sit inside it
        Slider MakeSlider(Row r, bool gauge = false)
        {
            var s = new Slider();
            float x = gauge ? 14 + 44 + 10 : 14, w = gauge ? CtlW - 2 * 54 : CtlW;
            s.Track = UiKit.Rect(gauge ? "Gauge" : "Track", r.Inner);
            UiKit.Place(s.Track, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(x, -52), new Vector2(w, CtlH));
            s.Back = UiKit.Box(s.Track, "Back", new Color(0.02f, 0.01f, 0.07f, 0.9f), UiKit.Pill, true, 24);
            UiKit.Place(s.Back.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(w, 12));
            s.Fill = UiKit.Box(s.Track, "Fill", Color.white, UiKit.Pill, true, 24);
            s.Fill.gameObject.AddComponent<UiGradient>().Set(UiKit.Hex(0xffe27a), UiKit.Hex(0xff9a2e));
            s.Knob = UiKit.Box(s.Track, "Knob", Color.white, UiKit.Disc);
            s.KnobRing = UiKit.Box(s.Knob.rectTransform, "Ring", UiKit.Hex(0xff9a2e), UiKit.Ring);
            UiKit.Stretch(s.KnobRing.rectTransform);
            return s;
        }

        Pill StepButton(Row r, string name, float x, bool plus)
        {
            var b = MakePill(r.Inner, name, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(x, -52), new Vector2(44, CtlH), 14f, new Vector2(1, 1));
            b.Chip.gameObject.SetActive(false);
            b.Label.gameObject.SetActive(false);
            var bar = Bar(b.Inner, "H", Color.white, Vector2.zero, new Vector2(16, 4));
            if (plus) Bar(b.Inner, "V", Color.white, Vector2.zero, new Vector2(4, 16));
            b.Ring.gameObject.name = "Ring";
            return b;
        }

        void MakeSegs(Row r, int baseIdx, string[] labels)
        {
            float gap = 6, w = (CtlW - gap * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                var sg = new Seg();
                sg.Rt = UiKit.Rect("Seg" + i, r.Inner);
                UiKit.Place(sg.Rt, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(14 + i * (w + gap), -52), new Vector2(w, CtlH));
                sg.Fill = UiKit.Box(sg.Rt, "Fill", Color.white, UiKit.Pill, true, 24);
                UiKit.Stretch(sg.Fill.rectTransform);
                sg.Grad = sg.Fill.gameObject.AddComponent<UiGradient>();
                sg.Ring = UiKit.Box(sg.Rt, "Ring", new Color(1, 1, 1, 0.2f), UiKit.PillRing(4), true, 24);
                UiKit.Stretch(sg.Ring.rectTransform);
                sg.Label = UiKit.Text(sg.Rt, "Label", TextStyle.Display, 14f, Color.white);
                sg.Label.text = labels[i];
                UiKit.Place(sg.Label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(w, CtlH + 10));
                _segs[baseIdx + i - ReduceBase] = sg;
                _ctl[baseIdx + i] = sg.Rt;
            }
        }

        void MakeReset(Row r)
        {
            _resetBtn = UiKit.Rect("Reset", r.Inner);
            UiKit.Place(_resetBtn, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(14, -52), new Vector2(CtlW, CtlH));
            _resetFill = UiKit.Box(_resetBtn, "Fill", Color.white, UiKit.Pill, true, 24); UiKit.Stretch(_resetFill.rectTransform);
            _resetGrad = _resetFill.gameObject.AddComponent<UiGradient>();
            _resetRing = UiKit.Box(_resetBtn, "Ring", new Color(1, 1, 1, 0.25f), UiKit.PillRing(4), true, 24); UiKit.Stretch(_resetRing.rectTransform);
            _resetLabel = UiKit.Text(_resetBtn, "Label", TextStyle.Display, 14f, Color.white);
            UiKit.Place(_resetLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(CtlW, CtlH + 10));
            _ctl[ResetBtn] = _resetBtn;
            _keepBtn = UiKit.Rect("Keep", r.Inner);
            UiKit.Place(_keepBtn, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(14 + CtlW / 2 + 3, -52), new Vector2(CtlW / 2 - 3, CtlH));
            _keepFill = UiKit.Box(_keepBtn, "Fill", Color.white, UiKit.Pill, true, 24); UiKit.Stretch(_keepFill.rectTransform);
            _keepGrad = _keepFill.gameObject.AddComponent<UiGradient>();
            _keepRing = UiKit.Box(_keepBtn, "Ring", new Color(1, 1, 1, 0.25f), UiKit.PillRing(4), true, 24); UiKit.Stretch(_keepRing.rectTransform);
            _keepLabel = UiKit.Text(_keepBtn, "Label", TextStyle.Display, 14f, Color.white);
            UiKit.Place(_keepLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(CtlW / 2, CtlH + 10));
            _keepBtn.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ tick

        const string SwingHelp = "Tap is the default. Mouse: hold the button as the ball flies, flick the pointer DOWN as it lands.";
        const string CamHelp = "Camera: raise your hand as the ball flies, conduct DOWN as it lands. Asks for your webcam when a round starts; nothing is recorded or sent.";
        const string NoCamHelp = "Camera conducting needs a browser with webcam access; here it conducts with the mouse.";

        public override void Tick(float dt, ShellInput input)
        {
            bool red = input.Reduced;
            float shown = Shown, age = Age;
            Group.alpha = UiKit.Clamp01(shown * 1.6f);
            bool small = Screen.height < 600;
            float ts = small ? 1.4f : 1f;
            var v = View;
            _confirmT = Damp(_confirmT, v.Confirm ? 1 : 0, red ? 40 : 16, dt);

            // ---- the pointer's long gestures: a slider drag, a held stepper
            ReadGestures(input, dt, v.Confirm);

            float hEnter = red ? 1 : UiKit.EaseOutCubic(age / 0.5f);
            _headGroup.alpha = UiKit.Clamp01(hEnter * 1.3f) * (red ? shown : 1);
            _head.anchoredPosition = new Vector2(0, -16 + (1 - hEnter) * 26);

            // ---- section headers
            float[][] sec =
            {
                new[] { -ColW - 10, 112f, ColW }, new[] { 10f, 112f, ColW }, new[] { 10f, 306f, ColW }, new[] { 10f, 420f, ColW },
            };
            for (int i = 0; i < 4; i++)
            {
                float e = red ? 1 : UiKit.EaseOutCubic((age - 0.08f - i * 0.05f) / 0.4f);
                var rt = _secRt[i];
                UiKit.Place(rt, new Vector2(0.5f, 1), new Vector2(0, 1), new Vector2(sec[i][0] + 6, -sec[i][1]), new Vector2(ColW - 6, 22));
                _secs[i].fontSize = UiKit.FontSizeForCap(13f * ts);
                _secs[i].characterSpacing = small ? 3f : 5f;
                UiKit.Place(_secs[i].rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(ColW, 22));
                float tw = _secs[i].GetPreferredValues(SecNames[i]).x;
                UiKit.Place(_secRule[i].rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(tw + 14, 0), new Vector2(Mathf.Max(0, ColW - 6 - tw - 14), 3));
                _secs[i].color = new Color(Gold.r, Gold.g, Gold.b, UiKit.Clamp01(e * 1.5f) * (red ? shown : 1));
                _secRule[i].color = new Color(1f, 0.85f, 0.4f, 0.35f * UiKit.Clamp01(e) * (red ? shown : 1));
            }

            int hovRow = input.Hover >= 0 ? RowOf(input.Hover) : -1;
            if (input.Hover == BackBtn) hovRow = -1;
            for (int i = 0; i < RowCount; i++) DriveRow(i, input, dt, red, age, ts, small, hovRow);

            // ---- the controls
            DriveSlider(_music, 0, v.Music, input, dt, red, v.Focus == 0);
            DriveSlider(_sfx, 1, v.Sfx, input, dt, red, v.Focus == 1);
            DriveGauge(v, input, dt, red);
            DriveSegs(ReduceBase, 2, v.Reduce ? 1 : 0, input, dt, red, v.Focus == 3, ts, v.Confirm, null);
            DriveSegs(GfxBase, 4, v.Graphics, input, dt, red, v.Focus == 4, ts, v.Confirm, null);
            DriveSegs(SwingBase, 3, v.Swing, input, dt, red, v.Focus == 5, ts, v.Confirm, v.CameraAvail ? null : "CAMERA");
            DriveReset(v, input, dt, red, ts);

            // value readouts
            _rows[0].Value.text = Mathf.RoundToInt(v.Music * 100) + "%";
            _rows[1].Value.text = Mathf.RoundToInt(v.Sfx * 100) + "%";
            _rows[2].Value.text = (v.Offset > 0 ? "+" : "") + v.Offset.ToString("0") + " MS";
            _rows[3].Value.text = !v.Reduce && v.ReduceSystem ? "ON BY SYSTEM" : "";
            _rows[4].Value.text = ""; _rows[5].Value.text = ""; _rows[6].Value.text = "";

            // ---- the help panel
            float pe = red ? 1 : UiKit.EaseOutCubic((age - 0.4f) / 0.4f);
            _panelGroup.alpha = UiKit.Clamp01(pe * 1.4f) * (red ? shown : 1) * (v.Confirm ? 0.5f : 1);
            string head, body = Help(v, out head);
            _helpHead.text = head; _helpText.text = body;
            _helpHead.fontSize = UiKit.FontSizeForCap(12f * ts);
            _helpText.fontSize = UiKit.FontSizeForCap(13f * ts * (small ? 0.95f : 1f));
            _helpText.enableAutoSizing = true; _helpText.fontSizeMax = UiKit.FontSizeForCap(13f * ts * (small ? 0.95f : 1f)); _helpText.fontSizeMin = 11f;
            _panel.sizeDelta = new Vector2(ColW, 124);
            _hint.text = v.Confirm ? "SPACE  RESET     ANY OTHER KEY  CANCEL" : "UP / DOWN  CHOOSE     LEFT / RIGHT  ADJUST";
            _hintGroup.alpha = UiKit.Clamp01((age - 0.5f) * 3f) * (red ? shown : 1) * (small ? 0 : 1);
            DriveButton(_back, input, BackBtn, v.Focus == 7, "BACK", "ESC", dt, red, age - 0.9f, !small);
            if (small) _back.Inner.localScale *= 1.3f;
            _back.Label.rectTransform.anchoredPosition = new Vector2(small ? 0 : -20, 1);
        }

        string Help(OptionsView v, out string head)
        {
            switch (v.Focus)
            {
                case 0: head = "MUSIC VOLUME"; return "How loud the music plays.";
                case 1: head = "SFX VOLUME"; return "How loud taps, hits and menu sounds are. Each step plays a tick so you can hear it.";
                case 2: head = "TIMING OFFSET"; return "Negative offset = your presses land late.\nPress late and see GOOD more than PERFECT? Try -20 ms steps.";
                case 3: head = "REDUCE MOTION"; return "Calms the camera moves and screen animation." + (v.ReduceSystem && !v.Reduce ? " Your system already asks for this." : "");
                case 4: head = "GRAPHICS"; return string.IsNullOrEmpty(v.GfxLine) ? "AUTO picks a level for this device." : v.GfxLine;
                case 5:
                    head = "SWING KINGS INPUT";
                    return SwingHelp + "\n" + (v.CameraAvail ? CamHelp : NoCamHelp);
                case 6: head = "RESET PROGRESS"; return v.Confirm ? "Erases every score, rank and stat. This cannot be undone." : "Erases every score, rank and stat.";
                default: head = "BACK"; return "Back to the title screen.";
            }
        }

        // ---- gestures

        void ReadGestures(ShellInput input, float dt, bool confirm)
        {
            if (input.Fresh) Repeated = false;
            // slider drag: begins on a fresh press over a track, runs while the button is down
            if (!confirm && input.Fresh && (input.Hover == MusicTrack || input.Hover == SfxTrack)) _drag = input.Hover;
            if (_drag >= 0 && (!input.Held || confirm)) _drag = -1;
            DragRow = _drag;
            if (_drag >= 0)
            {
                var tr = _drag == 0 ? _music.Track : _sfx.Track;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(tr, input.Pointer, null, out var lp))
                {
                    float w = tr.rect.width;
                    DragValue = Mathf.Clamp01((lp.x - 10f) / Mathf.Max(1f, w - 20f));
                }
            }
            // held stepper: after 0.45 s it repeats every 0.07 s
            if (!confirm && input.Fresh && (input.Hover == OffMinus || input.Hover == OffPlus)) { _holdBtn = input.Hover; _holdT = 0; _repT = 0; }
            if (_holdBtn >= 0)
            {
                if (!input.Held || confirm) _holdBtn = -1;
                else if (input.Hover == _holdBtn)
                {
                    _holdT += dt;
                    if (_holdT > 0.45f)
                    {
                        _repT -= dt;
                        if (_repT <= 0) { _repT = 0.07f; _repeat = _holdBtn == OffPlus ? 1 : -1; Repeated = true; }
                    }
                }
            }
        }

        // ---- rows

        void DriveRow(int i, ShellInput input, float dt, bool red, float age, float ts, bool small, int hovRow)
        {
            var r = _rows[i];
            bool foc = View.Focus == i;
            r.Focus = Damp(r.Focus, foc ? 1 : 0, red ? 40 : 14, dt);
            r.Hover = Damp(r.Hover, hovRow == i ? 1 : 0, red ? 40 : 18, dt);
            float enter = red ? 1 : UiKit.BackOut((age - 0.1f - i * 0.05f) / 0.45f);
            float dim = View.Confirm && i != 6 ? 0.4f : 1f;
            r.Group.alpha = UiKit.Clamp01(enter * 2f) * (red ? Shown : 1) * dim;
            r.Rt.anchoredPosition = new Vector2(r.X, -r.Y + (red ? 0 : (1 - Mathf.Min(1, enter)) * -26f));
            r.Rt.sizeDelta = new Vector2(ColW, RowH);
            float hv = r.Hover * (1 - r.Focus);
            r.Inner.localScale = Vector3.one * (1 + 0.015f * r.Focus) * Mathf.Clamp01(0.9f + 0.1f * enter);
            Color top = Color.Lerp(new Color(0.14f, 0.09f, 0.30f, 0.95f), new Color(0.22f, 0.16f, 0.44f, 0.97f), hv);
            Color bot = Color.Lerp(new Color(0.06f, 0.03f, 0.15f, 0.95f), new Color(0.11f, 0.07f, 0.27f, 0.97f), hv);
            top = Color.Lerp(top, new Color(0.30f, 0.20f, 0.40f, 0.98f), r.Focus);
            bot = Color.Lerp(bot, new Color(0.12f, 0.06f, 0.20f, 0.98f), r.Focus);
            r.Grad.Set(top, bot);
            Color rim = Color.Lerp(new Color(1, 0.95f, 0.8f, 0.16f), new Color(0.55f, 0.95f, 1f, 0.9f), hv);
            r.Ring.color = Color.Lerp(rim, new Color(1, 0.88f, 0.4f, 1f), r.Focus);
            r.Glow.color = new Color(1, 0.75f, 0.25f, 0.28f * r.Focus * (red ? 1 : 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 3f)));
            r.Label.fontSize = UiKit.FontSizeForCap(14f * ts);
            r.Label.characterSpacing = small ? 1.5f : 3f;
            r.Label.color = Color.Lerp(UiKit.Cream, Gold, r.Focus);
            if (i == 6) r.Label.text = View.Confirm ? "RESET ALL PROGRESS?" : RowNames[6];
            if (i == 6) r.Label.color = Color.Lerp(r.Label.color, new Color(1f, 0.55f, 0.5f), _confirmT);
            UiKit.Place(r.Label.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -8), new Vector2(ColW - 160, 26));
            r.Value.fontSize = UiKit.FontSizeForCap(17f * ts);
            r.Value.color = Color.Lerp(Color.white, Gold, r.Focus);
            r.Value.enableAutoSizing = false;
            UiKit.Place(r.Value.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-18, -6), new Vector2(190, 28));
        }

        void DriveSlider(Slider s, int btn, float value, ShellInput input, float dt, bool red, bool focus)
        {
            bool hov = input.Hover == btn || _drag == btn;
            s.Hover = Damp(s.Hover, hov ? 1 : 0, red ? 40 : 18, dt);
            s.Press = Damp(s.Press, _drag == btn ? 1 : 0, red ? 60 : 30, dt);
            float w = s.Track.rect.width;
            float x = 10 + (w - 20) * Mathf.Clamp01(value);
            var back = s.Back.rectTransform; back.sizeDelta = new Vector2(w, 12);
            var fill = s.Fill.rectTransform;
            UiKit.Place(fill, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(Mathf.Max(12, x), 12));
            float k = 22 + 6 * Mathf.Max(s.Hover, focus ? 0.6f : 0) + 4 * s.Press;
            UiKit.Place(s.Knob.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0), new Vector2(k, k));
            s.Knob.color = Color.Lerp(Color.white, UiKit.Hex(0xfff2b0), s.Press);
            s.KnobRing.color = Color.Lerp(UiKit.Hex(0xff9a2e), new Color(0.35f, 0.85f, 1f), s.Hover * (1 - (focus ? 1 : 0)));
            s.Back.color = Color.Lerp(new Color(0.02f, 0.01f, 0.07f, 0.9f), new Color(0.08f, 0.06f, 0.2f, 0.95f), s.Hover);
        }

        void DriveGauge(OptionsView v, ShellInput input, float dt, bool red)
        {
            var g = _gauge;
            float w = g.Track.rect.width;
            g.Hover = Damp(g.Hover, 0, 20, dt);
            float f = Mathf.Clamp(v.Offset / 300f, -1, 1);
            float cx = w * 0.5f, x = cx + f * (w * 0.5f - 12);
            g.Back.rectTransform.sizeDelta = new Vector2(w, 10);
            float left = Mathf.Min(cx, x), len = Mathf.Abs(x - cx);
            UiKit.Place(g.Fill.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(left, 0), new Vector2(Mathf.Max(0, len), 10));
            g.Fill.enabled = len > 1;
            bool foc = v.Focus == 2;
            float k = 20 + (foc ? 4 : 0);
            UiKit.Place(g.Knob.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0), new Vector2(k, k));
            g.KnobRing.color = UiKit.Hex(0xff9a2e);
            // the zero tick
            if (_knobBar == null)
            {
                var t = UiKit.Box(g.Track, "Zero", new Color(1, 1, 1, 0.55f), UiKit.Pill, true, 24);
                UiKit.Place(t.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(cx, 0), new Vector2(3, 22));
                t.transform.SetSiblingIndex(1);
                _knobBar = t.rectTransform;
            }
            _knobBar.anchoredPosition = new Vector2(cx, 0);
            DriveStep(_minus, OffMinus, input, dt, red, v.Focus == 2, v.Confirm);
            DriveStep(_plus, OffPlus, input, dt, red, v.Focus == 2, v.Confirm);
        }

        void DriveStep(Pill b, int idx, ShellInput input, float dt, bool red, bool rowFocus, bool confirm)
        {
            bool hov = !confirm && input.Hover == idx;
            b.Hover = Damp(b.Hover, hov ? 1 : 0, red ? 40 : 18, dt);
            b.Press = Damp(b.Press, hov && input.Pressed ? 1 : 0, red ? 60 : 30, dt);
            b.Inner.localScale = Vector3.one * ((1 + 0.08f * b.Hover) * (1 - 0.08f * b.Press));
            Color top = Color.Lerp(new Color(0.11f, 0.06f, 0.27f, 0.96f), new Color(0.27f, 0.19f, 0.55f, 0.97f), b.Hover);
            Color bot = Color.Lerp(new Color(0.05f, 0.02f, 0.14f, 0.96f), new Color(0.14f, 0.09f, 0.34f, 0.97f), b.Hover);
            float dark = 1 - 0.38f * b.Press;
            b.Grad.Set(new Color(top.r * dark, top.g * dark, top.b * dark, top.a), new Color(bot.r * dark, bot.g * dark, bot.b * dark, bot.a));
            b.Ring.color = Color.Lerp(new Color(1, 0.95f, 0.78f, 0.55f), new Color(0.55f, 0.95f, 1f, 1f), b.Hover);
            b.Glow.color = new Color(1f, 0.75f, 0.25f, 0);
            b.Shadow.color = new Color(0, 0, 0, 0.4f * (1 - 0.6f * b.Press));
            foreach (var im in b.Inner.GetComponentsInChildren<Image>(true))
                if (im.name == "H" || im.name == "V") im.color = Color.Lerp(Color.white, Gold, b.Hover);
        }

        void DriveSegs(int baseIdx, int count, int sel, ShellInput input, float dt, bool red, bool rowFocus, float ts, bool confirm, string mutedLabel)
        {
            for (int i = 0; i < count; i++)
            {
                var sg = _segs[baseIdx + i - ReduceBase];
                int idx = baseIdx + i;
                bool hov = !confirm && input.Hover == idx;
                sg.Hover = Damp(sg.Hover, hov ? 1 : 0, red ? 40 : 18, dt);
                sg.Press = Damp(sg.Press, hov && input.Pressed ? 1 : 0, red ? 60 : 30, dt);
                sg.Sel = Damp(sg.Sel, sel == i ? 1 : 0, red ? 40 : 16, dt);
                Color top = Color.Lerp(new Color(0.09f, 0.05f, 0.21f, 0.95f), new Color(0.24f, 0.17f, 0.50f, 0.97f), sg.Hover);
                Color bot = Color.Lerp(new Color(0.04f, 0.02f, 0.12f, 0.95f), new Color(0.13f, 0.08f, 0.32f, 0.97f), sg.Hover);
                Color gtop = Color.Lerp(UiKit.Hex(0xffe27a), Color.white, 0.35f * sg.Hover), gbot = Color.Lerp(UiKit.Hex(0xff9a2e), UiKit.Hex(0xffd36a), 0.5f * sg.Hover);
                top = Color.Lerp(top, gtop, sg.Sel); bot = Color.Lerp(bot, gbot, sg.Sel);
                float dark = 1 - 0.3f * sg.Press;
                sg.Grad.Set(new Color(top.r * dark, top.g * dark, top.b * dark, top.a), new Color(bot.r * dark, bot.g * dark, bot.b * dark, bot.a));
                sg.Rt.localScale = Vector3.one * (1 + 0.03f * sg.Hover) * (1 - 0.04f * sg.Press);
                Color rim = Color.Lerp(new Color(1, 1, 1, 0.16f), new Color(0.55f, 0.95f, 1f, 1f), sg.Hover);
                sg.Ring.color = Color.Lerp(rim, new Color(1, 0.98f, 0.82f, 1f), sg.Sel);
                bool muted = mutedLabel != null && sg.Label.text == mutedLabel;
                var lc = Color.Lerp(Color.white, Ink, sg.Sel);
                sg.Label.color = muted ? new Color(lc.r, lc.g, lc.b, 0.55f) : lc;
                sg.Label.fontSize = UiKit.FontSizeForCap(14f * ts);
                bool lit = sg.Sel > 0.5f;
                if (lit != sg.Lit) { sg.Lit = lit; sg.Label.outlineColor = lit ? (Color32)UiKit.Hex(0xffb347) : new Color32(20, 8, 40, 255); }
            }
        }

        void DriveReset(OptionsView v, ShellInput input, float dt, bool red, float ts)
        {
            bool conf = v.Confirm;
            _keepBtn.gameObject.SetActive(conf);
            float full = CtlW, half = CtlW / 2 - 3;
            _resetBtn.sizeDelta = new Vector2(conf ? half : full, CtlH);
            bool hovR = input.Hover == ResetBtn, hovK = conf && input.Hover == KeepBtn;
            _resetHover = Damp(_resetHover, hovR ? 1 : 0, red ? 40 : 18, dt);
            _resetPress = Damp(_resetPress, hovR && input.Pressed ? 1 : 0, red ? 60 : 30, dt);
            _keepHover = Damp(_keepHover, hovK ? 1 : 0, red ? 40 : 18, dt);
            _keepPress = Damp(_keepPress, hovK && input.Pressed ? 1 : 0, red ? 60 : 30, dt);
            // reset: calm violet with a red edge, red fill while confirming
            Color rtop = Color.Lerp(new Color(0.20f, 0.08f, 0.22f, 0.96f), new Color(0.42f, 0.14f, 0.28f, 0.97f), _resetHover);
            Color rbot = Color.Lerp(new Color(0.10f, 0.03f, 0.14f, 0.96f), new Color(0.26f, 0.07f, 0.20f, 0.97f), _resetHover);
            rtop = Color.Lerp(rtop, UiKit.Hex(0xff7a6b), _confirmT); rbot = Color.Lerp(rbot, UiKit.Hex(0xd83a4a), _confirmT);
            float dk = 1 - 0.3f * _resetPress;
            _resetGrad.Set(new Color(rtop.r * dk, rtop.g * dk, rtop.b * dk, rtop.a), new Color(rbot.r * dk, rbot.g * dk, rbot.b * dk, rbot.a));
            _resetRing.color = Color.Lerp(Color.Lerp(new Color(1f, 0.5f, 0.5f, 0.5f), new Color(0.55f, 0.95f, 1f, 1f), _resetHover), new Color(1, 0.9f, 0.85f, 1), _confirmT);
            _resetLabel.text = conf ? "YES, RESET" : "ERASE SCORES AND STATS";
            _resetLabel.fontSize = UiKit.FontSizeForCap(14f * ts);
            _resetLabel.rectTransform.sizeDelta = new Vector2(conf ? half : full, CtlH + 10);
            _resetBtn.localScale = Vector3.one * (1 + 0.03f * _resetHover) * (1 - 0.04f * _resetPress);
            // keep
            Color ktop = Color.Lerp(new Color(0.11f, 0.06f, 0.27f, 0.96f), new Color(0.27f, 0.19f, 0.55f, 0.97f), _keepHover);
            Color kbot = Color.Lerp(new Color(0.05f, 0.02f, 0.14f, 0.96f), new Color(0.14f, 0.09f, 0.34f, 0.97f), _keepHover);
            float kd = 1 - 0.3f * _keepPress;
            _keepGrad.Set(new Color(ktop.r * kd, ktop.g * kd, ktop.b * kd, ktop.a), new Color(kbot.r * kd, kbot.g * kd, kbot.b * kd, kbot.a));
            _keepRing.color = Color.Lerp(new Color(1, 1, 1, 0.3f), new Color(0.55f, 0.95f, 1f, 1f), _keepHover);
            _keepLabel.text = "KEEP";
            _keepLabel.fontSize = UiKit.FontSizeForCap(14f * ts);
            _keepBtn.localScale = Vector3.one * (1 + 0.03f * _keepHover) * (1 - 0.04f * _keepPress);
        }
    }
}
