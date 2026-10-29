using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

namespace MalbersAnimations.PathCreation
{
    [AddComponentMenu("Malbers/Animal Controller/Path Link (Unity Spline)")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/main-components/path-constraint/path-link-unity-spline")] // MWC: fixed to match the actual GitBook page slug (auto-generated from the page title)
    public class PathLink_Spline : MonoBehaviour, IPath
    {
        public SplineContainer spline;
        [Tooltip("Coarse samples used to seed the nearest-point search (higher = more accurate, slower)")]
        [Min(2)] public int m_SearchResolution = 8;
        [Tooltip("Refinement passes for the nearest-point search")]
        [Min(1)] public int m_SearchIterations = 2;

        public Vector3 StartPath => spline.EvaluatePosition(0);

        public Vector3 EndPath => spline.EvaluatePosition(1);

        public bool IsClosed => spline.Spline.Closed;

        //MWC: World-space bounds. spline.Spline.GetBounds() is in the container's LOCAL space, so
        //transform its 8 corners by the container's localToWorldMatrix and re-encapsulate.
        public Bounds WorldBounds
        {
            get
            {
                var local = spline.Spline.GetBounds();
                var m = spline.transform.localToWorldMatrix;
                var center = local.center;
                var ext = local.extents;

                var world = new Bounds(m.MultiplyPoint3x4(center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                {
                    var corner = center + new Vector3(
                        (i & 1) == 0 ? -ext.x : ext.x,
                        (i & 2) == 0 ? -ext.y : ext.y,
                        (i & 4) == 0 ? -ext.z : ext.z);
                    world.Encapsulate(m.MultiplyPoint3x4(corner));
                }
                return world;
            }
        }

        public float GetClosestTimeOnPath(Vector3 position)
        {
            //MWC: Use Unity's built-in nearest-point solver (coarse pass + iterative refinement) instead of
            //the hand-rolled fixed-step scan. More accurate, maintained, and it returns the normalized t
            //directly. Query in the spline's LOCAL space (allocation-free, no NativeSpline per call).
            var local = (float3)spline.transform.InverseTransformPoint(position);
            SplineUtility.GetNearestPoint(spline.Spline, local, out float3 _, out float t,
                m_SearchResolution, m_SearchIterations);
            return math.clamp(t, 0f, 1f);
        }


        //REVISAR!!!!!
        public Quaternion GetPathRotation(float NormalizedTime)
        {
            var forward = Vector3.Normalize(spline.EvaluateTangent(NormalizedTime));
            var up = spline.EvaluateUpVector(NormalizedTime);
            var rotation = Quaternion.LookRotation(forward, up)// * axisRemapRotation
                 ;

            return rotation;
        }

        public Vector3 GetPointAtTime(float NormalizedTime) => spline.EvaluatePosition(NormalizedTime);


        private void Reset()
        {
            spline = GetComponent<SplineContainer>();
        }

        private void OnDrawGizmos()
        {
            if (spline == null) { return; }
            if (spline.Spline.Closed) { return; }

            Gizmos.color = Color.green;
            Gizmos.DrawSphere(StartPath, 0.02f * transform.localScale.y);
            Gizmos.DrawSphere(EndPath, 0.02f * transform.localScale.y);
        }
    }
}
