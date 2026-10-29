using UnityEngine;
using MalbersAnimations.Scriptables;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Utilities
{
    public enum RotationSource
    {
        Aligner,
        Target,
        WorldDirection
    }

    public enum WorldDirection
    {
        Forward,
        Right,
        Back,
        Left
    }

    [AddComponentMenu("Malbers/Utilities/Align/AlignerExtended")]
    [DisallowMultipleComponent]
    public sealed class AlignerExtended : Aligner
    {
        [Tooltip("Rotation source to use for alignment.")]
        public RotationSource rotationSource = RotationSource.Aligner;

        [Tooltip("If the Rotation Source is 'Target', the reference to take the direction from.")]
        public TransformReference rotationTarget = new();

        [Tooltip("Snaps the target direction to the nearest of the four cardinal directions.")]
        public bool useAbsoluteRotation = false;

        [Tooltip("Direction to use if the Rotation Source is 'WorldDirection'.")]
        public WorldDirection worldDirection = WorldDirection.Forward;

        [Tooltip(
            "Angle-based alignment gate. When enabled, the target aligns only when it is within the specified angle window.")]
        public bool UseAngleControl = false;

        [Tooltip("Angle window (0–360). 360 = unlimited, 180 = half-circle, 90 = quarter-circle.")]
        [Range(0f, 360f)]
        public float AngleRange = 180f;

        [Tooltip("Offset to the window’s center direction (degrees).")]
        [Range(-180f, 180f)]
        public float AngleDirection = 0f;

        [Tooltip("Shift the angle-control center on the XZ plane.")]
        public Vector2 AngleControlOffset = Vector2.zero;

        Transform _pivot;

        private void EnsurePivot()
        {
            if (_pivot != null)
            {
                return;
            }

            GameObject go = new("__AlignerExtended_Pivot__");
            go.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            _pivot = go.transform;
        }

        private static Vector3 ClosestCardinal(Vector3 v)
        {
            v.y = 0f;
            if (v.sqrMagnitude < 1e-6f)
            {
                return Vector3.forward;
            }

            v.Normalize();
            Vector3[] c = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            float best = 999f;
            Vector3 pick = Vector3.forward;
            for (int i = 0; i < c.Length; i++)
            {
                float a = Vector3.Angle(v, c[i]);
                if (a < best)
                {
                    best = a;
                    pick = c[i];
                }
            }

            return pick;
        }

        private Quaternion BuildBaseRotation(Transform target)
        {
            switch (rotationSource)
            {
                case RotationSource.Target:
                    {
                        Transform src = rotationTarget.Value;
                        Vector3 look;
                        if (src != null)
                        {
                            look = src.position - MainPoint.position;
                            look.y = 0f;
                            if (look.sqrMagnitude < 1e-6f)
                            {
                                look = MainPoint.forward;
                            }
                        }
                        else
                        {
                            look = MainPoint.forward;
                            look.y = 0f;
                            if (look.sqrMagnitude < 1e-6f)
                            {
                                look = Vector3.forward;
                            }
                        }

                        if (useAbsoluteRotation)
                        {
                            look = ClosestCardinal(look);
                        }

                        return Quaternion.LookRotation(look.normalized);
                    }

                case RotationSource.WorldDirection:
                    {
                        Vector3 dir = worldDirection switch
                        {
                            WorldDirection.Forward => Vector3.forward,
                            WorldDirection.Right => Vector3.right,
                            WorldDirection.Back => Vector3.back,
                            WorldDirection.Left => Vector3.left,
                            _ => Vector3.forward
                        };
                        return Quaternion.LookRotation(dir);
                    }

                case RotationSource.Aligner:
                default:
                    return MainPoint.rotation;
            }
        }

        private bool IsWithinAngleRange(Transform target)
        {
            if (!UseAngleControl || AngleRange >= 360f)
            {
                return true;
            }

            Vector3 localOffset = new(AngleControlOffset.x, 0f, AngleControlOffset.y);
            Vector3 center = MainPoint.position + MainPoint.TransformDirection(localOffset);

            Vector3 dir = target.position - center;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f)
            {
                return true;
            }

            dir.Normalize();

            Vector3 fwd = MainPoint.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
            {
                fwd = MainPoint.right;
                fwd.y = 0f;
            }

            fwd.Normalize();
            fwd = Quaternion.Euler(0f, AngleDirection, 0f) * fwd;

            float angle = Vector3.Angle(fwd, dir);
            return angle <= (AngleRange * 0.5f);
        }

        public override void Align(Transform TargetToAlign)
        {
            if (!Active || !MainPoint || !TargetToAlign)
            {
                return;
            }

            Vector3 checkCenter = MainPoint.position;
            if (UseAngleControl && AngleControlOffset.sqrMagnitude > 0.0001f)
            {
                Vector3 localOffset = new(AngleControlOffset.x, 0f, AngleControlOffset.y);
                checkCenter = MainPoint.position + MainPoint.TransformDirection(localOffset);
            }

            if (AlignMinDistance > 0f && Vector3.Distance(TargetToAlign.position, checkCenter) > AlignMinDistance)
            {
                return;
            }

            if (!IsWithinAngleRange(TargetToAlign))
            {
                return;
            }

            float oldMin = AlignMinDistance;
            AlignMinDistance = 0f;

            Transform oldMain = mainPoint.Value;
            bool swapped = false;

            try
            {
                if (AlignRot && (rotationSource != RotationSource.Aligner || useAbsoluteRotation))
                {
                    EnsurePivot();
                    _pivot.position = MainPoint.position;
                    _pivot.rotation = BuildBaseRotation(TargetToAlign);
                    mainPoint.Value = _pivot;
                    swapped = true;
                }

                base.Align(TargetToAlign);
            }
            finally
            {
                if (swapped)
                {
                    mainPoint.Value = oldMain;
                }

                AlignMinDistance = oldMin;
            }
        }

        private void OnDrawGizmos()
        {
            Color WireColor = new(DebugColor.r, DebugColor.g, DebugColor.b, 1f);

            if (!MainPoint)
            {
                return;
            }

            Gizmos.color = WireColor;
            Gizmos.DrawCube(MainPoint.position, Vector3.one * 0.05f);

#if UNITY_EDITOR

            if (AlignLookAt && LookAtRadius > 0f)
            {
                Handles.color = DebugColor;
                Handles.DrawWireDisc(MainPoint.position, transform.up, LookAtRadius);
            }

            if (UseAngleControl && AngleRange < 360f)
            {
                Vector3 localOffset = new(AngleControlOffset.x, 0, AngleControlOffset.y);
                Vector3 controlCenter = MainPoint.position + MainPoint.TransformDirection(localOffset);

                Vector3 forward = MainPoint.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f)
                {
                    forward = MainPoint.right;
                    forward.y = 0f;
                }

                forward.Normalize();

                forward = Quaternion.Euler(0, AngleDirection, 0) * forward;
                float halfAngle = AngleRange * 0.5f;
                Vector3 from = Quaternion.Euler(0, -halfAngle, 0) * forward;

                if (AlignMinDistance > 0f)
                {
                    Handles.color = new Color(1, 0, 0, 0.1f);
                    Handles.DrawSolidArc(controlCenter, MainPoint.up, from, AngleRange, AlignMinDistance);

                    Handles.color = Color.red;
                    Handles.DrawWireArc(controlCenter, MainPoint.up, from, AngleRange, AlignMinDistance);

                    Vector3 leftBoundary =
                        controlCenter + (Quaternion.Euler(0, -halfAngle, 0) * forward) * AlignMinDistance;
                    Vector3 rightBoundary =
                        controlCenter + (Quaternion.Euler(0, halfAngle, 0) * forward) * AlignMinDistance;
                    Handles.DrawLine(controlCenter, leftBoundary);
                    Handles.DrawLine(controlCenter, rightBoundary);

                    if (AngleControlOffset.sqrMagnitude > 0.01f)
                    {
                        Handles.color = new Color(1, 1, 0, 0.5f);
                        Handles.DrawDottedLine(MainPoint.position, controlCenter, 2f);
                        Gizmos.color = Color.yellow;
                        Gizmos.DrawWireSphere(controlCenter, 0.1f);
                    }
                }

                float previewDistance = AlignMinDistance > 0 ? AlignMinDistance : 2f;
                Handles.color = Color.yellow;
                Vector3 arrowEnd = controlCenter + forward * previewDistance * 0.8f;
                Handles.DrawLine(controlCenter, arrowEnd);
                Handles.ConeHandleCap(0, arrowEnd, Quaternion.LookRotation(forward), previewDistance * 0.1f,
                    EventType.Repaint);

                Handles.color = new Color(0, 1, 0, 0.5f);
                Vector3 leftDir = Quaternion.Euler(0, -halfAngle, 0) * forward;
                Vector3 rightDir = Quaternion.Euler(0, halfAngle, 0) * forward;
                Handles.DrawLine(controlCenter, controlCenter + leftDir * previewDistance * 0.6f);
                Handles.DrawLine(controlCenter, controlCenter + rightDir * previewDistance * 0.6f);

                Vector3 textPos = controlCenter + forward * previewDistance * 0.5f + Vector3.up * 0.2f;
                Handles.Label(textPos, $"{AngleRange}°", EditorStyles.whiteBoldLabel);
            }
            else if (AlignMinDistance > 0f)
            {
                Handles.color = Color.red;
                if (UseAngleControl && AngleControlOffset.sqrMagnitude > 0.01f)
                {
                    Vector3 localOffset = new(AngleControlOffset.x, 0, AngleControlOffset.y);
                    Vector3 controlCenter = MainPoint.position + MainPoint.TransformDirection(localOffset);
                    Handles.DrawWireDisc(controlCenter, transform.up, AlignMinDistance);

                    Handles.color = new Color(1, 1, 0, 0.5f);
                    Handles.DrawDottedLine(MainPoint.position, controlCenter, 2f);
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireSphere(controlCenter, 0.1f);
                }
                else
                {
                    Handles.DrawWireDisc(MainPoint.position, transform.up, AlignMinDistance);
                }
            }
#endif

            if (SecondPoint)
            {
                Gizmos.DrawLine(MainPoint.position, SecondPoint.position);
                Gizmos.DrawCube(SecondPoint.position, Vector3.one * 0.05f);

                if (DoubleSided)
                {
                    Vector3 p1 = transform.InverseTransformPoint(MainPoint.position);
                    Vector3 p2 = transform.InverseTransformPoint(SecondPoint.position);
                    p1.z *= -1;
                    p2.z *= -1;
                    p1 = transform.TransformPoint(p1);
                    p2 = transform.TransformPoint(p2);
                    Gizmos.DrawLine(p1, p2);
                    Gizmos.DrawCube(p1, Vector3.one * 0.05f);
                    Gizmos.DrawCube(p2, Vector3.one * 0.05f);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
#if UNITY_EDITOR
            if (!MainPoint)
            {
                return;
            }

            if (AlignLookAt && LookAtRadius > 0f)
            {
                Handles.color = new Color(1, 1, 0, 1);
                Handles.DrawWireDisc(MainPoint.position, transform.up, LookAtRadius);
            }

            if (UseAngleControl && AngleRange < 360f)
            {
                Vector3 localOffset = new(AngleControlOffset.x, 0, AngleControlOffset.y);
                Vector3 controlCenter = MainPoint.position + MainPoint.TransformDirection(localOffset);

                Vector3 forward = MainPoint.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f)
                {
                    forward = MainPoint.right;
                    forward.y = 0f;
                }

                forward.Normalize();

                forward = Quaternion.Euler(0, AngleDirection, 0) * forward;
                float halfAngle = AngleRange * 0.5f;
                float distance = AlignMinDistance > 0 ? AlignMinDistance : 3f;

                Handles.color = new Color(1, 0.5f, 0, 0.3f);
                Vector3 from = Quaternion.Euler(0, -halfAngle, 0) * forward;
                Handles.DrawSolidArc(controlCenter, MainPoint.up, from, AngleRange, distance);

                int markers = Mathf.Min((int)(AngleRange / 30f), 12);
                Handles.color = Color.white;
                for (int i = 0; i <= markers; i++)
                {
                    float a = -halfAngle + (AngleRange / markers) * i;
                    Vector3 dir = Quaternion.Euler(0, a, 0) * forward;
                    Vector3 s = controlCenter + dir * (distance * 0.95f);
                    Vector3 e = controlCenter + dir * (distance * 1.05f);
                    Handles.DrawLine(s, e);
                }

                Vector3 dirTextPos = controlCenter + forward * distance * 1.2f;
                Handles.Label(dirTextPos, $"Direction: {AngleDirection}°", EditorStyles.whiteLargeLabel);

                if (AngleControlOffset.sqrMagnitude > 0.01f)
                {
                    Vector3 offsetTextPos = controlCenter + Vector3.up * 0.5f;
                    Handles.Label(offsetTextPos, $"Offset: ({AngleControlOffset.x:F1}, {AngleControlOffset.y:F1})",
                        EditorStyles.whiteLabel);
                }
            }
#endif
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(AlignerExtended)), CanEditMultipleObjects]
    public class AlignerExtendedEditor : Editor
    {
        private SerializedProperty
            AlignPos,
            AlignRot,
            AlignLookAt,
            AlingPoint1,
            AlingPoint2,
            AlignTime,
            AlignCurve,
            AlignMinDistance,
            ignoreY,
            DoubleSided,
            LookAtRadius,
            DebugColor,
            AngleOffset,
            rotationSource,
            rotationTarget,
            useAbsoluteRotation,
            worldDirection,
            UseAngleControl,
            AngleRange,
            AngleDirection,
            AngleControlOffset;

        protected virtual void OnEnable()
        {
            AlignPos = serializedObject.FindProperty("AlignPos");
            AlignRot = serializedObject.FindProperty("AlignRot");
            AlignLookAt = serializedObject.FindProperty("AlignLookAt");
            AlingPoint1 = serializedObject.FindProperty("mainPoint");
            AlingPoint2 = serializedObject.FindProperty("secondPoint");
            AlignTime = serializedObject.FindProperty("AlignTime");
            AlignCurve = serializedObject.FindProperty("AlignCurve");
            AlignMinDistance = serializedObject.FindProperty("AlignMinDistance");
            ignoreY = serializedObject.FindProperty("ignoreY");
            DoubleSided = serializedObject.FindProperty("DoubleSided");
            LookAtRadius = serializedObject.FindProperty("LookAtRadius");
            DebugColor = serializedObject.FindProperty("DebugColor");
            AngleOffset = serializedObject.FindProperty("AngleOffset");

            rotationSource = serializedObject.FindProperty("rotationSource");
            rotationTarget = serializedObject.FindProperty("rotationTarget");
            useAbsoluteRotation = serializedObject.FindProperty("useAbsoluteRotation");
            worldDirection = serializedObject.FindProperty("worldDirection");

            UseAngleControl = serializedObject.FindProperty("UseAngleControl");
            AngleRange = serializedObject.FindProperty("AngleRange");
            AngleDirection = serializedObject.FindProperty("AngleDirection");
            AngleControlOffset = serializedObject.FindProperty("AngleControlOffset");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    Color cur = GUI.color;
                    Color on = (cur + Color.green) / 2f;

                    GUI.color = AlignPos.boolValue ? on : cur;
                    AlignPos.boolValue = GUILayout.Toggle(AlignPos.boolValue, new GUIContent("Position"),
                        EditorStyles.miniButton);

                    GUI.color = AlignRot.boolValue ? on : cur;
                    AlignRot.boolValue = GUILayout.Toggle(AlignRot.boolValue, new GUIContent("Rotation"),
                        EditorStyles.miniButton);
                    if (AlignPos.boolValue || AlignRot.boolValue)
                    {
                        AlignLookAt.boolValue = false;
                    }

                    GUI.color = AlignLookAt.boolValue ? on : cur;
                    AlignLookAt.boolValue = GUILayout.Toggle(AlignLookAt.boolValue, new GUIContent("Look At"),
                        EditorStyles.miniButton);
                    if (AlignLookAt.boolValue)
                    {
                        AlignPos.boolValue = AlignRot.boolValue = false;
                    }

                    GUI.color = cur;
                    EditorGUILayout.PropertyField(DebugColor, GUIContent.none, GUILayout.MaxWidth(40));
                }

                if (AlignRot.boolValue || AlignPos.boolValue)
                {
                    EditorGUILayout.PropertyField(DoubleSided, new GUIContent("Double Sided"));
                }

                if (AlignLookAt.boolValue)
                {
                    EditorGUILayout.PropertyField(LookAtRadius, new GUIContent("Radius"));
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(AlingPoint1, new GUIContent("Main Point"));
                if (AlignPos.boolValue)
                {
                    EditorGUILayout.PropertyField(AlingPoint2, new GUIContent("2nd Point"));
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(AlignTime, new GUIContent("Align Time"));
                    EditorGUILayout.PropertyField(AlignCurve, GUIContent.none, GUILayout.MaxWidth(90));
                }

                EditorGUILayout.PropertyField(AlignMinDistance, new GUIContent("Min Distance"));
                EditorGUILayout.PropertyField(ignoreY, new GUIContent("Ignore Y"));
                if (AlignRot.boolValue || AlignLookAt.boolValue)
                {
                    EditorGUILayout.PropertyField(AngleOffset);
                }
            }

            if (AlignRot.boolValue)
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(rotationSource);
                    EditorGUI.indentLevel++;
                    RotationSource src = (RotationSource)rotationSource.enumValueIndex;
                    if (src == RotationSource.Target)
                    {
                        EditorGUILayout.PropertyField(rotationTarget, new GUIContent("Rotation Target"));
                        EditorGUILayout.PropertyField(useAbsoluteRotation, new GUIContent("Absolute (Cardinal Snap)"));
                    }
                    else if (src == RotationSource.WorldDirection)
                    {
                        EditorGUILayout.PropertyField(worldDirection, new GUIContent("World Direction"));
                    }

                    EditorGUI.indentLevel--;
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(UseAngleControl, new GUIContent("Use Angle Control"));
                if (UseAngleControl.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(AngleRange, new GUIContent("Angle Range"));
                    EditorGUILayout.PropertyField(AngleDirection, new GUIContent("Angle Direction"));
                    EditorGUILayout.PropertyField(AngleControlOffset, new GUIContent("Center Offset (X,Z)"));
                    EditorGUI.indentLevel--;
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}