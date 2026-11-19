using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Flandern: 6 x 6 km flaches Land wie im Ersten und Zweiten Weltkrieg. In der Mitte die Stadt Sint-Joris mit
    /// Marktplatz, Belfried, Tuchhalle, grosser Kirche, Wall und Stadtgraben; fuenf Doerfer mit Kirchtuermen,
    /// ein Kanal mit Deichen und Treidelpfaden, Baeche, Felder mit Hecken, Pappelreihen an den Strassen.
    /// Oestlich der Stadt ein Streifen Niemandsland mit Granattrichtern, Schuetzengraeben, Ruinen und Baumstuempfen.
    /// </summary>
    public class FlandernMap : GameMap
    {
        public override string Name => "Flandern";
        public override string Description => "6 x 6 km flaches Flandern: die Stadt Sint-Joris mit Marktplatz, Belfried, Kirche und Stadtgraben, fuenf Doerfer, ein Kanal mit Deichen, Felder mit Hecken und Pappelreihen. Im Osten Niemandsland mit Trichtern und Schuetzengraeben. Kurze Sicht hinter Hecken, Haeuserkampf in der Stadt.";
        public override bool Fog => true;
        public override float FogStart => 900f;
        public override float FogEnd => 9000f;
        public override bool SpawnSelect => true;
        public override float Extent => Half;
        public override Vector2 PlayerXZ => spawns.Count > 0 ? spawns[0].xz : new Vector2(-2500f, 0f);
        public override float PlayerYaw => spawns.Count > 0 ? spawns[0].yaw : 90f;
        public override List<SpawnPoint> SpawnPoints => spawns;
        public override Texture2D Overview => overview;

        public const float Size = 6000f, Half = Size / 2f, MaxH = 60f;
        const int Tiles = 4, TileRes = 513;                 // Kacheln zu 1,5 km
        const int G = (TileRes - 1) * Tiles + 1;            // 2049
        const float Cell = Size / (G - 1);                  // 2,93 m
        const byte SWiese = 0, SWeizen = 1, SAcker = 2, SWeg = 3, SWeide = 4, SStadt = 5, SWasser = 6, SSchlamm = 7, SGarten = 8, SWald = 9, SDeich = 10;
        const int WKanal = 0, WGracht = 1, WBach = 2, WGraben = 3;   // Art im Wasser-Abstandsfeld

        static readonly Vector2 C = new Vector2(0f, 250f);   // Stadtmitte (Marktplatz)
        const float RR = 440f;                                // Stadtgraben (Mitte)
        const float SqYaw = 8f, SqW = 96f, SqL = 72f;         // Marktplatz
        const float BandX0 = 880f, BandX1 = 1560f;            // Niemandsland

        float[] H; byte[] surf;
        Roads.Field roadF, waterF;
        Noise2D n;
        Texture2D overview;
        float townH, canalL;
        readonly List<SpawnPoint> spawns = new List<SpawnPoint>();
        readonly List<Vector3> houses = new List<Vector3>();
        readonly List<Roads.Road> roads = new List<Roads.Road>();
        readonly List<Roads.Road> waters = new List<Roads.Road>();
        readonly List<(string name, Vector2 c)> places = new List<(string, Vector2)>();
        readonly List<Vector2> farms = new List<Vector2>();
        readonly List<Vector4> craters = new List<Vector4>();   // x, z, Radius, Tiefe
        readonly List<Vector2> woods = new List<Vector2>();
        struct BridgeInfo { public Vector2 a, b; public float top, width; }
        readonly List<BridgeInfo> bridges = new List<BridgeInfo>();
        struct Village { public string name; public Vector2 c; public float r; }
        readonly List<Village> villages = new List<Village>();
        readonly List<Roads.Road> townStreets = new List<Roads.Road>();

        // ================= Abfragen =================
        static int Idx(int ix, int iz) => iz * G + ix;
        static int Gi(float x) => Mathf.Clamp(Mathf.RoundToInt((x + Half) / Cell), 0, G - 1);
        public override float GroundY(float x, float z) => H == null ? base.GroundY(x, z) : TerrainKit.Sample(H, G, Cell, Half, x, z);
        byte SurfAt(float x, float z) => surf == null ? SWiese : surf[Idx(Gi(x), Gi(z))];
        float RoadE(float x, float z) => roadF == null ? 99f : roadF.edge[Idx(Gi(x), Gi(z))];
        float WaterE(float x, float z) => waterF == null ? 99f : waterF.edge[Idx(Gi(x), Gi(z))];
        public override IEnumerable<(string name, Vector2 c)> Places() => places;

        public override int SurfaceAt(float x, float z)
        {
            switch (SurfAt(x, z))
            {
                case SWeizen: case SAcker: return GroundTracks.Acker;
                case SWeg: return roadF.KindAt(x, z) == Roads.Feldweg ? GroundTracks.Weg : GroundTracks.Hart;
                case SSchlamm: return GroundTracks.Acker;
                case SStadt: return GroundTracks.Hart;
                default: return GroundTracks.Wiese;
            }
        }

        public override bool CanSpawn(float x, float z)
        {
            if (Mathf.Abs(x) > Half - 50f || Mathf.Abs(z) > Half - 50f) return false;
            byte s = SurfAt(x, z);
            if (s == SWasser || s == SWald) return false;
            if (WaterE(x, z) < 10f || RoadE(x, z) < 4f) return false;
            foreach (var h in houses) if ((x - h.x) * (x - h.x) + (z - h.y) * (z - h.y) < (h.z + 7f) * (h.z + 7f)) return false;
            float sl = Mathf.Abs(GroundY(x + 4f, z) - GroundY(x - 4f, z)) + Mathf.Abs(GroundY(x, z + 4f) - GroundY(x, z - 4f));
            return sl < 2.5f;
        }

        // ================= Bauen =================
        public override void Build(Transform root)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            n = new Noise2D(1917);
            houses.Clear(); roads.Clear(); waters.Clear(); places.Clear(); farms.Clear(); craters.Clear(); woods.Clear();
            bridges.Clear(); villages.Clear(); spawns.Clear(); townStreets.Clear();
            townH = Raw(C.x, C.y);
            PlanWaters();
            PlanRoads();
            BuildHeights();
            BuildSurfaces();
            float tGen = sw.ElapsedMilliseconds;
            var layers = TerrainKit.Layers(false, out var tm);
            TerrainKit.Build(root, H, G, Size, MaxH, Tiles, 12, layers, tm, Weights, 4f);
            var kit = new MeshKit(300f);
            BuildTown(kit);
            BuildVillages(kit);
            BuildFarms(kit);
            BuildNoMansLand(kit);
            BuildBridges(kit);
            int tris = kit.Build(root, "Gebaeude");
            var hedgeKit = new MeshKit(300f);
            var trees = new List<Forest.TreeInfo>(60000);
            BuildHedges(hedgeKit, trees);
            hedgeKit.Build(root, "Hecken", false);
            Roads.BuildMeshes(root, roads, RoadSurfaceY);
            BuildWater(root);
            PlaceTrees(trees);
            Forest.Create(root, trees);
            MakeSpawns();
            if (!NetSync.Headless)
            {
                BuildOverview();
                WheatField.Create(root, (x, z) => SurfAt(x, z) == SWeizen && RoadE(x, z) > 3f, GroundY, RowsAlongU, GrassGrows);
                BuildDetails(root);
            }
            Debug.Log($"[Flandern] gebaut in {sw.ElapsedMilliseconds} ms (Gelaende-Rechnung {tGen} ms), {houses.Count} Gebaeude ({tris} Dreiecke), {roads.Count} Wege, {bridges.Count} Bruecken, {craters.Count} Trichter, {trees.Count} Baeume, {spawns.Count} Startpunkte");
        }

        // ---------------- Grundhoehe: fast eben, im Osten ein flacher Hoehenruecken ----------------
        float Raw(float x, float z)
        {
            float big = n.Fbm(x / 2600f + 3f, z / 2600f + 7f, 3) * 9f;
            float ridge = 13f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1000f, 2600f, x + (n.Get(z / 1500f, 2f) - 0.5f) * 500f));
            float polder = -2.5f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1400f, -2600f, x));
            float small = (n.Get(x / 140f + 5f, z / 140f + 3f) - 0.5f) * 0.9f + (n.Get(x / 37f, z / 37f) - 0.5f) * 0.25f;
            return 6f + big + ridge + polder + small;
        }

        // ---------------- Felder (gedrehtes, gewelltes Raster) ----------------
        const float FRot = 11f * Mathf.Deg2Rad, Su = 160f, Sv = 220f;
        void FieldCoords(float x, float z, out float fu, out float fv)
        {
            float c = Mathf.Cos(FRot), s = Mathf.Sin(FRot);
            fu = x * c + z * s; fv = -x * s + z * c;
            fu += (n.Get(fu / 700f + 3f, fv / 700f) - 0.5f) * 60f;
            fv += (n.Get(fu / 650f, fv / 650f + 9f) - 0.5f) * 60f;
        }
        static int Hash(int a, int b, int c)
        {
            unchecked { int h = a * 73856093 ^ b * 19349663 ^ c * 83492791; h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return h & 0x7fffffff; }
        }
        /// <summary>Feldart (0 Wiese, 1 Weizen, 2 Acker, 3 Weide) und Abstand zum Feldrand.</summary>
        int FieldAt(float x, float z, out float edge, out int ci, out int cj)
        {
            FieldCoords(x, z, out float fu, out float fv);
            ci = Mathf.FloorToInt(fu / Su); cj = Mathf.FloorToInt(fv / Sv);
            float lu = fu - ci * Su, lv = fv - cj * Sv;
            int h = Hash(ci, cj, 1);
            float sv = Sv;
            if ((h & 16) != 0) { int half = lv < Sv / 2f ? 0 : 1; cj = cj * 2 + half; lv = half == 0 ? lv : lv - Sv / 2f; sv = Sv / 2f; h = Hash(ci, cj, 2); }
            edge = Mathf.Min(Mathf.Min(lu, Su - lu), Mathf.Min(lv, sv - lv));
            int r = h % 20;
            return r < 7 ? 0 : r < 12 ? 1 : r < 16 ? 2 : 3;
        }
        bool RowsAlongU(float x, float z) { FieldAt(x, z, out _, out int i, out int j); return (Hash(i, j, 5) & 1) == 0; }

        // ---------------- Gewaesser ----------------
        static float CanalX(float z) => -1100f + 70f * Mathf.Sin(z / 1300f + 0.4f);
        void PlanWaters()
        {
            var p = new List<Vector2>();
            for (float z = -3100f; z <= 3100f; z += 30f) p.Add(new Vector2(CanalX(z), z));
            waters.Add(new Roads.Road(WKanal, Roads.Smooth(p, 5f)) { hw = 12f, ditch = false });
            // Stadtgraben (geschlossener Ring, leicht unregelmaessig)
            p = new List<Vector2>();
            for (int i = 0; i <= 360; i += 2)
            {
                float a = i * Mathf.Deg2Rad;
                float r = RR + 22f * Mathf.Sin(a * 3f + 0.5f) + 10f * Mathf.Sin(a * 7f);
                p.Add(C + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * r);
            }
            waters.Add(new Roads.Road(WGracht, p) { hw = 9f, ditch = false });
            // Baeche (zum Kanal)
            var b1 = new List<Vector2>();
            for (float x = 3100f; x >= CanalX(-1000f); x -= 60f) b1.Add(new Vector2(x, -950f + 120f * Mathf.Sin(x / 520f) + 50f * Mathf.Sin(x / 170f + 1f)));
            waters.Add(new Roads.Road(WBach, Roads.Smooth(b1, 5f)) { hw = 2.6f, ditch = false });
            var b2 = new List<Vector2>();
            for (float x = CanalX(1300f); x >= -3100f; x -= 60f) b2.Add(new Vector2(x, 1300f + 140f * Mathf.Sin(x / 610f + 2f) + 40f * Mathf.Sin(x / 150f)));
            waters.Add(new Roads.Road(WBach, Roads.Smooth(b2, 5f)) { hw = 2.4f, ditch = false });
            // Schuetzengraeben im Niemandsland (Zickzack)
            foreach (float gx in new[] { BandX0 + 70f, BandX1 - 70f })
            {
                var t = new List<Vector2>();
                bool flip = false;
                for (float z = -2950f; z <= 2950f; z += 11f) { t.Add(new Vector2(gx + (flip ? 7f : -7f) + (n.Get(z / 300f, gx) - 0.5f) * 60f, z)); flip = !flip; }
                waters.Add(new Roads.Road(WGraben, t) { hw = 1.6f, ditch = false });
            }
        }

        // ---------------- Strassen, Doerfer, Hoefe ----------------
        Vector2 Gate(float compassDeg, float r) { float a = compassDeg * Mathf.Deg2Rad; return C + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * r; }

        void PlanRoads()
        {
            void AddVillage(string nm, Vector2 c, float r) { villages.Add(new Village { name = nm, c = c, r = r }); places.Add((nm, c)); }
            places.Add(("Sint-Joris", C));
            AddVillage("Hooghem", new Vector2(-1900f, 1700f), 180f);
            AddVillage("Langemeersch", new Vector2(-2100f, -1500f), 170f);
            AddVillage("Polderkerke", new Vector2(-400f, -2300f), 170f);
            AddVillage("Kortewaal", new Vector2(2300f, -1700f), 160f);
            AddVillage("Brielendam", new Vector2(2400f, 1500f), 170f);

            // Strassen in der Stadt (Kopfsteinpflaster): vom Marktplatz durch die Tore ueber den Graben
            float[] dirs = { 270f, 90f, 0f, 180f, 225f, 45f, 135f };
            var gates = new Vector2[dirs.Length];
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector2 a = Gate(dirs[i], 46f), b = Gate(dirs[i], RR + 45f);
                gates[i] = b;
                Vector2 m = Gate(dirs[i] + (i % 2 == 0 ? 3f : -3f), (46f + RR) * 0.5f);
                var st = new Roads.Road(Roads.Pflaster, Roads.Smooth(new List<Vector2> { a, m, b }, 4f));
                roads.Add(st); townStreets.Add(st);
            }
            // innere Ringstrasse
            var ring = new List<Vector2>();
            for (int i = 0; i <= 360; i += 6) ring.Add(Gate(i, 255f + 12f * Mathf.Sin(i * Mathf.Deg2Rad * 2f)));
            var rs = new Roads.Road(Roads.Pflaster, Roads.Smooth(ring, 4f)); roads.Add(rs); townStreets.Add(rs);

            // Landstrassen
            Roads.Road Country(int kind, int seed, params Vector2[] pts)
            {
                var all = new List<Vector2>();
                for (int i = 0; i + 1 < pts.Length; i++)
                {
                    var seg = Roads.Meander(pts[i], pts[i + 1], n, seed + i, 0.05f, 110f, 700f);
                    if (all.Count > 0) seg.RemoveAt(0);
                    all.AddRange(seg);
                }
                var r = new Roads.Road(kind, Roads.Smooth(all, 4f));
                roads.Add(r);
                return r;
            }
            Country(Roads.Haupt, 1, gates[0], new Vector2(-1900f, 140f), new Vector2(-3100f, 120f));
            Country(Roads.Haupt, 2, gates[1], new Vector2(2000f, 380f), new Vector2(3100f, 430f));
            Country(Roads.Neben, 3, gates[2], villages[0].c, new Vector2(-2500f, 3100f));
            Country(Roads.Neben, 4, gates[3], villages[2].c, new Vector2(-500f, -3100f));
            Country(Roads.Neben, 5, gates[4], villages[1].c, new Vector2(-3100f, -1900f));
            Country(Roads.Neben, 6, gates[5], villages[4].c, new Vector2(3100f, 2300f));
            Country(Roads.Neben, 7, gates[6], villages[3].c, new Vector2(3100f, -2400f));
            Country(Roads.Neben, 8, villages[0].c, villages[1].c);
            Country(Roads.Neben, 9, villages[4].c, villages[3].c);
            Country(Roads.Neben, 10, villages[2].c, villages[3].c);
            Country(Roads.Neben, 11, villages[0].c, new Vector2(-600f, 2600f), new Vector2(400f, 3100f));

            // Treidelpfade auf beiden Deichen des Kanals
            foreach (float side in new[] { -1f, 1f })
            {
                var t = new List<Vector2>();
                for (float z = -3050f; z <= 3050f; z += 30f) t.Add(new Vector2(CanalX(z) + side * 19f, z));
                roads.Add(new Roads.Road(Roads.Feldweg, Roads.Smooth(t, 4f)));
            }

            // Hoefe in den Feldern, mit Feldweg zur naechsten Strasse
            var rng = new System.Random(1418);
            int made = 0;
            for (int tries = 0; tries < 800 && made < 34; tries++)
            {
                var hp = new Vector2((float)(rng.NextDouble() * 5600 - 2800), (float)(rng.NextDouble() * 5600 - 2800));
                bool bad = Vector2.Distance(hp, C) < RR + 200f || (hp.x > BandX0 - 80f && hp.x < BandX1 + 80f);
                foreach (var v in villages) if (Vector2.Distance(v.c, hp) < v.r + 180f) bad = true;
                foreach (var f in farms) if (Vector2.Distance(f, hp) < 300f) bad = true;
                foreach (var w in waters) if (DistTo(w.p, hp) < 60f) bad = true;
                if (bad) continue;
                float dr = 1e9f; Vector2 near = hp;
                foreach (var r in roads) { var q = Nearest(r.p, hp); float d = Vector2.Distance(q, hp); if (d < dr) { dr = d; near = q; } }
                if (dr < 50f || dr > 650f) continue;
                bool crossesBig = false;
                foreach (var w in waters) if (w.kind <= WGracht && Crosses(w.p, near, hp)) crossesBig = true;
                if (crossesBig) continue;
                farms.Add(hp);
                roads.Add(new Roads.Road(Roads.Feldweg, Roads.Smooth(new List<Vector2> { near, Vector2.Lerp(near, hp, 0.5f) + Perp(hp - near) * (float)(rng.NextDouble() - 0.5) * 40f, hp }, 4f)));
                made++;
            }
            // Feldwege aus den Doerfern in die Felder
            foreach (var v in villages)
                for (int k = 0; k < 3; k++)
                {
                    float a = (float)(rng.NextDouble() * Mathf.PI * 2);
                    Vector2 end = v.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (float)(500 + rng.NextDouble() * 500);
                    end.x = Mathf.Clamp(end.x, -2950f, 2950f); end.y = Mathf.Clamp(end.y, -2950f, 2950f);
                    bool crossesBig = false;
                    foreach (var w in waters) if (w.kind <= WGracht && Crosses(w.p, v.c, end)) crossesBig = true;
                    if (crossesBig) continue;
                    roads.Add(new Roads.Road(Roads.Feldweg, Roads.Meander(v.c, end, n, 200 + k + (int)v.c.x, 0.06f, 50f, 300f)));
                }
            // kleine Waelder (Bosjes)
            for (int k = 0; k < 40 && woods.Count < 9; k++)
            {
                var wp = new Vector2((float)(rng.NextDouble() * 5200 - 2600), (float)(rng.NextDouble() * 5200 - 2600));
                bool bad = Vector2.Distance(wp, C) < RR + 300f || (wp.x > BandX0 - 150f && wp.x < BandX1 + 150f);
                foreach (var v in villages) if (Vector2.Distance(v.c, wp) < v.r + 250f) bad = true;
                foreach (var f in farms) if (Vector2.Distance(f, wp) < 200f) bad = true;
                foreach (var w in woods) if (Vector2.Distance(w, wp) < 600f) bad = true;
                if (!bad) woods.Add(wp);
            }
            // Trichter im Niemandsland (und vereinzelt drumherum)
            for (int k = 0; k < 900; k++)
            {
                float x = k < 760 ? Mathf.Lerp(BandX0 - 60f, BandX1 + 60f, (float)rng.NextDouble()) : (float)(rng.NextDouble() * 5800 - 2900);
                float z = (float)(rng.NextDouble() * 5900 - 2950);
                if (Vector2.Distance(new Vector2(x, z), C) < RR + 60f) continue;
                float rad = (float)(3.5 + Mathf.Pow((float)rng.NextDouble(), 1.8f) * 6.0);
                craters.Add(new Vector4(x, z, rad, rad * (float)(0.18 + rng.NextDouble() * 0.12)));
            }
        }

        static Vector2 Perp(Vector2 d) { d.Normalize(); return new Vector2(-d.y, d.x); }
        static Vector2 Nearest(List<Vector2> p, Vector2 q) { float best = 1e18f; Vector2 r = p[0]; foreach (var a in p) { float d = (a - q).sqrMagnitude; if (d < best) { best = d; r = a; } } return r; }
        static float DistTo(List<Vector2> p, Vector2 q) => Vector2.Distance(Nearest(p, q), q);
        static bool SegX(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t)
        {
            t = 0f; Vector2 r = b - a, s = d - c;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-6f) return false;
            float tt = ((c.x - a.x) * s.y - (c.y - a.y) * s.x) / den, uu = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / den;
            t = tt; return tt >= 0f && tt <= 1f && uu >= 0f && uu <= 1f;
        }
        static bool Crosses(List<Vector2> line, Vector2 a, Vector2 b) { for (int i = 0; i + 1 < line.Count; i++) if (SegX(a, b, line[i], line[i + 1], out _)) return true; return false; }

        // ---------------- Hoehen ----------------
        void BuildHeights()
        {
            H = new float[G * G];
            Parallel.For(0, G, iz =>
            {
                float wz = iz * Cell - Half;
                for (int ix = 0; ix < G; ix++)
                {
                    float wx = ix * Cell - Half;
                    float h = Raw(wx, wz);
                    // Stadt: eben (innerhalb des Walls), Doerfer leicht eingeebnet
                    float dc = Vector2.Distance(new Vector2(wx, wz), C);
                    if (dc < RR + 60f) h = Mathf.Lerp(townH + (h - townH) * 0.08f, h, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RR - 30f, RR + 60f, dc)));
                    H[Idx(ix, iz)] = h;
                }
            });
            foreach (var v in villages)
            {
                float vh = GroundY(v.c.x, v.c.y);
                Flatten(v.c, v.r + 40f, vh, 0.2f);
            }
            foreach (var f in farms) Flatten(f, 45f, GroundY(f.x, f.y), 0.2f);
            // Kanal-Wasserspiegel: eben (tiefster Uferpunkt minus 1,2 m)
            canalL = 1e9f;
            foreach (var p in waters[0].p) canalL = Mathf.Min(canalL, GroundY(p.x, p.y));
            canalL -= 1.2f;
            // Deiche am Kanal und Wall um die Stadt (vor den Strassen: Strassen fuehren darueber)
            var dyke = Roads.Stamp(new List<Roads.Road> { waters[0], waters[1] }, G, Cell, -Half, 40f);
            Parallel.For(0, G * G, id =>
            {
                if (dyke.kind[id] == 255) return;
                float e = dyke.edge[id];
                if (dyke.kind[id] == WKanal)
                {
                    float top = canalL + 2.6f;
                    float k = e < 4f ? 1f : e < 12f ? 1f : Mathf.SmoothStep(1f, 0f, (e - 12f) / 14f);
                    if (e >= 0f) H[id] = Mathf.Max(H[id], Mathf.Lerp(H[id], top, k));
                }
                else
                {
                    // Wall: innen am Graben, 4 m hoch, 26 m breit
                    int ix = id % G, iz = id / G;
                    float dc = Vector2.Distance(new Vector2(ix * Cell - Half, iz * Cell - Half), C);
                    float inner = dc < RR ? 1f : 0f;
                    if (e > 2f && e < 34f && inner > 0f)
                    {
                        float t = (e - 2f) / 32f;
                        float prof = Mathf.Sin(Mathf.Clamp01(t * 1.15f) * Mathf.PI);
                        H[id] = Mathf.Max(H[id], townH + 4.2f * Mathf.Pow(prof, 0.6f));
                    }
                }
            });
            // Strassen
            foreach (var r in roads) Roads.SetHeights(r, GroundY, r.kind == Roads.Pflaster ? 4 : 12);
            roadF = Roads.Stamp(roads, G, Cell, -Half, 26f);
            var copy = (float[])H.Clone();
            Parallel.For(0, G * G, id => { H[id] = Roads.Apply(roadF, id, copy[id], 14f); });
            // Trichter (nicht auf Strassen)
            foreach (var c in craters)
            {
                if (RoadE(c.x, c.y) < c.z + 3f) continue;
                float R2 = c.z * 1.9f;
                int x0 = Gi(c.x - R2), x1 = Gi(c.x + R2), z0 = Gi(c.y - R2), z1 = Gi(c.y + R2);
                for (int iz = z0; iz <= z1; iz++)
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        float d = Vector2.Distance(new Vector2(ix * Cell - Half, iz * Cell - Half), new Vector2(c.x, c.y));
                        float q = d / c.z, dh;
                        if (q < 1f) dh = -c.w * (1f - q * q) + c.w * 0.25f * q * q * q;
                        else dh = c.w * 0.25f * Mathf.Exp(-Mathf.Pow((q - 1f) / 0.35f, 2f));
                        H[Idx(ix, iz)] += dh;
                    }
            }
            // Wasser und Graeben eingraben
            foreach (var w in waters)
            {
                if (w.kind == WKanal) { w.h = new float[w.p.Count]; for (int i = 0; i < w.h.Length; i++) w.h[i] = canalL; }
                else if (w.kind == WGracht) { w.h = new float[w.p.Count]; for (int i = 0; i < w.h.Length; i++) w.h[i] = townH - 1.6f; }
                else
                {
                    Roads.SetHeights(w, GroundY, w.kind == WGraben ? 3 : 20);
                    for (int i = 0; i < w.h.Length; i++) w.h[i] -= w.kind == WGraben ? 0f : 0.7f;
                    if (w.kind == WBach) for (int i = 0; i < w.h.Length; i++) w.h[i] = Mathf.Max(w.h[i], canalL);
                }
            }
            waterF = Roads.Stamp(waters, G, Cell, -Half, 10f);
            Parallel.For(0, G * G, id =>
            {
                int k = waterF.kind[id];
                if (k == 255) return;
                float e = waterF.edge[id], lvl = waterF.h[id];
                if (k == WGraben)
                {
                    if (roadF.edge[id] < 5f) return;   // Strassen queren die Graeben
                    if (e < 0f) H[id] = Mathf.Min(H[id], lvl - 1.8f);
                    else if (e < 2.5f) H[id] += 0.45f * Mathf.Sin(e / 2.5f * Mathf.PI);   // Brustwehr
                    return;
                }
                float D = k == WKanal ? 3.6f : k == WGracht ? 2.6f : 1.3f, bank = k == WBach ? 2.5f : 4f;
                if (e < 0f) H[id] = lvl - D * (0.45f + 0.55f * Mathf.Sqrt(Mathf.Clamp01(-e / 5f)));
                else if (e < bank) H[id] = Mathf.Min(H[id], Mathf.Lerp(lvl - D * 0.45f, lvl + 0.6f, e / bank));
                else if (k == WBach && e < bank + 4f) H[id] = Mathf.Min(H[id], Mathf.Lerp(lvl + 0.6f, H[id], (e - bank) / 4f));
            });
            for (int i = 0; i < H.Length; i++) H[i] = Mathf.Clamp(H[i], 0.5f, MaxH - 1f);
        }

        void Flatten(Vector2 c, float r, float h0, float keep)
        {
            int x0 = Gi(c.x - r), x1 = Gi(c.x + r), z0 = Gi(c.y - r), z1 = Gi(c.y + r);
            for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    float d = Vector2.Distance(new Vector2(ix * Cell - Half, iz * Cell - Half), c);
                    if (d > r) continue;
                    float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r * 0.6f, r, d));
                    int id = Idx(ix, iz);
                    H[id] = Mathf.Lerp(h0 + (H[id] - h0) * keep, H[id], t);
                }
        }

        // ---------------- Bodenarten ----------------
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
                    float we = waterF.edge[id]; int wk = waterF.kind[id];
                    if (wk != 255 && wk != WGraben && we < 0.3f) { surf[id] = SWasser; continue; }
                    if (roadF.edge[id] < 0.3f) { surf[id] = SWeg; continue; }
                    float dc = Vector2.Distance(new Vector2(wx, wz), C);
                    if (dc < RR - 6f) { surf[id] = dc < RR - 40f ? SStadt : SGarten; continue; }
                    bool band = wx > BandX0 - 40f && wx < BandX1 + 40f;
                    if (band || (wk == WGraben && we < 3f)) { surf[id] = (band && n.Get(wx / 60f, wz / 60f) > 0.35f) || wk == WGraben ? SSchlamm : SWiese; continue; }
                    if (wk == WKanal && we < 26f) { surf[id] = SDeich; continue; }
                    float dv = 1e9f; foreach (var v in villages) dv = Mathf.Min(dv, Vector2.Distance(new Vector2(wx, wz), v.c) - v.r);
                    if (dv < 0f) { surf[id] = SGarten; continue; }
                    bool wood = false;
                    foreach (var w in woods)
                    {
                        float d = Vector2.Distance(new Vector2(wx, wz), w);
                        if (d < 90f + (n.Get(wx / 50f, wz / 50f) - 0.5f) * 70f) { wood = true; break; }
                    }
                    if (wood && roadF.edge[id] > 6f) { surf[id] = SWald; continue; }
                    int fk = FieldAt(wx, wz, out float edge, out _, out _);
                    if (edge < 4f || roadF.edge[id] < 2.5f) { surf[id] = SWiese; continue; }
                    surf[id] = fk == 1 ? SWeizen : fk == 2 ? SAcker : fk == 3 ? SWeide : SWiese;
                }
            });
        }

        void Weights(int gx, int gz, float[] w)
        {
            int id = Idx(gx, gz);
            float wx = gx * Cell - Half, wz = gz * Cell - Half;
            switch (surf[id])
            {
                case SWeizen: w[1] = 1f; break;
                case SAcker: w[1] = 0.55f; w[2] = 0.45f; break;
                case SWeg: w[2] = 1f; break;
                case SWeide: w[3] = 0.85f; w[0] = 0.15f; break;
                case SStadt: w[2] = 0.6f; w[0] = 0.25f; w[3] = 0.15f; break;
                case SWasser: w[2] = 0.7f; w[3] = 0.3f; break;
                case SSchlamm: { float k = n.Get(wx / 12f, wz / 12f); w[2] = 0.55f + k * 0.3f; w[1] = 0.45f - k * 0.3f; break; }
                case SGarten: w[0] = 0.6f; w[3] = 0.4f; break;
                case SWald: w[3] = 0.7f; w[2] = 0.3f; break;
                case SDeich: w[0] = 0.7f; w[3] = 0.3f; break;
                default:
                {
                    float v = n.Get(wx * 0.008f + 3f, wz * 0.008f + 7f) * 0.6f + n.Get(wx * 0.045f + 13f, wz * 0.045f + 5f) * 0.4f;
                    w[3] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.66f, v)); w[0] = 1f - w[3];
                    break;
                }
            }
            float re = roadF.edge[id];
            if (surf[id] != SWeg && re < 1.5f) { float t = Mathf.InverseLerp(0.3f, 1.5f, re); w[0] *= t; w[1] *= t; w[3] *= t; w[2] = Mathf.Max(w[2], 1f - t); }
        }

        bool GrassGrows(float x, float z)
        {
            byte s = SurfAt(x, z);
            if (s != SWiese && s != SWeide && s != SGarten && s != SDeich) return false;
            if (RoadE(x, z) < 1.2f || WaterE(x, z) < 2f) return false;
            if (s == SGarten) foreach (var h in houses) if ((x - h.x) * (x - h.x) + (z - h.y) * (z - h.y) < (h.z + 2f) * (h.z + 2f)) return false;
            return true;
        }

        // ---------------- Gebaeude ----------------
        static readonly Color[] Brick = { new Color(0.62f, 0.32f, 0.24f), new Color(0.55f, 0.28f, 0.22f), new Color(0.68f, 0.40f, 0.30f), new Color(0.50f, 0.30f, 0.25f) };
        static readonly Color[] Whitewash = { new Color(0.94f, 0.92f, 0.86f), new Color(0.90f, 0.87f, 0.80f) };
        static readonly Color[] RoofTiles = { new Color(0.62f, 0.28f, 0.18f), new Color(0.52f, 0.24f, 0.17f), new Color(0.30f, 0.29f, 0.30f), new Color(0.70f, 0.36f, 0.22f) };

        bool Free(float x, float z, float r)
        {
            if (RoadE(x, z) < r * 0.7f + 1.5f || WaterE(x, z) < r + 4f) return false;
            foreach (var h in houses) if ((x - h.x) * (x - h.x) + (z - h.y) * (z - h.y) < (r + h.z) * (r + h.z)) return false;
            return true;
        }

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

        /// <summary>Flaemisches Haus: Giebel zur Strasse (w = Breite an der Strasse, l = Tiefe), Backstein oder gekalkt.</summary>
        void FlemishHouse(MeshKit k, System.Random rng, Vector2 p, float yaw, float w, float l, float wallH, bool town)
        {
            GroundRange(p, yaw, w, l, out float lo, out float hi);
            bool white = rng.NextDouble() < (town ? 0.22 : 0.35);
            Color wall = white ? Whitewash[rng.Next(Whitewash.Length)] : Brick[rng.Next(Brick.Length)];
            int steps = rng.NextDouble() < (town ? 0.65 : 0.25) ? 4 + rng.Next(3) : 0;
            float roofH = w * (steps > 0 ? 0.62f : 0.5f);
            k.House(p, yaw, w, l, wallH, roofH, lo, hi, wall, white ? MeshKit.TexPlaster : MeshKit.TexBrick, RoofTiles[rng.Next(RoofTiles.Length)], MeshKit.TexRoof, steps > 0 ? 0.05f : 0.35f, steps);
            houses.Add(new Vector3(p.x, p.y, Mathf.Max(w, l) * 0.55f));
        }

        bool OnSquare(Vector2 p, float margin)
        {
            Vector2 d = p - C;
            float a = -SqYaw * Mathf.Deg2Rad;
            float lx = d.x * Mathf.Cos(a) - d.y * Mathf.Sin(a), lz = d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a);
            return Mathf.Abs(lx) < SqW / 2f + margin && Mathf.Abs(lz) < SqL / 2f + margin;
        }

        bool TouchesOtherStreet(Vector2 p, float yaw, float w, float l, Roads.Road own)
        {
            var q = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < 5; i++)
            {
                Vector3 o = i == 4 ? Vector3.zero : q * new Vector3((i & 1) == 0 ? -w / 2f : w / 2f, 0f, (i & 2) == 0 ? -l / 2f : l / 2f);
                Vector2 c = p + new Vector2(o.x, o.z);
                if (RoadE(c.x, c.y) < 0.6f) return true;
                if (OnSquare(c, 1f)) return true;
            }
            return false;
        }

        void BuildTown(MeshKit k)
        {
            var rng = new System.Random(1302);
            float sq = SqYaw * Mathf.Deg2Rad;
            Vector2 ax = new Vector2(Mathf.Cos(sq), -Mathf.Sin(sq)), az = new Vector2(Mathf.Sin(sq), Mathf.Cos(sq));   // Platz-Achsen (x Ost, z Nord)
            // Marktplatz (Pflasterplatte)
            var corners = new[] { C - ax * SqW / 2f - az * SqL / 2f, C + ax * SqW / 2f - az * SqL / 2f, C + ax * SqW / 2f + az * SqL / 2f, C - ax * SqW / 2f + az * SqL / 2f };
            k.Plate(corners, townH + 0.06f, new Color(0.62f, 0.60f, 0.56f), MeshKit.TexCobble);
            // Suedseite: Tuchhalle mit Belfried
            Vector2 hall = C - az * (SqL / 2f + 11f);
            GroundRange(hall, SqYaw + 90f, 18f, 70f, out float lo, out float hi);
            k.House(hall, SqYaw + 90f, 18f, 70f, 12f, 9f, lo, hi, new Color(0.78f, 0.70f, 0.58f), MeshKit.TexBrick, new Color(0.30f, 0.30f, 0.32f), MeshKit.TexSlate, 0.2f, 0);
            houses.Add(new Vector3(hall.x, hall.y, 36f));
            k.Tower(hall + az * 2f, SqYaw, 12f, 46f, 20f, townH - 1f, new Color(0.76f, 0.68f, 0.56f), MeshKit.TexBrickPlain, new Color(0.24f, 0.30f, 0.28f));
            places.Add(("Belfried", hall));
            // Kirche nordoestlich des Platzes (grosses Schiff, Westturm)
            Vector2 church = C + az * 128f + ax * 70f;
            GroundRange(church, SqYaw, 20f, 52f, out lo, out hi);
            k.House(church, SqYaw, 20f, 52f, 14f, 10f, lo, hi, new Color(0.72f, 0.66f, 0.56f), MeshKit.TexBrickPlain, new Color(0.28f, 0.28f, 0.30f), MeshKit.TexSlate, 0.3f, 6);
            k.House(church + az * 31f, SqYaw, 14f, 10f, 11f, 7f, lo, hi, new Color(0.72f, 0.66f, 0.56f), MeshKit.TexBrickPlain, new Color(0.28f, 0.28f, 0.30f), MeshKit.TexSlate, 0.3f);
            k.Tower(church - az * 31f, SqYaw, 10f, 40f, 26f, townH - 1f, new Color(0.70f, 0.64f, 0.54f), MeshKit.TexBrickPlain, new Color(0.22f, 0.28f, 0.26f));
            houses.Add(new Vector3(church.x, church.y, 34f));
            // Haeuserzeilen um den Platz (Giebel zum Platz, hoch, Stufengiebel)
            for (int side = 0; side < 4; side++)
            {
                if (side == 0) continue;   // Sueden: Tuchhalle
                Vector2 dirAlong = side % 2 == 0 ? ax : az;
                Vector2 outward = side == 1 ? ax : side == 2 ? az : -ax;
                float len = side % 2 == 0 ? SqW : SqL, dist = side % 2 == 0 ? SqL / 2f : SqW / 2f;
                float pos = -len / 2f + 2f;
                while (pos < len / 2f - 4f)
                {
                    float w = (float)(6.5 + rng.NextDouble() * 3), l = (float)(12 + rng.NextDouble() * 4);
                    Vector2 hp = C + dirAlong * (pos + w / 2f) + outward * (dist + 1.5f + l / 2f);
                    float yaw = Mathf.Atan2(outward.x, outward.y) * Mathf.Rad2Deg;
                    if (!TouchesOtherStreet(hp, yaw, w, l, null))
                        FlemishHouse(k, rng, hp, yaw, w, l, (float)(8.5 + rng.NextDouble() * 3.5), true);
                    pos += w + 0.05f;
                }
            }
            // Haeuserzeilen an den Strassen (Reihenhaeuser, Giebel zur Strasse)
            foreach (var st in townStreets)
            {
                float hw = Roads.HalfWidth[st.kind];
                foreach (float sgn in new[] { -1f, 1f })
                {
                    float acc = 0f, next = (float)rng.NextDouble() * 4f;
                    for (int i = 1; i < st.p.Count; i++)
                    {
                        acc += Vector2.Distance(st.p[i - 1], st.p[i]);
                        if (acc < next) continue;
                        Vector2 p = st.p[i];
                        float dc = Vector2.Distance(p, C);
                        Vector2 dir = (st.p[Mathf.Min(i + 1, st.p.Count - 1)] - st.p[i - 1]).normalized;
                        Vector2 side = new Vector2(-dir.y, dir.x) * sgn;
                        float w = (float)(5.5 + rng.NextDouble() * 3.5), l = (float)(10 + rng.NextDouble() * 4);
                        next = acc + w + 0.05f;
                        if (dc < 40f || dc > RR - 40f) continue;
                        if (rng.NextDouble() < 0.06) { next += 8f; continue; }   // Durchgang/Hof
                        Vector2 hp = p + side * (hw + 1.6f + l / 2f);
                        float yaw = Mathf.Atan2(side.x, side.y) * Mathf.Rad2Deg;
                        if (TouchesOtherStreet(hp, yaw, w * 0.9f, l * 0.9f, st)) continue;
                        bool clash = false;
                        foreach (var h in houses) if ((hp.x - h.x) * (hp.x - h.x) + (hp.y - h.y) * (hp.y - h.y) < (h.z + 2.5f) * (h.z + 2.5f) && h.z > 15f) clash = true;
                        if (clash) continue;
                        bool near = false;
                        for (int j = Mathf.Max(0, houses.Count - 60); j < houses.Count; j++)
                        {
                            var h = houses[j];
                            if ((hp.x - h.x) * (hp.x - h.x) + (hp.y - h.y) * (hp.y - h.y) < 25f) near = true;
                        }
                        if (near) continue;
                        FlemishHouse(k, rng, hp, yaw, w, l, (float)(5.5 + rng.NextDouble() * 3.5), true);
                    }
                }
            }
        }

        void BuildVillages(MeshKit k)
        {
            var rng = new System.Random(2525);
            foreach (var v in villages)
            {
                // Kirche mit schlankem Turm an der Kreuzung
                Vector2 cp = v.c + new Vector2(28f, 26f);
                for (int t = 0; t < 14 && !Free(cp.x, cp.y, 16f); t++) cp = v.c + new Vector2((float)(rng.NextDouble() - 0.5) * 110f, (float)(rng.NextDouble() - 0.5) * 110f);
                if (Free(cp.x, cp.y, 16f))
                {
                    float yaw = (float)rng.NextDouble() * 360f;
                    GroundRange(cp, yaw, 11f, 24f, out float lo, out float hi);
                    k.Church(cp, yaw, lo, hi, Brick[rng.Next(Brick.Length)], MeshKit.TexBrickPlain, new Color(0.30f, 0.29f, 0.31f), new Color(0.22f, 0.27f, 0.26f), 24f, 11f, 26f, 15f, 4);
                    houses.Add(new Vector3(cp.x, cp.y, 19f));
                }
                foreach (var r in roads)
                {
                    if (r.kind == Roads.Feldweg) continue;
                    for (int i = 2; i < r.p.Count; i += 3)
                    {
                        Vector2 p = r.p[i];
                        if (Vector2.Distance(p, v.c) > v.r) continue;
                        Vector2 dir = (r.p[i] - r.p[i - 2]).normalized, side = new Vector2(-dir.y, dir.x);
                        foreach (float sgn in new[] { 1f, -1f })
                        {
                            if (rng.NextDouble() < 0.4) continue;
                            float w = (float)(6 + rng.NextDouble() * 3), l = (float)(9 + rng.NextDouble() * 4);
                            bool gableToStreet = rng.NextDouble() < 0.5;
                            Vector2 hp = p + side * sgn * (Roads.HalfWidth[r.kind] + 3f + (gableToStreet ? l : w) / 2f + (float)rng.NextDouble() * 4f);
                            if (!Free(hp.x, hp.y, 6.5f)) continue;
                            float yaw = Mathf.Atan2(side.x * sgn, side.y * sgn) * Mathf.Rad2Deg + (gableToStreet ? 0f : 90f);
                            FlemishHouse(k, rng, hp, yaw, w, l, (float)(3.8 + rng.NextDouble() * 2.2), false);
                        }
                    }
                }
            }
        }

        void BuildFarms(MeshKit k)
        {
            var rng = new System.Random(3636);
            foreach (var f in farms)
            {
                // Vierkanthof: Wohnhaus, Scheune und Stall um einen Hof
                float yaw = (float)rng.NextDouble() * 360f;
                var q = Quaternion.Euler(0f, yaw, 0f);
                FlemishHouse(k, rng, f, yaw + 90f, 8f, 14f, 4.2f, false);
                Vector3 o1 = q * new Vector3(-12f, 0f, 14f), o2 = q * new Vector3(12f, 0f, 14f);
                Vector2 b1 = f + new Vector2(o1.x, o1.z), b2 = f + new Vector2(o2.x, o2.z);
                GroundRange(b1, yaw, 11f, 20f, out float lo, out float hi);
                k.House(b1, yaw, 11f, 20f, 5f, 4.5f, lo, hi, Brick[1], MeshKit.TexBrickPlain, RoofTiles[2], MeshKit.TexRoof, 0.4f);
                GroundRange(b2, yaw, 9f, 18f, out lo, out hi);
                k.House(b2, yaw, 9f, 18f, 3.8f, 3.5f, lo, hi, Whitewash[0], MeshKit.TexPlaster, RoofTiles[0], MeshKit.TexRoof, 0.4f);
                houses.Add(new Vector3(f.x + (o1.x + o2.x) / 4f, f.y + (o1.z + o2.z) / 4f, 24f));
            }
        }

        void BuildNoMansLand(MeshKit k)
        {
            var rng = new System.Random(1916);
            // zerschossene Hoefe
            int made = 0;
            for (int t = 0; t < 200 && made < 10; t++)
            {
                float x = Mathf.Lerp(BandX0 + 20f, BandX1 - 20f, (float)rng.NextDouble()), z = (float)(rng.NextDouble() * 5600 - 2800);
                if (!Free(x, z, 14f)) continue;
                float yaw = (float)rng.NextDouble() * 360f;
                k.Ruin(new Vector2(x, z), yaw, (float)(8 + rng.NextDouble() * 5), (float)(10 + rng.NextDouble() * 8), GroundY(x, z), rng, Brick[rng.Next(Brick.Length)], MeshKit.TexBrickPlain);
                houses.Add(new Vector3(x, z, 10f));
                made++;
            }
            // Baumstuempfe
            for (int t = 0; t < 260; t++)
            {
                float x = Mathf.Lerp(BandX0 - 30f, BandX1 + 30f, (float)rng.NextDouble()), z = (float)(rng.NextDouble() * 5800 - 2900);
                if (RoadE(x, z) < 3f) continue;
                float hh = (float)(1.5 + rng.NextDouble() * 5.5), r = (float)(0.25 + rng.NextDouble() * 0.2);
                float y = GroundY(x, z);
                k.At(x, z);
                k.Box(new Vector3(x, y + hh / 2f - 0.3f, z), new Vector3(r * 2f, hh, r * 2f), (float)rng.NextDouble() * 90f, new Color(0.26f, 0.23f, 0.20f), MeshKit.TexStone);
            }
        }

        // ---------------- Bruecken ----------------
        void BuildBridges(MeshKit k)
        {
            foreach (var r in roads)
                foreach (var w in waters)
                {
                    if (w.kind == WGraben) continue;
                    for (int i = 0; i + 1 < r.p.Count; i++)
                    {
                        if (WaterE(r.p[i].x, r.p[i].y) > 40f && WaterE(r.p[i + 1].x, r.p[i + 1].y) > 40f) continue;   // weit weg von jedem Wasser
                        for (int j = 0; j + 1 < w.p.Count; j++)
                        {
                            if (!SegX(r.p[i], r.p[i + 1], w.p[j], w.p[j + 1], out float t)) continue;
                            Vector2 c = Vector2.Lerp(r.p[i], r.p[i + 1], t);
                            Vector2 d = (r.p[Mathf.Min(i + 3, r.p.Count - 1)] - r.p[Mathf.Max(i - 2, 0)]).normalized;
                            float half = w.HW + (w.kind == WBach ? 3.5f : 6f);
                            float top = Mathf.Lerp(r.h[i], r.h[i + 1], t) + 0.15f;
                            var bi = new BridgeInfo { a = c - d * half, b = c + d * half, top = top, width = Roads.HalfWidth[r.kind] * 2f + (r.kind == Roads.Feldweg ? 1.0f : 1.6f) };
                            bridges.Add(bi);
                            bool stone = w.kind != WKanal;
                            k.Bridge(bi.a, bi.b, bi.width, top, GroundY(c.x, c.y), stone ? new Color(0.58f, 0.40f, 0.32f) : new Color(0.50f, 0.50f, 0.50f), stone ? new Color(0.55f, 0.36f, 0.29f) : new Color(0.30f, 0.30f, 0.32f), stone ? MeshKit.TexBrickPlain : MeshKit.TexStone);
                        }
                    }
                }
        }

        float RoadSurfaceY(float x, float z)
        {
            float g = GroundY(x, z);
            foreach (var b in bridges)
            {
                Vector2 ab = b.b - b.a; float len = ab.magnitude; Vector2 d = ab / len;
                Vector2 q = new Vector2(x, z) - b.a;
                float along = Vector2.Dot(q, d), across = Mathf.Abs(q.x * d.y - q.y * d.x);
                if (along >= -0.5f && along <= len + 0.5f && across <= b.width / 2f + 1f) g = Mathf.Max(g, b.top - 0.05f);
            }
            return g;
        }

        // ---------------- Wasser ----------------
        void BuildWater(Transform root)
        {
            if (NetSync.Headless) return;
            var mat = new Material(GameMap.Mat(new Color(0.22f, 0.28f, 0.24f)));
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.9f);
            var parent = new GameObject("Gewaesser").transform; parent.SetParent(root, false);
            foreach (var w in waters)
            {
                if (w.kind == WGraben) continue;
                var lv = new float[w.p.Count];
                float drop = w.kind == WBach ? 0.35f : 0.5f;
                for (int i = 0; i < lv.Length; i++) lv[i] = w.h[i] - drop;
                TerrainKit.WaterRibbon(parent, w.p, lv, w.HW + (w.kind == WBach ? 1.2f : 2.5f), mat);
            }
        }

        // ---------------- Hecken ----------------
        void BuildHedges(MeshKit k, List<Forest.TreeInfo> trees)
        {
            var rng = new System.Random(808);
            float c = Mathf.Cos(FRot), s = Mathf.Sin(FRot);
            Vector2 ToWorld(float fu, float fv) => new Vector2(fu * c - fv * s, fu * s + fv * c);
            bool Ok(Vector2 p)
            {
                if (Mathf.Abs(p.x) > Half - 15f || Mathf.Abs(p.y) > Half - 15f) return false;
                if (Vector2.Distance(p, C) < RR + 45f) return false;
                if (p.x > BandX0 - 60f && p.x < BandX1 + 60f) return false;
                if (RoadE(p.x, p.y) < 3.5f || WaterE(p.x, p.y) < 5f) return false;
                byte sf = SurfAt(p.x, p.y);
                if (sf == SGarten || sf == SWald || sf == SDeich) return false;
                foreach (var h in houses) if ((p.x - h.x) * (p.x - h.x) + (p.y - h.y) * (p.y - h.y) < (h.z + 5f) * (h.z + 5f)) return false;
                return true;
            }
            int segs = 0;
            // Linien des Feldrasters (Feldkoordinaten -> Welt; die Wellung des Rasters wird mit abgetastet)
            float range = Half * 1.5f;
            for (int dirU = 0; dirU < 2; dirU++)
            {
                float S1 = dirU == 0 ? Su : Sv, S2 = dirU == 0 ? Sv : Su;
                for (int li = Mathf.FloorToInt(-range / S1); li <= Mathf.CeilToInt(range / S1); li++)
                    for (int cell = Mathf.FloorToInt(-range / S2); cell <= Mathf.CeilToInt(range / S2); cell++)
                    {
                        if (Hash(li, cell, dirU + 7) % 10 >= 4) continue;   // ca. 40 % der Feldraender haben Hecken
                        Vector2 prev = Vector2.zero; float prevY = 0f, prevH = 0f; bool has = false;
                        for (float t = 4f; t <= S2 - 4f; t += 4f)
                        {
                            float a = li * S1, b = cell * S2 + t;
                            // Feldkoordinaten mit Wellung rueckwaerts naeherungsweise: Punkt suchen, dessen gewellte Koordinate auf der Linie liegt
                            Vector2 w0 = dirU == 0 ? ToWorld(a, b) : ToWorld(b, a);
                            for (int it = 0; it < 3; it++)
                            {
                                FieldCoords(w0.x, w0.y, out float fu, out float fv);
                                float err = (dirU == 0 ? fu : fv) - a;
                                Vector2 axis = dirU == 0 ? new Vector2(c, s) : new Vector2(-s, c);
                                w0 -= axis * err;
                            }
                            bool ok = Ok(w0);
                            float y = GroundY(w0.x, w0.y);
                            float hh = 1.3f + n.Get(w0.x / 9f, w0.y / 9f) * 1.2f + n.Get(w0.x / 2.5f + 7f, w0.y / 2.5f) * 1.1f;
                            if (n.Get(w0.x / 23f + 3f, w0.y / 23f) > 0.78f) ok = false;   // Luecken (Durchlass, Gatter)
                            if (ok && has) { k.Hedge(prev, w0, prevY, y, prevH, hh, 1.6f + (float)rng.NextDouble() * 0.8f, new Color(0.19f, 0.30f, 0.12f) * (0.8f + (float)rng.NextDouble() * 0.4f)); segs++; }
                            if (ok && rng.NextDouble() < 0.05)
                                trees.Add(new Forest.TreeInfo { pos = new Vector3(w0.x, y - 0.2f, w0.y), yaw = (float)rng.NextDouble() * 360f, scale = 0.6f + (float)rng.NextDouble() * 0.5f, kind = rng.NextDouble() < 0.5 ? Forest.Eiche : rng.NextDouble() < 0.5 ? Forest.Weide : Forest.Buche });
                            prev = w0; prevY = y; prevH = hh; has = ok;
                        }
                    }
            }
            Debug.Log($"[Flandern] {segs} Heckenstuecke");
        }

        // ---------------- Baeume ----------------
        void PlaceTrees(List<Forest.TreeInfo> list)
        {
            var r = new System.Random(919);
            Forest.TreeInfo T(Vector2 p, byte kind, float sc) => new Forest.TreeInfo { pos = new Vector3(p.x, GroundY(p.x, p.y) - 0.2f, p.y), yaw = (float)r.NextDouble() * 360f, scale = sc, kind = kind };
            bool Clear(Vector2 p, float rr)
            {
                if (Mathf.Abs(p.x) > Half - 5f || Mathf.Abs(p.y) > Half - 5f) return false;
                if (RoadE(p.x, p.y) < 1.5f || WaterE(p.x, p.y) < 1.5f) return false;
                foreach (var h in houses) if ((p.x - h.x) * (p.x - h.x) + (p.y - h.y) * (p.y - h.y) < (h.z + rr) * (h.z + rr)) return false;
                return true;
            }
            // Pappelreihen an Land- und Hauptstrassen, mit Luecken
            foreach (var rd in roads)
            {
                if (rd.kind != Roads.Haupt && rd.kind != Roads.Neben) continue;
                float acc = 0f, next = 0f;
                for (int i = 1; i < rd.p.Count; i++)
                {
                    acc += Vector2.Distance(rd.p[i - 1], rd.p[i]);
                    if (acc < next) continue;
                    next = acc + 14f;
                    Vector2 p = rd.p[i];
                    if (n.Get(p.x / 300f + 4f, p.y / 300f) < 0.56f) continue;
                    if (Vector2.Distance(p, C) < RR + 60f) continue;
                    if (p.x > BandX0 - 40f && p.x < BandX1 + 40f) continue;
                    Vector2 dir = (rd.p[Mathf.Min(i + 1, rd.p.Count - 1)] - rd.p[i - 1]).normalized, side = new Vector2(-dir.y, dir.x);
                    float off = Roads.HalfWidth[rd.kind] + 4.5f;
                    foreach (float sg in new[] { -1f, 1f })
                    {
                        Vector2 q = p + side * sg * off;
                        if (Clear(q, 2f) && RoadE(q.x, q.y) > 2.5f) list.Add(T(q, Forest.Pappel, 0.65f + (float)r.NextDouble() * 0.25f));
                    }
                }
            }
            // Pappeln auf den Deichen, Weiden an den Baechen und am Stadtgraben
            foreach (var w in waters)
            {
                if (w.kind == WGraben) continue;
                float acc = 0f, next = 0f;
                for (int i = 1; i < w.p.Count; i++)
                {
                    acc += Vector2.Distance(w.p[i - 1], w.p[i]);
                    if (acc < next) continue;
                    Vector2 p = w.p[i];
                    Vector2 dir = (w.p[Mathf.Min(i + 1, w.p.Count - 1)] - w.p[i - 1]).normalized, side = new Vector2(-dir.y, dir.x);
                    if (w.kind == WKanal)
                    {
                        next = acc + 12f;
                        foreach (float sg in new[] { -1f, 1f }) { Vector2 q = p + side * sg * 24f; if (Clear(q, 2f) && RoadE(q.x, q.y) > 2f && n.Get(q.x / 250f, q.y / 250f + 5f) > 0.4f) list.Add(T(q, Forest.Pappel, 0.7f + (float)r.NextDouble() * 0.25f)); }
                    }
                    else
                    {
                        next = acc + (float)(8 + r.NextDouble() * 18);
                        if (p.x > BandX0 - 40f && p.x < BandX1 + 40f) continue;
                        float off = w.HW + 3f + (float)r.NextDouble() * 2f;
                        Vector2 q = p + side * (r.NextDouble() < 0.5 ? -off : off);
                        if (Clear(q, 2f)) list.Add(T(q, w.kind == WGracht && r.NextDouble() < 0.5 ? Forest.Buche : Forest.Weide, 0.7f + (float)r.NextDouble() * 0.4f));
                    }
                }
            }
            // Baeume auf dem Stadtwall und in den Gaerten
            for (int k = 0; k < 2600; k++)
            {
                float a = (float)(r.NextDouble() * Mathf.PI * 2), d = (float)(RR - 30f - r.NextDouble() * 360f);
                Vector2 p = C + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * d;
                bool wall = d > RR - 40f;
                if (!wall && r.NextDouble() < 0.7) continue;
                if (OnSquare(p, 4f) || !Clear(p, 3f)) continue;
                list.Add(T(p, wall ? Forest.Buche : (r.NextDouble() < 0.5 ? Forest.Eiche : Forest.Birke), wall ? 0.8f + (float)r.NextDouble() * 0.3f : 0.55f + (float)r.NextDouble() * 0.3f));
            }
            // Doerfer und Hoefe: Obstwiesen und Hofbaeume
            foreach (var v in villages)
                for (int k = 0; k < 70; k++)
                {
                    Vector2 p = v.c + new Vector2((float)(r.NextDouble() - 0.5), (float)(r.NextDouble() - 0.5)) * v.r * 2.3f;
                    if (Clear(p, 3f)) list.Add(T(p, r.NextDouble() < 0.6 ? Forest.Eiche : Forest.Buche, 0.45f + (float)r.NextDouble() * 0.35f));
                }
            foreach (var f in farms)
                for (int k = 0; k < 10; k++)
                {
                    Vector2 p = f + new Vector2((float)(r.NextDouble() - 0.5), (float)(r.NextDouble() - 0.5)) * 90f;
                    if (Clear(p, 3f)) list.Add(T(p, r.NextDouble() < 0.5 ? Forest.Eiche : Forest.Weide, 0.5f + (float)r.NextDouble() * 0.4f));
                }
            // kleine Waelder
            foreach (var w in woods)
                for (float z = w.y - 140f; z < w.y + 140f; z += 9f)
                    for (float x = w.x - 140f; x < w.x + 140f; x += 9f)
                    {
                        float px = x + (float)(r.NextDouble() - 0.5) * 8f, pz = z + (float)(r.NextDouble() - 0.5) * 8f;
                        if (SurfAt(px, pz) != SWald || r.NextDouble() > 0.85) continue;
                        double k = r.NextDouble();
                        bool edge = SurfAt(px + 12f, pz) != SWald || SurfAt(px - 12f, pz) != SWald || SurfAt(px, pz + 12f) != SWald || SurfAt(px, pz - 12f) != SWald;
                        list.Add(T(new Vector2(px, pz), Forest.MixedBroadleaf(k, edge), 0.75f + (float)r.NextDouble() * 0.45f));
                    }
        }

        // ---------------- Kleinkram und Schilder (alte Holzschilder, ohne moderne Verkehrszeichen) ----------------
        void BuildDetails(Transform root)
        {
            var c = new MapDetails.Context
            {
                half = Half, seed = 1918, signStyle = 2, modern = false, roads = roads, houses = houses, farms = farms, groundY = GroundY,
                free = (x, z) => { byte s = SurfAt(x, z); return s != SWasser && s != SWald && s != SWeg && s != SStadt && RoadE(x, z) > 1.5f && WaterE(x, z) > 3f; },
                meadow = (x, z) => { byte s = SurfAt(x, z); return s == SWiese || s == SWeide; },
                forest = (x, z) => SurfAt(x, z) == SWald,
            };
            c.towns.Add(new MapDetails.Town { name = "Sint-Joris", c = C, r = RR + 75f });
            foreach (var v in villages) c.towns.Add(new MapDetails.Town { name = v.name, c = v.c, r = v.r + 20f });
            c.destinations.Add(("Kust", new Vector2(-6000f, 12000f)));
            c.destinations.Add(("Rijsel", new Vector2(2000f, -14000f)));
            MapDetails.Decorate(root, c);
        }

        // ---------------- Startpunkte ----------------
        void MakeSpawns()
        {
            var want = new List<(string, Vector2)>
            {
                ("Kanal West", new Vector2(-2500f, 300f)),
                ("Hooghem", villages[0].c), ("Langemeersch", villages[1].c), ("Polderkerke", villages[2].c),
                ("Sint-Joris (Markt)", C + new Vector2(-30f, 0f)),
                ("Niemandsland", new Vector2((BandX0 + BandX1) / 2f, -400f)),
                ("Kortewaal", villages[3].c), ("Brielendam", villages[4].c),
                ("Hoehenruecken Ost", new Vector2(2600f, 0f)),
            };
            foreach (var (nm, p) in want)
            {
                Vector2 best = p; bool ok = false;
                for (float rr = 0f; rr < 500f && !ok; rr += 10f)
                    for (int k = 0; k < 24 && !ok; k++)
                    {
                        float ang = k * Mathf.PI * 2f / 24f;
                        Vector2 q = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
                        if (CanSpawn(q.x, q.y)) { best = q; ok = true; }
                    }
                if (!ok) continue;
                Vector2 to = C - best;
                if (to.sqrMagnitude < 1f) to = Vector2.right;
                spawns.Add(new SpawnPoint(nm, best.x, best.y, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg));
            }
        }

        public override List<Spawn> Targets()
        {
            var t = new List<Spawn>();
            var kinds = new[] { TankLibrary.Sherman, TankLibrary.T34_85, TankLibrary.PanzerIV, TankLibrary.Tiger };
            for (int i = 1; i < spawns.Count && t.Count < 7; i++)
            {
                var s = spawns[i];
                Vector2 p = s.xz + new Vector2(22f, 14f);
                if (!CanSpawn(p.x, p.y)) p = s.xz + new Vector2(-22f, -14f);
                if (!CanSpawn(p.x, p.y)) continue;
                t.Add(new Spawn(kinds[t.Count % kinds.Length], p.x, p.y, s.yaw + 180f));
            }
            return t;
        }

        // ---------------- Karte ----------------
        void BuildOverview()
        {
            var dots = new List<Vector3>();
            foreach (var h in houses) dots.Add(new Vector3((h.x + Half) / Size, (h.y + Half) / Size, h.z));
            overview = TerrainKit.Overview("Flandern Karte", H, G, id =>
            {
                switch (surf[id])
                {
                    case SWeizen: return new Color(0.84f, 0.76f, 0.46f);
                    case SAcker: return new Color(0.62f, 0.52f, 0.38f);
                    case SWeg: return new Color(0.94f, 0.90f, 0.80f);
                    case SWeide: return new Color(0.44f, 0.58f, 0.32f);
                    case SStadt: return new Color(0.70f, 0.64f, 0.56f);
                    case SWasser: return new Color(0.36f, 0.56f, 0.78f);
                    case SSchlamm: return new Color(0.52f, 0.46f, 0.38f);
                    case SGarten: return new Color(0.58f, 0.66f, 0.44f);
                    case SWald: return new Color(0.22f, 0.36f, 0.20f);
                    case SDeich: return new Color(0.50f, 0.62f, 0.38f);
                    default: return new Color(0.54f, 0.66f, 0.38f);
                }
            }, 0f, 0.25f, dots);
        }
    }
}
