using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Sygnalizacja świetlna na skrzyżowaniu. Dwie fazy: ruch wzdłuż osi X i wzdłuż osi Z
    /// (zielone → żółte → wszystkie czerwone → druga oś...). Faza liczona z czasu serwera,
    /// więc u wszystkich graczy światła zmieniają się jednocześnie – bez żadnej synchronizacji sieciowej.
    /// Auta AI (Vehicle.UpdateRoam) stają na czerwonym, piesi (PedestrianAI) przechodzą po pasach,
    /// gdy auta na przecinanej ulicy mają czerwone.
    /// </summary>
    public class TrafficSignal : MonoBehaviour
    {
        public enum LightState { Green, Yellow, Red }

        static readonly Dictionary<Vector2Int, TrafficSignal> ByCell = new Dictionary<Vector2Int, TrafficSignal>();
        static readonly List<TrafficSignal> All = new List<TrafficSignal>();

        [Header("Czasy (s)")]
        public float greenTime = 11f;
        public float yellowTime = 3f;
        public float allRedTime = 2f;
        [Tooltip("Przesunięcie fazy – sąsiednie skrzyżowania nie zmieniają się naraz")]
        public float offset;

        [Header("Lampy (ustawia GTASceneBuilder)")]
        public Renderer[] xRed, xYellow, xGreen;   // głowice dla aut jadących wzdłuż X
        public Renderer[] zRed, zYellow, zGreen;   // głowice dla aut jadących wzdłuż Z
        public Material redOn, yellowOn, greenOn, lampOff;

        int _shownX = -1, _shownZ = -1;

        float Cycle => 2f * (greenTime + yellowTime + allRedTime);

        static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.RoundToInt(p.x / 4f), Mathf.RoundToInt(p.z / 4f));

        void OnEnable()
        {
            All.Add(this);
            ByCell[Cell(transform.position)] = this;
        }

        void OnDisable()
        {
            All.Remove(this);
            var c = Cell(transform.position);
            if (ByCell.TryGetValue(c, out var s) && s == this) ByCell.Remove(c);
        }

        /// <summary>Sygnalizator na skrzyżowaniu w punkcie (węzeł RoadNetwork) albo null.</summary>
        public static TrafficSignal At(Vector3 nodePos)
        {
            ByCell.TryGetValue(Cell(nodePos), out var s);
            return s;
        }

        /// <summary>Najbliższy sygnalizator w promieniu (dla pieszych).</summary>
        public static TrafficSignal Nearest(Vector3 p, float maxDist)
        {
            TrafficSignal best = null;
            float bd = maxDist * maxDist;
            foreach (var s in All)
            {
                Vector3 d = s.transform.position - p; d.y = 0;
                if (d.sqrMagnitude < bd) { bd = d.sqrMagnitude; best = s; }
            }
            return best;
        }

        static double Now()
        {
            var nm = NetworkManager.Singleton;
            return nm != null && nm.IsListening ? nm.ServerTime.Time : Time.timeAsDouble;
        }

        /// <summary>Światło dla aut jadących wzdłuż osi X (alongX=true) lub Z.</summary>
        public LightState State(bool alongX)
        {
            float t = (float)((Now() + offset) % Cycle);
            float half = greenTime + yellowTime + allRedTime;
            if (!alongX) t = (t + half) % Cycle; // oś Z ma przesuniętą fazę o pół cyklu
            if (t < greenTime) return LightState.Green;
            if (t < greenTime + yellowTime) return LightState.Yellow;
            return LightState.Red;
        }

        /// <summary>Światło dla auta jadącego w kierunku dir.</summary>
        public LightState StateFor(Vector3 dir) => State(Mathf.Abs(dir.x) >= Mathf.Abs(dir.z));

        /// <summary>Czy pieszy może wejść na pasy przecinające ulicę z ruchem wzdłuż X (crossingXTraffic) lub Z.</summary>
        public bool PedestrianCanCross(bool crossingXTraffic) => State(crossingXTraffic) == LightState.Red;

        void Update()
        {
            int x = (int)State(true), z = (int)State(false);
            if (x != _shownX) { _shownX = x; Show(xRed, xYellow, xGreen, (LightState)x); }
            if (z != _shownZ) { _shownZ = z; Show(zRed, zYellow, zGreen, (LightState)z); }
        }

        void Show(Renderer[] red, Renderer[] yellow, Renderer[] green, LightState s)
        {
            Set(red, s == LightState.Red ? redOn : lampOff);
            Set(yellow, s == LightState.Yellow ? yellowOn : lampOff);
            Set(green, s == LightState.Green ? greenOn : lampOff);
        }

        static void Set(Renderer[] rs, Material m)
        {
            if (rs == null || m == null) return;
            foreach (var r in rs) if (r != null) r.sharedMaterial = m;
        }
    }
}
