using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PanzerSim.EditorTools
{
    /// <summary>Menue "Panzer > Panzer-Testgelaende erstellen": Gelaende + Start-Objekt. Panzer entstehen beim Druecken von Play.</summary>
    public static class TankRangeBuilder
    {
        [MenuItem("Panzer/Panzer-Testgelaende erstellen")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Boden: 3 x 3 km
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Boden";
            ground.transform.position = new Vector3(0f, 0f, 500f);
            ground.transform.localScale = new Vector3(300f, 1f, 300f);

            // Rampen und Huegel zum Fahren
            Block("Rampe", new Vector3(-25f, 0.9f, 40f), new Vector3(10f, 1f, 22f), new Vector3(-9f, 0f, 0f));
            Block("Rampe steil", new Vector3(-45f, 1.2f, 40f), new Vector3(10f, 1f, 18f), new Vector3(-20f, 0f, 0f));
            Block("Rampe runter", new Vector3(-25f, 0.9f, 62f), new Vector3(10f, 1f, 22f), new Vector3(9f, 0f, 0f));
            Block("Mauer", new Vector3(10f, 1.5f, 130f), new Vector3(14f, 3f, 1f), Vector3.zero);
            Block("Graben-Rand", new Vector3(-10f, 0.4f, 160f), new Vector3(30f, 0.8f, 3f), Vector3.zero);
            for (int i = 0; i < 6; i++)
                Block("Kiste", new Vector3(-30f + i * 4f, 0.75f, 110f + (i % 2) * 3f), Vector3.one * 1.5f, new Vector3(0f, i * 17f, 0f));

            var cam = Camera.main;
            cam.transform.position = new Vector3(0f, 4f, -10f);
            cam.farClipPlane = 5000f;
            cam.gameObject.AddComponent<PanzerSim.TankCamera>();

            new GameObject("Start (Panzer werden hier erzeugt)").AddComponent<PanzerSim.TankRangeSetup>();

            string folder = "Assets/PanzerSim";
            Directory.CreateDirectory(folder);
            EditorSceneManager.SaveScene(scene, folder + "/PanzerTestgelaende.unity");
            AssetDatabase.Refresh();
            Debug.Log("Panzer-Testgelaende erstellt. Druecke Play!");
        }

        static void Block(string name, Vector3 pos, Vector3 scale, Vector3 euler)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.transform.rotation = Quaternion.Euler(euler);
        }
    }
}
