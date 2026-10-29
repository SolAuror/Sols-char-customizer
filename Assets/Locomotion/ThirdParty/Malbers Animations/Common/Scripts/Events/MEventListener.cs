// Last Change: 9/24/2026 1:14pm
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using MalbersAnimations.Scriptables;

#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
#endif

namespace MalbersAnimations.Events
{
    ///<summary>
    /// Listener to use with the GameEvents
    /// Based on the Talk - Game Architecture with Scriptable Objects by Ryan Hipple
    /// </summary>
    // MWC — how a listener filters responses by the event's sender (see MEvent.LastSender)
    public enum OwnerFilterMode
    {
        /// <summary> No filtering. Respond to every invocation (default, backward-compatible). </summary>
        Ignore,
        /// <summary> Only respond when the sender belongs to this listener's Owner. </summary>
        MustMatch,
        /// <summary> Only respond when the sender does NOT belong to this listener's Owner. </summary>
        MustNotMatch
    }

    [AddComponentMenu("Malbers/Events/Event Listener")]
    public class MEventListener : MonoBehaviour
    {
        public List<MEventItemListener> Events = new();
        // public bool debug;
#pragma warning disable 414
        [HideInInspector, SerializeField] private bool ShowEvents = true;
        [HideInInspector, SerializeField] private int SelectedEvent;
#pragma warning restore 414

        // MWC — Owner filtering (applies to all entries on this component; entries may opt out).
        [Tooltip("Filter responses by the event's Sender. Ignore = respond to everything (default).")]
        public OwnerFilterMode FilterMode = OwnerFilterMode.Ignore;
        [Tooltip("The owner this listener belongs to (e.g. the player). Accepts a scene Transform or a TransformVar asset.")]
        public TransformReference OwnerFilter = new();
        [Tooltip("Match by hierarchy: the Sender is the Owner or any child of it. Off = exact Transform match.")]
        public bool MatchHierarchy = true;

        [System.NonSerialized] private Transform _ownerOverride; //  runtime override set via SetOwner()

        /// <summary> The owner used for filtering: runtime override wins, otherwise the OwnerFilter reference (resolved lazily). </summary>
        public Transform ResolvedOwner => _ownerOverride != null ? _ownerOverride : OwnerFilter.Value;

        /// <summary> Bind this listener to an owner at runtime (e.g. from a local-multiplayer spawner). </summary>
        public void SetOwner(Transform owner) => _ownerOverride = owner;

        /// <summary> Registers every entry on its event. </summary>
        public virtual void OnEnable()
        {
            foreach (var item in Events)
            {
                if (item.Event) item.Event.RegisterListener(item);

                item.Owner = transform;
                item.Listener = this; // MWC — back-reference so entries can read the owner filter

                item.DebugRegister(true);
            }
        }

        /// <summary> Unregisters every entry from its event. </summary>
        public virtual void OnDisable()
        {
            foreach (var item in Events)
            {
                if (item.Event) item.Event.UnregisterListener(item);

                item.DebugRegister(false);
            }
        }

        public void Pause()
        {
            Debug.Log("Pause Editor", this);
            Debug.Break();
        }

        public void Behaviour_EnableNextFrame(Behaviour behaviour)
        {
            behaviour.enabled = false;
            this.Delay_Action(() => behaviour.enabled = true);
        }
    }


    [System.Serializable]
    public class MEventItemListener
    {
        public MEvent Event;

        public bool active = true;

        [Tooltip("Logs on the Console when this entry is listening, when it responds, and why it was skipped (Owner Filter, disabled entry, disabled response type)")]
        public bool debug = false;

        public Transform Owner { get; set; }

        // MWC — set by MEventListener.OnEnable; lets this entry read the component's owner filter.
        public MEventListener Listener { get; set; }
        // MWC — set by MEvent dispatch right before each call (reentrancy-safe). Who raised the event.
        public Object CurrentSender { get; set; }
        [Tooltip("Ignore this component's Owner filter for this entry (always respond).")]
        public bool ignoreOwnerFilter = false; // MWC — per-entry opt-out


        [HideInInspector]
        public bool useInt = false, useFloat = false, useVoid = true, useString = false, useBool = false,
            useGO = false, useTransform = false, useVector3, useVector2 = false, useComponent = false, useSprite = false,
            useSender = false; // MWC — expose the sender to user logic via ResponseSender

        public UnityEvent Response = new();

        public UnityEvent ResponseNull = new();

        public TransformEvent ResponseSender = new(); // MWC — fires with the sender (as Transform) when this entry responds

        public FloatEvent ResponseFloat = new();
        public IntEvent ResponseInt = new();

        public BoolEvent ResponseBool = new();
        public UnityEvent ResponseBoolFalse = new();
        public UnityEvent ResponseBoolTrue = new();

        public StringEvent ResponseString = new();
        public GameObjectEvent ResponseGO = new();
        public TransformEvent ResponseGOasT = new();
        public TransformEvent ResponseTransform = new();
        public ComponentEvent ResponseComponent = new();
        public SpriteEvent ResponseSprite = new();
        public Vector3Event ResponseVector3 = new();
        public Vector2Event ResponseVector2 = new();

        public List<AdvancedIntegerEvent> IntEventList = new();
        public List<AdvancedFloatEvent> FloatEventList = new();


        public bool AdvancedInteger = false;
        public bool AdvancedFloat = false;
        public bool AdvancedBool = false;
        [Tooltip("Inverts the value of the Bool Event")]
        public bool InvertBool = false;

        [Tooltip("Multiply the Upcoming Result from the Float Event with this value")]
        public FloatReference multiplier = new(1f);

        // MWC — CurrentSender resolved to a Transform (GameObject / Component are converted).
        private Transform SenderTransform =>
            CurrentSender is Transform t ? t :
            CurrentSender is GameObject go ? go.transform :
            CurrentSender is Component c ? c.transform : null;

        // MWC — Owner filter: does the event's sender satisfy this listener's FilterMode?
        private bool PassFilter()
        {
            if (Listener == null || ignoreOwnerFilter) return true;

            var mode = Listener.FilterMode;
            if (mode == OwnerFilterMode.Ignore) return true;

            var owner = Listener.ResolvedOwner;
            var sender = SenderTransform;

            bool match;

            if (owner == null || sender == null) match = false;                         // unbound owner / no sender never matches
            else if (Listener.MatchHierarchy) match = sender.SameHierarchy(owner);      // same hierarchy
            else match = sender == owner;                                               // exact transform

            return mode == OwnerFilterMode.MustMatch ? match : !match;          // MustNotMatch inverts
        }

        // MWC — shared gate: active + owner filter. Fires the sender response when this entry may respond.
        /// <summary> Gate shared by all responses: entry enabled + owner filter + the response type being used. </summary>
        private bool CanRespond(string type, object value, bool useType)
        {
            if (!active)
            {
                if (debug) DebugEntry(type, value, "<color=grey><B>SKIPPED</B></color> Entry is Disabled");
                return false;
            }

            if (!PassFilter())
            {
                if (debug) DebugEntry(type, value,
                    $"<color=orange><B>SKIPPED</B></color> by Owner Filter [{Listener.FilterMode}]" +
                    $"{(Listener.MatchHierarchy ? " [Match Hierarchy]" : string.Empty)} Owner:[{FilterOwnerName}] Sender:[{SenderName}]");
                return false;
            }

            if (useSender) ResponseSender.Invoke(SenderTransform);

            if (!useType)
            {
                if (debug) DebugEntry(type, value, $"<color=grey><B>SKIPPED</B></color> [{type}] Response is Disabled");
                return false;
            }

            if (debug) DebugEntry(type, value, $"<color=green><B>RESPONSE</B></color> Sender:[{SenderName}]");

            return true;
        }

        /// <summary> Console log for this entry when its Debug toggle is on. </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("MALBERS_DEBUG")]
        private void DebugEntry(string type, object value, string message)
        {
            var val = value != null ? value.ToString() : string.Empty;
            MDebug.Log($"<B>[{OwnerName}]</B> → <B>[{EventName}]</B> {type}({val}) : {message}", Owner);
        }

        /// <summary> Logs when this entry starts or stops listening to its event (Debug toggle only). </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("MALBERS_DEBUG")]
        public void DebugRegister(bool listening)
        {
            if (!debug) return;

            var state = listening ? "<color=cyan><B>LISTENING</B></color>" : "<color=grey><B>STOPPED LISTENING</B></color>";
            MDebug.Log($"<B>[{OwnerName}]</B> → <B>[{EventName}]</B> : {state}", Owner);
        }

        /// <summary> Name of the event this entry listens to (for debug logs). </summary>
        private string EventName => Event != null ? Event.name : "<EMPTY>";

        /// <summary> Name of the GameObject holding this listener (for debug logs). </summary>
        private string OwnerName => Owner != null ? Owner.name : "No Owner";

        /// <summary> Name of the object that raised the event (for debug logs). </summary>
        private string SenderName => CurrentSender != null ? CurrentSender.name : "NULL";

        /// <summary> Name of the owner used by the listener's filter (for debug logs). </summary>
        private string FilterOwnerName =>
            (Listener != null && Listener.ResolvedOwner != null) ? Listener.ResolvedOwner.name : "NULL";

        /// <summary> Response with no parameters. </summary>
        public virtual void OnEventInvoked()
        {
            if (!CanRespond("Void", null, useVoid)) return;
            Response.Invoke();
        }

        /// <summary> Response with a string parameter. </summary>
        public virtual void OnEventInvoked(string value)
        {
            if (!CanRespond("String", value, useString)) return;
            ResponseString.Invoke(value);
        }

        /// <summary> Response with a float parameter. </summary>
        public virtual void OnEventInvoked(float value)
        {
            if (!CanRespond("Float", value, useFloat)) return;
            ResponseFloat.Invoke(value * multiplier);
        }

        /// <summary> Response with an integer parameter. </summary>
        public virtual void OnEventInvoked(int value)
        {
            if (!CanRespond("Int", value, useInt)) return;

            ResponseInt.Invoke(value);

            if (AdvancedInteger)
            {
                foreach (var item in IntEventList)
                    item.ExecuteAdvanceIntegerEvent(value);
            }
        }

        /// <summary> Response with a bool parameter. </summary>
        public virtual void OnEventInvoked(bool value)
        {
            if (!CanRespond("Bool", value, useBool)) return;

            ResponseBool.Invoke(InvertBool ? !value : value);

            if (AdvancedBool)
            {
                if (value)
                    ResponseBoolTrue.Invoke();
                else
                    ResponseBoolFalse.Invoke();
            }
        }

        /// <summary> Response with a Vector3 parameter. </summary>
        public virtual void OnEventInvoked(Vector3 value)
        {
            if (!CanRespond("Vector3", value, useVector3)) return;
            ResponseVector3.Invoke(value);
        }

        /// <summary> Response with a Vector2 parameter. </summary>
        public virtual void OnEventInvoked(Vector2 value)
        {
            if (!CanRespond("Vector2", value, useVector2)) return;
            ResponseVector2.Invoke(value);
        }

        /// <summary> Response with a GameObject parameter. </summary>
        public virtual void OnEventInvoked(GameObject value)
        {
            if (!CanRespond("GameObject", value, useGO)) return;

            if (value)
            {
                ResponseGO.Invoke(value);
                ResponseGOasT.Invoke(value.transform);
            }
            else
            {
                ResponseNull.Invoke();
            }
        }

        /// <summary> Response with a Transform parameter. </summary>
        public virtual void OnEventInvoked(Transform value)
        {
            if (!CanRespond("Transform", value, useTransform)) return;

            ResponseTransform.Invoke(value);
            if (!value) ResponseNull.Invoke();
        }

        /// <summary> Response with a Component parameter. </summary>
        public virtual void OnEventInvoked(Component value)
        {
            if (!CanRespond("Component", value, useComponent)) return;

            if (value) ResponseComponent.Invoke(value);
            else ResponseNull.Invoke();
        }

        /// <summary> Response with a Sprite parameter. </summary>
        public virtual void OnEventInvoked(Sprite value)
        {
            if (!CanRespond("Sprite", value, useSprite)) return;

            if (value) ResponseSprite.Invoke(value);
            else ResponseNull.Invoke();
        }

        public MEventItemListener()
        {
            useVoid = true;
            useInt = useFloat = useString = useBool = useGO = useTransform = useVector3 = useVector2 = useSprite = useComponent = false;
        }
    }


    /// <summary> CustomPropertyDrawer</summary>

#if UNITY_EDITOR 
    [CanEditMultipleObjects, CustomEditor(typeof(MEventListener))]
    public class MEventListenerEditor : Editor
    {
        private ReorderableList list;
        private SerializedProperty eventsListeners, showEvents, SelectedEvent,
            useFloat, useBool, useInt, useString, useVoid, useGo, useTransform, useVector3, useSprite, useVector2, useComponent,
            useSender, filterMode, ownerFilter, matchHierarchy;
        private MEventListener M;
        // MonoScript script;

        private readonly Dictionary<string, ReorderableList> innerListDict = new();



        private static GUIContent _icon_short;
        public static GUIContent Icon_Short
        {
            get
            {
                if (_icon_short == null)
                {
                    _icon_short = EditorGUIUtility.IconContent("d_Shortcut Icon", "Shortcut");
                    _icon_short.tooltip = "Show/Hide Description";
                }

                return _icon_short;
            }
        }

        public virtual void OnEnable()
        {
            M = ((MEventListener)target);
            //   script = MonoScript.FromMonoBehaviour(M);

            eventsListeners = serializedObject.FindProperty("Events");
            showEvents = serializedObject.FindProperty("ShowEvents");
            SelectedEvent = serializedObject.FindProperty("SelectedEvent");
            filterMode = serializedObject.FindProperty("FilterMode");
            ownerFilter = serializedObject.FindProperty("OwnerFilter");
            matchHierarchy = serializedObject.FindProperty("MatchHierarchy");
            // debug = serializedObject.FindProperty("debug");

            EventPopup();

            list = new(serializedObject, eventsListeners, true, false, true, true)
            {
                drawElementCallback = DrawElementCallback,
                // drawHeaderCallback = HeaderCallbackDelegate,
                onAddCallback = OnAddCallBack,
                onSelectCallback = OnSelected
            };


            list.index = SelectedEvent.intValue;

        }

        private void OnSelected(ReorderableList list)
        {
            SelectedEvent.intValue = list.index;
        }

        GUIStyle TypeStyle;

        //void HeaderCallbackDelegate(Rect rect)
        //{
        //    EditorGUI.LabelField(rect, "   Malbers Events");
        //}

        void DrawElementCallback(Rect rect, int index, bool isActive, bool isFocused)
        {
            rect.y += 2;
            rect.height -= 5;

            var dC = GUI.backgroundColor;
            GUI.backgroundColor = isActive ? MTools.MBlue : dC;

            SerializedProperty Element = eventsListeners.GetArrayElementAtIndex(index);

            var eventt = Element.FindPropertyRelative("Event");
            var active = Element.FindPropertyRelative("active");
            var debugE = Element.FindPropertyRelative("debug");


            var eventRect = new Rect(rect);
            var activeRect = new Rect(rect);
            activeRect.width = 20;
            eventRect.x += 20;
            eventRect.width -= 20 + 28; //Leave a gutter for the debug icon

            var debugRect = new Rect(rect)
            {
                x = rect.x + rect.width - 26,
                width = 26,
                height = EditorGUIUtility.singleLineHeight
            };


            eventsListeners.GetArrayElementAtIndex(index);
            EditorGUI.PropertyField(activeRect, active, GUIContent.none);
            EditorGUI.PropertyField(eventRect, eventt, GUIContent.none);
            MalbersEditor.DrawDebugIcon(debugRect, debugE);

            GUI.backgroundColor = dC;
        }

        void OnAddCallBack(ReorderableList list)
        {
            if (M.Events == null) M.Events = new();
            M.Events.Add(new MEventItemListener());
        }

        GUIStyle Description_Style;


        private string[] EventPopupList;


        private void EventPopup()
        {
            EventPopupList = new string[eventsListeners.arraySize];

            for (int i = 0; i < EventPopupList.Length; i++)
            {
                EventPopupList[i] = M.Events[i].Event != null ? M.Events[i].Event.name : "<EMPTY>";
            }
        }

        /// <summary> Cached style to use to draw the popup button. </summary>
        private GUIStyle popupStyle;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (Description_Style == null)
            {
                Description_Style = new GUIStyle(MTools.StyleGreen)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    stretchWidth = true
                };

                Description_Style.normal.textColor = EditorStyles.label.normal.textColor;
            }

            MalbersEditor.DrawDescription("Listen to [MEvents] and response when the events are invoked");

            // MWC — Owner filter (applies to all entries; entries can opt out with 'Ignore Owner Filter')
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(filterMode, new GUIContent("Owner Filter",
                    "Filter responses by the event's Sender. Ignore = respond to everything."));

                if (filterMode.intValue != (int)OwnerFilterMode.Ignore)
                {
                    EditorGUILayout.PropertyField(ownerFilter, new GUIContent("Owner",
                        "The owner this listener belongs to (e.g. the player). Scene Transform or TransformVar."));
                    EditorGUILayout.PropertyField(matchHierarchy, new GUIContent("Match Hierarchy",
                        "Match by hierarchy: the Sender is the Owner or any child of it. Off = exact Transform match."));
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    MalbersEditor.Foldout(showEvents, $"Events [{eventsListeners.arraySize}]");

                    if (!showEvents.boolValue)
                    {
                        if (popupStyle == null)
                        {
                            popupStyle = new(GUI.skin.GetStyle("PaneOptions"));
                            popupStyle.imagePosition = ImagePosition.ImageOnly;
                        }
                    }
                }

                list.index = SelectedEvent.intValue;

                if (showEvents.boolValue)
                {
                    list.DoLayoutList();
                }
            }
            if (list.index != -1)
            {
                if (list.index < list.count)
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        SerializedProperty Element = eventsListeners.GetArrayElementAtIndex(list.index);
                        using (new GUILayout.HorizontalScope())
                        {


                            if (!showEvents.boolValue)
                            {
                                if (EventPopupList.Length != list.count) EventPopup();
                                SelectedEvent.intValue = EditorGUILayout.Popup(SelectedEvent.intValue, EventPopupList, popupStyle, GUILayout.Width(15));
                            }

                            EditorGUI.indentLevel++;
                            EditorGUIUtility.labelWidth = 20;


                            EditorGUILayout.PropertyField(Element, new GUIContent($"[{list.index}]"), false, GUILayout.Width(20));

                            useBool = Element.FindPropertyRelative("useBool");

                            var IDD = Element.FindPropertyRelative("Event");

                            //var active = Element.FindPropertyRelative("active");
                            //EditorGUILayout.PropertyField(active, GUIContent.none, GUILayout.Width(15));

                            EditorGUIUtility.labelWidth = 20;
                            using (new EditorGUI.DisabledGroupScope(true))
                                EditorGUILayout.ObjectField(IDD, new GUIContent("  "), GUILayout.MinWidth(50));
                            EditorGUIUtility.labelWidth = 0;

                            //Update Array List



                            //Debug Icon (per event entry)
                            MalbersEditor.DrawDebugIcon(Element.FindPropertyRelative("debug"));

                            //Description Icon
                            var dC = GUI.color;
                            GUI.color = useBool.isExpanded ? dC : dC * 0.8f;
                            useBool.isExpanded = GUILayout.Toggle(useBool.isExpanded, Icon_Short,
                                EditorStyles.label, GUILayout.Width(25), GUILayout.Height(25));
                            GUI.color = dC;
                            EditorGUI.indentLevel--;
                        }


                        if (M.Events[list.index].Event != null && Element.isExpanded)
                        {
                            if (useBool.isExpanded)
                            {
                                var Descp = M.Events[list.index].Event.Description;

                                if (Descp != string.Empty)
                                {


                                    M.Events[list.index].Event.Description = UnityEditor.EditorGUILayout.TextArea(Descp, Description_Style);
                                }
                                EditorGUILayout.Space();
                            }


                            useFloat = Element.FindPropertyRelative("useFloat");

                            useBool = Element.FindPropertyRelative("useBool");
                            useInt = Element.FindPropertyRelative("useInt");
                            useString = Element.FindPropertyRelative("useString");
                            useVoid = Element.FindPropertyRelative("useVoid");
                            useGo = Element.FindPropertyRelative("useGO");
                            useTransform = Element.FindPropertyRelative("useTransform");
                            useComponent = Element.FindPropertyRelative("useComponent");
                            useVector3 = Element.FindPropertyRelative("useVector3");
                            useVector2 = Element.FindPropertyRelative("useVector2");
                            useSprite = Element.FindPropertyRelative("useSprite");
                            useSender = Element.FindPropertyRelative("useSender");


                            TypeStyle ??= new GUIStyle(EditorStyles.objectField)
                            {
                                alignment = TextAnchor.MiddleCenter,
                                fontStyle = FontStyle.Bold,
                            };

                            using (new GUILayout.HorizontalScope())
                            {
                                useVoid.boolValue = GUILayout.Toggle(useVoid.boolValue, new GUIContent("Void", "No Parameters Response"), TypeStyle);
                                useBool.boolValue = GUILayout.Toggle(useBool.boolValue, new GUIContent("Bool", "Bool Response"), TypeStyle);
                                useFloat.boolValue = GUILayout.Toggle(useFloat.boolValue, new GUIContent("Float", "Float Response"), TypeStyle);
                                useInt.boolValue = GUILayout.Toggle(useInt.boolValue, new GUIContent("Int", "Int Response"), TypeStyle);
                                useString.boolValue = GUILayout.Toggle(useString.boolValue, new GUIContent("String", "String Response"), TypeStyle);
                                useVector3.boolValue = GUILayout.Toggle(useVector3.boolValue, new GUIContent("V3", "Vector3 Response"), TypeStyle);
                                useVector2.boolValue = GUILayout.Toggle(useVector2.boolValue, new GUIContent("V2", "Vector2 Response"), TypeStyle);
                            }


                            using (new GUILayout.HorizontalScope())
                            {
                                useGo.boolValue = GUILayout.Toggle(useGo.boolValue, new GUIContent("GameObject", "Game Object Response"), TypeStyle);
                                useTransform.boolValue = GUILayout.Toggle(useTransform.boolValue, new GUIContent("Transform", "Transform Response"), TypeStyle);
                                useComponent.boolValue = GUILayout.Toggle(useComponent.boolValue, new GUIContent("Component", "Component Response"), TypeStyle);
                                useSprite.boolValue = GUILayout.Toggle(useSprite.boolValue, new GUIContent("Sprite", "Sprite Response"), TypeStyle);
                                useSender.boolValue = GUILayout.Toggle(useSender.boolValue, new GUIContent("Sender", "Fires the event's Sender (as Transform) when this entry responds"), TypeStyle);
                            }

                            // MWC — per-entry opt-out from the component's Owner filter
                            var ignoreFilter = Element.FindPropertyRelative("ignoreOwnerFilter");
                            if (M.FilterMode != OwnerFilterMode.Ignore)
                            {
                                ignoreFilter.boolValue = EditorGUILayout.ToggleLeft(
                                    new GUIContent("Ignore Owner Filter", "This entry always responds, bypassing the component's Owner filter."),
                                    ignoreFilter.boolValue);
                            }


                            Draw_Void(Element);
                            Draw_Bool(Element);
                            Draw_Float(Element);
                            Draw_Integer(Element);
                            DrawString(Element);
                            Draw_GameObject(Element);
                            DrawTransform(Element);
                            DrawComponent(Element);
                            DrawSprite(Element);
                            DrawVector2(Element);
                            DrawVector3(Element);
                            Draw_Sender(Element);
                        }
                    }
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        private void Draw_Void(SerializedProperty Element)
        {
            if (useVoid.boolValue)
            {
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("Response"));
            }
        }

        private void Draw_Bool(SerializedProperty Element)
        {
            if (useBool.boolValue)
            {
                MalbersEditor.DrawLineHelpBox();
                var useAdvBool = Element.FindPropertyRelative("AdvancedBool");
                // if (!useAdvBool.boolValue)
                {
                    EditorGUILayout.PropertyField(Element.FindPropertyRelative("InvertBool"));
                    EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseBool"), new GUIContent("Response"));
                }
                useAdvBool.boolValue = EditorGUILayout.ToggleLeft(new GUIContent("Use Advanced Bool", "Uses Separated Unity Events for True and False Entries"), useAdvBool.boolValue);
                if (useAdvBool.boolValue)
                {
                    EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseBoolTrue"), new GUIContent("On True"));
                    EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseBoolFalse"), new GUIContent("On False"));
                }
            }
        }

        private void Draw_Float(SerializedProperty Element)
        {
            if (useFloat.boolValue)
            {
                MalbersEditor.DrawLineHelpBox();
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseFloat"), new GUIContent("Response"));
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("multiplier"));
            }
        }

        private void Draw_Integer(SerializedProperty Element)
        {
            if (useInt.boolValue)
            {
                MalbersEditor.DrawLineHelpBox();
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseInt"), new GUIContent("Response"));
                MalbersEditor.DrawLineHelpBox();

                var useAdvInteger = Element.FindPropertyRelative("AdvancedInteger");
                useAdvInteger.boolValue = GUILayout.Toggle(useAdvInteger.boolValue, new GUIContent("Use Integer Comparer",
                    "Compare the Event entry value with a new  one to make a new Int Response"), EditorStyles.foldoutHeader);

                if (useAdvInteger.boolValue)
                {
                    var compare = Element.FindPropertyRelative("IntEventList");
                    ReorderableList Reo_AbilityList;

                    string listKey = Element.propertyPath;

                    if (innerListDict.ContainsKey(listKey))
                    {
                        // fetch the reorderable list in dict
                        Reo_AbilityList = innerListDict[listKey];
                    }
                    else
                    {
                        Reo_AbilityList = new ReorderableList(serializedObject, compare, true, true, true, true)
                        {
                            drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
                            {
                                var element = compare.GetArrayElementAtIndex(index);
                                var name = element.FindPropertyRelative("name");
                                var comparer = element.FindPropertyRelative("comparer");
                                var Value = element.FindPropertyRelative("Value");
                                var active = element.FindPropertyRelative("active");

                                rect.y += 1;
                                var height = UnityEditor.EditorGUIUtility.singleLineHeight;
                                var split = rect.width / 3;
                                var p = 5;

                                var rectActveName = new Rect(rect.x, rect.y, 20, height);
                                var rectName = new Rect(rect.x + 20, rect.y, (split + p - 2) * 1.2f - 20, height);
                                var rectComparer = new Rect(rect.x + (split + p) * 1.2f, rect.y, (split - p) * 0.75f, height);
                                var rectValue = new Rect(rect.x + split * 2 + p + 15, rect.y, split - p - 10, height);


                                var def = GUI.color;

                                if (isActive) GUI.color = Color.yellow;
                                if (!active.boolValue) GUI.color = Color.gray;

                                UnityEditor.EditorGUI.PropertyField(rectActveName, active, GUIContent.none);
                                UnityEditor.EditorGUI.PropertyField(rectName, name, GUIContent.none);
                                UnityEditor.EditorGUI.PropertyField(rectComparer, comparer, GUIContent.none);
                                UnityEditor.EditorGUI.PropertyField(rectValue, Value, GUIContent.none);
                                GUI.color = def;
                            },

                            drawHeaderCallback = (Rect rect) =>
                            {
                                rect.y += 1;
                                var height = UnityEditor.EditorGUIUtility.singleLineHeight;
                                var split = rect.width / 3;
                                var p = (split * 0.3f);
                                var rectName = new Rect(rect.x, rect.y, split + p - 2, height);
                                var rectComparer = new Rect(rect.x + split + p, rect.y, split - p + 15, height);
                                var rectValue = new Rect(rect.x + split * 2 + p + 5, rect.y, split - p, height);
                                var DebugRect = new Rect(rect.width, rect.y - 1, 25, height + 2);

                                EditorGUI.LabelField(rectName, "    Name");
                                EditorGUI.LabelField(rectComparer, " Compare");
                                EditorGUI.LabelField(rectValue, " Value");
                            },
                        };


                        innerListDict.Add(listKey, Reo_AbilityList);  //Store it on the Editor

                    }
                    Reo_AbilityList.DoLayoutList();

                    int SelectedItem = Reo_AbilityList.index;

                    if (SelectedItem != -1 && SelectedItem < Reo_AbilityList.count)
                    {
                        var element = compare.GetArrayElementAtIndex(SelectedItem);
                        if (element != null)
                        {
                            UnityEditor.EditorGUILayout.Space(-20);

                            var description = element.FindPropertyRelative("description");

                            //   if (styleDesc == null)
                            styleDesc = new GUIStyle(MTools.StyleGray)
                            {
                                fontSize = 14,
                                fontStyle = FontStyle.Normal,
                                alignment = TextAnchor.MiddleLeft,
                                stretchWidth = true
                            };

                            styleDesc.normal.textColor = UnityEditor.EditorStyles.boldLabel.normal.textColor;


                            UnityEditor.EditorGUILayout.LabelField("Description", UnityEditor.EditorStyles.boldLabel);
                            description.stringValue = UnityEditor.EditorGUILayout.TextArea(description.stringValue, styleDesc);

                            var Response = element.FindPropertyRelative("Response");
                            var name = element.FindPropertyRelative("name").stringValue;
                            UnityEditor.EditorGUILayout.PropertyField(Response, new GUIContent("Response: [" + name + "]   "));
                        }
                    }
                    ////   else
                    //{
                    //    MalbersEditor.DrawLineHelpBox();
                    //    EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseInt"), new GUIContent("Response"));
                    //}
                }
            }
        }

        protected GUIStyle style, styleDesc;


        private void DrawString(SerializedProperty Element)
        {
            if (useString.boolValue)
            {
                MalbersEditor.DrawLineHelpBox();
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseString"), new GUIContent("Response"));
            }
        }

        // MWC — Sender response: exposes who raised the event (as Transform)
        private void Draw_Sender(SerializedProperty Element)
        {
            if (useSender.boolValue)
            {
                MalbersEditor.DrawLineHelpBox();
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseSender"), new GUIContent("Response Sender"));
            }
        }

        private void Draw_GameObject(SerializedProperty Element)
        {
            if (useGo.boolValue)
            {
                MalbersEditor.DrawLineHelpBox();
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseGO"), new GUIContent("Response GameObject"));
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseGOasT"), new GUIContent("Response GameObject as Transform"));
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseNull"), new GUIContent("Response NULL"));
            }
        }

        private void DrawTransform(SerializedProperty Element)
        {
            if (useTransform.boolValue)
            {
                MalbersEditor.DrawLineHelpBox();
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseTransform"), new GUIContent("Response Transform"));
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseNull"), new GUIContent("Response NULL"));
            }
        }

        private void DrawComponent(SerializedProperty Element)
        {
            if (useComponent.boolValue)
            {
                MalbersEditor.DrawLineHelpBox();
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseComponent"), new GUIContent("Response C"));
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseNull"), new GUIContent("Response NULL"));
            }
        }

        private void DrawSprite(SerializedProperty Element)
        {
            if (useSprite.boolValue)
            {
                MalbersEditor.DrawLineHelpBox();
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseSprite"), new GUIContent("Response Sprite"));
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseNull"), new GUIContent("Response NULL"));
            }
        }

        private void DrawVector2(SerializedProperty Element)
        {
            if (useVector3.boolValue)
            {
                MalbersEditor.DrawLineHelpBox();
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseVector3"), new GUIContent("Response V3"));
            }
        }

        private void DrawVector3(SerializedProperty Element)
        {
            if (useVector2.boolValue)
            {
                MalbersEditor.DrawLineHelpBox();
                EditorGUILayout.PropertyField(Element.FindPropertyRelative("ResponseVector2"), new GUIContent("Response V2"));
            }
        }
    }
#endif
}