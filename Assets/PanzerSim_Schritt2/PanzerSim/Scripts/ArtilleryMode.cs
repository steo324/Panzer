using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Artillerie-Modus (Panzerhaubitze 2000, Taste M): Karte von oben mit Raster.
    ///  - Linksklick setzt das Ziel, der Feuerleitrechner berechnet Seitenrichtung, Rohrerhoehung,
    ///    Treibladung (Zone 1-6) und Flugzeit - mit Luftwiderstand und Hoehenunterschied (wie die echte Rechnung).
    ///  - Tab: Steilfeuer (Rohr ueber 45 Grad, Granate faellt steil ein) / Flachfeuer
    ///  - Leertaste: Feuer, sobald der Turm gerichtet ist; Countdown bis zum Einschlag, Einschlaege auf der Karte
    ///  - Mausrad: Zoom, rechte Maustaste: Karte verschieben, M/Esc: zurueck
    /// Das Fahrzeug steht dabei, der Turm richtet sich selbst.
    /// </summary>
    public class ArtilleryMode : MonoBehaviour
    {
        public static bool Active => inst != null && inst.enabled && inst.tc != null;
        static ArtilleryMode inst;

        // Treibladungen (modulares Ladungssystem): Muendungsgeschwindigkeit je Zone
        static readonly float[] ZoneSpeed = { 270f, 360f, 470f, 590f, 720f, 900f };

        TankController tc;
        Camera cam;
        float orthoSize = 900f;
        Vector3 center;
        Vector3? target;
        bool high = true;
        Solution sol;
        float savedFov, savedFar;
        class Pending { public Vector3 at; public float impactTime; }
        readonly List<Pending> inFlight = new List<Pending>();
        readonly List<Pending> impacts = new List<Pending>();
        GUIStyle txt, big;
        Texture2D white;

        struct Solution { public bool ok, steil; public float az, el, speed, tof, dist, dy; public int zone; public string why, note; }

        public static void Toggle(TankController tc, Camera cam)
        {
            if (Active) { inst.Exit(); return; }
            if (inst == null) inst = cam.gameObject.AddComponent<ArtilleryMode>();
            inst.Enter(tc, cam);
        }

        void Enter(TankController t, Camera c)
        {
            tc = t; cam = c; enabled = true;
            center = new Vector3(tc.transform.position.x, 0f, tc.transform.position.z);
            savedFov = cam.fieldOfView; savedFar = cam.farClipPlane;
            cam.orthographic = true; cam.farClipPlane = 4000f;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            tc.artilleryAim = true;
            if (target.HasValue) Solve();
        }

        /// <summary>Artillerie-Karte schliessen (z. B. Reiter "Karte" oder Taste M).</summary>
        public static void Close() { if (Active) inst.Exit(); }

        void Exit()
        {
            if (tc != null) tc.artilleryAim = false;
            if (cam != null) { cam.orthographic = false; cam.fieldOfView = savedFov; cam.farClipPlane = savedFar; }
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            tc = null; enabled = false;
        }

        void LateUpdate()
        {
            if (tc == null) return;
            if (tc.destroyed) { Exit(); return; }

            // Karte bedienen
            float sc = Inp.Scroll;
            if (sc != 0f) orthoSize = Mathf.Clamp(orthoSize * (sc > 0f ? 0.8f : 1.25f), 60f, 6000f);
            if (Inp.LookHeld)
            {
                Vector2 d = Inp.MouseDelta * 10f;   // Pixel
                float mPerPx = orthoSize * 2f / Screen.height;
                center -= new Vector3(d.x, 0f, d.y) * mPerPx;
            }
            cam.orthographicSize = orthoSize;
            cam.transform.SetPositionAndRotation(center + Vector3.up * 2000f, Quaternion.Euler(90f, 0f, 0f));   // Norden oben

            if (Inp.FireDown && Inp.MousePos.y < Screen.height - 60f * GameSettings.UiFactor)   // oben liegen die Reiter
            {
                Vector2 mp = Inp.MousePos;
                Ray r = cam.ScreenPointToRay(new Vector3(mp.x, mp.y, 0f));
                if (Physics.Raycast(r, out RaycastHit hit, 5000f, ~0, QueryTriggerInteraction.Ignore)) { target = hit.point; Solve(); }
            }
            if (Inp.Down(KeyCode.Tab)) { high = !high; if (target.HasValue) Solve(); }
            if (target.HasValue && Time.frameCount % 15 == 0) Solve();   // Fahrzeug/Turm bewegt sich minimal: nachrechnen

            // Turm richten: Zielpunkt weit entlang der berechneten Schussrichtung
            if (sol.ok)
            {
                Vector3 dir = Quaternion.Euler(-sol.el, sol.az, 0f) * Vector3.forward;
                tc.aimPoint = tc.parts.gunPivot.position + dir * 3000f;
                tc.artySpeed = sol.speed;
                if (Inp.Down(KeyCode.Space) && Aligned(dir) && tc.ReadyToFire)
                {
                    tc.ArtilleryFire();
                    inFlight.Add(new Pending { at = target.Value, impactTime = Time.time + sol.tof });
                }
            }
            for (int i = inFlight.Count - 1; i >= 0; i--)
                if (Time.time >= inFlight[i].impactTime) { impacts.Add(inFlight[i]); inFlight.RemoveAt(i); }
            impacts.RemoveAll(p => Time.time - p.impactTime > 20f);
        }

        // ---- fuer den Leistungstest ----
        public static void BenchSetTarget(Vector3 p, bool steil) { if (!Active) return; inst.target = p; inst.high = steil; inst.Solve(); }
        public static bool BenchReady => Active && inst.sol.ok && inst.tc.ReadyToFire && inst.Aligned(Quaternion.Euler(-inst.sol.el, inst.sol.az, 0f) * Vector3.forward);
        public static string BenchInfo => Active && inst.sol.ok ? $"Entfernung {inst.sol.dist:0} m, Erhoehung {inst.sol.el:0.0} Grad, Zone {inst.sol.zone}, Flugzeit {inst.sol.tof:0.0} s" : "keine Loesung";
        public static float BenchTof => Active ? inst.sol.tof : 0f;
        public static void BenchFire() { if (BenchReady) { inst.tc.ArtilleryFire(); inst.inFlight.Add(new Pending { at = inst.target.Value, impactTime = Time.time + inst.sol.tof }); } }

        // Gerichtet = Rohr weniger als 0,05 Grad neben der Soll-Richtung UND seit 0,5 s ruhig.
        // (Bei schwacher Treibladung macht schon 0,3 Grad ~100 m Weitenfehler - darum so streng.)
        float stableSince = -1f;
        bool Aligned(Vector3 dir)
        {
            bool close = Vector3.Angle(tc.parts.gunPivot.forward, dir) < 0.05f;
            if (!close) { stableSince = -1f; return false; }
            if (stableSince < 0f) stableSince = Time.time;
            return Time.time - stableSince > 0.5f;
        }

        // ---------------- Feuerleitrechner ----------------
        void Solve()
        {
            sol = new Solution();
            if (!target.HasValue || tc == null) return;
            Vector3 o = tc.parts.gunPivot.position, t = target.Value;
            Vector3 flat = new Vector3(t.x - o.x, 0f, t.z - o.z);
            sol.dist = flat.magnitude; sol.dy = t.y - o.y;
            sol.az = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            float maxEl = tc.spec.maxElevation, minEl = tc.spec.minElevation;
            if (high && TrySolve(true, minEl, maxEl)) return;
            // Steilfeuer geht nicht (z. B. Ziel zu nah: das Rohr kommt nur bis 65 Grad) -> Flachfeuer
            if (TrySolve(false, minEl, maxEl)) { if (high) sol.note = "Steilfeuer nicht moeglich (Ziel zu nah, unter 4 km) - Flachfeuer" + (sol.note != null ? "\n" + sol.note : ""); return; }
            sol.why = "ausser Reichweite";
        }

        bool TrySolve(bool steil, float minEl, float maxEl)
        {
            // kleinste Ladung zuerst (hoechster Bogen); Ladungen, deren Bahn in einen Huegel/Wald fuehrt, ueberspringen
            Solution blocked = new Solution();
            for (int z = 0; z < ZoneSpeed.Length; z++)
            {
                float v = ZoneSpeed[z];
                float el = steil ? SolveHigh(v, sol.dist, sol.dy, Mathf.Max(45f, minEl), maxEl) : SolveLow(v, sol.dist, sol.dy, minEl, Mathf.Min(45f, maxEl));
                if (float.IsNaN(el)) continue;
                var s = sol; s.ok = true; s.el = el; s.speed = v; s.zone = z + 1; s.steil = steil;
                Simulate(v, el, sol.dy, out _, out s.tof);
                if (PathBlocked(v, el, out float at))
                {
                    if (!blocked.ok) { blocked = s; blocked.note = $"Achtung: Huegel/Wald in der Flugbahn ({at:0} m vor dem Geschuetz) - Ziel so nicht erreichbar"; }
                    continue;
                }
                sol = s;
                return true;
            }
            if (blocked.ok && !steil) { sol = blocked; return true; }   // Flachfeuer: trotzdem anzeigen, aber mit Warnung
            return false;
        }

        /// <summary>Fliegt die Granate auf dem Weg ins Gelaende oder in Baeume? at = Entfernung des Hindernisses.</summary>
        bool PathBlocked(float v, float elDeg, out float at)
        {
            at = 0f;
            var d = tc.Ammo;
            Vector3 o = tc.parts.gunPivot.position;
            Vector3 hdir = Quaternion.Euler(0f, sol.az, 0f) * Vector3.forward;
            Vector2 p = Vector2.zero, vel = new Vector2(Mathf.Cos(elDeg * Mathf.Deg2Rad), Mathf.Sin(elDeg * Mathf.Deg2Rad)) * v;
            float area = Mathf.PI * Mathf.Pow(d.caliberMm / 2000f, 2f), t = 0f, dt = 0.02f;
            Vector3 last = o + hdir * 8f + Vector3.up * 8f * Mathf.Tan(elDeg * Mathf.Deg2Rad);   // erst ausserhalb des eigenen Fahrzeugs pruefen
            int step = 0;
            while (t < 120f)
            {
                float sp = vel.magnitude;
                Vector2 acc = new Vector2(0f, Physics.gravity.y) - vel / sp * (0.5f * Ballistics.AirDensity * sp * sp * d.dragCoefficient * area / d.massKg);
                p += vel * dt; vel += acc * dt; t += dt;
                if (p.x < 8f) continue;
                if (++step % 8 != 0 && !(vel.y < 0f && p.y <= sol.dy)) continue;
                Vector3 now = o + hdir * p.x + Vector3.up * p.y;
                Vector3 seg = now - last;
                if (seg.sqrMagnitude > 0.01f && Physics.Raycast(last, seg.normalized, out RaycastHit hit, seg.magnitude, ~0, QueryTriggerInteraction.Ignore))
                {
                    bool own = hit.collider.GetComponentInParent<TankController>() == tc;
                    bool nearTarget = target.HasValue && Vector3.Distance(hit.point, target.Value) < 30f;
                    if (!own && !nearTarget) { at = new Vector2(hit.point.x - o.x, hit.point.z - o.z).magnitude; return true; }
                }
                last = now;
                if (vel.y < 0f && p.y <= sol.dy) return false;   // am Ziel angekommen
            }
            return false;
        }

        /// <summary>Flugbahn in der Senkrechten (mit Luftwiderstand): horizontale Weite beim Durchgang durch Hoehe dy (absteigend).</summary>
        float Simulate(float v, float elDeg, float dy, out bool reached, out float tof)
        {
            var d = tc.Ammo;
            Vector2 p = Vector2.zero, vel = new Vector2(Mathf.Cos(elDeg * Mathf.Deg2Rad), Mathf.Sin(elDeg * Mathf.Deg2Rad)) * v;
            float area = Mathf.PI * Mathf.Pow(d.caliberMm / 2000f, 2f), t = 0f, dt = 0.02f;
            float maxY = 0f;
            while (t < 120f)
            {
                float sp = vel.magnitude;
                Vector2 acc = new Vector2(0f, Physics.gravity.y) - vel / sp * (0.5f * Ballistics.AirDensity * sp * sp * d.dragCoefficient * area / d.massKg);
                Vector2 np = p + vel * dt; vel += acc * dt; t += dt;
                maxY = Mathf.Max(maxY, np.y);
                if (vel.y < 0f && np.y <= dy && p.y > dy) { float k = (p.y - dy) / Mathf.Max(p.y - np.y, 1e-4f); reached = true; tof = t - dt + dt * k; return Mathf.Lerp(p.x, np.x, k); }
                p = np;
            }
            reached = false; tof = t; return p.x;
        }

        float RangeAt(float v, float el, float dy) { float r = Simulate(v, el, dy, out bool ok, out _); return ok ? r : -1f; }

        float SolveLow(float v, float dist, float dy, float lo, float hi)
        {
            // Weite steigt mit dem Winkel (bis ~45 Grad)
            float rHi = RangeAt(v, hi, dy);
            if (rHi < dist) return float.NaN;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) / 2f;
                if (RangeAt(v, mid, dy) < dist) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2f;
        }

        float SolveHigh(float v, float dist, float dy, float lo, float hi)
        {
            // Steilfeuer: Weite faellt mit dem Winkel (ab 45 Grad)
            float rLo = RangeAt(v, lo, dy), rHi = RangeAt(v, hi, dy);
            if (rLo < dist || (rHi > dist && rHi > 0f)) return float.NaN;   // zu weit / zu nah fuer diese Ladung
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) / 2f;
                if (RangeAt(v, mid, dy) > dist) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2f;
        }

        // ---------------- Anzeige ----------------
        void OnGUI()
        {
            if (tc == null) return;
            DrawMain();
            if (tc != null) TeamHud.DrawArtilleryOverlay(cam, tc);   // Panzer als Punkte + Reiter Karte/Artillerie
        }

        void DrawMain()
        {
            if (tc == null) return;
            if (txt == null)
            {
                txt = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height / 62f), richText = true };
                txt.normal.textColor = Color.white;
                big = new GUIStyle(txt) { fontSize = Mathf.RoundToInt(Screen.height / 40f), fontStyle = FontStyle.Bold };
                white = Texture2D.whiteTexture;
            }
            // Raster: 100 m, alle 500 m kraeftiger mit Beschriftung
            float step = orthoSize > 2500f ? 1000f : orthoSize > 700f ? 500f : 100f;
            Vector3 bl = cam.ScreenToWorldPoint(new Vector3(0, 0, 10)), tr = cam.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, 10));
            for (float x = Mathf.Floor(bl.x / step) * step; x <= tr.x; x += step)
            {
                bool major = Mathf.Abs(x % 500f) < 1f;
                float sx = cam.WorldToScreenPoint(new Vector3(x, 0, 0)).x;
                Rect(sx, 0, major ? 2 : 1, Screen.height, new Color(1, 1, 1, major ? 0.35f : 0.15f));
                if (major) GUI.Label(new UnityEngine.Rect(sx + 3, 4, 80, 22), $"{x:0}", txt);
            }
            for (float z = Mathf.Floor(bl.z / step) * step; z <= tr.z; z += step)
            {
                bool major = Mathf.Abs(z % 500f) < 1f;
                float sy = Screen.height - cam.WorldToScreenPoint(new Vector3(0, 0, z)).y;
                Rect(0, sy, Screen.width, major ? 2 : 1, new Color(1, 1, 1, major ? 0.35f : 0.15f));
                if (major) GUI.Label(new UnityEngine.Rect(4, sy + 2, 80, 22), $"{z:0}", txt);
            }
            // eigenes Fahrzeug (Pfeil in Fahrtrichtung) und Rohrrichtung
            Vector2 me = Screen2(tc.transform.position);
            Mark(me, 12f, new Color(0.3f, 0.6f, 1f));
            Vector3 gf = tc.parts.gunPivot.forward; gf.y = 0f;
            Vector2 gEnd = Screen2(tc.transform.position + gf.normalized * orthoSize * 0.15f);
            Line(me, gEnd, new Color(0.3f, 0.6f, 1f), 2f);
            // Ziel, Verbindungslinie
            if (target.HasValue)
            {
                Vector2 tp = Screen2(target.Value);
                Line(me, tp, new Color(1f, 0.85f, 0.2f, 0.8f), 1.5f);
                Rect(tp.x - 14, tp.y - 1, 28, 2, Color.red); Rect(tp.x - 1, tp.y - 14, 2, 28, Color.red);
            }
            foreach (var p in inFlight) { Vector2 q = Screen2(p.at); Mark(q, 8f, new Color(1f, 0.6f, 0.1f)); }
            foreach (var p in impacts) { Vector2 q = Screen2(p.at); float a = 1f - (Time.time - p.impactTime) / 20f; Mark(q, 10f + (Time.time - p.impactTime) * 3f, new Color(1f, 0.2f, 0.1f, a)); }

            // Feuerleit-Daten
            string s = "<b>ARTILLERIE - Feuerleitrechner</b>\n";
            if (!target.HasValue) s += "Linksklick: Ziel auf der Karte setzen\n";
            else if (!sol.ok) s += $"Entfernung {sol.dist:0} m  -  <color=#f66>{sol.why}</color>\n";
            else
            {
                float mils = Mathf.Repeat(sol.az, 360f) * 6400f / 360f;
                bool aligned = Aligned(Quaternion.Euler(-sol.el, sol.az, 0f) * Vector3.forward);
                s += $"Entfernung {sol.dist:0} m   Hoehe {sol.dy:+0;-0} m\n" +
                     $"Seitenrichtung {mils:0000} Strich ({Mathf.Repeat(sol.az, 360f):0.0}°)\n" +
                     $"Rohrerhoehung {sol.el:0.0}°   {(sol.steil ? "Steilfeuer" : "Flachfeuer")}\n" +
                     (sol.note != null ? $"<color=#fd6>{sol.note}</color>\n" : "") +
                     $"Treibladung Zone {sol.zone} ({sol.speed:0} m/s)   Flugzeit {sol.tof:0.0} s\n" +
                     (tc.ReadyToFire ? (aligned ? "<color=#7f7>FEUERBEREIT - Leertaste</color>" : "<color=#fd6>Turm richtet ...</color>") :
                                       $"<color=#fd6>Laedt ... {tc.reloadLeft:0.0} s</color>") + "\n";
            }
            foreach (var p in inFlight) s += $"<color=#fa4>Granate unterwegs - Einschlag in {p.impactTime - Time.time:0.0} s</color>\n";
            s += "\n<size=12>Linksklick Ziel   Tab Steil/Flach   Leertaste Feuer   Mausrad Zoom   rechte Maus verschieben   M zurueck</size>";
            float w = Screen.height * 0.5f;
            GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(new UnityEngine.Rect(10, 10, w, txt.CalcHeight(new GUIContent(s), w) + 16), white); GUI.color = Color.white;
            GUI.Label(new UnityEngine.Rect(18, 16, w - 16, 600), s, txt);
        }

        Vector2 Screen2(Vector3 w) { Vector3 s = cam.WorldToScreenPoint(w); return new Vector2(s.x, Screen.height - s.y); }
        void Rect(float x, float y, float w, float h, Color c) { GUI.color = c; GUI.DrawTexture(new UnityEngine.Rect(x, y, w, h), white); GUI.color = Color.white; }
        void Mark(Vector2 p, float r, Color c)
        {
            Rect(p.x - r, p.y - 1, 2 * r, 2, c); Rect(p.x - 1, p.y - r, 2, 2 * r, c);
            Rect(p.x - r, p.y - r, 2 * r, 2, c); Rect(p.x - r, p.y + r - 2, 2 * r, 2, c); Rect(p.x - r, p.y - r, 2, 2 * r, c); Rect(p.x + r - 2, p.y - r, 2, 2 * r, c);
        }
        void Line(Vector2 a, Vector2 b, Color c, float width)
        {
            Vector2 d = b - a; float len = d.magnitude; if (len < 1f) return;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            Rect(a.x, a.y - width / 2f, len, width, c);
            GUI.matrix = m;
        }
    }
}
