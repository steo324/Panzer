using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PanzerSim.EditorTools
{
    /// <summary>
    /// Menue "Panzer/Windows-Build erstellen (ZIP)": baut das Spiel nach Builds/BallistikStudios/ und packt es als
    /// Builds/BallistikStudios.zip - zum Weitergeben an Mitspieler (braucht kein Unity).
    /// </summary>
    public static class BuildGame
    {
        [MenuItem("Panzer/Windows-Build erstellen (ZIP)")]
        public static void BuildWindows()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outDir = Path.Combine(root, "Builds", "BallistikStudios");
            string zip = Path.Combine(root, "Builds", "BallistikStudios.zip");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            string scene = "Assets/PanzerSim/Spiel.unity";
            if (!File.Exists(Path.Combine(root, scene)))
            {
                var guids = AssetDatabase.FindAssets("Spiel t:Scene");
                if (guids.Length > 0) scene = AssetDatabase.GUIDToAssetPath(guids[0]);
            }
            PlayerSettings.companyName = "Ballistik Studios";
            PlayerSettings.productName = "Ballistik Studios - Panzer";

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = Path.Combine(outDir, "BallistikStudios.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"BUILD FEHLGESCHLAGEN: {report.summary.result}, {report.summary.totalErrors} Fehler");
                return;
            }
            // Debug-Symbole nicht mitschicken
            foreach (var d in Directory.GetDirectories(outDir, "*BurstDebugInformation_DoNotShip*")) Directory.Delete(d, true);
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(outDir, zip, System.IO.Compression.CompressionLevel.Optimal, false);
            Debug.Log($"BUILD FERTIG: {zip}  ({new FileInfo(zip).Length / 1048576f:0} MB)");
            EditorUtility.RevealInFinder(zip);
        }
    }
}
