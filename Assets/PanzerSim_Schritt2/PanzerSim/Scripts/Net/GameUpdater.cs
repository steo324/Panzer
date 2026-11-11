using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace PanzerSim
{
    /// <summary>
    /// Auto-Update beim Spielstart (nur Windows-Spiel, nicht im Editor/Server):
    /// fragt das neueste GitHub-Release ab; ist es neuer als diese Version, wird BallistikStudios.zip geladen,
    /// ein kleines PowerShell-Skript wartet bis das Spiel beendet ist, entpackt das Update ueber den Spielordner
    /// und startet das Spiel neu. Ohne Internet oder ohne Release startet das Spiel ganz normal.
    /// Abschalten: Startparameter -noupdate.
    /// </summary>
    public class GameUpdater : MonoBehaviour
    {
        public const string Repo = "steo324/Panzer";
        public const string ClientAsset = "BallistikStudios.zip";

        public static string Status = "";
        public static bool Busy;
        public static string Latest = "";

        [Serializable] class Asset { public string name; public string browser_download_url; }
        [Serializable] class Release { public string tag_name; public Asset[] assets; }

        IEnumerator Start()
        {
            if (GameVersion.IsDev || NetSync.Headless || Application.platform != RuntimePlatform.WindowsPlayer) yield break;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-noupdate") >= 0) yield break;

            Status = "Suche nach Updates ...";
            using (var req = UnityWebRequest.Get($"https://api.github.com/repos/{Repo}/releases/latest"))
            {
                req.SetRequestHeader("User-Agent", "BallistikStudios-Updater");
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Status = ""; yield break; }   // offline / noch kein Release
                Release rel;
                try { rel = JsonUtility.FromJson<Release>(req.downloadHandler.text); } catch { Status = ""; yield break; }
                if (rel == null || string.IsNullOrEmpty(rel.tag_name)) { Status = ""; yield break; }
                Latest = rel.tag_name.TrimStart('v', 'V');
                if (Latest == GameVersion.Current) { Status = ""; yield break; }
                string url = null;
                if (rel.assets != null) foreach (var a in rel.assets) if (a.name == ClientAsset) url = a.browser_download_url;
                if (url == null) { Status = ""; yield break; }
                yield return Download(url);
            }
        }

        IEnumerator Download(string url)
        {
            Busy = true;
            string zip = Path.Combine(Application.temporaryCachePath, "update.zip");
            using (var dl = new UnityWebRequest(url) { downloadHandler = new DownloadHandlerFile(zip) { removeFileOnAbort = true } })
            {
                dl.SetRequestHeader("User-Agent", "BallistikStudios-Updater");
                var op = dl.SendWebRequest();
                while (!op.isDone) { Status = $"Update {Latest} wird geladen ... {dl.downloadProgress * 100f:0}%"; yield return null; }
                if (dl.result != UnityWebRequest.Result.Success) { Status = "Update fehlgeschlagen: " + dl.error; Busy = false; yield break; }
            }
            Status = $"Update {Latest} wird installiert, Spiel startet neu ...";
            yield return null;
            try
            {
                string gameDir = Path.GetDirectoryName(Application.dataPath);   // ...\BallistikStudios_Data -> Spielordner
                string exe = Path.Combine(gameDir, "BallistikStudios.exe");
                string ps1 = Path.Combine(Application.temporaryCachePath, "update.ps1");
                File.WriteAllText(ps1, Script);
                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{ps1}\" -ProcId {pid} -Zip \"{zip}\" -Dir \"{gameDir}\" -Exe \"{exe}\"")
                { UseShellExecute = false, CreateNoWindow = true };
                System.Diagnostics.Process.Start(psi);
                Application.Quit();
            }
            catch (Exception e) { Status = "Update fehlgeschlagen: " + e.Message; Busy = false; }
        }

        const string Script = @"param([int]$ProcId, [string]$Zip, [string]$Dir, [string]$Exe)
try { Wait-Process -Id $ProcId -Timeout 60 -ErrorAction SilentlyContinue } catch {}
Start-Sleep -Seconds 1
$tmp = Join-Path $env:TEMP ('panzer_update_' + [guid]::NewGuid())
try {
  Expand-Archive -Path $Zip -DestinationPath $tmp -Force
  Copy-Item -Path (Join-Path $tmp '*') -Destination $Dir -Recurse -Force
} finally {
  Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
  Remove-Item $Zip -Force -ErrorAction SilentlyContinue
}
Start-Process -FilePath $Exe
";
    }
}
