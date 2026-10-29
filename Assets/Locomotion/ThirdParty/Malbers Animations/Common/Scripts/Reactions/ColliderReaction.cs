// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Reactions;
using MalbersAnimations.Utilities;
using UnityEngine;

namespace MalbersAnimations
{
    [System.Serializable] //Needs to be Serializable!!!!
    [AddTypeMenu("Unity/Collider")]
    [HelpURL(ReactionDocs.Unity + "collider")]
    public class ColliderReaction : Reaction
    {
        public override string DynamicName
        {
            get
            {
                var display = "Collider"; //Name of the Reaction
                if ((option & ColliderOption.Enable) == ColliderOption.Enable) display += $" [{(enable ? "Enable" : "Disable")}]";
                if ((option & ColliderOption.IsTrigger) == ColliderOption.IsTrigger) display += $" [Trigger {(isTrigger ? "On" : "Off")}]";
                if ((option & ColliderOption.Material) == ColliderOption.Material) display += $" [Material: {(material != null ? material.name : "None")}]";
                return display;
            }
        }

        public override System.Type ReactionType => typeof(Collider); //set the Type of component this Reaction Needs

        public enum ColliderOption { Enable = 1, IsTrigger = 2, Material = 4 }

        [Flag]
        public ColliderOption option = ColliderOption.Enable;

        [Hide("option", false, true, true, 1, 3, -1)]
        public bool enable;
        [Hide("option", false, true, true, 2, 6, -1)]
        public bool isTrigger;
        [Hide("option", false, true, true, 5, 6, -1)]
        public PhysicsMaterial material;

        protected override bool _TryReact(Component reactor)
        {
            Collider collider = reactor as Collider; //Cast the reactor as collider type.

            if (collider != null)
            {
                if ((option & ColliderOption.Enable) == ColliderOption.Enable) collider.enabled = enable;
                if ((option & ColliderOption.IsTrigger) == ColliderOption.IsTrigger) collider.isTrigger = isTrigger;
                if ((option & ColliderOption.Material) == ColliderOption.Material) collider.material = material;
                return true; //Reaction successful!!
            }
            return false; //Reaction failed!!
        }
    }
}