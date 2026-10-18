// The environment kit (web render/env/index.js): reusable, procedurally built stage-set pieces every game
// composes instead of inventing a world each. A game says
//
//     var env = Look.CreateEnv(EnvPreset.Arena);                 // or Look.CreateEnv(EnvPreset.Stage, o => { ... })
//     env.Crowd?.Hype(0.8);                                     // on a combo milestone
//
// and gets a lit, beat-reactive set for about ten draw calls. LookRig owns it: it pulses every piece on each
// whole beat of the audio clock (TickBeat: 1.25 on the downbeat, 0.8 between, like the web's stage.js) and
// updates it on scene time, so a pause freezes the set and a hitstop does not.
//
// Presets (the web's four): arena = full show, stage = no audience, void = a lone platform in open air,
// field = stands but no overhead clutter. Every piece takes options (EnvOptions) whose defaults are the web's.
// Materials and textures come from Resources/Look and Resources/Env (Shader.Find is null in a player).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bonanza.Look
{
    public enum EnvPreset { Arena, Stage, Void, Field }

    /// One piece of a set: built once, pulsed on the beat, updated on scene time.
    public abstract class EnvPiece
    {
        public GameObject Root;
        public virtual void Pulse(float strength, int beatIndex) { }
        public virtual void Update(float dt, double beat, float time, Palette pal) { }
        public virtual void OnQuality() { }
    }

    public sealed class Env
    {
        public readonly Transform Root;
        public EnvGround Ground { get; private set; }
        public EnvBackdrop Backdrop { get; private set; }
        public EnvCrowd Crowd { get; private set; }
        public EnvBanners Banners { get; private set; }
        public EnvSpotlights Spotlights { get; private set; }
        public EnvFloaters Floaters { get; private set; }
        readonly List<EnvPiece> _pieces = new List<EnvPiece>();
        readonly LookRig _look;
        readonly FxRng _rng;

        public static readonly Dictionary<EnvPreset, string[]> Presets = new Dictionary<EnvPreset, string[]>
        {
            { EnvPreset.Arena, new[] { "ground", "backdrop", "crowd", "banners", "spotlights", "floaters" } },
            { EnvPreset.Stage, new[] { "ground", "backdrop", "spotlights", "floaters" } },
            { EnvPreset.Void, new[] { "ground", "backdrop", "floaters", "spotlights" } },
            { EnvPreset.Field, new[] { "ground", "backdrop", "crowd", "floaters" } },
        };

        internal Env(LookRig look, Transform parent, uint seed)
        {
            _look = look;
            _rng = new FxRng(seed);
            Root = new GameObject("Env").transform;
            Root.SetParent(parent, false);
        }

        public float Rand() => _rng.Next();

        /// Compose a whole set in one call. Pieces already added are kept (add one by hand first to override it).
        public Env Build(EnvPreset preset, EnvOptions o)
        {
            Root.localPosition = new Vector3(0, o.GroundY, 0);
            foreach (var name in Presets[preset])
                switch (name)
                {
                    case "ground": if (!o.SkipGround) AddGround(o.Ground); break;
                    case "backdrop": AddBackdrop(o.Backdrop); break;
                    case "crowd": AddCrowd(o.Crowd); break;
                    case "banners": AddBanners(o.Banners); break;
                    case "spotlights": AddSpotlights(o.Spotlights); break;
                    case "floaters": AddFloaters(o.Floaters); break;
                }
            return this;
        }

        public EnvGround AddGround(EnvGround.Options o = null) => Ground ??= Add(new EnvGround(this, o ?? new EnvGround.Options()));
        public EnvBackdrop AddBackdrop(EnvBackdrop.Options o = null) => Backdrop ??= Add(new EnvBackdrop(this, o ?? new EnvBackdrop.Options()));
        public EnvCrowd AddCrowd(EnvCrowd.Options o = null) => Crowd ??= Add(new EnvCrowd(this, o ?? new EnvCrowd.Options()));
        public EnvBanners AddBanners(EnvBanners.Options o = null) => Banners ??= Add(new EnvBanners(this, o ?? new EnvBanners.Options()));
        public EnvSpotlights AddSpotlights(EnvSpotlights.Options o = null) => Spotlights ??= Add(new EnvSpotlights(this, o ?? new EnvSpotlights.Options()));
        public EnvFloaters AddFloaters(EnvFloaters.Options o = null) => Floaters ??= Add(new EnvFloaters(this, o ?? new EnvFloaters.Options()));

        /// An authored static mesh (Resources/Env/<name>.bytes) lit by the house toon with its vertex colours,
        /// e.g. a game's set edge. One draw, no shadow casting. Null if the asset is missing.
        public MeshRenderer AddMesh(string name, Color tint, float rim = 0.5f, float pulse = 0.06f, float bands = 3, float fog = 1)
        {
            var mesh = EnvBytesMesh.Load(name);
            if (mesh == null) return null;
            Own(mesh);
            var r = EnvKit.Renderer(name, Root, mesh, EnvKit.Surface(this, tint, null, null, rim: rim, pulse: pulse, bands: bands, fog: fog));
            r.receiveShadows = false;
            return r;
        }

        T Add<T>(T piece) where T : EnvPiece
        {
            _pieces.Add(piece);
            return piece;
        }

        /// A beat accent across the whole set (LookRig calls it on every whole beat; a game may add its own).
        public void Pulse(float strength = 1, int beatIndex = 0)
        {
            if (Settings.ReduceMotion) strength *= 0.35f;
            for (int i = 0; i < _pieces.Count; i++) _pieces[i].Pulse(strength, beatIndex);
        }

        internal void Update(float dt, double beat, float time)
        {
            var pal = _look.Palette.Live;
            for (int i = 0; i < _pieces.Count; i++) _pieces[i].Update(dt, beat, time, pal);
        }

        internal void OnQuality() { for (int i = 0; i < _pieces.Count; i++) _pieces[i].OnQuality(); }

        internal void Destroy()
        {
            if (Root) Object.Destroy(Root.gameObject);
            EnvKit.Release(this);
        }

        // ------------------------------------------------------------ owned resources

        readonly List<Object> _owned = new List<Object>();

        /// A material copy this env owns (destroyed with it).
        public Material Mat(Material template)
        {
            var m = new Material(template);
            _owned.Add(m);
            return m;
        }

        public Mesh Own(Mesh m) { _owned.Add(m); return m; }

        internal void FreeOwned()
        {
            foreach (var o in _owned) if (o) Object.Destroy(o);
            _owned.Clear();
        }
    }

    /// Per-piece options; the defaults are the web's.
    public sealed class EnvOptions
    {
        public float GroundY;
        public bool SkipGround;
        public EnvGround.Options Ground = new EnvGround.Options();
        public EnvBackdrop.Options Backdrop = new EnvBackdrop.Options();
        public EnvCrowd.Options Crowd = new EnvCrowd.Options();
        public EnvBanners.Options Banners = new EnvBanners.Options();
        public EnvSpotlights.Options Spotlights = new EnvSpotlights.Options();
        public EnvFloaters.Options Floaters = new EnvFloaters.Options();
    }

    /// Shared resources: the env materials and value maps, loaded once from Resources.
    public static class EnvKit
    {
        static Material _toonEnv, _crowd, _beam;
        public static Material ToonEnv => _toonEnv ? _toonEnv : _toonEnv = Resources.Load<Material>("Look/ToonEnv");
        public static Material CrowdMat => _crowd ? _crowd : _crowd = Resources.Load<Material>("Look/Crowd");
        public static Material BeamMat => _beam ? _beam : _beam = Resources.Load<Material>("Look/Beam");

        static readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();

        /// A value map from Resources/Env (grass, dirt, stage, cloth, seats, arena). Null if missing: the surface is then flat.
        public static Texture2D Tex(string name)
        {
            if (_tex.TryGetValue(name, out var t) && t) return t;
            t = Resources.Load<Texture2D>("Env/" + name);
            if (t == null) Debug.LogWarning("[ENV] missing texture Env/" + name);
            _tex[name] = t;
            return t;
        }

        /// A ToonEnv material copy owned by `env`.
        public static Material Surface(Env env, Color c, string map = null, Vector2? tiling = null, float rim = 1, float pulse = 0.12f,
            float lift = 0, float bands = 3, bool twoSided = false, float fog = 1, float mapStrength = 1)
        {
            var m = env.Mat(ToonEnv);
            m.SetColor("_BaseColor", c);
            if (map != null)
            {
                var t = Tex(map);
                if (t) { m.SetTexture("_BaseMap", t); m.SetTextureScale("_BaseMap", tiling ?? Vector2.one); }
            }
            m.SetFloat("_MapStrength", mapStrength);
            m.SetFloat("_Rim", rim); m.SetFloat("_Pulse", pulse); m.SetFloat("_Lift", lift); m.SetFloat("_Bands", bands);
            m.SetFloat("_Cull", twoSided ? (float)CullMode.Off : (float)CullMode.Back);
            m.SetFloat("_FogMul", fog);
            return m;
        }

        public static MeshRenderer Renderer(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        public static MeshRenderer Static(Env env, string name, Transform parent, MeshBuilder b, Material mat) =>
            Renderer(name, parent, env.Own(b.ToMesh(name)), mat);

        internal static void Release(Env env) => env.FreeOwned();

        public static Color Lin(Color gamma, float mul) { var l = gamma.linear * mul; return new Color(l.r, l.g, l.b).gamma; }
        /// Linear-space lerp of two gamma colours (the web mixes in linear).
        public static Color Mix(Color a, Color b, float t) => Color.Lerp(a.linear, b.linear, t).gamma;
        public static float Damp(float a, float b, float lambda, float dt) => Mathf.Lerp(a, b, 1 - Mathf.Exp(-lambda * dt));
        public static float EaseOutCubic(float t) { t = Mathf.Clamp01(t); return 1 - (1 - t) * (1 - t) * (1 - t); }
        public static Matrix4x4 TRS(Vector3 p, Quaternion q, Vector3 s) => Matrix4x4.TRS(p, q, s);
    }
}
