using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace MiniGTA
{
    /// <summary>
    /// Arkadowy samochód z "raycastowym" zawieszeniem (bez WheelColliderów – stabilny i prosty).
    /// Fizykę liczy WŁAŚCICIEL obiektu: kierowca-gracz albo serwer (ruch uliczny / policja).
    /// Gdy gracz wsiada, serwer przekazuje mu własność auta (ChangeOwnership).
    ///
    /// AI (serwer): jazda po RoadNetwork prawym pasem, czujnik przeszkód (hamuje przed autami, ludźmi),
    /// ustępowanie na skrzyżowaniach, wyprzedzanie stojących aut, pościg policyjny po NavMeshu.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(Health))]
    public class Vehicle : NetworkBehaviour
    {
        public enum AIMode { None, FollowPath, Chase, Roam }

        /// <summary>Statystyka do testów: zderzenia aut AI między sobą / z pieszymi.</summary>
        public static int AiCrashes, AiPedHits;
        public static readonly System.Collections.Generic.List<Vehicle> All = new System.Collections.Generic.List<Vehicle>();

        [Header("Typ")]
        public string displayName = "Samochód";
        public bool isPoliceCar;
        [Tooltip("Gdzie stoi kierowca (pozycja gracza w czasie jazdy)")]
        public Transform seat;
        [Tooltip("Gdzie gracz pojawia się po wyjściu")]
        public Transform exitPoint;

        [Header("Silnik")]
        public float enginePower = 18f;       // przyspieszenie (m/s^2)
        public float maxSpeed = 35f;          // m/s (~126 km/h)
        public float reverseMaxSpeed = 10f;
        public float brakePower = 30f;
        public float maxSteerAngle = 32f;
        public float rollingDrag = 0.4f;

        [Header("Zawieszenie (4 koła w kolejności: LP, PP, LT, PT)")]
        public Transform[] wheelPoints = new Transform[4];
        public Transform[] wheelVisuals = new Transform[4];
        public float suspensionLength = 0.35f;
        public float springStrength = 35000f;
        public float springDamper = 3500f;
        public float wheelRadius = 0.35f;
        [Range(0f, 1f)] public float frontGrip = 0.9f;
        [Range(0f, 1f)] public float rearGrip = 0.85f;
        [Range(0f, 1f)] public float handbrakeRearGrip = 0.2f;
        [Tooltip("Środek masy (lokalnie). Nisko = auto się nie przewraca.")]
        public Vector3 centerOfMass = new Vector3(0f, 0.2f, 0f);

        [Header("Uszkodzenia")]
        public float collisionDamageMultiplier = 1.5f;
        public float minImpactSpeed = 7f;
        public float explosionRadius = 7f;
        public float explosionDamage = 120f;

        [Header("Skoki kaskaderskie")]
        [Tooltip("Minimalny czas w powietrzu (s), żeby dostać bonus")]
        public float stuntMinAirTime = 1.2f;

        // ---- stan sieciowy ----
        /// <summary>clientId kierowcy-gracza lub NoClient.</summary>
        public readonly NetworkVariable<ulong> DriverClientId = new NetworkVariable<ulong>(GameUtil.NoClient);
        public readonly NetworkVariable<bool> IsAIDriven = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<bool> SirenOn = new NetworkVariable<bool>(false);

        // ---- tuning (warsztat): poziomy 0..3, zmienia serwer ----
        public const int TuneMax = 3;
        public static readonly int[] EnginePrices = { 1500, 2500, 4000 };
        public static readonly int[] HpPrices = { 1000, 1800, 3000 };
        public readonly NetworkVariable<int> EngineLevel = new NetworkVariable<int>(0);
        public readonly NetworkVariable<int> HpLevel = new NetworkVariable<int>(0);
        float _baseEngine, _baseSpeed, _baseHp;
        bool _tuneBase;

        void CaptureTuneBase()
        {
            if (_tuneBase) return;
            _tuneBase = true;
            _baseEngine = enginePower; _baseSpeed = maxSpeed; _baseHp = Health.maxHealth;
        }

        /// <summary>Nakłada poziomy tuningu na silnik (moc +25%, prędkość +12% za poziom) i wytrzymałość (+40% za poziom).</summary>
        void ApplyTuning()
        {
            CaptureTuneBase();
            enginePower = _baseEngine * (1f + 0.25f * EngineLevel.Value);
            maxSpeed = _baseSpeed * (1f + 0.12f * EngineLevel.Value);
            Health.maxHealth = _baseHp * (1f + 0.4f * HpLevel.Value);
        }

        /// <summary>Serwer: ulepsz silnik (kind 0) albo wytrzymałość (kind 1). false = maksymalny poziom.</summary>
        public bool Upgrade(int kind)
        {
            if (!IsServer) return false;
            CaptureTuneBase();
            if (kind == 0) { if (EngineLevel.Value >= TuneMax) return false; EngineLevel.Value++; }
            else { if (HpLevel.Value >= TuneMax) return false; HpLevel.Value++; }
            ApplyTuning();
            if (kind == 1) Health.Heal(10000f);   // większy zbiornik HP – od razu pełny
            return true;
        }

        // ---- wejście (ustawiane przez kierowcę lub AI) ----
        [HideInInspector] public float throttleInput;
        [HideInInspector] public float steerInput;
        [HideInInspector] public bool handbrakeInput;

        // ---- AI (tylko serwer) ----
        public AIMode aiMode = AIMode.None;
        [HideInInspector] public WaypointPath aiPath;
        [HideInInspector] public int aiWaypointIndex;
        [HideInInspector] public Transform aiChaseTarget;
        public float aiCruiseSpeed = 12f;

        public Rigidbody Body { get; private set; }
        public Health Health { get; private set; }
        public bool HasPlayerDriver => DriverClientId.Value != GameUtil.NoClient;
        public float SpeedKmh => Body != null ? Body.linearVelocity.magnitude * 3.6f : 0f;
        public float ForwardSpeed => Vector3.Dot(Body.linearVelocity, transform.forward);
        /// <summary>Prędkość liczona z ruchu transformu – działa na każdej maszynie (też gdy Rigidbody jest kinematyczny).</summary>
        public Vector3 ApproxVelocity { get; private set; }
        /// <summary>Połowa szerokości i długości auta (z colliderów).</summary>
        public Vector2 HalfSize { get; private set; } = new Vector2(0.95f, 2.2f);
        public Light Headlight { get; private set; }
        /// <summary>Helikopter (fizykę i sterowanie liczy wtedy Helicopter, nie koła).</summary>
        public Helicopter Heli { get; private set; }

        bool _grounded;
        float _throttleS, _steerS;   // wygładzone wejście (gaz / kierownica)
        float _stuckTimer, _reverseTimer, _pathTimer, _blockedTimer, _overtakeTimer, _airTime;
        NavMeshPath _navPath;
        int _navCorner;
        Renderer[] _renderers;
        Quaternion[] _wheelBaseRot;
        float _wheelSpin;
        Vector3 _lastPos;
        Collider[] _ownColliders;

        // roam (sieć dróg)
        int _roamFrom = -1, _roamTo = -1, _roamNext = -1;
        /// <summary>Serwer: auto AI stoi na czerwonym świetle (statystyki / debug).</summary>
        public bool WaitingAtRed { get; private set; }
        Vehicle _aheadVehicle;
        bool IsDestroyedWreck { get { var h = GetComponent<Health>(); return h != null && h.Dead; } }
        bool _crossing;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Health = GetComponent<Health>();
            Heli = GetComponent<Helicopter>();
            _renderers = GetComponentsInChildren<Renderer>();
            _ownColliders = GetComponentsInChildren<Collider>(true);
            Body.centerOfMass = centerOfMass - new Vector3(0f, 0.15f, 0f);   // niżej = mniej kołysania na zakrętach

            // zderzenia: nadwozie nie odbija się jak piłka (zero sprężystości, średnie tarcie)
            var bodyMat = new PhysicsMaterial("CarBody") { bounciness = 0f, dynamicFriction = 0.35f, staticFriction = 0.4f, bounceCombine = PhysicsMaterialCombine.Minimum };
            foreach (var oc in _ownColliders) if (oc != null && !oc.isTrigger) oc.sharedMaterial = bodyMat;
            if (Heli == null && GetComponent<VehicleDamageFx>() == null) gameObject.AddComponent<VehicleDamageFx>();
            if (Heli == null && GetComponent<VehicleAudio>() == null) gameObject.AddComponent<VehicleAudio>();

            // wymiary auta z colliderów
            var box = GetComponent<BoxCollider>();
            if (box != null) HalfSize = new Vector2(box.size.x * 0.5f, box.size.z * 0.5f);

            // przeszkoda dla NavMeshAgentów – piesi i policjanci omijają auta (bez kosztownego "carvingu")
            if (GetComponent<NavMeshObstacle>() == null)
            {
                var obs = gameObject.AddComponent<NavMeshObstacle>();
                obs.shape = NavMeshObstacleShape.Box;
                obs.center = box != null ? box.center : new Vector3(0, 1, 0);
                obs.size = box != null ? box.size + new Vector3(0.4f, 0f, 0.4f) : new Vector3(2.2f, 1.6f, 4.6f);
                obs.carving = false;
            }

            // zapamiętaj początkowy obrót kół względem auta (działa z kołami z dowolnego modelu)
            _wheelBaseRot = new Quaternion[wheelVisuals.Length];
            for (int i = 0; i < wheelVisuals.Length; i++)
                if (wheelVisuals[i] != null)
                    _wheelBaseRot[i] = Quaternion.Inverse(transform.rotation) * wheelVisuals[i].rotation;
            _lastPos = transform.position;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            ApplyTuning();
            EngineLevel.OnValueChanged += (a, b) => ApplyTuning();
            HpLevel.OnValueChanged += (a, b) => ApplyTuning();
            Health.OnDeadChanged += OnDeadChanged;
            if (IsServer)
            {
                Health.OnDiedServer += OnDiedServer;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }
            if (Health.Dead) OnDeadChanged(true);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Health.OnDeadChanged -= OnDeadChanged;
            if (IsServer)
            {
                Health.OnDiedServer -= OnDiedServer;
                if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
        }

        /// <summary>Reflektor (tworzony na żądanie przez DayNightCycle).</summary>
        public Light EnsureHeadlight()
        {
            if (Headlight != null) return Headlight;
            var go = new GameObject("Headlight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.9f, HalfSize.y + 0.2f);
            go.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            Headlight = go.AddComponent<Light>();
            Headlight.type = LightType.Spot;
            Headlight.spotAngle = 70f;
            Headlight.range = 28f;
            Headlight.intensity = 2.5f;
            Headlight.color = new Color(1f, 0.95f, 0.8f);
            Headlight.shadows = LightShadows.None;
            Headlight.enabled = false;
            return Headlight;
        }

        // ================= STEROWANIE =================

        void Update()
        {
            UpdateWheelVisuals();
            if (Heli != null) return;
            if (!IsSpawned || !IsOwner) return;
            RunOverCheck();

            if (HasPlayerDriver && DriverClientId.Value == NetworkManager.LocalClientId)
            {
                Vector2 m = GameInput.Move;
                throttleInput = m.y;
                steerInput = m.x;
                handbrakeInput = GameInput.Handbrake;
                if (GameInput.FlipVehicle && Vector3.Dot(transform.up, Vector3.up) < 0.5f) Flip();
                if (isPoliceCar && Input.GetKeyDown(KeyCode.G)) ToggleSirenRpc();
            }
            else if (IsServer && !HasPlayerDriver && !Health.Dead)
            {
                UpdateAI();
                // po zderzeniu kierowca AI chwilę stoi oszołomiony, zamiast jechać dalej przez wrak
                if (Time.time < _stunUntil) { throttleInput = 0f; handbrakeInput = true; steerInput *= 0.3f; }
            }
            else if (!HasPlayerDriver)
            {
                throttleInput = 0f; steerInput = 0f; handbrakeInput = true;
            }
        }

        void FixedUpdate()
        {
            if (Heli != null) return;
            if (!IsSpawned || !IsOwner || Body.isKinematic) return;

            if (Health.Dead) { throttleInput = 0; steerInput = 0; handbrakeInput = true; }

            float speed = ForwardSpeed;
            // gaz i kierownica narastają płynnie (nie skaczą od 0 do 1)
            _throttleS = Mathf.MoveTowards(_throttleS, throttleInput, (Mathf.Abs(throttleInput) > Mathf.Abs(_throttleS) ? 2.2f : 5f) * Time.fixedDeltaTime);
            _steerS = Mathf.MoveTowards(_steerS, steerInput, (Mathf.Abs(steerInput) > 0.01f ? 3.2f : 5f) * Time.fixedDeltaTime);
            float steerFactor = Mathf.Lerp(1f, 0.3f, Mathf.Abs(speed) / maxSpeed); // mniej skrętu przy dużej prędkości
            float steerAngle = _steerS * maxSteerAngle * steerFactor;

            int groundedWheels = 0;
            for (int i = 0; i < wheelPoints.Length; i++)
            {
                var wp = wheelPoints[i];
                if (wp == null) continue;
                bool front = i < 2;

                if (!GroundRay(wp.position, out var hit)) continue;
                groundedWheels++;

                // --- sprężyna ---
                Vector3 wheelVel = Body.GetPointVelocity(wp.position);
                float compression = suspensionLength + wheelRadius - hit.distance;
                float springVel = Vector3.Dot(transform.up, wheelVel);
                float springForce = compression * springStrength - springVel * springDamper;
                Body.AddForceAtPosition(transform.up * Mathf.Max(0f, springForce), wp.position);

                // --- przyczepność boczna ---
                Quaternion steerRot = front ? Quaternion.AngleAxis(steerAngle, transform.up) : Quaternion.identity;
                // siła boczna działa na wysokości środka masy – nie wywraca i nie „kołysze” nadwozia na zakręcie
                Vector3 latPoint = wp.position - transform.up * Vector3.Dot(wp.position - Body.worldCenterOfMass, transform.up);
                Vector3 wheelRight = steerRot * transform.right;
                float lateralVel = Vector3.Dot(wheelRight, wheelVel);
                float grip = front ? frontGrip : (handbrakeInput ? handbrakeRearGrip : rearGrip);
                float desiredChange = -lateralVel * grip;
                float wheelMass = Body.mass * 0.25f;
                Body.AddForceAtPosition(wheelRight * (wheelMass * desiredChange / Time.fixedDeltaTime), latPoint);
            }

            UpdateStunt(groundedWheels);

            _grounded = groundedWheels >= 2;
            if (!_grounded) return;

            // tłumienie przechyłu i kiwania (nadwozie nie buja się na boki ani do przodu / tyłu)
            Vector3 av = Body.angularVelocity;
            Body.AddTorque(-transform.forward * Vector3.Dot(av, transform.forward) * 7f - transform.right * Vector3.Dot(av, transform.right) * 4f, ForceMode.Acceleration);

            // --- napęd / hamowanie ---
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            float accel = 0f;
            // przyspieszenie jak w zwykłym aucie: łagodny start, słabnie wraz z prędkością (0,32 dawnej mocy)
            float curve = Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(Mathf.Abs(speed) / Mathf.Max(1f, maxSpeed)));
            float drive = enginePower * 0.32f * curve;
            if (_throttleS > 0.01f)
                accel = speed < -0.5f ? brakePower * 0.6f : (speed < maxSpeed ? drive * _throttleS : 0f);
            else if (_throttleS < -0.01f)
                accel = speed > 0.5f ? -brakePower * 0.6f : (speed > -reverseMaxSpeed ? drive * 0.6f * _throttleS : 0f);
            else
                accel = -speed * rollingDrag; // toczenie się

            if (handbrakeInput) accel -= Mathf.Sign(speed) * Mathf.Min(Mathf.Abs(speed), brakePower * 0.5f);

            Body.AddForce(fwd * accel, ForceMode.Acceleration);
        }

        /// <summary>Raycast koła w dół, ignorujący własne collidery auta.</summary>
        bool GroundRay(Vector3 from, out RaycastHit best)
        {
            best = default;
            float bestDist = float.MaxValue;
            var hits = Physics.RaycastAll(from, -transform.up, suspensionLength + wheelRadius, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.rigidbody == Body || h.distance >= bestDist) continue;
                bestDist = h.distance;
                best = h;
            }
            return bestDist < float.MaxValue;
        }

        void UpdateWheelVisuals()
        {
            // prędkość z przesunięcia – działa też u innych graczy (tam Rigidbody jest kinematyczny)
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            ApproxVelocity = Vector3.Lerp(ApproxVelocity, (transform.position - _lastPos) / dt, 0.5f);
            float fwdSpeed = Vector3.Dot(transform.position - _lastPos, transform.forward) / dt;
            _lastPos = transform.position;
            _wheelSpin = Mathf.Repeat(_wheelSpin + fwdSpeed / Mathf.Max(0.05f, wheelRadius) * Mathf.Rad2Deg * dt, 360f);

            for (int i = 0; i < wheelVisuals.Length; i++)
            {
                var vis = wheelVisuals[i];
                if (vis == null || _wheelBaseRot == null || i >= _wheelBaseRot.Length) continue;
                float steer = i < 2 ? _steerS * maxSteerAngle : 0f;
                vis.rotation = transform.rotation * Quaternion.Euler(0f, steer, 0f) * Quaternion.Euler(_wheelSpin, 0f, 0f) * _wheelBaseRot[i];
            }
        }

        void Flip()
        {
            transform.SetPositionAndRotation(transform.position + Vector3.up * 1.5f, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
        }

        [Rpc(SendTo.Server)]
        void ToggleSirenRpc() => SirenOn.Value = !SirenOn.Value;

        // ================= SKOKI =================

        void UpdateStunt(int groundedWheels)
        {
            if (!HasPlayerDriver) { _airTime = 0f; return; }
            if (groundedWheels == 0) { _airTime += Time.fixedDeltaTime; return; }
            if (_airTime >= stuntMinAirTime && Vector3.Dot(transform.up, Vector3.up) > 0.5f) StuntRpc(_airTime);
            _airTime = 0f;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void StuntRpc(float airTime)
        {
            var p = GameUtil.GetPlayer(DriverClientId.Value);
            if (p == null) return;
            airTime = Mathf.Min(airTime, 5f); // anty-cheat
            int bonus = Mathf.Min(1500, Mathf.RoundToInt(airTime * airTime * 80f / 10f) * 10);
            p.Stats.AddMoney(bonus);
            StuntMessageRpc(airTime, bonus, RpcTarget.Single(p.OwnerClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void StuntMessageRpc(float airTime, int bonus, RpcParams rpcParams)
        {
            HUD.ShowBigMessage($"SKOK! {airTime:0.0} s", new Color(1f, 0.8f, 0.2f), 2f);
            if (Application.isBatchMode) Debug.Log($"[STUNT] {airTime:0.0}s bonus={bonus}");
        }

        // ================= AI (serwer) =================

        void UpdateAI()
        {
            Vector3? target = null;
            float desiredSpeed = aiCruiseSpeed;
            bool careful = true; // czy hamować przed przeszkodami (pościg – nie)

            if (aiMode == AIMode.Roam && RoadNetwork.Instance != null && _roamTo >= 0)
            {
                target = UpdateRoam(ref desiredSpeed);
            }
            else if (aiMode == AIMode.FollowPath && aiPath != null && aiPath.Count > 0)
            {
                Vector3 wp = aiPath.GetPoint(aiWaypointIndex);
                if (Vector3.Distance(Flat(wp), Flat(transform.position)) < 6f)
                    aiWaypointIndex = aiPath.Next(aiWaypointIndex);
                target = aiPath.GetPoint(aiWaypointIndex);
            }
            else if (aiMode == AIMode.Chase && aiChaseTarget != null)
            {
                careful = false;
                desiredSpeed = maxSpeed * 0.8f;
                _pathTimer -= Time.deltaTime;
                if (_navPath == null) _navPath = new NavMeshPath();
                if (_pathTimer <= 0f)
                {
                    _pathTimer = 0.5f;
                    if (NavMesh.SamplePosition(transform.position, out var a, 5f, NavMesh.AllAreas) &&
                        NavMesh.SamplePosition(aiChaseTarget.position, out var b, 5f, NavMesh.AllAreas))
                        NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, _navPath);
                    _navCorner = 1;
                }
                if (_navPath.corners.Length > 1)
                {
                    while (_navCorner < _navPath.corners.Length - 1 &&
                           Vector3.Distance(Flat(_navPath.corners[_navCorner]), Flat(transform.position)) < 5f)
                        _navCorner++;
                    target = _navPath.corners[Mathf.Min(_navCorner, _navPath.corners.Length - 1)];
                }
                else target = aiChaseTarget.position;
            }

            if (target == null)
            {
                throttleInput = 0f; steerInput = 0f; handbrakeInput = true;
                return;
            }

            // wyprzedzanie: przesuń cel na lewy pas
            Vector3 aim = target.Value;
            if (_overtakeTimer > 0f)
            {
                _overtakeTimer -= Time.deltaTime;
                aim -= transform.right * 6f;
            }

            Vector3 local = transform.InverseTransformPoint(aim);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            steerInput = Mathf.Clamp(angle / maxSteerAngle, -1f, 1f);

            // zwolnij na zakrętach
            float turnSlow = Mathf.Lerp(1f, 0.4f, Mathf.Abs(angle) / 90f);
            float targetSpeed = desiredSpeed * turnSlow;
            float speed = ForwardSpeed;

            // czujnik przeszkód: hamuj przed autami, ludźmi i ścianami
            if (careful)
            {
                float range = Mathf.Max(8f, speed * 1.6f + 6f);
                if (SenseAhead(range, out float dist, out bool isVehicle, out bool movingAway))
                {
                    float safe = Mathf.Max(0f, (dist - 3.5f) * 0.9f);
                    if (!movingAway) targetSpeed = Mathf.Min(targetSpeed, safe);
                    else targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(safe, 3f));

                    // stoi przed nami auto, które się nie rusza → po chwili wyprzedź
                    // (ale nie w korku przed światłami / skrzyżowaniem – auta AI w kolejce czekają legalnie)
                    bool queue = WaitingAtRed || (_aheadVehicle != null && _aheadVehicle.IsAIDriven.Value && !_aheadVehicle.IsDestroyedWreck);
                    if (isVehicle && !queue && !movingAway && dist < 10f && Mathf.Abs(speed) < 1f)
                    {
                        _blockedTimer += Time.deltaTime;
                        if (_blockedTimer > 5f && _overtakeTimer <= 0f) { _overtakeTimer = 3.5f; _blockedTimer = 0f; }
                    }
                    else _blockedTimer = 0f;
                }
                else _blockedTimer = 0f;
            }

            handbrakeInput = false;

            // wykrywanie zablokowania (np. na krawężniku) → cofanie
            if (_reverseTimer > 0f)
            {
                _reverseTimer -= Time.deltaTime;
                throttleInput = -1f;
                steerInput = -steerInput;
                return;
            }
            if (targetSpeed > 2f && Mathf.Abs(speed) < 1f) _stuckTimer += Time.deltaTime; else _stuckTimer = 0f;
            if (_stuckTimer > 3f) { _stuckTimer = 0f; _reverseTimer = 1.5f; }

            if (targetSpeed < 0.5f) { throttleInput = speed > 0.5f ? -1f : 0f; handbrakeInput = speed < 0.5f; return; }
            throttleInput = speed < targetSpeed ? 1f : (speed > targetSpeed + 2f ? -0.6f : 0f);
        }

        /// <summary>Ruch po sieci dróg: dojazd do linii stopu, czekanie na wolne skrzyżowanie, przejazd.</summary>
        Vector3? UpdateRoam(ref float desiredSpeed)
        {
            var net = RoadNetwork.Instance;
            Vector3 inDir = Flat(net.nodes[_roamTo] - net.nodes[_roamFrom]);
            Vector3 outDir = Flat(net.nodes[_roamNext] - net.nodes[_roamTo]);
            float turn = Vector3.Angle(inDir, outDir);
            if (!_crossing)
            {
                desiredSpeed = net.SpeedLimit(_roamFrom, _roamTo);

                // sygnalizacja świetlna: czerwone (lub żółte, gdy da się bezpiecznie zahamować) → stój przed pasami
                var signal = TrafficSignal.At(net.nodes[_roamTo]);
                if (signal != null)
                {
                    Vector3 dirIn = inDir.normalized;
                    float along = Vector3.Dot(Flat(net.nodes[_roamTo] - transform.position), dirIn);
                    var light = signal.StateFor(dirIn);
                    bool mustStop = light == TrafficSignal.LightState.Red
                        || (light == TrafficSignal.LightState.Yellow && along > net.signalStopDistance + 10f);
                    if (mustStop && along > net.signalStopDistance - 1.5f)
                    {
                        float toLine = along - net.signalStopDistance;
                        desiredSpeed = toLine < 1.2f ? 0f : Mathf.Min(desiredSpeed, toLine * 0.6f + 1f);
                        WaitingAtRed = true;
                        return net.SignalStopPoint(_roamFrom, _roamTo) + dirIn * 3f;
                    }
                }
                WaitingAtRed = false;

                Vector3 stop = net.StopPoint(_roamFrom, _roamTo);
                float d = Vector3.Distance(Flat(stop), Flat(transform.position));

                if (d < 4.5f)
                {
                    // skrzyżowanie zajęte → czekaj na linii
                    if (net.IsIntersection(_roamTo) && IntersectionBusy(net.nodes[_roamTo], net.intersectionRadius))
                    {
                        desiredSpeed = 0f;
                        return stop + (stop - transform.position).normalized; // trzymaj kierunek
                    }
                    _crossing = true;
                }
                else
                {
                    // zwalniaj przed skrzyżowaniem / zakrętem
                    if (turn > 25f) desiredSpeed = Mathf.Min(desiredSpeed, 5f + d * 0.35f);
                    else if (net.IsIntersection(_roamTo)) desiredSpeed = Mathf.Min(desiredSpeed, 9f + d * 0.4f);
                    return stop;
                }
            }

            // przejazd przez skrzyżowanie
            Vector3 exit = net.ExitPoint(_roamTo, _roamNext);
            desiredSpeed = turn > 25f ? 6.5f : Mathf.Min(10f, net.SpeedLimit(_roamTo, _roamNext));
            if (Vector3.Distance(Flat(exit), Flat(transform.position)) < 4f)
            {
                _roamFrom = _roamTo;
                _roamTo = _roamNext;
                _roamNext = net.PickNext(_roamFrom, _roamTo);
                _crossing = false;
                return net.StopPoint(_roamFrom, _roamTo);
            }
            return exit;
        }

        bool IntersectionBusy(Vector3 center, float radius)
        {
            // liczy się ŚRODEK auta – auta czekające na liniach stopu nie blokują skrzyżowania
            float r2 = radius * radius;
            foreach (var v in All)
            {
                if (v == this || v == null) continue;
                // stojący wrak / porzucone auto gracza nie blokuje na zawsze (omijamy je czujnikiem)
                if (!v.IsAIDriven.Value && v.ApproxVelocity.sqrMagnitude < 1f) continue;
                Vector3 d = v.transform.position - center; d.y = 0;
                if (d.sqrMagnitude < r2) return true;
            }
            return false;
        }

        /// <summary>BoxCast przed autem (szerokość auta). Zwraca odległość do najbliższej przeszkody.</summary>
        bool SenseAhead(float range, out float dist, out bool isVehicle, out bool movingAway)
        {
            dist = range; isVehicle = false; movingAway = false; _aheadVehicle = null;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            // patrz trochę w stronę skrętu
            fwd = Quaternion.AngleAxis(steerInput * 20f, Vector3.up) * fwd;
            Vector3 origin = transform.position + Vector3.up * 1.0f + fwd * (HalfSize.y - 0.3f);
            Vector3 half = new Vector3(HalfSize.x * 0.9f, 0.45f, 0.3f);
            var hits = Physics.BoxCastAll(origin, half, fwd, Quaternion.LookRotation(fwd), range, ~0, QueryTriggerInteraction.Collide);
            bool found = false;
            foreach (var h in hits)
            {
                if (h.distance <= 0f || h.distance >= dist) continue;
                if (IsOwnCollider(h.collider)) continue;
                var hp = h.collider.GetComponentInParent<Health>();
                if (h.collider.isTrigger && hp == null) continue;
                if (hp != null && hp.Dead) continue;
                var v = h.collider.GetComponentInParent<Vehicle>();
                dist = h.distance;
                isVehicle = v != null;
                _aheadVehicle = v;
                movingAway = v != null && Vector3.Dot(v.ApproxVelocity, fwd) > 2f;
                found = true;
            }
            return found;
        }

        bool IsOwnCollider(Collider c)
        {
            foreach (var o in _ownColliders) if (o == c) return true;
            return false;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>Serwer: ustaw auto jako ruch uliczny jadący po ścieżce.</summary>
        public void StartTraffic(WaypointPath path, int startIndex)
        {
            if (!IsServer) return;
            aiMode = AIMode.FollowPath;
            aiPath = path;
            aiWaypointIndex = path.Next(startIndex);
            IsAIDriven.Value = true;
        }

        /// <summary>Serwer: ruch uliczny po sieci dróg (odcinek from→to).</summary>
        public void StartRoam(int fromNode, int toNode)
        {
            if (!IsServer || RoadNetwork.Instance == null) return;
            aiMode = AIMode.Roam;
            _roamFrom = fromNode;
            _roamTo = toNode;
            _roamNext = RoadNetwork.Instance.PickNext(fromNode, toNode);
            _crossing = false;
            aiCruiseSpeed = RoadNetwork.Instance.SpeedLimit(fromNode, toNode);
            IsAIDriven.Value = true;
        }

        public void StartChase(Transform target)
        {
            if (!IsServer) return;
            aiMode = AIMode.Chase;
            aiChaseTarget = target;
            IsAIDriven.Value = true;
        }

        public void StopAI()
        {
            if (!IsServer) return;
            aiMode = AIMode.None;
            aiPath = null;
            aiChaseTarget = null;
            _roamTo = -1;
            IsAIDriven.Value = false;
        }

        // ================= KIEROWCA (serwer) =================

        public bool CanEnter => !Health.Dead && !HasPlayerDriver;

        /// <summary>Serwer: gracz wsiada. Zwraca true jeśli się udało.</summary>
        public bool SetDriverServer(PlayerController player)
        {
            if (!IsServer || !CanEnter) return false;

            bool wasOccupiedByAI = IsAIDriven.Value;
            StopAI();
            DriverClientId.Value = player.OwnerClientId;
            NetworkObject.ChangeOwnership(player.OwnerClientId);

            // przestępstwo: kradzież
            if (isPoliceCar) WantedSystem.ReportCrime(player.OwnerClientId, 1, 2);
            else if (wasOccupiedByAI) WantedSystem.ReportCrime(player.OwnerClientId, 1);

            if (wasOccupiedByAI) VehicleEvents.RaiseCarjacked(this, player);
            return true;
        }

        public void ClearDriverServer()
        {
            if (!IsServer) return;
            DriverClientId.Value = GameUtil.NoClient;
            if (NetworkObject.OwnerClientId != NetworkManager.ServerClientId) NetworkObject.RemoveOwnership();
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (DriverClientId.Value == clientId) ClearDriverServer();
        }

        // ================= KOLIZJE I WYBUCH =================

        float _stunUntil;
        float _graceUntil;

        // po zmianie właściciela (wsiadanie/wysiadanie) fizyka auta przez moment „łapie” stare prędkości – nie liczymy wtedy zderzeń
        public override void OnGainedOwnership() { base.OnGainedOwnership(); _graceUntil = Time.time + 1.5f; }
        public override void OnLostOwnership() { base.OnLostOwnership(); _graceUntil = Time.time + 1.5f; }

        float _nextRunOver;

        /// <summary>Przejeżdżanie ludzi: strefa przed zderzakiem w kierunku jazdy (kolizje z kapsułami NPC są zbyt miękkie, żeby polegać na OnCollisionEnter).</summary>
        void RunOverCheck()
        {
            if (Time.time < _nextRunOver || Health.Dead) return;
            Vector3 vel = ApproxVelocity; vel.y = 0f;
            float sp = vel.magnitude;
            if (sp < 3f) return;
            _nextRunOver = Time.time + 0.08f;
            Vector3 dir = vel / sp;
            Vector3 center = transform.position + Vector3.up * 0.9f + dir * (HalfSize.y * 0.55f);
            var cols = Physics.OverlapBox(center, new Vector3(HalfSize.x + 0.25f, 0.9f, HalfSize.y * 0.5f + 0.35f), Quaternion.LookRotation(dir), ~0, QueryTriggerInteraction.Ignore);
            ulong attacker = HasPlayerDriver ? DriverClientId.Value : GameUtil.NoClient;
            foreach (var col in cols)
            {
                var h = col.GetComponentInParent<Health>();
                if (h == null || h == Health || h.Dead || h.faction == Faction.Vehicle) continue;
                if (h.GetComponent<Vehicle>() != null || h.GetComponent<PlayerController>() != null) continue;
                float dmg = Mathf.Clamp(sp * 9f, 30f, 220f);
                if (IsServer) h.TakeDamage(dmg, attacker);
                else RunOverRpc(new NetworkObjectReference(h.NetworkObject), dmg);
                if (h.GetComponent<NetworkObject>() != null) Effects.Blood(h.transform.position + Vector3.up, dir);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RunOverRpc(NetworkObjectReference target, float dmg)
        {
            if (!target.TryGet(out var no)) return;
            var h = no.GetComponent<Health>();
            if (h == null || h.Dead || h.GetComponent<PlayerController>() != null) return;
            h.TakeDamage(Mathf.Min(dmg, 220f), DriverClientId.Value);
        }

        void OnCollisionEnter(Collision c)
        {
            if (Heli != null) return; // helikopter nie dostaje obrażeń od zderzeń z ziemią
            if (!IsSpawned || !IsOwner) return;
            if (Time.time < _graceUntil) return;
            float impact = c.relativeVelocity.magnitude;
            if (c.contactCount > 0)
            {
                var cn = c.GetContact(0).normal;
                if (Mathf.Abs(cn.y) > 0.7f) return;                          // jezdnia / chodnik / dach – to nie uderzenie
                impact = Mathf.Abs(Vector3.Dot(c.relativeVelocity, cn));      // ślizg wzdłuż ściany nie boli, liczy się uderzenie w nią
            }
            if (impact > 25f) Debug.Log($"[CAR] {name}: uderzenie {impact:0.0} m/s w {c.collider.name}");
            var otherHealth = c.collider.GetComponentInParent<Health>();

            // statystyka: czy boty się zderzają
            if (IsServer && IsAIDriven.Value && impact > 2f && otherHealth != null)
            {
                var ov = otherHealth.GetComponent<Vehicle>();
                if (ov != null && ov.IsAIDriven.Value && NetworkObjectId < ov.NetworkObjectId) AiCrashes++;
                else if (ov == null && otherHealth.faction == Faction.Civilian) AiPedHits++;
            }

            // kierowca AI zatrzymuje się po poważniejszym stuknięciu (im mocniej, tym dłużej)
            if (IsServer && IsAIDriven.Value && impact > 3.5f) _stunUntil = Time.time + Mathf.Clamp(impact * 0.25f, 1.2f, 4f);

            if (impact < minImpactSpeed) return;

            float damage = (impact - minImpactSpeed) * collisionDamageMultiplier;
            ulong attacker = HasPlayerDriver ? DriverClientId.Value : GameUtil.NoClient;
            damage = Mathf.Min(damage, 60f);

            if (IsServer)
            {
                ApplyCollisionDamage(damage, otherHealth, impact, attacker);
            }
            else
            {
                var otherRef = otherHealth != null ? new NetworkObjectReference(otherHealth.NetworkObject) : default;
                CollisionDamageRpc(damage, otherHealth != null, otherRef, impact);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void CollisionDamageRpc(float damage, bool hasOther, NetworkObjectReference other, float impact)
        {
            // prosta walidacja przeciw oszustom
            damage = Mathf.Min(damage, 60f);
            Health h = null;
            if (hasOther && other.TryGet(out var no)) h = no.GetComponent<Health>();
            ApplyCollisionDamage(damage, h, impact, DriverClientId.Value);
        }

        void ApplyCollisionDamage(float damage, Health other, float impact, ulong attacker)
        {
            Health.TakeDamage(damage * 0.5f, attacker);
            // mocne uderzenie rani też kierowcę-gracza (pasów nie ma)
            if (HasPlayerDriver && impact > 14f)
            {
                var drv = GameUtil.GetPlayer(DriverClientId.Value);
                if (drv != null && drv.Health != null && !drv.Health.Dead) drv.Health.TakeDamage(Mathf.Min((impact - 14f) * 1.0f, 30f), GameUtil.NoClient);
            }
            if (other != null && other != Health)
            {
                // potrącenie pieszego zadaje dużo więcej obrażeń
                float mult = other.faction == Faction.Vehicle ? 0.5f : 4f;
                other.TakeDamage(damage * mult, attacker);
            }
        }

        void OnDiedServer(ulong killer)
        {
            // wyrzuć kierowcę
            if (HasPlayerDriver)
            {
                var p = GameUtil.GetPlayer(DriverClientId.Value);
                if (p != null && p.Vehicles != null) p.Vehicles.ForceExitServer();
            }
            StopAI();
            if (NetworkObject.OwnerClientId != NetworkManager.ServerClientId) NetworkObject.RemoveOwnership();

            // helikopter nie wybucha – spada, a po wylądowaniu znów lata
            if (Heli != null) { Heli.OnShotDown(transform.position); return; }

            ExplodeRpc(transform.position);
            CombatUtil.ExplodeServer(transform.position, explosionRadius, explosionDamage, killer, Health);
            StartCoroutine(LaunchAfterExplosion());
        }

        IEnumerator LaunchAfterExplosion()
        {
            yield return null;
            if (!Body.isKinematic)
                Body.AddForce(Vector3.up * 6f + Random.insideUnitSphere * 2f, ForceMode.VelocityChange);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ExplodeRpc(Vector3 pos) => Effects.Explosion(pos, explosionRadius);

        void OnDeadChanged(bool dead)
        {
            if (!dead) return;
            foreach (var r in _renderers)
                if (r != null) r.material.color = new Color(0.12f, 0.12f, 0.12f);
        }
    }
}
