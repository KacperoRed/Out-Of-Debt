using System;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Menu pauzy (Esc) w stylu wzoru: zakładki STEROWANIE / INTERFEJS / DŹWIĘK, przyciski WZNÓW, PRZEBIERALNIA, ZAPISZ I WYJDŹ.
    /// Rysuje NetworkUI, gdy kursor jest odblokowany w grze.
    /// </summary>
    public static class PauseMenuUI
    {
        static readonly string[] Tabs = { "STEROWANIE", "INTERFEJS", "DŹWIĘK" };

        static readonly string[,] Controls =
        {
            { "WASD", "RUCH / JAZDA" },
            { "SHIFT", "SPRINT" },
            { "SPACJA", "SKOK / HAMULEC RĘCZNY" },
            { "MYSZ", "KAMERA" },
            { "LPM", "STRZAŁ / CIOS" },
            { "PPM", "CELOWANIE" },
            { "1 - 9", "WYBÓR BRONI (8-GRANAT, 9-RPG)" },
            { "0", "SCHOWAJ / WYJMIJ BROŃ" },
            { "R", "PRZEŁADUJ / POSTAW OBRÓĆ AUTO" },
            { "F", "WSIĄDŹ / WYSIĄDŹ" },
            { "G", "SYRENA (RADIOWÓZ)" },
            { "H", "KLAKSON" },
            { "E", "SKLEP / BAR / KRYJÓWKA" },
            { "PPM", "NA KASJERA (NAPAD OD AKTU 2)" },
            { "M", "DUŻA MAPA" },
            { "F2", "PRZEBIERALNIA" },
            { "I", "PLECAK" },
            { "TAB", "KOŁO BRONI (PRZYTRZYMAJ)" },
            { "Z / X", "LECZENIE / KAMIZELKA" },
            { "`", "KONSOLA" },
            { "ESC", "MENU" },
        };

        static int _tab;
        static Vector2 _scroll;

        public static void Draw(Action resume, Action wardrobe, Action saveExit)
        {
            MenuSkin.Ensure();
            float w = Mathf.Min(900f, Screen.width - 60f);
            float h = Mathf.Min(720f, Screen.height - 40f);
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);

            // pasek zakładek
            float tabH = 44f;
            var tabsBar = new Rect(area.x, area.y, area.width, tabH + 6f);
            MenuSkin.DrawPanel(tabsBar);
            float tw = 190f;
            for (int i = 0; i < Tabs.Length; i++)
            {
                var r = new Rect(tabsBar.x + 10 + i * (tw + 6), tabsBar.y + 5, tw, tabH - 4);
                if (GUI.Button(r, Tabs[i], i == _tab ? MenuSkin.TabActive : MenuSkin.Tab)) _tab = i;
            }

            // zawartość
            var body = new Rect(area.x, tabsBar.yMax + 8, area.width, area.height - tabsBar.height - 8 - 66f);
            MenuSkin.DrawPanel(body);
            var inner = new Rect(body.x + 18, body.y + 14, body.width - 36, body.height - 28);
            if (_tab == 0) DrawControls(inner);
            else if (_tab == 1) DrawInterface(inner);
            else DrawSound(inner);

            // przyciski na dole
            float by = body.yMax + 10, bh = 48f, gap = 10f;
            float bw = (area.width - gap * 2f) / 3f;
            if (GUI.Button(new Rect(area.x, by, bw, bh), "WZNÓW", MenuSkin.Button)) resume();
            if (GUI.Button(new Rect(area.x + bw + gap, by, bw, bh), "PRZEBIERALNIA", MenuSkin.Button)) wardrobe();
            if (GUI.Button(new Rect(area.x + (bw + gap) * 2f, by, bw, bh), "ZAPISZ I WYJDŹ", MenuSkin.Button)) saveExit();
        }

        static int _settingsTab;

        /// <summary>Ustawienia z menu głównego: tylko INTERFEJS i DŹWIĘK + przycisk WSTECZ.</summary>
        public static void DrawSettings(Action back, Action<Rect> heroTab = null)
        {
            MenuSkin.Ensure();
            float w = Mathf.Min(900f, Screen.width - 60f);
            float h = Mathf.Min(520f, Screen.height - 40f);
            var area = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            string[] names = heroTab != null ? new[] { "BOHATER", "INTERFEJS", "DŹWIĘK" } : new[] { "INTERFEJS", "DŹWIĘK" };
            if (_settingsTab >= names.Length) _settingsTab = 0;

            var tabsBar = new Rect(area.x, area.y, area.width, 50f);
            MenuSkin.DrawPanel(tabsBar);
            for (int i = 0; i < names.Length; i++)
                if (GUI.Button(new Rect(tabsBar.x + 10 + i * 196, tabsBar.y + 5, 190, 40), names[i], i == _settingsTab ? MenuSkin.TabActive : MenuSkin.Tab)) _settingsTab = i;

            var body = new Rect(area.x, tabsBar.yMax + 8, area.width, area.height - tabsBar.height - 8 - 66f);
            MenuSkin.DrawPanel(body);
            var inner = new Rect(body.x + 18, body.y + 14, body.width - 36, body.height - 28);
            int t = heroTab != null ? _settingsTab : _settingsTab + 1;
            if (t == 0) heroTab(inner); else if (t == 1) DrawInterface(inner); else DrawSound(inner);

            if (GUI.Button(new Rect(area.x, body.yMax + 10, area.width, 48), "WSTECZ", MenuSkin.Button)) back();
        }

        static void DrawControls(Rect r)
        {
            const float rowH = 46f;
            int rows = Controls.GetLength(0);
            var view = new Rect(0, 0, r.width - 22, rows * rowH);
            _scroll = GUI.BeginScrollView(r, _scroll, view);
            float keyW = view.width * 0.28f;
            for (int i = 0; i < rows; i++)
            {
                float y = i * rowH;
                GUI.Box(new Rect(0, y, keyW, rowH - 8), Controls[i, 0], MenuSkin.Pill);
                GUI.Label(new Rect(keyW, y, 40, rowH - 8), "–", new GUIStyle(MenuSkin.Title) { fontSize = 26 });
                GUI.Box(new Rect(keyW + 46, y, view.width - keyW - 46, rowH - 8), Controls[i, 1], MenuSkin.Pill);
            }
            GUI.EndScrollView();
        }

        static void DrawToggle(Rect r, string label, bool value, Action<bool> set)
        {
            float lw = r.width * 0.62f;
            GUI.Box(new Rect(r.x, r.y, lw, r.height), label, MenuSkin.PillLeft);
            if (GUI.Button(new Rect(r.x + lw + 10, r.y, r.width - lw - 10, r.height), value ? "WŁĄCZONE" : "WYŁĄCZONE", MenuSkin.Button))
            {
                set(!value);
                GameSettings.Save();
            }
        }

        static void DrawInterface(Rect r)
        {
            float y = r.y, rowH = 52f;
            DrawToggle(new Rect(r.x, y, r.width, rowH - 8), "MINIMAPA", GameSettings.ShowMinimap, v => GameSettings.ShowMinimap = v); y += rowH;
            DrawToggle(new Rect(r.x, y, r.width, rowH - 8), "ZNACZNIK NAJBLIŻSZEJ MISJI", GameSettings.ShowWaypoint, v => GameSettings.ShowWaypoint = v); y += rowH;
            DrawToggle(new Rect(r.x, y, r.width, rowH - 8), "IKONY NA MAPIE", GameSettings.ShowMapIcons, v => GameSettings.ShowMapIcons = v); y += rowH;
            DrawToggle(new Rect(r.x, y, r.width, rowH - 8), "CELOWNIK", GameSettings.ShowCrosshair, v => GameSettings.ShowCrosshair = v); y += rowH + 8;
            GUI.Label(new Rect(r.x, y, r.width, 60), "Lista klawiszy jest w zakładce STEROWANIE. Przebieralnia: F2.", MenuSkin.Label);
        }

        static void DrawSlider(Rect r, string label, float value, Action<float> set)
        {
            float lw = r.width * 0.42f;
            GUI.Box(new Rect(r.x, r.y, lw, r.height), label, MenuSkin.PillLeft);
            var sr = new Rect(r.x + lw + 16, r.y + (r.height - 22) / 2f, r.width - lw - 100, 22);
            float nv = MenuSkin.Slider(sr, value);
            GUI.Label(new Rect(sr.xMax + 10, r.y, 70, r.height), Mathf.RoundToInt(nv * 100f) + "%", MenuSkin.Label);
            if (!Mathf.Approximately(nv, value)) { set(nv); }
            if (Event.current.type == EventType.MouseUp) GameSettings.Save();
        }

        static void DrawSound(Rect r)
        {
            float y = r.y, rowH = 60f;
            DrawSlider(new Rect(r.x, y, r.width, rowH - 10), "GŁOŚNOŚĆ GŁÓWNA", GameSettings.MasterVolume, v => GameSettings.MasterVolume = v); y += rowH;
            DrawSlider(new Rect(r.x, y, r.width, rowH - 10), "EFEKTY (BROŃ)", GameSettings.SfxVolume, v => GameSettings.SfxVolume = v); y += rowH;
            DrawSlider(new Rect(r.x, y, r.width, rowH - 10), "SILNIKI AUT", GameSettings.EngineVolume, v => GameSettings.EngineVolume = v);
        }
    }
}
