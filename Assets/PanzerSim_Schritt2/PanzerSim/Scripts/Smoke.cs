using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Nebel: Taste K = Nebelwerfer (6 Wurfgranaten aus den Bechern am Turm, 30-45 m in Turmrichtung, 2 Ladungen, 25 s
    /// Nachladen). Jede Granate wird zu einer dichten Wolke (bis 13 m, ca. 45 s). Wolken verdecken die Sicht: im Bild
    /// (dichter Rauch), fuer das Zielen der KI, die Pak und Panzerfaeuste und fuer die Namen/Minikarte.
    /// </summary>
    public static class Smoke
    {
        class Cloud { public Vector3 pos; public float born, life, maxR, emitT; public Vector3 drift; }
        static readonly List<Cloud> clouds = new List<Cloud>();
        static readonly Dictionary<TankController, (int charges, float reload)> launchers = new Dictionary<TankController, (int, float)>();
        static Runner runner;
        public const int Charges = 2;
        public static int CloudCount => clouds.Count;

        static void Ensure() { if (runner == null) { var go = new GameObject("Nebel"); Object.DontDestroyOnLoad(go); runner = go.AddComponent<Runner>(); } }

        public static void Clear() { clouds.Clear(); launchers.Clear(); }

        /// <summary>Radius der Wolke jetzt (0 = weg).</summary>
        static float Radius(Cloud c)
        {
            float age = Time.time - c.born;
            if (age < 0f || age > c.life) return 0f;
            float grow = Mathf.Clamp01(age / 4f), fade = Mathf.Clamp01((c.life - age) / 10f);
            return c.maxR * Mathf.Lerp(0.25f, 1f, grow) * Mathf.Lerp(0.6f, 1f, fade);
        }
        static float Density(Cloud c) { float age = Time.time - c.born; return age < 0.6f ? 0f : Mathf.Clamp01((c.life - age) / 8f); }

        /// <summary>Wolke an einer Stelle (Nebelgranate, Rauchgranate der Artillerie).</summary>
        public static void AddCloud(Vector3 pos, float radius = 13f, float life = 45f, float delay = 0f)
        {
            Ensure();
            clouds.Add(new Cloud { pos = pos, born = Time.time + delay, life = life, maxR = radius, drift = new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.4f, 0.4f)) });
        }

        /// <summary>Verdeckt Rauch die Linie a-b? (Wolke dicht genug und die Linie geht durch ihren Kern)</summary>
        public static bool Hides(Vector3 a, Vector3 b)
        {
            for (int i = 0; i < clouds.Count; i++)
            {
                var c = clouds[i];
                float r = Radius(c) * 0.85f;
                if (r < 1f || Density(c) < 0.35f) continue;
                Vector3 ab = b - a; float len2 = ab.sqrMagnitude;
                float t = len2 > 0f ? Mathf.Clamp01(Vector3.Dot(c.pos - a, ab) / len2) : 0f;
                Vector3 p = a + ab * t;
                // Wolke ist flacher als breit (liegt am Boden)
                Vector3 d = p - c.pos; d.y *= 1.6f;
                if (d.sqrMagnitude < r * r) return true;
            }
            return false;
        }

        public static int ChargesLeft(TankController t) => launchers.TryGetValue(t, out var l) ? l.charges : Charges;
        public static float ReloadLeft(TankController t) => launchers.TryGetValue(t, out var l) ? Mathf.Max(0f, l.reload) : 0f;

        /// <summary>Nebelwerfer ausloesen: Granaten facherfoermig in Turmrichtung.</summary>
        public static bool Discharge(TankController t)
        {
            if (t == null || t.destroyed) return false;
            Ensure();
            var l = launchers.TryGetValue(t, out var x) ? x : (Charges, 0f);
            if (l.Item1 <= 0) { if (t.isPlayer) RangeLog.AddLocal("Nebelwerfer leer"); return false; }
            if (l.Item2 > 0f) { if (t.isPlayer) RangeLog.AddLocal($"Nebelwerfer laedt nach ({l.Item2:0} s)"); return false; }
            launchers[t] = (l.Item1 - 1, 25f);
            Transform tur = t.parts.turret != null ? t.parts.turret : t.transform;
            Vector3 fw = tur.forward; fw.y = 0f; fw.Normalize();
            var map = GameManager.Instance != null ? GameManager.Instance.CurrentMap : null;
            Vector3 top = t.transform.position + Vector3.up * (t.spec.clearance + t.spec.hullHeight + t.spec.turretHeight * 0.7f);
            for (int i = 0; i < 6; i++)
            {
                float ang = (i - 2.5f) * 14f;
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * fw;
                Vector3 p = t.transform.position + dir * Random.Range(30f, 45f);
                if (map != null) p.y = map.GroundY(p.x, p.z) + 1.5f;
                float flight = 0.9f + i * 0.05f;
                AddCloud(p, Random.Range(11f, 14f), Random.Range(40f, 50f), flight);
                if (!NetSync.Headless)
                {
                    Effects.SmokePuff(top + dir * 1.2f, 0.6f, 1.5f, 2);     // Abschuss aus dem Becher
                    runner.Fly(top, p, flight);                           // Granate im Flug (kleine Rauchspur)
                }
            }
            if (!NetSync.Headless) Sfx.Cannon(top, 20f);
            if (t.isPlayer) RangeLog.AddLocal($"Nebelwerfer: Nebelwand in Turmrichtung ({ChargesLeft(t)} Ladung(en) uebrig)");
            return true;
        }

        public static void Refill(TankController t) => launchers.Remove(t);

        class Runner : MonoBehaviour
        {
            readonly List<(Vector3 a, Vector3 b, float t0, float dur)> flying = new List<(Vector3, Vector3, float, float)>();
            public void Fly(Vector3 a, Vector3 b, float dur) => flying.Add((a, b, Time.time, dur));

            void Update()
            {
                float dt = Time.deltaTime;
                // Nachladen
                if (launchers.Count > 0)
                {
                    var keys = new List<TankController>(launchers.Keys);
                    foreach (var k in keys) { if (k == null) { launchers.Remove(k); continue; } var l = launchers[k]; if (l.reload > 0f) launchers[k] = (l.charges, l.reload - dt); }
                }
                for (int i = flying.Count - 1; i >= 0; i--)
                {
                    var f = flying[i];
                    float u = (Time.time - f.t0) / f.dur;
                    if (u >= 1f) { flying.RemoveAt(i); continue; }
                    Vector3 p = Vector3.Lerp(f.a, f.b, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 9f;
                    if (!NetSync.Headless) Effects.SmokeOne(p, Vector3.zero, 0.5f, 1.2f, 0.85f, 0.7f);
                }
                for (int i = clouds.Count - 1; i >= 0; i--)
                {
                    var c = clouds[i];
                    float age = Time.time - c.born;
                    if (age > c.life) { clouds.RemoveAt(i); continue; }
                    if (age < 0f) continue;
                    c.pos += c.drift * dt;   // Wind
                    if (NetSync.Headless) continue;
                    c.emitT -= dt;
                    if (c.emitT > 0f) continue;
                    c.emitT = 0.35f;   // wenige grosse Teile: viel Rauch kostet sonst Grafikleistung (Ueberzeichnen)
                    float r = Radius(c), dens = Density(c);
                    int n = age < 2f ? 4 : 2;
                    for (int k = 0; k < n; k++)
                    {
                        Vector3 q = c.pos + new Vector3(Random.Range(-1f, 1f) * r * 0.75f, Random.Range(-0.2f, 0.6f) * r * 0.5f, Random.Range(-1f, 1f) * r * 0.75f);
                        Effects.SmokeOne(q, Vector3.up * 0.15f + c.drift, r * Random.Range(0.7f, 1.0f), Random.Range(5f, 7f), Random.Range(0.8f, 0.92f), 0.85f * Mathf.Max(dens, 0.2f));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Artillerie anfordern (Einzelspieler): in der Kommandantensicht (B) mit L das Ziel im Fadenkreuz markieren.
    /// Nach 14 s Flugzeit schlaegt eine Salve aus 6 Sprenggranaten (122 mm) um den Punkt ein (Streuung ca. 35 m).
    /// Danach 75 s, bis die Batterie wieder bereit ist. Rote Rauchmarke und Kartenkreuz zeigen das Ziel.
    /// </summary>
    public static class ArtyStrike
    {
        public const float Delay = 14f, Cooldown = 75f, Spread = 35f;
        const int Rounds = 6;
        static ShellData Round => AmmoLibrary.All[32];   // OF-471, 122 mm Spreng
        public static Vector3 Target { get; private set; }
        public static bool Pending => pending;
        public static float TimeToImpact => Mathf.Max(0f, impactAt - Time.time);
        public static float ReadyIn => Mathf.Max(0f, readyAt - Time.time);
        public static int Impacts;
        static bool pending;
        static float impactAt, readyAt, markT;
        static int fired;
        static Runner runner;

        public static void Reset() { pending = false; readyAt = 0f; Impacts = 0; }

        /// <summary>Ziel aus der Kommandantensicht anfordern (Strahl aus der Kamera, bis 6 km).</summary>
        public static void Request(TankController from, Camera cam)
        {
            if (from == null || cam == null) return;
            if (NetSync.Active) { RangeLog.AddLocal("Artillerie gibt es nur im Einzelspieler."); return; }
            if (pending) { RangeLog.AddLocal($"Artillerie ist schon unterwegs (Einschlag in {TimeToImpact:0} s)"); return; }
            if (ReadyIn > 0f) { RangeLog.AddLocal($"Batterie laedt nach - bereit in {ReadyIn:0} s"); return; }
            // erster "echter" Treffer: Gelaende, Panzer oder etwas Hohes (Haus, Baum) - flache Hilfs-Collider (Weizenfeld) zaehlen nicht
            var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, 6000f, ArmorPlate.ShotMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            RaycastHit hit = default; bool found = false;
            foreach (var h in hits)
            {
                if (h.collider.GetComponentInParent<TankController>() == from) continue;
                bool solid = h.collider is TerrainCollider || h.collider.GetComponentInParent<TankController>() != null || h.collider.GetComponentInParent<ArmorPlate>() != null || h.collider.bounds.size.y > 2.5f;
                if (solid) { hit = h; found = true; break; }
            }
            if (!found) { RangeLog.AddLocal("Artillerie: kein Ziel im Fadenkreuz (zu weit oder Himmel)"); return; }
            if (Vector3.Distance(hit.point, from.transform.position) < 150f) { RangeLog.AddLocal("Artillerie: Ziel zu nah an dir (mindestens 150 m)"); return; }
            Begin(hit.point);
            TankCamera.Notice($"ARTILLERIE ANGEFORDERT  -  {Vector3.Distance(hit.point, from.transform.position):0} m, Einschlag in {Delay:0} s", true);
        }

        public static void Begin(Vector3 p)
        {
            if (runner == null) { var go = new GameObject("Artillerie-Unterstuetzung"); Object.DontDestroyOnLoad(go); runner = go.AddComponent<Runner>(); }
            Target = p; pending = true; fired = 0;
            impactAt = Time.time + Delay; markT = 0f;
            RangeLog.AddLocal($"Artillerie: Feuerauftrag {p.x / 1000f:0.00} / {p.z / 1000f:0.00} km, Salve {Rounds} x 122 mm");
        }

        class Runner : MonoBehaviour
        {
            void Update()
            {
                if (!pending) return;
                // Zielmarke: roter Rauch
                markT -= Time.deltaTime;
                if (markT <= 0f && !NetSync.Headless) { markT = 0.25f; Effects.SmokeOne(Target + Vector3.up * 0.5f, Vector3.up * 1.2f, 2.2f, 4f, 0.5f, 0.8f); }
                // Granaten so starten, dass sie zur Zeit X einschlagen: steil von oben, 2 s vorher
                float t = Time.time;
                while (fired < Rounds && t >= impactAt - 2f + fired * 0.6f)
                {
                    Vector2 off = Random.insideUnitCircle * Spread;
                    Vector3 hitAt = Target + new Vector3(off.x, 0f, off.y);
                    Vector3 dir = new Vector3(0.25f, -1f, 0.15f).normalized;
                    Vector3 start = hitAt - dir * 620f;
                    var s = Shell.Fire(Round, start, dir, false, 310f);
                    if (s != null) s.noTracer = true;
                    fired++;
                }
                if (fired >= Rounds && t > impactAt + Rounds * 0.6f + 1f) { pending = false; readyAt = t + Cooldown; Impacts += Rounds; RangeLog.AddLocal("Artillerie: Salve beendet"); }
            }

            GUIStyle st;
            void OnGUI()
            {
                if (NetSync.Headless || (!pending && ReadyIn <= 0f)) return;
                var gm = GameManager.Instance; var cam = Camera.main;
                if (gm == null || !gm.InGame || cam == null || TeamHud.MapOpen) return;
                float k = GameSettings.UiFactor;
                if (st == null) { st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold }; }
                st.fontSize = Mathf.RoundToInt(14 * k);
                string txt = pending ? $"Artillerie: Einschlag in {TimeToImpact:0} s" : $"Artillerie bereit in {ReadyIn:0} s";
                st.normal.textColor = Color.black; GUI.Label(new Rect(Screen.width - 401f * k, 268f * k, 380f * k, 22f * k), txt, st);
                st.normal.textColor = new Color(1f, 0.6f, 0.4f); GUI.Label(new Rect(Screen.width - 402f * k, 267f * k, 380f * k, 22f * k), txt, st);
                if (!pending) return;
                Vector3 sp = cam.WorldToScreenPoint(Target + Vector3.up * 2f);
                if (sp.z <= 0f) return;
                float y = Screen.height - sp.y, s = 22f * k;
                GUI.color = new Color(1f, 0.25f, 0.2f);
                GUI.DrawTexture(new Rect(sp.x - s / 2f, y - 1f * k, s, 2f * k), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(sp.x - 1f * k, y - s / 2f, 2f * k, s), Texture2D.whiteTexture);
                GUI.Label(new Rect(sp.x - 100f * k, y + s / 2f, 200f * k, 20f * k), $"Artillerieziel  {TimeToImpact:0} s", st);
                GUI.color = Color.white;
            }
        }

        /// <summary>Zielkreuz auf Mini- und grosser Karte.</summary>
        public static void DrawOnMap(System.Func<Vector3, Vector2> toMap, Rect r, float k, Texture2D dot)
        {
            if (!pending) return;
            Vector2 q = toMap(Target);
            if (!r.Contains(q)) return;
            GUI.color = new Color(1f, 0.25f, 0.2f);
            float s = 14f * k;
            GUI.DrawTexture(new Rect(q.x - s / 2f, q.y - 1.5f * k, s, 3f * k), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(q.x - 1.5f * k, q.y - s / 2f, 3f * k, s), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
