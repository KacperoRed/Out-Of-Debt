using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>Rodzaj służb reagujących na poszukiwanie.</summary>
    public enum PoliceTier { Police, Swat, Army }

    /// <summary>
    /// Reakcja służb na gwiazdki (tylko serwer). Trzy poziomy:
    ///  • Policja (1–2★): patrole pieszo i radiowozy
    ///  • SWAT (3–4★): ciężko uzbrojone oddziały, dojeżdżają radiowozami SWAT
    ///  • Wojsko (5★): żołnierze i wojskowe transportery
    /// Ile jednostek każdego rodzaju jest przy danej liczbie gwiazdek – tabela "forces" w Inspectorze.
    /// Prefaby: officerPrefab (policja), swatPrefab, armyPrefab, policeCarPrefab, armyCarPrefab.
    /// Brak swatPrefab/armyPrefab = używa prefabu niższego poziomu (SWAT → policja, wojsko → SWAT).
    /// </summary>
    public class PoliceSpawner : MonoBehaviour
    {
        [System.Serializable]
        public struct Force
        {
            public int police, swat, army;       // piesi
            public int policeCars, swatVans, armyCars;
            public int helis, tanks;             // helikoptery policji, czołgi wojska
        }

        public GameObject officerPrefab;
        public GameObject swatPrefab;
        public GameObject armyPrefab;
        public GameObject policeCarPrefab;
        public GameObject armyCarPrefab;
        public GameObject helicopterPrefab;
        public GameObject tankPrefab;

        [Tooltip("Indeks = liczba gwiazdek (0..5)")]
        public Force[] forces =
        {
            new Force(),
            new Force { police = 2 },
            new Force { police = 3, policeCars = 1 },
            new Force { police = 2, swat = 2, policeCars = 1, swatVans = 1, helis = 1 },
            new Force { police = 1, swat = 4, policeCars = 1, swatVans = 2, helis = 1 },
            new Force { swat = 3, army = 5, swatVans = 1, armyCars = 2, helis = 2, tanks = 1 },
        };
        public int officersPerCar = 2;

        public float spawnMinDistance = 35f;
        public float spawnMaxDistance = 60f;
        public float carSpawnMinDistance = 70f;
        public float carSpawnMaxDistance = 110f;
        public float despawnDistance = 90f;

        readonly List<CombatNpcAI> _units = new List<CombatNpcAI>();
        readonly List<(PoliceCarAI car, PoliceTier tier)> _cars = new List<(PoliceCarAI, PoliceTier)>();
        readonly List<Helicopter> _helis = new List<Helicopter>();
        readonly List<Vehicle> _tanks = new List<Vehicle>();
        float _next;

        void Update()
        {
            if (!GameUtil.IsServerRunning || Time.time < _next) return;
            _next = Time.time + 2f;

            // wszystkie żywe jednostki służb (także wysadzone z radiowozów)
            _units.Clear();
            foreach (var o in CombatNpcAI.All)
                if (o != null && o.IsSpawned && o.Faction == Faction.Police && !o.GetComponent<Health>().Dead) _units.Add(o);
            _cars.RemoveAll(c => c.car == null || !c.car.GetComponent<NetworkObject>().IsSpawned);

            // najbardziej poszukiwany gracz
            PlayerController most = null;
            foreach (var p in PlayerController.All)
                if (!p.IsDead && p.Wanted != null && p.Wanted.Level > 0 && (most == null || p.Wanted.Level > most.Wanted.Level))
                    most = p;

            Cleanup(most == null);
            if (most == null)
            {
                // nikt nie jest poszukiwany – helikoptery lądują, czołgi wracają do ruchu
                foreach (var h in _helis) if (h != null) h.StandDown();
                foreach (var t in _tanks) if (t != null && t.IsSpawned) t.StopAI();
                return;
            }

            var f = forces[Mathf.Clamp(most.Wanted.Level, 0, forces.Length - 1)];
            SpawnFoot(PoliceTier.Police, f.police, most);
            SpawnFoot(PoliceTier.Swat, f.swat, most);
            SpawnFoot(PoliceTier.Army, f.army, most);
            SpawnCar(PoliceTier.Police, f.policeCars, most);
            SpawnCar(PoliceTier.Swat, f.swatVans, most);
            SpawnCar(PoliceTier.Army, f.armyCars, most);
            SpawnHelis(f.helis, most);
            SpawnTanks(f.tanks, most);
        }

        void SpawnHelis(int target, PlayerController most)
        {
            if (helicopterPrefab == null) return;
            int alive = 0;
            foreach (var h in _helis) if (h != null && h.AIControlled && !h.Car.Health.Dead) alive++;
            if (alive >= target) return;

            // helikopter pojawia się w powietrzu, kilkadziesiąt metrów od gracza
            Vector2 d = Random.insideUnitCircle.normalized * 80f;
            Vector3 pos = most.transform.position + new Vector3(d.x, 22f, d.y);
            var no = GameUtil.SpawnNetworked(helicopterPrefab, pos, Quaternion.LookRotation(most.transform.position - pos));
            var heli = no.GetComponent<Helicopter>();
            if (heli == null) return;
            heli.Dispatch(most);
            _helis.Add(heli);
        }

        void SpawnTanks(int target, PlayerController most)
        {
            if (tankPrefab == null) return;
            int alive = 0;
            foreach (var t in _tanks) if (t != null && t.IsAIDriven.Value && !t.Health.Dead) alive++;
            if (alive >= target) return;

            Vector3 pos;
            if (FindRoadPoint(most.transform.position, out pos) ||
                GameUtil.RandomNavPoint(most.transform.position, carSpawnMinDistance, carSpawnMaxDistance, out pos))
            {
                var tank = GameUtil.SpawnNetworked(tankPrefab, pos + Vector3.up * 0.5f, Quaternion.identity).GetComponent<Vehicle>();
                tank.StartChase(most.transform);
                _tanks.Add(tank);
            }
        }

        GameObject OfficerFor(PoliceTier t)
        {
            switch (t)
            {
                case PoliceTier.Swat: return swatPrefab != null ? swatPrefab : officerPrefab;
                case PoliceTier.Army: return armyPrefab != null ? armyPrefab : (swatPrefab != null ? swatPrefab : officerPrefab);
                default: return officerPrefab;
            }
        }

        GameObject CarFor(PoliceTier t) => t == PoliceTier.Army && armyCarPrefab != null ? armyCarPrefab : policeCarPrefab;

        void SpawnFoot(PoliceTier tier, int target, PlayerController most)
        {
            int alive = 0;
            foreach (var u in _units) if (u.tier == tier) alive++;
            var prefab = OfficerFor(tier);
            if (alive >= target || prefab == null) return;
            if (GameUtil.RandomNavPoint(most.transform.position, spawnMinDistance, spawnMaxDistance, out var pos))
                SpawnOfficer(prefab, pos, tier);
        }

        void SpawnCar(PoliceTier tier, int target, PlayerController most)
        {
            int active = 0;
            foreach (var c in _cars) if (c.tier == tier && c.car.IsActive) active++;
            var prefab = CarFor(tier);
            if (active >= target || prefab == null) return;

            Vector3 pos;
            if (FindRoadPoint(most.transform.position, out pos) ||
                GameUtil.RandomNavPoint(most.transform.position, carSpawnMinDistance, carSpawnMaxDistance, out pos))
            {
                var rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(most.transform.position - pos, Vector3.up));
                var car = GameUtil.SpawnNetworked(prefab, pos + Vector3.up * 0.5f, rot);
                var ai = car.GetComponent<PoliceCarAI>();
                if (ai == null) { Debug.LogWarning("[MiniGTA] Prefab auta służb nie ma PoliceCarAI: " + prefab.name); car.Despawn(true); return; }
                ai.Dispatch(most, OfficerFor(tier), officersPerCar, tier);
                _cars.Add((ai, tier));
            }
        }

        void SpawnOfficer(GameObject prefab, Vector3 pos, PoliceTier tier)
        {
            var no = GameUtil.SpawnNetworked(prefab, pos, Quaternion.identity);
            var ai = no.GetComponent<CombatNpcAI>();
            if (ai != null)
            {
                ai.tier = tier;
                // policja jest słabsza: mniej obrażeń, mniejsza celność, wolniejszy ogień i mniej zdrowia
                ai.damage *= 0.5f;
                ai.accuracy *= 0.6f;
                ai.fireRate *= 0.75f;
                var h = no.GetComponent<Health>();
                if (h != null)
                {
                    h.maxHealth *= tier == PoliceTier.Police ? 0.6f : tier == PoliceTier.Swat ? 0.8f : 0.9f;
                    h.Current.Value = h.maxHealth;
                }
            }
        }

        /// <summary>Punkt na trasie ruchu ulicznego w odpowiedniej odległości – radiowozy przyjeżdżają ulicami.</summary>
        bool FindRoadPoint(Vector3 around, out Vector3 result)
        {
            result = around;
            var net = RoadNetwork.Instance;
            if (net != null)
            {
                for (int t = 0; t < 20; t++)
                {
                    if (!net.RandomLanePosition(out _, out _, out var p, out _)) break;
                    float d = Vector3.Distance(p, around);
                    if (d > carSpawnMinDistance && d < carSpawnMaxDistance) { result = p; return true; }
                }
            }
            for (int t = 0; t < 15 && WaypointPath.All.Count > 0; t++)
            {
                var path = WaypointPath.All[Random.Range(0, WaypointPath.All.Count)];
                if (path.Count == 0) continue;
                var p = path.GetPoint(Random.Range(0, path.Count));
                float d = Vector3.Distance(p, around);
                if (d > carSpawnMinDistance && d < carSpawnMaxDistance) { result = p; return true; }
            }
            return false;
        }

        void Cleanup(bool nobodyWanted)
        {
            _helis.RemoveAll(h => h == null || !h.Car.IsSpawned);
            _tanks.RemoveAll(t => t == null || !t.IsSpawned);
            if (!nobodyWanted) return;

            // helikoptery i czołgi bez pilota znikają, gdy są daleko od graczy
            for (int i = _helis.Count - 1; i >= 0; i--)
            {
                var v = _helis[i].Car;
                if (!_helis[i].AIControlled && !v.HasPlayerDriver && GameUtil.DistanceToNearestPlayer(v.transform.position) > despawnDistance)
                {
                    v.NetworkObject.Despawn(true);
                    _helis.RemoveAt(i);
                }
            }
            for (int i = _tanks.Count - 1; i >= 0; i--)
            {
                var v = _tanks[i];
                if (!v.HasPlayerDriver && GameUtil.DistanceToNearestPlayer(v.transform.position) > despawnDistance)
                {
                    v.NetworkObject.Despawn(true);
                    _tanks.RemoveAt(i);
                }
            }
            // gdy nikt nie jest poszukiwany, służby daleko od graczy znikają
            for (int i = _units.Count - 1; i >= 0; i--)
            {
                if (GameUtil.DistanceToNearestPlayer(_units[i].transform.position) > despawnDistance)
                {
                    _units[i].NetworkObject.Despawn(true);
                    _units.RemoveAt(i);
                }
            }
            for (int i = _cars.Count - 1; i >= 0; i--)
            {
                var v = _cars[i].car.GetComponent<Vehicle>();
                if (!v.HasPlayerDriver && GameUtil.DistanceToNearestPlayer(v.transform.position) > despawnDistance)
                {
                    v.NetworkObject.Despawn(true);
                    _cars.RemoveAt(i);
                }
            }
        }
    }
}
