// Last Change: 9/17/2026 5:33pm
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Utilities
{
    [AddComponentMenu("Malbers/Effects")]
    public class EffectBehaviour : StateMachineBehaviour
    {
        public List<EffectItem> effects = new();

        // MWC — Cached registry lookup instead of a GetComponentsInChildren sweep. The list is owned by
        // EffectManager's Registry, so nothing is copied; it is only revalidated when the Animator changes
        // or a manager registered/unregistered. This is what the old [Dynamic Managers] toggle was for —
        // weapons added or removed at runtime are now picked up automatically, so the toggle is gone.
        private List<EffectManager> cache;
        private Animator cacheAnimator;
        private int cacheVersion = -1;

        /// <summary>Refreshes the cached list only when the Animator changed or a manager was (un)registered.</summary>
        private void CheckCache(Animator animator)
        {
            if (cacheAnimator == animator && cacheVersion == EffectManager.Version) return;

            cacheAnimator = animator;
            cacheVersion = EffectManager.Version;
            cache = EffectManager.GetAll(animator);
        }

        override public void OnStateEnter(Animator anim, AnimatorStateInfo stateInfo, int layerIndex)
        {
            CheckCache(anim);

            // MWC — the reset and the hash gathering now run unconditionally. They used to be skipped when
            // no Effect Manager existed on Enter, which left IgnoreInTransitionHash null; with a live
            // registry a manager can join mid-state and OnStateUpdate would then dereference it.
            foreach (var item in effects)
            {
                item.sent = false;
                item.played = false; // MWC — reset the [Range] Play stage

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
        }

        override public void OnStateUpdate(Animator anim, AnimatorStateInfo state, int layer)
        {
            CheckCache(anim);

            if (cache != null)
            {
                var NextAnim = anim.GetNextAnimatorStateInfo(layer).shortNameHash;
                var InTransition = anim.IsInTransition(layer) && state.shortNameHash != NextAnim; //Check only the Exit Transition not the Start Transition
                var time = state.normalizedTime % 1;

                foreach (var e in effects)
                {
                    if (e.sent) continue; //If the effect was already sent keep looking for the next one

                    if (InTransition)
                    {
                        if (e.IgnoreInTransitionHash.Contains(NextAnim))
                        {
                            // MWC — [Range] the effect is already playing, close it before ignoring the rest
                            if (e.action == EffectOption.Range && e.played) Send(e, EffectOption.Stop);

                            e.sent = true;
                            continue; // MWC — ignored, do not evaluate the time below
                        }
                        // MWC — [Range] quick exit transition: stop the effect as soon as it starts transitioning out
                        else if (e.action == EffectOption.Range)
                        {
                            if (e.played && e.Range.Max == 1 && e.ExitInTransition)
                            {
                                Send(e, EffectOption.Stop);
                                e.sent = true;
                                return;
                            }
                        }
                        else if (e.Time == 1 && e.ExitInTransition) //If is a quick exit transition
                        {
                            Execute(e);
                            return;
                        }
                    }

                    // MWC — [Range] Play at Range.Min and Stop at Range.Max on the same effect
                    if (e.action == EffectOption.Range)
                    {
                        if (!e.played && time >= e.Range.Min)
                        {
                            Send(e, EffectOption.Play);
                            e.played = true;
                        }

                        if (e.played && time >= e.Range.Max)
                        {
                            Send(e, EffectOption.Stop);
                            e.sent = true;
                        }
                    }
                    //Regular Update Check for the Effect
                    else if (time >= e.Time)
                    {
                        Execute(e);
                    }
                }
            }
        }



        override public void OnStateExit(Animator anim, AnimatorStateInfo state, int layer)
        {
            CheckCache(anim);

            if (cache != null)
            {
                //means is transitioning to it self
                if (anim.GetCurrentAnimatorStateInfo(layer).fullPathHash == state.fullPathHash) return;

                foreach (var e in effects)
                {
                    if (e.sent) continue; //If the effect was already sent keep looking for the next one

                    // MWC — [Range] a playing effect must always be closed on exit, even if the next animation is ignored
                    if (e.action == EffectOption.Range)
                    {
                        if (e.played)
                        {
                            Send(e, EffectOption.Stop);
                            e.sent = true;
                        }
                        continue; //Never Play a Range effect on Exit: it would be stopped on the same frame
                    }

                    //Ignore the Effect action if the Next Animation is on the list
                    if (e.IgnoreInTransitionHash.Contains(anim.GetCurrentAnimatorStateInfo(layer).shortNameHash))
                    {
                        return;
                    }


                    if (e.Time == 1 || (e.ExecuteOnExit && !e.sent))
                    {
                        Execute(e);
                    }
                }
            }
        }


        private void Execute(EffectItem e)
        {
            Send(e, e.action);
            e.sent = true;
        }

        /// <summary> Sends the Play/Stop message to all the Effect Managers without closing the Item </summary>
        private void Send(EffectItem e, EffectOption action) // MWC — [Range] needs to Play and Stop the same item
        {
            if (cache == null) return;

            // MWC — indexed loop, not foreach: this walks the Registry's live list, and playing an effect
            // can instantiate or re-parent objects, which could register a manager mid-iteration.
            for (int i = 0; i < cache.Count; i++)
            {
                var EM = cache[i];
                if (EM == null) continue;

                if (action == EffectOption.Play)
                    EM.PlayEffect(e.ID);
                else
                    EM.StopEffect(e.ID);
            }
        }

        //Fake Inspector
        private void OnValidate()
        {
            foreach (var item in effects)
            {
                item.name = $"{item.action} [{item.ID}]";

                if (item.action == EffectOption.Range)
                {
                    // MWC — keep the ID visible on the Range mode
                    item.name = $"Play-Stop [{item.ID}]  -  (t: {item.Range.Min:F2} .. {item.Range.Max:F2})";
                }
                else if (item.Time == 0)
                    item.name += $"  -  [On Enter]";
                else if (item.Time == 1)
                    item.name += $"  -  [On Exit]";
                else
                    item.name += $"  -  [OnTime] (t: {item.Time:F2})";

                item.name += $" → [{item.Description}]";

                // MWC — [Range] the exit transition applies when the Stop time is at the end of the animation
                var isRange = item.action == EffectOption.Range;

                if (item.ExitInTransition && (isRange ? item.Range.Max == 1 : item.Time == 1))
                    item.name += " - [Exit In Transition]";

                if (item.IgnoreInTransition.Count > 0) item.name += $" - [Ignore: {item.IgnoreInTransition.Count}]";

                // MWC — Range always closes itself on Exit, so [Execute On Exit] is meaningless there
                item.showExecute = !isRange && item.Time != 1 && item.Time != 0;
                item.showExitInTransition = isRange ? item.Range.Max == 1 : item.Time == 1;
            }
        }
    }




    [System.Serializable]
    public class EffectItem
    {
        [HideInInspector] public string name;
        [HideInInspector] public bool showExecute;
        [HideInInspector] public bool showExitInTransition;
        [Tooltip("ID of the Effect")]
        public int ID = 1;                           //ID of the Attack Trigger to Enable/Disable during the Attack Animation
        public EffectOption action = EffectOption.Play;
        [Hide(("action"), true, 2), Range(0, 1)]
        public float Time = 0;

        [Hide(("action"), 2), MinMaxRange(0, 1)]
        public RangedFloat Range = new(0.2f, 0.5f);

        [Tooltip("If the animation is interrupted by a transition and the time has not played yet, execute the Effect anyways")]
        [Hide(nameof(showExecute))]
        public bool ExecuteOnExit = true;

        [Tooltip("If the animation is interrupted, Execute the Effect as soon as it start transition to another Animation State")]
        [Hide(nameof(showExitInTransition))]
        public bool ExitInTransition = true;

        public string Description;

        [Tooltip("Ignore the effect if execute is called in Transition and the next transition is this list. Use the name of the Animation State")]
        public List<string> IgnoreInTransition = new();
        public List<int> IgnoreInTransitionHash { get; set; }

        public bool sent { get; set; }

        /// <summary> [Range] The Play part was already executed, only the Stop is pending </summary>
        public bool played { get; set; }
    }

    public enum EffectOption
    {
        Play,
        Stop,
        [InspectorName("Play-Stop [Range]")]
        Range
    }
}