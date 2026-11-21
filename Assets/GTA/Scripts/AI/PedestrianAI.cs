using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace MiniGTA
{
    /// <summary>
    /// Przechodzień: spaceruje losowo po NavMeshu, ucieka przed strzałami i napastnikiem.
    /// AI działa TYLKO na serwerze; klienci dostają pozycję przez (serwerowy) NetworkTransform.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(Health))]
    public class PedestrianAI : NetworkBehaviour
    {
        public float walkSpeed = 1.6f;
        public float runSpeed = 6f;
        public float wanderRadius = 25f;
        public float fleeDuration = 8f;
        public float hearingRange = 35f;
        public int moneyDropMin = 5;
        public int moneyDropMax = 60;
        [Tooltip("Koszt chodzenia po jezdni (większy = piesi trzymają się chodników)")]
        public float roadCost = 40f;
        [Tooltip("Koszt chodzenia po trawnikach/placach wewnątrz kwartału")]
        public float lotCost = 3f;
        [Tooltip("Czekaj na pasach, aż auta na przecinanej ulicy dostaną czerwone")]
        public bool obeyTrafficLights = true;
        [Tooltip("Uskakiwanie przed nadjeżdżającym autem")]
        public bool dodgeCars = true;

        [Header("Opcjonalnie: animacje (float 'Speed', bool 'Dead')")]
        public Animator animator;

        NavMeshAgent _agent;
        Health _health;
        float _fleeUntil, _nextDecision, _nextDodgeCheck, _nextLightCheck;
        bool _waitingForLight;
        Vector3 _fleeFrom;

        public bool IsFleeing => Time.time < _fleeUntil;
        public bool WaitingForLight => _waitingForLight;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();
            CharacterAppearance.Ensure(gameObject); // każdy przechodzień dostaje losowe ubranie i kolory
            NameTag.Add(gameObject);                // i imię nad głową
        }

        public override void OnNetworkSpawn()
        {
            _health.OnDeadChanged += OnDeadChanged;
            if (!IsServer)
            {
                _agent.enabled = false; // klient tylko wyświetla
                return;
            }
            _health.OnDamagedServer += OnDamaged;
            _health.OnDiedServer += OnDied;
            CombatEvents.Gunshot += OnGunshot;
            _agent.speed = walkSpeed;

            // chodniki zamiast jezdni + omijanie innych ludzi
            _agent.SetAreaCost(GameUtil.RoadArea, roadCost);
            _agent.SetAreaCost(GameUtil.CrosswalkArea, 1f);
            _agent.SetAreaCost(GameUtil.LotArea, lotCost);
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            _agent.avoidancePriority = Random.Range(30, 70);
        }

        public override void OnNetworkDespawn()
        {
            _health.OnDeadChanged -= OnDeadChanged;
            if (!IsServer) return;
            _health.OnDamagedServer -= OnDamaged;
            _health.OnDiedServer -= OnDied;
            CombatEvents.Gunshot -= OnGunshot;
        }

        void Update()
        {
            if (animator != null) animator.SetFloat("Speed", _agent.enabled ? _agent.velocity.magnitude : 0f);
            if (!IsServer || !IsSpawned || _health.Dead || !_agent.isOnNavMesh) return;

            if (dodgeCars && Time.time > _nextDodgeCheck)
            {
                _nextDodgeCheck = Time.time + 0.2f;
                if (CheckIncomingCar()) return;
            }

            if (IsFleeing)
            {
                if (_waitingForLight) { _waitingForLight = false; _agent.isStopped = false; }
                _agent.speed = runSpeed;
                if (Time.time > _nextDecision || _agent.remainingDistance < 2f)
                {
                    _nextDecision = Time.time + 1f;
                    Vector3 away = (transform.position - _fleeFrom).normalized;
                    if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
                    away.y = 0;
                    Vector3 target = transform.position + away.normalized * 15f + Random.insideUnitSphere * 4f;
                    if (NavMesh.SamplePosition(target, out var hit, 8f, NavMesh.AllAreas)) _agent.SetDestination(hit.position);
                }
            }
            else
            {
                if (obeyTrafficLights && Time.time > _nextLightCheck)
                {
                    _nextLightCheck = Time.time + 0.25f;
                    bool wait = MustWaitAtCrossing();
                    if (wait != _waitingForLight) { _waitingForLight = wait; _agent.isStopped = wait; }
                }
                if (_waitingForLight) return;
                _agent.speed = walkSpeed;
                if (Time.time > _nextDecision || (!_agent.pathPending && _agent.remainingDistance < 1f))
                {
                    _nextDecision = Time.time + Random.Range(6f, 15f);
                    if (GameUtil.RandomNavPoint(transform.position, 5f, wanderRadius, GameUtil.SidewalkMask, out var p)) _agent.SetDestination(p);
                }
            }
        }

        /// <summary>
        /// Stoję na chodniku tuż przed zebrą, a auta na przecinanej ulicy mają zielone/żółte → czekaj.
        /// Kto już jest na pasach lub jezdni, ten przechodzi dalej.
        /// </summary>
        bool MustWaitAtCrossing()
        {
            Vector3 vel = _waitingForLight ? (_agent.steeringTarget - transform.position) : _agent.desiredVelocity;
            vel.y = 0;
            if (vel.sqrMagnitude < 0.01f) return false;
            Vector3 me = transform.position;
            const int roadMask = (1 << GameUtil.RoadArea) | (1 << GameUtil.CrosswalkArea);
            if (NavMesh.SamplePosition(me, out _, 0.4f, roadMask)) return false; // już na jezdni
            Vector3 dir = vel.normalized;
            if (!NavMesh.SamplePosition(me + dir * 1.3f, out _, 0.5f, roadMask)) return false; // nie wchodzę na jezdnię
            var signal = TrafficSignal.Nearest(me, 18f);
            if (signal == null) return false;
            // idę wzdłuż Z → przecinam ulicę, po której auta jadą wzdłuż X
            bool crossingXTraffic = Mathf.Abs(dir.z) > Mathf.Abs(dir.x);
            return !signal.PedestrianCanCross(crossingXTraffic);
        }

        /// <summary>Czy w ciągu ~1.5 s wjedzie we mnie auto? Jeśli tak – uskocz w bok.</summary>
        bool CheckIncomingCar()
        {
            Vector3 me = transform.position;
            foreach (var v in Vehicle.All)
            {
                if (v == null) continue;
                Vector3 vel = v.ApproxVelocity; vel.y = 0;
                float speed = vel.magnitude;
                if (speed < 4f) continue;
                Vector3 rel = me - v.transform.position; rel.y = 0;
                if (rel.sqrMagnitude > 30f * 30f) continue;
                Vector3 dir = vel / speed;
                float along = Vector3.Dot(rel, dir);
                if (along < 0f || along > speed * 1.5f + 3f) continue;
                Vector3 side = Vector3.Cross(Vector3.up, dir);
                float lateral = Vector3.Dot(rel, side);
                if (Mathf.Abs(lateral) > v.HalfSize.x + 1.6f) continue;

                // uskocz w stronę, po której już stoimy względem toru auta
                Vector3 dodge = me + side * ((lateral >= 0f ? 1f : -1f) * 4.5f);
                if (NavMesh.SamplePosition(dodge, out var hit, 3f, NavMesh.AllAreas))
                {
                    _agent.speed = runSpeed * 1.2f;
                    _agent.SetDestination(hit.position);
                    _fleeFrom = v.transform.position;
                    _fleeUntil = Time.time + 2f;
                    _nextDecision = Time.time + 1.2f;
                }
                return true;
            }
            return false;
        }

        public void Flee(Vector3 from)
        {
            _fleeFrom = from;
            _fleeUntil = Time.time + fleeDuration;
            _nextDecision = 0f;
        }

        void OnGunshot(Vector3 pos, ulong shooter)
        {
            if (!_health.Dead && Vector3.Distance(pos, transform.position) < hearingRange) Flee(pos);
        }

        void OnDamaged(float dmg, ulong attacker)
        {
            var p = GameUtil.GetPlayer(attacker);
            Flee(p != null ? p.transform.position : transform.position - transform.forward);
        }

        void OnDied(ulong killer)
        {
            if (killer != GameUtil.NoClient)
            {
                WantedSystem.ReportCrime(killer, 1);
                var p = GameUtil.GetPlayer(killer);
                if (p != null) p.Stats.AddMoney(Random.Range(moneyDropMin, moneyDropMax + 1));
            }
        }

        void OnDeadChanged(bool dead)
        {
            if (animator != null) animator.SetBool("Dead", dead);
            // zwłoki nie blokują aut (można przejechać dalej po potrąceniu)
            if (dead) foreach (var c in GetComponentsInChildren<Collider>()) c.isTrigger = true;
            var look = GetComponent<CharacterAppearance>();
            if (look != null && look.HasModel)
            {
                look.SetLying(dead);
                if (dead && IsServer) _agent.enabled = false;
                return;
            }
            if (dead && IsServer)
            {
                // "animacja" śmierci dla kapsuły – podmień na ragdoll / Animator
                _agent.enabled = false;
                transform.rotation = Quaternion.Euler(-90f, transform.eulerAngles.y, 0f);
                transform.position += Vector3.down * 0.6f;
            }
        }
    }
}
