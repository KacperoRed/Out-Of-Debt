using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Helikopter. Wsiadasz jak do auta (to samo Vehicle), ale lata.
    ///  • Sterowanie kierowcy: W/S – przód/tył, A/D – obrót, Spacja – w górę, Shift – w dół.
    ///  • Policja (AI, serwer): krąży nad graczem i ostrzeliwuje go.
    ///  • Zestrzelony (IsDead): spada. Gdy dotknie ziemi, wraca do życia i staje się zwykłym helikopterem,
    ///    którym można latać (wsiadasz i lecisz).
    /// Fizykę liczy WŁAŚCICIEL obiektu: kierowca-gracz albo serwer (AI, spadanie).
    /// </summary>
    [RequireComponent(typeof(Vehicle), typeof(Rigidbody))]
    public class Helicopter : NetworkBehaviour
    {
        public Transform rotor;
        public float rotorSpeed = 1100f;
        [Tooltip("Prędkość pozioma (m/s)")]
        public float maxSpeed = 24f;
        public float climbSpeed = 9f;
        public float turnSpeed = 110f;
        [Tooltip("Jak szybko helikopter zmienia prędkość (większe = bardziej zwinny)")]
        public float responsiveness = 3f;
        [Tooltip("Wysokość punktu obrotu nad ziemią, gdy helikopter stoi (m)")]
        public float groundClearance = 0.4f;

        [Header("Policja (AI)")]
        public float aiOrbitDistance = 35f;
        public float aiHoverHeight = 22f;
        public float aiShotRange = 90f;
        public float aiShotInterval = 0.5f;
        public float aiDamagePerShot = 4f;
        [Range(0f, 1f)] public float aiAccuracy = 0.4f;

        [Header("Karabin (gracz)")]
        public float gunInterval = 0.12f;
        public float gunDamage = 12f;
        public float gunRange = 160f;

        float _nextGun, _serverNextGun;

        Vehicle _v;
        Rigidbody _body;
        float _heading;
        float _downTime;
        bool _aiOn;
        PlayerController _target;
        float _nextShot;

        public Vehicle Car => _v;
        /// <summary>Czy helikopter jest sterowany przez policję (a nie przez gracza).</summary>
        public bool AIControlled => _aiOn;

        void Awake()
        {
            _v = GetComponent<Vehicle>();
            _body = GetComponent<Rigidbody>();
            _heading = transform.eulerAngles.y;
        }

        // ================= API SERWERA =================

        /// <summary>Serwer: policja wysyła helikopter na gracza.</summary>
        public void Dispatch(PlayerController target)
        {
            if (!IsServer) return;
            _target = target;
            _aiOn = true;
        }

        /// <summary>Serwer: policja odpuszcza – helikopter wyląduje.</summary>
        public void StandDown()
        {
            if (!IsServer) return;
            _aiOn = false;
            _target = null;
        }

        /// <summary>Serwer: wywoływane przez Vehicle, gdy helikopter zostanie zestrzelony.</summary>
        public void OnShotDown(Vector3 pos)
        {
            _aiOn = false;
            _target = null;
            _downTime = 0f;
            ExplodeRpc(pos);
        }

        // ================= PĘTLE =================

        void Update()
        {
            // wirnik kręci się na wszystkich maszynach (wizualnie)
            if (rotor != null) rotor.Rotate(0f, (_v.Health.Dead ? rotorSpeed * 0.3f : rotorSpeed) * Time.deltaTime, 0f, Space.Self);

            if (IsServer && _aiOn && !_v.Health.Dead) AIFire();

            // pilot-gracz: LPM = karabin maszynowy w kierunku celownika
            if (_v.IsOwner && _v.HasPlayerDriver && !_v.Health.Dead && _v.DriverClientId.Value == NetworkManager.LocalClientId
                && GameInput.FireHeld && Time.time >= _nextGun)
            {
                _nextGun = Time.time + gunInterval;
                Vector3 origin = GunOrigin();
                var cam = Camera.main;
                Vector3 aim = cam != null ? cam.transform.position + cam.transform.forward * gunRange : origin + transform.forward * gunRange;
                if (cam != null)
                {
                    float best = gunRange;
                    foreach (var h in Physics.RaycastAll(cam.transform.position, cam.transform.forward, gunRange, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (h.collider.GetComponentInParent<Vehicle>() == _v || h.distance >= best) continue;
                        best = h.distance; aim = h.point;
                    }
                }
                PlayerFireRpc(origin, (aim - origin).normalized);
            }
        }

        Vector3 GunOrigin() => transform.position + transform.forward * 2.5f - transform.up * 0.6f;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void PlayerFireRpc(Vector3 origin, Vector3 dir)
        {
            if (!_v.HasPlayerDriver || _v.Health.Dead || Time.time < _serverNextGun) return;
            if ((origin - GunOrigin()).sqrMagnitude > 25f) return; // anty-cheat
            _serverNextGun = Time.time + gunInterval * 0.8f;
            dir = dir.normalized;
            Vector3 end = origin + dir * gunRange;
            float best = gunRange;
            Health target = null;
            foreach (var h in Physics.RaycastAll(origin, dir, gunRange, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<Vehicle>() == _v || h.distance >= best) continue;
                best = h.distance; end = h.point;
                target = h.collider.GetComponentInParent<Health>();
            }
            if (target != null && !target.Dead) target.TakeDamage(gunDamage, _v.DriverClientId.Value);
            ShotRpc(origin, end);
        }

        void FixedUpdate()
        {
            if (!IsSpawned || !_v.IsOwner) return;
            if (_v.Health.Dead) { FallStep(); return; }
            FlyStep();
        }

        // ================= LOT =================

        void FlyStep()
        {
            float dt = Time.fixedDeltaTime;
            _body.useGravity = false;
            _body.angularDamping = 4f;

            float fwd = 0f, yaw = 0f, up = 0f;
            bool hasPilot = _v.HasPlayerDriver;

            if (hasPilot && _v.DriverClientId.Value == NetworkManager.LocalClientId)
            {
                Vector2 m = GameInput.Move;
                fwd = m.y;
                yaw = m.x;
                if (!GameInput.Blocked)
                {
                    if (Input.GetKey(KeyCode.Space)) up = 1f;
                    if (Input.GetKey(KeyCode.LeftShift)) up = -1f;
                }
            }
            else if (_aiOn && _target != null && !_target.IsDead)
            {
                AIInput(out fwd, out yaw, out up);
            }
            else
            {
                // bez pilota: powoli ląduje i stoi
                if (Altitude() <= groundClearance + 0.1f) up = 0f;
                else up = -0.7f;
                if (Altitude() <= groundClearance + 0.1f) _body.linearVelocity = Vector3.zero;
            }

            _heading += yaw * turnSpeed * dt;
            Quaternion lean = Quaternion.Euler(-fwd * 10f, _heading, -yaw * 14f);
            _body.MoveRotation(Quaternion.Slerp(_body.rotation, lean, 6f * dt));

            Vector3 fwdDir = Quaternion.Euler(0f, _heading, 0f) * Vector3.forward;
            Vector3 desired = fwdDir * (fwd * maxSpeed) + Vector3.up * (up * climbSpeed);

            // nie wpadaj w ziemię
            if (Altitude() < groundClearance && desired.y < 0f) desired.y = 0f;

            float k = 1f - Mathf.Exp(-responsiveness * dt);
            _body.linearVelocity = Vector3.Lerp(_body.linearVelocity, desired, k);
        }

        void AIInput(out float fwd, out float yaw, out float up)
        {
            Vector3 tp = _target.transform.position;
            Vector3 me = transform.position;

            // orbita: trzymaj się w odległości od gracza, na wysokości
            Vector3 away = me - tp; away.y = 0f;
            if (away.sqrMagnitude < 1f) away = Vector3.back;
            Vector3 goal = tp + away.normalized * aiOrbitDistance;
            goal.y = tp.y + aiHoverHeight;

            // obrót w stronę gracza
            Vector3 fwdFlat = Quaternion.Euler(0f, _heading, 0f) * Vector3.forward;
            Vector3 look = tp - me; look.y = 0f;
            float angle = look.sqrMagnitude > 0.01f ? Vector3.SignedAngle(fwdFlat, look, Vector3.up) : 0f;
            yaw = Mathf.Clamp(angle / 25f, -1f, 1f);

            // lot ku punktowi orbity, ale tylko gdy patrzy w jego stronę
            Vector3 toGoal = goal - me; toGoal.y = 0f;
            fwd = Mathf.Clamp(toGoal.magnitude / 12f, -0.5f, 1f) * Mathf.Clamp01(1f - Mathf.Abs(angle) / 60f);

            up = Mathf.Clamp((goal.y - me.y) * 0.6f, -1f, 1f);
        }

        /// <summary>Spadanie po zestrzeleniu. Gdy dotknie ziemi (lub po 25 s), ożywa.</summary>
        void FallStep()
        {
            if (!IsServer) return; // spadanie liczy serwer (po zestrzeleniu przejmuje własność)
            _body.useGravity = true;
            _body.angularDamping = 0.8f;
            if (Altitude() > 2f) _body.angularVelocity = Vector3.up * 4f; // wiruje w powietrzu, przy ziemi już nie (nie podskakuje)
            _downTime += Time.fixedDeltaTime;

            // ląduje na ziemi albo na czymś (dach, samochód) – liczy się zatrzymanie, nie wysokość nad ziemią
            bool onGround = _downTime > 0.5f && Altitude() < groundClearance + 0.15f && Mathf.Abs(_body.linearVelocity.y) < 1.5f;
            bool resting = _downTime > 3f && Mathf.Abs(_body.linearVelocity.y) < 1.5f;
            if (onGround || resting || _downTime > 25f) Land();
        }

        /// <summary>Serwer: helikopter wylądował – wraca do życia i staje się zwykłym, sprawnym pojazdem.</summary>
        void Land()
        {
            _body.useGravity = false;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _heading = transform.eulerAngles.y;
            _body.rotation = Quaternion.Euler(0f, _heading, 0f);
            _downTime = 0f;
            _aiOn = false;
            _target = null;
            _v.Health.ResetHealth();
        }

        /// <summary>Wysokość punktu obrotu nad ziemią (pomija własne collidery).</summary>
        float Altitude()
        {
            float best = 80f;
            foreach (var h in Physics.RaycastAll(transform.position, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<Vehicle>() == _v || h.distance >= best) continue;
                best = h.distance;
            }
            return best;
        }

        // ================= OGIEŃ POLICJI =================

        void AIFire()
        {
            if (_target == null || _target.IsDead || Time.time < _nextShot) return;
            Vector3 from = transform.position - Vector3.up * 0.5f;
            Vector3 to = _target.transform.position + Vector3.up;
            if (Vector3.Distance(from, to) > aiShotRange) return;

            _nextShot = Time.time + aiShotInterval;
            if (Random.value < aiAccuracy) _target.Health.TakeDamage(aiDamagePerShot, GameUtil.NoClient);
            ShotRpc(from, to);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ShotRpc(Vector3 from, Vector3 to) => Effects.Tracer(from, to, new Color(1f, 0.4f, 0.2f));

        [Rpc(SendTo.ClientsAndHost)]
        void ExplodeRpc(Vector3 pos) => Effects.Explosion(pos, 3f);
    }
}
