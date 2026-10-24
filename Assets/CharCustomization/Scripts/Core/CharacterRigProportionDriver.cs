using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
                                                                                                    // Rig proportion and grounding driver.
    
                                                                                                    // The proportion ratios and foot-grounding strategy are adapted from the
                                                                                                    // BodyMorphLite reference implementation by Serhat Dikel, used here as an
                                                                                                    // MIT-licensed behavioral reference. CharacterEditor keeps its own stable
                                                                                                    // morph catalog, bind-pose restore, active-root rebinding, and Animator IK
                                                                                                    // bridge instead of copying BML's component lifecycle directly.

    public sealed class CharacterRigProportionDriver : MonoBehaviour
    {
        private const float WidthOffsetRange = 0.35f;
        private const int RightFootIndex = 0;
        private const int LeftFootIndex = 1;

        private static readonly HumanBodyBones[] RequiredHumanoidBones =
        {
            HumanBodyBones.Hips,
            HumanBodyBones.Spine,
            HumanBodyBones.LeftShoulder,
            HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightFoot
        };

        [Header("Rig Profile")]
        [SerializeField] private CharacterRigProportionProfile proportionProfile;

        [Header("Inspector BML Test Controls")]
        [SerializeField] private bool useInspectorBmlControls;
        [SerializeField, Range(-1f, 1f)] private float heightInput;
        [SerializeField, Range(-1f, 1f)] private float upperBodyInput;
        [SerializeField, Range(-1f, 1f)] private float lowerBodyInput;
        [SerializeField, Range(-1f, 1f)] private float headInput;
        [SerializeField, Range(-1f, 1f)] private float neckInput;
        [SerializeField, Range(-1f, 1f)] private float waistInput;
        [SerializeField, Range(-1f, 1f)] private float chestInput;
        [SerializeField, Range(-1f, 1f)] private float spineInput;
        [SerializeField, Range(-1f, 1f)] private float shouldersInput;
        [SerializeField, Range(-1f, 1f)] private float upperArmsInput;
        [SerializeField, Range(-1f, 1f)] private float lowerArmsInput;
        [SerializeField, Range(-1f, 1f)] private float handsInput;
        [SerializeField, Range(-1f, 1f)] private float fingersInput;
        [SerializeField, Range(-1f, 1f)] private float legsInput;
        [SerializeField, Range(-1f, 1f)] private float feetInput;
        [SerializeField, Range(-1f, 1f)] private float footRadiusInput;
        [SerializeField, Range(-1f, 1f)] private float shoulderWidthInput;
        [SerializeField, Range(-1f, 1f)] private float hipsWidthInput;

        [Header("Optional Grounding")]
        [SerializeField] private bool enableFootIkGrounding;
        [SerializeField, Min(0.01f)] private float footProbeRadius = 0.05f;
        [SerializeField, Min(0.01f)] private float footProbeHeight = 0.5f;
        [SerializeField, Min(0.01f)] private float minFootProbeHeight = 0.18f;
        [SerializeField, Min(0f)] private float pelvisOffsetSpeed = 4f;
        [SerializeField, Min(0f)] private float footIkAdaptSpeed = 6f;
        [SerializeField, Range(0f, 1f)] private float pelvisIkWeight = 0.75f;
        [SerializeField, Min(0f)] private float footRotationSpeed = 90f;
        [SerializeField] private LayerMask groundingLayers = 1;
        [SerializeField] private bool warnWhenIkBridgeMissing = true;

        [Header("Update")]
        [Tooltip("Reapplies proportions every frame instead of only after edits. Needed only when something else writes bone scales or the edited bone positions, such as clips with scale curves. Translation DoF Avatars enable this automatically.")]
        [SerializeField] private bool reapplyPoseEveryFrame;

        [Header("Humanoid Avatar")]
        [Tooltip("Rebuilds the humanoid Avatar from the morphed skeleton so retargeting and IK use the edited limb lengths.")]
        [SerializeField] private bool rebuildAvatarOnSkeletalChange = true;
        [Tooltip("Seconds without skeletal edits before the Avatar is rebuilt. Rebuilding rebinds the Animator, so it is deferred while sliders are dragged.")]
        [SerializeField, Min(0f)] private float avatarRebuildDelay = 0.25f;

        [Header("Grounding Debug")]
        [SerializeField] private bool enableGroundingDebugDraw;
        [SerializeField] private Color probePathColor = new(0.25f, 0.6f, 1f, 1f);
        [SerializeField] private Color hitPointColor = new(0.2f, 1f, 0.35f, 1f);
        [SerializeField] private Color missColor = new(1f, 0.35f, 0.25f, 1f);
        [SerializeField] private Color footTargetColor = new(1f, 0.85f, 0.2f, 1f);
        [SerializeField] private Color normalColor = new(0.75f, 0.35f, 1f, 1f);
        [SerializeField] private Color pelvisOffsetColor = new(1f, 0.45f, 0.85f, 1f);

                                                                                                    // Keyed by morph ID rather than channel: multiple catalog morphs can
                                                                                                    // share one rig channel (visible body.shoulder_width and the hidden
                                                                                                    // BML-compatible rig.shoulders both drive Shoulders), and a full recipe
                                                                                                    // application must not let a later zero wipe an earlier value.
        private readonly Dictionary<string, ChannelContribution> channelValues = new(StringComparer.Ordinal);
        private readonly Dictionary<HumanBodyBones, BonePose> bindPose = new();
        private readonly Transform[] footTransforms = new Transform[2];
        private readonly Vector3[] footIkPositions = new Vector3[2];
        private readonly Vector3[] footIkNormals = { Vector3.up, Vector3.up };
        private readonly Quaternion[] footSlopeRotations = { Quaternion.identity, Quaternion.identity };
        private readonly Quaternion[] lastFootSlopeRotations = { Quaternion.identity, Quaternion.identity };
        private readonly bool[] hasLastFootRotation = new bool[2];
        private readonly float[] lastFootHeights = new float[2];
        private readonly bool[] footGrounded = new bool[2];
        private readonly GroundingProbeState[] groundingProbeStates = { new(), new() };

        private Transform activeRoot;
        private Animator activeAnimator;
        private CharacterRigAnimatorIkBridge activeIkBridge;
        private float currentPelvisOffset;
        private float groundingWeight;
        private float rootVelocity = 1f;
        private Vector3 lastRootPosition;
        private bool hasLastRootPosition;
        private float morphGroundOffset;
        private float ankleHeight;
        private bool hasAnkleHeight;
        private Vector3 rootBindScale = Vector3.one;
        private bool hasRootBindScale;
        private float currentFootRadiusScale = 1f;
        private float currentFeetInputScale = 1f;
        private bool warnedMissingIkBridge;

        // Per-bone edits from the last ApplyNow, relative to bind pose. The Avatar
        // rebuild bakes these into the skeleton description.
        private readonly Dictionary<HumanBodyBones, float> appliedBoneScales = new();
        private readonly Dictionary<Transform, Vector3> appliedPositionOffsets = new();
        private CharacterJointOffsetTable jointOffsetTable;
        private CharacterJointOffsetBinding jointOffsets;
        private string jointOffsetError;
        private Avatar sourceAvatar;
        private Avatar runtimeAvatar;
        private bool avatarDirty;
        private bool avatarMatchesSkeleton;
        private bool poseDirty = true;
        private bool animatorWritesEditedBonePositions;
        private float lastSkeletalEditTime;

        public bool CanApply => activeRoot != null && activeAnimator != null && activeAnimator.isHuman && bindPose.Count > 0;
        public bool HasAnimatorIkBridge => activeIkBridge != null;
        public bool EnableFootIkGrounding
        {
            get => enableFootIkGrounding;
            set => enableFootIkGrounding = value;
        }

        public bool RebuildAvatarOnSkeletalChange
        {
            get => rebuildAvatarOnSkeletalChange;
            set => rebuildAvatarOnSkeletalChange = value;
        }

        public bool IsAvatarRebuildPending => avatarDirty && rebuildAvatarOnSkeletalChange;
        public bool IsPoseDirty => poseDirty;
        public bool ReapplyPoseEveryFrame
        {
            get => reapplyPoseEveryFrame;
            set => reapplyPoseEveryFrame = value;
        }

        public CharacterJointOffsetBinding JointOffsets => jointOffsets;
        public Avatar SourceAvatar => sourceAvatar;
        public Avatar RuntimeAvatar => runtimeAvatar;
        public float GroundingWeight => groundingWeight;
        public float MorphGroundOffset => morphGroundOffset;

        public void Bind(Transform root, Animator animator)
        {
            Bind(root, animator, null);
        }

        // jointOffsetsTable: optional .solrig data that moves joints with the active
        // character's blendshapes. See CharacterJointOffsetBinding.
        public void Bind(Transform root, Animator animator, CharacterJointOffsetTable jointOffsetsTable)
        {
            if (activeRoot == root && activeAnimator == animator && jointOffsetTable == jointOffsetsTable)
            {
                return;
            }

            // Restoring the Avatar can evaluate the Animator, which runs the IK bridge
            // and reapplies proportions, so the bind pose is restored after it.
            RestoreSourceAvatar();
            if (activeIkBridge != null)
            {
                activeIkBridge.Bind(null);
            }

            RestoreBindPose();
            jointOffsets?.Restore();
            jointOffsets = null;
            jointOffsetError = null;

            activeRoot = root;
            activeAnimator = animator;
            jointOffsetTable = jointOffsetsTable;
            sourceAvatar = activeAnimator != null ? activeAnimator.avatar : null;

            // With translation DoF the Animator writes shoulder and upper-leg positions
            // every frame, overwriting the width offsets unless they are reapplied.
            animatorWritesEditedBonePositions = sourceAvatar != null && sourceAvatar.isHuman &&
                                                sourceAvatar.humanDescription.hasTranslationDoF;
            activeIkBridge = activeAnimator != null
                ? activeAnimator.GetComponent<CharacterRigAnimatorIkBridge>()
                : null;
            if (activeIkBridge != null)
            {
                activeIkBridge.Bind(this);
            }

            channelValues.Clear();
            ResetGroundingState();
            CaptureBindPose();
            BindJointOffsets();
            ApplyNow();
            MarkSkeletonDirty();
        }

        // Called when the active character's blendshape weights change, so joints
        // follow the reshaped mesh on the next pose application.
        public void MarkJointOffsetsDirty()
        {
            if (jointOffsets == null)
            {
                return;
            }

            jointOffsets.MarkWeightsDirty();
            MarkSkeletonDirty();
        }

        public void SetMorph(CharacterMorphDefinition definition, float value)
        {
            if (definition == null || !definition.IsSkeletalDriven)
            {
                return;
            }

            float clampedValue = Mathf.Clamp(value, definition.MinimumValue, 1f);
            if (definition.RigChannel != CharacterRigProportionChannel.None)
            {
                bool changed = !channelValues.TryGetValue(definition.Id, out ChannelContribution previous) ||
                               !Mathf.Approximately(previous.Value, clampedValue);
                channelValues[definition.Id] = new ChannelContribution(definition.RigChannel, clampedValue);
                if (changed)
                {
                    MarkSkeletonDirty();
                }
            }

            ApplyNow();
        }

        public void ResetMorph(CharacterMorphDefinition definition)
        {
            if (definition == null || definition.RigChannel == CharacterRigProportionChannel.None)
            {
                return;
            }

            channelValues[definition.Id] = new ChannelContribution(definition.RigChannel, 0f);
            MarkSkeletonDirty();
            ApplyNow();
        }

        // Bone scales and the edited bone positions are not written by humanoid
        // animation, so a clean pose only needs reapplying after an edit.
        public bool ApplyIfDirty()
        {
            if (!poseDirty && !reapplyPoseEveryFrame && !animatorWritesEditedBonePositions)
            {
                return false;
            }

            ApplyNow();
            return !poseDirty;
        }

        public void ApplyNow()
        {
            if (!CanApply)
            {
                poseDirty = true;
                return;
            }

            poseDirty = false;

            RestoreBindPose();
            appliedPositionOffsets.Clear();
            ApplyJointOffsets();
            ApplyBodyMorphLiteScales();

            ApplySideOffset(HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, 
                            GetChannelValue(CharacterRigProportionChannel.ShoulderWidth));

            ApplySideOffset(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, 
                            GetChannelValue(CharacterRigProportionChannel.HipsWidth));

            UpdateMorphGroundOffset();
        }

        private void BindJointOffsets()
        {
            if (jointOffsetTable == null || activeRoot == null)
            {
                return;
            }

            Transform hips = activeAnimator != null && activeAnimator.isHuman
                ? activeAnimator.GetBoneTransform(HumanBodyBones.Hips)
                : null;
            if (CharacterJointOffsetBinding.TryCreate(jointOffsetTable, activeRoot, hips, out jointOffsets, out string error))
            {
                return;
            }

            jointOffsetError = $"Joint offsets from '{jointOffsetTable.name}' are disabled. {error}";
            Debug.LogWarning(jointOffsetError, this);
        }

        private void ApplyJointOffsets()
        {
            if (jointOffsets == null)
            {
                return;
            }

            jointOffsets.Evaluate();
            jointOffsets.ApplyLocalPositions();
            foreach (KeyValuePair<Transform, Vector3> entry in jointOffsets.LocalOffsets)
            {
                appliedPositionOffsets[entry.Key] = entry.Value;
            }
        }

        public bool RebuildAvatarNow()
        {
            avatarDirty = false;
            avatarMatchesSkeleton = false;
            if (!CanApply || sourceAvatar == null || !sourceAvatar.isValid || !sourceAvatar.isHuman)
            {
                return false;
            }

            ApplyNow();
            if (!HasSkeletalEdits())
            {
                AssignAvatar(sourceAvatar);
                DestroyRuntimeAvatar();
                avatarMatchesSkeleton = true;
                UpdateMorphGroundOffset();
                jointOffsets?.Settle();
                return true;
            }

            if (!TryBuildMorphedDescription(out HumanDescription description, out string error))
            {
                Debug.LogWarning($"Could not rebuild the humanoid Avatar for '{activeAnimator.name}'. {error}", activeAnimator);
                return false;
            }

            Avatar built = AvatarBuilder.BuildHumanAvatar(activeAnimator.gameObject, description);
            if (built == null || !built.isValid || !built.isHuman)
            {
                Debug.LogWarning(
                    $"AvatarBuilder produced an invalid humanoid Avatar for '{activeAnimator.name}'. Keeping the previous Avatar.",
                    activeAnimator);
                DestroyAvatar(built);
                return false;
            }

            built.name = $"{sourceAvatar.name} (Morphed)";
            built.hideFlags = HideFlags.DontSave;

            Avatar previousRuntimeAvatar = runtimeAvatar;
            runtimeAvatar = built;
            AssignAvatar(built);
            DestroyAvatar(previousRuntimeAvatar);
            avatarMatchesSkeleton = true;
            UpdateMorphGroundOffset();
            jointOffsets?.Settle();
            return true;
        }

        public bool ValidateBinding(out string error)
        {
            if (activeRoot == null)
            {
                error = "No active character root is bound.";
                return false;
            }

            if (activeAnimator == null)
            {
                error = $"'{activeRoot.name}' has no Animator bound for skeletal morphs.";
                return false;
            }

            if (!activeAnimator.isHuman)
            {
                error = $"'{activeAnimator.name}' must use a humanoid Avatar for skeletal morphs.";
                return false;
            }

            for (int index = 0; index < RequiredHumanoidBones.Length; index++)
            {
                HumanBodyBones bone = RequiredHumanoidBones[index];
                if (activeAnimator.GetBoneTransform(bone) == null)
                {
                    error = $"'{activeAnimator.name}' is missing required humanoid bone {bone}.";
                    return false;
                }
            }

            if (rebuildAvatarOnSkeletalChange &&
                (sourceAvatar == null || sourceAvatar.humanDescription.skeleton == null ||
                 sourceAvatar.humanDescription.skeleton.Length == 0))
            {
                error = $"'{activeAnimator.name}' has no Avatar skeleton description, so the Avatar cannot be rebuilt after skeletal morphs.";
                return false;
            }

            if (jointOffsetError != null)
            {
                error = jointOffsetError;
                return false;
            }

            if (enableFootIkGrounding && activeIkBridge == null)
            {
                error = $"Foot IK grounding is enabled, but '{activeAnimator.name}' has no {nameof(CharacterRigAnimatorIkBridge)} component.";
                return false;
            }

            error = null;
            return true;
        }

        private void FixedUpdate()
        {
            if (!enableFootIkGrounding || activeRoot == null)
            {
                groundingWeight = Mathf.MoveTowards(groundingWeight, 0f, 10f * Time.fixedDeltaTime);
                hasLastRootPosition = false;
                return;
            }

            float deltaTime = Mathf.Max(Time.fixedDeltaTime, 0.0001f);
            if (!hasLastRootPosition)
            {
                lastRootPosition = activeRoot.position;
                hasLastRootPosition = true;
            }

            rootVelocity = Mathf.Max((lastRootPosition - activeRoot.position).magnitude / deltaTime, 1f);
            lastRootPosition = activeRoot.position;

            bool anyFootGrounded = footGrounded[RightFootIndex] || footGrounded[LeftFootIndex];
            float targetWeight = anyFootGrounded ? 1f : 0f;
            float adaptRate = anyFootGrounded ? footIkAdaptSpeed * rootVelocity : 10f * rootVelocity;
            groundingWeight = Mathf.MoveTowards(groundingWeight, targetWeight, adaptRate * deltaTime);
        }

        private void LateUpdate()
        {
            if (IsAvatarRebuildPending && Time.unscaledTime - lastSkeletalEditTime >= avatarRebuildDelay)
            {
                RebuildAvatarNow();
            }

            if (enableFootIkGrounding && activeIkBridge != null)
            {
                return;
            }

            ApplyIfDirty();
            if (enableFootIkGrounding || enableGroundingDebugDraw)
            {
                if (enableFootIkGrounding)
                {
                    WarnIfGroundingBridgeMissing();
                }

                UpdateFootTargets(false);
            }
        }

        internal void ApplyAnimatorIk(int layerIndex)
        {
            if (!enableFootIkGrounding || activeAnimator == null || activeRoot == null)
            {
                return;
            }

            ApplyIfDirty();
            UpdateFootTargets(true);
            ApplyPelvisHeight();
            ApplyFootIk(AvatarIKGoal.RightFoot, RightFootIndex);
            ApplyFootIk(AvatarIKGoal.LeftFoot, LeftFootIndex);
        }

        private void OnAnimatorIK(int layerIndex)
        {
            ApplyAnimatorIk(layerIndex);
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                MarkSkeletonDirty();
                ApplyNow();
            }
        }

        private void OnDestroy()
        {
            // A living character keeps its morphed body and Avatar: host games may
            // destroy the customization manager once the character is finalized.
            if (activeAnimator == null || activeAnimator.avatar != runtimeAvatar)
            {
                DestroyRuntimeAvatar();
            }
        }

        private void MarkSkeletonDirty()
        {
            poseDirty = true;
            avatarDirty = true;
            avatarMatchesSkeleton = false;
            lastSkeletalEditTime = Time.unscaledTime;
        }

        private bool HasSkeletalEdits()
        {
            foreach (float scale in appliedBoneScales.Values)
            {
                if (Mathf.Abs(scale - 1f) > 0.0001f)
                {
                    return true;
                }
            }

            foreach (Vector3 offset in appliedPositionOffsets.Values)
            {
                if (offset.sqrMagnitude > 0.00000001f)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryBuildMorphedDescription(out HumanDescription description, out string error)
        {
            description = sourceAvatar.humanDescription;
            if (description.skeleton == null || description.skeleton.Length == 0)
            {
                error = "The source Avatar has no skeleton description.";
                return false;
            }

            var bonesByName = new Dictionary<string, HumanBodyBones>(StringComparer.Ordinal);
            foreach (KeyValuePair<HumanBodyBones, BonePose> entry in bindPose)
            {
                if (entry.Value.Transform != null)
                {
                    bonesByName[entry.Value.Transform.name] = entry.Key;
                }
            }

            var positionOffsetsByName = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            foreach (KeyValuePair<Transform, Vector3> entry in appliedPositionOffsets)
            {
                if (entry.Key != null)
                {
                    positionOffsetsByName[entry.Key.name] = entry.Value;
                }
            }

            // Skeleton positions are T-pose locals in parent space, the same space as
            // the joint and side offsets, so edits are applied as deltas and the
            // Avatar's own T-pose rotations are preserved.
            // Entries are rebuilt from their public fields rather than copied. Imported
            // entries carry a hidden parent name, and once the root entry is renamed
            // below the children would still name the old root and fail the build.
            // Without it, AvatarBuilder resolves parents from the live hierarchy.
            SkeletonBone[] source = description.skeleton;
            var skeleton = new SkeletonBone[source.Length];
            for (int index = 0; index < skeleton.Length; index++)
            {
                skeleton[index] = new SkeletonBone
                {
                    name = source[index].name,
                    position = source[index].position,
                    rotation = source[index].rotation,
                    scale = source[index].scale
                };

                if (skeleton[index].name == null)
                {
                    continue;
                }

                if (bonesByName.TryGetValue(skeleton[index].name, out HumanBodyBones bone) &&
                    appliedBoneScales.TryGetValue(bone, out float scale))
                {
                    skeleton[index].scale *= scale;
                }

                if (positionOffsetsByName.TryGetValue(skeleton[index].name, out Vector3 offset))
                {
                    skeleton[index].position += offset;
                }
            }

            // Model import records the root as it was named during import, such as
            // "sk_f_human(Clone)". The scene instance is usually renamed, so map the
            // unmatched root entry onto the Animator object.
            GameObject avatarRoot = activeAnimator.gameObject;
            if (!string.Equals(skeleton[0].name, avatarRoot.name, StringComparison.Ordinal) &&
                FindInHierarchy(avatarRoot.transform, skeleton[0].name) == null)
            {
                skeleton[0].name = avatarRoot.name;
            }

            description.skeleton = skeleton;
            error = null;
            return true;
        }

        private static Transform FindInHierarchy(Transform root, string name)
        {
            if (string.Equals(root.name, name, StringComparison.Ordinal))
            {
                return root;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                Transform match = FindInHierarchy(root.GetChild(index), name);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private void AssignAvatar(Avatar avatar)
        {
            if (activeAnimator == null || activeAnimator.avatar == avatar)
            {
                return;
            }

            // Changing the Avatar rebinds the Animator, which resets its state machine
            // and parameters. Carry them across so the preview does not restart.
            AnimatorStateSnapshot snapshot = AnimatorStateSnapshot.Capture(activeAnimator);
            activeAnimator.avatar = avatar;
            activeAnimator.Rebind();

            // Proportions are no longer reapplied every frame, so restore them
            // explicitly in case the rebind reset any bone the driver edits.
            ApplyNow();
            snapshot.Restore(activeAnimator);
        }

        private void RestoreSourceAvatar()
        {
            if (activeAnimator != null && sourceAvatar != null)
            {
                AssignAvatar(sourceAvatar);
            }

            DestroyRuntimeAvatar();
            sourceAvatar = null;
            avatarDirty = false;
            avatarMatchesSkeleton = false;
        }

        private void DestroyRuntimeAvatar()
        {
            DestroyAvatar(runtimeAvatar);
            runtimeAvatar = null;
        }

        private static void DestroyAvatar(Avatar avatar)
        {
            if (avatar == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(avatar);
            }
            else
            {
                DestroyImmediate(avatar);
            }
        }

        private void WarnIfGroundingBridgeMissing()
        {
            if (!warnWhenIkBridgeMissing || warnedMissingIkBridge || activeAnimator == null || activeIkBridge != null)
            {
                return;
            }

            warnedMissingIkBridge = true;
            Debug.LogWarning(
                $"Foot IK grounding is enabled, but '{activeAnimator.name}' has no {nameof(CharacterRigAnimatorIkBridge)} component.",
                activeAnimator);
        }

        private void CaptureBindPose()
        {
            bindPose.Clear();
            appliedBoneScales.Clear();
            appliedPositionOffsets.Clear();
            Array.Clear(footTransforms, 0, footTransforms.Length);
            hasAnkleHeight = false;
            ankleHeight = 0f;
            hasRootBindScale = activeRoot != null;
            rootBindScale = hasRootBindScale ? activeRoot.localScale : Vector3.one;
            if (activeAnimator == null || !activeAnimator.isHuman)
            {
                return;
            }

            AddBone(HumanBodyBones.Hips);
            AddBone(HumanBodyBones.Spine);
            AddBone(HumanBodyBones.Chest);
            AddBone(HumanBodyBones.UpperChest);
            AddBone(HumanBodyBones.Neck);
            AddBone(HumanBodyBones.Head);
            AddBone(HumanBodyBones.LeftShoulder);
            AddBone(HumanBodyBones.RightShoulder);
            AddBone(HumanBodyBones.LeftUpperArm);
            AddBone(HumanBodyBones.RightUpperArm);
            AddBone(HumanBodyBones.LeftLowerArm);
            AddBone(HumanBodyBones.RightLowerArm);
            AddBone(HumanBodyBones.LeftHand);
            AddBone(HumanBodyBones.RightHand);
            AddBone(HumanBodyBones.LeftThumbProximal);
            AddBone(HumanBodyBones.RightThumbProximal);
            AddBone(HumanBodyBones.LeftIndexProximal);
            AddBone(HumanBodyBones.RightIndexProximal);
            AddBone(HumanBodyBones.LeftMiddleProximal);
            AddBone(HumanBodyBones.RightMiddleProximal);
            AddBone(HumanBodyBones.LeftRingProximal);
            AddBone(HumanBodyBones.RightRingProximal);
            AddBone(HumanBodyBones.LeftLittleProximal);
            AddBone(HumanBodyBones.RightLittleProximal);
            AddBone(HumanBodyBones.LeftUpperLeg);
            AddBone(HumanBodyBones.RightUpperLeg);
            AddBone(HumanBodyBones.LeftLowerLeg);
            AddBone(HumanBodyBones.RightLowerLeg);
            AddBone(HumanBodyBones.LeftFoot);
            AddBone(HumanBodyBones.RightFoot);

            footTransforms[RightFootIndex] = activeAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            footTransforms[LeftFootIndex] = activeAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (footTransforms[RightFootIndex] != null && activeRoot != null)
            {
                ankleHeight = footTransforms[RightFootIndex].position.y - activeRoot.position.y;
                hasAnkleHeight = true;
            }
        }

        private void AddBone(HumanBodyBones bone)
        {
            Transform boneTransform = activeAnimator.GetBoneTransform(bone);
            if (boneTransform != null && !bindPose.ContainsKey(bone))
            {
                bindPose.Add(bone, new BonePose(boneTransform));
            }
        }

        private void RestoreBindPose()
        {
                                                                                                    // The humanoid Animator writes the hips position every frame, so the
                                                                                                    // restore only rewrites positions of bones the driver actually moves
                                                                                                    // (the side-offset bones). Restoring every captured position here
                                                                                                    // would cancel animated hip motion in the LateUpdate path.
            foreach (KeyValuePair<HumanBodyBones, BonePose> entry in bindPose)
            {
                entry.Value.Restore(IsPositionEditedBone(entry.Key));
            }

            if (hasRootBindScale && activeRoot != null)
            {
                activeRoot.localScale = rootBindScale;
            }
        }

        private static bool IsPositionEditedBone(HumanBodyBones bone)
        {
            return bone == HumanBodyBones.LeftShoulder ||
                   bone == HumanBodyBones.RightShoulder ||
                   bone == HumanBodyBones.LeftUpperLeg ||
                   bone == HumanBodyBones.RightUpperLeg;
        }

        private void ResetGroundingState()
        {
            currentPelvisOffset = 0f;
            groundingWeight = 0f;
            rootVelocity = 1f;
            hasLastRootPosition = false;
            lastRootPosition = Vector3.zero;
            morphGroundOffset = 0f;
            currentFootRadiusScale = 1f;
            currentFeetInputScale = 1f;
            warnedMissingIkBridge = false;

            for (int index = 0; index < footGrounded.Length; index++)
            {
                footGrounded[index] = false;
                footIkPositions[index] = Vector3.zero;
                footIkNormals[index] = Vector3.up;
                footSlopeRotations[index] = Quaternion.identity;
                lastFootSlopeRotations[index] = Quaternion.identity;
                hasLastFootRotation[index] = false;
                lastFootHeights[index] = 0f;
                groundingProbeStates[index].Clear();
            }
        }

        private void ApplyBodyMorphLiteScales()
        {
                                                                                                            // BML expresses body controls as neutral 1.0 scales. CharacterEditor
                                                                                                            // stores recipes as -1..1 values, then maps them through the rig
                                                                                                            // profile so BML-style rig channels and visible creator sliders can
                                                                                                            // use the same evaluator without changing saved recipe IDs.

            float heightScale = EvaluateScale(CharacterRigProportionChannel.Height, 0.92f, 1.08f);
            float lowerBodyScale = EvaluateScale(CharacterRigProportionChannel.LowerBody, 0.5f, 1.5f);
            float rootScale = heightScale + lowerBodyScale - 1f;
            if (activeRoot != null)
            {
                activeRoot.localScale = (hasRootBindScale ? rootBindScale : Vector3.one) * rootScale;
            }

            float spineInputScale = EvaluateScale(CharacterRigProportionChannel.Spine, 0.5f, 1.5f);
            float upperBodyInputScale = EvaluateScale(CharacterRigProportionChannel.UpperBody, 0.5f, 1.5f);
            float waistInputScale = EvaluateScale(CharacterRigProportionChannel.Waist, 0.5f, 1.5f);
            float chestInputScale = EvaluateScale(CharacterRigProportionChannel.Chest, 0.5f, 1.5f);
            float neckInputScale = EvaluateScale(CharacterRigProportionChannel.Neck, 0.5f, 1.5f);
            float headInputScale = EvaluateScale(CharacterRigProportionChannel.Head, 0.5f, 1.5f);
            float shouldersInputScale = EvaluateScale(CharacterRigProportionChannel.Shoulders, 0.5f, 2f);
            float upperArmsInputScale = EvaluateScale(CharacterRigProportionChannel.UpperArms, 0.5f, 1.5f);
            float lowerArmsInputScale = EvaluateScale(CharacterRigProportionChannel.LowerArms, 0.5f, 1.5f);
            float handsInputScale = EvaluateScale(CharacterRigProportionChannel.Hands, 0.5f, 1.5f);
            float fingersInputScale = EvaluateScale(CharacterRigProportionChannel.Fingers, 0.5f, 2.5f);
            float legsInputScale = EvaluateScale(CharacterRigProportionChannel.Legs, 0.8f, 1.2f);
            float feetInputScale = EvaluateScale(CharacterRigProportionChannel.Feet, 0.5f, 1.5f);
            currentFootRadiusScale = EvaluateScale(CharacterRigProportionChannel.FootRadius, 0.5f, 1.5f);
            currentFeetInputScale = feetInputScale;

            // These compensating ratios follow BML's scale chain. Each solve starts
            // from the captured bind pose so repeated slider edits do not drift.

            float spineScale = spineInputScale / lowerBodyScale;
            float upperLegScale = legsInputScale;
            float lowerLegScale = 1f / upperLegScale / upperLegScale;
            float feetScale = 1f / lowerLegScale / legsInputScale * feetInputScale;
            float waistScale = waistInputScale / spineScale / lowerBodyScale;
            float upperBodyScale = waistScale + upperBodyInputScale - 1f;
            float chestScale = chestInputScale / waistScale / spineInputScale;
            float neckScale = neckInputScale / chestInputScale;
            float headScale = headInputScale / neckInputScale;
            float shoulderScale = shouldersInputScale / chestInputScale;
            float upperArmScale = upperArmsInputScale / shouldersInputScale;
            float lowerArmScale = lowerArmsInputScale / upperArmsInputScale;
            float handScale = handsInputScale / lowerArmsInputScale;

            ScaleBone(HumanBodyBones.LeftUpperLeg, upperLegScale);
            ScaleBone(HumanBodyBones.RightUpperLeg, upperLegScale);
            ScaleBone(HumanBodyBones.LeftLowerLeg, lowerLegScale);
            ScaleBone(HumanBodyBones.RightLowerLeg, lowerLegScale);
            ScaleBone(HumanBodyBones.LeftFoot, feetScale);
            ScaleBone(HumanBodyBones.RightFoot, feetScale);
            ScaleBone(HumanBodyBones.Spine, spineScale);

            ScaleBone(HumanBodyBones.Chest, bindPose.ContainsKey(HumanBodyBones.UpperChest) 
                                            ? upperBodyScale : upperBodyScale * chestScale);

            ScaleBone(HumanBodyBones.UpperChest, chestScale);
            ScaleBone(HumanBodyBones.Neck, neckScale);

            ScaleBone(HumanBodyBones.Head, bindPose.ContainsKey(HumanBodyBones.Neck) 
                                                ? headScale : headScale * neckScale);
                                                
            ScaleBone(HumanBodyBones.LeftShoulder, shoulderScale);
            ScaleBone(HumanBodyBones.RightShoulder, shoulderScale);

            ScaleBone(HumanBodyBones.LeftUpperArm, bindPose.ContainsKey(HumanBodyBones.LeftShoulder) 
                                                    ? upperArmScale : upperArmScale * shoulderScale);
            ScaleBone(HumanBodyBones.RightUpperArm, bindPose.ContainsKey(HumanBodyBones.RightShoulder) 
                                                    ? upperArmScale : upperArmScale * shoulderScale);

            ScaleBone(HumanBodyBones.LeftLowerArm, lowerArmScale);
            ScaleBone(HumanBodyBones.RightLowerArm, lowerArmScale);
            ScaleBone(HumanBodyBones.LeftHand, handScale);
            ScaleBone(HumanBodyBones.RightHand, handScale);
            ScaleFingerBones(fingersInputScale);
        }

        private void ScaleFingerBones(float fingerScale)
        {
            ScaleBone(HumanBodyBones.LeftThumbProximal, fingerScale);
            ScaleBone(HumanBodyBones.LeftIndexProximal, fingerScale);
            ScaleBone(HumanBodyBones.LeftMiddleProximal, fingerScale);
            ScaleBone(HumanBodyBones.LeftRingProximal, fingerScale);
            ScaleBone(HumanBodyBones.LeftLittleProximal, fingerScale);
            ScaleBone(HumanBodyBones.RightThumbProximal, fingerScale);
            ScaleBone(HumanBodyBones.RightIndexProximal, fingerScale);
            ScaleBone(HumanBodyBones.RightMiddleProximal, fingerScale);
            ScaleBone(HumanBodyBones.RightRingProximal, fingerScale);
            ScaleBone(HumanBodyBones.RightLittleProximal, fingerScale);
        }

        private void ScaleBone(HumanBodyBones bone, float scale)
        {
            if (bindPose.TryGetValue(bone, out BonePose pose))
            {
                pose.Transform.localScale = pose.LocalScale * scale;
                appliedBoneScales[bone] = scale;
            }
        }

        private void ApplySideOffset(HumanBodyBones leftBone, HumanBodyBones rightBone, float value)
        {
            ApplySideOffset(leftBone, value, -1f);
            ApplySideOffset(rightBone, value, 1f);
        }

        private void ApplySideOffset(HumanBodyBones bone, float value, float fallbackSign)
        {
            if (!bindPose.TryGetValue(bone, out BonePose pose))
            {
                return;
            }

            float side = Mathf.Abs(pose.LocalPosition.x) > 0.0001f
                ? Mathf.Sign(pose.LocalPosition.x)
                : fallbackSign;
            float baseDistance = Mathf.Max(Mathf.Abs(pose.LocalPosition.x), 0.03f);
            var delta = new Vector3(side * baseDistance * WidthOffsetRange * value, 0f, 0f);
            if (delta.x == 0f)
            {
                return;
            }

            // Added on top of any joint offset already applied to this bone.
            pose.Transform.localPosition += delta;
            appliedPositionOffsets.TryGetValue(pose.Transform, out Vector3 existing);
            appliedPositionOffsets[pose.Transform] = existing + delta;
        }

        private float GetChannelValue(CharacterRigProportionChannel channel)
        {
            if (useInspectorBmlControls && TryGetInspectorValue(channel, out float inspectorValue))
            {
                return inspectorValue;
            }

            float sum = 0f;
            foreach (ChannelContribution contribution in channelValues.Values)
            {
                if (contribution.Channel == channel)
                {
                    sum += contribution.Value;
                }
            }

            return Mathf.Clamp(sum, -1f, 1f);
        }

        private bool TryGetInspectorValue(CharacterRigProportionChannel channel, out float value)
        {
            value = channel switch
            {
                CharacterRigProportionChannel.Height => heightInput,
                CharacterRigProportionChannel.ShoulderWidth => shoulderWidthInput,
                CharacterRigProportionChannel.HipsWidth => hipsWidthInput,
                CharacterRigProportionChannel.UpperBody => upperBodyInput,
                CharacterRigProportionChannel.LowerBody => lowerBodyInput,
                CharacterRigProportionChannel.Spine => spineInput,
                CharacterRigProportionChannel.Chest => chestInput,
                CharacterRigProportionChannel.Waist => waistInput,
                CharacterRigProportionChannel.Head => headInput,
                CharacterRigProportionChannel.Neck => neckInput,
                CharacterRigProportionChannel.Shoulders => shouldersInput,
                CharacterRigProportionChannel.UpperArms => upperArmsInput,
                CharacterRigProportionChannel.LowerArms => lowerArmsInput,
                CharacterRigProportionChannel.Hands => handsInput,
                CharacterRigProportionChannel.Fingers => fingersInput,
                CharacterRigProportionChannel.Legs => legsInput,
                CharacterRigProportionChannel.Feet => feetInput,
                CharacterRigProportionChannel.FootRadius => footRadiusInput,
                _ => 0f
            };
            return channel != CharacterRigProportionChannel.None;
        }

        private float EvaluateScale(CharacterRigProportionChannel channel, float fallbackMinimum, float fallbackMaximum)
        {
            float value = GetChannelValue(channel);
            if (proportionProfile != null)
            {
                return proportionProfile.EvaluateScale(channel, value, fallbackMinimum, fallbackMaximum);
            }

            return value < 0f
                ? Mathf.Lerp(1f, fallbackMinimum, -value)
                : Mathf.Lerp(1f, fallbackMaximum, value);
        }

        // Inside OnAnimatorIK the foot transforms still hold last frame's pose, so the
        // IK path probes from this frame's animated IK goals instead.
        private void UpdateFootTargets(bool useAnimatorIkGoals)
        {
            UpdateFootTarget(RightFootIndex, LeftFootIndex, AvatarIKGoal.RightFoot, useAnimatorIkGoals);
            UpdateFootTarget(LeftFootIndex, RightFootIndex, AvatarIKGoal.LeftFoot, useAnimatorIkGoals);
        }

        private void UpdateFootTarget(int index, int oppositeIndex, AvatarIKGoal goal, bool useAnimatorIkGoal)
        {
            if (activeRoot == null || footTransforms[index] == null)
            {
                footGrounded[index] = false;
                groundingProbeStates[index].Clear();
                return;
            }

            float stepHeight = footGrounded[oppositeIndex]
                ? Mathf.Max(footProbeHeight, minFootProbeHeight)
                : Mathf.Min(footProbeHeight, minFootProbeHeight);
            stepHeight = Mathf.Max(stepHeight, 0.01f);

            Vector3 footPosition = useAnimatorIkGoal && activeAnimator != null
                ? activeAnimator.GetIKPosition(goal)
                : footTransforms[index].position;
            Vector3 origin = new(footPosition.x, activeRoot.position.y + stepHeight, footPosition.z);
            float distance = stepHeight * 2f;
            float probeRadius = ResolveFootProbeRadius();
            groundingProbeStates[index].RecordProbe(footPosition, origin, probeRadius, distance);

                                                                                                                                // Adapted from BML's BipedalKinematics: probe from above each animated
                                                                                                                                // foot, then blend the resulting target through Animator IK.

            if (Physics.SphereCast(origin, probeRadius, Vector3.down, out RaycastHit hit, 
                                                        distance, groundingLayers, QueryTriggerInteraction.Ignore))
            {
                float feetHeight = activeRoot.position.y - hit.point.y;
                if (feetHeight < stepHeight)
                {
                    footGrounded[index] = true;
                    footIkPositions[index] = hit.point;
                    footIkNormals[index] = hit.normal;
                    footSlopeRotations[index] = Quaternion.FromToRotation(Vector3.up, hit.normal);
                    groundingProbeStates[index].RecordHit(hit.point, hit.normal, footIkPositions[index]);
                    return;
                }
            }

            footGrounded[index] = false;
            footIkPositions[index] = origin + Vector3.down * distance;
            footIkNormals[index] = Vector3.up;
            footSlopeRotations[index] = Quaternion.identity;
            groundingProbeStates[index].RecordMiss(footIkPositions[index]);
        }

        private float ResolveFootProbeRadius()
        {
            float rootScale = activeRoot != null ? activeRoot.localScale.y : 1f;
            return Mathf.Max(0.001f, footProbeRadius * currentFootRadiusScale * rootScale * currentFeetInputScale);
        }

        private void UpdateMorphGroundOffset()
        {
                                                                                                            // BML exposes this as a foot/leg offset output. CharacterEditor feeds
                                                                                                            // it into pelvis and foot IK compensation so proportion edits do not
                                                                                                            // leave animated feet visibly floating or sinking.
            float legsScale = EvaluateScale(CharacterRigProportionChannel.Legs, 0.8f, 1.2f);
            float feetScale = EvaluateScale(CharacterRigProportionChannel.Feet, 0.5f, 1.5f);
            float lowerLegScale = 1f / legsScale / legsScale;
            float resolvedFeetScale = 1f / lowerLegScale / legsScale * feetScale;
            // A rebuilt Avatar already carries the new leg length into the animated
            // hip height, so the leg heuristic only covers a stale or disabled rebuild.
            float legOffset = avatarMatchesSkeleton
                ? 0f
                : legsScale > 1f
                    ? Mathf.InverseLerp(0f, 1.2f, legsScale) * -0.01f
                    : Mathf.InverseLerp(1f, 0.8f, legsScale) * 0.03f;

            float feetOffset = hasAnkleHeight ? (ankleHeight * resolvedFeetScale) - ankleHeight : 0f;
            float rootScale = activeRoot != null ? activeRoot.localScale.y : 1f;
            morphGroundOffset = (feetOffset + legOffset) * rootScale;
        }

        private void ApplyPelvisHeight()
        {
            float terrainOffset = 0f;
            bool hasGroundedFoot = false;
            for (int index = 0; index < footGrounded.Length; index++)
            {
                if (!footGrounded[index])
                {
                    continue;
                }

                float footTerrainOffset = GetTerrainGroundOffset(index);
                terrainOffset = hasGroundedFoot ? Mathf.Min(terrainOffset, footTerrainOffset) : footTerrainOffset;
                hasGroundedFoot = true;
            }

            float targetOffset = hasGroundedFoot
                ? terrainOffset * pelvisIkWeight * groundingWeight + morphGroundOffset
                : 0f;

            currentPelvisOffset = Mathf.MoveTowards(currentPelvisOffset, targetOffset, pelvisOffsetSpeed * Time.deltaTime);
            Vector3 bodyPosition = activeAnimator.bodyPosition;
            bodyPosition.y += currentPelvisOffset;
            activeAnimator.bodyPosition = bodyPosition;
        }

        private float GetTerrainGroundOffset(int index)
        {
            return activeRoot != null ? footIkPositions[index].y - activeRoot.position.y : 0f;
        }

        private void ApplyFootIk(AvatarIKGoal goal, int index)
        {
            Vector3 targetPosition = activeAnimator.GetIKPosition(goal);
            Quaternion targetSlope = footSlopeRotations[index];
            Vector3 localTargetPosition = activeRoot.InverseTransformPoint(targetPosition);
            Vector3 localIkPosition = activeRoot.InverseTransformPoint(footIkPositions[index]);

            lastFootHeights[index] = Mathf.MoveTowards(
                lastFootHeights[index],
                localIkPosition.y,
                footIkAdaptSpeed * Time.deltaTime);
            localTargetPosition.y += lastFootHeights[index];
            targetPosition = activeRoot.TransformPoint(localTargetPosition);
            targetPosition += footIkNormals[index] * morphGroundOffset;

            // Only the terrain slope is smoothed; the animated foot rotation is layered
            // on top each frame so heel lift and toe-off survive full IK weight.
            if (!hasLastFootRotation[index])
            {
                lastFootSlopeRotations[index] = targetSlope;
                hasLastFootRotation[index] = true;
            }
            else
            {
                lastFootSlopeRotations[index] = Quaternion.RotateTowards(
                    lastFootSlopeRotations[index],
                    targetSlope,
                    footRotationSpeed * Time.deltaTime);
            }

            Quaternion targetRotation = lastFootSlopeRotations[index] * activeAnimator.GetIKRotation(goal);
            float weight = Mathf.Clamp01(groundingWeight);
            activeAnimator.SetIKPosition(goal, targetPosition);
            activeAnimator.SetIKPositionWeight(goal, weight);
            activeAnimator.SetIKRotation(goal, targetRotation);
            activeAnimator.SetIKRotationWeight(goal, weight);
        }

        private void OnDrawGizmos()
        {
            if (!enableGroundingDebugDraw)
            {
                return;
            }

            DrawGroundingProbeGizmo(groundingProbeStates[RightFootIndex]);
            DrawGroundingProbeGizmo(groundingProbeStates[LeftFootIndex]);
            DrawPelvisOffsetGizmo();
        }

        private void DrawGroundingProbeGizmo(GroundingProbeState state)
        {
            if (!state.HasProbe)
            {
                return;
            }

            Gizmos.color = probePathColor;
            Gizmos.DrawWireSphere(state.Origin, state.Radius);
            Gizmos.DrawLine(state.Origin, state.ProbeEnd);
            Gizmos.DrawWireSphere(state.ProbeEnd, state.Radius);

            Gizmos.color = state.Hit ? hitPointColor : missColor;
            Gizmos.DrawSphere(state.HitPoint, Mathf.Max(0.015f, state.Radius * 0.35f));

            if (state.Hit)
            {
                Gizmos.color = normalColor;
                Gizmos.DrawLine(state.HitPoint, state.HitPoint + state.Normal * 0.25f);
            }

            Gizmos.color = footTargetColor;
            float targetSize = Mathf.Max(0.025f, state.Radius * 0.5f);
            Gizmos.DrawLine(state.TargetPosition + Vector3.left * targetSize, state.TargetPosition + Vector3.right * targetSize);
            Gizmos.DrawLine(state.TargetPosition + Vector3.forward * targetSize, state.TargetPosition + Vector3.back * targetSize);
            Gizmos.DrawLine(state.TargetPosition + Vector3.down * targetSize, state.TargetPosition + Vector3.up * targetSize);
            Gizmos.DrawLine(state.FootPosition, state.TargetPosition);
        }

        private void DrawPelvisOffsetGizmo()
        {
            if (activeRoot == null)
            {
                return;
            }

            Vector3 origin = activeRoot.position;
            Vector3 target = origin + Vector3.up * currentPelvisOffset;
            Gizmos.color = pelvisOffsetColor;
            Gizmos.DrawLine(origin, target);
            Gizmos.DrawWireSphere(target, 0.035f);
        }

        private readonly struct ChannelContribution
        {
            public ChannelContribution(CharacterRigProportionChannel channel, float value)
            {
                Channel = channel;
                Value = value;
            }

            public CharacterRigProportionChannel Channel { get; }
            public float Value { get; }
        }

        private readonly struct BonePose
        {
            public BonePose(Transform transform)
            {
                Transform = transform;
                LocalPosition = transform.localPosition;
                LocalScale = transform.localScale;
            }

            public Transform Transform { get; }
            public Vector3 LocalPosition { get; }
            public Vector3 LocalScale { get; }

            public void Restore(bool includePosition)
            {
                if (Transform == null)
                {
                    return;
                }

                if (includePosition)
                {
                    Transform.localPosition = LocalPosition;
                }

                Transform.localScale = LocalScale;
            }
        }

        private sealed class GroundingProbeState
        {
            public bool HasProbe { get; private set; }
            public bool Hit { get; private set; }
            public Vector3 FootPosition { get; private set; }
            public Vector3 Origin { get; private set; }
            public Vector3 ProbeEnd { get; private set; }
            public float Radius { get; private set; }
            public float MaxDistance { get; private set; }
            public Vector3 HitPoint { get; private set; }
            public Vector3 Normal { get; private set; }
            public Vector3 TargetPosition { get; private set; }

            public void Clear()
            {
                HasProbe = false;
                Hit = false;
            }

            public void RecordProbe(Vector3 footPosition, Vector3 origin, float radius, float distance)
            {
                HasProbe = true;
                Hit = false;
                FootPosition = footPosition;
                Origin = origin;
                ProbeEnd = origin + Vector3.down * distance;
                Radius = radius;
                MaxDistance = distance;
                HitPoint = ProbeEnd;
                Normal = Vector3.up;
                TargetPosition = footPosition;
            }

            public void RecordHit(Vector3 point, Vector3 normal, Vector3 targetPosition)
            {
                Hit = true;
                HitPoint = point;
                Normal = normal;
                TargetPosition = targetPosition;
            }

            public void RecordMiss(Vector3 fallbackTarget)
            {
                Hit = false;
                HitPoint = fallbackTarget;
                TargetPosition = fallbackTarget;
                Normal = Vector3.up;
            }
        }
    }
}
