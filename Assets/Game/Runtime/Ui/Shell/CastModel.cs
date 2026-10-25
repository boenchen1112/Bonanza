// One imported cast body (Resources/Cast/cast-<id>.prefab, built by Editor/CastSetup.cs from Assets/Art/Cast/*.fbx): the
// skinned mesh with house-toon materials and its idle clip, played through the Playables API (no controller asset) and
// stepped by hand, so a still screen (reduced motion) is just a frame that is not advanced.

using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Bonanza.Shell
{
    public sealed class CastModel : MonoBehaviour
    {
        /// Filled by the importer script.
        public AnimationClip Idle;
        public SkinnedMeshRenderer Body;
        /// Feet at y 0, head top at this height (metres, unscaled); measured by the importer script.
        public float Height = 1.7f;

        PlayableGraph _graph;
        AnimationClipPlayable _play;
        float _t;
        bool _built;

        public static GameObject Load(string id) => Resources.Load<GameObject>("Cast/cast-" + id);

        void Build()
        {
            if (_built) return;
            _built = true;
            if (Idle == null) return;
            var anim = GetComponentInChildren<Animator>();
            if (anim == null) anim = gameObject.AddComponent<Animator>();
            anim.enabled = true;
            _graph = PlayableGraph.Create("CastIdle");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_graph, "out", anim);
            _play = AnimationClipPlayable.Create(_graph, Idle);
            _play.SetApplyFootIK(false);
            output.SetSourcePlayable(_play);
            _graph.Play();
        }

        /// Pose the idle at `seconds` (looped) now.
        public void Pose(float seconds)
        {
            Build();
            if (!_graph.IsValid() || Idle == null) return;
            _t = seconds;
            float len = Mathf.Max(0.01f, Idle.length);
            _play.SetTime(Mathf.Repeat(seconds, len));
            _graph.Evaluate(0);
        }

        Transform _lArm, _lFore, _lHand, _rArm, _rFore, _rHand, _head;
        bool _bound;

        void Bind()
        {
            if (_bound) return;
            _bound = true;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "mixamorigLeftArm": _lArm = t; break;
                    case "mixamorigLeftForeArm": _lFore = t; break;
                    case "mixamorigLeftHand": _lHand = t; break;
                    case "mixamorigRightArm": _rArm = t; break;
                    case "mixamorigRightForeArm": _rFore = t; break;
                    case "mixamorigRightHand": _rHand = t; break;
                    case "mixamorigHead": _head = t; break;
                }
            }
        }

        /// The hand a held prop belongs to (the right one), or null for a rig without arm bones.
        public Transform HeadBone { get { Bind(); return _head; } }
        public Transform HeldHand { get { Bind(); return _rHand; } }

        /// A procedural arms-up cheer laid over the pose just evaluated: both arms swing toward up and a little out
        /// (`amount` 0..1), pumping with `phase`. Call after Pose/Step, every frame it should show (the idle
        /// re-evaluation resets the bones each time).
        public void Cheer(float amount, float phase, float pump)
        {
            if (amount <= 0.001f) return;
            Bind();
            Reach(_lArm, _lFore, _lHand, amount, phase, pump);
            Reach(_rArm, _rFore, _rHand, amount, phase + 1.7f, pump);
        }

        void Reach(Transform arm, Transform fore, Transform hand, float amount, float phase, float pump)
        {
            if (!arm || !fore || !hand) return;
            float side = Mathf.Sign(transform.InverseTransformPoint(arm.position).x + 1e-4f);
            var up = transform.up; var outv = transform.right * side;
            float p = Mathf.Sin(phase) * pump;
            var upper = (up * 0.8f + outv * (0.95f + 0.2f * p) - transform.forward * 0.05f).normalized;
            arm.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation((fore.position - arm.position).normalized, upper), amount) * arm.rotation;
            var lower = (up * 1f + outv * (0.4f - 0.3f * p)).normalized;
            fore.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation((hand.position - fore.position).normalized, lower), amount) * fore.rotation;
        }

        /// Advance the idle by dt.
        public void Step(float dt) => Pose(_t + dt);

        void OnDestroy() { if (_graph.IsValid()) _graph.Destroy(); }
    }
}
