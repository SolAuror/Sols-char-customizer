// Last Change: 9/17/2026 5:33pm
using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace MalbersAnimations
{
    [AddComponentMenu("Malbers/Utilities/Tools/Component Selector")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/utilities/component-selector")]
    public class ComponentSelector : MonoBehaviour
    {
        public List<ComponentSet> internalComponents;
        public bool edit = true;

        public ComponentSet this[int index] => internalComponents[index];


        [ContextMenu("Show|Hide Editor")]
        private void ShowHideEditor()
        {
            edit ^= true;
#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
#endif
        }

        private void Reset()
        {
            internalComponents = new List<ComponentSet>();
        }

    }

    [System.Serializable]
    public class ComponentSet
    {
        public string name = "Description Here";
        [TextArea] public string tooltip;
        public bool active = true;
        public GameObject[] gameObjects;
        public MonoBehaviour[] monoBehaviours;
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ComponentSelector))]
    public class SelectComponentsEditor : Editor
    {
        private static GUIContent _icon_Add;
        public static GUIContent Icon_Add
        {
            get
            {
                if (_icon_Add == null)
                {
                    _icon_Add = EditorGUIUtility.IconContent("d_ViewToolOrbit", "Enable/Disable");
                    _icon_Add.tooltip = "Enable/Disable";
                }

                return _icon_Add;
            }
        }


        SerializedProperty internalComponents, edit;
        ComponentSelector M;
        ReorderableList ReoInternalComponents;

        private void OnEnable()
        {
            M = (ComponentSelector)target;
            internalComponents = serializedObject.FindProperty("internalComponents");
            edit = serializedObject.FindProperty("edit");
            _flowBtnLeft = null; _flowBtnRight = null;


            ReoInternalComponents = new ReorderableList(serializedObject, internalComponents)
            {
                drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
                {
                    // MWC: right click a row -> remove menu (on MouseDown: ContextClick never arrives once the row/text field consumes the click)
                    if (IsRightClick(Event.current) && rect.Contains(Event.current.mousePosition))
                    {
                        ShowSetContextMenu(index, flowMode: false);
                        Event.current.Use();
                    }

                    var element = internalComponents.GetArrayElementAtIndex(index);
                    var active = element.FindPropertyRelative("active");
                    var name = element.FindPropertyRelative("name");

                    var activeRect1 = new Rect(rect.x, rect.y - 1, 20, rect.height);
                    var IDRect = new Rect(rect.x + 20, rect.y, rect.width - 20, EditorGUIUtility.singleLineHeight);


                    active.boolValue = EditorGUI.Toggle(activeRect1, GUIContent.none, active.boolValue);
                    EditorGUI.PropertyField(IDRect, name, GUIContent.none);

                },
                drawHeaderCallback = (Rect rect) =>
                {
                    var r = new Rect(rect) { x = rect.x + 30, width = 60 };
                    var a = new Rect(rect) { width = 65 };

                    EditorGUI.LabelField(a, new GUIContent("Act", "Is the Component Selection ON or OFF"));
                    EditorGUI.LabelField(r, new GUIContent("Name", "Name of the Button"));
                },
                onAddCallback = (ReorderableList list) =>
                {
                    M.internalComponents.Add(new ComponentSet());
                    EditorUtility.SetDirty(M);
                }
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            Rect dropArea; // MWC: the whole inspector is a drop target for new sets

            using (var scope = new EditorGUILayout.VerticalScope())
            {
                if (edit.boolValue)
                {
                    ReoInternalComponents.DoLayoutList();

                    if (ReoInternalComponents.index != -1 && ReoInternalComponents.index < internalComponents.arraySize) // MWC: guard after a removal
                    {
                        var elem = internalComponents.GetArrayElementAtIndex(ReoInternalComponents.index);

                        var gos = elem.FindPropertyRelative("gameObjects");
                        var monoBehaviours = elem.FindPropertyRelative("monoBehaviours");
                        var tooltip = elem.FindPropertyRelative("tooltip");
                        EditorGUILayout.PropertyField(gos, true);
                        EditorGUILayout.PropertyField(monoBehaviours, true);
                        EditorGUILayout.Space();

                        EditorGUILayout.PropertyField(tooltip);
                    }
                }
                else
                {
                    DrawFlowButtons(); // MWC: flow/wrap layout replaces fixed 2-column (always drawn: it also hosts the [Drop Here] box)
                    DrawShownElements(); // MWC: elements of the set picked with right click > Show Elements
                }

                dropArea = scope.rect;
            }

            HandleDragAndDrop(dropArea);

            serializedObject.ApplyModifiedProperties();
        }

        // ── MWC: Drag & Drop new sets ─────────────────────────────────────────────
        private bool _dropHover;

        private static readonly GUIContent DropHereContent = new("Drop Here", "Drop GameObjects / Components here to add a new Set");
        private static GUIStyle _dropHereStyle;
        private static GUIStyle DropHereStyle => _dropHereStyle ??= new GUIStyle(EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter, fontSize = 11, fixedHeight = 0 };

        /// <summary>Draws the small [Drop Here] box that sits after the last button; highlighted while a valid drag hovers the inspector.</summary>
        private void DrawDropHereBox(Rect r)
        {
            var saved = GUI.color;
            GUI.color = _dropHover ? new Color(0.25f, 0.65f, 1f, 1f) : new Color(saved.r, saved.g, saved.b, 0.6f);
            GUI.Label(r, DropHereContent, DropHereStyle);
            GUI.color = saved;
        }

        /// <summary>Accepts dragged GameObjects / MonoBehaviours anywhere on the inspector and creates one new set per object.</summary>
        private void HandleDragAndDrop(Rect area)
        {
            Event evt = Event.current;

            if (evt.type == EventType.DragExited) { _dropHover = false; Repaint(); return; }
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;

            bool inside = area.Contains(evt.mousePosition);
            bool valid = false;

            if (inside)
            {
                foreach (var o in DragAndDrop.objectReferences)
                    if (o is GameObject || o is MonoBehaviour) { valid = true; break; }
            }

            if (_dropHover != valid) { _dropHover = valid; Repaint(); }
            if (!valid) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();

                foreach (var o in DragAndDrop.objectReferences)
                {
                    if (o is GameObject go) AddSet(go.name, go, null);
                    else if (o is MonoBehaviour mb) AddSet($"{mb.gameObject.name} ({mb.GetType().Name})", null, mb);
                }

                _dropHover = false;
                GUI.changed = true;
            }

            evt.Use();
        }

        /// <summary>Appends a new ComponentSet through the serialized property so Undo/Prefab overrides work.</summary>
        private void AddSet(string setName, GameObject go, MonoBehaviour mb)
        {
            int i = internalComponents.arraySize++;
            var e = internalComponents.GetArrayElementAtIndex(i);

            e.FindPropertyRelative("name").stringValue = setName;
            e.FindPropertyRelative("tooltip").stringValue = string.Empty;
            e.FindPropertyRelative("active").boolValue = go ? go.activeSelf : mb.enabled;

            var gos = e.FindPropertyRelative("gameObjects");
            gos.arraySize = go ? 1 : 0;
            if (go) gos.GetArrayElementAtIndex(0).objectReferenceValue = go;

            var mbs = e.FindPropertyRelative("monoBehaviours");
            mbs.arraySize = mb ? 1 : 0;
            if (mb) mbs.GetArrayElementAtIndex(0).objectReferenceValue = mb;

            ReoInternalComponents.index = i;
        }

        // ── MWC: Right click removal ──────────────────────────────────────────────

        /// <summary>True on a right-button MouseDown (or Ctrl+Click on macOS).</summary>
        private static bool IsRightClick(Event evt) =>
            evt.type == EventType.MouseDown && (evt.button == 1 || (evt.button == 0 && evt.control && Application.platform == RuntimePlatform.OSXEditor));

        // MWC: right click state — one set can be inline-renamed, one set can have its elements shown
        private int    _renameIndex  = -1;
        private string _renameOriginal;
        private bool   _renameFocusPending;
        private int    _shownIndex   = -1;
        private const string RenameControlName = "MSetRenameField";

        private static GUIStyle _renameStyle;
        private static GUIStyle RenameStyle => _renameStyle ??= new GUIStyle(EditorStyles.textField) { fontSize = 13, fixedHeight = 0, alignment = TextAnchor.MiddleLeft };

        /// <summary>Shows the context menu for the set at <paramref name="index"/>; <paramref name="flowMode"/> adds Rename / Show Elements.</summary>
        private void ShowSetContextMenu(int index, bool flowMode)
        {
            if (_shownIndex >= 0 && _shownIndex != index) { _shownIndex = -1; Repaint(); } // right clicking another set hides the shown one

            var setName = internalComponents.GetArrayElementAtIndex(index).FindPropertyRelative("name").stringValue;
            var menu = new GenericMenu();

            if (flowMode)
            {
                menu.AddItem(new GUIContent("Rename"), false, () => BeginRename(index));

                if (_shownIndex == index)
                    menu.AddItem(new GUIContent("Hide Elements"), false, () => { _shownIndex = -1; Repaint(); });
                else
                    menu.AddItem(new GUIContent("Show Elements"), false, () => { _shownIndex = index; Repaint(); });
            }
            else
            {
                menu.AddItem(new GUIContent("Show Elements"), false, () => { ReoInternalComponents.index = index; Repaint(); });
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent($"Remove [{setName}]"), false, () => RemoveSet(index));
            menu.ShowAsContext();
        }

        /// <summary>Starts inline renaming of the set at <paramref name="index"/> (called from a GenericMenu, outside OnInspectorGUI).</summary>
        private void BeginRename(int index)
        {
            if (target == null || index < 0 || index >= internalComponents.arraySize) return;

            _renameIndex        = index;
            _renameOriginal     = internalComponents.GetArrayElementAtIndex(index).FindPropertyRelative("name").stringValue;
            _renameFocusPending = true;
            Repaint();
        }

        /// <summary>Finishes inline renaming; <paramref name="revert"/> restores the original name.</summary>
        private void EndRename(bool revert)
        {
            if (revert && _renameIndex >= 0 && _renameIndex < internalComponents.arraySize)
                internalComponents.GetArrayElementAtIndex(_renameIndex).FindPropertyRelative("name").stringValue = _renameOriginal;

            _renameIndex        = -1;
            _renameFocusPending = false;
            GUI.FocusControl(null);
            Repaint();
        }

        /// <summary>Deletes the set at <paramref name="index"/> (called from a GenericMenu, outside OnInspectorGUI).</summary>
        private void RemoveSet(int index)
        {
            if (target == null) return;

            serializedObject.Update();
            if (index < 0 || index >= internalComponents.arraySize) return;

            internalComponents.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();

            if (ReoInternalComponents.index >= internalComponents.arraySize)
                ReoInternalComponents.index = internalComponents.arraySize - 1;

            // keep the shown/renamed set pointing at the same element after the shift
            if (_shownIndex == index) _shownIndex = -1;
            else if (_shownIndex > index) _shownIndex--;

            if (_renameIndex == index) EndRename(false);
            else if (_renameIndex > index) _renameIndex--;

            Repaint();
        }

        /// <summary>Draws the GameObjects / MonoBehaviours / tooltip of the set chosen with Show Elements, with a Hide button.</summary>
        private void DrawShownElements()
        {
            if (_shownIndex < 0) return;
            if (_shownIndex >= internalComponents.arraySize) { _shownIndex = -1; return; }

            var elem    = internalComponents.GetArrayElementAtIndex(_shownIndex);
            var setName = elem.FindPropertyRelative("name").stringValue;

            EditorGUILayout.Space(2);
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent($"[{setName}] Elements"), EditorStyles.boldLabel);
                    if (GUILayout.Button(new GUIContent("Hide", "Hide the elements of this set"), EditorStyles.miniButton, GUILayout.Width(50)))
                        _shownIndex = -1;
                }

                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("gameObjects"), true);
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("monoBehaviours"), true);
                EditorGUILayout.PropertyField(elem.FindPropertyRelative("tooltip"));
                EditorGUI.indentLevel--;
            }
        }

        // MWC: flow layout constants — eye icon embedded on the right of each name button
        private const float EyeButtonWidth = 24f;
        private const float FlowPadding = 3f;
        private const float FlowRowHeight = 25f;   // MWC: taller button height (+4 from user request)
        private const float MinNameWidth = 40f;
        private const float RenameMinWidth = 140f; // MWC: minimum slot width while a set is being renamed inline
        private const float TextPadding = 10f;    // MWC: 3 units each side

        private static GUIStyle _flowBtnLeft;
        private static GUIStyle _flowBtnRight;

        // MWC: drag-and-drop reorder state
        private int  _dragIndex  = -1;
        private int  _dropIndex  = -1;
        private bool _isDragging = false;

        // MWC: fixedHeight = 0 lets the Rect drive the actual button height instead of the miniButton default (16 px)
        private static GUIStyle FlowBtnLeft => _flowBtnLeft ??= new GUIStyle(EditorStyles.miniButtonLeft) { fontSize = 13, fontStyle = FontStyle.Bold, fixedHeight = 0 };
        private static GUIStyle FlowBtnRight => _flowBtnRight ??= new GUIStyle(EditorStyles.miniButtonRight) { fontSize = 13, fixedHeight = 0 };

        private float GetFlowButtonWidth(string label)
        {
            float calcWidth = FlowBtnLeft.CalcSize(new GUIContent(label)).x + TextPadding; // MWC: +3 px each side
            return Mathf.Max(calcWidth, MinNameWidth) + EyeButtonWidth;
        }

        // MWC: flow layout with drag-to-reorder; drop indicator shown as a blue vertical bar
        private void DrawFlowButtons()
        {
            int   count          = internalComponents.arraySize;
            float availableWidth = EditorGUIUtility.currentViewWidth - 22f;
            Event evt            = Event.current;

            // Build per-button widths. MWC: one extra flow item (index == count) is the [Drop Here] box
            int items = count + 1;
            var bws = new float[items];
            for (int i = 0; i < count; i++)
                bws[i] = GetFlowButtonWidth(internalComponents.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);
            bws[count] = DropHereStyle.CalcSize(DropHereContent).x + TextPadding;

            if (_renameIndex >= count) EndRename(false); // MWC: set vanished while renaming
            if (_renameIndex >= 0) bws[_renameIndex] = Mathf.Max(bws[_renameIndex], RenameMinWidth); // MWC: room to type

            // Count rows to reserve the correct total height
            int rows = 1; float rx = 0;
            for (int i = 0; i < items; i++)
            {
                if (rx > 0 && rx + bws[i] > availableWidth) { rx = 0; rows++; }
                rx += bws[i] + FlowPadding;
            }

            Rect area = GUILayoutUtility.GetRect(availableWidth, rows * FlowRowHeight + (rows - 1) * FlowPadding);

            // Compute final rects for every button (+ the drop box)
            var nameRects = new Rect[count];
            var eyeRects  = new Rect[count];
            Rect dropHereRect = default;
            float x = 0, y = 0;
            for (int i = 0; i < items; i++)
            {
                if (x > 0 && x + bws[i] > availableWidth) { x = 0; y += FlowRowHeight + FlowPadding; }

                if (i == count) // MWC: the [Drop Here] box
                {
                    dropHereRect = new Rect(area.x + x, area.y + y, bws[i], FlowRowHeight);
                    break;
                }

                float nw     = bws[i] - EyeButtonWidth;
                nameRects[i] = new Rect(area.x + x, area.y + y, nw,            FlowRowHeight);
                eyeRects[i]  = new Rect(nameRects[i].xMax, area.y + y, EyeButtonWidth, FlowRowHeight);
                x += bws[i] + FlowPadding;
            }

            DrawDropHereBox(dropHereRect);

            // ── MWC: Rename keyboard handling: Enter commits, Escape reverts ─────────
            if (_renameIndex >= 0 && evt.type == EventType.KeyDown)
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) { EndRename(false); evt.Use(); }
                else if (evt.keyCode == KeyCode.Escape) { EndRename(true); evt.Use(); }
            }

            // ── MWC: Right click a button -> context menu (MouseDown, so GUI.Button never grabs the right click) ──
            if (IsRightClick(evt))
            {
                if (_renameIndex >= 0) EndRename(false);

                for (int i = 0; i < count; i++)
                {
                    if (FullRect(nameRects[i], eyeRects[i]).Contains(evt.mousePosition))
                    { ShowSetContextMenu(i, flowMode: true); evt.Use(); break; }
                }
            }

            // ── Drag state machine ──────────────────────────────────────────────────
            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                // MWC: while renaming, a click outside the field commits; a click inside it must not start a drag
                if (_renameIndex >= 0)
                {
                    if (!nameRects[_renameIndex].Contains(evt.mousePosition)) EndRename(false);
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (FullRect(nameRects[i], eyeRects[i]).Contains(evt.mousePosition))
                        { _dragIndex = i; _dropIndex = i; _isDragging = false; break; }
                    }
                }
            }
            else if (evt.type == EventType.MouseDrag && _dragIndex >= 0)
            {
                _isDragging = true;
                _dropIndex  = NearestDropIndex(evt.mousePosition, nameRects, eyeRects, count);
                evt.Use();
                Repaint();
            }
            else if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                if (_isDragging && _dragIndex >= 0 && _dropIndex != _dragIndex && _dropIndex != _dragIndex + 1)
                {
                    int dst = _dropIndex > _dragIndex ? _dropIndex - 1 : _dropIndex;
                    internalComponents.MoveArrayElement(_dragIndex, dst);
                    serializedObject.ApplyModifiedProperties();
                    evt.Use();
                    Repaint();
                }
                _dragIndex = -1; _dropIndex = -1; _isDragging = false;
            }

            // ── Draw buttons ────────────────────────────────────────────────────────
            for (int i = 0; i < count; i++)
            {
                var element    = internalComponents.GetArrayElementAtIndex(i);
                var nameProp   = element.FindPropertyRelative("name");
                var tipProp    = element.FindPropertyRelative("tooltip");
                var activeProp = element.FindPropertyRelative("active");

                bool beingDragged = _isDragging && i == _dragIndex;
                var  saved        = GUI.color;
                if (beingDragged) GUI.color = new Color(saved.r, saved.g, saved.b, 0.35f);

                if (i == _renameIndex) // MWC: inline rename — text field takes the button's place
                {
                    GUI.SetNextControlName(RenameControlName);
                    nameProp.stringValue = EditorGUI.TextField(nameRects[i], nameProp.stringValue, RenameStyle);

                    if (_renameFocusPending)
                    {
                        EditorGUI.FocusTextInControl(RenameControlName);
                        _renameFocusPending = false;
                    }

                    GUI.color = activeProp.boolValue ? (saved + Color.green) / 2 : (saved + Color.black) / 2;
                    GUI.Label(eyeRects[i], Icon_Add, FlowBtnRight);
                }
                else if (!_isDragging)
                {
                    // Normal interactive mode
                    if (GUI.Button(nameRects[i], new GUIContent(nameProp.stringValue, tipProp.stringValue), FlowBtnLeft)
                        && Event.current.button == 0) // MWC: GUI.Button fires on any mouse button; ignore right click (it opens the remove menu)
                    {
                        if (M[i].gameObjects.Length > 0)
                            Selection.objects = M[i].gameObjects;
                        else if (M[i].monoBehaviours.Length > 0)
                            Selection.objects = new Object[1] { M[i].monoBehaviours[0].gameObject };
                    }
                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        GUI.color = activeProp.boolValue ? (saved + Color.green) / 2 : (saved + Color.black) / 2;
                        bool newVal = GUI.Toggle(eyeRects[i], activeProp.boolValue, Icon_Add, FlowBtnRight);
                        GUI.color = saved;
                        if (cc.changed && Event.current.button == 0) // MWC: ignore right click
                        {
                            activeProp.boolValue = newVal;
                            foreach (var item in M[i].gameObjects)
                                if (item) { item.SetActive(newVal); EditorUtility.SetDirty(item); }
                            foreach (var item in M[i].monoBehaviours)
                                if (item) { item.enabled = newVal; EditorUtility.SetDirty(item); }
                            Undo.RecordObject(target, "Component Selector");
                        }
                    }
                }
                else
                {
                    // Drag mode: render visuals only, no click interaction
                    GUI.Label(nameRects[i], new GUIContent(nameProp.stringValue, tipProp.stringValue), FlowBtnLeft);
                    GUI.color = beingDragged
                        ? new Color(0, 0, 0, 0.35f)
                        : (activeProp.boolValue ? (saved + Color.green) / 2 : (saved + Color.black) / 2);
                    GUI.Label(eyeRects[i], Icon_Add, FlowBtnRight);
                }

                GUI.color = saved;
            }

            // ── Drop indicator: blue vertical bar in the gap before _dropIndex ──────
            if (_isDragging && _dropIndex >= 0 && _dropIndex != _dragIndex && _dropIndex != _dragIndex + 1)
                EditorGUI.DrawRect(DropIndicatorRect(_dropIndex, nameRects, eyeRects, count),
                    new Color(0.25f, 0.65f, 1f, 0.9f));
        }

        // Rect spanning the full button (name + eye)
        private static Rect FullRect(Rect nr, Rect er) =>
            new(nr.x, nr.y, nr.width + er.width, nr.height);

        // MWC: returns insert-before index (0..count) nearest to the mouse position
        private static int NearestDropIndex(Vector2 mouse, Rect[] nameRects, Rect[] eyeRects, int count)
        {
            int best = count; float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Rect  f    = FullRect(nameRects[i], eyeRects[i]);
                float dist = Vector2.Distance(mouse, new Vector2(f.x + f.width * .5f, f.y + f.height * .5f));
                if (dist < bestDist) { bestDist = dist; best = mouse.x <= f.x + f.width * .5f ? i : i + 1; }
            }
            return best;
        }

        // MWC: 3-px bar placed in the FlowPadding gap before dropIndex
        private static Rect DropIndicatorRect(int dropIndex, Rect[] nameRects, Rect[] eyeRects, int count)
        {
            const float w = 3f;
            if (dropIndex >= count)
            {
                Rect last = FullRect(nameRects[count - 1], eyeRects[count - 1]);
                return new Rect(last.xMax + FlowPadding * .5f - w * .5f, last.y, w, last.height);
            }
            Rect r = FullRect(nameRects[dropIndex], eyeRects[dropIndex]);
            return new Rect(r.x - FlowPadding * .5f - w * .5f, r.y, w, r.height);
        }
    }
#endif
}