using System;
using System.Collections.Generic;
using Sol.CharacterCustomization;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace Sol.Locomotion.EditorTools
{
    // Isolated, disposable play-mode scene. No changes to the creator or its saved scenes.
    [InitializeOnLoad]
    public static class LocomotionFeelValidation
    {
        const string Running = "Sol.Locomotion.FeelValidation";
        static HumanLocomotionController driver;
        static CharacterMorphController morph;
        static Gamepad pad;
        static InputSettings previousSettings;
        static GamepadState input;
        static int phase, lastFrame, fps;
        static float started, distance, fullSpeed, minTurnForward, maxTurnStep, peak, baseY;
        static float previousCaptureDelta, oldHeight;
        static Vector3 previousPosition, swapPosition;
        static Quaternion previousRotation, swapRotation;
        static double deadline;
        static int jumpEntries, lastState;
        static readonly List<string> failures = new();

        static LocomotionFeelValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Tick;
            InputSystem.onBeforeUpdate += () =>
            {
                if (SessionState.GetBool(Running, false) && pad != null && InputState.currentUpdateType == InputUpdateType.Dynamic)
                    InputSystem.QueueStateEvent(pad, input);
            };
        }

        [MenuItem("Tools/Sol/Locomotion/Run Movement Feel Checks %#F8")]
        public static void Run() => RunAtFrameRate(60);

        [MenuItem("Tools/Sol/Locomotion/Run Movement Feel Checks at 30 Hz %#F7")]
        public static void Run30() => RunAtFrameRate(30);

        [MenuItem("Tools/Sol/Locomotion/Run Movement Feel Checks at 120 Hz %#F9")]
        public static void Run120() => RunAtFrameRate(120);

        static void RunAtFrameRate(int frameRate)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SessionState.SetString(Running + ".Scene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
            SessionState.SetInt(Running + ".Fps", frameRate);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Validation Ground";
            ground.transform.position = new Vector3(0, -.5f, 0);
            ground.transform.localScale = new Vector3(200, 1, 200);
            var light = new GameObject("Validation Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(45, 30, 0);
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(LocomotionValidation.PlayerPath), scene);
            var settings = new SerializedObject(player.GetComponent<HumanLocomotionController>());
            settings.FindProperty("startInGameplay").boolValue = true;
            settings.FindProperty("lockCursorDuringGameplay").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            player.GetComponent<HumanLocomotionController>().enabled = true;
            foreach (var camera in player.GetComponentsInChildren<Camera>(true)) camera.enabled = true;
            player.GetComponentInChildren<AudioListener>(true).enabled = true;
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
            {
                fps = SessionState.GetInt(Running + ".Fps", 60);
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-movementFps") fps = int.Parse(args[i + 1]);
                previousCaptureDelta = Time.captureDeltaTime;
                Time.captureDeltaTime = 1f / fps;
                previousSettings = InputSystem.settings;
                InputSystem.settings = Object.Instantiate(previousSettings);
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                pad = InputSystem.AddDevice<Gamepad>();
                input = default;
                phase = 0; lastFrame = -1; failures.Clear();
                started = Time.time; deadline = EditorApplication.timeSinceStartup + 180;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode) Cleanup();
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Running, false) || !EditorApplication.isPlaying || pad == null || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Movement checks timed out.");
                float elapsed = Time.time - started;
                if (driver)
                {
                    int state = driver.Motor.ActiveStateID.ID;
                    if (state == 2 && lastState != 2) jumpEntries++;
                    lastState = state;
                    if (elapsed > .6f) distance += Vector3.ProjectOnPlane(driver.Animator.transform.position - previousPosition, Vector3.up).magnitude;
                    maxTurnStep = Mathf.Max(maxTurnStep, Quaternion.Angle(previousRotation, driver.Animator.transform.rotation));
                    previousPosition = driver.Animator.transform.position;
                    previousRotation = driver.Animator.transform.rotation;
                }
                switch (phase)
                {
                    case 0:
                        if (elapsed < .75f) break;
                        driver = Object.FindFirstObjectByType<HumanLocomotionController>();
                        morph = driver.GetComponentInChildren<CharacterMorphController>();
                        driver.MoveAction.action.actionMap.asset.devices = new InputDevice[] { pad };
                        driver.GetComponentInChildren<CinemachineOrbitalFollow>().HorizontalAxis.Value = 0;
                        Stick(Vector2.up); Next();
                        break;
                    case 1:
                        if (elapsed < 1.6f) break;
                        Debug.Log($"FEEL forward fps={fps}: speed={distance/(elapsed-.6f):F3}m/s");
                        minTurnForward = 10;
                        Stick(Vector2.right); Next();
                        break;
                    case 2:
                        minTurnForward = Mathf.Min(minTurnForward, driver.Motor.MovementAxis.z);
                        if (elapsed < .4f) break;
                        Check(minTurnForward > .1f, $"90-degree turn applies AI braking: forward axis={minTurnForward:F3}");
                        Debug.Log($"FEEL turn fps={fps}: minimum forward={minTurnForward:F3}; max frame turn={maxTurnStep:F2}deg");
                        Stick(Vector2.left); Next();
                        break;
                    case 3:
                        if (elapsed < .8f) break;
                        float angle = Vector3.Angle(Vector3.ProjectOnPlane(driver.Animator.transform.forward, Vector3.up), Vector3.left);
                        Check(angle < 15, $"Reversal failed to settle: {angle:F1} degrees");
                        Check(maxTurnStep < 90, $"Reversal snapped {maxTurnStep:F1} degrees in a frame");
                        Stick(Vector2.zero); Next();
                        break;
                    case 4:
                        if (elapsed < 1.4f) break;
                        Check(distance < .15f && driver.Motor.Grounded, $"Stop did not settle on ground: drift={distance:F3}m");
                        driver.Motor.Strafe = true;
                        Stick(Vector2.up); Next();
                        break;
                    case 5:
                        if (elapsed < 1.8f) break;
                        fullSpeed = distance / (elapsed - .6f);
                        Stick(Vector2.up * .4f); Next();
                        break;
                    case 6:
                        if (elapsed < 1.8f) break;
                        float slowSpeed = distance / (elapsed - .6f);
                        Debug.Log($"FEEL strafe fps={fps}: full={fullSpeed:F3}m/s; partial={slowSpeed:F3}m/s; ratio={slowSpeed/fullSpeed:F3}");
                        Check(slowSpeed < fullSpeed * .8f && slowSpeed > .05f, "Partial stick input does not produce a controlled slow strafe");
                        driver.Motor.Strafe = false; Stick(Vector2.zero); Next();
                        break;
                    case 7:
                        if (elapsed < 1.2f) break;
                        baseY = peak = driver.Animator.transform.position.y;
                        jumpEntries = 0;
                        input = new GamepadState().WithButton(GamepadButton.South); Next();
                        break;
                    case 8:
                        if (elapsed >= .1f && elapsed < .1f + 1f / fps)
                            Check(driver.Animator.transform.position.y - baseY > .2f, "Jump takeoff waited for animation instead of responding to input");
                        peak = Mathf.Max(peak, driver.Animator.transform.position.y);
                        input = elapsed < .15f || (elapsed > .2f && elapsed < .28f)
                            ? new GamepadState().WithButton(GamepadButton.South) : default;
                        if (elapsed < 2.5f) break;
                        Debug.Log($"FEEL jump fps={fps}: peak={peak-baseY:F3}m; grounded={driver.Motor.Grounded}");
                        Check(peak - baseY > 1.05f && peak - baseY < 1.5f && driver.Motor.Grounded, "Jump missed the 1.25m ballistic target or did not settle");
                        Check(jumpEntries == 1, $"Airborne jump press caused another jump: {jumpEntries} jumps");
                        swapPosition = driver.Animator.transform.position;
                        swapRotation = driver.Animator.transform.rotation;
                        morph.SetSex(morph.ActiveSex == CharacterSex.Female ? CharacterSex.Male : CharacterSex.Female);
                        Next();
                        break;
                    case 9:
                        if (elapsed < .35f) break;
                        Check(Vector3.ProjectOnPlane(driver.Animator.transform.position - swapPosition, Vector3.up).magnitude < .1f, "Runtime rig switch teleported the player");
                        Check(Quaternion.Angle(swapRotation, driver.Animator.transform.rotation) < 10, "Runtime rig switch lost heading");
                        oldHeight = driver.Motor.MainCollider.height;
                        morph.SetMorph("body.height", 1);
                        morph.RigProportionDriver.RebuildAvatarNow();
                        Next();
                        break;
                    case 10:
                        if (elapsed < .5f) break;
                        Debug.Log($"FEEL rig fps={fps}: capsule height {oldHeight:F3} -> {driver.Motor.MainCollider.height:F3}");
                        Check(driver.Motor.MainCollider.height > oldHeight + .02f, "Runtime proportion change did not refit the locomotion capsule");
                        input = new GamepadState().WithButton(GamepadButton.East); Next();
                        break;
                    case 11:
                        input = default;
                        if (elapsed < .6f) break;
                        Check(driver.Motor.Stance.ID == 7 && !driver.Motor.LockInput && !driver.Motor.LockMovement, "Gamepad crouch input did not activate cleanly");
                        input = new GamepadState().WithButton(GamepadButton.East); Next();
                        break;
                    case 12:
                        input = default;
                        if (elapsed < .8f) break;
                        Check(driver.Motor.Stance.ID != 7, "Gamepad crouch did not toggle off");
                        baseY = driver.Animator.transform.position.y;
                        jumpEntries = 0;
                        input = new GamepadState().WithButton(GamepadButton.South); Next();
                        break;
                    case 13:
                        input = default;
                        if (elapsed > .35f && !driver.Motor.Grounded && driver.Motor.ActiveStateID.ID == 3
                            && driver.Animator.transform.position.y < baseY + .15f)
                        { input = new GamepadState().WithButton(GamepadButton.South); Next(); }
                        else if (elapsed > 2) throw new InvalidOperationException("Did not reach pre-landing buffer test window");
                        break;
                    case 14:
                        if (elapsed > .035f) input = default;
                        if (elapsed < 1.5f) break;
                        Check(jumpEntries == 2, $"Pre-landing jump was lost or repeated: {jumpEntries} jumps");
                        driver.TestDeath(); Next();
                        break;
                    case 15:
                        if (elapsed < 1) break;
                        Check(driver.IsDead && driver.Motor.LockMovement, "Death test did not activate");
                        driver.Respawn(); Stick(Vector2.up); Next();
                        break;
                    case 16:
                        if (elapsed < 1.5f) break;
                        Check(!driver.IsDead && !driver.Motor.LockInput && driver.Motor.MainCollider.enabled && distance > .5f,
                            "Death/respawn did not restore movement and collision");
                        driver.TestDeath(); driver.Respawn(); Stick(Vector2.up); Next();
                        break;
                    case 17:
                        if (elapsed < 1.2f) break;
                        Check(!driver.IsDead && driver.Motor.MainCollider.enabled && distance > .5f,
                            "Immediate respawn was broken by delayed death cleanup");
                        Finish();
                        break;
                }
            }
            catch (Exception e) { failures.Add(e.ToString()); Finish(); }
        }

        static void Stick(Vector2 value) => input = new GamepadState { leftStick = value };
        static void Check(bool condition, string message) { if (!condition) failures.Add(message); }
        static void Next()
        {
            phase++; started = Time.time; distance = maxTurnStep = 0;
            previousPosition = driver.Animator.transform.position;
            previousRotation = driver.Animator.transform.rotation;
        }
        static void Cleanup()
        {
            SessionState.SetBool(Running, false);
            if (driver) driver.MoveAction.action.actionMap.asset.devices = null;
            if (pad != null) InputSystem.RemoveDevice(pad);
            pad = null;
            if (previousSettings)
            {
                var temporarySettings = InputSystem.settings;
                InputSystem.settings = previousSettings;
                Object.DestroyImmediate(temporarySettings);
            }
            Time.captureDeltaTime = previousCaptureDelta;
        }
        static void Finish()
        {
            foreach (var failure in failures) Debug.LogError("FEEL_FAIL: " + failure);
            if (failures.Count == 0) Debug.Log($"LOCOMOTION_FEEL_PASS: {fps}fps; turns, reversal, stop, analog strafe, single jump, runtime rig/proportion changes.");
            Cleanup();
            if (Application.isBatchMode) EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }
    }
}
