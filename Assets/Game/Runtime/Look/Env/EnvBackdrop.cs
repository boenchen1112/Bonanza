// Backdrop (web render/env/backdrop.js): big concentric arches standing behind the stage, half-buried in the
// ground, and a far skyline. The arches are the repeating depth cue and carry the beat: each pops one 16th
// after the one in front, so the beat reads as travelling toward the player. The skyline stops the horizon
// being an empty gradient: boxes in the fog colour, or an authored mesh (Resources/Env/<name>.bytes, grey
// vertex values tinted by the palette) when a game has one.

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
        }

        readonly Options O;
        readonly MeshBatch _arches;
        readonly Material _sky;
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
            else if (o.Skyline > 0)
            {
                _sky = EnvKit.Surface(env, Color.white, null, null, rim: 0.5f, pulse: 0.06f, bands: 2);
                var b = new MeshBuilder();
                for (int i = 0; i < o.Skyline; i++)
                {
                    float x = (i / (float)(o.Skyline - 1) - 0.5f) * o.SkylineSpread + (env.Rand() - 0.5f) * 3;
                    float h = 4 + env.Rand() * 16, w = 3 + env.Rand() * 5;
                    float z = o.SkylineZ + env.Rand() * 22;
                    b.Box(Matrix4x4.TRS(new Vector3(x, h / 2 - 2.5f, z), Quaternion.identity, new Vector3(w, h, w)), Color.white);
                }
                EnvKit.Static(env, "Skyline", t, b, _sky).receiveShadows = false;
            }
        }

        public override void Pulse(float strength, int beatIndex)
        {
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
        }
    }
}
