using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Echte Gegend aus Daten (Tools/RealMap/fetch_map.py): Hoehen aus dem amtlichen Gelaendemodell (DGM NRW),
    /// Strassen, Gebaeude, Wald, Felder, Gewaesser und Ortsnamen aus OpenStreetMap. Umland als Hintergrund.
    /// Resources/Karten/&lt;Ordner&gt;/hoehe.bytes, umland.bytes, osm.txt. Koordinaten: Meter, Mitte 0/0, x Ost, z Nord.
    /// </summary>
    public class RealMap : GameMap
    {
        readonly string folder, title, desc;
        readonly (string name, float x, float z)[] extraSpawns;

        public RealMap(string folder, string title, string desc, params (string, float, float)[] extraSpawns)
        { this.folder = folder; this.title = title; this.desc = desc; this.extraSpawns = extraSpawns; }

        public override string Name => title;
        public override string Description => desc;
        public override bool Fog => true;
        public override float FogStart => 1200f;
        public override float FogEnd => 14000f;
        public override bool SpawnSelect => true;
        public override float Extent => half;
        public override Vector2 PlayerXZ => spawns.Count > 0 ? spawns[0].xz : Vector2.zero;
        public override float PlayerYaw => spawns.Count > 0 ? spawns[0].yaw : 0f;
        public override List<SpawnPoint> SpawnPoints => spawns;
        public override Texture2D Overview => overview;

        // ---- Daten ----
        int G; float cell, size = 5000f, half = 2500f, baseH;
        float[] H;                   // Hoehe ueber dem tiefsten Punkt (m)
        const int S = 2048;          // Bodenarten-Raster (2,44 m)
        float scell;
        byte[] surf;
        const byte Wiese = 0, Acker = 1, Erde = 2, Asphalt = 3, Wald = 4, Wasser = 5, Garten = 6, Gebuesch = 7, Fels = 8, Gleis = 9;
        readonly List<SpawnPoint> spawns = new List<SpawnPoint>();
        readonly List<(string name, string type, Vector2 p)> places = new List<(string, string, Vector2)>();
        readonly List<Vector4> buildingBoxes = new List<Vector4>();   // Mitte x/z, Radius, Hoehe
        Texture2D overview;

        struct Road { public char kind; public float width; public bool bridge; public Vector2[] p; }
        struct Building { public float h; public char cls; public Vector2[] p; }
        struct Area { public string cls; public Vector2[] p; }
        readonly List<Road> roads = new List<Road>();
        readonly List<Building> buildings = new List<Building>();
        readonly List<Area> areas = new List<Area>();

        // ================= Abfragen =================
        public override float GroundY(float x, float z)
        {
            if (H == null) return base.GroundY(x, z);
            float fx = Mathf.Clamp((x + half) / cell, 0f, G - 1.001f), fz = Mathf.Clamp((z + half) / cell, 0f, G - 1.001f);
            int ix = (int)fx, iz = (int)fz; float tx = fx - ix, tz = fz - iz;
            float a = Mathf.Lerp(H[iz * G + ix], H[iz * G + ix + 1], tx), b = Mathf.Lerp(H[(iz + 1) * G + ix], H[(iz + 1) * G + ix + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }
        byte SurfAt(float x, float z)
        {
            if (surf == null) return Wiese;
            int ix = Mathf.Clamp((int)((x + half) / scell), 0, S - 1), iz = Mathf.Clamp((int)((z + half) / scell), 0, S - 1);
            return surf[iz * S + ix];
        }
        public override int SurfaceAt(float x, float z)
        {
            switch (SurfAt(x, z)) { case Acker: return GroundTracks.Acker; case Erde: case Gleis: return GroundTracks.Weg; case Asphalt: case Fels: return GroundTracks.Hart; default: return GroundTracks.Wiese; }
        }
        public override bool CanSpawn(float x, float z)
        {
            if (Mathf.Abs(x) > half - 40f || Mathf.Abs(z) > half - 40f) return false;
            byte s = SurfAt(x, z);
            if (s == Wald || s == Wasser || s == Fels || s == Gebuesch) return false;
            foreach (var b in buildingBoxes) if ((x - b.x) * (x - b.x) + (z - b.y) * (z - b.y) < (b.z + 7f) * (b.z + 7f)) return false;
            float sl = Mathf.Abs(GroundY(x + 4f, z) - GroundY(x - 4f, z)) + Mathf.Abs(GroundY(x, z + 4f) - GroundY(x, z - 4f));
            return sl < 4f;
        }

        // ================= Bauen =================
        public override void Build(Transform root)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            spawns.Clear(); places.Clear(); buildingBoxes.Clear(); roads.Clear(); buildings.Clear(); areas.Clear();
            LoadHeights();
            LoadOsm();
            Rasterize();
            BuildTerrain(root);
            BuildBackdrop(root);
            BuildBuildings(root);
            BuildWater(root);
            BuildEdge(root);
            var trees = PlaceTrees();
            Forest.Create(root, trees);
            MakeSpawns();
            if (!NetSync.Headless) { BuildOverview(); WheatField.Create(root, (x, z) => SurfAt(x, z) == Acker, GroundY, (x, z) => (((int)((x + 5000f) / 150f) * 7 + (int)((z + 5000f) / 150f)) & 1) == 0, GrassOk); }
            Debug.Log($"[Echte Karte] {title}: gebaut in {sw.ElapsedMilliseconds} ms, {buildings.Count} Gebaeude, {roads.Count} Wege, {trees.Count} Baeume, Hoehen {baseH:0}..{baseH + MaxRange:0} m");
        }

        bool GrassOk(float x, float z)
        {
            byte s = SurfAt(x, z);
            return s == Wiese || s == Garten;
        }

        float MaxRange;
        void LoadHeights()
        {
            var ta = Resources.Load<TextAsset>($"Karten/{folder}/hoehe");
            var d = ta.bytes;
            G = System.BitConverter.ToInt32(d, 0); cell = System.BitConverter.ToSingle(d, 4); baseH = System.BitConverter.ToSingle(d, 8);
            float sc = System.BitConverter.ToSingle(d, 12); size = System.BitConverter.ToSingle(d, 16); half = size / 2f;
            H = new float[G * G]; MaxRange = 0f;
            for (int i = 0; i < G * G; i++) { H[i] = System.BitConverter.ToUInt16(d, 20 + i * 2) * sc; if (H[i] > MaxRange) MaxRange = H[i]; }
            scell = size / S;
        }

        static Vector2[] ParsePts(string s)
        {
            var parts = s.Split(';');
            var p = new Vector2[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                int c = parts[i].IndexOf(',');
                p[i] = new Vector2(float.Parse(parts[i].Substring(0, c), CultureInfo.InvariantCulture), float.Parse(parts[i].Substring(c + 1), CultureInfo.InvariantCulture));
            }
            return p;
        }

        void LoadOsm()
        {
            var ta = Resources.Load<TextAsset>($"Karten/{folder}/osm");
            foreach (var line in ta.text.Split('\n'))
            {
                if (line.Length < 3 || line[0] == '#') continue;
                var f = line.TrimEnd('\r').Split('|');
                try
                {
                    switch (f[0])
                    {
                        case "R": roads.Add(new Road { kind = f[1][0], width = float.Parse(f[2], CultureInfo.InvariantCulture), bridge = f[3] == "1", p = ParsePts(f[4]) }); break;
                        case "B": buildings.Add(new Building { h = float.Parse(f[1], CultureInfo.InvariantCulture), cls = f[2][0], p = ParsePts(f[3]) }); break;
                        case "P": areas.Add(new Area { cls = f[1], p = ParsePts(f[2]) }); break;
                        case "N": places.Add((f[1], f[2], new Vector2(float.Parse(f[3], CultureInfo.InvariantCulture), float.Parse(f[4], CultureInfo.InvariantCulture)))); break;
                    }
                }
                catch { }
            }
        }

        // ---------------- Bodenarten ----------------
        static readonly string[] AreaOrder = { "wohnen", "gewerbe", "acker", "wiese", "obst", "garten", "park", "platz", "hof", "erde", "gebuesch", "wald", "fels", "parkplatz", "gleis", "wasser" };
        static byte AreaSurf(string c)
        {
            switch (c)
            {
                case "acker": return Acker;
                case "wald": return Wald;
                case "wasser": return Wasser;
                case "gebuesch": return Gebuesch;
                case "fels": return Fels;
                case "parkplatz": case "gewerbe": return Asphalt;
                case "hof": case "erde": case "platz": return Erde;
                case "gleis": return Gleis;
                case "wohnen": case "garten": case "obst": case "park": return Garten;
                default: return Wiese;
            }
        }

        void Rasterize()
        {
            surf = new byte[S * S];
            foreach (var cls in AreaOrder)
                foreach (var a in areas)
                    if (a.cls == cls) FillPolygon(a.p, AreaSurf(cls));
            // Wege: erst Feldwege, dann Asphalt, Gleise, Gewaesser zuletzt
            foreach (char k in new[] { 'd', 'a', 'r', 'w' })
                foreach (var r in roads)
                    if (r.kind == k) StampLine(r.p, r.width, k == 'a' ? Asphalt : k == 'd' ? Erde : k == 'r' ? Gleis : Wasser);
        }

        void FillPolygon(Vector2[] p, byte v)
        {
            if (p.Length < 3) return;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var q in p) { minZ = Mathf.Min(minZ, q.y); maxZ = Mathf.Max(maxZ, q.y); }
            int z0 = Mathf.Clamp((int)((minZ + half) / scell), 0, S - 1), z1 = Mathf.Clamp((int)((maxZ + half) / scell), 0, S - 1);
            var xs = new List<float>();
            for (int iz = z0; iz <= z1; iz++)
            {
                float wz = (iz + 0.5f) * scell - half;
                xs.Clear();
                for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                {
                    Vector2 a = p[i], b = p[j];
                    if ((a.y > wz) != (b.y > wz)) xs.Add(a.x + (wz - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int x0 = Mathf.Clamp(Mathf.CeilToInt((xs[k] + half) / scell - 0.5f), 0, S - 1), x1 = Mathf.Clamp(Mathf.FloorToInt((xs[k + 1] + half) / scell - 0.5f), 0, S - 1);
                    for (int ix = x0; ix <= x1; ix++) surf[iz * S + ix] = v;
                }
            }
        }

        void StampLine(Vector2[] p, float width, byte v)
        {
            float r = Mathf.Max(width * 0.5f, scell * 0.55f);
            for (int s = 0; s + 1 < p.Length; s++)
            {
                Vector2 a = p[s], b = p[s + 1];
                int x0 = Mathf.Clamp((int)((Mathf.Min(a.x, b.x) - r + half) / scell), 0, S - 1), x1 = Mathf.Clamp((int)((Mathf.Max(a.x, b.x) + r + half) / scell), 0, S - 1);
                int z0 = Mathf.Clamp((int)((Mathf.Min(a.y, b.y) - r + half) / scell), 0, S - 1), z1 = Mathf.Clamp((int)((Mathf.Max(a.y, b.y) + r + half) / scell), 0, S - 1);
                Vector2 ab = b - a; float len2 = Mathf.Max(ab.sqrMagnitude, 1e-4f);
                for (int iz = z0; iz <= z1; iz++)
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        Vector2 c = new Vector2((ix + 0.5f) * scell - half, (iz + 0.5f) * scell - half);
                        float t = Mathf.Clamp01(Vector2.Dot(c - a, ab) / len2);
                        if ((a + ab * t - c).sqrMagnitude <= r * r) surf[iz * S + ix] = v;
                    }
            }
        }

        // ---------------- Gelaende ----------------
        static TerrainLayer[] layers; static Material terrainMat;
        void EnsureLayers()
        {
            if (layers != null && layers[0] != null) return;
            bool hd = Resources.Load<Texture2D>("Boden/Wiese_Farbe") != null;
            var asphalt = PlainsMap.Layer(PlainsMap.NoiseTex(new Color(0.30f, 0.30f, 0.31f), new Color(0.22f, 0.22f, 0.23f), 23), 4f);
            layers = hd ? new[]
            {
                PlainsMap.HdLayer("Wiese", 3.2f, new Color(1.0f, 1.0f, 0.76f)),
                PlainsMap.HdLayer("Acker", 2.6f, new Color(1.08f, 0.98f, 0.78f)),
                PlainsMap.HdLayer("Weg", 3.0f, new Color(0.95f, 0.90f, 0.82f)),
                asphalt,
            } : new[]
            {
                PlainsMap.Layer(PlainsMap.NoiseTex(new Color(0.33f, 0.45f, 0.18f), new Color(0.24f, 0.36f, 0.12f), 7), 10f),
                PlainsMap.Layer(PlainsMap.NoiseTex(new Color(0.78f, 0.66f, 0.33f), new Color(0.62f, 0.50f, 0.22f), 11), 6f),
                PlainsMap.Layer(PlainsMap.NoiseTex(new Color(0.45f, 0.36f, 0.25f), new Color(0.35f, 0.27f, 0.18f), 13), 5f),
                asphalt,
            };
            var sh = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (sh != null) { terrainMat = new Material(sh); if (hd) PlainsMap.SetTerrainDetail(terrainMat, GameSettings.Effects >= 1); }
        }

        void Weights(byte s, out float w0, out float w1, out float w2, out float w3)
        {
            w0 = w1 = w2 = w3 = 0f;
            switch (s)
            {
                case Acker: w1 = 1f; break;
                case Erde: case Gleis: w2 = 1f; break;
                case Asphalt: w3 = 1f; break;
                case Wald: w0 = 0.35f; w2 = 0.65f; break;          // Waldboden
                case Wasser: w2 = 0.6f; w3 = 0.4f; break;          // Ufer/Grund (Wasserflaeche liegt darueber)
                case Fels: w3 = 0.7f; w2 = 0.3f; break;
                case Gebuesch: w0 = 0.7f; w2 = 0.3f; break;
                case Garten: w0 = 0.85f; w2 = 0.15f; break;
                default: w0 = 1f; break;
            }
        }

        void BuildTerrain(Transform root)
        {
            bool gfx = !NetSync.Headless;
            if (gfx) EnsureLayers();
            // Unity erlaubt nur 2^n+1 Hoehenpunkte je Kachel: 1281 = 5 x 256 + 1 -> 5 x 5 Kacheln mit 257 Punkten (1 km)
            int tiles = 1; while ((G - 1) / tiles > 512 || !Mathf.IsPowerOfTwo((G - 1) / tiles)) tiles++;
            int tres = (G - 1) / tiles + 1;
            float tsize = size / tiles;
            const int ares = 512;
            float maxH = MaxRange + 10f;
            var parent = new GameObject("Gelaende").transform; parent.SetParent(root, false);
            for (int tz = 0; tz < tiles; tz++)
                for (int tx = 0; tx < tiles; tx++)
                {
                    var td = new TerrainData { heightmapResolution = tres };
                    td.size = new Vector3(tsize, maxH, tsize);
                    var h = new float[tres, tres];
                    for (int iz = 0; iz < tres; iz++)
                        for (int ix = 0; ix < tres; ix++)
                            h[iz, ix] = H[(tz * (tres - 1) + iz) * G + tx * (tres - 1) + ix] / maxH;
                    td.SetHeights(0, 0, h);
                    float x0 = -half + tx * tsize, z0 = -half + tz * tsize;
                    if (gfx)
                    {
                        td.terrainLayers = layers;
                        td.alphamapResolution = ares;
                        var a = new float[ares, ares, 4];
                        Parallel.For(0, ares, iz =>
                        {
                            for (int ix = 0; ix < ares; ix++)
                            {
                                float wx = x0 + ix * tsize / (ares - 1), wz = z0 + iz * tsize / (ares - 1);
                                Weights(SurfAt(wx, wz), out a[iz, ix, 0], out a[iz, ix, 1], out a[iz, ix, 2], out a[iz, ix, 3]);
                            }
                        });
                        td.SetAlphamaps(0, 0, a);
                    }
                    var go = Terrain.CreateTerrainGameObject(td);
                    go.name = $"Gelaende {tx},{tz}";
                    go.transform.SetParent(parent, false);
                    go.transform.position = new Vector3(x0, 0f, z0);
                    var t = go.GetComponent<Terrain>();
                    t.groupingID = 9; t.allowAutoConnect = true; t.heightmapPixelError = 4f; t.basemapDistance = 1500f;
                    if (terrainMat != null) t.materialTemplate = terrainMat;
                }
            Terrain.SetConnectivityDirty();
            Physics.SyncTransforms();
        }

        /// <summary>Umland (15 x 15 km, grob, ohne Kollision): echte Hoehenzuege am Horizont. Unter der Karte ausgehoehlt.</summary>
        void BuildBackdrop(Transform root)
        {
            if (NetSync.Headless) return;
            var ta = Resources.Load<TextAsset>($"Karten/{folder}/umland");
            if (ta == null) return;
            var d = ta.bytes;
            int R = System.BitConverter.ToInt32(d, 0); float uc = System.BitConverter.ToSingle(d, 4), sc = System.BitConverter.ToSingle(d, 12), us = System.BitConverter.ToSingle(d, 16);
            float uh = us / 2f;
            var hs = new float[R * R]; float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < R * R; i++) { hs[i] = System.BitConverter.ToInt16(d, 20 + i * 2) * sc; lo = Mathf.Min(lo, hs[i]); hi = Mathf.Max(hi, hs[i]); }
            float off = lo - 40f, range = hi - off + 10f;
            var td = new TerrainData { heightmapResolution = R };
            td.size = new Vector3(us, range, us);
            var h = new float[R, R];
            for (int iz = 0; iz < R; iz++)
                for (int ix = 0; ix < R; ix++)
                {
                    float wx = ix * uc - uh, wz = iz * uc - uh;
                    bool under = Mathf.Abs(wx) < half - uc * 0.6f && Mathf.Abs(wz) < half - uc * 0.6f;   // unter der eigentlichen Karte: tief absenken
                    h[iz, ix] = under ? 0f : (hs[iz * R + ix] - off - 1.0f) / range;
                }
            td.SetHeights(0, 0, h);
            // Farbe: Wald auf Hoehen und Haengen, sonst Felder/Wiesen (aus der Ferne reicht das)
            td.terrainLayers = new[]
            {
                PlainsMap.Layer(PlainsMap.NoiseTex(new Color(0.36f, 0.46f, 0.20f), new Color(0.50f, 0.52f, 0.26f), 31), 40f),
                PlainsMap.Layer(PlainsMap.NoiseTex(new Color(0.12f, 0.22f, 0.10f), new Color(0.16f, 0.26f, 0.12f), 37), 30f),
            };
            const int ar = 512;
            td.alphamapResolution = ar;
            var a = new float[ar, ar, 2];
            var n = new Noise2D(5);
            for (int iz = 0; iz < ar; iz++)
                for (int ix = 0; ix < ar; ix++)
                {
                    int gx = Mathf.Clamp(ix * (R - 1) / (ar - 1), 1, R - 2), gz = Mathf.Clamp(iz * (R - 1) / (ar - 1), 1, R - 2);
                    float slope = (Mathf.Abs(hs[gz * R + gx + 1] - hs[gz * R + gx - 1]) + Mathf.Abs(hs[(gz + 1) * R + gx] - hs[(gz - 1) * R + gx])) / (2f * uc);
                    float f = Mathf.Clamp01((hs[gz * R + gx] - 90f) / 120f) * 0.6f + slope * 2.5f + (n.Get(ix * 0.05f, iz * 0.05f) - 0.5f) * 0.6f;
                    float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.55f, f));
                    a[iz, ix, 0] = 1f - w; a[iz, ix, 1] = w;
                }
            td.SetAlphamaps(0, 0, a);
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Umland";
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(-uh, off, -uh);
            var col = go.GetComponent<TerrainCollider>(); if (col != null) Object.Destroy(col);
            var t = go.GetComponent<Terrain>();
            t.heightmapPixelError = 12f; t.basemapDistance = 0f; t.castShadows = false;
            if (terrainMat != null) t.materialTemplate = new Material(terrainMat);
        }

        // ---------------- Gebaeude ----------------
        static readonly Color[] WallH = { new Color(0.90f, 0.87f, 0.80f), new Color(0.94f, 0.92f, 0.86f), new Color(0.82f, 0.74f, 0.60f), new Color(0.66f, 0.36f, 0.27f), new Color(0.86f, 0.80f, 0.66f) };
        static readonly Color[] Roofs = { new Color(0.50f, 0.20f, 0.14f), new Color(0.40f, 0.17f, 0.13f), new Color(0.22f, 0.22f, 0.24f), new Color(0.32f, 0.26f, 0.24f) };

        void BuildBuildings(Transform root)
        {
            var parent = new GameObject("Gebaeude").transform; parent.SetParent(root, false);
            // Material-Gruppen: 0-4 Waende Wohnhaus, 5 Gewerbe-Wand, 6 Kirche, 7 Holz/Scheune, 8-11 Daecher, 12 Flachdach
            var mats = new List<Material>();
            foreach (var c in WallH) mats.Add(GameMap.Mat(c));
            mats.Add(GameMap.Mat(new Color(0.72f, 0.72f, 0.70f))); mats.Add(GameMap.Mat(new Color(0.80f, 0.76f, 0.68f))); mats.Add(GameMap.Mat(new Color(0.44f, 0.31f, 0.21f)));
            foreach (var c in Roofs) mats.Add(GameMap.Mat(c));
            mats.Add(GameMap.Mat(new Color(0.38f, 0.38f, 0.38f)));
            const float Chunk = 500f;
            var chunks = new Dictionary<int, List<List<int>>[]>();
            var verts = new Dictionary<int, List<Vector3>>();
            foreach (var b in buildings)
            {
                if (b.p.Length < 3) continue;
                Vector2 c = Vector2.zero; foreach (var q in b.p) c += q; c /= b.p.Length;
                if (Mathf.Abs(c.x) > half - 2f || Mathf.Abs(c.y) > half - 2f) continue;
                int key = Mathf.FloorToInt((c.x + half) / Chunk) * 100 + Mathf.FloorToInt((c.y + half) / Chunk);
                if (!chunks.TryGetValue(key, out var sub)) { sub = new List<List<int>>[1]; sub[0] = new List<List<int>>(); for (int m = 0; m < mats.Count; m++) sub[0].Add(new List<int>()); chunks[key] = sub; verts[key] = new List<Vector3>(); }
                float rad = 0f; foreach (var q in b.p) rad = Mathf.Max(rad, Vector2.Distance(q, c));
                buildingBoxes.Add(new Vector4(c.x, c.y, rad, b.h));
                AddBuilding(b, verts[key], sub[0]);
            }
            foreach (var kv in chunks)
            {
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, name = "Gebaeude" };
                m.SetVertices(verts[kv.Key]);
                var used = new List<Material>(); int si = 0;
                m.subMeshCount = 0;
                var lists = new List<List<int>>();
                for (int k = 0; k < mats.Count; k++) if (kv.Value[0][k].Count > 0) { lists.Add(kv.Value[0][k]); used.Add(mats[k]); }
                m.subMeshCount = lists.Count;
                foreach (var l in lists) m.SetTriangles(l, si++);
                m.RecalculateNormals(); m.RecalculateBounds();
                var go = new GameObject("Haeuser");
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                go.AddComponent<MeshRenderer>().sharedMaterials = used.ToArray();
                go.AddComponent<MeshCollider>().sharedMesh = m;
            }
        }

        void AddBuilding(Building b, List<Vector3> v, List<List<int>> sub)
        {
            var p = b.p;
            Vector2 cen = Vector2.zero; foreach (var q in p) cen += q; cen /= p.Length;
            float gmin = float.MaxValue, gmax = float.MinValue;
            foreach (var q in p) { float g = GroundY(q.x, q.y); gmin = Mathf.Min(gmin, g); gmax = Mathf.Max(gmax, g); }
            int hash = Mathf.Abs((int)(p[0].x * 7.13f + p[0].y * 3.71f));
            int wall = b.cls == 'i' ? 5 : b.cls == 'k' ? 6 : b.cls == 's' || b.cls == 'n' ? 7 : hash % 5;
            int roof = 8 + (b.cls == 'i' ? 2 : (hash / 5) % 4);
            float bottom = gmin - 1f, eave = gmax + b.h;
            // Satteldach fuer rechteckige Wohnhaeuser/Scheunen: First entlang der langen Seite
            bool gable = (b.cls == 'h' || b.cls == 's' || b.cls == 'k') && p.Length == 4 && b.h < 25f;
            float roofH = 0f;
            Vector2 c0 = p[0], c1 = p[1], c2 = p[2], c3 = p.Length > 3 ? p[3] : p[2];
            if (gable)
            {
                float l01 = Vector2.Distance(c0, c1), l12 = Vector2.Distance(c1, c2);
                roofH = Mathf.Clamp(Mathf.Min(l01, l12) * 0.42f, 1.5f, 6f);
                eave = gmax + Mathf.Max(b.h - roofH * 0.6f, 2.6f);
            }
            Vector3 C3 = new Vector3(cen.x, 0f, cen.y);
            for (int i = 0; i < p.Length; i++)
            {
                Vector2 a = p[i], c = p[(i + 1) % p.Length];
                Vector3 outward = new Vector3((a.x + c.x) / 2f - cen.x, 0f, (a.y + c.y) / 2f - cen.y);
                Quad(v, sub[wall], new Vector3(a.x, bottom, a.y), new Vector3(a.x, eave, a.y), new Vector3(c.x, eave, c.y), new Vector3(c.x, bottom, c.y), outward);
            }
            if (gable)
            {
                // First zwischen den Mitten der kurzen Seiten
                bool long01 = Vector2.Distance(c0, c1) >= Vector2.Distance(c1, c2);
                Vector2 m1 = long01 ? (c1 + c2) / 2f : (c0 + c1) / 2f, m2 = long01 ? (c3 + c0) / 2f : (c2 + c3) / 2f;
                float top = eave + roofH;
                Vector3 r1 = new Vector3(m1.x, top, m1.y), r2 = new Vector3(m2.x, top, m2.y);
                Vector3 P0 = new Vector3(c0.x, eave, c0.y), P1 = new Vector3(c1.x, eave, c1.y), P2 = new Vector3(c2.x, eave, c2.y), P3 = new Vector3(c3.x, eave, c3.y);
                if (long01)
                {
                    Quad(v, sub[roof], P0, r2, r1, P1, (P0 + P1) / 2f - C3 + Vector3.up);   // Dachflaechen
                    Quad(v, sub[roof], P2, r1, r2, P3, (P2 + P3) / 2f - C3 + Vector3.up);
                    Tri(v, sub[wall], P1, r1, P2, r1 - C3); Tri(v, sub[wall], P3, r2, P0, r2 - C3);   // Giebel
                }
                else
                {
                    Quad(v, sub[roof], P1, r1, r2, P2, (P1 + P2) / 2f - C3 + Vector3.up);
                    Quad(v, sub[roof], P3, r2, r1, P0, (P3 + P0) / 2f - C3 + Vector3.up);
                    Tri(v, sub[wall], P0, r1, P1, r1 - C3); Tri(v, sub[wall], P2, r2, P3, r2 - C3);
                }
            }
            else
            {
                var tris = Triangulate(p);
                for (int t = 0; t + 2 < tris.Count; t += 3)
                    Tri(v, sub[12], new Vector3(p[tris[t]].x, eave, p[tris[t]].y), new Vector3(p[tris[t + 1]].x, eave, p[tris[t + 1]].y), new Vector3(p[tris[t + 2]].x, eave, p[tris[t + 2]].y), Vector3.up);
            }
        }

        // Vorderseite (Unity: im Uhrzeigersinn gesehen) zeigt in Richtung 'face'
        static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 face)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), face) < 0f) { var x = b; b = d; d = x; }
            int k = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3);
        }
        static void Tri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 face)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), face) < 0f) { var x = b; b = c; c = x; }
            int k = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(k); t.Add(k + 1); t.Add(k + 2);
        }

        /// <summary>Ohrenschneiden fuer einfache Polygone (beliebiger Umlaufsinn). Ausrichtung der Dreiecke setzt der Aufrufer.</summary>
        static List<int> Triangulate(Vector2[] p)
        {
            var res = new List<int>();
            var idx = new List<int>(); for (int i = 0; i < p.Length; i++) idx.Add(i);
            float area = 0f; for (int i = 0, j = p.Length - 1; i < p.Length; j = i++) area += (p[j].x * p[i].y - p[i].x * p[j].y);
            float sgn = area >= 0f ? 1f : -1f;   // gegen den Uhrzeigersinn: konvexe Ecken haben positives Kreuzprodukt
            int guard = 0;
            while (idx.Count > 3 && guard++ < 5000)
            {
                bool cut = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector2 a = p[ia], b = p[ib], c = p[ic];
                    float cross = ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * sgn;
                    if (cross <= 0f) continue;   // keine konvexe Ecke
                    bool inside = false;
                    for (int j = 0; j < idx.Count && !inside; j++)
                    {
                        int ij = idx[j]; if (ij == ia || ij == ib || ij == ic) continue;
                        if (InTri(p[ij], a, b, c)) inside = true;
                    }
                    if (inside) continue;
                    res.Add(ia); res.Add(ib); res.Add(ic);
                    idx.RemoveAt(i); cut = true; break;
                }
                if (!cut) break;
            }
            if (idx.Count == 3) { res.Add(idx[0]); res.Add(idx[1]); res.Add(idx[2]); }
            return res;
        }
        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        // ---------------- Gewaesser ----------------
        void BuildWater(Transform root)
        {
            if (NetSync.Headless) return;
            var parent = new GameObject("Gewaesser").transform; parent.SetParent(root, false);
            var mat = new Material(GameMap.Mat(new Color(0.16f, 0.24f, 0.26f)));
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.9f);
            foreach (var a in areas)
            {
                if (a.cls != "wasser" || a.p.Length < 3) continue;
                var p = a.p;
                float area = 0f; for (int i = 0, j = p.Length - 1; i < p.Length; j = i++) area += (p[j].x - p[i].x) * (p[j].y + p[i].y);
                if (Mathf.Abs(area) < 40f) continue;
                float level = float.MaxValue; foreach (var q in p) level = Mathf.Min(level, GroundY(q.x, q.y));
                var tris = Triangulate(p);
                var vs = new List<Vector3>(); var ts = new List<int>();
                for (int t = 0; t + 2 < tris.Count; t += 3)
                    Tri(vs, ts, new Vector3(p[tris[t]].x, level + 0.25f, p[tris[t]].y), new Vector3(p[tris[t + 1]].x, level + 0.25f, p[tris[t + 1]].y), new Vector3(p[tris[t + 2]].x, level + 0.25f, p[tris[t + 2]].y), Vector3.up);
                var m = new Mesh { name = "Wasser", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; m.SetVertices(vs); m.SetTriangles(ts, 0); m.RecalculateNormals(); m.RecalculateBounds();
                var go = new GameObject("Wasser"); go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m; go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        /// <summary>Unsichtbarer Kartenrand: Panzer fahren nicht ins Umland (dort gibt es keine Kollision).</summary>
        void BuildEdge(Transform root)
        {
            var e = new GameObject("Kartenrand").transform; e.SetParent(root, false);
            float hgt = MaxRange + 200f;
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Rand");
                go.transform.SetParent(e, false);
                var bc = go.AddComponent<BoxCollider>();
                bool alongX = i < 2; float s = i % 2 == 0 ? 1f : -1f;
                go.transform.position = alongX ? new Vector3(0f, hgt / 2f - 50f, s * (half + 2f)) : new Vector3(s * (half + 2f), hgt / 2f - 50f, 0f);
                bc.size = alongX ? new Vector3(size + 10f, hgt, 4f) : new Vector3(4f, hgt, size + 10f);
            }
        }

        // ---------------- Baeume ----------------
        List<Forest.TreeInfo> PlaceTrees()
        {
            var list = new List<Forest.TreeInfo>(120000);
            var r = new System.Random(1234);
            var n = new Noise2D(77);
            const float step = 9.5f;
            for (float z = -half + 5f; z < half - 5f; z += step)
                for (float x = -half + 5f; x < half - 5f; x += step)
                {
                    float px = x + (float)(r.NextDouble() - 0.5) * step * 0.9f, pz = z + (float)(r.NextDouble() - 0.5) * step * 0.9f;
                    double roll = r.NextDouble();
                    byte s = SurfAt(px, pz);
                    bool tree = s == Wald ? roll < 0.85 : s == Gebuesch ? roll < 0.25 : s == Garten ? roll < 0.06 : false;
                    if (!tree) continue;
                    if (s != Wald) { bool nearHouse = false; foreach (var b in buildingBoxes) if (Mathf.Abs(px - b.x) < b.z + 4f && Mathf.Abs(pz - b.y) < b.z + 4f) { nearHouse = true; break; } if (nearHouse) continue; }
                    float y = GroundY(px, pz);
                    // Teutoburger Wald: Buchen und Fichten gemischt, Fichten eher in Flecken
                    byte kind = (byte)(n.Get(px / 350f + 3f, pz / 350f + 9f) > 0.55f ? 0 : 1);
                    float sc = s == Wald ? 0.8f + (float)r.NextDouble() * 0.5f : 0.6f + (float)r.NextDouble() * 0.4f;
                    list.Add(new Forest.TreeInfo { pos = new Vector3(px, y - 0.2f, pz), yaw = (float)r.NextDouble() * 360f, scale = sc, kind = kind });
                }
            return list;
        }

        // ---------------- Startpunkte ----------------
        void MakeSpawns()
        {
            var want = new List<(string, Vector2)>();
            foreach (var pl in places) if (pl.type == "town" || pl.type == "village" || pl.type == "hamlet") want.Add((pl.name, pl.p));
            foreach (var e in extraSpawns) want.Add((e.name, new Vector2(e.x, e.z)));
            want.Add(("Suedwest", new Vector2(-half + 300f, -half + 300f))); want.Add(("Nordost", new Vector2(half - 300f, half - 300f)));
            want.Add(("Nordwest", new Vector2(-half + 300f, half - 300f))); want.Add(("Suedost", new Vector2(half - 300f, -half + 300f)));
            foreach (var (nm, p) in want)
            {
                if (Mathf.Abs(p.x) > half - 40f || Mathf.Abs(p.y) > half - 40f) continue;
                // naechste freie Stelle (offenes Gelaende) rund um den Ort suchen
                Vector2 best = p; bool ok = false;
                for (float rr = 0f; rr < 600f && !ok; rr += 15f)
                    for (int k = 0; k < 24 && !ok; k++)
                    {
                        float ang = k * Mathf.PI * 2f / 24f;
                        Vector2 q = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
                        if (CanSpawn(q.x, q.y)) { best = q; ok = true; }
                    }
                if (!ok) continue;
                float yaw = Mathf.Atan2(-best.x, -best.y) * Mathf.Rad2Deg;   // Blick zur Kartenmitte
                spawns.Add(new SpawnPoint(nm, best.x, best.y, yaw));
            }
        }

        public override List<Spawn> Targets()
        {
            // Zielpanzer an einigen Orten (gegenueber vom ersten Startpunkt)
            var t = new List<Spawn>();
            var kinds = new[] { TankLibrary.Sherman, TankLibrary.T34_85, TankLibrary.PanzerIV, TankLibrary.Tiger };
            for (int i = 1; i < spawns.Count && t.Count < 6; i++)
            {
                var s = spawns[i];
                Vector2 p = s.xz + new Vector2(25f, 15f);
                if (!CanSpawn(p.x, p.y)) p = s.xz + new Vector2(-25f, -15f);
                if (!CanSpawn(p.x, p.y)) continue;
                t.Add(new Spawn(kinds[t.Count % kinds.Length], p.x, p.y, s.yaw + 180f));
            }
            return t;
        }

        /// <summary>Ortsnamen fuer die Startpunkt-Karte.</summary>
        public IEnumerable<(string name, Vector2 c)> Places()
        {
            foreach (var p in places)
                if ((p.type == "town" || p.type == "village" || p.type == "hamlet" || p.type == "peak") && Mathf.Abs(p.p.x) < half && Mathf.Abs(p.p.y) < half)
                    yield return (p.type == "peak" ? "▲ " + p.name : p.name, p.p);
        }

        // ---------------- Karte fuer die Startpunkt-Auswahl ----------------
        void BuildOverview()
        {
            const int N = 1024;
            overview = new Texture2D(N, N, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, name = title + " Karte" };
            var px = new Color32[N * N];
            Parallel.For(0, N, py =>
            {
                for (int qx = 0; qx < N; qx++)
                {
                    float wx = (qx + 0.5f) / N * size - half, wz = (py + 0.5f) / N * size - half;
                    Color c;
                    switch (SurfAt(wx, wz))
                    {
                        case Acker: c = new Color(0.82f, 0.74f, 0.46f); break;
                        case Erde: c = new Color(0.78f, 0.70f, 0.54f); break;
                        case Asphalt: c = new Color(0.96f, 0.94f, 0.90f); break;
                        case Wald: c = new Color(0.22f, 0.38f, 0.20f); break;
                        case Wasser: c = new Color(0.38f, 0.58f, 0.80f); break;
                        case Gebuesch: c = new Color(0.40f, 0.52f, 0.30f); break;
                        case Fels: c = new Color(0.62f, 0.60f, 0.56f); break;
                        case Gleis: c = new Color(0.40f, 0.36f, 0.34f); break;
                        case Garten: c = new Color(0.70f, 0.74f, 0.58f); break;
                        default: c = new Color(0.56f, 0.68f, 0.40f); break;
                    }
                    float hx = GroundY(wx + 6f, wz) - GroundY(wx - 6f, wz), hz = GroundY(wx, wz + 6f) - GroundY(wx, wz - 6f);
                    float shade = Mathf.Clamp(1f + (-hx + hz) * 0.06f, 0.6f, 1.3f);
                    float g = GroundY(wx, wz);
                    if (Mathf.Repeat(g + baseH, 10f) < 0.45f) shade *= 0.85f;   // Hoehenlinien alle 10 m (echte Meereshoehe)
                    px[py * N + qx] = c * shade;
                }
            });
            foreach (var b in buildings)
            {
                if (b.p.Length < 3) continue;
                Vector2 c = Vector2.zero; foreach (var q in b.p) c += q; c /= b.p.Length;
                int cx = Mathf.RoundToInt((c.x + half) / size * N), cz = Mathf.RoundToInt((c.y + half) / size * N);
                if (cx >= 0 && cz >= 0 && cx < N && cz < N) px[cz * N + cx] = b.cls == 'k' ? new Color32(70, 20, 20, 255) : new Color32(140, 60, 45, 255);
            }
            overview.SetPixels32(px); overview.Apply(false);
        }
    }
}
