using UnityEngine;

namespace MiniGTA
{
    /// <summary>Przedmioty w plecaku (liczba sztuk trzymana w PlayerStats.Pack, zapisywana razem z grą).</summary>
    public static class BackpackItems
    {
        public const int Medkit = 0, Vest = 1, Food = 2, Drink = 3, AmmoPack = 4;
        public static readonly string[] Names = { "Apteczka", "Kamizelka kuloodporna", "Jedzenie", "Napój energetyczny", "Pakiet amunicji" };
        public static readonly string[] Desc = { "Przywraca całe zdrowie", "Pełny pancerz", "+35 zdrowia", "+15 zdrowia", "+1 magazynek do każdej broni" };
        public static readonly string[] Keys = { "", "X", "", "", "" };
    }

    /// <summary>
    /// Plecak (klawisz I) i koło wyboru broni (przytrzymaj Tab, wskaż myszą, puść). Skróty: Z – szybkie leczenie, X – kamizelka.
    /// Dołącza się sam do obiektu HUD.
    /// </summary>
    public class InventoryUI : MonoBehaviour
    {
        bool _pack, _wheel;
        float _debtFrac = 0.5f;
        int _hover = -1;
        float _k = 1f;

        void SetPack(bool open)
        {
            _pack = open;
            GameInput.UIOpen = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        void SetWheel(bool open)
        {
            _wheel = open;
            _hover = -1;
            GameInput.UIOpen = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        void CloseAll()
        {
            if (_pack) SetPack(false);
            if (_wheel) SetWheel(false);
        }

        void OnDisable() { if (_pack || _wheel) CloseAll(); }

        void Update()
        {
            var p = PlayerController.Local;
            if (p == null || !p.IsSpawned) { if (_pack || _wheel) CloseAll(); return; }
            bool blocked = GameInput.ConsoleOpen || GameInput.PauseOpen || DialogueUI.Active || p.IsDead;
            if (blocked && (_pack || _wheel)) { CloseAll(); return; }

            // plecak
            if (Input.GetKeyDown(KeyCode.I) && !blocked)
            {
                if (_pack) SetPack(false);
                else if (!GameInput.UIOpen && !_wheel) SetPack(true);
            }
            if (_pack && Input.GetKeyDown(KeyCode.Escape)) SetPack(false);

            // koło broni
            if (!blocked && !_pack && !p.InVehicle && p.Weapons != null && Input.GetKeyDown(KeyCode.Tab) && !GameInput.UIOpen) SetWheel(true);
            if (_wheel && (Input.GetKeyUp(KeyCode.Tab) || p.InVehicle))
            {
                if (_hover >= 0 && !p.InVehicle && p.Weapons != null) p.Weapons.SelectWeapon(_hover);
                SetWheel(false);
            }

            // skróty
            if (!blocked && !GameInput.UIOpen && !GameInput.Blocked)
            {
                if (Input.GetKeyDown(KeyCode.Z)) p.Stats.QuickHeal();
                if (Input.GetKeyDown(KeyCode.X)) p.Stats.RequestUsePack(BackpackItems.Vest);
            }
        }

        void OnGUI()
        {
            var p = PlayerController.Local;
            if (p == null || !p.IsSpawned || p.IsDead) return;
            MenuSkin.Ensure();
            _k = Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_k, _k, 1f));
            float W = Screen.width / _k, H = Screen.height / _k;

            if (_wheel) DrawWheel(p, W, H);
            else if (_pack) DrawPack(p, W, H);
            else if (!GameInput.UIOpen && !DialogueUI.Active && !GameInput.ConsoleOpen)
            {
                // mała podpowiedź przy dolnej krawędzi
                var st = HudSkin.Text(15, TextAnchor.MiddleLeft);
                HudSkin.Outline(new Rect(22, H - 34, 520, 24), "[I] plecak   [TAB] wybór broni   [Z] leczenie", st, new Color(1f, 1f, 1f, 0.75f), 1.2f);
            }
            GUI.matrix = old;
        }

        // ---------- koło broni ----------

        void DrawWheel(PlayerController p, float W, float H)
        {
            var wc = p.Weapons;
            if (wc == null) return;
            var owned = new System.Collections.Generic.List<int>();
            for (int i = 0; i < wc.weapons.Length; i++) if (wc.Owns(i)) owned.Add(i);
            if (owned.Count == 0) return;

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
            GUI.color = Color.white;

            Vector2 c = new Vector2(W / 2f, H / 2f);
            int n = owned.Count;
            float R = Mathf.Min(300f, H * 0.34f);

            // wskazanie myszą
            Vector2 m = Event.current.mousePosition - c;
            _hover = -1;
            if (m.magnitude > 55f)
            {
                float a = Mathf.Atan2(m.x, -m.y); if (a < 0) a += Mathf.PI * 2f;
                int idx = Mathf.RoundToInt(a / (Mathf.PI * 2f / n)) % n;
                _hover = owned[idx];
            }

            // środek: nazwa i amunicja
            int shown = _hover >= 0 ? _hover : wc.CurrentWeapon.Value;
            var cr = new Rect(c.x - 150, c.y - 62, 300, 124);
            HudSkin.Draw(HudSkin.Dark, cr);
            var data = wc.weapons[shown];
            HudSkin.Outline(new Rect(cr.x, cr.y + 14, cr.width, 40), data.name.ToUpper(), HudSkin.Text(26), Color.white, 2f);
            string ammo;
            if (data.kind == WeaponKind.Melee) ammo = "broń biała";
            else { wc.GetAmmo(shown, out int mag, out int res); ammo = mag + " / " + res; }
            HudSkin.Outline(new Rect(cr.x, cr.y + 60, cr.width, 40), ammo, HudSkin.Text(24), new Color(1f, 0.85f, 0.3f), 1.6f);

            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                Vector2 pos = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * R;
                var r = new Rect(pos.x - 78, pos.y - 52, 156, 104);
                bool hov = owned[i] == _hover, cur = owned[i] == wc.CurrentWeapon.Value;
                if (hov) { r = new Rect(r.x - 8, r.y - 6, r.width + 16, r.height + 12); }
                HudSkin.Draw(hov ? HudSkin.Frame : HudSkin.Slot, r);
                HudSkin.Icon(HudSkin.WeaponIcon(wc.weapons[owned[i]]), new Rect(r.x + 14, r.y + 8, r.width - 28, r.height - 40));
                HudSkin.Outline(new Rect(r.x, r.yMax - 32, r.width, 24), wc.weapons[owned[i]].name, HudSkin.Text(14), cur ? HudSkin.Yellow : Color.white, 1.2f);
            }
            HudSkin.Outline(new Rect(0, H - 60, W, 30), "Wskaż myszą i puść TAB", HudSkin.Text(18), new Color(1f, 1f, 1f, 0.8f), 1.4f);
        }

        // ---------- plecak ----------

        void DrawPack(PlayerController p, float W, float H)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.4f);
            GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var area = new Rect(W / 2f - 340, H / 2f - 385, 680, 770);
            MenuSkin.DrawPanel(area);
            float x = area.x + 26, w = area.width - 52, y = area.y + 14;
            HudSkin.Outline(new Rect(area.x, y, area.width, 48), "PLECAK", HudSkin.Text(36), Color.white, 2.2f); y += 54;

            var hp = p.Health;
            GUI.Box(new Rect(x, y, w, 38), $"Gotówka: ${p.Stats.Money.Value:N0}     HP: {hp.Current.Value:0}/{hp.maxHealth:0}     Pancerz: {hp.Armor.Value:0}", MenuSkin.Pill); y += 50;

            for (int i = 0; i < PlayerStats.PackItems; i++)
            {
                int cnt = p.Stats.PackCount(i);
                var row = new Rect(x, y, w, 70);
                HudSkin.Draw(HudSkin.PillDark, row);
                HudSkin.Outline(new Rect(row.x + 22, row.y + 6, 380, 32), BackpackItems.Names[i], HudSkin.Text(21, TextAnchor.MiddleLeft), Color.white, 1.4f);
                HudSkin.Outline(new Rect(row.x + 22, row.y + 36, 380, 26), BackpackItems.Desc[i] + (BackpackItems.Keys[i] != "" ? $"   [{BackpackItems.Keys[i]}]" : ""), HudSkin.Text(14, TextAnchor.MiddleLeft), new Color(1f, 0.85f, 0.4f), 1.1f);
                HudSkin.Outline(new Rect(row.x + 400, row.y, 90, row.height), $"x{cnt}", HudSkin.Text(26), cnt > 0 ? Color.white : new Color(0.6f, 0.55f, 0.5f), 1.6f);
                GUI.enabled = cnt > 0;
                if (GUI.Button(new Rect(row.xMax - 150, row.y + 12, 130, 46), "UŻYJ", MenuSkin.Button)) p.Stats.RequestUsePack(i);
                GUI.enabled = true;
                y += 78;
            }

            // spłata długu: przelej część gotówki na dług
            int debt = Story.Debt, cash = p.Stats.Money.Value;
            int maxPay = Mathf.Min(Story.Payable, cash);
            var drow = new Rect(x, y, w, 118);
            HudSkin.Draw(HudSkin.PillDark, drow);
            HudSkin.Outline(new Rect(drow.x + 22, drow.y + 6, 420, 30), "SPŁATA DŁUGU", HudSkin.Text(21, TextAnchor.MiddleLeft), Color.white, 1.4f);
            HudSkin.Outline(new Rect(drow.x, drow.y + 6, drow.width - 22, 30), $"dług: ${debt:N0}  (+{Story.InterestPerMinute * 100f:0}%/min)", HudSkin.Text(19, TextAnchor.MiddleRight), new Color(1f, 0.4f, 0.3f), 1.3f);
            if (debt <= 0) HudSkin.Outline(new Rect(drow.x, drow.y + 40, drow.width, 60), "Dług spłacony", HudSkin.Text(24), new Color(0.5f, 1f, 0.4f), 1.5f);
            else if (Story.Payable < 10) HudSkin.Outline(new Rect(drow.x, drow.y + 40, drow.width, 60), "Resztę długu spłacisz po wykonaniu wszystkich zadań", HudSkin.Text(17), new Color(1f, 0.6f, 0.4f), 1.2f);
            else if (maxPay < 10) HudSkin.Outline(new Rect(drow.x, drow.y + 40, drow.width, 60), "Za mało gotówki, żeby coś przelać", HudSkin.Text(17), new Color(1f, 0.85f, 0.4f), 1.2f);
            else
            {
                _debtFrac = MenuSkin.Slider(new Rect(drow.x + 22, drow.y + 46, drow.width - 44, 24), _debtFrac);
                int amount = Mathf.Clamp(Mathf.RoundToInt(_debtFrac * maxPay / 10f) * 10, 10, maxPay);
                HudSkin.Outline(new Rect(drow.x + 22, drow.y + 74, 330, 36), $"Przelej: ${amount:N0} z ${cash:N0}", HudSkin.Text(17, TextAnchor.MiddleLeft), Color.white, 1.2f);
                if (GUI.Button(new Rect(drow.xMax - 300, drow.y + 74, 130, 36), "PRZELEJ", MenuSkin.Button)) p.Stats.RequestPayDebt(amount);
                if (GUI.Button(new Rect(drow.xMax - 160, drow.y + 74, 138, 36), "MAKSYMALNIE", new GUIStyle(MenuSkin.Button) { fontSize = 15 })) p.Stats.RequestPayDebt(maxPay);
            }
            y += 126;

            GUI.Label(new Rect(x, y, w, 26), $"Maksymalnie {PlayerStats.PackMax} sztuk każdego przedmiotu. Kupujesz je w sklepach i barach.", new GUIStyle(MenuSkin.Label) { fontSize = 14 });
            if (GUI.Button(new Rect(x, area.yMax - 64, w, 48), "ZAMKNIJ (I)", MenuSkin.Button)) SetPack(false);
        }
    }
}
