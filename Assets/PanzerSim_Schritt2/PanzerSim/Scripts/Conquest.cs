using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Eroberung (Einzel- und Mehrspieler): drei Flaggenpunkte A/B/C zwischen den Startplaetzen der Teams.
    /// Stehen nur Panzer eines Teams im Kreis (45 m), kippt die Flagge zu diesem Team (mehr Panzer = schneller).
    /// Jeder ausgeschaltete Panzer kostet sein Team ein Ticket; wer mehr Flaggen haelt, laesst die Tickets des
    /// anderen Teams zusaetzlich ablaufen. Bei 0 Tickets hat das Team verloren.
    /// Fehlende Spieler fuellen KI-Panzer auf (je Team 6 Panzer). Im Mehrspieler rechnet der Server alles und
    /// schickt Tickets/Flaggen ueber NetSession; die Clients bauen die Flaggen aus der Karte selbst (gleiche Lage).
    /// </summary>
    public static class Conquest
    {
        public const int TeamSize = 6;            // Panzer je Team (Spieler + KI)
        public const int StartTickets = 100;
        public const float Radius = 45f;          // Flaggenkreis
        const float CaptureTime = 20f;            // ein Panzer, neutral -> eigen
        const float BleedInterval = 5f;           // je Flagge Vorsprung: 1 Ticket alle 5 s
        const float AiRespawn = 12f, WreckLife = 40f;
        public static readonly string[] FlagNames = { "A", "B", "C" };

        public class Flag
        {
            public string name; public Vector3 pos;
            public int owner = -1;                // -1 neutral, 0 Blau, 1 Rot
            public float progress;                // -1 = ganz Blau ... +1 = ganz Rot
            public int n0, n1;                    // Panzer im Kreis (nur Server/Einzelspieler)
            public Renderer cloth; public LineRenderer ring;
        }

        public static bool On { get; private set; }
        public static readonly Flag[] Flags = new Flag[3];
        public static readonly int[] Tickets = { StartTickets, StartTickets };
        public static int Winner { get; private set; } = -1;
        public static bool Simulate { get; private set; }   // Einzelspieler oder Server: wir rechnen
        static bool networked;
        static GameMap map;
        static System.Func<TankController> getPlayer;
        static readonly Vector2[] basePos = new Vector2[2];
        static readonly float[] baseYaw = new float[2];
        static Transform root;
        static Runner runner;
        static float scanT, syncT, endT;
        static readonly float[] bleedAcc = new float[2];
        static readonly HashSet<TankController> counted = new HashSet<TankController>();
        static readonly List<TankController> known = new List<TankController>();

        class Slot { public int team; public TankController tc; public NetTank nt; public float respawnT; }
        static readonly List<Slot> slots = new List<Slot>();
        static readonly List<(TankController tc, NetTank nt, float t)> wrecks = new List<(TankController, NetTank, float)>();
        static readonly Dictionary<TankAI, (int flag, float until, Vector3 off)> choice = new Dictionary<TankAI, (int, float, Vector3)>();

        // ================= Start / Ende =================
        /// <summary>Einzelspieler: Flaggen, KI-Panzer beider Teams.</summary>
        public static void BeginOffline(GameMap m, System.Func<TankController> player)
        {
            Stop();
            Setup(m); getPlayer = player;
            Simulate = true; networked = false;
            var pl = player();
            int myTeam = GameSettings.Team;
            for (int t = 0; t < 2; t++)
                for (int i = 0; i < TeamSize - (t == myTeam ? 1 : 0); i++) slots.Add(new Slot { team = t, respawnT = i * 0.3f });
            RangeLog.AddLocal("EROBERUNG: Nimm die Flaggen A, B und C ein. Wer mehr Flaggen haelt, laesst die Tickets des Gegners ablaufen.");
        }

        /// <summary>Server/Host: rechnet Flaggen und Tickets, fuellt die Teams mit KI auf.</summary>
        public static void BeginServer(GameMap m)
        {
            Stop();
            Setup(m); getPlayer = () => GameManager.Instance != null ? GameManager.Instance.Player : null;
            Simulate = true; networked = true;
        }

        /// <summary>Client: zeigt nur an, was der Server schickt.</summary>
        public static void BeginClient(GameMap m)
        {
            Stop();
            Setup(m, true); getPlayer = () => GameManager.Instance != null ? GameManager.Instance.Player : null;
            Simulate = false; networked = true;
        }

        public static void Stop()
        {
            if (!On && root == null) return;
            foreach (var s in slots) RemoveTank(s.tc, s.nt);
            foreach (var w in wrecks) RemoveTank(w.tc, w.nt);
            slots.Clear(); wrecks.Clear(); counted.Clear(); choice.Clear(); known.Clear();
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            On = false; Winner = -1;
        }

        /// <summary>N nach Rundenende (Einzelspieler).</summary>
        public static void RestartOffline() => BeginOffline(map, getPlayer);

        static void RemoveTank(TankController tc, NetTank nt)
        {
            if (nt != null && nt.IsSpawned && NetSync.IsServer) nt.NetworkObject.Despawn(true);
            else if (tc != null) Object.Destroy(tc.gameObject);
        }

        // ================= Lage der Flaggen =================
        static void Setup(GameMap m, bool client = false)
        {
            map = m;
            On = true; Winner = -1;
            Tickets[0] = Tickets[1] = StartTickets;
            bleedAcc[0] = bleedAcc[1] = 0f; endT = 0f;
            var gm = GameManager.Instance;
            gm.TeamStart(0, out Vector2 p0, out float y0);
            gm.TeamStart(1, out Vector2 p1, out float y1);
            float lim = m.Extent - 120f;
            Vector2 d = p1 - p0;
            if (d.magnitude < 500f)
            {
                // Karte ohne zweiten Startplatz (Testgelaende): Gegenseite 1,4 km voraus
                Vector2 f = new Vector2(Mathf.Sin(y0 * Mathf.Deg2Rad), Mathf.Cos(y0 * Mathf.Deg2Rad));
                p1 = Clamp(p0 + f * 1400f, lim);
                if ((p1 - p0).magnitude < 500f) p1 = Clamp(-p0 - f * 600f, lim);
                d = p1 - p0;
            }
            if (d.magnitude > 2200f)
            {
                // grosse Karten: Kampfgebiet 2,2 km um die Mitte
                Vector2 mid = (p0 + p1) * 0.5f, dir = d.normalized;
                p0 = mid - dir * 1100f; p1 = mid + dir * 1100f;
            }
            p0 = Clamp(p0, lim); p1 = Clamp(p1, lim);
            d = p1 - p0;
            float L = d.magnitude;
            Vector2 fw = d / Mathf.Max(L, 1f), side = new Vector2(fw.y, -fw.x);
            basePos[0] = p0; basePos[1] = p1;
            baseYaw[0] = Mathf.Atan2(fw.x, fw.y) * Mathf.Rad2Deg; baseYaw[1] = baseYaw[0] + 180f;

            // feste Zufallszahl aus dem Kartennamen: alle Rechner im Netz finden dieselben Plaetze
            int seed = 17; foreach (char ch in m.Name) seed = seed * 31 + ch;   // eigener Hash: gleich auf Windows und Linux
            var rnd = new System.Random(seed & 0x7fffffff);
            var ns = client ? NetSession.Instance : null;
            Vector3[] given = ns != null ? new[] { ns.cqFlagA.Value, ns.cqFlagB.Value, ns.cqFlagC.Value } : null;
            float[] along = { 0.22f, 0.5f, 0.78f }, across = { 0.16f, 0f, -0.16f };
            root = new GameObject("Eroberung").transform;
            for (int i = 0; i < 3; i++)
            {
                Vector2 c = p0 + fw * (L * along[i]) + side * (L * across[i]);
                Vector2 best = FreeSpot(c, rnd, lim);
                var f = new Flag { name = FlagNames[i], pos = new Vector3(best.x, m.GroundY(best.x, best.y), best.y) };
                if (given != null && given[i] != Vector3.zero) f.pos = given[i];   // Client: Lage vom Server
                Flags[i] = f;
                if (!NetSync.Headless) BuildFlag(f);
            }
            if (runner == null) { var go = new GameObject("Eroberung-Ablauf"); Object.DontDestroyOnLoad(go); runner = go.AddComponent<Runner>(); }
        }

        static Vector2 Clamp(Vector2 p, float lim) => new Vector2(Mathf.Clamp(p.x, -lim, lim), Mathf.Clamp(p.y, -lim, lim));

        /// <summary>Freier, ebener Platz nahe c (kein Haus, kein Wald, befahrbar).</summary>
        static Vector2 FreeSpot(Vector2 c, System.Random rnd, float lim)
        {
            Vector2 best = c; float bestScore = float.MaxValue;
            for (int k = 0; k < 40; k++)
            {
                float r = k == 0 ? 0f : 15f + (float)rnd.NextDouble() * 120f, a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                Vector2 q = Clamp(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, lim);
                if (!map.CanSpawn(q.x, q.y)) continue;
                float y = map.GroundY(q.x, q.y);
                float slope = Mathf.Abs(map.GroundY(q.x + 10f, q.y) - map.GroundY(q.x - 10f, q.y)) + Mathf.Abs(map.GroundY(q.x, q.y + 10f) - map.GroundY(q.x, q.y - 10f));
                bool blocked = Physics.CheckBox(new Vector3(q.x, y + 4f, q.y), new Vector3(9f, 3f, 9f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                float score = r + slope * 25f + (blocked ? 1000f : 0f);
                if (score < bestScore) { bestScore = score; best = q; }
                if (!blocked && slope < 2f && k > 0 && r < 40f) break;
            }
            return best;
        }

        static readonly Color Blue = new Color(0.25f, 0.5f, 1f), Red = new Color(0.95f, 0.25f, 0.2f), Neutral = new Color(0.9f, 0.9f, 0.88f);
        static Color TeamColor(int owner) => owner == 0 ? Blue : owner == 1 ? Red : Neutral;

        static void BuildFlag(Flag f)
        {
            var g = new GameObject("Flagge " + f.name).transform;
            g.SetParent(root, false);
            g.position = f.pos;
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(pole.GetComponent<Collider>());
            pole.transform.SetParent(g, false);
            pole.transform.localPosition = new Vector3(0f, 6f, 0f);
            pole.transform.localScale = new Vector3(0.22f, 6f, 0.22f);
            pole.GetComponent<Renderer>().sharedMaterial = GameMap.Mat(new Color(0.3f, 0.3f, 0.3f));
            var cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(cloth.GetComponent<Collider>());
            cloth.transform.SetParent(g, false);
            cloth.transform.localPosition = new Vector3(1.75f, 10.6f, 0f);
            cloth.transform.localScale = new Vector3(3.3f, 2.1f, 0.06f);
            f.cloth = cloth.GetComponent<Renderer>();
            f.cloth.sharedMaterial = GameMap.Mat(Neutral);
            // Kreis am Boden
            var lr = g.gameObject.AddComponent<LineRenderer>();
            const int N = 72;
            lr.positionCount = N; lr.loop = true; lr.useWorldSpace = true;
            lr.widthMultiplier = 0.9f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                float x = f.pos.x + Mathf.Cos(a) * Radius, z = f.pos.z + Mathf.Sin(a) * Radius;
                lr.SetPosition(i, new Vector3(x, map.GroundY(x, z) + 0.35f, z));
            }
            lr.sharedMaterial = GameMap.Mat(Neutral);
            f.ring = lr;
        }

        // ================= Startplaetze =================
        /// <summary>Startplatz Nummer slot fuer ein Team (nebeneinander, Front zum Gegner).</summary>
        public static void SpawnPoint(int team, int slot, out Vector3 pos, out float yaw)
        {
            team = Mathf.Clamp(team, 0, 1);
            Vector2 p = basePos[team];
            yaw = baseYaw[team];
            Vector2 fw = new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad)), side = new Vector2(fw.y, -fw.x);
            int s = slot % 10;
            float off = (s % 2 == 0 ? 1 : -1) * ((s + 1) / 2) * 16f;
            Vector2 q = p + side * off - fw * (slot / 10 * 18f);
            // besetzt (Wrack, Haus)? etwas weiter hinten versuchen
            for (int k = 0; k < 6; k++)
            {
                float y0 = map.GroundY(q.x, q.y);
                if (!Physics.CheckBox(new Vector3(q.x, y0 + 2.6f, q.y), new Vector3(2.4f, 1.6f, 4.2f), Quaternion.Euler(0f, yaw, 0f), ~0, QueryTriggerInteraction.Ignore)) break;
                q -= fw * 12f;
            }
            pos = new Vector3(q.x, map.GroundY(q.x, q.y) + 0.5f, q.y);
        }

        // ================= Ablauf =================
        static void Tick(float dt)
        {
            if (!On || map == null) return;
            if (!Simulate) { ReadNet(); UpdateVisuals(); return; }

            scanT -= dt;
            if (scanT <= 0f) { scanT = 0.25f; Scan(0.25f); }
            if (Winner < 0)
            {
                // Tickets ablaufen lassen: wer mehr Flaggen haelt
                int f0 = 0, f1 = 0;
                foreach (var f in Flags) { if (f.owner == 0) f0++; else if (f.owner == 1) f1++; }
                if (f0 != f1)
                {
                    int loser = f0 > f1 ? 1 : 0;
                    bleedAcc[loser] += dt * Mathf.Abs(f0 - f1) / BleedInterval;
                    while (bleedAcc[loser] >= 1f) { bleedAcc[loser] -= 1f; Tickets[loser] = Mathf.Max(0, Tickets[loser] - 1); }
                }
                for (int t = 0; t < 2; t++)
                    if (Tickets[t] <= 0 && Winner < 0) EndRound(1 - t);
                UpdateSlots(dt);
            }
            else if (networked)
            {
                endT += dt;
                if (endT > 20f) ServerNewRound();
            }
            UpdateWrecks(dt);
            UpdateVisuals();
            if (networked) WriteNet(dt);
        }

        static void EndRound(int winner)
        {
            Winner = winner;
            endT = 0f;
            int my = TankController.LocalTeam;
            string msg = NetSync.Headless ? "" : winner == my ? "SIEG!" : "NIEDERLAGE";
            if (!NetSync.Headless) TankCamera.Notice($"{msg}  -  {GameSettings.TeamNames[winner]} gewinnt die Eroberung" + (networked ? "  (neue Runde in 20 s)" : "  -  N: neue Runde"), true);
            RangeLog.Add($"*** EROBERUNG: {GameSettings.TeamNames[winner]} gewinnt ({Tickets[0]} : {Tickets[1]} Tickets) ***");
            foreach (var s in slots) if (s.tc != null && s.tc.ai != null) s.tc.ai.objective = null;
        }

        /// <summary>Flaggen einnehmen, Verluste zaehlen.</summary>
        static void Scan(float dt)
        {
            known.Clear();
            known.AddRange(Object.FindObjectsByType<TankController>(FindObjectsSortMode.None));
            foreach (var f in Flags) { f.n0 = 0; f.n1 = 0; }
            foreach (var t in known)
            {
                if (t == null || t.team > 1 || t.team < 0) continue;
                if (t.destroyed)
                {
                    if (Winner < 0 && counted.Add(t)) Tickets[t.team] = Mathf.Max(0, Tickets[t.team] - 1);
                    continue;
                }
                Vector3 p = t.transform.position;
                foreach (var f in Flags)
                {
                    float dx = p.x - f.pos.x, dz = p.z - f.pos.z;
                    if (dx * dx + dz * dz < Radius * Radius && Mathf.Abs(p.y - f.pos.y) < 25f) { if (t.team == 0) f.n0++; else f.n1++; }
                }
            }
            if (Winner >= 0) return;
            foreach (var f in Flags)
            {
                float rate = 0f;
                if (f.n0 > 0 && f.n1 == 0) rate = -Mathf.Min(f.n0, 3) / CaptureTime;
                else if (f.n1 > 0 && f.n0 == 0) rate = Mathf.Min(f.n1, 3) / CaptureTime;
                else if (f.n0 == 0 && f.n1 == 0 && f.owner >= 0) rate = (f.owner == 0 ? -1f : 1f) / (CaptureTime * 3f);   // leer: langsam zurueck zum Besitzer
                float old = f.progress;
                f.progress = Mathf.Clamp(f.progress + rate * dt, -1f, 1f);
                int was = f.owner;
                if (f.progress <= -1f) f.owner = 0;
                else if (f.progress >= 1f) f.owner = 1;
                else if (f.owner == 0 && f.progress > 0f) f.owner = -1;
                else if (f.owner == 1 && f.progress < 0f) f.owner = -1;
                if (f.owner != was)
                {
                    if (f.owner >= 0) RangeLog.Add($"Flagge {f.name} eingenommen von {GameSettings.TeamNames[f.owner]}");
                    else RangeLog.Add($"Flagge {f.name} neutralisiert");
                    if (!NetSync.Headless && f.owner >= 0) TankCamera.Notice($"FLAGGE {f.name} {(f.owner == TankController.LocalTeam ? "EINGENOMMEN" : "VERLOREN")}", f.owner == TankController.LocalTeam);
                }
                _ = old;
            }
        }

        // ---------------- KI-Panzer ----------------
        static void UpdateSlots(float dt)
        {
            if (networked)
            {
                // so viele KI-Panzer, wie Spieler fehlen
                int[] humans = new int[2];
                foreach (var nt in Object.FindObjectsByType<NetTank>(FindObjectsSortMode.None))
                    if (nt.IsSpawned && !nt.isTarget.Value && !nt.isAi.Value && nt.team.Value >= 0 && nt.team.Value <= 1) humans[nt.team.Value]++;
                for (int t = 0; t < 2; t++)
                {
                    int want = Mathf.Max(0, TeamSize - humans[t]), have = 0;
                    foreach (var s in slots) if (s.team == t) have++;
                    for (; have < want; have++) slots.Add(new Slot { team = t, respawnT = 2f });
                    for (int i = slots.Count - 1; i >= 0 && have > want; i--)
                        if (slots[i].team == t) { RemoveTank(slots[i].tc, slots[i].nt); slots.RemoveAt(i); have--; }
                }
            }
            int slotNo = 0;
            foreach (var s in slots)
            {
                slotNo++;
                if (s.tc != null && !s.tc.destroyed) continue;
                if (s.tc != null && s.tc.destroyed)
                {
                    wrecks.Add((s.tc, s.nt, 0f));
                    s.tc = null; s.nt = null; s.respawnT = AiRespawn;
                }
                s.respawnT -= dt;
                if (s.respawnT > 0f || Tickets[s.team] <= 0) continue;
                SpawnAi(s, slotNo);
            }
        }

        // KI-Fahrzeuge nach Zeit: Weltkrieg oder modern, je nachdem, was der Spieler faehrt
        static readonly System.Func<TankSpec>[] Ww2 =
        {
            () => TankLibrary.PanzerIV, () => TankLibrary.Sherman, () => TankLibrary.T34_85_44, () => TankLibrary.Panther, () => TankLibrary.Firefly,
            () => TankLibrary.Tiger, () => TankLibrary.KV1, () => TankLibrary.IS2, () => TankLibrary.T34, () => TankLibrary.PanzerIII,
        };
        static readonly System.Func<TankSpec>[] Modern =
            { () => TankLibrary.T55A, () => TankLibrary.T10M, () => TankLibrary.Leopard2A6, () => TankLibrary.Challenger2, () => TankLibrary.T54 };

        static void SpawnAi(Slot s, int slotNo)
        {
            var pl = getPlayer != null ? getPlayer() : null;
            bool modern = pl != null && pl.spec.fireControl;
            var list = modern ? Modern : Ww2;
            var spec = list[Random.Range(0, list.Length)]();
            SpawnPoint(s.team, slotNo + 1, out Vector3 pos, out float yaw);
            yaw += Random.Range(-10f, 10f);
            string name = $"{spec.name} (KI)";
            if (networked)
            {
                var nt = NetSync.SpawnAiTank(GameManager.IndexOfSpec(spec), s.team, pos, yaw, name);
                if (nt == null || nt.tc == null) { s.respawnT = 3f; return; }
                s.nt = nt; s.tc = nt.tc;
            }
            else
            {
                var tc = TankBuilder.Build(spec, pos, Quaternion.Euler(0f, yaw, 0f), false);
                tc.team = s.team; tc.playerName = name;
                s.tc = tc;
            }
            var ai = TankAI.Attach(s.tc, Random.Range(0.4f, 0.65f), map.Extent);
            ai.objective = Objective;
            ai.holdRadius = Random.Range(8f, 22f);
            ai.searchRange = 1600f;
        }

        static void UpdateWrecks(float dt)
        {
            for (int i = wrecks.Count - 1; i >= 0; i--)
            {
                var w = wrecks[i];
                w.t += dt;
                wrecks[i] = w;
                if (w.tc == null) { wrecks.RemoveAt(i); continue; }
                if (w.t > WreckLife) { RemoveTank(w.tc, w.nt); wrecks.RemoveAt(i); }
            }
        }

        /// <summary>Auftrag fuer einen KI-Panzer: naechste Flagge, die dem Team noch nicht gehoert (mit etwas Zufall).</summary>
        static Vector3? Objective(TankAI ai)
        {
            if (!On || Winner >= 0 || ai == null || ai.tc == null) return null;
            int team = ai.tc.team;
            bool need = !choice.TryGetValue(ai, out var c) || Time.time > c.until;
            if (!need)
            {
                var f = Flags[c.flag];
                // Flagge sicher eigen und anderswo gibt es noch etwas zu tun: weiter
                if (f.owner == team && Mathf.Abs(f.progress) >= 0.999f && AnyOpen(team) && Random.value < 0.004f) need = true;
            }
            if (need)
            {
                Vector3 me = ai.tc.transform.position;
                int best = 0; float bs = float.MaxValue;
                for (int i = 0; i < 3; i++)
                {
                    var f = Flags[i];
                    float score = Vector3.Distance(me, f.pos) + Random.Range(0f, 450f);
                    bool mine = f.owner == team && Mathf.Abs(f.progress) >= 0.999f;
                    if (mine) score += 1100f;
                    if (score < bs) { bs = score; best = i; }
                }
                Vector2 o = Random.insideUnitCircle * (Radius * 0.55f);
                c = (best, Time.time + Random.Range(25f, 50f), new Vector3(o.x, 0f, o.y));
                choice[ai] = c;
            }
            return Flags[c.flag].pos + c.off;
        }

        static bool AnyOpen(int team) { foreach (var f in Flags) if (f.owner != team) return true; return false; }

        /// <summary>Mehrspieler: neue Runde (alle Panzer neu, Tickets und Flaggen zurueck).</summary>
        static void ServerNewRound()
        {
            foreach (var s in slots) RemoveTank(s.tc, s.nt);
            foreach (var w in wrecks) RemoveTank(w.tc, w.nt);
            slots.Clear(); wrecks.Clear(); counted.Clear(); choice.Clear();
            Tickets[0] = Tickets[1] = StartTickets; bleedAcc[0] = bleedAcc[1] = 0f;
            foreach (var f in Flags) { f.owner = -1; f.progress = 0f; }
            Winner = -1; endT = 0f;
            NetSync.RespawnAllPlayers();
            RangeLog.Add("EROBERUNG: neue Runde!");
        }

        // ---------------- Netz ----------------
        static void WriteNet(float dt)
        {
            syncT -= dt;
            var ns = NetSession.Instance;
            if (syncT > 0f || ns == null || !ns.IsSpawned || !ns.IsServer) return;
            syncT = 0.2f;
            int tk = Tickets[0] * 1000 + Tickets[1];
            int st = 0, mul = 1;
            foreach (var f in Flags) { st += (f.owner + 1) * mul; mul *= 3; }
            st += (Winner + 1) * mul;
            var pr = new Vector3(Flags[0].progress, Flags[1].progress, Flags[2].progress);
            if (ns.cqTickets.Value != tk) ns.cqTickets.Value = tk;
            if (ns.cqState.Value != st) ns.cqState.Value = st;
            if ((ns.cqProgress.Value - pr).sqrMagnitude > 1e-5f) ns.cqProgress.Value = pr;
        }

        static void ReadNet()
        {
            var ns = NetSession.Instance;
            if (ns == null) return;
            int tk = ns.cqTickets.Value, st = ns.cqState.Value;
            Tickets[0] = tk / 1000; Tickets[1] = tk % 1000;
            var pr = ns.cqProgress.Value;
            float[] p = { pr.x, pr.y, pr.z };
            int prevWinner = Winner;
            for (int i = 0; i < 3; i++)
            {
                int was = Flags[i].owner;
                Flags[i].owner = st % 3 - 1; st /= 3;
                Flags[i].progress = p[i];
                if (was != Flags[i].owner && Flags[i].owner >= 0)
                    TankCamera.Notice($"FLAGGE {Flags[i].name} {(Flags[i].owner == TankController.LocalTeam ? "EINGENOMMEN" : "VERLOREN")}", Flags[i].owner == TankController.LocalTeam);
            }
            Winner = st % 3 - 1;
            if (Winner >= 0 && prevWinner < 0)
                TankCamera.Notice($"{(Winner == TankController.LocalTeam ? "SIEG!" : "NIEDERLAGE")}  -  {GameSettings.TeamNames[Winner]} gewinnt die Eroberung  (neue Runde in 20 s)", true);
        }

        // ---------------- Anzeige ----------------
        static void UpdateVisuals()
        {
            if (NetSync.Headless) return;
            foreach (var f in Flags)
            {
                if (f == null || f.cloth == null) continue;
                // Tuch zeigt den Besitzer, waehrend der Einnahme blinkt es in der Farbe des einnehmenden Teams
                Color c = TeamColor(f.owner);
                bool contesting = f.owner >= 0 ? Mathf.Abs(f.progress) < 0.999f : Mathf.Abs(f.progress) > 0.01f;
                if (contesting && Mathf.Repeat(Time.time, 1f) < 0.5f) c = TeamColor(f.progress < 0f ? 0 : 1);
                var m = GameMap.Mat(c);
                if (f.cloth.sharedMaterial != m) f.cloth.sharedMaterial = m;
                var rm = GameMap.Mat(TeamColor(f.owner));
                if (f.ring.sharedMaterial != rm) f.ring.sharedMaterial = rm;
                // Tuch nach Einnahme hoch, sonst halb
                float h = Mathf.Lerp(6.5f, 10.6f, Mathf.Abs(f.progress));
                var lp = f.cloth.transform.localPosition;
                f.cloth.transform.localPosition = new Vector3(lp.x, h, lp.z);
                f.cloth.transform.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 1.3f + f.pos.x) * 8f, 0f);
            }
        }

        public static Color HudColor(int owner) => owner == 0 ? new Color(0.45f, 0.7f, 1f) : owner == 1 ? new Color(1f, 0.42f, 0.36f) : new Color(0.9f, 0.9f, 0.9f);

        /// <summary>Flaggen auf Minikarte und grosser Karte (TeamHud).</summary>
        public static void DrawOnMap(System.Func<Vector3, Vector2> toMap, Rect r, float k, Texture2D dot, GUIStyle label, float size)
        {
            if (!On) return;
            foreach (var f in Flags)
            {
                if (f == null) continue;
                Vector2 q = toMap(f.pos);
                if (!r.Contains(q)) continue;
                float s = size * k;
                GUI.color = new Color(0f, 0f, 0f, 0.8f); GUI.DrawTexture(new Rect(q.x - s * 0.62f, q.y - s * 0.62f, s * 1.24f, s * 1.24f), dot);
                GUI.color = HudColor(f.owner); GUI.DrawTexture(new Rect(q.x - s / 2f, q.y - s / 2f, s, s), dot);
                GUI.color = Color.black;
                GUI.Label(new Rect(q.x - s, q.y - s / 2f, s * 2f, s), f.name, label);
            }
            GUI.color = Color.white;
        }

        class Runner : MonoBehaviour
        {
            GUIStyle big, mid, small, letter;

            void Update() => Tick(Time.deltaTime);

            void OnGUI()
            {
                if (!On || NetSync.Headless) return;
                var gm = GameManager.Instance;
                if (gm == null || !gm.InGame || SpawnSelect.IsOpen) return;
                float k = GameSettings.UiFactor;
                if (big == null)
                {
                    big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
                    mid = new GUIStyle(big);
                    small = new GUIStyle(big) { fontStyle = FontStyle.Normal };
                    letter = new GUIStyle(big);
                    foreach (var st in new[] { big, mid, small, letter }) st.normal.textColor = Color.white;
                }
                big.fontSize = Mathf.RoundToInt(24 * k); mid.fontSize = Mathf.RoundToInt(20 * k); small.fontSize = Mathf.RoundToInt(14 * k); letter.fontSize = Mathf.RoundToInt(18 * k);
                int my = TankController.LocalTeam;
                // ---- oben: Tickets und Flaggen ----
                float cx = Screen.width / 2f, y = 8f * k;
                float bw = 470f * k, bh = 50f * k;
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(cx - bw / 2f, y, bw, bh), Texture2D.whiteTexture);
                GUI.color = HudColor(0); GUI.Label(new Rect(cx - bw / 2f, y, 110f * k, bh), Tickets[0].ToString(), big);
                GUI.color = HudColor(1); GUI.Label(new Rect(cx + bw / 2f - 110f * k, y, 110f * k, bh), Tickets[1].ToString(), big);
                for (int i = 0; i < 3; i++)
                {
                    var f = Flags[i]; if (f == null) continue;
                    float fx = cx + (i - 1) * 74f * k, s = 40f * k;
                    var fr = new Rect(fx - s / 2f, y + (bh - s) / 2f, s, s);
                    GUI.color = new Color(0.12f, 0.12f, 0.12f, 0.9f); GUI.DrawTexture(fr, Texture2D.whiteTexture);
                    // Fuellung: Anteil der Einnahme
                    float a = Mathf.Abs(f.progress);
                    GUI.color = HudColor(f.progress < 0f ? 0 : 1) * new Color(1f, 1f, 1f, f.owner >= 0 ? 1f : 0.75f);
                    GUI.DrawTexture(new Rect(fr.x, fr.yMax - fr.height * a, fr.width, fr.height * a), Texture2D.whiteTexture);
                    GUI.color = Color.white; GUI.Label(fr, f.name, mid);
                }
                GUI.color = new Color(1f, 1f, 1f, 0.8f);
                GUI.Label(new Rect(cx - bw / 2f, y + bh, bw, 20f * k), $"<color=#{(my == 0 ? "8bf" : "f88")}>Du: {GameSettings.TeamNames[my]}</color>   ·   Eroberung", small);

                // ---- im Flaggenkreis: Balken ----
                var pl = gm.Player;
                if (pl != null && !pl.destroyed)
                    foreach (var f in Flags)
                    {
                        if (f == null) continue;
                        Vector3 d = pl.transform.position - f.pos; d.y = 0f;
                        if (d.magnitude > Radius) continue;
                        float w = 360f * k, h = 22f * k, yy = Screen.height * 0.24f;
                        GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(new Rect(cx - w / 2f - 2, yy - 2, w + 4, h + 4), Texture2D.whiteTexture);
                        // Balken: Anteil der Einnahme in der Farbe des Teams, dem die Flagge gerade zufaellt
                        GUI.color = new Color(0.15f, 0.15f, 0.15f, 0.9f); GUI.DrawTexture(new Rect(cx - w / 2f, yy, w, h), Texture2D.whiteTexture);
                        GUI.color = HudColor(f.progress < 0f ? 0 : 1); GUI.DrawTexture(new Rect(cx - w / 2f, yy, w * Mathf.Abs(f.progress), h), Texture2D.whiteTexture);
                        bool mine = f.owner == my && Mathf.Abs(f.progress) >= 0.999f;
                        string txt = mine ? $"Flagge {f.name} gehoert deinem Team" : $"Flagge {f.name} wird eingenommen ...";
                        if (Simulate && f.n0 > 0 && f.n1 > 0) txt = $"Flagge {f.name} umkaempft!";
                        GUI.color = new Color(0f, 0f, 0f, 0.8f); GUI.Label(new Rect(cx - w / 2f + 1, yy + h + 3f, w, 24f * k), txt, small);
                        GUI.color = Color.white; GUI.Label(new Rect(cx - w / 2f, yy + h + 2f, w, 24f * k), txt, small);
                        break;
                    }

                // ---- Flaggen im Bild: Buchstabe + Entfernung ----
                var cam = Camera.main;
                if (cam != null && !TeamHud.MapOpen && !ArtilleryMode.Active)
                    foreach (var f in Flags)
                    {
                        if (f == null) continue;
                        Vector3 sp = cam.WorldToScreenPoint(f.pos + Vector3.up * 14f);
                        if (sp.z <= 0f) continue;
                        float dist = Vector3.Distance(cam.transform.position, f.pos);
                        float s = 26f * k, yy = Screen.height - sp.y;
                        GUI.color = new Color(0f, 0f, 0f, 0.7f); GUI.DrawTexture(new Rect(sp.x - s / 2f - 2, yy - s / 2f - 2, s + 4, s + 4), Texture2D.whiteTexture);
                        GUI.color = HudColor(f.owner); GUI.DrawTexture(new Rect(sp.x - s / 2f, yy - s / 2f, s, s), Texture2D.whiteTexture);
                        GUI.color = Color.black; GUI.Label(new Rect(sp.x - s / 2f, yy - s / 2f, s, s), f.name, letter);
                        GUI.color = new Color(0f, 0f, 0f, 0.8f); GUI.Label(new Rect(sp.x - 60f * k + 1, yy + s / 2f + 1, 120f * k, 18f * k), dist < 1000f ? $"{dist:0} m" : $"{dist / 1000f:0.0} km", small);
                        GUI.color = Color.white; GUI.Label(new Rect(sp.x - 60f * k, yy + s / 2f, 120f * k, 18f * k), dist < 1000f ? $"{dist:0} m" : $"{dist / 1000f:0.0} km", small);
                    }

                // ---- Rundenende ----
                if (Winner >= 0)
                {
                    float w = 640f * k, h = 130f * k;
                    var rr = new Rect(cx - w / 2f, Screen.height * 0.34f, w, h);
                    GUI.color = new Color(0f, 0f, 0f, 0.7f); GUI.DrawTexture(rr, Texture2D.whiteTexture);
                    GUI.color = Winner == my ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.45f, 0.4f);
                    var t = new GUIStyle(big) { fontSize = Mathf.RoundToInt(46 * k) };
                    GUI.Label(new Rect(rr.x, rr.y + 8f * k, rr.width, 64f * k), Winner == my ? "SIEG!" : "NIEDERLAGE", t);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(rr.x, rr.y + 74f * k, rr.width, 40f * k), $"{GameSettings.TeamNames[Winner]} gewinnt  ·  {Tickets[0]} : {Tickets[1]}  ·  {(networked ? "neue Runde gleich" : "N: neue Runde")}", small);
                }
                GUI.color = Color.white;
            }
        }

        // ---------------- Test ----------------
        public static string BenchText
        {
            get
            {
                string s = $"Tickets {Tickets[0]}:{Tickets[1]}, Sieger {Winner}";
                foreach (var f in Flags) if (f != null) s += $"  [{f.name} {f.pos.x:0}/{f.pos.z:0} Besitzer {f.owner} {f.progress:0.00} B{f.n0}/R{f.n1}]";
                int ai = 0; foreach (var sl in slots) if (sl.tc != null && !sl.tc.destroyed) ai++;
                return s + $"  KI aktiv {ai}/{slots.Count}, Wracks {wrecks.Count}";
            }
        }
        public static void BenchSetTickets(int t0, int t1) { Tickets[0] = t0; Tickets[1] = t1; }
    }
}
