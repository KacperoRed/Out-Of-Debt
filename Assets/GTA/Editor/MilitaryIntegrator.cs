using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// MiniGTA → 8. Podłącz czołg i helikopter z Military FREE
    /// Podmienia prymitywy w prefabach Tank i Helicopter (Assets/GTA/Generated/Prefabs) na modele z paczki:
    /// czołg Tank_006 (wylot lufy ustawiany z modelu działa), helikopter Helicopter_001 (wirnik główny kręci się wokół własnej osi).
    /// Kierunek „przód” wykrywa z geometrii (lufa / ogon). Uruchamiaj po menu 1 (menu 1 odtwarza prefaby od zera).
    /// </summary>
    public static class MilitaryIntegrator
    {
        const string Dir = "Assets/GTA/Generated/Prefabs";

        [MenuItem("MiniGTA/8. Podłącz czołg i helikopter z Military", priority = 47)]
        public static void Run()
        {
            var sb = new StringBuilder("Military FREE:\n");
            sb.AppendLine(Tank(sb) ? "  ✔ czołg" : "  ✖ czołg");
            sb.AppendLine(Heli(sb) ? "  ✔ helikopter" : "  ✖ helikopter");
            AssetDatabase.SaveAssets();
            Debug.Log(sb.ToString());
            BuildDialog.Show("MiniGTA", sb.ToString(), "OK");
        }

        static GameObject Find(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Prefab", new[] { "Assets/ithappy/Military_Free" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            return null;
        }

        static GameObject Instance(GameObject src, Transform parent, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            // kolejność: skrypty → collidery → rigidbody (skrypty paczki mogą wymagać colliderów)
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) Object.DestroyImmediate(mb);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            return go;
        }

        /// <summary>Granice renderów w układzie lokalnym korzenia prefabu (opcjonalnie bez jednego poddrzewa).</summary>
        static Bounds LocalBounds(Transform root, Transform exclude, Transform only)
        {
            bool first = true;
            var b = new Bounds();
            foreach (var r in only.GetComponentsInChildren<Renderer>(false))
            {
                if (exclude != null && r.transform.IsChildOf(exclude)) continue;
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var l = root.InverseTransformPoint(corner);
                    if (first) { b = new Bounds(l, Vector3.zero); first = false; } else b.Encapsulate(l);
                }
            }
            return b;
        }

        static Transform FindDeep(Transform root, params string[] keys)
        {
            Transform best = null;
            float bestSize = -1f;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (!keys.Any(k => n.Contains(k))) continue;
                var rs = t.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                float size = b.size.sqrMagnitude;
                if (size > bestSize) { best = t; bestSize = size; }
            }
            return best;
        }

        static void RotateForwardTo(Transform model, Vector3 currentForwardLocal)
        {
            currentForwardLocal.y = 0f;
            if (currentForwardLocal.sqrMagnitude < 1e-4f) return;
            model.localRotation = Quaternion.FromToRotation(currentForwardLocal.normalized, Vector3.forward) * model.localRotation;
        }

        static bool Tank(StringBuilder sb)
        {
            string path = Dir + "/Tank.prefab";
            var src = Find("Tank_006");
            if (src == null || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { sb.AppendLine("  brak Tank_006 lub prefabu Tank (uruchom menu 1)"); return false; }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var n in new[] { "Body", "Cabin", "Windshield", "HeadlightL", "HeadlightR", "Turret", "Barrel", "TrackL", "TrackR", "TankModel" })
                {
                    var t = root.transform.Find(n);
                    if (t != null) Object.DestroyImmediate(t.gameObject);
                }
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    if (r.transform.name == "Visual") r.enabled = false; // stare koła-cylindry (transformy zostają dla fizyki)

                var m = Instance(src, root.transform, "TankModel");
                // przód = tam, gdzie wskazuje działo
                var gun = FindDeep(m.transform, "tank_gun", "gun");
                var all = LocalBounds(root.transform, null, m.transform);
                if (gun != null)
                {
                    var gb = LocalBounds(root.transform, null, gun);
                    RotateForwardTo(m.transform, gb.center - all.center);
                }
                all = LocalBounds(root.transform, null, m.transform);
                float length = Mathf.Max(0.1f, all.size.z);
                m.transform.localScale = Vector3.one * (6.2f / length);
                all = LocalBounds(root.transform, null, m.transform);
                m.transform.localPosition = new Vector3(-all.center.x, -all.min.y, -all.center.z);

                var muzzle = root.transform.Find("Muzzle");
                if (muzzle != null)
                {
                    if (gun != null)
                    {
                        var gb = LocalBounds(root.transform, null, gun);
                        muzzle.localPosition = new Vector3(gb.center.x, gb.center.y, gb.max.z + 0.1f);
                    }
                    else muzzle.localPosition = new Vector3(0f, 2.2f, 3.4f);
                }
                // wieża obracana w skrypcie Tank; działo i wylot lufy jadą razem z nią
                var tower = FindDeep(m.transform, "tank_tower", "tower", "turret");
                var tk = root.GetComponent<MiniGTA.Tank>();
                if (tower != null)
                {
                    if (gun != null && !gun.IsChildOf(tower)) gun.SetParent(tower, true);
                    if (muzzle != null) muzzle.SetParent(tower, true);
                    if (tk != null) tk.turret = tower;
                }
                else sb.AppendLine("  ⚠ nie znaleziono wieży czołgu (Tank_tower) – nie będzie się obracać");
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static bool Heli(StringBuilder sb)
        {
            string path = Dir + "/Helicopter.prefab";
            var src = Find("Helicopter_001");
            if (src == null || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { sb.AppendLine("  brak Helicopter_001 lub prefabu Helicopter (uruchom menu 1)"); return false; }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var n in new[] { "Cabin", "Glass", "Tail", "TailFin", "SkidL", "SkidR", "Mast", "Rotor", "HeliModel" })
                {
                    var t = root.transform.Find(n);
                    if (t != null) Object.DestroyImmediate(t.gameObject);
                }

                var m = Instance(src, root.transform, "HeliModel");
                // wirnik główny = największy obiekt o nazwie blade/rotor; wirnik ogonowy = drugi
                var blades = m.GetComponentsInChildren<Transform>(true)
                    .Where(t =>
                    {
                        string n = t.name.ToLowerInvariant();
                        return (n.Contains("blade") || n.Contains("rotor") || n.Contains("propeller")) && t.GetComponentInChildren<Renderer>(true) != null;
                    })
                    .Select(t => new { t, size = t.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds.size.sqrMagnitude).Max() })
                    .OrderByDescending(x => x.size).ToList();
                Transform mainBlade = blades.Count > 0 ? blades[0].t : null;
                Transform tailBlade = blades.Count > 1 ? blades[1].t : null;

                if (mainBlade != null && tailBlade != null)
                {
                    var mb = LocalBounds(root.transform, null, mainBlade);
                    var tb = LocalBounds(root.transform, null, tailBlade);
                    RotateForwardTo(m.transform, -(tb.center - mb.center)); // ogon jest z tyłu
                }

                // wirnik: pivot w środku łopat, żeby kręcił się wokół własnej osi
                Transform pivot = null;
                if (mainBlade != null)
                {
                    var mb = LocalBounds(root.transform, null, mainBlade);
                    var go = new GameObject("Rotor");
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = mb.center;
                    mainBlade.SetParent(go.transform, true);
                    pivot = go.transform;
                }

                var body = LocalBounds(root.transform, pivot, m.transform);
                float length = Mathf.Max(0.1f, body.size.z);
                float s = 8.5f / length;
                Vector3 pivotLocalBefore = pivot != null ? pivot.localPosition : Vector3.zero;
                m.transform.localScale = Vector3.one * s;
                body = LocalBounds(root.transform, pivot, m.transform);
                m.transform.localPosition = new Vector3(-body.center.x, -body.min.y, -body.center.z);
                if (pivot != null)
                {
                    // wirnik jest dzieckiem korzenia (nie modelu) – skala i przesunięcie jak u modelu
                    pivot.localScale = Vector3.one * s;
                    pivot.localPosition = pivotLocalBefore * s + m.transform.localPosition;
                    var hh = root.GetComponent<Helicopter>();
                    if (hh != null) hh.rotor = pivot;
                }

                var box = root.GetComponent<BoxCollider>();
                body = LocalBounds(root.transform, pivot, m.transform);
                if (box != null) { box.center = body.center; box.size = new Vector3(body.size.x * 0.8f, body.size.y * 0.85f, body.size.z * 0.8f); }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return true;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
