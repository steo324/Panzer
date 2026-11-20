using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudSave;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Statistik pro Konto: Schuesse, Treffer, Durchschlaege, Abschuesse (Panzer, Flugzeuge, Pak/Infanterie), Verluste,
    /// Fahrzeit je Panzer (daraus der Lieblingspanzer) und der beste Wellen-Modus-Lauf.
    /// Gespeichert in Unity Cloud Save beim eigenen Spieler (Schluessel "statistik", privat); ohne Konto lokal.
    /// Rangliste Wellen-Modus: Bestwert zusaetzlich oeffentlich ("wellen", "wellenAbschuesse"); die Liste wird ueber den
    /// vorhandenen Namens-Index abgefragt und hier sortiert (kein eigener Dienst noetig).
    /// </summary>
    public static class PlayerStats
    {
        [Serializable]
        public class Data
        {
            public int shots, hits, pens, kills, airKills, groundKills, deaths, rounds, waveBest, waveBestKills;
            public List<string> tanks = new List<string>();
            public List<float> tankSeconds = new List<float>();
        }

        public static Data S = new Data();
        static bool dirty, loaded;
        static float saveTimer;
        static string loadedFor = "";
        public static string Status = "";
        /// <summary>Test (-bench): eigene Testwerte, nichts in Cloud Save oder die echte lokale Statistik.</summary>
        public static bool TestMode;

        // ---------------- Erfassen ----------------
        public static void OnShot() { S.shots++; dirty = true; }
        public static void OnHit(TankController target, bool pen)
        {
            if (target == null || target.isPlayer) return;
            S.hits++; if (pen) S.pens++;
            target.lastPlayerHit = Time.time;
            dirty = true;
        }
        public static void OnKill(TankController t) { S.kills++; dirty = true; }
        public static void OnAirKill() { S.airKills++; dirty = true; }
        public static void OnGroundKill() { S.groundKills++; dirty = true; }
        public static void OnDeath() { S.deaths++; dirty = true; }
        public static void OnRound() { S.rounds++; dirty = true; }
        public static void OnWaveEnd(int wave, int kills)
        {
            if (wave > S.waveBest || (wave == S.waveBest && kills > S.waveBestKills)) { S.waveBest = wave; S.waveBestKills = kills; dirty = true; SaveSoon(true); }
        }

        /// <summary>Fahrzeit im Panzer (GameManager ruft das jede Sekunde im Spiel).</summary>
        public static void Drive(string tank, float sec)
        {
            int i = S.tanks.IndexOf(tank);
            if (i < 0) { S.tanks.Add(tank); S.tankSeconds.Add(0f); i = S.tanks.Count - 1; }
            S.tankSeconds[i] += sec;
            dirty = true;
        }

        public static string Favorite
        {
            get
            {
                int best = -1; float bs = 0f;
                for (int i = 0; i < S.tanks.Count && i < S.tankSeconds.Count; i++) if (S.tankSeconds[i] > bs) { bs = S.tankSeconds[i]; best = i; }
                return best >= 0 ? $"{S.tanks[best]} ({bs / 60f:0} min)" : "-";
            }
        }

        // ---------------- Laden / Speichern ----------------
        static bool Online => Accounts.LoggedIn;
        static string LocalKey => TestMode ? "statistik.test" : "statistik.lokal";

        /// <summary>Nach dem Anmelden (oder beim Start ohne Konto): Statistik laden.</summary>
        public static async void Load()
        {
            string who = Online && !TestMode ? Accounts.PlayerId : "lokal";
            if (loaded && loadedFor == who) return;
            S = new Data(); loaded = true; loadedFor = who;
            if (TestMode) return;
            if (!Online)
            {
                try { var j = PlayerPrefs.GetString(LocalKey, ""); if (j.Length > 0) S = JsonUtility.FromJson<Data>(j) ?? new Data(); } catch { S = new Data(); }
                return;
            }
            try
            {
                var r = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { "statistik" });
                if (r.TryGetValue("statistik", out var it)) S = JsonUtility.FromJson<Data>(it.Value.GetAs<string>()) ?? new Data();
                Status = "";
            }
            catch (Exception e) { Debug.LogWarning("[Statistik] Laden: " + e.Message); Status = "Statistik konnte nicht geladen werden (offline?)"; }
        }

        public static void Tick(float dt)
        {
            if (!loaded) Load();
            string who = Online && !TestMode ? Accounts.PlayerId : "lokal";
            if (loaded && loadedFor != who) { loaded = false; Load(); return; }   // Konto gewechselt
            saveTimer -= dt;
            if (dirty && saveTimer <= 0f) SaveSoon(false);
        }

        static bool saving;
        public static async void SaveSoon(bool force)
        {
            if (saving || (!dirty && !force)) return;
            saveTimer = 60f;
            dirty = false;
            string json = JsonUtility.ToJson(S);
            if (!Online || TestMode) { PlayerPrefs.SetString(LocalKey, json); PlayerPrefs.Save(); return; }
            saving = true;
            try
            {
                await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object> { { "statistik", json } });
                // Rangliste: Bestwert oeffentlich (nur Welle, Abschuesse; der Name steht schon oeffentlich)
                if (S.waveBest > 0)
                    await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object> { { "wellen", S.waveBest }, { "wellenAbschuesse", S.waveBestKills } },
                        new Unity.Services.CloudSave.Models.Data.Player.SaveOptions(new Unity.Services.CloudSave.Models.Data.Player.PublicWriteAccessClassOptions()));
            }
            catch (Exception e) { Debug.LogWarning("[Statistik] Speichern: " + e.Message); dirty = true; }
            saving = false;
        }

        // ---------------- Rangliste (Wellen-Modus) ----------------
        public class Rank { public string name; public int wave, kills; }
        public static List<Rank> Ranking = new List<Rank>();
        public static string RankMessage = "";
        public static bool RankBusy;

        public static async void LoadRanking()
        {
            if (RankBusy) return;
            if (!Online) { RankMessage = "Rangliste nur mit Konto (angemeldet)."; return; }
            RankBusy = true; RankMessage = "Lade Rangliste ...";
            try
            {
                var list = new List<Rank>();
                for (int offset = 0; offset < 1000; offset += 100)
                {
                    var q = new Unity.Services.CloudSave.Models.Query(
                        new List<Unity.Services.CloudSave.Models.FieldFilter> { new Unity.Services.CloudSave.Models.FieldFilter("name", "", Unity.Services.CloudSave.Models.FieldFilter.OpOptions.GE, true) },
                        new HashSet<string> { "name", "wellen", "wellenAbschuesse" }, offset, 100);
                    var page = await CloudSaveService.Instance.Data.Player.QueryAsync(q, new Unity.Services.CloudSave.Models.Data.Player.QueryOptions());
                    foreach (var ent in page)
                    {
                        var rk = new Rank { name = "?" };
                        if (ent.Data != null)
                            foreach (var it in ent.Data)
                            {
                                try
                                {
                                    if (it.Key == "name") rk.name = it.Value.GetAs<string>();
                                    else if (it.Key == "wellen") rk.wave = it.Value.GetAs<int>();
                                    else if (it.Key == "wellenAbschuesse") rk.kills = it.Value.GetAs<int>();
                                }
                                catch { }
                            }
                        if (rk.wave > 0) list.Add(rk);
                    }
                    if (page.Count < 100) break;
                }
                list.Sort((a, b) => b.wave != a.wave ? b.wave.CompareTo(a.wave) : b.kills.CompareTo(a.kills));
                Ranking = list;
                RankMessage = list.Count == 0 ? "Noch keine Eintraege - spiel den Wellen-Modus!" : $"{list.Count} Spieler";
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Rangliste] " + e);
                RankMessage = "Rangliste nicht verfuegbar: " + (e.Message.Length > 100 ? e.Message.Substring(0, 100) : e.Message);
            }
            RankBusy = false;
        }

        public static string Summary
        {
            get
            {
                float acc = S.shots > 0 ? S.hits * 100f / S.shots : 0f, pen = S.hits > 0 ? S.pens * 100f / S.hits : 0f;
                return $"Runden: <b>{S.rounds}</b>   ·   Schuesse: <b>{S.shots}</b>   ·   Treffer: <b>{S.hits}</b> ({acc:0} %)   ·   Durchschlaege: <b>{S.pens}</b> ({pen:0} % der Treffer)\n" +
                       $"Abschuesse: <b>{S.kills}</b> Panzer, <b>{S.airKills}</b> Flugzeuge, <b>{S.groundKills}</b> Pak/Infanterie   ·   Verluste: <b>{S.deaths}</b>\n" +
                       $"Lieblingspanzer: <b>{Favorite}</b>   ·   Wellen-Modus bestes Spiel: <b>Welle {S.waveBest}</b> ({S.waveBestKills} Abschuesse)";
            }
        }
    }
}
