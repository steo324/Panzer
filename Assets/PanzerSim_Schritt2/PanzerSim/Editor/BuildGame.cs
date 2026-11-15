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
            BuildLinuxClient(root, scene);
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
        /// Spiel fuer Linux (Steam Deck, CachyOS, Ubuntu ...) nach Builds/BallistikLinux/: BallistikStudios.x86_64, Vulkan.
        /// Braucht im Unity Hub das Modul "Linux Build Support (Mono)"; ohne Modul wird es uebersprungen.
        /// publish.ps1 packt den Ordner als BallistikStudios-Linux.tar.gz ins Release (Auto-Update wie unter Windows).
        /// </summary>
        [MenuItem("Panzer/Linux-Spiel bauen")]
        public static void BuildLinuxClientMenu() => BuildLinuxClient(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Assets/PanzerSim/Spiel.unity");

        static void BuildLinuxClient(string root, string scene)
        {
            string engines = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "LinuxStandaloneSupport", "Variations");
            // Test (-linuxdev): Entwickler-Build, falls nur diese Variante installiert ist (nicht zum Veroeffentlichen)
            bool devTest = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-linuxdev") >= 0 && Directory.Exists(Path.Combine(engines, "linux64_player_development_mono"));
            if (!devTest && !Directory.Exists(Path.Combine(engines, "linux64_player_nondevelopment_mono")))
            {
                Debug.LogWarning("LINUX-SPIEL UEBERSPRUNGEN: Modul 'Linux Build Support (Mono)' im Unity Hub installieren.");
                return;
            }
            string outDir = Path.Combine(root, "Builds", "BallistikLinux");
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir)) File.Delete(f);
            foreach (var d in Directory.GetDirectories(outDir)) Directory.Delete(d, true);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = Path.Combine(outDir, "BallistikStudios.x86_64"),
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Player,
                options = devTest ? BuildOptions.Development : BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"LINUX-SPIEL FEHLGESCHLAGEN: {report.summary.result}, {report.summary.totalErrors} Fehler");
                return;
            }
            foreach (var d in Directory.GetDirectories(outDir, "*DoNotShip*")) Directory.Delete(d, true);
            // Startskript (setzt das Ausfuehrbar-Recht, das beim Packen unter Windows verloren geht); Zeilenenden nur LF
            File.WriteAllText(Path.Combine(outDir, "start.sh"),
                "#!/bin/sh\n# Ballistik Studios - Panzer (Linux)\ncd \"$(dirname \"$0\")\"\nchmod +x BallistikStudios.x86_64 2>/dev/null\nexec ./BallistikStudios.x86_64 \"$@\"\n");
            string tgz = Path.Combine(root, "Builds", "BallistikStudios-Linux.tar.gz");
            WriteTarGz(outDir, "BallistikLinux", tgz, new[] { "BallistikStudios.x86_64", "start.sh" });
            Debug.Log($"LINUX-SPIEL FERTIG: {tgz}  ({new FileInfo(tgz).Length / 1048576f:0} MB)");
        }

        /// <summary>
        /// tar.gz (ustar) mit Unix-Rechten: Windows-tar setzt kein Ausfuehrbar-Recht, ohne das startet das Spiel unter Linux nicht per Doppelklick.
        /// Eintraege liegen unter prefix/ ; die Dateien in exec bekommen 0755, alles andere 0644.
        /// </summary>
        /// <summary>Test: Linux-Server-Ordner mit dem eigenen tar-Packer packen (Builds/TarTest.tar.gz).</summary>
        public static void TestTar() { string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")); WriteTarGz(Path.Combine(root, "Builds", "BallistikServer"), "BallistikServer", Path.Combine(root, "Builds", "TarTest.tar.gz"), new[] { "BallistikServer.x86_64" }); Debug.Log("TARTEST FERTIG"); }

        static void WriteTarGz(string dir, string prefix, string outFile, string[] exec)
        {
            if (File.Exists(outFile)) File.Delete(outFile);
            using (var fs = File.Create(outFile))
            using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal))
            {
                void Header(string name, long size, bool isDir, int mode)
                {
                    var h = new byte[512];
                    var nb = System.Text.Encoding.UTF8.GetBytes(name);
                    if (nb.Length > 100)
                    {
                        // ustar: langer Pfad -> Praefix (bis 155) + Name (bis 100), geteilt an einem /
                        int cut = name.LastIndexOf('/', Mathf.Min(name.Length - 1, 155));
                        var pre = System.Text.Encoding.UTF8.GetBytes(name.Substring(0, cut));
                        nb = System.Text.Encoding.UTF8.GetBytes(name.Substring(cut + 1));
                        System.Array.Copy(pre, 0, h, 345, Mathf.Min(pre.Length, 155));
                    }
                    System.Array.Copy(nb, 0, h, 0, Mathf.Min(nb.Length, 100));
                    void Oct(long v, int off, int len) { var s = System.Convert.ToString(v, 8).PadLeft(len - 1, '0'); var b = System.Text.Encoding.ASCII.GetBytes(s); System.Array.Copy(b, 0, h, off, len - 1); }
                    Oct(mode, 100, 8); Oct(0, 108, 8); Oct(0, 116, 8); Oct(size, 124, 12);
                    Oct(System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 136, 12);
                    h[156] = (byte)(isDir ? '5' : '0');
                    System.Text.Encoding.ASCII.GetBytes("ustar").CopyTo(h, 257); h[262] = 0; h[263] = (byte)'0'; h[264] = (byte)'0';
                    for (int i = 148; i < 156; i++) h[i] = (byte)' ';
                    long sum = 0; foreach (var b in h) sum += b;
                    var cs = System.Text.Encoding.ASCII.GetBytes(System.Convert.ToString(sum, 8).PadLeft(6, '0'));
                    System.Array.Copy(cs, 0, h, 148, 6); h[154] = 0; h[155] = (byte)' ';
                    gz.Write(h, 0, 512);
                }
                Header(prefix + "/", 0, true, 493);
                foreach (var d in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories))
                    Header(prefix + "/" + d.Substring(dir.Length + 1).Replace('\\', '/') + "/", 0, true, 493);
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(dir.Length + 1).Replace('\\', '/');
                    var data = File.ReadAllBytes(f);
                    Header(prefix + "/" + rel, data.Length, false, System.Array.IndexOf(exec, rel) >= 0 ? 493 : 420);
                    gz.Write(data, 0, data.Length);
                    int pad = (512 - data.Length % 512) % 512;
                    if (pad > 0) gz.Write(new byte[pad], 0, pad);
                }
                gz.Write(new byte[1024], 0, 1024);   // Ende: zwei leere Bloecke
            }
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
