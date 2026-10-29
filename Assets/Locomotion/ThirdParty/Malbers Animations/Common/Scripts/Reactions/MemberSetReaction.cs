// Last Change: 9/17/2026 5:33pm
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    // Old version. Do not show it in the AddTypeMenu anymore. Use MemberValuesSetReaction instead.
    // [System.Serializable, AddTypeMenu("General/Member Set (Old)")]

    /// <summary> This Reaction sets a value to a member of a component. It uses the MemberValueSetter class to apply the value to the specified member. </summary>
    public class MemberSetReaction : Reaction
    {
        public override string DynamicName => Member.Description();

        public MemberValueSetter Member;
        public override System.Type ReactionType => typeof(Component);

        protected override bool _TryReact(Component component)
        {
            Member.Apply(component);
            return true;
        }
    }

    [System.Serializable, AddTypeMenu("[Member Set]")]
    [HelpURL(ReactionDocs.General + "member-set")]
    /// <summary>  This Reaction sets a value to a member of a component. It uses the MemberValuesSetter class to apply the value to the specified member.  </summary>
    public class MemberValuesSetReaction : Reaction
    {
        public override string DynamicName => Member.Description();

        public MemberValuesSetter Member;
        public override System.Type ReactionType => typeof(Component);

        protected override bool _TryReact(Component component)
        {
            Member.Apply(component);
            return true;
        }
    }
}
