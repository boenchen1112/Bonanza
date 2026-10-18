// The house look for one game: a palette, the three-light rig (key, hemisphere
// fill, kick), a gradient sky dome, linear fog and blob shadows. Shaders write linear light;
// Bonanza/Composite (a Full Screen Pass feature on the URP renderers) does exposure, tone curve, saturation, vignette, all written
// to shader globals once a frame. Toon materials read those globals, so a
// palette change re-lights every surface with no per-material work.
//
// Shaders and materials come from Resources/Look (never Shader.Find, which is
// null in a WebGL player). The lights are evaluated in the shader, shadows are
// blobs. A scene can ask for the key's REAL shadow inside a focus circle
// (ShadowFocus; the scene's directional Light carries it, High/Medium tiers).
//
// The rig also owns the camera rig (Rig), the pooled effects (Fx), the game's
// environment set (Env, built with CreateEnv from Look/Env and pulsed on every
// beat TickBeat sees) and the post-chain inputs (bloom, chroma, flash), driven
// in one LateUpdate so the order is fixed: camera, then sky, globals, set, effects.

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
        /// The camera rig: frame, shake, punch zoom, flash, chroma. Valid after Create.
        public CameraRig Rig { get; private set; }
        /// The pooled effects: verdict bursts, rings, trails, callouts. Valid after Create.
        public Fx Fx { get; private set; }
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
            var cam = Camera.main;
            if (cam) Rig = new CameraRig(cam);
            Fx = new Fx(transform, this, Rig);
            Fx.VerdictColor = v => Palette.Live[v == Core.Verdict.Perfect ? PC.Perfect : v == Core.Verdict.Great ? PC.Great : v == Core.Verdict.Good ? PC.Good : PC.Miss];
            Push();
        }

        /// A point in front of the camera, for touching an effect once before play.
        public Vector3 PrewarmPoint() { var c = Camera.main; return c ? c.transform.position + c.transform.forward * 6 : Vector3.zero; }

        // ------------------------------------------------------------ beat, hitstop, shadow focus

        int _lastBeat = int.MinValue;

        /// Call once a frame with the audio beat: the camera pushes in a few millimetres on every beat, and the
        /// environment (if any) pulses: downbeats harder than the beats between (web stage.js trackBeat).
        public void TickBeat(double beat)
        {
            TrackEnvBeat(beat);
            if (beat < 0) { _lastBeat = int.MinValue; return; }
            int whole = (int)System.Math.Floor(beat);
            if (whole == _lastBeat) return;
            int index = ((whole % 4) + 4) % 4;
            if (_lastBeat != int.MinValue || whole >= 0) Rig?.Beat(index);
            _lastBeat = whole;
            Env?.Pulse(index == 0 ? 1.25f : 0.8f, index);
        }

        // ------------------------------------------------------------ environment kit

        /// The set this game built with CreateEnv (null if none).
        public Env Env { get; private set; }
        double _envBeat, _tickBeat; float _tickTime = -1, _bps = 2, _envTime; int _tickFrame = -1;

        /// Build the game's environment set from a preset (Look/Env). `configure` adjusts the per-piece options
        /// before anything is built. Call from Load(): every mesh and material is made here, never mid-round.
        public Env CreateEnv(EnvPreset preset, System.Action<EnvOptions> configure = null, uint seed = 0xba5e)
        {
            Env?.Destroy();
            var o = new EnvOptions();
            configure?.Invoke(o);
            Env = new Env(this, transform, seed);
            Env.Build(preset, o);
            Env.Update(0, 0, 0);
            return Env;
        }

        /// The env's beat follows TickBeat while the game ticks, and runs on at the last tempo after (results).
        void TrackEnvBeat(double beat)
        {
            float now = Time.unscaledTime;
            if (_tickTime >= 0 && now > _tickTime + 1e-4f && beat > _tickBeat)
                _bps = Mathf.Lerp(_bps, (float)((beat - _tickBeat) / (now - _tickTime)), 0.2f);
            _tickBeat = beat; _tickTime = now; _tickFrame = Time.frameCount;
            _envBeat = beat;
        }

        void UpdateEnv(float sceneDt)
        {
            if (Env == null) return;
            if (_tickFrame != Time.frameCount) _envBeat += sceneDt * _bps;
            _envTime += sceneDt;   // scene time: a pause freezes the set
            Env.OnQuality();
            Env.Update(sceneDt, _envBeat, _envTime);
        }

        /// A visual-only freeze (hitstop). Animation and FX age on VisualDt() and stand still; judging never reads it.
        public void Hitstop(float seconds) => VisualClock.Hitstop(seconds);
        public static float VisualDt(float dt) => VisualClock.Dt(dt);

        bool _focusOn; Vector3 _focusCentre; float _focusRadius;
        Light _key; LightShadows _keyShadows; Quaternion _keyRot; float _keyBias, _keyNormalBias, _keyStrength;
        UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset _rp;
        float _origDist; int _origCascades, _origRes;
        bool _shadowActive; QualityTier _shadowTier;

        /// The key light casts a real shadow, but only inside a circle of `radius` around `centre` (web setShadowFocus):
        /// the few objects the player watches. On High and Medium; Low keeps the blob shadows only. Mark casters with
        /// ShadowCaster(); every Toon surface receives.
        public void ShadowFocus(Vector3 centre, float radius)
        {
            _focusOn = true; _focusCentre = centre; _focusRadius = radius;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.isActiveAndEnabled) { _key = l; break; }
            if (_key)
            {
                _keyShadows = _key.shadows; _keyRot = _key.transform.rotation;
                _keyBias = _key.shadowBias; _keyNormalBias = _key.shadowNormalBias; _keyStrength = _key.shadowStrength;
            }
            _rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        }

        /// A mesh that casts the key's shadow (everything else on a look-kit stage does not).
        public static void ShadowCaster(GameObject go, bool on = true)
        {
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                r.shadowCastingMode = on ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void UpdateShadow()
        {
            if (!_focusOn || !_key || _rp == null || !_rp.supportsMainLightShadows) { Shader.SetGlobalVector("_BonShadowFocus", Vector4.zero); return; }
            var tier = RenderQuality.Level.Tier;
            bool want = tier != QualityTier.Low;
            if (want && (!_shadowActive || tier != _shadowTier))
            {
                if (!_shadowActive) { _origDist = _rp.shadowDistance; _origCascades = _rp.shadowCascadeCount; _origRes = _rp.mainLightShadowmapResolution; }
                // One cascade over camera -> far side of the focus circle: the box the shadow map is spent on.
                float far = _cam ? Vector3.Distance(_cam.transform.position, _focusCentre) + _focusRadius + 0.5f : 20f;
                _rp.shadowDistance = Mathf.Clamp(far, 8f, 40f);
                _rp.shadowCascadeCount = 1;
                _rp.mainLightShadowmapResolution = tier == QualityTier.High ? 2048 : 1024;
                _key.shadows = tier == QualityTier.High && _rp.supportsSoftShadows ? LightShadows.Soft : LightShadows.Hard;
                _key.shadowStrength = 1;
                _key.shadowBias = 0.02f; _key.shadowNormalBias = 0.04f;   // a big normal bias shrinks thin casters (batter, bat, plate) out of the map
                _shadowActive = true; _shadowTier = tier;
            }
            else if (!want && _shadowActive) RestoreShadow();
            if (_shadowActive)
            {
                var d = Palette.Live[PD.KeyDir];
                // The shadow map's light is the toon key swung sideways and lowered: with the stock direction the shadow
                // falls straight behind the batter and the camera never sees it; this one lies out to the side, in view.
                Vector3 tl = Dir(d);
                tl = new Vector3(Mathf.Sign(tl.x == 0 ? -1 : tl.x) * Mathf.Max(Mathf.Abs(tl.x) * 1.5f, 0.7f), tl.y * 0.8f, -0.2f).normalized;
                _key.transform.rotation = Quaternion.LookRotation(-tl);   // forward points away from the light
                Shader.SetGlobalVector("_BonShadowFocus", new Vector4(_focusCentre.x, _focusCentre.y, _focusCentre.z, _focusRadius));
            }
            else Shader.SetGlobalVector("_BonShadowFocus", Vector4.zero);
        }

        void RestoreShadow()
        {
            if (!_shadowActive) return;
            _shadowActive = false;
            if (_rp != null) { _rp.shadowDistance = _origDist; _rp.shadowCascadeCount = _origCascades; _rp.mainLightShadowmapResolution = _origRes; }
            if (_key) { _key.shadows = _keyShadows; _key.transform.rotation = _keyRot; _key.shadowBias = _keyBias; _key.shadowNormalBias = _keyNormalBias; _key.shadowStrength = _keyStrength; }
        }

        void OnDestroy()
        {
            // The camera, the scene's light and the pipeline asset outlive the game: hand them back as found.
            if (_camSaved && _cam) { _cam.clearFlags = _flags; _cam.backgroundColor = _bg; }
            Rig?.Restore();
            Fx?.Destroy();
            Env?.Destroy();
            Env = null;
            RestoreShadow();
            Shader.SetGlobalVector("_BonShadowFocus", Vector4.zero);
            VisualClock.Clear();
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
            PostState.Frame = Time.frameCount;
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
            // Order matters: the rig moves the camera, then the sky follows it, then the globals and effects read it.
            float sceneDt = Time.deltaTime;   // 0 while paused, so a pause freezes the camera and every effect
            Rig?.Update(sceneDt);
            if (_cam && _sky) _sky.position = _cam.transform.position;
            Push();
            UpdateEnv(sceneDt);
            Fx?.Update(VisualClock.Dt(sceneDt), sceneDt, _cam);
            _blobs?.Update();
            UpdateShadow();
            VisualClock.Tick(dt);
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
            Shader.SetGlobalVector("_BonPostParams", new Vector4(p[PF.Exposure], p[PF.Saturation], p[PF.Vignette] * RenderQuality.Vignette, _time % 100f));
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

            // Post chain inputs: bloom from the palette (linear light, before the curve), the chroma that spikes on
            // hits and decays, the white flash.
            PostState.Bloom = p[PF.Bloom]; PostState.Threshold = p[PF.BloomThreshold];
            float chroma = CameraRig.ChromaRest + (Rig != null ? Rig.ChromaSpike : 0) + pulse * 0.0006f;
            float flash = Rig != null ? Rig.Flash * 0.85f : 0;
            Shader.SetGlobalVector("_BonFxParams", new Vector4(p[PF.Bloom], PostState.Wide ? 1 : 0, chroma, flash));
            var fc = Rig != null ? Rig.FlashColor.linear : Color.white;
            Shader.SetGlobalVector("_BonFlashColor", new Vector4(fc.r, fc.g, fc.b, 1));
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
