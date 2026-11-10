using System.Collections.Generic;
using UnityEngine;

namespace PanzerSim
{
    /// <summary>
    /// Baeume aus Modelle/Pflanzen.glb (Eiche, Linde, Birke, Kiefer, Fichte):
    /// nah das volle Modell, weit weg zwei gekreuzte Bilder (LOD), Stamm mit Collider (haelt Granaten und Panzer auf).
    /// Alle Baeume wiegen im Wind und biegen sich bei Explosionen (Shader "PanzerSim/Baum").
    /// </summary>
    public static class Trees
    {
        public enum Kind { Eiche, Linde, Birke, Kiefer, Fichte }
        static readonly float[] TrunkRadius = { 0.42f, 0.38f, 0.2f, 0.3f, 0.33f };
        static readonly float[] TrunkHeight = { 6f, 6.5f, 7f, 13f, 12f };

        class Species { public Mesh mesh, far; public Material[] mats; public Material farMat; }
        static Species[] species;
        static bool tried;

        public static bool Enabled = false;   // vorerst aus (Absturz untersuchen), dann wieder einschalten
        public static bool Available => Enabled && GameSettings.DetailTrees && Load();

        static bool Load()
        {
            if (species != null) return true;
            if (tried) return false;
            tried = true;
            var prefab = Resources.Load<GameObject>("Modelle/Pflanzen");
            var sh = Shader.Find("PanzerSim/Baum");
            if (sh == null) sh = Resources.Load<Shader>("Shaders/Baum");
            if (prefab == null || sh == null) { Debug.LogWarning("Baeume: Modelle/Pflanzen.glb oder Shader fehlt - einfache Baeume."); return false; }
            Vegetation.EnsureInit();
            var names = System.Enum.GetNames(typeof(Kind));
            species = new Species[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var t = Find(prefab.transform, names[i]);
                var tf = Find(prefab.transform, names[i] + "_Fern");
                if (t == null || tf == null) { Debug.LogWarning("Baeume: " + names[i] + " fehlt im Modell."); species = null; return false; }
                var sp = new Species();
                sp.mesh = t.GetComponent<MeshFilter>().sharedMesh;
                var src = t.GetComponent<MeshRenderer>().sharedMaterials;
                sp.mats = new Material[src.Length];
                for (int m = 0; m < src.Length; m++)
                {
                    bool foliage = m > 0;
                    var mat = new Material(sh);
                    mat.SetTexture("_BaseMap", Tex(src[m]));
                    mat.SetFloat("_Cutoff", foliage ? 0.5f : 0f);
                    mat.SetFloat("_Cull", foliage ? 0f : 2f);
                    mat.SetFloat("_Flutter", foliage ? 1f : 0f);
                    mat.enableInstancing = true;
                    sp.mats[m] = mat;
                }
                sp.far = tf.GetComponent<MeshFilter>().sharedMesh;
                sp.farMat = new Material(sh);
                sp.farMat.SetTexture("_BaseMap", Tex(tf.GetComponent<MeshRenderer>().sharedMaterial));
                sp.farMat.SetFloat("_Cutoff", 0.5f); sp.farMat.SetFloat("_Cull", 0f); sp.farMat.SetFloat("_Flutter", 0f);
                sp.farMat.enableInstancing = true;
                species[i] = sp;
            }
            return true;
        }

        static Texture Tex(Material m)
        {
            foreach (var p in new[] { "baseColorTexture", "_BaseMap", "_MainTex" })
                if (m != null && m.HasProperty(p) && m.GetTexture(p) != null) return m.GetTexture(p);
            return Texture2D.whiteTexture;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = Find(c, name); if (r != null) return r; }
            return null;
        }

        /// <summary>Baum setzen. scale ~0.75..1.25 (Modelle: Eiche 17 m, Linde 19 m, Birke 14 m, Kiefer 21 m, Fichte 22 m).</summary>
        public static GameObject Spawn(Transform parent, Kind kind, Vector3 groundPos, float yaw, float scale)
        {
            if (!Load()) return null;
            var sp = species[(int)kind];
            var go = new GameObject(kind.ToString());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(groundPos - Vector3.up * 0.15f, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;

            var near = new GameObject("Nah");
            near.transform.SetParent(go.transform, false);
            near.AddComponent<MeshFilter>().sharedMesh = sp.mesh;
            var r0 = near.AddComponent<MeshRenderer>();
            r0.sharedMaterials = sp.mats;

            var far = new GameObject("Fern");
            far.transform.SetParent(go.transform, false);
            far.AddComponent<MeshFilter>().sharedMesh = sp.far;
            var r1 = far.AddComponent<MeshRenderer>();
            r1.sharedMaterial = sp.farMat;
            r1.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var lod = go.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.11f, new Renderer[] { r0 }), new LOD(0.004f, new Renderer[] { r1 }) });
            lod.fadeMode = LODFadeMode.None;
            lod.RecalculateBounds();

            // Stamm: haelt Granaten und Panzer auf
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = TrunkRadius[(int)kind];
            col.height = TrunkHeight[(int)kind];
            col.center = new Vector3(0f, col.height / 2f, 0f);
            return go;
        }

        /// <summary>Zufaellige Art fuer einen Wald / eine Allee.</summary>
        public static Kind ForestKind(System.Random r)
        {
            double v = r.NextDouble();
            return v < 0.4 ? Kind.Fichte : v < 0.6 ? Kind.Kiefer : v < 0.8 ? Kind.Eiche : Kind.Birke;
        }
        public static Kind AlleyKind(System.Random r)
        {
            double v = r.NextDouble();
            return v < 0.6 ? Kind.Linde : v < 0.85 ? Kind.Eiche : Kind.Birke;
        }
    }
}
