// Swing Kings: the hand-built parts of the ballpark (web swingKings/world.js), everything the game is ABOUT:
//
//   PITCH-O   the pitching machine with a face. Its wheels spin up a beat before it fires (the telegraph before the
//             telegraph), it squints while armed, blinks and kicks back on the shot with a muzzle flash, and its
//             eyes follow the ball.
//   pips      half-beat markers laid along the pitch's own flight path: the arc IS the metronome. A pip flashes as
//             the ball reaches it, then recedes to half size.
//   infield   dirt cut-out, mound with a rubber, home plate, chalk foul lines and batter's boxes, a chain-link
//             backstop (Bonanza/Net) that sways and bulges where a ball hits it.
//   towers    light towers whose banks, flares, floodlight cones and turf pools come on with the night palette.
//   stands    an ad-board fascia, a rail and aisles on the bowl (the env kit draws the bowl and the crowd).
//   batter    a NEUTRAL stand-in (capsule + helmet) holding a real bat with a swing arc. The character work plugs in
//             through BatterCue (below); nothing here is the main character.
//
// Built once in Load from MeshBuilder parts (no physics, no Shader.Find): materials are copies of the Look
// templates in Resources, owned by the env. Every glow is drawn from Load on at zero intensity so nothing links a
// program mid-round (prewarm rule). Per-frame work writes transforms and material floats only: no allocations.

using Bonanza.Core;
using Bonanza.Look;
using UnityEngine;

namespace Bonanza
{
    /// What the batter is doing, for whatever body stands at the plate (today the stand-in; later the character).
    public enum BatterCue { Ready, Coil, Swing, Contact, BigHit, Whiff, Strike, SideRetired, Finale }

    public sealed partial class SwingKingsGame
    {
        /// The batter's beats: the character work subscribes here (tier is set for Contact/BigHit).
        public event System.Action<BatterCue, TierId> BatterCued;

        // ------------------------------------------------------------ machine state
        Transform _mHead, _mBrow, _muzzle;
        readonly Transform[] _mWheels = new Transform[2], _mEyes = new Transform[2], _mPupils = new Transform[2];
        Material _muzzleMat;
        float _wheelSpin, _wheelSpinT, _muzzleFlash, _recoil, _flinch;
        bool[] _armed;

        // pips
        const int PipMax = 20;
        MeshBatch _pips;
        readonly float[] _pipBase = new float[PipMax], _pipPop = new float[PipMax];
        readonly Vector3[] _pipPos = new Vector3[PipMax];
        int _pipCount;
        Pitch _pipOwner;
        static readonly Color PipCol = new Color(1f, 0.784f, 0.239f);

        // net, night, batter
        Material _netMat;
        float _netHit;
        Material[] _flareMats, _poolMats, _blinkMats;
        Renderer[] _blinkers;
        Material _glareMat;
        float _nightK, _nightGoal;
        Transform _batter;
        float _batterHop, _batterSlump;
        TrailHandle _batTrail;
        float _batTrailHold;
        Mesh _glowQuad;

        static readonly int IdIntensity = Shader.PropertyToID("_Intensity"), IdColor = Shader.PropertyToID("_Color"),
            IdSwayTime = Shader.PropertyToID("_SwayTime"), IdHit = Shader.PropertyToID("_Hit"), IdSway = Shader.PropertyToID("_Sway");

        static readonly Vector3[] TowerAt = { W(-13, 0, 3.5f), W(-13, 0, -9.5f) };
        const float BankY = 8.5f;

        // ------------------------------------------------------------ helpers

        static Color Lin(uint hex) => Hex(hex).linear;

        MeshRenderer Part(string name, Transform parent, MeshBuilder b, Material m, Vector3 pos, bool cast = false)
        {
            var r = EnvKit.Renderer(name, parent, _env.Own(b.ToMesh(name)), m);
            r.transform.localPosition = pos;
            if (cast) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return r;
        }

        /// A unit glow card (XY, uv 0..1) shared by every Bonanza/Glow renderer in the set.
        Mesh GlowQuad()
        {
            if (_glowQuad) return _glowQuad;
            var b = new MeshBuilder();
            b.Surface(1, 1, (u, v) => (new Vector3(u - 0.5f, v - 0.5f, 0), Vector3.back), Matrix4x4.identity, Color.white, Vector2.one);
            return _glowQuad = _env.Own(b.ToMesh("GlowQuad"));
        }

        Material Glow(string name, Transform parent, Vector3 pos, float size, Color c, bool billboard, Quaternion? rot = null, float core = 2)
        {
            var m = _env.Mat(EnvKit.GlowMat);
            m.SetColor(IdColor, c);
            m.SetFloat(IdIntensity, 0);
            m.SetFloat("_Billboard", billboard ? 1 : 0);
            m.SetFloat("_Core", core);
            var r = EnvKit.Renderer(name, parent, GlowQuad(), m);
            r.receiveShadows = false;
            r.transform.localPosition = pos;
            r.transform.localRotation = rot ?? Quaternion.identity;
            r.transform.localScale = Vector3.one * size;
            return m;
        }

        /// A flat chalk stroke on the ground from a to b (y from a).
        static void Chalk(MeshBuilder b, Vector3 a, Vector3 c, float w, Color col)
        {
            var d = c - a; d.y = 0;
            b.FloorQuad(w, d.magnitude, Matrix4x4.TRS((a + c) * 0.5f, Quaternion.LookRotation(d.normalized, Vector3.up), Vector3.one), col);
        }

        /// A ring on the ground from r0 to r1 whose vertex colour fades from `inner` to `outer` (dirt feathered
        /// into grass: the outer colour is the grass/dirt ratio, so the dirt material lands on the turf's colour).
        static void FeatherRing(MeshBuilder b, Vector3 centre, float r0, float r1, float y, Color inner, Color outer, int seg)
        {
            int start = b.Count;
            b.Surface(seg, 2, (u, v) =>
            {
                float a = u * Mathf.PI * 2, r = Mathf.Lerp(r0, r1, v);
                return (new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r), Vector3.up);
            }, Matrix4x4.Translate(centre + Vector3.up * y), Color.white, new Vector2(4, 0.3f));
            for (int i = start; i < b.Count; i++)
            {
                var p = b.P[i] - centre;
                float k = Mathf.InverseLerp(r0, r1, new Vector2(p.x, p.z).magnitude);
                b.C[i] = Color.Lerp(inner, outer, k * k * (3 - 2 * k));
            }
        }

        // ------------------------------------------------------------ build

        /// Infield, backstop, towers' lights, stands dressing. Called from BuildSet once the env exists.
        void BuildInfield()
        {
            var pal = Look.Palette.Live;
            var dirtL = pal[PC.Dirt].linear; var grassL = pal[PC.Ground].linear;
            var toGrass = new Color(grassL.r / Mathf.Max(0.02f, dirtL.r), grassL.g / Mathf.Max(0.02f, dirtL.g), grassL.b / Mathf.Max(0.02f, dirtL.b), 1) * 0.92f;

            // Dirt: the cut-out around the plate and the mound under the machine, feathered into the turf.
            var dirtMat = EnvKit.Surface(_env, Color.white, "sk-dirt", new Vector2(2.2f, 2.2f), rim: 0.25f, pulse: 0.05f, mapStrength: 0.75f);
            Look.Track(dirtMat, PC.Dirt);
            var b = new MeshBuilder();
            var cut = PlatePos + new Vector3(-0.2f, 0, 0); cut.y = 0;
            b.Disc(2.55f, 56, Matrix4x4.Translate(cut + Vector3.up * 0.02f), Color.white, 2.2f);
            FeatherRing(b, cut, 2.55f, 3.0f, 0.02f, Color.white, toGrass, 56);
            var mound = new Vector3(MachinePos.x + 0.3f, 0, MachinePos.z);
            b.Shell(1.45f, 1.95f, 0.16f, 40, 0, Mathf.PI * 2, Matrix4x4.Translate(mound + new Vector3(0, 0.06f, 0)), Color.white, new Vector2(4, 0.2f));
            b.Disc(1.45f, 40, Matrix4x4.Translate(mound + new Vector3(0, 0.14f, 0)), Color.white, 1.2f);
            FeatherRing(b, mound, 1.9f, 2.35f, 0.012f, new Color(0.9f, 0.9f, 0.9f), toGrass, 40);
            EnvKit.Static(_env, "Infield", transform, b, dirtMat);

            // Chalk and the plate: one draw, a near-flat bright white (web MeshBasic 0xf6f1e4, below the bloom cut).
            var chalkMat = EnvKit.Surface(_env, Hex(0xf6f1e4), null, null, rim: 0, pulse: 0.05f, lift: 0.55f, bands: 1);
            b = new MeshBuilder();
            var white = Color.white;
            var plate = new Vector3(PlatePos.x, 0.035f, PlatePos.z);
            foreach (var s in new[] { -1f, 1f })
            {
                // Foul lines leave the plate at 45 degrees either side of the line to the mound.
                var from = plate + new Vector3(-0.25f, 0, 0);
                var dir = new Vector3(-Mathf.Cos(Mathf.PI / 4), 0, s * Mathf.Sin(Mathf.PI / 4));
                Chalk(b, from, from + dir * 13f, 0.08f, white);
                // Batter's boxes: open rectangles either side of the plate.
                const float BW = 1.05f, BD = 1.7f, T = 0.055f;
                var c = new Vector3(PlatePos.x, 0.04f, PlatePos.z + s * 0.95f);
                Vector3 p00 = c + new Vector3(-BW / 2, 0, -BD / 2), p10 = c + new Vector3(BW / 2, 0, -BD / 2),
                        p01 = c + new Vector3(-BW / 2, 0, BD / 2), p11 = c + new Vector3(BW / 2, 0, BD / 2);
                Chalk(b, p00, p01, T, white); Chalk(b, p10, p11, T, white);
                Chalk(b, p00 - Vector3.right * T / 2, p10 + Vector3.right * T / 2, T, white);
                Chalk(b, p01 - Vector3.right * T / 2, p11 + Vector3.right * T / 2, T, white);
            }
            EnvKit.Static(_env, "Chalk", transform, b, chalkMat);
            var plateMat = EnvKit.Surface(_env, Hex(0xfff6e0), null, null, rim: 0.2f, pulse: 0.05f, lift: 0.85f, bands: 1);
            b = new MeshBuilder();
            b.Disc(0.36f, 5, Matrix4x4.Translate(new Vector3(PlatePos.x, 0.045f, PlatePos.z)), white);   // a vertex points at the catcher
            b.Shell(0.36f, 0.38f, 0.04f, 5, Mathf.PI / 2, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(PlatePos.x, 0.025f, PlatePos.z)), new Color(0.55f, 0.52f, 0.48f), Vector2.one);
            EnvKit.Static(_env, "Plate", transform, b, plateMat);

            // Backstop behind the plate (+x): a chain-link arc with a top rail and three posts (web: r 3.3, h 3.4).
            var bc = new Vector3(PlatePos.x + 0.4f, 1.7f, PlatePos.z + 0.6f);
            float a0 = Mathf.PI * 0.32f, arc = Mathf.PI * 0.5f;
            _netMat = _env.Mat(EnvKit.NetMat);
            _netMat.SetColor(IdColor, Hex(0xcfeaff));
            _netMat.SetFloat("_Opacity", 0.42f);
            b = new MeshBuilder();
            b.Shell(3.3f, 3.3f, 3.4f, 28, a0, arc, Matrix4x4.Translate(bc), white, Vector2.one);
            var net = EnvKit.Static(_env, "Backstop", transform, b, _netMat);
            net.receiveShadows = false;
            var railMat = EnvKit.Surface(_env, Hex(0x2b2350), null, null, rim: 0.8f, pulse: 0.05f);
            b = new MeshBuilder();
            var rail = new Vector3[29];
            for (int i = 0; i < rail.Length; i++)
            {
                float a = a0 + arc * i / (rail.Length - 1);
                rail[i] = bc + new Vector3(Mathf.Sin(a) * 3.3f, 1.7f, Mathf.Cos(a) * 3.3f);
            }
            b.Tube(rail, 0.05f, 6, Matrix4x4.identity, white);
            foreach (float k in new[] { 0f, 1f })   // end posts only: a middle post made it read as a goal
            {
                float a = a0 + arc * k;
                var foot = bc + new Vector3(Mathf.Sin(a) * 3.3f, -1.7f, Mathf.Cos(a) * 3.3f);
                b.Tube(new[] { foot, foot + Vector3.up * 3.42f }, 0.045f, 6, Matrix4x4.identity, white);
            }
            EnvKit.Static(_env, "BackstopRail", transform, b, railMat);

            // Stands dressing: an ad-board fascia along the wall cap, a rail on top, aisles up the bowl.
            var crowdO = _env.Crowd?.O;
            if (crowdO != null)
            {
                float wallTop = crowdO.WallY + crowdO.WallHeight / 2;
                var adMat = EnvKit.Surface(_env, Color.white, "sk-adboard", new Vector2(7, 1), rim: 0.2f, pulse: 0.15f, lift: 0.3f, bands: 2, mapStrength: 1);
                adMat.SetColor("_BaseColor", new Color(0.86f, 0.86f, 0.9f));
                b = new MeshBuilder();
                b.Shell(crowdO.WallRadius - 0.04f, crowdO.WallRadius - 0.04f, 0.46f, 72, -crowdO.BowlArc / 2, crowdO.BowlArc,
                    Matrix4x4.Translate(new Vector3(0, wallTop - 0.3f, crowdO.Center.z)), white, Vector2.one, inward: true);
                EnvKit.Static(_env, "AdBoards", transform, b, adMat).receiveShadows = false;

                var steel = EnvKit.Surface(_env, Hex(0xb9c4dc), null, null, rim: 0.9f, pulse: 0.05f, bands: 2);
                b = new MeshBuilder();
                var top = new Vector3[57];
                for (int i = 0; i < top.Length; i++)
                {
                    float a = -crowdO.BowlArc / 2 + crowdO.BowlArc * i / (top.Length - 1);
                    top[i] = new Vector3(Mathf.Sin(a) * (crowdO.WallRadius - 0.1f), wallTop + 0.42f, crowdO.Center.z + Mathf.Cos(a) * (crowdO.WallRadius - 0.1f));
                }
                b.Tube(top, 0.025f, 5, Matrix4x4.identity, white);
                for (int i = 0; i < top.Length; i += 3) b.Tube(new[] { top[i] - Vector3.up * 0.42f, top[i] }, 0.018f, 4, Matrix4x4.identity, white);
                // Aisles: lighter concrete strips up the bowl (inside its surface so the seat texture stays under them).
                Color sc = Lin(0x5474ad), stl = Lin(0xb9c4dc);   // the stand's blue under the steel material, a shade lighter
                var aisle = new Color(sc.r / stl.r, sc.g / stl.g, sc.b / stl.b) * 1.35f;
                for (int i = 1; i < 10; i++)
                {
                    float a = -crowdO.BowlArc / 2 + crowdO.BowlArc * i / 10f;
                    b.Shell(crowdO.BowlTop - 0.05f, crowdO.BowlBottom - 0.05f, crowdO.BowlHeight, 1, a - 0.012f, 0.024f,
                        Matrix4x4.Translate(new Vector3(0, crowdO.BowlY, crowdO.Center.z)), aisle, Vector2.one, inward: true);
                }
                EnvKit.Static(_env, "StandRail", transform, b, steel).receiveShadows = false;
            }

            // Night lights: tower-head flares, turf pools under the floods (both on at zero from Load: prewarmed).
            _flareMats = new Material[TowerAt.Length];
            for (int i = 0; i < TowerAt.Length; i++)
                _flareMats[i] = Glow("TowerFlare" + i, transform, TowerAt[i] + new Vector3(0.25f, BankY, 0), 4.2f, Hex(0xfff3d0), true, null, 2.6f);
            var flat = Quaternion.Euler(90, 0, 0);
            _poolMats = new[]
            {
                Glow("PoolPlate", transform, new Vector3(PlatePos.x - 0.3f, 0.06f, PlatePos.z), 9f, Hex(0xfff0d6), false, flat, 0.5f),
                Glow("PoolMound", transform, new Vector3(MachinePos.x + 0.3f, 0.2f, MachinePos.z), 6.5f, Hex(0xfff0d6), false, flat, 0.5f),
            };
            // The low sun behind the skyline (day): a wide soft glare the bloom carries into the arches.
            _glareMat = Glow("SunGlare", transform, new Vector3(30, 7, 92), 70, Hex(0xffc98a), true, null, 1.2f);
            BuildBlinkers();
        }

        /// Red aviation lights on the tallest skyline roofs, blinking at night (two phases, one material each).
        void BuildBlinkers()
        {
            _blinkMats = new[] { _env.Mat(EnvKit.GlowMat), _env.Mat(EnvKit.GlowMat) };
            foreach (var m in _blinkMats) { m.SetColor(IdColor, Hex(0xff3b30)); m.SetFloat(IdIntensity, 0); m.SetFloat("_Billboard", 1); m.SetFloat("_Core", 3); }
            var verts = EnvBytesMesh.Positions("sk-skyline");
            var sky = _env.Backdrop?.Root ? _env.Backdrop.Root.transform.Find("Skyline") : null;
            if (verts == null || sky == null) { _blinkers = new Renderer[0]; return; }
            const int Bins = 9;
            var best = new Vector3[Bins];
            for (int i = 0; i < Bins; i++) best[i] = new Vector3(0, float.MinValue, 0);
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var v0 in verts) { minX = Mathf.Min(minX, v0.x); maxX = Mathf.Max(maxX, v0.x); }
            foreach (var v0 in verts)
            {
                var v = sky.TransformPoint(v0);
                int bin = Mathf.Clamp((int)((v0.x - minX) / Mathf.Max(0.01f, maxX - minX) * Bins), 0, Bins - 1);
                if (v.y > best[bin].y) best[bin] = v;
            }
            var list = new System.Collections.Generic.List<Renderer>();
            for (int i = 0; i < Bins; i++)
            {
                if (best[i].y < 6) continue;
                var r = EnvKit.Renderer("Blinker" + i, transform, GlowQuad(), _blinkMats[i % 2]);
                r.receiveShadows = false;
                r.transform.position = best[i] + Vector3.up * 0.4f;
                r.transform.localScale = Vector3.one * 2.4f;
                list.Add(r);
            }
            _blinkers = list.ToArray();
        }

        /// PITCH-O (web world.js machine): wheeled cart, neck, capsule head with a cream band, flared barrel, two
        /// yellow spoked wheels that spin up, a hopper of spare balls, two big eyes and a visor brow. The face is on
        /// the camera side (Unity -z).
        void BuildMachine()
        {
            var root = new GameObject("PitchO").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(MachinePos.x, 0.14f, MachinePos.z);   // on the mound top
            Color shell = Lin(0xe8563f), dark = Lin(0x2b2350), trim = Lin(0xd8c7a6), cream = Lin(0xfffaf0), yellow = Lin(0xffd93d), hub = Lin(0x8a8fa8);
            var mat = EnvKit.Surface(_env, Color.white, null, null, rim: 1.0f, pulse: 0.1f, bands: 3);

            // Cart: a squat dark box on four tyres, with a cream bumper strip.
            var b = new MeshBuilder();
            b.Box(Matrix4x4.TRS(new Vector3(0, 0.28f, 0), Quaternion.identity, new Vector3(1.7f, 0.34f, 1.15f)), dark);
            b.Box(Matrix4x4.TRS(new Vector3(0, 0.3f, -0.585f), Quaternion.identity, new Vector3(1.5f, 0.08f, 0.03f)), trim);
            foreach (var x in new[] { -0.6f, 0.6f })
                foreach (var z in new[] { -0.62f, 0.62f })
                {
                    b.Torus(0.17f, 0.09f, 8, 18, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(x, 0.12f, z)), dark);
                    b.Sphere(0.1f, 10, 6, Matrix4x4.TRS(new Vector3(x, 0.12f, z), Quaternion.identity, new Vector3(1, 1, 0.45f)), hub, Vector3.one);
                }
            b.Shell(0.2f, 0.3f, 0.75f, 14, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(0, 0.81f, 0)), trim, Vector2.one);
            Part("PitchO.Cart", root, b, mat, Vector3.zero, cast: true);

            _mHead = new GameObject("PitchO.Head").transform;
            _mHead.SetParent(root, false);
            _mHead.localPosition = new Vector3(0, MachinePos.y - 0.14f, 0);
            b = new MeshBuilder();
            var alongX = Quaternion.Euler(0, 0, 90);
            b.Capsule(0.52f, 0.55f, 6, 18, Matrix4x4.TRS(Vector3.zero, alongX, Vector3.one), shell);
            b.Shell(0.538f, 0.538f, 0.2f, 24, 0, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(-0.18f, 0, 0), alongX, Vector3.one), trim, Vector2.one);
            // Barrel: a flared cannon from the body to the muzzle, aimed a little up at the plate; a trim lip on the mouth.
            var aim = new Vector3(Mathf.Cos(0.3f), Mathf.Sin(0.3f), 0);
            var bq = Quaternion.FromToRotation(Vector3.up, aim);
            var bcen = new Vector3(0.68f, 0.24f, 0);
            b.Shell(0.34f, 0.24f, 0.8f, 18, 0, Mathf.PI * 2, Matrix4x4.TRS(bcen, bq, Vector3.one), dark, Vector2.one);
            b.Shell(0.32f, 0.22f, 0.8f, 18, 0, Mathf.PI * 2, Matrix4x4.TRS(bcen, bq, Vector3.one), Lin(0x120c22), Vector2.one, inward: true);
            b.Torus(0.33f, 0.045f, 6, 22, Mathf.PI * 2, Matrix4x4.TRS(bcen + aim * 0.4f, Quaternion.FromToRotation(Vector3.forward, aim), Vector3.one), trim);
            // Hopper (the chute) with spare balls waiting in it.
            b.Shell(0.42f, 0.2f, 0.5f, 16, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(-0.2f, 0.72f, 0)), trim, Vector2.one);
            b.Shell(0.40f, 0.18f, 0.5f, 16, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(-0.2f, 0.72f, 0)), Lin(0x6b5d4a), Vector2.one, inward: true);
            foreach (var (x, z) in new[] { (-0.32f, -0.08f), (-0.1f, 0.1f), (-0.18f, -0.18f), (-0.05f, -0.12f) })
                b.Sphere(0.14f, 10, 8, Matrix4x4.Translate(new Vector3(x, 0.88f, z)), cream, Vector3.one);
            // A little antenna with a bulb: a toy, not a lamp post.
            b.Tube(new[] { new Vector3(-0.62f, 0.3f, 0), new Vector3(-0.78f, 0.82f, 0.05f) }, 0.02f, 5, Matrix4x4.identity, dark);
            b.Sphere(0.06f, 8, 6, Matrix4x4.Translate(new Vector3(-0.78f, 0.86f, 0.05f)), yellow, Vector3.one);
            Part("PitchO.Body", _mHead, b, mat, Vector3.zero, cast: true);

            // Wheels: yellow rings with dark spokes, so the spin-up reads (a plain torus spinning looks still).
            b = new MeshBuilder();
            b.Torus(0.34f, 0.1f, 8, 22, Mathf.PI * 2, Matrix4x4.identity, yellow);
            for (int k = 0; k < 3; k++)
                b.Box(Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, k * 60), new Vector3(0.62f, 0.06f, 0.05f)), dark);
            b.Sphere(0.08f, 8, 6, Matrix4x4.identity, trim, new Vector3(1, 1, 0.8f));
            var wheelMesh = _env.Own(b.ToMesh("PitchO.Wheel"));
            for (int s = 0; s < 2; s++)
            {
                var r = EnvKit.Renderer("PitchO.Wheel" + s, _mHead, wheelMesh, mat);
                r.transform.localPosition = new Vector3(0.62f, s == 0 ? -0.5f : 0.5f, -0.05f);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                _mWheels[s] = r.transform;
            }

            // Face: two big eyes (white, dark pupil with a catch-light) and a visor brow.
            var eyeMat = EnvKit.Surface(_env, Color.white, null, null, rim: 0.3f, pulse: 0.05f, lift: 0.45f, bands: 2);
            b = new MeshBuilder();
            b.Sphere(0.17f, 14, 10, Matrix4x4.identity, Color.white, new Vector3(1, 1, 0.55f));
            var whiteMesh = _env.Own(b.ToMesh("PitchO.EyeWhite"));
            b = new MeshBuilder();
            b.Sphere(0.085f, 10, 8, Matrix4x4.identity, Lin(0x1a1030), new Vector3(1, 1, 0.7f));
            b.Sphere(0.026f, 6, 4, Matrix4x4.Translate(new Vector3(-0.028f, 0.03f, -0.055f)), new Color(3, 3, 3), Vector3.one);
            var pupilMesh = _env.Own(b.ToMesh("PitchO.Pupil"));
            float[] ex = { -0.34f, 0.02f };
            for (int i = 0; i < 2; i++)
            {
                var e = new GameObject("PitchO.Eye" + i).transform;
                e.SetParent(_mHead, false);
                e.localPosition = new Vector3(ex[i], 0.12f, -0.46f);
                EnvKit.Renderer("White", e, whiteMesh, eyeMat).receiveShadows = false;
                var p = EnvKit.Renderer("Pupil", e, pupilMesh, eyeMat);
                p.receiveShadows = false;
                p.transform.localPosition = new Vector3(0.05f, 0, -0.075f);
                _mEyes[i] = e; _mPupils[i] = p.transform;
            }
            b = new MeshBuilder();
            b.Box(Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(0.7f, 0.07f, 0.12f)), dark);
            _mBrow = Part("PitchO.Brow", _mHead, b, mat, new Vector3(-0.16f, 0.34f, -0.47f)).transform;

            // Muzzle flash: an additive glow card at the barrel mouth (web: a radial glow, not a solid disc).
            _muzzleMat = Glow("PitchO.Muzzle", _mHead, new Vector3(1.06f, 0.36f, -0.05f), 1.5f, Hex(0xfff2c0), true);
            _muzzle = _mHead.Find("PitchO.Muzzle");

            Look.Blob(root, 1.15f, 0.5f, groundY: 0.16f, heightOffset: 0.3f);
        }

        /// The stand-in batter (neutral capsule + batting helmet) and a real bat: tapered barrel, knob, tip.
        void BuildBatter()
        {
            var batterMat = Mat(Color.white, rim: 0.9f); Look.Track(batterMat, PC.Batter);
            var go = Cast(Prim(PrimitiveType.Capsule, BatterPos + Vector3.up * 0.9f, new Vector3(0.6f, 0.9f, 0.6f), batterMat));
            go.name = "BatterStandIn";
            _batter = go.transform;
            Look.Blob(_batter, 0.85f, 0.7f, groundY: 0.03f, heightOffset: 0.87f);
            var helmetMat = EnvKit.Surface(_env, Hex(0x1f2f6b), null, null, rim: 1.2f, pulse: 0.08f, bands: 3);
            var b = new MeshBuilder();
            b.Sphere(0.33f, 16, 10, Matrix4x4.identity, Color.white, new Vector3(1, 0.82f, 1));
            b.Box(Matrix4x4.TRS(new Vector3(-0.3f, -0.08f, 0), Quaternion.Euler(0, 0, 8), new Vector3(0.28f, 0.03f, 0.4f)), Color.white);   // brim toward the pitcher
            b.Sphere(0.12f, 8, 6, Matrix4x4.Translate(new Vector3(0.02f, -0.12f, -0.3f)), Color.white, new Vector3(1, 1.3f, 0.5f));          // ear flap, camera side
            var helmet = Part("Helmet", transform, b, helmetMat, Vector3.zero, cast: true).transform;
            helmet.SetParent(_batter, false);
            helmet.localScale = new Vector3(1 / 0.6f, 1 / 0.9f, 1 / 0.6f);
            helmet.localPosition = new Vector3(0, 0.82f, 0);

            _batPivot = new GameObject("BatPivot").transform;
            _batPivot.SetParent(transform, false);
            _batPivot.position = BatterPos + new Vector3(-0.2f, 1.25f, 0);
            var batMat = EnvKit.Surface(_env, Color.white, null, null, rim: 1.0f, pulse: 0.08f, bands: 3);
            b = new MeshBuilder();
            var along = Quaternion.Euler(0, 0, 90);   // +y -> -x: the bat runs out along local -x from the hands
            Color wood = Lin(0xd79a58), knob = Lin(0x3a2418), tape = Lin(0x2a2236);
            b.Shell(0.075f, 0.032f, 0.86f, 12, 0, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(-0.62f, 0, 0), along, Vector3.one), wood, Vector2.one);
            b.Sphere(0.075f, 12, 6, Matrix4x4.Translate(new Vector3(-1.05f, 0, 0)), wood, Vector3.one);
            b.Shell(0.034f, 0.032f, 0.22f, 10, 0, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(-0.09f, 0, 0), along, Vector3.one), tape, Vector2.one);
            b.Sphere(0.055f, 10, 6, Matrix4x4.Translate(new Vector3(0.03f, 0, 0)), knob, new Vector3(0.6f, 1, 1));
            Part("Bat", _batPivot, b, batMat, Vector3.zero, cast: true);
            PoseBat(1);
        }

        void BuildPips()
        {
            var tpl = new MeshBuilder();
            tpl.Sphere(0.09f, 16, 12, Matrix4x4.identity, Color.white, Vector3.one);
            var m = EnvKit.Surface(_env, Color.white, null, null, rim: 0.4f, pulse: 0.2f, lift: 2.2f, bands: 1);
            _pips = new MeshBatch("Pips", transform, tpl, PipMax, m);
            _pips.Renderer.receiveShadows = false;
            for (int i = 0; i < PipMax; i++) _pips.Matrix[i] = Matrix4x4.Scale(Vector3.zero);
            _pips.Flush();
        }

        // ------------------------------------------------------------ machine and pips (web armMachine / fireMachine / setPips)

        void ArmMachine(float strength) { _wheelSpinT = Mathf.Max(_wheelSpinT, 26 * strength); }

        void FireMachine()
        {
            _muzzleFlash = 1; _recoil = 1;
            _wheelSpinT = Mathf.Max(_wheelSpinT, 46);
            var fx = Look.Fx;
            fx.Flare(Muzzle, Hex(0xfff2c0), 0.55f, 0.14f);
            fx.Burst(Muzzle + new Vector3(0.1f, 0.05f, 0), 6, Hex(0xfff6e0), 2.2f, 0.1f, 0.35f, -2, new Vector3(1, 0.3f, 0), 0.6f);
        }

        /// Arm the machine one beat before each launch (web updatePitches: armed at launchBeat - 1).
        void UpdateArming(double beat)
        {
            var ps = _round.Pitches;
            if (_armed == null || _armed.Length != ps.Count) _armed = new bool[ps.Count];
            for (int i = 0; i < ps.Count; i++)
            {
                if (_armed[i]) continue;
                double lb = ps[i].LaunchBeat;
                if (beat >= lb - 1 && beat < lb + 0.5)
                {
                    _armed[i] = true;
                    ArmMachine(ps[i].Kind == PitchKind.Slam ? 1.6f : 1);
                    if (ps[i].Kind != PitchKind.Demo) Cue(BatterCue.Ready);
                }
            }
        }

        void SetPips(Pitch p)
        {
            _pipOwner = p;
            int steps = Mathf.Min(PipMax, Mathf.Max(2, (int)System.Math.Round(p.Lead * 2)));
            for (int i = 0; i < PipMax; i++)
            {
                if (i >= steps) { _pipBase[i] = 0; continue; }
                double u = (i + 1) / (double)steps;
                double off = u * p.Lead;
                bool whole = System.Math.Abs(off - System.Math.Round(off)) < 0.08;
                _pipPos[i] = Sample(p, u);
                _pipBase[i] = whole ? 2.0f : 1.35f;   // big enough to read as the beat across the room; whole beats bigger
                _pipPop[i] = 0;
            }
            _pipCount = steps;
        }

        void PopPip(int i, Pitch p)
        {
            if (p != _pipOwner || i < 0 || i >= _pipCount) return;
            _pipPop[i] = 1; _pipBase[i] *= 0.5f;   // flash, then recede: passed markers do not hang as glowing discs
        }

        void ClearPips(Pitch p)
        {
            if (p != _pipOwner) return;
            _pipOwner = null;
            if (_pipCount == 0) return;
            _pipCount = 0;
            for (int i = 0; i < PipMax; i++) _pips.Matrix[i] = Matrix4x4.Scale(Vector3.zero);
            _pips.Flush();
        }

        void Cue(BatterCue c, TierId tier = TierId.Bunt)
        {
            switch (c)
            {
                case BatterCue.Contact: _batterHop = 0.5f; break;
                case BatterCue.BigHit: case BatterCue.Finale: _batterHop = 1; break;
                case BatterCue.Whiff: case BatterCue.Strike: _batterSlump = 0.7f; break;
                case BatterCue.SideRetired: _batterSlump = 1; break;
            }
            BatterCued?.Invoke(c, tier);
        }

        // ------------------------------------------------------------ per frame (presentation time)

        void UpdateSet(float dt, double beat)
        {
            if (_mHead == null) return;
            bool calm = Settings.ReduceMotion;

            // machine
            _wheelSpinT = Damp(_wheelSpinT, 0, 1.6f, dt);
            _wheelSpin += dt * (2 + _wheelSpinT);
            _mWheels[0].localRotation = Quaternion.Euler(0, 0, _wheelSpin * Mathf.Rad2Deg);
            _mWheels[1].localRotation = Quaternion.Euler(0, 0, -_wheelSpin * Mathf.Rad2Deg);
            _muzzleFlash = Damp(_muzzleFlash, 0, 9, dt);
            _recoil = Damp(_recoil, 0, 7, dt);
            _flinch = Damp(_flinch, 0, 4, dt);
            float bob = _playing ? Mathf.Sin((float)beat * Mathf.PI) * 0.022f : 0;
            float kick = _recoil * (calm ? 0.04f : 0.14f);
            _mHead.localPosition = new Vector3(-kick, MachinePos.y - 0.14f + _recoil * 0.03f, 0);
            _mHead.localRotation = Quaternion.Euler(0, _flinch * 10, (bob + _recoil * 0.12f - _flinch * 0.1f) * Mathf.Rad2Deg);
            float sq = 1 + _recoil * 0.06f;
            _mHead.localScale = new Vector3(1 / Mathf.Sqrt(sq), sq, 1 / Mathf.Sqrt(sq));
            _muzzleMat.SetFloat(IdIntensity, _muzzleFlash * 1.6f);
            _muzzle.localScale = Vector3.one * 1.5f * (1 + _muzzleFlash * 0.8f);

            // face: squint while the wheels spin up, blink shut on the shot, eyes wide on a big hit; pupils on the ball
            float focus = Mathf.Clamp01(_wheelSpinT / 26);
            float open = (1 - focus * 0.5f) * (1 - _muzzleFlash * 0.9f) * (1 + _flinch * 0.25f);
            Vector3 look = Contact;
            foreach (var kv in _pitchBalls) { look = kv.Value.Go.transform.position; break; }
            if (_hitBalls.Count > 0) look = _hitBalls[_hitBalls.Count - 1].P;
            for (int i = 0; i < 2; i++)
            {
                _mEyes[i].localScale = new Vector3(1, Mathf.Max(0.08f, open), 1);
                var d = _mEyes[i].InverseTransformDirection(look - _mEyes[i].position).normalized;
                var want = new Vector3(Mathf.Clamp(d.x, -1, 1) * 0.075f, Mathf.Clamp(d.y, -1, 1) * 0.06f, -0.075f);
                _mPupils[i].localPosition = Damp(_mPupils[i].localPosition, want, 14, dt);
            }
            _mBrow.localPosition = new Vector3(-0.16f, 0.34f - focus * 0.07f + _flinch * 0.05f, -0.47f);
            _mBrow.localRotation = Quaternion.Euler(0, 0, (-focus * 0.12f + _flinch * 0.1f) * Mathf.Rad2Deg);

            // pips
            if (_pipCount > 0)
            {
                for (int i = 0; i < _pipCount; i++)
                {
                    _pipPop[i] = Damp(_pipPop[i], 0, 7, dt);
                    float s = _pipBase[i] * (1 + _pipPop[i] * 1.2f);
                    _pips.Matrix[i] = Matrix4x4.TRS(_pipPos[i], Quaternion.identity, new Vector3(s, s, s));
                    float glow = 1 + _pipPop[i] * 2.5f + (_pipBase[i] >= 1.9f ? 0.4f : 0);
                    _pips.Tint[i] = PipCol.linear * glow;
                }
                _pips.Flush();
            }

            // backstop net: breeze + the bulge where a ball went in
            _netHit = Damp(_netHit, 0, 3.5f, dt);
            _netMat.SetFloat(IdSwayTime, Time.time * (calm ? 0.3f : 1));
            _netMat.SetFloat(IdSway, calm ? 0.015f : 0.05f);
            _netMat.SetFloat(IdHit, calm ? _netHit * 0.3f : _netHit);

            // night lights (eased; the palette swap takes ~2.4 s)
            _nightK = Damp(_nightK, _nightGoal, 1.4f, dt);
            float n = _nightK;
            float flicker = 1 + Mathf.Sin(Time.time * 23f) * 0.015f;
            for (int i = 0; i < _flareMats.Length; i++) _flareMats[i].SetFloat(IdIntensity, n * 2.6f * flicker + Look.BeatPulse * n * 0.4f);
            for (int i = 0; i < _poolMats.Length; i++) _poolMats[i].SetFloat(IdIntensity, n * 0.2f);
            _glareMat.SetFloat(IdIntensity, (1 - n) * 0.22f);
            bool blinkOn = n > 0.5f;
            for (int i = 0; i < _blinkers.Length; i++) _blinkers[i].enabled = blinkOn;
            if (blinkOn)
            {
                float t = Time.time * 0.8f;
                _blinkMats[0].SetFloat(IdIntensity, (t % 1f) < 0.35f ? 1.6f : 0.05f);
                _blinkMats[1].SetFloat(IdIntensity, ((t + 0.5f) % 1f) < 0.35f ? 1.6f : 0.05f);
            }
            _env?.Spotlights?.SetLevel(Mathf.Lerp(0.03f, 0.075f, n), Mathf.Lerp(0.04f, 0.1f, n));
            _env?.Crowd?.SetTint(Color.Lerp(Color.white, new Color(0.7f, 0.74f, 0.9f), n));

            // stand-in: hop on a hit, slump on a miss (the character replaces this; the cues stay)
            _batterHop = Damp(_batterHop, 0, 5, dt);
            _batterSlump = Damp(_batterSlump, 0, 3, dt);
            float hop = Mathf.Sin(Mathf.Clamp01(_batterHop) * Mathf.PI) * (calm ? 0.05f : 0.22f);
            float st = 1 + _batterHop * 0.08f - _batterSlump * 0.1f;
            _batter.localScale = new Vector3(0.6f / Mathf.Sqrt(st), 0.9f * st, 0.6f / Mathf.Sqrt(st));
            _batter.localPosition = BatterPos + Vector3.up * (0.9f * st + hop);
            _batter.localRotation = Quaternion.Euler(0, 0, -_batterSlump * 9);

            // swing arc: a short warm ribbon off the bat tip through the follow-through
            if (_batTrail != null)
            {
                _batTrail.Set(BatTip);
                if (_swingT >= 1) { _batTrailHold -= dt; if (_batTrailHold <= 0) { _batTrail.Release(); _batTrail = null; } }
            }
        }

        void StartSwingArc()
        {
            if (_mouse) return;   // conducting draws its own ribbon (the trace)
            _batTrail?.Release();
            _batTrail = Look.Fx.Trail(Hex(0xfff3cf) * 1.8f, 0.2f);   // HDR: the arc crosses the bloom like the hit flash
            _batTrail.Set(BatTip);
            _batTrailHold = 0.12f;
        }
    }
}
