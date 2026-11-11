using UnityEditor;
using UnityEngine;

namespace PanzerSim.EditorTools
{
    /// <summary>
    /// Menue "Panzer/Modell-Prefabs einrichten": macht aus Assets/Modelle_Quelle/&lt;Name&gt; (Geometrie-.glb + Texturen aus
    /// Tools/Blender/rig_*.py) das Spielmodell Assets/Resources/Modelle/&lt;Name&gt;.prefab mit URP-Lit-Materialien.
    /// Die Texturen sind normale Unity-Texturen mit Crunch-Kompression - in die .glb eingebettet waeren sie im Build riesig
    /// (GitHub nimmt keine Dateien ueber 100 MB). Wird vor jedem Build automatisch aufgerufen (BuildGame).
    /// </summary>
    public static class ModelPrefabs
    {
        struct Entry { public string name; public string[] materials; public Entry(string n, params string[] m) { name = n; materials = m; } }

        // Name des Ordners/.glb/Prefabs  +  Materialnamen im Modell (= Texturpraefix)
        static readonly Entry[] Models =
        {
            new Entry("M4A1E9", "Frame", "Turret", "Wheels"),
            new Entry("PanzerIV_F2", "hull_001", "hull_002", "drivetrain", "turret", "track"),
        };

        [MenuItem("Panzer/Modell-Prefabs einrichten")]
        public static void RunAll()
        {
            foreach (var e in Models) Run(e);
            AssetDatabase.SaveAssets();
        }

        static void Run(Entry e)
        {
            string src = "Assets/Modelle_Quelle/" + e.name;
            string prefabPath = "Assets/Resources/Modelle/" + e.name + ".prefab";
            var glb = AssetDatabase.LoadAssetAtPath<GameObject>($"{src}/{e.name}.glb");
            if (glb == null) { Debug.LogWarning($"Modell: {src}/{e.name}.glb fehlt."); return; }

            var mats = new System.Collections.Generic.Dictionary<string, Material>();
            foreach (var k in e.materials)
            {
                var bc = Tex($"{src}/{k}_BaseColor.png", 2048, true, false);
                var nm = Tex($"{src}/{k}_Normal.png", 1024, false, true);
                var ms = Tex($"{src}/{k}_MetallicSmoothness.png", 1024, false, false);
                string path = $"{src}/{k}.mat";
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(lit); AssetDatabase.CreateAsset(m, path); }
                m.shader = lit;
                m.enableInstancing = true;
                m.SetTexture("_BaseMap", bc); m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_BumpMap", nm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP");
                m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Smoothness", 1f); m.SetFloat("_SmoothnessTextureChannel", 0f);   // Glaette = Alpha der Metallic-Textur
                EditorUtility.SetDirty(m);
                mats[k] = m;
            }

            var inst = Object.Instantiate(glb);
            inst.name = e.name;
            int replaced = 0;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var sm = r.sharedMaterials;
                for (int i = 0; i < sm.Length; i++)
                {
                    if (sm[i] == null) continue;
                    // laengster passender Name zuerst (hull_001 vs. hull_002 sind eindeutig, "turret" vs. "Turret" auch)
                    foreach (var k in e.materials)
                        if (sm[i].name == k || sm[i].name.StartsWith(k + " ") || sm[i].name.StartsWith(k + "(") || sm[i].name.StartsWith(k + "."))
                        { sm[i] = mats[k]; replaced++; break; }
                }
                r.sharedMaterials = sm;
            }
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Modelle")) AssetDatabase.CreateFolder("Assets/Resources", "Modelle");
            PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            Object.DestroyImmediate(inst);
            Debug.Log($"Modell {e.name}: Prefab gespeichert ({replaced} Materialien ersetzt): {prefabPath}");
        }

        static Texture2D Tex(string path, int maxSize, bool srgb, bool normal)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("Modell: Textur fehlt: " + path); return null; }
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.maxTextureSize != maxSize || ti.sRGBTexture != srgb || ti.textureType != type ||
                ti.textureCompression != TextureImporterCompression.Compressed || !ti.crunchedCompression)
            {
                ti.textureType = type;
                ti.sRGBTexture = srgb;
                ti.maxTextureSize = maxSize;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.crunchedCompression = true;      // im Build stark verkleinert
                ti.compressionQuality = 75;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
