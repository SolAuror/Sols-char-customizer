// Last Change: 9/17/2026 6:05pm
using MalbersAnimations.Events;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
#endif

// MWC — Reads a value from a member (property / field / zero-arg method) on any Unity Object and pushes
// it out through the typed UnityEvent that matches the member's type. It is the read counterpart of
// MemberValueSetter/MemberValuesSetter. The member is always read from a Component found on the target
// GameObject (self → parent → children); it shares the Target Type inspector row (MemberDrawerUtility).
//
// Typical use: read SuperItem.Description and dynamic-bind OnString to MEvent.Invoke(string), so any
// MEventListener (UI text, etc.) receives the value.
//
// Everything except the events lives in MemberValueReader: owners that consume the value themselves
// (MEventReaction) use that one instead, so they do not serialize twelve unused events.
namespace MalbersAnimations
{
    /// <summary> A MemberValueReader that also pushes the value it reads through the typed UnityEvent matching its type. </summary>
    [Serializable]
    public class MemberValueGetter : MemberValueReader
    {
        // ─── Output channels ─────────────────────────────────────────────────────
        // MWC — exactly one of these is used, selected by valueKind. The drawer shows only that one.

        public FloatEvent OnFloat = new();
        public IntEvent OnInt = new();
        public BoolEvent OnBool = new();
        public StringEvent OnString = new();
        public GameObjectEvent OnGameObject = new();
        public TransformEvent OnTransform = new();
        public ComponentEvent OnComponent = new();
        public SpriteEvent OnSprite = new();
        public UnityObjectEvent OnObject = new();
        public Vector2Event OnVector2 = new();
        public Vector3Event OnVector3 = new();
        public ColorEvent OnColor = new();

        /// <summary>
        /// Reads the member and invokes the UnityEvent matching <see cref="MemberValueReader.valueKind"/>.
        /// Returns false when the member could not be read.
        /// </summary>
        public bool Raise(Object target)
        {
            if (valueKind == MGetKind.None) return false;
            if (!TryRead(target, out var raw)) return false;

            Send(raw);
            return true;
        }

        //MWC — Cleared on every deserialize, so the check runs once per fresh instance
        [NonSerialized] private bool argumentsFixed;

        private void Send(object raw)
        {
            //MWC — An empty [None] Object argument authored in the Inspector deserializes as a FAKE-null, which makes
            //Unity's own persistent-call builder throw before any listener runs. Only the Object-carrying events can
            //hit it. See UnityEventTools.FixNullObjectArguments.
            if (!argumentsFixed)
            {
                argumentsFixed = true;

                UnityEventTools.FixNullObjectArguments(OnGameObject);
                UnityEventTools.FixNullObjectArguments(OnTransform);
                UnityEventTools.FixNullObjectArguments(OnComponent);
                UnityEventTools.FixNullObjectArguments(OnSprite);
                UnityEventTools.FixNullObjectArguments(OnObject);
            }

            switch (valueKind)
            {
                case MGetKind.Float: OnFloat.Invoke(ToFloat(raw)); break;
                case MGetKind.Int: OnInt.Invoke(ToInt(raw)); break;
                case MGetKind.Bool: OnBool.Invoke(raw is bool b && b); break;
                case MGetKind.String: OnString.Invoke(raw as string ?? string.Empty); break;
                case MGetKind.GameObject: OnGameObject.Invoke(raw as GameObject); break;
                case MGetKind.Transform: OnTransform.Invoke(raw as Transform); break;
                case MGetKind.Component: OnComponent.Invoke(raw as Component); break;
                case MGetKind.Sprite: OnSprite.Invoke(raw as Sprite); break;
                case MGetKind.Object: OnObject.Invoke(raw as Object); break;
                case MGetKind.Vector2: OnVector2.Invoke(ToVector2(raw)); break;
                case MGetKind.Vector3: OnVector3.Invoke(ToVector3(raw)); break;
                case MGetKind.Color: OnColor.Invoke(raw is Color c ? c : default); break;
            }
        }

        /// <summary>Human readable "Type.Member(args) → On Kind" description, naming the event the value goes out through.</summary>
        public override string Description() => $"{MemberSignature} → On {valueKind}";
    }


#if UNITY_EDITOR

    // ─── Property Drawer ─────────────────────────────────────────────────────────

    /// <summary> The reader rows plus the one output event that can carry the picked member's value. </summary>
    [CustomPropertyDrawer(typeof(MemberValueGetter))]
    public class MemberValueGetterDrawer : MemberValueReaderDrawer
    {
        // MWC — serialized field name of the event that carries each kind.
        private static string EventPropertyName(MGetKind kind) => kind switch
        {
            MGetKind.Float => "OnFloat",
            MGetKind.Int => "OnInt",
            MGetKind.Bool => "OnBool",
            MGetKind.String => "OnString",
            MGetKind.GameObject => "OnGameObject",
            MGetKind.Transform => "OnTransform",
            MGetKind.Component => "OnComponent",
            MGetKind.Sprite => "OnSprite",
            MGetKind.Object => "OnObject",
            MGetKind.Vector2 => "OnVector2",
            MGetKind.Vector3 => "OnVector3",
            MGetKind.Color => "OnColor",
            _ => null
        };

        // MWC — the single event property matching the picked member, or null when nothing is picked.
        private static SerializedProperty ActiveEventProperty(SerializedProperty property)
        {
            var kindProp = property.FindPropertyRelative("valueKind");
            if (kindProp == null) return null;

            string name = EventPropertyName((MGetKind)kindProp.enumValueIndex);
            return name == null ? null : property.FindPropertyRelative(name);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = RowsHeight(property);

            var eventProp = ActiveEventProperty(property);
            if (eventProp != null) height += EditorGUI.GetPropertyHeight(eventProp, true) + PAD;

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            int row = DrawRows(position, property);

            // MWC — only the event that can carry the member's value is drawn.
            var eventProp = ActiveEventProperty(property);
            if (eventProp == null) return;

            var kind = (MGetKind)property.FindPropertyRelative("valueKind").enumValueIndex;
            float y = position.y + row * (ROW_H + PAD);
            float h = EditorGUI.GetPropertyHeight(eventProp, true);

            EditorGUI.PropertyField(new Rect(position.x, y, position.width, h), eventProp,
                                    new GUIContent($"On {kind}"), true);
        }
    }

#endif
}
