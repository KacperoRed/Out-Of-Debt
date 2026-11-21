using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>Stół ruletki w kasynie (obiekt sceny). Gracz przy stole naciska E; sama gra jest w ShopClient (serwer losuje i płaci).</summary>
    public class RouletteTable : MonoBehaviour
    {
        public static readonly List<RouletteTable> All = new List<RouletteTable>();

        public Transform wheel;
        public float radius = 3.4f;
        /// <summary>Koło kręci się szybciej, gdy ktoś właśnie gra.</summary>
        public static float SpinBoostUntil;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            if (wheel != null) wheel.Rotate(0f, (Time.time < SpinBoostUntil ? 420f : 18f) * Time.deltaTime, 0f);
        }

        public static RouletteTable Near(Vector3 pos)
        {
            foreach (var t in All)
            {
                if (t == null) continue;
                Vector3 d = pos - t.transform.position;
                if (Mathf.Abs(d.y) < 3f && new Vector2(d.x, d.z).magnitude <= t.radius) return t;
            }
            return null;
        }
    }
}
