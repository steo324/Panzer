using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Talland: 8 x 8 km Gebirge (Art Schweizer Voralpen). Ein langes Trogtal von West nach Ost mit flachem Talboden,
    /// maeandrierendem Fluss, Landstrasse und Doerfern; ein Seitental nach Norden (mit Bach) und eines nach Sueden.
    /// Die Haenge sind bewaldet (unten Buchen, hoeher Fichten und Kiefern), darueber Almen, oben Fels und Geroell.
    /// Rundum eine Bergkulisse ohne Kollision. Alles aus fester Zufallszahl (bei jedem Spieler gleich).
    /// </summary>
    public class TallandMap : GameMap
    {
        public override string Name => "Talland";
        public override string Description => "8 x 8 km Gebirge: ein langes Tal zwischen hohen Bergen mit Fluss, Landstrasse, Doerfern und Bruecken, dazu zwei Seitentaeler. Bewaldete Haenge, Almen und Fels oben. Kaempfe im Talboden, Ueberblick von den Haengen.";
        public override bool Fog => true;
        public override float FogStart => 1500f;
        public override float FogEnd => 18000f;
        public override bool SpawnSelect => true;
        public override float Extent => Half;
        public override Vector2 PlayerXZ => spawns.Count > 0 ? spawns[0].xz : new Vector2(-3500f, 0f);
        public override float PlayerYaw => spawns.Count > 0 ? spawns[0].yaw : 90f;
        public override List<SpawnPoint> SpawnPoints => spawns;
        public override Texture2D Overview => overview;

        public const float Size = 8000f, Half = Size / 2f, MaxH = 1800f;
        const int Tiles = 4, TileRes = 513;
        const int G = (TileRes - 1) * Tiles + 1;   // 2049
        const float Cell = Size / (G - 1);         // 3,906 m
        const byte SWiese = 0, SAlm = 1, SWeg = 2, SWald = 3, SDorf = 4, SFels = 5, SWasser = 6, SKies = 7;

        float[] H; byte[] surf;
        Roads.Field roadF, riverF;
        Noise2D n;
        Texture2D overview;
        readonly List<SpawnPoint> spawns = new List<SpawnPoint>();
        readonly List<Vector3> houses = new List<Vector3>();          // x, z, Radius
        readonly List<Roads.Road> roads = new List<Roads.Road>();
        readonly List<Roads.Road> rivers = new List<Roads.Road>();
        readonly List<(string name, Vector2 c)> places = new List<(string, Vector2)>();
        readonly List<Vector2> farms = new List<Vector2>();
        struct BridgeInfo { public Vector2 a, b; public float top, width; }
        readonly List<BridgeInfo> bridges = new List<BridgeInfo>();
        struct Village { public string name; public Vector2 c; public float r; }
        readonly List<Village> villages = new List<Village>();

        // ================= Taeler (alles als Formeln, damit auch die Kulisse ausserhalb passt) =================
        static float Vz(float x) => 260f * Mathf.Sin(x / 1900f + 0.5f) + 110f * Mathf.Sin(x / 730f + 1.3f);
        static float VzD(float x) => 260f / 1900f * Mathf.Cos(x / 1900f + 0.5f) + 110f / 730f * Mathf.Cos(x / 730f + 1.3f);
        static float Fmain(float x) => 230f + Mathf.Clamp(x + 4000f, -2000f, 10000f) * 0.018f;
        static float Wmain(float x) => 380f + 110f * Mathf.Sin(x / 1500f + 2f);
        const float NJx = 1300f, SJx = -1700f;
        static readonly float NJz = Vz(NJx), SJz = Vz(SJx);
        static float Nx(float z) { float t = Mathf.Max(0f, z - NJz); return NJx + t * 0.28f + 180f * Mathf.Sin(t / 900f) * Mathf.SmoothStep(0f, 1f, t / 600f); }
        static float Sx(float z) { float t = Mathf.Max(0f, SJz - z); return SJx - t * 0.2f + 160f * Mathf.Sin(t / 800f) * Mathf.SmoothStep(0f, 1f, t / 600f); }
        static float Nlen => 4400f - NJz;
        static float Slen => SJz + 4400f;
        static float FN(float z) => Fmain(NJx) + 400f * Mathf.Pow(Mathf.Clamp01((z - NJz) / Nlen), 1.25f) + Mathf.Max(0f, z - 4400f) * 0.12f;
        static float FS(float z) => Fmain(SJx) + 320f * Mathf.Pow(Mathf.Clamp01((SJz - z) / Slen), 1.25f) + Mathf.Max(0f, -4400f - z) * 0.12f;
        static float WN(float z) => Mathf.Lerp(210f, 110f, Mathf.Clamp01((z - NJz) / Nlen));
        static float WS(float z) => Mathf.Lerp(200f, 120f, Mathf.Clamp01((SJz - z) / Slen));

        /// <summary>Talboden-Hoehe und Abstand vom Talboden (0 = im Tal).</summary>
        void Valley(float x, float z, out float floor, out float d)
        {
            float cm = 1f / Mathf.Sqrt(1f + VzD(x) * VzD(x));
            float dm = Mathf.Max(0f, Mathf.Abs(z - Vz(x)) * cm - Wmain(x));
            float fm = Fmain(x);
            float dn = 1e9f, fn = 0f, ds = 1e9f, fs = 0f;
            if (z > NJz - 50f) { dn = Mathf.Max(0f, Mathf.Abs(x - Nx(z)) * 0.96f - WN(z)); fn = FN(z); }
            if (z < SJz + 50f) { ds = Mathf.Max(0f, Mathf.Abs(x - Sx(z)) * 0.98f - WS(z)); fs = FS(z); }
            d = Mathf.Min(dm, Mathf.Min(dn, ds));
            float wm = 1f / ((dm + 40f) * (dm + 40f)), wn = dn < 1e8f ? 1f / ((dn + 40f) * (dn + 40f)) : 0f, ws = ds < 1e8f ? 1f / ((ds + 40f) * (ds + 40f)) : 0f;
            floor = (fm * wm + fn * wn + fs * ws) / (wm + wn + ws);
        }

        float RawHeight(float x, float z)
        {
            Valley(x, z, out float floor, out float d);
            float dd = Mathf.Sqrt(d * d + 70f * 70f) - 70f;   // runder Uebergang Talboden -> Hang
            float u = Mathf.Clamp01(dd / 2300f);
            float s = 1f - Mathf.Pow(1f - u, 1.65f);
            float ridge = n.Ridge(x / 2300f + 4f, z / 2300f + 9f, 4);
            float mrel = 700f + 560f * ridge + (n.Fbm(x / 650f + 2f, z / 650f + 5f, 3) - 0.5f) * 180f;
            float detail = (n.Fbm(x / 170f + 13f, z / 170f + 3f, 3) - 0.5f) * 46f * s;
            float floorNoise = (n.Get(x / 260f + 7f, z / 260f + 1f) - 0.5f) * 3.5f * (1f - s);
            return floor + mrel * s + detail + floorNoise;
        }

        // ================= Abfragen =================
        static int Idx(int ix, int iz) => iz * G + ix;
        static int Gi(float x) => Mathf.Clamp(Mathf.RoundToInt((x + Half) / Cell), 0, G - 1);

        public override float GroundY(float x, float z) => H == null ? base.GroundY(x, z) : TerrainKit.Sample(H, G, Cell, Half, x, z);
        byte SurfAt(float x, float z) => surf == null ? SWiese : surf[Idx(Gi(x), Gi(z))];
        float RoadE(float x, float z) => roadF == null ? 99f : roadF.edge[Idx(Gi(x), Gi(z))];
        float RiverE(float x, float z) => riverF == null ? 99f : riverF.edge[Idx(Gi(x), Gi(z))];

        public override int SurfaceAt(float x, float z)
        {
            switch (SurfAt(x, z))
            {
                case SWeg: return roadF.KindAt(x, z) <= Roads.Neben ? GroundTracks.Hart : GroundTracks.Weg;
                case SFels: return GroundTracks.Hart;
                case SKies: case SWasser: return GroundTracks.Weg;
                default: return GroundTracks.Wiese;
            }
        }

        public override bool CanSpawn(float x, float z)
        {
            if (Mathf.Abs(x) > Half - 60f || Mathf.Abs(z) > Half - 60f) return false;
            byte s = SurfAt(x, z);
            if (s == SWald || s == SFels || s == SWasser || s == SKies) return false;
            if (RiverE(x, z) < 12f || RoadE(x, z) < 6f) return false;
            foreach (var h in houses) if ((x - h.x) * (x - h.x) + (z - h.y) * (z - h.y) < (h.z + 8f) * (h.z + 8f)) return false;
            float sl = Mathf.Abs(GroundY(x + 4f, z) - GroundY(x - 4f, z)) + Mathf.Abs(GroundY(x, z + 4f) - GroundY(x, z - 4f));
            return sl < 3.5f;
        }

        public override IEnumerable<(string name, Vector2 c)> Places() => places;

        // ================= Bauen =================
        public override void Build(Transform root)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            n = new Noise2D(4711);
            houses.Clear(); roads.Clear(); rivers.Clear(); places.Clear(); farms.Clear(); bridges.Clear(); villages.Clear(); spawns.Clear();
            PlanRivers();
            PlanRoads();
            BuildHeights();
            BuildSurfaces();
            float tGen = sw.ElapsedMilliseconds;
            var layers = TerrainKit.Layers(true, out var tm);
            TerrainKit.Build(root, H, G, Size, MaxH, Tiles, 11, layers, tm, Weights, 6f);
            BuildBackdrop(root, layers, tm);
            var kit = new MeshKit();
            BuildVillages(kit);
            BuildFarms(kit);
            BuildBridges(kit);
            BuildRocks(kit);
            int tris = kit.Build(root, "Gebaeude");
            Roads.BuildMeshes(root, roads, RoadSurfaceY);
            BuildWater(root);
            var trees = PlaceTrees();
            Forest.Create(root, trees);
            MakeSpawns();
            if (!NetSync.Headless)
            {
                BuildOverview();
                WheatField.Create(root, (x, z) => false, GroundY, (x, z) => false, GrassGrows);
            }
            Debug.Log($"[Talland] gebaut in {sw.ElapsedMilliseconds} ms (Gelaende-Rechnung {tGen} ms), {houses.Count} Gebaeude ({tris} Dreiecke), {roads.Count} Strassen, {bridges.Count} Bruecken, {trees.Count} Baeume, {spawns.Count} Startpunkte");
        }

        // ---------------- Fluss und Bach ----------------
        void PlanRivers()
        {
            var p = new List<Vector2>();
            for (float x = -4100f; x <= 4100f; x += 40f)
            {
                float w = Wmain(x);
                float z = Vz(x) + w * (0.36f * Mathf.Sin(x / 560f + 0.7f) + 0.08f * Mathf.Sin(x / 210f + 2f));
                p.Add(new Vector2(x, z));
            }
            rivers.Add(new Roads.Road(Roads.Feldweg, Roads.Smooth(p, 6f)) { hw = 8f, ditch = false });
            // Bach aus dem Nordtal bis in den Fluss
            p = new List<Vector2>();
            for (float z = 4100f; z > NJz + 80f; z -= 40f) p.Add(new Vector2(Nx(z) + WN(z) * 0.32f * Mathf.Sin(z / 380f), z));
            // ueber den Talboden zum Fluss
            float xr = Nx(NJz + 80f) + 60f;
            p.Add(new Vector2(xr, NJz + 40f));
            p.Add(new Vector2(xr + 20f, RiverZ(xr + 20f)));
            rivers.Add(new Roads.Road(Roads.Feldweg, Roads.Smooth(p, 6f)) { hw = 3f, ditch = true });   // ditch = Bach (flacheres Bett)
        }

        float RiverZ(float x)
        {
            var p = rivers[0].p;
            for (int i = 0; i + 1 < p.Count; i++)
                if (p[i].x <= x && p[i + 1].x >= x) return Mathf.Lerp(p[i].y, p[i + 1].y, (x - p[i].x) / Mathf.Max(0.01f, p[i + 1].x - p[i].x));
            return Vz(x);
        }

        // ---------------- Strassen, Doerfer, Hoefe ----------------
        void PlanRoads()
        {
            var rng = new System.Random(812);
            // Hauptstrasse am Suedrand des Talbodens
            var p = new List<Vector2>();
            for (float x = -4100f; x <= 4100f; x += 80f) p.Add(new Vector2(x, Vz(x) - Wmain(x) * 0.64f + (n.Get(x / 400f, 3f) - 0.5f) * 30f));
            var main = new Roads.Road(Roads.Haupt, Roads.Smooth(p, 4f));
            roads.Add(main);
            // Nebenstrasse am Nordrand (West bis zum Nordtal)
            p = new List<Vector2>();
            for (float x = -4100f; x <= NJx - 150f; x += 80f) p.Add(new Vector2(x, Vz(x) + Wmain(x) * 0.66f + (n.Get(x / 380f, 9f) - 0.5f) * 28f));
            var north = new Roads.Road(Roads.Neben, Roads.Smooth(p, 4f));
            roads.Add(north);
            // Strasse ins Nordtal: von der Hauptstrasse ueber den Fluss, dann am Westhang des Seitentals hinauf
            p = new List<Vector2>();
            Vector2 j1 = Nearest(main.p, new Vector2(NJx - 300f, Vz(NJx - 300f)));
            p.Add(j1);
            p.Add(new Vector2(j1.x + 60f, Vz(j1.x + 60f)));
            p.Add(north.p[north.p.Count - 1] + new Vector2(60f, 30f));
            for (float z = NJz + 260f; z <= 4100f; z += 90f) p.Add(new Vector2(Nx(z) - WN(z) * 0.5f, z));
            roads.Add(new Roads.Road(Roads.Neben, Roads.Smooth(p, 4f)));
            // Strasse ins Suedtal (bleibt suedlich vom Fluss)
            p = new List<Vector2>();
            Vector2 j2 = Nearest(main.p, new Vector2(SJx + 150f, Vz(SJx + 150f)));
            p.Add(j2);
            for (float z = j2.y - 150f; z >= -4100f; z -= 90f) p.Add(new Vector2(Sx(z) + WS(z) * 0.45f, z));
            roads.Add(new Roads.Road(Roads.Neben, Roads.Smooth(p, 4f)));
            // Querstrasse im Westen ueber den Fluss
            p = new List<Vector2>();
            Vector2 a = Nearest(main.p, new Vector2(-2900f, 0f)), b = Nearest(north.p, new Vector2(-2750f, Vz(-2750f)));
            p.Add(a); p.Add(Vector2.Lerp(a, b, 0.5f) + new Vector2(40f, 0f)); p.Add(b);
            roads.Add(new Roads.Road(Roads.Neben, Roads.Smooth(p, 4f)));

            // Doerfer an den Strassen
            void AddVillage(string nm, Vector2 c, float r) { villages.Add(new Village { name = nm, c = c, r = r }); places.Add((nm, c)); }
            AddVillage("Unterwald", Nearest(main.p, new Vector2(-3100f, 0f)) + new Vector2(0f, -10f), 190f);
            AddVillage("Brunnen", Nearest(main.p, new Vector2(-800f, 0f)) + new Vector2(0f, -10f), 210f);
            AddVillage("Matt", Nearest(north.p, new Vector2(-1900f, 0f)) + new Vector2(0f, 15f), 160f);
            AddVillage("Oberried", Nearest(main.p, new Vector2(2300f, 0f)) + new Vector2(0f, -10f), 200f);
            AddVillage("Hinterbach", new Vector2(Nx(NJz + 1900f) - WN(NJz + 1900f) * 0.35f, NJz + 1900f), 140f);
            AddVillage("Sankt Anton", new Vector2(Sx(SJz - 1500f) + WS(SJz - 1500f) * 0.3f, SJz - 1500f), 150f);

            // Hoefe auf dem Talboden und an den unteren Haengen, mit Feldweg zur naechsten Strasse
            int made = 0;
            for (int tries = 0; tries < 600 && made < 26; tries++)
            {
                float x = (float)(rng.NextDouble() * 7400 - 3700), z;
                double kindRoll = rng.NextDouble();
                if (kindRoll < 0.7) z = Vz(x) + (float)(rng.NextDouble() * 2 - 1) * Wmain(x) * 1.15f;
                else if (kindRoll < 0.85) { z = (float)(NJz + 300 + rng.NextDouble() * 3000); x = Nx(z) + (float)(rng.NextDouble() * 2 - 1) * WN(z) * 1.1f; }
                else { z = (float)(SJz - 300 - rng.NextDouble() * 3000); x = Sx(z) + (float)(rng.NextDouble() * 2 - 1) * WS(z) * 1.1f; }
                var hp = new Vector2(x, z);
                bool bad = Mathf.Abs(x) > Half - 150f || Mathf.Abs(z) > Half - 150f;
                foreach (var v in villages) if (Vector2.Distance(v.c, hp) < v.r + 220f) bad = true;
                foreach (var f in farms) if (Vector2.Distance(f, hp) < 260f) bad = true;
                foreach (var rv in rivers) if (DistTo(rv.p, hp) < 70f) bad = true;
                float dr = 1e9f; Vector2 near = hp;
                foreach (var r in roads) { var q = Nearest(r.p, hp); float d = Vector2.Distance(q, hp); if (d < dr) { dr = d; near = q; } }
                if (dr < 45f || dr > 700f) bad = true;
                if (bad) continue;
                // Weg darf keinen Fluss queren
                bool crosses = false;
                foreach (var rv in rivers) if (Crosses(rv.p, hp, near)) crosses = true;
                if (crosses) continue;
                farms.Add(hp);
                var w = new List<Vector2> { near, Vector2.Lerp(near, hp, 0.5f) + Perp(hp - near) * (float)(rng.NextDouble() - 0.5) * 60f, hp };
                roads.Add(new Roads.Road(Roads.Feldweg, Roads.Smooth(w, 4f)));
                made++;
            }
            // Alpwege: Serpentinen von einigen Doerfern den Hang hinauf
            foreach (var (vi, up) in new[] { (0, -1f), (1, -1f), (2, 1f), (3, -1f) })
            {
                var v = villages[vi];
                var pts = new List<Vector2> { v.c };
                Vector2 cur = v.c + new Vector2(0f, up * (v.r + 60f));
                pts.Add(cur);
                float side = 1f;
                for (int leg = 0; leg < 5; leg++)
                {
                    cur += new Vector2(side * 260f, up * 70f);
                    pts.Add(cur); side = -side;
                }
                roads.Add(new Roads.Road(Roads.Feldweg, Roads.Smooth(pts, 4f)));
            }
        }

        static Vector2 Perp(Vector2 d) { d.Normalize(); return new Vector2(-d.y, d.x); }
        static Vector2 Nearest(List<Vector2> p, Vector2 q)
        {
            float best = 1e18f; Vector2 r = p[0];
            foreach (var a in p) { float d = (a - q).sqrMagnitude; if (d < best) { best = d; r = a; } }
            return r;
        }
        static float DistTo(List<Vector2> p, Vector2 q) => Vector2.Distance(Nearest(p, q), q);
        static bool SegX(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out float t)
        {
            t = 0f;
            Vector2 r = b - a, s = d - c;
            float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) < 1e-6f) return false;
            float tt = ((c.x - a.x) * s.y - (c.y - a.y) * s.x) / den, uu = ((c.x - a.x) * r.y - (c.y - a.y) * r.x) / den;
            t = tt;
            return tt >= 0f && tt <= 1f && uu >= 0f && uu <= 1f;
        }
        static bool Crosses(List<Vector2> line, Vector2 a, Vector2 b)
        {
            for (int i = 0; i + 1 < line.Count; i++) if (SegX(a, b, line[i], line[i + 1], out _)) return true;
            return false;
        }

        // ---------------- Hoehen ----------------
        void BuildHeights()
        {
            H = new float[G * G];
            Parallel.For(0, G, iz =>
            {
                float wz = iz * Cell - Half;
                for (int ix = 0; ix < G; ix++) H[Idx(ix, iz)] = RawHeight(ix * Cell - Half, wz);
            });
            // Doerfer und Hoefe leicht einebnen
            var flat = new List<Vector3>();
            foreach (var v in villages) flat.Add(new Vector3(v.c.x, v.c.y, v.r + 60f));
            foreach (var f in farms) flat.Add(new Vector3(f.x, f.y, 55f));
            var fh = new float[flat.Count];
            for (int i = 0; i < flat.Count; i++) fh[i] = GroundY(flat[i].x, flat[i].y);
            for (int i = 0; i < flat.Count; i++)
            {
                var f = flat[i];
                int x0 = Gi(f.x - f.z), x1 = Gi(f.x + f.z), z0 = Gi(f.y - f.z), z1 = Gi(f.y + f.z);
                for (int iz = z0; iz <= z1; iz++)
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        float d = Vector2.Distance(new Vector2(ix * Cell - Half, iz * Cell - Half), new Vector2(f.x, f.y));
                        if (d > f.z) continue;
                        float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(f.z * 0.55f, f.z, d));
                        int id = Idx(ix, iz);
                        H[id] = Mathf.Lerp(fh[i] + (H[id] - fh[i]) * 0.25f, H[id], t);
                    }
            }
            // Strassen: geglaettete Laengsneigung, Damm/Einschnitt, Graeben
            foreach (var r in roads) Roads.SetHeights(r, GroundY, r.kind == Roads.Feldweg ? 6 : 12);
            roadF = Roads.Stamp(roads, G, Cell, -Half, 30f);
            var copy = (float[])H.Clone();
            Parallel.For(0, G * G, id => { H[id] = Roads.Apply(roadF, id, copy[id], 16f); });
            // Fluss und Bach: Bett eingraben (Wasserspiegel entlang des Laufs geglaettet)
            foreach (var r in rivers) Roads.SetHeights(r, GroundY, 25);
            // Wasser fliesst bergab: Hoehe entlang des Laufs nie steigen lassen (Fluss nach Westen, Bach nach Sueden)
            var rp = rivers[0].h; for (int i = 1; i < rp.Length; i++) rp[i] = Mathf.Max(rp[i], rp[i - 1]);
            var bp = rivers[1].h; for (int i = 1; i < bp.Length; i++) bp[i] = Mathf.Min(bp[i], bp[i - 1]);
            riverF = Roads.Stamp(rivers, G, Cell, -Half, 16f);
            Parallel.For(0, G * G, id =>
            {
                float e = riverF.edge[id];
                if (riverF.kind[id] == 255 || e > 15f) return;
                float lvl = riverF.h[id];
                bool brook = riverF.ditch[id] != 0;      // beim Bach ist das Graben-Merkmal gesetzt
                float D = brook ? 1.2f : 2.4f, bank = brook ? 4f : 7f;
                float bed;
                if (e < 0f) bed = lvl - D * (0.5f + 0.5f * Mathf.Sqrt(Mathf.Clamp01(-e / 4f)));
                else if (e <= bank) bed = lvl - D * 0.5f * (1f - e / bank) - 0.2f;
                else { H[id] = Mathf.Lerp(lvl - 0.2f, H[id], Mathf.SmoothStep(0f, 1f, (e - bank) / 8f)); return; }
                H[id] = bed;   // Ufer genau festlegen: Wasser liegt nie ueber dem Land
            });
            for (int i = 0; i < H.Length; i++) H[i] = Mathf.Clamp(H[i], 1f, MaxH - 2f);
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
                    if (riverF.edge[id] < 0.5f) { surf[id] = SWasser; continue; }
                    if (riverF.edge[id] < 3.5f) { surf[id] = SKies; continue; }
                    float re = roadF.edge[id];
                    if (re < 0.3f) { surf[id] = SWeg; continue; }
                    int ax = Mathf.Min(ix + 1, G - 1), bx = Mathf.Max(ix - 1, 0), az = Mathf.Min(iz + 1, G - 1), bz = Mathf.Max(iz - 1, 0);
                    float slope = new Vector2((H[Idx(ax, iz)] - H[Idx(bx, iz)]) / ((ax - bx) * Cell), (H[Idx(ix, az)] - H[Idx(ix, bz)]) / ((az - bz) * Cell)).magnitude;
                    float h = H[id];
                    float treeline = 1380f + (n.Get(wx / 700f + 3f, wz / 700f + 8f) - 0.5f) * 260f;
                    float rockNoise = (n.Get(wx / 90f + 1f, wz / 90f + 2f) - 0.5f) * 0.25f;
                    if (slope > 1.05f + rockNoise || h > treeline + 260f + rockNoise * 300f || (h > treeline + 80f && slope > 0.6f + rockNoise)) { surf[id] = SFels; continue; }
                    float dv = 1e9f; foreach (var v in villages) dv = Mathf.Min(dv, Vector2.Distance(new Vector2(wx, wz), v.c) - v.r);
                    if (dv < 0f) { surf[id] = SDorf; continue; }
                    if (h > treeline) { surf[id] = SAlm; continue; }
                    float df = 1e9f; foreach (var f in farms) df = Mathf.Min(df, Vector2.Distance(new Vector2(wx, wz), f));
                    // Wald an den Haengen, flecken- und streifenweise (Lawinenzuege bleiben frei)
                    float f1 = n.Fbm(wx / 600f + 21f, wz / 600f + 13f, 4);
                    float strip = n.Get(wx / 140f + 5f, wz / 900f + 2f);
                    float edgeN = (n.Get(wx / 40f, wz / 40f) - 0.5f) * 0.06f;
                    float want = Mathf.InverseLerp(0.06f, 0.22f, slope);   // auf dem Talboden kaum Wald
                    float nearLine = Mathf.InverseLerp(treeline, treeline - 140f, h);
                    if (f1 * want * nearLine + edgeN > 0.33f && strip > 0.22f && re > 7f && dv > 70f && df > 80f) { surf[id] = SWald; continue; }
                    surf[id] = SWiese;
                }
            });
        }

        void Weights(int gx, int gz, float[] w)
        {
            int id = Idx(gx, gz);
            float wx = gx * Cell - Half, wz = gz * Cell - Half;
            switch (surf[id])
            {
                case SWeg: w[2] = 1f; break;
                case SWasser: w[1] = 0.6f; w[2] = 0.4f; break;
                case SKies: w[1] = 0.7f; w[2] = 0.3f; break;
                case SFels: { float k = n.Get(wx / 25f, wz / 25f); w[1] = 0.75f + k * 0.25f; w[2] = 0.25f - k * 0.2f; w[3] = 0.05f; break; }
                case SWald: w[3] = 0.75f; w[2] = 0.25f; break;
                case SAlm: { float k = n.Get(wx / 30f + 4f, wz / 30f); w[0] = 0.75f; w[1] = 0.25f * k + 0.05f; w[3] = 0.15f; break; }
                case SDorf: { float b = Mathf.InverseLerp(0.55f, 0.8f, n.Get(wx * 0.05f, wz * 0.05f)) * 0.5f; w[0] = 1f - b; w[2] = b; break; }
                default:
                {
                    float v = n.Get(wx * 0.008f + 3f, wz * 0.008f + 7f) * 0.6f + n.Get(wx * 0.045f + 13f, wz * 0.045f + 5f) * 0.4f;
                    w[3] = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.66f, v)); w[0] = 1f - w[3];
                    break;
                }
            }
            // Bankett/Strassenrand: etwas Erde
            float re = roadF.edge[id];
            if (surf[id] != SWeg && re < 1.5f) { float t = Mathf.InverseLerp(0.3f, 1.5f, re); w[0] *= t; w[1] *= t; w[3] *= t; w[2] = Mathf.Max(w[2], 1f - t); }
            // steile Haenge: Geroell und Fels schimmern durch
            int x0 = Mathf.Max(gx - 1, 0), x1 = Mathf.Min(gx + 1, G - 1), z0 = Mathf.Max(gz - 1, 0), z1 = Mathf.Min(gz + 1, G - 1);
            float slope = new Vector2((H[Idx(x1, gz)] - H[Idx(x0, gz)]) / ((x1 - x0) * Cell), (H[Idx(gx, z1)] - H[Idx(gx, z0)]) / ((z1 - z0) * Cell)).magnitude;
            float steep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.0f, slope));
            if (steep > 0f && surf[id] != SFels) { float k = 1f - steep; w[0] *= k; w[2] *= k; w[3] *= k; w[1] = w[1] * k + steep; }
        }

        bool GrassGrows(float x, float z)
        {
            byte s = SurfAt(x, z);
            if (s != SWiese && s != SAlm && s != SDorf) return false;
            if (RoadE(x, z) < 1.2f || RiverE(x, z) < 4f) return false;
            if (s == SDorf) foreach (var h in houses) if ((x - h.x) * (x - h.x) + (z - h.y) * (z - h.y) < (h.z + 2f) * (h.z + 2f)) return false;
            return true;
        }

        // ---------------- Kulisse (Berge rundum, ohne Kollision) ----------------
        void BuildBackdrop(Transform root, TerrainLayer[] layers, Material tm)
        {
            if (NetSync.Headless || layers == null) return;
            const float US = 26000f; const int R = 513;
            float uh = US / 2f, uc = US / (R - 1);
            var td = new TerrainData { heightmapResolution = R };
            td.size = new Vector3(US, MaxH + 400f, US);
            var h = new float[R, R];
            var hs = new float[R * R];
            Parallel.For(0, R, iz =>
            {
                for (int ix = 0; ix < R; ix++)
                {
                    float wx = ix * uc - uh, wz = iz * uc - uh;
                    bool under = Mathf.Abs(wx) < Half - uc * 0.6f && Mathf.Abs(wz) < Half - uc * 0.6f;
                    float v = RawHeight(wx, wz) - 4f;
                    hs[iz * R + ix] = v;
                    h[iz, ix] = under ? 0f : Mathf.Clamp01(v / (MaxH + 400f));
                }
            });
            td.SetHeights(0, 0, h);
            td.terrainLayers = layers;
            const int ar = 512;
            td.alphamapResolution = ar;
            var a = new float[ar, ar, 4];
            Parallel.For(0, ar, iz =>
            {
                for (int ix = 0; ix < ar; ix++)
                {
                    int gx = Mathf.Clamp(ix * (R - 1) / (ar - 1), 1, R - 2), gz = Mathf.Clamp(iz * (R - 1) / (ar - 1), 1, R - 2);
                    float hh = hs[gz * R + gx];
                    float slope = new Vector2(hs[gz * R + gx + 1] - hs[gz * R + gx - 1], hs[(gz + 1) * R + gx] - hs[(gz - 1) * R + gx]).magnitude / (2f * uc);
                    float rock = Mathf.Clamp01(Mathf.InverseLerp(0.7f, 1.0f, slope) + Mathf.InverseLerp(1500f, 1650f, hh));
                    float forest = (1f - rock) * Mathf.InverseLerp(0.08f, 0.25f, slope) * Mathf.InverseLerp(1400f, 1250f, hh);
                    a[iz, ix, 1] = rock; a[iz, ix, 3] = forest; a[iz, ix, 0] = Mathf.Max(0f, 1f - rock - forest);
                }
            });
            td.SetAlphamaps(0, 0, a);
            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Kulisse";
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(-uh, 0f, -uh);
            var col = go.GetComponent<TerrainCollider>(); if (col != null) Object.Destroy(col);
            var t = go.GetComponent<Terrain>();
            t.heightmapPixelError = 10f; t.basemapDistance = 0f; t.castShadows = false;
            if (tm != null) t.materialTemplate = new Material(tm);
        }

        // ---------------- Gebaeude ----------------
        static readonly Color ChaletWood = new Color(0.55f, 0.38f, 0.24f), ChaletDark = new Color(0.40f, 0.27f, 0.17f);
        static readonly Color[] Plaster = { new Color(0.95f, 0.93f, 0.88f), new Color(0.92f, 0.88f, 0.80f), new Color(0.88f, 0.86f, 0.84f) };
        static readonly Color[] RoofCol = { new Color(0.30f, 0.28f, 0.27f), new Color(0.42f, 0.26f, 0.20f), new Color(0.36f, 0.34f, 0.33f) };

        bool HouseFree(float x, float z, float r)
        {
            if (RoadE(x, z) < r * 0.75f + 2.5f || RiverE(x, z) < r + 6f) return false;
            foreach (var h in houses) if ((x - h.x) * (x - h.x) + (z - h.y) * (z - h.y) < (r + h.z) * (r + h.z)) return false;
            return true;
        }

        void Chalet(MeshKit k, System.Random rng, Vector2 p, float yaw, bool big)
        {
            float w = big ? 12f : (float)(8 + rng.NextDouble() * 3), l = big ? 16f : (float)(9 + rng.NextDouble() * 4);
            GroundRange(p, yaw, w, l, out float lo, out float hi);
            bool wood = rng.NextDouble() < 0.65;
            k.House(p, yaw, w, l, big ? 6.5f : 5.6f, w * 0.26f, lo, hi, Plaster[rng.Next(Plaster.Length)], MeshKit.TexPlaster, RoofCol[rng.Next(RoofCol.Length)], MeshKit.TexRoof,
                    1.3f, 0, wood ? (Color?)(rng.NextDouble() < 0.5 ? ChaletWood : ChaletDark) : null, MeshKit.TexWood, 3f);
            houses.Add(new Vector3(p.x, p.y, Mathf.Max(w, l) * 0.62f));
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

        void BuildVillages(MeshKit k)
        {
            var rng = new System.Random(5150);
            foreach (var v in villages)
            {
                // Kirche (weiss, spitzer Turm) nahe der Mitte
                Vector2 cp = v.c + new Vector2(26f, 30f);
                for (int t = 0; t < 12 && !HouseFree(cp.x, cp.y, 18f); t++) cp = v.c + new Vector2((float)(rng.NextDouble() - 0.5) * 120f, (float)(rng.NextDouble() - 0.5) * 120f);
                if (HouseFree(cp.x, cp.y, 18f))
                {
                    float yaw = (float)rng.NextDouble() * 360f;
                    GroundRange(cp, yaw, 12f, 26f, out float lo, out float hi);
                    k.Church(cp, yaw, lo, hi, new Color(0.95f, 0.94f, 0.90f), MeshKit.TexPlaster, new Color(0.30f, 0.29f, 0.30f), new Color(0.25f, 0.30f, 0.28f), 22f, 11f, 24f, 18f);
                    houses.Add(new Vector3(cp.x, cp.y, 20f));
                }
                // Haeuser entlang der Strassen im Ort
                foreach (var r in roads)
                {
                    if (r.kind == Roads.Feldweg) continue;
                    for (int i = 2; i < r.p.Count; i += 4)
                    {
                        Vector2 p = r.p[i];
                        if (Vector2.Distance(p, v.c) > v.r) continue;
                        Vector2 dir = (r.p[i] - r.p[i - 2]).normalized, side = new Vector2(-dir.y, dir.x);
                        float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                        foreach (float sgn in new[] { 1f, -1f })
                        {
                            if (rng.NextDouble() < 0.45) continue;
                            Vector2 hp = p + side * sgn * (Roads.HalfWidth[r.kind] + 9f + (float)rng.NextDouble() * 6f);
                            if (!HouseFree(hp.x, hp.y, 8f)) continue;
                            Chalet(k, rng, hp, yaw + (rng.NextDouble() < 0.5 ? 0f : 90f), false);
                        }
                    }
                }
                // Hinterhaeuser und Stadel
                for (int j = 0; j < 14; j++)
                {
                    float a = (float)(rng.NextDouble() * Mathf.PI * 2), d = (float)(40 + rng.NextDouble() * (v.r - 30));
                    Vector2 hp = v.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                    if (!HouseFree(hp.x, hp.y, 10f) || SurfAt(hp.x, hp.y) == SFels) continue;
                    Chalet(k, rng, hp, (float)rng.NextDouble() * 360f, rng.NextDouble() < 0.3);
                }
            }
        }

        void BuildFarms(MeshKit k)
        {
            var rng = new System.Random(6160);
            foreach (var f in farms)
            {
                float yaw = (float)rng.NextDouble() * 360f;
                var q = Quaternion.Euler(0f, yaw, 0f);
                Chalet(k, rng, f, yaw, false);
                Vector3 o = q * new Vector3(17f, 0f, 3f);
                Vector2 bp = f + new Vector2(o.x, o.z);
                GroundRange(bp, yaw, 13f, 20f, out float lo, out float hi);
                k.House(bp, yaw, 13f, 20f, 6f, 3.6f, lo, hi, ChaletDark, MeshKit.TexWood, RoofCol[0], MeshKit.TexRoof, 1.2f);   // Stall/Heustadel
                houses.Add(new Vector3(bp.x, bp.y, 13f));
            }
            // einzelne Heustadel auf Wiesen und Almen
            int made = 0;
            for (int t = 0; t < 400 && made < 40; t++)
            {
                float x = (float)(rng.NextDouble() * 7600 - 3800), z = (float)(rng.NextDouble() * 7600 - 3800);
                byte s = SurfAt(x, z);
                if ((s != SWiese && s != SAlm) || !HouseFree(x, z, 6f) || !CanSpawn(x, z)) continue;
                float yaw = (float)rng.NextDouble() * 360f;
                GroundRange(new Vector2(x, z), yaw, 5f, 7f, out float lo, out float hi);
                k.House(new Vector2(x, z), yaw, 5f, 7f, 3.2f, 1.8f, lo, hi, ChaletDark, MeshKit.TexWood, RoofCol[2], MeshKit.TexRoof, 0.6f);
                houses.Add(new Vector3(x, z, 5f));
                made++;
            }
        }

        // ---------------- Bruecken ----------------
        void BuildBridges(MeshKit k)
        {
            foreach (var r in roads)
            {
                if (r.kind == Roads.Feldweg) continue;
                foreach (var rv in rivers)
                    for (int i = 0; i + 1 < r.p.Count; i++)
                    {
                        if (RiverE(r.p[i].x, r.p[i].y) > 40f && RiverE(r.p[i + 1].x, r.p[i + 1].y) > 40f) continue;
                        for (int j = 0; j + 1 < rv.p.Count; j++)
                        {
                            if (!SegX(r.p[i], r.p[i + 1], rv.p[j], rv.p[j + 1], out float t)) continue;
                            Vector2 c = Vector2.Lerp(r.p[i], r.p[i + 1], t);
                            Vector2 d = (r.p[Mathf.Min(i + 3, r.p.Count - 1)] - r.p[Mathf.Max(i - 2, 0)]).normalized;
                            float half = rv.HW + 7f;
                            float top = r.h != null ? Mathf.Lerp(r.h[i], r.h[i + 1], t) + 0.15f : GroundY(c.x, c.y);
                            var bi = new BridgeInfo { a = c - d * half, b = c + d * half, top = top, width = Roads.HalfWidth[r.kind] * 2f + 1.6f };
                            bridges.Add(bi);
                            k.Bridge(bi.a, bi.b, bi.width, top, GroundY(c.x, c.y), new Color(0.62f, 0.60f, 0.57f), new Color(0.55f, 0.54f, 0.52f));
                        }
                    }
            }
        }

        /// <summary>Hoehe fuer das Strassenband: auf Bruecken die Fahrbahnplatte, sonst das Gelaende.</summary>
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
            var mat = new Material(GameMap.Mat(new Color(0.20f, 0.32f, 0.33f)));
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.92f);
            var parent = new GameObject("Gewaesser").transform; parent.SetParent(root, false);
            foreach (var rv in rivers)
            {
                var lv = new float[rv.p.Count];
                for (int i = 0; i < lv.Length; i++) lv[i] = rv.h[i] - (rv.ditch ? 0.55f : 1.0f);
                TerrainKit.WaterRibbon(parent, rv.p, lv, rv.HW + 2.5f, mat);
            }
        }

        // ---------------- Felsbrocken ----------------
        void BuildRocks(MeshKit k)
        {
            var rng = new System.Random(7070);
            int made = 0;
            for (int t = 0; t < 30000 && made < 1600; t++)
            {
                float x = (float)(rng.NextDouble() * 7900 - 3950), z = (float)(rng.NextDouble() * 7900 - 3950);
                byte s = SurfAt(x, z);
                bool scree = s == SAlm && rng.NextDouble() < 0.15;
                if (s != SFels && !scree) continue;
                float sz = (float)(1.2 + Mathf.Pow((float)rng.NextDouble(), 2.5f) * 7.0);
                k.Rock(new Vector3(x, GroundY(x, z) - sz * 0.15f, z), new Vector3(sz, sz * (float)(0.5 + rng.NextDouble() * 0.4), sz * (float)(0.7 + rng.NextDouble() * 0.5)), rng, new Color(0.52f, 0.51f, 0.50f));
                made++;
            }
            // Bachbett: Steine
            foreach (var rv in rivers)
                for (int i = 0; i < rv.p.Count; i += 5)
                {
                    if (rng.NextDouble() > 0.35) continue;
                    Vector2 d = (rv.p[Mathf.Min(i + 1, rv.p.Count - 1)] - rv.p[Mathf.Max(i - 1, 0)]).normalized;
                    Vector2 p = rv.p[i] + new Vector2(-d.y, d.x) * (rv.HW + 1.5f) * (rng.NextDouble() < 0.5 ? -1f : 1f);
                    float sz = (float)(0.6 + rng.NextDouble() * 1.4);
                    k.Rock(new Vector3(p.x, GroundY(p.x, p.y) - 0.2f, p.y), new Vector3(sz, sz * 0.6f, sz), rng, new Color(0.60f, 0.58f, 0.55f));
                }
        }

        // ---------------- Baeume ----------------
        List<Forest.TreeInfo> PlaceTrees()
        {
            var list = new List<Forest.TreeInfo>(160000);
            var r = new System.Random(4343);
            const float step = 10.5f;
            for (float z = -Half + 15f; z < Half - 15f; z += step)
                for (float x = -Half + 15f; x < Half - 15f; x += step)
                {
                    float px = x + (float)(r.NextDouble() - 0.5) * step * 0.9f, pz = z + (float)(r.NextDouble() - 0.5) * step * 0.9f;
                    double roll = r.NextDouble(), kroll = r.NextDouble();
                    byte s = SurfAt(px, pz);
                    if (s != SWald)
                    {
                        // vereinzelte Baeume auf Wiesen, am Bach Weiden und Pappeln
                        if (s == SWiese && roll < 0.006 && RoadE(px, pz) > 6f && HouseFree(px, pz, 4f))
                            list.Add(Tree(px, pz, kroll < 0.5 ? Forest.Buche : kroll < 0.8 ? Forest.Fichte : Forest.Eiche, r));
                        else if ((s == SWiese || s == SKies) && RiverE(px, pz) > 3f && RiverE(px, pz) < 14f && roll < 0.12 && RoadE(px, pz) > 5f)
                            list.Add(Tree(px, pz, kroll < 0.55 ? Forest.Weide : kroll < 0.8 ? Forest.Pappel : Forest.Birke, r));
                        continue;
                    }
                    if (roll > 0.86) continue;   // Luecken
                    float y = GroundY(px, pz);
                    Valley(px, pz, out float floor, out _);
                    float above = y - floor;
                    byte kind;
                    float mix = n.Get(px / 500f + 40f, pz / 500f + 2f);
                    if (above < 220f && mix + above / 600f < 0.62f) kind = Forest.MixedBroadleaf(kroll, false);
                    else kind = kroll < 0.82 ? Forest.Fichte : kroll < 0.96 ? Forest.Kiefer : Forest.Birke;
                    list.Add(Tree(px, pz, kind, r));
                }
            return list;
        }

        Forest.TreeInfo Tree(float x, float z, byte kind, System.Random r) =>
            new Forest.TreeInfo { pos = new Vector3(x, GroundY(x, z) - 0.25f, z), yaw = (float)r.NextDouble() * 360f, scale = 0.72f + (float)r.NextDouble() * 0.55f, kind = kind };

        // ---------------- Startpunkte ----------------
        void MakeSpawns()
        {
            var want = new List<(string, Vector2)>
            {
                ("Westausgang", new Vector2(-3550f, Vz(-3550f))),
                ("Unterwald", villages[0].c), ("Matt", villages[2].c), ("Brunnen", villages[1].c),
                ("Suedtal (Sankt Anton)", villages[5].c), ("Nordtal (Hinterbach)", villages[4].c),
                ("Talmitte", new Vector2(600f, Vz(600f))), ("Oberried", villages[3].c),
                ("Ostausgang", new Vector2(3550f, Vz(3550f))),
            };
            foreach (var (nm, p) in want)
            {
                Vector2 best = p; bool ok = false;
                for (float rr = 0f; rr < 700f && !ok; rr += 15f)
                    for (int k = 0; k < 24 && !ok; k++)
                    {
                        float ang = k * Mathf.PI * 2f / 24f;
                        Vector2 q = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
                        if (CanSpawn(q.x, q.y)) { best = q; ok = true; }
                    }
                if (!ok) continue;
                // Blick talaufwaerts zur Talmitte
                Vector2 to = new Vector2(0f, Vz(0f)) - best;
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
                Vector2 p = s.xz + new Vector2(25f, 15f);
                if (!CanSpawn(p.x, p.y)) p = s.xz + new Vector2(-25f, -15f);
                if (!CanSpawn(p.x, p.y)) continue;
                t.Add(new Spawn(kinds[t.Count % kinds.Length], p.x, p.y, s.yaw + 180f));
            }
            return t;
        }

        // ---------------- Karte fuer die Startpunkt-Auswahl ----------------
        void BuildOverview()
        {
            var dots = new List<Vector3>();
            foreach (var h in houses) dots.Add(new Vector3((h.x + Half) / Size, (h.y + Half) / Size, h.z));
            overview = TerrainKit.Overview("Talland Karte", H, G, id =>
            {
                switch (surf[id])
                {
                    case SWeg: return new Color(0.92f, 0.88f, 0.76f);
                    case SWald: return new Color(0.20f, 0.34f, 0.18f);
                    case SFels: return new Color(0.62f, 0.60f, 0.58f);
                    case SAlm: return new Color(0.62f, 0.70f, 0.44f);
                    case SWasser: return new Color(0.36f, 0.56f, 0.80f);
                    case SKies: return new Color(0.70f, 0.68f, 0.62f);
                    case SDorf: return new Color(0.66f, 0.68f, 0.50f);
                    default: return new Color(0.52f, 0.64f, 0.36f);
                }
            }, 50f, 0.035f, dots);
            // Gipfel benennen (hoechster Punkt noerdlich und suedlich des Tals)
            AddPeak("▲ Grauhorn", true); AddPeak("▲ Schneestock", false);
        }

        void AddPeak(string name, bool north)
        {
            float best = -1f; Vector2 bp = Vector2.zero;
            for (int iz = 8; iz < G - 8; iz += 8)
                for (int ix = 8; ix < G - 8; ix += 8)
                {
                    float x = ix * Cell - Half, z = iz * Cell - Half;
                    if ((z > Vz(x)) != north) continue;
                    if (H[Idx(ix, iz)] > best) { best = H[Idx(ix, iz)]; bp = new Vector2(x, z); }
                }
            if (best > 0f) places.Add((name, bp));
        }
    }
}
