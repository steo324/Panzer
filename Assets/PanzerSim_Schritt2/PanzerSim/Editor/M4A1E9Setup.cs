using UnityEditor;
using UnityEngine;

namespace PanzerSim.EditorTools
{
    /// <summary>
    /// Menue "Panzer/M4A1E9-Modell einrichten": macht aus Assets/Modelle_Quelle/M4A1E9 (Geometrie-.glb + Texturen aus
    /// Tools/Blender/rig_sherman.py) das Spielmodell Assets/Resources/Modelle/M4A1E9.prefab mit URP-Lit-Materialien.
    /// Die Texturen sind normale Unity-Texturen (komprimiert) - in die .glb eingebettet waeren sie im Build riesig.
    /// Wird vor jedem Build automatisch aufgerufen (BuildGame).
    /// </summary>
    public static class M4A1E9Setup
    {
        const string Src = "Assets/Modelle_Quelle/M4A1E9";
        const string Prefab = "Assets/Resources/Modelle/M4A1E9.prefab";
        static readonly string[] Keys = { "Frame", "Turret", "Wheels" };

        [MenuItem("Panzer/M4A1E9-Modell einrichten")]
        public static void Run()
        {
            var glb = AssetDatabase.LoadAssetAtPath<GameObject>(Src + "/M4A1E9.glb");
            if (glb == null) { Debug.LogWarning("M4A1E9: " + Src + "/M4A1E9.glb fehlt."); return; }

            var mats = new System.Collections.Generic.Dictionary<string, Material>();
            foreach (var k in Keys)
            {
                var bc = Tex($"{Src}/{k}_BaseColor.png", 2048, true, false);
                var nm = Tex($"{Src}/{k}_Normal.png", 1024, false, true);   // 1024 reicht; haelt den Build unter GitHubs 100-MB-Grenze
                var ms = Tex($"{Src}/{k}_MetallicSmoothness.png", 1024, false, false);
                string path = $"{Src}/{k}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
                m.shader = Shader.Find("Universal Render Pipeline/Lit");
                m.enableInstancing = true;
                m.SetTexture("_BaseMap", bc); m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_BumpMap", nm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP");
                m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Smoothness", 1f); m.SetFloat("_SmoothnessTextureChannel", 0f);   // Glaette = Alpha der Metallic-Textur
                EditorUtility.SetDirty(m);
                mats[k] = m;
            }

            var inst = Object.Instantiate(glb);
            inst.name = "M4A1E9";
            int replaced = 0;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var sm = r.sharedMaterials;
                for (int i = 0; i < sm.Length; i++)
                    foreach (var k in Keys)
                        if (sm[i] != null && sm[i].name.StartsWith(k)) { sm[i] = mats[k]; replaced++; }
                r.sharedMaterials = sm;
            }
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Modelle")) AssetDatabase.CreateFolder("Assets/Resources", "Modelle");
            PrefabUtility.SaveAsPrefabAsset(inst, Prefab);
            Object.DestroyImmediate(inst);
            AssetDatabase.SaveAssets();
            Debug.Log($"M4A1E9: Prefab gespeichert ({replaced} Materialien ersetzt): {Prefab}");
        }

        static Texture2D Tex(string path, int maxSize, bool srgb, bool normal)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("M4A1E9: Textur fehlt: " + path); return null; }
            bool changed = ti.maxTextureSize != maxSize || ti.sRGBTexture != srgb ||
                           ti.textureType != (normal ? TextureImporterType.NormalMap : TextureImporterType.Default) ||
                           ti.textureCompression != TextureImporterCompression.Compressed;
            if (changed)
            {
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = srgb;
                ti.maxTextureSize = maxSize;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
