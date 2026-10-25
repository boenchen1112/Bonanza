// The roster ("PICK YOUR RIVALS") in uGUI: a portrait-card grid of the eight characters on the left, the focused one
// standing large on the hall's stage on the right (CastStage), a strip of the four seats along the bottom with their
// CPU level, and the key hints. A pure view: GameHost owns the RosterState and the input and pushes it in `View`.
//
// Buttons, in order: 0..7 the character cards, 8..11 the seats, 12 the main action (continue / lock in / start), 13 back.
// Buttons that do nothing right now have a zero-size rect, so the host never lays a click target over them.

using Bonanza.Core;
using Bonanza.HudKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bonanza.Shell
{
    public struct RosterView
    {
        public RosterState R;
        public string Flash;            // the red line that replaces the sub heading for a moment
        public float ShakeT;            // > 0 while a taken card is shaking
        public int ShakeCard;
        public CastStage Cast;          // portraits come from here
    }

    public sealed class RosterScreen : ShellScreen
    {
        public RosterView View;
        /// Cast index on stage, and whether the stage figure steps back; read by the host each frame.
        public int FocusChar { get; private set; }
        /// What stands on the stage: 0 the focused character, 1 an empty lit floor (an empty seat), 2 a die for a CPU seat
        /// (nobody is picked yet), read by the host each frame.
        public int StageMode { get; private set; }
        RectTransform _stageDie; CanvasGroup _dieGroup; Image[] _dieImgs = new Image[6];
        float _dieAmt;
        /// The ref-px point the stage figure stands on.
        public const float FigureFeet = 490f, FigureHeight = 312f;

        public const int CardBase = 0, SeatBase = 8, ActionBtn = 12, BackBtn = 13;
        const float CardW = 112, CardH = 128, CardGap = 10, GridLeft = -450, GridTop = 128;
        const float SeatW = 214, SeatH = 92, SeatGap = 10, SeatTop = 572;
        const float StageX = 320, SmallSeatTop = 536;
        public const float StageXRef = StageX;

        static readonly Color Gold = UiKit.Hex(0xffd93d), Coral = UiKit.Hex(0xff7a6b), Plate = new Color(0.05f, 0.03f, 0.12f, 0.93f);

        sealed class Card
        {
            public RectTransform Rt, Inner; public Image Shadow, Glow, Frame, Ring, PlateFill, Veil, BadgeFill;
            public RawImage Portrait; public Image Tint; public TextMeshProUGUI Name, Badge;
            public CanvasGroup Group;
            public float Focus, Hover, Press, Pop, Fade;
            public int LastPick = -2;
        }
        sealed class Seat
        {
            public RectTransform Rt, Inner; public Image Fill, Ring, Glow, DiscRing, DiscFill, Ready;
            public RawImage Face; public TextMeshProUGUI Q, Who, Kind, Pick; public RectTransform ChevL, ChevR, Dice; public Image[] ChevBars = new Image[4], DiceImgs = new Image[6];
            public Image[] Pips = new Image[3];
            public CanvasGroup Group;
            public float Focus, Hover, Press, Pulse, Show;
            public string LastPick;
        }
        sealed class Pill
        {
            public RectTransform Rt, Inner; public Image Fill, Ring, Glow, Shadow; public UiGradient Grad;
            public TextMeshProUGUI Label; public RectTransform Chip; public TextMeshProUGUI ChipText;
            public float Focus, Hover, Press;
        }

        readonly Card[] _cards = new Card[8];
        readonly Seat[] _seats = new Seat[4];
        Pill _action, _back;
        TextMeshProUGUI _head, _sub, _hint, _name, _trait, _status, _youTag;
        CanvasGroup _subGroup; Image _subPlate;
        RectTransform _headRt, _info, _hintPlate, _youRt;
        CanvasGroup _infoGroup, _headGroup, _hintGroup;
        Image[] _swatch = new Image[3];
        Image _infoBar, _spot;
        int _infoChar = -1; float _infoT;
        float _flashA;
        float _prevShake;

        protected override void Build(RectTransform root)
        {
            // heading
            _headRt = UiKit.Rect("Heading", root);
            UiKit.Place(_headRt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -18), new Vector2(900, 100));
            _headGroup = _headRt.gameObject.AddComponent<CanvasGroup>();
            _head = UiKit.Text(_headRt, "Head", TextStyle.Display, 42f, Gold);
            UiKit.Place(_head.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(900, 80));
            _sub = UiKit.Text(_headRt, "Sub", TextStyle.Label, 15f, UiKit.Cream, italic: false, upper: true);
            _sub.characterSpacing = 3f;
            UiKit.Place(_sub.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -66), new Vector2(900, 30));
            _subGroup = _sub.gameObject.AddComponent<CanvasGroup>();
            var subPlate = UiKit.Box(_headRt, "SubPlate", new Color(0.05f, 0.03f, 0.12f, 0.55f), UiKit.Pill, true, 24);
            UiKit.Place(subPlate.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -64), new Vector2(880, 30));
            subPlate.rectTransform.SetAsFirstSibling();
            _subPlate = subPlate;

            // cards
            for (int i = 0; i < 8; i++) _cards[i] = MakeCard(root, i);

            // the focused character's plate, under the grid
            _info = UiKit.Rect("Info", root);
            UiKit.Place(_info, new Vector2(0.5f, 1), new Vector2(0, 1), new Vector2(GridLeft, -(GridTop + 2 * CardH + CardGap + 22)), new Vector2(478, 120));
            _infoGroup = _info.gameObject.AddComponent<CanvasGroup>();
            var infoBg = UiKit.Box(_info, "Bg", Plate, UiKit.Pill, true, 20);
            UiKit.Stretch(infoBg.rectTransform);
            _infoBar = UiKit.Box(_info, "Bar", Gold, UiKit.Pill, true, 12);
            UiKit.Place(_infoBar.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(8, 88));
            var ir = UiKit.Box(infoBg.rectTransform, "Ring", new Color(1, 0.95f, 0.8f, 0.16f), UiKit.PillRing(4), true, 20);
            UiKit.Stretch(ir.rectTransform);
            _name = UiKit.Text(_info, "Name", TextStyle.Display, 36f, Color.white, TextAlignmentOptions.Left);
            UiKit.Place(_name.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -12), new Vector2(330, 60));
            _trait = UiKit.Text(_info, "Trait", TextStyle.Label, 15f, UiKit.Cream, TextAlignmentOptions.Left, italic: false, upper: true);
            _trait.characterSpacing = 3f;
            UiKit.Place(_trait.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -58), new Vector2(420, 28));
            _status = UiKit.Text(_info, "Status", TextStyle.Label, 14f, Gold, TextAlignmentOptions.Left, italic: false);
            _status.characterSpacing = 3f;
            UiKit.Place(_status.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(36, 9), new Vector2(430, 26));
            for (int i = 0; i < 3; i++)
            {
                _swatch[i] = UiKit.Box(_info, "Sw" + i, Color.white, UiKit.Disc);
                UiKit.Place(_swatch[i].rectTransform, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-96 + i * 30, -34), new Vector2(i == 0 ? 28 : 22, i == 0 ? 28 : 22));
            }

            // the seats
            for (int i = 0; i < 4; i++) _seats[i] = MakeSeat(root, i);

            // a big die for the stage when the focused seat is a CPU (nobody picked yet)
            _stageDie = UiKit.Rect("StageDie", root);
            UiKit.Place(_stageDie, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(StageX, -330), new Vector2(150, 150));
            _dieGroup = _stageDie.gameObject.AddComponent<CanvasGroup>();
            _dieImgs[0] = UiKit.Box(_stageDie, "Shadow", new Color(0, 0, 0, 0.35f), UiKit.Pill, true, 24);
            UiKit.Place(_dieImgs[0].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -12), new Vector2(150, 150));
            var dieBody = UiKit.Box(_stageDie, "Body", new Color(1, 1, 1, 0.92f), UiKit.Pill, true, 24);
            UiKit.Stretch(dieBody.rectTransform);
            _dieImgs[1] = dieBody;
            var dpp = new[] { new Vector2(-38, 38), new Vector2(38, 38), new Vector2(0, 0), new Vector2(-38, -38), new Vector2(38, -38) };
            for (int k = 0; k < 5; k++)
            {
                _dieImgs[k + 1 > 5 ? 5 : k + 1] = _dieImgs[k + 1 > 5 ? 5 : k + 1];
                var pip = UiKit.Box(_stageDie, "Pip" + k, UiKit.Hex(0x2a0f45), UiKit.Disc);
                UiKit.Place(pip.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dpp[k], new Vector2(26, 26));
            }

            // main action and back
            _action = MakePill(root, "Action", "CONTINUE", "SPACE", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(StageX, -506), new Vector2(340, 58), 23f);
            _back = MakePill(root, "Back", "BACK", "X", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-72, 12), new Vector2(136, 36), 16f);

            // hint
            _hintPlate = UiKit.Rect("Hint", root);
            UiKit.Place(_hintPlate, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-40, 14), new Vector2(480, 30));
            _hintGroup = _hintPlate.gameObject.AddComponent<CanvasGroup>();
            var hp = UiKit.Box(_hintPlate, "Plate", new Color(0.05f, 0.03f, 0.12f, 0.9f), UiKit.Pill, true, 24);
            UiKit.Stretch(hp.rectTransform);
            _hint = UiKit.Text(_hintPlate, "Text", TextStyle.Label, 13f, UiKit.Cream, italic: false);
            _hint.characterSpacing = 3f;
            UiKit.Place(_hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(470, 28));

            for (int i = 0; i < 8; i++) Buttons.Add(_cards[i].Rt);
            for (int i = 0; i < 4; i++) Buttons.Add(_seats[i].Rt);
            Buttons.Add(_action.Rt);
            Buttons.Add(_back.Rt);
        }

        static RectTransform TopCentre(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var rt = UiKit.Rect(name, parent);
            UiKit.Place(rt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), pos, size);
            return rt;
        }

        Card MakeCard(RectTransform root, int i)
        {
            var c = new Card();
            var def = Cast.All[i];
            var col = ColorOf(def.Color);
            c.Rt = TopCentre(root, "Card" + i, CardPos(i), new Vector2(CardW, CardH));
            c.Group = c.Rt.gameObject.AddComponent<CanvasGroup>();
            c.Inner = UiKit.Rect("Inner", c.Rt); UiKit.Stretch(c.Inner);
            c.Shadow = UiKit.Box(c.Inner, "Shadow", new Color(0, 0, 0, 0.5f), UiKit.Pill, true, 20);
            UiKit.Place(c.Shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -6), new Vector2(CardW + 4, CardH + 4));
            c.Glow = UiKit.Box(c.Inner, "Glow", new Color(col.r, col.g, col.b, 0), UiKit.Soft);
            UiKit.Place(c.Glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardW * 1.9f, CardH * 1.7f));
            c.Frame = UiKit.Box(c.Inner, "Frame", Color.Lerp(col, UiKit.Hex(0x120a28), 0.45f), UiKit.Pill, true, 20);
            UiKit.Stretch(c.Frame.rectTransform);
            // the portrait, clipped to the rounded frame
            var clip = UiKit.Box(c.Inner, "Clip", Color.white, UiKit.Pill, true, 20);
            UiKit.Stretch(clip.rectTransform);
            var mask = clip.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = false;
            c.Tint = UiKit.Box(clip.rectTransform, "Tint", col, null);
            UiKit.Stretch(c.Tint.rectTransform);
            var pgo = new GameObject("Portrait", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            pgo.transform.SetParent(clip.rectTransform, false);
            c.Portrait = pgo.GetComponent<RawImage>(); c.Portrait.raycastTarget = false;
            UiKit.Stretch((RectTransform)pgo.transform);
            c.Portrait.uvRect = new Rect((1 - CardW / CardH) / 2f, 0, CardW / CardH, 1);
            c.Portrait.color = new Color(1, 1, 1, 0);
            // name plate across the foot
            c.PlateFill = UiKit.Box(clip.rectTransform, "Plate", Color.white, null);
            UiKit.Place(c.PlateFill.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(CardW, 32));
            var g = c.PlateFill.gameObject.AddComponent<UiGradient>();
            g.Set(new Color(col.r * 0.85f, col.g * 0.85f, col.b * 0.85f, 0.97f), new Color(col.r * 0.35f, col.g * 0.35f, col.b * 0.35f, 0.99f));
            c.Name = UiKit.Text(c.PlateFill.rectTransform, "Name", TextStyle.Display, 17f, Color.white);
            c.Name.text = def.Name;
            UiKit.Place(c.Name.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(CardW, 34));
            c.Veil = UiKit.Box(clip.rectTransform, "Veil", new Color(0.04f, 0.02f, 0.1f, 0), null);
            UiKit.Stretch(c.Veil.rectTransform);
            c.Ring = UiKit.Box(c.Inner, "Ring", new Color(1, 1, 1, 0.22f), UiKit.PillRing(5), true, 20);
            UiKit.Stretch(c.Ring.rectTransform);
            // taken badge
            c.BadgeFill = UiKit.Box(c.Inner, "Badge", Gold, UiKit.Disc);
            UiKit.Place(c.BadgeFill.rectTransform, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-12, -12), new Vector2(34, 34));
            c.Badge = UiKit.Text(c.BadgeFill.rectTransform, "T", TextStyle.Display, 15f, UiKit.Hex(0x2a0f45));
            UiKit.Place(c.Badge.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(40, 34));
            return c;
        }

        static Vector2 CardPos(int i) =>
            new Vector2(GridLeft + CardW / 2 + (i % 4) * (CardW + CardGap), -(GridTop + (i / 4) * (CardH + CardGap)));

        Seat MakeSeat(RectTransform root, int i)
        {
            var s = new Seat();
            s.Rt = TopCentre(root, "Seat" + i, SeatPos(i, 4), new Vector2(SeatW, SeatH));
            s.Group = s.Rt.gameObject.AddComponent<CanvasGroup>();
            s.Inner = UiKit.Rect("Inner", s.Rt); UiKit.Stretch(s.Inner);
            s.Glow = UiKit.Box(s.Inner, "Glow", new Color(1, 0.8f, 0.3f, 0), UiKit.Soft);
            UiKit.Place(s.Glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SeatW * 1.5f, SeatH * 2.1f));
            s.Fill = UiKit.Box(s.Inner, "Fill", Plate, UiKit.Pill, true, 24);
            UiKit.Stretch(s.Fill.rectTransform);
            s.Ring = UiKit.Box(s.Inner, "Ring", new Color(1, 0.95f, 0.8f, 0.2f), UiKit.PillRing(5), true, 24);
            UiKit.Stretch(s.Ring.rectTransform);
            // face disc
            s.DiscFill = UiKit.Box(s.Inner, "Disc", new Color(1, 1, 1, 0.1f), UiKit.Disc);
            UiKit.Place(s.DiscFill.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(11, 0), new Vector2(52, 52));
            var mask = s.DiscFill.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = true;
            var fgo = new GameObject("Face", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            fgo.transform.SetParent(s.DiscFill.rectTransform, false);
            s.Face = fgo.GetComponent<RawImage>(); s.Face.raycastTarget = false; s.Face.color = new Color(1, 1, 1, 0);
            UiKit.Stretch((RectTransform)fgo.transform);
            s.Face.uvRect = new Rect(0.15f, 0.2f, 0.7f, 0.7f);
            s.DiscRing = UiKit.Box(s.Inner, "DiscRing", new Color(1, 1, 1, 0.5f), UiKit.Ring);
            UiKit.Place(s.DiscRing.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(11, 0), new Vector2(52, 52));
            s.Q = UiKit.Text(s.Inner, "Q", TextStyle.Display, 24f, new Color(1, 1, 1, 0.4f));
            s.Q.text = "?";
            UiKit.Place(s.Q.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(11, 1), new Vector2(52, 60));
            s.Who = UiKit.Text(s.Inner, "Who", TextStyle.Label, 13f, Gold, TextAlignmentOptions.Left, italic: false);
            s.Who.characterSpacing = 3f;
            UiKit.Place(s.Who.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(72, -8), new Vector2(110, 20));
            s.Kind = UiKit.Text(s.Inner, "Kind", TextStyle.Display, 18f, Color.white, TextAlignmentOptions.Left);
            UiKit.Place(s.Kind.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(72, 2), new Vector2(86, 30));
            s.Pick = UiKit.Text(s.Inner, "Pick", TextStyle.Label, 13f, UiKit.Cream, TextAlignmentOptions.Left, italic: false);
            s.Pick.characterSpacing = 2f;
            UiKit.Place(s.Pick.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(72, 9), new Vector2(136, 22));
            for (int k = 0; k < 3; k++)
            {
                s.Pips[k] = UiKit.Box(s.Inner, "Pip" + k, Color.white, UiKit.Pill, true, 8);
                UiKit.Place(s.Pips[k].rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-14 - (2 - k) * 19, 11), new Vector2(14, 12 + k * 4));
            }
            s.ChevL = MakeChev(s.Inner, true, s.ChevBars, 0);
            s.ChevR = MakeChev(s.Inner, false, s.ChevBars, 2);
            // CPU / empty seat icon: a small die
            s.Dice = UiKit.Rect("Dice", s.Inner);
            UiKit.Place(s.Dice, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(11 + 11, 0), new Vector2(30, 30));
            s.DiceImgs[0] = UiKit.Box(s.Dice, "Body", new Color(1, 1, 1, 0.9f), UiKit.Pill, true, 8);
            UiKit.Stretch(s.DiceImgs[0].rectTransform);
            var pp = new[] { new Vector2(-8, 8), new Vector2(8, 8), new Vector2(0, 0), new Vector2(-8, -8), new Vector2(8, -8) };
            for (int k = 0; k < 5; k++)
            {
                s.DiceImgs[k + 1] = UiKit.Box(s.Dice, "Pip" + k, UiKit.Hex(0x2a0f45), UiKit.Disc);
                UiKit.Place(s.DiceImgs[k + 1].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pp[k], new Vector2(6, 6));
            }
            return s;
        }

        // a real chevron from two rotated bars
        static RectTransform MakeChev(RectTransform parent, bool left, Image[] bars, int at)
        {
            var rt = UiKit.Rect(left ? "ChevL" : "ChevR", parent);
            UiKit.Place(rt, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(left ? -64 : -18, 2), new Vector2(24, 30));
            for (int k = 0; k < 2; k++)
            {
                var b = UiKit.Box(rt, "Bar" + k, Gold, UiKit.Pill, true, 6);
                UiKit.Place(b.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(left ? -2 : 2, k == 0 ? 6 : -6), new Vector2(18, 6));
                b.rectTransform.localRotation = Quaternion.Euler(0, 0, (left ? 1 : -1) * (k == 0 ? 40 : -40));
                bars[at + k] = b;
            }
            return rt;
        }

        static Vector2 SeatPos(int i, int count)
        {
            float total = count * SeatW + (count - 1) * SeatGap;
            return new Vector2(-total / 2 + SeatW / 2 + i * (SeatW + SeatGap), -SeatTop);
        }

        Pill MakePill(RectTransform root, string name, string label, string key, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, float cap)
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
            b.Label.text = label;
            UiKit.Place(b.Label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(name == "Back" ? -14 : -40, 1), new Vector2(size.x - 60, size.y + 10));
            b.Chip = UiKit.Rect("Chip", b.Inner);
            var chipSize = name == "Back" ? new Vector2(30, 24) : new Vector2(76, 30);
            UiKit.Place(b.Chip, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(name == "Back" ? -8 : -14, 0), chipSize);
            var cf = UiKit.Box(b.Chip, "Fill", new Color(0.1f, 0.04f, 0.2f, 0.85f), UiKit.Pill, true, 24);
            UiKit.Stretch(cf.rectTransform);
            b.ChipText = UiKit.Text(b.Chip, "T", TextStyle.Label, 11f, Gold, italic: false);
            b.ChipText.characterSpacing = 3f;
            b.ChipText.text = key;
            UiKit.Place(b.ChipText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, chipSize);
            return b;
        }

        static Color ColorOf(int c) => new Color(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f);
        static float Damp(float a, float b, float rate, float dt) => Mathf.Lerp(a, b, 1 - Mathf.Exp(-rate * dt));
        static readonly string[] Levels = { "EASY", "NORMAL", "HARD", "EMPTY" };

        // ------------------------------------------------------------------ tick

        public override void Tick(float dt, ShellInput input)
        {
            var r = View.R;
            if (r == null) return;
            bool red = input.Reduced;
            float shown = Shown, age = Age;
            Group.alpha = UiKit.Clamp01(shown * 1.6f);
            bool party = r.Mode == "party";
            var stage = r.Stage;
            bool chars = stage == RosterStage.Chars, lineup = stage == RosterStage.Lineup;
            float comp = Mathf.Clamp01((600f - Screen.height) / 240f);
            bool small = Screen.height < 600;
            float ts = small ? 1.45f : 1f;      // text bump so nothing is under ~11 real px

            // ---- which character stands on stage
            int focus = r.Cursor;
            bool hoverCard = chars && input.Hover >= CardBase && input.Hover < CardBase + 8;
            if (hoverCard) focus = input.Hover - CardBase;   // the card under the pointer is previewed on the stage and the plate
            if (stage == RosterStage.Cpu || stage == RosterStage.Go)
            {
                var mine = r.Picks[0];
                if (mine != null) focus = Cast.IndexOf(mine);
            }
            FocusChar = Mathf.Clamp(focus, 0, Cast.All.Count - 1);
            // the stage follows the focused seat while choosing rivals: an empty seat is a lit empty floor, a CPU a die
            int seatMode = 0;
            if (lineup && r.SeatSel > 0) seatMode = r.Seat[Mathf.Clamp(r.SeatSel, 0, 3)] == RosterState.Off ? 1 : 2;
            StageMode = seatMode;
            _dieAmt = Damp(_dieAmt, seatMode == 2 ? 1 : 0, red ? 40 : 14, dt);
            _dieGroup.alpha = _dieAmt * UiKit.Clamp01(shown * 1.6f);
            _stageDie.gameObject.SetActive(_dieAmt > 0.01f);
            _stageDie.anchoredPosition = new Vector2(StageX, -(small ? 300 : 340) + (red ? 0 : Mathf.Sin(Time.unscaledTime * 2f) * 5f));
            _stageDie.localRotation = Quaternion.Euler(0, 0, red ? -6 : -6 + Mathf.Sin(Time.unscaledTime * 1.3f) * 4f);
            _stageDie.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, _dieAmt) * (small ? 0.9f : 1f);

            // ---- heading
            string head, sub, hint, action, key;
            switch (stage)
            {
                case RosterStage.Lineup: head = "PICK YOUR RIVALS"; sub = "you are P1 - up/down pick a seat - left/right sets the CPU level"; hint = "UP / DOWN  SEAT      LEFT / RIGHT  CPU LEVEL"; action = "CONTINUE"; key = "SPACE"; break;
                case RosterStage.Chars: head = "CHOOSE YOUR CHARACTER"; sub = "move with the arrows - space to lock in"; hint = "ARROWS  CHOOSE      SPACE  LOCK IN"; action = "LOCK IN"; key = "SPACE"; break;
                case RosterStage.Cpu: head = "CPU IS CHOOSING..."; sub = "they always take the good one"; hint = "CPU CHOOSING..."; action = "WAIT..."; key = ""; break;
                default:
                    head = party ? "LINEUP SET" : "READY";
                    sub = party ? "four games, one crown" : "pick a minigame next";
                    hint = party ? "SPACE  START THE PARTY" : "SPACE  PICK A GAME";
                    action = party ? "START PARTY" : "PICK A GAME"; key = "SPACE"; break;
            }
            if (stage == RosterStage.Lineup && r.Seat[1] == RosterState.Off && r.Seat[2] == RosterState.Off && r.Seat[3] == RosterState.Off) action = "SOLO";   // no rivals: say so
            if (small && stage == RosterStage.Lineup) sub = "you are P1 - pick a seat, set the CPU level";
            _head.text = head;
            bool flashing = !string.IsNullOrEmpty(View.Flash);
            _flashA = Damp(_flashA, flashing ? 1 : 0, red ? 40 : 18, dt);
            _sub.text = flashing ? View.Flash : sub;
            _sub.color = Color.Lerp(new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, 0.82f), Coral, _flashA);
            _hint.text = hint;
            float hEnter = red ? shown : UiKit.EaseOutCubic(age / 0.5f);
            _headGroup.alpha = UiKit.Clamp01(hEnter * 1.3f);
            _headRt.anchoredPosition = new Vector2(0, -18 + (1 - hEnter) * 30);
            _headRt.localScale = Vector3.one * Mathf.Lerp(1f, 1.18f, comp);
            if (comp > 0) _headRt.anchoredPosition = new Vector2(0, -18 + 16 * comp);
            bool tight = Screen.height < 720;
            bool subOff = tight && !small && !flashing;       // the short screens (640x360) keep a one-line instruction under the title
            _subGroup.alpha = subOff ? 0 : 1;
            _subPlate.color = new Color(0.05f, 0.03f, 0.12f, subOff ? 0 : small ? 0.8f : 0.55f);
            float subY = small ? -74f : -66f;
            _sub.rectTransform.anchoredPosition = new Vector2(0, subY);
            _subPlate.rectTransform.anchoredPosition = new Vector2(0, subY + 2);
            _sub.fontSize = UiKit.FontSizeForCap(small ? 17f : 15f);
            _hintGroup.alpha = 0;   // the instruction line under the heading carries the hint; the footer copy is gone

            // ---- cards
            bool shaking = View.ShakeT > 0;
            for (int i = 0; i < 8; i++)
            {
                var c = _cards[i];
                bool foc = chars && r.Cursor == i;
                bool taken = r.Taken(i);
                int by = taken ? r.TakenBy(i) : -1;
                bool selectable = chars;
                c.Focus = Damp(c.Focus, foc ? 1 : 0, red ? 40 : 16, dt);
                c.Hover = Damp(c.Hover, selectable && input.Hover == CardBase + i && !foc ? 1 : 0, red ? 40 : 18, dt);
                c.Press = Damp(c.Press, selectable && input.Pressed && input.Hover == CardBase + i ? 1 : 0, red ? 60 : 30, dt);
                if (by != c.LastPick) { if (by >= 0 && c.LastPick == -1 && !red) c.Pop = 1; c.LastPick = by; }
                c.Pop = Mathf.MoveTowards(c.Pop, 0, dt / 0.45f);
                float enter = red ? 1 : UiKit.BackOut((age - 0.12f - i * 0.045f) / 0.45f);
                c.Group.alpha = UiKit.Clamp01(enter * 2f) * (red ? shown : 1);
                float sx = shaking && View.ShakeCard == i && !red ? Mathf.Sin(View.ShakeT * 90f) * 7f : 0;
                var basePos = CardPos(i);
                float lift = 8f * c.Focus + 3f * c.Hover * (1 - c.Focus);
                c.Rt.anchoredPosition = new Vector2(basePos.x + sx, basePos.y + lift - (1 - Mathf.Min(1, enter)) * 50f);
                float hop = Mathf.Sin(Mathf.Clamp01(1 - c.Pop) * Mathf.PI) * 0.09f * (c.Pop > 0 ? 1 : 0);
                bool mine = !chars && !lineup && r.Picks[0] != null && Cast.IndexOf(r.Picks[0]) == i;
                float mineUp = mine ? 0.06f : 0f;
                c.Inner.localScale = Vector3.one * ((0.7f + 0.3f * Mathf.Min(1.1f, enter)) * (1 + 0.09f * c.Focus + mineUp + 0.035f * c.Hover + hop) * (1 - 0.06f * c.Press));
                float dim = mine ? 1f : chars ? (taken ? 0.7f : 1f) : (by >= 0 ? 1f : 0.7f);   // picked cards stay bright with their badge, the rest step back
                var port = View.Cast != null ? View.Cast.Portrait(i) : null;
                c.Portrait.texture = port;
                float pa = port != null ? 1 : 0;
                c.Portrait.color = new Color(dim, dim, dim, pa);
                c.Tint.color = Color.Lerp(ColorOf(Cast.All[i].Color) * 0.5f, ColorOf(Cast.All[i].Color), 0.3f) * new Color(dim, dim, dim, 1);
                bool bad = shaking && View.ShakeCard == i;
                c.Veil.color = new Color(bad ? 0.6f : 0.04f, bad ? 0.05f : 0.02f, bad ? 0.05f : 0.1f, bad ? 0.45f : (taken ? 0.4f : 0f));
                c.Name.color = Color.Lerp(new Color(1, 1, 1, 0.75f), Color.white, Mathf.Max(c.Focus, mine ? 1f : 0f));
                c.Name.fontSize = 17f * Mathf.Lerp(1f, 1.3f, small ? 1 : 0);
                var colr = ColorOf(Cast.All[i].Color);
                c.Glow.color = new Color(colr.r, colr.g, colr.b, mine ? 0.7f : 0.7f * c.Focus * (taken ? 0.4f : 1f));
                Color rim = Color.Lerp(new Color(1, 1, 1, 0.22f), new Color(0.55f, 0.95f, 1f, 1f), c.Hover);
                c.Ring.color = Color.Lerp(rim, new Color(1, 0.93f, 0.5f, 1f), Mathf.Max(c.Focus, mine ? 1f : 0f));
                c.Shadow.color = new Color(0, 0, 0, 0.5f * (1 - 0.5f * c.Press));
                c.BadgeFill.gameObject.SetActive(taken);
                c.Badge.text = by == 0 ? "YOU" : "P" + (by + 1);
                c.Badge.fontSize = by == 0 ? 11f : 15f;
                if (taken)
                {
                    c.BadgeFill.rectTransform.localScale = Vector3.one * (1 + c.Pop * 0.5f);
                }
            }

            // ---- character plate
            if (_infoChar != FocusChar) { _infoChar = FocusChar; _infoT = 0; }
            _infoT += dt;
            var def = Cast.All[FocusChar];
            var col = ColorOf(def.Color);
            _name.text = def.Name;
            _trait.text = def.Trait;
            _infoBar.color = col;
            _swatch[0].color = col; _swatch[1].color = ColorOf(def.Accent); _swatch[2].color = Color.Lerp(col, Color.white, 0.55f);
            float pop = red ? 1 : UiKit.BackOut(_infoT / 0.3f);
            _name.rectTransform.anchoredPosition = new Vector2(36 + (1 - Mathf.Min(1, pop)) * -14, small ? -4 : -12);
            _infoGroup.alpha = UiKit.Clamp01((age - 0.35f) * 3f) * (red ? shown : 1);
            _info.gameObject.SetActive(!small);   // no room for the plate on a short screen
            _info.anchoredPosition = new Vector2(GridLeft, -(GridTop + 2 * CardH + CardGap + (small ? 10 : 22)));
            _info.sizeDelta = new Vector2(478, small ? 64 : 120);
            _infoBar.rectTransform.sizeDelta = new Vector2(8, small ? 40 : 88);
            _name.rectTransform.sizeDelta = new Vector2(330, small ? 56 : 60);
            int tb = r.Taken(FocusChar) ? r.TakenBy(FocusChar) : -1;
            _status.text = tb == 0 && !chars ? "YOUR PICK" : tb >= 0 ? "TAKEN BY P" + (tb + 1) : (chars ? "FREE - SPACE TO LOCK IN" : "");
            _status.color = tb > 0 || (tb == 0 && chars) ? Coral : Gold;
            if (lineup)
            {
                int ss = Mathf.Clamp(r.SeatSel, 0, 3);
                int sv = r.Seat[ss];
                bool sYou = sv == RosterState.You, sOff = sv == RosterState.Off;
                _name.text = sYou ? "P1  YOU" : "P" + (ss + 1) + (sOff ? "  EMPTY" : "  CPU");
                string seatChar = r.Picks[ss] != null ? Cast.All[Mathf.Max(0, Cast.IndexOf(r.Picks[ss]))].Name : null;
                _trait.text = sYou ? (seatChar != null ? "PLAYING AS " + seatChar : "PLAYER ONE  -  PICK A CHARACTER NEXT") : sOff ? "NO RIVAL" : Levels[sv] + " LEVEL" + (seatChar != null ? "  -  " + seatChar : "");
                _status.text = sYou ? "YOU PLAY EVERY GAME" : sOff ? "RIGHT TO ADD A CPU" : sv == 0 ? "MISSES OFTEN" : sv == 1 ? "STEADY, FAIR FIGHT" : "NEAR PERFECT PLAY";
                _status.color = Gold;
                _infoBar.color = sYou ? Gold : sOff ? new Color(1, 1, 1, 0.3f) : Color.Lerp(Gold, UiKit.Hex(0xff7a3a), sv * 0.5f);
                for (int k = 0; k < 3; k++) _swatch[k].gameObject.SetActive(false);
            }
            else for (int k = 0; k < 3; k++) _swatch[k].gameObject.SetActive(false);   // unexplained colour dots: gone
            _trait.fontSize = 15f * ts; _status.fontSize = 14f * ts;
            _trait.gameObject.SetActive(!small); _status.gameObject.SetActive(!small);

            // ---- seats
            for (int i = 0; i < 4; i++)
            {
                var s = _seats[i];
                bool hidden = !party && i > 0;
                s.Show = Damp(s.Show, hidden ? 0 : 1, 30, dt);
                s.Rt.gameObject.SetActive(!hidden);
                if (hidden) { s.Rt.sizeDelta = Vector2.zero; continue; }
                s.Rt.sizeDelta = new Vector2(SeatW, SeatH);
                var pos = SeatPos(!party ? 0 : i, party ? 4 : 1);
                float seatScale = small ? 1.2f : 1f;
                bool off = r.Seat[i] == RosterState.Off;
                bool you = r.Seat[i] == RosterState.You;
                bool active = (lineup && r.SeatSel == i) || (chars && r.Active == i);
                s.Focus = Damp(s.Focus, active ? 1 : 0, red ? 40 : 14, dt);
                s.Hover = Damp(s.Hover, lineup && input.Hover == SeatBase + i && !active ? 1 : 0, red ? 40 : 18, dt);
                s.Press = Damp(s.Press, lineup && input.Pressed && input.Hover == SeatBase + i ? 1 : 0, red ? 60 : 30, dt);
                string pickId = r.Picks[i];
                if (pickId != s.LastPick) { if (pickId != null && !red) s.Pulse = 1; s.LastPick = pickId; }
                s.Pulse = Mathf.MoveTowards(s.Pulse, 0, dt / 0.5f);
                float enter = red ? 1 : UiKit.BackOut((age - 0.35f - i * 0.07f) / 0.45f);
                s.Group.alpha = UiKit.Clamp01(enter * 2f) * (red ? shown : 1) * (off ? 0.6f : 1f);
                s.Rt.anchoredPosition = new Vector2(pos.x * seatScale, pos.y + (small ? SeatTop - SmallSeatTop : 0) - (1 - Mathf.Min(1, enter)) * 60f);
                s.Rt.localScale = Vector3.one * seatScale;
                s.Inner.localScale = Vector3.one * ((1 + 0.05f * s.Focus + 0.025f * s.Hover + 0.05f * Mathf.Sin(Mathf.Clamp01(1 - s.Pulse) * Mathf.PI) * (s.Pulse > 0 ? 1 : 0)) * (1 - 0.05f * s.Press));
                s.Who.text = "P" + (i + 1) + (you ? "  YOU" : off ? "" : "  CPU");
                s.Who.gameObject.SetActive(true);
                s.Who.fontSize = UiKit.FontSizeForCap(small ? 16f : 13f);
                s.Who.rectTransform.anchoredPosition = new Vector2(72, small ? -5 : -8);
                bool chev = lineup && active && i > 0;
                s.Kind.text = you ? "PLAYER" : Levels[r.Seat[i]];
                s.Kind.fontSize = small ? (chev ? 19f : 24f) : 18f;
                s.Kind.rectTransform.sizeDelta = new Vector2(small ? (chev ? 82 : 150) : 86, 30);
                s.Kind.rectTransform.anchoredPosition = new Vector2(72, small ? 6 : 2);
                s.Pick.fontSize = small ? 22f : 13f;
                s.Kind.color = off ? new Color(1, 1, 1, 0.55f) : Color.white;
                // face
                int ci = pickId != null ? Cast.IndexOf(pickId) : -1;
                var tex = ci >= 0 && View.Cast != null ? View.Cast.Portrait(ci) : null;
                float faceA = 1f;
                if (tex == null && you && View.Cast != null)   // P1 not picked yet: the figure on stage, ghosted, instead of a '?'
                {
                    tex = View.Cast.Portrait(FocusChar); faceA = 0.5f;
                }
                s.Face.texture = tex; s.Face.color = new Color(1, 1, 1, tex != null ? faceA : 0);
                s.Q.gameObject.SetActive(ci < 0 && you && tex == null);
                s.Q.text = "?";
                s.Dice.gameObject.SetActive(ci < 0 && !you);
                float da = off ? 0.35f : 0.9f;
                s.DiceImgs[0].color = new Color(1, 1, 1, da);
                for (int k = 1; k < 6; k++) s.DiceImgs[k].gameObject.SetActive(!off);
                var seatCol = ci >= 0 ? ColorOf(Cast.All[ci].Color) : new Color(1, 1, 1, 0.5f);
                s.DiscRing.color = ci >= 0 ? seatCol : new Color(1, 1, 1, 0.35f);
                s.DiscFill.color = ci >= 0 ? Color.Lerp(seatCol, Color.black, 0.5f) : new Color(1, 1, 1, 0.08f);
                s.Pick.text = ci >= 0 ? Cast.All[ci].Name : (you && chars && active ? "PICKING..." : "");
                s.Pick.color = ci >= 0 ? Color.Lerp(seatCol, Color.white, 0.9f) : Gold;
                // level pips: easy 1, normal 2, hard 3
                int lvl = you || off ? 0 : r.Seat[i] + 1;
                for (int k = 0; k < 3; k++)
                    s.Pips[k].color = k < lvl ? Color.Lerp(Gold, UiKit.Hex(0xff7a3a), lvl == 3 ? 0.6f : 0f) : new Color(1, 1, 1, you || off ? 0f : 0.2f);
                bool arrows = lineup && active && i > 0;
                s.ChevL.gameObject.SetActive(arrows); s.ChevR.gameObject.SetActive(arrows);
                if (arrows)
                {
                    float w = red ? 0 : Mathf.Sin(Time.unscaledTime * 6f) * 2.5f;
                    float ky = small ? 6 : 2;
                    s.ChevL.anchoredPosition = new Vector2(-62 - w, ky); s.ChevR.anchoredPosition = new Vector2(-16 + w, ky);
                    if (small) { s.ChevL.anchoredPosition = new Vector2(-48 - w, ky); s.ChevR.anchoredPosition = new Vector2(-14 + w, ky); }
                }
                for (int k = 0; k < 3; k++)
                {
                    s.Pips[k].gameObject.SetActive(!(you || off));
                    // short screens: bigger pips (at least 6 real px)
                    UiKit.Place(s.Pips[k].rectTransform, new Vector2(1, 0), new Vector2(1, 0),
                        small ? new Vector2(-12 - (2 - k) * 24, 11) : new Vector2(-14 - (2 - k) * 19, 11),
                        small ? new Vector2(20, 15 + k * 5) : new Vector2(14, 12 + k * 4));
                }
                var ac = Color.Lerp(new Color(0.05f, 0.03f, 0.12f, 0.93f), new Color(0.2f, 0.14f, 0.38f, 0.96f), s.Hover);
                s.Fill.color = Color.Lerp(ac, new Color(0.24f, 0.17f, 0.08f, 0.97f), s.Focus);
                Color rim = Color.Lerp(new Color(1, 0.95f, 0.8f, 0.2f), new Color(0.55f, 0.95f, 1f, 1f), s.Hover);
                s.Ring.color = Color.Lerp(rim, new Color(1, 0.9f, 0.45f, 1f), s.Focus);
                s.Glow.color = new Color(1, 0.75f, 0.25f, 0.5f * s.Focus);
            }

            // ---- action and back
            bool actOn = stage != RosterStage.Cpu;
            DriveButton(_action, input, ActionBtn, actOn, PressFocus != 1, action, key, dt, red, age, 0.6f);
            DriveButton(_back, input, BackBtn, true, PressFocus == 1, "BACK", "X", dt, red, age, 0.8f);
            _action.Rt.anchoredPosition = new Vector2(StageX, small ? -448 : Mathf.Lerp(-506, -516, comp));
            _action.Rt.sizeDelta = actOn ? new Vector2(340, small ? 60 : 58) : Vector2.zero;
            _action.Chip.gameObject.SetActive(!string.IsNullOrEmpty(key));
            _action.Chip.sizeDelta = small ? new Vector2(110, 40) : new Vector2(76, 30);
            _action.ChipText.rectTransform.sizeDelta = _action.Chip.sizeDelta;
            _action.ChipText.fontSize = UiKit.FontSizeForCap(small ? 17f : 11f);
            _back.Chip.gameObject.SetActive(!small);
            _action.Label.rectTransform.anchoredPosition = new Vector2(small ? -44 : -40, 1);
            _back.Label.rectTransform.anchoredPosition = new Vector2(small ? 0 : -14, 1);
            _back.Rt.anchoredPosition = new Vector2(-72 - 60 * comp, 12 + 6 * comp);
            if (small) _back.Rt.anchoredPosition = new Vector2(-60, 8);
            _back.Inner.localScale *= Mathf.Lerp(1f, 1.3f, comp);
        }

        void DriveButton(Pill b, ShellInput input, int idx, bool on, bool primary, string label, string key, float dt, bool red, float age, float delay)
        {
            b.Label.text = label;
            b.ChipText.text = key;
            bool hov = on && input.Hover == idx;
            b.Hover = Damp(b.Hover, hov ? 1 : 0, red ? 40 : 18, dt);
            b.Press = Damp(b.Press, hov && input.Pressed ? 1 : 0, red ? 60 : 30, dt);
            b.Focus = Damp(b.Focus, primary && on ? 1 : 0, red ? 40 : 14, dt);
            float enter = red ? 1 : UiKit.BackOut((age - delay) / 0.45f);
            float pulse = primary && on && !red ? 1 + 0.018f * Mathf.Sin(Time.unscaledTime * 4f) : 1;
            b.Inner.localScale = Vector3.one * ((1 + 0.04f * b.Hover) * (1 - 0.06f * b.Press) * Mathf.Clamp01(enter) * pulse);
            Color top = Color.Lerp(new Color(0.11f, 0.06f, 0.27f, 0.96f), new Color(0.15f, 0.09f, 0.34f, 0.97f), b.Hover);
            Color bot = Color.Lerp(new Color(0.05f, 0.02f, 0.14f, 0.96f), new Color(0.07f, 0.03f, 0.18f, 0.97f), b.Hover);
            top = Color.Lerp(top, UiKit.Hex(0xffe27a), b.Focus); bot = Color.Lerp(bot, UiKit.Hex(0xff9a2e), b.Focus);
            float dark = 1 - 0.38f * b.Press;
            b.Grad.Set(new Color(top.r * dark, top.g * dark, top.b * dark, top.a), new Color(bot.r * dark, bot.g * dark, bot.b * dark, bot.a));
            Color rim = Color.Lerp(new Color(1, 0.95f, 0.78f, 0.7f), new Color(0.55f, 0.95f, 1f, 1f), b.Hover);
            b.Ring.color = Color.Lerp(rim, new Color(1, 0.98f, 0.82f, 1f), b.Focus);
            b.Label.color = Color.Lerp(Color.white, UiKit.Hex(0x2a0f45), b.Focus);
            b.Glow.color = new Color(1f, 0.75f, 0.25f, 0.5f * b.Focus);
            b.Shadow.color = new Color(0, 0, 0, 0.5f * (1 - 0.6f * b.Press));
        }
    }
}
