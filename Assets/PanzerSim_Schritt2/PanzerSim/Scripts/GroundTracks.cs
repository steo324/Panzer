using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PanzerSim
{
    /// <summary>
    /// Kettenspuren auf dem Boden, je nach Untergrund verschieden:
    ///  Acker  - tiefe, dunkle Furchen mit deutlichem Kettenprofil
    ///  Wiese  - plattgedruecktes, dunkleres Gras (die Halme selbst legt WheatField flach)
    ///  Weg    - heller, staubiger Abdruck
    ///  Hart   - leichte Schmutzspur (Testgelaende, Beton, Rampen)
    /// Jede Kette legt alle ~35 cm ein Stueck Band auf den Boden. Pro Untergrund ein Mesh als Ringpuffer
    /// (die aeltesten Stuecke verschwinden zuerst) - zusammen 4 Zeichenaufrufe, egal wie viele Panzer fahren.
    /// </summary>
    public class GroundTracks : MonoBehaviour
    {
        public const int Wiese = 0, Acker = 1, Weg = 2, Hart = 3;
        const float Step = 0.35f;          // Abstand der Bandpunkte
        const float Lift = 0.025f;         // ueber dem Boden (gegen Flimmern)
        static readonly int[] Capacity = { 3000, 3000, 2000, 2000 };   // Stuecke pro Untergrund

        class Strip
        {
            public bool has; public Vector3 pos, right; public float v; public int surface;
        }
        class TankState { public Strip left = new Strip(), right = new Strip(); }

        class Layer
        {
            public Mesh mesh; public Material mat;
            public Vector3[] verts; public Vector3[] normals; public Vector4[] tangents; public Vector2[] uvs; public int[] tris;
            public int next, cap; public bool dirty;
        }

        readonly Layer[] layers = new Layer[4];
        readonly Dictionary<TankController, TankState> states = new Dictionary<TankController, TankState>();
        readonly List<TankController> tanks = new List<TankController>();
        GameMap map;
        float refresh;
        static Texture2D albedo, normal;

        public static GroundTracks Create(Transform parent, GameMap map)
        {
            if (NetSync.Headless) return null;   // Server ohne Grafik: reine Optik
            var go = new GameObject("Kettenspuren");
            go.transform.SetParent(parent, false);
            var gt = go.AddComponent<GroundTracks>();
            gt.map = map;
            gt.Setup();
            return gt;
        }

        void Setup()
        {
            MakeTextures();
            //            Farbe                                     Deckkraft  Relief
            layers[Wiese] = NewLayer(Wiese, new Color(0.20f, 0.22f, 0.10f), 0.55f, 0.6f);
            layers[Acker] = NewLayer(Acker, new Color(0.17f, 0.12f, 0.08f), 0.92f, 1.6f);
            layers[Weg]   = NewLayer(Weg,   new Color(0.62f, 0.55f, 0.44f), 0.35f, 0.5f);
            layers[Hart]  = NewLayer(Hart,  new Color(0.12f, 0.11f, 0.10f), 0.35f, 0.3f);
        }

        Layer NewLayer(int s, Color c, float alpha, float bump)
        {
            var L = new Layer { cap = Capacity[s] };
            L.verts = new Vector3[L.cap * 4]; L.normals = new Vector3[L.cap * 4]; L.tangents = new Vector4[L.cap * 4]; L.uvs = new Vector2[L.cap * 4];
            L.tris = new int[L.cap * 6];
            for (int i = 0; i < L.cap; i++)
            {
                int v = i * 4, t = i * 6;
                L.tris[t] = v; L.tris[t + 1] = v + 2; L.tris[t + 2] = v + 1;
                L.tris[t + 3] = v + 1; L.tris[t + 4] = v + 2; L.tris[t + 5] = v + 3;
            }
            L.mesh = new Mesh { name = "Spuren" + s, indexFormat = IndexFormat.UInt32 };
            L.mesh.MarkDynamic();
            L.mesh.vertices = L.verts; L.mesh.normals = L.normals; L.mesh.tangents = L.tangents; L.mesh.uv = L.uvs; L.mesh.triangles = L.tris;
            L.mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);   // nie wegschneiden (liegt ueberall)

            // URP Lit, transparent, mit Normal Map (Profil der Kettenglieder)
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Kettenspur" + s };
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.EnableKeyword("_NORMALMAP");
            m.renderQueue = 2990 + s;   // vor anderen durchsichtigen Dingen (Rauch, Glas)
            m.SetTexture("_BaseMap", albedo);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", bump);
            m.SetFloat("_Smoothness", 0.05f);
            m.SetFloat("_Metallic", 0f);
            c.a = alpha;
            m.SetColor("_BaseColor", c);
            L.mat = m;
            return L;
        }

        /// <summary>Kettenprofil: Stollen quer zur Fahrtrichtung, ausgefranste Raender (alles im Code erzeugt).</summary>
        static void MakeTextures()
        {
            if (albedo != null) return;
            const int W = 64, H = 128;     // quer x laengs; laengs = 4 Stollen
            var height = new float[W * H];
            var rnd = new System.Random(7);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)(W - 1), v = y / (float)H;
                    float bar = Mathf.Repeat(v * 4f, 1f);                       // 4 Stollen pro Kachel
                    float cleat = Mathf.SmoothStep(0f, 1f, Mathf.Abs(bar - 0.5f) * 4f - 0.6f);   // 1 = Stollenabdruck (tief)
                    float guide = Mathf.Abs(u - 0.5f) < 0.06f ? 0.5f : 0f;     // Fuehrungszahn in der Mitte
                    float n = Mathf.PerlinNoise(u * 9f, v * 14f) * 0.35f + (float)rnd.NextDouble() * 0.12f;
                    height[y * W + x] = Mathf.Clamp01(1f - cleat * 0.75f - guide * 0.3f + n - 0.2f);
                }
            albedo = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4, name = "Spur" };
            normal = new Texture2D(W, H, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 4, name = "SpurN" };
            var ca = new Color[W * H]; var cn = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float h = height[y * W + x];
                    float u = x / (float)(W - 1), v = y / (float)H;
                    // Rand: weich und unregelmaessig ausgefranst
                    float edge = Mathf.Min(u, 1f - u) * 2f;
                    float fray = Mathf.PerlinNoise(v * 22f + (u < 0.5f ? 0f : 40f), 3f) * 0.35f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f + fray * 0.5f, 0.28f + fray, edge));
                    a *= 0.72f + (1f - h) * 0.28f;                              // im Stollenabdruck deckender
                    float lum = 0.8f + h * 0.35f;                               // Vertiefungen dunkler
                    ca[y * W + x] = new Color(lum, lum, lum, a);
                    float hx = height[y * W + (x + 1) % W] - height[y * W + (x + W - 1) % W];
                    float hy = height[((y + 1) % H) * W + x] - height[((y + H - 1) % H) * W + x];
                    var nrm = new Vector3(-hx * 2.5f, -hy * 2.5f, 1f).normalized;
                    cn[y * W + x] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f);
                }
            albedo.SetPixels(ca); albedo.Apply(true);
            normal.SetPixels(cn); normal.Apply(true);
        }

        void LateUpdate()
        {
            refresh -= Time.unscaledDeltaTime;
            if (refresh <= 0f)
            {
                refresh = 1f;
                tanks.Clear();
                tanks.AddRange(FindObjectsByType<TankController>(FindObjectsSortMode.None));
            }
            foreach (var t in tanks)
            {
                if (t == null || t.spec == null) continue;
                if (!states.TryGetValue(t, out var st)) states[t] = st = new TankState();
                var s = t.spec;
                float x = s.hullWidth / 2f + s.trackWidth / 2f - s.trackInset;
                float z = -s.hullLength * 0.38f;   // hinteres Ende der Auflage: Spur erscheint, wenn der Panzer darueber ist
                UpdateStrip(t, st.left, new Vector3(-x, 0f, z), s.trackWidth);
                UpdateStrip(t, st.right, new Vector3(x, 0f, z), s.trackWidth);
            }
            foreach (var L in layers)
            {
                if (L == null || !L.dirty) continue;
                L.mesh.vertices = L.verts; L.mesh.normals = L.normals; L.mesh.tangents = L.tangents; L.mesh.uv = L.uvs;
                L.dirty = false;
            }
            foreach (var L in layers)
                if (L != null) Graphics.DrawMesh(L.mesh, Matrix4x4.identity, L.mat, 0, null, 0, null, ShadowCastingMode.Off, true);
        }

        void UpdateStrip(TankController t, Strip st, Vector3 local, float width)
        {
            Vector3 above = t.transform.TransformPoint(local + Vector3.up * 1.2f);
            if (!t.RaycastGround(above, Vector3.down, 2.0f, out RaycastHit hit)) { st.has = false; return; }   // in der Luft
            Vector3 p = hit.point;
            if (st.has && (p - st.pos).sqrMagnitude < Step * Step) return;

            Vector3 right = Vector3.ProjectOnPlane(t.transform.right, hit.normal).normalized;
            int surface = hit.collider is TerrainCollider ? map.SurfaceAt(p.x, p.z) : Hart;
            if (st.has && (p - st.pos).sqrMagnitude < 4f)   // Sprung (z. B. Neustart) -> neues Band
            {
                float v1 = st.v + Vector3.Distance(p, st.pos) / 0.6f;   // Profil wiederholt sich alle 0,6 m
                AddQuad(surface, st.pos, st.right, st.v, p, right, v1, hit.normal, width);
                st.v = v1;
            }
            st.has = true; st.pos = p; st.right = right; st.surface = surface;
        }

        void AddQuad(int surface, Vector3 a, Vector3 ra, float va, Vector3 b, Vector3 rb, float vb, Vector3 n, float width)
        {
            var L = layers[surface];
            int i = L.next; L.next = (L.next + 1) % L.cap;
            int v = i * 4;
            float hw = width * 0.5f;
            Vector3 up = n * Lift;
            L.verts[v] = a - ra * hw + up; L.verts[v + 1] = a + ra * hw + up;
            L.verts[v + 2] = b - rb * hw + up; L.verts[v + 3] = b + rb * hw + up;
            L.normals[v] = L.normals[v + 1] = L.normals[v + 2] = L.normals[v + 3] = n;
            Vector4 tg = new Vector4(ra.x, ra.y, ra.z, 1f);   // u laeuft quer (nach rechts)
            L.tangents[v] = L.tangents[v + 1] = L.tangents[v + 2] = L.tangents[v + 3] = tg;
            L.uvs[v] = new Vector2(0f, va); L.uvs[v + 1] = new Vector2(1f, va);
            L.uvs[v + 2] = new Vector2(0f, vb); L.uvs[v + 3] = new Vector2(1f, vb);
            L.dirty = true;
        }

        void OnDestroy()
        {
            foreach (var L in layers) if (L != null) { Destroy(L.mesh); Destroy(L.mat); }
        }
    }
}
