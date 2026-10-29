// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using System;
using UnityEngine;


namespace MalbersAnimations.Items
{
    [HelpURL(ReactionDocs.SuperItem + "#pouch-ammo")]
    [System.Serializable, AddTypeMenu("Malbers/Super Item/[Pouch] Ammo")]
    public class PouchManagerReaction : Reaction
    {
        public enum PouchAction
        {
            AddAmount, RemoveAmount, Clear,
            Infinite,       //MWC — Infinite reserve for ONE projectile
            InfiniteAll,    //MWC — Infinite reserve for the WHOLE pouch (master switch)
            SetAmount,      //Set the reserve to an exact amount
            ClearAll,       //Empty every reserve in the pouch
            RefreshUI       //Re-invoke On Ammo Changed so any bound UI redraws
        }


        public PouchAction Action;

        //MWC — every action except [Infinite All] targets one projectile reserve
        [Hide(nameof(Action), 0, 1, 2, 3, 5, 7)]
        public ProjectileID ProjectileID;

        [Tooltip("Amount of ammo to add, remove or set")]
        [Hide(nameof(Action), 0, 1, 5)] //MWC — only meaningful for Add/Remove/Set
        //MWC — was uninitialized: a freshly created Reaction had a null [Amount] until it round-tripped through
        //serialization, which threw inside DynamicName on the very first repaint. Authored values still override this.
        public IntReference Amount = new(1);

        //MWC — value applied by the [Infinite] and [Infinite All] actions. Infinite affects the RESERVE only:
        //a weapon using a Chamber still empties its magazine and still has to Reload.
        [Tooltip("Turn Infinite Ammo ON or OFF.\nInfinite affects the reserve only: a weapon using a Chamber still empties its magazine and still needs to Reload")]
        [Hide(nameof(Action), 3, 4)]
        public BoolReference InfiniteValue = new(true);

        //MWC — the reaction list showed the class name for every mode, which told you nothing with 5 modes
        public override string DynamicName => Action switch
        {
            PouchAction.AddAmount => $"Pouch: Add [{Amount.Value}] {IDName}",
            PouchAction.RemoveAmount => $"Pouch: Remove [{Amount.Value}] {IDName}",
            PouchAction.Clear => $"Pouch: Clear {IDName}",
            PouchAction.Infinite => $"Pouch: Infinite [{(InfiniteValue.Value ? "ON" : "OFF")}] {IDName}",
            PouchAction.InfiniteAll => $"Pouch: Infinite ALL [{(InfiniteValue.Value ? "ON" : "OFF")}]",
            PouchAction.SetAmount => $"Pouch: Set [{Amount.Value}] {IDName}",
            PouchAction.ClearAll => "Pouch: Clear ALL",
            PouchAction.RefreshUI => $"Pouch: Refresh UI {(ProjectileID != null ? IDName : "[All]")}",
            _ => base.DynamicName,
        };

        /// <summary> Projectile ID name used by the DynamicName </summary>
        private string IDName => $"[{(ProjectileID != null ? ProjectileID.name : "<Missing>")}]";

        public override Type ReactionType => typeof(PouchManager);

        protected override bool _TryReact(Component reactor)
        {
            if (reactor is PouchManager pouch)
            {
                switch (Action)
                {
                    case PouchAction.AddAmount:
                        pouch.Ammo_Add(ProjectileID, Amount);
                        break;
                    case PouchAction.RemoveAmount:
                        pouch.Ammo_Reduce(ProjectileID, Amount);
                        break;
                    case PouchAction.Clear: //The reserve is emptied, the Ammo entry itself stays in the pouch
                        return Ammo_Set(pouch, ProjectileID, 0);
                    case PouchAction.Infinite:
                        pouch.Ammo_SetInfinite(ProjectileID, InfiniteValue.Value);
                        break;
                    case PouchAction.InfiniteAll:
                        pouch.Infinite_All(InfiniteValue.Value);
                        break;
                    case PouchAction.SetAmount:
                        return Ammo_Set(pouch, ProjectileID, Amount.Value);
                    case PouchAction.ClearAll:
                        foreach (var ammo in pouch.ammo) Ammo_Set(ammo, 0);
                        break;
                    case PouchAction.RefreshUI:
                        if (ProjectileID != null)
                            pouch.InvokeAmmoChanged(ProjectileID);
                        else
                            foreach (var ammo in pouch.ammo) ammo.OnAmmoChanged.Invoke(ammo.Total);
                        break;
                }

                return true;
            }
            return false;
        }

        /// <summary> Sets one reserve to an exact amount, creating the Ammo entry when the pouch has none for that Projectile </summary>
        private static bool Ammo_Set(PouchManager pouch, ProjectileID id, int value)
        {
            if (id == null) return false;

            var ammo = pouch.GetProjectile(id);

            //No entry for this Projectile yet: Ammo_Add creates one holding exactly that amount
            if (ammo == null)
            {
                if (value > 0) pouch.Ammo_Add(id, value);
                return true;
            }

            Ammo_Set(ammo, value);
            return true;
        }

        /// <summary> Sets an Ammo reserve to an exact amount and fires its events the way Add Ammo / Reduce Ammo do (total, then delta) </summary>
        private static void Ammo_Set(Ammo ammo, int value)
        {
            var newTotal = Mathf.Max(0, value);
            var delta = newTotal - ammo.Total;

            if (delta == 0) return;

            ammo.Total = newTotal;
            ammo.OnAmmoChanged.Invoke(ammo.Total);
            ammo.OnAmmoAddedOrRemoved.Invoke(delta);
        }
    }
}
