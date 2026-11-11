using UnityEngine;

namespace PanzerSim
{
    public enum HitOutcome { Penetration, NoPenetration, Ricochet, Shatter }

    public struct HitResult
    {
        public HitOutcome outcome;
        public float penetrationMm;     // was die Granate bei dieser Geschwindigkeit senkrecht schafft
        public float effectiveArmorMm;  // Plattendicke nach Winkel und Material
        public float impactAngleDeg;    // 0 = senkrecht
        public float residualVelocity;  // m/s nach dem Treffer
        public Vector3 exitDirection;
        public string note;
    }

    /// <summary>
    /// Reine Rechenlogik, ohne Grafik. So kann sie spaeter im Mehrspieler auf dem Host laufen.
    /// Alle Zahlen hier sind Startwerte zum Kalibrieren.
    /// </summary>
    public static class Penetration
    {
        // De-Marre-Konstante. Hoeher = weniger Durchschlag.
        public const float DeMarreK = 1900f;

        /// <summary>Durchschlag in mm senkrecht gegen Walzstahl (RHA), Formel nach de Marre.</summary>
        public static float DeMarreMm(float velocity, float massKg, float caliberMm)
        {
            if (velocity <= 0f) return 0f;
            float num = Mathf.Pow(velocity, 1.43f) * Mathf.Pow(massKg, 0.71f);
            float den = Mathf.Pow(DeMarreK, 1.43f) * Mathf.Pow(caliberMm / 100f, 1.07f);
            return num / den * 100f;
        }

        /// <summary>Viel Sprengstoff = duennere Wand = weniger Durchschlag.</summary>
        public static float FillerFactor(ShellData s)
        {
            float pct = s.fillerKg / s.massKg * 100f;
            if (pct <= 0.65f) return 1f;
            return Mathf.Lerp(1f, 0.80f, Mathf.InverseLerp(0.65f, 3f, pct));
        }

        public static float FlatPenetration(ShellData s, float velocity)
        {
            if (s.IsHE) return HEBlastMm(s.fillerKg);
            if (s.IsHEAT) return s.heatPenMm;   // Hohlladung: unabhaengig von der Geschwindigkeit
            return DeMarreMm(velocity, s.massKg, s.caliberMm) * FillerFactor(s);
        }

        /// <summary>
        /// Sprenggranate: so dicke Platten schlaegt die Druckwelle beim Aufschlag durch (mm RHA).
        /// Faustregel: waechst mit der dritten Wurzel der Sprengstoffmenge (0,7 kg -> ca. 18 mm, 0,9 kg -> ca. 19 mm).
        /// </summary>
        public static float HEBlastMm(float fillerKg) => 20f * Mathf.Pow(Mathf.Max(fillerKg, 0f), 1f / 3f);

        /// <summary>Ab diesem Auftreffwinkel gleitet eine Hohlladung ab bzw. der Zuender spricht nicht richtig an.</summary>
        public const float HEATGrazeAngle = 80f;

        /// <summary>Ab diesem Auftreffwinkel beginnen Abpraller.</summary>
        static float RicochetStartAngle(ShellType t)
        {
            switch (t)
            {
                case ShellType.APC:
                case ShellType.APCBC: return 70f;
                case ShellType.APHEBC: return 72f;
                default: return 65f;
            }
        }

        /// <summary>Wie stark die Granate sich beim Auftreffen "in die Platte dreht" (Normalisierung).</summary>
        static float NormalizationDeg(ShellType t)
        {
            switch (t)
            {
                case ShellType.APC:
                case ShellType.APCBC: return 6f;
                case ShellType.APHEBC: return 8f;
                default: return 3f;
            }
        }

        public static HitResult Evaluate(ShellData s, Vector3 velocityVec, Vector3 surfaceNormal, ArmorPlate plate)
        {
            return Evaluate(s, velocityVec, surfaceNormal, plate.thicknessMm, plate.material);
        }

        public static HitResult Evaluate(ShellData s, Vector3 velocityVec, Vector3 surfaceNormal, float plateMm, ArmorMaterial plateMat)
        {
            var r = new HitResult();
            float v = velocityVec.magnitude;
            Vector3 dir = velocityVec / Mathf.Max(v, 0.001f);

            r.impactAngleDeg = Vector3.Angle(-dir, surfaceNormal);
            r.penetrationMm = FlatPenetration(s, v);

            // Sprenggranate: explodiert sofort auf der Oberflaeche, kein Abpraller. Die Druckwelle wirkt fast senkrecht
            // auf die Platte (Winkel spielt kaum eine Rolle) - durch, wenn die Platte duenner ist als die Druckwelle schafft.
            if (s.IsHE)
            {
                r.effectiveArmorMm = plateMm;
                r.residualVelocity = 0f;
                r.exitDirection = dir;
                r.outcome = r.penetrationMm >= plateMm ? HitOutcome.Penetration : HitOutcome.NoPenetration;
                r.note = r.outcome == HitOutcome.Penetration
                    ? "Sprenggranate: Druckwelle schlaegt die Platte durch"
                    : $"Sprenggranate explodiert aussen (Platte {plateMm - r.penetrationMm:0} mm zu dick fuer die Druckwelle)";
                return r;
            }

            // Hohlladung: fester Durchschlag, effektive Dicke nach Winkel (keine Normalisierung wie bei Panzerkappen).
            // Sehr flach auftreffend gleitet sie ab.
            if (s.IsHEAT)
            {
                float t = plateMm * (plateMat == ArmorMaterial.Cast ? 0.95f : 1f);
                r.effectiveArmorMm = t / Mathf.Max(Mathf.Cos(r.impactAngleDeg * Mathf.Deg2Rad), 0.05f);
                if (r.impactAngleDeg > HEATGrazeAngle)
                {
                    r.outcome = HitOutcome.Ricochet;
                    r.exitDirection = Vector3.Reflect(dir, surfaceNormal);
                    r.residualVelocity = v * 0.5f;
                    r.note = "Hohlladung gleitet ab (zu flach)";
                    return r;
                }
                r.residualVelocity = 0f;
                r.exitDirection = dir;
                r.outcome = r.penetrationMm >= r.effectiveArmorMm ? HitOutcome.Penetration : HitOutcome.NoPenetration;
                r.note = r.outcome == HitOutcome.Penetration
                    ? $"Hohlladung: Strahl schlaegt durch ({r.penetrationMm - r.effectiveArmorMm:0} mm Reserve)"
                    : $"Hohlladung: kein Durchschlag (fehlen {r.effectiveArmorMm - r.penetrationMm:0} mm)";
                return r;
            }

            float thickness = plateMm * ArmorPlate.Quality(plateMat, s);
            float overmatch = s.caliberMm / Mathf.Max(plateMm, 1f); // Kaliber im Verhaeltnis zur Dicke

            // 1) Abpraller. Grosses Kaliber gegen duenne Platte prallt kaum ab (Overmatch).
            float ricStart = RicochetStartAngle(s.type) + Mathf.Clamp(overmatch - 1f, 0f, 1f) * 10f;
            float ricChance = overmatch > 2f ? 0f : Mathf.InverseLerp(ricStart, ricStart + 10f, r.impactAngleDeg);
            if (Random.value < ricChance)
            {
                r.outcome = HitOutcome.Ricochet;
                r.exitDirection = Vector3.Reflect(dir, surfaceNormal);
                r.residualVelocity = v * 0.6f;
                r.effectiveArmorMm = thickness / Mathf.Max(Mathf.Cos(r.impactAngleDeg * Mathf.Deg2Rad), 0.05f);
                r.note = $"Abpraller ({ricChance * 100f:0}% Chance)";
                return r;
            }

            // 2) Effektive Dicke durch Schraege, Normalisierung staerker bei Overmatch.
            float norm = NormalizationDeg(s.type) * Mathf.Clamp(overmatch, 0.5f, 2f);
            float effAngle = Mathf.Max(0f, r.impactAngleDeg - norm);
            r.effectiveArmorMm = thickness / Mathf.Max(Mathf.Cos(effAngle * Mathf.Deg2Rad), 0.05f);

            // 3) Zerbrechen (Shatter Gap): Granate ohne Kappe, sehr schnell, gegen dicke Platte.
            if (!s.IsCapped && v > 800f && plateMm > s.caliberMm * 1.05f
                && r.penetrationMm >= r.effectiveArmorMm && Random.value < 0.4f)
            {
                r.outcome = HitOutcome.Shatter;
                r.residualVelocity = 0f;
                r.note = "Granate zerbrochen (ohne Kappe, zu schnell gegen dicke Platte)";
                return r;
            }

            // 4) Durchschlag ja/nein
            if (r.penetrationMm >= r.effectiveArmorMm)
            {
                r.outcome = HitOutcome.Penetration;
                float ratio = r.effectiveArmorMm / r.penetrationMm;
                r.residualVelocity = v * Mathf.Sqrt(Mathf.Max(0f, 1f - ratio * ratio));
                // Geschoss knickt leicht Richtung Plattennormale ab
                r.exitDirection = Vector3.Slerp(dir, -surfaceNormal, 0.15f).normalized;
                r.note = $"Durchschlag, Restgeschwindigkeit {r.residualVelocity:0} m/s";
            }
            else
            {
                r.outcome = HitOutcome.NoPenetration;
                r.residualVelocity = 0f;
                r.note = $"Kein Durchschlag (fehlen {r.effectiveArmorMm - r.penetrationMm:0} mm)";
            }
            return r;
        }
    }

    /// <summary>Flugbahn mit Schwerkraft und Luftwiderstand.</summary>
    public static class Ballistics
    {
        public const float AirDensity = 1.225f; // kg/m^3 auf Meereshoehe

        public static Vector3 Acceleration(ShellData s, Vector3 velocity)
        {
            float speed = velocity.magnitude;
            float area = Mathf.PI * Mathf.Pow(s.caliberMm / 2000f, 2f);
            float dragAcc = 0.5f * AirDensity * speed * speed * s.dragCoefficient * area / s.massKg;
            Vector3 drag = speed > 0.01f ? -velocity / speed * dragAcc : Vector3.zero;
            return Physics.gravity + drag;
        }

        /// <summary>Simuliert einen waagerechten Schuss und gibt Geschwindigkeit, Fall und Flugzeit auf Entfernung zurueck.</summary>
        public static void SimulateFlat(ShellData s, float distance, out float velocity, out float drop, out float time)
        {
            Vector3 pos = Vector3.zero;
            Vector3 vel = new Vector3(0f, 0f, s.muzzleVelocity);
            const float dt = 0.002f;
            time = 0f;
            while (pos.z < distance && time < 20f)
            {
                vel += Acceleration(s, vel) * dt;
                pos += vel * dt;
                time += dt;
            }
            velocity = vel.magnitude;
            drop = -pos.y;
        }
    }
}
