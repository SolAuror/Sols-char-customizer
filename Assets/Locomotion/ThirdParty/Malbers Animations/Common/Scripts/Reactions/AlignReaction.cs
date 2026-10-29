// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable, AddTypeMenu("Tools/Align Look At")]
    [HelpURL(ReactionDocs.Tools + "align-look-at")]
    public class AlignReaction : Reaction
    {
        public override string DynamicName => $"Align Look At [{AlignTargetName}] [{AlignTime}s]"; //Name of the Reaction

        /// <summary>Target shown on the row: the Transform, or the Tag when searching by Tag</summary>
        private string AlignTargetName => SearchTarget == TargetType.Tag
            ? $"Tag: {(Tag != null ? Tag.name : "None")}"
            : (Target != null && Target.UseConstant && Target.ConstantValue != null ? Target.Value.name : (!Target.UseConstant && Target.Variable != null ? Target.Variable.name : "None"));
        public override System.Type ReactionType => typeof(Component);

        public enum TargetType { Transform, Tag }

        public TargetType SearchTarget = TargetType.Transform;

        [Tooltip("The target to Look At Align")]
        [Hide(nameof(SearchTarget), 0)]
        public TransformReference Target;

        [Tooltip("Objects with tag to Look At Align")]
        [Hide(nameof(SearchTarget), 1)]
        public Tag Tag;

        [Tooltip("The Radius to search for targets. Zero ignore distance")]
        [Min(0)] public float distance = 0;

        [Header("Align Settings")]
        public float AlignTime = 0.15f;
        public float AlignOffset = 0f;

        public AnimationCurve AlignCurve = new(MTools.DefaultCurve);


        protected override bool _TryReact(Component component)
        {
            if (component.TryGetComponent<MonoBehaviour>(out var Mono)) //Find a MonoBehaviour to start the Coroutine
            {
                var Targ = SearchTarget == TargetType.Transform ? Target.Value : FindByCondition(component);

                if (Targ != null)
                {
                    if (distance > 0)
                    {
                        MDebug.DrawCircle(component.transform.position, component.transform.rotation, distance, Color.green, 1f);

                        var dist = Vector3.Distance(component.transform.position, Targ.position);
                        if (dist > distance) return false; //Out of range
                    }

                    Mono.StartCoroutine(MTools.AlignLookAtTransform(component.transform, Targ, AlignTime, AlignOffset, AlignCurve));

                    return true;
                }
            }
            return false;
        }

        protected Transform FindByCondition(Component component)
        {
            Transform ClosestGo = null;
            float ClosestDistance = float.MaxValue;

            foreach (var go in Tag.gameObjects)
            {
                if (go == null) continue;
                if (go.transform.IsChildOf(component.transform)) continue; //Skip itself and its children
                if (component.transform.IsChildOf(go.transform)) continue; //Skip if the animal is part of the target

                var DistTarget = Vector3.Distance(component.transform.position, go.transform.position);

                if (ClosestDistance > DistTarget)
                {
                    ClosestDistance = DistTarget;
                    ClosestGo = go.transform;
                }
            }
            return ClosestGo;
        }
    }

    [System.Serializable, AddTypeMenu("Tools/Align to Aim Direction")]
    [HelpURL(ReactionDocs.Tools + "align-to-aim-direction")]
    public class AlignToAimDirection : Reaction
    {
        public override string DynamicName => $"Aim Direction Align"; //Name of the Reaction
        public override System.Type ReactionType => typeof(Component);
        public float AlignTime = 0.15f;
        public float AlignOffset = 0f;
        public AnimationCurve AlignCurve = new(MTools.DefaultCurve);
        protected override bool _TryReact(Component component)
        {
            if (component.TryGetComponent<Aim>(out var Aim)) //Find a MonoBehaviour to start the Coroutine
            {
                Aim.StartCoroutine(MTools.AlignLookAtTransformDirection(component.transform, Aim.RawAimDirection, AlignTime, AlignOffset, AlignCurve));
                return true;
            }
            return false;
        }
    }
}
