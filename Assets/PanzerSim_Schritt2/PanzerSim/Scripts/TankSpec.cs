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
    public enum SightType { German, US, Soviet }

    /// <summary>Alle Daten eines Panzertyps. Masse in Metern, Panzerung in mm.</summary>
    [System.Serializable]
    public class TankSpec
    {
        public string name;
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
            // Masse an das 3D-Modell angepasst (MaÃŸstab 1,2 = echte Tiger-Groesse)
            hullLength = 6.15f, hullWidth = 3.2f, hullHeight = 1.5f, clearance = 0.35f,
            trackWidth = 0.725f, trackInset = 0.49f,
            hullFront = new Armor(100, 9), hullRear = new Armor(80, 9), hullSide = new Armor(80, 0),
            hullRoof = new Armor(25, 0), hullBottom = new Armor(25, 0),
            turretLength = 2.75f, turretWidth = 2.1f, turretHeight = 0.86f, turretOffsetZ = 0f,
            turretFront = new Armor(100, 0), turretSide = new Armor(80, 0), turretRear = new Armor(80, 0),
            turretRoof = new Armor(25, 0), mantlet = new Armor(110, 0),
            traverseDegS = 7f, elevationDegS = 4f, minElevation = -8f, maxElevation = 15f,
            ammoIndex = 1, ammo = new[] { 1, 11, 12 }, reloadS = 8.5f, barrelLength = 3.5f, muzzleBrake = true,
            sightType = SightType.German, sightFov = 25f, sightMag = 2.5f, sightName = "TZF 9c",
            sightZoomFov = new[] { 25f, 14f }, sightZoomMag = new[] { 2.5f, 5f },
            modelResource = "Modelle/Tiger", modelScale = 1.2f,
            meshArmor = true,
            hullFrontLower = new Armor(100, 24), hullGlacis = new Armor(60, 80), hullLowerSide = new Armor(60, 0),
            wheelBoneLeft = "wheeltrackleft", wheelBoneRight = "wheeltrackright", wheelBoneCount = 10, wheelRadius = 0.33f,
            linkTracks = true, idlerRadius = 0.256f, sprocketRadius = 0.33f,
            trackPitch = 0.13f, trackShoeWidth = 0.725f, trackThickness = 0.05f, trackSag = 0.06f
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
            name = "M4A1E9 Sherman",
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
            ammoIndex = 2, ammo = new[] { 2, 6, 13 }, reloadS = 6f, barrelLength = 1.93f,   // sichtbarer Teil ab Schildzapfen
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
            ammoIndex = 12, ammo = new[] { 12 }, reloadS = 15f, barrelLength = 1.0f, noGun = true, devOnly = true,
            // TOW-Zielgeraet: 13-fach
            sightType = SightType.US, sightFov = 4.5f, sightMag = 13f, sightName = "TOW-Zielgeraet (Rakete folgt)",
            modelResource = "Modelle/Wiesel", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            // Leitrad hinten (radL0, laeuft mit am Boden), 3 Laufrollen, Antriebsrad vorne hochgesetzt (radL4)
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 5, wheelRadius = 0.19f,
            linkTracks = true, idlerRadius = 0.235f, sprocketRadius = 0.14f,
            trackPitch = 0.10f, trackShoeWidth = 0.24f, trackThickness = 0.035f, trackSag = 0.03f,
            returnRollerLeft = "rollL", returnRollerRight = "rollR", returnRollerCount = 1, returnRollerRadius = 0.065f
        };

        public static TankSpec T34_85 => new TankSpec
        {
            name = "T-34-85",
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
            ammoIndex = 4, ammo = new[] { 4, 7, 14 }, reloadS = 8f, barrelLength = 3.2f,
            sightType = SightType.Soviet, sightFov = 16f, sightMag = 4f, sightName = "TSh-16",
            modelResource = "Modelle/T34_85", modelScale = 1f,
            modelTurretPivotShift = new Vector3(0f, 0f, 0.25f),   // Turmring-Mitte liegt 25 cm vor dem Turmgelenk des Modells (sonst Loch beim Drehen)
            meshArmorFixWinding = true,                           // Turm des Modells hat nach innen zeigende Flaechen
            modelTurretBone = "turret", modelGunBone = "cannon",
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 7, wheelRadius = 0.4f,
            linkTracks = true, idlerRadius = 0.281f, sprocketRadius = 0.233f,   // hinten Antriebsrad, vorne Leitrad
            trackPitch = 0.172f, trackShoeWidth = 0.5f, trackThickness = 0.06f, trackSag = 0.03f
        };
    }
}
