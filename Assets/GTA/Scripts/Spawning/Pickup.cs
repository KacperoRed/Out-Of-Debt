using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Znajdźka: apteczka, amunicja, kasa albo odblokowanie broni.
    /// Serwer sprawdza odległość graczy i przyznaje nagrodę.
    /// </summary>
    public class Pickup : NetworkBehaviour
    {
        public enum PickupType { Health, Ammo, Money, Weapon }

        public PickupType type = PickupType.Health;
        public int amount = 50;
        [Tooltip("Dla typu Weapon: indeks broni w WeaponController")]
        public int weaponIndex = 4;
        public float radius = 1.5f;

        void Update()
        {
            transform.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
            if (!IsServer || !IsSpawned) return;

            foreach (var p in PlayerController.All)
            {
                if (p.IsDead || p.InVehicle) continue;
                if (Vector3.Distance(p.transform.position + Vector3.up, transform.position) > radius + 0.5f) continue;
                if (Apply(p)) { NetworkObject.Despawn(true); return; }
            }
        }

        bool Apply(PlayerController p)
        {
            switch (type)
            {
                case PickupType.Health:
                    if (p.Health.Current.Value >= p.Health.maxHealth) return false;
                    p.Health.Heal(amount);
                    break;
                case PickupType.Ammo:
                    p.Weapons.AddAmmoServer(Mathf.Max(1, amount / 10));
                    break;
                case PickupType.Money:
                    p.Stats.AddMoney(amount);
                    break;
                case PickupType.Weapon:
                    p.Weapons.GrantWeaponServer(weaponIndex);
                    p.Weapons.AddAmmoServer(weaponIndex, 2);
                    break;
            }
            NotifyRpc(type.ToString(), RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
            return true;
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void NotifyRpc(string what, RpcParams rpcParams)
        {
            string label = what switch
            {
                "Health" => "Apteczka",
                "Ammo" => "Amunicja",
                "Money" => "Gotówka",
                _ => "Broń",
            };
            HUD.Notify("Podniesiono: " + label, Color.white);
        }
    }
}
