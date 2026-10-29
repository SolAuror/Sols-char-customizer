using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    [AddComponentMenu("Malbers/Utilities/Gizmos/Gizmo Visualizer")]

    public class GizmoVisualizer : MonoBehaviour
    {
#if UNITY_EDITOR
        public enum GizmoType
        {
            Cube,
            Sphere,
            Rect,
            Cylinder
        }
        public bool UseColliders;
        public GizmoType gizmoType;

        [Min(0)] public float debugSize = 0.03f;
        [Min(0)] public float height = 2f;

        public Color DebugColor = Color.blue;
        public bool DrawAxis;
        [Min(0)] public float AxisSize = 0.65f;

        [SerializeField] private Collider[] _colliders;

#pragma warning disable CS0414
        [SerializeField] private bool HasColliders;
#pragma warning restore CS0414


        public bool DrawLineTo;
        [Hide(nameof(DrawLineTo))]
        public Transform ConnectTo;

        public Vector3 Rect = Vector3.one;
        public Vector3 Center = Vector3.zero;

        //public StatModifier modifier;


        [ContextMenu("Get Gizmo Color")]
        private void GetGizmoColor()
        {
            Debug.Log($"{name}: GizmoColor: {DebugColor}");
        }

        private void Reset()
        {
            FindColliders();
            DebugColor = new Color(Random.Range(0f, 1f), Random.Range(0f, 1f), Random.Range(0f, 1f), Random.Range(0f, 1f));
        }

        [ContextMenu("Find Colliders")]
        internal void FindColliders()
        {
            _colliders = gameObject.GetComponents<Collider>();

            if (_colliders != null && _colliders.Length > 0)
            {
                UseColliders = true;
                HasColliders = true;
            }
        }


        [SerializeField] private bool FirstSearch = false;

        private void OnValidate()
        {
            if (!FirstSearch)
            {
                FindColliders();
                FirstSearch = true;
                MTools.SetDirty(this);
            }
        }


#if UNITY_EDITOR && MALBERS_DEBUG
        private void OnDrawGizmos()
        {
            if (!UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(this)) return;

            if (!enabled) return;

            var DebugColorWire = new Color(DebugColor.r, DebugColor.g, DebugColor.b, 1);


            Gizmos.color = DebugColorWire;

            if (DrawAxis)
            {
                Handles.color = DebugColor;
                Handles.ArrowHandleCap(0, transform.position, transform.rotation, AxisSize, EventType.Repaint);
            }

            if (DrawLineTo && ConnectTo)
            {
                Gizmos.color = DebugColor;
                // Handles.color = DebugColorWire;
                MDebug.DrawLine(transform.position, ConnectTo.position, 2);
                //Handles.DrawDottedLine(transform.position, ConnectTo.position, 5);
            }

            Gizmos.matrix = transform.localToWorldMatrix;

            if (HasColliders && UseColliders)
            {
                UsesColliders(false);
                return;
            }

            switch (gizmoType)
            {
                case GizmoType.Cube:
                    Gizmos.color = DebugColorWire;
                    Gizmos.DrawWireCube(Center * debugSize, Vector3.one * debugSize);
                    Gizmos.color = DebugColor;
                    Gizmos.DrawCube(Center * debugSize, Vector3.one * debugSize);
                    break;
                case GizmoType.Sphere:
                    Gizmos.color = DebugColorWire;
                    Gizmos.DrawWireSphere(Center * debugSize, debugSize);
                    Gizmos.color = DebugColor;
                    Gizmos.DrawSphere(Center * debugSize, debugSize);
                    break;
                case GizmoType.Rect:
                    Gizmos.color = DebugColorWire;
                    Gizmos.DrawWireCube(Center * debugSize, Rect * debugSize);
                    Gizmos.color = DebugColor;
                    Gizmos.DrawCube(Center * debugSize, Rect * debugSize);
                    break;
                case GizmoType.Cylinder:
                    MDebug.GizmoCylinder(transform, ref _cylinderMesh, Center * debugSize, debugSize * 0.5f, height, DebugColor, 32, 4);
                    break;
                default:
                    break;
            }

        }

        private Mesh _cylinderMesh;

        private void OnDrawGizmosSelected()
        {
            if (!enabled) return;
            Gizmos.color = new Color(1, 1, 0, 1);
            Gizmos.matrix = transform.localToWorldMatrix;

            if (HasColliders && UseColliders)
            {
                UsesColliders(true);
                return;
            }


            switch (gizmoType)
            {
                case GizmoType.Cube:
                    Gizmos.DrawWireCube(Center * debugSize, Vector3.one * debugSize);
                    break;
                case GizmoType.Sphere:
                    Gizmos.DrawWireSphere(Center * debugSize, debugSize);
                    break;
                case GizmoType.Rect:
                    Gizmos.DrawWireCube(Center * debugSize, Rect * debugSize);
                    break;
                case GizmoType.Cylinder:
                    MDebug.GizmoCylinderWire(transform, Center * debugSize, debugSize * 0.5f, height, Color.yellow, 32, 4);
                    break;
            }
        }
#endif

        [SerializeField] private Mesh CapsuleMesh;
        private void UsesColliders(bool sel)
        {
            var DebugColorWire = new Color(DebugColor.r, DebugColor.g, DebugColor.b, 1);
            if (sel) DebugColorWire = Color.yellow;

            foreach (var _Collider in _colliders)
            {
                if (!_Collider) continue;
                if (_Collider is BoxCollider _box)
                {
                    if (!_box.enabled) continue;

                    Gizmos.matrix = transform.localToWorldMatrix;

                    var pos = _box.center;
                    var sca = _box.size;

                    Gizmos.color = DebugColorWire;

                    Gizmos.DrawWireCube(pos, sca);

                    if (!sel)
                    {
                        Gizmos.color = DebugColor;
                        Gizmos.DrawCube(pos, sca);
                    }
                }
                else if (_Collider is SphereCollider _sphere)
                {
                    if (!_sphere.enabled) continue;

                    Gizmos.matrix = transform.localToWorldMatrix;

                    Gizmos.color = DebugColorWire;
                    Gizmos.DrawWireSphere(_sphere.center, _sphere.radius);

                    if (!sel)
                    {
                        Gizmos.color = DebugColor;
                        Gizmos.DrawSphere(_sphere.center, _sphere.radius);
                    }
                }
                else if (_Collider is CapsuleCollider _capsule)
                {
                    if (!_capsule.enabled) continue;

                    Gizmos.matrix = transform.localToWorldMatrix;
                    MDebug.GizmoCapsule(_capsule.center, Quaternion.identity, _capsule.height, _capsule.radius, DebugColorWire, _capsule.direction);
                    MDebug.GizmoCapsuleMesh(ref CapsuleMesh, _capsule.center, Quaternion.identity, _capsule.height, _capsule.radius, DebugColor, _capsule.direction);
                }
                else if (_Collider is MeshCollider meshCol)
                {
                    if (!meshCol.enabled || meshCol.sharedMesh == null) continue;

                    Gizmos.matrix = transform.localToWorldMatrix;

                    Gizmos.color = DebugColorWire;
                    Gizmos.DrawWireMesh(meshCol.sharedMesh);

                    if (!sel)
                    {
                        Gizmos.color = DebugColor;
                        Gizmos.DrawMesh(meshCol.sharedMesh);
                    }
                }
            }
        }
#endif
    }


#if UNITY_EDITOR
    [CustomEditor(typeof(GizmoVisualizer)), CanEditMultipleObjects]
    public class GizmoVisualizerEditor : Editor
    {

        private SerializedProperty UseColliders, gizmoType, debugSize, DebugColor, DrawAxis, AxisSize, DrawLineTo, ConnectTo, Rect, height, Center;

        protected virtual void OnEnable()
        {
            UseColliders = serializedObject.FindProperty("UseColliders");
            gizmoType = serializedObject.FindProperty("gizmoType");
            debugSize = serializedObject.FindProperty("debugSize");
            DebugColor = serializedObject.FindProperty("DebugColor");
            DrawAxis = serializedObject.FindProperty("DrawAxis");
            AxisSize = serializedObject.FindProperty("AxisSize");
            DrawLineTo = serializedObject.FindProperty("DrawLineTo");
            ConnectTo = serializedObject.FindProperty("ConnectTo");
            Rect = serializedObject.FindProperty("Rect");
            Center = serializedObject.FindProperty("Center");
            height = serializedObject.FindProperty("height");
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))

            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(UseColliders);

                    //add a button to find colliders
                    if (GUILayout.Button("Find", GUILayout.Width(40)))
                    {
                        foreach (var target in targets)
                        {
                            if (target is GizmoVisualizer gizmoVisualizer)
                            {
                                gizmoVisualizer.FindColliders();
                                EditorUtility.SetDirty(gizmoVisualizer);
                            }
                        }
                    }
                    EditorGUILayout.PropertyField(DebugColor, GUIContent.none, GUILayout.MaxWidth(60));
                }

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(DrawAxis);
                    if (DrawAxis.boolValue)
                    {
                        EditorGUIUtility.labelWidth = 35;
                        EditorGUILayout.PropertyField(AxisSize, new GUIContent("Size"), GUILayout.MaxWidth(100), GUILayout.MinWidth(70));
                        EditorGUIUtility.labelWidth = 0;
                    }
                }

                if (!UseColliders.boolValue)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(gizmoType);
                        EditorGUIUtility.labelWidth = 35;
                        if (gizmoType.enumValueIndex == (int)GizmoVisualizer.GizmoType.Cylinder)
                        {
                            EditorGUILayout.PropertyField(height, new GUIContent("Height"), GUILayout.MaxWidth(100), GUILayout.MinWidth(70));
                            EditorGUILayout.PropertyField(debugSize, new GUIContent("Radius"), GUILayout.MaxWidth(100), GUILayout.MinWidth(70));
                        }
                        else
                        {
                            EditorGUILayout.PropertyField(debugSize, new GUIContent("Size"), GUILayout.MaxWidth(100), GUILayout.MinWidth(70));
                        }
                        EditorGUIUtility.labelWidth = 0;
                    }
                    if (gizmoType.enumValueIndex == 2)
                    {
                        EditorGUILayout.PropertyField(Rect);
                    }
                    EditorGUILayout.PropertyField(Center);
                }




                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(DrawLineTo);
                    EditorGUILayout.PropertyField(ConnectTo, GUIContent.none);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}