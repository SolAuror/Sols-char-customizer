// Last Change: 9/19/2026 10:30am
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace MalbersAnimations.Conditions
{
    // Targets the IRider interface instead of MRider, so the Condition ships with Animal Controller and
    // works with any custom riding component. Moved out of the Riding System folder and namespace;
    // [MovedFrom] keeps the SerializeReference data written as MalbersAnimations.HAP.C2_Rider loading.
    [System.Serializable, AddTypeMenu("Riding/Rider is Riding")]
    [MovedFrom(false, "MalbersAnimations.HAP")]
    public class C2_Rider : ConditionCore
    {
        [Tooltip("Component that implements IRider (e.g. MRider). Any Component of the Rider works: the interface is searched on it, its parents and its children")]
        [Hide(nameof(LocalTarget))] public Component Target;

        /// <summary>Riding phases the Condition can check.</summary>
        public enum RiderState { IsRiding, IsMounting, IsDismounting, AnimalInMountTrigger, }

        public RiderState state = RiderState.IsRiding;

        /// <summary>Runtime cache of the IRider resolved from the Target. Not serialized.</summary>
        [System.NonSerialized] private IRider rider;

        public override string DynamicName => $"Rider {state}";

        /// <summary>IRider resolved from the Target. Re-resolves when the Target changed or the rider was destroyed.</summary>
        public IRider Rider
        {
            get
            {
                if (rider is Object riderObj && !riderObj) rider = null; //Rider was destroyed
                rider ??= MTools.VerifyInterface<IRider>(Target, null);
                return rider;
            }
        }

        protected override bool _Evaluate()
        {
            var rider = Rider;

            if (rider == null)
                return false;

            Debugging($"Rider Is {state} {rider.IsRiding}", rider.IsRiding, Target);

            return state switch
            {
                RiderState.IsRiding => rider.IsRiding,
                RiderState.IsMounting => rider.IsMounting,
                RiderState.IsDismounting => rider.IsDismounting,
                RiderState.AnimalInMountTrigger => rider.InMountTrigger,
                _ => false,
            };
        }

        /// <summary>Finds the IRider on the new target and keeps its Component as the Target.</summary>
        protected override void _SetTarget(Object target)
        {
            rider = MTools.VerifyInterface(target, rider);
            Target = rider as Component;
        }

        /// <summary>True while the IRider of the Target is resolved.</summary>
        public override bool HasTarget => rider != null;
    }
}
