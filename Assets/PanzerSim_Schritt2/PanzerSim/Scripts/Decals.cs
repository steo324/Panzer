using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Sichtbare Treffer auf der Panzerung: Einschussloecher, Dellen (kein Durchschlag), Schleifspuren (Abpraller)
    /// und Russflecken (Sprengtreffer). Groesse nach Kaliber, Form nach Auftreffwinkel (flach = laenglich).
    /// Die Marken haengen am getroffenen Teil (Wanne, Turm, Kanone) und bewegen sich mit.
    /// Texturen werden im Code erzeugt, Shader ist der Partikel-Shader (Resources).
    /// </summary>
    public static class Decals
    {
        public enum Kind { Hole, Dent, Scrape, Scorch, SmallHole }

        static Material holeMat, dentMat, scrapeMat, scorchMat;
        static Mesh quad;
        static readonly Queue<GameObject> all = new Queue<GameObject>();
        const int MaxDecals = 300;

        /// <summary>
        /// Marke setzen. pos/normal = Treffpunkt auf der Oberflaeche, shellDir = Flugrichtung, angleDeg = Auftreffwinkel (0 = senkrecht).
        /// </summary>
        public static void Add(Kind kind, Vector3 pos, Vector3 normal, Vector3 shellDir, float caliberMm, float angleDeg, Transform parent, float size = 0f)
        {
            if (!Ensure()) return;
            normal.Normalize();
            float cal = caliberMm / 1000f;
            // Laengsrichtung der Marke: Flugrichtung, auf die Oberflaeche projiziert
            Vector3 along = Vector3.ProjectOnPlane(shellDir, normal);
            if (along.sqrMagnitude < 1e-6f) along = Vector3.ProjectOnPlane(Random.onUnitSphere, normal);
            along.Normalize();
            float stretch = 1f / Mathf.Max(Mathf.Cos(Mathf.Clamp(angleDeg, 0f, 80f) * Mathf.Deg2Rad), 0.25f);   // flach -> laenglich

            float w, l; Material m;
            switch (kind)
            {
                case Kind.Hole:      w = cal * 2.2f; l = w * Mathf.Min(stretch, 2.5f); m = holeMat; break;            // Loch + aufgerissener Rand
                case Kind.SmallHole: w = cal * 0.9f; l = w * Mathf.Min(stretch, 2f); m = holeMat; break;              // Hohlladung: kleines Loch
                case Kind.Dent:      w = cal * 2.6f; l = w * Mathf.Min(stretch, 2.2f); m = dentMat; break;            // Delle/Aufpraller
                case Kind.Scrape:    w = cal * 1.4f; l = cal * (3f + 3f * Mathf.Min(stretch, 5f)); m = scrapeMat; break; // Schleifspur
                default:             w = size > 0f ? size : 0.6f; l = w; m = scorchMat; break;                       // Russ
            }

            var go = new GameObject("Treffermarke");
            go.layer = parent != null ? parent.gameObject.layer : 0;
            // Lage: knapp ueber der Oberflaeche; lokale z = Laengsrichtung, y = Normale
            go.transform.SetPositionAndRotation(pos + normal * 0.004f, Quaternion.LookRotation(along, normal));
            if (kind == Kind.Scrape) go.transform.position += along * l * 0.45f;   // Spur beginnt am Treffpunkt
            go.transform.localScale = new Vector3(w, 1f, l);
            if (kind == Kind.Scorch) go.transform.rotation = Quaternion.AngleAxis(Random.Range(0f, 360f), normal) * go.transform.rotation;
            if (parent != null) go.transform.SetParent(parent, true);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            all.Enqueue(go);
            while (all.Count > MaxDecals) { var old = all.Dequeue(); if (old != null) Object.Destroy(old); }
        }

        static bool Ensure()
        {
            if (quad != null && holeMat != null) return true;
            var sh = Shader.Find("PanzerSim/Partikel");
            if (sh == null) sh = Resources.Load<Shader>("Shaders/Partikel");
            if (sh == null) return false;
            // Flaches Quadrat in der x-z-Ebene (Normale +y), Mitte im Ursprung, Kantenlaenge 1
            quad = new Mesh { name = "Treffermarke" };
            quad.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) });
            quad.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            quad.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            quad.RecalculateBounds();
            Material mk(Texture2D t, float lit)
            {
                var mat = new Material(sh);
                mat.SetTexture("_BaseMap", t); mat.SetFloat("_SrcBlend", 5); mat.SetFloat("_DstBlend", 10); mat.SetFloat("_Lit", lit);
                mat.renderQueue = 2995;
                return mat;
            }
            holeMat = mk(HoleTex(128), 0.7f);
            dentMat = mk(DentTex(128), 0.85f);
            scrapeMat = mk(ScrapeTex(64, 256), 0.9f);
            scorchMat = mk(ScorchTex(128), 0.6f);
            return true;
        }

        // ---- Texturen ----
        static float Noise(float x, float y, int seed) => Mathf.PerlinNoise(x + seed * 7.13f, y + seed * 3.71f);

        /// <summary>Einschussloch: schwarzer Kern, dunkler Brandring, heller aufgerissener Metallrand, Russ aussen.</summary>
        static Texture2D HoleTex(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float ang = Mathf.Atan2(v, u);
                    float rag = 0.025f * Mathf.Sin(ang * 7f) + 0.07f * (Noise(Mathf.Cos(ang) * 2.5f, Mathf.Sin(ang) * 2.5f, 3) - 0.5f) * 2f;   // unregelmaessig aufgerissen
                    float r = Mathf.Sqrt(u * u + v * v) + rag;
                    Color c;
                    if (r < 0.32f) c = new Color(0.01f, 0.01f, 0.01f, 1f);                                   // Loch
                    else if (r < 0.42f) c = Color.Lerp(new Color(0.55f, 0.52f, 0.48f, 1f), new Color(0.75f, 0.72f, 0.66f, 1f), Noise(u * 8f, v * 8f, 5)); // blanker Rand
                    else if (r < 0.75f) { float k = Mathf.InverseLerp(0.42f, 0.75f, r); c = new Color(0.05f, 0.045f, 0.04f, Mathf.Lerp(0.9f, 0f, k) * (0.7f + 0.3f * Noise(u * 6f, v * 6f, 9))); } // Russ
                    else c = new Color(0, 0, 0, 0);
                    px[y * n + x] = c;
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        /// <summary>Delle/Aufpraller: helle aufgeschmolzene Mitte, dunkler Schlagring, Splitterspritzer.</summary>
        static Texture2D DentTex(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float ang = Mathf.Atan2(v, u);
                    Color c = new Color(0, 0, 0, 0);
                    if (r < 0.28f) c = Color.Lerp(new Color(0.48f, 0.46f, 0.43f, 1f), new Color(0.3f, 0.29f, 0.27f, 1f), r / 0.28f);   // blankgeschlagen
                    else if (r < 0.4f) c = new Color(0.12f, 0.11f, 0.1f, 0.95f);                                                      // Schlagring
                    else if (r < 0.95f)
                    {
                        float rays = Mathf.Pow(Mathf.Abs(Mathf.Sin(ang * 6f + Noise(r * 3f, ang, 2) * 3f)), 12f);            // Spritzer
                        float a = rays * Mathf.InverseLerp(0.95f, 0.4f, r);
                        c = new Color(0.5f, 0.48f, 0.44f, a * 0.8f);
                    }
                    px[y * n + x] = c;
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        /// <summary>Schleifspur eines Abprallers: blanker Metallstreifen, am Anfang tiefer und breiter, laeuft aus.</summary>
        static Texture2D ScrapeTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f;          // quer
                    float v = (y + 0.5f) / h;                   // laengs 0 = Anfang
                    float width = Mathf.Lerp(0.85f, 0.25f, v);
                    float edge = Mathf.Abs(u) / width;
                    float a = Mathf.Clamp01((1f - edge) * 4f) * Mathf.Clamp01((1f - v) * 3f) * Mathf.Clamp01(v * 12f);
                    float groove = Mathf.Clamp01(1f - edge * 1.6f);
                    float streak = 0.75f + 0.25f * Noise(u * 3f, v * 40f, 4);
                    Color metal = new Color(0.78f, 0.75f, 0.7f) * streak;
                    Color dark = new Color(0.15f, 0.14f, 0.12f);
                    var c = Color.Lerp(dark, metal, groove);
                    c.a = a;
                    px[y * w + x] = c;
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        /// <summary>Russfleck einer Explosion: unregelmaessig, aussen weich.</summary>
        static Texture2D ScorchTex(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float nz = Noise(u * 3f, v * 3f, 11) * 0.6f + Noise(u * 9f, v * 9f, 13) * 0.4f;
                    float a = Mathf.Clamp01((1f - r) * 1.6f) * Mathf.Clamp01(nz * 1.8f - 0.2f);
                    px[y * n + x] = new Color(0.03f, 0.03f, 0.03f, a * 0.9f);
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }
    }
}
