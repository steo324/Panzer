using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Zerstoerbare Haeuser: MeshKit meldet jedes Haus (Block-Mesh, Vertexbereich, Grundflaeche). Treffer und Sprengwirkung
    /// zehren an der Haltbarkeit (kleine Scheunen halten wenig, grosse Hallen viel); bei 0 stuerzt das Haus ein:
    /// die Vertices sacken zu einem flachen Truemmerhaufen zusammen, der Kollisionskoerper des Blocks wird neu gebacken
    /// (man kann ueber die Truemmer fahren), Staubwolke und Rauch.
    /// </summary>
    public static class Ruins
    {
        class House
        {
            public Mesh mesh; public MeshCollider col; public int v0, v1;
            public Vector3 pos, size; public float yaw, hp, maxHp; public bool down;
        }
        static readonly List<House> houses = new List<House>();
        static readonly Dictionary<Mesh, List<House>> byMesh = new Dictionary<Mesh, List<House>>();
        public static int Collapsed { get; private set; }
        public static int Count => houses.Count;

        public static void Clear() { houses.Clear(); byMesh.Clear(); simpleHp.Clear(); Collapsed = 0; }

        public static void Register(Mesh m, MeshCollider col, int v0, int v1, Vector3 pos, Vector3 size, float yaw)
        {
            if (m == null || v1 <= v0) return;
            float vol = size.x * size.y * size.z;
            var h = new House { mesh = m, col = col, v0 = v0, v1 = v1, pos = pos, size = size, yaw = yaw, maxHp = Mathf.Clamp(size.x * size.z * 2.5f, 150f, 2500f) };
            h.hp = h.maxHp;
            houses.Add(h);
            if (!byMesh.TryGetValue(m, out var l)) { l = new List<House>(); byMesh[m] = l; }
            l.Add(h);
        }

        static bool Inside(House h, Vector3 p, float pad)
        {
            Vector3 l = Quaternion.Euler(0f, -h.yaw, 0f) * (p - h.pos);
            return Mathf.Abs(l.x) <= h.size.x * 0.5f + pad && Mathf.Abs(l.z) <= h.size.z * 0.5f + pad && l.y > -2f && l.y < h.size.y + pad;
        }

        static House Find(Collider c, Vector3 p, float pad)
        {
            if (houses.Count == 0) return null;
            if (c is MeshCollider mc && mc.sharedMesh != null && byMesh.TryGetValue(mc.sharedMesh, out var list))
            { foreach (var h in list) if (!h.down && Inside(h, p, pad)) return h; return null; }
            return null;
        }

        /// <summary>Granate trifft ein Haus (Shell: Einschlag ohne Panzerung).</summary>
        public static void Hit(Collider c, Vector3 p, ShellData d)
        {
            if (!NetSync.MayApplyDamage) return;
            var h = Find(c, p, 1.5f);
            if (h == null) { HitSimple(c, p, d); return; }
            float dmg = d.HasFiller ? 15f + d.fillerKg * 90f : d.caliberMm * 0.25f;
            Damage(h, dmg);
        }

        /// <summary>Sprengwirkung in der Naehe (Artillerie, Bomben, grosse Sprenggranaten).</summary>
        public static void Splash(Vector3 p, float fillerKg)
        {
            if (!NetSync.MayApplyDamage || fillerKg < 0.5f) return;
            float r = SoftTargets.Radius(fillerKg) * 0.5f;
            foreach (var h in houses)
            {
                if (h.down) continue;
                if ((new Vector2(h.pos.x - p.x, h.pos.z - p.z)).magnitude > r + Mathf.Max(h.size.x, h.size.z)) continue;
                if (!Inside(h, p, r)) continue;
                Damage(h, fillerKg * 70f * (1f - Mathf.Clamp01(Vector3.Distance(p, h.pos) / (r + h.size.magnitude))));
            }
        }

        static void Damage(House h, float dmg)
        {
            h.hp -= dmg;
            if (h.hp <= 0f) Collapse(h);
        }

        static void Collapse(House h)
        {
            h.down = true; Collapsed++;
            var v = h.mesh.vertices;
            var rng = new System.Random(h.v0 * 7919);
            float ground = h.pos.y - 0.5f;
            for (int i = h.v0; i < h.v1 && i < v.Length; i++)
            {
                // alles sackt auf einen flachen Haufen (bis ~1/5 der Hoehe), etwas nach aussen verschoben, zerkluefted
                Vector3 p = v[i];
                float hgt = Mathf.Max(0f, p.y - ground);
                Vector3 outw = new Vector3(p.x - h.pos.x, 0f, p.z - h.pos.z) * 0.12f;
                p.y = ground + hgt * 0.18f + (float)rng.NextDouble() * 0.9f;
                p += outw + new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f) * 0.8f;
                v[i] = p;
            }
            h.mesh.vertices = v;
            h.mesh.RecalculateNormals();
            h.mesh.RecalculateBounds();
            if (h.col != null) { h.col.sharedMesh = null; h.col.sharedMesh = h.mesh; }   // Kollision neu backen
            if (!NetSync.Headless)
            {
                Vector3 c = h.pos + Vector3.up * h.size.y * 0.3f;
                Effects.Explosion(c, 2f, true);
                for (int k = 0; k < 10; k++)
                    Effects.SmokeOne(c + new Vector3(Random.Range(-1f, 1f) * h.size.x * 0.5f, Random.Range(0f, 3f), Random.Range(-1f, 1f) * h.size.z * 0.5f),
                                     Vector3.up * 1.2f, Mathf.Max(h.size.x, h.size.z) * 0.6f, Random.Range(6f, 10f), 0.62f, 0.85f);
                Sfx.GroundHit(c);
            }
            RangeLog.Add("Haus eingestuerzt");
        }

        // ---- Haeuser aus Einzelteilen (Weite Ebene, Maps.cs: Objekt "Haus" mit Waenden und Dach) ----
        static readonly Dictionary<Transform, float> simpleHp = new Dictionary<Transform, float>();
        static void HitSimple(Collider c, Vector3 p, ShellData d)
        {
            if (TankController.BenchLogFell) Debug.Log($"RUINEN-TREFFER {c.name} (Eltern {(c.transform.parent != null ? c.transform.parent.name : "-")}), {d.name}");
            Transform t = c.transform;
            while (t != null && t.name != "Haus") t = t.parent;
            if (t == null) return;
            if (!simpleHp.TryGetValue(t, out float hp))
            {
                var b = new Bounds(t.position, Vector3.zero); foreach (var r in t.GetComponentsInChildren<Collider>()) b.Encapsulate(r.bounds);
                hp = Mathf.Clamp(b.size.x * b.size.z * 2.5f, 150f, 2500f);   // nach Grundflaeche
            }
            if (hp <= 0f) return;
            hp -= d.HasFiller ? 15f + d.fillerKg * 90f : d.caliberMm * 0.25f;
            simpleHp[t] = hp;
            if (hp > 0f) return;
            // Einsturz: auf einen niedrigen Truemmerhaufen stauchen, leicht verkippt, Staub
            Collapsed++;
            Vector3 basePos = t.position;
            t.localScale = new Vector3(t.localScale.x * 1.08f, t.localScale.y * 0.22f, t.localScale.z * 1.08f);
            t.rotation *= Quaternion.Euler(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
            foreach (var r in t.GetComponentsInChildren<Renderer>()) { var m = r.material; m.color *= 0.55f; }
            if (!NetSync.Headless)
            {
                Effects.Explosion(basePos + Vector3.up * 2f, 2f, true);
                for (int k = 0; k < 8; k++) Effects.SmokeOne(basePos + Random.insideUnitSphere * 5f + Vector3.up * 2f, Vector3.up, 6f, Random.Range(6f, 9f), 0.62f, 0.85f);
                Sfx.GroundHit(basePos);
            }
            RangeLog.Add("Haus eingestuerzt");
        }

        public static string BenchText => $"Haeuser {houses.Count}, eingestuerzt {Collapsed}";

        /// <summary>Test: Haus in der Naehe eines Punktes (Mitte, Groesse).</summary>
        public static bool Nearest(Vector3 p, out Vector3 pos, out Vector3 size, out float yaw)
        {
            pos = size = Vector3.zero; yaw = 0f; float bd = float.MaxValue;
            foreach (var h in houses) { if (h.down) continue; float d = (h.pos - p).sqrMagnitude; if (d < bd) { bd = d; pos = h.pos; size = h.size; yaw = h.yaw; } }
            return bd < float.MaxValue;
        }
    }
}
