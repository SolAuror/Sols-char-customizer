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

        [Header("Grounding Debug")]
        [SerializeField] private bool enableGroundingDebugDraw;
        [SerializeField] private Color probePathColor = new(0.25f, 0.6f, 1f, 1f);
        [SerializeField] private Color hitPointColor = new(0.2f, 1f, 0.35f, 1f);
        [SerializeField] private Color missColor = new(1f, 0.35f, 0.25f, 1f);
        [SerializeField] private Color footTargetColor = new(1f, 0.85f, 0.2f, 1f);
        [SerializeField] private Color normalColor = new(0.75f, 0.35f, 1f, 1f);
        [SerializeField] private Color pelvisOffsetColor = new(1f, 0.45f, 0.85f, 1f);

        private readonly Dictionary<CharacterRigProportionChannel, float> channelValues = new();
        private readonly Dictionary<HumanBodyBones, BonePose> bindPose = new();
        private readonly Transform[] footTransforms = new Transform[2];
        private readonly Vector3[] footIkPositions = new Vector3[2];
        private readonly Vector3[] footIkNormals = { Vector3.up, Vector3.up };
        private readonly Quaternion[] footIkRotations = { Quaternion.identity, Quaternion.identity };
        private readonly Quaternion[] lastFootRotations = { Quaternion.identity, Quaternion.identity };
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
        private float currentFootRadiusScale = 1f;
        private bool warnedMissingIkBridge;

        public bool CanApply => activeRoot != null && activeAnimator != null && activeAnimator.isHuman && bindPose.Count > 0;
        public bool HasAnimatorIkBridge => activeIkBridge != null;
        public bool EnableFootIkGrounding
        {
            get => enableFootIkGrounding;
            set => enableFootIkGrounding = value;
        }

        public float GroundingWeight => groundingWeight;
        public float MorphGroundOffset => morphGroundOffset;

        public void Bind(Transform root, Animator animator)
        {
            if (activeRoot == root && activeAnimator == animator)
            {
                return;
            }

            RestoreBindPose();
            if (activeIkBridge != null)
            {
                activeIkBridge.Bind(null);
            }

            activeRoot = root;
            activeAnimator = animator;
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
            ApplyNow();
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
                channelValues[definition.RigChannel] = clampedValue;
            }

            ApplyNow();
        }

        public void ResetMorph(CharacterMorphDefinition definition)
        {
            if (definition == null || definition.RigChannel == CharacterRigProportionChannel.None)
            {
                return;
            }

            channelValues[definition.RigChannel] = 0f;
            ApplyNow();
        }

        public void ApplyNow()
        {
            if (!CanApply)
            {
                return;
            }

            RestoreBindPose();
            ApplyBodyMorphLiteScales();

            ApplySideOffset(HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, 
                            GetChannelValue(CharacterRigProportionChannel.ShoulderWidth));

            ApplySideOffset(HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, 
                            GetChannelValue(CharacterRigProportionChannel.HipsWidth));

            UpdateMorphGroundOffset();
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
            if (enableFootIkGrounding && activeIkBridge != null)
            {
                return;
            }

            ApplyNow();
            if (enableFootIkGrounding || enableGroundingDebugDraw)
            {
                if (enableFootIkGrounding)
                {
                    WarnIfGroundingBridgeMissing();
                }

                UpdateFootTargets();
            }
        }

        internal void ApplyAnimatorIk(int layerIndex)
        {
            if (!enableFootIkGrounding || activeAnimator == null || activeRoot == null)
            {
                return;
            }

            ApplyNow();
            UpdateFootTargets();
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
                ApplyNow();
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
            Array.Clear(footTransforms, 0, footTransforms.Length);
            hasAnkleHeight = false;
            ankleHeight = 0f;
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
            foreach (BonePose pose in bindPose.Values)
            {
                pose.Restore();
            }
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
            warnedMissingIkBridge = false;

            for (int index = 0; index < footGrounded.Length; index++)
            {
                footGrounded[index] = false;
                footIkPositions[index] = Vector3.zero;
                footIkNormals[index] = Vector3.up;
                footIkRotations[index] = activeRoot != null ? activeRoot.rotation : Quaternion.identity;
                lastFootRotations[index] = Quaternion.identity;
                hasLastFootRotation[index] = false;
                lastFootHeights[index] = 0f;
                groundingProbeStates[index].Clear();
            }
        }

        private void ApplyBodyMorphLiteScales()
        {
                                                                                                            // BML expresses body controls as neutral 1.0 scales. CharacterEditor
                                                                                                            // stores recipes as -1..1 values, then maps them through the rig
                                                                                                            // profile so hidden BML test channels and visible creator sliders can
                                                                                                            // use the same evaluator without changing saved recipe IDs.

            float heightScale = EvaluateScale(CharacterRigProportionChannel.Height, 0.92f, 1.08f);
            float lowerBodyScale = EvaluateScale(CharacterRigProportionChannel.LowerBody, 0.5f, 1.5f);
            float rootScale = heightScale + lowerBodyScale - 1f;
            if (activeRoot != null)
            {
                activeRoot.localScale = Vector3.one * rootScale;
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
            Vector3 position = pose.LocalPosition;
            position.x += side * baseDistance * WidthOffsetRange * value;
            pose.Transform.localPosition = position;
        }

        private float GetChannelValue(CharacterRigProportionChannel channel)
        {
            if (useInspectorBmlControls && TryGetInspectorValue(channel, out float inspectorValue))
            {
                return inspectorValue;
            }

            return channelValues.TryGetValue(channel, out float value) ? value : 0f;
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

        private void UpdateFootTargets()
        {
            UpdateFootTarget(RightFootIndex, LeftFootIndex);
            UpdateFootTarget(LeftFootIndex, RightFootIndex);
        }

        private void UpdateFootTarget(int index, int oppositeIndex)
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

            Vector3 footPosition = footTransforms[index].position;
            Vector3 origin = new(footPosition.x, activeRoot.position.y + stepHeight, footPosition.z);
            float distance = stepHeight * 2f;
            groundingProbeStates[index].RecordProbe(footPosition, origin, footProbeRadius * currentFootRadiusScale, distance);

                                                                                                                                // Adapted from BML's BipedalKinematics: probe from above each animated
                                                                                                                                // foot, then blend the resulting target through Animator IK.

            if (Physics.SphereCast(origin, footProbeRadius * currentFootRadiusScale, Vector3.down, out RaycastHit hit, 
                                                        distance, groundingLayers, QueryTriggerInteraction.Ignore))
            {
                float feetHeight = activeRoot.position.y - hit.point.y;
                if (feetHeight < stepHeight)
                {
                    footGrounded[index] = true;
                    footIkPositions[index] = hit.point;
                    footIkNormals[index] = hit.normal;
                    footIkRotations[index] = Quaternion.FromToRotation(Vector3.up, hit.normal) * activeRoot.rotation;
                    groundingProbeStates[index].RecordHit(hit.point, hit.normal, footIkPositions[index]);
                    return;
                }
            }

            footGrounded[index] = false;
            footIkPositions[index] = origin + Vector3.down * distance;
            footIkNormals[index] = Vector3.up;
            footIkRotations[index] = activeRoot.rotation;
            groundingProbeStates[index].RecordMiss(footIkPositions[index]);
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
            float legOffset = legsScale > 1f
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
            Quaternion targetRotation = activeAnimator.GetIKRotation(goal);
            Vector3 localTargetPosition = activeRoot.InverseTransformPoint(targetPosition);
            Vector3 localIkPosition = activeRoot.InverseTransformPoint(footIkPositions[index]);

            lastFootHeights[index] = Mathf.MoveTowards(
                lastFootHeights[index],
                localIkPosition.y,
                footIkAdaptSpeed * Time.deltaTime);
            localTargetPosition.y += lastFootHeights[index];
            targetPosition = activeRoot.TransformPoint(localTargetPosition);
            targetPosition += footIkNormals[index] * morphGroundOffset;

            Quaternion rotationOffset = Quaternion.Inverse(targetRotation) * footIkRotations[index];
            targetRotation *= rotationOffset;
            if (!hasLastFootRotation[index])
            {
                lastFootRotations[index] = targetRotation;
                hasLastFootRotation[index] = true;
            }
            else
            {
                lastFootRotations[index] = Quaternion.RotateTowards(
                    lastFootRotations[index],
                    targetRotation,
                    footRotationSpeed * Time.deltaTime);
            }

            float weight = Mathf.Clamp01(groundingWeight);
            activeAnimator.SetIKPosition(goal, targetPosition);
            activeAnimator.SetIKPositionWeight(goal, weight);
            activeAnimator.SetIKRotation(goal, lastFootRotations[index]);
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

            public void Restore()
            {
                if (Transform == null)
                {
                    return;
                }

                Transform.localPosition = LocalPosition;
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
