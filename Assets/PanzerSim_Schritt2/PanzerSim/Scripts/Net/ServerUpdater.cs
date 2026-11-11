using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace PanzerSim
{
    /// <summary>
    /// Dedizierter Server haelt sich selbst aktuell: alle 5 Minuten wird das neueste GitHub-Release abgefragt.
    /// Gibt es eine neuere Version, beendet sich der Server, sobald niemand mehr spielt (spaetestens nach
    /// 15 Minuten mit einer Minute Vorwarnung). systemd startet ihn sofort neu und holt dabei das Update
    /// (ExecStartPre=panzer-update.sh, siehe Tools/Server/install.sh). Abschalten: Startparameter -noupdate.
    /// Test: -updateinterval &lt;s&gt; (Abfrage-Abstand), -serverversion &lt;v&gt; (so tun, als laufe diese Version).
    /// </summary>
    public class ServerUpdater : MonoBehaviour
    {
        [Serializable] class Release { public string tag_name; }

        float interval = 300f;
        string current;
        const float MaxWaitWithPlayers = 15f * 60f, Warning = 60f;

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string n) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            if (Array.IndexOf(args, "-noupdate") >= 0) yield break;
            if (float.TryParse(Arg("-updateinterval"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float iv)) interval = Mathf.Max(5f, iv);
            current = Arg("-serverversion") ?? GameVersion.Current;
            if (current == "dev") yield break;   // Editor/Entwicklung: keine Updates

            yield return new WaitForSecondsRealtime(Mathf.Min(interval, 30f));
            while (true)
            {
                string latest = null;
                yield return Check(v => latest = v);
                if (latest != null && string.CompareOrdinal(latest, current) > 0)
                {
                    Debug.Log($"[Server] Update gefunden: {current} -> {latest}");
                    yield return RestartWhenEmpty(latest);
                    yield break;
                }
                yield return new WaitForSecondsRealtime(interval);
            }
        }

        IEnumerator Check(Action<string> done)
        {
            using (var req = UnityWebRequest.Get($"https://api.github.com/repos/{GameUpdater.Repo}/releases/latest"))
            {
                req.SetRequestHeader("User-Agent", "BallistikStudios-Server");
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) yield break;   // offline: spaeter nochmal
                try
                {
                    var rel = JsonUtility.FromJson<Release>(req.downloadHandler.text);
                    if (rel != null && !string.IsNullOrEmpty(rel.tag_name)) done(rel.tag_name.TrimStart('v', 'V'));
                }
                catch { }
            }
        }

        static int Players => NetSync.Active ? NetSync.NM.ConnectedClientsIds.Count : 0;

        IEnumerator RestartWhenEmpty(string latest)
        {
            float waited = 0f;
            bool told = false;
            while (Players > 0 && waited < MaxWaitWithPlayers - Warning)
            {
                if (!told) { NetSession.Instance?.Log($"Server-Update {latest} verfuegbar - Neustart, sobald alle Spieler raus sind (spaetestens in 15 Minuten)."); told = true; }
                yield return new WaitForSecondsRealtime(5f);
                waited += 5f;
            }
            if (Players > 0)
            {
                NetSession.Instance?.Log("Server startet in 1 Minute fuer das Update neu. Danach Spiel neu starten (aktualisiert sich selbst) und wieder beitreten.");
                float t = 0f;
                while (Players > 0 && t < Warning) { yield return new WaitForSecondsRealtime(1f); t += 1f; }
            }
            Debug.Log($"[Server] Neustart fuer Update {latest}");
            NetSync.Stop();
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);   // systemd (Restart=always) startet neu und laedt dabei das Update
        }
    }
}
