using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PanzerSim.EditorTools
{
    /// <summary>
    /// Menue "Panzer/GPU-Leistung einschalten": GPU Resident Drawer (+ GPU Occlusion Culling) und Forward+ in allen URP-Assets,
    /// dazu "BatchRendererGroup Variants: Keep All" in den Grafik-Einstellungen. Schreibt ins Console-Fenster, was geaendert wurde.
    /// </summary>
    public static class GpuPerformanceSetup
    {
        [MenuItem("Panzer/GPU-Leistung einschalten")]
        static void Enable()
        {
            var log = new System.Text.StringBuilder("GPU-Leistung:\n");
            var assets = new System.Collections.Generic.HashSet<RenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline != null) assets.Add(GraphicsSettings.defaultRenderPipeline);
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                var a = QualitySettings.GetRenderPipelineAssetAt(i);
                if (a != null) assets.Add(a);
            }

            foreach (var a in assets)
            {
                var so = new SerializedObject(a);
                SetEnum(so, "m_GPUResidentDrawerMode", 0, a.name, log);   // 0 = aus: unter DX11 mit vielen Baeumen Grafiktreiber-Absturz (Device Removed)
                SetBool(so, "m_GPUResidentDrawerEnableOcclusionCullingInCameras", false, a.name, log);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(a);

                var list = so.FindProperty("m_RendererDataList");
                if (list == null) { log.AppendLine($"  {a.name}: keine Renderer-Liste gefunden"); continue; }
                for (int r = 0; r < list.arraySize; r++)
                {
                    var rd = list.GetArrayElementAtIndex(r).objectReferenceValue;
                    if (rd == null) continue;
                    var rso = new SerializedObject(rd);
                    SetEnum(rso, "m_RenderingMode", 2, rd.name, log);         // 2 = Forward+
                    rso.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(rd);
                }
            }

            var gsObj = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (gsObj != null && gsObj.Length > 0)
            {
                var gso = new SerializedObject(gsObj[0]);
                var p = gso.FindProperty("m_BrgStripping");
                if (p != null) { p.intValue = 2; gso.ApplyModifiedPropertiesWithoutUndo(); log.AppendLine("  Grafik: BatchRendererGroup Variants = Keep All"); }
                else log.AppendLine("  Grafik: m_BrgStripping nicht gefunden - bitte in Project Settings > Graphics > Shader Stripping auf 'Keep All' stellen");
            }
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
        }

        static void SetEnum(SerializedObject so, string name, int value, string owner, System.Text.StringBuilder log)
        {
            var p = so.FindProperty(name);
            if (p == null) { log.AppendLine($"  {owner}: {name} nicht gefunden"); return; }
            if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = IndexOf(p, value); else p.intValue = value;
            log.AppendLine($"  {owner}: {name} = {(p.propertyType == SerializedPropertyType.Enum ? p.enumDisplayNames[p.enumValueIndex] : p.intValue.ToString())}");
        }

        // Enum-Index zum gespeicherten Zahlenwert finden (Reihenfolge der Namen muss nicht dem Wert entsprechen)
        static int IndexOf(SerializedProperty p, int value)
        {
            int old = p.intValue;
            p.intValue = value;
            int idx = p.enumValueIndex;
            if (idx < 0) { p.intValue = old; idx = p.enumValueIndex; }
            return idx;
        }

        static void SetBool(SerializedObject so, string name, bool value, string owner, System.Text.StringBuilder log)
        {
            var p = so.FindProperty(name);
            if (p == null) { log.AppendLine($"  {owner}: {name} nicht gefunden"); return; }
            p.boolValue = value;
            log.AppendLine($"  {owner}: {name} = {value}");
        }
    }
}
