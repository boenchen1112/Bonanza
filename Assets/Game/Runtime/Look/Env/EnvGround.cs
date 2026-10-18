// Ground / platform (web render/env/ground.js): a raised disc with visible thickness, a glowing edge ring, an
// inner ring and confetti strewn across it. Thickness matters: a flat plane reads as a texture, a slab with an
// edge reads as a place. The edge ring is the beat instrument (the largest continuous shape on screen).
// The top takes a value map (Resources/Env: "arena" by default, "grass" for a ballpark) times the palette's
// ground colour, so the day -> night swap still owns the hue.

using UnityEngine;

namespace Bonanza.Look
{
    public sealed class EnvGround : EnvPiece
    {
        public sealed class Options
        {
            public float Radius = 13, Thickness = 0.9f, Y = 0;
            public int Confetti = 90;
            public bool Rings = true, EdgeRing = true;
            public string Map = "arena";
            /// Tiles of the value map across the disc's diameter (1 = the map spans the whole disc).
            public float MapTiles = 1;
            /// Extra stripes texture multiplied over the map, as (count across the diameter, contrast); 0 = none.
            public float Stripes = 0, StripeContrast = 0.16f;
        }

        public readonly Options O;
        public readonly MeshRenderer Top;
        readonly Material _top, _side, _edge, _inner, _bits;
        readonly Transform _edgeT, _innerT;
        float _pulse;

        public EnvGround(Env env, Options o)
        {
            O = o;
            Root = new GameObject("Ground");
            Root.transform.SetParent(env.Root, false);
            Root.transform.localPosition = new Vector3(0, o.Y, 0);
            var t = Root.transform;

            // Top: a disc, UVs planar across it. Stripes (mow bands) ride in the vertex colour, so the value map
            // stays a plain tile and the bands stay crisp at any tiling.
            _top = EnvKit.Surface(env, Color.white, o.Map, new Vector2(o.MapTiles, o.MapTiles), rim: 0.25f, pulse: 0.05f);
            var b = new MeshBuilder();
            if (o.Stripes > 0) StripedDisc(b, o.Radius, (int)o.Stripes, o.StripeContrast);
            else b.Disc(o.Radius, 72, Matrix4x4.identity, Color.white);
            Top = EnvKit.Static(env, "GroundTop", t, b, _top);

            _side = EnvKit.Surface(env, Color.white, null, null, rim: 0.7f, pulse: 0.08f, bands: 2, twoSided: true);
            b = new MeshBuilder();
            b.Shell(o.Radius, o.Radius * 0.965f, o.Thickness, 72, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(0, -o.Thickness / 2, 0)), Color.white, Vector2.one);
            EnvKit.Static(env, "GroundSide", t, b, _side).receiveShadows = false;

            if (o.EdgeRing)
            {
                _edge = EnvKit.Surface(env, Color.white, null, null, rim: 0, pulse: 0, lift: 2.6f, bands: 1);   // crosses the bloom threshold
                b = new MeshBuilder();
                b.Torus(o.Radius * 1.002f, 0.075f, 8, 128, Mathf.PI * 2, Matrix4x4.Rotate(Quaternion.Euler(90, 0, 0)), Color.white);
                var r = EnvKit.Static(env, "GroundEdge", t, b, _edge);
                r.receiveShadows = false;
                _edgeT = r.transform;
                _edgeT.localPosition = new Vector3(0, 0.02f, 0);
            }
            if (o.Rings)
            {
                _inner = EnvKit.Surface(env, Color.white, null, null, rim: 0, pulse: 0, lift: 1.4f, bands: 1);
                b = new MeshBuilder();
                b.Torus(o.Radius * 0.55f, 0.035f, 6, 96, Mathf.PI * 2, Matrix4x4.Rotate(Quaternion.Euler(90, 0, 0)), Color.white);
                var r = EnvKit.Static(env, "GroundInner", t, b, _inner);
                r.receiveShadows = false;
                _innerT = r.transform;
                _innerT.localPosition = new Vector3(0, 0.015f, 0);
            }
            if (o.Confetti > 0)
            {
                // Static scatter, one draw: the colours mix the accents per bit (the web tints them all one colour).
                _bits = EnvKit.Surface(env, Color.white, null, null, rim: 0, pulse: 0.25f, bands: 2, twoSided: true);
                b = new MeshBuilder();
                for (int i = 0; i < o.Confetti; i++)
                {
                    float a = env.Rand() * Mathf.PI * 2, rr = Mathf.Sqrt(env.Rand()) * o.Radius * 0.93f;
                    float sc = 0.7f + env.Rand() * 0.9f;
                    var m = Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * rr, 0.02f, Mathf.Sin(a) * rr), Quaternion.Euler(0, env.Rand() * 180, 0), Vector3.one * sc);
                    float k = env.Rand();
                    b.FloorQuad(0.26f, 0.16f, m, new Color(k, 1 - k, env.Rand(), 1));   // channels pick the accent mix in Update
                }
                _bitsMesh = env.Own(b.ToMesh("GroundConfetti"));
                _bitsSrc = b.C.ToArray();
                _bitsCol = new Color[_bitsSrc.Length];
                EnvKit.Renderer("GroundConfetti", t, _bitsMesh, _bits);
            }
        }

        readonly Mesh _bitsMesh;
        readonly Color[] _bitsSrc, _bitsCol;
        Color _bitsA, _bitsB, _bitsC;

        /// A disc cut into mow bands across x: alternate bands lighter (vertex colour), UVs planar.
        static void StripedDisc(MeshBuilder b, float r, int bands, float contrast)
        {
            // Fan per band: clip the disc into vertical slabs so each band has a hard edge.
            for (int k = 0; k < bands; k++)
            {
                float x0 = -r + 2 * r * k / bands, x1 = -r + 2 * r * (k + 1) / bands;
                float shade = k % 2 == 0 ? 1 : 1 - contrast;
                var c = new Color(shade, shade, shade, 1);
                // Sample the slab as a strip of quads from its bottom to its top edge.
                const int steps = 12;
                for (int s = 0; s < steps; s++)
                {
                    float xa = Mathf.Lerp(x0, x1, s / (float)steps), xb = Mathf.Lerp(x0, x1, (s + 1) / (float)steps);
                    float za = Mathf.Sqrt(Mathf.Max(0, r * r - xa * xa)), zb = Mathf.Sqrt(Mathf.Max(0, r * r - xb * xb));
                    int i0 = b.Count;
                    Add(b, xa, -za, r, c); Add(b, xa, za, r, c); Add(b, xb, zb, r, c); Add(b, xb, -zb, r, c);
                    b.Tri(i0, i0 + 1, i0 + 2); b.Tri(i0, i0 + 2, i0 + 3);
                }
            }
        }

        static void Add(MeshBuilder b, float x, float z, float r, Color c)
        {
            b.P.Add(new Vector3(x, 0, z)); b.N.Add(Vector3.up); b.C.Add(c);
            b.UV.Add(new Vector2(0.5f + x / (2 * r), 0.5f + z / (2 * r)));
        }

        public override void Pulse(float strength, int beatIndex) => _pulse = Mathf.Max(_pulse, strength);

        public override void Update(float dt, double beat, float time, Palette pal)
        {
            _pulse = EnvKit.Damp(_pulse, 0, 7, dt);
            _top.SetColor("_BaseColor", pal[PC.Ground]);
            _side.SetColor("_BaseColor", pal[PC.GroundAlt]);
            if (_edge)
            {
                _edge.SetColor("_BaseColor", EnvKit.Lin(pal[PC.GroundRim], 1 + _pulse * 1.5f));
                _edgeT.localScale = Vector3.one * (1 + _pulse * 0.012f);
            }
            if (_inner)
            {
                _inner.SetColor("_BaseColor", EnvKit.Lin(pal[PC.Accent2], 0.55f + _pulse * 0.9f));
                _innerT.localScale = Vector3.one * (1 + _pulse * 0.03f);
            }
            if (_bits)
            {
                Color a = pal[PC.Accent].linear, bb = pal[PC.Accent2].linear, c = pal[PC.Accent3].linear;
                if (a != _bitsA || bb != _bitsB || c != _bitsC)
                {
                    _bitsA = a; _bitsB = bb; _bitsC = c;
                    for (int i = 0; i < _bitsSrc.Length; i++)
                    {
                        var s = _bitsSrc[i];
                        _bitsCol[i] = s.r > 0.66f ? a : s.r > 0.33f ? bb : (s.b > 0.5f ? c : Color.Lerp(a, bb, 0.35f));
                    }
                    _bitsMesh.SetColors(_bitsCol);
                }
            }
        }
    }
}
