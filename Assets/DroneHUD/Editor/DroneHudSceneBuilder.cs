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
    /// for the SkyQuest intro + drone HUD, and makes it the scene the app launches into.
    /// </summary>
    public static class DroneHudSceneBuilder
    {
        private const string SourceScenePath = "Assets/PassthroughCameraApiSamples/CameraViewer/CameraViewer.unity";
        private const string HudScenePath = "Assets/DroneHUD/DroneHUD.unity";
        private const string FontFolder = "Assets/PassthroughCameraApiSamples/StartScene/Fonts/Montserrat/";

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
            var controller = hud.AddComponent<DroneHudController>();
            controller.Source = source;

            var intro = new SerializedObject(hud.AddComponent<SkyQuestIntro>());
            intro.FindProperty("m_hud").objectReferenceValue = controller;
            intro.FindProperty("m_titleFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(FontFolder + "Montserrat-Bold.ttf");
            intro.FindProperty("m_bodyFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>(FontFolder + "Montserrat-Medium.ttf");
            _ = intro.ApplyModifiedPropertiesWithoutUndo();

            _ = EditorSceneManager.SaveScene(scene);

            // DroneHUD goes first so the app boots straight into the intro. The start menu
            // (and ☰ "return to start") then also lead here; drag StartScene back to the top
            // of Build Profiles > Scene List to get the sample menu back.
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != HudScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(HudScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Selection.activeGameObject = hud;
            Debug.Log($"[DroneHUD] Created {HudScenePath} and set it as the launch scene.");
        }
    }
}
