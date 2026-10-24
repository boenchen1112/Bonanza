// The cast on the hall's stage for the roster: the focused character, large and lit, standing where the roster screen
// asks (a point and a height in the 1280x720 reference, so it keeps its place at any aspect), and the eight bust
// portraits the grid cards show.
//
//   - The live body is one of eight pooled instances (CastModel prefabs). A focus change squashes the old one away and
//     pops the new one in; Pop() is the lock-in hop. Under reduced motion it swaps at once and holds one idle frame.
//   - The portraits are baked once per session: a second, disabled camera is switched on for one frame per character,
//     looking at a spare copy of the body parked far from the hall, and renders into a small RenderTexture.
//
// Everything is placed by projecting from the shell camera (ViewportPointToRay onto the deck), after the rig has moved
// it this frame, so the figure stands on the boards however the pose or the aspect changes.

using Bonanza.Core;
using Bonanza.HudKit;
using UnityEngine;

namespace Bonanza.Shell
{
    [DefaultExecutionOrder(1000)]
    public sealed class CastStage : MonoBehaviour
    {
        public const int PortraitSize = 256;
        const float RefH = 720f;
        static readonly Vector3 BakeAt = new Vector3(300, 0, 300);

        ShellBackdrop _hall;
        Camera _main;
        readonly GameObject[] _bodies = new GameObject[8];
        readonly CastModel[] _models = new CastModel[8];
        readonly RenderTexture[] _portraits = new RenderTexture[8];
        Material _pool, _halo;
        Transform _poolT, _haloT;

        // focus state
        int _shown = -1, _leaving = -1;
        float _enter = 1, _leave, _pop, _clock, _fade;
        float _showAmt;                       // 0..1 stage fade
        bool _on;
        Vector3 _feet; float _scale = 1;
        Color _tint = Color.white;

        // placement request
        float _refX = 255, _feetY = 470, _height = 300;
        bool _dim;

        // baking
        Camera _bakeCam;
        GameObject _bakeBody;
        int _bakeIndex = -1, _bakeNext;
        public int BakedCount { get; private set; }
        public bool AllBaked => BakedCount >= 8;

        public static CastStage Create(ShellBackdrop hall)
        {
            var go = new GameObject("CastStage");
            go.transform.SetParent(hall.transform, false);
            var s = go.AddComponent<CastStage>();
            s._hall = hall;
            s.BuildSet();
            go.SetActive(false);
            return s;
        }

        void BuildSet()
        {
            _pool = _hall.Card(new Color(1f, 0.8f, 0.5f), 0.9f, 0.5f, false);
            _halo = _hall.Card(new Color(1f, 0.8f, 0.5f), 0.7f, 0.2f, true);
            _poolT = _hall.CardAt("CastPool", _pool, Vector3.zero, new Vector3(90, 0, 0), new Vector2(4, 4), transform).transform;
            _haloT = _hall.CardAt("CastHalo", _halo, Vector3.zero, Vector3.zero, new Vector2(4, 4), transform).transform;
        }

        // ------------------------------------------------------------------ public

        /// The roster is on (stage visible and baking) or off.
        public void Show(bool on)
        {
            _on = on;
            if (on) gameObject.SetActive(true);
        }

        /// Where the figure stands: x from the screen centre and the feet's distance from the top, in reference px, and
        /// its height in reference px. `dim`: the figure steps back (a stage with nothing to choose).
        public void Place(float refX, float feetFromTop, float heightPx, bool dim)
        {
            _refX = refX; _feetY = feetFromTop; _height = heightPx; _dim = dim;
        }

        /// Make `castIndex` the figure on stage.
        public void Focus(int castIndex)
        {
            if (castIndex == _shown) return;
            bool red = Settings.ReduceMotion;
            if (_shown >= 0 && !red) { _leaving = _shown; _leave = 1; }
            else if (_shown >= 0 && _bodies[_shown]) _bodies[_shown].SetActive(false);
            _shown = castIndex;
            _enter = red ? 1 : 0;
            Ensure(castIndex);
            if (_models[castIndex]) _models[castIndex].Pose(red ? 1.2f : _clock);
            var c = Cast.All[castIndex];
            _tint = new Color(((c.Color >> 16) & 255) / 255f, ((c.Color >> 8) & 255) / 255f, (c.Color & 255) / 255f);
        }

        /// A little hop: the character was locked in.
        public void Pop() { if (!Settings.ReduceMotion) _pop = 1; }

        public RenderTexture Portrait(int castIndex) => _portraits[castIndex] != null && _portraits[castIndex].IsCreated() && IsBaked(castIndex) ? _portraits[castIndex] : null;
        readonly bool[] _baked = new bool[8];
        public bool IsBaked(int i) => _baked[i];

        public void Hide()
        {
            _on = false;
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            bool red = Settings.ReduceMotion;
            _showAmt = Mathf.MoveTowards(_showAmt, _on ? 1 : 0, dt / (red ? 0.15f : 0.4f));
            if (!_on && _showAmt <= 0.001f)
            {
                SetAllBodies(false);
                _leaving = -1;
                SetGlow(false);
                gameObject.SetActive(false);
                return;
            }
            StepBake();
            if (!_main) _main = Camera.main;
            if (!_main || _shown < 0) { SetGlow(false); return; }
            _clock += red ? 0 : dt;
            _enter = Mathf.MoveTowards(_enter, 1, dt / 0.38f);
            _leave = Mathf.MoveTowards(_leave, 0, dt / 0.16f);
            _pop = Mathf.MoveTowards(_pop, 0, dt / 0.75f);
            _fade = Mathf.MoveTowards(_fade, _dim ? 1 : 0, dt / 0.3f);

            // where the feet go: the ray through the wanted screen point onto the deck
            float aspect = Mathf.Max(0.5f, _main.aspect);
            float vx = 0.5f + _refX / (RefH * aspect);
            float vy = 1f - _feetY / RefH;
            var ray = _main.ViewportPointToRay(new Vector3(vx, vy, 0));
            if (Mathf.Abs(ray.direction.y) < 1e-3f) return;
            float t = (0f - ray.origin.y) / ray.direction.y;
            if (t <= 0) return;
            _feet = ray.origin + ray.direction * t;
            float depth = Vector3.Dot(_feet - _main.transform.position, _main.transform.forward);
            float worldH = _height / RefH * 2f * depth * Mathf.Tan(_main.fieldOfView * 0.5f * Mathf.Deg2Rad);

            // face the camera, with a slow sway
            var toCam = _main.transform.position - _feet; toCam.y = 0;
            float yaw = Mathf.Atan2(toCam.x, toCam.z) * Mathf.Rad2Deg;
            float sway = red ? 0 : Mathf.Sin(_clock * 0.7f) * 9f;

            if (_leaving >= 0 && _bodies[_leaving])
            {
                if (_leave <= 0.001f) { _bodies[_leaving].SetActive(false); _leaving = -1; }
                else Pose(_leaving, yaw - 30f * (1 - _leave), worldH, Mathf.Lerp(0.0f, 1f, _leave), 1f, 0, false);
            }
            float e = UiKit.BackOut(_enter, 2.1f);
            float pk = Mathf.Clamp01(1 - _pop);
            float hop = Mathf.Sin(Mathf.Min(1f, pk * 1.6f) * Mathf.PI) * (_pop > 0 ? 1 : 0) * 0.2f;
            float twirl = red ? 0 : UiKit.EaseOutCubic(Mathf.Clamp01(pk * 1.25f)) * 360f * (_pop > 0 ? 1 : 0);
            if (_pop <= 0) twirl = 0;
            float squash = 1 + 0.09f * Mathf.Sin(Mathf.Clamp01(1 - _pop) * Mathf.PI * 3f) * _pop;
            float sc = Mathf.Lerp(0.55f, 1f, e) * Mathf.Lerp(1f, 0.82f, _fade);
            Pose(_shown, yaw + sway + twirl + (1 - Mathf.Clamp01(_enter)) * 40f, worldH, sc * Mathf.Clamp01(_enter * 4f + (red ? 1 : 0)), 1f / squash, hop * worldH, true);

            // glow set: pool on the boards, halo behind the figure
            var rim = _tint;
            float a = _showAmt * (0.4f + 0.6f * Mathf.Clamp01(_enter)) * (1 - 0.5f * _fade);
            SetGlow(true);
            _poolT.position = _feet + Vector3.up * 0.04f;
            _poolT.localScale = new Vector3(worldH * 1.5f, worldH * 1.5f, 1);
            var back = _main.transform.right;
            _haloT.position = _feet + Vector3.up * worldH * 0.55f - toCam.normalized * 0.8f;
            _haloT.localScale = new Vector3(worldH * 1.7f, worldH * 1.7f, 1);
            var pc = Color.Lerp(rim, Color.white, 0.25f);
            _pool.SetColor("_Color", pc);
            _pool.SetFloat("_Intensity", 0.75f * a + _pop * 0.5f);
            _halo.SetColor("_Color", rim);
            _halo.SetFloat("_Intensity", 0.38f * a + _pop * 0.4f);
        }

        void SetGlow(bool on)
        {
            if (_poolT) _poolT.gameObject.SetActive(on);
            if (_haloT) _haloT.gameObject.SetActive(on);
        }

        void SetAllBodies(bool on) { foreach (var b in _bodies) if (b) b.SetActive(on); }

        void Pose(int i, float yaw, float worldH, float scale, float squashY, float lift, bool animate)
        {
            var go = _bodies[i];
            if (!go) return;
            if (!go.activeSelf) go.SetActive(true);
            var m = _models[i];
            float k = m != null && m.Height > 0.1f ? worldH / m.Height : 1f;
            // the shorter ones read a little shorter on stage: scaled by their own height, softened
            float rel = Mathf.Clamp(Mathf.Pow(m != null ? m.Height / 1.9f : 1f, 0.5f), 0.8f, 1.12f);
            go.transform.position = _feet + Vector3.up * lift;
            float lean = animate && !Settings.ReduceMotion ? Mathf.Sin(_clock * 1.3f) * 2.2f : 0f;
            go.transform.rotation = Quaternion.Euler(lean * 0.6f, yaw, lean);
            float s = k * scale * rel;
            go.transform.localScale = new Vector3(s / Mathf.Sqrt(squashY), s * squashY, s / Mathf.Sqrt(squashY));
            if (animate && m != null) m.Step(Settings.ReduceMotion ? 0 : Time.unscaledDeltaTime);
        }

        void Ensure(int i)
        {
            if (_bodies[i]) return;
            var prefab = CastModel.Load(Cast.All[i].Id);
            if (!prefab) { Debug.LogWarning("[CAST] missing prefab for " + Cast.All[i].Id); return; }
            var go = Instantiate(prefab, transform);
            go.name = "cast-" + Cast.All[i].Id;
            _bodies[i] = go;
            _models[i] = go.GetComponent<CastModel>();
            go.SetActive(false);
        }

        // ------------------------------------------------------------------ portraits

        void StepBake()
        {
            if (_bakeCam != null && _bakeCam.enabled)
            {
                // the frame with the camera on has rendered
                _bakeCam.enabled = false;
                if (_bakeIndex >= 0) { _baked[_bakeIndex] = true; BakedCount++; }
                if (_bakeBody) { Destroy(_bakeBody); _bakeBody = null; }
                _bakeIndex = -1;
            }
            if (_bakeNext >= 8 || !_on) return;
            int i = _bakeNext++;
            var prefab = CastModel.Load(Cast.All[i].Id);
            if (!prefab) { _baked[i] = false; return; }
            if (_bakeCam == null)
            {
                var cgo = new GameObject("PortraitCam");
                cgo.transform.SetParent(transform, false);
                _bakeCam = cgo.AddComponent<Camera>();
                _bakeCam.enabled = false;
                _bakeCam.clearFlags = CameraClearFlags.SolidColor;
                _bakeCam.nearClipPlane = 0.2f; _bakeCam.farClipPlane = 30f;
                _bakeCam.fieldOfView = 24f;
                _bakeCam.allowHDR = false; _bakeCam.allowMSAA = false;
                _bakeCam.depth = -50;
            }
            var tex = new RenderTexture(PortraitSize, PortraitSize, 16, RenderTextureFormat.ARGB32) { name = "portrait-" + Cast.All[i].Id, antiAliasing = 1, filterMode = FilterMode.Bilinear };
            tex.Create();
            _portraits[i] = tex;
            _bakeBody = Instantiate(prefab, BakeAt, Quaternion.Euler(0, 8, 0));
            var m = _bakeBody.GetComponent<CastModel>();
            float h = m != null ? m.Height : 1.9f;
            m?.Pose(1.0f);
            // a bust: the head and shoulders in a square
            var tgt = BakeAt + Vector3.up * h * 0.74f;
            float dist = h * 0.34f / Mathf.Tan(_bakeCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _bakeCam.transform.position = tgt + new Vector3(0, h * 0.02f, 1f) * 0 + new Vector3(0.0f, h * 0.03f, dist);
            _bakeCam.transform.rotation = Quaternion.LookRotation(tgt - _bakeCam.transform.position, Vector3.up);
            var c = Cast.All[i];
            var col = new Color(((c.Color >> 16) & 255) / 255f, ((c.Color >> 8) & 255) / 255f, (c.Color & 255) / 255f);
            _bakeCam.backgroundColor = Color.Lerp(new Color(0.07f, 0.04f, 0.16f), col, 0.34f);
            _bakeCam.targetTexture = tex;
            _bakeIndex = i;
            _bakeCam.enabled = true;
        }

        void OnDestroy()
        {
            foreach (var r in _portraits) if (r) { r.Release(); Destroy(r); }
            if (_pool) Destroy(_pool);
            if (_halo) Destroy(_halo);
        }
    }
}
