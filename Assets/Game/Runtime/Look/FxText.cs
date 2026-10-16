// World-space callouts (web fx/poptext.js and the Swing Kings badge): words that
// belong to a PLACE, erupting where the ball was hit, with a dark rounded plate so
// they read over a busy crowd. TextMeshPro (3D text, SDF, ASCII from the OFL Lilita One
// atlas in Resources/Look/CalloutFont) on a depth-ignoring overlay material, pooled and
// prewarmed. Aged on the caller's clock: Fx.Update passes REAL elapsed time, so a callout
// spawned on the hit keeps popping through the hitstop instead of freezing mid-pop.

using TMPro;
using UnityEngine;

namespace Bonanza.Look
{
    sealed class CalloutPool
    {
        sealed class Item
        {
            public GameObject Go, SubGo; public TextMeshPro Tmp, Sub; public Transform Tr, SubTr;
            public bool On; public float Age, Life, Rise, Size, Tilt, Spin, PlateW, PlateH, Unit = 1;
            public float Em, SubEm, MainY, SubY, Alpha = 1, SubAlpha = 1;   // badge layout (web makeWordTexture), world units at scale 1
            public bool HasSub;
            public Vector3 Pos; public Color Color, Stroke; public bool Plate;
        }

        readonly Item[] _items;
        readonly PlateBatch _plates;
        // The plate shader and Stroke take LINEAR colours (custom shader, linear project). TextMeshPro text colours
        // (vertex gradient, colour) are given as sRGB: TMP linearises vertex colours itself in a linear project.
        // The plate fill goes to the shader as sRGB bytes (rgb(16,11,40)) and is decoded there: a linear value in a UNorm8
        // vertex colour quantises to (13,13,41).
        static readonly Color PlateFill = new Color(16 / 255f, 11 / 255f, 40 / 255f);
        const float HudTopNdc = 0.6f;   // web HUD_TOP_NDC: a callout's top edge never rises into the HUD band

        // Web world.js WORD_GRAD: c0 (top), c1, c2 (bottom, also the plate stroke).
        static readonly System.Collections.Generic.Dictionary<string, Color[]> WordGrad = new System.Collections.Generic.Dictionary<string, Color[]>
        {
            { "BUNT", G(0xffffff, 0x9ff0ff, 0x3cc4ff) },
            { "LINE DRIVE", G(0xffffff, 0xfff39a, 0xffc21a) },
            { "HOME RUN!", G(0xffffff, 0xffb27a, 0xff5a3c) },
            { "GRAND SLAM!", G(0xffffff, 0xfff3cf, 0xffd35a) },
            { "FOUL!", G(0xffffff, 0xe2c4ff, 0xa86bff) },
            { "OUT!", G(0xffffff, 0xffb0bd, 0xff5d73) },
            { "LIKE THIS!", G(0xffffff, 0xc8ffb0, 0x6fe37a) },
            { "WHIFF!", G(0xffffff, 0xffc0d8, 0xff5d8a) },
            { "STRIKE!", G(0xffffff, 0xffd9b0, 0xff8a3c) },
            { "SIDE RETIRED!", G(0xffffff, 0xffb0bd, 0xff4d6a) },
        };
        static Color[] G(uint a, uint b, uint c) => new[] { Feel.Hex(a), Feel.Hex(b), Feel.Hex(c) };

        public CalloutPool(Transform parent, int max, Material plateMat, Material textMat, TMP_FontAsset font)
        {
            _plates = new PlateBatch(parent, plateMat, max, 40);
            _items = new Item[max];
            for (int i = 0; i < max; i++)
            {
                var go = new GameObject("Callout", typeof(TextMeshPro));
                go.transform.SetParent(parent, false);
                var t = go.GetComponent<TextMeshPro>();
                if (font) t.font = font;
                if (textMat) t.fontSharedMaterial = textMat;
                t.fontSize = 10;
                t.fontStyle = FontStyles.Normal;
                t.alignment = TextAlignmentOptions.Center;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Overflow;
                t.richText = true;
                t.enableVertexGradient = true;
                t.raycastTarget = false;
                t.rectTransform.sizeDelta = new Vector2(40, 10);
                t.text = " ";
                var mr = go.GetComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.sortingOrder = 41;   // above the plates (40): sorting order outranks the render queue
                go.SetActive(false);
                var sgo = new GameObject("CalloutSub", typeof(TextMeshPro));
                sgo.transform.SetParent(parent, false);
                var st = sgo.GetComponent<TextMeshPro>();
                if (font) st.font = font;
                if (textMat) st.fontSharedMaterial = textMat;
                st.fontSize = 10; st.fontStyle = FontStyles.Normal; st.alignment = TextAlignmentOptions.Center;
                st.textWrappingMode = TextWrappingModes.NoWrap; st.overflowMode = TextOverflowModes.Overflow;
                st.richText = false; st.raycastTarget = false;
                st.rectTransform.sizeDelta = new Vector2(40, 10);
                st.text = " ";
                var smr = sgo.GetComponent<MeshRenderer>();
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.receiveShadows = false;
                smr.sortingOrder = 41;
                sgo.SetActive(false);
                _items[i] = new Item { Go = go, Tmp = t, Tr = go.transform, SubGo = sgo, Sub = st, SubTr = sgo.transform };
            }
        }

        int Slot()
        {
            int oldest = 0; float worst = -1;
            for (int i = 0; i < _items.Length; i++)
            {
                if (!_items[i].On) return i;
                float f = _items[i].Age / _items[i].Life;
                if (f > worst) { worst = f; oldest = i; }
            }
            return oldest;   // recycle the one closest to done
        }

        // Web world.js makeWordTexture, in units of the sprite scale s (sprite 3.1s wide = 512 px, so 1 px = 0.006055 s).
        const float PlateWk = 2.93f, PlateHSub = 0.975f, PlateHPlain = 0.675f;
        // TMP's em runs ~6% small against the browser's (measured: HOME RUN word 83% of the plate wide here vs 88% on the web).
        const float EmK = 1.06f;
        const float EmSub = 0.4625f * EmK, EmPlain = 0.4875f * EmK, EmSubLine = 0.225f * EmK;
        const float MaxTextW = 2.676f;      // (512 - 70) px: the web shrinks the word until it fits
        const float MainYSub = 0.109f, SubLineY = -0.266f;   // main word centre 18 px above the plate centre; sub line 44 px below

        /// `sub` (smaller second line) may be null. Plate badges: `size` is the web sprite scale (0.8 / 1.05 / 1.75). Plain words
        /// (PopWord): `size` is the world height of the word's line box.
        public void Spawn(string word, string sub, Vector3 pos, Color color, float size, float life, float rise, float tilt, float spin, bool plate)
        {
            var it = _items[Slot()];
            it.On = true; it.Age = 0; it.Life = Mathf.Max(0.05f, life); it.Rise = rise; it.Size = size; it.Tilt = tilt; it.Spin = spin;
            it.Pos = pos; it.Color = color; it.Plate = plate; it.HasSub = plate && sub != null;
            it.Go.SetActive(true);
            var t = it.Tmp;
            Color top, bottom;
            if (WordGrad.TryGetValue(word, out var g))
            {
                top = Color.Lerp(g[0], g[1], 0.2f); bottom = g[2];
                it.Stroke = g[2].linear;
            }
            else
            {
                top = Color.Lerp(Color.white, color, 0.2f); bottom = color;
                it.Stroke = color.linear;
            }
            t.text = word;
            t.colorGradient = new VertexGradient(top, top, bottom, bottom);
            t.color = Color.white;
            t.alpha = 1; it.Alpha = 1;
            t.ForceMeshUpdate();
            if (!plate)
            {
                // Scale so the word's line box is `size` world units tall.
                var li = t.textInfo.lineInfo[0];
                it.Unit = Mathf.Max(0.01f, li.ascender - li.descender);
                it.SubGo.SetActive(false);
                return;
            }
            // Badge: fixed plate, em from the web's pixel sizes; the word shrinks only if it would overrun the plate.
            float w1 = Mathf.Max(0.01f, t.textBounds.size.x);   // word width at em = 1 world unit (fontSize 10)
            it.Em = (it.HasSub ? EmSub : EmPlain) * size;
            if (w1 * it.Em > MaxTextW * size) it.Em = MaxTextW * size / w1;
            it.PlateW = PlateWk * size; it.PlateH = (it.HasSub ? PlateHSub : PlateHPlain) * size;
            it.MainY = it.HasSub ? MainYSub * size : 0; it.SubY = SubLineY * size;
            if (it.HasSub)
            {
                it.SubEm = EmSubLine * size;
                var st = it.Sub;
                st.text = sub;
                st.color = new Color(1f, 0xf6 / 255f, 0xd8 / 255f, 1f);
                st.alpha = 1; it.SubAlpha = 1;
                st.ForceMeshUpdate();
                it.SubGo.SetActive(true);
            }
            else it.SubGo.SetActive(false);
        }

        /// Vertex-alpha fade without a relayout (TMP's alpha setter rebuilds the mesh).
        static void SetAlpha(TextMeshPro t, ref float cur, float a)
        {
            if (Mathf.Abs(a - cur) < 1f / 512f) return;
            cur = a;
            byte b = (byte)(Mathf.Clamp01(a) * 255f + 0.5f);
            var mi = t.textInfo.meshInfo;
            for (int m = 0; m < mi.Length; m++)
            {
                var c = mi[m].colors32; int n = mi[m].vertexCount;
                if (c == null) continue;
                for (int k = 0; k < n && k < c.Length; k++) c[k].a = b;
            }
            t.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        }

        public void Update(float dt, Camera cam, Vector3 camRight, Vector3 camUp, bool prewarm)
        {
            int plates = 0;
            for (int i = 0; i < _items.Length; i++)
            {
                var it = _items[i];
                if (!it.On) continue;
                it.Age += dt;
                if (it.Age >= it.Life) { it.On = false; it.Go.SetActive(false); it.SubGo.SetActive(false); continue; }
                float t = Mathf.Clamp01(it.Age / it.Life);
                var tr = it.Tr;
                float rot = it.Tilt + it.Spin * it.Age;
                Vector3 p;
                if (it.Plate)
                {
                    // Web swingKings badge: pop from 60% with a back-out over the first 24%, slow growth, full rise, fade after 70%.
                    float f = (0.6f + 0.4f * FxColor.BackOut(Mathf.Clamp01(t / 0.24f))) * (1 + 0.12f * t);
                    float alpha = t > 0.7f ? 1 - (t - 0.7f) / 0.3f : 1;
                    p = new Vector3(it.Pos.x, it.Pos.y + FxColor.EaseOutCubic(t) * it.Rise, it.Pos.z);
                    {   // keep-below-HUD (web keepBelowHud): half = the sprite's half height
                        float half = (it.HasSub ? 1.1f : 0.8f) * it.Size * f * 0.5f;
                        var mid = cam.WorldToViewportPoint(p); var top = cam.WorldToViewportPoint(p + camUp * half);
                        float over = (top.y * 2 - 1) - HudTopNdc, per = ((top.y - mid.y) * 2) / Mathf.Max(1e-4f, half);
                        if (over > 0 && per > 1e-4f) p -= camUp * (over / per);
                    }
                    var q = cam.transform.rotation * Quaternion.Euler(0, 0, rot * Mathf.Rad2Deg);
                    float c = Mathf.Cos(rot), s = Mathf.Sin(rot);
                    var r = camRight * c + camUp * s; var u = -camRight * s + camUp * c;
                    tr.SetPositionAndRotation(p + u * (it.MainY * f), q);
                    tr.localScale = Vector3.one * (it.Em * f);
                    SetAlpha(it.Tmp, ref it.Alpha, alpha);
                    if (it.HasSub)
                    {
                        it.SubTr.SetPositionAndRotation(p + u * (it.SubY * f), q);
                        it.SubTr.localScale = Vector3.one * (it.SubEm * f);
                        SetAlpha(it.Sub, ref it.SubAlpha, alpha);
                    }
                    _plates.Plate(plates++, p, r, u, it.PlateW * f, it.PlateH * f, PlateFill, alpha, it.Stroke);
                    continue;
                }
                // overshoot in, hold, then shrink + fade: the pop reads at speed because the scale peaks BEFORE the eye arrives
                // starts at 60% (web world.js): from zero the first frames drew a speck
                float grow = t < 0.34f ? 0.6f + 0.4f * FxColor.BackOut(t / 0.34f, 2.6f) : 1 + 0.10f * Mathf.Sin((t - 0.34f) * 7.5f);
                float shrink = t > 0.78f ? 1 - (t - 0.78f) / 0.22f * 0.45f : 1;
                float sc = it.Size * grow * shrink / it.Unit;
                float y = it.Pos.y + it.Rise * (1 - (1 - t) * (1 - t)) * 0.55f;   // decelerating rise
                float a2 = t < 0.08f ? t / 0.08f : t > 0.7f ? 1 - (t - 0.7f) / 0.3f : 1;
                p = new Vector3(it.Pos.x, y, it.Pos.z);
                {
                    float half = it.Unit * sc * 0.5f;
                    var mid = cam.WorldToViewportPoint(p); var top = cam.WorldToViewportPoint(p + camUp * half);
                    float over = (top.y * 2 - 1) - HudTopNdc, per = ((top.y - mid.y) * 2) / Mathf.Max(1e-4f, half);
                    if (over > 0 && per > 1e-4f) p -= camUp * (over / per);
                }
                tr.SetPositionAndRotation(p, cam.transform.rotation * Quaternion.Euler(0, 0, rot * Mathf.Rad2Deg));
                tr.localScale = Vector3.one * sc;
                SetAlpha(it.Tmp, ref it.Alpha, a2);
            }
            if (prewarm && plates == 0) _plates.Plate(plates++, new Vector3(0, -500, 0), camRight, camUp, 0.01f, 0.01f, PlateFill, 0.01f, Color.white);
            _plates.Flush(plates);
        }

        public void KillAll()
        {
            foreach (var it in _items) { it.On = false; it.Go.SetActive(false); it.SubGo.SetActive(false); }
            _plates.Flush(0);
        }

        /// One invisible callout shown for a frame, so the text shader and font atlas are touched before play.
        public void Prewarm(Vector3 pos, Camera cam)
        {
            var it = _items[0];
            Spawn("PERFECT!", "ABCXYZ 0123456789 \u00B7 FT", pos, Color.white, 0.01f, 0.2f, 0, 0, 0, true);
            SetAlpha(it.Tmp, ref it.Alpha, 0.01f); SetAlpha(it.Sub, ref it.SubAlpha, 0.01f);
        }

        public void Destroy() { _plates.Destroy(); }
    }
}
