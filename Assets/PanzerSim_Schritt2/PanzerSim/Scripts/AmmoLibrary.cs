using UnityEngine;

namespace PanzerSim
{
    /// <summary>Granatentypen des Zweiten Weltkriegs.</summary>
    public enum ShellType
    {
        AP,      // Vollgeschoss ohne Kappe
        APHE,    // Panzergranate mit Sprengladung, ohne Kappe
        APC,     // mit Panzerkappe (hilft gegen gehaertete Platten und Schraege)
        APCBC,   // mit Panzerkappe + ballistischer Haube, meist mit Sprengladung
        APHEBC,  // stumpfe Spitze + ballistische Haube (sowjetisch, z. B. BR-365)
        HE,      // Sprenggranate: explodiert beim Aufschlag, Druckwelle schlaegt nur duenne Platten durch
        HEAT     // Hohlladung: Metallstrahl, Durchschlag unabhaengig von Entfernung/Geschwindigkeit
    }

    [System.Serializable]
    public class ShellData
    {
        public string name;
        public string gun;
        public ShellType type;
        public float caliberMm;
        public float massKg;
        public float muzzleVelocity;          // m/s
        public float fillerKg;                // Sprengstoff in kg (0 = keiner)
        public float dragCoefficient = 0.30f; // Luftwiderstandsbeiwert (Ueberschall, grob)
        public float fuseDelay = 0.0012f;     // Sekunden nach Durchschlag bis zur Explosion
        public float fuseSensitivityMm = 15f; // so dick muss eine Platte sein, damit der Zuender scharf wird

        public float heatPenMm;               // nur HEAT: Durchschlag des Strahls senkrecht (mm RHA)

        public bool IsCapped => type == ShellType.APC || type == ShellType.APCBC;
        public bool HasFiller => fillerKg > 0f;
        public bool IsHE => type == ShellType.HE;
        public bool IsHEAT => type == ShellType.HEAT;
        /// <summary>Explodiert beim Aufschlag (kein Eindringen des Geschosskoerpers, kein Steckenbleiben).</summary>
        public bool ImpactFuze => IsHE || IsHEAT;

        public ShellData(string name, string gun, ShellType type, float caliberMm, float massKg,
                         float muzzleVelocity, float fillerKg)
        {
            this.name = name;
            this.gun = gun;
            this.type = type;
            this.caliberMm = caliberMm;
            this.massKg = massKg;
            this.muzzleVelocity = muzzleVelocity;
            this.fillerKg = fillerKg;
        }
    }

    /// <summary>
    /// Ungefaehre historische Werte. Spaeter gegen echte Beschusstabellen kalibrieren!
    /// </summary>
    public static class AmmoLibrary
    {
        public static readonly ShellData[] All =
        {
            new ShellData("PzGr. 39",  "7,5 cm KwK 40 L/48 (Panzer IV)",  ShellType.APCBC,  75f,  6.80f, 790f, 0.018f),
            new ShellData("PzGr. 39",  "8,8 cm KwK 36 (Tiger I)",         ShellType.APCBC,  88f, 10.20f, 773f, 0.059f),
            new ShellData("M61 APCBC", "75 mm M3 (Sherman)",              ShellType.APCBC,  75f,  6.79f, 618f, 0.066f),
            new ShellData("M62 APCBC", "76 mm M1 (Sherman 76)",           ShellType.APCBC,  76.2f, 7.00f, 792f, 0.064f),
            new ShellData("BR-365",    "85 mm ZiS-S-53 (T-34-85)",        ShellType.APHEBC, 85f,  9.20f, 792f, 0.048f),
            new ShellData("APCBC Mk8", "17-Pfuender (Firefly)",           ShellType.APCBC,  76.2f, 7.65f, 883f, 0.0f),
            new ShellData("M72 AP",    "75 mm M3 (Sherman)",              ShellType.AP,     75f,  6.32f, 619f, 0.0f),
            new ShellData("BR-365K",   "85 mm ZiS-S-53 (T-34-85)",        ShellType.APHE,   85f,  9.20f, 792f, 0.048f),
            new ShellData("PzGr. 39",  "7,5 cm KwK 40 L/43 (Panzer IV F2)", ShellType.APCBC, 75f,  6.80f, 740f, 0.018f),   // 8: kuerzeres Rohr -> 740 statt 790 m/s
            // ---- Sprenggranaten (HE) ----
            new ShellData("Sprgr. 34", "7,5 cm KwK 40 L/43 (Panzer IV F2)", ShellType.HE,  75f,  5.74f, 550f, 0.686f) { dragCoefficient = 0.33f },  // 9
            // ---- Hohlladung (HEAT) ----
            new ShellData("Gr. 38 HL/B", "7,5 cm KwK 40 L/43 (Panzer IV F2)", ShellType.HEAT, 75f, 4.57f, 450f, 0.513f) { heatPenMm = 75f, dragCoefficient = 0.36f },  // 10
            new ShellData("Sprgr. L/4,5", "8,8 cm KwK 36 (Tiger I)",       ShellType.HE,   88f,  9.00f, 820f, 0.900f) { dragCoefficient = 0.32f },  // 11
            new ShellData("Gr. 39 HL",  "8,8 cm KwK 36 (Tiger I)",         ShellType.HEAT, 88f,  7.65f, 600f, 0.800f) { heatPenMm = 100f, dragCoefficient = 0.35f }, // 12
            new ShellData("M48 HE",     "75 mm M3 (Sherman)",              ShellType.HE,   75f,  6.62f, 463f, 0.676f) { dragCoefficient = 0.33f },  // 13
            new ShellData("O-365",      "85 mm ZiS-S-53 (T-34-85)",        ShellType.HE,   85f,  9.54f, 793f, 0.741f) { dragCoefficient = 0.32f },  // 14
        };
    }
}
