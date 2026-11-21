using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Trasa dla ruchu ulicznego: dzieci tego obiektu = kolejne punkty.
    /// Dodaj nowy pusty obiekt jako dziecko, żeby dodać punkt. Trasa jest zapętlona.
    /// </summary>
    public class WaypointPath : MonoBehaviour
    {
        public static readonly List<WaypointPath> All = new List<WaypointPath>();
        public bool loop = true;

        public int Count => transform.childCount;
        public Vector3 GetPoint(int i) => transform.GetChild(Mathf.Clamp(i, 0, Count - 1)).position;
        public int Next(int i) => loop ? (i + 1) % Count : Mathf.Min(i + 1, Count - 1);

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < Count; i++)
            {
                Gizmos.DrawSphere(GetPoint(i), 0.6f);
                if (i + 1 < Count || loop) Gizmos.DrawLine(GetPoint(i), GetPoint((i + 1) % Count));
            }
        }
    }

    /// <summary>Zdarzenia pojazdów, na które mogą reagować inne systemy (serwer).</summary>
    public static class VehicleEvents
    {
        /// <summary>Gracz wyrzucił NPC-kierowcę z auta.</summary>
        public static event Action<Vehicle, PlayerController> Carjacked;
        public static void RaiseCarjacked(Vehicle v, PlayerController p) => Carjacked?.Invoke(v, p);
    }
}
