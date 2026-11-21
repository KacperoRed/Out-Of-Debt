using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>Imię nad głową postaci (bohater, przechodnie, sprzedawcy). Rysuje jeden wspólny rysownik, więc koszt jest mały.</summary>
    public class NameTag : MonoBehaviour
    {
        static readonly string[] Names =
        {
            "Marek", "Tomek", "Kuba", "Piotr", "Adam", "Darek", "Paweł", "Janek", "Stefan", "Leszek", "Bartek", "Wojtek",
            "Anna", "Kasia", "Ewa", "Magda", "Zosia", "Basia", "Iga", "Marta", "Ola", "Julia", "Hania", "Karolina",
        };

        public string label;
        public bool isHero;
        public float height = 2.15f;
        public float range = 22f;

        public static readonly List<NameTag> All = new List<NameTag>();
        static Drawer _drawer;

        public static NameTag Add(GameObject go, string label = null, bool hero = false)
        {
            var t = go.GetComponent<NameTag>();
            if (t == null) t = go.AddComponent<NameTag>();
            t.isHero = hero;
            t.label = label ?? (hero ? null : Names[Mathf.Abs(Mathf.RoundToInt(go.transform.position.x * 13.7f + go.transform.position.z * 7.1f + go.GetInstanceID())) % Names.Length]);
            return t;
        }

        void OnEnable()
        {
            All.Add(this);
            if (_drawer == null)
            {
                var d = new GameObject("[NameTags]");
                DontDestroyOnLoad(d);
                _drawer = d.AddComponent<Drawer>();
            }
        }

        void OnDisable() { All.Remove(this); }

        class Drawer : MonoBehaviour
        {
            GUIStyle _st;
            Camera _cam;

            void OnGUI()
            {
                if (Event.current.type != EventType.Repaint || All.Count == 0) return;
                if (DialogueUI.Cinematic) return;
                if (_cam == null) _cam = Camera.main;
                if (_cam == null) return;
                if (_st == null)
                {
                    _st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
                }
                Vector3 cp = _cam.transform.position;
                Vector3 cf = _cam.transform.forward;
                for (int i = 0; i < All.Count; i++)
                {
                    var t = All[i];
                    if (t == null) continue;
                    string text = t.isHero ? NetworkUI.LocalPlayerName : t.label;
                    if (string.IsNullOrEmpty(text)) continue;
                    var h = t.GetComponent<Health>();
                    if (h != null && h.IsSpawned && h.Dead) continue;
                    Vector3 wp = t.transform.position + Vector3.up * t.height;
                    Vector3 d = wp - cp;
                    float dist = d.magnitude;
                    if (dist > t.range || Vector3.Dot(d, cf) <= 0.5f) continue;
                    Vector3 sp = _cam.WorldToScreenPoint(wp);
                    float y = Screen.height - sp.y;
                    var r = new Rect(sp.x - 90f, y - 12f, 180f, 24f);
                    Color a = t.isHero ? new Color(1f, 0.85f, 0.3f) : Color.white;
                    a.a = Mathf.Clamp01((t.range - dist) / 6f);
                    _st.normal.textColor = new Color(0f, 0f, 0f, a.a);
                    GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, _st);
                    _st.normal.textColor = a;
                    GUI.Label(r, text, _st);
                }
            }
        }
    }
}
