// Last Change: 9/17/2026 6:05pm
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

//AL Serialized Properties and Serialized Reference Extensions and new Methods are located here
namespace MalbersAnimations
{
    public static class MSerializedTools
    {
        public static IEnumerable<SerializedProperty> FindChildrenProperties(this SerializedProperty parent, int depth = 1)
        {
            var depthOfParent = parent.depth;
            var enumerator = parent.GetEnumerator();

            while (enumerator.MoveNext())
            {
                if (enumerator.Current is not SerializedProperty childProperty) continue;
                if (childProperty.depth > depthOfParent + depth) continue;

                yield return childProperty.Copy();
            }
        }

        private const string ArrayPropertySubstring = ".Array.data[";

        public static object SetManagedReference(this SerializedProperty property, System.Type type)
        {
            object obj = (type != null) ? System.Activator.CreateInstance(type) : null;
            property.managedReferenceValue = obj;
            return obj;
        }

        /// <summary>True when the property lives anywhere under an array (it may not be an element itself)</summary>
        public static bool IsArray(this SerializedProperty property)
        {
            return property.propertyPath.Contains(ArrayPropertySubstring);
        }

        /// <summary>True only when the property IS an array element (its path ends with .Array.data[i])</summary>
        public static bool IsArrayElement(this SerializedProperty property)
        {
            var path = property.propertyPath;
            var start = path.LastIndexOf(ArrayPropertySubstring);

            //MWC — the last [i] must close the path, else this is a field INSIDE an element, not the element
            return start != -1 && path.EndsWith("]")
                && path.IndexOf('.', start + ArrayPropertySubstring.Length) == -1;
        }

        /// <summary>Array property that directly owns the given array element (null when it is not one)</summary>
        public static SerializedProperty GetArrayPropertyFromArrayElement(SerializedProperty property)
        {
            var path = property.propertyPath;

            //MWC — LastIndexOf, NOT IndexOf: on a nested path (a.Array.data[0].b.Array.data[1]) the first
            //occurrence resolves to the OUTER array, so elements were appended to the wrong list
            var startIndexArrayPropertyPath = path.LastIndexOf(ArrayPropertySubstring);

            if (startIndexArrayPropertyPath == -1) return null;

            var propertyPath = path.Remove(startIndexArrayPropertyPath);
            return property.serializedObject.FindProperty(propertyPath);
        }


        public static IEnumerable<SerializedProperty> Children(this SerializedProperty serializedProperty)
        {
            SerializedProperty currentProperty = serializedProperty.Copy();
            SerializedProperty nextSiblingProperty = serializedProperty.Copy();
            {
                nextSiblingProperty.Next(false);
            }

            if (currentProperty.Next(true))
            {
                do
                {
                    if (SerializedProperty.EqualContents(currentProperty, nextSiblingProperty))
                        break;

                    yield return currentProperty;
                }
                while (currentProperty.Next(false));
            }
        }

        public static void CreateAssetInternal(SerializedProperty property, Type type)
        {
            property.objectReferenceValue = ScriptableObject.CreateInstance(type);
            property.serializedObject.ApplyModifiedProperties();
        }


        /// <summary>  Returns the Type of a serialized property </summary>
        public static System.Type GetPropertyType(SerializedProperty property)
        {
            System.Type parentType = property.serializedObject.targetObject.GetType();
            var fi = parentType.GetFieldViaPath(property.propertyPath);
            return fi != null ? fi.FieldType : null;
        }

        public static Type GetPropertyType2(SerializedProperty property)
        {
            object obj = GetTargetObjectOfProperty(property);
            Type objType = obj.GetType();

            return objType;
        }
        public static bool IsInsideArrayElement(this SerializedProperty property)
        {
            return property.propertyPath.Contains("Array");
        }

        public static System.Type GetType(string typeName)
        {
            int splitIndex = typeName.IndexOf(' ');
            var assembly = Assembly.Load(typeName[..splitIndex]);
            return assembly.GetType(typeName[(splitIndex + 1)..]);
        }

        /// <summary>  Returns the object value of a serialized property </summary>
        public static object GetValue(this SerializedProperty property)
        {
            System.Type parentType = property.serializedObject.targetObject.GetType();
            System.Reflection.FieldInfo fi = parentType.GetField(property.propertyPath);
            return fi.GetValue(property.serializedObject.targetObject);
        }

        /// <summary>  Set the object value of a serialized property </summary>
        public static void SetValue(this SerializedProperty property, object value)
        {
            System.Type parentType = property.serializedObject.targetObject.GetType();
            System.Reflection.FieldInfo fi = parentType.GetField(property.propertyPath);//this FieldInfo contains the type.
            fi.SetValue(property.serializedObject.targetObject, value);
        }

        public static void SetValue(this SerializedProperty property, Type parentType, object value)
        {
            //System.Type parentType = property.serializedObject.targetObject.GetType();
            System.Reflection.FieldInfo fi = parentType.GetField(property.propertyPath);//this FieldInfo contains the type.
            fi.SetValue(property.serializedObject.targetObject, value);
        }


        /// <summary> Returns attributes of type <typeparamref name="TAttribute"/> on <paramref name="serializedProperty"/>. </summary>
        public static TAttribute[] GetAttributes<TAttribute>(this SerializedProperty serializedProperty, bool inherit)
            where TAttribute : Attribute
        {
            if (serializedProperty == null)
            {
                throw new ArgumentNullException(nameof(serializedProperty));
            }

            var targetObjectType = serializedProperty.serializedObject.targetObject.GetType();

            if (targetObjectType == null)
            {
                throw new ArgumentException($"Could not find the {nameof(targetObjectType)} of {nameof(serializedProperty)}");
            }

            foreach (var pathSegment in serializedProperty.propertyPath.Split('.'))
            {
                var fieldInfo = targetObjectType.GetField(pathSegment, AllBindingFlags);
                if (fieldInfo != null)
                {
                    return (TAttribute[])fieldInfo.GetCustomAttributes<TAttribute>(inherit);
                }

                var propertyInfo = targetObjectType.GetProperty(pathSegment, AllBindingFlags);
                if (propertyInfo != null)
                {
                    return (TAttribute[])propertyInfo.GetCustomAttributes<TAttribute>(inherit);
                }
            }

            throw new ArgumentException($"Could not find the field or property of {nameof(serializedProperty)}");
        }
        private const BindingFlags AllBindingFlags = (BindingFlags)(-1);



        /// <summary> Gets the object the property represents.</summary>
        /// <param name="property"></param>
        /// <returns></returns>
        public static object GetTargetObjectOfProperty(SerializedProperty property)
        {
            if (property == null) return null;


            string path = property.propertyPath.Replace(".Array.data[", "[");
            object obj = property.serializedObject.targetObject;
            string[] elements = path.Split('.');

            foreach (var element in elements)
            {
                if (element.Contains("["))
                {
                    string elementName = element.Substring(0, element.IndexOf("["));
                    int index = Convert.ToInt32(element.Substring(element.IndexOf("[")).Replace("[", "").Replace("]", ""));
                    obj = GetValue_Imp(obj, elementName, index);
                }
                else
                {
                    obj = GetValue_Imp(obj, element);
                }
            }

            return obj;
        }

        /// <summary> Gets the object that the property is a member of  </summary>
        public static object GetTargetObjectWithProperty(SerializedProperty property)
        {
            string path = property.propertyPath.Replace(".Array.data[", "[");
            object obj = property.serializedObject.targetObject;
            string[] elements = path.Split('.');

            for (int i = 0; i < elements.Length - 1; i++)
            {
                string element = elements[i];
                if (element.Contains("["))
                {
                    string elementName = element.Substring(0, element.IndexOf("["));
                    int index = Convert.ToInt32(element.Substring(element.IndexOf("[")).Replace("[", "").Replace("]", ""));
                    obj = GetValue_Imp(obj, elementName, index);
                }
                else
                {
                    obj = GetValue_Imp(obj, element);
                }
            }

            return obj;
        }


        private static object GetValue_Imp(object source, string name)
        {
            if (source == null)
            {
                return null;
            }

            Type type = source.GetType();

            while (type != null)
            {
                FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    return field.GetValue(source);
                }

                PropertyInfo property = type.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (property != null)
                {
                    return property.GetValue(source, null);
                }

                type = type.BaseType;
            }

            return null;
        }
        private static object GetValue_Imp(object source, string name, int index)
        {
            IEnumerable enumerable = GetValue_Imp(source, name) as IEnumerable;
            if (enumerable == null)
            {
                return null;
            }

            IEnumerator enumerator = enumerable.GetEnumerator();
            for (int i = 0; i <= index; i++)
            {
                if (!enumerator.MoveNext())
                {
                    return null;
                }
            }

            return enumerator.Current;
        }


        //public static T GetPropertyAttribute<T>(this SerializedProperty prop, bool inherit) where T : PropertyAttribute
        //{
        //    if (prop == null) return null;
        //    Type t = prop.serializedObject.targetObject.GetType();

        //    FieldInfo f = null;
        //    PropertyInfo p = null;

        //    foreach (var name in prop.propertyPath.Split('.'))
        //    {
        //        f = t.GetField(name, (BindingFlags)(-1));

        //        if (f == null)
        //        {
        //            p = t.GetProperty(name, (BindingFlags)(-1));
        //            if (p == null)
        //            {
        //                return null;
        //            }

        //            t = p.PropertyType;
        //        }
        //        t = f.FieldType;
        //    }

        //    T[] attributes;

        //    if (f != null)
        //    {
        //        attributes = f.GetCustomAttribute(typeof(T), inherit) as T[];
        //    }
        //    else if (p != null)
        //    {
        //        attributes = p.GetCustomAttribute(typeof(T), inherit) as T[];
        //    }
        //    else
        //    {
        //        return null;
        //    }

        //    return attributes.Length > 0 ? attributes[0] : null;
        //}

    }

    public static class TypeUtility
    {
        public const string k_NullDisplayName = "[Null]";

        public static AddTypeMenuAttribute GetAttribute(Type type)
        {
            return Attribute.GetCustomAttribute(type, typeof(AddTypeMenuAttribute)) as AddTypeMenuAttribute;
        }

        public static string[] GetSplittedTypePath(Type type)
        {
            AddTypeMenuAttribute typeMenu = GetAttribute(type);
            if (typeMenu != null)
            {
                return typeMenu.GetSplittedMenuName();
            }
            else
            {
                int splitIndex = type.FullName.LastIndexOf('.');
                if (splitIndex >= 0)
                {
                    return new string[] { type.FullName[..splitIndex], type.FullName[(splitIndex + 1)..] };
                }
                else
                {
                    return new string[] { type.Name };
                }
            }
        }

        public static IEnumerable<Type> OrderByType(this IEnumerable<Type> source)
        {
            return source.OrderBy(type =>
            {
                if (type == null)
                {
                    return -999;
                }
                return GetAttribute(type)?.Order ?? 0;
            }).ThenBy(type =>
            {
                if (type == null)
                {
                    return null;
                }
                return GetAttribute(type)?.MenuName ?? type.Name;
            });
        }
    }

    public class AdvancedTypePopupItem : AdvancedDropdownItem
    {
        public Type Type { get; }

        public AdvancedTypePopupItem(Type type, string name) : base(name) => Type = type;
    }

    public readonly struct TypePopupCache
    {
        const int k_MaxTypePopupLineCount = 10;

        static readonly Type k_UnityObjectType = typeof(UnityEngine.Object);

        public AdvancedTypePopup TypePopup { get; }
        public AdvancedDropdownState State { get; }
        public TypePopupCache(AdvancedTypePopup typePopup, AdvancedDropdownState state)
        {
            TypePopup = typePopup;
            State = state;
        }

        public static GUIContent GetTypeName(SerializedProperty property, Dictionary<string, GUIContent> m_TypeNameCaches)
        {
            // Cache this string.
            string managedReferenceFullTypeName = property.managedReferenceFullTypename;

            if (string.IsNullOrEmpty(managedReferenceFullTypeName))
            {
                return new GUIContent("[Null]");
            }
            if (m_TypeNameCaches.TryGetValue(managedReferenceFullTypeName, out GUIContent cachedTypeName))
            {
                return cachedTypeName;
            }

            Type type = MSerializedTools.GetType(managedReferenceFullTypeName);
            string typeName = null;

            AddTypeMenuAttribute typeMenu = TypeUtility.GetAttribute(type);
            if (typeMenu != null)
            {
                typeName = typeMenu.GetTypeNameWithoutPath();
                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    typeName = ObjectNames.NicifyVariableName(typeName);
                }
            }

            if (string.IsNullOrWhiteSpace(typeName))
            {
                typeName = ObjectNames.NicifyVariableName(type.Name);
            }

            GUIContent result = new(typeName);
            m_TypeNameCaches.Add(managedReferenceFullTypeName, result);
            return result;
        }
    }




    /// <summary> A type popup with a fuzzy finder. </summary>
    public class AdvancedTypePopup : AdvancedDropdown
    {
        const int kMaxNamespaceNestCount = 16;

        public static void AddTo(AdvancedDropdownItem root, IEnumerable<Type> types)
        {
            int itemCount = 0;

            // Add null item.
            var nullItem = new AdvancedTypePopupItem(null, TypeUtility.k_NullDisplayName)
            {
                id = itemCount++
            };
            root.AddChild(nullItem);

            Type[] typeArray = types.OrderByType().ToArray();

            // Single namespace if the root has one namespace and the nest is unbranched.
            bool isSingleNamespace = true;
            string[] namespaces = new string[kMaxNamespaceNestCount];
            foreach (Type type in typeArray)
            {
                string[] splittedTypePath = TypeUtility.GetSplittedTypePath(type);
                if (splittedTypePath.Length <= 1)
                {
                    continue;
                }
                // If they explicitly want sub category, let them do.
                if (TypeUtility.GetAttribute(type) != null)
                {
                    isSingleNamespace = false;
                    break;
                }
                for (int k = 0; (splittedTypePath.Length - 1) > k; k++)
                {
                    string ns = namespaces[k];
                    if (ns == null)
                    {
                        namespaces[k] = splittedTypePath[k];
                    }
                    else if (ns != splittedTypePath[k])
                    {
                        isSingleNamespace = false;
                        break;
                    }
                }

                if (!isSingleNamespace) break;
            }

            // Add type items.
            foreach (Type type in typeArray)
            {
                string[] splittedTypePath = TypeUtility.GetSplittedTypePath(type);
                if (splittedTypePath.Length == 0)
                {
                    continue;
                }

                AdvancedDropdownItem parent = root;

                // Add namespace items.
                if (!isSingleNamespace)
                {
                    for (int k = 0; (splittedTypePath.Length - 1) > k; k++)
                    {
                        AdvancedDropdownItem foundItem = GetItem(parent, splittedTypePath[k]);
                        if (foundItem != null)
                        {
                            parent = foundItem;
                        }
                        else
                        {
                            var newItem = new AdvancedDropdownItem(splittedTypePath[k])
                            {
                                id = itemCount++,
                            };
                            parent.AddChild(newItem);
                            parent = newItem;
                        }
                    }
                }

                // Add type item.
                var item = new AdvancedTypePopupItem(type, ObjectNames.NicifyVariableName(splittedTypePath[splittedTypePath.Length - 1]))
                {
                    id = itemCount++
                };
                parent.AddChild(item);
            }
        }

        static AdvancedDropdownItem GetItem(AdvancedDropdownItem parent, string name)
        {
            foreach (AdvancedDropdownItem item in parent.children)
            {
                if (item.name == name) return item;
            }
            return null;
        }

        static readonly float k_HeaderHeight = EditorGUIUtility.singleLineHeight * 2f;

        Type[] m_Types;

        public event Action<AdvancedTypePopupItem> OnItemSelected;

        public AdvancedTypePopup(IEnumerable<Type> types, int maxLineCount, AdvancedDropdownState state) : base(state)
        {
            SetTypes(types);
            minimumSize = new Vector2(minimumSize.x, EditorGUIUtility.singleLineHeight * maxLineCount + k_HeaderHeight);
        }

        public void SetTypes(IEnumerable<Type> types) => m_Types = types.ToArray();

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Select Type");
            AddTo(root, m_Types);
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            base.ItemSelected(item);
            if (item is AdvancedTypePopupItem typePopupItem)
            {
                OnItemSelected?.Invoke(typePopupItem);
            }
        }


    }
}
#endif
