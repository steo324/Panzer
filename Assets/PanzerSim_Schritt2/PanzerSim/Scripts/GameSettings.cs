using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PanzerSim
{
    /// <summary>
    /// Alle Einstellungen (Grafik + Steuerung): werden in PlayerPrefs gespeichert und sofort angewendet.
    /// Beim allerersten Start wird je nach Grafikkarte eine passende Stufe gewaehlt.
    /// </summary>
    public static class GameSettings
    {
        // ---- Anzeige ----
        public static int ResW, ResH;                 // 0 = Bildschirmaufloesung
        public static int WindowMode;                 // 0 Vollbild (Fenster), 1 Vollbild exklusiv, 2 Fenster
        public static bool VSync;
        public static int FpsLimit;                   // 0 = unbegrenzt

        // ---- Grafik ----
        public static int Preset;                     // 0 Niedrig, 1 Mittel, 2 Hoch, 3 Ultra, 4 Benutzerdefiniert
        public static float RenderScale = 1f;         // 0.5 .. 2.0 (ueber 1 = Supersampling, schaerfer)
        public static bool Fsr = true;                // Hochskalierung mit FSR statt einfach unscharf
        public static int AntiAliasing = 2;           // 0 Aus, 1 FXAA, 2 SMAA, 3 MSAA 4x + SMAA
        public static int Shadows = 2;                // 0 Aus, 1 Niedrig, 2 Mittel, 3 Hoch
        public static float WheatDensity = 1f;        // 0.25 .. 1
        public static float WheatDistance = 105f;     // 3D-Halme bis (m)
        public static float ViewDistance = 750f;      // Weizen-Fernbilder bis (m)
        public static float WheatShadows = 15f;       // Halme werfen Schatten bis (m), 0 = aus

        // ---- Spiel ----
        public static float Fov = 60f;
        public static float MouseSens = 1f;
        public static bool InvertY;
        public static bool DetailTrees = true;        // neue Baeume (sonst einfache Kugelbaeume)
        public static float UiScale = 1f;             // 0.75 .. 1.5, zusaetzlich zur automatischen Anpassung an die Aufloesung

        /// <summary>Faktor fuer Anzeigen im Spiel: waechst mit der Bildschirmhoehe (1080p = 1) mal UI-Groesse.</summary>
        public static float UiFactor => Mathf.Max(0.5f, Screen.height / 1080f) * UiScale;

        public static readonly string[] PresetNames = { "Niedrig", "Mittel", "Hoch", "Ultra", "Benutzerdefiniert" };
        public static readonly string[] AaNames = { "Aus", "FXAA (schnell)", "SMAA", "MSAA 4x + SMAA" };
        public static readonly string[] ShadowNames = { "Aus", "Niedrig", "Mittel", "Hoch" };
        public static readonly string[] WindowNames = { "Vollbild (randloses Fenster)", "Vollbild (exklusiv)", "Fenster" };
        public static readonly int[] FpsSteps = { 0, 30, 60, 75, 120, 144, 165, 240 };

        static bool loaded;

        public static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            if (PlayerPrefs.GetInt("s.version") < 2)   // neu oder alte Version: einmal passende Stufe waehlen
            {
                ApplyPreset(IsWeakGpu() ? 1 : 2);
                if (IsWeakGpu() && SystemInfo.graphicsMemorySize < 1500) ApplyPreset(0);
                VSync = true;
                Save();
            }
            else
            {
                ResW = PlayerPrefs.GetInt("s.resW"); ResH = PlayerPrefs.GetInt("s.resH");
                WindowMode = PlayerPrefs.GetInt("s.window"); VSync = PlayerPrefs.GetInt("s.vsync") == 1;
                FpsLimit = PlayerPrefs.GetInt("s.fps"); Preset = PlayerPrefs.GetInt("s.preset");
                RenderScale = PlayerPrefs.GetFloat("s.scale", 1f); Fsr = PlayerPrefs.GetInt("s.fsr", 1) == 1;
                AntiAliasing = PlayerPrefs.GetInt("s.aa", 2); Shadows = PlayerPrefs.GetInt("s.shadows", 2);
                WheatDensity = PlayerPrefs.GetFloat("s.wDens", 1f); WheatDistance = PlayerPrefs.GetFloat("s.wDist", 105f);
                ViewDistance = PlayerPrefs.GetFloat("s.view", 750f); WheatShadows = PlayerPrefs.GetFloat("s.wShadow", 15f);
                Fov = PlayerPrefs.GetFloat("s.fov", 60f); MouseSens = PlayerPrefs.GetFloat("s.sens", 1f);
                InvertY = PlayerPrefs.GetInt("s.invY") == 1;
                UiScale = PlayerPrefs.GetFloat("s.ui", 1f);
                DetailTrees = PlayerPrefs.GetInt("s.trees", 1) == 1;
            }
            Apply(true);
        }

        public static void Save()
        {
            PlayerPrefs.SetInt("s.version", 2);
            PlayerPrefs.SetInt("s.trees", DetailTrees ? 1 : 0);
            PlayerPrefs.SetInt("s.resW", ResW); PlayerPrefs.SetInt("s.resH", ResH);
            PlayerPrefs.SetInt("s.window", WindowMode); PlayerPrefs.SetInt("s.vsync", VSync ? 1 : 0);
            PlayerPrefs.SetInt("s.fps", FpsLimit); PlayerPrefs.SetInt("s.preset", Preset);
            PlayerPrefs.SetFloat("s.scale", RenderScale); PlayerPrefs.SetInt("s.fsr", Fsr ? 1 : 0);
            PlayerPrefs.SetInt("s.aa", AntiAliasing); PlayerPrefs.SetInt("s.shadows", Shadows);
            PlayerPrefs.SetFloat("s.wDens", WheatDensity); PlayerPrefs.SetFloat("s.wDist", WheatDistance);
            PlayerPrefs.SetFloat("s.view", ViewDistance); PlayerPrefs.SetFloat("s.wShadow", WheatShadows);
            PlayerPrefs.SetFloat("s.fov", Fov); PlayerPrefs.SetFloat("s.sens", MouseSens);
            PlayerPrefs.SetInt("s.invY", InvertY ? 1 : 0);
            PlayerPrefs.SetFloat("s.ui", UiScale);
            PlayerPrefs.Save();
        }

        /// <summary>Grafikchip im Prozessor (Intel UHD/Iris, AMD "Radeon Graphics") oder wenig Grafikspeicher?</summary>
        public static bool IsWeakGpu()
        {
            string n = SystemInfo.graphicsDeviceName ?? "";
            bool intel = n.Contains("Intel") && !n.Contains("Arc");
            bool amdApu = n.Contains("Radeon") && n.Contains("Graphics") && !n.Contains("RX");
            return intel || amdApu || SystemInfo.graphicsMemorySize < 2048;
        }

        public static void ApplyPreset(int p)
        {
            Preset = p;
            switch (p)
            {
                case 0: RenderScale = 0.7f; Fsr = true; AntiAliasing = 1; Shadows = 1; WheatDensity = 0.5f; WheatDistance = 55f; ViewDistance = 400f; WheatShadows = 0f; break;
                case 1: RenderScale = 0.85f; Fsr = true; AntiAliasing = 2; Shadows = 2; WheatDensity = 0.75f; WheatDistance = 80f; ViewDistance = 600f; WheatShadows = 8f; break;
                case 2: RenderScale = 1f; Fsr = true; AntiAliasing = 2; Shadows = 2; WheatDensity = 1f; WheatDistance = 105f; ViewDistance = 750f; WheatShadows = 15f; break;
                case 3: RenderScale = 1.25f; Fsr = true; AntiAliasing = 2; Shadows = 3; WheatDensity = 1f; WheatDistance = 105f; ViewDistance = 900f; WheatShadows = 20f; break;
            }
        }

        // ---------------- Anwenden ----------------

        public static void Apply(bool includeResolution)
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync || FpsLimit <= 0 ? -1 : FpsLimit;

            if (includeResolution)
            {
                var mode = WindowMode == 0 ? FullScreenMode.FullScreenWindow : WindowMode == 1 ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.Windowed;
                int w = ResW, h = ResH;
                if (w <= 0 || h <= 0) { w = Screen.currentResolution.width; h = Screen.currentResolution.height; }
                if (WindowMode == 2 && ResW <= 0) { w = Mathf.RoundToInt(w * 0.8f); h = Mathf.RoundToInt(h * 0.8f); }
                if (!Application.isEditor) Screen.SetResolution(w, h, mode);
            }

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                Remember(urp);
                urp.renderScale = Mathf.Clamp(RenderScale, 0.5f, 2f);
                SetProp(urp, "upscalingFilter", RenderScale < 0.99f && Fsr ? 3 : 0);   // 3 = FSR, 0 = Automatisch
                urp.msaaSampleCount = AntiAliasing == 3 ? 4 : 1;
                SetPropObj(urp, "supportsMainLightShadows", Shadows > 0);
                urp.shadowDistance = Shadows switch { 1 => 120f, 2 => 250f, _ => 400f };
                urp.shadowCascadeCount = Shadows switch { 1 => 1, 2 => 2, _ => 4 };
                SetProp(urp, "mainLightShadowmapResolution", Shadows switch { 1 => 1024, _ => 2048 });
                // GPU-Occlusion-Culling aus: mit sehr vielen Objekten (Baeume) unter DX11 nicht stabil genug
                SetPropObj(urp, "gpuResidentDrawerEnableOcclusionCullingInCameras", false);
                SetProp(urp, "gpuResidentDrawerMode", 0);   // GPU Resident Drawer aus (DX11-Absturz mit vielen Objekten)
            }
            ApplyCamera(Camera.main);
        }

        public static void ApplyCamera(Camera cam)
        {
            if (cam == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;
            data.antialiasing = AntiAliasing == 0 ? AntialiasingMode.None
                : AntiAliasing == 1 ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = Shadows > 0;
            cam.allowMSAA = AntiAliasing == 3;
        }

        // URP-Einstellungen, die es nicht in jeder Version oeffentlich gibt: per Reflection setzen
        static void SetProp(object o, string prop, int value)
        {
            var p = o.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p == null || !p.CanWrite) return;
            try { p.SetValue(o, p.PropertyType.IsEnum ? System.Enum.ToObject(p.PropertyType, value) : (object)value); }
            catch { /* in dieser URP-Version nicht aenderbar */ }
        }
        static void SetPropObj(object o, string prop, object value)
        {
            var p = o.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p == null || !p.CanWrite) return;
            try { p.SetValue(o, value); } catch { }
        }
        static object GetProp(object o, string prop)
        {
            var p = o.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p != null && p.CanRead ? p.GetValue(o) : null;
        }

        // Im Editor wird das URP-Asset direkt geaendert: beim Beenden des Play-Modus die alten Werte zuruecksetzen
        static UniversalRenderPipelineAsset remembered;
        static readonly Dictionary<string, object> original = new Dictionary<string, object>();
        static void Remember(UniversalRenderPipelineAsset urp)
        {
            if (!Application.isEditor || remembered == urp) return;
            remembered = urp;
            foreach (var n in new[] { "renderScale", "upscalingFilter", "msaaSampleCount", "supportsMainLightShadows", "shadowDistance", "shadowCascadeCount", "mainLightShadowmapResolution" })
                original[n] = GetProp(urp, n);
            Application.quitting += Restore;
        }
        static void Restore()
        {
            Application.quitting -= Restore;
            if (remembered == null) return;
            foreach (var kv in original)
            {
                if (kv.Value == null) continue;
                var p = remembered.GetType().GetProperty(kv.Key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && p.CanWrite) try { p.SetValue(remembered, kv.Value); } catch { }
            }
            remembered = null; loaded = false;
        }

        public static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (!list.Contains(v) && r.width >= 1024) list.Add(v);
            }
            if (list.Count == 0) list.Add(new Vector2Int(Screen.width, Screen.height));
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }
    }
}
