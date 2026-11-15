using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PanzerSim
{
    /// <summary>
    /// Trefferkamera: trifft eine eigene Granate einen Panzer, zeigt ein Fenster rechts ~4 s lang die Einschlagstelle aus
    /// Schussrichtung, mit Roentgenblick NUR in diesem Fenster: Panzer glasig, Innenteile nach Zustand gefaerbt
    /// (gruen heil, rot beschaedigt, schwarz zerstoert), Schussbahn als rote Linie. Darueber Ergebnis und Schaden.
    /// Einstellbar (GameSettings.HitCam).
    /// </summary>
    public static class HitCam
    {
        const float Duration = 4f;
        public static bool Active => cam != null && cam.enabled;
        public static TankController Target => target;
        static Camera cam;
        static Runner runner;
        static TankController target;
        static Vector3 impactLocal, dirLocal;
        static float t;
        static string title = "", details = "";
        static Color titleColor = Color.white;
        static LineRenderer line;

        /// <summary>Eigene Granate trifft Panzerung: Fenster oeffnen (neuer Treffer ersetzt den alten).</summary>
        public static void Begin(TankController tank, Vector3 point, Vector3 dir, HitResult r, string plateName)
        {
            if (!GameSettings.HitCam || NetSync.Headless || tank == null) return;
            Ensure();
            target = tank;
            impactLocal = tank.transform.InverseTransformPoint(point);
            dirLocal = tank.transform.InverseTransformDirection(dir.normalized);
            t = 0f;
            switch (r.outcome)
            {
                case HitOutcome.Penetration: title = $"DURCHSCHLAG  -  {plateName}"; titleColor = new Color(0.45f, 1f, 0.45f); break;
                case HitOutcome.Ricochet: title = $"ABPRALLER  -  {plateName}"; titleColor = new Color(1f, 0.9f, 0.35f); break;
                case HitOutcome.Shatter: title = $"GRANATE ZERBROCHEN  -  {plateName}"; titleColor = new Color(1f, 0.55f, 1f); break;
                default: title = $"KEIN DURCHSCHLAG  -  {plateName}"; titleColor = new Color(1f, 0.45f, 0.4f); break;
            }
            details = $"{tank.spec.name}:  {r.penetrationMm:0} mm Durchschlag gegen {r.effectiveArmorMm:0} mm (Winkel {r.impactAngleDeg:0}°)";
            // Schussbahn: 1,5 m vor dem Einschlag bis 4 m dahinter (in Panzer-Koordinaten gemerkt, die Linie folgt jedes Bild).
            // Linien haengen NICHT am Panzer: wird er geloescht (neue Ziele, Wrack aufgeraeumt), gingen sie sonst mit verloren
            // und jeder spaetere Treffer wuerde abbrechen (Trefferkamera kam nicht mehr).
            shotA = impactLocal - dirLocal * 1.5f;
            shotB = impactLocal + dirLocal * (r.outcome == HitOutcome.Penetration ? 4f : 0.05f);
            spallLocal.Clear();   // Splitter des vorigen Treffers weg
            cam.enabled = true;
        }
        static Vector3 shotA, shotB;
        static readonly List<(Vector3 a, Vector3 b)> spallLocal = new List<(Vector3 a, Vector3 b)>();

        // ---------- Splitter (Spall): Bahnen als orange Linien, nur in der Trefferkamera ----------
        static readonly List<LineRenderer> spallLines = new List<LineRenderer>();
        static Material spallMat;

        /// <summary>Splitterbahnen der Granate (Welt-Koordinaten) anzeigen - fahren mit dem Panzer mit.</summary>
        public static void SetSpall(TankController tank, List<(Vector3 a, Vector3 b)> rays)
        {
            if (tank != target || cam == null || !cam.enabled) return;
            if (spallMat == null)
            {
                spallMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
                spallMat.color = new Color(1f, 0.62f, 0.15f);
            }
            int n = Mathf.Min(rays.Count, 80);
            spallLines.RemoveAll(l => l == null);
            while (spallLines.Count < n)
            {
                var go = new GameObject("Splitterbahn");
                go.transform.SetParent(cam.transform, false);   // gehoert der Trefferkamera, nicht dem Panzer
                var l = go.AddComponent<LineRenderer>();
                l.useWorldSpace = true; l.positionCount = 2; l.startWidth = 0.025f; l.endWidth = 0.008f;
                l.sharedMaterial = spallMat; l.shadowCastingMode = ShadowCastingMode.Off; l.enabled = false;
                spallLines.Add(l);
            }
            spallLocal.Clear();
            for (int i = 0; i < n; i++)
                spallLocal.Add((tank.transform.InverseTransformPoint(rays[i].a), tank.transform.InverseTransformPoint(rays[i].b)));
            if (n > 0 && !details.Contains("Splitter")) details += $"\nSplitter: {n} Bahnen im Kampfraum";
        }

        /// <summary>Granate fertig: Schaden nachtragen.</summary>
        public static void Details(TankController tank, IEnumerable<string> damage)
        {
            if (tank != target || cam == null || !cam.enabled) return;
            string d = string.Join(", ", damage);
            if (d.Length > 0) details += "\nSchaden: " + d;
            if (tank.destroyed) { details += "\nPANZER AUSGESCHALTET"; t = Mathf.Min(t, 0.5f); }
        }

        static void Ensure()
        {
            if (cam != null) return;
            var go = new GameObject("Trefferkamera");
            Object.DontDestroyOnLoad(go);
            cam = go.AddComponent<Camera>();
            cam.depth = 50;
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 3000f;
            cam.cullingMask = ~((1 << ArmorPlate.PhysicsOnlyLayer) | (1 << ArmorPlate.MeshArmorLayer));
            cam.enabled = false;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) { data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.FastApproximateAntialiasing; data.renderShadows = true; }   // ohne Bloom: Explosionen ueberstrahlen sonst den Roentgenblick
            var lgo = new GameObject("Schussbahn");
            lgo.transform.SetParent(go.transform, false);
            line = lgo.AddComponent<LineRenderer>();
            line.useWorldSpace = true; line.positionCount = 2;
            line.startWidth = 0.06f; line.endWidth = 0.03f;
            line.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            line.material.color = new Color(1f, 0.15f, 0.1f);
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.enabled = false;
            runner = go.AddComponent<Runner>();
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
        }

        // ---------- Roentgenblick nur fuer die Trefferkamera ----------
        static Material hitGlass;
        /// <summary>Sehr durchsichtiges, blaugraues Glas: die farbigen Innenteile sollen gut sichtbar sein.</summary>
        static Material HitGlass()
        {
            if (hitGlass != null) return hitGlass;
            var g = TankController.Glass();
            if (g == null) return null;
            hitGlass = new Material(g) { name = "Trefferkamera-Glas" };
            hitGlass.SetColor("_BaseColor", new Color(0.45f, 0.6f, 0.75f, 0.13f));
            hitGlass.SetFloat("_Smoothness", 0.2f);
            return hitGlass;
        }
        static readonly List<(Renderer r, Material[] mats)> swapped = new List<(Renderer, Material[])>();
        static readonly List<(Renderer r, bool on)> modules = new List<(Renderer, bool)>();

        static void OnBegin(ScriptableRenderContext ctx, Camera c)
        {
            if (c != cam || target == null) return;
            try { XrayOn(); }
            catch (System.Exception e) { Debug.LogWarning("Trefferkamera: " + e.Message); }   // nie die Kamera blockieren
        }

        static void XrayOn()
        {
            var glass = HitGlass();
            if (target.visual != null && glass != null)
                foreach (var r in target.visual.Renderers)
                {
                    if (r == null || !r.enabled) continue;
                    swapped.Add((r, r.sharedMaterials));
                    var g = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < g.Length; i++) g[i] = glass;
                    r.sharedMaterials = g;
                }
            foreach (var m in target.parts.modules)
            {
                var r = m != null ? m.GetComponent<Renderer>() : null;
                if (r == null) continue;
                modules.Add((r, r.enabled));
                r.enabled = true;
            }
            if (line != null) line.enabled = true;
            for (int i = 0; i < spallLocal.Count && i < spallLines.Count; i++) if (spallLines[i] != null) spallLines[i].enabled = true;
        }

        /// <summary>Linien jedes Bild an den (fahrenden) Panzer anlegen - aus den gemerkten Panzer-Koordinaten.</summary>
        static void UpdateLines(Transform tr)
        {
            if (line != null) { line.SetPosition(0, tr.TransformPoint(shotA)); line.SetPosition(1, tr.TransformPoint(shotB)); }
            for (int i = 0; i < spallLocal.Count && i < spallLines.Count; i++)
            {
                var l = spallLines[i];
                if (l == null) continue;
                l.SetPosition(0, tr.TransformPoint(spallLocal[i].a));
                l.SetPosition(1, tr.TransformPoint(spallLocal[i].b));
            }
        }

        static void OnEnd(ScriptableRenderContext ctx, Camera c)
        {
            if (c != cam) return;
            foreach (var (r, mats) in swapped) if (r != null) r.sharedMaterials = mats;
            foreach (var (r, on) in modules) if (r != null) r.enabled = on;
            swapped.Clear(); modules.Clear();
            if (line != null) line.enabled = false;
            foreach (var l in spallLines) if (l != null) l.enabled = false;
        }

        class Runner : MonoBehaviour
        {
            void LateUpdate()
            {
                if (cam == null || !cam.enabled) return;
                t += Time.unscaledDeltaTime;
                if (target == null || t > Duration || !GameSettings.HitCam) { cam.enabled = false; return; }
                // Fenster rechts ueber der Minikarte
                cam.rect = new Rect(0.62f, 0.27f, 0.36f, 0.34f);
                Transform tr = target.transform;
                Vector3 p = tr.TransformPoint(impactLocal), d = tr.TransformDirection(dirLocal);
                // aus Schussrichtung, leicht erhoeht und seitlich, kreist langsam um die Einschlagstelle
                Vector3 side = Vector3.Cross(Vector3.up, d).normalized;
                Quaternion orbit = Quaternion.AngleAxis(-12f + t * 7f, Vector3.up);
                Vector3 from = p + orbit * (-d * 4.2f + side * 1.4f) + Vector3.up * 1.3f;
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(p - from, Vector3.up));
                UpdateLines(tr);
            }

            void OnGUI()
            {
                if (cam == null || !cam.enabled) return;
                Rect r = cam.pixelRect;
                var gr = new Rect(r.x, Screen.height - r.y - r.height, r.width, r.height);
                // Rahmen
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.DrawTexture(new Rect(gr.x - 3, gr.y - 3, gr.width + 6, 3), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(gr.x - 3, gr.yMax, gr.width + 6, 3), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(gr.x - 3, gr.y, 3, gr.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(gr.xMax, gr.y, 3, gr.height), Texture2D.whiteTexture);
                float f = GameSettings.UiFactor;
                var head = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(19 * f), fontStyle = FontStyle.Bold, wordWrap = true };
                var body = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * f), wordWrap = true };
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(gr.x, gr.y, gr.width, 30f * f), Texture2D.whiteTexture);
                GUI.color = titleColor;
                GUI.Label(new Rect(gr.x + 8, gr.y + 3, gr.width - 16, 28f * f), "TREFFERKAMERA  ·  " + title, head);
                float bh = body.CalcHeight(new GUIContent(details), gr.width - 16) + 8f;
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(gr.x, gr.yMax - bh, gr.width, bh), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(gr.x + 8, gr.yMax - bh + 4, gr.width - 16, bh), details, body);
            }
        }
    }
}
