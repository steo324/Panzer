using System.IO;
using UnityEditor;
using UnityEngine;

namespace PanzerSim.EditorTools
{
    /// <summary>
    /// Menue "Panzer/Modell-Check": baut jeden Panzer mit 3D-Modell einmal auf, schreibt Gelenke/Raeder/Muendung ins Log
    /// und rendert Bilder (Seite, schraeg vorne, Roentgen) nach Builds/ModelCheck/. Auch per -executeMethod aufrufbar.
    /// </summary>
    public static class ModelCheck
    {
        [MenuItem("Panzer/Modell-Check (Bilder rendern)")]
        public static void Run()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "ModelCheck"));
            Directory.CreateDirectory(outDir);
            ModelPrefabs.RunAll();
            var specs = new[] { TankLibrary.Sherman, TankLibrary.PanzerIV };
            foreach (var s in specs)
            {
                var tc = TankBuilder.Build(s, Vector3.zero, Quaternion.identity, false);
                var root = tc.transform;
                var model = root.Find("Modell");
                Debug.Log($"CHECK {s.name}: Modell={(model != null)} Visual={(tc.visual != null)}");
                if (model != null)
                {
                    foreach (var n in new[] { s.modelTurretBone, s.modelGunBone, s.wheelBoneLeft + "0", s.wheelBoneLeft + (s.wheelBoneCount - 1), s.wheelBoneRight + "3" })
                    {
                        var t = FindDeep(model, n);
                        Debug.Log(t != null ? $"CHECK   {n}: {root.InverseTransformPoint(t.position):F2}" : $"CHECK   {n}: FEHLT");
                    }
                    var b = new Bounds(); bool any = false;
                    foreach (var r in model.GetComponentsInChildren<Renderer>())
                    { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
                    Debug.Log($"CHECK   Modell-Grenzen: min {b.min:F2} max {b.max:F2}");
                }
                Debug.Log($"CHECK   Muendung: {root.InverseTransformPoint(tc.parts.muzzle.position):F2}  Turm: {root.InverseTransformPoint(tc.parts.turret.position):F2}  Kanone: {root.InverseTransformPoint(tc.parts.gunPivot.position):F2}");

                // Sonne + Kamera
                var sun = new GameObject("Sonne").AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.3f;
                sun.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
                RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
                var cam = new GameObject("Kamera").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
                cam.fieldOfView = 30f;
                Shot(cam, root, new Vector3(-14f, 2f, 0f), Path.Combine(outDir, $"{s.name}_seite.png"));
                Shot(cam, root, new Vector3(-9f, 4f, 10f), Path.Combine(outDir, $"{s.name}_vorne.png"));
                TankController.ViewMode = 1; tc.ApplyXray();
                Shot(cam, root, new Vector3(-9f, 5f, -9f), Path.Combine(outDir, $"{s.name}_roentgen.png"));
                TankController.ViewMode = 0;
                Object.DestroyImmediate(cam.gameObject); Object.DestroyImmediate(sun.gameObject);
                Object.DestroyImmediate(tc.gameObject);
            }
            Debug.Log("CHECK fertig: " + outDir);
        }

        static void Shot(Camera cam, Transform target, Vector3 offset, string path)
        {
            Vector3 c = target.position + Vector3.up * 1.4f;
            cam.transform.position = c + offset;
            cam.transform.LookAt(c);
            var rt = new RenderTexture(1280, 720, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }

        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var f = FindDeep(c, n); if (f != null) return f; }
            return null;
        }
    }
}
