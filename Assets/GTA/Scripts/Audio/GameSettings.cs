using UnityEngine;

namespace MiniGTA
{
    /// <summary>Ustawienia gracza (menu Esc → INTERFEJS / DŹWIĘK), zapisywane w PlayerPrefs.</summary>
    public static class GameSettings
    {
        static float _master = -1f, _sfx = -1f, _engine = -1f;
        static int _minimap = -1, _waypoint = -1, _crosshair = -1, _mapIcons = -1;

        static float GetF(ref float cache, string key, float def)
        {
            if (cache < 0f) cache = PlayerPrefs.GetFloat(key, def);
            return cache;
        }

        static void SetF(ref float cache, string key, float v)
        {
            cache = Mathf.Clamp01(v);
            PlayerPrefs.SetFloat(key, cache);
        }

        static bool GetB(ref int cache, string key, bool def)
        {
            if (cache < 0) cache = PlayerPrefs.GetInt(key, def ? 1 : 0);
            return cache == 1;
        }

        static void SetB(ref int cache, string key, bool v)
        {
            cache = v ? 1 : 0;
            PlayerPrefs.SetInt(key, cache);
        }

        public static float MasterVolume
        {
            get => GetF(ref _master, "set_master", 1f);
            set { SetF(ref _master, "set_master", value); AudioListener.volume = _master; }
        }
        public static float SfxVolume { get => GetF(ref _sfx, "set_sfx", 0.8f); set => SetF(ref _sfx, "set_sfx", value); }
        public static float EngineVolume { get => GetF(ref _engine, "set_engine", 0.6f); set => SetF(ref _engine, "set_engine", value); }

        public static bool ShowMinimap { get => GetB(ref _minimap, "set_minimap", true); set => SetB(ref _minimap, "set_minimap", value); }
        public static bool ShowWaypoint { get => GetB(ref _waypoint, "set_waypoint", true); set => SetB(ref _waypoint, "set_waypoint", value); }
        public static bool ShowCrosshair { get => GetB(ref _crosshair, "set_crosshair", true); set => SetB(ref _crosshair, "set_crosshair", value); }
        public static bool ShowMapIcons { get => GetB(ref _mapIcons, "set_mapicons", true); set => SetB(ref _mapIcons, "set_mapicons", value); }

        public static void Save() => PlayerPrefs.Save();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Apply() => AudioListener.volume = MasterVolume;
    }
}
