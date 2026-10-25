// Swing Kings: the hand-built parts of the ballpark (web swingKings/world.js), everything the game is ABOUT:
//
//   PITCH-O   the pitching machine with a face. Its wheels spin up a beat before it fires (the telegraph before the
//             telegraph), it squints while armed, blinks and kicks back on the shot with a muzzle flash, and its
//             eyes follow the ball.
//   pips      half-beat markers as flat diamonds on the turf under the pitch's flight path (the ball's shadow runs
//             over them: the path IS the metronome), and a reticle ring on the ground at the plate for the strike zone.
//             A pip flashes as the ball passes over it, then recedes to half size.
//   infield   dirt cut-out, mound with a rubber, home plate, chalk foul lines and batter's boxes, a chain-link
//             backstop (Bonanza/Net) that sways and bulges where a ball hits it.
//   towers    light towers whose banks, flares, floodlight cones and turf pools come on with the night palette.
//   stands    an ad-board fascia, a rail and aisles on the bowl, seat-row bands, a facade lip, the padded track edge
//             with LED segments (the env kit draws the bowl and the crowd).
//   skyline   lit window grids, a sun-side wash and beacons built from the authored skyline's faces; a haze band.
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
        Material _muzzleMat, _muzzleRingMat;
        const float MachineRootY = 0.33f;
        float _wheelSpin, _wheelSpinT, _muzzleFlash, _recoil, _flinch;
        bool[] _armed;

        // pips
        const int PipMax = 20;
        MeshBatch _pips;
        readonly float[] _pipBase = new float[PipMax], _pipPop = new float[PipMax];
        readonly Vector3[] _pipPos = new Vector3[PipMax];
        readonly Quaternion[] _pipRot = new Quaternion[PipMax];
        Transform _reticle;
        Material _reticleMat;
        float _reticleOn, _reticleGoal, _reticlePop;
        int _pipCount;
        Pitch _pipOwner;
        static readonly Color PipCol = new Color(1f, 0.784f, 0.239f);
        static readonly Color MuzzleRingCol = new Color(1f, 0.74f, 0.25f);
        static readonly Color StandDay = new Color(0x54 / 255f, 0x74 / 255f, 0xad / 255f), StandNight = new Color(0.12f, 0.16f, 0.3f);
        static readonly Color RoofDay = new Color(0.55f, 0.6f, 0.72f), RoofNight = new Color(1.6f, 1.25f, 0.8f);
        static readonly Color HazeDay = new Color(1f, 0.62f, 0.45f) * 0.16f, HazeNight = new Color(0.25f, 0.33f, 0.62f) * 0.05f;
        static readonly Color LedDay = new Color(1f, 0.62f, 0.25f) * 0.55f, LedNight = new Color(1f, 0.7f, 0.35f) * 0.85f;

        // net, night, batter
        Material _netMat;
        float _netHit;
        Material _batterMat, _cardMat;
        Material[] _flareMats, _poolMats, _blinkMats, _winMats;
        int _skyWindowCount;
        Renderer[] _blinkers;
        Material _glareMat, _ledMat;
        float _nightK, _nightGoal;
        Transform _batter;
        float _batterHop, _batterSlump;
        TrailHandle _batTrail;
        float _batTrailHold;
        Mesh _glowQuad;

        static readonly int IdIntensity = Shader.PropertyToID("_Intensity"), IdColor = Shader.PropertyToID("_Color"),
            IdSwayTime = Shader.PropertyToID("_SwayTime"), IdHit = Shader.PropertyToID("_Hit"), IdSway = Shader.PropertyToID("_Sway");

        Vector3 _netC; float _netA0, _netArc;

        /// Aim the net's bulge at where a ball went in (the arc's uv: x around, y up).
        void NetHitAt(Vector3 p)
        {
            var d = p - _netC;
            float u = Mathf.Clamp01((Mathf.Atan2(d.x, d.z) - _netA0) / _netArc), v = Mathf.Clamp01(p.y / 3.4f);
            _netMat.SetVector("_HitU", new Vector4(u, v, 0, 0));
        }

        static readonly Vector3[] TowerAt = { W(-13, 0, 3.5f), W(-13, 0, -9.5f) };
        const float BankY = 8.5f, BankYRight = 10.5f;
        // The right-hand tower behind the bowl, and two mid-ground towers standing behind the far stands (depth layers
        // between the bowl and the city; their heads light at night like the near ones).
        static readonly Vector3[] TowerRight = { new Vector3(13.5f, 0, 17.5f), new Vector3(-7.6f, 0, 23.2f), new Vector3(5.2f, 0, 24.4f) };
        static readonly float[] TowerRightH = { BankYRight, 10.2f, 10.8f };
        Material _hazeMat, _rowsMat, _roofMat, _turfMat;
        Material[] _beamMats = new Material[0];
        Renderer[] _beamR = new Renderer[0];
        Material[] _flareMatsR;

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

        const float MoundR = 1.95f, MoundH = 0.32f;
        static float Smooth01(float k) => k * k * (3 - 2 * k);
        /// Worn grass: the turf a shade darker and drier (scuffed, not lit), as a ratio over the dirt material. It must
        /// never read lighter than the turf: a pale band round the dirt read as a glow halo.
        static Color Worn(Color toGrass) => new Color(toGrass.r * 0.95f, toGrass.g * 0.86f, toGrass.b * 0.72f, 1);
        /// A cut's edge: a crisp, slightly raised rim of packed dirt (a low bevelled ring, darker on its outer face).
        static void Lip(MeshBuilder b, Vector3 centre, float r, float y, int seg)
        {
            int s0 = b.Count;
            b.Surface(seg, 3, (u, v) =>
            {
                float a = u * Mathf.PI * 2;
                float rr = r - 0.1f + v * 0.16f, h = 0.035f * Mathf.Sin(v * Mathf.PI);
                var n = new Vector3(Mathf.Cos(a) * (0.5f - v) * 0.9f, 1, Mathf.Sin(a) * (0.5f - v) * 0.9f).normalized;
                return (new Vector3(Mathf.Cos(a) * rr, h, Mathf.Sin(a) * rr), n);
            }, Matrix4x4.Translate(centre + Vector3.up * (y + 0.002f)), Color.white, new Vector2(6, 0.2f));
            for (int i = s0; i < b.Count; i++)
            {
                float k = Mathf.InverseLerp(r - 0.1f, r + 0.06f, new Vector2(b.P[i].x - centre.x, b.P[i].z - centre.z).magnitude);
                b.C[i] = Color.white * Mathf.Lerp(1.12f, 0.8f, k);
            }
        }
        /// Cheap, deterministic value noise for ragged edges (no allocation; build time only).
        static float Hash1(float x) { float s = Mathf.Sin(x * 127.1f) * 43758.5453f; return s - Mathf.Floor(s); }
        static float Noise1(float x) { float i = Mathf.Floor(x), f = x - i; f = f * f * (3 - 2 * f); return Mathf.Lerp(Hash1(i), Hash1(i + 1), f); }
        /// A worn ring whose outer edge wanders (scuffed grass is never a clean circle), vertex colour fading to the turf.
        static void WornRing(MeshBuilder b, Vector3 centre, float r0, float r1, float y, Color inner, Color outer, int seg, float seed)
        {
            int start = b.Count;
            b.Surface(seg, 3, (u, v) =>
            {
                float a = u * Mathf.PI * 2;
                float wob = (Noise1(u * 13 + seed) - 0.5f) * 0.5f + (Noise1(u * 37 + seed * 3) - 0.5f) * 0.18f;
                float r = Mathf.Lerp(r0, r1 + wob, v);
                return (new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r), Vector3.up);
            }, Matrix4x4.Translate(centre + Vector3.up * y), Color.white, new Vector2(5, 0.4f));
            for (int i = start; i < b.Count; i++)
            {
                int row = (i - start) / (seg + 1);   // Surface writes rows of (seg + 1) vertices, u fastest
                float k = row / 3f;
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
            var dirtMat = EnvKit.Surface(_env, Color.white, "sk-dirt", new Vector2(3.4f, 3.4f), rim: 0.25f, pulse: 0.05f, mapStrength: 0.9f);   // finer, firmer grain
            Look.Track(dirtMat, PC.Dirt);
            var b = new MeshBuilder();
            var cut = PlatePos + new Vector3(-0.2f, 0, 0); cut.y = 0;
            b.Disc(2.55f, 56, Matrix4x4.Translate(cut + Vector3.up * 0.02f), Color.white, 2.2f);
            // A crisp cut edge with a thin lighter lip, then worn, yellowed grass fading out into the turf.
            Lip(b, cut, 2.55f, 0.021f, 72);
            WornRing(b, cut, 2.6f, 3.25f, 0.019f, Worn(toGrass), toGrass, 72, 3.1f);
            // Wear down the baselines: a scuffed strip beside each foul line for the first few metres from the plate.
            foreach (var sgn in new[] { -1f, 1f })
            {
                var from = new Vector3(PlatePos.x - 0.25f, 0, PlatePos.z);
                var dir = new Vector3(-Mathf.Cos(Mathf.PI / 4), 0, sgn * Mathf.Sin(Mathf.PI / 4));
                var side = new Vector3(-dir.z, 0, dir.x);
                int s0 = b.Count;
                b.Surface(12, 2, (u, v) => (from + dir * (2.4f + u * 5.5f) + side * ((v - 0.5f) * 0.7f) + Vector3.up * 0.018f, Vector3.up), Matrix4x4.identity, Color.white, new Vector2(3, 0.3f));
                for (int i = s0; i < b.Count; i++)
                {
                    var q = b.P[i] - from; float along = Vector3.Dot(q, dir), across = Mathf.Abs(Vector3.Dot(q, side));
                    float k = Mathf.Clamp01((along - 2.4f) / 5.5f);
                    float w = Mathf.Clamp01(across / 0.35f);
                    b.C[i] = Color.Lerp(Worn(toGrass), toGrass, Mathf.Max(k * k, w));
                }
            }
            var mound = new Vector3(MachinePos.x + 0.3f, 0, MachinePos.z);
            // A raised dome (flat-ish top for the cart) so PITCH-O stands ON a mound, darker where it is packed.
            int moundStart = b.Count;
            b.Surface(40, 10, (u, v) =>
            {
                float a = u * Mathf.PI * 2, r = (1 - v) * MoundR;
                float h = MoundH * Smooth01(Mathf.InverseLerp(MoundR, 0.85f, r));
                float dh = MoundH * 1.6f / MoundR;   // rough slope for the normal on the flank
                var nrm = r > 0.85f && r < MoundR ? new Vector3(Mathf.Cos(a) * dh * 0.5f, 1, Mathf.Sin(a) * dh * 0.5f).normalized : Vector3.up;
                return (new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r), nrm);
            }, Matrix4x4.Translate(mound + Vector3.up * 0.015f), Color.white, new Vector2(4, 1.2f));
            for (int i = moundStart; i < b.Count; i++)   // the flank a shade darker than the packed top: reads as a dome
            {
                float r = new Vector2(b.P[i].x - mound.x, b.P[i].z - mound.z).magnitude;
                float k = Mathf.InverseLerp(0.85f, MoundR, r);
                b.C[i] = Color.white * Mathf.Lerp(1.08f, 0.78f, k);
            }
            Lip(b, mound, MoundR, 0.016f, 56);
            WornRing(b, mound, MoundR + 0.05f, MoundR + 0.6f, 0.012f, Worn(toGrass), toGrass, 56, 7.7f);
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
            // The pitching rubber: a white slab on the mound's flat top, in front of PITCH-O's cart (toward the plate).
            var rubber = new Vector3(MachinePos.x + 1.05f, MachineRootY + 0.02f, MachinePos.z - 0.05f);
            b.Box(Matrix4x4.TRS(rubber, Quaternion.identity, new Vector3(0.16f, 0.05f, 0.62f)), white);
            EnvKit.Static(_env, "Plate", transform, b, plateMat);

            // Backstop behind the plate (+x): a chain-link arc with a top rail and three posts (web: r 3.3, h 3.4).
            var bc = new Vector3(PlatePos.x + 0.4f, 1.7f, PlatePos.z + 0.6f);
            float a0 = Mathf.PI * 0.32f, arc = Mathf.PI * 0.5f;
            _netC = bc; _netA0 = a0; _netArc = arc;
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
                BuildTrack(crowdO);
                BuildStandsDetail(crowdO);
            }

            // Night lights: tower-head flares, turf pools under the floods (both on at zero from Load: prewarmed).
            _flareMats = new Material[TowerAt.Length];
            for (int i = 0; i < TowerAt.Length; i++)
                _flareMats[i] = Glow("TowerFlare" + i, transform, TowerAt[i] + new Vector3(0.25f, BankY, 0), i == 0 ? 2.0f : 3.2f, Hex(0xfff3d0), true, null, i == 0 ? 3.4f : 2.6f);   // the near head is small and tight: it sits by the score
            var flat = Quaternion.Euler(90, 0, 0);
            _poolMats = new[]
            {
                Glow("PoolPlate", transform, new Vector3(PlatePos.x - 0.3f, 0.06f, PlatePos.z), 7.6f, Hex(0xffe2b8), false, flat, 0.3f),
                Glow("PoolMound", transform, new Vector3(MachinePos.x + 0.3f, 0.37f, MachinePos.z), 5.8f, Hex(0xffe2b8), false, flat, 0.3f),
            };
            // The low sun behind the skyline (day): a wide soft glare the bloom carries into the arches.
            _glareMat = Glow("SunGlare", transform, new Vector3(30, 7, 92), 70, Hex(0xffc98a), true, null, 1.2f);
            BuildSkylineDressing();
            BuildAtmosphere();
            BuildPropTrim();
        }

        // The inner face of the authored low wall on the machine side (sk-setedge), one point per segment joint.
        static readonly Vector2[] SideWall =
        {
            new Vector2(-13.15f, 0.94f), new Vector2(-13.14f, -0.97f), new Vector2(-13.04f, -1.91f), new Vector2(-12.87f, -2.84f),
            new Vector2(-12.63f, -3.76f), new Vector2(-12.34f, -4.65f), new Vector2(-11.96f, -5.52f), new Vector2(-11.54f, -6.36f),
            new Vector2(-11.05f, -7.17f), new Vector2(-10.51f, -7.95f), new Vector2(-9.91f, -8.68f), new Vector2(-9.26f, -9.37f),
            new Vector2(-8.56f, -10.01f), new Vector2(-7.83f, -10.6f), new Vector2(-7.05f, -11.13f),
        };

        /// Trim and decals on the props that read as primitives: an ad panel and bolts on each segment of the side wall,
        /// a stencil plate and corner caps on the crate stack beside the machine, and on PITCH-O's cart (BuildMachine).
        void BuildPropTrim()
        {
            // Ad panels: each wall segment shows a different slice of the ad-board sheet, inset from the segment's ends.
            var adMat = EnvKit.Surface(_env, Color.white, "sk-adboard", Vector2.one, rim: 0.2f, pulse: 0.12f, lift: 0.22f, bands: 2, mapStrength: 1);
            adMat.SetColor("_BaseColor", new Color(0.8f, 0.8f, 0.86f));
            var ad = new MeshBuilder();
            var trim = new MeshBuilder();
            Color bolt = new Color(0.75f, 0.78f, 0.86f), seam = new Color(0.25f, 0.27f, 0.35f);
            for (int i = 0; i + 1 < SideWall.Length; i++)
            {
                Vector3 p0 = new Vector3(SideWall[i].x, 0, SideWall[i].y), p1 = new Vector3(SideWall[i + 1].x, 0, SideWall[i + 1].y);
                var along = (p1 - p0); float len = along.magnitude; along /= len;
                var n = new Vector3(along.z, 0, -along.x);   // toward the field (+x side)
                if (n.x < 0) n = -n;
                Vector3 a0 = p0 + along * 0.12f + n * 0.025f, a1 = p1 - along * 0.12f + n * 0.025f;
                const float Y0 = 0.32f, Y1 = 0.86f;
                float u0 = (i * 0.137f) % 1f, u1 = u0 + 0.13f;
                int o = ad.Count;
                ad.P.Add(a0 + Vector3.up * Y0); ad.P.Add(a0 + Vector3.up * Y1); ad.P.Add(a1 + Vector3.up * Y1); ad.P.Add(a1 + Vector3.up * Y0);
                ad.UV.Add(new Vector2(u0, 0)); ad.UV.Add(new Vector2(u0, 1)); ad.UV.Add(new Vector2(u1, 1)); ad.UV.Add(new Vector2(u1, 0));
                for (int k = 0; k < 4; k++) { ad.N.Add(n); ad.C.Add(Color.white); }
                ad.T.Add(o); ad.T.Add(o + 2); ad.T.Add(o + 1); ad.T.Add(o); ad.T.Add(o + 3); ad.T.Add(o + 2);
                ad.T.Add(o); ad.T.Add(o + 1); ad.T.Add(o + 2); ad.T.Add(o); ad.T.Add(o + 2); ad.T.Add(o + 3);   // both windings: no guessing the face
                // a frame round the panel and bolts at its corners, a dark seam at the joint
                var q = Quaternion.LookRotation(n);
                var mid = (a0 + a1) * 0.5f + n * 0.005f;
                trim.Box(Matrix4x4.TRS(mid + Vector3.up * (Y1 + 0.03f), q, new Vector3(len - 0.2f, 0.04f, 0.03f)), bolt);
                trim.Box(Matrix4x4.TRS(mid + Vector3.up * (Y0 - 0.03f), q, new Vector3(len - 0.2f, 0.04f, 0.03f)), bolt);
                foreach (var c in new[] { a0, a1 })
                    foreach (var y in new[] { 0.14f, 1.02f })
                        trim.Sphere(0.035f, 6, 4, Matrix4x4.Translate(c + n * 0.01f + Vector3.up * y), bolt, Vector3.one);
                trim.Box(Matrix4x4.TRS(p0 + n * 0.02f + Vector3.up * 0.58f, q, new Vector3(0.05f, 1.12f, 0.03f)), seam);
            }
            EnvKit.Static(_env, "SideWallAds", transform, ad, adMat).receiveShadows = false;

            // The crate stack by the machine (x -12.4..-10.5, z 1.5..3.0, 1.8 high): a stencil plate and corner caps.
            Color cream = Lin(0xe9dcc0), dark = Lin(0x1a1f33), yellow = Lin(0xffc93d);
            var face = new Vector3(-11.45f, 0, 1.49f);
            trim.Box(Matrix4x4.TRS(face + new Vector3(0, 0.82f, 0), Quaternion.identity, new Vector3(1.1f, 0.42f, 0.02f)), cream);
            for (int k = 0; k < 3; k++)   // stencil bars: reads as a crate number at this distance
                trim.Box(Matrix4x4.TRS(face + new Vector3(-0.3f + k * 0.3f, 0.82f, -0.012f), Quaternion.identity, new Vector3(k == 1 ? 0.08f : 0.18f, 0.26f, 0.01f)), dark);
            for (int k = 0; k < 6; k++)   // a hazard band along the foot
                trim.Box(Matrix4x4.TRS(face + new Vector3(-0.75f + k * 0.3f, 0.12f, -0.005f), Quaternion.Euler(0, 0, 35), new Vector3(0.1f, 0.26f, 0.015f)), k % 2 == 0 ? yellow : dark);
            foreach (var x in new[] { -12.38f, -10.52f })
                foreach (var y in new[] { 0.05f, 1.56f })
                    trim.Box(Matrix4x4.TRS(new Vector3(x, y, 1.53f), Quaternion.identity, new Vector3(0.14f, 0.12f, 0.14f)), bolt);
            var trimMat = EnvKit.Surface(_env, Color.white, null, null, rim: 0.8f, pulse: 0.06f, bands: 3);
            EnvKit.Static(_env, "PropTrim", transform, trim, trimMat).receiveShadows = true;
        }

        /// The bowl gets rows: a dark riser band and a pale seat nosing under every row of fans, lighter step nosings
        /// up each aisle, a facade with a roof lip along the top, and a tone that falls off from the near-left stand
        /// (brightest, most saturated) to the far bowl. Overlays just inside the bowl's surface plus a recolour of the
        /// env kit's bowl mesh: EnvCrowd itself is unchanged.
        void BuildStandsDetail(EnvCrowd.Options o)
        {
            float a0 = -o.BowlArc / 2, arc = o.BowlArc, cz = o.Center.z;
            float H = o.BowlHeight, y0 = o.BowlY - H / 2;
            float YAt(float r) => y0 + (r - o.BowlBottom) / (o.BowlTop - o.BowlBottom) * H;
            float Tone(float a) { float k = Mathf.Clamp01((a - a0) / arc); return Mathf.Lerp(1.12f, 0.8f, Smooth01(Mathf.Clamp01(k * 1.6f))); }

            // recolour the kit's bowl by angle (the wall below keeps its own shade, scaled the same way)
            var stands = _env.Crowd.Root.transform.Find("Stands");
            var mf = stands ? stands.GetComponent<MeshFilter>() : null;
            if (mf && mf.sharedMesh && mf.sharedMesh.isReadable)
            {
                var m = mf.sharedMesh; var vs = m.vertices; var cs = m.colors;
                if (cs.Length == vs.Length)
                {
                    for (int i = 0; i < vs.Length; i++)
                    {
                        float a = Mathf.Atan2(vs[i].x, vs[i].z - cz);
                        float t = Tone(a);
                        cs[i] = new Color(cs[i].r * t, cs[i].g * t, cs[i].b * Mathf.Lerp(1, t, 0.7f), 1);
                    }
                    m.colors = cs;
                }
            }

            var mat = _rowsMat = EnvKit.Surface(_env, Hex(0x5474ad), null, null, rim: 0.35f, pulse: 0.04f, bands: 3, twoSided: true);
            var b = new MeshBuilder();
            void Band(float r0, float r1, float inset, float c0, float aA, float aArc, int seg)
            {
                int s0 = b.Count;
                b.Shell(r1 - inset, r0 - inset, YAt(r1) - YAt(r0), seg, aA, aArc, Matrix4x4.Translate(new Vector3(0, (YAt(r0) + YAt(r1)) / 2, cz)), Color.white, Vector2.one, inward: true);
                for (int i = s0; i < b.Count; i++)
                {
                    float a = Mathf.Atan2(b.P[i].x, b.P[i].z - cz), t = Tone(a) * c0;
                    b.C[i] = new Color(t, t, t * 1.04f, 1);
                }
            }
            for (int k = 0; ; k++)
            {
                float r = o.Radius + k * o.RowDepth;
                if (r > o.BowlTop - 0.3f) break;
                Band(r - 0.55f, r - 0.22f, 0.05f, 0.6f, a0, arc, 56);    // riser shadow
                Band(r - 0.22f, r - 0.12f, 0.06f, 1.45f, a0, arc, 56);   // seat nosing
                for (int i = 1; i < 10; i++)                             // aisle step nosings
                {
                    float a = a0 + arc * i / 10f;
                    for (int st = 0; st < 2; st++)
                    {
                        float rs = r - 0.55f + st * 0.6f;
                        Band(rs, rs + 0.07f, 0.07f, 1.7f, a - 0.012f, 0.024f, 1);
                    }
                }
            }
            // facade along the top of the bowl, a pale lip on it
            float topY = YAt(o.BowlTop);
            int f0 = b.Count;
            b.Shell(o.BowlTop + 0.05f, o.BowlTop + 0.05f, 1.0f, 56, a0, arc, Matrix4x4.Translate(new Vector3(0, topY + 0.4f, cz)), Color.white, Vector2.one, inward: true);
            for (int i = f0; i < b.Count; i++) { float t = Tone(Mathf.Atan2(b.P[i].x, b.P[i].z - cz)) * 0.42f; b.C[i] = new Color(t, t, t * 1.1f, 1); }
            var lip = new Vector3[57];
            for (int i = 0; i < lip.Length; i++)
            {
                float a = a0 + arc * i / (lip.Length - 1);
                lip[i] = new Vector3(Mathf.Sin(a) * (o.BowlTop - 0.05f), topY + 0.92f, cz + Mathf.Cos(a) * (o.BowlTop - 0.05f));
            }
            b.Tube(lip, 0.11f, 6, Matrix4x4.identity, new Color(1.7f, 1.7f, 1.75f));
            var r0r = EnvKit.Static(_env, "StandRows", transform, b, mat);
            r0r.receiveShadows = false;

            // Low quality trims the crowd to a share of the figures; flat fan cards fill the seats it leaves empty (one
            // static mesh drawn in Load, in the crowd's palette, tinted with it at night). Nothing on High.
            float gap = 1 - RenderQuality.CrowdBudget;
            if (gap > 0.01f)
            {
                _cardMat = EnvKit.Surface(_env, Color.white, null, null, rim: 0.3f, pulse: 0.03f, bands: 2, twoSided: true);
                var cb = new MeshBuilder();
                var rng = new System.Random(0x5ea7);
                // muted silhouettes (a bean body and a head), not bright rectangles: they read as a crowd in the dark seats
                Color[] fans = { Lin(0x6c5a86), Lin(0x4f6a86), Lin(0x86566a), Lin(0x76766f), Lin(0x5a5f8c) };
                const int Seg = 70;
                for (int k = 0; ; k++)
                {
                    float r = o.Radius + k * o.RowDepth;
                    if (r > o.BowlTop - 0.5f) break;
                    for (int i = 0; i < Seg; i++)
                    {
                        if (rng.NextDouble() > gap * 0.9f) continue;
                        float a = a0 + arc * (i + 0.5f) / Seg;
                        float t = Tone(a) * 0.7f;
                        var c = fans[rng.Next(fans.Length)];
                        var col = new Color(c.r * t, c.g * t, c.b * t, 1);
                        var at = new Vector3(Mathf.Sin(a) * (r - 0.3f), YAt(r) + 0.2f, cz + Mathf.Cos(a) * (r - 0.3f));
                        float h = 0.9f + (float)rng.NextDouble() * 0.25f;
                        cb.Sphere(0.24f, 8, 5, Matrix4x4.Translate(at + Vector3.up * 0.28f * h), col, new Vector3(1f, 1.35f * h, 0.8f));
                        cb.Sphere(0.15f, 8, 5, Matrix4x4.Translate(at + Vector3.up * 0.78f * h), col, Vector3.one);
                    }
                }
                if (cb.Count > 0) EnvKit.Static(_env, "StandCards", transform, cb, _cardMat).receiveShadows = false;
            }

            // Section dividers: low concrete walls up three of the aisles, splitting the bowl into sections, with a pale cap.
            var db = new MeshBuilder();
            foreach (int ai in new[] { 2, 5, 8 })
            {
                float a = a0 + arc * ai / 10f + 0.016f;
                for (int side = 0; side < 2; side++)
                {
                    int d0 = db.Count;
                    float aa = a + (side == 0 ? -0.0025f : 0.0025f);
                    db.Surface(1, 6, (u, v) =>
                    {
                        float rr = Mathf.Lerp(o.BowlBottom + 0.1f, o.BowlTop - 0.1f, v);
                        float yy = YAt(rr) + u * 0.55f;
                        return (new Vector3(Mathf.Sin(aa) * rr, yy, cz + Mathf.Cos(aa) * rr), new Vector3(Mathf.Cos(aa), 0, -Mathf.Sin(aa)) * (side == 0 ? -1 : 1));
                    }, Matrix4x4.identity, Color.white, Vector2.one, side == 1);
                    for (int i = d0; i < db.Count; i++) { float t = Tone(a) * 0.55f; db.C[i] = new Color(t, t, t * 1.08f, 1); }
                }
                var cap = new Vector3[8];
                for (int i = 0; i < cap.Length; i++)
                {
                    float rr = Mathf.Lerp(o.BowlBottom + 0.1f, o.BowlTop - 0.1f, i / 7f);
                    cap[i] = new Vector3(Mathf.Sin(a) * rr, YAt(rr) + 0.57f, cz + Mathf.Cos(a) * rr);
                }
                db.Tube(cap, 0.05f, 4, Matrix4x4.identity, new Color(1.5f, 1.5f, 1.55f));
            }
            EnvKit.Static(_env, "StandDividers", transform, db, mat).receiveShadows = false;

            // Aisle handrails: a steel rail on posts up the middle of every aisle.
            var steel = EnvKit.Surface(_env, Hex(0xc9d2e6), null, null, rim: 1.0f, pulse: 0.05f, bands: 2);
            var hb = new MeshBuilder();
            for (int ai = 1; ai < 10; ai++)
            {
                float a = a0 + arc * ai / 10f;
                var rail = new Vector3[10];
                for (int i = 0; i < rail.Length; i++)
                {
                    float rr = Mathf.Lerp(o.BowlBottom + 0.4f, o.BowlTop - 0.4f, i / (float)(rail.Length - 1));
                    rail[i] = new Vector3(Mathf.Sin(a) * rr, YAt(rr) + 0.82f, cz + Mathf.Cos(a) * rr);
                }
                hb.Tube(rail, 0.022f, 4, Matrix4x4.identity, Color.white);
                for (int i = 0; i < rail.Length; i += 3) hb.Tube(new[] { rail[i] - Vector3.up * 0.82f, rail[i] }, 0.016f, 4, Matrix4x4.identity, Color.white);
            }
            EnvKit.Static(_env, "AisleRails", transform, hb, steel).receiveShadows = false;

            // The roof edge: a lit strip under the facade's lip with lamp boxes along it (pale by day, warm and glowing at night).
            _roofMat = EnvKit.Surface(_env, RoofDay, null, null, rim: 0f, pulse: 0.15f, lift: 1.1f, bands: 1);
            _roofMat.SetFloat("_ShadowRecv", 0);
            var lb = new MeshBuilder();
            var strip = new Vector3[57];
            for (int i = 0; i < strip.Length; i++)
            {
                float a = a0 + arc * i / (strip.Length - 1);
                strip[i] = new Vector3(Mathf.Sin(a) * (o.BowlTop - 0.12f), topY + 0.74f, cz + Mathf.Cos(a) * (o.BowlTop - 0.12f));
            }
            lb.Tube(strip, 0.045f, 4, Matrix4x4.identity, Color.white);
            for (int i = 2; i < 56; i += 4)
            {
                float a = a0 + arc * i / 56f;
                var at = new Vector3(Mathf.Sin(a) * (o.BowlTop - 0.2f), topY + 1.12f, cz + Mathf.Cos(a) * (o.BowlTop - 0.2f));
                lb.Box(Matrix4x4.TRS(at, Quaternion.Euler(0, a * Mathf.Rad2Deg, 0), new Vector3(0.5f, 0.22f, 0.14f)), Color.white);
            }
            EnvKit.Static(_env, "RoofEdge", transform, lb, _roofMat).receiveShadows = false;
        }

        /// The warning track's edge (replaces the env kit's full-circle glow ring): a padded navy bumper at the foot of
        /// the wall with a run of warm LED segments in it, and a padded cap with a thin warm trim on top of the wall.
        /// Both stop behind the backstop net instead of running on round the field.
        void BuildTrack(EnvCrowd.Options o)
        {
            const float A1 = 55 * Mathf.Deg2Rad;   // the right end, hidden behind the net
            float a0 = -o.BowlArc / 2, arc = A1 - a0;
            float rWall = o.WallRadius - 0.06f, wallTop = o.WallY + o.WallHeight / 2;
            var cz = o.Center.z;
            Vector3 At(float a, float r, float y) => new Vector3(Mathf.Sin(a) * r, y, cz + Mathf.Cos(a) * r);

            var pad = EnvKit.Surface(_env, Hex(0x1d2a5c), null, null, rim: 0.7f, pulse: 0.05f, bands: 3);
            var b = new MeshBuilder();
            const int N = 64;
            var foot = new Vector3[N + 1]; var cap = new Vector3[N + 1];
            for (int i = 0; i <= N; i++)
            {
                float a = a0 + arc * i / N;
                foot[i] = At(a, rWall - 0.16f, 0.16f);
                cap[i] = At(a, rWall - 0.04f, wallTop + 0.47f);
            }
            b.Tube(foot, 0.17f, 8, Matrix4x4.identity, Color.white);
            b.Tube(cap, 0.07f, 6, Matrix4x4.identity, new Color(0.8f, 0.82f, 0.9f));
            EnvKit.Static(_env, "TrackPad", transform, b, pad).receiveShadows = false;

            // LED segments: short warm bars on the bumper's face, gaps between them, below the bloom threshold.
            _ledMat = EnvKit.Surface(_env, Color.white, null, null, rim: 0f, pulse: 0.35f, lift: 1.1f, bands: 1);
            _ledMat.SetFloat("_ShadowRecv", 0);
            b = new MeshBuilder();
            float len = arc * (rWall - 0.33f);
            int segs = Mathf.RoundToInt(len / 0.62f);
            for (int i = 0; i < segs; i++)
            {
                float a = a0 + arc * (i + 0.5f) / segs;
                var p = At(a, rWall - 0.33f, 0.17f);
                var inward = (new Vector3(0, 0.17f, cz) - p); inward.y = 0;
                b.Box(Matrix4x4.TRS(p, Quaternion.LookRotation(-inward.normalized), new Vector3(0.42f, 0.07f, 0.02f)), Color.white);
            }
            // the cap's trim: a thin warm line just under the padded cap
            var trim = new Vector3[N + 1];
            for (int i = 0; i <= N; i++) trim[i] = At(a0 + arc * i / N, rWall - 0.11f, wallTop + 0.38f);
            b.Tube(trim, 0.018f, 4, Matrix4x4.identity, new Color(0.7f, 0.7f, 0.7f));
            EnvKit.Static(_env, "TrackLeds", transform, b, _ledMat).receiveShadows = false;
        }

        /// A warm additive copy of one skyline triangle (the sun wash): `edge` brightens it toward its right edge and top.
        static void AddWash(MeshBuilder w, Vector3[] P, int a, int b, int c, Vector3 n, float gain, bool edge)
        {
            float xmin = Mathf.Min(P[a].x, Mathf.Min(P[b].x, P[c].x)), xmax = Mathf.Max(P[a].x, Mathf.Max(P[b].x, P[c].x));
            int o = w.Count;
            foreach (int k in new[] { a, b, c })
            {
                float hk = Mathf.Clamp01(P[k].y / 27f);
                float ek = edge ? Mathf.InverseLerp(xmin, xmax + 0.01f, P[k].x) : 1;
                w.P.Add(P[k] + n * 0.03f); w.N.Add(n); w.UV.Add(Vector2.zero);
                float g = gain * (0.12f + 0.88f * ek * ek) * (0.35f + 0.65f * hk);
                w.C.Add(new Color(1f, 0.55f, 0.28f) * g);
            }
            w.T.Add(o); w.T.Add(o + 1); w.T.Add(o + 2);
        }

        /// Air between the bowl and the city: a wide, flat additive haze band low over the far stands (sky-tinted,
        /// warm at dusk, cool at night), so the skyline sits back. And the right-hand light towers behind the bowl,
        /// so the night has lit floodlight heads on both sides.
        void BuildAtmosphere()
        {
            var hz = _env.Mat(EnvKit.GlowMat);
            hz.SetColor(IdColor, Color.white); hz.SetFloat(IdIntensity, 0); hz.SetFloat("_Billboard", 0); hz.SetFloat("_Core", 0.9f);
            var r = EnvKit.Renderer("HazeBand", transform, GlowQuad(), hz);
            r.receiveShadows = false;
            r.transform.localPosition = new Vector3(0, 5.2f, 27);
            r.transform.localScale = new Vector3(120, 13, 1);
            _hazeMat = hz;

            var towerMat = EnvKit.Surface(_env, Hex(0x262d3f), null, null, rim: 0.6f, pulse: 0.04f);
            var shafts = new MeshBuilder(); var banks = new MeshBuilder();
            for (int ti = 0; ti < TowerRight.Length; ti++)
            {
                var at = TowerRight[ti]; float hy = TowerRightH[ti];
                shafts.Shell(0.18f, 0.32f, hy, 8, 0, Mathf.PI * 2, Matrix4x4.Translate(at + new Vector3(0, hy / 2, 0)), Color.white, Vector2.one);
                var face = Quaternion.LookRotation((new Vector3(PlatePos.x, 0, PlatePos.z) - at).normalized);
                banks.Box(Matrix4x4.TRS(at + new Vector3(0, hy, 0), face, new Vector3(1.7f, 1.0f, 0.28f)), Color.white);
                shafts.Box(Matrix4x4.TRS(at + new Vector3(0, hy - 0.62f, 0), face, new Vector3(1.9f, 0.08f, 0.5f)), Color.white);
                // a lattice: cross braces up the shaft, so the far towers read as structures, not poles
                for (float y = 1.5f; y < hy - 1.2f; y += 1.6f)
                    shafts.Box(Matrix4x4.TRS(at + new Vector3(0, y, 0), face * Quaternion.Euler(0, 0, (y % 3.2f) < 1.6f ? 35 : -35), new Vector3(0.9f, 0.06f, 0.06f)), Color.white);
            }
            EnvKit.Static(_env, "TowerShaftsR", transform, shafts, towerMat);
            EnvKit.Static(_env, "TowerBanksR", transform, banks, _bankMat);
            // Faint floodlight beams from the far towers' heads down onto the field (night only; off by day). Cones, like the
            // near towers' (EnvSpotlights), but static and narrow: far away they cover little of the screen.
            Vector3[] beamTo = { new Vector3(PlatePos.x, 0, PlatePos.z + 1), new Vector3(MachinePos.x + 1, 0, MachinePos.z), new Vector3(0, 0, -1) };
            _beamMats = new Material[TowerRight.Length]; _beamR = new Renderer[TowerRight.Length];
            for (int i = 0; i < TowerRight.Length; i++)
            {
                var head = TowerRight[i] + new Vector3(0, TowerRightH[i] - 0.4f, -0.3f);
                var d = beamTo[i] - head;
                var cb = new MeshBuilder();
                cb.Cone(i == 0 ? 2.0f : 2.6f, d.magnitude, 18, Matrix4x4.identity, Color.white);
                var m = _beamMats[i] = _env.Mat(EnvKit.BeamMat);
                m.SetColor(IdColor, Hex(0xfff0d6)); m.SetFloat(IdIntensity, 0);
                var br = EnvKit.Renderer("FarBeam" + i, transform, _env.Own(cb.ToMesh("FarBeam")), m);
                br.receiveShadows = false;
                _beamR[i] = br;
                br.transform.localPosition = head;
                br.transform.localRotation = Quaternion.FromToRotation(Vector3.down, d.normalized);
            }
            _flareMatsR = new Material[TowerRight.Length];
            for (int i = 0; i < TowerRight.Length; i++)
                _flareMatsR[i] = Glow("TowerFlareR" + i, transform, TowerRight[i] + new Vector3(0, TowerRightH[i], -0.3f), i == 0 ? 3.2f : 2.6f, Hex(0xfff3d0), true, null, 2.8f);
        }

        /// The authored skyline's dressing, built from its own faces at load:
        ///   windows   grids of lit panes on every camera-facing facade, in two additive draws: a sparse set that is
        ///             on at dusk, and the rest that fills in, warm and dense, at night;
        ///   sun wash  a warm additive copy of the facades that face the low sun (+x) and the roofs: a rim light at
        ///             dusk that goes out with the sun;
        ///   beacons   three red aviation lights on the tallest roofs, blinking day and night (brighter at night).
        void BuildSkylineDressing()
        {
            _winMats = new Material[3];
            for (int i = 0; i < 3; i++)
            {
                var m = _winMats[i] = _env.Mat(EnvKit.GlowAddMat);
                m.SetColor(IdColor, Color.white); m.SetFloat(IdIntensity, 0); m.SetFloat("_FogMul", 0.3f);
            }
            _blinkMats = new[] { _env.Mat(EnvKit.GlowMat), _env.Mat(EnvKit.GlowMat) };
            foreach (var m in _blinkMats) { m.SetColor(IdColor, Hex(0xff3b30)); m.SetFloat(IdIntensity, 0); m.SetFloat("_Billboard", 1); m.SetFloat("_Core", 3); }
            var sky = _env.Backdrop?.Root ? _env.Backdrop.Root.transform.Find("Skyline") : null;
            if (sky == null || !EnvBytesMesh.Read("sk-skyline", out var P, out var N, out var I)) { _blinkers = new Renderer[0]; return; }

            // Facades that face the camera (the towers stand at angles, so many face it diagonally), grouped by plane;
            // panes are laid out on each plane in its own (across, up) coordinates.
            var planes = new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<int>>();
            var sparse = new MeshBuilder(); var dense = new MeshBuilder(); var wash = new MeshBuilder();
            for (int t = 0; t < I.Length; t += 3)
            {
                int a = I[t], b = I[t + 1], c = I[t + 2];
                var nn = (N[a] + N[b] + N[c]).normalized;
                if (Mathf.Abs(nn.y) > 0.3f) { if (nn.y > 0.9f) AddWash(wash, P, a, b, c, nn, 0.3f, false); continue; }
                if (nn.z < -0.35f)
                {
                    long key = ((long)Mathf.RoundToInt(nn.x * 50) * 1000 + Mathf.RoundToInt(nn.z * 50)) * 1000003L + Mathf.RoundToInt(Vector3.Dot(nn, P[a]) * 20);
                    if (!planes.TryGetValue(key, out var l)) planes[key] = l = new System.Collections.Generic.List<int>();
                    l.Add(t);
                    // the faces turned toward the low sun's side (+x): a warm rim wash, strongest up top and on the right edge
                    if (nn.x > 0.5f) AddWash(wash, P, a, b, c, nn, 1f, true);
                }
            }
            var rng = new System.Random(0x5c1e);
            const float CW = 1.15f, RH = 1.35f, PW = 0.62f, PH = 0.8f, M = 0.25f;
            Color warm = new Color(1f, 0.6f, 0.26f), cool = new Color(0.55f, 0.75f, 1f);
            foreach (var kv in planes)
            {
                var tris = kv.Value;
                var n0 = (N[I[tris[0]]] + N[I[tris[0] + 1]] + N[I[tris[0] + 2]]).normalized; n0.y = 0; n0.Normalize();
                var right = Vector3.Cross(Vector3.up, n0).normalized;   // across the facade
                var o0 = P[I[tris[0]]];
                float S(Vector3 q) => Vector3.Dot(q - o0, right);
                float smin = float.MaxValue, smax = float.MinValue, ymin = float.MaxValue, ymax = float.MinValue;
                foreach (int t in tris) for (int k = 0; k < 3; k++) { var q = P[I[t + k]]; smin = Mathf.Min(smin, S(q)); smax = Mathf.Max(smax, S(q)); ymin = Mathf.Min(ymin, q.y); ymax = Mathf.Max(ymax, q.y); }
                bool On(float sx, float sy)
                {
                    foreach (int t in tris)
                    {
                        Vector2 A = new Vector2(S(P[I[t]]), P[I[t]].y), B = new Vector2(S(P[I[t + 1]]), P[I[t + 1]].y), C = new Vector2(S(P[I[t + 2]]), P[I[t + 2]].y);
                        float d1 = (sx - B.x) * (A.y - B.y) - (A.x - B.x) * (sy - B.y);
                        float d2 = (sx - C.x) * (B.y - C.y) - (B.x - C.x) * (sy - C.y);
                        float d3 = (sx - A.x) * (C.y - A.y) - (C.x - A.x) * (sy - A.y);
                        if (!((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0))) return true;
                    }
                    return false;
                }
                int cols = Mathf.FloorToInt((smax - smin - 2 * M) / CW);
                if (cols < 1) continue;
                float s0 = (smin + smax) / 2 - (cols - 1) * CW / 2;
                float density = 0.45f + (float)rng.NextDouble() * 0.35f;
                bool coolBlock = rng.NextDouble() < 0.25;
                // rows: scan up the facade and take a row wherever a pane fits (the authored towers have recessed
                // floor bands, so a fixed grid would miss most floors)
                const float MY = 0.1f;
                bool Fits(float sx, float y) =>
                    On(sx - PW / 2 - M * 0.5f, y - PH / 2 - MY) && On(sx + PW / 2 + M * 0.5f, y - PH / 2 - MY) &&
                    On(sx - PW / 2 - M * 0.5f, y + PH / 2 + MY) && On(sx + PW / 2 + M * 0.5f, y + PH / 2 + MY);
                for (float y = Mathf.Max(ymin, 0) + 1.6f; y < ymax - 0.5f; )
                {
                    bool row = false;
                    for (int ci = 0; ci < cols; ci++)
                    {
                        float sx = s0 + ci * CW;
                        if (!Fits(sx, y)) continue;
                        row = true;
                        double r = rng.NextDouble();
                        if (r > density) continue;
                        var mb = r < density * 0.4 ? sparse : dense;
                        float k = 0.7f + (float)rng.NextDouble() * 0.5f;
                        var col = ((coolBlock ^ rng.NextDouble() < 0.12) ? cool : warm) * k;
                        var c0 = o0 + right * sx + Vector3.up * (y - o0.y) + n0 * 0.04f;
                        var hx = right * (PW / 2); var hy = Vector3.up * (PH / 2);
                        int o = mb.Count;
                        mb.P.Add(c0 - hx - hy); mb.P.Add(c0 - hx + hy); mb.P.Add(c0 + hx + hy); mb.P.Add(c0 + hx - hy);
                        for (int v = 0; v < 4; v++) { mb.N.Add(n0); mb.C.Add(col); mb.UV.Add(Vector2.zero); }
                        mb.T.Add(o); mb.T.Add(o + 1); mb.T.Add(o + 2); mb.T.Add(o); mb.T.Add(o + 2); mb.T.Add(o + 3);
                    }
                    y += row ? RH : 0.2f;
                }
            }
            var builders = new[] { sparse, dense, wash };
            string[] names = { "SkyWindowsDusk", "SkyWindowsNight", "SkySunWash" };
            for (int i = 0; i < 3; i++)
                if (builders[i].Count > 0)
                {
                    var r = EnvKit.Static(_env, names[i], sky, builders[i], _winMats[i]);
                    r.receiveShadows = false;
                }
            _skyWindowCount = sparse.Count / 4 + dense.Count / 4;

            // beacons: the three tallest roofs, far enough apart
            var tops = new System.Collections.Generic.List<Vector3>();
            var order = new System.Collections.Generic.List<Vector3>(P);
            order.Sort((u, v) => v.y.CompareTo(u.y));
            foreach (var v in order)
            {
                bool near = false;
                foreach (var t0 in tops) if (Mathf.Abs(t0.x - v.x) < 14) { near = true; break; }
                if (near) continue;
                tops.Add(v);
                if (tops.Count == 3) break;
            }
            var list = new System.Collections.Generic.List<Renderer>();
            for (int i = 0; i < tops.Count; i++)
            {
                var r = EnvKit.Renderer("Beacon" + i, transform, GlowQuad(), _blinkMats[i % 2]);
                r.receiveShadows = false;
                r.transform.position = sky.TransformPoint(tops[i]) + Vector3.up * 0.4f;
                r.transform.localScale = Vector3.one * 2.2f;
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
            root.localPosition = new Vector3(MachinePos.x, MachineRootY, MachinePos.z);   // on the mound's dome
            Color shell = Lin(0xe8563f), dark = Lin(0x2b2350), trim = Lin(0xd8c7a6), cream = Lin(0xfffaf0), yellow = Lin(0xffd93d), hub = Lin(0x8a8fa8);
            Color hood = Lin(0x6a52c0);   // the barrel: the web's purple hood, lighter than the cart so it reads on the dark stands
            var mat = EnvKit.Surface(_env, Color.white, null, null, rim: 1.5f, pulse: 0.1f, bands: 3);

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
            // Trim so the cart stops reading as a primitive: a cream lip round the top edge, a yellow-and-dark hazard band
            // on the camera face, bolts at its corners, corner bumpers and a small number plate on the side.
            b.Box(Matrix4x4.TRS(new Vector3(0, 0.455f, 0), Quaternion.identity, new Vector3(1.74f, 0.035f, 1.19f)), trim);
            for (int k = 0; k < 9; k++)
                b.Box(Matrix4x4.TRS(new Vector3(-0.68f + k * 0.17f, 0.2f, -0.588f), Quaternion.Euler(0, 0, 35), new Vector3(0.07f, 0.17f, 0.012f)), k % 2 == 0 ? yellow : dark);
            b.Box(Matrix4x4.TRS(new Vector3(0, 0.2f, -0.582f), Quaternion.identity, new Vector3(1.5f, 0.13f, 0.008f)), dark);
            foreach (var x in new[] { -0.78f, 0.78f })
                foreach (var y in new[] { 0.16f, 0.4f })
                    b.Sphere(0.028f, 6, 4, Matrix4x4.Translate(new Vector3(x, y, -0.59f)), hub, Vector3.one);
            foreach (var x in new[] { -0.86f, 0.86f })
                foreach (var z in new[] { -0.58f, 0.58f })
                    b.Box(Matrix4x4.TRS(new Vector3(x, 0.28f, z), Quaternion.identity, new Vector3(0.07f, 0.36f, 0.07f)), trim);
            b.Box(Matrix4x4.TRS(new Vector3(0.858f, 0.28f, -0.05f), Quaternion.identity, new Vector3(0.012f, 0.17f, 0.42f)), cream);
            b.Box(Matrix4x4.TRS(new Vector3(0.866f, 0.28f, -0.05f), Quaternion.identity, new Vector3(0.006f, 0.1f, 0.06f)), Lin(0xe8563f));
            Part("PitchO.Cart", root, b, mat, Vector3.zero, cast: true);

            _mHead = new GameObject("PitchO.Head").transform;
            _mHead.SetParent(root, false);
            _mHead.localPosition = new Vector3(0, MachinePos.y - MachineRootY, 0);
            b = new MeshBuilder();
            var alongX = Quaternion.Euler(0, 0, 90);
            b.Capsule(0.52f, 0.55f, 6, 18, Matrix4x4.TRS(Vector3.zero, alongX, Vector3.one), shell);
            b.Shell(0.538f, 0.538f, 0.2f, 24, 0, Mathf.PI * 2, Matrix4x4.TRS(new Vector3(-0.18f, 0, 0), alongX, Vector3.one), trim, Vector2.one);
            // Barrel: a flared cannon from the body to the muzzle, aimed a little up at the plate; a trim lip on the mouth.
            var aim = new Vector3(Mathf.Cos(0.3f), Mathf.Sin(0.3f), 0);
            var bq = Quaternion.FromToRotation(Vector3.up, aim);
            var bcen = new Vector3(0.68f, 0.24f, 0);
            b.Shell(0.34f, 0.24f, 0.8f, 18, 0, Mathf.PI * 2, Matrix4x4.TRS(bcen, bq, Vector3.one), hood, Vector2.one);
            b.Shell(0.32f, 0.22f, 0.8f, 18, 0, Mathf.PI * 2, Matrix4x4.TRS(bcen, bq, Vector3.one), Lin(0x120c22), Vector2.one, inward: true);
            // a pale band round the barrel's waist: a spec-like highlight line on the purple
            b.Shell(0.315f, 0.30f, 0.07f, 18, 0, Mathf.PI * 2, Matrix4x4.TRS(bcen - aim * 0.12f, bq, Vector3.one), Lin(0xb8a8ff), Vector2.one);
            // Hopper (the chute) with spare balls waiting in it.
            b.Shell(0.42f, 0.2f, 0.5f, 16, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(-0.2f, 0.72f, 0)), trim, Vector2.one);
            b.Shell(0.40f, 0.18f, 0.5f, 16, 0, Mathf.PI * 2, Matrix4x4.Translate(new Vector3(-0.2f, 0.72f, 0)), Lin(0x6b5d4a), Vector2.one, inward: true);
            foreach (var (x, z) in new[] { (-0.32f, -0.08f), (-0.1f, 0.1f), (-0.18f, -0.18f), (-0.05f, -0.12f) })
                b.Sphere(0.14f, 10, 8, Matrix4x4.Translate(new Vector3(x, 0.88f, z)), cream, Vector3.one);
            // A little antenna with a bulb: a toy, not a lamp post.
            b.Tube(new[] { new Vector3(-0.62f, 0.3f, 0), new Vector3(-0.78f, 0.82f, 0.05f) }, 0.02f, 5, Matrix4x4.identity, dark);
            b.Sphere(0.06f, 8, 6, Matrix4x4.Translate(new Vector3(-0.78f, 0.86f, 0.05f)), yellow, Vector3.one);
            Part("PitchO.Body", _mHead, b, mat, Vector3.zero, cast: true);

            // The muzzle ring: a lit lip on the barrel's mouth that warms as the wheels spin up and flares on the shot.
            b = new MeshBuilder();
            b.Torus(0.33f, 0.05f, 6, 22, Mathf.PI * 2, Matrix4x4.TRS(bcen + aim * 0.4f, Quaternion.FromToRotation(Vector3.forward, aim), Vector3.one), Color.white);
            _muzzleRingMat = EnvKit.Surface(_env, Color.white, null, null, rim: 0.5f, pulse: 0.3f, lift: 1.2f, bands: 1);
            _muzzleRingMat.SetFloat("_ShadowRecv", 0);
            Part("PitchO.MuzzleRing", _mHead, b, _muzzleRingMat, Vector3.zero).receiveShadows = false;

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

            Look.Blob(root, 1.15f, 0.5f, groundY: MachineRootY + 0.02f, heightOffset: 0);
        }

        /// The stand-in batter (neutral capsule + batting helmet) and a real bat: tapered barrel, knob, tip.
        void BuildBatter()
        {
            var batterMat = Mat(Color.white, rim: 0.9f); Look.Track(batterMat, PC.Batter); _batterMat = batterMat;
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

        /// Beat pips as flat diamonds on the turf under the flight path (the ball's shadow runs over them), and the
        /// strike zone as a reticle ring on the ground at the plate: nothing round floats in the air but the ball.
        void BuildPips()
        {
            var tpl = new MeshBuilder();
            // A diamond along +x, bright centre to a deeper edge (four triangles round the centre vertex).
            Vector3[] rim = { new Vector3(0.5f, 0, 0), new Vector3(0, 0, 0.3f), new Vector3(-0.5f, 0, 0), new Vector3(0, 0, -0.3f) };
            int c0 = tpl.Count;
            tpl.P.Add(Vector3.zero); tpl.N.Add(Vector3.up); tpl.C.Add(Color.white); tpl.UV.Add(Vector2.zero);
            foreach (var v in rim) { tpl.P.Add(v * 0.7f); tpl.N.Add(Vector3.up); tpl.C.Add(new Color(0.5f, 0.42f, 0.3f)); tpl.UV.Add(Vector2.zero); }
            for (int k = 0; k < 4; k++) tpl.Tri(c0, c0 + 1 + (k + 1) % 4, c0 + 1 + k);
            var m = EnvKit.Surface(_env, Color.white, null, null, rim: 0f, pulse: 0f, lift: 1.6f, bands: 1);   // no beat pulse: every pip is the same on every beat
            m.SetFloat("_ShadowRecv", 0);
            _pips = new MeshBatch("Pips", transform, tpl, PipMax, m);
            _pips.Renderer.receiveShadows = false;
            for (int i = 0; i < PipMax; i++) _pips.Matrix[i] = Matrix4x4.Scale(Vector3.zero);
            _pips.Flush();

            // Reticle: a flat ring with four ticks pointing in, lying on the ground under the contact point.
            var b = new MeshBuilder();
            b.Torus(0.5f, 0.035f, 4, 40, Mathf.PI * 2, Matrix4x4.Rotate(Quaternion.Euler(90, 0, 0)), Color.white);
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI / 2 + Mathf.PI / 4;
                var at = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.5f;
                b.FloorQuad(0.05f, 0.22f, Matrix4x4.TRS(at * 0.86f, Quaternion.LookRotation(at.normalized), Vector3.one), Color.white);
            }
            _reticleMat = EnvKit.Surface(_env, Color.white, null, null, rim: 0f, pulse: 0.1f, lift: 1.4f, bands: 1);
            _reticleMat.SetFloat("_ShadowRecv", 0);
            _reticleMat.SetColor("_BaseColor", Color.black);
            var r = Part("StrikeReticle", transform, b, _reticleMat, new Vector3(Contact.x, 0.05f, Contact.z));
            r.receiveShadows = false;
            _reticle = r.transform;
        }

        // ------------------------------------------------------------ machine and pips (web armMachine / fireMachine / setPips)

        void ArmMachine(float strength) { _wheelSpinT = Mathf.Max(_wheelSpinT, 26 * strength); }

        /// The shot. PITCH-O fires on every beat of the hymn now (60-160 bpm), so the shot is a pneumatic puff, not a
        /// gun flash: a soft warm pop at the muzzle and a grey breath of air, scaled down at quick tempos (a big flash
        /// three times a second strobed), and the same on every beat (the shot must not count the bar).
        void FireMachine()
        {
            float k = TempoK;
            _muzzleFlash = 0.55f + 0.45f * k; _recoil = 0.6f + 0.4f * k;
            _wheelSpinT = Mathf.Max(_wheelSpinT, 46);
            var fx = Look.Fx;
            fx.Flare(Muzzle, Hex(0xffe2a8) * 0.6f, 0.3f + 0.1f * k, 0.1f);
            fx.Burst(Muzzle + new Vector3(0.1f, 0.05f, 0), Settings.ReduceMotion ? 2 : 4, Hex(0xfff6e0), 2.0f, 0.08f, 0.3f, -2, new Vector3(1, 0.3f, 0), 0.6f);
            if (_smoke != null)
            {
                var s = _smoke.Begin();
                s.Pos(Muzzle + new Vector3(0.12f, 0.03f, 0)).Dir(new Vector3(1, 0.35f, 0)); s.Cone = 0.35f; s.Speed = 2.4f; s.Jitter = 0.04f;
                s.Color(new Color(0.85f, 0.84f, 0.88f)); s.Alpha = 0.32f; s.Size = 0.26f; s.Life = 0.42f; s.S0 = 0.5f; s.S1 = 1.7f; s.Grav = 0.4f; s.Drag = 4f;
                _smoke.Emit(2);
            }
        }

        /// 1 at a relaxed beat (0.6 s or longer), down to 0 at 160 bpm: how big the per-beat machine motion may be.
        float TempoK
        {
            get
            {
                double spb = _round != null && _round.Clock != null ? _round.Clock.SpbAt(_now) : 0.6;
                return Mathf.Clamp01((float)(spb - 0.375) / (0.6f - 0.375f));
            }
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
                // The last step lands on the plate: the reticle marks it, not a pip.
                if (i >= steps - 1) { _pipBase[i] = 0; continue; }
                double u = (i + 1) / (double)steps;
                var at = Sample(p, u);
                var ahead = Sample(p, u + 0.02) - at; ahead.y = 0;
                _pipPos[i] = new Vector3(at.x, 0.035f, at.z);
                _pipRot[i] = Quaternion.LookRotation(ahead.sqrMagnitude > 1e-6f ? ahead.normalized : Vector3.right) * Quaternion.Euler(0, -90, 0);
                _pipBase[i] = 1.1f;   // one size for every pip: the diamond must not count the bar or the beat
                _pipPop[i] = 0;
            }
            _pipCount = steps;
            _reticleGoal = 1;
        }

        void PopPip(int i, Pitch p)
        {
            if (p != _pipOwner || i < 0 || i >= _pipCount) return;
            if (i == _pipCount - 1) { _reticlePop = 1; return; }
            return;   // static markers: a pop or a fade would make the diamond's size and brightness differ between frames
        }

        void ClearPips(Pitch p)
        {
            if (p != _pipOwner) return;
            _pipOwner = null;
            _reticleGoal = 0;
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
            // A shot on every beat keeps the wheels spun up: their speed is capped where the three spokes still read
            // as turning forward at 30 fps (a faster wheel strobed and seemed to run backwards).
            _wheelSpinT = Damp(_wheelSpinT, 0, 1.6f, dt);
            _wheelSpin += dt * Mathf.Min(2 + _wheelSpinT, calm ? 5f : 11f);
            _mWheels[0].localRotation = Quaternion.Euler(0, 0, _wheelSpin * Mathf.Rad2Deg);
            _mWheels[1].localRotation = Quaternion.Euler(0, 0, -_wheelSpin * Mathf.Rad2Deg);
            _muzzleFlash = Damp(_muzzleFlash, 0, 14, dt);
            _recoil = Damp(_recoil, 0, 9, dt);
            _flinch = Damp(_flinch, 0, 4, dt);
            // idle: one nod per beat, every beat alike (a toy that keeps time, never the bar). f: 0 at the shot.
            float f = (float)(beat - System.Math.Floor(beat));
            float tk = TempoK;
            float bob = _playing && !calm ? -Mathf.Sin(f * Mathf.PI * 2) * (0.02f + 0.02f * tk) : 0;
            float hopY = _playing && !calm ? Mathf.Sin(f * Mathf.PI) * (0.015f + 0.025f * tk) : 0;
            float kick = _recoil * (calm ? 0.03f : 0.08f + 0.07f * tk);
            _mHead.localPosition = new Vector3(-kick, MachinePos.y - MachineRootY + _recoil * 0.025f + hopY, 0);
            _mHead.localRotation = Quaternion.Euler(0, _flinch * 10, (bob + _recoil * 0.1f - _flinch * 0.1f) * Mathf.Rad2Deg);
            // recoil squash on the shot, and the crouch of the next one building through the second half of the beat
            float arm = _playing ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.45f, 1f, f)) : 0;
            float sq = 1 - _recoil * (0.06f + 0.06f * tk) - arm * 0.035f;
            _mHead.localScale = new Vector3(1 / Mathf.Sqrt(sq), sq, 1 / Mathf.Sqrt(sq));
            _muzzleMat.SetFloat(IdIntensity, _muzzleFlash * 0.55f);
            _muzzleRingMat.SetColor("_BaseColor", MuzzleRingCol * (0.6f + arm * 0.35f + _muzzleFlash * 1.1f));
            _muzzle.localScale = Vector3.one * 0.95f * (1 + _muzzleFlash * 0.4f);

            // face: a light squint as each shot builds, eyes wide on a big hit; pupils on the live ball (no blink on
            // every shot: at a beat a shot that was a twitch)
            float focus = arm * 0.6f;
            float open = (1 - focus * 0.5f) * (1 - _muzzleFlash * 0.2f) * (1 + _flinch * 0.25f);
            Vector3 look = Contact;
            foreach (var kv in _pitchBalls) { look = kv.Value.Go.transform.position; break; }
            if (_hitBalls.Count > 0) look = _hitBalls[_hitBalls.Count - 1].P;
            for (int i = 0; i < 2; i++)
            {
                _mEyes[i].localScale = new Vector3(1, Mathf.Max(0.08f, open), 1);
                var d = _mEyes[i].InverseTransformDirection(look - _mEyes[i].position).normalized;
                var want = new Vector3(Mathf.Clamp(d.x, -1, 1) * 0.075f, Mathf.Clamp(d.y, -1, 1) * 0.06f, -0.075f);
                _mPupils[i].localPosition = Damp(_mPupils[i].localPosition, want, 9, dt);
            }
            _mBrow.localPosition = new Vector3(-0.16f, 0.34f - focus * 0.07f + _flinch * 0.05f, -0.47f);
            _mBrow.localRotation = Quaternion.Euler(0, 0, (-focus * 0.12f + _flinch * 0.1f) * Mathf.Rad2Deg);

            // pips
            if (_pipCount > 0)
            {
                for (int i = 0; i < _pipCount; i++)
                {
                    _pipPop[i] = Damp(_pipPop[i], 0, 7, dt);
                    float s = _pipBase[i];
                    _pips.Matrix[i] = Matrix4x4.TRS(_pipPos[i], _pipRot[i], new Vector3(s, 1, s));
                    _pips.Tint[i] = PipCol.linear;
                }
                _pips.Flush();
            }

            // strike-zone reticle: fades in while a pitch is live, flashes and swells as the ball arrives
            _reticleOn = Damp(_reticleOn, _reticleGoal, 6, dt);
            _reticlePop = Damp(_reticlePop, 0, 6, dt);
            if (_reticleOn < 0.01f && _reticlePop < 0.01f) { if (_reticle.gameObject.activeSelf) _reticle.gameObject.SetActive(false); }
            else
            {
                if (!_reticle.gameObject.activeSelf) _reticle.gameObject.SetActive(true);
                float rs = (0.8f + _reticleOn * 0.2f) * (1 + _reticlePop * 0.35f) * (1 + Look.BeatPulse * 0.05f);
                _reticle.localScale = new Vector3(rs, 1, rs);
                _reticleMat.SetColor("_BaseColor", PipCol.linear * (_reticleOn * 0.75f + _reticlePop * 1.3f) * (1 - 0.45f * _nightK));   // under the night pool it read as a white-hot spot
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
            for (int i = 0; i < _flareMats.Length; i++) _flareMats[i].SetFloat(IdIntensity, (n * 2.0f * flicker + Look.BeatPulse * n * 0.4f) * (i == 0 ? 0f : 1));   // the near head sits by the score: a faint lamp that cannot bloom beside the HUD
            for (int i = 0; i < _flareMatsR.Length; i++) _flareMatsR[i].SetFloat(IdIntensity, n * 2.2f * flicker + Look.BeatPulse * n * 0.3f);
            // the floods carry the night: warm pools on the plate and the mound
            for (int i = 0; i < _poolMats.Length; i++) _poolMats[i].SetFloat(IdIntensity, n * 0.6f * (1 + Look.BeatPulse * 0.04f));   // warm, never a white hot spot on the plate
            for (int i = 0; i < _beamMats.Length; i++)
            {
                _beamMats[i].SetFloat(IdIntensity, n * 0.065f * flicker);   // half the haze the cones laid over the stands
                bool on = n > 0.01f;   // no blended cone over the frame by day (the shader is linked by the near cones)
                if (_beamR[i].enabled != on) _beamR[i].enabled = on;
            }
            if (_roofMat) _roofMat.SetColor("_BaseColor", Color.Lerp(RoofDay, RoofNight, n) * (1 + Look.BeatPulse * n * 0.2f));
            _glareMat.SetFloat(IdIntensity, (1 - n) * 0.22f);
            {
                // beacons blink day and night (two phases), a touch brighter in the dark
                float t = Time.time * 0.8f, on = Mathf.Lerp(0.9f, 1.7f, n);
                _blinkMats[0].SetFloat(IdIntensity, (t % 1f) < 0.35f ? on : 0.04f);
                _blinkMats[1].SetFloat(IdIntensity, ((t + 0.5f) % 1f) < 0.35f ? on : 0.04f);
            }
            // windows: a sparse scatter at dusk, the whole city lit warm at night; the sun wash leaves with the sun
            float winBeat = 1 + Look.BeatPulse * 0.12f;
            _winMats[0].SetFloat(IdIntensity, Mathf.Lerp(2.8f, 2.2f, n) * winBeat);
            _winMats[1].SetFloat(IdIntensity, n * 2.0f * winBeat);
            _winMats[2].SetFloat(IdIntensity, (1 - n) * 1.3f);
            _hazeMat.SetColor(IdColor, Color.Lerp(HazeDay, HazeNight, n));
            _hazeMat.SetFloat(IdIntensity, 1);
            // the night turf keeps its clumps and mow texture under the floods (flat teal read as a painted floor)
            if (_turfMat) _turfMat.SetFloat("_MapStrength", Mathf.Lerp(0.72f, 1.0f, n));
            if (_ledMat) _ledMat.SetColor("_BaseColor", Color.Lerp(LedDay, LedNight, n) * (1 + Look.BeatPulse * 0.25f));
            _env?.Spotlights?.SetLevel(Mathf.Lerp(0.03f, 0.004f, n), Mathf.Lerp(0.04f, 0.007f, n));   // at night the cones read from the towers to the pools, without a milky veil over the crowd
            // the bowl falls dark toward the walls at night; the floods and the rim light carry the crowd
            if (_env?.Crowd != null) _env.Crowd.O.StandColor = Color.Lerp(StandDay, StandNight, n);
            if (_rowsMat) _rowsMat.SetColor("_BaseColor", Color.Lerp(StandDay, StandNight, n));
            _env?.Crowd?.SetTint(Color.Lerp(Color.white, new Color(0.9f, 0.93f, 1.12f), n));   // the crowd's lit side is lifted so it reads against the haze; the pools stay the brightest thing
            if (_cardMat) _cardMat.SetColor("_BaseColor", Color.Lerp(Color.white, new Color(0.9f, 0.93f, 1.12f), n));

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
