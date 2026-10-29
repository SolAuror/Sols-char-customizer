// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
#endif

namespace MalbersAnimations.Conditions
{
    [System.Serializable, AddTypeMenu("General/Check Angle from Origin")]
    public class C2_AngleFromOrigin : ConditionCore
    {
        public override string DynamicName =>
            $"Angle From: [Origin {Direction}] to [Target]. ({fromAngle.Value} < ?? < {toAngle.Value})";

        protected override void _SetTarget(Object target) => Target.Value = MTools.VerifyComponent(target, Target.Value);
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target?.Value != null;

        [Hide(nameof(LocalTarget))]
        public GameObjectReference Target = new();

        [Tooltip("Target to check for the condition")]
        public GameObjectReference Origin = new();
        public AxisDirection Direction = AxisDirection.Forward;
        [Tooltip("Get the Local Axis from a GameObject. Leave empty World Axis")]
        public AxisDirection UpAxis = AxisDirection.Up;

        public FloatReference fromAngle = new(-60);
        public FloatReference toAngle = new(60);

        protected override bool _Evaluate()
        {
            var from = MTools.TransformDirVector(Origin.transform, Direction);
            if (Origin.Value == null || Target.Value == null) { Debug.Log("Origin Missing in Condition", Target); return false; }
            var to = Origin.Value.transform.DirectionTo(Target.transform.position);
            var axis = MTools.TransformDirVector(Origin.transform, UpAxis);

            var angle = Vector3.SignedAngle(from, to, axis);

            var FromAnglePos = Quaternion.AngleAxis(fromAngle, axis) * from;
            var FromAngleNeg = Quaternion.AngleAxis(toAngle, axis) * from;

            MDebug.DrawRay(Origin.transform.position, FromAnglePos * 2, Color.yellow, 2f);
            MDebug.DrawRay(Origin.transform.position, FromAngleNeg * 2, Color.yellow, 2f);

            //draw the axis using the Angle.Value
            MDebug.DrawRay(Origin.Value.transform.position, axis.normalized * 2, Color.blue, 2f);

            var MinAngle = Mathf.Min(fromAngle, toAngle.Value);
            var MaxAngle = Mathf.Max(fromAngle, toAngle.Value);

            bool result = angle >= MinAngle && angle <= MaxAngle;

            MDebug.DrawRay(Origin.Value.transform.position, to * 2, result ? Color.green : Color.red, 2f);

            return result;
        }


        /// <summary>
        /// To make this work you need to call this method the ONDrawGizmos on any Monobehaviour that has conditions
        /// </summary>
        /// <param name="target"></param>

        public override void DrawGizmos(Component target)
        {
#if UNITY_EDITOR
            var from = MTools.TransformDirVector(Origin.transform, Direction);
            //  var to = Origin.Value.transform.DirectionTo(Target.transform.position);
            var axis = MTools.TransformDirVector(Origin.transform, UpAxis);

            var FromAnglePos = Quaternion.AngleAxis(fromAngle, axis) * from;
            var FromAngleNeg = Quaternion.AngleAxis(toAngle, axis) * from;

            var angle = Vector3.SignedAngle(FromAnglePos, FromAngleNeg, axis);

            if (angle != 360)
            {
                var Direction = MTools.TransformDirVector(target.transform, this.Direction);

                Handles.color = new Color(0, 1, 0, 0.1f);

                Handles.DrawSolidArc(Origin.transform.position, axis, FromAnglePos, angle, target.transform.localScale.y);
                Handles.color = Color.green;
                Handles.DrawWireArc(Origin.transform.position, axis, FromAnglePos, angle, target.transform.localScale.y);
            }
#endif
        }
    }
}
