// Free Play in uGUI: one designed card per game in registry order (art tile in the game's colour, name, tagline, and the
// record: rank badge, best score, plays, or a NEW tag when it was never played), the focused card lifted and glowing, a
// PLAY pill for the focused game and BACK. A pure view: GameHost owns the focus, the keys and the click targets and
// pushes the cards in `View`.
//
// Buttons, in order: 0..n-1 the cards (a click plays that game), PlayBtn(n) the focused game, BackBtn(n+1).

using Bonanza.HudKit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Bonanza.Shell.PartyKit;

namespace Bonanza.Shell
{
    public struct FreePlayCard
    {
        public string Id, Title, Blurb;
        public bool Played;
        public string Rank, Best, Plays;    // from Records.Card; only read when Played
    }

    public struct FreePlayView
    {
        public int Focus;
        public FreePlayCard[] Cards;
    }

    public sealed class FreePlayScreen : ShellScreen
    {
        public FreePlayView View;
        public const int MaxCards = 5;
        public const int PlayBtn = MaxCards, BackBtn = MaxCards + 1;

        const float CardW = 168, Gap = 14;

        sealed class CardUi
        {
            public RectTransform Rt, Inner, TileHolder, NewRt, RecordRt, NoRecRt;
            public Image Shadow, Glow, Fill, Stripe, Ring, StageGlow, RankFill, NewFill;
            public UiGradient Grad;
            public TextMeshProUGUI Name, Blurb, Rank, BestLabel, Best, Plays, New, NoRec;
            public RectTransform Tile; public string TileId;
            public CanvasGroup Group;
            public float Focus, Hover, Press;
        }

        readonly CardUi[] _cards = new CardUi[MaxCards];
        RectTransform _head, _cardsRt, _hintRt, _strip;
        CanvasGroup _headGroup, _hintGroup, _stripGroup;
        TextMeshProUGUI _title, _hint, _stripText;
        Pill _play, _back;
        int _lastFocus = -1;
        int _count;
        int _pressCard = -1;
        /// The card the pointer went down on this frame (focus follows the press), or -1; read once.
        public int TakePressCard() { int c = _pressCard; _pressCard = -1; return c; }

        protected override void Build(RectTransform root)
        {
            _head = UiKit.Rect("Head", root);
            UiKit.Place(_head, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(700, 76));
            _headGroup = _head.gameObject.AddComponent<CanvasGroup>();
            _title = UiKit.Text(_head, "Title", TextStyle.Display, 40f, Gold);
            _title.text = "FREE PLAY";
            UiKit.Place(_title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(700, 62));
            for (int s = -1; s <= 1; s += 2)
            {
                var line = UiKit.Box(_head, "Rule", new Color(1f, 0.85f, 0.4f, 0.55f), UiKit.Pill, true, 24);
                UiKit.Place(line.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(s * 210, -32), new Vector2(110, 4));
            }

            _cardsRt = UiKit.Rect("Cards", root);
            UiKit.Place(_cardsRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 400));
            for (int i = 0; i < MaxCards; i++) _cards[i] = MakeCard(i);

            // the focused game's tagline on a wide strip: on a short screen the cards have no room for theirs
            _strip = UiKit.Rect("Strip", root);
            UiKit.Place(_strip, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -470), new Vector2(760, 44));
            _stripGroup = _strip.gameObject.AddComponent<CanvasGroup>();
            var sp = UiKit.Box(_strip, "Plate", new Color(0.05f, 0.03f, 0.12f, 0.9f), UiKit.Pill, true, 24);
            UiKit.Stretch(sp.rectTransform);
            _stripText = UiKit.Text(_strip, "Text", TextStyle.Label, 17f, UiKit.Cream, italic: false, upper: false);
            UiKit.Place(_stripText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(740, 40));

            _hintRt = UiKit.Rect("Hint", root);
            UiKit.Place(_hintRt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -612), new Vector2(430, 32));
            _hintGroup = _hintRt.gameObject.AddComponent<CanvasGroup>();
            var hp = UiKit.Box(_hintRt, "Plate", new Color(0.05f, 0.03f, 0.12f, 0.9f), UiKit.Pill, true, 24);
            UiKit.Stretch(hp.rectTransform);
            var hr = UiKit.Box(_hintRt, "Ring", new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, 0.75f), UiKit.PillRing(4), true, 24);
            UiKit.Stretch(hr.rectTransform);
            _hint = UiKit.Text(_hintRt, "Text", TextStyle.Label, 14f, UiKit.Cream, italic: false);
            _hint.characterSpacing = 3f; _hint.text = "LEFT / RIGHT  CHOOSE";
            UiKit.Place(_hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420, 28));

            _play = MakePill(root, "Play", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -540), new Vector2(430, 62), 24f, new Vector2(76, 30));
            _back = MakePill(root, "Back", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-64, 14), new Vector2(210, 42), 16f, new Vector2(50, 24));
            for (int i = 0; i < MaxCards; i++) Buttons.Add(_cards[i].Rt);
            Buttons.Add(_play.Rt);
            Buttons.Add(_back.Rt);
        }

        CardUi MakeCard(int i)
        {
            var c = new CardUi();
            c.Rt = UiKit.Rect("Card" + i, _cardsRt);
            UiKit.Place(c.Rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardW, 340));
            c.Group = c.Rt.gameObject.AddComponent<CanvasGroup>();
            c.Inner = UiKit.Rect("Inner", c.Rt); UiKit.Stretch(c.Inner);
            c.Shadow = UiKit.Box(c.Inner, "Shadow", new Color(0, 0, 0, 0.5f), UiKit.Pill, true, 28);
            c.Glow = UiKit.Box(c.Inner, "Glow", new Color(1, 0.8f, 0.3f, 0), UiKit.Soft);
            c.Fill = UiKit.Box(c.Inner, "Fill", Color.white, UiKit.Pill, true, 28);
            UiKit.Stretch(c.Fill.rectTransform);
            c.Grad = c.Fill.gameObject.AddComponent<UiGradient>();
            c.Stripe = UiKit.Box(c.Inner, "Stripe", Gold, UiKit.Pill, true, 12);
            UiKit.Place(c.Stripe.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(CardW - 40, 6));
            c.StageGlow = UiKit.Box(c.Inner, "Stage", new Color(1, 1, 1, 0.3f), UiKit.Soft);
            UiKit.Place(c.StageGlow.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -86), new Vector2(210, 190));
            c.Ring = UiKit.Box(c.Inner, "Ring", new Color(1, 0.95f, 0.8f, 0.2f), UiKit.PillRing(5), true, 28);
            UiKit.Stretch(c.Ring.rectTransform);
            c.TileHolder = UiKit.Rect("TileHolder", c.Inner);
            UiKit.Place(c.TileHolder, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -88), new Vector2(112, 112));
            c.Name = UiKit.Text(c.Inner, "Name", TextStyle.Display, 22f, Color.white, TextAlignmentOptions.Top);
            c.Name.textWrappingMode = TextWrappingModes.Normal;
            c.Name.enableAutoSizing = true; c.Name.fontSizeMin = 14f; c.Name.fontSizeMax = UiKit.FontSizeForCap(22f);
            c.Name.overflowMode = TextOverflowModes.Truncate;
            c.Blurb = UiKit.Text(c.Inner, "Blurb", TextStyle.Label, 14f, UiKit.Cream, TextAlignmentOptions.Top, italic: false, upper: false);
            c.Blurb.textWrappingMode = TextWrappingModes.Normal;
            c.Blurb.lineSpacing = 0f;
            c.Blurb.color = new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, 0.9f);

            // the record
            c.RecordRt = UiKit.Rect("Record", c.Inner);
            UiKit.Place(c.RecordRt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(CardW - 24, 62));
            c.RankFill = UiKit.Box(c.RecordRt, "Badge", Gold, UiKit.Disc);
            UiKit.Place(c.RankFill.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(26, 0), new Vector2(46, 46));
            c.Rank = UiKit.Text(c.RankFill.rectTransform, "Rank", TextStyle.Display, 26f, Ink);
            UiKit.Place(c.Rank.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(56, 46));
            c.BestLabel = UiKit.Text(c.RecordRt, "BestLabel", TextStyle.Label, 12f, Gold, TextAlignmentOptions.Left, italic: false);
            c.BestLabel.characterSpacing = 3f; c.BestLabel.text = "BEST";
            c.Best = UiKit.Text(c.RecordRt, "Best", TextStyle.Display, 20f, Color.white, TextAlignmentOptions.Left);
            c.Plays = UiKit.Text(c.RecordRt, "Plays", TextStyle.Label, 12f, new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, 0.75f), TextAlignmentOptions.Left, italic: false);
            c.Plays.characterSpacing = 2f;
            c.NoRecRt = UiKit.Rect("NoRecord", c.Inner);
            UiKit.Place(c.NoRecRt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(CardW - 24, 62));
            c.NoRec = UiKit.Text(c.NoRecRt, "Text", TextStyle.Label, 13f, new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, 0.7f), italic: false);
            c.NoRec.characterSpacing = 2f; c.NoRec.text = "NOT PLAYED YET";
            c.NoRec.textWrappingMode = TextWrappingModes.Normal;
            UiKit.Place(c.NoRec.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardW - 30, 56));

            // NEW: a tilted gold tag over the top corner
            c.NewRt = UiKit.Rect("New", c.Inner);
            UiKit.Place(c.NewRt, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-30, -10), new Vector2(64, 28));
            c.NewRt.localRotation = Quaternion.Euler(0, 0, 10);
            var ns = UiKit.Box(c.NewRt, "Shadow", new Color(0, 0, 0, 0.5f), UiKit.Pill, true, 28);
            UiKit.Place(ns.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -3), new Vector2(66, 30));
            c.NewFill = UiKit.Box(c.NewRt, "Fill", Color.white, UiKit.Pill, true, 28);
            UiKit.Stretch(c.NewFill.rectTransform);
            c.NewFill.gameObject.AddComponent<UiGradient>().Set(UiKit.Hex(0xffe27a), UiKit.Hex(0xff9a2e));
            var nr = UiKit.Box(c.NewRt, "Ring", new Color(1, 1, 1, 0.75f), UiKit.PillRing(5), true, 28);
            UiKit.Stretch(nr.rectTransform);
            c.New = UiKit.Text(c.NewRt, "T", TextStyle.Display, 17f, Ink);
            c.New.text = "NEW";
            UiKit.Place(c.New.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(64, 28));
            return c;
        }

        /// The card's tagline: a short form with an even break (the full line is on the strip of a short screen).
        static string ShortBlurb(string id, string full)
        {
            switch (id)
            {
                case "swing-kings": return "One swing.\nOne beat.\nSend it.";
                case "drumline-dash": return "Hear it.\nHit it back.";
                case "bounce-brigade": return "Find the downbeat.\nLead the hymn.";
                case "chomp-chorus": return "Four singers,\nyou are one.";
                case "finale-fever": return "Hold the beat\nas the tempo moves.";
                default: return full ?? "";
            }
        }

        static void Counter(RectTransform rt, float centreToAnchor, float lift, float scale)
        {
            float d = centreToAnchor + rt.anchoredPosition.y;
            rt.anchoredPosition += new Vector2(0, -(lift + d * (scale - 1f)));
        }

        public override void Tick(float dt, ShellInput input)
        {
            bool red = input.Reduced;
            float shown = Shown, age = Age;
            Group.alpha = UiKit.Clamp01(shown * 1.6f);
            bool small = Screen.height < 600;
            float ts = small ? 1.45f : 1f;
            var cards = View.Cards ?? new FreePlayCard[0];
            _count = Mathf.Min(cards.Length, MaxCards);
            int focus = Mathf.Clamp(View.Focus, 0, Mathf.Max(0, _count - 1));

            // ---- header
            float hEnter = red ? 1 : UiKit.EaseOutCubic(age / 0.5f);
            _headGroup.alpha = UiKit.Clamp01(hEnter * 1.3f) * (red ? shown : 1);
            _head.anchoredPosition = new Vector2(0, -16 + (1 - hEnter) * 26);

            // ---- cards: a row, centred; on a short screen they are shorter and the tagline moves to the strip
            float cardH = small ? 300 : 392;
            float pitch = CardW + Gap;
            _cardsRt.anchoredPosition = new Vector2(0, small ? 84 : 30);
            if (input.Fresh && input.Hover >= 0 && input.Hover < _count) _pressCard = input.Hover;
            if (focus != _lastFocus && _lastFocus >= 0 && _count > 0) _cards[focus].Rt.SetAsLastSibling();
            _lastFocus = focus;
            for (int i = 0; i < MaxCards; i++)
            {
                var c = _cards[i];
                bool on = i < _count;
                c.Rt.gameObject.SetActive(on);
                if (!on) continue;
                var d = cards[i];
                bool foc = focus == i;
                c.Focus = Damp(c.Focus, foc ? 1 : 0, red ? 40 : 14, dt);
                c.Hover = Damp(c.Hover, input.Hover == i ? 1 : 0, red ? 40 : 18, dt);
                c.Press = Damp(c.Press, input.Pressed && input.Hover == i ? 1 : 0, red ? 60 : 30, dt);
                float hov = c.Hover * (1 - c.Focus);
                float enter = red ? 1 : UiKit.BackOut((age - 0.12f - i * 0.07f) / 0.5f);
                c.Group.alpha = UiKit.Clamp01(enter * 2f) * (red ? shown : 1);
                c.Rt.sizeDelta = new Vector2(CardW, cardH);
                c.Rt.anchoredPosition = new Vector2((i - (_count - 1) * 0.5f) * pitch, (1 - Mathf.Min(1, enter)) * -70f + (small ? 0 : 0));
                float pulse = foc && !red ? 1 + 0.01f * Mathf.Sin(Time.unscaledTime * 4f) : 1;
                c.Inner.localScale = Vector3.one * ((1 + 0.085f * c.Focus + 0.02f * hov) * (1 - 0.05f * c.Press) * Mathf.Clamp01(0.85f + 0.15f * enter) * pulse);
                c.Inner.anchoredPosition = new Vector2(0, 16 * c.Focus + 3 * hov - 5 * c.Press);

                var gc = GameColor(d.Id);
                UiKit.Place(c.Shadow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -8), new Vector2(CardW + 6, cardH + 4));
                UiKit.Place(c.Glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardW * 1.9f, cardH * 1.45f));
                c.Glow.color = A(Color.Lerp(gc, Gold, 0.35f), 0.6f * c.Focus * (red ? 1 : 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 3f)) + 0.08f * hov);
                Color top = Color.Lerp(new Color(0.15f, 0.10f, 0.31f, 0.97f), new Color(0.19f, 0.14f, 0.38f, 0.98f), hov);
                Color bot = Color.Lerp(new Color(0.06f, 0.03f, 0.15f, 0.97f), new Color(0.09f, 0.05f, 0.21f, 0.98f), hov);
                top = Color.Lerp(top, Color.Lerp(new Color(0.2f, 0.12f, 0.36f, 0.98f), gc, 0.28f), c.Focus);
                bot = Color.Lerp(bot, Color.Lerp(new Color(0.08f, 0.04f, 0.16f, 0.98f), gc, 0.1f), c.Focus);
                float dark = 1 - 0.3f * c.Press;
                c.Grad.Set(new Color(top.r * dark, top.g * dark, top.b * dark, top.a), new Color(bot.r * dark, bot.g * dark, bot.b * dark, bot.a));
                c.Stripe.color = A(gc, 0.65f + 0.35f * Mathf.Max(c.Focus, hov));
                c.StageGlow.color = A(gc, 0.38f + 0.3f * c.Focus);
                Color rim = Color.Lerp(new Color(1, 0.95f, 0.8f, 0.2f), new Color(1, 0.95f, 0.8f, 0.55f), hov);
                c.Ring.color = Color.Lerp(rim, new Color(1, 0.93f, 0.6f, 1f), c.Focus);
                c.Shadow.color = new Color(0, 0, 0, 0.5f * (1 - 0.5f * c.Press));

                // art tile, built on first sight of the game
                if (c.TileId != d.Id)
                {
                    if (c.Tile) Object.Destroy(c.Tile.gameObject);
                    c.Tile = MakeGameTile(c.TileHolder, "Tile", d.Id, 108);
                    c.TileId = d.Id;
                }
                if (c.Tile)
                {
                    float bob = red ? 0 : Mathf.Sin(Time.unscaledTime * 1.6f + i) * 3f * (0.35f + 0.65f * c.Focus);
                    c.Tile.anchoredPosition = new Vector2(0, bob);
                    c.Tile.localRotation = Quaternion.Euler(0, 0, red ? -3f * c.Focus : -3f * c.Focus + Mathf.Sin(Time.unscaledTime * 1.1f + i) * 2f * c.Focus);
                    float te = red ? 1 : UiKit.BackOut((age - 0.3f - i * 0.07f) / 0.5f);
                    c.Tile.localScale = Vector3.one * Mathf.Clamp01(te) * (1 + (te > 1 ? te - 1 : 0)) * (0.92f + 0.12f * c.Focus);
                }
                UiKit.Place(c.TileHolder, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, small ? -84 : -92), new Vector2(112, 112));

                // text
                c.Name.text = (d.Title ?? "").Replace(" ", "\n");   // two lines, one word each
                c.Name.overflowMode = TextOverflowModes.Overflow; c.Name.textWrappingMode = TextWrappingModes.NoWrap;
                float nameCap = small ? 20f : 17f;
                c.Name.enableAutoSizing = false;   // one size for every title
                c.Name.fontSize = Mathf.Max(UiKit.FontSizeForCap(nameCap), small ? 22f : 0f);
                float nameY = small ? -146 : -160;
                UiKit.Place(c.Name.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, nameY), new Vector2(CardW - 14, small ? 62 : 58));
                c.Blurb.gameObject.SetActive(!small);
                c.Blurb.text = ShortBlurb(d.Id, d.Blurb);
                UiKit.Place(c.Blurb.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -224), new Vector2(CardW - 12, 74));
                c.Blurb.enableAutoSizing = false; c.Blurb.fontSize = UiKit.FontSizeForCap(14f);   // one size for every tagline
                c.Blurb.overflowMode = TextOverflowModes.Truncate;

                // the record, or NEW
                c.RecordRt.gameObject.SetActive(d.Played);
                c.NoRecRt.gameObject.SetActive(!d.Played);
                c.NewRt.gameObject.SetActive(!d.Played);
                if (!d.Played)
                {
                    float nb = red ? 0 : Mathf.Sin(Time.unscaledTime * 3.2f + i * 1.3f);
                    float pop = Mathf.Clamp01(red ? 1 : UiKit.BackOut((age - 0.5f - i * 0.07f) / 0.4f));
                    float ns = small ? 1.25f : 1f;
                    c.NewRt.localScale = Vector3.one * ns * (1 + 0.05f * nb) * pop;
                    // top-right corner, 12 px inside the card
                    UiKit.Place(c.NewRt, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-(12 + 32 * ns), -(12 + 14 * ns + 2)), new Vector2(64, 28));
                    c.NewRt.localRotation = Quaternion.Euler(0, 0, small ? 0 : 8);
                    var sr = c.Stripe.rectTransform;   // the stripe leaves the corner to the badge
                    UiKit.Place(sr, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -14), new Vector2(small ? 40 : 56, 6));
                    c.NoRecRt.gameObject.SetActive(!small);
                    c.NoRec.textWrappingMode = TextWrappingModes.NoWrap;      // one line
                    c.NoRec.enableAutoSizing = true; c.NoRec.fontSizeMax = UiKit.FontSizeForCap(11.5f); c.NoRec.fontSizeMin = UiKit.FontSizeForCap(10f);
                    c.NoRec.characterSpacing = 1f;
                    c.NoRec.alignment = TextAlignmentOptions.Bottom;
                    UiKit.Place(c.NoRec.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardW - 14, 40));
                    c.NoRec.text = "NOT PLAYED YET";
                    c.New.fontSize = UiKit.FontSizeForCap(17f);
                    UiKit.Place(c.NoRecRt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(CardW - 24, 40));   // text sits 16 px above the card bottom
                }
                else
                {
                    UiKit.Place(c.Stripe.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -14), new Vector2(CardW - 40, 6));
                    var rc = UiKit.RankColor(d.Rank);
                    bool ranked = d.Rank != "-" && !string.IsNullOrEmpty(d.Rank);
                    c.RankFill.color = ranked ? rc : new Color(1, 1, 1, 0.14f);
                    c.Rank.text = ranked ? d.Rank : "-";
                    c.Rank.color = ranked ? Ink : new Color(UiKit.Cream.r, UiKit.Cream.g, UiKit.Cream.b, 0.8f);
                    var rt = c.RankFill.rectTransform;
                    c.BestLabel.text = "BEST";
                    c.BestLabel.fontSize = UiKit.FontSizeForCap(12f);
                    c.Best.text = d.Best ?? "";
                    c.Best.enableAutoSizing = true; c.Best.fontSizeMax = UiKit.FontSizeForCap(22f); c.Best.fontSizeMin = 12f;
                    c.Best.textWrappingMode = TextWrappingModes.NoWrap;
                    c.Best.alignment = TextAlignmentOptions.Center;
                    c.Plays.text = d.Plays ?? "";
                    c.Plays.fontSize = UiKit.FontSizeForCap(12f);
                    c.BestLabel.gameObject.SetActive(!small); c.Best.gameObject.SetActive(!small); c.Plays.gameObject.SetActive(!small);
                    if (small)
                    {
                        // just the rank badge; the score and plays are in the strip below
                        UiKit.Place(c.RecordRt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(CardW - 20, 62));
                        rt.sizeDelta = Vector2.one * 56; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero;
                        c.Rank.fontSize = UiKit.FontSizeForCap(32f);
                    }
                    else
                    {
                        // badge and BEST / plays on one row, the score across the card under them
                        UiKit.Place(c.RecordRt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 16), new Vector2(CardW - 24, 76));
                        rt.sizeDelta = Vector2.one * 42; rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f); rt.anchoredPosition = new Vector2(24, 17);
                        c.Rank.fontSize = UiKit.FontSizeForCap(24f);
                        UiKit.Place(c.BestLabel.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(54, 27), new Vector2(70, 18));
                        UiKit.Place(c.Plays.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(54, 8), new Vector2(70, 18));
                        c.BestLabel.alignment = TextAlignmentOptions.Left; c.Plays.alignment = TextAlignmentOptions.Left;
                        UiKit.Place(c.Best.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0.5f), new Vector2(0, 14), new Vector2(CardW - 28, 30));
                    }
                }

                // the card rises and grows when focused; its text does not: shift it back so every baseline is level
                float lift = c.Inner.anchoredPosition.y, sc = c.Inner.localScale.x;
                Counter(c.Name.rectTransform, cardH * 0.5f, lift, sc);
                Counter(c.Blurb.rectTransform, cardH * 0.5f, lift, sc);
                Counter(c.RecordRt, -cardH * 0.5f, lift, sc);
                Counter(c.NoRecRt, -cardH * 0.5f, lift, sc);
                // the text keeps its size and gaps whether the card is lifted or not
                float inv = 1f / Mathf.Max(0.01f, sc);
                c.Name.rectTransform.localScale = c.Blurb.rectTransform.localScale = c.RecordRt.localScale = c.NoRecRt.localScale = new Vector3(inv, inv, 1);
            }

            // ---- the strip (short screens only) and the hint
            float stripA = small && _count > 0 ? UiKit.Clamp01((age - 0.3f) * 3f) * (red ? shown : 1) : 0;
            _stripGroup.alpha = stripA;
            if (_count > 0)
            {
                _stripText.text = (cards[focus].Blurb ?? "") + (cards[focus].Played ? "   -   BEST " + cards[focus].Best + "   -   " + cards[focus].Plays : "");
                _stripText.enableAutoSizing = true; _stripText.fontSizeMax = UiKit.FontSizeForCap(17f * 1.2f); _stripText.fontSizeMin = 12f;
                _strip.anchoredPosition = new Vector2(0, -442);
                _strip.sizeDelta = new Vector2(760, 54);
            }
            _hintGroup.alpha = UiKit.Clamp01((age - 0.5f) * 3f) * (red ? shown : 1) * (small ? 0 : 1);

            // ---- buttons
            string label = _count > 0 ? "PLAY " + cards[focus].Title : "PLAY";
            DriveButton(_play, input, PlayBtn, true, label, "SPACE", dt, red, age - 0.6f, !small);
            DriveButton(_back, input, BackBtn, false, "BACK", "ESC", dt, red, age - 0.9f, !small);
            _play.Rt.anchoredPosition = new Vector2(0, small ? -508 : -538);
            _play.Rt.sizeDelta = new Vector2(small ? 400 : 430, 62);
            _play.Label.rectTransform.anchoredPosition = new Vector2(small ? 0 : -42, 1);
            _play.Label.rectTransform.sizeDelta = new Vector2((small ? 400 : 430) - (small ? 24 : 112), 72);
            _play.Label.enableAutoSizing = true; _play.Label.fontSizeMax = UiKit.FontSizeForCap(24f); _play.Label.fontSizeMin = 14f;
            _back.Rt.anchoredPosition = small ? new Vector2(-124, 30) : new Vector2(-64, 14);
            _back.Inner.localScale *= Mathf.Lerp(1f, 1.3f, small ? 1 : 0);
            _back.Label.rectTransform.anchoredPosition = new Vector2(small ? 0 : -20, 1);
        }
    }
}
