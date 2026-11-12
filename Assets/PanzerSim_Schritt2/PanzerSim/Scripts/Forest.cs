using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PanzerSim
{
    /// <summary>
    /// Wald fuer grosse Karten: zehntausende Baeume ohne eigene GameObjects.
    /// Gezeichnet per GPU-Instancing in Bloecken (nah: volles Modell mit Schatten, weit: einfache Form),
    /// Staemme als ein Kollisionsnetz je Block (halten Panzer und Granaten auf, Kronen nicht).
    /// </summary>
    public class Forest : MonoBehaviour
    {
        public struct TreeInfo { public Vector3 pos; public float yaw, scale; public byte kind; }   // kind 0 Fichte, 1 Laubbaum

        public float nearDist = 420f;     // volles Modell
        public float farDist = 5000f;     // einfache Form bis hier; dahinter zeigt der dunkle Waldboden die Waelder
        const float Block = 250f;
        const int Batch = 1023;

        class Chunk
        {
            public Bounds bounds;
            public readonly List<Matrix4x4>[] mats = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };   // Art 0/1 x Kronenfarbe
            public Matrix4x4[][][] batches;   // [gruppe][batch]
        }

        readonly List<Chunk> chunks = new List<Chunk>();
        // Fern: grosse Bloecke (1 km) mit allen Baeumen darin - wenige Zeichenaufrufe; nah an der Kamera die kleinen Bloecke
        const float SuperBlock = 1000f;
        class Super { public Bounds bounds; public readonly List<Chunk> chunks = new List<Chunk>(); public Matrix4x4[][][] batches; }
        readonly List<Super> supers = new List<Super>();
        Mesh[] nearMesh = new Mesh[2], farMesh = new Mesh[2];
        Material trunkMat; Material[] crownMats;
        RenderParams rpTrunk, rpTrunkNoShadow; RenderParams[] rpCrown, rpCrownFar;
        readonly Plane[] planes = new Plane[6];
        public static int StatDrawn;

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
                long key = ((long)Mathf.FloorToInt(t.pos.x / Block) << 32) ^ (uint)Mathf.FloorToInt(t.pos.z / Block);
                if (!map.TryGetValue(key, out var c))
                {
                    c = new Chunk { bounds = new Bounds(t.pos, Vector3.one) };
                    map[key] = c; chunks.Add(c); colliders[key] = new List<TreeInfo>();
                }
                float h = (t.kind == 0 ? 22f : 18f) * t.scale;
                c.bounds.Encapsulate(t.pos + Vector3.up * h);
                c.bounds.Encapsulate(t.pos + new Vector3(5f, 0f, 5f) * t.scale);
                c.bounds.Encapsulate(t.pos - new Vector3(5f, 0f, 5f) * t.scale);
                int colour = (int)(Mathf.Abs(t.pos.x * 0.37f + t.pos.z * 0.61f)) & 1;
                c.mats[t.kind * 2 + colour].Add(Matrix4x4.TRS(t.pos, Quaternion.Euler(0f, t.yaw, 0f), Vector3.one * t.scale));
                colliders[key].Add(t);
            }
            // grosse Bloecke: Matrizen der kleinen Bloecke zusammenlegen (bevor deren Listen geleert werden)
            var superMap = new Dictionary<long, Super>();
            var superLists = new Dictionary<Super, List<Matrix4x4>[]>();
            foreach (var c in chunks)
            {
                Vector3 cc = c.bounds.center;
                long key = ((long)Mathf.FloorToInt(cc.x / SuperBlock) << 32) ^ (uint)Mathf.FloorToInt(cc.z / SuperBlock);
                if (!superMap.TryGetValue(key, out var s))
                {
                    s = new Super { bounds = c.bounds }; superMap[key] = s; supers.Add(s);
                    superLists[s] = new[] { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
                }
                s.bounds.Encapsulate(c.bounds); s.chunks.Add(c);
                for (int g = 0; g < 4; g++) superLists[s][g].AddRange(c.mats[g]);
            }
            foreach (var s in supers)
            {
                s.batches = new Matrix4x4[4][][];
                for (int g = 0; g < 4; g++)
                {
                    var all = superLists[s][g]; int n = (all.Count + Batch - 1) / Batch;
                    s.batches[g] = new Matrix4x4[n][];
                    for (int b = 0; b < n; b++) s.batches[g][b] = all.GetRange(b * Batch, Mathf.Min(Batch, all.Count - b * Batch)).ToArray();
                }
            }
            foreach (var c in chunks)
            {
                c.batches = new Matrix4x4[4][][];
                for (int g = 0; g < 4; g++)
                {
                    var all = c.mats[g]; int n = (all.Count + Batch - 1) / Batch;
                    c.batches[g] = new Matrix4x4[n][];
                    for (int b = 0; b < n; b++) c.batches[g][b] = all.GetRange(b * Batch, Mathf.Min(Batch, all.Count - b * Batch)).ToArray();
                    all.Clear();
                }
            }
            // Staemme: ein Kollisionsnetz je Block (vierkantige Saeulen, 0,55 m dick, 7 m hoch)
            foreach (var kv in colliders) BuildColliders(kv.Value);
            if (!NetSync.Headless) SetupRendering();
            Debug.Log($"[Wald] {trees.Count} Baeume in {chunks.Count} Bloecken");
        }

        void BuildColliders(List<TreeInfo> list)
        {
            var v = new List<Vector3>(list.Count * 8); var tri = new List<int>(list.Count * 24);
            foreach (var t in list)
            {
                float r = 0.28f * t.scale, h = 7f * t.scale;
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
                    tri.Add(a0); tri.Add(a1); tri.Add(b1); tri.Add(a0); tri.Add(b1); tri.Add(b0);   // nur aussen (Treffer kommen von aussen)
                }
            }
            var m = new Mesh { indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16, name = "Staemme" };
            m.SetVertices(v); m.SetTriangles(tri, 0);
            var go = new GameObject("Staemme");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshCollider>().sharedMesh = m;
        }

        // ---------------- Zeichnen ----------------
        void SetupRendering()
        {
            nearMesh[0] = Spruce(8, 4); farMesh[0] = Spruce(5, 1);
            nearMesh[1] = Broadleaf(true); farMesh[1] = Broadleaf(false);
            trunkMat = Mat(new Color(0.30f, 0.22f, 0.15f));
            crownMats = new[] { Mat(new Color(0.10f, 0.20f, 0.10f)), Mat(new Color(0.12f, 0.23f, 0.11f)),
                                Mat(new Color(0.20f, 0.31f, 0.12f)), Mat(new Color(0.25f, 0.34f, 0.13f)) };
            rpTrunk = new RenderParams(trunkMat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
            rpTrunkNoShadow = new RenderParams(trunkMat) { shadowCastingMode = ShadowCastingMode.Off };
            rpCrown = new RenderParams[4]; rpCrownFar = new RenderParams[4];
            for (int g = 0; g < 4; g++)
            {
                rpCrown[g] = new RenderParams(crownMats[g]) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
                rpCrownFar[g] = new RenderParams(crownMats[g]) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true };
            }
        }

        static Material Mat(Color c)
        {
            var m = new Material(GameMap.Mat(c)) { enableInstancing = true };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
            return m;
        }

        void Update()
        {
            if (NetSync.Headless || trunkMat == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 cp = cam.transform.position;
            float near2 = nearDist * nearDist, far2 = farDist * farDist;
            int drawn = 0;
            foreach (var s in supers)
            {
                float sd2 = s.bounds.SqrDistance(cp);
                if (sd2 > far2 || !GeometryUtility.TestPlanesAABB(planes, s.bounds)) continue;
                if (sd2 > near2)
                {
                    // ganzer Block weit weg: einfache Formen in wenigen Aufrufen
                    for (int g = 0; g < 4; g++)
                        foreach (var b in s.batches[g]) { Graphics.RenderMeshInstanced(rpCrownFar[g], farMesh[g / 2], 1, b); drawn += b.Length; }
                    continue;
                }
                // Block nahe der Kamera: kleine Bloecke einzeln (nah volles Modell, sonst einfach)
                foreach (var c in s.chunks)
                {
                    float d2 = c.bounds.SqrDistance(cp);
                    if (!GeometryUtility.TestPlanesAABB(planes, c.bounds)) continue;
                    bool near = d2 < near2;
                    for (int g = 0; g < 4; g++)
                    {
                        Mesh mesh = near ? nearMesh[g / 2] : farMesh[g / 2];
                        foreach (var b in c.batches[g])
                        {
                            // Unternetz 0 = Stamm, 1 = Krone (weit weg nur Krone)
                            if (near) Graphics.RenderMeshInstanced(rpTrunk, mesh, 0, b);
                            Graphics.RenderMeshInstanced(near ? rpCrown[g] : rpCrownFar[g], mesh, 1, b);
                            drawn += b.Length;
                        }
                    }
                }
            }
            StatDrawn = drawn;
        }

        // ---------------- Baum-Formen (Stamm = Unternetz 0, Krone = Unternetz 1) ----------------
        static Mesh Spruce(int sides, int tiers)
        {
            var v = new List<Vector3>(); var t0 = new List<int>(); var t1 = new List<int>();
            Cylinder(v, t0, 0.22f, 0.12f, 0f, 9f, 6);
            for (int i = 0; i < tiers; i++)
            {
                float y0 = 3.5f + i * (16f / Mathf.Max(tiers, 1)) * 0.82f;
                float top = tiers == 1 ? 22f : y0 + 7.5f - i * 0.6f;
                float r = (tiers == 1 ? 3.2f : 3.6f - i * 0.75f);
                Cone(v, t1, r, y0, top, sides);
            }
            return Build("Fichte", v, t0, t1);
        }

        static Mesh Broadleaf(bool near)
        {
            var v = new List<Vector3>(); var t0 = new List<int>(); var t1 = new List<int>();
            Cylinder(v, t0, 0.3f, 0.18f, 0f, 8f, 6);
            if (near)
            {
                Blob(v, t1, new Vector3(0f, 11f, 0f), new Vector3(4.6f, 4.4f, 4.6f));
                Blob(v, t1, new Vector3(1.6f, 13.5f, 0.8f), new Vector3(3.0f, 3.2f, 3.0f));
                Blob(v, t1, new Vector3(-1.4f, 9.5f, -1.2f), new Vector3(3.2f, 2.8f, 3.2f));
            }
            else
            {
                // Oktaeder als Krone (8 Dreiecke)
                int b = v.Count;
                v.Add(new Vector3(0, 16.5f, 0)); v.Add(new Vector3(0, 6.5f, 0));
                for (int k = 0; k < 4; k++) { float a = k * Mathf.PI / 2f; v.Add(new Vector3(Mathf.Cos(a) * 5f, 11.5f, Mathf.Sin(a) * 5f)); }
                for (int k = 0; k < 4; k++)
                {
                    int p = b + 2 + k, q = b + 2 + (k + 1) % 4;
                    t1.Add(b); t1.Add(q); t1.Add(p);
                    t1.Add(b + 1); t1.Add(p); t1.Add(q);
                }
            }
            return Build("Laubbaum", v, t0, t1);
        }

        static void Cylinder(List<Vector3> v, List<int> t, float r0, float r1, float y0, float y1, int n)
        {
            int b = v.Count;
            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.PI * 2f / n;
                v.Add(new Vector3(Mathf.Cos(a) * r0, y0, Mathf.Sin(a) * r0));
                v.Add(new Vector3(Mathf.Cos(a) * r1, y1, Mathf.Sin(a) * r1));
            }
            for (int k = 0; k < n; k++)
            {
                int a0 = b + k * 2, a1 = a0 + 1, b0 = b + ((k + 1) % n) * 2, b1 = b0 + 1;
                t.Add(a0); t.Add(a1); t.Add(b1); t.Add(a0); t.Add(b1); t.Add(b0);
            }
        }

        static void Cone(List<Vector3> v, List<int> t, float r, float y0, float y1, int n)
        {
            int b = v.Count;
            v.Add(new Vector3(0f, y1, 0f));
            for (int k = 0; k < n; k++)
            {
                float a = k * Mathf.PI * 2f / n + (y0 * 0.7f);
                v.Add(new Vector3(Mathf.Cos(a) * r, y0, Mathf.Sin(a) * r));
            }
            v.Add(new Vector3(0f, y0 + 0.4f, 0f));   // Unterseite leicht hochgezogen
            int under = v.Count - 1;
            for (int k = 0; k < n; k++)
            {
                int p = b + 1 + k, q = b + 1 + (k + 1) % n;
                t.Add(b); t.Add(q); t.Add(p);
                t.Add(under); t.Add(p); t.Add(q);
            }
        }

        static void Blob(List<Vector3> v, List<int> t, Vector3 c, Vector3 s)
        {
            // grobe Kugel: 2 Ringe x 6 + Pole (24 Dreiecke), leicht verbeult
            int b = v.Count; const int n = 6;
            v.Add(c + Vector3.up * s.y);
            float[] lat = { 0.45f, -0.35f };
            for (int r = 0; r < 2; r++)
                for (int k = 0; k < n; k++)
                {
                    float a = (k + r * 0.5f) * Mathf.PI * 2f / n, cl = Mathf.Cos(lat[r] * Mathf.PI * 0.5f) * (0.9f + 0.2f * ((k * 7 + r * 3) % 3) / 2f);
                    v.Add(c + new Vector3(Mathf.Cos(a) * cl * s.x, Mathf.Sin(lat[r] * Mathf.PI * 0.5f) * s.y, Mathf.Sin(a) * cl * s.z));
                }
            v.Add(c - Vector3.up * s.y * 0.8f);
            int top = b, bot = v.Count - 1;
            for (int k = 0; k < n; k++)
            {
                int u0 = b + 1 + k, u1 = b + 1 + (k + 1) % n, l0 = b + 1 + n + k, l1 = b + 1 + n + (k + 1) % n;
                t.Add(top); t.Add(u1); t.Add(u0);
                t.Add(u0); t.Add(u1); t.Add(l0); t.Add(u1); t.Add(l1); t.Add(l0);
                t.Add(bot); t.Add(l0); t.Add(l1);
            }
        }

        static Mesh Build(string name, List<Vector3> v, List<int> t0, List<int> t1)
        {
            // Flaechen-Normalen (harte Kanten wirken bei so wenig Dreiecken besser): Eckpunkte je Dreieck trennen
            var vv = new List<Vector3>(); var a = new List<int>(); var b = new List<int>();
            foreach (int i in t0) { a.Add(vv.Count); vv.Add(v[i]); }
            foreach (int i in t1) { b.Add(vv.Count); vv.Add(v[i]); }
            var m = new Mesh { name = name, subMeshCount = 2 };
            m.SetVertices(vv); m.SetTriangles(a, 0); m.SetTriangles(b, 1);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
