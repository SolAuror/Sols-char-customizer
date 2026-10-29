// Last Change: 9/18/2026 10:30pm
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable, AddTypeMenu("General/Reposition")]
    [HelpURL(ReactionDocs.General + "reposition")]
    public class RepositionReaction : Reaction
    {
        public override string DynamicName =>
            $"Reposition to [{(Parent != null ? Parent.name : "None")}{(string.IsNullOrEmpty(Child) ? string.Empty : "/" + Child)}]" +
            $"{(Position ? " [Pos]" : string.Empty)}{(Rotation ? " [Rot]" : string.Empty)}{(Scale ? " [Scale]" : string.Empty)}";
        public override System.Type ReactionType => typeof(Transform);

        [RequiredField] public Transform Parent;

        public string Child;

        public bool Position = true;
        public bool Rotation = true;
        public bool Scale = false;

        protected override bool _TryReact(Component component)
        {
            if (Parent == null) return false;
            var Target = component.transform;

            var referenceTranform = Parent;

            if (!string.IsNullOrEmpty(Child))
            {
                referenceTranform = Parent.FindGrandChild(Child);
            }


            Debug.Log($"referenceTranform : [{referenceTranform}]");

            Target.SetPositionAndRotation(Position ? referenceTranform.position : Target.position, Rotation ? referenceTranform.rotation : Target.rotation);
            Target.localScale = Scale ? referenceTranform.localScale : Target.localScale;

            return true;
        }
    }
}
