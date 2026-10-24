// The party hub, between rounds, in uGUI: "ROUND n OF 4" with four pips, the next game on an announcement card (art
// tile, name, tagline, who it is scored against), and the standings with the cast's portraits. After a round the
// points it paid count in as green "+n" chips and the rows slide to their new places. A pure view: GameHost owns the
// party, the input and the log lines, and pushes the state in `View`.
//
// Buttons, in order: 0 the main action (start / continue), 1 quit to menu.

using System.Collections.Generic;
using Bonanza.Core;
using Bonanza.HudKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Bonanza.Shell.PartyKit;

namespace Bonanza.Shell
{
    public struct PartyHubView
    {
        public int Round, Length;            // Round is 0-based
        public bool Finale;
        public string GameId, GameTitle, Blurb;
        public bool Races;                   // the game races the whole lineup live (Drumline); others score against the CPUs' rounds
        public List<PartyBoardRow> Rows;     // best first
        public string LastLine;              // "SWING KINGS: KWARK TOOK IT" (null before round 1)
        public int RoundsPlayed;
        public CastStage Cast;
    }

    public sealed class PartyHubScreen : ShellScreen
    {
        public PartyHubView View;
        public const int ActionBtn = 0, QuitBtn = 1;

        const float RowW = 440, RowH = 72, RowGap = 10, ColX = 10, Top = 122, CardW = 420, CardH = 360;
        float _startY = 170;

        static string HowTo(string id) => id switch
        {
            "swing-kings" => "How to play: tap SPACE on the downbeat of every bar.",
            "drumline-dash" => "How to play: listen to the drum major, then tap the rhythm back.",
            "bounce-brigade" => "How to play: find the downbeat, then key in the beat pattern.",
            "chomp-chorus" => "How to play: tap on the beat when your ringed singer gulps.",
            "finale-fever" => "How to play: tap every beat as the tempo speeds up and slows.",
            _ => "",
        };

        sealed class Row
        {
            public RectTransform Rt, Inner; public CanvasGroup Group;
            public Image Shadow, Fill, Ring, Glow, BadgeFill, YouFill, ChipFill;
            public TextMeshProUGUI Badge, Name, Sub, Pts, PtsLabel, You, Chip;
            public RectTransform ChipRt, YouRt;
            public Face Face;
            public float Pop;
            public int LastShown = -99;
        }

        readonly Row[] _rows = new Row[4];
        Pill _action, _quit;
        TextMeshProUGUI _how, _round, _stand, _after, _upNext, _title, _blurb, _race, _last;
        RectTransform _headRt, _pipsRt, _card, _tileRt, _standHead, _lastRt, _rivalsRt;
        CanvasGroup _headGroup, _cardGroup, _standGroup, _lastGroup;
        Image _cardStripe, _cardGlow;
        readonly Image[] _pip = new Image[4], _pipRing = new Image[4], _pipLine = new Image[3], _pipMark = new Image[4];
        readonly RectTransform[] _pipRt = new RectTransform[4];
        RectTransform _tile; string _tileId;
        readonly Face[] _rival = new Face[3];
        readonly TextMeshProUGUI[] _rivalName = new TextMeshProUGUI[3];

        protected override void Build(RectTransform root)
        {
            // ---- header: round label and the four pips
            _headRt = UiKit.Rect("Head", root);
            UiKit.Place(_headRt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(700, 96));
            _headGroup = _headRt.gameObject.AddComponent<CanvasGroup>();
            _round = UiKit.Text(_headRt, "Round", TextStyle.Display, 36f, Gold);
            UiKit.Place(_round.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(700, 56));
            _pipsRt = UiKit.Rect("Pips", _headRt);
            UiKit.Place(_pipsRt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -62), new Vector2(300, 30));
            for (int i = 0; i < 3; i++)
            {
                _pipLine[i] = UiKit.Box(_pipsRt, "Line" + i, new Color(1, 1, 1, 0.2f), UiKit.Pill, true, 4);
                UiKit.Place(_pipLine[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-60 + i * 60, 0), new Vector2(40, 6));
            }
            for (int i = 0; i < 4; i++)
            {
                _pipRt[i] = UiKit.Rect("Pip" + i, _pipsRt);
                UiKit.Place(_pipRt[i], new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-90 + i * 60, 0), new Vector2(30, 30));
                _pipRing[i] = UiKit.Box(_pipRt[i], "Ring", new Color(1, 1, 1, 0.35f), UiKit.Ring);
                UiKit.Stretch(_pipRing[i].rectTransform);
                _pip[i] = UiKit.Box(_pipRt[i], "Fill", Gold, UiKit.Disc);
                UiKit.Place(_pip[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 22));
                _pipMark[i] = UiKit.Box(_pipRt[i], "Mark", Ink, UiKit.Disc);
                UiKit.Place(_pipMark[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8, 8));
            }

            // ---- the announcement card
            _card = UiKit.Rect("Card", root);
            UiKit.Place(_card, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(-ColX - 20, -Top), new Vector2(CardW, CardH));
            _cardGroup = _card.gameObject.AddComponent<CanvasGroup>();
            var cs = UiKit.Box(_card, "Shadow", new Color(0, 0, 0, 0.5f), UiKit.Pill, true, 28);
            UiKit.Place(cs.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -8), new Vector2(CardW + 6, CardH + 6));
            _cardGlow = UiKit.Box(_card, "Glow", new Color(1, 0.8f, 0.3f, 0.3f), UiKit.Soft);
            UiKit.Place(_cardGlow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardW * 1.4f, CardH * 1.4f));
            var cf = UiKit.Box(_card, "Fill", Color.white, UiKit.Pill, true, 28);
            UiKit.Stretch(cf.rectTransform);
            cf.gameObject.AddComponent<UiGradient>().Set(new Color(0.13f, 0.08f, 0.28f, 0.95f), new Color(0.05f, 0.03f, 0.13f, 0.96f));
            _cardStripe = UiKit.Box(_card, "Stripe", Gold, UiKit.Pill, true, 12);
            UiKit.Place(_cardStripe.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(CardW - 44, 6));
            var cr = UiKit.Box(_card, "Ring", new Color(1, 0.95f, 0.8f, 0.2f), UiKit.PillRing(5), true, 28);
            UiKit.Stretch(cr.rectTransform);
            _upNext = UiKit.Text(_card, "UpNext", TextStyle.Label, 14f, Gold, TextAlignmentOptions.Left, italic: false);
            _upNext.characterSpacing = 4f;
            UiKit.Place(_upNext.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -30), new Vector2(300, 24));
            _tileRt = UiKit.Rect("TileHolder", _card);
            UiKit.Place(_tileRt, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(26 + 70, -60 - 70), new Vector2(140, 140));
            _title = UiKit.Text(_card, "Title", TextStyle.Display, 32f, Color.white, TextAlignmentOptions.Left);
            _title.textWrappingMode = TextWrappingModes.Normal;
            _title.enableAutoSizing = true; _title.fontSizeMin = 16f; _title.fontSizeMax = UiKit.FontSizeForCap(32f);
            _title.overflowMode = TextOverflowModes.Truncate;
            UiKit.Place(_title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(188, -60), new Vector2(CardW - 188 - 20, 84));
            _blurb = UiKit.Text(_card, "Blurb", TextStyle.Label, 15f, UiKit.Cream, TextAlignmentOptions.TopLeft, italic: false, upper: false);
            _blurb.textWrappingMode = TextWrappingModes.Normal;
            UiKit.Place(_blurb.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(188, -150), new Vector2(CardW - 188 - 20, 56));
            _how = UiKit.Text(_card, "How", TextStyle.Label, 13f, UiKit.Cream, TextAlignmentOptions.TopLeft, italic: false, upper: false);
            _how.textWrappingMode = TextWrappingModes.Normal;
            _how.color = new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.95f);
            UiKit.Place(_how.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(26, -206), new Vector2(CardW - 52, 38));
            var div = UiKit.Box(_card, "Div", new Color(1, 1, 1, 0.14f), UiKit.Pill, true, 4);
            UiKit.Place(div.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 104), new Vector2(CardW - 52, 3));
            _race = UiKit.Text(_card, "Race", TextStyle.Label, 12f, Gold, TextAlignmentOptions.Left, italic: false);
            _race.characterSpacing = 3f;
            UiKit.Place(_race.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(26, 78), new Vector2(CardW - 52, 20));
            _rivalsRt = UiKit.Rect("Rivals", _card);
            UiKit.Place(_rivalsRt, new Vector2(0, 0), new Vector2(0, 0), new Vector2(26, 14), new Vector2(CardW - 52, 58));
            for (int i = 0; i < 3; i++)
            {
                var holder = UiKit.Rect("R" + i, _rivalsRt);
                UiKit.Place(holder, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * 124, 0), new Vector2(120, 52));
                _rival[i] = MakeFace(holder, "Face", 44);
                UiKit.Place(_rival[i].Rt, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(44, 44));
                _rivalName[i] = UiKit.Text(holder, "Name", TextStyle.Display, 16f, Color.white, TextAlignmentOptions.Left);
                UiKit.Place(_rivalName[i].rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(60, 1), new Vector2(72, 28));
            }

            // ---- the standings
            _standHead = UiKit.Rect("StandHead", root);
            UiKit.Place(_standHead, new Vector2(0.5f, 1), new Vector2(0, 1), new Vector2(ColX + 20, -Top + 4), new Vector2(RowW, 26));
            _standGroup = _standHead.gameObject.AddComponent<CanvasGroup>();
            _stand = UiKit.Text(_standHead, "Stand", TextStyle.Label, 15f, Gold, TextAlignmentOptions.Left, italic: false);
            _stand.characterSpacing = 4f; _stand.text = "STANDINGS";
            UiKit.Place(_stand.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(240, 26));
            _after = UiKit.Text(_standHead, "After", TextStyle.Label, 13f, new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, 0.7f), TextAlignmentOptions.Right, italic: false);
            _after.characterSpacing = 3f;
            UiKit.Place(_after.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-6, 0), new Vector2(240, 26));
            for (int i = 0; i < 4; i++) _rows[i] = MakeRow(root, i);

            _lastRt = UiKit.Rect("Last", root);
            UiKit.Place(_lastRt, new Vector2(0.5f, 1), new Vector2(0, 1), new Vector2(ColX + 20, -Top - CardH), new Vector2(RowW, 30));
            _lastGroup = _lastRt.gameObject.AddComponent<CanvasGroup>();
            var lp = UiKit.Box(_lastRt, "Plate", new Color(0.05f, 0.03f, 0.12f, 0.95f), UiKit.Pill, true, 24);
            UiKit.Stretch(lp.rectTransform);
            _last = UiKit.Text(_lastRt, "Text", TextStyle.Label, 13f, UiKit.Cream, italic: false);
            _last.characterSpacing = 2f;
            UiKit.Place(_last.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(RowW - 16, 28));

            // ---- buttons
            _action = MakePill(root, "Action", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -516), new Vector2(380, 62), 24f, new Vector2(76, 30));
            _quit = MakePill(root, "Quit", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-64, 14), new Vector2(276, 42), 16f, new Vector2(50, 24));
            Buttons.Add(_action.Rt);
            Buttons.Add(_quit.Rt);
        }

        Row MakeRow(RectTransform root, int i)
        {
            var r = new Row();
            r.Rt = UiKit.Rect("Row" + i, root);
            UiKit.Place(r.Rt, new Vector2(0.5f, 1), new Vector2(0, 1), Vector2.zero, new Vector2(RowW, RowH));
            r.Group = r.Rt.gameObject.AddComponent<CanvasGroup>();
            r.Inner = UiKit.Rect("Inner", r.Rt); UiKit.Stretch(r.Inner);
            r.Shadow = UiKit.Box(r.Inner, "Shadow", new Color(0, 0, 0, 0.45f), UiKit.Pill, true, 24);
            UiKit.Place(r.Shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -5), new Vector2(RowW + 4, RowH + 3));
            r.Glow = UiKit.Box(r.Inner, "Glow", new Color(1, 0.8f, 0.3f, 0), UiKit.Soft);
            UiKit.Place(r.Glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(RowW * 1.25f, RowH * 2.4f));
            r.Fill = UiKit.Box(r.Inner, "Fill", Plate, UiKit.Pill, true, 24);
            UiKit.Stretch(r.Fill.rectTransform);
            r.Ring = UiKit.Box(r.Inner, "Ring", new Color(1, 0.95f, 0.8f, 0.18f), UiKit.PillRing(5), true, 24);
            UiKit.Stretch(r.Ring.rectTransform);
            // rank badge
            r.BadgeFill = UiKit.Box(r.Inner, "Badge", Gold, UiKit.Disc);
            UiKit.Place(r.BadgeFill.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(32, 0), new Vector2(38, 38));
            r.Badge = UiKit.Text(r.BadgeFill.rectTransform, "N", TextStyle.Display, 22f, Ink);
            UiKit.Place(r.Badge.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(46, 40));
            // face
            var fh = UiKit.Rect("FaceHolder", r.Inner);
            UiKit.Place(fh, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(88, 0), new Vector2(54, 54));
            r.Face = MakeFace(fh, "Face", 54);
            UiKit.Place(r.Face.Rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(54, 54));
            // name + sub
            r.Name = UiKit.Text(r.Inner, "Name", TextStyle.Display, 24f, Color.white, TextAlignmentOptions.Left);
            UiKit.Place(r.Name.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(128, -11), new Vector2(150, 34));
            r.Sub = UiKit.Text(r.Inner, "Sub", TextStyle.Label, 12f, new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, 0.75f), TextAlignmentOptions.Left, italic: false);
            r.Sub.characterSpacing = 2f;
            UiKit.Place(r.Sub.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(129, 11), new Vector2(190, 20));
            // YOU tag
            r.YouRt = UiKit.Rect("You", r.Inner);
            UiKit.Place(r.YouRt, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(88, -62), new Vector2(40, 20));
            r.YouFill = UiKit.Box(r.YouRt, "Fill", Gold, UiKit.Pill, true, 20);
            UiKit.Stretch(r.YouFill.rectTransform);
            r.You = UiKit.Text(r.YouRt, "T", TextStyle.Display, 12f, Ink);
            r.You.text = "YOU";
            UiKit.Place(r.You.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(50, 22));
            // points
            r.Pts = UiKit.Text(r.Inner, "Pts", TextStyle.Display, 32f, Color.white, TextAlignmentOptions.Right);
            UiKit.Place(r.Pts.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-54, 1), new Vector2(80, 44));
            r.PtsLabel = UiKit.Text(r.Inner, "PtsLabel", TextStyle.Label, 11f, new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, 0.7f), TextAlignmentOptions.Left, italic: false);
            r.PtsLabel.characterSpacing = 2f; r.PtsLabel.text = "PTS";
            UiKit.Place(r.PtsLabel.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, -4), new Vector2(40, 18));
            // delta chip
            r.ChipRt = UiKit.Rect("Chip", r.Inner);
            UiKit.Place(r.ChipRt, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-138, 0), new Vector2(50, 30));
            r.ChipFill = UiKit.Box(r.ChipRt, "Fill", UiKit.Hex(0x3fbf5a), UiKit.Pill, true, 30);
            UiKit.Stretch(r.ChipFill.rectTransform);
            r.Chip = UiKit.Text(r.ChipRt, "T", TextStyle.Display, 18f, Color.white);
            UiKit.Place(r.Chip.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(60, 30));
            return r;
        }

        float RowY(float rank) => -(_startY + rank * (RowH + RowGap));

        // ------------------------------------------------------------------ tick

        public override void Tick(float dt, ShellInput input)
        {
            var rows = View.Rows;
            if (rows == null || rows.Count == 0) return;
            bool red = input.Reduced;
            float shown = Shown, age = Age;
            Group.alpha = UiKit.Clamp01(shown * 1.6f);
            bool small = Screen.height < 600;
            float ts = small ? 1.45f : 1f;
            bool hasLast = View.RoundsPlayed > 0 && rows[0].LastPlace > 0;

            // ---- header
            float hEnter = red ? 1 : UiKit.EaseOutCubic(age / 0.5f);
            _headGroup.alpha = UiKit.Clamp01(hEnter * 1.3f) * (red ? shown : 1);
            _headRt.anchoredPosition = new Vector2(0, -16 + (1 - hEnter) * 26);
            _round.text = View.Finale ? "FINAL ROUND" : $"ROUND {View.Round + 1} OF {View.Length}";
            _round.color = View.Finale ? new Color(1f, 0.72f, 0.42f) : Color.white;
            int n = Mathf.Clamp(View.Length, 1, 4);
            for (int i = 0; i < 4; i++)
            {
                bool on = i < n;
                _pipRt[i].gameObject.SetActive(on);
                if (i < 3) _pipLine[i].gameObject.SetActive(i < n - 1);
                if (!on) continue;
                float cx = (i - (n - 1) * 0.5f) * 60;
                _pipRt[i].anchoredPosition = new Vector2(cx, 0);
                if (i < 3) _pipLine[i].rectTransform.anchoredPosition = new Vector2(cx + 30, 0);
                bool done = i < View.Round, cur = i == View.Round;
                bool fin = i == n - 1;
                float pe = red ? 1 : UiKit.BackOut((age - 0.15f - i * 0.08f) / 0.4f);
                float pulse = cur && !red ? 1 + 0.12f * Mathf.Sin(Time.unscaledTime * 4.5f) : 1;
                _pipRt[i].localScale = Vector3.one * (Mathf.Clamp01(pe) * pulse * (fin ? 1.15f : 1f));
                var c = fin ? UiKit.Hex(0xff9f45) : Gold;
                _pip[i].gameObject.SetActive(done || cur);
                _pip[i].color = done ? c : Color.Lerp(c, Color.white, 0.5f);
                _pip[i].rectTransform.sizeDelta = Vector2.one * (cur ? 18 : 22);
                _pipMark[i].gameObject.SetActive(done);
                _pipRing[i].color = done ? new Color(1, 0.95f, 0.7f, 0.9f) : cur ? new Color(1, 1, 1, 0.95f) : new Color(1, 1, 1, 0.3f);
                if (i < 3) _pipLine[i].color = i < View.Round ? A(Gold, 0.85f) : new Color(1, 1, 1, 0.2f);
            }

            // ---- the announcement card
            float cEnter = red ? 1 : UiKit.BackOut((age - 0.2f) / 0.55f);
            _cardGroup.alpha = UiKit.Clamp01(cEnter * 2f) * (red ? shown : 1);
            _card.anchoredPosition = new Vector2(-ColX - 20 - (1 - Mathf.Min(1, cEnter)) * 80, -Top);
            _card.localScale = Vector3.one * (0.9f + 0.1f * Mathf.Min(1.05f, cEnter));
            var gc = GameColor(View.GameId);
            _cardStripe.color = gc;
            _cardGlow.color = A(gc, 0.2f);
            _upNext.text = View.Finale ? "THE FINALE" : "UP NEXT";
            _upNext.fontSize = 14f * ts;
            _title.text = View.GameTitle ?? "";
            _blurb.text = View.Blurb ?? "";
            _how.text = small ? "" : HowTo(View.GameId);
            _how.fontSize = UiKit.FontSizeForCap(12f);
            _blurb.fontSize = UiKit.FontSizeForCap(15f * Mathf.Lerp(1f, 1.3f, small ? 1 : 0));
            if (_tileId != View.GameId)
            {
                if (_tile) Object.Destroy(_tile.gameObject);
                _tile = MakeGameTile(_tileRt, "Tile", View.GameId, 140);
                _tileId = View.GameId;
            }
            if (_tile)
            {
                float bob = red ? 0 : Mathf.Sin(Time.unscaledTime * 1.6f) * 3f;
                _tile.anchoredPosition = new Vector2(0, bob);
                _tile.localRotation = Quaternion.Euler(0, 0, red ? -3f : -3f + Mathf.Sin(Time.unscaledTime * 1.1f) * 2f);
                float te = red ? 1 : UiKit.BackOut((age - 0.4f) / 0.5f);
                _tile.localScale = Vector3.one * Mathf.Clamp01(te) * (1 + (te > 1 ? te - 1 : 0));
            }
            _race.text = View.Races ? "RACING LIVE" : "BEAT THEIR ROUNDS";
            _race.fontSize = 12f * ts;
            var rivals = new List<PartyBoardRow>();
            foreach (var r in rows) if (!(r.Id == HumanId(rows))) rivals.Add(r);
            for (int i = 0; i < 3; i++)
            {
                bool on = i < rivals.Count;
                _rival[i].Rt.parent.gameObject.SetActive(on);
                if (!on) continue;
                SetFace(_rival[i], rivals[i].Char, View.Cast, new Vector2(22, 0), 44, 1);
                _rivalName[i].text = rivals[i].Name;
            }

            // ---- standings
            int cnt = rows.Count;
            float blockH = cnt * (RowH + RowGap) - RowGap;
            _startY = Top + CardH * 0.5f - blockH * 0.5f + 12;
            float sEnter = red ? 1 : UiKit.EaseOutCubic((age - 0.1f) / 0.4f);
            _standGroup.alpha = UiKit.Clamp01(sEnter) * (red ? shown : 1);
            _after.text = hasLast ? $"AFTER ROUND {View.RoundsPlayed}" : "NO ROUNDS YET";
            _stand.fontSize = 15f * ts; _after.fontSize = 13f * ts;
            _standHead.anchoredPosition = new Vector2(ColX + 20, -(_startY - 38));

            float tCount = 1.0f, tMove = 1.9f;
            float count = !hasLast || red ? 1 : UiKit.EaseOutCubic((age - tCount) / 0.9f);
            float move = !hasLast || red ? 1 : UiKit.BackOut((age - tMove) / 0.6f, 1.2f);
            bool moved = !hasLast || red || age > tMove + 0.25f;
            for (int i = 0; i < 4; i++)
            {
                var r = _rows[i];
                bool on = i < rows.Count;
                r.Rt.gameObject.SetActive(on);
                if (!on) continue;
                var d = rows[i];
                bool you = !d.IsCpu;
                // the row stays on its own player: it is drawn at the rank it has "now", sliding from the old one
                float rank = Mathf.LerpUnclamped(d.PrevRank, d.Rank, move);
                float enter = red ? 1 : UiKit.BackOut((age - 0.15f - d.PrevRank * 0.08f) / 0.45f);
                r.Group.alpha = UiKit.Clamp01(enter * 2f) * (red ? shown : 1);
                r.Rt.anchoredPosition = new Vector2(ColX + 20 + (1 - Mathf.Min(1, enter)) * 90, RowY(rank));
                float lift = hasLast && !red ? Mathf.Sin(Mathf.Clamp01((age - tMove) / 0.7f) * Mathf.PI) * Mathf.Sign(d.PrevRank - d.Rank) * 0.04f : 0;
                r.Inner.localScale = Vector3.one * (1 + lift + (you ? 0.015f : 0));

                int place = moved ? d.Place : d.PrevPlace;
                if (place != r.LastShown) { if (r.LastShown != -99 && !red) r.Pop = 1; r.LastShown = place; }
                r.Pop = Mathf.MoveTowards(r.Pop, 0, dt / 0.4f);
                r.BadgeFill.color = PlaceColor(place);
                r.Badge.text = place.ToString();
                r.Badge.fontSize = UiKit.FontSizeForCap(22f);
                r.BadgeFill.rectTransform.localScale = Vector3.one * (1 + 0.25f * Mathf.Sin(Mathf.Clamp01(1 - r.Pop) * Mathf.PI) * (r.Pop > 0 ? 1 : 0));

                SetFace(r.Face, d.Char, View.Cast, Vector2.zero, 54, 1);
                r.Name.text = d.Name;
                r.Name.fontSize = UiKit.FontSizeForCap(24f * (small ? 0.95f : 1f));
                bool tiedNow = moved && d.Tied;
                string wins = d.Wins > 0 ? $"{d.Wins} WIN{(d.Wins == 1 ? "" : "S")}" : "NO WINS";
                r.Sub.text = View.RoundsPlayed == 0 ? "READY" : tiedNow ? (d.Wins > 0 ? "TIED - " + wins : "TIED") : wins;
                r.Sub.fontSize = 12f * ts;
                r.YouRt.gameObject.SetActive(you);

                int pts = Mathf.RoundToInt(Mathf.Lerp(d.PrevPoints, d.Points, count));
                r.Pts.text = pts.ToString();
                bool counting = hasLast && !red && count < 1 && count > 0;
                r.Pts.color = counting && d.Delta > 0 ? Color.Lerp(Color.white, UiKit.Hex(0x9ee87a), 0.8f) : Color.white;
                r.PtsLabel.fontSize = 11f * ts;

                // delta chip pops in just before the count
                bool showChip = hasLast && d.Delta >= 0;
                float ce = red ? 1 : UiKit.BackOut((age - tCount + 0.1f - d.PrevRank * 0.1f) / 0.35f);
                r.ChipRt.gameObject.SetActive(showChip && ce > 0.01f);
                r.Chip.text = "+" + d.Delta;
                r.ChipRt.localScale = Vector3.one * Mathf.Clamp(ce, 0, 1.2f);
                float settle = red ? 1 : UiKit.Clamp01((age - tMove - 0.6f) / 0.6f);
                r.ChipFill.color = d.Delta == 0 ? new Color(1, 1, 1, Mathf.Lerp(0.22f, 0.14f, settle)) : Color.Lerp(UiKit.Hex(0x3fbf5a), UiKit.Hex(0x2d8f45), settle);
                r.Chip.color = d.Delta == 0 ? new Color(1, 1, 1, 0.7f) : Color.white;

                // you highlight
                float glow = you ? (red ? 0.4f : 0.34f + 0.1f * Mathf.Sin(Time.unscaledTime * 2.4f)) : 0;
                r.Glow.color = A(Gold, glow);
                var plate = you ? new Color(0.22f, 0.15f, 0.07f, 0.96f) : Plate;
                r.Fill.color = plate;
                r.Ring.color = you ? new Color(1, 0.88f, 0.42f, 1f) : place == 1 ? A(PlaceColor(1), 0.75f) : new Color(1, 0.95f, 0.8f, 0.18f);
                r.Shadow.color = new Color(0, 0, 0, 0.45f);
            }

            // ---- the last round's winner line
            _lastGroup.alpha = hasLast ? UiKit.Clamp01((age - (red ? 0 : 0.5f)) * 3f) * (red ? shown : 1) : 0;
            _last.text = View.LastLine ?? "";
            _last.fontSize = 13f * ts;
            float ly = RowY(rows.Count) + 6;
            _lastRt.anchoredPosition = new Vector2(ColX + 20, ly);

            // ---- buttons
            string label = View.Round == 0 && View.RoundsPlayed == 0 ? (View.Finale ? "START FINALE" : "START ROUND 1")
                         : View.Finale ? "START FINALE" : "NEXT ROUND";
            if (View.Round > 0 && !View.Finale) label = $"START ROUND {View.Round + 1}";
            float bt = age - 0.6f;
            DriveButton(_action, input, ActionBtn, true, label, "SPACE", dt, red, bt, !small);
            DriveButton(_quit, input, QuitBtn, false, "QUIT TO MENU", "ESC", dt, red, age - 0.9f, !small);
            _action.Rt.anchoredPosition = new Vector2(0, -516);
            _quit.Rt.anchoredPosition = new Vector2(-64, 14);
            _quit.Inner.localScale *= Mathf.Lerp(1f, 1.3f, small ? 1 : 0);
            _quit.Label.rectTransform.anchoredPosition = new Vector2(small ? 0 : -20, 1);
            _action.Label.rectTransform.anchoredPosition = new Vector2(small ? 0 : -42, 1);
        }

        static int HumanId(List<PartyBoardRow> rows) { foreach (var r in rows) if (!r.IsCpu) return r.Id; return -1; }
    }
}
