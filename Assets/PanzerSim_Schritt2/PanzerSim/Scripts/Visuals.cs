using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PanzerSim
{
    /// <summary>
    /// Bildlook: Farbkorrektur (filmisches Tonemapping), Bloom, Vignette, Himmel, Sonne, Umgebungslicht, SSAO.
    /// Alles zur Laufzeit erzeugt; die Staerke haengt an der Grafik-Qualitaet (GameSettings.Look 0..3).
    /// Start mit -novisuals schaltet den Look ab (zum Vergleichen).
    /// </summary>
    public static class Visuals
    {
        static Volume volume;
        static VolumeProfile profile;
        static Tonemapping tone;
        static ColorAdjustments color;
        static WhiteBalance white;
        static Bloom bloom;
        static Vignette vignette;
        static ShadowsMidtonesHighlights smh;
        static Material sky;
        static bool disabled;

        public static void Apply(Camera cam, int look)
        {
            if (NetSync.Headless) return;
            disabled = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-novisuals") >= 0;
            EnsureVolume();
            bool on = !disabled;
            volume.enabled = on;

            // 0 Niedrig: nur Farbe/Tonemapping (fast kostenlos) · 1 Mittel: + Bloom · 2 Hoch: + Vignette · 3 Ultra: Bloom in hoher Qualitaet
            var args = System.Environment.GetCommandLineArgs();
            bool Has(string a) => System.Array.IndexOf(args, a) >= 0;   // Fehlersuche: einzelne Effekte abschalten
            bloom.active = look >= 1 && !Has("-nobloom");
            if (Has("-nossao")) look = Mathf.Min(look, 0);
            bloom.highQualityFiltering.Override(look >= 3);
            bloom.downscale.Override(look >= 3 ? BloomDownscaleMode.Half : BloomDownscaleMode.Quarter);
            vignette.active = look >= 2;

            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null) data.renderPostProcessing = on;
            }
            ApplySky(on && !Has("-nosky"));
            ApplySsao(on ? look : -1);
        }

        static void EnsureVolume()
        {
            if (volume != null) return;
            var go = new GameObject("Bildlook");
            Object.DontDestroyOnLoad(go);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;

            tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.45f);    // ACES dunkelt ab - etwas heller belichten
            color.contrast.Override(8f);
            color.saturation.Override(-6f);        // leicht entsaettigt, wirkt natuerlicher/filmischer

            white = profile.Add<WhiteBalance>(true);
            white.temperature.Override(6f);        // etwas waermer (Nachmittagslicht)

            smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.96f, 0.99f, 1.06f, 0f));     // Schatten leicht kuehl
            smh.highlights.Override(new Vector4(1.04f, 1.01f, 0.96f, 0f));  // Lichter leicht warm

            bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.3f);
            bloom.scatter.Override(0.65f);

            vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.45f);
        }

        // ================= Tageszeit =================
        public static readonly string[] TimeNames = { "Morgen (Dunst)", "Mittag", "Nachmittag", "Abend" };
        struct DayLook
        {
            public float elev, azim, sunI, temp, exposure, fogDensity, fogFalloff, sunGlow;
            public Color sun, fog, sunFog, skyTint;
        }
        static readonly DayLook[] Days =
        {
            // Morgen: tief im Osten, warmes Licht, Bodennebel in den Senken
            new DayLook { elev = 11f, azim = 95f, sunI = 1.35f, temp = 14f, exposure = 0.55f, fogDensity = 0.0009f, fogFalloff = 0.035f, sunGlow = 0.9f,
                          sun = new Color(1f, 0.80f, 0.62f), fog = new Color(0.80f, 0.79f, 0.78f), sunFog = new Color(1f, 0.86f, 0.66f), skyTint = new Color(0.56f, 0.56f, 0.60f) },
            // Mittag: hoch, neutral, klare Luft
            new DayLook { elev = 58f, azim = 185f, sunI = 1.9f, temp = 2f, exposure = 0.35f, fogDensity = 0.0002f, fogFalloff = 0.008f, sunGlow = 0.3f,
                          sun = new Color(1f, 0.97f, 0.92f), fog = new Color(0.70f, 0.77f, 0.86f), sunFog = new Color(0.95f, 0.93f, 0.88f), skyTint = new Color(0.50f, 0.56f, 0.64f) },
            // Nachmittag (wie bisher): leicht warm
            new DayLook { elev = 36f, azim = 228f, sunI = 1.7f, temp = 6f, exposure = 0.45f, fogDensity = 0.00038f, fogFalloff = 0.010f, sunGlow = 0.5f,
                          sun = new Color(1f, 0.93f, 0.82f), fog = new Color(0.72f, 0.77f, 0.83f), sunFog = new Color(1f, 0.90f, 0.74f), skyTint = new Color(0.52f, 0.56f, 0.62f) },
            // Abend: tief im Westen, orange, lange Schatten
            new DayLook { elev = 8f, azim = 278f, sunI = 1.3f, temp = 20f, exposure = 0.6f, fogDensity = 0.0005f, fogFalloff = 0.012f, sunGlow = 1f,
                          sun = new Color(1f, 0.62f, 0.38f), fog = new Color(0.78f, 0.68f, 0.62f), sunFog = new Color(1f, 0.66f, 0.38f), skyTint = new Color(0.58f, 0.52f, 0.52f) },
        };
        static DayLook Day => Days[Mathf.Clamp(GameSettings.TimeOfDay, 0, Days.Length - 1)];

        // ================= Hoehennebel =================
        /// <summary>Hoehennebel aktiv (HeightFogFeature fragt das ab).</summary>
        public static bool FogOn { get; private set; }
        static float fogGround, fogScale = 1f;

        /// <summary>Nach dem Bau einer Karte: Bodenhoehe fuer den Nebel, Spiegelung neu aufnehmen. fogStrength: Karte (1 = normal).</summary>
        public static void OnMapBuilt(float groundY, float fogStrength)
        {
            fogGround = groundY; fogScale = fogStrength;
            UpdateFog();
            RenderReflections();
        }

        /// <summary>Hauptmenue: Podest liegt 2000 m unter der Karte - dort kein Hoehennebel (sonst ist alles weiss).</summary>
        public static void MenuMode()
        {
            fogScale = 0f;
            UpdateFog();
        }

        static void UpdateFog()
        {
            var d = Day;
            bool on = !disabled && GameSettings.Effects >= 1 && fogScale > 0f && !NetSync.Headless;
            FogOn = on;
            Shader.SetGlobalVector("_PzFogParams", new Vector4(on ? d.fogDensity * fogScale : 0f, d.fogFalloff, fogGround, 0.92f));
            Shader.SetGlobalColor("_PzFogColor", d.fog);
            Shader.SetGlobalColor("_PzSunColor", new Color(d.sunFog.r, d.sunFog.g, d.sunFog.b, d.sunGlow));
            Vector3 toSun = Quaternion.Euler(d.elev, d.azim, 0f) * Vector3.back;
            Shader.SetGlobalVector("_PzSunDir", toSun.normalized);
            Shader.SetGlobalFloat("_PzSkyDist", 6000f);
            // der eingebaute Entfernungsnebel bleibt nur als Ersatz, wenn der Hoehennebel aus ist
            if (on) RenderSettings.fog = false;
        }

        // ================= Spiegelung =================
        static ReflectionProbe probe;
        /// <summary>Umgebung (Himmel + Gelaende) einmal in eine Spiegelungs-Sonde aufnehmen: sonst spiegeln glaenzende Teile Schwarz.</summary>
        public static void RenderReflections()
        {
            if (NetSync.Headless) return;
            if (probe == null)
            {
                var go = new GameObject("Spiegelung (Umgebung)");
                Object.DontDestroyOnLoad(go);
                probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Realtime;
                probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
                probe.size = new Vector3(40000f, 8000f, 40000f);
                probe.resolution = 256;
                probe.hdr = true;
                probe.clearFlags = ReflectionProbeClearFlags.Skybox;
                probe.nearClipPlane = 1f; probe.farClipPlane = 4000f;
                probe.shadowDistance = 0f;
                probe.importance = 0;
            }
            probe.transform.position = new Vector3(0f, fogGround + 25f, 0f);
            probe.RenderProbe();
        }

        // ================= Panzer: matt statt Chrom =================
        static readonly System.Collections.Generic.HashSet<Material> matted = new System.Collections.Generic.HashSet<Material>();
        static readonly int PMetal = Shader.PropertyToID("metallicFactor"), PRough = Shader.PropertyToID("roughnessFactor"),
                            PLitMetal = Shader.PropertyToID("_Metallic"), PLitSmooth = Shader.PropertyToID("_Smoothness"), PGloss = Shader.PropertyToID("_Glossiness");
        /// <summary>
        /// Panzerlack ist matt: Metall-Anteil der Modelle begrenzen, Oberflaeche rau (manche Modelle sind als blankes Metall angelegt
        /// und wirken dann verchromt/schwarz).
        /// </summary>
        public static void MatteTank(Renderer[] renderers)
        {
            if (renderers == null) return;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !matted.Add(m)) continue;
                    if (m.HasProperty(PMetal)) m.SetFloat(PMetal, Mathf.Min(m.GetFloat(PMetal), 0.12f));
                    if (m.HasProperty(PRough)) m.SetFloat(PRough, 1f);
                    if (m.HasProperty(PLitMetal)) m.SetFloat(PLitMetal, Mathf.Min(m.GetFloat(PLitMetal), 0.12f));
                    if (m.HasProperty(PLitSmooth)) m.SetFloat(PLitSmooth, Mathf.Min(m.GetFloat(PLitSmooth), 0.35f));
                    if (m.HasProperty(PGloss)) m.SetFloat(PGloss, Mathf.Min(m.GetFloat(PGloss), 0.35f));
                }
            }
        }


        /// <summary>Himmel mit mehr Dunst, kraeftigere Sonne (nach Tageszeit), Umgebungslicht aus dem Himmel.</summary>
        static void ApplySky(bool on)
        {
            Light sun = RenderSettings.sun;
            if (sun == null)
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            var day = Day;
            if (sun != null)
            {
                sun.intensity = on ? day.sunI : 1f;
                sun.color = on ? day.sun : new Color(1f, 0.957f, 0.839f);
                sun.shadowStrength = on ? 0.92f : 1f;
                if (on) sun.transform.rotation = Quaternion.Euler(day.elev, day.azim, 0f);
            }
            if (white != null) white.temperature.Override(day.temp);
            if (color != null) color.postExposure.Override(day.exposure);
            if (sky == null && RenderSettings.skybox != null && RenderSettings.skybox.shader.name == "Skybox/Procedural")
            {
                sky = new Material(RenderSettings.skybox) { name = "Himmel" };
                sky.SetFloat("_SunDisk", 2f);            // hochwertige Sonnenscheibe
                sky.SetFloat("_SunSize", 0.035f);
                sky.SetFloat("_SunSizeConvergence", 6f);
                sky.SetFloat("_AtmosphereThickness", 1.15f);   // mehr Dunst am Horizont
                sky.SetColor("_SkyTint", new Color(0.52f, 0.56f, 0.62f));
                sky.SetColor("_GroundColor", new Color(0.36f, 0.35f, 0.32f));
                sky.SetFloat("_Exposure", 1.15f);
            }
            if (sky != null) sky.SetColor("_SkyTint", day.skyTint);
            if (sky != null && on) RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = on ? 1.1f : 1f;
            DynamicGI.UpdateEnvironment();
            UpdateFog();
            if (probe != null) RenderReflections();   // Tageszeit geaendert: Spiegelung neu
        }

        // ---------- SSAO (Renderer-Feature im PC_Renderer) per Reflection einstellen ----------
        static void ApplySsao(int look)
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) return;
            var list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(urp) as ScriptableRendererData[];
            if (list == null) return;
            foreach (var rd in list)
            {
                if (rd == null) continue;
                foreach (var f in rd.rendererFeatures)
                {
                    if (f == null || f.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                    f.SetActive(look >= 1);
                    var s = f.GetType().GetField("m_Settings", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(f);
                    if (s == null || look < 1) continue;
                    Set(s, "Intensity", look >= 2 ? 0.9f : 0.7f);
                    Set(s, "Radius", 0.45f);
                    Set(s, "DirectLightingStrength", 0.3f);
                    Set(s, "Downsample", look <= 1);             // Mittel: halbe Aufloesung
                    SetEnum(s, "Samples", look >= 3 ? 0 : look == 2 ? 1 : 2);   // 0 High, 1 Medium, 2 Low
                    SetEnum(s, "BlurQuality", look >= 3 ? 0 : 1);               // 0 High, 1 Medium
                }
            }
        }

        static void Set(object o, string field, object v)
        {
            var fi = o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (fi != null) try { fi.SetValue(o, v); } catch { }
        }
        static void SetEnum(object o, string field, int v)
        {
            var fi = o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (fi != null && fi.FieldType.IsEnum) try { fi.SetValue(o, System.Enum.ToObject(fi.FieldType, v)); } catch { }
        }
    }
}
