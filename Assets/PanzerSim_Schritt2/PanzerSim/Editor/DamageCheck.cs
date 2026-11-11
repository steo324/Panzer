using UnityEditor;
using UnityEngine;

namespace PanzerSim.EditorTools
{
    /// <summary>
    /// Menue "Panzer/Schadens-Check (Bilder)": rendert nach Builds/ModelCheck/
    ///  - schaden_anbau_*: welche Teile als zerstoerbare Anbauteile erkannt wurden (rot)
    ///  - schaden_marken_*: Einschussloch, Delle, Abpraller-Spur, Russ (echte Schuesse)
    ///  - schaden_abrollen: T-34 faehrt mit gerissener Kette 15 m, Kette rollt ab
    /// </summary>
    public static class DamageCheck
    {
        [MenuItem("Panzer/Schadens-Check (Bilder)")]
        public static void Run()
        {
            ModelPrefabs.RunAll();
            // ---- 1. Anbauteile ----
            var red = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(1f, 0.15f, 0.1f) };
            foreach (var s in new[] { TankLibrary.PanzerIV, TankLibrary.Sherman, TankLibrary.T34_85, TankLibrary.Tiger })
            {
                var tc = TankBuilder.Build(s, Vector3.zero, Quaternion.identity, false);
                var parts = tc.GetComponentsInChildren<DestructiblePart>();
                foreach (var p in parts)
                {
                    var r = p.GetComponent<MeshRenderer>();
                    var mats = r.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = red; r.sharedMaterials = mats;
                }
                Debug.Log($"DAMAGE {s.name}: {parts.Length} Anbauteile");
                ModelCheck.Render(tc, new Vector3(-8f, 5f, 8f), 30f, $"schaden_anbau_{s.name}.png");
                ModelCheck.Render(tc, new Vector3(8f, 4f, -8f), 30f, $"schaden_anbau_{s.name}_hinten.png");
                Cleanup(tc);
            }

            // ---- 2. Treffermarken durch echte Schuesse ----
            var lib = AmmoLibrary.All;
            var shots = new (int ammo, System.Func<TankSpec> t, float az, float el, float h, string name)[]
            {
                (8, () => TankLibrary.T34_85, 30f, 0f, 0.7f, "loch_T34_seite"),       // PzGr. 39 schraeg auf T-34-Seite: Loch
                (4, () => TankLibrary.Tiger, 25f, 0f, 0.6f, "delle_Tiger_front"),     // BR-365 schraeg auf Tiger-Front (100 mm): kein Durchschlag
                (6, () => TankLibrary.Tiger, 0f, 0f, -1f, "abpraller_Tiger_seite"),  // M72 AP fast parallel an der Tiger-Seite entlang (ca. 78 Grad): Abpraller
                (11, () => TankLibrary.Sherman, 90f, 0f, 0.85f, "russ_Sherman_seite"), // 8,8 HE auf Sherman-Seite
            };
            foreach (var c in shots)
            {
                var spec = c.t();
                var tc = TankBuilder.Build(spec, Vector3.zero, Quaternion.identity, false);
                Physics.SyncTransforms();
                Vector3 aim = tc.transform.TransformPoint(new Vector3(0f, spec.clearance + spec.hullHeight * Mathf.Abs(c.h), 0f));
                Vector3 shotDir = Quaternion.Euler(c.el, c.az + 180f, 0f) * Vector3.forward;
                if (c.h < 0f)
                {
                    // streifender Schuss: Punkt auf der linken Seitenwand suchen, Schuss mit 12 Grad dagegen
                    Vector3 o = tc.transform.TransformPoint(new Vector3(-6f, spec.clearance + spec.hullHeight * 0.75f, 0.5f));
                    if (Physics.Raycast(o, tc.transform.right, out RaycastHit sh, 6f, ArmorPlate.ShotMask, QueryTriggerInteraction.Ignore)) aim = sh.point;
                    shotDir = (tc.transform.right * Mathf.Sin(12f * Mathf.Deg2Rad) - tc.transform.forward * Mathf.Cos(12f * Mathf.Deg2Rad)).normalized;
                }
                for (int k = 0; k < 3; k++)   // drei Schuesse nebeneinander
                {
                    Vector3 a = aim + tc.transform.right * ((k - 1) * 0.45f) * (Mathf.Abs(Mathf.Sin(c.az * Mathf.Deg2Rad)) < 0.5f ? 1f : 0f)
                                    + tc.transform.forward * ((k - 1) * 0.6f) * (Mathf.Abs(Mathf.Sin(c.az * Mathf.Deg2Rad)) >= 0.5f ? 1f : 0f);
                    Fire(lib[c.ammo], a - shotDir * 60f, shotDir);
                }
                HideFx();
                // Kamera seitlich versetzt, damit sie nicht in der Rohrachse sitzt
                Vector3 camPos = aim - shotDir * 4.5f + Vector3.up * 1.2f + Vector3.Cross(Vector3.up, shotDir).normalized * 1.5f;
                ModelCheck.RenderAt(camPos, aim, 35f, $"schaden_marken_{c.name}.png");
                ShowFx();
                Cleanup(tc);
            }

            // ---- 3. Kette rollt ab ----
            {
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.transform.localScale = Vector3.one * 10f;
                var tc = TankBuilder.Build(TankLibrary.T34_85, new Vector3(0f, 0.05f, 0f), Quaternion.identity, false);
                Physics.SyncTransforms();
                tc.DamageTrack(tc.transform.TransformPoint(new Vector3(1.3f, 0.9f, 2.6f)), 60f, Vector3.zero, false);   // vorne oben gerissen
                var lu = typeof(TankVisual).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                // 15 m vorwaerts in kleinen Schritten (im Editor ist die Bildzeit winzig: Geschwindigkeit entsprechend klein)
                TankTracks.SuppressDraw = true;
                for (int i = 0; i < 30000; i++)
                {
                    float step = 15f / 30000f;
#if UNITY_6000_0_OR_NEWER
                    tc.rb.linearVelocity = tc.transform.forward * (step / 0.0001f);
#else
                    tc.rb.velocity = tc.transform.forward * (step / 0.0001f);
#endif
                    tc.transform.position += tc.transform.forward * step;
                    Physics.SyncTransforms();
                    lu.Invoke(tc.visual, null);
                }
                TankTracks.SuppressDraw = false;
                HideFx();
                ModelCheck.Render(tc, new Vector3(12f, 16f, -20f), 40f, "schaden_abrollen.png");
                ShowFx();
                Debug.Log($"DAMAGE Abrollen: Luecke jetzt {tc.trackBreaks[0].gap:0.0} m");
                var trk = typeof(TankVisual).GetField("tracks", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(tc.visual);
                if (trk != null)
                    foreach (var fn in new[] { "laidL", "laidR" })
                    {
                        var list = typeof(TankTracks).GetField(fn, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(trk) as System.Collections.Generic.List<Matrix4x4>;
                        if (list == null) continue;
                        string first = list.Count > 0 ? list[0].GetColumn(3).ToString("F2") + " .. " + list[list.Count - 1].GetColumn(3).ToString("F2") : "-";
                        Debug.Log($"DAMAGE   {fn}: {list.Count} Glieder, {first}; Panzer bei {tc.transform.position:F2}");
                    }
                Cleanup(tc);
                Object.DestroyImmediate(ground);
            }
            Debug.Log("DAMAGE fertig");
        }

        static void Fire(ShellData d, Vector3 from, Vector3 dir)
        {
            var sh = Shell.Fire(d, from, dir);
            var step = typeof(Shell).GetMethod("FixedUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            for (int i = 0; i < 400 && sh != null && sh.enabled; i++) step.Invoke(sh, null);
            if (sh != null) Object.DestroyImmediate(sh.gameObject);
        }

        static GameObject hiddenFx;
        static void HideFx()
        {
            hiddenFx = GameObject.Find("Effekte");          // Blitz/Feuer stehen im Editor still: fuer die Aufnahme ausblenden
            if (hiddenFx != null) hiddenFx.SetActive(false);
        }
        static void ShowFx() { if (hiddenFx != null) hiddenFx.SetActive(true); hiddenFx = null; }

        static void Cleanup(TankController tc)
        {
            Object.DestroyImmediate(tc.gameObject);
            foreach (var f in Object.FindObjectsByType<ArmorMeshFollower>(FindObjectsSortMode.None)) Object.DestroyImmediate(f.gameObject);
            foreach (var d in Object.FindObjectsByType<DestructiblePart>(FindObjectsSortMode.None)) Object.DestroyImmediate(d.gameObject);
            foreach (var g in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                if (g != null && (g.name == "Treffermarke" || g.name == "Kettenglied" || g.name == "Treffer")) Object.DestroyImmediate(g.gameObject);
        }
    }
}
