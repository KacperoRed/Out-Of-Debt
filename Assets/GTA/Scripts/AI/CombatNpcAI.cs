using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace MiniGTA
{
    /// <summary>
    /// Uzbrojony NPC: policjant (Health.faction = Police) albo gangster (Faction.Gang).
    /// Policja ściga graczy z gwiazdkami, gang atakuje graczy z listy wrogów (misje) i tych, którzy go zaatakowali.
    /// AI działa tylko na serwerze.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(Health))]
    public class CombatNpcAI : NetworkBehaviour
    {
        public static readonly List<CombatNpcAI> All = new List<CombatNpcAI>();

        [Header("Walka")]
        public float damage = 8f;
        public float fireRate = 1f;
        public float attackRange = 25f;
        public float sightRange = 55f;
        [Range(0f, 1f)] public float accuracy = 0.55f;
        public float chaseSpeed = 5.5f;
        public float patrolSpeed = 1.8f;

        [Header("Nagroda za zabicie")]
        public int moneyDrop = 50;
        [Tooltip("Rodzaj służb (ustawia PoliceSpawner po zespawnowaniu)")]
        public PoliceTier tier = PoliceTier.Police;

        [Header("Opcjonalnie: animacje (float 'Speed', bool 'Dead', trigger 'Shoot')")]
        public Animator animator;

        /// <summary>Gracze, na których ten NPC poluje zawsze (np. uczestnicy misji).</summary>
        public readonly HashSet<ulong> HostileTo = new HashSet<ulong>();

        public Faction Faction => _health.faction;
        public PlayerController Target { get; private set; }

        NavMeshAgent _agent;
        Health _health;
        float _nextRetarget, _nextShot, _nextRepath, _nextWander;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();
            // policja w mundurach, gangsterzy w losowych ubraniach (kolory/stroje z paczki postaci)
            CharacterAppearance.Ensure(gameObject, false, _health.faction == Faction.Police);
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            _health.OnDeadChanged += OnDeadChanged;
            if (!IsServer)
            {
                _agent.enabled = false;
                return;
            }
            _health.OnDamagedServer += OnDamaged;
            _health.OnDiedServer += OnDied;
            CombatEvents.Gunshot += OnGunshot;
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            _agent.avoidancePriority = Random.Range(20, 50); // policja ma pierwszeństwo przed przechodniami
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
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

            if (Time.time > _nextRetarget)
            {
                _nextRetarget = Time.time + 0.5f;
                Target = PickTarget();
            }

            if (Target == null || Target.IsDead)
            {
                Patrol();
                return;
            }

            Vector3 eye = transform.position + Vector3.up * 1.6f;
            Vector3 targetPoint = Target.transform.position + Vector3.up * (Target.InVehicle ? 0.8f : 1.2f);
            float dist = Vector3.Distance(eye, targetPoint);
            var targetVehicle = Target.Vehicles != null ? Target.Vehicles.CurrentVehicle : null;
            Transform targetRoot = targetVehicle != null ? targetVehicle.transform : Target.transform;
            bool los = dist < sightRange && GameUtil.HasLineOfSight(eye, targetPoint, transform, targetRoot);

            // policja „widzi” gracza do ok. 45 m (dalej gracz może się już wymknąć)
            if (los && dist < 45f && Faction == Faction.Police && Target.Wanted != null) Target.Wanted.ReportSeen();

            if (los && dist < attackRange)
            {
                _agent.isStopped = true;
                Vector3 look = targetPoint - transform.position;
                look.y = 0;
                if (look.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), 360f * Time.deltaTime);

                if (Time.time > _nextShot)
                {
                    _nextShot = Time.time + 1f / fireRate * Random.Range(0.8f, 1.3f);
                    Shoot(eye, targetPoint, dist);
                }
            }
            else
            {
                _agent.isStopped = false;
                _agent.speed = chaseSpeed;
                if (Time.time > _nextRepath)
                {
                    _nextRepath = Time.time + 0.3f;
                    _agent.SetDestination(Target.transform.position);
                }
            }
        }

        PlayerController PickTarget()
        {
            PlayerController best = null;
            float bestDist = float.MaxValue;
            foreach (var p in PlayerController.All)
            {
                if (p.IsDead) continue;
                bool valid = Faction == Faction.Police
                    ? (p.Wanted != null && p.Wanted.Level > 0)
                    : HostileTo.Contains(p.OwnerClientId);
                if (!valid) continue;
                float d = Vector3.Distance(p.transform.position, transform.position);
                if (d < bestDist && d < 200f) { bestDist = d; best = p; }
            }
            return best;
        }

        void Patrol()
        {
            _agent.isStopped = false;
            _agent.speed = patrolSpeed;
            if (Time.time > _nextWander || (!_agent.pathPending && _agent.remainingDistance < 1f))
            {
                _nextWander = Time.time + Random.Range(5f, 12f);
                if (GameUtil.RandomNavPoint(transform.position, 5f, 20f, out var p)) _agent.SetDestination(p);
            }
        }

        void Shoot(Vector3 from, Vector3 target, float dist)
        {
            if (animator != null) animator.SetTrigger("Shoot");
            // szansa trafienia maleje z odległością i gdy cel jedzie autem
            float chance = accuracy * Mathf.Lerp(1f, 0.4f, dist / attackRange);
            if (Target.InVehicle) chance *= 0.7f;
            Vector3 aim = target;
            if (Random.value > chance) aim += Random.insideUnitSphere * 2.5f;

            Vector3 dir = (aim - from).normalized;
            var hit = WeaponController.ShootRay(from, dir, attackRange + 5f, transform, out var end);
            if (hit != null)
            {
                // policja nie strzela do siebie nawzajem (friendly fire wyłączony)
                if (!(hit.faction == Faction && hit.faction != Faction.Player)) hit.TakeDamage(damage, GameUtil.NoClient);
            }
            TracerRpc(from + transform.forward * 0.4f, end);
            CombatEvents.RaiseGunshot(from, GameUtil.NoClient);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void TracerRpc(Vector3 from, Vector3 to) => Effects.Tracer(from, to, new Color(1f, 0.6f, 0.4f));

        void OnGunshot(Vector3 pos, ulong shooter)
        {
            // policja słyszy strzały gracza w pobliżu → 1 gwiazdka
            if (shooter == GameUtil.NoClient || Faction != Faction.Police) return;
            if (Vector3.Distance(pos, transform.position) < 40f) WantedSystem.ReportCrime(shooter, 0, 1);
        }

        void OnDamaged(float dmg, ulong attacker)
        {
            if (attacker == GameUtil.NoClient) return;
            if (Faction == Faction.Police) WantedSystem.ReportCrime(attacker, 0, 1);
            else HostileTo.Add(attacker);
        }

        void OnDied(ulong killer)
        {
            if (killer == GameUtil.NoClient) return;
            if (Faction == Faction.Police) WantedSystem.ReportCrime(killer, 1, 2);
            var p = GameUtil.GetPlayer(killer);
            if (p != null) p.Stats.AddMoney(moneyDrop);
        }

        void OnDeadChanged(bool dead)
        {
            if (animator != null) animator.SetBool("Dead", dead);
            var look = GetComponent<CharacterAppearance>();
            if (look != null && look.HasModel)
            {
                look.SetLying(dead);
                if (dead && IsServer) _agent.enabled = false;
                return;
            }
            if (dead && IsServer)
            {
                _agent.enabled = false;
                transform.rotation = Quaternion.Euler(-90f, transform.eulerAngles.y, 0f);
                transform.position += Vector3.down * 0.6f;
            }
        }
    }
}
