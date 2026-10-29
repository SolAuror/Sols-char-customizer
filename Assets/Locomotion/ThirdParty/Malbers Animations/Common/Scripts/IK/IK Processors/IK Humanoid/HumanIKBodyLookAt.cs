using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.IK
{
    [System.Serializable]
    [AddTypeMenu("Humanoid/IK Body Look At")]
    public class HumanIKBodyLookAt : IKProcessorOnAnimIK
    {
        public override bool RequireTargets => false;

        [Header("Set LookAt Weights")]
        [Range(0, 1)] public float BodyWeight = 0.5f;
        [Range(0, 1)] public float HeadWeight = 1;
        [Range(0, 1)] public float EyesWeight = 1;
        [Range(0, 1)] public float ClampWeight = 0.75f;

        [Header("Extras")]
        public float Distance = 50f;
        public Vector2Reference offset = new();

        public override void OnAnimatorIK(IKSet set, Animator anim, int index, float weight)
        {
            if (set.aimer == null || weight <= 0) return;

            // 1. Calculate Target Point
            var dir = set.aimer.AimDirection;
            var origin = set.aimer.AimOrigin;
            dir = Quaternion.AngleAxis(offset.x, Vector3.up) * dir;
            var rightV = Vector3.Cross(dir, Vector3.up);
            dir = Quaternion.AngleAxis(offset.y, rightV) * dir;
            var point = origin.position + (dir * Distance);

            // 3. APPLY LookAt (This rotates Spine/Chest/Head)
            anim.SetLookAtWeight(weight, BodyWeight, HeadWeight, EyesWeight, ClampWeight);
            anim.SetLookAtPosition(point);
        }

        public override void Verify(IKManager manager, IKSet set, Animator animator, int index)
        {
            if (set.aimer == null)
                Debug.LogWarning($"The IK Set <B>[{set.Name}]</B> has no Aimer. [IK Processor: {name}] needs an Aimer", animator);
            else
                Debug.Log($"<B>[IK Processor: {name}][Humanoid - LookAt]</B>  <color=yellow>[OK]</color>");
        }
    }
}
