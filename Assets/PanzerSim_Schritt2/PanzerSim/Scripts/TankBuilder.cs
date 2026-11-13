using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Baut einen Panzer aus einzelnen Panzerplatten (Kisten) und Innenteilen.
    /// Wurzel des Panzers liegt auf Bodenhoehe, z = vorne, x = rechts.
    /// </summary>
    public static class TankBuilder
    {
        public class Parts
        {
            public Transform turret, gunPivot, muzzle, sight;
            public Renderer barrel;
            public readonly List<ArmorPlate> plates = new List<ArmorPlate>();
            public readonly List<TankModule> modules = new List<TankModule>();
        }

        public static TankController Build(TankSpec s, Vector3 position, Quaternion rotation, bool isPlayer)
        {
            var root = new GameObject(s.name + (isPlayer ? " (du)" : ""));
            root.transform.SetPositionAndRotation(position, Quaternion.identity); // Drehung erst am Ende (Modell wird gerade vermessen)
            var parts = new Parts();

            float L = s.hullLength, W = s.hullWidth, H = s.hullHeight, y0 = s.clearance;

            // ---- Wanne ----
            var hull = new GameObject("Wanne").transform;
            hull.SetParent(root.transform, false);
            float minRoofW = s.turretWidth + 0.3f, minRoofL = s.turretLength + 0.4f;
            Box(hull, new Vector3(0f, y0 + H / 2f, 0f), L, W, H, "Wanne",
                s.hullFront, s.hullRear, s.hullSide, s.hullRoof, s.hullBottom, minRoofW, minRoofL, parts);

            // Ketten (schuetzen die untere Seite)
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (W / 2f + s.trackWidth / 2f - s.trackInset);
                var p = Plate(hull, side < 0 ? "Kette links" : "Kette rechts", new Armor(20, 0),
                    new Vector3(x, 0.55f, 0f), Quaternion.LookRotation(Vector3.right * side, Vector3.up),
                    L * 0.95f, 0.8f, parts);
                p.transform.localScale = new Vector3(L * 0.95f, 0.8f, s.trackWidth); // Kette ist breit, aber nur 20 mm "Wert"
            }

            // ---- Turm ----
            var turret = new GameObject("Turm").transform;
            turret.SetParent(hull, false);
            turret.localPosition = new Vector3(0f, y0 + H, s.turretOffsetZ);
            parts.turret = turret;
            float Lt = s.turretLength, Wt = s.turretWidth, Ht = s.turretHeight;
            Box(turret, new Vector3(0f, Ht / 2f, 0f), Lt, Wt, Ht, "Turm",
                s.turretFront, s.turretRear, s.turretSide, s.turretRoof, new Armor(0, 0), Wt * 0.5f, Lt * 0.5f, parts);

            // Kanone mit Blende
            var gun = new GameObject("Kanone").transform;
            gun.SetParent(turret, false);
            float frontTopZ = Lt / 2f - Ht / 2f * Mathf.Tan(s.turretFront.angle * Mathf.Deg2Rad);
            gun.localPosition = new Vector3(0f, Ht * 0.45f, frontTopZ);
            parts.gunPivot = gun;

            Plate(gun, "Turm Blende", s.mantlet, new Vector3(0f, 0f, 0.15f + s.mantlet.mm / 2000f),
                Quaternion.identity, Wt * 0.45f, Ht * 0.65f, parts);

            float cal = AmmoLibrary.All[s.ammoIndex].caliberMm / 1000f;
            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = "Rohr";
            Object.DestroyImmediate(barrel.GetComponent<Collider>());
            barrel.transform.SetParent(gun, false);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            barrel.transform.localScale = new Vector3(cal * 2.2f, s.barrelLength / 2f, cal * 2.2f);
            barrel.transform.localPosition = new Vector3(0f, 0f, s.barrelLength / 2f);
            parts.barrel = barrel.GetComponent<Renderer>();
            parts.barrel.sharedMaterial = GameMap.Mat(new Color(0.42f, 0.45f, 0.38f));

            var muzzle = new GameObject("Muendung").transform;
            muzzle.SetParent(gun, false);
            muzzle.localPosition = new Vector3(0f, 0f, s.barrelLength + 0.2f);
            parts.muzzle = muzzle;

            // Rohr als Trefferzone und Hindernis: fester Collider (stoesst an Waende/Panzer), getroffen wird das Teil "Rohr"
            // (beschaedigt = streut, zerstoert = Kanone schiesst nicht mehr). Beginnt vor der Blende, endet an der Muendung.
            {
                float start = Mathf.Min(0.6f, s.barrelLength * 0.3f), len = Mathf.Max(0.3f, s.barrelLength - start);
                float d = Mathf.Clamp(cal * 2.2f, 0.08f, 0.32f);
                var bgo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bgo.name = "Rohr (Trefferzone)";
                bgo.transform.SetParent(gun, false);
                bgo.transform.localPosition = new Vector3(0f, 0f, start + len / 2f);
                bgo.transform.localScale = new Vector3(d, d, len);
                var bm = bgo.AddComponent<TankModule>();
                bm.type = ModuleType.Barrel; bm.label = "Rohr"; bm.maxHp = 80f; bm.hp = 80f; bm.absorb = 0f;
                bm.UpdateColor();
                parts.modules.Add(bm);
            }

            var sight = new GameObject("Visier").transform;
            sight.SetParent(turret, false);
            sight.localPosition = new Vector3(-0.35f, Ht * 0.6f, frontTopZ + 0.3f);
            parts.sight = sight;

            // ---- Innenteile ----
            float tf = Mathf.Tan(s.hullFront.angle * Mathf.Deg2Rad);
            float ts = Mathf.Tan(s.hullSide.angle * Mathf.Deg2Rad);
            // Wie weit vorne / seitlich darf ein Teil in Hoehe h ueber dem Wannenboden sitzen?
            System.Func<float, float> frontZ = h => L / 2f - h * tf - 0.15f;
            System.Func<float, float> sideX = h => W / 2f - h * ts - 0.1f;

            float crewH = Mathf.Min(0.8f, H * 0.75f);
            float dz = Mathf.Min(L * 0.30f, frontZ(crewH) - 0.25f);
            float dx = Mathf.Min(W * 0.22f, sideX(crewH) - 0.25f);
            bool Crew(ModuleType t) => s.crew == null || System.Array.IndexOf(s.crew, t) >= 0;   // Besatzung laut Fahrzeug (Standard: 5 Mann)
            Module(hull, ModuleType.Driver, new Vector3(-dx, y0 + crewH / 2f + 0.05f, dz), new Vector3(0.45f, crewH, 0.45f), 100, 0.05f, root, parts);
            if (Crew(ModuleType.RadioOperator))
                Module(hull, ModuleType.RadioOperator, new Vector3(dx, y0 + crewH / 2f + 0.05f, dz), new Vector3(0.45f, crewH, 0.45f), 100, 0.05f, root, parts);
            Module(hull, ModuleType.Transmission, new Vector3(0f, y0 + 0.3f, Mathf.Min(L * 0.42f, frontZ(0.5f) - 0.3f)), new Vector3(W * 0.3f, 0.5f, 0.5f), 100, 0.3f, root, parts);
            Module(hull, ModuleType.Engine, new Vector3(0f, y0 + H * 0.4f, -L * 0.33f), new Vector3(W * 0.5f, H * 0.6f, L * 0.22f), 120, 0.5f, root, parts);
            Module(hull, ModuleType.Fuel, new Vector3(0f, y0 + 0.2f, -L * 0.13f), new Vector3(W * 0.6f, 0.3f, 0.6f), 60, 0.1f, root, parts);
            float ax = Mathf.Min(W * 0.33f, sideX(H * 0.6f) - 0.2f);
            Module(hull, ModuleType.Ammo, new Vector3(-ax, y0 + H * 0.35f, 0f), new Vector3(0.35f, H * 0.45f, 0.9f), 40, 0.1f, root, parts, "Munition links");
            Module(hull, ModuleType.Ammo, new Vector3(ax, y0 + H * 0.35f, 0f), new Vector3(0.35f, H * 0.45f, 0.9f), 40, 0.1f, root, parts, "Munition rechts");

            // Im Turm (Besatzung sitzt teils unter dem Turmring)
            if (s.crewInHull)
            {
                // kein bemannter Turm (z. B. Wiesel: Werfer auf einer Saeule) - Richtschuetze und Kommandant sitzen hinten in der Wanne
                Module(hull, ModuleType.Gunner, new Vector3(dx, y0 + crewH / 2f + 0.05f, s.turretOffsetZ), new Vector3(0.45f, crewH, 0.45f), 100, 0.05f, root, parts);
                Module(hull, ModuleType.Commander, new Vector3(-dx, y0 + crewH / 2f + 0.05f, s.turretOffsetZ - 0.45f), new Vector3(0.45f, crewH, 0.45f), 100, 0.05f, root, parts);
            }
            else
            {
                Module(turret, ModuleType.Gunner, new Vector3(-Wt * 0.25f, 0.05f, Lt * 0.1f), new Vector3(0.45f, 0.8f, 0.45f), 100, 0.05f, root, parts);
                if (Crew(ModuleType.Loader))
                    Module(turret, ModuleType.Loader, new Vector3(Wt * 0.25f, 0.05f, Lt * 0.05f), new Vector3(0.45f, 0.8f, 0.45f), 100, 0.05f, root, parts);
                Module(turret, ModuleType.Commander, new Vector3(0f, 0.15f, -Lt * 0.28f), new Vector3(0.45f, 0.8f, 0.45f), 100, 0.05f, root, parts);
            }
            Module(turret, ModuleType.Breech, new Vector3(0f, Ht * 0.45f, Lt * 0.15f), new Vector3(0.3f, 0.3f, 0.8f), 150, 0.3f, root, parts);
            Module(turret, ModuleType.TurretDrive, new Vector3(-Wt * 0.32f, 0.1f, Lt * 0.3f), new Vector3(0.25f, 0.25f, 0.25f), 60, 0.1f, root, parts);

            // ---- Physik ----
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = s.massT * 1000f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.centerOfMass = new Vector3(0f, y0 + 0.3f, 0f);

            var tc = root.AddComponent<TankController>();
            tc.Init(s, rb, parts, isPlayer);
            TankVisual.Attach(tc);
            tc.ApplyXray();
            root.transform.rotation = rotation;
            return tc;
        }

        /// <summary>Hohle Kiste aus 6 Platten mit schraeger Front, Heck und Seiten.</summary>
        static void Box(Transform parent, Vector3 c, float L, float W, float H, string name,
                        Armor front, Armor rear, Armor side, Armor roof, Armor bottom,
                        float minRoofW, float minRoofL, Parts parts)
        {
            float af = front.angle * Mathf.Deg2Rad, ar = rear.angle * Mathf.Deg2Rad, asd = side.angle * Mathf.Deg2Rad;
            float tf = Mathf.Tan(af), tr = Mathf.Tan(ar), ts = Mathf.Tan(asd);
            const float m = 0.04f; // etwas Ueberlappung, damit keine Spalten bleiben

            // Front
            Vector3 nF = new Vector3(0f, Mathf.Sin(af), Mathf.Cos(af));
            Vector3 uF = new Vector3(0f, Mathf.Cos(af), -Mathf.Sin(af));
            Plate(parent, name + " Front", front, c + new Vector3(0f, 0f, L / 2f - H / 2f * tf), Quaternion.LookRotation(nF, uF),
                  W + m, H / Mathf.Cos(af) + m, parts);
            // Heck
            Vector3 nR = new Vector3(0f, Mathf.Sin(ar), -Mathf.Cos(ar));
            Vector3 uR = new Vector3(0f, Mathf.Cos(ar), Mathf.Sin(ar));
            Plate(parent, name + " Heck", rear, c + new Vector3(0f, 0f, -L / 2f + H / 2f * tr), Quaternion.LookRotation(nR, uR),
                  W + m, H / Mathf.Cos(ar) + m, parts);
            // Seiten
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Vector3 nS = new Vector3(sgn * Mathf.Cos(asd), Mathf.Sin(asd), 0f);
                Vector3 uS = new Vector3(-sgn * Mathf.Sin(asd), Mathf.Cos(asd), 0f);
                Plate(parent, name + (sgn < 0 ? " Seite links" : " Seite rechts"), side,
                      c + new Vector3(sgn * (W / 2f - H / 2f * ts), 0f, 0f), Quaternion.LookRotation(nS, uS),
                      L + m, H / Mathf.Cos(asd) + m, parts);
            }
            // Dach
            float roofL = Mathf.Max(L - H * (tf + tr), minRoofL);
            float roofW = Mathf.Max(W - 2f * H * ts, minRoofW);
            float roofZ = (H * tr - H * tf) / 2f;
            Plate(parent, name + " Dach", roof, c + new Vector3(0f, H / 2f, roofZ), Quaternion.LookRotation(Vector3.up, Vector3.forward),
                  roofW + m, roofL + m, parts);
            // Boden
            if (bottom.mm > 0f)
                Plate(parent, name + " Boden", bottom, c + new Vector3(0f, -H / 2f, 0f), Quaternion.LookRotation(Vector3.down, Vector3.forward),
                      W + m, L + m, parts);
        }

        /// <summary>Eine Platte. Lokale z-Achse = Aussenseite (Normale), Dicke entlang z.</summary>
        static ArmorPlate Plate(Transform parent, string label, Armor a, Vector3 localPos, Quaternion localRot,
                                float width, float height, Parts parts)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = label;
            go.transform.SetParent(parent, false);
            float t = Mathf.Max(a.mm, 1f) / 1000f;
            Vector3 normal = localRot * Vector3.forward;
            go.transform.localPosition = localPos - normal * (t / 2f); // Aussenflaeche liegt auf der Kistenkante
            go.transform.localRotation = localRot;
            go.transform.localScale = new Vector3(width, height, t);
            go.GetComponent<Renderer>().sharedMaterial = GameMap.Mat(new Color(0.42f, 0.45f, 0.38f));
            var p = go.AddComponent<ArmorPlate>();
            p.thicknessMm = a.mm;
            p.material = a.mat;
            p.label = $"{label} ({a.mm:0} mm, {a.angle:0}°)";
            parts.plates.Add(p);
            return p;
        }

        static void Module(Transform parent, ModuleType type, Vector3 localPos, Vector3 size, float hp, float absorb,
                           GameObject root, Parts parts, string label = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Collider>().isTrigger = true;
            var m = go.AddComponent<TankModule>();
            m.type = type;
            m.label = label ?? TankModule.RoleName(type);
            go.name = m.label;
            m.maxHp = hp;
            m.hp = hp;
            m.absorb = absorb;
            m.owner = null; // wird in TankController.Init gesetzt
            m.UpdateColor();
            parts.modules.Add(m);
        }
    }
}
