// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor; // MWC — embedded custom editor below
#endif

namespace MalbersAnimations.Items
{
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/super-item/super-item-system/super-item-processor-by-animation")]
    [AddComponentMenu("Malbers/Super Item/Super Item Processor By Animation Behaviour")]
    public class SuperItemProcessorByAnimation : StateMachineBehaviour
    {
        public IDList<SuperItemID> ItemsID;
        public StringReference Set = new("Default");
        public StringReference Action = new("Action1");

        [Range(0, 1)]
        public float NormalizedTime;
        private SuperItemManager SIM;

        private class SuperItemMessage
        {
            public SuperItem item;
            public bool sent;
        }

        private List<SuperItemMessage> items;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            items ??= new();

            if (SIM == null) SIM = animator.GetComponent<SuperItemManager>();

            if (SIM)
            {
                foreach (var item in ItemsID.items)
                {
                    //find if an item with the same ID is equipped
                    var itemFound = SIM.Get_ItemEquipped(item);

                    if (itemFound != null)
                    {
                        items.Add(new() { item = itemFound, sent = false });
                    }
                }
                foreach (var item in items)
                {
                    if (NormalizedTime == 0)
                    {
                        OnAnimation(item.item);
                        item.sent = true;
                    }
                }
            }
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            //If the animation in in transition , we don't want to send the message
            if (animator.IsInTransition(layerIndex)) return;


            if (items == null) return;

            foreach (var item in items)
            {
                if (!item.sent && NormalizedTime <= stateInfo.normalizedTime)
                {
                    OnAnimation(item.item);
                    item.sent = true;
                }
            }
        }

        private void OnAnimation(SuperItem item)
        {
            if (item.ActiveActionSet.Name == Set.Value)
            {
                if (item.MainAction != null && item.MainAction.Name == Action.Value && item.MainAction.IsPlaying)
                {
                    //Debug.Log($"Main Action On Animation");
                    item.MainAction.ActionOnAnimation(item, item.ActiveActionSet);
                }
                if (item.SlaveAction != null && item.SlaveAction.Name == Action.Value && item.SlaveAction.IsPlaying)
                {
                    //Debug.Log($"Slave Action On Animation");
                    item.SlaveAction.ActionOnAnimation(item, item.ActiveActionSet);
                }
            }
        }
    }

#if UNITY_EDITOR
    // MWC — custom inspector: uses the Preview Super Item to offer Set/Action name dropdowns
    [CustomEditor(typeof(SuperItemProcessorByAnimation))]
    public class SuperItemProcessorByAnimationED : Editor
    {
        private SerializedProperty ItemsID, Set, Action, NormalizedTime;

        // MWC — the behaviour lives inside the Controller asset, so a serialized field cannot reference scene objects.
        // Keep the preview item as editor-session state instead: static (shared while editing states) + SessionState (survives domain reloads)
        private const string PreviewKey = "SuperItemProcessorByAnimation_Preview";
        private static SuperItem PreviewItem;

        private void OnEnable()
        {
            ItemsID = serializedObject.FindProperty("ItemsID");
            Set = serializedObject.FindProperty("Set");
            Action = serializedObject.FindProperty("Action");
            NormalizedTime = serializedObject.FindProperty("NormalizedTime");

            // MWC — restore the preview item after a domain reload (scene objects resolve via GlobalObjectId)
            if (PreviewItem == null)
            {
                var savedID = SessionState.GetString(PreviewKey, string.Empty);
                if (GlobalObjectId.TryParse(savedID, out var globalID))
                    PreviewItem = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalID) as SuperItem;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(ItemsID);

                // MWC — non-serialized ObjectField so scene objects are allowed (asset fields reject them)
                EditorGUI.BeginChangeCheck();
                PreviewItem = (SuperItem)EditorGUILayout.ObjectField(
                    new GUIContent("Preview Item", "Editor Only. Reference a Super Item (scene object or prefab) to select the Set and Action names from dropdowns. Not saved on the Controller"),
                    PreviewItem, typeof(SuperItem), true);
                if (EditorGUI.EndChangeCheck())
                {
                    SessionState.SetString(PreviewKey,
                        PreviewItem != null ? GlobalObjectId.GetGlobalObjectIdSlow(PreviewItem).ToString() : string.Empty);
                }

                var preview = PreviewItem;

                if (preview != null && preview.actionSets != null && preview.actionSets.Count > 0)
                {
                    // Set dropdown from the preview item's action sets
                    var setNames = new string[preview.actionSets.Count];
                    for (int i = 0; i < setNames.Length; i++)
                        setNames[i] = preview.actionSets[i].Name;

                    DrawStringPopup(Set, setNames, new GUIContent("Set", "Action Set the item must have active"));

                    // Action dropdown from the selected set's actions
                    var currentSet = preview.actionSets.Find(s => s.Name == GetValue(Set));

                    if (currentSet != null && currentSet.actions.Count > 0)
                    {
                        var actionNames = new string[currentSet.actions.Count];
                        for (int i = 0; i < actionNames.Length; i++)
                            actionNames[i] = currentSet.actions[i].Name;

                        DrawStringPopup(Action, actionNames, new GUIContent("Action", "Action inside the Set that receives the animation message"));
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(Action);
                        if (currentSet == null)
                            EditorGUILayout.HelpBox($"The Preview Item [{preview.name}] has no Action Set named [{GetValue(Set)}]", MessageType.Warning);
                    }
                }
                else
                {
                    // No preview item assigned: plain fields
                    EditorGUILayout.PropertyField(Set);
                    EditorGUILayout.PropertyField(Action);
                }

                EditorGUILayout.PropertyField(NormalizedTime);
            }

            serializedObject.ApplyModifiedProperties();
        }

        // MWC — current string of a StringReference property (constant or variable asset)
        private static string GetValue(SerializedProperty reference)
        {
            if (reference.FindPropertyRelative("UseConstant").boolValue)
                return reference.FindPropertyRelative("ConstantValue").stringValue;

            var variable = reference.FindPropertyRelative("Variable").objectReferenceValue as StringVar;
            return variable != null ? variable.Value : string.Empty;
        }

        // MWC — draw a StringReference as a name dropdown; falls back to the default field when a StringVar is used
        private static void DrawStringPopup(SerializedProperty reference, string[] names, GUIContent label)
        {
            var useConstant = reference.FindPropertyRelative("UseConstant");

            if (!useConstant.boolValue)
            {
                EditorGUILayout.PropertyField(reference, label); // StringVar asset: keep the default drawer
                return;
            }

            var constant = reference.FindPropertyRelative("ConstantValue");
            int index = System.Array.IndexOf(names, constant.stringValue);

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup(label, index, names);
            if (EditorGUI.EndChangeCheck() && newIndex >= 0)
                constant.stringValue = names[newIndex];

            if (index == -1)
                EditorGUILayout.HelpBox($"[{constant.stringValue}] was not found on the Preview Item. Select a valid {label.text} name", MessageType.Warning);
        }
    }
#endif
}
