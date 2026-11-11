using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Mehrspieler-Grundlagen. Der Server (oder Host) rechnet alles, was zaehlt: Treffer, Durchschlag, Schaden, Brand,
    /// Zerstoerung, Ketten, Anbauteile, Treffermarken - und schickt die Ergebnisse an alle.
    /// Jeder Spieler faehrt und zielt seinen eigenen Panzer selbst (fuehlt sich ohne Verzoegerung an).
    /// Granaten der anderen fliegen bei jedem Spieler als "Geist" mit (Optik), machen dort aber keinen Schaden.
    /// </summary>
    public static class NetSync
    {
        public const ushort DefaultPort = 7777;

        public static NetworkManager NM => NetworkManager.Singleton;
        /// <summary>Laeuft gerade ein Mehrspieler-Spiel (Host, Client oder Server)?</summary>
        public static bool Active => NM != null && NM.IsListening;
        /// <summary>Wir sind Server oder Host: unsere Rechnung zaehlt.</summary>
        public static bool IsServer => Active && NM.IsServer;
        /// <summary>Wir sind reiner Client: Schaden kommt nur vom Server.</summary>
        public static bool IsClientOnly => Active && !NM.IsServer;
        /// <summary>Dedizierter Server ohne Grafik (z. B. auf Linux).</summary>
        public static bool Headless => Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        /// <summary>Gerade wird eine Geister-Granate gerechnet: keinen Schaden, keine Marken, kein Protokoll.</summary>
        public static bool GhostActive;
        /// <summary>Gerade wird ein Ergebnis vom Server angewendet (darf Schaden setzen, auch auf Clients).</summary>
        public static bool ApplyingServerState;

        public const int MaxPlayers = 8;
        /// <summary>Name in der Serverliste (Startparameter -name).</summary>
        public static string ServerName = "Ballistik-Server";

        static byte[] Payload(int tankIndex)
        {
            var v = System.Text.Encoding.UTF8.GetBytes(GameVersion.Current);
            var p = new byte[1 + v.Length];
            p[0] = (byte)tankIndex;
            System.Array.Copy(v, 0, p, 1, v.Length);
            return p;
        }

        /// <summary>Angaben fuer die Serverliste aktualisieren (Server/Host, im Hauptthread).</summary>
        public static void UpdateQueryInfo(string mapName, ushort port)
        {
            if (!IsServer) return;
            int players = NM.ConnectedClientsIds.Count;   // beim Host zaehlt er selbst mit
            ServerQuery.SetInfo(new ServerQuery.Info { name = ServerName, map = mapName, version = GameVersion.Current, players = players, max = MaxPlayers, port = port });
        }

        public static GameObject TankPrefab, SessionPrefab;
        static readonly Dictionary<ulong, int> requestedTank = new Dictionary<ulong, int>();
        public static int MapIndex;
        public static string LastError = "";

        /// <summary>NetworkManager einmal anlegen (auch zur Laufzeit moeglich, keine Szene noetig).</summary>
        public static NetworkManager Ensure()
        {
            if (NM != null) return NM;
            TankPrefab = Resources.Load<GameObject>("Netz/NetTank");
            SessionPrefab = Resources.Load<GameObject>("Netz/NetSession");
            if (TankPrefab == null || SessionPrefab == null) { LastError = "Netzwerk-Prefabs fehlen (Resources/Netz)."; Debug.LogError(LastError); return null; }
            var go = new GameObject("NetworkManager");
            Object.DontDestroyOnLoad(go);
            var ut = go.AddComponent<UnityTransport>();
            var nm = go.AddComponent<NetworkManager>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = ut,
                ConnectionApproval = true,
                EnableSceneManagement = false,   // eine Szene, Karte wird bei jedem selbst gebaut
                TickRate = 30,
            };
            nm.SetSingleton();
            nm.AddNetworkPrefab(TankPrefab);
            nm.AddNetworkPrefab(SessionPrefab);
            nm.ConnectionApprovalCallback = (req, resp) =>
            {
                // Anfrage: [Panzer-Nummer][Version als Text]
                int tank = req.Payload != null && req.Payload.Length > 0 ? req.Payload[0] : 0;
                string ver = req.Payload != null && req.Payload.Length > 1 ? System.Text.Encoding.UTF8.GetString(req.Payload, 1, req.Payload.Length - 1) : "?";
                resp.CreatePlayerObject = false;
                bool self = req.ClientNetworkId == NetworkManager.ServerClientId;   // Host selbst
                if (!self && ver != GameVersion.Current && !GameVersion.IsDev)
                {
                    resp.Approved = false;
                    resp.Reason = $"Andere Version: Server {GameVersion.Current}, du {ver}. Spiel neu starten (aktualisiert sich dann).";
                    return;
                }
                if (!self && NM.ConnectedClientsIds.Count >= MaxPlayers)
                {
                    resp.Approved = false;
                    resp.Reason = $"Server voll ({MaxPlayers} Spieler).";
                    return;
                }
                requestedTank[req.ClientNetworkId] = tank;
                resp.Approved = true;
            };
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
            return nm;
        }

        static UnityTransport Transport => NM != null ? NM.GetComponent<UnityTransport>() : null;

        public static bool StartHost(int tankIndex, int mapIndex, ushort port)
        {
            var nm = Ensure(); if (nm == null) return false;
            MapIndex = mapIndex;
            Transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
            nm.NetworkConfig.ConnectionData = Payload(tankIndex);
            bool ok = nm.StartHost();
            if (ok) { SpawnSession(); ServerQuery.StartResponder(); } else LastError = "Host konnte nicht starten (Port belegt?).";
            return ok;
        }

        /// <summary>Host ueber Unity Relay (vorher RelayConnect.Host). Kein offener Port noetig.</summary>
        public static bool StartHostRelay(int tankIndex, int mapIndex)
        {
            var nm = Ensure(); if (nm == null) return false;
            MapIndex = mapIndex;
            Transport.SetRelayServerData(RelayConnect.Data);
            nm.NetworkConfig.ConnectionData = Payload(tankIndex);
            bool ok = nm.StartHost();
            if (ok) SpawnSession(); else LastError = "Host konnte nicht starten.";
            return ok;
        }

        /// <summary>Client ueber Unity Relay (vorher RelayConnect.Join).</summary>
        public static bool StartClientRelay(int tankIndex)
        {
            var nm = Ensure(); if (nm == null) return false;
            Transport.SetRelayServerData(RelayConnect.Data);
            nm.NetworkConfig.ConnectionData = Payload(tankIndex);
            bool ok = nm.StartClient();
            if (!ok) LastError = "Verbindung konnte nicht gestartet werden.";
            return ok;
        }

        public static bool StartServer(int mapIndex, ushort port)
        {
            var nm = Ensure(); if (nm == null) return false;
            MapIndex = mapIndex;
            Transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
            bool ok = nm.StartServer();
            if (ok) { SpawnSession(); ServerQuery.StartResponder(); } else LastError = "Server konnte nicht starten (Port belegt?).";
            return ok;
        }

        public static bool StartClient(int tankIndex, string address, ushort port)
        {
            var nm = Ensure(); if (nm == null) return false;
            Transport.SetConnectionData(address, port);
            nm.NetworkConfig.ConnectionData = Payload(tankIndex);
            bool ok = nm.StartClient();
            if (!ok) LastError = "Verbindung konnte nicht gestartet werden.";
            return ok;
        }

        public static void Stop()
        {
            ServerQuery.StopResponder();
            if (NM != null && NM.IsListening) NM.Shutdown();
            requestedTank.Clear();
        }

        static void SpawnSession()
        {
            var s = Object.Instantiate(SessionPrefab);
            s.GetComponent<NetSession>().mapIndex.Value = MapIndex;
            s.GetComponent<NetworkObject>().Spawn();
            // Zielpanzer der Karte als Server-Panzer (stehen, werden von allen beschossen)
            GameManager.Instance?.ServerSpawnTargets();
        }

        static void OnClientConnected(ulong clientId)
        {
            if (!NM.IsServer) return;
            Debug.Log($"[Netz] Spieler {clientId} verbunden");
            int tank = requestedTank.TryGetValue(clientId, out int t) ? t : 0;
            SpawnPlayerTank(clientId, tank);
            NetSession.Instance?.Log($"Spieler {clientId} ist beigetreten");
        }

        static void OnClientDisconnected(ulong clientId)
        {
            if (NM.IsServer)
            {
                Debug.Log($"[Netz] Spieler {clientId} getrennt");
                NetSession.Instance?.Log($"Spieler {clientId} hat das Spiel verlassen");
                // Panzer des Spielers entfernen
                foreach (var nt in Object.FindObjectsByType<NetTank>(FindObjectsSortMode.None))
                    if (!nt.isTarget.Value && nt.OwnerClientId == clientId && nt.IsSpawned) nt.NetworkObject.Despawn(true);
            }
            else if (clientId == NM.LocalClientId || !NM.IsConnectedClient)
            {
                LastError = !string.IsNullOrEmpty(NM.DisconnectReason) ? NM.DisconnectReason : "Verbindung zum Server getrennt.";
                GameManager.Instance?.OnNetworkEnded();
            }
        }

        static int spawnCounter;
        /// <summary>Panzer fuer einen Spieler erzeugen (nur Server). Spawnpunkte nebeneinander am Startpunkt der Karte.</summary>
        public static void SpawnPlayerTank(ulong clientId, int tankIndex)
        {
            var gm = GameManager.Instance;
            Vector3 pos; float yaw;
            gm.SpawnPoint(spawnCounter++, out pos, out yaw);
            var go = Object.Instantiate(TankPrefab, pos, Quaternion.Euler(0f, yaw, 0f));
            var nt = go.GetComponent<NetTank>();
            nt.tankType.Value = tankIndex;
            nt.isTarget.Value = false;
            go.GetComponent<NetworkObject>().SpawnWithOwnership(clientId, true);
        }

        public static void SpawnTargetTank(int tankIndex, Vector3 pos, float yaw)
        {
            var go = Object.Instantiate(TankPrefab, pos, Quaternion.Euler(0f, yaw, 0f));
            var nt = go.GetComponent<NetTank>();
            nt.tankType.Value = tankIndex;
            nt.isTarget.Value = true;
            go.GetComponent<NetworkObject>().Spawn(true);
        }

        // ---------------- Hilfen fuer die Spiel-Logik ----------------

        /// <summary>Auf dem Server: Ergebnis an alle Clients schicken? (nicht fuer Geister, nicht ohne Netz)</summary>
        public static bool ShouldBroadcast(TankController tc) => IsServer && !GhostActive && tc != null && tc.net != null && tc.net.IsSpawned;

        /// <summary>Darf hier Schaden entstehen? Offline und auf dem Server ja; auf Clients nur, wenn der Server es schickt.</summary>
        public static bool MayApplyDamage => !GhostActive && (!IsClientOnly || ApplyingServerState);
    }
}
