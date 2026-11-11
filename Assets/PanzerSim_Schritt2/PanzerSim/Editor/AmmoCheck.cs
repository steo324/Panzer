using UnityEditor;
using UnityEngine;

namespace PanzerSim.EditorTools
{
    /// <summary>
    /// Menue "Panzer/Munitions-Test": feuert echte Granaten (Spiellogik, Schritt fuer Schritt simuliert) aus 200 m auf
    /// Zielpanzer und schreibt das Trefferprotokoll ins Log. Zum Pruefen von AP, HE und HEAT. Auch per -executeMethod.
    /// </summary>
    public static class AmmoCheck
    {
        [MenuItem("Panzer/Munitions-Test")]
        public static void Run()
        {
            ModelPrefabs.RunAll();
            var lib = AmmoLibrary.All;
            // (Granate, Ziel, Richtung zum Ziel in Grad: 0 = von vorne, 90 = von rechts, 180 = von hinten, Hoehe relativ zur Wanne 0..1, Beschreibung)
            var cases = new (int ammo, System.Func<TankSpec> target, float az, float h, string what)[]
            {
                (10, () => TankLibrary.T34_85,   90f, 0.75f, "Gr. 38 HL/B (75 mm HEAT) auf T-34 Seite oben (45 mm @ 40)"),
                (10, () => TankLibrary.T34_85,   90f, 0.25f, "Gr. 38 HL/B auf T-34 Seite unten (hinter Kette + Laufrollen)"),
                (10, () => TankLibrary.Tiger,     0f, 0.60f, "Gr. 38 HL/B auf Tiger Front (100 mm)"),
                (12, () => TankLibrary.Tiger,    90f, 0.75f, "Gr. 39 HL (88 mm HEAT) auf Tiger Seite (80 mm)"),
                ( 9, () => TankLibrary.PanzerIV, 180f, 0.6f, "Sprgr. 34 (HE) auf Panzer IV Heck (20 mm)"),
                ( 9, () => TankLibrary.Tiger,     0f, 0.60f, "Sprgr. 34 (HE) auf Tiger Front (100 mm)"),
                (14, () => TankLibrary.PanzerIV,  0f, 1.05f, "O-365 (85 mm HE) auf Panzer IV Turm/Dach-Bereich vorne"),
                ( 8, () => TankLibrary.T34_85,    0f, 0.60f, "PzGr. 39 L/43 (AP) auf T-34 Front zum Vergleich"),
                // ---- Ketten ----
                ( 8, () => TankLibrary.Sherman,  90f, -0.15f, "KETTE: PzGr. 39 (AP) quer durch beide Ketten des Sherman"),
                ( 9, () => TankLibrary.T34_85,   90f, -0.15f, "KETTE: Sprgr. 34 (HE) direkt auf die T-34-Kette"),
                (13, () => TankLibrary.PanzerIV, 90f, -0.33f, "KETTE: M48 HE Nahtreffer im Boden neben der Panzer-IV-Kette"),
            };
            foreach (var c in cases)
            {
                var spec = c.target();
                var tc = TankBuilder.Build(spec, Vector3.zero, Quaternion.identity, false);
                // Boden fuer Nahtreffer (h < 0: Hoehe ueber Grund statt in der Wanne; -0.33 = Boden 1 m neben der Kette)
                GameObject ground = null;
                if (c.h < 0f)
                {
                    ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    ground.transform.localScale = Vector3.one * 5f;
                }
                Physics.SyncTransforms();
                Vector3 aim = c.h >= 0f
                    ? tc.transform.TransformPoint(new Vector3(0f, spec.clearance + spec.hullHeight * c.h, 0f))
                    : c.h > -0.3f
                        ? tc.transform.TransformPoint(new Vector3(0f, -c.h * 3f, 0f))                    // Kettenhoehe (ca. 0,45 m)
                        : tc.transform.TransformPoint(new Vector3((spec.hullWidth / 2f + spec.trackWidth + 0.6f), 0.02f, 0f));   // Boden neben der Kette
                Vector3 dirToTarget = Quaternion.Euler(0f, c.az + 180f, 0f) * Vector3.forward;   // Schussrichtung
                Vector3 from = aim - dirToTarget * 200f + Vector3.up * 0.0f;
                // Fall auf 200 m ausgleichen
                Ballistics.SimulateFlat(lib[c.ammo], 200f, out _, out float drop, out _);
                Vector3 dir = (aim + Vector3.up * drop - from).normalized;
                if (c.h <= -0.3f)
                {
                    // Nahtreffer: steil von oben (fast waagerecht wuerde 1 cm Bahnfehler den Einschlag um Meter verschieben)
                    from = aim + Vector3.up * 30f - dirToTarget * 30f;
                    dir = (aim - from).normalized;
                }
                RangeLog.Lines.Clear();
                var shell = Shell.Fire(lib[c.ammo], from, dir);
                var step = typeof(Shell).GetMethod("FixedUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                for (int i = 0; i < 1500 && shell != null && shell.enabled; i++) step.Invoke(shell, null);
                string res = RangeLog.Lines.Count > 0 ? RangeLog.Lines[0] : "(kein Treffer protokolliert)";
                string tr = "";
                foreach (var b in tc.trackBreaks) tr += $"\n   Bruchstelle {(b.leftSide ? "links" : "rechts")} z={b.zy.x:0.00} y={b.zy.y:0.00}: Schaden {b.damage:0}, Luecke {b.gap:0.00} m";
                if (tc.trackBreaks.Count > 0) tr += $"\n   Reparaturzeit: {tc.RepairTimeNeeded():0} s";
                if (shell != null) tr += $"\n   Granate endete bei {tc.transform.InverseTransformPoint(shell.transform.position):F2} (im Panzer)";
                Debug.Log($"AMMO {c.what}:\n{res}{tr}");
                if (tc.trackBreaks.Count > 0)
                {
                    // Explosionsblitz/Feuer stehen im Editor still und ueberstrahlen das Bild: fuer die Aufnahme ausblenden
                    var fx = GameObject.Find("Effekte");
                    if (fx != null) fx.SetActive(false);
                    foreach (var mk in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)) if (mk.name == "Treffer") mk.enabled = false;
                    ModelCheck.Render(tc, new Vector3(c.what.Contains("Nahtreffer") ? -9f : 9f, -0.3f, 0f), 22f, $"kette_{tc.spec.name}.png");
                    if (fx != null) fx.SetActive(true);
                }
                if (ground != null) Object.DestroyImmediate(ground);
                if (shell != null) Object.DestroyImmediate(shell.gameObject);
                Object.DestroyImmediate(tc.gameObject);
                foreach (var f in Object.FindObjectsByType<ArmorMeshFollower>(FindObjectsSortMode.None)) Object.DestroyImmediate(f.gameObject);
            }
        }
    }
}
