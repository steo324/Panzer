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
        public override string Description => "10 x 10 km huegeliges Land: bewaldete Kuppen, weite Taeler, sieben Doerfer und acht Weiler, Hoefe und Felder, verbunden durch Haupt- und Nebenstrassen und Feldwege. Startpunkt auf der Karte frei waehlbar. Sehr lange Schussdistanzen, viel Deckung hinter Kuppen.";
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
        Roads.Field roadF;   // Abstand zum naechsten Fahrbahnrand (m, hoechstens 30), Art, Fahrbahnhoehe
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
        // Weiler (kleine Orte ohne Kirche) zwischen den Doerfern
        static readonly Village[] hamlets =
        {
            new Village("Birkenhof", -600f, -1300f), new Village("Eichwald", 400f, 2100f), new Village("Steinach", -3900f, -400f),
            new Village("Moosbach", 3800f, -800f), new Village("Talhausen", -1600f, 600f), new Village("Wiesental", 1300f, -1500f),
            new Village("Forsthaus", -2600f, 3900f), new Village("Grund", 2200f, 4300f),
        };
        // Strassen: Hauptstrassen (Rueckgrat West-Ost-Nord), Nebenstrassen zwischen den Doerfern, Ausfahrten zum Kartenrand
        static readonly int[,] mainLinks = { { 2, 0 }, { 0, 3 }, { 3, 1 }, { 3, 5 } };
        static readonly int[,] sideLinks = { { 0, 6 }, { 6, 1 }, { 2, 4 }, { 4, 3 }, { 4, 5 } };
        static readonly (int v, Vector2 p, int kind)[] exits =
        {
            (2, new Vector2(-5000f, 1100f), Roads.Haupt), (6, new Vector2(700f, -5000f), Roads.Neben), (1, new Vector2(5000f, -2700f), Roads.Haupt),
            (5, new Vector2(3700f, 5000f), Roads.Haupt), (4, new Vector2(-1300f, 5000f), Roads.Neben), (0, new Vector2(-5000f, -2600f), Roads.Neben),
        };
        readonly List<Roads.Road> roads = new List<Roads.Road>();
        readonly List<Vector2> farmSpots = new List<Vector2>();

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
        /// <summary>Abstand zum naechsten Fahrbahnrand (negativ = auf der Fahrbahn).</summary>
        float RoadAt(float x, float z)
        {
            if (roadF == null) return 99f;
            int ix = Mathf.Clamp(Mathf.RoundToInt(Gx(x)), 0, G - 1), iz = Mathf.Clamp(Mathf.RoundToInt(Gx(z)), 0, G - 1);
            return roadF.edge[Idx(ix, iz)];
        }

        public override int SurfaceAt(float x, float z)
        {
            switch (SurfAt(x, z))
            {
                case SAcker: return GroundTracks.Acker;
                case SWeg: return roadF != null && roadF.KindAt(x, z) <= Roads.Neben ? GroundTracks.Hart : GroundTracks.Weg;
                default: return GroundTracks.Wiese;
            }
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
            houses.Clear(); roads.Clear(); farmSpots.Clear();
            PlanRoads();
            BuildHeights();
            BuildSurfaces();
            float tGen = sw.ElapsedMilliseconds;
            BuildTerrain(root);
            BuildVillages(root);
            BuildFarms(root);
            Roads.BuildMeshes(root, roads, GroundY);
            var trees = PlaceTrees();
            Forest.Create(root, trees);
            if (!NetSync.Headless) { BuildOverview(); WheatField.Create(root, WheatGrows, GroundY, RowsAlongX, GrassGrows); BuildDetails(root); }
            int km = 0; foreach (var r in roads) km += r.p.Count * 4;
            Debug.Log($"[Huegelland] gebaut in {sw.ElapsedMilliseconds} ms (Gelaende-Rechnung {tGen} ms), {houses.Count} Gebaeude, {roads.Count} Strassen ({km / 1000} km), {trees.Count} Baeume");
        }

        // ---------------- Strassen ----------------
        /// <summary>
        /// Netz: Hauptstrassen als Rueckgrat, Nebenstrassen zwischen allen Doerfern und zu den Weilern,
        /// Feldwege von den Hoefen zur naechsten Strasse und aus den Orten in die Felder.
        /// </summary>
        void PlanRoads()
        {
            Roads.Road Add(int kind, Vector2 a, Vector2 b, int seed, float ampF = 0.09f, float maxAmp = 320f, float wave = 900f)
            {
                var r = new Roads.Road(kind, Roads.Meander(a, b, n, seed, ampF, maxAmp, wave));
                roads.Add(r);
                return r;
            }
            for (int i = 0; i < mainLinks.GetLength(0); i++) Add(Roads.Haupt, villages[mainLinks[i, 0]].c, villages[mainLinks[i, 1]].c, i, 0.06f, 220f, 1100f);
            for (int i = 0; i < exits.Length; i++) Add(exits[i].kind, villages[exits[i].v].c, exits[i].p, 50 + i, exits[i].kind == Roads.Haupt ? 0.06f : 0.09f);
            for (int i = 0; i < sideLinks.GetLength(0); i++) Add(Roads.Neben, villages[sideLinks[i, 0]].c, villages[sideLinks[i, 1]].c, 20 + i);
            // Weiler: Nebenstrasse zum naechsten Dorf, Feldweg zum naechsten anderen Ort
            for (int h = 0; h < hamlets.Length; h++)
            {
                Vector2 c = hamlets[h].c;
                int best = 0; float bd = 1e9f, bd2 = 1e9f; Vector2 second = c;
                for (int v = 0; v < villages.Length; v++) { float d = Vector2.Distance(villages[v].c, c); if (d < bd) { bd = d; best = v; } }
                Add(Roads.Neben, c, villages[best].c, 100 + h, 0.08f, 200f, 700f);
                foreach (var o in villages) { float d = Vector2.Distance(o.c, c); if (o.c != villages[best].c && d < bd2) { bd2 = d; second = o.c; } }
                foreach (var o in hamlets) { float d = Vector2.Distance(o.c, c); if (o.c != c && d < bd2) { bd2 = d; second = o.c; } }
                if (bd2 < 3200f) Add(Roads.Feldweg, c, second, 120 + h, 0.07f, 160f, 500f);
            }
            // Hoefe: auf flachem Land neben den Strassen, mit Feldweg zur Strasse
            var rng = new System.Random(77);
            var baseRoads = new List<Roads.Road>(roads);
            for (int tries = 0; tries < 900 && farmSpots.Count < 40; tries++)
            {
                var r = baseRoads[rng.Next(baseRoads.Count)];
                var p = r.p[rng.Next(r.p.Count)];
                float a = (float)(rng.NextDouble() * Mathf.PI * 2);
                Vector2 hp = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (float)(90 + rng.NextDouble() * 160);
                if (Mathf.Abs(hp.x) > Half - 200f || Mathf.Abs(hp.y) > Half - 200f) continue;
                bool bad = false;
                foreach (var v in villages) if (Vector2.Distance(hp, v.c) < 450f) bad = true;
                foreach (var v in hamlets) if (Vector2.Distance(hp, v.c) < 250f) bad = true;
                foreach (var f in farmSpots) if (Vector2.Distance(hp, f) < 380f) bad = true;
                foreach (var s in spawns) if (Vector2.Distance(hp, s.xz) < 160f) bad = true;
                float sl = Mathf.Abs(LowH(hp.x + 20f, hp.y) - LowH(hp.x - 20f, hp.y)) + Mathf.Abs(LowH(hp.x, hp.y + 20f) - LowH(hp.x, hp.y - 20f));
                if (bad || sl > 7f) continue;
                Vector2 near = p; float nd = 1e9f;
                foreach (var rr in baseRoads) for (int i = 0; i < rr.p.Count; i += 3) { float d = (rr.p[i] - hp).sqrMagnitude; if (d < nd) { nd = d; near = rr.p[i]; } }
                if (Mathf.Sqrt(nd) < 60f) continue;
                farmSpots.Add(hp);
                Add(Roads.Feldweg, near, hp, 300 + farmSpots.Count, 0.12f, 40f, 200f);
            }
            // Feldwege aus Doerfern und Weilern hinaus in die Felder
            int fw = 0;
            foreach (var v in villages)
                for (int k = 0; k < 4; k++)
                {
                    float a = (float)(rng.NextDouble() * Mathf.PI * 2);
                    Vector2 end = v.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (float)(700 + rng.NextDouble() * 900);
                    end.x = Mathf.Clamp(end.x, -Half + 150f, Half - 150f); end.y = Mathf.Clamp(end.y, -Half + 150f, Half - 150f);
                    Add(Roads.Feldweg, v.c + (end - v.c).normalized * 120f, end, 500 + fw++, 0.08f, 120f, 400f);
                }
            foreach (var v in hamlets)
                for (int k = 0; k < 2; k++)
                {
                    float a = (float)(rng.NextDouble() * Mathf.PI * 2);
                    Vector2 end = v.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (float)(500 + rng.NextDouble() * 600);
                    end.x = Mathf.Clamp(end.x, -Half + 150f, Half - 150f); end.y = Mathf.Clamp(end.y, -Half + 150f, Half - 150f);
                    Add(Roads.Feldweg, v.c, end, 600 + fw++, 0.08f, 100f, 400f);
                }
        }

        // ---------------- Hoehen ----------------
        float Big(float x, float z) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.72f, n.Fbm(x / 3400f + 11f, z / 3400f + 5f, 3)));
        float RidgeH(float x, float z) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1f, n.Ridge(x / 1600f + 3f, z / 1600f + 7f, 3)));
        float Hills(float x, float z) => (n.Fbm(x / 520f + 2f, z / 520f + 8f, 3) - 0.5f) * 2f;

        float LowH(float x, float z) => 25f + Big(x, z) * 140f + RidgeH(x, z) * 80f + Hills(x, z) * 9f;

        void BuildHeights()
        {
            H = new float[G * G];
            var flat = new List<Vector4>();   // x, z, Radius innen, Radius aussen
            foreach (var v in villages) flat.Add(new Vector4(v.c.x, v.c.y, 170f, 300f));
            foreach (var v in hamlets) flat.Add(new Vector4(v.c.x, v.c.y, 70f, 150f));
            foreach (var f in farmSpots) flat.Add(new Vector4(f.x, f.y, 35f, 80f));
            var fh = new float[flat.Count];
            for (int i = 0; i < flat.Count; i++) fh[i] = LowH(flat[i].x, flat[i].y);
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
                    // Doerfer, Weiler, Hoefe und Startpunkte: eingeebnet
                    for (int v = 0; v < flat.Count; v++)
                    {
                        var f = flat[v];
                        float dx = wx - f.x, dz = wz - f.y;
                        if (dx * dx + dz * dz > f.w * f.w) continue;
                        float dv = Mathf.Sqrt(dx * dx + dz * dz);
                        float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(f.z, f.w, dv)); h = Mathf.Lerp(fh[v] + (h - fh[v]) * 0.15f, h, t);
                    }
                    for (int s = 0; s < sc.Length; s++)
                    {
                        float ds = Vector2.Distance(new Vector2(wx, wz), spawns[s].xz);
                        if (ds < 110f) { float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(45f, 110f, ds)); h = Mathf.Lerp(sc[s] + (h - sc[s]) * 0.2f, h, t); }
                    }
                    H[Idx(ix, iz)] = Mathf.Clamp(h, 0f, MaxH - 1f);
                }
            });
            // Strassen: geglaettete Steigung, Damm und Einschnitt, Bankett und Graben
            foreach (var r in roads) Roads.SetHeights(r, GroundY, r.kind == Roads.Feldweg ? 6 : 12);
            roadF = Roads.Stamp(roads, G, Cell, -Half, 30f);
            var copy = (float[])H.Clone();
            Parallel.For(0, G * G, id => { H[id] = Mathf.Clamp(Roads.Apply(roadF, id, copy[id], 18f), 0f, MaxH - 1f); });
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
                    float d = roadF.edge[id];   // Abstand zum Fahrbahnrand
                    if (d < 0.4f) { surf[id] = SWeg; continue; }
                    float dv = 1e9f;
                    foreach (var v in villages) dv = Mathf.Min(dv, Vector2.Distance(new Vector2(wx, wz), v.c));
                    if (dv < 180f) { surf[id] = SDorf; continue; }
                    float dh = 1e9f;
                    foreach (var v in hamlets) dh = Mathf.Min(dh, Vector2.Distance(new Vector2(wx, wz), v.c));
                    foreach (var fs in farmSpots) dh = Mathf.Min(dh, Vector2.Distance(new Vector2(wx, wz), fs) + 40f);
                    if (dh < 75f) { surf[id] = SDorf; continue; }
                    float ds = 1e9f;
                    foreach (var s in spawns) ds = Mathf.Min(ds, Vector2.Distance(new Vector2(wx, wz), s.xz));
                    int ax = Mathf.Clamp(ix + 1, 0, G - 1), bx = Mathf.Clamp(ix - 1, 0, G - 1), az = Mathf.Clamp(iz + 1, 0, G - 1), bz = Mathf.Clamp(iz - 1, 0, G - 1);
                    float slope = Mathf.Sqrt(Mathf.Pow((H[Idx(ax, iz)] - H[Idx(bx, iz)]) / (2f * Cell), 2) + Mathf.Pow((H[Idx(ix, az)] - H[Idx(ix, bz)]) / (2f * Cell), 2));
                    // Wald: auf Kuppen, Kaemmen und steilen Haengen haeufiger
                    float f = n.Fbm(wx / 760f + 21f, wz / 760f + 13f, 4) + (H[id] - 110f) / 1000f + slope * 0.3f;
                    float edge = (n.Get(wx / 45f, wz / 45f) - 0.5f) * 0.03f;   // ausgefranste Waldraender
                    if (f + edge > 0.605f && d > 9f && dv > 260f && dh > 130f && ds > 130f) { surf[id] = SWald; continue; }
                    // Felder: Raster 160 x 170 m, etwa 40 % Aecker (nicht an steilen Haengen), Feldraine dazwischen
                    float lx = Mathf.Repeat(wx + Half, 160f), lz = Mathf.Repeat(wz + Half, 170f);
                    bool inside = lx > 7f && lx < 153f && lz > 7f && lz < 163f;
                    surf[id] = inside && slope < 0.16f && d > 2.5f && ds > 90f && FieldHash(wx, wz) % 10 < 4 ? SAcker : SWiese;   // Startplaetze: Wiese
                }
            });
        }

        bool WheatGrows(float x, float z) => SurfAt(x, z) == SAcker && RoadAt(x, z) > 3.5f && Mathf.Abs(x) < Half - 10f && Mathf.Abs(z) < Half - 10f;
        bool GrassGrows(float x, float z)
        {
            byte s = SurfAt(x, z);
            if (s != SWiese && s != SDorf) return false;
            if (RoadAt(x, z) < 1.0f) return false;
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
                                Weights(surf[Idx(gx, gz)], roadF.edge[Idx(gx, gz)], wx, wz, out float w0, out float w1, out float w2, out float w3);
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
            if (s != SWeg && d < 1.5f) { float t = Mathf.InverseLerp(0.4f, 1.5f, d); w2 = Mathf.Max(w2, 1f - t); w0 *= t; w1 *= t; w3 *= t; }   // Bankett
            float sum = w0 + w1 + w2 + w3;
            w0 /= sum; w1 /= sum; w2 /= sum; w3 /= sum;
        }

        // ---------------- Doerfer und Hoefe ----------------
        System.Random rng;
        float Rand(float a, float b) => (float)(a + rng.NextDouble() * (b - a));

        bool HouseFree(float x, float z, float r)
        {
            if (RoadAt(x, z) < r * 0.75f + 2.5f) return false;
            foreach (var h in houses) if ((x - h.x) * (x - h.x) + (z - h.y) * (z - h.y) < (r + h.z) * (r + h.z)) return false;
            return true;
        }

        static readonly Color[] Walls = { new Color(0.92f, 0.89f, 0.82f), new Color(0.86f, 0.82f, 0.72f), new Color(0.70f, 0.42f, 0.32f), new Color(0.82f, 0.78f, 0.68f), new Color(0.95f, 0.93f, 0.88f) };
        static readonly Color[] RoofCols = { new Color(0.55f, 0.22f, 0.15f), new Color(0.44f, 0.20f, 0.15f), new Color(0.30f, 0.27f, 0.26f), new Color(0.60f, 0.30f, 0.20f) };
        static readonly Color Barn = new Color(0.50f, 0.34f, 0.22f);

        void GroundRange(Vector2 p, float yaw, float w, float l, out float lo, out float hi)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            lo = float.MaxValue; hi = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 o = q * new Vector3((i & 1) == 0 ? -w / 2f : w / 2f, 0f, (i & 2) == 0 ? -l / 2f : l / 2f);
                float g = GroundY(p.x + o.x, p.y + o.z); lo = Mathf.Min(lo, g); hi = Mathf.Max(hi, g);
            }
            hi = Mathf.Max(hi, GroundY(p.x, p.y));
        }

        void KitHouse(MeshKit k, Vector2 p, float yaw, float w, float l, float wallH, Color wall, Color roof, bool barn = false)
        {
            GroundRange(p, yaw, w, l, out float lo, out float hi);
            k.House(p, yaw, w, l, wallH, w * (barn ? 0.42f : 0.5f), lo, hi, wall, barn ? MeshKit.TexWood : MeshKit.TexPlaster, roof, MeshKit.TexRoof, 0.5f);
        }

        void BuildVillages(Transform root)
        {
            rng = new System.Random(77);
            var k = new MeshKit();
            void Place(Village v, float radius, bool church, int barns)
            {
                if (church)
                {
                    Vector2 cp = v.c + new Vector2(22f, 18f);
                    for (int t = 0; t < 10 && !HouseFree(cp.x, cp.y, 16f); t++) cp = v.c + new Vector2(Rand(-60f, 60f), Rand(-60f, 60f));
                    if (HouseFree(cp.x, cp.y, 16f))
                    {
                        float yaw = Rand(0f, 360f);
                        GroundRange(cp, yaw, 11f, 22f, out float lo, out float hi);
                        k.Church(cp, yaw, lo, hi, new Color(0.92f, 0.90f, 0.84f), MeshKit.TexPlaster, RoofCols[2], new Color(0.26f, 0.30f, 0.30f), 22f, 11f, 22f, 12f);
                        houses.Add(new Vector3(cp.x, cp.y, 18f));
                    }
                }
                // Haeuser entlang der Strassen im Ort (alle 16 m ein Versuch je Seite)
                foreach (var r in roads)
                {
                    if (r.kind == Roads.Feldweg) continue;
                    for (int i = 2; i < r.p.Count; i += 4)
                    {
                        Vector2 p = r.p[i];
                        if (Vector2.Distance(p, v.c) > radius) continue;
                        Vector2 dir = (r.p[i] - r.p[i - 2]).normalized, side = new Vector2(-dir.y, dir.x);
                        float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                        foreach (float sgn in new[] { 1f, -1f })
                        {
                            if (rng.NextDouble() < 0.4) continue;
                            float w = Rand(7f, 10f), l = Rand(9f, 14f);
                            bool turn = rng.NextDouble() < 0.5;
                            Vector2 hp = p + side * sgn * (Roads.HalfWidth[r.kind] + 4.5f + (turn ? l : w) * 0.5f + Rand(0f, 3f));
                            float rr = Mathf.Max(w, l) * 0.62f;
                            if (!HouseFree(hp.x, hp.y, rr)) continue;
                            KitHouse(k, hp, yaw + (turn ? 90f : 0f), w, l, Rand(3.5f, 6f), Walls[rng.Next(Walls.Length)], RoofCols[rng.Next(RoofCols.Length)]);
                            houses.Add(new Vector3(hp.x, hp.y, rr));
                        }
                    }
                }
                // Scheunen am Ortsrand
                for (int j = 0; j < barns; j++)
                {
                    float a = Rand(0f, Mathf.PI * 2f), d = Rand(radius * 0.35f, radius * 0.95f);
                    Vector2 hp = v.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                    if (!HouseFree(hp.x, hp.y, 12f)) continue;
                    KitHouse(k, hp, Rand(0f, 360f), 12f, 18f, 6f, Barn, RoofCols[2], true);
                    houses.Add(new Vector3(hp.x, hp.y, 12f));
                }
            }
            foreach (var v in villages) Place(v, 175f, true, 6);
            foreach (var v in hamlets) Place(v, 70f, false, 2);
            k.Build(root, "Doerfer");
        }

        void BuildFarms(Transform root)
        {
            var k = new MeshKit();
            foreach (var hp in farmSpots)
            {
                // Hof: Wohnhaus, Scheune, Stall um einen Hofplatz, offen zur Zufahrt (der Feldweg endet in der Hofmitte)
                Vector2 toRoad = Vector2.down;
                foreach (var r in roads)
                    if (r.kind == Roads.Feldweg && (r.p[r.p.Count - 1] - hp).sqrMagnitude < 1f && r.p.Count > 6) { toRoad = (r.p[r.p.Count - 6] - hp).normalized; break; }
                float yaw = Mathf.Atan2(-toRoad.x, -toRoad.y) * Mathf.Rad2Deg;   // lokales +z zeigt weg von der Zufahrt
                var q = Quaternion.Euler(0f, yaw, 0f);
                Vector3 o1 = q * new Vector3(-15f, 0f, 2f), o2 = q * new Vector3(15f, 0f, 4f), o3 = q * new Vector3(0f, 0f, 19f);
                KitHouse(k, hp + new Vector2(o1.x, o1.z), yaw, 8f, 11f, 4.5f, Walls[rng.Next(Walls.Length)], RoofCols[0]);
                KitHouse(k, hp + new Vector2(o2.x, o2.z), yaw, 12f, 18f, 6f, Barn, RoofCols[2], true);
                KitHouse(k, hp + new Vector2(o3.x, o3.z), yaw + 90f, 7f, 14f, 3.5f, Walls[rng.Next(Walls.Length)], RoofCols[rng.Next(RoofCols.Length)]);
                houses.Add(new Vector3(hp.x + o1.x, hp.y + o1.z, 8f)); houses.Add(new Vector3(hp.x + o2.x, hp.y + o2.z, 11f)); houses.Add(new Vector3(hp.x + o3.x, hp.y + o3.z, 9f));
            }
            k.Build(root, "Bauernhoefe");
        }

        // ---------------- Kleinkram und Schilder ----------------
        void BuildDetails(Transform root)
        {
            var c = new MapDetails.Context
            {
                half = Half, seed = 2024, roads = roads, houses = houses, farms = farmSpots, groundY = GroundY,
                free = (x, z) => { byte s = SurfAt(x, z); return s != SWald && s != SWeg && RoadAt(x, z) > 1.5f; },
                meadow = (x, z) => SurfAt(x, z) == SWiese,
                forest = (x, z) => SurfAt(x, z) == SWald,
            };
            foreach (var v in villages) c.towns.Add(new MapDetails.Town { name = v.name, sub = "Kreis Hügelland", c = v.c, r = 200f });
            foreach (var v in hamlets) c.towns.Add(new MapDetails.Town { name = v.name, sub = null, c = v.c, r = 95f });
            MapDetails.Decorate(root, c);
        }

        // ---------------- Baeume ----------------
        List<Forest.TreeInfo> PlaceTrees()
        {
            var list = new List<Forest.TreeInfo>(120000);
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
                    // hoeher und in kuehlen Lagen Nadelwald (Fichte, auf Kuppen auch Kiefer), sonst Laubwald (Buche, Eiche, am Rand Birke)
                    float conifer = n.Get(px / 600f + 40f, pz / 600f + 2f) + (y - 120f) / 400f;
                    double k = r.NextDouble();
                    byte kind;
                    bool edge = SurfAt(px + 14f, pz) != SWald || SurfAt(px - 14f, pz) != SWald || SurfAt(px, pz + 14f) != SWald || SurfAt(px, pz - 14f) != SWald;
                    if (conifer > 0.52f) kind = n.Get(px / 300f + 7f, pz / 300f) > 0.62f ? (k < 0.7 ? Forest.Kiefer : Forest.Fichte) : (edge && k < 0.15 ? Forest.Birke : Forest.Fichte);
                    else kind = Forest.MixedBroadleaf(k, edge);
                    list.Add(new Forest.TreeInfo { pos = new Vector3(px, y - 0.2f, pz), yaw = (float)r.NextDouble() * 360f, scale = 0.75f + (float)r.NextDouble() * 0.5f, kind = kind });
                }
            // Alleen an Abschnitten der Haupt- und Nebenstrassen (Eiche, Buche), mit Luecken
            foreach (var rd in roads)
            {
                if (rd.kind == Roads.Feldweg) continue;
                float acc = 0f, next = 0f;
                for (int i = 1; i < rd.p.Count; i++)
                {
                    acc += Vector2.Distance(rd.p[i - 1], rd.p[i]);
                    if (acc < next) continue;
                    next = acc + (rd.kind == Roads.Haupt ? 14f : 16f);
                    Vector2 p = rd.p[i];
                    if (n.Get(p.x / 420f + 9f, p.y / 420f + 1f) < 0.6f) continue;
                    Vector2 dir = (rd.p[Mathf.Min(i + 1, rd.p.Count - 1)] - rd.p[i - 1]).normalized, side = new Vector2(-dir.y, dir.x);
                    foreach (float sg in new[] { -1f, 1f })
                    {
                        Vector2 q = p + side * sg * (Roads.HalfWidth[rd.kind] + 7.5f);
                        byte s = SurfAt(q.x, q.y);
                        if (s == SDorf || s == SWald || RoadAt(q.x, q.y) < 3f || !HouseFree(q.x, q.y, 3f)) continue;
                        list.Add(new Forest.TreeInfo { pos = new Vector3(q.x, GroundY(q.x, q.y) - 0.2f, q.y), yaw = (float)r.NextDouble() * 360f, scale = 0.7f + (float)r.NextDouble() * 0.3f, kind = r.NextDouble() < 0.55 ? Forest.Eiche : Forest.Buche });
                    }
                }
            }
            // Obstwiesen und Hofbaeume um Doerfer, Weiler und Hoefe; einzelne Feldbaeume
            void Around(Vector2 c, float rad, int count)
            {
                for (int k = 0; k < count; k++)
                {
                    Vector2 p = c + new Vector2((float)(r.NextDouble() - 0.5), (float)(r.NextDouble() - 0.5)) * rad * 2f;
                    byte s = SurfAt(p.x, p.y);
                    if ((s != SDorf && s != SWiese) || RoadAt(p.x, p.y) < 3f || !HouseFree(p.x, p.y, 3f)) continue;
                    list.Add(new Forest.TreeInfo { pos = new Vector3(p.x, GroundY(p.x, p.y) - 0.2f, p.y), yaw = (float)r.NextDouble() * 360f, scale = 0.45f + (float)r.NextDouble() * 0.35f, kind = r.NextDouble() < 0.6 ? Forest.Eiche : Forest.Buche });
                }
            }
            foreach (var v in villages) Around(v.c, 240f, 90);
            foreach (var v in hamlets) Around(v.c, 110f, 30);
            foreach (var f in farmSpots) Around(f, 70f, 14);
            for (int k = 0; k < 1500; k++)
            {
                float x = (float)(r.NextDouble() * (Size - 200f) - Half + 100f), z = (float)(r.NextDouble() * (Size - 200f) - Half + 100f);
                if (SurfAt(x, z) != SWiese || RoadAt(x, z) < 4f || !HouseFree(x, z, 3f)) continue;
                list.Add(new Forest.TreeInfo { pos = new Vector3(x, GroundY(x, z) - 0.2f, z), yaw = (float)r.NextDouble() * 360f, scale = 0.8f + (float)r.NextDouble() * 0.4f, kind = r.NextDouble() < 0.6 ? Forest.Eiche : r.NextDouble() < 0.5 ? Forest.Buche : Forest.Birke });
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
                    // Strassen deutlich (Hauptstrasse hell, Nebenstrasse, Feldweg duenner)
                    int rk = roadF.kind[id]; float re = roadF.edge[id];
                    if (rk == Roads.Haupt && re < 4.5f) c = new Color(0.98f, 0.94f, 0.80f);
                    else if (rk == Roads.Neben && re < 3.5f) c = new Color(0.94f, 0.90f, 0.78f);
                    else if (rk == Roads.Feldweg && re < 1.5f) c = Color.Lerp(c, new Color(0.80f, 0.72f, 0.56f), 0.7f);
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
        public override IEnumerable<(string name, Vector2 c)> Places() { foreach (var v in villages) yield return (v.name, v.c); foreach (var v in hamlets) yield return (v.name, v.c); }
    }
}
