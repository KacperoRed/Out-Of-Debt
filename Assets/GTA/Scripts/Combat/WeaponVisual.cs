using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Pokazuje w ręce model aktualnej broni (WeaponData.viewModel). Działa lokalnie u każdego gracza
    /// na podstawie zsynchronizowanego WeaponController.CurrentWeapon.
    /// </summary>
    public class WeaponVisual : MonoBehaviour
    {
        [Tooltip("Punkt chwytu (prawa ręka)")]
        public Transform hand;
        [Tooltip("Zastępczy model, gdy broń nie ma viewModel (np. kostka)")]
        public GameObject fallback;

        public Transform CurrentMuzzle { get; private set; }

        /// <summary>Czy w ręku jest teraz widoczny model broni (nie pięści, nie w aucie).</summary>
        public bool Showing => _current != null && _current.activeSelf;

        /// <summary>Broń w ręku jest białą bronią (kij) – inna poza niż przy strzelaniu.</summary>
        public bool ShowingMelee => Showing && _weapons != null && _shown >= 0 && _shown < _weapons.weapons.Length && _weapons.weapons[_shown].kind == WeaponKind.Melee;

        WeaponController _weapons;
        PlayerController _player;
        GameObject _current;
        int _shown = -1;

        void Awake()
        {
            _weapons = GetComponent<WeaponController>();
            _player = GetComponent<PlayerController>();
        }

        void LateUpdate()
        {
            if (_weapons == null || !_weapons.IsSpawned) return;
            int idx = Mathf.Clamp(_weapons.CurrentWeapon.Value, 0, _weapons.weapons.Length - 1);
            if (idx != _shown) Show(idx);

            bool visible = (_player == null || !_player.InVehicle) && !_weapons.Holstered.Value;
            if (_current != null && _current.activeSelf != visible) _current.SetActive(visible);
            if (fallback != null)
            {
                bool fb = visible && _current == null && _weapons.weapons[idx].kind != WeaponKind.Melee;
                if (fallback.activeSelf != fb) fallback.SetActive(fb);
            }
        }

        void Show(int idx)
        {
            _shown = idx;
            if (_current != null) Destroy(_current);
            _current = null;
            CurrentMuzzle = null;

            var data = _weapons.weapons[idx];
            if (data.viewModel == null || hand == null) return;
            _current = Instantiate(data.viewModel, hand, false);
            _current.transform.localPosition = Vector3.zero;
            _current.transform.localRotation = Quaternion.identity;
            foreach (var c in _current.GetComponentsInChildren<Collider>()) Destroy(c);
            CurrentMuzzle = _current.transform.Find("Muzzle");
        }
    }
}
