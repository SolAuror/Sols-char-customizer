// Last Change: 9/18/2026 1:05pm
using MalbersAnimations.Items;
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    // MWC — Super Item AI Task: press/hold/release an Item Action (Attack, Aim, Reload…) on the equipped Super Item through the Super Item Manager
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Tasks/Super Item/Item Action", fileName = "new SI Item Action Task")]
    public class SIActionTask : MTask
    {
        public override string DisplayName => "Super Item/Item Action";

        [RequiredField, Tooltip("Action to play on the equipped Item (the same ID the Item Actions use, e.g. Attack, Aim, Reload)")]
        public ItemActionID actionID;

        [Tooltip("Only use the Action of this Item. Leave empty for any equipped Item that has the Action")]
        public SuperItemID itemID;

        [Tooltip("Play Once: plays the Action once and the Task is Done when it finishes.\nPlay Forever: keeps playing it using the CoolDown.\nInterrupt: stops the Action")]
        public PlayWhen Play = PlayWhen.PlayForever;

        [Tooltip("How long the Action input stays pressed.\n0 = tap (press, released on the next update).\n> 0 = hold for that many seconds (charged attacks).\n< 0 = hold until the AI State exits (Aim, Block)")]
        public FloatReference HoldTime = new(0);

        [Tooltip("Play the Action only when the animal has arrived to the target")]
        public bool near = false;

        [Tooltip("Play the Action if the AI Animal is within this distance from the Target. Ignored if this value is zero")]
        public FloatReference ActivateDistance = new(0);

        [Tooltip("Time elapsed to play the Action again and again (Play Forever)")]
        public FloatReference CoolDown = new(0);

        [Tooltip("When the Task is set to Play Forever, it will ignore the first cooldown")]
        public bool IgnoreFirstCoolDown = true;

        [Tooltip("Play Forever: ACCEPTED presses per burst before the CoolDown applies (e.g. 3 for a 3-hit combo: press, press, press, CoolDown, press, press, press…).\n" +
            "A press the Combo drops (outside its window) or the Action ignores (still playing) is not counted and is retried at the Repeat Rate.\n0 = no burst, the CoolDown runs between every press")]
        public IntReference Repeat = new(0);

        [Tooltip("Play Forever: time between the presses of a burst. 0 = one press per Update Interval")]
        public FloatReference RepeatRate = new(0);

        [Tooltip("Play the Action only if the Animal is looking at the Target. 360 = no angle check")]
        [Range(0f, 360f)]
        public float ModeAngle = 360f;




        [Tooltip("Set the Brain Target as the Aim Target of the Super Item Manager while the Task runs (needed by Aim/Fire Actions)")]
        public bool AimAtTarget = false;

        [Tooltip("Clear the Aim Target set by this Task when the AI State exits.\nDisable it to keep aiming at the Target after the Task ends (e.g. a following Task that fires at the same Target)")]
        public bool ClearTargetOnExit = true;

        [Tooltip("Release and interrupt the Action when the AI State exits")]
        public bool StopOnExit = true;

        //MWC: Per-agent runtime state. Lives on the brain (not on this shared SO) so each AI tracks its own press.
        private class ActionState : BrainState
        {
            /// <summary>True once the Action has been played at least once by this task run</summary>
            public bool HasPlayed;
            /// <summary>True while the Action input is pressed</summary>
            public bool Holding;
            /// <summary>Time.time of the last press</summary>
            public float PressTime;
            /// <summary>True when this task set the Aim Target (so it only clears its own)</summary>
            public bool AimSet;
            /// <summary>Presses done in the current burst (Play Forever + Repeat)</summary>
            public int Count;
            /// <summary>True when the last press was actually taken (the Action restarted, or the Combo advanced a sequence)</summary>
            public bool Accepted;
        }

        /// <summary>Caches the Super Item Manager and prepares the Aim Target</summary>
        public override void StartTask(MAnimalBrain brain, int index)
        {
            var manager = brain.Animal.FindComponent<SuperItemManager>();
            brain.TasksVars[index].mono = manager; //Store the Super Item Manager on the TaskVars

            var state = brain.TaskState<ActionState>(index);
            state.HasPlayed = false;   //reset per activation (typed state persists across re-entries)
            state.Holding = false;
            state.PressTime = 0;
            state.AimSet = false;
            state.Count = 0;

            if (manager == null)
            {
                Debug.Log("No Super Item Manager Found on the Animal", brain.Animal);
                brain.TaskDone(index);
                return;
            }

            if (Play == PlayWhen.Interrupt)
            {
                manager.Action_Release(actionID, itemID);
                manager.Action_Interrupt(actionID);
                brain.TaskDone(index);
                return;
            }

            if (AimAtTarget && manager.Aim != null && brain.Target != null)
            {
                manager.Aim.UseCamera = false; //An NPC has no camera to aim with
                manager.Aim.SetTarget(brain.Target);
                state.AimSet = true;
            }
        }

        /// <summary>Presses/releases the Action while the Target is in range, and finishes the Task (Play Once) when the Action ends</summary>
        public override void UpdateTask(MAnimalBrain brain, int index)
        {
            var manager = brain.TasksVars[index].mono as SuperItemManager;
            var state = brain.TaskState<ActionState>(index);

            if (manager == null) return;

            //Release the input once the Hold Time elapsed (negative = hold until exit)
            if (state.Holding && HoldTime >= 0 && Time.time - state.PressTime >= HoldTime)
                Release(manager, state);

            //A Targeter queued by a limited [AI Target] (Target Limit) has "arrived" at the WAITING ring, not at the
            //Target: it must not attack from there. Same rule as PlayModeTask, whatever the [Near] setting.
            var arrived = (!near || brain.AIControl.HasArrived) && !brain.AIControl.IsWaitingOnTarget;
            var inRange = arrived && InRange(brain) && InAngle(brain);

            if (!inRange)
            {
                if (state.Holding) Release(manager, state); //The Target went away: release the input (WeaponTask behaviour)
                state.Count = 0;                            //A burst never survives losing the Target
            }
            else
            {
                switch (Play)
                {
                    case PlayWhen.PlayOnce:
                        if (!state.HasPlayed && (CoolDown <= 0 || MTools.ElapsedTime(brain.TasksStartTime[index], CoolDown)))
                            Press(brain, manager, state);
                        break;

                    case PlayWhen.PlayForever:
                        if (Repeat > 0 && state.Count > 0) //Inside a burst: the next press waits the Repeat Rate, not the CoolDown
                        {
                            if (MTools.ElapsedTime(state.PressTime, RepeatRate) && Press(brain, manager, state))
                                BurstStep(brain, index, state);
                        }
                        else //First press (of a burst, or a regular one): wait the CoolDown
                        {
                            var ready = (!state.HasPlayed && IgnoreFirstCoolDown) || MTools.ElapsedTime(brain.TasksStartTime[index], CoolDown);

                            if (ready && Press(brain, manager, state))
                            {
                                //Reset the cooldown timer. In a burst only an ACCEPTED first press starts it (a dropped one is retried at the Repeat Rate)
                                if (Repeat <= 0 || state.Accepted) brain.TasksStartTime[index] = Time.time;
                                BurstStep(brain, index, state);
                            }
                        }
                        break;

                    default: break;
                }
            }

            //Play Once: the Task is done when the Action finished playing
            if (Play == PlayWhen.PlayOnce && state.HasPlayed && !state.Holding && !manager.IsActionPlaying(itemID, actionID))
                brain.TaskDone(index);
        }

        /// <summary>Releases/interrupts the Action and clears the Aim Target set by this task</summary>
        public override void ExitAIState(MAnimalBrain brain, int index)
        {
            base.ExitAIState(brain, index);

            var manager = brain.TasksVars[index].mono as SuperItemManager;
            var state = brain.TaskState<ActionState>(index);

            if (manager == null) return;

            if (state.Holding) Release(manager, state);
            if (StopOnExit) manager.Action_Interrupt(actionID);

            if (state.AimSet)
            {
                if (ClearTargetOnExit && manager.Aim != null) manager.Aim.ClearTarget();
                state.AimSet = false;
            }
        }

        /// <summary>Presses the Action input. Returns true if the Action is playing afterwards; state.Accepted tells if THIS press was taken</summary>
        private bool Press(MAnimalBrain brain, SuperItemManager manager, ActionState state)
        {
            if (state.Holding) return false; //Already pressed (waiting for the Hold Time)

            //Snapshot the Combo Manager: a press the Combo takes changes the active sequence (or starts the combo)
            var combo = manager.ComboManager;
            var sequenceBefore = combo != null ? combo.ActiveComboSequence : null;
            var indexBefore = combo != null && combo.ActiveCombo != null ? combo.ActiveCombo.ActiveSequenceIndex : -1;
            var playingBefore = combo != null && combo.PlayingCombo;
            var bufferedBefore = CountBuffered(combo);

            if (!manager.Action_Press(actionID, itemID))
            {
                //The press was rejected (the Action was vetoed, or its Combo/Mode could not start and the Action exited at
                //once). ActivateOnInput(true) has already latched the Action's InputValue, and nothing here would ever
                //release it (Holding stays false), so every later press was a no-op for the Action's edge detection
                //(lastInputValue == inputValue): the AI stood in its Attack State and never attacked again. Let go now.
                manager.Action_Release(actionID, itemID);
                state.Accepted = false;
                return false;
            }

            state.Holding = true;
            state.PressTime = Time.time;
            state.HasPlayed = true;
            state.Accepted = WasAccepted(manager, combo, sequenceBefore, indexBefore, playingBefore, bufferedBefore);

            return true;
        }

        /// <summary>Was the press taken? Combo Action: the Combo advanced, or buffered the next Sequence. Other Actions: the Action (re)started this frame</summary>
        private bool WasAccepted(SuperItemManager manager, ComboManager combo, ComboSequence sequenceBefore, int indexBefore, bool playingBefore, int bufferedBefore)
        {
            if (!manager.TryGet_Action(actionID, out _, out var action, itemID)) return false;

            if (combo != null && HasComboProcessor(action))
            {
                var indexAfter = combo.ActiveCombo != null ? combo.ActiveCombo.ActiveSequenceIndex : -1;

                if (combo.ActiveComboSequence != sequenceBefore || indexAfter != indexBefore || (combo.PlayingCombo && !playingBefore)) return true;

                //A press inside the Activation range but before the Activation Time only BUFFERS the next Sequence: the
                //Combo plays it later from a coroutine, so nothing has changed yet. That press WAS taken, and counting it
                //as dropped made the burst press one extra time, which played one extra attack at the end of the combo.
                return CountBuffered(combo) > bufferedBefore;
            }

            return action.StartFrame == Time.frameCount; //A press inside a playing Action (no Interrupt Self, or in its Rate) is ignored by the Action
        }

        /// <summary>Sequences of the Active Combo that are buffered and waiting for their Activation Time</summary>
        private static int CountBuffered(ComboManager combo)
        {
            var sequences = combo != null && combo.ActiveCombo != null ? combo.ActiveCombo.Sequence : null;
            if (sequences == null) return 0;

            int count = 0;

            for (int i = 0; i < sequences.Count; i++)
                if (sequences[i] != null && sequences[i].Buffer) count++;

            return count;
        }

        /// <summary>True when the Action drives a Combo (has a Combo processor)</summary>
        private static bool HasComboProcessor(ItemAction action)
        {
            var processors = action.processors;
            if (processors == null) return false;

            for (int i = 0; i < processors.Length; i++)
                if (processors[i] is ItemProcessorCombo) return true;

            return false;
        }

        /// <summary>Counts an ACCEPTED press of the burst. When the burst is complete the CoolDown starts counting from now</summary>
        private void BurstStep(MAnimalBrain brain, int index, ActionState state)
        {
            if (Repeat <= 0) return;      //No burst: the CoolDown runs between every press
            if (!state.Accepted) return;  //Dropped press (outside the Combo window, Action still playing…): retried at the Repeat Rate, not counted

            state.Count++;

            if (state.Count >= Repeat)
            {
                state.Count = 0;                          //Burst complete
                brain.TasksStartTime[index] = Time.time;  //The CoolDown starts after the LAST press of the burst
            }
        }

        /// <summary>Releases the Action input</summary>
        private void Release(SuperItemManager manager, ActionState state)
        {
            manager.Action_Release(actionID, itemID);
            state.Holding = false;
        }

        /// <summary>True when the Target is inside the Activate Distance. No Target = only when no distance is required</summary>
        private bool InRange(MAnimalBrain brain)
        {
            if (ActivateDistance <= 0) return true; //No distance required
            if (brain.Target == null) return false;

            return Vector3.Distance(brain.Animal.transform.position, brain.Target.position) < ActivateDistance;
        }

        /// <summary>True when the Target is inside the Mode Angle in front of the Animal Eyes (360 = always)</summary>
        private bool InAngle(MAnimalBrain brain)
        {
            if (ModeAngle >= 360f || brain.Target == null) return true;

            var eyes = brain.Eyes != null ? brain.Eyes : brain.Animal.transform;
            var directionToTarget = brain.Target.position - eyes.position;
            var eyesForward = Vector3.ProjectOnPlane(eyes.forward, brain.Animal.UpVector);

            return Vector3.Dot(directionToTarget.normalized, eyesForward) > Mathf.Cos(ModeAngle * 0.5f * Mathf.Deg2Rad);
        }

#if UNITY_EDITOR && MALBERS_DEBUG
        public override void DrawGizmos(MAnimalBrain brain)
        {
            if (brain == null || brain.Animal == null) return;

            var eyes = brain.Eyes != null ? brain.Eyes : brain.Animal.transform;
            var center = brain.Animal.transform.position;
            var c = (Color.green + Color.yellow) / 2;
            c.a = 1f;
            UnityEditor.Handles.color = c;

            if (ActivateDistance > 0)
                UnityEditor.Handles.DrawWireDisc(center, brain.Animal.UpVector, ActivateDistance);

            if (ModeAngle < 360)
            {
                var eyesForward = Vector3.ProjectOnPlane(eyes.forward, brain.Animal.UpVector);
                var rotatedForward = Quaternion.Euler(0, -ModeAngle * 0.5f, 0) * eyesForward;
                UnityEditor.Handles.DrawWireArc(eyes.position, Vector3.up, rotatedForward, ModeAngle, 2);
            }
        }
#endif

        private void Reset() => Description = "Press, hold or release an Item Action (Attack, Aim, Reload…) on the equipped Super Item when the Target is in range";
    }

    #region Inspector
#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(SIActionTask))]
    public class SIActionTaskEditor : UnityEditor.Editor
    {
        UnityEditor.SerializedProperty Description, MessageID, WaitForPreviousTask, UpdateInterval,
            actionID, itemID, Play, HoldTime, near, ActivateDistance, CoolDown, IgnoreFirstCoolDown, Repeat, RepeatRate,
            ModeAngle, AimAtTarget, ClearTargetOnExit, StopOnExit;

        protected virtual void OnEnable()
        {
            Description = serializedObject.FindProperty("Description");
            MessageID = serializedObject.FindProperty("MessageID");
            WaitForPreviousTask = serializedObject.FindProperty("WaitForPreviousTask");
            UpdateInterval = serializedObject.FindProperty("UpdateInterval");
            actionID = serializedObject.FindProperty("actionID");
            itemID = serializedObject.FindProperty("itemID");
            Play = serializedObject.FindProperty("Play");
            HoldTime = serializedObject.FindProperty("HoldTime");
            near = serializedObject.FindProperty("near");
            ActivateDistance = serializedObject.FindProperty("ActivateDistance");
            CoolDown = serializedObject.FindProperty("CoolDown");
            IgnoreFirstCoolDown = serializedObject.FindProperty("IgnoreFirstCoolDown");
            Repeat = serializedObject.FindProperty("Repeat");
            RepeatRate = serializedObject.FindProperty("RepeatRate");
            ModeAngle = serializedObject.FindProperty("ModeAngle");
            AimAtTarget = serializedObject.FindProperty("AimAtTarget");
            ClearTargetOnExit = serializedObject.FindProperty("ClearTargetOnExit");
            StopOnExit = serializedObject.FindProperty("StopOnExit");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            UnityEditor.EditorGUILayout.PropertyField(Description);
            UnityEditor.EditorGUILayout.PropertyField(MessageID);
            UnityEditor.EditorGUILayout.PropertyField(WaitForPreviousTask);
            UnityEditor.EditorGUILayout.PropertyField(UpdateInterval);

            UnityEditor.EditorGUILayout.Space();

            var oldColor = GUI.color;
            GUI.color = actionID.objectReferenceValue == null ? new Color(1, 0.4f, 0.4f, 1) : oldColor;
            UnityEditor.EditorGUILayout.PropertyField(actionID, new GUIContent("Action ID"));
            GUI.color = oldColor;
            UnityEditor.EditorGUILayout.PropertyField(itemID, new GUIContent("Item ID (Optional)"));

            UnityEditor.EditorGUILayout.PropertyField(Play, new GUIContent("Action"));
            var playtype = (PlayWhen)Play.intValue;

            UnityEditor.EditorGUILayout.PropertyField(CoolDown);
            if (playtype == PlayWhen.PlayForever)
            {
                UnityEditor.EditorGUILayout.PropertyField(IgnoreFirstCoolDown);
                UnityEditor.EditorGUILayout.PropertyField(Repeat, new GUIContent("Repeat (Burst)"));
                if (Repeat.FindPropertyRelative("ConstantValue").intValue > 0 || !Repeat.FindPropertyRelative("UseConstant").boolValue)
                    UnityEditor.EditorGUILayout.PropertyField(RepeatRate);
            }


            if (playtype != PlayWhen.Interrupt)
            {
                UnityEditor.EditorGUILayout.PropertyField(HoldTime);
                UnityEditor.EditorGUILayout.PropertyField(near);

                if (!near.boolValue)
                    UnityEditor.EditorGUILayout.PropertyField(ActivateDistance);



                UnityEditor.EditorGUILayout.PropertyField(ModeAngle);
                UnityEditor.EditorGUILayout.PropertyField(AimAtTarget);

                if (AimAtTarget.boolValue)
                {
                    UnityEditor.EditorGUI.indentLevel++;
                    UnityEditor.EditorGUILayout.PropertyField(ClearTargetOnExit, new GUIContent("Clear Target on Exit"));
                    UnityEditor.EditorGUI.indentLevel--;
                }

                UnityEditor.EditorGUILayout.PropertyField(StopOnExit);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
    #endregion
}
