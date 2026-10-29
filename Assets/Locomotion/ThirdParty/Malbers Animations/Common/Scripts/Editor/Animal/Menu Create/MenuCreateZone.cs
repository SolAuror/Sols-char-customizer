// Last Change: 9/30/2026 12:00pm
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MalbersAnimations.Controller
{
    public class MenuCreateZone
    {
        /// <summary>Creates a new GameObject with a trigger BoxCollider and a Zone2 component</summary>
        [MenuItem("GameObject/Malbers Animations/Create New Zone", false, 10)]
        static void CreateZoneGameObject()
        {
            GameObject newObject = new("New Zone");
            var col = newObject.AddComponent<BoxCollider>();
            newObject.AddComponent<Zone2>(); //Collider is added first so Zone2.Reset() picks it up
            var gz = newObject.AddComponent<GizmoVisualizer>();

            col.center = new Vector3(0, 1, 0);
            col.size = new Vector3(2, 2, 2);

            gz.DebugColor = new Color(0.1f, 1, 0.0f, 0.3f);

            if (Selection.activeGameObject != null)
            {
                newObject.transform.SetParent(Selection.activeGameObject.transform);
                newObject.transform.ResetLocal();
            }
            Selection.activeGameObject = newObject;
        }
    }
}
#endif