using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>Protokoll der Treffer fuer die Anzeige.</summary>
    public static class RangeLog
    {
        public static readonly List<string> Lines = new List<string>();
        public static void Add(string s)
        {
            Lines.Insert(0, s);
            if (Lines.Count > 8) Lines.RemoveAt(Lines.Count - 1);
        }
    }

    /// <summary>Eine fliegende Granate. Bewegt sich selbst, prueft Treffer per Raycast.</summary>
    public class Shell : MonoBehaviour
    {
        ShellData data;
        Vector3 velocity;
        Vector3 origin;
        float life;
        float fuseTimer = -1f;   // >= 0 heisst: Zuender laeuft
        bool insideTank;         // erst nach einem Durchschlag kann die Granate Innenteile treffen
        bool finished;

        GameObject body;          // sichtbares Geschoss
        bool stuck;               // steckt im Boden
        readonly List<string> report = new List<string>();
        readonly Dictionary<TankModule, string> damage = new Dictionary<TankModule, string>();
        readonly HashSet<TankModule> touched = new HashSet<TankModule>();
        static Material bodyMat;

        /// <summary>Wie oft eine Granate im Boden stecken bleibt statt abzuprallen/zu zerspringen (bei steilem Einschlag).</summary>
        public const float StuckChance = 0.3f;

        public static Shell Fire(ShellData d, Vector3 position, Vector3 direction)
        {
            var go = new GameObject("Granate " + d.name);
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(direction);
            var sh = go.AddComponent<Shell>();
            sh.data = d;
            sh.origin = position;
            sh.velocity = direction.normalized * d.muzzleVelocity;
            sh.SetupBody();
            return sh;
        }

        /// <summary>Geschosskoerper: Kapsel in Kaliber-Groesse, etwa 3,5 Kaliber lang.</summary>
        void SetupBody()
        {
            body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Geschoss";
            DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(transform, false);
            float cal = data.caliberMm / 1000f;
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // Kapsel-Achse (Y) nach vorne (Z)
            body.transform.localScale = new Vector3(cal, cal * 1.75f, cal);
            var r = body.GetComponent<Renderer>();
            if (bodyMat == null) bodyMat = GameMap.Mat(new Color(0.25f, 0.22f, 0.15f));
            r.sharedMaterial = bodyMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void ReportDamage(TankModule m, string msg) { damage[m] = msg; }

        void FixedUpdate()
        {
            if (finished) return;
            float dt = Time.fixedDeltaTime;
            life += dt;
            if (life > 15f) { Finish(); return; }

            velocity += Ballistics.Acceleration(data, velocity) * dt;

            Vector3 pos = transform.position;
            float remaining = velocity.magnitude * dt;
            Vector3 dir = velocity.normalized;
            int guard = 0;

            while (remaining > 0f && guard++ < 8 &&
                   Physics.Raycast(pos, dir, out RaycastHit hit, remaining, ArmorPlate.ShotMask, QueryTriggerInteraction.Ignore))
            {
                HitModules(pos, hit.point);
                if (velocity.sqrMagnitude < 900f) { Stop(hit.point); return; }

                remaining -= hit.distance;
                pos = hit.point;

                var plate = hit.collider.GetComponentInParent<ArmorPlate>();
                if (plate == null)
                {
                    HitGround(hit, dir);
                    return;
                }

                var tank = plate.Tank;
                plate.GetArmor(hit, out float plateMm, out ArmorMaterial plateMat, out string plateName);
                HitResult r = Penetration.Evaluate(data, velocity, hit.normal, plateMm, plateMat);
                string who = tank != null ? tank.spec.name + ": " : "";
                report.Add($"{data.name} -> {who}{plateName} ({plateMm:0} mm)\n   {r.note}. {r.penetrationMm:0} mm Durchschlag gegen " +
                           $"{r.effectiveArmorMm:0} mm effektiv, Winkel {r.impactAngleDeg:0}°, {Vector3.Distance(origin, pos):0} m");

                // Spreng- und Hohlladungsgranaten explodieren beim Aufschlag - eigene Wirkung statt Weiterfliegen
                if (data.IsHE) { HitHE(pos, hit.normal, tank, plateMm, plateName, r); return; }
                if (data.IsHEAT && r.outcome != HitOutcome.Ricochet) { HitHEAT(pos, hit.normal, dir, r); return; }

                switch (r.outcome)
                {
                    case HitOutcome.Ricochet:
                        Shell.Marker(pos, Color.yellow, 0.25f);
                        Effects.Impact(pos, hit.normal, dir, data.caliberMm, true, false);
                        velocity = r.exitDirection * r.residualVelocity;
                        pos += hit.normal * 0.02f;
                        break;

                    case HitOutcome.Penetration:
                        Shell.Marker(pos, Color.green, 0.25f);
                        Effects.Impact(pos, hit.normal, dir, data.caliberMm, true, true);
                        velocity = r.exitDirection * r.residualVelocity;
                        // Startpunkt knapp in der Platte: Raycasts ignorieren den Collider, in dem sie beginnen
                        pos += velocity.normalized * 0.005f;
                        if (tank != null)
                        {
                            insideTank = true;
                            // Splitter von Platte und Geschoss
                            float e = Mathf.Clamp01(r.residualVelocity / 600f);
                            int count = Mathf.RoundToInt(8f + data.caliberMm * 0.2f * e + plateMm * 0.1f);
                            float cone = 15f + 15f * Mathf.Clamp01(plateMm / data.caliberMm);
                            Damage.Fragments(pos, velocity.normalized, count, cone, 15f + 35f * e, 4f, ReportDamage);
                        }
                        if (data.HasFiller && fuseTimer < 0f && plateMm >= data.fuseSensitivityMm)
                            fuseTimer = data.fuseDelay;
                        if (r.residualVelocity < 30f) { Stop(pos); return; }
                        break;

                    case HitOutcome.Shatter:
                        Shell.Marker(pos, new Color(1f, 0.4f, 1f), 0.3f);
                        Effects.Impact(pos, hit.normal, dir, data.caliberMm, true, false);
                        Finish();
                        return;

                    default: // kein Durchschlag
                        Shell.Marker(pos, Color.red, 0.25f);
                        Effects.Impact(pos, hit.normal, dir, data.caliberMm, true, false);
                        Finish();
                        return;
                }
                dir = velocity.normalized;
            }

            Vector3 end = pos + dir * Mathf.Max(remaining, 0f);
            HitModules(pos, end);
            pos = end;
            transform.position = pos;
            if (velocity.sqrMagnitude > 1f) transform.rotation = Quaternion.LookRotation(velocity);
            if (velocity.sqrMagnitude < 900f) { Stop(pos); return; }

            if (fuseTimer >= 0f)
            {
                fuseTimer -= dt;
                if (fuseTimer <= 0f) { Stop(pos, true); return; }
            }
        }

        /// <summary>
        /// Einschlag in Boden oder etwas ohne Panzerung. Im Boden entsteht eine kleine Grube;
        /// bei steilem Einschlag bleibt die Granate manchmal einfach stecken (auch Sprenggranaten: Blindgaenger).
        /// </summary>
        void HitGround(RaycastHit hit, Vector3 dir)
        {
            Vector3 pos = hit.point;
            bool isGround = hit.collider is TerrainCollider || (hit.normal.y > 0.5f && hit.collider.attachedRigidbody == null);
            float graze = 90f - Vector3.Angle(-dir, hit.normal);   // 90 = senkrecht hinein, 0 = streifend
            float cal = data.caliberMm / 1000f;

            if (isGround)
            {
                float radius = Mathf.Clamp(cal * 6f, 0.3f, 0.7f);   // 88 mm -> ca. 0,5 m
                if (data.HasFiller) radius += Mathf.Clamp(Mathf.Pow(data.fillerKg * 1000f, 1f / 3f) * 0.08f, 0.1f, 0.6f);

                if (!data.ImpactFuze && graze > 12f && Random.value < StuckChance)
                {
                    // Granate steckt im Boden: halb eingegraben, kein Zuender
                    Effects.Impact(pos, hit.normal, dir, data.caliberMm, false, false);
                    Effects.Crater(pos, hit.normal, radius * 0.6f, hit.collider.transform);
                    stuck = true;
                    transform.position = pos + dir * cal * 1.6f;
                    transform.rotation = Quaternion.LookRotation(dir);
                    transform.SetParent(hit.collider.transform, true);
                    report.Add($"{data.name}: steckt im Boden ({Vector3.Distance(origin, pos):0} m){(data.HasFiller ? ", Blindgaenger" : "")}");
                    Finish();
                    return;
                }
                Effects.Crater(pos, hit.normal, radius, hit.collider.transform);
            }

            if (!data.HasFiller) Effects.Impact(pos, hit.normal, dir, data.caliberMm, false, false);
            Stop(pos, data.HasFiller);
        }

        /// <summary>
        /// Sprenggranate trifft Panzerung: Explosion auf der Oberflaeche. Duenne Platte -> Druckwelle und Splitter innen;
        /// dicke Platte -> hoechstens Abplatzer innen; Treffer an Rohr/Blende beschaedigen die Kanone.
        /// </summary>
        void HitHE(Vector3 pos, Vector3 normal, TankController tank, float plateMm, string plateName, HitResult r)
        {
            Effects.Explosion(pos, data.fillerKg, false);
            Shell.Marker(pos, new Color(1f, 0.55f, 0.1f), 0.3f);
            if (tank != null)
            {
                Vector3 inside = pos - normal * (plateMm / 1000f + 0.05f);   // knapp hinter der Platte
                if (r.outcome == HitOutcome.Penetration)
                {
                    Damage.Explosion(inside, data.fillerKg * 0.5f, ReportDamage);   // ein Teil der Wucht geht nach innen
                    Damage.Fragments(inside, -normal, 24, 55f, 30f, 3f, ReportDamage);
                    report.Add("   Druckwelle und Splitter im Inneren");
                }
                else if (plateMm < r.penetrationMm * 2f)
                {
                    Damage.Fragments(inside, -normal, 6, 40f, 12f, 1.5f, ReportDamage);   // Abplatzer von der Innenseite
                    report.Add("   Abplatzer an der Innenseite");
                }
                // Kanone getroffen (Rohr oder Blende): Verschluss/Rohr beschaedigt
                bool gunHit = plateName.Contains("Blende") || plateName.Contains("Kanone") || plateName.Contains("Rohr");
                if (!gunHit && tank.parts.gunPivot != null && tank.parts.muzzle != null)
                {
                    Vector3 a = tank.parts.gunPivot.position, b = tank.parts.muzzle.position;
                    Vector3 ab = b - a;
                    float t = Mathf.Clamp01(Vector3.Dot(pos - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.001f));
                    gunHit = Vector3.Distance(pos, a + ab * t) < 0.35f;
                }
                if (gunHit)
                    foreach (var m in tank.parts.modules)
                        if (m != null && m.type == ModuleType.Breech)
                        {
                            string msg = m.ApplyDamage(35f);
                            if (msg != null) ReportDamage(m, msg + " (Sprengtreffer an der Kanone)");
                        }
            }
            Finish();
        }

        /// <summary>
        /// Hohlladung trifft Panzerung: Explosion aussen, bei Durchschlag ein Metallstrahl von hoechstens 2,5 m Reichweite.
        /// Weitere Platten auf dem Weg (z. B. erst Kette, dann Wanne) kosten Durchschlag; Luftspalte lassen den Strahl zerfasern.
        /// Innen trifft der Strahl nur, was in seiner Linie liegt, dazu ein schmaler Splitterkegel.
        /// </summary>
        void HitHEAT(Vector3 pos, Vector3 normal, Vector3 dir, HitResult r)
        {
            Effects.Explosion(pos, data.fillerKg * 0.6f, false);
            if (r.outcome != HitOutcome.Penetration)
            {
                Shell.Marker(pos, Color.red, 0.25f);
                Finish();
                return;
            }
            Shell.Marker(pos, Color.green, 0.25f);
            float reserve = r.penetrationMm - r.effectiveArmorMm;
            float reach = 2.5f;
            Vector3 exit = pos + dir * 0.01f;
            for (int i = 0; i < 6; i++)
            {
                if (!Physics.Raycast(exit, dir, out RaycastHit h, reach, ArmorPlate.ShotMask, QueryTriggerInteraction.Ignore)) break;
                var plate = h.collider.GetComponentInParent<ArmorPlate>();
                if (plate == null) { reach = h.distance; break; }          // Boden o. ae.: Strahl endet dort
                if (h.distance > 0.05f) reserve *= 1f - Mathf.Clamp01(h.distance / 2.5f);   // Luftspalt: Strahl zerfasert
                plate.GetArmor(h, out float mm, out ArmorMaterial mat, out string nm);
                float ang = Vector3.Angle(-dir, h.normal);
                float eff = mm / Mathf.Max(Mathf.Cos(ang * Mathf.Deg2Rad), 0.05f);
                if (reserve < eff)
                {
                    report.Add($"   Strahl bleibt in {nm} stecken ({mm:0} mm, effektiv {eff:0} mm, Rest {reserve:0} mm)");
                    reach = h.distance;
                    break;
                }
                reserve -= eff;
                reach -= h.distance;
                exit = h.point + dir * 0.01f;
                report.Add($"   Strahl durchschlaegt auch {nm} ({eff:0} mm effektiv, Rest {reserve:0} mm)");
                if (reach <= 0.05f) break;
            }
            // Innenwirkung: der Strahl selbst + schmaler Kegel aus Splittern (stoppt an der naechsten Panzerung)
            float len = Mathf.Max(reach, 0.3f);
            Damage.Fragments(exit, dir, 1, 0f, 140f, len, ReportDamage);
            Damage.Fragments(exit, dir, 10, 9f, 35f + reserve * 0.3f, len, ReportDamage);
            Finish();
        }

        /// <summary>Innenteile entlang eines Wegstuecks treffen (nur nach Durchschlag).</summary>
        void HitModules(Vector3 a, Vector3 b)
        {
            if (!insideTank) return;
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.0001f) return;
            var hits = Physics.RaycastAll(a, d / len, len, ~0, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
            foreach (var h in hits)
            {
                if (!h.collider.isTrigger) continue;
                var m = h.collider.GetComponent<TankModule>();
                if (m == null || touched.Contains(m)) continue;
                touched.Add(m);
                float dmg = 50f + 250f * Mathf.Clamp01(velocity.magnitude / 300f); // direkter Granatentreffer
                string msg = m.ApplyDamage(dmg);
                if (msg != null) ReportDamage(m, msg);
                velocity *= 1f - m.absorb;
                if (velocity.sqrMagnitude < 900f) return;
            }
        }

        /// <summary>Granate bleibt stecken / schlaegt ein. Bei scharfem Zuender: Explosion.</summary>
        void Stop(Vector3 pos, bool explode = false)
        {
            if (explode || fuseTimer >= 0f) Explode(pos);
            Finish();
        }

        void Explode(Vector3 pos)
        {
            bool onGround = !insideTank && Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out RaycastHit g, 1.5f, ArmorPlate.ShotMask, QueryTriggerInteraction.Ignore)
                            && g.collider.GetComponentInParent<ArmorPlate>() == null;
            Effects.Explosion(pos, data.fillerKg, onGround);
            report.Add($"   Explosion ({data.fillerKg * 1000f:0} g Sprengstoff)");
            Damage.Explosion(pos, data.fillerKg, ReportDamage);
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            if (damage.Count > 0) report.Add("   Schaden: " + string.Join(", ", damage.Values));
            if (report.Count > 0) RangeLog.Add(string.Join("\n", report));
            enabled = false;
            if (stuck) { Destroy(gameObject, 120f); return; }   // steckende Granate bleibt eine Weile liegen
            if (body != null) body.SetActive(false);
            Destroy(gameObject);
        }

        public static void Marker(Vector3 pos, Color c, float size)
        {
            var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m.name = "Treffer";
            DestroyImmediate(m.GetComponent<Collider>()); // sofort weg, damit keine Granate daran haengen bleibt
            m.transform.position = pos;
            m.transform.localScale = Vector3.one * size;
            m.GetComponent<Renderer>().sharedMaterial = GameMap.Mat(c);
            Destroy(m, 60f);
        }
    }
}
