// Last Change: 9/17/2026 5:33pm
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MalbersAnimations
{
    [AddComponentMenu("Malbers/Utilities/Tools/Surface Tag")]

    public class SurfaceTag : MonoBehaviour, ISurface
    {
        /// <summary>Store all the surfaces in a static list when the scene is playing</summary>
        public static HashSet<SurfaceTag> AllSurfaces { get; set; }

        public SurfaceID surface;

        public SurfaceID Surface => surface;

        private void OnEnable()
        {
            AllSurfaces ??= new HashSet<SurfaceTag>();
            AllSurfaces.Add(this);
        }

        private void OnDisable()
        {
            AllSurfaces?.Remove(this);
        }
    }

    // create an Inspector Editor
#if UNITY_EDITOR

    [UnityEditor.CustomEditor(typeof(SurfaceTag)), CanEditMultipleObjects]
    public class SurfaceTagEditor : UnityEditor.Editor
    {
        SerializedProperty surface;

        private void OnEnable()
        {
            surface = serializedObject.FindProperty("surface");
        }


        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(surface);
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}

