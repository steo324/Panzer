using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>Pings (mittlere Maustaste): Markierung fuer das eigene Team, 12 s sichtbar (im Bild und auf der Minikarte).</summary>
    public static class Pings
    {
        public struct Ping { public Vector3 pos; public int team; public string from; public float time; }
        public static readonly List<Ping> List = new List<Ping>();
        public const float Life = 12f;
        static float lastSend = -9f;

        public static void Add(Vector3 p, int team, string from)
        {
            List.RemoveAll(x => Time.time - x.time > Life);
            if (List.Count > 30) List.RemoveAt(0);
            List.Add(new Ping { pos = p, team = team, from = from, time = Time.time });
            if (team == TankController.LocalTeam && !NetSync.Headless) RangeLog.AddLocal($"Ping von {from}");
        }

        /// <summary>Eigener Ping (an die Stelle im Fadenkreuz). Hoechstens einer pro Sekunde.</summary>
        public static void Send(TankController tc, Vector3 p)
        {
            if (Time.time - lastSend < 1f) return;
            lastSend = Time.time;
            if (tc.net != null && NetSync.Active) tc.net.RequestPing(p);
            else Add(p, tc.team, tc.DisplayName);
        }
    }

    /// <summary>
    /// Team-Anzeige: Minikarte (unten rechts), Namen ueber den Panzern (blau = eigenes Team, rot = Gegner)
    /// und Pings im Bild. Gegner erscheinen nur, wenn man sie sieht (freie Sichtlinie) - kein Blick durch Huegel.
    /// </summary>
    public class TeamHud : MonoBehaviour
    {
        readonly List<TankController> tanks = new List<TankController>();
        readonly Dictionary<TankController, float> seen = new Dictionary<TankController, float>();   // zuletzt gesehen (Zeit)
        float refresh, visTimer;
        Texture2D dot, ring, fallback; GameMap fallbackFor;
        GUIStyle tag, tagSmall, mm;
        static readonly Color Blue = new Color(0.40f, 0.68f, 1f), Red = new Color(1f, 0.36f, 0.30f), Gray = new Color(0.65f, 0.65f, 0.65f), PingCol = new Color(1f, 0.85f, 0.2f);
        public static float MinimapRange = 1200f;

        void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.InGame) return;
            var me = gm.Player;
            if (me != null && me.isPlayer && !me.destroyed && Inp.PingDown && Cursor.lockState == CursorLockMode.Locked)
                Pings.Send(me, me.aimPoint);
            refresh -= Time.unscaledDeltaTime;
            if (refresh <= 0f)
            {
                refresh = 0.5f; tanks.Clear();
                tanks.AddRange(FindObjectsByType<TankController>(FindObjectsSortMode.None));
            }
            visTimer -= Time.unscaledDeltaTime;
            if (visTimer <= 0f && Camera.main != null)
            {
                visTimer = 0.25f;
                Vector3 eye = Camera.main.transform.position;
                foreach (var t in tanks)
                {
                    if (t == null || t == me || t.IsFriendly) continue;
                    Vector3 c = t.transform.position + Vector3.up * 1.6f;
                    float d = Vector3.Distance(eye, c);
                    if (d > 4000f) continue;
                    bool vis = true;
                    foreach (var h in Physics.RaycastAll(eye, (c - eye) / d, d - 2f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        var owner = h.collider.GetComponentInParent<TankController>();
                        if (owner == me || owner == t) continue;
                        if (h.collider.GetComponent<ArmorPlate>() != null) continue;   // andere Panzer verdecken nicht ganz
                        vis = false; break;
                    }
                    if (vis) seen[t] = Time.time;
                }
            }
        }

        bool Visible(TankController t, float memory) => t.IsFriendly || (seen.TryGetValue(t, out float s) && Time.time - s < memory);

        void Styles()
        {
            if (tag != null) return;
            dot = Circle(32, true); ring = Circle(48, false);
            tag = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true, fontSize = 15 };
            tagSmall = new GUIStyle(tag) { fontSize = 12, fontStyle = FontStyle.Normal };
            mm = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        static Texture2D Circle(int size, bool filled)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size / 2f - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
                    float a = filled ? Mathf.Clamp01(r - d + 0.5f) : Mathf.Clamp01(1.8f - Mathf.Abs(d - (r - 2f)));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        void OnGUI()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.InGame || SpawnSelect.IsOpen || Camera.main == null) return;
            Styles();
            var me = gm.Player;
            var cam = Camera.main;
            float k = GameSettings.UiFactor;
            GUI.matrix = Matrix4x4.identity;
            if (!ArtilleryMode.Active)
            {
                DrawTags(cam, me, k);
                DrawPings(cam, k);
            }
            if (me != null && !ArtilleryMode.Active) DrawMinimap(gm.CurrentMap, me, k);
        }

        // ---------------- Namen ueber den Panzern ----------------
        void DrawTags(Camera cam, TankController me, float k)
        {
            tag.fontSize = Mathf.RoundToInt(15 * k); tagSmall.fontSize = Mathf.RoundToInt(12 * k);
            Vector3 eye = cam.transform.position;
            foreach (var t in tanks)
            {
                if (t == null || t == me) continue;
                float d = Vector3.Distance(eye, t.transform.position);
                bool friend = t.IsFriendly;
                if (!friend && (d > 3000f || !Visible(t, 0.6f))) continue;
                Vector3 top = t.transform.position + t.transform.up * (t.spec.clearance + t.spec.hullHeight + t.spec.turretHeight + 1.4f);
                Vector3 sp = cam.WorldToScreenPoint(top);
                if (sp.z <= 0f) continue;
                float y = Screen.height - sp.y;
                Color c = t.destroyed ? Gray : friend ? Blue : Red;
                string name = t.team == TankController.TeamTarget && string.IsNullOrEmpty(t.playerName) ? $"Ziel: {t.spec.name}" : t.DisplayName;
                if (t.destroyed) name += " (aus)";
                float w = 260f * k, h = 22f * k;
                GUI.color = new Color(0f, 0f, 0f, 0.75f);
                GUI.Label(new Rect(sp.x - w / 2f + 1, y - h + 1, w, h), name, tag);
                GUI.color = c;
                GUI.Label(new Rect(sp.x - w / 2f, y - h, w, h), name, tag);
                GUI.Label(new Rect(sp.x - w / 2f, y - 2f, w, h * 0.8f), d < 1000f ? $"{d:0} m" : $"{d / 1000f:0.0} km", tagSmall);
                GUI.color = Color.white;
            }
        }

        // ---------------- Pings im Bild ----------------
        void DrawPings(Camera cam, float k)
        {
            Pings.List.RemoveAll(p => Time.time - p.time > Pings.Life);
            Vector3 eye = cam.transform.position;
            foreach (var p in Pings.List)
            {
                if (p.team != TankController.LocalTeam) continue;
                Vector3 sp = cam.WorldToScreenPoint(p.pos + Vector3.up * 2f);
                if (sp.z <= 0f) continue;
                float age = Time.time - p.time, a = Mathf.Clamp01((Pings.Life - age) / 2f);
                float pulse = 1f + 0.25f * Mathf.Sin(age * 8f);
                float y = Screen.height - sp.y, s = 22f * k * pulse;
                GUI.color = new Color(PingCol.r, PingCol.g, PingCol.b, a);
                GUI.DrawTexture(new Rect(sp.x - s / 2f, y - s / 2f, s, s), ring);
                GUI.DrawTexture(new Rect(sp.x - 4f * k, y - 4f * k, 8f * k, 8f * k), dot);
                float d = Vector3.Distance(eye, p.pos);
                GUI.Label(new Rect(sp.x - 150f * k, y + s / 2f, 300f * k, 20f * k), $"{p.from}: {(d < 1000f ? $"{d:0} m" : $"{d / 1000f:0.0} km")}", tagSmall);
                GUI.color = Color.white;
            }
        }

        // ---------------- Minikarte ----------------
        void DrawMinimap(GameMap map, TankController me, float k)
        {
            if (map == null) return;
            Texture2D tex = map.Overview != null ? map.Overview : Fallback(map);
            float E = map.Extent, R = Mathf.Min(E, MinimapRange);
            float size = 250f * k, pad = 12f * k;
            var r = new Rect(Screen.width - size - pad, Screen.height - size - pad, size, size);
            Vector3 mp = me.transform.position;
            // Rahmen + Ausschnitt um den eigenen Panzer (Norden oben)
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float u = (mp.x + E) / (2f * E), v = (mp.z + E) / (2f * E), hw = R / (2f * E);
            if (tex != null) GUI.DrawTextureWithTexCoords(r, tex, new Rect(u - hw, v - hw, hw * 2f, hw * 2f));
            System.Func<Vector3, Vector2> toMap = w => new Vector2(r.center.x + (w.x - mp.x) / R * size / 2f, r.center.y - (w.z - mp.z) / R * size / 2f);
            float ds = 9f * k;
            foreach (var t in tanks)
            {
                if (t == null || t == me) continue;
                if (!Visible(t, 6f)) continue;   // Gegner nur, wenn kuerzlich gesehen
                Vector2 q = toMap(t.transform.position);
                if (!r.Contains(q)) continue;
                GUI.color = t.destroyed ? Gray : t.IsFriendly ? Blue : Red;
                GUI.DrawTexture(new Rect(q.x - ds / 2f, q.y - ds / 2f, ds, ds), dot);
            }
            foreach (var p in Pings.List)
            {
                if (p.team != TankController.LocalTeam) continue;
                Vector2 q = toMap(p.pos);
                if (!r.Contains(q)) continue;
                float s = (14f + 4f * Mathf.Sin((Time.time - p.time) * 8f)) * k;
                GUI.color = PingCol;
                GUI.DrawTexture(new Rect(q.x - s / 2f, q.y - s / 2f, s, s), ring);
            }
            // eigener Panzer: weisser Punkt + Blickrichtung des Turms
            Vector2 c = r.center;
            Vector3 fwd = me.parts.turret != null ? me.parts.turret.forward : me.transform.forward;
            GUI.color = Color.white;
            for (int i = 1; i <= 4; i++)
            {
                Vector2 q = c + new Vector2(fwd.x, -fwd.z).normalized * i * 4f * k;
                GUI.DrawTexture(new Rect(q.x - 2f * k, q.y - 2f * k, 4f * k, 4f * k), dot);
            }
            GUI.DrawTexture(new Rect(c.x - ds * 0.65f, c.y - ds * 0.65f, ds * 1.3f, ds * 1.3f), dot);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(r.x, r.y + 2, r.width, 16 * k), "N", mm);
            GUI.Label(new Rect(r.x, r.yMax - 18 * k, r.width, 16 * k), $"{R * 2f / 1000f:0.#} km", mm);
            GUI.color = Color.white;
        }

        /// <summary>Draufsicht fuer Karten ohne eigene (Weite Ebene, Testgelaende): einmal aus Bodenart und Hoehe gerechnet.</summary>
        Texture2D Fallback(GameMap map)
        {
            if (fallback != null && fallbackFor == map) return fallback;
            const int N = 256;
            float E = map.Extent;
            var t = new Texture2D(N, N, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float wx = (x + 0.5f) / N * 2f * E - E, wz = (y + 0.5f) / N * 2f * E - E;
                    Color c;
                    switch (map.SurfaceAt(wx, wz))
                    {
                        case GroundTracks.Acker: c = new Color(0.80f, 0.72f, 0.44f); break;
                        case GroundTracks.Weg: c = new Color(0.84f, 0.78f, 0.62f); break;
                        case GroundTracks.Hart: c = new Color(0.62f, 0.60f, 0.56f); break;
                        default: c = new Color(0.50f, 0.62f, 0.34f); break;
                    }
                    float s = E / N * 2f;
                    float hx = map.GroundY(wx + s, wz) - map.GroundY(wx - s, wz), hz = map.GroundY(wx, wz + s) - map.GroundY(wx, wz - s);
                    c *= Mathf.Clamp(1f + (-hx + hz) * 0.12f, 0.6f, 1.3f);
                    px[y * N + x] = c;
                }
            t.SetPixels32(px); t.Apply(false);
            fallback = t; fallbackFor = map;
            return t;
        }
    }
}
