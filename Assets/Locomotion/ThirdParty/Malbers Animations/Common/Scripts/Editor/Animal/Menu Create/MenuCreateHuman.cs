#if UNITY_EDITOR
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MalbersAnimations.Controller
{
    /// <summary> Floating window opened from [GameObject > Malbers Animations > Create Human] to pick the base Human prefab from the [Create Human] GameObjectList asset </summary>
    public class MenuCreateHuman : EditorWindow
    {
        /// <summary> GUID of the [Create Human] GameObjectList asset that holds the base Human prefabs </summary>
        private const string CreateHumanList_Guid = "cc32e6ab4a53dc04294119e7056a8c2b";
        /// <summary> Menu path used by the GameObject right-click menu </summary>
        private const string MenuPath = "GameObject/Malbers Animations/Create Human";

        /// <summary> Asset holding the base prefabs to choose from. Add or remove prefabs on the asset to change the options </summary>
        [SerializeField] private GameObjectList prefabList;
        /// <summary> Scene GameObjects (Character Meshes) that will be parented to the new Human </summary>
        [SerializeField] private List<GameObject> targets = new();
        /// <summary> Index of the selected base prefab inside the list </summary>
        [SerializeField] private int selected;
        /// <summary> Foldout state of the Base Prefab section </summary>
        [SerializeField] private bool showPrefabs = true;
        /// <summary> Scroll position of the window </summary>
        private Vector2 scroll;

        /// <summary> Opens the Create Human window for the selected scene GameObjects </summary>
        [MenuItem(MenuPath, false, -100)]
        private static void OpenWindow(MenuCommand menuCommand)
        {
            var window = GetWindow<MenuCreateHuman>(true, "Create Human", true);
            window.minSize = new Vector2(360, 260);
            window.targets = Selection.gameObjects.Where(g => g != null && !EditorUtility.IsPersistent(g)).ToList();
            window.FindPrefabList();
            window.Show();
        }

        /// <summary> Enable the menu only when a scene GameObject (not a prefab asset) is selected </summary>
        [MenuItem(MenuPath, true)]
        private static bool OpenWindowValidate()
        {
            var go = Selection.activeGameObject;
            return go != null && !EditorUtility.IsPersistent(go);
        }

        /// <summary> Reload the prefab list after a domain reload </summary>
        private void OnEnable() => FindPrefabList();

        /// <summary> Load the [Create Human] list by GUID, falling back to the first GameObjectList named "Create Human" </summary>
        private void FindPrefabList()
        {
            if (prefabList != null) return;

            var path = AssetDatabase.GUIDToAssetPath(CreateHumanList_Guid);
            if (!string.IsNullOrEmpty(path))
                prefabList = AssetDatabase.LoadAssetAtPath<GameObjectList>(path);

            if (prefabList == null)
            {
                var guid = AssetDatabase.FindAssets("t:GameObjectList Create Human").FirstOrDefault();
                if (!string.IsNullOrEmpty(guid))
                    prefabList = AssetDatabase.LoadAssetAtPath<GameObjectList>(AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        /// <summary> Valid (non-null) prefabs of the current list </summary>
        private List<GameObject> Prefabs => prefabList != null && prefabList.list != null
            ? prefabList.list.Where(p => p != null).ToList()
            : new List<GameObject>();

        /// <summary> Draw the window </summary>
        private void OnGUI()
        {
            MalbersEditor.DrawDescription("Create a new Human Character using the selected GameObject as the Character Mesh");

            targets.RemoveAll(t => t == null);
            var prefabs = Prefabs;
            selected = Mathf.Clamp(selected, 0, Mathf.Max(0, prefabs.Count - 1));

            using (var sc = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = sc.scrollPosition;

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Character Mesh", EditorStyles.boldLabel);

                    if (targets.Count == 0)
                        EditorGUILayout.HelpBox("No scene GameObject selected. Right-click a Character Mesh on the Hierarchy and choose [Malbers Animations > Create Human]", MessageType.Warning);

                    using (new EditorGUI.DisabledGroupScope(true))
                    {
                        foreach (var t in targets)
                            EditorGUILayout.ObjectField(t, typeof(GameObject), true);
                    }
                }

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    prefabList = (GameObjectList)EditorGUILayout.ObjectField(
                        new GUIContent("Prefab List", "GameObjectList asset with the base Human prefabs. Add or remove prefabs here to change the options"),
                        prefabList, typeof(GameObjectList), false);

                    if (prefabList == null)
                        EditorGUILayout.HelpBox("Assign a GameObjectList asset with the base Human prefabs", MessageType.Warning);
                    else if (prefabs.Count == 0)
                        EditorGUILayout.HelpBox("The Prefab List is empty. Add the base Human prefabs to the asset", MessageType.Warning);
                }

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    showPrefabs = MalbersEditor.Foldout(showPrefabs, "Base Prefab");

                    if (showPrefabs)
                    {
                        for (int i = 0; i < prefabs.Count; i++)
                        {
                            using (new GUILayout.HorizontalScope())
                            {
                                GUILayout.Space(5);

                                //Radio circle on the left, name fills the remaining width so long names are not clamped
                                if (GUILayout.Toggle(selected == i, new GUIContent("   " + prefabs[i].name), EditorStyles.radioButton, GUILayout.ExpandWidth(true)))
                                    selected = i;

                                if (GUILayout.Button(new GUIContent("Ping", "Ping the prefab on the Project window"), EditorStyles.miniButton, GUILayout.Width(40)))
                                    EditorGUIUtility.PingObject(prefabs[i]);
                            }
                        }
                    }
                }
            }

            var prefab = prefabs.Count > 0 ? prefabs[selected] : null;

            using (new EditorGUI.DisabledGroupScope(prefab == null || targets.Count == 0))
            {
                var label = prefab != null ? $"Create [{prefab.name}]" : "Create";

                if (GUILayout.Button(new GUIContent(label, "Instantiate the base prefab and parent the Character Mesh to it"), GUILayout.Height(28)))
                {
                    foreach (var t in targets.ToList())
                        DoHuman(t, prefab);

                    Debug.Log($"[{prefab.name}] Created!. Please save the new Created Character as a Prefab Variant on your project");
                    Close();
                }
            }
        }

        /// <summary> Instantiate the base prefab, move the Avatar and Root Bone from the Character Mesh, and parent the mesh to it </summary>
        private static void DoHuman(GameObject gameObject, GameObject humanPrefab)
        {
            if (gameObject == null || humanPrefab == null) return;

            gameObject.transform.ResetLocal(); //important to reset the transform Local

            if (!gameObject.TryGetComponent<Animator>(out var animator))
                animator = gameObject.AddComponent<Animator>();

            var currentAvatar = animator.avatar;
            var AvatarRoot = animator.avatarRoot;

            var sceneObj = (GameObject)PrefabUtility.InstantiatePrefab(humanPrefab);
            sceneObj.GetComponent<Animator>().avatar = currentAvatar; //Set the Avatar to the new Player

            gameObject.transform.parent = sceneObj.transform;
            sceneObj.transform.ResetLocal();

            var animal = sceneObj.GetComponent<MAnimal>();
            if (animal != null) animal.RootBone = AvatarRoot; //Set the Root Bone on the Animal Controller
            sceneObj.name = gameObject.name;

            Selection.activeGameObject = sceneObj; //Select the new Player
            DestroyImmediate(animator); //Remove the animator from the mesh
        }
    }
}
#endif
