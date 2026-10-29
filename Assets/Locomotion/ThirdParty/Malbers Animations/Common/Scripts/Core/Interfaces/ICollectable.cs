// Last Change: 9/17/2026 6:05pm
using UnityEngine;

namespace MalbersAnimations
{
    /// <summary>Interface to Identify Collectables Item</summary>
    public interface ICollectable : IObjectCore
    {
        int ID { get; }

        /// <summary>Applies the Item Dropped Logic</summary>
        void Drop();

        /// <summary>Applies the Item Picked Logic</summary>
        void Pick();

        void PickedFailed(GameObject FailedByPicker);

        /// <summary>If the collectable has physic, Enable it</summary>
        void Physics_Restore();

        /// <summary>If the collectable has physic, Disable it</summary>
        void Physics_Disable();

        /// <summary>Set the Focused GameObject</summary>
        void SetFocused(GameObject FocusBy, bool isFocused);

        /// <summary>Pre dropped by an User.</summary>
        void PreDrop(GameObject gameObject);

        /// <summary>Prepicked by an User.</summary>
        void PrePicked(GameObject gameObject);

        /// <summary>Can the Collectable be dropped or Picked?</summary>
        bool InCoolDown { get; }

        /// <summary> Is the Item Picked?</summary>
        bool IsPicked { get; set; }

        /// <summary>Who is currently holding this collectable (null when free). Used to detect and resolve contested picks</summary>
        GameObject Picker { get; set; }

        /// <summary>Forces the current Picker (if any) to drop this collectable</summary>
        void ForceDrop();

        bool AutoPick { get; }

        /// <summary>When an Object is Collectable it means that the Picker can still pick objects, the item was collected by other component 
        /// (E.g. Weapons or Inventory)</summary>
        bool Collectable { get; set; }

        bool Active { get; set; }

        GameObject gameObject { get; }

        //bool CanBePicked { get; }

        /// <summary>  What holder will the item be parent to. -1: Default Holder. >=0 : Index of the Extra Holder list  </summary>
        int Holder { get; }

        /// <summary>  If the item is Picked or Dropped by animation </summary>
        bool ByAnimation { get; }

        /// <summary>  Delay time after calling the Drop() method. the item will be unparented from the PickUp component after this time has passed </summary>
        float DropDelay { get; }

        /// <summary>  Delay before the item can be picked  </summary>
        float PickDelay { get; }
    }
}