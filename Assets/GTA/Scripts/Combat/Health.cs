using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    public enum Faction { Player, Civilian, Police, Gang, Vehicle }

    /// <summary>
    /// Zdrowie zsynchronizowane po sieci. Obrażenia zadaje WYŁĄCZNIE serwer (TakeDamage).
    /// Używane przez graczy, NPC i pojazdy.
    /// </summary>
    public class Health : NetworkBehaviour
    {
        public float maxHealth = 100f;
        public Faction faction = Faction.Civilian;
        [Tooltip("Czy po śmierci obiekt ma zniknąć (NPC). Gracz się odradza, więc u gracza = false.")]
        public bool despawnOnDeath = true;
        public float despawnDelay = 10f;

        public readonly NetworkVariable<float> Current = new NetworkVariable<float>(100f);
        public readonly NetworkVariable<bool> IsDead = new NetworkVariable<bool>(false);
        /// <summary>Pancerz (kamizelka) – przyjmuje obrażenia przed zdrowiem. Kupuje się go w Ammu-Nation.</summary>
        public readonly NetworkVariable<float> Armor = new NetworkVariable<float>(0f);
        public const float MaxArmor = 100f;

        /// <summary>Czas (serwer) ostatnich obrażeń – do regeneracji.</summary>
        public float LastDamageTime { get; private set; } = -999f;

        /// <summary>(obrażenia, clientId atakującego lub GameUtil.NoClient) – tylko serwer.</summary>
        public event Action<float, ulong> OnDamagedServer;
        /// <summary>(clientId zabójcy lub GameUtil.NoClient) – tylko serwer.</summary>
        public event Action<ulong> OnDiedServer;
        /// <summary>Wywoływane na wszystkich maszynach, gdy zmieni się stan śmierci.</summary>
        public event Action<bool> OnDeadChanged;

        /// <summary>Loguj obrażenia graczy i aut (włączane przez -autotest).</summary>
        public static bool DebugLog;

        public bool Dead => IsDead.Value;
        [Tooltip("Serwer: nieśmiertelność (konsola ` → nieśmiertelnosc)")]
        public bool Invulnerable;
        public float Percent => maxHealth <= 0 ? 0 : Current.Value / maxHealth;

        public override void OnNetworkSpawn()
        {
            if (IsServer) Current.Value = maxHealth;
            IsDead.OnValueChanged += HandleDeadChanged;
        }

        public override void OnNetworkDespawn()
        {
            IsDead.OnValueChanged -= HandleDeadChanged;
        }

        void HandleDeadChanged(bool oldV, bool newV) => OnDeadChanged?.Invoke(newV);

        public void TakeDamage(float amount, ulong attackerClientId)
        {
            if (!IsServer || IsDead.Value || amount <= 0f || Invulnerable) return;
            LastDamageTime = Time.time;
            if (Armor.Value > 0f)
            {
                float absorbed = Mathf.Min(Armor.Value, amount * 0.8f); // kamizelka zatrzymuje 80% obrażeń
                Armor.Value -= absorbed;
                amount -= absorbed;
            }
            Current.Value = Mathf.Max(0f, Current.Value - amount);
            if (faction != Faction.Vehicle) BloodRpc(transform.position + Vector3.up * 1.2f);
            if (DebugLog && (faction == Faction.Player || faction == Faction.Vehicle))
                Debug.Log($"[DMG] {name} -{amount:0.0} => {Current.Value:0} (attacker={attackerClientId})");
            OnDamagedServer?.Invoke(amount, attackerClientId);

            if (Current.Value <= 0f)
            {
                IsDead.Value = true;
                OnDiedServer?.Invoke(attackerClientId);
                if (despawnOnDeath) StartCoroutine(DespawnLater());
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        void BloodRpc(Vector3 pos) => Effects.Blood(pos, Vector3.up);

        public void Heal(float amount)
        {
            if (!IsServer || IsDead.Value) return;
            Current.Value = Mathf.Min(maxHealth, Current.Value + amount);
        }

        public void AddArmor(float amount)
        {
            if (!IsServer || IsDead.Value) return;
            Armor.Value = Mathf.Min(MaxArmor, Armor.Value + amount);
        }

        public void ResetHealth()
        {
            if (!IsServer) return;
            Armor.Value = 0f;
            Current.Value = maxHealth;
            IsDead.Value = false;
        }

        IEnumerator DespawnLater()
        {
            yield return new WaitForSeconds(despawnDelay);
            if (NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn(true);
        }
    }
}
