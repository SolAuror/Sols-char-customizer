// Last Change: 10/14/2026 2:59pm
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Events;

// ── UnityEvent Function Search ────────────────────────────────────────────────
// Replaces the function dropdown of every UnityEvent in the inspector with a
// searchable popup that can also be browsed as a hierarchy, each component's
// functions grouped by parameter type (Dynamic, No Parameters, bool, int,
// float, string, then object types):
//
//   GameObject ▸
//       Dynamic bool
//         SetActive
//       No Parameters
//         BroadcastMessage ()
//       bool
//         bool isStatic
//         SetActive (bool)
//       string
//         SendMessage (string)
//   Transform ▸
//   ...
//
// Usage:
//   Click the function dropdown     — popup, opened inside the assigned function's component
//   Alt + click                     — original Unity menu
//   Type                            — search the current level (root = every component)
//   Up / Down / PageUp / PageDown   — move the selection
//   Enter / Right / click on ▸ row  — open a component
//   Left / Backspace (empty search) — back to the component list
//   Tab / Shift + Tab               — next / previous component
//   Escape                          — clear the search, then close
//
// Search tokens are separated by spaces, '/', '>' or '.', and every token must
// appear in "component function", so "gameobject/isstatic", "GameObject > isStatic"
// and "go static" style queries all work. Matches at the start of a word in the
// function name are listed first. At the root, components whose name matches the
// whole query are listed above the functions so Enter opens them.
//
// How it works:
//   The list of functions is taken from Unity's own UnityEventDrawer.BuildPopupList
//   (internal, reached through reflection), so the entries and what they assign
//   are identical to the stock menu. If any of the reflected members go missing
//   in a future Unity version the drawer silently falls back to the stock menu.
//
// The drawer is registered on UnityEvent and UnityEvent<> rather than on
// UnityEventBase so it is always more specific than Unity's built-in drawer.

[CustomPropertyDrawer(typeof(UnityEvent), true)]
[CustomPropertyDrawer(typeof(UnityEvent<>), true)]
[CustomPropertyDrawer(typeof(UnityEvent<,>), true)]
[CustomPropertyDrawer(typeof(UnityEvent<,,>), true)]
[CustomPropertyDrawer(typeof(UnityEvent<,,,>), true)]
public class UnityEventFunctionSearchDrawer : UnityEventDrawer
{
    // ── Reflection (resolved once) ────────────────────────────────────────────

    /// <summary>Instance members of any visibility.</summary>
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    /// <summary>Static members of any visibility.</summary>
    private const BindingFlags AnyStatic   = BindingFlags.Static   | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>UnityEventDrawer.m_ListenersArray — the m_Calls array of the event being drawn.</summary>
    private static readonly FieldInfo  ListenersArrayField = typeof(UnityEventDrawer).GetField("m_ListenersArray", AnyInstance);
    /// <summary>UnityEventDrawer.m_DummyEvent — event instance BuildPopupList reads the argument types from.</summary>
    private static readonly FieldInfo  DummyEventField     = typeof(UnityEventDrawer).GetField("m_DummyEvent", AnyInstance);
    /// <summary>GenericMenu.m_MenuItems — the menu items list.</summary>
    private static readonly FieldInfo  MenuItemsField      = typeof(GenericMenu).GetField("m_MenuItems", AnyInstance);
    /// <summary>UnityEventDrawer.BuildPopupList — builds the stock function menu.</summary>
    private static readonly MethodInfo BuildPopupList      = typeof(UnityEventDrawer).GetMethod("BuildPopupList", AnyStatic, null,
        new[] { typeof(UnityEngine.Object), typeof(UnityEventBase), typeof(SerializedProperty) }, null);

    /// <summary>True when every reflected member was found.</summary>
    private static readonly bool ReflectionReady =
        ListenersArrayField != null && DummyEventField != null && MenuItemsField != null && BuildPopupList != null;

    // ── Drawing ───────────────────────────────────────────────────────────────

    /// <summary>Returns null so the inspector uses the IMGUI path, where the dropdown click can be intercepted.</summary>
    public override UnityEngine.UIElements.VisualElement CreatePropertyGUI(SerializedProperty property)
    {
        return ReflectionReady ? null : base.CreatePropertyGUI(property);
    }

    /// <summary>Opens the search popup instead of the stock menu when the function dropdown is clicked.</summary>
    protected override void DrawEvent(Rect rect, int index, bool isActive, bool isFocused)
    {
        Event e = Event.current;
        if (ReflectionReady && GUI.enabled && e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            Rect functionRect = GetFunctionRect(rect);
            if (functionRect.Contains(e.mousePosition) && TryShowSearch(functionRect, index))
            {
                e.Use();
                GUIUtility.ExitGUI();
            }
        }

        base.DrawEvent(rect, index, isActive, isFocused);
    }

    /// <summary>Mirrors UnityEventDrawer.GetRowRects: top row, right of the 30% wide call state column.</summary>
    private static Rect GetFunctionRect(Rect rect)
    {
        float xMin = rect.x + rect.width * 0.3f + 5f;
        return new Rect(xMin, rect.y + 3f, rect.xMax - xMin, EditorGUIUtility.singleLineHeight);
    }

    /// <summary>Builds the stock menu for the listener at index and shows it as a search popup.</summary>
    private bool TryShowSearch(Rect functionRect, int index)
    {
        try
        {
            var listeners  = ListenersArrayField.GetValue(this) as SerializedProperty;
            var dummyEvent = DummyEventField.GetValue(this) as UnityEventBase;
            if (listeners == null || dummyEvent == null || index < 0 || index >= listeners.arraySize)
                return false;

            SerializedProperty listener = listeners.GetArrayElementAtIndex(index);
            UnityEngine.Object target   = listener.FindPropertyRelative("m_Target").objectReferenceValue;
            if (target == null)
                return false; // stock dropdown is disabled without a target

            var menu = BuildPopupList.Invoke(null, new object[] { target, dummyEvent, listener }) as GenericMenu;
            List<UnityEventFunctionSearchPopup.Entry> entries = ReadEntries(menu);
            if (entries == null || entries.Count == 0)
                return false;

            PopupWindow.Show(functionRect, new UnityEventFunctionSearchPopup(entries, functionRect.width, target.name));
            return true;
        }
        catch (ExitGUIException)
        {
            // PopupWindow.Show may exit GUI itself once the popup is open.
            Event.current?.Use();
            throw;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[UnityEventFunctionSearch] Falling back to the default menu: " + ex.Message);
            return false;
        }
    }

    // ── GenericMenu → flat entry list ─────────────────────────────────────────

    /// <summary>Cached fields of GenericMenu's internal MenuItem class.</summary>
    private static FieldInfo _itemContent, _itemSeparator, _itemOn, _itemFunc, _itemFunc2, _itemUserData;

    /// <summary>Flattens the GenericMenu items into entries, keeping the section header each one sits under.</summary>
    private static List<UnityEventFunctionSearchPopup.Entry> ReadEntries(GenericMenu menu)
    {
        if (menu == null || !(MenuItemsField.GetValue(menu) is IList items))
            return null;

        var entries = new List<UnityEventFunctionSearchPopup.Entry>(items.Count);
        string sectionGroup = null; // component the current "Dynamic x" / "Static Parameters" header belongs to
        string section      = null;

        foreach (object item in items)
        {
            if (item == null)
                continue;

            if (_itemContent == null)
            {
                Type t = item.GetType();
                _itemContent   = t.GetField("content", AnyInstance);
                _itemSeparator = t.GetField("separator", AnyInstance);
                _itemOn        = t.GetField("on", AnyInstance);
                _itemFunc      = t.GetField("func", AnyInstance);
                _itemFunc2     = t.GetField("func2", AnyInstance);
                _itemUserData  = t.GetField("userData", AnyInstance);
                if (_itemContent == null || _itemSeparator == null || _itemOn == null ||
                    _itemFunc == null || _itemFunc2 == null || _itemUserData == null)
                {
                    _itemContent = null;
                    return null;
                }
            }

            if ((bool)_itemSeparator.GetValue(item))
                continue;

            string path = (_itemContent.GetValue(item) as GUIContent)?.text;
            if (string.IsNullOrEmpty(path))
                continue;

            // "Component/Function" — component names never contain '/', so only the first slash separates the two.
            int slash    = path.IndexOf('/');
            string group = slash < 0 ? "" : path.Substring(0, slash);
            string label = slash < 0 ? path : path.Substring(slash + 1);

            var func     = _itemFunc.GetValue(item) as GenericMenu.MenuFunction;
            var func2    = _itemFunc2.GetValue(item) as GenericMenu.MenuFunction2;
            object data  = _itemUserData.GetValue(item);

            if (func == null && func2 == null)
            {
                // Disabled items are the section headers inside a component submenu.
                sectionGroup = group;
                section      = label;
                continue;
            }

            Action invoke;
            if (func2 != null) invoke = () => func2(data);
            else               invoke = () => func();

            entries.Add(new UnityEventFunctionSearchPopup.Entry(
                label,
                group,
                group == sectionGroup ? section : null,
                (bool)_itemOn.GetValue(item),
                invoke));
        }

        return entries;
    }
}

// ── Search popup ──────────────────────────────────────────────────────────────

public class UnityEventFunctionSearchPopup : PopupWindowContent
{
    /// <summary>One assignable function of the stock menu.</summary>
    public readonly struct Entry
    {
        /// <summary>Function label, e.g. "SetActive (bool)".</summary>
        public readonly string Label;
        /// <summary>Component / object name, empty for "No Function".</summary>
        public readonly string Group;
        /// <summary>Parameter type the function is grouped under: "Dynamic float", "No Parameters", "bool", "Transform"...</summary>
        public readonly string Section;
        /// <summary>Sort order of the section inside its component.</summary>
        public readonly int    SectionOrder;
        /// <summary>Lower case label, for word start ranking.</summary>
        public readonly string LabelLower;
        /// <summary>Lower case "component function" the query is matched against.</summary>
        public readonly string Haystack;
        /// <summary>True for the currently assigned function.</summary>
        public readonly bool   On;
        /// <summary>Assigns the function, exactly like the stock menu item.</summary>
        public readonly Action Invoke;

        /// <summary>Creates an entry; section is the stock menu header the function sits under.</summary>
        public Entry(string label, string group, string section, bool on, Action invoke)
        {
            Label        = label;
            Group        = group;
            Section      = string.IsNullOrEmpty(group) ? null : TypeOf(label, section);
            SectionOrder = OrderOf(Section);
            LabelLower   = label.ToLowerInvariant();
            Haystack     = (group + " " + label + " " + Section).ToLowerInvariant();
            On           = on;
            Invoke       = invoke;
        }

        /// <summary>Parameter type from the stock label: "Dynamic x" header, "Name (args)" method or "type name" property.</summary>
        private static string TypeOf(string label, string section)
        {
            if (section != null && section.StartsWith("Dynamic", StringComparison.Ordinal))
                return section;

            if (label.EndsWith(")", StringComparison.Ordinal))
            {
                int open = label.LastIndexOf('(');
                if (open >= 0)
                {
                    string args = label.Substring(open + 1, label.Length - open - 2).Trim();
                    return args.Length == 0 ? "No Parameters" : args;
                }
            }

            int space = label.IndexOf(' ');
            return space > 0 ? label.Substring(0, space) : section;
        }

        /// <summary>Dynamic first, then no parameters, the primitive types, and object types last.</summary>
        private static int OrderOf(string section)
        {
            if (section == null) return 0;
            if (section.StartsWith("Dynamic", StringComparison.Ordinal)) return 0;
            switch (section)
            {
                case "No Parameters": return 1;
                case "bool":          return 2;
                case "int":           return 3;
                case "float":         return 4;
                case "string":        return 5;
                default:              return 6;
            }
        }
    }

    /// <summary>What a list row shows.</summary>
    private enum RowKind { Entry, Group, Section }

    /// <summary>One visible line of the list.</summary>
    private readonly struct Row
    {
        /// <summary>Entry, component (opens) or section header (not selectable).</summary>
        public readonly RowKind Kind;
        /// <summary>Left text.</summary>
        public readonly string  Text;
        /// <summary>Right, grey text.</summary>
        public readonly string  Hint;
        /// <summary>Index into the entry list, -1 for groups and sections.</summary>
        public readonly int     Entry;

        /// <summary>Creates a row.</summary>
        public Row(RowKind kind, string text, string hint, int entry)
        {
            Kind  = kind;
            Text  = text;
            Hint  = hint;
            Entry = entry;
        }

        /// <summary>Section headers cannot be selected.</summary>
        public bool Selectable => Kind != RowKind.Section;
    }

    // ── Layout constants ──────────────────────────────────────────────────────

    /// <summary>Minimum popup width.</summary>
    private const float MinWidth      = 420f;
    /// <summary>Popup height.</summary>
    private const float Height        = 360f;
    /// <summary>Height of a list row.</summary>
    private const float RowHeight     = 20f;
    /// <summary>Height of the search field.</summary>
    private const float FieldHeight   = 18f;
    /// <summary>Height of the breadcrumb bar.</summary>
    private const float BarHeight     = 22f;
    /// <summary>Outer padding.</summary>
    private const float Padding       = 4f;
    /// <summary>Search field + breadcrumb bar.</summary>
    private const float HeaderHeight  = Padding * 2f + FieldHeight + BarHeight;
    /// <summary>Column for the ✓ of the assigned function.</summary>
    private const float CheckWidth    = 18f;
    /// <summary>Column for the ▸ of component rows.</summary>
    private const float ArrowWidth    = 16f;
    /// <summary>Control name of the search field.</summary>
    private const string SearchControl = "UnityEventFunctionSearchField";

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>Every function of the menu, in stock order.</summary>
    private readonly List<Entry> _all;
    /// <summary>Components in stock order.</summary>
    private readonly List<string> _groups = new List<string>();
    /// <summary>Number of functions per component.</summary>
    private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
    /// <summary>Rows currently listed.</summary>
    private readonly List<Row> _rows = new List<Row>();
    /// <summary>Popup width.</summary>
    private readonly float _width;
    /// <summary>Title of the root level.</summary>
    private readonly string _rootTitle;

    /// <summary>Opened component, null at the root.</summary>
    private string  _group;
    /// <summary>Search text.</summary>
    private string  _query = "";
    /// <summary>Selected row, -1 when nothing is selectable.</summary>
    private int     _selected = -1;
    /// <summary>List scroll position.</summary>
    private Vector2 _scroll;
    /// <summary>Focus the search field on the next GUI pass.</summary>
    private bool    _focusPending  = true;
    /// <summary>Scroll the selection into view on the next layout.</summary>
    private bool    _scrollPending = true;

    // ── Styles (lazily initialised) ───────────────────────────────────────────

    /// <summary>Row label.</summary>
    private GUIStyle _labelStyle;
    /// <summary>Right aligned grey hint.</summary>
    private GUIStyle _hintStyle;
    /// <summary>Section header.</summary>
    private GUIStyle _sectionStyle;
    /// <summary>Breadcrumb title.</summary>
    private GUIStyle _barStyle;

    /// <summary>Creates the popup and opens it on the assigned function.</summary>
    public UnityEventFunctionSearchPopup(List<Entry> entries, float activatorWidth, string rootTitle = null)
    {
        _all       = entries;
        _width     = Mathf.Max(MinWidth, activatorWidth);
        _rootTitle = string.IsNullOrEmpty(rootTitle) ? "Functions" : rootTitle;

        foreach (Entry entry in _all)
        {
            if (string.IsNullOrEmpty(entry.Group))
                continue;

            if (!_counts.ContainsKey(entry.Group))
            {
                _counts[entry.Group] = 0;
                _groups.Add(entry.Group);
            }
            _counts[entry.Group]++;
        }

        // Start inside the component of the assigned function, with it selected.
        int on = _all.FindIndex(x => x.On);
        if (on >= 0 && !string.IsNullOrEmpty(_all[on].Group))
            _group = _all[on].Group;

        Rebuild();
        if (on >= 0) SelectEntry(on);
    }

    /// <summary>Fixed popup size.</summary>
    public override Vector2 GetWindowSize()
    {
        return new Vector2(_width, Height);
    }

    /// <summary>Enables hover tracking.</summary>
    public override void OnOpen()
    {
        editorWindow.wantsMouseMove = true;
    }

    /// <summary>Draws the search field, the breadcrumb bar and the list.</summary>
    public override void OnGUI(Rect rect)
    {
        InitStyles();
        HandleKeyboard();

        DrawSearchField(new Rect(rect.x + Padding, rect.y + Padding, rect.width - Padding * 2f, FieldHeight));
        DrawBar(new Rect(rect.x, rect.y + Padding * 2f + FieldHeight, rect.width, BarHeight));
        DrawList(new Rect(rect.x, rect.y + HeaderHeight, rect.width, rect.height - HeaderHeight));

        if (Event.current.type == EventType.MouseMove)
            editorWindow.Repaint();
    }

    // ── Search field ──────────────────────────────────────────────────────────

    /// <summary>Draws the search field and rebuilds the list when it changes.</summary>
    private void DrawSearchField(Rect rect)
    {
        GUI.SetNextControlName(SearchControl);
        EditorGUI.BeginChangeCheck();
        string query = EditorGUI.TextField(rect, _query, EditorStyles.toolbarSearchField);
        if (EditorGUI.EndChangeCheck())
            SetQuery(query);

        if (_focusPending)
        {
            EditorGUI.FocusTextInControl(SearchControl);
            if (Event.current.type == EventType.Repaint)
                _focusPending = false;
        }
    }

    /// <summary>Changes the search text and selects the best match.</summary>
    private void SetQuery(string query)
    {
        _query = query;
        Rebuild();
        SelectFirst();
        _scroll = Vector2.zero;
    }

    // ── Breadcrumb bar ────────────────────────────────────────────────────────

    /// <summary>Draws "◂ Component" inside a component (click to go back) or the root title.</summary>
    private void DrawBar(Rect rect)
    {
        Event e = Event.current;
        bool inGroup = _group != null;

        if (inGroup && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            e.Use();
            Back();
            GUIUtility.ExitGUI();
        }

        if (e.type != EventType.Repaint)
            return;

        if (inGroup && rect.Contains(e.mousePosition))
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.15f));

        string title = inGroup ? "◂  " + _group : _rootTitle;
        GUI.Label(new Rect(rect.x + Padding, rect.y, rect.width - Padding * 2f, rect.height), title, _barStyle);

        if (inGroup)
            GUI.Label(new Rect(rect.x, rect.y, rect.width - Padding, rect.height), _counts[_group] + " functions", _hintStyle);

        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.25f));
    }

    // ── Hierarchy navigation ──────────────────────────────────────────────────

    /// <summary>Opens a component and clears the search.</summary>
    private void Open(string group)
    {
        _group  = group;
        _query  = "";
        _scroll = Vector2.zero;
        _focusPending = true;
        Rebuild();

        int on = _all.FindIndex(x => x.On && x.Group == group);
        if (on >= 0) SelectEntry(on);
        else         SelectFirst();
    }

    /// <summary>Returns to the component list, selecting the component that was open.</summary>
    private void Back()
    {
        if (_group == null)
            return;

        string from = _group;
        _group  = null;
        _query  = "";
        _focusPending = true;
        Rebuild();

        int row = _rows.FindIndex(x => x.Kind == RowKind.Group && x.Text == from);
        if (row >= 0) Select(row);
        else          SelectFirst();
    }

    /// <summary>Opens the next (or previous) component, wrapping around.</summary>
    private void CycleGroup(int step)
    {
        if (_groups.Count == 0)
            return;

        int current = _group == null ? (step > 0 ? -1 : 0) : _groups.IndexOf(_group);
        Open(_groups[(current + step + _groups.Count) % _groups.Count]);
    }

    // ── Keyboard ──────────────────────────────────────────────────────────────

    /// <summary>Runs before the text field so navigation keys never reach it.</summary>
    private void HandleKeyboard()
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown)
            return;

        // Tab arrives as a key code event followed by a character event; swallow both.
        if (e.character == '\t')
        {
            e.Use();
            return;
        }

        bool emptyQuery = _query.Length == 0;

        switch (e.keyCode)
        {
            case KeyCode.Tab:
                e.Use();
                CycleGroup(e.shift ? -1 : 1);
                break;

            case KeyCode.DownArrow: MoveSelection(1);   e.Use(); break;
            case KeyCode.UpArrow:   MoveSelection(-1);  e.Use(); break;
            case KeyCode.PageDown:  MoveSelection(10);  e.Use(); break;
            case KeyCode.PageUp:    MoveSelection(-10); e.Use(); break;

            case KeyCode.RightArrow:
                if (SelectedRow(out Row right) && right.Kind == RowKind.Group)
                {
                    e.Use();
                    Open(right.Text);
                }
                break;

            case KeyCode.LeftArrow:
            case KeyCode.Backspace:
                if (emptyQuery && _group != null)
                {
                    e.Use();
                    Back();
                }
                break;

            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                e.Use();
                if (SelectedRow(out Row row))
                    Activate(row);
                break;

            case KeyCode.Escape:
                e.Use();
                if (!emptyQuery) SetQuery("");
                else             editorWindow.Close();
                break;
        }
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    /// <summary>Gets the selected row, false when nothing is selected.</summary>
    private bool SelectedRow(out Row row)
    {
        bool valid = _selected >= 0 && _selected < _rows.Count;
        row = valid ? _rows[_selected] : default;
        return valid;
    }

    /// <summary>Selects a row and scrolls it into view.</summary>
    private void Select(int row)
    {
        _selected      = row;
        _scrollPending = true;
    }

    /// <summary>Selects the first selectable row.</summary>
    private void SelectFirst()
    {
        Select(_rows.FindIndex(x => x.Selectable));
    }

    /// <summary>Selects the row of an entry, or the first row when it is not listed.</summary>
    private void SelectEntry(int entry)
    {
        int row = _rows.FindIndex(x => x.Kind == RowKind.Entry && x.Entry == entry);
        if (row >= 0) Select(row);
        else          SelectFirst();
    }

    /// <summary>Moves the selection by delta selectable rows, skipping section headers.</summary>
    private void MoveSelection(int delta)
    {
        if (_selected < 0)
        {
            SelectFirst();
            return;
        }

        int step  = delta > 0 ? 1 : -1;
        int index = _selected;
        for (int moved = 0; moved < Mathf.Abs(delta); moved++)
        {
            int next = index + step;
            while (next >= 0 && next < _rows.Count && !_rows[next].Selectable)
                next += step;

            if (next < 0 || next >= _rows.Count)
                break;
            index = next;
        }

        if (_rows[index].Selectable)
            Select(index);
    }

    /// <summary>Opens a component row or assigns an entry row.</summary>
    private void Activate(Row row)
    {
        if (row.Kind == RowKind.Group)
        {
            Open(row.Text);
            return;
        }

        if (row.Kind == RowKind.Entry)
        {
            editorWindow.Close();
            _all[row.Entry].Invoke?.Invoke();
        }
    }

    // ── List ──────────────────────────────────────────────────────────────────

    /// <summary>Draws the visible rows of the list.</summary>
    private void DrawList(Rect rect)
    {
        if (_rows.Count == 0)
        {
            GUI.Label(rect, "No matching functions", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        float contentHeight = _rows.Count * RowHeight;
        bool  hasScrollbar  = contentHeight > rect.height;
        var   viewRect      = new Rect(0f, 0f, rect.width - (hasScrollbar ? 14f : 0f), contentHeight);

        if (_scrollPending && _selected >= 0 && Event.current.type == EventType.Layout)
        {
            // Keep the section header above the first entry visible too.
            float top = (_selected > 0 && !_rows[_selected - 1].Selectable ? _selected - 1 : _selected) * RowHeight;
            float bottom = (_selected + 1) * RowHeight;
            if (top < _scroll.y)                         _scroll.y = top;
            else if (bottom > _scroll.y + rect.height)   _scroll.y = bottom - rect.height;
            _scrollPending = false;
        }

        _scroll = GUI.BeginScrollView(rect, _scroll, viewRect);

        // Only the rows inside the visible window are drawn.
        int first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / RowHeight));
        int last  = Mathf.Min(_rows.Count - 1, Mathf.CeilToInt((_scroll.y + rect.height) / RowHeight));

        Event e = Event.current;
        for (int i = first; i <= last; i++)
        {
            Row row     = _rows[i];
            var rowRect = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);

            if (row.Selectable && rowRect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseMove)
                    _selected = i;

                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    e.Use();
                    Activate(row);
                    GUIUtility.ExitGUI();
                }
            }

            if (e.type == EventType.Repaint)
                DrawRow(rowRect, row, i == _selected);
        }

        GUI.EndScrollView();
    }

    /// <summary>Paints one row.</summary>
    private void DrawRow(Rect rect, Row row, bool selected)
    {
        if (row.Kind == RowKind.Section)
        {
            GUI.Label(new Rect(rect.x + Padding, rect.y, rect.width - Padding * 2f, rect.height), row.Text, _sectionStyle);
            return;
        }

        if (selected)
            EditorGUI.DrawRect(rect, GUI.skin.settings.selectionColor);

        if (row.Kind == RowKind.Entry && _all[row.Entry].On)
            GUI.Label(new Rect(rect.x + 2f, rect.y, CheckWidth, rect.height), "✓", _labelStyle);

        float arrow = row.Kind == RowKind.Group ? ArrowWidth : 0f;
        if (arrow > 0f)
            GUI.Label(new Rect(rect.xMax - arrow, rect.y, arrow, rect.height), "▸", _labelStyle);

        float hintWidth = string.IsNullOrEmpty(row.Hint) ? 0f
            : Mathf.Min(_hintStyle.CalcSize(new GUIContent(row.Hint)).x, rect.width * 0.5f);
        var labelRect   = new Rect(rect.x + CheckWidth, rect.y, rect.width - CheckWidth - hintWidth - arrow - 8f, rect.height);
        var hintRect    = new Rect(rect.xMax - arrow - hintWidth - 4f, rect.y, hintWidth, rect.height);

        GUI.Label(labelRect, row.Text, _labelStyle);
        if (hintWidth > 0f)
            GUI.Label(hintRect, row.Hint, _hintStyle);
    }

    // ── Rows ──────────────────────────────────────────────────────────────────

    /// <summary>Token separators: spaces plus the path separators people type ("GameObject/isStatic", "GameObject > isStatic").</summary>
    private static readonly char[] TokenSeparators = { ' ', '\t', '/', '>', '.', '▸' };

    /// <summary>Rebuilds the rows for the current level and search.</summary>
    private void Rebuild()
    {
        _rows.Clear();
        _selected = -1;

        string[] tokens = _query.ToLowerInvariant().Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) BuildBrowse();
        else                    BuildSearch(tokens);
    }

    /// <summary>No search: the component list at the root, or a component's functions under their section headers.</summary>
    private void BuildBrowse()
    {
        if (_group == null)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                if (string.IsNullOrEmpty(_all[i].Group)) // "No Function"
                    _rows.Add(new Row(RowKind.Entry, _all[i].Label, null, i));
            }

            foreach (string group in _groups)
                _rows.Add(new Row(RowKind.Group, group, _counts[group].ToString(), -1));
            return;
        }

        // Grouped by parameter type, stock order inside each type.
        var indices = new List<int>();
        for (int i = 0; i < _all.Count; i++)
        {
            if (_all[i].Group == _group)
                indices.Add(i);
        }

        indices.Sort((a, b) =>
        {
            Entry x = _all[a], y = _all[b];
            int order = x.SectionOrder.CompareTo(y.SectionOrder);
            if (order == 0) order = string.CompareOrdinal(x.Section, y.Section);
            return order != 0 ? order : a.CompareTo(b);
        });

        string section = null;
        foreach (int i in indices)
        {
            Entry entry = _all[i];
            if (!string.IsNullOrEmpty(entry.Section) && entry.Section != section)
            {
                section = entry.Section;
                _rows.Add(new Row(RowKind.Section, section, null, -1));
            }

            _rows.Add(new Row(RowKind.Entry, entry.Label, null, i));
        }
    }

    /// <summary>Search: a flat, ranked list of the matches in the current level.</summary>
    private void BuildSearch(string[] tokens)
    {
        // At the root, components whose own name matches come first so Enter opens them.
        if (_group == null)
        {
            foreach (string group in _groups)
            {
                if (ContainsAll(group.ToLowerInvariant(), tokens))
                    _rows.Add(new Row(RowKind.Group, group, _counts[group].ToString(), -1));
            }
        }

        // Three ranks, stock order inside each: a word of the function starts with a token,
        // the function alone contains every token, the match needs the component name.
        var ranks = new[] { new List<Row>(), new List<Row>(), new List<Row>() };
        for (int i = 0; i < _all.Count; i++)
        {
            Entry entry = _all[i];
            if (_group != null && entry.Group != _group)
                continue;
            if (!ContainsAll(entry.Haystack, tokens))
                continue;

            int rank = 2;
            if (ContainsAll(entry.LabelLower, tokens)) rank = 1;
            foreach (string token in tokens)
            {
                if (StartsWord(entry.LabelLower, token)) { rank = 0; break; }
            }

            ranks[rank].Add(new Row(RowKind.Entry, entry.Label, Hint(entry), i));
        }

        foreach (List<Row> rank in ranks)
            _rows.AddRange(rank);
    }

    /// <summary>Right hand text of a search result: "Component · Section", or just the section inside a component.</summary>
    private string Hint(Entry entry)
    {
        if (_group != null)
            return entry.Section;
        return string.IsNullOrEmpty(entry.Section) ? entry.Group : entry.Group + "  ·  " + entry.Section;
    }

    /// <summary>True when every token appears in text.</summary>
    private static bool ContainsAll(string text, string[] tokens)
    {
        foreach (string token in tokens)
        {
            if (text.IndexOf(token, StringComparison.Ordinal) < 0)
                return false;
        }
        return true;
    }

    /// <summary>True when token appears at the start of a word of text ("bool isStatic", "Stat_Modify", "SetActive (bool)").</summary>
    private static bool StartsWord(string text, string token)
    {
        for (int i = text.IndexOf(token, StringComparison.Ordinal); i >= 0; i = text.IndexOf(token, i + 1, StringComparison.Ordinal))
        {
            if (i == 0 || !char.IsLetterOrDigit(text[i - 1]))
                return true;
        }
        return false;
    }

    // ── Styles ────────────────────────────────────────────────────────────────

    /// <summary>Creates the styles on the first GUI pass.</summary>
    private void InitStyles()
    {
        if (_labelStyle != null)
            return;

        var grey = new Color(0.6f, 0.6f, 0.6f);
        _labelStyle   = new GUIStyle(EditorStyles.label)     { alignment = TextAnchor.MiddleLeft,  clipping = TextClipping.Clip };
        _hintStyle    = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip };
        _sectionStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.LowerLeft, clipping = TextClipping.Clip };
        _barStyle     = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft,  clipping = TextClipping.Clip };
        _hintStyle.normal.textColor    = grey;
        _sectionStyle.normal.textColor = grey;
    }
}
#endif
