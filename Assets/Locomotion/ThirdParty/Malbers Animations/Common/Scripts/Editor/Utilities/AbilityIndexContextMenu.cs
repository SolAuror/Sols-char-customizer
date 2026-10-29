// MWC (Made with Claude): New context menu for Int and IntReference fields.
// Right-click any Int or IntReference property -> "Find Mode Ability Index" opens a
// floating window that asks for an MAnimal, lets you pick one of its Modes and Abilities,
// and writes the selected Ability Index back into the property.
#if UNITY_EDITOR
using MalbersAnimations.Controller;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MalbersAnimations
{
    public static class AbilityIndexContextMenu
    {
        [InitializeOnLoadMethod]
        public static void Init()
        {
            EditorApplication.contextualPropertyMenu += ContextualPropertyMenu;
        }

        private static void ContextualPropertyMenu(GenericMenu menu, SerializedProperty property)
        {
            //MWC: show the option on plain ints and on IntReference (Generic property whose type name is IntReference)
            var isInt = property.propertyType == SerializedPropertyType.Integer;
            var isIntReference = property.propertyType == SerializedPropertyType.Generic && property.type == nameof(Scriptables.IntReference);

            if (!isInt && !isIntReference) return;

            //MWC: capture what we need now; the SerializedProperty itself may be disposed after the menu closes
            var targets = property.serializedObject.targetObjects;
            var path = property.propertyPath;

            menu.AddItem(new GUIContent("Find Mode Ability Index"), false, () =>
            {
                AbilityIndexFinderWindow.Open(targets, path, isIntReference);
            });
        }
    }

    /// <summary> Floating window to find an Ability Index from an MAnimal's Modes and write it into an Int or IntReference property </summary>
    public class AbilityIndexFinderWindow : EditorWindow
    {
        private Object[] targets;
        private string propertyPath;
        private bool isIntReference;

        private MAnimal animal;
        private ModeID modeID;
        private int abilitySelection;

        public static void Open(Object[] targets, string propertyPath, bool isIntReference)
        {
            var window = GetWindow<AbilityIndexFinderWindow>(true, "Find Mode Ability Index", true);
            window.targets = targets;
            window.propertyPath = propertyPath;
            window.isIntReference = isIntReference;
            window.minSize = new Vector2(330, 190);
            window.maxSize = new Vector2(330, 190);

            //MWC: pre-fill the Animal from the component holding the property (works for components; ScriptableObjects stay null)
            if (targets.Length > 0 && targets[0] is Component component)
                window.animal = component.FindComponent<MAnimal>();

            if (window.animal == null)
                window.animal = Selection.activeGameObject != null ? Selection.activeGameObject.FindComponent<MAnimal>() : null;

            //MWC: last fallback -> use the first MAnimal found on the open Scenes
            if (window.animal == null)
            {
                var sceneAnimals = FindSceneAnimals();
                if (sceneAnimals.Length > 0) window.animal = sceneAnimals[0];
            }

            window.ShowUtility();
        }

        //MWC: all MAnimals on the loaded Scenes (inactive included), sorted by name
        private static MAnimal[] FindSceneAnimals() =>
            FindObjectsByType<MAnimal>(FindObjectsInactive.Include
#if !UNITY_6000_4_OR_NEWER
             , FindObjectsSortMode.None
#endif
                )
            .OrderBy(a => a.name).ToArray();

        //MWC: dropdown listing every MAnimal on the Scene so the Animal field can be filled with one click
        private void ShowSceneAnimalsMenu()
        {
            var sceneAnimals = FindSceneAnimals();
            var menu = new GenericMenu();

            if (sceneAnimals.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("No MAnimals found on the Scene"));
            }
            else
            {
                for (int i = 0; i < sceneAnimals.Length; i++)
                {
                    var sceneAnimal = sceneAnimals[i];

                    //MWC: duplicated names would collapse into one menu entry -> disambiguate them
                    var label = sceneAnimals.Count(a => a.name == sceneAnimal.name) > 1
                        ? $"{sceneAnimal.name} " +
#if UNITY_6000_4_OR_NEWER
                        $"[{sceneAnimal.GetEntityId()}]"
#else
                        $"[{sceneAnimal.GetInstanceID()}]"
#endif
                        : sceneAnimal.name;

                    menu.AddItem(new GUIContent(label), animal == sceneAnimal, () =>
                    {
                        animal = sceneAnimal;
                        modeID = null;
                        abilitySelection = 0;
                        Repaint();
                    });
                }
            }

            menu.ShowAsContext();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox($"Property: {(targets != null && targets.Length > 0 && targets[0] != null ? targets[0].name : "?")}.{propertyPath}", MessageType.None);

            //MWC: removed the ModeID input; the [Modes on Animal] popup below is the only Mode selector now
            //MWC: Animal field + dropdown button listing all the MAnimals on the Scene
            using (new GUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                animal = (MAnimal)EditorGUILayout.ObjectField(new GUIContent("Animal", "Character with the Modes to search"), animal, typeof(MAnimal), true);
                if (EditorGUI.EndChangeCheck())
                {
                    modeID = null;
                    abilitySelection = 0;
                }

                if (GUILayout.Button(new GUIContent("▼", "Select an MAnimal from the Scene"), EditorStyles.miniButton, GUILayout.Width(22)))
                    ShowSceneAnimalsMenu();
            }

            if (animal == null)
            {
                EditorGUILayout.HelpBox("Assign an Animal Character", MessageType.Info);
                return;
            }

            //MWC: quick Mode picker filled from the Animal's own Modes list (writes into the Mode ID field)
            if (animal.modes != null && animal.modes.Count > 0)
            {
                var modeNames = animal.modes.Select(m => m.ID != null ? m.ID.name : "<No ID>").ToArray();
                var currentMode = animal.modes.FindIndex(m => m.ID == modeID && modeID != null);

                EditorGUI.BeginChangeCheck();
                var newMode = EditorGUILayout.Popup(new GUIContent("Modes on Animal"), currentMode, modeNames);
                if (EditorGUI.EndChangeCheck() && newMode >= 0)
                {
                    modeID = animal.modes[newMode].ID;
                    abilitySelection = 0;
                }
            }

            else
            {
                EditorGUILayout.HelpBox($"[{animal.name}] has no Modes", MessageType.Warning);
                return;
            }

            if (modeID == null)
            {
                EditorGUILayout.HelpBox("Pick a Mode from the Animal", MessageType.Info);
                return;
            }

            var mode = animal.modes?.Find(m => m.ID == modeID);

            if (mode == null)
            {
                EditorGUILayout.HelpBox($"[{animal.name}] does not have the Mode [{modeID.name}]", MessageType.Warning);
                return;
            }

            if (mode.Abilities == null || mode.Abilities.Count == 0)
            {
                EditorGUILayout.HelpBox($"Mode [{modeID.name}] has no Abilities", MessageType.Warning);
                return;
            }

            var abilityNames = mode.Abilities.Select(a => $"{a.Name}  [{a.Index.Value}]").ToArray();
            abilitySelection = Mathf.Clamp(abilitySelection, 0, mode.Abilities.Count - 1);
            abilitySelection = EditorGUILayout.Popup(new GUIContent("Ability"), abilitySelection, abilityNames);

            var selectedIndex = mode.Abilities[abilitySelection].Index.Value;

            EditorGUILayout.Space();

            if (GUILayout.Button($"Set Ability Index [{selectedIndex}]", GUILayout.Height(28)))
            {
                ApplyIndex(selectedIndex);
                Close();
            }
        }

        //MWC: re-resolve the property from the stored targets+path so the value is written safely with Undo support
        private void ApplyIndex(int newIndex)
        {
            foreach (var target in targets)
            {
                if (target == null) continue;

                var so = new SerializedObject(target);
                var property = so.FindProperty(propertyPath);
                if (property == null) continue;

                if (!isIntReference)
                {
                    property.intValue = newIndex;
                }
                else
                {
                    var useConstant = property.FindPropertyRelative("UseConstant");
                    var variable = property.FindPropertyRelative("Variable");

                    if (useConstant.boolValue || variable.objectReferenceValue == null)
                    {
                        property.FindPropertyRelative("ConstantValue").intValue = newIndex;
                    }
                    else //MWC: the reference points to an IntVar asset -> write the value inside the asset instead
                    {
                        var varSO = new SerializedObject(variable.objectReferenceValue);
                        varSO.FindProperty("value").intValue = newIndex;
                        varSO.ApplyModifiedProperties();
                    }
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }
    }
}
#endif
