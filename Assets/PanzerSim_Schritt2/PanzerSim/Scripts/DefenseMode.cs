using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Verteidigung (Einzelspieler): eine Stellung (grüner Kreis, 70 m) gegen KI-Wellen aus zwei bis drei Richtungen halten.
    /// Zwei verbuendete KI-Panzer helfen. Die Gegner fahren auf die Stellung zu und kaempfen unterwegs.
    /// Verloren: Gegner stehen 30 s in der Stellung, ohne dass ein eigener Panzer drin ist - oder alle 3 Leben verbraucht.
    /// Gewonnen: 10 Wellen ueberstanden. Zwischen den Wellen 25 s Pause, alles wird repariert.
    /// </summary>
    public static class DefenseMode
    {
        public const float Radius = 70f;
        public const int Waves = 10, StartLives = 3, Allies = 2;
        const float LoseTime = 30f, Pause = 25f;

        public static bool On { get; private set; }
        public static int Wave { get; private set; }
        public static int Lives { get; private set; }
        public static int Kills { get; private set; }
        public static bool Over { get; private set; }
        public static bool Won { get; private set; }
        public static Vector3 Center => center;
        public static int EnemyCount => enemies.Count;
        public static IEnumerable<TankController> Enemies => enemies;

        static GameMap map;
        static System.Func<TankController> getPlayer;
        static Vector3 center;
        static float pauseT, holdT;
        static int myTeam;
        static string dirText = "";
        static readonly List<TankController> enemies = new List<TankController>();
        static readonly List<TankController> allies = new List<TankController>();
        static readonly List<TankController> wrecks = new List<TankController>();
        static readonly HashSet<TankController> lostPlayers = new HashSet<TankController>();
        static Transform root;
        static LineRenderer ring;
        static Runner runner;

        public static void Begin(GameMap m, System.Func<TankController> player)
        {
            Stop();
            map = m; getPlayer = player;
            On = true; Wave = 0; Kills = 0; Lives = StartLives; Over = false; Won = false; holdT = 0f;
            pauseT = 15f;
            myTeam = GameSettings.Team;
            GameManager.Instance.TeamStart(myTeam, out Vector2 p, out _);
            if (m.Extent - Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)) < 900f) p = Vector2.Lerp(p, Vector2.zero, 0.8f);   // nah am Rand: Richtung Mitte, Platz fuer Angriffe von allen Seiten
            { var sp = ModeKit.FreeSpot(m, p, 60f); p = new Vector2(sp.x, sp.z); }
            center = new Vector3(p.x, m.GroundY(p.x, p.y), p.y);
            root = new GameObject("Verteidigung").transform;
            if (!NetSync.Headless)
            {
                ring = ModeKit.Ring(root, m, center, Radius, new Color(0.3f, 0.9f, 0.35f), 1.1f);
                ModeKit.Flagpole(root, center + new Vector3(6f, 0f, 6f), new Color(0.3f, 0.9f, 0.35f));
            }
            if (runner == null) { var go = new GameObject("Verteidigung-Ablauf"); Object.DontDestroyOnLoad(go); runner = go.AddComponent<Runner>(); }
            SpawnAllies();
            RangeLog.AddLocal("VERTEIDIGUNG: Halte die gruene Stellung gegen 10 Wellen. Gegner kommen aus mehreren Richtungen!");
        }

        public static void Stop()
        {
            foreach (var l in new[] { enemies, allies, wrecks })
            {
                foreach (var t in l) if (t != null) Object.Destroy(t.gameObject);
                l.Clear();
            }
            lostPlayers.Clear();
            if (root != null) Object.Destroy(root.gameObject);
            root = null; On = false;
        }

        public static void Restart() => Begin(map, getPlayer);

        /// <summary>Platz fuer den Spieler (Neustart nach Verlust eines Lebens).</summary>
        public static void RespawnPoint(out Vector3 pos, out float yaw)
        {
            pos = ModeKit.FreeSpot(map, new Vector2(center.x, center.z) + Random.insideUnitCircle * 25f, 50f);
            GameManager.Instance.TeamStart(myTeam, out _, out yaw);
        }

        public static bool CanRespawn => On && !Over && Lives > 0;

        static void SpawnAllies()
        {
            allies.RemoveAll(a => a == null || a.destroyed);
            var pl = getPlayer();
            bool modern = pl != null && pl.spec.fireControl;
            while (allies.Count < Allies)
            {
                float ang = Random.Range(0f, 360f);
                Vector2 c = new Vector2(center.x, center.z) + ModeKit.Dir(ang) * Random.Range(25f, 50f);
                var spec = ModeKit.PickEnemy(1, modern, 0.6f);
                var t = ModeKit.SpawnAi(spec, ModeKit.FreeSpot(map, c, 40f), ang, myTeam, 0.55f, map.Extent);
                t.playerName = $"{spec.name} (Verbuendet)";
                Vector3 off = new Vector3(Random.Range(-30f, 30f), 0f, Random.Range(-30f, 30f));
                t.ai.objective = _ => center + off;
                t.ai.holdRadius = 12f;
                allies.Add(t);
            }
        }

        static bool InZone(TankController t)
        {
            Vector3 d = t.transform.position - center; d.y = 0f;
            return d.magnitude < Radius;
        }

        static void Tick(float dt)
        {
            if (!On || map == null || Over) return;
            var pl = getPlayer();
            // eigener Panzer ausgeschaltet: ein Leben weg
            if (pl != null && pl.destroyed && lostPlayers.Add(pl))
            {
                Lives--;
                if (Lives <= 0) { Finish(false, "alle Leben verbraucht"); return; }
                TankCamera.Notice($"PANZER VERLOREN  -  noch {Lives} {(Lives == 1 ? "Leben" : "Leben")}  -  N: neuer Panzer in der Stellung", true);
            }
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                var e = enemies[i];
                if (e == null) { enemies.RemoveAt(i); continue; }
                if (e.destroyed) { Kills++; enemies.RemoveAt(i); wrecks.Add(e); }
            }
            // Stellung: Gegner drin, kein Eigener drin -> Uhr laeuft
            bool enemyIn = false, friendIn = false;
            foreach (var e in enemies) if (InZone(e)) enemyIn = true;
            if (pl != null && !pl.destroyed && InZone(pl)) friendIn = true;
            foreach (var a in allies) if (a != null && !a.destroyed && InZone(a)) friendIn = true;
            if (enemyIn && !friendIn) holdT += dt; else holdT = Mathf.Max(0f, holdT - dt * 0.5f);
            if (holdT >= LoseTime) { Finish(false, "Stellung verloren"); return; }
            if (ring != null)
            {
                var m = GameMap.Mat(enemyIn && !friendIn && Mathf.Repeat(Time.time, 0.8f) < 0.4f ? new Color(1f, 0.3f, 0.25f) : new Color(0.3f, 0.9f, 0.35f));
                if (ring.sharedMaterial != m) ring.sharedMaterial = m;
            }

            if (enemies.Count > 0) return;
            if (Wave >= Waves) { Finish(true, ""); return; }
            if (Wave > 0 && pauseT <= 0f)
            {
                pauseT = Pause;
                if (pl != null && !pl.destroyed) pl.FullRepair();
                foreach (var a in allies) if (a != null && !a.destroyed) a.FullRepair();
                SpawnAllies();
                TankCamera.Notice($"WELLE {Wave} ABGEWEHRT  -  repariert, naechste Welle in {Pause:0} s", true);
                while (wrecks.Count > 12) { if (wrecks[0] != null) Object.Destroy(wrecks[0].gameObject); wrecks.RemoveAt(0); }
            }
            pauseT -= dt;
            if (pauseT <= 0f) SpawnWave(pl);
        }

        static readonly string[] Compass = { "Norden", "Nordosten", "Osten", "Suedosten", "Sueden", "Suedwesten", "Westen", "Nordwesten" };

        static void SpawnWave(TankController pl)
        {
            Wave++;
            int count = Mathf.Min(2 + Wave, 10);
            int dirs = Wave < 3 ? 2 : 3;
            int tier = Mathf.Min((Wave - 1) / 3, ModeKit.Tiers.Length - 1);
            bool modern = pl != null && pl.spec.fireControl;
            float skill = Mathf.Clamp01(0.3f + Wave * 0.05f);
            float baseAng = Random.Range(0f, 360f);
            var names = new List<string>();
            float[] angs = new float[dirs];
            for (int k = 0; k < dirs; k++)
            {
                angs[k] = baseAng + k * 360f / dirs + Random.Range(-25f, 25f);
                names.Add(Compass[Mathf.RoundToInt(Mathf.Repeat(angs[k], 360f) / 45f) % 8]);
            }
            float lim = map.Extent - 70f;
            for (int i = 0; i < count; i++)
            {
                float a = angs[i % dirs] + Random.Range(-12f, 12f);
                Vector2 c = new Vector2(center.x, center.z) + ModeKit.Dir(a) * Random.Range(900f, 1300f);
                // Kartenrand: naeher heran
                for (int k = 0; k < 6 && (Mathf.Abs(c.x) > lim || Mathf.Abs(c.y) > lim); k++) c = Vector2.Lerp(new Vector2(center.x, center.z), c, 0.85f);
                c.x = Mathf.Clamp(c.x, -lim, lim); c.y = Mathf.Clamp(c.y, -lim, lim);
                if ((c - new Vector2(center.x, center.z)).magnitude < 600f) continue;
                var pos = ModeKit.FreeSpot(map, c, 60f);
                var t = ModeKit.SpawnAi(ModeKit.PickEnemy(tier, modern && Wave >= 4, 0.3f + Wave * 0.04f), pos, ModeKit.Yaw(pos, center), myTeam == 0 ? 1 : 0, skill * Random.Range(0.85f, 1.15f), map.Extent);
                Vector3 off = new Vector3(Random.Range(-40f, 40f), 0f, Random.Range(-40f, 40f));
                t.ai.objective = _ => center + off;
                t.ai.holdRadius = 15f;
                enemies.Add(t);
            }
            dirText = string.Join(", ", names);
            TankCamera.Notice($"WELLE {Wave}/{Waves}: {enemies.Count} Gegner aus {dirText}", true);
        }

        static void Finish(bool won, string why)
        {
            Over = true; Won = won;
            TankCamera.Notice(won ? $"STELLUNG GEHALTEN!  Alle {Waves} Wellen abgewehrt, {Kills} Abschuesse  -  N: neues Spiel"
                : $"VERLOREN ({why})  -  Welle {Wave}, {Kills} Abschuesse  -  N: neues Spiel", true);
            RangeLog.AddLocal(won ? "Verteidigung gewonnen" : $"Verteidigung verloren: {why}");
        }

        class Runner : MonoBehaviour
        {
            void Update() => Tick(Time.deltaTime);
            void OnGUI()
            {
                if (!On || NetSync.Headless) return;
                var gm = GameManager.Instance;
                if (gm == null || !gm.InGame || SpawnSelect.IsOpen) return;
                string head = Over ? (Won ? $"<b>STELLUNG GEHALTEN</b>   {Kills} Abschuesse   (N: neues Spiel)" : $"<b>VERLOREN</b>   Welle {Wave}   ·   {Kills} Abschuesse   (N: neues Spiel)")
                    : enemies.Count > 0 ? $"<b>WELLE {Wave}/{Waves}</b>   ·   Gegner: {enemies.Count}   ·   Leben: {Lives}   ·   Abschuesse: {Kills}"
                    : $"<b>{(Wave == 0 ? "ERSTE WELLE" : $"WELLE {Wave + 1}/{Waves}")}</b> in {Mathf.CeilToInt(Mathf.Max(pauseT, 0f))} s   ·   Leben: {Lives}   ·   Abschuesse: {Kills}";
                ModeKit.Banner(head);
                if (!Over && holdT > 0.5f) ModeKit.Banner($"<color=#f66><b>GEGNER IN DER STELLUNG!</b> - noch {Mathf.CeilToInt(LoseTime - holdT)} s</color>", 1);
                else if (!Over && enemies.Count > 0) ModeKit.Banner($"Angriff aus {dirText}", 1);
                else if (!Over && gm.Player != null && !gm.Player.destroyed && !InZone(gm.Player)) ModeKit.Banner("Bleib in der Naehe der gruenen Stellung", 1);
            }
        }

        public static string BenchText
        {
            get
            {
                var pl = getPlayer != null ? getPlayer() : null;
                int al = 0; foreach (var a in allies) if (a != null && !a.destroyed) al++;
                string s = $"Verbuendete {al}, Welle {Wave}, Gegner {enemies.Count}, Leben {Lives}, Abschuesse {Kills}, Stellung-Uhr {holdT:0.0}, vorbei {Over} (gewonnen {Won})";
                foreach (var e in enemies) if (e != null && pl != null) s += $" [{e.spec.name} {Vector3.Distance(e.transform.position, center):0} m zur Stellung]";
                return s;
            }
        }
        public static void BenchSkipPause() { pauseT = 0.1f; }
    }
}
