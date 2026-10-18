// Backdrop (web render/env/backdrop.js): big concentric arches standing behind the stage, half-buried in the
// ground, and a far skyline. The arches are the repeating depth cue and carry the beat: each pops one 16th
// after the one in front, so the beat reads as travelling toward the player. The skyline stops the horizon
// being an empty gradient: modelled towers (plinths, setbacks, cornices, masts, water tanks) with lit windows,
// or an authored mesh (Resources/Env/<name>.bytes, grey vertex values tinted by the palette) when a game has one.

using UnityEngine;

namespace Bonanza.Look
{
    public sealed class EnvBackdrop : EnvPiece
    {
        public sealed class Options
        {
            public int Arches = 5;
            public float Radius = 17, Spacing = 5.5f, Z = 8, ArchY = -1.4f, ArchTube = 0.055f;
            public int Skyline = 26;
            public float SkylineZ = 46, SkylineSpread = 120;
            /// An authored skyline (Resources/Env/<name>.bytes, see EnvBytesMesh); replaces the boxes.
            public string SkylineMesh;
            /// The skyline sits one shade above the fog: band -> fog by this much.
            public float SkylineFog = 0.45f;
            /// Self-glow of the arches (web emissive 0.35; a little more here so they reach the bloom like the web's).
            public float Glow = 0.6f;
            /// Push the arch colour toward white (linear mix): 0 = the palette's accent as is. Pale arches cross the
            /// bloom threshold and read as lit hoops rather than painted ones.
            public float ArchWhiten = 0;
            /// Extra brightness on the near arch's sunward (upper-right) shoulder, a baked vertex gradient. 0 = flat.
            public float SunSide = 0;
            /// Modelled skyline only (no SkylineMesh): additive intensity of the lit windows, and how much the fog
            /// takes the massing (1 = fully fogged like any set piece; lower keeps the silhouettes readable).
            public float WindowGlow = 1.6f, SkylineFogMul = 0.55f;
        }

        readonly Options O;
        readonly MeshBatch _arches;
        Material _sky;
        readonly float[] _r, _z, _pop, _tilt, _delay, _queue;
        readonly bool[] _queued;

        public EnvBackdrop(Env env, Options o)
        {
            O = o;
            Root = new GameObject("Backdrop");
            Root.transform.SetParent(env.Root, false);
            var t = Root.transform;

            int n = o.Arches;
            _r = new float[n]; _z = new float[n]; _pop = new float[n]; _tilt = new float[n]; _delay = new float[n]; _queue = new float[n]; _queued = new bool[n];
            if (n > 0)
            {
                var tpl = new MeshBuilder();
                // Unit ring scaled per arch: the tube scales with it (web TorusGeometry(1, 0.055) scaled by r).
                tpl.Torus(1, o.ArchTube, 8, 96, Mathf.PI * 2, Matrix4x4.identity, Color.white);
                if (o.SunSide > 0)
                    for (int v = 0; v < tpl.Count; v++)
                    {
                        // Brightest where the ring faces the low sun (up and to the right), fading round the arc.
                        var d = new Vector2(tpl.P[v].x, tpl.P[v].y).normalized;
                        float k = Mathf.Clamp01(Vector2.Dot(d, new Vector2(0.55f, 0.83f)));
                        tpl.C[v] = Color.white * (1 + o.SunSide * k * k);
                    }
                var mat = EnvKit.Surface(env, Color.white, null, null, rim: 1.4f, pulse: 0.5f, lift: o.Glow, bands: 2);
                _arches = new MeshBatch("Arches", t, tpl, n, mat);
                for (int i = 0; i < n; i++)
                {
                    _r[i] = o.Radius + i * 2.6f;
                    _z[i] = o.Z + i * o.Spacing;
                    _tilt[i] = (env.Rand() - 0.5f) * 0.05f;
                }
            }

            if (!string.IsNullOrEmpty(o.SkylineMesh))
            {
                var mesh = EnvBytesMesh.Load(o.SkylineMesh);
                if (mesh)
                {
                    env.Own(mesh);
                    _sky = EnvKit.Surface(env, Color.white, null, null, rim: 0.9f, pulse: 0.04f, bands: 2, fog: 0);
                    EnvKit.Renderer("Skyline", t, mesh, _sky).receiveShadows = false;
                }
            }
            else if (o.Skyline > 0) BuildSkyline(env, o, t);
        }

        // ------------------------------------------------------------ the modelled skyline (no authored mesh)

        Material _windows;
        float _winPop;

        /// A city of modelled towers instead of raw boxes: each building is a shaft with a plinth, often a setback
        /// tier and a roof cap, a few carry an antenna mast or a water tank; the camera-facing facades carry a grid of
        /// lit windows (one additive draw, warm and cool, about half on) and the masts a red tip. Two draws in all:
        /// the massing in the palette band colour, the lights as glow. Built once; the windows breathe on the beat.
        void BuildSkyline(Env env, Options o, Transform t)
        {
            _sky = EnvKit.Surface(env, Color.white, null, null, rim: 0.7f, pulse: 0.06f, bands: 2, fog: o.SkylineFogMul);
            var b = new MeshBuilder();
            var lit = new MeshBuilder();
            var white = Color.white;
            Color warm = new Color(1f, 0.78f, 0.45f), cool = new Color(0.55f, 0.78f, 1f), red = new Color(1f, 0.16f, 0.1f);
            for (int i = 0; i < o.Skyline; i++)
            {
                float x = (i / (float)(o.Skyline - 1) - 0.5f) * o.SkylineSpread + (env.Rand() - 0.5f) * 3;
                float h = 4 + env.Rand() * 16, w = 3 + env.Rand() * 5, d = w * (0.7f + env.Rand() * 0.4f);
                float z = o.SkylineZ + env.Rand() * 22;
                const float y0 = -2.5f;
                // Facade shade: a few tones of the band colour so neighbouring towers separate without outlines.
                float tone = 0.78f + env.Rand() * 0.34f;
                var shade = new Color(tone, tone, tone * (1.0f + (env.Rand() - 0.5f) * 0.12f), 1);
                b.Box(Matrix4x4.TRS(new Vector3(x, y0 + h / 2, z), Quaternion.identity, new Vector3(w, h, d)), shade);
                // plinth band and a cornice: the floors read as a building, not a crate
                b.Box(Matrix4x4.TRS(new Vector3(x, y0 + 0.9f, z - 0.08f), Quaternion.identity, new Vector3(w + 0.3f, 1.8f, d + 0.2f)), shade * 0.82f);
                float top = y0 + h;
                b.Box(Matrix4x4.TRS(new Vector3(x, top + 0.18f, z), Quaternion.identity, new Vector3(w + 0.35f, 0.36f, d + 0.35f)), shade * 1.15f);
                float roofW = w, roofD = d;
                float r = env.Rand();
                if (h > 9 && r < 0.55f)
                {
                    // setback tier
                    float h2 = h * (0.18f + env.Rand() * 0.22f);
                    roofW = w * (0.55f + env.Rand() * 0.2f); roofD = d * 0.7f;
                    b.Box(Matrix4x4.TRS(new Vector3(x, top + 0.36f + h2 / 2, z + 0.2f), Quaternion.identity, new Vector3(roofW, h2, roofD)), shade * 0.95f);
                    b.Box(Matrix4x4.TRS(new Vector3(x, top + 0.36f + h2 + 0.14f, z + 0.2f), Quaternion.identity, new Vector3(roofW + 0.25f, 0.28f, roofD + 0.25f)), shade * 1.15f);
                    Windows(lit, env, x, top + 0.36f + 0.5f, top + 0.36f + h2 - 0.4f, roofW, z + 0.2f - roofD / 2 - 0.02f, warm, cool);
                    top += 0.36f + h2 + 0.28f;
                }
                else top += 0.36f;
                float r2 = env.Rand();
                if (h > 12 && r2 < 0.5f)
                {
                    // antenna mast with a red tip
                    float mh = 2.5f + env.Rand() * 3.5f;
                    b.Box(Matrix4x4.TRS(new Vector3(x, top + mh / 2, z + 0.2f), Quaternion.identity, new Vector3(0.14f, mh, 0.14f)), shade);
                    b.Box(Matrix4x4.TRS(new Vector3(x, top + 0.4f, z + 0.2f), Quaternion.identity, new Vector3(0.6f, 0.8f, 0.6f)), shade * 0.9f);
                    lit.Sphere(0.22f, 6, 4, Matrix4x4.Translate(new Vector3(x, top + mh + 0.1f, z + 0.2f)), red * 2.2f, Vector3.one);
                }
                else if (r2 < 0.8f && roofW > 2.4f)
                {
                    // water tank on legs
                    float tx = x + (env.Rand() - 0.5f) * roofW * 0.4f;
                    b.Box(Matrix4x4.TRS(new Vector3(tx, top + 0.5f, z + 0.3f), Quaternion.identity, new Vector3(0.9f, 1.0f, 0.9f)), shade * 0.7f);
                    b.Shell(0.62f, 0.62f, 1.1f, 10, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(tx, top + 1.55f, z + 0.3f)), shade * 1.05f, Vector2.one);
                    b.Cone(0.75f, 0.55f, 10, Matrix4x4.Translate(new Vector3(tx, top + 2.65f, z + 0.3f)), shade * 1.2f);
                }
                Windows(lit, env, x, y0 + 2.4f, y0 + h - 0.5f, w, z - d / 2 - 0.02f, warm, cool);
            }
            EnvKit.Static(env, "Skyline", t, b, _sky).receiveShadows = false;
            if (lit.Count > 0)
            {
                _windows = env.Mat(EnvKit.GlowAddMat);
                _windows.SetColor("_Color", Color.white);
                _windows.SetFloat("_Intensity", o.WindowGlow);
                _windows.SetFloat("_FogMul", 0.35f);
                var r = EnvKit.Static(env, "SkylineWindows", t, lit, _windows);
                r.receiveShadows = false;
            }
        }

        /// A grid of window panes on the facade plane z (facing the camera), between y0 and y1, across width w.
        static void Windows(MeshBuilder lit, Env env, float cx, float y0, float y1, float w, float z, Color warm, Color cool)
        {
            if (y1 - y0 < 1) return;
            int cols = Mathf.Max(2, Mathf.RoundToInt(w / 0.95f));
            int rows = Mathf.Max(1, Mathf.FloorToInt((y1 - y0) / 1.15f));
            float cw = (w - 0.5f) / cols, rh = (y1 - y0) / rows;
            float pw = cw * 0.55f, ph = rh * 0.5f;
            bool coolBlock = env.Rand() < 0.35f;
            float density = 0.3f + env.Rand() * 0.4f;
            for (int j = 0; j < rows; j++)
                for (int i = 0; i < cols; i++)
                {
                    if (env.Rand() > density) continue;
                    float k = 0.45f + env.Rand() * 0.75f;
                    var c = (coolBlock ^ (env.Rand() < 0.15f) ? cool : warm) * k;
                    float x = cx - (w - 0.5f) / 2 + (i + 0.5f) * cw, y = y0 + (j + 0.5f) * rh;
                    int a = lit.Count;
                    lit.P.Add(new Vector3(x - pw / 2, y - ph / 2, z)); lit.P.Add(new Vector3(x - pw / 2, y + ph / 2, z));
                    lit.P.Add(new Vector3(x + pw / 2, y + ph / 2, z)); lit.P.Add(new Vector3(x + pw / 2, y - ph / 2, z));
                    for (int q = 0; q < 4; q++) { lit.N.Add(Vector3.back); lit.C.Add(c); }
                    lit.UV.Add(new Vector2(0, 0)); lit.UV.Add(new Vector2(0, 1)); lit.UV.Add(new Vector2(1, 1)); lit.UV.Add(new Vector2(1, 0));
                    lit.T.Add(a); lit.T.Add(a + 1); lit.T.Add(a + 2); lit.T.Add(a); lit.T.Add(a + 2); lit.T.Add(a + 3);
                }
        }

        public override void Pulse(float strength, int beatIndex)
        {
            _winPop = Mathf.Max(_winPop, strength * (beatIndex == 0 ? 1 : 0.5f));
            int n = O.Arches;
            for (int i = 0; i < n; i++)
            {
                // Stagger so the pop travels forward through the set.
                _queue[i] = strength;
                _queued[i] = true;
                _delay[i] = -(((i + beatIndex) % n) * 0.045f);
            }
        }

        public override void Update(float dt, double beat, float time, Palette pal)
        {
            int n = O.Arches;
            if (_arches != null)
            {
                bool calm = Settings.ReduceMotion;
                for (int i = 0; i < n; i++)
                {
                    if (_queued[i])
                    {
                        _delay[i] += dt;
                        if (_delay[i] >= 0) { _pop[i] = Mathf.Max(_pop[i], _queue[i]); _queued[i] = false; }
                    }
                    _pop[i] = EnvKit.Damp(_pop[i], 0, 8, dt);
                    float bob = calm ? 0 : Mathf.Sin(time * 0.5f + i * 0.9f) * 0.12f;
                    float sc = _r[i] * (1 + (calm ? 0 : _pop[i] * 0.02f));
                    var rot = Quaternion.Euler(0, 0, Mathf.Rad2Deg * (_tilt[i] + (calm ? 0 : Mathf.Sin(time * 0.24f + i) * 0.012f)));
                    _arches.Matrix[i] = Matrix4x4.TRS(new Vector3(0, O.ArchY + bob, _z[i]), rot, new Vector3(sc, sc, sc));
                    float k = Mathf.Clamp01(i / (float)Mathf.Max(1, n - 1));
                    var col = Color.Lerp(pal[PC.Accent].linear, pal[PC.Band].linear, 0.35f + k * 0.5f);
                    if (O.ArchWhiten > 0) col = Color.Lerp(col, Color.white * col.maxColorComponent, O.ArchWhiten);
                    col *= 0.75f + _pop[i] * 1.4f + EnvKit.EaseOutCubic(1 - k) * 0.35f;
                    col.a = 1;
                    _arches.Tint[i] = col;
                }
                _arches.Flush();
            }
            if (_sky) _sky.SetColor("_BaseColor", EnvKit.Mix(pal[PC.Band], pal[PC.Fog], O.SkylineFog));
            if (_windows)
            {
                _winPop = EnvKit.Damp(_winPop, 0, 5, dt);
                _windows.SetFloat("_Intensity", O.WindowGlow * (1 + _winPop * (Settings.ReduceMotion ? 0.05f : 0.18f)));
            }
        }
    }
}
