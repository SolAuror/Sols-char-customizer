// Last Change: 9/17/2026 6:05pm
using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Utilities;
using UnityEngine;

#if UNITY_EDITOR
#endif


namespace MalbersAnimations.Items
{
    [System.Serializable]
    public abstract class ItemProcessorDamagerMaster : ItemProcessor
    {
        [Tooltip("Trigger Profile to use when the AttackIndex is activated. This is used to activate the Trigger Proxy and Collider. -1 will ignore profiles")]
        public IntReference profile = new(-1);
        public override ExecuteProcessor InitialExecute => ExecuteProcessor.OnTriggerEnter;
        public override void ItemEquipped(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (action.Trigger == null)
            {
                Debug.LogError($"[Item: {item.name}] The Damager Trigger is not set. Please assign a Trigger Proxy to the Set[{set.Name}] Action[{action.name.Value}] ", item);
                Active = false;
                return;
            }
        }

        public override void EnterTrigger(GameObject root, Collider col, TriggerProxy proxy, SuperItem item, ItemActionSet set, ItemAction action, SuperItemManager user)
        {
            if (!ValidTrigger(proxy, item, set, action, user)) return; // MWC — shared Trigger/Profile/ID filter, see ValidTrigger

            var damagee = col.FindInterface<MDamageable>();                                     //Get the Animal on the Other collider
            if (damagee == null) return;

            HandDamager(damagee, col, proxy, user);
            TriggerValidHit(damagee, item);
        }

        /// <summary> Hands the attacker and a default hit Direction/Point to the Damageable, so Align To Damage, the Damager Reaction and the directional Damage animations know where the hit came from. A more precise Direction (E.g. the sampled one of the Physics Force processor) can still overwrite it before the hit is applied </summary>
        protected static void HandDamager(MDamageable damagee, Collider col, TriggerProxy proxy, SuperItemManager user)
        {
            var attacker = user.Animal != null ? user.Animal.gameObject : user.gameObject;
            damagee.PrepareDamage(attacker);

            if (damagee.PreDamage.Direction != Vector3.zero) return;  //Another processor of this hit already handed a Direction

            var trigger = proxy != null && proxy.ownCollider ? proxy.ownCollider : col;
            var center = trigger.bounds.center;
            var direction = (col.bounds.center - center).normalized;  //From the weapon trigger to the hit collider, same rule as the legacy Attack Trigger
            if (direction == Vector3.zero) direction = user.transform.forward;

            damagee.PrepareDamage(direction, ClosestPoint(col, center));
        }

        /// <summary> ClosestPoint is not supported on concave Mesh Colliders, fall back to the bounds there </summary>
        protected static Vector3 ClosestPoint(Collider col, Vector3 point)
        {
            if (col is MeshCollider mesh && !mesh.convex) return col.bounds.ClosestPoint(point);
            return col.ClosestPoint(point);
        }

        /// <summary> True when the Trigger Enter belongs to this processor: the Action Trigger, the Damager Profile and the Damager ID all match </summary>
        protected bool ValidTrigger(TriggerProxy proxy, SuperItem item, ItemActionSet set, ItemAction action, SuperItemManager user)
        {
            var FullActive = item.enabled && set.Active && action.Active && this.Active;
            if (!FullActive) return false; //If the Item Processor is not active, do nothing

            var Trigger = action.Trigger;

            if (Trigger != null && Trigger != proxy) return false;
            if (profile >= 0 && user.CurrentDamagerProfile != profile) return false;    //Only Activate when the Active Damager Index is the same as the one in the Item Processor
            if (Trigger.ID.Value > 0 && Trigger.ID.Value != user.CurrentDamagerID) return false;                  //If the Trigger ID is not the same as the one in the Item Processor, skip

            return true;
        }

        public abstract void TriggerValidHit(MDamageable damageable, SuperItem item);

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (action.Trigger == null)
            {
                return $"Requires a Trigger Proxy to be assigned to the Action. Please assign one to the Action. [{action.Name}]";
            }
            return string.Empty;
        }
    }

    //-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [System.Serializable, AddTypeMenu("Damager/Critical")]
    [HelpURL(ItemDocs.Processors + "damager-critical")]
    public class ItemProcessorDamagerCritical : ItemProcessorDamagerMaster
    {

        [Tooltip("Critical Change (0 - 1)\n1 means it will be always critical")]
        public FloatReference criticalChance = new(0);

        [Tooltip("If the Damage is critical, the Stat modifier value will be multiplied by the Critical Multiplier")]
        public FloatReference criticalMult = new(2);

        public override string DynamicName => $"[Damager] Crit-Chance [{criticalChance.Value * 100}] Profile [{profile.Value}]";

        public override void TriggerValidHit(MDamageable damageable, SuperItem item)
        {
            damageable.PrepareDamage(criticalChance > Random.value, criticalMult);

        }
        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action) { } //Do nothing because is done in the Trigger
    }


    [System.Serializable, AddTypeMenu("Damager/Can Miss")]
    [HelpURL(ItemDocs.Processors + "damager-can-miss")]
    public class ItemProcessorDamagerCanMiss : ItemProcessorDamagerMaster
    {
        [Tooltip("Miss Change (0 - 1)\n1 means it will always Miss")]
        public FloatReference MissChance = new(0);

        public override string DynamicName => $"[Damager] Miss Chance [{MissChance.Value * 100}] Profile [{profile.Value}]";

        public override void TriggerValidHit(MDamageable damageable, SuperItem item)
        {
            damageable.PrepareDamage(MissChance > Random.value);
        }
    }

    //MWC — reworked: the damage is a single [Stat Modifier]. Its [Source] Stats come from the modifier's own [Source Stats]
    //slot: leave it EMPTY and the wielding character's Stats are used on equip; set it in the inspector (E.g. the Item's own
    //Stats) and it is kept as is. The old [Stats From] Local/FromCharacter switch is gone. Add several of these processors;
    //their damage stacks on the target via the additive PrepareDamage on MDamageable. Replaces the old single [Owner Base Stat] add-on.
    [System.Serializable, AddTypeMenu("Damager/Do Damage (Attack Stat)")]
    [HelpURL(ItemDocs.Processors + "damager-do-damage-attack-stat")]
    public class ItemProcessorDamagerStat : ItemProcessorDamagerMaster
    {
        [Tooltip("Damage applied to the Damagee.\n" +
            "[Target] Stat to modify on the Damagee (E.g. Health) and how to modify it.\n" +
            "[Value From] can be a Min-Max range, or a [Source] expression read from the [Source Stats] component.\n" +
            "Leave [Source Stats] EMPTY to read the Stats of the character wielding the Item. Set it to read a specific Stats (E.g. the Item's own)")]
        public StatModifier damage = new(null, 10); // MWC — replaces the old [AttackStat] + [modify] + [TargetStat] fields

        //MWC — true when the Source Stats were taken from the character on equip, so they are released on unequip
        //and an inspector-assigned Stats is never touched
        [System.NonSerialized] private bool sourceFromCharacter;


        public override string DynamicName =>
            $"[Damager] " +
            $"{(damage.from == StatValueFrom.Stat ? damage.SourceExpression : $"[{damage.MinValue.Value} - {damage.MaxValue.Value}]")} " + // MWC — the whole Source expression
            $"[{(damage.ID != null ? damage.ID.name : "<None>")},{damage.modify}]";

        public override void ItemEquipped(SuperItem item, ItemActionSet set, ItemAction action)
        {
            base.ItemEquipped(item, set, action);

            //MWC — default Source Stats: the wielding character. An inspector-assigned Stats (E.g. the Item's own) is kept
            if (damage.SourceStats == null && item.User != null)
            {
                damage.SourceStats = item.User.FindComponent<Stats>();
                sourceFromCharacter = damage.SourceStats != null;
            }
        }

        public override void ItemUnequipped(SuperItem item, ItemActionSet set, ItemAction action)
        {
            //MWC — release the character Stats so an unequipped Item stops referencing its last wielder
            if (sourceFromCharacter)
            {
                damage.SourceStats = null;
                sourceFromCharacter = false;
            }

            base.ItemUnequipped(item, set, action);
        }

        public override void TriggerValidHit(MDamageable damageable, SuperItem item)
        {
            var SendStat = damage.Resolve();

            //MWC — additive on the damagee: several Damager processors (e.g. Local + FromCharacter) stack into one hit
            damageable.PrepareDamage(SendStat, /*pureDamage*/ false);
        }

        //Validate the Source Stat against the Item's Stats component (same pattern as Reload/Projectile)
        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            var validation = base.Validation(item, set, action);
            if (!string.IsNullOrEmpty(validation)) validation += "\n";

            if (damage.ID == null) validation += "Missing the Target Stat";

            //MWC — the Source expression needs a Stat only when [Value From] is [Stat]
            if (damage.from == StatValueFrom.Stat)
            {
                var sourceStat = damage.SourceStat;

                if (sourceStat == null)
                {
                    validation += "Missing the Source Stat ID [The Stat used as the damage source]";
                }
                else
                {
                    //MWC — only an inspector-assigned [Source Stats] can be checked at edit time. When it is empty the Stats
                    //come from the wielding character (unknown here), so they are resolved at runtime instead.
                    validation += damage.ValidateSourceStats(damage.SourceStats);
                }
            }

            return validation;
        }


#if UNITY_EDITOR
        public override void Reset()
        {
            //MWC — auto-fill the common setup: the weapon's [Attack] Stat substracts the target's [Health]
            damage = new StatModifier(MTools.GetInstance<StatID>("Health"), 10)
            {
                modify = StatOption.SubstractValue,
                from = StatValueFrom.Stat,
                sources = new() { new StatSource(MTools.GetInstance<StatID>("Attack")) }, // MWC — first term of the expression
            };
        }

        //MWC — auto-configure the processor the moment it's created in the Item inspector: auto-fill the Target/Source
        //Stat IDs (looks up the StatID assets by name). No Stats component is added to the Item anymore: by default the
        //Source Stats are the wielding character's. Add a Stats next to the Super Item and assign it as [Source Stats] to use the Item's own
        public override void OnCreated(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (damage.ID == null) damage.ID = MTools.GetInstance<StatID>("Health");

            //MWC — seed the Source expression with the [Attack] Stat when it is still empty
            if (!damage.HasSources)
                damage.sources = new() { new StatSource(MTools.GetInstance<StatID>("Attack")) };
        }
#endif
    }


    [System.Serializable, AddTypeMenu("Damager/Add Element")]
    [HelpURL(ItemDocs.Processors + "damager-add-element")]
    public class ItemProcessorElement : ItemProcessorDamagerMaster
    {
        [Tooltip("Stat to modify on the Damagee")]
        public StatElement element;

        public override string DynamicName => $"[Damager] Element: [{(element != null ? $"{element.name}" : "<None>")}] Profile [{profile.Value}]";

        public override void TriggerValidHit(MDamageable damageable, SuperItem item)
        {
            damageable.PrepareDamage(element);
        }
    }

    [System.Serializable, AddTypeMenu("Damager/Override Hit Reaction")]
    [HelpURL(ItemDocs.Processors + "damager-override-hit-reaction")]
    public class ItemProcessorHitReaction : ItemProcessorDamagerMaster
    {
        [Tooltip("Override Damageable Reaction")]
        public Reaction2 HitReaction = new();

        public override string DynamicName => $"[Damager] Hit Reaction";

        public override void TriggerValidHit(MDamageable damageable, SuperItem item)
        {
            damageable.PrepareDamage(HitReaction);
        }
    }

    //Lets an attack decide how the TARGET takes the hit. E.g. a Heavy or Charge Attack resolves against a [Guard Break] Profile
    //while the enemy is Blocking (its Shield put it on a [Block] Profile with Multiplier 0), so the hit lands with full damage,
    //its own Reaction and its own Animation. The Profile can be one the target already owns (found by name), one authored here,
    //or a Damageable Profile asset. [This Hit Only] resolves just this hit with it; [Until Restored] sets it on the target.
    [System.Serializable, AddTypeMenu("Damager/Override Target Damageable Profile")]
    [HelpURL(ItemDocs.Processors + "damager-override-target-profile")]
    public class ItemProcessorTargetDamageProfile : ItemProcessorDamagerMaster
    {
        /// <summary> Where the override Profile comes from </summary>
        public enum ProfileSource { TargetDamageProfile, Local, Asset }

        /// <summary> How long the override Profile stays on the target </summary>
        public enum OverrideScope { ThisHitOnly, UntilRestored }

        [Tooltip("Where the Profile comes from:\n" +
            "• Target Damage Profile: one of the Profiles already on the target's Damageable, found by name (E.g. the Goblin's own [Guard Break]). A target without it takes the hit as usual.\n" +
            "• Local: a Profile authored here, applied to any target.\n" +
            "• Asset: a Damageable Profile asset shared between Items")]
        public ProfileSource source = ProfileSource.TargetDamageProfile;

        [Tooltip("Name of the Profile on the target's Damageable. [Default] uses its Default Profile (the plain 'ignore the Block' case)")]
        [Hide(nameof(source), (int)ProfileSource.TargetDamageProfile)]
        public StringReference profileName = new("Guard Break");

        [Tooltip("Profile applied to the target. Its Multiplier, Reactions, Elements and Conditions replace the ones of the Profile the target is on")]
        [Hide(nameof(source), (int)ProfileSource.Local)]
        public MDamageableProfile localProfile;

        [Tooltip("Damageable Profile asset applied to the target")]
        [Hide(nameof(source), (int)ProfileSource.Asset)]
        [RequiredField] public MDamageableProfileVar ProfileVar;

        [Tooltip("• This Hit Only: the hit resolves with the Profile (Multiplier, Reactions, Conditions), then the target goes on with the Profile it had.\n" +
            "• Until Restored: the Profile is SET on the target and its Hit Boxes, and stays until the target's own logic changes it (E.g. its Shield restoring the Default Profile when the Block is released)")]
        public OverrideScope scope = OverrideScope.ThisHitOnly;

        [Tooltip("Skip a target that is already on its Default Profile (not Blocking, Parrying…), so a target nobody restores is not left on this Profile forever")]
        [Hide(nameof(scope), (int)OverrideScope.UntilRestored)]
        public BoolReference onlyWhenNotDefault = new(true);

        /// <summary> Name shown for the Profile in the processor header </summary>
        private string ProfileLabel => source switch
        {
            ProfileSource.TargetDamageProfile => $"Target: {profileName.Value}",
            ProfileSource.Asset => ProfileVar != null ? $"Asset: {ProfileVar.name}" : "<MISSING>",
            _ => string.IsNullOrEmpty(localProfile.name) ? "<UNNAMED>" : $"Local: {localProfile.name}",
        };

        public override string DynamicName =>
            $"[Damager] Target Profile [{ProfileLabel}] {(scope == OverrideScope.ThisHitOnly ? "This Hit" : "Until Restored")} Profile [{profile.Value}]";

        public override void TriggerValidHit(MDamageable damageable, SuperItem item)
        {
            if (scope == OverrideScope.ThisHitOnly)
            {
                //The hit Damageable is the one resolving the damage, so it is the only one that needs the override
                if (source == ProfileSource.TargetDamageProfile) damageable.PrepareDamage(profileName.Value);
                else if (source == ProfileSource.Local) damageable.PrepareDamage(localProfile);
                else if (ProfileVar != null) damageable.PrepareDamage(ProfileVar.value);
                return;
            }

            //Until Restored: set it on the Root and every Hit Box, the same way the Damageable processor sets a Block Profile
            var root = damageable.Root != null ? damageable.Root : damageable;

            if (onlyWhenNotDefault.Value && root.IsOnDefaultProfile && damageable.IsOnDefaultProfile) return;

            var all = root.GetComponentsInChildren<MDamageable>();

            foreach (var d in all)
            {
                if (source == ProfileSource.TargetDamageProfile) d.Profile_Set(profileName.Value);
                else if (source == ProfileSource.Local) d.Profile_Set(localProfile);
                else if (ProfileVar != null) d.Profile_Set(ProfileVar.value);
            }
        }

        public override string Validation(SuperItem item, ItemActionSet set, ItemAction action)
        {
            var validation = base.Validation(item, set, action);

            switch (source)
            {
                case ProfileSource.TargetDamageProfile when string.IsNullOrEmpty(profileName.Value):
                    validation += "Missing the name of the Profile to find on the target. Use [Default] for its Default Profile. "; break;
                case ProfileSource.Local when string.IsNullOrEmpty(localProfile.name):
                    validation += "The Local Profile needs a name. "; break;
                case ProfileSource.Asset when ProfileVar == null:
                    validation += "Missing the Damageable Profile asset. "; break;
            }

            return validation;
        }

        /// <summary> Seeds the Local Profile with a plain full-damage [Guard Break] so it is usable right after being added </summary>
        public override void Reset()
        {
            localProfile = MDamageableProfile.New("Guard Break");
        }
    }

    //MWC — Physics interaction of the old Weapon Manager (MDamager.TryPhysics): when the Damager Trigger hits a Rigidbody
    //it gets pushed with a random force between [Min Force] and [Max Force] along the Attack Direction, at the closest
    //point of the hit collider. The Direction and Hit Point are also handed to the Damageable (if any), so Directional
    //Damage, Ragdoll Death and the Damage Reactions know where the hit came from. Works on any Rigidbody, a Damageable is NOT required.
    [System.Serializable, AddTypeMenu("Damager/Physics Force")]
    [HelpURL(ItemDocs.Processors + "damager-physics-force")]
    public class ItemProcessorDamagerPhysics : ItemProcessorDamagerMaster
    {
        [Tooltip("MIN Force to apply to the Rigidbody hit by the Damager Trigger")]
        public FloatReference minForce = new(200f);

        [Tooltip("MAX Force to apply to the Rigidbody hit by the Damager Trigger. The applied force is a random value between Min and Max")]
        public FloatReference maxForce = new(300f);

        [Tooltip("Force mode to apply to the Rigidbody hit by the Damager Trigger")]
        public ForceMode forceMode = ForceMode.Force;

        [Tooltip("True: the Attack Direction is calculated using the movement of the Damager Trigger while the Action is playing.\n" +
            "False: The Attack Direction is the Character Forward Direction")]
        public bool attackDirection = false;

        [Tooltip("Rigidbodies set as Kinematic are ignored (a force does nothing on them)")]
        public bool ignoreKinematic = true;

        /// <summary> Direction of the last attack (movement of the Trigger, or the Character forward) </summary>
        [System.NonSerialized] private Vector3 direction;

        /// <summary> Trigger movement sampler, alive while the Action plays and [Attack Direction] is on </summary>
        [System.NonSerialized] private System.Collections.IEnumerator C_Direction;

        /// <summary> Hit data of the current Trigger Enter, consumed by TriggerValidHit </summary>
        [System.NonSerialized] private Vector3 hitPoint;
        [System.NonSerialized] private Collider hitCollider;

        public override string DynamicName => $"[Damager] Physics Force [{minForce.Value} - {maxForce.Value}] {forceMode} Profile [{profile.Value}]";

        /// <summary> Random force between Min and Max, same as the old MDamager.Force </summary>
        public float Force => Mathf.Lerp(minForce, maxForce, Random.value);

        public override void EnterTrigger(GameObject root, Collider col, TriggerProxy proxy, SuperItem item, ItemActionSet set, ItemAction action, SuperItemManager user)
        {
            if (!ValidTrigger(proxy, item, set, action, user)) return;

            //Fallback to the character forward when the Trigger movement is not being sampled (or it has not moved yet)
            if (!attackDirection || direction == Vector3.zero) direction = user.transform.forward;

            var trigger = proxy.ownCollider ? proxy.ownCollider : col;
            var center = trigger.bounds.center;

            hitCollider = col;
            hitPoint = ClosestPoint(col, center);



            TryPhysics(col.attachedRigidbody, item);

            var damagee = col.FindInterface<MDamageable>();                                     //Get the Animal on the Other collider
            if (damagee == null) return;

            HandDamager(damagee, col, proxy, user);
            TriggerValidHit(damagee, item);  //Overwrites the default Direction and Hit Point with the sampled ones
        }

        /// <summary> Hands the Attack Direction, Hit Point, Hit Collider and Force Mode to the Damageable so its reactions know where the hit came from </summary>
        public override void TriggerValidHit(MDamageable damageable, SuperItem item)
        {
            damageable.HitCollider = hitCollider;
            damageable.LastForceMode = forceMode;
            damageable.PrepareDamage(direction, hitPoint);
        }

        /// <summary> Pushes the Rigidbody along the Attack Direction at the Hit Point. Returns false when there is nothing to push </summary>
        private bool TryPhysics(Rigidbody rb, SuperItem item)
        {
            if (rb == null) return false;
            if (ignoreKinematic && rb.isKinematic) return false;

            var force = Force;
            if (force <= 0) return false;

            var push = direction * force;
            rb.AddForceAtPosition(push, hitPoint, forceMode);

            if (item.debug)
            {
                MDebug.DrawWireSphere(hitPoint, Color.red, 0.1f, 2f);
                MDebug.Draw_Arrow(hitPoint, push.normalized, Color.red, 2f);
            }

            item.Debugging($"Physics Force [{force:F1}] {forceMode} → [{rb.name}]", rb);
            return true;
        }

        #region MWC — Attack Direction sampling (the old MDamager.I_CalculateDirection), alive while the Action plays
        public override void ActionStart(SuperItem item, ItemActionSet set, ItemAction action)
        {
            if (!attackDirection) return;

            var trigger = action.Trigger != null ? action.Trigger.ownCollider : null;
            if (trigger == null) return;

            StopDirection(item);
            C_Direction = I_CalculateDirection(trigger, item);
            item.StartCoroutine(C_Direction);
        }

        public override void ActionEnd(SuperItem item, ItemActionSet set, ItemAction itemAction) => StopDirection(item);
        public override void ActionExit(SuperItem item, ItemActionSet set, ItemAction itemAction) => StopDirection(item);
        public override void SetExit(SuperItem item, ItemActionSet set, ItemAction itemAction) => StopDirection(item);
        public override void OnDisable(SuperItem item, ItemActionSet set, ItemAction itemAction) => StopDirection(item);

        public override void ItemUnequipped(SuperItem item, ItemActionSet set, ItemAction action)
        {
            StopDirection(item);
            base.ItemUnequipped(item, set, action);
        }

        /// <summary> Stops the Trigger movement sampler and forgets the last direction </summary>
        private void StopDirection(SuperItem item)
        {
            if (C_Direction != null)
            {
                if (item != null) item.StopCoroutine(C_Direction);
                C_Direction = null;
            }
            direction = Vector3.zero;
        }

        /// <summary> Every frame: the Attack Direction is the movement of the Damager Trigger since the last frame </summary>
        private System.Collections.IEnumerator I_CalculateDirection(Collider trigger, SuperItem item)
        {
            var lastPos = trigger.bounds.center;
            var debColor = (Color.red + Color.yellow) / 2;
            debColor.a = 0.7f;

            while (trigger != null)
            {
                var center = trigger.bounds.center;
                var newDir = (center - lastPos).normalized;                    //Calculate the direction of the attack
                if (newDir != Vector3.zero) direction = newDir;
                lastPos = center;

                if (item.debug && direction != Vector3.zero) MDebug.Draw_Arrow(center, direction, debColor, 0.5f);
                yield return null;
            }

            C_Direction = null;
        }
        #endregion

        public override void Execute(SuperItem item, ItemActionSet set, ItemAction action) { } //Do nothing because is done in the Trigger
    }
}