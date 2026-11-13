using UnityEngine;

namespace PanzerSim
{
    /// <summary>Eine Panzerplatte: Dicke in mm, Winkel von der Senkrechten in Grad, Material.</summary>
    [System.Serializable]
    public struct Armor
    {
        public float mm;
        public float angle;
        public ArmorMaterial mat;
        public Armor(float mm, float angle, ArmorMaterial mat = ArmorMaterial.RHA)
        {
            this.mm = mm; this.angle = angle; this.mat = mat;
        }
    }

    /// <summary>Art des Zielfernrohrs (Strichplatte).</summary>
    public enum SightType { German, US, Soviet, TOW }

    /// <summary>Nation (Reiter in der Panzerauswahl).</summary>
    public enum Nation { Germany, USA, USSR, UK }

    /// <summary>Alle Daten eines Panzertyps. Masse in Metern, Panzerung in mm.</summary>
    [System.Serializable]
    public class TankSpec
    {
        public string name;
        public Nation nation;                // Reiter in der Panzerauswahl (Standard: Deutschland)
        public float massT;
        public float powerPS;
        public float maxSpeedKmh;
        public float reverseKmh;
        public float turnRateDegS = 30f; // Drehgeschwindigkeit auf der Stelle

        // Wanne
        public float hullLength, hullWidth, hullHeight;
        public float clearance = 0.45f;
        public float trackWidth = 0.45f;
        public float trackInset = 0f;     // Ketten unter die Wanne ruecken (z. B. T-34 mit Ueberhang)
        public Armor hullFront, hullRear, hullSide, hullRoof, hullBottom;
        // Nur fuer Panzerung aus der Modellform (meshArmor): genauere Zonen
        public bool meshArmor;
        public bool meshArmorFixWinding;     // Modell hat nach innen zeigende Flaechen: fuer die Panzerung nach aussen drehen (sonst fliegen Granaten durch)
        public Armor hullFrontLower = new Armor(-1, 0), hullGlacis = new Armor(-1, 0), hullLowerSide = new Armor(-1, 0);

        // Turm
        public float turretLength, turretWidth, turretHeight, turretOffsetZ;
        public Armor turretFront, turretSide, turretRear, turretRoof, mantlet;
        public float traverseDegS, elevationDegS, minElevation, maxElevation;

        // Kanone
        public int ammoIndex;       // Index in AmmoLibrary.All (Startmunition)
        public int[] ammo;          // Granaten, die diese Kanone verschiesst (Indizes in AmmoLibrary.All); Taste 1, 2, ...
        public float reloadS;
        public float barrelLength;
        public bool noGun;                   // (noch) keine Hauptwaffe: Feuern und MG aus (Wiesel: TOW-Lenkrakete folgt)
        public bool devOnly;                 // nur im Entwickler-Modus waehlbar (Fahrzeug in Arbeit)
        public bool noMg;                    // kein Turm-MG
        public int mgType;                   // Koaxial-MG: 0 MG 34, 1 DT 7,62 mm, 2 DShKM 12,7 mm, 3 KPVT 14,5 mm, 4 M1919 .30, 5 SGMT 7,62 mm, 6 MG3 7,62 mm NATO (MachineGun.Rounds)
        public ModuleType[] crew;            // Besatzung (null = Kommandant, Richtschuetze, Ladeschuetze, Fahrer, Funker)
        public bool crewInHull;              // Richtschuetze/Kommandant sitzen in der Wanne (kein bemannter Turm)
        public bool hasThermal;              // Zielgeraet mit Waermebild (Taste V im Visier)
        public bool stabilizer;              // Waffenanlage stabilisiert: Rohr haelt die Richtung, auch wenn die Wanne schaukelt
        public bool fireControl;             // Feuerleitrechner: beim Lasern wird die Zielbewegung gemessen und der Vorhalt eingerechnet
        public bool hasRangefinder;          // Laser-Entfernungsmesser (Taste R); ohne: Entfernung schaetzen (Q/E), keine Meterangabe
        public bool artillery;               // Artillerie: Kartenmodus (Taste M) fuer indirektes Feuer
        public bool gunPivotFromModel;       // Kanonen-/Werfergelenk an die Stelle des Modell-Gelenks legen (Werfer sitzt nicht vorne am Turm)
        public bool muzzleBrake;             // Muendungsbremse (Gas schiesst seitlich raus)

        // Zielfernrohr
        public SightType sightType = SightType.German;
        public float sightFov = 25f;         // Sichtfeld in Grad (kleiner = staerkere Vergroesserung)
        public float sightMag = 2.5f;        // Vergroesserung bei diesem Sichtfeld
        // Echte Zoomstufen des Visiers (leer = nur sightFov/sightMag)
        public float[] sightZoomFov;
        public float[] sightZoomMag;
        public string sightName = "TZF";

        // 3D-Modell (optional). Liegt in Assets/Resources, Pfad ohne Endung.
        public string modelResource;
        public string modelTurretBone = "turret";
        public string modelGunBone = "cannon";
        public float modelYaw = 0f;          // falls das Modell falsch herum steht: 180
        public float modelScale = 0f;        // feste Skalierung (0 = automatisch aus der Breite)
        public float modelScaleMul = 1f;     // Feinanpassung der Groesse
        public Vector3 modelOffset;          // Feinanpassung der Position
        public Vector3 modelTurretPivotShift; // Turm-Drehpunkt verschieben (m, Panzer-Koordinaten, z = vorne), Modell bleibt stehen:
                                              // fuer Modelle, deren Turmgelenk nicht in der Mitte des Turmrings sitzt

        // Laufrollen und Ketten im Modell (optional)
        public string wheelBoneLeft;         // z. B. "wheeltrackleft" -> wheeltrackleft0 ... wheeltrackleft9
        public string wheelBoneRight;
        public int wheelBoneCount;           // erste und letzte = Antriebsrad/Leitrad (drehen nur, federn nicht)
        public float wheelRadius = 0.33f;    // in Modell-Einheiten (vor Skalierung)
        public string trackMaterialLeft;     // Teil des Materialnamens
        public string trackMaterialRight;
        public float trackMetersPerUV = 1.15f; // Modell-Einheiten pro Texturlaenge
        public float trackScrollSign = 1f;   // falls die Kette falsch herum laeuft: -1
        // Stuetzrollen (optional): Kette laeuft oben darueber; Teile heissen z. B. rollL1 ... rollL4
        public string returnRollerLeft, returnRollerRight;
        public int returnRollerCount;
        public float returnRollerRadius = 0.13f;   // Modell-Einheiten (wie wheelRadius)

        // Eigene Kette aus Gliedern (ersetzt die Kette des Modells)
        public bool linkTracks;
        public float idlerRadius = 0.25f;    // Modell-Einheiten
        public float sprocketRadius = 0.33f; // Modell-Einheiten
        public float trackPitch = 0.13f;     // Laenge eines Kettenglieds in m
        public float trackShoeWidth = 0.7f;  // Breite der Kette in m
        public float trackThickness = 0.05f; // Dicke in m
        public float trackSag = 0.05f;       // Durchhang oben in m
    }

    /// <summary>
    /// Panzer des Zweiten Weltkriegs mit ungefaehren echten Werten.
    /// Die Form ist stark vereinfacht (Kisten mit schraegen Platten).
    /// </summary>
    public static class TankLibrary
    {
        public static TankSpec Tiger => new TankSpec
        {
            name = "Tiger I",
            massT = 57f, powerPS = 700f, maxSpeedKmh = 40f, reverseKmh = 7f, turnRateDegS = 22f,
            // Masse aus dem 3D-Modell (Joanthan To, CC BY 4.0; Tools/Blender/rig_tiger.py, auf 6,3 m Wannenlaenge skaliert)
            hullLength = 6.28f, hullWidth = 3.43f, hullHeight = 1.48f, clearance = 0.29f,
            trackWidth = 0.725f, trackInset = 0.54f,      // Kettenmitte auf den Laufrollen des Modells (x = 1,54 m)
            hullFront = new Armor(100, 9), hullRear = new Armor(80, 9), hullSide = new Armor(80, 0),
            hullRoof = new Armor(25, 0), hullBottom = new Armor(25, 0),
            turretLength = 2.75f, turretWidth = 2.3f, turretHeight = 0.86f, turretOffsetZ = -0.2f,
            turretFront = new Armor(100, 0), turretSide = new Armor(80, 0), turretRear = new Armor(80, 0),
            turretRoof = new Armor(25, 0), mantlet = new Armor(110, 0),
            traverseDegS = 7f, elevationDegS = 4f, minElevation = -8f, maxElevation = 15f,
            ammoIndex = 1, ammo = new[] { 1, 11, 12 }, reloadS = 8.5f, barrelLength = 3.9f, muzzleBrake = true, gunPivotFromModel = true,
            sightType = SightType.German, sightFov = 25f, sightMag = 2.5f, sightName = "TZF 9c",
            sightZoomFov = new[] { 25f, 14f }, sightZoomMag = new[] { 2.5f, 5f },
            modelResource = "Modelle/Tiger", modelScale = 1f,
            modelOffset = new Vector3(0f, 0f, -0.44f),   // Modell ist hinten laenger (Auspuff, Leitrad): Wanne mittig
            modelTurretBone = "turret", modelGunBone = "cannon",
            meshArmor = true, meshArmorFixWinding = true,  // Plattenmodell: manche Flaechen zeigen nach innen
            hullFrontLower = new Armor(100, 24), hullGlacis = new Armor(60, 80), hullLowerSide = new Armor(60, 0),
            // Leitrad hinten (radL0), 8 Achsen mit verschachtelten Laufrollen (800 mm), Antriebsrad vorne (radL9)
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 10, wheelRadius = 0.36f,
            linkTracks = true, idlerRadius = 0.36f, sprocketRadius = 0.456f,
            trackPitch = 0.13f, trackShoeWidth = 0.725f, trackThickness = 0.05f, trackSag = 0.06f
        };

        /// <summary>
        /// Tiger II (Sd.Kfz. 182, "Koenigstiger", Serienturm): 8,8 cm KwK 43 L/71, schraege Panzerung bis 180 mm.
        /// Modell: "Tank Tiger 2" von maximus0075550 (sketchfab.com/maximus0075550), CC BY 4.0 - gerigged mit Tools/Blender/rig_tiger2.py.
        /// </summary>
        public static TankSpec TigerII => new TankSpec
        {
            name = "Tiger II",
            // Maybach HL 230 P30, 700 PS; 69,8 t; 41,5 km/h (Strasse)
            massT = 69.8f, powerPS = 700f, maxSpeedKmh = 41.5f, reverseKmh = 7f, turnRateDegS = 18f,
            hullLength = 7.2f, hullWidth = 3.29f, hullHeight = 1.5f, clearance = 0.36f,
            trackWidth = 0.80f, trackInset = 0.625f,          // Kettenmitte auf den Laufrollen des Modells (x = 1,42 m)
            hullFront = new Armor(150, 50), hullRear = new Armor(80, 30), hullSide = new Armor(80, 25),
            hullRoof = new Armor(40, 0), hullBottom = new Armor(28, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(100, 50), hullGlacis = new Armor(150, 50), hullLowerSide = new Armor(80, 0),
            turretLength = 3.6f, turretWidth = 2.6f, turretHeight = 0.95f, turretOffsetZ = 0.14f,
            turretFront = new Armor(180, 9), turretSide = new Armor(80, 21), turretRear = new Armor(80, 20),
            turretRoof = new Armor(44, 0), mantlet = new Armor(150, 0, ArmorMaterial.Cast),   // "Saukopf"-Blende
            traverseDegS = 6f, elevationDegS = 4f, minElevation = -8f, maxElevation = 15f,    // hydraulisch, vom Motor angetrieben
            ammoIndex = 18, ammo = new[] { 18, 19, 20 }, reloadS = 8f, barrelLength = 4.6f, muzzleBrake = true, gunPivotFromModel = true,
            // TZF 9d: umschaltbar 3- / 6-fach
            sightType = SightType.German, sightFov = 23f, sightMag = 3f, sightName = "TZF 9d",
            sightZoomFov = new[] { 23f, 11.5f }, sightZoomMag = new[] { 3f, 6f },
            modelResource = "Modelle/TigerII", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            // Leitrad hinten (radL0), 9 Achsen mit verschachtelten Laufrollen (800 mm), Antriebsrad vorne (radL10)
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 11, wheelRadius = 0.413f,
            linkTracks = true, idlerRadius = 0.325f, sprocketRadius = 0.403f,
            trackPitch = 0.15f, trackShoeWidth = 0.80f, trackThickness = 0.06f, trackSag = 0.05f   // Gefechtskette Kgs 73/800/152
        };

        /// <summary>
        /// Panzer III Ausf. L (Sd.Kfz. 141/1, 1942): 5 cm KwK 39 L/60. Modell: "Panzer III Medium Tank - Toshueyi" von Joanthan To
        /// (sketchfab.com/jonathanto99), CC BY 4.0 - gerigged und bemalt mit Tools/Blender/rig_toshueyi.py.
        /// </summary>
        public static TankSpec PanzerIII => new TankSpec
        {
            name = "Panzer III Ausf. L",
            // Maybach HL 120 TRM, 300 PS; 22,7 t; 40 km/h
            massT = 22.7f, powerPS = 300f, maxSpeedKmh = 40f, reverseKmh = 8f, turnRateDegS = 34f,
            hullLength = 5.45f, hullWidth = 2.4f, hullHeight = 1.25f, clearance = 0.38f,
            trackWidth = 0.40f, trackInset = 0.18f,      // Kettenmitte bei x = 1,22 m
            hullFront = new Armor(50, 9), hullRear = new Armor(50, 10), hullSide = new Armor(30, 0),
            hullRoof = new Armor(16, 0), hullBottom = new Armor(16, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(50, 21), hullGlacis = new Armor(25, 72), hullLowerSide = new Armor(30, 0),
            turretLength = 2.2f, turretWidth = 1.9f, turretHeight = 0.75f, turretOffsetZ = -0.02f,
            turretFront = new Armor(57, 15), turretSide = new Armor(30, 25), turretRear = new Armor(30, 12),
            turretRoof = new Armor(10, 0), mantlet = new Armor(50, 0),
            traverseDegS = 10f, elevationDegS = 5f, minElevation = -10f, maxElevation = 20f,
            ammoIndex = 21, ammo = new[] { 21, 22 }, reloadS = 4.5f, barrelLength = 2.6f, gunPivotFromModel = true,
            sightType = SightType.German, sightFov = 25f, sightMag = 2.5f, sightName = "TZF 5e",
            modelResource = "Modelle/PanzerIII", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            // Leitrad hinten (radL0), 6 Laufrollen (520 mm, Drehstab), Antriebsrad vorne (radL7), 3 Stuetzrollen
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 8, wheelRadius = 0.26f,
            linkTracks = true, idlerRadius = 0.37f, sprocketRadius = 0.42f,
            trackPitch = 0.12f, trackShoeWidth = 0.40f, trackThickness = 0.04f, trackSag = 0.03f,
            returnRollerLeft = "rollL", returnRollerRight = "rollR", returnRollerCount = 3, returnRollerRadius = 0.16f
        };

        /// <summary>
        /// Panther Ausf. A (Sd.Kfz. 171, 1943/44, mit Zimmerit und Schuerzen): 7,5 cm KwK 42 L/70, schraeger Bug 80 mm.
        /// Modell: "Panzer V Panther Medium Tank" von Joanthan To (sketchfab.com/jonathanto99), CC BY 4.0 - gerigged und bemalt mit Tools/Blender/rig_toshueyi.py.
        /// </summary>
        public static TankSpec Panther => new TankSpec
        {
            name = "Panther Ausf. A",
            // Maybach HL 230 P30, 700 PS; 44,8 t; 46 km/h
            massT = 44.8f, powerPS = 700f, maxSpeedKmh = 46f, reverseKmh = 10f, turnRateDegS = 26f,
            hullLength = 6.7f, hullWidth = 2.6f, hullHeight = 1.4f, clearance = 0.52f,
            trackWidth = 0.66f, trackInset = 0.38f,      // Kettenmitte bei x = 1,25 m
            hullFront = new Armor(80, 55), hullRear = new Armor(40, 30), hullSide = new Armor(40, 40),
            hullRoof = new Armor(16, 0), hullBottom = new Armor(16, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(60, 55), hullGlacis = new Armor(80, 55), hullLowerSide = new Armor(40, 0),
            turretLength = 2.9f, turretWidth = 2.3f, turretHeight = 0.95f, turretOffsetZ = -0.12f,
            turretFront = new Armor(110, 11, ArmorMaterial.Cast), turretSide = new Armor(45, 25), turretRear = new Armor(45, 25),
            turretRoof = new Armor(16, 0), mantlet = new Armor(100, 0, ArmorMaterial.Cast),
            traverseDegS = 18f, elevationDegS = 5f, minElevation = -8f, maxElevation = 18f,   // hydraulisch, abhaengig von der Motordrehzahl
            ammoIndex = 23, ammo = new[] { 23, 24 }, reloadS = 6.5f, barrelLength = 3.97f, muzzleBrake = true, gunPivotFromModel = true,
            // TZF 12a: umschaltbar 2,5- / 5-fach
            sightType = SightType.German, sightFov = 30f, sightMag = 2.5f, sightName = "TZF 12a",
            sightZoomFov = new[] { 30f, 15f }, sightZoomMag = new[] { 2.5f, 5f },
            modelResource = "Modelle/Panther", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            // Leitrad hinten (radL0), 8 Achsen mit verschachtelten Laufrollen (860 mm), Antriebsrad vorne (radL9)
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 10, wheelRadius = 0.41f,
            linkTracks = true, idlerRadius = 0.29f, sprocketRadius = 0.41f,
            trackPitch = 0.153f, trackShoeWidth = 0.66f, trackThickness = 0.05f, trackSag = 0.05f
        };

        /// <summary>
        /// Leopard 2A6 (Bundeswehr, ab 2001): 120 mm Rh L/55, Verbundpanzerung mit Keilpanzer am Turm, Feuerleitanlage EMES 15
        /// (Laser-Entfernungsmesser, Waermebild, Vorhalt), Waffenanlage zweiachsig stabilisiert. MG3 koaxial (zusaetzlich MG3 am Ladeschuetzen).
        /// Panzerung ungefaehr nach oeffentlichen Schaetzungen (mm = Schutz gegen Pfeilgeschosse; gegen Hohlladungen ~1,7-fach);
        /// die Schraege kommt aus der Modellform (Keile vorne am Turm).
        /// Modell: "Leopard 2 A6" von buh (sketchfab.com/buh-late), CC BY 4.0 - gerigged mit Tools/Blender/rig_leopard2.py.
        /// </summary>
        public static TankSpec Leopard2A6 => new TankSpec
        {
            name = "Leopard 2A6",
            // MTU MB 873 Ka-501, 1500 PS; 62,3 t; 68 km/h vorwaerts, 31 km/h rueckwaerts
            massT = 62.3f, powerPS = 1500f, maxSpeedKmh = 68f, reverseKmh = 31f, turnRateDegS = 36f,
            hullLength = 7.63f, hullWidth = 3.75f, hullHeight = 1.16f, clearance = 0.54f,
            trackWidth = 0.635f, trackInset = 0.80f,     // Kettenmitte bei x = 1,39 m
            hullFront = new Armor(400, 0, ArmorMaterial.Composite), hullRear = new Armor(30, 0), hullSide = new Armor(80, 0),
            hullRoof = new Armor(40, 0), hullBottom = new Armor(30, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(400, 0, ArmorMaterial.Composite),   // Bug mit Verbundmodulen
            hullGlacis = new Armor(60, 0),                                  // flache Oberwanne vorne: duenn, aber sehr schraeg
            hullLowerSide = new Armor(45, 0),
            turretLength = 4.0f, turretWidth = 3.0f, turretHeight = 0.85f, turretOffsetZ = 0.95f,
            turretFront = new Armor(550, 0, ArmorMaterial.Composite),       // Keilpanzer + Grundpanzerung: mit Schraege ~900 mm
            turretSide = new Armor(220, 0, ArmorMaterial.Composite), turretRear = new Armor(60, 0),
            turretRoof = new Armor(50, 0), mantlet = new Armor(350, 0, ArmorMaterial.Composite),
            traverseDegS = 40f, elevationDegS = 16f, minElevation = -9f, maxElevation = 20f,   // elektrisch, 360 Grad in ~9 s
            stabilizer = true, fireControl = true, hasRangefinder = true, hasThermal = true,
            // Ladeschuetze von Hand: ~5 s
            ammoIndex = 47, ammo = new[] { 47, 48, 49 }, reloadS = 5f, barrelLength = 5.1f, gunPivotFromModel = true, mgType = 6,
            crew = new[] { ModuleType.Gunner, ModuleType.Driver, ModuleType.Loader, ModuleType.Commander },
            // EMES 15: Tagkanal 12-fach, Waermebild (Taste V) umschaltbar 4- / 12-fach
            sightType = SightType.German, sightFov = 5f, sightMag = 12f, sightName = "EMES 15",
            sightZoomFov = new[] { 14f, 5f }, sightZoomMag = new[] { 4f, 12f },
            modelResource = "Modelle/Leopard2A6", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            // Antriebsrad hinten (radL0), 7 Laufrollen (700 mm, Drehstab), Leitrad vorne (radL8), 4 Stuetzrollen
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 9, wheelRadius = 0.345f,
            linkTracks = true, idlerRadius = 0.29f, sprocketRadius = 0.26f,
            trackPitch = 0.184f, trackShoeWidth = 0.635f, trackThickness = 0.08f, trackSag = 0.03f,   // Kette Diehl 570 FT
            returnRollerLeft = "rollL", returnRollerRight = "rollR", returnRollerCount = 4, returnRollerRadius = 0.09f
        };

        // ================= Sowjetische schwere Panzer =================
        // Modelle: "Soviet Heavy Tanks" von snrnsrk5 (sketchfab.com/snrnsrk5), CC BY 4.0 - gerigged mit Tools/Blender/rig_soviet.py.
        // Laufwerk: Antriebsrad hinten (radL0), Laufrollen mit Drehstabfederung, Leitrad vorne, 3 Stuetzrollen (rollL1..3).
        // (idlerRadius = hinteres Rad, sprocketRadius = vorderes Rad - im Spiel nur fuer die Kettenform.)
        // Visier, Koaxial-MG und Munition nach den echten Fahrzeugen; Winkel = Neigung von der Senkrechten.

        /// <summary>KV-1 Modell 1941 (geschweisster Turm): 76 mm F-32, rundum 75 mm. DT-MG koaxial (zusaetzlich Bug- und Heck-DT).</summary>
        public static TankSpec KV1 => new TankSpec
        {
            name = "KV-1 (1941)", nation = Nation.USSR,
            // V-2K Diesel 600 PS; 47,5 t; 35 km/h
            massT = 47.5f, powerPS = 600f, maxSpeedKmh = 35f, reverseKmh = 7f, turnRateDegS = 18f,
            hullLength = 6.77f, hullWidth = 3.32f, hullHeight = 1.26f, clearance = 0.44f,
            trackWidth = 0.70f, trackInset = 0.67f,      // Kettenmitte bei x = 1,34 m
            hullFront = new Armor(75, 30), hullRear = new Armor(60, 50), hullSide = new Armor(75, 0),
            hullRoof = new Armor(30, 0), hullBottom = new Armor(30, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(75, 25), hullGlacis = new Armor(60, 70), hullLowerSide = new Armor(75, 0),
            turretLength = 2.4f, turretWidth = 2.0f, turretHeight = 1.0f, turretOffsetZ = 0.45f,
            turretFront = new Armor(90, 15), turretSide = new Armor(75, 15), turretRear = new Armor(75, 15),
            turretRoof = new Armor(30, 0), mantlet = new Armor(90, 0, ArmorMaterial.Cast),
            traverseDegS = 12f, elevationDegS = 5f, minElevation = -5f, maxElevation = 25f,   // elektrischer Turmantrieb
            ammoIndex = 25, ammo = new[] { 25, 26 }, reloadS = 6f, barrelLength = 1.73f, gunPivotFromModel = true, mgType = 1,
            // TOD-6: 2,5-fach, 26 Grad
            sightType = SightType.Soviet, sightFov = 26f, sightMag = 2.5f, sightName = "TOD-6",
            modelResource = "Modelle/KV1", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 8, wheelRadius = 0.295f,
            linkTracks = true, idlerRadius = 0.41f, sprocketRadius = 0.33f,
            trackPitch = 0.16f, trackShoeWidth = 0.70f, trackThickness = 0.06f, trackSag = 0.04f,
            returnRollerLeft = "rollL", returnRollerRight = "rollR", returnRollerCount = 3, returnRollerRadius = 0.19f
        };

        /// <summary>IS-1 (IS-85, 1943): 85 mm D-5T, gestufter Bug 120 mm. 4 Mann, DT koaxial (zusaetzlich Heck-DT im Turm).</summary>
        public static TankSpec IS1 => new TankSpec
        {
            name = "IS-1", nation = Nation.USSR,
            // V-2IS 520 PS; 44,2 t; 37 km/h
            massT = 44.2f, powerPS = 520f, maxSpeedKmh = 37f, reverseKmh = 7f, turnRateDegS = 20f,
            hullLength = 6.70f, hullWidth = 3.07f, hullHeight = 1.17f, clearance = 0.42f,
            trackWidth = 0.65f, trackInset = 0.65f,      // Kettenmitte bei x = 1,21 m
            hullFront = new Armor(120, 30), hullRear = new Armor(60, 49), hullSide = new Armor(90, 15),
            hullRoof = new Armor(30, 0), hullBottom = new Armor(20, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(100, 30), hullGlacis = new Armor(60, 72), hullLowerSide = new Armor(90, 0),
            turretLength = 2.9f, turretWidth = 2.3f, turretHeight = 1.0f, turretOffsetZ = 0.78f,
            turretFront = new Armor(100, 0, ArmorMaterial.Cast), turretSide = new Armor(90, 20, ArmorMaterial.Cast),
            turretRear = new Armor(90, 30, ArmorMaterial.Cast), turretRoof = new Armor(30, 0), mantlet = new Armor(100, 0, ArmorMaterial.Cast),
            traverseDegS = 14f, elevationDegS = 5f, minElevation = -5f, maxElevation = 25f,
            ammoIndex = 27, ammo = new[] { 27, 28, 29 }, reloadS = 8f, barrelLength = 2.79f, gunPivotFromModel = true, mgType = 1,
            crew = new[] { ModuleType.Gunner, ModuleType.Driver, ModuleType.Loader, ModuleType.Commander },
            // TSh-17: 4-fach, 16 Grad
            sightType = SightType.Soviet, sightFov = 16f, sightMag = 4f, sightName = "TSh-17",
            modelResource = "Modelle/IS1", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 8, wheelRadius = 0.275f,
            linkTracks = true, idlerRadius = 0.36f, sprocketRadius = 0.275f,
            trackPitch = 0.16f, trackShoeWidth = 0.65f, trackThickness = 0.06f, trackSag = 0.04f,
            returnRollerLeft = "rollL", returnRollerRight = "rollR", returnRollerCount = 3, returnRollerRadius = 0.19f
        };

        /// <summary>IS-2 Modell 1944 (gerade Bugplatte): 122 mm D-25T mit Muendungsbremse, getrennte Munition. DT koaxial (zusaetzlich Heck-DT, DShK).</summary>
        public static TankSpec IS2 => new TankSpec
        {
            name = "IS-2 (1944)", nation = Nation.USSR,
            // V-2IS 520 PS; 46 t; 37 km/h
            massT = 46f, powerPS = 520f, maxSpeedKmh = 37f, reverseKmh = 7f, turnRateDegS = 20f,
            hullLength = 6.70f, hullWidth = 3.07f, hullHeight = 1.17f, clearance = 0.42f,
            trackWidth = 0.65f, trackInset = 0.65f,
            hullFront = new Armor(120, 60), hullRear = new Armor(60, 49), hullSide = new Armor(90, 15),
            hullRoof = new Armor(30, 0), hullBottom = new Armor(20, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(100, 30), hullGlacis = new Armor(120, 60), hullLowerSide = new Armor(90, 0),
            turretLength = 2.9f, turretWidth = 2.3f, turretHeight = 1.0f, turretOffsetZ = 0.78f,
            turretFront = new Armor(100, 0, ArmorMaterial.Cast), turretSide = new Armor(90, 20, ArmorMaterial.Cast),
            turretRear = new Armor(90, 30, ArmorMaterial.Cast), turretRoof = new Armor(30, 0), mantlet = new Armor(100, 0, ArmorMaterial.Cast),
            traverseDegS = 14f, elevationDegS = 4f, minElevation = -3f, maxElevation = 20f,
            // getrennte Munition (Geschoss + Kartusche), Keilverschluss: 2-3 Schuss/min
            ammoIndex = 31, ammo = new[] { 31, 30, 32 }, reloadS = 20f, barrelLength = 4.1f, muzzleBrake = true, gunPivotFromModel = true, mgType = 1,
            crew = new[] { ModuleType.Gunner, ModuleType.Driver, ModuleType.Loader, ModuleType.Commander },
            sightType = SightType.Soviet, sightFov = 16f, sightMag = 4f, sightName = "TSh-17",
            modelResource = "Modelle/IS2", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 8, wheelRadius = 0.275f,
            linkTracks = true, idlerRadius = 0.36f, sprocketRadius = 0.275f,
            trackPitch = 0.16f, trackShoeWidth = 0.65f, trackThickness = 0.06f, trackSag = 0.04f,
            returnRollerLeft = "rollL", returnRollerRight = "rollR", returnRollerCount = 3, returnRollerRadius = 0.19f
        };

        /// <summary>IS-3 (1945): Hechtbug 110 mm, flacher Gussturm bis 220 mm, Blende 250 mm. 122 mm D-25T. DT koaxial (zusaetzlich DShK).</summary>
        public static TankSpec IS3 => new TankSpec
        {
            name = "IS-3", nation = Nation.USSR,
            // V-11 520 PS; 46,5 t; 40 km/h
            massT = 46.5f, powerPS = 520f, maxSpeedKmh = 40f, reverseKmh = 8f, turnRateDegS = 20f,
            hullLength = 6.88f, hullWidth = 3.15f, hullHeight = 1.02f, clearance = 0.45f,
            trackWidth = 0.65f, trackInset = 0.63f,      // Kettenmitte bei x = 1,27 m
            hullFront = new Armor(110, 56), hullRear = new Armor(60, 50), hullSide = new Armor(90, 30),
            hullRoof = new Armor(30, 0), hullBottom = new Armor(20, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(110, 50), hullGlacis = new Armor(110, 72), hullLowerSide = new Armor(90, 0),
            turretLength = 3.0f, turretWidth = 2.5f, turretHeight = 0.9f, turretOffsetZ = 0.63f,
            turretFront = new Armor(220, 0, ArmorMaterial.Cast), turretSide = new Armor(150, 30, ArmorMaterial.Cast),
            turretRear = new Armor(100, 30, ArmorMaterial.Cast), turretRoof = new Armor(30, 0), mantlet = new Armor(250, 0, ArmorMaterial.Cast),
            traverseDegS = 14f, elevationDegS = 4f, minElevation = -3f, maxElevation = 20f,
            ammoIndex = 31, ammo = new[] { 31, 30, 32 }, reloadS = 20f, barrelLength = 4.16f, muzzleBrake = true, gunPivotFromModel = true, mgType = 1,
            crew = new[] { ModuleType.Gunner, ModuleType.Driver, ModuleType.Loader, ModuleType.Commander },
            sightType = SightType.Soviet, sightFov = 16f, sightMag = 4f, sightName = "TSh-17",
            modelResource = "Modelle/IS3", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 8, wheelRadius = 0.27f,
            linkTracks = true, idlerRadius = 0.35f, sprocketRadius = 0.28f,
            trackPitch = 0.16f, trackShoeWidth = 0.65f, trackThickness = 0.06f, trackSag = 0.04f,
            returnRollerLeft = "rollL", returnRollerRight = "rollR", returnRollerCount = 3, returnRollerRadius = 0.19f
        };

        /// <summary>T-10 (1953): 122 mm D-25TA mit Ansetzer, 7 Laufrollen. 12,7 mm DShKM koaxial und auf dem Turm.</summary>
        public static TankSpec T10 => new TankSpec
        {
            name = "T-10", nation = Nation.USSR,
            // V-12-5 700 PS; 50 t; 42 km/h
            massT = 50f, powerPS = 700f, maxSpeedKmh = 42f, reverseKmh = 9f, turnRateDegS = 22f,
            hullLength = 7.30f, hullWidth = 3.38f, hullHeight = 1.13f, clearance = 0.45f,
            trackWidth = 0.72f, trackInset = 0.72f,      // Kettenmitte bei x = 1,33 m
            hullFront = new Armor(120, 55), hullRear = new Armor(60, 45), hullSide = new Armor(80, 30),
            hullRoof = new Armor(30, 0), hullBottom = new Armor(20, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(120, 50), hullGlacis = new Armor(120, 72), hullLowerSide = new Armor(80, 0),
            turretLength = 3.1f, turretWidth = 2.5f, turretHeight = 1.0f, turretOffsetZ = 0.54f,
            turretFront = new Armor(201, 0, ArmorMaterial.Cast), turretSide = new Armor(148, 30, ArmorMaterial.Cast),
            turretRear = new Armor(80, 30, ArmorMaterial.Cast), turretRoof = new Armor(40, 0), mantlet = new Armor(250, 0, ArmorMaterial.Cast),
            traverseDegS = 16f, elevationDegS = 4f, minElevation = -3f, maxElevation = 17f,
            // Ansetzer: 3-4 Schuss/min
            ammoIndex = 31, ammo = new[] { 31, 30, 32 }, reloadS = 15f, barrelLength = 3.47f, muzzleBrake = true, gunPivotFromModel = true, mgType = 2,
            crew = new[] { ModuleType.Gunner, ModuleType.Driver, ModuleType.Loader, ModuleType.Commander },
            // TSh2-27: umschaltbar 3,1- / 6,2-fach
            sightType = SightType.Soviet, sightFov = 22f, sightMag = 3.1f, sightName = "TSh2-27",
            sightZoomFov = new[] { 22f, 11f }, sightZoomMag = new[] { 3.1f, 6.2f },
            modelResource = "Modelle/T10", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 9, wheelRadius = 0.277f,
            linkTracks = true, idlerRadius = 0.36f, sprocketRadius = 0.277f,
            trackPitch = 0.16f, trackShoeWidth = 0.72f, trackThickness = 0.06f, trackSag = 0.04f,
            returnRollerLeft = "rollL", returnRollerRight = "rollR", returnRollerCount = 3, returnRollerRadius = 0.155f
        };

        /// <summary>T-10M (1957): 122 mm M-62-T2 (schneller, mit Hohlladung), zweiachsig stabilisiert. 14,5 mm KPVT koaxial und auf dem Turm.</summary>
        public static TankSpec T10M => new TankSpec
        {
            name = "T-10M", nation = Nation.USSR, stabilizer = true,
            // V-12-6B 750 PS; 51,5 t; 50 km/h
            massT = 51.5f, powerPS = 750f, maxSpeedKmh = 50f, reverseKmh = 10f, turnRateDegS = 24f,
            hullLength = 7.30f, hullWidth = 3.38f, hullHeight = 1.13f, clearance = 0.45f,
            trackWidth = 0.72f, trackInset = 0.72f,
            hullFront = new Armor(120, 55), hullRear = new Armor(60, 45), hullSide = new Armor(80, 30),
            hullRoof = new Armor(30, 0), hullBottom = new Armor(20, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(120, 50), hullGlacis = new Armor(120, 72), hullLowerSide = new Armor(80, 0),
            turretLength = 3.1f, turretWidth = 2.5f, turretHeight = 1.0f, turretOffsetZ = 0.58f,
            turretFront = new Armor(201, 0, ArmorMaterial.Cast), turretSide = new Armor(148, 30, ArmorMaterial.Cast),
            turretRear = new Armor(80, 30, ArmorMaterial.Cast), turretRoof = new Armor(40, 0), mantlet = new Armor(250, 0, ArmorMaterial.Cast),
            traverseDegS = 18f, elevationDegS = 5f, minElevation = -3f, maxElevation = 17f,   // Stabilisator "Liwen"
            // mechanisierte Ladehilfe: ~5 Schuss/min
            ammoIndex = 33, ammo = new[] { 33, 35, 34 }, reloadS = 12f, barrelLength = 4.2f, muzzleBrake = true, gunPivotFromModel = true, mgType = 3,
            crew = new[] { ModuleType.Gunner, ModuleType.Driver, ModuleType.Loader, ModuleType.Commander },
            // T2S-29-14: stabilisiertes Periskop, 3,1- / 8-fach
            sightType = SightType.Soviet, sightFov = 20f, sightMag = 3.1f, sightName = "T2S-29-14",
            sightZoomFov = new[] { 20f, 7.8f }, sightZoomMag = new[] { 3.1f, 8f },
            modelResource = "Modelle/T10M", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 9, wheelRadius = 0.277f,
            linkTracks = true, idlerRadius = 0.36f, sprocketRadius = 0.277f,
            trackPitch = 0.16f, trackShoeWidth = 0.72f, trackThickness = 0.06f, trackSag = 0.04f,
            returnRollerLeft = "rollL", returnRollerRight = "rollR", returnRollerCount = 3, returnRollerRadius = 0.155f
        };

        /// <summary>
        /// Panzer IV Ausf. F2 (Sd.Kfz. 161/1, 1942): erste Version mit der langen 7,5 cm KwK 40 L/43
        /// (Kugel-Muendungsbremse), noch ohne Zusatzpanzerung und Schuerzen. Modell: Tools/Blender/rig_panzer4.py.
        /// </summary>
        public static TankSpec PanzerIV => new TankSpec
        {
            name = "Panzer IV Ausf. F2",
            // Maybach HL 120 TRM, 300 PS; 23,6 t; 40 km/h
            massT = 23.6f, powerPS = 300f, maxSpeedKmh = 40f, reverseKmh = 8f, turnRateDegS = 35f,
            hullLength = 5.76f, hullWidth = 2.4f, hullHeight = 1.31f, clearance = 0.40f,
            trackWidth = 0.40f, trackInset = 0.39f,     // Kettenmitte auf den Laufrollen des Modells (x = 1,21 m)
            hullFront = new Armor(50, 10), hullRear = new Armor(20, 10), hullSide = new Armor(30, 0),
            hullRoof = new Armor(12, 0), hullBottom = new Armor(10, 0),
            meshArmor = true,
            hullFrontLower = new Armor(50, 12),          // Bugplatte unten
            hullGlacis = new Armor(20, 72),              // Bugschraegplatte zwischen Bug und Aufbau
            hullLowerSide = new Armor(30, 0),
            turretLength = 1.77f, turretWidth = 1.8f, turretHeight = 0.58f, turretOffsetZ = -0.25f,
            turretFront = new Armor(50, 11), turretSide = new Armor(30, 25), turretRear = new Armor(30, 15),
            turretRoof = new Armor(10, 0), mantlet = new Armor(50, 0),
            traverseDegS = 14f, elevationDegS = 5f, minElevation = -8f, maxElevation = 20f,   // elektrischer Turmantrieb
            ammoIndex = 8, ammo = new[] { 8, 9, 10 }, reloadS = 6.5f, barrelLength = 2.73f, muzzleBrake = true,   // sichtbarer Teil ab Schildzapfen
            // Turmzielfernrohr TZF 5f: 2,5-fach, 25 Grad Sehfeld
            sightType = SightType.German, sightFov = 25f, sightMag = 2.5f, sightName = "TZF 5f",
            modelResource = "Modelle/PanzerIV_F2", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            // Leitrad hinten (radL0), 8 Laufrollen (470 mm, Blattfeder-Drehgestelle), Antriebsrad vorne (radL9)
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 10, wheelRadius = 0.23f,
            linkTracks = true, idlerRadius = 0.30f, sprocketRadius = 0.37f,
            trackPitch = 0.12f, trackShoeWidth = 0.40f, trackThickness = 0.04f, trackSag = 0.04f,  // Kette Kgs 61/400/120
            returnRollerLeft = "rollL", returnRollerRight = "rollR", returnRollerCount = 4, returnRollerRadius = 0.129f
        };

        /// <summary>
        /// M4A1E9: wiederaufgebauter M4A1 (Gusswanne) mit 75 mm M3, Zusatzpanzerung, Kommandantenkuppel und
        /// verbreitertem VVSS-Laufwerk (Distanzstuecke + verlaengerte Endverbinder).
        /// Masse aus dem 3D-Modell (Tools/Blender/rig_sherman.py, auf 5,84 m Wannenlaenge skaliert).
        /// </summary>
        public static TankSpec Sherman => new TankSpec
        {
            name = "M4A1E9 Sherman", nation = Nation.USA,
            // Continental R975 Sternmotor, 400 PS; ca. 31 t mit Zusatzpanzerung; 39 km/h (24 mph)
            massT = 31f, powerPS = 400f, maxSpeedKmh = 39f, reverseKmh = 6f, turnRateDegS = 28f,
            hullLength = 5.84f, hullWidth = 2.6f, hullHeight = 1.52f, clearance = 0.43f,
            trackWidth = 0.42f, trackInset = 0.48f,      // Kettenmitte liegt auf den Laufrollen des Modells (x = 1,03 m)
            // Gusswanne: Bug 51 mm, je nach Stelle 37-55 Grad geneigt (genauer Winkel kommt aus der Modellform)
            hullFront = new Armor(51, 47, ArmorMaterial.Cast), hullRear = new Armor(38, 10),
            hullSide = new Armor(38, 0, ArmorMaterial.Cast), hullRoof = new Armor(19, 0), hullBottom = new Armor(19, 0),
            meshArmor = true,
            hullFrontLower = new Armor(64, 45, ArmorMaterial.Cast),   // Getriebegehaeuse (Guss, 51-108 mm gewoelbt)
            hullGlacis = new Armor(51, 55, ArmorMaterial.Cast),
            hullLowerSide = new Armor(38, 0),                         // untere Wanne hinter den Laufrollen (gewalzt)
            turretLength = 2.5f, turretWidth = 2.0f, turretHeight = 0.77f, turretOffsetZ = -0.2f,
            turretFront = new Armor(76, 30, ArmorMaterial.Cast), turretSide = new Armor(51, 5, ArmorMaterial.Cast),
            turretRear = new Armor(51, 0, ArmorMaterial.Cast), turretRoof = new Armor(25, 0),
            mantlet = new Armor(89, 0, ArmorMaterial.Cast),           // Blende M34A1: Schild 51 mm + Rotor 38 mm
            traverseDegS = 24f, elevationDegS = 5f, minElevation = -10f, maxElevation = 25f,   // hydraulisch 360 Grad in 15 s
            // 75 mm M3: M61 APC (mit Sprengladung) und M72 AP (Vollgeschoss)
            ammoIndex = 2, ammo = new[] { 2, 6, 13 }, reloadS = 6f, barrelLength = 1.93f, mgType = 4,   // sichtbarer Teil ab Schildzapfen
            // Richtschuetze: Zielfernrohr M70F in der Blende M34A1, 3-fach, 12,7 Grad Sehfeld
            sightType = SightType.US, sightFov = 12.7f, sightMag = 3f, sightName = "M70F",
            modelResource = "Modelle/M4A1E9", modelScale = 1f,
            modelTurretPivotShift = new Vector3(0f, 0f, 0.23f),   // Turmring-Mitte liegt 23 cm vor dem Turmgelenk des Modells
            modelTurretBone = "turret", modelGunBone = "cannon",
            // Leitrad hinten (radL0), 6 Laufrollen (VVSS, 508 mm), Antriebsrad vorne (radL7)
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 8, wheelRadius = 0.257f,
            linkTracks = true, idlerRadius = 0.306f, sprocketRadius = 0.32f,
            trackPitch = 0.152f, trackShoeWidth = 0.42f, trackThickness = 0.05f, trackSag = 0.04f   // Kette T48/T51: 6 Zoll Teilung
        };

        /// <summary>
        /// Sherman Firefly VC (britisch, 1944, auf M4A4): 17-Pfuender (76,2 mm) - der einzige westalliierte Panzer, der Tiger und Panther
        /// frontal auf normale Kampfentfernung bekaempfen konnte. Funker/Bug-MG entfallen (Platz fuer Munition), Funkgeraet im Turmanbau hinten.
        /// Modell: "M4 Sherman Firefly" von barking_dogo (sketchfab.com/barking_dogo), CC BY 4.0 - gerigged mit Tools/Blender/rig_firefly.py.
        /// </summary>
        public static TankSpec Firefly => new TankSpec
        {
            name = "Sherman Firefly VC", nation = Nation.UK,
            // Chrysler A57 Multibank 425 PS; 34,8 t; 40 km/h
            massT = 34.8f, powerPS = 425f, maxSpeedKmh = 40f, reverseKmh = 6f, turnRateDegS = 26f,
            hullLength = 6.24f, hullWidth = 2.62f, hullHeight = 1.42f, clearance = 0.43f,
            trackWidth = 0.42f, trackInset = 0.46f,      // Kettenmitte bei x = 1,06 m
            hullFront = new Armor(51, 56), hullRear = new Armor(38, 10), hullSide = new Armor(38, 0),
            hullRoof = new Armor(19, 0), hullBottom = new Armor(13, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(64, 45, ArmorMaterial.Cast),   // Getriebegehaeuse (Guss, 51-108 mm gewoelbt)
            hullGlacis = new Armor(51, 56), hullLowerSide = new Armor(38, 0),
            turretLength = 2.9f, turretWidth = 2.0f, turretHeight = 0.8f, turretOffsetZ = 0.19f,
            turretFront = new Armor(76, 30, ArmorMaterial.Cast), turretSide = new Armor(51, 5, ArmorMaterial.Cast),
            turretRear = new Armor(51, 0, ArmorMaterial.Cast), turretRoof = new Armor(25, 0), mantlet = new Armor(89, 0, ArmorMaterial.Cast),
            traverseDegS = 24f, elevationDegS = 5f, minElevation = -5f, maxElevation = 20f,   // hydraulisch wie beim Sherman
            ammoIndex = 5, ammo = new[] { 5, 44, 42, 43 }, reloadS = 7f, barrelLength = 3.53f, muzzleBrake = true, gunPivotFromModel = true, mgType = 4,
            crew = new[] { ModuleType.Gunner, ModuleType.Driver, ModuleType.Loader, ModuleType.Commander },
            // Zielfernrohr No. 43 x3 ML: 3-fach, 13 Grad
            sightType = SightType.US, sightFov = 13f, sightMag = 3f, sightName = "No. 43 x3 ML",
            modelResource = "Modelle/Firefly", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            // Leitrad hinten (radL0), 6 Laufrollen (VVSS, 508 mm), Antriebsrad vorne (radL7), Stuetzrollen auf den Drehgestellen
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 8, wheelRadius = 0.243f,
            linkTracks = true, idlerRadius = 0.287f, sprocketRadius = 0.31f,
            trackPitch = 0.152f, trackShoeWidth = 0.42f, trackThickness = 0.05f, trackSag = 0.04f,
            returnRollerLeft = "rollL", returnRollerRight = "rollR", returnRollerCount = 3, returnRollerRadius = 0.112f
        };

        /// <summary>
        /// Wiesel 1 TOW (Bundeswehr, Luftlande-Waffentraeger, ab 1989): Modell im Code gebaut nach Risszeichnungen
        /// (Tools/Blender/build_wiesel.py). Vorerst nur fahrbar - die TOW-Lenkrakete kommt spaeter.
        /// Werte ungefaehr: 2,8 t, Audi-5-Zylinder-Turbodiesel 87 PS, 70 km/h; Panzerung nur gegen Gewehrfeuer und Splitter.
        /// </summary>
        public static TankSpec Wiesel => new TankSpec
        {
            name = "Wiesel 1 TOW",
            massT = 2.8f, powerPS = 87f, maxSpeedKmh = 70f, reverseKmh = 15f, turnRateDegS = 55f,
            hullLength = 3.30f, hullWidth = 1.58f, hullHeight = 0.78f, clearance = 0.30f,
            trackWidth = 0.24f, trackInset = 0.12f,          // Kettenmitte bei x = 0,79 m (Breite gesamt 1,82 m)
            hullFront = new Armor(10, 60), hullRear = new Armor(6, 10), hullSide = new Armor(8, 10),
            hullRoof = new Armor(6, 0), hullBottom = new Armor(6, 0),
            turretLength = 0.5f, turretWidth = 0.45f, turretHeight = 0.5f, turretOffsetZ = -0.40f,
            turretFront = new Armor(6, 0), turretSide = new Armor(6, 0), turretRear = new Armor(6, 0),
            turretRoof = new Armor(4, 0), mantlet = new Armor(6, 0),
            traverseDegS = 30f, elevationDegS = 15f, minElevation = -10f, maxElevation = 25f,
            // TOW-2 und TOW; Nachladen von Hand durch die Luke (~20 s), nur eine Rakete gleichzeitig im Flug (Draht)
            ammoIndex = 16, ammo = new[] { 16, 15 }, reloadS = 20f, barrelLength = 0.85f, devOnly = true, noMg = true,
            // 3 Mann: Fahrer (vorne links), Richtschuetze (hinten rechts unter dem Werfer), Kommandant (hinten links)
            crew = new[] { ModuleType.Driver, ModuleType.Gunner, ModuleType.Commander }, crewInHull = true,
            gunPivotFromModel = true,
            // TOW-Zielgeraet: Tagkanal 13-fach, Waermebild (Taste V) mit weitem Feld 4-fach
            sightType = SightType.TOW, sightFov = 4.5f, sightMag = 13f, sightName = "TOW-Zielgeraet",
            sightZoomFov = new[] { 13f, 4.5f }, sightZoomMag = new[] { 4f, 13f }, hasThermal = true,   // Mausrad: 4x / 13x
            modelResource = "Modelle/Wiesel", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            // Leitrad hinten (radL0, laeuft mit am Boden), 3 Laufrollen, Antriebsrad vorne hochgesetzt (radL4)
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 5, wheelRadius = 0.19f,
            linkTracks = true, idlerRadius = 0.235f, sprocketRadius = 0.14f,
            trackPitch = 0.10f, trackShoeWidth = 0.24f, trackThickness = 0.035f, trackSag = 0.03f,
            returnRollerLeft = "rollL", returnRollerRight = "rollR", returnRollerCount = 1, returnRollerRadius = 0.065f
        };

        /// <summary>
        /// Panzerhaubitze 2000 (Bundeswehr, ab 1998): 155 mm L/52, Feuerleitrechner, Laser-Entfernungsmesser.
        /// Modell: "Panzerhaubitze 2000" von gogiart (sketchfab.com/agt14032013), CC BY 4.0 - gerigged mit Tools/Blender/rig_pzh2000.py.
        /// Werte ungefaehr: 55,8 t, MTU 881 mit 1000 PS, 67 km/h; Panzerung gegen 14,5 mm und Artilleriesplitter.
        /// Artillerie-Modus: Taste M (Karte, Ziel anklicken, Leertaste feuert).
        /// </summary>
        public static TankSpec PzH2000 => new TankSpec
        {
            name = "Panzerhaubitze 2000",
            massT = 55.8f, powerPS = 1000f, maxSpeedKmh = 67f, reverseKmh = 20f, turnRateDegS = 25f,
            hullLength = 7.0f, hullWidth = 3.0f, hullHeight = 1.24f, clearance = 0.44f,
            trackWidth = 0.55f, trackInset = 0.5f,
            hullFront = new Armor(30, 60), hullRear = new Armor(12, 10), hullSide = new Armor(15, 0),
            hullRoof = new Armor(15, 0), hullBottom = new Armor(10, 0),
            turretLength = 4.6f, turretWidth = 3.0f, turretHeight = 1.0f, turretOffsetZ = -1.5f,
            turretFront = new Armor(25, 20), turretSide = new Armor(15, 10), turretRear = new Armor(12, 0),
            turretRoof = new Armor(15, 0), mantlet = new Armor(20, 0),
            traverseDegS = 20f, elevationDegS = 10f, minElevation = -2.5f, maxElevation = 65f,
            // Ladeautomat: Feuerstoss 3 Schuss in ~10 s
            ammoIndex = 17, ammo = new[] { 17 }, reloadS = 3.5f, barrelLength = 5.9f, muzzleBrake = true, noMg = true,
            gunPivotFromModel = true, hasRangefinder = true, artillery = true, devOnly = true,
            sightType = SightType.US, sightFov = 7f, sightMag = 8f, sightName = "Richtaufsatz (direktes Feuer)",
            modelResource = "Modelle/PzH2000", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            // Antriebsrad hinten (radL0), 7 Laufrollen, Leitrad vorne (radL8); Kette des Modells (Textur laeuft mit)
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 9, wheelRadius = 0.30f,
            idlerRadius = 0.28f, sprocketRadius = 0.23f,
            trackMaterialLeft = "kette_links", trackMaterialRight = "kette_rechts", trackMetersPerUV = 1.0f
        };

        public static TankSpec T34_85 => new TankSpec
        {
            name = "T-34-85", nation = Nation.USSR,
            massT = 32f, powerPS = 500f, maxSpeedKmh = 54f, reverseKmh = 8f, turnRateDegS = 32f,
            // Masse passend zum Modell (DedRam, CC BY 4.0), auf echte Groesse umgerechnet
            hullLength = 6.0f, hullWidth = 2.4f, hullHeight = 1.35f, clearance = 0.3f,
            trackWidth = 0.5f, trackInset = 0.45f,
            hullFront = new Armor(45, 60), hullRear = new Armor(45, 47), hullSide = new Armor(45, 40),
            hullRoof = new Armor(20, 0), hullBottom = new Armor(20, 0),
            meshArmor = true,
            hullFrontLower = new Armor(45, 53), hullGlacis = new Armor(45, 60), hullLowerSide = new Armor(45, 0),
            turretLength = 2.65f, turretWidth = 1.86f, turretHeight = 0.89f, turretOffsetZ = 0f,
            turretFront = new Armor(90, 20, ArmorMaterial.Cast), turretSide = new Armor(75, 20, ArmorMaterial.Cast),
            turretRear = new Armor(52, 10, ArmorMaterial.Cast), turretRoof = new Armor(20, 0),
            mantlet = new Armor(40, 0, ArmorMaterial.Cast),
            traverseDegS = 20f, elevationDegS = 5f, minElevation = -5f, maxElevation = 22f,
            ammoIndex = 4, ammo = new[] { 4, 7, 14 }, reloadS = 8f, barrelLength = 3.2f, mgType = 1,
            sightType = SightType.Soviet, sightFov = 16f, sightMag = 4f, sightName = "TSh-16",
            modelResource = "Modelle/T34_85", modelScale = 1f,
            modelTurretPivotShift = new Vector3(0f, 0f, 0.25f),   // Turmring-Mitte liegt 25 cm vor dem Turmgelenk des Modells (sonst Loch beim Drehen)
            meshArmorFixWinding = true,                           // Turm des Modells hat nach innen zeigende Flaechen
            modelTurretBone = "turret", modelGunBone = "cannon",
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 7, wheelRadius = 0.4f,
            linkTracks = true, idlerRadius = 0.281f, sprocketRadius = 0.233f,   // hinten Antriebsrad, vorne Leitrad
            trackPitch = 0.172f, trackShoeWidth = 0.5f, trackThickness = 0.06f, trackSag = 0.03f
        };

        // ================= Sowjetische mittlere Panzer =================
        // Modelle: "Soviet Medium Tanks" von snrnsrk5 (sketchfab.com/snrnsrk5), CC BY 4.0 - gerigged mit Tools/Blender/rig_soviet.py.
        // Laufwerk: Antriebsrad hinten (radL0), 5 grosse Laufrollen (T-34: Christie, sonst Drehstab), Leitrad vorne (radL6), keine Stuetzrollen.

        /// <summary>T-34 Modell 1943 (Sechseckturm): 76 mm F-34, schraege 45-mm-Wanne. 4 Mann, DT koaxial (zusaetzlich Bug-DT).</summary>
        public static TankSpec T34 => new TankSpec
        {
            name = "T-34 (1943)", nation = Nation.USSR,
            // V-2-34 Diesel 500 PS; 30,9 t; 54 km/h
            massT = 30.9f, powerPS = 500f, maxSpeedKmh = 54f, reverseKmh = 8f, turnRateDegS = 32f,
            hullLength = 5.92f, hullWidth = 3.0f, hullHeight = 1.14f, clearance = 0.40f,
            trackWidth = 0.50f, trackInset = 0.53f,      // Kettenmitte bei x = 1,22 m
            hullFront = new Armor(45, 60), hullRear = new Armor(45, 47), hullSide = new Armor(45, 40),
            hullRoof = new Armor(20, 0), hullBottom = new Armor(15, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(45, 53), hullGlacis = new Armor(45, 60), hullLowerSide = new Armor(45, 0),
            turretLength = 2.3f, turretWidth = 1.8f, turretHeight = 0.8f, turretOffsetZ = 0.42f,
            turretFront = new Armor(60, 20, ArmorMaterial.Cast), turretSide = new Armor(52, 20, ArmorMaterial.Cast),
            turretRear = new Armor(52, 10, ArmorMaterial.Cast), turretRoof = new Armor(20, 0), mantlet = new Armor(52, 0, ArmorMaterial.Cast),
            traverseDegS = 24f, elevationDegS = 6f, minElevation = -5f, maxElevation = 30f,
            ammoIndex = 36, ammo = new[] { 36, 37 }, reloadS = 6f, barrelLength = 1.77f, gunPivotFromModel = true, mgType = 1,
            crew = new[] { ModuleType.Gunner, ModuleType.Driver, ModuleType.Loader, ModuleType.RadioOperator },   // Kommandant war zugleich Richtschuetze
            // TMFD-7: 2,5-fach, 15 Grad
            sightType = SightType.Soviet, sightFov = 15f, sightMag = 2.5f, sightName = "TMFD-7",
            modelResource = "Modelle/T34", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 7, wheelRadius = 0.415f,
            linkTracks = true, idlerRadius = 0.30f, sprocketRadius = 0.25f,
            trackPitch = 0.172f, trackShoeWidth = 0.50f, trackThickness = 0.06f, trackSag = 0.03f
        };

        /// <summary>T-34-85 Modell 1944: 85 mm ZiS-S-53, Dreimannturm. DT koaxial (zusaetzlich Bug-DT).</summary>
        public static TankSpec T34_85_44 => new TankSpec
        {
            name = "T-34-85 (1944)", nation = Nation.USSR,
            massT = 32f, powerPS = 500f, maxSpeedKmh = 54f, reverseKmh = 8f, turnRateDegS = 32f,
            hullLength = 5.92f, hullWidth = 3.0f, hullHeight = 1.15f, clearance = 0.40f,
            trackWidth = 0.50f, trackInset = 0.53f,
            hullFront = new Armor(45, 60), hullRear = new Armor(45, 47), hullSide = new Armor(45, 40),
            hullRoof = new Armor(20, 0), hullBottom = new Armor(15, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(45, 53), hullGlacis = new Armor(45, 60), hullLowerSide = new Armor(45, 0),
            turretLength = 2.65f, turretWidth = 1.86f, turretHeight = 0.89f, turretOffsetZ = 0.42f,
            turretFront = new Armor(90, 20, ArmorMaterial.Cast), turretSide = new Armor(75, 20, ArmorMaterial.Cast),
            turretRear = new Armor(52, 10, ArmorMaterial.Cast), turretRoof = new Armor(20, 0), mantlet = new Armor(40, 0, ArmorMaterial.Cast),
            traverseDegS = 20f, elevationDegS = 5f, minElevation = -5f, maxElevation = 22f,
            ammoIndex = 4, ammo = new[] { 4, 7, 14 }, reloadS = 8f, barrelLength = 3.24f, gunPivotFromModel = true, mgType = 1,
            sightType = SightType.Soviet, sightFov = 16f, sightMag = 4f, sightName = "TSh-16",
            modelResource = "Modelle/T3485", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 7, wheelRadius = 0.415f,
            linkTracks = true, idlerRadius = 0.30f, sprocketRadius = 0.25f,
            trackPitch = 0.172f, trackShoeWidth = 0.50f, trackThickness = 0.06f, trackSag = 0.03f
        };

        /// <summary>T-44 (1944): quer eingebauter Motor, Bug 90 mm, 85 mm ZiS-S-53. 4 Mann, DT koaxial (zusaetzlich Bug-DT).</summary>
        public static TankSpec T44 => new TankSpec
        {
            name = "T-44", nation = Nation.USSR,
            // V-44 520 PS; 31,9 t; 51 km/h
            massT = 31.9f, powerPS = 520f, maxSpeedKmh = 51f, reverseKmh = 8f, turnRateDegS = 30f,
            hullLength = 6.07f, hullWidth = 3.18f, hullHeight = 0.97f, clearance = 0.42f,
            trackWidth = 0.50f, trackInset = 0.54f,      // Kettenmitte bei x = 1,30 m
            hullFront = new Armor(90, 60), hullRear = new Armor(30, 0), hullSide = new Armor(75, 0),
            hullRoof = new Armor(15, 0), hullBottom = new Armor(15, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(90, 45), hullGlacis = new Armor(90, 60), hullLowerSide = new Armor(75, 0),
            turretLength = 2.6f, turretWidth = 2.1f, turretHeight = 0.9f, turretOffsetZ = 0f,
            turretFront = new Armor(120, 0, ArmorMaterial.Cast), turretSide = new Armor(90, 20, ArmorMaterial.Cast),
            turretRear = new Armor(75, 10, ArmorMaterial.Cast), turretRoof = new Armor(20, 0), mantlet = new Armor(120, 0, ArmorMaterial.Cast),
            traverseDegS = 15f, elevationDegS = 5f, minElevation = -5f, maxElevation = 25f,
            ammoIndex = 4, ammo = new[] { 4, 7, 14 }, reloadS = 8f, barrelLength = 3.33f, gunPivotFromModel = true, mgType = 1,
            crew = new[] { ModuleType.Gunner, ModuleType.Driver, ModuleType.Loader, ModuleType.Commander },
            sightType = SightType.Soviet, sightFov = 16f, sightMag = 4f, sightName = "TSh-16",
            modelResource = "Modelle/T44", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 7, wheelRadius = 0.415f,
            linkTracks = true, idlerRadius = 0.26f, sprocketRadius = 0.25f,
            trackPitch = 0.16f, trackShoeWidth = 0.50f, trackThickness = 0.06f, trackSag = 0.03f
        };

        /// <summary>T-44-100 (Versuchsfahrzeug 1945): T-44 mit 100 mm D-10T. DT koaxial.</summary>
        public static TankSpec T44_100 => new TankSpec
        {
            name = "T-44-100", nation = Nation.USSR,
            massT = 34.2f, powerPS = 520f, maxSpeedKmh = 50f, reverseKmh = 8f, turnRateDegS = 29f,
            hullLength = 6.07f, hullWidth = 3.18f, hullHeight = 0.99f, clearance = 0.42f,
            trackWidth = 0.50f, trackInset = 0.54f,
            hullFront = new Armor(90, 60), hullRear = new Armor(30, 0), hullSide = new Armor(75, 0),
            hullRoof = new Armor(15, 0), hullBottom = new Armor(15, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(90, 45), hullGlacis = new Armor(90, 60), hullLowerSide = new Armor(75, 0),
            turretLength = 2.7f, turretWidth = 2.2f, turretHeight = 0.85f, turretOffsetZ = 0f,
            turretFront = new Armor(120, 0, ArmorMaterial.Cast), turretSide = new Armor(90, 20, ArmorMaterial.Cast),
            turretRear = new Armor(75, 10, ArmorMaterial.Cast), turretRoof = new Armor(20, 0), mantlet = new Armor(120, 0, ArmorMaterial.Cast),
            traverseDegS = 14f, elevationDegS = 4f, minElevation = -5f, maxElevation = 20f,
            ammoIndex = 38, ammo = new[] { 38, 39 }, reloadS = 10f, barrelLength = 4.09f, gunPivotFromModel = true, mgType = 1,
            crew = new[] { ModuleType.Gunner, ModuleType.Driver, ModuleType.Loader, ModuleType.Commander },
            sightType = SightType.Soviet, sightFov = 18f, sightMag = 3.5f, sightName = "TSh-20",
            modelResource = "Modelle/T44100", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 7, wheelRadius = 0.415f,
            linkTracks = true, idlerRadius = 0.26f, sprocketRadius = 0.25f,
            trackPitch = 0.16f, trackShoeWidth = 0.50f, trackThickness = 0.06f, trackSag = 0.03f
        };

        /// <summary>T-54 Modell 1951 (Kuppelturm): 100 mm D-10T, Bug 100 mm, Turm bis 200 mm. SGMT koaxial (zusaetzlich DShK und Bug-SGMT).</summary>
        public static TankSpec T54 => new TankSpec
        {
            name = "T-54 (1951)", nation = Nation.USSR,
            // V-54 520 PS; 36 t; 50 km/h
            massT = 36f, powerPS = 520f, maxSpeedKmh = 50f, reverseKmh = 8f, turnRateDegS = 30f,
            hullLength = 6.33f, hullWidth = 3.27f, hullHeight = 1.11f, clearance = 0.425f,
            trackWidth = 0.58f, trackInset = 0.6f,       // Kettenmitte bei x = 1,33 m
            hullFront = new Armor(100, 60), hullRear = new Armor(45, 0), hullSide = new Armor(80, 0),
            hullRoof = new Armor(30, 0), hullBottom = new Armor(20, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(100, 55), hullGlacis = new Armor(100, 60), hullLowerSide = new Armor(80, 0),
            turretLength = 2.6f, turretWidth = 2.3f, turretHeight = 0.85f, turretOffsetZ = -0.1f,
            turretFront = new Armor(200, 0, ArmorMaterial.Cast), turretSide = new Armor(160, 30, ArmorMaterial.Cast),
            turretRear = new Armor(65, 0, ArmorMaterial.Cast), turretRoof = new Armor(30, 0), mantlet = new Armor(200, 0, ArmorMaterial.Cast),
            traverseDegS = 18f, elevationDegS = 4f, minElevation = -5f, maxElevation = 18f,
            ammoIndex = 40, ammo = new[] { 40, 45, 38, 39 }, reloadS = 8.5f, barrelLength = 3.8f, gunPivotFromModel = true, mgType = 5,
            crew = new[] { ModuleType.Gunner, ModuleType.Driver, ModuleType.Loader, ModuleType.Commander },
            // TSh-20: 3,5-fach, 18 Grad
            sightType = SightType.Soviet, sightFov = 18f, sightMag = 3.5f, sightName = "TSh-20",
            modelResource = "Modelle/T54", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 7, wheelRadius = 0.398f,
            linkTracks = true, idlerRadius = 0.31f, sprocketRadius = 0.25f,
            trackPitch = 0.137f, trackShoeWidth = 0.58f, trackThickness = 0.05f, trackSag = 0.03f   // Kette OMSh
        };

        /// <summary>T-55A (1963): 100 mm D-10T2S, zweiachsig stabilisiert, Strahlenschutz. SGMT koaxial (zusaetzlich Bug-SGMT).</summary>
        public static TankSpec T55A => new TankSpec
        {
            name = "T-55A", nation = Nation.USSR, stabilizer = true,
            // V-55 580 PS; 38 t; 50 km/h
            massT = 38f, powerPS = 580f, maxSpeedKmh = 50f, reverseKmh = 8f, turnRateDegS = 32f,
            hullLength = 6.33f, hullWidth = 3.27f, hullHeight = 1.10f, clearance = 0.425f,
            trackWidth = 0.58f, trackInset = 0.6f,
            hullFront = new Armor(100, 60), hullRear = new Armor(45, 0), hullSide = new Armor(80, 0),
            hullRoof = new Armor(30, 0), hullBottom = new Armor(20, 0),
            meshArmor = true, meshArmorFixWinding = true,
            hullFrontLower = new Armor(100, 55), hullGlacis = new Armor(100, 60), hullLowerSide = new Armor(80, 0),
            turretLength = 2.6f, turretWidth = 2.3f, turretHeight = 0.85f, turretOffsetZ = -0.12f,
            turretFront = new Armor(205, 0, ArmorMaterial.Cast), turretSide = new Armor(160, 30, ArmorMaterial.Cast),
            turretRear = new Armor(65, 0, ArmorMaterial.Cast), turretRoof = new Armor(30, 0), mantlet = new Armor(205, 0, ArmorMaterial.Cast),
            traverseDegS = 20f, elevationDegS = 4.5f, minElevation = -5f, maxElevation = 18f,   // Stabilisator "Zyklon"
            ammoIndex = 40, ammo = new[] { 40, 45, 46, 41, 39 }, reloadS = 8f, barrelLength = 3.86f, gunPivotFromModel = true, mgType = 5,
            crew = new[] { ModuleType.Gunner, ModuleType.Driver, ModuleType.Loader, ModuleType.Commander },
            // TSh2B-32P: umschaltbar 3,5- / 7-fach
            sightType = SightType.Soviet, sightFov = 18f, sightMag = 3.5f, sightName = "TSh2B-32P",
            sightZoomFov = new[] { 18f, 9f }, sightZoomMag = new[] { 3.5f, 7f },
            modelResource = "Modelle/T55A", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 7, wheelRadius = 0.404f,
            linkTracks = true, idlerRadius = 0.31f, sprocketRadius = 0.25f,
            trackPitch = 0.137f, trackShoeWidth = 0.58f, trackThickness = 0.05f, trackSag = 0.03f
        };
    }
}
