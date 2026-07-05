using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sol.CharacterCustomization.EditorTools
{
    public static class DemoPlayerCinemachineSetup
    {
        private const string DemoPlayerPrefabPath = "Assets/CharCustomization/Prefabs/DemoPlayer.prefab";
        private const string MoveActionPath = "Assets/CharCustomization/Scripts/Input/PlayerMoveActionReference.asset";
        private const string LookActionPath = "Assets/CharCustomization/Scripts/Input/PlayerLookActionReference.asset";
        private const string JumpActionPath = "Assets/CharCustomization/Scripts/Input/PlayerJumpActionReference.asset";
        private const string SprintActionPath = "Assets/CharCustomization/Scripts/Input/PlayerSprintActionReference.asset";

        [MenuItem("Tools/Sol/Character Customization/Configure DemoPlayer Cinemachine")]
        public static void ConfigureDemoPlayerPrefabFromMenu()
        {
            ConfigureDemoPlayerPrefab();
        }

        public static void ConfigureDemoPlayerPrefabFromCommandLine()
        {
            ConfigureDemoPlayerPrefab();
        }

        private static void ConfigureDemoPlayerPrefab()
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(DemoPlayerPrefabPath);
            try
            {
                Configure(prefabRoot);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, DemoPlayerPrefabPath);
                Debug.Log($"Configured Cinemachine 3 third-person camera on {DemoPlayerPrefabPath}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static void Configure(GameObject prefabRoot)
        {
            var controller = prefabRoot.GetComponent<DemoPlayerController>();
            var characterController = prefabRoot.GetComponent<CharacterController>();
            Camera outputCamera = FindGameplayOutputCamera(prefabRoot);
            if (controller == null || characterController == null || outputCamera == null)
            {
                Debug.LogError("DemoPlayer prefab is missing the gameplay controller, CharacterController, or camera.");
                return;
            }

            RemoveLegacyCinemachineComponents(prefabRoot);

            Transform gameplayRig = EnsureChild(prefabRoot.transform, "GameplayCameraRig");
            Transform yawRoot = EnsureChild(gameplayRig, "CameraYawRoot");
            Transform pitchRoot = EnsureChild(yawRoot, "CameraPitchRoot");
            Transform lookTarget = EnsureChild(gameplayRig, "CameraLookTarget");
            Transform cinemachineCameraTransform = EnsureChild(gameplayRig, "ThirdPersonGameplayCamera");

            outputCamera.gameObject.name = "PlayerCamera";
            outputCamera.transform.SetParent(gameplayRig, false);

            gameplayRig.localPosition = Vector3.zero;
            gameplayRig.localRotation = Quaternion.identity;
            yawRoot.localPosition = Vector3.zero;
            yawRoot.localRotation = Quaternion.identity;
            pitchRoot.localPosition = new Vector3(0f, 1.35f, 0f);
            pitchRoot.localRotation = Quaternion.identity;
            lookTarget.localPosition = new Vector3(0f, 1.45f, 0f);
            lookTarget.localRotation = Quaternion.identity;
            cinemachineCameraTransform.localPosition = new Vector3(0f, 2.95f, -4.5f);
            cinemachineCameraTransform.localRotation = Quaternion.identity;
            outputCamera.transform.localPosition = new Vector3(0f, 0f, -4.5f);
            outputCamera.transform.localRotation = Quaternion.identity;

            ConfigureBrain(outputCamera);
            ConfigureCinemachineCamera(cinemachineCameraTransform, lookTarget, outputCamera);
            ConfigureController(controller, characterController, outputCamera, yawRoot, pitchRoot);
        }

        private static Camera FindGameplayOutputCamera(GameObject prefabRoot)
        {
            Transform playerCamera = prefabRoot.transform.Find("GameplayCameraRig/PlayerCamera") ??
                                     prefabRoot.transform.Find("PlayerCamera");
            if (playerCamera != null && playerCamera.TryGetComponent(out Camera camera))
            {
                return camera;
            }

            foreach (Camera childCamera in prefabRoot.GetComponentsInChildren<Camera>(true))
            {
                if (childCamera.gameObject.name == "PlayerCamera")
                {
                    return childCamera;
                }
            }

            return prefabRoot.GetComponentInChildren<Camera>(true);
        }

        private static void ConfigureBrain(Camera outputCamera)
        {
            var brain = outputCamera.GetComponent<CinemachineBrain>();
            if (brain == null)
            {
                brain = outputCamera.gameObject.AddComponent<CinemachineBrain>();
            }

            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.35f);
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.SmartUpdate;
            brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;
            brain.ChannelMask = OutputChannels.Default;
        }

        private static void ConfigureCinemachineCamera(
            Transform owner,
            Transform lookTarget,
            Camera outputCamera)
        {
            var cinemachineCamera = EnsureComponent<CinemachineCamera>(owner.gameObject);
            cinemachineCamera.Follow = lookTarget;
            cinemachineCamera.LookAt = lookTarget;
            cinemachineCamera.Priority = 10;
            cinemachineCamera.OutputChannel = OutputChannels.Default;
            cinemachineCamera.Lens = LensSettings.FromCamera(outputCamera);
            cinemachineCamera.Lens.FieldOfView = 75f;

            RemoveComponent<CinemachineThirdPersonFollow>(owner.gameObject);

            var orbit = EnsureComponent<CinemachineOrbitalFollow>(owner.gameObject);
            orbit.TargetOffset = Vector3.zero;
            orbit.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            orbit.TrackerSettings.PositionDamping = Vector3.zero;
            orbit.TrackerSettings.RotationDamping = Vector3.zero;
            orbit.TrackerSettings.QuaternionDamping = 0f;
            orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            orbit.Radius = 4.5f;
            orbit.Orbits.Top.Radius = 2f;
            orbit.Orbits.Top.Height = 5f;
            orbit.Orbits.Center.Radius = 4f;
            orbit.Orbits.Center.Height = 2.25f;
            orbit.Orbits.Bottom.Radius = 2.5f;
            orbit.Orbits.Bottom.Height = 0.1f;
            orbit.Orbits.SplineCurvature = 0.5f;
            orbit.RecenteringTarget = CinemachineOrbitalFollow.ReferenceFrames.TrackingTarget;
            orbit.HorizontalAxis.Value = 0f;
            orbit.HorizontalAxis.Center = 0f;
            orbit.HorizontalAxis.Range = new Vector2(-180f, 180f);
            orbit.HorizontalAxis.Wrap = true;
            orbit.HorizontalAxis.Recentering.Enabled = false;
            orbit.VerticalAxis.Value = 10f;
            orbit.VerticalAxis.Center = 10f;
            orbit.VerticalAxis.Range = new Vector2(-10f, 75f);
            orbit.VerticalAxis.Wrap = false;
            orbit.VerticalAxis.Recentering.Enabled = false;
            orbit.RadialAxis.Value = 1f;
            orbit.RadialAxis.Center = 1f;
            orbit.RadialAxis.Range = Vector2.one;
            orbit.RadialAxis.Wrap = false;
            orbit.RadialAxis.Recentering.Enabled = false;

            var rotationComposer = EnsureComponent<CinemachineRotationComposer>(owner.gameObject);
            rotationComposer.TargetOffset = Vector3.zero;
            rotationComposer.Damping = Vector2.zero;
            rotationComposer.CenterOnActivate = true;
            rotationComposer.Composition.ScreenPosition = Vector2.zero;
            rotationComposer.Composition.DeadZone.Enabled = false;
            rotationComposer.Composition.HardLimits.Enabled = false;
        }

        private static void ConfigureController(
            DemoPlayerController controller,
            CharacterController characterController,
            Camera outputCamera,
            Transform yawRoot,
            Transform pitchRoot)
        {
            var serializedController = new SerializedObject(controller);
            SetObject(serializedController, "moveAction", AssetDatabase.LoadAssetAtPath<InputActionReference>(MoveActionPath));
            SetObject(serializedController, "lookAction", AssetDatabase.LoadAssetAtPath<InputActionReference>(LookActionPath));
            SetObject(serializedController, "jumpAction", AssetDatabase.LoadAssetAtPath<InputActionReference>(JumpActionPath));
            SetObject(serializedController, "sprintAction", AssetDatabase.LoadAssetAtPath<InputActionReference>(SprintActionPath));
            SetObject(serializedController, "cameraTransform", outputCamera.transform);
            SetObject(serializedController, "cameraYawRoot", yawRoot);
            SetObject(serializedController, "cameraPitchRoot", pitchRoot);
            SetObject(serializedController, "cameraOrbit", controller.GetComponentInChildren<CinemachineOrbitalFollow>(true));
            SetObject(serializedController, "characterController", characterController);
            SetFloat(serializedController, "moveSpeed", 4f);
            SetFloat(serializedController, "sprintSpeed", 6f);
            SetFloat(serializedController, "rotationSpeed", 720f);
            SetFloat(serializedController, "gravity", -20f);
            SetFloat(serializedController, "jumpHeight", 1.2f);
            serializedController.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RemoveLegacyCinemachineComponents(GameObject prefabRoot)
        {
            foreach (Component component in prefabRoot.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                {
                    continue;
                }

                string typeName = component.GetType().Name;
                if (typeName is "CinemachineVirtualCamera" or "CinemachineTransposer" or "CinemachineComposer" or "CinemachinePipeline")
                {
                    Object.DestroyImmediate(component, true);
                }
            }

            RemoveEmptyLegacyPipelineChildren(prefabRoot.transform);
        }

        private static void RemoveEmptyLegacyPipelineChildren(Transform root)
        {
            for (int index = root.childCount - 1; index >= 0; index--)
            {
                Transform child = root.GetChild(index);
                RemoveEmptyLegacyPipelineChildren(child);
                if (child.name == "cm" && child.childCount == 0 && child.GetComponents<Component>().Length == 1)
                {
                    Object.DestroyImmediate(child.gameObject, true);
                }
            }
        }

        private static Transform EnsureChild(Transform parent, string childName)
        {
            Transform existing = parent.Find(childName);
            if (existing != null)
            {
                return existing;
            }

            var child = new GameObject(childName);
            child.layer = parent.gameObject.layer;
            Transform childTransform = child.transform;
            childTransform.SetParent(parent, false);
            childTransform.localPosition = Vector3.zero;
            childTransform.localRotation = Quaternion.identity;
            childTransform.localScale = Vector3.one;
            return childTransform;
        }

        private static T EnsureComponent<T>(GameObject owner) where T : Component
        {
            T component = owner.GetComponent<T>();
            return component != null ? component : owner.AddComponent<T>();
        }

        private static void RemoveComponent<T>(GameObject owner) where T : Component
        {
            T component = owner.GetComponent<T>();
            if (component != null)
            {
                Object.DestroyImmediate(component, true);
            }
        }

        private static void SetObject(SerializedObject serializedObject, string propertyPath, Object value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property != null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetFloat(SerializedObject serializedObject, string propertyPath, float value)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property != null)
            {
                property.floatValue = value;
            }
        }
    }
}
