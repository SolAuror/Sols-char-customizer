#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MalbersAnimations.Controller
{
    // MWC - partial: States tab — list, draw callbacks, drag-drop, cache helpers
    public partial class MAnimalEditor
    {
        #region States Stuff

        private void ShowStates()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(OverrideStartState, G_OverrideStartState);
                    CloneStates.boolValue = GUILayout.Toggle(CloneStates.boolValue, G_CloneStates, EditorStyles.miniButton, GUILayout.Width(85));
                    MalbersEditor.DrawDebugIcon(DebugStates, MTools.MBlue);
                }
                //  EditorGUI.indentLevel--;

                if (!CloneStates.boolValue)
                {
                    EditorGUILayout.HelpBox("Disable Clone States only when you are setting values and debugging while playing. ", MessageType.Warning);
                }


                using (new GUILayout.HorizontalScope())
                {
                    var Head = " States";

                    if (m.states != null && m.states.Count > 0 && SelectedState.intValue != -1 && SelectedState.intValue < m.states.Count)
                    {
                        var s = m.states[SelectedState.intValue];
                        //  Head += $" [{s.GetType().Name}]";

                        if (s != null && s.ID != null)
                        {
                            Head += $"  ID: [{s.ID.ID}]";
                            Head += $"  Tag: [{s.ID.name}]";
                        }
                    }


                    showStateList.boolValue = MalbersEditor.Foldout(showStateList.boolValue, Head);

                }

                if (showStateList.boolValue)
                {
                    Reo_List_States.DoLayoutList();        //Paint the Reordable List
                    DropAreaGUIStates();

                }
                EditorGUILayout.Space();

                Reo_List_States.index = SelectedState.intValue;
                var index = SelectedState.intValue;

                UpdateCacheState();

                if (index != -1 && Reo_List_States.serializedProperty.arraySize > index)
                {
                    var element = Reo_List_States.serializedProperty.GetArrayElementAtIndex(index);

                    var StateObj = m.states[index];

                    if (element != null & StateObj != null)
                    {
                        bool showStateEditor = false;

                        using (new GUILayout.HorizontalScope())
                        {
                            if (m.MainCollider && StateObj.OverrideCapsule &&
                                GUILayout.Button(new GUIContent("CC", "Copy Main Capsule values to the Override Capsule values of the state "), GUILayout.Width(30)))
                            {
                                StateObj.newCapsule = new(m.MainCollider);
                                EditorUtility.SetDirty(StateObj);
                            }
                            if (!showStateList.boolValue)
                            {
                                if (StatePopupList.Length != m.states.Count) StateArray_Popup();
                                SelectedState.intValue = EditorGUILayout.Popup(SelectedState.intValue, StatePopupList, popupStyle, GUILayout.Width(20));
                            }

                            showStateEditor = MalbersEditor.Foldout(ShowStateInInspector, $"ID [{StateObj.ID.ID}] ");
                        }


                        GUILayout.Space(-20);
                        EditorGUIUtility.labelWidth = 110;

                        using (new EditorGUI.DisabledGroupScope(true))
                            EditorGUILayout.ObjectField(new GUIContent("    "), StateObj, typeof(StateID), false, GUILayout.MinWidth(50));

                        EditorGUIUtility.labelWidth = 0;




                        //Show the inspector in the Animal Controller
                        if (showStateEditor)
                        {
                            if (element.objectReferenceValue != null)
                            {
                                // MMDrawnPropertiesEditor.MMDrawnProperties(element.objectReferenceValue);
                                var key = element.propertyPath;

                                if (State_Editor.TryGetValue(key, out Editor editor))
                                {
                                    editor = State_Editor[key];
                                }
                                else
                                {
                                    Editor.CreateCachedEditor(element.objectReferenceValue, null, ref editor);
                                    State_Editor.Add(key, editor);
                                }
                                editor.OnInspectorGUI();
                                editor.serializedObject.ApplyModifiedProperties();

                                //if (Application.isPlaying)
                                //     Repaint();
                            }
                        }
                    }
                }
            }
        }

        #region DrawStates
        //-------------------------STATES-----------------------------------------------------------
        private void Draw_Header_State(Rect rect)
        {
            var r = new Rect(rect);
            r.x += 13;
            r.width -= 60;

            var Head = "    States";

            if (m.states != null && m.states.Count > 0 && SelectedState.intValue != -1 && SelectedState.intValue < m.states.Count)
            {
                var s = m.states[SelectedState.intValue];
                //  Head += $" [{s.GetType().Name}]";

                if (s != null && s.ID != null)
                {
                    Head += $"  ID: [{s.ID.ID}]";
                    Head += $"  Tag: [{s.ID.name}]";
                }
            }

            EditorGUI.LabelField(r, new GUIContent(Head, "States are the core logic the Animals can do [Double click to modify them]"), EditorStyles.boldLabel);

            Rect R_2 = new(rect.width - 8, rect.y, 60, EditorGUIUtility.singleLineHeight - 3);
            EditorGUI.LabelField(R_2, new GUIContent("Priority", "Priority of the States, Higher value -> Higher priority"));
        }

        private void Selected_State(ReorderableList list)
        {
            SelectedState.intValue = list.index;

            var stateProperty = S_State_List.GetArrayElementAtIndex(list.index);

            //Update the Local State ID also
            states_C.GetArrayElementAtIndex(list.index).FindPropertyRelative("state").objectReferenceValue = stateProperty.objectReferenceValue;
        }

        private void Draw_Element_State(Rect rect, int index, bool isActive, bool isFocused)
        {
            rect.y += 1;
            rect.height += 2;
            if (S_State_List.arraySize <= index) return;

            var stateProperty = S_State_List.GetArrayElementAtIndex(index);

            var activeRect = new Rect(rect);
            activeRect.width -= 20;
            activeRect.x += 20;

            var ActiveRect = new Rect(rect.x - 2, rect.y - 3, 20, activeRect.height);
            var StateRect = new Rect(activeRect.x - 5, activeRect.y, activeRect.width - 30, activeRect.height - 5);
            var PriorityRect = new Rect(activeRect.width + 45, activeRect.y, 25, activeRect.height - 2);

            if (Application.isPlaying) StateRect.width = activeRect.width / 2f + 10;


            State state = stateProperty.objectReferenceValue as State;

            // Remove the ability if it no longer exists.
            if (state == null)
            {
                EditorGUI.ObjectField(StateRect, stateProperty, GUIContent.none);
                return;
            }

            var stat_C = states_C.GetArrayElementAtIndex(index);

            var priority = stat_C.FindPropertyRelative("priority");

            var active = stat_C.FindPropertyRelative("active");
            active.boolValue = EditorGUI.Toggle(ActiveRect, GUIContent.none, Application.isPlaying ? state.Active : active.boolValue);

            //state.Active = EditorGUI.Toggle(ActiveRect, GUIContent.none, state.Active);

            var st_label = "";

            if (Application.isPlaying)
            {
                if (m.ActiveState == state)
                {
                    if (state.IsPending) st_label = "[Pending]";
                    else st_label = "[Active]";

                    if (state.IsPersistent) st_label += "[Pers]";
                }
                else if (state.IsSleepFromState) st_label = "[Sleep by State]";
                else if (state.IsSleepFromMode) st_label = "[Sleep by Mode]";
                else if (state.IsSleepFromStance) st_label = "[Sleep by Stance]";
                else if (state.OnActiveQueue) st_label = "[Active Queue]";
                else if (state.OnQueue) st_label = "[Queued]";
                else if (state.OnHoldByReset) st_label = "[On Hold Reset]";
            }

            var dbC = GUI.backgroundColor;
            GUI.backgroundColor = isActive ? MTools.MBlue : dbC;

            // var dC = GUI.contentColor;
            //   if (isActive) GUI.contentColor = new Color(0.7f, 0.7f, 2f);
            EditorGUI.ObjectField(StateRect, stateProperty, GUIContent.none);
            //  GUI.contentColor = dC;
            GUI.backgroundColor = dbC;
            var style = new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };

            if (Application.isPlaying && m.isActiveAndEnabled && state != null)
            {
                var activeState = m.ActiveState;

                if (activeState != null)
                {
                    if (state.IsPersistent)
                    {
                        style.normal.textColor = Color.green;
                    }

                    if (state.Priority < activeState.Priority && activeState.IsPersistent)
                    {
                        style.normal.textColor = new Color(style.normal.textColor.r, style.normal.textColor.g, style.normal.textColor.b, style.normal.textColor.a / 2);
                    }
                }

                var Rect_Label = new Rect() { x = activeRect.width / 2 + 80, width = activeRect.width / 2 - 36, y = activeRect.y, height = activeRect.height };

                EditorGUI.LabelField(Rect_Label, st_label, style);
            }

            if (Application.isPlaying)
            {
                var TestBRect = new Rect(rect.width - 20, rect.y, 38, EditorGUIUtility.singleLineHeight);

                var color = GUI.color;
                GUI.color = MTools.MGreen * 1.5f;
                if (GUI.Button(TestBRect, "Test"))
                {
                    m.State_Force(state.ID);
                }
                GUI.color = color;
            }



            PriorityRect.height = 18;




            // state.Priority = EditorGUI.IntField(PriorityRect, GUIContent.none, state.Priority);
            priority.intValue = EditorGUI.IntField(PriorityRect, GUIContent.none, priority.intValue);

            activeRect = rect;
            activeRect.x += activeRect.width - 34;
            activeRect.width = 20;
        }

        private void OnReorderCallback_States(ReorderableList list, int oldIndex, int newIndex)
        {
            //Check if the Cache and States have the same size

            var OldState = m.states[oldIndex];
            var NewState = m.states[newIndex];

            if (OldState != null)
            {
                OldState.Priority = S_State_List.arraySize - newIndex;
                EditorUtility.SetDirty(OldState);
            }

            if (NewState != null)
            {
                NewState.Priority = S_State_List.arraySize - oldIndex;
                EditorUtility.SetDirty(NewState);
            }

            UpdateCacheState();

            //Do Cache stuff
            states_C.MoveArrayElement(oldIndex, newIndex);
            states_C.GetArrayElementAtIndex(oldIndex).FindPropertyRelative("priority").intValue = S_State_List.arraySize - oldIndex;
            states_C.GetArrayElementAtIndex(newIndex).FindPropertyRelative("priority").intValue = S_State_List.arraySize - newIndex;

            states_C.serializedObject.ApplyModifiedProperties();



            EditorUtility.SetDirty(target);
        }

        private void OnAddCallback_State(ReorderableList list)
        {
            addMenu = new GenericMenu();

            for (int i = 0; i < StatesType.Count; i++)
            {
                Type st = StatesType[i];

                bool founded = false;
                for (int j = 0; j < m.states.Count; j++)
                {
                    if (m.states[j].GetType() == st)
                    {
                        founded = true;
                    }
                }

                if (!founded)
                {
                    var att = st.GetCustomAttribute<AddTypeMenuAttribute>(false); //Find the correct name
                    string LabelName = att != null ? att.MenuName : st.Name;
                    addMenu.AddItem(new GUIContent(LabelName), false, () => AddState(st, st.Name));
                }
            }
            addMenu.ShowAsContext();
        }

        /// <summary> The ReordableList remove button has been pressed. Remove the selected ability.</summary>
        private void OnRemove_State(ReorderableList list)
        {
            // bool DeleteAsset = false;
            // State state = S_StateList.GetArrayElementAtIndex(list.index).objectReferenceValue as State;

            S_State_List.DeleteArrayElementAtIndex(list.index);
            states_C.DeleteArrayElementAtIndex(list.index);
            states_C.serializedObject.ApplyModifiedProperties();

            list.index -= 1;

            EditorUtility.SetDirty(m);
        }

        /// <summary>Adds a new State of the specified type.</summary>
        private void AddState(Type selectedState, string name)
        {
            State state = (State)CreateInstance(selectedState);

            var nameS = m.name.RemoveSpecialCharacters();

            if (m.states != null && m.states.Count > 0)
            {
                var anySt = m.states[0];
                var path = AssetDatabase.GetAssetPath(anySt);

                path = System.IO.Path.GetDirectoryName(path);
                //Debug.Log("path = " + path);
                AssetDatabase.CreateAsset(state, $"{path}/{nameS} {name}.asset");
            }
            else
            {
                AssetDatabase.CreateAsset(state, $"Assets/{nameS} {name}.asset");
            }

            AssetDatabase.SaveAssets();

            // Pull all the information from the target of the serializedObject.
            S_State_List.serializedObject.Update();
            // Add a null array element to the start of the array then populate it with the object parameter.
            S_State_List.InsertArrayElementAtIndex(0);
            S_State_List.GetArrayElementAtIndex(0).objectReferenceValue = state;
            // Push all the information on the serializedObject back to the target.
            S_State_List.serializedObject.ApplyModifiedProperties();

            state.Priority = S_State_List.arraySize;  //Set the priority!! Important!

            AddState_Cache(state);
            state.SetSpeedSets(m);

            EditorUtility.SetDirty(target);
            EditorUtility.SetDirty(state);
        }

        #endregion

        /// <summary>
        /// This is used to Have local Active and Priority values
        /// </summary>
        private void UpdateCacheState()
        {
            //Use the same that is already on the states
            if (m.states_C == null || (m.states_C.Count != m.states.Count && m.states.Count > 0))
            {
                m.states_C = new List<MAnimal.StateCache>();

                foreach (var st in m.states)
                {
                    m.states_C.Add(new MAnimal.StateCache() { active = st.Active, priority = st.Priority, state = st });
                }
                EditorUtility.SetDirty(m);
                // return;

                Debug.Log($"<B>[{m.name}]</B> Local State Priority Value and Active Value Updated (AC v1.4.2c) Save the Prefab");
            }
        }

        private void AddState_Cache(State newState)
        {
            states_C.InsertArrayElementAtIndex(0);
            states_C.GetArrayElementAtIndex(0).FindPropertyRelative("state").objectReferenceValue = newState;
            states_C.GetArrayElementAtIndex(0).FindPropertyRelative("active").boolValue = true;
            states_C.GetArrayElementAtIndex(0).FindPropertyRelative("priority").intValue = newState.Priority;
            states_C.serializedObject.ApplyModifiedProperties();
        }


        public void DropAreaGUIStates()
        {
            EditorGUILayout.Space(5);

            Event evt = Event.current;
            Rect drop_area = GUILayoutUtility.GetRect(0f, 20, GUILayout.ExpandWidth(true));

            var st = new GUIStyle(EditorStyles.toolbarButton)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14
            };


            GUI.Box(drop_area, "> Drag created states here < ", st);

            switch (evt.type)
            {
                case EventType.DragUpdated:
                    // ... change whether or not the drag *can* be performed by changing the visual mode of the cursor based on the IsDragValid function.
                    DragAndDrop.visualMode = IsDragValid() ? DragAndDropVisualMode.Generic : DragAndDropVisualMode.Rejected;
                    break;
                case EventType.DragPerform:
                    if (!drop_area.Contains(evt.mousePosition))
                        return;

                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();

                        foreach (UnityEngine.Object dragged_object in DragAndDrop.objectReferences)
                        {
                            if (dragged_object is State)
                            {
                                State newState = dragged_object as State;

                                if (m.states.Contains(newState)) continue;

                                EditorUtility.SetDirty(m);

                                // Pull all the information from the target of the serializedObject.
                                S_State_List.serializedObject.Update();
                                // Add a null array element to the start of the array then populate it with the object parameter.
                                S_State_List.InsertArrayElementAtIndex(0);
                                S_State_List.GetArrayElementAtIndex(0).objectReferenceValue = newState;
                                // Push all the information on the serializedObject back to the target.
                                S_State_List.serializedObject.ApplyModifiedProperties();

                                AddState_Cache(newState);

                                Reo_List_States.index = -1;

                                EditorUtility.SetDirty(newState);
                            }
                        }
                    }
                    break;
            }
        }

        private bool IsDragValid()
        {
            // Go through all the objects being dragged...
            for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
            {
                // ... and if any of them are not script assets, return that the drag is invalid.
                if (DragAndDrop.objectReferences[i].GetType().BaseType != typeof(State))
                    return false;
            }

            // If none of the dragging objects returned that the drag was invalid, return that it is valid.
            return true;
        }

        #endregion
    }
}
#endif
