using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace MiniGTA
{
    /// <summary>
    /// Sprzedawca / kasjer / barman: w miejscu niewidzialnej kapsuły (collider do celowania) pojawia się model postaci z paczki
    /// (kopia przechodnia bez AI, sieci i fizyki), ubrany losowo przez CharacterAppearance. Stoi w miejscu w pozie spoczynkowej.
    /// </summary>
    public class Shopkeeper : MonoBehaviour
    {
        public GameObject[] sources;

        void Start()
        {
            if (sources == null || sources.Length == 0) return;
            int idx = Mathf.Abs(Mathf.RoundToInt(transform.position.x * 7.3f + transform.position.z * 3.1f)) % sources.Length;
            var src = sources[idx];
            if (src == null) return;

            // kapsuła ma środek 1 m nad stopami
            Vector3 feet = transform.position - Vector3.up;
            var go = Instantiate(src, feet, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), transform.parent);
            go.name = name + "_Model";

            // zostaje sam model: wyrzucamy AI, sieć, fizykę i kolidery
            // najpierw skrypty zależne (AI, sklepy…), na końcu Health – inne skrypty mogą go wymagać
            for (int pass = 0; pass < 2; pass++)
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    if (mb is CharacterAppearance || mb is ModularCharacter) continue;
                    if ((mb is Health) != (pass == 1)) continue;
                    DestroyImmediate(mb);
                }
            foreach (var a in go.GetComponentsInChildren<NavMeshAgent>(true)) DestroyImmediate(a);
            foreach (var n in go.GetComponentsInChildren<NetworkObject>(true)) DestroyImmediate(n);
            foreach (var c in go.GetComponentsInChildren<CharacterController>(true)) DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (var r in go.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(r);

            var ca = CharacterAppearance.Ensure(go, false, false);
            ca.seed = AppearanceSettings.SeedFor("shop" + Mathf.RoundToInt(transform.position.x) + "_" + Mathf.RoundToInt(transform.position.z));
            NameTag.Add(go);
        }
    }
}
