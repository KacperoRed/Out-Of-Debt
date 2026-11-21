using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Pocisk fizyczny: granat (wybucha po czasie, odbija się) albo rakieta (wybucha przy uderzeniu).
    /// Symuluje go SERWER, klienci widzą pozycję przez NetworkTransform.
    /// Prefab: NetworkObject + NetworkTransform + Rigidbody + NetworkRigidbody + collider + Projectile.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Projectile : NetworkBehaviour
    {
        [Tooltip("Wybuch przy pierwszym uderzeniu (rakieta). Wyłączone = wybuch po fuseTime (granat).")]
        public bool explodeOnImpact;
        public float fuseTime = 3f;
        public float maxLifetime = 8f;
        public bool useGravity = true;

        Rigidbody _rb;
        ulong _attacker = GameUtil.NoClient;
        float _damage = 100f, _radius = 6f, _spawnTime;
        bool _exploded;

        void Awake() => _rb = GetComponent<Rigidbody>();

        /// <summary>Serwer: nadaj pociskowi prędkość i parametry wybuchu.</summary>
        public void Launch(ulong attacker, Vector3 velocity, float damage, float radius, Transform thrower)
        {
            _attacker = attacker;
            _damage = damage;
            _radius = radius;
            _spawnTime = Time.time;
            _rb.useGravity = useGravity;
            _rb.isKinematic = false;
            _rb.linearVelocity = velocity;
            if (!explodeOnImpact) _rb.angularVelocity = Random.insideUnitSphere * 10f;

            // nie zderzaj się z rzucającym
            if (thrower != null)
                foreach (var a in GetComponentsInChildren<Collider>())
                foreach (var b in thrower.GetComponentsInChildren<Collider>(true))
                    Physics.IgnoreCollision(a, b);
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || _exploded) return;
            float age = Time.time - _spawnTime;
            if ((!explodeOnImpact && age > fuseTime) || age > maxLifetime) Explode();
            if (explodeOnImpact && _rb.linearVelocity.sqrMagnitude > 1f)
                transform.rotation = Quaternion.LookRotation(_rb.linearVelocity); // rakieta leci nosem do przodu
        }

        void OnCollisionEnter(Collision c)
        {
            if (!IsServer || !IsSpawned || !explodeOnImpact || Time.time - _spawnTime < 0.05f) return;
            Explode();
        }

        void Explode()
        {
            if (_exploded) return;
            _exploded = true;
            if (Health.DebugLog) Debug.Log($"[BOOM] {name} at {transform.position} radius={_radius} dmg={_damage} attacker={_attacker}");
            CombatUtil.ExplodeServer(transform.position, _radius, _damage, _attacker, null);
            ExplodeRpc(transform.position, _radius);
            NetworkObject.Despawn(true);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ExplodeRpc(Vector3 pos, float radius) => Effects.Explosion(pos, radius);
    }

    public static class CombatUtil
    {
        /// <summary>
        /// Serwer: obrażenia obszarowe (spadają z odległością) + odrzut fizyczny + "hałas" dla AI.
        /// Używane przez granaty, rakiety i wybuchające auta.
        /// </summary>
        public static void ExplodeServer(Vector3 pos, float radius, float damage, ulong attacker, Health ignore)
        {
            var done = new HashSet<Health>();
            foreach (var col in Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Collide))
            {
                var rb = col.attachedRigidbody;
                if (rb != null && !rb.isKinematic) rb.AddExplosionForce(damage * 40f, pos, radius, 1.5f);

                var hp = col.GetComponentInParent<Health>();
                if (hp == null || hp == ignore || !done.Add(hp)) continue;
                float dist = Vector3.Distance(hp.transform.position + Vector3.up, pos);
                float k = Mathf.Clamp01(1f - dist / radius);
                if (k > 0f) hp.TakeDamage(damage * (0.25f + 0.75f * k), attacker);
            }
            CombatEvents.RaiseGunshot(pos, attacker);
        }
    }
}
