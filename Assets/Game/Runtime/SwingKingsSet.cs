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
        static readonly Color StandDay = new Color(0x54 / 255f, 0x74 / 255f, 0xad / 255f), StandNight = new Color(0.2f, 0.27f, 0.45f);
        static readonly Color HazeDay = new Color(1f, 0.62f, 0.45f) * 0.16f, HazeNight = new Color(0.25f, 0.33f, 0.62f) * 0.05f;
        static readonly Color LedDay = new Color(1f, 0.62f, 0.25f) * 0.55f, LedNight = new Color(1f, 0.7f, 0.35f) * 0.85f;

        // net, night, batter
        Material _netMat;
        float _netHit;
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

        static readonly Vector3[] TowerAt = { W(-13, 0, 3.5f), W(-13, 0, -9.5f) };
        const float BankY = 8.5f, BankYRight = 10.5f;
        static readonly Vector3[] TowerRight = { new Vector3(13.5f, 0, 17.5f) };
        Material _hazeMat, _rowsMat;
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
        /// Worn grass: the turf colour pulled toward a dry, sandy green (as a ratio over the dirt material).
        static Color Worn(Color toGrass) => Color.Lerp(toGrass, new Color(0.78f, 0.82f, 0.62f), 0.5f);
        /// The thin lighter lip on a dirt cut's edge.
        static void Lip(MeshBuilder b, Vector3 centre, float r, float y, int seg) =>
            FeatherRing(b, centre, r - 0.06f, r + 0.07f, y + 0.002f, new Color(1.35f, 1.3f, 1.2f), new Color(1.2f, 1.15f, 1.05f), seg);

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
            // A crisp cut edge with a thin lighter lip, then worn, yellowed grass fading out into the turf.
            Lip(b, cut, 2.55f, 0.021f, 56);
            FeatherRing(b, cut, 2.62f, 3.5f, 0.02f, Worn(toGrass), toGrass, 56);
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
            Lip(b, mound, MoundR, 0.016f, 40);
            FeatherRing(b, mound, MoundR + 0.07f, MoundR + 0.75f, 0.012f, Worn(toGrass), toGrass, 40);
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
                BuildTrack(crowdO);
                BuildStandsDetail(crowdO);
            }

            // Night lights: tower-head flares, turf pools under the floods (both on at zero from Load: prewarmed).
            _flareMats = new Material[TowerAt.Length];
            for (int i = 0; i < TowerAt.Length; i++)
                _flareMats[i] = Glow("TowerFlare" + i, transform, TowerAt[i] + new Vector3(0.25f, BankY, 0), 3.2f, Hex(0xfff3d0), true, null, 2.6f);
            var flat = Quaternion.Euler(90, 0, 0);
            _poolMats = new[]
            {
                Glow("PoolPlate", transform, new Vector3(PlatePos.x - 0.3f, 0.06f, PlatePos.z), 11f, Hex(0xfff0d6), false, flat, 0.5f),
                Glow("PoolMound", transform, new Vector3(MachinePos.x + 0.3f, 0.36f, MachinePos.z), 8f, Hex(0xfff0d6), false, flat, 0.5f),
            };
            // The low sun behind the skyline (day): a wide soft glare the bloom carries into the arches.
            _glareMat = Glow("SunGlare", transform, new Vector3(30, 7, 92), 70, Hex(0xffc98a), true, null, 1.2f);
            BuildSkylineDressing();
            BuildAtmosphere();
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
            foreach (var at in TowerRight)
            {
                shafts.Shell(0.18f, 0.32f, BankYRight, 8, 0, Mathf.PI * 2, Matrix4x4.Translate(at + new Vector3(0, BankYRight / 2, 0)), Color.white, Vector2.one);
                var face = Quaternion.LookRotation((new Vector3(PlatePos.x, 0, PlatePos.z) - at).normalized);
                banks.Box(Matrix4x4.TRS(at + new Vector3(0, BankYRight, 0), face, new Vector3(1.7f, 1.0f, 0.28f)), Color.white);
                shafts.Box(Matrix4x4.TRS(at + new Vector3(0, BankYRight - 0.62f, 0), face, new Vector3(1.9f, 0.08f, 0.5f)), Color.white);
            }
            EnvKit.Static(_env, "TowerShaftsR", transform, shafts, towerMat);
            EnvKit.Static(_env, "TowerBanksR", transform, banks, _bankMat);
            _flareMatsR = new Material[TowerRight.Length];
            for (int i = 0; i < TowerRight.Length; i++)
                _flareMatsR[i] = Glow("TowerFlareR" + i, transform, TowerRight[i] + new Vector3(0, BankYRight, -0.3f), 3.2f, Hex(0xfff3d0), true, null, 2.8f);
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
            var m = EnvKit.Surface(_env, Color.white, null, null, rim: 0f, pulse: 0.2f, lift: 1.6f, bands: 1);
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
                // The last step lands on the plate: the reticle marks it, not a pip.
                if (i >= steps - 1) { _pipBase[i] = 0; continue; }
                double u = (i + 1) / (double)steps;
                double off = u * p.Lead;
                bool whole = System.Math.Abs(off - System.Math.Round(off)) < 0.08;
                var at = Sample(p, u);
                var ahead = Sample(p, u + 0.02) - at; ahead.y = 0;
                _pipPos[i] = new Vector3(at.x, 0.035f, at.z);
                _pipRot[i] = Quaternion.LookRotation(ahead.sqrMagnitude > 1e-6f ? ahead.normalized : Vector3.right) * Quaternion.Euler(0, -90, 0);
                _pipBase[i] = whole ? 1.35f : 0.9f;   // whole beats bigger
                _pipPop[i] = 0;
            }
            _pipCount = steps;
            _reticleGoal = 1;
        }

        void PopPip(int i, Pitch p)
        {
            if (p != _pipOwner || i < 0 || i >= _pipCount) return;
            if (i == _pipCount - 1) { _reticlePop = 1; return; }
            _pipPop[i] = 1; _pipBase[i] *= 0.5f;   // flash, then recede: passed markers do not hang as glowing discs
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
            _wheelSpinT = Damp(_wheelSpinT, 0, 1.6f, dt);
            _wheelSpin += dt * (2 + _wheelSpinT);
            _mWheels[0].localRotation = Quaternion.Euler(0, 0, _wheelSpin * Mathf.Rad2Deg);
            _mWheels[1].localRotation = Quaternion.Euler(0, 0, -_wheelSpin * Mathf.Rad2Deg);
            _muzzleFlash = Damp(_muzzleFlash, 0, 9, dt);
            _recoil = Damp(_recoil, 0, 7, dt);
            _flinch = Damp(_flinch, 0, 4, dt);
            // idle: a nod on the beat and a little hop off the downbeat (a toy that keeps time)
            float bob = _playing && !calm ? Mathf.Sin((float)beat * Mathf.PI) * 0.05f : 0;
            float hopY = _playing && !calm ? Mathf.Abs(Mathf.Sin((float)beat * Mathf.PI)) * 0.045f : 0;
            float kick = _recoil * (calm ? 0.04f : 0.18f);
            _mHead.localPosition = new Vector3(-kick, MachinePos.y - MachineRootY + _recoil * 0.03f + hopY, 0);
            _mHead.localRotation = Quaternion.Euler(0, _flinch * 10, (bob + _recoil * 0.14f - _flinch * 0.1f) * Mathf.Rad2Deg);
            // recoil squash: short and fat on the shot, then a stretch back (anticipation while armed: a slight crouch)
            float arm = Mathf.Clamp01(_wheelSpinT / 26);
            float sq = 1 - _recoil * 0.16f - arm * 0.04f + Look.BeatPulse * (calm ? 0 : 0.025f);
            _mHead.localScale = new Vector3(1 / Mathf.Sqrt(sq), sq, 1 / Mathf.Sqrt(sq));
            _muzzleMat.SetFloat(IdIntensity, _muzzleFlash * 1.6f);
            _muzzleRingMat.SetColor("_BaseColor", MuzzleRingCol * (0.55f + arm * 0.9f + _muzzleFlash * 2.2f));
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
                    _pips.Matrix[i] = Matrix4x4.TRS(_pipPos[i], _pipRot[i], new Vector3(s, 1, s));
                    float glow = 1 + _pipPop[i] * 2.5f + (_pipBase[i] >= 1.3f ? 0.35f : 0);
                    _pips.Tint[i] = PipCol.linear * glow;
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
                _reticleMat.SetColor("_BaseColor", PipCol.linear * (_reticleOn * 0.75f + _reticlePop * 1.3f));
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
            for (int i = 0; i < _flareMats.Length; i++) _flareMats[i].SetFloat(IdIntensity, n * 2.0f * flicker + Look.BeatPulse * n * 0.4f);
            for (int i = 0; i < _flareMatsR.Length; i++) _flareMatsR[i].SetFloat(IdIntensity, n * 2.2f * flicker + Look.BeatPulse * n * 0.3f);
            // the floods carry the night: warm pools on the plate and the mound
            for (int i = 0; i < _poolMats.Length; i++) _poolMats[i].SetFloat(IdIntensity, n * 0.42f);
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
            if (_ledMat) _ledMat.SetColor("_BaseColor", Color.Lerp(LedDay, LedNight, n) * (1 + Look.BeatPulse * 0.25f));
            _env?.Spotlights?.SetLevel(Mathf.Lerp(0.03f, 0.05f, n), Mathf.Lerp(0.04f, 0.09f, n));   // the cones stay a hint; the pools carry the light
            // the bowl falls dark toward the walls at night; the floods and the rim light carry the crowd
            if (_env?.Crowd != null) _env.Crowd.O.StandColor = Color.Lerp(StandDay, StandNight, n);
            if (_rowsMat) _rowsMat.SetColor("_BaseColor", Color.Lerp(StandDay, StandNight, n));
            _env?.Crowd?.SetTint(Color.Lerp(Color.white, new Color(0.6f, 0.65f, 0.86f), n));

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
