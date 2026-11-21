using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Poziom poszukiwania (gwiazdki 0–5). Liczony na serwerze.
    /// Gwiazdki rosną za przestępstwa (ReportCrime) i spadają, gdy policja nie widzi gracza przez dłuższy czas.
    /// Ile policji się pojawi przy danej liczbie gwiazdek – patrz PoliceSpawner.
    /// </summary>
    public class WantedSystem : NetworkBehaviour
    {
        public const int MaxStars = 5;

        [Tooltip("Ile sekund bez bycia widzianym przez policję, żeby stracić gwiazdkę (bazowo)")]
        public float baseCooldown = 5f;
        [Tooltip("Dodatkowe sekundy za każdą gwiazdkę")]
        public float cooldownPerStar = 2f;

        public readonly NetworkVariable<int> Stars = new NetworkVariable<int>(0);
        /// <summary>Czy policja aktualnie widzi gracza (HUD: gwiazdki świecą, inaczej migają).</summary>
        public readonly NetworkVariable<bool> SeenByPolice = new NetworkVariable<bool>(false);

        float _lastSeenTime;

        public int Level => Stars.Value;

        public override void OnNetworkSpawn()
        {
            Stars.OnValueChanged += OnStarsChanged;
        }

        public override void OnNetworkDespawn()
        {
            Stars.OnValueChanged -= OnStarsChanged;
        }

        void OnStarsChanged(int oldV, int newV)
        {
            if (!IsOwner) return;
            if (newV > oldV)
            {
                string call = newV >= 5 && oldV < 5 ? "\nWezwano WOJSKO!" : newV >= 3 && oldV < 3 ? "\nWezwano SWAT!" : "";
                HUD.Notify("Poziom poszukiwania: " + new string('★', newV) + call, new Color(1f, 0.85f, 0.2f));
            }
            else if (newV == 0 && oldV > 0) HUD.Notify("Zgubiłeś policję!", Color.green);
        }

        void Update()
        {
            if (!IsServer || Stars.Value == 0) return;

            float since = Time.time - _lastSeenTime;
            SeenByPolice.Value = since < 2f;

            // ukryty (we wnętrzu budynku) znika dużo szybciej
            float cooldown = baseCooldown + cooldownPerStar * Stars.Value;
            var me = GetComponent<PlayerController>();
            if (me != null && ShopZone.Inside(me.transform.position)) cooldown *= 0.4f;
            if (since > cooldown)
            {
                Stars.Value--;
                // kolejna gwiazdka znika szybciej (połowa czasu), więc z dużej liczby gwiazdek da się wyjść
                _lastSeenTime = Time.time - cooldown * 0.5f;
            }
        }

        // ---------- API serwera ----------

        public void AddStars(int amount)
        {
            if (!IsServer || amount <= 0) return;
            Stars.Value = Mathf.Clamp(Stars.Value + amount, 0, MaxStars);
            _lastSeenTime = Time.time;
        }

        /// <summary>Ustawia co najmniej tyle gwiazdek (np. zabicie policjanta = min. 2).</summary>
        public void SetAtLeast(int stars)
        {
            if (!IsServer) return;
            if (Stars.Value < stars) Stars.Value = Mathf.Clamp(stars, 0, MaxStars);
            _lastSeenTime = Time.time;
        }

        public void ReportSeen()
        {
            if (IsServer) _lastSeenTime = Time.time;
        }

        public void ClearWanted()
        {
            if (!IsServer) return;
            Stars.Value = 0;
            SeenByPolice.Value = false;
        }

        /// <summary>
        /// Zgłoś przestępstwo gracza. Wywołuj na serwerze, np. WantedSystem.ReportCrime(attackerId, 1, 0).
        /// </summary>
        public static void ReportCrime(ulong clientId, int addStars, int minimumStars = 0)
        {
            if (clientId == GameUtil.NoClient) return;
            var p = GameUtil.GetPlayer(clientId);
            if (p == null || p.Wanted == null || p.IsDead) return;
            if (minimumStars > 0) p.Wanted.SetAtLeast(minimumStars);
            p.Wanted.AddStars(addStars);
        }
    }
}
