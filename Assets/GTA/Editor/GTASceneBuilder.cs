using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Menu "MiniGTA" w edytorze: generuje prefaby (gracz, auta, NPC, policja, znajdźki)
    /// oraz demonstracyjne miasto z NavMeshem, ruchem ulicznym, misjami i NetworkManagerem.
    /// Po wygenerowaniu wszystko możesz dowolnie edytować ręcznie.
    /// </summary>
    public static partial class GTASceneBuilder
    {
        const string Root = "Assets/GTA/Generated";
        const string PrefabDir = Root + "/Prefabs";
        const string MatDir = Root + "/Materials";
        const string ScenePath = "Assets/GTA/Scenes/City.unity";

        // miasto: siatka 10x10 kwartałów (+ obwodnica) – układ w GTASceneBuilder.City.cs
        const int Blocks = 10;
        const float BlockSize = 44f;
        const float RoadWidth = 14f;
        const float Pitch = BlockSize + RoadWidth;               // 58
        static float Half => (Blocks * Pitch + RoadWidth) * 0.5f; // 297
        static float RoadCenter(int i) => -Half + RoadWidth * 0.5f + i * Pitch;

        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        class Prefabs
        {
            public GameObject player, officer, swat, army, gang, policeCar, armyCar, tank, heli, healthPickup, ammoPickup, moneyPickup;
            public readonly List<GameObject> peds = new List<GameObject>();
            public readonly List<GameObject> cars = new List<GameObject>();
            public readonly List<GameObject> all = new List<GameObject>();
        }

        [MenuItem("MiniGTA/1. Zbuduj wszystko (prefaby + miasto)", priority = 0)]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var prefabs = CreatePrefabs();
            BuildScene(prefabs);
            IntegratePacksIfPresent();
            BuildDialog.Show("MiniGTA",
                "Gotowe!\n\nScena: " + ScenePath + "\nWciśnij Play i kliknij 'HOSTUJ GRĘ'.\n\n" +
                "Multiplayer na jednym PC: Build (File > Build Profiles > Build) i uruchom .exe jako drugi gracz (Dołącz, IP 127.0.0.1).", "OK");
        }

        [MenuItem("MiniGTA/2. Zbuduj grę (Windows .exe)", priority = 1)]
        public static void BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/MiniGTA.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log("[MiniGTA] Build: " + report.summary.result + " -> " + options.locationPathName);
            if (!Application.isBatchMode && report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorUtility.RevealInFinder(options.locationPathName);
        }

        /// <summary>Wersja do uruchamiania z linii poleceń (-batchmode -executeMethod).</summary>
        public static void BuildAllBatch()
        {
            var prefabs = CreatePrefabs();
            BuildScene(prefabs);
            IntegratePacksIfPresent();
        }

        /// <summary>Po wygenerowaniu miasta podłącz modele z paczek (bronie/auta) i sklepy.</summary>
        static void IntegratePacksIfPresent()
        {
            if (PackIntegrator.PacksPresent())
            {
                var sb = new System.Text.StringBuilder();
                PackIntegrator.Run(sb);
            }
            else PackIntegrator.AddShops();
            PackIntegrator.EnsureSceneReferencesRegistered();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
        }

        // =====================================================================
        //  MATERIAŁY
        // =====================================================================

        static Material Mat(string name, Color color, bool emissive = false)
        {
            if (Mats.TryGetValue(name, out var m) && m != null) return m;
            string path = $"{MatDir}/{name}.mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                bool urp = GraphicsSettings.currentRenderPipeline != null;
                var shader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = color;
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * 2f);
            }
            EditorUtility.SetDirty(m);
            Mats[name] = m;
            return m;
        }

        // =====================================================================
        //  POMOCNICZE
        // =====================================================================

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool keepCollider = false, Vector3? euler = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (euler.HasValue) go.transform.localRotation = Quaternion.Euler(euler.Value);
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static Transform Empty(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        static void EnsureDir(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }

        static GameObject SavePrefab(GameObject go, string name, Prefabs list)
        {
            if (go.GetComponents<Health>().Length > 1) Debug.LogError($"[MiniGTA] {name}: więcej niż jeden komponent Health!");
            string path = $"{PrefabDir}/{name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);

            // wygeneruj GlobalObjectIdHash dla NetworkObject (normalnie robi to Inspector)
            var no = prefab.GetComponent<NetworkObject>();
            if (no != null)
            {
                var validate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                validate?.Invoke(no, null);
                EditorUtility.SetDirty(prefab);
            }
            list.all.Add(prefab);
            return prefab;
        }

        // =====================================================================
        //  PREFABY
        // =====================================================================

        static Prefabs CreatePrefabs()
        {
            EnsureDir(PrefabDir);
            EnsureDir(MatDir);
            EnsureDir("Assets/GTA/Scenes");
            AssetDatabase.Refresh();

            var p = new Prefabs();
            p.player = SavePrefab(BuildPlayer(), "Player", p);

            Color[] pedColors = { new Color(0.9f, 0.75f, 0.55f), new Color(0.55f, 0.75f, 0.5f), new Color(0.75f, 0.55f, 0.8f), new Color(0.9f, 0.9f, 0.9f) };
            for (int i = 0; i < pedColors.Length; i++)
                p.peds.Add(SavePrefab(BuildPedestrian(pedColors[i], i), "Pedestrian_" + i, p));

            p.officer = SavePrefab(BuildCombatNpc("Officer", Faction.Police, new Color(0.1f, 0.2f, 0.6f), 100, 6, 0.5f, 50), "PoliceOfficer", p);
            p.swat = SavePrefab(BuildCombatNpc("SWAT", Faction.Police, new Color(0.05f, 0.05f, 0.05f), 180, 11, 0.7f, 90), "PoliceSwat", p);
            p.army = SavePrefab(BuildCombatNpc("Zolnierz", Faction.Police, new Color(0.25f, 0.32f, 0.18f), 150, 9, 0.6f, 70), "ArmySoldier", p);
            p.gang = SavePrefab(BuildCombatNpc("Gangster", Faction.Gang, new Color(0.55f, 0.1f, 0.6f), 80, 6, 0.45f, 120), "Gangster", p);

            p.cars.Add(SavePrefab(BuildCar("Sedan", new Color(0.75f, 0.1f, 0.1f), false, 35, 18), "Car_Sedan_Red", p));
            p.cars.Add(SavePrefab(BuildCar("Sedan", new Color(0.9f, 0.9f, 0.9f), false, 33, 17), "Car_Sedan_White", p));
            p.cars.Add(SavePrefab(BuildCar("Taxi", new Color(1f, 0.8f, 0.1f), false, 34, 18), "Car_Taxi", p));
            p.cars.Add(SavePrefab(BuildCar("Sport", new Color(0.1f, 0.5f, 0.2f), false, 48, 26), "Car_Sport_Green", p));
            p.policeCar = SavePrefab(BuildCar("Radiowóz", new Color(0.08f, 0.1f, 0.2f), true, 42, 22), "Car_Police", p);
            p.armyCar = SavePrefab(BuildCar("Wojskowy transporter", new Color(0.25f, 0.32f, 0.18f), true, 40, 24), "Car_Army", p);
            p.tank = SavePrefab(BuildTank(), "Tank", p);
            p.heli = SavePrefab(BuildHelicopter(), "Helicopter", p);

            p.healthPickup = SavePrefab(BuildPickup("Pickup_Health", Pickup.PickupType.Health, 50, new Color(0.2f, 0.9f, 0.3f)), "Pickup_Health", p);
            p.ammoPickup = SavePrefab(BuildPickup("Pickup_Ammo", Pickup.PickupType.Ammo, 30, new Color(0.9f, 0.7f, 0.1f)), "Pickup_Ammo", p);
            p.moneyPickup = SavePrefab(BuildPickup("Pickup_Money", Pickup.PickupType.Money, 250, new Color(0.3f, 0.8f, 0.3f)), "Pickup_Money", p);

            AssetDatabase.SaveAssets();
            return p;
        }

        static GameObject BuildHumanoidBody(GameObject root, Color color, bool hat, Color hatColor)
        {
            var body = Prim(PrimitiveType.Capsule, "Body", root.transform, new Vector3(0, 1f, 0), new Vector3(0.8f, 1f, 0.8f), Mat("Body_" + ColorUtility.ToHtmlStringRGB(color), color));
            Prim(PrimitiveType.Cube, "Face", root.transform, new Vector3(0, 1.55f, 0.33f), new Vector3(0.4f, 0.2f, 0.1f), Mat("Skin", new Color(0.95f, 0.8f, 0.65f)));
            if (hat) Prim(PrimitiveType.Cylinder, "Hat", root.transform, new Vector3(0, 2.02f, 0), new Vector3(0.6f, 0.06f, 0.6f), Mat("Hat_" + ColorUtility.ToHtmlStringRGB(hatColor), hatColor));
            return body;
        }

        static GameObject BuildPlayer()
        {
            var go = new GameObject("Player");
            var cc = go.AddComponent<CharacterController>();
            cc.center = new Vector3(0, 1f, 0);
            cc.height = 2f;
            cc.radius = 0.4f;

            // trafienia: osobny collider-trigger (CharacterController działa tylko u właściciela)
            var hitbox = new GameObject("Hitbox");
            hitbox.transform.SetParent(go.transform, false);
            var cap = hitbox.AddComponent<CapsuleCollider>();
            cap.isTrigger = true;
            cap.center = new Vector3(0, 1f, 0);
            cap.height = 2f;
            cap.radius = 0.4f;

            BuildHumanoidBody(go, new Color(0.2f, 0.4f, 0.9f), false, Color.black);
            var gun = Prim(PrimitiveType.Cube, "Gun", go.transform, new Vector3(0.35f, 1.25f, 0.45f), new Vector3(0.08f, 0.12f, 0.45f), Mat("Gun", new Color(0.1f, 0.1f, 0.1f)));
            var muzzle = Empty("Muzzle", gun.transform, new Vector3(0, 0, 0.6f));

            go.AddComponent<NetworkObject>();
            go.AddComponent<ClientNetworkTransform>();
            var h = go.AddComponent<Health>();
            h.maxHealth = 100;
            h.faction = Faction.Player;
            h.despawnOnDeath = false;
            go.AddComponent<PlayerStats>();
            go.AddComponent<WantedSystem>();
            go.AddComponent<VehicleInteractor>();
            var w = go.AddComponent<WeaponController>();
            w.muzzle = muzzle;
            go.AddComponent<PlayerMission>();
            go.AddComponent<PlayerController>();
            go.AddComponent<DebugCheats>();
            var hand = Empty("Hand", go.transform, new Vector3(0.32f, 1.25f, 0.35f));
            var visual = go.AddComponent<WeaponVisual>();
            visual.hand = hand;
            visual.fallback = gun;
            go.AddComponent<ShopClient>();
            return go;
        }

        static GameObject BuildNpcBase(string name, Color color, bool hat, Color hatColor)
        {
            var go = new GameObject(name);
            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 1f, 0);
            col.height = 2f;
            col.radius = 0.35f;
            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 2f;
            agent.angularSpeed = 540f;
            agent.acceleration = 12f;
            agent.stoppingDistance = 0.5f;
            BuildHumanoidBody(go, color, hat, hatColor);
            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkTransform>(); // serwerowy – AI liczy serwer
            return go;
        }

        static GameObject BuildPedestrian(Color color, int i)
        {
            var go = BuildNpcBase("Pedestrian_" + i, color, i % 2 == 0, new Color(0.3f, 0.2f, 0.1f));
            var h = go.AddComponent<Health>();
            h.maxHealth = 50;
            h.faction = Faction.Civilian;
            h.despawnDelay = 15f;
            go.AddComponent<PedestrianAI>();
            return go;
        }

        static GameObject BuildCombatNpc(string name, Faction faction, Color color, float hp, float dmg, float acc, int money)
        {
            var go = BuildNpcBase(name, color, true, faction == Faction.Police ? new Color(0.05f, 0.05f, 0.2f) : Color.black);
            Prim(PrimitiveType.Cube, "Gun", go.transform, new Vector3(0.35f, 1.25f, 0.45f), new Vector3(0.08f, 0.12f, 0.45f), Mat("Gun", new Color(0.1f, 0.1f, 0.1f)));
            var h = go.AddComponent<Health>();
            h.maxHealth = hp;
            h.faction = faction;
            h.despawnDelay = 15f;
            var ai = go.AddComponent<CombatNpcAI>();
            ai.damage = dmg;
            ai.accuracy = acc;
            ai.moneyDrop = money;
            return go;
        }

        static GameObject BuildTank()
        {
            var go = BuildCar("Czołg", new Color(0.3f, 0.36f, 0.2f), false, 7f, 6f);   // wolny: ~25 km/h
            var h = go.GetComponent<Health>();
            h.maxHealth = 2000;
            h.despawnDelay = 40f;
            var dark = Mat("TankDark", new Color(0.12f, 0.14f, 0.1f));
            Prim(PrimitiveType.Cube, "Turret", go.transform, new Vector3(0, 1.75f, -0.3f), new Vector3(1.7f, 0.5f, 2.0f), Mat("Car_" + ColorUtility.ToHtmlStringRGB(new Color(0.3f, 0.36f, 0.2f)), new Color(0.3f, 0.36f, 0.2f))).isStatic = false;
            Prim(PrimitiveType.Cylinder, "Barrel", go.transform, new Vector3(0, 1.75f, 1.2f), new Vector3(0.22f, 1.5f, 0.22f), dark, false, new Vector3(90f, 0f, 0f));
            var muzzle = Empty("Muzzle", go.transform, new Vector3(0, 1.75f, 2.8f));
            // gąsienice po bokach (dekoracja)
            Prim(PrimitiveType.Cube, "TrackL", go.transform, new Vector3(-1.05f, 0.45f, 0f), new Vector3(0.35f, 0.5f, 4.6f), dark);
            Prim(PrimitiveType.Cube, "TrackR", go.transform, new Vector3(1.05f, 0.45f, 0f), new Vector3(0.35f, 0.5f, 4.6f), dark);
            go.AddComponent<Tank>().muzzle = muzzle;
            return go;
        }

        static GameObject BuildHelicopter()
        {
            var go = new GameObject("Helikopter");
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1200f;
            rb.linearDamping = 0.3f;
            rb.angularDamping = 4f;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0, 1.5f, 0);
            box.size = new Vector3(2.2f, 2.2f, 5.0f);

            var olive = Mat("Heli_Police", new Color(0.12f, 0.16f, 0.3f));
            Prim(PrimitiveType.Cube, "Cabin", go.transform, new Vector3(0, 1.5f, 0.3f), new Vector3(2.0f, 1.8f, 2.6f), olive);
            Prim(PrimitiveType.Cube, "Glass", go.transform, new Vector3(0, 1.6f, 1.62f), new Vector3(1.7f, 1.2f, 0.05f), Mat("Glass", new Color(0.2f, 0.3f, 0.4f)));
            Prim(PrimitiveType.Cube, "Tail", go.transform, new Vector3(0, 1.9f, -2.8f), new Vector3(0.5f, 0.5f, 3.2f), olive);
            Prim(PrimitiveType.Cube, "TailFin", go.transform, new Vector3(0, 2.5f, -4.2f), new Vector3(0.1f, 0.9f, 0.7f), olive);
            Prim(PrimitiveType.Cube, "SkidL", go.transform, new Vector3(-0.9f, 0.15f, 0f), new Vector3(0.12f, 0.12f, 4.4f), Mat("Metal", new Color(0.2f, 0.2f, 0.2f)));
            Prim(PrimitiveType.Cube, "SkidR", go.transform, new Vector3(0.9f, 0.15f, 0f), new Vector3(0.12f, 0.12f, 4.4f), Mat("Metal", new Color(0.2f, 0.2f, 0.2f)));
            Prim(PrimitiveType.Cylinder, "Mast", go.transform, new Vector3(0, 2.7f, 0f), new Vector3(0.2f, 0.25f, 0.2f), Mat("Metal", new Color(0.2f, 0.2f, 0.2f)));

            var rotor = Empty("Rotor", go.transform, new Vector3(0, 3.0f, 0f));
            Prim(PrimitiveType.Cube, "BladeA", rotor, Vector3.zero, new Vector3(9.0f, 0.05f, 0.35f), Mat("Blade", new Color(0.1f, 0.1f, 0.1f)));
            Prim(PrimitiveType.Cube, "BladeB", rotor, Vector3.zero, new Vector3(0.35f, 0.05f, 9.0f), Mat("Blade", new Color(0.1f, 0.1f, 0.1f)));

            var h = go.AddComponent<Health>();
            h.maxHealth = 600;
            h.faction = Faction.Vehicle;
            h.despawnOnDeath = false; // po zestrzeleniu spada i wraca do życia

            var v = go.AddComponent<Vehicle>();
            v.displayName = "Helikopter";
            v.isPoliceCar = false;
            v.maxSpeed = 24f;
            v.enginePower = 10f;
            v.seat = Empty("Seat", go.transform, new Vector3(-0.45f, 1.3f, 0.6f));
            v.exitPoint = Empty("ExitPoint", go.transform, new Vector3(-2.0f, 0.2f, 0f));

            var heli = go.AddComponent<Helicopter>();
            heli.rotor = rotor;
            heli.groundClearance = 0.4f;

            var no = go.AddComponent<NetworkObject>();
            no.DontDestroyWithOwner = true;
            go.AddComponent<ClientNetworkTransform>();
            go.AddComponent<NetworkRigidbody>();
            return go;
        }

        static GameObject BuildCar(string displayName, Color color, bool police, float maxSpeed, float power)
        {
            var go = new GameObject(displayName);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 1200f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 1.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var bodyCol = go.AddComponent<BoxCollider>();
            bodyCol.center = new Vector3(0, 0.95f, 0);
            bodyCol.size = new Vector3(1.9f, 0.8f, 4.3f);
            var cabinCol = go.AddComponent<BoxCollider>();
            cabinCol.center = new Vector3(0, 1.65f, -0.2f);
            cabinCol.size = new Vector3(1.7f, 0.6f, 2.2f);

            var paint = Mat("Car_" + ColorUtility.ToHtmlStringRGB(color), color);
            Prim(PrimitiveType.Cube, "Body", go.transform, new Vector3(0, 0.95f, 0), new Vector3(1.9f, 0.8f, 4.3f), paint);
            Prim(PrimitiveType.Cube, "Cabin", go.transform, new Vector3(0, 1.65f, -0.2f), new Vector3(1.7f, 0.6f, 2.2f), police ? Mat("PoliceWhite", Color.white) : paint);
            Prim(PrimitiveType.Cube, "Windshield", go.transform, new Vector3(0, 1.65f, 0.92f), new Vector3(1.6f, 0.5f, 0.05f), Mat("Glass", new Color(0.2f, 0.3f, 0.4f)));
            Prim(PrimitiveType.Cube, "HeadlightL", go.transform, new Vector3(-0.65f, 0.95f, 2.16f), new Vector3(0.35f, 0.2f, 0.05f), Mat("Headlight", new Color(1f, 1f, 0.8f), true));
            Prim(PrimitiveType.Cube, "HeadlightR", go.transform, new Vector3(0.65f, 0.95f, 2.16f), new Vector3(0.35f, 0.2f, 0.05f), Mat("Headlight", new Color(1f, 1f, 0.8f), true));

            // Health PRZED Vehicle – inaczej [RequireComponent] dodałby drugi, domyślny Health
            var h = go.AddComponent<Health>();
            h.maxHealth = police ? 400 : 300;
            h.faction = Faction.Vehicle;
            h.despawnDelay = 25f;

            var v = go.AddComponent<Vehicle>();
            v.displayName = displayName;
            v.isPoliceCar = police;
            v.maxSpeed = maxSpeed;
            v.enginePower = power;
            v.seat = Empty("Seat", go.transform, new Vector3(-0.4f, 0.4f, -0.1f));
            v.exitPoint = Empty("ExitPoint", go.transform, new Vector3(-2.2f, 0.3f, 0f));

            Vector3[] wheelPos = { new Vector3(-0.85f, 0.55f, 1.35f), new Vector3(0.85f, 0.55f, 1.35f), new Vector3(-0.85f, 0.55f, -1.35f), new Vector3(0.85f, 0.55f, -1.35f) };
            string[] names = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            var tire = Mat("Tire", new Color(0.08f, 0.08f, 0.08f));
            for (int i = 0; i < 4; i++)
            {
                var wp = Empty(names[i], go.transform, wheelPos[i]);
                v.wheelPoints[i] = wp;
                var vis = Prim(PrimitiveType.Cylinder, "Visual", wp, new Vector3(0, -0.27f, 0), new Vector3(0.7f, 0.12f, 0.7f), tire, false, new Vector3(0, 0, 90));
                v.wheelVisuals[i] = vis.transform;
            }

            if (police)
            {
                var bar = Prim(PrimitiveType.Cube, "LightBar", go.transform, new Vector3(0, 2.0f, -0.2f), new Vector3(1.2f, 0.12f, 0.3f), Mat("LightBar", new Color(0.3f, 0.3f, 0.3f)));
                Prim(PrimitiveType.Cube, "Red", go.transform, new Vector3(-0.35f, 2.07f, -0.2f), new Vector3(0.45f, 0.1f, 0.25f), Mat("SirenRed", Color.red, true));
                Prim(PrimitiveType.Cube, "Blue", go.transform, new Vector3(0.35f, 2.07f, -0.2f), new Vector3(0.45f, 0.1f, 0.25f), Mat("SirenBlue", Color.blue, true));
                var ai = go.AddComponent<PoliceCarAI>();
                ai.sirenRed = MakeLight("SirenRedLight", go.transform, new Vector3(-0.4f, 2.4f, -0.2f), Color.red);
                ai.sirenBlue = MakeLight("SirenBlueLight", go.transform, new Vector3(0.4f, 2.4f, -0.2f), Color.blue);
                Object.DestroyImmediate(bar.GetComponent<Collider>());
            }

            var no = go.AddComponent<NetworkObject>();
            no.DontDestroyWithOwner = true; // gdy kierowca wyjdzie z gry, auto zostaje
            go.AddComponent<ClientNetworkTransform>();
            go.AddComponent<NetworkRigidbody>();
            return go;
        }

        static Light MakeLight(string name, Transform parent, Vector3 pos, Color c)
        {
            var t = Empty(name, parent, pos);
            var l = t.gameObject.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = 12f;
            l.intensity = 4f;
            l.enabled = false;
            return l;
        }

        /// <summary>Nakłada tekstury (apteczka z krzyżem, skrzynka z amunicją, banknot) wygenerowane w Assets/GTA/Textures/Pickups.</summary>
        static void TexturePickup(Material m, Pickup.PickupType type)
        {
            string file = type == Pickup.PickupType.Health ? "medkit" : type == Pickup.PickupType.Ammo ? "ammo" : "money";
            string path = "Assets/GTA/Textures/Pickups/" + file + ".png";
            AssetDatabase.ImportAsset(path);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null) return;
            m.color = Color.white;
            m.mainTexture = tex;
            m.EnableKeyword("_EMISSION");
            m.SetTexture("_EmissionMap", tex);
            m.SetColor("_EmissionColor", Color.white * 0.35f);
            EditorUtility.SetDirty(m);
        }

        static GameObject BuildPickup(string name, Pickup.PickupType type, int amount, Color color)
        {
            var go = new GameObject(name);
            var shape = type == Pickup.PickupType.Money ? PrimitiveType.Cylinder : PrimitiveType.Cube;
            var scale = type == Pickup.PickupType.Money ? new Vector3(0.6f, 0.08f, 0.6f) : new Vector3(0.5f, 0.5f, 0.5f);
            var pm = Mat("Pickup_" + type, color, true);
            TexturePickup(pm, type);
            Prim(shape, "Visual", go.transform, new Vector3(0, 0.8f, 0), scale, pm, false, type == Pickup.PickupType.Money ? new Vector3(90, 0, 0) : (Vector3?)null);
            var l = Empty("Glow", go.transform, new Vector3(0, 1f, 0)).gameObject.AddComponent<Light>();
            l.color = color;
            l.range = 3f;
            l.intensity = 2f;
            go.AddComponent<NetworkObject>();
            var p = go.AddComponent<Pickup>();
            p.type = type;
            p.amount = amount;
            return go;
        }
    }
}
