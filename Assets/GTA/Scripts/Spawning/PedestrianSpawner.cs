using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Utrzymuje populację przechodniów wokół graczy (tylko serwer).
    /// Dodaj więcej prefabów do listy, żeby mieć różnych ludzi.
    /// </summary>
    public class PedestrianSpawner : MonoBehaviour
    {
        public GameObject[] pedestrianPrefabs;
        public int maxPedestrians = 25;
        public float minSpawnDistance = 25f;
        public float maxSpawnDistance = 70f;
        public float despawnDistance = 110f;
        public float interval = 1f;

        readonly List<NetworkObject> _spawned = new List<NetworkObject>();
        float _next;

        void Update()
        {
            if (!GameUtil.IsServerRunning || Time.time < _next || PlayerController.All.Count == 0) return;
            _next = Time.time + interval;

            _spawned.RemoveAll(n => n == null || !n.IsSpawned);

            // usuń dalekich
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (GameUtil.DistanceToNearestPlayer(_spawned[i].transform.position) > despawnDistance)
                {
                    _spawned[i].Despawn(true);
                    _spawned.RemoveAt(i);
                }
            }

            // dospawnuj (max 2 na tick, żeby nie było skoków)
            for (int k = 0; k < 2 && _spawned.Count < maxPedestrians && pedestrianPrefabs.Length > 0; k++)
            {
                var player = PlayerController.All[Random.Range(0, PlayerController.All.Count)];
                if (!GameUtil.RandomNavPoint(player.transform.position, minSpawnDistance, maxSpawnDistance, out var pos)) continue;
                if (GameUtil.DistanceToNearestPlayer(pos) < minSpawnDistance) continue;
                var prefab = pedestrianPrefabs[Random.Range(0, pedestrianPrefabs.Length)];
                _spawned.Add(GameUtil.SpawnNetworked(prefab, pos, Quaternion.Euler(0, Random.Range(0, 360), 0)));
            }
        }
    }
}
