using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Latarnia uliczna. W nocy świeci jej klosz (emisja), a prawdziwe światło (Light)
    /// dostają tylko najbliższe kamerze latarnie – DayNightCycle tym zarządza.
    /// </summary>
    public class StreetLamp : MonoBehaviour
    {
        static readonly List<StreetLamp> All = new List<StreetLamp>();
        static readonly List<StreetLamp> _lit = new List<StreetLamp>();

        public Light lampLight;
        public Renderer bulb;
        public Color onColor = new Color(1f, 0.85f, 0.55f);

        Material _mat;
        bool _on;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Awake()
        {
            if (bulb != null) _mat = bulb.material;
            if (lampLight != null) lampLight.enabled = false;
            SetOn(false);
        }

        void SetOn(bool on)
        {
            _on = on;
            if (_mat != null)
            {
                _mat.color = on ? onColor : new Color(0.6f, 0.6f, 0.55f);
                if (on) { _mat.EnableKeyword("_EMISSION"); _mat.SetColor("_EmissionColor", onColor * 2f); }
                else _mat.DisableKeyword("_EMISSION");
            }
        }

        public static void UpdateLights(Vector3 camPos, bool night, int maxLights, float range)
        {
            foreach (var l in _lit) if (l != null && l.lampLight != null) l.lampLight.enabled = false;
            _lit.Clear();

            foreach (var l in All) if (l._on != night) l.SetOn(night);
            if (!night) return;

            var sorted = new List<StreetLamp>(All);
            sorted.Sort((a, b) => (a.transform.position - camPos).sqrMagnitude.CompareTo((b.transform.position - camPos).sqrMagnitude));
            for (int i = 0; i < sorted.Count && i < maxLights; i++)
            {
                if ((sorted[i].transform.position - camPos).sqrMagnitude > range * range) break;
                if (sorted[i].lampLight == null) continue;
                sorted[i].lampLight.enabled = true;
                _lit.Add(sorted[i]);
            }
        }
    }
}
