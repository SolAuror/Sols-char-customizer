using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    // MWC — Range version of Reaction2Behaviour.
    // Executes a Start Reaction when the animation ENTERS a normalized range and an Exit Reaction when it LEAVES it.
    // Made for paired ON/OFF behaviours: play/stop an effect, a weapon trail, a sound, an IK Set, a damager...
    // The Exit Reaction is guaranteed to be executed if the Start Reaction was executed, even when the
    // animation is interrupted, so nothing is ever left playing.
    [AddComponentMenu("Malbers/Reaction Range")]
    public class Reaction2RangeBehaviour : StateMachineBehaviour
    {
        [Tooltip("List of Reaction Ranges to execute on the Animator")]
        public List<Reaction2Range> ranges = new() { new Reaction2Range() };

        [Tooltip("Log on the console when the Start/Exit Reactions are executed")]
        public bool debug;

        override public void OnStateEnter(Animator anim, AnimatorStateInfo state, int layer)
        {
            foreach (var r in ranges)
            {
                if (r == null) continue;

                //MWC — Transition to itself (chained attacks) re-enters the State with the range still open.
                //Keep it open instead of sending a second Start Reaction without its Exit Reaction.
                if (r.Started && !r.Ended) { r.RestartTime(); continue; }

                r.ResetRange();

                if (r.Range.minValue <= 0) React_Start(r, anim); //The range starts with the animation itself
            }
        }

        override public void OnStateUpdate(Animator anim, AnimatorStateInfo state, int layer)
        {
            var inTransition = anim.IsInTransition(layer);

            //Transitioning to itself: the range must survive the transition (chained/looped attacks)
            var toItself = inTransition && anim.GetNextAnimatorStateInfo(layer).fullPathHash == state.fullPathHash;
            var exitTransition = inTransition && !toItself;

            //MWC — Non-Looping states clamp at 1, and (1 % 1) would wrap them back to zero, closing the range too early
            var time = state.loop ? state.normalizedTime % 1 : Mathf.Clamp01(state.normalizedTime);

            foreach (var r in ranges)
            {
                if (r == null) continue;

                //MWC — Looping animation: close the open range on the loop point, and reopen it if it repeats
                if (r.UpdateTime(time) && state.loop)
                {
                    if (r.Started && !r.Ended) React_Exit(r, anim);
                    if (r.Repeat) r.ResetRange();
                }

                if (!r.Started && time >= r.Range.minValue) React_Start(r, anim);

                if (r.Started && !r.Ended)
                {
                    if (time >= r.Range.maxValue)
                    {
                        if (toItself) continue; //Chained animation: keep the range open, the next entry inherits it
                        React_Exit(r, anim);
                    }
                    else if (exitTransition && r.ExitInTransition)
                    {
                        React_Exit(r, anim); //The animation is leaving, close the range now instead of waiting
                    }
                }
            }
        }

        override public void OnStateExit(Animator anim, AnimatorStateInfo state, int layer)
        {
            if (anim.GetCurrentAnimatorStateInfo(layer).fullPathHash == state.fullPathHash) return; //means is transitioning to it self

            foreach (var r in ranges)
            {
                if (r == null) continue;

                //MWC — Safety net: the animation was interrupted inside the range. Never leave an effect/trail playing
                if (r.Started && !r.Ended && r.ExitOnInterrupt) React_Exit(r, anim);

                r.ResetRange();
            }
        }

        private void React_Start(Reaction2Range r, Animator anim)
        {
            r.ReactStart(anim);
            if (debug) Debug.Log($"<b>[{anim.name}]</b> - [Reaction Range] <b>[Start]</b> ({r.Range.minValue:F2})", anim);
        }

        private void React_Exit(Reaction2Range r, Animator anim)
        {
            r.ReactExit(anim);
            if (debug) Debug.Log($"<b>[{anim.name}]</b> - [Reaction Range] <b>[Exit]</b> ({r.Range.maxValue:F2})", anim);
        }

        private void OnValidate()
        {
            if (ranges == null) return;

            foreach (var r in ranges)
            {
                if (r == null) continue;

                r.Range.minValue = Mathf.Clamp01(r.Range.minValue);
                r.Range.maxValue = Mathf.Clamp01(r.Range.maxValue);

                //MWC — An inverted range would open and never close on the same frame check
                if (r.Range.maxValue < r.Range.minValue) r.Range.maxValue = r.Range.minValue;
            }
        }
    }

    // MWC — One normalized [Start, Exit] window on an animation with its pair of reactions
    [System.Serializable]
    public class Reaction2Range
    {
        [Tooltip("Normalized range of the animation where the reactions will be executed")]
        [MinMaxRange(0, 1)]
        public RangedFloat Range = new(0.2f, 0.6f);

        [Tooltip("Reactions executed when the animation ENTERS the range")]
        public Reaction2 StartReaction;

        [Tooltip("Reactions executed when the animation EXITS the range")]
        public Reaction2 ExitReaction;

        [Tooltip("If the animation is interrupted before the range is completed, execute the Exit Reaction anyway.\n" +
            "Keep it ON so effects, trails or sounds are never left playing")]
        public bool ExitOnInterrupt = true;

        [Tooltip("Execute the Exit Reaction as soon as the animation starts transitioning to another animation, " +
            "without waiting for the end of the range")]
        public bool ExitInTransition = true;

        [Tooltip("On Looping animations, execute the range again on every loop")]
        public bool Repeat = true;

        /// <summary> The Start Reaction was already executed. Runtime only (property, so it's never serialized) </summary>
        public bool Started { get; private set; }

        /// <summary> The Exit Reaction was already executed. Runtime only (property, so it's never serialized) </summary>
        public bool Ended { get; private set; }

        private float lastTime;

        public void ResetRange() { Started = Ended = false; lastTime = 0; }

        /// <summary> Keeps the range open through a transition to itself </summary>
        public void RestartTime() => lastTime = 0;

        /// <summary> Stores the normalized time. Returns true when the animation has looped (time went backwards) </summary>
        public bool UpdateTime(float time)
        {
            var looped = time < lastTime;
            lastTime = time;
            return looped;
        }

        //Flags are set BEFORE reacting: a reaction that changes the animation cannot make it re-enter twice
        public void ReactStart(Component target) { Started = true; StartReaction.React(target); }

        public void ReactExit(Component target) { Ended = true; ExitReaction.React(target); }
    }
}
