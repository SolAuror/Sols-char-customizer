// Last Change: 9/24/2026 12:57pm
using UnityEngine;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    public struct Reaction2
    {
        [SerializeReference] public Reaction[] reactions;

        public Reaction2(Reaction reaction)
        {
            this.reactions = new Reaction[1];
            this.reactions[0] = reaction;
        }

        public Reaction2(ListReaction reaction)
        {
            this.reactions = reaction.reactions.ToArray();
        }

        public readonly int Count => reactions != null ? reactions.Length : 0;

        /// <summary>  Conditions can be used the list has some conditions </summary>
        public readonly bool IsValid => reactions != null && reactions.Length > 0;

        //Find the first reaction type
        public readonly Type ReactionType => reactions != null && reactions.Length > 0 && reactions[0] != null ? reactions[0].ReactionType : null;

        /// <summary>  Cache the component value on each reaction to avoid calling GetComponent every time  </summary>
        /// <param name="target">Global Target to find each component</param>
        public readonly void VerifyComponent(Component target)
        {
            for (int i = 0; i < reactions.Length; i++)
            {
                reactions[i].VerifyComponent(target);
            }
        }

        /// <summary>Runs every reaction on the target; true only when all of them succeeded</summary>
        public readonly bool TryReact(GameObject target)
        {
            if (!IsValid) return false; //If there are no reactions, return false
            if (target == null) return false;

            bool result = true;
            for (int i = 0; i < reactions.Length; i++)
            {
                if (reactions[i] == null) { Debug.Log("There's a null Reaction. Please check all your reactions."); continue; } //Skip null reactions
                reactions[i].VerifyComponent(target.transform);
                result = reactions[i].TryReact(target.transform) && result; //React first: a failed row must not skip the rows below
            }

            return result;
        }
        /// <summary>Runs every reaction on the target; true only when all of them succeeded</summary>
        public readonly bool TryReact(Component target)
        {
            if (!IsValid)
            {
                // Debug.LogWarning("No Reactions to React.", target);
                return false; //If there are no reactions, return false
            }
            bool result = true;

            for (int i = 0; i < reactions.Length; i++)
            {
                if (reactions[i] == null) { Debug.Log("There's a null Reaction. Please check all your reactions."); continue; } //Skip null reactions

                if (reactions[i].useLocal && reactions[i].localTarget == null)
                {
                    Debug.LogWarning($"Reaction {i} is set to use Local Target but the Local Target is null. Skipping this reaction.", target);
                    continue;
                }

                reactions[i].VerifyComponent(target);

                result = reactions[i].TryReact(target) && result; //React first: a failed row must not skip the rows below
            }

            return result;
        }
        public readonly bool React(Component target) => TryReact(target);
        public readonly bool React(GameObject target) => TryReact(target);

        public readonly bool Invoke(GameObject target) => TryReact(target);
        public readonly bool Invoke(Component target) => TryReact(target);

        public static implicit operator Reaction2(Reaction reference)
        {
            if (reference == null) return new Reaction2();

            if (reference is ListReaction listReact)
            {
                return new(listReact);
            }
            else
            {
                return new(reference);
            }
        }
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(Reaction2))]
    public class Reactions_Drawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var reactions = property.FindPropertyRelative("reactions");
            label.text += $" ({reactions.arraySize})";
            EditorGUI.PropertyField(position, reactions, label, true);
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property.FindPropertyRelative("reactions"), label);
        }
    }
#endif 
}