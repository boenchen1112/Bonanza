// The house look for one game: a palette, the three-light rig (key, hemisphere
// fill, kick), a gradient sky dome, linear fog and blob shadows. Shaders write linear light;
// Bonanza/Composite (a Full Screen Pass feature on the URP renderers) does exposure, tone curve, saturation, vignette, all written
// to shader globals once a frame. Toon materials read those globals, so a
// palette change re-lights every surface with no per-material work.
//
// Shaders and materials come from Resources/Look (never Shader.Find, which is
// null in a WebGL player). No Light components and no shadow maps: the lights
// are evaluated in the shader, shadows are blobs. Cheap on an integrated GPU.

using System.Collections.Generic;
using UnityEngine;

namespace Bonanza.Look
{
    public sealed class LookRig : MonoBehaviour
    {
        static Material _toon;

        /// The shared toon material; copy it (new Material(Toon)) to tint it.
        public static Material Toon => _toon ? _toon : _toon = Resources.Load<Material>("Look/Toon");

        public static LookRig Create(Transform parent, string palette)
        {
            var go = new GameObject("LookRig");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<LookRig>();
            rig.Init(palette);
            return rig;
        }

        public PaletteState Palette { get; private set; }
        /// 0..1 beat flash. Set by the game each frame; surfaces lift and the key breathes with it.
        public float BeatPulse;

        Transform _sky;
        Camera _cam;
        BlobShadows _blobs;
        readonly List<Tracked> _tracked = new List<Tracked>();
        float _time;
        CameraClearFlags _flags; Color _bg; bool _camSaved;

        struct Tracked { public Material M; public PC Key; public float Mul; }

        void Init(string palette)
        {
            Palette = new PaletteState(palette);
            var skyMat = Resources.Load<Material>("Look/Sky");
            var blobMat = Resources.Load<Material>("Look/Blob");
            if (skyMat == null || blobMat == null || Toon == null)
            {
                Debug.LogError("LookRig: Resources/Look materials missing");
                return;
            }
            var skyGo = new GameObject("Sky", typeof(MeshFilter), typeof(MeshRenderer));
            skyGo.transform.SetParent(transform, false);
            skyGo.GetComponent<MeshFilter>().sharedMesh = GameHost.BuiltinMesh(PrimitiveType.Sphere);
            var r = skyGo.GetComponent<MeshRenderer>();
            r.sharedMaterial = skyMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            skyGo.transform.localScale = Vector3.one * 100f;   // builtin sphere radius 0.5 -> dome radius 50
            _sky = skyGo.transform;
            _blobs = new BlobShadows(transform, blobMat);
            Push();
        }

        void OnDestroy()
        {
            // The camera outlives the game: hand it back as found.
            if (_camSaved && _cam) { _cam.clearFlags = _flags; _cam.backgroundColor = _bg; }
        }

        public void SetPalette(string name, float seconds = 0.55f) => Palette.Set(name, seconds);

        /// Make a material follow a palette colour (even mid cross-fade). `mul` darkens/lightens it.
        public void Track(Material m, PC key, float mul = 1)
        {
            _tracked.Add(new Tracked { M = m, Key = key, Mul = mul });
            m.SetColor("_BaseColor", Scaled(Palette.Live[key], mul));
        }

        /// A blob shadow under `follow`; it grows and fades as the object rises.
        /// `groundY` is the surface the shadow lies on (lift it above dirt pads); `heightOffset` is how
        /// far above that surface `follow` sits when grounded (0.9 for a capsule centre), so a grounded
        /// object gets full strength.
        public void Blob(Transform follow, float radius = 0.7f, float opacity = 0.55f, float groundY = 0, float heightOffset = 0) =>
            _blobs?.Add(follow, radius, opacity, groundY, heightOffset);

        static Color Scaled(Color c, float mul) => new Color(c.r * mul, c.g * mul, c.b * mul, 1);

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            _time += dt;
            if (Palette.Update(dt))
                foreach (var t in _tracked) if (t.M) t.M.SetColor("_BaseColor", Scaled(Palette.Live[t.Key], t.Mul));
            if (!_cam)
            {
                _cam = Camera.main;
                // The dome is the background; the default skybox would draw over it.
                if (_cam)
                {
                    _flags = _cam.clearFlags; _bg = _cam.backgroundColor; _camSaved = true;
                    _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = Palette.Live[PC.Fog];
                }
            }
            if (_cam && _sky) _sky.position = _cam.transform.position;
            Push();
            _blobs?.Update();
        }

        static Vector4 V(Color gamma, float k = 1)
        {
            var l = gamma.linear;
            return new Vector4(l.r * k, l.g * k, l.b * k, 1);
        }

        /// Web look space is z-toward-camera; Unity's is the mirror.
        static Vector4 Dir(Vector3 d) { d = new Vector3(d.x, d.y, -d.z).normalized; return new Vector4(d.x, d.y, d.z, 0); }

        void Push()
        {
            var p = Palette.Live;
            float pulse = Mathf.Clamp01(BeatPulse);
            // The key breathes a few percent on the beat.
            Shader.SetGlobalVector("_BonKeyDir", Dir(p[PD.KeyDir]));
            Shader.SetGlobalVector("_BonKeyColor", V(p[PC.Key], p[PF.KeyIntensity] * (1 + pulse * 0.10f)));
            Shader.SetGlobalVector("_BonFillSky", V(p[PC.FillSky], p[PF.FillIntensity]));
            Shader.SetGlobalVector("_BonFillGround", V(p[PC.FillGround], p[PF.FillIntensity]));
            Shader.SetGlobalVector("_BonKickDir", Dir(p[PD.RimDir]));
            Shader.SetGlobalVector("_BonKickColor", V(p[PC.Rim], 0.35f + p[PF.RimStrength] * 0.45f));
            Shader.SetGlobalVector("_BonRimColor", V(p[PC.Rim]));
            Shader.SetGlobalVector("_BonRimDir", Dir(p[PD.RimDir]));
            Shader.SetGlobalVector("_BonRimParams", new Vector4(p[PF.RimStrength], p[PF.RimPower], pulse, 0));
            Shader.SetGlobalVector("_BonPostParams", new Vector4(p[PF.Exposure], p[PF.Saturation], p[PF.Vignette], _time % 100f));
            Shader.SetGlobalVector("_BonFogColor", V(p[PC.Fog]));
            Shader.SetGlobalVector("_BonFogParams", new Vector4(p[PF.FogNear], p[PF.FogFar], 0, 0));
            // Shadow tint: a much darker, slightly cooler ground, so shadows sit in the palette.
            var sh = Color.Lerp(p[PC.Ground].linear * 0.28f, p[PC.Fog].linear, 0.25f);
            Shader.SetGlobalVector("_BonShadowColor", new Vector4(sh.r, sh.g, sh.b, 1));
            Shader.SetGlobalVector("_BonSkyTop", V(p[PC.SkyTop]));
            Shader.SetGlobalVector("_BonSkyMid", V(p[PC.SkyMid]));
            Shader.SetGlobalVector("_BonSkyBot", V(p[PC.SkyBot]));
            Shader.SetGlobalVector("_BonSun", V(p[PC.Sun]));
            Shader.SetGlobalVector("_BonSunDir", Dir(p[PD.SunDir]));
            Shader.SetGlobalVector("_BonSkyParams", new Vector4(p[PF.SunSize], p[PF.SunStrength], _time, pulse));
        }
    }

    /// Blob shadows: a flat soft ellipse under each followed object (web shadows.js).
    sealed class BlobShadows
    {
        sealed class Slot
        {
            public Transform Follow; public float Radius, Opacity, GroundY, HeightOffset;
            public GameObject Go; public MeshRenderer R; public MaterialPropertyBlock Mpb;
        }

        readonly Transform _root;
        readonly Material _mat;
        readonly List<Slot> _slots = new List<Slot>();
        const int Max = 64;
        static readonly int Strength = Shader.PropertyToID("_Strength");

        public BlobShadows(Transform parent, Material mat)
        {
            _root = new GameObject("Blobs").transform;
            _root.SetParent(parent, false);
            _mat = mat;
        }

        public void Add(Transform follow, float radius, float opacity, float groundY, float heightOffset)
        {
            if (_slots.Count >= Max) return;
            var go = new GameObject("Blob", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_root, false);
            go.transform.localRotation = Quaternion.Euler(90, 0, 0);   // builtin quad faces -z; lay it flat, facing up
            go.GetComponent<MeshFilter>().sharedMesh = GameHost.BuiltinMesh(PrimitiveType.Quad);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _slots.Add(new Slot { Follow = follow, Radius = radius, Opacity = opacity, GroundY = groundY, HeightOffset = heightOffset, Go = go, R = r, Mpb = new MaterialPropertyBlock() });
        }

        public void Update()
        {
            foreach (var s in _slots)
            {
                bool live = s.Follow && s.Follow.gameObject.activeInHierarchy;
                if (s.Go.activeSelf != live) s.Go.SetActive(live);
                if (!live) continue;
                var p = s.Follow.position;
                float h = Mathf.Max(0, p.y - s.GroundY - s.HeightOffset);
                float grow = 1 + Mathf.Min(h, 6) * 0.16f;       // a rising object's shadow spreads out
                float fade = 1 / (1 + Mathf.Min(h, 8) * 0.34f);
                float r = s.Radius * grow;
                s.Go.transform.position = new Vector3(p.x, s.GroundY + 0.012f, p.z);
                s.Go.transform.localScale = new Vector3(r * 2, r * 2, 1);
                s.R.GetPropertyBlock(s.Mpb);
                s.Mpb.SetFloat(Strength, s.Opacity * fade);
                s.R.SetPropertyBlock(s.Mpb);
            }
        }
    }
}
