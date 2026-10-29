// Last Change: 9/17/2026 6:05pm
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    /// <summary>
    /// Reads a value from a member (property / field / zero-arg method) on the target and sends it
    /// through the typed UnityEvent matching that member's type. It is the read counterpart of
    /// <see cref="MemberValuesSetReaction"/> and reuses <see cref="MemberValueGetter"/> for the reflection.
    /// <para>Example: Target Type = SuperItem, Member = Description → dynamic-bind "On String" to
    /// MEvent.Invoke(string) so every MEventListener (UI text, etc.) receives the description.</para>
    /// </summary>
    [System.Serializable, AddTypeMenu("[Member Get]")]
    [HelpURL(ReactionDocs.General + "member-get")]
    public class MemberGetReaction : Reaction
    {
        public override string DynamicName => Member.Description();

        public MemberValueGetter Member = new();
        public override System.Type ReactionType => typeof(Component);

        protected override bool _TryReact(Component component) => Member.Raise(component);
    }
}
