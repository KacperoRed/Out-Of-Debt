using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Kody do testowania (wykonuje je serwer):
    /// F4 czołg, F5 +1 gwiazdka, F6 +$1000, F7 pełne zdrowie + wszystkie bronie, F8 auto obok, F9 wyczyść gwiazdki.
    /// Wyłącz "allowCheats" w prefabie gracza przed wydaniem gry.
    /// Argument "-autotest" uruchamia automatyczny test dymny (logi z prefiksem [AUTOTEST]).
    /// </summary>
    public class DebugCheats : NetworkBehaviour
    {
        public bool allowCheats = true;

        PlayerController _player;
        float _nextLog;
        bool _autotest;
        int _autotestStage;

        void Awake() => _player = GetComponent<PlayerController>();

        public override void OnNetworkSpawn()
        {
            _autotest = IsOwner && System.Environment.GetCommandLineArgs().Contains("-autotest");
            if (_autotest) Health.DebugLog = true;
            if (_autotest) Debug.Log($"[AUTOTEST] spawned player client={OwnerClientId} server={IsServer} pos={transform.position}");
        }

        void Update()
        {
            if (!IsOwner) return;
            if (_autotest) AutoTest();
            if (allowCheats && (Input.GetKeyDown(KeyCode.BackQuote) || (_consoleOpen && Input.GetKeyDown(KeyCode.Escape)))) ToggleConsole();
            if (!allowCheats || GameInput.Blocked) return;
            if (Input.GetKeyDown(KeyCode.F4)) CheatRpc(6); // F4: przywołaj czołg
            if (Input.GetKeyDown(KeyCode.F5)) CheatRpc(0);
            if (Input.GetKeyDown(KeyCode.F6)) CheatRpc(1);
            if (Input.GetKeyDown(KeyCode.F7)) CheatRpc(2);
            if (Input.GetKeyDown(KeyCode.F8)) CheatRpc(3);
            if (Input.GetKeyDown(KeyCode.F9)) CheatRpc(4);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void CheatRpc(int code)
        {
            if (!allowCheats) return;
            switch (code)
            {
                case 0: _player.Wanted.AddStars(1); break;
                case 1: _player.Stats.AddMoney(1000); break;
                case 2: _player.Health.Heal(1000); _player.Weapons.GrantAllWeaponsServer(); break;
                case 3:
                    var ts = FindAnyObjectByType<TrafficSpawner>();
                    if (ts != null && ts.carPrefabs.Length > 0)
                        GameUtil.SpawnNetworked(ts.carPrefabs[Random.Range(0, ts.carPrefabs.Length)],
                            transform.position + transform.forward * 5f + Vector3.up, transform.rotation);
                    break;
                case 4: _player.Wanted.ClearWanted(); break;
                case 5: // test: uszkodź auto, w którym siedzisz
                    var car = _player.Vehicles.CurrentVehicle;
                    if (car != null) car.Health.TakeDamage(150f, GameUtil.NoClient);
                    break;
                case 6: // F4: bezpośredni spawn czołgu
                    SpawnVehicle("czolg");
                    break;
            }
        }

        // ---------- konsola deweloperska (klawisz `) ----------

        bool _consoleOpen, _focusConsole;
        string _consoleInput = "";
        readonly System.Collections.Generic.List<string> _consoleLog = new System.Collections.Generic.List<string>();

        int _toggledFrame = -1;

        void ToggleConsole()
        {
            _toggledFrame = Time.frameCount;
            _consoleOpen = !_consoleOpen;
            GameInput.ConsoleOpen = _consoleOpen;
            GameInput.UIOpen = _consoleOpen;
            _consoleInput = "";
            if (_consoleOpen)
            {
                _focusConsole = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void OnGUI()
        {
            if (!IsOwner || !_consoleOpen) return;

            // zamknięcie konsoli: pole tekstowe zjada zdarzenia klawiszy, więc łapiemy je tu, zanim trafią do pola
            if (Time.frameCount != _toggledFrame && Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Escape || Event.current.keyCode == KeyCode.BackQuote || Event.current.character == '`'))
            {
                Event.current.Use();
                ToggleConsole();
                return;
            }

            // Sprawdzamy Enter PRZED wyrenderowaniem pola TextField, aby Unity nie zjadło eventu
            if (Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                string cmd = _consoleInput.Replace("`", "").Trim();
                _consoleInput = "";
                Event.current.Use();
                RunConsole(cmd);
            }

            float w = Mathf.Min(680f, Screen.width - 20f), h = 260f;
            var r = new Rect(10f, Screen.height - h - 10f, w, h);
            GUI.Box(r, "Konsola  (` lub Esc = zamknij)");
            GUILayout.BeginArea(new Rect(r.x + 10f, r.y + 28f, r.width - 20f, r.height - 36f));
            int from = Mathf.Max(0, _consoleLog.Count - 9);
            for (int i = from; i < _consoleLog.Count; i++) GUILayout.Label(_consoleLog[i]);
            GUILayout.FlexibleSpace();

            GUI.SetNextControlName("MiniGTAConsole");
            _consoleInput = GUILayout.TextField(_consoleInput.Replace("`", ""));
            GUILayout.Label("F4: czołg · poziom 0-11 · akt 1-3 · gwiazdki 0-5 · nieśmiertelnosc · pieniadze N · bronie · zdrowie · pojazd czolg|heli|radiowoz|wojsko · czysc", GUI.skin.label);
            GUILayout.EndArea();

            if (_focusConsole) { GUI.FocusControl("MiniGTAConsole"); _focusConsole = false; }
        }

        void RunConsole(string cmd)
        {
            if (cmd.Length == 0) return;
            Log("> " + cmd);
            string key = Norm(cmd);
            if (key.StartsWith("pomoc") || key.StartsWith("help")) { Log("Komendy: poziom 0-11 · akt 1-3 · gwiazdki 0-5 · nieśmiertelnosc · pieniadze N · bronie · zdrowie · pojazd czolg|heli|radiowoz|wojsko|nazwa auta · czysc · pomoc"); return; }
            if (key.StartsWith("czysc") || key.StartsWith("clear")) { _consoleLog.Clear(); return; }
            ConsoleRpc(cmd);
        }

        void Log(string line)
        {
            _consoleLog.Add(line);
            if (_consoleLog.Count > 60) _consoleLog.RemoveAt(0);
        }

        static string Norm(string s) =>
            s.Trim().ToLowerInvariant().Replace("ą", "a").Replace("ć", "c").Replace("ę", "e").Replace("ł", "l")
             .Replace("ń", "n").Replace("ó", "o").Replace("ś", "s").Replace("ź", "z").Replace("ż", "z");

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void ConsoleRpc(string cmd)
        {
            if (!allowCheats) return;
            var p = Norm(cmd).Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            string c = p.Length > 0 ? p[0] : "";
            string arg = p.Length > 1 ? p[1] : "";
            bool hasNumber = int.TryParse(arg, out int n);

            switch (c)
            {
                case "gwiazdki":
                case "gwiazdka":
                case "stars":
                    if (!hasNumber) { Reply("Podaj liczbę 0-5, np. gwiazdki 3"); break; }
                    n = Mathf.Clamp(n, 0, WantedSystem.MaxStars);
                    _player.Wanted.Stars.Value = n;
                    Reply("Gwiazdki: " + n);
                    break;
                case "niesmiertelnosc":
                case "god":
                case "nieśmiertelnosc":
                    _player.Health.Invulnerable = !_player.Health.Invulnerable;
                    Reply("Nieśmiertelność: " + (_player.Health.Invulnerable ? "WŁ" : "WYŁ"));
                    break;
                case "pieniadze":
                case "kasa":
                case "money":
                    if (!hasNumber) { Reply("Podaj kwotę, np. pieniadze 5000"); break; }
                    _player.Stats.AddMoney(n);
                    Reply("Pieniądze: +" + n);
                    break;
                case "bronie":
                case "weapons":
                    _player.Weapons.GrantAllWeaponsServer();
                    Reply("Dostałeś wszystkie bronie");
                    break;
                case "zdrowie":
                case "heal":
                    _player.Health.Heal(1000f);
                    _player.Health.AddArmor(100f);
                    Reply("Pełne zdrowie i kamizelka");
                    break;
                case "poziom":
                case "level":
                case "misja":
                    if (!hasNumber) { Reply("Podaj poziom 0-" + Story.MaxLevel + ", np. poziom 5 (0 = początek, " + Story.MaxLevel + " = koniec). Teraz: " + Story.Level); break; }
                    Story.SetLevel(n);
                    Reply("Poziom fabuły: " + Story.Level + "/" + Story.MaxLevel + "  (akt " + Story.ActReached + ", dług $" + Story.Debt + ")");
                    break;
                case "akt":
                case "act":
                    if (!hasNumber || n < 1 || n > 3) { Reply("Podaj akt 1-3, np. akt 2"); break; }
                    Story.SetLevel(n == 1 ? 0 : n == 2 ? 3 : 7);
                    Reply("Akt " + n + " – poziom " + Story.Level + ", dług $" + Story.Debt);
                    break;
                case "ulica":
                case "tp":
                {
                    var rn = RoadNetwork.Instance;
                    if (rn == null || rn.NodeCount == 0) { Reply("Brak sieci dróg"); break; }
                    int ni = hasNumber ? Mathf.Abs(n) % rn.NodeCount : rn.NodeCount / 2;
                    _player.Teleport(rn.nodes[ni] + Vector3.up * 0.6f, transform.rotation);
                    Reply("Teleport na ulicę (węzeł " + ni + ")");
                    break;
                }
                case "start":
                {
                    // teleport przed znacznik pierwszej dostępnej misji fabularnej (do testów)
                    Mission best = null;
                    foreach (var mm in FindObjectsByType<Mission>(FindObjectsSortMode.None))
                        if (mm.isStory && mm.Available && (best == null || mm.debtPayment >= 0 && string.CompareOrdinal(mm.missionName, best.missionName) < 0)) best = mm;
                    if (best == null) { Reply("Brak dostępnej misji fabularnej"); break; }
                    _player.Teleport(best.transform.position + new Vector3(-6f, 0.6f, 0f), Quaternion.Euler(0f, 90f, 0f));
                    Reply("Teleport do misji: " + best.missionName);
                    break;
                }
                case "katscena":
                    DialogueUI.Play(new[]
                    {
                        new DialogueLine("Rico", "Dwadzieścia tysięcy, kolego. Oddasz co do centa – a jak nie pieniędzmi, to robotą."),
                        new DialogueLine("Ola", "Dziękuję, że jesteś przy mnie. Jakoś to wszystko spłacimy."),
                        new DialogueLine("Narrator", "Miasto nigdy nie śpi, a dług rośnie z każdą minutą."),
                    }, true);
                    break;
                case "daleko":
                    _player.Teleport(new Vector3(hasNumber ? n : 450f, 1.5f, 40f), Quaternion.Euler(0f, 90f, 0f));
                    Reply("Teleport za obwodnicę (obrzeża mapy)");
                    break;
                case "ofiara":
                {
                    // test przejeżdżania: najbliższy żywy pieszy staje 14 m przed autem/graczem
                    var from = _player.Vehicles != null && _player.Vehicles.CurrentVehicle != null ? _player.Vehicles.CurrentVehicle.transform : transform;
                    PedestrianAI best = null; float bd = 1e9f;
                    foreach (var pd in FindObjectsByType<PedestrianAI>(FindObjectsSortMode.None))
                    {
                        var hp = pd.GetComponent<Health>();
                        if (hp == null || hp.Dead) continue;
                        float d = (pd.transform.position - from.position).sqrMagnitude;
                        if (d < bd) { bd = d; best = pd; }
                    }
                    if (best == null) { Reply("Brak pieszych"); break; }
                    best.dodgeCars = false;
                    var ag = best.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    Vector3 target = from.position + from.forward * 14f;
                    if (UnityEngine.AI.NavMesh.SamplePosition(target, out var nh, 6f, UnityEngine.AI.NavMesh.AllAreas)) target = nh.position;
                    if (ag != null && ag.enabled) { ag.Warp(target); ag.ResetPath(); ag.isStopped = true; } else best.transform.position = target;
                    best.enabled = false;   // stoi w miejscu na czas testu
                    Reply("Pieszy ustawiony przed pojazdem");
                    break;
                }
                case "pojazd":
                case "auto":
                case "car":
                    SpawnVehicle(p.Length > 1 ? string.Join(" ", p, 1, p.Length - 1) : "");
                    break;
                default:
                    Reply("Nieznana komenda: " + cmd + " (wpisz pomoc)");
                    break;
            }
        }

        void SpawnVehicle(string name)
        {
            string key = Norm(name);
            if (key.Length == 0) { Reply("Podaj pojazd: czolg, heli, radiowoz, wojsko albo nazwę auta (sport, taxi, sedan)"); return; }

            var ps = FindAnyObjectByType<PoliceSpawner>();
            var ts = FindAnyObjectByType<TrafficSpawner>();
            GameObject prefab = null;
            float lift = 1f;
            if (key.Contains("czolg") || key.Contains("tank")) prefab = ps != null ? ps.tankPrefab : null;
            else if (key.Contains("heli")) { prefab = ps != null ? ps.helicopterPrefab : null; lift = 2f; }
            else if (key.Contains("radiow") || key.Contains("police")) prefab = ps != null ? ps.policeCarPrefab : null;
            else if (key.Contains("wojsk") || key.Contains("army") || key.Contains("transport")) prefab = ps != null ? ps.armyCarPrefab : null;
            else if (ts != null) prefab = ts.carPrefabs.FirstOrDefault(c => c != null && Norm(c.name).Contains(key));

            if (prefab == null) { Reply("Brak pojazdu: " + name); return; }
            Vector3 f = transform.forward; f.y = 0f;
            GameUtil.SpawnNetworked(prefab, transform.position + f.normalized * 7f + Vector3.up * lift, Quaternion.LookRotation(f.normalized));
            Reply("Pojazd: " + prefab.name);
        }

        [Rpc(SendTo.Owner)]
        void ReplyRpc(string text) => Log(text);

        void Reply(string text) => ReplyRpc(text);

        // ---------- automatyczny test dymny ----------

        float _robUntil, _nextRob;

        void TeleportTo(ShopType type, Vector3 offset)
        {
            var z = ShopZone.All.FirstOrDefault(s => s.type == type);
            if (z != null) _player.Teleport(z.transform.position + offset + Vector3.up * 0.3f, transform.rotation);
            Debug.Log($"[AUTOTEST] teleport -> {type} {(z != null ? z.shopName + " " + z.transform.position : "BRAK")}");
        }

        void AutoTest()
        {
            float t = Time.timeSinceLevelLoad;
            var wc = _player.Weapons;
            var shop = GetComponent<ShopClient>();
            bool host = IsServer;

            if (_autotestStage == 0 && t > 3f)
            {
                _autotestStage = 1;
                var net = RoadNetwork.Instance;
                Debug.Log($"[AUTOTEST] world: roadNodes={(net != null ? net.NodeCount : 0)} shops={string.Join(",", ShopZone.All.GroupBy(z => z.type).Select(g => g.Key + "x" + g.Count()))} " +
                          $"lamps={FindObjectsByType<StreetLamp>(FindObjectsSortMode.None).Length} doors={FindObjectsByType<AutoDoor>(FindObjectsSortMode.None).Length}");
                // czy da się dojść z ulicy do każdego wnętrza (NavMesh)
                foreach (var z in ShopZone.All)
                {
                    var path = new UnityEngine.AI.NavMeshPath();
                    Vector3 outside = z.transform.parent != null ? z.transform.parent.TransformPoint(new Vector3(13f, 0f, 0f)) : z.transform.position;
                    bool ok = UnityEngine.AI.NavMesh.SamplePosition(outside, out var a, 4f, UnityEngine.AI.NavMesh.AllAreas) &&
                              UnityEngine.AI.NavMesh.SamplePosition(z.transform.position, out var b, 3f, UnityEngine.AI.NavMesh.AllAreas) &&
                              UnityEngine.AI.NavMesh.CalculatePath(a.position, b.position, UnityEngine.AI.NavMesh.AllAreas, path);
                    Debug.Log($"[AUTOTEST]   path street->{z.shopName}: {(ok ? path.status.ToString() : "brak NavMesh")}");
                }
                if (host) { TeleportTo(ShopType.Weapons, Vector3.zero); for (int i = 0; i < 6; i++) CheatRpc(1); }
            }
            if (!host)
            {
                // klient: auto obok, wsiądź i jedź – sprawdza widoczność aut i jazdę klienta
                if (_autotestStage == 1 && t > 8f) { _autotestStage = 2; CheatRpc(3); }
                if (_autotestStage == 2 && t > 10f)
                {
                    _autotestStage = 3;
                    var car = Vehicle.All.Where(v => v != null && v.CanEnter).OrderBy(v => Vector3.Distance(v.transform.position, transform.position)).FirstOrDefault();
                    Debug.Log($"[AUTOTEST] client sees {Vehicle.All.Count} vehicles, entering {(car != null ? car.displayName : "BRAK")}");
                    if (car != null) _player.Vehicles.RequestEnter(car);
                }
                if (_autotestStage == 3 && t > 12f) { _autotestStage = 4; GameInput.OverrideMove = new Vector2(0.2f, 1f); }
                if (_autotestStage == 4 && t > 18f) { _autotestStage = 5; GameInput.OverrideMove = null; }
                AutoLog(t);
                return;
            }

            if (_autotestStage == 1 && t > 5f) { _autotestStage = 2; shop.RequestBuy(ShopClient.ArmorItem); shop.RequestBuy(4); }
            if (_autotestStage == 2 && t > 7f)
            {
                _autotestStage = 3;
                Debug.Log($"[AUTOTEST] armor={_player.Health.Armor.Value:0} owned={System.Convert.ToString(wc.OwnedMask.Value, 2)}");
                TeleportTo(ShopType.Store, Vector3.zero);
                wc.CurrentWeapon.Value = 1;
                _robUntil = t + 4.5f;
            }
            if (_autotestStage == 3)
            {
                if (t < _robUntil) { if (t > _nextRob) { _nextRob = t + 0.25f; shop.DebugRobTick(); } }
                else { _autotestStage = 4; Debug.Log($"[AUTOTEST] after robbery: stars={_player.Wanted.Level} money={_player.Stats.Money.Value}"); }
            }
            if (_autotestStage == 4 && t > 13f) { _autotestStage = 5; CheatRpc(4); TeleportTo(ShopType.Bar, Vector3.zero); }
            if (_autotestStage == 5 && t > 14f) { _autotestStage = 6; shop.RequestBuy(ShopClient.FoodOffset); }
            if (_autotestStage == 6 && t > 15f) { _autotestStage = 7; TeleportTo(ShopType.Safehouse, Vector3.zero); }
            if (_autotestStage == 7 && t > 16f)
            {
                _autotestStage = 8;
                shop.RequestBuy(ShopClient.SaveGame);
                Debug.Log("[AUTOTEST] save dir: " + System.IO.Path.Combine(Application.persistentDataPath, "saves"));
            }
            // skok kaskaderski w stunt parku
            if (_autotestStage == 8 && t > 18f)
            {
                _autotestStage = 9;
                var ramp = GameObject.Find("StuntRamp");
                var ramps = FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(x => x.name == "StuntRamp").ToList();
                var r = ramps.OrderBy(x => x.position.x).FirstOrDefault(x => x.position.x > 150f && x.position.z < -150f) ?? ramps.FirstOrDefault();
                if (r != null)
                {
                    Vector3 dir = Vector3.ProjectOnPlane(r.forward, Vector3.up).normalized;
                    _rampDir = dir;
                    _player.Teleport(r.position - dir * 40f + Vector3.up * 0.5f, Quaternion.LookRotation(dir));
                    Debug.Log($"[AUTOTEST] stunt ramp at {r.position} dir={dir}");
                }
            }
            if (_autotestStage == 9 && t > 19f) { _autotestStage = 10; CheatRpc(3); }
            if (_autotestStage == 10 && t > 21f)
            {
                _autotestStage = 11;
                var car = FindObjectsByType<Vehicle>(FindObjectsSortMode.None)
                    .Where(v => v.CanEnter).OrderBy(v => Vector3.Distance(v.transform.position, transform.position)).FirstOrDefault();
                if (car != null) _player.Vehicles.RequestEnter(car);
            }
            if (_autotestStage == 11 && t > 22.5f)
            {
                _autotestStage = 12;
                var v = _player.Vehicles.CurrentVehicle;
                if (v != null && v.IsOwner)
                {
                    v.Body.rotation = Quaternion.LookRotation(_rampDir);
                    v.transform.rotation = v.Body.rotation;
                    v.Body.linearVelocity = _rampDir * 26f;
                    GameInput.OverrideMove = new Vector2(0f, 1f);
                    Debug.Log($"[AUTOTEST] launching {v.displayName} toward ramp");
                }
            }
            if (_autotestStage == 12 && t > 27f) { _autotestStage = 13; GameInput.OverrideMove = null; }
            AutoLog(t);
        }

        Vector3 _rampDir = Vector3.forward;

        void AutoLog(float t)
        {
            if (Time.time < _nextLog) return;
            _nextLog = Time.time + 5f;
            var v = _player.Vehicles.CurrentVehicle;
            float aiSum = 0; int aiN = 0, atRed = 0, cops = 0, swat = 0, army = 0;
            foreach (var u in CombatNpcAI.All)
            {
                if (u == null || u.Faction != Faction.Police || u.GetComponent<Health>().Dead) continue;
                if (u.tier == PoliceTier.Swat) swat++; else if (u.tier == PoliceTier.Army) army++; else cops++;
            }
            foreach (var car in Vehicle.All)
                if (car != null && car.IsAIDriven.Value) { aiSum += car.SpeedKmh; aiN++; if (car.WaitingAtRed) atRed++; }

            // ilu pieszych stoi na jezdni (powinno być mało – chodniki!)
            int onRoad = 0, onZebra = 0, waiting = 0, peds = 0;
            foreach (var ped in FindObjectsByType<PedestrianAI>(FindObjectsSortMode.None))
            {
                peds++;
                if (UnityEngine.AI.NavMesh.SamplePosition(ped.transform.position, out var hit, 1f, UnityEngine.AI.NavMesh.AllAreas) &&
                    (hit.mask & (1 << GameUtil.RoadArea)) != 0) onRoad++;
                if (UnityEngine.AI.NavMesh.SamplePosition(ped.transform.position, out hit, 1f, UnityEngine.AI.NavMesh.AllAreas) &&
                    (hit.mask & (1 << GameUtil.CrosswalkArea)) != 0) onZebra++;
                if (ped.WaitingForLight) waiting++;
            }
            var dn = DayNightCycle.Instance;
            Debug.Log($"[AUTOTEST] t={t:0} client={OwnerClientId} players={PlayerController.All.Count} time={(dn != null ? dn.ClockText : "-")} " +
                      $"vehicles={Vehicle.All.Count} spawnedVeh={FindObjectsByType<Vehicle>(FindObjectsSortMode.None).Length} aiCars={aiN} aiSpeed={(aiN > 0 ? aiSum / aiN : 0):0}kmh atRed={atRed} aiCrashes={Vehicle.AiCrashes} aiPedHits={Vehicle.AiPedHits} " +
                      $"peds={peds} pedsOnRoad={onRoad} pedsOnZebra={onZebra} pedsWaitLight={waiting} armed={CombatNpcAI.All.Count} cops={cops} swat={swat} army={army} hp={_player.Health.Current.Value:0} armor={_player.Health.Armor.Value:0} " +
                      $"stars={_player.Wanted.Level} money={_player.Stats.Money.Value} inCar={(v != null ? v.displayName + " " + v.SpeedKmh.ToString("0") + "kmh" : "no")} pos={transform.position}");
        }
    }
}