using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Wsiadanie / wysiadanie z pojazdów (klawisz F). Serwer decyduje, czy wolno wsiąść.
    /// </summary>
    public class VehicleInteractor : NetworkBehaviour
    {
        public float enterDistance = 4f;

        /// <summary>NetworkObjectId auta, w którym siedzi gracz (NoClient = pieszo).</summary>
        public readonly NetworkVariable<ulong> VehicleObjectId = new NetworkVariable<ulong>(GameUtil.NoClient);

        PlayerController _player;
        Vehicle _cached;
        ulong _cachedId = GameUtil.NoClient;

        public Vehicle CurrentVehicle
        {
            get
            {
                ulong id = VehicleObjectId.Value;
                if (id == GameUtil.NoClient) return null;
                if (id != _cachedId || _cached == null)
                {
                    var no = GameUtil.FindSpawned(id);
                    _cached = no != null ? no.GetComponent<Vehicle>() : null;
                    _cachedId = id;
                }
                return _cached;
            }
        }

        void Awake() => _player = GetComponent<PlayerController>();

        public override void OnNetworkSpawn()
        {
            VehicleObjectId.OnValueChanged += OnVehicleChanged;
            OnVehicleChanged(GameUtil.NoClient, VehicleObjectId.Value);
        }

        public override void OnNetworkDespawn()
        {
            VehicleObjectId.OnValueChanged -= OnVehicleChanged;
            if (IsServer) ForceExitServer();
        }

        void OnVehicleChanged(ulong oldV, ulong newV)
        {
            _player.SetVisible(newV == GameUtil.NoClient);
        }

        void Update()
        {
            if (!IsOwner || _player.IsDead) return;
            if (!GameInput.Interact) return;

            if (CurrentVehicle != null)
            {
                ExitRpc();
            }
            else
            {
                RequestEnter(FindNearestVehicle());
            }
        }

        /// <summary>Właściciel: poproś serwer o wejście do auta.</summary>
        public void RequestEnter(Vehicle v)
        {
            if (IsOwner && v != null) EnterRpc(new NetworkObjectReference(v.NetworkObject));
        }

        void LateUpdate()
        {
            // właściciel "przykleja" swoją postać do fotela – dzięki temu pozycja gracza też jest zsynchronizowana
            if (!IsOwner) return;
            var v = CurrentVehicle;
            if (v == null) return;
            var seat = v.seat != null ? v.seat : v.transform;
            transform.SetPositionAndRotation(seat.position, Quaternion.Euler(0f, seat.eulerAngles.y, 0f));
        }

        Vehicle FindNearestVehicle()
        {
            Vehicle best = null;
            float bestDist = enterDistance;
            foreach (var c in Physics.OverlapSphere(transform.position, enterDistance))
            {
                var v = c.GetComponentInParent<Vehicle>();
                if (v == null || !v.CanEnter) continue;
                float d = Vector3.Distance(transform.position, c.ClosestPoint(transform.position));
                if (d < bestDist) { bestDist = d; best = v; }
            }
            return best;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void EnterRpc(NetworkObjectReference vehicleRef)
        {
            if (_player.IsDead || VehicleObjectId.Value != GameUtil.NoClient) return;
            if (!vehicleRef.TryGet(out var no)) return;
            var v = no.GetComponent<Vehicle>();
            if (v == null) return;
            if (Vector3.Distance(v.transform.position, transform.position) > enterDistance + 4f) return; // anty-cheat

            if (v.SetDriverServer(_player))
                VehicleObjectId.Value = no.NetworkObjectId;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ExitRpc() => ForceExitServer();

        /// <summary>Serwer: wyrzuć gracza z auta (wysiadanie, śmierć, wybuch, rozłączenie).</summary>
        public void ForceExitServer()
        {
            if (!IsServer) return;
            var v = CurrentVehicle;
            if (VehicleObjectId.Value == GameUtil.NoClient) return;

            Vector3 exitPos = transform.position + Vector3.up;
            if (v != null)
            {
                exitPos = v.exitPoint != null ? v.exitPoint.position : v.transform.position - v.transform.right * 2f + Vector3.up;
                v.ClearDriverServer();
            }
            VehicleObjectId.Value = GameUtil.NoClient;
            if (IsSpawned) PlaceAtRpc(exitPos);
        }

        [Rpc(SendTo.Owner)]
        void PlaceAtRpc(Vector3 pos)
        {
            _player.Teleport(pos, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        }
    }
}
