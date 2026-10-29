using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>Malbers Isolated classes to be used on other Scripts </summary>
namespace MalbersAnimations.Controller
{
    [System.Serializable]
    public class MPivots
    {
        /// <summary>Name of the Pivot</summary>
        public string name = "Pivot";
        public Vector3 position = Vector3.up;
        // public float multiplier = 1;

        [HideInInspector] public bool EditorModify = false;
        [HideInInspector] public int EditorDisplay = 0;
        [HideInInspector] public Color PivotColor = Color.blue;

        public MPivots(string name, Vector3 pos, float mult)
        {
            this.name = name;
            position = pos;
            //  multiplier = mult;
            PivotColor = Color.blue;
        }

        /// <summary>Returns the World position of the Pivot </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 World(Transform t) => t.TransformPoint(position);
    }
}
