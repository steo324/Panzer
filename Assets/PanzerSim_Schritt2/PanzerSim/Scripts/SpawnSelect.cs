using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Startpunkt waehlen auf grossen Karten: Draufsicht mit Doerfern, Hoehenlinien und Startpunkten.
    /// Startpunkt anklicken (oder eine freie Stelle) und "Hier starten". Esc = bleiben, wo man ist.
    /// </summary>
    public class SpawnSelect : MonoBehaviour
    {
        static SpawnSelect inst;
        public static bool IsOpen => inst != null;

        GameMap map;
        System.Action<Vector2, float> onPick;
        int selected = 0;
        Vector2? custom;
        string msg = "";
        float lastClick; int lastClickIndex = -2;
        static Texture2D dot, ring;
        GUIStyle title, label, small, btn, btnSel, mapName;

        public static void Show(GameMap map, System.Action<Vector2, float> onPick)
        {
            if (inst != null) Destroy(inst.gameObject);
            var go = new GameObject("Startpunkt-Auswahl");
            inst = go.AddComponent<SpawnSelect>();
            inst.map = map; inst.onPick = onPick;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        /// <summary>Schliessen ohne Wahl (Panzer bleibt stehen, wo er ist).</summary>
        public static void Cancel()
        {
            if (inst == null) return;
            Destroy(inst.gameObject); inst = null;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }

        void Choose()
        {
            Vector2 p; float yaw;
            if (custom.HasValue) { p = custom.Value; yaw = Mathf.Atan2(-p.x, -p.y) * Mathf.Rad2Deg; }   // Blick zur Kartenmitte
            else { var s = map.SpawnPoints[selected]; p = s.xz; yaw = s.yaw; }
            var cb = onPick;
            Destroy(gameObject); inst = null;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            cb?.Invoke(p, yaw);
        }

        static Texture2D Circle(int size, bool filled)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size / 2f - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
                    float a = filled ? Mathf.Clamp01(r - d + 0.5f) : Mathf.Clamp01(1.6f - Mathf.Abs(d - (r - 2f)));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        void Styles()
        {
            if (title != null) return;
            dot = Circle(32, true); ring = Circle(48, false);
            title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, richText = true };
            title.normal.textColor = Color.white;
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = true };
            label.normal.textColor = new Color(0.92f, 0.92f, 0.88f);
            small = new GUIStyle(label) { fontSize = 12 };
            mapName = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            mapName.normal.textColor = new Color(0.15f, 0.08f, 0.05f);
            btn = new GUIStyle(GUI.skin.button) { fontSize = 15, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(12, 8, 4, 4) };
            btnSel = new GUIStyle(btn) { fontStyle = FontStyle.Bold };
            btnSel.normal.textColor = btnSel.hover.textColor = new Color(1f, 0.85f, 0.3f);
        }

        void OnGUI()
        {
            if (map == null || map.Overview == null) { Cancel(); return; }
            Styles();
            GUI.depth = -50;
            float k = GameSettings.UiFactor;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            float SW = Screen.width / k, SH = Screen.height / k;
            GUI.color = new Color(0.05f, 0.06f, 0.05f, 0.94f);
            GUI.DrawTexture(new Rect(0, 0, SW, SH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float size = Mathf.Min(SH - 130f, SW - 460f);
            var mr = new Rect(40f, 90f, size, size);
            GUI.Label(new Rect(40, 22, 1200, 44), $"STARTPUNKT WAEHLEN  <size=18><color=#bbb>{map.Name}, {map.Extent * 2f / 1000f:0} x {map.Extent * 2f / 1000f:0} km</color></size>", title);
            GUI.DrawTexture(mr, map.Overview);

            // Kilometer-Gitter
            float E = map.Extent;
            GUI.color = new Color(0f, 0f, 0f, 0.18f);
            for (float g = -E + 1000f; g < E; g += 1000f)
            {
                Vector2 a = ToScreen(mr, new Vector2(g, -E)), b = ToScreen(mr, new Vector2(-E, g));
                GUI.DrawTexture(new Rect(a.x, mr.y, 1f, mr.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(mr.x, b.y, mr.width, 1f), Texture2D.whiteTexture);
            }
            GUI.color = Color.white;
            GUI.Label(new Rect(mr.x + 4, mr.yMax - 20, 300, 20), "<color=#222>Gitter: 1 km   Norden oben</color>", small);

            // Ortsnamen
            IEnumerable<(string, Vector2)> names = map is HillsMap hm ? hm.Places() : map is RealMap rm ? rm.Places() : null;
            if (names != null)
                foreach (var (nm, c) in names)
                {
                    Vector2 s = ToScreen(mr, c);
                    GUI.Label(new Rect(s.x - 90, s.y - 26, 180, 18), nm, mapName);
                }

            // Startpunkte
            var sp = map.SpawnPoints;
            Vector2 mouse = Event.current.mousePosition;
            for (int i = 0; i < sp.Count; i++)
            {
                Vector2 s = ToScreen(mr, sp[i].xz);
                bool sel = !custom.HasValue && i == selected;
                GUI.color = sel ? new Color(1f, 0.8f, 0.15f) : new Color(1f, 1f, 1f, 0.95f);
                GUI.DrawTexture(new Rect(s.x - 8, s.y - 8, 16, 16), dot);
                GUI.color = new Color(0f, 0f, 0f, 0.9f);
                GUI.DrawTexture(new Rect(s.x - 12, s.y - 12, 24, 24), ring);
                if (sel) { GUI.color = new Color(1f, 0.8f, 0.15f); GUI.DrawTexture(new Rect(s.x - 18, s.y - 18, 36, 36), ring); }
                GUI.color = Color.white;
                GUI.Label(new Rect(s.x + 12, s.y - 10, 220, 22), $"<b>{i + 1}</b>  {sp[i].name}", small);
            }
            if (custom.HasValue)
            {
                Vector2 s = ToScreen(mr, custom.Value);
                GUI.color = new Color(1f, 0.3f, 0.2f);
                GUI.DrawTexture(new Rect(s.x - 9, s.y - 9, 18, 18), dot);
                GUI.DrawTexture(new Rect(s.x - 16, s.y - 16, 32, 32), ring);
                GUI.color = Color.white;
            }
            // Mausposition in km
            if (mr.Contains(mouse))
            {
                Vector2 w = ToWorld(mr, mouse);
                GUI.Label(new Rect(mr.xMax - 210, mr.y + 4, 205, 20), $"<color=#111>x {w.x / 1000f:+0.0;-0.0} km   y {w.y / 1000f:+0.0;-0.0} km</color>", small);
            }

            // Klicks auf die Karte
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && mr.Contains(e.mousePosition))
            {
                int hit = -1; float best = 16f;
                for (int i = 0; i < sp.Count; i++)
                {
                    float d = Vector2.Distance(ToScreen(mr, sp[i].xz), e.mousePosition);
                    if (d < best) { best = d; hit = i; }
                }
                if (hit >= 0)
                {
                    bool dbl = hit == lastClickIndex && Time.unscaledTime - lastClick < 0.4f;
                    selected = hit; custom = null; msg = "";
                    lastClick = Time.unscaledTime; lastClickIndex = hit;
                    if (dbl) { e.Use(); Choose(); return; }
                }
                else
                {
                    Vector2 w = ToWorld(mr, e.mousePosition);
                    if (map.CanSpawn(w.x, w.y)) { custom = w; msg = ""; lastClickIndex = -1; }
                    else msg = "<color=#f86>Dort kann kein Panzer stehen (Wald, Gebaeude, zu steil oder am Rand).</color>";
                }
                e.Use();
            }
            for (int i = 0; i < sp.Count && i < 9; i++)
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Alpha1 + i) { selected = i; custom = null; e.Use(); }
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { e.Use(); Choose(); return; }

            // rechte Spalte
            float px = mr.xMax + 30f, pw = SW - px - 30f;
            GUILayout.BeginArea(new Rect(px, 90f, pw, SH - 120f));
            GUILayout.Label("Startpunkte (Doppelklick oder Taste 1-8):", label);
            for (int i = 0; i < sp.Count; i++)
                if (GUILayout.Button($"{i + 1}   {sp[i].name}", !custom.HasValue && i == selected ? btnSel : btn, GUILayout.Height(34)))
                { selected = i; custom = null; msg = ""; }
            GUILayout.Space(10);
            GUILayout.Label("Oder eine freie Stelle auf der Karte anklicken (roter Punkt).", small);
            GUILayout.Space(8);
            string where = custom.HasValue ? $"Eigener Punkt: {custom.Value.x / 1000f:0.0} / {custom.Value.y / 1000f:0.0} km" : sp[selected].name;
            GUILayout.Label($"Gewaehlt: <b><color=#fd6>{where}</color></b>", label);
            if (msg.Length > 0) GUILayout.Label(msg, small);
            GUILayout.Space(10);
            if (GUILayout.Button("HIER STARTEN  (Enter)", btnSel, GUILayout.Height(52))) { Choose(); GUILayout.EndArea(); return; }
            if (GUILayout.Button("Abbrechen  (Esc)", btn, GUILayout.Height(36))) { Cancel(); GUILayout.EndArea(); return; }
            GUILayout.EndArea();
        }

        Vector2 ToScreen(Rect r, Vector2 w)
        {
            float E = map.Extent;
            return new Vector2(r.x + (w.x + E) / (2f * E) * r.width, r.yMax - (w.y + E) / (2f * E) * r.height);
        }
        Vector2 ToWorld(Rect r, Vector2 s)
        {
            float E = map.Extent;
            return new Vector2((s.x - r.x) / r.width * 2f * E - E, (r.yMax - s.y) / r.height * 2f * E - E);
        }
    }
}
