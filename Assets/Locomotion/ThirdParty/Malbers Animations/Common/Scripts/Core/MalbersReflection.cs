// Last Change: 10/14/2026 2:45pm
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace MalbersAnimations
{
    public static class MalbersReflection
    {
        public const BindingFlags FULL_BINDING = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Assemblies currently loaded by Unity. CurrentAssemblies (6000.4+) skips assemblies being unloaded by the CoreCLR reload; AppDomain is the fallback for older editors</summary>
        public static IEnumerable<Assembly> GetLoadedAssemblies()
        {
#if UNITY_6000_4_OR_NEWER
            return UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies();
#else
            return AppDomain.CurrentDomain.GetAssemblies();
#endif
        }

        #region Field / Property / Method — UnityEngine.Object extensions (typed, with logging)

        /// <summary>Given a UnityEngine.Object and a property name, returns the PropertyInfo (type-validated).</summary>
        public static PropertyInfo GetProperty<T>(this UnityEngine.Object component, string propertyName)
        {
            Type type = component.GetType();
            var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (property == null)
            {
                Debug.LogError($"Property '{propertyName}' of type '{typeof(T)}' not found on [{type.FullName}]");
                return null;
            }
            else if (property.PropertyType != typeof(T))
            {
                Debug.LogError($"Property '{propertyName}' was found, but it does not have the type '{typeof(T)}'. '{type.FullName}'.");
                return null;
            }

            return property;
        }

        /// <summary>Given a UnityEngine.Object and a method name (no parameters), returns the MethodInfo (type-validated).</summary>
        public static MethodInfo GetMethod<T>(this UnityEngine.Object component, string methodName)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));

            Type type = component.GetType();
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (method == null)
            {
                Debug.Log($"Method ({typeof(T)})[{methodName}] not found on [{type.FullName}].", component);
                return null;
            }
            else if (method.GetParameters().Length > 0)
            {
                Debug.LogWarning($"Method ({typeof(T)})[{methodName}] needs arguments. Skip", component);
                return null;
            }
            else if (method.ReturnType != typeof(T))
            {
                Debug.Log($"Method [{methodName}] was found, but it does not have the type ({typeof(T)}). '{type.FullName}'.", component);
                return null;
            }

            return method;
        }

        /// <summary>Given a UnityEngine.Object and a field name, returns the FieldInfo (type-validated).</summary>
        public static FieldInfo GetField<T>(this UnityEngine.Object component, string fieldName)
        {
            if (component == null) throw new ArgumentNullException(nameof(component));

            Type type = component.GetType();
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (field == null)
            {
                Debug.LogError($"Variable ({typeof(T)})[{fieldName}] not found on [{type.FullName}].");
                return default;
            }
            else if (field.FieldType != typeof(T))
            {
                Debug.LogError($"Variable [{fieldName}] was found, but it does not have the type ({typeof(T)}). '{type.FullName}'.");
                return default;
            }

            return field;
        }

        /// <summary>Given a UnityEngine.Object and a property name, returns the property value.</summary>
        public static T GetPropertyValue<T>(this UnityEngine.Object component, string propertyName)
        {
            var property = component.GetProperty<T>(propertyName);
            if (property == null) return default;
            return (T)property.GetValue(component);
        }

        /// <summary>Given a UnityEngine.Object and a method name (no parameters), invokes it and returns the value.</summary>
        public static T GetMethodValue<T>(this UnityEngine.Object component, string methodName)
        {
            MethodInfo method = GetMethod<T>(component, methodName);
            if (method == null) return default;
            return (T)method.Invoke(component, null);
        }

        /// <summary>Given a UnityEngine.Object and a field name, returns the field value.</summary>
        public static T GetFieldValue<T>(this UnityEngine.Object component, string fieldName)
        {
            var field = GetField<T>(component, fieldName);
            if (field == null) return default;
            return (T)field.GetValue(component);
        }

        #endregion

        #region Field / Property / Method — object-based deep search (predicate-driven)

        /// <summary>Returns all fields on the target and its base types that match the predicate.</summary>
        public static IEnumerable<FieldInfo> GetAllFields(object target, Func<FieldInfo, bool> predicate)
        {
            if (target == null)
            {
                Debug.LogError("The target object is null. Check for missing scripts.");
                yield break;
            }

            List<Type> types = GetSelfAndBaseTypes(target);

            for (int i = types.Count - 1; i >= 0; i--)
            {
                IEnumerable<FieldInfo> fieldInfos = types[i]
                    .GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(predicate);

                foreach (var fieldInfo in fieldInfos)
                    yield return fieldInfo;
            }
        }

        /// <summary>Returns all properties on the target and its base types that match the predicate.</summary>
        public static IEnumerable<PropertyInfo> GetAllProperties(object target, Func<PropertyInfo, bool> predicate)
        {
            if (target == null)
            {
                Debug.LogError("The target object is null. Check for missing scripts.");
                yield break;
            }

            List<Type> types = GetSelfAndBaseTypes(target);

            for (int i = types.Count - 1; i >= 0; i--)
            {
                IEnumerable<PropertyInfo> propertyInfos = types[i]
                    .GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(predicate);

                foreach (var propertyInfo in propertyInfos)
                    yield return propertyInfo;
            }
        }

        /// <summary>Returns all methods on the target and its base types that match the predicate.</summary>
        public static IEnumerable<MethodInfo> GetAllMethods(object target, Func<MethodInfo, bool> predicate)
        {
            if (target == null)
            {
                Debug.LogError("The target object is null. Check for missing scripts.");
                yield break;
            }

            List<Type> types = GetSelfAndBaseTypes(target);

            for (int i = types.Count - 1; i >= 0; i--)
            {
                IEnumerable<MethodInfo> methodInfos = types[i]
                    .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(predicate);

                foreach (var methodInfo in methodInfos)
                    yield return methodInfo;
            }
        }

        /// <summary>Finds the first field by name across the target and all base types.</summary>
        public static FieldInfo GetField(object target, string fieldName)
        {
            return GetAllFields(target, f => f.Name.Equals(fieldName, StringComparison.Ordinal)).FirstOrDefault();
        }

        /// <summary>Finds the first property by name across the target and all base types.</summary>
        public static PropertyInfo GetProperty(object target, string propertyName)
        {
            return GetAllProperties(target, p => p.Name.Equals(propertyName, StringComparison.Ordinal)).FirstOrDefault();
        }

        /// <summary>Finds the first method by name across the target and all base types.</summary>
        public static MethodInfo GetMethod(object target, string methodName)
        {
            return GetAllMethods(target, m => m.Name.Equals(methodName, StringComparison.Ordinal)).FirstOrDefault();
        }

        /// <summary>Get type and all base types of target, sorted: [target's type, base type, base's base type, ...]</summary>
        private static List<Type> GetSelfAndBaseTypes(object target)
        {
            List<Type> types = new() { target.GetType() };

            while (types.Last().BaseType != null)
                types.Add(types.Last().BaseType);

            return types;
        }

        #endregion

        #region Field Path & Type Discovery

        /// <summary>Traverses a dot-separated serialized property path on a type and returns the terminal FieldInfo.</summary>
        public static FieldInfo GetFieldViaPath(this Type type, string path)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var parent = type;
            var fi = parent.GetField(path, flags);
            var paths = path.Split('.');

            for (int i = 0; i < paths.Length; i++)
            {
                fi = parent.GetField(paths[i], flags);

                if (fi == null) { continue; }

                if (fi.FieldType.IsArray)
                {
                    parent = fi.FieldType.GetElementType();
                    i += 2;
                    continue;
                }

                if (fi.FieldType.IsGenericType)
                {
                    parent = fi.FieldType.GetGenericArguments()[0];
                    i += 2;
                    continue;
                }

                if (fi != null)
                    parent = fi.FieldType;
                else
                    return null;
            }

            return fi;
        }

        /// <summary>Returns all non-abstract subclasses of T. In the Editor it searches every assembly (add-ons included); in builds only T's assembly.</summary>
        public static List<Type> GetAllTypes<T>() => GetAllTypes(typeof(T));

        /// <summary>Returns all non-abstract subclasses (or implementations) of the given type. Every assembly in the Editor; only the type's assembly in builds.</summary>
        public static List<Type> GetAllTypes(Type type)
        {
            var subTypeList = new List<Type>();

#if UNITY_EDITOR
            //TypeCache covers every assembly, so Tasks, Decisions, States, etc. defined in add-on asmdefs are found too
            foreach (var t in UnityEditor.TypeCache.GetTypesDerivedFrom(type))
            {
                if (!t.IsAbstract && !t.IsInterface) subTypeList.Add(t);
            }
#else
            var allTypes = type.Assembly.GetTypes();

            for (int i = 0; i < allTypes.Length; i++)
            {
                if (allTypes[i].IsSubclassOf(type) && !allTypes[i].IsAbstract)
                    subTypeList.Add(allTypes[i]);
            }
#endif
            return subTypeList;
        }

        /// <summary>Returns all types across all loaded assemblies whose simple Name matches className.</summary>
        public static Type[] GetTypesByName(string className)
        {
            List<Type> returnVal = new();

            foreach (Assembly a in GetLoadedAssemblies())
            {
                Type[] assemblyTypes = a.GetTypes();
                for (int j = 0; j < assemblyTypes.Length; j++)
                {
                    if (assemblyTypes[j].Name == className)
                        returnVal.Add(assemblyTypes[j]);
                }
            }

            return returnVal.ToArray();
        }

        /// <summary>Returns the first type across all loaded assemblies whose simple Name matches className.</summary>
        public static Type GetTypeByName(string className)
        {
            return GetLoadedAssemblies()
                .SelectMany(x => x.GetTypes())
                .FirstOrDefault(t => t.Name == className);
        }

        /// <summary>Resolves a type by its assembly-qualified or full name, searching all loaded assemblies as fallback.</summary>
        public static Type FindType(string qualifiedTypeName)
        {
            Type t = Type.GetType(qualifiedTypeName);

            if (t != null) return t;

            foreach (Assembly asm in GetLoadedAssemblies())
            {
                t = asm.GetType(qualifiedTypeName);
                if (t != null) return t;
            }

            return null;
        }

        /// <summary>Returns the element type of a List&lt;T&gt; or array.</summary>
        public static Type GetListElementType(Type listType)
        {
            if (listType.IsGenericType)
                return listType.GetGenericArguments()[0];
            else
                return listType.GetElementType();
        }

        #endregion

        #region Delegates & UnityActions

        /// <summary>Converts a MethodInfo into a typed UnityAction&lt;T&gt;.</summary>
        public static UnityAction<T> CreateDelegate<T>(object target, MethodInfo method)
        {
            return (UnityAction<T>)Delegate.CreateDelegate(typeof(UnityAction<T>), target, method);
        }

        /// <summary>Converts a MethodInfo into a parameterless UnityAction.</summary>
        public static UnityAction CreateDelegate(object target, MethodInfo method)
        {
            return (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), target, method);
        }

        /// <summary>Returns a parameterless UnityAction bound to a method on a component found via string name.</summary>
        public static UnityAction GetUnityAction(this Component c, string component, string method)
        {
            var sender = (c.GetComponent(component) ?? c.GetComponentInParent(component)) ?? c.GetComponentInChildren(component);
            if (sender == null) return null;

            MethodInfo methodPtr = sender.GetType().GetMethod(method, new Type[0]);
            if (methodPtr != null)
                return CreateDelegate(sender, methodPtr);

            return null;
        }

        /// <summary>Returns a parameterless UnityAction bound to a method on a ScriptableObject.</summary>
        public static UnityAction GetUnityAction(this ScriptableObject sender, string method)
        {
            if (sender == null) return null;

            MethodInfo methodPtr = sender.GetType().GetMethod(method, new Type[0]);
            if (methodPtr != null)
                return CreateDelegate(sender, methodPtr);

            return null;
        }

        /// <summary>Returns a typed UnityAction&lt;T&gt; bound to a method or property setter on a component found via string name.</summary>
        public static UnityAction<T> GetUnityAction<T>(this Component c, string component, string method)
        {
            if (string.IsNullOrEmpty(component)) return null;

            var sender = (c.GetComponent(component) ?? c.GetComponentInParent(component)) ?? c.GetComponentInChildren(component);
            if (sender == null) return null;

            var methodPtr = sender.GetType().GetMethod(method, new Type[] { typeof(T) });
            if (methodPtr != null)
                return CreateDelegate<T>(sender, methodPtr);

            PropertyInfo property = sender.GetType().GetProperty(method);
            if (property != null)
                return CreateDelegate<T>(sender, property.SetMethod);

            return null;
        }

        /// <summary>Returns a typed UnityAction&lt;T&gt; bound to a method or property setter on a ScriptableObject.</summary>
        public static UnityAction<T> GetUnityAction<T>(this ScriptableObject sender, string method)
        {
            if (sender == null) return null;

            var methodPtr = sender.GetType().GetMethod(method, new Type[] { typeof(T) });
            if (methodPtr != null)
                return CreateDelegate<T>(sender, methodPtr);

            PropertyInfo property = sender.GetType().GetProperty(method);
            if (property != null)
                return CreateDelegate<T>(sender, property.SetMethod);

            return null;
        }

        #endregion

        #region Invoke

        /// <summary>Invokes a method by name on any object using reflection, matching parameter types.</summary>
        public static void MInvokeMethod(this object obj, string methodName, params object[] args)
        {
            obj.GetType().MFindMethod(methodName, args.Select(a => a.GetType()).ToArray()).Invoke(obj, args);
        }

        /// <summary>Finds a method on a type by name and optional parameter types. Throws MissingMethodException when not found (unless throwNotFound is false).</summary>
        public static MethodInfo MFindMethod(this Type type, string methodName, Type[] args = null, bool throwNotFound = true)
        {
            if (type == null) throw new ArgumentNullException("type");

            MethodInfo method;

            if (args == null)
            {
                method = type.GetMethod(methodName, FULL_BINDING);
            }
            else
            {
                method = type.GetMethod(methodName, FULL_BINDING, null, args, null);

                // Fallback: enum/int mismatches can break the above bind, so retry without args
                if (method == null)
                {
                    method = MFindMethod(type, methodName, null, throwNotFound);

                    if (method != null && method.GetParameters().Length != args.Length)
                        method = null;
                }
            }

            if (method == null && throwNotFound)
                throw new MissingMethodException(type.FullName, methodName);

            return method;
        }

        /// <summary>Invokes a method or sets a property by name on a MonoBehaviour, with optional argument.</summary>
        public static bool InvokeWithParams(this MonoBehaviour sender, string method, object args)
        {
            Type argType = args?.GetType();
            MethodInfo methodPtr;

            if (argType != null)
            {
                methodPtr = sender.GetType().GetMethod(method, new Type[] { argType });
            }
            else
            {
                try
                {
                    methodPtr = sender.GetType().GetMethod(method);
                }
                catch (Exception)
                {
                    throw;
                }
            }

            if (methodPtr != null)
            {
                methodPtr.Invoke(sender, args != null ? new object[] { args } : null);
                return true;
            }

            PropertyInfo property = sender.GetType().GetProperty(method);
            if (property != null)
            {
                property.SetValue(sender, args, null);
                return true;
            }

            return false;
        }

        /// <summary>Invokes a method with a delay on a MonoBehaviour via coroutine.</summary>
        public static void InvokeDelay(this MonoBehaviour behaviour, string method, object options, YieldInstruction wait)
        {
            behaviour.StartCoroutine(_invoke(behaviour, method, wait, options));
        }

        private static IEnumerator _invoke(this MonoBehaviour behaviour, string method, YieldInstruction wait, object options)
        {
            yield return wait;
            Type instance = behaviour.GetType();
            MethodInfo mthd = instance.GetMethod(method);
            mthd.Invoke(behaviour, new object[] { options });
            yield return null;
        }

        /// <summary>Invokes a method with an optional argument on a ScriptableObject.</summary>
        public static void Invoke(this ScriptableObject sender, string method, object args)
        {
            var methodPtr = sender.GetType().GetMethod(method);
            if (methodPtr == null) return;

            methodPtr.Invoke(sender, args != null ? new object[] { args } : null);
        }

        #endregion

        #region Component Utilities

        /// <summary>Returns a field value of type T from a named component on owner.</summary>
        public static T GetFieldClass<T>(this Component owner, string component, string field) where T : class
        {
            var sender = owner.GetComponent(component);
            if (sender == null) return null;

            FieldInfo fieldPtr = sender.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance);
            return fieldPtr?.GetValue(sender) as T;
        }

        /// <summary>Recursively searches children for a component by type name string.</summary>
        public static Component GetComponentInChildren(this Component owner, string classtype)
        {
            var sender = owner.GetComponent(classtype);
            if (sender) return sender;

            foreach (Transform item in owner.transform)
            {
                var found = item.GetComponentInChildren(classtype);
                if (found) return found;
            }

            return null;
        }

        /// <summary>Recursively searches parents for a component by type name string.</summary>
        public static Component GetComponentInParent(this Component owner, string classtype)
        {
            var sender = owner.GetComponent(classtype);
            if (sender != null) return sender;
            if (owner.transform.parent == null) return null;

            return owner.transform.parent.GetComponentInParent(classtype);
        }

        /// <summary>Copies all fields and writable properties from another component of the same type.</summary>
        private static T GetCopyOf<T>(this Component comp, T other) where T : Component
        {
            Type type = comp.GetType();
            if (type != other.GetType()) return null;

            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Default | BindingFlags.DeclaredOnly;

            foreach (var pinfo in type.GetProperties(flags))
            {
                if (pinfo.CanWrite)
                {
                    try { pinfo.SetValue(comp, pinfo.GetValue(other, null), null); }
                    catch { }
                }
            }

            foreach (var finfo in type.GetFields(flags))
                finfo.SetValue(comp, finfo.GetValue(other));

            return comp as T;
        }

        /// <summary>Adds a component to a GameObject and copies all data from an existing component of the same type.</summary>
        public static T AddCopyComponent<T>(this GameObject go, T toAdd) where T : Component
        {
            return go.AddComponent<T>().GetCopyOf(toAdd);
        }
        #endregion
    }
}