#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace MalbersAnimations
{
    public class MalbersACMenu : EditorWindow
    {

        private const string Steve_GUID = "9c48d1fd6bf45244a96eadfdeed449c3";
        private const string WolfLite_GUID = "a578de702f2191b4fa9cdcec1ad493c5";
        private const string Camera_GUID = "97ae43971f3549145bbc254732633bed";
        private const string Volume_GUID = "e96e724e61aa7fd4e8b5b1b6c48f55a1";
        private const string PlayerInput_GUID = "386c7bbdb3568ae47b41dd962c701f7c";
        private const string MainUI_GUID = "8034030d3588d8143aff9b260982b089";

        //const string Steve_Path = "Assets/Malbers Animations/Animal Controller/Human/Steve Player.prefab";
        //const string WolfLite_Path = "Assets/Malbers Animations/Animal Controller/Wolf Lite/Wolf Lite.prefab";
        //const string Camera_Path = "Assets/Malbers Animations/Common/Cinemachine/Cameras CM3.prefab";
        //const string Volume_Path = "Assets/Malbers Animations/Common/Prefabs/Global Volume Post Effects.prefab";
        //const string PlayerInputPath = "Assets/Malbers Animations/Common/Prefabs/Game Logic/Player Input.prefab";

        private static GameObject LoadByGUID(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning($"Asset not found for GUID: {guid}");
                return null;
            }
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }



        [MenuItem("Tools/Malbers Animations/Create Test Scene (Steve)", false, 100)]
        public static void CreateSampleSceneSteve()
        {
            CreateScene(Steve_GUID, false);
        }


        [MenuItem("Tools/Malbers Animations/Create Test Scene (Wolf)", false, 100)]
        public static void CreateSampleSceneWolf()
        {
            CreateScene(WolfLite_GUID);
        }


        [MenuItem("Tools/Malbers Animations/Create Test Scene", false, 100)]
        public static void CreateSampleScene()
        {
            RemoveDefaultCamera();
            CreateGroundPlane();

            InstantiateGameObjectByGuid(Camera_GUID);
            InstantiateGameObjectByGuid(Volume_GUID);
            InstantiateGameObjectByGuid(PlayerInput_GUID);
            AddUI();
        }

        [MenuItem("GameObject/Malbers Animations/Add Player Input", false, -100)]
        private static void CreatePlayerInputGO()
        {
            var PI = InstantiateGameObjectByGuid(PlayerInput_GUID);
            Selection.activeGameObject = PI;
        }

        private static void CreateGroundPlane()
        {
            var TestPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            TestPlane.transform.localScale = new Vector3(20, 1, 20);
            TestPlane.GetComponent<MeshRenderer>().sharedMaterial =
                AssetDatabase.LoadAssetAtPath("Assets/Malbers Animations/Common/Materials & Textures/Environment/Ground 20.mat", typeof(Material)) as Material;
            TestPlane.isStatic = true;
        }

        public static void CreateScene(string character, bool playerinput = true)
        {
            RemoveDefaultCamera();
            CreateGroundPlane();

            InstantiateGameObjectByGuid(character);
            InstantiateGameObjectByGuid(Camera_GUID);
            InstantiateGameObjectByGuid(Volume_GUID);

            if (playerinput)
            {
                InstantiateGameObjectByGuid(PlayerInput_GUID);
            }
            AddUI();
        }


        private static void AddUI()
        {
            InstantiateGameObjectByGuid(MainUI_GUID);
        }

        private static void RemoveDefaultCamera()
        {
            var all = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().ToList();
            var mainCam = all.Find(x => x.name == "Main Camera");
            if (mainCam)
            { DestroyImmediate(mainCam); }
        }

        //private static GameObject InstantiateGameObject(string path)
        //{
        //    var gameObject = AssetDatabase.LoadAssetAtPath(path, typeof(GameObject)) as GameObject;
        //    if (gameObject)
        //    {
        //        var instance = (GameObject)PrefabUtility.InstantiatePrefab(gameObject);
        //        return instance;
        //    }
        //    return null;
        //}

        private static GameObject InstantiateGameObjectByGuid(string path)
        {
            var gameObject = LoadByGUID(path);
            if (gameObject)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(gameObject);
                return instance;
            }
            return null;
        }


        [MenuItem("Tools/Malbers Animations/Tools/Remove All MonoBehaviours from Selected", false, 500)]
        public static void RemoveMono()
        {
            var allGo = Selection.gameObjects;

            if (allGo != null)
            {
                foreach (var selected in allGo)
                {
                    var AllComponents = selected.GetComponentsInChildren<MonoBehaviour>(true);

                    Debug.Log($"Removed {AllComponents.Length} from {selected}", selected);

                    foreach (var comp in AllComponents)
                    {
                        var t = comp.gameObject;
                        DestroyImmediate(comp);
                        EditorUtility.SetDirty(t);
                    }
                }
            }
        }

        [MenuItem("GameObject/-Incremental Name Suffix-", false, 0)]
        public static void IncrementalSuffix()
        {
            GameObject[] selectedObjects = Selection.gameObjects;

            if (selectedObjects.Length == 0) return;

            // Sort by hierarchy order
            System.Array.Sort(selectedObjects, (a, b) =>
                a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

            Undo.RecordObjects(selectedObjects, "Add Incremental Naming");

            for (int i = 0; i < selectedObjects.Length; i++)
            {
                GameObject obj = selectedObjects[i];
                obj.name = $"{obj.name} ({i + 1})";
            }

            //Clear Selection
            Selection.objects = new Object[0];
        }
    }
}
#endif