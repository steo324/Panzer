using System.Text;
using UnityEditor;
using UnityEngine;

namespace PanzerSim.EditorTools
{
    /// <summary>
    /// Menue "Panzer/Panzerungs-Check": beschiesst jeden Panzer aus allen Richtungen mit Teststrahlen auf Punkte in der Wanne
    /// und zaehlt, wie viele KEINE Panzerung treffen (= Granate fliegt einfach hindurch). Auch per -executeMethod.
    /// Zweite Zahl: dasselbe, wenn auch Rueckseiten von Dreiecken zaehlen (falsch herum gedrehte Flaechen im Modell).
    /// </summary>
    public static class ArmorCheck
    {
        [MenuItem("Panzer/Panzerungs-Check")]
        public static void Run()
        {
            ModelPrefabs.RunAll();
            var specs = new[] { TankLibrary.Tiger, TankLibrary.PanzerIV, TankLibrary.Sherman, TankLibrary.T34_85 };
            foreach (var s in specs)
            {
                var tc = TankBuilder.Build(s, Vector3.zero, Quaternion.identity, false);
                Physics.SyncTransforms();
                var root = tc.transform;
                var rng = new System.Random(7);
                int total = 0, miss = 0, missBack = 0;
                var sectors = new int[8]; var sectorTotal = new int[8];
                var log = new StringBuilder();
                for (int az = 0; az < 360; az += 5)
                    for (int el = -10; el <= 40; el += 10)
                        for (int k = 0; k < 5; k++)
                        {
                            // Zielpunkt irgendwo in Wanne oder Turm
                            bool turret = k == 4;
                            Vector3 target = turret
                                ? tc.parts.turret.position + new Vector3(R(rng, 0.6f), 0.3f + R(rng, 0.2f), R(rng, 0.6f))
                                : root.TransformPoint(new Vector3(R(rng, s.hullWidth * 0.35f), s.clearance + s.hullHeight * (0.3f + 0.4f * (float)rng.NextDouble()), R(rng, s.hullLength * 0.4f)));
                            Vector3 dir = Quaternion.Euler(-el, az, 0f) * Vector3.forward;
                            Vector3 from = target - dir * 25f;
                            total++;
                            int sector = ((az + 22) % 360) / 45;   // 0 = Shot von vorne kommend? (az 0 = Schuss in +z = von hinten)
                            sectorTotal[sector]++;
                            if (!HitsArmor(from, dir)) { miss++; sectors[sector]++; }
                            Physics.queriesHitBackfaces = true;
                            if (!HitsArmor(from, dir)) missBack++;
                            Physics.queriesHitBackfaces = false;
                        }
                // Welche Zonen werden zuerst getroffen (Name, Dicke)? Nur Treffer von vorne/seitlich-vorne und von der Seite
                var zoneHits = new System.Collections.Generic.Dictionary<string, int>();
                for (int az = 0; az < 360; az += 10)
                    for (int el = -5; el <= 30; el += 5)
                        for (int k = 0; k < 6; k++)
                        {
                            bool turret = k >= 4;
                            Vector3 target = turret
                                ? tc.parts.turret.position + new Vector3(R(rng, 0.6f), 0.3f + R(rng, 0.2f), R(rng, 0.6f))
                                : root.TransformPoint(new Vector3(R(rng, s.hullWidth * 0.35f), s.clearance + s.hullHeight * (0.3f + 0.4f * (float)rng.NextDouble()), R(rng, s.hullLength * 0.4f)));
                            Vector3 dir = Quaternion.Euler(-el, az, 0f) * Vector3.forward;
                            if (!Physics.Raycast(target - dir * 25f, dir, out RaycastHit h, 40f, ArmorPlate.ShotMask, QueryTriggerInteraction.Ignore)) continue;
                            var plate = h.collider.GetComponentInParent<ArmorPlate>();
                            if (plate == null) continue;
                            plate.GetArmor(h, out float mm, out ArmorMaterial mat, out string lab);
                            string side = az >= 135 && az <= 225 ? "vorne" : (az <= 45 || az >= 315 ? "hinten" : "seite");
                            string key = $"{side}:{(turret ? "T" : "W")}:{lab} {mm:0}mm";
                            zoneHits[key] = zoneHits.TryGetValue(key, out int c) ? c + 1 : 1;
                        }
                var zl = new System.Collections.Generic.List<string>();
                foreach (var kv in zoneHits) zl.Add($"{kv.Value,4} {kv.Key}");
                zl.Sort((a, b) => string.CompareOrdinal(b, a));
                Debug.Log($"ZONES {s.name}:\n" + string.Join("\n", zl));

                string[] names = { "von hinten", "hinten-links", "links", "vorne-links", "von vorne", "vorne-rechts", "rechts", "hinten-rechts" };
                for (int i = 0; i < 8; i++) if (sectors[i] > 0) log.Append($" {names[i]} {100f * sectors[i] / sectorTotal[i]:0}%");
                Debug.Log($"ARMOR {s.name}: {miss}/{total} Strahlen ohne Panzerung ({100f * miss / total:0.0}%), mit Rueckseiten {missBack} ({100f * missBack / total:0.0}%) |{log}");
                Object.DestroyImmediate(tc.gameObject);
                foreach (var f in Object.FindObjectsByType<ArmorMeshFollower>(FindObjectsSortMode.None)) Object.DestroyImmediate(f.gameObject);
            }
        }

        static float R(System.Random r, float a) => ((float)r.NextDouble() * 2f - 1f) * a;

        static bool HitsArmor(Vector3 from, Vector3 dir)
        {
            // Anbauteile ueberspringen: die Granate reisst sie ab und fliegt weiter
            var hits = Physics.RaycastAll(from, dir, 40f, ArmorPlate.ShotMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider.GetComponentInParent<DestructiblePart>() != null) continue;
                return h.collider.GetComponentInParent<ArmorPlate>() != null;
            }
            return false;
        }
    }
}
