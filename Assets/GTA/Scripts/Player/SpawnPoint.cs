using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>Punkt odrodzenia gracza ("szpital"). Postaw kilka pustych obiektów z tym skryptem na mapie.</summary>
    public class SpawnPoint : MonoBehaviour
    {
        static readonly List<SpawnPoint> Points = new List<SpawnPoint>();

        void OnEnable() => Points.Add(this);
        void OnDisable() => Points.Remove(this);

        [Tooltip("Łóżko w kryjówce: tu odradza się gracz po śmierci")]
        public bool bed;

        /// <summary>Łóżko w kryjówce (jeśli jest) – miejsce i obrót do odrodzenia.</summary>
        public static bool GetBed(out Vector3 pos, out Quaternion rot)
        {
            foreach (var p in Points)
                if (p != null && p.bed) { pos = p.transform.position; rot = p.transform.rotation; return true; }
            pos = default; rot = Quaternion.identity;
            return false;
        }

        public static Vector3 GetRandom()
        {
            if (Points.Count == 0) return Vector3.up * 2f;
            var p = Points[Random.Range(0, Points.Count)].transform.position;
            Vector2 jitter = Random.insideUnitCircle * 2f;
            return p + new Vector3(jitter.x, 0.5f, jitter.y);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, 1f);
        }
    }
}
