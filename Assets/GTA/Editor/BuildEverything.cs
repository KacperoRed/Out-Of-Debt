using System;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>Okno dialogowe, które milknie podczas „BUDUJ WSZYSTKO” (jedno podsumowanie na końcu zamiast wielu okien).</summary>
    public static class BuildDialog
    {
        public static bool Silent;

        public static bool Show(string title, string message, string ok)
        {
            if (Silent || Application.isBatchMode) { Debug.Log("[MiniGTA] " + title + ": " + message); return true; }
            return EditorUtility.DisplayDialog(title, message, ok);
        }
    }

    /// <summary>
    /// JEDEN PRZYCISK: MiniGTA → ★ BUDUJ WSZYSTKO. Wykonuje po kolei:
    ///   0. konwersję materiałów paczek ithappy na Built-in,
    ///   1. generator miasta + prefabów (budynki z Cartoon City, kasyno, misje fabularne, nocne okna),
    ///   2. modele broni i auta z paczek, sklepy,
    ///   3. postacie modułowe z paczki Creative Characters (przebieralnia, kolory, losowi NPC),
    ///   4. auta z Cartoon City i transporter z Military,
    ///   5. czołg i helikopter z Military,
    ///   6. dźwięki (strzały, silnik),
    ///   7. zapis sceny i raport.
    /// Kroki, których brakuje paczki, są pomijane z wpisem w raporcie. Menu 1, 5–8 nadal działają osobno.
    /// </summary>
    public static class BuildEverything
    {
        [MenuItem("MiniGTA/★ BUDUJ WSZYSTKO (1 przycisk)", priority = -100)]
        public static void Run()
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("MiniGTA", "Zatrzymaj Play i uruchom ponownie.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("MiniGTA – BUDUJ WSZYSTKO",
                "Przebuduje scenę Assets/GTA/Scenes/City.unity oraz prefaby w Assets/GTA/Generated (nadpisze je) i podłączy wszystkie paczki.\n\nTo może potrwać kilka minut. Kontynuować?",
                "Buduj", "Anuluj")) return;

            var report = new StringBuilder("BUDUJ WSZYSTKO – raport:\n\n");
            var steps = new (string name, Action action)[]
            {
                ("Materiały paczek (Built-in)", () => { if (!EditorPrefs.GetBool("MiniGTA_ithappy_builtin_v3", false)) PackMaterialConverter.Convert(); else report.AppendLine("  już przekonwertowane"); }),
                ("Miasto, prefaby, misje, budynki", () => { EditorSceneManager.SaveOpenScenes(); GTASceneBuilder.BuildAll(); }),
                ("Modele broni, auta, sklepy", () => PackIntegrator.IntegrateAll()),
                ("Postacie (Creative Characters)", () => CharacterPackIntegrator.Run()),
                ("Auta z Cartoon City i Military", () => CityCarIntegrator.Run()),
                ("Czołg i helikopter (Military)", () => MilitaryIntegrator.Run()),
                ("Dźwięki (broń, silnik)", () => AudioIntegrator.Run(report)),
            };

            BuildDialog.Silent = true;
            int failed = 0;
            try
            {
                for (int i = 0; i < steps.Length; i++)
                {
                    EditorUtility.DisplayProgressBar("MiniGTA – BUDUJ WSZYSTKO", $"{i + 1}/{steps.Length}: {steps[i].name}", i / (float)steps.Length);
                    report.AppendLine($"[{i + 1}/{steps.Length}] {steps[i].name}");
                    try { steps[i].action(); report.AppendLine("  ✔ gotowe"); }
                    catch (Exception e)
                    {
                        failed++;
                        report.AppendLine("  ✖ błąd: " + e.Message);
                        Debug.LogError("[MiniGTA] Krok '" + steps[i].name + "' nie powiódł się:\n" + e);
                    }
                }

                EditorUtility.DisplayProgressBar("MiniGTA – BUDUJ WSZYSTKO", "Zapis sceny", 0.98f);
                EditorSceneManager.MarkAllScenesDirty();
                EditorSceneManager.SaveOpenScenes();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                BuildDialog.Silent = false;
                EditorUtility.ClearProgressBar();
            }

            string extra = "";
            try { extra = System.IO.File.ReadAllText("Assets/GTA/Generated/BuildReport.txt"); } catch (Exception) { }
            if (!string.IsNullOrEmpty(extra)) report.AppendLine("\nWalidacja miasta:\n" + extra);
            report.AppendLine(failed == 0 ? "\nGOTOWE. Wciśnij Play i wybierz NOWA GRA." : $"\nZakończono z błędami w {failed} krokach – patrz Console.");
            Debug.Log("[MiniGTA] " + report);
            EditorUtility.DisplayDialog("MiniGTA – BUDUJ WSZYSTKO", report.ToString().Length > 1500 ? report.ToString().Substring(0, 1500) + "\n…(pełny raport w Console)" : report.ToString(), "OK");
        }
    }
}
