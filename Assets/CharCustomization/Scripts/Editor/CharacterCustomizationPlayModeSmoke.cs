using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sol.CharacterCustomization.Editor
{
    public static class CharacterCustomizationPlayModeSmoke
    {
        private const string DemoScenePath = "Assets/CharCustomization/Scenes/DemoScene.unity";
        private const string PendingKey = "Sol.CharacterCustomization.PlayModeSmoke.Pending";
        private const string ExitCodeKey = "Sol.CharacterCustomization.PlayModeSmoke.ExitCode";

        [InitializeOnLoadMethod]
        private static void RestorePendingRun()
        {
            if (SessionState.GetBool(PendingKey, false))
            {
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            }
        }

        public static void RunFromCommandLine()
        {
            SessionState.SetBool(PendingKey, true);
            SessionState.SetInt(ExitCodeKey, 1);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.OpenScene(DemoScenePath);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.delayCall += ValidateRuntime;
            }
            else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(PendingKey, false))
            {
                int exitCode = SessionState.GetInt(ExitCodeKey, 1);
                SessionState.SetBool(PendingKey, false);
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EditorApplication.Exit(exitCode);
            }
        }

        private static void ValidateRuntime()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "SolCharacterCustomizationPlayMode",
                Guid.NewGuid().ToString("N"));
            try
            {
                CharacterMorphController controller = UnityEngine.Object.FindFirstObjectByType<CharacterMorphController>();
                CharacterProfile profile = UnityEngine.Object.FindFirstObjectByType<CharacterProfile>();
                CharacterMorphDemoUI menu = UnityEngine.Object.FindFirstObjectByType<CharacterMorphDemoUI>();
                CharacterPreviewControls preview = UnityEngine.Object.FindFirstObjectByType<CharacterPreviewControls>();
                CharacterFinalizationFlow flow = UnityEngine.Object.FindFirstObjectByType<CharacterFinalizationFlow>();
                Require(controller != null && profile != null && menu != null && preview != null && flow != null,
                    "The Play Mode demo is missing a required character customization component.");

                controller.SetSex(CharacterSex.Male);
                profile.RandomizeCurrent(0.65f, new System.Random(321));
                menu.RefreshPanel();
                Require(controller.ActiveSex == CharacterSex.Male, "Randomize changed the selected sex.");
                foreach (CharacterMorphDefinition definition in CharacterMorphCatalog.Definitions)
                {
                    float minimum = definition.MinimumValue < 0f ? definition.MinimumValue * 0.65f : 0f;
                    float value = controller.GetMorph(definition.Id);
                    Require(value >= minimum && value <= 0.65f,
                        $"Randomized morph '{definition.Id}' exceeded its restrained range.");
                }

                Require(profile.SetSkinTone("deep"), "The authored deep skin tone could not be selected.");
                Renderer skinRenderer = FindSkinRenderer(controller.ActiveCharacterRoot, "M_HumanMale");
                Require(skinRenderer != null, "The male skin renderer was not found.");
                var block = new MaterialPropertyBlock();
                skinRenderer.GetPropertyBlock(block);
                Color appliedColor = block.GetColor(Shader.PropertyToID("_BaseColor"));
                Require(ColorDistance(appliedColor, profile.CurrentSkinColor) < 0.001f,
                    "The selected skin colour was not applied through the renderer property block.");

                preview.SnapToFocus();
                Bounds bounds = CombineBounds(controller.ActiveCharacterRoot);
                Vector3 directionToFocus = (bounds.center - preview.PreviewCamera.transform.position).normalized;
                Require(Vector3.Dot(preview.PreviewCamera.transform.forward, directionToFocus) > 0.999f,
                    "The native preview camera is not focused on the active character bounds.");

                string savePath = Path.Combine(directory, "players.json");
                Directory.CreateDirectory(directory);
                SetPrivateField(flow, "savePathOverride", savePath);
                InvokePrivate(flow, "Awake");
                TMP_InputField nameInput = GameObject.Find("Character Name Input")?.GetComponent<TMP_InputField>();
                Require(nameInput != null, "The character name input was not found.");
                nameInput.SetTextWithoutNotify("Play Mode Player");
                bool finalized = false;
                flow.Finalized += _ => finalized = true;
                flow.FinalizeCharacter();
                Require(finalized && File.Exists(savePath), "Null-camera finalization did not persist and emit its event.");
                Require(flow.gameObject.activeInHierarchy,
                    "The customization UI should remain active when no gameplay camera is assigned.");

                var gameplayCameraObject = new GameObject("Smoke Gameplay Camera");
                Camera gameplayCamera = gameplayCameraObject.AddComponent<Camera>();
                gameplayCamera.transform.SetPositionAndRotation(
                    preview.PreviewCamera.transform.position + new Vector3(1f, 0.5f, 1f),
                    Quaternion.Euler(10f, 25f, 0f));
                gameplayCamera.fieldOfView = 48f;
                gameplayCamera.enabled = false;
                bool handoffCompleted = false;
                Require(preview.BlendTo(gameplayCamera, 0f, () => handoffCompleted = true),
                    "The preview camera rejected a valid gameplay-camera handoff.");
                Require(handoffCompleted && gameplayCamera.enabled && !preview.PreviewCamera.enabled,
                    "The native gameplay-camera handoff did not enable the target after disabling the preview.");
                UnityEngine.Object.Destroy(gameplayCameraObject);

                SessionState.SetInt(ExitCodeKey, 0);
                Debug.Log("Character customization Play Mode smoke validation passed.");
            }
            catch (Exception exception)
            {
                SessionState.SetInt(ExitCodeKey, 1);
                Debug.LogException(exception);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }

                EditorApplication.ExitPlaymode();
            }
        }

        private static Renderer FindSkinRenderer(Transform root, string materialName)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && material.name == materialName)
                    {
                        return renderer;
                    }
                }
            }

            return null;
        }

        private static Bounds CombineBounds(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
            Require(renderers.Length > 0, "The active character has no visible renderers.");
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static float ColorDistance(Color left, Color right)
        {
            return Mathf.Abs(left.r - right.r) + Mathf.Abs(left.g - right.g) +
                   Mathf.Abs(left.b - right.b) + Mathf.Abs(left.a - right.a);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);
        }

        private static void InvokePrivate(object target, string methodName)
        {
            target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(target, null);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
