using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// KI fuer gegnerische Panzer (Wellen-Modus). Faehrt auf Kampfentfernung heran (mit seitlichem Versatz, damit nicht alle
    /// in einer Reihe kommen), haelt dann an, dreht die Front zum Ziel, schaetzt die Entfernung (anfangs mit Fehler, wird mit
    /// jedem Schuss genauer - "eingabeln"), haelt bei bewegten Zielen vor und schiesst nur bei freier Sicht und ruhigem Rohr.
    /// Hindernisse vorne: ausweichen; festgefahren: kurz zurueck.
    /// </summary>
    public class TankAI : MonoBehaviour
    {
        public TankController tc;
        public float skill = 0.5f;           // 0 = Anfaenger, 1 = Profi (Genauigkeit, Reaktion)
        public float mapExtent = 1000f;

        TankController target;
        float engageDist, flank, searchT, losT, reactT, settleT, stuckT, reverseT, avoidT, avoidDir;
        bool visible;
        float rangeErr;                      // relativer Fehler der geschaetzten Entfernung (wird pro Schuss kleiner)
        Vector3 aimJitter;
        int shotsAtTarget;
        float lastReload;
        float hurtT, lastHp;
        public static int SmokeUsed;

        /// <summary>Auftrag (Eroberung/Verteidigung): Punkt, den die KI anfahren und halten soll, solange kein Gegner in Kampfentfernung ist.</summary>
        public System.Func<TankAI, Vector3?> objective;
        /// <summary>Radius, in dem die KI am Auftragspunkt anhaelt.</summary>
        public float holdRadius = 18f;
        /// <summary>Hoechste Entfernung, auf die die KI Gegner sucht (0 = ganze Karte).</summary>
        public float searchRange;
        public TankController Target => target;

        public static TankAI Attach(TankController t, float skill, float extent)
        {
            var ai = t.gameObject.AddComponent<TankAI>();
            ai.tc = t; ai.skill = Mathf.Clamp01(skill); ai.mapExtent = extent;
            ai.engageDist = Random.Range(550f, 950f);
            ai.flank = Random.Range(-1f, 1f);
            ai.reactT = Mathf.Lerp(4f, 1.5f, ai.skill);
            t.ai = ai;
            return ai;
        }

        public void Tick(float dt)
        {
            if (tc == null || tc.destroyed) return;
            searchT -= dt; losT -= dt;
            if (searchT <= 0f || target == null || target.destroyed) { searchT = 1.5f; FindTarget(); }
            Vector3? obj = objective != null ? objective(this) : null;
            if (target == null)
            {
                if (obj.HasValue) DriveTo(dt, obj.Value, false);
                else tc.AiControl(0f, 0f, true);
                return;
            }

            Vector3 me = tc.transform.position, tp = target.transform.position;
            Vector3 to = tp - me; to.y = 0f;
            float dist = to.magnitude;

            if (losT <= 0f) { losT = 0.4f; bool v = LineOfSight(); if (v && !visible) reactT = Mathf.Lerp(3.5f, 1.2f, skill) * Random.Range(0.8f, 1.2f); visible = v; }
            if (visible) reactT -= dt;

            // getroffen (Schaden an Besatzung/Technik): Nebel werfen und zuruecksetzen
            hurtT -= dt;
            if (hurtT <= 0f)
            {
                hurtT = 1f;
                float hp = 0f; foreach (var m in tc.parts.modules) if (m != null) hp += m.hp;
                if (lastHp > 0f && hp < lastHp - 5f && visible && Random.value < 0.6f && Smoke.Discharge(tc)) { reverseT = 3.5f; avoidDir = Random.value < 0.5f ? -0.4f : 0.4f; SmokeUsed++; }
                lastHp = hp;
            }
            if (obj.HasValue && !(visible && dist < engageDist)) DriveTo(dt, obj.Value, true);
            else Drive(dt, to, dist);
            Aim(dt, dist);
        }

        void FindTarget()
        {
            TankController best = null; float bd = float.MaxValue;
            foreach (var t in FindObjectsByType<TankController>(FindObjectsSortMode.None))
            {
                if (t == tc || t.destroyed || t.team == tc.team || t.team == TankController.TeamTarget) continue;
                if (Smoke.Hides(tc.transform.position + Vector3.up * 2.5f, t.transform.position + Vector3.up * 1.5f)) continue;   // im Nebel nicht zu sehen
                float d = (t.transform.position - tc.transform.position).sqrMagnitude;
                if (searchRange > 0f && d > searchRange * searchRange) continue;
                if (d < bd) { bd = d; best = t; }
            }
            if (best != target) { target = best; shotsAtTarget = 0; rangeErr = Random.Range(-1f, 1f) * Mathf.Lerp(0.14f, 0.05f, skill); }
        }

        Vector3 TargetPoint() => target.transform.TransformPoint(0f, target.spec.clearance + target.spec.hullHeight * 0.65f, 0f);

        bool LineOfSight()
        {
            Vector3 from = tc.parts.muzzle != null ? tc.parts.muzzle.position : tc.transform.position + Vector3.up * 2.5f;
            Vector3 d = TargetPoint() - from;
            if (Smoke.Hides(from, TargetPoint())) return false;   // Nebel verdeckt
            if (!Weather.InRange(tc, (TargetPoint() - from).magnitude)) return false;   // zu dunkel / zu dunstig
            if (!tc.RaycastIgnoringSelf(from, d.normalized, d.magnitude + 2f, out RaycastHit hit)) return true;
            return BelongsTo(hit.collider, target) || hit.distance > d.magnitude - 4f;   // am Ziel oder direkt davor (Anbauteil, Schuerze)
        }

        /// <summary>Gehoert der Collider zum Panzer? (Panzerung aus der Modellform haengt nicht unter dem Panzer-Objekt.)</summary>
        static bool BelongsTo(Collider c, TankController t)
        {
            if (c.GetComponentInParent<TankController>() == t) return true;
            var plate = c.GetComponentInParent<ArmorPlate>();
            if (plate != null && plate.Tank == t) return true;
            var part = c.GetComponentInParent<DestructiblePart>();
            return part != null && part.owner == t;
        }

        public string DebugText => $"Sicht {visible}, Reaktion {reactT:0.0}, Ruhe {settleT:0.0}";

        // ---------------- Fahren ----------------
        void Drive(float dt, Vector3 to, float dist)
        {
            bool inRange = visible && dist < engageDist;
            Vector3 fwd = tc.transform.forward; fwd.y = 0f; fwd.Normalize();

            // Festgefahren: kurz rueckwaerts mit eingeschlagener Lenkung
            if (reverseT > 0f) { reverseT -= dt; tc.AiControl(-0.8f, avoidDir, false); return; }

            if (inRange)
            {
                // Stehen und die Front (staerkste Panzerung) zum Ziel drehen
                float a = Vector3.SignedAngle(fwd, to, Vector3.up);
                tc.AiControl(0f, Mathf.Abs(a) > 20f ? Mathf.Sign(a) * 0.7f : 0f, Mathf.Abs(a) <= 20f);
                stuckT = 0f;
                return;
            }

            // Ziel mit seitlichem Versatz anfahren (je naeher, desto direkter)
            Vector3 side = Vector3.Cross(Vector3.up, to.normalized);
            Vector3 goal = target.transform.position + side * flank * Mathf.Clamp(dist * 0.35f, 0f, 350f);
            // im Kartenrand bleiben
            float lim = mapExtent - 80f;
            goal.x = Mathf.Clamp(goal.x, -lim, lim); goal.z = Mathf.Clamp(goal.z, -lim, lim);
            Vector3 g = goal - tc.transform.position; g.y = 0f;
            float ang = Vector3.SignedAngle(fwd, g, Vector3.up);

            // Hindernis voraus (Haus, dichter Wald, Wrack): ausweichen
            avoidT -= dt;
            if (avoidT <= 0f)
            {
                avoidT = 0.3f;
                Vector3 o = tc.transform.position + Vector3.up * 1.4f + fwd * (tc.spec.hullLength * 0.5f);
                if (tc.RaycastIgnoringSelf(o, fwd, 14f, out RaycastHit hit) && hit.normal.y < 0.6f)
                {
                    if (Mathf.Abs(avoidDir) < 0.1f) avoidDir = Random.value < 0.5f ? -1f : 1f;
                }
                else avoidDir = 0f;
            }
            float steer = Mathf.Abs(avoidDir) > 0.1f ? avoidDir : Mathf.Clamp(ang / 35f, -1f, 1f);
            float throttle = Mathf.Abs(avoidDir) > 0.1f ? 0.45f : Mathf.Abs(ang) > 70f ? 0.25f : 0.85f;
            tc.AiControl(throttle, steer, false);

            // nicht vorwaerts gekommen?
            if (tc.SpeedKmh < 1.5f) stuckT += dt; else stuckT = 0f;
            if (stuckT > 3f) { stuckT = 0f; reverseT = 1.8f; avoidDir = Random.value < 0.5f ? -1f : 1f; }
        }

        /// <summary>Auftragspunkt anfahren (Hindernissen ausweichen), dort anhalten und die Front zum Ziel drehen.</summary>
        void DriveTo(float dt, Vector3 goal, bool haveTarget)
        {
            Vector3 fwd = tc.transform.forward; fwd.y = 0f; fwd.Normalize();
            if (reverseT > 0f) { reverseT -= dt; tc.AiControl(-0.8f, avoidDir, false); return; }
            Vector3 g = goal - tc.transform.position; g.y = 0f;
            if (g.magnitude < holdRadius)
            {
                stuckT = 0f;
                if (haveTarget && target != null)
                {
                    Vector3 to = target.transform.position - tc.transform.position; to.y = 0f;
                    float a = Vector3.SignedAngle(fwd, to, Vector3.up);
                    tc.AiControl(0f, Mathf.Abs(a) > 20f ? Mathf.Sign(a) * 0.7f : 0f, Mathf.Abs(a) <= 20f);
                }
                else tc.AiControl(0f, 0f, true);
                return;
            }
            float ang = Vector3.SignedAngle(fwd, g, Vector3.up);
            avoidT -= dt;
            if (avoidT <= 0f)
            {
                avoidT = 0.3f;
                Vector3 o = tc.transform.position + Vector3.up * 1.4f + fwd * (tc.spec.hullLength * 0.5f);
                if (tc.RaycastIgnoringSelf(o, fwd, 14f, out RaycastHit hit) && hit.normal.y < 0.6f)
                { if (Mathf.Abs(avoidDir) < 0.1f) avoidDir = Random.value < 0.5f ? -1f : 1f; }
                else avoidDir = 0f;
            }
            float steer = Mathf.Abs(avoidDir) > 0.1f ? avoidDir : Mathf.Clamp(ang / 35f, -1f, 1f);
            float slow = Mathf.Clamp01((g.magnitude - holdRadius) / 40f);
            float throttle = Mathf.Abs(avoidDir) > 0.1f ? 0.45f : Mathf.Abs(ang) > 70f ? 0.25f : Mathf.Lerp(0.35f, 0.85f, slow);
            tc.AiControl(throttle, steer, false);
            if (tc.SpeedKmh < 1.5f) stuckT += dt; else stuckT = 0f;
            if (stuckT > 3f) { stuckT = 0f; reverseT = 1.8f; avoidDir = Random.value < 0.5f ? -1f : 1f; }
        }

        // ---------------- Zielen und Schiessen ----------------
        void Aim(float dt, float dist)
        {
            Vector3 tpos = TargetPoint();
            var rb = target.rb;
            Vector3 vel = rb != null ? rb.linearVelocity : Vector3.zero;
            Ballistics.SimulateFlat(tc.Ammo, Mathf.Max(dist, 10f), out _, out _, out float tof);
            // Vorhalt (die KI schaetzt die Zielbewegung, nicht perfekt)
            Vector3 lead = vel * tof * Mathf.Lerp(0.7f, 1f, skill);
            // Richtfehler: neu gewuerfelt nach jedem Schuss, kleiner mit Koennen und je oefter schon geschossen
            if (tc.reloadLeft > lastReload + 0.5f) { shotsAtTarget++; rangeErr *= Random.Range(0.25f, 0.55f); NewJitter(dist); }
            lastReload = tc.reloadLeft;
            if (aimJitter == Vector3.zero) NewJitter(dist);
            tc.aimPoint = tpos + lead + aimJitter;
            tc.sightRange = Mathf.Max(50f, dist * (1f + rangeErr));

            if (!visible || reactT > 0f || dist > 2200f || tc.reloadLeft > 0f) { settleT = 0f; return; }
            // Rohr steht auf dem Ziel und der Panzer faehrt nicht mehr: kurz ruhig halten, dann Feuer
            Vector3 gunF = tc.parts.gunPivot.forward, want = (tc.aimPoint - tc.parts.gunPivot.position);
            gunF.y = 0f; want.y = 0f;
            bool onTarget = Vector3.Angle(gunF, want) < 0.6f;
            bool steady = tc.SpeedKmh < 4f || tc.spec.stabilizer;
            if (onTarget && steady) settleT += dt; else settleT = 0f;
            if (settleT > Mathf.Lerp(1.4f, 0.5f, skill)) { tc.AiFire(); settleT = 0f; }
        }

        void NewJitter(float dist)
        {
            float r = dist * Mathf.Lerp(0.0025f, 0.0008f, skill) / Mathf.Sqrt(1f + shotsAtTarget);
            aimJitter = new Vector3(Random.Range(-r, r), Random.Range(-r, r) * 0.6f, Random.Range(-r, r));
            if (aimJitter == Vector3.zero) aimJitter = Vector3.up * 0.001f;
        }
    }
}
