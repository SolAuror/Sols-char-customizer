// Last Change: 9/18/2026 1:20pm
using MalbersAnimations.Events;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/main-components/ai/ai-target")]
    /// <summary>
    /// This Script allows to set a gameObject as a Target for the Ai Logic. 
    /// So when an AI Animal sets a gameObject holding this script  as a target, 
    /// it can have a stopping distance and it can stop on a properly distance.
    /// </summary>
    [AddComponentMenu("Malbers/AI/AI Target"), SelectionBase]
    public class AITarget : MonoBehaviour, IAITargeterTarget, IAITarget
    {
        public WayPointType pointType = WayPointType.Ground;

        public ICharacterAction character;

        [Tooltip("Default Height for the Waypoints")]
        [SerializeField][Min(0)] private float m_height = 1.5f;

        [Tooltip("Distance for AI driven animals to stop when arriving to this gameobject. When is set as the AI Target.")]
        [FormerlySerializedAs("stoppingDistance")]
        [Min(0)] public float radius = 0.5f;

        [Tooltip("Distance for AI driven animals to start slowing its speed when arriving to this gameobject. If its set to zero or lesser than the Stopping distance, the Slowing Movement Logic will be ignored")]
        [Min(0)] public float slowingDistance = 0;

        [Tooltip("Offset to correct the Position of the Target")]
        [SerializeField] private Vector3 center;

        [Tooltip(" When the AI  arrives to this target, The character will rotate in place to  looks at the center of the AI Target?")]
        [SerializeField] private bool m_arriveLookAt = true;

        [Min(0), Tooltip("How many AI character can target this gameObject at the same time. Zero means infinite targets")]
        public int m_TargetLimit = 0;

        [Hide(nameof(m_TargetLimit), true)]
        [Tooltip("Distance the AI has to wait if all the spots on this Target are occupied")]
        [Min(0)] public float m_WaitTargeterDistance = 4f;
        public int Targeters// { get; set; }
        {
            get => targeters;
            set
            {
                targeters = value;
                FullTargeters = m_TargetLimit > 0 && value >= m_TargetLimit; //Zero = unlimited, so it is never "full"
            }
        }

        /// <summary>How many AI can target this object at once (0 = unlimited). Changing it at runtime re-seats the current Targeters</summary>
        public int TargetsLimits
        {
            get => m_TargetLimit;
            set
            {
                if (m_TargetLimit == value) return;
                m_TargetLimit = Mathf.Max(0, value);
                if (Application.isPlaying) RebuildTargeters();
            }
        }

        /// <summary> The AI TARGET HAS ALL THE TARGETERs Occupied  </summary>
        public bool FullTargeters { get; set; }

        private int targeters;

        /// <summary>Current Queues </summary>
        public IAIControl[] TargetersObjects;
        public List<IAIControl> TargetersWaiting;

        public bool ArriveLookAt => m_arriveLookAt;
        public float Height => m_height * transform.localScale.y;

        //Event to listen when the Target Type of waypoint has changed
        public System.Action<WayPointType> TargetTypeChanged { get; set; }

        /// <summary>There's a new AI using this target so Refresh </summary>
        public UnityEvent TargetersRefresh { get; set; }

        public Color gizmoColor = new Color(1f, 0f, 0f, 0.4f);
        public WayPointType TargetType
        {
            set
            {
                if (pointType != value)
                {
                    pointType = value;
                    TargetTypeChanged?.Invoke(value);
                }
            }
            get
            {
                return pointType;
            }
        }

        [Space]
        public GameObjectEvent OnTargetArrived = new();

        /// <summary>Center of the Animal to be used for AI and Targeting  </summary>
        public Vector3 Center
        {
            private set => center = value;
            get => transform.TransformPoint(center);
        }

        public float WaitTargeterDistance
        {
            private set => m_WaitTargeterDistance = value;
            get => m_WaitTargeterDistance + StopDistance();
        }

        protected virtual void OnEnable()
        {
            character = this.FindInterface<ICharacterAction>();

            //Allocated once. Re-creating the buffers on every enable (the old code) orphaned every AI that was still
            //registered after a disable/enable cycle: [Targeters] kept the old count on empty slots, so new AI could
            //register past the limit and the queued ones were never promoted (nobody held them any more).
            EnsureTargeterBuffers();

            if (character != null) character.OnState += (OnStateChanged);

            TargetersRefresh ??= new();
        }

        private void OnDisable()
        {
            if (character != null) character.OnState -= (OnStateChanged);

            ////Clear Every Targeter??
            //foreach (var ai in TargetersObjects)
            //{
            //    if (ai.IsAITarget.transform == transform) ai.ClearTarget();
            //    ai.Index = -1;
            //}
            //TargetersRefresh.Invoke();

            //TargetersObjects = new();
        }


        /// <summary> Listen if the character is swimming, flying, or underwater  </summary>
        private void OnStateChanged(int state)
        {
            if (state == StateEnum.UnderWater) TargetType = WayPointType.Underwater;
            else if (state == StateEnum.Fly) TargetType = WayPointType.Air;
            else if (state == StateEnum.Swim) TargetType = WayPointType.Water;
            else
            {
                TargetType = WayPointType.Ground;
            }
        }

        public void TargetArrived(GameObject target) => OnTargetArrived.Invoke(target);

        public void SetLocalCenter(Vector3 localCenter) => center = localCenter;

        /// <summary>Get the center of the AI Target</summary>
        public virtual Vector3 GetCenterPosition(int index) => TargeterPosition(index);

        public virtual Vector3 GetCenterPosition() => Center;

        /// <summary>Get the center of the AI Target plus the Height value</summary>
        public virtual Vector3 GetCenterY() => Center + (transform.up * Height);

        public float StopDistance() => radius * transform.localScale.y; //IMPORTANT For Scaled objects like the ball
        public float SlowDistance() => slowingDistance * transform.localScale.y; //IMPORTANT For Scaled objects like the ball


        public virtual void AddTargeter(IAIControl targeter)
        {
            if (TargetsLimits == 0) return;

            bool FoundSpace = false;

            //Find the first space 
            for (int i = 0; i < TargetsLimits; i++)
            {
                if (TargetersObjects[i] == targeter) //We have found ourselves so skip everything Do not do anything if we are already here
                {
                    return;
                }

                if (TargetersObjects[i] == null)
                {
                    TargetersObjects[i] = targeter;
                    targeter.Index = i;
                    FoundSpace = true;
                    Targeters++;
                    break;
                }
            }

            //Meaning all spaces were occupied
            if (!FoundSpace)
            {
                if (TargetersWaiting.Contains(targeter)) return; //Already queued: nothing changed, do not make every Targeter re-path

                TargetersWaiting.Add(targeter); //Add the waiting Target to the wait list
                targeter.IsWaitingOnTarget = true;
                targeter.Index = Targeters + TargetersWaiting.Count - 1; //Set Index to the waiting ones
            }

            TargetersRefresh.Invoke(); //BroadCast that the Amount of targeters has changed
        }

        public virtual void RemoveTargeter(IAIControl targeter)
        {
            if (TargetsLimits == 0) return;

            int index = Array.IndexOf(TargetersObjects, targeter); //Find if the target was added

            //Debug.Log($"Remove Targeter {targeter.Transform}");

            if (index >= 0)
            {
                //SET again the first one 
                if (TargetersWaiting.Count > 0)
                {
                    TargetersObjects[index] = TargetersWaiting[0];
                    TargetersObjects[index].Index = index;

                    TargetersWaiting[0].IsWaitingOnTarget = false;
                    TargetersWaiting.RemoveAt(0);
                }
                else
                {
                    //Clear Remove
                    TargetersObjects[index].Index = -1;
                    TargetersObjects[index] = null;
                    Targeters--;
                }
            }
            else
            {
                //The Targeter was on the Wait list so remove it because it went to another Target
                if (!TargetersWaiting.Remove(targeter)) return; //Unknown Targeter: nothing changed, do not make every Targeter re-path

                targeter.IsWaitingOnTarget = false;
            }

            TargetersRefresh.Invoke(); //BroadCast that the Amount of targeters has changed
        }

        /// <summary>Creates the slot and waiting buffers once, or when the limit no longer matches them. Never clears live registrations</summary>
        private void EnsureTargeterBuffers()
        {
            if (TargetsLimits == 0) return;

            if (TargetersObjects == null || TargetersObjects.Length != TargetsLimits)
            {
                TargetersObjects = new IAIControl[TargetsLimits]; //Set the Limit on the Targeter Objects
                Targeters = 0;
            }

            TargetersWaiting ??= new(); //Set the Waiting Objects
        }

        /// <summary>Re-seats every registered Targeter after the limit changed at runtime: the first ones keep a slot, the rest go back to the waiting list</summary>
        private void RebuildTargeters()
        {
            var current = new List<IAIControl>();

            if (TargetersObjects != null)
                foreach (var t in TargetersObjects) if (t != null) current.Add(t);

            if (TargetersWaiting != null) current.AddRange(TargetersWaiting);

            TargetersObjects = null;
            TargetersWaiting?.Clear();
            Targeters = 0;

            if (TargetsLimits == 0)
            {
                foreach (var t in current) { t.IsWaitingOnTarget = false; t.Index = -1; } //Unlimited now: nobody waits
                TargetersRefresh?.Invoke();
                return;
            }

            EnsureTargeterBuffers();

            foreach (var t in current)
            {
                t.IsWaitingOnTarget = false;
                AddTargeter(t); //Re-seats it and broadcasts the refresh
            }
        }

        /// <summary>  Calculates the position of the Targeter by its index </summary>
        /// <param name="index"></param>
        private Vector3 TargeterPosition(int index)
        {
            if (TargetsLimits == 0 || Targeters == 1) return Center;

            if (index > TargetsLimits - 1) return Center; //Skip for all the other targeters that are outside the limit

            var total = Mathf.Min(Targeters, TargetsLimits);

            float arcDegree = 360.0f / total;
            var rot = arcDegree * index;

            if (float.IsNaN(rot) || float.IsInfinity(rot)) rot = 0; //Weird bug

            // Debug.Log($"rot: {rot}");

            Quaternion rotation = Quaternion.Euler(0, rot, 0);

            Vector3 currentDirection = Vector3.forward;
            currentDirection = rotation * currentDirection;

            Vector3 CurrentPoint = Center + (currentDirection * StopDistance());
            return CurrentPoint;
        }

        public bool TargeterIsWaiting(int index)
        {
            if (TargetsLimits == 0) { return false; }
            return (index > TargetsLimits - 1);
        }

        public float GetTargeterStoppingDistance(int index)
        {
            if (TargetsLimits == 0 || Targeters == 1) return StopDistance();

            //Sent the Wait Target Distance for the Outside waiting ones
            return index > TargetsLimits - 1 ? WaitTargeterDistance : StopDistance();
        }
        public void SetGrounded() => TargetType = WayPointType.Ground;
        public void SetAir() => TargetType = WayPointType.Air;
        public void SetWater() => TargetType = WayPointType.Water;


#if UNITY_EDITOR && MALBERS_DEBUG

        private void OnDrawGizmos()
        {
            if (!UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(this)) return;


            UnityEditor.Handles.color = gizmoColor;
            UnityEditor.Handles.DrawWireDisc(Center, transform.up, radius * transform.localScale.y);
        }

        private void OnDrawGizmosSelected()
        {
            if (!UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(this)) return;

            StopDistanceGizmo(gizmoColor);

            if (radius < slowingDistance)
            {
                UnityEditor.Handles.color = Color.cyan;
                UnityEditor.Handles.DrawWireDisc(Center, transform.up, slowingDistance * transform.localScale.y);
            }

            // var limit = Application.isPlaying ? Targeters : m_TargetLimit;

            if (Application.isPlaying)
            {
                for (int i = 0; i < Targeters; i++)
                {
                    var CurrentPoint = TargeterPosition(i);
                    Gizmos.DrawWireSphere(CurrentPoint, StopDistance());
                }
            }

            if (TargetsLimits > 0 && m_WaitTargeterDistance > 0)
            {
                UnityEditor.Handles.color = (Color.yellow + Color.white) / 2;
                UnityEditor.Handles.DrawWireDisc(Center, transform.up, (radius + m_WaitTargeterDistance) * transform.localScale.y);
            }
        }

        private Mesh _mesh;

        private void StopDistanceGizmo(Color color)
        {
            var matrix = Gizmos.matrix;

            MDebug.GizmoCylinder(transform, ref _mesh, center + new Vector3(0, 0.001f, 0), radius, Height, color);
            MDebug.GizmoCylinderWire(transform, center + new Vector3(0, 0.001f, 0), radius, Height, Color.yellow);

            Gizmos.matrix = matrix;

            color.a = 1;

            //Draw Stopping Distance
            UnityEditor.Handles.color = Gizmos.color = color;

            //Draw Height
            Gizmos.DrawRay(Center, transform.up * Height);
            UnityEditor.Handles.DrawWireDisc(Center, transform.up, radius * transform.localScale.y * 0.1f);
            Gizmos.DrawWireSphere(Center + transform.up * Height, radius * 0.1f);
        }
#endif
    }


#if UNITY_EDITOR
    [CustomEditor(typeof(AITarget)), CanEditMultipleObjects]
    public class AITargetEditor : Editor
    {
        AITarget M;

        SerializedProperty
            m_height, pointType, radius, slowingDistance, center, gizmoColor,
            m_arriveLookAt, m_TargetLimit, m_targeterStopDistance, m_WaitTargeterDistance, OnTargetArrived;

        protected virtual void OnEnable()
        {
            M = target as AITarget;

            m_height = serializedObject.FindProperty("m_height");
            pointType = serializedObject.FindProperty("pointType");
            radius = serializedObject.FindProperty("radius");
            slowingDistance = serializedObject.FindProperty("slowingDistance");
            center = serializedObject.FindProperty("center");
            m_arriveLookAt = serializedObject.FindProperty("m_arriveLookAt");
            m_TargetLimit = serializedObject.FindProperty("m_TargetLimit");
            m_targeterStopDistance = serializedObject.FindProperty("m_targeterStopDistance");
            m_WaitTargeterDistance = serializedObject.FindProperty("m_WaitTargeterDistance");
            OnTargetArrived = serializedObject.FindProperty("OnTargetArrived");
            gizmoColor = serializedObject.FindProperty("gizmoColor");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(pointType);
                    EditorGUILayout.PropertyField(gizmoColor, GUIContent.none, GUILayout.Width(50));
                }


                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(radius);

                    EditorGUIUtility.labelWidth = 50;
                    EditorGUILayout.PropertyField(m_height, GUILayout.MinWidth(50));
                    EditorGUIUtility.labelWidth = 0;
                }

                EditorGUILayout.PropertyField(slowingDistance);
                EditorGUILayout.PropertyField(center);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(m_arriveLookAt);
                EditorGUILayout.PropertyField(m_TargetLimit);

                if (m_TargetLimit.intValue > 0)
                {
                    // EditorGUILayout.PropertyField(m_targeterStopDistance);
                    EditorGUILayout.PropertyField(m_WaitTargeterDistance);
                }
            }

            EditorGUILayout.PropertyField(OnTargetArrived);

            serializedObject.ApplyModifiedProperties();


            if (Application.isPlaying && M.TargetersObjects != null)
            {
                using (new EditorGUI.DisabledGroupScope(true))
                {
                    EditorGUILayout.IntField("Current Targeters", M.Targeters);

                    if (M.TargetersObjects != null && M.TargetersObjects.Length > 0)
                    {
                        for (int i = 0; i < M.TargetersObjects.Length; i++)
                        {
                            Transform value = M.TargetersObjects[i].IsUnityRefNull() ? null : M.TargetersObjects[i].Owner;

                            EditorGUILayout.ObjectField($"Targeter [{i}]", value, typeof(Transform), false);
                        }
                    }

                    if (M.TargetersWaiting != null && M.TargetersWaiting.Count > 0)
                    {
                        EditorGUILayout.IntField("Waiting Targeters", M.TargetersWaiting.Count);

                        for (int i = 0; i < M.TargetersWaiting.Count; i++)
                        {
                            Transform value = M.TargetersWaiting[i].IsUnityRefNull() ? null : M.TargetersWaiting[i].Owner;
                            EditorGUILayout.ObjectField($"Waiting Targeter [{i}]", value, typeof(Transform), false);
                        }
                    }
                }
            }


        }
    }
#endif
}