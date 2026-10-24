// The living 3D backdrop behind every shell screen (title, free play, roster, party hub, options): a plum concert
// hall at showtime. A planked stage with a lit lip and footlights, red velvet drapes under a swagged valance with a
// chasing marquee, a gilt sunburst on the back wall, crossing spot cones, strings of party lights, drifting
// glitter and the dark heads of the house in front, some waving glow sticks.
//
// Built from the Look kit (LookRig, Env, MeshBuilder, MeshBatch): no physics, no Shader.Find, materials from
// Resources/Look, roughly 40k triangles. The camera moves between named poses (SetPose); the idle drift and the
// beat push come from the kit's CameraRig and are off under reduced motion.
//
// One LookRig may own the camera at a time, so the host Suspends this before a game binds its own rig and
// Resumes it once the game is gone.

using System.Collections.Generic;
using Bonanza.HudKit;
using Bonanza.Look;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Bonanza.Shell
{
    public sealed class ShellBackdrop : MonoBehaviour
    {
        public const string PaletteName = "shell-hall";

        public struct Pose
        {
            public Vector3 Target; public float Dist, Height, Yaw, Fov;
            public Pose(float tx, float ty, float tz, float dist, float height, float yaw, float fov)
            { Target = new Vector3(tx, ty, tz); Dist = dist; Height = height; Yaw = yaw; Fov = fov; }
        }

        /// The camera poses of the screens (target, distance, height above the target, yaw, fov).
        static readonly Dictionary<string, Pose> Poses = new Dictionary<string, Pose>
        {
            { "title",    new Pose(0, 3.7f, 9, 20, -0.5f, 0, 48) },
            { "attract",  new Pose(0, 3.6f, 9, 18f, -0.3f, 0.1f, 47) },
            { "freeplay", new Pose(2.2f, 3.5f, 9, 17, -0.3f, -0.26f, 50) },
            { "roster",   new Pose(0, 2.6f, 9, 14, 0.5f, 0.18f, 46) },
            { "hub",      new Pose(0, 3.2f, 9, 22, 1.8f, -0.1f, 52) },
            { "options",  new Pose(-3, 4, 9, 18, -0.6f, 0.34f, 48) },
        };

        public static ShellBackdrop Create(Transform parent)
        {
            var go = new GameObject("ShellBackdrop");
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<ShellBackdrop>();
            b.Build();
            return b;
        }

        /// True while the hall is on and owns the camera.
        public bool Live { get; private set; }
        public string PoseName { get; private set; } = "title";
        public LookRig Look => _look;
        CastStage _cast;
        /// The cast on the stage (roster); built on first use.
        public CastStage Cast => _cast ? _cast : _cast = CastStage.Create(this);
        public bool HasCast => _cast != null;

        LookRig _look;
        Env _env;
        Camera _cam;
        CameraClearFlags _flags; Color _bg;
        readonly List<Object> _owned = new List<Object>();
        float _time, _poseAge;
        int _frame;
        bool _quiet;   // Low tier: the small stuff updates at a third of the rate

        // moving parts
        Transform _burst;
        AntialiasingMode _aa; bool _aaSet;
        MeshBatch _bulbs, _heads, _bodies, _sticks, _flecks;
        Vector3[] _bulbAt; float[] _bulbPhase, _bulbBase; Color[] _bulbColor; int[] _bulbKind;   // kind 0 foot, 1 marquee, 2 string
        int _footN, _marqN;
        Vector3[] _headAt; float[] _headPhase; Color[] _headTint;
        Vector3[] _stickAt; float[] _stickPhase; Color[] _stickTint;
        Vector3[] _fleckAt; float[] _fleckPhase, _fleckSpeed; Color[] _fleckTint;
        Material _poolMat, _haloMat, _ringMat;
        Transform _ring;

        // ---------------------------------------------------------------- build

        static Color L(uint hex) => UiKit.Hex(hex).linear;
        static Color L(float r, float g, float b) => new Color(r, g, b).linear;

        T Own<T>(T o) where T : Object { _owned.Add(o); return o; }

        Material Surf(Color c, string map = null, Vector2? tiling = null, float rim = 1, float pulse = 0.05f, float lift = 0,
            float bands = 3, float fog = 1, float softness = 0.3f, bool twoSided = false, float mapStrength = 1)
        {
            var m = Own(new Material(EnvKit.ToonEnv));
            m.SetColor("_BaseColor", c);
            if (map != null)
            {
                var t = EnvKit.Tex(map);
                if (t) { m.SetTexture("_BaseMap", t); m.SetTextureScale("_BaseMap", tiling ?? Vector2.one); }
            }
            m.SetFloat("_MapStrength", map != null ? mapStrength : 0);
            m.SetFloat("_Rim", rim); m.SetFloat("_Pulse", pulse); m.SetFloat("_Lift", lift); m.SetFloat("_Bands", bands);
            m.SetFloat("_Softness", softness);
            m.SetFloat("_Cull", twoSided ? (float)UnityEngine.Rendering.CullMode.Off : (float)UnityEngine.Rendering.CullMode.Back);
            m.SetFloat("_FogMul", fog);
            return m;
        }

        internal Material AddMat(Color c, float intensity, float facing = 0, float fog = 0, float ramp = 0)
        {
            var m = Own(new Material(EnvKit.GlowAddMat));
            m.SetColor("_Color", c);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Facing", facing);
            m.SetFloat("_Ramp", ramp);
            m.SetFloat("_FogMul", fog);
            return m;
        }

        internal Material Card(Color c, float intensity, float core, bool billboard)
        {
            var m = Own(new Material(EnvKit.GlowMat));
            m.SetColor("_Color", c);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Billboard", billboard ? 1 : 0);
            m.SetFloat("_Core", core);
            m.SetVector("_Stretch", new Vector4(1, 1, 0, 0));
            return m;
        }

        MeshRenderer Part(string name, Transform parent, MeshBuilder b, Material m)
        {
            var r = EnvKit.Renderer(name, parent, Own(b.ToMesh(name)), m);
            r.receiveShadows = false;
            return r;
        }

        Mesh _quad;
        Mesh Quad()
        {
            if (_quad) return _quad;
            var q = new MeshBuilder();
            q.Surface(1, 1, (u, v) => (new Vector3(u - 0.5f, v - 0.5f, 0), Vector3.back), Matrix4x4.identity, Color.white, Vector2.one);
            return _quad = Own(q.ToMesh("Quad"));
        }

        internal MeshRenderer CardAt(string name, Material m, Vector3 pos, Vector3 euler, Vector2 size, Transform parent = null)
        {
            var r = EnvKit.Renderer(name, parent ? parent : transform, Quad(), m);
            r.receiveShadows = false;
            r.transform.localPosition = pos;
            r.transform.localRotation = Quaternion.Euler(euler);
            r.transform.localScale = new Vector3(size.x, size.y, 1);
            return r;
        }

        void Build()
        {
            _cam = Camera.main;
            if (_cam) { _flags = _cam.clearFlags; _bg = _cam.backgroundColor; }
            _look = LookRig.Create(transform, PaletteName);
            if (_cam) { _cam.nearClipPlane = 0.3f; _cam.farClipPlane = 160f; }
            _look.Rig?.SetPushGain(0.6f);
            _look.Rig?.SetDriftGain(7f);

            _env = _look.CreateEnv(EnvPreset.Stage, o =>
            {
                o.SkipGround = true;
                o.Backdrop.Arches = 0; o.Backdrop.Skyline = 0;
                o.Floaters.Count = 0;
                // Cross spots from the front of the house onto the stage, soft so the logo stays readable.
                o.Spotlights.Count = 3;
                o.Spotlights.Anchors = new[] { new Vector3(-11, 17, -4), new Vector3(11, 17, -4), new Vector3(0, 19, -2) };
                o.Spotlights.Targets = new[] { new Vector3(-2.5f, 0, 9), new Vector3(2.5f, 0, 9), new Vector3(0, 0, 10) };
                o.Spotlights.Height = 25f; o.Spotlights.Radius = 3.0f; o.Spotlights.Sweep = 0.05f;
                o.Spotlights.Base = 0.07f; o.Spotlights.Flare = 0.07f; o.Spotlights.Tint = new Color(1f, 0.86f, 0.62f);
            });

            BuildRoom();
            BuildStage();
            BuildDrapes();
            BuildBurst();
            BuildLights();
            BuildHouse();
            BuildGlitter();
            FlushAll(0);
            gameObject.SetActive(false);   // off until the host resumes it
        }

        /// The walls, floors and ceiling of the hall (everything the fog melts into).
        void BuildRoom()
        {
            var wall = Surf(L(0.2f, 0.06f, 0.2f), rim: 0.5f, pulse: 0.02f, bands: 2, fog: 1, softness: 0.4f);
            var b = new MeshBuilder();
            b.Box(Matrix4x4.TRS(new Vector3(0, 8, 21), Quaternion.identity, new Vector3(70, 26, 1)), Color.white);
            b.Box(Matrix4x4.TRS(new Vector3(-30, 8, 4), Quaternion.identity, new Vector3(1, 26, 56)), Color.white);
            b.Box(Matrix4x4.TRS(new Vector3(30, 8, 4), Quaternion.identity, new Vector3(1, 26, 56)), Color.white);
            b.Box(Matrix4x4.TRS(new Vector3(0, 21, 4), Quaternion.identity, new Vector3(70, 1, 56)), Color.white);
            Part("Walls", transform, b, wall);

            // the house floor: dark carpet, lighter in the aisle under the stage lights
            var carpet = Surf(L(0.16f, 0.04f, 0.14f), rim: 0.3f, pulse: 0, bands: 2, softness: 0.5f);
            b = new MeshBuilder();
            b.FloorQuad(64, 56, Matrix4x4.Translate(new Vector3(0, -1.9f, -6)), Color.white);
            Part("House", transform, b, carpet);

            // arched window panes glowing violet on the back wall, framed in gilt
            var pane = AddMat(L(0.3f, 0.12f, 0.6f), 0.55f, 0, 0.5f);
            var gilt = Surf(L(0.85f, 0.6f, 0.22f), rim: 1.2f, pulse: 0.03f, lift: 0.05f, bands: 3, fog: 0.5f);
            var pb = new MeshBuilder(); var gb = new MeshBuilder();
            foreach (float x in new[] { -22f, -15.5f, 15.5f, 22f })
            {
                pb.Box(Matrix4x4.TRS(new Vector3(x, 8.5f, 20.35f), Quaternion.identity, new Vector3(3.2f, 11, 0.1f)), Color.white);
                pb.Sphere(1.6f, 14, 4, Matrix4x4.TRS(new Vector3(x, 14f, 20.35f), Quaternion.identity, new Vector3(1, 1, 0.05f)), Color.white, Vector3.one);
                gb.Box(Matrix4x4.TRS(new Vector3(x - 1.75f, 8.5f, 20.25f), Quaternion.identity, new Vector3(0.3f, 11.4f, 0.2f)), Color.white);
                gb.Box(Matrix4x4.TRS(new Vector3(x + 1.75f, 8.5f, 20.25f), Quaternion.identity, new Vector3(0.3f, 11.4f, 0.2f)), Color.white);
                gb.Box(Matrix4x4.TRS(new Vector3(x, 2.9f, 20.25f), Quaternion.identity, new Vector3(3.8f, 0.3f, 0.2f)), Color.white);
                gb.Torus(1.75f, 0.15f, 5, 24, Mathf.PI, Matrix4x4.TRS(new Vector3(x, 14f, 20.25f), Quaternion.identity, Vector3.one), Color.white);
            }
            Part("Panes", transform, pb, pane);
            Part("PaneFrames", transform, gb, gilt);
        }

        /// The stage deck, its gilt lip, the spot pool and the gilt ring of the stage mark.
        void BuildStage()
        {
            var deck = Surf(L(0.66f, 0.36f, 0.17f), "stage", new Vector2(3, 5), rim: 0.35f, pulse: 0.03f, bands: 3, fog: 1, softness: 0.35f, mapStrength: 0.85f);
            var b = new MeshBuilder();
            b.Box(Matrix4x4.TRS(new Vector3(0, -0.95f, 10), Quaternion.identity, new Vector3(52, 1.9f, 20)), Color.white);
            Part("Deck", transform, b, deck);

            var face = Surf(L(0.3f, 0.04f, 0.1f), rim: 0.7f, pulse: 0.02f, bands: 2, fog: 0.8f);
            b = new MeshBuilder();
            b.Box(Matrix4x4.TRS(new Vector3(0, -0.95f, -0.04f), Quaternion.identity, new Vector3(52, 1.9f, 0.08f)), Color.white);
            Part("DeckFace", transform, b, face);

            var gold = Surf(L(1f, 0.74f, 0.3f), rim: 1.3f, pulse: 0.05f, lift: 0.08f, bands: 3, fog: 0.5f);
            b = new MeshBuilder();
            b.Box(Matrix4x4.TRS(new Vector3(0, -0.07f, -0.06f), Quaternion.identity, new Vector3(52, 0.14f, 0.16f)), Color.white);
            b.Box(Matrix4x4.TRS(new Vector3(0, -1.0f, -0.06f), Quaternion.identity, new Vector3(52, 0.1f, 0.12f)), Color.white);
            Part("DeckTrim", transform, b, gold);

            // the lit lip
            var lip = AddMat(L(1f, 0.72f, 0.36f), 1.2f, 0, 0.2f);
            b = new MeshBuilder();
            b.Box(Matrix4x4.TRS(new Vector3(0, 0.012f, 0.1f), Quaternion.identity, new Vector3(52, 0.02f, 0.14f)), Color.white);
            Part("Lip", transform, b, lip);

            // the pool of light on the boards and the gilt ring of the stage mark
            _poolMat = Card(new Color(1f, 0.78f, 0.45f), 0.55f, 0.4f, false);
            CardAt("Pool", _poolMat, new Vector3(0, 0.03f, 9.5f), new Vector3(90, 0, 0), new Vector2(26, 14));
            _ringMat = AddMat(L(1f, 0.8f, 0.4f), 0.9f, 0, 0.3f);
            b = new MeshBuilder();
            b.Torus(5.2f, 0.07f, 6, 96, Mathf.PI * 2, Matrix4x4.Rotate(Quaternion.Euler(90, 0, 0)), Color.white);
            var ring = Part("StageMark", transform, b, _ringMat);
            ring.transform.localPosition = new Vector3(0, 0.04f, 9.5f);
                        _ring = ring.transform;
        }

        /// Red velvet drapes gathered at both sides and a swagged valance with a gilt fringe, in front of the stage.
        void BuildDrapes()
        {
            var velvet = Surf(L(0.6f, 0.04f, 0.1f), rim: 1.5f, pulse: 0.03f, bands: 3, softness: 0.45f, fog: 0.5f, twoSided: true);
            var gold = Surf(L(1f, 0.74f, 0.3f), rim: 1.3f, pulse: 0.04f, lift: 0.06f, bands: 3, fog: 0.4f);
            const float z = 1.2f, top = 16f, gap = 8.4f, outerX = 26f;
            var b = new MeshBuilder();
            foreach (float side in new[] { -1f, 1f })
            {
                b.Surface(56, 22, (u, v) =>
                {
                    float y = v * top;
                    const float tie = 2.8f;
                    float gather = y > tie ? Mathf.Lerp(0.5f, 1f, Mathf.Clamp01((y - tie) / 7f)) : Mathf.Lerp(0.5f, 0.88f, Mathf.Clamp01((tie - y) / tie));
                    float inner = gap + (1 - gather) * 2.4f;
                    float x = Mathf.Lerp(inner, outerX, u);
                    float fold = Mathf.Sin(u * Mathf.PI * 22f) * 0.24f * (0.6f + 0.4f * gather);
                    var p = new Vector3(side * x, y, z + fold + (1 - u) * 0.1f);
                    var n = new Vector3(-Mathf.Cos(u * Mathf.PI * 22f) * 0.6f * side, 0, -1).normalized;
                    return (p, n);
                }, Matrix4x4.identity, Color.white, Vector2.one);
            }
            // the valance: swags across the top
            b.Surface(120, 8, (u, v) =>
            {
                float x = Mathf.Lerp(-outerX, outerX, u);
                float bottom = SwagY(x);
                float y = Mathf.Lerp(bottom, top + 2, v);
                float fold = Mathf.Sin(u * Mathf.PI * 70f) * 0.06f * (1 - v);
                return (new Vector3(x, y, z - 0.25f + fold), Vector3.back);
            }, Matrix4x4.identity, Color.white, Vector2.one);
            Part("Drapes", transform, b, velvet);

            // gilt fringe under the swags, the tie-back cords and a gilt pelmet board
            var g = new MeshBuilder();
            var pts = new Vector3[121];
            for (int i = 0; i <= 120; i++)
            {
                float x = Mathf.Lerp(-outerX, outerX, i / 120f);
                pts[i] = new Vector3(x, SwagY(x) - 0.06f, z - 0.3f);
            }
            g.Tube(pts, 0.1f, 6, Matrix4x4.identity, Color.white);
            foreach (float side in new[] { -1f, 1f })
            {
                g.Tube(new[] { new Vector3(side * (gap + 0.4f), 2.8f, z - 0.4f), new Vector3(side * (gap + 2.4f), 2.9f, z - 0.4f), new Vector3(side * (gap + 4.4f), 2.4f, z - 0.3f) }, 0.07f, 5, Matrix4x4.identity, Color.white);
            }
            Part("Fringe", transform, g, gold);
        }

        /// The valance's lower edge: three swags (a shallow scallop per span).
        static float SwagY(float x)
        {
            float u = (x + 26f) / 52f * 5f;
            float s = Mathf.Abs(Mathf.Sin(u * Mathf.PI));
            return 9.1f - s * 1.1f;
        }

        /// The gilt sunburst on the back wall: pale wedges that turn very slowly behind a pink halo.
        void BuildBurst()
        {
            var burstGo = new GameObject("Sunburst");
            burstGo.transform.SetParent(transform, false);
            burstGo.transform.localPosition = new Vector3(0, 6.6f, 20.2f);
            _burst = burstGo.transform;
            var b = new MeshBuilder();
            const int n = 20; const float len = 34f, half = 4.2f * Mathf.Deg2Rad;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                var perp = new Vector3(-dir.y, dir.x, 0);
                b.Surface(1, 1, (u, v) =>
                {
                    float d = (1 - v) * len;   // uv.y = 1 at the hub: the shader's ramp fades the far end
                    return (dir * d + perp * ((u - 0.5f) * 2 * Mathf.Tan(half) * d), Vector3.back);
                }, Matrix4x4.identity, Color.white, Vector2.one);
            }
            Part("Rays", burstGo.transform, b, AddMat(L(1f, 0.78f, 0.5f), 0.20f, 0, 0.3f, 1f));
            _haloMat = Card(new Color(1f, 0.32f, 0.62f), 0.55f, 0.7f, false);
            CardAt("Halo", _haloMat, new Vector3(0, 0, -0.1f), Vector3.zero, new Vector2(30, 30), burstGo.transform);
            // a gilt medallion rim at the hub
            var gold = Surf(L(1f, 0.74f, 0.3f), rim: 1.3f, pulse: 0.04f, lift: 0.06f, bands: 3, fog: 0.4f);
            var mb = new MeshBuilder();
            mb.Torus(3.4f, 0.16f, 6, 64, Mathf.PI * 2, Matrix4x4.identity, Color.white);
            mb.Torus(4.2f, 0.08f, 5, 64, Mathf.PI * 2, Matrix4x4.identity, Color.white);
            Part("Medallion", burstGo.transform, mb, gold);
        }

        /// Marquee bulbs on the valance, footlights on the lip and strings of party lights across the stage.
        void BuildLights()
        {
            var pos = new List<Vector3>(); var col = new List<Color>(); var kind = new List<int>(); var scl = new List<float>();
            Color[] warm = { L(1f, 0.8f, 0.4f), L(1f, 0.55f, 0.35f), L(1f, 0.9f, 0.65f) };
            // footlights along the lip
            for (int i = -15; i <= 15; i++) { pos.Add(new Vector3(i * 1.2f, 0.14f, 0.5f)); col.Add(warm[0]); kind.Add(0); scl.Add(1.2f); }
            _footN = pos.Count;
            // marquee bulbs along the valance fringe
            for (float x = -25f; x <= 25f; x += 0.85f) { pos.Add(new Vector3(x, SwagY(x) - 0.42f, 0.78f)); col.Add(warm[(int)((x + 40) * 3) % 3]); kind.Add(1); scl.Add(1f); }
            _marqN = pos.Count - _footN;
            // strings of party lights behind the valance, sagging between the walls
            Color[] party = { L(1f, 0.82f, 0.3f), L(1f, 0.38f, 0.65f), L(0.3f, 0.85f, 1f), L(1f, 0.55f, 0.25f) };
            int pi = 0;
            foreach (float zz in new[] { 5f, 10f, 15.5f })
                for (float x = -28f; x <= 28f; x += 1.9f)
                {
                    float u = x / 28f;
                    pos.Add(new Vector3(x, 11f, zz)); col.Add(party[pi++ % party.Length]); kind.Add(2); scl.Add(0.9f);
                }
            int n = pos.Count;
            _bulbAt = pos.ToArray(); _bulbColor = col.ToArray(); _bulbKind = kind.ToArray();
            _bulbPhase = new float[n]; _bulbBase = scl.ToArray();
            var rng = new FxRng(0x51e11);
            for (int i = 0; i < n; i++) _bulbPhase[i] = rng.Next() * 6.28f;
            var tpl = new MeshBuilder();
            tpl.Sphere(0.1f, 6, 4, Matrix4x4.identity, Color.white, Vector3.one);
            _bulbs = new MeshBatch("Bulbs", transform, tpl, n, AddMat(Color.white, 1.5f, 0, 0.4f));
            // the wires of the party strings
            var wire = Surf(L(0.08f, 0.04f, 0.08f), rim: 0.2f, pulse: 0, bands: 2, fog: 1);
            var wb = new MeshBuilder();
            foreach (float zz in new[] { 5f, 10f, 15.5f })
            {
                var pts = new List<Vector3>();
                for (float x = -28f; x <= 28.01f; x += 1.9f)
                {
                    float u = x / 28f;
                    pts.Add(new Vector3(x, 12.3f - 1.2f * (1 - u * u), zz));
                }
                wb.Tube(pts.ToArray(), 0.025f, 4, Matrix4x4.identity, Color.white);
            }
            Part("Wires", transform, wb, wire);
            // fix the bulb heights onto the wires
            for (int i = _footN + _marqN; i < n; i++)
            {
                float u = _bulbAt[i].x / 28f;
                _bulbAt[i].y = 12.3f - 1.2f * (1 - u * u) - 0.12f;
            }
        }

        /// The heads and shoulders of the house in front of the stage, and the glow sticks over them.
        void BuildHouse()
        {
            var rng = new FxRng(0x40115e);
            const int rows = 3, per = 26;
            int n = rows * per;
            _headAt = new Vector3[n]; _headPhase = new float[n]; _headTint = new Color[n];
            Color[] hue = { L(0.22f, 0.08f, 0.3f), L(0.3f, 0.07f, 0.18f), L(0.12f, 0.1f, 0.34f), L(0.34f, 0.14f, 0.1f), L(0.16f, 0.08f, 0.24f) };
            int k = 0;
            for (int r = 0; r < rows; r++)
                for (int i = 0; i < per; i++, k++)
                {
                    float x = (i - per / 2f + 0.5f) * (1.5f - r * 0.1f) * 1.25f + (rng.Next() - 0.5f) * 0.6f + (r % 2) * 0.55f;
                    float z = -4.4f - r * 1.7f + (rng.Next() - 0.5f) * 0.5f;
                    float y = 0.55f + r * 0.5f - 0.2f * rng.Next();
                    _headAt[k] = new Vector3(x * (1 + r * 0.0f), y, z);
                    _headPhase[k] = rng.Next() * 6.28f;
                    _headTint[k] = hue[(int)(rng.Next() * hue.Length) % hue.Length] * (0.75f + rng.Next() * 0.7f);
                }
            var hm = new MeshBuilder(); hm.Sphere(0.42f, 8, 6, Matrix4x4.identity, Color.white, new Vector3(1, 1.08f, 1));
            var bm = new MeshBuilder(); bm.Sphere(0.82f, 8, 5, Matrix4x4.Translate(new Vector3(0, -0.95f, 0)), Color.white, new Vector3(1.3f, 0.95f, 0.8f));
            var mat = Surf(Color.white, rim: 2.4f, pulse: 0.04f, bands: 3, fog: 0.5f, softness: 0.4f);
            _heads = new MeshBatch("Heads", transform, hm, n, mat);
            _bodies = new MeshBatch("Shoulders", transform, bm, n, mat);

            // glow sticks: thin bars held up over the rows
            int sn = 18;
            _stickAt = new Vector3[sn]; _stickPhase = new float[sn]; _stickTint = new Color[sn];
            Color[] glow = { L(1f, 0.85f, 0.3f), L(1f, 0.4f, 0.7f), L(0.3f, 0.9f, 1f), L(0.6f, 1f, 0.5f) };
            for (int i = 0; i < sn; i++)
            {
                int who = (int)(rng.Next() * n) % n;
                for (int tries = 0; tries < 40 && Mathf.Abs(_headAt[who].x) < 5.6f; tries++) who = (who + 7) % n;   // none behind the menu column
                _stickAt[i] = _headAt[who] + new Vector3((rng.Next() - 0.5f) * 0.9f, 0.9f, 0.1f);
                _stickPhase[i] = rng.Next() * 6.28f;
                _stickTint[i] = glow[i % glow.Length];
            }
            var st = new MeshBuilder(); st.Box(Matrix4x4.TRS(new Vector3(0, 0.45f, 0), Quaternion.identity, new Vector3(0.07f, 0.9f, 0.07f)), Color.white);
            _sticks = new MeshBatch("GlowSticks", transform, st, sn, AddMat(Color.white, 1.9f, 0, 0.2f));
        }

        /// Gold and pink flecks drifting up through the light.
        void BuildGlitter()
        {
            var rng = new FxRng(0x611773);
            int n = 44;
            _fleckAt = new Vector3[n]; _fleckPhase = new float[n]; _fleckSpeed = new float[n]; _fleckTint = new Color[n];
            Color[] c = { L(1f, 0.85f, 0.4f), L(1f, 0.5f, 0.75f), L(0.6f, 0.9f, 1f) };
            for (int i = 0; i < n; i++)
            {
                _fleckAt[i] = new Vector3((rng.Next() - 0.5f) * 30f, rng.Next() * 12f, 1.5f + rng.Next() * 16f);
                _fleckPhase[i] = rng.Next() * 6.28f;
                _fleckSpeed[i] = 0.12f + rng.Next() * 0.22f;
                _fleckTint[i] = c[i % c.Length] * 0.9f;
            }
            var t = new MeshBuilder(); t.Octahedron(Matrix4x4.Scale(new Vector3(0.09f, 0.14f, 0.09f)), Color.white);
            _flecks = new MeshBatch("Glitter", transform, t, n, AddMat(Color.white, 1.7f, 0, 0.3f));
        }

        // ---------------------------------------------------------------- lifecycle

        /// Turn the hall on and put the camera in `pose`. Safe to call when already live.
        public void Resume(string pose, bool snap)
        {
            if (Live) { SetPose(pose, snap); return; }
            if (_look == null) return;
            if (!_cam) _cam = Camera.main;
            if (_cam) { _flags = _cam.clearFlags; _bg = _cam.backgroundColor; }
            gameObject.SetActive(true);
            Live = true;
            if (_cam)
            {
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = _look.Palette.Live[PC.Fog];
                _cam.nearClipPlane = 0.3f; _cam.farClipPlane = 160f;
            }
            _poseAge = 0;
            if (_cam && RenderQuality.Level.Msaa > 1)
            {
                var ad = _cam.GetUniversalAdditionalCameraData();
                if (ad != null) { _aa = ad.antialiasing; ad.antialiasing = AntialiasingMode.FastApproximateAntialiasing; _aaSet = true; }
            }
            SetPose(pose, true);
            _look.Rig?.Reset();
            ApplyPose(0, true);
        }

        /// Give the camera back as found (a game is about to bind its own rig) and switch the hall off.
        public void Suspend()
        {
            if (!Live) return;
            Live = false;
            _look.Rig?.Restore();
            _look.Rig?.Release();
            if (_aaSet && _cam) { var ad = _cam.GetUniversalAdditionalCameraData(); if (ad != null) ad.antialiasing = _aa; }
            _aaSet = false;
            if (_cam) { _cam.clearFlags = _flags; _cam.backgroundColor = _bg; }
            Shader.SetGlobalVector("_BonShadowFocus", Vector4.zero);
            gameObject.SetActive(false);
        }

        public void SetPose(string name, bool snap = false)
        {
            if (!Poses.ContainsKey(name)) name = "title";
            if (name == PoseName && !snap) return;
            PoseName = name;
            _poseAge = 0;
            if (snap) ApplyPose(0, true);
        }

        /// The title music's beat (when it runs), so the hall's key breathes, the camera pushes and the set pulses.
        public void Tick(double beat, bool onGrid, float beatPulse)
        {
            if (!Live || _look == null) return;
            _look.BeatPulse = beatPulse;
            _look.TickBeat(onGrid ? beat : -1);
        }

        void ApplyPose(float dt, bool snap)
        {
            var rig = _look?.Rig;
            if (rig == null) return;
            var p = Poses[PoseName];
            float yaw = p.Yaw, tx = p.Target.x;
            if (!Settings.ReduceMotion)
            {
                // a slow pan so the hall is never still, even without the rig's own drift
                yaw += Mathf.Sin(_time * 0.11f) * 0.022f;
                tx += Mathf.Sin(_time * 0.07f + 1f) * 0.5f;
            }
            rig.Frame(new Vector3(tx, p.Target.y, p.Target.z), p.Dist, p.Height, yaw, p.Fov, 2.0f, snap);
        }

        void Update()
        {
            if (!Live) return;
            float dt = Time.unscaledDeltaTime;
            _time += dt; _poseAge += dt;
            _frame++;
            _quiet = RenderQuality.Level.Tier == QualityTier.Low;
            ApplyPose(dt, false);
            if (_burst && !Settings.ReduceMotion) _burst.localRotation = Quaternion.Euler(0, 0, _time * 1.6f);
            if (_ring) _ring.localScale = Vector3.one * (1 + _look.BeatPulse * 0.025f);
            if (_poolMat) _poolMat.SetFloat("_Intensity", 0.5f + _look.BeatPulse * 0.1f);
            if (_haloMat) _haloMat.SetFloat("_Intensity", 0.5f + _look.BeatPulse * 0.1f);
            if (_quiet ? _frame % 3 == 0 : _frame % 2 == 0) FlushAll(_time);
        }

        void FlushAll(float t)
        {
            bool still = Settings.ReduceMotion;
            float pulse = _look != null ? _look.BeatPulse : 0;

            // bulbs: a slow twinkle, a chase running along the marquee, a lift on the beat
            for (int i = 0; i < _bulbAt.Length; i++)
            {
                float k;
                if (still) k = 0.85f;
                else
                {
                    float tw = 0.78f + 0.22f * Mathf.Sin(t * 2.1f + _bulbPhase[i]);
                    k = tw;
                    if (_bulbKind[i] == 1) k *= 0.8f + 0.5f * Mathf.Max(0, Mathf.Sin(t * 3.2f - _bulbAt[i].x * 0.45f));
                    else if (_bulbKind[i] == 0) k *= 0.85f + 0.3f * pulse;
                }
                _bulbs.Matrix[i] = Matrix4x4.TRS(_bulbAt[i], Quaternion.identity, new Vector3(_bulbBase[i], _bulbKind[i] == 0 ? 0.55f * _bulbBase[i] : _bulbBase[i], _bulbBase[i]));
                _bulbs.Tint[i] = _bulbColor[i] * k;
            }
            _bulbs.Flush();

            // the house: a gentle sway, hopping on the beat
            for (int i = 0; i < _headAt.Length; i++)
            {
                float hop = still ? 0 : Mathf.Max(0, Mathf.Sin(t * 2.4f + _headPhase[i])) * 0.06f + pulse * 0.07f * (0.6f + 0.4f * Mathf.Sin(_headPhase[i] * 3));
                float sway = still ? 0 : Mathf.Sin(t * 0.9f + _headPhase[i]) * 0.05f;
                var p = _headAt[i] + new Vector3(sway, hop, 0);
                _heads.Matrix[i] = Matrix4x4.Translate(p);
                _bodies.Matrix[i] = Matrix4x4.Translate(p);
                _heads.Tint[i] = _headTint[i] * 1.15f;
                _bodies.Tint[i] = _headTint[i];
            }
            _heads.Flush(); _bodies.Flush();

            bool hideProps = still || PoseName == "attract";   // reduced motion: no waving sticks, no confetti; attract: no crowd under them
            for (int i = 0; i < _stickAt.Length; i++)
            {
                float a = still ? 0 : Mathf.Sin(t * 2.6f + _stickPhase[i]) * 0.35f;
                _sticks.Matrix[i] = hideProps ? Matrix4x4.Scale(Vector3.zero) : Matrix4x4.TRS(_stickAt[i], Quaternion.Euler(0, 0, a * Mathf.Rad2Deg), Vector3.one);
                _sticks.Tint[i] = _stickTint[i] * (0.8f + 0.3f * pulse);
            }
            _sticks.Flush();

            if (!_quiet)
            {
                for (int i = 0; i < _fleckAt.Length; i++)
                {
                    var p = _fleckAt[i];
                    float y = still ? p.y : Mathf.Repeat(p.y + t * _fleckSpeed[i], 12.5f);
                    float x = p.x + (still ? 0 : Mathf.Sin(t * 0.5f + _fleckPhase[i]) * 0.5f);
                    float tw = 0.5f + 0.5f * Mathf.Sin(t * 2.5f + _fleckPhase[i] * 2);
                    float fade = Mathf.Clamp01(y * 0.6f) * Mathf.Clamp01((12.5f - y) * 0.6f);
                    _flecks.Matrix[i] = still ? Matrix4x4.Scale(Vector3.zero) : Matrix4x4.TRS(new Vector3(x, y, p.z), Quaternion.Euler(0, t * 60 + _fleckPhase[i] * 50, 20), Vector3.one);
                    _flecks.Tint[i] = _fleckTint[i] * (0.25f + 0.75f * tw) * fade;
                }
                _flecks.Flush();
            }
        }

        void OnDestroy()
        {
            if (Live && _cam) { _cam.clearFlags = _flags; _cam.backgroundColor = _bg; }
            foreach (var o in _owned) if (o) Destroy(o);
            _owned.Clear();
            foreach (var m in new[] { _heads?.Mesh, _bodies?.Mesh, _sticks?.Mesh, _flecks?.Mesh, _bulbs?.Mesh }) if (m) Destroy(m);
        }
    }
}
