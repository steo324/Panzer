using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PanzerSim.EditorTools
{
    /// <summary>Menue "Panzer > Spiel mit Menue erstellen": Szene mit Hauptmenue, Karten werden beim Start gebaut.</summary>
    public static class GameSceneBuilder
    {
        const string ScenePath = "Assets/PanzerSim/Spiel.unity";

        [MenuItem("Panzer/Spiel mit Menue erstellen")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = Camera.main;
            cam.farClipPlane = 5000f;
            cam.gameObject.AddComponent<PanzerSim.TankCamera>();

            var sun = Object.FindFirstObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
                sun.shadows = LightShadows.Soft;
            }

            new GameObject("Spiel (Menue + Karten)").AddComponent<PanzerSim.GameManager>();

            Directory.CreateDirectory("Assets/PanzerSim");
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Als erste Szene in die Build-Liste (fuer die spaetere .exe)
            var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes) if (s.path != ScenePath) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();

            AssetDatabase.Refresh();
            Debug.Log("Spiel-Szene erstellt. Druecke Play!");
        }
    }
}
