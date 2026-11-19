using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Panzer-Duell (Einzelspieler): 1 gegen 1 gegen einen KI-Panzer auf einer kleinen Kampfflaeche (700 m Kreis).
    /// Gegner = gleicher Panzer wie der eigene (faires Duell). Wer zuerst 3 Runden gewinnt, gewinnt das Duell.
    /// Wer die Flaeche verlaesst, hat 10 s Zeit zurueckzukommen, sonst ist die Runde verloren.
    /// </summary>
    public static class DuelMode
    {
        public const float Radius = 350f;
        const int WinRounds = 3;
        const float OutTime = 10f, RoundPause = 6f;

        public static bool On { get; private set; }
        public static int MyWins { get; private set; }
        public static int EnemyWins { get; private set; }
        public static int Round { get; private set; }
        public static bool MatchOver => MyWins >= WinRounds || EnemyWins >= WinRounds;
        public static TankController Enemy => enemy;
        public static Vector3 Center => center;

        static GameMap map;
        static System.Func<TankSpec> playerSpec;
        static System.Func<Vector3, float, TankController> placePlayer;
        static TankController enemy, player;
        static Vector3 center, startA, startB;
        static float yawA, yawB;
        static float outT, pauseT, startT;
        static bool roundOver;
        static string lastResult = "";
        static Transform root;
        static Runner runner;

        public static void Begin(GameMap m, System.Func<TankSpec> spec, System.Func<Vector3, float, TankController> place)
        {
            Stop();
            map = m; playerSpec = spec; placePlayer = place;
            On = true; MyWins = EnemyWins = 0; Round = 0;
            // Kampfflaeche: 450 m vor dem Startplatz der Karte, auf ebenem freien Grund
            GameManager.Instance.TeamStart(0, out Vector2 p, out float yaw);
            float lim = m.Extent - Radius - 60f;
            Vector2 c = p + ModeKit.Dir(yaw) * 450f;
            c.x = Mathf.Clamp(c.x, -lim, lim); c.y = Mathf.Clamp(c.y, -lim, lim);
            center = new Vector3(c.x, m.GroundY(c.x, c.y), c.y);
            Vector2 fw = ModeKit.Dir(yaw);
            startA = ModeKit.FreeSpot(m, c - fw * (Radius * 0.75f));
            startB = ModeKit.FreeSpot(m, c + fw * (Radius * 0.75f));
            yawA = ModeKit.Yaw(startA, startB); yawB = yawA + 180f;
            root = new GameObject("Duell").transform;
            if (!NetSync.Headless) ModeKit.Ring(root, m, center, Radius, new Color(1f, 0.55f, 0.15f), 1.4f);
            if (runner == null) { var go = new GameObject("Duell-Ablauf"); Object.DontDestroyOnLoad(go); runner = go.AddComponent<Runner>(); }
            NewRound();
            RangeLog.AddLocal("DUELL: 1 gegen 1 in der orangen Kampfflaeche. Wer zuerst 3 Runden gewinnt, gewinnt das Duell.");
        }

        public static void Stop()
        {
            if (enemy != null) Object.Destroy(enemy.gameObject);
            enemy = null;
            if (root != null) Object.Destroy(root.gameObject);
            root = null; On = false;
        }

        public static void Restart() { var m = map; var s = playerSpec; var pp = placePlayer; Begin(m, s, pp); }

        static void NewRound()
        {
            Round++;
            roundOver = false; outT = 0f; startT = 0f;
            if (enemy != null) Object.Destroy(enemy.gameObject);
            player = placePlayer(startA, yawA);
            var spec = playerSpec();
            enemy = ModeKit.SpawnAi(spec, startB, yawB, player.team == 0 ? 1 : 0, Mathf.Clamp01(0.5f + Round * 0.06f), map.Extent);
            enemy.ai.searchRange = Radius * 2.5f;
            enemy.ai.objective = KeepInside;
            enemy.ai.holdRadius = 30f;
            TankCamera.Notice($"RUNDE {Round}  -  Stand {MyWins} : {EnemyWins}", true);
        }

        /// <summary>KI bleibt in der Flaeche: draussen faehrt sie zur Mitte zurueck.</summary>
        static Vector3? KeepInside(TankAI ai)
        {
            if (ai == null || ai.tc == null) return null;
            Vector3 d = ai.tc.transform.position - center; d.y = 0f;
            return d.magnitude > Radius - 60f ? center : (Vector3?)null;
        }

        static void EndRound(bool won, string why)
        {
            roundOver = true; pauseT = RoundPause;
            if (won) MyWins++; else EnemyWins++;
            lastResult = (won ? "RUNDE GEWONNEN" : "RUNDE VERLOREN") + (string.IsNullOrEmpty(why) ? "" : $" ({why})");
            if (MatchOver)
                TankCamera.Notice((MyWins > EnemyWins ? "DUELL GEWONNEN" : "DUELL VERLOREN") + $"  {MyWins} : {EnemyWins}  -  N: neues Duell", true);
            else
                TankCamera.Notice($"{lastResult}  -  Stand {MyWins} : {EnemyWins}", true);
            RangeLog.AddLocal($"Duell Runde {Round}: {lastResult}, Stand {MyWins} : {EnemyWins}");
        }

        static void Tick(float dt)
        {
            if (!On || map == null) return;
            var gm = GameManager.Instance;
            player = gm != null ? gm.Player : null;
            if (roundOver)
            {
                if (MatchOver) return;
                pauseT -= dt;
                if (pauseT <= 0f) NewRound();
                return;
            }
            if (player == null || enemy == null) return;
            startT += dt;
            if (player.destroyed && enemy.destroyed) { EndRound(false, "beide ausgeschaltet"); return; }
            if (enemy.destroyed) { EndRound(true, ""); return; }
            if (player.destroyed) { EndRound(false, ""); return; }
            Vector3 d = player.transform.position - center; d.y = 0f;
            if (d.magnitude > Radius) { outT += dt; if (outT > OutTime) EndRound(false, "Kampfflaeche verlassen"); }
            else outT = 0f;
        }

        class Runner : MonoBehaviour
        {
            void Update() => Tick(Time.deltaTime);
            void OnGUI()
            {
                if (!On || NetSync.Headless) return;
                var gm = GameManager.Instance;
                if (gm == null || !gm.InGame || SpawnSelect.IsOpen) return;
                string head = MatchOver ? $"<b>{(MyWins > EnemyWins ? "DUELL GEWONNEN" : "DUELL VERLOREN")}</b>   {MyWins} : {EnemyWins}   (N: neues Duell)"
                    : $"<b>DUELL</b>   Runde {Round}   ·   Du <b>{MyWins}</b> : <b>{EnemyWins}</b> Gegner   ·   {WinRounds} Siege gewinnen";
                ModeKit.Banner(head);
                if (roundOver && !MatchOver) ModeKit.Banner($"{lastResult}  -  naechste Runde in {Mathf.CeilToInt(Mathf.Max(0f, pauseT))} s", 1);
                else if (outT > 0f) ModeKit.Banner($"<color=#f66><b>AUSSERHALB DER KAMPFFLAECHE</b> - zurueck in {Mathf.CeilToInt(OutTime - outT)} s</color>", 1);
                else if (enemy != null && !enemy.destroyed && gm.Player != null)
                    ModeKit.Banner($"Gegner: {enemy.spec.name}   ·   {Vector3.Distance(enemy.transform.position, gm.Player.transform.position):0} m", 1);
            }
        }

        public static string BenchText => $"Runde {Round}, Stand {MyWins}:{EnemyWins}, aus {outT:0.0}s, Rundenende {roundOver}, Gegner {(enemy != null ? $"{enemy.spec.name} zerstoert {enemy.destroyed} {(enemy.ai != null ? enemy.ai.DebugText : "")}" : "-")}";
    }
}
