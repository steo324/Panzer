using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Strassen fuer alle Karten: glatte Kurven (Catmull-Rom), Belag als Band knapp ueber dem Gelaende
    /// (Hauptstrasse mit Leit- und Mittellinie, Nebenstrasse mit Flicken, Feldweg mit Fahrspuren und Grasnarbe, Kopfsteinpflaster),
    /// ausgefranste Raender mit Bankett, Strassengraeben und Damm/Einschnitt im Gelaende (Abstandsfeld fuer Hoehenraster).
    /// Das Band hat keinen Collider: Panzer fahren auf dem Gelaende darunter.
    /// </summary>
    public static class Roads
    {
        public const int Haupt = 0, Neben = 1, Feldweg = 2, Pflaster = 3;
        public static readonly float[] HalfWidth = { 3.3f, 2.4f, 1.5f, 3.2f };   // Fahrbahn (halbe Breite)
        public static readonly float[] Shoulder = { 0.9f, 0.7f, 0.6f, 0.25f };   // Bankett / ausgefranster Rand
        static readonly float[] Lift = { 0.10f, 0.085f, 0.07f, 0.10f };
        static readonly float[] Tile = { 12f, 8f, 8f, 4f };                      // Laenge einer Texturkachel (m)

        public class Road
        {
            public int kind;
            public List<Vector2> p;          // geglaettete Mittellinie (ca. 4 m Abstand)
            public float[] h;                // Hoehe der Fahrbahn je Punkt (optional, fuer Damm/Einschnitt)
            public bool ditch = true;        // Strassengraben (Haupt-/Nebenstrasse ausserorts)
            public float hw = -1f;           // eigene halbe Breite (z. B. Fluss), sonst nach Art
            public Road(int kind, List<Vector2> p) { this.kind = kind; this.p = p; ditch = kind <= Neben; }
            public float HW => hw > 0f ? hw : HalfWidth[kind];
        }

        public static float Total(int kind) => HalfWidth[kind] + Shoulder[kind];

        // ================= Linien =================
        /// <summary>Glatte Kurve durch die Punkte (zentripetales Catmull-Rom), gleichmaessig neu abgetastet.</summary>
        public static List<Vector2> Smooth(IList<Vector2> pts, float spacing = 4f)
        {
            var res = new List<Vector2>();
            if (pts.Count < 2) { res.AddRange(pts); return res; }
            // dicht abtasten, dann in gleichen Abstaenden ausgeben
            var dense = new List<Vector2>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 p0 = pts[Mathf.Max(i - 1, 0)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(i + 2, pts.Count - 1)];
                if (i == 0) p0 = p1 - (p2 - p1);
                if (i + 2 >= pts.Count) p3 = p2 + (p2 - p1);
                float seg = Vector2.Distance(p1, p2);
                int n = Mathf.Max(1, Mathf.CeilToInt(seg / (spacing * 0.5f)));
                for (int k = 0; k < n; k++) dense.Add(CatmullRom(p0, p1, p2, p3, k / (float)n));
            }
            dense.Add(pts[pts.Count - 1]);
            res.Add(dense[0]);
            float acc = 0f;
            for (int i = 1; i < dense.Count; i++)
            {
                Vector2 a = dense[i - 1], b = dense[i];
                float d = Vector2.Distance(a, b);
                while (acc + d >= spacing)
                {
                    float t = (spacing - acc) / d;
                    a = Vector2.Lerp(a, b, t); d = Vector2.Distance(a, b);
                    res.Add(a); acc = 0f;
                }
                acc += d;
            }
            if (Vector2.Distance(res[res.Count - 1], pts[pts.Count - 1]) > spacing * 0.3f) res.Add(pts[pts.Count - 1]);
            else res[res.Count - 1] = pts[pts.Count - 1];
            return res;
        }

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t0 = 0f, t1 = t0 + Mathf.Sqrt(Mathf.Max(Vector2.Distance(p0, p1), 0.01f));
            float t2 = t1 + Mathf.Sqrt(Mathf.Max(Vector2.Distance(p1, p2), 0.01f)), t3 = t2 + Mathf.Sqrt(Mathf.Max(Vector2.Distance(p2, p3), 0.01f));
            float tt = Mathf.Lerp(t1, t2, t);
            Vector2 a1 = (t1 - tt) / (t1 - t0) * p0 + (tt - t0) / (t1 - t0) * p1;
            Vector2 a2 = (t2 - tt) / (t2 - t1) * p1 + (tt - t1) / (t2 - t1) * p2;
            Vector2 a3 = (t3 - tt) / (t3 - t2) * p2 + (tt - t2) / (t3 - t2) * p3;
            Vector2 b1 = (t2 - tt) / (t2 - t0) * a1 + (tt - t0) / (t2 - t0) * a2;
            Vector2 b2 = (t3 - tt) / (t3 - t1) * a2 + (tt - t1) / (t3 - t1) * a3;
            return (t2 - tt) / (t2 - t1) * b1 + (tt - t1) / (t2 - t1) * b2;
        }

        /// <summary>Weg von a nach b mit sanften Schwuengen (Enden fest), schon geglaettet.</summary>
        public static List<Vector2> Meander(Vector2 a, Vector2 b, Noise2D n, int seed, float ampFactor = 0.09f, float maxAmp = 320f, float wave = 900f)
        {
            var pts = new List<Vector2>();
            float len = Vector2.Distance(a, b);
            if (len < 1f) { pts.Add(a); pts.Add(b); return pts; }
            Vector2 dir = (b - a) / len, side = new Vector2(-dir.y, dir.x);
            int count = Mathf.Max(2, Mathf.CeilToInt(len / 60f));
            float amp = Mathf.Min(maxAmp, len * ampFactor);
            for (int i = 0; i <= count; i++)
            {
                float t = i / (float)count;
                float w = Mathf.Sin(t * Mathf.PI);
                float off = ((n.Fbm(t * len / wave + seed * 3.7f, seed * 1.3f, 3) - 0.5f) * 2.6f) * amp * w;
                pts.Add(a + dir * (t * len) + side * off);
            }
            return Smooth(pts, 4f);
        }

        /// <summary>Hoehenversatz quer zur Strasse (e = Abstand vom Fahrbahnrand, negativ = auf der Fahrbahn): Bankett, dann Graben.</summary>
        public static float Profile(int kind, bool ditch, float e)
        {
            if (e <= 0f) return kind == Feldweg ? -0.08f : 0f;
            float sh = Shoulder[kind];
            if (e < sh) return -0.06f * e / sh;
            if (!ditch) return -0.06f;
            const float W = 5.5f;   // Grabenbreite (Gelaende-Raster ist grob: lieber breit und flach)
            float t = (e - sh) / W;
            if (t >= 1f) return 0f;
            float depth = kind == Haupt ? 0.75f : 0.55f;
            return -0.06f * (1f - t) - depth * Mathf.Sin(t * Mathf.PI) * (t < 0.5f ? 1f : Mathf.Lerp(1f, 0.85f, (t - 0.5f) * 2f));
        }

        /// <summary>Wo das Gelaende in das Strassenprofil uebergeht: bis hier ganz Strasse, danach weich ins Gelaende.</summary>
        public static float ProfileEnd(int kind, bool ditch) => Shoulder[kind] + (ditch ? 5.5f : 0.5f);

        /// <summary>Hoehen der Fahrbahn aus dem Gelaende, entlang der Strasse geglaettet (sanfte Steigungen, Damm/Einschnitt).</summary>
        public static void SetHeights(Road r, System.Func<float, float, float> rawY, int window = 10)
        {
            int n = r.p.Count;
            var raw = new float[n];
            for (int i = 0; i < n; i++) raw[i] = rawY(r.p[i].x, r.p[i].y);
            // zweimal gleitender Mittelwert ueber +-window Punkte (ca. +-40 m)
            var a = raw;
            for (int pass = 0; pass < 2; pass++)
            {
                var b = new float[n];
                float sum = 0f; int cnt = 0;
                int lo = 0, hi = -1;
                for (int i = 0; i < n; i++)
                {
                    int l = Mathf.Max(0, i - window), h = Mathf.Min(n - 1, i + window);
                    while (hi < h) { hi++; sum += a[hi]; cnt++; }
                    while (lo < l) { sum -= a[lo]; lo++; cnt--; }
                    b[i] = sum / cnt;
                }
                a = b;
            }
            // Enden behalten die echte Hoehe (Anschluss an Doerfer, Kreuzungen)
            for (int i = 0; i < n; i++)
            {
                float endW = Mathf.Clamp01(Mathf.Min(i, n - 1 - i) / (float)window);
                a[i] = Mathf.Lerp(raw[i], a[i], endW);
            }
            r.h = a;
        }

        // ================= Abstandsfeld fuer Hoehenraster =================
        public class Field
        {
            public int G; public float cell, origin, maxR;
            public float[] edge;      // Abstand vom Fahrbahnrand (negativ = auf der Fahrbahn), hoechstens maxR
            public byte[] kind;       // Art der naechsten Strasse (255 = keine)
            public byte[] ditch;      // 1 = mit Graben
            public float[] h;         // Fahrbahnhoehe am naechsten Punkt der Mittellinie (NaN = unbekannt)
            public float Edge(float x, float z)
            {
                int ix = Mathf.Clamp(Mathf.RoundToInt((x - origin) / cell), 0, G - 1), iz = Mathf.Clamp(Mathf.RoundToInt((z - origin) / cell), 0, G - 1);
                return edge[iz * G + ix];
            }
            public int KindAt(float x, float z)
            {
                int ix = Mathf.Clamp(Mathf.RoundToInt((x - origin) / cell), 0, G - 1), iz = Mathf.Clamp(Mathf.RoundToInt((z - origin) / cell), 0, G - 1);
                return kind[iz * G + ix];
            }
        }

        /// <summary>Abstand jeder Rasterzelle zur naechsten Strasse (exakt je Teilstueck), mit Art und Fahrbahnhoehe.</summary>
        public static Field Stamp(List<Road> roads, int G, float cell, float origin, float maxR)
        {
            var f = new Field { G = G, cell = cell, origin = origin, maxR = maxR, edge = new float[G * G], kind = new byte[G * G], ditch = new byte[G * G], h = new float[G * G] };
            for (int i = 0; i < f.edge.Length; i++) { f.edge[i] = maxR; f.kind[i] = 255; f.h[i] = float.NaN; }
            foreach (var r in roads)
            {
                float hw = r.HW, R = maxR + hw;
                for (int s = 0; s + 1 < r.p.Count; s++)
                {
                    Vector2 a = r.p[s], b = r.p[s + 1];
                    Vector2 ab = b - a; float len2 = Mathf.Max(ab.sqrMagnitude, 1e-4f);
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - R - origin) / cell)), x1 = Mathf.Min(G - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + R - origin) / cell));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - R - origin) / cell)), z1 = Mathf.Min(G - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + R - origin) / cell));
                    float ha = r.h != null ? r.h[s] : float.NaN, hb = r.h != null ? r.h[s + 1] : float.NaN;
                    for (int iz = z0; iz <= z1; iz++)
                    {
                        float wz = origin + iz * cell;
                        for (int ix = x0; ix <= x1; ix++)
                        {
                            float wx = origin + ix * cell;
                            float t = Mathf.Clamp01(((wx - a.x) * ab.x + (wz - a.y) * ab.y) / len2);
                            float dx = a.x + ab.x * t - wx, dz = a.y + ab.y * t - wz;
                            float e = Mathf.Sqrt(dx * dx + dz * dz) - hw;
                            int id = iz * G + ix;
                            // bei Ueberschneidung gewinnt die naehere, bei gleichem Abstand die wichtigere Strasse
                            if (e < f.edge[id] - 0.01f || (e < f.edge[id] + 0.01f && r.kind < f.kind[id]))
                            {
                                f.edge[id] = e; f.kind[id] = (byte)r.kind; f.ditch[id] = (byte)(r.ditch ? 1 : 0);
                                f.h[id] = Mathf.Lerp(ha, hb, t);
                            }
                        }
                    }
                }
            }
            return f;
        }

        /// <summary>Hoehe an einer Rasterzelle mit Strasse: Fahrbahn, Bankett, Graben, dann weich (Boeschung) ins Gelaende.</summary>
        public static float Apply(Field f, int id, float terrainH, float blend = 18f)
        {
            float e = f.edge[id];
            if (f.kind[id] == 255 || e >= f.maxR) return terrainH;
            int k = f.kind[id]; bool d = f.ditch[id] != 0;
            float road = float.IsNaN(f.h[id]) ? terrainH : f.h[id];
            float end = ProfileEnd(k, d);
            float prof = road + Profile(k, d, e);
            if (e <= end) return prof;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((e - end) / blend));
            return Mathf.Lerp(road, terrainH, t);
        }

        // ================= Belag (Netz) =================
        static Material[] mats;

        static Material GetMat(int kind)
        {
            if (mats == null) mats = new Material[4];
            if (mats[kind] != null) return mats[kind];
            var sh = Shader.Find("PanzerSim/Strasse");
            if (sh == null) sh = Resources.Load<Shader>("Shaders/Strasse");
            Material m;
            if (sh != null)
            {
                m = new Material(sh) { enableInstancing = true };
                m.SetTexture("_BaseMap", MakeTexture(kind));
                m.SetFloat("_Cutoff", 0.5f);
                m.SetFloat("_Gloss", kind == Haupt ? 0.12f : kind == Neben ? 0.08f : 0.02f);
            }
            else
            {
                m = new Material(GameMap.Mat(Color.white));
                m.mainTexture = MakeTexture(kind);
            }
            m.name = "Strasse " + kind;
            mats[kind] = m;
            return m;
        }

        /// <summary>Strassenbaender bauen (nur mit Grafik). covered: Punkt liegt schon auf einer wichtigeren Strasse (Kreuzung) - dort kein Band.</summary>
        public static void BuildMeshes(Transform root, List<Road> roads, System.Func<float, float, float> groundY)
        {
            if (NetSync.Headless || roads.Count == 0) return;
            var parent = new GameObject("Strassen").transform; parent.SetParent(root, false);
            var index = new SegIndex(roads);
            const float Chunk = 500f;
            var groups = new Dictionary<long, MeshData>();
            for (int ri = 0; ri < roads.Count; ri++)
            {
                var r = roads[ri];
                int n = r.p.Count;
                if (n < 2) continue;
                float hw = HalfWidth[r.kind], W = hw + Shoulder[r.kind], lift = Lift[r.kind], tile = Tile[r.kind];
                var cov = new bool[n];
                for (int i = 0; i < n; i++) cov[i] = index.Covered(r.p[i], ri, r.kind);
                float vAcc = 0f;
                MeshData md = null; int prevBase = -1; long prevKey = long.MinValue;
                for (int i = 0; i < n; i++)
                {
                    if (i > 0) vAcc += Vector2.Distance(r.p[i - 1], r.p[i]);
                    bool needed = !cov[i] || (i > 0 && !cov[i - 1]) || (i + 1 < n && !cov[i + 1]);
                    if (!needed) { prevBase = -1; continue; }
                    Vector2 p = r.p[i];
                    Vector2 t = (r.p[Mathf.Min(i + 1, n - 1)] - r.p[Mathf.Max(i - 1, 0)]).normalized;
                    Vector2 s = new Vector2(-t.y, t.x);
                    long key = ((long)Mathf.FloorToInt(p.x / Chunk) << 40) ^ ((long)(Mathf.FloorToInt(p.y / Chunk) & 0xFFFFF) << 8) ^ (long)r.kind;
                    if (!groups.TryGetValue(key, out var g)) { g = new MeshData { kind = r.kind }; groups[key] = g; }
                    // Chunkwechsel: letzten Querschnitt im neuen Chunk wiederholen, damit kein Loch entsteht
                    bool newStrip = prevBase < 0 || key != prevKey;
                    if (newStrip && prevBase >= 0 && md != null && i > 0)
                    {
                        // vorigen Punkt auch in den neuen Chunk schreiben
                        Vector2 pp = r.p[i - 1];
                        Vector2 tp = (r.p[i] - r.p[Mathf.Max(i - 2, 0)]).normalized; Vector2 sp = new Vector2(-tp.y, tp.x);
                        prevBase = AddSection(g, pp, sp, hw, W, lift, (vAcc - Vector2.Distance(r.p[i - 1], r.p[i])) / tile, groundY);
                        newStrip = false;
                    }
                    int b = AddSection(g, p, s, hw, W, lift, vAcc / tile, groundY);
                    if (!newStrip && prevBase >= 0)
                        for (int k = 0; k < 4; k++)
                        {
                            int a0 = prevBase + k, a1 = prevBase + k + 1, b0 = b + k, b1 = b + k + 1;
                            g.tri.Add(a0); g.tri.Add(b1); g.tri.Add(b0); g.tri.Add(a0); g.tri.Add(a1); g.tri.Add(b1);   // von oben im Uhrzeigersinn (Vorderseite)
                        }
                    prevBase = b; prevKey = key; md = g;
                }
            }
            int tris = 0;
            foreach (var g in groups.Values)
            {
                if (g.tri.Count == 0) continue;
                var m = new Mesh { name = "Strasse", indexFormat = g.v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(g.v); m.SetNormals(g.n); m.SetUVs(0, g.uv); m.SetTriangles(g.tri, 0);
                m.RecalculateBounds();
                var go = new GameObject("Strasse " + g.kind);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = GetMat(g.kind);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tris += g.tri.Count / 3;
            }
            Debug.Log($"[Strassen] {roads.Count} Strassen, {groups.Count} Teilnetze, {tris} Dreiecke");
        }

        class MeshData { public int kind; public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector3> n = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<int> tri = new List<int>(); }

        // Querschnitt: 5 Punkte (aussen links, Rand links, Mitte, Rand rechts, aussen rechts), jeder auf dem Gelaende
        static int AddSection(MeshData g, Vector2 p, Vector2 s, float hw, float W, float lift, float v, System.Func<float, float, float> groundY)
        {
            int b = g.v.Count;
            float[] offs = { -W, -hw, 0f, hw, W };
            var pos = new Vector3[5];
            for (int k = 0; k < 5; k++)
            {
                Vector2 q = p + s * offs[k];
                pos[k] = new Vector3(q.x, groundY(q.x, q.y) + lift, q.y);
            }
            // Normale aus dem Quergefaelle und der Laengsneigung (grob, nach oben)
            Vector3 across = pos[4] - pos[0];
            Vector3 along = new Vector3(-s.y, 0f, s.x);
            Vector3 nrm = Vector3.Cross(along, across).normalized;
            if (nrm.y < 0f) nrm = -nrm;
            for (int k = 0; k < 5; k++)
            {
                g.v.Add(pos[k]); g.n.Add(nrm);
                g.uv.Add(new Vector2((offs[k] + W) / (2f * W), v));
            }
            return b;
        }

        /// <summary>Schnelle Abfrage: liegt ein Punkt auf einer anderen, gleich wichtigen oder wichtigeren Strasse?</summary>
        class SegIndex
        {
            const float C = 40f;
            readonly Dictionary<long, List<(int r, int s)>> cells = new Dictionary<long, List<(int, int)>>();
            readonly List<Road> roads;
            public SegIndex(List<Road> roads)
            {
                this.roads = roads;
                for (int ri = 0; ri < roads.Count; ri++)
                    for (int s = 0; s + 1 < roads[ri].p.Count; s++)
                    {
                        Vector2 m = (roads[ri].p[s] + roads[ri].p[s + 1]) * 0.5f;
                        long key = Key(m);
                        if (!cells.TryGetValue(key, out var l)) { l = new List<(int, int)>(); cells[key] = l; }
                        l.Add((ri, s));
                    }
            }
            static long Key(Vector2 p) => ((long)Mathf.FloorToInt(p.x / C) << 32) ^ (uint)Mathf.FloorToInt(p.y / C);
            public bool Covered(Vector2 p, int self, int kind)
            {
                int cx = Mathf.FloorToInt(p.x / C), cz = Mathf.FloorToInt(p.y / C);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (!cells.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out var l)) continue;
                        foreach (var (ri, s) in l)
                        {
                            if (ri == self) continue;
                            var r = roads[ri];
                            if (r.kind > kind || (r.kind == kind && ri > self)) continue;   // nur wichtigere (bzw. gleiche, aeltere) Strassen decken ab
                            Vector2 a = r.p[s], b = r.p[s + 1], ab = b - a;
                            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                            if ((a + ab * t - p).sqrMagnitude < (HalfWidth[r.kind] - 0.3f) * (HalfWidth[r.kind] - 0.3f)) return true;
                        }
                    }
                return false;
            }
        }

        // ================= Texturen (im Code) =================
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 2147483647;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
            }
        }
        /// <summary>Wertrauschen, in y kachelbar (Periode py Gitterzellen).</summary>
        static float VNoise(float x, float y, int py, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int ya = ((y0 % py) + py) % py, yb = (((y0 + 1) % py) + py) % py;
            float a = Hash(x0, ya, seed), b = Hash(x0 + 1, ya, seed), c = Hash(x0, yb, seed), d = Hash(x0 + 1, yb, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static Texture2D MakeTexture(int kind)
        {
            const int NU = 128, NV = 256;
            float hw = HalfWidth[kind], W = hw + Shoulder[kind], L = Tile[kind];
            var px = new Color[NU * NV];
            for (int y = 0; y < NV; y++)
                for (int x = 0; x < NU; x++)
                {
                    float off = ((x + 0.5f) / NU * 2f - 1f) * W;   // Meter quer (links negativ)
                    float a = Mathf.Abs(off);
                    float along = (y + 0.5f) / NV * L;             // Meter laengs
                    // Rauschen in Metern: Periode passt zur Kachellaenge
                    float Fine(float scale, int seed) { int per = Mathf.Max(1, Mathf.RoundToInt(L * scale)); float sc = per / L; return VNoise(off * sc + 50f, along * sc, per, seed); }
                    float fine = Fine(12f, 1), mid = Fine(2.5f, 2), big = Fine(0.6f, 3);
                    Color c; float alpha = 1f;
                    float fray = (Fine(3f, 9) * 0.6f + Fine(9f, 10) * 0.4f);   // ausgefranste Raender
                    switch (kind)
                    {
                        case Haupt:
                        {
                            float g = 0.21f + (fine - 0.5f) * 0.06f + (mid - 0.5f) * 0.04f + (big - 0.5f) * 0.05f;
                            float wear = Mathf.Exp(-Mathf.Pow((a - hw * 0.5f) / 0.45f, 2f)) * 0.05f;   // blanke Reifenspuren
                            g += wear;
                            c = new Color(g, g, g * 1.02f);
                            if (fine > 0.86f) c *= 0.75f;   // Flickstellen/dunkle Koerner
                            // Leitlinien am Rand, Mittellinie gestrichelt (4 m Strich, 8 m Luecke)
                            float worn = 0.75f + fine * 0.2f;
                            if (a > hw - 0.38f && a < hw - 0.22f) c = Color.Lerp(c, new Color(0.86f, 0.86f, 0.84f), worn);
                            if (a < 0.065f && along < 4f) c = Color.Lerp(c, new Color(0.86f, 0.86f, 0.84f), worn);
                            if (a > hw)
                            {
                                float k = (a - hw) / (W - hw);   // Bankett: Schotter, ins Gras ausfransend
                                float gr = 0.42f + (fine - 0.5f) * 0.25f;
                                c = Color.Lerp(new Color(gr, gr * 0.95f, gr * 0.85f), new Color(0.30f, 0.33f, 0.20f), Mathf.Clamp01(k * 1.2f - 0.2f));
                                alpha = fray > k * 1.15f - 0.05f ? 1f : 0f;
                            }
                            else if (a > hw - 0.12f) c *= 0.9f;
                            break;
                        }
                        case Neben:
                        {
                            float g = 0.29f + (fine - 0.5f) * 0.07f + (mid - 0.5f) * 0.05f + (big - 0.5f) * 0.06f;
                            c = new Color(g, g * 0.99f, g * 0.97f);
                            // Flickstellen (dunkle Rechtecke) und Risse
                            float patch = VNoise(off * 0.8f + 7f, along * 0.5f, Mathf.Max(1, Mathf.RoundToInt(L * 0.5f)), 21);
                            if (patch > 0.72f) c *= 0.72f;
                            float crack = Mathf.Abs(Fine(5f, 31) - 0.5f);
                            if (crack < 0.018f) c *= 0.6f;
                            // Rand broeckelt ab
                            float edgeK = (a - (hw - 0.4f)) / (W - hw + 0.4f);
                            if (edgeK > 0f)
                            {
                                float gr = 0.40f + (fine - 0.5f) * 0.25f;
                                if (fray < edgeK * 1.1f + 0.15f) c = Color.Lerp(new Color(gr, gr * 0.94f, gr * 0.82f), new Color(0.30f, 0.33f, 0.20f), Mathf.Clamp01(edgeK * 1.3f - 0.4f));
                                alpha = fray > edgeK * 1.1f - 0.12f ? 1f : 0f;
                            }
                            break;
                        }
                        case Feldweg:
                        {
                            // Schotter/Erde, zwei festgefahrene Spuren, Grasnarbe in der Mitte und am Rand
                            float g = 0.44f + (fine - 0.5f) * 0.18f + (mid - 0.5f) * 0.08f;
                            Color dirt = new Color(g * 1.0f, g * 0.88f, g * 0.70f);
                            float rut = Mathf.Exp(-Mathf.Pow((a - 0.8f) / 0.28f, 2f));
                            dirt = Color.Lerp(dirt, new Color(0.36f, 0.30f, 0.22f) * (0.9f + fine * 0.2f), rut * 0.7f);
                            if (fine > 0.8f) dirt *= 1.15f;   // Steinchen
                            float grassAmt = Mathf.Clamp01(1f - a / 0.42f) * 0.95f;
                            float edgeK = (a - (hw - 0.3f)) / (W - hw + 0.3f);
                            grassAmt = Mathf.Max(grassAmt, Mathf.Clamp01(edgeK * 1.5f));
                            grassAmt *= 0.6f + fray * 0.6f;
                            Color grass = new Color(0.25f, 0.33f, 0.14f) * (0.8f + fine * 0.4f);
                            c = Color.Lerp(dirt, grass, Mathf.Clamp01(grassAmt));
                            if (edgeK > 0f) alpha = fray > edgeK * 1.1f - 0.05f ? 1f : 0f;
                            break;
                        }
                        default:   // Kopfsteinpflaster mit Bordstein
                        {
                            float sx = 0.16f, sy = 0.13f;
                            int row = Mathf.FloorToInt(along / sy);
                            float ox = (row & 1) * sx * 0.5f;
                            int col = Mathf.FloorToInt((off + 20f + ox) / sx);
                            float fx = Mathf.Repeat(off + 20f + ox, sx) / sx, fy = Mathf.Repeat(along, sy) / sy;
                            float stone = Hash(col, row % Mathf.RoundToInt(L / sy), 5);
                            float jd = Mathf.Min(Mathf.Min(fx, 1f - fx) * sx, Mathf.Min(fy, 1f - fy) * sy);
                            float g = 0.36f + (stone - 0.5f) * 0.14f + (fine - 0.5f) * 0.05f;
                            c = new Color(g * 1.02f, g, g * 0.95f);
                            if (jd < 0.012f) c = new Color(0.17f, 0.16f, 0.14f);
                            else c *= 0.85f + Mathf.Clamp01(jd / 0.04f) * 0.15f;   // gewoelbte Steine
                            if (a > hw - 0.3f) { float k = 0.55f + (fine - 0.5f) * 0.08f; c = new Color(k, k, k * 0.98f); if (Mathf.Repeat(along, 1f) < 0.02f) c *= 0.6f; }
                            if (a > hw) { c = new Color(0.30f, 0.31f, 0.27f); alpha = fray > (a - hw) / (W - hw) ? 1f : 0f; }
                            break;
                        }
                    }
                    c.a = alpha;
                    px[y * NU + x] = c;
                }
            var tex = new Texture2D(NU, NV, TextureFormat.RGBA32, true) { name = "Strasse " + kind, anisoLevel = 8, filterMode = FilterMode.Trilinear };
            tex.wrapModeU = TextureWrapMode.Clamp; tex.wrapModeV = TextureWrapMode.Repeat;
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }
    }
}
