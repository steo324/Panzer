using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PanzerSim.EditorTools
{
    /// <summary>
    /// Traegt die Grafik-Erweiterungen in den PC_Renderer ein (einmalig, wird vor jedem Build geprueft):
    ///  - HeightFogFeature (Hoehennebel/Dunst)
    ///  (ein frueher eingetragenes Decal-Feature wird wieder entfernt)

    /// </summary>
    public static class GraphicsSetup
    {
        const string RendererPath = "Assets/Settings/PC_Renderer.asset";

        [MenuItem("PanzerSim/Grafik-Erweiterungen einrichten")]
        public static void Ensure()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (data == null) { Debug.LogWarning("GraphicsSetup: " + RendererPath + " fehlt"); return; }
            bool changed = false;
            if (!data.rendererFeatures.Exists(f => f is HeightFogFeature))
            {
                AddFeature(data, ScriptableObject.CreateInstance<HeightFogFeature>(), "Hoehennebel");
                changed = true;
            }
            // Decal-Feature (war fuer eine Staubschicht gedacht, auf den Modell-Materialien nicht zuverlaessig): wieder entfernen, kostet sonst Rechenzeit
            int di = data.rendererFeatures.FindIndex(f => f is DecalRendererFeature);
            if (di >= 0)
            {
                var decal = data.rendererFeatures[di];
                data.rendererFeatures.RemoveAt(di);
                var map = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap", BindingFlags.NonPublic | BindingFlags.Instance);
                if (map?.GetValue(data) is List<long> list && di < list.Count) list.RemoveAt(di);
                AssetDatabase.RemoveObjectFromAsset(decal);
                Object.DestroyImmediate(decal, true);
                data.SetDirty();
                changed = true;
            }
            if (changed) { EditorUtility.SetDirty(data); AssetDatabase.SaveAssets(); Debug.Log("GraphicsSetup: Renderer-Features aktualisiert"); }
        }

        static void AddFeature(UniversalRendererData data, ScriptableRendererFeature f, string name)
        {
            f.name = name;
            AssetDatabase.AddObjectToAsset(f, data);
            data.rendererFeatures.Add(f);
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out string _, out long localId))
            {
                var map = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap", BindingFlags.NonPublic | BindingFlags.Instance);
                if (map?.GetValue(data) is List<long> list) list.Add(localId);
            }
            data.SetDirty();
        }

    }
}
