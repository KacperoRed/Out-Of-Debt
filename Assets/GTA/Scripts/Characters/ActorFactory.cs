using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Składa postać z części (ten sam wygląd co w grze), podpina animacje Kevin Iglesias i zwraca aktora.
    /// player = wygląd bohatera z przebieralni; inaczej wygląd zależny od ziarna gry i klucza (stały przez całą grę).
    /// </summary>
    public static class ActorFactory
    {
        public static KIActor Make(string label, bool player, Transform parent, Vector3 pos, float yaw, bool worldPos = false)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            if (worldPos) { go.transform.position = pos; go.transform.rotation = Quaternion.Euler(0f, yaw, 0f); }
            else { go.transform.localPosition = pos; go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f); }
            var model = new GameObject("ModularModel");
            model.transform.SetParent(go.transform, false);
            var mc = model.AddComponent<ModularCharacter>();
            var ca = CharacterAppearance.Ensure(go, player);
            if (!player) { ca.seed = AppearanceSettings.SeedFor("Other_" + label); ca.noCostumes = true; }
            ca.Rebuild();
            var anim = mc.Animator != null ? mc.Animator : go.GetComponentInChildren<Animator>(true);
            if (anim == null) anim = model.AddComponent<Animator>();
            return KIActor.Attach(anim);
        }

        /// <summary>Korzeń postaci (obiekt utworzony w Make) dla aktora z Animatorem głębiej w hierarchii.</summary>
        public static GameObject RootOf(KIActor a, Transform parent)
        {
            var t = a.transform;
            while (t.parent != null && t.parent != parent) t = t.parent;
            return t.gameObject;
        }
    }
}
