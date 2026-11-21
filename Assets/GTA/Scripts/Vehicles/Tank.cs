using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Czołg (wojsko, 5★): wolny, 2000 HP. Wsiadasz jak do auta, LPM = strzał z działa
    /// (pocisk wybucha w miejscu trafienia celownika). Czołg AI goni gracza i strzela, gdy jest w zasięgu.
    /// Strzela tylko serwer – kierowca wysyła tylko punkt i kierunek strzału, a serwer go sprawdza.
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public class Tank : NetworkBehaviour
    {
        public Transform muzzle;
        [Tooltip("Obracana wieża (z działem). Gdy pusta, czołg strzela tylko prosto.")]
        public Transform turret;
        [Tooltip("Prędkość obrotu wieży (stopnie/s)")]
        public float turretSpeed = 80f;
        [Tooltip("Czas przeładowania działa (s)")]
        public float reload = 3f;
        public float range = 250f;
        public float shellDamage = 450f;
        public float splashRadius = 6f;
        [Tooltip("Zasięg ostrzału czołgu AI")]
        public float aiFireRange = 70f;

        Vehicle _v;
        float _localNextShot, _serverNextShot, _aiNextShot;
        float _yaw;
        Quaternion _baseRel = Quaternion.identity;
        readonly NetworkVariable<float> _netYaw = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        void Awake()
        {
            _v = GetComponent<Vehicle>();
            if (turret != null) _baseRel = Quaternion.Inverse(transform.rotation) * turret.rotation;
        }

        /// <summary>Obrót wieży: właściciel (kierowca albo serwer-AI) celuje, reszta dostaje kąt przez sieć.</summary>
        void LateUpdate()
        {
            if (turret == null || !IsSpawned) return;
            if (IsOwner)
            {
                Vector3? dir = null;
                if (_v.HasPlayerDriver && _v.DriverClientId.Value == NetworkManager.LocalClientId && Camera.main != null)
                    dir = Camera.main.transform.forward;
                else if (!_v.HasPlayerDriver && _v.aiChaseTarget != null)
                    dir = _v.aiChaseTarget.position - transform.position;
                if (dir.HasValue)
                {
                    Vector3 l = transform.InverseTransformDirection(dir.Value);
                    if (l.x * l.x + l.z * l.z > 0.0001f)
                        _yaw = Mathf.MoveTowardsAngle(_yaw, Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg, turretSpeed * Time.deltaTime);
                }
                if (Mathf.Abs(Mathf.DeltaAngle(_netYaw.Value, _yaw)) > 0.5f) _netYaw.Value = _yaw;
            }
            else _yaw = Mathf.MoveTowardsAngle(_yaw, _netYaw.Value, 360f * Time.deltaTime);

            turret.rotation = transform.rotation * Quaternion.AngleAxis(_yaw, Vector3.up) * _baseRel;
        }

        void Update()
        {
            if (!IsSpawned) return;

            // kierowca-gracz: LPM strzela tam, gdzie celuje kamera
            if (IsOwner && _v.HasPlayerDriver && _v.DriverClientId.Value == NetworkManager.LocalClientId
                && GameInput.FirePressed && Time.time >= _localNextShot && muzzle != null)
            {
                _localNextShot = Time.time + reload;
                Vector3 aim = AimPoint();
                FireRpc(muzzle.position, (aim - muzzle.position).normalized);
            }

            // AI (serwer): ostrzał celu, który goni
            if (IsServer && !_v.HasPlayerDriver && !_v.Health.Dead && _v.aiChaseTarget != null && muzzle != null
                && Time.time >= _aiNextShot)
            {
                Vector3 target = _v.aiChaseTarget.position + Vector3.up;
                if (Vector3.Distance(muzzle.position, target) < aiFireRange)
                {
                    _aiNextShot = Time.time + reload + Random.Range(0f, 1.5f);
                    Fire(muzzle.position, (target - muzzle.position).normalized, GameUtil.NoClient);
                }
            }
        }

        /// <summary>Punkt, w który patrzy kamera (pomija sam czołg).</summary>
        Vector3 AimPoint()
        {
            var cam = Camera.main;
            if (cam == null) return muzzle.position + muzzle.forward * range;
            var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 point = cam.transform.position + cam.transform.forward * range;
            foreach (var h in hits)
            {
                if (h.collider.GetComponentInParent<Vehicle>() == _v || h.distance >= best) continue;
                best = h.distance;
                point = h.point;
            }
            return point;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void FireRpc(Vector3 origin, Vector3 dir)
        {
            // anty-cheat: lufa musi być przy czołgu, a kierunek jednostkowy
            if (!_v.HasPlayerDriver || (origin - muzzle.position).sqrMagnitude > 16f) return;
            Fire(muzzle.position, dir.normalized, _v.DriverClientId.Value);
        }

        /// <summary>Serwer: pocisk leci prosto i wybucha w pierwszym trafieniu.</summary>
        void Fire(Vector3 origin, Vector3 dir, ulong attacker)
        {
            if (!IsServer || Time.time < _serverNextShot) return;
            _serverNextShot = Time.time + reload * 0.9f;

            Vector3 point = origin + dir * range;
            float best = float.MaxValue;
            foreach (var h in Physics.RaycastAll(origin, dir, range, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<Vehicle>() == _v || h.distance >= best) continue;
                best = h.distance;
                point = h.point;
            }

            CombatUtil.ExplodeServer(point, splashRadius, shellDamage, attacker, _v.Health);
            ShellRpc(origin, point);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ShellRpc(Vector3 from, Vector3 to)
        {
            Effects.Tracer(from, to, new Color(1f, 0.6f, 0.2f));
            Effects.Explosion(to, splashRadius);
        }
    }
}
