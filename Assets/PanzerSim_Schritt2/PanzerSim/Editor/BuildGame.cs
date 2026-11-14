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
            BodenTexturImport.Ensure();   // Bodentexturen: Normal Maps/Masken richtig importiert
            ShaderIncludes.Ensure();   // Laufzeit-Shader (URP Lit, Terrain) muessen mit in den Build, sonst wird alles pink
            ModelPrefabs.RunAll();     // Modell-Prefabs (Sherman, Panzer IV) aus Geometrie + komprimierten Texturen
            NetPrefabs.Ensure();       // Netzwerk-Prefabs fuer den Mehrspieler
            GraphicsSetup.Ensure();    // Hoehennebel- und Decal-Feature im Renderer, Staub-Material
            StampVersion();            // Update-Nummer (1.0, 1.1 ...) fuer Auto-Update und Versionspruefung

            // Nur den Inhalt leeren: der Ordner selbst kann gesperrt sein (Explorer, Terminal ...)
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir)) File.Delete(f);
            foreach (var d in Directory.GetDirectories(outDir)) Directory.Delete(d, true);

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
                subtarget = (int)StandaloneBuildSubtarget.Player,   // ausdruecklich normales Spiel (nicht Server)
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
            BuildLinuxServer(root, scene);
            if (!Application.isBatchMode) EditorUtility.RevealInFinder(zip);
        }

        /// <summary>
        /// Schreibt Assets/Resources/version.txt: Update-Nummer "Haupt.Neben" (1.0, 1.1, 1.2 ...). Die Nummer ist immer
        /// "letztes veroeffentlichtes Update + 1" (GitHub-Release); Testbuilds dazwischen behalten dieselbe Nummer.
        /// Vor dem Wechsel trugen die Releases das Datum (2026.11.13.2025) - danach geht es mit 1.0 los.
        /// Die Updater vergleichen nur "gleich/ungleich", darum klappt der Wechsel ohne Zutun der Spieler.
        /// </summary>
        static void StampVersion()
        {
            string path = Path.Combine(Application.dataPath, "Resources", "version.txt");
            string v = NextUpdateNumber(File.Exists(path) ? File.ReadAllText(path).Trim() : "");
            File.WriteAllText(path, v);
            AssetDatabase.ImportAsset("Assets/Resources/version.txt", ImportAssetOptions.ForceUpdate);
            Debug.Log("VERSION: " + v);
        }

        const string Repo = "steo324/Panzer";

        /// <summary>Naechste Update-Nummer aus dem neuesten GitHub-Release; ohne Netz: bisherige Nummer behalten.</summary>
        static string NextUpdateNumber(string current)
        {
            string latest = null;
            try
            {
                using (var http = new System.Net.Http.HttpClient())
                {
                    http.Timeout = System.TimeSpan.FromSeconds(10);
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("BallistikBuild");
                    string json = http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest").Result;
                    var m = System.Text.RegularExpressions.Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([^\"]+)\"");
                    if (m.Success) latest = m.Groups[1].Value;
                }
            }
            catch (System.Exception e) { Debug.LogWarning("Update-Nummer: GitHub nicht erreichbar (" + e.Message + ")"); }

            if (latest == null) return IsUpdateNumber(current, out _, out _) ? current : "1.0";
            if (!IsUpdateNumber(latest, out int major, out int minor)) return "1.0";   // letztes Release noch mit Datum
            return $"{major}.{minor + 1}";
        }

        /// <summary>"1.4" ja, "2026.11.13.2025" (altes Datum) nein.</summary>
        static bool IsUpdateNumber(string s, out int major, out int minor)
        {
            major = minor = 0;
            var p = (s ?? "").Split('.');
            return p.Length == 2 && int.TryParse(p[0], out major) && int.TryParse(p[1], out minor) && major < 1000;
        }

        /// <summary>
        /// Dedizierter Linux-Server ohne Grafik nach Builds/BallistikServer/ (z. B. fuer CachyOS/Arch).
        /// Braucht im Unity Hub das Modul "Linux Dedicated Server Build Support"; ohne Modul wird er uebersprungen.
        /// Start: ./BallistikServer.x86_64 -batchmode -nographics -server -map 0 -port 7777
        /// </summary>
        [MenuItem("Panzer/Linux-Server bauen")]
        public static void BuildLinuxServerMenu() => BuildLinuxServer(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Assets/PanzerSim/Spiel.unity");

        static void BuildLinuxServer(string root, string scene)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64))
            {
                Debug.LogWarning("LINUX-SERVER UEBERSPRUNGEN: Modul 'Linux Dedicated Server Build Support' im Unity Hub installieren.");
                return;
            }
            string outDir = Path.Combine(root, "Builds", "BallistikServer");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir)) File.Delete(f);
            foreach (var d in Directory.GetDirectories(outDir)) Directory.Delete(d, true);
            NetPrefabs.Ensure();
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = Path.Combine(outDir, "BallistikServer.x86_64"),
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.None
            };
            // Shader NICHT wegoptimieren: die Karten erzeugen Materialien zur Laufzeit (new Material(shader)),
            // ohne Shader stuerzt der Server beim Kartenaufbau ab.
            bool oldOpt = PlayerSettings.dedicatedServerOptimizations;
            PlayerSettings.dedicatedServerOptimizations = false;
            var report = BuildPipeline.BuildPlayer(opts);
            PlayerSettings.dedicatedServerOptimizations = oldOpt;
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;   // Unity merkt sich sonst "Server"
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"LINUX-SERVER FEHLGESCHLAGEN: {report.summary.result} (Modul installiert?)");
                return;
            }
            foreach (var d in Directory.GetDirectories(outDir, "*BurstDebugInformation_DoNotShip*")) Directory.Delete(d, true);
            Debug.Log($"LINUX-SERVER FERTIG: {outDir}");
        }
    }
}
