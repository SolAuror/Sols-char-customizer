using UnityEngine;


#if UNITY_EDITOR
using UnityEditor;
using System.Reflection;
#endif

namespace MalbersAnimations
{
    /// <summary>
    /// A serializable container pairing a ScriptableObject ID with any Unity Object reference.
    /// Drawn inline in the Inspector: [FieldLabel] [ID──40%] [Object──60%]
    /// Fields tint red when null.
    /// Works inside MonoBehaviours, ScriptableObjects, nested classes, and arrays/lists.
    /// </summary>
    [System.Serializable]
    public class IDPair<T1, T2>
    {
        /// <summary>
        /// The ID field is meant to hold a reference to a ScriptableObject that implements the IDs interface.
        /// </summary>
        public T1 ID;
        /// <summary>
        /// The Value field can hold a reference to any Unity Object, such as a GameObject, Component, or ScriptableObject.
        /// </summary>
        public T2 Value;

        public IDPair(T1 iD, T2 val)
        {
            this.ID = iD;
            this.Value = val;
        }
    }

    // MWC: Moved outside #if UNITY_EDITOR so it can be applied to fields in runtime scripts.
    public class IDPairRatioAttribute : PropertyAttribute
    {
        public readonly float IDRatio;
        public readonly float ObjRatio;
        public IDPairRatioAttribute(float idRatio, float objRatio)
        {
            IDRatio = idRatio;
            ObjRatio = objRatio;
        }
    }

    // ── Editor-only drawer ────────────────────────────────────────────────────────
#if UNITY_EDITOR

    [CustomPropertyDrawer(typeof(IDPair<,>))]
    public class IDPairDrawer : PropertyDrawer
    {
        // ── Layout constants ──────────────────────────────────────────────────
        private float ID_RATIO = 0.50f;
        private float OBJ_RATIO = 0.50f;
        private readonly float FIELD_SPACING = 0f;
        private readonly float ID_LABEL_WIDTH = 30f;
        private readonly float ID_OBJECT_WIDTH = 30f;
        private readonly float ID_SPACING = 20f;
        private static readonly GUIContent ID_LABEL = new("ID");
        // private static readonly GUIContent ID_Value = new("Val");

        // ── Height ────────────────────────────────────────────────────────────
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUIUtility.singleLineHeight;

        // ── Draw ──────────────────────────────────────────────────────────────
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            // When inside an array or list the label is "Element 0", "Element 1", etc.
            // which wastes horizontal space — suppress it and use the full row width.
            bool isArrayElement = property.propertyPath.Contains(".Array.data[");

            Rect contentRect = isArrayElement
                ? position
                : EditorGUI.PrefixLabel(
                    position,
                    GUIUtility.GetControlID(FocusType.Passive),
                    label);



            // MWC: Read IDPairRatioAttribute via fieldInfo — the type drawer never sets `attribute`,
            //      so the old `attribute is IDPairRatioAttribute` check was always false.
            var ratioAttr = fieldInfo?.GetCustomAttribute<IDPairRatioAttribute>();
            if (ratioAttr != null)
            {
                ID_RATIO = ratioAttr.IDRatio;
                OBJ_RATIO = ratioAttr.ObjRatio;
            }

            // Disable indent inside our layout so sub-fields don't shift.
            int savedIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            var height = EditorGUIUtility.singleLineHeight;


            // ── Sub-properties ────────────────────────────────────────────
            SerializedProperty idProp = property.FindPropertyRelative("ID");
            SerializedProperty objProp = property.FindPropertyRelative("Value");

            // ── Rects ─────────────────────────────────────────────────────
            bool isBool = objProp.propertyType == SerializedPropertyType.Boolean;
            bool isPrimitive = objProp.propertyType is SerializedPropertyType.Integer
                                                     or SerializedPropertyType.Float;

            float totalW = contentRect.width;
            float y = contentRect.y;
            float h = height;

            const float TOGGLE_W = 16f;

            float idRatio = isPrimitive ? 0.70f : ID_RATIO;
            float idW = isBool ? totalW - TOGGLE_W - FIELD_SPACING
                                   : totalW * idRatio - FIELD_SPACING;

            Rect idRect = new(contentRect.x, y, idW, h);
            Rect objRect = isBool
                ? new(contentRect.xMax - TOGGLE_W + 2, y, TOGGLE_W, h)
                : new(contentRect.x + totalW * idRatio + ID_SPACING, y,
                      totalW * (isPrimitive ? 0.30f : OBJ_RATIO) - ID_SPACING, h);

            // ── ID field ──────────────────────────────────────────────────
            EditorGUIUtility.labelWidth = ID_LABEL_WIDTH;
            EditorGUI.PropertyField(idRect, idProp, isArrayElement ? ID_LABEL : GUIContent.none);

            // ── Value field ───────────────────────────────────────────────
            EditorGUIUtility.labelWidth = ID_OBJECT_WIDTH;
            EditorGUI.PropertyField(objRect, objProp, GUIContent.none);

            EditorGUI.EndProperty();
        }
    }
#endif
}