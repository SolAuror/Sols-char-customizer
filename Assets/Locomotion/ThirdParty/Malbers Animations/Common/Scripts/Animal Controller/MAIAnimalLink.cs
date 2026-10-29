using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MalbersAnimations.Reactions;
using UnityEngine.Serialization;
using Unity.AI.Navigation;
using MalbersAnimations.Conditions;



#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Controller.AI
{
    [AddComponentMenu("Malbers/AI/AI Animal Link")]
    [RequireComponent(typeof(NavMeshLink))]
    public class MAIAnimalLink : MonoBehaviour
    {
        public static List<MAIAnimalLink> OffMeshLinks;
        public bool BiDirectional = true;

        public Conditions2 ReactionsConditions;

        public Reaction2 StartReaction;
        public Reaction2 EndReaction;
        public Color DebugColor = Color.yellow;

        [Min(0)] public float StoppingDistance = 1f;
        [Min(0)] public float SlowingDistance = 1f;
        [Min(0)] public float SlowingLimit = 0.3f;

        [Tooltip("Safety Time to exit the Offmesh in case something went wrong. This will force the character to exit the offmesh link because it failed to complete it. ")]
        [Min(0)] public float TimeInOffMesh = 0f;

        [Tooltip("Align the Animal to the OffMesh Link when the START point is the closest")]
        public bool AlignToLinkFromStart = true;
        [Tooltip("Align the Animal to the OffMesh Link when the END point  is the closest")]
        public bool AlignToLinkFromEnd = true;

        [Min(0)] public float AlignTime = 0.2f;
        [Tooltip("OffMesh Start Link Transform For aligning when the Character is near the start point")]
        [FormerlySerializedAs("startAlignPoint")]
        [RequiredField] public Transform start;

        [Tooltip("OffMesh End Link Transform")]
        [FormerlySerializedAs("endAlignPoint")]
        [RequiredField] public Transform end;

        [Tooltip("Width of the OffMesh Link (Make it match with Nav Mesh Link)")]
        public float width = 0;


        [Tooltip("Orient the character to the Align Point. If False it will only reposition the character")]
        public bool AlignRotation = true; //If the Animal should rotate to the Direction of the OffMesh Link

        [Tooltip("Use the Animation Rotation to move the Animal instead rotate the transform directly")]
        public bool UseAnimRotation = true;

        [Tooltip("Use Direction for setting the Direction from Start Point to End Point. Disable for Climbing, Ladders")]
        public bool UseDirection = true;

        [Tooltip("If using Direction, the Animal will move using the Direction Vector from Start to End point. In case of Going Upwards on states like Climb, Ladder, Run Vertical")]
        public bool SwapYZ; //Use for climbing

        [Tooltip("Calculate the direction from the Start to End Point")]
        public bool LinkDirection = false;

        [Tooltip("Input Axis to move the Animal when going from Start to the End point. (X: Means Horizontal, Y: Up Down, Z: Forward) Use for climbing")]
        public Vector3 InputAxisFromStart = Vector3.forward;
        [Tooltip("Input Axis to move the Animal when going from End to the Start point. (X: Means Horizontal, Y: Up Down, Z: Forward) Use for climbing")]
        public Vector3 InputAxisFromEnd = Vector3.forward;

        [Tooltip("Minimum Angle to consider the Alignment with the Link is completed. This is used only if Align Rotation is true and UseAnimRotation is true")]
        public float MinAlignAngle = 1.5f;

        public bool debug = true;

        [SerializeField] private NavMeshLink navMeshLink;

        public float gizmoSize = 1f;

        protected virtual void OnEnable()
        {
            OffMeshLinks ??= new List<MAIAnimalLink>();
            OffMeshLinks.Add(this);

            if (TryGetComponent(out navMeshLink))
            {
                navMeshLink.width = width;
            }
        }

        protected virtual void OnDisable()
        {
            OffMeshLinks.Remove(this);
        }

        public virtual void Execute(IAIControl ai, MAnimal animal, Vector3 StartPoint, Vector3 EndPoint)
        {
            animal.StartCoroutine(OffMeshMove(ai, animal, StartPoint, EndPoint));
        }

        /// <summary>  AI START Pathfinding Coroutine </summary>
        public IEnumerator Coroutine_Execute(IAIControl ai, MAnimal animal, Vector3 StartPoint, Vector3 EndPoint)
        {
            yield return OffMeshMove(ai, animal, StartPoint, EndPoint);
        }

        //FIX ALIGNMENT!!!!!
        private IEnumerator OffMeshMove(IAIControl ai, MAnimal animal, Vector3 StartPoint, Vector3 EndPoint)
        {
            if (!start || !end)
            {
                Debug.LogError($"<B>OffMeshLink - [{name}]</B> -> Please assign the Start and End Align Points", this);
                yield break;
            }

            //Debug.Log("OFFMESH MOVING ALIGNMENTNS");

            MDebug.DrawRay(StartPoint, Vector3.up, Color.white * 5, 3f);
            MDebug.DrawRay(EndPoint, Vector3.up, Color.black * 5, 3f);

            Vector3 nearPoint;
            Quaternion nearRotation;
            bool FromStart;

            var dir = (end.position - start.position).normalized;

            var Up = Vector3.Cross(dir, Vector3.Cross(dir, Vector3.up)).normalized;
            var right = 0.5f * width * Vector3.Cross(dir, Up);

            var ClosestStart = MTools.ClosestPointOnLine(animal.Position, start.position - right, start.position + right);
            var ClosestEnd = MTools.ClosestPointOnLine(animal.Position, end.position - right, end.position + right);

            var DistStart = Vector3.Distance(animal.Position, ClosestStart);
            var DistEnd = Vector3.Distance(animal.Position, ClosestEnd);

            if (DistStart < DistEnd)
            {
                nearPoint = ClosestStart;
                nearRotation = start.rotation;
                FromStart = true;
            }
            else
            {
                FromStart = false;
                nearPoint = ClosestEnd;
                nearRotation = end.rotation;
            }

            Debbuging($" Nearest Align Point: {nearPoint}");

            ai.Transform.GetLocalPositionAndRotation(out Vector3 childLocalPos, out Quaternion childLocalRot);

            // Step 2: Desired root rotation = target.rotation * inverse(child.localRotation)
            Quaternion desiredRootRot = nearRotation * Quaternion.Inverse(childLocalRot);

            // Step 3: Desired root position = target.position - (desiredRootRot * child.localPosition)
            Vector3 desiredRootPos = nearPoint - (desiredRootRot * childLocalPos);


            if (start && AlignToLinkFromStart && FromStart || end && AlignToLinkFromEnd && !FromStart)
            {
                desiredRootRot = AlignRotation && !UseAnimRotation ? desiredRootRot : animal.transform.rotation;

                Debbuging($"Start alignment with [{animal.name}]");
                yield return MTools.AlignTransform(animal.transform, desiredRootPos, desiredRootRot, AlignTime);
                Debbuging($"Finish alignment with [{animal.name}]");
            }

            else
                if (AlignRotation)
                {
                    if (UseAnimRotation)
                    {
                        while (Quaternion.Angle(animal.transform.rotation, desiredRootRot) > MinAlignAngle)
                        {
                            animal.RotateAtDirection(desiredRootRot * Vector3.forward);
                            yield return null;
                        }
                    }
                    else
                    {
                        yield return MTools.AlignTransform_Rotation(animal.transform, desiredRootRot, AlignTime);

                    }
                }

            if (ReactionsConditions.Evaluate(animal)) StartReaction.React(animal);


            Debbuging($"Start Offmesh Coroutine");

            ai.InOffMeshLink = true;

            RemainingDistance = float.MaxValue;


            var timeInLink = 0f;

            while (RemainingDistance >= StoppingDistance && ai.InOffMeshLink)
            {
                var AIDirection = StartPoint.DirectionTo(EndPoint);

                if (UseDirection) //If its using Direction vector to move
                {
                    if (LinkDirection)
                    {
                        //if (!BiDirectional) 
                        AIDirection = FromStart ? start.forward : end.forward;
                        // AIDirection = ClosestStart.DirectionTo(ClosestEnd);
                        AIDirection = Vector3.ProjectOnPlane(AIDirection, animal.UpVector);
                    }

                    if (SwapYZ) //Use for climbing(Meaning go All Horizontal, or Forward movement)
                    {
                        AIDirection = transform.InverseTransformDirection(AIDirection); //Convert to UP Down like Climb
                        AIDirection.z = AIDirection.y;
                        AIDirection.y = 0;
                        animal.SetInputAxis(AIDirection * SlowMultiplier);
                        animal.UsingMoveWithDirection = false;
                    }
                    else
                    {
                        ai.AIDirection = (AIDirection);
                        animal.Move(AIDirection * SlowMultiplier);
                    }
                }
                else //If its using Input Axis to to move (Meaning go All Horizontal, or Forward movement)
                {
                    var StartInput = FromStart ? InputAxisFromStart : InputAxisFromEnd; // Determine which Input Axis to use
                    animal.SetInputAxis(StartInput * SlowMultiplier);
                    animal.UsingMoveWithDirection = false;
                }
                //MDebug.Draw_Arrow(animal.Position, AIDirection, Color.green);

                //MDebug.DrawWireSphere(EndPoint, DebugColor, StoppingDistance);
                //MDebug.DrawWireSphere(EndPoint, Color.cyan, SlowingDistance);

                RemainingDistance = Vector3.Distance(animal.transform.position, EndPoint);
                timeInLink += Time.deltaTime;

                if (TimeInOffMesh > 0 && timeInLink >= TimeInOffMesh)
                {
                    Debbuging($"Offmesh Link Timeout after {TimeInOffMesh} seconds");
                    ai.CompleteOffMeshLink();
                    break; //Exit the Offmesh Link because it took too long
                }

                yield return null;
            }

            if (ai.InOffMeshLink)
            {
                if (ReactionsConditions.Evaluate(animal)) EndReaction.React(animal); //Execute the End Reaction only if the Animal has not interrupted the Offmesh Link

                Debbuging($"End Offmesh Coroutine");
                ai.CompleteOffMeshLink();
            }
        }


        public float SlowMultiplier
        {
            get
            {
                var result = 1f;
                if (SlowingDistance > StoppingDistance && RemainingDistance < SlowingDistance)
                    result = Mathf.Max(RemainingDistance / SlowingDistance, SlowingLimit);
                return result;
            }
        }

        public float RemainingDistance { get; private set; }

        private void Debbuging(string valu)
        {
            if (debug) Debug.Log($"<B>OffMeshLink - [{name}]</B> -> {valu}", this);
        }



#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = DebugColor;
            Handles.color = DebugColor;

            var AxisSize = transform.lossyScale.y * gizmoSize;

            if (start)
            {
                Gizmos.DrawSphere(start.position, 0.2f * AxisSize);
                // Handles.ArrowHandleCap(0, startAlignPoint.position, startAlignPoint.rotation, AxisSize, EventType.Repaint);
            }
            if (end)
            {
                Gizmos.DrawSphere(end.position, 0.2f * AxisSize);
                // Handles.ArrowHandleCap(0, endAlignPoint.position, endAlignPoint.rotation, AxisSize, EventType.Repaint);

            }
            if (start && end)
            {
                if (width > 0)
                {
                    var dir = (end.position - start.position).normalized;
                    var Up = Vector3.Cross(dir, Vector3.Cross(dir, Vector3.up)).normalized;
                    var right = 0.5f * width * Vector3.Cross(dir, Up);
                    Handles.DrawLine(start.position + right, end.position + right, 2);
                    Handles.DrawLine(start.position - right, end.position - right, 2);
                    Handles.DrawLine(start.position + right, start.position - right, 2);
                    Handles.DrawLine(end.position + right, end.position - right, 2);

                    Handles.ArrowHandleCap(0, end.position + right, end.rotation, AxisSize, EventType.Repaint);
                    Handles.ArrowHandleCap(0, end.position - right, end.rotation, AxisSize, EventType.Repaint);

                    Handles.ArrowHandleCap(0, start.position + right, start.rotation, AxisSize, EventType.Repaint);
                    Handles.ArrowHandleCap(0, start.position - right, start.rotation, AxisSize, EventType.Repaint);
                }
                else
                {
                    Handles.DrawLine(start.position, end.position, 2);
                    Handles.ArrowHandleCap(0, start.position, start.rotation, AxisSize, EventType.Repaint);
                    Handles.ArrowHandleCap(0, end.position, end.rotation, AxisSize, EventType.Repaint);
                }
            }

        }

        private void OnDrawGizmosSelected()
        {
            if (start)
            {
                Gizmos.color = DebugColor;
                Gizmos.DrawWireSphere(start.position, 0.2f * transform.lossyScale.y * gizmoSize);
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(start.position, StoppingDistance);
                if (StoppingDistance < SlowingDistance)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireSphere(start.position, SlowingDistance);
                }
            }
            if (end)
            {
                Gizmos.color = DebugColor;
                Gizmos.DrawWireSphere(end.position, 0.2f * transform.lossyScale.y * gizmoSize);
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(end.position, StoppingDistance);
                if (StoppingDistance < SlowingDistance)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireSphere(end.position, SlowingDistance);
                }
            }
        }


        private void Reset()
        {
            if (!TryGetComponent<Unity.AI.Navigation.NavMeshLink>(out var navLink))
            {
                navMeshLink = gameObject.AddComponent<Unity.AI.Navigation.NavMeshLink>();

                start = new GameObject("Start Align Point").transform;
                start.SetParent(transform);
                start.SetPositionAndRotation(transform.position, transform.rotation);
                end = new GameObject("End Align Point").transform;
                end.SetParent(transform);
                end.SetPositionAndRotation(transform.position + transform.forward, transform.rotation * Quaternion.Euler(0, 180, 0)); //Point backwards
                navMeshLink.startTransform = start;
                navMeshLink.endTransform = end;
            }

            width = navMeshLink ? navMeshLink.width : 0;
        }

        private void OnValidate()
        {
            if (navMeshLink == null)
                navMeshLink = GetComponent<Unity.AI.Navigation.NavMeshLink>();

            if (navMeshLink)
            {
                if (navMeshLink.width != width)
                    navMeshLink.width = width;

                if (navMeshLink.bidirectional != BiDirectional)
                    navMeshLink.bidirectional = BiDirectional;

                if (navMeshLink.startTransform != navMeshLink.startTransform)
                    navMeshLink.startTransform = start;

                if (navMeshLink.endTransform != navMeshLink.endTransform)
                    navMeshLink.endTransform = end;
            }
        }

#endif


#if UNITY_EDITOR
        [CustomEditor(typeof(MAIAnimalLink)), CanEditMultipleObjects]
        public class MAILinkEditor : Editor
        {
            SerializedProperty StartReaction, EndReaction,
                Start, End, Width,
                DebugColor, UseDirection, AlignRotation, UseAnimRotation, MinAlignAngle,
                  InputAxiStart, InputAxiEnd, SwapYZ, LinkDirection,
                AlignToLinkFromStart, AlignToLinkFromEnd, ReactionsConditions,
                AlignTime, debug,
                StoppingDistance, SlowingLimit, SlowingDistance, BiDirectional, TimeInOffMesh, gizmoSize;

            MAIAnimalLink M;

            protected virtual void OnEnable()
            {
                M = (MAIAnimalLink)target;
                StartReaction = serializedObject.FindProperty("StartReaction");
                debug = serializedObject.FindProperty("debug");
                EndReaction = serializedObject.FindProperty("EndReaction");

                Start = serializedObject.FindProperty(nameof(M.start));
                End = serializedObject.FindProperty(nameof(M.end));
                LinkDirection = serializedObject.FindProperty(nameof(M.LinkDirection));

                AlignRotation = serializedObject.FindProperty(nameof(M.AlignRotation));
                MinAlignAngle = serializedObject.FindProperty(nameof(M.MinAlignAngle));
                Width = serializedObject.FindProperty(nameof(M.width));

                StoppingDistance = serializedObject.FindProperty("StoppingDistance");
                SlowingLimit = serializedObject.FindProperty("SlowingLimit");
                SlowingDistance = serializedObject.FindProperty("SlowingDistance");
                DebugColor = serializedObject.FindProperty("DebugColor");
                UseDirection = serializedObject.FindProperty("UseDirection");
                SwapYZ = serializedObject.FindProperty(nameof(M.SwapYZ));
                UseAnimRotation = serializedObject.FindProperty(nameof(M.UseAnimRotation));

                ReactionsConditions = serializedObject.FindProperty(nameof(M.ReactionsConditions));
                BiDirectional = serializedObject.FindProperty(nameof(M.BiDirectional));
                TimeInOffMesh = serializedObject.FindProperty(nameof(M.TimeInOffMesh));

                AlignToLinkFromStart = serializedObject.FindProperty(nameof(M.AlignToLinkFromStart));
                AlignToLinkFromEnd = serializedObject.FindProperty(nameof(M.AlignToLinkFromEnd));

                AlignTime = serializedObject.FindProperty("AlignTime");
                InputAxiStart = serializedObject.FindProperty(nameof(M.InputAxisFromStart));
                InputAxiEnd = serializedObject.FindProperty(nameof(M.InputAxisFromEnd));
                gizmoSize = serializedObject.FindProperty(nameof(M.gizmoSize));
            }
            public override void OnInspectorGUI()
            {
                //base.OnInspectorGUI();
                serializedObject.Update();

                MalbersEditor.DrawDescription("Uses Animal reactions to move the Agent when its at a OffMeshLinks");
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(StoppingDistance);
                    EditorGUILayout.PropertyField(DebugColor, GUIContent.none, GUILayout.Width(50));
                    MalbersEditor.DrawDebugIcon(debug);
                    EditorGUIUtility.labelWidth = 10;
                    EditorGUILayout.PropertyField(gizmoSize, new GUIContent("G", "Gizmo Size"), GUILayout.Width(50));
                    EditorGUIUtility.labelWidth = 0;

                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.PropertyField(SlowingDistance);
                    EditorGUILayout.PropertyField(SlowingLimit);
                    EditorGUILayout.PropertyField(TimeInOffMesh);
                    EditorGUILayout.PropertyField(BiDirectional);
                }
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(UseDirection);
                    if (!UseDirection.boolValue)
                    {
                        EditorGUILayout.PropertyField(InputAxiStart);
                        if (BiDirectional.boolValue)
                            EditorGUILayout.PropertyField(InputAxiEnd);
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(LinkDirection);
                        EditorGUILayout.PropertyField(SwapYZ);
                    }
                }

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(AlignTime);

                    if (AlignTime.floatValue > 0.0f)
                    {
                        EditorGUILayout.PropertyField(Start);
                        EditorGUILayout.PropertyField(End);
                        EditorGUILayout.PropertyField(Width);

                        EditorGUILayout.PropertyField(AlignToLinkFromStart, new GUIContent("Align to Link Start (Pos)"));

                        if (BiDirectional.boolValue)
                            EditorGUILayout.PropertyField(AlignToLinkFromEnd, new GUIContent("Align to Link End (Pos)"));

                        EditorGUILayout.PropertyField(UseAnimRotation);
                        if (UseAnimRotation.boolValue)
                            EditorGUILayout.PropertyField(MinAlignAngle);

                        using (new EditorGUI.DisabledGroupScope(UseAnimRotation.boolValue))
                            EditorGUILayout.PropertyField(AlignRotation);
                    }
                }

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUI.indentLevel++;

                    EditorGUILayout.PropertyField(ReactionsConditions);
                    EditorGUILayout.PropertyField(StartReaction);
                    //EditorGUILayout.Space();
                    MalbersEditor.DrawSplitter();
                    EditorGUILayout.PropertyField(EndReaction);
                    EditorGUI.indentLevel--;
                }
                serializedObject.ApplyModifiedProperties();
            }

            void OnSceneGUI()
            {
                if (!debug.boolValue) return;

                using (var cc = new EditorGUI.ChangeCheckScope())
                {
                    if (M.start && M.start != M.transform)
                    {
                        var start = M.start.position;
                        start = Handles.PositionHandle(start, M.transform.rotation);

                        if (cc.changed)
                        {
                            Undo.RecordObject(M.start, "Move Start AI Link");
                            M.start.position = start;
                        }
                    }
                }

                using (var cc = new EditorGUI.ChangeCheckScope())
                {
                    if (M.end && M.end != M.transform)
                    {
                        var end = M.end.position;
                        end = Handles.PositionHandle(end, M.transform.rotation);

                        if (cc.changed)
                        {
                            Undo.RecordObject(M.end, "Move End AI Link");
                            M.end.position = end;
                        }
                    }
                }
            }
        }
#endif
    }
}