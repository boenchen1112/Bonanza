// The procedural sprite atlas for the pooled VFX (web render/fx/textures.js).
// Drawn once at load into one 512x512 texture (4x4 cells of 128 px) so every
// family shares one texture and no clip is fetched. Mipmaps are off: a mip chain
// bleeds neighbouring cells into each other.

using UnityEngine;

namespace Bonanza.Look
{
    public static class FxSprites
    {
        public const int Size = 512, Grid = 4, Cell = Size / Grid, Pad = 6;

        public enum Cell_ { Glow, Sparkle, Streak, Star6, Scorch, Confetti, Shard, Smoke, Solid, Puff }

        static Texture2D _tex;

        /// UV rect (u0, v0, du, dv) of a cell, inset by the guard band.
        public static Vector4 Rect(Cell_ c)
        {
            int cx = (int)c % Grid, cy = (int)c / Grid;
            float u0 = (cx * Cell + Pad) / (float)Size, v0 = (cy * Cell + Pad) / (float)Size;
            return new Vector4(u0, v0, (Cell - 2f * Pad) / Size, (Cell - 2f * Pad) / Size);
        }

        public static Texture2D Atlas => _tex ? _tex : _tex = Build();

        static float Ramp(float t, float[] stops)
        {
            // stops = t0,a0, t1,a1, ...  linear between
            if (t <= stops[0]) return stops[1];
            for (int i = 2; i < stops.Length; i += 2)
                if (t <= stops[i]) { float k = (t - stops[i - 2]) / (stops[i] - stops[i - 2]); return Mathf.Lerp(stops[i - 1], stops[i + 1], k); }
            return stops[stops.Length - 1];
        }

        static float Union(float a, float b) => 1 - (1 - a) * (1 - b);

        /// Tapered spike from the centre outward (a filled triangle with an alpha gradient).
        static float Spike(float x, float y, float angle, float len, float hw, float alpha)
        {
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            float u = x * c + y * s, v = -x * s + y * c;
            if (u < 0 || u > len) return 0;
            float w = hw * (1 - u / len);
            float edge = Mathf.Clamp01((w - Mathf.Abs(v)) / 1.2f + 0.5f);
            float prof = Ramp(u / len, new[] { 0f, 1f, 0.35f, 0.5f, 1f, 0f });
            return edge * alpha * prof;
        }

        static float RoundRect(float x, float y, float hw, float hh, float r)
        {
            float qx = Mathf.Abs(x) - (hw - r), qy = Mathf.Abs(y) - (hh - r);
            float d = Mathf.Sqrt(Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qy, 0) * Mathf.Max(qy, 0)) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
            return Mathf.Clamp01(0.5f - d);
        }

        static Texture2D Build()
        {
            var px = new Color32[Size * Size];
            var rng = new System.Random(0xfeed);
            // scorch / smoke blob jitter, fixed per atlas
            var blobs = new float[9, 3];
            for (int i = 0; i < 9; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2, d = (float)rng.NextDouble() * 0.30f, r = 0.34f + (float)rng.NextDouble() * 0.30f;
                blobs[i, 0] = Mathf.Cos(a) * d; blobs[i, 1] = Mathf.Sin(a) * d; blobs[i, 2] = r;
            }
            var puffs = new float[7, 3];
            for (int i = 0; i < 7; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2, d = (float)rng.NextDouble() * 0.28f, r = 0.26f + (float)rng.NextDouble() * 0.2f;
                puffs[i, 0] = Mathf.Cos(a) * d; puffs[i, 1] = Mathf.Sin(a) * d; puffs[i, 2] = r;
            }

            float S = Cell, c = S / 2;
            foreach (Cell_ cell in System.Enum.GetValues(typeof(Cell_)))
            {
                int cx = (int)cell % Grid, cy = (int)cell / Grid;
                for (int py = 0; py < Cell; py++)
                    for (int pxl = 0; pxl < Cell; pxl++)
                    {
                        float x = pxl + 0.5f - c, y = py + 0.5f - c;   // centred, y up
                        float rr = Mathf.Sqrt(x * x + y * y);
                        float a = 0, shade = 1;
                        switch (cell)
                        {
                            case Cell_.Glow:
                                a = Ramp(rr / c, new[] { 0f, 1f, 0.18f, 0.92f, 0.45f, 0.34f, 0.75f, 0.07f, 1f, 0f });
                                break;
                            case Cell_.Sparkle:
                                for (int k = 0; k < 4; k++) a = Union(a, Spike(x, y, k * Mathf.PI / 2, c * 0.98f, S * 0.045f, 0.95f));
                                for (int k = 0; k < 4; k++) a = Union(a, Spike(x, y, Mathf.PI / 4 + k * Mathf.PI / 2, c * 0.44f, S * 0.03f, 0.55f));
                                a = Union(a, Ramp(rr / (c * 0.30f), new[] { 0f, 1f, 0.5f, 0.75f, 1f, 0f }));
                                break;
                            case Cell_.Streak:
                                a = Ramp(Mathf.Sqrt(x * x + (y / 0.17f) * (y / 0.17f)) / c, new[] { 0f, 1f, 0.35f, 0.7f, 0.72f, 0.16f, 1f, 0f });
                                a = Union(a, Mathf.Clamp01(1 - Mathf.Sqrt(x * x + (y / 0.05f) * (y / 0.05f)) / (c * 0.8f)));
                                break;
                            case Cell_.Star6:
                                for (int k = 0; k < 6; k++) a = Union(a, Spike(x, y, k * Mathf.PI / 3, c * 0.97f, S * 0.055f, 0.9f));
                                a = Union(a, Ramp(rr / (c * 0.42f), new[] { 0f, 1f, 0.42f, 0.8f, 1f, 0f }));
                                break;
                            case Cell_.Scorch:
                                for (int k = 0; k < 9; k++)
                                {
                                    float bx = x - blobs[k, 0] * c, by = y - blobs[k, 1] * c;
                                    a = Union(a, Ramp(Mathf.Sqrt(bx * bx + by * by) / (c * blobs[k, 2]), new[] { 0f, 0.34f, 0.55f, 0.14f, 1f, 0f }));
                                }
                                a = Union(a, Ramp(rr / (c * 0.34f), new[] { 0f, 0.9f, 1f, 0f }));
                                break;
                            case Cell_.Confetti:
                            {
                                float m = S * 0.12f, w = S - 2 * m, h = S * 0.62f;
                                a = RoundRect(x, y, w / 2, h / 2, S * 0.07f);
                                // sheen band: darker rgb, still opaque, so a tumbling face catches "light"
                                float t = Mathf.Clamp01(((x + w / 2) / w + (y + h / 2) / h) / 2);
                                float band = t < 0.45f ? t / 0.45f * 0.85f : t < 0.62f ? 0.85f * (1 - (t - 0.45f) / 0.17f) : 0;
                                shade = 1 - band * (1 - 196 / 255f);   // a soft sheen, not a grey half
                                break;
                            }
                            case Cell_.Shard:
                            {
                                // an elongated diamond along +x with a hot tip: faint tail, bright head
                                float u = (x + c * 0.9f) / (c * 1.8f);              // 0 tail .. 1 tip
                                float hw = S * 0.13f * (u < 0.55f ? u / 0.55f : (1 - u) / 0.45f);
                                if (u > 0 && u < 1) a = Mathf.Clamp01((hw - Mathf.Abs(y)) / 1.2f + 0.5f) * Mathf.Lerp(0.3f, 1f, u);
                                break;
                            }
                            case Cell_.Smoke:
                                for (int k = 0; k < 7; k++)
                                {
                                    float bx = x - puffs[k, 0] * c, by = y - puffs[k, 1] * c;
                                    a = Union(a, Ramp(Mathf.Sqrt(bx * bx + by * by) / (c * puffs[k, 2] * 1.6f), new[] { 0f, 0.55f, 0.6f, 0.3f, 1f, 0f }));
                                }
                                break;
                            case Cell_.Solid:
                                a = 1;
                                break;
                            case Cell_.Puff:
                            {
                                // a cartoon smoke puff: a cluster of hard-edged discs in three flat tone bands (lit cap, mid, shaded base)
                                for (int k = 0; k < 7; k++)
                                {
                                    float bx = x - puffs[k, 0] * c * 1.1f, by = y - puffs[k, 1] * c * 0.9f + c * 0.05f;
                                    float r = c * puffs[k, 2] * 1.55f;
                                    a = Mathf.Max(a, Mathf.Clamp01((r - Mathf.Sqrt(bx * bx + by * by)) / 1.5f + 0.5f));
                                }
                                shade = y > c * 0.22f ? 1f : y > -c * 0.2f ? 0.8f : 0.6f;
                                break;
                            }
                        }
                        byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(shade * 255), 0, 255);
                        px[(cy * Cell + py) * Size + cx * Cell + pxl] = new Color32(g, g, g, (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255), 0, 255));
                    }
            }
            var t2 = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true)
            {
                name = "FxAtlas", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            };
            t2.SetPixels32(px);
            t2.Apply(false, true);
            return t2;
        }
    }
}
