// Finale Fever: the stage. A concert hall seen from the orchestra platform: the maestro on his podium in the middle,
// facing us (we are his orchestra), the timpani section on both sides of him (the player's drum front right, the
// rivals' or the section's other three), and behind him, through a red-velvet proscenium, the dark house: raked
// stalls full of listeners, two gilt balcony tiers with sconces, three chandeliers.
//
//   maestro    a bean in tails with a white mane, a moustache and a baton. He conducts the curve: every stroke lands
//              its ictus on a curve beat (the pattern follows the bar: 4, 3 or 2), rebounds and falls back into the next
//              one, so the sweep IS the tempo (a ramp speeds or slows his arm, a jump changes it on the beat, a fermata
//              holds the baton high, palm up). While the pulse is hidden he tucks the baton away, closes his eyes and
//              cups his ear: no baton, no ictus flash, no beat nudge, no bounce anywhere; the hall hushes to a moon spot.
//   timpani    four kettle drums with their players. The player's drummer strikes on every tap (the head rings in the
//              call's colour); the others strike on their own beats (a CPU's rolled taps in a party, the section's in
//              free play), never while the pulse is hidden.
//   house      the kit's crowd on raked risers: it bounces on the beat while the baton shows, leans in during a hush,
//              sags on a miss, waves on a clean line, stands for the finale.
//   light      one palette per excerpt (matinee gold, sapphire gala, crimson finale), the "-hush" twin while hidden,
//              the "-red" twin on the last heart (SURVIVE). A real key shadow on the platform, spot cones on the maestro.
//
// Everything is built in Load (no physics, no Shader.Find: materials are copies of Resources/Look templates).
// Per frame: transforms, a few MeshBatch flushes and material floats, no allocations.

using Bonanza.Core;
using Bonanza.Look;
using UnityEngine;

namespace Bonanza
{
    public sealed partial class FinaleFeverGame
    {
        // ------------------------------------------------------------ layout (Unity space: the camera looks down +z)

        static readonly Vector3 PodiumAt = new Vector3(0, 0, 4.0f);
        const float PodiumH = 0.46f, PodiumR = 1.2f;
        const float StageFront = 7.0f, StallsY = -1.35f;
        const float MaestroScale = 1.18f;
        static readonly Vector3 CamTarget = new Vector3(0, 2.3f, 3.6f);
        const float CamDist = 10.4f, CamHeight = 0.95f, CamFov = 40f;
        static readonly Color[] SeatColors = { new Color(1f, 0.85f, 0.24f), new Color(0.3f, 0.84f, 1f), new Color(1f, 0.48f, 0.85f), new Color(0.62f, 0.91f, 0.48f) };
        static readonly Color Lime = Hex(0x9ee87a), Sky = Hex(0x6fd0ff), Orange = Hex(0xff8a3c), Coral = Hex(0xff5d73);

        readonly System.Collections.Generic.List<Object> _owned = new System.Collections.Generic.List<Object>();
        Env _env;

        // ------------------------------------------------------------ materials

        T Own<T>(T o) where T : Object { _owned.Add(o); return o; }

        Material Surf(Color c, float rim = 1, float pulse = 0.06f, float lift = 0, float bands = 3, float softness = 0.22f, float fog = 1, Texture2D map = null, Vector2? tiling = null, float mapStrength = 0, bool twoSided = false)
        {
            var m = Own(new Material(EnvKit.ToonEnv));
            m.SetColor("_BaseColor", c);
            if (map) { m.SetTexture("_BaseMap", map); m.SetTextureScale("_BaseMap", tiling ?? Vector2.one); }
            m.SetFloat("_MapStrength", mapStrength);
            m.SetFloat("_Rim", rim); m.SetFloat("_Pulse", pulse); m.SetFloat("_Lift", lift); m.SetFloat("_Bands", bands);
            m.SetFloat("_Softness", softness);
            m.SetFloat("_Cull", twoSided ? (float)UnityEngine.Rendering.CullMode.Off : (float)UnityEngine.Rendering.CullMode.Back);
            m.SetFloat("_FogMul", fog);
            return m;
        }

        Material AddMat(Color c, float intensity = 1, float facing = 0, float fog = 0)
        {
            var m = Own(new Material(EnvKit.GlowAddMat));
            m.SetColor("_Color", c);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Facing", facing);
            m.SetFloat("_Ramp", 0);
            m.SetFloat("_FogMul", fog);
            return m;
        }

        Material GlowCard(Color c, float intensity, float core = 1.6f)
        {
            var m = Own(new Material(EnvKit.GlowMat));
            m.SetColor("_Color", c);
            m.SetFloat("_Intensity", intensity);
            m.SetFloat("_Billboard", 1);
            m.SetFloat("_Core", core);
            m.SetVector("_Stretch", new Vector4(1, 1, 0, 0));
            return m;
        }

        MeshRenderer Part(string name, Transform parent, MeshBuilder b, Material m, Vector3 pos, bool cast = false)
        {
            var r = EnvKit.Renderer(name, parent, Own(b.ToMesh(name)), m);
            r.transform.localPosition = pos;
            r.receiveShadows = true;
            if (cast) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return r;
        }

        static Color L(uint hex) => Hex(hex).linear;
        static Color L(float r, float g, float b) => new Color(r, g, b).linear;

        /// A lathe around +y: radius r(v) at height y(v), v = 0 bottom .. 1 top.
        static void Lathe(MeshBuilder b, int seg, int rows, System.Func<float, (float y, float r)> f, Matrix4x4 m, Color c)
        {
            b.Surface(seg, rows, (u, v) =>
            {
                float a = u * Mathf.PI * 2;
                var (y, r) = f(v);
                var (y2, r2) = f(Mathf.Min(1, v + 0.01f));
                var (y1, r1) = f(Mathf.Max(0, v - 0.01f));
                float dy = y2 - y1, dr = r2 - r1;
                var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var n = (d * dy - Vector3.up * dr).normalized;
                if (n.sqrMagnitude < 1e-6f) n = v > 0.5f ? Vector3.up : Vector3.down;
                return (d * r + Vector3.up * y, n);
            }, m, c, Vector2.one);
        }

        /// Planks along x for the platform: a value map (tiles), grain streaks and seams.
        static Texture2D PlankTex(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "ff-planks" };
            var px = new Color32[n * n];
            var rng = new FxRng(0xf1f1);
            const int Rows = 8;
            var tone = new float[Rows * 4];
            for (int i = 0; i < tone.Length; i++) tone[i] = 0.84f + rng.Next() * 0.16f;
            var off = new float[Rows];
            for (int i = 0; i < Rows; i++) off[i] = rng.Next();
            for (int y = 0; y < n; y++)
            {
                int row = y * Rows / n;
                float fy = (y * Rows / (float)n) - row;
                for (int x = 0; x < n; x++)
                {
                    float fx = x / (float)n + off[row];
                    int plank = (int)(fx * 2) & 3;
                    float seamX = Mathf.Abs((fx * 2) % 1 - 0.0f);
                    float v = tone[row * 4 + plank];
                    float grain = 0.5f + 0.5f * Mathf.Sin(fx * 90f + Mathf.Sin(fy * 6 + row) * 3f + row * 7);
                    v *= 0.93f + 0.07f * grain;
                    if (fy < 0.05f || fy > 0.95f) v *= 0.55f;          // the seam between rows
                    if (seamX < 0.006f || seamX > 0.994f) v *= 0.62f;  // the butt joints
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255), 0, 255);
                    px[y * n + x] = new Color32(b, b, b, 255);
                }
            }
            t.SetPixels32(px);
            t.Apply(true, false);
            return t;
        }

        // ------------------------------------------------------------ build

        void BuildStage()
        {
            var cam = Camera.main;
            if (cam) { cam.nearClipPlane = 0.3f; cam.farClipPlane = 220f; }
            Look.Rig.SetPushGain(0.6f);
            Look.Rig.SetDriftGain(0.7f);
            Look.Fx.SetGroundY(0);
            Look.Fx.SetFocus(new Vector3(0, 1.6f, 3.2f));
            Look.ShadowFocus(new Vector3(0, 0, 2.6f), 7.6f);   // the maestro, his podium and the front drums

            _env = Look.CreateEnv(EnvPreset.Stage, o =>
            {
                o.SkipGround = true;
                o.Backdrop.Arches = 0; o.Backdrop.Skyline = 0;
                o.Floaters.Count = 0;
                // Two spot cones from the flies onto the maestro, one onto the player's drum.
                o.Spotlights.Count = 3;
                o.Spotlights.Anchors = new[] { new Vector3(-3.2f, 11.5f, 6.5f), new Vector3(3.2f, 11.5f, 6.5f), new Vector3(5.5f, 11.0f, 3.5f) };
                o.Spotlights.Targets = new[] { new Vector3(0, 0, 4.0f), new Vector3(0, 0, 4.0f), new Vector3(3.6f, 0, 0.6f) };
                o.Spotlights.Height = 12f; o.Spotlights.Radius = 1.55f; o.Spotlights.Sweep = 0.025f;
                o.Spotlights.Base = 0.03f; o.Spotlights.Flare = 0.025f; o.Spotlights.Tint = new Color(1f, 0.95f, 0.85f);
            });
            _env.AddCrowd(new EnvCrowd.Options
            {
                Style = EnvCrowd.FigureStyle.Figure, Stands = EnvCrowd.StandStyle.Risers, Poses = true, PoseKinds = 4,
                Rows = 7, PerRow = 30, RowGrow = 3, Radius = 13.2f, RowDepth = 1.5f, RowRise = 0.62f, Arc = Mathf.PI * 0.6f,
                Scale = 1.75f, Center = new Vector3(0, StallsY + 0.05f, -3.2f), RiserY = StallsY - 0.2f, RisersFollowRows = true,
                PaletteMix = 0.55f, BrightMin = 0.55f, BrightMax = 0.85f, Deepen = 1.3f, Energy = 0.25f, Rim = 0.9f, FogMul = 0.85f,
                Seed = 0xf1a1e, RiserLights = true,
            });
            _env.Crowd.SetEnergy(0.22f);

            BuildPlatform();
            BuildHouse();
            BuildProscenium();
            BuildChandeliers();
            BuildPodium();
            BuildMaestro();
            BuildTimpani();
        }

        void DestroyStage()
        {
            _trail?.Release();
            _trail = null;
            foreach (var o in _owned) if (o) Destroy(o);
            _owned.Clear();
            if (_heads != null && _heads.Mesh) Destroy(_heads.Mesh);
        }

        // ---- the orchestra platform: polished planks, a gold inlay round the podium, a lit lip and footlights

        Material _floorMat, _lipMat, _footMat, _inlayMat;

        void BuildPlatform()
        {
            var planks = Own(PlankTex(256));
            _floorMat = Surf(L(0.62f, 0.36f, 0.2f), rim: 0.35f, pulse: 0.02f, bands: 3, softness: 0.35f, map: planks, tiling: new Vector2(6, 9), mapStrength: 0.9f);
            var b = new MeshBuilder();
            b.Box(Matrix4x4.TRS(new Vector3(0, -0.7f, -1.5f), Quaternion.identity, new Vector3(34, 1.4f, 17f)), Color.white);
            Part("Platform", transform, b, _floorMat, Vector3.zero);
            // the riser face toward the house, a step darker
            var face = Surf(L(0.16f, 0.07f, 0.06f), rim: 0.4f, pulse: 0.02f, bands: 2);
            b = new MeshBuilder();
            b.Box(Matrix4x4.TRS(new Vector3(0, (StallsY - 0.02f) / 2, StageFront + 0.06f), Quaternion.identity, new Vector3(34, -StallsY + 0.02f, 0.1f)), Color.white);
            Part("PlatformFace", transform, b, face, Vector3.zero);

            // gold inlay: concentric rings on the floor round the podium (the eye goes to the maestro)
            _inlayMat = Surf(Color.white, rim: 0.5f, pulse: 0.1f, lift: 0.12f, bands: 2);
            b = new MeshBuilder();
            var gold = L(1f, 0.74f, 0.32f);
            foreach (var (r, w) in new[] { (1.75f, 0.05f), (2.05f, 0.025f), (3.2f, 0.035f) })
                b.Torus(r, w, 3, 72, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(PodiumAt.x, 0.004f, PodiumAt.z), Quaternion.Euler(90, 0, 0), new Vector3(1, 1, 0.12f)), gold);
            Part("Inlay", transform, b, _inlayMat, Vector3.zero);

            // the lit lip along the platform's front edge (web: the floor's glowing lip) and a row of hooded footlights
            _lipMat = AddMat(L(1f, 0.72f, 0.36f), 1.1f, 0.25f, 0.2f);
            b = new MeshBuilder();
            b.Box(Matrix4x4.TRS(new Vector3(0, 0.015f, StageFront - 0.02f), Quaternion.identity, new Vector3(34, 0.03f, 0.06f)), Color.white);
            Part("Lip", transform, b, _lipMat, Vector3.zero);
            var hood = Surf(L(0.1f, 0.06f, 0.05f), rim: 0.8f, pulse: 0, bands: 2);
            var hb = new MeshBuilder();
            var lb = new MeshBuilder();
            for (int i = -7; i <= 7; i++)
            {
                float x = i * 1.25f;
                hb.Box(Matrix4x4.TRS(new Vector3(x, 0.09f, StageFront - 0.18f), Quaternion.Euler(-20, 0, 0), new Vector3(0.5f, 0.16f, 0.22f)), Color.white);
                lb.Sphere(0.09f, 8, 5, Matrix4x4.Translate(new Vector3(x, 0.06f, StageFront - 0.32f)), Color.white, new Vector3(2.2f, 0.7f, 1));
            }
            Part("FootHoods", transform, hb, hood, Vector3.zero);
            _footMat = AddMat(L(1f, 0.82f, 0.5f), 1.2f, 0.5f, 0);
            Part("Footlights", transform, lb, _footMat, Vector3.zero);
        }

        // ---- the house: stalls floor, balconies with sconces and listeners, the back wall, the ceiling

        Material _balconyMat, _wallMat, _sconceMat, _carpetMat;

        void BuildHouse()
        {
            var center = new Vector3(0, 0, -3.2f);
            _carpetMat = Surf(Color.white, rim: 0.2f, pulse: 0, bands: 2, softness: 0.5f);
            Look.Track(_carpetMat, PC.Band, 0.4f);
            var b = new MeshBuilder();
            b.FloorQuad(60, 40, Matrix4x4.Translate(new Vector3(0, StallsY, StageFront + 20)), Color.white);
            Part("Stalls", transform, b, _carpetMat, Vector3.zero);

            // balconies: two gilt tiers on the arc of the stalls, crimson fronts with a gold rail and a sconce every few metres
            _balconyMat = Surf(Color.white, rim: 0.9f, pulse: 0.04f, lift: 0.02f, bands: 3, softness: 0.3f, fog: 0.8f);
            Look.Track(_balconyMat, PC.BandAlt, 0.95f);
            var gilt = Surf(L(1f, 0.76f, 0.34f), rim: 1.2f, pulse: 0.05f, lift: 0.08f, bands: 3, fog: 0.75f);
            var front = new MeshBuilder();
            var rail = new MeshBuilder();
            var sconce = new MeshBuilder();
            var heads = new MeshBuilder();
            var rng = new FxRng(0xba1c);
            Color[] crowd = { L(0xffcf6a), L(0xff8a5c), L(0xe86a8a), L(0xfff0d0), L(0x8ac8ff), L(0xb08cff) };
            float[] radii = { 21.5f, 24.5f }, tops = { 3.6f, 7.0f };
            const float arc = Mathf.PI * 0.62f;
            for (int t = 0; t < 2; t++)
            {
                float R = radii[t], top = tops[t];
                // the front: a curved panel with a soffit under it and the box seats' floor behind it
                front.Shell(R, R, 1.25f, 64, -arc / 2, arc, Matrix4x4.Translate(center + new Vector3(0, top - 0.62f, 0)), Color.white, Vector2.one, inward: true);
                front.Shell(R + 3.5f, R, 0.01f, 64, -arc / 2, arc, Matrix4x4.Translate(center + new Vector3(0, top - 1.25f, 0)), new Color(0.45f, 0.45f, 0.45f), Vector2.one, inward: true);
                // gilt: the rail on top, a band at the foot, ribs every few degrees
                rail.Torus(R - 0.05f, 0.09f, 6, 96, arc, Matrix4x4.TRS(center + new Vector3(0, top + 0.02f, 0), Quaternion.Euler(90, 0, 0), Vector3.one), Color.white, Mathf.PI / 2 - arc / 2);
                rail.Torus(R - 0.04f, 0.06f, 5, 96, arc, Matrix4x4.TRS(center + new Vector3(0, top - 1.22f, 0), Quaternion.Euler(90, 0, 0), Vector3.one), Color.white, Mathf.PI / 2 - arc / 2);
                int ribs = 18;
                for (int i = 0; i <= ribs; i++)
                {
                    float a = -arc / 2 + arc * i / ribs;
                    var p = center + new Vector3(Mathf.Sin(a) * (R - 0.06f), top - 0.62f, Mathf.Cos(a) * (R - 0.06f));
                    rail.Box(Matrix4x4.TRS(p, Quaternion.Euler(0, a * Mathf.Rad2Deg, 0), new Vector3(0.14f, 1.2f, 0.08f)), Color.white);
                    if (i < ribs)
                    {
                        float am = a + arc / ribs / 2;
                        var sp = center + new Vector3(Mathf.Sin(am) * (R - 0.12f), top - 0.45f, Mathf.Cos(am) * (R - 0.12f));
                        sconce.Sphere(0.11f, 8, 6, Matrix4x4.Translate(sp), Color.white, new Vector3(1, 1.3f, 1));
                    }
                }
                // listeners behind the rail: shoulders and heads, coloured once
                int n = 46 + t * 6;
                for (int i = 0; i < n; i++)
                {
                    float a = -arc / 2 + arc * (i + 0.5f + (rng.Next() - 0.5f) * 0.5f) / n;
                    float rr = R + 0.75f + rng.Next() * 0.5f;
                    var p = center + new Vector3(Mathf.Sin(a) * rr, top - 0.25f + rng.Next() * 0.15f, Mathf.Cos(a) * rr);
                    var c = crowd[(int)(rng.Next() * crowd.Length) % crowd.Length] * (0.45f + rng.Next() * 0.3f);
                    heads.Sphere(0.34f, 7, 4, Matrix4x4.Translate(p + new Vector3(0, -0.15f, 0)), c, new Vector3(1.2f, 0.8f, 1));
                    heads.Sphere(0.2f, 7, 5, Matrix4x4.Translate(p + new Vector3(0, 0.38f, 0)), L(0.85f, 0.68f, 0.56f) * 0.7f, Vector3.one);
                }
            }
            Part("BalconyFronts", transform, front, _balconyMat, Vector3.zero);
            Part("BalconyGilt", transform, rail, gilt, Vector3.zero);
            var people = Surf(Color.white, rim: 0.6f, pulse: 0.02f, bands: 2, softness: 0.5f, fog: 0.9f);
            Part("BalconyListeners", transform, heads, people, Vector3.zero);
            _sconceMat = AddMat(L(1f, 0.78f, 0.45f), 1.5f, 0.6f, 0.3f);
            Part("Sconces", transform, sconce, _sconceMat, Vector3.zero);

            // the back wall and side walls: a tall dark drum with gilt pilasters, and a ceiling with coffer rings
            _wallMat = Surf(Color.white, rim: 0.5f, pulse: 0.02f, bands: 2, softness: 0.4f, fog: 1);
            Look.Track(_wallMat, PC.Band, 0.42f);
            b = new MeshBuilder();
            b.Shell(29, 29, 16, 72, -Mathf.PI * 0.55f, Mathf.PI * 1.1f, Matrix4x4.Translate(center + new Vector3(0, 6.5f, 0)), Color.white, Vector2.one, inward: true);
            Part("HallWall", transform, b, _wallMat, Vector3.zero);
            b = new MeshBuilder();
            for (int i = 0; i <= 14; i++)
            {
                float a = -Mathf.PI * 0.5f + Mathf.PI * i / 14;
                var p = center + new Vector3(Mathf.Sin(a) * 28.6f, 6.5f, Mathf.Cos(a) * 28.6f);
                b.Box(Matrix4x4.TRS(p, Quaternion.Euler(0, a * Mathf.Rad2Deg, 0), new Vector3(0.7f, 16, 0.4f)), Color.white);
            }
            Part("Pilasters", transform, b, gilt, Vector3.zero);
            var ceil = Surf(Color.white, rim: 0.3f, pulse: 0, bands: 2, fog: 1);
            Look.Track(ceil, PC.Band, 0.3f);
            b = new MeshBuilder();
            b.Disc(32, 48, Matrix4x4.TRS(center + new Vector3(0, 13.5f, 0), Quaternion.Euler(180, 0, 0), Vector3.one), Color.white);
            Part("Ceiling", transform, b, ceil, Vector3.zero);
            b = new MeshBuilder();
            foreach (float r in new[] { 9f, 15f, 21f })
                b.Torus(r, 0.16f, 5, 96, Mathf.PI * 2, Matrix4x4.TRS(center + new Vector3(0, 13.4f, 6), Quaternion.Euler(90, 0, 0), Vector3.one), Color.white);
            Part("Coffers", transform, b, gilt, Vector3.zero);
        }

        // ---- the proscenium: red velvet drapes gathered at both sides, a swagged valance, gold fringe and tassels

        Material _drapeMat;

        void BuildProscenium()
        {
            _drapeMat = Surf(L(0.62f, 0.05f, 0.09f), rim: 1.6f, pulse: 0.02f, bands: 3, softness: 0.45f, fog: 0.4f, twoSided: true);
            var gold = Surf(L(1f, 0.74f, 0.3f), rim: 1.3f, pulse: 0.04f, lift: 0.06f, bands: 3, fog: 0.4f);
            float z = StageFront + 0.35f;
            var b = new MeshBuilder();
            foreach (float side in new[] { -1f, 1f })
            {
                // a drape from the flies to the floor: folds across, pulled in to a tie-back at 2.6 m and flaring below it
                b.Surface(48, 24, (u, v) =>
                {
                    float y = v * 13.5f;
                    float tie = 2.6f;
                    float gather = y > tie ? Mathf.Lerp(0.55f, 1f, Mathf.Clamp01((y - tie) / 6f)) : Mathf.Lerp(0.55f, 0.9f, Mathf.Clamp01((tie - y) / tie));
                    float inner = 7.3f + (1 - gather) * 2.6f;
                    float x = Mathf.Lerp(inner, 13.5f, u);
                    float fold = Mathf.Sin(u * Mathf.PI * 13f) * 0.22f * (0.6f + 0.4f * gather);
                    var p = new Vector3(side * x, y, z + fold + (1 - u) * 0.1f);
                    var n = new Vector3(-Mathf.Cos(u * Mathf.PI * 13f) * 0.6f * side, 0, -1).normalized;
                    return (p, n);
                }, Matrix4x4.identity, Color.white, Vector2.one);
            }
            // the valance: three swags across the top
            b.Surface(96, 8, (u, v) =>
            {
                float x = Mathf.Lerp(-13.5f, 13.5f, u);
                float swag = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 3f));
                float bottom = 9.3f - swag * 0.9f;
                float y = Mathf.Lerp(bottom, 13.5f, v);
                float fold = Mathf.Sin(u * Mathf.PI * 40f) * 0.06f * (1 - v);
                return (new Vector3(x, y, z - 0.2f + fold), Vector3.back);
            }, Matrix4x4.identity, Color.white, Vector2.one);
            Part("Drapes", transform, b, _drapeMat, Vector3.zero);
            // gold fringe under the swags and the tie-back tassels
            var g = new MeshBuilder();
            var pts = new Vector3[97];
            for (int i = 0; i <= 96; i++)
            {
                float u = i / 96f;
                float swag = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 3f));
                pts[i] = new Vector3(Mathf.Lerp(-13.5f, 13.5f, u), 9.3f - swag * 0.9f - 0.05f, z - 0.25f);
            }
            g.Tube(pts, 0.09f, 6, Matrix4x4.identity, Color.white);
            foreach (float side in new[] { -1f, 1f })
            {
                g.Torus(0.42f, 0.07f, 5, 18, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(side * 8.35f, 2.6f, z - 0.05f), Quaternion.Euler(0, 0, 90), new Vector3(1.6f, 1, 1)), Color.white);
                g.Sphere(0.16f, 8, 6, Matrix4x4.Translate(new Vector3(side * 7.8f, 2.35f, z - 0.3f)), Color.white, Vector3.one);
                g.Cone(0.16f, 0.55f, 8, Matrix4x4.Translate(new Vector3(side * 7.8f, 2.25f, z - 0.3f)), Color.white);
            }
            Part("Fringe", transform, g, gold, Vector3.zero);
        }

        // ---- chandeliers: gilt rings of candle flames over the house, a soft halo each

        Material _candleMat, _haloMat;
        readonly Transform[] _halos = new Transform[3];
        static readonly Vector3[] ChandelierAt = { new Vector3(0, 9.6f, 17.5f), new Vector3(-9.5f, 9.2f, 13.5f), new Vector3(9.5f, 9.2f, 13.5f) };

        void BuildChandeliers()
        {
            var gilt = Surf(L(1f, 0.76f, 0.34f), rim: 1.3f, pulse: 0.05f, lift: 0.18f, bands: 3, fog: 0.7f);
            var frame = new MeshBuilder();
            var flames = new MeshBuilder();
            for (int c = 0; c < 3; c++)
            {
                var at = ChandelierAt[c];
                float k = c == 0 ? 1.35f : 1f;
                frame.Box(Matrix4x4.TRS(at + new Vector3(0, 3.0f * k, 0), Quaternion.identity, new Vector3(0.05f, 6f * k, 0.05f)), Color.white);   // the chain
                frame.Torus(1.25f * k, 0.06f, 5, 40, Mathf.PI * 2, Matrix4x4.TRS(at, Quaternion.Euler(90, 0, 0), Vector3.one), Color.white);
                frame.Torus(0.8f * k, 0.05f, 5, 32, Mathf.PI * 2, Matrix4x4.TRS(at + new Vector3(0, 0.6f * k, 0), Quaternion.Euler(90, 0, 0), Vector3.one), Color.white);
                frame.Sphere(0.3f * k, 10, 8, Matrix4x4.Translate(at + new Vector3(0, -0.25f * k, 0)), Color.white, new Vector3(1, 1.4f, 1));
                frame.Cone(0.16f * k, 0.6f * k, 8, Matrix4x4.Translate(at + new Vector3(0, -0.55f * k, 0)), Color.white);
                for (int i = 0; i < 6; i++)
                {
                    float a = i / 6f * Mathf.PI * 2;
                    frame.Tube(new[] { at + new Vector3(0, 0.6f * k, 0), at + new Vector3(Mathf.Cos(a) * 0.9f * k, 0.15f * k, Mathf.Sin(a) * 0.9f * k), at + new Vector3(Mathf.Cos(a) * 1.25f * k, 0, Mathf.Sin(a) * 1.25f * k) }, 0.035f, 4, Matrix4x4.identity, Color.white);
                }
                for (int i = 0; i < 12; i++)
                {
                    float a = i / 12f * Mathf.PI * 2;
                    flames.Sphere(0.075f * k, 6, 4, Matrix4x4.Translate(at + new Vector3(Mathf.Cos(a) * 1.25f * k, 0.16f * k, Mathf.Sin(a) * 1.25f * k)), Color.white, new Vector3(1, 1.7f, 1));
                }
                for (int i = 0; i < 8; i++)
                {
                    float a = i / 8f * Mathf.PI * 2 + 0.2f;
                    flames.Sphere(0.06f * k, 6, 4, Matrix4x4.Translate(at + new Vector3(Mathf.Cos(a) * 0.8f * k, 0.74f * k, Mathf.Sin(a) * 0.8f * k)), Color.white, new Vector3(1, 1.7f, 1));
                }
            }
            Part("Chandeliers", transform, frame, gilt, Vector3.zero);
            _candleMat = AddMat(L(1f, 0.85f, 0.55f), 2.2f, 0.4f, 0.15f);
            Part("Candles", transform, flames, _candleMat, Vector3.zero);
            _haloMat = GlowCard(new Color(1f, 0.82f, 0.55f), 0.16f, 1.8f);
            var q = new MeshBuilder();
            q.Surface(1, 1, (u, v) => (new Vector3(u - 0.5f, v - 0.5f, 0), Vector3.back), Matrix4x4.identity, Color.white, Vector2.one);
            var qm = Own(q.ToMesh("Halo"));
            for (int c = 0; c < 3; c++)
            {
                var r = EnvKit.Renderer("ChandelierHalo" + c, transform, qm, _haloMat);
                r.transform.localPosition = ChandelierAt[c] + new Vector3(0, 0.2f, 0);
                r.transform.localScale = Vector3.one * (c == 0 ? 7.5f : 5.5f);
                _halos[c] = r.transform;
            }
        }

        // ---- the podium: velvet top, gilt bands, a glowing ring; the music stand beside it with a lamp on the score

        Material _podRingMat, _standLampMat;

        void BuildPodium()
        {
            var velvet = Surf(L(0.55f, 0.05f, 0.1f), rim: 1.4f, pulse: 0.04f, bands: 3, softness: 0.35f);
            var wood = Surf(L(0.22f, 0.08f, 0.06f), rim: 0.8f, pulse: 0.03f, bands: 3);
            var gilt = Surf(L(1f, 0.76f, 0.34f), rim: 1.2f, pulse: 0.06f, lift: 0.1f, bands: 3);
            var b = new MeshBuilder();
            b.Shell(PodiumR, PodiumR + 0.12f, PodiumH, 40, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(0, PodiumH / 2, 0)), Color.white, Vector2.one);
            Part("PodiumSide", transform, b, wood, PodiumAt, cast: false);
            b = new MeshBuilder();
            b.Disc(PodiumR, 40, Matrix4x4.Translate(new Vector3(0, PodiumH, 0)), Color.white);
            Part("PodiumTop", transform, b, velvet, PodiumAt);
            b = new MeshBuilder();
            b.Torus(PodiumR + 0.01f, 0.05f, 5, 48, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(0, PodiumH - 0.02f, 0), Quaternion.Euler(90, 0, 0), Vector3.one), Color.white);
            b.Torus(PodiumR + 0.12f, 0.06f, 5, 48, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(0, 0.05f, 0), Quaternion.Euler(90, 0, 0), Vector3.one), Color.white);
            b.Shell(PodiumR + 0.065f, PodiumR + 0.075f, 0.06f, 48, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(0, PodiumH * 0.55f, 0)), Color.white, Vector2.one);
            Part("PodiumGilt", transform, b, gilt, PodiumAt);
            _podRingMat = AddMat(L(1f, 0.55f, 0.4f), 0.9f, 0.3f, 0);
            b = new MeshBuilder();
            b.Torus(PodiumR + 0.32f, 0.035f, 4, 64, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(0, 0.012f, 0), Quaternion.Euler(90, 0, 0), new Vector3(1, 1, 0.3f)), Color.white);
            Part("PodiumRing", transform, b, _podRingMat, PodiumAt);

            // the stand: a post, a raked desk (its back to us), the score's lamp glowing over the top edge
            var stand = new MeshBuilder();
            var dark = L(0.07f, 0.05f, 0.06f);
            stand.Shell(0.03f, 0.045f, 1.15f, 8, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(0, 0.575f, 0)), dark, Vector2.one);
            stand.Box(Matrix4x4.TRS(new Vector3(0, 1.2f, 0), Quaternion.Euler(-28, 0, 0), new Vector3(0.95f, 0.62f, 0.04f)), dark);
            stand.Box(Matrix4x4.TRS(new Vector3(0, 0.9f, -0.12f), Quaternion.Euler(-28, 0, 0), new Vector3(0.95f, 0.05f, 0.12f)), L(1f, 0.74f, 0.3f));
            for (int i = 0; i < 3; i++)
            {
                float a = i / 3f * Mathf.PI * 2;
                stand.Box(Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * 0.18f, 0.05f, Mathf.Sin(a) * 0.18f), Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0), new Vector3(0.4f, 0.03f, 0.04f)), dark);
            }
            var standR = Part("MusicStand", transform, stand, Surf(Color.white, rim: 1.4f, pulse: 0.02f, bands: 3), new Vector3(-1.7f, 0, 2.9f), cast: true);
            standR.transform.localRotation = Quaternion.Euler(0, 200, 0);
            var lamp = new MeshBuilder();
            lamp.Sphere(0.07f, 8, 5, Matrix4x4.Translate(new Vector3(0, 1.58f, 0.05f)), Color.white, new Vector3(3.2f, 0.6f, 1));
            _standLampMat = AddMat(L(1f, 0.86f, 0.6f), 1.6f, 0.5f, 0);
            var lr = Part("StandLamp", transform, lamp, _standLampMat, new Vector3(-1.7f, 0, 2.9f));
            lr.transform.localRotation = Quaternion.Euler(0, 200, 0);
        }

        // ------------------------------------------------------------ the maestro

        Transform _mRoot, _mHips, _mTorso, _mHead, _mBaton, _mTip, _mBrowL, _mBrowR, _mEyes, _mStache, _mTailL, _mTailR;
        readonly Transform[] _mUpper = new Transform[2], _mFore = new Transform[2], _mHand = new Transform[2];
        Material _tipMat;
        TrailHandle _trail;
        const float UpperLen = 0.44f, ForeLen = 0.42f;
        static readonly Vector3 ShoulderR = new Vector3(-0.44f, 1.66f, 0.02f), ShoulderL = new Vector3(0.44f, 1.66f, 0.02f);

        void BuildMaestro()
        {
            var mat = Surf(Color.white, rim: 1.25f, pulse: 0.04f, lift: 0.02f, bands: 3, softness: 0.24f, fog: 0.5f);
            _mRoot = new GameObject("Maestro").transform;
            _mRoot.SetParent(transform, false);
            _mRoot.localPosition = PodiumAt + new Vector3(0, PodiumH, 0);
            _mRoot.localScale = Vector3.one * MaestroScale;

            var coat = L(0.05f, 0.045f, 0.075f);
            var satin = L(0.12f, 0.1f, 0.16f);
            var shirt = L(0.96f, 0.95f, 0.92f);
            var skin = L(1f, 0.8f, 0.66f);
            var hair = L(0.95f, 0.95f, 0.98f);
            var crimson = L(0.85f, 0.08f, 0.16f);
            var gold = L(1f, 0.76f, 0.3f);
            var shoe = L(0.03f, 0.03f, 0.04f);

            // legs (on the root: they stay planted)
            var legs = new MeshBuilder();
            foreach (float sx in new[] { -1f, 1f })
            {
                legs.Capsule(0.12f, 0.5f, 2, 10, Matrix4x4.Translate(new Vector3(sx * 0.17f, 0.42f, 0)), coat);
                legs.Sphere(0.13f, 10, 6, Matrix4x4.Translate(new Vector3(sx * 0.18f, 0.06f, -0.08f)), shoe, new Vector3(1, 0.55f, 1.6f));
            }
            Part("Legs", _mRoot, legs, mat, Vector3.zero, cast: true);

            _mHips = new GameObject("Hips").transform; _mHips.SetParent(_mRoot, false); _mHips.localPosition = new Vector3(0, 0.78f, 0);
            _mTorso = new GameObject("Torso").transform; _mTorso.SetParent(_mHips, false);

            // torso: a tailcoat bean from the hips to the shoulders (local to the hips)
            var body = new MeshBuilder();
            Lathe(body, 24, 14, v =>
            {
                float y = Mathf.Lerp(-0.12f, 1.02f, v);
                float r = 0.36f + 0.1f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(v * 1.1f)) - 0.12f * Mathf.Pow(Mathf.Max(0, v - 0.75f) / 0.25f, 2);
                return (y, Mathf.Max(0.02f, r));
            }, Matrix4x4.identity, coat);
            // the shirt front and the waistcoat V, lapels, bow tie, gold buttons
            int s0 = body.Count;
            body.Surface(6, 8, (u, v) =>
            {
                float y = Mathf.Lerp(0.42f, 0.98f, v);
                float halfW = Mathf.Lerp(0.02f, 0.2f, v);
                float x = Mathf.Lerp(-halfW, halfW, u);
                float rr = 0.43f + 0.02f * Mathf.Sin(Mathf.PI * v) - 0.08f * Mathf.Pow(Mathf.Max(0, v - 0.7f) / 0.3f, 2);
                float z = -Mathf.Sqrt(Mathf.Max(0.001f, rr * rr - x * x)) - 0.008f;
                return (new Vector3(x, y, z), new Vector3(x, 0.1f, z).normalized);
            }, Matrix4x4.identity, shirt, Vector2.one);
            foreach (float sx in new[] { -1f, 1f })
            {
                body.Box(Matrix4x4.TRS(new Vector3(sx * 0.17f, 0.66f, -0.4f), Quaternion.Euler(-8, sx * 18, sx * 24), new Vector3(0.09f, 0.62f, 0.03f)), satin);
                body.Sphere(0.04f, 6, 4, Matrix4x4.Translate(new Vector3(sx * 0.11f, 0.3f, -0.43f)), gold, Vector3.one);
            }
            body.Sphere(0.03f, 6, 4, Matrix4x4.Translate(new Vector3(0, 0.6f, -0.415f)), gold, Vector3.one);
            body.Sphere(0.03f, 6, 4, Matrix4x4.Translate(new Vector3(0, 0.46f, -0.425f)), gold, Vector3.one);
            foreach (float sx in new[] { -1f, 1f })
                body.Sphere(0.075f, 8, 5, Matrix4x4.TRS(new Vector3(sx * 0.075f, 0.93f, -0.39f), Quaternion.Euler(0, 0, sx * 10), Vector3.one), crimson, new Vector3(1.2f, 0.75f, 0.5f));
            body.Sphere(0.04f, 6, 4, Matrix4x4.Translate(new Vector3(0, 0.93f, -0.415f)), crimson * 0.8f, Vector3.one);
            // collar
            body.Torus(0.17f, 0.04f, 5, 18, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(0, 1.0f, -0.02f), Quaternion.Euler(90, 0, 0), Vector3.one), shirt);
            // epaulette-ish shoulder rounds so the arms read as attached
            foreach (float sx in new[] { -1f, 1f })
                body.Sphere(0.15f, 10, 6, Matrix4x4.Translate(new Vector3(sx * 0.4f, 0.88f, 0.02f)), coat, Vector3.one);
            Part("Coat", _mTorso, body, mat, Vector3.zero, cast: true);

            // coat tails: two hanging flaps behind, swinging with him
            var tail = new MeshBuilder();
            tail.Box(Matrix4x4.TRS(new Vector3(0, -0.32f, 0), Quaternion.identity, new Vector3(0.2f, 0.64f, 0.05f)), coat);
            var tailMesh = Own(tail.ToMesh("Tail"));
            _mTailL = new GameObject("TailL").transform; _mTailL.SetParent(_mTorso, false); _mTailL.localPosition = new Vector3(0.14f, 0.0f, 0.36f);
            _mTailR = new GameObject("TailR").transform; _mTailR.SetParent(_mTorso, false); _mTailR.localPosition = new Vector3(-0.14f, 0.0f, 0.36f);
            EnvKit.Renderer("TailMesh", _mTailL, tailMesh, mat).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            EnvKit.Renderer("TailMesh", _mTailR, tailMesh, mat).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            // head: big and round, a white mane, bushy brows and moustache, a rosy nose
            _mHead = new GameObject("Head").transform; _mHead.SetParent(_mTorso, false); _mHead.localPosition = new Vector3(0, 1.08f, -0.02f);
            var head = new MeshBuilder();
            head.Sphere(0.36f, 18, 12, Matrix4x4.Translate(new Vector3(0, 0.3f, 0)), skin, new Vector3(1, 1.02f, 0.96f));
            foreach (float sx in new[] { -1f, 1f })
            {
                head.Sphere(0.08f, 8, 6, Matrix4x4.Translate(new Vector3(sx * 0.355f, 0.27f, 0.02f)), skin * 0.92f, new Vector3(0.5f, 1, 0.8f));   // ears
                head.Sphere(0.07f, 8, 5, Matrix4x4.Translate(new Vector3(sx * 0.19f, 0.2f, -0.29f)), L(1f, 0.5f, 0.52f), new Vector3(1.3f, 0.7f, 0.4f));   // cheeks
            }
            head.Sphere(0.075f, 10, 7, Matrix4x4.Translate(new Vector3(0, 0.25f, -0.36f)), L(1f, 0.6f, 0.55f), Vector3.one);   // nose
            // the mane: tufts round the sides and back, a swept crest on top
            var rng = new FxRng(0x3a3e);
            for (int i = 0; i < 14; i++)
            {
                float a = Mathf.Lerp(-2.0f, 2.0f, i / 13f);
                float y = 0.36f + Mathf.Cos(a * 0.8f) * 0.12f + rng.Next() * 0.05f;
                var p = new Vector3(Mathf.Sin(a) * 0.36f, y, Mathf.Cos(a) * 0.3f + 0.04f);
                head.Sphere(0.15f + rng.Next() * 0.05f, 9, 6, Matrix4x4.Translate(p), hair * (0.92f + rng.Next() * 0.08f), new Vector3(1, 0.9f, 1));
            }
            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * 0.1f;
                head.Sphere(0.12f, 9, 6, Matrix4x4.TRS(new Vector3(x, 0.6f + 0.03f * (2 - Mathf.Abs(i - 2)), 0.02f - i * 0.01f), Quaternion.Euler(-20, 0, (i - 2) * 12), Vector3.one), hair, new Vector3(1, 1.25f, 1.3f));
            }
            foreach (float sx in new[] { -1f, 1f })
                head.Sphere(0.11f, 9, 6, Matrix4x4.TRS(new Vector3(sx * 0.42f, 0.42f, 0.05f), Quaternion.Euler(0, 0, sx * -30), Vector3.one), hair, new Vector3(1.2f, 0.8f, 1));   // the wild flyaways
            Part("HeadMesh", _mHead, head, mat, Vector3.zero, cast: true);

            // features that move: eyes (close in a hush), brows (frown on a miss), moustache (lifts in a smile)
            var eyes = new MeshBuilder();
            var ink = L(0.03f, 0.03f, 0.05f);
            foreach (float sx in new[] { -1f, 1f })
            {
                eyes.Sphere(0.045f, 8, 6, Matrix4x4.Translate(new Vector3(sx * 0.12f, 0, 0)), ink, new Vector3(0.85f, 1.2f, 0.5f));
                eyes.Sphere(0.014f, 6, 4, Matrix4x4.Translate(new Vector3(sx * 0.12f + 0.012f, 0.02f, -0.024f)), new Color(3, 3, 3), Vector3.one);
            }
            _mEyes = Part("Eyes", _mHead, eyes, mat, new Vector3(0, 0.34f, -0.335f)).transform;
            var brow = new MeshBuilder();
            brow.Sphere(0.07f, 8, 5, Matrix4x4.identity, hair, new Vector3(1.4f, 0.5f, 0.6f));
            var browMesh = Own(brow.ToMesh("Brow"));
            _mBrowL = EnvKit.Renderer("BrowL", _mHead, browMesh, mat).transform; _mBrowL.localPosition = new Vector3(0.13f, 0.45f, -0.32f);
            _mBrowR = EnvKit.Renderer("BrowR", _mHead, browMesh, mat).transform; _mBrowR.localPosition = new Vector3(-0.13f, 0.45f, -0.32f);
            var st = new MeshBuilder();
            foreach (float sx in new[] { -1f, 1f })
                st.Sphere(0.09f, 9, 6, Matrix4x4.TRS(new Vector3(sx * 0.09f, 0, 0), Quaternion.Euler(0, 0, sx * -18), Vector3.one), hair, new Vector3(1.5f, 0.6f, 0.7f));
            _mStache = Part("Moustache", _mHead, st, mat, new Vector3(0, 0.155f, -0.35f)).transform;

            // arms: upper and fore segments built along +y, placed by the IK each frame; white gloves
            var upper = new MeshBuilder();
            upper.Capsule(0.1f, UpperLen, 2, 10, Matrix4x4.Translate(new Vector3(0, UpperLen / 2, 0)), coat);
            var fore = new MeshBuilder();
            fore.Capsule(0.085f, ForeLen, 2, 10, Matrix4x4.Translate(new Vector3(0, ForeLen / 2, 0)), coat);
            fore.Shell(0.09f, 0.095f, 0.06f, 12, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(0, ForeLen - 0.02f, 0)), shirt, Vector2.one);
            var hand = new MeshBuilder();
            hand.Sphere(0.1f, 10, 7, Matrix4x4.identity, shirt, new Vector3(1, 1.1f, 0.85f));
            hand.Sphere(0.04f, 6, 4, Matrix4x4.Translate(new Vector3(0.07f, 0.04f, -0.04f)), shirt, Vector3.one);   // a thumb
            var um = Own(upper.ToMesh("UpperArm")); var fm = Own(fore.ToMesh("ForeArm")); var hm = Own(hand.ToMesh("Hand"));
            for (int s = 0; s < 2; s++)
            {
                _mUpper[s] = EnvKit.Renderer("Upper" + s, _mRoot, um, mat).transform;
                _mFore[s] = EnvKit.Renderer("Fore" + s, _mRoot, fm, mat).transform;
                _mHand[s] = EnvKit.Renderer("Hand" + s, _mRoot, hm, mat).transform;
                foreach (var t in new[] { _mUpper[s], _mFore[s], _mHand[s] }) t.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            // the baton: ivory shaft with a cork grip, along +z of its own transform; a glowing tip
            _mBaton = new GameObject("Baton").transform; _mBaton.SetParent(_mRoot, false);
            var bat = new MeshBuilder();
            bat.Shell(0.012f, 0.026f, 0.62f, 8, 0, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(0, 0, 0.36f), Quaternion.Euler(90, 0, 0), Vector3.one), L(0.98f, 0.95f, 0.86f), Vector2.one);
            bat.Sphere(0.045f, 8, 6, Matrix4x4.Translate(new Vector3(0, 0, 0.04f)), L(0.7f, 0.5f, 0.32f), new Vector3(1, 1, 1.5f));
            Part("BatonMesh", _mBaton, bat, mat, Vector3.zero, cast: true);
            _tipMat = AddMat(L(1f, 0.9f, 0.6f), 1.6f, 0.5f, 0);
            var tip = new MeshBuilder();
            tip.Sphere(0.04f, 8, 6, Matrix4x4.identity, Color.white, Vector3.one);
            _mTip = Part("BatonTip", _mBaton, tip, _tipMat, new Vector3(0, 0, 0.68f)).transform;

            Look.Blob(_mRoot, 0.8f, 0.5f, PodiumH, 0);
        }

        // ------------------------------------------------------------ the timpani section

        sealed class Drummer
        {
            public Transform Root, Body, Head, ArmL, ArmR, Mouth;
            public Vector3 Drum;            // the head's centre (world, local to the game)
            public float Strike, Side, Joy, Sad, Flash, Bob;
            public Color FlashColor;
            public int Cursor;              // the next beat of the excerpt this drummer may strike (others)
            public int LastSlot = -1;
            public bool Player;
        }

        readonly Drummer[] _drummers = new Drummer[4];
        MeshBatch _heads;
        Transform _youMark;
        Material _youMat;
        static readonly Vector3[] SeatAt = { new Vector3(3.55f, 0, 0.55f), new Vector3(-3.55f, 0, 0.55f), new Vector3(-6.0f, 0, 2.7f), new Vector3(6.0f, 0, 2.7f) };

        void BuildTimpani()
        {
            var mat = Surf(Color.white, rim: 1.15f, pulse: 0.04f, lift: 0.02f, bands: 3, softness: 0.26f, fog: 0.6f);
            var copper = Surf(L(0.95f, 0.5f, 0.28f), rim: 1.6f, pulse: 0.04f, lift: 0.04f, bands: 4, softness: 0.18f, fog: 0.6f);
            // the kettle: a copper bowl, a gilt rim, three legs and a base ring
            var kettle = new MeshBuilder();
            Lathe(kettle, 28, 10, v =>
            {
                float a = v * Mathf.PI * 0.5f;   // a bowl: the round bottom up to the widest point at the rim
                return (0.82f - Mathf.Cos(a) * 0.6f, 0.02f + Mathf.Sin(a) * 0.6f);
            }, Matrix4x4.identity, Color.white);
            var rimB = new MeshBuilder();
            rimB.Torus(0.62f, 0.035f, 5, 32, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(0, 0.83f, 0), Quaternion.Euler(90, 0, 0), Vector3.one), L(1f, 0.76f, 0.3f));
            for (int i = 0; i < 3; i++)
            {
                float a = i / 3f * Mathf.PI * 2 + 0.5f;
                rimB.Tube(new[] { new Vector3(Mathf.Cos(a) * 0.45f, 0.4f, Mathf.Sin(a) * 0.45f), new Vector3(Mathf.Cos(a) * 0.55f, 0.1f, Mathf.Sin(a) * 0.55f), new Vector3(Mathf.Cos(a) * 0.6f, 0f, Mathf.Sin(a) * 0.6f) }, 0.03f, 5, Matrix4x4.identity, L(0.12f, 0.1f, 0.12f));
            }
            rimB.Torus(0.55f, 0.03f, 4, 24, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(0, 0.02f, 0), Quaternion.Euler(90, 0, 0), Vector3.one), L(0.12f, 0.1f, 0.12f));
            // the heads: one batch so each can flash in its call's colour
            var headTpl = new MeshBuilder();
            headTpl.Disc(0.6f, 32, Matrix4x4.identity, Color.white);
            var skinMat = Surf(Color.white, rim: 0.3f, pulse: 0.05f, lift: 0.12f, bands: 2, softness: 0.4f, fog: 0.6f);
            _heads = new MeshBatch("DrumHeads", transform, headTpl, 4, skinMat);
            _heads.Renderer.receiveShadows = true;

            var km = Own(kettle.ToMesh("Kettle")); var rm = Own(rimB.ToMesh("KettleRim"));
            for (int s = 0; s < 4; s++)
            {
                var d = new Drummer { Player = s == 0 };
                var seat = SeatAt[s];
                // the drum sits between the drummer and the maestro
                var toM = (PodiumAt - seat); toM.y = 0; toM.Normalize();
                var drumPos = seat + toM * 0.95f;
                var kr = EnvKit.Renderer("Kettle" + s, transform, km, copper); kr.transform.localPosition = drumPos; kr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                var rr = EnvKit.Renderer("KettleRim" + s, transform, rm, mat); rr.transform.localPosition = drumPos;
                d.Drum = drumPos + new Vector3(0, 0.84f, 0);
                // the drummer faces the maestro, turned partly toward us (stage blocking: we see their faces)
                var toCam = new Vector3(0, 0, -1);
                var face = Vector3.Slerp(toM, toCam, 0.42f).normalized;
                d.Root = new GameObject("Drummer" + s).transform;
                d.Root.SetParent(transform, false);
                d.Root.localPosition = seat;
                d.Root.localRotation = Quaternion.LookRotation(-face, Vector3.up);   // the model faces its local -z
                BuildDrummer(d, mat, s);
                _drummers[s] = d;
                Look.Blob(d.Root, 0.55f, 0.45f, 0, 0);
            }
            HeadsFlush();

            // a small marker over the player's drummer
            _youMat = AddMat(L(1f, 0.85f, 0.24f), 1.4f, 0.2f, 0);
            var m = new MeshBuilder();
            m.Cone(0.16f, 0.3f, 4, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(180, 45, 0), new Vector3(1, 1, 0.5f)), Color.white);
            _youMark = Part("YouMark", transform, m, _youMat, SeatAt[0] + new Vector3(0, 2.3f, 0)).transform;
        }

        void BuildDrummer(Drummer d, Material mat, int s)
        {
            var jacket = SeatColorFor(s).linear * 0.85f;
            var skin = (s % 2 == 0 ? new Color(1f, 0.82f, 0.68f) : new Color(0.82f, 0.6f, 0.45f)).linear;
            var dark = L(0.08f, 0.07f, 0.1f);
            var white = L(0.96f, 0.96f, 0.94f);
            // stool
            var stool = new MeshBuilder();
            stool.Shell(0.26f, 0.26f, 0.08f, 16, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(0, 0.62f, 0.15f)), dark, Vector2.one);
            stool.Disc(0.26f, 16, Matrix4x4.Translate(new Vector3(0, 0.66f, 0.15f)), dark);
            stool.Shell(0.04f, 0.05f, 0.6f, 6, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(0, 0.3f, 0.15f)), dark, Vector2.one);
            Part("Stool", d.Root, stool, mat, Vector3.zero);
            d.Body = new GameObject("Body").transform; d.Body.SetParent(d.Root, false); d.Body.localPosition = new Vector3(0, 0.66f, 0.12f);
            var b = new MeshBuilder();
            Lathe(b, 18, 10, v =>
            {
                float y = v * 0.86f;
                float r = 0.3f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Max(0.02f, v * 0.92f + 0.06f)), 0.55f);
                return (y, r);
            }, Matrix4x4.identity, jacket);
            b.Torus(0.17f, 0.04f, 5, 16, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(0, 0.78f, -0.02f), Quaternion.Euler(90, 0, 0), Vector3.one), white);   // collar
            b.Sphere(0.05f, 8, 5, Matrix4x4.Translate(new Vector3(0, 0.74f, -0.24f)), dark, new Vector3(1.6f, 0.7f, 0.6f));   // bow tie
            foreach (float sx in new[] { -1f, 1f })
                b.Capsule(0.08f, 0.22f, 2, 8, Matrix4x4.TRS(new Vector3(sx * 0.12f, 0.02f, -0.22f), Quaternion.Euler(80, 0, 0), Vector3.one), dark);   // thighs
            Part("BodyMesh", d.Body, b, mat, Vector3.zero, cast: true);
            d.Head = new GameObject("Head").transform; d.Head.SetParent(d.Body, false); d.Head.localPosition = new Vector3(0, 0.82f, 0);
            var h = new MeshBuilder();
            h.Sphere(0.26f, 14, 10, Matrix4x4.Translate(new Vector3(0, 0.22f, 0)), skin, Vector3.one);
            var hairC = s % 2 == 0 ? L(0.35f, 0.2f, 0.1f) : L(0.08f, 0.06f, 0.06f);
            h.Sphere(0.27f, 14, 8, Matrix4x4.TRS(new Vector3(0, 0.3f, 0.04f), Quaternion.Euler(-15, 0, 0), Vector3.one), hairC, new Vector3(1.04f, 0.72f, 1.02f));
            var ink = L(0.03f, 0.03f, 0.05f);
            foreach (float sx in new[] { -1f, 1f })
            {
                h.Sphere(0.035f, 8, 6, Matrix4x4.Translate(new Vector3(sx * 0.09f, 0.24f, -0.235f)), ink, new Vector3(0.85f, 1.25f, 0.5f));
                h.Sphere(0.011f, 6, 4, Matrix4x4.Translate(new Vector3(sx * 0.09f + 0.01f, 0.26f, -0.255f)), new Color(3, 3, 3), Vector3.one);
                h.Sphere(0.04f, 8, 5, Matrix4x4.Translate(new Vector3(sx * 0.15f, 0.17f, -0.2f)), L(1f, 0.45f, 0.5f), new Vector3(1.3f, 0.7f, 0.4f));
            }
            Part("HeadMesh", d.Head, h, mat, Vector3.zero, cast: true);
            var mo = new MeshBuilder();
            mo.Sphere(0.035f, 8, 5, Matrix4x4.identity, L(0.35f, 0.05f, 0.1f), new Vector3(1.2f, 1, 0.5f));
            d.Mouth = Part("Mouth", d.Head, mo, mat, new Vector3(0, 0.13f, -0.245f)).transform;
            // arms with mallets, pivoting at the shoulders; built pointing down-forward
            var arm = new MeshBuilder();
            arm.Capsule(0.065f, 0.36f, 2, 8, Matrix4x4.Translate(new Vector3(0, -0.2f, 0)), jacket);
            arm.Sphere(0.07f, 8, 6, Matrix4x4.Translate(new Vector3(0, -0.42f, 0)), skin, Vector3.one);
            arm.Shell(0.012f, 0.014f, 0.5f, 6, 0, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(0, -0.44f, -0.25f), Quaternion.Euler(90, 0, 0), Vector3.one), L(0.75f, 0.6f, 0.4f), Vector2.one);
            arm.Sphere(0.06f, 8, 6, Matrix4x4.Translate(new Vector3(0, -0.44f, -0.5f)), white, Vector3.one);
            var am = Own(arm.ToMesh("DrummerArm"));
            d.ArmL = new GameObject("ArmL").transform; d.ArmL.SetParent(d.Body, false); d.ArmL.localPosition = new Vector3(0.25f, 0.68f, -0.02f);
            d.ArmR = new GameObject("ArmR").transform; d.ArmR.SetParent(d.Body, false); d.ArmR.localPosition = new Vector3(-0.25f, 0.68f, -0.02f);
            EnvKit.Renderer("Arm", d.ArmL, am, mat).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            EnvKit.Renderer("Arm", d.ArmR, am, mat).shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            d.Root.localScale = Vector3.one * 1.2f;
        }

        Color SeatColorFor(int seat)
        {
            if (_ids != null && seat < _ids.Count) return SeatColors[_ids[seat] % SeatColors.Length];
            if (_ids == null && seat > 0) return seat == 1 ? new Color(0.62f, 0.42f, 0.86f) : seat == 2 ? new Color(0.36f, 0.6f, 0.9f) : new Color(0.84f, 0.4f, 0.46f);   // the section, in plain colours
            return SeatColors[seat % SeatColors.Length];
        }

        void HeadsFlush()
        {
            for (int s = 0; s < 4; s++)
            {
                var d = _drummers[s];
                if (d == null) { _heads.Matrix[s] = Matrix4x4.TRS(new Vector3(0, -99, 0), Quaternion.identity, Vector3.zero); continue; }
                float f = d.Flash;
                float dip = f * 0.03f;
                _heads.Matrix[s] = Matrix4x4.TRS(d.Drum + new Vector3(0, -dip, 0), Quaternion.identity, Vector3.one);
                var baseC = new Color(0.92f, 0.88f, 0.8f).linear;
                var fc = d.FlashColor.linear * 2.2f;
                _heads.Tint[s] = Color.Lerp(baseC, fc, Mathf.Clamp01(f));
            }
            _heads.Flush();
        }

        // ------------------------------------------------------------ per-excerpt data (Begin: the slots are known)

        // per slot: is beat i a fermata, does a jump land on it, ramp direction at it (-1, 0, +1)
        bool[][] _fermata;
        int[][] _rampDir;
        bool[][] _jumpAt;

        void BeginStage()
        {
            int n = _round.Slots.Count;
            _fermata = new bool[n][]; _rampDir = new int[n][]; _jumpAt = new bool[n][];
            for (int k = 0; k < n; k++)
            {
                var s = _round.Slots[k];
                int bc = s.Curve.BeatCount;
                _fermata[k] = new bool[bc + 1]; _rampDir[k] = new int[bc + 1]; _jumpAt[k] = new bool[bc + 1];
                double at = 0;
                TempoSegment prev = null;
                foreach (var g in s.Curve.Segments)
                {
                    int i0 = (int)System.Math.Round(at), i1 = (int)System.Math.Round(at + g.Beats);
                    for (int i = i0; i < i1 && i < bc; i++)
                    {
                        if (g.Kind == TempoKind.Fermata) _fermata[k][i] = true;
                        if (g.Kind == TempoKind.Ramp) _rampDir[k][i] = g.Bpm1 > g.Bpm0 + 0.5 ? 1 : g.Bpm1 < g.Bpm0 - 0.5 ? -1 : 0;
                    }
                    if (g.Kind == TempoKind.Jump && prev != null && i0 < bc) _jumpAt[k][i0] = true;
                    if (g.Kind != TempoKind.Fermata) prev = g;
                    at += g.Beats;
                }
            }
            _trail = Look.Fx.Trail(new Color(1f, 0.88f, 0.55f), 0.07f);
            Frame(true, 0);
            int tris = 0, draws = 0;
            foreach (var f in GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = f.GetComponent<MeshRenderer>();
                if (f.sharedMesh == null || mr == null || !mr.enabled || !f.gameObject.activeInHierarchy) continue;
                tris += f.sharedMesh.triangles.Length / 3; draws++;
            }
            Debug.Log($"[FF] stage tris={tris} draws={draws}");
        }

        // ------------------------------------------------------------ events

        float _hush, _joy, _frown, _shake, _finale, _time, _strikeGlow, _lean, _crowdBeat, _lastDrive;
        int _lastBeatWhole = int.MinValue, _comboHyped;
        string _mood;

        void StagePressed()
        {
            var d = _drummers[0];
            d.Strike = 1;
            d.Side = -d.Side == 0 ? 1 : -d.Side;
        }

        void StageTapped(TempoBeat b)
        {
            var d = _drummers[0];
            var c = b.Call == TapCall.On ? Hex(0xffd93d) : b.Call == TapCall.Early ? Sky : Orange;
            d.Flash = 1; d.FlashColor = c;
            bool reduce = Settings.ReduceMotion;
            if (b.Call == TapCall.On)
            {
                Look.Fx.Ring(d.Drum + new Vector3(0, 0.02f, 0), new Color(1f, 0.85f, 0.35f), 0.4f, 0.4f, 1.35f, 0.12f, 0.02f, 0.02f, 0.9f, Vector3.up);
                if (!reduce) Look.Fx.Burst(d.Drum + new Vector3(0, 0.1f, 0), 6, new Color(1f, 0.88f, 0.45f), 2.6f, 0.07f, 0.35f, -6f, Vector3.up, 0.6f);
                d.Joy = Mathf.Min(1, d.Joy + 0.3f);
            }
            else Look.Fx.Ring(d.Drum + new Vector3(0, 0.02f, 0), c, 0.35f, 0.4f, 1.1f, 0.1f, 0.02f, 0.02f, 0.7f, Vector3.up);
            int combo = _round.Combo;
            if (combo >= 16 && combo % 16 == 0 && combo != _comboHyped) { _comboHyped = combo; _env.Crowd.Hype(0.6f); _strikeGlow = 1; }
        }

        void StageMissed(TempoBeat b)
        {
            var d = _drummers[0];
            d.Sad = 1; d.Flash = 0.8f; d.FlashColor = Coral;
            _frown = 1;
            _env.Crowd.Deflate(0.5f);
        }

        void StageLine(TempoLine line)
        {
            if (line.Misses == 0 && line.Top == line.Count) { _env.Crowd.Wave(0.8f); _joy = 1; }
        }

        void StageHeart(int delta, int left)
        {
            if (delta < 0)
            {
                _frown = 1; _shake = 1;
                if (!Settings.ReduceMotion) { Look.Rig.Shake(0.08f); Look.Rig.DoFlash(0.12f, new Color(1f, 0.2f, 0.25f)); }
            }
            else { _joy = 1; _env.Crowd.Cheer(0.8f); }
        }

        void StageExcerpt(TempoSlot slot)
        {
            _env.Spotlights?.Chase(0.6f, 0.12f);
            _comboHyped = 0;
        }

        void StageFinale()
        {
            _finale = 0.001f;
            _env.Crowd.Cheer(1.4f);
            _env.Crowd.SetEnergy(0.9f);
            _env.Spotlights?.Chase(1f, 0.1f);
            if (!Settings.ReduceMotion) Look.Fx.Confetti(new Vector3(0, 9, 9), 70, new[] { new Color(1f, 0.85f, 0.35f), new Color(1f, 0.4f, 0.45f), Color.white }, 4f, 0.2f, 0.16f, 3.2f);
        }

        // ------------------------------------------------------------ per frame

        void UpdateStage(float dt)
        {
            if (_mRoot == null) return;
            _time += dt;
            bool reduce = Settings.ReduceMotion;
            bool live = _playing && !_round.Done;
            bool hidden = PulseHidden && live;
            _hush = Damp(_hush, hidden ? 1 : 0, hidden ? 6f : 9f, dt);
            if (_finale > 0) _finale += dt;

            // the light: level, last heart, hush
            string mood = MoodPalette();
            if (mood != _mood)
            {
                bool level = _mood == null || !mood.StartsWith(_mood.Substring(0, Mathf.Min(_mood.Length, 18)));
                float dur = _mood == null ? 0.01f : mood.EndsWith("-hush") ? 0.55f : (_mood.EndsWith("-hush") ? 0.35f : level ? 1.8f : 1.1f);
                Look.SetPalette(mood, dur);
                _mood = mood;
            }

            // the beat the hall keeps: only while the baton shows
            double beat = Beat;
            int whole = (int)System.Math.Floor(beat);
            if (live && !hidden && beat >= -LeadBeats - 0.01)
            {
                Look.TickBeat(beat < 0 ? beat + 1000 : beat);   // the count-in nudges too (the kit treats a negative beat as "no beat")
                float p = (float)(beat - whole);
                Look.BeatPulse = Mathf.Pow(1 - p, 3) * 0.45f;
                _crowdBeat = (float)beat;
            }
            else
            {
                Look.BeatPulse = 0;
                _crowdBeat += dt * (hidden ? 0.12f : 0.9f);   // a slow, unmusical sway: no pulse to read
                Look.DriveEnvBeat(_crowdBeat);
            }
            _lastBeatWhole = whole;

            // crowd energy: the combo warms the house; a hush hushes it
            float energy = _finale > 0 ? 0.9f : hidden ? 0.02f : 0.18f + 0.45f * Mathf.Clamp01(_round.Combo / 32f);
            _env.Crowd.SetEnergy(energy);

            UpdateMaestro(dt, hidden);
            UpdateDrummers(dt, hidden);
            UpdateLights(dt, hidden);
            Frame(false, dt);
        }

        // ---- the maestro's conducting

        /// The ictus point (root-local, before the beat plane offset) of a beat at bar position `pos` of `n`.
        static Vector2 Ictus(int pos, int n)
        {
            // x: + is his left (inward for the right hand), - his right (outward). y: up.
            if (n == 2) return pos == 0 ? new Vector2(0, 0) : new Vector2(-0.12f, 0.1f);
            if (n == 3) return pos == 0 ? new Vector2(0, 0) : pos == 1 ? new Vector2(-0.5f, 0.05f) : new Vector2(-0.14f, 0.13f);
            if (n == 4) return pos == 0 ? new Vector2(0, 0) : pos == 1 ? new Vector2(0.36f, 0.05f) : pos == 2 ? new Vector2(-0.5f, 0.05f) : new Vector2(-0.14f, 0.13f);
            if (n == 6) return pos == 0 ? new Vector2(0, 0) : pos <= 2 ? new Vector2(0.18f * pos, 0.05f) : pos <= 4 ? new Vector2(-0.25f * (pos - 2), 0.05f) : new Vector2(-0.14f, 0.13f);
            return pos == 0 ? new Vector2(0, 0) : pos == n - 1 ? new Vector2(-0.14f, 0.13f) : new Vector2(pos % 2 == 1 ? 0.3f : -0.45f, 0.05f);
        }

        static readonly Vector3 PlaneR = new Vector3(-0.3f, 1.22f, -0.58f);
        Vector3 _handR, _handL, _batonDir = new Vector3(0, 0.3f, -1);
        bool _batonShown = true;

        void UpdateMaestro(float dt, bool hidden)
        {
            bool reduce = Settings.ReduceMotion;
            var s = Slot;
            double beat = Beat;
            int i = (int)System.Math.Floor(beat);
            float u = (float)(beat - i);
            bool live = _playing && !_round.Done && s != null;
            float len = s != null ? (float)s.Curve.LengthOf(Mathf.Clamp(i, 0, s.Curve.BeatCount - 1)) : 0.6f;

            Vector2 a = Vector2.zero, bpt = Vector2.zero;
            bool fermata = false;
            int rampDir = 0;
            bool waiting = !live || beat < -LeadBeats - 0.02;
            if (live && s != null)
            {
                int k = s.Index;
                if (i >= 0 && i < s.Beats.Count)
                {
                    var tb = s.Beats[i];
                    a = Ictus(tb.BarPosition, tb.BeatsPerBar);
                    fermata = _fermata[k][i];
                    rampDir = _rampDir[k][i];
                }
                else if (i < 0) a = Ictus(((i % 2) + 2) % 2 == 0 ? 0 : 1, 2);   // the count-in: down, up
                else a = Vector2.zero;
                int j = i + 1;
                if (j >= 0 && j < s.Beats.Count) bpt = Ictus(s.Beats[j].BarPosition, s.Beats[j].BeatsPerBar);
                else if (j < 0) bpt = Ictus(((j % 2) + 2) % 2 == 0 ? 0 : 1, 2);
                else bpt = Vector2.zero;
            }

            // the stroke: rebound up out of ictus a, float, fall (accelerating) into ictus b
            float H = Mathf.Clamp(0.2f + 0.36f * (len - 0.4f), 0.18f, 0.48f);
            Vector2 pos;
            float ictusFlash;
            if (waiting)
            {
                // ready: baton raised, a breath before the downbeat
                pos = new Vector2(-0.05f, 0.42f + Mathf.Sin(_time * 1.6f) * 0.015f);
                ictusFlash = 0;
            }
            else if (fermata)
            {
                // the hold: up to the top quickly, held there (a slow small circle), then the cut-off falls into b
                float rise = Mathf.SmoothStep(0, 1, Mathf.Clamp01(u / 0.16f));
                float fall = Mathf.Clamp01((u - 0.84f) / 0.16f);
                var top = new Vector2(a.x - 0.08f + Mathf.Sin(u * 7f) * 0.025f, a.y + 0.62f + Mathf.Cos(u * 7f) * 0.02f);
                pos = Vector2.Lerp(Vector2.Lerp(a, top, rise), bpt, fall * fall);
                ictusFlash = Mathf.Exp(-u * 30f);
            }
            else
            {
                float ex = Mathf.SmoothStep(0, 1, Mathf.Clamp01((u - 0.1f) / 0.75f));
                float bounce = Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.62f));
                pos = new Vector2(Mathf.Lerp(a.x, bpt.x, ex), Mathf.Lerp(a.y, bpt.y, u) + H * bounce);
                ictusFlash = Mathf.Exp(-u * 14f);
            }
            var handTarget = PlaneR + new Vector3(pos.x, pos.y, -0.06f * (1 - Mathf.Abs(Mathf.Sin(Mathf.PI * u))));
            // the left hand mirrors at half size; in a fermata it opens high (hold), in a ramp it beckons on (faster) or calms (slower)
            var mirror = new Vector3(-PlaneR.x + 0.06f - pos.x * 0.45f, PlaneR.y + 0.08f + pos.y * 0.5f, PlaneR.z + 0.08f);
            Vector3 leftTarget = mirror;
            if (fermata && live) leftTarget = new Vector3(0.48f, 2.0f, -0.42f);
            else if (rampDir < 0 && live) leftTarget = new Vector3(0.36f, 1.42f + Mathf.Sin(_time * 2.2f) * 0.03f, -0.58f);   // palm down: calm, slower
            if (waiting) leftTarget = new Vector3(0.32f, 1.25f, -0.42f);

            // the hush: baton away, right hand on the heart, left hand cupped to the ear, eyes shut
            var restR = new Vector3(-0.12f, 1.42f, -0.48f);
            var earL = new Vector3(0.44f, 2.12f, -0.12f);
            float hh = _hush;
            handTarget = Vector3.Lerp(handTarget, restR, hh);
            leftTarget = Vector3.Lerp(leftTarget, earL, hh);

            // the finale: arms wide, then a deep bow
            float bow = 0;
            if (_finale > 0)
            {
                float f = _finale;
                float wide = Mathf.SmoothStep(0, 1, Mathf.Clamp01(f / 0.6f));
                handTarget = Vector3.Lerp(handTarget, new Vector3(-0.95f, 1.85f, -0.2f), wide);
                leftTarget = Vector3.Lerp(leftTarget, new Vector3(0.95f, 1.85f, -0.2f), wide);
                bow = Mathf.SmoothStep(0, 1, Mathf.Clamp01((f - 0.9f) / 0.5f)) * (1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((f - 2.4f) / 0.6f)));
                if (reduce) bow *= 0.5f;
            }

            float lam = waiting ? 6 : 40;
            _handR = Damp(_handR == Vector3.zero ? handTarget : _handR, handTarget, lam, dt);
            _handL = Damp(_handL == Vector3.zero ? leftTarget : _handL, leftTarget, 10, dt);
            if (!waiting && hh < 0.02f && _finale <= 0) _handR = handTarget;   // the stroke itself is exact: it IS the beat

            // body: a knee dip on each ictus, a lean with the tempo (forward when it presses on, back when it broadens)
            float dip = live && !waiting ? ictusFlash * 0.035f * (1 - hh) : 0;
            float leanGoal = rampDir * 5f + (fermata ? -4 : 0);
            _lean = Damp(_lean, leanGoal * (1 - hh), 3, dt);
            float sway = Mathf.Sin(_time * 1.3f) * 3f * hh;
            _mHips.localPosition = new Vector3(0, 0.78f - dip, 0);
            _mTorso.localRotation = Quaternion.Euler(_lean + bow * 48f, 0, sway);
            float headNod = live && !waiting ? ictusFlash * 6f * (1 - hh) : 0;
            _shake = Mathf.Max(0, _shake - dt * 1.6f);
            float headShake = reduce ? 0 : Mathf.Sin(_time * 22f) * 7f * _shake;
            _mHead.localRotation = Quaternion.Euler(headNod - hh * 6f + bow * 10f, headShake, -hh * 14f);
            // tails swing a little with the stroke
            float tsw = (live ? Mathf.Sin(Mathf.PI * u) * 6f : 0) * (1 - hh);
            _mTailL.localRotation = Quaternion.Euler(8 + tsw, 0, 4); _mTailR.localRotation = Quaternion.Euler(8 + tsw, 0, -4);

            // face: eyes close in the hush, brows frown on a miss, lift in joy; the moustache smiles
            _frown = Mathf.Max(0, _frown - dt * 0.9f);
            _joy = Mathf.Max(0, _joy - dt * 0.7f);
            float joy = Mathf.Max(_joy, _finale > 0 ? 1 : 0);
            _mEyes.localScale = new Vector3(1, Mathf.Lerp(1, 0.12f, Mathf.Max(hh, bow * 0.8f)) * (1 - joy * 0.35f), 1);
            float browY = 0.45f + joy * 0.03f - _frown * 0.025f;
            _mBrowL.localPosition = new Vector3(0.13f, browY, -0.32f); _mBrowR.localPosition = new Vector3(-0.13f, browY, -0.32f);
            _mBrowL.localRotation = Quaternion.Euler(0, 0, -_frown * 22f + joy * 8f); _mBrowR.localRotation = Quaternion.Euler(0, 0, _frown * 22f - joy * 8f);
            _mStache.localScale = new Vector3(1 + joy * 0.12f, 1 - joy * 0.1f, 1);
            _mStache.localRotation = Quaternion.Euler(0, 0, 0);

            // arms (IK in root space; shoulders follow the torso)
            var shR = RootPoint(_mTorso, ShoulderR - new Vector3(0, 0.78f, 0));
            var shL = RootPoint(_mTorso, ShoulderL - new Vector3(0, 0.78f, 0));
            var elbowR = TwoBone(shR, _handR, new Vector3(-0.9f, -0.4f, 0.4f), UpperLen, ForeLen, out var hr);
            var elbowL = TwoBone(shL, _handL, new Vector3(0.9f, -0.4f, 0.4f), UpperLen, ForeLen, out var hl);
            PlaceSeg(_mUpper[0], shR, elbowR); PlaceSeg(_mFore[0], elbowR, hr);
            PlaceSeg(_mUpper[1], shL, elbowL); PlaceSeg(_mFore[1], elbowL, hl);
            _mHand[0].localPosition = hr; _mHand[1].localPosition = hl;
            _mHand[0].localRotation = Quaternion.LookRotation(hr - elbowR);
            _mHand[1].localRotation = Quaternion.LookRotation(hl - elbowL) * Quaternion.Euler(0, 0, fermata ? 90 : 0);

            // the baton: out of the right hand along the forearm, tipped up and toward us; it whips a little ahead of the hand
            var fdir = (hr - elbowR).normalized;
            var want = (fdir + new Vector3(0.15f, 0.55f, -0.65f)).normalized;
            if (!waiting && hh < 0.5f)
            {
                float whip = Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.62f));
                want = (want + new Vector3(0, -0.25f + whip * 0.5f, 0)).normalized;
            }
            _batonDir = Vector3.Slerp(_batonDir, want, 1 - Mathf.Exp(-30 * dt));
            _mBaton.localPosition = hr;
            _mBaton.localRotation = Quaternion.LookRotation(_batonDir, Vector3.up);
            // hidden: the baton is gone (tucked away), not merely dimmed
            bool show = hh < 0.35f && !(live && PulseHidden);
            if (_finale > 0.6f) show = true;
            if (show != _batonShown) { _batonShown = show; _mBaton.gameObject.SetActive(show); }
            float flash = show && live && !waiting ? ictusFlash : 0;
            _tipMat.SetFloat("_Intensity", 1.2f + flash * 3.2f + _strikeGlow * 0.6f);
            _strikeGlow = Mathf.Max(0, _strikeGlow - dt);
            if (_trail != null)
            {
                bool tr = show && live && !waiting && !reduce;
                _trail.SetWidth(tr ? 0.06f + flash * 0.05f : 0f);
                _trail.Set(_mTip.position);
                // the trail warms as the tempo presses on, cools as it broadens
                var tc = rampDir > 0 ? new Color(1f, 0.7f, 0.35f) : rampDir < 0 ? new Color(0.6f, 0.85f, 1f) : new Color(1f, 0.9f, 0.6f);
                _trail.SetColor(tc);
            }
        }

        Vector3 RootPoint(Transform t, Vector3 local) => _mRoot.InverseTransformPoint(t.TransformPoint(local));

        /// Two-bone IK: the elbow for a shoulder at `s` reaching `t` with segment lengths a, b, bending toward `pole`.
        static Vector3 TwoBone(Vector3 s, Vector3 t, Vector3 pole, float a, float b, out Vector3 hand)
        {
            var d = t - s;
            float dist = Mathf.Clamp(d.magnitude, 0.05f, a + b - 0.001f);
            var dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.down;
            hand = s + dir * dist;
            float x = (a * a - b * b + dist * dist) / (2 * dist);
            float h = Mathf.Sqrt(Mathf.Max(0, a * a - x * x));
            var side = pole - dir * Vector3.Dot(pole, dir);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.down;
            side.Normalize();
            return s + dir * x + side * h;
        }

        static void PlaceSeg(Transform seg, Vector3 from, Vector3 to)
        {
            seg.localPosition = from;
            var d = to - from;
            seg.localRotation = Quaternion.FromToRotation(Vector3.up, d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.up);
        }

        // ---- drummers

        void UpdateDrummers(float dt, bool hidden)
        {
            bool reduce = Settings.ReduceMotion;
            var s = Slot;
            for (int k = 0; k < 4; k++)
            {
                var d = _drummers[k];
                if (k > 0 && s != null && _playing && !_round.Done)
                {
                    // the others: their own taps (a CPU's rolled ones; the section's on the beat), never in a hush
                    if (d.LastSlot != s.Index) { d.LastSlot = s.Index; d.Cursor = 0; }
                    var roll = s.CpuRolls != null && k - 1 < s.CpuRolls.Length ? s.CpuRolls[k - 1] : null;
                    bool section = _ids == null;
                    while (d.Cursor < s.Beats.Count)
                    {
                        var b = s.Beats[d.Cursor];
                        double err = roll != null && d.Cursor < roll.ErrMs.Length ? roll.ErrMs[d.Cursor] / 1000.0 : 0;
                        if (_now < b.Time + err) break;
                        bool hit = section || (roll != null && d.Cursor < roll.Verdicts.Length && roll.Verdicts[d.Cursor] != Verdict.Miss);
                        if ((roll != null || section) && hit && !b.Hidden && _now - (b.Time + err) < 0.2)
                        {
                            d.Strike = 1; d.Side = d.Side == 0 ? 1 : -d.Side;
                            d.Flash = 0.55f; d.FlashColor = Color.Lerp(SeatColorFor(k), Color.white, 0.3f);
                        }
                        d.Cursor++;
                    }
                }
                d.Strike = Mathf.Max(0, d.Strike - dt * 7f);
                d.Flash = Mathf.Max(0, d.Flash - dt * 3.2f);
                d.Joy = Mathf.Max(0, d.Joy - dt * 0.8f);
                d.Sad = Mathf.Max(0, d.Sad - dt * 1.2f);
                float joy = Mathf.Max(d.Joy, _finale > 0 ? 1 : 0);
                // the strike: the active arm whips down onto the head, the other rests raised
                float st = d.Strike;
                float hitA = st > 0 ? Mathf.Sin(st * Mathf.PI * 0.5f) : 0;   // 1 at the moment of the strike
                float restA = -30f;
                float rA = d.Side >= 0 ? Mathf.Lerp(restA, 18f, hitA) : restA - 18f * hitA * 0.3f;
                float lA = d.Side < 0 ? Mathf.Lerp(restA, 18f, hitA) : restA - 18f * hitA * 0.3f;
                if (_finale > 0) { rA = lA = -120f - Mathf.Sin(_time * 8f + k) * 10f; }
                if (hidden && k == 0 && st <= 0) { rA = lA = -20f; }
                d.ArmR.localRotation = Quaternion.Euler(rA, 0, -12); d.ArmL.localRotation = Quaternion.Euler(lA, 0, 12);
                float bob = reduce ? 0 : st * 0.025f;
                d.Body.localPosition = new Vector3(0, 0.66f - bob, 0.12f);
                d.Body.localRotation = Quaternion.Euler(8 + st * 6f - d.Sad * 8f, 0, 0);
                // the head: watching the maestro, a nod with a strike, a droop on a miss, eyes up in the hush (listening)
                d.Head.localRotation = Quaternion.Euler(st * 6f + d.Sad * 14f - _hush * 6f, -10f * (k % 2 == 0 ? 1 : -1), 0);
                float open = 0.4f + joy * 0.9f;
                d.Mouth.localScale = new Vector3(1, open * (1 - d.Sad * 0.5f), 1);
            }
            HeadsFlush();
            if (_youMark)
            {
                float y = 2.35f + (reduce ? 0 : Mathf.Sin(_time * 2.1f) * 0.06f);
                _youMark.localPosition = SeatAt[0] + new Vector3(0, y, 0);
                _youMark.localRotation = Quaternion.Euler(0, _time * 60f, 0);
                _youMat.SetFloat("_Intensity", Mathf.Lerp(1.4f, 0.5f, _hush) * (_finale > 0 ? 0.3f : 1));
            }
        }

        // ---- the practicals: candles, sconces, footlights, the podium ring

        void UpdateLights(float dt, bool hidden)
        {
            float hh = _hush;
            float flick = 0.94f + 0.06f * Mathf.Sin(_time * 13.1f) * Mathf.Sin(_time * 7.3f + 1.3f);
            float house = Mathf.Lerp(1f, 0.28f, hh) * (_finale > 0 ? 1.35f : 1);
            _candleMat.SetFloat("_Intensity", 2.2f * house * flick);
            _haloMat.SetFloat("_Intensity", 0.16f * house);
            _sconceMat.SetFloat("_Intensity", 1.5f * house * flick);
            float pulse = Look.BeatPulse;
            _footMat.SetFloat("_Intensity", Mathf.Lerp(1.2f + pulse * 0.9f, 0.25f, hh));
            _lipMat.SetFloat("_Intensity", Mathf.Lerp(1.0f + pulse * 0.6f, 0.2f, hh));
            _podRingMat.SetFloat("_Intensity", Mathf.Lerp(0.8f + pulse * 1.4f, 0.35f, hh));
            _standLampMat.SetFloat("_Intensity", 1.6f);
            var spot = _env.Spotlights;
            if (spot != null) spot.SetLevel(Mathf.Lerp(0.03f, 0.05f, hh), hidden ? 0 : 0.025f);
            var cam = Look.Rig?.Camera;
            if (cam)
                for (int c = 0; c < 3; c++) _halos[c].rotation = Quaternion.LookRotation(_halos[c].position - cam.transform.position);
        }

        // ------------------------------------------------------------ camera

        void Frame(bool snap, float dt)
        {
            bool reduce = Settings.ReduceMotion;
            float k = reduce ? 0.3f : 1;
            var target = CamTarget + new Vector3(0, 0.15f * _hush * k, 0);
            float dist = CamDist - 1.4f * _hush * k;
            float height = CamHeight - 0.15f * _hush * k;
            float fov = CamFov - 2f * _hush * k;
            if (_finale > 0)
            {
                float f = Mathf.SmoothStep(0, 1, Mathf.Clamp01(_finale / 2.5f));
                dist += 2.2f * f * k; height += 0.6f * f * k; target += new Vector3(0, 0.6f * f * k, 1.0f * f * k);
            }
            Look.Rig.Frame(target, dist, height, 0, fov, 2.2f, snap);
        }
    }
}
