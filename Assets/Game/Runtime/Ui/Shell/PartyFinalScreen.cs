// The party's wrap-up in uGUI: the winner (or the tied winners) on a podium under a spotlight with a crown, a
// trophy and falling confetti, everyone else on lower blocks with their portraits and points, the stat line, and
// PLAY AGAIN / MENU. A single winner stands on stage as the live cast figure; tied winners are crowned medals.
//
// Buttons, in order: 0 play again, 1 menu.

using System.Collections.Generic;
using Bonanza.Core;
using Bonanza.HudKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Bonanza.Shell.PartyKit;

namespace Bonanza.Shell
{
    public struct PartyFinalView
    {
        public string Headline, Tiebreak, StatLine, BestLine;
        public List<PartyBoardRow> Rows;     // best first
        public CastStage Cast;
    }

    public sealed class PartyFinalScreen : ShellScreen
    {
        public PartyFinalView View;
        public const int AgainBtn = 0, MenuBtn = 1;
        /// Where the stage figure stands (reference px), read by the host to place the cast.
        public const float FigureFeet = 458f, FigureHeight = 236f, ColW = 190, ColStep = 204, BaseY = 548;
        static readonly float[] BlockH = { 124, 84, 62, 48 };
        static readonly float[] SmallBlockH = { 134, 100, 94, 92 };   // small screens stack name, points and chips in fixed rows, so every plate is tall enough for them
        static readonly int[] Identity = { 0, 1, 2, 3 };
        static readonly int[][] ColOf = { new[] { 0 }, new[] { 1, 0 }, new[] { 1, 0, 2 }, new[] { 1, 0, 2, 3 } };

        /// The single winner's cast index, or -1 (tie: medals, no live figure).
        public int FigureChar { get; private set; } = -1;
        /// The figure's x from the screen centre (reference px).
        public float FigureX { get; private set; }

        sealed class Col
        {
            public RectTransform Rt; public CanvasGroup Group;
            public Image Fill, Ring, Shine, YouFill, YouRing, Badge, BadgeRing;
            public TextMeshProUGUI Num, Name, Pts, You, Ph;
            public RectTransform YouRt, TieRt;
            public Image TieFill; public TextMeshProUGUI Tie;
            public Face Face;
        }

        readonly Col[] _cols = new Col[4];
        TextMeshProUGUI _label, _headline, _stat, _best;
        RectTransform _headRt, _statRt, _trophy, _crownB;
        CanvasGroup _headGroup, _statGroup;
        Image _spot;
        Pill _again, _menu;
        const int ConfettiN = 72;
        RectTransform _confRt; readonly Image[] _conf = new Image[ConfettiN];
        readonly float[] _cx = new float[ConfettiN], _cp = new float[ConfettiN], _cs = new float[ConfettiN], _cw = new float[ConfettiN], _cr = new float[ConfettiN];
        RectTransform[] _crowns = new RectTransform[2];
        RectTransform _fist; Image _fistImg; Image[] _fistParts;

        protected override void Build(RectTransform root)
        {
            _spot = UiKit.Box(root, "Spot", new Color(1, 0.85f, 0.4f, 0.0f), UiKit.Soft);
            UiKit.Place(_spot.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -330), new Vector2(760, 760));

            _confRt = UiKit.Rect("Confetti", root);
            UiKit.Place(_confRt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(10, 10));
            var cols = new[] { UiKit.Hex(0xffd93d), UiKit.Hex(0xff5d73), UiKit.Hex(0x4dd6ff), UiKit.Hex(0x9ee87a), UiKit.Hex(0xc08cff), Color.white, UiKit.Hex(0xff9f45) };
            var rng = new System.Random(77);
            for (int i = 0; i < ConfettiN; i++)
            {
                _cx[i] = (float)rng.NextDouble() * 2 - 1; _cp[i] = (float)rng.NextDouble(); _cs[i] = 0.6f + (float)rng.NextDouble() * 0.9f;
                _cw[i] = 8 + (float)rng.NextDouble() * 8; _cr[i] = (float)rng.NextDouble() * 360;
                _conf[i] = UiKit.Box(_confRt, "C" + i, cols[i % cols.Length]);
                UiKit.Place(_conf[i].rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(_cw[i], _cw[i] * 0.55f));
            }

            _headRt = UiKit.Rect("Head", root);
            UiKit.Place(_headRt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(900, 100));
            _headGroup = _headRt.gameObject.AddComponent<CanvasGroup>();
            _label = UiKit.Text(_headRt, "Label", TextStyle.Label, 15f, Gold, italic: false);
            _label.characterSpacing = 5f;
            UiKit.Place(_label.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(900, 24));
            _headline = UiKit.Text(_headRt, "Headline", TextStyle.Display, 50f, Gold);
            UiKit.Place(_headline.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -26), new Vector2(900, 76));

            // podium
            for (int i = 0; i < 4; i++) _cols[i] = MakeCol(root, i);

            _trophy = MakeTrophy(root, "Trophy", 110);
            _crownB = MakeCrown(root, "Crown");
            _fist = UiKit.Rect("Fist", root);
            UiKit.Place(_fist, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(34, 34));
            // a shaded fist: the palm, a darker lower half, four knuckles across the top, a thumb across the front
            var fdark = UiKit.Box(_fist, "Shade", new Color(0.18f, 0.08f, 0.02f, 0.9f), UiKit.Disc);
            UiKit.Place(fdark.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1.5f, -2f), new Vector2(34, 32));
            _fistImg = UiKit.Box(_fist, "Palm", Color.white, UiKit.Disc);
            UiKit.Place(_fistImg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1f), new Vector2(31, 29));
            _fistParts = new Image[5];
            for (int k = 0; k < 4; k++)
            {
                var kn = UiKit.Box(_fist, "Finger" + k, Color.white, UiKit.Disc);
                UiKit.Place(kn.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-10.5f + k * 7f, 9.5f), new Vector2(8.5f, 11));
                var kr = UiKit.Box(kn.rectTransform, "Line", new Color(0.18f, 0.08f, 0.02f, 0.8f), UiKit.Ring); UiKit.Stretch(kr.rectTransform);
                _fistParts[k] = kn;
            }
            var th = UiKit.Box(_fist, "Thumb", Color.white, UiKit.Disc);
            UiKit.Place(th.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-9f, -4f), new Vector2(14, 10));
            th.rectTransform.localRotation = Quaternion.Euler(0, 0, 25);
            var thr = UiKit.Box(th.rectTransform, "Line", new Color(0.18f, 0.08f, 0.02f, 0.8f), UiKit.Ring); UiKit.Stretch(thr.rectTransform);
            _fistParts[4] = th;
            _crowns[0] = MakeCrown(root, "CrownA"); _crowns[1] = MakeCrown(root, "CrownB");

            _statRt = UiKit.Rect("Stat", root);
            UiKit.Place(_statRt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -BaseY - 8), new Vector2(560, 46));
            _statGroup = _statRt.gameObject.AddComponent<CanvasGroup>();
            var sp = UiKit.Box(_statRt, "Plate", new Color(0.05f, 0.03f, 0.12f, 1f), UiKit.Pill, true, 24);
            UiKit.Stretch(sp.rectTransform);
            var spr = UiKit.Box(_statRt, "Ring", new Color(1, 0.95f, 0.8f, 0.45f), UiKit.PillRing(5), true, 24);
            UiKit.Stretch(spr.rectTransform);
            _stat = UiKit.Text(_statRt, "Stat", TextStyle.Display, 17f, Color.white);
            UiKit.Place(_stat.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -5), new Vector2(540, 26));
            _best = UiKit.Text(_statRt, "Best", TextStyle.Label, 12f, UiKit.Cream, italic: false);
            _best.characterSpacing = 3f;
            UiKit.Place(_best.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 5), new Vector2(540, 18));

            _again = MakePill(root, "Again", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-120, -628), new Vector2(320, 56), 24f, new Vector2(76, 30));
            _menu = MakePill(root, "Menu", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(165, -628), new Vector2(230, 56), 22f, new Vector2(56, 28));
            Buttons.Add(_again.Rt);
            Buttons.Add(_menu.Rt);
        }

        Col MakeCol(RectTransform root, int i)
        {
            var c = new Col();
            c.Rt = UiKit.Rect("Col" + i, root);
            UiKit.Place(c.Rt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(ColW, 160));
            c.Group = c.Rt.gameObject.AddComponent<CanvasGroup>();
            var sh = UiKit.Box(c.Rt, "Shadow", new Color(0, 0, 0, 0.45f), UiKit.Pill, true, 20);
            UiKit.Stretch(sh.rectTransform); sh.rectTransform.offsetMin = new Vector2(-2, -6); sh.rectTransform.offsetMax = new Vector2(2, -2);
            c.Fill = UiKit.Box(c.Rt, "Fill", Color.white, UiKit.Pill, true, 20);
            UiKit.Stretch(c.Fill.rectTransform);
            c.Fill.gameObject.AddComponent<UiGradient>();
            c.Ring = UiKit.Box(c.Rt, "Ring", new Color(1, 0.95f, 0.8f, 0.3f), UiKit.PillRing(5), true, 20);
            UiKit.Stretch(c.Ring.rectTransform);
            c.Badge = UiKit.Box(c.Rt, "Badge", new Color(0.12f, 0.07f, 0.3f), UiKit.Disc);
            c.BadgeRing = UiKit.Box(c.Rt, "BadgeRing", new Color(1, 0.95f, 0.8f, 0.9f), UiKit.Ring);
            c.Num = UiKit.Text(c.Rt, "Num", TextStyle.Display, 34f, Ink);
            UiKit.Place(c.Num.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 1), new Vector2(54, 56));
            c.Ph = UiKit.Text(c.Rt, "Ph", TextStyle.Display, 34f, new Color(0.12f, 0.06f, 0.3f, 0.55f), TextAlignmentOptions.Center);
            UiKit.Stretch(c.Ph.rectTransform); c.Ph.text = "?";
            c.Name = UiKit.Text(c.Rt, "Name", TextStyle.Display, 20f, Color.white, TextAlignmentOptions.Left);
            c.Name.enableAutoSizing = true; c.Name.fontSizeMin = 10f; c.Name.fontSizeMax = UiKit.FontSizeForCap(20f);
            UiKit.Place(c.Name.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(64, 11), new Vector2(120, 26));
            c.Pts = UiKit.Text(c.Rt, "Pts", TextStyle.Label, 13f, UiKit.Cream, TextAlignmentOptions.Left, italic: false);
            c.Pts.characterSpacing = 1f;
            UiKit.Place(c.Pts.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(64, -12), new Vector2(124, 20));
            c.YouRt = UiKit.Rect("You", c.Rt);
            UiKit.Place(c.YouRt, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-34, 0), new Vector2(48, 22));
            c.YouFill = UiKit.Box(c.YouRt, "Fill", Gold, UiKit.Pill, true, 22);
            UiKit.Stretch(c.YouFill.rectTransform);
            c.YouRing = UiKit.Box(c.YouRt, "Ring", new Color(0.16f, 0.06f, 0f, 0.9f), UiKit.PillRing(3), true, 22);
            UiKit.Stretch(c.YouRing.rectTransform);
            c.You = UiKit.Text(c.YouRt, "T", TextStyle.Display, 12f, Ink);
            c.You.text = "YOU";
            UiKit.Place(c.You.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(50, 22));
            c.TieRt = UiKit.Rect("Tie", c.Rt);
            UiKit.Place(c.TieRt, new Vector2(0, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44, 20));
            c.TieFill = UiKit.Box(c.TieRt, "Fill", new Color(0.16f, 0.06f, 0f), UiKit.Pill, true, 22);
            UiKit.Stretch(c.TieFill.rectTransform);
            c.Tie = UiKit.Text(c.TieRt, "T", TextStyle.Display, 12f, Gold);
            c.Tie.text = "TIE";
            UiKit.Place(c.Tie.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(50, 22));
            c.Face = MakeFace(root, "Face" + i, 76);
            c.Face.Rt.anchorMin = c.Face.Rt.anchorMax = new Vector2(0.5f, 1);
            c.Face.Ring.rectTransform.anchorMin = c.Face.Ring.rectTransform.anchorMax = new Vector2(0.5f, 1);
            return c;
        }

        // ------------------------------------------------------------------ tick

        /// The podium waits for the cast to be drawn (as the hub does): its cards never show alone on a dark stage.
        public override bool Ready => View.Cast == null || View.Cast.AllBaked;

        public override void Tick(float dt, ShellInput input)
        {
            var rows = View.Rows;
            if (rows == null || rows.Count == 0) return;
            bool red = input.Reduced;
            float age = Age, shown = Shown, now = Time.unscaledTime;
            Group.alpha = UiKit.Clamp01(shown * 1.6f);
            bool small = Screen.height < 600;
            float ts = small ? 1.4f : 1f;
            int n = Mathf.Min(rows.Count, 4);
            int winners = 0;
            for (int i = 0; i < n; i++) if (rows[i].Place == 1) winners++;
            bool single = winners == 1;
            FigureChar = single ? Mathf.Max(0, Cast.IndexOf(rows[0].Char)) : -1;
            var order = winners >= 2 ? Identity : ColOf[n - 1];   // a shared crown stands left to right in standing order, matching the headline

            // the plates rise last place first; the winner's title, crown, cup and confetti wait for the first-place plate
            float firstUp = 0.25f + (n - 1) * 0.14f + 0.45f;

            // ---- heading
            float he = red ? 1 : UiKit.EaseOutCubic((age - firstUp) / 0.5f);
            _headGroup.alpha = UiKit.Clamp01(he * 1.4f) * (red ? shown : 1);
            _headRt.anchoredPosition = new Vector2(0, -14 + (1 - he) * 24);
            _label.text = View.Tiebreak != null ? "PARTY COMPLETE   -   " + View.Tiebreak : "PARTY COMPLETE";
            _label.fontSize = UiKit.FontSizeForCap(15f * ts);
            _headline.text = View.Headline ?? "";
            float hs = View.Headline != null && View.Headline.Length > 22 ? 36f : View.Headline != null && View.Headline.Length > 15 ? 44f : 52f;
            _headline.fontSize = UiKit.FontSizeForCap(hs);
            float hp = red ? 1 : 1 + 0.025f * Mathf.Sin(now * 2.2f);
            _headline.rectTransform.localScale = Vector3.one * hp;

            // ---- spotlight
            float sp = red ? 1 : UiKit.EaseOutCubic((age - 0.5f) / 0.8f);
            FigureX = single ? ColCenter(order, 0, n) : 0;
            _spot.rectTransform.anchoredPosition = new Vector2(FigureX, -300);
            _spot.color = new Color(1, 0.85f, 0.4f, 0.14f * sp * (red ? 1 : 0.9f + 0.1f * Mathf.Sin(now * 1.7f)));

            // ---- podium
            for (int i = 0; i < 4; i++)
            {
                var c = _cols[i];
                bool on = i < n;
                c.Rt.gameObject.SetActive(on);
                c.Face.Rt.gameObject.SetActive(on); c.Face.Ring.gameObject.SetActive(on);
                if (!on) continue;
                var d = rows[i];
                int col = order[i];
                float cx = ColCenter(order, i, n);
                float bh = (small ? SmallBlockH : BlockH)[Mathf.Clamp(d.Place - 1, 0, 3)];
                bool win = d.Place == 1;
                // rise from the floor, last place first
                float rise = red ? 1 : UiKit.BackOut((age - 0.25f - (n - 1 - i) * 0.14f) / 0.55f, 1.0f);
                float h = Mathf.Max(0.01f, bh * Mathf.Clamp(rise, 0f, 1.08f));
                c.Rt.sizeDelta = new Vector2(ColW, h);
                c.Rt.anchoredPosition = new Vector2(cx, -(BaseY - h));
                c.Group.alpha = UiKit.Clamp01(rise * 3f) * (red ? shown : 1);
                bool you = !d.IsCpu;
                var g = c.Fill.GetComponent<UiGradient>();
                Color top = win ? UiKit.Hex(0xffe27a) : d.Place == 2 ? UiKit.Hex(0xe6e9f0) : d.Place == 3 ? UiKit.Hex(0xe8a56a) : UiKit.Hex(0x7a74b8);
                Color bot = win ? UiKit.Hex(0xe58a1f) : d.Place == 2 ? UiKit.Hex(0x8c93a6) : d.Place == 3 ? UiKit.Hex(0x9c5a2c) : UiKit.Hex(0x3a3478);
                g.Set(top, bot);
                c.Ring.color = you ? new Color(1, 1, 1, 0.95f) : new Color(1, 1, 1, 0.35f);
                c.Num.text = UiKit.Int(d.Place);
                c.Num.fontSize = UiKit.FontSizeForCap(26f);
                c.Num.color = win ? new Color(0.45f, 0.2f, 0.02f) : new Color(0.12f, 0.06f, 0.3f);
                c.Name.text = d.Name;
                c.Pts.text = $"{d.Points} PTS";
                c.Pts.fontSize = UiKit.FontSizeForCap(small ? 20f : 14f);
                Color ink = win ? new Color(0.16f, 0.06f, 0.0f) : d.Place == 2 ? new Color(0.14f, 0.15f, 0.24f) : Color.white;
                c.Pts.color = ink;
                c.Name.color = ink;
                c.Name.fontSizeMax = UiKit.FontSizeForCap(20f * (small ? 1.3f : 1f));
                c.Name.fontSizeMin = small ? UiKit.FontSizeForCap(20f) : 10f;
                c.YouRt.gameObject.SetActive(you);
                // portrait: sits on the block (the single winner stands on stage instead)
                bool figure = win && single;
                float fsz = win ? (single ? 80 : 112) : 62;
                float lift = small ? 8f : 0f;   // small: the portrait stands clear above its plate, never on top of the name
                float fy = -(BaseY - h) + fsz * 0.5f - 2 + lift;
                if (figure) { cx -= 84; fsz = small ? 66 : fsz; fy = small ? -(BaseY - h) + fsz * 0.5f - 2 + lift : -(BaseY - h) + 22; }   // small: the avatar sits on the card's edge, not cut into it
                float pillH = small ? 24f : 20f, pillW = small ? 58f : 44f;
                c.You.fontSize = UiKit.FontSizeForCap(small ? 17f : 12f);
                c.Tie.fontSize = UiKit.FontSizeForCap(small ? 17f : 12f);
                c.YouRt.anchorMin = c.YouRt.anchorMax = new Vector2(0, 1);
                c.YouRt.sizeDelta = new Vector2(pillW, pillH);
                c.TieRt.gameObject.SetActive(d.Tied);
                c.TieRt.anchorMin = c.TieRt.anchorMax = new Vector2(0, 1);
                float tieW = small ? 50f : pillW * 0.8f;
                c.TieRt.sizeDelta = new Vector2(tieW, pillH);
                c.TieRt.anchoredPosition = new Vector2(ColW - 10 - tieW * 0.5f, -(win ? 8 : 10) - pillH * 0.5f);
                var nrt = c.Name.rectTransform; var prt = c.Pts.rectTransform;
                c.Badge.gameObject.SetActive(small); c.BadgeRing.gameObject.SetActive(small);
                if (small)
                {
                    // stacked rows with fixed slots: the rank in a corner badge straddling the plate's edge, the name, the points,
                    // then one bottom row for the YOU and TIE chips side by side (they always fit in the plate's width)
                    float tw = ColW - 36f, edgeY = h * 0.5f;
                    c.Name.alignment = TextAlignmentOptions.Left; c.Pts.alignment = TextAlignmentOptions.Left;
                    nrt.sizeDelta = new Vector2(tw - 28f, 26); prt.sizeDelta = new Vector2(tw - 28f, 22);
                    float ny = win ? 30f : 22f, py = win ? 58f : 47f;   // the tallest plate spaces its rows out
                    nrt.anchoredPosition = new Vector2(58, edgeY - ny); prt.anchoredPosition = new Vector2(58, edgeY - py);
                    c.Name.fontSizeMax = UiKit.FontSizeForCap(win ? 30f : 28f); c.Name.fontSizeMin = UiKit.FontSizeForCap(22f);
                    c.Pts.fontSize = UiKit.FontSizeForCap(win ? 22f : 21f);
                    var br = c.Badge.rectTransform;
                    br.anchorMin = br.anchorMax = new Vector2(0, 0.5f); br.pivot = new Vector2(0, 0.5f);
                    br.sizeDelta = new Vector2(34, 34); br.anchoredPosition = new Vector2(8, edgeY - (win ? 34f : 26f));
                    var brr = c.BadgeRing.rectTransform;
                    brr.anchorMin = brr.anchorMax = br.anchorMin; brr.pivot = br.pivot; brr.sizeDelta = br.sizeDelta; brr.anchoredPosition = br.anchoredPosition;
                    c.Badge.color = win ? UiKit.Gold : new Color(0.86f, 0.9f, 1f);
                    c.Num.alignment = TextAlignmentOptions.Center;
                    c.Num.fontSize = UiKit.FontSizeForCap(22f); c.Num.color = UiKit.Ink;
                    var nr = c.Num.rectTransform;
                    nr.anchorMin = nr.anchorMax = br.anchorMin; nr.pivot = br.pivot;
                    nr.sizeDelta = new Vector2(34, 34);
                    nr.anchoredPosition = br.anchoredPosition;
                    float chipY = -(py + 11f + 6f + pillH * 0.5f);   // right under the points row, so the tag never floats in an empty band
                    c.YouRt.anchoredPosition = new Vector2(10 + pillW * 0.5f, chipY);
                    c.TieRt.anchoredPosition = new Vector2(10 + (you ? pillW + 6f : 0f) + tieW * 0.5f, chipY);
                    if (win)
                    {
                        // the first-place plate is the tallest: its tags sit centred on the bottom row, clear of the name and points above
                        float total = (you ? pillW : 0f) + (d.Tied ? (you ? 4f : 0f) + tieW : 0f), x0 = (ColW - total) * 0.5f;
                        c.YouRt.anchoredPosition = new Vector2(x0 + pillW * 0.5f, chipY);
                        c.TieRt.anchoredPosition = new Vector2(x0 + (you ? pillW + 4f : 0f) + tieW * 0.5f, chipY);
                    }
                }
                else if (win)
                {
                    // first place: number top-left, the name and points centred and the biggest text on the podium, the YOU tag on the bottom row
                    c.Name.alignment = TextAlignmentOptions.Center; c.Pts.alignment = TextAlignmentOptions.Center;
                    nrt.sizeDelta = new Vector2(176, 40); prt.sizeDelta = new Vector2(176, 22);
                    nrt.anchoredPosition = new Vector2(ColW * 0.5f - 88f, h * 0.5f - 56);
                    prt.anchoredPosition = new Vector2(ColW * 0.5f - 88f, h * 0.5f - 82);
                    c.Name.fontSizeMax = UiKit.FontSizeForCap(30f);
                    c.Name.fontSizeMin = UiKit.FontSizeForCap(16f);
                    c.Pts.fontSize = UiKit.FontSizeForCap(small ? 18f : 15f);
                    c.Num.rectTransform.sizeDelta = new Vector2(54, 34); c.Num.alignment = TextAlignmentOptions.Center;
                    c.Num.fontSize = UiKit.FontSizeForCap(22f);
                    c.Num.rectTransform.anchoredPosition = new Vector2(ColW * 0.5f - 27f, h * 0.5f - 12f);   // its own row above the name
                    c.YouRt.anchoredPosition = new Vector2(ColW * 0.5f, -(h - 9 - pillH * 0.5f));
                }
                else
                {
                    c.Name.alignment = TextAlignmentOptions.Left; c.Pts.alignment = TextAlignmentOptions.Left;
                    nrt.sizeDelta = new Vector2(120, 26); prt.sizeDelta = new Vector2(124, 20);
                    nrt.anchoredPosition = new Vector2(64, 11); prt.anchoredPosition = new Vector2(64, -12);
                    c.Name.fontSizeMax = UiKit.FontSizeForCap(20f * (small ? 1.3f : 1f));
                    c.Name.fontSizeMin = small ? UiKit.FontSizeForCap(20f) : 10f;
                    c.Num.rectTransform.sizeDelta = new Vector2(54, 56); c.Num.alignment = TextAlignmentOptions.Center;
                    c.Num.rectTransform.anchoredPosition = new Vector2(8, 1);
                    // a short card has the tag at its right end, a taller one in the bottom-right corner (clear of the name)
                    bool low = bh < 75f;
                    c.YouRt.anchoredPosition = low ? new Vector2(ColW - 6 - pillW * 0.5f, -h * 0.5f) : new Vector2(ColW - 6 - pillW * 0.5f, -(h - 6 - pillH * 0.5f));
                }
                bool settled = red || rise >= 0.8f;   // the plate's words and tags wait for it to be tall enough to hold them
                c.Num.gameObject.SetActive(settled); c.Name.gameObject.SetActive(settled); c.Pts.gameObject.SetActive(settled);
                c.Ph.gameObject.SetActive(!settled);   // a rising plate shows a question mark until its card has a name
                c.Ph.fontSize = UiKit.FontSizeForCap(small ? 30f : 34f);
                if (!small && rise < 0.97f && !red) { c.YouRt.gameObject.SetActive(false); c.TieRt.gameObject.SetActive(false); }   // the tall plate's bottom tag waits until the plate is full height
                if (!settled) { c.YouRt.gameObject.SetActive(false); c.TieRt.gameObject.SetActive(false); c.Badge.gameObject.SetActive(false); c.BadgeRing.gameObject.SetActive(false); }
                c.YouFill.color = UiKit.Hex(0xfff4d6);   // a light tag with dark text, readable on gold and silver alike
                c.YouRing.color = new Color(0.16f, 0.06f, 0f, 0.9f);
                c.You.color = new Color(0.16f, 0.06f, 0f);
                float fa = red ? shown : UiKit.Clamp01((rise - 0.3f) * 2f);
                SetFace(c.Face, d.Char, View.Cast, new Vector2(cx, fy), fsz, fa);
                if (win && !single && i < 2)
                {
                    float cs = 0.9f * (fsz / 120f);
                    var cr = _crowns[i];
                    cr.gameObject.SetActive(true);
                    cr.anchoredPosition = new Vector2(cx, fy + fsz * 0.5f - 28);
                    float bob = red ? 0 : Mathf.Sin(now * 2.4f + i) * 1.5f;
                    cr.anchoredPosition += new Vector2(0, bob);
                    cr.localScale = Vector3.one * (cs * Mathf.Clamp01(UiKit.BackOut((age - firstUp - 0.2f - i * 0.15f) / 0.5f)));
                    cr.localRotation = Quaternion.Euler(0, 0, i == 0 ? -6 : 6);
                }
            }
            for (int k = winners; k < 2; k++) _crowns[k].gameObject.SetActive(false);
            if (single) { _crowns[0].gameObject.SetActive(false); _crowns[1].gameObject.SetActive(false); }

            // ---- crown over the stage figure, trophy beside it
            float pop = red ? 1 : UiKit.BackOut((age - firstUp - 0.15f) / 0.5f);
            _crownB.gameObject.SetActive(single);
            _trophy.gameObject.SetActive(true);
            if (single)
            {
                float rel = View.Cast != null ? View.Cast.FigureRel : 1f;
                float headY = FigureFeet - FigureHeight * rel;
                float bob = red ? 0 : Mathf.Sin(now * 2.6f) * 1.5f;
                // the crown rides the head bone when the rig has one, else the nominal head height
                Vector2 head = default;
                float cxn = ColCenter(order, 0, n);
                bool onHead = View.Cast != null && View.Cast.TryHeadRef(out head);
                float crownY = onHead ? head.y - 3f : headY + 36f - (View.Cast != null ? View.Cast.HopRef : 0f);
                _crownB.anchoredPosition = new Vector2(onHead ? head.x : cxn, -crownY + bob);
                _crownB.localScale = Vector3.one * ((small ? 0.84f : 0.72f) * Mathf.Clamp01(pop));
                _crownB.localRotation = Quaternion.Euler(0, 0, 0);
                // held up in the raised hand when the cheer has the arms up (its foot in a fist), else standing on the first-place block
                _heldOn = true; _heldPop = pop; _heldCx = cxn;
                bool held = PlaceHeld();
                View.Cast.AfterPose = () => PlaceHeld();   // the hand moves in the cast's LateUpdate: re-read it there so the grip never lags a frame
                _trophy.localScale = Vector3.one * (HeldScale * Mathf.Clamp01(pop));
                _trophy.SetAsLastSibling();   // in front of the crown
                if (held) _fist.SetAsLastSibling();   // the fist closes over the foot
            }
            else
            {
                _heldOn = false;
                if (View.Cast != null) View.Cast.AfterPose = null;
                _fist.gameObject.SetActive(false);
                // a shared crown: the cup stands on the stat plate, centred between the winners, not floating
                float mx = 0;
                for (int k = 0; k < winners; k++) mx += ColCenter(order, k, n) / winners;
                // the cup stands centred above the gap between the two winners' plates, clear of both
                float topEdge = BaseY - (small ? SmallBlockH[0] : BlockH[0]);
                _trophy.anchoredPosition = new Vector2(mx, -(topEdge - 6f));
                _trophy.localScale = Vector3.one * (0.75f * Mathf.Clamp01(pop));
                _trophy.SetAsLastSibling();
            }
            _trophy.localRotation = Quaternion.Euler(0, 0, (red ? 0 : Mathf.Sin(now * 1.4f) * 3f) + (_heldOn ? _heldLean : 0f));

            // ---- confetti
            _confRt.gameObject.SetActive(!red);
            if (!red)
            {
                float fall = UiKit.Clamp01((age - firstUp) / 0.4f);
                for (int i = 0; i < ConfettiN; i++)
                {
                    float t = now * 55f * _cs[i] / 760f + _cp[i];
                    float y = (t - Mathf.Floor(t)) * 780f - 30f;
                    float x = _cx[i] * 760f + Mathf.Sin(now * 1.4f * _cs[i] + _cp[i] * 20f) * 30f;
                    var rt = _conf[i].rectTransform;
                    rt.anchoredPosition = new Vector2(x, -y);
                    rt.localRotation = Quaternion.Euler(0, 0, _cr[i] + now * 120f * _cs[i]);
                    float flip = Mathf.Abs(Mathf.Cos(now * 3f * _cs[i] + _cp[i] * 9f));
                    rt.localScale = new Vector3(1, 0.25f + 0.75f * flip, 1);
                    var cc = _conf[i].color; cc.a = fall; _conf[i].color = cc;
                }
            }

            // ---- stat plate
            float se = red ? 1 : UiKit.EaseOutCubic((age - firstUp - 0.1f) / 0.5f);
            _statGroup.alpha = UiKit.Clamp01(se) * (red ? shown : 1);
            _stat.text = !single ? $"TIED ON {rows[0].Points} PTS" : View.StatLine ?? "";
            _best.text = View.BestLine ?? "";
            _best.gameObject.SetActive(!string.IsNullOrEmpty(View.BestLine));
            bool hasBest = !string.IsNullOrEmpty(View.BestLine);
            _statRt.sizeDelta = new Vector2(560, hasBest ? (small ? 64 : 50) : (small ? 40 : 34));
            _stat.rectTransform.anchoredPosition = new Vector2(0, hasBest ? -5 : -4);
            _stat.fontSize = UiKit.FontSizeForCap(17f * (small ? 1.2f : 1f));
            _best.fontSize = UiKit.FontSizeForCap(small ? 18f : 12f);
            _best.rectTransform.anchoredPosition = new Vector2(0, small ? 6 : 5);
            _statRt.anchoredPosition = new Vector2(0, -BaseY - 8 + (1 - se) * -10);

            // ---- buttons
            DriveButton(_again, input, AgainBtn, PressFocus != 1, "PLAY AGAIN", "SPACE", dt, red, age - firstUp - 0.2f, !small);
            DriveButton(_menu, input, MenuBtn, PressFocus == 1, "MENU", "ESC", dt, red, age - firstUp - 0.35f, !small);
            _again.Label.rectTransform.anchoredPosition = new Vector2(small ? 0 : -42, 1);
            _menu.Label.rectTransform.anchoredPosition = new Vector2(small ? 0 : -30, 1);
            _menu.Inner.localScale *= Mathf.Lerp(1f, 1.1f, small ? 1 : 0);
        }

        // the cup's parts are laid out from the rect centre (55 above its bottom pivot): the stem sits 83 units above the pivot
        const float StemUp = 83f;
        static float HeldScale => Screen.height < 600 ? 0.62f : 0.55f;   // the cup and crown read a little larger on a small screen
        bool _heldOn; float _heldPop, _heldCx, _heldLean;

        /// The trophy's foot sits in the raised hand's fist: both follow the hand bone every frame (the cheer hop and sway included).
        /// False (and the cup stands on the first-place block) when the arms are not up.
        bool PlaceHeld()
        {
            var cast = View.Cast;
            if (!_heldOn || cast == null) return false;
            Vector2 hand;
            bool held = cast.TryHandRef(out hand);
            _fist.gameObject.SetActive(held);
            if (held)
            {
                float pop = Mathf.Clamp01(_heldPop);
                _trophy.anchoredPosition = new Vector2(hand.x, -(hand.y + StemUp * HeldScale * pop));   // the cup is bottom-pivoted: its foot and stem sit inside the fist
                _fist.anchoredPosition = new Vector2(hand.x, -hand.y);
                _fist.localScale = Vector3.one * pop;
                _fistImg.color = cast.Tint; foreach (var fp in _fistParts) fp.color = Color.Lerp(cast.Tint, Color.white, 0.12f);
                _fist.SetAsLastSibling();
                // small: the cup leans out, away from the head, so it never covers the crown or the face
                _heldLean = 0f;
                if (Screen.height < 600 && cast.TryHeadRef(out var hd)) _heldLean = hand.x < hd.x ? 14f : -14f;
            }
            else _heldLean = 0f;
            if (!held) _trophy.anchoredPosition = new Vector2(_heldCx + 70f, -(BaseY - BlockH[0] + 4f));
            // the swinging cheer can bring the cup right over the head: the crown steps back rather than fight it
            if (_crownB.gameObject.activeSelf)
            {
                var cg = _crownB.GetComponent<CanvasGroup>() ?? _crownB.gameObject.AddComponent<CanvasGroup>();
                var ca = _crownB.anchoredPosition; var ta = _trophy.anchoredPosition;
                float cupY = ta.y + 55f * _trophy.localScale.y;
                bool clash = held && Mathf.Abs(ta.x - ca.x) < 50f && Mathf.Abs(cupY - ca.y) < 32f;
                cg.alpha = Mathf.MoveTowards(cg.alpha, clash ? 0.45f : 1f, Time.unscaledDeltaTime * 6f);
            }
            return held;
        }

        static float ColCenter(int[] order, int standing, int n) => (order[standing] - (n - 1) * 0.5f) * ColStep;
    }
}
