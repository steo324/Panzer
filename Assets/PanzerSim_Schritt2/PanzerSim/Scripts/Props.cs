using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PanzerSim
{
    /// <summary>
    /// Kleinkram auf den Karten: Leitpfosten, Strommasten, Laternen, Baenke, Bushaltestellen, Briefkaesten, Holzstapel,
    /// Heuballen, Zaunfelder, Steine, Buesche, Wegkreuze. Formen im Code gebaut, gezeichnet per GPU-Instancing in Bloecken
    /// (Shader BaumMesh: Vertexfarben, Buesche wiegen im Wind), jede Art nur bis zu ihrer Sichtweite, Schatten nur nah.
    /// Keine Kollision (Panzer walzen das Zeug um); dafuer kosten zehntausende Teile kaum Rechenzeit.
    /// </summary>
    public class Props : MonoBehaviour
    {
        public enum T { Leitpfosten, Strommast, Laterne, Bank, Haltestelle, Briefkasten, Holzstapel, Heuballen, Zaunfeld, Stein, Busch, Wegkreuz, Hochsitz, Count }
        const int N = (int)T.Count;
        //                                    Leit  Mast  Lat   Bank  Halt  Brief Holz  Heu   Zaun  Stein Busch Kreuz Hochs
        static readonly float[] DrawDist   = { 260, 1200, 600,  220,  400,  160,  380,  700,  380,  450,  550,  450,  700 };
        static readonly float[] ShadowDist = { 60,  250,  150,  60,   120,  40,   120,  150,  90,   120,  120,  120,  200 };
        const float Block = 200f, SuperBlock = 1000f, Batch = 1023;

        class Chunk { public Bounds bounds; public readonly List<Matrix4x4>[] lists = new List<Matrix4x4>[N]; public Matrix4x4[][][] batches; public int[] present; }
        class Super { public Bounds bounds; public readonly List<Chunk> chunks = new List<Chunk>(); public Matrix4x4[][][] batches; public int[] present; }
        const float AggDist = 450f;   // weiter weg: ganzer 1-km-Block in wenigen Aufrufen, ohne Schatten
        readonly Dictionary<long, Chunk> map = new Dictionary<long, Chunk>();
        readonly List<Super> supers = new List<Super>();
        readonly int[] counts = new int[N];
        static Mesh[] meshes;
        Material mat;
        readonly Plane[] planes = new Plane[6];
        public static int StatDrawn;

        public static Props Create(Transform parent)
        {
            var go = new GameObject("Kleinkram");
            go.transform.SetParent(parent, false);
            return go.AddComponent<Props>();
        }

        public void Add(T t, Vector3 pos, float yaw, float scale = 1f) => Add(t, pos, yaw, new Vector3(scale, scale, scale));

        public void Add(T t, Vector3 pos, float yaw, Vector3 scale)
        {
            long key = ((long)Mathf.FloorToInt(pos.x / Block) << 32) ^ (uint)Mathf.FloorToInt(pos.z / Block);
            if (!map.TryGetValue(key, out var c)) { c = new Chunk { bounds = new Bounds(pos, Vector3.one * 2f) }; map[key] = c; }
            int k = (int)t;
            if (c.lists[k] == null) c.lists[k] = new List<Matrix4x4>();
            c.lists[k].Add(Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), scale));
            c.bounds.Encapsulate(pos + Vector3.up * 12f * scale.y);
            c.bounds.Encapsulate(pos - Vector3.up);
            counts[k]++;
        }

        public int Count(T t) => counts[(int)t];

        /// <summary>Nach dem letzten Add: Bloecke fertig machen.</summary>
        public void Finish()
        {
            var sm = new Dictionary<long, Super>();
            foreach (var c in map.Values)
            {
                var present = new List<int>();
                c.batches = new Matrix4x4[N][][];
                for (int k = 0; k < N; k++)
                {
                    var l = c.lists[k];
                    if (l == null || l.Count == 0) continue;
                    present.Add(k);
                    int nb = (l.Count + (int)Batch - 1) / (int)Batch;
                    c.batches[k] = new Matrix4x4[nb][];
                    for (int b = 0; b < nb; b++) c.batches[k][b] = l.GetRange(b * (int)Batch, Mathf.Min((int)Batch, l.Count - b * (int)Batch)).ToArray();
                    }
                c.present = present.ToArray();
                c.bounds.Expand(new Vector3(4f, 0f, 4f));
                Vector3 cc = c.bounds.center;
                long key = ((long)Mathf.FloorToInt(cc.x / SuperBlock) << 32) ^ (uint)Mathf.FloorToInt(cc.z / SuperBlock);
                if (!sm.TryGetValue(key, out var s)) { s = new Super { bounds = c.bounds }; sm[key] = s; supers.Add(s); }
                s.bounds.Encapsulate(c.bounds); s.chunks.Add(c);
            }
            // grosse Bloecke: Matrizen aller kleinen Bloecke je Art zusammenlegen
            foreach (var s in supers)
            {
                var present = new List<int>();
                s.batches = new Matrix4x4[N][][];
                for (int k = 0; k < N; k++)
                {
                    var all = new List<Matrix4x4>();
                    foreach (var c in s.chunks) if (c.lists[k] != null) all.AddRange(c.lists[k]);
                    if (all.Count == 0) continue;
                    present.Add(k);
                    int nb = (all.Count + (int)Batch - 1) / (int)Batch;
                    s.batches[k] = new Matrix4x4[nb][];
                    for (int b = 0; b < nb; b++) s.batches[k][b] = all.GetRange(b * (int)Batch, Mathf.Min((int)Batch, all.Count - b * (int)Batch)).ToArray();
                }
                s.present = present.ToArray();
                foreach (var c in s.chunks) for (int k = 0; k < N; k++) c.lists[k] = null;
            }
            if (!NetSync.Headless)
            {
                if (meshes == null) meshes = PropShapes.BuildAll(N);
                mat = Forest.SharedMaterial();
            }
            var sb = new System.Text.StringBuilder("[Kleinkram]");
            for (int k = 0; k < N; k++) if (counts[k] > 0) sb.Append($" {(T)k} {counts[k]}");
            Debug.Log(sb.ToString());
        }

        void Update()
        {
            if (NetSync.Headless || mat == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 cp = cam.transform.position;
            // Zoom (Visier, Fernglas): Sichtweiten mit der Vergroesserung strecken
            float zoom = Mathf.Clamp(60f / Mathf.Max(cam.fieldOfView, 1f), 1f, 8f);
            float maxD = 1200f * zoom;
            int drawn = 0;
            var rpS = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
            var rpN = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true };
            foreach (var s in supers)
            {
                if (s.bounds.SqrDistance(cp) > maxD * maxD || !GeometryUtility.TestPlanesAABB(planes, s.bounds)) continue;
                float sd = Mathf.Sqrt(s.bounds.SqrDistance(cp));
                if (sd > AggDist)
                {
                    var rpf = rpN; rpf.worldBounds = s.bounds;
                    foreach (int k in s.present)
                    {
                        if (sd > DrawDist[k] * zoom) continue;
                        foreach (var b in s.batches[k]) { Graphics.RenderMeshInstanced(rpf, meshes[k], 0, b); drawn += b.Length; }
                    }
                    continue;
                }
                foreach (var c in s.chunks)
                {
                    float d2 = c.bounds.SqrDistance(cp);
                    if (d2 > maxD * maxD || !GeometryUtility.TestPlanesAABB(planes, c.bounds)) continue;
                    float d = Mathf.Sqrt(d2);
                    foreach (int k in c.present)
                    {
                        if (d > DrawDist[k] * zoom) continue;
                        var rp = d < ShadowDist[k] ? rpS : rpN;
                        rp.worldBounds = c.bounds;
                        foreach (var b in c.batches[k]) { Graphics.RenderMeshInstanced(rp, meshes[k], 0, b); drawn += b.Length; }
                    }
                }
            }
            StatDrawn = drawn;
        }
    }

    /// <summary>Formen fuer den Kleinkram (Vertexfarbe, UV wie Shader BaumMesh: starre Teile ohne Wind).</summary>
    static class PropShapes
    {
        class B
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(), uv = new List<Vector3>();
            public readonly List<Color> c = new List<Color>();
            public readonly List<int> t = new List<int>();
            public float H = 2f; public bool sway;
            public readonly System.Random r = new System.Random(5);

            int V(Vector3 p, Vector3 nrm, Color col, float ao = 1f)
            {
                v.Add(p); n.Add(nrm.normalized);
                Color lc = col.linear; lc.a = ao; c.Add(lc);
                uv.Add(sway ? new Vector3(Mathf.Clamp01(p.y / H), 1f, H) : new Vector3(0f, 0f, 1f));
                return v.Count - 1;
            }
            void Quad(Vector3 a, Vector3 b2, Vector3 c2, Vector3 d, Vector3 nrm, Color col, float ao = 1f)
            {
                // Unity: Vorderseite im Uhrzeigersinn; Reihenfolge so pruefen, dass die Normale passt
                if (Vector3.Dot(Vector3.Cross(b2 - a, c2 - a), nrm) < 0f) { var x = b2; b2 = d; d = x; }
                int i0 = V(a, nrm, col, ao), i1 = V(b2, nrm, col, ao), i2 = V(c2, nrm, col, ao), i3 = V(d, nrm, col, ao);
                t.Add(i0); t.Add(i1); t.Add(i2); t.Add(i0); t.Add(i2); t.Add(i3);
            }
            /// <summary>Quader mit Mitte c, Groesse s, gedreht um rotY (Grad) und optional geneigt um X.</summary>
            public void Box(Vector3 c0, Vector3 s, Color col, float rotY = 0f, float tiltX = 0f, bool bottom = false)
            {
                var q = Quaternion.Euler(tiltX, rotY, 0f);
                Vector3 X = q * Vector3.right * s.x / 2f, Y = q * Vector3.up * s.y / 2f, Z = q * Vector3.forward * s.z / 2f;
                Color side = col * 0.92f; side.a = 1f;
                Quad(c0 - X - Y + Z, c0 - X + Y + Z, c0 + X + Y + Z, c0 + X - Y + Z, Z, side);
                Quad(c0 + X - Y - Z, c0 + X + Y - Z, c0 - X + Y - Z, c0 - X - Y - Z, -Z, side);
                Quad(c0 + X - Y + Z, c0 + X + Y + Z, c0 + X + Y - Z, c0 + X - Y - Z, X, col);
                Quad(c0 - X - Y - Z, c0 - X + Y - Z, c0 - X + Y + Z, c0 - X - Y + Z, -X, col);
                Quad(c0 - X + Y + Z, c0 - X + Y - Z, c0 + X + Y - Z, c0 + X + Y + Z, Y, col * 1.05f);
                if (bottom) Quad(c0 - X - Y - Z, c0 - X - Y + Z, c0 + X - Y + Z, c0 + X - Y - Z, -Y, col * 0.6f);
            }
            /// <summary>Zylinder von a nach b (Achse beliebig), optional mit Deckeln.</summary>
            public void Cyl(Vector3 a, Vector3 b2, float r0, float r1, int sides, Color col, bool caps = false, Color? capCol = null)
            {
                Vector3 ax = (b2 - a).normalized;
                Vector3 u = Vector3.Cross(ax, Mathf.Abs(ax.y) > 0.9f ? Vector3.right : Vector3.up).normalized, w = Vector3.Cross(ax, u);
                int b0 = v.Count;
                for (int k = 0; k <= sides; k++)
                {
                    float ang = k * Mathf.PI * 2f / sides;
                    Vector3 d = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                    V(a + d * r0, d, col * (0.92f + 0.08f * Mathf.Cos(ang * 3f)), 0.85f);
                    V(b2 + d * r1, d, col * (0.92f + 0.08f * Mathf.Cos(ang * 3f)));
                }
                for (int k = 0; k < sides; k++)
                {
                    int a0 = b0 + k * 2, a1 = a0 + 1, c0 = a0 + 2, c1 = a0 + 3;
                    // Richtung pruefen ueber die Normale
                    Vector3 nn = v[a0] - a;
                    if (Vector3.Dot(Vector3.Cross(v[a1] - v[a0], v[c1] - v[a0]), nn) >= 0f) { t.Add(a0); t.Add(a1); t.Add(c1); t.Add(a0); t.Add(c1); t.Add(c0); }
                    else { t.Add(a0); t.Add(c1); t.Add(a1); t.Add(a0); t.Add(c0); t.Add(c1); }
                }
                if (caps)
                {
                    Color cc = capCol ?? col;
                    foreach (var (p, rr, nrm) in new[] { (a, r0, -ax), (b2, r1, ax) })
                    {
                        int center = V(p, nrm, cc);
                        int first = v.Count;
                        for (int k = 0; k < sides; k++) { float ang = k * Mathf.PI * 2f / sides; V(p + (u * Mathf.Cos(ang) + w * Mathf.Sin(ang)) * rr, nrm, cc * (0.85f + 0.15f * ((k * 7) % 3) / 2f)); }
                        for (int k = 0; k < sides; k++)
                        {
                            int i1 = first + k, i2 = first + (k + 1) % sides;
                            if (Vector3.Dot(Vector3.Cross(v[i1] - p, v[i2] - p), nrm) >= 0f) { t.Add(center); t.Add(i1); t.Add(i2); }
                            else { t.Add(center); t.Add(i2); t.Add(i1); }
                        }
                    }
                }
            }
            /// <summary>Verbeulte Kugel (Stein, Busch).</summary>
            public void Blob(Vector3 c0, Vector3 rad, int rings, int segs, Color col, float bump, float aoLow)
            {
                int b0 = v.Count;
                var ring = new List<int[]>();
                int top = V(c0 + Vector3.up * rad.y, Vector3.up, col * 1.08f);
                for (int ri = 1; ri < rings; ri++)
                {
                    float lat = Mathf.PI / 2f - ri * Mathf.PI / rings;
                    var idx = new int[segs];
                    for (int k = 0; k < segs; k++)
                    {
                        float lon = (k + (ri & 1) * 0.5f) * Mathf.PI * 2f / segs;
                        Vector3 d = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                        float j = 1f + (float)(r.NextDouble() - 0.5) * 2f * bump;
                        Vector3 p = c0 + Vector3.Scale(d, rad) * j;
                        float ao = Mathf.Lerp(aoLow, 1f, (d.y + 1f) * 0.5f);
                        idx[k] = V(p, d, col * (0.9f + (float)r.NextDouble() * 0.2f), ao);
                    }
                    ring.Add(idx);
                }
                int bot = V(c0 - Vector3.up * rad.y, Vector3.down, col * 0.6f, aoLow);
                void Tri(int a, int b2, int c2)
                {
                    Vector3 nrm = (v[a] + v[b2] + v[c2]) / 3f - c0;
                    if (Vector3.Dot(Vector3.Cross(v[b2] - v[a], v[c2] - v[a]), nrm) >= 0f) { t.Add(a); t.Add(b2); t.Add(c2); } else { t.Add(a); t.Add(c2); t.Add(b2); }
                }
                for (int k = 0; k < segs; k++) Tri(top, ring[0][k], ring[0][(k + 1) % segs]);
                for (int ri = 0; ri + 1 < ring.Count; ri++)
                    for (int k = 0; k < segs; k++)
                    {
                        Tri(ring[ri][k], ring[ri][(k + 1) % segs], ring[ri + 1][k]);
                        Tri(ring[ri][(k + 1) % segs], ring[ri + 1][(k + 1) % segs], ring[ri + 1][k]);
                    }
                var last = ring[ring.Count - 1];
                for (int k = 0; k < segs; k++) Tri(bot, last[(k + 1) % segs], last[k]);
            }
            public Mesh Make(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetNormals(n); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        static readonly Color White = new Color(0.92f, 0.92f, 0.90f), Black = new Color(0.08f, 0.08f, 0.08f), Wood = new Color(0.42f, 0.30f, 0.20f),
            DarkWood = new Color(0.30f, 0.22f, 0.15f), Grey = new Color(0.45f, 0.46f, 0.47f), DarkGrey = new Color(0.25f, 0.26f, 0.27f),
            Straw = new Color(0.78f, 0.66f, 0.38f), Stone = new Color(0.50f, 0.49f, 0.47f), Leaf = new Color(0.20f, 0.33f, 0.13f);

        public static Mesh[] BuildAll(int n)
        {
            var res = new Mesh[n];
            for (int k = 0; k < n; k++) res[k] = Build((Props.T)k);
            return res;
        }

        static Mesh Build(Props.T t)
        {
            var b = new B();
            switch (t)
            {
                case Props.T.Leitpfosten:
                    // weisser Pfosten mit schwarzem Band und Rueckstrahler (Vorderseite +z)
                    b.Box(new Vector3(0f, 0.5f, 0f), new Vector3(0.12f, 1.0f, 0.10f), White);
                    b.Box(new Vector3(0f, 0.83f, 0f), new Vector3(0.125f, 0.25f, 0.105f), Black);
                    b.Box(new Vector3(0f, 0.86f, 0.055f), new Vector3(0.05f, 0.12f, 0.01f), new Color(0.95f, 0.45f, 0.05f));
                    break;
                case Props.T.Strommast:
                    // Holzmast mit Traverse und drei Isolatoren (Leitungen haengen bei x = -0.75, 0, +0.75 in 8,7 m)
                    b.Cyl(Vector3.zero, new Vector3(0f, 9.2f, 0f), 0.15f, 0.11f, 7, Wood);
                    b.Box(new Vector3(0f, 8.5f, 0f), new Vector3(1.8f, 0.12f, 0.12f), DarkWood);
                    foreach (float x in new[] { -0.75f, 0.75f }) b.Cyl(new Vector3(x, 8.56f, 0f), new Vector3(x, 8.75f, 0f), 0.05f, 0.04f, 5, White);
                    b.Cyl(new Vector3(0f, 9.15f, 0f), new Vector3(0f, 9.35f, 0f), 0.05f, 0.04f, 5, White);
                    break;
                case Props.T.Laterne:
                    // Mast mit Ausleger zur Strasse (+z), Leuchte
                    b.Cyl(Vector3.zero, new Vector3(0f, 5.6f, 0f), 0.08f, 0.05f, 6, DarkGrey);
                    b.Cyl(new Vector3(0f, 5.5f, 0f), new Vector3(0f, 5.75f, 0.9f), 0.04f, 0.035f, 5, DarkGrey);
                    b.Box(new Vector3(0f, 5.72f, 1.05f), new Vector3(0.28f, 0.12f, 0.5f), Grey);
                    b.Box(new Vector3(0f, 5.65f, 1.05f), new Vector3(0.24f, 0.03f, 0.42f), new Color(1f, 0.95f, 0.8f));
                    break;
                case Props.T.Bank:
                    b.Box(new Vector3(0f, 0.45f, 0f), new Vector3(1.8f, 0.05f, 0.42f), Wood);
                    b.Box(new Vector3(0f, 0.78f, -0.2f), new Vector3(1.8f, 0.28f, 0.04f), Wood, 0f, -12f);
                    foreach (float x in new[] { -0.75f, 0.75f }) { b.Box(new Vector3(x, 0.22f, 0f), new Vector3(0.06f, 0.44f, 0.4f), DarkGrey); b.Box(new Vector3(x, 0.62f, -0.21f), new Vector3(0.06f, 0.4f, 0.05f), DarkGrey); }
                    break;
                case Props.T.Haltestelle:
                    // Wartehaeuschen: Rueckwand (Glas), Seitenwand, Dach, Bank; daneben Haltestellenschild (gelbes Rund mit gruenem Rand)
                    b.Box(new Vector3(0f, 1.1f, -0.7f), new Vector3(3.0f, 2.0f, 0.06f), new Color(0.55f, 0.62f, 0.66f));
                    b.Box(new Vector3(-1.5f, 1.1f, 0f), new Vector3(0.06f, 2.0f, 1.4f), new Color(0.55f, 0.62f, 0.66f));
                    foreach (float x in new[] { -1.5f, 1.5f }) b.Box(new Vector3(x, 1.1f, 0.68f), new Vector3(0.08f, 2.2f, 0.08f), DarkGrey);
                    b.Box(new Vector3(0f, 2.25f, 0f), new Vector3(3.3f, 0.1f, 1.7f), DarkGrey);
                    b.Box(new Vector3(0f, 0.45f, -0.5f), new Vector3(2.4f, 0.05f, 0.35f), Wood);
                    b.Cyl(new Vector3(2.1f, 0f, 0.6f), new Vector3(2.1f, 2.6f, 0.6f), 0.04f, 0.04f, 5, Grey);
                    b.Cyl(new Vector3(2.1f, 2.45f, 0.58f), new Vector3(2.1f, 2.45f, 0.62f), 0.28f, 0.28f, 10, new Color(0.15f, 0.55f, 0.25f), true, new Color(0.95f, 0.85f, 0.15f));
                    break;
                case Props.T.Briefkasten:
                    b.Box(new Vector3(0f, 0.55f, 0f), new Vector3(0.07f, 1.1f, 0.07f), DarkGrey);
                    b.Box(new Vector3(0f, 1.25f, 0f), new Vector3(0.34f, 0.32f, 0.22f), new Color(0.15f, 0.30f, 0.20f));
                    b.Box(new Vector3(0f, 1.42f, 0f), new Vector3(0.38f, 0.04f, 0.26f), DarkGrey);
                    break;
                case Props.T.Holzstapel:
                    // Rundholz-Stapel: 3 Lagen Stammstuecke (laengs x), Stirnseiten hell
                    for (int row = 0; row < 3; row++)
                        for (int i = 0; i < 5 - row; i++)
                        {
                            float r = 0.17f + (float)(b.r.NextDouble() * 0.05);
                            float z = (i - (4 - row) / 2f) * 0.38f, y = 0.18f + row * 0.32f;
                            b.Cyl(new Vector3(-1.4f, y, z), new Vector3(1.4f, y, z), r, r, 7, new Color(0.40f, 0.30f, 0.22f), true, new Color(0.80f, 0.65f, 0.45f));
                        }
                    break;
                case Props.T.Heuballen:
                    b.Cyl(new Vector3(-0.6f, 0.72f, 0f), new Vector3(0.6f, 0.72f, 0f), 0.75f, 0.75f, 12, Straw, true, Straw * 1.1f);
                    break;
                case Props.T.Zaunfeld:
                    // 3 m: Pfosten am Anfang, zwei Latten bis zum naechsten Pfosten (laengs +z)
                    b.Box(new Vector3(0f, 0.6f, 0f), new Vector3(0.12f, 1.3f, 0.12f), DarkWood);
                    foreach (float y in new[] { 0.45f, 0.95f }) b.Box(new Vector3(0.07f, y, 1.5f), new Vector3(0.04f, 0.1f, 3.0f), Wood);
                    break;
                case Props.T.Stein:
                    b.Blob(new Vector3(0f, 0.25f, 0f), new Vector3(0.7f, 0.5f, 0.55f), 4, 7, Stone, 0.2f, 0.55f);
                    break;
                case Props.T.Busch:
                    b.sway = true; b.H = 1.8f;
                    b.Blob(new Vector3(0f, 0.75f, 0f), new Vector3(1.0f, 0.8f, 1.0f), 4, 7, Leaf, 0.22f, 0.5f);
                    b.Blob(new Vector3(0.6f, 0.6f, 0.4f), new Vector3(0.7f, 0.6f, 0.7f), 3, 6, Leaf * 1.1f, 0.22f, 0.5f);
                    b.Blob(new Vector3(-0.5f, 0.55f, -0.4f), new Vector3(0.65f, 0.55f, 0.65f), 3, 6, Leaf * 0.9f, 0.22f, 0.5f);
                    break;
                case Props.T.Wegkreuz:
                    // Holzkreuz mit kleinem Dach auf Steinsockel
                    b.Box(new Vector3(0f, 0.3f, 0f), new Vector3(0.7f, 0.6f, 0.7f), Stone);
                    b.Box(new Vector3(0f, 1.9f, 0f), new Vector3(0.16f, 2.8f, 0.14f), DarkWood);
                    b.Box(new Vector3(0f, 2.6f, 0f), new Vector3(1.2f, 0.14f, 0.12f), DarkWood);
                    b.Box(new Vector3(0f, 3.4f, 0f), new Vector3(0.5f, 0.06f, 0.4f), DarkGrey, 0f, 0f);
                    break;
                case Props.T.Hochsitz:
                    // Jaegerhochsitz am Waldrand: vier Stangen, Kanzel, Leiter
                    foreach (var (x, z) in new[] { (-0.8f, -0.8f), (0.8f, -0.8f), (-0.6f, 0.6f), (0.6f, 0.6f) }) b.Cyl(new Vector3(x, 0f, z), new Vector3(x * 0.7f, 4.0f, z * 0.7f), 0.07f, 0.06f, 5, Wood);
                    b.Box(new Vector3(0f, 4.6f, 0f), new Vector3(1.4f, 1.2f, 1.4f), DarkWood);
                    b.Box(new Vector3(0f, 5.35f, 0f), new Vector3(1.7f, 0.08f, 1.7f), DarkGrey);
                    b.Box(new Vector3(0f, 2.0f, 1.3f), new Vector3(0.5f, 4.2f, 0.06f), Wood, 0f, -18f);
                    break;
            }
            return b.Make("Kleinkram " + t);
        }
    }

    /// <summary>Stromleitungen zwischen Masten: durchhaengende Draehte als ein Netz je Block (dunkles URP-Material, ohne Kollision).</summary>
    public static class Wires
    {
        public static void Build(Transform root, List<List<(Vector3 pos, float yaw)>> lines)
        {
            if (NetSync.Headless || lines.Count == 0) return;
            var parts = new Dictionary<long, (List<Vector3> v, List<Vector3> n, List<int> t)>();
            float[] offX = { -0.75f, 0.75f, 0f }; float[] offY = { 8.72f, 8.72f, 9.33f };
            foreach (var line in lines)
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    var a = line[i]; var b = line[i + 1];
                    Vector3 mid = (a.pos + b.pos) / 2f;
                    long key = ((long)Mathf.FloorToInt(mid.x / 500f) << 32) ^ (uint)Mathf.FloorToInt(mid.z / 500f);
                    if (!parts.TryGetValue(key, out var p)) { p = (new List<Vector3>(), new List<Vector3>(), new List<int>()); parts[key] = p; }
                    var qa = Quaternion.Euler(0f, a.yaw, 0f); var qb = Quaternion.Euler(0f, b.yaw, 0f);
                    float span = Vector3.Distance(a.pos, b.pos), sag = span * span / 4000f + 0.3f;
                    for (int w = 0; w < 3; w++)
                    {
                        Vector3 pa = a.pos + qa * new Vector3(offX[w], offY[w], 0f), pb = b.pos + qb * new Vector3(offX[w], offY[w], 0f);
                        const int S = 8; const float R = 0.025f;
                        Vector3 prev = pa;
                        for (int s = 1; s <= S; s++)
                        {
                            float f = s / (float)S;
                            Vector3 cur = Vector3.Lerp(pa, pb, f) - Vector3.up * sag * 4f * f * (1f - f);
                            Strand(p.v, p.n, p.t, prev, cur, R);
                            prev = cur;
                        }
                    }
                }
            var mat = new Material(GameMap.Mat(new Color(0.12f, 0.12f, 0.12f)));
            var parent = new GameObject("Stromleitungen").transform; parent.SetParent(root, false);
            foreach (var p in parts.Values)
            {
                var m = new Mesh { name = "Leitungen", indexFormat = p.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(p.v); m.SetNormals(p.n); m.SetTriangles(p.t, 0); m.RecalculateBounds();
                var go = new GameObject("Leitungen"); go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // Dreikant-Strang (3 Flaechen) von a nach b
        static void Strand(List<Vector3> v, List<Vector3> n, List<int> t, Vector3 a, Vector3 b, float r)
        {
            Vector3 ax = (b - a).normalized;
            Vector3 u = Vector3.Cross(ax, Vector3.up).normalized; if (u.sqrMagnitude < 0.01f) u = Vector3.right;
            Vector3 w = Vector3.Cross(ax, u);
            int b0 = v.Count;
            for (int k = 0; k < 3; k++)
            {
                float ang = k * Mathf.PI * 2f / 3f;
                Vector3 d = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                v.Add(a + d * r); n.Add(d); v.Add(b + d * r); n.Add(d);
            }
            for (int k = 0; k < 3; k++)
            {
                int a0 = b0 + k * 2, a1 = a0 + 1, c0 = b0 + ((k + 1) % 3) * 2, c1 = c0 + 1;
                Vector3 nn = v[a0] - a;
                if (Vector3.Dot(Vector3.Cross(v[a1] - v[a0], v[c1] - v[a0]), nn) >= 0f) { t.Add(a0); t.Add(a1); t.Add(c1); t.Add(a0); t.Add(c1); t.Add(c0); }
                else { t.Add(a0); t.Add(c1); t.Add(a1); t.Add(a0); t.Add(c0); t.Add(c1); }
            }
        }
    }
}
