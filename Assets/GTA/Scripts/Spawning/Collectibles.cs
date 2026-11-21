using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Ukryte paczki: złote skrzynki rozsiane po chodnikach miasta (te same miejsca w każdej grze).
    /// Zbierasz je pieszo lub autem; każda daje gotówkę, komplet daje dużą premię. Postęp jest w zapisie (flagi „pk_N”).
    /// Logika tylko na hoście; skrzynki widzi host. Dodawany przez HUD, niczego nie trzeba budować w scenie.
    /// </summary>
    public class Collectibles : MonoBehaviour
    {
        public const int Total = 15;
        public const int Reward = 250;
        public const int Bonus = 6000;

        public static readonly List<Vector3> Active = new List<Vector3>();
        public static int Found { get; private set; }

        class Crate { public int id; public Transform tf; }
        readonly List<Crate> _crates = new List<Crate>();
        float _spawnAt = -1f;
        bool _spawned;

        void Update()
        {
            Active.Clear();
            if (!GameUtil.IsServerRunning) return;
            if (!_spawned)
            {
                if (PlayerController.Local == null || RoadNetwork.Instance == null) return;
                if (_spawnAt < 0f) _spawnAt = Time.time + 4f;   // poczekaj aż wczyta się zapis (flagi)
                if (Time.time < _spawnAt) return;
                Spawn();
                _spawned = true;
            }

            for (int i = _crates.Count - 1; i >= 0; i--)
            {
                var c = _crates[i];
                if (c.tf == null) { _crates.RemoveAt(i); continue; }
                c.tf.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);
                c.tf.position += Vector3.up * (Mathf.Sin(Time.time * 2f + c.id) * 0.15f * Time.deltaTime);
                Active.Add(c.tf.position);

                foreach (var p in PlayerController.All)
                {
                    if (p == null || p.IsDead) continue;
                    if ((p.transform.position - c.tf.position).sqrMagnitude > 9f) continue;
                    Collect(p, c);
                    _crates.RemoveAt(i);
                    break;
                }
            }
        }

        void Collect(PlayerController p, Crate c)
        {
            Story.Set("pk_" + c.id);
            Destroy(c.tf.gameObject);
            Found = CountFound();
            p.Stats.AddMoney(Reward);
            if (p.IsOwner)
                HUD.Notify($"Ukryta paczka {Found}/{Total}  +${Reward}", new Color(1f, 0.85f, 0.2f));
            if (Found >= Total && !Story.HasFlagStrict("pk_all"))
            {
                Story.Set("pk_all");
                p.Stats.AddMoney(Bonus);
                p.Stats.AddPackItem(BackpackItems.Vest, 2);
                if (p.IsOwner) HUD.Notify($"Wszystkie paczki! Premia ${Bonus:N0} + 2 kamizelki", new Color(0.4f, 1f, 0.4f));
            }
        }

        static int CountFound()
        {
            int n = 0;
            for (int i = 0; i < Total; i++) if (Story.HasFlagStrict("pk_" + i)) n++;
            return n;
        }

        void Spawn()
        {
            Found = CountFound();
            var rn = RoadNetwork.Instance;
            if (rn.edgeA.Length == 0) return;

            var old = Random.state;
            Random.InitState(7041);   // te same miejsca w każdej grze
            var shader = Shader.Find("Standard");
            int placed = 0, tries = 0;
            while (placed < Total && tries++ < 400)
            {
                int e = Random.Range(0, rn.edgeA.Length);
                Vector3 a = rn.nodes[rn.edgeA[e]], b = rn.nodes[rn.edgeB[e]];
                Vector3 d = b - a; d.y = 0;
                if (d.magnitude < 20f) continue;
                d.Normalize();
                Vector3 pos = Vector3.Lerp(a, b, Random.Range(0.3f, 0.7f)) + RoadNetwork.RightOf(d) * (rn.laneOffset + 5.5f * (Random.value > 0.5f ? 1f : -1f));
                if (!Physics.Raycast(pos + Vector3.up * 40f, Vector3.down, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.point.y > 2f) continue;   // nie na dachu
                int id = placed++;
                if (Story.HasFlagStrict("pk_" + id)) continue;

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Paczka_" + id;
                Destroy(go.GetComponent<Collider>());
                go.transform.position = hit.point + Vector3.up * 0.9f;
                go.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
                go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 90f), 0f);
                var r = go.GetComponent<Renderer>();
                if (shader != null)
                {
                    var m = new Material(shader) { color = new Color(1f, 0.78f, 0.15f) };
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", new Color(0.9f, 0.6f, 0.1f));
                    m.SetFloat("_Metallic", 0.8f);
                    r.sharedMaterial = m;
                }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _crates.Add(new Crate { id = id, tf = go.transform });
            }
            Random.state = old;
        }
    }
}
