using System;
using System.IO;
using System.Linq;
using MalbersAnimations.Controller;
using Sol.CharacterCustomization;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Sol.Locomotion.EditorTools
{
    [InitializeOnLoad]
    public static class LocomotionValidation
    {
        public const string PlayerPath = "Assets/Locomotion/Resources/Locomotion/HumanPlayer.prefab";
        public const string ControllerPath = "Assets/Locomotion/Animations/HumanRpg.controller";
        public const string TestScene = "Assets/Locomotion/Scenes/SC_HumanLocomotionTest.unity";
        const string Running = "Sol.Locomotion.Validation";
        static int phase, sexIndex;
        static float started, peak, walkDistance;
        static double deadline;
        static Vector3 position;
        static HumanLocomotionController driver;
        static Keyboard keyboard;
        static string failure;
        static Transform[] face;
        static Quaternion[] faceRotations;
        static float maxFaceDrift;
        static Key[] heldKeys = Array.Empty<Key>();
        static Transform outputCamera;
        static float previousCameraYaw, cameraYawTravel;
        static InputSettings previousInputSettings;

        static LocomotionValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Tick;
            Application.logMessageReceived += (message, stack, type) =>
            { if (SessionState.GetBool(Running, false) && (type == LogType.Exception || type == LogType.Error)) failure = message + "\n" + stack; };
            InputSystem.onBeforeUpdate += () =>
            {
                if (SessionState.GetBool(Running, false) && keyboard != null && InputState.currentUpdateType == InputUpdateType.Dynamic)
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(heldKeys));
            };
        }

        [MenuItem("Tools/Sol/Locomotion/Validate Assets and Optional Boundary")]
        public static void ValidateAssets()
        {
            ValidatePlayer();
            foreach (var path in AssetDatabase.FindAssets("", new[] { "Assets/CharCustomization" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (AssetDatabase.IsValidFolder(path) || path.EndsWith(".cs")) continue;
                var dependencies = AssetDatabase.GetDependencies(path, true);
                var leak = dependencies.FirstOrDefault(p => p.StartsWith("Assets/Locomotion/"));
                if (leak != null) throw new InvalidOperationException(path + " depends on optional locomotion: " + leak);
            }
            Debug.Log("LOCOMOTION_BOUNDARY_PASS: no customizer asset depends on Assets/Locomotion.");
        }

        static void ValidatePlayer()
        {
            var player = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                if (player.GetComponent<CharacterController>()) throw new InvalidOperationException("Legacy CharacterController remains.");
                var orbit = player.GetComponentInChildren<Unity.Cinemachine.CinemachineOrbitalFollow>(true);
                if (!orbit || orbit.TrackerSettings.BindingMode != Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace
                    || orbit.HorizontalAxis.Recentering.Enabled || orbit.VerticalAxis.Recentering.Enabled)
                    throw new InvalidOperationException("Gameplay orbit must use world space without automatic recentering.");
                if (orbit.GetComponent<Unity.Cinemachine.CinemachineInputAxisController>())
                    throw new InvalidOperationException("Duplicate camera input driver: HumanLocomotionController owns look input.");
                var motors = player.GetComponentsInChildren<MAnimal>(true);
                if (motors.Length != 2) throw new InvalidOperationException("Expected one Malbers motor per sex.");
                foreach (var motor in motors)
                {
                    if (motor.enabled || !motor.CloneStates || !motor.Anim || motor.Anim.runtimeAnimatorController != AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath)) throw new InvalidOperationException("Invalid motor configuration.");
                    if (motor.Anim.avatar.humanDescription.human.Any(b => b.humanName == "LeftEye" || b.humanName == "RightEye" || b.humanName == "Jaw")) throw new InvalidOperationException("Facial retargeting regression.");
                    if (motor.states.Count != 5 || motor.states.Any(s => !s || !new[] { 0, 1, 2, 3, 10 }.Contains(s.ID.ID)) || motor.states_C.Count != motor.states.Count || motor.states.Where((s,i) => motor.states_C[i].state != s).Any()) throw new InvalidOperationException("Missing or mismatched locomotion state cache.");
                    if (!motor.Stances.Any(s => s.ID && s.ID.ID == 7)) throw new InvalidOperationException("Missing crouch stance.");
                }
                foreach (var t in player.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0) throw new InvalidOperationException("Missing script on " + t.name);
                Debug.Log("HUMAN_LOCOMOTION_ASSET_CHECK_PASS: both avatars, four movement states plus test death, crouch stance, face fix and no missing scripts.");
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
        }

        [MenuItem("Tools/Sol/Locomotion/Run Runtime Checks %#F10")]
        public static void RunRuntime()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ValidateAssets();
            SessionState.SetString(Running + ".Scene", SceneManager.GetActiveScene().path);
            EditorSceneManager.OpenScene("Assets/CharCustomization/Scenes/SC_CustomizerDemo.unity");
            SessionState.SetBool(Running, true);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                var path = SessionState.GetString(Running + ".Scene", "");
                SessionState.EraseString(Running + ".Scene");
                if (!string.IsNullOrEmpty(path)) EditorSceneManager.OpenScene(path);
            }
            if (!SessionState.GetBool(Running, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            { phase = sexIndex = 0; failure = null; maxFaceDrift = 0; heldKeys = Array.Empty<Key>(); started = Time.time; deadline = EditorApplication.timeSinceStartup + 150; previousInputSettings = InputSystem.settings; InputSystem.settings = Object.Instantiate(previousInputSettings); InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView; keyboard = InputSystem.AddDevice<Keyboard>(); }
            else if (state == PlayModeStateChange.ExitingPlayMode) Cleanup();
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Running, false) || !EditorApplication.isPlaying || keyboard == null) return;
            try
            {
                if (failure != null) throw new Exception(failure);
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Locomotion runtime checks timed out.");
                float elapsed = Time.time - started;
                if (driver && face != null)
                    for (int i = 0; i < face.Length; i++) if (face[i]) maxFaceDrift = Mathf.Max(maxFaceDrift, Quaternion.Angle(face[i].localRotation, faceRotations[i]));
                switch (phase)
                {
                    case 0:
                        if (elapsed < .75f) break;
                        var flow = Object.FindFirstObjectByType<CharacterFinalizationFlow>();
                        Require(flow && flow.GameplayController is CustomizerLocomotionHandoff, "Optional provider did not register.");
                        var sex = sexIndex == 0 ? CharacterSex.Female : CharacterSex.Male;
                        flow.Profile.Controller.SetSex(sex);
                        flow.Profile.Controller.SetMorph("body.height", sexIndex == 0 ? .3f : -.2f);
                        flow.Profile.SetCustomSkinColor(new Color(.42f, .24f, .15f));
                        flow.SetPlayerSaveRepository(new CharacterPlayerSaveRepository(Path.GetFullPath("../work/locomotion-validation-" + Guid.NewGuid() + ".json")));
                        flow.CharacterNameInput.text = "Locomotion QA " + sex;
                        driver = Object.FindFirstObjectByType<HumanLocomotionController>(FindObjectsInactive.Include);
                        var settings = new SerializedObject(driver);
                        settings.FindProperty("lockCursorDuringGameplay").boolValue = false;
                        settings.ApplyModifiedPropertiesWithoutUndo();
                        flow.FinalizeCharacter();
                        Next();
                        break;
                    case 1:
                        if (elapsed < 1.5f) break;
                        Require(driver && driver.GameplayInputEnabled && driver.Motor.enabled, "Finalize did not enable locomotion.");
                        var profile = driver.GetComponentInChildren<CharacterProfile>(true);
                        Require(profile.Controller.ActiveSex == (sexIndex == 0 ? CharacterSex.Female : CharacterSex.Male), "Sex was not transferred.");
                        Require(Mathf.Abs(profile.Controller.GetMorph("body.height") - (sexIndex == 0 ? .3f : -.2f)) < .001f, "Proportion was not transferred.");
                        Require(profile.UsesCustomSkinColor, "Skin appearance was not transferred.");
                        Require(!driver.Animator.avatar.humanDescription.human.Any(b => b.humanName == "Jaw" || b.humanName.EndsWith("Eye")), "Face mapped after proportion rebuild.");
                        face = driver.Animator.GetBoneTransform(HumanBodyBones.Head).GetComponentsInChildren<Transform>(true).Skip(1).ToArray();
                        faceRotations = face.Select(t => t.localRotation).ToArray();
                        // Start off-axis: a target-relative orbit feeds the character's
                        // turn back into its camera-relative movement and spins forever.
                        driver.GetComponentInChildren<CinemachineOrbitalFollow>(true).HorizontalAxis.Value = 75;
                        outputCamera = driver.GetComponentInChildren<Camera>(true).transform;
                        cameraYawTravel = 0;
                        position = driver.Animator.transform.position;
                        Keys(Key.W); Next();
                        break;
                    case 2:
                        Keys(Key.W);
                        if (elapsed > .5f)
                            cameraYawTravel += Mathf.Abs(Mathf.DeltaAngle(previousCameraYaw, outputCamera.eulerAngles.y));
                        previousCameraYaw = outputCamera.eulerAngles.y;
                        if (elapsed < 2) break;
                        Require(cameraYawTravel < 15, $"Forward input rotated the camera without look input: {cameraYawTravel:F1} degrees.");
                        Debug.Log($"LOCOMOTION_CAMERA_PASS: forward input at 75-degree orbit; camera yaw travel={cameraYawTravel:F2}deg.");
                        Debug.Log("INPUT_DIAG asset=" + driver.MoveAction.action.actionMap.asset.name + " controls=" + string.Join(";", driver.MoveAction.action.controls.Select(c => c.path + "=" + c.ReadValueAsObject())) + " bindings=" + string.Join(";", driver.MoveAction.action.bindings.Select(b => b.effectivePath)) + " devices=" + driver.MoveAction.action.actionMap.asset.devices + " mask=" + driver.MoveAction.action.actionMap.asset.bindingMask);
                        walkDistance = Vector3.ProjectOnPlane(driver.Animator.transform.position - position, Vector3.up).magnitude;
                        Require(walkDistance > .5f, "Forward input did not move the character: " + walkDistance + $" input={driver.CurrentInput} action={driver.MoveAction.action.ReadValue<Vector2>()} enabled={driver.MoveAction.action.enabled} key={keyboard.wKey.isPressed} driver={driver.enabled} state={driver.Motor.ActiveStateID.ID} grounded={driver.Motor.Grounded} lock={driver.Motor.LockInput}/{driver.Motor.LockMovement} axis={driver.Motor.MovementAxis} smooth={driver.Motor.VerticalSmooth} time={driver.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime}");
                        position = driver.Animator.transform.position; Keys(Key.W, Key.LeftShift); Next();
                        break;
                    case 3:
                        Keys(Key.W, Key.LeftShift);
                        if (elapsed < 2) break;
                        float sprint = Vector3.ProjectOnPlane(driver.Animator.transform.position - position, Vector3.up).magnitude;
                        Require(sprint > walkDistance * 1.05f, "Sprint did not increase distance: " + sprint + " vs " + walkDistance);
                        position = driver.Animator.transform.position; peak = position.y; Keys(Key.Space); Next();
                        break;
                    case 4:
                        peak = Mathf.Max(peak, driver.Animator.transform.position.y);
                        Keys(elapsed < .25f ? new[] { Key.Space } : Array.Empty<Key>());
                        if (elapsed < 2) break;
                        Require(peak > position.y + .1f, "Jump did not leave the ground.");
                        Keys(Key.C); Next();
                        break;
                    case 5:
                        Keys();
                        if (elapsed < 1) break;
                        Require(driver.Motor.Stance.ID == 7 && !driver.Motor.LockInput && !driver.Motor.LockMovement, "Crouch stance did not activate cleanly.");
                        Require(maxFaceDrift < .1f, "Facial animation drift: " + maxFaceDrift);
                        Debug.Log($"LOCOMOTION_RUNTIME_PASS: {(sexIndex == 0 ? "Female" : "Male")}; finalized appearance/proportions; walk={walkDistance:F2}m; jump={peak-position.y:F2}m; sprint/crouch passed; face drift={maxFaceDrift:F4}deg.");
                        if (sexIndex++ == 0)
                        {
                            driver = null; face = null; phase = 0; maxFaceDrift = 0;
                            EditorSceneManager.LoadSceneInPlayMode("Assets/CharCustomization/Scenes/SC_CustomizerDemo.unity", new LoadSceneParameters(LoadSceneMode.Single));
                            started = Time.time;
                        }
                        else
                        {
                            driver = null; face = null; Keys(); phase = 6;
                            EditorSceneManager.LoadSceneInPlayMode(TestScene, new LoadSceneParameters(LoadSceneMode.Single));
                            started = Time.time;
                        }
                        break;
                    case 6:
                        if (elapsed < 1) break;
                        Require(Object.FindObjectsByType<HumanLocomotionController>(FindObjectsSortMode.None).Length == 1, "Test scene contains duplicate players.");
                        Require(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(l => l.enabled) == 1, "Test scene must have exactly one audio listener.");
                        driver = Object.FindFirstObjectByType<HumanLocomotionController>();
                        Require(driver && driver.GameplayInputEnabled, "Standalone test scene did not start its player.");
                        var standaloneSettings = new SerializedObject(driver);
                        standaloneSettings.FindProperty("lockCursorDuringGameplay").boolValue = false;
                        standaloneSettings.ApplyModifiedPropertiesWithoutUndo();
                        driver.GetComponentInChildren<CinemachineOrbitalFollow>(true).HorizontalAxis.Value = -75;
                        outputCamera = driver.GetComponentInChildren<Camera>(true).transform;
                        cameraYawTravel = 0;
                        position = driver.Animator.transform.position; Keys(Key.W); Next();
                        break;
                    case 7:
                        if (elapsed > .5f)
                            cameraYawTravel += Mathf.Abs(Mathf.DeltaAngle(previousCameraYaw, outputCamera.eulerAngles.y));
                        previousCameraYaw = outputCamera.eulerAngles.y;
                        if (elapsed < 1.5f) break;
                        Require(Vector3.Distance(position, driver.Animator.transform.position) > .5f, "Standalone test scene did not move.");
                        Require(cameraYawTravel < 15, $"Standalone camera spun at negative orbit: {cameraYawTravel:F1} degrees.");
                        Debug.Log($"LOCOMOTION_TEST_SCENE_PASS: player starts and moves at -75-degree orbit; yaw travel={cameraYawTravel:F2}deg.");
                        Keys(); phase = 9; started = Time.time;
                        break;
                    case 9:
                        // Allow the animation's authored braking, then check it settles.
                        if (elapsed < .75f) { position = driver.Animator.transform.position; break; }
                        if (elapsed < 1.25f) break;
                        float drift = Vector3.ProjectOnPlane(driver.Animator.transform.position - position, Vector3.up).magnitude;
                        Require(drift < .1f && !driver.IsMoving, $"Movement did not settle after release: {drift:F3}m drift.");
                        Require(driver.Motor.Grounded, "Character did not stay grounded after stopping.");
                        var oldPosition = driver.Animator.transform.position;
                        var oldCameraPosition = outputCamera.position;
                        driver.Respawn();
                        position = oldCameraPosition + driver.Animator.transform.position - oldPosition;
                        Next();
                        break;
                    case 10:
                        if (elapsed < .1f) break;
                        Require(Vector3.Distance(outputCamera.position, position) < .3f, "Camera dragged across the scene after respawn.");
                        Debug.Log("LOCOMOTION_STOP_RESPAWN_PASS: release settles, character stays grounded, camera follows teleport.");
                        driver = null; Keys();
                        EditorSceneManager.LoadSceneInPlayMode("Assets/CharCustomization/Scenes/SC_CustomizerDemo.unity", new LoadSceneParameters(LoadSceneMode.Single));
                        phase = 8; started = Time.time;
                        break;
                    case 8:
                        if (elapsed < .75f) break;
                        var editorOnly = Object.FindFirstObjectByType<CharacterFinalizationFlow>();
                        var optional = editorOnly.GameplayController;
                        editorOnly.RegisterGameplayHandoff(null, null);
                        Object.DestroyImmediate(optional.gameObject);
                        var repository = new CharacterPlayerSaveRepository(Path.GetFullPath("../work/locomotion-absent-" + Guid.NewGuid() + ".json"));
                        editorOnly.SetPlayerSaveRepository(repository);
                        editorOnly.CharacterNameInput.text = "Editor Only QA";
                        editorOnly.FinalizeCharacter();
                        Require(editorOnly.gameObject.activeInHierarchy && editorOnly.CharacterNameInput.interactable, "Finalizing without a provider hid or disabled the editor.");
                        Require(repository.TryFindByName("Editor Only QA", out var saved, out var error), "Editor-only save failed: " + error);
                        Debug.Log("CUSTOMIZER_WITHOUT_PROVIDER_PASS: character saved and editor remains interactive.");
                        Finish(0);
                        break;
                }
            }
            catch (Exception e) { Debug.LogException(e); Finish(1); }
        }

        static void Keys(params Key[] keys) => heldKeys = keys;
        static void Next() { phase++; started = Time.time; }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Finish(int code)
        {
            Cleanup();
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.ExitPlaymode();
        }

        static void Cleanup()
        {
            SessionState.SetBool(Running, false);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            keyboard = null;
            if (previousInputSettings)
            {
                var temporarySettings = InputSystem.settings;
                InputSystem.settings = previousInputSettings;
                Object.DestroyImmediate(temporarySettings);
                previousInputSettings = null;
            }
        }
    }
}
