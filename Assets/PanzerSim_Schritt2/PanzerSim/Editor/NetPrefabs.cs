using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace PanzerSim.EditorTools
{
    /// <summary>
    /// Menue "Panzer/Netzwerk-Prefabs einrichten": legt Assets/Resources/Netz/NetTank.prefab und NetSession.prefab an
    /// (Netcode braucht fuer Netzwerk-Objekte echte Prefabs mit fester Kennung). Laeuft vor jedem Build automatisch.
    /// </summary>
    public static class NetPrefabs
    {
        const string Dir = "Assets/Resources/Netz";

        [MenuItem("Panzer/Netzwerk-Prefabs einrichten")]
        public static void Ensure()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Resources", "Netz");
            Make<NetTank>("NetTank");
            Make<NetSession>("NetSession");
            AssetDatabase.SaveAssets();
        }

        static void Make<T>(string name) where T : Component
        {
            string path = $"{Dir}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && existing.GetComponent<NetworkObject>() != null && existing.GetComponent<T>() != null) return;
            var go = new GameObject(name);
            go.AddComponent<NetworkObject>();
            go.AddComponent<T>();
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            Debug.Log("Netzwerk-Prefab angelegt: " + path);
        }
    }
}
