using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Tarnanstriche: je Panzer waehlbar (Menue, gespeichert). Getoent wird die Grundfarbe aller Modellteile (die Textur mit
    /// Bemalung, Rost und Schmutz bleibt sichtbar). Im Mehrspieler sehen alle den Anstrich (NetTank).
    /// </summary>
    public static class Camo
    {
        public static readonly string[] Names = { "Original", "Dunkelgelb", "Olivgruen", "Feldgrau", "Wintertarn", "Wuestensand", "Rotbraun" };
        static readonly Color[] Tints =
        {
            Color.white,
            new Color(1.18f, 1.02f, 0.66f),
            new Color(0.62f, 0.76f, 0.48f),
            new Color(0.70f, 0.74f, 0.74f),
            new Color(2.1f, 2.1f, 2.15f),
            new Color(1.30f, 1.12f, 0.80f),
            new Color(1.05f, 0.70f, 0.55f),
        };
        static readonly int PBase = Shader.PropertyToID("_BaseColor"), PColor = Shader.PropertyToID("_Color"), PGltf = Shader.PropertyToID("baseColorFactor");

        /// <summary>Test (-tarn n): Anstrich nur fuer diesen Start, nicht gespeichert.</summary>
        public static int Override = -1;
        public static int For(TankSpec s) => Override >= 0 ? Mathf.Clamp(Override, 0, Names.Length - 1) : s == null ? 0 : Mathf.Clamp(PlayerPrefs.GetInt("tarn." + s.name, 0), 0, Names.Length - 1);
        public static void Set(TankSpec s, int v) { if (s == null) return; PlayerPrefs.SetInt("tarn." + s.name, Mathf.Clamp(v, 0, Names.Length - 1)); PlayerPrefs.Save(); }

        /// <summary>Anstrich auf den Panzer legen (0 = Original).</summary>
        public static void Apply(TankController t, int idx)
        {
            if (t == null || t.visual == null || t.visual.Renderers == null || NetSync.Headless) return;
            idx = Mathf.Clamp(idx, 0, Names.Length - 1);
            var mpb = new MaterialPropertyBlock();
            foreach (var r in t.visual.Renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (idx == 0) { r.SetPropertyBlock(null, i); continue; }
                    mpb.Clear();
                    if (m.HasProperty(PBase)) mpb.SetColor(PBase, Mul(m.GetColor(PBase), Tints[idx]));
                    if (m.HasProperty(PColor)) mpb.SetColor(PColor, Mul(m.GetColor(PColor), Tints[idx]));
                    if (m.HasProperty(PGltf)) mpb.SetColor(PGltf, Mul(m.GetColor(PGltf), Tints[idx]));
                    r.SetPropertyBlock(mpb, i);
                }
            }
        }

        static Color Mul(Color a, Color t) => new Color(a.r * t.r, a.g * t.g, a.b * t.b, a.a);
    }
}
