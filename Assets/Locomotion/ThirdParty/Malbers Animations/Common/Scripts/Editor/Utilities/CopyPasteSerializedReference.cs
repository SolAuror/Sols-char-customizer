using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
#if UNITY_EDITOR

    [InitializeOnLoad]
    public class CopyPasteSerializedReference
    {
        private static (string json, Type type) lastObject;

        private static (string json, Type type)[] lastObjects;

        static CopyPasteSerializedReference()
        {
            EditorApplication.contextualPropertyMenu += ShowSerializeReferenceCopyPasteContextMenu;
        }

        public static Action UpdateDropdownCallback;

        private static void ShowSerializeReferenceCopyPasteContextMenu(GenericMenu menu, SerializedProperty property)
        {
            if (property.propertyType == SerializedPropertyType.ManagedReference)
            {
                var copyProperty = property.Copy();
                menu.AddItem(new GUIContent("Copy Serialize Reference"), false,
                    (_) =>
                    {
                        CopyReferenceValue(copyProperty);
                    },
                    null);

                var pasteContent = new GUIContent("Paste Serialize Reference");
                menu.AddItem(pasteContent, false,
                    (_) =>
                    {
                        PasteReferenceValue(copyProperty);
                    },
                    null);

                //MWC — IsArrayElement, not IsArray: a Serialize Reference field INSIDE an array element is
                //not itself an element, and duplicating it appended garbage to the wrong (outer) list
                if (property.IsArrayElement())
                {
                    var duplicateContent = new GUIContent("Duplicate Serialize Reference Array Element");
                    menu.AddItem(duplicateContent, false, (_) => DuplicateSerializeReferenceArrayElement(copyProperty),
                        null);
                }
            }
            else if (property.isArray && property.arrayElementType.Contains("managedReference"))
            {
                menu.AddItem(new GUIContent("Copy Array Managed Reference"), false,
                    (_) =>
                    {
                        CopyReferenceValueArray(property);
                    },
                    null);


                menu.AddItem(new GUIContent("Paste Array Managed Reference"), false,
                   (_) =>
                   {
                       PasteReferenceValueArray(property);
                   },
                   null);
            }
        }

        private static void CopyReferenceValue(SerializedProperty property)
        {
            var refValue = property.managedReferenceValue;
            lastObject.json = JsonUtility.ToJson(refValue);
            lastObject.type = refValue?.GetType();
        }

        private static void CopyReferenceValueArray(SerializedProperty property)
        {
            lastObjects = new (string json, Type type)[property.arraySize];

            for (int i = 0; i < property.arraySize; i++)
            {
                var refValue = property.GetArrayElementAtIndex(i).managedReferenceValue;
                lastObjects[i].json = JsonUtility.ToJson(refValue);
                lastObjects[i].type = refValue?.GetType();
            }

            Debug.Log($"Copied [{property.displayName}] Managed Reference Array Elements.");
        }

        private static void PasteReferenceValueArray(SerializedProperty property)
        {
            try
            {
                property.ClearArray();

                property.arraySize = lastObjects.Length;
                property.serializedObject.ApplyModifiedProperties();
                property.serializedObject.Update();

                for (int i = 0; i < property.arraySize; i++)
                {
                    var (json, type) = lastObjects[i];

                    var arrayElement = property.GetArrayElementAtIndex(i);

                    if (type != null)
                    {
                        var pasteObj = JsonUtility.FromJson(json, type);
                        arrayElement.managedReferenceValue = pasteObj;
                    }
                    else
                    {
                        arrayElement.managedReferenceValue = null;
                    }
                }
                property.serializedObject.ApplyModifiedProperties();
                property.serializedObject.Update();

                Debug.Log($"Pasted [{(property != null ? property.displayName : "<>")}] Managed Reference Array Elements.");

            }
            catch (Exception)
            {
                // ignored
            }
        }


        private static void PasteReferenceValue(SerializedProperty property)
        {
            try
            {
                if (lastObject.type != null)
                {
                    var pasteObj = JsonUtility.FromJson(lastObject.json, lastObject.type);
                    property.managedReferenceValue = pasteObj;
                }
                else
                {
                    property.managedReferenceValue = null;
                }

                property.serializedObject.ApplyModifiedProperties();
                property.serializedObject.Update();
                // PropertyDrawer.UpdateDropdownCallback?.Invoke();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        /// <summary>Appends a copy of the given Serialize Reference array element to its own array</summary>
        private static void DuplicateSerializeReferenceArrayElement(SerializedProperty property)
        {
            var sourceElement = property.managedReferenceValue;
            var arrayProperty = MSerializedTools.GetArrayPropertyFromArrayElement(property);

            //MWC — the path was not an array element, or its owner array no longer resolves
            if (arrayProperty == null || !arrayProperty.isArray)
            {
                Debug.LogWarning($"[{property.propertyPath}] is not a Serialize Reference Array Element. Cannot duplicate");
                return;
            }

            var newElementIndex = arrayProperty.arraySize;
            arrayProperty.arraySize = newElementIndex + 1;

            //MWC — the resize must be committed BEFORE the new element can take a managed reference
            property.serializedObject.ApplyModifiedProperties();
            property.serializedObject.Update();

            if (sourceElement != null)
            {
                var newElementProperty = arrayProperty.GetArrayElementAtIndex(newElementIndex);

                //MWC — never assign to a plain [Serializable] element: Unity throws InvalidOperationException
                if (newElementProperty.propertyType != SerializedPropertyType.ManagedReference)
                {
                    Debug.LogWarning($"[{arrayProperty.propertyPath}] is not a [SerializeReference] Array. Cannot duplicate");
                    return;
                }

                var json = JsonUtility.ToJson(sourceElement);
                var newObj = JsonUtility.FromJson(json, sourceElement.GetType());
                newElementProperty.managedReferenceValue = newObj;
            }

            property.serializedObject.ApplyModifiedProperties();
            property.serializedObject.Update();
        }
    }
#endif
}