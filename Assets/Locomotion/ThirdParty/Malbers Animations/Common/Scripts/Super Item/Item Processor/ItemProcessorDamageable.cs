// Last Change: 9/17/2026 5:33pm
using UnityEngine;

namespace MalbersAnimations.Items
{
    [System.Serializable, AddTypeMenu("Item/Damageable")]
    [UnityEngine.HelpURL(ItemDocs.Processors + "damageable")]
    public class ItemProcessorDamageable : ItemProcessor
    {
        public bool useScriptable;

        [Hide("useScriptable")]
        [RequiredField]
        public MDamageableProfileVar ProfileVar;
        [Hide("useScriptable", true)]
        public MDamageableProfile Profile;

        [Tooltip("When the action is released, the Damageable will restore the previous profile")]
        public ReleaseAction Restore = ReleaseAction.OnRelease;

        private MDamageable[] Damageable;

        public override string DynamicName => $"Damageable [{Profile.name}] Mult: {(Profile.Multiplier)}.  {(Profile.AlignToDamage ? "Align to Damage" : "")}";

        public override void ItemEquipped(SuperItem item, ItemActionSet set, ItemAction action)
        {
            Damageable = item.User.GetComponentsInChildren<MDamageable>();
        }

        public override void ItemUnequipped(SuperItem item, ItemActionSet set, ItemAction action)
        {
            Damageable = null;
        }

        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (Damageable == null) return;

            foreach (var d in Damageable)
            {
                d.Profile_Set(useScriptable && ProfileVar ? ProfileVar.value : Profile);
            }
        }

        public override void ActionReleased(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (Damageable == null) return;
            if (Restore != ReleaseAction.OnRelease) return;

            foreach (var d in Damageable) d.Profile_Restore();
        }

        public override void ActionEnd(SuperItem item, ItemActionSet set, ItemAction itemAction)
        {
            if (Damageable == null) return;
            if (Restore != ReleaseAction.OnEnd) return;

            foreach (var d in Damageable) d.Profile_Restore();
        }


        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (useScriptable && ProfileVar == null) return $"Missing the Profile reference.";
            return string.Empty;
        }

        /// <summary> A freshly added processor starts with a ready-to-use inline Profile instead of a zero-initialized one </summary>
        public override void Reset() => Profile = MDamageableProfile.New("New Damage Profile");
    }
}