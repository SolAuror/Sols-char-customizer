// Last Change: 10/16/2026 9:45am
using MalbersAnimations.Scriptables;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.IK
{
    /// <summary> Moves the Humanoid Body Position (pelvis) by an offset. Use it to raise or lower the whole body, e.g. to fit characters of different heights on a ladder </summary>
    [System.Serializable]
    [AddTypeMenu("Humanoid/IK Body Position Offset")]
    public class HumanIKBodyPosition : IKProcessorOnAnimIK
    {
        /// <summary> Space the Offset is expressed in </summary>
        public enum OffsetSpace
        {
            [InspectorName("Root (Character Transform)")]
            Root,
            [InspectorName("Body (Body Rotation)")]
            Body,
            [InspectorName("World")]
            World,
        }

        [Tooltip("Space the Offset is expressed in. Root: relative to the character transform (Up = character up). Body: relative to the Animator Body Rotation. World: raw world axes")]
        public OffsetSpace space = OffsetSpace.Root;

        [Tooltip("Position Offset added to the Animator Body Position (the Humanoid pelvis). Y moves the whole body up or down")]
        public Vector3Reference Offset = new(Vector3.zero);

        public override bool RequireTargets => false;

        /// <summary> Tick the offset was already applied on. Body Position is a shared read-modify-write value, so it must be added once per tick, not once per IK-Pass layer </summary>
        private double m_LastTick = -1;

        /// <summary> Character transform, cached on Start </summary>
        private Transform m_Root;

        /// <summary> Hips bone, used only to draw the gizmo and the handle outside the IK Pass </summary>
        private Transform m_Hips;

        /// <summary> Caches the root and the hips. Disables itself on a non Humanoid Animator since Body Position does not exist there </summary>
        public override void Start(IKSet set, Animator anim, int index)
        {
            m_Root = anim.transform;
            m_LastTick = -1;

            if (!anim.isHuman)
            {
                Debug.LogWarning($"<B>[IK Processor: {name}][IK Body Position]</B> requires a Humanoid Animator. Disabling.", anim);
                Active = false;
                return;
            }

            m_Hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        }

        /// <summary> Adds the weighted offset to the Body Position, once per tick </summary>
        public override void OnAnimatorIK(IKSet set, Animator anim, int index, float weight)
        {
            if (m_Root == null) return;

            //Every IK-Pass layer sees what the previous one added, so only the first pass of the tick writes.
            var tick = set.CurrentTick;
            if (m_LastTick == tick) return;
            m_LastTick = tick;

            if (weight <= 0) return;

            anim.bodyPosition += GetWorldOffset(anim.bodyRotation) * (weight * OffsetScale(set)); //IK Manager Scale Multiplier
        }

        /// <summary> Offset rotated into world space according to [space] </summary>
        private Vector3 GetWorldOffset(Quaternion bodyRotation)
        {
            return space switch
            {
                OffsetSpace.Root => m_Root.rotation * Offset.Value,
                OffsetSpace.Body => bodyRotation * Offset.Value,
                _ => Offset.Value,
            };
        }

        /// <summary> Rotation of the space the Offset lives in, outside the IK Pass (Body uses the Hips bone) </summary>
        private Quaternion GetSpaceRotation(Animator anim, Transform hips)
        {
            return space switch
            {
                OffsetSpace.Root => anim.transform.rotation,
                OffsetSpace.Body => hips.rotation,
                _ => Quaternion.identity,
            };
        }

        /// <summary> Draws the hips and where the offset moves them </summary>
        public override void OnDrawGizmos(IKManager manager, IKSet IKSet, Animator anim, float weight)
        {
            if (!IKSet.Gizmos || anim == null || !anim.isHuman) return;

            var hips = m_Hips != null ? m_Hips : anim.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) return;

            var from = hips.position;
            var to = from + GetSpaceRotation(anim, hips) * (Offset.Value * OffsetScale(IKSet));

            Gizmos.color = Color.gray;
            Gizmos.DrawWireSphere(from, 0.04f);
            Gizmos.DrawLine(from, to);
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(to, 0.04f);
        }

#if UNITY_EDITOR
        /// <summary> Position handle on the offset body position, in the selected space </summary>
        internal override void OnSceneGUI(IKSet set, Animator animator, UnityEngine.Object Target, int index)
        {
            if (!set.Gizmos || animator == null || !animator.isHuman) return;
            if (Tools.current != Tool.Move) return;

            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null) return;

            var spaceRot = GetSpaceRotation(animator, hips);
            var handleRot = Tools.pivotRotation == PivotRotation.Local ? spaceRot : Quaternion.identity;
            var scale = OffsetScale(set); //Handle shows the scaled offset, a drag is divided back

            using (var cc = new EditorGUI.ChangeCheckScope())
            {
                var pos = hips.position + spaceRot * (Offset.Value * scale);
                var newPos = Handles.PositionHandle(pos, handleRot);

                if (cc.changed)
                {
                    Undo.RecordObject(Target, "Change Body Position Offset");
                    Offset.Value = Quaternion.Inverse(spaceRot) * (newPos - hips.position) / scale;
                    EditorUtility.SetDirty(Target);
                }
            }
        }
#endif

        /// <summary> Checks the Animator is Humanoid </summary>
        public override void Verify(IKManager manager, IKSet set, Animator animator, int index)
        {
            if (animator == null || !animator.isHuman)
            {
                Debug.LogWarning($"<B>[IK Processor: {name}][IK Body Position]</B> <color=red>[Requires a Humanoid Animator]</color>", animator);
                return;
            }

            Debug.Log($"<B>[IK Processor: {name}][IK Body Position]</B>  <color=yellow>[OK]</color>");
        }
    }
}
