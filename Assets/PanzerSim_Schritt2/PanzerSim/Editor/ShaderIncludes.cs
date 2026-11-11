using System.IO;
using UnityEditor;
using UnityEngine;

namespace PanzerSim.EditorTools
{
    /// <summary>
    /// Der Code erzeugt viele Materialien zur Laufzeit per Shader.Find("Universal Render Pipeline/Lit") usw.
    /// Unity packt aber nur Shader in den Build, auf die ein Asset verweist - sonst liefert Shader.Find null
    /// und alles wird pink. Deshalb liegen unter Assets/Resources/ShaderRefs Referenz-Materialien
    /// (mit den Varianten, die der Code braucht: Instancing, transparent). Wird vor jedem Build aufgerufen.
    /// </summary>
    public static class ShaderIncludes
    {
        const string Dir = "Assets/Resources/ShaderRefs";

        [MenuItem("Panzer/Shader fuer Build sicherstellen")]
        public static void Ensure()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Resources", "ShaderRefs");

            Make("Lit", "Universal Render Pipeline/Lit", m => { m.enableInstancing = true; });
            Make("LitTransparent", "Universal Render Pipeline/Lit", m =>
            {
                // wie TankController.GlassMaterial (Panzerplatten-Ansicht)
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = 3000;
            });
            Make("TerrainLit", "Universal Render Pipeline/Terrain/Lit", null);
            // Gelaende mit Fototexturen: Normal Map, Maske, Hoehen-Uebergaenge (Varianten muessen mit in den Build)
            Make("TerrainLitHD", "Universal Render Pipeline/Terrain/Lit", m => PanzerSim.PlainsMap.SetTerrainDetail(m, true));
            Make("SpritesDefault", "Sprites/Default", null);
            Make("SkyProcedural", "Skybox/Procedural", null);
            AssetDatabase.SaveAssets();
        }

        static void Make(string name, string shaderName, System.Action<Material> setup)
        {
            string path = $"{Dir}/{name}.mat";
            var sh = Shader.Find(shaderName);
            if (sh == null) { Debug.LogWarning($"Shader fehlt im Projekt: {shaderName}"); return; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(sh) { name = name };
            else m.shader = sh;
            setup?.Invoke(m);
            if (isNew) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
        }
    }
}
