using System;
using System.IO;
using UnityEngine;

namespace MiniGTA
{
    /// <summary>
    /// Zapis postępu gracza po stronie SERWERA (hosta): plik JSON na nick.
    /// Lokalizacja: %USERPROFILE%\AppData\LocalLow\&lt;firma&gt;\&lt;gra&gt;\saves\
    /// Zapis: automatycznie co 30 s, przy wyjściu z gry i w kryjówce (E → Zapisz).
    /// Wczytanie: gdy gracz o tym nicku dołączy do serwera.
    /// </summary>
    public static class SaveSystem
    {
        [Serializable]
        public class PlayerSave
        {
            public string name;
            public int money;
            public int ownedWeapons;
            public float armor;
            public string pack;   // plecak: ulong jako tekst
            public string savedAt;
        }

        static string Dir => Path.Combine(Application.persistentDataPath, "saves");

        static string PathFor(string playerName)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) playerName = playerName.Replace(c, '_');
            return Path.Combine(Dir, playerName.ToLowerInvariant() + ".json");
        }

        public static void Delete(string playerName)
        {
            _cacheUntil = 0f;
            try { if (Exists(playerName)) File.Delete(PathFor(playerName)); } catch (Exception) { }
        }

        public static bool Exists(string playerName) => File.Exists(PathFor(playerName));

        /// <summary>Data ostatniego zapisu dla nicka albo null, gdy zapisu nie ma.</summary>
        static string _cacheName, _cacheValue;
        static float _cacheUntil;

        public static string SavedAt(string playerName)
        {
            // menu woła to kilka razy na klatkę – nie czytamy pliku częściej niż raz na sekundę
            if (_cacheName == playerName && Time.realtimeSinceStartup < _cacheUntil) return _cacheValue;
            string result = null;
            try
            {
                if (Exists(playerName))
                {
                    var data = JsonUtility.FromJson<PlayerSave>(File.ReadAllText(PathFor(playerName)));
                    result = data != null ? data.savedAt : null;
                }
            }
            catch (Exception) { result = null; }
            _cacheName = playerName; _cacheValue = result; _cacheUntil = Time.realtimeSinceStartup + 1f;
            return result;
        }

        public static void Save(PlayerController p)
        {
            if (p == null || !p.IsServer || p.Stats == null) return;
            _cacheUntil = 0f;
            try
            {
                Directory.CreateDirectory(Dir);
                var data = new PlayerSave
                {
                    name = p.Stats.PlayerName.Value.ToString(),
                    money = p.Stats.Money.Value,
                    ownedWeapons = p.Weapons != null ? p.Weapons.OwnedMask.Value : 0,
                    armor = p.Health.Armor.Value,
                    pack = p.Stats.Pack.Value.ToString(),
                    savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                };
                File.WriteAllText(PathFor(data.name), JsonUtility.ToJson(data, true));
            }
            catch (Exception e) { Debug.LogWarning("[MiniGTA] Zapis nie powiódł się: " + e.Message); }
        }

        /// <summary>Wczytuje zapis (jeśli istnieje) i nakłada go na gracza. Zwraca true, gdy wczytano.</summary>
        public static bool Load(PlayerController p, string playerName)
        {
            if (p == null || !p.IsServer) return false;
            try
            {
                string path = PathFor(playerName);
                if (!File.Exists(path)) return false;
                var data = JsonUtility.FromJson<PlayerSave>(File.ReadAllText(path));
                if (data == null) return false;
                p.Stats.Money.Value = Mathf.Max(0, data.money);
                if (p.Weapons != null) p.Weapons.OwnedMask.Value |= data.ownedWeapons;
                p.Health.Armor.Value = Mathf.Clamp(data.armor, 0f, Health.MaxArmor);
                if (!string.IsNullOrEmpty(data.pack) && ulong.TryParse(data.pack, out var packValue)) p.Stats.Pack.Value = packValue;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MiniGTA] Wczytanie zapisu nie powiodło się: " + e.Message);
                return false;
            }
        }
    }
}
