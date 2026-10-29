// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    [System.Serializable, AddTypeMenu("Unity/Layers")]
    public class C2_Layers : ConditionCore
    {
        public override string DynamicName =>
            $"[{(Target ? Target.name : "Dynamic Target")}] Layer [{(int)Layer.Value}]";

        [Tooltip("Target to check for the condition ")]
        [RequiredField, Hide(nameof(LocalTarget))] public Object Target;
        public LayerReference Layer;

        protected override bool _Evaluate()
        {
            if (Target == null) return false;

            if (Target is GameObject go)
                return MTools.Layer_in_LayerMask(go.layer, Layer.Value);
            else if (Target is Component comp)
                return MTools.Layer_in_LayerMask(comp.gameObject.layer, Layer.Value);
            return false;
        }

        protected override void _SetTarget(Object target)
        {
            if (target is GameObject go) Target = go;
            else if (target is Component comp) Target = comp.gameObject;
        }
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;
    }
}
