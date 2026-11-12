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
        public static void Explosion(Vector3 pos, float fillerKg, Action<TankModule, string> report, bool outside = false)
        {
            float radius = 0.8f + 2.2f * Mathf.Pow(fillerKg, 1f / 3f);
            float blast = 60f + fillerKg * 2000f;
            float shield = Penetration.HEBlastMm(fillerKg);   // dickere Panzerung dazwischen haelt die Druckwelle auf
            foreach (var c in Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Collide))
            {
                if (!c.isTrigger) continue;
                var m = c.GetComponent<TankModule>();
                if (m == null || m.Destroyed) continue;
                Vector3 cp = c.ClosestPoint(pos);
                // Liegt Panzerung zwischen Explosion und Innenteil? (z. B. Sprenggranate neben dem Panzer)
                if (Physics.Linecast(pos, cp, out RaycastHit wall, ArmorPlate.ShotMask, QueryTriggerInteraction.Ignore))
                {
                    var plate = wall.collider.GetComponentInParent<ArmorPlate>();
                    if (plate != null)
                    {
                        plate.GetArmor(wall, out float mm, out _, out _);
                        // Druckwelle schlaegt nur bei (fast) Kontakt durch Panzerung: mit Abstand zur Platte faellt das schnell ab
                        float contact = Mathf.Clamp01(1f - wall.distance / (0.6f * Mathf.Pow(fillerKg, 1f / 3f)));
                        if (mm > shield * contact) continue;
                    }
                    else if (outside) continue;   // Boden o. ae. dazwischen
                }
                else if (outside) continue;   // Explosion draussen, keine Platte gefunden (Spalt am Turmring o. ae.): Wanne schirmt ab
                float d = Vector3.Distance(pos, cp);
                string msg = m.ApplyDamage(blast * (1f - d / radius));
                if (msg != null) report?.Invoke(m, msg);
            }
            Fragments(pos, Vector3.forward, 40, 180f, 30f, 4f, report);
        }

        /// <summary>
        /// Erschuetterung der Besatzung durch eine Explosion an/neben dem Panzer (Druckstoss, Koerperschall durch die Panzerung).
        /// Gerechnet mit dem skalierten Abstand Z = Abstand / Sprengstoff^(1/3) (wie in der Sprengtechnik):
        /// 155 mm (10 kg) direkt am Kampfraum -> bewusstlos bis ~40 s; 7,5-cm-Sprenggranate (0,7 kg) -> hoechstens kurz benommen.
        /// outside: 1 = Explosion im Inneren (Durchschlag), ~0,6 = an der Panzerung ohne Durchschlag, ~0,4 = Nahtreffer daneben.
        /// heat: Hohlladung - die Wucht geht vor allem in den Strahl, Erschuetterung deutlich schwaecher.
        /// </summary>
        public static void Concussion(TankController tank, Vector3 pos, float fillerKg, float outside, bool heat, Action<TankModule, string> report)
        {
            if (tank == null || fillerKg <= 0f || !NetSync.MayApplyDamage) return;
            float w = Mathf.Pow(fillerKg, 1f / 3f);
            float sizeFactor = Mathf.Sqrt(Mathf.Clamp01(fillerKg / 10f));   // kleine Ladungen viel schwaecher (0,9 kg -> 0,3)
            foreach (var m in tank.parts.modules)
            {
                if (m == null || !m.IsCrew || m.Destroyed) continue;
                float z = Mathf.Max(Vector3.Distance(pos, m.transform.position), 0.4f) / w;
                float sev = Mathf.Clamp01((3.0f - z) / 2.2f) * sizeFactor * outside * (heat ? 0.5f : 1f);
                string msg = m.ApplyConcussion(sev);
                if (msg != null) report?.Invoke(m, msg);
            }
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
