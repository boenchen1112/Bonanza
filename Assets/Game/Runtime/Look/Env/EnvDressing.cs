// The overhead dressing of a set: bunting (web render/env/banners.js), show spotlights (spotlights.js) and
// floating shapes (floaters.js). Each is one draw (the cones one each), all beat-reactive.

using UnityEngine;

namespace Bonanza.Look
{
    /// A garland of triangular flags strung in an arc above the arena, waving out of phase. Framing first:
    /// it closes the top of the frame and gives a camera push something to parallax against.
    public sealed class EnvBanners : EnvPiece
    {
        public sealed class Options
        {
            public int Count = 26;
            public float Radius = 12.5f, Y = 7.2f, Sag = 1.6f, Size = 1.05f, Z = 2, Span = Mathf.PI * 1.3f;
            public string Map = "cloth";
            public float Glow = 0.8f;
            /// Push the flag colours toward white (linear mix): backlit cloth against a low sun reads pale and lit.
            public float Whiten = 0;
            /// Line radius (thickness) of the cable the flags hang from.
            public float LineRadius = 0.03f;
            /// Random per-flag size spread (0 = all the same).
            public float SizeJitter = 0;
        }

        readonly Options O;
        readonly MeshBatch _flags;
        readonly Material _line;
        readonly Vector3[] _pos;
        readonly float[] _phase, _speed, _face, _pop, _size;
        readonly int[] _hue;

        public EnvBanners(Env env, Options o)
        {
            O = o;
            Root = new GameObject("Banners");
            Root.transform.SetParent(env.Root, false);
            var t = Root.transform;
            int n = o.Count;
            _pos = new Vector3[n]; _phase = new float[n]; _speed = new float[n]; _face = new float[n]; _pop = new float[n]; _hue = new int[n]; _size = new float[n];
            var line = new Vector3[n * 3 + 1];
            for (int i = 0; i < n; i++)
            {
                float k = n > 1 ? i / (float)(n - 1) : 0.5f;
                float a = -o.Span / 2 + o.Span * k;   // 0 = straight behind the stage
                _pos[i] = new Vector3(Mathf.Sin(a) * o.Radius, o.Y - Mathf.Sin(k * Mathf.PI) * o.Sag, o.Z + Mathf.Cos(a) * o.Radius);
                _phase[i] = env.Rand() * Mathf.PI * 2;
                _speed[i] = 1.4f + env.Rand() * 0.8f;
                _face[i] = a;
                _hue[i] = i % 3;
                _size[i] = 1 + (env.Rand() - 0.5f) * 2 * o.SizeJitter;
            }
            // The line: a thin tube through the flag tops (the web's flat torus did not follow the sag).
            for (int i = 0; i < line.Length; i++)
            {
                float k = i / (float)(line.Length - 1);
                float a = -o.Span / 2 + o.Span * k;
                line[i] = new Vector3(Mathf.Sin(a) * o.Radius, o.Y - Mathf.Sin(k * Mathf.PI) * o.Sag + 0.02f, o.Z + Mathf.Cos(a) * o.Radius);
            }
            _line = EnvKit.Surface(env, Color.white, null, null, rim: 0, pulse: 0, bands: 1);
            var b = new MeshBuilder();
            b.Tube(line, o.LineRadius, 5, Matrix4x4.identity, Color.white);
            EnvKit.Static(env, "BuntingLine", t, b, _line).receiveShadows = false;

            var tpl = new MeshBuilder();
            tpl.Flag(0.45f * o.Size, 0.85f * o.Size, Matrix4x4.identity, Color.white);
            // A little self-glow: backlit cloth against the sky reads as lit, and a beat pop crosses the bloom.
            var mat = EnvKit.Surface(env, Color.white, o.Map, new Vector2(1, 1), rim: 1.1f, pulse: 0.3f, lift: o.Glow, bands: 2, twoSided: true, mapStrength: 0.6f);
            _flags = new MeshBatch("Bunting", t, tpl, n, mat);
            _flags.Renderer.receiveShadows = false;
        }

        public override void Pulse(float strength, int beatIndex)
        {
            for (int i = 0; i < _pop.Length; i++) _pop[i] = Mathf.Max(_pop[i], strength * (0.4f + 0.6f * (i % 2)));
        }

        public override void Update(float dt, double beat, float time, Palette pal)
        {
            float calm = Settings.ReduceMotion ? 0.35f : 1;
            Color a = pal[PC.Accent].linear, b2 = pal[PC.Accent2].linear, c3 = pal[PC.Accent3].linear;
            for (int i = 0; i < _pos.Length; i++)
            {
                _pop[i] = EnvKit.Damp(_pop[i], 0, 7, dt);
                float swing = (Mathf.Sin(time * _speed[i] + _phase[i]) * 0.28f + _pop[i] * 0.35f) * calm;
                // Face the stage (local -z toward the centre), swing about the line (x) and twist a little (z).
                var q = Quaternion.Euler(0, _face[i] * Mathf.Rad2Deg, 0) * Quaternion.Euler(swing * 0.5f * Mathf.Rad2Deg, 0, swing * Mathf.Rad2Deg);
                float sc = (1 + _pop[i] * 0.25f * calm) * _size[i];
                _flags.Matrix[i] = Matrix4x4.TRS(_pos[i], q, new Vector3(sc, sc, sc));
                var hue = _hue[i] == 0 ? a : _hue[i] == 1 ? b2 : c3;
                if (O.Whiten > 0) hue = Color.Lerp(hue, Color.white, O.Whiten);
                var col = hue * (0.8f + _pop[i] * 0.8f);
                col.a = 1;
                _flags.Tint[i] = col;
            }
            _flags.Flush();
            _line.SetColor("_BaseColor", EnvKit.Lin(pal[PC.Band], 0.5f));
        }
    }

    /// Fake volumetric cones that sweep slowly across the stage and flare on the beat (half the rig per beat,
    /// alternating: a full flash every beat stops reading as an accent within four bars). No real lights.
    public sealed class EnvSpotlights : EnvPiece
    {
        public sealed class Options
        {
            public int Count = 4;
            public float Height = 13, Spread = 9, Radius = 2.6f, Z = 6, Base = 0.16f, Flare = 0.30f;
            /// Optional lamp positions (env-local): each cone hangs from its anchor and aims at the matching Target,
            /// sweeping a little around it, instead of hanging in the sky in a row. Height is then the cone length.
            public Vector3[] Anchors, Targets;
            /// Sweep amplitude, radians (anchored cones).
            public float Sweep = 0.08f;
            /// One colour for every cone (floodlights) instead of the palette accents.
            public Color? Tint;
        }

        readonly Options O;
        readonly Transform[] _cone;
        readonly Material[] _mat;
        readonly float[] _swing, _phase, _tilt, _pop;
        static readonly int IdColor = Shader.PropertyToID("_Color"), IdIntensity = Shader.PropertyToID("_Intensity");

        public EnvSpotlights(Env env, Options o)
        {
            O = o;
            Root = new GameObject("Spotlights");
            Root.transform.SetParent(env.Root, false);
            int n = o.Count;
            _cone = new Transform[n]; _mat = new Material[n];
            _swing = new float[n]; _phase = new float[n]; _tilt = new float[n]; _pop = new float[n];
            var b = new MeshBuilder();
            b.Cone(o.Radius, o.Height, 22, Matrix4x4.identity, Color.white);
            // (anchored cones: Height is the throw from the lamp to the floor)
            var mesh = env.Own(b.ToMesh("Beam"));
            for (int i = 0; i < n; i++)
            {
                _mat[i] = env.Mat(EnvKit.BeamMat);
                var r = EnvKit.Renderer("Beam" + i, Root.transform, mesh, _mat[i]);
                r.receiveShadows = false;
                float k = n == 1 ? 0.5f : i / (float)(n - 1);
                _cone[i] = r.transform;
                _cone[i].localPosition = o.Anchors != null ? o.Anchors[i % o.Anchors.Length] : new Vector3((k - 0.5f) * o.Spread * 2, o.Height, o.Z);
                _swing[i] = 0.5f + env.Rand() * 0.5f;
                _phase[i] = env.Rand() * Mathf.PI * 2;
                _tilt[i] = (k - 0.5f) * 0.5f;
            }
        }

        public override void Pulse(float strength, int beatIndex)
        {
            for (int i = 0; i < _pop.Length; i++)
                if ((i + beatIndex) % 2 == 0) _pop[i] = Mathf.Max(_pop[i], strength);
        }

        float _chaseT = -1, _chaseStep, _chaseAmt;

        /// A light sweep: every cone flares in turn, left to right, `step` seconds apart (a big moment).
        public void Chase(float strength, float step = 0.07f) { _chaseT = 0; _chaseStep = step; _chaseAmt = strength; }

        /// Retune the rig at runtime (a game's night switch): resting intensity and the beat flare.
        public void SetLevel(float baseIntensity, float flare) { O.Base = baseIntensity; O.Flare = flare; }

        public override void Update(float dt, double beat, float time, Palette pal)
        {
            bool calm = Settings.ReduceMotion;
            float chase0 = _chaseT;
            if (_chaseT >= 0) { _chaseT += dt; if (_chaseT > _chaseStep * (_cone.Length + 1)) _chaseT = -1; }
            for (int i = 0; i < _cone.Length; i++)
            {
                _pop[i] = EnvKit.Damp(_pop[i], 0, 6, dt);
                float at = i * _chaseStep;
                bool crossed = chase0 >= 0 && chase0 <= at && (_chaseT > at || _chaseT < 0);   // the sweep passed this cone this frame
                if (crossed) _pop[i] = Mathf.Max(_pop[i], _chaseAmt);
                float sw = calm ? 0.35f : 1;
                if (O.Anchors != null)
                {
                    var dir = (O.Targets[i % O.Targets.Length] - _cone[i].localPosition).normalized;
                    float s1 = Mathf.Sin(time * 0.31f * _swing[i] * sw + _phase[i]) * O.Sweep * sw;
                    float s2 = Mathf.Sin(time * 0.19f * _swing[i] * sw + _phase[i] * 1.3f) * O.Sweep * 0.6f * sw;
                    _cone[i].localRotation = Quaternion.Euler(s2 * Mathf.Rad2Deg, s1 * Mathf.Rad2Deg, 0) * Quaternion.FromToRotation(Vector3.down, dir);
                }
                else
                {
                float rz = Mathf.Sin(time * 0.35f * _swing[i] * sw + _phase[i]) * 0.26f + _tilt[i];
                float rx = -(Mathf.Sin(time * 0.22f * _swing[i] * sw + _phase[i] * 0.7f) * 0.14f + 0.1f);   // web +x tilt, z mirrored
                _cone[i].localRotation = Quaternion.Euler(rx * Mathf.Rad2Deg, 0, rz * Mathf.Rad2Deg);
                }
                int hue = i % 3;
                var c = hue == 0 ? pal[PC.Accent] : hue == 1 ? pal[PC.Rim] : pal[PC.Accent2];
                _mat[i].SetColor(IdColor, O.Tint ?? EnvKit.Mix(c, Color.white, 0.35f));
                _mat[i].SetFloat(IdIntensity, O.Base + _pop[i] * O.Flare * (calm ? 0.3f : 1));
            }
        }
    }

    /// Chunky low-poly solids orbiting well outside the play space: parallax, colour in the empty upper third,
    /// and rhythm in peripheral vision (they punch on the beat, alternating halves of the ring).
    public sealed class EnvFloaters : EnvPiece
    {
        public sealed class Options
        {
            public int Count = 22;
            public float InnerR = 16, OuterR = 30, MinY = -2, MaxY = 15, Size = 1, Z = 4;
        }

        readonly Options O;
        readonly MeshBatch _batch;
        readonly float[] _a, _r, _y, _s, _spin, _orbit, _bob, _phase, _hue, _pop;

        public EnvFloaters(Env env, Options o)
        {
            O = o;
            Root = new GameObject("Floaters");
            Root.transform.SetParent(env.Root, false);
            int n = o.Count;
            _a = new float[n]; _r = new float[n]; _y = new float[n]; _s = new float[n]; _spin = new float[n];
            _orbit = new float[n]; _bob = new float[n]; _phase = new float[n]; _hue = new float[n]; _pop = new float[n];
            for (int i = 0; i < n; i++)
            {
                _a[i] = env.Rand() * Mathf.PI * 2;
                _r[i] = o.InnerR + env.Rand() * (o.OuterR - o.InnerR);
                _y[i] = o.MinY + env.Rand() * (o.MaxY - o.MinY);
                _s[i] = o.Size * (0.5f + env.Rand() * 1.5f);
                _spin[i] = (env.Rand() - 0.5f) * 0.9f;
                _orbit[i] = (env.Rand() - 0.5f) * 0.05f;
                _bob[i] = 0.4f + env.Rand() * 0.9f;
                _phase[i] = env.Rand() * Mathf.PI * 2;
                _hue[i] = env.Rand();
            }
            var tpl = new MeshBuilder();
            tpl.Octahedron(Matrix4x4.identity, Color.white);
            var mat = EnvKit.Surface(env, Color.white, null, null, rim: 1.5f, pulse: 0.35f, bands: 3);
            _batch = new MeshBatch("Floaters", Root.transform, tpl, n, mat);
            _batch.Renderer.receiveShadows = false;
        }

        public override void Pulse(float strength, int beatIndex)
        {
            for (int i = 0; i < _pop.Length; i++) _pop[i] = Mathf.Max(_pop[i], strength * (0.5f + 0.5f * Mathf.Abs(Mathf.Sin(_a[i]))));
        }

        public override void Update(float dt, double beat, float time, Palette pal)
        {
            float calm = Settings.ReduceMotion ? 0.3f : 1;
            Color a = pal[PC.Accent].linear, b = pal[PC.Accent2].linear, c = pal[PC.Accent3].linear;
            for (int i = 0; i < _a.Length; i++)
            {
                _a[i] += _orbit[i] * dt * calm;
                _pop[i] = EnvKit.Damp(_pop[i], 0, 6, dt);
                float y = _y[i] + Mathf.Sin(time * _bob[i] * calm + _phase[i]) * 0.7f;
                var p = new Vector3(Mathf.Cos(_a[i]) * _r[i], y, -Mathf.Sin(_a[i]) * _r[i] + O.Z);
                float ts = time * calm;
                var q = Quaternion.Euler(ts * _spin[i] * Mathf.Rad2Deg, (ts * _spin[i] * 0.7f + _phase[i]) * Mathf.Rad2Deg, _phase[i] * Mathf.Rad2Deg);
                float sc = _s[i] * (1 + _pop[i] * 0.22f * calm);
                _batch.Matrix[i] = Matrix4x4.TRS(p, q, new Vector3(sc, sc, sc));
                var col = (_hue[i] < 0.4f ? a : _hue[i] < 0.75f ? b : c) * (0.62f + _pop[i] * 0.75f);
                col.a = 1;
                _batch.Tint[i] = col;
            }
            _batch.Flush();
        }
    }
}
