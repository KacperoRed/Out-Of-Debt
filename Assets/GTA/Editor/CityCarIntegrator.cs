using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>
    /// MiniGTA → 7. Podłącz auta z Cartoon City i Military
    /// Zamienia auta ruchu ulicznego, auto misji i wyścigu na modele z paczki Cartoon City (Car_06/13/16/19, Futuristic_Car_1, Van),
    /// a wojskowy transporter na Hummera z paczki Military FREE. Radiowóz zostaje z paczki Super Retro (Cartoon City nie ma radiowozu).
    /// Tekstury i materiały zostają z paczek – po konwersji na Built-in (menu 0) nie ma różowych modeli.
    /// Uruchamiaj po menu 1 i 5, na otwartej scenie City.
    /// </summary>
    public static class CityCarIntegrator
    {
        static readonly string[] CityCars = { "Car_06", "Car_13", "Car_16", "Car_19", "Futuristic_Car_1", "Van" };

        [MenuItem("MiniGTA/7. Podłącz auta z Cartoon City i Military", priority = 46)]
        public static void Run()
        {
            var report = new StringBuilder("Auta z paczek:\n");
            // VehicleImporter przy aucie „służbowym” sam ustawia je jako radiowóz – zapamiętujemy prawdziwy radiowóz i przywracamy go
            var ps0 = Object.FindAnyObjectByType<PoliceSpawner>();
            var ts0 = Object.FindAnyObjectByType<TrafficSpawner>();
            var keepPolice = ps0 != null ? ps0.policeCarPrefab : null;
            var keepTrafficPolice = ts0 != null ? ts0.policeCarPrefab : null;
            var civilians = new List<GameObject>();
            GameObject fast = null;
            foreach (var name in CityCars)
            {
                var src = Find("Assets/ithappy/Cartoon_City_Free", name);
                if (src == null) { report.AppendLine($"  ✖ brak prefabu {name}"); continue; }
                var car = Convert(src, name, report);
                if (car == null) continue;
                civilians.Add(car);
                if (name.StartsWith("Futuristic")) fast = car;
            }

            GameObject army = null;
            var hummer = Find("Assets/ithappy/Military_Free", "Hummer_003");
            if (hummer != null) army = Convert(hummer, "Hummer_003", report, true);
            else report.AppendLine("  ✖ brak Hummer_003 (paczka Military FREE niezaimportowana?)");

            var ts = Object.FindAnyObjectByType<TrafficSpawner>();
            var ps = Object.FindAnyObjectByType<PoliceSpawner>();
            if (ps != null && keepPolice != null) { ps.policeCarPrefab = keepPolice; EditorUtility.SetDirty(ps); }
            if (ts != null && keepTrafficPolice != null) { ts.policeCarPrefab = keepTrafficPolice; EditorUtility.SetDirty(ts); }
            var mm = Object.FindAnyObjectByType<MissionManager>();
            if (civilians.Count > 0 && ts != null) { Undo.RecordObject(ts, "cars"); ts.carPrefabs = civilians.ToArray(); EditorUtility.SetDirty(ts); }
            if (civilians.Count > 0 && mm != null) { Undo.RecordObject(mm, "cars"); mm.vehiclePrefab = civilians[0]; EditorUtility.SetDirty(mm); }
            if (army != null && ps != null) { Undo.RecordObject(ps, "army car"); ps.armyCarPrefab = army; EditorUtility.SetDirty(ps); }
            if (fast != null)
                foreach (var m in Object.FindObjectsByType<Mission>(FindObjectsSortMode.None))
                {
                    bool ch = false;
                    foreach (var s in m.steps) if (s.vehicleOverride != null) { s.vehicleOverride = fast; ch = true; }
                    if (ch) EditorUtility.SetDirty(m);
                }

            PackIntegrator.EnsureSceneReferencesRegistered();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            report.AppendLine($"Gotowe: {civilians.Count} aut cywilnych{(army != null ? ", transporter wojskowy" : "")}. Zapisz scenę (Ctrl+S).");
            Debug.Log(report.ToString());
            BuildDialog.Show("MiniGTA", report.ToString(), "OK");
        }

        static GameObject Find(string folder, string name)
        {
            foreach (var guid in AssetDatabase.FindAssets(name + " t:Prefab", new[] { folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            return null;
        }

        static GameObject Convert(GameObject src, string name, StringBuilder report, bool service = false)
        {
            var sb = new StringBuilder();
            var car = VehicleImporter.Convert(src, service, sb); // służby (radiowóz/wojsko) potrzebują PoliceCarAI
            report.AppendLine(car != null ? $"  ✔ {name}" : $"  ✖ {name}: {sb}");
            if (car != null && sb.ToString().Contains("⚠")) report.AppendLine("    " + sb.ToString().Replace("\n", "\n    "));
            return car;
        }
    }
}
