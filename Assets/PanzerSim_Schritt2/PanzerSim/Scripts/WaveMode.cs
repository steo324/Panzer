using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Wellen-Modus (Einzelspieler): KI-Panzer greifen in Wellen an, jede Welle mehr und staerkere Gegner.
    /// Zwischen den Wellen 20 s Pause, der eigene Panzer wird ganz repariert. Ausgeschaltet = Spiel vorbei (N: neu).
    /// Gegner kommen aus einer Richtung (pro Welle neu gewuerfelt) in 1-1,5 km Entfernung.
    /// </summary>
    public static class WaveMode
    {
        public static bool On { get; private set; }
        public static int Wave { get; private set; }
        public static int Kills { get; private set; }
        public static int Best { get; private set; }
        public static int EnemyCount => enemies.Count;
        public static IEnumerable<TankController> Enemies => enemies;

        static GameMap map;
        static System.Func<TankController> getPlayer;
        static readonly List<TankController> enemies = new List<TankController>();
        static readonly List<TankController> wrecks = new List<TankController>();
        static float pause;
        static bool over;
        static Runner runner;

        // Gegner nach Staerke (nur Fahrzeuge bis 1960; moderne Gegner nur, wenn man selbst modern faehrt)
        static readonly System.Func<TankSpec>[][] Tiers =
        {
            new System.Func<TankSpec>[] { () => TankLibrary.PanzerIII, () => TankLibrary.PanzerIV, () => TankLibrary.Sherman, () => TankLibrary.T34 },
            new System.Func<TankSpec>[] { () => TankLibrary.T34_85_44, () => TankLibrary.Panther, () => TankLibrary.KV1, () => TankLibrary.Firefly, () => TankLibrary.PanzerIV },
            new System.Func<TankSpec>[] { () => TankLibrary.Tiger, () => TankLibrary.IS1, () => TankLibrary.IS2, () => TankLibrary.T44, () => TankLibrary.Panther },
            new System.Func<TankSpec>[] { () => TankLibrary.TigerII, () => TankLibrary.IS3, () => TankLibrary.T54, () => TankLibrary.T10, () => TankLibrary.T44_100 },
        };
        static readonly System.Func<TankSpec>[] Modern =
            { () => TankLibrary.T55A, () => TankLibrary.T10M, () => TankLibrary.Leopard2A6, () => TankLibrary.Challenger2 };

        public static void Begin(GameMap m, System.Func<TankController> player)
        {
            Stop();
            map = m; getPlayer = player;
            On = true; Wave = 0; Kills = 0; over = false;
            pause = 12f;   // Zeit zum Einrichten (Startpunkt waehlen, Stellung suchen)
            if (runner == null) { var go = new GameObject("Wellen-Modus"); Object.DontDestroyOnLoad(go); runner = go.AddComponent<Runner>(); }
            RangeLog.AddLocal("WELLEN-MODUS: Gegner greifen in Wellen an. Suche dir eine gute Stellung!");
        }

        public static void Stop()
        {
            foreach (var e in enemies) if (e != null) Object.Destroy(e.gameObject);
            foreach (var e in wrecks) if (e != null) Object.Destroy(e.gameObject);
            enemies.Clear(); wrecks.Clear();
            On = false;
        }

        /// <summary>N nach Spielende: alles neu (GameManager baut den Spielerpanzer neu).</summary>
        public static void Restart() => Begin(map, getPlayer);

        static void Tick(float dt)
        {
            if (!On || map == null) return;
            var pl = getPlayer();
            if (pl == null) return;
            if (pl.destroyed)
            {
                if (!over) { over = true; Best = Mathf.Max(Best, Wave - 1); PlayerStats.OnWaveEnd(Wave - 1, Kills); TankCamera.Notice($"SPIEL VORBEI  -  Welle {Wave}, {Kills} Abschuesse  -  N: neues Spiel", true); }
                return;
            }
            // ausgeschaltete Gegner zaehlen
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                var e = enemies[i];
                if (e == null) { enemies.RemoveAt(i); continue; }
                if (e.destroyed) { Kills++; enemies.RemoveAt(i); wrecks.Add(e); }
            }
            if (enemies.Count > 0) return;
            if (Wave > 0 && pause <= 0f)
            {
                pause = 20f;
                pl.FullRepair();
                TankCamera.Notice($"WELLE {Wave} GESCHAFFT  -  Panzer repariert, naechste Welle in 20 s", true);
                // alte Wracks aufraeumen (die letzte Welle bleibt als Deckung liegen)
                while (wrecks.Count > 10) { if (wrecks[0] != null) Object.Destroy(wrecks[0].gameObject); wrecks.RemoveAt(0); }
            }
            pause -= dt;
            if (pause <= 0f) SpawnWave(pl);
        }

        static void SpawnWave(TankController pl)
        {
            Wave++;
            int count = Mathf.Min(1 + Wave, 8);
            int tier = Mathf.Min((Wave - 1) / 2, Tiers.Length - 1);
            bool modern = pl.spec.fireControl;   // selbst moderner Panzer: spaeter auch moderne Gegner
            float skill = Mathf.Clamp01(0.25f + Wave * 0.07f);
            float dirAng = Random.Range(0f, 360f);
            float extent = map.Extent;
            int enemyTeam = pl.team == 0 ? 1 : 0;
            for (int i = 0; i < count; i++)
            {
                System.Func<TankSpec> pick;
                if (modern && Wave >= 4 && Random.value < 0.35f + Wave * 0.03f) pick = Modern[Random.Range(0, Modern.Length)];
                else
                {
                    int t = Mathf.Clamp(tier - (Random.value < 0.35f ? 1 : 0), 0, Tiers.Length - 1);
                    pick = Tiers[t][Random.Range(0, Tiers[t].Length)];
                }
                if (!FindSpawn(pl.transform.position, dirAng, extent, out Vector3 pos)) continue;
                Vector3 toPl = pl.transform.position - pos; toPl.y = 0f;
                var t2 = TankBuilder.Build(pick(), pos, Quaternion.LookRotation(toPl.normalized, Vector3.up), false);
                t2.team = enemyTeam;
                t2.playerName = $"{t2.spec.name} (KI)";
                TankAI.Attach(t2, skill * Random.Range(0.8f, 1.2f), extent);
                enemies.Add(t2);
            }
            TankCamera.Notice($"WELLE {Wave}: {enemies.Count} Gegner", true);
        }

        /// <summary>Platz 1-1,5 km vom Spieler in Angriffsrichtung, auf der Karte, nicht in Haeusern/Baeumen.</summary>
        static bool FindSpawn(Vector3 player, float dirAng, float extent, out Vector3 pos)
        {
            float lim = extent - 60f;
            for (int k = 0; k < 40; k++)
            {
                float a = (dirAng + Random.Range(-40f, 40f) + (k > 20 ? Random.Range(0f, 360f) : 0f)) * Mathf.Deg2Rad;
                float d = Random.Range(1000f, 1500f) * (k > 25 ? 0.6f : 1f);
                float x = Mathf.Clamp(player.x + Mathf.Sin(a) * d, -lim, lim), z = Mathf.Clamp(player.z + Mathf.Cos(a) * d, -lim, lim);
                if (new Vector2(x - player.x, z - player.z).magnitude < 400f) continue;
                float y = map.GroundY(x, z);
                Vector3 c = new Vector3(x, y + 2.6f, z);
                if (Physics.CheckBox(c, new Vector3(2.2f, 1.6f, 4f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;   // Haus, Baum, anderer Panzer
                pos = new Vector3(x, y + 0.5f, z);
                return true;
            }
            pos = Vector3.zero;
            return false;
        }

        class Runner : MonoBehaviour
        {
            void Update() => Tick(Time.deltaTime);

            void OnGUI()
            {
                if (!On) return;
                float f = GameSettings.UiFactor;
                var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22 * f), alignment = TextAnchor.UpperCenter, richText = true };
                string txt = over ? $"<b>SPIEL VORBEI</b>   Welle {Wave}  ·  {Kills} Abschuesse   (N: neues Spiel)"
                    : enemies.Count > 0 ? $"<b>WELLE {Wave}</b>   ·   Gegner: {enemies.Count}   ·   Abschuesse: {Kills}"
                    : $"<b>{(Wave == 0 ? "ERSTE WELLE" : $"WELLE {Wave + 1}")}</b> in {Mathf.CeilToInt(Mathf.Max(pause, 0f))} s   ·   Abschuesse: {Kills}";
                var r = new Rect(Screen.width / 2f - 400f * f, 8f * f, 800f * f, 36f * f);
                GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), StripTags(txt), st);
                GUI.color = new Color(1f, 0.85f, 0.4f); GUI.Label(r, txt, st);
                GUI.color = Color.white;
            }

            static string StripTags(string s) => s.Replace("<b>", "").Replace("</b>", "");
        }
    }
}
