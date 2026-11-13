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
        public bool guided;                   // Lenkrakete (TOW): fliegt der Visierlinie nach statt ballistisch
        public float wireRange = 3750f;       // Lenkrakete: Drahtlaenge (danach ungelenkt)

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
            // Lenkraketen (Wiesel 1 TOW): 152 mm Hohlladung, drahtgelenkt. Geschwindigkeit hier = Einschlag des Gefechtskopfs.
            new ShellData("BGM-71A TOW",   "TOW-Werfer (Wiesel)",  ShellType.HEAT, 152f, 18.9f, 280f, 2.4f) { heatPenMm = 430f, guided = true, wireRange = 3000f },  // 15
            new ShellData("BGM-71D TOW-2", "TOW-Werfer (Wiesel)",  ShellType.HEAT, 152f, 21.5f, 280f, 3.1f) { heatPenMm = 850f, guided = true, wireRange = 3750f },  // 16
            // PzH 2000: 155 mm L/52, Sprenggranate DM121 (43,5 kg); Geschwindigkeit = staerkste Treibladung (Zone 6)
            new ShellData("DM121 Spreng", "155 mm L/52 (PzH 2000)", ShellType.HE, 155f, 43.5f, 900f, 10.0f) { dragCoefficient = 0.25f },  // 17
            // Tiger II: 8,8 cm KwK 43 L/71 (deutlich schneller als die KwK 36 des Tiger I)
            new ShellData("PzGr. 39/43", "8,8 cm KwK 43 L/71 (Tiger II)", ShellType.APCBC, 88f, 10.16f, 1000f, 0.059f),   // 18
            new ShellData("Sprgr. 43",   "8,8 cm KwK 43 L/71 (Tiger II)", ShellType.HE,    88f, 9.40f,  750f, 1.0f) { dragCoefficient = 0.32f },   // 19
            new ShellData("Gr. 39/43 HL", "8,8 cm KwK 43 L/71 (Tiger II)", ShellType.HEAT, 88f, 7.65f,  600f, 0.8f) { heatPenMm = 100f, dragCoefficient = 0.35f },   // 20
            // Panzer III Ausf. L: 5 cm KwK 39 L/60
            new ShellData("PzGr. 39 (5 cm)", "5 cm KwK 39 L/60 (Panzer III)", ShellType.APCBC, 50f, 2.06f, 835f, 0.017f),   // 21
            new ShellData("Sprgr. 38 (5 cm)", "5 cm KwK 39 L/60 (Panzer III)", ShellType.HE, 50f, 1.82f, 550f, 0.17f) { dragCoefficient = 0.33f },   // 22
            // Panther: 7,5 cm KwK 42 L/70
            new ShellData("PzGr. 39/42", "7,5 cm KwK 42 L/70 (Panther)", ShellType.APCBC, 75f, 6.80f, 935f, 0.018f),   // 23
            new ShellData("Sprgr. 42",   "7,5 cm KwK 42 L/70 (Panther)", ShellType.HE,    75f, 5.74f, 700f, 0.725f) { dragCoefficient = 0.33f },   // 24
            // KV-1 (1941): 76,2 mm F-32 L/31,5
            new ShellData("BR-350A", "76 mm F-32 (KV-1)", ShellType.APHEBC, 76.2f, 6.30f, 612f, 0.155f),   // 25  kurzes Rohr: 612 m/s
            new ShellData("OF-350",  "76 mm F-32 (KV-1)", ShellType.HE,     76.2f, 6.20f, 600f, 0.71f) { dragCoefficient = 0.33f },   // 26
            // IS-1: 85 mm D-5T (Ballistik wie ZiS-S-53)
            new ShellData("BR-365",  "85 mm D-5T (IS-1)", ShellType.APHEBC, 85f, 9.20f, 792f, 0.048f),   // 27
            new ShellData("BR-365K", "85 mm D-5T (IS-1)", ShellType.APHE,   85f, 9.20f, 792f, 0.048f),   // 28
            new ShellData("O-365",   "85 mm D-5T (IS-1)", ShellType.HE,     85f, 9.54f, 793f, 0.741f) { dragCoefficient = 0.32f },   // 29
            // IS-2, IS-3, T-10: 122 mm D-25T L/43 (getrennte Munition: Geschoss + Kartusche)
            new ShellData("BR-471",  "122 mm D-25T (IS-2/IS-3/T-10)", ShellType.APHE,   122f, 25.0f, 795f, 0.156f),   // 30  stumpfe Spitze
            new ShellData("BR-471B", "122 mm D-25T (IS-2/IS-3/T-10)", ShellType.APHEBC, 122f, 25.0f, 795f, 0.156f),   // 31  stumpf + ballistische Haube (ab 1944)
            new ShellData("OF-471",  "122 mm D-25T (IS-2/IS-3/T-10)", ShellType.HE,     122f, 25.0f, 800f, 3.6f) { dragCoefficient = 0.31f },   // 32
            // T-10M: 122 mm M-62-T2 L/46,5
            new ShellData("BR-472",  "122 mm M-62-T2 (T-10M)", ShellType.APHEBC, 122f, 25.0f, 950f, 0.156f),   // 33
            new ShellData("OF-472",  "122 mm M-62-T2 (T-10M)", ShellType.HE,     122f, 27.3f, 885f, 3.0f) { dragCoefficient = 0.31f },   // 34
            new ShellData("BK-6M",   "122 mm M-62-T2 (T-10M)", ShellType.HEAT,   122f, 21.6f, 900f, 1.4f) { heatPenMm = 420f, dragCoefficient = 0.33f },   // 35
            // T-34 (1943): 76 mm F-34 L/41,5
            new ShellData("BR-350A", "76 mm F-34 (T-34)", ShellType.APHEBC, 76.2f, 6.30f, 662f, 0.155f),   // 36
            new ShellData("OF-350",  "76 mm F-34 (T-34)", ShellType.HE,     76.2f, 6.20f, 680f, 0.71f) { dragCoefficient = 0.33f },   // 37
            // T-44-100, T-54, T-55A: 100 mm D-10T L/53,5
            new ShellData("BR-412B", "100 mm D-10T (T-44-100/T-54/T-55A)", ShellType.APHEBC, 100f, 15.88f, 887f, 0.065f),   // 38
            new ShellData("OF-412",  "100 mm D-10T (T-44-100/T-54/T-55A)", ShellType.HE,     100f, 15.6f,  900f, 1.46f) { dragCoefficient = 0.31f },   // 39
            new ShellData("BR-412D", "100 mm D-10T (T-44-100/T-54/T-55A)", ShellType.APCBC,  100f, 15.88f, 887f, 0.061f),   // 40  Panzerkappe + Haube (ab 1953)
            new ShellData("BK-5M",   "100 mm D-10T (T-44-100/T-54/T-55A)", ShellType.HEAT,   100f, 12.2f,  900f, 0.9f) { heatPenMm = 380f, dragCoefficient = 0.33f },   // 41
            // Sherman Firefly: 17-Pfuender (76,2 mm L/55); dazu APCBC Mk8 (Index 5)
            new ShellData("APC Mk4", "17-Pfuender (Firefly)", ShellType.APC, 76.2f, 7.70f, 884f, 0f),   // 42  Vollgeschoss mit Panzerkappe
            new ShellData("HE Mk1",  "17-Pfuender (Firefly)", ShellType.HE,  76.2f, 6.98f, 610f, 0.585f) { dragCoefficient = 0.33f },   // 43
        };
    }
}
