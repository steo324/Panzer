using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Huegelland: 10 x 10 km. Bewaldete Kuppen und Kaemme, weite Taeler, sieben Doerfer mit Landstrassen dazwischen,
    /// Felder und Bauernhoefe. Startpunkt auf der Karte waehlbar. Alles aus fester Zufallszahl (bei jedem Spieler gleich).
    /// Gelaende: 5 x 5 Kacheln a 2 km (513 Hoehenpunkte, 3,9 m Raster).
    /// </summary>
    public class HillsMap : GameMap
    {
        public override string Name => "Huegelland";
        public override string Description => "10 x 10 km huegeliges Land: bewaldete Kuppen, weite Taeler, sieben Doerfer, Felder und Landstrassen. Startpunkt auf der Karte frei waehlbar. Sehr lange Schussdistanzen, viel Deckung hinter Kuppen.";
        public override bool Fog => true;
        public override float FogStart => 1500f;
        public override float FogEnd => 16000f;   // nur Dunst: Panzer bleiben auf jeder Entfernung sichtbar
        public override bool SpawnSelect => true;
        public override float Extent => Half;
        public override Vector2 PlayerXZ => spawns[0].xz;
        public override float PlayerYaw => spawns[0].yaw;
        public override List<SpawnPoint> SpawnPoints => spawns;
        public override Texture2D Overview => overview;

        public const float Size = 10000f, Half = Size / 2f, MaxH = 300f;
        const int Tiles = 5, TileRes = 513, AlphaPerTile = 512;
        const int G = (TileRes - 1) * Tiles + 1;        // 2561 Rasterpunkte je Richtung
        const float Cell = Size / (G - 1);              // 3,906 m
        const byte SWiese = 0, SAcker = 1, SWeg = 2, SWald = 3, SDorf = 4;

        float[] H;        // Hoehe in m
        float[] road;     // Abstand zur naechsten Strasse (m, hoechstens 40)
        byte[] surf;      // Bodenart
        Texture2D overview;
        Noise2D n;
        readonly List<Vector3> houses = new List<Vector3>();   // x, z, Radius (Platz fuer Haeuser)

        struct Village { public string name; public Vector2 c; public Village(string n, float x, float z) { name = n; c = new Vector2(x, z); } }
        static readonly Village[] villages =
        {
            new Village("Lindenau", -2300f, -1800f), new Village("Kirchberg", 2600f, -2300f), new Village("Westheim", -3300f, 1500f),
            new Village("Mittelbach", 1600f, 1000f), new Village("Nordhof", -700f, 3300f), new Village("Hohenfeld", 3300f, 3400f),
            new Village("Suedrode", 400f, -3500f),
        };
        // Strassen: Doerfer verbinden, dazu Ausfahrten zum Kartenrand (-1 = Endpunkt aus edge)
        static readonly int[,] links = { { 0, 6 }, { 6, 1 }, { 0, 2 }, { 0, 3 }, { 3, 1 }, { 2, 4 }, { 4, 3 }, { 3, 5 }, { 4, 5 } };
        static readonly (int v, Vector2 p)[] exits =
        {
            (2, new Vector2(-5000f, 1100f)), (6, new Vector2(700f, -5000f)), (1, new Vector2(5000f, -2700f)),
            (5, new Vector2(3700f, 5000f)), (4, new Vector2(-1300f, 5000f)), (0, new Vector2(-5000f, -2600f)),
        };
        readonly List<List<Vector2>> roads = new List<List<Vector2>>();

        readonly List<SpawnPoint> spawns = new List<SpawnPoint>
        {
            new SpawnPoint("Suedweide", -300f, -4300f, 10f),
            new SpawnPoint("Lindenau (Ortsrand)", -2050f, -2150f, 30f),
            new SpawnPoint("Kirchberg (Hang)", 2250f, -2700f, -20f),
            new SpawnPoint("Westtal", -4200f, 300f, 80f),
            new SpawnPoint("Mittelbach (Bruecke)", 1250f, 650f, 0f),
            new SpawnPoint("Osthoehe", 4300f, 700f, -90f),
            new SpawnPoint("Nordhof (Waldrand)", -1100f, 3900f, 170f),
            new SpawnPoint("Hohenfeld (Kuppe)", 3000f, 4100f, 200f),
        };

        public override List<Spawn> Targets() => new List<Spawn>
        {
            new Spawn(TankLibrary.Sherman, 150f, -3500f, 190f), new Spawn(TankLibrary.T34_85, -900f, -3300f, 160f),
            new Spawn(TankLibrary.PanzerIV, -2400f, -1500f, 200f), new Spawn(TankLibrary.Sherman, 2500f, -1900f, 170f),
            new Spawn(TankLibrary.Tiger, 1700f, 1350f, 180f), new Spawn(TankLibrary.T34_85, -3100f, 1900f, 120f),
            new Spawn(TankLibrary.Sherman, -500f, 2900f, 200f), new Spawn(TankLibrary.T34_85, 3500f, 3000f, 220f),
        };

        // ================= Abfragen =================
        static int Idx(int ix, int iz) => iz * G + ix;
        static float Gx(float x) => (x + Half) / Cell;

        public override float GroundY(float x, float z)
        {
            if (H == null) return base.GroundY(x, z);
            float fx = Mathf.Clamp(Gx(x), 0f, G - 1.001f), fz = Mathf.Clamp(Gx(z), 0f, G - 1.001f);
            int ix = (int)fx, iz = (int)fz; float tx = fx - ix, tz = fz - iz;
            float a = Mathf.Lerp(H[Idx(ix, iz)], H[Idx(ix + 1, iz)], tx), b = Mathf.Lerp(H[Idx(ix, iz + 1)], H[Idx(ix + 1, iz + 1)], tx);
            return Mathf.Lerp(a, b, tz);
        }

        byte SurfAt(float x, float z)
        {
            if (surf == null) return SWiese;
            int ix = Mathf.Clamp(Mathf.RoundToInt(Gx(x)), 0, G - 1), iz = Mathf.Clamp(Mathf.RoundToInt(Gx(z)), 0, G - 1);
            return surf[Idx(ix, iz)];
        }
        float RoadAt(float x, float z)
        {
            if (road == null) return 99f;
            int ix = Mathf.Clamp(Mathf.RoundToInt(Gx(x)), 0, G - 1), iz = Mathf.Clamp(Mathf.RoundToInt(Gx(z)), 0, G - 1);
            return road[Idx(ix, iz)];
        }

        public override int SurfaceAt(float x, float z)
        {
            switch (SurfAt(x, z)) { case SAcker: return GroundTracks.Acker; case SWeg: return GroundTracks.Weg; default: return GroundTracks.Wiese; }
        }

        public override bool CanSpawn(float x, float z)
        {
            if (Mathf.Abs(x) > Half - 60f || Mathf.Abs(z) > Half - 60f) return false;
            if (SurfAt(x, z) == SWald) return false;
            foreach (var h in houses) if ((x - h.x) * (x - h.x) + (z - h.y) * (z - h.y) < (h.z + 6f) * (h.z + 6f)) return false;
            float s = Mathf.Abs(GroundY(x + 4f, z) - GroundY(x - 4f, z)) + Mathf.Abs(GroundY(x, z + 4f) - GroundY(x, z - 4f));
            return s < 4.5f;   // nicht steiler als ca. 30 Grad
        }

        // ================= Bauen =================
        public override void Build(Transform root)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            n = new Noise2D(1944);
            houses.Clear(); roads.Clear();
            BuildRoads();
            BuildHeights();
            BuildSurfaces();
            float tGen = sw.ElapsedMilliseconds;
            BuildTerrain(root);
            BuildVillages(root);
            BuildFarms(root);
            var trees = PlaceTrees();
            Forest.Create(root, trees);
            if (!NetSync.Headless) { BuildOverview(); WheatField.Create(root, WheatGrows, GroundY, RowsAlongX, GrassGrows); }
            Debug.Log($"[Huegelland] gebaut in {sw.ElapsedMilliseconds} ms (Gelaende-Rechnung {tGen} ms), {houses.Count} Gebaeude, {trees.Count} Baeume");
        }

        // ---------------- Strassen ----------------
        void BuildRoads()
        {
            for (int i = 0; i < links.GetLength(0); i++) roads.Add(Meander(villages[links[i, 0]].c, villages[links[i, 1]].c, i));
            for (int i = 0; i < exits.Length; i++) roads.Add(Meander(villages[exits[i].v].c, exits[i].p, 50 + i));
            road = new float[G * G];
            for (int i = 0; i < road.Length; i++) road[i] = 40f;
            const float R = 30f;
            int rc = Mathf.CeilToInt(R / Cell);
            foreach (var pl in roads)
                for (int s = 0; s + 1 < pl.Count; s++)
                {
                    Vector2 a = pl[s], b = pl[s + 1];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 2f));
                    for (int k = 0; k <= steps; k++)
                    {
                        Vector2 p = Vector2.Lerp(a, b, k / (float)steps);
                        int cx = Mathf.RoundToInt(Gx(p.x)), cz = Mathf.RoundToInt(Gx(p.y));
                        for (int dz = -rc; dz <= rc; dz++)
                            for (int dx = -rc; dx <= rc; dx++)
                            {
                                int ix = cx + dx, iz = cz + dz;
                                if (ix < 0 || iz < 0 || ix >= G || iz >= G) continue;
                                float wx = ix * Cell - Half, wz = iz * Cell - Half;
                                float d = Mathf.Sqrt((wx - p.x) * (wx - p.x) + (wz - p.y) * (wz - p.y));
                                int id = Idx(ix, iz);
                                if (d < road[id]) road[id] = d;
                            }
                    }
                }
        }

        /// <summary>Strasse von a nach b mit sanften Kurven (Enden liegen fest).</summary>
        List<Vector2> Meander(Vector2 a, Vector2 b, int seed)
        {
            var pts = new List<Vector2>();
            float len = Vector2.Distance(a, b);
            Vector2 dir = (b - a) / len, side = new Vector2(-dir.y, dir.x);
            int count = Mathf.Max(2, Mathf.CeilToInt(len / 20f));
            float amp = Mathf.Min(320f, len * 0.09f);
            for (int i = 0; i <= count; i++)
            {
                float t = i / (float)count;
                float w = Mathf.Sin(t * Mathf.PI);   // an den Enden 0
                float off = ((n.Fbm(t * len / 900f + seed * 3.7f, seed * 1.3f, 3) - 0.5f) * 2.6f) * amp * w;
                pts.Add(a + dir * (t * len) + side * off);
            }
            return pts;
        }

        // ---------------- Hoehen ----------------
        float Big(float x, float z) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.72f, n.Fbm(x / 3400f + 11f, z / 3400f + 5f, 3)));
        float RidgeH(float x, float z) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1f, n.Ridge(x / 1600f + 3f, z / 1600f + 7f, 3)));
        float Hills(float x, float z) => (n.Fbm(x / 520f + 2f, z / 520f + 8f, 3) - 0.5f) * 2f;

        float LowH(float x, float z) => 25f + Big(x, z) * 140f + RidgeH(x, z) * 80f + Hills(x, z) * 9f;

        void BuildHeights()
        {
            H = new float[G * G];
            var vc = new float[villages.Length];
            for (int v = 0; v < villages.Length; v++) vc[v] = LowH(villages[v].c.x, villages[v].c.y);
            var sc = new float[spawns.Count];
            for (int s = 0; s < spawns.Count; s++) sc[s] = LowH(spawns[s].xz.x, spawns[s].xz.y);
            Parallel.For(0, G, iz =>
            {
                float wz = iz * Cell - Half;
                for (int ix = 0; ix < G; ix++)
                {
                    float wx = ix * Cell - Half;
                    float big = Big(wx, wz), ridge = RidgeH(wx, wz), hills = Hills(wx, wz);
                    float small = (n.Get(wx / 110f + 5f, wz / 110f + 3f) - 0.5f) * 3f + (n.Get(wx / 31f, wz / 31f) - 0.5f) * 0.5f;
                    float h = 25f + big * 140f + ridge * 80f + hills * 22f + small;
                    // Strassen: folgen dem Gelaende, aber ohne Buckel
                    float d = road[Idx(ix, iz)];
                    if (d < 26f) h = Mathf.Lerp(25f + big * 140f + ridge * 80f + hills * 9f, h, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 26f, d)));
                    // Doerfer und Startpunkte: eingeebnet
                    for (int v = 0; v < villages.Length; v++)
                    {
                        float dv = Vector2.Distance(new Vector2(wx, wz), villages[v].c);
                        if (dv < 300f) { float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(170f, 300f, dv)); h = Mathf.Lerp(vc[v] + (h - vc[v]) * 0.15f, h, t); }
                    }
                    for (int s = 0; s < sc.Length; s++)
                    {
                        float ds = Vector2.Distance(new Vector2(wx, wz), spawns[s].xz);
                        if (ds < 110f) { float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(45f, 110f, ds)); h = Mathf.Lerp(sc[s] + (h - sc[s]) * 0.2f, h, t); }
                    }
                    H[Idx(ix, iz)] = Mathf.Clamp(h, 0f, MaxH - 1f);
                }
            });
        }

        // ---------------- Bodenarten ----------------
        static int FieldHash(float wx, float wz)
        {
            int cx = Mathf.FloorToInt((wx + Half) / 160f), cz = Mathf.FloorToInt((wz + Half) / 170f);
            unchecked
            {
                int h = (cx * 73856093) ^ (cz * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;   // gut durchmischen (sonst Schachbrettmuster)
                return h & 0x7fffffff;
            }
        }
        static bool RowsAlongX(float x, float z) => (FieldHash(x, z) & 8) != 0;

        void BuildSurfaces()
        {
            surf = new byte[G * G];
            Parallel.For(0, G, iz =>
            {
                float wz = iz * Cell - Half;
                for (int ix = 0; ix < G; ix++)
                {
                    float wx = ix * Cell - Half;
                    int id = Idx(ix, iz);
                    float d = road[id];
                    if (d < 3.4f) { surf[id] = SWeg; continue; }
                    float dv = 1e9f;
                    foreach (var v in villages) dv = Mathf.Min(dv, Vector2.Distance(new Vector2(wx, wz), v.c));
                    if (dv < 180f) { surf[id] = SDorf; continue; }
                    float ds = 1e9f;
                    foreach (var s in spawns) ds = Mathf.Min(ds, Vector2.Distance(new Vector2(wx, wz), s.xz));
                    int ax = Mathf.Clamp(ix + 1, 0, G - 1), bx = Mathf.Clamp(ix - 1, 0, G - 1), az = Mathf.Clamp(iz + 1, 0, G - 1), bz = Mathf.Clamp(iz - 1, 0, G - 1);
                    float slope = Mathf.Sqrt(Mathf.Pow((H[Idx(ax, iz)] - H[Idx(bx, iz)]) / (2f * Cell), 2) + Mathf.Pow((H[Idx(ix, az)] - H[Idx(ix, bz)]) / (2f * Cell), 2));
                    // Wald: auf Kuppen, Kaemmen und steilen Haengen haeufiger
                    float f = n.Fbm(wx / 760f + 21f, wz / 760f + 13f, 4) + (H[id] - 110f) / 1000f + slope * 0.3f;
                    float edge = (n.Get(wx / 45f, wz / 45f) - 0.5f) * 0.03f;   // ausgefranste Waldraender
                    if (f + edge > 0.605f && d > 14f && dv > 260f && ds > 130f) { surf[id] = SWald; continue; }
                    // Felder: Raster 160 x 170 m, etwa 40 % Aecker (nicht an steilen Haengen), Feldraine dazwischen
                    float lx = Mathf.Repeat(wx + Half, 160f), lz = Mathf.Repeat(wz + Half, 170f);
                    bool inside = lx > 7f && lx < 153f && lz > 7f && lz < 163f;
                    surf[id] = inside && slope < 0.16f && d > 6f && ds > 90f && FieldHash(wx, wz) % 10 < 4 ? SAcker : SWiese;   // Startplaetze: Wiese
                }
            });
        }

        bool WheatGrows(float x, float z) => SurfAt(x, z) == SAcker && RoadAt(x, z) > 5.5f && Mathf.Abs(x) < Half - 10f && Mathf.Abs(z) < Half - 10f;
        bool GrassGrows(float x, float z)
        {
            byte s = SurfAt(x, z);
            if (s != SWiese && s != SDorf) return false;
            if (RoadAt(x, z) < 4f) return false;
            if (s == SDorf) foreach (var h in houses) if ((x - h.x) * (x - h.x) + (z - h.y) * (z - h.y) < (h.z + 2f) * (h.z + 2f)) return false;
            return true;
        }

        // ---------------- Gelaende-Kacheln ----------------
        void BuildTerrain(Transform root)
        {
            bool gfx = !NetSync.Headless;
            bool hd = gfx && Resources.Load<Texture2D>("Boden/Wiese_Farbe") != null;
            TerrainLayer[] layers = null;
            Material tm = null;
            if (gfx)
            {
                layers = hd ? new[]
                {
                    PlainsMap.HdLayer("Wiese", 3.2f, new Color(1.0f, 1.0f, 0.76f)),
                    PlainsMap.HdLayer("Acker", 2.6f, new Color(1.08f, 0.98f, 0.78f)),
                    PlainsMap.HdLayer("Weg", 3.0f, new Color(0.95f, 0.90f, 0.82f)),
                    PlainsMap.HdLayer("WieseDunkel", 3.6f, new Color(0.74f, 0.86f, 0.64f)),
                } : new[]
                {
                    PlainsMap.Layer(PlainsMap.NoiseTex(new Color(0.33f, 0.45f, 0.18f), new Color(0.24f, 0.36f, 0.12f), 7), 10f),
                    PlainsMap.Layer(PlainsMap.NoiseTex(new Color(0.78f, 0.66f, 0.33f), new Color(0.62f, 0.50f, 0.22f), 11), 6f),
                    PlainsMap.Layer(PlainsMap.NoiseTex(new Color(0.45f, 0.36f, 0.25f), new Color(0.35f, 0.27f, 0.18f), 13), 5f),
                    PlainsMap.Layer(PlainsMap.NoiseTex(new Color(0.26f, 0.38f, 0.15f), new Color(0.20f, 0.30f, 0.10f), 17), 12f),
                };
                var sh = Shader.Find("Universal Render Pipeline/Terrain/Lit");
                if (sh != null) { tm = new Material(sh); if (hd) PlainsMap.SetTerrainDetail(tm, GameSettings.Effects >= 1); }
            }
            var parent = new GameObject("Gelaende").transform; parent.SetParent(root, false);
            for (int tz = 0; tz < Tiles; tz++)
                for (int tx = 0; tx < Tiles; tx++)
                {
                    var td = new TerrainData { heightmapResolution = TileRes };
                    td.size = new Vector3(Size / Tiles, MaxH, Size / Tiles);
                    var h = new float[TileRes, TileRes];
                    for (int iz = 0; iz < TileRes; iz++)
                        for (int ix = 0; ix < TileRes; ix++)
                            h[iz, ix] = H[Idx(tx * (TileRes - 1) + ix, tz * (TileRes - 1) + iz)] / MaxH;
                    td.SetHeights(0, 0, h);
                    if (gfx)
                    {
                        td.terrainLayers = layers;
                        td.alphamapResolution = AlphaPerTile;
                        var a = new float[AlphaPerTile, AlphaPerTile, 4];
                        for (int iz = 0; iz < AlphaPerTile; iz++)
                            for (int ix = 0; ix < AlphaPerTile; ix++)
                            {
                                // Alphamap-Texel deckt die ganze Kachel ab: auf das Hoehenraster abbilden
                                int gx = tx * (TileRes - 1) + Mathf.RoundToInt(ix * (TileRes - 1) / (float)(AlphaPerTile - 1));
                                int gz = tz * (TileRes - 1) + Mathf.RoundToInt(iz * (TileRes - 1) / (float)(AlphaPerTile - 1));
                                float wx = gx * Cell - Half, wz = gz * Cell - Half;
                                Weights(surf[Idx(gx, gz)], road[Idx(gx, gz)], wx, wz, out float w0, out float w1, out float w2, out float w3);
                                // steile Haenge: Gras haelt nicht, offene Erde (Bodenart "Weg") - je steiler, desto mehr
                                int x0 = Mathf.Max(gx - 1, 0), x1 = Mathf.Min(gx + 1, G - 1), z0 = Mathf.Max(gz - 1, 0), z1 = Mathf.Min(gz + 1, G - 1);
                                float slope = new Vector2((H[Idx(x1, gz)] - H[Idx(x0, gz)]) / ((x1 - x0) * Cell), (H[Idx(gx, z1)] - H[Idx(gx, z0)]) / ((z1 - z0) * Cell)).magnitude;
                                float steep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1.0f, slope));
                                if (steep > 0f) { float k = 1f - steep; w0 *= k; w1 *= k; w3 *= k; w2 = w2 * k + steep; }
                                a[iz, ix, 0] = w0; a[iz, ix, 1] = w1; a[iz, ix, 2] = w2; a[iz, ix, 3] = w3;
                            }
                        td.SetAlphamaps(0, 0, a);
                    }
                    var go = Terrain.CreateTerrainGameObject(td);
                    go.name = $"Gelaende {tx},{tz}";
                    go.transform.SetParent(parent, false);
                    go.transform.position = new Vector3(-Half + tx * Size / Tiles, 0f, -Half + tz * Size / Tiles);
                    var t = go.GetComponent<Terrain>();
                    t.groupingID = 7; t.allowAutoConnect = true;
                    t.heightmapPixelError = 6f;
                    t.basemapDistance = 1200f;
                    if (tm != null) t.materialTemplate = tm;
                }
            Terrain.SetConnectivityDirty();
            Physics.SyncTransforms();
            // Kontrolle: Gelaende-Hoehe = gerechnete Hoehe?
            foreach (var p in new[] { new Vector2(1234f, -567f), new Vector2(-3000f, 2500f) })
                if (Physics.Raycast(new Vector3(p.x, 1000f, p.y), Vector3.down, out RaycastHit hit, 2000f))
                    Debug.Log($"[Huegelland] Pruefpunkt {p}: Gelaende {hit.point.y:0.0} m, gerechnet {GroundY(p.x, p.y):0.0} m ({hit.collider.name})");
        }

        void Weights(byte s, float d, float wx, float wz, out float w0, out float w1, out float w2, out float w3)
        {
            w0 = 0f; w1 = 0f; w2 = 0f; w3 = 0f;
            switch (s)
            {
                case SAcker: w1 = 1f; break;
                case SWeg: w2 = 1f; break;
                case SWald: w3 = 0.75f; w2 = 0.25f; break;   // Waldboden: dunkel mit Erde
                default:
                    float v = n.Get(wx * 0.008f + 3f, wz * 0.008f + 7f) * 0.6f + n.Get(wx * 0.045f + 13f, wz * 0.045f + 5f) * 0.4f;
                    w3 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.66f, v));
                    w0 = 1f - w3;
                    if (s == SDorf) { float b = Mathf.InverseLerp(0.55f, 0.75f, n.Get(wx * 0.05f, wz * 0.05f)) * 0.5f; w2 = b; w0 *= 1f - b; w3 *= 1f - b; }
                    break;
            }
            if (s != SWeg && d < 5f) { float t = Mathf.InverseLerp(3.4f, 5f, d); w2 = Mathf.Max(w2, 1f - t); w0 *= t; w1 *= t; w3 *= t; }
            float sum = w0 + w1 + w2 + w3;
            w0 /= sum; w1 /= sum; w2 /= sum; w3 /= sum;
        }

        // ---------------- Doerfer und Hoefe ----------------
        System.Random rng;
        float Rand(float a, float b) => (float)(a + rng.NextDouble() * (b - a));

        bool HouseFree(float x, float z, float r)
        {
            if (RoadAt(x, z) < r * 0.75f + 4f) return false;
            foreach (var h in houses) if ((x - h.x) * (x - h.x) + (z - h.y) * (z - h.y) < (r + h.z) * (r + h.z)) return false;
            return true;
        }

        void BuildVillages(Transform root)
        {
            rng = new System.Random(77);
            var parent = new GameObject("Doerfer").transform; parent.SetParent(root, false);
            Color[] walls = { new Color(0.86f, 0.83f, 0.75f), new Color(0.80f, 0.76f, 0.66f), new Color(0.62f, 0.34f, 0.25f), new Color(0.74f, 0.70f, 0.60f) };
            Color[] roofs = { new Color(0.47f, 0.17f, 0.12f), new Color(0.36f, 0.16f, 0.12f), new Color(0.28f, 0.24f, 0.22f) };
            foreach (var v in villages)
            {
                var vt = new GameObject(v.name).transform; vt.SetParent(parent, false);
                // Kirche mit Turm nahe der Mitte
                Vector2 cp = v.c + new Vector2(22f, 18f);
                if (HouseFree(cp.x, cp.y, 14f))
                {
                    float yaw = Rand(0f, 90f);
                    House(vt, cp.x, cp.y, yaw, 11f, 22f, 8f, new Color(0.88f, 0.86f, 0.80f), roofs[2]);
                    var q = Quaternion.Euler(0f, yaw, 0f);
                    Vector3 tp = new Vector3(cp.x, 0f, cp.y) + q * new Vector3(0f, 0f, 13f);
                    float ty = GroundY(tp.x, tp.z);
                    Block(vt, "Kirchturm", new Vector3(tp.x, ty + 9f, tp.z), new Vector3(5f, 20f, 5f), new Vector3(0f, yaw, 0f), new Color(0.86f, 0.84f, 0.78f));
                    var spire = Block(vt, "Turmspitze", new Vector3(tp.x, ty + 21.5f, tp.z), new Vector3(3.6f, 3.6f, 3.6f), new Vector3(45f, yaw, 45f), roofs[2]);
                    houses.Add(new Vector3(cp.x, cp.y, 16f));
                }
                // Haeuser entlang der Strassen im Ort
                foreach (var pl in roads)
                    for (int i = 1; i < pl.Count; i++)
                    {
                        Vector2 p = pl[i];
                        if (Vector2.Distance(p, v.c) > 175f || i % 1 != 0) continue;
                        Vector2 dir = (pl[i] - pl[i - 1]).normalized, side = new Vector2(-dir.y, dir.x);
                        float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                        foreach (float sgn in new[] { 1f, -1f })
                        {
                            if (rng.NextDouble() < 0.35) continue;
                            float w = Rand(7f, 10f), l = Rand(9f, 14f);
                            Vector2 hp = p + side * sgn * (8f + w * 0.5f + Rand(0f, 3f));
                            float r = Mathf.Max(w, l) * 0.62f;
                            if (!HouseFree(hp.x, hp.y, r)) continue;
                            House(vt, hp.x, hp.y, yaw + (rng.NextDouble() < 0.5 ? 0f : 90f), w, l, Rand(3.5f, 5.5f), walls[rng.Next(walls.Length)], roofs[rng.Next(roofs.Length)]);
                            houses.Add(new Vector3(hp.x, hp.y, r));
                        }
                    }
                // ein paar Scheunen/Hoefe am Ortsrand
                for (int k = 0; k < 6; k++)
                {
                    float a = Rand(0f, Mathf.PI * 2f), d = Rand(60f, 160f);
                    Vector2 hp = v.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                    if (!HouseFree(hp.x, hp.y, 12f)) continue;
                    House(vt, hp.x, hp.y, Rand(0f, 90f), 12f, 18f, 6f, new Color(0.45f, 0.30f, 0.20f), roofs[2]);
                    houses.Add(new Vector3(hp.x, hp.y, 12f));
                }
            }
        }

        void BuildFarms(Transform root)
        {
            var parent = new GameObject("Bauernhoefe").transform; parent.SetParent(root, false);
            Color[] walls = { new Color(0.85f, 0.82f, 0.74f), new Color(0.62f, 0.32f, 0.24f), new Color(0.78f, 0.74f, 0.64f) };
            int made = 0;
            for (int tries = 0; tries < 400 && made < 34; tries++)
            {
                var pl = roads[rng.Next(roads.Count)];
                var p = pl[rng.Next(pl.Count)];
                Vector2 dir = Random2(); Vector2 hp = p + dir * Rand(45f, 160f);
                byte s = SurfAt(hp.x, hp.y);
                if (s == SWald || s == SDorf || !HouseFree(hp.x, hp.y, 30f) || !CanSpawn(hp.x, hp.y)) continue;
                bool nearVillage = false;
                foreach (var v in villages) if (Vector2.Distance(hp, v.c) < 450f) nearVillage = true;
                if (nearVillage) continue;
                float yaw = Rand(0f, 90f); var q = Quaternion.Euler(0f, yaw, 0f);
                Vector3 o2 = q * new Vector3(18f, 0f, 4f), o3 = q * new Vector3(4f, 0f, 20f);
                House(parent, hp.x, hp.y, yaw, 8f, 11f, 4f, walls[rng.Next(walls.Length)], new Color(0.45f, 0.16f, 0.12f));
                House(parent, hp.x + o2.x, hp.y + o2.z, yaw + 90f, 12f, 18f, 6f, new Color(0.45f, 0.30f, 0.20f), new Color(0.30f, 0.24f, 0.22f));
                House(parent, hp.x + o3.x, hp.y + o3.z, yaw, 7f, 14f, 3.5f, walls[rng.Next(walls.Length)], new Color(0.45f, 0.16f, 0.12f));
                houses.Add(new Vector3(hp.x + 8f, hp.y + 8f, 26f));
                made++;
            }
        }

        Vector2 Random2() { float a = Rand(0f, Mathf.PI * 2f); return new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }

        void House(Transform root, float x, float z, float yaw, float w, float l, float hgt, Color wall, Color roof)
        {
            // Hangseite: tiefster Eckpunkt bestimmt die Unterkante, Sockel reicht in den Hang
            float y = Mathf.Min(Mathf.Min(GroundY(x - w / 2f, z - l / 2f), GroundY(x + w / 2f, z + l / 2f)), Mathf.Min(GroundY(x - w / 2f, z + l / 2f), GroundY(x + w / 2f, z - l / 2f)));
            float top = Mathf.Max(GroundY(x, z), y);
            var house = new GameObject("Haus").transform;
            house.SetParent(root, false);
            house.position = new Vector3(x, y, z);
            house.rotation = Quaternion.Euler(0f, yaw, 0f);
            float roofY = (top - y) + hgt;   // Waende von 1 m unter dem tiefsten Eck bis zur Traufe
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Waende"; body.transform.SetParent(house, false);
            body.transform.localPosition = new Vector3(0f, (roofY - 1f) / 2f, 0f);
            body.transform.localScale = new Vector3(w, roofY + 1f, l);
            body.GetComponent<Renderer>().sharedMaterial = Mat(wall);
            var r = GameObject.CreatePrimitive(PrimitiveType.Cube);
            r.name = "Dach"; r.transform.SetParent(house, false);
            float s = w / 1.414f + 0.3f;
            r.transform.localPosition = new Vector3(0f, roofY, 0f);
            r.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            r.transform.localScale = new Vector3(s, s, l + 0.6f);
            r.GetComponent<Renderer>().sharedMaterial = Mat(roof);
        }

        // ---------------- Baeume ----------------
        List<Forest.TreeInfo> PlaceTrees()
        {
            var list = new List<Forest.TreeInfo>(100000);
            var r = new System.Random(4242);
            const float step = 11.5f;
            for (float z = -Half + 20f; z < Half - 20f; z += step)
                for (float x = -Half + 20f; x < Half - 20f; x += step)
                {
                    float px = x + (float)(r.NextDouble() - 0.5) * step * 0.9f, pz = z + (float)(r.NextDouble() - 0.5) * step * 0.9f;
                    if (SurfAt(px, pz) != SWald) { r.NextDouble(); r.NextDouble(); continue; }
                    double roll = r.NextDouble();
                    if (roll > 0.8) { r.NextDouble(); continue; }   // Lichtungen
                    float y = GroundY(px, pz);
                    // hoeher und in kuehlen Lagen Fichten, sonst Laubwald (grossflaechig gemischt)
                    float conifer = n.Get(px / 600f + 40f, pz / 600f + 2f) + (y - 120f) / 400f;
                    byte kind = (byte)(conifer > 0.52f ? 0 : 1);
                    list.Add(new Forest.TreeInfo { pos = new Vector3(px, y - 0.2f, pz), yaw = (float)r.NextDouble() * 360f, scale = 0.75f + (float)r.NextDouble() * 0.5f, kind = kind });
                }
            return list;
        }

        // ---------------- Karte fuer die Startpunkt-Auswahl ----------------
        void BuildOverview()
        {
            const int N = 1024;
            overview = new Texture2D(N, N, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, name = "Huegelland Karte" };
            var px = new Color32[N * N];
            Parallel.For(0, N, py =>
            {
                for (int qx = 0; qx < N; qx++)
                {
                    int ix = Mathf.Clamp(Mathf.RoundToInt(qx / (float)(N - 1) * (G - 1)), 1, G - 2);
                    int iz = Mathf.Clamp(Mathf.RoundToInt(py / (float)(N - 1) * (G - 1)), 1, G - 2);
                    int id = Idx(ix, iz);
                    Color c;
                    switch (surf[id])
                    {
                        case SAcker: c = new Color(0.78f, 0.70f, 0.42f); break;
                        case SWeg: c = new Color(0.86f, 0.80f, 0.66f); break;
                        case SWald: c = new Color(0.20f, 0.34f, 0.18f); break;
                        case SDorf: c = new Color(0.62f, 0.66f, 0.46f); break;
                        default: c = new Color(0.50f, 0.62f, 0.34f); break;
                    }
                    // Schummerung (Licht von Nordwest) + Hoehenfarbe
                    float dx = H[Idx(ix + 1, iz)] - H[Idx(ix - 1, iz)], dz = H[Idx(ix, iz + 1)] - H[Idx(ix, iz - 1)];
                    float shade = Mathf.Clamp(1f + (-dx + dz) * 0.09f, 0.55f, 1.35f);
                    float alt = Mathf.Lerp(0.9f, 1.12f, H[id] / 260f);
                    c *= shade * alt;
                    // Hoehenlinien alle 20 m
                    if (Mathf.Repeat(H[id], 20f) < 0.9f && surf[id] != SWeg) c *= 0.82f;
                    px[py * N + qx] = c;
                }
            });
            // Gebaeude als dunkelrote Punkte
            foreach (var h in houses)
            {
                int cx = Mathf.RoundToInt((h.x + Half) / Size * (N - 1)), cz = Mathf.RoundToInt((h.y + Half) / Size * (N - 1));
                int rr = h.z > 15f ? 2 : 1;
                for (int dz = -rr; dz <= rr; dz++) for (int dx = -rr; dx <= rr; dx++)
                    {
                        int x = cx + dx, z = cz + dz;
                        if (x >= 0 && z >= 0 && x < N && z < N) px[z * N + x] = new Color32(120, 40, 30, 255);
                    }
            }
            overview.SetPixels32(px); overview.Apply(false);
        }

        /// <summary>Ortsnamen fuer die Karte (Mitte in Weltkoordinaten).</summary>
        public IEnumerable<(string name, Vector2 c)> Places() { foreach (var v in villages) yield return (v.name, v.c); }
    }
}
