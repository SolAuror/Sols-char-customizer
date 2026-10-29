using System;
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations.IK
{
    [Serializable, AddTypeMenu("Humanoid/Search For Tag")]
    public class SearchForTag : IKProcessor
    {
        public override bool RequireTargets => false; // This processor does not require specific targets

        [Tooltip("Target limb to lock using IK.")]
        [SerializeField] AvatarIKGoal avatarIKGoal;

        [Tooltip("LayerMask used for detecting valid IK target layers.")]
        [SerializeField] LayerMask detectionLayer;

        [Tooltip("The tag used to identify valid IK targets.")]
        [SerializeField] Tag iKTargetTag;

        [Tooltip("If true, use the transform-based method for IK target detection, otherwise use raycast-based detection.")]
        [SerializeField] bool useTransform;

        [Hide("useTransform", false)]
        [Tooltip("Radius of the SphereCast to detect IK targets.")]
        [Min(0), SerializeField] float detectionRadius = 0.2f;

        [Hide("useTransform", true)]
        [Tooltip("The angle by which we rotate the forward vector when determining the direction of the raycast.")]
        [SerializeField] float detectionAngle = 45f;

        [Hide("useTransform", true)]
        [Tooltip("The maximum length of the sphere cast or raycast used to detect targets.")]
        [Min(0), SerializeField] float castLength = 0.7f;

        [Tooltip("Offset to apply to the IK target's position.")]
        [SerializeField] Vector3 positionOffset;

        [Tooltip("Enable or disable the rotation adjustment for the IK target.")]
        [SerializeField] bool enableRotation;

        [Hide("enableRotation", false)]
        [Tooltip("Optional offset to apply to the IK target's rotation.")]
        [SerializeField] Vector3 rotationOffset;

        private readonly Transform[] iKTargets;
        private Transform iKTarget;
        private Vector3 ikHitPoint;
        private Transform bodyPart;

        public override void Verify(IKManager manager, IKSet set, Animator animator, int index)
        {
            if (iKTargetTag == null)
            {
                Debug.LogWarning($"<B>[IK Processor: {name}][SearchForTag]</B>  <color=red>[No Tag defined]</color>");
                return;
            }
            Debug.Log($"<B>[IK Processor: {name}][SearchForTag]</B>  <color=green>[OK]</color>");
        }

        public override void Start(IKSet set, Animator anim, int index)
        {
            // Cache the RootBone in the local variables 
            switch (avatarIKGoal)
            {
                case AvatarIKGoal.LeftFoot:
                    Bone = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                    break;
                case AvatarIKGoal.RightFoot:
                    Bone = anim.GetBoneTransform(HumanBodyBones.RightFoot);
                    break;
                case AvatarIKGoal.LeftHand:
                    Bone = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                    break;
                case AvatarIKGoal.RightHand:
                    Bone = anim.GetBoneTransform(HumanBodyBones.RightHand);
                    break;
                default:
                    break;
            }
        }

        public override void OnAnimatorIK(IKSet IKSet, Animator anim, int index, float weight, int LayerIndex)
        {
            bodyPart = Bone;

            if (useTransform)
            {
                GetIKTargetTransform(anim, iKTargets); // Find IK targets based on Spherecast

                if (iKTarget != null)
                {
                    ApplyIKPositionRotation(anim, iKTarget.position, weight);
                }
            }
            else
            {
                GetIKTargetHitPoint(anim); // Find IK targets based on Raycast

                if (ikHitPoint != Vector3.zero)
                {
                    ApplyIKPositionRotation(anim, ikHitPoint, weight);
                }
            }
        }

        private void ApplyIKPositionRotation(Animator anim, Vector3 position, float weight)
        {
            Vector3 finalPosition = position + anim.transform.TransformDirection(positionOffset);
            anim.SetIKPositionWeight(avatarIKGoal, weight);
            anim.SetIKPosition(avatarIKGoal, finalPosition);

            if (enableRotation)
            {
                Quaternion finalRotation = anim.transform.rotation * Quaternion.Euler(rotationOffset);

                anim.SetIKRotationWeight(avatarIKGoal, weight);
                anim.SetIKRotation(avatarIKGoal, finalRotation);
            }
        }


        public void GetIKTargetHitPoint(Animator anim)
        {
            // Define a direction by rotating the forward vector by detectionAngle degrees around the Y-axis
            Vector3 direction = Quaternion.AngleAxis(detectionAngle, Vector3.up) * anim.transform.forward;

            if (Physics.Raycast(bodyPart.position, direction, out RaycastHit hit, castLength, detectionLayer))
            {
                if (hit.transform.HasMalbersTag(iKTargetTag))
                {
                    ikHitPoint = hit.point;
                }
            }
            else
            {
                ikHitPoint = Vector3.zero;
            }
        }

        public void GetIKTargetTransform(Animator anim, Transform[] IKTargets)
        {
            IKTargets = FindIKTargetsUsingSphereCast(bodyPart, iKTargetTag, detectionRadius, detectionLayer);

            if (IKTargets == null || IKTargets.Length == 0)
            {
                iKTarget = null;
                return;
            }

            iKTarget = anim.transform.NearestTransform(IKTargets);
        }

        public Transform[] FindIKTargetsUsingSphereCast(Transform parent, Tag tag, float radius, LayerMask detectionLayer)
        {
            List<Transform> targets = new();

            RaycastHit[] hits = Physics.SphereCastAll(parent.position, radius, parent.forward, castLength, detectionLayer);

            if (tag != null)
            {
                //Walk the tagged objects instead of every child of every hit: no array allocation and no ancestor false positives
                foreach (RaycastHit hit in hits)
                    foreach (var go in tag.gameObjects)
                        if (go != null && go.transform.SameHierarchy(hit.transform) && !targets.Contains(go.transform))
                            targets.Add(go.transform);
            }
            return targets.ToArray();
        }

        public override void OnDrawGizmos(IKManager manager, IKSet IKSet, Animator anim, float weight)
        {
            if (bodyPart == null) return;

            if (useTransform)
            {
                Vector3 sphereCastOrigin = bodyPart.position;

                // Perform a SphereCast and visualize hits
                RaycastHit[] hits = Physics.SphereCastAll(sphereCastOrigin, detectionRadius, bodyPart.forward, castLength, detectionLayer);
                if (hits.Length > 0)
                {
                    foreach (RaycastHit hit in hits)
                    {
                        Gizmos.color = Color.green;
                        Gizmos.DrawLine(sphereCastOrigin, hit.transform.position);// Draw Line to the hit point
                    }
                }
                else
                {
                    Gizmos.color = Color.white; // No hit
                }
                Gizmos.DrawWireSphere(sphereCastOrigin, detectionRadius);
            }
            else
            {
                // Define a direction by rotating the forward vector by detectionAngle degrees around the Y-axis
                Vector3 rayDirection = Quaternion.AngleAxis(detectionAngle, Vector3.up) * anim.transform.forward;

                Vector3 rayOrigin = bodyPart.position;

                if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, castLength, detectionLayer))
                {
                    Gizmos.color = Color.green;
                    Gizmos.DrawSphere(hit.point, 0.05f);  // Small sphere at the hit point
                }
                else
                {
                    Gizmos.color = Color.white; // No hit
                }
                Gizmos.DrawLine(rayOrigin, rayOrigin + rayDirection * castLength);
            }
        }
    }
}

