using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Nocne światła w oknach budynków. Paczka Cartoon City ma warianty „…_NightLight” z podświetlonymi oknami.
    /// Generator zapisuje tu pary materiałów (dzień / noc) dla każdego renderera budynku, a w nocy (DayNightCycle.IsNight)
    /// podmieniamy same materiały. Geometria się nie zmienia, więc budynek nigdy nie znika – w najgorszym razie zostaje taki sam.
    /// </summary>
    public class NightVariant : MonoBehaviour
    {
        [Serializable]
        public class Slot
        {
            public Renderer renderer;
            public Material[] day;
            public Material[] night;
        }

        public List<Slot> slots = new List<Slot>();

        bool _isNight;
        bool _applied;

        void Start()
        {
            InvokeRepeating(nameof(Check), UnityEngine.Random.Range(0f, 2f), 2f);
        }

        void Check()
        {
            bool n = DayNightCycle.Instance != null && DayNightCycle.Instance.IsNight;
            if (_applied && n == _isNight) return;
            _applied = true;
            _isNight = n;
            foreach (var s in slots)
            {
                if (s == null || s.renderer == null) continue;
                var mats = n ? s.night : s.day;
                if (mats != null && mats.Length > 0) s.renderer.sharedMaterials = mats;
            }
        }
    }
}
