using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Paczki ithappy są przygotowane pod URP, a ten projekt używa Built-in – bez konwersji materiały byłyby różowe.
    /// Każda paczka ma w Render_Pipeline_Convert gotowy pakiet „…Built-In…”, który podmienia tylko materiały i tekstury.
    /// Ten skrypt importuje je automatycznie raz (znacznik: EditorPrefs). Ręcznie: menu MiniGTA → 0.
    /// </summary>
    public static class PackMaterialConverter
    {
        const string Key = "MiniGTA_ithappy_builtin_v3";

        [InitializeOnLoadMethod]
        static void AutoRun()
        {
            if (!Directory.Exists("Assets/ithappy")) return;
            // migracja: wcześniej jeden znacznik dla wszystkich paczek – te już przekonwertowane oznaczamy, nowe (np. meble) zostają do zrobienia
            if (EditorPrefs.GetBool(Key, false) && !EditorPrefs.GetBool(Key + "_migrated", false))
            {
                foreach (var pkg in Directory.GetFiles("Assets/ithappy", "*.unitypackage", SearchOption.AllDirectories))
                    if (IsBuiltIn(pkg) && !pkg.Contains("Cute_Furniture")) EditorPrefs.SetBool(PkgKey(pkg), true);
                EditorPrefs.SetBool(Key + "_migrated", true);
            }
            EditorApplication.delayCall += () => { if (Pending()) ConvertInner(); };
        }

        static string PkgKey(string pkg) => Key + "_" + pkg.Replace("\\", "/");

        static bool IsBuiltIn(string pkg) { string f = Path.GetFileName(pkg); return f.Contains("Built-In") || f.Contains("BuiltIn"); }

        /// <summary>Czy jest paczka (np. dopiero co zaimportowane meble), której konwersji jeszcze nie robiono.</summary>
        static bool Pending()
        {
            foreach (var pkg in Directory.GetFiles("Assets/ithappy", "*.unitypackage", SearchOption.AllDirectories))
                if (IsBuiltIn(pkg) && !EditorPrefs.GetBool(PkgKey(pkg), false)) return true;
            return false;
        }

        [MenuItem("MiniGTA/0. Napraw materiały paczek ithappy (Built-in)", priority = 5)]
        public static void Convert()
        {
            Force = true; // z menu: zawsze importuj wszystko ponownie
            try { ConvertInner(); } finally { Force = false; }
        }

        static bool Force;

        static void ConvertInner()
        {
            int n = 0;
            foreach (var pkg in Directory.GetFiles("Assets/ithappy", "*.unitypackage", SearchOption.AllDirectories))
            {
                if (!IsBuiltIn(pkg)) continue;
                if (EditorPrefs.GetBool(PkgKey(pkg), false) && !Force) continue; // ta paczka jest już przekonwertowana
                AssetDatabase.ImportPackage(pkg.Replace("\\", "/"), false); // false = bez okna dialogowego
                EditorPrefs.SetBool(PkgKey(pkg), true);
                n++;
            }
            Debug.Log($"[MiniGTA] Zaimportowano {n} pakietów konwersji materiałów ithappy → Built-in.");
        }
    }
}
