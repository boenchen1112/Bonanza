// The camera rig (web render/stage.js): the stage drives the camera so a PERFECT
// punches identically in every game.
//
//   Frame / Snap   an arc around a target, eased with a frame-rate independent damp
//   SetPushGain    how hard the camera dollies on the beat (0 disables)
//   Shake          directional damped kick in VIEW space (not white noise), plus a hair of roll
//   PunchZoom      fov punch that snaps back
//   Flash, Chroma  screen-wide, read by the composite pass
//   Hitstop        a VISUAL freeze only (VisualClock): never the audio clock, never judging
//
// Each LateUpdate the pose is rebuilt from the eased base plus this frame's
// offsets (nothing accumulates in the camera transform). Reduce Motion drops
// shake, flash, chroma spikes and the idle drift, and softens the punches.

using UnityEngine;

namespace Bonanza.Look
{
    /// Presentation time. Dt() is 0 while a hitstop is running, so animation and
    /// FX age freeze for a few frames. Judging, the audio clock and the game's
    /// `now` never read this: the freeze can not move a hit window.
    public static class VisualClock
    {
        static float _remaining;
        public static bool Frozen => _remaining > 0;
        public static float Remaining => _remaining;
        public static void Hitstop(float seconds)
        {
            if (Settings.ReduceMotion) return;
            _remaining = Mathf.Max(_remaining, seconds);
        }
        public static float Dt(float dt) => _remaining > 0 ? 0f : dt;
        /// Called once per frame, after the games have asked for their dt.
        internal static void Tick(float realDt) { if (_remaining > 0) _remaining = Mathf.Max(0, _remaining - realDt); }
        public static void Clear() => _remaining = 0;
    }

    public sealed class CameraRig
    {
        const float MaxShake = 0.34f;      // world units
        const float MaxRoll = 0.018f;      // radians
        public const float ChromaRest = 0.0011f;

        readonly Camera _cam;
        readonly Vector3 _origPos; readonly Quaternion _origRot; readonly float _origFov;

        // frame
        bool _active;
        Vector3 _target, _look, _pos;
        float _distance = 8, _height = 2.2f, _yaw, _fov = 50, _lambda = 4.5f;
        bool _snap = true;

        // impulses
        Vector3 _shakeDir = Vector3.right;
        float _shakeAmp, _shakePhase, _roll;
        float _push, _pushGain = 1;
        float _driftGain = 1;
        float _zoom = 1, _zoomTarget = 1;
        float _chroma, _flash;
        Color _flashColor = Color.white;
        float _time;

        public float Flash => _flash;
        public Color FlashColor => _flashColor;
        public float ChromaSpike => _chroma;
        public bool Active => _active;
        public Camera Camera => _cam;

        public CameraRig(Camera cam)
        {
            _cam = cam;
            _origPos = cam.transform.position; _origRot = cam.transform.rotation; _origFov = cam.fieldOfView;
            _pos = _origPos; _fov = _origFov;
        }

        /// Hand the camera to the rig: an arc around `target` at `distance`, `height` above it, `yaw` radians
        /// round (0 = straight on from -z, Unity's side). Eases there at `lambda`.
        public void Frame(Vector3 target, float distance, float height, float yaw, float fov, float lambda = 4.5f, bool immediate = false)
        {
            _target = target; _distance = distance; _height = height; _yaw = yaw; _fov = fov; _lambda = lambda;
            _active = true;
            if (immediate) Snap();
        }

        /// Jump to the framing this frame instead of easing into it.
        public void Snap() { _snap = true; }
        public void Release() { _active = false; }
        public void SetPushGain(float g) => _pushGain = g;
        public void SetDriftGain(float g) => _driftGain = g;

        Vector3 Desired() => new Vector3(
            _target.x + Mathf.Sin(_yaw) * _distance, _target.y + _height, _target.z - Mathf.Cos(_yaw) * _distance);

        /// Directional camera kick. `dir` is in VIEW space: x = screen right, y = screen up, z = toward the camera.
        public void Shake(float amp, Vector3? dir = null)
        {
            if (!(amp > 0) || Settings.ReduceMotion) return;
            _shakeAmp = Mathf.Min(MaxShake, Mathf.Max(_shakeAmp, amp));
            var d = dir ?? new Vector3(0.85f, 0.35f, 0);
            if (d.sqrMagnitude < 1e-6f) d = Vector3.right;
            _shakeDir = d.normalized;
            _shakePhase = 0;
            // A kick that big is an impact: a touch of chroma and roll with it.
            Chroma(Mathf.Clamp01(amp * 2.2f) * 0.004f);
            _roll = Mathf.Clamp(-_shakeDir.x * amp * 0.09f, -MaxRoll, MaxRoll);
        }

        public void DoFlash(float alpha, Color? color = null)
        {
            if (Settings.ReduceMotion || alpha <= _flash) return;
            _flash = alpha;
            _flashColor = color ?? Color.white;
        }

        /// Extra chromatic separation, decaying. Spikes on impact.
        public void Chroma(float amount) { if (!Settings.ReduceMotion) _chroma = Mathf.Max(_chroma, amount); }

        /// Punch-in for big moments: 1 = normal, above 1 = closer. Snaps back unless `snapBack` is false.
        public void PunchZoom(float z, bool snapBack = true)
        {
            if (Settings.ReduceMotion) z = 1 + (z - 1) * 0.35f;
            _zoom = z; _zoomTarget = snapBack ? 1 : z;
        }

        /// A manual beat accent: the camera nudges in a few millimetres.
        public void Pulse(float strength = 1) => _push = Mathf.Max(_push, 0.05f * strength * Gain());

        float Gain() => _pushGain * (Settings.ReduceMotion ? 0.35f : 1f);

        /// Called on each whole beat (downbeats hit harder than the beats between them).
        public void Beat(int beatIndexInBar)
        {
            float strength = beatIndexInBar == 0 ? 1.25f : 0.8f;
            _push = Mathf.Max(_push, 0.045f * strength * Gain());
        }

        /// Forget every impulse (scene change).
        public void Reset() { _shakeAmp = 0; _push = 0; _roll = 0; _chroma = 0; _flash = 0; _zoom = _zoomTarget = 1; }

        static float DampF(float a, float b, float lambda, float dt) => Mathf.Lerp(a, b, 1 - Mathf.Exp(-lambda * dt));

        /// Per frame, with REAL elapsed time (the shake and punch are presentation and do not freeze in a hitstop).
        public void Update(float dt)
        {
            if (!_cam) return;
            _time += dt;
            var tr = _cam.transform;
            bool reduce = Settings.ReduceMotion;

            if (!_active)
            {
                // The game owns the camera's pose: only the grade impulses and the fov punch run.
                if (Mathf.Abs(_zoom - _zoomTarget) > 0.0005f) _zoom = DampF(_zoom, _zoomTarget, 11, dt);
                _cam.fieldOfView = _fov / _zoom;
                _chroma = DampF(_chroma, 0, 9, dt);
                _flash = _flash > 0.001f ? DampF(_flash, 0, 16, dt) : 0;
                return;
            }

            var want = Desired();
            if (_snap) { _pos = want; _look = _target; _snap = false; }
            else
            {
                _pos = new Vector3(DampF(_pos.x, want.x, _lambda, dt), DampF(_pos.y, want.y, _lambda, dt), DampF(_pos.z, want.z, _lambda, dt));
                _look = new Vector3(DampF(_look.x, _target.x, _lambda, dt), DampF(_look.y, _target.y, _lambda, dt), DampF(_look.z, _target.z, _lambda, dt));
            }
            var rot = Quaternion.LookRotation(_look - _pos, Vector3.up);

            var offset = Vector3.zero;
            var fwd = rot * Vector3.forward;

            // shake: a damped sinusoid along a fixed VIEW-space axis
            if (_shakeAmp > 0.0006f)
            {
                _shakePhase += dt * 31f;
                var world = rot * new Vector3(_shakeDir.x, _shakeDir.y, -_shakeDir.z);
                offset += world * (_shakeAmp * Mathf.Sin(_shakePhase * Mathf.PI * 2));
                _shakeAmp = DampF(_shakeAmp, 0, 15, dt);
                _roll = DampF(_roll, 0, 15, dt);
            }
            else { _shakeAmp = 0; _roll = 0; }

            // beat push: a few millimetres along the view axis, snappy in, eased out
            _push = DampF(_push, 0, 7.5f, dt);
            offset += fwd * _push;

            // idle drift: the frame is never perfectly still
            if (_driftGain > 0 && !reduce)
            {
                float d = _driftGain * 0.03f;
                offset += rot * new Vector3(Mathf.Sin(_time * 0.37f) * d, Mathf.Sin(_time * 0.29f + 1.7f) * d * 0.7f, 0);
            }

            if (offset.sqrMagnitude > MaxShake * MaxShake) offset = offset.normalized * MaxShake;
            tr.position = _pos + offset;
            tr.rotation = _roll != 0 ? rot * Quaternion.Euler(0, 0, -_roll * Mathf.Rad2Deg) : rot;

            // punch zoom via fov
            if (Mathf.Abs(_zoom - _zoomTarget) > 0.0005f) _zoom = DampF(_zoom, _zoomTarget, 11, dt);
            _cam.fieldOfView = _fov / _zoom;

            // grade
            _chroma = DampF(_chroma, 0, 9, dt);
            _flash = _flash > 0.001f ? DampF(_flash, 0, 16, dt) : 0;
        }

        /// Hand the camera back as found.
        public void Restore()
        {
            if (!_cam) return;
            _cam.transform.SetPositionAndRotation(_origPos, _origRot);
            _cam.fieldOfView = _origFov;
        }
    }
}
