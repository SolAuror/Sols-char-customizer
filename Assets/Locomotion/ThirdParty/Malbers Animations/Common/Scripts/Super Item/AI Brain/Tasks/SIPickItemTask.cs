// Last Change: 9/19/2026 10:40am
using MalbersAnimations.Items;
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    // MWC — Super Item AI Task: search free Super Items around the AI, walk to them (keeping the Brain Target, e.g. the Player) and pick them
    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Tasks/Super Item/Pick Item", fileName = "new SI Pick Item Task")]
    public class SIPickItemTask : MTask
    {
        public override string DisplayName => "Super Item/Pick Item";

        public enum PickAsOption { ItemDefault, Equip, Holster }

        [Tooltip("Items the AI can pick.\nInclude: only these Super Item IDs.\nExclude: any Item but these.\nEmpty: any free Super Item.\n\n" +
            "With [Pick Multiple] the list is a WISH LIST: one pick per entry, duplicates allowed (Melee, Melee = two melee weapons. Melee, Shield = one of each)")]
        public IDListCheck<SuperItemID> items = new();

        [Tooltip("Pick every entry of the list, not just one Item. The Task keeps picking until each entry is OWNED (equipped or holstered).\n" +
            "Duplicated IDs mean several Items of that type. Include mode only (Exclude falls back to a single pick)")]
        public bool PickMultiple = false;

        [Tooltip("When no free Item is found, keep searching every Update Interval instead of finishing the Task (e.g. wait for an enemy to drop its weapon)")]
        public bool KeepSearching = false;

        [Tooltip("Search radius around the Animal for a free Super Item (not equipped, not holstered). The closest one is chosen")]
        public FloatReference SearchRadius = new(10);

        [Tooltip("Walk to the Item using a Destination point. The Brain Target (e.g. the Player) is NOT changed")]
        public bool MoveToItem = true;

        [Tooltip("Pick the Item when the AI is inside this distance. 0 = wait until the AI Control has arrived (Stopping Distance)")]
        public FloatReference PickDistance = new(0);

        [Tooltip("Item Default: the hands when the Manager's global [Auto Equip] is ON, otherwise the Item's own [Auto Equip] decides.\nEquip: put it in the hands.\nHolster: store it in its Holster")]
        public PickAsOption PickAs = PickAsOption.ItemDefault;

        [Tooltip("Skip the Item's Pick Mode and Pick Duration (instant pick)")]
        public bool Instant = false;

        [Tooltip("When the pick is done (or the AI State exits) resume moving to the Brain Target, if there is one")]
        public bool ReturnToTarget = true;

        public Color debugColor = new(0, 0.7f, 0.7f, 0.3f);

        /// <summary>Seconds after the Pick Duration to consider an accepted pick as failed (the Item never reached the Manager)</summary>
        private const float PickTimeout = 1f;

        /// <summary>Give up on the current Item after this many failed picks</summary>
        private const int MaxAttempts = 3;

        //MWC: Per-agent runtime state. Lives on the brain (not on this shared SO) so each AI tracks its own Item.
        private class PickState : BrainState
        {
            /// <summary>Item this task is picking</summary>
            public SuperItem Item;
            /// <summary>True once the pick was accepted by the Manager</summary>
            public bool Picked;
            /// <summary>True while the AI is travelling to the Item (its destination was overridden)</summary>
            public bool Moving;
            /// <summary>Time.time the pick was accepted</summary>
            public float PickTime;
            /// <summary>Failed picks on the current Item</summary>
            public int Attempts;
        }

        /// <summary>True when the list is used as a wish list (Pick Multiple + Include list with entries)</summary>
        private bool WishList => PickMultiple && items != null && !items.Empty && items.include;

        /// <summary>Caches the Super Item Manager, searches the closest free Item and starts moving to it</summary>
        public override void StartTask(MAnimalBrain brain, int index)
        {
            var manager = brain.Animal.FindComponent<SuperItemManager>();
            brain.TasksVars[index].mono = manager; //Store the Super Item Manager on the TaskVars

            var state = brain.TaskState<PickState>(index);
            state.Item = null;    //reset per activation (typed state persists across re-entries)
            state.Picked = false;
            state.Moving = false;
            state.Attempts = 0;

            if (manager == null)
            {
                Debug.Log("No Super Item Manager Found on the Animal", brain.Animal);
                brain.TaskDone(index);
                return;
            }

            if (ListComplete(manager))
            {
                if (brain.debug) Debug.Log($"[{brain.Animal.name}] Pick Item: every Item of the list is already owned", brain.Animal);
                brain.TaskDone(index);
                return;
            }

            if (!FindItem(brain, manager, state))
            {
                if (brain.debug) Debug.Log($"[{brain.Animal.name}] Pick Item: no free Super Item found within [{SearchRadius.Value}]", brain.Animal);
                if (!KeepSearching) brain.TaskDone(index); //Keep Searching: UpdateTask retries every Update Interval
                return;
            }

            GoToItem(brain, state);
            TryPick(brain, manager, state);
        }

        /// <summary>Picks the Item as soon as the AI is in distance (searching again if it was taken), then continues with the next entry of the list or finishes</summary>
        public override void UpdateTask(MAnimalBrain brain, int index)
        {
            var manager = brain.TasksVars[index].mono as SuperItemManager;
            var state = brain.TaskState<PickState>(index);

            if (manager == null) { brain.TaskDone(index); return; }

            if (!state.Picked)
            {
                if (state.Item == null || !state.Item.IsFree) //No Item yet, or someone else took it: look for one
                {
                    if (!SearchNext(brain, manager, state)) { if (!KeepSearching) Finish(brain, index, state); return; }
                }

                TryPick(brain, manager, state);
            }
            else if (state.Item == null || state.Item.IsInManager || !state.Item.isActiveAndEnabled) //The pick landed (or the Item is gone)
            {
                if (!WishList || ListComplete(manager)) { Finish(brain, index, state); return; }

                //Wish list: go for the next entry
                state.Item = null;
                state.Picked = false;
                state.Attempts = 0;

                if (SearchNext(brain, manager, state)) TryPick(brain, manager, state);
                else if (!KeepSearching) Finish(brain, index, state);
            }
            else if (Time.time - state.PickTime > state.Item.PickDuration + PickTimeout) //Accepted but the Item never reached the Manager
            {
                state.Picked = false;
                state.Attempts++;

                if (state.Attempts >= MaxAttempts)
                {
                    if (brain.debug) Debug.Log($"[{brain.Animal.name}] Pick Item: could not pick [{state.Item.name}] after {MaxAttempts} attempts", brain.Animal);
                    Finish(brain, index, state);
                }
            }
        }

        /// <summary>Resumes the movement to the Brain Target if the Task was interrupted while travelling to the Item</summary>
        public override void ExitAIState(MAnimalBrain brain, int index)
        {
            base.ExitAIState(brain, index);

            var state = brain.TaskState<PickState>(index);
            if (state.Moving) RestoreTarget(brain, state);
        }

        /// <summary>Searches an Item and starts moving to it. False when nothing was found</summary>
        private bool SearchNext(MAnimalBrain brain, SuperItemManager manager, PickState state)
        {
            if (!FindItem(brain, manager, state)) return false;
            GoToItem(brain, state);
            return true;
        }

        /// <summary>Closest free Super Item inside the Search Radius that passes the ID filter (or is still missing from the wish list). Stored on the state</summary>
        private bool FindItem(MAnimalBrain brain, SuperItemManager manager, PickState state)
        {
            var origin = brain.Animal.transform.position;
            var maxSqr = SearchRadius * SearchRadius;
            var filter = items != null && !items.Empty;
            var wishList = WishList;

            float closestSqr = float.MaxValue;
            SuperItem best = null;

            foreach (var item in SuperItem.AllItems)
            {
                if (item == null || !item.IsFree) continue;                                //Equipped, holstered or disabled

                if (wishList) { if (Missing(manager, item.ItemType) <= 0) continue; }      //Already own enough of this type
                else if (filter && !items.Check(item.ItemType, true)) continue;            //Include / Exclude filter

                var sqrDistance = (item.transform.position - origin).sqrMagnitude;
                if (sqrDistance > maxSqr || sqrDistance >= closestSqr) continue;

                closestSqr = sqrDistance;
                best = item;
            }

            if (best != state.Item) state.Attempts = 0; //A different Item gets a fresh attempt counter
            state.Item = best;
            return best != null;
        }

        /// <summary>Wish list: how many Items of this type are still missing (entries in the list minus the owned ones)</summary>
        private int Missing(SuperItemManager manager, SuperItemID id)
        {
            if (id == null) return 0;

            int needed = 0;
            foreach (var entry in items.IDs)
                if (entry != null && entry.ID == id.ID) needed++;

            return needed - manager.Count_Owned(id);
        }

        /// <summary>Wish list: true when every entry of the list is owned (equipped or holstered). Always false on a single pick</summary>
        private bool ListComplete(SuperItemManager manager)
        {
            if (!WishList) return false;

            foreach (var id in items.IDs)
                if (id != null && Missing(manager, id) > 0) return false;

            return true;
        }

        /// <summary>Sets the Item position as the AI destination, keeping the Brain Target untouched</summary>
        private void GoToItem(MAnimalBrain brain, PickState state)
        {
            if (!MoveToItem || state.Item == null) return;

            brain.AIControl.SetDestination(state.Item.transform.position, true);
            brain.AIControl.UpdateDestinationPosition = false; //A moving Brain Target (Player) must not overwrite the Item destination every frame
            state.Moving = true;
        }

        /// <summary>Requests the pick to the Manager when the AI is in distance. Retried every update until the Manager accepts it (pick cooldown)</summary>
        private void TryPick(MAnimalBrain brain, SuperItemManager manager, PickState state)
        {
            if (state.Item == null || !InPickDistance(brain, state.Item)) return;

            var equip = PickAs switch
            {
                PickAsOption.Equip => true,
                PickAsOption.Holster => false,
                _ => manager.ResolveAutoEquip(state.Item), //Manager global [Auto Equip] ON → hands; OFF → the Item's own flag
            };

            if (Instant)
            {
                if (equip) manager.Pick_and_Equip(state.Item);
                else manager.Pick_and_Holster(state.Item);
                state.Picked = true;
            }
            else
            {
                state.Picked = manager.Item_Pick(state.Item, equip); //Plays the Item's Pick Mode and waits its Pick Duration. False while the pick cooldown runs
            }

            if (state.Picked) state.PickTime = Time.time;
        }

        /// <summary>True when the AI is close enough to pick the Item (Pick Distance, or the AI Control has arrived)</summary>
        private bool InPickDistance(MAnimalBrain brain, SuperItem item)
        {
            if (PickDistance > 0)
                return Vector3.Distance(brain.Animal.transform.position, item.transform.position) <= PickDistance;

            return !MoveToItem || brain.AIControl.HasArrived;
        }

        /// <summary>Restores the movement to the Brain Target and marks the Task as Done</summary>
        private void Finish(MAnimalBrain brain, int index, PickState state)
        {
            RestoreTarget(brain, state);
            brain.TaskDone(index);
        }

        /// <summary>Resumes the movement to the Brain Target (SetTarget re-enables the destination update)</summary>
        private void RestoreTarget(MAnimalBrain brain, PickState state)
        {
            state.Moving = false;

            if (ReturnToTarget && brain.Target != null)
                brain.AIControl.SetTarget(brain.Target, true);
        }

#if UNITY_EDITOR && MALBERS_DEBUG
        public override void DrawGizmos(MAnimalBrain brain)
        {
            if (brain == null || brain.Animal == null) return;

            var center = brain.Animal.transform.position;
            var up = brain.Animal.UpVector;

            UnityEditor.Handles.color = debugColor;
            UnityEditor.Handles.DrawSolidDisc(center, up, SearchRadius);
            UnityEditor.Handles.color = new Color(debugColor.r, debugColor.g, debugColor.b, 1f);
            UnityEditor.Handles.DrawWireDisc(center, up, SearchRadius);

            //Pick distance: the explicit Pick Distance, or the AI Control Stopping Distance when it is zero (HasArrived) — MWC
            var pickRadius = PickDistance > 0 ? PickDistance.Value : (brain.AIControl != null ? brain.AIControl.StoppingDistance : 0f);
            if (pickRadius > 0)
            {
                UnityEditor.Handles.color = Color.yellow;
                UnityEditor.Handles.DrawWireDisc(center, up, pickRadius, 2f);
            }
        }
#endif

        private void Reset() => Description = "Search free Super Items around the AI, walk to them (keeping the current Target) and pick them: one Item, or every entry of the list with [Pick Multiple]";
    }
}
