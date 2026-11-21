using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// Podłącza dźwięki z paczek „Weapons of Choice FREE” (strzały, przeładowanie) i „Car Engine Sound – i6 German Free” (silnik).
    /// Tworzy Assets/Resources/GameAudio.asset, który ładuje GameAudio.cs. Można uruchamiać wielokrotnie.
    /// </summary>
    public static class AudioIntegrator
    {
        const string AssetPath = "Assets/Resources/GameAudio.asset";

        [MenuItem("MiniGTA/Dźwięki: podłącz z paczek", priority = 48)]
        public static void RunMenu()
        {
            var sb = new StringBuilder();
            Run(sb);
            Debug.Log(sb.ToString());
            BuildDialog.Show("MiniGTA", sb.ToString(), "OK");
        }

        static AudioClip[] Find(string folderContains, string namePrefix)
        {
            return AssetDatabase.FindAssets("t:AudioClip")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.Replace('\\', '/').Contains(folderContains) && Path.GetFileNameWithoutExtension(p).StartsWith(namePrefix))
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
                .Where(c => c != null).ToArray();
        }

        static AudioClip One(string folderContains, string exactName)
        {
            return Find(folderContains, exactName).FirstOrDefault(c => c.name == exactName);
        }

        public static void Run(StringBuilder report)
        {
            const string weapons = "Weapons of Choice";
            const string engine = "i6_german_free";
            Directory.CreateDirectory("Assets/Resources");
            if (File.Exists(AssetPath)) AssetDatabase.DeleteAsset(AssetPath);

            var lib = ScriptableObject.CreateInstance<GameAudioLibrary>();
            lib.gunShots = Find(weapons, "Pistol_01_Fire_");
            lib.gunTails = new[] { One(weapons, "Pistol_01_Street_Tail_SFX") }.Where(c => c != null).ToArray();
            lib.ricochets = Find(weapons, "Ricochet_");
            lib.shells = Find(weapons, "Shell_");
            lib.reloadOut = One(weapons, "Handling_Gun_01_Clip_Out_SFX");
            lib.reloadIn = One(weapons, "Handling_Gun_01_Clip_In_SFX");
            lib.arming = One(weapons, "Handling_Gun_01_Arming_SFX");

            // zewnętrzne (bez katalogu Interior)
            AudioClip Ext(string n) => Find(engine, n).FirstOrDefault(c => AssetDatabase.GetAssetPath(c).Replace('\\', '/').EndsWith("/i6_german_free/" + n + ".wav"));
            lib.engineStart = Ext("startup");
            lib.engineIdle = Ext("idle");
            lib.engineLow = Ext("low_on");
            lib.engineMed = Ext("med_on");
            lib.engineHigh = Ext("high_on");
            lib.engineMax = Ext("maxRPM");
            lib.engineLowOff = Ext("low_off");
            lib.engineMedOff = Ext("med_off");
            lib.engineHighOff = Ext("high_off");

            AssetDatabase.CreateAsset(lib, AssetPath);
            AssetDatabase.SaveAssets();

            report.AppendLine("Dźwięki:");
            report.AppendLine($"  strzały: {lib.gunShots.Length}, echo: {lib.gunTails.Length}, rykoszety: {lib.ricochets.Length}, łuski: {lib.shells.Length}");
            report.AppendLine($"  przeładowanie: {(lib.reloadOut != null && lib.reloadIn != null ? "tak" : "brak")}");
            report.AppendLine($"  silnik: idle {(lib.engineIdle != null ? "tak" : "brak")}, low {(lib.engineLow != null ? "tak" : "brak")}, med {(lib.engineMed != null ? "tak" : "brak")}, high {(lib.engineHigh != null ? "tak" : "brak")}");
            if (lib.gunShots.Length == 0 && lib.engineIdle == null) report.AppendLine("  ⚠ nie znaleziono dźwięków – zaimportuj paczki Weapons of Choice FREE i i6 German Free.");
        }
    }
}
