using System;
using System.IO;
using SnakeCatcher.Interaction;
using SnakeCatcher.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SnakeCatcher.Editor
{
    /// <summary>Adds the detector to the existing player without rebuilding the playground.</summary>
    public static class PlayerInteractionSetup
    {
        [MenuItem("Snake Catcher/Setup/Add Interaction Detection")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before configuring interaction detection.");

            if (!File.Exists(PlayerMovementSetup.ScenePath))
                PlayerMovementSetup.CreatePlayground();

            if (EditorSceneManager.GetActiveScene().path != PlayerMovementSetup.ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                EditorSceneManager.OpenScene(PlayerMovementSetup.ScenePath);
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PlayerMovementSetup.PrefabPath);
            try
            {
                WirePlayer(prefabRoot);
                if (PrefabUtility.SaveAsPrefabAsset(prefabRoot, PlayerMovementSetup.PrefabPath) == null)
                    throw new InvalidOperationException("Could not save the player interaction component.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            AssetDatabase.ImportAsset(PlayerMovementSetup.PrefabPath);
            FirstPersonPlayerController[] players = UnityEngine.Object.FindObjectsByType<FirstPersonPlayerController>(FindObjectsSortMode.None);
            foreach (FirstPersonPlayerController player in players)
                WirePlayer(player.gameObject);

            const string markerName = "Interaction Practice Marker";
            if (GameObject.Find(markerName) == null)
            {
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = markerName;
                marker.transform.position = new Vector3(0f, 1.25f, -5.8f);
                marker.transform.localScale = Vector3.one;
                Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_SnakeCatcher/Art/Materials/PlaygroundObstacle.mat");
                if (material != null)
                    marker.GetComponent<Renderer>().sharedMaterial = material;
                marker.AddComponent<InteractionPracticeTarget>();

                GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pedestal.name = "Interaction Marker Pedestal";
                pedestal.transform.position = new Vector3(0f, 0.375f, -5.8f);
                pedestal.transform.localScale = new Vector3(1.2f, 0.75f, 1.2f);
                Material pedestalMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_SnakeCatcher/Art/Materials/PlaygroundWall.mat");
                if (pedestalMaterial != null)
                    pedestal.GetComponent<Renderer>().sharedMaterial = pedestalMaterial;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            if (!EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()))
                throw new InvalidOperationException("Could not save interaction detection in the playground.");
            AssetDatabase.SaveAssets();
            Debug.Log("Interaction detection is ready. Look at the practice marker and hold E to change its color.");
        }

        private static void WirePlayer(GameObject player)
        {
            Camera camera = player.GetComponentInChildren<Camera>(true);
            FirstPersonPlayerController controller = player.GetComponent<FirstPersonPlayerController>();
            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            if (camera == null || controller == null || actions == null)
                throw new InvalidOperationException("Player needs its camera, controller and input actions before adding interaction detection.");

            PlayerInteractor interactor = player.GetComponent<PlayerInteractor>();
            if (interactor == null)
                interactor = player.AddComponent<PlayerInteractor>();

            SerializedObject serialized = new SerializedObject(interactor);
            serialized.FindProperty("viewTransform").objectReferenceValue = camera.transform;
            serialized.FindProperty("inputActions").objectReferenceValue = actions;
            serialized.FindProperty("playerController").objectReferenceValue = controller;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(interactor);
        }
    }
}
