// Last Change: 9/18/2026 10:30pm
using System;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Malbers/Tags")]
    [HelpURL(ReactionDocs.Malbers + "tags")]
    public class TagReaction : Reaction
    {
        public override string DynamicName => $"Tag {action} [{(Tag != null ? Tag.name : "None")}]"; //Name of the Reaction


        public enum TagReactionType { Add, Remove }

        public TagReactionType action = TagReactionType.Add;
        public Tag Tag;
        public override Type ReactionType => typeof(Tags);

        protected override bool _TryReact(Component reactor)
        {
            var tags = reactor as Tags;

            switch (action)
            {
                case TagReactionType.Add:
                    tags.AddTag(Tag);
                    break;
                case TagReactionType.Remove:
                    tags.RemoveTag(Tag);
                    break;
            }

            return true;
        }
    }
}