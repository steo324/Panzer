using UnityEditor;

namespace PanzerSim.EditorTools
{
    /// <summary>
    /// Bodentexturen (Assets/Resources/Boden, CC0 von ambientCG.com): *_Normal = Normal Map,
    /// *_Maske = lineare Maske (R Metall, G Verdeckung, B Hoehe, A Glaette), alles kachelbar, komprimiert, Anisotropie.
    /// </summary>
    public class BodenTexturImport : AssetPostprocessor
    {
        /// <summary>Vor dem Build: falls Texturen vor diesem Skript importiert wurden, neu importieren.</summary>
        public static void Ensure()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Boden" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) continue;
                bool wrong = (p.Contains("_Normal") && ti.textureType != TextureImporterType.NormalMap)
                          || (p.Contains("_Maske") && ti.sRGBTexture) || ti.anisoLevel != 8;
                if (wrong) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            }
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Boden/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;   // Boden schraeg von oben: bleibt in der Ferne scharf
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.compressionQuality = 100;
            ti.maxTextureSize = 2048;
            if (assetPath.Contains("_Normal"))
            {
                ti.textureType = TextureImporterType.NormalMap;
            }
            else if (assetPath.Contains("_Maske"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = false;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.maxTextureSize = 1024;
            }
            else
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.None;
            }
        }
    }
}
