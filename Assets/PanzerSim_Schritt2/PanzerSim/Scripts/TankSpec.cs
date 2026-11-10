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
        public Armor hullFrontLower = new Armor(-1, 0), hullGlacis = new Armor(-1, 0), hullLowerSide = new Armor(-1, 0);

        // Turm
        public float turretLength, turretWidth, turretHeight, turretOffsetZ;
        public Armor turretFront, turretSide, turretRear, turretRoof, mantlet;
        public float traverseDegS, elevationDegS, minElevation, maxElevation;

        // Kanone
        public int ammoIndex;       // Index in AmmoLibrary.All
        public float reloadS;
        public float barrelLength;
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

        // Laufrollen und Ketten im Modell (optional)
        public string wheelBoneLeft;         // z. B. "wheeltrackleft" -> wheeltrackleft0 ... wheeltrackleft9
        public string wheelBoneRight;
        public int wheelBoneCount;           // erste und letzte = Antriebsrad/Leitrad (drehen nur, federn nicht)
        public float wheelRadius = 0.33f;    // in Modell-Einheiten (vor Skalierung)
        public string trackMaterialLeft;     // Teil des Materialnamens
        public string trackMaterialRight;
        public float trackMetersPerUV = 1.15f; // Modell-Einheiten pro Texturlaenge
        public float trackScrollSign = 1f;   // falls die Kette falsch herum laeuft: -1

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
            // Masse an das 3D-Modell angepasst (Maßstab 1,2 = echte Tiger-Groesse)
            hullLength = 6.15f, hullWidth = 3.2f, hullHeight = 1.5f, clearance = 0.35f,
            trackWidth = 0.725f, trackInset = 0.49f,
            hullFront = new Armor(100, 9), hullRear = new Armor(80, 9), hullSide = new Armor(80, 0),
            hullRoof = new Armor(25, 0), hullBottom = new Armor(25, 0),
            turretLength = 2.75f, turretWidth = 2.1f, turretHeight = 0.86f, turretOffsetZ = 0f,
            turretFront = new Armor(100, 0), turretSide = new Armor(80, 0), turretRear = new Armor(80, 0),
            turretRoof = new Armor(25, 0), mantlet = new Armor(110, 0),
            traverseDegS = 7f, elevationDegS = 4f, minElevation = -8f, maxElevation = 15f,
            ammoIndex = 1, reloadS = 8.5f, barrelLength = 3.5f, muzzleBrake = true,
            sightType = SightType.German, sightFov = 25f, sightMag = 2.5f, sightName = "TZF 9c",
            sightZoomFov = new[] { 25f, 14f }, sightZoomMag = new[] { 2.5f, 5f },
            modelResource = "Modelle/Tiger", modelScale = 1.2f,
            meshArmor = true,
            hullFrontLower = new Armor(100, 24), hullGlacis = new Armor(60, 80), hullLowerSide = new Armor(60, 0),
            wheelBoneLeft = "wheeltrackleft", wheelBoneRight = "wheeltrackright", wheelBoneCount = 10, wheelRadius = 0.33f,
            linkTracks = true, idlerRadius = 0.256f, sprocketRadius = 0.33f,
            trackPitch = 0.13f, trackShoeWidth = 0.725f, trackThickness = 0.05f, trackSag = 0.06f
        };

        public static TankSpec PanzerIV => new TankSpec
        {
            name = "Panzer IV Ausf. H",
            massT = 25f, powerPS = 300f, maxSpeedKmh = 38f, reverseKmh = 8f, turnRateDegS = 35f,
            hullLength = 5.9f, hullWidth = 2.0f, hullHeight = 1.25f,
            hullFront = new Armor(80, 10), hullRear = new Armor(20, 10), hullSide = new Armor(30, 0),
            hullRoof = new Armor(12, 0), hullBottom = new Armor(10, 0),
            turretLength = 2.1f, turretWidth = 1.75f, turretHeight = 0.8f, turretOffsetZ = 0.1f,
            turretFront = new Armor(50, 10), turretSide = new Armor(30, 25), turretRear = new Armor(30, 15),
            turretRoof = new Armor(10, 0), mantlet = new Armor(50, 0),
            traverseDegS = 14f, elevationDegS = 5f, minElevation = -8f, maxElevation = 20f,
            ammoIndex = 0, reloadS = 6.5f, barrelLength = 3.6f, muzzleBrake = true,
            sightType = SightType.German, sightFov = 25f, sightName = "TZF 5f"
        };

        public static TankSpec Sherman => new TankSpec
        {
            name = "M4A2 Sherman",
            massT = 30.3f, powerPS = 410f, maxSpeedKmh = 48f, reverseKmh = 8f, turnRateDegS = 28f,
            hullLength = 5.9f, hullWidth = 2.1f, hullHeight = 1.45f,
            hullFront = new Armor(51, 56), hullRear = new Armor(38, 10), hullSide = new Armor(38, 0),
            hullRoof = new Armor(19, 0), hullBottom = new Armor(13, 0),
            turretLength = 2.1f, turretWidth = 1.9f, turretHeight = 0.9f, turretOffsetZ = 0f,
            turretFront = new Armor(76, 30, ArmorMaterial.Cast), turretSide = new Armor(51, 5, ArmorMaterial.Cast),
            turretRear = new Armor(51, 0, ArmorMaterial.Cast), turretRoof = new Armor(25, 0),
            mantlet = new Armor(89, 0, ArmorMaterial.Cast),
            traverseDegS = 24f, elevationDegS = 5f, minElevation = -10f, maxElevation = 25f,
            ammoIndex = 2, reloadS = 6f, barrelLength = 2.8f,
            sightType = SightType.US, sightFov = 12.7f, sightMag = 3f, sightName = "M70F"
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
            ammoIndex = 4, reloadS = 8f, barrelLength = 3.2f,
            sightType = SightType.Soviet, sightFov = 16f, sightMag = 4f, sightName = "TSh-16",
            modelResource = "Modelle/T34_85", modelScale = 1f,
            modelTurretBone = "turret", modelGunBone = "cannon",
            wheelBoneLeft = "radL", wheelBoneRight = "radR", wheelBoneCount = 7, wheelRadius = 0.4f,
            linkTracks = true, idlerRadius = 0.281f, sprocketRadius = 0.233f,   // hinten Antriebsrad, vorne Leitrad
            trackPitch = 0.172f, trackShoeWidth = 0.5f, trackThickness = 0.06f, trackSag = 0.03f
        };
    }
}
