using System;
using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    public enum WeaponKind
    {
        Hitscan,    // zwykła broń palna (raycast)
        Melee,      // pięści / broń biała
        Throwable,  // granat (rzut, wybuch po czasie)
        Rocket,     // RPG (pocisk wybucha przy uderzeniu)
    }

    [Serializable]
    public class WeaponData
    {
        public string name = "Pistolet";
        public WeaponKind kind = WeaponKind.Hitscan;
        public float damage = 25f;
        public float fireRate = 4f;        // strzały na sekundę
        public float range = 80f;
        public float spread = 1.5f;        // stopnie
        public int pellets = 1;            // >1 = shotgun
        public bool automatic;
        public int magazineSize = 12;
        public int startAmmo = 60;         // zapas poza magazynkiem; -1 = nieskończona (pięści)
        public float reloadTime = 1.2f;

        [Header("Granat / rakieta")]
        public float explosionRadius = 6f;
        public float projectileSpeed = 18f;

        [Header("Celowanie")]
        [Tooltip("Pole widzenia kamery przy celowaniu (0 = bez zoomu). Snajperka ~20.")]
        public float aimFov = 0f;

        [Header("Sklep")]
        public bool startsOwned;
        public int price = 1000;
        [Tooltip("Cena jednego magazynka (dla granatów: jednej sztuki)")]
        public int ammoPrice = 100;

        [Header("Wygląd")]
        [Tooltip("Model broni w ręce (zwykły prefab, bez NetworkObject). Dziecko 'Muzzle' = wylot lufy.")]
        public GameObject viewModel;
    }

    /// <summary>
    /// Bronie gracza: przełączanie 1–9 / kółkiem, strzał LPM, przeładowanie R.
    /// Klient tylko PROSI o strzał – serwer robi raycast / rzuca granat i zadaje obrażenia.
    /// Posiadane bronie (OwnedMask) trzyma serwer – kupuje się je w sklepie (ShopClient).
    /// </summary>
    public class WeaponController : NetworkBehaviour
    {
        public WeaponData[] weapons = DefaultWeapons();

        [Tooltip("Skąd lecą pociski, jeśli model broni nie ma 'Muzzle'. Puste = wysokość klatki piersiowej.")]
        public Transform muzzle;

        [Header("Pociski (prefaby sieciowe z komponentem Projectile)")]
        public GameObject grenadePrefab;
        public GameObject rocketPrefab;

        public readonly NetworkVariable<int> CurrentWeapon = new NetworkVariable<int>(1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        /// <summary>Gracz schował broń (klawisz 0): puste ręce, nie można atakować.</summary>
        public readonly NetworkVariable<bool> Holstered = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        /// <summary>Bit i = gracz ma broń i. Zapisuje tylko serwer.</summary>
        public readonly NetworkVariable<int> OwnedMask = new NetworkVariable<int>(0);

        // amunicja trzymana lokalnie u właściciela (prosto; dla pełnego anty-cheata przenieś na serwer)
        int[] _inMag, _reserve;
        float _nextFireTime, _reloadEnd;
        PlayerController _player;
        WeaponVisual _visual;

        public WeaponData Current => weapons[Mathf.Clamp(CurrentWeapon.Value, 0, weapons.Length - 1)];
        public int AmmoInMag => _inMag != null ? _inMag[CurrentWeapon.Value] : 0;
        public int AmmoReserve => _reserve != null ? _reserve[CurrentWeapon.Value] : 0;
        public bool IsReloading => Time.time < _reloadEnd;
        public bool Owns(int i) => i >= 0 && i < 31 && (OwnedMask.Value & (1 << i)) != 0;

        public static WeaponData[] DefaultWeapons() => new[]
        {
            new WeaponData { name = "Pięści", kind = WeaponKind.Melee, damage = 15, fireRate = 2, range = 1.8f, spread = 0, magazineSize = 0, startAmmo = -1, startsOwned = true, price = 0 },
            new WeaponData { name = "Pistolet", damage = 25, fireRate = 4, range = 70, spread = 1.5f, magazineSize = 12, startAmmo = 72, startsOwned = true, price = 300, ammoPrice = 40 },
            new WeaponData { name = "Uzi", damage = 14, fireRate = 12, range = 60, spread = 3f, automatic = true, magazineSize = 30, startAmmo = 120, price = 800, ammoPrice = 80 },
            new WeaponData { name = "Shotgun", damage = 12, fireRate = 1.2f, range = 30, spread = 7f, pellets = 8, magazineSize = 6, startAmmo = 30, reloadTime = 2f, price = 1200, ammoPrice = 100 },
            new WeaponData { name = "AK-74", damage = 30, fireRate = 8, range = 120, spread = 1f, automatic = true, magazineSize = 30, startAmmo = 120, reloadTime = 1.6f, price = 2500, ammoPrice = 150 },
            new WeaponData { name = "M249", damage = 26, fireRate = 13, range = 110, spread = 2.5f, automatic = true, magazineSize = 100, startAmmo = 200, reloadTime = 3f, price = 4000, ammoPrice = 300 },
            new WeaponData { name = "Snajperka", damage = 150, fireRate = 0.7f, range = 300, spread = 0f, magazineSize = 5, startAmmo = 20, reloadTime = 2.2f, aimFov = 18f, price = 5000, ammoPrice = 200 },
            new WeaponData { name = "Granat", kind = WeaponKind.Throwable, damage = 140, fireRate = 1f, range = 30, spread = 0f, magazineSize = 1, startAmmo = 4, reloadTime = 0.5f, explosionRadius = 7f, projectileSpeed = 16f, price = 600, ammoPrice = 150 },
            new WeaponData { name = "RPG", kind = WeaponKind.Rocket, damage = 250, fireRate = 0.5f, range = 200, spread = 0f, magazineSize = 1, startAmmo = 4, reloadTime = 2.5f, explosionRadius = 8f, projectileSpeed = 45f, price = 8000, ammoPrice = 500 },
        };

        void Awake()
        {
            _player = GetComponent<PlayerController>();
            _visual = GetComponent<WeaponVisual>();
            InitAmmo();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                int mask = 0;
                for (int i = 0; i < weapons.Length && i < 31; i++) if (weapons[i].startsOwned) mask |= 1 << i;
                OwnedMask.Value = mask;
            }
            OwnedMask.OnValueChanged += OnOwnedChanged;
        }

        public override void OnNetworkDespawn() => OwnedMask.OnValueChanged -= OnOwnedChanged;

        void OnOwnedChanged(int oldMask, int newMask)
        {
            if (!IsOwner) return;
            // automatycznie wyjmij nowo zdobytą broń
            int gained = newMask & ~oldMask;
            for (int i = 0; i < weapons.Length; i++)
                if ((gained & (1 << i)) != 0) { CurrentWeapon.Value = i; break; }
        }

        void InitAmmo()
        {
            _inMag = new int[weapons.Length];
            _reserve = new int[weapons.Length];
            for (int i = 0; i < weapons.Length; i++)
            {
                _inMag[i] = weapons[i].magazineSize;
                _reserve[i] = Mathf.Max(0, weapons[i].startAmmo);
            }
        }

        void Update()
        {
            if (!IsOwner || _player.IsDead || _player.InVehicle) return;

            if (!Owns(CurrentWeapon.Value)) CurrentWeapon.Value = 0;

            // klawisz 0 – schowaj / wyjmij broń (brak broni w rękach)
            if (GameInput.HolsterPressed) Holstered.Value = !Holstered.Value;

            int slot = GameInput.WeaponSlotPressed();
            if (slot >= 0 && slot < weapons.Length && Owns(slot)) { CurrentWeapon.Value = slot; Holstered.Value = false; }
            float scroll = GameInput.WeaponScroll;
            if (Mathf.Abs(scroll) > 0.01f && !GameInput.Aim) { CycleWeapon(scroll > 0 ? 1 : -1); Holstered.Value = false; }
            if (Holstered.Value) return;

            var w = Current;
            if (GameInput.Reload) StartReload();

            bool wantFire = w.automatic ? GameInput.FireHeld : GameInput.FirePressed;
            if (!wantFire) return;
            TryFire();
        }

        /// <summary>Strzał bieżącą bronią (właściciel). Publiczne – używa też test automatyczny.</summary>
        public void TryFire()
        {
            if (!IsOwner || Time.time < _nextFireTime || IsReloading) return;
            int idx = CurrentWeapon.Value;
            var w = weapons[idx];
            bool melee = w.kind == WeaponKind.Melee;
            if (!melee && _inMag[idx] <= 0)
            {
                StartReload();
                return;
            }

            _nextFireTime = Time.time + 1f / w.fireRate;
            if (!melee) _inMag[idx]--;

            if (melee)
            {
                // kij: zamach (animacja u wszystkich), obrażenia w momencie uderzenia
                Swing();
                SwingServerRpc();
                StartCoroutine(MeleeStrike(idx));
                return;
            }
            Vector3 origin = GetMuzzlePos();
            FireRpc(origin, (GetAimPoint(origin, w.range) - origin).normalized, idx);

            // granat / rakieta: od razu "przeładuj" kolejną sztukę
            if ((w.kind == WeaponKind.Throwable || w.kind == WeaponKind.Rocket) && _inMag[idx] <= 0) StartReload();
        }

        System.Collections.IEnumerator MeleeStrike(int idx)
        {
            yield return new WaitForSeconds(0.2f);
            if (_player.IsDead || _player.InVehicle || !IsSpawned) yield break;
            Vector3 origin = GetMuzzlePos();
            FireRpc(origin, (GetAimPoint(origin, weapons[idx].range) - origin).normalized, idx);
        }

        void Swing()
        {
            var pose = GetComponent<ArmedPose>();
            if (pose != null) pose.Swing();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SwingServerRpc() => SwingRpc();

        [Rpc(SendTo.NotOwner)]
        void SwingRpc() => Swing();

        /// <summary>Wybór broni z koła (Tab). Tylko właściciel.</summary>
        public void SelectWeapon(int index)
        {
            if (!IsOwner || !Owns(index)) return;
            CurrentWeapon.Value = index;
            Holstered.Value = false;
        }

        /// <summary>Amunicja w magazynku i zapas wybranej broni (do koła broni).</summary>
        public void GetAmmo(int index, out int mag, out int reserve)
        {
            mag = _inMag != null && index >= 0 && index < _inMag.Length ? _inMag[index] : 0;
            reserve = _reserve != null && index >= 0 && index < _reserve.Length ? _reserve[index] : 0;
        }

        /// <summary>Punkt, w który celuje gracz (środek ekranu).</summary>
        Vector3 GetAimPoint(Vector3 origin, float range)
        {
            var cam = Camera.main;
            if (cam == null) return origin + transform.forward * range;
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            float best = range + 10f;
            Vector3 aimPoint = ray.origin + ray.direction * best;
            foreach (var h in Physics.RaycastAll(ray, best, ~0, QueryTriggerInteraction.Collide))
            {
                if (h.transform.IsChildOf(transform) || (h.collider.isTrigger && h.collider.GetComponentInParent<Health>() == null)) continue;
                if (h.distance < best) { best = h.distance; aimPoint = h.point; }
            }
            return aimPoint;
        }

        void CycleWeapon(int dir)
        {
            int i = CurrentWeapon.Value;
            for (int n = 0; n < weapons.Length; n++)
            {
                i = (i + dir + weapons.Length) % weapons.Length;
                if (Owns(i)) { CurrentWeapon.Value = i; return; }
            }
        }

        void StartReload()
        {
            int idx = CurrentWeapon.Value;
            var w = weapons[idx];
            if (w.kind == WeaponKind.Melee || IsReloading || _inMag[idx] >= w.magazineSize || _reserve[idx] <= 0) return;
            _reloadEnd = Time.time + w.reloadTime;
            GameAudio.Reload(transform.position, w.reloadTime);
            int take = Mathf.Min(w.magazineSize - _inMag[idx], _reserve[idx]);
            _inMag[idx] += take;
            _reserve[idx] -= take;
        }

        Vector3 GetMuzzlePos()
        {
            if (_visual != null && _visual.CurrentMuzzle != null) return _visual.CurrentMuzzle.position;
            return muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.4f + transform.forward * 0.5f;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void FireRpc(Vector3 origin, Vector3 direction, int weaponIndex)
        {
            if (_player.IsDead || _player.InVehicle) return;
            if (weaponIndex < 0 || weaponIndex >= weapons.Length || !Owns(weaponIndex)) return; // anty-cheat
            if (Vector3.Distance(origin, transform.position) > 4f) origin = transform.position + Vector3.up * 1.4f;

            var w = weapons[weaponIndex];
            switch (w.kind)
            {
                case WeaponKind.Throwable:
                    LaunchProjectile(grenadePrefab, origin, (direction + Vector3.up * 0.35f).normalized, w);
                    return;
                case WeaponKind.Rocket:
                    LaunchProjectile(rocketPrefab, origin, direction, w);
                    CombatEvents.RaiseGunshot(origin, OwnerClientId);
                    return;
            }

            for (int p = 0; p < Mathf.Max(1, w.pellets); p++)
            {
                Vector3 dir = Quaternion.Euler(UnityEngine.Random.Range(-w.spread, w.spread), UnityEngine.Random.Range(-w.spread, w.spread), 0f) * direction;
                Health hit = ShootRay(origin, dir, w.range, transform, out var end);
                if (hit != null) hit.TakeDamage(w.damage, OwnerClientId);
                if (w.kind == WeaponKind.Hitscan) TracerRpc(origin, end);
            }

            // hałas: przechodnie uciekają, policja w pobliżu reaguje
            if (w.kind == WeaponKind.Hitscan) CombatEvents.RaiseGunshot(origin, OwnerClientId);
        }

        void LaunchProjectile(GameObject prefab, Vector3 origin, Vector3 dir, WeaponData w)
        {
            if (prefab == null) { Debug.LogWarning($"[MiniGTA] Brak prefabu pocisku dla broni {w.name}"); return; }
            var no = GameUtil.SpawnNetworked(prefab, origin + dir * 0.6f, Quaternion.LookRotation(dir));
            var proj = no.GetComponent<Projectile>();
            proj.Launch(OwnerClientId, dir * w.projectileSpeed, w.damage, w.explosionRadius, transform);
        }

        /// <summary>Wspólny raycast strzału (używany też przez NPC). Zwraca trafione Health albo null.</summary>
        public static Health ShootRay(Vector3 origin, Vector3 dir, float range, Transform ignore, out Vector3 endPoint)
        {
            endPoint = origin + dir * range;
            var hits = Physics.RaycastAll(origin, dir, range, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (ignore != null && h.transform.IsChildOf(ignore)) continue;
                var health = h.collider.GetComponentInParent<Health>();
                if (h.collider.isTrigger && health == null) continue;
                endPoint = h.point;
                return health;
            }
            return null;
        }

        [Rpc(SendTo.ClientsAndHost)]
        void TracerRpc(Vector3 from, Vector3 to) => Effects.Tracer(from, to, new Color(1f, 0.9f, 0.5f));

        // ---------- API serwera (sklep, znajdźki, respawn) ----------

        public void RefillAmmoServer() => RefillAmmoRpc();

        /// <summary>Dodaj magazynki do wszystkich broni.</summary>
        public void AddAmmoServer(int magazines) => AddAmmoRpc(-1, magazines);

        /// <summary>Dodaj magazynki (granaty: sztuki) do jednej broni.</summary>
        public void AddAmmoServer(int weaponIndex, int magazines) => AddAmmoRpc(weaponIndex, magazines);

        public void GrantWeaponServer(int weaponIndex)
        {
            if (!IsServer || weaponIndex < 0 || weaponIndex >= weapons.Length) return;
            OwnedMask.Value |= 1 << weaponIndex;
        }

        public void GrantAllWeaponsServer()
        {
            if (!IsServer) return;
            OwnedMask.Value = (1 << weapons.Length) - 1;
            RefillAmmoRpc();
        }

        [Rpc(SendTo.Owner)]
        void RefillAmmoRpc() => InitAmmo();

        [Rpc(SendTo.Owner)]
        void AddAmmoRpc(int weaponIndex, int magazines)
        {
            for (int i = 0; i < weapons.Length; i++)
            {
                if (weaponIndex >= 0 && i != weaponIndex) continue;
                if (weapons[i].kind == WeaponKind.Melee) continue;
                _reserve[i] += Mathf.Max(1, weapons[i].magazineSize) * magazines;
            }
        }
    }

    /// <summary>Zdarzenia walki (serwer) – AI na nie reaguje.</summary>
    public static class CombatEvents
    {
        public static event Action<Vector3, ulong> Gunshot;
        public static void RaiseGunshot(Vector3 pos, ulong shooter) => Gunshot?.Invoke(pos, shooter);
    }
}
