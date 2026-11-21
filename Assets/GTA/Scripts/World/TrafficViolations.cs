using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Wykroczenia drogowe gracza (tylko serwer): przejazd na czerwonym świetle daje gwiazdkę.
    /// Liczy się raz na każde przecięcie linii stopu – dopóki gracz nie odjedzie od skrzyżowania.
    /// </summary>
    public class TrafficViolations : MonoBehaviour
    {
        [Tooltip("Ile gwiazdek za przejazd na czerwonym")]
        public int starsForRedLight = 1;
        [Tooltip("Maksymalna liczba gwiazdek, jaką da się dostać za przejazd na czerwonym")]
        public int maxStarsForRedLight = 2;
        [Tooltip("Zasięg, w którym pilnujemy świateł wokół auta (m)")]
        public float watchRadius = 16f;
        [Tooltip("Linia zatrzymania od środka skrzyżowania (m) – przekroczenie jej na czerwonym to wykroczenie")]
        public float stopLineDistance = 11f;

        // auto gracza → skrzyżowanie, które już ukarało (żeby nie liczyć dwa razy)
        readonly Dictionary<Vehicle, TrafficSignal> _flagged = new Dictionary<Vehicle, TrafficSignal>();

        void Update()
        {
            if (!GameUtil.IsServerRunning) return;

            foreach (var v in Vehicle.All)
            {
                if (v == null) continue;
                if (!v.HasPlayerDriver || v.Health.Dead) { _flagged.Remove(v); continue; }

                Vector3 vel = v.ApproxVelocity; vel.y = 0;
                if (vel.sqrMagnitude < 4f) continue; // stoi albo wolno toczy się – nie wykroczenie
                Vector3 dir = vel.normalized;

                var s = TrafficSignal.Nearest(v.transform.position, watchRadius);
                if (s == null) { _flagged.Remove(v); continue; }

                Vector3 toS = s.transform.position - v.transform.position; toS.y = 0;
                float dist = toS.magnitude;
                bool approaching = Vector3.Dot(toS, dir) > 0f;
                if (!approaching) { _flagged.Remove(v); continue; } // odjechał – kolejny przejazd liczy się osobno

                if (dist < stopLineDistance && s.StateFor(dir) == TrafficSignal.LightState.Red)
                {
                    if (_flagged.TryGetValue(v, out var last) && last == s) continue;
                    _flagged[v] = s;
                    // za samo światło dostajesz najwyżej 2 gwiazdki (wyższy poziom pochodzi już z innych przestępstw)
                    var pl = GameUtil.GetPlayer(v.DriverClientId.Value);
                    int have = pl != null && pl.Wanted != null ? pl.Wanted.Level : 0;
                    int add = Mathf.Min(starsForRedLight, maxStarsForRedLight - have);
                    if (add > 0) WantedSystem.ReportCrime(v.DriverClientId.Value, add);
                }
            }
        }
    }
}
