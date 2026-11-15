using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using Unity.Services.Core;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Spieler-Konten (ohne E-Mail): Benutzername + Passwort ueber Unity Authentication.
    /// Ohne Anmeldung kein Spiel. Unity speichert nur eine Pruefsumme des Passworts, niemand (auch der Entwickler) sieht es.
    /// Kontoliste fuer den Entwickler: jedes Konto legt seinen Namen als oeffentliche Cloud-Save-Daten ab ("name", "seit", "zuletzt"),
    /// das Entwickler-Konto fragt sie ab (Unity-Dashboard: Cloud Save > Queries, Index auf "name", Zugriff Public).
    /// Freischalten im Unity-Dashboard: Player Authentication > Identity Providers > Username &amp; Password.
    /// </summary>
    public static class Accounts
    {
        public const string DevName = "steo";
        // Der Name "steo" laesst sich nur mit dem richtigen Passwort neu anlegen. Gespeichert ist nur eine PBKDF2-Pruefsumme (Repo ist oeffentlich).
        const string DevHash = "5b6a79ba3a62ba0d391d15ba88b4f4c2f99a8709af36b5d1637cc88b535a4278";
        // Unity verlangt Gross-/Kleinbuchstaben, Ziffer und Sonderzeichen; dieser feste Anhang erfuellt das, damit Spieler freie Passwoerter waehlen koennen.
        const string PwSuffix = "#Bs1";
        public const int MinPw = 6, MaxPw = 26;

        public static bool Busy;
        public static string Message = "";
        public static string Username = "";
        public static bool LoggedIn => !string.IsNullOrEmpty(Username) && UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;
        public static bool IsDevAccount => LoggedIn && string.Equals(Username, DevName, StringComparison.OrdinalIgnoreCase);
        public static bool RestoreTried;

        /// <summary>Tests/Benchmarks und der Server laufen ohne Konto.</summary>
        public static bool Required
        {
            get
            {
                if (NetSync.Headless) return false;
                foreach (var a in Environment.GetCommandLineArgs())
                    if (a == "-nologin" || a.StartsWith("-bench") || a == "-jet") return false;
                return true;
            }
        }

        static async Task InitServices()
        {
            if (UnityServices.State == ServicesInitializationState.Initialized) return;
            var opt = new InitializationOptions();
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-profile");
            if (i >= 0 && i + 1 < args.Length) opt.SetProfile(args[i + 1]);
            await UnityServices.InitializeAsync(opt);
        }

        /// <summary>Beim Spielstart: gespeicherte Sitzung des letzten Kontos wiederherstellen (kein neues anonymes Konto anlegen).</summary>
        public static async void TryRestore()
        {
            if (RestoreTried) return;
            RestoreTried = true;
            Busy = true;
            try
            {
                await InitServices();
                var auth = AuthenticationService.Instance;
                if (!auth.IsSignedIn && auth.SessionTokenExists) await auth.SignInAnonymouslyAsync();   // meldet das gespeicherte Konto wieder an
                if (auth.IsSignedIn) await AfterSignIn(false);
            }
            catch (Exception e) { Debug.LogWarning("[Konto] Sitzung: " + e.Message); SafeSignOut(); }
            Busy = false;
        }

        public static async void Login(string user, string pw) => await Run(user, pw, false);
        public static async void Register(string user, string pw) => await Run(user, pw, true);

        static async Task Run(string user, string pw, bool create)
        {
            if (Busy) return;
            user = (user ?? "").Trim();
            pw = pw ?? "";
            string err = Check(user, pw);
            if (err != null) { Message = err; return; }
            if (create && string.Equals(user, DevName, StringComparison.OrdinalIgnoreCase) && Hash(pw) != DevHash)
            { Message = "Dieser Name ist vergeben."; return; }
            Busy = true; Message = "";
            try
            {
                await InitServices();
                var auth = AuthenticationService.Instance;
                if (auth.IsSignedIn) auth.SignOut(true);
                if (create) await auth.SignUpWithUsernamePasswordAsync(user, pw + PwSuffix);
                else await auth.SignInWithUsernamePasswordAsync(user, pw + PwSuffix);
                await AfterSignIn(create);
                PlayerPrefs.SetString("konto.name", Username); PlayerPrefs.Save();
            }
            catch (Exception e) { Message = Explain(e, create); SafeSignOut(); }
            Busy = false;
        }

        static async Task AfterSignIn(bool created)
        {
            var auth = AuthenticationService.Instance;
            string name = auth.PlayerInfo?.Username;
            if (string.IsNullOrEmpty(name)) { var info = await auth.GetPlayerInfoAsync(); name = info?.Username; }
            if (string.IsNullOrEmpty(name)) { SafeSignOut(); return; }   // alte anonyme Sitzung ohne Konto: neu anmelden
            Username = name;
            GameSettings.PlayerName = name;
            if (IsDevAccount) GameSettings.DevMode = true;
            Debug.Log($"[Konto] angemeldet: {name}");
            // Name fuer die Entwickler-Kontoliste (oeffentlich lesbar, nur der Name)
            try
            {
                string now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
                var data = new Dictionary<string, object> { { "name", name.ToLowerInvariant() }, { "zuletzt", now } };
                if (created) data["seit"] = now;
                await CloudSaveService.Instance.Data.Player.SaveAsync(data,
                    new Unity.Services.CloudSave.Models.Data.Player.SaveOptions(new Unity.Services.CloudSave.Models.Data.Player.PublicWriteAccessClassOptions()));
            }
            catch (Exception e) { Debug.LogWarning("[Konto] Cloud Save: " + e.Message); }
        }

        public static void Logout()
        {
            SafeSignOut();
            Message = "Abgemeldet.";
        }

        static void SafeSignOut()
        {
            Username = "";
            try { if (UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn) AuthenticationService.Instance.SignOut(true); } catch { }
            try { if (UnityServices.State == ServicesInitializationState.Initialized) AuthenticationService.Instance.ClearSessionToken(); } catch { }
        }

        static string Check(string user, string pw)
        {
            if (user.Length < 3 || user.Length > 20) return "Benutzername: 3 bis 20 Zeichen.";
            foreach (char c in user)
                if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '.' || c == '-' || c == '_' || c == '@'))
                    return "Benutzername: nur Buchstaben (ohne Umlaute), Ziffern und . - _ @";
            if (pw.Length < MinPw || pw.Length > MaxPw) return $"Passwort: {MinPw} bis {MaxPw} Zeichen.";
            return null;
        }

        static string Hash(string pw)
        {
            var salt = System.Text.Encoding.UTF8.GetBytes("BallistikStudios-Konto-steo");
            using (var k = new System.Security.Cryptography.Rfc2898DeriveBytes(pw, salt, 200000, System.Security.Cryptography.HashAlgorithmName.SHA256))
            {
                var sb = new System.Text.StringBuilder();
                foreach (byte b in k.GetBytes(32)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static string Explain(Exception e, bool create)
        {
            while (e is AggregateException && e.InnerException != null) e = e.InnerException;
            Debug.LogWarning("[Konto] " + e);
            string m = e.Message ?? "";
            if (m.Contains("provider is not available") || m.Contains("PERMISSION_DENIED"))
                return "Konten sind noch nicht freigeschaltet (Unity-Dashboard: Username & Password).";
            if (create && (m.Contains("exist") || m.Contains("ENTITY_EXISTS") || m.Contains("already")))
                return "Dieser Name ist vergeben.";
            if (m.Contains("WRONG_USERNAME_PASSWORD") || m.Contains("Invalid username or password") || m.Contains("INVALID_PASSWORD") || m.Contains("username or password"))
                return "Benutzername oder Passwort falsch.";
            if (m.Contains("Network") || m.Contains("network") || m.Contains("Cannot resolve") || m.Contains("timed out"))
                return "Keine Verbindung zum Konto-Server (Internet?).";
            return (create ? "Konto anlegen" : "Anmelden") + " fehlgeschlagen: " + (m.Length > 110 ? m.Substring(0, 110) : m);
        }

        // ---------------- Kontoliste (nur Entwickler) ----------------
        public class Entry { public string name, seit, zuletzt; }
        public static List<Entry> List = new List<Entry>();
        public static string ListMessage = "";
        public static bool ListBusy;

        public static async void LoadList()
        {
            if (!IsDevAccount || ListBusy) return;
            ListBusy = true; ListMessage = "Lade Konten ...";
            try
            {
                var result = new List<Entry>();
                for (int offset = 0; offset < 2000; offset += 100)
                {
                    var q = new Query(new List<FieldFilter> { new FieldFilter("name", "", FieldFilter.OpOptions.GE, true) },
                        new HashSet<string> { "name", "seit", "zuletzt" }, offset, 100);
                    var page = await CloudSaveService.Instance.Data.Player.QueryAsync(q, new Unity.Services.CloudSave.Models.Data.Player.QueryOptions());
                    foreach (var ent in page)
                    {
                        var en = new Entry { name = "?", seit = "", zuletzt = "" };
                        if (ent.Data != null)
                            foreach (var it in ent.Data)
                            {
                                string v = "";
                                try { v = it.Value.GetAs<string>(); } catch { }
                                if (it.Key == "name") en.name = v; else if (it.Key == "seit") en.seit = v; else if (it.Key == "zuletzt") en.zuletzt = v;
                            }
                        result.Add(en);
                    }
                    if (page.Count < 100) break;
                }
                List = result;
                ListMessage = $"{result.Count} Konten";
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Konto] Liste: " + e);
                string m = e.Message ?? "";
                ListMessage = m.Contains("index") || m.Contains("Index") || m.Contains("422") || m.Contains("400")
                    ? "Kontoliste braucht im Unity-Dashboard einen Cloud-Save-Index (Queries: Player, Public, Schluessel \"name\")."
                    : "Kontoliste fehlgeschlagen: " + (m.Length > 120 ? m.Substring(0, 120) : m);
            }
            ListBusy = false;
        }
    }
}
