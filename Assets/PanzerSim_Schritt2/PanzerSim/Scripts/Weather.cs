using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Wetter (im Menue waehlbar, gespeichert): Klar, Regen, Nebel, Schnee - dazu die Tageszeit "Nacht" (Visuals).
    /// Optik: Regen-/Schneepartikel um die Kamera, dichterer Dunst, graues Licht. Spielwirkung: Sichtweite fuer die
    /// KI-Panzer, Pak und Panzerfaeuste (Nacht 400 m, Nebel 250 m, Schnee 900 m, Regen 1300 m); Fahrzeuge mit
    /// Waermebild (hasThermal) sehen nachts und im Nebel deutlich weiter. Spieler mit Waermebild: im Visier V.
    /// Boden (Schnee rutscht, Regen macht Schlamm) siehe Block "Boden mit Wirkung".
    /// </summary>
    public static class Weather
    {
        public const int Clear = 0, Rain = 1, Fog = 2, Snow = 3;
        public static readonly string[] Names = { "Klar", "Regen", "Nebel", "Schnee" };
        public static int Current { get; private set; }
        /// <summary>Test (-wetter n): Wetter nur fuer diesen Start, nicht gespeichert.</summary>
        public static int Override = -1;
        static ParticleSystem rain, snow;
        static Runner runner;

        /// <summary>Gewaehltes Wetter (gespeichert).</summary>
        public static int Setting
        {
            get => Override >= 0 ? Mathf.Clamp(Override, 0, Names.Length - 1) : Mathf.Clamp(PlayerPrefs.GetInt("s.wetter", 0), 0, Names.Length - 1);
            set { PlayerPrefs.SetInt("s.wetter", Mathf.Clamp(value, 0, Names.Length - 1)); PlayerPrefs.Save(); }
        }

        /// <summary>Wetter fuer die Runde anwenden (Optik + Sichtweite). Im Menue: klar (Podest).</summary>
        public static void Apply(int w)
        {
            Current = Mathf.Clamp(w, 0, Names.Length - 1);
            switch (Current)
            {
                case Rain: Visuals.WeatherFogMul = 3f; Visuals.WeatherSunMul = 0.45f; Visuals.WeatherSkyMul = 0.55f; Visuals.WeatherGrey = 0.85f; Visuals.WeatherFogColor = new Color(0.55f, 0.58f, 0.62f); break;
                case Fog: Visuals.WeatherFogMul = 14f; Visuals.WeatherSunMul = 0.55f; Visuals.WeatherSkyMul = 0.8f; Visuals.WeatherGrey = 1f; Visuals.WeatherFogColor = new Color(0.74f, 0.76f, 0.78f); break;
                case Snow: Visuals.WeatherFogMul = 5f; Visuals.WeatherSunMul = 0.6f; Visuals.WeatherSkyMul = 0.85f; Visuals.WeatherGrey = 0.9f; Visuals.WeatherFogColor = new Color(0.82f, 0.84f, 0.88f); break;
                default: Visuals.WeatherFogMul = 1f; Visuals.WeatherSunMul = 1f; Visuals.WeatherSkyMul = 1f; Visuals.WeatherGrey = 0f; Visuals.WeatherFogColor = Color.clear; break;
            }
            Visuals.RefreshSky();
            if (NetSync.Headless) return;
            Ensure();
            SetEmit(rain, Current == Rain ? 3500f : 0f);
            SetEmit(snow, Current == Snow ? 1800f : 0f);
            // Karten ohne Hoehennebel: eingebauten Nebel nutzen
            if (Current == Fog && !Visuals.FogOn) { RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 15f; RenderSettings.fogEndDistance = 380f; RenderSettings.fogColor = Visuals.WeatherFogColor; }
        }

        /// <summary>Wie weit sieht dieser Beobachter (0 = unbegrenzt)? thermal: Waermebildgeraet.</summary>
        public static float SightRange(bool thermal)
        {
            float r = 0f;
            switch (Current)
            {
                case Rain: r = 1300f; break;
                case Fog: r = thermal ? 1200f : 250f; break;
                case Snow: r = thermal ? 1500f : 900f; break;
            }
            if (Visuals.IsNight) { float n = thermal ? 2500f : 400f; r = r > 0f ? Mathf.Min(r, n) : n; }
            return r;
        }

        public static float SightRange(TankController viewer) => SightRange(viewer != null && viewer.spec != null && viewer.spec.hasThermal);

        /// <summary>Sieht der Beobachter auf diese Entfernung noch etwas?</summary>
        public static bool InRange(TankController viewer, float dist) { float r = SightRange(viewer); return r <= 0f || dist <= r; }
        public static bool InRange(float dist) { float r = SightRange(false); return r <= 0f || dist <= r; }

        static void SetEmit(ParticleSystem ps, float rate)
        {
            if (ps == null) return;
            var em = ps.emission; em.rateOverTime = rate; em.enabled = rate > 0f;
            if (rate <= 0f) ps.Clear();
        }

        static void Ensure()
        {
            if (runner != null && rain != null && snow != null) return;
            var sh = Shader.Find("PanzerSim/Partikel");
            if (sh == null) sh = Resources.Load<Shader>("Shaders/Partikel");
            if (sh == null) { Debug.LogWarning("Wetter: Partikel-Shader fehlt."); return; }
            var go = new GameObject("Wetter");
            Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
            rain = Make(go.transform, "Regen", sh, Dot(32, 0.5f), 6000, new Color(0.72f, 0.76f, 0.82f, 0.38f), 0.025f, 1.2f, true);
            var rm = rain.main; rm.startSpeed = 0f; rm.gravityModifier = 0f;
            var rv = rain.velocityOverLifetime; rv.enabled = true; rv.space = ParticleSystemSimulationSpace.World;
            rv.x = new ParticleSystem.MinMaxCurve(-1.5f, -0.5f); rv.y = new ParticleSystem.MinMaxCurve(-24f, -20f); rv.z = new ParticleSystem.MinMaxCurve(0.2f, 1f);
            snow = Make(go.transform, "Schnee", sh, Dot(32, 0.15f), 8000, new Color(1f, 1f, 1f, 0.95f), 0.11f, 7f, false);
            var sv = snow.velocityOverLifetime; sv.enabled = true; sv.space = ParticleSystemSimulationSpace.World;
            sv.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f); sv.y = new ParticleSystem.MinMaxCurve(-1.8f, -1.1f); sv.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var sn = snow.noise; sn.enabled = true; sn.strength = 0.6f; sn.frequency = 0.4f;
        }

        static ParticleSystem Make(Transform parent, string name, Shader sh, Texture2D tex, int max, Color col, float size, float life, bool stretch)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            var ps = g.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.maxParticles = max; main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = life; main.startSize = size; main.startColor = col;
            var shp = ps.shape; shp.enabled = true; shp.shapeType = ParticleSystemShapeType.Box; shp.scale = new Vector3(70f, 1f, 70f);
            var em = ps.emission; em.enabled = false;
            var r = g.GetComponent<ParticleSystemRenderer>();
            var m = new Material(sh); m.SetTexture("_BaseMap", tex); m.SetFloat("_SrcBlend", 5); m.SetFloat("_DstBlend", 10); m.SetFloat("_Lit", 0.6f);
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            if (stretch) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.045f; r.lengthScale = 1f; }
            else r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            return ps;
        }

        static Texture2D Dot(int n, float hard)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            float c = (n - 1) / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) / Mathf.Max(0.05f, 1f - hard))));
                }
            t.Apply();
            return t;
        }

        class Runner : MonoBehaviour
        {
            void LateUpdate()
            {
                var cam = Camera.main;
                if (cam == null) return;
                // Partikel fallen um die Kamera (Weltraum-Simulation: bewegt man sich, bleiben sie in der Luft)
                Vector3 p = cam.transform.position;
                bool show = GameManager.Instance != null && GameManager.Instance.InGame;
                if (rain != null) { rain.transform.position = p + Vector3.up * 14f + cam.transform.forward * 10f; var e = rain.emission; e.enabled = show && Current == Rain; }
                if (snow != null) { snow.transform.position = p + Vector3.up * 9f + cam.transform.forward * 10f; var e = snow.emission; e.enabled = show && Current == Snow; }
            }
        }
    }
}
