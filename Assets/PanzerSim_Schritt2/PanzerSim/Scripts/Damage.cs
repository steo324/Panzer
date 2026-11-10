using System;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>Splitter und Explosionen im Inneren eines Panzers.</summary>
    public static class Damage
    {
        public static bool ShowFragments = true;   // Splitter als kleine schwarze Partikel zeigen

        /// <summary>
        /// Splitterkegel: count Strahlen in einem Kegel um dir. Splitter treffen Innenteile
        /// und werden von Panzerplatten gestoppt.
        /// </summary>
        public static void Fragments(Vector3 origin, Vector3 dir, int count, float coneDeg, float damage, float range,
                                     Action<TankModule, string> report)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 d = RandomInCone(dir, coneDeg);
                RaycastHit[] hits = Physics.RaycastAll(origin, d, range, ArmorPlate.ShotMask, QueryTriggerInteraction.Collide);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                float dmg = damage;
                float end = range;
                foreach (var h in hits)
                {
                    if (h.collider.isTrigger)
                    {
                        var m = h.collider.GetComponent<TankModule>();
                        if (m == null || m.Destroyed) continue;
                        float f = dmg * (1f - 0.5f * h.distance / range);
                        string msg = m.ApplyDamage(f);
                        if (msg != null) report?.Invoke(m, msg);
                        dmg *= m.IsCrew ? 0.5f : 0.2f; // Koerper/Technik bremst den Splitter
                        if (dmg < 3f) { end = h.distance; break; }
                    }
                    else
                    {
                        end = h.distance; // Panzerplatte (oder Boden) stoppt den Splitter
                        break;
                    }
                }
                if (ShowFragments) Effects.Spall(origin, d, end);
            }
        }

        /// <summary>Explosion der Sprengladung: Druckwelle + Splitter in alle Richtungen.</summary>
        public static void Explosion(Vector3 pos, float fillerKg, Action<TankModule, string> report)
        {
            float radius = 0.8f + 2.2f * Mathf.Pow(fillerKg, 1f / 3f);
            float blast = 60f + fillerKg * 2000f;
            foreach (var c in Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Collide))
            {
                if (!c.isTrigger) continue;
                var m = c.GetComponent<TankModule>();
                if (m == null || m.Destroyed) continue;
                float d = Vector3.Distance(pos, c.ClosestPoint(pos));
                string msg = m.ApplyDamage(blast * (1f - d / radius));
                if (msg != null) report?.Invoke(m, msg);
            }
            Fragments(pos, Vector3.forward, 40, 180f, 30f, 4f, report);
        }

        static Vector3 RandomInCone(Vector3 dir, float coneDeg)
        {
            dir = dir.normalized;
            Vector3 perp = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            perp = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, 360f), dir) * perp;
            // sqrt sorgt fuer gleichmaessigere Verteilung im Kegel
            float a = coneDeg * Mathf.Sqrt(UnityEngine.Random.value);
            return Quaternion.AngleAxis(a, perp) * dir;
        }
    }
}
