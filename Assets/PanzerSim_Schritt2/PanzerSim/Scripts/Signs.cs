using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Strassenschilder: Ortsschild (vorne Ortsname, hinten Ortsende mit rotem Balken), Wegweiser (Ziel und Entfernung, Pfeil),
    /// Verkehrszeichen (Vorfahrt achten, Vorfahrtstrasse, Stopp, Tempo 30/50/70/100). Alle Bilder liegen in einer Textur,
    /// die im Code gemalt wird (eigene Pixelschrift); alle Schilder einer Karte sind ein Netz je Block mit einem Material.
    /// Stil: 0 Deutschland (gelb), 1 Schweiz (blau/weiss), 2 alte Holzschilder (braun/weiss).
    /// </summary>
    public class SignKit
    {
        public const int VorfahrtAchten = 1, Vorfahrtstrasse = 2, Stopp = 3, Tempo30 = 4, Tempo50 = 5, Tempo70 = 6, Tempo100 = 7;
        const int W = 2048, Hh = 4096, Icon = 128, Split = 1400;   // unten Symbole und Ortsschilder, ab Split Wegweiser-Tafeln
        readonly Color32[] px = new Color32[W * Hh];
        readonly int style;
        int cx = 0, cy = Icon + 4, rowH = 0;   // Packer fuer Ortsschilder (ueber der Symbolzeile)
        int pcx = 0, pcy = Split, prow = 0;    // Packer fuer Wegweiser-Tafeln
        readonly Dictionary<string, Rect> cache = new Dictionary<string, Rect>();
        readonly List<Vector3> v = new List<Vector3>(); readonly List<Vector3> n = new List<Vector3>(); readonly List<Vector2> uv = new List<Vector2>(); readonly List<int> t = new List<int>();
        public int Signs;

        public SignKit(int style)
        {
            this.style = style;
            if (NetSync.Headless) return;
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(128, 128, 128, 255);
            DrawIcons();
        }

        // ================= Texturen =================
        Color32 Bg => style == 1 ? new Color32(20, 70, 160, 255) : style == 2 ? new Color32(95, 70, 45, 255) : new Color32(250, 205, 30, 255);
        Color32 Fg => style == 0 ? new Color32(15, 15, 15, 255) : new Color32(245, 245, 240, 255);

        void Fill(int x0, int y0, int w, int h, Color32 c) { for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++) if (x >= 0 && y >= 0 && x < W && y < Hh) px[y * W + x] = c; }

        /// <summary>Freie Zelle (w x h Pixel) holen.</summary>
        RectInt Alloc(int w, int h, bool plate = false)
        {
            if (plate)
            {
                if (pcx + w > W) { pcx = 0; pcy += prow + 4; prow = 0; }
                var rp = new RectInt(pcx, pcy, w, h);
                pcx += w + 4; prow = Mathf.Max(prow, h);
                return rp;
            }
            if (cx + w > W) { cx = 0; cy += rowH + 4; rowH = 0; }
            var r = new RectInt(cx, cy, w, h);
            cx += w + 4; rowH = Mathf.Max(rowH, h);
            return r;
        }
        static Rect UV(RectInt r) => new Rect((r.x + 1f) / W, (r.y + 1f) / Hh, (r.width - 2f) / W, (r.height - 2f) / Hh);
        static Rect IconUV(int i) => UV(new RectInt(i * (Icon + 4), 0, Icon, Icon));

        void DrawIcons()
        {
            // 0: grau (Pfosten, Rueckseiten)
            Fill(0, 0, Icon, Icon, new Color32(140, 142, 145, 255));
            Color32 red = new Color32(200, 25, 30, 255), white = new Color32(245, 245, 245, 255), yellow = new Color32(250, 200, 20, 255), black = new Color32(20, 20, 20, 255);
            for (int i = 1; i <= 7; i++)
            {
                int ox = i * (Icon + 4);
                for (int y = 0; y < Icon; y++)
                    for (int x = 0; x < Icon; x++)
                    {
                        float u = (x + 0.5f) / Icon * 2f - 1f, vv = (y + 0.5f) / Icon * 2f - 1f;   // -1..1, v nach oben
                        Color32 c = new Color32(140, 142, 145, 255);
                        switch (i)
                        {
                            case VorfahrtAchten:
                            {
                                // Dreieck mit Spitze nach unten: roter Rand, weiss innen
                                float d = TriDist(u, vv);
                                c = d < 0f ? (d > -0.17f ? red : white) : c;
                                break;
                            }
                            case Vorfahrtstrasse:
                            {
                                float d = Mathf.Abs(u) + Mathf.Abs(vv);
                                c = d < 0.98f ? (d > 0.80f ? white : d > 0.74f ? black : yellow) : c;
                                break;
                            }
                            case Stopp:
                            {
                                float d = Mathf.Max(Mathf.Max(Mathf.Abs(u), Mathf.Abs(vv)), (Mathf.Abs(u) + Mathf.Abs(vv)) * 0.7071f);
                                c = d < 0.97f ? (d > 0.90f ? white : red) : c;
                                break;
                            }
                            default:
                            {
                                float d = Mathf.Sqrt(u * u + vv * vv);
                                c = d < 0.97f ? (d > 0.72f ? red : white) : c;
                                break;
                            }
                        }
                        px[y * W + ox + x] = c;
                    }
                if (i == Stopp) DrawText(ox + 10, 40, Icon - 20, 48, "STOP", white);
                if (i >= Tempo30) DrawText(ox + 26, 34, Icon - 52, 60, i == Tempo30 ? "30" : i == Tempo50 ? "50" : i == Tempo70 ? "70" : "100", black);
            }
        }
        static float TriDist(float u, float v)
        {
            // gleichseitiges Dreieck, Spitze unten (v = -0.95), Oberkante bei v = 0.75
            float top = v - 0.75f;
            float s1 = (-0.95f - v) * 0.5f + u * 0.866f, s2 = (-0.95f - v) * 0.5f - u * 0.866f;
            return Mathf.Max(top, Mathf.Max(s1, s2) * 1.0f);
        }

        /// <summary>Ortsschild-Zellen (vorne Name, hinten Ortsende). false, wenn die Textur voll ist.</summary>
        bool PlaceCells(string name, string sub, out Rect front, out Rect back)
        {
            string key = "O|" + name + "|" + sub;
            front = back = default;
            if (cache.TryGetValue(key, out front) && cache.TryGetValue(key + "|b", out back)) return true;
            const int w = 280, h = 180;
            if (!Fits(w, h, 2)) return false;
            for (int side = 0; side < 2; side++)
            {
                var r = Alloc(w, h);
                Fill(r.x, r.y, w, h, Bg);
                Frame(r, 6, Fg);
                bool hasSub = !string.IsNullOrEmpty(sub);
                DrawText(r.x + 18, r.y + (hasSub ? 70 : 50), w - 36, hasSub ? 76 : 84, name, Fg);
                if (hasSub) DrawText(r.x + 26, r.y + 24, w - 52, 32, sub, Fg);
                if (side == 1)
                {
                    // roter Balken von links unten nach rechts oben
                    Color32 red = new Color32(200, 25, 30, 255);
                    float k = h / (float)w, norm = Mathf.Sqrt(1f + k * k);
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            float d = Mathf.Abs((x - w * 0.5f) * k - (y - h * 0.5f)) / norm;
                            if (d < 10f && x > 8 && x < w - 8 && y > 8 && y < h - 8) px[(r.y + y) * W + r.x + x] = red;
                        }
                }
                cache[side == 0 ? key : key + "|b"] = UV(r);
            }
            front = cache[key]; back = cache[key + "|b"];
            return true;
        }

        /// <summary>Wegweiser-Tafel mit Pfeil rechts (right = true) oder links. false, wenn die Textur voll ist.</summary>
        bool PlateCell(string text, bool right, out Rect c)
        {
            string key = "W|" + text + "|" + right;
            if (cache.TryGetValue(key, out c)) return true;
            const int w = 320, h = 60;
            if (!Fits(w, h, 1, true)) return false;
            var r = Alloc(w, h, true);
            Fill(r.x, r.y, w, h, Bg);
            Frame(r, 4, Fg);
            int ax = right ? r.x + w - 46 : r.x + 10;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < 36; x++)
                {
                    float fx = right ? x : 35 - x, fy = Mathf.Abs(y - h / 2f);
                    bool shaft = fx < 22 && fy < 6, head = fx >= 16 && fy < (36 - fx) * 1.1f;
                    if ((shaft || head) && y > 8 && y < h - 8) px[(r.y + y) * W + ax + x] = Fg;
                }
            DrawText(right ? r.x + 12 : r.x + 54, r.y + 14, w - 66, 32, text, Fg);
            c = UV(r); cache[key] = c;
            return true;
        }

        /// <summary>Passen noch n Zellen dieser Groesse in die Textur?</summary>
        bool Fits(int w, int h, int n, bool plate = false)
        {
            int x = plate ? pcx : cx, y = plate ? pcy : cy, rh = plate ? prow : rowH, lim = plate ? Hh : Split - 4;
            for (int i = 0; i < n; i++)
            {
                if (x + w > W) { x = 0; y += rh + 4; rh = 0; }
                if (y + h > lim) { full++; return false; }
                x += w + 4; rh = Mathf.Max(rh, h);
            }
            return true;
        }
        int full;

        void Frame(RectInt r, int th, Color32 c)
        {
            Fill(r.x + th, r.y + th, r.width - 2 * th, th / 2 + 2, c); Fill(r.x + th, r.y + r.height - th - th / 2 - 2, r.width - 2 * th, th / 2 + 2, c);
            Fill(r.x + th, r.y + th, th / 2 + 2, r.height - 2 * th, c); Fill(r.x + r.width - th - th / 2 - 2, r.y + th, th / 2 + 2, r.height - 2 * th, c);
        }

        // ---------------- Pixelschrift (5 x 7) ----------------
        static Dictionary<char, string[]> font;
        static void EnsureFont()
        {
            if (font != null) return;
            font = new Dictionary<char, string[]>();
            void G(char ch, params string[] rows) { font[ch] = rows; }
            G('A', " ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #");
            G('B', "#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### ");
            G('C', " ####", "#    ", "#    ", "#    ", "#    ", "#    ", " ####");
            G('D', "#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### ");
            G('E', "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####");
            G('F', "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    ");
            G('G', " ####", "#    ", "#    ", "#  ##", "#   #", "#   #", " ####");
            G('H', "#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #");
            G('I', " ### ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### ");
            G('J', "  ###", "    #", "    #", "    #", "    #", "#   #", " ### ");
            G('K', "#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #");
            G('L', "#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####");
            G('M', "#   #", "## ##", "# # #", "# # #", "#   #", "#   #", "#   #");
            G('N', "#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #");
            G('O', " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### ");
            G('P', "#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    ");
            G('Q', " ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #");
            G('R', "#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #");
            G('S', " ####", "#    ", "#    ", " ### ", "    #", "    #", "#### ");
            G('T', "#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ");
            G('U', "#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### ");
            G('V', "#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  ");
            G('W', "#   #", "#   #", "#   #", "# # #", "# # #", "# # #", " # # ");
            G('X', "#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #");
            G('Y', "#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  ");
            G('Z', "#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####");
            G('Ä', "#   #", " ### ", "#   #", "#####", "#   #", "#   #", "#   #");
            G('Ö', "#   #", " ### ", "#   #", "#   #", "#   #", "#   #", " ### ");
            G('Ü', "#   #", "     ", "#   #", "#   #", "#   #", "#   #", " ### ");
            G('0', " ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### ");
            G('1', "  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### ");
            G('2', " ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####");
            G('3', " ### ", "#   #", "    #", "  ## ", "    #", "#   #", " ### ");
            G('4', "   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # ");
            G('5', "#####", "#    ", "#### ", "    #", "    #", "#   #", " ### ");
            G('6', " ### ", "#    ", "#    ", "#### ", "#   #", "#   #", " ### ");
            G('7', "#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   ");
            G('8', " ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### ");
            G('9', " ### ", "#   #", "#   #", " ####", "    #", "    #", " ### ");
            G('-', "     ", "     ", "     ", " ### ", "     ", "     ", "     ");
            G('.', "     ", "     ", "     ", "     ", "     ", " ##  ", " ##  ");
            G(',', "     ", "     ", "     ", "     ", " ##  ", "  #  ", " #   ");
            G('\'', "  #  ", "  #  ", "     ", "     ", "     ", "     ", "     ");
            G('/', "    #", "   # ", "   # ", "  #  ", " #   ", " #   ", "#    ");
            G(' ', "     ", "     ", "     ", "     ", "     ", "     ", "     ");
        }

        static string Clean(string s)
        {
            s = s.ToUpperInvariant().Replace("ß", "SS").Replace("É", "E").Replace("È", "E").Replace("Ê", "E");
            return s;
        }

        /// <summary>Text mittig in ein Feld (x, y, w, h) setzen, so gross wie moeglich.</summary>
        void DrawText(int x0, int y0, int w, int h, string text, Color32 col)
        {
            EnsureFont();
            text = Clean(text);
            int cols = text.Length * 6 - 1;
            float s = Mathf.Min(w / (float)cols, h / 7f);
            if (s < 1f) s = 1f;
            float tw = cols * s, th = 7f * s;
            float sx = x0 + (w - tw) / 2f, sy = y0 + (h - th) / 2f;
            for (int i = 0; i < text.Length; i++)
            {
                if (!font.TryGetValue(text[i], out var g)) g = font[' '];
                for (int row = 0; row < 7; row++)
                    for (int c = 0; c < 5; c++)
                    {
                        if (g[row][c] != '#') continue;
                        // Glyphenpixel als leicht gerundetes Quadrat (Kanten weicher)
                        float gx = sx + (i * 6 + c) * s, gy = sy + (6 - row) * s;
                        int xa = Mathf.FloorToInt(gx), xb = Mathf.CeilToInt(gx + s), ya = Mathf.FloorToInt(gy), yb = Mathf.CeilToInt(gy + s);
                        for (int y = ya; y < yb; y++)
                            for (int x = xa; x < xb; x++)
                            {
                                if (x < 0 || y < 0 || x >= W || y >= Hh) continue;
                                float cov = Mathf.Clamp01(Mathf.Min(x + 1, gx + s) - Mathf.Max(x, gx)) * Mathf.Clamp01(Mathf.Min(y + 1, gy + s) - Mathf.Max(y, gy));
                                if (cov <= 0f) continue;
                                px[y * W + x] = Color32.Lerp(px[y * W + x], col, cov);
                            }
                    }
            }
        }

        // ================= Geometrie =================
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Rect r, Vector3 face, bool flipU = false)
        {
            // a unten links, b oben links, c oben rechts, d unten rechts (vom Betrachter aus)
            Vector2 ua = new Vector2(flipU ? r.xMax : r.xMin, r.yMin), ub = new Vector2(flipU ? r.xMax : r.xMin, r.yMax), uc = new Vector2(flipU ? r.xMin : r.xMax, r.yMax), ud = new Vector2(flipU ? r.xMin : r.xMax, r.yMin);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), face) < 0f) { var x = b; b = d; d = x; var y = ub; ub = ud; ud = y; }
            int k = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for (int i = 0; i < 4; i++) n.Add(face.normalized);
            uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
            t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3);
        }

        /// <summary>Tafel: Mitte c, Blickrichtung face (zum Betrachter), Breite/Hoehe; Vorder- und Rueckseite.</summary>
        void Plate(Vector3 c, Vector3 face, float w, float h, Rect front, Rect back, float thick = 0.03f)
        {
            face.y = 0f; face.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, -face);   // rechts aus Sicht des Betrachters
            Vector3 R = right * w / 2f, U = Vector3.up * h / 2f, F = face * thick / 2f;
            Quad(c + F - R - U, c + F - R + U, c + F + R + U, c + F + R - U, front, face);
            // Rueckseite: vom hinteren Betrachter aus gesehen ist "rechts" die andere Seite
            Quad(c - F + R - U, c - F + R + U, c - F - R + U, c - F - R - U, back, -face);
            var grey = IconUV(0);
            Quad(c - F - R + U, c + F - R + U, c + F + R + U, c - F + R + U, grey, Vector3.up);
            Quad(c + F + R - U, c - F + R - U, c - F + R + U, c + F + R + U, grey, right);
            Quad(c - F - R - U, c + F - R - U, c + F - R + U, c - F - R + U, grey, -right);
        }

        void Post(Vector3 ground, float h, float r = 0.04f)
        {
            var g = IconUV(0);
            Vector3 X = Vector3.right * r, Z = Vector3.forward * r, H = Vector3.up * (h + 0.3f), b = ground - Vector3.up * 0.3f;
            Quad(b - X + Z, b - X + Z + H, b + X + Z + H, b + X + Z, g, Vector3.forward);
            Quad(b + X - Z, b + X - Z + H, b - X - Z + H, b - X - Z, g, Vector3.back);
            Quad(b + X + Z, b + X + Z + H, b + X - Z + H, b + X - Z, g, Vector3.right);
            Quad(b - X - Z, b - X - Z + H, b - X + Z + H, b - X + Z, g, Vector3.left);
        }

        static Vector3 F3(Vector2 d) => new Vector3(d.x, 0f, d.y);

        /// <summary>Ortsschild: Vorderseite (Name) zeigt in Richtung faceDir (zu den Ankommenden), Rueckseite Ortsende.</summary>
        public void Place(Vector3 ground, Vector2 faceDir, string name, string sub)
        {
            if (NetSync.Headless) { Signs++; return; }
            if (!PlaceCells(name, sub, out var f, out var b)) return;
            Signs++;
            Vector3 face = F3(faceDir.normalized);
            Vector3 right = Vector3.Cross(Vector3.up, -face);
            Post(ground - right * 0.55f, 2.4f); Post(ground + right * 0.55f, 2.4f);
            Plate(ground + Vector3.up * 1.95f + face * 0.06f, face, 1.4f, 0.9f, f, b);
        }

        /// <summary>Verkehrszeichen (Symbol 1..7) auf einem Pfosten.</summary>
        public void Traffic(Vector3 ground, Vector2 faceDir, int icon)
        {
            Signs++;
            if (NetSync.Headless) return;
            Vector3 face = F3(faceDir.normalized);
            Post(ground, 2.4f);
            float s = icon == Vorfahrtstrasse ? 0.85f : 0.75f;
            Plate(ground + Vector3.up * 2.05f + face * 0.06f, face, s, s, IconUV(icon), IconUV(0), 0.02f);
        }

        /// <summary>Wegweiser: Pfosten mit bis zu vier Tafeln, jede zeigt in ihre Richtung (dir) mit Ziel und Entfernung.</summary>
        public void Signpost(Vector3 ground, List<(Vector2 dir, string text)> plates)
        {
            if (plates.Count == 0) return;
            if (NetSync.Headless) { Signs++; return; }
            // nur Tafeln, die noch in die Textur passen
            var ok = new List<(Vector2 dir, string text, Rect a, Rect b, bool r)>();
            foreach (var (dir0, text0) in plates)
            {
                Vector3 d0 = F3(dir0.normalized); Vector3 face0 = Vector3.Cross(d0, Vector3.up); Vector3 right0 = Vector3.Cross(Vector3.up, -face0);
                bool ar = Vector3.Dot(right0, d0) > 0f;
                if (PlateCell(text0, ar, out var ca) && PlateCell(text0, !ar, out var cb)) ok.Add((dir0, text0, ca, cb, ar));
            }
            if (ok.Count == 0) return;
            Signs++;
            Post(ground, 2.2f + ok.Count * 0.42f, 0.05f);
            float y = 2.0f + ok.Count * 0.42f;
            foreach (var (dir, text, ca, cb, ar) in ok)
            {
                Vector3 d = F3(dir.normalized);
                // Tafel steht laengs zur Zielrichtung; sichtbar von beiden Seiten, Pfeil zeigt jeweils zum Ziel
                Vector3 face = Vector3.Cross(d, Vector3.up);   // Normale quer zur Zielrichtung
                Vector3 c = ground + Vector3.up * y + d * 0.85f;
                // Vorderseite: rechts = d?  rechts aus Sicht des Betrachters auf 'face'
                Vector3 right = Vector3.Cross(Vector3.up, -face);
                Plate(c, face, 1.7f, 0.32f, ca, cb, 0.03f);
                y -= 0.42f;
            }
        }

        public void Build(Transform root)
        {
            if (NetSync.Headless || v.Count == 0) return;
            var tex = new Texture2D(W, Hh, TextureFormat.RGBA32, true) { name = "Schilder", anisoLevel = 8, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px); tex.Apply(true); tex.Compress(true);
            var m = new Material(GameMap.Mat(Color.white)) { name = "Schilder" };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); else m.mainTexture = tex;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.3f);
            var mesh = new Mesh { name = "Schilder", indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateBounds();
            var go = new GameObject("Schilder"); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m;
            Debug.Log($"[Schilder] {Signs} Schilder, {t.Count / 3} Dreiecke, Ortsschilder bis Zeile {cy + rowH} von {Split}, Tafeln bis {pcy + prow} von {Hh}" + (full > 0 ? $", {full} weggelassen (Textur voll)" : ""));
        }
    }
}
