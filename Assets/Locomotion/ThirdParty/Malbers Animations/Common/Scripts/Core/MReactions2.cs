// Last Change: 9/24/2026 1:14pm

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif


namespace MalbersAnimations.Reactions
{
    [AddComponentMenu("Malbers/Animal Controller/Reactions 2"), DefaultExecutionOrder(1000)]
    public class MReactions2 : MonoBehaviour
    {
        [Tooltip("Try to find a target on Enable when none is assigned. (Search first on this GameObject then in the parents, for the type of the first Reaction)")]
        [ContextMenuItem("Find Target", nameof(GetTarget))]
        public bool FindTarget = false;

        [SerializeField] private Component Target;

        [Tooltip("React when the Component is Enabled")]
        public bool ReactOnEnable = false;
        [Tooltip("React when the Component is Disabled")]
        public bool ReactOnDisable = false;

        public Reaction2 reactions;

        /// <summary>Finds the Target when [FindTarget] is on and none is assigned, then reacts if [ReactOnEnable] is on.</summary>
        protected virtual void OnEnable()
        {
            if (FindTarget && Target == null) GetTarget(); //An assigned Target always wins

            // Debug.Log($"ReactOnEnable : [{ReactOnEnable}]", this);
            if (ReactOnEnable) React();
        }

        /// <summary>Reacts when the component is disabled and [ReactOnDisable] is on.</summary>
        private void OnDisable()
        {
            if (ReactOnDisable) React();
        }

        /// <summary>Finds the first component of the Reaction's type on this GameObject, then in its parents, and stores it as the Target.</summary>
        [ContextMenu("Find Target")]
        public void GetTarget()
        {
            var type = reactions.ReactionType;
            if (type == null) return; //No reaction (or an empty first row): nothing to search for

            //GameObject reactions: GetComponent(typeof(GameObject)) throws, this GameObject is the target
            if (!type.IsInterface && !typeof(Component).IsAssignableFrom(type))
            {
                Target = transform;
                MTools.SetDirty(this);
                return;
            }

            // Replace null coalescing with explicit null checks to comply with UNT0007
            var component = GetComponent(type);
            if (component == null)
                component = GetComponentInParent(type);

            Target = component;

            MTools.SetDirty(this);
        }

        private Reaction Pin_Reaction;

        public void Pin(int index)
        {
            if (reactions.IsValid)
            {
                index = Mathf.Clamp(index, 0, reactions.reactions.Length - 1);
                Pin_Reaction = reactions.reactions[index];
            }
            else
            {
                Debug.LogError("Reaction is Empty. Please use any reaction", this);
            }
        }



        public void React()
        {
            if (reactions.IsValid)
            {
                reactions.TryReact(Target);
            }
            else
            {
                Debug.LogError("Reaction is Empty. Please use any reaction", this);
            }
        }


        public void React(int index)
        {
            if (reactions.IsValid)
            {
                index = Mathf.Clamp(index, 0, reactions.reactions.Length - 1);
                reactions.reactions[index]?.React(Target);
            }
            else
            {
                Debug.LogError("Reaction is Empty. Please use any reaction", this);
            }
        }


        public void React(Component component)
        {
            if (reactions.IsValid)
            {
                reactions.VerifyComponent(component);
                reactions.TryReact(component);
            }
            else
            {
                Debug.LogError("Reaction is Empty. Please use any reaction", this);
            }
        }

        public void React_Pin(Component component)
        {
            if (Pin_Reaction != null)
            {
                Pin_Reaction.VerifyComponent(component);
                Pin_Reaction.TryReact(component);
            }
            else
            {
                Debug.LogError("Pin Reaction is Empty. Please use any reaction", this);
            }
        }

        public void React_Pin(GameObject component) => React_Pin(component.transform);


        public void React(GameObject newAnimal) => React(newAnimal.transform);

        public void Target_Set(GameObject target) => Target = (target != null ? target.transform : null);

        public void Target_Set(Component target) => Target = target;

        public void Target_Clear() => Target = null;

    }

#if UNITY_EDITOR

    [CustomEditor(typeof(MReactions2))]
    public class MReaction2Editor : Editor
    {
        SerializedProperty FindTarget, Target, reaction, ReactOnEnable, ReactOnDisable;

        private GUIContent _SearchIcon;
        private GUIContent _OnEnable, _OnDisable, _FindTarget;
        private GUIContent _ReactIcon;
        MReactions2 M;


        protected virtual void OnEnable()
        {
            M = (MReactions2)target;
            FindTarget = serializedObject.FindProperty("FindTarget");
            Target = serializedObject.FindProperty("Target");
            reaction = serializedObject.FindProperty("reactions");
            ReactOnDisable = serializedObject.FindProperty("ReactOnDisable");
            ReactOnEnable = serializedObject.FindProperty("ReactOnEnable");
        }


        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                var width = 28f;

                if (Application.isPlaying)
                {
                    if (_ReactIcon == null)
                    {
                        _ReactIcon = EditorGUIUtility.IconContent("d_PlayButton@2x");
                        _ReactIcon.tooltip = "React at Runtime";
                    }

                    if (GUILayout.Button(_ReactIcon, EditorStyles.miniButton, GUILayout.Width(width), GUILayout.Height(20)))
                    {
                        (target as MReactions2).React();
                    }
                }

                if (_SearchIcon == null)
                {
                    _SearchIcon = EditorGUIUtility.IconContent("Search Icon");
                    _SearchIcon.tooltip = "Find Target in hierarchy";
                }

                if (GUILayout.Button(_SearchIcon, EditorStyles.miniButton, GUILayout.Width(width), GUILayout.Height(20)))
                {
                    (target as MReactions2).GetTarget();
                }

                EditorGUIUtility.labelWidth = 60;
                EditorGUILayout.PropertyField(Target);
                EditorGUIUtility.labelWidth = 0;

                #region ICONS

                if (_FindTarget == null)
                {
                    _FindTarget = EditorGUIUtility.IconContent("d_ol_plus");
                    _FindTarget.tooltip = "GetTarget on Enable";
                }

                if (_OnEnable == null)
                {
                    _OnEnable = EditorGUIUtility.IconContent("d_toggle_on_focus");
                    _OnEnable.tooltip = "React On Enable";
                }

                if (_OnDisable == null)
                {
                    _OnDisable = EditorGUIUtility.IconContent("d_toggle_bg_focus");
                    _OnDisable.tooltip = "React On Disable";
                }
                #endregion

                FindTarget.boolValue = GUILayout.Toggle(FindTarget.boolValue, _FindTarget,
                    EditorStyles.miniButton, GUILayout.Width(width), GUILayout.Height(20));

                var dC = GUI.color;
                if (ReactOnEnable.boolValue) GUI.color = Color.green;
                ReactOnEnable.boolValue = GUILayout.Toggle(ReactOnEnable.boolValue, _OnEnable,
                EditorStyles.miniButton, GUILayout.Width(width), GUILayout.Height(20));
                GUI.color = dC;

                if (ReactOnDisable.boolValue) GUI.color = Color.green;
                ReactOnDisable.boolValue = GUILayout.Toggle(ReactOnDisable.boolValue, _OnDisable,
                EditorStyles.miniButton, GUILayout.Width(width), GUILayout.Height(20));
                GUI.color = dC;
            }

            // using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(reaction);
                EditorGUI.indentLevel--;
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}