// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Conditions;
using MalbersAnimations.Scriptables;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Items
{
    //MWC — Reload is now independent of the Fire (Projectile) processor. It resolves the shared Chamber Stat itself
    //and refills it from the Pouch reserve, instead of reaching into ItemProcessorProjectile.PreloadInChamber().
    [System.Serializable, AddTypeMenu("Projectile/Reload")]
    [HelpURL(ItemDocs.Processors + "projectile-reload")]
    public class ItemProcessorReload : ItemProcessor
    {
        [Tooltip("Projectile reserve (in the Pouch) the rounds are drawn from when reloading")]
        public ProjectileID projectileID;

        //MWC — the Chamber lives on the item's Stats component (Value = rounds in the magazine, Max Value = magazine size).
        //Shared with the Fire processor by pointing both at the same Stat ID.
        [Tooltip("Stat on the Item's Stats component used as the Chamber.\nValue = Units in the chamber. Max Value = Chamber Size")]
        public StatID ChamberStat;

        private PouchManager pouch;
        private Ammo ProjectileAmmo;   //MWC — reserve to draw rounds from
        private IMStat Chamber;          //MWC — resolved on Item Equipped

        //MWC — the reload time is the Processor's own [Delay] (executeDelay) now. The base class starts the countdown and
        //cancels it if the Action is interrupted, so an aborted reload never refills the Chamber.
        public override string DynamicName =>
            $"Reload Chamber: [{(ChamberStat != null ? ChamberStat.name : "<None>")}]" +
            $"{(executeDelay > 0 ? $" - Time: [{executeDelay:F2}]" : "")}";

        //MWC — sensible default reload time when the processor is created (the old hardcoded reloadTime default)
        public override void Reset() => executeDelay = 1f;

        public override void ItemEquipped(SuperItem item, ItemActionSet set, ItemAction action)
        {
            pouch = item.User.Pouch;
            ProjectileAmmo = pouch != null ? pouch.GetProjectile(projectileID) : null;
            Chamber = item.Stat_Get(ChamberStat); //MWC — resolve the shared Chamber Stat
        }

        public override void ItemUnequipped(SuperItem item, ItemActionSet set, ItemAction action)
        {
            pouch = null;
            ProjectileAmmo = null;
            Chamber = null;
        }

        //MWC — called straight away, or after [Delay] seconds. The base class already dropped this call if the Action was interrupted
        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action) => ReloadChamber();

        //MWC — refill the Chamber Stat toward its Max, consuming the rounds from the Pouch reserve (magazine model)
        private void ReloadChamber()
        {
            if (Chamber == null) return;

            var need = (int)(Chamber.MaxValue - Chamber.Value);
            if (need <= 0) return;   //Already full

            //MWC — EffectiveTotal (not Total) so an Infinite reserve always fills the magazine. ReduceAmmo below
            //no-ops while Infinite, so the reserve stays frozen but the reload still completes.
            var available = ProjectileAmmo != null ? ProjectileAmmo.EffectiveTotal : 0;
            var load = Mathf.Min(need, available);
            if (load <= 0) return;   //No reserve rounds to load

            ProjectileAmmo.ReduceAmmo(load); //Consume from the reserve (fires OnAmmoChanged)
            Chamber.Value += load;           //Fill the magazine (fires the Stat events / UI counters)
        }

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            var validation = string.Empty;

            //MWC — validate the Chamber Stat against the item's Stats component (edit-time: use TryGetComponent)
            if (ChamberStat == null) validation += "Missing the Chamber Stat ID reference.\n";
            else if (!item.TryGetComponent<Stats>(out var stats))
                validation += $"The Item has no [Stats] component. Add one next to the Super Item with a [{ChamberStat.name}] Stat.\n";
            else if (stats.Stat_Get(ChamberStat) == null)
                validation += $"The Item's [Stats] component does not contain the [{ChamberStat.name}] Stat.\n";

            if (projectileID == null) validation += "Missing the Projectile ID reference (the reserve to reload from).\n";

            return validation;
        }

#if UNITY_EDITOR
        //MWC — auto-configure the Reload processor the moment it's created in the Item inspector:
        //  1) auto-fill the Chamber Stat ID (looks up a StatID asset named "Chamber")
        //  2) copy the Projectile ID from a Fire (Projectile) processor in the same set, if there is one
        //  3) make sure the Item's GameObject has a Stats component that holds that Chamber Stat
        //  4) gate the owning Action with a "Chamber is NOT full" condition, so a reload doesn't play when already full
        public override void OnCreated(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (ChamberStat == null) ChamberStat = MTools.GetInstance<StatID>("Chamber"); //1

            if (projectileID == null) projectileID = FindProjectileID(set);               //2

            var stats = EnsureChamberStat(item);                                           //3

            if (stats != null) AddChamberNotFullCondition(action, stats);                  //4
        }

        //MWC — first Projectile ID used by any Fire (Projectile) processor in the set, so Reload draws from the right reserve
        private static ProjectileID FindProjectileID(ItemActionSet set)
        {
            if (set?.actions == null) return null;

            foreach (var a in set.actions)
            {
                var fire = a?.GetProcessor<ItemProcessorProjectile>();
                //A Fire Processor throwing a direct Prefab has no reserve to reload from, so it is skipped
                if (fire != null && fire.projectileFrom == ProjectileSource.ProjectileID && fire.projectileID != null) return fire.projectileID;
            }
            return null;
        }

        //MWC — ensure the Item's GameObject has a Stats component containing the Chamber Stat; returns it (or null)
        private Stats EnsureChamberStat(SuperItem item)
        {
            if (ChamberStat == null) return null;

            var stats = item.GetComponent<Stats>();
            if (stats == null) stats = Undo.AddComponent<Stats>(item.gameObject); //Add the Stats component if missing

            if (!stats.stats.Exists(s => s != null && s.ID == ChamberStat))
            {
                stats.stats.Add(new Stat
                {
                    ID = ChamberStat,
                    maxValue = new FloatReference(6), //default magazine size — tweak per weapon
                    value = new FloatReference(6),    //start loaded
                    minValue = new FloatReference(0),
                });
            }

            EditorUtility.SetDirty(stats);
            return stats;
        }

        //MWC — add a "Chamber is NOT full" condition to the Action so the reload is skipped when the magazine is already full.
        //It targets the ITEM's own Stats (LocalTarget + explicit Target) because the Action evaluates ConditionsOnOwner against
        //the User (character), whereas the Chamber Stat lives on the Item.
        private void AddChamberNotFullCondition(ItemAction action, Stats stats)
        {
            if (ChamberStat == null) return;

            var existing = action.ConditionsOnOwner.conditions;
            if (existing != null)
                foreach (var c in existing)
                    if (c is C_Stats2 cs && cs.stat == ChamberStat) return; //Already present → don't duplicate

            action.ConditionsOnOwner.Add(new C_Stats2
            {
                stat = ChamberStat,
                Condition = StatCondition.Full,
                invert = true,       //true when the chamber is NOT full
                LocalTarget = true,  //ignore the passed-in owner; use my own Target
                Target = stats,      //the Item's own Stats component
            });
            action.ConditionsOnOwner.active = true; //A condition group only evaluates when it's marked active
        }
#endif
    }
}
