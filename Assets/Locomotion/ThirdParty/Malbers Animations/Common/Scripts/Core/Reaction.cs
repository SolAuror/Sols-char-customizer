// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Conditions; //MWC — per-reaction condition gate
using System;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    //MWC — Base URLs for the Reaction documentation, one per menu group. Every built-in reaction carries a
    //[HelpURL] built from these, which the Help button on the reaction row opens. Unity ignores [HelpURL] on
    //non-Object types, so the drawer reads the attribute itself — meaning a custom reaction gets a working
    //Help button just by adding the attribute.
    public static class ReactionDocs
    {
        private const string Base = "https://malbersanimations.gitbook.io/animal-controller/main-components/reactions/";

        public const string Animal = Base + "animal-reactions/";
        public const string Unity = Base + "unity-reactions/";
        public const string Malbers = Base + "malbers-reactions/";
        public const string Stats = Base + "stat-reactions/";
        public const string Scriptables = Base + "scriptable-reactions/";
        public const string Tools = Base + "tool-reactions/";
        public const string General = Base + "general-reactions/";

        /// <summary> Super Item, Super Item Manager and Pouch Reactions: they are documented with the Super Item System, not under the Reactions group. Append the heading anchor (e.g. "#manager-global") </summary>
        public const string SuperItem = "https://malbersanimations.gitbook.io/animal-controller/super-item/super-item-system/reactions";
    }

    [Serializable]
    public abstract class Reaction
    {
        [HideInInspector] public string desc = string.Empty;

        public static MonoBehaviour Delay;

        /// <summary>Instant Reaction ... without considering Active or Delay parameters</summary>
        protected abstract bool _TryReact(Component reactor);

        /// <summary>Get the Type of the reaction</summary>
        public abstract Type ReactionType { get; }

        public virtual string DynamicName => this.GetType().Name + " Reaction";

        public void React(Component component) => TryReact(useLocal ? localTarget : component);

        public void React(GameObject go) => TryReact(go.transform);

        [Tooltip("Enable or Disable the Reaction")]
        [HideInInspector] public bool Active = true;

        [Tooltip("Use a local Target instead of a dynamic one")]
        [HideInInspector] public bool useLocal;
        [HideInInspector] public Component localTarget;
        [HideInInspector, Min(0)] public float delay = 0;

        [Tooltip("Use a condition to gate this reaction. When enabled the reaction only runs if the condition is valid")]
        [HideInInspector] public bool useCondition;

        //MWC — [Hide] and NOT [HideInInspector]: HideInInspector hides the whole subtree from SerializedProperty
        //(hasVisibleChildren returns false), so each condition lost its foldout and could not be edited.
        //[Hide] hides at the drawer level instead, keeping the children visible.
        [Hide(nameof(useCondition))] public Conditions2 condition;

        /// <summary>  Checks and find the correct component to apply a reaction  </summary>  
        public Component VerifyComponent(Component component)
        {
            if (component == null) return null; //If the component is null return null (No Component to React_)

            Component TrueComponent;

            //Find if the component is the same 
            if (ReactionType.IsAssignableFrom(component.GetType()) || ReactionType == typeof(GameObject))
            {
                TrueComponent = component;
            }
            else
            {
                TrueComponent = component.GetComponent(ReactionType);

                if (TrueComponent == null)
                    TrueComponent = component.GetComponentInParent(ReactionType);
                if (TrueComponent == null)
                    TrueComponent = component.GetComponentInChildren(ReactionType);
            }
            return TrueComponent;
        }

        public bool TryReact(Component component)
        {
            if (!Application.isPlaying) return false; //Reactions cannot be called in Editor!!
            if (Active)
            {
                //MWC — gate the reaction with the optional condition before resolving/reacting
                if (useCondition && !condition.Evaluate(useLocal ? localTarget : component)) return false;

                component = VerifyComponent(useLocal ? localTarget : component);

                if (component == null) //verification if the component is null
                {
                    // Debug.Log($"Component is null. Ignoring the Reaction. <b>[{ReactionType.Name}] </b>");
                    return false; //NO Component to React
                }

                if (component.gameObject == null || component.gameObject.IsPrefab())
                {
                    Debug.Log($"Component's GameObject is not in the scene. Ignoring the Reaction. <b>[{ReactionType.Name}] </b>. is Loaded: {component.gameObject.scene.isLoaded}", component);
                    return false; //If the component is in the scene return false
                }
                if (delay > 0)
                {
                    //Create the Delay Reactions for the first time
                    if (Delay == null)
                    {
                        var DelayGameObject = new GameObject("Reaction Delay");
                        Delay = DelayGameObject.AddComponent<UnityUtils>();
                    }

                    Delay.Delay_Action(delay, () => _TryReact(component));
                    return true;
                }
                else
                {
                    return _TryReact(component);
                }
            }
            return false;
        }

        //React to multiple components
        public bool TryReact(params Component[] components)
        {
            if (Active && components != null && components.Length > 0)
            {
                foreach (var component in components)
                {
                    if (useCondition && !condition.Evaluate(component)) continue; //MWC — gated by condition

                    var comp = VerifyComponent(component);
                    _TryReact(comp);
                }
            }
            return true;
        }


        public static implicit operator Reaction(Reaction2 reference)
        {
            if (!reference.IsValid) return null;

            if (reference.reactions.Length == 1)
            {
                return reference.reactions[0]; //Get the first reaction
            }
            else
            {
                return new ListReaction(reference.reactions);
            }
        }

        public virtual void DrawGizmos(Component origin)
        {

        }
    }
}