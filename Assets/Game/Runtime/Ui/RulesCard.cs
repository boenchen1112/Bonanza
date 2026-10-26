// The rules card shown before a round: a dimmed scene, a framed panel in the HUD/results font with the game's name,
// its one-line pitch and the rules as icon rows (one numbered gold badge per line), then a pulsing PRESS TO START
// prompt with the back hint as a key glyph. Built once per round (not in play), so it may allocate.

using TMPro;
using UnityEngine;

namespace Bonanza.HudKit
{
    public sealed class RulesCard : MonoBehaviour
    {
        RectTransform _root, _prompt;
        CanvasGroup _promptGroup;
        float _t;

        public static RulesCard Create(Transform parent)
        {
            var rt = UiKit.Rect("Rules", parent); UiKit.Stretch(rt);
            var c = rt.gameObject.AddComponent<RulesCard>();
            c._root = rt;
            rt.gameObject.SetActive(false);
            return c;
        }

        public bool Visible => _root && _root.gameObject.activeSelf;

        public void Hide() { if (_root) _root.gameObject.SetActive(false); }

        public void Show(string title, string blurb, string controls)
        {
            foreach (Transform ch in _root) Destroy(ch.gameObject);
            _root.gameObject.SetActive(true);
            _t = 0;
            var dim = UiKit.Box(_root, "Dim", new Color(8 / 255f, 7 / 255f, 20 / 255f, 0.72f));
            UiKit.Stretch(dim.rectTransform);

            var lines = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(controls))
                foreach (var l in controls.Split('\n')) if (l.Trim().Length > 0) lines.Add(l.Trim());

            const float W = 760, Pad = 34, BadgeW = 34, TextX = Pad + BadgeW + 14, TextW = W - TextX - Pad;
            float rowCap = lines.Count > 4 ? 14.5f : 16f;
            // measure first: the rows wrap, the panel is as tall as they need
            var rows = new System.Collections.Generic.List<TextMeshProUGUI>();
            var heights = new System.Collections.Generic.List<float>();
            var measure = _root;
            foreach (var l in lines)
            {
                var t = UiKit.Text(_root, "Row", TextStyle.Label, rowCap, Color.white, TextAlignmentOptions.TopLeft, italic: false, upper: false);
                t.textWrappingMode = TextWrappingModes.Normal;
                t.text = l; t.characterSpacing = 0;
                t.rectTransform.sizeDelta = new Vector2(TextW, 40);
                t.ForceMeshUpdate();
                float h = Mathf.Max(BadgeW, t.GetPreferredValues(l, TextW, 0).y + 2);
                rows.Add(t); heights.Add(h);
            }
            float y = Pad;
            float titleY = y; y += 44 + 6;
            var bl = UiKit.Text(_root, "Blurb", TextStyle.Display, 19f, Color.white, TextAlignmentOptions.Top);
            bl.textWrappingMode = TextWrappingModes.Normal;
            bl.text = blurb ?? ""; bl.rectTransform.sizeDelta = new Vector2(W - 2 * Pad, 40);
            bl.ForceMeshUpdate();
            float blurbH = Mathf.Max(28, bl.GetPreferredValues(bl.text, W - 2 * Pad, 0).y);
            float blurbY = y; y += blurbH + 16;
            float dividerY = y; y += 1 + 16;
            float rowsY = y;
            foreach (var h in heights) y += h + 12;
            y += 12 + 36 + Pad - 8;   // prompt row
            float H = y;

            var card = UiKit.Rect("Card", _root);
            UiKit.Place(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(W, H));
            var shadow = UiKit.Box(card, "Shadow", new Color(0, 0, 0, 0.55f), UiKit.Pill, true, 18f);
            UiKit.Stretch(shadow.rectTransform); shadow.rectTransform.anchoredPosition = new Vector2(0, -7);
            var body = UiKit.Box(card, "Body", Color.white, UiKit.Pill, true, 18f); UiKit.Stretch(body.rectTransform);
            body.gameObject.AddComponent<UiGradient>().Set(UiKit.Panel, UiKit.Panel2);
            var border = UiKit.Box(card, "Border", new Color(1, 0.85f, 0.4f, 0.55f), UiKit.PillRing(2.5f * 24f / 18f), true, 18f); UiKit.Stretch(border.rectTransform);
            var hi = UiKit.Box(card, "Rim", new Color(1, 1, 1, 0.14f), UiKit.Pill, true, 18f);
            var hr = hi.rectTransform; hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1); hr.pivot = new Vector2(0.5f, 1);
            hr.sizeDelta = new Vector2(-30, 2); hr.anchoredPosition = new Vector2(0, -2);

            var tt = UiKit.Text(card, "Title", TextStyle.Display, 32f, UiKit.Gold, TextAlignmentOptions.Center);
            TopMid(tt.rectTransform, titleY, W - 2 * Pad, 44); tt.text = title;
            TopMid(bl.rectTransform, blurbY, W - 2 * Pad, blurbH); bl.transform.SetParent(card, false);
            var div = UiKit.Box(card, "Divider", new Color(1, 1, 1, 0.12f)); TopMid(div.rectTransform, dividerY, W - 2 * Pad, 1);
            float ry = rowsY;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i]; r.transform.SetParent(card, false);
                r.rectTransform.anchorMin = r.rectTransform.anchorMax = new Vector2(0, 1); r.rectTransform.pivot = new Vector2(0, 1);
                r.rectTransform.sizeDelta = new Vector2(TextW, heights[i]); r.rectTransform.anchoredPosition = new Vector2(TextX, -(ry + 3));
                var badge = UiKit.Box(card, "Badge", UiKit.Gold, UiKit.Disc);
                badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(0, 1); badge.rectTransform.pivot = new Vector2(0, 0.5f);
                badge.rectTransform.sizeDelta = new Vector2(BadgeW, BadgeW); badge.rectTransform.anchoredPosition = new Vector2(Pad, -(ry + BadgeW * 0.5f));
                var n = UiKit.Text(badge.rectTransform, "N", TextStyle.Display, 17f, UiKit.Ink, TextAlignmentOptions.Center);
                UiKit.Stretch(n.rectTransform); n.text = (i + 1).ToString();
                ry += heights[i] + 12;
            }

            // prompt row: the pulsing START prompt in the middle, the back hint as a key glyph at the right
            _prompt = UiKit.Rect("Prompt", card);
            UiKit.Place(_prompt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(300, 40));
            _promptGroup = _prompt.gameObject.AddComponent<CanvasGroup>();
            var pf = UiKit.Box(_prompt, "Fill", Color.white, UiKit.Pill, true, 14f); UiKit.Stretch(pf.rectTransform);
            pf.gameObject.AddComponent<UiGradient>().Set(new Color(1, 0.9f, 0.55f, 0.42f), new Color(1, 0.75f, 0.3f, 0.25f));
            var pr = UiKit.Box(_prompt, "Rim", UiKit.Gold, UiKit.PillRing(2f * 24f / 14f), true, 14f); UiKit.Stretch(pr.rectTransform);
            var pt = UiKit.Text(_prompt, "Text", TextStyle.Display, 17f, Color.white, TextAlignmentOptions.Center);
            UiKit.Stretch(pt.rectTransform); pt.text = "PRESS TO START";

            var back = UiKit.Rect("Back", card);
            UiKit.Place(back, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-Pad, 26), new Vector2(120, 28));
            var bt = UiKit.Text(back, "Label", TextStyle.Label, 11.5f, UiKit.Dim, TextAlignmentOptions.Right, italic: false);
            bt.text = "back"; bt.characterSpacing = 0;
            bt.rectTransform.anchorMin = bt.rectTransform.anchorMax = new Vector2(1, 0.5f); bt.rectTransform.pivot = new Vector2(1, 0.5f);
            bt.rectTransform.sizeDelta = new Vector2(50, 20); bt.rectTransform.anchoredPosition = Vector2.zero;
            var key = UiKit.Rect("Key", back);
            key.anchorMin = key.anchorMax = new Vector2(1, 0.5f); key.pivot = new Vector2(1, 0.5f);
            key.sizeDelta = new Vector2(44, 26); key.anchoredPosition = new Vector2(-50, 0);
            var kf = UiKit.Box(key, "Fill", new Color(1, 1, 1, 0.13f), UiKit.Pill, true, 7f); UiKit.Stretch(kf.rectTransform);
            var kb = UiKit.Box(key, "Border", new Color(1, 1, 1, 0.3f), UiKit.PillRing(1f * 24f / 7f), true, 7f); UiKit.Stretch(kb.rectTransform);
            var kt = UiKit.Text(key, "KeyText", TextStyle.Label, 11.5f, Color.white, TextAlignmentOptions.Center, italic: false);
            kt.text = "ESC"; kt.characterSpacing = 0; UiKit.Stretch(kt.rectTransform);
        }

        void Update()
        {
            if (!Visible || _prompt == null) return;
            _t += Time.unscaledDeltaTime;
            float p = Settings.ReduceMotion ? 1f : 0.5f + 0.5f * Mathf.Sin(_t * 4.2f);
            _promptGroup.alpha = Settings.ReduceMotion ? 1f : 0.7f + 0.3f * p;
            _prompt.localScale = Vector3.one * (Settings.ReduceMotion ? 1f : 1f + 0.04f * p);
        }

        static void TopMid(RectTransform rt, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(0, -y);
        }
    }
}
