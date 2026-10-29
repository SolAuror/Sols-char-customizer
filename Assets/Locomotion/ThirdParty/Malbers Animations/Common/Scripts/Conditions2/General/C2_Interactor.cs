// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using MalbersAnimations.Utilities;
using System.Linq;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    [System.Serializable]
    public abstract class C2_Interactor : ConditionCore
    {
        [Hide(nameof(LocalTarget))] public MInteractor Target;
        protected override void _SetTarget(Object target) => VerifyComponent(target, ref Target);
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;

    }

    [System.Serializable, AddTypeMenu("Interactor/Item Focused")]
    public class C2_FocusedInteractor : C2_Interactor
    {
        public override string DynamicName => $"Interactor - Focused [{(InteractableID.Value == -1 ? "Any" : InteractableID.Value)}]";
        [Tooltip("The ID of the Interactable to check if is focused. -1 will set true to any interactable")]
        public IntReference InteractableID = new(-1);
        protected override bool _Evaluate()
        {
            if (Target == null) return false;
            if (Target.FocusedInteractables == null || Target.FocusedInteractables.Count == 0) return false;
            if (InteractableID.Value == -1) return true; //Means any Interactable is focused
            return Target.FocusedInteractables.Any(item => item.Index == InteractableID.Value);
        }
    }
}
