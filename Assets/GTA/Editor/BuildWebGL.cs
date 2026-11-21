using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MiniGTA.EditorTools
{
    /// <summary>Menu: MiniGTA → Zbuduj WebGL (Unity Play). Tworzy Builds/WebGL oraz Builds/MiniGTA_WebGL.zip do wgrania na play.unity.com.</summary>
    public static class BuildWebGL
    {
        [MenuItem("MiniGTA/Zbuduj WebGL (Unity Play)")]
        public static void Build()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "WebGL"));
            string zip = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "MiniGTA_WebGL.zip"));
            if (Directory.Exists(dir)) Directory.Delete(dir, true);

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled) scenes.Add(s.path);

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.High);
            PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            PlayerSettings.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = dir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("[MiniGTA] Budowanie WebGL nie powiodło się: " + report.summary.result);
                return;
            }

            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(dir, zip, System.IO.Compression.CompressionLevel.Optimal, false);
            Debug.Log("[MiniGTA] WebGL gotowy: " + zip + " (" + (new FileInfo(zip).Length / 1048576) + " MB)");
            EditorUtility.RevealInFinder(zip);
        }
    }
}
