using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Sieć dróg dla ruchu ulicznego: węzły (skrzyżowania / zakręty) i krawędzie (odcinki ulic, dwukierunkowe).
    /// Auta AI jeżdżą prawym pasem i na każdym skrzyżowaniu losują, gdzie skręcić.
    /// Generuje ją GTASceneBuilder; dla własnej mapy wypełnij tablice ręcznie (gizmo pokazuje sieć w Scene View).
    /// </summary>
    public class RoadNetwork : MonoBehaviour
    {
        public static RoadNetwork Instance { get; private set; }

        public Vector3[] nodes = new Vector3[0];
        public int[] edgeA = new int[0];
        public int[] edgeB = new int[0];
        [Tooltip("Ograniczenie prędkości na odcinku (m/s). Puste = defaultSpeed.")]
        public float[] edgeSpeed = new float[0];
        public float defaultSpeed = 12f;
        [Tooltip("Odległość środka pasa od osi jezdni")]
        public float laneOffset = 3f;
        [Tooltip("Gdzie auta zatrzymują się przed skrzyżowaniem (od środka węzła)")]
        public float stopDistance = 9f;
        [Tooltip("Promień skrzyżowania – jeśli jest w nim inne auto, czekamy")]
        public float intersectionRadius = 7.5f;
        [Tooltip("Linia zatrzymania przed sygnalizacją świetlną (od środka węzła) – za przejściem dla pieszych")]
        public float signalStopDistance = 13.5f;

        List<int>[] _adj;
        Dictionary<long, float> _speed;

        void OnEnable()
        {
            Instance = this;
            Build();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        public void Build()
        {
            _adj = new List<int>[nodes.Length];
            for (int i = 0; i < nodes.Length; i++) _adj[i] = new List<int>();
            _speed = new Dictionary<long, float>();
            for (int e = 0; e < edgeA.Length && e < edgeB.Length; e++)
            {
                int a = edgeA[e], b = edgeB[e];
                if (a < 0 || b < 0 || a >= nodes.Length || b >= nodes.Length) continue;
                _adj[a].Add(b);
                _adj[b].Add(a);
                float s = e < edgeSpeed.Length && edgeSpeed[e] > 0 ? edgeSpeed[e] : defaultSpeed;
                _speed[Key(a, b)] = s;
                _speed[Key(b, a)] = s;
            }
        }

        static long Key(int a, int b) => ((long)a << 32) | (uint)b;

        public int NodeCount => nodes.Length;
        public List<int> Neighbors(int n) => _adj[n];
        public bool IsIntersection(int n) => _adj[n].Count >= 3;
        public float SpeedLimit(int a, int b) => _speed != null && _speed.TryGetValue(Key(a, b), out var s) ? s : defaultSpeed;

        /// <summary>Wybierz następny węzeł po dojechaniu do "at" (bez zawracania, jeśli się da).</summary>
        public int PickNext(int from, int at)
        {
            var n = _adj[at];
            if (n.Count == 0) return from;
            if (n.Count == 1) return n[0];
            int pick;
            do pick = n[Random.Range(0, n.Count)]; while (pick == from);
            return pick;
        }

        public static Vector3 RightOf(Vector3 dir) => Vector3.Cross(Vector3.up, dir).normalized;

        /// <summary>Punkt na prawym pasie odcinka a→b, "distFromB" metrów przed węzłem b.</summary>
        public Vector3 StopPoint(int a, int b)
        {
            Vector3 d = (nodes[b] - nodes[a]); d.y = 0; d.Normalize();
            float len = Vector3.Distance(nodes[a], nodes[b]);
            float dist = Mathf.Min(stopDistance, len * 0.45f);
            return nodes[b] - d * dist + RightOf(d) * laneOffset;
        }

        /// <summary>Miejsce oczekiwania na zielone (prawy pas odcinka a→b, przed pasami).</summary>
        public Vector3 SignalStopPoint(int a, int b)
        {
            Vector3 d = (nodes[b] - nodes[a]); d.y = 0; d.Normalize();
            return nodes[b] - d * signalStopDistance + RightOf(d) * laneOffset;
        }

        /// <summary>Punkt wyjazdu ze skrzyżowania b w stronę c (prawy pas).</summary>
        public Vector3 ExitPoint(int b, int c)
        {
            Vector3 d = (nodes[c] - nodes[b]); d.y = 0; d.Normalize();
            float len = Vector3.Distance(nodes[b], nodes[c]);
            float dist = Mathf.Min(stopDistance, len * 0.45f);
            return nodes[b] + d * dist + RightOf(d) * laneOffset;
        }

        /// <summary>Losowe miejsce na prawym pasie losowej ulicy (do spawnu aut).</summary>
        public bool RandomLanePosition(out int from, out int to, out Vector3 pos, out Quaternion rot)
        {
            from = to = 0; pos = Vector3.zero; rot = Quaternion.identity;
            if (edgeA.Length == 0) return false;
            int e = Random.Range(0, edgeA.Length);
            bool flip = Random.value > 0.5f;
            from = flip ? edgeB[e] : edgeA[e];
            to = flip ? edgeA[e] : edgeB[e];
            Vector3 d = nodes[to] - nodes[from]; d.y = 0;
            float len = d.magnitude;
            d.Normalize();
            pos = nodes[from] + d * (len * Random.Range(0.3f, 0.7f)) + RightOf(d) * laneOffset;
            rot = Quaternion.LookRotation(d);
            return true;
        }

        public int NearestNode(Vector3 p)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < nodes.Length; i++)
            {
                float d = (nodes[i] - p).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            for (int e = 0; e < edgeA.Length && e < edgeB.Length; e++)
                if (edgeA[e] < nodes.Length && edgeB[e] < nodes.Length)
                    Gizmos.DrawLine(nodes[edgeA[e]] + Vector3.up, nodes[edgeB[e]] + Vector3.up);
            Gizmos.color = Color.yellow;
            foreach (var n in nodes) Gizmos.DrawWireSphere(n + Vector3.up, 1.5f);
        }
    }
}
