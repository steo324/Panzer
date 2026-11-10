using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace PanzerSim
{
    /// <summary>
    /// Weizen auf den Feldern, gezeichnet komplett von der GPU:
    ///  - Die CPU erzeugt die Pflanzen kachelweise (8-m-Kacheln nah, 32-m-Kacheln Fernbilder) und laedt sie einmal hoch.
    ///  - Ein Compute-Shader waehlt pro Frame aus, was im Bild ist (Sichtkegel, Entfernung, Dichte, Detailstufe).
    ///  - Gezeichnet wird per Indirect-Draw: ein Aufruf pro Pflanzenart, Biegen passiert im Vertex-Shader.
    /// Die CPU kuemmert sich nur noch um Panzer-Kontakt und die paar Pflanzen, die sich gerade bewegen.
    ///
    /// Panzer drueberfahren:
    ///  - vor dem Bug: Halme werden nach vorne (in Fahrtrichtung) weggedrueckt
    ///  - unter den Ketten: plattgewalzt, bleiben liegen, verschwinden nach einer Weile im Boden
    ///  - unter der Wanne: so weit gebogen, dass sie unter den Wannenboden passen, danach federn sie zurueck
    /// </summary>
    public class WheatField : MonoBehaviour
    {
        // ---- Einstellungen ----
        public float lodDist = 30f;        // danach Bueschel mit halb so vielen Halmen
        public float nearDist = 50f;       // volle Dichte
        public float farDist = 105f;       // Ende der 3D-Halme ...
        public float billboardStart = 92f; // ... ab hier Bilder, die zur Kamera schauen
        public float billboardFull = 260f;
        public float billboardDist = 750f;
        public float stalkDist = 16f;      // Einzelhalme (Scan) nur in der Naehe
        public float shadowDist = 15f;     // nur so nah werfen Halme Schatten
        public float flatLifetime = 25f;
        public float sinkTime = 3f;

        const float Chunk = 8f, Tile = 32f, BillSpacing = 1.15f;
        const float RowSpacing = 0.75f, PlantSpacing = 0.62f;
        const float TramlineEvery = 18f, TramlineGauge = 1.8f, TramlineWidth = 0.55f;
        const int PerChunk = 384, ChunkSlots = 1280, PerTile = 1024, TileSlots = 1600;

        System.Func<float, float, bool> grows;
        System.Func<float, float, float> groundY;
        System.Func<float, float, bool> rowsAlongX;
        System.Func<float, float, bool> grassGrows;
        public float grassDist = 45f;
        bool grassGrowsSet;

        // ---- CPU-Daten ----
        class Plant
        {
            public Vector3 pos;
            public float yaw, scale, lodRank;
            public byte kind;               // 0 Bueschel, 1/2 Einzelhalm, 3 Gras
            public byte state;              // 0 steht, 1 gedrueckt, 2 platt, 3 sinkt, 4 weg
            public float angle, vel, target, dirYaw, timer, rest;
            public bool active;
            public ChunkData chunk; public int index;
        }
        class ChunkData { public Plant[] plants; public bool touched, dirty; public int slot = -1; public int seen; public int cx, cz; }
        class FarTile { public int slot = -1; public int seen; }

        readonly Dictionary<long, ChunkData> chunks = new Dictionary<long, ChunkData>();
        readonly Dictionary<long, FarTile> tiles = new Dictionary<long, FarTile>();
        readonly List<Plant> active = new List<Plant>();
        readonly List<TankController> tanks = new List<TankController>();
        readonly Stack<int> freeChunkSlots = new Stack<int>(), freeTileSlots = new Stack<int>();
        float tankRefresh;
        static readonly float[] tipTable = new float[91];

        // ---- GPU ----
        [StructLayout(LayoutKind.Sequential)]
        struct GpuPlant { public Vector3 pos; public float scale, yaw, angle, lodRank, kindState; }
        const int GpuPlantSize = 32;

        ComputeShader cull;
        int kPlants, kBills;
        ComputeBuffer plantBuf, billBuf, argsBuf;
        readonly ComputeBuffer[] outBufs = new ComputeBuffer[7];   // 0-4 Weizen, 5 Fernbilder, 6 Gras
        readonly GpuPlant[] chunkUpload = new GpuPlant[PerChunk];
        readonly Vector4[] tileUpload = new Vector4[PerTile];
        Mesh[] meshes = new Mesh[5];     // Bueschel, Halm1, Halm2, BueschelFern, Gras
        Material[] mats = new Material[5];
        Mesh quad; Material billMat;
        readonly MaterialPropertyBlock[] blocks = new MaterialPropertyBlock[7];
        readonly Vector4[] planeVecs = new Vector4[6];
        readonly Plane[] planes = new Plane[6];
        bool ready;
        Vector3 lastStreamPos = new Vector3(1e9f, 0, 0);

        // fuer die Entwickler-Anzeige (F3) - Zahlen kommen verzoegert von der GPU zurueck
        public static int StatPlants, StatBillboards, StatActive, StatChunks, StatTiles, StatTris;
        static readonly int[] TrisPerKind = { 720, 720, 360, 1362, 1428, 2, 6 };
        float statTimer; bool statPending;

        public static WheatField Create(Transform parent, System.Func<float, float, bool> grows,
            System.Func<float, float, float> groundY, System.Func<float, float, bool> rowsAlongX,
            System.Func<float, float, bool> grassGrows = null)
        {
            if (!SystemInfo.supportsComputeShaders) { Debug.LogWarning("Weizen: Grafikkarte ohne Compute-Shader - kein Weizen."); return null; }
            var prefab = Resources.Load<GameObject>("Modelle/Weizen");
            var cs = Resources.Load<ComputeShader>("Shaders/WeizenCull");
            var sh = Shader.Find("PanzerSim/WeizenGPU");
            if (sh == null) { var s2 = Resources.Load<Shader>("Shaders/WeizenGPU"); sh = s2; }
            if (prefab == null || cs == null || sh == null) { Debug.LogWarning("Weizen: Modell, Compute-Shader oder Shader fehlt - Felder ohne Weizen."); return null; }
            var go = new GameObject("Weizen");
            go.transform.SetParent(parent, false);
            var wf = go.AddComponent<WheatField>();
            wf.grows = grows; wf.groundY = groundY; wf.rowsAlongX = rowsAlongX;
            wf.grassGrows = grassGrows ?? ((x, z) => false);
            wf.grassGrowsSet = grassGrows != null;
            wf.Setup(prefab, cs, sh);
            return wf;
        }

        void Setup(GameObject prefab, ComputeShader cs, Shader plantShader)
        {
            string[] names = { "Bueschel", "Halm1", "Halm2", "BueschelFern" };
            float[] heights = { 1f, 1.05f, 1.05f, 1f };
            for (int k = 0; k < names.Length; k++)
            {
                var t = FindDeep(prefab.transform, names[k]);
                var mf = t != null ? t.GetComponent<MeshFilter>() : null;
                var mr = t != null ? t.GetComponent<MeshRenderer>() : null;
                if (mf == null || mr == null) { Debug.LogWarning($"Weizen: '{names[k]}' nicht im Modell gefunden."); return; }
                meshes[k] = mf.sharedMesh;
                var m = new Material(plantShader);
                m.SetTexture("_BaseMap", BaseTexture(mr.sharedMaterial));
                m.SetFloat("_Cutoff", k == 0 || k == 3 ? 0.3f : 0.35f);
                m.SetFloat("_Height", heights[k]);
                m.SetColor("_Tint", new Color(1f, 0.97f, 0.9f));
                mats[k] = m;
            }

            Vegetation.EnsureInit();
            // Gras fuer die Wiesen (aus Modelle/Pflanzen.glb)
            var plants = this.grassGrows != null && grassGrowsSet ? Resources.Load<GameObject>("Modelle/Pflanzen") : null;
            var gt = plants != null ? FindDeep(plants.transform, "Gras") : null;
            if (gt != null)
            {
                meshes[4] = gt.GetComponent<MeshFilter>().sharedMesh;
                var gm = new Material(plantShader);
                gm.SetTexture("_BaseMap", BaseTexture(gt.GetComponent<MeshRenderer>().sharedMaterial));
                gm.SetFloat("_Cutoff", 0.45f); gm.SetFloat("_Height", 0.55f);
                gm.SetColor("_Tint", new Color(0.9f, 0.95f, 0.85f));
                mats[4] = gm;
            }
            else grassGrows = (x, z) => false;
            cull = cs;
            kPlants = cull.FindKernel("CullPlants");
            kBills = cull.FindKernel("CullBillboards");
            plantBuf = new ComputeBuffer(ChunkSlots * PerChunk, GpuPlantSize);
            billBuf = new ComputeBuffer(TileSlots * PerTile, 16);
            var dead = new GpuPlant[ChunkSlots * PerChunk];
            for (int i = 0; i < dead.Length; i++) dead[i].kindState = 4;
            plantBuf.SetData(dead);
            billBuf.SetData(new Vector4[TileSlots * PerTile]);
            for (int i = 0; i < 5; i++) outBufs[i] = new ComputeBuffer(ChunkSlots * PerChunk / 2, 4, ComputeBufferType.Append);
            outBufs[5] = new ComputeBuffer(TileSlots * PerTile / 2, 4, ComputeBufferType.Append);
            outBufs[6] = new ComputeBuffer(ChunkSlots * PerChunk / 2, 4, ComputeBufferType.Append);
            argsBuf = new ComputeBuffer(7 * 5, 4, ComputeBufferType.IndirectArguments);
            for (int i = ChunkSlots - 1; i >= 0; i--) freeChunkSlots.Push(i);
            for (int i = TileSlots - 1; i >= 0; i--) freeTileSlots.Push(i);

            SetupBillboards();

            var args = new uint[35];
            for (int c = 0; c < 7; c++)
            {
                Mesh m = c == 5 ? quad : meshes[MeshOf(c)];
                if (m == null) continue;
                args[c * 5 + 0] = m.GetIndexCount(0);
                args[c * 5 + 2] = m.GetIndexStart(0);
                args[c * 5 + 3] = m.GetBaseVertex(0);
                blocks[c] = new MaterialPropertyBlock();
                if (c != 5) blocks[c].SetBuffer("_Plants", plantBuf); else blocks[c].SetBuffer("_Bills", billBuf);
                blocks[c].SetBuffer("_Visible", outBufs[c]);
            }
            argsBuf.SetData(args);

            for (int a = 0; a <= 90; a++)
            {
                float total = a * Mathf.Deg2Rad, kap = Mathf.Min(total * 0.55f, 50f * Mathf.Deg2Rad), rigid = total - kap;
                Vector2 tip = kap < 1e-4f ? new Vector2(1f, 0f) : new Vector2(Mathf.Sin(kap) / kap, (1f - Mathf.Cos(kap)) / kap);
                tipTable[a] = tip.x * Mathf.Cos(rigid) - tip.y * Mathf.Sin(rigid);
            }
            ready = true;
        }

        // Ausgabeliste -> Mesh: 0/1 Bueschel, 2 Bueschel vereinfacht, 3/4 Einzelhalme
        static int MeshOf(int cat) => cat <= 1 ? 0 : cat == 2 ? 3 : cat == 3 ? 1 : cat == 6 ? 4 : 2;

        static Texture BaseTexture(Material m)
        {
            foreach (var p in new[] { "baseColorTexture", "_BaseMap", "_MainTex" })
                if (m.HasProperty(p) && m.GetTexture(p) != null) return m.GetTexture(p);
            return Texture2D.whiteTexture;
        }

        void OnDestroy()
        {
            plantBuf?.Release(); billBuf?.Release(); argsBuf?.Release();
            foreach (var b in outBufs) b?.Release();
            StatPlants = StatBillboards = StatActive = StatChunks = StatTiles = StatTris = 0;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
            return null;
        }

        // ---------------- Fernbilder ----------------

        void SetupBillboards()
        {
            var sh = Shader.Find("PanzerSim/WeizenBillboard");
            if (sh == null) sh = Resources.Load<Shader>("Shaders/WeizenBillboard");
            var ta = Resources.Load<TextAsset>("WeizenFern");
            if (sh == null || ta == null) { Debug.LogWarning("Weizen: Fern-Bild oder Shader fehlt - kein Weizen in der Ferne."); return; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            tex.LoadImage(ta.bytes, false);
            tex.Apply(true);
            PreserveCoverage(tex, 0.45f);
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Trilinear; tex.anisoLevel = 4;
            billMat = new Material(sh);
            billMat.SetTexture("_BaseMap", tex);
            billMat.SetFloat("_FadeStart", billboardStart);
            billMat.SetColor("_Tint", new Color(0.95f, 0.93f, 0.88f));

            const float w = 0.85f, h = 1.275f;
            quad = new Mesh { name = "WeizenFern" };
            quad.vertices = new[] { new Vector3(-w, 0, 0), new Vector3(w, 0, 0), new Vector3(w, h, 0), new Vector3(-w, h, 0) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        }

        static void PreserveCoverage(Texture2D t, float cut)
        {
            float Cov(Color32[] px, float scale)
            {
                int n = 0; byte c = (byte)(cut * 255f);
                foreach (var p in px) if (Mathf.Min(255f, p.a * scale) > c) n++;
                return n / (float)px.Length;
            }
            float target = Cov(t.GetPixels32(0), 1f);
            for (int l = 1; l < t.mipmapCount; l++)
            {
                var px = t.GetPixels32(l);
                float lo = 0.5f, hi = 16f;
                for (int it = 0; it < 14; it++) { float mid = (lo + hi) / 2f; if (Cov(px, mid) < target) lo = mid; else hi = mid; }
                for (int i = 0; i < px.Length; i++) px[i].a = (byte)Mathf.Min(255f, px[i].a * hi);
                t.SetPixels32(px, l);
            }
            t.Apply(false);
        }

        void UploadTile(int tx, int tz, FarTile t)
        {
            float x0 = tx * Tile, z0 = tz * Tile;
            var rnd = new System.Random(tx * 48611 ^ tz * 96769 ^ 77);
            float R() => (float)rnd.NextDouble();
            int n = 0;
            for (float a = x0 + BillSpacing / 2f; a < x0 + Tile; a += BillSpacing)
                for (float b = z0 + BillSpacing / 2f; b < z0 + Tile && n < PerTile; b += BillSpacing)
                {
                    float x = a + (R() - 0.5f) * BillSpacing * 0.9f, z = b + (R() - 0.5f) * BillSpacing * 0.9f;
                    float r1 = R(), r2 = R(), r3 = R();
                    if (!grows(x, z)) continue;
                    float patch = Mathf.PerlinNoise(x * 0.045f + 300f, z * 0.045f + 700f);
                    if (patch < 0.3f && r1 < (0.3f - patch) * 2.5f) continue;
                    float sc = (0.78f + patch * 0.35f) * (0.92f + r2 * 0.16f);
                    tileUpload[n++] = new Vector4(x, groundY(x, z) - 0.05f, z, r3 < 0.5f ? -sc : sc);
                }
            for (int i = n; i < PerTile; i++) tileUpload[i] = Vector4.zero;
            billBuf.SetData(tileUpload, 0, t.slot * PerTile, PerTile);
        }

        // ---------------- Kacheln mit 3D-Pflanzen ----------------

        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        ChunkData GetChunk(int cx, int cz)
        {
            long key = Key(cx, cz);
            if (chunks.TryGetValue(key, out var c)) return c;
            c = new ChunkData { cx = cx, cz = cz };
            c.plants = Generate(cx, cz);
            for (int i = 0; i < c.plants.Length; i++) { c.plants[i].chunk = c; c.plants[i].index = i; }
            chunks[key] = c;
            return c;
        }

        Plant[] Generate(int cx, int cz)
        {
            float x0 = cx * Chunk, z0 = cz * Chunk;
            var rnd = new System.Random(cx * 92821 ^ cz * 68917 ^ 1944);
            float R() => (float)rnd.NextDouble();
            var list = new List<Plant>();
            float mx = x0 + Chunk / 2f, mz = z0 + Chunk / 2f;
            bool anyWheat = grows(mx, mz) || grows(x0, z0) || grows(x0 + Chunk, z0 + Chunk) || grows(x0, z0 + Chunk) || grows(x0 + Chunk, z0);
            bool anyGrass = grassGrows(mx, mz) || grassGrows(x0, z0) || grassGrows(x0 + Chunk, z0 + Chunk) || grassGrows(x0, z0 + Chunk) || grassGrows(x0 + Chunk, z0);
            if (!anyWheat && !anyGrass) return System.Array.Empty<Plant>();
            bool alongX = rowsAlongX(mx, mz);
            if (!anyWheat) goto Grass;

            // Saatreihen: quer RowSpacing, laengs PlantSpacing, mit Zufallsversatz
            for (float a = Mathf.Floor(x0 / RowSpacing) * RowSpacing; a < x0 + Chunk; a += RowSpacing)
                for (float b = Mathf.Floor(z0 / PlantSpacing) * PlantSpacing; b < z0 + Chunk; b += PlantSpacing)
                {
                    float across = a + (R() - 0.5f) * 0.18f, along = b + (R() - 0.5f) * 0.45f;
                    float x = alongX ? along : across, z = alongX ? across : along;
                    float r1 = R(), r2 = R(), r3 = R(), r4 = R();
                    if (x < x0 || x >= x0 + Chunk || z < z0 || z >= z0 + Chunk) continue;
                    if (!grows(x, z)) continue;
                    float t = Mathf.Repeat(alongX ? z : x, TramlineEvery);   // Fahrgassen
                    if (Mathf.Abs(t - TramlineEvery / 2f + TramlineGauge / 2f) < TramlineWidth / 2f ||
                        Mathf.Abs(t - TramlineEvery / 2f - TramlineGauge / 2f) < TramlineWidth / 2f) continue;
                    float patch = Mathf.PerlinNoise(x * 0.045f + 300f, z * 0.045f + 700f);
                    if (patch < 0.3f && r1 < (0.3f - patch) * 2.5f) continue;
                    list.Add(NewPlant(0, x, z, (0.78f + patch * 0.35f) * (0.92f + r2 * 0.16f), r3, r4));
                }
            int stalks = Mathf.RoundToInt(Chunk * Chunk * 0.7f);
            for (int i = 0; i < stalks; i++)
            {
                float x = x0 + R() * Chunk, z = z0 + R() * Chunk, r1 = R(), r2 = R(), r3 = R();
                byte kind = (byte)(1 + rnd.Next(2));
                if (!grows(x, z)) continue;
                float patch = Mathf.PerlinNoise(x * 0.045f + 300f, z * 0.045f + 700f);
                list.Add(NewPlant(kind, x, z, (0.8f + patch * 0.3f) * (0.9f + r1 * 0.2f), r2, r3));
            }
        Grass:
            // Gras auf den Wiesen: ungeordnet, Flecken mit hoeherem und niedrigerem Bewuchs
            if (anyGrass)
            {
                int n = Mathf.RoundToInt(Chunk * Chunk * 2.6f);
                for (int i = 0; i < n; i++)
                {
                    float x = x0 + R() * Chunk, z = z0 + R() * Chunk, r1 = R(), r2 = R(), r3 = R();
                    if (!grassGrows(x, z)) continue;
                    float patch = Mathf.PerlinNoise(x * 0.07f + 50f, z * 0.07f + 90f);
                    if (r1 < 0.35f - patch * 0.4f) continue;
                    list.Add(NewPlant(3, x, z, (0.6f + patch * 0.7f) * (0.85f + r2 * 0.3f), r3, R()));
                }
            }
            if (list.Count > PerChunk) list.RemoveRange(PerChunk, list.Count - PerChunk);
            return list.ToArray();
        }

        Plant NewPlant(byte kind, float x, float z, float scale, float r1, float r2)
        {
            return new Plant
            {
                kind = kind, scale = scale, yaw = r1 * 360f, lodRank = r2,
                pos = new Vector3(x, groundY(x, z) - 0.04f, z),
                rest = 4f + r2 * 14f,
            };
        }

        static GpuPlant ToGpu(Plant p)
        {
            float yaw = p.angle < 0.01f ? p.yaw : Mathf.LerpAngle(p.yaw, p.dirYaw, Mathf.SmoothStep(0f, 1f, p.angle / 12f));
            return new GpuPlant
            {
                pos = p.pos, scale = p.scale, yaw = yaw * Mathf.Deg2Rad, angle = p.angle * Mathf.Deg2Rad,
                lodRank = p.lodRank, kindState = p.kind * 8 + p.state,
            };
        }

        void UploadChunk(ChunkData c)
        {
            int n = c.plants.Length;
            for (int i = 0; i < n; i++) chunkUpload[i] = ToGpu(c.plants[i]);
            for (int i = n; i < PerChunk; i++) chunkUpload[i] = new GpuPlant { kindState = 4 };
            plantBuf.SetData(chunkUpload, 0, c.slot * PerChunk, PerChunk);
            c.dirty = false;
        }

        void ReleaseChunkSlot(ChunkData c)
        {
            if (c.slot < 0) return;
            for (int i = 0; i < PerChunk; i++) chunkUpload[i] = new GpuPlant { kindState = 4 };
            plantBuf.SetData(chunkUpload, 0, c.slot * PerChunk, PerChunk);
            freeChunkSlots.Push(c.slot);
            c.slot = -1;
        }

        // ---------------- Nachladen um die Kamera ----------------

        readonly List<(float d, int x, int z)> missing = new List<(float, int, int)>();

        void Stream(Vector3 cp, int frame)
        {
            // 3D-Kacheln: alles im Umkreis bekommt einen GPU-Platz (die nahen zuerst), Rest wird freigegeben
            missing.Clear();
            int r = Mathf.CeilToInt((farDist + 8f) / Chunk);
            int ccx = Mathf.FloorToInt(cp.x / Chunk), ccz = Mathf.FloorToInt(cp.z / Chunk);
            for (int cx = ccx - r; cx <= ccx + r; cx++)
                for (int cz = ccz - r; cz <= ccz + r; cz++)
                {
                    float dx = (cx + 0.5f) * Chunk - cp.x, dz = (cz + 0.5f) * Chunk - cp.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d > farDist + 8f) continue;
                    if (chunks.TryGetValue(Key(cx, cz), out var c)) { c.seen = frame; if (c.slot >= 0 || c.plants.Length == 0) continue; }
                    missing.Add((d, cx, cz));
                }
            missing.Sort((a, b) => a.d.CompareTo(b.d));
            int made = 0;
            foreach (var m in missing)
            {
                if (made >= 40 && m.d > nearDist) break;
                bool fresh = !chunks.ContainsKey(Key(m.x, m.z));
                var c = GetChunk(m.x, m.z);
                c.seen = frame;
                if (fresh) made++;
                if (c.plants.Length == 0) continue;
                if (freeChunkSlots.Count == 0) break;
                c.slot = freeChunkSlots.Pop();
                UploadChunk(c);
            }
            var drop = new List<long>();
            foreach (var kv in chunks)
            {
                var c = kv.Value;
                if (c.seen == frame) continue;
                ReleaseChunkSlot(c);
                if (!c.touched && frame - c.seen > 600) drop.Add(kv.Key);
            }
            foreach (var k in drop) chunks.Remove(k);

            // Fernbild-Kacheln
            if (billMat == null) return;
            missing.Clear();
            int rt = Mathf.CeilToInt((billboardDist + Tile) / Tile);
            int ctx = Mathf.FloorToInt(cp.x / Tile), ctz = Mathf.FloorToInt(cp.z / Tile);
            for (int tx = ctx - rt; tx <= ctx + rt; tx++)
                for (int tz = ctz - rt; tz <= ctz + rt; tz++)
                {
                    float dx = (tx + 0.5f) * Tile - cp.x, dz = (tz + 0.5f) * Tile - cp.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < billboardStart - Tile * 0.75f || d > billboardDist + Tile * 0.7f) continue;
                    if (tiles.TryGetValue(Key(tx, tz), out var t)) { t.seen = frame; continue; }
                    missing.Add((d, tx, tz));
                }
            missing.Sort((a, b) => a.d.CompareTo(b.d));
            int madeT = 0;
            foreach (var m in missing)
            {
                if (madeT >= 8 || freeTileSlots.Count == 0) break;
                // leere Kacheln (kein Feld) brauchen keinen Platz
                float x0 = m.x * Tile, z0 = m.z * Tile;
                bool any = false;
                for (int i = 0; i <= 4 && !any; i++) for (int j = 0; j <= 4 && !any; j++) any = grows(x0 + i * Tile / 4f, z0 + j * Tile / 4f);
                var t = new FarTile { seen = frame };
                if (any) { t.slot = freeTileSlots.Pop(); UploadTile(m.x, m.z, t); }
                tiles[Key(m.x, m.z)] = t;
                madeT++;
            }
            drop.Clear();
            foreach (var kv in tiles)
                if (kv.Value.seen != frame)
                {
                    if (kv.Value.slot >= 0)
                    {
                        System.Array.Clear(tileUpload, 0, PerTile);
                        billBuf.SetData(tileUpload, 0, kv.Value.slot * PerTile, PerTile);
                        freeTileSlots.Push(kv.Value.slot);
                    }
                    drop.Add(kv.Key);
                }
            foreach (var k in drop) tiles.Remove(k);
        }

        // ---------------- Panzer ----------------

        void Update()
        {
            if (!ready) return;
            var cam = Camera.main;
            float dt = Time.deltaTime;
            int frame = Time.frameCount;
            tankRefresh -= Time.unscaledDeltaTime;
            if (tankRefresh <= 0f)
            {
                tankRefresh = 1f;
                tanks.Clear();
                tanks.AddRange(FindObjectsByType<TankController>(FindObjectsSortMode.None));
            }
            if (cam != null && (frame % 10 == 0 || (cam.transform.position - lastStreamPos).sqrMagnitude > 64f))
            {
                lastStreamPos = cam.transform.position;
                Stream(lastStreamPos, frame);
            }
            if (dt > 0f)
            {
                foreach (var t in tanks) if (t != null && t.spec != null) TankContact(t);
                Animate(dt);
            }
            foreach (var kv in chunks) if (kv.Value.dirty && kv.Value.slot >= 0) UploadChunk(kv.Value);
            if (cam != null) CullAndDraw(cam);
        }

        static float AngleForHeight(float h, float clear)
        {
            float rel = clear / Mathf.Max(h, 0.05f);
            if (rel >= 1f) return 0f;
            for (int a = 1; a <= 90; a++) if (tipTable[a] <= rel) return a;
            return 90f;
        }

        void TankContact(TankController t)
        {
            var s = t.spec;
            Transform tr = t.transform;
            float halfL = s.hullLength / 2f;
            float halfTrack = (s.hullWidth + s.trackWidth) / 2f - s.trackInset;
            float outer = halfTrack + s.trackWidth / 2f + 0.05f, inner = halfTrack - s.trackWidth / 2f;
            const float noseZone = 0.7f;
            float radius = halfL + noseZone + 1f;

            float v = t.ForwardSpeed;
            float sign = v < -0.2f ? -1f : 1f;
            Vector3 dir = tr.forward * sign;
            float dirYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

            Vector3 c = tr.position;
            int x0 = Mathf.FloorToInt((c.x - radius) / Chunk), x1 = Mathf.FloorToInt((c.x + radius) / Chunk);
            int z0 = Mathf.FloorToInt((c.z - radius) / Chunk), z1 = Mathf.FloorToInt((c.z + radius) / Chunk);
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                {
                    var ch = GetChunk(cx, cz);
                    ch.seen = Mathf.Max(ch.seen, Time.frameCount - 1);
                    foreach (var p in ch.plants)
                    {
                        if (p.state >= 2) continue;
                        Vector3 l = tr.InverseTransformPoint(p.pos);
                        if (l.y < -0.6f || l.y > 0.8f) continue;
                        float ax = Mathf.Abs(l.x);
                        if (ax > outer) continue;
                        float along = l.z * sign, ahead = along - halfL;
                        if (ahead > noseZone || along < -halfL - 0.2f) continue;
                        float want;
                        if (ahead > 0f)
                        {
                            want = AngleForHeight(PlantHeight(p), s.clearance + ahead * 1.6f);
                            if (want <= 0f) continue;
                        }
                        else if (ax >= inner && Mathf.Abs(l.z) < halfL * 0.92f)
                        {
                            p.state = 2; p.target = 86f + p.lodRank * 4f; p.timer = flatLifetime * (0.8f + p.lodRank * 0.4f);
                            p.dirYaw = dirYaw; Touch(p);
                            continue;
                        }
                        else want = AngleForHeight(PlantHeight(p), s.clearance);
                        if (p.state == 0 || want > p.target || Mathf.Abs(Mathf.DeltaAngle(p.dirYaw, dirYaw)) > 120f)
                        {
                            if (p.state == 0 || p.angle < 5f) p.dirYaw = dirYaw;
                            p.target = Mathf.Max(want, p.state == 1 ? p.target : 0f);
                        }
                        p.state = 1; p.timer = 0.15f;
                        Touch(p);
                    }
                }
        }

        static float PlantHeight(Plant p) => p.kind == 3 ? p.scale * 0.55f : p.scale;

        void Touch(Plant p)
        {
            p.chunk.touched = true; p.chunk.dirty = true;
            if (!p.active) { p.active = true; active.Add(p); }
        }

        void Animate(float dt)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var p = active[i];
                bool done = false;
                switch (p.state)
                {
                    case 1:
                        p.timer -= dt;
                        if (p.timer > 0f) { p.angle = Mathf.MoveTowards(p.angle, p.target, 400f * dt); p.vel = 0f; }
                        else
                        {
                            float acc = (p.rest - p.angle) * 60f - p.vel * 7f;
                            p.vel += acc * dt; p.angle = Mathf.Clamp(p.angle + p.vel * dt, 0f, 90f);
                            p.target = 0f;
                            if (Mathf.Abs(p.angle - p.rest) < 0.3f && Mathf.Abs(p.vel) < 0.5f) { p.angle = p.rest; done = true; p.state = 0; }
                        }
                        break;
                    case 2:
                        p.angle = Mathf.MoveTowards(p.angle, p.target, 500f * dt);
                        p.timer -= dt;
                        if (p.timer <= 0f) { p.state = 3; p.timer = sinkTime; }
                        break;
                    case 3:
                        p.timer -= dt;
                        p.pos.y -= 0.12f * dt;
                        if (p.timer <= 0f) { p.state = 4; done = true; }
                        break;
                    default: done = true; break;
                }
                p.chunk.dirty = true;
                if (done) { p.active = false; active.RemoveAt(i); }
            }
        }

        // ---------------- GPU: auswaehlen und zeichnen ----------------

        void CullAndDraw(Camera cam)
        {
            // Werte aus dem Einstellungsmenue
            farDist = GameSettings.WheatDistance; nearDist = Mathf.Min(50f, farDist * 0.5f);
            billboardStart = farDist - 13f; billboardDist = GameSettings.ViewDistance;
            billboardFull = Mathf.Min(260f, billboardDist * 0.5f); shadowDist = GameSettings.WheatShadows;
            stalkDist = GameSettings.WheatDensity < 0.6f ? 0f : 16f;
            if (billMat != null) billMat.SetFloat("_FadeStart", billboardStart);
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            for (int i = 0; i < 6; i++) planeVecs[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
            Vector3 cp = cam.transform.position;
            cull.SetVectorArray("_Planes", planeVecs);
            cull.SetVector("_CamPos", cp);
            cull.SetFloat("_NearDist", nearDist); cull.SetFloat("_FarDist", farDist); cull.SetFloat("_LodDist", lodDist);
            cull.SetFloat("_ShadowDist", shadowDist); cull.SetFloat("_StalkDist", stalkDist);
            cull.SetFloat("_BillStart", billboardStart); cull.SetFloat("_Density", GameSettings.WheatDensity); cull.SetFloat("_BillDist", billboardDist); cull.SetFloat("_BillFull", billboardFull);

            for (int i = 0; i < 6; i++) outBufs[i].SetCounterValue(0);
            cull.SetBuffer(kPlants, "_Plants", plantBuf);
            cull.SetBuffer(kPlants, "_Out0", outBufs[0]); cull.SetBuffer(kPlants, "_Out1", outBufs[1]);
            cull.SetBuffer(kPlants, "_Out2", outBufs[2]); cull.SetBuffer(kPlants, "_Out3", outBufs[3]);
            cull.SetBuffer(kPlants, "_Out4", outBufs[4]); cull.SetBuffer(kPlants, "_OutG", outBufs[6]);
            cull.SetFloat("_GrassDist", meshes[4] != null ? grassDist * Mathf.Lerp(0.6f, 1f, GameSettings.WheatDensity) : 0f);
            cull.SetInt("_Count", ChunkSlots * PerChunk);
            cull.Dispatch(kPlants, ChunkSlots * PerChunk / 64, 1, 1);
            if (billMat != null)
            {
                cull.SetBuffer(kBills, "_Bills", billBuf);
                cull.SetBuffer(kBills, "_OutB", outBufs[5]);
                cull.SetInt("_Count", TileSlots * PerTile);
                cull.Dispatch(kBills, TileSlots * PerTile / 64, 1, 1);
            }
            for (int c = 0; c < 7; c++) ComputeBuffer.CopyCount(outBufs[c], argsBuf, (c * 5 + 1) * 4);

            var bounds = new Bounds(cp, Vector3.one * (billboardDist * 2f + 50f));
            for (int c = 0; c < 5; c++)
            {
                int k = MeshOf(c);
                bool shadow = c == 0 || c == 3 || c == 4;
                Graphics.DrawMeshInstancedIndirect(meshes[k], 0, mats[k], bounds, argsBuf, c * 5 * 4, blocks[c],
                    shadow ? ShadowCastingMode.On : ShadowCastingMode.Off, true, 0);
            }
            if (billMat != null)
                Graphics.DrawMeshInstancedIndirect(quad, 0, billMat, bounds, argsBuf, 25 * 4, blocks[5], ShadowCastingMode.Off, false, 0);
            if (meshes[4] != null)
                Graphics.DrawMeshInstancedIndirect(meshes[4], 0, mats[4], bounds, argsBuf, 30 * 4, blocks[6], ShadowCastingMode.Off, true, 0);

            // Zahlen fuer F3 ab und zu von der GPU holen (asynchron, bremst nicht)
            StatActive = active.Count; StatChunks = chunks.Count; StatTiles = tiles.Count;
            statTimer -= Time.unscaledDeltaTime;
            if (DevMetrics.Visible && statTimer <= 0f && !statPending)
            {
                statTimer = 0.5f; statPending = true;
                AsyncGPUReadback.Request(argsBuf, req =>
                {
                    statPending = false;
                    if (req.hasError || this == null) return;
                    var a = req.GetData<uint>();
                    int plants = 0, tris = 0;
                    for (int c = 0; c < 7; c++) { if (c == 5) continue; plants += (int)a[c * 5 + 1]; tris += (int)a[c * 5 + 1] * TrisPerKind[c]; }
                    StatPlants = plants; StatBillboards = (int)a[26]; StatTris = tris + StatBillboards * 2;
                });
            }
        }
    }
}
