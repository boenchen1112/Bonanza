// The results card (web shell/results.js): a dimmed scene, a panel with the rank stamp, game name,
// score counting up, accuracy, the judge's counts, the game's own stats, a party's placings, a
// NEW BEST / NEW RANK sticker and the continue prompt. Pure presentation: the host feeds it the
// seconds since the round ended (Reveal(t)) and every animation is a function of t, so a skipped
// reveal just jumps t. Built once per round end (not in play), so it may allocate.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bonanza.HudKit
{
    public struct PartyRow { public string Place, Name; public int Points; public Color Color; public bool You; }

    public sealed class ResultsData
    {
        public string Title, Rank, Badge;
        public int Score, Perfect, Great, Good, Miss, MaxCombo;
        public float Accuracy;
        public List<KeyValuePair<string, string>> Extras = new List<KeyValuePair<string, string>>();
        /// A game's own stat row in place of the judge counts (Minigame.ResultsStats), null = the judge counts.
        public List<KeyValuePair<string, string>> Stats;
        /// Opt-in looks (Minigame.ResultsStyle; all off by default): the card centred on screen, a filled rank stamp
        /// with a glow, the sticker pinned to the stamp, and a row of result pips (one colour per round of the game).
        public bool Centered, RankFilled, BadgeOnRank;
        /// Opt-in: the sticker (NEW RANK! / NEW BEST!) as a flat ribbon under the game's name instead of the tilted corner sticker.
        public bool BadgeRibbon;
        public List<Color> Pips;
        /// Opt-in: a lead/lag gauge under the stats (EARLY | ON | LATE with a marker), `Lean` in -1 (early) .. +1 (late)
        /// of the track's half width, `LeanLabel` the words under it. Null = no gauge (the default).
        public float? Lean;
        public string LeanLabel;
        /// Opt-in: how dark the dim under the card is (default 0.68), so a game can keep its scene readable behind the card.
        public float DimAlpha = 0.68f;
        public List<PartyRow> Party;
        /// (key, label) chips for the continue prompt: ("SPACE", "play again"), ("ESC", "menu").
        public List<KeyValuePair<string, string>> Prompts = new List<KeyValuePair<string, string>>();
    }

    public sealed class ResultsCard : MonoBehaviour
    {
        // web results.js timeline (s)
        public const float CountAt = 0.3f, CountS = 0.9f, StatsAt = 0.45f, StampAt = 1.3f, PartyAt = 1.65f, PromptAt = 0.7f;

        const float CardW = 600, Pad = 30.7f;

        RectTransform _root, _card, _rankBox, _badge, _prompt;
        // The prompts that have a key (SPACE play again, ESC menu) are buttons inside the card; the host keeps only
        // invisible click targets over them (ButtonScreen). Key-less prompts ("autoplay") stay a label bottom right.
        readonly List<RectTransform> _buttons = new List<RectTransform>();
        CanvasGroup _buttonGroup;
        bool _badgeTilt = true;
        public int ButtonCount => _buttons.Count;
        Image _dim;
        CanvasGroup _group, _promptGroup;
        TextMeshProUGUI _scoreText, _rankText;
        int _score;
        readonly List<Item> _items = new List<Item>();
        bool _built;
        float _t = -1, _stampAt, _badgeAt;
        Color _rankColor;

        sealed class Item { public RectTransform Rt; public CanvasGroup Group; public float At; public Vector2 Pos; }

        public static ResultsCard Create(Transform parent)
        {
            var rt = UiKit.Rect("Results", parent); UiKit.Stretch(rt);
            var c = rt.gameObject.AddComponent<ResultsCard>();
            c._root = rt; c._group = rt.gameObject.AddComponent<CanvasGroup>();
            c._group.alpha = 0;
            rt.gameObject.SetActive(false);
            return c;
        }

        public bool Visible => _built && gameObject.activeSelf;

        public void Hide()
        {
            _t = -1;
            if (_root) _root.gameObject.SetActive(false);
        }

        public void Tick(float dt, bool calm) { }

        // ------------------------------------------------------------ build

        public void Show(ResultsData d)
        {
            foreach (Transform ch in _root) Destroy(ch.gameObject);
            _items.Clear();
            _root.gameObject.SetActive(true);
            _group.alpha = 1;
            _score = d.Score; _lastShown = -1;
            _rankColor = UiKit.RankColor(d.Rank);

            // the dim sits under the card: the scene behind stays, darker
            _dim = UiKit.Box(_root, "Dim", new Color(8 / 255f, 7 / 255f, 20 / 255f, d.DimAlpha));
            UiKit.Stretch(_dim.rectTransform);

            bool hasExtras = d.Extras != null && d.Extras.Count > 0;
            bool hasParty = d.Party != null && d.Party.Count > 0;
            int extraRows = hasExtras ? (d.Extras.Count + 4) / 5 : 0;

            // --- vertical layout (top to bottom, px from the card's top edge)
            float y = Pad;
            float rankY = y; y += 118.8f + 16f;
            float nameY = y + 18f; y += 37.6f + 8f;
            bool ribbon = d.BadgeRibbon && !string.IsNullOrEmpty(d.Badge);
            float ribbonY = y + 12f;
            if (ribbon) y += 32f;
            float scoreY = y + 17f; y += 33.6f + 25f;
            float statsTop = y; y += 11f + 8.6f;   // border + padding
            float statsY = y; y += 24f + 3f + 11f + 4f;
            float extraY0 = y + 6f;
            if (extraRows > 0) y += 8f + extraRows * 44f;
            float pipsY = y + 8f;
            if (d.Pips != null && d.Pips.Count > 0) y += 34f;
            float leanY = y + 8f;
            if (d.Lean.HasValue) y += 56f;
            float partyY0 = y + 8f;
            if (hasParty) y += 8f + 11f + d.Party.Count * 28f;
            y += Pad - 4f;
            int keyed = 0;
            foreach (var pr in d.Prompts) if (!string.IsNullOrEmpty(pr.Key)) keyed++;
            float buttonsY = y + 10f;
            if (keyed > 0) y += 68f;
            float h = y;

            _card = UiKit.Rect("Card", _root);
            // left 57%, top 44% (web), centred on that point
            UiKit.Place(_card, d.Centered ? new Vector2(0.5f, 0.5f) : new Vector2(0.57f, 0.56f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(CardW, h));
            var shadow = UiKit.Box(_card, "Shadow", new Color(0, 0, 0, 0.55f), UiKit.Pill, true, 18f);
            UiKit.Stretch(shadow.rectTransform); shadow.rectTransform.anchoredPosition = new Vector2(0, -7);
            var body = UiKit.Box(_card, "Body", Color.white, UiKit.Pill, true, 18f);
            UiKit.Stretch(body.rectTransform);
            var grad = body.gameObject.AddComponent<UiGradient>(); grad.Set(UiKit.Panel, UiKit.Panel2);
            var border = UiKit.Box(_card, "Border", new Color(1, 1, 1, 0.10f), UiKit.PillRing(2f * 24f / 18f), true, 18f);
            UiKit.Stretch(border.rectTransform);
            var hi = UiKit.Box(_card, "Rim", new Color(1, 1, 1, 0.14f), UiKit.Pill, true, 18f);   // inset 2px top highlight
            var hr = hi.rectTransform; hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1); hr.pivot = new Vector2(0.5f, 1);
            hr.sizeDelta = new Vector2(-30, 2); hr.anchoredPosition = new Vector2(0, -2);

            // rank stamp: a bordered box, the letter in the rank's colour
            _rankBox = UiKit.Rect("Rank", _card);
            UiKit.Place(_rankBox, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -(rankY + 59.4f)), new Vector2(118.8f, 118.8f));
            if (d.RankFilled)
            {
                var glow = UiKit.Box(_rankBox, "Glow", new Color(_rankColor.r, _rankColor.g, _rankColor.b, 0.55f), UiKit.Soft);
                UiKit.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(230, 230));
                var fill = UiKit.Box(_rankBox, "Fill", Color.white, UiKit.Pill, true, 16f); UiKit.Stretch(fill.rectTransform);
                fill.gameObject.AddComponent<UiGradient>().Set(Color.Lerp(_rankColor, Color.white, 0.35f), Color.Lerp(_rankColor, Color.black, 0.12f));
            }
            var rb = UiKit.Box(_rankBox, "Border", d.RankFilled ? Color.white : _rankColor, UiKit.PillRing(3f * 24f / 16f), true, 16f); UiKit.Stretch(rb.rectTransform);
            _rankText = UiKit.Text(_rankBox, "Letter", TextStyle.Display, 54f * 0.7f, d.RankFilled ? UiKit.Ink : _rankColor, TextAlignmentOptions.Center);
            UiKit.Stretch(_rankText.rectTransform);
            _rankText.text = d.Rank ?? "-";
            _stampAt = StampAt;
            var cg = _rankBox.gameObject.AddComponent<CanvasGroup>(); cg.alpha = 0;

            // name
            var name = UiKit.Text(_card, "Name", TextStyle.Display, 28f, Color.white, TextAlignmentOptions.Center);
            Mid(name.rectTransform, 0, nameY, 560, 50); name.text = d.Title;

            // score + accuracy: the score counts up (Reveal), the accuracy sits beside it
            _scoreText = UiKit.Text(_card, "Score", TextStyle.Display, 20f, Color.white, TextAlignmentOptions.Right);
            _scoreText.text = "0";
            _scoreText.ForceMeshUpdate();
            var acc = UiKit.Text(_card, "Acc", TextStyle.Label, 16.8f * 0.72f, UiKit.Dim, TextAlignmentOptions.Left, italic: false);
            acc.text = ((d.Accuracy * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)) + "% ACC";
            acc.characterSpacing = 4f;
            acc.ForceMeshUpdate();
            // centre the pair: score box right-aligned to the gap, accuracy left-aligned after it
            float fullScoreW = FmtScoreWidth(d.Score);
            float pairW = fullScoreW + 16.8f + acc.preferredWidth;
            float left = -pairW / 2;
            Anchor(_scoreText.rectTransform, left + fullScoreW, scoreY, 300, 40, 1f);
            Anchor(acc.rectTransform, left + fullScoreW + 16.8f, scoreY + 3, 300, 24, 0f);

            // judge counts: a divider, then five cells
            var div = UiKit.Box(_card, "Divider", new Color(1, 1, 1, 0.09f)); Mid(div.rectTransform, 0, statsTop, CardW - 2 * 31, 1);
            if (d.Stats != null)
            {
                var cells = new List<(string, string)>();
                for (int i = 0; i < d.Stats.Count && i < 5; i++) cells.Add((d.Stats[i].Key, d.Stats[i].Value));
                StatRow(0, statsY, StatsAt, cells.ToArray(), 24f);
            }
            else
                StatRow(0, statsY, StatsAt, new[] { ("PERFECT", d.Perfect.ToString()), ("GREAT", d.Great.ToString()), ("GOOD", d.Good.ToString()),
                    ("MISS", d.Miss.ToString()), ("COMBO", d.MaxCombo.ToString()) }, 24f);
            if (extraRows > 0)
            {
                for (int r = 0; r < extraRows; r++)
                {
                    var cells = new List<(string, string)>();
                    for (int i = r * 5; i < Mathf.Min(d.Extras.Count, r * 5 + 5); i++) cells.Add((d.Extras[i].Key, d.Extras[i].Value));
                    StatRow(r, extraY0 + r * 44f + 8f, StatsAt + 0.5f + r * 0.1f, cells.ToArray(), 17f, cells.Count == 5 ? 5 : cells.Count);
                }
            }
            if (d.Pips != null && d.Pips.Count > 0)
            {
                var row = UiKit.Rect("Pips", _card);
                Mid(row, 0, pipsY + 12f, d.Pips.Count * 34f, 26);
                var g = row.gameObject.AddComponent<CanvasGroup>(); g.alpha = 0;
                for (int i = 0; i < d.Pips.Count; i++)
                {
                    var rim = UiKit.Box(row, "PipRim", new Color(1, 1, 1, 0.85f), UiKit.Ring);
                    UiKit.Place(rim.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(17 + i * 34f, 0), new Vector2(24, 24));
                    var pip = UiKit.Box(rim.transform, "Pip", d.Pips[i], UiKit.Disc);
                    UiKit.Place(pip.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16, 16));
                }
                _items.Add(new Item { Rt = row, Group = g, At = StatsAt + 0.55f, Pos = row.anchoredPosition });
            }
            if (d.Lean.HasValue) LeanGauge(d.Lean.Value, d.LeanLabel, leanY, StatsAt + 0.6f);
            if (hasParty)
            {
                var pdiv = UiKit.Box(_card, "PDivider", new Color(1, 1, 1, 0.09f)); Mid(pdiv.rectTransform, 0, partyY0 - 4, CardW - 2 * 31, 1);
                for (int i = 0; i < d.Party.Count; i++) PartyLine(d.Party[i], partyY0 + 8 + i * 28f, PartyAt + i * 0.12f);
            }

            // sticker
            if (!string.IsNullOrEmpty(d.Badge))
            {
                _badge = UiKit.Rect("Badge", d.BadgeOnRank && !ribbon ? _rankBox : _card);
                _badgeTilt = !ribbon;
                if (ribbon) Mid(_badge, 0, ribbonY, 200, 26);
                else if (d.BadgeOnRank) UiKit.Place(_badge, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(34, -4), new Vector2(150, 34));
                else UiKit.Place(_badge, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-CardW * 0.02f, CardW * 0.02f + 8), new Vector2(150, 34));
                var bf = UiKit.Box(_badge, "Fill", UiKit.Coral, UiKit.Pill, true, 10f); UiKit.Stretch(bf.rectTransform);
                var bb = UiKit.Box(_badge, "Border", Color.white, UiKit.PillRing(1.8f * 24f / 10f), true, 10f); UiKit.Stretch(bb.rectTransform);
                var bt = UiKit.Text(_badge, "Text", TextStyle.Display, 20f * 0.7f, Color.white, TextAlignmentOptions.Center);
                UiKit.Stretch(bt.rectTransform); bt.text = d.Badge;
                _badge.gameObject.AddComponent<CanvasGroup>().alpha = 0;
                _badgeAt = StampAt + 0.25f;
            }
            else _badge = null;

            // the keyed prompts as buttons inside the card, the first one the primary (gold)
            _buttons.Clear();
            if (keyed > 0)
            {
                var row = UiKit.Rect("Buttons", _card);
                Mid(row, 0, buttonsY + 20f, CardW - 2 * Pad, 40);
                _buttonGroup = row.gameObject.AddComponent<CanvasGroup>(); _buttonGroup.alpha = 0;
                const float bw = 200, gap = 16;
                float x0 = -(keyed * bw + (keyed - 1) * gap) / 2 + bw / 2;
                int bi = 0;
                foreach (var pr in d.Prompts)
                {
                    if (string.IsNullOrEmpty(pr.Key)) continue;
                    bool primary = bi == 0;
                    var b = UiKit.Rect("Button" + bi, row);
                    UiKit.Place(b, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x0 + bi * (bw + gap), 0), new Vector2(bw, 40));
                    var bf = UiKit.Box(b, "Fill", Color.white, UiKit.Pill, true, 14f); UiKit.Stretch(bf.rectTransform);
                    bf.gameObject.AddComponent<UiGradient>().Set(primary ? new Color(1, 0.9f, 0.55f, 0.32f) : new Color(1, 1, 1, 0.1f), primary ? new Color(1, 0.75f, 0.3f, 0.18f) : new Color(1, 1, 1, 0.04f));
                    var brim = UiKit.Box(b, "Rim", primary ? UiKit.Gold : new Color(1, 1, 1, 0.3f), UiKit.PillRing(2f * 24f / 14f), true, 14f); UiKit.Stretch(brim.rectTransform);
                    var kt = UiKit.Text(b, "Key", TextStyle.Label, 9.5f, UiKit.Ink, TextAlignmentOptions.Center, italic: false);
                    kt.text = pr.Key; kt.characterSpacing = 4; kt.ForceMeshUpdate();
                    float kw = Mathf.Max(34, kt.preferredWidth + 14);
                    var lt = UiKit.Text(b, "Label", TextStyle.Display, 12f, Color.white, TextAlignmentOptions.MidlineLeft);
                    lt.text = pr.Value; lt.ForceMeshUpdate();
                    float total = kw + 9 + lt.preferredWidth, bl = -total / 2;
                    var kf = UiKit.Box(b, "KeyFill", primary ? UiKit.Gold : UiKit.Dim, UiKit.Pill, true, 8f);
                    UiKit.Place(kf.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(bl, 0), new Vector2(kw, 22));
                    kt.transform.SetAsLastSibling();
                    UiKit.Place(kt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(bl, 0), new Vector2(kw, 22));
                    UiKit.Place(lt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 0.5f), new Vector2(bl + kw + 9, 0), new Vector2(lt.preferredWidth + 6, 24));
                    _buttons.Add(b);
                    bi++;
                }
            }
            else _buttonGroup = null;

            // continue prompt (bottom right of the screen): the key-less prompts only
            var labelOnly = new List<KeyValuePair<string, string>>();
            foreach (var pr in d.Prompts) if (string.IsNullOrEmpty(pr.Key)) labelOnly.Add(pr);
            _prompt = UiKit.Rect("Prompt", _root);
            UiKit.Place(_prompt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-1280 * 0.032f, 720 * 0.034f), new Vector2(400, 24));
            _promptGroup = _prompt.gameObject.AddComponent<CanvasGroup>(); _promptGroup.alpha = 0;
            float px = 0;
            // lay the chips out right to left
            for (int i = labelOnly.Count - 1; i >= 0; i--)
            {
                var lab = UiKit.Text(_prompt, "Label", TextStyle.Label, 16f * 0.72f, UiKit.Dim, TextAlignmentOptions.Right, italic: false);
                lab.text = labelOnly[i].Value; lab.characterSpacing = 0; lab.ForceMeshUpdate();
                lab.rectTransform.anchorMin = lab.rectTransform.anchorMax = new Vector2(1, 0.5f); lab.rectTransform.pivot = new Vector2(1, 0.5f);
                lab.rectTransform.sizeDelta = new Vector2(lab.preferredWidth + 4, 20); lab.rectTransform.anchoredPosition = new Vector2(-px, 0);
                px += lab.preferredWidth + 7;
                if (string.IsNullOrEmpty(labelOnly[i].Key)) continue;
                var key = UiKit.Rect("Key", _prompt);
                var kt = UiKit.Text(key, "KeyText", TextStyle.Label, 16f * 0.72f, Color.white, TextAlignmentOptions.Center, italic: false);
                kt.text = labelOnly[i].Key; kt.characterSpacing = 0; kt.ForceMeshUpdate();
                float kw = Mathf.Max(33.6f, kt.preferredWidth + 16f);
                key.anchorMin = key.anchorMax = new Vector2(1, 0.5f); key.pivot = new Vector2(1, 0.5f);
                key.sizeDelta = new Vector2(kw, 24); key.anchoredPosition = new Vector2(-px, 0);
                var kf = UiKit.Box(key, "Fill", new Color(1, 1, 1, 0.13f), UiKit.Pill, true, 7f); UiKit.Stretch(kf.rectTransform);
                var kb = UiKit.Box(key, "Border", new Color(1, 1, 1, 0.2f), UiKit.PillRing(1f * 24f / 7f), true, 7f); UiKit.Stretch(kb.rectTransform);
                kf.transform.SetAsFirstSibling();
                UiKit.Stretch(kt.rectTransform);
                px += kw + 18;
            }
            _built = true;
            Reveal(0);
        }

        static float FmtScoreWidth(int score) => (Fmt(score).Length) * 14.4f + 4;

        public static string Fmt(int n)
        {
            n = Mathf.Max(0, n);
            string s = n.ToString(System.Globalization.CultureInfo.InvariantCulture), o = "";
            for (int i = 0; i < s.Length; i++) { if (i > 0 && (s.Length - i) % 3 == 0) o += ","; o += s[i]; }
            return o;
        }

        // ------------------------------------------------------------ pieces

        static void Mid(RectTransform rt, float cx, float yFromTop, float w, float h)
            => UiKit.Place(rt, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(cx, -yFromTop), new Vector2(w, h));

        static void Anchor(RectTransform rt, float x, float yFromTop, float w, float h, float pivotX)
            => UiKit.Place(rt, new Vector2(0.5f, 1), new Vector2(pivotX, 0.5f), new Vector2(x, -yFromTop), new Vector2(w, h));

        void StatRow(int row, float yTop, float at, (string label, string value)[] cells, float numCap, int cols = 5)
        {
            float inner = CardW - 2 * Pad, colW = inner / 5f;
            float x0 = -inner / 2 + colW / 2;
            if (cells.Length < 5) x0 = -colW * (cells.Length - 1) / 2f;
            for (int i = 0; i < cells.Length; i++)
            {
                var cell = UiKit.Rect("Stat", _card);
                Mid(cell, x0 + i * colW, yTop + 17f, colW, 40);
                var g = cell.gameObject.AddComponent<CanvasGroup>(); g.alpha = 0;
                var n = UiKit.Text(cell, "N", TextStyle.Display, numCap * 0.8f, Color.white, TextAlignmentOptions.Center);
                n.rectTransform.anchorMin = n.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                n.rectTransform.sizeDelta = new Vector2(colW, 30); n.rectTransform.anchoredPosition = new Vector2(0, 8);
                n.text = cells[i].value;
                var l = UiKit.Text(cell, "L", TextStyle.Label, 11f * 0.72f, UiKit.Dim, TextAlignmentOptions.Center, italic: false);
                l.characterSpacing = 8f;
                l.rectTransform.anchorMin = l.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                l.rectTransform.sizeDelta = new Vector2(colW, 16); l.rectTransform.anchoredPosition = new Vector2(0, -12);
                l.text = cells[i].label;
                _items.Add(new Item { Rt = cell, Group = g, At = at + i * 0.08f, Pos = cell.anchoredPosition });
            }
        }

        /// EARLY | ON | LATE across a track, the marker at `lean` (-1..1 of the half width), the words under it.
        void LeanGauge(float lean, string label, float yTop, float at)
        {
            const float W = 380, H = 14;
            var row = UiKit.Rect("Lean", _card);
            Mid(row, 0, yTop + 24f, W + 160, 48);
            var g = row.gameObject.AddComponent<CanvasGroup>(); g.alpha = 0;
            var track = UiKit.Rect("Track", row);
            UiKit.Place(track, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -12), new Vector2(W, H));
            var back = UiKit.Box(track, "Back", new Color(0.02f, 0.01f, 0.05f, 0.9f), UiKit.Pill, true, 7f); UiKit.Stretch(back.rectTransform);
            Color early = UiKit.Hex(0x6fd0ff), late = UiKit.Hex(0xffa04a), on = UiKit.Hex(0xb6f27a);
            var el = UiKit.Box(track, "Early", new Color(early.r, early.g, early.b, 0.45f), UiKit.Pill, true, 7f);
            el.rectTransform.anchorMin = Vector2.zero; el.rectTransform.anchorMax = new Vector2(0.5f, 1); el.rectTransform.offsetMin = new Vector2(2, 2); el.rectTransform.offsetMax = new Vector2(0, -2);
            var lt = UiKit.Box(track, "Late", new Color(late.r, late.g, late.b, 0.45f), UiKit.Pill, true, 7f);
            lt.rectTransform.anchorMin = new Vector2(0.5f, 0); lt.rectTransform.anchorMax = Vector2.one; lt.rectTransform.offsetMin = new Vector2(0, 2); lt.rectTransform.offsetMax = new Vector2(-2, -2);
            var oz = UiKit.Box(track, "On", new Color(on.r, on.g, on.b, 0.85f), UiKit.Pill, true, 5f);
            UiKit.Place(oz.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W * 0.33f, H - 2));
            var mid = UiKit.Box(track, "Mid", Color.white); UiKit.Place(mid.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2, H + 8));
            var mk = UiKit.Box(track, "Marker", Mathf.Abs(lean) < 0.16f ? on : lean < 0 ? early : late, UiKit.Pill, true, 4f);
            UiKit.Place(mk.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(Mathf.Clamp(lean, -1, 1) * W / 2, 0), new Vector2(18, 18));
            mk.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            var le = UiKit.Text(row, "EarlyL", TextStyle.Label, 11f * 0.72f, early, TextAlignmentOptions.MidlineRight, italic: false);
            UiKit.Place(le.rectTransform, new Vector2(0.5f, 1), new Vector2(1, 0.5f), new Vector2(-W / 2 - 10, -12), new Vector2(80, 18)); le.text = "EARLY"; le.characterSpacing = 6f;
            var ll = UiKit.Text(row, "LateL", TextStyle.Label, 11f * 0.72f, late, TextAlignmentOptions.MidlineLeft, italic: false);
            UiKit.Place(ll.rectTransform, new Vector2(0.5f, 1), new Vector2(0, 0.5f), new Vector2(W / 2 + 10, -12), new Vector2(80, 18)); ll.text = "LATE"; ll.characterSpacing = 6f;
            var lab = UiKit.Text(row, "Label", TextStyle.Label, 12f * 0.72f, UiKit.Dim, TextAlignmentOptions.Center, italic: false);
            UiKit.Place(lab.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), new Vector2(0, -36), new Vector2(W + 140, 18)); lab.text = label ?? ""; lab.characterSpacing = 6f;
            _items.Add(new Item { Rt = row, Group = g, At = at, Pos = row.anchoredPosition });
        }

        void PartyLine(PartyRow r, float yTop, float at)
        {
            float inner = CardW - 2 * Pad;
            var row = UiKit.Rect("PartyRow", _card);
            Mid(row, 0, yTop + 12f, inner, 26);
            var g = row.gameObject.AddComponent<CanvasGroup>(); g.alpha = 0;
            var bg = UiKit.Box(row, "Bg", r.You ? new Color(1, 229 / 255f, 138 / 255f, 0.12f) : new Color(1, 1, 1, 0.04f), UiKit.Pill, true, 8f);
            UiKit.Stretch(bg.rectTransform);
            var bar = UiKit.Box(row, "Bar", r.Color, UiKit.Pill, true, 2f);
            bar.rectTransform.anchorMin = new Vector2(0, 0); bar.rectTransform.anchorMax = new Vector2(0, 1); bar.rectTransform.pivot = new Vector2(0, 0.5f);
            bar.rectTransform.sizeDelta = new Vector2(4, 0); bar.rectTransform.anchoredPosition = Vector2.zero;
            var pl = UiKit.Text(row, "Place", TextStyle.Label, 15f * 0.72f, UiKit.Dim, TextAlignmentOptions.MidlineLeft, italic: false);
            pl.characterSpacing = 6f; pl.text = r.Place; PlaceCell(pl.rectTransform, 12, 60);
            var nm = UiKit.Text(row, "Name", TextStyle.Label, 15f * 0.72f, r.Color, TextAlignmentOptions.MidlineLeft, italic: false);
            nm.characterSpacing = 6f; nm.text = r.Name + (r.You ? " - YOU" : ""); PlaceCell(nm.rectTransform, 12 + 60 + 10, 300);
            var pp = UiKit.Text(row, "Points", TextStyle.Label, 15f * 0.72f, Color.white, TextAlignmentOptions.MidlineRight, italic: false);
            pp.characterSpacing = 4f; pp.text = "+" + r.Points; PlaceCell(pp.rectTransform, inner - 12 - 80, 80);
            _items.Add(new Item { Rt = row, Group = g, At = at, Pos = row.anchoredPosition });
        }

        static void PlaceCell(RectTransform rt, float x, float w)
        {
            rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0); rt.sizeDelta = new Vector2(w, 24);
        }

        // ------------------------------------------------------------ reveal

        /// Everything as it is `t` seconds after the round ended.
        public void Reveal(float t)
        {
            if (!_built) return;
            _t = t;
            bool calm = Settings.ReduceMotion;
            float k = UiKit.Clamp01((t - CountAt) / CountS);
            int shown = Mathf.RoundToInt(_score * (1 - Mathf.Pow(1 - k, 3)));
            if (shown != _lastShown) { _lastShown = shown; _scoreText.text = Fmt(shown); }

            foreach (var it in _items)
            {
                float e = t - it.At;
                float a = UiKit.Clamp01(e / 0.2f);
                float s = calm ? 1 : UiKit.BackOut(e / 0.28f, 1.6f);
                it.Group.alpha = a;
                it.Rt.anchoredPosition = it.Pos + new Vector2(0, calm ? 0 : -(1 - s) * 8f);
            }

            float st = t - _stampAt;
            var cg = _rankBox.GetComponent<CanvasGroup>();
            if (st < 0) { cg.alpha = 0; _rankBox.localScale = Vector3.one * 2.6f; }
            else
            {
                float f = UiKit.Clamp01(st / 0.22f);
                cg.alpha = calm ? UiKit.Clamp01(st / 0.15f) : UiKit.Clamp01(st / 0.08f);
                float b = calm ? 1 : UiKit.BackOut(f, 2.4f);
                _rankBox.localScale = Vector3.one * (calm ? 1 : Mathf.LerpUnclamped(2.6f, 1, b));
                _rankBox.localRotation = Quaternion.Euler(0, 0, calm ? 0 : Mathf.LerpUnclamped(-14, 0, b));
            }
            if (_badge != null)
            {
                float e = t - _badgeAt;
                var bg = _badge.GetComponent<CanvasGroup>();
                bg.alpha = UiKit.Clamp01(e / 0.2f);
                float b = calm ? 1 : UiKit.BackOut(e / 0.28f, 1.6f);
                _badge.localScale = Vector3.one * (calm ? 1 : Mathf.LerpUnclamped(1.9f, 1, b));
                _badge.localRotation = Quaternion.Euler(0, 0, _badgeTilt ? -10 : 0);
            }
            _promptGroup.alpha = UiKit.Clamp01((t - PromptAt) / 0.25f);
            if (_buttonGroup != null) _buttonGroup.alpha = _promptGroup.alpha;
            float breathe = calm ? 1 : 1 + Mathf.Sin(t * 3) * 0.006f;
            _card.localScale = Vector3.one * breathe;
        }

        int _lastShown = -1;

        static readonly Vector3[] _corners = new Vector3[4];
        /// Button i (in the order of the keyed prompts) in IMGUI screen coordinates (y down), for the host's click targets.
        public Rect ButtonScreen(int i)
        {
            if (i < 0 || i >= _buttons.Count || !_buttons[i]) return Rect.zero;
            _buttons[i].GetWorldCorners(_corners);
            return new Rect(_corners[0].x, Screen.height - _corners[1].y, _corners[2].x - _corners[0].x, _corners[1].y - _corners[0].y);
        }
    }
}
