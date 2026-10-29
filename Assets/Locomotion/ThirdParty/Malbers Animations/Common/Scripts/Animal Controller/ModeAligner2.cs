// Last Change: 9/21/2026 9:40am
using System.Collections;
using MalbersAnimations.Controller;
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using MalbersAnimations.Conditions;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace MalbersAnimations
{
    [AddComponentMenu("Malbers/Animal Controller/Mode Aligner2")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/main-components/mode-aligner2")]
    public class ModeAligner2 : MonoBehaviour
    {
        [System.Serializable]
        public class AlignData
        {
            public string name = "Aligner Set";
            public ModeID mode;

            public bool active = true;

            [Tooltip("include/exclude the abilities from the list")]
            public bool include = true;
            [Tooltip("List of Abilities to search for inside the Mode.\n If empty, then search for any ability")]
            public List<IntReference> specificAbilities;
            public bool HasAbilities => specificAbilities != null && specificAbilities.Count > 0;

            [Tooltip("Extra conditions to filter for this specific aligner [Dynamic Target: Found Target]")]
            public Conditions2 conditions;

            [Tooltip("Radius used for the Search off all possible align targets")]
            public float additiveRadius = 0;

            //[Tooltip("Radius used for the Search off all possible align targets")]
            //[Min(0)] public float search = 10;


            [Tooltip("Angle in front of the character to Prioritize  ")]
            [Min(0)] public float PriorityAngle = 90;

            [Tooltip("Delay Needed to activate the alignment")]
            [Min(0)] public float delay = 0;

            [Tooltip("Angle Offset to align to the target")]
            public float angleOffset = 0;

            [Tooltip("Time needed to complete the Position alignment")]
            [Min(0)] public float AlignTime = 0.3f;

            [Tooltip("When true, the aligner will move closer the animal to the target. Using the SelfRadius and AI Target Radius values to position correctly")]
            public bool UseRadius = (true);
            [Tooltip("Ignore Moving the character if we are already too close to the target. Only apply look At rotation. Use this for Quadruped animals")]
            public BoolReference IgnoreClose = new(true);
            //[Tooltip("Override default Distance with th e AI Target Distance on a Target")]
            //public BoolReference UseAITargetDistance = new(true);

            [Tooltip("Align Curve used for the alignment")]
            public AnimationCurve AlignCurve = new(new Keyframe[] { new(0, 0), new(1, 1) });

            public Color debugColor = new(1, 0.5f, 0, 0.2f);

            public bool FindAbility(int abilityID)
            {
                if (!HasAbilities) return true; //any ability is valid

                foreach (var ab in specificAbilities)
                {
                    if (ab.Value == abilityID)
                        return include;
                }
                return !include;
            }
        }

        public Vector3 PlatFormInertia { get; set; }


        [RequiredField] public MAnimal animal;
        [Tooltip("Search GameObject tagged as possible Align Targets")]
        public MTags Tags;

        [Tooltip("Owners Radius to keep distance from the dynamic target.")]
        [Min(0)] public float selfRadius = 0.5f;

        [Tooltip("Search Distance to find possible targets")]
        [Min(0)] public float searchDistance = 10f;

        [Tooltip("GLobal conditions to filter for this specific aligner [Dynamic Target: Found Target]")]
        public Conditions2 GlobalConditions;

        public List<AlignData> aligners = new();

        [Tooltip("Align Curve used for the alignment")]
        public AnimationCurve AlignCurve = new(new Keyframe[] { new(0, 0), new(1, 1) });

        public Color debugColor = new(1, 0.5f, 0, 0.2f);
#pragma warning disable CS0414 //MWC: field is read inside #if MALBERS_DEBUG gizmo code; safe to suppress
        [SerializeField] private int selectedIndex = -1;
#pragma warning restore CS0414

        public bool debug;
        private bool Aligning;

        //MWC — Coroutine handles, so the alignment can be cancelled without killing unrelated coroutines
        private Coroutine C_LookAt;
        private Coroutine C_AlignRadius;

        //MWC — Mode that started the current alignment. Used to interrupt it when a different Mode takes over
        private int AligningModeID;

        /// <summary>False when the alignment was started by a Mode and that Mode is no longer the one playing.
        /// (AligningModeID == 0 means the alignment was called manually, so there's no Mode to interrupt it) </summary>
        private bool AlignNotInterrupted => AligningModeID == 0 || (animal.IsPlayingMode && animal.ActiveModeID == AligningModeID);

        void Awake()
        {
            if (animal == null)
                animal = this.FindComponent<MAnimal>();

            if (Tags == null)
            {
                Debug.LogWarning($"[{animal.name}] Mode Aligner: No Align Tag assigned. Please assign a Tag to search for possible Align Targets", this);
                enabled = false;
            }
        }
        void OnEnable()
        {
            if (animal)
            {
                animal.OnModeStart.AddListener(StartingMode);
                animal.PostStateMovement += PosStateMovement;
            }
        }

        void OnDisable()
        {
            if (animal)
            {
                animal.OnModeStart.RemoveListener(StartingMode);
                animal.PostStateMovement -= PosStateMovement;
            }
        }


        private Vector3 OverrideAdditivePos;

        private void PosStateMovement(MAnimal animal)
        {
            if (Aligning)
            {
                //Debug.Log($"Overriding ADPOS {OverrideAdditivePos}");
                animal.AdditivePosition = OverrideAdditivePos;
            }
        }

        void StartingMode(int ModeID, int ability)
        {
            if (!isActiveAndEnabled) return;

            //MWC — Any new Mode invalidates the alignment of the previous one.
            //(E.g. a Dodge interrupting an Attack must free the character from the align hold)
            StopAlign();

            foreach (var mod in aligners)
            {
                //Find if the mode is in the list

                if (!mod.active) continue;
                if (mod.mode == null || mod.mode.ID != ModeID) continue; //MWC — skip empty Mode slots

                if (mod.FindAbility(ability))
                {
                    PreAlign(mod);
                }
            }
        }

        public virtual void Align()
        {
            foreach (var mod in aligners) PreAlign(mod);
        }

        public virtual void StopAlign()
        {
            //MWC — Stop only the align coroutines (instead of StopAllCoroutines) and restore the animal values
            var wasAligning = C_LookAt != null || C_AlignRadius != null;

            if (C_LookAt != null) { StopCoroutine(C_LookAt); C_LookAt = null; }
            if (C_AlignRadius != null) { StopCoroutine(C_AlignRadius); C_AlignRadius = null; }

            Aligning = false;
            OverrideAdditivePos = Vector3.zero;
            AligningModeID = 0;

            if (wasAligning)
            {
                if (animal) animal.ResetGravityValues();
                Debugging("Mode Align Interrupted", this);
            }
        }

        protected void PreAlign(AlignData aligner)
        {
            MDebug.DrawWireSphere(animal.Position, Color.cyan, searchDistance * animal.ScaleFactor, 1f);

            GameObject ClosestGo = null;
            IStopDistance ClosestAITarget = null;
            float MinDist = float.MaxValue;


            foreach (var tags in Tags.tags)
                EvaluateAlignTarget(tags, ref ClosestGo, ref ClosestAITarget, ref MinDist);


            var radius = (selfRadius + aligner.additiveRadius) * animal.ScaleFactor; //use self radius as default
            var TargetCenter = Vector3.zero; //No Center Offset LOCAL

            if (ClosestGo != null)
            {
                if (ClosestAITarget != null)
                {
                    radius = ClosestAITarget.StopDistance(); //replace radius with the AI Target Stop Distance
                    TargetCenter = ClosestAITarget.GetCenterPosition();
                    TargetCenter = ClosestGo.transform.InverseTransformPoint(TargetCenter); //Convert to Local Position
                }

                StartAligning(aligner, ClosestGo.transform, radius, TargetCenter);
            }

            //Local method to evaluate each Tag
            void EvaluateAlignTarget(Tag Tag, ref GameObject NearGo, ref IStopDistance NearAITarget, ref float MinDist)
            {
                if (Tag == null || !Tag.ValidObjects) return;


                foreach (var go in Tag.gameObjects)
                {
                    if (go == null) continue;
                    if (go.transform.IsChildOf(animal.transform)) continue;     //Skip itself and its children
                    if (animal.transform.IsChildOf(go.transform)) continue;     //Skip if the animal is part of the target
                    if (!GlobalConditions.Evaluate(go)) continue;               //Check global conditions
                    if (!aligner.conditions.Evaluate(go)) continue;             //Check extra conditions

                    var radius = (selfRadius + aligner.additiveRadius) * animal.ScaleFactor; //use self radius as default
                    var aiTarget = go.FindInterface<IStopDistance>();

                    if (aiTarget != null)
                    {
                        radius = (aiTarget.StopDistance()); //replace radius with the AI Target Stop Distance
                    }

                    float DistTarget = Vector3.Distance(animal.Position, go.transform.position) - radius;

                    if (DistTarget < MinDist && DistTarget < searchDistance)
                    {
                        MinDist = DistTarget;
                        NearGo = go;
                        NearAITarget = aiTarget;
                    }
                }
            }
        }

        private void StartAligning(AlignData aligner, Transform Target, float TargetRadius, Vector3 TargetCenter)
        {
            StopAlign(); //MWC — Cancel any alignment still running before starting a new one

            //MWC — Remember which Mode owns this alignment (ActiveModeID is already set when OnModeStart is invoked)
            AligningModeID = animal.ActiveModeID;

            C_LookAt = StartCoroutine(
                   LookAt(animal.transform, Target, TargetCenter, aligner)); //MWC — keep the handle

            //Align Look At the Zone Using Distance
            if (aligner.UseRadius)
            {
                MDebug.DrawLine(animal.Center, TargetCenter, Color.yellow, 2f);
                C_AlignRadius = StartCoroutine(AlignTransformRadius(animal, Target, TargetCenter, TargetRadius, aligner)); //MWC — keep the handle
            }
        }

        public IEnumerator LookAt(Transform t1, Transform t2, Vector3 LocalTargetPos, AlignData aligner)
        {
            float elapsedTime = 0;
            var wait = new WaitForFixedUpdate();

            if (aligner.delay > 0)
                yield return new WaitForSeconds(aligner.delay);

            Quaternion CurrentRot = t1.rotation;
            var target = t2.TransformPoint(LocalTargetPos);         //Convert to Global Position

            Vector3 direction = (target - t1.position);

            direction = Vector3.ProjectOnPlane(direction, t1.up);   //Clear Y values

            Quaternion FinalRot = Quaternion.LookRotation(direction);

            Vector3 Offset = transform.position;

            if (Offset != animal.Position) //Adjust the direction taking in consideration the Offset of the Aligner position
            {
                //Calculate Real Direction at the End! 
                Quaternion TargetInverse_Rot = Quaternion.Inverse(t1.rotation);
                Quaternion TargetDelta = TargetInverse_Rot * FinalRot;

                var TargetPosition = t1.position + t1.DeltaPositionFromRotate(Offset, TargetDelta);

                direction = (target - TargetPosition);

                var debTime = 2f;

                if (debug)
                {
                    MDebug.Draw_Arrow(TargetPosition, direction, Color.yellow, debTime);
                    MDebug.DrawWireSphere(TargetPosition, 0.1f, Color.green, debTime);
                    MDebug.DrawWireSphere(target, 0.1f, Color.yellow, debTime);
                }
            }

            if (direction.CloseToZero())
            {
                Debug.LogWarning("Direction is Zero. Please set a correct rotation", t1);
                C_LookAt = null; //MWC — the routine is over
                yield break;
            }
            else
            {
                Aligning = true;

                Quaternion Last_Platform_Rot = t1.rotation;

                var time = aligner.AlignTime;


                //MWC — Stop the LookAt as soon as another Mode takes over
                while ((time > 0) && (elapsedTime <= time) && AlignNotInterrupted && t2)
                {
                    yield return wait;

                    direction = (t2.position - t1.position);

                    //add the angle offset
                    var angleOffset = aligner.angleOffset;
                    if (angleOffset != 0)
                        direction = Quaternion.Euler(0, angleOffset, 0) * direction;

                    direction = Vector3.ProjectOnPlane(direction, t1.up); //Remove Y values
                    FinalRot = Quaternion.LookRotation(direction);

                    //Evaluation of the Pos curve
                    float result = AlignCurve != null ? AlignCurve.Evaluate(elapsedTime / time) : elapsedTime / time;

                    t1.rotation = Quaternion.SlerpUnclamped(CurrentRot, FinalRot, result);

                    if (Offset != animal.Position)
                    {
                        Quaternion Inverse_Rot = Quaternion.Inverse(Last_Platform_Rot);
                        Quaternion Delta = Inverse_Rot * t1.rotation;
                        t1.position += t1.DeltaPositionFromRotate(Offset, Delta);
                    }

                    elapsedTime += Time.fixedDeltaTime;
                    Last_Platform_Rot = t1.rotation;
                }
                Aligning = false;
            }

            C_LookAt = null; //MWC — the routine is over
        }

        public IEnumerator AlignTransformRadius(MAnimal animal, Transform Target, Vector3 localTargetPos, float TargetRadius, AlignData aligner)
        {
            float radius = (selfRadius + aligner.additiveRadius) * animal.ScaleFactor;

            // if (radius > 0)
            {
                float elapsedTime = 0;

                var Wait = new WaitForFixedUpdate();

                Vector3 StartingPos = animal.Position;

                var WorldTargetPos = Target.TransformPoint(localTargetPos); //Convert to Global Position

                Debugging($"Mode Align Begin with [{Target.name}]", this);

                //******Clear UP values******

                Plane plane = new(animal.UpVector, animal.Position);
                WorldTargetPos = plane.ClosestPointOnPlane(WorldTargetPos);

                var Direction = (animal.Position - WorldTargetPos).normalized;

                Ray TargetRay = new(WorldTargetPos, Direction.normalized);

                radius += TargetRadius;

                Vector3 TargetPos = TargetRay.GetPoint(radius);

                Debug.DrawRay(TargetPos, Vector3.up, Color.cyan, 2f);
                Debug.DrawRay(WorldTargetPos, Vector3.up, Color.blue, 2f);

                Debug.DrawRay(animal.Position, -Direction, Color.cyan, 1f);

                animal.TryResetDeltaRootMotion(); //Reset delta RootMotion

                MDebug.DrawWireSphere(TargetPos, Color.red, 0.05f, 3);

                float TargetDistance;

                var time = aligner.AlignTime;
                var curve = aligner.AlignCurve;
                var IgnoreClose = aligner.IgnoreClose.Value;
                var delay = aligner.delay;

                if (delay > 0)
                    yield return new WaitForSeconds(delay);

                Aligning = true;

                //MWC — [AlignNotInterrupted] stops the movement when a Mode outside the list takes over
                while ((time > 0) && (elapsedTime <= time) && animal.IsPlayingMode && AlignNotInterrupted && Target)
                {
                    //if (Target == null) yield break;


                    yield return Wait;

                    TargetDistance = Vector3.Distance(animal.Center, WorldTargetPos);

                    if (!IgnoreClose || TargetDistance >= radius)
                    {
                        //Evaluation of the Pos curve
                        float result = curve != null ? curve.Evaluate(elapsedTime / time) : elapsedTime / time;
                        Direction = (-animal.Position + WorldTargetPos).normalized * radius;

                        var TargetPoint = WorldTargetPos - Direction;

                        var nextPos = Vector3.Lerp(StartingPos, TargetPoint, result);
                        var deltaPos = nextPos - animal.Position;

                        OverrideAdditivePos = deltaPos; //Override the Delta Position

                        MDebug.DrawWireSphere(animal.Position, Color.yellow, 0.05f, 3);
                        MDebug.DrawWireSphere(TargetPos, Color.yellow, 0.05f, 3);

                        MDebug.DrawCircle(WorldTargetPos, Target.rotation, radius, Color.red);
                        MDebug.DrawWireSphere(TargetPoint, Color.white, 0.05f);
                    }
                    else //if (TargetDistance < radius)
                    {
                        OverrideAdditivePos = Vector3.zero;
                    }
                    elapsedTime += Time.fixedDeltaTime;

                    // animal.GlobalRootMotion.Value = TargetDistance >= radius; //Only apply RootMotion if we are not close to the target
                }

                //MWC — Do not snap to the final aligned position if another Mode interrupted the alignment
                if (AlignNotInterrupted)
                {
                    Direction = (-animal.Position + WorldTargetPos).normalized * radius;

                    var FinalPos = WorldTargetPos - Direction;//Override the Delta Position

                    animal.AdditivePosition = FinalPos - animal.Position;

                    animal.InertiaPositionSpeed = Vector3.zero;
                }

                //Make sure is not moving inside the target after the alignment ends
                //MWC — [AlignNotInterrupted] releases the hold when a Mode outside the list takes over
                while (animal.IsPlayingMode && AlignNotInterrupted && Target)
                {
                    //if (Target == null) yield break;

                    // if (TargetDistance < radius)
                    {
                        Aligning = true;
                        OverrideAdditivePos = Vector3.zero;
                    }
                    yield return Wait;
                }
                Aligning = false;
                OverrideAdditivePos = Vector3.zero; //MWC — clear the override so it does not leak into the next Mode
            }

            C_AlignRadius = null; //MWC — the routine is over

            Debugging($"Mode Align Finished with [{(Target != null ? Target.name : "Target Destroyed")}]", this);

            animal.ResetGravityValues(); //This works!

            yield return null;
        }

        /// <summary>Editor only console log for the Aligner, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void Debugging(string deb, Object ob)
        {
#if UNITY_EDITOR
            if (debug) Debug.Log($"<B>[{animal.name}]</B> <color=green>{deb}</color>", ob);
#endif
        }

#if UNITY_EDITOR

        void Reset()
        {
            animal = gameObject.FindComponent<MAnimal>();
            Tags = new MTags(MTools.GetInstance<Tag>("Targets"));


            aligners = new List<AlignData>()
            {
                new()
                {
                    active = true,
                    name = "Aligner 1",
                    UseRadius = true,
                    AlignTime = 0.3f,
                    include = true,
                    IgnoreClose = new(true),
                   // UseAITargetDistance = new(true),
                //     Layer = new(1 << 20), //Default Layer is Animal
                    AlignCurve = new AnimationCurve(new Keyframe[] { new(0,0), new(1,1)}),
                    mode = MTools.GetInstance<ModeID>("Attack1"),
                    conditions = new Conditions2()
                    {
                        active = true,
                        conditions =  new ConditionCore[1]
                        {
                            new C2_Layers() {Layer = new LayerReference(1 << 20) } //Animal Layer 
                        }
                    }
                }
            };
        }


#if MALBERS_DEBUG
        void OnDrawGizmosSelected()
        {
            if (animal && debug)
            {
                var scale = animal.ScaleFactor;
                var Pos = transform.position;

                if (selectedIndex != -1)
                {
                    var aligner = aligners[selectedIndex];
                    var radius = (aligner.additiveRadius + selfRadius) * scale;

                    var debColor = aligner.debugColor;
                    var c = debColor; c.a = 1;

                    Handles.color = c;

                    Handles.DrawWireDisc(Pos, Vector3.up, searchDistance * animal.ScaleFactor);
                    Handles.DrawWireDisc(Pos, Vector3.up, radius);
                    Handles.color = debColor;
                    Handles.DrawSolidDisc(Pos, Vector3.up, radius);

                    Gizmos.color = debColor;
                    Gizmos.DrawSphere(Pos, 0.1f * scale);
                    Gizmos.color = c;
                    Gizmos.DrawWireSphere(Pos, 0.1f * scale);

                    var angle = aligners[selectedIndex].angleOffset;
                    if (angle != 0)
                    {
                        Gizmos.color = Color.yellow;
                        Gizmos.DrawRay(Pos, Vector3.forward * selfRadius);

                        //draw another ray using the aligner angle offset
                        var direction = Quaternion.Euler(0, angle, 0) * transform.forward;
                        Gizmos.DrawRay(Pos, direction * selfRadius);
                    }

                    aligner.conditions.Gizmos(this);
                }
            }
        }
#endif
#endif
    }

    //----------------------------------------------------------------------- //

    //Create a custom editor to show the Attack Modes in a list
#if UNITY_EDITOR

    [CustomEditor(typeof(ModeAligner2))]
    public class MModeAlign2Editor : Editor
    {
        private ReorderableList Reo_List_AlignData;

        private SerializedProperty aligners, animal, debug, Tag, GlobalConditions,
            extras,
            radius, searchDistance, selectedIndex;


        private ModeAligner2 M;

        protected virtual void OnEnable()
        {
            aligners = serializedObject.FindProperty("aligners");
            debug = serializedObject.FindProperty("debug");
            radius = serializedObject.FindProperty("selfRadius");
            searchDistance = serializedObject.FindProperty("searchDistance");
            GlobalConditions = serializedObject.FindProperty("GlobalConditions");


            animal = serializedObject.FindProperty("animal");
            Tag = serializedObject.FindProperty("Tags");
            extras = serializedObject.FindProperty("extras");
            selectedIndex = serializedObject.FindProperty("selectedIndex");

            M = (ModeAligner2)target;

            Reo_List_AlignData = new(serializedObject, aligners, true, true, true, true)
            {
                drawHeaderCallback = (rect) =>
                {
                    float lineHeight = EditorGUIUtility.singleLineHeight;
                    //Draw name and Mode in the same line 
                    var nameRect = new Rect(rect.x, rect.y, rect.width * 0.5f - 5, lineHeight);
                    EditorGUI.LabelField(nameRect, "    Aligner Name");
                    var modeRect = new Rect(rect.x + rect.width * 0.5f + 15, rect.y, rect.width * 0.5f - 15, lineHeight);
                    EditorGUI.LabelField(modeRect, "    Mode");
                },
                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    var element = aligners.GetArrayElementAtIndex(index);
                    rect.y += 2;
                    float lineHeight = EditorGUIUtility.singleLineHeight;

                    var activeRect = new Rect(rect.x, rect.y, 20, lineHeight);
                    //Draw name and Mode in the same line 
                    EditorGUI.PropertyField(activeRect, element.FindPropertyRelative("active"), GUIContent.none);
                    var nameRect = new Rect(rect.x + 20, rect.y, rect.width * 0.5f - 5 - 20, lineHeight);
                    EditorGUI.PropertyField(nameRect, element.FindPropertyRelative("name"), GUIContent.none);
                    var modeRect = new Rect(rect.x + rect.width * 0.5f + 15, rect.y, rect.width * 0.5f - 15, lineHeight);
                    EditorGUI.PropertyField(modeRect, element.FindPropertyRelative("mode"), GUIContent.none);
                },
                onAddCallback = (ReorderableList list) =>
                {
                    M.aligners ??= new();

                    M.aligners.Add(
                        new ModeAligner2.AlignData()
                        {
                            active = true,
                            name = "Aligner " + (M.aligners.Count + 1),
                            // radius = 10,
                            UseRadius = true,
                            AlignTime = 0.3f,
                            include = true,
                            IgnoreClose = new(true),
                            //   UseAITargetDistance = new(true),
                            //Layer = new(1 << 20), //Default Layer is Animal
                            AlignCurve = new AnimationCurve(new Keyframe[] { new(0, 0), new(1, 1) }),
                            mode = MTools.GetInstance<ModeID>("Attack1"),
                            conditions = new Conditions2()
                            {
                                active = true,
                                conditions = new ConditionCore[1]
                                {
                                    new C2_Layers() {Layer = new LayerReference(1 << 20) } //Animal Layer 
                                }
                            }
                        }
                        );

                    MTools.SetDirty(M);
                },
            };

            Reo_List_AlignData.index = selectedIndex.intValue;
        }


        private GUIContent Icon_Include;
        private GUIContent Icon_Exclude;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription($"Execute a LookAt towards the closest [Align Targets]  when is playing a Mode from the list. Use [AITarget] to extract a Target Radius");

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(animal);
                    MalbersEditor.DrawDebugIcon(debug);
                }


                if (!M.Tags.ValidObjects)
                {
                    EditorGUILayout.HelpBox("Assign a Runtime GameObject Set to search for Align Targets", MessageType.Error);
                }

                EditorGUILayout.PropertyField(radius);
                EditorGUILayout.PropertyField(searchDistance);
                EditorGUILayout.PropertyField(Tag);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(GlobalConditions);
                EditorGUI.indentLevel--;
            }
            Reo_List_AlignData.DoLayoutList();

            selectedIndex.intValue = Reo_List_AlignData.index;

            if (Reo_List_AlignData.index >= 0 && Reo_List_AlignData.index < aligners.arraySize)
            {
                var element = aligners.GetArrayElementAtIndex(Reo_List_AlignData.index);

                var activeProp = element.FindPropertyRelative("active");
                var name = element.FindPropertyRelative("name");


                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        // EditorGUILayout.PropertyField(activeProp, GUIContent.none, GUILayout.Width(25));
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(element, false);
                        EditorGUI.indentLevel--;
                        EditorGUILayout.PropertyField(element.FindPropertyRelative("debugColor"), GUIContent.none, GUILayout.Width(50));
                        //element.isExpanded = GUILayout.Toggle(element.isExpanded, new GUIContent(name.stringValue), EditorStyles.foldoutHeader);
                    }

                    if (element.isExpanded)
                    {
                        var Include = element.FindPropertyRelative("include");
                        using (new GUILayout.HorizontalScope())
                        {
                            var abilities = element.FindPropertyRelative("specificAbilities");
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(abilities);
                            EditorGUI.indentLevel--;


                            if (abilities.arraySize > 0)
                            {
                                if (Icon_Include == null)
                                {
                                    Icon_Include = EditorGUIUtility.IconContent("d_Valid");
                                    Icon_Include.tooltip = "Includes the Ability ID";
                                }

                                if (Icon_Exclude == null)
                                {
                                    Icon_Exclude = EditorGUIUtility.IconContent("d_Toolbar Minus");
                                    Icon_Exclude.tooltip = "Excludes the Ability ID";
                                }

                                var btn = Include.boolValue ? Icon_Include : Icon_Exclude;
                                var bgColor = GUI.backgroundColor;

                                GUI.backgroundColor = Include.boolValue ? MTools.MGreen * 2 : MTools.MRed * 2;
                                Include.boolValue = GUILayout.Toggle(Include.boolValue, btn, EditorStyles.toolbarButton, GUILayout.Width(28), GUILayout.Height(28));
                                GUI.backgroundColor = bgColor;
                            }
                        }

                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(element.FindPropertyRelative("conditions"));
                        EditorGUI.indentLevel--;

                        var UseRadius = element.FindPropertyRelative("UseRadius");
                        using (new GUILayout.HorizontalScope())
                        {
                            EditorGUILayout.PropertyField(element.FindPropertyRelative("AlignTime"));
                            EditorGUIUtility.labelWidth = 40;
                            EditorGUILayout.PropertyField(element.FindPropertyRelative("delay"), GUILayout.MinWidth(50));
                            EditorGUIUtility.labelWidth = 0;
                        }
                        using (new GUILayout.HorizontalScope())
                        {

                            EditorGUILayout.PropertyField(element.FindPropertyRelative("angleOffset"));
                            EditorGUILayout.PropertyField(element.FindPropertyRelative("AlignCurve"), GUIContent.none, GUILayout.Width(30));
                        }
                        EditorGUILayout.PropertyField(UseRadius);

                        if (UseRadius.boolValue)
                        {
                            EditorGUILayout.PropertyField(element.FindPropertyRelative("additiveRadius"));
                            EditorGUILayout.PropertyField(element.FindPropertyRelative("IgnoreClose"));
                            //EditorGUILayout.PropertyField(element.FindPropertyRelative("UseAITargetDistance"));
                        }

                        //EditorGUILayout.PropertyField(element.FindPropertyRelative("KeepCurrentTarget"));
                    }
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}