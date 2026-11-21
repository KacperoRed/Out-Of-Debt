using System;
using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>Rozmieszcza znajdźki w stałych miejscach i odnawia je po czasie (tylko serwer).</summary>
    public class PickupSpawner : MonoBehaviour
    {
        [Serializable]
        public class Spot
        {
            public Transform point;
            public GameObject prefab;
            public float respawnTime = 30f;
            [NonSerialized] public NetworkObject current;
            [NonSerialized] public float nextSpawn;
            [NonSerialized] public bool wasAlive;
        }

        public Spot[] spots;

        void Update()
        {
            if (!GameUtil.IsServerRunning || spots == null) return;
            foreach (var s in spots)
            {
                if (s.point == null || s.prefab == null) continue;
                if (s.current != null && s.current.IsSpawned) continue;
                if (s.wasAlive) { s.wasAlive = false; s.nextSpawn = Time.time + s.respawnTime; } // ktoś podniósł
                if (Time.time < s.nextSpawn) continue;
                s.current = GameUtil.SpawnNetworked(s.prefab, s.point.position, Quaternion.identity);
                s.wasAlive = true;
            }
        }
    }
}
