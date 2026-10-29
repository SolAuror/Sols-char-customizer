// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Conditions
{
    [System.Serializable, AddTypeMenu("General/Check Angle")]
    public class C2_Angle : ConditionCore
    {
        public override string DynamicName =>
            $"Angle Diff: [{(Target.Value ? Target.Value.name : "<None>")}] [{(Target2.Value ? Target2.Value.name : "<None>")}] Angle {MTools.CompareToString(Compare)} {Angle.Value}";

        protected override void _SetTarget(Object target) => Target.Value = MTools.VerifyComponent(target, Target.Value);
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target?.Value != null;

        [Tooltip("Target to check for the condition")]
        [Hide(nameof(LocalTarget))]
        public GameObjectReference Target = new();

        [ContextMenuItem("Debug On", "ChangeDebugOn")]
        public AxisDirection Direction = AxisDirection.Forward;
        [Tooltip("Get the Local Axis from a GameObject. Leave empty World Axis")]

        public GameObjectReference Target2 = new();
        public AxisDirection Direction2 = AxisDirection.Forward;

        public ComparerNumber Compare = ComparerNumber.Less;
        public FloatReference Angle = new(5);

        [Tooltip("Use Vector3.SignedAngle() instead of normal Vector3.Angle")]
        public bool useSignedAngle = false;

        [Hide(nameof(useSignedAngle))]
        public AxisDirection Axis = AxisDirection.Up;
        [Hide(nameof(useSignedAngle))]
        [Tooltip("Get the Local Axis from a GameObject. Leave empty World Axis")]
        public GameObjectReference AxisTarget = new();


        protected override bool _Evaluate()
        {
            var from = MTools.TransformDirVector(Target.transform, Direction);
            var to = MTools.TransformDirVector(Target2.transform, Direction2);
            var axis = MTools.TransformDirVector(AxisTarget.transform, Axis);

            var angle = useSignedAngle ? Vector3.SignedAngle(from, to, axis) : Vector3.Angle(from, to);

            bool result = angle.MCompare(Angle, Compare);

            Debugging($"[Target:{Target.Value.name}] [Target2:{Target2.Value.name}] Angle: {angle:F2} is {Compare} than {Angle.Value} ? ", result, Target.Value);
            return result;
        }


        /// <summary>
        /// To make this work you need to call this method the ONDrawGizmos on any Monobehaviour that has conditions
        /// </summary>
        /// <param name="target"></param>

        public override void DrawGizmos(Component target)
        {
#if UNITY_EDITOR
            var angle = Angle.Value;
            if (angle != 360)
            {
                angle /= 2;

                var Direction = MTools.TransformDirVector(target.transform, this.Direction);

                Handles.color = new Color(0, 1, 0, 0.1f);
                Handles.DrawSolidArc(target.transform.position, target.transform.up, Quaternion.Euler(0, -angle, 0) * Direction, angle * 2, target.transform.localScale.y);
                Handles.color = Color.green;
                Handles.DrawWireArc(target.transform.position, target.transform.up, Quaternion.Euler(0, -angle, 0) * Direction, angle * 2, target.transform.localScale.y);
            }
#endif
        }
    }
}
