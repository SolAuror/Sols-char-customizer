using MalbersAnimations.Events;
using System.Collections.Generic;
using UnityEngine;
using MalbersAnimations.Reactions;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    [AddComponentMenu("Malbers/AI/Waypoint")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/main-components/ai/mwaypoint")]
    public class MWayPoint : MonoBehaviour, IWayPoint
    {
        public static List<MWayPoint> WayPoints;

        public WayPointType pointType = WayPointType.Ground;
        [Tooltip("Distance for AI driven animals to stop when arriving to this gameobject. When is set as the AI Target.")]
        [Min(0)] public float stoppingDistance = 1f;


        [Tooltip("Distance for AI driven animals to start slowing its speed when arriving to this gameobject. If its set to zero or lesser than the Stopping distance, the Slowing Movement Logic will be ignored")]
        [Min(0)] public float slowingDistance = 0;

        [Tooltip(" When the AI animal arrives to the target, do we Rotate the Animal so it looks at the center of the waypoint?")]
        [SerializeField] private bool m_arriveLookAt = false;

        [Tooltip("Default Height for the Waypoints")]
        [Min(0)][SerializeField] private float m_height = 0.5f;

        public float Height => m_height;

        [MinMaxRange(0, 60), Tooltip("Wait time range to go to the next destination")]
        public RangedFloat m_WaitTime = new(1, 5);

        public Color DebugColor = Color.red;
        public float WaitTime => m_WaitTime.RandomValue;

        public WayPointType TargetType => pointType;

        public virtual Vector3 GetCenterPosition(int Index) => transform.position;
        public virtual Vector3 GetCenterPosition() => transform.position;
        public Vector3 GetCenterY() => transform.position + transform.up * Height;
        public virtual float StopDistance() => stoppingDistance * transform.localScale.y; //IMPORTANT For Scaled objects like the ball
        public virtual float SlowDistance() => slowingDistance * transform.localScale.y; //IMPORTANT For Scaled objects like the ball

        public Transform WPTransform => base.transform;

        [SerializeField] protected List<Transform> nextWayPoints;
        public List<Transform> NextTargets { get => nextWayPoints; set => nextWayPoints = value; }

        public bool ArriveLookAt => m_arriveLookAt;

        [Space]
        public GameObjectEvent OnTargetArrived = new();

        public Reaction2 TargetArrivedReaction;


        protected virtual void OnEnable()
        {
            WayPoints ??= new List<MWayPoint>();
            WayPoints.Add(this);
        }

        protected virtual void OnDisable()
        {
            WayPoints.Remove(this);
        }

        public virtual void TargetArrived(GameObject target)
        {
            OnTargetArrived.Invoke(target);
            TargetArrivedReaction.React(target);
        }

        public virtual Transform NextTarget()
        {
            var next = NextTargets.Count > 0 ? NextTargets[UnityEngine.Random.Range(0, NextTargets.Count)] : null;

            if (next != null && !next.gameObject.activeInHierarchy) next = null; //Do not get the Next target if its deactive in hierarchy

            return next;
        }

        /// <summary>Returns a Random Waypoint from the Global WaypointList</summary>
        public static Transform GetWaypoint()
        {
            return (WayPoints != null && WayPoints.Count > 1) ? WayPoints[UnityEngine.Random.Range(0, WayPoints.Count)].WPTransform : null;
        }

        /// <summary>Returns a Random Waypoint from the Global WaypointList by its type (Ground, Air, Water)</summary>
        public static Transform GetWaypoint(WayPointType pointType)
        {
            if (WayPoints != null && WayPoints.Count > 1)
            {
                var MWayPoint = WayPoints.Find(item => item.pointType == pointType);

                return MWayPoint ? MWayPoint.WPTransform : null;
            }
            return null;
        }


        public int CurrentTargetLimit { get; set; }


        public float GetRadiusTargeter(int index)
        {
            return StopDistance();
        }

#if UNITY_EDITOR




        [ContextMenu("Connect to Zone")]
        void ConnectToWaypoint()
        {
            var method = this.GetUnityAction<GameObject>("Zone", "TargetArrived");
            if (method != null) UnityEditor.Events.UnityEventTools.AddPersistentListener(OnTargetArrived, method);
            MTools.SetDirty(this);
        }



        /// <summary>DebugOptions</summary>

        GUIStyle Styl;

        void OnDrawGizmos()
        {
            if (!enabled) return;
            if (!UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(this)) return;

            Gizmos.color = DebugColor;
            var sc = transform.localScale.y;



            if (pointType == WayPointType.Air)
            {
                Gizmos.DrawWireSphere(base.transform.position, stoppingDistance * sc);
                if (stoppingDistance < slowingDistance)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireSphere(base.transform.position, slowingDistance * sc);
                }
            }
            else
            {
                MDebug.GizmoCircle(base.transform.position, base.transform.rotation * Quaternion.Euler(90, 0, 0), stoppingDistance * sc, DebugColor);

                if (stoppingDistance < slowingDistance)
                {
                    MDebug.GizmoCircle(base.transform.position, base.transform.rotation * Quaternion.Euler(90, 0, 0), slowingDistance * sc, Color.cyan);
                }
            }

            Gizmos.color = DebugColor;

            Gizmos.DrawRay(transform.position, transform.up * (Height * sc));
            Gizmos.DrawWireSphere(transform.position, Height * 0.1f * sc);
            Gizmos.DrawWireSphere(transform.position + transform.up * (Height * sc), Height * 0.1f * sc);

            var col = DebugColor;
            col.a = 0.333f;
            Gizmos.color = col;
            Gizmos.DrawSphere(transform.position, Height * 0.1f * sc);
            Gizmos.DrawSphere(transform.position + transform.up * Height, Height * 0.1f * sc);


            col = Color.white;
            col.a = 0.2f;
            Gizmos.color = col;
            if (nextWayPoints != null)
            {
                foreach (var nw in nextWayPoints)
                {
                    if (nw) MDebug.DrawLine(transform.position, nw.position, 1);
                }
            }


            if (Styl == null)
            {
                Styl = new GUIStyle();
                Styl.normal.textColor = Color.white;
                Styl.fontStyle = FontStyle.Bold;
                Styl.alignment = TextAnchor.UpperCenter;
            }
            Handles.Label(transform.position + Vector3.up * 0.5f, name, Styl);
        }

        private void OnDrawGizmosSelected()
        {
            if (!enabled) return;
            if (!UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(this)) return;

            var sc = transform.localScale.y;

            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position, transform.up * (Height * sc));
            Gizmos.DrawWireSphere(transform.position, Height * 0.1f * sc);
            Gizmos.DrawWireSphere(transform.position + transform.up * (Height * sc), Height * 0.1f * sc);


            if (pointType == WayPointType.Air)
            {
                Gizmos.DrawWireSphere(base.transform.position, stoppingDistance * sc);
            }
            else
            {
                MDebug.GizmoCircle(base.transform.position, base.transform.rotation * Quaternion.Euler(90, 0, 0), stoppingDistance * sc, Color.yellow);
            }



            Gizmos.color = DebugColor;

            if (nextWayPoints != null)
            {
                foreach (var nw in nextWayPoints)
                {
                    if (nw)
                        MDebug.DrawLine(transform.position, nw.position, 3);
                }
            }
        }
#endif
    }


#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(MWayPoint)), CanEditMultipleObjects]
    public class MWayPointEditor : Editor
    {
        UnityEditor.SerializedProperty
            pointType, stoppingDistance, slowingDistance, WaitTime, nextWayPoints,
            //m_targetDistance, m_TargetLimit,
            DebugColor, OnTargetArrived, TargetArrivedReaction, m_height, m_arriveLookAt;

        MWayPoint M;

        private string[] uniquenames;


        private void OnEnable()
        {
            M = (MWayPoint)target;

            //Get all WP Names
#if UNITY_6000_4_OR_NEWER
            var allWP = FindObjectsByType<MWayPoint>(FindObjectsInactive.Exclude); //MWC: FindObjectsSortMode obsolete in Unity 6000.4+
#else
            var allWP = FindObjectsByType<MWayPoint>(FindObjectsSortMode.None);
#endif

            uniquenames = new string[allWP.Length];
            for (int i = 0; i < allWP.Length; i++) uniquenames[i] = allWP[i].name;


            pointType = serializedObject.FindProperty("pointType");
            stoppingDistance = serializedObject.FindProperty("stoppingDistance");
            slowingDistance = serializedObject.FindProperty("slowingDistance");
            WaitTime = serializedObject.FindProperty("m_WaitTime");
            nextWayPoints = serializedObject.FindProperty("nextWayPoints");
            DebugColor = serializedObject.FindProperty("DebugColor");
            OnTargetArrived = serializedObject.FindProperty("OnTargetArrived");
            m_height = serializedObject.FindProperty("m_height");
            m_arriveLookAt = serializedObject.FindProperty("m_arriveLookAt");
            TargetArrivedReaction = serializedObject.FindProperty("TargetArrivedReaction");
            //  m_targetDistance = serializedObject.FindProperty("m_targetDistance");
            // m_TargetLimit = serializedObject.FindProperty("m_TargetLimit");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Uses this Transform position as the destination point for AI Driven characters");
            //  EditorGUILayout.BeginVertical(MTools.StyleGray);
            {
                UnityEditor.EditorGUILayout.BeginVertical(UnityEditor.EditorStyles.helpBox);
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.PropertyField(pointType);
                    EditorGUILayout.PropertyField(DebugColor, GUIContent.none, GUILayout.Width(40));
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.PropertyField(m_height);
                    EditorGUILayout.PropertyField(m_arriveLookAt);
                    EditorGUILayout.PropertyField(stoppingDistance);
                    EditorGUILayout.PropertyField(slowingDistance);
                    // EditorGUILayout.PropertyField(m_TargetLimit);
                    // EditorGUILayout.PropertyField(m_targetDistance);
                    EditorGUILayout.PropertyField(WaitTime);
                }
                UnityEditor.EditorGUILayout.EndVertical();

                UnityEditor.EditorGUILayout.BeginVertical(UnityEditor.EditorStyles.helpBox);
                {
                    UnityEditor.EditorGUI.indentLevel++;
                    if (GUILayout.Button("Create Next Waypoint"))
                    {
                        var nextWayP = UnityEngine.Object.Instantiate(M.gameObject);
                        nextWayP.transform.position += M.gameObject.transform.forward * 2;
                        nextWayP.name = UnityEditor.ObjectNames.GetUniqueName(uniquenames, M.name);

                        System.Array.Resize(ref uniquenames, uniquenames.Length + 1);

                        uniquenames[uniquenames.Length - 1] = nextWayP.name;

                        if (M.NextTargets == null) M.NextTargets = new List<Transform>();

                        M.NextTargets.Add(nextWayP.transform);
                        nextWayPoints.serializedObject.ApplyModifiedProperties();

                        nextWayP.GetComponent<MWayPoint>().NextTargets = new List<Transform>(); //Clear the copied nextTargets
                        Selection.activeGameObject = nextWayP.gameObject;
                    }

                    UnityEditor.EditorGUILayout.PropertyField(nextWayPoints, true);
                    UnityEditor.EditorGUI.indentLevel--;
                }
                UnityEditor.EditorGUILayout.EndVertical();
                UnityEditor.EditorGUILayout.PropertyField(OnTargetArrived);
                UnityEditor.EditorGUILayout.PropertyField(TargetArrivedReaction);
            }
            // UnityEditor.EditorGUILayout.EndVertical();
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
