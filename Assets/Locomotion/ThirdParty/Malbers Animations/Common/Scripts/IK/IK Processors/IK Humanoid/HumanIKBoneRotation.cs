// Last Change: 9/19/2026 3:44pm
using UnityEngine;
using MalbersAnimations.Scriptables;




#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.IK
{
    [System.Serializable]
    [AddTypeMenu("Humanoid/IK Human Bone Offset <Rotation>")]
    public class HumanIKBoneRotation : IKProcessorOnAnimIK
    {
        public enum RotationOffsetType
        {
            [InspectorName("Local Rotation Additive")]
            LocalAdditive,
            [InspectorName("Local Rotation Override")]
            LocalOverride,
            [InspectorName("Root Relative Local Rotation Additive")]
            RootRelativeRotationAdditive,
            [InspectorName("Root Relative Local Rotation Override")]
            RootRelativeRotationOverride,
            [InspectorName("Rotation Relative to [Tag]")]
            WorldRotation,
        }

        public RotationOffsetType rotationType;
        [Hide(nameof(rotationType), (int)RotationOffsetType.WorldRotation)]
        public IKTag TargetTag;

        [SearcheableEnum] public HumanBodyBones humanBone;

        private Transform TargetWorldRotation;

        [Tooltip("Rotation Offset applied to the bone")]
        public Vector3Reference Offset;

        public override bool RequireTargets => false;

        // Weight blending lives in LateUpdate, not in the IK Pass. Probed on 2026-09-08 (Unity 6000.0.79f1, Synty humanoid):
        //  • Animator.SetBoneLocalRotation is an exact Transform-space write on the spine chain, neck, head and shoulders,
        //    but NOT on arms, legs and hips: the humanoid twist redistribution (Avatar Upper/Lower Arm/Leg Twist) re-solves
        //    those, so writing a limb its own rotation still moves it (7° upper arm, 20-30° legs, ~40° hips on that rig).
        //  • Every IK-Pass layer sees what the previous layer wrote, so this runs N times per tick on a rig with N IK-Pass layers.
        // Slerp(Bone.localRotation, final, weight) written through that call never reached the animated pose as the weight
        // went to 0: the pass wrote "animated pose + limb error" N times over, and when the weight hit exactly 0 the Set
        // stopped calling the Processor and the error vanished in one frame (the snap). The pass now writes the FULL
        // rotation and the weight is applied in LateUpdate, in Transform space, between the pose the first pass read and
        // the pose the IK Pass produced. The Additive types build that rotation from the cached animated pose too, so the
        // offset is applied once per tick instead of once per IK-Pass layer (it used to compound 4x on the Human rig).

        /// <summary>Animated local rotation of [Bone], read on the first IK Pass of the tick before anything wrote to it. </summary>
        private Quaternion AnimatedLocal;

        /// <summary>Tick [AnimatedLocal] was read on. </summary>
        private double CacheTick = -1;

        /// <summary>True when the IK Pass wrote the bone this tick, so LateUpdate has something to blend. </summary>
        private bool WroteThisTick;

        /// <summary>Tick the LateUpdate blend already ran on: a render frame without an Animator step must not blend twice. </summary>
        private double BlendTick = -1;

        /// <summary>Below this weight the pass does not write. The Set snaps its weight tail to 0 between the IK Pass and LateUpdate, and a skipped LateUpdate must never leave the full rotation on screen. </summary>
        private const float MinWeight = 0.01f;

        public override void Start(IKSet IKSet, Animator anim, int index)
        {
            Bone = anim.GetBoneTransform(humanBone);
            TargetWorldRotation = GetTarget(IKSet, TargetTag);
            CacheTick = -1; //A new character: the pose cached from the previous one is meaningless
        }

        public override void OnAnimatorIK(IKSet set, Animator anim, int index, float weight)
        {
            if (Bone == null) return;

            var root = anim.transform;
            var tick = set.CurrentTick;

            if (CacheTick != tick) //First IK-Pass layer of the tick: the bone still holds the animated pose
            {
                CacheTick = tick;
                WroteThisTick = false;
                AnimatedLocal = Bone.localRotation;
            }

            if (weight < MinWeight) return;

            var OffsetRot = Quaternion.Euler(Offset.Value);
            var InverseRot = Quaternion.Inverse(Bone.parent.rotation); //This is the Bone Rotation in world coordinates

            Quaternion finalRotation = Quaternion.identity;

            switch (rotationType)
            {
                case RotationOffsetType.LocalAdditive:
                    //The ANIMATED rotation, not the live one: a later IK-Pass layer already holds this pass's write, so the
                    //live read compounded the offset once per IK-Pass layer (4x on the Human rig). Once per tick now.
                    finalRotation = AnimatedLocal * OffsetRot;
                    break;
                case RotationOffsetType.LocalOverride:
                    finalRotation = OffsetRot;
                    break;
                case RotationOffsetType.WorldRotation:
                    if (TargetWorldRotation == null)
                    {
                        Debug.LogWarning($"<B>[IK Processor: {name}].</B>  Target failed in {TargetTag.name}");
                        Active = false;
                        return;
                    }
                    var TargetRelative = Quaternion.identity;
                    var Target = TargetWorldRotation;

                    if (Target != null)
                        TargetRelative = Target.rotation;

                    finalRotation = InverseRot * TargetRelative * OffsetRot;
                    break;
                case RotationOffsetType.RootRelativeRotationOverride:
                    finalRotation = InverseRot * root.rotation * OffsetRot;
                    break;

                case RotationOffsetType.RootRelativeRotationAdditive:
                    finalRotation = AnimatedLocal * root.rotation * OffsetRot; //Animated rotation, see LocalAdditive
                    break;
                default:
                    break;
            }

            if (!(System.Single.IsNaN(finalRotation.x) || System.Single.IsNaN(finalRotation.y) || System.Single.IsNaN(finalRotation.z)))
            {
                anim.SetBoneLocalRotation(humanBone, finalRotation); //Full rotation on every pass; the weight is applied in LateUpdate
                WroteThisTick = true;
            }
        }

        /// <summary>Applies the weight in Transform space, between the animated pose and the one the IK Pass produced (see the class comment). </summary>
        public override void LateUpdate(IKSet set, Animator anim, int index, float weight)
        {
            if (Bone == null || !WroteThisTick || weight >= 1) return; //Weight 1 is the IK Pass result untouched

            var tick = set.CurrentTick;

            if (CacheTick != tick || BlendTick == tick) return; //Nothing written this tick, or already blended

            BlendTick = tick;
            Bone.localRotation = Quaternion.Slerp(AnimatedLocal, Bone.localRotation, weight);
        }

        public override void Verify(IKManager manager, IKSet set, Animator animator, int index)
        {
            if (animator.GetBoneTransform(humanBone) == null)
            {
                Debug.LogWarning($"<B>[IK Processor: {name}].</B> The Bone [{humanBone}] is not valid on the Avatar");
                return;
            }
            if (rotationType == RotationOffsetType.WorldRotation && TargetTag == null)
            {
                Debug.LogWarning($"<B>[IK Processor: {name}].</B>  Needs a Target Tag");
                return;
            }

            Debug.Log($"<B>[IK Processor: {name}]</B>  <color=yellow>[OK]</color>");
        }



#if UNITY_EDITOR
        // The handle shows Base * Offset, the world rotation this Processor drives the bone to, and a drag is turned back
        // into Offset = Inv(Base) * handle. Base mirrors OnAnimatorIK with two rules:
        //  • the PARENT is read live: the local rotation this Processor writes ends up under the parent's FINAL pose (another
        //    Processor may override the parent, e.g. Spine under the Chest, and the LookAt Processors rotate the spine in
        //    LateUpdate), so a cached parent points the handle axes the wrong way;
        //  • the bone's OWN rotation is the animated one cached by the first IK Pass, never the live one, which already
        //    carries this offset (using it applied the offset twice and fed every drag back into itself).

        /// <summary>World rotation the bone was being driven to when the current handle drag started (see OnSceneGUI). </summary>
        private Quaternion m_RotateStartWorld = Quaternion.identity;

        /// <summary>Rotation fed to Handles.RotationHandle: the gizmo frame while idle, the handle's own last result during a drag. </summary>
        private Quaternion m_HandleRot = Quaternion.identity;

        /// <summary>World rotation that, multiplied by [Offset], is the rotation this Processor drives the bone to. Mirrors OnAnimatorIK. </summary>
        private Quaternion GetHandleBase(IKSet set, Animator animator)
        {
            var tick = set.CurrentTick;

            //The animated local rotation cached by this tick's IK Pass. When the Processor did not run this tick (weight 0)
            //the bone carries no offset and its live local rotation IS the animated one.
            var fresh = animator.updateMode == AnimatorUpdateMode.Fixed ? CacheTick == tick : tick - CacheTick <= 1;
            var local = fresh ? AnimatedLocal : Bone.localRotation;

            var parent = Bone.parent.rotation; //Final pose of the parent, see above
            var root = animator.transform.rotation;

            return rotationType switch
            {
                RotationOffsetType.LocalAdditive => parent * local,                              //local * Offset
                RotationOffsetType.LocalOverride => parent,                                      //Offset
                RotationOffsetType.WorldRotation => TargetWorldRotation != null ? TargetWorldRotation.rotation : parent, //Inv(parent) * Target * Offset
                RotationOffsetType.RootRelativeRotationOverride => root,                         //Inv(parent) * root * Offset
                RotationOffsetType.RootRelativeRotationAdditive => parent * local * root,        //local * root * Offset
                _ => Quaternion.identity,
            };
        }

        internal override void OnSceneGUI(IKSet set, Animator animator, UnityEngine.Object Target, int index)
        {
            if (!set.Gizmos || Bone == null) return;

            Handles.color = Color.yellow;
            Handles.SphereHandleCap(0, Bone.position, Quaternion.identity, 0.04f, EventType.Repaint);

            foreach (Transform child in Bone)
            {
                Handles.SphereHandleCap(0, child.position, Quaternion.identity, 0.02f, EventType.Repaint);
                Handles.DrawLine(Bone.position, child.position);
            }

            if (!Application.isPlaying || Tools.current != Tool.Rotate || Bone.parent == null) return;

            var Base = GetHandleBase(set, animator);
            var currentWorld = Base * Quaternion.Euler(Offset.Value); //Rotation this Processor drives the bone to

            // The two parts of Handles.RotationHandle report differently: the axis rings return the TOTAL rotation since the
            // drag began (they ignore the input while dragging), the center free-rotate returns the INCREMENT since the last
            // mouse event applied to the input. Feeding the handle its own previous result during a drag makes both of them
            // accumulate into "total rotation since the drag began", which is then applied to the rotation captured at drag
            // start. While idle (hotControl == 0) the frame is re-synced every repaint so the gizmo follows the bone.
            var local = Tools.pivotRotation == PivotRotation.Local;

            if (GUIUtility.hotControl == 0)
            {
                m_RotateStartWorld = currentWorld;
                m_HandleRot = local ? currentWorld : Quaternion.identity; //Gizmo frame: driven rotation in Local, world axes in Global
            }

            using (var cc = new EditorGUI.ChangeCheckScope())
            {
                var NewRotation = Handles.RotationHandle(m_HandleRot, Bone.position);

                if (cc.changed)
                {
                    m_HandleRot = NewRotation;

                    Undo.RecordObject(Target, "Change Rot");
                    var delta = local ? NewRotation * Quaternion.Inverse(m_RotateStartWorld) : NewRotation; //World-space total since drag start
                    var newWorld = delta * m_RotateStartWorld;
                    Offset.Value = (Quaternion.Inverse(Base) * newWorld).eulerAngles;
                    EditorUtility.SetDirty(Target);
                }
            }
        }
#endif
    }
}
