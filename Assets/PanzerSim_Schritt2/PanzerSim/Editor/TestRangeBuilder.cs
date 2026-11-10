using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PanzerSim.EditorTools
{
    /// <summary>Baut per Menue "Panzer > Testgelaende erstellen" die komplette Testszene.</summary>
    public static class TestRangeBuilder
    {
        [MenuItem("Panzer/Testgelaende erstellen")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Boden: 500 m breit, 2,5 km lang
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Boden";
            ground.transform.position = new Vector3(0f, 0f, 1200f);
            ground.transform.localScale = new Vector3(50f, 1f, 260f);

            // Kamera = Schuetze
            var cam = Camera.main;
            cam.transform.position = new Vector3(0f, 1.8f, 0f);
            cam.transform.rotation = Quaternion.identity;
            cam.farClipPlane = 5000f;
            cam.gameObject.AddComponent<PanzerSim.TestRangePlayer>();

            // 100 m: Raster aus Dicke x Winkel
            float[] thick = { 50f, 80f, 100f, 120f };
            float[] angles = { 0f, 30f, 45f, 60f };
            for (int i = 0; i < thick.Length; i++)
                for (int j = 0; j < angles.Length; j++)
                {
                    float x = (j - 1.5f) * 4f;
                    float y = 1.2f + i * 2.6f;
                    Plate($"{thick[i]:0} mm / {angles[j]:0}°", thick[i], angles[j], new Vector3(x, y, 100f), ArmorMaterial.RHA);
                }

            // 100 m rechts: Materialvergleich und Schottpanzerung
            Plate("80 mm Gussstahl / 0°", 80f, 0f, new Vector3(14f, 1.2f, 100f), ArmorMaterial.Cast);
            Plate("80 mm oberflaechengehaertet / 0°", 80f, 0f, new Vector3(14f, 3.8f, 100f), ArmorMaterial.FaceHardened);
            Plate("Schuerze 8 mm", 8f, 0f, new Vector3(20f, 1.2f, 99.4f), ArmorMaterial.RHA);
            Plate("hinter Schuerze 80 mm", 80f, 0f, new Vector3(20f, 1.2f, 100f), ArmorMaterial.RHA);

            // 500 m und 1000 m: je eine Reihe
            foreach (float dist in new[] { 500f, 1000f })
                for (int i = 0; i < thick.Length; i++)
                    Plate($"{thick[i]:0} mm / 30° ({dist:0} m)", thick[i], 30f,
                          new Vector3((i - 1.5f) * 5f, 1.5f, dist), ArmorMaterial.RHA);

            // Speichern
            string folder = "Assets/PanzerSim";
            Directory.CreateDirectory(folder);
            EditorSceneManager.SaveScene(scene, folder + "/Testgelaende.unity");
            AssetDatabase.Refresh();
            Debug.Log("Testgelaende erstellt. Druecke Play!");
        }

        static void Plate(string label, float mm, float angleDeg, Vector3 pos, ArmorMaterial mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = label;
            go.transform.position = pos;
            // Oberkante kippt vom Schuetzen weg -> schraege Panzerung
            go.transform.rotation = Quaternion.Euler(angleDeg, 0f, 0f);
            go.transform.localScale = new Vector3(3f, 2.2f, mm / 1000f);
            var p = go.AddComponent<PanzerSim.ArmorPlate>();
            p.thicknessMm = mm;
            p.material = mat;
            p.label = label;
        }
    }
}
