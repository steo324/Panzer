using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PanzerSim
{
    /// <summary>
    /// Waermebild (WBG) im Zielgeraet: "weiss = warm".
    ///  - Umgebung: graustufig, dunkel, kontrastreich, leichtes Rauschen (kalter Boden, kalter Himmel)
    ///  - Fahrzeuge: hell - eine unbeleuchtete, leuchtende Kopie jedes Modellteils liegt genau ueber dem Fahrzeug.
    ///    Fahrende/brennende Fahrzeuge sind heisser, ausgebrannte Wracks kuehlen ab.
    /// </summary>
    public static class ThermalVision
    {
        public static bool Active { get; private set; }

        static Volume volume;
        static Material normalSky, coldSky;
        static Material hotMat;
        static readonly Dictionary<TankController, List<(Renderer src, Renderer hot)>> overlays = new Dictionary<TankController, List<(Renderer, Renderer)>>();
        static Runner runner;
        static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        public static void Set(bool on)
        {
            if (NetSync.Headless) return;
            Active = on;
            Ensure();
            volume.weight = on ? 1f : 0f;
            // Klarer Himmel ist im Waermebild eiskalt -> fast schwarz: Himmel fuer die Dauer stark abdunkeln
            if (on && RenderSettings.skybox != null && RenderSettings.skybox != coldSky)
            {
                normalSky = RenderSettings.skybox;
                coldSky = new Material(normalSky) { name = "Himmel (Waermebild)" };
                if (coldSky.HasProperty("_Exposure")) coldSky.SetFloat("_Exposure", coldSky.GetFloat("_Exposure") * 0.12f);
                RenderSettings.skybox = coldSky;
            }
            else if (!on && normalSky != null && RenderSettings.skybox == coldSky) RenderSettings.skybox = normalSky;
            if (!on) foreach (var kv in overlays) foreach (var o in kv.Value) if (o.hot != null) o.hot.enabled = false;
        }

        static void Ensure()
        {
            if (volume != null) return;
            var go = new GameObject("Waermebild");
            Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 50; volume.weight = 0f;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = p;
            var ca = p.Add<ColorAdjustments>(true);
            ca.saturation.Override(-100f);
            ca.contrast.Override(35f);
            ca.postExposure.Override(-1.4f);
            ca.colorFilter.Override(new Color(0.88f, 1f, 0.9f));   // leicht gruenlicher Bildschirm
            var fg = p.Add<FilmGrain>(true);
            fg.type.Override(FilmGrainLookup.Medium3); fg.intensity.Override(0.55f); fg.response.Override(0.6f);
            var bl = p.Add<Bloom>(true);
            bl.intensity.Override(0.7f); bl.threshold.Override(0.9f); bl.scatter.Override(0.55f);
            var vg = p.Add<Vignette>(true);
            vg.intensity.Override(0.3f);

            var sh = Shader.Find("PanzerSim/Waerme");
            if (sh == null) sh = Resources.Load<Shader>("Shaders/Waerme");
            hotMat = new Material(sh) { name = "Waermebild-heiss" };
        }

        /// <summary>Wie warm ist das Fahrzeug? (Helligkeit vor der Belichtung; 1 = Umgebung)</summary>
        static float Heat(TankController tc)
        {
            if (tc.burning) return 7f;
            if (tc.destroyed) return 1.6f;
            return 3.2f + Mathf.Clamp01(Mathf.Abs(tc.ForwardSpeed) / 10f) * 1.2f;   // Motor und Laufwerk werden beim Fahren waermer
        }

        class Runner : MonoBehaviour
        {
            float refresh;
            readonly List<TankController> tanks = new List<TankController>();
            readonly List<TankController> dead = new List<TankController>();

            void LateUpdate()
            {
                if (!Active) return;
                refresh -= Time.unscaledDeltaTime;
                if (refresh <= 0f)
                {
                    refresh = 1f;
                    tanks.Clear();
                    tanks.AddRange(Object.FindObjectsByType<TankController>(FindObjectsSortMode.None));
                    dead.Clear();
                    foreach (var kv in overlays) if (kv.Key == null) dead.Add(kv.Key);
                    foreach (var d in dead) overlays.Remove(d);
                }
                foreach (var tc in tanks)
                {
                    if (tc == null || tc.visual == null || tc.visual.Renderers == null) continue;
                    if (!overlays.TryGetValue(tc, out var list)) { list = Build(tc); overlays[tc] = list; }
                    float h = Heat(tc);
                    block.SetColor("_Color", new Color(h, h, h, 1f));
                    foreach (var (src, hot) in list)
                    {
                        if (hot == null) continue;
                        hot.enabled = src != null && src.enabled && src.gameObject.activeInHierarchy;
                        hot.SetPropertyBlock(block);
                    }
                }
            }

            static List<(Renderer, Renderer)> Build(TankController tc)
            {
                var list = new List<(Renderer, Renderer)>();
                foreach (var src in tc.visual.Renderers)
                {
                    if (src == null) continue;
                    var go = new GameObject("Waerme");
                    go.layer = src.gameObject.layer;
                    go.transform.SetParent(src.transform.parent, false);
                    go.transform.localPosition = src.transform.localPosition;
                    go.transform.localRotation = src.transform.localRotation;
                    go.transform.localScale = src.transform.localScale;
                    Renderer hot = null;
                    if (src is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                    {
                        var h = go.AddComponent<SkinnedMeshRenderer>();
                        h.sharedMesh = smr.sharedMesh; h.bones = smr.bones; h.rootBone = smr.rootBone;
                        h.updateWhenOffscreen = true;
                        hot = h;
                    }
                    else if (src is MeshRenderer && src.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
                    {
                        go.transform.SetParent(src.transform, false);   // Teil bewegt sich (Turm, Raeder): direkt anhaengen
                        go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
                        go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                        hot = go.AddComponent<MeshRenderer>();
                    }
                    if (hot == null) { Object.Destroy(go); continue; }
                    var mats = new Material[Mathf.Max(1, src.sharedMaterials.Length)];
                    for (int i = 0; i < mats.Length; i++) mats[i] = hotMat;
                    hot.sharedMaterials = mats;
                    hot.shadowCastingMode = ShadowCastingMode.Off;
                    hot.receiveShadows = false;
                    hot.enabled = false;
                    list.Add((src, hot));
                }
                return list;
            }
        }
    }
}
