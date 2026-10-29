using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Sol.Locomotion.EditorTools
{
    // Authors only this optional module's controller and clips.
    public static class GroundedAnimationSetup
    {
        const string Clips = "Assets/Locomotion/Animations/Fishbox/";
        static AnimatorController controller;
        static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(Clips + name + ".anim")
            ?? throw new InvalidOperationException("Missing RPG clip: " + name);

        [MenuItem("Tools/Sol/Locomotion/Apply Grounded Animation Set %#F6")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            AssetDatabase.Refresh();
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { Clips.TrimEnd('/') }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                string name = Path.GetFileNameWithoutExtension(path);
                clip.name = name;
                AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
                // Imported facial muscle keys must not override the creator's facial pose.
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.propertyName.StartsWith("Jaw") || binding.propertyName.Contains("Eye"))
                        AnimationUtility.SetEditorCurve(clip, binding, null);
                    // Fishbox's takeoff clips continue into a many-metre authored fall.
                    // Retain the limb pose, but let the motor own the airborne trajectory.
                    if ((name.StartsWith("Jump") || name == "Fall") && binding.propertyName == "RootT.y")
                        AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, clip.length, .97f));
                }
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = !(name.StartsWith("Jump") || name.StartsWith("Land") || name == "Death");
                settings.loopBlend = settings.loopTime;
                // Motor owns yaw, gravity and ground alignment. Preserve horizontal root travel.
                settings.loopBlendOrientation = true;
                settings.loopBlendPositionY = true;
                settings.keepOriginalOrientation = true;
                settings.keepOriginalPositionY = true;
                settings.heightFromFeet = false;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
            }
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(LocomotionValidation.ControllerPath)
                ?? throw new InvalidOperationException("Missing RPG controller: " + LocomotionValidation.ControllerPath);
            var gait = Tree("Gait", BlendTreeType.FreeformCartesian2D, "Horizontal", "Vertical");
            var children = new List<ChildMotion> { Child("Idle", 0, 0), Child("WalkBack", 0, -1) };
            foreach (var x in new[] { -2f, -1f, 0f, 1f, 2f })
            {
                children.Add(Child(x < 0 ? "WalkLeanL" : x > 0 ? "WalkLeanR" : "Walk", x, 1));
                children.Add(Child(x < 0 ? "RunLeanL" : x > 0 ? "RunLeanR" : "Run", x, 2));
                children.Add(Child("Sprint", x, 3));
            }
            children.Add(Child("TurnL", -1, 0)); children.Add(Child("TurnR", 1, 0));
            gait.children = children.ToArray();
            var crouch = Tree("Crouch Gait", BlendTreeType.Simple1D, "Vertical");
            crouch.children = new[] { Child("CrouchS", 0, -1), Child("CrouchIdle", 0, 0), Child("CrouchN", 0, 1), Child("CrouchN", 0, 2, 1.2f) };
            var strafe = Directional(false);
            var crouchStrafe = Directional(true);
            var idleStrafe = TurnTree(false);
            var idleCrouchStrafe = TurnTree(true);
            var land = Tree("Landing", BlendTreeType.Simple1D, "Vertical");
            land.children = new[] { Child("Land", 0, 0), Child("LandRun", 0, 2) };
            int replaced = 0;
            foreach (var state in States(controller.layers[0].stateMachine))
            {
                Motion motion = state.name switch
                {
                    "Idle" or "Idle Look" => Clip("Idle"),
                    "Locomotion" => gait,
                    "Idle Crouch" => Clip("CrouchIdle"),
                    "Locomotion Crouch" => crouch,
                    "Locomotion Strafe IP" => strafe,
                    "Locomotion Strafe Crouch IP" => crouchStrafe,
                    "Idle Strafe" => idleStrafe,
                    "Idle Crouch Strafe" => idleCrouchStrafe,
                    "Jump In Place" => Clip("JumpIdle"),
                    "Jump Walk Air L" => Clip("JumpWalkL"),
                    "Jump Walk Air R" => Clip("JumpWalkR"),
                    "Jump Run Air L" or "Jump Sprint L" => Clip("JumpRunL"),
                    "Jump Run Air R" or "Jump Sprint R" => Clip("JumpRunR"),
                    "Fall" or "Fall From Edge" => Clip("Fall"),
                    "Land Soft" => land,
                    "Land Hard" or "Super Hero Land" => Clip("LandHard"),
                    "Death 1" or "Death 2" or "Death 3" => Clip("Death"),
                    _ => null
                };
                if (!motion) continue;
                state.motion = motion; state.speed = 1; state.mirror = false; state.cycleOffset = 0;
                if (state.tag == "Jump")
                {
                    // Jump height is input/physics driven; these old clip-timed messages
                    // must not restart it midway through the replacement animation.
                    foreach (var behaviour in state.behaviours) UnityEngine.Object.DestroyImmediate(behaviour, true);
                }
                EditorUtility.SetDirty(state); replaced++;
            }
            EditorUtility.SetDirty(controller);
            var player = PrefabUtility.LoadPrefabContents(LocomotionValidation.PlayerPath);
            try
            {
                foreach (var animator in player.GetComponentsInChildren<Animator>(true)) animator.runtimeAnimatorController = controller;
                PrefabUtility.SaveAsPrefabAsset(player, LocomotionValidation.PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            AssetDatabase.SaveAssets();
            Debug.Log($"RPG_ANIMATIONS_APPLIED: {replaced} base-layer states; 45 selected Fishbox clips; source controller preserved.");
        }

        static ChildMotion Child(string clip, float x, float y, float rate = 1) => new()
        { motion = Clip(clip), position = new Vector2(x, y), threshold = y, timeScale = rate };

        static BlendTree Tree(string name, BlendTreeType type, string x, string y = "Vertical")
        {
            name = "RPG " + name;
            var tree = AssetDatabase.LoadAllAssetsAtPath(LocomotionValidation.ControllerPath).OfType<BlendTree>().FirstOrDefault(t => t.name == name);
            if (!tree) { tree = new BlendTree { name = name }; AssetDatabase.AddObjectToAsset(tree, controller); }
            tree.blendType = type; tree.blendParameter = x; tree.blendParameterY = y; tree.useAutomaticThresholds = false;
            EditorUtility.SetDirty(tree); return tree;
        }

        static BlendTree Directional(bool crouch)
        {
            var tree = Tree(crouch ? "Crouch Strafe" : "Strafe", BlendTreeType.FreeformDirectional2D, "Horizontal", "Vertical");
            var children = new List<ChildMotion> { Child(crouch ? "CrouchIdle" : "Idle", 0, 0) };
            string[] directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            for (int i = 0; i < directions.Length; i++)
            {
                float angle = i * Mathf.PI / 4;
                var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                string suffix = directions[i] == "N" ? "" : directions[i] == "S" ? "Back" : directions[i];
                children.Add(Child(crouch ? "Crouch" + directions[i] : "Walk" + suffix, direction.x, direction.y));
                children.Add(Child(crouch ? "Crouch" + directions[i] : "Run" + suffix, direction.x * 2, direction.y * 2, crouch ? 1.2f : 1));
            }
            tree.children = children.ToArray(); return tree;
        }

        static BlendTree TurnTree(bool crouch)
        {
            var tree = Tree(crouch ? "Crouch Aim Turn" : "Aim Turn", BlendTreeType.Simple1D, "AimHorizontal");
            tree.children = new[] { Child(crouch ? "CrouchTurnL" : "TurnL", 0, -1), Child(crouch ? "CrouchIdle" : "Idle", 0, 0), Child(crouch ? "CrouchTurnR" : "TurnR", 0, 1) };
            return tree;
        }

        static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
        {
            foreach (var state in machine.states) yield return state.state;
            foreach (var child in machine.stateMachines)
                foreach (var state in States(child.stateMachine)) yield return state;
        }
    }
}
