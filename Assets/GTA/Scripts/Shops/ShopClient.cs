using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Kupowanie i usługi (na graczu). Klient wysyła prośbę BuyRpc, serwer sprawdza: czy gracz stoi w strefie,
    /// czy ma pieniądze – i dopiero wtedy coś daje. Warsztat działa automatycznie po wjechaniu,
    /// napad na sklep 24/7 – celując w kasjera przez kilka sekund.
    /// </summary>
    public class ShopClient : NetworkBehaviour
    {
        // kody: 0..99 broń i, 100..199 amunicja, 900 apteczka, 901 kamizelka, 1000+i jedzenie, 2000 zapis, 2001 odpoczynek
        public const int AmmoOffset = 100;
        public const int Medkit = 900;
        public const int ArmorItem = 901;
        public const int FoodOffset = 1000;
        public const int SaveGame = 2000;
        public const int Rest = 2001;
        public const int CasinoVest = 3000, CasinoMedkit = 3001, CasinoAmmo = 3002;
        const int VipVestPrice = 600, VipMedkitPrice = 400, VipAmmoPrice = 700;

        public struct FoodItem
        {
            public string name; public int price; public float heal;
            public FoodItem(string n, int p, float h) { name = n; price = p; heal = h; }
        }

        static readonly FoodItem[] StoreFood =
        {
            new FoodItem("Hot-dog", 15, 25),
            new FoodItem("Kanapka", 12, 20),
            new FoodItem("Napój energetyczny", 8, 10),
        };

        static readonly FoodItem[] BarFood =
        {
            new FoodItem("Burger", 25, 40),
            new FoodItem("Pizza", 30, 50),
            new FoodItem("Piwo", 10, 15),
        };

        public static FoodItem[] FoodFor(ShopType t) => t == ShopType.Store ? StoreFood : t == ShopType.Bar ? BarFood : new FoodItem[0];

        PlayerController _player;
        bool _open;
        Vector2 _scroll;
        ShopZone _repairZoneInside; // serwer: w którym warsztacie gracz już "obsłużony"
        float _repairRetryTime;

        // napad
        float _robLocalProgress, _nextRobTick;   // właściciel
        float _robStart, _lastRobTick;           // serwer
        float _lastRestTime = -999f;             // serwer

        public float RobProgress => _robLocalProgress;

        void Awake() => _player = GetComponent<PlayerController>();

        ShopZone MenuZone => ShopZone.FindMenu(transform.position);

        void Update()
        {
            if (IsServer) UpdateRepairServer();
            if (!IsOwner) return;

            bool atTable = RouletteTable.Near(transform.position) != null && !_player.InVehicle && !_player.IsDead;
            if (!atTable && _rouOpen) SetRoulette(false);
            if (atTable && !_open && Input.GetKeyDown(KeyCode.E)) SetRoulette(!_rouOpen);
            if (_rouOpen && Input.GetKeyDown(KeyCode.Escape)) SetRoulette(false);

            bool atTune = TuneVehicle() != null;
            if (!atTune && _tuneOpen) SetTune(false);
            if (atTune && !_rouOpen && Input.GetKeyDown(KeyCode.E)) SetTune(!_tuneOpen);
            if (_tuneOpen && Input.GetKeyDown(KeyCode.Escape)) SetTune(false);

            bool canShop = MenuZone != null && !_player.InVehicle && !_player.IsDead && !_rouOpen && !atTable;
            if (!canShop && _open) SetOpen(false);
            if (canShop && Input.GetKeyDown(KeyCode.E)) SetOpen(!_open);
            if (_open && Input.GetKeyDown(KeyCode.Escape)) SetOpen(false);

            UpdateRobberyOwner();
        }

        void SetOpen(bool open)
        {
            _open = open;
            GameInput.UIOpen = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner && _tuneOpen) SetTune(false);
            if (IsOwner && _rouOpen) SetRoulette(false);
            if (IsOwner && _open) SetOpen(false);
        }

        // ================= NAPAD (właściciel) =================

        float _nextLockHint;

        void UpdateRobberyOwner()
        {
            var zone = ShopZone.FindRobbable(transform.position);
            bool aiming = zone != null && zone.cashier != null && !_open && GameInput.Aim && !_player.InVehicle
                          && _player.Weapons != null && _player.Weapons.Current.kind != WeaponKind.Melee
                          && AimingAt(zone.cashier);
            if (!aiming) { _robLocalProgress = 0f; return; }

            _robLocalProgress += Time.deltaTime / Mathf.Max(0.1f, zone.robTime);
            if (Time.time > _nextRobTick)
            {
                _nextRobTick = Time.time + 0.25f;
                RobTickRpc();
            }
        }

        static bool AimingAt(Collider target)
        {
            var cam = Camera.main;
            if (cam == null) return false;
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            return target.Raycast(ray, out _, 25f);
        }

        /// <summary>Test automatyczny: udawaj celowanie w kasjera.</summary>
        public void DebugRobTick() { if (IsOwner) RobTickRpc(); }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RobTickRpc()
        {
            var zone = ShopZone.FindRobbable(transform.position);
            var wc = _player.Weapons;
            if (zone == null || _player.IsDead || _player.InVehicle || wc == null) return;
            int w = wc.CurrentWeapon.Value;
            if (!wc.Owns(w) || wc.weapons[w].kind == WeaponKind.Melee) return;

            if (Time.time - zone.lastRobbedTime < zone.robCooldown)
            {
                if (Time.time - _lastRobTick > 2f) Result("Kasa jest pusta – wróć później", false);
                _lastRobTick = Time.time;
                return;
            }

            if (Time.time - _lastRobTick > 1f) _robStart = Time.time; // nowy napad
            _lastRobTick = Time.time;
            if (Time.time - _robStart < zone.robTime) return;

            int cash = Random.Range(zone.robMinCash, zone.robMaxCash + 1) / 10 * 10;
            zone.lastRobbedTime = Time.time;
            _player.Stats.AddMoney(cash);
            _player.Wanted.SetAtLeast(Mathf.Max(2, zone.robWanted));
            CombatEvents.RaiseGunshot(transform.position, OwnerClientId); // klienci uciekają
            Result($"Kasjer oddał ${cash}! Uciekaj!", true);
        }

        // ================= UI (właściciel) =================

        void OnGUI()
        {
            if (!IsOwner || !IsSpawned) return;
            MenuSkin.Ensure();
            var center = new GUIStyle(MenuSkin.Title) { fontSize = 22 };

            if (_robLocalProgress > 0f)
            {
                var r = new Rect(Screen.width / 2f - 170, Screen.height * 0.6f, 340, 30);
                GUI.Box(r, "", MenuSkin.Pill);
                GUI.color = new Color(0.8f, 0.2f, 0.1f);
                GUI.DrawTexture(new Rect(r.x + 4, r.y + 4, (r.width - 8) * Mathf.Clamp01(_robLocalProgress), r.height - 8), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Box(new Rect(r.x - 30, r.y - 46, r.width + 60, 40), "NAPAD – trzymaj kasjera na muszce", MenuSkin.Pill);
            }

            if (_rouOpen) { DrawRoulette(); return; }
            if (_tuneOpen) { DrawTune(); return; }
            if (TuneVehicle() != null)
            {
                HudSkin.Prompt(new Rect(Screen.width / 2f, Screen.height * 0.66f, 0, 0), "E", "TUNING POJAZDU");
                return;
            }
            if (!_open && RouletteTable.Near(transform.position) != null && !_player.InVehicle && !_player.IsDead)
            {
                HudSkin.Prompt(new Rect(Screen.width / 2f, Screen.height * 0.66f, 0, 0), "E", "RULETKA – ZAGRAJ");
                return;
            }

            var zone = MenuZone;
            if (!_open)
            {
                if (zone != null && !_player.InVehicle)
                    HudSkin.Prompt(new Rect(Screen.width / 2f, Screen.height * 0.66f, 0, 0), "E",
                        zone.type == ShopType.Safehouse ? "WEJDŹ DO KRYJÓWKI" : zone.type == ShopType.Repair ? zone.shopName.ToUpper() : zone.shopName.ToUpper());
                return;
            }
            if (zone == null) return;

            float w = Mathf.Min(620, Screen.width - 40), h = Mathf.Min(560, Screen.height - 40);
            var area = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            MenuSkin.DrawPanel(area);
            GUILayout.BeginArea(new Rect(area.x + 20, area.y + 16, area.width - 40, area.height - 32));
            GUILayout.Label(zone.shopName.ToUpper(), MenuSkin.Title, GUILayout.Height(44));
            GUILayout.Label($"Gotówka: ${_player.Stats.Money.Value:N0}   HP: {_player.Health.Current.Value:0}   Pancerz: {_player.Health.Armor.Value:0}",
                MenuSkin.Pill, GUILayout.Height(36));
            GUILayout.Label("Kupione przedmioty trafiają do plecaka (I).", new GUIStyle(MenuSkin.Label) { fontSize = 14 });
            GUILayout.Space(4);
            _scroll = GUILayout.BeginScrollView(_scroll);

            switch (zone.type)
            {
                case ShopType.Weapons:
                    DrawWeapons();
                    Row("Apteczka (pełne HP)", $"Kup  ${zone.medkitPrice}", Medkit);
                    Row($"Kamizelka (+{Health.MaxArmor:0} pancerza)", $"Kup  ${zone.armorPrice}", ArmorItem);
                    break;
                case ShopType.Bar when zone.casino:
                    Row("Kamizelka VIP (pełny pancerz)", $"Kup  ${VipVestPrice}", CasinoVest);
                    Row("Apteczka VIP (pełne HP)", $"Kup  ${VipMedkitPrice}", CasinoMedkit);
                    Row("Pakiet amunicji (+1 magazynek)", $"Kup  ${VipAmmoPrice}", CasinoAmmo);
                    GUILayout.Label("Ruletka: podejdź do stołu w głębi sali i naciśnij E. Kasa kasyna jest do obrabowania (celuj w kasjera).", MenuSkin.Label);
                    break;
                case ShopType.Store:
                case ShopType.Bar:
                    var food = FoodFor(zone.type);
                    for (int i = 0; i < food.Length; i++)
                        Row($"{food[i].name} (+{food[i].heal:0} HP)", $"Kup  ${food[i].price}", FoodOffset + i);
                    if (zone.type == ShopType.Store)
                    {
                        Row("Apteczka (pełne HP)", $"Kup  ${zone.medkitPrice}", Medkit);
                        GUILayout.Label("Wskazówka: wyceluj bronią (PPM) w kasjera, żeby obrabować sklep. Policja przyjedzie!", MenuSkin.Label);
                    }
                    break;
                case ShopType.Safehouse:
                    Row("Zapisz grę (kasa, bronie, pancerz)", "Zapisz", SaveGame);
                    Row("Odpocznij (pełne zdrowie)", "Śpij", Rest);
                    GUILayout.Label("Gra zapisuje się też sama co 30 s i przy wyjściu.", MenuSkin.Label);
                    break;
            }

            GUILayout.EndScrollView();
            GUILayout.Space(6);
            if (GUILayout.Button("ZAMKNIJ (E / Esc)", MenuSkin.Button, GUILayout.Height(44))) SetOpen(false);
            GUILayout.EndArea();
        }

        void DrawWeapons()
        {
            var wc = _player.Weapons;
            for (int i = 0; i < wc.weapons.Length; i++)
            {
                var d = wc.weapons[i];
                if (d.kind == WeaponKind.Melee) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{i + 1}. {d.name}", MenuSkin.PillLeft, GUILayout.Width(200), GUILayout.Height(40));
                if (wc.Owns(i))
                {
                    string ammoLabel = d.kind == WeaponKind.Throwable || d.kind == WeaponKind.Rocket ? "+1 szt." : "+1 magazynek";
                    if (GUILayout.Button($"{ammoLabel}  ${d.ammoPrice}", MenuSkin.Button, GUILayout.Height(40))) BuyRpc(AmmoOffset + i);
                }
                else
                {
                    if (GUILayout.Button($"Kup  ${d.price}", MenuSkin.Button, GUILayout.Height(40))) BuyRpc(i);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(5);
            }
        }

        void Row(string label, string button, int code)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, MenuSkin.PillLeft, GUILayout.Width(320), GUILayout.Height(40));
            if (GUILayout.Button(button, MenuSkin.Button, GUILayout.Height(40))) BuyRpc(code);
            GUILayout.EndHorizontal();
            GUILayout.Space(5);
        }

        // ================= TUNING (warsztat, w aucie) =================

        bool _tuneOpen;

        Vehicle TuneVehicle()
        {
            if (_player == null || _player.IsDead || !_player.InVehicle) return null;
            var v = _player.Vehicles.CurrentVehicle;
            if (v == null || v.Heli != null) return null;
            return ShopZone.Find(v.transform.position, ShopType.Repair) != null ? v : null;
        }

        void SetTune(bool open)
        {
            _tuneOpen = open;
            GameInput.UIOpen = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void TuneRpc(int kind)
        {
            var v = TuneVehicle();
            if (v == null) { Result("Wjedź autem do warsztatu", false); return; }
            if (kind < 0 || kind > 1) return;
            int lvl = kind == 0 ? v.EngineLevel.Value : v.HpLevel.Value;
            if (lvl >= Vehicle.TuneMax) { Result("Maksymalny poziom", false); return; }
            int price = (kind == 0 ? Vehicle.EnginePrices : Vehicle.HpPrices)[lvl];
            if (!_player.Stats.TrySpend(price)) { Result("Za mało pieniędzy", false); return; }
            v.Upgrade(kind);
            Result(kind == 0 ? $"Silnik: poziom {lvl + 1}" : $"Wytrzymałość: poziom {lvl + 1}", true);
        }

        void DrawTune()
        {
            MenuSkin.Ensure();
            var v = TuneVehicle();
            if (v == null) return;
            float w = Mathf.Min(660, Screen.width - 30), h = 470;
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            MenuSkin.DrawPanel(area);
            float x = area.x + 26, cw = area.width - 52, y = area.y + 14;
            HudSkin.Outline(new Rect(area.x, y, area.width, 46), "TUNING", HudSkin.Text(34), Color.white, 2f); y += 48;
            GUI.Box(new Rect(x, y, cw, 36), $"{v.displayName}   {v.maxSpeed * 3.6f:0} km/h   HP {v.Health.Current.Value:0}/{v.Health.maxHealth:0}   Gotówka ${_player.Stats.Money.Value:N0}", MenuSkin.Pill);
            y += 48;

            DrawTuneRow(ref y, x, cw, "SILNIK", "+25% mocy i +12% prędkości za poziom", v.EngineLevel.Value, Vehicle.EnginePrices, 0);
            DrawTuneRow(ref y, x, cw, "WYTRZYMAŁOŚĆ", "+40% HP za poziom (auto naprawia się przy ulepszeniu)", v.HpLevel.Value, Vehicle.HpPrices, 1);

            GUI.Label(new Rect(x, y, cw, 44), "Ulepszenia dotyczą tego auta. Wyjedź z warsztatu i zostaną w nim.", new GUIStyle(MenuSkin.Label) { fontSize = 14 });
            if (GUI.Button(new Rect(x, area.yMax - 64, cw, 48), "ZAMKNIJ (E)", MenuSkin.Button)) SetTune(false);
        }

        void DrawTuneRow(ref float y, float x, float cw, string title, string desc, int level, int[] prices, int kind)
        {
            var row = new Rect(x, y, cw, 118);
            HudSkin.Draw(HudSkin.PillDark, row);
            HudSkin.Outline(new Rect(row.x + 22, row.y + 8, 300, 32), title, HudSkin.Text(22, TextAnchor.MiddleLeft), Color.white, 1.4f);
            HudSkin.Outline(new Rect(row.x + 22, row.y + 40, row.width - 44, 24), desc, HudSkin.Text(14, TextAnchor.MiddleLeft), new Color(1f, 0.85f, 0.4f), 1.1f);
            // pasek poziomu
            for (int i = 0; i < Vehicle.TuneMax; i++)
            {
                var pip = new Rect(row.x + 22 + i * 52, row.y + 74, 46, 26);
                GUI.color = HudSkin.Black; GUI.DrawTexture(pip, Texture2D.whiteTexture);
                GUI.color = i < level ? HudSkin.Orange : new Color(0.25f, 0.2f, 0.18f);
                GUI.DrawTexture(new Rect(pip.x + 2, pip.y + 2, pip.width - 4, pip.height - 4), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            bool max = level >= Vehicle.TuneMax;
            GUI.enabled = !max && _player.Stats.Money.Value >= prices[Mathf.Min(level, prices.Length - 1)];
            if (GUI.Button(new Rect(row.xMax - 230, row.y + 66, 208, 42), max ? "MAKSYMALNY" : $"ULEPSZ  ${prices[level]:N0}", MenuSkin.Button)) TuneRpc(kind);
            GUI.enabled = true;
            y += 126;
        }

        // ================= RULETKA =================

        bool _rouOpen, _spinning;
        int _betType = -1, _betVal, _betAmt = 50, _resNum = -2, _resPay, _lastNum = -1, _lastPay;
        float _spinStart, _lastSpinServer;

        static readonly HashSet<int> RedNumbers = new HashSet<int> { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };
        static bool IsRed(int n) => RedNumbers.Contains(n);

        /// <summary>Mnożnik zwrotu (razem ze stawką): 0 = przegrana.</summary>
        static int RoulettePayout(int type, int val, int n)
        {
            if (type == 0) return n == val ? 36 : 0;
            if (n == 0) return 0;
            switch (type)
            {
                case 1: return IsRed(n) ? 2 : 0;
                case 2: return !IsRed(n) ? 2 : 0;
                case 3: return n % 2 == 0 ? 2 : 0;
                case 4: return n % 2 == 1 ? 2 : 0;
                case 5: return n <= 18 ? 2 : 0;
                case 6: return n >= 19 ? 2 : 0;
                case 7: return (n - 1) / 12 == val ? 3 : 0;
            }
            return 0;
        }

        void SetRoulette(bool open)
        {
            _rouOpen = open;
            GameInput.UIOpen = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SpinRpc(int betType, int betVal, int amount)
        {
            if (RouletteTable.Near(transform.position) == null || _player.IsDead || _player.InVehicle) { Result("Podejdź do stołu", false); SpinResultRpc(-1, 0); return; }
            bool valid = (betType == 0 && betVal >= 0 && betVal <= 36) || (betType >= 1 && betType <= 6) || (betType == 7 && betVal >= 0 && betVal <= 2);
            if (!valid || amount < 10 || amount > 5000 || Time.time - _lastSpinServer < 1.5f) { SpinResultRpc(-1, 0); return; }
            if (!_player.Stats.TrySpend(amount)) { Result("Za mało pieniędzy na ten zakład", false); SpinResultRpc(-1, 0); return; }
            _lastSpinServer = Time.time;
            int number = Random.Range(0, 37);
            int payout = amount * RoulettePayout(betType, betVal, number);
            if (payout > 0) _player.Stats.AddMoney(payout);
            SpinResultRpc(number, payout);
        }

        [Rpc(SendTo.Owner)]
        void SpinResultRpc(int number, int payout)
        {
            _resNum = number; _resPay = payout;
            if (number < 0) { _spinning = false; _resNum = -2; }
            if (Application.isBatchMode) Debug.Log($"[ROULETTE] number={number} payout={payout}");
        }

        string BetName()
        {
            switch (_betType)
            {
                case 0: return "Numer " + _betVal + "  (x36)";
                case 1: return "Czerwone  (x2)";
                case 2: return "Czarne  (x2)";
                case 3: return "Parzyste  (x2)";
                case 4: return "Nieparzyste  (x2)";
                case 5: return "1-18  (x2)";
                case 6: return "19-36  (x2)";
                case 7: return new[] { "1-12", "13-24", "25-36" }[Mathf.Clamp(_betVal, 0, 2)] + "  (x3)";
            }
            return "wybierz zakład";
        }

        void DrawRoulette()
        {
            MenuSkin.Ensure();
            float w = Mathf.Min(800, Screen.width - 30), h = Mathf.Min(640, Screen.height - 30);
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            MenuSkin.DrawPanel(area);
            float x0 = area.x + 24, cw = area.width - 48, y = area.y + 14;

            HudSkin.Outline(new Rect(area.x, y, area.width, 46), "RULETKA", HudSkin.Text(34), Color.white, 2f); y += 50;
            GUI.Box(new Rect(x0, y, cw, 36), $"Gotówka: ${_player.Stats.Money.Value:N0}", MenuSkin.Pill); y += 44;

            // wynik / kręcenie
            if (_spinning && _resNum != -2 && Time.unscaledTime - _spinStart > 2.2f)
            {
                _spinning = false; _lastNum = _resNum; _lastPay = _resPay;
                if (_resPay > 0) HUD.Notify($"Ruletka: {_lastNum} – wygrywasz ${_resPay:N0}!", Color.green);
                else HUD.Notify($"Ruletka: {_lastNum} – przegrana", new Color(1f, 0.4f, 0.4f));
            }
            int shown = _spinning ? (int)(Time.unscaledTime * 17f) % 37 : _lastNum;
            if (_spinning) RouletteTable.SpinBoostUntil = Time.time + 0.5f;
            var res = new Rect(x0, y, cw, 54);
            GUI.Box(res, "", MenuSkin.Pill);
            if (shown >= 0)
            {
                var chip = new Rect(res.x + 8, res.y + 6, 56, 42);
                GUI.color = shown == 0 ? new Color(0.1f, 0.6f, 0.2f) : IsRed(shown) ? new Color(0.8f, 0.1f, 0.1f) : new Color(0.08f, 0.08f, 0.08f);
                GUI.DrawTexture(chip, Texture2D.whiteTexture); GUI.color = Color.white;
                HudSkin.Outline(chip, shown.ToString(), HudSkin.Text(26), Color.white, 1.5f);
            }
            string msg = _spinning ? "Kulka się kręci..." : _lastNum < 0 ? "Postaw zakład i zakręć" : _lastPay > 0 ? $"Wypadło {_lastNum} – wygrana ${_lastPay:N0}!" : $"Wypadło {_lastNum} – przegrana";
            HudSkin.Outline(new Rect(res.x + 76, res.y, res.width - 84, res.height), msg, HudSkin.Text(22, TextAnchor.MiddleLeft), _lastPay > 0 && !_spinning ? new Color(0.5f, 1f, 0.4f) : Color.white, 1.5f);
            y += 64;

            // plansza: 0 + 12 kolumn x 3 rzędy
            float cell = Mathf.Min(54f, (cw - 8) / 13f), ch = 40f;
            float gy = y;
            DrawBetCell(new Rect(x0 + 4, gy, cell - 3, ch * 3 - 3), 0);
            for (int c = 0; c < 12; c++)
                for (int r = 0; r < 3; r++)
                    DrawBetCell(new Rect(x0 + 4 + cell + c * cell, gy + r * ch, cell - 3, ch - 3), c * 3 + (3 - r));
            y += ch * 3 + 8;

            // zakłady zewnętrzne
            string[] names = { "1-12", "13-24", "25-36" };
            float bw = (cw - 8) / 3f;
            for (int i = 0; i < 3; i++) OutsideBet(new Rect(x0 + i * (bw + 4), y, bw, 34), names[i], 7, i);
            y += 40;
            string[] n2 = { "1-18", "PARZYSTE", "CZERWONE", "CZARNE", "NIEPARZYSTE", "19-36" };
            int[] t2 = { 5, 3, 1, 2, 4, 6 };
            float bw2 = (cw - 5 * 4) / 6f;
            for (int i = 0; i < 6; i++) OutsideBet(new Rect(x0 + i * (bw2 + 4), y, bw2, 34), n2[i], t2[i], 0);
            y += 46;

            // stawka
            int[] amounts = { 10, 50, 100, 500, 1000 };
            float aw = (cw - 4 * 6) / 5f;
            for (int i = 0; i < amounts.Length; i++)
                if (GUI.Button(new Rect(x0 + i * (aw + 6), y, aw, 36), "$" + amounts[i], _betAmt == amounts[i] ? MenuSkin.TabActive : MenuSkin.Tab)) _betAmt = amounts[i];
            y += 46;

            GUI.Box(new Rect(x0, y, cw, 36), $"Zakład: {BetName()}   stawka ${_betAmt}", MenuSkin.Pill); y += 46;

            GUI.enabled = !_spinning && _betType >= 0 && _player.Stats.Money.Value >= _betAmt;
            if (GUI.Button(new Rect(x0, y, cw * 0.62f - 4, 50), _spinning ? "KRĘCI SIĘ..." : "KRĘĆ", MenuSkin.Button))
            {
                _spinning = true; _resNum = -2; _spinStart = Time.unscaledTime;
                SpinRpc(_betType, _betVal, _betAmt);
            }
            GUI.enabled = true;
            if (GUI.Button(new Rect(x0 + cw * 0.62f + 4, y, cw * 0.38f - 4, 50), "ZAMKNIJ (E)", MenuSkin.Button)) SetRoulette(false);
        }

        void DrawBetCell(Rect r, int number)
        {
            bool sel = _betType == 0 && _betVal == number;
            GUI.color = HudSkin.Black; GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = sel ? HudSkin.Yellow : new Color(0.55f, 0.3f, 0.1f); GUI.DrawTexture(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), Texture2D.whiteTexture);
            GUI.color = number == 0 ? new Color(0.1f, 0.55f, 0.2f) : IsRed(number) ? new Color(0.78f, 0.1f, 0.1f) : new Color(0.1f, 0.1f, 0.1f);
            GUI.DrawTexture(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), Texture2D.whiteTexture);
            GUI.color = Color.white;
            HudSkin.Outline(r, number.ToString(), HudSkin.Text(18), Color.white, 1f);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { _betType = 0; _betVal = number; }
        }

        void OutsideBet(Rect r, string label, int type, int val)
        {
            bool sel = _betType == type && (type != 7 || _betVal == val);
            var st = new GUIStyle(sel ? MenuSkin.TabActive : MenuSkin.Tab) { fontSize = 14 };
            if (GUI.Button(r, label, st)) { _betType = type; _betVal = val; }
        }

        // ================= SERWER =================

        /// <summary>Publiczne opakowanie – kup przedmiot (wywołuje właściciel).</summary>
        public void RequestBuy(int item)
        {
            if (IsOwner) BuyRpc(item);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void BuyRpc(int item)
        {
            var zone = MenuZone;
            if (zone == null || _player.IsDead || _player.InVehicle) { Result("Musisz stać w sklepie", false); return; }

            var wc = _player.Weapons;
            var stats = _player.Stats;
            var hp = _player.Health;

            // --- kryjówka ---
            if (item == SaveGame && zone.type == ShopType.Safehouse)
            {
                stats.SaveNow();
                Result("Gra zapisana", true);
                return;
            }
            if (item == Rest && zone.type == ShopType.Safehouse)
            {
                if (Time.time - _lastRestTime < 30f) { Result("Jeszcze nie jesteś zmęczony", false); return; }
                _lastRestTime = Time.time;
                hp.Heal(1000f);
                Result("Wypoczęty – pełne zdrowie", true);
                return;
            }

            // --- kasyno: przedmioty VIP (zamiast jedzenia) ---
            if (zone.casino && item >= CasinoVest && item <= CasinoAmmo)
            {
                int slot = item == CasinoVest ? BackpackItems.Vest : item == CasinoMedkit ? BackpackItems.Medkit : BackpackItems.AmmoPack;
                int price = item == CasinoVest ? VipVestPrice : item == CasinoMedkit ? VipMedkitPrice : VipAmmoPrice;
                BuyToBackpack(stats, slot, price);
                return;
            }
            if (zone.casino) return; // w kasynie nie ma jedzenia

            // --- jedzenie ---
            if (item >= FoodOffset && item < SaveGame)
            {
                var food = FoodFor(zone.type);
                int i = item - FoodOffset;
                if (i < 0 || i >= food.Length) return;
                BuyToBackpack(stats, food[i].heal >= 20f ? BackpackItems.Food : BackpackItems.Drink, food[i].price, food[i].name);
                return;
            }

            if (item == Medkit && (zone.type == ShopType.Weapons || zone.type == ShopType.Store))
            {
                BuyToBackpack(stats, BackpackItems.Medkit, zone.medkitPrice);
                return;
            }

            if (zone.type != ShopType.Weapons) return;

            if (item == ArmorItem)
            {
                BuyToBackpack(stats, BackpackItems.Vest, zone.armorPrice);
                return;
            }

            bool ammo = item >= AmmoOffset;
            int idx = ammo ? item - AmmoOffset : item;
            if (idx < 0 || idx >= wc.weapons.Length || wc.weapons[idx].kind == WeaponKind.Melee) return;
            var d = wc.weapons[idx];

            if (ammo)
            {
                if (!wc.Owns(idx)) { Result("Najpierw kup broń", false); return; }
                if (!stats.TrySpend(d.ammoPrice)) { Result("Za mało pieniędzy", false); return; }
                wc.AddAmmoServer(idx, 1);
                Result($"Amunicja: {d.name}", true);
            }
            else
            {
                if (wc.Owns(idx)) { Result("Już masz tę broń", false); return; }
                if (!stats.TrySpend(d.price)) { Result("Za mało pieniędzy", false); return; }
                wc.GrantWeaponServer(idx);
                Result($"Kupiono: {d.name}", true);
            }
        }

        /// <summary>Kupiony przedmiot trafia do plecaka (użyjesz go klawiszem I / Z / X).</summary>
        void BuyToBackpack(PlayerStats stats, int slot, int price, string label = null)
        {
            if (stats.PackCount(slot) >= PlayerStats.PackMax) { Result("Plecak pełny: " + BackpackItems.Names[slot], false); return; }
            if (!stats.TrySpend(price)) { Result("Za mało pieniędzy", false); return; }
            stats.AddPackItem(slot, 1);
            Result($"W plecaku: {label ?? BackpackItems.Names[slot]} (I)", true);
        }

        void Result(string text, bool ok)
        {
            var fs = new FixedString64Bytes();
            fs.CopyFromTruncated(text);
            ResultRpc(fs, ok);
        }

        [Rpc(SendTo.Owner)]
        void ResultRpc(FixedString64Bytes text, bool ok)
        {
            HUD.Notify(text.ToString(), ok ? Color.green : new Color(1f, 0.4f, 0.4f));
            if (Application.isBatchMode) Debug.Log($"[SHOP] {text} ok={ok}");
        }

        /// <summary>Warsztat: wjazd autem do strefy = naprawa (i zgubienie gwiazdek).</summary>
        void UpdateRepairServer()
        {
            var v = _player.Vehicles != null ? _player.Vehicles.CurrentVehicle : null;
            var zone = v != null ? ShopZone.Find(v.transform.position, ShopType.Repair) : null;
            if (zone == null) { _repairZoneInside = null; return; }
            if (zone == _repairZoneInside) return; // już obsłużony – musi wyjechać i wjechać ponownie

            if (zone.clearsWanted && _player.Wanted.Level > 0 && _player.Wanted.SeenByPolice.Value)
            {
                // nie oznaczaj jako obsłużony – spróbuj ponownie, gdy policja straci cię z oczu
                if (Time.time > _repairRetryTime) Result("Policja cię widzi! Zgub ich najpierw.", false);
                _repairRetryTime = Time.time + 3f;
                return;
            }
            _repairZoneInside = zone;
            bool needsWork = v.Health.Current.Value < v.Health.maxHealth || (zone.clearsWanted && _player.Wanted.Level > 0);
            if (!needsWork) { Result("Auto jest sprawne", true); return; }
            if (!_player.Stats.TrySpend(zone.repairPrice)) { Result($"Naprawa kosztuje ${zone.repairPrice}", false); return; }

            bool hadStars = _player.Wanted.Level > 0;
            v.Health.Heal(10000f);
            if (zone.clearsWanted) _player.Wanted.ClearWanted();
            Result(zone.clearsWanted && hadStars ? "Naprawiono – policja cię zgubiła!" : "Naprawiono auto", true);
        }
    }
}
