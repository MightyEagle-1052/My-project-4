using System;
using System.IO;
using SnakeCatcher.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SnakeCatcher.Editor
{
    /// <summary>Builds the reusable player and a small, separate collision playground.</summary>
    public static class PlayerMovementSetup
    {
        public const string ScenePath = "Assets/_SnakeCatcher/Scenes/MovementPlayground.unity";
        public const string PrefabPath = "Assets/_SnakeCatcher/Prefabs/Player/Player.prefab";
        private const string Root = "Assets/_SnakeCatcher";

        [MenuItem("Snake Catcher/Setup/Create Movement Playground")]
        public static void CreatePlayground()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before creating the movement playground.");

            // Reopening is safe: the setup command never overwrites an edited level.
            if (File.Exists(ScenePath))
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    EditorSceneManager.OpenScene(ScenePath);
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolder(Root + "/Scenes");
            EnsureFolder(Root + "/Prefabs/Player");
            EnsureFolder(Root + "/Art/Materials");

            InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            if (actions == null)
                throw new InvalidOperationException("The project's InputSystem_Actions asset is missing.");

            Material floor = MaterialAsset("PlaygroundFloor", new Color(0.16f, 0.27f, 0.25f));
            Material wall = MaterialAsset("PlaygroundWall", new Color(0.75f, 0.79f, 0.67f));
            Material obstacle = MaterialAsset("PlaygroundObstacle", new Color(0.91f, 0.45f, 0.18f));
            Material marking = MaterialAsset("PlaygroundMarking", new Color(0.43f, 0.63f, 0.50f));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "MovementPlayground";

            GameObject environment = new GameObject("Environment");
            Box("Ground", new Vector3(0f, -0.25f, 0f), new Vector3(30f, 0.5f, 30f), floor, environment.transform);
            Box("Back Wall", new Vector3(0f, 1.5f, -15f), new Vector3(30f, 3f, 0.5f), wall, environment.transform);
            Box("Front Wall", new Vector3(0f, 1.5f, 15f), new Vector3(30f, 3f, 0.5f), wall, environment.transform);
            Box("Left Wall", new Vector3(-15f, 1.5f, 0f), new Vector3(0.5f, 3f, 30f), wall, environment.transform);
            Box("Right Wall", new Vector3(15f, 1.5f, 0f), new Vector3(0.5f, 3f, 30f), wall, environment.transform);

            Box("Collision Block", new Vector3(0f, 0.75f, 1f), new Vector3(2f, 1.5f, 2f), obstacle, environment.transform);
            for (int i = 0; i < 4; i++)
            {
                float height = 0.2f * (i + 1);
                Box("Step " + (i + 1), new Vector3(5f, height * 0.5f, -2f + i * 0.8f),
                    new Vector3(3f, height, 0.8f), obstacle, environment.transform);
            }

            GameObject ramp = Box("Walkable Ramp", new Vector3(-6f, 0.9f, 0f), new Vector3(3f, 0.3f, 4f), obstacle, environment.transform);
            ramp.transform.rotation = Quaternion.Euler(-25f, 0f, 0f);
            Box("Low Ceiling", new Vector3(0f, 2.35f, 7f), new Vector3(4f, 0.3f, 4f), wall, environment.transform);
            Box("Ceiling Support Left", new Vector3(-1.85f, 1.1f, 7f), new Vector3(0.3f, 2.2f, 4f), wall, environment.transform);
            Box("Ceiling Support Right", new Vector3(1.85f, 1.1f, 7f), new Vector3(0.3f, 2.2f, 4f), wall, environment.transform);

            for (int i = -2; i <= 2; i++)
            {
                GameObject stripeX = Box("Floor Grid X " + i, new Vector3(i * 5f, 0.003f, 0f), new Vector3(0.035f, 0.005f, 29f), marking, environment.transform);
                GameObject stripeZ = Box("Floor Grid Z " + i, new Vector3(0f, 0.003f, i * 5f), new Vector3(29f, 0.005f, 0.035f), marking, environment.transform);
                UnityEngine.Object.DestroyImmediate(stripeX.GetComponent<Collider>());
                UnityEngine.Object.DestroyImmediate(stripeZ.GetComponent<Collider>());
            }

            GameObject sunObject = new GameObject("Sun", typeof(Light));
            sunObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            Light sun = sunObject.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.5f;
            sun.color = new Color(1f, 0.94f, 0.83f);
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.51f, 0.57f);

            GameObject volumeObject = new GameObject("Global Volume", typeof(Volume));
            Volume volume = volumeObject.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                GameObject player = CreatePlayer(actions);
                prefab = PrefabUtility.SaveAsPrefabAsset(player, PrefabPath);
                UnityEngine.Object.DestroyImmediate(player);
                if (prefab == null)
                    throw new InvalidOperationException("Could not save the player prefab at " + PrefabPath);
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "Player";
            instance.transform.position = new Vector3(0f, 0.05f, -8f);
            instance.transform.rotation = Quaternion.identity;

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Could not save the movement playground at " + ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = instance;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Frame(new Bounds(new Vector3(0f, 1f, 0f), new Vector3(24f, 12f, 24f)), false);

            Debug.Log("Snake Catcher movement playground is ready. Press Play, click Game view, then use WASD, mouse, Shift and Space. Escape releases the cursor.");
        }

        private static GameObject CreatePlayer(InputActionAsset actions)
        {
            GameObject player = new GameObject("Player");
            player.tag = "Player";
            CharacterController body = player.AddComponent<CharacterController>();
            body.height = 1.8f;
            body.radius = 0.3f;
            body.center = new Vector3(0f, 0.9f, 0f);
            body.stepOffset = 0.3f;
            body.slopeLimit = 45f;
            body.skinWidth = 0.03f;
            body.minMoveDistance = 0f;

            GameObject cameraObject = new GameObject("Player Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(player.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.fieldOfView = 75f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 150f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.38f, 0.63f, 0.78f);
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;

            FirstPersonPlayerController controller = player.AddComponent<FirstPersonPlayerController>();
            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("inputActions").objectReferenceValue = actions;
            serialized.FindProperty("viewTransform").objectReferenceValue = cameraObject.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return player;
        }

        private static Material MaterialAsset(string name, Color color)
        {
            string path = Root + "/Art/Materials/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null || pipeline.defaultMaterial == null)
                throw new InvalidOperationException("Select a URP quality profile before creating the playground.");

            Material material = new Material(pipeline.defaultMaterial) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.2f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static GameObject Box(string name, Vector3 position, Vector3 scale, Material material, Transform parent)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.position = position;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            return box;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
