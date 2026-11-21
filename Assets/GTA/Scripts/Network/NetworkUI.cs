using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Menu głównej gry (SINGLEPLAYER): Kontynuuj / Nowa gra / Postać / Wyjdź.
    /// Pod spodem gra startuje jako lokalny host (127.0.0.1, port nie jest dostępny z zewnątrz),
    /// więc cały kod sieciowy działa bez zmian. Parametry linii poleceń (-host, -client, -server, -ip, -port, -name)
    /// nadal działają – używają ich testy automatyczne i serwer dedykowany.
    /// </summary>
    public class NetworkUI : MonoBehaviour
    {
        public static string LocalPlayerName = "Gracz";

        public string defaultAddress = "127.0.0.1";
        public ushort defaultPort = 7777;

        string _address, _port, _name;
        string _status = "";
        bool _showCustomizer;
        bool _newGameConfirm;
        CharacterPreview _preview;
        bool _showSettings;
        Texture2D _bg;
        bool _bgSearched;
        GUIStyle _hoverStyle;
        Vector2 _partsScroll;
        int _customTab;

        void Awake()
        {
            _preview = new GameObject("CharacterPreview").AddComponent<CharacterPreview>();
            DontDestroyOnLoad(_preview.gameObject);
            _address = defaultAddress;
            _port = defaultPort.ToString();
            _name = PlayerPrefs.GetString("mp_name", "Gracz");

            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-port" && i + 1 < args.Length) _port = args[i + 1];
                if ((args[i] == "-ip" || args[i] == "-address") && i + 1 < args.Length) _address = args[i + 1];
                if (args[i] == "-name" && i + 1 < args.Length) _name = args[i + 1];
                if (args[i] == "-server") Invoke(nameof(StartServer), 0.1f);
                if (args[i] == "-host") Invoke(nameof(StartHostLan), 0.1f);
                if (args[i] == "-client") Invoke(nameof(StartClient), 0.5f);
            }
        }

        /// <summary>Menu pauzy jest otwarte (Esc odblokował kursor), nie sklep/konsola/dialog.</summary>
        bool PauseMenuVisible =>
            Cursor.lockState != CursorLockMode.Locked && !GameInput.UIOpen && !GameInput.ConsoleOpen && !DialogueUI.Active;

        bool _exitPending;

        void OnDisable() { Time.timeScale = 1f; } // nigdy nie zostawiaj gry zatrzymanej po wyjściu z Play

        void Update()
        {
            var nm = NetworkManager.Singleton;
            // pauza: po Esc (menu pauzy) i w przebieralni świat stoi; w menu głównym i poza grą czas płynie normalnie
            bool inGame = nm != null && nm.IsListening && PlayerController.Local != null;
            GameInput.PauseOpen = inGame && PauseMenuVisible;
            Time.timeScale = inGame && (PauseMenuVisible || _showCustomizer || DialogueUI.Cinematic || EndingUI.Active) ? 0f : 1f;
            if (_exitPending)
            {
                _exitPending = false;
                if (nm != null) nm.Shutdown();
                _showCustomizer = false; _showSettings = false;
                GameInput.UIOpen = false; GameInput.PauseOpen = false;
                Time.timeScale = 1f;
            }
            if (_pendingNew && _showCustomizer && Input.GetKeyDown(KeyCode.Escape)) CancelNewGame();
            if (nm == null || !nm.IsListening)
            {
                // menu główne: kursor zawsze wolny, żeby dało się klikać
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }
            // F2 – przebieralnia w trakcie gry
            if (Input.GetKeyDown(KeyCode.F2) && !GameInput.ConsoleOpen) SetCustomizer(!_showCustomizer);
        }

        bool _pendingNew;

        /// <summary>Nowa gra: najpierw przebieralnia (wybór postaci), potem start i cutscenka z wybraną postacią.</summary>
        void BeginNewGame() { _pendingNew = true; SetCustomizer(true); }

        void SetCustomizer(bool open)
        {
            _showCustomizer = open;
            if (_preview != null) _preview.SetActive(open);
            GameInput.UIOpen = open;
            if (open) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }

        void OnGUI()
        {
            GUI.depth = -1000; // rysuj na samym wierzchu (nad HUD, mapą i minimapą)
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            if (!nm.IsClient && !nm.IsServer)
            {
                if (_showCustomizer) { DrawBackground(); DrawCustomizer(true); }
                else if (_showSettings) { DrawBackground(); PauseMenuUI.DrawSettings(() => _showSettings = false, DrawHeroTab); }
                else DrawMenu();
                return;
            }

            if (_showCustomizer) { DrawCustomizer(false); return; }

            // w grze: Esc odblokowuje kursor – wtedy pokazujemy menu pauzy (styl jak na wzorze)
            if (PauseMenuVisible)
            {
                PauseMenuUI.Draw(
                    resume: () => { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; },
                    wardrobe: () => SetCustomizer(true),
                    saveExit: () =>
                    {
                        var p = PlayerController.Local;
                        if (p != null) SaveSystem.Save(p);
                        _exitPending = true;   // zamknięcie sieci dopiero w Update – nie w środku OnGUI
                    });
            }
        }

        // ================= MENU GŁÓWNE =================

        // ---------- menu główne: obraz jako tło, przyciski nałożone na narysowane w obrazie ----------

        // układ obrazu 1920 × 1080 (piksele): lewa półprzezroczysta kolumna z czterema przyciskami
        static readonly Rect BtnNew = new Rect(87, 472, 421, 76);
        static readonly Rect BtnSettings = new Rect(87, 565, 421, 76);
        static readonly Rect BtnQuit = new Rect(87, 662, 421, 76);
        static readonly Rect BtnContinue = new Rect(87, 755, 421, 76);

        bool _bgImage;
        // pola przycisków w grafice 1920×1080: NOWA GRA, USTAWIENIA, WYJDŹ Z GRY, KONTYNUUJ
        static readonly Rect ImgNew = new Rect(224, 558, 520, 64), ImgSettings = new Rect(224, 630, 520, 64),
                             ImgQuit = new Rect(224, 705, 520, 64), ImgContinue = new Rect(224, 778, 520, 64);

        Texture2D Background
        {
            get
            {
                if (!_bgSearched)
                {
                    _bg = Resources.Load<Texture2D>("MenuBackground4K");   // gotowa grafika menu (4K) z logiem i przyciskami
                    _bgImage = _bg != null;
                    if (_bg == null) _bg = MenuArt.Get();
                    _bgSearched = true;
                }
                return _bg;
            }
        }

        /// <summary>Rysuje tło na cały ekran (przycięte do proporcji) i zwraca funkcję zamiany współrzędnych obrazu na ekran.</summary>
        bool DrawBackground()
        {
            var bg = Background;
            if (bg == null) return false;
            // proporcje podajemy jawnie (import tekstury mógł ją rozciągnąć do potęgi dwójki)
            // wypełnienie pasów po bokach: ten sam obraz powiększony na cały ekran i mocno przyciemniony
            GUI.color = new Color(0.08f, 0.07f, 0.08f, 1f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), bg, ScaleMode.ScaleAndCrop);
            GUI.color = Color.white;
            GUI.DrawTexture(Map(new Rect(0, 0, 1920, 1080)), bg, ScaleMode.StretchToFill);
            return true;
        }

        /// <summary>Zakładka BOHATER w ustawieniach: imię i przejście do przebieralni.</summary>
        void DrawHeroTab(Rect r)
        {
            GUI.Label(new Rect(r.x, r.y, r.width, 30), "IMIĘ BOHATERA", MenuSkin.Label);
            var fieldStyle = new GUIStyle(MenuSkin.PillLeft) { fontSize = 22 };
            fieldStyle.focused.background = fieldStyle.normal.background;
            fieldStyle.focused.textColor = Color.white;
            _name = GUI.TextField(new Rect(r.x, r.y + 34, r.width, 44), _name, 20, fieldStyle);
            if (GUI.Button(new Rect(r.x, r.y + 96, r.width, 50), "POSTAĆ (KOLORY I UBRANIA)", MenuSkin.Button)) { _showSettings = false; SetCustomizer(true); }
        }

        Rect Map(Rect imageRect)
        {
            float s = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f); // cały obraz w kadrze (pasy po bokach)
            float ox = (Screen.width - 1920f * s) * 0.5f, oy = (Screen.height - 1080f * s) * 0.5f;
            return new Rect(ox + imageRect.x * s, oy + imageRect.y * s, imageRect.width * s, imageRect.height * s);
        }

        Texture2D _barTex, _arrowTex;
        GUIStyle _menuLabel;

        void BuildHighlightTextures()
        {
            if (_barTex != null) return;
            _barTex = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 256; x++)
            {
                float t = x / 255f;
                float a = Mathf.Clamp01(Mathf.Min(t, 1f - t) / 0.12f);
                _barTex.SetPixel(x, 0, new Color(0.98f, 0.66f, 0.12f, 0.92f * a));
            }
            _barTex.Apply();
            _arrowTex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    // trójkąt wskazujący w prawo
                    float half = (1f - x / 31f) * 15.5f;
                    bool inside = Mathf.Abs(y - 15.5f) <= half;
                    _arrowTex.SetPixel(x, y, inside ? new Color(1f, 0.82f, 0.2f, 1f) : new Color(0, 0, 0, 0));
                }
            _arrowTex.Apply();
        }

        void DrawMenuHighlights(bool hasSave)
        {
            if (Event.current.type != EventType.Repaint) return;
            BuildHighlightTextures();
            var bg = Background;

            // 1) zakryj "zapalony" przycisk NOWA GRA wyciętym z grafiki pustym pasem między przyciskami
            // (grafika ma już wyczyszczone pole NOWA GRA – bez baked podświetlenia i napisu)

            // 2) podświetlenie pod kursorem
            var rects = new[] { ImgNew, ImgSettings, ImgQuit, ImgContinue };
            var names = new[] { "NOWA GRA", "USTAWIENIA", "WYJDŹ Z GRY", "KONTYNUUJ" };
            Vector2 mp = Event.current.mousePosition;
            float s = Map(new Rect(0, 0, 1, 1)).width;
            if (_menuLabel == null) _menuLabel = new GUIStyle(MenuSkin.Label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _menuLabel.fontSize = Mathf.RoundToInt(38f * s);
            for (int i = 0; i < 4; i++)
            {
                var r = Map(rects[i]);
                bool enabled = !(i == 3 && !hasSave);
                bool hover = enabled && r.Contains(mp);
                if (hover)
                {
                    GUI.DrawTexture(new Rect(r.x - 10f * s, r.y + 2f * s, r.width + 20f * s, r.height - 4f * s), _barTex, ScaleMode.StretchToFill);
                    float a = 30f * s;
                    GUI.DrawTexture(new Rect(r.x - 47f * s, r.center.y - a / 2f, a, a), _arrowTex);
                    var right = new Rect(r.xMax + 17f * s, r.center.y - a / 2f, a, a);
                    GUI.DrawTextureWithTexCoords(right, _arrowTex, new Rect(1f, 0f, -1f, 1f));
                }
                _menuLabel.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
                GUI.Label(new Rect(r.x + 2f * s, r.y + 2f * s, r.width, r.height), names[i], _menuLabel);
                _menuLabel.normal.textColor = enabled ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                GUI.Label(r, names[i], _menuLabel);
            }
        }

        bool HitButton(Rect imageRect)
        {
            if (_hoverStyle == null)
            {
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, new Color(1f, 1f, 1f, 0f));
                tex.Apply();
                _hoverStyle = new GUIStyle();
                _hoverStyle.hover.background = tex;
                _hoverStyle.active.background = tex;
            }
            return GUI.Button(Map(imageRect), GUIContent.none, _hoverStyle);
        }

        void DrawMenu()
        {
            if (!DrawBackground()) { DrawMenuClassic(); return; }
            MenuSkin.Ensure();

            // własny tytuł + ciemna kolumna pod przyciskami (tylko dla grafiki rysowanej w kodzie)
            if (!_bgImage)
            {
                float k = Map(new Rect(0, 0, 1, 1)).width;
                var prevCol = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.45f);
                GUI.DrawTexture(Map(new Rect(60, 440, 475, 420)), Texture2D.whiteTexture);
                GUI.color = prevCol;
                var ts = new GUIStyle(MenuSkin.Title) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(120f * k) };
                var shadow = new GUIStyle(ts); shadow.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
                var tr = Map(new Rect(80, 150, 1300, 170));
                GUI.Label(new Rect(tr.x + 5 * k, tr.y + 5 * k, tr.width, tr.height), "OUT OF DEBT", shadow);
                GUI.Label(tr, "OUT OF DEBT", ts);
                GUI.Label(Map(new Rect(90, 320, 900, 50)), "MiniGTA  –  tryb fabularny", new GUIStyle(MenuSkin.Label) { fontSize = Mathf.RoundToInt(30f * k) });
            }

            string nick = string.IsNullOrWhiteSpace(_name) ? "Gracz" : _name.Trim();
            string save = SaveSystem.SavedAt(nick);

            if (_newGameConfirm)
            {
                // potwierdzenie nadpisania zapisu
                var box = new Rect((Screen.width - 520f) / 2f, (Screen.height - 220f) / 2f, 520f, 220f);
                MenuSkin.DrawPanel(box);
                GUI.Label(new Rect(box.x + 24, box.y + 18, 472, 80), "Nowa gra usunie obecny zapis bohatera „" + nick + "”. Na pewno?", MenuSkin.Label);
                if (GUI.Button(new Rect(box.x + 24, box.y + 120, 300, 54), "TAK, OD NOWA", MenuSkin.Button)) { _newGameConfirm = false; BeginNewGame(); }
                if (GUI.Button(new Rect(box.x + 336, box.y + 120, 160, 54), "ANULUJ", MenuSkin.Button)) _newGameConfirm = false;
                return;
            }

            if (_bgImage)
            {
                // grafika ma już narysowane przyciski (NOWA GRA jest w niej "zapalona") – zakrywamy to i podświetlamy tylko ten, na który najedziesz
                DrawMenuHighlights(save != null);
                if (save != null && HitButton(ImgContinue)) StartGame(false);
                if (HitButton(ImgNew)) { if (save != null) _newGameConfirm = true; else BeginNewGame(); }
                if (HitButton(ImgSettings)) _showSettings = true;
#if UNITY_WEBGL && !UNITY_EDITOR
                // w przeglądarce nie da się zamknąć gry – WYJDŹ nie robi nic (zamknij kartę)
                if (false)
#else
                if (HitButton(ImgQuit))
#endif
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                }
                if (!string.IsNullOrEmpty(_status))
                    GUI.Label(Map(new Rect(87, 900, 600, 40)), _status, new GUIStyle(MenuSkin.Label) { fontSize = Mathf.RoundToInt(Map(new Rect(0, 0, 1, 40)).height * 0.5f) });
                return;
            }

            float sc = Map(new Rect(0, 0, 1, 1)).width;
            var bs = new GUIStyle(MenuSkin.Button) { fontSize = Mathf.RoundToInt(32f * sc) };
            GUI.enabled = save != null;
            if (GUI.Button(Map(BtnNew), "KONTYNUUJ", bs)) StartGame(false);
            GUI.enabled = true;
            if (GUI.Button(Map(BtnSettings), "NOWA GRA", bs)) { if (save != null) _newGameConfirm = true; else BeginNewGame(); }
            if (GUI.Button(Map(BtnQuit), "USTAWIENIA", bs)) _showSettings = true;
            if (GUI.Button(Map(BtnContinue), "WYJDŹ", bs))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }

            if (!string.IsNullOrEmpty(_status))
                GUI.Label(Map(new Rect(87, 850, 560, 40)), _status, new GUIStyle(MenuSkin.Label) { fontSize = Mathf.RoundToInt(Map(new Rect(0, 0, 1, 40)).height * 0.5f) });
        }

        void DrawMenuClassic()
        {
            MenuSkin.Ensure();
            string nick = string.IsNullOrWhiteSpace(_name) ? "Gracz" : _name.Trim();
            string save = SaveSystem.SavedAt(nick);

            float w = Mathf.Min(560f, Screen.width - 40f), h = 470f;
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            MenuSkin.DrawPanel(area);

            GUI.Label(new Rect(area.x, area.y + 14, w, 48), "OUT OF DEBT", MenuSkin.Title);
            GUI.Label(new Rect(area.x, area.y + 58, w, 28), "MiniGTA – tryb fabularny", new GUIStyle(MenuSkin.Label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });

            float x = area.x + 24, bw = w - 48, y = area.y + 98;
            GUI.Label(new Rect(x, y, bw, 26), "IMIĘ BOHATERA", MenuSkin.Label); y += 28;
            var fieldStyle = new GUIStyle(MenuSkin.PillLeft) { fontSize = 22 };
            fieldStyle.focused.background = fieldStyle.normal.background;
            fieldStyle.focused.textColor = Color.white;
            _name = GUI.TextField(new Rect(x, y, bw, 40), _name, 20, fieldStyle); y += 46;
            GUI.Label(new Rect(x, y, bw, 26), save != null ? "Zapis z: " + save : "Brak zapisu – zacznij nową grę", new GUIStyle(MenuSkin.Label) { fontSize = 16 }); y += 34;

            GUI.enabled = save != null;
            if (GUI.Button(new Rect(x, y, bw, 46), "KONTYNUUJ", MenuSkin.Button)) StartGame(false);
            GUI.enabled = true; y += 54;

            if (_newGameConfirm)
            {
                GUI.Label(new Rect(x, y, bw, 26), "Nowa gra usunie obecny zapis. Na pewno?", new GUIStyle(MenuSkin.Label) { fontSize = 16 }); y += 28;
                if (GUI.Button(new Rect(x, y, bw * 0.62f - 5, 44), "TAK, ZACZNIJ OD NOWA", MenuSkin.Button)) { _newGameConfirm = false; BeginNewGame(); }
                if (GUI.Button(new Rect(x + bw * 0.62f + 5, y, bw * 0.38f - 5, 44), "ANULUJ", MenuSkin.Button)) _newGameConfirm = false;
                y += 52;
            }
            else
            {
                if (GUI.Button(new Rect(x, y, bw, 46), "NOWA GRA", MenuSkin.Button))
                {
                    if (save != null) _newGameConfirm = true; else BeginNewGame();
                }
                y += 54;
            }

            if (GUI.Button(new Rect(x, y, bw, 46), "POSTAĆ (KOLORY I UBRANIA)", MenuSkin.Button)) SetCustomizer(true);
            y += 54;
            if (GUI.Button(new Rect(x, y, bw, 40), "WYJDŹ", MenuSkin.Button))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
            if (!string.IsNullOrEmpty(_status))
                GUI.Label(new Rect(x, area.yMax - 58, bw, 52), _status, new GUIStyle(MenuSkin.Label) { fontSize = 14 });
        }

        // ================= PRZEBIERALNIA =================

        static readonly string[] SlotNames = { "Skóra", "Włosy / czapka", "Góra", "Dół", "Buty" };
        int _slot;

        static Color Get(int slot)
        {
            switch (slot)
            {
                case 0: return AppearanceSettings.Skin;
                case 1: return AppearanceSettings.Hair;
                case 2: return AppearanceSettings.Top;
                case 3: return AppearanceSettings.Bottom;
                default: return AppearanceSettings.Shoes;
            }
        }

        static void Set(int slot, Color c)
        {
            switch (slot)
            {
                case 0: AppearanceSettings.Skin = c; break;
                case 1: AppearanceSettings.Hair = c; break;
                case 2: AppearanceSettings.Top = c; break;
                case 3: AppearanceSettings.Bottom = c; break;
                default: AppearanceSettings.Shoes = c; break;
            }
        }

        void DrawCustomizer(bool fromMenu)
        {
            MenuSkin.Ensure();
            if (!fromMenu)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            // ---- podgląd postaci po lewej, w ciemnej ramce ----
            float ph = Mathf.Min(Screen.height - 90, 600f), pw = ph * 420f / 620f;
            var prect = new Rect(50f, (Screen.height - ph) / 2f, pw, ph);
            HudSkin.Draw(HudSkin.Slot, new Rect(prect.x - 12, prect.y - 12, prect.width + 24, prect.height + 24));
            if (_preview != null && _preview.Texture != null)
            {
                GUI.DrawTexture(prect, _preview.Texture, ScaleMode.StretchToFill, false);
                var e = Event.current;
                if (e.type == EventType.MouseDrag && prect.Contains(e.mousePosition)) { _preview.Rotate(-e.delta.x * 0.6f); e.Use(); }
            }
            HudSkin.Outline(new Rect(prect.x - 20, prect.yMax + 14, prect.width + 40, 26), "Przeciągnij myszą, żeby obrócić postać", HudSkin.Text(14), Color.white, 1.2f);

            // ---- panel po prawej ----
            float w = 500, h = Mathf.Min(Screen.height - 40, 660f);
            var area = new Rect(Screen.width - w - 40, (Screen.height - h) / 2, w, h);
            MenuSkin.DrawPanel(area);
            GUILayout.BeginArea(new Rect(area.x + 22, area.y + 16, area.width - 44, area.height - 32));
            GUILayout.Label("PRZEBIERALNIA", MenuSkin.Title, GUILayout.Height(46));

            var lib = CharacterPartLibrary.Instance;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("UBRANIA I DODATKI", _customTab == 0 ? MenuSkin.TabActive : MenuSkin.Tab, GUILayout.Height(38))) _customTab = 0;
            if (GUILayout.Button("KOLORY", _customTab == 1 ? MenuSkin.TabActive : MenuSkin.Tab, GUILayout.Height(38))) _customTab = 1;
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            if (_customTab == 0) DrawPartsTab(lib); else DrawColorsTab();

            GUILayout.FlexibleSpace();
            if (GUILayout.Button(_pendingNew ? "GRAJ" : "GOTOWE", MenuSkin.Button, GUILayout.Height(48))) { AppearanceSettings.Save(); SetCustomizer(false); if (_pendingNew) { _pendingNew = false; AppearanceSettings.NewSeed(); CutsceneStage.Reset(); StartGame(true); } }
            if (_pendingNew && GUILayout.Button("WSTECZ", MenuSkin.Button, GUILayout.Height(40))) CancelNewGame();
            GUILayout.EndArea();
        }

        void CancelNewGame()
        {
            _pendingNew = false;
            AppearanceSettings.Save();
            SetCustomizer(false);
        }

        void DrawPartsTab(CharacterPartLibrary lib)
        {
            if (lib == null || lib.categories.Count == 0)
            {
                GUILayout.Label("Brak części postaci. W Unity uruchom MiniGTA → 6 (Podłącz postacie z paczki).", MenuSkin.Label);
                return;
            }

            _partsScroll = GUILayout.BeginScrollView(_partsScroll);
            bool full = false;
            foreach (var c in lib.categories) if (c.isFull && AppearanceSettings.GetPart(c) >= 0) full = true;

            var rowLabel = new GUIStyle(MenuSkin.Label) { fontSize = 17 };
            var valueStyle = new GUIStyle(MenuSkin.Pill) { fontSize = 14 };
            var arrow = new GUIStyle(MenuSkin.Button) { fontSize = 22, padding = new RectOffset(0, 0, 0, 2) };

            bool changed = false;
            foreach (var cat in lib.categories)
            {
                if (cat.parts.Count == 0) continue;
                int cur = Mathf.Clamp(AppearanceSettings.GetPart(cat), -1, cat.parts.Count - 1);
                bool required = cat.id == "face";
                bool hidden = full && cat.hiddenByFull;

                GUILayout.BeginHorizontal();
                GUILayout.Label(cat.label, rowLabel, GUILayout.Width(130), GUILayout.Height(36));
                GUI.enabled = !hidden;
                if (GUILayout.Button("<", arrow, GUILayout.Width(38), GUILayout.Height(36)))
                {
                    cur--;
                    if (cur < (required ? 0 : -1)) cur = cat.parts.Count - 1;
                    AppearanceSettings.SetPart(cat, cur); changed = true;
                }
                string label = hidden ? "(zakryte strojem)" : cur < 0 ? "brak" : CharacterPartLibrary.PartName(cat.parts[cur]);
                GUILayout.Label(label, valueStyle, GUILayout.Width(190), GUILayout.Height(36));
                if (GUILayout.Button(">", arrow, GUILayout.Width(38), GUILayout.Height(36)))
                {
                    cur++;
                    if (cur > cat.parts.Count - 1) cur = required ? 0 : -1;
                    AppearanceSettings.SetPart(cat, cur); changed = true;
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("LOSUJ WYGLĄD", MenuSkin.Button, GUILayout.Height(40)))
            {
                foreach (var cat in lib.categories)
                {
                    if (cat.parts.Count == 0) continue;
                    bool take = cat.id == "face" || cat.id == "pants" || cat.id == "shoes" || Random.value < Mathf.Max(0.35f, cat.npcChance);
                    AppearanceSettings.SetPart(cat, take ? Random.Range(0, cat.parts.Count) : -1);
                }
                changed = true;
            }
            if (GUILayout.Button("DOMYŚLNY", MenuSkin.Button, GUILayout.Height(40)))
            {
                foreach (var cat in lib.categories) AppearanceSettings.SetPart(cat, cat.defaultIndex);
                changed = true;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();

            if (changed) AppearanceSettings.Save();
        }

        void DrawColorsTab()
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < SlotNames.Length; i++)
                if (GUILayout.Button(SlotNames[i].ToUpper(), _slot == i ? MenuSkin.TabActive : MenuSkin.Tab, GUILayout.Height(34))) _slot = i;
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            Color c = Get(_slot);
            Color.RGBToHSV(c, out float hh, out float ss, out float vv);
            bool changed = false;

            Color[] palette = _slot == 0
                ? new[] { Color.white, new Color(1f, 0.87f, 0.75f), new Color(0.85f, 0.65f, 0.47f), new Color(0.65f, 0.45f, 0.3f), new Color(0.45f, 0.3f, 0.2f), new Color(0.32f, 0.2f, 0.14f) }
                : new[] { Color.white, Color.black, new Color(0.8f, 0.1f, 0.1f), new Color(0.95f, 0.5f, 0.1f), new Color(0.95f, 0.85f, 0.2f), new Color(0.2f, 0.7f, 0.25f), new Color(0.15f, 0.4f, 0.9f), new Color(0.55f, 0.2f, 0.75f), new Color(0.9f, 0.4f, 0.65f), new Color(0.45f, 0.3f, 0.2f) };
            GUILayout.BeginHorizontal();
            foreach (var pc in palette)
            {
                var r = GUILayoutUtility.GetRect(34, 34, GUILayout.Width(34), GUILayout.Height(34));
                GUI.color = HudSkin.Black; GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = ((Vector4)(Vector4)pc - (Vector4)c).magnitude < 0.02f ? HudSkin.Yellow : HudSkin.Orange; GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), Texture2D.whiteTexture);
                GUI.color = pc; GUI.DrawTexture(new Rect(r.x + 5, r.y + 5, r.width - 10, r.height - 10), Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition)) { c = pc; changed = true; Event.current.Use(); }
                GUILayout.Space(4);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.Label("Biały = oryginalne kolory części z paczki.", new GUIStyle(MenuSkin.Label) { fontSize = 15 });
            GUILayout.Space(8);

            GUILayout.Label("ODCIEŃ", new GUIStyle(MenuSkin.Label) { fontSize = 17 });
            float nh = GUILayout.HorizontalSlider(hh, 0f, 1f, MenuSkin.SliderTrack, MenuSkin.SliderThumb);
            GUILayout.Space(8);
            GUILayout.Label("NASYCENIE", new GUIStyle(MenuSkin.Label) { fontSize = 17 });
            float ns = GUILayout.HorizontalSlider(ss, 0f, 1f, MenuSkin.SliderTrack, MenuSkin.SliderThumb);
            GUILayout.Space(8);
            GUILayout.Label("JASNOŚĆ", new GUIStyle(MenuSkin.Label) { fontSize = 17 });
            float nv = GUILayout.HorizontalSlider(vv, 0f, 1f, MenuSkin.SliderTrack, MenuSkin.SliderThumb);
            if (!Mathf.Approximately(nh, hh) || !Mathf.Approximately(ns, ss) || !Mathf.Approximately(nv, vv))
            {
                c = Color.HSVToRGB(nh, ns, nv);
                changed = true;
            }
            if (changed) { Set(_slot, c); AppearanceSettings.Save(); }
        }

        // ================= START =================

        void StartGame(bool newGame)
        {
            LocalPlayerName = string.IsNullOrWhiteSpace(_name) ? "Gracz" : _name.Trim();
            PlayerPrefs.SetString("mp_name", LocalPlayerName);
            if (newGame) { SaveSystem.Delete(LocalPlayerName); Story.Reset(LocalPlayerName); }
            else Story.Load(LocalPlayerName);

            if (!_starting) StartCoroutine(StartHostRoutine(newGame));
        }

        bool _starting;

        /// <summary>
        /// Gra jednoosobowa to lokalny host. Gdy port jest zajęty (np. po poprzednim uruchomieniu w edytorze lub drugiej kopii gry),
        /// próbujemy kolejnych portów, zamiast pokazywać błąd.
        /// </summary>
        System.Collections.IEnumerator StartHostRoutine(bool newGame)
        {
            _starting = true;
            var nm = NetworkManager.Singleton;
            var transport = nm.GetComponent<UnityTransport>();
            ushort basePort = ushort.TryParse(_port, out var bp) ? bp : defaultPort;
            bool ok = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            // przeglądarka nie potrafi nasłuchiwać na porcie – gra jednoosobowa idzie przez transport bez sieci
            var sp = nm.GetComponent<Unity.Netcode.Transports.SinglePlayer.SinglePlayerTransport>();
            if (sp == null) sp = nm.gameObject.AddComponent<Unity.Netcode.Transports.SinglePlayer.SinglePlayerTransport>();
            nm.NetworkConfig.NetworkTransport = sp;
            ok = nm.StartHost();
            const int tries = 0;
#else
            const int tries = 40;
#endif
            for (int i = 0; i < tries && !ok; i++)
            {
                transport.SetConnectionData("127.0.0.1", (ushort)(basePort + i), "127.0.0.1");
                ok = nm.StartHost();
                if (ok) break;
                // nieudany start zamyka NetworkManager asynchronicznie – poczekaj, zanim spróbujesz na kolejnym porcie
                float t = 0f;
                while (nm.ShutdownInProgress && t < 3f) { t += Time.unscaledDeltaTime; yield return null; }
                yield return null;
            }
            _starting = false;
            _status = ok ? "" : "Nie udało się wystartować gry: wszystkie porty 7777–7816 są zajęte. Zamknij inne kopie gry albo zrestartuj Unity.";
            if (ok && newGame)
            {
                // katscena: dlaczego bohater jest w długu
                DialogueUI.Play(new[]
                {
                    new DialogueLine("TYTUŁ", "Trzy miesiące wcześniej"),
                    new DialogueLine("Narrator", "Twoja młodsza siostra, Ola, zasłabła w szkole. Diagnoza: poważna wada serca. Operacja za granicą, dwadzieścia tysięcy dolarów – i to od ręki."),
                    new DialogueLine("Narrator", "Banki odmówiły. Rodzina nie miała nic. Została jedna osoba, która nie zadawała pytań: Rico, szef gangu, który pożycza pieniądze tym, którym nie zostało już nikogo innego."),
                    new DialogueLine("Rico", "Dwadzieścia tysięcy, kolego. Dostaniesz je dziś. Oddasz co do centa – a jak nie pieniędzmi, to robotą."),
                    new DialogueLine("Narrator", "Operacja się udała. Ola żyje i wraca do zdrowia. Nie wie, za jaką cenę."),
                    new DialogueLine("Narrator", "Teraz Rico upomina się o swoje. Masz miasto, kryjówkę i dług do spłaty. Zarobione pieniądze przelejesz na dług w plecaku (I) – ale całość spłacisz dopiero, gdy wykonasz wszystkie zadania Rica."),
                    new DialogueLine("TYTUŁ", "OUT OF DEBT"),
                }, true);
                DialogueUI.Play(new[]
                {
                    new DialogueLine("Narrator", "Miasto nigdy nie śpi. Jesteś winien gangowi Rica 20 000 dolarów i nie masz jak ich oddać. Dług widzisz w prawym górnym rogu."),
                    new DialogueLine("Narrator", "Misje zaczynają się przed drzwiami budynków, w których czekają zleceniodawcy. Pierwszy to Rico w barze Pod Kogutem (mapa M, czerwony znacznik)."),
                });
            }
        }

        void StartHostLan()
        {
            LocalPlayerName = string.IsNullOrWhiteSpace(_name) ? "Gracz" : _name.Trim();
            Story.Load(LocalPlayerName);
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetConnectionData(_address, ushort.TryParse(_port, out var p) ? p : defaultPort, "0.0.0.0");
            NetworkManager.Singleton.StartHost();
        }

        void StartClient()
        {
            LocalPlayerName = string.IsNullOrWhiteSpace(_name) ? "Gracz" : _name.Trim();
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetConnectionData(_address, ushort.TryParse(_port, out var p) ? p : defaultPort);
            NetworkManager.Singleton.StartClient();
        }

        void StartServer()
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetConnectionData(_address, ushort.TryParse(_port, out var p) ? p : defaultPort, "0.0.0.0");
            NetworkManager.Singleton.StartServer();
        }
    }
}
