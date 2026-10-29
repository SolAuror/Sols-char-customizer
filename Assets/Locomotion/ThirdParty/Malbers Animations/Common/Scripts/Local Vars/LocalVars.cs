// Last Change: 9/17/2026 5:33pm
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Events;


#if UNITY_EDITOR
using UnityEditor;
#endif
namespace MalbersAnimations
{
    [System.Serializable]
    public class LocalVars
    {
        public List<LocalVar> variables = new();
        /// <summary>Runtime lookup of the local variables by name, built from the serialized list</summary>
        public Dictionary<string, object> vars { get; set; }
        private (string name, int index) PinVar;
        private string _ownerName = "Local Variables";

        private readonly Dictionary<string, Action<string>> _varListeners = new();

        /// <summary> Subscribe a callback to fire whenever the variable with the given name changes. </summary>
        public void Subscribe(string varName, Action<string> callback)
        {
            if (!_varListeners.ContainsKey(varName))
                _varListeners[varName] = null;

            _varListeners[varName] += callback;
        }

        /// <summary> Unsubscribe a previously registered callback for the given variable name. </summary>
        public void Unsubscribe(string varName, Action<string> callback)
        {
            if (_varListeners.ContainsKey(varName))
                _varListeners[varName] -= callback;
        }

        /// <summary> Remove all listeners for a specific variable. </summary>
        public void ClearListeners(string varName)
        {
            if (_varListeners.ContainsKey(varName))
                _varListeners.Remove(varName);
        }

        internal void FireChanged(string varName)
        {
            if (_varListeners.TryGetValue(varName, out var action))
                action?.Invoke(varName);
        }

        public void Initialize(string ownerName = "Local Variables")
        {
            _ownerName = ownerName;
            vars = new Dictionary<string, object>();

            foreach (var item in variables)
            {
                if (vars.ContainsKey(item.name))
                {
                    Debug.LogWarning($"[{_ownerName}] Duplicate variable name detected: <{item.name}>. Skipping. <B><color=orange>Vars are Case sensitive!!</color></B>");
                    continue;
                }
                vars.Add(item.name, item.GetValueRaw());
            }

            PinVar.name = string.Empty;
            PinVar.index = -1;
        }


        public void SetVar(LocalVar newvar)
        {
            var newValue = newvar.GetValueRaw();

            if (vars.ContainsKey(newvar.name))
            {
                if (Equals(vars[newvar.name], newValue)) return;

                vars[newvar.name] = newValue;

#if UNITY_EDITOR
                var listVar = variables.Find(v => v.name == newvar.name && v.type == newvar.type);
                listVar?.SetValue(newValue);
                listVar?.InvokeEvent(newValue);
#endif
                FireChanged(newvar.name);
            }
            else
            {
                vars.Add(newvar.name, newValue);
#if UNITY_EDITOR
                variables.Add(newvar);
                newvar.InvokeEvent(newValue);
#endif
                Debug.Log($"Variable {newvar.name} Added to the Local Vars");
                FireChanged(newvar.name);
            }
        }

        public bool SetVar<T>(string name, T value)
        {
            if (vars.ContainsKey(name))
            {
                if (Equals(vars[name], value)) return false;
                vars[name] = value;
#if UNITY_EDITOR
                var listVar = variables.Find(v => v.name == name);
                listVar?.SetValue(value);
                listVar?.InvokeEvent(value);
#endif
            }
            else
            {
                vars.Add(name, value);

#if UNITY_EDITOR
                var newVar = new LocalVar() { name = name };
                newVar.SetValue<T>(value);
                variables.Add(newVar);
                newVar.InvokeEvent(value);
#endif
            }

            FireChanged(name);
            return true;
        }

        public T GetVar<T>(string name)
        {
            if (!vars.ContainsKey(name))
            {
                Debug.LogWarning($"[{_ownerName}] does not contain the var <{name}>. <B><color=orange>Vars are Case sensitive!!</color></B>");
                return default;
            }

            Pin_Var(name);
            return vars.Get<T>(name);
        }


        public T GetVarEditor<T>(string name)
        {
            var found = variables.Find(v => v.name == name && v.CheckType(typeof(T)));

            if (found != null)
                return (T)found.GetValueRaw();
            return default;
        }

        public virtual bool HasVar(string name) => vars.ContainsKey(name);
        public virtual bool HasVar(LocalVar var) => vars.ContainsKey(var.name);

        public void Pin_Var(string name)
        {
            if (vars.ContainsKey(name))
            {
                PinVar.name = name;
                PinVar.index = variables.FindIndex(x => x.name == name);
            }
            else
            {
                Debug.LogWarning($"[{_ownerName}] does not contain the var <{name}>. <B><color=orange>Vars are Case sensitive!!</color></B>");
                PinVar.name = string.Empty;
                PinVar.index = -1;
            }
        }

        /// <summary> Set a Bool Variable to True directly   </summary>
        /// <param name="name"> Variable Name </param>
        public void Var_Set_True(string name)
        {
            Pin_Var(name);
            if (!string.IsNullOrEmpty(PinVar.name))
            {
                SetVar(name, true);
            }
        }

        public void Var_Set_False(string name)
        {
            Pin_Var(name);
            if (!string.IsNullOrEmpty(PinVar.name))
            {
                SetVar(name, false);
            }
        }

        public virtual void Pin_SetValue(int value)
        {
            if (!string.IsNullOrEmpty(PinVar.name))
            {
                if (Equals(vars[PinVar.name], value)) return;
                vars[PinVar.name] = value;
                if (PinVar.index != -1) { variables[PinVar.index].intValue = value; variables[PinVar.index].onIntChanged.Invoke(value); }
                FireChanged(PinVar.name);
            }
        }

        public virtual void Pin_SetValue(float value)
        {
            if (!string.IsNullOrEmpty(PinVar.name))
            {
                if (Equals(vars[PinVar.name], value)) return;
                vars[PinVar.name] = value;
                if (PinVar.index != -1) { variables[PinVar.index].floatValue = value; variables[PinVar.index].onFloatChanged.Invoke(value); }
                FireChanged(PinVar.name);
            }
        }

        public virtual void Pin_SetValue(bool value)
        {
            if (!string.IsNullOrEmpty(PinVar.name))
            {
                if (Equals(vars[PinVar.name], value)) return;
                vars[PinVar.name] = value;
                if (PinVar.index != -1) { variables[PinVar.index].boolValue = value; variables[PinVar.index].onBoolChanged.Invoke(value); }
                FireChanged(PinVar.name);
            }
        }

        public virtual void Pin_SetValue(string value)
        {
            if (!string.IsNullOrEmpty(PinVar.name))
            {
                if (Equals(vars[PinVar.name], value)) return;
                vars[PinVar.name] = value;
                if (PinVar.index != -1) { variables[PinVar.index].stringValue = value; variables[PinVar.index].onStringChanged.Invoke(value); }
                FireChanged(PinVar.name);
            }
        }

        public virtual void Pin_SetValue(Vector2 value)
        {
            if (!string.IsNullOrEmpty(PinVar.name))
            {
                if (Equals(vars[PinVar.name], value)) return;
                vars[PinVar.name] = value;
                if (PinVar.index != -1) { variables[PinVar.index].vector2Value = value; variables[PinVar.index].onVector2Changed.Invoke(value); }
                FireChanged(PinVar.name);
            }
        }

        public virtual void Pin_SetValue(Vector3 value)
        {
            if (!string.IsNullOrEmpty(PinVar.name))
            {
                if (Equals(vars[PinVar.name], value)) return;
                vars[PinVar.name] = value;
                if (PinVar.index != -1) { variables[PinVar.index].vector3Value = value; variables[PinVar.index].onVector3Changed.Invoke(value); }
                FireChanged(PinVar.name);
            }
        }

        public virtual void Pin_SetValue(GameObject value)
        {
            if (!string.IsNullOrEmpty(PinVar.name))
            {
                if (Equals(vars[PinVar.name], value)) return;
                vars[PinVar.name] = value;
                if (PinVar.index != -1) { variables[PinVar.index].gameObjectValue = value; variables[PinVar.index].onGameObjectChanged.Invoke(value); }
                FireChanged(PinVar.name);
            }
        }

        public virtual void Pin_SetValue(Transform value)
        {
            if (!string.IsNullOrEmpty(PinVar.name))
            {
                if (Equals(vars[PinVar.name], value)) return;
                vars[PinVar.name] = value;
                if (PinVar.index != -1) { variables[PinVar.index].transformValue = value; variables[PinVar.index].onTransformChanged.Invoke(value); }
                FireChanged(PinVar.name);
            }
        }

        public virtual void Pin_SetValue(Material value)
        {
            if (!string.IsNullOrEmpty(PinVar.name))
            {
                if (Equals(vars[PinVar.name], value)) return;
                vars[PinVar.name] = value;
                if (PinVar.index != -1) { variables[PinVar.index].materialValue = value; variables[PinVar.index].onObjectChanged.Invoke(value); }
                FireChanged(PinVar.name);
            }
        }

        public virtual void Pin_SetValue(Object value)
        {
            if (!string.IsNullOrEmpty(PinVar.name))
            {
                if (Equals(vars[PinVar.name], value)) return;
                vars[PinVar.name] = value;
                if (PinVar.index != -1) { variables[PinVar.index].objectValue = value; variables[PinVar.index].onObjectChanged.Invoke(value); }
                FireChanged(PinVar.name);
            }
        }

        public bool Compare(LocalVar value, ComparerNumber compare = ComparerNumber.Equal)
        {
            switch (value.type)
            {
                case LocalVar.VarType.Int:
                    var INT = GetVar<int>(value.name);
                    return value.intValue.MCompare(INT, compare);
                case LocalVar.VarType.Float:
                    var FLOAT = GetVar<float>(value.name);
                    return value.floatValue.MCompare(FLOAT, compare);
                case LocalVar.VarType.Bool:
                    var BOOL = GetVar<bool>(value.name);
                    return value.boolValue == BOOL;
                case LocalVar.VarType.String:
                    var STRING = GetVar<string>(value.name);
                    return value.stringValue == STRING;
                case LocalVar.VarType.Vector3:
                    var VECTOR3 = GetVar<Vector3>(value.name);
                    return value.vector3Value == VECTOR3;
                case LocalVar.VarType.Vector2:
                    var VECTOR2 = GetVar<Vector2>(value.name);
                    return value.vector2Value == VECTOR2;
                case LocalVar.VarType.GameObject:
                    var GO = GetVar<GameObject>(value.name);
                    return value.gameObjectValue == GO;
                case LocalVar.VarType.Transform:
                    var trans = GetVar<Transform>(value.name);
                    return trans == value.transformValue;
                case LocalVar.VarType.Material:
                    var MAT = GetVar<Material>(value.name);
                    return value.materialValue == MAT;
                case LocalVar.VarType.UnityObject:
                    var UOBJ = GetVar<Object>(value.name);
                    return value.objectValue == UOBJ;
                case LocalVar.VarType.IntVar:
                    var IntVar = GetVar<IntVar>(value.name);
                    return value.intValue.MCompare(IntVar, compare);
                case LocalVar.VarType.FloatVar:
                    var FloatVar = GetVar<FloatVar>(value.name);
                    return value.floatValue.MCompare(FloatVar, compare);
                case LocalVar.VarType.BoolVar:
                    var BoolVar = GetVar<BoolVar>(value.name);
                    return value.boolValue == BoolVar.Value;
                case LocalVar.VarType.StringVar:
                    var StringVar = GetVar<StringVar>(value.name);
                    return value.stringValue == StringVar.Value;
                default:
                    return false;
            }
        }
    }


    [System.Serializable]
    public class LocalVar : ILocalVar
    {
        public string name;
        public VarType type;

        public int intValue;
        public float floatValue;
        public bool boolValue;
        public string stringValue;
        public Vector3 vector3Value;
        public Vector2 vector2Value;
        public GameObject gameObjectValue;
        public Transform transformValue;
        public Material materialValue;
        public Object objectValue;

        public IntVar intVar;
        public FloatVar floatVar;
        public BoolVar boolVar;
        public StringVar stringVar;

        public IntEvent onIntChanged = new();
        public FloatEvent onFloatChanged = new();
        public BoolEvent onBoolChanged = new();
        public StringEvent onStringChanged = new();
        public Vector3Event onVector3Changed = new();
        public Vector2Event onVector2Changed = new();
        public GameObjectEvent onGameObjectChanged = new();
        public TransformEvent onTransformChanged = new();
        public UnityObjectEvent onObjectChanged = new();

        public string Name => name;

        /// <summary> Fires the typed UnityEvent matching this var's type with the given value. </summary>
        public void InvokeEvent(object value)
        {
            switch (type)
            {
                case VarType.Int: onIntChanged.Invoke((int)value); break;
                case VarType.Float: onFloatChanged.Invoke((float)value); break;
                case VarType.Bool: onBoolChanged.Invoke((bool)value); break;
                case VarType.String: onStringChanged.Invoke((string)value); break;
                case VarType.Vector3: onVector3Changed.Invoke((Vector3)value); break;
                case VarType.Vector2: onVector2Changed.Invoke((Vector2)value); break;
                case VarType.GameObject: onGameObjectChanged.Invoke((GameObject)value); break;
                case VarType.Transform: onTransformChanged.Invoke((Transform)value); break;
                case VarType.Material: onObjectChanged.Invoke((Material)value); break;
                case VarType.UnityObject: onObjectChanged.Invoke((Object)value); break;
                case VarType.IntVar: onIntChanged.Invoke(((IntVar)value).Value); break;
                case VarType.FloatVar: onFloatChanged.Invoke(((FloatVar)value).Value); break;
                case VarType.BoolVar: onBoolChanged.Invoke(((BoolVar)value).Value); break;
                case VarType.StringVar: onStringChanged.Invoke(((StringVar)value).Value); break;
            }
        }

        public object GetValueRaw()
        {
            return type switch
            {
                VarType.Int => intValue,
                VarType.Float => floatValue,
                VarType.Bool => boolValue,
                VarType.String => stringValue,
                VarType.Vector3 => vector3Value,
                VarType.Vector2 => vector2Value,
                VarType.GameObject => gameObjectValue,
                VarType.Transform => transformValue,
                VarType.Material => materialValue,
                VarType.UnityObject => objectValue,
                VarType.IntVar => intVar,
                VarType.FloatVar => floatVar,
                VarType.BoolVar => boolVar,
                VarType.StringVar => stringVar,
                _ => null,
            };
        }

        public void SetValue(object value)
        {
            switch (type)
            {
                case VarType.Int: intValue = (int)value; break;
                case VarType.Float: floatValue = (float)value; break;
                case VarType.Bool: boolValue = (bool)value; break;
                case VarType.String: stringValue = (string)value; break;
                case VarType.Vector3: vector3Value = (Vector3)value; break;
                case VarType.Vector2: vector2Value = (Vector2)value; break;
                case VarType.GameObject: gameObjectValue = (GameObject)value; break;
                case VarType.Transform: transformValue = (Transform)value; break;
                case VarType.Material: materialValue = (Material)value; break;
                case VarType.UnityObject: objectValue = (Object)value; break;
                case VarType.IntVar: intVar.Value = (int)value; break;
                case VarType.FloatVar: floatVar.Value = (float)value; break;
                case VarType.BoolVar: boolVar.Value = (bool)value; break;
                case VarType.StringVar: stringVar.Value = (string)value; break;
                default:
                    break;
            }
        }

        public bool CheckType(Type varType)
        {
            return type switch
            {
                VarType.Int => varType == typeof(int),
                VarType.Float => varType == typeof(float),
                VarType.Bool => varType == typeof(bool),
                VarType.String => varType == typeof(string),
                VarType.Vector3 => varType == typeof(Vector3),
                VarType.Vector2 => varType == typeof(Vector2),
                VarType.GameObject => varType == typeof(GameObject),
                VarType.Transform => varType == typeof(Transform),
                VarType.Material => varType == typeof(Material),
                VarType.UnityObject => varType == typeof(Object),
                VarType.IntVar => varType == typeof(IntVar),
                VarType.FloatVar => varType == typeof(FloatVar),
                VarType.BoolVar => varType == typeof(BoolVar),
                VarType.StringVar => varType == typeof(StringVar),
                _ => false,
            };
        }

        public void SetValue(VarType type, object value)
        {
            this.type = type;
            SetValue(value);
        }

        public void SetValue<T>(object value)
        {
            var type = typeof(T);

            if (type == typeof(int)) { SetValue(VarType.Int, (int)value); }
            else if (type == typeof(float)) { SetValue(VarType.Float, (float)value); }
            else if (type == typeof(bool)) { SetValue(VarType.Bool, (bool)value); }
            else if (type == typeof(string)) { SetValue(VarType.String, (string)value); }


            else if (type == typeof(Vector3)) { SetValue(VarType.Vector3, (Vector3)value); }
            else if (type == typeof(Vector2)) { SetValue(VarType.Vector2, (Vector2)value); }
            else if (type == typeof(GameObject)) { SetValue(VarType.GameObject, (GameObject)value); }
            else if (type == typeof(Transform)) { SetValue(VarType.Transform, (Transform)value); }
            else if (type == typeof(Material)) { SetValue(VarType.Material, (Material)value); }
            else if (type == typeof(Object)) { SetValue(VarType.UnityObject, (Object)value); }
        }

        public enum VarType { Int, Float, Bool, String, Vector3, Vector2, GameObject, Transform, Material, UnityObject, IntVar, FloatVar, BoolVar, StringVar }
    }

    #region EDITOR STUFF

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(LocalVar))]
    public class LocalVarAttribute : PropertyDrawer
    {
        private static string GetEventPropertyName(LocalVar.VarType type) => type switch
        {
            LocalVar.VarType.Int or LocalVar.VarType.IntVar => "onIntChanged",
            LocalVar.VarType.Float or LocalVar.VarType.FloatVar => "onFloatChanged",
            LocalVar.VarType.Bool or LocalVar.VarType.BoolVar => "onBoolChanged",
            LocalVar.VarType.String or LocalVar.VarType.StringVar => "onStringChanged",
            LocalVar.VarType.Vector3 => "onVector3Changed",
            LocalVar.VarType.Vector2 => "onVector2Changed",
            LocalVar.VarType.GameObject => "onGameObjectChanged",
            LocalVar.VarType.Transform => "onTransformChanged",
            LocalVar.VarType.Material or LocalVar.VarType.UnityObject => "onObjectChanged",
            _ => null
        };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;

            if (property.isExpanded)
            {
                var varType = (LocalVar.VarType)property.FindPropertyRelative("type").intValue;
                var eventPropName = GetEventPropertyName(varType);
                if (eventPropName != null)
                {
                    var eventProp = property.FindPropertyRelative(eventPropName);
                    if (eventProp != null)
                        height += EditorGUI.GetPropertyHeight(eventProp) + EditorGUIUtility.standardVerticalSpacing;
                }
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var nameProp = property.FindPropertyRelative("name");
            var type = property.FindPropertyRelative("type");

            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            var height = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;

            // Foldout arrow
            var foldoutRect = new Rect(position.x, position.y, 14, height);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, GUIContent.none, true);

            float offsetX = position.x + 2;
            float remaining = position.width - 2;

            var NameRect = new Rect(offsetX, position.y, remaining / 3, height);

            var TypeRect = new Rect(NameRect.xMax + 5, position.y, remaining / 3, height);

            var ValueRect = new Rect(TypeRect.xMax + 5, position.y, remaining - NameRect.width - TypeRect.width - 10, height);

            EditorGUIUtility.labelWidth = 25;
            EditorGUI.PropertyField(NameRect, nameProp, new GUIContent("Var", "Variable Name-Key used for the Dictionary"));
            EditorGUIUtility.labelWidth = 0;
            EditorGUI.PropertyField(TypeRect, type, GUIContent.none);

            switch ((LocalVar.VarType)type.intValue)
            {
                case LocalVar.VarType.Int: EditorGUI.PropertyField(ValueRect, property.FindPropertyRelative("intValue"), GUIContent.none); break;
                case LocalVar.VarType.Float: EditorGUI.PropertyField(ValueRect, property.FindPropertyRelative("floatValue"), GUIContent.none); break;
                case LocalVar.VarType.Bool: EditorGUI.PropertyField(ValueRect, property.FindPropertyRelative("boolValue"), GUIContent.none); break;
                case LocalVar.VarType.String: EditorGUI.PropertyField(ValueRect, property.FindPropertyRelative("stringValue"), GUIContent.none); break;
                case LocalVar.VarType.Vector3: EditorGUI.PropertyField(ValueRect, property.FindPropertyRelative("vector3Value"), GUIContent.none); break;
                case LocalVar.VarType.Vector2: EditorGUI.PropertyField(ValueRect, property.FindPropertyRelative("vector2Value"), GUIContent.none); break;
                case LocalVar.VarType.GameObject: EditorGUI.PropertyField(ValueRect, property.FindPropertyRelative("gameObjectValue"), GUIContent.none); break;
                case LocalVar.VarType.Transform: EditorGUI.PropertyField(ValueRect, property.FindPropertyRelative("transformValue"), GUIContent.none); break;
                case LocalVar.VarType.Material: EditorGUI.PropertyField(ValueRect, property.FindPropertyRelative("materialValue"), GUIContent.none); break;
                case LocalVar.VarType.UnityObject: EditorGUI.PropertyField(ValueRect, property.FindPropertyRelative("objectValue"), GUIContent.none); break;
                case LocalVar.VarType.IntVar: EditorGUI.PropertyField(ValueRect, property.FindPropertyRelative("intVar"), GUIContent.none); break;
                case LocalVar.VarType.FloatVar: EditorGUI.PropertyField(ValueRect, property.FindPropertyRelative("floatVar"), GUIContent.none); break;
                case LocalVar.VarType.BoolVar: EditorGUI.PropertyField(ValueRect, property.FindPropertyRelative("boolVar"), GUIContent.none); break;
                case LocalVar.VarType.StringVar: EditorGUI.PropertyField(ValueRect, property.FindPropertyRelative("stringVar"), GUIContent.none); break;
            }

            // Event foldout
            if (property.isExpanded)
            {
                var varType = (LocalVar.VarType)type.intValue;
                var eventPropName = GetEventPropertyName(varType);
                if (eventPropName != null)
                {
                    var eventProp = property.FindPropertyRelative(eventPropName);
                    if (eventProp != null)
                    {
                        var eventRect = new Rect(position.x, position.y + height + spacing, position.width, EditorGUI.GetPropertyHeight(eventProp));
                        EditorGUI.PropertyField(eventRect, eventProp);
                    }
                }
            }

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
#endif
    #endregion
}