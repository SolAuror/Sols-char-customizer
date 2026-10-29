// Last Change: 9/30/2026 3:20pm
using UnityEngine;
using UnityEngine.Playables;

namespace MalbersAnimations.Controller
{
    /// <summary> Aligns an Animal to the ground (position and rotation) while a Ground Aligner clip is playing </summary>
    public class ACGroundAlignerBehaviour : PlayableBehaviour
    {
        /// <summary> Length of the ground rays (multiplied by the Animal Scale) </summary>
        public float distance;
        /// <summary> Use the Hip pivot as the back ray. When false only the Chest (Main) pivot is used </summary>
        public bool HasHipPivot;
        /// <summary> (Optional) Transform the Animal is pulled towards during the clip </summary>
        public Transform EndLocation;
        /// <summary> Amount of End Location applied over the normalized clip time </summary>
        public AnimationCurve EndLocationCurve;
        /// <summary> Turn the Animal to face the End Location forward direction as well </summary>
        public bool MatchRotation;

        /// <summary> Extra height added to the Animal Height when positioning it on the ground </summary>
        public float Offset;

        /// <summary> Casts the ground rays from the Animal pivots and aligns its position and rotation to the hit surface </summary>
        public void GroundRayCast(MAnimal animal)
        {
            var t = animal.transform;
            var up = t.up;
            float height = animal.Height;
            float scaledDistance = distance * animal.ScaleFactor;

            //The rays start one Height above the pivots so they still find the ground when the animation root is below it (going up a slope)
            float lift = height;
            float rayLength = scaledDistance + lift;

            //Resolve the pivots from the serialized MPivots: Main_Pivot_Point is a runtime cache that is still (0,0,0) in Edit Mode,
            // which made the front ray start at the root and the slope direction degenerate while scrubbing the Timeline
            bool useHip = HasHipPivot && animal.Has_Pivot_Hip;
            Vector3 frontPivot = animal.Has_Pivot_Chest ? animal.Pivot_Chest.World(t) :
                                 animal.Has_Pivot_Hip ? animal.Pivot_Hip.World(t) : t.TransformPoint(0, height, 0);
            bool twoRays = useHip && animal.Has_Pivot_Chest;

#if UNITY_EDITOR
            MDebug.DrawWireSphere(t.position, Color.black, 0.05f * animal.ScaleFactor, 1);
#endif

            bool FrontRay = Physics.Raycast(frontPivot + up * lift, -up, out RaycastHit hit_Chest, rayLength, animal.GroundLayer, QueryTriggerInteraction.Ignore);
            RaycastHit hit_Hip = default;
            bool BackRay = twoRays && Physics.Raycast(animal.Pivot_Hip.World(t) + up * lift, -up, out hit_Hip, rayLength, animal.GroundLayer, QueryTriggerInteraction.Ignore);

            if (!FrontRay && !BackRay) return;   //Nothing to align to

#if UNITY_EDITOR
            if (FrontRay) MDebug.DrawWireSphere(hit_Chest.point, Color.green, 0.1f * animal.ScaleFactor);
            if (BackRay) MDebug.DrawWireSphere(hit_Hip.point, Color.green, 0.1f * animal.ScaleFactor);
#endif

            Vector3 SurfaceNormal;
            float hipDistance;

            if (FrontRay && BackRay)
            {
                //Use the line between both hits as the slope, so small bumps under one pivot do not tilt the Animal sideways
                Vector3 direction = (hit_Chest.point - hit_Hip.point).normalized;
                Vector3 Side = Vector3.Cross(animal.UpVector, direction).normalized;
                SurfaceNormal = Vector3.Cross(direction, Side).normalized;
                if (SurfaceNormal == Vector3.zero) SurfaceNormal = hit_Hip.normal;
                hipDistance = hit_Hip.distance - lift;
            }
            else
            {
                var hit = FrontRay ? hit_Chest : hit_Hip;
                SurfaceNormal = hit.normal;
                hipDistance = hit.distance - lift;
            }

            var rot = AlignRotation(animal, SurfaceNormal);
            var pos = AlignPosition(animal, hipDistance);

            t.SetPositionAndRotation(pos, rot);
        }

        /// <summary> Pulls the Animal towards the End Location by the curve value at the normalized clip time (1 = exactly on it) </summary>
        public void ApplyEndLocation(MAnimal animal, float normalizedTime)
        {
            if (EndLocation == null) return;

            float weight = EndLocationCurve != null && EndLocationCurve.length > 0 ?
                EndLocationCurve.Evaluate(normalizedTime) : normalizedTime;
            if (weight <= 0f) return;

            var t = animal.transform;
            var pos = Vector3.LerpUnclamped(t.position, EndLocation.position, weight);
            var rot = t.rotation;

            if (MatchRotation)
            {
                //Only the heading is matched, the ground alignment sets the tilt afterwards
                var up = t.up;
                var forward = Vector3.ProjectOnPlane(EndLocation.forward, up);
                if (forward.sqrMagnitude > 0.0001f)
                    rot = Quaternion.SlerpUnclamped(rot, Quaternion.LookRotation(forward, up), weight);
            }

            t.SetPositionAndRotation(pos, rot);
        }

        /// <summary> Returns the Animal rotation aligned to the ground normal </summary>
        public virtual Quaternion AlignRotation(MAnimal animal, Vector3 alignNormal)
        {
            Quaternion AlignRot = Quaternion.FromToRotation(animal.Up, alignNormal) * animal.Rotation;
            return AlignRot;
        }

        /// <summary> Returns the Animal position placed at its Height (+ Offset) over the ground </summary>
        public virtual Vector3 AlignPosition(MAnimal animal, float dist)
        {
            float difference = (animal.Height + Offset) - dist;
            Vector3 align = animal.Rotation * new Vector3(0, difference, 0);
            return animal.Position + align;
        }
    }
}
