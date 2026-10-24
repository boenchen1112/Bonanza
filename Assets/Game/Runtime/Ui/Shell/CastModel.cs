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

        /// Advance the idle by dt.
        public void Step(float dt) => Pose(_t + dt);

        void OnDestroy() { if (_graph.IsValid()) _graph.Destroy(); }
    }
}
