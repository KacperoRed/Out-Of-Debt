using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Podłącza modele z paczek Asset Store do gry:
    ///  • "Low Poly Weapons VOL.1" → modele broni w rękach gracza / policji / gangów, granat RGD-5, RPG7
    ///  • "Super Retro Compact Pack" → 16 aut (cywilne, taksówka, muscle, radiowóz)
    ///  • dodaje sklepy z bronią i warsztat do otwartej sceny
    /// Można uruchamiać wielokrotnie – istniejące rzeczy są aktualizowane, nie dublowane.
    /// </summary>
    public static class PackIntegrator
    {
        const string WeaponsOut = "Assets/GTA/Weapons";
        const string VehiclesOut = "Assets/GTA/Vehicles";
        const string GeneratedPrefabs = "Assets/GTA/Generated/Prefabs";
        const string PrefabListPath = "Assets/GTA/Generated/MiniGTANetworkPrefabs.asset";

        static readonly Dictionary<string, string> Preferred = new Dictionary<string, string>
        {
            { "M1911", "pistol_001" }, { "Bennelli_M4", "shotgun_001" }, { "AK74", "rifle_001" }, { "M107", "sniper_rifle_001" },
        };

        /// <summary>Indeks broni w WeaponController → (nazwa prefabu z paczki, docelowa długość w metrach).</summary>
        static readonly (int index, string prefab, float length)[] WeaponMap =
        {
            (0, "baseball_bat_001", 0.85f),   // Weapons FREE (ithappy): zamiast pięści
            (1, "M1911", 0.22f),
            (2, "Uzi", 0.42f),
            (3, "Bennelli_M4", 1.0f),
            (4, "AK74", 0.95f),
            (5, "M249", 1.05f),
            (6, "M107", 1.4f),
            (7, "RGD-5", 0.12f),
            (8, "RPG7", 1.0f),
        };

        [MenuItem("MiniGTA/5. Podłącz modele z paczek (bronie + auta + sklepy)", priority = 40)]
        public static void IntegrateAll()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (Object.FindAnyObjectByType<TrafficSpawner>() == null)
            {
                BuildDialog.Show("MiniGTA", "Najpierw otwórz scenę Assets/GTA/Scenes/City.unity (z obiektem GameManagers).", "OK");
                return;
            }
            var report = new StringBuilder();
            Run(report);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[MiniGTA] Integracja paczek:\n" + report);
            if (!Application.isBatchMode)
                BuildDialog.Show("MiniGTA – paczki podłączone", report.ToString(), "OK");
        }

        /// <summary>Do linii poleceń: otwiera City.unity, podłącza wszystko i zapisuje.</summary>
        public static void IntegrateBatch()
        {
            EditorSceneManager.OpenScene("Assets/GTA/Scenes/City.unity");
            IntegrateAll();
        }

        public static void Run(StringBuilder report)
        {
            Directory.CreateDirectory(WeaponsOut);
            Directory.CreateDirectory(VehiclesOut);
            AssetDatabase.Refresh();

            // ---------- BRONIE ----------
            report.AppendLine("BRONIE:");
            var views = new Dictionary<int, GameObject>();
            foreach (var (index, prefabName, length) in WeaponMap)
            {
                // styl kreskówkowy z „Weapons FREE” ma pierwszeństwo przed realistycznymi modelami
                string preferred;
                var src = (Preferred.TryGetValue(prefabName, out preferred) ? FindPackPrefab(preferred) : null) ?? FindPackPrefab(prefabName);
                if (src == null) { report.AppendLine($"  ✖ nie znaleziono {prefabName}"); continue; }
                var view = BuildWeaponView(src, length, prefabName == "RGD-5", index == 0);
                views[index] = view;
                report.AppendLine($"  ✔ {prefabName} → slot {index + 1}");
            }

            var grenade = BuildProjectile("Projectile_Grenade", false, views.TryGetValue(7, out var g) ? g : null);
            var rocket = BuildProjectile("Projectile_Rocket", true, null);
            RegisterNetworkPrefab(grenade);
            RegisterNetworkPrefab(rocket);
            report.AppendLine("  ✔ prefaby pocisków: granat + rakieta RPG");

            SetupPlayerPrefab(views, grenade, rocket, report);
            ArmNpc("PoliceOfficer", views.TryGetValue(1, out var pistol) ? pistol : null, report);
            ArmNpc("Gangster", views.TryGetValue(2, out var uzi) ? uzi : null, report);

            // ---------- AUTA ----------
            report.AppendLine("\nAUTA:");
            var civilians = new List<GameObject>();
            GameObject police = null, taxi = null;
            foreach (var src in FindAllPackPrefabs("CompactCar_"))
            {
                bool isPolice = src.name.Contains("Police");
                string outPath = $"{VehiclesOut}/{src.name}{(isPolice ? "_Police" : "")}.prefab";
                var car = AssetDatabase.LoadAssetAtPath<GameObject>(outPath);
                if (car == null)
                {
                    var sb = new StringBuilder();
                    car = VehicleImporter.Convert(src, isPolice, sb);
                    if (car == null) { report.AppendLine($"  ✖ {src.name}: " + sb); continue; }
                }
                TuneCar(car, src.name);
                RegisterNetworkPrefab(car); // lista mogła zostać wygenerowana od nowa (menu 1)
                if (isPolice) police = car;
                else civilians.Add(car);
                if (src.name.Contains("Taxi")) taxi = car;
            }
            report.AppendLine($"  ✔ aut cywilnych: {civilians.Count}, radiowóz: {(police != null ? "tak" : "nie")}");

            var ts = Object.FindAnyObjectByType<TrafficSpawner>();
            var ps = Object.FindAnyObjectByType<PoliceSpawner>();
            var mm = Object.FindAnyObjectByType<MissionManager>();
            if (civilians.Count > 0 && ts != null)
            {
                Undo.RecordObject(ts, "MiniGTA cars");
                ts.carPrefabs = civilians.ToArray(); // zastępuje auta-kostki
                EditorUtility.SetDirty(ts);
            }
            if (police != null)
            {
                if (ts != null) { ts.policeCarPrefab = police; EditorUtility.SetDirty(ts); }
                if (ps != null) { Undo.RecordObject(ps, "MiniGTA police"); ps.policeCarPrefab = police; EditorUtility.SetDirty(ps); }
            }
            if (taxi != null && mm != null) { Undo.RecordObject(mm, "MiniGTA taxi"); mm.vehiclePrefab = taxi; EditorUtility.SetDirty(mm); }

            // misje z własnym autem (np. wyścig) dostają muscle car z paczki
            var muscle = civilians.FirstOrDefault(c => c.name.Contains("Muscle"));
            if (muscle != null)
                foreach (var mission in Object.FindObjectsByType<Mission>(FindObjectsSortMode.None))
                {
                    bool changed = false;
                    foreach (var step in mission.steps)
                        if (step.vehicleOverride != null && !step.vehicleOverride.name.Contains("Compact")) { step.vehicleOverride = muscle; changed = true; }
                    if (changed) EditorUtility.SetDirty(mission);
                }
            report.AppendLine("  ✔ ruch uliczny, policja i misja 'Pierwsza robota' używają nowych aut");

            // ---------- SKLEPY ----------
            int added = AddShops();
            report.AppendLine(added > 0 ? $"\nSKLEPY: dodano {added} (2× broń, 1× warsztat)" : "\nSKLEPY: już są w scenie");

            int fixedRefs = EnsureSceneReferencesRegistered();
            if (fixedRefs > 0) report.AppendLine($"  ✔ dopisano {fixedRefs} prefabów do listy sieciowej");
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        // =====================================================================
        //  BRONIE
        // =====================================================================

        /// <summary>Czy w projekcie są paczki, które umiemy podłączyć.</summary>
        public static bool PacksPresent() => FindPackPrefab("M1911") != null || FindAllPackPrefabs("CompactCar_").Any();

        static GameObject FindPackPrefab(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/GTA/")) continue;
                if (Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            // brak prefabu – spróbuj samego modelu .fbx
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:Model"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }

        static IEnumerable<GameObject> FindAllPackPrefabs(string prefix)
        {
            return AssetDatabase.FindAssets($"{prefix} t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.StartsWith("Assets/GTA/") && Path.GetFileName(p).StartsWith(prefix))
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>);
        }

        /// <summary>
        /// Tworzy "model w ręce": ustawia lufę wzdłuż +Z, górę broni w +Y, skaluje do zadanej długości
        /// i dodaje dziecko "Muzzle" na końcu lufy. Kierunek wykrywany automatycznie z kształtu siatki.
        /// </summary>
        static GameObject BuildWeaponView(GameObject src, float targetLength, bool isGrenade, bool melee = false)
        {
            var root = new GameObject("View_" + src.name);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
            if (model == null) model = Object.Instantiate(src);
            if (PrefabUtility.IsPartOfPrefabInstance(model))
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            foreach (var c in model.GetComponentsInChildren<Component>(true))
                if (c is Collider || c is Rigidbody || c is MonoBehaviour) Object.DestroyImmediate(c);

            var verts = CollectVertices(root.transform);
            if (verts.Count == 0) return SaveView(root, src.name);

            if (!isGrenade)
            {
                // osie: najdłuższa = lufa, druga = góra/dół
                var b = BoundsOf(verts);
                int[] axes = { 0, 1, 2 };
                var sorted = axes.OrderByDescending(a => b.size[a]).ToArray();
                int L = sorted[0], U = sorted[1];

                // koniec lufy = koniec, gdzie przekrój jest najcieńszy
                float lMin = b.min[L], lMax = b.max[L], slice = (lMax - lMin) * 0.15f;
                var endA = verts.Where(v => v[L] < lMin + slice).ToList();
                var endB = verts.Where(v => v[L] > lMax - slice).ToList();
                float spreadA = endA.Count > 0 ? endA.Max(v => v[U]) - endA.Min(v => v[U]) : 999f;
                float spreadB = endB.Count > 0 ? endB.Max(v => v[U]) - endB.Min(v => v[U]) : 999f;
                float sL = spreadB <= spreadA ? 1f : -1f;
                if (melee) sL = -sL; // kij/siekiera: „przód” to grubszy koniec (główka), ręka trzyma cienką rękojeść
                var muzzleEnd = sL > 0 ? endB : endA;

                // góra: lufa leży w górnej części broni (chwyt/magazynek wystają w dół)
                float muzzleU = muzzleEnd.Count > 0 ? muzzleEnd.Average(v => v[U]) : b.center[U];
                float sU = muzzleU >= b.center[U] ? 1f : -1f;

                Vector3 f = Vector3.zero, u = Vector3.zero;
                f[L] = sL;
                u[U] = sU;
                model.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(f, u));
            }

            // skala do docelowej długości (granat: wysokość)
            verts = CollectVertices(root.transform);
            var nb = BoundsOf(verts);
            float current = isGrenade ? nb.size.y : nb.size.z;
            if (current > 0.0001f) model.transform.localScale *= targetLength / current;

            // pozycja: chwyt w dłoni – środek broni lekko przed ręką
            verts = CollectVertices(root.transform);
            nb = BoundsOf(verts);
            if (melee)
            {
                // chwyt: rękojeść (tył, z = min) tuż za dłonią, reszta kija do przodu
                model.transform.localPosition -= new Vector3(nb.center.x, nb.center.y, nb.min.z + 0.12f);
            }
            else
            model.transform.localPosition -= nb.center - new Vector3(0f, 0f, isGrenade ? 0f : nb.size.z * 0.2f);

            // wylot lufy
            verts = CollectVertices(root.transform);
            nb = BoundsOf(verts);
            var tip = verts.Where(v => v.z > nb.max.z - nb.size.z * 0.05f).ToList();
            float tipY = tip.Count > 0 ? tip.Average(v => v.y) : nb.center.y;
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root.transform, false);
            muzzle.localPosition = new Vector3(0f, tipY, nb.max.z + 0.02f);

            return SaveView(root, src.name);
        }

        static GameObject SaveView(GameObject root, string name)
        {
            var path = $"{WeaponsOut}/View_{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static List<Vector3> CollectVertices(Transform root)
        {
            var result = new List<Vector3>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices) result.Add(m.MultiplyPoint3x4(v));
            }
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh == null) continue;
                var m = root.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                foreach (var v in smr.sharedMesh.vertices) result.Add(m.MultiplyPoint3x4(v));
            }
            return result;
        }

        static Bounds BoundsOf(List<Vector3> pts)
        {
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            return b;
        }

        static GameObject BuildProjectile(string name, bool rocket, GameObject grenadeView)
        {
            var go = new GameObject(name);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = rocket ? 2f : 0.4f;
            rb.linearDamping = rocket ? 0f : 0.1f;
            rb.angularDamping = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.useGravity = !rocket;

            if (rocket)
            {
                var col = go.AddComponent<CapsuleCollider>();
                col.direction = 2;
                col.radius = 0.08f;
                col.height = 0.7f;
                var body = Prim(PrimitiveType.Cylinder, "Body", go.transform, Vector3.zero, new Vector3(0.12f, 0.3f, 0.12f), new Vector3(90, 0, 0), new Color(0.3f, 0.35f, 0.25f), false);
                Prim(PrimitiveType.Sphere, "Warhead", go.transform, new Vector3(0, 0, 0.32f), new Vector3(0.16f, 0.16f, 0.25f), Vector3.zero, new Color(0.25f, 0.3f, 0.2f), false);
                Prim(PrimitiveType.Sphere, "Flame", go.transform, new Vector3(0, 0, -0.35f), new Vector3(0.12f, 0.12f, 0.2f), Vector3.zero, new Color(1f, 0.6f, 0.1f), true);
                var trail = go.AddComponent<TrailRenderer>();
                trail.time = 0.6f;
                trail.startWidth = 0.18f;
                trail.endWidth = 0.02f;
                string trailPath = $"{WeaponsOut}/RocketTrail.mat";
                var trailMat = AssetDatabase.LoadAssetAtPath<Material>(trailPath);
                if (trailMat == null)
                {
                    trailMat = new Material(Shader.Find("Sprites/Default"));
                    AssetDatabase.CreateAsset(trailMat, trailPath);
                }
                trail.sharedMaterial = trailMat;
                trail.startColor = new Color(1f, 0.8f, 0.5f, 0.8f);
                trail.endColor = new Color(0.5f, 0.5f, 0.5f, 0f);
                var light = new GameObject("Light").AddComponent<Light>();
                light.transform.SetParent(go.transform, false);
                light.transform.localPosition = new Vector3(0, 0, -0.4f);
                light.color = new Color(1f, 0.6f, 0.2f);
                light.range = 5f;
                light.intensity = 3f;
                body.name = "Body";
            }
            else
            {
                var col = go.AddComponent<SphereCollider>();
                col.radius = 0.07f;
                if (grenadeView != null)
                {
                    var v = (GameObject)PrefabUtility.InstantiatePrefab(grenadeView);
                    PrefabUtility.UnpackPrefabInstance(v, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    v.name = "Visual";
                    v.transform.SetParent(go.transform, false);
                    v.transform.localPosition = Vector3.zero;
                    var m = v.transform.Find("Muzzle");
                    if (m != null) Object.DestroyImmediate(m.gameObject);
                }
                else Prim(PrimitiveType.Sphere, "Visual", go.transform, Vector3.zero, Vector3.one * 0.12f, Vector3.zero, new Color(0.2f, 0.3f, 0.15f), false);
            }

            var proj = go.AddComponent<Projectile>();
            proj.explodeOnImpact = rocket;
            proj.useGravity = !rocket;
            proj.fuseTime = 3f;
            proj.maxLifetime = rocket ? 6f : 8f;

            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkTransform>();      // serwer steruje lotem
            go.AddComponent<NetworkRigidbody>();

            string path = $"{WeaponsOut}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            ValidateNetworkObject(prefab);
            return prefab;
        }

        static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler, Color color, bool emissive)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.transform.localEulerAngles = euler;
            string matPath = $"{WeaponsOut}/Mat_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? "Universal Render Pipeline/Lit" : "Standard"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.color = color;
            if (emissive) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", color * 3f); }
            g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }

        static void SetupPlayerPrefab(Dictionary<int, GameObject> views, GameObject grenade, GameObject rocket, StringBuilder report)
        {
            string path = $"{GeneratedPrefabs}/Player.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { report.AppendLine("  ✖ brak prefabu gracza " + path); return; }
            var root = PrefabUtility.LoadPrefabContents(path);

            var wc = root.GetComponent<WeaponController>();
            if (wc.weapons == null || wc.weapons.Length < 9 || wc.weapons.All(w => w.price == 0))
                wc.weapons = WeaponController.DefaultWeapons();
            foreach (var kv in views)
                if (kv.Key < wc.weapons.Length) wc.weapons[kv.Key].viewModel = kv.Value;
            if (views.ContainsKey(0) && wc.weapons.Length > 0 && wc.weapons[0].kind == WeaponKind.Melee) wc.weapons[0].name = "Kij bejsbolowy";
            wc.grenadePrefab = grenade;
            wc.rocketPrefab = rocket;

            var hand = root.transform.Find("Hand");
            if (hand == null)
            {
                hand = new GameObject("Hand").transform;
                hand.SetParent(root.transform, false);
                hand.localPosition = new Vector3(0.32f, 1.25f, 0.35f);
            }
            var visual = root.GetComponent<WeaponVisual>();
            if (visual == null) visual = root.AddComponent<WeaponVisual>();
            visual.hand = hand;
            var gun = root.transform.Find("Gun");
            if (gun != null) visual.fallback = gun.gameObject;
            if (root.GetComponent<ShopClient>() == null) root.AddComponent<ShopClient>();

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            report.AppendLine("  ✔ gracz: modele broni w ręce, granaty, RPG, zakupy (ShopClient)");
        }

        static void ArmNpc(string prefabName, GameObject view, StringBuilder report)
        {
            string path = $"{GeneratedPrefabs}/{prefabName}.prefab";
            if (view == null || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
            var root = PrefabUtility.LoadPrefabContents(path);
            var oldGun = root.transform.Find("Gun");
            if (oldGun != null) oldGun.gameObject.SetActive(false);
            var oldModel = root.transform.Find("WeaponModel");
            if (oldModel != null) Object.DestroyImmediate(oldModel.gameObject);

            var w = (GameObject)PrefabUtility.InstantiatePrefab(view);
            PrefabUtility.UnpackPrefabInstance(w, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            w.name = "WeaponModel";
            w.transform.SetParent(root.transform, false);
            w.transform.localPosition = new Vector3(0.32f, 1.25f, 0.35f);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            report.AppendLine($"  ✔ {prefabName}: dostał model {view.name.Replace("View_", "")}");
        }

        // =====================================================================
        //  AUTA
        // =====================================================================

        static void TuneCar(GameObject car, string srcName)
        {
            var v = car.GetComponent<Vehicle>();
            if (v == null) return;
            if (srcName.Contains("Muscle")) { v.maxSpeed = 46f; v.enginePower = 25f; v.displayName = "Muscle car"; }
            else if (srcName.Contains("Taxi")) { v.displayName = "Taksówka"; }
            else if (srcName.Contains("Police")) { v.displayName = "Radiowóz"; }
            else if (srcName.Contains("Neon")) { v.maxSpeed = 42f; v.enginePower = 22f; v.displayName = "Neon"; }
            else v.displayName = "Kompakt " + srcName.Replace("CompactCar_", "");
            EditorUtility.SetDirty(car);
        }

        // =====================================================================
        //  SKLEPY
        // =====================================================================

        /// <summary>Dodaje 2 sklepy z bronią i warsztat do miasta (układ z GTASceneBuilder).</summary>
        public static int AddShops()
        {
            if (Object.FindObjectsByType<ShopZone>(FindObjectsSortMode.None).Length > 0) return 0;
            var root = new GameObject("Shops").transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "MiniGTA shops");
            AddShop(root, "Ammu-Nation Centrum", ShopType.Weapons, new Vector3(-5.5f, 0.15f, 38f), 3f);
            AddShop(root, "Ammu-Nation Zachód", ShopType.Weapons, new Vector3(-110.5f, 0.15f, 87f), 3f);
            AddShop(root, "Pay'n'Spray", ShopType.Repair, new Vector3(63.5f, 0.15f, -30f), 4.5f);
            return 3;
        }

        static void AddShop(Transform root, string name, ShopType type, Vector3 pos, float radius)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            var z = go.AddComponent<ShopZone>();
            z.type = type;
            z.shopName = name;
            z.radius = radius;
        }

        // =====================================================================

        /// <summary>
        /// Dopisuje do listy sieciowej każdy prefab używany przez spawnery / misje / gracza w otwartej scenie.
        /// Bez tego host spawnuje obiekt, którego klienci nie znają (i go nie widzą).
        /// </summary>
        public static int EnsureSceneReferencesRegistered()
        {
            var used = new HashSet<GameObject>();
            void Add(GameObject g) { if (g != null && g.GetComponent<NetworkObject>() != null) used.Add(g); }
            foreach (var ts in Object.FindObjectsByType<TrafficSpawner>(FindObjectsSortMode.None))
            {
                foreach (var c in ts.carPrefabs ?? new GameObject[0]) Add(c);
                Add(ts.policeCarPrefab); Add(ts.driverPedestrianPrefab);
            }
            foreach (var ps in Object.FindObjectsByType<PoliceSpawner>(FindObjectsSortMode.None))
            { Add(ps.officerPrefab); Add(ps.swatPrefab); Add(ps.armyPrefab); Add(ps.policeCarPrefab); Add(ps.armyCarPrefab); Add(ps.tankPrefab); Add(ps.helicopterPrefab); }
            foreach (var pd in Object.FindObjectsByType<PedestrianSpawner>(FindObjectsSortMode.None))
                foreach (var p in pd.pedestrianPrefabs ?? new GameObject[0]) Add(p);
            foreach (var pk in Object.FindObjectsByType<PickupSpawner>(FindObjectsSortMode.None))
                foreach (var spot in pk.spots ?? new PickupSpawner.Spot[0]) Add(spot.prefab);
            foreach (var mm in Object.FindObjectsByType<MissionManager>(FindObjectsSortMode.None)) { Add(mm.enemyPrefab); Add(mm.vehiclePrefab); }
            foreach (var m in Object.FindObjectsByType<Mission>(FindObjectsSortMode.None))
                foreach (var st in m.steps) Add(st.vehicleOverride);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>($"{GeneratedPrefabs}/Player.prefab");
            if (player != null)
            {
                var wc = player.GetComponent<WeaponController>();
                if (wc != null) { Add(wc.grenadePrefab); Add(wc.rocketPrefab); }
            }

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null) return 0;
            int added = 0;
            foreach (var g in used)
                if (!list.PrefabList.Any(p => p.Prefab == g)) { list.Add(new NetworkPrefab { Prefab = g }); added++; }
            if (added > 0) EditorUtility.SetDirty(list);
            return added;
        }

        static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null || list.PrefabList.Any(p => p.Prefab == prefab)) return;
            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        static void ValidateNetworkObject(GameObject prefab)
        {
            var no = prefab.GetComponent<NetworkObject>();
            typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.Invoke(no, null);
            EditorUtility.SetDirty(prefab);
        }
    }
}
