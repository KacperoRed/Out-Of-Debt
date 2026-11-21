using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Zamienia dowolny model auta (np. z Asset Store) w działający pojazd MiniGTA:
    /// collider, fizyka, koła, zdrowie, sieć, rejestracja w liście prefabów i w ruchu ulicznym.
    ///
    /// Użycie: zaznacz model/prefab auta w oknie Project → menu MiniGTA → 3/4.
    /// </summary>
    public static class VehicleImporter
    {
        const string OutDir = "Assets/GTA/Vehicles";
        const string PrefabListPath = "Assets/GTA/Generated/MiniGTANetworkPrefabs.asset";

        static readonly Regex WheelName = new Regex(@"(wheel|tire|tyre|rim|kolo|koło|rad\b|roue|rueda)", RegexOptions.IgnoreCase);
        static readonly Regex WheelExclude = new Regex(@"(steer|kierown|spare|zapas|well|arch|brake|caliper|disc|hub)", RegexOptions.IgnoreCase);

        [MenuItem("MiniGTA/3. Zrób AUTO z zaznaczonego modelu", priority = 20)]
        static void MakeCivilian() => MakeFromSelection(false);

        [MenuItem("MiniGTA/4. Zrób RADIOWÓZ z zaznaczonego modelu", priority = 21)]
        static void MakePolice() => MakeFromSelection(true);

        [MenuItem("MiniGTA/3. Zrób AUTO z zaznaczonego modelu", true)]
        [MenuItem("MiniGTA/4. Zrób RADIOWÓZ z zaznaczonego modelu", true)]
        static bool Validate() => Selection.gameObjects.Length > 0;

        static void MakeFromSelection(bool police)
        {
            var report = new StringBuilder();
            GameObject last = null;
            foreach (var src in Selection.gameObjects)
            {
                var prefab = Convert(src, police, report);
                if (prefab != null) last = prefab;
            }
            if (last != null)
            {
                Selection.activeObject = last;
                EditorGUIUtility.PingObject(last);
            }
            EditorUtility.DisplayDialog("MiniGTA – import pojazdu", report.ToString(), "OK");
        }

        /// <summary>Publiczne, żeby dało się wywołać ze skryptów / batchmode.</summary>
        public static GameObject Convert(GameObject src, bool police, StringBuilder report)
        {
            string baseName = Sanitize(src.name);
            report.AppendLine($"■ {src.name}");

            // ---------- 1. wrapper + kopia modelu ----------
            var root = new GameObject(baseName);
            GameObject model;
            if (PrefabUtility.IsPartOfPrefabAsset(src) || AssetDatabase.Contains(src))
                model = (GameObject)PrefabUtility.InstantiatePrefab(src);
            else
                model = Object.Instantiate(src);
            if (PrefabUtility.IsPartOfPrefabInstance(model))
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // ---------- 2. usuń fizykę i skrypty z paczki (gryzłyby się z naszymi) ----------
            int removed = StripComponents(model);
            if (removed > 0) report.AppendLine($"  • usunięto {removed} komponentów z paczki (Rigidbody/WheelCollider/collidery/skrypty)");

            // ---------- 3. wyśrodkuj: środek auta w (0,0), spód kół na y=0 ----------
            var renderers = model.GetComponentsInChildren<Renderer>().Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
            if (renderers.Length == 0)
            {
                report.AppendLine("  ✖ model nie ma żadnych meshy – pomijam");
                Object.DestroyImmediate(root);
                return null;
            }
            var b = LocalBounds(root.transform, renderers);
            model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            b = LocalBounds(root.transform, renderers);

            float length = b.size.z, width = b.size.x;
            if (width > length * 1.3f)
                report.AppendLine("  ⚠ auto wygląda na obrócone o 90° (szersze niż dłuższe). Obróć obiekt 'Model' w prefabie tak, by przód był w stronę +Z (niebieska strzałka).");
            if (length > 15f || length < 1.5f)
                report.AppendLine($"  ⚠ dziwny rozmiar ({length:0.0} m długości). Popraw Scale Factor w imporcie modelu (auto ≈ 4–5 m).");

            // ---------- 4. koła ----------
            var wheels = FindWheels(model.transform, root.transform);
            var vehicleWheelPoints = new Transform[4];
            var vehicleWheelVisuals = new Transform[4];
            float wheelRadius = 0.35f;
            const float suspension = 0.35f;

            if (wheels != null)
            {
                report.AppendLine("  • znaleziono koła: " + string.Join(", ", wheels.Select(w => w.name)));
                wheelRadius = 0f;
                for (int i = 0; i < 4; i++)
                {
                    var wb = LocalBounds(root.transform, wheels[i].GetComponentsInChildren<Renderer>());
                    wheelRadius += wb.extents.y / 4f;
                }
                for (int i = 0; i < 4; i++)
                {
                    var wb = LocalBounds(root.transform, wheels[i].GetComponentsInChildren<Renderer>());
                    var wp = Empty(new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" }[i], root.transform,
                        new Vector3(wb.center.x, wb.center.y + suspension * 0.75f, wb.center.z));
                    vehicleWheelPoints[i] = wp;
                    vehicleWheelVisuals[i] = wheels[i];
                }
                if (wheels.Any(w => !PivotCentered(w, root.transform)))
                    report.AppendLine("  ⚠ pivot któregoś koła nie jest w jego środku – przy toczeniu może się 'kiwać'. Wtedy wyczyść pole Wheel Visuals w Vehicle.");
            }
            else
            {
                report.AppendLine("  ⚠ nie znalazłem 4 kół po nazwach (wheel/tire/koło…). Punkty kół ustawione z wymiarów auta, koła nie będą się obracać.\n    Możesz je ręcznie przeciągnąć do pola 'Wheel Visuals' w komponencie Vehicle.");
                float x = width * 0.4f, z = length * 0.32f;
                Vector3[] pos = { new Vector3(-x, 0, z), new Vector3(x, 0, z), new Vector3(-x, 0, -z), new Vector3(x, 0, -z) };
                for (int i = 0; i < 4; i++)
                    vehicleWheelPoints[i] = Empty(new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" }[i], root.transform,
                        pos[i] + Vector3.up * (wheelRadius + suspension * 0.75f));
            }

            // ---------- 5. collider karoserii (nad kołami, żeby nie szorował) ----------
            float bottom = Mathf.Max(wheelRadius, b.min.y + 0.25f);
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, (bottom + b.max.y) * 0.5f, b.center.z);
            col.size = new Vector3(width * 0.95f, Mathf.Max(0.4f, b.max.y - bottom), length * 0.97f);

            // ---------- 6. fizyka ----------
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1200f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 1.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Health PRZED Vehicle (inaczej RequireComponent doda drugi)
            var h = root.AddComponent<Health>();
            h.maxHealth = police ? 400 : 300;
            h.faction = Faction.Vehicle;
            h.despawnDelay = 25f;

            var v = root.AddComponent<Vehicle>();
            v.displayName = police ? "Radiowóz" : ObjectNames.NicifyVariableName(baseName);
            v.isPoliceCar = police;
            v.maxSpeed = police ? 42f : 36f;
            v.enginePower = police ? 22f : 18f;
            v.wheelRadius = wheelRadius;
            v.suspensionLength = suspension;
            v.wheelPoints = vehicleWheelPoints;
            v.wheelVisuals = vehicleWheelVisuals;
            v.centerOfMass = new Vector3(0f, Mathf.Max(0.2f, wheelRadius * 0.8f), 0f);
            v.seat = Empty("Seat", root.transform, new Vector3(-width * 0.2f, b.size.y * 0.35f, b.center.z));
            v.exitPoint = Empty("ExitPoint", root.transform, new Vector3(-(width * 0.5f + 1.2f), 0.3f, b.center.z));

            if (police)
            {
                var ai = root.AddComponent<PoliceCarAI>();
                ai.sirenRed = MakeLight("SirenRedLight", root.transform, new Vector3(-0.4f, b.max.y + 0.3f, b.center.z - length * 0.05f), Color.red);
                ai.sirenBlue = MakeLight("SirenBlueLight", root.transform, new Vector3(0.4f, b.max.y + 0.3f, b.center.z - length * 0.05f), Color.blue);
            }

            var no = root.AddComponent<NetworkObject>();
            no.DontDestroyWithOwner = true;
            root.AddComponent<ClientNetworkTransform>();
            root.AddComponent<NetworkRigidbody>();

            // ---------- 7. zapis prefabu ----------
            if (!Directory.Exists(OutDir)) Directory.CreateDirectory(OutDir);
            string path = AssetDatabase.GenerateUniqueAssetPath($"{OutDir}/{baseName}{(police ? "_Police" : "")}.prefab");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            var prefabNo = prefab.GetComponent<NetworkObject>();
            typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(prefabNo, null);
            EditorUtility.SetDirty(prefab);
            report.AppendLine($"  ✔ zapisano: {path}");

            // ---------- 8. rejestracja w sieci i w scenie ----------
            RegisterNetworkPrefab(prefab, report);
            RegisterInScene(prefab, police, report);
            AssetDatabase.SaveAssets();
            report.AppendLine();
            return prefab;
        }

        // =====================================================================

        static int StripComponents(GameObject model)
        {
            int n = 0;
            foreach (var c in model.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c is Renderer || c is MeshFilter || c is LODGroup || c is Light || c is AudioSource) continue;
                if (c is Rigidbody || c is Collider || c is Joint || c is Animator || c is ParticleSystem) { Object.DestroyImmediate(c); n++; continue; }
                if (c is MonoBehaviour) { Object.DestroyImmediate(c); n++; }
            }
            // drugi przebieg – niektóre komponenty blokują usunięcie innych (RequireComponent)
            foreach (var c in model.GetComponentsInChildren<Component>(true))
                if (c is Rigidbody) { Object.DestroyImmediate(c); n++; }
            return n;
        }

        /// <summary>Szuka 4 kół po nazwach i sortuje je: LP, PP, LT, PT.</summary>
        static Transform[] FindWheels(Transform model, Transform root)
        {
            var candidates = model.GetComponentsInChildren<Transform>(true)
                .Where(t => t != model && WheelName.IsMatch(t.name) && !WheelExclude.IsMatch(t.name)
                            && t.GetComponentsInChildren<Renderer>().Length > 0
                            && IsWheelShaped(LocalBounds(root, t.GetComponentsInChildren<Renderer>())))
                .ToList();

            // weź najwyższe w hierarchii (np. "Wheel_FL", a nie jego dziecko "Wheel_FL_Rim")
            candidates = candidates.Where(t => !candidates.Any(o => o != t && t.IsChildOf(o))).ToList();
            if (candidates.Count < 4) return null;

            // jeśli jest więcej niż 4 (np. 6 kół w ciężarówce) – bierz 2 najbardziej z przodu i 2 z tyłu
            var withPos = candidates.Select(t => (t, p: LocalBounds(root, t.GetComponentsInChildren<Renderer>()).center)).ToList();
            var front = withPos.OrderByDescending(w => w.p.z).Take(2).OrderBy(w => w.p.x).ToList();
            var rear = withPos.OrderBy(w => w.p.z).Take(2).OrderBy(w => w.p.x).ToList();
            if (front.Intersect(rear).Any()) return null;
            if (Mathf.Abs(front[0].p.x - front[1].p.x) < 0.3f) return null; // to nie są lewe/prawe koło
            return new[] { front[0].t, front[1].t, rear[0].t, rear[1].t };
        }

        /// <summary>Koło: okrągłe z boku (wysokość ≈ długość), wąskie, nie większe niż ~1.6 m.
        /// Odrzuca np. obiekt-grupę "Wheels" zawierający wszystkie koła.</summary>
        static bool IsWheelShaped(Bounds b)
        {
            float d = b.size.y;
            return d > 0.2f && d < 1.6f && Mathf.Abs(b.size.z - d) < d * 0.35f && b.size.x < d * 1.2f;
        }

        static bool PivotCentered(Transform wheel, Transform root)
        {
            var wb = LocalBounds(root, wheel.GetComponentsInChildren<Renderer>());
            Vector3 pivot = root.InverseTransformPoint(wheel.position);
            return Vector3.Distance(pivot, wb.center) < wb.extents.magnitude * 0.25f;
        }

        static Bounds LocalBounds(Transform root, Renderer[] renderers)
        {
            bool init = false;
            var bounds = new Bounds();
            foreach (var r in renderers)
            {
                var wb = r.bounds;
                Vector3 c = wb.center, e = wb.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = c + Vector3.Scale(e, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var local = root.InverseTransformPoint(corner);
                    if (!init) { bounds = new Bounds(local, Vector3.zero); init = true; }
                    else bounds.Encapsulate(local);
                }
            }
            return bounds;
        }

        static void RegisterNetworkPrefab(GameObject prefab, StringBuilder report)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null)
            {
                report.AppendLine($"  ⚠ brak {PrefabListPath} – dodaj prefab ręcznie do listy prefabów NetworkManagera");
                return;
            }
            if (list.PrefabList.Any(p => p.Prefab == prefab)) return;
            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
            report.AppendLine("  ✔ dodano do listy prefabów sieciowych");
        }

        static void RegisterInScene(GameObject prefab, bool police, StringBuilder report)
        {
            var traffic = Object.FindAnyObjectByType<TrafficSpawner>();
            var policeSpawner = Object.FindAnyObjectByType<PoliceSpawner>();
            if (traffic == null && policeSpawner == null)
            {
                report.AppendLine("  ℹ otwarta scena nie ma GameManagers – otwórz City.unity i podepnij prefab ręcznie (TrafficSpawner → Car Prefabs)");
                return;
            }

            if (police)
            {
                if (policeSpawner != null)
                {
                    Undo.RecordObject(policeSpawner, "MiniGTA police car");
                    policeSpawner.policeCarPrefab = prefab;
                    EditorUtility.SetDirty(policeSpawner);
                }
                if (traffic != null)
                {
                    Undo.RecordObject(traffic, "MiniGTA police car");
                    traffic.policeCarPrefab = prefab;
                    EditorUtility.SetDirty(traffic);
                }
                report.AppendLine("  ✔ ustawiono jako radiowóz policji (PoliceSpawner + zaparkowany radiowóz)");
            }
            else if (traffic != null)
            {
                Undo.RecordObject(traffic, "MiniGTA car");
                var cars = new List<GameObject>(traffic.carPrefabs ?? new GameObject[0]);
                if (!cars.Contains(prefab)) cars.Add(prefab);
                traffic.carPrefabs = cars.ToArray();
                EditorUtility.SetDirty(traffic);
                report.AppendLine("  ✔ dodano do ruchu ulicznego i parkingów (TrafficSpawner)");
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            report.AppendLine("  → ZAPISZ SCENĘ (Ctrl+S)");
        }

        static Transform Empty(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        static Light MakeLight(string name, Transform parent, Vector3 pos, Color c)
        {
            var l = Empty(name, parent, pos).gameObject.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = 12f;
            l.intensity = 4f;
            l.enabled = false;
            return l;
        }

        static string Sanitize(string s)
        {
            foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s.Replace(" ", "_");
        }
    }
}
