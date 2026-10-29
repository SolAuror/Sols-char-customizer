// Last Change: 9/24/2026 1:14pm
using MalbersAnimations.Controller;
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Conditions
{
    [System.Serializable]
    public abstract class C2_PickDrop : ConditionCore
    {
        [Hide(nameof(LocalTarget))] public MPickUp Target;
        protected override void _SetTarget(Object target) => VerifyComponent(target, ref Target);
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;

    }

    [System.Serializable, AddTypeMenu("PickDrop/Pickeable Focused")]
    public class C2_FocusedPickable : C2_PickDrop
    {
        public override string DynamicName => $"[PickDrop] - Focused [{(ID.Value == -1 ? "Any" : ID.Value)}]";
        [Tooltip("The ID of the Interactable to check if is focused. -1 will set true to any interactable")]
        public IntReference ID = new(-1);
        protected override bool _Evaluate()
        {


            if (Target == null) return false;
            if (Target.FocusedItem == null) return false;
            if (ID.Value == -1) return true; //Means any Interactable is focused
            return Target.FocusedItem.ID == ID.Value;
        }
    }



    [System.Serializable, AddTypeMenu("PickDrop/Has Item Picked")]
    public class C2_PickDropHasItem : C2_PickDrop
    {
        public override string DynamicName => $"[PickDrop] - Has Item [{(ID.Value == -1 ? "Any" : ID.Value)}]";

        [Tooltip("The ID of the Pickable to check if is held. -1 will set true to any held item")]
        public IntReference ID = new(-1);
        /// <summary>True when the PickUp holds an item and (ID is -1 or the held item's ID matches).</summary>
        protected override bool _Evaluate()
        {
            if (Target == null) return false;
            if (Target.Item == null) return false;
            return ID.Value == -1 || Target.Item.ID == ID.Value; //-1 means any held item, otherwise the ID must match
        }
    }
}
