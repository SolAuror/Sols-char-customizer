// Last Change: 9/17/2026 5:33pm
using UnityEngine;
using System.Collections.Generic;
using System.Linq;



#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    public enum TagFindGameObject { First, Random, Index, ByName, Closest }


    [CreateAssetMenu(menuName = "Malbers Animations/Tag", fileName = "New Tag", order = 3000)]
    public class Tag : IDs
    {
        /// <summary>All the GameObjects currently tagged with this Tag (runtime only, never serialized)</summary>
        public HashSet<GameObject> gameObjects { get; set; } = new();
        public bool ValidObjects => gameObjects != null && gameObjects.Count > 0;

        [Tooltip("Color to identify tags in the inspector")]
        public Color Color = new(0.2f, 0.5f, 1f, 1f);
        public override Color IDColor => Color;

        public bool IsEmpty => gameObjects == null || gameObjects.Count == 0;

        public virtual void Clear() => gameObjects.Clear();

        /// <summary>  Adds a GameObject to the list of tagged GameObjects. This method should be called by the MalbersTag component when a GameObject is tagged with this Tag.
        public virtual void Add(GameObject go)
        {
            gameObjects ??= new HashSet<GameObject>();
            gameObjects.Add(go);
        }

        /// <summary>Finds a GameObject that is a child of the given parent Transform  </summary>
        public virtual GameObject FindInHierarchy(Transform parent)
        {
            foreach (var go in gameObjects)
            {
                if (go != null && go.transform.SameHierarchy(parent))
                {
                    return go;
                }
            }
            return null;
        }

        public virtual GameObject FindFirst() => gameObjects != null && gameObjects.Count > 0 ? gameObjects.First() : null;
        public virtual Transform FindFirstT() => gameObjects != null && gameObjects.Count > 0 ? gameObjects.First().transform : null;
        public virtual GameObject FindFirst(Transform parent)
        {
            return parent != null ? FindInHierarchy(parent) : FindFirst();
        }

        /// <summary>Finds a Transform that is a child of the given parent Transform  </summary>
        public virtual Transform FindInHierarchyT(Transform parent)
        {
            foreach (var go in gameObjects)
            {
                if (go != null && go.transform.SameHierarchy(parent))
                {
                    return go.transform;
                }
            }
            return null;
        }

        public virtual bool Contains(GameObject go) => gameObjects != null && gameObjects.Contains(go);


        /// <summary> Removes a GameObject from the list of tagged GameObjects. This method should be called by the MalbersTag component when a GameObject is untagged or destroyed  </summary>
        public virtual void Remove(GameObject go) => gameObjects.Remove(go);
        public virtual void Remove(Transform t) => gameObjects.Remove(t.gameObject);


        public GameObject GetItem(TagFindGameObject type, int Index = 0, string m_name = "", GameObject origin = null)
        {
            if (IsEmpty) return null;

            return type switch
            {
                TagFindGameObject.First => FindFirst(),
                TagFindGameObject.Random => Item_GetRandom(),
                TagFindGameObject.Index => gameObjects.ElementAtOrDefault(Index),
                TagFindGameObject.ByName => gameObjects.FirstOrDefault(x => x.name == m_name),
                TagFindGameObject.Closest => Item_GetClosest(origin),
                _ => null,
            };
        }


        public virtual GameObject Item_GetRandom()
        {
            if (gameObjects != null && gameObjects.Count > 0)
            {
                return gameObjects.ElementAt(Random.Range(0, gameObjects.Count));
            }
            return null;
        }

        /// <summary>Return the Closest game object from an origin</summary>
        public GameObject Item_GetClosest(GameObject origin)
        {
            if (origin == null) return null;

            GameObject closest = null;

            float minDistance = float.MaxValue;

            foreach (var item in gameObjects)
            {
                if (item == null) continue;
                var Distance = Vector3.SqrMagnitude(item.transform.position - origin.transform.position);

                if (Distance < minDistance)
                {
                    closest = item;
                    minDistance = Distance;
                }
            }
            return closest;
        }



        protected virtual void OnEnable() => ID = name.GetHashCode();

        //implicit operator to allow using the Tag directly as an int (the ID) in conditions and other places where an int is expected
        public static implicit operator int(Tag tag) => tag != null ? tag.ID : 0;
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(Tag)), CanEditMultipleObjects]
    public class TagEditor : Editor
    {
        SerializedProperty ID, Color,
            DisplayName;

        Tag M;

        void OnEnable()
        {
            ID = serializedObject.FindProperty("ID");
            Color = serializedObject.FindProperty(nameof(M.Color));
            DisplayName = serializedObject.FindProperty("DisplayName");
            M = (Tag)target;

            if (!Application.isPlaying)
            {
                ID.intValue = target.name.GetHashCode();
                serializedObject.ApplyModifiedProperties();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                MalbersEditor.Foldout_Bold(true, target.name, MTools.MBlue * 2, true);
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(DisplayName);

                    // MWC — compact "Set" button: copies the asset file name into Display Name
                    if (GUILayout.Button(new GUIContent("Set", "Set the Display Name to match the asset's file name"),
                        GUILayout.Width(40)))
                    {
                        serializedObject.ApplyModifiedProperties();   // flush any pending field edits
                        IDsEditor.SetNames(targets);                  // rename directly on the objects
                        serializedObject.Update();                    // re-read so the field shows the new value
                        GUI.FocusControl(null);
                    }

                    EditorGUILayout.PropertyField(Color, GUIContent.none, GUILayout.Width(50));
                }

                using (new EditorGUI.DisabledGroupScope(true))
                    EditorGUILayout.PropertyField(ID);
            }

            using (new EditorGUI.DisabledGroupScope(true))
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"Tagged GameObjects [{M.gameObjects.Count}] [RUNTIME ONLY]", EditorStyles.boldLabel);

                    if (Application.isPlaying && M.gameObjects != null)
                    {
                        var index = 0;
                        foreach (var item in M.gameObjects)
                        {
                            EditorGUILayout.ObjectField($"Tagged GameObject [{index}]", item, typeof(GameObject), true);
                            index++;
                        }
                    }
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}