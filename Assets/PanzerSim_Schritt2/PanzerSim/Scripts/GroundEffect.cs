using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Boden mit Wirkung: Bodenart (Karte) + Wetter + Wasser ergeben Hoechsttempo, Haftung und Rollwiderstand der Ketten.
    ///  - Weg/Strasse: voll. Wiese: etwas langsamer. Acker: deutlich langsamer.
    ///  - Regen: Acker wird Schlamm (sehr zaeh, rutschig), Wiese weich.
    ///  - Schnee: weniger Haftung ueberall (rutscht in Kurven und am Hang), etwas langsamer.
    ///  - Furt: Wasser bis 1,4 m Tiefe bremst stark, tieferes Wasser fast bis zum Stillstand (Motor saeuft nicht ab).
    /// Die Wasserflaechen werden nach dem Kartenbau aus den Wasser-Meshes ("Wasser") in ein 4-m-Raster gelesen.
    /// </summary>
    public static class GroundEffect
    {
        public struct Grip { public float speed, grip, drag; public string label; }
        static readonly Dictionary<long, float> water = new Dictionary<long, float>();
        const float Cell = 4f;
        static GameMap map;

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>Nach dem Kartenbau: Wasserspiegel aus allen Objekten "Wasser" rastern.</summary>
        public static void OnMapBuilt(GameMap m, Transform root)
        {
            map = m;
            water.Clear();
            if (root == null) return;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null || !mf.gameObject.name.StartsWith("Wasser")) continue;
                var mesh = mf.sharedMesh;
                if (!mesh.isReadable) continue;
                var v = mesh.vertices; var t = mesh.triangles; var tr = mf.transform;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = tr.TransformPoint(v[t[i]]), b = tr.TransformPoint(v[t[i + 1]]), c = tr.TransformPoint(v[t[i + 2]]);
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / Cell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / Cell);
                    int z0 = Mathf.FloorToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) / Cell), z1 = Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) / Cell);
                    if ((x1 - x0) * (z1 - z0) > 20000) continue;   // riesige Flaeche (See als ein Dreieck): ueberspringen
                    for (int x = x0; x <= x1; x++)
                        for (int z = z0; z <= z1; z++)
                        {
                            Vector2 p = new Vector2((x + 0.5f) * Cell, (z + 0.5f) * Cell);
                            if (!InTri(p, a, b, c, out float y)) continue;
                            long k = Key(x, z);
                            if (!water.TryGetValue(k, out float old) || y > old) water[k] = y;
                        }
                }
            }
            if (water.Count > 0) Debug.Log($"[Boden] Wasserflaechen: {water.Count} Zellen ({water.Count * Cell * Cell / 10000f:0.0} ha)");
        }

        static bool InTri(Vector2 p, Vector3 a, Vector3 b, Vector3 c, out float y)
        {
            y = 0f;
            Vector2 A = new Vector2(a.x, a.z), B = new Vector2(b.x, b.z), C = new Vector2(c.x, c.z);
            float d = (B.y - C.y) * (A.x - C.x) + (C.x - B.x) * (A.y - C.y);
            if (Mathf.Abs(d) < 1e-6f) return false;
            float l1 = ((B.y - C.y) * (p.x - C.x) + (C.x - B.x) * (p.y - C.y)) / d;
            float l2 = ((C.y - A.y) * (p.x - C.x) + (A.x - C.x) * (p.y - C.y)) / d;
            float l3 = 1f - l1 - l2;
            if (l1 < -0.02f || l2 < -0.02f || l3 < -0.02f) return false;
            y = l1 * a.y + l2 * b.y + l3 * c.y;
            return true;
        }

        /// <summary>Test: eine Stelle mit 0,5-1,3 m Wasser (Furt) suchen.</summary>
        public static bool FindFord(out Vector3 p)
        {
            p = Vector3.zero;
            foreach (var kv in water)
            {
                int x = (int)(kv.Key >> 32), z = (int)(kv.Key & 0xffffffff);
                var q = new Vector3((x + 0.5f) * Cell, 0f, (z + 0.5f) * Cell);
                float d = WaterDepth(q);
                if (d > 0.5f && d < 1.3f) { p = new Vector3(q.x, map.GroundY(q.x, q.z), q.z); return true; }
            }
            return false;
        }

        /// <summary>Wassertiefe an der Stelle (0 = trocken).</summary>
        public static float WaterDepth(Vector3 p)
        {
            if (water.Count == 0 || map == null) return 0f;
            if (!water.TryGetValue(Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell)), out float level)) return 0f;
            return Mathf.Max(0f, level - map.GroundY(p.x, p.z));
        }

        /// <summary>Wirkung des Bodens unter diesem Punkt.</summary>
        public static Grip At(Vector3 p)
        {
            var g = new Grip { speed = 1f, grip = 1f, drag = 0f, label = "" };
            if (map == null) return g;
            int s = map.SurfaceAt(p.x, p.z);
            bool rain = Weather.Current == Weather.Rain, snow = Weather.Current == Weather.Snow;
            switch (s)
            {
                case GroundTracks.Weg: case GroundTracks.Hart: g.label = "Weg"; break;
                case GroundTracks.Acker:
                    if (rain) { g.speed = 0.5f; g.grip = 0.6f; g.drag = 0.6f; g.label = "Schlamm"; }
                    else { g.speed = 0.8f; g.grip = 0.9f; g.drag = 0.15f; g.label = "Acker"; }
                    break;
                default:
                    if (rain) { g.speed = 0.8f; g.grip = 0.75f; g.drag = 0.2f; g.label = "nasse Wiese"; }
                    else { g.speed = 0.92f; g.label = "Wiese"; }
                    break;
            }
            if (snow)
            {
                g.grip *= 0.55f; g.speed *= s == GroundTracks.Weg || s == GroundTracks.Hart ? 0.85f : 0.75f; g.drag += 0.1f;
                g.label = "Schnee" + (g.label.Length > 0 ? $" ({g.label})" : "");
            }
            float depth = WaterDepth(p);
            if (depth > 0.15f)
            {
                if (depth < 1.4f) { g.speed *= Mathf.Lerp(0.7f, 0.35f, depth / 1.4f); g.drag += 0.4f + depth * 0.5f; g.grip *= 0.8f; g.label = $"Furt ({depth:0.0} m tief)"; }
                else { g.speed *= 0.12f; g.drag += 2f; g.grip *= 0.6f; g.label = $"tiefes Wasser ({depth:0.0} m)"; }
            }
            return g;
        }
    }
}
