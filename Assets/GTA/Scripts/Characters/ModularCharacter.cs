using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Składa postać z części (ciało + twarz + fryzura + ubrania…). Wszystkie części dzielą jeden szkielet, więc każdy
    /// SkinnedMeshRenderer z prefabu części jest kopiowany na szkielet ciała, a jego kości podmieniane po nazwie.
    /// Prefab części nie jest instancjonowany (czytamy tylko jego siatki i materiały), więc składanie jest tanie – także dla NPC.
    /// </summary>
    public class ModularCharacter : MonoBehaviour
    {
        public struct Entry { public Renderer renderer; public CharacterAppearance.Slot slot; }

        public readonly List<Entry> Entries = new List<Entry>();
        public Animator Animator { get; private set; }

        GameObject _rig;
        readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        readonly List<GameObject> _partObjects = new List<GameObject>();
        bool _warned;

        public bool Ready => _rig != null;

        void EnsureRig(CharacterPartLibrary lib)
        {
            if (_rig != null || lib == null || lib.bodyPrefab == null) return;
            _rig = Instantiate(lib.bodyPrefab, transform, false);
            _rig.name = "Rig";
            _rig.transform.localPosition = new Vector3(0f, lib.yOffset, 0f);
            _rig.transform.localRotation = Quaternion.identity;
            _rig.transform.localScale = Vector3.one * lib.scale;

            foreach (var mb in _rig.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) Destroy(mb);
            foreach (var cc in _rig.GetComponentsInChildren<CharacterController>(true)) Destroy(cc);
            foreach (var c in _rig.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var rb in _rig.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);

            foreach (var t in _rig.GetComponentsInChildren<Transform>(true))
                if (!_bones.ContainsKey(t.name)) _bones[t.name] = t;

            Animator = _rig.GetComponentInChildren<Animator>(true);
            if (Animator == null) Animator = _rig.AddComponent<Animator>();
            if (lib.controller != null) Animator.runtimeAnimatorController = lib.controller;
            if (lib.avatar != null && Animator.avatar == null) Animator.avatar = lib.avatar;
            Animator.applyRootMotion = false;
            Animator.cullingMode = AnimatorCullingMode.CullCompletely;
        }

        /// <summary>
        /// selection[i] = numer części w kategorii i (−1 = brak). Zwraca true, gdy zbudowano.
        /// </summary>
        public bool Build(CharacterPartLibrary lib, int[] selection)
        {
            if (lib == null) return false;
            EnsureRig(lib);
            if (_rig == null) return false;

            foreach (var go in _partObjects) if (go != null) Destroy(go);
            _partObjects.Clear();
            Entries.Clear();

            // ciało: renderery z prefabu ciała to skóra
            foreach (var r in _rig.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer)) Entries.Add(new Entry { renderer = r, slot = CharacterAppearance.Slot.Skin });

            bool full = false;
            for (int i = 0; i < lib.categories.Count && i < selection.Length; i++)
                if (lib.categories[i].isFull && selection[i] >= 0) full = true;

            for (int i = 0; i < lib.categories.Count && i < selection.Length; i++)
            {
                var cat = lib.categories[i];
                int s = selection[i];
                if (s < 0 || s >= cat.parts.Count || cat.parts[s] == null) continue;
                if (full && cat.hiddenByFull) continue;
                AddPart(cat, cat.parts[s]);
            }
            return true;
        }

        void AddPart(CharacterPartLibrary.Category cat, GameObject part)
        {
            // części skinowane: kopiujemy mesh + materiały, kości mapujemy po nazwie na szkielet ciała
            foreach (var src in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var go = new GameObject("Part_" + cat.id + "_" + part.name);
                go.transform.SetParent(_rig.transform, false);
                var dst = go.AddComponent<SkinnedMeshRenderer>();
                dst.sharedMesh = src.sharedMesh;
                dst.sharedMaterials = src.sharedMaterials;
                var bones = new Transform[src.bones.Length];
                for (int i = 0; i < bones.Length; i++)
                    bones[i] = src.bones[i] != null && _bones.TryGetValue(src.bones[i].name, out var b) ? b : null;
                dst.bones = bones;
                if (src.rootBone != null && _bones.TryGetValue(src.rootBone.name, out var root)) dst.rootBone = root;
                else if (_bones.TryGetValue("Hips", out var hips)) dst.rootBone = hips;
                dst.localBounds = src.localBounds;
                dst.updateWhenOffscreen = false;
                dst.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (!_warned && System.Array.IndexOf(bones, null) >= 0)
                {
                    _warned = true;
                    Debug.LogWarning("[MiniGTA] Część postaci " + part.name + " ma kości, których nie ma w szkielecie ciała – może się źle wyświetlać.");
                }
                _partObjects.Add(go);
                Entries.Add(new Entry { renderer = dst, slot = cat.slot });
            }

            // części sztywne (np. akcesoria zamocowane na kości głowy)
            foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null) continue;
                Transform anchor = _rig.transform;
                for (var t = mf.transform.parent; t != null; t = t.parent)
                    if (_bones.TryGetValue(t.name, out var found)) { anchor = found; break; }
                var go = new GameObject("Part_" + cat.id + "_" + mf.name);
                go.transform.SetParent(anchor, false);
                go.transform.localPosition = mf.transform.localPosition;
                go.transform.localRotation = mf.transform.localRotation;
                go.transform.localScale = mf.transform.localScale;
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var dst = go.AddComponent<MeshRenderer>();
                dst.sharedMaterials = mr.sharedMaterials;
                _partObjects.Add(go);
                Entries.Add(new Entry { renderer = dst, slot = cat.slot });
            }
        }
    }
}
