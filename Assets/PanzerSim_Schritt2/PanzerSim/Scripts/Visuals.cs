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
            bloom.active = look >= 1;
            bloom.highQualityFiltering.Override(look >= 3);
            bloom.downscale.Override(look >= 3 ? BloomDownscaleMode.Half : BloomDownscaleMode.Quarter);
            vignette.active = look >= 2;

            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null) data.renderPostProcessing = on;
            }
            ApplySky(on);
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

        /// <summary>Himmel mit mehr Dunst, kraeftigere Sonne, Umgebungslicht aus dem Himmel.</summary>
        static void ApplySky(bool on)
        {
            Light sun = RenderSettings.sun;
            if (sun == null)
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            if (sun != null)
            {
                sun.intensity = on ? 1.7f : 1f;
                sun.color = on ? new Color(1f, 0.93f, 0.82f) : new Color(1f, 0.957f, 0.839f);
                sun.shadowStrength = on ? 0.92f : 1f;
            }
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
            if (sky != null && on) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = on ? 1.1f : 1f;
            DynamicGI.UpdateEnvironment();
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
