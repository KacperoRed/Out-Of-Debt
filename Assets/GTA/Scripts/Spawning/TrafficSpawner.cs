using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Ruch uliczny (auta jeżdżące po WaypointPath) + zaparkowane auta (tylko serwer).
    /// </summary>
    public class TrafficSpawner : MonoBehaviour
    {
        public GameObject[] carPrefabs;
        public int maxTrafficCars = 10;
        public float minSpawnDistance = 40f;
        public float maxSpawnDistance = 130f;
        public float despawnDistance = 160f;

        [Header("Zaparkowane auta (pojawiają się raz na starcie serwera)")]
        public Transform[] parkedSpots;
        [Tooltip("Miejsca z zaparkowanym radiowozem (kradzież = 2 gwiazdki)")]
        public Transform[] parkedPoliceSpots;
        public GameObject policeCarPrefab;

        [Header("Kierowca wyrzucony przy kradzieży auta")]
        public GameObject driverPedestrianPrefab;

        readonly List<Vehicle> _traffic = new List<Vehicle>();
        float _next;
        bool _parkedSpawned;

        void OnEnable() => VehicleEvents.Carjacked += OnCarjacked;
        void OnDisable() => VehicleEvents.Carjacked -= OnCarjacked;

        void Update()
        {
            if (!GameUtil.IsServerRunning) { _parkedSpawned = false; return; }

            if (!_parkedSpawned)
            {
                _parkedSpawned = true;
                if (parkedSpots != null && carPrefabs.Length > 0)
                    foreach (var spot in parkedSpots)
                        if (spot != null) GameUtil.SpawnNetworked(carPrefabs[Random.Range(0, carPrefabs.Length)], spot.position, spot.rotation);
                if (parkedPoliceSpots != null && policeCarPrefab != null)
                    foreach (var spot in parkedPoliceSpots)
                        if (spot != null) GameUtil.SpawnNetworked(policeCarPrefab, spot.position, spot.rotation);
            }

            if (Time.time < _next || PlayerController.All.Count == 0) return;
            _next = Time.time + 1.5f;

            _traffic.RemoveAll(v => v == null || !v.IsSpawned);

            for (int i = _traffic.Count - 1; i >= 0; i--)
            {
                var v = _traffic[i];
                // auto ukradzione przez gracza przestaje być "ruchem ulicznym"
                if (v.HasPlayerDriver) { _traffic.RemoveAt(i); continue; }
                if (GameUtil.DistanceToNearestPlayer(v.transform.position) > despawnDistance)
                {
                    v.NetworkObject.Despawn(true);
                    _traffic.RemoveAt(i);
                }
            }

            if (_traffic.Count >= maxTrafficCars || carPrefabs.Length == 0) return;

            // nowa sieć dróg (cała siatka ulic) – preferowana
            var net = RoadNetwork.Instance;
            if (net != null && net.NodeCount > 1)
            {
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    if (!net.RandomLanePosition(out int from, out int to, out var lanePos, out var laneRot)) return;
                    float dist = GameUtil.DistanceToNearestPlayer(lanePos);
                    if (dist < minSpawnDistance || dist > maxSpawnDistance) continue;
                    if (SpotTaken(lanePos)) continue;
                    var roamer = GameUtil.SpawnNetworked(carPrefabs[Random.Range(0, carPrefabs.Length)], lanePos + Vector3.up * 0.5f, laneRot).GetComponent<Vehicle>();
                    roamer.StartRoam(from, to);
                    _traffic.Add(roamer);
                    return;
                }
                return;
            }

            if (WaypointPath.All.Count == 0) return;

            var path = WaypointPath.All[Random.Range(0, WaypointPath.All.Count)];
            if (path.Count < 2) return;
            int idx = Random.Range(0, path.Count);
            Vector3 pos = path.GetPoint(idx);
            float d = GameUtil.DistanceToNearestPlayer(pos);
            if (d < minSpawnDistance || d > maxSpawnDistance) return;
            if (SpotTaken(pos)) return;

            Vector3 next = path.GetPoint(path.Next(idx));
            var rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(next - pos, Vector3.up));
            var prefab = carPrefabs[Random.Range(0, carPrefabs.Length)];
            var car = GameUtil.SpawnNetworked(prefab, pos + Vector3.up * 0.5f, rot).GetComponent<Vehicle>();
            car.StartTraffic(path, idx);
            _traffic.Add(car);
        }

        static bool SpotTaken(Vector3 pos)
        {
            foreach (var v in Vehicle.All)
                if (v != null && (v.transform.position - pos).sqrMagnitude < 10f * 10f) return true;
            return false;
        }

        void OnCarjacked(Vehicle v, PlayerController thief)
        {
            if (!GameUtil.IsServerRunning || driverPedestrianPrefab == null) return;
            Vector3 pos = v.transform.position + v.transform.right * 2.5f;
            if (UnityEngine.AI.NavMesh.SamplePosition(pos, out var hit, 5f, UnityEngine.AI.NavMesh.AllAreas)) pos = hit.position;
            var ped = GameUtil.SpawnNetworked(driverPedestrianPrefab, pos, Quaternion.identity).GetComponent<PedestrianAI>();
            if (ped != null) ped.Flee(thief.transform.position);
        }
    }
}
