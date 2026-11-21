using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace MiniGTA
{
    /// <summary>
    /// Aktor odtwarzający animację humanoidalną z paczki Kevin Iglesias przez PlayableGraph (bez Animator Controllera).
    /// Czas liczony nieskalowany, więc działa też przy Time.timeScale = 0 (katscena).
    /// </summary>
    public class KIActor : MonoBehaviour
    {
        Animator _anim;
        PlayableGraph _graph;
        AnimationClipPlayable _pl;
        AnimationClip _clip;
        float _t;
        bool _has;

        public static KIActor Create(GameObject model, Avatar avatar, Transform parent, Vector3 pos, float yaw, Color tint)
        {
            var go = Instantiate(model, parent);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var a = go.GetComponent<Animator>();
            if (a == null) a = go.AddComponent<Animator>();
            if (a.avatar == null) a.avatar = avatar;
            a.applyRootMotion = false;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.materials;
                foreach (var m in mats) { if (m.HasProperty("_Color")) m.color = tint; }
                r.materials = mats;
            }
            var actor = go.AddComponent<KIActor>();
            actor._anim = a;
            return actor;
        }

        /// <summary>Podpina animacje KI pod Animator istniejącej postaci (np. złożonej z części): kontroler wyłączony, klipy humanoidalne.</summary>
        public static KIActor Attach(Animator a)
        {
            a.runtimeAnimatorController = null;
            a.applyRootMotion = false;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var k = a.gameObject.AddComponent<KIActor>();
            k._anim = a;
            return k;
        }

        public void Play(AnimationClip clip)
        {
            if (clip == null || clip == _clip) return;
            _clip = clip; _t = Random.value * clip.length;
            if (!_has)
            {
                _graph = PlayableGraph.Create("KIActor");
                _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(_graph, "out", _anim);
                _pl = AnimationClipPlayable.Create(_graph, clip);
                output.SetSourcePlayable(_pl);
                _has = true;
            }
            else
            {
                _graph.DestroyOutput(_graph.GetOutput(0));
                _pl.Destroy();
                var output = AnimationPlayableOutput.Create(_graph, "out", _anim);
                _pl = AnimationClipPlayable.Create(_graph, clip);
                output.SetSourcePlayable(_pl);
            }
        }

        void Update()
        {
            if (!_has || _clip == null) return;
            _t += Time.unscaledDeltaTime;
            float len = Mathf.Max(0.01f, _clip.length);
            _pl.SetTime(_t % len);
            _graph.Evaluate(0f);
        }

        void OnDestroy() { if (_has && _graph.IsValid()) _graph.Destroy(); }
    }
}
