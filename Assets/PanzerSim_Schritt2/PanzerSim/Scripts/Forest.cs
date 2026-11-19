using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PanzerSim
{
    /// <summary>
    /// Baeume fuer alle Karten: zehntausende ohne eigene GameObjects.
    /// Sieben Arten (Fichte, Buche, Eiche, Kiefer, Birke, Pappel, Weide), Formen im Code gebaut,
    /// drei Detailstufen (nah voll mit Schatten, mittel einfacher, weit nur die Krone).
    /// Jeder Baum bekommt im Shader aus seiner Lage eine eigene Farbnuance; Wind und Druckwellen biegen ihn.
    /// Staemme als ein Kollisionsnetz je Block (halten Panzer und Granaten auf, Kronen nicht).
    /// </summary>
    public class Forest : MonoBehaviour
    {
        public struct TreeInfo { public Vector3 pos; public float yaw, scale; public byte kind; }

        public const byte Fichte = 0, Buche = 1, Eiche = 2, Kiefer = 3, Birke = 4, Pappel = 5, Weide = 6;
        const int Kinds = 7;
        static readonly float[] TreeHeight = { 24f, 22f, 18f, 21f, 16f, 27f, 12f };
        static readonly float[] CrownRadius = { 4f, 6f, 7.5f, 4.5f, 3.5f, 3f, 6.5f };
        static readonly float[] TrunkR = { 0.28f, 0.30f, 0.42f, 0.25f, 0.17f, 0.30f, 0.40f };
        static readonly float[] TrunkCol = { 7f, 7f, 6f, 9f, 6f, 5f, 3.5f };

        public float nearDist = 260f;    // volles Modell
        public float midDist = 800f;     // einfaches Modell mit Schatten
        public float farDist = 5000f;    // nur Krone; dahinter zeigt der dunkle Waldboden die Waelder
        const float Block = 250f;
        const int Batch = 1023;

        class Chunk
        {
            public Bounds bounds;
            public readonly List<Matrix4x4>[] mats = NewLists();
            public Matrix4x4[][][] batches;   // [art][batch]
        }
        static List<Matrix4x4>[] NewLists() { var l = new List<Matrix4x4>[Kinds]; for (int i = 0; i < Kinds; i++) l[i] = new List<Matrix4x4>(); return l; }

        readonly List<Chunk> chunks = new List<Chunk>();
        const float SuperBlock = 1000f;
        class Super { public Bounds bounds; public readonly List<Chunk> chunks = new List<Chunk>(); public Matrix4x4[][][] batches; }
        readonly List<Super> supers = new List<Super>();
        static Mesh[,] meshes;            // [art, Stufe 0 nah / 1 mittel / 2 weit]
        static Material mat;
        RenderParams rpShadow, rpNoShadow;
        readonly Plane[] planes = new Plane[6];
        public static int StatDrawn;

        /// <summary>Laubbaum fuer Waelder: meist Buche, dazu Eiche, am Rand Birke.</summary>
        public static byte MixedBroadleaf(double roll, bool edge)
        {
            if (edge) return roll < 0.35 ? Birke : roll < 0.7 ? Eiche : Buche;
            return roll < 0.58 ? Buche : roll < 0.88 ? Eiche : Birke;
        }

        public static Forest Create(Transform parent, List<TreeInfo> trees)
        {
            var go = new GameObject("Wald");
            go.transform.SetParent(parent, false);
            var f = go.AddComponent<Forest>();
            f.Build(trees);
            return f;
        }

        void Build(List<TreeInfo> trees)
        {
            var map = new Dictionary<long, Chunk>();
            var colliders = new Dictionary<long, List<TreeInfo>>();
            foreach (var t in trees)
            {
                int k = Mathf.Clamp(t.kind, 0, Kinds - 1);
                long key = ((long)Mathf.FloorToInt(t.pos.x / Block) << 32) ^ (uint)Mathf.FloorToInt(t.pos.z / Block);
                if (!map.TryGetValue(key, out var c))
                {
                    c = new Chunk { bounds = new Bounds(t.pos, Vector3.one) };
                    map[key] = c; chunks.Add(c); colliders[key] = new List<TreeInfo>();
                }
                float h = TreeHeight[k] * t.scale * 1.1f, r = CrownRadius[k] * t.scale * 1.2f;
                c.bounds.Encapsulate(t.pos + Vector3.up * h);
                c.bounds.Encapsulate(t.pos + new Vector3(r, -1f, r));
                c.bounds.Encapsulate(t.pos - new Vector3(r, 1f, r));
                c.mats[k].Add(Matrix4x4.TRS(t.pos, Quaternion.Euler(0f, t.yaw, 0f), Vector3.one * t.scale));
                colliders[key].Add(t);
            }
            var superMap = new Dictionary<long, Super>();
            var superLists = new Dictionary<Super, List<Matrix4x4>[]>();
            foreach (var c in chunks)
            {
                Vector3 cc = c.bounds.center;
                long key = ((long)Mathf.FloorToInt(cc.x / SuperBlock) << 32) ^ (uint)Mathf.FloorToInt(cc.z / SuperBlock);
                if (!superMap.TryGetValue(key, out var s)) { s = new Super { bounds = c.bounds }; superMap[key] = s; supers.Add(s); superLists[s] = NewLists(); }
                s.bounds.Encapsulate(c.bounds); s.chunks.Add(c);
                for (int g = 0; g < Kinds; g++) superLists[s][g].AddRange(c.mats[g]);
            }
            foreach (var s in supers) s.batches = ToBatches(superLists[s]);
            foreach (var c in chunks) { c.batches = ToBatches(c.mats); foreach (var l in c.mats) l.Clear(); }
            foreach (var kv in colliders) BuildColliders(kv.Value);
            if (!NetSync.Headless) SetupRendering();
            var counts = new int[Kinds]; foreach (var t in trees) counts[Mathf.Clamp(t.kind, 0, Kinds - 1)]++;
            Debug.Log($"[Wald] {trees.Count} Baeume in {chunks.Count} Bloecken (Fichte {counts[0]}, Buche {counts[1]}, Eiche {counts[2]}, Kiefer {counts[3]}, Birke {counts[4]}, Pappel {counts[5]}, Weide {counts[6]})");
        }

        static Matrix4x4[][][] ToBatches(List<Matrix4x4>[] lists)
        {
            var res = new Matrix4x4[Kinds][][];
            for (int g = 0; g < Kinds; g++)
            {
                var all = lists[g]; int n = (all.Count + Batch - 1) / Batch;
                res[g] = new Matrix4x4[n][];
                for (int b = 0; b < n; b++) res[g][b] = all.GetRange(b * Batch, Mathf.Min(Batch, all.Count - b * Batch)).ToArray();
            }
            return res;
        }

        void BuildColliders(List<TreeInfo> list)
        {
            var v = new List<Vector3>(list.Count * 8); var tri = new List<int>(list.Count * 24);
            foreach (var t in list)
            {
                int kind = Mathf.Clamp(t.kind, 0, Kinds - 1);
                float r = TrunkR[kind] * t.scale, h = TrunkCol[kind] * t.scale;
                int b = v.Count;
                for (int k = 0; k < 4; k++)
                {
                    float a = (t.yaw + 45f + k * 90f) * Mathf.Deg2Rad;
                    Vector3 o = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    v.Add(t.pos + o - Vector3.up * 0.5f); v.Add(t.pos + o + Vector3.up * h);
                }
                for (int k = 0; k < 4; k++)
                {
                    int a0 = b + k * 2, a1 = a0 + 1, b0 = b + ((k + 1) % 4) * 2, b1 = b0 + 1;
                    tri.Add(a0); tri.Add(a1); tri.Add(b1); tri.Add(a0); tri.Add(b1); tri.Add(b0);
                }
            }
            var m = new Mesh { indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16, name = "Staemme" };
            m.SetVertices(v); m.SetTriangles(tri, 0);
            var go = new GameObject("Staemme");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshCollider>().sharedMesh = m;
        }

        // ---------------- Zeichnen ----------------
        /// <summary>Gemeinsames Material (Shader BaumMesh: Vertexfarben, Instancing, Wind) fuer Baeume und Kleinkram (Props).</summary>
        public static Material SharedMaterial()
        {
            if (mat == null)
            {
                var sh = Shader.Find("PanzerSim/BaumMesh");
                if (sh == null) sh = Resources.Load<Shader>("Shaders/BaumMesh");
                if (sh != null) { Vegetation.EnsureInit(); mat = new Material(sh) { enableInstancing = true, name = "Baeume" }; }
                else { mat = new Material(GameMap.Mat(new Color(0.2f, 0.3f, 0.15f))) { enableInstancing = true }; Debug.LogWarning("Wald: Shader BaumMesh fehlt"); }
            }
            return mat;
        }

        void SetupRendering()
        {
            if (meshes == null) meshes = TreeShapes.BuildAll(Kinds);
            if (mat == null)
            {
                var sh = Shader.Find("PanzerSim/BaumMesh");
                if (sh == null) sh = Resources.Load<Shader>("Shaders/BaumMesh");
                if (sh != null) { Vegetation.EnsureInit(); mat = new Material(sh) { enableInstancing = true, name = "Baeume" }; }
                else { mat = new Material(GameMap.Mat(new Color(0.2f, 0.3f, 0.15f))) { enableInstancing = true }; Debug.LogWarning("Wald: Shader BaumMesh fehlt"); }
            }
            rpShadow = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
            rpNoShadow = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true };
        }

        void Update()
        {
            if (NetSync.Headless || mat == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 cp = cam.transform.position;
            float near2 = nearDist * nearDist, mid2 = midDist * midDist, far2 = farDist * farDist;
            int drawn = 0;
            foreach (var s in supers)
            {
                float sd2 = s.bounds.SqrDistance(cp);
                if (sd2 > far2 || !GeometryUtility.TestPlanesAABB(planes, s.bounds)) continue;
                if (sd2 > mid2)
                {
                    var rp = rpNoShadow; rp.worldBounds = s.bounds;
                    for (int g = 0; g < Kinds; g++)
                        foreach (var b in s.batches[g]) { Graphics.RenderMeshInstanced(rp, meshes[g, 2], 0, b); drawn += b.Length; }
                    continue;
                }
                foreach (var c in s.chunks)
                {
                    if (!GeometryUtility.TestPlanesAABB(planes, c.bounds)) continue;
                    float d2 = c.bounds.SqrDistance(cp);
                    int lod = d2 < near2 ? 0 : d2 < mid2 ? 1 : 2;
                    var rp = lod < 2 ? rpShadow : rpNoShadow; rp.worldBounds = c.bounds;
                    for (int g = 0; g < Kinds; g++)
                        foreach (var b in c.batches[g]) { Graphics.RenderMeshInstanced(rp, meshes[g, lod], 0, b); drawn += b.Length; }
                }
            }
            StatDrawn = drawn;
        }
    }

    /// <summary>
    /// Baumformen im Code: Stamm und Aeste als Roehren, Kronen aus verbeulten Kugeln (Laubbaeume) oder
    /// haengenden Astetagen (Fichte). Normalen der Krone zeigen von der Kronenmitte weg (weiches, rundes Licht),
    /// Verdeckung innen/unten dunkler. Vertexfarbe/UV wie im Shader BaumMesh beschrieben.
    /// </summary>
    static class TreeShapes
    {
        class B
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(), uv = new List<Vector3>();
            public readonly List<Color> c = new List<Color>();
            public readonly List<int> t = new List<int>();
            public float H;
            public System.Random r;
            public float Rnd(float a, float b) => (float)(a + r.NextDouble() * (b - a));

            public int Add(Vector3 p, Vector3 nrm, Color col, float ao, float flex)
            {
                v.Add(p); n.Add(nrm.normalized);
                Color lc = col.linear; lc.a = ao;
                c.Add(lc);
                uv.Add(new Vector3(Mathf.Clamp01(p.y / H), flex, H));
                return v.Count - 1;
            }

            /// <summary>Roehre von a nach b (Stamm, Ast), verjuengt, mit Rinden-Flecken (Birke).</summary>
            public void Tube(Vector3 a, Vector3 b, float r0, float r1, int sides, int segs, Color col, Color? spots = null)
            {
                Vector3 ax = (b - a).normalized;
                Vector3 u = Vector3.Cross(ax, Mathf.Abs(ax.y) > 0.9f ? Vector3.right : Vector3.up).normalized, w = Vector3.Cross(ax, u);
                int b0 = v.Count;
                for (int s = 0; s <= segs; s++)
                {
                    float f = s / (float)segs;
                    Vector3 p = Vector3.Lerp(a, b, f); float rr = Mathf.Lerp(r0, r1, f);
                    for (int k = 0; k < sides; k++)
                    {
                        float ang = k * Mathf.PI * 2f / sides;
                        Vector3 d = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                        Color cc = col;
                        if (spots.HasValue && r.NextDouble() < 0.35) cc = spots.Value;
                        else cc *= Rnd(0.88f, 1.08f);
                        float ao = Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(p.y / 3f));
                        Add(p + d * rr, d, cc, ao, Mathf.Clamp01(p.y / H) * 0.25f);
                    }
                }
                for (int s = 0; s < segs; s++)
                    for (int k = 0; k < sides; k++)
                    {
                        int a0 = b0 + s * sides + k, a1 = b0 + s * sides + (k + 1) % sides, c0 = a0 + sides, c1 = a1 + sides;
                        t.Add(a0); t.Add(c1); t.Add(c0); t.Add(a0); t.Add(a1); t.Add(c1);
                    }
            }

            /// <summary>Verbeulte Kugel als Teil einer Krone (Mitte cc, Halbachsen cr der ganzen Krone fuer Licht und Verdeckung).</summary>
            public void Blob(Vector3 c0, Vector3 rad, int rings, int segs, Color col, Vector3 cc, Vector3 cr, float bump = 0.16f)
            {
                int b0 = v.Count;
                float rot = Rnd(0f, Mathf.PI * 2f);
                Vector3 P(float lat, float lon, out Vector3 dir)
                {
                    dir = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                    float j = 1f + Rnd(-bump, bump);
                    return c0 + Vector3.Scale(dir, rad) * j;
                }
                void V(Vector3 p, Vector3 dir)
                {
                    // Licht: Mischung aus Kugel- und Kronen-Normale; Verdeckung: unten und innen dunkler
                    Vector3 q = p - cc;
                    Vector3 cn = new Vector3(q.x / (cr.x * cr.x), q.y / (cr.y * cr.y), q.z / (cr.z * cr.z)).normalized;
                    Vector3 nn = (cn * 0.7f + dir * 0.3f).normalized;
                    float rel = Mathf.Clamp01((p.y - (cc.y - cr.y)) / (2f * cr.y));
                    float outw = Mathf.Clamp01(new Vector3(q.x / cr.x, q.y / cr.y, q.z / cr.z).magnitude);
                    float ao = Mathf.Lerp(0.42f, 1f, Mathf.Clamp01(rel * 0.8f + 0.2f)) * Mathf.Lerp(0.6f, 1f, outw);
                    Color cl = col * Rnd(0.9f, 1.1f);
                    cl = Color.Lerp(cl, cl * new Color(1.15f, 1.12f, 0.9f), rel * 0.5f);   // oben sonniger
                    Add(p, nn, cl, ao, 1f);
                }
                Vector3 dd;
                V(P(Mathf.PI / 2f, 0f, out dd), dd);   // Nordpol
                for (int ri = 1; ri < rings; ri++)
                {
                    float lat = Mathf.PI / 2f - ri * Mathf.PI / rings;
                    for (int k = 0; k < segs; k++) { float lon = rot + (k + (ri & 1) * 0.5f) * Mathf.PI * 2f / segs; V(P(lat, lon, out dd), dd); }
                }
                V(P(-Mathf.PI / 2f, 0f, out dd), dd);   // Suedpol
                int top = b0, bot = v.Count - 1;
                for (int k = 0; k < segs; k++) { t.Add(top); t.Add(b0 + 1 + (k + 1) % segs); t.Add(b0 + 1 + k); }
                for (int ri = 0; ri < rings - 2; ri++)
                {
                    int r0 = b0 + 1 + ri * segs, r1 = r0 + segs;
                    bool odd = ((ri + 1) & 1) == 1;
                    for (int k = 0; k < segs; k++)
                    {
                        int a0 = r0 + k, a1 = r0 + (k + 1) % segs, q0 = r1 + k, q1 = r1 + (k + 1) % segs;
                        if (!odd) { t.Add(a0); t.Add(a1); t.Add(q0); t.Add(a1); t.Add(q1); t.Add(q0); }
                        else { t.Add(a0); t.Add(a1); t.Add(q1); t.Add(a0); t.Add(q1); t.Add(q0); }
                    }
                }
                int last = b0 + 1 + (rings - 2) * segs;
                for (int k = 0; k < segs; k++) { t.Add(bot); t.Add(last + k); t.Add(last + (k + 1) % segs); }
            }

            /// <summary>Astetage einer Fichte: Kegel mit gezackt haengendem Rand und dunkler Unterseite.</summary>
            public void Tier(float y, float rad, float hgt, int sides, Color col, bool under)
            {
                int apex = Add(new Vector3(0f, y + hgt, 0f), Vector3.up, col * 1.05f, 1f, 1f);
                int b0 = v.Count;
                float rot = Rnd(0f, Mathf.PI * 2f);
                for (int k = 0; k < sides; k++)
                {
                    float ang = rot + k * Mathf.PI * 2f / sides;
                    float rr = rad * ((k & 1) == 0 ? 1f : 0.78f) * Rnd(0.9f, 1.1f);
                    Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    Vector3 p = d * rr + Vector3.up * (y - rad * 0.18f * Rnd(0.7f, 1.3f));   // Zweigspitzen haengen
                    Color tip = Color.Lerp(col, col * new Color(1.35f, 1.3f, 1.1f), 0.5f);
                    Add(p, (d * 0.8f + Vector3.up * 0.6f), tip, Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(y / H + 0.3f)), 1f);
                }
                for (int k = 0; k < sides; k++) { t.Add(apex); t.Add(b0 + (k + 1) % sides); t.Add(b0 + k); }
                if (under)
                {
                    int inner = Add(new Vector3(0f, y + hgt * 0.25f, 0f), Vector3.down, col * 0.5f, 0.35f, 0.6f);
                    for (int k = 0; k < sides; k++) { t.Add(inner); t.Add(b0 + k); t.Add(b0 + (k + 1) % sides); }
                }
            }

            public Mesh Make(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        static readonly Color Bark = new Color(0.30f, 0.24f, 0.18f), BarkDark = new Color(0.22f, 0.18f, 0.14f), BarkGrey = new Color(0.42f, 0.40f, 0.37f);

        public static Mesh[,] BuildAll(int kinds)
        {
            var res = new Mesh[kinds, 3];
            for (int k = 0; k < kinds; k++)
                for (int lod = 0; lod < 3; lod++)
                    res[k, lod] = Build(k, lod);
            return res;
        }

        static Mesh Build(int kind, int lod)
        {
            var b = new B { r = new System.Random(100 + kind * 7 + lod) };
            switch (kind)
            {
                case Forest.Fichte:
                {
                    b.H = 24f;
                    Color needle = new Color(0.10f, 0.20f, 0.11f);
                    if (lod < 2) b.Tube(Vector3.zero, new Vector3(0f, 22f, 0f), 0.30f, 0.05f, lod == 0 ? 6 : 4, 1, BarkDark);
                    int tiers = lod == 0 ? 9 : lod == 1 ? 4 : 2, sides = lod == 0 ? 10 : lod == 1 ? 7 : 6;
                    for (int i = 0; i < tiers; i++)
                    {
                        float f = i / (float)tiers;
                        float y = 2.8f + f * 17.5f;
                        float rad = Mathf.Lerp(3.8f, 0.9f, Mathf.Pow(f, 0.9f));
                        float hgt = lod == 2 ? (i == tiers - 1 ? 24f - y : 11f) : Mathf.Lerp(4.2f, 3.6f, f) * (lod == 1 ? 1.8f : 1f);
                        if (i == tiers - 1) hgt = 24f - y;
                        b.Tier(y, rad * (lod == 2 ? 1.05f : 1f), hgt, sides, needle * Mathf.Lerp(0.9f, 1.1f, f), lod < 2);
                    }
                    break;
                }
                case Forest.Buche:
                    b.H = 22f;
                    Broadleaf(b, lod, trunkTop: 11f, trunkR: 0.32f, cc: new Vector3(0f, 14.5f, 0f), cr: new Vector3(5.6f, 7f, 5.6f),
                              leaf: new Color(0.24f, 0.38f, 0.14f), bark: BarkGrey, blobs: 7, branches: 3);
                    break;
                case Forest.Eiche:
                    b.H = 18f;
                    Broadleaf(b, lod, trunkTop: 7f, trunkR: 0.48f, cc: new Vector3(0f, 11f, 0f), cr: new Vector3(7.2f, 5.6f, 7.2f),
                              leaf: new Color(0.21f, 0.31f, 0.12f), bark: Bark, blobs: 8, branches: 4);
                    break;
                case Forest.Kiefer:
                {
                    b.H = 21f;
                    Color upper = new Color(0.62f, 0.38f, 0.22f);
                    if (lod < 2)
                    {
                        b.Tube(Vector3.zero, new Vector3(0.3f, 8f, 0.1f), 0.30f, 0.22f, lod == 0 ? 7 : 5, 1, Bark);
                        b.Tube(new Vector3(0.3f, 8f, 0.1f), new Vector3(-0.2f, 18.5f, 0.3f), 0.22f, 0.1f, lod == 0 ? 6 : 4, 1, upper);
                    }
                    Vector3 cc = new Vector3(0f, 17.5f, 0.2f), cr = new Vector3(4.2f, 2.6f, 4.2f);
                    Color leaf = new Color(0.16f, 0.25f, 0.12f);
                    if (lod == 2) { b.Blob(cc, cr, 2, 6, leaf, cc, cr, 0.1f); break; }
                    int n = lod == 0 ? 5 : 2;
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * 2.4f + 0.5f;
                        Vector3 o = i == 0 ? new Vector3(0f, 1.0f, 0f) : new Vector3(Mathf.Cos(a) * 2.4f, b.Rnd(-1.4f, 0.4f), Mathf.Sin(a) * 2.4f);
                        if (lod == 0 && i > 0) b.Tube(new Vector3(0f, cc.y - 1.5f, 0.2f), cc + o, 0.1f, 0.05f, 4, 1, upper);
                        b.Blob(cc + o, new Vector3(2.6f, 1.6f, 2.6f) * (i == 0 ? 1.2f : b.Rnd(0.85f, 1.05f)) * (lod == 1 ? 1.4f : 1f), lod == 0 ? 4 : 3, lod == 0 ? 7 : 6, leaf, cc, cr);
                    }
                    break;
                }
                case Forest.Birke:
                    b.H = 16f;
                    Broadleaf(b, lod, trunkTop: 12f, trunkR: 0.17f, cc: new Vector3(0.3f, 10.5f, 0f), cr: new Vector3(3.2f, 5.2f, 3.2f),
                              leaf: new Color(0.38f, 0.48f, 0.18f), bark: new Color(0.88f, 0.87f, 0.83f), blobs: 6, branches: 2, spots: new Color(0.15f, 0.14f, 0.13f), blobScale: 0.8f);
                    break;
                case Forest.Pappel:
                    b.H = 27f;
                    Broadleaf(b, lod, trunkTop: 9f, trunkR: 0.30f, cc: new Vector3(0f, 14.5f, 0f), cr: new Vector3(2.3f, 12f, 2.3f),
                              leaf: new Color(0.20f, 0.32f, 0.12f), bark: BarkGrey, blobs: 6, branches: 0, columnar: true);
                    break;
                default:   // Weide
                    b.H = 12f;
                    Broadleaf(b, lod, trunkTop: 3.5f, trunkR: 0.45f, cc: new Vector3(0f, 7.2f, 0f), cr: new Vector3(6.2f, 4.6f, 6.2f),
                              leaf: new Color(0.40f, 0.47f, 0.30f), bark: Bark, blobs: 7, branches: 3, droop: true);
                    break;
            }
            return b.Make($"Baum {kind} Stufe {lod}");
        }

        static void Broadleaf(B b, int lod, float trunkTop, float trunkR, Vector3 cc, Vector3 cr, Color leaf, Color bark, int blobs, int branches,
                              Color? spots = null, float blobScale = 1f, bool columnar = false, bool droop = false)
        {
            if (lod == 2)
            {
                // weit: eine Kronenform (12 Dreiecke)
                b.Blob(cc, cr * 1.02f, 2, 6, leaf, cc, cr, 0.08f);
                return;
            }
            Vector3 top = new Vector3(b.Rnd(-0.3f, 0.3f), trunkTop, b.Rnd(-0.3f, 0.3f));
            b.Tube(Vector3.zero, top, trunkR, trunkR * 0.55f, lod == 0 ? 7 : 5, lod == 0 ? 2 : 1, bark, spots);
            if (lod == 0)
                for (int i = 0; i < branches; i++)
                {
                    float a = i * Mathf.PI * 2f / branches + b.Rnd(-0.4f, 0.4f);
                    Vector3 from = top * b.Rnd(0.55f, 0.85f);
                    Vector3 to = cc + new Vector3(Mathf.Cos(a) * cr.x * 0.55f, b.Rnd(-0.2f, 0.4f) * cr.y, Mathf.Sin(a) * cr.z * 0.55f);
                    b.Tube(from, to, trunkR * 0.45f, trunkR * 0.15f, 4, 1, bark, spots);
                }
            int n = lod == 0 ? blobs : 4;
            int rings = 4, segs = lod == 0 ? 7 : 6;
            for (int i = 0; i < n; i++)
            {
                Vector3 o; Vector3 rad;
                if (columnar)
                {
                    float f = n == 1 ? 0.5f : i / (float)(n - 1);
                    o = new Vector3(b.Rnd(-0.4f, 0.4f), Mathf.Lerp(-cr.y * 0.62f, cr.y * 0.62f, f), b.Rnd(-0.4f, 0.4f));
                    float w = Mathf.Sin(Mathf.Lerp(0.35f, 2.9f, f));
                    rad = new Vector3(cr.x * (0.7f + 0.35f * w), cr.y * (lod == 0 ? 0.38f : 0.6f), cr.z * (0.7f + 0.35f * w));
                }
                else if (i == 0)
                {
                    o = Vector3.zero; rad = cr * (lod == 0 ? 0.62f : 0.66f);
                }
                else
                {
                    // Teilkronen auf einer Kugelschale um die Mitte, eher oben und aussen
                    float a = i * 2.39996f + b.Rnd(-0.3f, 0.3f);
                    float lat = lod == 0 ? b.Rnd(-0.35f, 0.75f) : (i == 1 ? 0.6f : i == 2 ? -0.1f : 0.2f);
                    Vector3 d = new Vector3(Mathf.Cos(a) * Mathf.Cos(lat), Mathf.Sin(lat), Mathf.Sin(a) * Mathf.Cos(lat));
                    o = Vector3.Scale(d, cr) * 0.52f;
                    rad = cr * (lod == 0 ? b.Rnd(0.42f, 0.55f) : 0.52f) * blobScale;
                    if (droop) { o.y -= cr.y * 0.25f; rad.y *= 1.25f; }
                }
                b.Blob(cc + o, rad, rings, segs, leaf, cc, cr);
            }
            if (droop && lod == 0)
            {
                // Weide: Haengezweige als Kranz unter der Krone
                b.Blob(cc + new Vector3(0f, -cr.y * 0.45f, 0f), new Vector3(cr.x * 0.95f, cr.y * 0.6f, cr.z * 0.95f), 3, 9, leaf * 0.9f, cc, cr, 0.22f);
            }
        }
    }
}
