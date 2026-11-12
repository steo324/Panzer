using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>Eine Karte: baut ihr Gelaende selbst und sagt, wo Spieler und Ziele stehen.</summary>
    public abstract class GameMap
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public virtual bool Fog => false;
        public virtual float FogStart => 400f;
        public virtual float FogEnd => 2600f;
        public virtual Vector3? RampSpawn => null;   // Taste B (nur Testgelaende)

        // ---- Startpunkt waehlen (grosse Karten) ----
        public struct SpawnPoint
        {
            public string name; public Vector2 xz; public float yaw;
            public SpawnPoint(string name, float x, float z, float yaw) { this.name = name; xz = new Vector2(x, z); this.yaw = yaw; }
        }
        /// <summary>Spieler waehlt beim Start (und nach dem Neustart) den Startpunkt auf einer Karte.</summary>
        public virtual bool SpawnSelect => false;
        public virtual List<SpawnPoint> SpawnPoints => null;
        /// <summary>Draufsicht fuer die Startpunkt-Auswahl (Norden oben); deckt -Extent..+Extent ab.</summary>
        public virtual Texture2D Overview => null;
        public virtual float Extent => 1000f;
        /// <summary>Kann hier ein Panzer stehen (kein Wald, kein Haus, nicht zu steil)?</summary>
        public virtual bool CanSpawn(float x, float z) => true;

        public struct Spawn
        {
            public TankSpec spec; public float x, z, yaw;
            public Spawn(TankSpec spec, float x, float z, float yaw) { this.spec = spec; this.x = x; this.z = z; this.yaw = yaw; }
        }

        public abstract void Build(Transform root);
        public abstract Vector2 PlayerXZ { get; }
        public virtual float PlayerYaw => 0f;
        public abstract List<Spawn> Targets();

        /// <summary>Bodenhoehe an einer Stelle (Raycast von oben).</summary>
        public virtual float GroundY(float x, float z)
        {
            if (Physics.Raycast(new Vector3(x, 1000f, z), Vector3.down, out RaycastHit hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return 0f;
        }

        /// <summary>Untergrund fuer Kettenspuren (GroundTracks.Wiese/Acker/Weg/Hart).</summary>
        public virtual int SurfaceAt(float x, float z) => GroundTracks.Hart;

        protected static GameObject Block(Transform root, string name, Vector3 pos, Vector3 scale, Vector3 euler, Color? color = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.transform.rotation = Quaternion.Euler(euler);
            // immer eigenes Material: das Standardmaterial von CreatePrimitive ist im Build pink
            go.GetComponent<Renderer>().sharedMaterial = Mat(color ?? new Color(0.55f, 0.53f, 0.50f));
            return go;
        }

        static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();

        /// <summary>Geteiltes Material pro Farbe (spart Speicher, GPU-Instancing an).</summary>
        public static Material Mat(Color c)
        {
            if (mats.TryGetValue(c, out var m) && m != null) return m;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh) { color = c, enableInstancing = true };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            mats[c] = m;
            return m;
        }

        /// <summary>Eigenes Material (URP Lit) fuer Teile, deren Farbe sich aendert (z. B. Innenteile bei Schaden).
        /// Ersetzt das Standardmaterial von CreatePrimitive, das im Build pink waere.</summary>
        public static Material NewMat(Color c)
        {
            var m = new Material(Mat(Color.white)) { color = c };
            return m;
        }
    }

    // =====================================================================
    /// <summary>Das bisherige Testgelaende mit Rampen und Kisten.</summary>
    public class TestRangeMap : GameMap
    {
        public override string Name => "Testgelaende";
        public override string Description => "Flaches Gelaende mit Rampen, Mauer und Kisten. Zielpanzer auf 150-800 m. Zum Ausprobieren von Fahrphysik und Durchschlag.";
        public override Vector2 PlayerXZ => Vector2.zero;
        public override Vector3? RampSpawn => new Vector3(-45f, 0.3f, 18f);

        public override void Build(Transform root)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Boden";
            ground.transform.SetParent(root, false);
            ground.transform.position = new Vector3(0f, 0f, 500f);
            ground.transform.localScale = new Vector3(300f, 1f, 300f);
            ground.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.42f, 0.40f, 0.33f));   // sonst pink im Build

            Block(root, "Rampe", new Vector3(-25f, 0.9f, 40f), new Vector3(10f, 1f, 22f), new Vector3(-9f, 0f, 0f));
            Block(root, "Rampe runter", new Vector3(-25f, 0.9f, 62f), new Vector3(10f, 1f, 22f), new Vector3(9f, 0f, 0f));
            Block(root, "Rampe steil", new Vector3(-45f, 1.2f, 40f), new Vector3(10f, 1f, 18f), new Vector3(-20f, 0f, 0f));
            Block(root, "Mauer", new Vector3(10f, 1.5f, 130f), new Vector3(14f, 3f, 1f), Vector3.zero);
            Block(root, "Graben-Rand", new Vector3(-10f, 0.4f, 160f), new Vector3(30f, 0.8f, 3f), Vector3.zero);
            for (int i = 0; i < 6; i++)
                Block(root, "Kiste", new Vector3(-30f + i * 4f, 0.75f, 110f + (i % 2) * 3f), Vector3.one * 1.5f, new Vector3(0f, i * 17f, 0f));
        }

        public override List<Spawn> Targets() => new List<Spawn>
        {
            new Spawn(TankLibrary.Sherman, -15f, 300f, 180f),
            new Spawn(TankLibrary.T34_85, 25f, 500f, 150f),
            new Spawn(TankLibrary.PanzerIV, 45f, 200f, 270f),
            new Spawn(TankLibrary.Sherman, -60f, 800f, 210f),
            new Spawn(TankLibrary.Tiger, -40f, 150f, 135f),
        };
    }

    // =====================================================================
    /// <summary>Weite Ebene: sanfte Huegel, Wiesen, Weizenfelder, Feldwege, Waelder und Bauernhoefe.</summary>
    public class PlainsMap : GameMap
    {
        public override string Name => "Weite Ebene";
        public override string Description => "2 x 2 km offenes Land mit sanften Huegeln, Weizenfeldern, Feldwegen, Waeldern und Bauernhoefen. Lange Schussdistanzen, Deckung hinter Huegeln und Hoefen.";
        public override bool Fog => true;
        public override Vector2 PlayerXZ => new Vector2(0f, -650f);

        const float Size = 2000f, MaxHeight = 80f;
        const int Res = 513, AlphaRes = 512;
        Terrain terrain;
        System.Random rng;

        public override float GroundY(float x, float z)
        {
            if (terrain == null) return base.GroundY(x, z);
            return terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
        }

        public override List<Spawn> Targets() => new List<Spawn>
        {
            new Spawn(TankLibrary.Sherman, -80f, -150f, 180f),
            new Spawn(TankLibrary.T34_85, 140f, 50f, 200f),
            new Spawn(TankLibrary.PanzerIV, -260f, 250f, 160f),
            new Spawn(TankLibrary.Sherman, 60f, 420f, 185f),
            new Spawn(TankLibrary.Tiger, 320f, -250f, 235f),
        };

        // Bereiche, in denen nichts gebaut wird (Spawns)
        readonly List<Vector3> keepClear = new List<Vector3>();

        public override void Build(Transform root)
        {
            rng = new System.Random(1944);
            keepClear.Clear();
            occupied.Clear();
            keepClear.Add(new Vector3(PlayerXZ.x, 0f, PlayerXZ.y));
            foreach (var t in Targets()) keepClear.Add(new Vector3(t.x, 0f, t.z));

            BuildTerrain(root);
            BuildForests(root);
            BuildTreeLines(root);
            BuildFarms(root);
            BuildHay(root);
            WheatField.Create(root, WheatGrows, GroundY, RowsAlongX, GrassGrows);   // Weizen auf den Aeckern, Gras auf den Wiesen
        }

        // Bereiche ohne Weizen (Waelder, Hoefe): x, z, Radius
        readonly List<Vector3> occupied = new List<Vector3>();

        bool WheatGrows(float x, float z)
        {
            if (Mathf.Abs(x) > Size / 2f - 10f || Mathf.Abs(z) > Size / 2f - 10f) return false;
            if (FieldKind(x, z) != 1) return false;
            if (Mathf.Min(Mathf.Abs(x - RoadX(z)), Mathf.Abs(z - RoadZ(x))) < 5.5f) return false;
            foreach (var o in occupied)
                if ((x - o.x) * (x - o.x) + (z - o.y) * (z - o.y) < o.z * o.z) return false;
            return true;
        }

        bool GrassGrows(float x, float z)
        {
            if (Mathf.Abs(x) > Size / 2f - 10f || Mathf.Abs(z) > Size / 2f - 10f) return false;
            if (FieldKind(x, z) != 0) return false;
            if (Mathf.Min(Mathf.Abs(x - RoadX(z)), Mathf.Abs(z - RoadZ(x))) < 4f) return false;
            for (int i = 0; i < occupied.Count; i++)
            {
                var o = occupied[i];
                if (o.z < 40f && (x - o.x) * (x - o.x) + (z - o.y) * (z - o.y) < 30f * 30f) return false;   // Hofplatz
            }
            return true;
        }

        static bool RowsAlongX(float x, float z) => (FieldHash(x, z) & 8) != 0;

        // ---------------- Gelaende ----------------
        static float RoadX(float z) => -150f + Mathf.Sin(z * 0.004f) * 60f;      // Feldweg Nord-Sued
        static float RoadZ(float x) => 120f + Mathf.Sin(x * 0.003f + 1f) * 80f;  // Feldweg Ost-West

        float HeightAt(float wx, float wz)
        {
            // grosse sanfte Wellen + mittlere Huegel (ca. 300 m breit) + kleine Bodenwellen
            float big = Mathf.PerlinNoise((wx + 5000f) * 0.0010f, (wz + 5000f) * 0.0010f);
            float mid = Mathf.PerlinNoise((wx + 300f) * 0.0030f, (wz + 700f) * 0.0030f);
            float small = Mathf.PerlinNoise(wx * 0.012f + 50f, wz * 0.012f + 90f);
            float detail = Mathf.PerlinNoise(wx * 0.04f + 11f, wz * 0.04f + 23f);
            float n = big * 0.45f + mid * 0.32f + small * 0.05f + detail * 0.012f;
            n = Mathf.Pow(n, 1.35f);  // breitere Taeler, rundere Kuppen

            // Wege folgen dem Gelaende, aber ohne kleine Buckel
            float dRoad = Mathf.Min(Mathf.Abs(wx - RoadX(wz)), Mathf.Abs(wz - RoadZ(wx)));
            float smooth = Mathf.Pow(big * 0.45f + mid * 0.32f + 0.03f, 1.35f);
            n = Mathf.Lerp(smooth, n, Mathf.InverseLerp(4f, 14f, dRoad));
            return n * 0.75f; // Kuppen bis ca. 40 m, Huegel meist 10-25 m hoch
        }

        void BuildTerrain(Transform root)
        {
            var td = new TerrainData { heightmapResolution = Res };
            td.size = new Vector3(Size, MaxHeight, Size);
            var h = new float[Res, Res];
            for (int iz = 0; iz < Res; iz++)
                for (int ix = 0; ix < Res; ix++)
                {
                    float wx = ix / (float)(Res - 1) * Size - Size / 2f;
                    float wz = iz / (float)(Res - 1) * Size - Size / 2f;
                    h[iz, ix] = HeightAt(wx, wz);
                }
            td.SetHeights(0, 0, h);

            // Bodenarten: 0 Wiese, 1 Weizen, 2 Erde/Weg, 3 dunkle Wiese
            // Fototexturen (CC0, ambientCG.com) mit Normal Map + Maske; ohne sie (oder auf dem Server) einfache Rauschtexturen
            bool hd = !NetSync.Headless && Resources.Load<Texture2D>("Boden/Wiese_Farbe") != null;
            td.terrainLayers = hd ? new[]
            {
                HdLayer("Wiese", 3.2f, new Color(1.0f, 1.0f, 0.76f)),          // heller, gelblicher
                HdLayer("Acker", 2.6f, new Color(1.08f, 0.98f, 0.78f)),
                HdLayer("Weg", 3.0f, new Color(0.95f, 0.90f, 0.82f)),
                HdLayer("WieseDunkel", 3.6f, new Color(0.74f, 0.86f, 0.64f)),  // dunkler, satter
            } : new[]
            {
                Layer(NoiseTex(new Color(0.33f, 0.45f, 0.18f), new Color(0.24f, 0.36f, 0.12f), 7), 10f),
                Layer(NoiseTex(new Color(0.78f, 0.66f, 0.33f), new Color(0.62f, 0.50f, 0.22f), 11), 6f),
                Layer(NoiseTex(new Color(0.45f, 0.36f, 0.25f), new Color(0.35f, 0.27f, 0.18f), 13), 5f),
                Layer(NoiseTex(new Color(0.26f, 0.38f, 0.15f), new Color(0.20f, 0.30f, 0.10f), 17), 12f),
            };
            td.alphamapResolution = AlphaRes;
            var a = new float[AlphaRes, AlphaRes, 4];
            for (int iz = 0; iz < AlphaRes; iz++)
                for (int ix = 0; ix < AlphaRes; ix++)
                {
                    float wx = ix / (float)(AlphaRes - 1) * Size - Size / 2f;
                    float wz = iz / (float)(AlphaRes - 1) * Size - Size / 2f;
                    float road = Mathf.Min(Mathf.Abs(wx - RoadX(wz)), Mathf.Abs(wz - RoadZ(wx)));
                    float w0 = 1f, w1 = 0f, w2 = 0f, w3 = 0f;
                    int fk = FieldKind(wx, wz);
                    if (fk == 1) { w0 = 0f; w1 = 1f; }
                    else if (fk == 2) { w0 = 0f; w1 = 0.6f; w2 = 0.4f; }   // abgeerntet: Stoppeln
                    else
                    {
                        // Wiese: helle/dunkle Flecken in mehreren Groessen - bricht das Kachelmuster auf
                        float v = Mathf.PerlinNoise(wx * 0.008f + 3f, wz * 0.008f + 7f) * 0.6f
                                + Mathf.PerlinNoise(wx * 0.045f + 13f, wz * 0.045f + 5f) * 0.4f;
                        w3 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.36f, 0.66f, v));
                        w0 = 1f - w3;
                        // vereinzelt kahle Stellen
                        float bare = Mathf.PerlinNoise(wx * 0.06f + 41f, wz * 0.06f + 17f);
                        if (bare > 0.72f) { float b = Mathf.InverseLerp(0.72f, 0.85f, bare) * 0.6f; w2 = b; w0 *= 1f - b; w3 *= 1f - b; }
                    }
                    if (road < 3.5f) { w0 = 0f; w1 = 0f; w3 = 0f; w2 = 1f; }
                    else if (road < 5f) { float t = (road - 3.5f) / 1.5f; w2 = 1f - t; w0 *= t; w1 *= t; w3 *= t; }
                    float sum = w0 + w1 + w2 + w3;
                    a[iz, ix, 0] = w0 / sum; a[iz, ix, 1] = w1 / sum; a[iz, ix, 2] = w2 / sum; a[iz, ix, 3] = w3 / sum;
                }
            td.SetAlphamaps(0, 0, a);
            alpha = a;

            var tgo = Terrain.CreateTerrainGameObject(td);
            tgo.name = "Gelaende";
            tgo.transform.SetParent(root, false);
            tgo.transform.position = new Vector3(-Size / 2f, 0f, -Size / 2f);
            terrain = tgo.GetComponent<Terrain>();
            var sh = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (sh != null)
            {
                var tm = new Material(sh);
                if (hd) SetTerrainDetail(tm, GameSettings.Effects >= 1);
                terrain.materialTemplate = tm;
            }
            terrain.basemapDistance = 1500f;
            Physics.SyncTransforms();
        }

        float[,,] alpha;   // Bodenarten-Gewichte (0 Wiese, 1 Acker, 2 Weg, 3 dunkle Wiese)

        public override int SurfaceAt(float x, float z)
        {
            if (alpha == null) return GroundTracks.Wiese;
            int ix = Mathf.Clamp(Mathf.RoundToInt((x + Size / 2f) / Size * (AlphaRes - 1)), 0, AlphaRes - 1);
            int iz = Mathf.Clamp(Mathf.RoundToInt((z + Size / 2f) / Size * (AlphaRes - 1)), 0, AlphaRes - 1);
            float wiese = alpha[iz, ix, 0] + alpha[iz, ix, 3], acker = alpha[iz, ix, 1], weg = alpha[iz, ix, 2];
            if (weg >= wiese && weg >= acker) return GroundTracks.Weg;
            return acker > wiese ? GroundTracks.Acker : GroundTracks.Wiese;
        }

        static int FieldHash(float wx, float wz)
        {
            int cx = Mathf.FloorToInt((wx + 1000f) / 140f), cz = Mathf.FloorToInt((wz + 1000f) / 140f);
            return ((cx * 73856093) ^ (cz * 19349663)) & 0x7fffffff;
        }

        /// <summary>Felder: Raster aus 140-m-Feldern, etwa jedes dritte ist ein Acker.
        /// 0 = Wiese, 1 = stehender Weizen, 2 = abgeerntet (Stoppeln, Heuballen).</summary>
        static int FieldKind(float wx, float wz)
        {
            int hash = FieldHash(wx, wz);
            if (hash % 3 != 0) return 0;
            // Feldrand mit etwas Abstand (Wiesenstreifen)
            float lx = Mathf.Repeat(wx + 1000f, 140f), lz = Mathf.Repeat(wz + 1000f, 140f);
            if (!(lx > 8f && lx < 132f && lz > 8f && lz < 132f)) return 0;
            return 1;   // alle Aecker: stehender Weizen
        }

        /// <summary>Fototextur-Bodenart aus Resources/Boden: Farbe, Normal Map (Relief), Maske (Verdeckung, Hoehe, Glaette).</summary>
        internal static TerrainLayer HdLayer(string name, float tile, Color tint)
        {
            return new TerrainLayer
            {
                diffuseTexture = Resources.Load<Texture2D>($"Boden/{name}_Farbe"),
                normalMapTexture = Resources.Load<Texture2D>($"Boden/{name}_Normal"),
                maskMapTexture = Resources.Load<Texture2D>($"Boden/{name}_Maske"),
                normalScale = 1.3f,
                tileSize = new Vector2(tile, tile),
                diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f),
                maskMapRemapMax = new Vector4(0f, 1f, 1f, 0.35f),   // kein Metall, Glaette gedaempft (Boden glaenzt nicht)
                smoothness = 0f, metallic = 0f,
            };
        }

        /// <summary>Relief und Hoehen-Uebergaenge an/aus (Bildeffekte Niedrig = aus, spart Rechenzeit auf schwacher Grafik).</summary>
        public static void SetTerrainDetail(Material m, bool on)
        {
            if (on) { m.EnableKeyword("_NORMALMAP"); m.EnableKeyword("_MASKMAP"); m.EnableKeyword("_TERRAIN_BLEND_HEIGHT"); }
            else { m.DisableKeyword("_NORMALMAP"); m.DisableKeyword("_MASKMAP"); m.DisableKeyword("_TERRAIN_BLEND_HEIGHT"); }
            m.SetFloat("_EnableHeightBlend", on ? 1f : 0f);
            m.SetFloat("_HeightTransition", 0.25f);
        }

        internal static TerrainLayer Layer(Texture2D tex, float tile)
        {
            return new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(tile, tile), smoothness = 0f, metallic = 0f };
        }

        internal static Texture2D NoiseTex(Color a, Color b, int seed)
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var r = new System.Random(seed);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // kachelbares Rauschen (Kreis-Trick) + feine Koernung
                    float u = x / (float)n * Mathf.PI * 2f, v = y / (float)n * Mathf.PI * 2f;
                    float p = Mathf.PerlinNoise(seed + Mathf.Cos(u) * 1.5f + Mathf.Cos(v) * 0.7f, seed + Mathf.Sin(u) * 1.5f + Mathf.Sin(v) * 0.7f);
                    float g = (float)r.NextDouble() * 0.25f;
                    var col = Color.Lerp(a, b, Mathf.Clamp01(p * 0.8f + g));
                    col.a = 0.05f; // Alpha = Glaette im URP-Terrain -> matt, sonst spiegelt der Boden den Himmel
                    px[y * n + x] = col;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        // ---------------- Objekte ----------------
        float Rand(float min, float max) => (float)(min + rng.NextDouble() * (max - min));

        bool Clear(float x, float z, float radius)
        {
            if (Mathf.Abs(x) > Size / 2f - 20f || Mathf.Abs(z) > Size / 2f - 20f) return false;
            foreach (var k in keepClear)
                if ((new Vector2(x - k.x, z - k.z)).sqrMagnitude < radius * radius) return false;
            float road = Mathf.Min(Mathf.Abs(x - RoadX(z)), Mathf.Abs(z - RoadZ(x)));
            return road > 7f;
        }

        void Tree(Transform root, float x, float z, float size, bool alley = false)
        {
            float y = GroundY(x, z);
            if (Trees.Available)
            {
                var kind = alley ? Trees.AlleyKind(rng) : Trees.ForestKind(rng);
                Trees.Spawn(root, kind, new Vector3(x, y, z), Rand(0f, 360f), size * 0.85f);
                return;
            }
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Baum";
            trunk.transform.SetParent(root, false);
            trunk.transform.position = new Vector3(x, y + 3f * size, z);
            trunk.transform.localScale = new Vector3(0.6f * size, 3f * size, 0.6f * size);
            trunk.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.32f, 0.23f, 0.15f));

            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.name = "Krone";
            Object.DestroyImmediate(crown.GetComponent<Collider>()); // Blaetter halten keine Granate auf
            crown.transform.SetParent(trunk.transform, true);
            crown.transform.position = new Vector3(x, y + 7.5f * size, z);
            crown.transform.localScale = new Vector3(5f / 0.6f, 6f / 3f, 5f / 0.6f) * Rand(0.85f, 1.15f);
            float g = Rand(0f, 1f);
            crown.GetComponent<Renderer>().sharedMaterial = Mat(g < 0.33f ? new Color(0.17f, 0.30f, 0.11f)
                                                              : g < 0.66f ? new Color(0.21f, 0.35f, 0.13f) : new Color(0.14f, 0.26f, 0.10f));
        }

        void BuildForests(Transform root)
        {
            var forests = new GameObject("Waelder").transform;
            forests.SetParent(root, false);
            Vector2[] centers = { new Vector2(-500f, -300f), new Vector2(450f, 300f), new Vector2(-350f, 550f),
                                  new Vector2(600f, -500f), new Vector2(-700f, 150f), new Vector2(200f, 700f), new Vector2(-150f, -800f) };
            foreach (var c in centers)
            {
                float radius = Rand(60f, 110f);
                occupied.Add(new Vector3(c.x, c.y, radius + 6f));
                int count = (int)(radius * (Trees.Available ? 1.4f : 0.8f));
                for (int i = 0; i < count; i++)
                {
                    float ang = Rand(0f, Mathf.PI * 2f), d = Mathf.Sqrt(Rand(0f, 1f)) * radius;
                    float x = c.x + Mathf.Cos(ang) * d, z = c.y + Mathf.Sin(ang) * d;
                    if (Clear(x, z, 40f)) Tree(forests, x, z, Rand(0.8f, 1.3f));
                }
            }
        }

        void BuildTreeLines(Transform root)
        {
            var lines = new GameObject("Baumreihen").transform;
            lines.SetParent(root, false);
            // Alleen entlang der Feldwege, mit Luecken
            for (float z = -950f; z < 950f; z += Rand(10f, 16f))
            {
                if (Mathf.PerlinNoise(z * 0.01f, 3f) < 0.45f) continue;
                float x = RoadX(z);
                if (Clear(x - 9f, z, 35f)) Tree(lines, x - 9f + Rand(-1f, 1f), z, Rand(0.9f, 1.2f), true);
                if (Clear(x + 9f, z, 35f)) Tree(lines, x + 9f + Rand(-1f, 1f), z, Rand(0.9f, 1.2f), true);
            }
            for (float x = -950f; x < 950f; x += Rand(12f, 18f))
            {
                if (Mathf.PerlinNoise(x * 0.01f, 7f) < 0.5f) continue;
                float z = RoadZ(x);
                if (Clear(x, z + 9f, 35f)) Tree(lines, x, z + 9f + Rand(-1f, 1f), Rand(0.9f, 1.2f), true);
            }
        }

        void House(Transform root, float x, float z, float yaw, float w, float l, float hgt, Color wall, Color roof)
        {
            float y = GroundY(x, z);
            var house = new GameObject("Haus").transform;
            house.SetParent(root, false);
            house.position = new Vector3(x, y, z);
            house.rotation = Quaternion.Euler(0f, yaw, 0f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Waende";
            body.transform.SetParent(house, false);
            body.transform.localPosition = new Vector3(0f, hgt / 2f - 0.5f, 0f);
            body.transform.localScale = new Vector3(w, hgt + 1f, l);
            body.GetComponent<Renderer>().sharedMaterial = Mat(wall);

            // Satteldach: um 45 Grad gedrehter Wuerfel, untere Haelfte steckt im Haus
            var r = GameObject.CreatePrimitive(PrimitiveType.Cube);
            r.name = "Dach";
            r.transform.SetParent(house, false);
            float s = w / 1.414f + 0.3f;
            r.transform.localPosition = new Vector3(0f, hgt, 0f);
            r.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            r.transform.localScale = new Vector3(s, s, l + 0.6f);
            r.GetComponent<Renderer>().sharedMaterial = Mat(roof);
        }

        void BuildFarms(Transform root)
        {
            var farms = new GameObject("Bauernhoefe").transform;
            farms.SetParent(root, false);
            Vector2[] spots = { new Vector2(-120f, 160f), new Vector2(380f, 80f), new Vector2(-420f, -120f), new Vector2(150f, -420f), new Vector2(-560f, 480f) };
            Color[] walls = { new Color(0.85f, 0.82f, 0.74f), new Color(0.62f, 0.32f, 0.24f), new Color(0.78f, 0.74f, 0.64f) };
            Color[] roofs = { new Color(0.45f, 0.16f, 0.12f), new Color(0.30f, 0.24f, 0.22f) };
            foreach (var p in spots)
            {
                float yaw = Rand(0f, 90f);
                occupied.Add(new Vector3(p.x, p.y, 38f));
                var q = Quaternion.Euler(0f, yaw, 0f);
                // Wohnhaus, Scheune, Stall um einen Hof
                Vector3 o1 = q * new Vector3(0f, 0f, 0f), o2 = q * new Vector3(18f, 0f, 4f), o3 = q * new Vector3(4f, 0f, 20f);
                if (Clear(p.x, p.y, 45f))
                {
                    House(farms, p.x + o1.x, p.y + o1.z, yaw, 8f, 11f, 4f, walls[rng.Next(walls.Length)], roofs[0]);
                    House(farms, p.x + o2.x, p.y + o2.z, yaw + 90f, 12f, 18f, 6f, new Color(0.45f, 0.30f, 0.20f), roofs[1]);
                    House(farms, p.x + o3.x, p.y + o3.z, yaw, 7f, 14f, 3.5f, walls[rng.Next(walls.Length)], roofs[rng.Next(roofs.Length)]);
                }
            }
        }

        void BuildHay(Transform root)
        {
            var hay = new GameObject("Heuballen").transform;
            hay.SetParent(root, false);
            for (int i = 0; i < 450; i++)
            {
                float x = Rand(-950f, 950f), z = Rand(-950f, 950f);
                // Heu auf gemaehten Wiesen am Feldrand
                if (FieldKind(x, z) != 0 || !Clear(x, z, 30f)) continue;
                if (FieldKind(x + 25f, z) == 0 && FieldKind(x - 25f, z) == 0 && FieldKind(x, z + 25f) == 0 && FieldKind(x, z - 25f) == 0) continue;
                bool inWood = false;
                foreach (var o in occupied) if ((x - o.x) * (x - o.x) + (z - o.y) * (z - o.y) < o.z * o.z) inWood = true;
                if (inWood) continue;
                var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                b.name = "Heuballen";
                b.transform.SetParent(hay, false);
                b.transform.position = new Vector3(x, GroundY(x, z) + 0.7f, z);
                b.transform.rotation = Quaternion.Euler(90f, Rand(0f, 180f), 0f);
                b.transform.localScale = new Vector3(1.5f, 0.6f, 1.5f);
                b.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.80f, 0.68f, 0.36f));
            }
        }
    }
}
