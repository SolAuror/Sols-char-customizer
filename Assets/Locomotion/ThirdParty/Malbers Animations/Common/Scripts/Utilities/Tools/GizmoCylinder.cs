using UnityEngine;
namespace MalbersAnimations.Utilities
{
    public class GizmoCylinder : MonoBehaviour
    {
        [Min(0f)] public float radius = 0.5f;
        [Min(0f)] public float height = 2.0f;

        [Header("Visuals")]
        public Color gizmoColor = new Color(0, 1, 0, 0.4f);

        [Tooltip("Determines the roundness of the cylinder and the wireframe caps.")]
        [Range(1, 16)] public int segments = 8;
        public int Segments => segments * 4;

        [Tooltip("Number of vertical lines connecting the top and bottom circles.")]
        [Range(0, 4)] public int verticalLines = 1;
        public int VerticalLines => verticalLines * 4;

        // Cache the mesh per instance
        private Mesh _cylinderMesh;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            MDebug.GizmoCylinder(transform, ref _cylinderMesh, Vector3.zero, radius, height, gizmoColor, Segments, VerticalLines);
        }

        private void OnDrawGizmosSelected()
        {
            MDebug.GizmoCylinderWire(transform, new Vector3(0, 0.001f, 0), radius, height, Color.yellow, Segments, VerticalLines);
        }
#endif
    }
}