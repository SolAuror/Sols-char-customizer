using UnityEngine;

namespace MalbersAnimations.PathCreation
{
    public interface IPath
    {
        /// <summary> Gets the closest Normalize time of of a world point in the spline  </summary>
        /// <param name="position">World position</param>
        public float GetClosestTimeOnPath(Vector3 position);

        /// <summary> Get Position of the Path at a normalized time of the Path</summary>
        public Vector3 GetPointAtTime(float NormalizedTime);

        /// <summary> Get Rotation of the Path at a normalized time of the Path</summary>
        public Quaternion GetPathRotation(float NormalizedTime);

        /// <summary> Get the Start position of the Path</summary>
        public Vector3 StartPath{  get; }

        /// <summary> Get the End position of the Path</summary>
        public Vector3 EndPath {  get; }

        /// <summary> Is the Path Looped/Closed</summary>
        public bool IsClosed { get; }

        /// <summary> Get the Path Bounds in WORLD space (used for the proximity broad-phase) </summary>
        //MWC: was 'bounds' (local space, only consumed by the removed BoxCollider sizing). Now world-space.
        public Bounds WorldBounds { get; }
    }
}
