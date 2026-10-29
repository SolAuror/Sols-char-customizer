// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor; // MWC — was unguarded: broke player builds (UnityEditor is Editor-only)
#endif

namespace MalbersAnimations.Conditions
{
    [System.Serializable]
    public struct Conditions2
    {
        [SerializeReference] public ConditionCore[] conditions;

        public bool active;

        public Conditions2(int length)
        {
            conditions = new ConditionCore[length];
            active = true;
        }

        public readonly int Count => conditions != null ? conditions.Length : 0;

        /// <summary>  Conditions can be used the list has some conditions </summary>
        public readonly bool Valid => active && conditions != null && conditions.Length > 0;

        public readonly bool Evaluate(UnityEngine.Object target) => Fold(target, null, false);

        /// <summary> Evaluate the conditions using a basic value (int, float, string, bool...)
        /// sent by a UnityEvent&lt;T&gt;. Named apart from Evaluate() so an object-typed
        /// variable holding a UnityEngine.Object cannot silently take this path. </summary>
        public readonly bool EvaluateValue(object value) => Fold(null, value, true);

        /// <summary> Evaluate the conditions against a target and a payload value at the same
        /// time, so target-based and value-based conditions can live in the same list. </summary>
        public readonly bool EvaluateValue(UnityEngine.Object target, object value) => Fold(target, value, true);

        // MWC — single fold used by every overload. Left-to-right, no operator precedence:
        // each element's OrAnd flag is the connector between the accumulated result and itself.
        // Null elements are skipped explicitly instead of being caught as NullReferenceException.
        private readonly bool Fold(UnityEngine.Object target, object value, bool useValue)
        {
            if (!Valid) return true; //by default return true

            bool result = true;
            bool hasResult = false;

            for (int i = 0; i < conditions.Length; i++)
            {
                var condition = conditions[i];

                if (condition == null)
                {
                    Debug.LogError($"[Null] Condition [{i}] not Allowed. Please Check your conditions.", target);
                    continue;
                }

                //MWC — a Condition disabled individually is ignored: it does not seed the result
                //and its AND/OR connector is not applied either.
                if (!condition.active) continue;

                try
                {
                    if (useValue && target != null) condition.SetTarget(target);

                    bool next = useValue ? condition.Evaluate(value) : condition.Evaluate(target);

                    //The first valid condition seeds the result, it has no connector
                    result = hasResult ? (condition.OrAnd ? (result || next) : (result && next)) : next;
                    hasResult = true;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Condition [{i}] [{condition.DynamicName}] threw while evaluating. Please Check your conditions.", target);
                    Debug.LogException(e, target);
                }
            }
            return result;
        }


        public static bool FindConditions2ByName(MonoBehaviour mono, string fieldName, out Conditions2 result)
        {
            result = default;

            if (mono == null || string.IsNullOrEmpty(fieldName)) return false;
            return FindInObjectByName(mono, fieldName, ref result);
        }

        // MWC — rewritten. Old version only recursed into fields whose name already matched, so
        // nested search never actually descended. It also skipped [SerializeField] private fields
        // and could walk into UnityEngine.Object references (i.e. the whole scene).
        // NOTE: Conditions2 is a struct, so the returned value is a COPY. Callers that need to
        // modify it must write it back through the FieldInfo.
        private const int MaxSearchDepth = 5;

        private static bool FindInObjectByName(object obj, string fieldName, ref Conditions2 result, int depth = 0)
        {
            if (obj == null || depth > MaxSearchDepth) return false;

            Type type = obj.GetType();
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (FieldInfo field in fields)
            {
                //Skip private fields Unity would not serialize either
                if (!field.IsPublic && !Attribute.IsDefined(field, typeof(SerializeField))) continue;

                object value = field.GetValue(obj);
                if (value == null) continue;

                bool nameMatch = string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase);

                if (nameMatch && value is Conditions2 found)
                {
                    result = found;
                    return true;
                }

                // List/Array of Conditions2
                if (nameMatch && value is IEnumerable enumerable and not string)
                {
                    foreach (var element in enumerable)
                    {
                        if (element is Conditions2 foundInList)
                        {
                            result = foundInList;
                            return true;
                        }
                    }
                }

                //Descend into nested serializable classes/structs regardless of their name.
                //Never follow UnityEngine.Object references: that would crawl the whole scene.
                if (!nameMatch && CanDescendInto(field.FieldType)
                    && FindInObjectByName(value, fieldName, ref result, depth + 1))
                    return true;
            }
            return false;
        }

        private static bool CanDescendInto(Type type)
        {
            if (type.IsPrimitive || type.IsEnum || type == typeof(string)) return false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return false;
            if (type.IsArray || typeof(IEnumerable).IsAssignableFrom(type)) return false;
            return type.IsClass || (type.IsValueType && !type.IsPrimitive);
        }


        public void Add(ConditionCore condition)
        {
            if (condition == null) return;
            if (conditions == null || conditions.Length == 0)
            {
                conditions = new ConditionCore[1];
                conditions[0] = condition;
            }
            else
            {
                var newConditions = new ConditionCore[conditions.Length + 1];
                for (int i = 0; i < conditions.Length; i++)
                {
                    newConditions[i] = conditions[i];
                }
                newConditions[^1] = condition;
                conditions = newConditions;
            }
        }

        public void Add(Conditions2 newConditions)
        {
            if (newConditions.conditions == null || newConditions.conditions.Length == 0) return;

            if (conditions == null || conditions.Length == 0)
            {
                // MWC — copy instead of aliasing the source array, otherwise both Conditions2
                // share the same array and mutating one mutates the other.
                conditions = (ConditionCore[])newConditions.conditions.Clone();
            }
            else
            {
                var combinedConditions = new ConditionCore[conditions.Length + newConditions.conditions.Length];
                for (int i = 0; i < conditions.Length; i++)
                {
                    combinedConditions[i] = conditions[i];
                }
                for (int i = 0; i < newConditions.conditions.Length; i++)
                {
                    combinedConditions[conditions.Length + i] = newConditions.conditions[i];
                }
                conditions = combinedConditions;
            }
        }

        public void Remove(int index)
        {
            if (conditions == null || conditions.Length == 0 || index < 0 || index >= conditions.Length) return;

            if (conditions.Length == 1)
            {
                conditions = null; //If there is only one condition, set it to null
            }
            else
            {
                var newConditions = new ConditionCore[conditions.Length - 1];
                for (int i = 0, j = 0; i < conditions.Length; i++)
                {
                    if (i != index)
                    {
                        newConditions[j++] = conditions[i];
                    }
                }
                conditions = newConditions;
            }
        }

        public void RemoveLast() { if (conditions != null) Remove(conditions.Length - 1); } // MWC — null guard

        public void RemoveFirst() => Remove(0);

        public void Remove(string description)
        {
            if (conditions == null || conditions.Length == 0) return;
            for (int i = 0; i < conditions.Length; i++)
            {
                if (conditions[i] == null) continue;
                if (string.Equals(conditions[i].desc, description)) // MWC — null-safe compare
                {
                    Remove(i);
                    return; // Exit after removing the first match
                }
            }
        }

        public readonly void Gizmos(Component comp)
        {
            if (conditions == null || conditions.Length == 0) return;

            for (int i = 0; i < conditions.Length; i++)
            {
                //MWC — skip gizmos of Conditions disabled individually
                if (conditions[i] != null && conditions[i].active && conditions[i].DebugCondition) conditions[i].DrawGizmos(comp);
            }
        }
    }


    /*EXTRAS CONDITIONS Combos */
    [System.Serializable]
    public struct Conditions2Int
    {
        [Tooltip("Name of the Conditions2 to evaluate use it for identification")]
        public string name;
        public Conditions2 conditions;
        public IntReference value;
    }

    //[System.Serializable]
    //public struct Conditions2Float
    //{
    //    public Conditions2 conditions;
    //    public float value;
    //}

    //[System.Serializable]
    //public struct Conditions2Bool
    //{
    //    public Conditions2 conditions;
    //    public bool value;
    //}

    //[System.Serializable]
    //public struct Conditions2String
    //{
    //    public Conditions2 conditions;
    //    public string value;
    //}


#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(Conditions2))]
    public class Conditions2Drawer : PropertyDrawer
    {
        private static readonly GUIContent s_Label = new(); // MWC — scratch label, see OnGUI

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // var indent = EditorGUI.indentLevel;
            // EditorGUI.indentLevel = 0;

            var active = property.FindPropertyRelative("active");
            EditorGUI.BeginProperty(position, label, property);

            var rect1 = new Rect(position);// rect1.width -= 12;

            var width = 65 + EditorGUI.indentLevel * 15;

            // MWC — was rect1.width - width, which only lands correctly when the property starts at x = 0
            var activeRect = new Rect(position.xMax - width, position.y, 20f, EditorGUIUtility.singleLineHeight);
            EditorGUI.PropertyField(activeRect, active, GUIContent.none);

            // MWC — never mutate the incoming label: callers reuse cached GUIContent instances and
            // " (Disabled)" used to accumulate on every repaint.
            var listLabel = s_Label;
            listLabel.text = active.boolValue ? label.text : label.text + " (Disabled)";
            listLabel.tooltip = label.tooltip;
            listLabel.image = label.image;

            using (new EditorGUI.DisabledGroupScope(!active.boolValue))
                EditorGUI.PropertyField(rect1, property.FindPropertyRelative("conditions"), listLabel, true);

            //Had to painted twice, otherwise it wont show in the place I wanted
            EditorGUI.PropertyField(activeRect, active, GUIContent.none);

            EditorGUI.EndProperty();
            // EditorGUI.indentLevel = indent;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property.FindPropertyRelative("conditions"), label);
        }
    }
#endif
}