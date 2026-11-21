using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Generator miasta (część GTASceneBuilder):
    ///  • siatka 10x10 kwartałów z dzielnicami: śródmieście, centrum, przedmieścia, strefa przemysłowa, park, skatepark-skocznie
    ///  • obwodnica (autostrada) połączona z miastem
    ///  • budynki z WNĘTRZAMI: Ammu-Nation, 24/7 (napad), bar, kryjówka, warsztaty Pay'n'Spray
    ///  • latarnie, sieć dróg dla ruchu ulicznego (RoadNetwork), jezdnie oznaczone dla NavMesh (piesi chodzą chodnikami)
    /// Chcesz zmienić układ? Najprościej edytuj tablicę Interiors i funkcję DistrictOf.
    /// </summary>
    public static partial class GTASceneBuilder
    {
        enum District { Downtown, Midtown, Suburb, Industrial, Park, Stunt }

        static float HighwayRing => Half + 60f;   // oś obwodnicy (357)
        const float HighwayWidth = 20f;
        static readonly int[] Connectors = { 2, 5, 8 };

        static District DistrictOf(int bx, int bz)
        {
            if ((bx == 4 || bx == 5) && (bz == 4 || bz == 5)) return District.Park;
            if (bx == 8 && bz == 1) return District.Stunt;
            if (bx >= 7 && bz <= 2) return District.Industrial;
            float d = Mathf.Max(Mathf.Abs(bx - 4.5f), Mathf.Abs(bz - 4.5f));
            if (d <= 1.5f) return District.Downtown;
            if (d <= 2.5f) return District.Midtown;
            return District.Suburb;
        }

        /// <summary>Budynki z wnętrzem: kwartał (bx,bz), działka (lx,lz) w siatce 2x2, typ, nazwa.</summary>
        static readonly (int bx, int bz, int lx, int lz, ShopType type, string name)[] Interiors =
        {
            (4, 3, 0, 1, ShopType.Safehouse, "Kryjówka"),
            (3, 5, 0, 0, ShopType.Bar, "Kasyno Royal"),
            (3, 2, 0, 0, ShopType.Weapons, "Ammu-Nation Południe"),
            (7, 6, 1, 1, ShopType.Weapons, "Ammu-Nation Wschód"),
            (2, 6, 0, 0, ShopType.Store, "24/7 Zachód"),
            (6, 3, 1, 0, ShopType.Store, "24/7 Centrum"),
            (5, 7, 1, 1, ShopType.Store, "24/7 Północ"),
            (3, 6, 1, 0, ShopType.Bar, "Bar Pod Kogutem"),
            (7, 3, 1, 1, ShopType.Bar, "Bar Neon"),
            (2, 3, 0, 0, ShopType.Repair, "Pay'n'Spray Zachód"),
            (6, 7, 1, 1, ShopType.Repair, "Pay'n'Spray Północ"),
        };

        static Vector3 BlockCenter(int bx, int bz) => new Vector3(RoadCenter(bx) + Pitch * 0.5f, 0f, RoadCenter(bz) + Pitch * 0.5f);
        static Vector3 LotCenter(int bx, int bz, int lx, int lz) =>
            BlockCenter(bx, bz) + new Vector3((lx - 0.5f) * BlockSize * 0.5f, 0f, (lz - 0.5f) * BlockSize * 0.5f);

        static void BuildScene(Prefabs pf)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Random.InitState(12345);
            _packBuildings = 0; _fallbackBuildings = 0; _nightBuildings = 0; PackCache.Clear();

            // ---- kamera, światło, mgła ----
            var cam = Camera.main;
            cam.gameObject.AddComponent<PlayerCamera>();
            cam.farClipPlane = 1000f;
            cam.transform.position = new Vector3(0, 80, -120);
            cam.transform.rotation = Quaternion.Euler(35, 0, 0);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 220f;
            RenderSettings.fogEndDistance = 750f;
            RenderSettings.fogColor = new Color(0.7f, 0.78f, 0.85f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            var sun = Object.FindAnyObjectByType<Light>();

            var city = new GameObject("City").transform;
            float ground = 1200f;   // dalsza mapa: ziemia sięga do ±600 m (góry zaczynają się ok. 560 m)
            Prim(PrimitiveType.Cube, "Ground", city, new Vector3(0, -0.5f, 0), new Vector3(ground, 1f, ground), Mat("Grass", new Color(0.3f, 0.45f, 0.25f)), true).isStatic = true;

            BuildRoads(city);
            var shops = new List<ShopZone>();
            BuildBlocks(city, shops);
            BuildLamps(city);
            BuildOutskirts(city);

            // ---- NavMesh (jezdnie = obszar "Road", piesi go unikają) ----
            var surface = city.gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            string navPath = Root + "/CityNavMesh.asset";
            AssetDatabase.DeleteAsset(navPath);
            AssetDatabase.CreateAsset(surface.navMeshData, navPath);

            // ---- sieć dróg dla ruchu ulicznego ----
            BuildRoadNetwork();

            // ---- miejsca parkingowe ----
            var parked = new GameObject("ParkedCars").transform;
            var parkedSpots = new List<Transform>();
            for (int n = 0; n < 32; n++)
            {
                int road = Random.Range(0, Blocks + 1);
                int seg = Random.Range(0, Blocks);
                float along = RoadCenter(seg) + RoadWidth * 0.5f + Random.Range(8f, BlockSize - 8f);
                bool alongX = Random.value > 0.5f;
                float side = Random.value > 0.5f ? 5.4f : -5.4f;
                var t = Empty("Spot_" + n, parked, alongX
                    ? new Vector3(along, 0.3f, RoadCenter(road) + side)
                    : new Vector3(RoadCenter(road) + side, 0.3f, along));
                t.rotation = Quaternion.Euler(0, alongX ? 90 : 0, 0);
                parkedSpots.Add(t);
            }

            // ---- spawn graczy: park + przed kryjówką ----
            var spawns = new GameObject("SpawnPoints").transform;
            foreach (var sp in new[] { new Vector3(8, 0.2f, 15), new Vector3(-8, 0.2f, -15), new Vector3(8, 0.2f, -15), new Vector3(-8, 0.2f, 15) })
                Empty("Spawn", spawns, sp).gameObject.AddComponent<SpawnPoint>();
            foreach (var z in shops)
                if (z.type == ShopType.Safehouse)
                {
                    Empty("Spawn_Safehouse", spawns, z.transform.parent.TransformPoint(new Vector3(11.5f, 0.2f, 0f))).gameObject.AddComponent<SpawnPoint>();
                    // łóżko: tu odradzasz się po śmierci (pozycja ≈ materac łóżka w kryjówce, głową do tylnej ściany)
                    var bedT = Empty("Spawn_Bed", spawns, z.transform.parent.TransformPoint(new Vector3(-7.4f, 1.55f, -6.8f)));
                    bedT.rotation = z.transform.parent.rotation * Quaternion.Euler(0, 90, 0);
                    bedT.gameObject.AddComponent<SpawnPoint>().bed = true;
                }

            // ---- znajdźki na chodnikach ----
            var pickupsRoot = new GameObject("Pickups").transform;
            var spots = new List<PickupSpawner.Spot>();
            GameObject[] pickupPrefabs = { pf.healthPickup, pf.ammoPickup, pf.moneyPickup };
            for (int n = 0; n < 30; n++)
            {
                var c = BlockCenter(Random.Range(0, Blocks), Random.Range(0, Blocks));
                float edge = BlockSize * 0.5f - 1.2f;
                Vector3 off = Random.value > 0.5f
                    ? new Vector3(Random.value > 0.5f ? edge : -edge, 0.2f, Random.Range(-16f, 16f))
                    : new Vector3(Random.Range(-16f, 16f), 0.2f, Random.value > 0.5f ? edge : -edge);
                spots.Add(new PickupSpawner.Spot { point = Empty("PickupSpot_" + n, pickupsRoot, c + off), prefab = pickupPrefabs[n % 3], respawnTime = 60f });
            }

            // ---- misje ----
            var missionsRoot = new GameObject("Missions").transform;
            BuildMissions(missionsRoot, pf);

            // ---- menedżery ----
            var managers = new GameObject("GameManagers");
            var nm = managers.AddComponent<NetworkManager>();
            var utp = managers.AddComponent<UnityTransport>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = utp,
                PlayerPrefab = pf.player,
                TickRate = 30,
                ConnectionApproval = false,
            };
            var prefabList = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            foreach (var go in pf.all)
                if (go != pf.player) prefabList.Add(new NetworkPrefab { Prefab = go });
            string listPath = Root + "/MiniGTANetworkPrefabs.asset";
            AssetDatabase.DeleteAsset(listPath);
            AssetDatabase.CreateAsset(prefabList, listPath);
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabList);

            managers.AddComponent<NetworkUI>();
            managers.AddComponent<HUD>();
            var dn = managers.AddComponent<DayNightCycle>();
            dn.sun = sun;

            var mm = managers.AddComponent<MissionManager>();
            mm.enemyPrefab = pf.gang;
            mm.vehiclePrefab = pf.cars[2];

            var peds = managers.AddComponent<PedestrianSpawner>();
            peds.pedestrianPrefabs = pf.peds.ToArray();
            peds.maxPedestrians = 35;

            var ts = managers.AddComponent<TrafficSpawner>();
            ts.carPrefabs = pf.cars.ToArray();
            ts.parkedSpots = parkedSpots.ToArray();
            ts.driverPedestrianPrefab = pf.peds[0];
            ts.maxTrafficCars = 18;

            var ps = managers.AddComponent<PoliceSpawner>();
            ps.officerPrefab = pf.officer;
            ps.swatPrefab = pf.swat;
            ps.armyPrefab = pf.army;
            ps.policeCarPrefab = pf.policeCar;
            ps.armyCarPrefab = pf.armyCar;
            ps.tankPrefab = pf.tank;
            ps.helicopterPrefab = pf.heli;
            managers.AddComponent<TrafficViolations>(); // przejazd na czerwonym = gwiazdka

            var pks = managers.AddComponent<PickupSpawner>();
            pks.spots = spots.ToArray();

            // zaparkowany radiowóz do kradzieży (2 gwiazdki!) – obok kryjówki
            var policeSpot = Empty("PoliceSpot", parked, new Vector3(RoadCenter(4) + 5.4f, 0.3f, RoadCenter(3) + 20f));
            ts.parkedPoliceSpots = new[] { policeSpot };
            ts.policeCarPrefab = pf.policeCar;

            ValidateCity(shops);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == ScenePath)) list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();

            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
        }

        // =====================================================================
        //  DROGI
        // =====================================================================

        static void RoadStrip(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            var r = Prim(PrimitiveType.Cube, name, parent, center, size, mat);
            r.isStatic = true;
            // obszar NavMesh "jezdnia" – piesi płacą za nią więcej, więc trzymają się chodników
            var vol = Empty(name + "_NavArea", parent, new Vector3(center.x, 0f, center.z)).gameObject.AddComponent<NavMeshModifierVolume>();
            vol.size = new Vector3(size.x, 4f, size.z);
            vol.center = Vector3.zero;
            vol.area = GameUtil.RoadArea;
        }

        static void BuildRoads(Transform city)
        {
            var roads = new GameObject("Roads").transform;
            roads.SetParent(city, false);
            var asphalt = Mat("Asphalt", new Color(0.18f, 0.18f, 0.2f));
            var line = Mat("RoadLineYellow", new Color(0.95f, 0.8f, 0.2f));
            float len = Half * 2f;

            for (int i = 0; i <= Blocks; i++)
            {
                float c = RoadCenter(i);
                RoadStrip(roads, "Road_X" + i, new Vector3(0, 0.01f, c), new Vector3(len, 0.02f, RoadWidth), asphalt);
                RoadStrip(roads, "Road_Z" + i, new Vector3(c, 0.012f, 0), new Vector3(RoadWidth, 0.02f, len), asphalt);
                // linia środkowa między skrzyżowaniami (kończy się przed linią zatrzymania)
                for (int s = 0; s < Blocks; s++)
                {
                    float mid = RoadCenter(s) + Pitch * 0.5f;
                    Prim(PrimitiveType.Cube, "Line", roads, new Vector3(mid, 0.025f, c), new Vector3(BlockSize - 10f, 0.01f, 0.3f), line).isStatic = true;
                    Prim(PrimitiveType.Cube, "Line", roads, new Vector3(c, 0.026f, mid), new Vector3(0.3f, 0.01f, BlockSize - 10f), line).isStatic = true;
                }
            }
            // przejścia dla pieszych, linie zatrzymania i sygnalizacja świetlna (GTASceneBuilder.Streets.cs)
            BuildCrossings(roads);

            // obwodnica
            float H = HighwayRing, W = HighwayWidth, full = H * 2f + W;
            RoadStrip(roads, "Highway_N", new Vector3(0, 0.01f, H), new Vector3(full, 0.02f, W), asphalt);
            RoadStrip(roads, "Highway_S", new Vector3(0, 0.01f, -H), new Vector3(full, 0.02f, W), asphalt);
            RoadStrip(roads, "Highway_E", new Vector3(H, 0.012f, 0), new Vector3(W, 0.02f, full), asphalt);
            RoadStrip(roads, "Highway_W", new Vector3(-H, 0.012f, 0), new Vector3(W, 0.02f, full), asphalt);
            Prim(PrimitiveType.Cube, "HLine", roads, new Vector3(0, 0.025f, H), new Vector3(full - 30, 0.01f, 0.3f), line);
            Prim(PrimitiveType.Cube, "HLine", roads, new Vector3(0, 0.025f, -H), new Vector3(full - 30, 0.01f, 0.3f), line);
            Prim(PrimitiveType.Cube, "HLine", roads, new Vector3(H, 0.026f, 0), new Vector3(0.3f, 0.01f, full - 30), line);
            Prim(PrimitiveType.Cube, "HLine", roads, new Vector3(-H, 0.026f, 0), new Vector3(0.3f, 0.01f, full - 30), line);

            // bariery na zewnętrznej krawędzi obwodnicy
            var barrier = Mat("Barrier", new Color(0.75f, 0.75f, 0.72f));
            float bOff = H + W * 0.5f + 0.6f, bLen = full + 2f;
            Prim(PrimitiveType.Cube, "Barrier_N", roads, new Vector3(0, 0.5f, bOff), new Vector3(bLen, 1f, 0.6f), barrier, true);
            Prim(PrimitiveType.Cube, "Barrier_S", roads, new Vector3(0, 0.5f, -bOff), new Vector3(bLen, 1f, 0.6f), barrier, true);
            Prim(PrimitiveType.Cube, "Barrier_E", roads, new Vector3(bOff, 0.5f, 0), new Vector3(0.6f, 1f, bLen), barrier, true);
            Prim(PrimitiveType.Cube, "Barrier_W", roads, new Vector3(-bOff, 0.5f, 0), new Vector3(0.6f, 1f, bLen), barrier, true);

            // zjazdy miasto ↔ obwodnica
            float cLen = H - W * 0.5f - Half, cMid = Half + cLen * 0.5f;
            foreach (int k in Connectors)
            {
                float c = RoadCenter(k);
                RoadStrip(roads, "Ramp_N" + k, new Vector3(c, 0.014f, cMid), new Vector3(RoadWidth, 0.02f, cLen), asphalt);
                RoadStrip(roads, "Ramp_S" + k, new Vector3(c, 0.014f, -cMid), new Vector3(RoadWidth, 0.02f, cLen), asphalt);
                RoadStrip(roads, "Ramp_E" + k, new Vector3(cMid, 0.014f, c), new Vector3(cLen, 0.02f, RoadWidth), asphalt);
                RoadStrip(roads, "Ramp_W" + k, new Vector3(-cMid, 0.014f, c), new Vector3(cLen, 0.02f, RoadWidth), asphalt);
            }

            // skocznie na poboczu obwodnicy
            AddRamp(roads, new Vector3(-150f, 0.02f, H + 7f), Vector3.right, 14f, 4.5f, 16f);
            AddRamp(roads, new Vector3(150f, 0.02f, -H - 7f), Vector3.left, 14f, 4.5f, 16f);
        }

        static void BuildRoadNetwork()
        {
            var go = new GameObject("RoadNetwork");
            var net = go.AddComponent<RoadNetwork>();
            var nodes = new List<Vector3>();
            var ea = new List<int>();
            var eb = new List<int>();
            var sp = new List<float>();
            int N = Blocks + 1;

            for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
                nodes.Add(new Vector3(RoadCenter(i), 0.3f, RoadCenter(j)));
            int G(int i, int j) => i * N + j;
            for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                if (i + 1 < N) { ea.Add(G(i, j)); eb.Add(G(i + 1, j)); sp.Add(12f); }
                if (j + 1 < N) { ea.Add(G(i, j)); eb.Add(G(i, j + 1)); sp.Add(12f); }
            }

            // obwodnica: węzły w narożnikach i przy zjazdach
            float H = HighwayRing;
            int Add(Vector3 p) { nodes.Add(p); return nodes.Count - 1; }
            var north = new SortedDictionary<float, int>();
            var south = new SortedDictionary<float, int>();
            var east = new SortedDictionary<float, int>();
            var west = new SortedDictionary<float, int>();
            int ne = Add(new Vector3(H, 0.3f, H)), nw = Add(new Vector3(-H, 0.3f, H));
            int se = Add(new Vector3(H, 0.3f, -H)), sw = Add(new Vector3(-H, 0.3f, -H));
            north[-H] = nw; north[H] = ne; south[-H] = sw; south[H] = se;
            east[-H] = se; east[H] = ne; west[-H] = sw; west[H] = nw;
            foreach (int k in Connectors)
            {
                float c = RoadCenter(k);
                int n = Add(new Vector3(c, 0.3f, H)); north[c] = n;
                int s = Add(new Vector3(c, 0.3f, -H)); south[c] = s;
                int e = Add(new Vector3(H, 0.3f, c)); east[c] = e;
                int w = Add(new Vector3(-H, 0.3f, c)); west[c] = w;
                // zjazdy z miasta
                ea.Add(G(k, Blocks)); eb.Add(n); sp.Add(15f);
                ea.Add(G(k, 0)); eb.Add(s); sp.Add(15f);
                ea.Add(G(Blocks, k)); eb.Add(e); sp.Add(15f);
                ea.Add(G(0, k)); eb.Add(w); sp.Add(15f);
            }
            foreach (var side in new[] { north, south, east, west })
            {
                int prev = -1;
                foreach (var kv in side)
                {
                    if (prev >= 0) { ea.Add(prev); eb.Add(kv.Value); sp.Add(24f); }
                    prev = kv.Value;
                }
            }

            net.nodes = nodes.ToArray();
            net.edgeA = ea.ToArray();
            net.edgeB = eb.ToArray();
            net.edgeSpeed = sp.ToArray();
            net.laneOffset = 3f;
        }

        static void AddRamp(Transform parent, Vector3 lowEnd, Vector3 dir, float length, float width, float angle)
        {
            const float thick = 0.6f;
            var rot = Quaternion.LookRotation(dir) * Quaternion.Euler(-angle, 0f, 0f);
            Vector3 fwd = rot * Vector3.forward, up = rot * Vector3.up;
            Vector3 center = lowEnd + fwd * (length * 0.5f) - up * (thick * 0.5f);
            var ramp = Prim(PrimitiveType.Cube, "StuntRamp", parent, center, new Vector3(width, thick, length), Mat("Ramp", new Color(0.85f, 0.55f, 0.15f)), true);
            ramp.transform.rotation = rot;
            ramp.isStatic = true;
            // podpora pod wysokim końcem
            Vector3 high = lowEnd + fwd * length;
            Prim(PrimitiveType.Cube, "RampSupport", parent, new Vector3(high.x, high.y * 0.5f, high.z) - dir * 0.6f, new Vector3(width, Mathf.Max(0.1f, high.y), 1.2f), Mat("RampSupport", new Color(0.3f, 0.3f, 0.3f)), true).isStatic = true;
        }

        static void BuildLamps(Transform city)
        {
            var root = new GameObject("StreetLamps").transform;
            root.SetParent(city, false);
            var pole = Mat("LampPole", new Color(0.2f, 0.2f, 0.22f));
            float off = RoadWidth * 0.5f + 0.6f; // tuż przy krawężniku (dalej stoją już budynki)
            for (int i = 0; i <= Blocks; i++)
            for (int s = 0; s < Blocks; s++)
            {
                float c = RoadCenter(i), mid = RoadCenter(s) + Pitch * 0.5f;
                float side = (i + s) % 2 == 0 ? off : -off;
                AddLamp(root, new Vector3(mid, 0f, c + side), new Vector3(0, 0, -Mathf.Sign(side)), pole);
                AddLamp(root, new Vector3(c + side, 0f, mid), new Vector3(-Mathf.Sign(side), 0, 0), pole);
            }
            // obwodnica
            float H = HighwayRing, ho = HighwayWidth * 0.5f + 1.5f;
            for (float t = -H; t <= H; t += 70f)
            {
                AddLamp(root, new Vector3(t, 0f, H + ho), Vector3.back, pole);
                AddLamp(root, new Vector3(t, 0f, -H - ho), Vector3.forward, pole);
                AddLamp(root, new Vector3(H + ho, 0f, t), Vector3.left, pole);
                AddLamp(root, new Vector3(-H - ho, 0f, t), Vector3.right, pole);
            }
        }

        static void AddLamp(Transform parent, Vector3 pos, Vector3 towardRoad, Material poleMat)
        {
            var lamp = new GameObject("Lamp").transform;
            lamp.SetParent(parent, false);
            lamp.position = pos;
            Prim(PrimitiveType.Cylinder, "Pole", lamp, new Vector3(0, 3f, 0), new Vector3(0.18f, 3f, 0.18f), poleMat).isStatic = true;
            Prim(PrimitiveType.Cube, "Arm", lamp, new Vector3(0, 5.9f, 0) + towardRoad * 0.7f, new Vector3(0.12f, 0.12f, 0.12f) + new Vector3(Mathf.Abs(towardRoad.x), 0, Mathf.Abs(towardRoad.z)) * 1.4f, poleMat).isStatic = true;
            var bulb = Prim(PrimitiveType.Sphere, "Bulb", lamp, new Vector3(0, 5.75f, 0) + towardRoad * 1.4f, new Vector3(0.5f, 0.3f, 0.5f), Mat("LampBulb", new Color(0.6f, 0.6f, 0.55f)));
            var lightGo = Empty("Light", lamp, new Vector3(0, 5.4f, 0) + towardRoad * 1.4f);
            var l = lightGo.gameObject.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 22f;
            l.intensity = 2.2f;
            l.color = new Color(1f, 0.85f, 0.55f);
            l.shadows = LightShadows.None;
            l.enabled = false;
            var sl = lamp.gameObject.AddComponent<StreetLamp>();
            sl.lampLight = l;
            sl.bulb = bulb.GetComponent<Renderer>();
        }

        // =====================================================================
        //  KWARTAŁY
        // =====================================================================

        static void BuildBlocks(Transform city, List<ShopZone> shops)
        {
            var buildings = new GameObject("Buildings").transform;
            buildings.SetParent(city, false);
            var lawn = Mat("Lawn", new Color(0.32f, 0.55f, 0.28f));

            var interiorLots = new Dictionary<(int, int, int, int), (ShopType, string)>();
            foreach (var it in Interiors) interiorLots[(it.bx, it.bz, it.lx, it.lz)] = (it.type, it.name);

            for (int bx = 0; bx < Blocks; bx++)
            for (int bz = 0; bz < Blocks; bz++)
            {
                Vector3 c = BlockCenter(bx, bz);
                var district = DistrictOf(bx, bz);
                float ring = district == District.Suburb ? 3f : 3.5f;
                BuildSidewalk(city, bx, bz, c, ring, district != District.Park);
                if (district != District.Stunt) { AddStreetFurniture(city, c); AddPackStreetProps(city, c); }
                if (district == District.Downtown || district == District.Midtown)
                    Prim(PrimitiveType.Cube, "Plaza", city, c + Vector3.up * 0.125f, new Vector3(BlockSize - ring * 2f, 0.01f, BlockSize - ring * 2f),
                        Mat("Plaza", new Color(0.5f, 0.49f, 0.47f))).isStatic = true;
                switch (district)
                {
                    case District.Park: BuildPark(city, c, bx == 4 && bz == 4); continue;
                    case District.Stunt: BuildStuntPark(city, c); continue;
                    case District.Industrial: BuildIndustrial(buildings, c); continue;
                    case District.Suburb:
                        Prim(PrimitiveType.Cube, "Lawn", city, c + Vector3.up * 0.125f, new Vector3(BlockSize - 6f, 0.01f, BlockSize - 6f), lawn).isStatic = true;
                        BuildSuburb(buildings, c);
                        continue;
                }

                // śródmieście / centrum: 2x2 działki
                for (int lx = 0; lx < 2; lx++)
                for (int lz = 0; lz < 2; lz++)
                {
                    Vector3 lot = LotCenter(bx, bz, lx, lz);
                    bool downtown = district == District.Downtown;
                    if (interiorLots.TryGetValue((bx, bz, lx, lz), out var info))
                    {
                        float h = info.Item1 == ShopType.Repair ? 6.4f : (downtown ? Random.Range(30f, 60f) : Random.Range(16f, 28f));
                        shops.Add(BuildEnterable(buildings, lot, 20f, 20f, h, lx == 0 ? -1 : 1, info.Item1, info.Item2));
                        continue;
                    }
                    if (Random.value < (downtown ? 0.05f : 0.15f)) continue; // plac
                    float w = Random.Range(16.5f, 19.5f), d = Random.Range(16.5f, 19.5f);
                    if (TryPackBuilding(buildings, lot, w, d, downtown, 0.12f, downtown ? 140f : 58f) != null) continue;
                    float hgt = downtown ? Random.Range(45f, 125f) : Random.Range(16f, 40f);
                    Color col = downtown
                        ? Color.Lerp(new Color(0.35f, 0.42f, 0.5f), new Color(0.6f, 0.65f, 0.7f), Random.value)
                        : Color.Lerp(new Color(0.55f, 0.45f, 0.38f), new Color(0.8f, 0.76f, 0.66f), Random.value);
                    var b = Prim(PrimitiveType.Cube, "Building", buildings, lot + Vector3.up * (hgt * 0.5f + 0.12f), new Vector3(w, hgt, d),
                        Mat("Building_" + ColorUtility.ToHtmlStringRGB(Quantize(col)), Quantize(col)), true);
                    b.isStatic = true;
                    Prim(PrimitiveType.Cube, "Roof", buildings, lot + Vector3.up * (hgt + 0.42f), new Vector3(w * 0.9f, 0.6f, d * 0.9f), Mat("Roof", new Color(0.25f, 0.25f, 0.25f))).isStatic = true;
                    if (downtown) AddWindowBands(buildings, lot, w, d, hgt);
                }
            }
        }

        static Color Quantize(Color c) => new Color(Mathf.Round(c.r * 8f) / 8f, Mathf.Round(c.g * 8f) / 8f, Mathf.Round(c.b * 8f) / 8f);

        /// <summary>Paski "okien" na wieżowcach (tanio: kilka płaskich prostopadłościanów).</summary>
        static void AddWindowBands(Transform parent, Vector3 lot, float w, float d, float h)
        {
            var glass = Mat("WindowBand", new Color(0.25f, 0.35f, 0.45f));
            for (float y = 6f; y < h - 3f; y += 6f)
                Prim(PrimitiveType.Cube, "Windows", parent, lot + Vector3.up * (y + 0.12f), new Vector3(w + 0.1f, 1.6f, d + 0.1f), glass).isStatic = true;
        }

        static void BuildSuburb(Transform parent, Vector3 c)
        {
            var roofMat = Mat("HouseRoof", new Color(0.55f, 0.22f, 0.15f));
            Color[] walls = { new Color(0.9f, 0.85f, 0.7f), new Color(0.75f, 0.85f, 0.9f), new Color(0.95f, 0.75f, 0.6f), new Color(0.85f, 0.9f, 0.75f) };
            float step = BlockSize / 3f;
            for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
            {
                if (x == 1 && z == 1) { AddTree(parent, c + new Vector3(Random.Range(-3f, 3f), 0, Random.Range(-3f, 3f))); continue; }
                Vector3 lot = c + new Vector3((x - 1) * step, 0, (z - 1) * step);
                if (Random.value < 0.15f) { AddTree(parent, lot); continue; }
                float w = Random.Range(9f, 11f), d = Random.Range(9f, 11f), h = Random.Range(4.5f, 7f);
                if (TryPackBuilding(parent, lot, w + 2f, d + 2f, false, 0.12f, 20f) != null) { if (Random.value < 0.5f) AddTree(parent, lot + new Vector3(Random.value > 0.5f ? 6.5f : -6.5f, 0, Random.Range(-4f, 4f))); continue; }
                var wall = walls[Random.Range(0, walls.Length)];
                Prim(PrimitiveType.Cube, "House", parent, lot + Vector3.up * (h * 0.5f + 0.12f), new Vector3(w, h, d), Mat("House_" + ColorUtility.ToHtmlStringRGB(wall), wall), true).isStatic = true;
                // dach dwuspadowy
                float roofY = h + 0.12f + w * 0.22f;
                Prim(PrimitiveType.Cube, "RoofL", parent, lot + new Vector3(-w * 0.25f, roofY, 0), new Vector3(w * 0.62f, 0.25f, d + 0.6f), roofMat, false, new Vector3(0, 0, 32)).isStatic = true;
                Prim(PrimitiveType.Cube, "RoofR", parent, lot + new Vector3(w * 0.25f, roofY, 0), new Vector3(w * 0.62f, 0.25f, d + 0.6f), roofMat, false, new Vector3(0, 0, -32)).isStatic = true;
                if (Random.value < 0.5f) AddTree(parent, lot + new Vector3(Random.value > 0.5f ? 5.5f : -5.5f, 0, Random.Range(-4f, 4f)));
                if (Random.value < 0.6f) AddBush(parent, lot + new Vector3(Random.Range(-5f, 5f), 0, Random.value > 0.5f ? d * 0.5f + 1.2f : -d * 0.5f - 1.2f));
            }
        }

        /// <summary>Krzew z paczki – tylko na trawniku wewnątrz kwartału, w małej skali (bez wchodzenia na chodnik).</summary>
        static void AddBush(Transform parent, Vector3 p)
        {
            string[] bushes = { "Bush_06", "Bush_07", "Bush_10" };
            PlacePack(bushes[Random.Range(0, bushes.Length)], parent, KeepInsideBlock(p, 2f), Random.Range(0, 360), 1.2f);
        }

        static void BuildIndustrial(Transform parent, Vector3 c)
        {
            var wh = Mat("Warehouse", new Color(0.5f, 0.52f, 0.5f));
            var roof = Mat("WarehouseRoof", new Color(0.35f, 0.38f, 0.4f));
            foreach (float zo in new[] { -11f, 11f })
            {
                float h = Random.Range(11f, 16f);
                if (TryPackBuilding(parent, c + new Vector3(0, 0, zo), 38f, 15f, false, 0.12f, h + 2f, 0f, true) != null) continue;
                Prim(PrimitiveType.Cube, "Warehouse", parent, c + new Vector3(0, h * 0.5f + 0.12f, zo), new Vector3(38f, h, 15f), wh, true).isStatic = true;
                Prim(PrimitiveType.Cube, "WarehouseRoof", parent, c + new Vector3(0, h + 0.4f, zo), new Vector3(38.5f, 0.6f, 15.5f), roof).isStatic = true;
            }
            Color[] cont = { new Color(0.7f, 0.2f, 0.15f), new Color(0.15f, 0.35f, 0.6f), new Color(0.2f, 0.5f, 0.25f), new Color(0.8f, 0.6f, 0.15f) };
            for (int n = 0; n < 3; n++)
            {
                var col = cont[Random.Range(0, cont.Length)];
                Prim(PrimitiveType.Cube, "Container", parent, c + new Vector3(Random.Range(-13f, 13f), 1.42f, Random.Range(-0.8f, 0.8f)), new Vector3(6f, 2.6f, 2.5f),
                    Mat("Container_" + ColorUtility.ToHtmlStringRGB(col), col), true).isStatic = true;
            }
        }

        static void BuildStuntPark(Transform parent, Vector3 c)
        {
            Prim(PrimitiveType.Cube, "StuntFloor", parent, c + Vector3.up * 0.13f, new Vector3(BlockSize - 7f, 0.02f, BlockSize - 7f), Mat("Concrete", new Color(0.62f, 0.6f, 0.56f)));
            AddRamp(parent, c + new Vector3(-12f, 0.12f, -8f), Vector3.forward, 10f, 5f, 18f);
            AddRamp(parent, c + new Vector3(10f, 0.12f, 18f), Vector3.back, 12f, 5f, 22f);
            AddRamp(parent, c + new Vector3(-6f, 0.12f, 6f), Vector3.right, 9f, 4f, 14f);
            var sign = new GameObject("StuntSign");
            sign.transform.SetParent(parent, false);
            sign.transform.position = c + new Vector3(0, 6f, -21f);
            var tm = sign.AddComponent<TextMesh>();
            tm.text = "STUNT PARK\nskoki = $$$";
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.25f;
            tm.fontSize = 48;
            tm.color = new Color(1f, 0.6f, 0.1f);
        }

        static void BuildPark(Transform city, Vector3 center, bool fountain)
        {
            var grass = Mat("ParkGrass", new Color(0.25f, 0.55f, 0.25f));
            Prim(PrimitiveType.Cube, "ParkGrass", city, center + Vector3.up * 0.13f, new Vector3(BlockSize - 7, 0.02f, BlockSize - 7), grass).isStatic = true;
            for (int i = 0; i < 7; i++)
            {
                Vector3 p = center + new Vector3(Random.Range(-17f, 17f), 0, Random.Range(-17f, 17f));
                if (Mathf.Abs(p.x - center.x) < 7 && Mathf.Abs(p.z - center.z) < 7) continue;
                AddTree(city, p);
            }
            if (fountain)
            {
                Prim(PrimitiveType.Cylinder, "FountainBasin", city, center + Vector3.up * 0.5f, new Vector3(7f, 0.4f, 7f), Mat("Stone", new Color(0.7f, 0.7f, 0.68f)), true).isStatic = true;
                Prim(PrimitiveType.Cylinder, "FountainWater", city, center + Vector3.up * 0.92f, new Vector3(6.2f, 0.02f, 6.2f), Mat("Water", new Color(0.2f, 0.5f, 0.8f), true));
                Prim(PrimitiveType.Cylinder, "FountainPillar", city, center + Vector3.up * 1.5f, new Vector3(0.8f, 1.2f, 0.8f), Mat("Stone", new Color(0.7f, 0.7f, 0.68f)), true).isStatic = true;
            }
            // ławki
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                Vector3 p = center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 9f;
                Prim(PrimitiveType.Cube, "Bench", city, p + Vector3.up * 0.55f, new Vector3(2.2f, 0.15f, 0.6f), Mat("Wood", new Color(0.45f, 0.3f, 0.15f)), true, new Vector3(0, -i * 90f + 90f, 0)).isStatic = true;
            }
        }

        /// <summary>Przesuwa punkt do wnętrza kwartału – poza chodnik (pierścień) i jezdnię.</summary>
        static Vector3 KeepInsideBlock(Vector3 p, float margin = 2f)
        {
            for (int bx = 0; bx < Blocks; bx++)
            for (int bz = 0; bz < Blocks; bz++)
            {
                Vector3 c = BlockCenter(bx, bz);
                float half = BlockSize * 0.5f;
                if (Mathf.Abs(p.x - c.x) > half + 0.01f || Mathf.Abs(p.z - c.z) > half + 0.01f) continue;
                float lim = half - 3.5f - margin;
                return new Vector3(c.x + Mathf.Clamp(p.x - c.x, -lim, lim), p.y, c.z + Mathf.Clamp(p.z - c.z, -lim, lim));
            }
            return p;
        }

        /// <summary>
        /// Dalsza mapa: pas za obwodnicą aż do gór – pola uprawne, gospodarstwa, drogi gruntowe przedłużające wyjazdy z miasta,
        /// lasy i głazy. Deterministyczne (stały seed), bez wpływu na sieć dróg AI.
        /// </summary>
        static void BuildOutskirts(Transform city)
        {
            var oldState = Random.state;
            Random.InitState(9051);
            var root = new GameObject("Outskirts").transform;
            root.SetParent(city, false);
            float inner = HighwayRing + HighwayWidth * 0.5f + 6f, outer = 585f;

            var dirt = Mat("Dirt", new Color(0.45f, 0.34f, 0.22f));
            var fieldMats = new[]
            {
                Mat("FieldWheat", new Color(0.78f, 0.66f, 0.28f)), Mat("FieldGreen", new Color(0.35f, 0.58f, 0.24f)),
                Mat("FieldSoil", new Color(0.36f, 0.25f, 0.16f)), Mat("FieldCorn", new Color(0.6f, 0.62f, 0.25f)),
            };

            // drogi gruntowe: przedłużenia wyjazdów (2, 5, 8) w cztery strony
            var corridors = new List<float>();
            foreach (int k in Connectors)
            {
                float c = RoadCenter(k);
                corridors.Add(c);
                float len = outer - inner, mid = inner + len * 0.5f;
                Prim(PrimitiveType.Cube, "DirtRoad_N" + k, root, new Vector3(c, 0.03f, mid), new Vector3(8f, 0.03f, len), dirt).isStatic = true;
                Prim(PrimitiveType.Cube, "DirtRoad_S" + k, root, new Vector3(c, 0.03f, -mid), new Vector3(8f, 0.03f, len), dirt).isStatic = true;
                Prim(PrimitiveType.Cube, "DirtRoad_E" + k, root, new Vector3(mid, 0.03f, c), new Vector3(len, 0.03f, 8f), dirt).isStatic = true;
                Prim(PrimitiveType.Cube, "DirtRoad_W" + k, root, new Vector3(-mid, 0.03f, c), new Vector3(len, 0.03f, 8f), dirt).isStatic = true;
            }
            bool NearCorridor(Vector3 p, float margin)
            {
                foreach (float c in corridors)
                    if (Mathf.Abs(p.x - c) < margin || Mathf.Abs(p.z - c) < margin) return true;
                return false;
            }
            bool InCity(Vector3 p, float pad) => Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)) < inner + pad;

            // pola uprawne i gospodarstwa
            var rects = new List<Rect>();
            int placedFields = 0;
            for (int t = 0; t < 400 && placedFields < 46; t++)
            {
                var p = new Vector3(Random.Range(-outer + 40f, outer - 40f), 0f, Random.Range(-outer + 40f, outer - 40f));
                float w = Random.Range(40f, 90f), d = Random.Range(40f, 90f);
                var r = new Rect(p.x - w / 2f - 6f, p.z - d / 2f - 6f, w + 12f, d + 12f);
                if (InCity(p, w * 0.5f + d * 0.5f + 8f) && (Mathf.Max(Mathf.Abs(p.x) - w * 0.5f, Mathf.Abs(p.z) - d * 0.5f) < inner + 6f)) continue;
                if (NearCorridor(p, 10f + Mathf.Max(w, d) * 0.5f)) continue;
                bool overlap = false;
                foreach (var o in rects) if (o.Overlaps(r)) { overlap = true; break; }
                if (overlap) continue;
                rects.Add(r); placedFields++;
                var f = Prim(PrimitiveType.Cube, "Field", root, new Vector3(p.x, 0.03f, p.z), new Vector3(w, 0.04f, d), fieldMats[Random.Range(0, fieldMats.Length)]);
                f.isStatic = true;
                if (Random.value < 0.22f)   // gospodarstwo na polu: dom + stodoła
                {
                    var wall = Mat("FarmWall", new Color(0.82f, 0.72f, 0.58f));
                    var roof = Mat("FarmRoof", new Color(0.6f, 0.22f, 0.16f));
                    var hp = new Vector3(p.x + w * 0.25f, 0f, p.z);
                    Prim(PrimitiveType.Cube, "FarmHouse", root, hp + Vector3.up * 2f, new Vector3(9f, 4f, 7f), wall, true).isStatic = true;
                    Prim(PrimitiveType.Cube, "FarmRoof", root, hp + Vector3.up * 4.5f, new Vector3(10f, 1f, 7.6f), roof).isStatic = true;
                    var bp = new Vector3(p.x - w * 0.2f, 0f, p.z + d * 0.15f);
                    Prim(PrimitiveType.Cube, "Barn", root, bp + Vector3.up * 3f, new Vector3(12f, 6f, 9f), Mat("BarnRed", new Color(0.55f, 0.15f, 0.12f)), true).isStatic = true;
                }
            }

            // las i głazy
            int trees = 0;
            var rock = Mat("Rock", new Color(0.46f, 0.46f, 0.44f));
            for (int t = 0; t < 2600 && trees < 700; t++)
            {
                var p = new Vector3(Random.Range(-outer, outer), 0f, Random.Range(-outer, outer));
                if (InCity(p, 8f) || NearCorridor(p, 9f)) continue;
                bool inField = false;
                foreach (var o in rects) if (o.Contains(new Vector2(p.x, p.z))) { inField = true; break; }
                if (inField) continue;
                // skupiska: więcej drzew na obrzeżach niż tuż przy mieście
                float edge = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z));
                if (edge < inner + 40f && Random.value < 0.6f) continue;
                if (Random.value < 0.07f)
                {
                    float s = Random.Range(1.5f, 4.5f);
                    var rk = Prim(PrimitiveType.Sphere, "Rock", root, p + Vector3.up * s * 0.3f, new Vector3(s * 1.4f, s * 0.8f, s), rock, true);
                    rk.isStatic = true; rk.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    continue;
                }
                var tree = PlacePack("tree_" + Random.Range(1, 8), root, p, Random.Range(0f, 360f), Random.Range(6f, 10.5f));
                if (tree == null)
                {
                    float s = Random.Range(0.9f, 1.5f);
                    Prim(PrimitiveType.Cylinder, "Trunk", root, p + Vector3.up * 1.5f * s, new Vector3(0.45f, 1.5f, 0.45f) * s, Mat("Trunk", new Color(0.4f, 0.25f, 0.1f)), true).isStatic = true;
                    Prim(PrimitiveType.Sphere, "Leaves", root, p + Vector3.up * 4f * s, new Vector3(4f, 3.6f, 4f) * s, Mat("Leaves", new Color(0.15f, 0.45f, 0.15f))).isStatic = true;
                }
                else
                {
                    var tc = new GameObject("TrunkCollider").transform;
                    tc.SetParent(tree.transform, true);
                    tc.position = p + Vector3.up * 1f;
                    var cc = tc.gameObject.AddComponent<CapsuleCollider>();
                    cc.radius = 0.25f / Mathf.Max(0.01f, tree.transform.lossyScale.x); cc.height = 2f / Mathf.Max(0.01f, tree.transform.lossyScale.y);
                }
                trees++;
            }
            Random.state = oldState;
        }

        static void AddTree(Transform parent, Vector3 p)
        {
            p = KeepInsideBlock(p, 2.5f);
            // drzewa z paczki Decorations (tree_1 … tree_7); kolizja tylko pnia (cienki walec), żeby nie blokować chodników
            var packTree = PlacePack("tree_" + Random.Range(1, 8), parent, p, Random.Range(0f, 360f), Random.Range(5.5f, 8.5f));
            if (packTree != null)
            {
                var trunk0 = new GameObject("TrunkCollider").transform;
                trunk0.SetParent(packTree.transform, true);
                trunk0.position = p + Vector3.up * 1f;
                var cc = trunk0.gameObject.AddComponent<CapsuleCollider>();
                cc.radius = 0.25f / Mathf.Max(0.01f, packTree.transform.lossyScale.x); cc.height = 2f / Mathf.Max(0.01f, packTree.transform.lossyScale.y);
                return;
            }

            var trunk = Mat("Trunk", new Color(0.4f, 0.25f, 0.1f));
            var leaves = Mat("Leaves", new Color(0.15f, 0.45f, 0.15f));
            float s = Random.Range(0.8f, 1.3f);
            Prim(PrimitiveType.Cylinder, "Trunk", parent, p + Vector3.up * 1.5f * s, new Vector3(0.45f, 1.5f, 0.45f) * s, trunk, true).isStatic = true;
            Prim(PrimitiveType.Sphere, "Leaves", parent, p + Vector3.up * 4f * s, new Vector3(4f, 3.6f, 4f) * s, leaves).isStatic = true;
        }

        // =====================================================================
        //  BUDYNKI Z WNĘTRZEM
        // =====================================================================

        /// <summary>
        /// Budynek z wejściem: parter jest pusty w środku (ściany z otworem drzwiowym, sufit, światło, wyposażenie),
        /// wyższe piętra to pełna bryła. Drzwi są po stronie ulicy (faceSign = ±1 wzdłuż osi X).
        /// </summary>
        /// <summary>Czy budynki z wejściem dostają zewnętrzną powłokę/piętra z paczki. Włączone: z zewnątrz budynek z paczki, a wnętrze parteru jest z niego wycięte.</summary>
        const bool PackShell = true;

        static ShopZone BuildEnterable(Transform parent, Vector3 center, float w, float d, float totalH, int faceSign, ShopType type, string name)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = center;
            root.rotation = faceSign > 0 ? Quaternion.identity : Quaternion.Euler(0, 180, 0);

            var zoneColor = TypeColorOf(type);
            bool garage = type == ShopType.Repair;
            const float t = 0.4f, floorY = 0.12f;
            float fh = garage ? 7.5f : 6.6f;                                              // wyższy parter
            float doorW = garage ? 8.5f : 4.8f, doorH = garage ? 5.4f : 4.6f;           // szersze i wyższe wejście
            var wallCol = garage ? new Color(0.45f, 0.47f, 0.5f) : Quantize(Color.Lerp(new Color(0.6f, 0.55f, 0.5f), new Color(0.8f, 0.78f, 0.7f), Random.value));
            var wall = Mat("Wall_" + ColorUtility.ToHtmlStringRGB(wallCol), wallCol);
            var inner = Mat("InteriorWall_" + type, Color.Lerp(zoneColor, Color.white, 0.75f));

            GameObject W(string n, Vector3 p, Vector3 s, Material m) { var g = Prim(PrimitiveType.Cube, n, root, p, s, m, true); g.isStatic = true; return g; }

            // ściany
            W("Wall_Back", new Vector3(-w / 2 + t / 2, floorY + fh / 2, 0), new Vector3(t, fh, d), wall);
            W("Wall_L", new Vector3(0, floorY + fh / 2, d / 2 - t / 2), new Vector3(w, fh, t), wall);
            W("Wall_R", new Vector3(0, floorY + fh / 2, -d / 2 + t / 2), new Vector3(w, fh, t), wall);
            float seg = (d - doorW) / 2f;
            W("Wall_Front_A", new Vector3(w / 2 - t / 2, floorY + fh / 2, doorW / 2 + seg / 2), new Vector3(t, fh, seg), wall);
            W("Wall_Front_B", new Vector3(w / 2 - t / 2, floorY + fh / 2, -(doorW / 2 + seg / 2)), new Vector3(t, fh, seg), wall);
            W("Lintel", new Vector3(w / 2 - t / 2, floorY + doorH + (fh - doorH) / 2, 0), new Vector3(t, fh - doorH, doorW), wall);
            // fasada: okna wystawowe po obu stronach drzwi i na scianach bocznych, oscieznica, gzyms
            {
                var shopGlass = Mat("ShopGlass", new Color(0.5f, 0.75f, 0.9f));
                var frame = Mat("DoorFrame", new Color(0.12f, 0.12f, 0.14f));
                float paneH = garage ? 2.2f : 2.8f, paneY = floorY + (garage ? 3.2f : 1.9f) + paneH / 2f;
                float paneLen = Mathf.Max(0.6f, seg - 1.8f);
                foreach (float sgn in new[] { 1f, -1f })
                {
                    if (!garage && !PackShell) Prim(PrimitiveType.Cube, "ShopWindow", root, new Vector3(w / 2 + 0.03f, paneY, sgn * (doorW / 2 + seg / 2)), new Vector3(0.08f, paneH, paneLen), shopGlass).isStatic = true;
                    foreach (float px in new[] { -w * 0.25f, w * 0.1f })
                        if (!PackShell) Prim(PrimitiveType.Cube, "SideWindow", root, new Vector3(px, floorY + 2.2f + 1.4f, sgn * (d / 2 + 0.03f)), new Vector3(4.2f, 2.8f, 0.08f), shopGlass).isStatic = true;
                    Prim(PrimitiveType.Cube, "DoorPost", root, new Vector3(w / 2 + 0.08f, floorY + doorH / 2, sgn * (doorW / 2 + 0.1f)), new Vector3(0.2f, doorH, 0.3f), frame).isStatic = true;
                }
                Prim(PrimitiveType.Cube, "DoorHeader", root, new Vector3(w / 2 + 0.08f, floorY + doorH + 0.1f, 0), new Vector3(0.2f, 0.3f, doorW + 0.7f), frame).isStatic = true;
                if (!PackShell) Prim(PrimitiveType.Cube, "Cornice", root, new Vector3(0, floorY + fh + 0.55f, 0), new Vector3(w + 0.6f, 0.35f, d + 0.6f), Mat("Cornice_" + type, Color.Lerp(zoneColor, Color.black, 0.55f))).isStatic = true;
            }
            // okładzina wewnątrz (jaśniejsza, w kolorze sklepu)
            Prim(PrimitiveType.Cube, "Inner_Back", root, new Vector3(-w / 2 + t + 0.02f, floorY + fh / 2, 0), new Vector3(0.04f, fh - 0.1f, d - 2 * t), inner).isStatic = true;
            // sufit + wyższe piętra
            W("Ceiling", new Vector3(0, floorY + fh + 0.2f, 0), new Vector3(w, 0.4f, d), wall);
            float upper = totalH - fh - 0.4f;
            // elewacja: cały budynek z paczki od ziemi po dach (nieco większy niż ściany), z wyciętym otworem drzwiowym –
            // z zewnątrz wygląda jak każdy inny budynek, a nie parter-pudełko z budynkiem na wierzchu
            if (PackShell)
            {
                float fullH = Mathf.Max(totalH, fh + 1.2f);
                var fullPack = TryPackBuilding(root, root.position, w + 0.14f, d + 0.14f, fullH > 30f, floorY, 0f, fullH, true, false, 0f);
                if (fullPack != null)
                {
                    FitGroundFootprint(fullPack, root.position, w + 0.14f, d + 0.14f, floorY);
                    var doorBox = new Bounds(root.TransformPoint(new Vector3(w / 2f, floorY + doorH / 2f - 0.1f, 0f)), new Vector3(2.2f, doorH + 0.2f, doorW));
                    CutBox(fullPack, doorBox);
                    // wnętrze: wycinamy z modelu budynku całą objętość parteru (podłogi, przepierzenia, szyby) – widać tylko nasze wnętrze ze ścianami
                    var roomBox = new Bounds(root.TransformPoint(new Vector3(0f, floorY + fh / 2f + 0.05f, 0f)), new Vector3(w - 0.9f, fh + 0.1f, d - 0.9f));
                    CutBox(fullPack, roomBox);
                    // prostokątnej bryły (ściany, sufit) nie rysujemy wcale – zostają tylko jej kolidery i cienkie okładziny od środka
                    foreach (var wn in new[] { "Wall_Back", "Wall_L", "Wall_R", "Wall_Front_A", "Wall_Front_B", "Lintel", "Ceiling" })
                    {
                        var wt = root.Find(wn);
                        var wr = wt != null ? wt.GetComponent<MeshRenderer>() : null;
                        if (wr != null) wr.enabled = false;
                    }
                    void Skin(string sn, Vector3 sp, Vector3 ss) => Prim(PrimitiveType.Cube, sn, root, sp, ss, inner).isStatic = true;
                    Skin("In_L", new Vector3(0, floorY + fh / 2, d / 2 - t - 0.02f), new Vector3(w - 2 * t, fh, 0.04f));
                    Skin("In_R", new Vector3(0, floorY + fh / 2, -d / 2 + t + 0.02f), new Vector3(w - 2 * t, fh, 0.04f));
                    Skin("In_FrontA", new Vector3(w / 2 - t - 0.02f, floorY + fh / 2, doorW / 2 + seg / 2), new Vector3(0.04f, fh, seg));
                    Skin("In_FrontB", new Vector3(w / 2 - t - 0.02f, floorY + fh / 2, -(doorW / 2 + seg / 2)), new Vector3(0.04f, fh, seg));
                    Skin("In_Lintel", new Vector3(w / 2 - t - 0.02f, floorY + doorH + (fh - doorH) / 2, 0), new Vector3(0.04f, fh - doorH, doorW));
                    Skin("In_Ceiling", new Vector3(0, floorY + fh - 0.02f, 0), new Vector3(w - 2 * t, 0.04f, d - 2 * t));
                    if (upper > 0.2f)
                    {
                        var inv = W("Upper", new Vector3(0, floorY + fh + 0.4f + upper / 2, 0), new Vector3(w, upper, d), wall);
                        inv.GetComponent<MeshRenderer>().enabled = false;
                    }
                    goto skipUpper;
                }
            }
            if (PackShell && upper > 1.5f)
            {
                // piętra nad parterem: budynek z paczki rozciągnięty do obrysu (stary prostopadłościan zostaje jako niewidzialny collider)
                var up = TryPackBuilding(root, root.position, w, d, true, floorY + fh + 0.4f, 0f, upper, true, false, 0f);
                if (up != null)
                {
                    var inv = W("Upper", new Vector3(0, floorY + fh + 0.4f + upper / 2, 0), new Vector3(w, upper, d), wall);
                    inv.GetComponent<MeshRenderer>().enabled = false;
                    goto skipUpper;
                }
                W("Upper", new Vector3(0, floorY + fh + 0.4f + upper / 2, 0), new Vector3(w, upper, d), wall);
                Prim(PrimitiveType.Cube, "Roof", root, new Vector3(0, floorY + totalH + 0.3f, 0), new Vector3(w * 0.9f, 0.6f, d * 0.9f), Mat("Roof", new Color(0.25f, 0.25f, 0.25f))).isStatic = true;
            }
            skipUpper:;
            // podłoga (tylko wizualna)
            Prim(PrimitiveType.Cube, "Floor", root, new Vector3(0, floorY + 0.02f, 0), new Vector3(w - 2 * t, 0.04f, d - 2 * t),
                garage ? Mat("GarageFloor", new Color(0.35f, 0.35f, 0.36f)) : Mat("Floor_" + type, Color.Lerp(zoneColor, new Color(0.4f, 0.35f, 0.3f), 0.8f))).isStatic = true;

            // światło wewnątrz
            var light = Empty("InteriorLight", root, new Vector3(0, floorY + fh - 0.6f, 0)).gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = Mathf.Max(w, d) * 0.95f;
            light.intensity = 2.4f;
            light.color = type == ShopType.Bar ? new Color(1f, 0.6f, 0.85f) : new Color(1f, 0.93f, 0.82f);
            light.shadows = LightShadows.None;

            // markiza + szyld
            Prim(PrimitiveType.Cube, "Awning", root, new Vector3(w / 2 + 0.9f, floorY + doorH + 0.45f, 0), new Vector3(1.8f, 0.15f, doorW + 2f), Mat("Awning_" + type, zoneColor)).isStatic = true;
            var sign = new GameObject("Sign");
            sign.transform.SetParent(root, false);
            sign.transform.localPosition = new Vector3(w / 2 + 0.25f, floorY + fh - 0.7f, 0);
            sign.transform.localRotation = Quaternion.Euler(0, -90, 0);
            var tm = sign.AddComponent<TextMesh>();
            tm.text = name.ToUpper();
            tm.anchor = TextAnchor.MiddleCenter;
            tm.characterSize = 0.12f;
            tm.fontSize = 48;
            tm.color = zoneColor;

            // drzwi rozsuwane (bez collidera)
            if (!garage)
            {
                var door = new GameObject("Door").transform;
                door.SetParent(root, false);
                door.localPosition = new Vector3(w / 2 - t / 2, 0, 0);
                door.localRotation = Quaternion.Euler(0, 90, 0);
                var glass = Mat("DoorGlass", new Color(0.55f, 0.75f, 0.85f));
                var l = Prim(PrimitiveType.Cube, "DoorL", door, new Vector3(-doorW / 4, floorY + doorH / 2, 0), new Vector3(doorW / 2 - 0.05f, doorH - 0.05f, 0.08f), glass);
                var r = Prim(PrimitiveType.Cube, "DoorR", door, new Vector3(doorW / 4, floorY + doorH / 2, 0), new Vector3(doorW / 2 - 0.05f, doorH - 0.05f, 0.08f), glass);
                var ad = door.gameObject.AddComponent<AutoDoor>();
                ad.leftPanel = l.transform;
                ad.rightPanel = r.transform;
                ad.openDistance = doorW / 2 - 0.1f;
            }

            // strefa usługi + wyposażenie
            var zoneGo = new GameObject("Zone_" + type);
            zoneGo.transform.SetParent(root, false);
            var zone = zoneGo.AddComponent<ShopZone>();
            zone.type = type;
            zone.shopName = name;
            BuildProps(root, zone, w, d, fh, floorY);
            if (name.StartsWith("Kasyno")) DecorateCasino(root, w, d, floorY);
            else FurnishInterior(root, type, w, d, floorY);
            return zone;
        }

        static Color TypeColorOf(ShopType t) => t switch
        {
            ShopType.Weapons => new Color(0.2f, 1f, 0.3f),
            ShopType.Repair => new Color(0.2f, 0.6f, 1f),
            ShopType.Store => new Color(1f, 0.6f, 0.1f),
            ShopType.Bar => new Color(0.9f, 0.3f, 0.9f),
            _ => new Color(1f, 1f, 1f),
        };

        static void BuildProps(Transform root, ShopZone zone, float w, float d, float fh, float floorY)
        {
            var zt = zone.transform;
            GameObject P(string n, Vector3 p, Vector3 s, Color c, bool col = true, PrimitiveType pt = PrimitiveType.Cube)
            {
                var g = Prim(pt, n, root, p, s, Mat("Prop_" + ColorUtility.ToHtmlStringRGB(Quantize(c)), Quantize(c)), col);
                g.isStatic = true;
                return g;
            }
            GameObject Person(string n, Vector3 p, Color c)
            {
                var g = Prim(PrimitiveType.Capsule, n, root, p + Vector3.up * 1f, new Vector3(0.8f, 1f, 0.8f), Mat("Body_" + ColorUtility.ToHtmlStringRGB(c), c), true);
                g.transform.localRotation = Quaternion.Euler(0, 90, 0); // twarzą do drzwi
                // kapsuła zostaje tylko jako niewidzialny collider (np. do celowania w kasjera); wygląd daje model postaci z paczki
                var peds = new List<GameObject>();
                for (int pi = 0; pi < 4; pi++)
                {
                    var pp = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GTA/Generated/Prefabs/Pedestrian_" + pi + ".prefab");
                    if (pp != null) peds.Add(pp);
                }
                if (peds.Count > 0)
                {
                    g.GetComponent<MeshRenderer>().enabled = false;
                    var sk = g.AddComponent<Shopkeeper>();
                    sk.sources = peds.ToArray();
                }
                else Prim(PrimitiveType.Cube, "Face", g.transform, new Vector3(0.42f, 0.55f, 0), new Vector3(0.12f, 0.2f, 0.5f), Mat("Skin", new Color(0.95f, 0.8f, 0.65f)));
                return g;
            }
            float y = floorY;
            var wood = new Color(0.45f, 0.3f, 0.18f);

            switch (zone.type)
            {
                case ShopType.Weapons:
                    P("Counter", new Vector3(-w / 2 + 5f, y + 0.55f, 0), new Vector3(1.2f, 1.1f, d - 5f), new Color(0.25f, 0.25f, 0.28f));
                    P("GunWall", new Vector3(-w / 2 + 0.55f, y + 2.2f, 0), new Vector3(0.2f, 2.4f, d - 4f), new Color(0.35f, 0.3f, 0.25f), false);
                    for (int i = 0; i < 7; i++)
                        P("GunDisplay", new Vector3(-w / 2 + 0.75f, y + 1.6f + (i % 2) * 1.1f, -d / 2 + 3f + i * (d - 6f) / 6f), new Vector3(0.1f, 0.15f, 1.3f), new Color(0.1f, 0.1f, 0.1f), false);
                    Person("Clerk", new Vector3(-w / 2 + 3f, y, 0), new Color(0.3f, 0.35f, 0.2f));
                    zt.localPosition = new Vector3(-w / 2 + 7.5f, y, 0);
                    zone.radius = 3.2f;
                    break;

                case ShopType.Store:
                    // regały z towarem
                    Color[] goods = { new Color(0.9f, 0.2f, 0.2f), new Color(0.2f, 0.6f, 0.9f), new Color(0.95f, 0.85f, 0.2f), new Color(0.3f, 0.8f, 0.3f) };
                    foreach (float z in new[] { -5f, -1.5f, 2f })
                    {
                        P("Shelf", new Vector3(-3f, y + 0.9f, z), new Vector3(9f, 1.8f, 0.9f), new Color(0.75f, 0.75f, 0.78f));
                        for (int i = 0; i < 6; i++)
                            P("Goods", new Vector3(-7f + i * 1.6f, y + 1.9f, z), new Vector3(0.6f, 0.35f, 0.6f), goods[(i + (int)z) & 3], false);
                    }
                    P("Counter", new Vector3(w / 2 - 4.5f, y + 0.55f, d / 2 - 3.2f), new Vector3(3f, 1.1f, 1.1f), wood);
                    var cashier = Person("Cashier", new Vector3(w / 2 - 4.5f, y, d / 2 - 1.7f), new Color(0.9f, 0.5f, 0.1f));
                    cashier.transform.localRotation = Quaternion.Euler(0, 90, 0);
                    zone.cashier = cashier.GetComponent<Collider>();
                    P("Fridge", new Vector3(-w / 2 + 0.9f, y + 1.1f, 0), new Vector3(1f, 2.2f, d - 3f), new Color(0.85f, 0.9f, 0.95f));
                    zt.localPosition = new Vector3(0.5f, y, 0);
                    zone.radius = Mathf.Min(w, d) * 0.5f - 0.8f;
                    break;

                case ShopType.Bar:
                    P("BarCounter", new Vector3(-w / 2 + 3f, y + 0.55f, 0), new Vector3(1.2f, 1.1f, d - 6f), wood);
                    P("Bottles", new Vector3(-w / 2 + 0.7f, y + 1.8f, 0), new Vector3(0.4f, 1.2f, d - 5f), new Color(0.3f, 0.55f, 0.35f), false);
                    for (int i = 0; i < 5; i++)
                        P("Stool", new Vector3(-w / 2 + 4.4f, y + 0.4f, -d / 2 + 4.5f + i * 2.2f), new Vector3(0.5f, 0.4f, 0.5f), new Color(0.6f, 0.15f, 0.2f), true, PrimitiveType.Cylinder);
                    for (int i = 0; i < 3; i++)
                    {
                        var tp = new Vector3(2.5f + (i % 2) * 2.5f, 0, -5f + i * 5f);
                        P("TableTop", tp + Vector3.up * (y + 0.85f), new Vector3(1.4f, 0.05f, 1.4f), wood, true, PrimitiveType.Cylinder);
                        P("TableLeg", tp + Vector3.up * (y + 0.42f), new Vector3(0.15f, 0.42f, 0.15f), new Color(0.15f, 0.15f, 0.15f), true, PrimitiveType.Cylinder);
                    }
                    Person("Barman", new Vector3(-w / 2 + 1.8f, y, 2f), new Color(0.15f, 0.15f, 0.15f));
                    if (zone.shopName != null && zone.shopName.StartsWith("Kasyno"))
                    {
                        // kasa kasyna: kasjer za ladą – celuj w niego, żeby obrabować (większa wypłata, 4 gwiazdki)
                        var cc = Person("Cashier", new Vector3(-w / 2 + 1.8f, y, -2.5f), new Color(0.6f, 0.1f, 0.1f));
                        zone.cashier = cc.GetComponent<Collider>();
                        zone.casino = true; zone.robMinCash = 4000; zone.robMaxCash = 9000; zone.robTime = 5f; zone.robCooldown = 300f; zone.robWanted = 4;
                    }
                    var neon = Empty("Neon", root, new Vector3(0, y + fh - 1f, d / 2 - 1f)).gameObject.AddComponent<Light>();
                    neon.color = new Color(1f, 0.2f, 0.8f);
                    neon.range = 9f;
                    neon.intensity = 2f;
                    neon.shadows = LightShadows.None;
                    zt.localPosition = new Vector3(-w / 2 + 5f, y, 0);
                    zone.radius = 3.5f;
                    break;

                case ShopType.Safehouse:
                    P("Bed", new Vector3(-w / 2 + 2.2f, y + 0.35f, -d / 2 + 2.6f), new Vector3(2.2f, 0.7f, 3.4f), new Color(0.25f, 0.35f, 0.6f));
                    P("Pillow", new Vector3(-w / 2 + 2.2f, y + 0.8f, -d / 2 + 1.3f), new Vector3(1.6f, 0.25f, 0.6f), new Color(0.95f, 0.95f, 0.95f), false);
                    P("Sofa", new Vector3(1f, y + 0.4f, d / 2 - 2f), new Vector3(3.5f, 0.8f, 1.1f), new Color(0.5f, 0.25f, 0.2f));
                    P("TV", new Vector3(1f, y + 1.5f, -d / 2 + 0.7f), new Vector3(2.4f, 1.4f, 0.12f), new Color(0.05f, 0.05f, 0.05f), false);
                    Prim(PrimitiveType.Cube, "TVScreen", root, new Vector3(1f, y + 1.5f, -d / 2 + 0.78f), new Vector3(2.2f, 1.2f, 0.02f), Mat("TVScreen", new Color(0.2f, 0.5f, 0.9f), true));
                    P("Rug", new Vector3(1f, y + 0.05f, 0), new Vector3(5f, 0.02f, 4f), new Color(0.6f, 0.2f, 0.2f), false);
                    P("Table", new Vector3(1f, y + 0.4f, 1.5f), new Vector3(1.6f, 0.08f, 0.9f), wood);
                    zt.localPosition = new Vector3(-w / 2 + 3.5f, y, -d / 2 + 3f);
                    zone.radius = 3f;
                    break;

                case ShopType.Repair:
                    for (int i = 0; i < 3; i++)
                        P("Tires", new Vector3(-w / 2 + 1.2f, y + 0.3f + i * 0.4f, d / 2 - 1.5f), new Vector3(1f, 0.2f, 1f), new Color(0.08f, 0.08f, 0.08f), true, PrimitiveType.Cylinder);
                    P("Toolwall", new Vector3(-w / 2 + 0.5f, y + 2.2f, 0), new Vector3(0.15f, 2f, 6f), new Color(0.7f, 0.2f, 0.15f), false);
                    P("Stripe", new Vector3(0, y + 0.05f, 0), new Vector3(w - 4f, 0.02f, 0.3f), new Color(0.95f, 0.8f, 0.2f), false);
                    zt.localPosition = new Vector3(-1f, y, 0);
                    zone.radius = 5f;
                    zone.repairPrice = 500;
                    break;
            }
        }

        // =====================================================================
        //  MISJE
        // =====================================================================

        static Transform MissionPoint(Transform parent, string name, Vector3 pos, float yRot = 0)
        {
            var t = Empty(name, parent, pos);
            t.rotation = Quaternion.Euler(0, yRot, 0);
            return t;
        }

        static void BuildMissions(Transform root, Prefabs pf)
        {
            float H = HighwayRing;

            // --- 1: kurier ---
            {
                var m = Empty("Mission_Kurier", root, Door(6, 7, 1, 1)).gameObject.AddComponent<Mission>();
                m.requiresFlag = "a1_1";
                m.chapter = "Zlecenie poboczne";
                m.missionName = "Pierwsza robota";
                m.description = "Odbierz taksówkę i dostarcz ją pod warsztat na zachodzie przed czasem.";
                m.moneyReward = 1500;
                var car = MissionPoint(m.transform, "CarSpot", new Vector3(RoadCenter(6) + 3f, 0.3f, 29f), 0);
                var garage = MissionPoint(m.transform, "Garage", new Vector3(RoadCenter(2) + 3f, 0.2f, RoadCenter(3) + 18f));
                m.steps = new[]
                {
                    new MissionStep { type = MissionStepType.EnterVehicle, objective = "Wsiądź do taksówki", target = car, spawnVehicleAtTarget = true },
                    new MissionStep { type = MissionStepType.DriveTo, objective = "Dostarcz taksówkę pod warsztat", target = garage, radius = 10f, timeLimit = 120f },
                };
            }

            // --- 2: porachunki (strefa przemysłowa) ---
            {
                var m = Empty("Mission_Gang", root, Door(3, 2, 0, 0)).gameObject.AddComponent<Mission>();
                m.requiresFlag = "a1_2";
                m.chapter = "Zlecenie poboczne";
                m.missionName = "Porachunki";
                m.description = "Rozpraw się z gangiem w strefie przemysłowej i zgub policję.";
                m.moneyReward = 3000;
                var hood = MissionPoint(m.transform, "GangTurf", new Vector3(RoadCenter(8), 0.2f, RoadCenter(1)));
                m.steps = new[]
                {
                    new MissionStep { type = MissionStepType.GoTo, objective = "Jedź do strefy przemysłowej", target = hood, radius = 25f },
                    new MissionStep { type = MissionStepType.KillEnemies, objective = "Zlikwiduj gangsterów", target = hood, enemyCount = 6, radius = 12f },
                    new MissionStep { type = MissionStepType.LoseWanted, objective = "Zgub policję", setWantedOnStart = 2 },
                };
            }

            // --- 3: obława ---
            {
                var m = Empty("Mission_Oblawa", root, Door(7, 6, 1, 1)).gameObject.AddComponent<Mission>();
                m.requiresFlag = "a1_3";
                m.chapter = "Zlecenie poboczne";
                m.missionName = "Obława";
                m.description = "Przetrwaj 45 sekund z 3 gwiazdkami, a potem zgub pościg.";
                m.moneyReward = 5000;
                m.steps = new[]
                {
                    new MissionStep { type = MissionStepType.Survive, objective = "Przetrwaj obławę", duration = 45f, setWantedOnStart = 3 },
                    new MissionStep { type = MissionStepType.LoseWanted, objective = "Zgub pościg" },
                };
            }

            // --- 4: wyścig po obwodnicy ---
            {
                var m = Empty("Mission_Wyscig", root, Door(6, 3, 1, 0)).gameObject.AddComponent<Mission>();
                m.requiresFlag = "a2_1";
                m.chapter = "Zlecenie poboczne";
                m.missionName = "Wyścig po obwodnicy";
                m.description = "Wsiądź do sportowego auta i objedź obwodnicę, zaliczając punkty kontrolne na czas.";
                m.moneyReward = 4000;
                var car = MissionPoint(m.transform, "RaceCar", new Vector3(3f, 0.3f, 100f), 0);
                Transform Cp(string n, float x, float z) => MissionPoint(m.transform, n, new Vector3(x, 0.2f, z));
                m.steps = new[]
                {
                    new MissionStep { type = MissionStepType.EnterVehicle, objective = "Wsiądź do auta wyścigowego", target = car, spawnVehicleAtTarget = true, vehicleOverride = pf.cars[3] },
                    new MissionStep { type = MissionStepType.DriveTo, objective = "Wjedź na obwodnicę", target = Cp("CP1", 0, H), radius = 14f, timeLimit = 30f },
                    new MissionStep { type = MissionStepType.DriveTo, objective = "Punkt kontrolny 1/4", target = Cp("CP2", H, H), radius = 14f, timeLimit = 35f },
                    new MissionStep { type = MissionStepType.DriveTo, objective = "Punkt kontrolny 2/4", target = Cp("CP3", H, -H), radius = 14f, timeLimit = 45f },
                    new MissionStep { type = MissionStepType.DriveTo, objective = "Punkt kontrolny 3/4", target = Cp("CP4", -H, -H), radius = 14f, timeLimit = 45f },
                    new MissionStep { type = MissionStepType.DriveTo, objective = "Punkt kontrolny 4/4", target = Cp("CP5", -H, H), radius = 14f, timeLimit = 45f },
                    new MissionStep { type = MissionStepType.DriveTo, objective = "META!", target = Cp("Finish", 0, H), radius = 14f, timeLimit = 35f },
                };
            }

            BuildStoryMissions(root, pf);
        }
    }
}
