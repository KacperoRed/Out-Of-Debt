using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Główny skrypt gracza: chodzenie, bieganie, skakanie, śmierć i respawn ("WASTED").
    /// Ruch liczony jest u WŁAŚCICIELA (klienta) i wysyłany przez ClientNetworkTransform.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(Health))]
    public class PlayerController : NetworkBehaviour
    {
        public static readonly List<PlayerController> All = new List<PlayerController>();
        public static PlayerController Local { get; private set; }

        [Header("Ruch")]
        public float walkSpeed = 4.5f;
        public float sprintSpeed = 8f;
        public float jumpHeight = 1.2f;
        public float gravity = -20f;
        public float turnSpeed = 720f;

        [Header("Śmierć / respawn")]
        public float respawnDelay = 4f;
        [Tooltip("Zdrowie odnawia się samo do tej wartości po chwili bez obrażeń")]
        public float regenUpTo = 50f;
        public float regenDelay = 8f;
        public float regenPerSecond = 3f;
        public int deathMoneyPenalty = 100;

        [Header("Opcjonalnie: animacje (parametr float 'Speed', bool 'Dead')")]
        public Animator animator;

        public CharacterController Controller { get; private set; }
        public Health Health { get; private set; }
        public PlayerStats Stats { get; private set; }
        public WantedSystem Wanted { get; private set; }
        public VehicleInteractor Vehicles { get; private set; }
        public WeaponController Weapons { get; private set; }
        public PlayerMission Mission { get; private set; }

        public bool InVehicle => Vehicles != null && Vehicles.CurrentVehicle != null;
        public bool IsDead => Health != null && Health.Dead;

        float _verticalVelocity;
        Renderer[] _renderers;
        Collider[] _colliders;

        void Awake()
        {
            CharacterAppearance.Ensure(gameObject, true); // wygląd bohatera z menu postaci
            Controller = GetComponent<CharacterController>();
            Health = GetComponent<Health>();
            Stats = GetComponent<PlayerStats>();
            Wanted = GetComponent<WantedSystem>();
            Vehicles = GetComponent<VehicleInteractor>();
            Weapons = GetComponent<WeaponController>();
            Mission = GetComponent<PlayerMission>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            Health.OnDeadChanged += OnDeadChanged;

            // CharacterController działa tylko u właściciela – u innych pozycję ustawia NetworkTransform.
            Controller.enabled = IsOwner;

            if (IsOwner)
            {
                Local = this;
                NameTag.Add(gameObject, null, true);
                StartCoroutine(InitialTeleport());
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            if (IsServer) Health.OnDiedServer += OnDiedServer;
        }

        IEnumerator InitialTeleport()
        {
            yield return null; // poczekaj aż NetworkTransform się zainicjalizuje
            Teleport(SpawnPoint.GetRandom(), Quaternion.identity);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (Local == this) Local = null;
            Health.OnDeadChanged -= OnDeadChanged;
            if (IsServer) Health.OnDiedServer -= OnDiedServer;
        }

        bool _hiddenInCar;

        void LateUpdate()
        {
            // postać znika w aucie także wtedy, gdy jej części (przebieralnia F2) powstały już po wsiadnięciu
            bool inCar = InVehicle;
            if (inCar != _hiddenInCar)
            {
                _hiddenInCar = inCar;
                SetVisible(!inCar);
            }
        }

        void Update()
        {
            if (IsServer && IsSpawned && !IsDead && Health.Current.Value < regenUpTo && Time.time - Health.LastDamageTime > regenDelay)
                Health.Heal(Mathf.Min(regenPerSecond * Time.deltaTime, regenUpTo - Health.Current.Value));

            if (!IsOwner) return;
            GameInput.UpdateCursor();
            if (IsDead || InVehicle) return;
            if (!Controller.enabled) Controller.enabled = true; // po śmierci/wysiadaniu kontroler potrafił zostać wyłączony

            Vector2 input = GameInput.Move;
            var cam = Camera.main != null ? Camera.main.transform : transform;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            Vector3 move = Vector3.ClampMagnitude(fwd * input.y + right * input.x, 1f);

            bool aiming = GameInput.Aim || GameInput.FireHeld;
            float speed = (GameInput.Sprint && !aiming) ? sprintSpeed : walkSpeed;

            // obrót postaci: przy celowaniu patrzy tam gdzie kamera, inaczej w kierunku ruchu
            Vector3 lookDir = aiming ? fwd : move;
            if (lookDir.sqrMagnitude > 0.01f)
            {
                var target = Quaternion.LookRotation(lookDir);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
            }

            if (Controller.isGrounded)
            {
                _verticalVelocity = -2f;
                if (GameInput.Jump) _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 velocity = move * speed + Vector3.up * _verticalVelocity;
            Controller.Move(velocity * Time.deltaTime);

            if (animator != null) animator.SetFloat("Speed", move.magnitude * speed);

            // spadł pod mapę
            if (transform.position.y < -50f) Teleport(SpawnPoint.GetRandom(), Quaternion.identity);
        }

        /// <summary>Teleport właściciela (bez interpolacji przez sieć).</summary>
        public void Teleport(Vector3 pos, Quaternion rot)
        {
            if (!IsOwner) return;
            bool wasEnabled = Controller.enabled;
            Controller.enabled = false;
            var nt = GetComponent<NetworkTransform>();
            if (nt != null && nt.CanCommitToTransform) nt.Teleport(pos, rot, transform.localScale);
            else transform.SetPositionAndRotation(pos, rot);
            Controller.enabled = wasEnabled;
        }

        /// <summary>Ukrywa/pokazuje postać (np. gdy siedzi w aucie).</summary>
        public void SetVisible(bool visible)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r != null && !(r is ParticleSystemRenderer)) r.enabled = visible;
            foreach (var c in _colliders) if (c != null && !(c is CharacterController)) c.enabled = visible;
            if (IsOwner) Controller.enabled = visible && !IsDead;
            else Controller.enabled = false;
        }

        // ---------- śmierć ----------

        void OnDeadChanged(bool dead)
        {
            if (animator != null) animator.SetBool("Dead", dead);
            var look = GetComponent<CharacterAppearance>();
            if (look != null && look.HasModel) { look.SetLying(dead); return; } // model kładzie się sam, korzeń zostaje pionowo
            if (IsOwner)
            {
                // prosta "animacja" śmierci: położenie kapsuły
                transform.rotation = dead
                    ? Quaternion.Euler(-90f, transform.eulerAngles.y, 0f)
                    : Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            }
        }

        void OnDiedServer(ulong killer)
        {
            if (Vehicles != null) Vehicles.ForceExitServer();
            if (Wanted != null) Wanted.ClearWanted();
            if (Stats != null) Stats.AddMoney(-deathMoneyPenalty);
            if (killer != GameUtil.NoClient && killer != OwnerClientId)
            {
                var k = GameUtil.GetPlayer(killer);
                if (k != null) k.Stats.AddMoney(250);
            }
            ShowWastedRpc();
            StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(respawnDelay);
            Health.ResetHealth();
            if (Weapons != null) Weapons.RefillAmmoServer();
            // budzisz się w łóżku w kryjówce (a bez niej – w losowym punkcie odrodzenia)
            if (SpawnPoint.GetBed(out var bedPos, out var bedRot)) RespawnRpc(bedPos, bedRot, true);
            else RespawnRpc(SpawnPoint.GetRandom(), Quaternion.identity, false);
        }

        [Rpc(SendTo.Owner)]
        void RespawnRpc(Vector3 pos, Quaternion rot, bool inBed)
        {
            Teleport(pos, rot);
            if (inBed) HUD.ShowBigMessage("Obudziłeś się w kryjówce", new Color(1f, 0.9f, 0.7f), 2.5f);
        }

        [Rpc(SendTo.Owner)]
        void ShowWastedRpc()
        {
            HUD.ShowBigMessage("WASTED", new Color(0.8f, 0.1f, 0.1f), respawnDelay);
        }
    }
}
