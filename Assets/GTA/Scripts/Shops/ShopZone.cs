using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    public enum ShopType
    {
        Weapons,    // Ammu-Nation: bronie, amunicja, granaty, apteczka, kamizelka
        Repair,     // "Pay'n'Spray": wjedź autem – naprawa + zgubienie gwiazdek
        Store,      // sklep 24/7: jedzenie; można go obrabować (celuj w kasjera)
        Bar,        // bar: jedzenie i picie (leczy)
        Safehouse,  // kryjówka: zapis gry, odpoczynek (pełne HP)
    }

    /// <summary>
    /// Strefa sklepu / usługi na mapie (zwykły obiekt sceny, bez sieci). Postaw pusty obiekt z tym skryptem.
    /// Obsługę zakupów robi ShopClient na graczu (walidacja na serwerze).
    /// </summary>
    public class ShopZone : MonoBehaviour
    {
        public static readonly List<ShopZone> All = new List<ShopZone>();

        public ShopType type = ShopType.Weapons;
        public string shopName = "Ammu-Nation";
        public float radius = 3f;
        [Tooltip("Pokaż świecący krąg na podłodze")]
        public bool showMarker = true;

        [Header("Warsztat")]
        public int repairPrice = 500;
        [Tooltip("Czy warsztat kasuje gwiazdki (jak w GTA), o ile policja cię nie widzi")]
        public bool clearsWanted = true;

        [Header("Ceny")]
        public int medkitPrice = 150;
        public int armorPrice = 250;

        [Header("Napad (sklep 24/7)")]
        [Tooltip("Collider kasjera – w niego trzeba celować")]
        public Collider cashier;
        public float robTime = 3f;
        public int robMinCash = 300;
        public int robMaxCash = 900;
        public float robCooldown = 180f;
        [Tooltip("Ile gwiazdek dostajesz po napadzie")]
        public int robWanted = 2;
        [Tooltip("Kasyno: zamiast jedzenia sprzedaje przedmioty VIP")]
        public bool casino;
        [HideInInspector] public float lastRobbedTime = -9999f; // serwer

        /// <summary>Kolor kategorii (znacznik, minimapa, napis).</summary>
        public Color TypeColor => type switch
        {
            ShopType.Weapons => new Color(0.2f, 1f, 0.3f),
            ShopType.Repair => new Color(0.2f, 0.6f, 1f),
            ShopType.Store => new Color(1f, 0.6f, 0.1f),
            ShopType.Bar => new Color(0.9f, 0.3f, 0.9f),
            _ => new Color(1f, 1f, 1f),
        };

        public string Hint => type switch
        {
            ShopType.Weapons => "[E] zakupy",
            ShopType.Repair => "wjedź autem",
            ShopType.Store => "[E] zakupy · celuj w kasjera = napad",
            ShopType.Bar => casino ? "[E] sklep VIP · ruletka przy stole" : cashier != null ? "[E] menu · celuj w kasjera = napad" : "[E] menu",
            _ => "[E] zapis / odpoczynek",
        };

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            var color = TypeColor;
            if (showMarker)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.name = "ShopMarker";
                Destroy(marker.GetComponent<Collider>());
                marker.transform.SetParent(transform, false);
                marker.transform.localPosition = new Vector3(0f, 0.06f, 0f);
                marker.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);
                marker.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(color.r, color.g, color.b, 0.35f) };
            }

            // napis nad strefą (obraca się do kamery)
            var label = new GameObject("Label");
            label.transform.SetParent(transform, false);
            label.transform.localPosition = Vector3.up * 2.8f;
            var tm = label.AddComponent<TextMesh>();
            tm.text = shopName + "\n" + Hint;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.08f;
            tm.fontSize = 48;
            tm.color = color;
            _label = label.transform;
        }

        Transform _label;

        void LateUpdate()
        {
            var cam = Camera.main;
            if (_label != null && cam != null)
                _label.rotation = Quaternion.LookRotation(_label.position - cam.transform.position);
        }

        public bool Contains(Vector3 pos)
        {
            Vector3 d = pos - transform.position;
            if (Mathf.Abs(d.y) > 4f) return false;
            d.y = 0f;
            return d.magnitude <= radius;
        }

        /// <summary>Czy punkt jest wewnątrz któregoś budynku z wejściem (przybliżenie: blisko strefy usługi, na tej samej wysokości).</summary>
        public static bool Inside(Vector3 pos)
        {
            foreach (var z in All)
            {
                if (z == null) continue;
                Vector3 d = pos - z.transform.position;
                if (Mathf.Abs(d.y) < 4f && new Vector2(d.x, d.z).magnitude < 9f) return true;
            }
            return false;
        }

        public static ShopZone Find(Vector3 pos, ShopType type)
        {
            foreach (var z in All)
                if (z.type == type && z.Contains(pos)) return z;
            return null;
        }

        /// <summary>Strefa z kasjerem, którą można obrabować (sklep 24/7 albo kasyno).</summary>
        public static ShopZone FindRobbable(Vector3 pos)
        {
            foreach (var z in All)
                if (z != null && z.cashier != null && z.Contains(pos)) return z;
            return null;
        }

        /// <summary>Dowolna strefa z menu (wszystko poza warsztatem).</summary>
        public static ShopZone FindMenu(Vector3 pos)
        {
            foreach (var z in All)
                if (z.type != ShopType.Repair && z.Contains(pos)) return z;
            return null;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = TypeColor;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
