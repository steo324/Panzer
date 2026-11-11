using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Hauptmenue (Panzer + Karte waehlen, 3D-Vorschau), Spielstart, Pausemenue (Esc).
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        enum State { Menu, Playing, Paused }
        State state;

        static readonly System.Func<TankSpec>[] Tanks =
            { () => TankLibrary.Tiger, () => TankLibrary.PanzerIV, () => TankLibrary.Sherman, () => TankLibrary.T34_85 };
        readonly GameMap[] maps = { new PlainsMap(), new TestRangeMap() };

        int tankIndex, mapIndex;
        GameMap map;
        Transform mapRoot;
        TankController player;
        readonly List<TankController> targets = new List<TankController>();

        Camera cam;
        TankCamera tankCam;
        TankController preview;
        Transform stage;
        float orbit;
        static readonly Vector3 StagePos = new Vector3(0f, -2000f, 0f);

        public static GameManager Instance { get; private set; }
        public static TankSpec TankSpecByIndex(int i) => Tanks[Mathf.Clamp(i, 0, Tanks.Length - 1)]();
        static int IndexOfSpec(TankSpec s) { for (int i = 0; i < Tanks.Length; i++) if (Tanks[i]().name == s.name) return i; return 0; }

        // ---- Mehrspieler ----
        bool netGame, connecting, autoFire;
        float connectStart, statusTimer;
        NetTank localNet;
        string ipText = "127.0.0.1", portText = NetSync.DefaultPort.ToString(), netMessage = "";
        bool relayBusy, viaRelay;
        string hostCode = "", codeText = "";

        void Awake() { Instance = this; }

        void Start()
        {
            Time.fixedDeltaTime = 0.01f;
            Application.runInBackground = true;   // Mehrspieler: auch ohne Fokus weiterrechnen
            cam = Camera.main;
            tankCam = cam.GetComponent<TankCamera>();
            if (tankCam == null) tankCam = cam.gameObject.AddComponent<TankCamera>();
            GameSettings.EnsureLoaded();
            GameSettings.ApplyCamera(cam);

            // Startparameter: -server / -host / -connect <IP>  [-map n] [-tank n] [-port n] [-autofire]
            var args = System.Environment.GetCommandLineArgs();
            string Arg(string name) { int i = System.Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            bool Has(string name) => System.Array.IndexOf(args, name) >= 0;
            if (int.TryParse(Arg("-map"), out int m)) mapIndex = Mathf.Clamp(m, 0, maps.Length - 1);
            if (int.TryParse(Arg("-tank"), out int t)) tankIndex = Mathf.Clamp(t, 0, Tanks.Length - 1);
            if (ushort.TryParse(Arg("-port"), out ushort p)) portText = p.ToString();
            autoFire = Has("-autofire");
            NetSync.ServerName = Arg("-name") ?? $"Ballistik-Server ({System.Environment.MachineName})";
            if (Has("-server") || NetSync.Headless) { StartDedicatedServer(); return; }
            gameObject.AddComponent<GameUpdater>();   // neue Version auf GitHub? -> laden, neu starten
            LoadServerList();
            ShowMenu();
            if (Has("-host")) StartHostGame();
            else if (Arg("-code") != null) StartJoinCode(Arg("-code"));
            else if (Arg("-connect") != null) { ipText = Arg("-connect"); StartJoin(); }
        }

        ushort Port => ushort.TryParse(portText, out ushort p) ? p : NetSync.DefaultPort;

        /// <summary>Karte bauen (bei jedem Spieler gleich, feste Zufallszahl).</summary>
        void BuildMap(int index)
        {
            if (preview != null) Destroy(preview.gameObject);
            if (stage != null) stage.gameObject.SetActive(false);
            if (mapRoot != null) Destroy(mapRoot.gameObject);
            mapIndex = index;
            map = maps[index];
            mapRoot = new GameObject("Karte: " + map.Name).transform;
            map.Build(mapRoot);
            RenderSettings.fog = map.Fog && !NetSync.Headless;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.78f, 0.84f);
            RenderSettings.fogStartDistance = 400f;
            RenderSettings.fogEndDistance = 2600f;
        }

        void EnterPlaying()
        {
            RangeLog.Lines.Clear();
            TankController.ViewMode = 0;
            state = State.Playing;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // ================= Mehrspieler-Ablauf =================
        /// <summary>Hosten: zuerst ueber Unity Relay (Beitrittscode, geht ohne Portfreigabe), sonst klassisch ueber Port 7777.</summary>
        async void StartHostGame()
        {
            if (relayBusy) return;
            netMessage = ""; hostCode = ""; relayBusy = true;
            string code = null, relayError = null;
            try { code = await RelayConnect.Host(NetSync.MaxPlayers - 1); }
            catch (System.Exception e) { relayError = RelayConnect.Explain(e); }
            relayBusy = false;
            if (this == null || state != State.Menu) return;
            BuildMap(mapIndex);
            bool ok = code != null ? NetSync.StartHostRelay(tankIndex, mapIndex) : NetSync.StartHost(tankIndex, mapIndex, Port);
            if (!ok) { netMessage = NetSync.LastError; ShowMenu(); return; }
            netGame = true;
            EnterPlaying();
            if (code != null)
            {
                hostCode = code;
                Debug.Log("[Netz] Beitrittscode: " + code);
                RangeLog.AddLocal($"Spiel gehostet. Beitrittscode: {code}  (steht oben im Bild; Freunde: Serverliste -> Code eingeben)");
            }
            else
                RangeLog.AddLocal($"{relayError} Gehostet auf Port {Port} - nur Heimnetz/Tailscale koennen beitreten.");
        }

        void StartJoin()
        {
            netMessage = ""; viaRelay = false;
            if (!NetSync.StartClient(tankIndex, ipText.Trim(), Port)) { netMessage = NetSync.LastError; return; }
            netGame = true;
            connecting = true;
            connectStart = Time.unscaledTime;
        }

        /// <summary>Beitreten mit Beitrittscode (Unity Relay).</summary>
        async void StartJoinCode(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (relayBusy || code.Length == 0) return;
            netMessage = ""; relayBusy = true; listOpen = false;
            try { await RelayConnect.Join(code); }
            catch (System.Exception e) { relayBusy = false; netMessage = RelayConnect.Explain(e); return; }
            relayBusy = false;
            if (this == null || state != State.Menu) return;
            viaRelay = true; ipText = code;
            if (!NetSync.StartClientRelay(tankIndex)) { netMessage = NetSync.LastError; return; }
            netGame = true;
            connecting = true;
            connectStart = Time.unscaledTime;
        }

        string ConnectTarget => viaRelay ? $"Code {ipText}" : $"{ipText}:{Port}";

        /// <summary>Client: Server hat die Spielrunde geschickt -> Karte bauen und los.</summary>
        public void BeginClientGame(int mapIdx)
        {
            connecting = false;
            BuildMap(Mathf.Clamp(mapIdx, 0, maps.Length - 1));
            EnterPlaying();
            RangeLog.AddLocal($"Verbunden mit {ConnectTarget}");
        }

        void StartDedicatedServer()
        {
            Application.targetFrameRate = 60;   // sonst rechnet der Server ohne Pause mit voller CPU-Last
            BuildMap(mapIndex);
            if (!NetSync.StartServer(mapIndex, Port)) { Debug.LogError("[Server] " + NetSync.LastError); Application.Quit(1); return; }
            netGame = true;
            state = State.Playing;
            Debug.Log($"[Server] laeuft: Karte '{map.Name}', Port {Port}. Beenden mit Strg+C.");
        }

        public void ServerSpawnTargets()
        {
            if (map == null) return;
            foreach (var s in map.Targets())
                NetSync.SpawnTargetTank(IndexOfSpec(s.spec), new Vector3(s.x, map.GroundY(s.x, s.z) + 0.4f, s.z), s.yaw);
        }

        /// <summary>Spawnpunkte nebeneinander (14 m Abstand) am Startpunkt der Karte.</summary>
        public void SpawnPoint(int i, out Vector3 pos, out float yaw)
        {
            yaw = map != null ? map.PlayerYaw : 0f;
            Vector2 p = map != null ? map.PlayerXZ : Vector2.zero;
            Vector3 side = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            int slot = i % 8;
            float off = (slot % 2 == 0 ? 1 : -1) * ((slot + 1) / 2) * 14f;
            Vector3 xz = new Vector3(p.x, 0f, p.y) + side * off;
            pos = new Vector3(xz.x, (map != null ? map.GroundY(xz.x, xz.z) : 0f) + 0.4f, xz.z);
        }

        public void OnNetTankSpawned(NetTank nt)
        {
            if (!nt.IsLocalPlayer) return;
            localNet = nt;
            player = nt.tc;
            player.autoFire = autoFire;
            tankCam.target = player;
        }

        public void OnNetTankDespawned(NetTank nt)
        {
            if (nt != localNet) return;
            localNet = null;
            player = null;
            if (tankCam != null) tankCam.target = null;
        }

        /// <summary>Verbindung weg (Server beendet, Netzwerkfehler): zurueck ins Menue.</summary>
        public void OnNetworkEnded()
        {
            if (!netGame) return;
            netMessage = NetSync.LastError;
            ShowMenu();
        }

        // ================= Ablauf =================
        void ShowMenu()
        {
            if (netGame || NetSync.Active)
            {
                NetSync.Stop();
                netGame = false; connecting = false; localNet = null; hostCode = "";
            }
            ClearGame();
            state = State.Menu;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            BuildPreview();
        }

        void BuildPreview()
        {
            if (stage == null)
            {
                stage = new GameObject("Menue-Vorschau").transform;
                stage.position = StagePos;
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                floor.name = "Podest";
                floor.transform.SetParent(stage, false);
                floor.transform.localPosition = new Vector3(0f, -0.05f, 0f);
                floor.transform.localScale = new Vector3(16f, 0.05f, 16f);
                floor.GetComponent<Renderer>().sharedMaterial = GameMap.Mat(new Color(0.18f, 0.18f, 0.17f));
            }
            stage.gameObject.SetActive(true);
            if (preview != null) Destroy(preview.gameObject);
            preview = TankBuilder.Build(Tanks[tankIndex](), StagePos, Quaternion.Euler(0f, 210f, 0f), false);
            preview.rb.isKinematic = true;
        }

        void StartGame()
        {
            BuildMap(mapIndex);
            SpawnPlayer();
            SpawnTargets();
            EnterPlaying();
        }

        void SpawnPlayer()
        {
            if (player != null) Destroy(player.gameObject);
            Vector2 p = map.PlayerXZ;
            var pos = new Vector3(p.x, map.GroundY(p.x, p.y) + 0.4f, p.y);
            player = TankBuilder.Build(Tanks[tankIndex](), pos, Quaternion.Euler(0f, map.PlayerYaw, 0f), true);
            tankCam.target = player;
        }

        void SpawnTargets()
        {
            foreach (var t in targets) if (t != null) Destroy(t.gameObject);
            targets.Clear();
            foreach (var s in map.Targets())
            {
                var pos = new Vector3(s.x, map.GroundY(s.x, s.z) + 0.4f, s.z);
                targets.Add(TankBuilder.Build(s.spec, pos, Quaternion.Euler(0f, s.yaw, 0f), false));
            }
        }

        void ClearGame()
        {
            // Netzwerk-Objekte raeumt Netcode beim Trennen selbst ab; hier nur die gebauten Panzer entfernen
            foreach (var nt in FindObjectsByType<NetTank>(FindObjectsSortMode.None))
                if (nt.tc != null) { Destroy(nt.tc.gameObject); nt.tc = null; }
            if (player != null) Destroy(player.gameObject);
            foreach (var t in targets) if (t != null) Destroy(t.gameObject);
            targets.Clear();
            if (mapRoot != null) Destroy(mapRoot.gameObject);
            if (tankCam != null) tankCam.target = null;
            foreach (var sh in FindObjectsByType<Shell>(FindObjectsSortMode.None)) Destroy(sh.gameObject);
            foreach (var go in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go.name == "Treffer" || go.name == "Splitter") Destroy(go);
            RenderSettings.fog = false;
            RangeLog.Lines.Clear();
        }

        // ================= Eingabe =================
        void Update()
        {
            // Server/Host: Angaben fuer die Serverliste aktuell halten
            if (NetSync.IsServer && map != null)
            {
                queryTimer -= Time.unscaledDeltaTime;
                if (queryTimer <= 0f) { queryTimer = 1f; NetSync.UpdateQueryInfo(map.Name, Port); }
            }
            if (listOpen) UpdateServerList();
            if (NetSync.Headless)
            {
                // Dedizierter Server: alle 30 s Status ins Log
                statusTimer -= Time.unscaledDeltaTime;
                if (statusTimer <= 0f && NetSync.NM != null)
                {
                    statusTimer = 30f;
                    Debug.Log($"[Server] {NetSync.NM.ConnectedClientsIds.Count} Spieler verbunden, {FindObjectsByType<NetTank>(FindObjectsSortMode.None).Length} Panzer");
                }
                return;
            }
            if (connecting && Time.unscaledTime - connectStart > 15f)
            {
                NetSync.LastError = $"Keine Antwort von {ipText}:{Port} (Server an? Firewall/Port 7777 UDP offen?)";
                OnNetworkEnded();
            }
            if (state == State.Menu)
            {
                // Kamera kreist langsam um den Vorschau-Panzer
                orbit += Time.unscaledDeltaTime * 12f;
                Vector3 c = StagePos + Vector3.up * 1.4f;
                Quaternion q = Quaternion.Euler(12f, orbit, 0f);
                cam.transform.position = c - q * Vector3.forward * 10.5f;
                cam.transform.rotation = q;
                cam.fieldOfView = 50f;
                if (settingsOpen && Inp.Down(KeyCode.Escape)) CloseSettings();
                return;
            }

            if (Inp.Down(KeyCode.Escape))
            {
                if (settingsOpen) CloseSettings();
                else if (state == State.Playing) Pause(true);
                else if (state == State.Paused) Pause(false);
            }
            if (state != State.Playing) return;

            if (Inp.Down(KeyCode.N) && netGame)
            {
                // Mehrspieler: neuer Panzer nur, wenn der eigene zerstoert ist (Server erzeugt ihn)
                if (localNet != null && (player == null || player.destroyed)) localNet.RequestRespawn();
            }
            else if (Inp.Down(KeyCode.N))
            {
                if (player == null || player.destroyed) SpawnPlayer();
                SpawnTargets();
                RangeLog.Lines.Clear();
                foreach (var go in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    if (go.name == "Treffer") Destroy(go);
            }
            if (Inp.Down(KeyCode.T) && player != null) player.ResetUpright();
            if (Inp.Down(KeyCode.B) && player != null && map.RampSpawn.HasValue) player.Teleport(map.RampSpawn.Value, 0f);
        }

        void Pause(bool on)
        {
            state = on ? State.Paused : State.Playing;
            Time.timeScale = on && !netGame ? 0f : 1f;   // im Mehrspieler laeuft die Welt weiter
            Cursor.lockState = on ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = on;
        }

        // ================= Oberflaeche =================
        GUIStyle title, sub, panel, head, text, btn, btnSel, big, small;
        Texture2D texPanel, texBtn, texBtnHover, texSel, texBig, texBigHover, texShade;

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void InitStyles()
        {
            if (title != null) return;
            Color orange = new Color(1f, 0.55f, 0.15f);
            texPanel = Tex(new Color(0.08f, 0.09f, 0.10f, 0.88f));
            texBtn = Tex(new Color(0.18f, 0.19f, 0.20f, 0.95f));
            texBtnHover = Tex(new Color(0.26f, 0.27f, 0.28f, 0.95f));
            texSel = Tex(new Color(0.75f, 0.38f, 0.08f, 0.95f));
            texBig = Tex(new Color(0.85f, 0.42f, 0.08f, 1f));
            texBigHover = Tex(new Color(1f, 0.55f, 0.15f, 1f));
            texShade = Tex(new Color(0f, 0f, 0f, 0.55f));

            title = new GUIStyle(GUI.skin.label) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            title.normal.textColor = Color.white;
            sub = new GUIStyle(title) { fontSize = 18, fontStyle = FontStyle.Normal };
            sub.normal.textColor = orange;
            panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(18, 18, 14, 14) };
            panel.normal.background = texPanel;
            head = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            head.normal.textColor = orange;
            text = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, richText = true };
            text.normal.textColor = new Color(0.88f, 0.88f, 0.86f);
            small = new GUIStyle(text) { fontSize = 13 };
            small.normal.textColor = new Color(0.65f, 0.65f, 0.63f);
            btn = new GUIStyle(GUI.skin.button) { fontSize = 17, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(14, 10, 6, 6) };
            btn.normal.background = texBtn; btn.hover.background = texBtnHover; btn.active.background = texSel;
            btn.normal.textColor = btn.hover.textColor = btn.active.textColor = Color.white;
            btnSel = new GUIStyle(btn) { fontStyle = FontStyle.Bold };
            btnSel.normal.background = btnSel.hover.background = texSel;
            big = new GUIStyle(btn) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            big.normal.background = texBig; big.hover.background = texBigHover;
        }

        void OnGUI()
        {
            if (NetSync.Headless) return;
            if (connecting)
            {
                InitStyles();
                float sc = Screen.height / 1080f;
                GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
                float w = Screen.width / sc;
                GUI.Box(new Rect(w / 2f - 260, 460, 520, 160), GUIContent.none, panel);
                GUI.Label(new Rect(w / 2f - 230, 480, 460, 40), $"Verbinde mit {ConnectTarget} ...", head);
                if (GUI.Button(new Rect(w / 2f - 120, 550, 240, 50), "Abbrechen", btn)) { NetSync.LastError = ""; netMessage = ""; ShowMenu(); }
                GUI.matrix = Matrix4x4.identity;
                return;
            }
            if (relayBusy)
            {
                InitStyles();
                float sc = Screen.height / 1080f;
                GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
                float w = Screen.width / sc;
                GUI.Box(new Rect(w / 2f - 260, 470, 520, 100), GUIContent.none, panel);
                GUI.Label(new Rect(w / 2f - 230, 495, 460, 40), "Online-Verbindung wird aufgebaut ...", head);
                GUI.matrix = Matrix4x4.identity;
                return;
            }
            if (netGame && hostCode.Length > 0)
            {
                // Beitrittscode fuer Freunde immer oben sichtbar
                InitStyles();
                float sc = Screen.height / 1080f;
                GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
                float w = Screen.width / sc;
                GUI.Box(new Rect(w / 2f - 170, 8, 340, 40), GUIContent.none, panel);
                GUI.Label(new Rect(w / 2f - 160, 12, 320, 32), $"Beitrittscode:  <b><color=#fd6>{hostCode}</color></b>", text);
                GUI.matrix = Matrix4x4.identity;
            }
            if (state == State.Playing) return;
            InitStyles();
            // Alles fuer 1920x1080 auslegen und skalieren
            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float W = Screen.width / scale, H = 1080f;

            if (settingsOpen) DrawSettings(W, H);
            else if (state == State.Menu) DrawMenu(W, H);
            else DrawPause(W, H);
            GUI.matrix = Matrix4x4.identity;
        }

        void DrawMenu(float W, float H)
        {
            if (listOpen) { DrawServerList(W, H); return; }   // sonst nimmt das Menue darunter die Klicks
            GUI.Label(new Rect(60, 40, 900, 60), "BALLISTIK STUDIOS", title);
            GUI.Label(new Rect(64, 100, 900, 30), "Panzer  ·  Zweiter Weltkrieg  ·  Echte Ballistik", sub);

            // ---- Panzer ----
            var left = new Rect(60, 160, 430, 760);
            GUI.Box(left, GUIContent.none, panel);
            GUILayout.BeginArea(new Rect(left.x + 18, left.y + 14, left.width - 36, left.height - 28));
            GUILayout.Label("PANZER", head);
            for (int i = 0; i < Tanks.Length; i++)
            {
                var s = Tanks[i]();
                if (GUILayout.Button(s.name, i == tankIndex ? btnSel : btn, GUILayout.Height(44)) && i != tankIndex)
                {
                    tankIndex = i;
                    BuildPreview();
                }
            }
            GUILayout.Space(14);
            GUILayout.Label(TankInfo(Tanks[tankIndex]()), text);
            GUILayout.EndArea();

            // ---- Karte ----
            var right = new Rect(W - 490, 160, 430, 560);
            GUI.Box(right, GUIContent.none, panel);
            GUILayout.BeginArea(new Rect(right.x + 18, right.y + 14, right.width - 36, right.height - 28));
            GUILayout.Label("KARTE", head);
            for (int i = 0; i < maps.Length; i++)
                if (GUILayout.Button(maps[i].Name, i == mapIndex ? btnSel : btn, GUILayout.Height(44))) mapIndex = i;
            GUILayout.Space(14);
            GUILayout.Label(maps[mapIndex].Description, text);

            // ---- Mehrspieler ----
            GUILayout.Space(16);
            GUILayout.Label("MEHRSPIELER", head);
            GUI.enabled = !GameUpdater.Busy && !relayBusy;
            if (GUILayout.Button("Serverliste / Mit Code beitreten", btnSel, GUILayout.Height(44))) OpenServerList();
            if (GUILayout.Button("Selbst hosten (Freunde treten mit Code bei)", btn, GUILayout.Height(40))) StartHostGame();
            GUI.enabled = true;
            if (!string.IsNullOrEmpty(netMessage)) GUILayout.Label($"<color=#f77>{netMessage}</color>", text);
            GUILayout.EndArea();

            // Update-Hinweis und Version
            if (!string.IsNullOrEmpty(GameUpdater.Status))
                GUI.Label(new Rect(W - 490, 720, 430, 26), $"<color=#fd6>{GameUpdater.Status}</color>", small);
            GUI.Label(new Rect(W - 490, 916, 430, 22), $"Version {GameVersion.Current}", small);

            // ---- Start ----
            if (GUI.Button(new Rect(W - 490, 750, 430, 90), "LOS GEHT'S", big)) StartGame();
            if (GUI.Button(new Rect(W - 490, 856, 212, 50), "Einstellungen", btn)) settingsOpen = true;
            if (GUI.Button(new Rect(W - 272, 856, 212, 50), "Beenden", btn)) Quit();

            GUI.Label(new Rect(60, H - 128, 1600, 26),
                "Modelle: Panzer IV von Greg McKechnie (sketchfab.com/mckechniegreg6)  ·  M4A1E9 Sherman von Sem (sketchfab.com/Sem-Hofstede)  ·  beide CC BY 4.0, fuer das Spiel angepasst (gerigged, Kette ersetzt)", small);
            GUI.Label(new Rect(60, H - 102, 1600, 26),
                "Modelle: T-34-85 von DedRam (sketchfab.com/heirohindrima), CC BY 4.0  ·  Weizenbueschel von ebmclachlan (sketchfab.com/ebmclachlan), CC BY 4.0", small);
            GUI.Label(new Rect(60, H - 76, 1400, 26),
                "Weizenhalme: ffish.asia / floraZia.com, CC0", small);
            GUI.Label(new Rect(60, H - 50, 900, 30),
                "Im Spiel: Esc = Pause  ·  W/A/S/D fahren  ·  Maus zielen  ·  Linksklick feuern  ·  Shift Visier  ·  X Ansicht  ·  G Kette reparieren  ·  F3 Dev-Anzeige", small);
        }

        // ================= Serverliste =================
        bool listOpen;
        float probeTimer, queryTimer;
        Vector2 listScroll;
        string addText = "";
        readonly Dictionary<string, ServerQuery.Entry> servers = new Dictionary<string, ServerQuery.Entry>();
        readonly List<string> savedServers = new List<string>();     // vom Spieler eingetragen (lokal gespeichert)
        readonly List<string> listedServers = new List<string>();    // aus servers.txt im GitHub-Repo

        void LoadServerList()
        {
            savedServers.Clear();
            foreach (var s in PlayerPrefs.GetString("servers", "").Split('\n'))
                if (!string.IsNullOrWhiteSpace(s)) savedServers.Add(s.Trim());
            StartCoroutine(FetchListedServers());
        }

        void SaveServerList() { PlayerPrefs.SetString("servers", string.Join("\n", savedServers)); PlayerPrefs.Save(); }

        /// <summary>Optionale oeffentliche Liste: servers.txt im Repo (je Zeile eine Adresse, # = Kommentar).</summary>
        System.Collections.IEnumerator FetchListedServers()
        {
            using (var req = UnityEngine.Networking.UnityWebRequest.Get($"https://raw.githubusercontent.com/{GameUpdater.Repo}/main/servers.txt"))
            {
                req.timeout = 8;
                yield return req.SendWebRequest();
                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success) yield break;
                listedServers.Clear();
                foreach (var line in req.downloadHandler.text.Split('\n'))
                {
                    string l = line.Split('#')[0].Trim();
                    if (l.Length > 0) listedServers.Add(l);
                }
            }
        }

        void OpenServerList() { listOpen = true; probeTimer = 0f; netMessage = ""; }

        void UpdateServerList()
        {
            probeTimer -= Time.unscaledDeltaTime;
            if (probeTimer <= 0f)
            {
                probeTimer = 2f;
                var targets = new List<string>(savedServers);
                targets.AddRange(listedServers);
                ServerQuery.Probe(targets);
                foreach (var s in targets)
                    if (!servers.ContainsKey(s)) servers[s] = new ServerQuery.Entry { address = s, manual = true };
            }
            ServerQuery.Collect(servers);
        }

        void DrawServerList(float W, float H)
        {
            GUI.DrawTexture(new Rect(0, 0, W, H), texShade);
            var r = new Rect(W / 2f - 560, 120, 1120, 820);
            GUI.Box(r, GUIContent.none, panel);
            GUILayout.BeginArea(new Rect(r.x + 24, r.y + 18, r.width - 48, r.height - 36));
            GUILayout.Label("SERVERLISTE", head);
            GUILayout.Label($"Server im Heimnetz erscheinen von selbst. Fuer Server ueber Tailscale/Internet die IP unten eintragen.   Deine Version: {GameVersion.Current}   ·   Dein Panzer: {Tanks[tankIndex]().name}", small);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Name</b>", text, GUILayout.Width(330));
            GUILayout.Label("<b>Karte</b>", text, GUILayout.Width(160));
            GUILayout.Label("<b>Spieler</b>", text, GUILayout.Width(80));
            GUILayout.Label("<b>Ping</b>", text, GUILayout.Width(80));
            GUILayout.Label("<b>Version</b>", text, GUILayout.Width(190));
            GUILayout.EndHorizontal();
            listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Height(440));
            var entries = new List<ServerQuery.Entry>(servers.Values);
            entries.Sort((a, b) => (b.info != null).CompareTo(a.info != null));
            string remove = null;
            foreach (var e in entries)
            {
                bool online = e.info != null && Time.unscaledTime - e.lastSeen < 7f;
                bool sameVer = online && (e.info.version == GameVersion.Current || GameVersion.IsDev);
                GUILayout.BeginHorizontal();
                string name = online ? e.info.name : e.address;
                GUILayout.Label($"{name}\n<color=#999>{e.address}{(online ? ":" + e.info.port : "")}</color>", text, GUILayout.Width(330), GUILayout.Height(46));
                GUILayout.Label(online ? e.info.map : "<color=#f77>keine Antwort</color>", text, GUILayout.Width(160));
                GUILayout.Label(online ? $"{e.info.players}/{e.info.max}" : "-", text, GUILayout.Width(80));
                GUILayout.Label(online ? $"{e.pingMs:0} ms" : "-", text, GUILayout.Width(80));
                GUILayout.Label(online ? (sameVer ? e.info.version : $"<color=#f77>{e.info.version}</color>") : "-", text, GUILayout.Width(190));
                GUI.enabled = online && sameVer && e.info.players < e.info.max;
                if (GUILayout.Button("Beitreten", btnSel, GUILayout.Width(130), GUILayout.Height(40)))
                {
                    ipText = e.address; portText = e.info.port.ToString();
                    listOpen = false;
                    StartJoin();
                }
                GUI.enabled = true;
                if (e.manual && savedServers.Contains(e.address) && GUILayout.Button("X", btn, GUILayout.Width(40), GUILayout.Height(40))) remove = e.address;
                GUILayout.EndHorizontal();
            }
            if (entries.Count == 0) GUILayout.Label("Suche ... (noch kein Server gefunden)", text);
            GUILayout.EndScrollView();
            if (remove != null) { savedServers.Remove(remove); servers.Remove(remove); SaveServerList(); }

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Beitrittscode:", text, GUILayout.Width(130));
            codeText = GUILayout.TextField(codeText, 12, GUILayout.Width(160), GUILayout.Height(34)).ToUpperInvariant();
            if (GUILayout.Button("Mit Code beitreten", btnSel, GUILayout.Width(220), GUILayout.Height(38)) && codeText.Trim().Length > 0)
                StartJoinCode(codeText);
            GUILayout.Label("<color=#999>(Code bekommt, wer \"Selbst hosten\" klickt)</color>", small);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("IP / Name:", text, GUILayout.Width(90));
            addText = GUILayout.TextField(addText, 64, GUILayout.Width(300), GUILayout.Height(34));
            if (GUILayout.Button("Zur Liste hinzufuegen", btn, GUILayout.Width(220), GUILayout.Height(38)) && addText.Trim().Length > 0)
            {
                if (!savedServers.Contains(addText.Trim())) { savedServers.Add(addText.Trim()); SaveServerList(); }
                probeTimer = 0f;
            }
            if (GUILayout.Button("Direkt verbinden", btn, GUILayout.Width(200), GUILayout.Height(38)) && addText.Trim().Length > 0)
            {
                ipText = addText.Trim(); portText = NetSync.DefaultPort.ToString();
                listOpen = false;
                StartJoin();
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Schliessen", btn, GUILayout.Width(160), GUILayout.Height(38))) { listOpen = false; ServerQuery.StopSearch(); }
            GUILayout.EndHorizontal();
            GUILayout.Label("Kein Server sichtbar? Auf dem Server-PC muessen UDP-Port 7777 (Spiel) und 7778 (Serverliste) offen sein.", small);
            GUILayout.EndArea();
        }

        void DrawPause(float W, float H)
        {
            GUI.DrawTexture(new Rect(0, 0, W, H), texShade);
            var r = new Rect(W / 2f - 230, H / 2f - 220, 460, 440);
            GUI.Box(r, GUIContent.none, panel);
            GUILayout.BeginArea(new Rect(r.x + 24, r.y + 20, r.width - 48, r.height - 40));
            GUILayout.Label("PAUSE", head);
            GUILayout.Label($"{player?.spec.name}  ·  {map?.Name}", text);
            GUILayout.Space(16);
            if (GUILayout.Button("Weiter", btn, GUILayout.Height(52))) Pause(false);
            GUILayout.Space(6);
            if (netGame)
            {
                GUI.enabled = localNet != null && (player == null || player.destroyed);
                if (GUILayout.Button("Neuer Panzer (N, wenn zerstoert)", btn, GUILayout.Height(52))) { Pause(false); localNet.RequestRespawn(); }
                GUI.enabled = true;
                GUILayout.Space(6);
                if (GUILayout.Button("Spiel verlassen", btn, GUILayout.Height(52))) ShowMenu();
            }
            else
            {
                if (GUILayout.Button("Neu starten (N)", btn, GUILayout.Height(52))) { Pause(false); SpawnPlayer(); SpawnTargets(); RangeLog.Lines.Clear(); }
                GUILayout.Space(6);
                if (GUILayout.Button("Hauptmenue (Panzer/Karte wechseln)", btn, GUILayout.Height(52))) ShowMenu();
            }
            GUILayout.Space(6);
            if (GUILayout.Button("Einstellungen", btn, GUILayout.Height(52))) settingsOpen = true;
            GUILayout.Space(6);
            if (GUILayout.Button("Beenden", btn, GUILayout.Height(52))) Quit();
            GUILayout.EndArea();
        }

        // ================= Einstellungen =================
        bool settingsOpen;
        int settingsTab;
        Vector2 settingsScroll;
        GUIStyle rowLabel, valueLabel, tabBtn, tabSel, note;

        void CloseSettings()
        {
            settingsOpen = false;
            GameSettings.Save();
        }

        void DrawSettings(float W, float H)
        {
            if (rowLabel == null)
            {
                rowLabel = new GUIStyle(text) { fontSize = 17, alignment = TextAnchor.MiddleLeft, wordWrap = false };
                valueLabel = new GUIStyle(rowLabel) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                note = new GUIStyle(small) { wordWrap = true };
                tabBtn = new GUIStyle(btn) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
                tabSel = new GUIStyle(btnSel) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            }
            GUI.DrawTexture(new Rect(0, 0, W, H), texShade);
            var r = new Rect(W / 2f - 480, 70, 960, 940);
            GUI.Box(r, GUIContent.none, panel);
            GUILayout.BeginArea(new Rect(r.x + 28, r.y + 22, r.width - 56, r.height - 44));
            GUILayout.Label("EINSTELLUNGEN", head);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Grafik", settingsTab == 0 ? tabSel : tabBtn, GUILayout.Height(44))) settingsTab = 0;
            if (GUILayout.Button("Anzeige", settingsTab == 1 ? tabSel : tabBtn, GUILayout.Height(44))) settingsTab = 1;
            if (GUILayout.Button("Steuerung", settingsTab == 2 ? tabSel : tabBtn, GUILayout.Height(44))) settingsTab = 2;
            GUILayout.EndHorizontal();
            GUILayout.Space(14);

            settingsScroll = GUILayout.BeginScrollView(settingsScroll, GUILayout.Height(690));
            bool changed = false, custom = false;
            if (settingsTab == 0)
            {
                int p = GameSettings.Preset;
                if (Selector("Qualitaetsstufe", ref p, GameSettings.PresetNames, 0, 3)) { GameSettings.ApplyPreset(p); changed = true; }
                GUILayout.Label("Grafikkarte: " + SystemInfo.graphicsDeviceName + (GameSettings.IsWeakGpu() ? "  (im Prozessor eingebaut - \"Niedrig\" oder \"Mittel\" empfohlen)" : ""), note);
                GUILayout.Space(10);
                float sc = GameSettings.RenderScale * 100f;
                if (Slider("Render-Aufloesung", ref sc, 50f, 200f, $"{sc:0} %", 5f)) { GameSettings.RenderScale = sc / 100f; custom = true; }
                GUILayout.Label("Ueber 100 % wird intern hoeher gerechnet und verkleinert (schaerfer, kostet viel Grafikleistung). Unter 100 % schneller.", note);
                if (GameSettings.RenderScale < 0.99f && Toggle("Hochskalieren mit FSR", ref GameSettings.Fsr)) custom = true;
                if (Selector("Kantenglaettung", ref GameSettings.AntiAliasing, GameSettings.AaNames)) custom = true;
                if (Selector("Schatten", ref GameSettings.Shadows, GameSettings.ShadowNames)) custom = true;

                GUILayout.Space(10);
                GUILayout.Label("WEIZEN UND SICHTWEITE", head);
                float d = GameSettings.WheatDensity * 100f;
                if (Slider("Weizen-Dichte", ref d, 25f, 100f, $"{d:0} %", 5f)) { GameSettings.WheatDensity = d / 100f; custom = true; }
                if (Slider("3D-Halme bis", ref GameSettings.WheatDistance, 40f, 105f, $"{GameSettings.WheatDistance:0} m", 5f)) custom = true;
                if (Slider("Weizen in der Ferne bis", ref GameSettings.ViewDistance, 300f, 1000f, $"{GameSettings.ViewDistance:0} m", 50f)) custom = true;
                if (Slider("Weizen-Schatten bis", ref GameSettings.WheatShadows, 0f, 25f, GameSettings.WheatShadows < 1f ? "aus" : $"{GameSettings.WheatShadows:0} m", 1f)) custom = true;
            }
            else if (settingsTab == 1)
            {
                var res = GameSettings.Resolutions();
                var names = new string[res.Count + 1];
                names[0] = $"Bildschirm ({Screen.currentResolution.width} x {Screen.currentResolution.height})";
                int idx = 0;
                for (int i = 0; i < res.Count; i++)
                {
                    names[i + 1] = $"{res[i].x} x {res[i].y}";
                    if (res[i].x == GameSettings.ResW && res[i].y == GameSettings.ResH) idx = i + 1;
                }
                if (Selector("Aufloesung", ref idx, names))
                {
                    GameSettings.ResW = idx == 0 ? 0 : res[idx - 1].x;
                    GameSettings.ResH = idx == 0 ? 0 : res[idx - 1].y;
                    GameSettings.Apply(true);
                }
                if (Selector("Fenstermodus", ref GameSettings.WindowMode, GameSettings.WindowNames)) GameSettings.Apply(true);
                if (Application.isEditor)
                    GUILayout.Label("Hinweis: Aufloesung und Fenstermodus wirken erst im fertigen Spiel. Im Editor bestimmt das Game-Fenster die Aufloesung.", note);
                GUILayout.Space(10);
                if (Toggle("VSync (kein Bildreissen)", ref GameSettings.VSync)) changed = true;
                if (!GameSettings.VSync)
                {
                    int fi = System.Array.IndexOf(GameSettings.FpsSteps, GameSettings.FpsLimit);
                    if (fi < 0) fi = 0;
                    var fn = new string[GameSettings.FpsSteps.Length];
                    for (int i = 0; i < fn.Length; i++) fn[i] = GameSettings.FpsSteps[i] == 0 ? "unbegrenzt" : GameSettings.FpsSteps[i] + " FPS";
                    if (Selector("Bildrate begrenzen", ref fi, fn)) { GameSettings.FpsLimit = GameSettings.FpsSteps[fi]; changed = true; }
                }
                GUILayout.Space(10);
                float ui = GameSettings.UiScale * 100f;
                if (Slider("UI-Groesse im Spiel", ref ui, 75f, 150f, $"{ui:0} %", 5f)) GameSettings.UiScale = ui / 100f;
                GUILayout.Label("Anzeigen im Spiel (Panzer-Info, Hilfe, F3) wachsen automatisch mit der Aufloesung mit; hier zusaetzlich groesser oder kleiner stellen.", note);
                GUILayout.Space(10);
                bool dev = DevMetrics.Visible;
                if (Toggle("Entwickler-Anzeige (F3)", ref dev)) DevMetrics.Visible = dev;
            }
            else
            {
                if (Slider("Sichtfeld (3. Person)", ref GameSettings.Fov, 50f, 90f, $"{GameSettings.Fov:0}°", 1f)) changed = true;
                GUILayout.Label("Das Visier hat immer das echte Sichtfeld des jeweiligen Zielfernrohrs.", note);
                if (Slider("Mausempfindlichkeit", ref GameSettings.MouseSens, 0.2f, 3f, $"{GameSettings.MouseSens:0.00}", 0.05f)) changed = true;
                if (Toggle("Maus Y umkehren", ref GameSettings.InvertY)) changed = true;
            }
            GUILayout.EndScrollView();
            if (custom) { GameSettings.Preset = 4; changed = true; }
            if (changed) GameSettings.Apply(false);

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Standard (Hoch)", btn, GUILayout.Height(50), GUILayout.Width(260)))
            {
                GameSettings.ApplyPreset(2); GameSettings.Fov = 60f; GameSettings.MouseSens = 1f; GameSettings.InvertY = false; GameSettings.UiScale = 1f;
                GameSettings.Apply(false);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Speichern & zurueck", big, GUILayout.Height(50), GUILayout.Width(320))) CloseSettings();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        bool Selector(string label, ref int value, string[] names, int min = 0, int max = -1)
        {
            if (max < 0) max = names.Length - 1;
            int old = value;
            GUILayout.BeginHorizontal(GUILayout.Height(44));
            GUILayout.Label(label, rowLabel, GUILayout.Width(300), GUILayout.Height(40));
            if (GUILayout.Button("<", btn, GUILayout.Width(48), GUILayout.Height(40))) value = value <= min ? max : value - 1;
            GUILayout.Label(names[Mathf.Clamp(value, 0, names.Length - 1)], valueLabel, GUILayout.Height(40), GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", btn, GUILayout.Width(48), GUILayout.Height(40))) value = value >= max ? min : value + 1;
            GUILayout.EndHorizontal();
            return value != old;
        }

        bool Slider(string label, ref float value, float min, float max, string shown, float step)
        {
            float old = value;
            GUILayout.BeginHorizontal(GUILayout.Height(40));
            GUILayout.Label(label, rowLabel, GUILayout.Width(300), GUILayout.Height(36));
            GUILayout.BeginVertical();
            GUILayout.Space(14);
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            GUILayout.EndVertical();
            GUILayout.Label(shown, valueLabel, GUILayout.Width(130), GUILayout.Height(36));
            GUILayout.EndHorizontal();
            value = Mathf.Clamp(Mathf.Round(v / step) * step, min, max);
            return Mathf.Abs(value - old) > 1e-4f;
        }

        bool Toggle(string label, ref bool value)
        {
            bool old = value;
            GUILayout.BeginHorizontal(GUILayout.Height(44));
            GUILayout.Label(label, rowLabel, GUILayout.Width(300), GUILayout.Height(40));
            if (GUILayout.Button(value ? "An" : "Aus", value ? btnSel : btn, GUILayout.Width(120), GUILayout.Height(40))) value = !value;
            GUILayout.EndHorizontal();
            return value != old;
        }

        static string AmmoList(TankSpec s)
        {
            int[] list = s.ammo != null && s.ammo.Length > 0 ? s.ammo : new[] { s.ammoIndex };
            var parts = new string[list.Length];
            for (int i = 0; i < list.Length; i++)
                parts[i] = $"[{i + 1}] {AmmoLibrary.All[list[i]].name} ({AmmoLibrary.All[list[i]].type})";
            return string.Join("  ·  ", parts);
        }

        static string TankInfo(TankSpec s)
        {
            var a = AmmoLibrary.All[s.ammoIndex];
            Ballistics.SimulateFlat(a, 100f, out float v100, out _, out _);
            Ballistics.SimulateFlat(a, 1000f, out float v1000, out _, out _);
            string Arm(Armor x) => $"{x.mm:0} mm" + (x.angle > 1f ? $" @ {x.angle:0}°" : "");
            return
                $"<b>{s.massT:0} t</b>  ·  {s.powerPS:0} PS  ·  {s.maxSpeedKmh:0} km/h\n" +
                $"Turm: {s.traverseDegS:0}°/s  ·  Laden: {s.reloadS:0.0} s\n\n" +
                $"<b>Kanone:</b> {a.gun}\n" +
                $"Munition: {AmmoList(s)}\n" +
                $"Durchschlag: <b>{Penetration.FlatPenetration(a, v100):0} mm</b> (100 m)  ·  {Penetration.FlatPenetration(a, v1000):0} mm (1 km)\n\n" +
                $"<b>Panzerung Wanne</b>\nFront {Arm(s.hullFront)}  ·  Seite {Arm(s.hullSide)}  ·  Heck {Arm(s.hullRear)}\n" +
                $"<b>Panzerung Turm</b>\nFront {Arm(s.turretFront)}  ·  Seite {Arm(s.turretSide)}  ·  Blende {Arm(s.mantlet)}";
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
