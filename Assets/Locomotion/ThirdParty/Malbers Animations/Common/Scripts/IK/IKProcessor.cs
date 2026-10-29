// Last Change: 10/16/2026 4:28pm
using MalbersAnimations.Conditions; // MWC — Conditions2 support on the IK Processor
using System;
using UnityEngine;

namespace MalbersAnimations.IK
{
    [Serializable]
    public abstract class IKProcessor
    {
        [HideInInspector] public string name;
        [HideInInspector] public bool Active = true;

        [Tooltip("Weight Applied for the Processor")]
        [HideInInspector][Range(0, 1)] public float Weight = 1;

        [Tooltip("Target transform reference from the IK Set [Targets Array]. Index Value. Target applied to the Avatar IK Goal")]
        [Hide("showIndex")]
        [Min(-1)] public int TargetIndex = 0;

        //MWC — legacy single Anim Parameter. Kept ONLY so old assets can migrate into [AnimParameters]. Do not read it.
        [HideInInspector] public string AnimParameter;

        //MWC — multiple float Anim Parameters per Processor: every non empty one is multiplied into the weight.
        //NO field initializer on purpose: a default would overwrite the legacy value before MigrateAnimParameter() runs.
        [Tooltip("Local Anim Parameters to apply to this IK Processor. Every valid one is multiplied into the Processor weight")]
        [HideInInspector] public string[] AnimParameters;

        /// <summary>Hashes of [AnimParameters], resolved by the IK Set. A zero means 'not on the Animator' and is ignored.</summary>
        [HideInInspector] public int[] AnimParameterHashes;

        //MWC — always false. Only exists so [Hide] can keep CheckIf out of the default PropertyField draw
        //(IKSetDrawer renders it by hand under the Anim Param row).
        [HideInInspector] public bool drawConditions;

        // MWC — Local Conditions to influence the weight of THIS processor only (same idea as AnimParameter, but boolean).
        //[Hide] and NOT [HideInInspector]: HideInInspector hides the whole subtree from SerializedProperty
        //(hasVisibleChildren returns false), so the conditions lose their foldout and cannot be edited.
        [Tooltip("Local Conditions for this IK Processor. If they evaluate false the Processor weight is set to zero")]
        [Hide(nameof(drawConditions))] public Conditions2 CheckIf = new();

        [HideInInspector, Tooltip("Tag to identify the IK Processor. Useful for the IK Manager to find a specific processor with the same tag")]
        public IKTag Tag;

        /// <summary>  Bone reference for the IK Processor.  </summary>
        public Transform Bone { get; set; }


        public virtual Transform GetTarget(IKSet set)
        {
            if (!RequireTargets) return null;

            /// Get the Target from the IK Set. If there's a Tag, get the Target from the IK Manager Global Targets with that Tag,
            if (Tag == null)
            {
                if (set.Targets.Length > TargetIndex)
                    return set.Targets[TargetIndex].Value;
            }
            else //if not, get the Target from the IK Set Targets Array with the Target Index
            {
                return set.Owner.GlobalTargets_Dic.TryGetValue(Tag, out var target) ? target : null;
            }
            return null;
        }

        public virtual Transform GetTarget(IKSet set, IKTag Tag)
        {
            if (Tag == null) return null;

            if (set.Owner == null)
            {
                Debug.LogWarning($"The IK Set <B>[{set.Name}]</B> has no Owner. Please assign the IK Set to an IK Manager.", set.Owner);
                return null;
            }

            return set.Owner.GetTargetByTag(Tag);
        }


        /// <summary> Tells the IK Manager if it needs Targets to check</summary>
        public abstract bool RequireTargets { get; }

        public virtual void Start(IKSet IKSet, Animator anim, int index)
        {
            Bone = GetTarget(IKSet);

            if (Bone == null && RequireTargets && Tag != null)
            {
                Debug.LogWarning($"The IK Set <B>[{IKSet.Name}]</B> has no Target set for the [IK Processor: {name}] with the Tag <B><color=white>{Tag.name}</Color></B>. " +
                    $"Please add a reference in the [Targets Global] list .", anim);
            }
        }

        /// <summary>
        /// Cache the initial values of the IK Processor. This is useful for the IK Manager to restore the original values of the IK Processor when it gets disabled or when the weight is zero.
        /// </summary>
        public virtual void CacheValue(IKSet iKSet, Animator anim, int index) { }

        public virtual void OnEnable(IKSet IKSet, Animator anim, int index) { }

        public virtual void OnDisable(IKSet IKSet, Animator anim, int index) { }

        public virtual void OnAnimatorIK(IKSet IKSet, Animator anim, int index, float weight, int layer) { }

        /// <summary> LateUpdate to call all IK Processors that need to be updated after the Animator IK  </summary>
        /// <param name="IKSet">IK Set</param>
        /// <param name="anim">Reference for the animator controller</param>
        /// <param name="index">index of the Processor in the IK Set List</param>
        /// <param name="weight">Weight of the Processor</param>
        public virtual void LateUpdate(IKSet IKSet, Animator anim, int index, float weight) { }
        public virtual void OnDrawGizmos(IKManager manager, IKSet IKSet, Animator anim, float weight) { }

        /// <summary>Scale Multiplier of the IK Manager owning [set] (1 without one). Multiplies position offsets only, never rotations</summary>
        public static float OffsetScale(IKSet set) => set != null && set.Owner != null ? set.Owner.ScaleMultiplier : 1f;

        ///<summary> Verify if the IKProcessor is set correctly.If it needs some references </summary>
        public abstract void Verify(IKManager manager, IKSet set, Animator animator, int index);

        // MWC — was a single parameter. Every resolved parameter is multiplied together; empty rows and names the
        // Animator does not have resolve to hash 0 and are skipped, so they cannot zero the weight by accident.
        /// <summary> Multiplies the value of every valid Anim Parameter of this Processor. 1 when it has none </summary>
        public float GetProcessorAnimWeight(Animator animator)
        {
            if (AnimParameterHashes == null) return 1;

            float weight = 1;

            for (int i = 0; i < AnimParameterHashes.Length; i++)
            {
                var hash = AnimParameterHashes[i];
                if (hash != 0) weight *= animator.GetFloat(hash);
            }

            return weight;
        }

        // MWC — one way migration from the legacy single [AnimParameter]. Idempotent: it only fires while the array is
        // empty. The legacy field is cleared by the Editor (OnValidate) so the asset stops carrying a dead value.
        /// <summary> Moves the legacy single Anim Parameter into the [AnimParameters] array </summary>
        public void MigrateAnimParameter()
        {
            if (string.IsNullOrEmpty(AnimParameter)) return;

            if (AnimParameters == null || AnimParameters.Length == 0)
                AnimParameters = new string[] { AnimParameter };
        }

        // MWC — Per tick cache of the Conditions result. The IK pass runs the Processors once per Animator IK-Pass
        // LAYER plus once in LateUpdate (5 times per frame on the Human v6 rig, which has 4 of them), and the
        // Conditions cannot answer differently between two passes of the SAME phase — nothing runs in between.
        // The two phases are cached apart on purpose: the LateUpdate pass runs after the other components' LateUpdate,
        // so it re evaluates instead of reusing the answer from the Animator update. 5 evaluations become 2.
        // Cheap Conditions barely notice; an expensive one (raycast, distance over many objects, GetComponent) does.
        private double m_CondTickIK = -1, m_CondTickLate = -1;
        private float m_CondWeightIK = 1, m_CondWeightLate = 1;

        // MWC — Local Conditions weight. Returns 1 when there are no conditions (or they are disabled), 0 when they fail.
        /// <summary> Process the Local Conditions of the IK Processor. Hard 0/1 gate, evaluated once per tick and phase </summary>
        public float GetProcessorConditionWeight(IKSet set, Animator animator)
        {
            if (!CheckIf.Valid) return 1;

            //No Set (a direct call from outside the IK pass): no tick to key the cache on, so just evaluate
            if (set == null) return CheckIf.Evaluate(animator) ? 1 : 0;

            var tick = set.CurrentTick;

            if (set.InAnimatorIKPass)
            {
                if (m_CondTickIK != tick)
                {
                    m_CondTickIK = tick;
                    m_CondWeightIK = CheckIf.Evaluate(animator) ? 1 : 0;
                }
                return m_CondWeightIK;
            }

            if (m_CondTickLate != tick)
            {
                m_CondTickLate = tick;
                m_CondWeightLate = CheckIf.Evaluate(animator) ? 1 : 0;
            }
            return m_CondWeightLate;
        }

        /// <summary>Draws the Processor Scene view handles (editor only).</summary>
        internal virtual void OnSceneGUI(IKSet set, Animator animator, UnityEngine.Object target, int index) { }

#if UNITY_EDITOR
        /// <summary>Per type: does the Processor override OnSceneGUI (i.e. draw its own handles)?</summary>
        private static readonly System.Collections.Generic.Dictionary<Type, bool> sceneHandlesCache = new();

        /// <summary>True when this Processor type draws its own Scene view handles.</summary>
        internal bool HasSceneHandles
        {
            get
            {
                var type = GetType();
                if (!sceneHandlesCache.TryGetValue(type, out var has))
                {
                    var method = type.GetMethod(nameof(OnSceneGUI), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    has = method != null && method.DeclaringType != typeof(IKProcessor);
                    sceneHandlesCache[type] = has;
                }
                return has;
            }
        }
#endif


        [HideInInspector] public bool showIndex;

        internal virtual void OnValidate(IKManager manager, IKSet set)
        {
            showIndex = RequireTargets && Tag == null;

            //MWC — Editor side of the migration: move the legacy value into the array, then drop it so it cannot return
            MigrateAnimParameter();
            AnimParameter = string.Empty;
        }
    }
    [System.Serializable] //MWC: Required so [SerializeReference] fields in subclasses don't trigger missing-attribute warning
    public abstract class IKProcessorOnAnimIK : IKProcessor
    {
        // public override RequireTargets => false;

        [Min(-1)]
        [Tooltip("Layer Index to apply the IK. If the Animator has more than one layer, you can specify in which layer you want to apply the IK. If the value is 0, it will be applied in the Base Layer.")]
        public int LayerIndex = 0;

        public override void OnAnimatorIK(IKSet set, Animator anim, int index, float weight, int layer)
        {
            if (LayerIndex > 0 && LayerIndex != layer) return;
            OnAnimatorIK(set, anim, index, weight);
        }

        public abstract void OnAnimatorIK(IKSet set, Animator anim, int index, float weight);

    }
}