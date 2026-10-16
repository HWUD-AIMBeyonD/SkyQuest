using System.Linq;
using PassthroughCameraSamples.CameraViewer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DroneHud.Editor
{
    /// <summary>
    /// Creates Assets/DroneHUD/DroneHUD.unity from the known-good CameraViewer scene
    /// (camera rig, passthrough, permissions, return-to-menu), swaps the CameraViewer UI
    /// for the drone HUD, and adds the scene to the build so it appears in the start menu.
    /// </summary>
    public static class DroneHudSceneBuilder
    {
        private const string SourceScenePath = "Assets/PassthroughCameraApiSamples/CameraViewer/CameraViewer.unity";
        private const string HudScenePath = "Assets/DroneHUD/DroneHUD.unity";

        [MenuItem("Drone HUD/Create HUD Scene")]
        private static void CreateHudScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(HudScenePath) != null &&
                !EditorUtility.DisplayDialog("Drone HUD", $"{HudScenePath} already exists. Recreate it?", "Recreate", "Cancel"))
            {
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            _ = AssetDatabase.DeleteAsset(HudScenePath);
            if (!AssetDatabase.CopyAsset(SourceScenePath, HudScenePath))
            {
                Debug.LogError($"[DroneHUD] Could not copy {SourceScenePath}");
                return;
            }

            var scene = EditorSceneManager.OpenScene(HudScenePath, OpenSceneMode.Single);

            foreach (var viewer in Object.FindObjectsByType<CameraViewerManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var root = PrefabUtility.GetOutermostPrefabInstanceRoot(viewer.gameObject) ?? viewer.gameObject;
                Object.DestroyImmediate(root);
            }

            var hud = new GameObject("DroneHUD");
            var source = hud.AddComponent<SimulatedTelemetrySource>();
            hud.AddComponent<DroneHudController>().Source = source;

            _ = EditorSceneManager.SaveScene(scene);

            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.All(s => s.path != HudScenePath))
            {
                scenes.Add(new EditorBuildSettingsScene(HudScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }

            Selection.activeGameObject = hud;
            Debug.Log($"[DroneHUD] Created {HudScenePath} and added it to Build Settings.");
        }
    }
}
