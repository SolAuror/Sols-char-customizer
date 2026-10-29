#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MalbersAnimations.IK
{
    /// <summary>
    /// Adds "Copy IKSet" / "Paste IKSet (Replace)" / "Paste IKSet (Append)" to the
    /// right-click context menu of any element inside IKManager.sets[].
    ///
    /// JsonUtility cannot round-trip [SerializeReference] polymorphic types by itself,
    /// so each IKProcessor / WeightProcessor is wrapped with its assembly-qualified
    /// type name so it can be reconstructed faithfully on paste.
    /// </summary>
    [InitializeOnLoad]
    public static class IKSetCopyPaste
    {
        // ── Clipboard ────────────────────────────────────────────────────────────
        private static IKSetClipboard s_Clipboard;

        /// <summary> True when an IKSet has been copied and is ready to paste. </summary>
        public static bool HasClipboard => s_Clipboard != null;

        /// <summary> Display name of the currently copied IKSet, or null if empty. </summary>
        public static string ClipboardSetName => s_Clipboard?.SetName;

        /// <summary>
        /// Pastes only the IKProcessors and WeightProcessors from the clipboard
        /// into <paramref name="destination"/>, leaving its Targets untouched.
        /// Called by IKSetProfile to paste from an IKManager clipboard.
        /// </summary>
        public static void PasteProcessorsInto(IKSet destination)
        {
            if (s_Clipboard == null) return;
            destination.IKProcesors = RestorePolymorphicList<IKProcessor>(s_Clipboard.Processors);
            destination.weightProcessors = RestorePolymorphicList<WeightProcessor>(s_Clipboard.WeightProcessors);
            s_Clipboard = null; // Clear clipboard after paste
        }

        /// <summary>
        /// Copies only the IKProcessors and WeightProcessors from <paramref name="source"/>
        /// into the clipboard. Called by the IKManager inspector Copy button.
        /// </summary>
        public static void CopyProcessorsFrom(IKSet source)
        {
            s_Clipboard = Capture(source);
            Debug.Log($"[IKSetCopyPaste] Copied processors from IKSet \"{s_Clipboard.SetName}\".");
        }

        // ── Hook ─────────────────────────────────────────────────────────────────
        static IKSetCopyPaste()
        {
            EditorApplication.contextualPropertyMenu += OnContextualPropertyMenu;
        }

        // ─────────────────────────────────────────────────────────────────────────
        private static void OnContextualPropertyMenu(GenericMenu menu, SerializedProperty property)
        {
            // Only react when the right-clicked property belongs to an IKManager.sets element
            if (property.serializedObject.targetObject is not IKManager) return;

            // The path for a list element looks like:  sets.Array.data[N]
            // Accept both the element itself and any child property inside it.
            string path = property.propertyPath;
            if (!path.StartsWith("sets.Array.data[")) return;

            // Resolve the root element (sets.Array.data[N])
            int closeIndex = path.IndexOf(']');
            if (closeIndex < 0) return;
            string elementPath = path.Substring(0, closeIndex + 1);
            SerializedProperty elementProp = property.serializedObject.FindProperty(elementPath);
            if (elementProp == null) return;

            // Parse the array index from the path
            int arrayIndex = ParseArrayIndex(elementPath);
            if (arrayIndex < 0) return;

            // ── Copy ─────────────────────────────────────────────────────────
            menu.AddItem(new GUIContent("IKSet/Copy IKSet"), false, () =>
            {
                var manager = (IKManager)property.serializedObject.targetObject;
                if (arrayIndex >= manager.sets.Count) return;

                s_Clipboard = Capture(manager.sets[arrayIndex]);
                Debug.Log($"[IKSetCopyPaste] Copied IKSet \"{s_Clipboard.SetName}\".");
            });

            // ── Paste (Replace) ───────────────────────────────────────────────
            if (s_Clipboard != null)
            {
                menu.AddItem(new GUIContent("IKSet/Paste IKSet (Replace)"), false, () =>
                {
                    var manager = (IKManager)property.serializedObject.targetObject;
                    if (arrayIndex >= manager.sets.Count) return;

                    Undo.RecordObject(manager, "Paste IKSet (Replace)");
                    manager.sets[arrayIndex] = Restore(s_Clipboard);
                    EditorUtility.SetDirty(manager);
                    Debug.Log($"[IKSetCopyPaste] Replaced sets[{arrayIndex}] with \"{s_Clipboard.SetName}\".");
                });

                menu.AddItem(new GUIContent("IKSet/Paste IKSet (Append)"), false, () =>
                {
                    var manager = (IKManager)property.serializedObject.targetObject;

                    Undo.RecordObject(manager, "Paste IKSet (Append)");
                    manager.sets.Add(Restore(s_Clipboard));
                    EditorUtility.SetDirty(manager);
                    Debug.Log($"[IKSetCopyPaste] Appended \"{s_Clipboard.SetName}\" to sets.");
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("IKSet/Paste IKSet (Replace)"));
                menu.AddDisabledItem(new GUIContent("IKSet/Paste IKSet (Append)"));
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Serialization helpers
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Capture an IKSet into a plain-data clipboard object that survives
        /// the session (but not between editor restarts, which is fine).
        /// </summary>
        private static IKSetClipboard Capture(IKSet source)
        {
            // Serialize the "value-type" fields of IKSet via JsonUtility.
            // [SerializeReference] lists will come out as empty arrays here – we
            // handle them separately below.
            string ikSetJson = JsonUtility.ToJson(source);

            var processors = CapturePolymorphicList<IKProcessor>(source.IKProcesors);
            var weightProcessors = CapturePolymorphicList<WeightProcessor>(source.weightProcessors);

            return new IKSetClipboard
            {
                SetName = source.Name,
                IKSetJson = ikSetJson,
                Processors = processors,
                WeightProcessors = weightProcessors,
            };
        }

        /// <summary>
        /// Restore an IKSet from clipboard data.
        /// A new IKSet instance is created each time so independent copies are returned.
        /// </summary>
        private static IKSet Restore(IKSetClipboard clip)
        {
            // Deserialize the value-type fields back into a fresh IKSet.
            var newSet = new IKSet();
            JsonUtility.FromJsonOverwrite(clip.IKSetJson, newSet);

            // Overwrite the [SerializeReference] lists with properly typed instances.
            newSet.IKProcesors = RestorePolymorphicList<IKProcessor>(clip.Processors);
            newSet.weightProcessors = RestorePolymorphicList<WeightProcessor>(clip.WeightProcessors);

            return newSet;
        }

        // ── Polymorphic list helpers ──────────────────────────────────────────────

        private static List<PolymorphicEntry> CapturePolymorphicList<T>(List<T> list)
        {
            var result = new List<PolymorphicEntry>(list?.Count ?? 0);
            if (list == null) return result;

            foreach (var item in list)
            {
                if (item == null)
                {
                    result.Add(new PolymorphicEntry { TypeName = string.Empty, Json = string.Empty });
                    continue;
                }

                result.Add(new PolymorphicEntry
                {
                    TypeName = item.GetType().AssemblyQualifiedName,
                    Json = JsonUtility.ToJson(item),
                });
            }
            return result;
        }

        private static List<T> RestorePolymorphicList<T>(List<PolymorphicEntry> entries)
        {
            var result = new List<T>(entries?.Count ?? 0);
            if (entries == null) return result;

            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.TypeName))
                {
                    result.Add(default);
                    continue;
                }

                Type type = Type.GetType(entry.TypeName);
                if (type == null)
                {
                    Debug.LogWarning($"[IKSetCopyPaste] Could not find type \"{entry.TypeName}\". Entry skipped.");
                    continue;
                }

                object instance = Activator.CreateInstance(type);
                JsonUtility.FromJsonOverwrite(entry.Json, instance);
                result.Add((T)instance);
            }
            return result;
        }

        // ── Utility ──────────────────────────────────────────────────────────────

        private static int ParseArrayIndex(string elementPath)
        {
            // Path format:  sets.Array.data[N]
            int open = elementPath.LastIndexOf('[');
            int close = elementPath.LastIndexOf(']');
            if (open < 0 || close < 0 || close <= open) return -1;

            string indexStr = elementPath.Substring(open + 1, close - open - 1);
            return int.TryParse(indexStr, out int index) ? index : -1;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Data transfer objects
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// In-memory clipboard for one IKSet.
    /// Not a ScriptableObject so it lives only for the current editor session,
    /// which is acceptable for copy/paste within the same Unity instance.
    /// </summary>
    internal class IKSetClipboard
    {
        public string SetName;

        /// <summary> JsonUtility snapshot of IKSet value-type fields. </summary>
        public string IKSetJson;

        public List<PolymorphicEntry> Processors;
        public List<PolymorphicEntry> WeightProcessors;
    }

    /// <summary>
    /// Stores one [SerializeReference] element as its concrete type name + JSON data.
    /// </summary>
    [Serializable]
    internal class PolymorphicEntry
    {
        /// <summary> Assembly-qualified name of the concrete type. </summary>
        public string TypeName;

        /// <summary> JsonUtility.ToJson output for the concrete instance. </summary>
        public string Json;
    }
}
#endif