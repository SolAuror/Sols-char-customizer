// Last Change: 9/21/2026 9:40am
using MalbersAnimations.Events;
using System.Collections; // MWC — needed for the LongPress/Delay coroutines
using UnityEngine;
using UnityEngine.Events;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace MalbersAnimations.InputSystem
{
#if ENABLE_INPUT_SYSTEM
    // MWC — FastInput upgraded from struct to class so it can hold runtime state (coroutines, tap timers)
    // and support the same MInputInteraction types as MInputLink (Toggle, DoubleTap, LongPress, etc.)
    /// <summary>Lightweight Input entry that translates an InputAction into Malbers events using MInputInteraction types</summary>
    [System.Serializable]
    public class FastInput
    {
        public string name = "Input"; // MWC — default value (class field initializers now apply)
        public bool debug;

        [Tooltip("Type of interaction the input has (same as MInput Link)")]
        public MInputInteraction interaction = MInputInteraction.Press; // MWC — Press keeps the old FastInput behavior

        public InputAction input;

        #region Interaction Settings // MWC — new block: per-interaction tuning values (plain floats to keep Fast Input simple)
        [Tooltip("Max time between taps to be considered a Double Tap")]
        public float DoubleTapTime = 0.3f;

        [Tooltip("Time the Input should be pressed to be considered a Long Press")]
        public float LongPressTime = 0.5f;

        [Tooltip("Delay before the Long Press starts. If the Input is released within this time, the Long Press is ignored")]
        public float LongPressDelay = 0f;

        [Tooltip("If the Input Up is not released within this time, it will be considered as a failed input")]
        public float InputUpFailed = 0.1f;

        [Tooltip("Multiply the Vector2 value by this value")]
        public float Vector2Mult = 1f;
        #endregion

        [Space]
        public UnityEvent OnInputDown = new();
        public UnityEvent OnInputUp = new();
        public BoolEvent OnInputPressed = new();

        // MWC — new block: events for the new interactions
        public UnityEvent OnLongPress = new();
        public UnityEvent OnDoubleTap = new();
        public FloatEvent OnInputFloatValue = new();
        public Vector2Event OnInputV2Value = new();

        #region Runtime State // MWC — new block: internal state ported from MInputAction
        /// <summary>MonoBehaviour used to run the LongPress coroutines (set by MFastInput on enable)</summary>
        public MonoBehaviour MCoroutine { get; set; }

        private IEnumerator C_LongPress;
        private bool FirstInputPress;       // MWC — DoubleTap: first tap registered
        private bool InputCompleted;        // MWC — LongPress: completed flag
        private bool LongPressTempValue;    // MWC — LongPress: pressed while waiting the delay
        private float InputStartTime;

        /// <summary>Current Input Value (true = pressed/toggled on)</summary>
        public bool InputValue { get; private set; }
        #endregion

        // MWC — renamed from InputAction (clashed with the type name) and rewritten to route every
        // MInputInteraction exactly like MInputAction.TranslateInput on MInputLink.
        /// <summary>Translates the Unity Input System callback into the Malbers events using the selected interaction</summary>
        public void TranslateInput(InputAction.CallbackContext context)
        {
            bool OldValue = InputValue;
            bool NewValue = context.performed || context.started;

            var Performed = context.phase == InputActionPhase.Performed;
            var Started = context.phase == InputActionPhase.Started;
            var Canceled = context.phase == InputActionPhase.Canceled;

            switch (interaction) // MWC — whole switch is new
            {
                #region Press Interaction
                case MInputInteraction.Press:

                    InputValue = NewValue;

                    if (OldValue != InputValue) // MWC — guard so Started+Performed don't fire Down twice
                    {
                        if (InputValue)
                        {
                            OnInputDown.Invoke();
                            DebugInput("Pressed");
                        }
                        else
                        {
                            OnInputUp.Invoke();
                            DebugInput("Released");
                        }

                        OnInputPressed.Invoke(InputValue);
                    }
                    break;
                #endregion

                #region Down Interaction
                case MInputInteraction.Down:

                    if (Started)
                    {
                        OnInputDown.Invoke();
                        OnInputPressed.Invoke(true);
                        InputValue = true;
                        DebugInput("Down");
                    }
                    else if (Performed)
                    {
                        OnInputPressed.Invoke(false);
                        InputValue = false;
                    }
                    break;
                #endregion

                #region Up Interaction
                case MInputInteraction.Up:

                    if (Performed)
                    {
                        InputValue = true;
                        InputStartTime = Time.time; //Store the time when the Input was pressed
                    }
                    else if (Canceled)
                    {
                        //It must have been pressed, AND either there's no fail window (0) or it was released inside it
                        if (InputValue && (InputUpFailed == 0 || !MTools.ElapsedTime(InputStartTime, InputUpFailed)))
                        {
                            InputValue = false;
                            OnInputUp.Invoke();
                            OnInputPressed.Invoke(true);
                            DebugInput("Up");

                            //Release the Input Value after a frame
                            MCoroutine.Delay_Action(() => OnInputPressed.Invoke(false));
                        }
                    }
                    break;
                #endregion

                #region Long Press
                case MInputInteraction.LongPress:

                    if (Performed)
                    {
                        LongPressTempValue = true;

                        if (C_LongPress != null)
                            MCoroutine.StopCoroutine(C_LongPress); //Clear the Long Press Coroutine

                        C_LongPress = IEnum_LongPress();
                        MCoroutine.StartCoroutine(C_LongPress);
                    }
                    else if (Canceled)
                    {
                        LongPressTempValue = false;

                        //If the Input was released before the LongPress completed, take it as Interrupted
                        //(On Input Up serves as Interrupted)
                        if (!InputCompleted)
                        {
                            if (C_LongPress != null) MCoroutine.StopCoroutine(C_LongPress);
                        }

                        OnInputUp.Invoke(); //Invoke no matter what
                        DebugInput("Long Press Released");

                        InputCompleted = false; //Reset the Long Press
                        OnInputPressed.Invoke(false);
                        InputValue = false;
                    }
                    break;
                #endregion

                #region Double Tap
                case MInputInteraction.DoubleTap:

                    InputValue = NewValue;

                    if (OldValue != InputValue)
                    {
                        OnInputPressed.Invoke(InputValue); //Just to make sure the Input is Pressed

                        if (InputValue)
                        {
                            if (InputStartTime != 0 && MTools.ElapsedTime(InputStartTime, DoubleTapTime))
                            {
                                FirstInputPress = false; //It was just one Tap... reset it
                            }

                            if (!FirstInputPress)
                            {
                                OnInputDown.Invoke(); //First Tap
                                InputStartTime = Time.time;
                                FirstInputPress = true;
                            }
                            else
                            {
                                if ((Time.time - InputStartTime) <= DoubleTapTime)
                                {
                                    FirstInputPress = false;
                                    InputStartTime = 0;
                                    OnDoubleTap.Invoke(); //Successful Double Tap
                                    DebugInput("Double Tap");
                                }
                                else
                                {
                                    FirstInputPress = false;
                                }
                            }
                        }
                    }
                    break;
                #endregion

                #region Toggle
                case MInputInteraction.Toggle:

                    if (Started)
                    {
                        InputValue = !InputValue;

                        OnInputPressed.Invoke(InputValue);

                        if (InputValue) OnInputDown.Invoke();
                        else OnInputUp.Invoke();

                        DebugInput($"Toggle [{InputValue}]");
                    }
                    break;
                #endregion

                #region Float
                case MInputInteraction.Float:
                    {
                        var value = context.action.ReadValue<float>();

                        InputValue = value != 0;

                        if (OldValue != InputValue)
                        {
                            OnInputPressed.Invoke(InputValue);

                            if (InputValue) OnInputDown.Invoke();
                            else OnInputUp.Invoke();
                        }

                        OnInputFloatValue.Invoke(value);
                    }
                    break;
                #endregion

                #region Vector2
                case MInputInteraction.Vector2:
                    {
                        var v2 = context.action.ReadValue<Vector2>();

                        InputValue = v2 != Vector2.zero;

                        if (OldValue != InputValue)
                        {
                            OnInputPressed.Invoke(InputValue);

                            if (InputValue) OnInputDown.Invoke();
                            else OnInputUp.Invoke();
                        }

                        OnInputV2Value.Invoke(v2 * Vector2Mult);
                    }
                    break;
                #endregion

                default: break;
            }
        }

        // MWC — ported from MInputAction.IEnum_LongPress (uses plain floats instead of FloatReferences)
        /// <summary>Waits the LongPress delay, then reports the normalized hold time until the Long Press completes</summary>
        private IEnumerator IEnum_LongPress()
        {
            if (LongPressDelay > 0)
                yield return new WaitForSeconds(LongPressDelay);

            if (!LongPressTempValue) yield break; //The Input was released during the delay

            InputStartTime = Time.time;
            InputCompleted = false;
            OnInputDown.Invoke();
            OnInputPressed.Invoke(true);
            InputValue = true;

            float elapsed;

            while (!InputCompleted)
            {
                elapsed = (Time.time - InputStartTime) / LongPressTime;
                OnInputFloatValue.Invoke(elapsed);

                if (elapsed >= 1f)
                {
                    OnInputFloatValue.Invoke(1);
                    OnLongPress.Invoke();
                    DebugInput("Long Press Completed");
                    InputCompleted = true; //Avoid the Long Press being invoked more than once
                    break;
                }
                yield return null;
            }
        }

        // MWC — centralized debug log (was inlined in the old struct)
        /// <summary>Logs the input state to the console when debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void DebugInput(string state)
        {
#if UNITY_EDITOR
            if (debug) Debug.Log($"<color=yellow><B>[Fast Input: {name} - {interaction}]</B></color> {state}");
#endif
        }
    }

    [AddComponentMenu("Malbers/Input/Fast Input")]
    public class MFastInput : MonoBehaviour
    {
        public FastInput[] inputs;

        /// <summary>Enables every InputAction and connects all interaction callbacks</summary>
        private void OnEnable()
        {
            if (inputs != null && inputs.Length > 0) // MWC — was '||', which threw on a null array
            {
                for (int i = 0; i < inputs.Length; i++)
                {
                    inputs[i].MCoroutine = this;                            // MWC — coroutine host for LongPress/Up interactions
                    inputs[i].input.Enable();
                    inputs[i].input.started += inputs[i].TranslateInput;    // MWC — renamed callback
                    inputs[i].input.performed += inputs[i].TranslateInput;  // MWC — Performed drives Down/Up/LongPress/Float/Vector2
                    inputs[i].input.canceled += inputs[i].TranslateInput;   // MWC — renamed callback

                    if (inputs[i].OnInputPressed == null) inputs[i].OnInputPressed = new();
                    if (inputs[i].OnInputDown == null) inputs[i].OnInputDown = new();
                    if (inputs[i].OnInputUp == null) inputs[i].OnInputUp = new();
                }
            }
        }

        /// <summary>Disconnects all interaction callbacks and disables every InputAction</summary>
        private void OnDisable()
        {
            if (inputs != null && inputs.Length > 0) // MWC — was '||', which threw on a null array
            {
                for (int i = 0; i < inputs.Length; i++)
                {
                    inputs[i].input.started -= inputs[i].TranslateInput;    // MWC — renamed callback
                    inputs[i].input.performed -= inputs[i].TranslateInput;
                    inputs[i].input.canceled -= inputs[i].TranslateInput;   // MWC — renamed callback
                    inputs[i].input.Disable();
                }
            }
        }

        /// <summary>Creates a default Space input the first time the component is added</summary>
        private void Reset()
        {
            //Create a new InputAction with Key 'Space' and add it to the list #inputs
            if (inputs == null || inputs.Length == 0)
            {
                inputs = new FastInput[1];
                inputs[0] = new FastInput
                {
                    name = "Space",
                    input = new InputAction("Space", InputActionType.Button, "<Keyboard>/space")
                };
            }
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(MFastInput))]
    public class MFastInputEditor : Editor
    {
        private ReorderableList inputList;
        private SerializedProperty inputs;

        private void OnEnable()
        {
            inputs = serializedObject.FindProperty("inputs");
            BuildList();
        }

        private void BuildList()
        {
            // displayRemoveButton = false: we draw a per-element [x] button instead
            inputList = new ReorderableList(serializedObject, inputs, true, true, true, false)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Input Name"),

                // MWC — new elements are zero-initialized by the serializer, so restore the class defaults
                onAddCallback = list =>
                {
                    bool wasEmpty = inputs.arraySize == 0;
                    inputs.arraySize++;
                    list.index = inputs.arraySize - 1;

                    if (wasEmpty)
                    {
                        var el = inputs.GetArrayElementAtIndex(0);
                        el.FindPropertyRelative("name").stringValue = "New Input";
                        el.FindPropertyRelative("DoubleTapTime").floatValue = 0.3f;
                        el.FindPropertyRelative("LongPressTime").floatValue = 0.5f;
                        el.FindPropertyRelative("InputUpFailed").floatValue = 0.1f;
                        el.FindPropertyRelative("Vector2Mult").floatValue = 1f;
                    }
                },

                elementHeightCallback = index =>
                {
                    if (index >= inputs.arraySize) return EditorGUIUtility.singleLineHeight + 6;
                    var inputProp = inputs.GetArrayElementAtIndex(index).FindPropertyRelative("input");
                    float inputH = EditorGUI.GetPropertyHeight(inputProp, GUIContent.none, true);
                    return EditorGUIUtility.singleLineHeight               // row 1
                         + EditorGUIUtility.standardVerticalSpacing        // gap
                         + inputH                                          // row 2 (actual)
                         + EditorGUIUtility.standardVerticalSpacing * 3;  // top + bottom pad
                },

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    if (index >= inputs.arraySize) return;

                    var element   = inputs.GetArrayElementAtIndex(index);
                    var nameProp  = element.FindPropertyRelative("name");
                    var debugProp = element.FindPropertyRelative("debug");
                    var inputProp = element.FindPropertyRelative("input");
                    var interProp = element.FindPropertyRelative("interaction");

                    float pad     = EditorGUIUtility.standardVerticalSpacing;
                    float lineH   = EditorGUIUtility.singleLineHeight;
                    const float debugW  = 28f;
                    const float removeW = 20f;
                    const float interW  = 90f; // MWC — interaction popup width
                    float nameW   = rect.width - interW - debugW - removeW - 8; // MWC — reserve room for the popup

                    float y = rect.y + pad;

                    // Row 1: [Name ......][Interaction][Debug][X]   // MWC — interaction popup added
                    var nameRect   = new Rect(rect.x,                               y, nameW,   lineH);
                    var interRect  = new Rect(rect.x + nameW + 2,                   y, interW,  lineH);
                    var debugRect  = new Rect(rect.x + nameW + interW + 4,          y, debugW,  lineH); // MWC — shifted
                    var removeRect = new Rect(rect.x + nameW + interW + debugW + 6, y, removeW, lineH); // MWC — shifted

                    EditorGUI.PropertyField(nameRect, nameProp, GUIContent.none);
                    EditorGUI.PropertyField(interRect, interProp, GUIContent.none);
                    MalbersEditor.DrawDebugIcon(debugRect, debugProp);

                    if (GUI.Button(removeRect, EditorGUIUtility.IconContent("d_TreeEditor.Trash"), EditorStyles.miniButtonRight))
                    {
                        inputs.DeleteArrayElementAtIndex(index);
                        serializedObject.ApplyModifiedProperties();
                        return;
                    }

                    // Row 2: [---- InputAction ----]
                    y += lineH + pad;
                    float inputH  = EditorGUI.GetPropertyHeight(inputProp, GUIContent.none, true);
                    var inputRect = new Rect(rect.x, y, rect.width, inputH);
                    EditorGUI.PropertyField(inputRect, inputProp, GUIContent.none);
                },
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            inputList.DoLayoutList();

            int index = inputList.index;
            if (index >= 0 && index < inputs.arraySize)
            {
                var element   = inputs.GetArrayElementAtIndex(index);
                var onDown    = element.FindPropertyRelative("OnInputDown");
                var onUp      = element.FindPropertyRelative("OnInputUp");
                var onPressed = element.FindPropertyRelative("OnInputPressed");

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var name = element.FindPropertyRelative("name").stringValue;
                    onDown.isExpanded = MalbersEditor.Foldout(onDown.isExpanded, $"Events  [{name}]");

                    if (onDown.isExpanded)
                    {
                        // MWC — show only the events/settings that the selected interaction uses (same as MInput Link)
                        var interaction = (MInputInteraction)element.FindPropertyRelative("interaction").enumValueIndex;

                        switch (interaction)
                        {
                            case MInputInteraction.Press:
                                EditorGUILayout.PropertyField(onDown);
                                EditorGUILayout.PropertyField(onUp);
                                EditorGUILayout.PropertyField(onPressed);
                                break;

                            case MInputInteraction.Down:
                                EditorGUILayout.PropertyField(onDown);
                                EditorGUILayout.PropertyField(onPressed);
                                break;

                            case MInputInteraction.Up:
                                EditorGUILayout.PropertyField(element.FindPropertyRelative("InputUpFailed"));
                                EditorGUILayout.Space();
                                EditorGUILayout.PropertyField(onUp);
                                EditorGUILayout.PropertyField(onPressed);
                                break;

                            case MInputInteraction.LongPress:
                                EditorGUILayout.PropertyField(element.FindPropertyRelative("LongPressTime"));
                                EditorGUILayout.PropertyField(element.FindPropertyRelative("LongPressDelay"));
                                EditorGUILayout.Space();
                                EditorGUILayout.PropertyField(element.FindPropertyRelative("OnLongPress"), new GUIContent("On Long Press Completed"));
                                EditorGUILayout.PropertyField(element.FindPropertyRelative("OnInputFloatValue"), new GUIContent("On Pressed Time Normalized"));
                                EditorGUILayout.PropertyField(onDown, new GUIContent("On Input Down"));
                                EditorGUILayout.PropertyField(onUp, new GUIContent("On Press Interrupted (On Input Up)"));
                                EditorGUILayout.PropertyField(onPressed);
                                break;

                            case MInputInteraction.DoubleTap:
                                EditorGUILayout.PropertyField(element.FindPropertyRelative("DoubleTapTime"));
                                EditorGUILayout.Space();
                                EditorGUILayout.PropertyField(onDown, new GUIContent("On First Tap"));
                                EditorGUILayout.PropertyField(element.FindPropertyRelative("OnDoubleTap"));
                                EditorGUILayout.PropertyField(onPressed);
                                break;

                            case MInputInteraction.Toggle:
                                EditorGUILayout.PropertyField(onPressed, new GUIContent("On Input Toggle"));
                                EditorGUILayout.PropertyField(onDown, new GUIContent("On Toggle On"));
                                EditorGUILayout.PropertyField(onUp, new GUIContent("On Toggle Off"));
                                break;

                            case MInputInteraction.Float:
                                EditorGUILayout.PropertyField(element.FindPropertyRelative("OnInputFloatValue"), new GUIContent("On Input Float"));
                                EditorGUILayout.PropertyField(onDown);
                                EditorGUILayout.PropertyField(onUp);
                                EditorGUILayout.PropertyField(onPressed);
                                break;

                            case MInputInteraction.Vector2:
                                EditorGUILayout.PropertyField(element.FindPropertyRelative("Vector2Mult"), new GUIContent("Vector2 Multiplier"));
                                EditorGUILayout.PropertyField(element.FindPropertyRelative("OnInputV2Value"), new GUIContent("On Vector2 Value"));
                                EditorGUILayout.PropertyField(onDown);
                                EditorGUILayout.PropertyField(onUp);
                                EditorGUILayout.PropertyField(onPressed);
                                break;

                            default:
                                EditorGUILayout.PropertyField(onDown);
                                EditorGUILayout.PropertyField(onUp);
                                EditorGUILayout.PropertyField(onPressed);
                                break;
                        }
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif

#endif
}
