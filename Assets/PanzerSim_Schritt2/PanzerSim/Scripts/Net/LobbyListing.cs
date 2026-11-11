using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Oeffentliche Serverliste im Internet ueber Unity Lobby:
    ///  - Der dedizierte Server traegt sich mit Name, Karte, Version, Spielerzahl und Relay-Beitrittscode ein
    ///    und meldet sich alle 15 s ("Herzschlag"), sonst verschwindet der Eintrag nach ca. 30 s von selbst.
    ///  - Die Serverliste im Spiel fragt die Eintraege ab; Beitreten laeuft ueber den Code (Unity Relay),
    ///    also ohne Tailscale, ohne IP und ohne Portfreigabe.
    /// </summary>
    public static class LobbyListing
    {
        static string lobbyId;
        static int lastPlayers = -1, wantPlayers;
        static float nextHeartbeat, nextUpdate;
        static bool updating;

        // ================= Server: eintragen =================
        public static async Task Publish(string name, string map, string code, int max)
        {
            try
            {
                await RelayConnect.EnsureSignedIn();
                var opt = new CreateLobbyOptions
                {
                    IsPrivate = false,
                    Data = new Dictionary<string, DataObject>
                    {
                        { "code", new DataObject(DataObject.VisibilityOptions.Public, code) },
                        { "map", new DataObject(DataObject.VisibilityOptions.Public, map) },
                        { "version", new DataObject(DataObject.VisibilityOptions.Public, GameVersion.Current) },
                        { "players", new DataObject(DataObject.VisibilityOptions.Public, "0") },
                        { "max", new DataObject(DataObject.VisibilityOptions.Public, max.ToString()) },
                    }
                };
                string n = string.IsNullOrWhiteSpace(name) ? "Ballistik-Server" : name;
                if (n.Length > 48) n = n.Substring(0, 48);
                var lobby = await LobbyService.Instance.CreateLobbyAsync(n, Mathf.Clamp(max, 2, 100), opt);
                lobbyId = lobby.Id;
                lastPlayers = 0;
                nextHeartbeat = Time.realtimeSinceStartup + 15f;
                Debug.Log($"[Server] Oeffentlich gelistet (Unity Lobby): '{n}'");
                Application.quitting += Unpublish;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Server] Oeffentliche Liste (Unity Lobby) nicht verfuegbar: " + e.Message);
            }
        }

        /// <summary>Jedes Bild aufrufen (Server): Herzschlag und Spielerzahl.</summary>
        public static void Tick()
        {
            if (lobbyId == null || updating) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextHeartbeat)
            {
                nextHeartbeat = now + 15f;
                _ = Heartbeat();
            }
            else if (wantPlayers != lastPlayers && now >= nextUpdate)
            {
                nextUpdate = now + 5f;
                _ = UpdatePlayers(wantPlayers);
            }
        }

        public static void SetPlayers(int n) => wantPlayers = n;

        static async Task Heartbeat()
        {
            updating = true;
            try { await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId); }
            catch (System.Exception e) { Debug.LogWarning("[Server] Lobby-Herzschlag: " + e.Message); }
            updating = false;
        }

        static async Task UpdatePlayers(int n)
        {
            updating = true;
            try
            {
                await LobbyService.Instance.UpdateLobbyAsync(lobbyId, new UpdateLobbyOptions
                {
                    Data = new Dictionary<string, DataObject> { { "players", new DataObject(DataObject.VisibilityOptions.Public, n.ToString()) } }
                });
                lastPlayers = n;
            }
            catch (System.Exception e) { Debug.LogWarning("[Server] Lobby-Update: " + e.Message); }
            updating = false;
        }

        public static void Unpublish()
        {
            if (lobbyId == null) return;
            string id = lobbyId; lobbyId = null;
            Application.quitting -= Unpublish;
            try { _ = LobbyService.Instance.DeleteLobbyAsync(id); } catch { }   // sonst verschwindet er ohne Herzschlag von selbst
        }

        // ================= Client: abfragen =================
        static bool querying;

        /// <summary>Oeffentliche Server abfragen; null = gerade nicht moeglich (offline, Dienst nicht erreichbar).</summary>
        public static async Task<List<ServerQuery.Entry>> Query()
        {
            if (querying) return null;
            querying = true;
            try
            {
                await RelayConnect.EnsureSignedIn();
                var resp = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions { Count = 50 });
                var list = new List<ServerQuery.Entry>();
                foreach (var l in resp.Results)
                {
                    if (l.Data == null || !l.Data.TryGetValue("code", out var code)) continue;
                    string D(string k) => l.Data.TryGetValue(k, out var v) && v != null ? v.Value : "";
                    int.TryParse(D("players"), out int pl); int.TryParse(D("max"), out int mx);
                    list.Add(new ServerQuery.Entry
                    {
                        address = "Internet",
                        internet = true,
                        lastSeen = Time.unscaledTime,
                        info = new ServerQuery.Info { name = l.Name, map = D("map"), version = D("version"), players = pl, max = mx > 0 ? mx : NetSync.MaxPlayers, code = code.Value },
                    });
                }
                return list;
            }
            catch (System.Exception e) { Debug.LogWarning("Serverliste (Internet): " + e.Message); return null; }
            finally { querying = false; }
        }
    }
}
