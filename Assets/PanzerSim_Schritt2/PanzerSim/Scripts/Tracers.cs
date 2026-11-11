using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PanzerSim
{
    /// <summary>
    /// Leuchtspuren: der brennende Leuchtsatz im Heck eines Geschosses. Gezeichnet als heller Kopf mit kurzem,
    /// nach hinten auslaufendem Schweif (so sieht das Auge/eine Kamera eine schnell fliegende Leuchtspur).
    /// Jedes Geschoss meldet sich pro Bild mit Streak(); alles zusammen wird in einem Zeichenaufruf gemalt
    /// (additiv, Farben ueber 1 -> leuchten mit Bloom).
    /// </summary>
    public class Tracers : MonoBehaviour
    {
        struct Item { public Vector3 head, dir; public float len, width; public Color color; }
        static readonly List<Item> items = new List<Item>();
        static Tracers instance;
        public static int DrawnTotal, AddedTotal;   // fuer Tests
        Mesh mesh; Material mat;
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Color> c = new List<Color>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();

        /// <summary>Leuchtspur fuer dieses Bild. head = Geschossposition, dir = Flugrichtung, len = Schweiflaenge (m),
        /// width = Breite (m, waechst mit der Entfernung automatisch mit, damit sie sichtbar bleibt), color = HDR-Farbe.</summary>
        public static void Streak(Vector3 head, Vector3 dir, float len, float width, Color color)
        {
            if (NetSync.Headless) return;
            if (instance == null)
            {
                var go = new GameObject("Leuchtspuren");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<Tracers>();
            }
            AddedTotal++;
            items.Add(new Item { head = head, dir = dir.normalized, len = len, width = width, color = color });
        }

        void Awake()
        {
            mesh = new Mesh { name = "Leuchtspuren" };
            mesh.MarkDynamic();
            var sh = Shader.Find("PanzerSim/Partikel");
            if (sh == null) sh = Resources.Load<Shader>("Shaders/Partikel");
            mat = new Material(sh) { name = "Leuchtspur", renderQueue = 3100 };
            mat.SetFloat("_SrcBlend", 1f); mat.SetFloat("_DstBlend", 1f); mat.SetFloat("_Lit", 0f);   // additiv
            mat.SetTexture("_BaseMap", GlowTexture());
        }

        /// <summary>Weicher Leuchtfleck: u quer (Mitte hell), v laengs (0 = Schweifende, 1 = Kopf).</summary>
        static Texture2D GlowTexture()
        {
            const int W = 32, H = 128;   // unten (v 0..0,5): Schweif, oben (v 0,5..1): runder Lichtpunkt
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Leuchtspur" };
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (y >= H / 2)
                    {
                        float dx = (x + 0.5f) / W - 0.5f, dy = (y - H / 2 + 0.5f) / (H / 2) - 0.5f;
                        float rr = (dx * dx + dy * dy) * 4f;   // 0 Mitte .. 1 Rand
                        float glow = Mathf.Exp(-rr * 3.2f) * (1f - Mathf.Clamp01(rr)), hot = Mathf.Exp(-rr * 18f);
                        px[y * W + x] = new Color(glow + hot * 0.5f, glow + hot * 0.5f, glow + hot * 0.5f, glow);
                        continue;
                    }
                    float u = Mathf.Abs((x + 0.5f) / W - 0.5f) * 2f, f = (y + 0.5f) / (H / 2);
                    float across = Mathf.Exp(-u * u * 7f);
                    float along = Mathf.Pow(f, 1.6f);                        // Schweif laeuft aus
                    float head = Mathf.Exp(-Mathf.Pow((1f - f) * 9f, 2f));   // heller Kopf vorne
                    float a = across * Mathf.Clamp01(along * 0.65f + head);
                    float core = Mathf.Exp(-u * u * 40f) * Mathf.Clamp01(along + head);   // weisser Kern
                    px[y * W + x] = new Color(a + core * 0.35f, a + core * 0.35f, a + core * 0.35f, a);
                }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || items.Count == 0) { items.Clear(); return; }
            Vector3 cp = cam.transform.position;
            float pxAngle = cam.fieldOfView * Mathf.Deg2Rad / Mathf.Max(Screen.height, 1);   // Winkel eines Bildpunkts
            v.Clear(); c.Clear(); uv.Clear(); t.Clear();
            foreach (var it in items)
            {
                float dist = Vector3.Distance(cp, it.head);
                // mindestens ~2,5 Bildpunkte breit, sonst verschwindet sie in der Ferne (und im Zielfernrohr bleibt sie fein)
                float w = Mathf.Max(it.width, dist * pxAngle * 3.5f) * 0.5f;
                Vector3 tail = it.head - it.dir * it.len;
                Vector3 toCam = (cp - it.head).normalized;
                Vector3 side = Vector3.Cross(it.dir, toCam);
                if (side.sqrMagnitude < 1e-6f) side = cam.transform.right;   // fliegt genau auf die Kamera zu
                side = side.normalized * w;
                Vector3 front = it.head + it.dir * w;   // Kopf etwas runder
                int i0 = v.Count;
                v.Add(tail - side); v.Add(tail + side); v.Add(front - side); v.Add(front + side);
                uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(0f, 0.49f)); uv.Add(new Vector2(1f, 0.49f));
                c.Add(it.color); c.Add(it.color); c.Add(it.color); c.Add(it.color);
                t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0 + 3);

                // Gluehender Kopf als runder Lichtpunkt zur Kamera hin: von hinten/vorne sieht man eine Leuchtspur
                // verkuerzt nur als Punkt - der muss deutlich sichtbar sein (mind. ~7 Bildpunkte)
                float hs = Mathf.Max(it.width * 3f, dist * pxAngle * 16f) * 0.5f;
                Vector3 r = cam.transform.right * hs, u = cam.transform.up * hs;
                int h0 = v.Count;
                v.Add(it.head - r - u); v.Add(it.head + r - u); v.Add(it.head - r + u); v.Add(it.head + r + u);
                uv.Add(new Vector2(0f, 0.51f)); uv.Add(new Vector2(1f, 0.51f)); uv.Add(new Vector2(0f, 1f)); uv.Add(new Vector2(1f, 1f));   // oberer Teil der Textur: runder Punkt
                Color hc = it.color; hc.a = 1f;
                c.Add(hc); c.Add(hc); c.Add(hc); c.Add(hc);
                t.Add(h0); t.Add(h0 + 2); t.Add(h0 + 1); t.Add(h0 + 1); t.Add(h0 + 2); t.Add(h0 + 3);
            }
            DrawnTotal += items.Count;
            items.Clear();
            mesh.Clear();
            mesh.SetVertices(v); mesh.SetColors(c); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
            mesh.bounds = new Bounds(cp, Vector3.one * 20000f);
            Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0, null, 0, null, ShadowCastingMode.Off, false);
        }

        // ---------------- Farben ----------------
        /// <summary>Leuchtspur der Kanonengranaten (rot-orange).</summary>
        public static readonly Color Shell = new Color(3.2f, 1.0f, 0.3f, 1f);
        /// <summary>MG-Leuchtspur je Nation: deutsch gelb, sowjetisch gruen, amerikanisch rot.</summary>
        public static Color MgColor(SightType s) =>
            s == SightType.Soviet ? new Color(0.45f, 2.4f, 0.5f, 1f) :
            s == SightType.US ? new Color(2.4f, 0.45f, 0.3f, 1f) :
                                new Color(3.2f, 1.75f, 0.15f, 1f);
    }
}
