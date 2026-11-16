using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Baukasten fuer Karten aus dem Code: Gebaeude (Haus, Chalet, Stufengiebel, Kirche, Turm, Ruine), Felsbrocken, Bruecken.
    /// Alles landet in wenigen zusammengefassten Netzen je Block (eine Untergruppe je Farbe/Textur) mit Kollision -
    /// viel schneller als tausende Einzelteile. Dazu Gelaende-Kacheln und die Draufsicht fuer die Startpunkt-Auswahl.
    /// </summary>
    public class MeshKit
    {
        public const int TexNone = 0, TexPlaster = 1, TexBrick = 2, TexRoof = 3, TexWood = 4, TexStone = 5, TexSlate = 6, TexCobble = 7, TexBrickPlain = 8;
        readonly float chunk;
        class Part { public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector3> n = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly Dictionary<long, List<int>> sub = new Dictionary<long, List<int>>(); }
        readonly Dictionary<long, Part> parts = new Dictionary<long, Part>();
        readonly Dictionary<long, (Color c, int tex)> matKeys = new Dictionary<long, (Color, int)>();
        Part cur; long curKey;

        public MeshKit(float chunk = 400f) { this.chunk = chunk; }

        /// <summary>Folgende Teile in den Block an dieser Stelle legen.</summary>
        public void At(float x, float z)
        {
            long key = ((long)Mathf.FloorToInt(x / chunk) << 32) ^ (uint)Mathf.FloorToInt(z / chunk);
            if (cur != null && key == curKey) return;
            if (!parts.TryGetValue(key, out cur)) { cur = new Part(); parts[key] = cur; }
            curKey = key;
        }

        List<int> Sub(Color c, int tex)
        {
            if (cur == null) At(0f, 0f);
            Color32 q = c;
            long key = ((long)tex << 32) | ((long)q.r << 16) | ((long)q.g << 8) | q.b;
            matKeys[key] = (c, tex);
            if (!cur.sub.TryGetValue(key, out var l)) { l = new List<int>(); cur.sub[key] = l; }
            return l;
        }

        // ---- Grundformen (Vorderseite zeigt nach 'face') ----
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, int tex, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 face)
        {
            var t = Sub(col, tex);
            Vector3 nrm = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(nrm, face) < 0f) { var x = b; b = d; d = x; var y = ub; ub = ud; ud = y; nrm = -nrm; }
            nrm.Normalize();
            int k = cur.v.Count;
            cur.v.Add(a); cur.v.Add(b); cur.v.Add(c); cur.v.Add(d);
            for (int i = 0; i < 4; i++) cur.n.Add(nrm);
            cur.uv.Add(ua); cur.uv.Add(ub); cur.uv.Add(uc); cur.uv.Add(ud);
            t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3);
        }
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col, int tex, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 face)
        {
            var t = Sub(col, tex);
            Vector3 nrm = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(nrm, face) < 0f) { var x = b; b = c; c = x; var y = ub; ub = uc; uc = y; nrm = -nrm; }
            nrm.Normalize();
            int k = cur.v.Count;
            cur.v.Add(a); cur.v.Add(b); cur.v.Add(c);
            for (int i = 0; i < 3; i++) cur.n.Add(nrm);
            cur.uv.Add(ua); cur.uv.Add(ub); cur.uv.Add(uc);
            t.Add(k); t.Add(k + 1); t.Add(k + 2);
        }

        /// <summary>Wand von a nach b (Boden y0 bis y1), Textur in Metern/3 (ein Feld = Fensterachse x Geschoss).</summary>
        public void Wall(Vector2 a, Vector2 b, float y0, float y1, float yRef, Vector2 center, Color col, int tex)
        {
            float len = Vector2.Distance(a, b);
            float u1 = Mathf.Max(1f, Mathf.Round(len / 3f));
            if (len < 2f) u1 = len / 3f;
            Vector3 face = new Vector3((a.x + b.x) / 2f - center.x, 0f, (a.y + b.y) / 2f - center.y);
            Quad(new Vector3(a.x, y0, a.y), new Vector3(a.x, y1, a.y), new Vector3(b.x, y1, b.y), new Vector3(b.x, y0, b.y), col, tex,
                 new Vector2(0f, (y0 - yRef) / 3f), new Vector2(0f, (y1 - yRef) / 3f), new Vector2(u1, (y1 - yRef) / 3f), new Vector2(u1, (y0 - yRef) / 3f), face);
        }

        /// <summary>Quader (gedreht um yaw), Textur in Metern/3.</summary>
        public void Box(Vector3 center, Vector3 size, float yaw, Color col, int tex = TexNone, bool top = true)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 hx = q * Vector3.right * size.x / 2f, hz = q * Vector3.forward * size.z / 2f;
            float y0 = center.y - size.y / 2f, y1 = center.y + size.y / 2f;
            Vector2 c = new Vector2(center.x, center.z);
            Vector2 P(float sx, float sz) { Vector3 p = center + hx * sx + hz * sz; return new Vector2(p.x, p.z); }
            Vector2 p00 = P(-1, -1), p10 = P(1, -1), p11 = P(1, 1), p01 = P(-1, 1);
            Wall(p00, p10, y0, y1, y0, c, col, tex); Wall(p10, p11, y0, y1, y0, c, col, tex);
            Wall(p11, p01, y0, y1, y0, c, col, tex); Wall(p01, p00, y0, y1, y0, c, col, tex);
            if (top)
                Quad(new Vector3(p00.x, y1, p00.y), new Vector3(p10.x, y1, p10.y), new Vector3(p11.x, y1, p11.y), new Vector3(p01.x, y1, p01.y), col, tex,
                     p00 / 3f, p10 / 3f, p11 / 3f, p01 / 3f, Vector3.up);
        }

        /// <summary>
        /// Haus mit Satteldach. w = Giebelseite (quer zum First), l = Traufseite (laengs). ground = tiefster Boden, top = hoechster Boden.
        /// steps &gt; 0: flaemischer Stufengiebel an beiden Enden. upperTex: Obergeschoss anders (Chalet: Holz ueber weissem Sockel).
        /// </summary>
        public void House(Vector2 p, float yaw, float w, float l, float wallH, float roofH, float ground, float top,
                          Color wall, int wallTex, Color roof, int roofTex = TexRoof, float overhang = 0.4f, int steps = 0,
                          Color? upper = null, int upperTex = TexWood, float upperFrom = 3f)
        {
            At(p.x, p.y);
            var q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 ax = q * Vector3.right, az = q * Vector3.forward;   // x quer (Giebelbreite), z laengs (First)
            Vector2 X = new Vector2(ax.x, ax.z) * (w / 2f), Z = new Vector2(az.x, az.z) * (l / 2f);
            Vector2 c00 = p - X - Z, c10 = p + X - Z, c11 = p + X + Z, c01 = p - X + Z;
            float y0 = ground - 1f, eave = top + wallH, ridge = eave + roofH;
            float split = upper.HasValue ? Mathf.Min(top + upperFrom, eave) : eave;
            // Waende
            Wall(c00, c10, y0, split, top, p, wall, wallTex); Wall(c10, c11, y0, split, top, p, wall, wallTex);
            Wall(c11, c01, y0, split, top, p, wall, wallTex); Wall(c01, c00, y0, split, top, p, wall, wallTex);
            Color gableCol = upper ?? wall; int gableTex = upper.HasValue ? upperTex : wallTex;
            if (upper.HasValue && split < eave)
            {
                Wall(c00, c10, split, eave, top, p, gableCol, gableTex); Wall(c10, c11, split, eave, top, p, gableCol, gableTex);
                Wall(c11, c01, split, eave, top, p, gableCol, gableTex); Wall(c01, c00, split, eave, top, p, gableCol, gableTex);
            }
            // Giebeldreiecke an den Schmalseiten (-Z und +Z)
            Vector2 m0 = p - Z, m1 = p + Z;
            Vector3 V(Vector2 a, float y) => new Vector3(a.x, y, a.y);
            Vector2 Uv(Vector2 a, Vector2 b0, float y) => new Vector2(Vector2.Distance(a, b0) / 3f, (y - top) / 3f);
            Tri(V(c00, eave), V(m0, ridge), V(c10, eave), gableCol, gableTex, Uv(c00, c00, eave), Uv(m0, c00, ridge), Uv(c10, c00, eave), V(-Z, 0f));
            Tri(V(c11, eave), V(m1, ridge), V(c01, eave), gableCol, gableTex, Uv(c11, c11, eave), Uv(m1, c11, ridge), Uv(c01, c11, eave), V(Z, 0f));
            // Dach mit Ueberstand (Traufe und Ortgang)
            float oh = overhang;
            Vector2 ox = X.normalized * oh, oz = Z.normalized * (steps > 0 ? 0f : oh);
            float drop = roofH / (w / 2f) * oh;
            Vector2 e00 = c00 - ox - oz, e10 = c10 + ox - oz, e11 = c11 + ox + oz, e01 = c01 - ox + oz;
            Vector2 r0 = m0 - oz, r1 = m1 + oz;
            float slope = Mathf.Sqrt(roofH * roofH + (w / 2f + oh) * (w / 2f + oh));
            float L = l + 2f * oz.magnitude;
            Quad(V(e00, eave - drop), V(r0, ridge), V(r1, ridge), V(e01, eave - drop), roof, roofTex,
                 new Vector2(0f, 0f), new Vector2(0f, slope), new Vector2(L, slope), new Vector2(L, 0f), V(-X, 0f) + Vector3.up);
            Quad(V(e10, eave - drop), V(r0, ridge), V(r1, ridge), V(e11, eave - drop), roof, roofTex,
                 new Vector2(0f, 0f), new Vector2(0f, slope), new Vector2(L, slope), new Vector2(L, 0f), V(X, 0f) + Vector3.up);
            // Unterseite des Ueberstands (sonst sieht man von unten ins Dach)
            if (oh > 0.05f)
            {
                Color under = roof * 0.55f; under.a = 1f;
                Quad(V(e00, eave - drop - 0.02f), V(r0, ridge - 0.25f), V(r1, ridge - 0.25f), V(e01, eave - drop - 0.02f), under, TexNone, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, V(-X, 0f) - Vector3.up);
                Quad(V(e10, eave - drop - 0.02f), V(r0, ridge - 0.25f), V(r1, ridge - 0.25f), V(e11, eave - drop - 0.02f), under, TexNone, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, V(X, 0f) - Vector3.up);
            }
            if (steps > 0)
            {
                // Stufengiebel: Treppe aus Mauerkloetzen ueber dem Dach an beiden Enden
                for (int end = 0; end < 2; end++)
                {
                    Vector2 m = end == 0 ? m0 : m1;
                    Vector2 inward = (end == 0 ? Z : -Z).normalized;
                    float sh = (ridge + 1.0f - eave) / steps;
                    for (int s = 0; s < steps; s++)
                    {
                        float half = (w / 2f + 0.1f) * (1f - s / (float)steps);
                        float yb = eave - 0.3f + s * sh, yt = eave + (s + 1) * sh + 0.2f;
                        Vector2 cc = m + inward * 0.2f;
                        Box(new Vector3(cc.x, (yb + yt) / 2f, cc.y), new Vector3(half * 2f, yt - yb, 0.45f), yaw, gableCol, gableTex);
                    }
                }
            }
        }

        /// <summary>Kirche: Schiff mit Satteldach, Turm am Westende mit Spitzhelm.</summary>
        public void Church(Vector2 p, float yaw, float ground, float top, Color wall, int wallTex, Color roof, Color spire, float naveL = 26f, float naveW = 12f, float towerH = 28f, float spireH = 16f, int steps = 0)
        {
            House(p, yaw, naveW, naveL, 9f, 6.5f, ground, top, wall, wallTex, roof, TexSlate, 0.3f, steps);
            var q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 tz = q * Vector3.back * (naveL / 2f + 3f);
            Vector2 tp = p + new Vector2(tz.x, tz.z);
            Tower(tp, yaw, 6.5f, towerH, spireH, ground - 1f, wall, wallTex, spire);
            // Chor (halbrund angedeutet als schmaleres Haus am Ostende)
            Vector3 cz = q * Vector3.forward * (naveL / 2f + 3f);
            House(p + new Vector2(cz.x, cz.z), yaw, naveW * 0.7f, 6f, 7.5f, 4.5f, ground, top, wall, wallTex, roof, TexSlate, 0.2f);
        }

        /// <summary>Viereckiger Turm mit Pyramidenhelm.</summary>
        public void Tower(Vector2 p, float yaw, float w, float h, float spireH, float y0, Color wall, int wallTex, Color spire)
        {
            At(p.x, p.y);
            Box(new Vector3(p.x, y0 + h / 2f, p.y), new Vector3(w, h, w), yaw, wall, wallTex, false);
            var q = Quaternion.Euler(0f, yaw, 0f);
            float yt = y0 + h;
            var corners = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3 o = q * new Vector3((i == 0 || i == 3 ? -1 : 1) * (w / 2f + 0.3f), 0f, (i < 2 ? -1 : 1) * (w / 2f + 0.3f));
                corners[i] = new Vector3(p.x, yt, p.y) + o;
            }
            Vector3 apex = new Vector3(p.x, yt + spireH, p.y);
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = corners[i], b = corners[(i + 1) % 4];
                Vector3 face = (a + b) / 2f - new Vector3(p.x, yt, p.y) + Vector3.up * 0.2f;
                Tri(a, apex, b, spire, TexSlate, new Vector2(0f, 0f), new Vector2(w / 2f, spireH), new Vector2(w, 0f), face);
            }
            Quad(corners[0], corners[1], corners[2], corners[3], spire, TexNone, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.down);
        }

        /// <summary>Ruine: Mauerreste ohne Dach, unregelmaessig hoch.</summary>
        public void Ruin(Vector2 p, float yaw, float w, float l, float ground, System.Random r, Color wall, int wallTex)
        {
            At(p.x, p.y);
            var q = Quaternion.Euler(0f, yaw, 0f);
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side % 2 == 0; float sgn = side < 2 ? 1f : -1f;
                float len = alongX ? w : l;
                int segs = Mathf.Max(2, Mathf.RoundToInt(len / 2.5f));
                for (int s = 0; s < segs; s++)
                {
                    if (r.NextDouble() < 0.3) continue;   // Luecken
                    float f = (s + 0.5f) / segs - 0.5f;
                    float hh = (float)(0.6 + r.NextDouble() * 5.0);
                    Vector3 o = alongX ? new Vector3(f * w, 0f, sgn * l / 2f) : new Vector3(sgn * w / 2f, 0f, f * l);
                    Vector3 c = new Vector3(p.x, ground - 0.5f + hh / 2f, p.y) + q * o;
                    Box(c, alongX ? new Vector3(len / segs + 0.05f, hh + 1f, 0.5f) : new Vector3(0.5f, hh + 1f, len / segs + 0.05f), yaw, wall, wallTex);
                }
            }
            // Schutthaufen
            for (int k = 0; k < 4; k++)
            {
                Vector3 o = q * new Vector3((float)(r.NextDouble() - 0.5) * w * 0.7f, 0f, (float)(r.NextDouble() - 0.5) * l * 0.7f);
                Rock(new Vector3(p.x, ground, p.y) + o, new Vector3(2.2f, 0.9f, 1.8f) * (float)(0.6 + r.NextDouble() * 0.7), r, wall * 0.8f);
            }
        }

        /// <summary>Heckenstueck von a nach b: unten breit, oben schmaler und buckelig (ohne Textur).</summary>
        public void Hedge(Vector2 a, Vector2 b, float ya, float yb, float ha, float hb, float w, Color col)
        {
            At((a.x + b.x) / 2f, (a.y + b.y) / 2f);
            Vector2 d = (b - a).normalized, s = new Vector2(-d.y, d.x);
            Vector2 sb = s * (w / 2f), st = s * (w * 0.3f);
            Vector3 A0 = new Vector3(a.x - sb.x, ya - 0.3f, a.y - sb.y), A1 = new Vector3(a.x + sb.x, ya - 0.3f, a.y + sb.y);
            Vector3 B0 = new Vector3(b.x - sb.x, yb - 0.3f, b.y - sb.y), B1 = new Vector3(b.x + sb.x, yb - 0.3f, b.y + sb.y);
            Vector3 At0 = new Vector3(a.x - st.x, ya + ha, a.y - st.y), At1 = new Vector3(a.x + st.x, ya + ha, a.y + st.y);
            Vector3 Bt0 = new Vector3(b.x - st.x, yb + hb, b.y - st.y), Bt1 = new Vector3(b.x + st.x, yb + hb, b.y + st.y);
            Vector3 sideN = new Vector3(s.x, 0.4f, s.y);
            Color dark = col * 0.8f; dark.a = 1f;
            Quad(A0, At0, Bt0, B0, dark, TexNone, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector3(-s.x, 0.4f, -s.y));
            Quad(A1, At1, Bt1, B1, dark, TexNone, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, sideN);
            Quad(At0, At1, Bt1, Bt0, col, TexNone, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
        }

        /// <summary>Flache Platte (Marktplatz), Textur in Metern/3.</summary>
        public void Plate(Vector2[] corners, float y, Color col, int tex)
        {
            Vector2 c = (corners[0] + corners[2]) / 2f; At(c.x, c.y);
            Vector3 V(Vector2 p) => new Vector3(p.x, y, p.y);
            Quad(V(corners[0]), V(corners[1]), V(corners[2]), V(corners[3]), col, tex, corners[0] / 3f, corners[1] / 3f, corners[2] / 3f, corners[3] / 3f, Vector3.up);
        }

        /// <summary>Felsbrocken: verbeulter Wuerfel (ruht mit dem unteren Drittel im Boden).</summary>
        public void Rock(Vector3 basePos, Vector3 size, System.Random r, Color col)
        {
            At(basePos.x, basePos.z);
            float yaw = (float)r.NextDouble() * 360f;
            var q = Quaternion.Euler((float)(r.NextDouble() - 0.5) * 25f, yaw, (float)(r.NextDouble() - 0.5) * 25f);
            // 8 Ecken + 6 Flaechenmitten nach aussen gedrueckt -> 24 Dreiecke
            var corner = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                Vector3 d = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                d = Vector3.Scale(d, new Vector3((float)(0.7 + r.NextDouble() * 0.5), (float)(0.6 + r.NextDouble() * 0.5), (float)(0.7 + r.NextDouble() * 0.5)));
                if (d.y > 0) d.x *= 0.8f;
                corner[i] = basePos + Vector3.up * size.y * 0.25f + q * Vector3.Scale(d, size * 0.5f);
            }
            int[][] faces = { new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 }, new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 } };
            Vector3 center = basePos + Vector3.up * size.y * 0.25f;
            foreach (var f in faces)
            {
                Vector3 mid = (corner[f[0]] + corner[f[1]] + corner[f[2]] + corner[f[3]]) / 4f;
                mid += (mid - center).normalized * Mathf.Min(size.x, size.y) * (float)(0.1 + r.NextDouble() * 0.15);
                Color c = col * (float)(0.85 + r.NextDouble() * 0.25); c.a = 1f;
                for (int e = 0; e < 4; e++)
                {
                    Vector3 a = corner[f[e]], b = corner[f[(e + 1) % 4]];
                    Tri(a, mid, b, c, TexStone, new Vector2(a.x + a.z, a.y) / 3f, new Vector2(mid.x + mid.z, mid.y) / 3f, new Vector2(b.x + b.z, b.y) / 3f, (a + b + mid) / 3f - center);
                }
            }
        }

        /// <summary>Bruecke: Fahrbahnplatte (Kollision) mit Gelaender und Pfeilern. a/b = Enden auf den Ufern, Hoehe der Fahrbahn yTop.</summary>
        public void Bridge(Vector2 a, Vector2 b, float width, float yTop, float bedY, Color deck, Color rail, int deckTex = TexStone)
        {
            Vector2 m = (a + b) / 2f; At(m.x, m.y);
            Vector2 d = b - a; float len = d.magnitude; d /= len;
            float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            Box(new Vector3(m.x, yTop - 0.4f, m.y), new Vector3(width, 0.8f, len), yaw, deck, deckTex);
            Vector2 s = new Vector2(d.y, -d.x) * (width / 2f - 0.2f);
            foreach (float sg in new[] { -1f, 1f })
            {
                Vector2 rp = m + s * sg;
                Box(new Vector3(rp.x, yTop + 0.5f, rp.y), new Vector3(0.35f, 1.0f, len), yaw, rail, deckTex);
            }
            // Pfeiler bei langen Bruecken
            int piers = Mathf.FloorToInt(len / 14f);
            for (int i = 1; i <= piers; i++)
            {
                Vector2 pp = a + d * (len * i / (piers + 1));
                float hh = yTop - 0.8f - bedY + 1f;
                if (hh > 0.5f) Box(new Vector3(pp.x, bedY - 1f + hh / 2f, pp.y), new Vector3(width * 0.8f, hh, 1.4f), yaw, rail * 0.9f, deckTex);
            }
        }

        // ---- fertig bauen ----
        public int Build(Transform parent, string name, bool collider = true)
        {
            var root = new GameObject(name).transform; root.SetParent(parent, false);
            int tris = 0;
            foreach (var part in parts.Values)
            {
                if (part.v.Count == 0) continue;
                var m = new Mesh { name = name, indexFormat = part.v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(part.v); m.SetNormals(part.n); m.SetUVs(0, part.uv);
                m.subMeshCount = part.sub.Count;
                var ms = new Material[part.sub.Count];
                int si = 0;
                foreach (var kv in part.sub)
                {
                    m.SetTriangles(kv.Value, si);
                    var (c, tex) = matKeys[kv.Key];
                    ms[si] = GetMaterial(c, tex);
                    tris += kv.Value.Count / 3; si++;
                }
                m.RecalculateBounds();
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                if (!NetSync.Headless)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    go.AddComponent<MeshRenderer>().sharedMaterials = ms;
                }
                if (collider) go.AddComponent<MeshCollider>().sharedMesh = m;
            }
            parts.Clear(); cur = null;
            return tris;
        }

        // ---- Materialien und Texturen (im Code) ----
        static readonly Dictionary<long, Material> matCache = new Dictionary<long, Material>();
        static Texture2D[] texs;

        static Material GetMaterial(Color c, int tex)
        {
            if (NetSync.Headless) return null;
            Color32 q = c;
            long key = ((long)tex << 32) | ((long)q.r << 16) | ((long)q.g << 8) | q.b;
            if (matCache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(GameMap.Mat(c)) { enableInstancing = true };
            if (tex != TexNone)
            {
                EnsureTextures();
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texs[tex]); else m.mainTexture = texs[tex];
            }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", tex == TexSlate ? 0.2f : 0.06f);
            matCache[key] = m;
            return m;
        }

        static void EnsureTextures()
        {
            if (texs != null) return;
            texs = new Texture2D[9];
            var r = new System.Random(11);
            float N(float a) => (float)(r.NextDouble() - 0.5) * a;
            // Fenster in einem 3 x 3 m Feld (Fassade): Rahmen, Glas mit Sprossen, Fensterbank
            Color Window(float u, float v, Color wallC, Color frameC)
            {
                bool frame = u > 0.31f && u < 0.69f && v > 0.30f && v < 0.84f;
                bool glass = u > 0.34f && u < 0.66f && v > 0.33f && v < 0.81f;
                bool bar = glass && (Mathf.Abs(u - 0.5f) < 0.012f || Mathf.Abs(v - 0.62f) < 0.012f);
                bool sill = u > 0.29f && u < 0.71f && v > 0.27f && v < 0.30f;
                if (glass && !bar) { float g = 0.14f + v * 0.12f + N(0.02f); return new Color(g * 0.8f, g * 0.9f, g * 1.05f, 1f); }
                if (frame || bar || sill) return frameC;
                return wallC;
            }
            texs[TexPlaster] = Make(128, (x, y) => { float u = x / 128f, v = y / 128f; Color p = Color.white * (0.92f + N(0.06f)); p.a = 1f; return Window(u, v, p, new Color(0.96f, 0.96f, 0.94f)); });
            texs[TexBrick] = Make(128, (x, y) =>
            {
                float u = x / 128f, v = y / 128f;
                int row = y / 5; int ox = (row & 1) * 6;
                bool joint = (y % 5) == 0 || ((x + ox) % 12) == 0;
                float s = joint ? 0.78f : 0.82f + N(0.22f);
                Color b = joint ? new Color(0.80f, 0.78f, 0.72f) : new Color(s, s, s);
                return Window(u, v, b, new Color(0.92f, 0.90f, 0.84f));
            });
            texs[TexRoof] = Make(64, (x, y) =>
            {
                int row = y / 8; int ox = (row % 2) * 8;
                bool gap = (y % 8) < 1 || ((x + ox) % 16) < 1;
                float s = gap ? 0.45f : 0.85f + N(0.14f) - (y % 8) * 0.02f;
                return new Color(s, s, s, 1f);
            });
            texs[TexWood] = Make(128, (x, y) =>
            {
                float u = x / 128f, v = y / 128f;
                float s = 0.72f + ((y % 11) < 1 ? -0.35f : 0f) + 0.06f * Mathf.Sin(x * 0.21f + y) + N(0.06f);
                Color wd = new Color(s, s * 0.95f, s * 0.9f, 1f);
                return Window(u, v, wd, new Color(0.95f, 0.93f, 0.88f));
            });
            texs[TexStone] = Make(64, (x, y) => { float s = 0.78f + N(0.28f) + 0.1f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f); return new Color(s, s, s * 0.97f, 1f); });
            // Kopfsteinpflaster: 1 Feld = 3 m -> Steine ca. 15 x 12 cm
            texs[TexCobble] = Make(256, (x, y) =>
            {
                int row = y / 10; int ox = (row & 1) * 6;
                int col = (x + ox) / 12;
                bool joint = (y % 10) < 1 || ((x + ox) % 12) < 1;
                float s = joint ? 0.42f : 0.78f + Mathf.Sin(col * 12.9898f + row * 78.233f) * 0.12f + N(0.08f);
                return new Color(s * 1.02f, s, s * 0.94f, 1f);
            });
            texs[TexBrickPlain] = Make(128, (x, y) =>
            {
                int row = y / 5; int ox = (row & 1) * 6;
                bool joint = (y % 5) == 0 || ((x + ox) % 12) == 0;
                float s = joint ? 0.78f : 0.82f + N(0.22f);
                return joint ? new Color(0.80f, 0.78f, 0.72f) : new Color(s, s, s);
            });
            texs[TexSlate] = Make(64, (x, y) =>
            {
                int row = y / 6; int ox = (row % 2) * 5;
                bool gap = (y % 6) < 1 || ((x + ox) % 10) < 1;
                float s = gap ? 0.55f : 0.88f + N(0.1f);
                return new Color(s, s, s, 1f);
            });
        }

        static Texture2D Make(int n, System.Func<int, int, Color> f)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = f(x, y);
            t.SetPixels(px); t.Apply(true);
            return t;
        }
    }

    /// <summary>Gelaende-Kacheln und Draufsicht fuer Karten mit Hoehenraster (G x G Punkte, Mitte 0/0).</summary>
    public static class TerrainKit
    {
        /// <summary>Die vier Standard-Bodenarten (Wiese, Acker, Weg, dunkle Wiese); rock = statt Acker grauer Fels/Geroell.</summary>
        public static TerrainLayer[] Layers(bool rock, out Material tm)
        {
            tm = null;
            if (NetSync.Headless) return null;
            bool hd = Resources.Load<Texture2D>("Boden/Wiese_Farbe") != null;
            TerrainLayer[] layers = hd ? new[]
            {
                PlainsMap.HdLayer("Wiese", 3.2f, new Color(1.0f, 1.0f, 0.76f)),
                rock ? PlainsMap.HdLayer("Weg", 7f, new Color(0.78f, 0.80f, 0.84f)) : PlainsMap.HdLayer("Acker", 2.6f, new Color(1.08f, 0.98f, 0.78f)),
                PlainsMap.HdLayer("Weg", 3.0f, new Color(0.95f, 0.90f, 0.82f)),
                PlainsMap.HdLayer("WieseDunkel", 3.6f, new Color(0.74f, 0.86f, 0.64f)),
            } : new[]
            {
                PlainsMap.Layer(PlainsMap.NoiseTex(new Color(0.33f, 0.45f, 0.18f), new Color(0.24f, 0.36f, 0.12f), 7), 10f),
                rock ? PlainsMap.Layer(PlainsMap.NoiseTex(new Color(0.52f, 0.52f, 0.52f), new Color(0.38f, 0.38f, 0.40f), 19), 8f)
                     : PlainsMap.Layer(PlainsMap.NoiseTex(new Color(0.78f, 0.66f, 0.33f), new Color(0.62f, 0.50f, 0.22f), 11), 6f),
                PlainsMap.Layer(PlainsMap.NoiseTex(new Color(0.45f, 0.36f, 0.25f), new Color(0.35f, 0.27f, 0.18f), 13), 5f),
                PlainsMap.Layer(PlainsMap.NoiseTex(new Color(0.26f, 0.38f, 0.15f), new Color(0.20f, 0.30f, 0.10f), 17), 12f),
            };
            var sh = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (sh != null) { tm = new Material(sh); if (hd) PlainsMap.SetTerrainDetail(tm, GameSettings.Effects >= 1); }
            return layers;
        }

        public delegate void WeightFn(int gx, int gz, float[] w);

        /// <summary>Gelaende aus Kacheln (2 km, 513 Punkte). weights fuellt je Rasterpunkt vier Gewichte (Summe egal).</summary>
        public static void Build(Transform root, float[] H, int G, float size, float maxH, int tiles, int group, TerrainLayer[] layers, Material tm, WeightFn weights, float pixelError = 5f, int ares = 512)
        {
            int tres = (G - 1) / tiles + 1;
            bool gfx = !NetSync.Headless && layers != null;
            float tsize = size / tiles, half = size / 2f;
            var parent = new GameObject("Gelaende").transform; parent.SetParent(root, false);
            for (int tz = 0; tz < tiles; tz++)
                for (int tx = 0; tx < tiles; tx++)
                {
                    var td = new TerrainData { heightmapResolution = tres };
                    td.size = new Vector3(tsize, maxH, tsize);
                    var h = new float[tres, tres];
                    for (int iz = 0; iz < tres; iz++)
                        for (int ix = 0; ix < tres; ix++)
                            h[iz, ix] = Mathf.Clamp01(H[(tz * (tres - 1) + iz) * G + tx * (tres - 1) + ix] / maxH);
                    td.SetHeights(0, 0, h);
                    if (gfx)
                    {
                        td.terrainLayers = layers;
                        td.alphamapResolution = ares;
                        var a = new float[ares, ares, 4];
                        int ttx = tx, ttz = tz;
                        Parallel.For(0, ares, iz =>
                        {
                            var w = new float[4];
                            for (int ix = 0; ix < ares; ix++)
                            {
                                int gx = ttx * (tres - 1) + Mathf.RoundToInt(ix * (tres - 1) / (float)(ares - 1));
                                int gz = ttz * (tres - 1) + Mathf.RoundToInt(iz * (tres - 1) / (float)(ares - 1));
                                w[0] = w[1] = w[2] = w[3] = 0f;
                                weights(gx, gz, w);
                                float sum = w[0] + w[1] + w[2] + w[3];
                                if (sum <= 0f) { w[0] = 1f; sum = 1f; }
                                for (int k = 0; k < 4; k++) a[iz, ix, k] = w[k] / sum;
                            }
                        });
                        td.SetAlphamaps(0, 0, a);
                    }
                    var go = Terrain.CreateTerrainGameObject(td);
                    go.name = $"Gelaende {tx},{tz}";
                    go.transform.SetParent(parent, false);
                    go.transform.position = new Vector3(-half + tx * tsize, 0f, -half + tz * tsize);
                    var t = go.GetComponent<Terrain>();
                    t.groupingID = group; t.allowAutoConnect = true;
                    t.heightmapPixelError = pixelError;
                    t.basemapDistance = 1200f;
                    if (tm != null) t.materialTemplate = tm;
                }
            Terrain.SetConnectivityDirty();
            Physics.SyncTransforms();
        }

        /// <summary>Wasserband (Fluss, Bach, Kanal) entlang einer Linie; level = Wasserspiegel je Punkt. Ohne Kollision.</summary>
        public static void WaterRibbon(Transform parent, List<Vector2> p, float[] level, float hw, Material mat)
        {
            if (NetSync.Headless || p.Count < 2) return;
            const int Piece = 120;
            for (int start = 0; start < p.Count - 1; start += Piece)
            {
                int end = Mathf.Min(start + Piece, p.Count - 1);
                var v = new List<Vector3>(); var t = new List<int>(); var nr = new List<Vector3>();
                for (int i = start; i <= end; i++)
                {
                    Vector2 d = (p[Mathf.Min(i + 1, p.Count - 1)] - p[Mathf.Max(i - 1, 0)]).normalized;
                    Vector2 s = new Vector2(-d.y, d.x) * hw;
                    v.Add(new Vector3(p[i].x - s.x, level[i], p[i].y - s.y)); v.Add(new Vector3(p[i].x + s.x, level[i], p[i].y + s.y));
                    nr.Add(Vector3.up); nr.Add(Vector3.up);
                    if (i > start)
                    {
                        int b = v.Count - 4;
                        // oben sichtbar: beide Umlaufrichtungen pruefen ueber die Normale
                        Vector3 c = Vector3.Cross(v[b + 2] - v[b], v[b + 1] - v[b]);
                        if (c.y > 0f) { t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b + 1); t.Add(b + 2); t.Add(b + 3); }
                        else { t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b + 1); t.Add(b + 3); t.Add(b + 2); }
                    }
                }
                var m = new Mesh { name = "Wasser" };
                m.SetVertices(v); m.SetNormals(nr); m.SetTriangles(t, 0); m.RecalculateBounds();
                var go = new GameObject("Wasser"); go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>Hoehe bilinear aus dem Raster.</summary>
        public static float Sample(float[] H, int G, float cell, float half, float x, float z)
        {
            float fx = Mathf.Clamp((x + half) / cell, 0f, G - 1.001f), fz = Mathf.Clamp((z + half) / cell, 0f, G - 1.001f);
            int ix = (int)fx, iz = (int)fz; float tx = fx - ix, tz = fz - iz;
            float a = Mathf.Lerp(H[iz * G + ix], H[iz * G + ix + 1], tx), b = Mathf.Lerp(H[(iz + 1) * G + ix], H[(iz + 1) * G + ix + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        /// <summary>Draufsicht (Norden oben) mit Schummerung und Hoehenlinien; col liefert die Grundfarbe je Rasterpunkt.</summary>
        public static Texture2D Overview(string name, float[] H, int G, System.Func<int, Color> col, float contour, float shadeK, List<Vector3> dots = null)
        {
            const int N = 1024;
            var tex = new Texture2D(N, N, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, name = name };
            var px = new Color32[N * N];
            Parallel.For(0, N, py =>
            {
                for (int qx = 0; qx < N; qx++)
                {
                    int ix = Mathf.Clamp(Mathf.RoundToInt(qx / (float)(N - 1) * (G - 1)), 1, G - 2);
                    int iz = Mathf.Clamp(Mathf.RoundToInt(py / (float)(N - 1) * (G - 1)), 1, G - 2);
                    int id = iz * G + ix;
                    Color c = col(id);
                    float dx = H[id + 1] - H[id - 1], dz = H[id + G] - H[id - G];
                    float shade = Mathf.Clamp(1f + (-dx + dz) * shadeK, 0.5f, 1.4f);
                    c *= shade;
                    if (contour > 0f && Mathf.Repeat(H[id], contour) < contour * 0.05f) c *= 0.82f;
                    px[py * N + qx] = c;
                }
            });
            if (dots != null)
                foreach (var d in dots)
                {
                    // x, z = Weltlage, Groesse (m) -> Punkt
                    float size = (G - 1);
                    int cx = Mathf.RoundToInt(d.x * (N - 1)), cz = Mathf.RoundToInt(d.y * (N - 1));
                    int rr = d.z > 15f ? 2 : 1;
                    for (int dz = -rr; dz <= rr; dz++) for (int dx = -rr; dx <= rr; dx++)
                        {
                            int x = cx + dx, z = cz + dz;
                            if (x >= 0 && z >= 0 && x < N && z < N) px[z * N + x] = new Color32(120, 40, 30, 255);
                        }
                }
            tex.SetPixels32(px); tex.Apply(false);
            return tex;
        }
    }
}
