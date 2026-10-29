using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    public class Reaction2Behaviour : StateMachineBehaviour
    {
        [Tooltip("List of reactions to send to the animator")]
        public List<Reaction2B> reactionsOnAnimator = new();

        override public void OnStateEnter(Animator anim, AnimatorStateInfo stateInfo, int layerIndex)
        {
            foreach (var item in reactionsOnAnimator)
            {
                item.Sent = false;
                if (item.Time == 0) item.React(anim);
                GetIgnoreTransitionHash(item);
            }
        }

        override public void OnStateUpdate(Animator anim, AnimatorStateInfo state, int layer)
        {
            var NextAnim = anim.GetNextAnimatorStateInfo(layer).shortNameHash;
            var inTransition = anim.IsInTransition(layer);

            if (inTransition && NextAnim == state.shortNameHash) return; //means is transitioning to it self, so ignore the transition and check the time as normal (IMPORTANT)
            var InTransition = inTransition && state.shortNameHash != NextAnim; //Check only the Exit Transition not the Start Transition


            var time = state.normalizedTime % 1;

            foreach (var e in reactionsOnAnimator)
            {
                if (e.Sent) continue; //If the effect was already sent keep looking for the next one

                if (InTransition)
                {
                    if (e.IgnoreInTransitionHash.Contains(NextAnim))
                    {
                        e.Sent = true;
                        continue; //MWC: Bug fix — must skip time check below or the ignored reaction still fires
                    }
                    else if (e.Time == 1 && e.ExitInTransition) //If is a quick exit transition
                    {
                        e.React(anim);
                        continue; //MWC: Bug fix — was return, which skipped all remaining reactions in the list
                    }
                }

                //Regular Update Check for the Effect
                if (time >= e.Time)
                {
                    e.React(anim);
                }
            }
        }

        override public void OnStateExit(Animator anim, AnimatorStateInfo state, int layer)
        {
            if (anim.GetCurrentAnimatorStateInfo(layer).fullPathHash == state.fullPathHash) return; //means is transitioning to it self

            foreach (var reaction in reactionsOnAnimator)
            {
                if (reaction.Time == 1 && !reaction.Sent)
                {
                    reaction.React(anim);
                }
                else if (reaction.Time > 0 && reaction.Time < 1 && !reaction.Sent && reaction.ExecuteOnExit) //MWC: Bug fix — ExecuteOnExit was defined but never checked
                {
                    reaction.React(anim);
                }
            }
        }

        private void GetIgnoreTransitionHash(Reaction2B item)
        {
            //Gather all the hashes the first time only
            if (item.IgnoreInTransitionHash == null)
            {
                item.IgnoreInTransitionHash = new List<int>();

                if (item.IgnoreInTransition != null && item.IgnoreInTransition.Count > 0)
                {
                    foreach (var hash in item.IgnoreInTransition)
                    {
                        item.IgnoreInTransitionHash.Add(Animator.StringToHash(hash));
                    }
                }
            }
        }

        private void OnValidate()
        {
            for (int i = 0; i < reactionsOnAnimator.Count; i++)
            {
                var react = reactionsOnAnimator[i];
                var count = react.reactions.IsValid ? react.reactions.Count : 0; //MWC: Bug fix — .reactions array can be null before assignment, use safe Count property
                react.display = $"[Reaction ({count})]";

                if (react.Time == 0)
                    react.display += $"  -  [On Enter]";
                else if (react.Time == 1)
                    react.display += $"  -  [On Exit]";
                else
                    react.display += $"  -  [OnTime] ({react.Time:F2})";

                if (react.ExitInTransition && react.Time == 1) react.display += "[In Transition]";

                react.showExecute = react.Time != 1 && react.Time != 0;
                react.showExitInTransition = react.Time == 1;
            }
        }
    }

    [System.Serializable]
    public class Reaction2B
    {
        [HideInInspector] public string display;
        [HideInInspector] public bool showExecute;
        [HideInInspector] public bool showExitInTransition;

        [Range(0, 1)]
        public float Time;
        public Reaction2 reactions;

        public bool Sent { get; set; }
        [Tooltip("If the animation was interrupted by a transition and the Time has not played yet, execute the Reaction anyways")]
        [Hide(nameof(showExecute))]
        public bool ExecuteOnExit = true;

        [Tooltip("If the animation is interrupted, Execute the Reaction as soon as it start transition to another Animation State")]
        [Hide(nameof(showExitInTransition))]
        public bool ExitInTransition = true;

        [Tooltip("Ignore the Reaction if Execute is called in a transition for the next anim state. included in any of these State List")]
        public List<string> IgnoreInTransition = new();
        public List<int> IgnoreInTransitionHash { get; set; }

        public void React(Component target)
        {
            reactions.React(target);
            Sent = true;
        }
    }
}
