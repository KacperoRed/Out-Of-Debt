using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Radiowóz: (serwer) dojeżdża do poszukiwanego gracza, a blisko celu wysadza policjantów.
    /// (wszyscy) miga kogutem, gdy syrena jest włączona.
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public class PoliceCarAI : MonoBehaviour
    {
        public Light sirenRed;
        public Light sirenBlue;
        public float dismountDistance = 18f;

        Vehicle _vehicle;
        PlayerController _target;
        GameObject _officerPrefab;
        int _officers;
        bool _dispatched, _dismounted;
        PoliceTier _tier;

        public bool IsActive => _dispatched && !_dismounted;

        void Awake() => _vehicle = GetComponent<Vehicle>();

        /// <summary>Serwer: wyślij radiowóz do gracza.</summary>
        public void Dispatch(PlayerController target, GameObject officerPrefab, int officers, PoliceTier tier)
        {
            _tier = tier;
            _target = target;
            _officerPrefab = officerPrefab;
            _officers = officers;
            _dispatched = true;
            _vehicle.SirenOn.Value = true;
            _vehicle.StartChase(target.transform);
        }

        void Update()
        {
            // koguty
            bool siren = _vehicle.IsSpawned && _vehicle.SirenOn.Value && !_vehicle.Health.Dead;
            bool phase = Mathf.Repeat(Time.time * 4f, 1f) > 0.5f;
            if (sirenRed != null) sirenRed.enabled = siren && phase;
            if (sirenBlue != null) sirenBlue.enabled = siren && !phase;

            if (!GameUtil.IsServerRunning || !IsActive || !_vehicle.IsSpawned) return;

            // gracz ukradł radiowóz albo auto zniszczone → koniec akcji
            if (_vehicle.HasPlayerDriver || _vehicle.Health.Dead)
            {
                _dismounted = true;
                _vehicle.SirenOn.Value = false;
                return;
            }

            if (_target == null || _target.IsDead || _target.Wanted.Level == 0)
            {
                _vehicle.StopAI();
                _vehicle.SirenOn.Value = false;
                _dispatched = false;
                return;
            }

            float dist = Vector3.Distance(transform.position, _target.transform.position);
            bool targetSlowOrOnFoot = !_target.InVehicle || (_target.Vehicles.CurrentVehicle != null && _target.Vehicles.CurrentVehicle.SpeedKmh < 15f);
            if (dist < dismountDistance && targetSlowOrOnFoot) Dismount();
        }

        void Dismount()
        {
            _dismounted = true;
            _vehicle.StopAI();
            for (int i = 0; i < _officers && _officerPrefab != null; i++)
            {
                Vector3 side = (i % 2 == 0 ? -transform.right : transform.right) * 2.2f;
                Vector3 pos = transform.position + side + transform.forward * (i / 2) * 1.5f;
                if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 4f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
                var ai = GameUtil.SpawnNetworked(_officerPrefab, pos, transform.rotation).GetComponent<CombatNpcAI>();
                if (ai != null) ai.tier = _tier;
            }
        }
    }
}
