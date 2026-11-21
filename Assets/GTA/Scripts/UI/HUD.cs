using System.Collections.Generic;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Cały interfejs w OnGUI (zero konfiguracji): zdrowie, kasa, gwiazdki, broń, prędkość,
    /// cel misji, powiadomienia, "WASTED", minimapa i nicki nad graczami.
    /// Docelowo możesz to przepisać na Canvas/UI Toolkit – logika gry nie zależy od HUD.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        static HUD _instance;

        [Header("Minimapa")]
        public int minimapSize = 200;
        public float minimapZoom = 60f;

        class Note { public string text; public Color color; public float until; }
        readonly List<Note> _notes = new List<Note>();
        string _bigText;
        Color _bigColor;
        float _bigUntil;
        bool _showHelp = true;

        Camera _miniCam;
        RenderTexture _miniRT;
        Texture2D _white;
        GUIStyle _bold, _big, _small, _stars;
        float _blipRefresh;
        readonly List<(Vector3 pos, Color color, string glyph)> _blips = new List<(Vector3, Color, string)>();

        void Awake()
        {
            _instance = this;
            _white = Texture2D.whiteTexture;
            if (GetComponent<InventoryUI>() == null) gameObject.AddComponent<InventoryUI>();
            if (GetComponent<Collectibles>() == null) gameObject.AddComponent<Collectibles>();
            if (GetComponent<Courier>() == null) gameObject.AddComponent<Courier>();
            if (GetComponent<Bribe>() == null) gameObject.AddComponent<Bribe>();
        }

        public static void Notify(string text, Color color)
        {
            if (_instance == null) return;
            _instance._notes.Add(new Note { text = text, color = color, until = Time.time + 4f });
            if (_instance._notes.Count > 6) _instance._notes.RemoveAt(0);
        }

        public static void ShowBigMessage(string text, Color color, float duration)
        {
            if (_instance == null) return;
            _instance._bigText = text;
            _instance._bigColor = color;
            _instance._bigUntil = Time.time + duration;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) _showHelp = !_showHelp;
            var pl = PlayerController.Local;
            if (pl != null && pl.IsSpawned && !pl.IsDead)
            {
                int interest = Story.TickInterest(Time.deltaTime);
                if (interest > 0) Notify($"Odsetki: +${interest:N0} do długu", new Color(1f, 0.45f, 0.35f));
            }
            if (Input.GetKeyDown(KeyCode.M) && !GameInput.ConsoleOpen && !GameInput.PauseOpen) SetFullMap(!_fullMap);
            UpdateMinimapCamera();
            if (_fullMap) UpdateFullMapCamera();
        }

        // ---------- duża mapa (M) ----------

        bool _fullMap;
        Camera _mapCam;
        RenderTexture _mapRT;
        Vector3 _mapCenter;
        float _mapExtent = 300f, _nextMapRender;

        /// <summary>Otwiera/zamyka dużą mapę. Otwarta mapa odblokowuje kursor (klik = znacznik) i nie wywołuje menu pauzy.</summary>
        void SetFullMap(bool open)
        {
            _fullMap = open;
            GameInput.UIOpen = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        void OnDisable()
        {
            if (_fullMap) SetFullMap(false);
        }

        void UpdateFullMapCamera()
        {
            if (_mapCam == null)
            {
                // zasięg mapy z sieci dróg (albo domyślny)
                var net = RoadNetwork.Instance;
                if (net != null && net.NodeCount > 0)
                {
                    var b = new Bounds(net.nodes[0], Vector3.zero);
                    foreach (var n in net.nodes) b.Encapsulate(n);
                    // mapa musi obejmować też cele misji (np. wyjazd na obwodnicę), nawet poza siecią dróg
                    foreach (var ms in FindObjectsByType<Mission>(FindObjectsSortMode.None))
                    {
                        b.Encapsulate(ms.transform.position);
                        if (ms.steps == null) continue;
                        foreach (var st in ms.steps) if (st != null && st.target != null) b.Encapsulate(st.target.position);
                    }
                    _mapCenter = b.center;
                    _mapExtent = Mathf.Max(b.extents.x, b.extents.z) + 30f;
                }
                _mapRT = new RenderTexture(1024, 1024, 16);
                var go = new GameObject("FullMapCamera");
                _mapCam = go.AddComponent<Camera>();
                _mapCam.orthographic = true;
                _mapCam.targetTexture = _mapRT;
                _mapCam.clearFlags = CameraClearFlags.SolidColor;
                _mapCam.backgroundColor = new Color(0.12f, 0.16f, 0.12f);
                _mapCam.enabled = false; // renderujemy ręcznie
                _mapCam.farClipPlane = 600f;
            }
            if (Time.time < _nextMapRender) return;
            _nextMapRender = Time.time + 1f;
            _mapCam.orthographicSize = _mapExtent;
            _mapCam.transform.SetPositionAndRotation(_mapCenter + Vector3.up * 300f, Quaternion.Euler(90f, 0f, 0f));
            _mapCam.Render();
        }

        void DrawFullMap(PlayerController p)
        {
            float size = Mathf.Min(Screen.width, Screen.height) * 0.88f;
            var rect = new Rect((Screen.width - size) / 2f, (Screen.height - size) / 2f, size, size);
            GUI.color = new Color(0, 0, 0, 0.85f);
            GUI.DrawTexture(new Rect(rect.x - 4, rect.y - 4, size + 8, size + 8), _white);
            GUI.color = Color.white;
            if (_mapRT != null) GUI.DrawTexture(rect, _mapRT);

            // klik w mapę: lewy = postaw znacznik (klik tuż przy znaczniku go usuwa), prawy = usuń
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && rect.Contains(ev.mousePosition))
            {
                float u = (ev.mousePosition.x - rect.x) / rect.width, v = (rect.yMax - ev.mousePosition.y) / rect.height;
                var wp = new Vector3(_mapCenter.x + (u - 0.5f) * _mapExtent * 2f, 0f, _mapCenter.z + (v - 0.5f) * _mapExtent * 2f);
                if (ev.button == 1) MapMarker.Clear();
                else if (MapMarker.Has && Vector3.Distance(wp, MapMarker.Pos) < _mapExtent * 0.03f) MapMarker.Clear();
                else MapMarker.Set(wp);
                ev.Use();
            }

            if (Time.time > _blipRefresh) RefreshBlips(p);
            if (MapMarker.Has) DrawMapBlip(rect, MapMarker.Pos, new Color(0.3f, 0.9f, 1f), 16, "X");
            foreach (var b in _blips) DrawMapBlip(rect, b.pos, b.color, string.IsNullOrEmpty(b.glyph) ? 9 : 22, b.glyph);
            if (p.Mission != null && p.Mission.InMission && p.Mission.HasTarget.Value) DrawMapBlip(rect, p.Mission.TargetPos.Value, Color.yellow, 14);
            // gracz: strzałka obrócona w kierunku patrzenia
            Vector3 f = Quaternion.Euler(0f, HeadingYaw(p), 0f) * Vector3.forward;
            {
                float pu = Mathf.Clamp01((p.transform.position.x - _mapCenter.x) / (_mapExtent * 2f) + 0.5f), pv = Mathf.Clamp01((p.transform.position.z - _mapCenter.z) / (_mapExtent * 2f) + 0.5f);
                HudSkin.DrawPlayerArrow(rect.x + pu * rect.width, rect.yMax - pv * rect.height, 40f, HeadingYaw(p));
            }

            Shadowed(new Rect(rect.x, rect.yMax - 30, size, 26),
                "MAPA (M zamyka)   klik – ustaw znacznik, prawy klik – usuń   B – broń   W – warsztat   S – sklep 24/7   D – bar   K – kryjówka   $ – kasyno   ! – misja fabularna   ? – zlecenie   czerwony punkt – policja",
                new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter }, Color.white);
        }

        GUIStyle _glyphStyle;

        /// <summary>Litera / symbol na znaczniku mapy (kolorowy kwadrat + biały znak).</summary>
        void DrawGlyph(Rect r, string glyph)
        {
            if (string.IsNullOrEmpty(glyph)) return;
            if (_glyphStyle == null) _glyphStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _glyphStyle.fontSize = Mathf.RoundToInt(r.height * 0.78f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), glyph, new GUIStyle(_glyphStyle) { normal = { textColor = Color.black } });
            GUI.Label(r, glyph, _glyphStyle);
        }

        static string ShopGlyph(ShopZone z)
        {
            if (z.shopName != null && z.shopName.StartsWith("Kasyno")) return "$";
            switch (z.type)
            {
                case ShopType.Weapons: return "B";
                case ShopType.Repair: return "W";
                case ShopType.Store: return "S";
                case ShopType.Bar: return "D";
                default: return "K";
            }
        }

        void DrawMapBlip(Rect rect, Vector3 world, Color c, float size, string glyph = null)
        {
            float u = (world.x - _mapCenter.x) / (_mapExtent * 2f) + 0.5f;
            float v = (world.z - _mapCenter.z) / (_mapExtent * 2f) + 0.5f;
            // punkt poza mapą przyklejamy do krawędzi zamiast go gubić
            u = Mathf.Clamp(u, 0.02f, 0.98f); v = Mathf.Clamp(v, 0.02f, 0.98f);
            float x = rect.x + u * rect.width, y = rect.yMax - v * rect.height;
            var ic = HudSkin.MapIcon(glyph);
            if (ic != null) { HudSkin.MapIconAt(ic, x, y, Mathf.Max(30f, size * 1.9f), glyph == "X"); return; }
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(x - size / 2 - 1, y - size / 2 - 1, size + 2, size + 2), _white);
            GUI.color = c;
            GUI.DrawTexture(new Rect(x - size / 2, y - size / 2, size, size), _white);
            GUI.color = Color.white;
            DrawGlyph(new Rect(x - size / 2, y - size / 2, size, size), glyph);
        }

        void InitStyles()
        {
            if (_bold != null) return;
            MenuSkin.Ensure();
            _bold = new GUIStyle(GUI.skin.label) { fontSize = 20 };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            _big = new GUIStyle(GUI.skin.label) { fontSize = 64, alignment = TextAnchor.MiddleCenter };
            _stars = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.UpperRight };
            _bold.font = MenuSkin.UIFont; _small.font = MenuSkin.UIFont; _big.font = MenuSkin.UIFont;
        }

        void OnGUI()
        {
            var p = PlayerController.Local;
            if (p == null || !p.IsSpawned) return;
            if (DialogueUI.Cinematic) return;   // w katscenie bez HUD
            InitStyles();

            _k = Mathf.Clamp(Screen.height / 1080f, 0.7f, 2f);
            var oldM = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_k, _k, 1f));
            DrawTopRight(p);
            DrawMission(p);
            DrawBottomRight(p);
            if (GameSettings.ShowMinimap) DrawMinimap(p);
            DrawNotes();
            GUI.matrix = oldM;
            if (!_fullMap) MissionWaypoint.Draw(p);
            DrawNameTags();
            if (!p.InVehicle && !p.IsDead) DrawCrosshair();
            else if (p.InVehicle && !p.IsDead)
            {
                var cv = p.Vehicles.CurrentVehicle;
                if (cv != null && (cv.GetComponent<Tank>() != null || cv.Heli != null)) DrawVehicleCrosshair(cv.Heli != null);
            }
            // opis sterowania jest teraz w menu pauzy (Esc) – patrz NetworkUI, HUD.ControlsText
            if (_fullMap) DrawFullMap(p);

            if (Time.time < _bigUntil && !string.IsNullOrEmpty(_bigText))
                Shadowed(new Rect(0, Screen.height * 0.3f, Screen.width, 100), _bigText, _big, _bigColor);
        }

        // ---------- sekcje (rysowane w skali względem wysokości ekranu 1080) ----------

        float VW => Screen.width / _k;
        float VH => Screen.height / _k;
        float _k = 1f;

        void DrawTopRight(PlayerController p)
        {
            float W = VW;
            var panel = new Rect(W - 330, 18, 316, 132);
            HudSkin.Draw(HudSkin.Dark, panel);
            var money = HudSkin.Text(46, TextAnchor.MiddleRight);
            HudSkin.Outline(new Rect(panel.x + 8, panel.y + 6, panel.width - 24, 62), "$" + p.Stats.Money.Value.ToString("N0"), money, new Color(0.4f, 1f, 0.3f), 2.2f);

            var debt = new Rect(panel.x + 16, panel.y + 78, panel.width - 32, 40);
            if (Story.Debt > 0)
            {
                HudSkin.Draw(HudSkin.Debt, debt);
                HudSkin.Outline(new Rect(debt.x + 12, debt.y, 120, debt.height), "DŁUG:", HudSkin.Text(18, TextAnchor.MiddleLeft), Color.white, 1.5f);
                HudSkin.Outline(new Rect(debt.x, debt.y, debt.width - 12, debt.height), "$" + Story.Debt.ToString("N0"), HudSkin.Text(24, TextAnchor.MiddleRight), new Color(1f, 0.32f, 0.25f), 1.8f);
            }
            else
            {
                HudSkin.Draw(HudSkin.Strip, debt);
                HudSkin.Outline(debt, "DŁUG SPŁACONY", HudSkin.Text(20), new Color(0.45f, 1f, 0.4f), 1.6f);
            }

            // gwiazdki poszukiwania + zegar
            int stars = p.Wanted.Level;
            bool flash = stars > 0 && !p.Wanted.SeenByPolice.Value && Mathf.Repeat(Time.time * 2f, 1f) > 0.5f;
            var clock = new Rect(W - 168, 160, 150, 56);
            float sx = clock.x - 6 - WantedSystem.MaxStars * 42f;
            for (int i = 0; i < WantedSystem.MaxStars; i++)
                HudSkin.Icon(i < stars && !flash ? HudSkin.StarOn : HudSkin.StarOff, new Rect(sx + i * 42f, 162, 44, 44));

            var dn = DayNightCycle.Instance;
            if (dn != null)
            {
                HudSkin.Draw(HudSkin.PillDark, clock);
                HudSkin.Icon(HudSkin.Sun, new Rect(clock.x + 10, clock.y + 9, 38, 38));
                HudSkin.Outline(new Rect(clock.x + 50, clock.y, clock.width - 58, clock.height), dn.ClockText, HudSkin.Text(24), Color.white, 1.6f);
            }
        }

        void DrawMission(PlayerController p)
        {
            var m = p.Mission;
            bool inMission = m != null && m.InMission;
            string title, line;
            if (inMission) { title = m.MissionName.Value.ToString().ToUpper(); line = m.Objective.Value.ToString(); }
            else
            {
                var sug = MissionWaypoint.Suggested;
                if (sug == null || DialogueUI.Active) return;
                title = sug.isStory ? "NOWA MISJA" : "ZLECENIE";
                line = sug.missionName;
            }
            float W = VW;
            var banner = new Rect(W / 2f - 300, 24, 600, 112);
            HudSkin.Draw(HudSkin.Frame, banner);
            int fs = title.Length > 26 ? 24 : title.Length > 18 ? 32 : 40;
            HudSkin.Outline(new Rect(banner.x, banner.y + 2, banner.width, 58), title, HudSkin.Text(fs), Color.white, 2.2f);
            var strip = new Rect(banner.x + 28, banner.y + 61, banner.width - 56, 38);
            HudSkin.Draw(HudSkin.Strip, strip);
            int ls = line != null && line.Length > 46 ? 15 : 19;
            HudSkin.Outline(strip, line, HudSkin.Text(ls), new Color(1f, 0.9f, 0.2f), 1.4f);
            if (inMission && m.SecondsLeft.Value >= 0)
            {
                int sec = m.SecondsLeft.Value;
                var tr = new Rect(W / 2f - 64, banner.yMax + 6, 128, 46);
                HudSkin.Draw(HudSkin.PillDark, tr);
                HudSkin.Outline(tr, $"{sec / 60}:{sec % 60:00}", HudSkin.Text(26), sec < 10 ? new Color(1f, 0.3f, 0.25f) : Color.white, 1.6f);
            }
        }

        void DrawBottomRight(PlayerController p)
        {
            float W = VW, H = VH;
            var panel = new Rect(W - 372, H - 132, 354, 114);
            var v = p.Vehicles.CurrentVehicle;
            var small = HudSkin.Text(17, TextAnchor.MiddleRight);
            if (v != null)
            {
                HudSkin.Draw(HudSkin.Dark, panel);
                HudSkin.Outline(new Rect(panel.x + 16, panel.y + 10, panel.width - 34, 24), $"{v.displayName.ToUpper()}   {v.Health.Current.Value:0} HP", small, new Color(1f, 0.62f, 0.15f), 1.4f);
                HudSkin.Outline(new Rect(panel.x + 16, panel.y + 34, panel.width - 126, 70), $"{v.SpeedKmh:0}", HudSkin.Text(54, TextAnchor.MiddleRight), Color.white, 2.4f);
                HudSkin.Outline(new Rect(panel.xMax - 116, panel.y + 52, 100, 40), "km/h", HudSkin.Text(22, TextAnchor.MiddleLeft), new Color(0.8f, 0.76f, 0.72f), 1.6f);
            }
            else if (p.Weapons != null)
            {
                HudSkin.Draw(HudSkin.Dark, panel);
                var w = p.Weapons.Current;
                var slot = new Rect(panel.x + 18, panel.y + 15, 122, 84);
                HudSkin.Draw(HudSkin.Slot, slot);
                HudSkin.Icon(HudSkin.WeaponIcon(w), new Rect(slot.x + 8, slot.y + 8, slot.width - 16, slot.height - 16));
                HudSkin.Outline(new Rect(panel.x + 150, panel.y + 10, panel.width - 168, 24), w.name.ToUpper(), small, new Color(1f, 0.62f, 0.15f), 1.4f);
                if (p.Weapons.IsReloading)
                    HudSkin.Outline(new Rect(panel.x + 150, panel.y + 40, panel.width - 168, 56), "PRZEŁADOWANIE", HudSkin.Text(22, TextAnchor.MiddleRight), Color.white, 1.8f);
                else if (w.startAmmo < 0)
                    HudSkin.Outline(new Rect(panel.x + 150, panel.y + 40, panel.width - 168, 56), "--", HudSkin.Text(46, TextAnchor.MiddleRight), Color.white, 2.2f);
                else
                {
                    var rs = HudSkin.Text(26, TextAnchor.MiddleRight);
                    string reserve = "/ " + p.Weapons.AmmoReserve;
                    float rw = rs.CalcSize(new GUIContent(reserve)).x + 4f;
                    HudSkin.Outline(new Rect(panel.xMax - 18 - rw, panel.y + 50, rw, 44), reserve, rs, new Color(0.82f, 0.78f, 0.74f), 1.6f);
                    HudSkin.Outline(new Rect(panel.x + 150, panel.y + 36, panel.width - 168 - rw - 4, 64), p.Weapons.AmmoInMag.ToString(), HudSkin.Text(54, TextAnchor.MiddleRight), Color.white, 2.4f);
                }
            }
        }

        /// <summary>Kierunek, w którym jedzie / patrzy postać (w aucie – kierunek auta, bo postać w środku nie obraca się).</summary>
        static float HeadingYaw(PlayerController p)
        {
            if (p.InVehicle && p.Vehicles != null && p.Vehicles.CurrentVehicle != null) return p.Vehicles.CurrentVehicle.transform.eulerAngles.y;
            return p.transform.eulerAngles.y;
        }

        void DrawMinimap(PlayerController p)
        {
            float size = minimapSize * 1.3f;
            var frame = new Rect(14, 14, size + 30, size + 30);
            HudSkin.Draw(HudSkin.Frame, frame);
            var rect = new Rect(frame.x + 15, frame.y + 15, size, size);
            GUI.color = new Color(0.1f, 0.05f, 0.02f, 0.9f);
            GUI.DrawTexture(new Rect(rect.x - 3, rect.y - 3, size + 6, size + 6), _white);
            GUI.color = Color.white;
            if (_miniRT != null) GUI.DrawTexture(rect, _miniRT);

            float yaw = Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f;
            Vector3 center = p.transform.position;

            if (Time.time > _blipRefresh) RefreshBlips(p);
            foreach (var b in _blips) DrawBlip(rect, center, yaw, b.pos, b.color, string.IsNullOrEmpty(b.glyph) ? 6 : 15, true, b.glyph);

            // cel misji (przyklejony do krawędzi, jeśli daleko)
            if (p.Mission != null && p.Mission.InMission && p.Mission.HasTarget.Value)
                DrawBlip(rect, center, yaw, p.Mission.TargetPos.Value, Color.yellow, 10, true);

            if (MapMarker.Has) DrawBlip(rect, center, yaw, MapMarker.Pos, new Color(0.3f, 0.9f, 1f), 12, true, "X");

            // gracz w środku
            GUI.color = Color.white;
            HudSkin.DrawPlayerArrow(rect.center.x, rect.center.y, 34f, HeadingYaw(p) - yaw);

            // N krąży po ramce i zawsze wskazuje północ
            Vector3 north = Quaternion.Euler(0f, -yaw, 0f) * Vector3.forward;
            Vector2 dir = new Vector2(north.x, -north.z);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.down;
            float kk = (frame.width * 0.5f - 6f) / Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y));
            Vector2 np = frame.center + dir * kk;
            HudSkin.Badge(new Rect(np.x - 24, np.y - 24, 48, 48), "N", Color.white, 22);

            // paski: zdrowie (serce) i pancerz (tarcza) pod ramką
            float y0 = frame.yMax + 6;
            DrawStatBar(new Rect(frame.x, y0, frame.width, 44), HudSkin.Heart, p.Health.Percent, HudSkin.FillGreen, p.Health.Percent < 0.3f && Mathf.Repeat(Time.time * 2f, 1f) > 0.5f);
            DrawStatBar(new Rect(frame.x, y0 + 40, frame.width, 44), HudSkin.Shield, Mathf.Clamp01(p.Health.Armor.Value / Health.MaxArmor), HudSkin.FillBlue, false);
        }

        void DrawStatBar(Rect r, Texture2D icon, float pct, GUIStyle fill, bool warn)
        {
            var track = new Rect(r.x + 28, r.y + 7, r.width - 28, 30);
            HudSkin.Draw(HudSkin.Track, track);
            float fw = (track.width - 8) * Mathf.Clamp01(pct);
            if (fw > 8f)
            {
                GUI.color = warn ? new Color(1f, 0.4f, 0.4f) : Color.white;
                HudSkin.Draw(fill, new Rect(track.x + 4, track.y + 4, fw, track.height - 8));
                GUI.color = Color.white;
            }
            GUI.DrawTexture(new Rect(r.x, r.y, 44, 44), HudSkin.Circle);
            HudSkin.Icon(icon, new Rect(r.x + 8, r.y + 8, 28, 28));
        }

        void RefreshBlips(PlayerController self)
        {
            _blipRefresh = Time.time + 0.25f;
            _blips.Clear();
            foreach (var o in PlayerController.All)
                if (o != self) _blips.Add((o.transform.position, new Color(0.3f, 0.6f, 1f), null));
            foreach (var npc in CombatNpcAI.All)
                if (npc != null && npc.IsSpawned)
                    _blips.Add((npc.transform.position, npc.Faction == Faction.Police ? new Color(1f, 0.2f, 0.2f) : new Color(1f, 0.5f, 0f), npc.Faction == Faction.Police ? "P" : null));
            if (!GameSettings.ShowMapIcons) return; // ikony sklepów i misji można wyłączyć w menu
            if (Courier.Has) _blips.Add((Courier.Target, new Color(1f, 0.6f, 0.1f), "X"));
            foreach (var pk in Collectibles.Active)
                if ((pk - self.transform.position).sqrMagnitude < 150f * 150f) _blips.Add((pk, HudSkin.Gold, "$"));
            foreach (var shop in ShopZone.All)
                _blips.Add((shop.transform.position, shop.TypeColor * 0.75f + new Color(0f, 0f, 0f, 0.25f), ShopGlyph(shop)));
            foreach (var m in FindObjectsByType<Mission>(FindObjectsSortMode.None))
                if (m.Available && (self.Mission == null || !self.Mission.InMission)) _blips.Add((m.transform.position, m.isStory ? new Color(0.9f, 0.25f, 0.1f) : new Color(0.85f, 0.7f, 0.05f), m.isStory ? "!" : "?"));
        }

        void DrawBlip(Rect rect, Vector3 center, float yaw, Vector3 world, Color c, float size, bool clampToEdge, string glyph = null)
        {
            Vector3 rel = Quaternion.Euler(0f, -yaw, 0f) * (world - center);
            float scale = rect.width * 0.5f / minimapZoom;
            Vector2 pt = new Vector2(rel.x * scale, -rel.z * scale);
            float half = rect.width * 0.5f - size * 0.5f;
            if (Mathf.Abs(pt.x) > half || Mathf.Abs(pt.y) > half)
            {
                if (!clampToEdge) return;
                float k = half / Mathf.Max(Mathf.Abs(pt.x), Mathf.Abs(pt.y));
                pt *= k;
            }
            var ic = HudSkin.MapIcon(glyph);
            if (ic != null) { HudSkin.MapIconAt(ic, rect.center.x + pt.x, rect.center.y + pt.y, Mathf.Max(26f, size * 1.7f), glyph == "X"); return; }
            GUI.color = c;
            var br = new Rect(rect.center.x + pt.x - size / 2, rect.center.y + pt.y - size / 2, size, size);
            GUI.DrawTexture(br, _white);
            GUI.color = Color.white;
            DrawGlyph(br, glyph);
        }

        void DrawNotes()
        {
            _notes.RemoveAll(n => Time.time > n.until);
            // Powiadomienia zaczynają się pod minimapą i paskami zdrowia
            float y = 14 + minimapSize * 1.3f + 30 + 100f;
            foreach (var n in _notes)
            {
                Shadowed(new Rect(20, y, 600, 28), n.text, _bold, n.color);
                y += 26;
            }
        }

        void DrawNameTags()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var style = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            foreach (var o in PlayerController.All)
            {
                if (o == PlayerController.Local || o.InVehicle) continue;
                Vector3 sp = cam.WorldToScreenPoint(o.transform.position + Vector3.up * 2.3f);
                if (sp.z < 0 || sp.z > 60) continue;
                string label = o.Stats.PlayerName.Value.ToString();
                if (o.Wanted.Level > 0) label += " " + new string('★', o.Wanted.Level);
                Shadowed(new Rect(sp.x - 100, Screen.height - sp.y - 12, 200, 24), label, style, new Color(0.5f, 0.8f, 1f));
            }
        }

        /// <summary>Celownik czołgu (okrąg + krzyż) i helikoptera (krzyż z przerwą + kropka).</summary>
        void DrawVehicleCrosshair(bool heli)
        {
            if (!GameSettings.ShowCrosshair) return;
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            var col = heli ? new Color(1f, 0.85f, 0.2f, 0.95f) : new Color(1f, 0.45f, 0.2f, 0.95f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            DrawCross(cx + 1, cy + 1, heli); // cień
            GUI.color = col;
            DrawCross(cx, cy, heli);
            GUI.DrawTexture(new Rect(cx - 2, cy - 2, 4, 4), _white);
            GUI.color = Color.white;
        }

        void DrawCross(float cx, float cy, bool heli)
        {
            float gap = heli ? 10f : 14f, len = heli ? 22f : 30f, th = 2f;
            GUI.DrawTexture(new Rect(cx - gap - len, cy - th / 2f, len, th), _white);
            GUI.DrawTexture(new Rect(cx + gap, cy - th / 2f, len, th), _white);
            GUI.DrawTexture(new Rect(cx - th / 2f, cy - gap - len, th, len), _white);
            GUI.DrawTexture(new Rect(cx - th / 2f, cy + gap, th, len), _white);
            if (!heli)
            {
                // okrąg z kwadratów (czołg)
                const int n = 40; float r = gap + 2f;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    GUI.DrawTexture(new Rect(cx + Mathf.Cos(a) * r - 1, cy + Mathf.Sin(a) * r - 1, 2, 2), _white);
                }
            }
        }

        void DrawCrosshair()
        {
            if (!GameSettings.ShowCrosshair) return;
            float s = GameInput.Aim ? 6 : 4;
            GUI.color = new Color(1, 1, 1, 0.85f);
            GUI.DrawTexture(new Rect(Screen.width / 2f - s / 2, Screen.height / 2f - s / 2, s, s), _white);
            GUI.color = Color.white;
        }

        /// <summary>Lista klawiszy pokazywana w menu pauzy (Esc).</summary>
        public const string ControlsText =
            "WASD – ruch / jazda\n" +
            "Shift – sprint\n" +
            "Spacja – skok / hamulec ręczny\n" +
            "Mysz – kamera\n" +
            "LPM – strzał / cios\n" +
            "PPM – celowanie\n" +
            "1–9 / kółko myszy – wybór broni (8 granat, 9 RPG)\n" +
            "0 – schowaj / wyjmij broń\n" +
            "R – przeładuj / postaw przewrócone auto\n" +
            "F – wsiądź / wysiądź\n" +
            "J – zlecenie kurierskie (ponownie: anuluj)\n" +
            "K – łapówka dla policji (400 zł za gwiazdkę, gdy cię nie widzą)\n" +
            "G – syrena (radiowóz)\n" +
            "H – klakson\n" +
            "E – sklep / bar / kryjówka (w kolorowym kręgu)\n" +
            "PPM na kasjera z bronią – napad (sklepy 24/7 i kasyno)\n" +
            "E w aucie w warsztacie – tuning (silnik, wytrzymałość)\n" +
            "M – duża mapa\n" +
            "I – plecak, Tab (przytrzymaj) – koło wyboru broni\n" +
            "Z – szybkie leczenie, X – kamizelka z plecaka\n" +
            "F2 – przebieralnia\n" +
            "` – konsola\n" +
            "Esc – to menu\n" +
            "\n" +
            "Mapa: zielony = broń, niebieski = warsztat, pomarańczowy = 24/7, fioletowy = bar, biały = kryjówka, czerwony/żółty = misje";

        void DrawHelp()
        {
            const string help =
                "WASD – ruch / jazda    Shift – sprint    Spacja – skok / ręczny    F – wsiądź / wysiądź    G – syrena\n" +
                "Mysz – kamera    LPM – strzał / rzut    PPM – celowanie    1-9 / kółko – broń (8 granat, 9 RPG), 0 – schowaj broń    R – przeładuj / postaw auto\n" +
                "E – sklep / bar / kryjówka    M – mapa    Esc – kursor / menu    F1 – ukryj pomoc\n" +
                "Mapa: zielony = broń, niebieski = warsztat, pomarańczowy = 24/7 (napad!), fioletowy = bar, biały = kryjówka, żółty = misje.";
            Shadowed(new Rect(240, Screen.height - 95, 900, 90), help, _small, new Color(1, 1, 1, 0.8f));
        }

        static void Shadowed(Rect r, string text, GUIStyle style, Color color)
        {
            HudSkin.Outline(r, text, style, color, 1.6f);
        }

        // ---------- kamera minimapy ----------

        void UpdateMinimapCamera()
        {
            var p = PlayerController.Local;
            if (p == null) return;
            if (_miniCam == null)
            {
                _miniRT = new RenderTexture(256, 256, 16);
                var go = new GameObject("MinimapCamera");
                _miniCam = go.AddComponent<Camera>();
                _miniCam.orthographic = true;
                _miniCam.targetTexture = _miniRT;
                _miniCam.clearFlags = CameraClearFlags.SolidColor;
                _miniCam.backgroundColor = new Color(0.15f, 0.2f, 0.15f);
                _miniCam.depth = -10;
                _miniCam.allowHDR = false;
                _miniCam.allowMSAA = false;
            }
            _miniCam.orthographicSize = minimapZoom;
            float yaw = Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f;
            _miniCam.transform.SetPositionAndRotation(p.transform.position + Vector3.up * 150f, Quaternion.Euler(90f, yaw, 0f));
            _miniCam.farClipPlane = 300f;
        }
    }
}