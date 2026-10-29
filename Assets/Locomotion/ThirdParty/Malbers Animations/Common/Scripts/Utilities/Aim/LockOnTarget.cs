// Last Change: 9/21/2026 9:40am
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Utilities
{
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/utilities/lock-on-target")]
    public class LockOnTarget : MonoBehaviour
    {
        [Tooltip("The Lock On Target will activate automatically if any target is stored on the list")]
        public BoolReference Auto = new(false);

        [Tooltip("The Lock On Target requires an Aim Component")]
        [RequiredField] public Aim aim;

        [Tooltip("Set of the focused 'potential' Targets")]
        [RequiredField] public RuntimeGameObjects Targets;

        [Tooltip("Optional. When set, the locked transform is the child of the Target that holds one of these Tags (e.g. a Head bone with a Tags component) instead of the Target's root. Tags are checked in order; falls back to the root if no child has any of them")]
        public MTags LockOnTags = new();

        [Tooltip("Time needed to change to the next or previous target")]
        public FloatReference NextTargetTime = new(0f);

        [Tooltip("If an obstacle is between the target and the character. Locking will be disabled")]
        public BoolReference CheckForObstacles = new(false);

        [Tooltip("If another target can be found then swap to that target")]
        public BoolReference AutoSwapOnObstacle = new(false);
        // public LayerReference ObstacleLayer = new(1);
        private float CurrentTime;

        private int CurrentTargetIndex = -1;
        public bool debug;

        public TransformEvent OnTargetChanged = new();
        //  public TransformEvent OnTargetAimAssist = new();
        public BoolEvent OnLockingTarget = new();

        [HideInInspector] public int Editor_Tabs1;

        /// <summary>Transform currently locked on (tagged child or Target root). Setting it runs the obstacle check and fires OnTargetChanged</summary>
        public Transform LockedTarget
        {
            get => locketTarget;

            private set
            {
                if (CheckForObstacles.Value)
                {
                    aim.SetTargetValueInternal(value);  //Temporal set the Value to the new target to check for obstacles
                    aim.AimLogic(true);                 //Calculate the HIT RAYCAST
                    aim.SetTargetValueInternal(null);   //Reset the value to the original one

                    var aimHit = aim.AimHit.transform;

                    if (aimHit != null && !aimHit.SameHierarchy(value) && value != null && aimHit.gameObject.layer != value.gameObject.layer)
                    {
                        value = null;
                        aim.SetTarget(value);
                    }
                }

                //Reference comparison on purpose. When the locked Transform was DESTROYED (the enemy died, or the scene is
                //being torn down on Play Mode exit) the Unity == treats it as null, so a Reset (value = null) looked like
                //"no change": OnTargetChanged never sent null and every listener (the Locked Target var, the UI, the
                //Camera event) kept the dead reference.
                if (!ReferenceEquals(value, locketTarget))
                {
                    locketTarget = value;
                    aim.SetTarget(value);

                    IsAimTarget = value != null ? value.FindComponent<AimTarget>() : null;
                    aimTargetAssist = IsAimTarget != null ? IsAimTarget.AimPoint : null;

                    OnTargetChanged.Invoke(aimTargetAssist != null ? aimTargetAssist : value);
                    // OnTargetAimAssist.Invoke(aimTargetAssist);
                }
            }
        }
        private Transform locketTarget;
        private Transform aimTargetAssist;
        //  private Transform DefaultAimTarget;

        /// <summary>True while the Lock On is active (the target may still be null if none was reachable)</summary>
        public bool LockingOn { get; private set; }

        /// <summary>AimTarget component found on the Locked Target (null if it has none)</summary>
        public AimTarget IsAimTarget { get; private set; }

        /// <summary>Aim Point of the AimTarget component on the Locked Target (null if none). This is what OnTargetChanged sends when present</summary>
        public Transform AimAssistPoint => aimTargetAssist;

        /// <summary>Index of the Locked Target inside the Targets Set (-1 when nothing is locked)</summary>
        public int TargetIndex => CurrentTargetIndex;

        /// <summary>Seconds left before Target_Next/Target_Previous can cycle again (0 when ready)</summary>
        public float NextTargetCooldown => NextTargetTime > 0 ? Mathf.Max(0f, NextTargetTime - (Time.time - CurrentTime)) : 0f;

        /// <summary>Root GameObject of this component</summary>
        public GameObject Owner => transform.root.gameObject;

        private void Awake()
        {
            Targets.Clear();

            if (aim == null) aim = this.FindComponent<Aim>();
        }

        protected virtual void OnEnable()
        {
            if (Targets != null)
            {
                Targets.OnItemAdded.AddListener(OnItemAdded);
                Targets.OnItemRemoved.AddListener(OnItemRemoved);
            }

            if (aim != null) aim.OnRayHitChanged += OnHitChanged;


            ResetLockOn();
        }



        private void OnDisable()
        {
            if (Targets != null)
            {
                Targets.OnItemAdded.RemoveListener(OnItemAdded);

                Targets.OnItemRemoved.RemoveListener(OnItemRemoved);
            }
            ResetLockOn();

            if (aim != null) aim.OnRayHitChanged -= OnHitChanged;

        }

        /// <summary>Aim ray hit listener: drops or swaps the lock when an obstacle blocks the Locked Target</summary>
        private void OnHitChanged(RaycastHit hit)
        {
            if (CheckForObstacles.Value && LockedTarget != null)
            {
                var aimHit = aim.AimHit.transform;

                if (aimHit != null && !aimHit.SameHierarchy(LockedTarget) && aimHit.gameObject.layer != LockedTarget.gameObject.layer)
                {
                    if (AutoSwapOnObstacle.Value)
                    {
                        FindNextInLineWhenObstacle();
                    }
                    else
                    {
                        ResetLockOn();
                    }
                }
            }
        }

        /// <summary>Targets Set listener: auto-locks the first target that enters when Auto is enabled</summary>
        private void OnItemAdded(GameObject arg0)
        {
            if (Auto.Value && !LockingOn)
            {
                LockTarget(true);
            }
        }

        /// <summary>Toggles the Lock On state (ignored while the game is paused)</summary>
        public void LockTargetToggle()
        {
            if (Time.timeScale == 0) return; //Do nothing if the game is paused

            LockingOn ^= true;
            LookingTarget();
        }

        /// <summary>Enables or disables the Lock On (ignored while the game is paused)</summary>
        public void LockTarget(bool value)
        {
            if (Time.timeScale == 0) return; //Do nothing if the game is paused

            LockingOn = value;
            LookingTarget();
        }

        /// <summary>Locks the nearest target when LockingOn is true, otherwise resets the lock</summary>
        private void LookingTarget()
        {
            if (LockingOn)
            {
                if (Targets != null && Targets.Count > 0) //if we have a focused Item
                {
                    FindNearestTarget();
                    OnLockingTarget.Invoke(true);
                }
            }
            else
            {
                ResetLockOn();
            }
        }

        /// <summary>Resets the values when the Lock Target is off</summary>
        private void ResetLockOn()
        {
            //if (LockedTarget != null)
            {
                CurrentTargetIndex = -1;
                LockedTarget = null;
                LockingOn = false;
                OnLockingTarget.Invoke(false);
                OnTargetChanged.Invoke(null);
                Debugging($"Reset Locked Target: [Empty]");
            }
        }

        /// <summary>Resolves the transform to lock on for a Target: the first child holding one of the LockOnTags (checked in order), otherwise the Target's own transform</summary>
        private Transform ResolveLockTransform(GameObject target)
        {
            if (target == null) return null;
            if (LockOnTags == null || !LockOnTags.ValidObjects) return target.transform;

            foreach (var tag in LockOnTags.tags)
            {
                if (tag == null) continue;
                var tagged = target.transform.FindWithMalbersTag(tag);
                if (tagged != null) return tagged;
            }

            return target.transform;
        }

        /// <summary>Locks on the closest Target of the Set, resolving the tagged child when LockOnTags is set</summary>
        private void FindNearestTarget()
        {
            var closest = Targets.Item_GetClosest(gameObject);  //When Lock Target is On.. Get the nearest Target on the Set

            if (closest)
            {
                LockedTarget = ResolveLockTransform(closest);

                if (LockedTarget != null)
                {
                    CurrentTargetIndex = Targets.items.IndexOf(closest);    //Save the Index so we can cycle to all the targets.
                    Debugging($"Locked Target: {LockedTarget.name}");
                }
                else
                {
                    if (AutoSwapOnObstacle.Value) FindNextInLineWhenObstacle();
                }
            }
            else
            {
                ResetLockOn();
            }
        }

        /// <summary>Tries every Target in order until one is not blocked by an obstacle; resets the lock if none is reachable</summary>
        private void FindNextInLineWhenObstacle()
        {
            for (int i = 0; i < Targets.Count; i++)
            {
                LockedTarget = ResolveLockTransform(Targets.items[i]);
                if (LockedTarget != null) break;
            }

            if (LockedTarget == null) ResetLockOn();
        }

        /// <summary>Cycles targets from a Vector2 input (positive = next, negative = previous)</summary>
        public void Target_Scroll(Vector2 value)
        {
            if (value.y > 0 || value.x > 0)
                Target_Next();
            else if (value.y < 0 || value.x < 0)
                Target_Previous();
        }

        /// <summary>Cycles targets from a float input (positive = next, negative = previous)</summary>
        public void Target_Scroll(float value)
        {
            if (value > 0) Target_Next();
            else if (value < 0) Target_Previous();
        }

        /// <summary>Locks the next target on the Set (wraps around). Respects NextTargetTime</summary>
        public void Target_Next()
        {
            if (NextTargetTime > 0 && (Time.time - CurrentTime) <= NextTargetTime) return;
            // if (CurrentTime == Time.time) return;

            if (Targets != null && LockedTarget != null && CurrentTargetIndex != -1) //Check everything is working
            {
                CurrentTime = Time.time;

                CurrentTargetIndex++;
                CurrentTargetIndex %= Targets.Count; //Cycle to the first in case we are on the last item on the list

                LockedTarget = ResolveLockTransform(Targets.Item_Get(CurrentTargetIndex));     //Store the Next Target

                if (LockedTarget)
                {
                    Debugging($"Locked Next Target: {LockedTarget.name}");
                }
                else
                {
                    if (AutoSwapOnObstacle.Value) FindNextInLineWhenObstacle();
                }
            }
        }

        /// <summary>Locks the previous target on the Set (wraps around). Respects NextTargetTime</summary>
        public void Target_Previous()
        {
            if (NextTargetTime > 0 && (Time.time - CurrentTime) <= NextTargetTime) return;
            // if (CurrentTime == Time.time) return;

            if (Targets != null && LockedTarget != null && CurrentTargetIndex != -1) //Check everything is working
            {
                CurrentTime = Time.time;

                CurrentTargetIndex--;
                if (CurrentTargetIndex == -1) CurrentTargetIndex = Targets.Count - 1;

                LockedTarget = ResolveLockTransform(Targets.Item_Get(CurrentTargetIndex));     //Store the Previous Target

                if (LockedTarget)
                {
                    Debugging($"Locked Previous Target: {LockedTarget.name}");

                }
                else
                {
                    if (AutoSwapOnObstacle.Value) FindNextInLineWhenObstacle();
                }
            }
        }

        /// <summary>Targets Set listener: re-locks the nearest target next frame when one leaves while locking</summary>
        private void OnItemRemoved(GameObject _)
        {
            if (LockingOn) //If we are still on Lock Mode then find the next Target
                this.Delay_Action(() => FindNearestTarget()); //Find the nearest target the next frame
        }

        /// <summary>Editor-only console log, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void Debugging(string value)
        {
#if UNITY_EDITOR
            if (debug)
                Debug.Log($"<B>[{aim.name}]</B> → <color=white>{value}</color>", this);
#endif
        }


#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!aim) aim = this.FindComponent<Aim>();
        }


        private void Reset()
        {
            aim = this.FindComponent<Aim>();

            Targets = MTools.GetInstance<RuntimeGameObjects>("Lock on Targets");
            var lockedTarget = MTools.GetInstance<TransformVar>("Locked Target");

            var CamEvent = MTools.GetInstance<MEvent>("Set Camera LockOnTarget");


            UnityEditor.Events.UnityEventTools.AddPersistentListener(OnTargetChanged, CamEvent.Invoke);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(OnTargetChanged, lockedTarget.SetValue);
        }
#endif
    }

    #region Inspector
#if UNITY_EDITOR
    [CanEditMultipleObjects, CustomEditor(typeof(LockOnTarget), true)]
    public class LockOnTargetEditor : Editor
    {
        LockOnTarget m;

        SerializedProperty Auto, aim, Targets, LockOnTags, NextTargetTime, CheckForObstacles, AutoSwapOnObstacle,
            debug, OnTargetChanged, OnLockingTarget, Editor_Tabs1;

        private static readonly Color LockedColor = new(0.3f, 1f, 0.5f, 1f);
        private static readonly string[] Tabs = new string[] { "General", "Events", "Debug" };

        protected virtual void OnEnable()
        {
            m = (LockOnTarget)target;

            Auto = serializedObject.FindProperty(nameof(m.Auto));
            aim = serializedObject.FindProperty(nameof(m.aim));
            Targets = serializedObject.FindProperty(nameof(m.Targets));
            LockOnTags = serializedObject.FindProperty(nameof(m.LockOnTags));
            NextTargetTime = serializedObject.FindProperty(nameof(m.NextTargetTime));
            CheckForObstacles = serializedObject.FindProperty(nameof(m.CheckForObstacles));
            AutoSwapOnObstacle = serializedObject.FindProperty(nameof(m.AutoSwapOnObstacle));
            debug = serializedObject.FindProperty(nameof(m.debug));
            OnTargetChanged = serializedObject.FindProperty(nameof(m.OnTargetChanged));
            OnLockingTarget = serializedObject.FindProperty(nameof(m.OnLockingTarget));
            Editor_Tabs1 = serializedObject.FindProperty(nameof(m.Editor_Tabs1));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MalbersEditor.DrawDescription("Locks the Aim component on the closest object of the Targets Set. Cycle targets with Next/Previous");

            using (var cc = new EditorGUI.ChangeCheckScope())
            {
                Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tabs);

                int Selection = Editor_Tabs1.intValue;

                if (Selection == 0) ShowGeneral();
                else if (Selection == 1) ShowEvents();
                else if (Selection == 2) ShowDebug();

                if (cc.changed) Undo.RecordObject(target, "Lock On Target Changed");
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>General tab: references, tags, cycling and obstacle settings</summary>
        private void ShowGeneral()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(Auto);
                    MalbersEditor.DrawDebugIcon(debug);
                }

                EditorGUILayout.PropertyField(aim);
                if (aim.objectReferenceValue == null)
                    EditorGUILayout.HelpBox("An Aim component is required. It is searched on this GameObject and its parents", MessageType.Error);

                EditorGUILayout.PropertyField(Targets);
                if (Targets.objectReferenceValue == null)
                    EditorGUILayout.HelpBox("A Runtime GameObjects Set is required to store the potential Targets", MessageType.Error);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                LockOnTags.isExpanded = MalbersEditor.Foldout(LockOnTags.isExpanded, "Lock On Point");
                if (LockOnTags.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(LockOnTags);
                    EditorGUI.indentLevel--;
                    EditorGUILayout.HelpBox("Optional. Lock on the child of the Target that has one of these Tags (e.g. the Head). Falls back to the Target root", MessageType.None);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                NextTargetTime.isExpanded = MalbersEditor.Foldout(NextTargetTime.isExpanded, "Target Cycling");
                if (NextTargetTime.isExpanded)
                {
                    EditorGUILayout.PropertyField(NextTargetTime);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                CheckForObstacles.isExpanded = MalbersEditor.Foldout(CheckForObstacles.isExpanded, "Obstacles");
                if (CheckForObstacles.isExpanded)
                {
                    EditorGUILayout.PropertyField(CheckForObstacles);

                    using (new EditorGUI.DisabledGroupScope(!m.CheckForObstacles.Value))
                    {
                        EditorGUILayout.PropertyField(AutoSwapOnObstacle);
                    }
                }
            }
        }

        /// <summary>Events tab</summary>
        private void ShowEvents()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(OnTargetChanged);
                EditorGUILayout.PropertyField(OnLockingTarget);
                EditorGUI.indentLevel--;
            }
        }

        /// <summary>Debug tab: console toggle, live lock state, Targets Set contents and runtime controls</summary>
        private void ShowDebug()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(debug, new GUIContent("Console", "Log the Lock On changes on the Console"));
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Runtime data is shown in Play Mode", MessageType.Info);
                return;
            }

            DrawLockState();
            DrawTargetsSet();
            DrawRuntimeControls();

            Repaint();
        }

        /// <summary>Live state of the lock: flags, locked transform, aim assist and cycling data</summary>
        private void DrawLockState()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Lock State", EditorStyles.boldLabel);

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.ToggleLeft("Locking On", m.LockingOn, Bold(m.LockingOn), GUILayout.Width(95));
                    EditorGUILayout.ToggleLeft("Has Target", m.LockedTarget != null, Bold(m.LockedTarget != null), GUILayout.Width(95));
                    EditorGUILayout.ToggleLeft("Aim Assist", m.AimAssistPoint != null, Bold(m.AimAssistPoint != null), GUILayout.Width(95));
                }

                EditorGUILayout.ObjectField("Locked Target", m.LockedTarget, typeof(Transform), true);
                EditorGUILayout.ObjectField("Aim Assist Point", m.AimAssistPoint, typeof(Transform), true);
                EditorGUILayout.ObjectField("Aim Target (Aim)", m.aim != null ? m.aim.AimTarget : null, typeof(Transform), true);

                using (new EditorGUI.DisabledGroupScope(true))
                {
                    EditorGUILayout.IntField("Target Index", m.TargetIndex);

                    if (m.NextTargetTime > 0)
                        EditorGUILayout.FloatField("Next Target Cooldown", (float)System.Math.Round(m.NextTargetCooldown, 2));

                    if (m.LockedTarget != null)
                        EditorGUILayout.FloatField("Distance", (float)System.Math.Round(Vector3.Distance(m.transform.position, m.LockedTarget.position), 2));
                }

                if (m.CheckForObstacles.Value && m.aim != null)
                {
                    var hit = m.aim.AimHit.transform;
                    var blocked = hit != null && m.LockedTarget != null && !hit.SameHierarchy(m.LockedTarget) && hit.gameObject.layer != m.LockedTarget.gameObject.layer;

                    EditorGUILayout.ObjectField("Aim Ray Hit", hit, typeof(Transform), true);
                    EditorGUILayout.ToggleLeft("Blocked by Obstacle", blocked, Bold(blocked));
                }
            }
        }

        /// <summary>Contents of the Targets Set with the distance to each item; the locked one is highlighted</summary>
        private void DrawTargetsSet()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var set = m.Targets;

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Lock On Targets", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(set != null ? $"Count: {set.Count}" : "No Set", EditorStyles.miniBoldLabel, GUILayout.Width(70));
                }

                if (set == null)
                {
                    EditorGUILayout.HelpBox("No Targets Set assigned", MessageType.Warning);
                    return;
                }

                EditorGUILayout.ObjectField(set, typeof(RuntimeGameObjects), false);

                if (set.IsEmpty)
                {
                    EditorGUILayout.HelpBox("The Targets Set is empty. Targets are added by Triggers/Zones or by calling Item_Add on the Set", MessageType.None);
                    return;
                }

                var origin = m.transform.position;
                var dBg = GUI.backgroundColor;

                for (int i = 0; i < set.Count; i++)
                {
                    var item = set.items[i];
                    var locked = i == m.TargetIndex && m.LockedTarget != null;

                    GUI.backgroundColor = locked ? LockedColor : dBg;

                    using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(locked ? "►" : " ", GUILayout.Width(14));
                        EditorGUILayout.LabelField($"[{i}]", GUILayout.Width(28));
                        EditorGUILayout.ObjectField(item, typeof(GameObject), true);

                        var dist = item != null ? Vector3.Distance(origin, item.transform.position) : 0f;
                        EditorGUILayout.LabelField(item != null ? $"{dist:F1} m" : "null", EditorStyles.miniLabel, GUILayout.Width(55));
                    }
                }

                GUI.backgroundColor = dBg;
            }
        }

        /// <summary>Buttons to drive the public API while playing</summary>
        private void DrawRuntimeControls()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);

                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent(m.LockingOn ? "Unlock" : "Lock On", "LockTarget(bool)")))
                        m.LockTarget(!m.LockingOn);

                    using (new EditorGUI.DisabledGroupScope(m.LockedTarget == null))
                    {
                        if (GUILayout.Button(new GUIContent("◄ Previous", "Target_Previous()"))) m.Target_Previous();
                        if (GUILayout.Button(new GUIContent("Next ►", "Target_Next()"))) m.Target_Next();
                    }
                }
            }
        }

        /// <summary>Bold label style when the flag is true, mini bold otherwise</summary>
        private GUIStyle Bold(bool value) => value ? EditorStyles.boldLabel : EditorStyles.miniBoldLabel;
    }
#endif
    #endregion
}
