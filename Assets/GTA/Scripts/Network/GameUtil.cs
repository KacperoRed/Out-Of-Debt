using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace MiniGTA
{
    /// <summary>Wspólne funkcje pomocnicze (głównie po stronie serwera).</summary>
    public static class GameUtil
    {
        public const ulong NoClient = ulong.MaxValue;
        /// <summary>Obszar NavMesh "jezdnia" (oznaczony przez NavMeshModifierVolume na ulicach). Piesi go unikają.</summary>
        public const int RoadArea = 3;
        /// <summary>Tylko obszar Walkable (chodniki, budynki) – bez jezdni.</summary>
        public const int SidewalkMask = 1 << 0;
        /// <summary>Obszar NavMesh "przejście dla pieszych" (zebry) – tani dla pieszych, więc przechodzą po pasach.</summary>
        public const int CrosswalkArea = 4;
        /// <summary>Obszar NavMesh "wnętrze kwartału" (trawniki, place) – piesi wolą chodniki.</summary>
        public const int LotArea = 5;

        public static bool IsServerRunning =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer;

        /// <summary>Zwraca obiekt gracza danego klienta (tylko na serwerze).</summary>
        public static PlayerController GetPlayer(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return null;
            if (!nm.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null) return null;
            return client.PlayerObject.GetComponent<PlayerController>();
        }

        /// <summary>Wszyscy gracze (działa na serwerze i na klientach).</summary>
        public static IReadOnlyList<PlayerController> AllPlayers => PlayerController.All;

        public static NetworkObject FindSpawned(ulong networkObjectId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.SpawnManager == null) return null;
            nm.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var obj);
            return obj;
        }

        /// <summary>Losowy punkt na NavMeshu w pierścieniu [minDist, maxDist] od środka.</summary>
        public static bool RandomNavPoint(Vector3 center, float minDist, float maxDist, out Vector3 result) =>
            RandomNavPoint(center, minDist, maxDist, NavMesh.AllAreas, out result);

        public static bool RandomNavPoint(Vector3 center, float minDist, float maxDist, int areaMask, out Vector3 result)
        {
            for (int i = 0; i < 20; i++)
            {
                Vector2 dir = Random.insideUnitCircle.normalized * Random.Range(minDist, maxDist);
                Vector3 p = center + new Vector3(dir.x, 0f, dir.y);
                p.y = center.y;
                if (NavMesh.SamplePosition(p, out var hit, 4f, areaMask) && hit.position.y < center.y + 3f)
                {
                    result = hit.position;
                    return true;
                }
            }
            result = center;
            return false;
        }

        public static float DistanceToNearestPlayer(Vector3 pos)
        {
            float best = float.MaxValue;
            foreach (var p in PlayerController.All)
                best = Mathf.Min(best, Vector3.Distance(p.transform.position, pos));
            return best;
        }

        public static bool HasLineOfSight(Vector3 from, Vector3 to, Transform ignoreA = null, Transform ignoreB = null)
        {
            Vector3 dir = to - from;
            var hits = Physics.RaycastAll(from, dir.normalized, dir.magnitude, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (ignoreA != null && h.transform.IsChildOf(ignoreA)) continue;
                if (ignoreB != null && h.transform.IsChildOf(ignoreB)) continue;
                if (h.collider.GetComponentInParent<Health>() != null) continue; // ludzie/auta nie zasłaniają
                return false;
            }
            return true;
        }

        public static NetworkObject SpawnNetworked(GameObject prefab, Vector3 pos, Quaternion rot)
        {
            var go = Object.Instantiate(prefab, pos, rot);
            var no = go.GetComponent<NetworkObject>();
            no.Spawn(true);
            return no;
        }
    }
}
