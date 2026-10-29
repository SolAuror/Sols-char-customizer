// Last Change: 10/12/2026 10:44am
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;
using MalbersAnimations.Events;
using System.Collections;
using MalbersAnimations.Conditions;


#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
#endif

namespace MalbersAnimations.Controller
{
    [DefaultExecutionOrder(3000)]
    [AddComponentMenu("Malbers/Animal Controller/Combo Manager")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/main-components/combo-manager")]
    public class ComboManager : MonoBehaviour
    {
        [RequiredField] public MAnimal animal;

        [Tooltip("Conditions to check when trying to play a combo. If the conditions are not met the combo will not be played.")]
        public Conditions2 ComboConditions;

        public int Branch = 0;
        public List<Combo> combos = new();

        [Tooltip("Shareable Combo Set assets. Their combos are cloned into this manager at runtime so the same asset can be shared by many animals. MWC")]
        public List<ComboSet> comboAssets = new();

        private List<Combo> extraStateCombos;

        //MWC: asset -> its per-manager instantiated runtime clone
        private Dictionary<ComboSet, ComboSet> runtimeSets;
        /// <summary>Every Combo Set asset ever loaded on this manager (authored or by ComboSet.Play), so a disable/enable cycle reloads all of them</summary>
        private readonly List<ComboSet> runtimeLoadedAssets = new();
        //MWC: number of inline (authored) combos before asset combos are appended; -1 until OnEnable runs
        private int inlineComboCount = -1;

        [Tooltip("Current Active Combo Index")]
        public IntReference ActiveComboIndex = new(0);

        /// <summary>Current Active Combo</summary>
        public Combo ActiveCombo { get; internal set; }

        /// <summary>Blocks playing combos while the animal is Sleep.</summary>
        [Tooltip("Disable Combo Manager if the animal is Sleep.")]
        public bool DisableOnSleep = true;

        /// <summary>Blocks playing combos while the animal has its Input locked.</summary>
        [Tooltip("Disable Combo Manager if the animal has LockInput enabled.\n\nTurn this OFF for Riders: mounting sets the rider's MAnimal to Sleep, which also sets LockInput, and that would veto every mounted combo.")]
        public bool DisableOnLockInput = true;

        public int ActiveComboSequenceIndex { get; internal set; }

        public ComboSequence ActiveComboSequence => ActiveCombo?.CurrentSequence; // MWC — null-safe: OnModeEnd could NRE here when the combo was deactivated (SetActiveCombo(-1)) while a mode was still playing

        /// <summary> Is the manager playing a combo? </summary>
        public bool PlayingCombo { get; internal set; }

        public System.Action OnComboRestart;
        public System.Action OnComboFinished;
        public System.Action OnComboIncomplete;


        public bool debug;


        private void OnEnable()
        {
            if (!animal) animal = this.FindComponent<MAnimal>();

            ActiveCombo = null; //Reset the Active Combo

            if (!animal)
            {
                Debug.LogWarning("The Combo Manager needs an Animal Component", gameObject);
            }
            else
            {
                animal.OnModeEnd.AddListener(OnModeEnd);
                animal.OnStateActivate.AddListener(OnStateEnter);


                //Save the Modes in the Combo itself
                for (int i = 0; i < combos.Count; i++)
                    InitCombo(combos[i], i);

                inlineComboCount = combos.Count; //MWC snapshot before appending asset combos

                //MWC: clone & append the shared Combo Set assets
                if (comboAssets != null)
                    foreach (var asset in comboAssets)
                        if (asset != null) LoadComboSet(asset);

                //Sets loaded at runtime by ComboSet.Play (an Item's Combo processor) are unloaded on disable like the
                //authored ones, but were never reloaded here, so ActiveComboIndex still pointed past the inline combos
                //and combos[ActiveComboIndex] threw after a disable/enable of the character (hiding the player).
                for (int i = 0; i < runtimeLoadedAssets.Count; i++)
                    LoadComboSet(runtimeLoadedAssets[i]);

                if (ActiveComboIndex >= combos.Count) //Backstop: the combo this index pointed at no longer exists
                {
                    Debugging($"Active Combo index [{ActiveComboIndex.Value}] is out of range after enable. No combo selected");
                    ActiveComboIndex = -1;
                }

                if (ActiveComboIndex >= 0)
                {
                    ActiveCombo = combos[ActiveComboIndex];
                    ActiveComboSequenceIndex = 0;
                    PlayingCombo = false;
                    RestartActiveCombo();
                }
            }
        }
        private void OnDisable()
        {
            if (animal)
            {
                animal.OnStateActivate.RemoveListener(OnStateEnter);
                animal.OnModeEnd.RemoveListener(OnModeEnd);
            }
            StopAllCoroutines();

            UnloadComboSets();
        }

        /// <summary> Initializes a combo's runtime data (upgrade, index, cooldown, cached mode). </summary>
        private void InitCombo(Combo combo, int index)
        {
            combo.UpgradeToIDList(this); //Upgrade old combos to have the new IDList of states

            combo.ComboIndex = index;
            combo.FinishTime = -combo.CoolDown;

            if (combo.Mode == null) //MWC: guard so asset/inline combos with no Mode don't NRE
            {
                Debug.LogError($"[{name}] Combo '{combo.Name}' has no Mode assigned.", this);
                return;
            }

            combo.CachedMode = animal.Mode_Get(combo.Mode);

            if (combo.CachedMode == null)
                Debug.LogError($"Animal {animal.name} does not have the mode {combo.Mode.name}. Please Add it to your character", this);
        }

        /// <summary> Clones a shared ComboSet asset into this manager's runtime combo list (once per asset). </summary>
        public List<Combo> LoadComboSet(ComboSet asset)
        {
            if (asset == null || animal == null) return null;

            runtimeSets ??= new Dictionary<ComboSet, ComboSet>();

            if (runtimeSets.TryGetValue(asset, out var existing))
                return existing.combos; //Already loaded on this manager

            var runtime = Instantiate(asset); //Per-manager clone so a shared asset can't corrupt other animals
            runtime.StampMode();
            runtimeSets[asset] = runtime;

            if (!runtimeLoadedAssets.Contains(asset)) runtimeLoadedAssets.Add(asset); //Survives OnDisable so OnEnable can load it again

            if (inlineComboCount < 0) inlineComboCount = combos.Count; //Safety if loaded before the OnEnable snapshot

            foreach (var combo in runtime.combos)
            {
                combo.Source = asset; //MWC — remember which asset this clone came from so it can be selected by identity
                combos.Add(combo);
                InitCombo(combo, combos.Count - 1);
            }

            Debugging($"Loaded Combo Set [{asset.name}] with {runtime.combos.Count} combo(s)");

            return runtime.combos;
        }

        /// <summary> Removes cloned asset combos and destroys their runtime instances. </summary>
        private void UnloadComboSets()
        {
            if (runtimeSets != null)
            {
                foreach (var runtime in runtimeSets.Values)
                    if (runtime != null) Destroy(runtime);
                runtimeSets.Clear();
            }

            //Truncate the appended asset combos back to the inline authored ones
            if (inlineComboCount >= 0 && combos.Count > inlineComboCount)
                combos.RemoveRange(inlineComboCount, combos.Count - inlineComboCount);

            inlineComboCount = -1;
        }

        private void OnStateEnter(int stateID)
        {
            //Re-enable the combos disabled by Finish/Interrupt that are waiting for this State
            CheckEnableOnState(stateID);

            //Do nothing if we do not have a state to search
            // MWC — was checking the DisableOnSleep *flag* instead of the animal actually being asleep,
            // so every manager with the default (true) skipped the extra-state combo swap entirely.
            if ((DisableOnSleep && animal.Sleep)
                || (DisableOnLockInput && animal.LockInput)
                || !enabled
                || ActiveCombo == null
                || ActiveComboIndex == -1
               ) return;

            var state = animal.ActiveStateID;

            if (extraStateCombos != null)
            {
                foreach (var combo in extraStateCombos)
                {
                    if (combo == ActiveCombo && combo.HasState(state))
                    {
                        break; //Find yourself and you were the first
                    }
                    if (combo.HasState(state))
                    {
                        //MWC — switch straight to the variation. Re-searching by Mode ID picked the first combo
                        //sharing that ID, which can belong to a different Combo Set (Melee Weapon ID vs Melee Super Item ID).
                        SetActiveCombo(combo.ComboIndex);
                        break;
                    }
                }
            }
        }

        /// <summary> Enables every disabled Combo listing the new Active State on its Enable On list. </summary>
        private void CheckEnableOnState(int stateID)
        {
            for (int i = 0; i < combos.Count; i++)
            {
                var combo = combos[i];

                if (combo == null || combo.active || !combo.CanEnableOnState(stateID)) continue;

                combo.active = true; //The CoolDown is left untouched: it is a separate gate
                Debugging($"Combo [{combo.Name}] <color=green>Enabled</color> by the [Enable On] States");
            }
        }

        private void OnModeEnd(int modeID, int CurrentExitAbility)
        {
            //Debug.Log($"MODE END {modeID}  CurrentExitAbility {CurrentExitAbility} : PlayingCombo {PlayingCombo}");
            if (PlayingCombo)
            {
                if (ActiveComboSequence == null) { Restart(); return; } //Weird bug

                if (ActiveComboSequence.Finisher)
                {
                    ActiveCombo.OnComboFinished.Invoke(ActiveComboSequenceIndex);
                    Debugging($"Combo <color=green>Finished </color> <b>[{ActiveComboSequenceIndex}]</b> Branch:<b>[{Branch}]</b>. <color=red> [Restarting] </color>\"");
                    Restart();

                    ActiveCombo.FinishTime = Time.time; //cache the time the combo has finished.

                    if (ActiveCombo.DisableOnFinish.Value)
                    {
                        ActiveCombo.active = false;
                    }
                }
                //Are we exiting the Current Sequence or just the Old one??? A new Sequence is playing
                //MWC — Abs on both sides. A MIRRORED sequence asks for a NEGATIVE Ability (-102 = play [102] inverted),
                //but the Mode always reports back the POSITIVE index of the Ability it actually resolved
                //(Mode.LastAbilityIndex = ActiveAbility.Index, and GetCachedAbility looks up with Mathf.Abs), so
                //'102 == -102' was always false: the [Incomplete] branch — the only thing that raises OnComboIncomplete
                //— never ran for a mirrored combo, and any listener waiting on it (an Item Action whose sole exit path
                //is the Combo callback) stayed playing forever.
                else if (Mathf.Abs(CurrentExitAbility) == Mathf.Abs(ActiveComboSequence.Ability) && !animal.ForcingMode)
                {
                    if (!animal.IsPlayingMode) // if is no longer playing a Mode then means it was interrupted or Unfinished
                    {
                        Debugging($"<color=yellow>Incomplete </color><b>[{ActiveComboSequenceIndex}]</b> Branch: <b>[{Branch}]</b>. <color=red> [*Restarting*] </color>");
                        ActiveCombo.OnComboInterrupted.Invoke(ActiveComboSequenceIndex);
                        OnComboIncomplete?.Invoke();

                        if (ActiveCombo.DisableOnInterrupt) ActiveCombo.active = false;

                        Restart();//meaning it got to the end of the combo

                        ActiveCombo.FinishTime = Time.time; //cache the time the combo has finished/Interrupted.
                    }
                }
            }
        }

        /// <summary> Drops PlayingCombo only when the outgoing combo has nothing left in flight. </summary>
        private void ReleasePlayingCombo()
        {
            //MWC — Clearing PlayingCombo while the outgoing combo's Mode was STILL PLAYING made the pending OnModeEnd
            //bail out at its 'if (PlayingCombo)' gate, so Restart() never ran and neither OnComboRestart nor
            //OnComboIncomplete was ever raised. An ItemProcessorCombo has no ModeEnd hook — those two events are its
            //ONLY exit path — so its Item Action stayed latched IsPlaying forever whenever the INCOMING combo failed
            //to start a sequence of its own (pressed outside the buffer window, no matching sequence, Mode vetoed…).
            //Keeping the flag lets that Mode End reach OnModeEnd, where the incoming combo's cleared CurrentSequence
            //hits the null guard and Restart() releases every listener on the frame the animation really ends.
            if (PlayingCombo
                && ActiveCombo != null
                && animal != null
                && animal.IsPlayingMode
                && animal.ActiveMode == ActiveCombo.CachedMode) return;

            PlayingCombo = false;
        }

        /// <summary> Changes the Active Combo on the Manager  </summary>
        public virtual void SetActiveCombo(int index)
        {
            ActiveComboIndex = index;

            RestartActiveCombo();

            if (ActiveComboIndex < 0) //minus 1 means ignore playing combos
            {
                Debugging($"Combo Manager Failed. No combo set for activation.-1");
                selectedComboEditor = -1;
                //MWC — ReleasePlayingCombo runs BEFORE ActiveCombo is dropped (it inspects it). Deactivating every
                //combo while a Mode was still in flight silently ate that Mode's End — the same hang: the listeners
                //of OnComboRestart never heard back. With the flag kept, OnModeEnd finds a null ActiveComboSequence
                //and its Restart() guard releases them.
                ReleasePlayingCombo();
                ActiveCombo = null;
                return;
            }

            ActiveCombo = combos[ActiveComboIndex];

            //MWC — restart the INCOMING combo too. The RestartActiveCombo() at the top only ever reached the OUTGOING
            //one, so the combo being switched TO kept the CurrentSequence, ActiveSequenceIndex and seq.Used flags of
            //its previous run. That stale sequence was then what OnModeEnd evaluated for the pending Mode End.
            RestartActiveCombo();

            Debugging($"Set Active Combo [{ActiveCombo.Name},Index: {index}]");

            //find all other combos with the same mode AND the same origin.
            //MWC — Source keeps two Combo Sets (or a Set and the inline combos) that share a Mode ID from mixing variations.
            extraStateCombos = combos.FindAll(x => x.Mode == ActiveCombo.Mode && x.Source == ActiveCombo.Source);

            selectedComboEditor = ActiveComboIndex;
        }

        /// <summary> Changes the Active Combo on the Manager using the Mode ID </summary>
        public virtual void SetActiveCombo(ModeID ComboMode)
        {
            if (ComboMode == null)
            {
                SetActiveCombo(-1);  //meaning that we are disabling all combos
            }
            else
            {
                int index = combos.FindIndex(x => x.Mode != null && x.Mode.ID == ComboMode.ID && x.HasState(animal.ActiveStateID));

                //MWC — Already on that combo? Do nothing. Re-selecting mid-combo called RestartActiveCombo()
                //(clearing CurrentSequence, ActiveSequenceIndex and every seq.Used) and left PlayingCombo false,
                //so the following Mode End was ignored and the Item Action never received its Exit.
                if (index == ActiveComboIndex && ActiveCombo != null) return;

                ReleasePlayingCombo(); //MWC — was an unconditional PlayingCombo = false; see ReleasePlayingCombo
                RestartActiveCombo();

                SetActiveCombo(index);
            }
        }

        /// <summary> Changes the Active Combo to the variation owned by a Combo Set asset. </summary>
        public virtual void SetActiveCombo(ComboSet asset)
        {
            if (asset == null) { SetActiveCombo(-1); return; } //meaning that we are disabling all combos

            LoadComboSet(asset); //Make sure this manager already holds the runtime clones of the asset

            //MWC — search by the OWNING asset, not by the Mode ID. Many combos can share the same Mode ID
            //(Melee Weapon ID and Melee Super Item ID are both ID 100) and the ID search always returned the first one.
            int index = combos.FindIndex(x => x.Source == asset && x.HasState(animal.ActiveStateID));

            if (index == -1) Debugging($"[Failed] No combo on the Combo Set [{asset.name}] can play on the Active State");

            //MWC — Already on that combo? Do nothing, so an in-progress combo is not restarted. See SetActiveCombo(ModeID)
            if (index == ActiveComboIndex && ActiveCombo != null) return;

            ReleasePlayingCombo(); //MWC — was an unconditional PlayingCombo = false; see ReleasePlayingCombo
            RestartActiveCombo();

            SetActiveCombo(index);
        }

        public virtual void SetActiveCombo(IntVar index) => SetActiveCombo(index.Value);

        public virtual void SetActiveCombo(string ComboName)
        {
            int index = combos.FindIndex(x => x.Name == ComboName);
            SetActiveCombo(index);
        }

        public virtual void Play() => TryPlay(Branch);

        public virtual bool TryPlay() => TryPlay(Branch);
        public virtual void Play(int branch) => TryPlay(Branch = branch);

        ///// <summary>  Play Combo Sequence  <param name="branch"></param>
        //public virtual void Play(int[] branch)
        //{

        //}


        public virtual bool TryPlay(int branch)
        {
            if (!gameObject.activeInHierarchy
                || !enabled      //  the component disabled
                || animal.ActiveState == null
                )
            {
                Debugging($"[Failed] Disabled or InactiveInHierarchy");
                return false; //Active combo is minus 1 ... ignore playing combos
            }

            if (ComboConditions.Valid && !ComboConditions.Evaluate(animal))
            {
                Debugging($"[Failed] Combo Conditions not met");
                return false; //Combo conditions are not met
            }

            // MWC — split the old combined check so LockInput has its own toggle and each veto reports its real reason.
            // Riders need this: MRider mounting sets the rider's MAnimal to Sleep, and the Sleep setter also raises
            // LockInput, so the unconditional LockInput clause blocked every mounted combo even with DisableOnSleep off.
            if (DisableOnSleep && animal.Sleep)
            {
                Debugging($"[Failed] Animal is Sleep");
                return false;
            }

            if (DisableOnLockInput && animal.LockInput)
            {
                Debugging($"[Failed] Animal has LockInput enabled");
                return false;
            }

            if (animal.InTimeline) //If is a timeline do nothing
            {
                Debugging($"[Failed] Animal is in a Timeline");
                return false;
            }

            if (ActiveComboIndex < 0)
            {
                Debugging($"[Failed] No Active Combo");
                return false; //Active combo is minus 1 ... ignore playing combos
            }

            if (animal.IsPreparingMode) return true; //Bug when the buttons are played at the same time

            ////Means is not Playing any mode so Restart
            //if (!animal.IsPlayingMode)
            //{
            //    Restart();
            //}

            Branch = branch;

            if (ActiveCombo != null)
            {
                if (ActiveCombo.CachedMode.Active == false)
                {
                    Debugging($"{ActiveCombo.Name} - [Mode Disabled]");
                    return false; //Do not Play a Combo if the Mode is disabled
                }

                if (animal.IsPlayingMode && animal.ActiveMode.IgnoreLowerModes && ActiveCombo.CachedMode.Priority < animal.ActiveMode.Priority && animal.ActiveMode.InCoolDown)
                {
                    Debugging($"{ActiveCombo.Name} - [Active Mode does not allow playing the combo]");
                    return false; //Do not Play a Combo if the current mode has higher priority
                }


                if (!ActiveCombo.active)
                {
                    Debugging($"{ActiveCombo.Name} - [Disabled]");
                    return false; //Do not Play a Combo if its disabled
                }

                if (ActiveCombo.InCoolDown)
                {
                    Debugging($"{ActiveCombo.Name} - [In CoolDown]");
                    return false; //Do not Play a Combo if its in cooldown
                }
                //Debug.Log("ActiveCombo.Play = " + ActiveCombo);

                return ActiveCombo.Play(this);
            }
            return false;
        }

        public virtual void SetBranch(int branch)
        {
            Branch = branch;
        }

        public virtual void Restart()
        {
            ActiveComboSequenceIndex = 0;
            PlayingCombo = false;
            RestartActiveCombo();
            Debugging("Restart");

            OnComboRestart?.Invoke(); //Notify listeners that the combo has been restarted

        }

        public virtual void Combo_Enable(int index)
        {
            if (index >= 0 && index < combos.Count)
            {
                combos[index].active = true;
            }
        }

        public virtual void Combo_Disable(int index)
        {
            if (index >= 0 && index < combos.Count)
            {
                combos[index].active = false;
            }
        }

        public virtual void Combo_Enable(string name) => Combo_Enable(name, true);
        public virtual void Combo_Disable(string name) => Combo_Enable(name, false);

        public virtual void Combo_Enable(string name, bool value)
        {
            // improved string compare performance and made it non-case-sensitive
            var combo = combos.Find(x => string.Equals(x.Name, name, System.StringComparison.OrdinalIgnoreCase));

            if (combo != null)
            {
                combo.active = value;
            }
        }


        private void RestartActiveCombo()
        {
            if (ActiveCombo != null)
            {
                ActiveCombo.CurrentSequence = null;  //Clean the current combo Sequence
                ActiveCombo.ActiveSequenceIndex = -1;  //Clean the current combo Sequence
                foreach (var seq in ActiveCombo.Sequence)
                {
                    seq.Used = false; //Set that the Sequence is used to 
                }
            }
        }


        public void ResetCoolDown(string name)
        {
            var combo = combos.Find(x => x.Name == name);
            combo?.ResetCoolDown();
        }

        /// <summary>Editor only console log for the Combo Manager, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        internal void Debugging(string value)
        {
#if UNITY_EDITOR
            if (debug) MDebug.Log($"<b><color=orange>[{animal.name}] - [Combo - {(ActiveCombo != null ? ActiveCombo.Name : "NULL")}] - {value}</color></b>", this);
#endif
        }

        [HideInInspector] public int selectedComboEditor = -1;

        internal Combo GetCombo(ModeID weaponID)
        {
            return combos.Find(x => x.Mode == weaponID);
        }


        private void Reset()
        {
            animal = this.FindComponent<MAnimal>();
        }

        private void OnValidate()
        {
            ActiveComboIndex = Mathf.Clamp(ActiveComboIndex.Value, -1, combos.Count - 1);

            foreach (var c in combos)
            {
                c.UpgradeToIDList(this);
            }
        }

    }

    [System.Serializable]
    public class Combo
    {
        public bool active = true;
        public ModeID Mode;
        public string Name = "Combo1";
        public Mode CachedMode;

        [Tooltip("After the Combo is Finished, With a finisher it cannot play again until the cooldown has passed")]
        public FloatReference CoolDown = new();

        public List<StateID> states = new(); //Old List to keep compatibility with old combos. This will be converted to the new IDList on the OnValidate method

        [Tooltip("States the Combo can be played")]
        public IDListCheck<StateID> States;


        [Tooltip("Disable the Combo after it has been completed to the last sequencer (Finisher)")]
        public BoolReference DisableOnFinish = new();
        [Tooltip("Disable the Combo if the sequence was not completed")]
        public BoolReference DisableOnInterrupt = new();

        [Tooltip("States that re-enable this Combo when the Animal activates them (e.g. an Aereal combo disabled on interrupt " +
            "becomes available again when the Animal is back on Locomotion or Idle).\nLeave the list empty to never auto-enable it")]
        public IDListCheck<StateID> EnableOn = new();


        public float FinishTime
        {
            get => finishTime;
            internal set
            {

                finishTime = value;
                // Debug.Log($"finishTime {finishTime}");
            }
        }

        public float finishTime;

        public bool InCoolDown => CoolDown > 0 && (Time.time - FinishTime) <= CoolDown;

        public List<ComboSequence> Sequence = new();
        public ComboSequence CurrentSequence { get; internal set; }

        /// <summary> Current Index on the list to search combos. This is used to avoid searching already used Sequences on the list</summary>
        public int ActiveSequenceIndex { get; internal set; }
        //{
        //    get => atvs;
        //    internal set
        //    {
        //        atvs = value;
        //         Debug.Log($"[{Name}] - ActiveSequenceIndex [{value} ]");
        //    }
        //}
        //int atvs;

        /// <summary> Resets the Cooldown by giving a possible Finish time value to compare in the InCooldown property </summary>
        public void ResetCoolDown() => FinishTime = Time.time - CoolDown * 2;

        /// <summary>  Returns if the list state contains a given state   </summary>
        public bool HasState(StateID activeState) => States.Empty || States.Check(activeState);

        /// <summary> Returns if a State ID is on the Enable On list. An empty list returns false (never auto-enable). </summary>
        public bool CanEnableOnState(int stateID) => EnableOn != null && EnableOn.Check(stateID);

        public int ComboIndex { get; internal set; }

        /// <summary>Combo Set asset this combo was cloned from. Null for combos authored inline on the manager.</summary>
        public ComboSet Source { get; internal set; }

        public IntEvent OnComboFinished = new();
        public IntEvent OnComboInterrupted = new();

        public bool Play(ComboManager M)
        {
            //Do nothing because there's a sequence waiting to be activated
            if (M.ActiveComboSequence != null && M.ActiveComboSequence.Buffer)
            {
                // Debug.Log("BUFFERING");
                return false;
            }
            var animal = M.animal;
            if (animal.IsPreparingMode) return false;

            ActiveSequenceIndex = Mathf.Clamp(ActiveSequenceIndex, 0, Sequence.Count - 1); //Clamp the Active sequence problem

            //If the Animal is not Playing a Mode means that there's no Combo Player
            if (!animal.IsPlayingMode ||
                (animal.ActiveMode != CachedMode)) //OR the Mode currently playing is different.... try to activate the Combo in the normal way
            {
                for (int i = 0; i < Sequence.Count; i++)
                {
                    var Starter = Sequence[i];

                    // Debug.Log($"Starter.Used {!Starter.Used} + Starter.Branch == M.Branch [{Starter.Branch == M.Branch}]  Starter.PreviewsAbility == 0 {Starter.PreviewsAbility == 0}   ");

                    if (!Starter.Used && CheckBranch(M.Branch, Starter) && Starter.PreviewsAbility == 0) //Only Start with Started Abilities
                    {
                        if (CachedMode.TryActivate(Starter.Ability))
                        {
                            // Debug.Log($"Success! ({CachedMode.Name}) Playing");

                            PlaySequence(M, Starter, i); //MWC — index passed in: PlaySequence now owns ActiveSequenceIndex and PlayingCombo

                            if (CachedMode.ActiveAbility.Status == AbilityStatus.Charged)
                            {
                                CachedMode.InputValue = true; //Make sure the Input is set to true for charged modes?
                            }

                            return true;
                        }
                        else
                        {
                            M.Debugging($"Try Activate First Sequence ({CachedMode.Name}) Failed. Check Mode Conditions");
                            return false;
                        }
                    }
                }
            }
            //Check if we are playing the same mode or if the current mode has lower priority
            else //if (animal.ActiveMode == CachedMode) 
            {
                var ModeTime = animal.ModeTime;

                var sequence = Sequence[ActiveSequenceIndex];

                //If we are on a Finisher Sequence Ignore!! This will allow to finish the combo
                if (sequence.Finisher)
                {
                    //Use this to restart the combo at the end of a Finisher
                    if (sequence.Restarter && sequence.FinisherTime < (ModeTime))
                    {
                        OnComboFinished.Invoke(ActiveSequenceIndex);

                        M.Debugging($"Combo Finished -<RESTARTING>-. <b>[{ActiveSequenceIndex}]</b> Branch:<b>[{M.Branch}]</b>. [Restarting]");
                        M.Restart();

                        //RESTART CLEAN
                        for (int i = 0; i < Sequence.Count; i++)
                        {
                            var Starter = Sequence[i];

                            if (!Starter.Used && CheckBranch(M.Branch, Starter) && Starter.PreviewsAbility == 0) //Only Start with Started Abilities
                            {
                                // Debug.Log($"CachedMode {CachedMode.Name} + Starter.Ability [{Starter.Ability}]");

                                if (CachedMode.ForceActivate(Starter.Ability))
                                {
                                    // M.MDebug($"Success! ({CachedMode.Name}) Playing");

                                    PlaySequence(M, Starter, i);
                                    return true;
                                }
                                else
                                {
                                    M.Debugging($"Try Activate First Sequence ({CachedMode.Name}) Failed. Check Mode Conditions");
                                    return false;
                                }
                            }
                        }
                    }
                    return true;
                }
                else
                    for (int i = ActiveSequenceIndex + 1; i < Sequence.Count; i++) //Search from the next one
                    {
                        var seq = Sequence[i];

                        if (!seq.Used && CheckBranch(M.Branch, seq) && seq.PreviewsAbility != 0 && seq.PreviewsAbility == CachedMode.AbilityIndex)
                        {
                            //Play the nex animation on the sequence if is not buffered
                            if (seq.Activation.IsInRange(ModeTime))
                            {
                                if (ModeTime > seq.ActivationTime)
                                {
                                    animal.Mode_ForceActivate(Mode, seq.Ability);
                                    PlaySequence(M, seq, i);
                                }
                                else
                                {
                                    if (!seq.Buffer)
                                    {
                                        seq.Buffer = true;  //The Combo will definitely play after the Mode Time has passed

                                        M.Debugging($"Sequence <b>[{i}]</b> [**Buffering**] - Branch:<b>[{M.Branch}]</b>. [{animal.ModeTime:F2}]");

                                        //Stop the sequence if other was playing
                                        if (C_WaitBuffer != null) M.StopCoroutine(C_WaitBuffer);
                                        C_WaitBuffer = WaitForBuffer(M, seq, i);

                                        //Wait for the correct animation time
                                        M.StartCoroutine(C_WaitBuffer);
                                    }
                                }
                            }
                            else
                            {
                                //M.MDebug($"Sequence [{ActiveSequenceIndex}]: <b>[{M.ActiveComboSequenceIndex}]</b> - Branch:<b>[{M.Branch}]</b>. " +
                                //    $"NOT IN TIME RANGE YET [{animal.ModeTime}]");
                            }
                            return true;
                        }
                        //else
                        //{
                        //    Debug.Log("IGNORE BRANCH!!!");
                        //}
                    }
            }

            return false;
        }

        private static bool CheckBranch(int Branch, ComboSequence Starter)
        {
            return (Starter.Branch == Branch || Starter.Branch == -1);
        }

        /// <summary>Stores the sequence as the current one and reports it to the manager. <paramref name="index"/> is its position in <see cref="Sequence"/>.</summary>
        private void PlaySequence(ComboManager M, ComboSequence sequence, int index)
        {
            CurrentSequence = sequence; //Store the current sequence
            CurrentSequence.Used = true;

            //MWC — Set BEFORE the debug log. Every caller used to assign it right after PlaySequence,
            //so the log always printed the previous sequence's index.
            ActiveSequenceIndex = index; //Finding which is the active Sequence index

            //MWC — Single place every sequence activation goes through (starter, continuation, restarter and buffered),
            //so the manager can never end up playing a sequence with PlayingCombo false. When that happened OnModeEnd
            //skipped the Finished/Incomplete logic and the Item Action was never told to Exit.
            M.PlayingCombo = true;

            M.ActiveComboSequenceIndex = Mode.ID * 1000 + sequence.Ability;
            CurrentSequence.OnSequencePlay.Invoke(M.ActiveComboSequenceIndex);

            M.Debugging($"Sequence [{ActiveSequenceIndex}]: <b><color=green>[{M.ActiveComboSequenceIndex}]</color>  </b> - Branch:<b>[{M.Branch}]</b>. Time: {M.animal.ModeTime:F2}");
        }


        private IEnumerator WaitForBuffer(ComboManager M, ComboSequence seq, int Index)
        {
            yield return new WaitUntil(() => M.animal.ModeTime > seq.ActivationTime);
            seq.Buffer = false;

            M.animal.Mode_ForceActivate(Mode, seq.Ability); //Play the mode!!

            //  M.MDebug($"Sequence Buffered!" );
            // Debug.Log($"Sequence Buffered Played!" );
            PlaySequence(M, seq, Index);

        }

        /// <summary> Moves the legacy [states] list into the States IDList, only when there is legacy data to migrate </summary>
        internal void UpgradeToIDList(UnityEngine.Object dirtyTarget) //MWC: was ComboManager; generalized so ComboSet can reuse it
        {
            States ??= new(); //MWC — a code-created Combo has no States list yet

            //MWC — [states] deserializes as an empty list (never null), so check Count too; otherwise an empty
            //States re-ran the upgrade (and dirtied the scene/prefab) on every OnValidate
            if (states != null && states.Count > 0 && States.Empty)
            {
                States = new IDListCheck<StateID>(states);
                states = null;
                if (!Application.isPlaying) MTools.SetDirty(dirtyTarget);
            }
        }

        private IEnumerator C_WaitBuffer;

    }


    [System.Serializable]
    public class ComboSequence
    {
        [MinMaxRange(0, 1)]
        [Tooltip("Buffer Input Activation time for activating the next Sequence")]
        public RangedFloat Activation = new(0.3f, 0.6f);

        [Range(0, 1)]
        [Tooltip("Normalized time on the animation to activate the next ability if the animation reached this normalize time and the Sequence has been buffered.")]
        public float ActivationTime = 0.5f;

        [Tooltip("Ability needed to activate the next Sequence")]
        public int PreviewsAbility = 0;

        [Tooltip("Name of the Ability that will be used to activate the next Sequence")]
        public string previewAbilityName;

        /// <summary>Next Ability on the Sequence</summary>
        public int Ability = 0;

        [Tooltip("Name of the Next Ability to activate")]
        public string nextAbilityName;


        [Tooltip("Branch used on the combo sequence")]
        [Min(-1)] public int Branch = 0;

        [Tooltip("Is this Sequence a Finisher Combo?")]
        public bool Finisher;
        [Tooltip("Is the sequence a Restarter if is a finisher?")]
        public bool Restarter;
        [Tooltip("Restarter Finisher needs to pass this time to finish")]
        public float FinisherTime = 0.5f;
        public IntEvent OnSequencePlay = new();


        /// <summary> The Sequence has been used already </summary>
        public bool Used { get; set; }

        /// <summary> The Sequence is waiting for the activation time  </summary>
        public bool Buffer { get; set; }

        public void Reset()
        {
            Buffer = false;
            Used = false;
        }

    }

    #region INSPECTOR
#if UNITY_EDITOR
    [CustomEditor(typeof(ComboManager))]

    public class ComboEditor : Editor
    {
        public static GUIStyle StyleGray => MTools.Style(new Color(0.5f, 0.5f, 0.5f, 0.3f));
        public static GUIStyle StyleBlue => MTools.Style(new Color(0, 0.5f, 1f, 0.3f));
        public static GUIStyle StyleGreen => MTools.Style(new Color(0f, 1f, 0.5f, 0.3f));
        /// <summary> Cached style to use to draw the popup button. </summary>
        private GUIStyle popupStyle;

        private int branch, prev, current;

        private SerializedProperty Branch, combos, animal, selectedComboEditor, debug, DisableOnSleep, DisableOnLockInput, ComboConditions,
            ActiveComboIndex;
        private readonly Dictionary<string, ReorderableList> SequenceReordable = new();
        private ReorderableList CombosReor;

        private ComboManager M;
        private int SelectedAbilityIndex;
        // private readonly int IndexAbility;

        private string activeComboName;

        protected virtual void OnEnable() //MWC: virtual so ComboSetEditor can inherit this editor and reuse the sequence drawing
        {
            M = (ComboManager)target;

            animal = serializedObject.FindProperty("animal");
            combos = serializedObject.FindProperty("combos");
            ComboConditions = serializedObject.FindProperty("ComboConditions");

            Branch = serializedObject.FindProperty("Branch");
            DisableOnSleep = serializedObject.FindProperty("DisableOnSleep");
            DisableOnLockInput = serializedObject.FindProperty("DisableOnLockInput");

            selectedComboEditor = serializedObject.FindProperty("selectedComboEditor");
            debug = serializedObject.FindProperty("debug");
            ActiveComboIndex = serializedObject.FindProperty("ActiveComboIndex");
            DrawComboList();
        }

        private GUIContent con;

        private void DrawComboList()
        {
            CombosReor = new ReorderableList(serializedObject, combos, true, true, true, true)
            {
                drawHeaderCallback = (rect) =>
                {
                    float half = rect.width / 2;
                    var IDIndex = new Rect(rect.x, rect.y, 45, EditorGUIUtility.singleLineHeight);
                    var IDName = new Rect(rect.x + 45, rect.y, half - 15 - 45, EditorGUIUtility.singleLineHeight);
                    var IDRect = new Rect(rect.x + half + 10, rect.y, half - 10, EditorGUIUtility.singleLineHeight);

                    EditorGUI.LabelField(IDIndex, "Index");
                    EditorGUI.LabelField(IDName, " Name");
                    EditorGUI.LabelField(IDRect, "  Mode");
                },

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    var element = combos.GetArrayElementAtIndex(index);
                    var Mode = element.FindPropertyRelative("Mode");
                    var Name = element.FindPropertyRelative("Name");
                    var states = element.FindPropertyRelative("states");
                    var active = element.FindPropertyRelative("active");

                    rect.y += 2;

                    float half = rect.width / 2;

                    var IDActive = new Rect(rect.x, rect.y, 25, EditorGUIUtility.singleLineHeight);
                    var IDIndex = new Rect(rect.x + 20, rect.y, 25, EditorGUIUtility.singleLineHeight);
                    var IDName = new Rect(rect.x + 25 + 20, rect.y, half - 15 - 25 - 20, EditorGUIUtility.singleLineHeight);
                    var IDRect = new Rect(rect.x + half + 10, rect.y, half - 10, EditorGUIUtility.singleLineHeight);
                    var IDRectActive = new Rect(rect.x + half - 15 - 25 - 30, rect.y, 60, EditorGUIUtility.singleLineHeight);

                    var oldColor = GUI.color;

                    if (index == selectedComboEditor.intValue)
                        GUI.color = MTools.MBlue * 3;

                    EditorGUI.PropertyField(IDActive, active, GUIContent.none);
                    EditorGUI.LabelField(IDIndex, "(" + index.ToString() + ")");
                    EditorGUI.PropertyField(IDName, Name, GUIContent.none);

                    if (states.arraySize > 0)
                    {
                        IDRect.width -= 20;

                        var UpRect = new Rect(rect.x + rect.width - 15, rect.y, 25, EditorGUIUtility.singleLineHeight);

                        if (con == null)
                        {
                            var img_State = EditorGUIUtility.ObjectContent(CreateInstance<StateID>(), typeof(StateID)).image;

                            con = new(img_State);
                            //con = EditorGUIUtility.IconContent("d_P4_OutOfSync");
                            con.tooltip = "Combos with states limitation must be higher in the list";
                        }

                        EditorGUI.LabelField(UpRect, con);
                    }

                    if (index == M.ActiveComboIndex)
                    {
                        EditorGUI.LabelField(IDRectActive, "(Active)");
                        activeComboName = Name.stringValue;
                    }

                    EditorGUI.PropertyField(IDRect, Mode, GUIContent.none);

                    GUI.color = oldColor;
                },


                onSelectCallback = (list) => selectedComboEditor.intValue = list.index,

                onRemoveCallback = (list) =>
                {
                    // The reference value must be null in order for the element to be removed from the SerializedProperty array.
                    combos.DeleteArrayElementAtIndex(list.index);
                    list.index -= 1;

                    if (list.index == -1 && combos.arraySize > 0) list.index = 0;   //In Case you remove the first one

                    selectedComboEditor.intValue--;

                    list.index = Mathf.Clamp(list.index, 0, list.index - 1);

                    EditorUtility.SetDirty(target);
                }
            };
        }

        /// <summary> Mode used to resolve the Ability popups/names of a sequence.
        /// Virtual so the ComboSet asset editor can resolve it from a Preview Animal instead of the Manager's animal </summary>
        protected virtual Mode GetSequenceMode(int ComboIndex)
            => M != null && M.animal != null && ComboIndex >= 0 && ComboIndex < M.combos.Count
                ? M.animal.Mode_Get(M.combos[ComboIndex].Mode) : null;

        protected void DrawSequence(int ComboIndex, SerializedProperty combo, SerializedProperty sequence, bool drawEvents = true) //MWC: was private; protected so ComboSetEditor can reuse it
        {
            ReorderableList Reo_AbilityList;
            string listKey = combo.propertyPath;

            if (popupStyle == null)
            {
                popupStyle = (GUI.skin.GetStyle("PaneOptions"));
                popupStyle.imagePosition = ImagePosition.ImageOnly;
            }

            if (SequenceReordable.ContainsKey(listKey))
            {
                Reo_AbilityList = SequenceReordable[listKey]; // fetch the reorderable list in dict
            }
            else
            {
                Reo_AbilityList = new ReorderableList(combo.serializedObject, sequence, true, true, true, true)
                {
                    drawElementCallback = (rect, index, isActive, isFocused) =>
                    {
                        rect.y += 2;

                        var Height = EditorGUIUtility.singleLineHeight;
                        var element = sequence.GetArrayElementAtIndex(index);

                        //var Activation = element.FindPropertyRelative("Activation");
                        var PreviewsAbility = element.FindPropertyRelative("PreviewsAbility");
                        var previewAbilityName = element.FindPropertyRelative("previewAbilityName");

                        var Ability = element.FindPropertyRelative("Ability");
                        var abilityName = element.FindPropertyRelative("nextAbilityName");

                        var Branch = element.FindPropertyRelative("Branch");

                        var Activation = element.FindPropertyRelative("Activation");
                        var finisher = element.FindPropertyRelative("Finisher");
                        var FinisherTime = element.FindPropertyRelative("FinisherTime");
                        var Restarter = element.FindPropertyRelative("Restarter");
                        var ActivationTime = element.FindPropertyRelative("ActivationTime");

                        var IDRect = new Rect(rect) { height = Height };
                        float wid = rect.width / 2;

                        var IRWidth = 30f;
                        var Sep = -10f;
                        var Offset = 40f;

                        float xx = IRWidth + Offset;

                        var HasRestarter = finisher.boolValue;

                        var IndexRect = new Rect(IDRect) { width = IRWidth };
                        var BranchRect = new Rect(IDRect) { x = xx, width = 45 };


                        var PrevARect = new Rect(IDRect) { x = 75 + xx + Sep + 5, width = wid - 15 - Sep - 20 - 45 };

                        var PrevARectName = new Rect(PrevARect);
                        PrevARectName.x += 30;
                        PrevARectName.width -= 30;

                        var AbilityRect = new Rect(IDRect) { x = wid + xx + Sep + 35, width = wid - Sep - 60 - 15 - (HasRestarter ? 30 : 0) };
                        var AbilityRectName = new Rect(AbilityRect);
                        AbilityRectName.x += 30;
                        AbilityRectName.width -= 30;

                        if (HasRestarter && Restarter.boolValue) AbilityRect.width -= 28;

                        var FinisherRect = new Rect(IDRect) { x = IDRect.width + 35, width = 20 };

                        var RestarterRect = new Rect(IDRect) { x = IDRect.width + 10, width = 20 };

                        var FinisherTimeRect = new Rect(IDRect) { x = IDRect.width - 23, width = 30 };


                        var ActivationRect = new Rect(rect) { height = Height, width = rect.width - 17 };
                        ActivationRect.y += Height + 2;
                        ActivationRect.width = (rect.width / 3) * 2;


                        //NEXT SEQUENCE TIME!!!
                        var RActivationTime = new Rect(rect) { height = Height, width = rect.width - 17 };
                        RActivationTime.y += Height + 2;
                        RActivationTime.width = (rect.width / 3);
                        RActivationTime.x = (rect.width / 3) * 2 + 50;


                        var style = new GUIStyle(EditorStyles.label);

                        if (Application.isPlaying && M != null && M.ActiveCombo != null && index >= 0 && index < M.ActiveCombo.Sequence.Count && !M.ActiveCombo.Sequence[index].Used) //MWC: null-guard (asset editor has no manager)
                            style.normal.textColor = Color.green; //If the Combo is not used turn the combos to Green


                        EditorGUI.LabelField(IndexRect, "(" + index.ToString() + ")", style);
                        var oldCColor = GUI.contentColor;
                        var oldColor = GUI.color;

                        //MWC — was <= 0. A NEGATIVE Previous Ability is a valid MIRRORED ability (-101 = play [101]
                        //inverted: see Mode.GetCachedAbility / the 'invert' flag in Mode.TryActivate), NOT a starter.
                        //Only 0 marks a starter. Because this ran inside the draw callback, simply LOOKING at a combo
                        //that used mirrored abilities silently wiped [Finisher] off the asset on every repaint — and
                        //the toggle to put it back was hidden by the same test further down.
                        if (PreviewsAbility.intValue == 0)
                        {
                            GUI.contentColor = Color.green;
                            finisher.boolValue = false; //FINISHER COMBOS CANNOT BE STARTERS
                        }

                        if (finisher.boolValue) GUI.contentColor = Color.cyan;

                        if (Application.isPlaying && M != null && M.ActiveComboIndex == ComboIndex) //MWC: null-guard (asset editor has no manager)
                        {
                            if (M.ActiveCombo != null)
                            {
                                var Index = M.ActiveCombo.ActiveSequenceIndex;

                                if (Index == index) //Paint Active Index
                                {
                                    GUI.contentColor =
                                    GUI.color = Color.yellow;

                                    if (M.ActiveComboSequence != null && M.ActiveComboSequence.Finisher) //Paint finisher
                                    {
                                        GUI.contentColor =
                                        GUI.color = (Color.red + Color.yellow) / 2;
                                    }
                                }
                                else if (Index > index)  //Paint Used Index
                                {
                                    GUI.contentColor =
                                    GUI.color = Color.gray;
                                }
                            }
                        }

                        EditorGUI.PropertyField(BranchRect, Branch, GUIContent.none);

                        // Calculate rect for configuration first button
                        Rect PrevButtonRect = new(PrevARect);
                        PrevButtonRect.yMin += popupStyle.margin.top;
                        PrevButtonRect.width = popupStyle.fixedWidth + popupStyle.margin.right;
                        PrevButtonRect.x -= 20;
                        PrevButtonRect.height = EditorGUIUtility.singleLineHeight;


                        // Calculate rect for configuration first button
                        Rect NextButtonRect = new(AbilityRect);
                        NextButtonRect.yMin += popupStyle.margin.top;
                        NextButtonRect.width = popupStyle.fixedWidth + popupStyle.margin.right;
                        NextButtonRect.x -= 20;
                        NextButtonRect.height = EditorGUIUtility.singleLineHeight;


                        int result = -1;
                        var popupOptions = new List<string>();
                        var AbilitiesIndex = new List<int>();

                        popupOptions.Add("[0] Combo Starter");
                        AbilitiesIndex.Add(0);


                        //if (IndexAbility == index)
                        //{
                        //    rect.height = Height * 2;
                        //}

                        {
                            var Mode = GetSequenceMode(ComboIndex); //MWC: resolved via virtual method so the ComboSet editor can use a Preview Animal

                            if (Mode != null && Mode.Abilities != null && Mode.Abilities.Count > 0)
                            {
                                foreach (var item in Mode.Abilities)
                                {
                                    popupOptions.Add("[" + item.Index.Value + "] " + item.Name);
                                    AbilitiesIndex.Add(item.Index.Value);
                                }

                                result = EditorGUI.Popup(PrevButtonRect, result, popupOptions.ToArray(), popupStyle);

                                if (result != -1)
                                {
                                    PreviewsAbility.intValue = AbilitiesIndex[result];

                                    if (PreviewsAbility.intValue == 0) //MWC — only 0 means [Combo Starter]; a negative index is a valid MIRRORED ability
                                        previewAbilityName.stringValue = "Combo Starter";
                                    else
                                        previewAbilityName.stringValue = Mode.Abilities[result - 1].Name;
                                }

                                popupOptions = new List<string>();
                                AbilitiesIndex = new();

                                foreach (var item in Mode.Abilities)
                                {
                                    popupOptions.Add("[" + item.Index.Value + "] " + item.Name);
                                    AbilitiesIndex.Add(item.Index.Value);
                                }

                                result = -1;

                                //Do NEXT ABILITY

                                result = EditorGUI.Popup(NextButtonRect, result, popupOptions.ToArray(), popupStyle);

                                if (result != -1)
                                {
                                    Ability.intValue = AbilitiesIndex[result];
                                    abilityName.stringValue = Mode.Abilities[result].Name;
                                }

                                //Check Ability Empty

                                if (abilityName.stringValue == string.Empty)
                                {
                                    if (Ability.intValue == 0) //MWC — a negative index is a valid MIRRORED ability, not a starter
                                        abilityName.stringValue = "<CANNOT BE COMBO STARTER>";

                                    var ability = Mode.GetAbility(Ability.intValue);


                                    abilityName.stringValue = ability != null ? ability.Name : "<Not Found>";
                                }

                                if (previewAbilityName.stringValue == string.Empty)
                                {
                                    if (PreviewsAbility.intValue == 0) //MWC — only 0 means [Combo Starter]; a negative index is a valid MIRRORED ability
                                        previewAbilityName.stringValue = "Combo Starter";
                                    else
                                    {
                                        var ability = Mode.GetAbility(PreviewsAbility.intValue); //MWC — was naming the PREVIOUS ability from the NEXT one
                                        previewAbilityName.stringValue = ability != null ? ability.Name : "<Not Found>";
                                    }
                                }
                            }
                        }
                        EditorGUI.PropertyField(PrevARect, PreviewsAbility, GUIContent.none);
                        EditorGUI.LabelField(PrevARectName, previewAbilityName.stringValue);

                        EditorGUI.PropertyField(AbilityRect, Ability, GUIContent.none);
                        EditorGUI.LabelField(AbilityRectName, abilityName.stringValue);

                        var old = GUI.contentColor;


                        if (PreviewsAbility.intValue != 0) //MWC — was > 0, which hid the Finisher/Restarter toggles on every MIRRORED sequence (negative Previous Ability)
                        {
                            EditorGUI.PropertyField(FinisherRect, finisher, GUIContent.none);

                            if (finisher.boolValue)
                            {
                                GUI.contentColor =
                                GUI.color = Restarter.boolValue ? Color.green : oldCColor;
                                Restarter.boolValue = GUI.Toggle(RestarterRect, Restarter.boolValue, new GUIContent("R", "This finisher can restart the combo"), EditorStyles.miniButton);

                                GUI.contentColor = GUI.color = old;
                                // EditorGUI.PropertyField(RestarterRect, Restarter, GUIContent.none);

                                if (Restarter.boolValue)
                                {
                                    EditorGUI.PropertyField(FinisherTimeRect, FinisherTime, GUIContent.none);
                                    FinisherTime.floatValue = Mathf.Clamp01(FinisherTime.floatValue);
                                }
                            }
                        }

                        if (PreviewsAbility.intValue == 0)
                        {
                            ActivationRect.width = rect.width;
                            EditorGUI.LabelField(ActivationRect, "Sequence starter. It doesn't require an Activation Time", EditorStyles.whiteLabel);
                        }
                        else
                        {
                            EditorGUIUtility.labelWidth = 120;
                            EditorGUI.PropertyField(ActivationRect, Activation, new GUIContent($"Buffer Time [Ab:{PreviewsAbility.intValue}]"));
                            EditorGUIUtility.labelWidth = 32;
                            EditorGUI.PropertyField(RActivationTime, ActivationTime, new GUIContent("Anim"));
                            EditorGUIUtility.labelWidth = 00;
                        }
                        GUI.contentColor = GUI.color = oldCColor;



                        var r = new Rect(ActivationRect);
                        r.y += Height + 2;
                        r.x -= 20;
                        r.width += 43;
                        r.height = 1f;

                        EditorGUI.DrawRect(r, new Color(0, 0, 0, 0.25f));


                        if (index == SelectedAbilityIndex)
                        {
                            branch = Branch.intValue;
                            prev = PreviewsAbility.intValue;
                            current = Ability.intValue;
                        }
                    },

                    drawHeaderCallback = rect =>
                    {
                        var Height = EditorGUIUtility.singleLineHeight;
                        var IDRect = new Rect(rect) { height = Height };

                        float wid = rect.width / 2;
                        var IRWidth = 30f;
                        var Sep = -10f;
                        var Offset = 40f;

                        float xx = IRWidth + Offset;

                        var IndexRect = new Rect(IDRect) { width = IRWidth + 5 };
                        var BranchRect = new Rect(IDRect) { x = xx, width = 45 };
                        var PrevARect = new Rect(IDRect) { x = 75 + xx + Sep + 5, width = wid - 15 - Sep - 20 - 45 };
                        var AbilityRect = new Rect(IDRect) { x = wid + xx + Sep + 25, width = wid - 15 - Sep - 90 };
                        var FinisherRect = new Rect(IDRect) { x = IDRect.width - 10, width = 50 };

                        EditorGUI.LabelField(IndexRect, "Index");
                        EditorGUI.LabelField(BranchRect, " Branch");
                        EditorGUI.LabelField(PrevARect,
                            new GUIContent("Required Ability", "Mode Ability [Index] on the Animal required to activate a sequence"));
                        EditorGUI.LabelField(AbilityRect,
                            new GUIContent("Play Ability", "Next Mode Ability [Index] to Play on the Animal if the Active Mode Animation is withing the Activation Range limit "));
                        EditorGUI.LabelField(FinisherRect, new GUIContent("Finisher", "Combo Finisher"));
                    },

                    elementHeightCallback = (int index) =>
                    {
                        return EditorGUIUtility.singleLineHeight * 2 + 6;
                    }

                };

                SequenceReordable.Add(listKey, Reo_AbilityList);  //Store it on the Editor
            }

            Reo_AbilityList.DoLayoutList();

            SelectedAbilityIndex = Reo_AbilityList.index;

            Reo_AbilityList.elementHeightCallback(SelectedAbilityIndex);

            if (SelectedAbilityIndex != -1)
            {
                var element = sequence.GetArrayElementAtIndex(SelectedAbilityIndex);

                var Activation = element.FindPropertyRelative("Activation");
                var OnSequencePlay = element.FindPropertyRelative("OnSequencePlay");

                var lbl = "Branch:[" + branch + "] Required Ab:[" + prev + "] Next Ab:[" + current + "]";

                if (drawEvents)
                {

                    //EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    //{

                    //    EditorGUILayout.LabelField("Sequence Properties - " + lbl);
                    //    EditorGUILayout.PropertyField(Activation, new GUIContent("Activation", "Range of the Preview Animation the Sequence can be activate"));
                    //}
                    // EditorGUILayout.EndVertical();
                    EditorGUILayout.PropertyField(OnSequencePlay, new GUIContent("Sequence Play - " + lbl));
                }
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription
                ("Use Modes to make combos. Activate using ComboManager.Play(int Branch)\nBranches are the different Inputs Values");

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(animal);


                if (animal.objectReferenceValue != null)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        using (var cc = new EditorGUI.ChangeCheckScope())
                        {
                            int oldActiveCIndex = M.ActiveComboIndex.Value;

                            EditorGUILayout.PropertyField(ActiveComboIndex, new GUIContent($"Active Combo [{activeComboName}]", "Active Combo Index"));
                            if (cc.changed && Application.isPlaying)
                            {
                                ActiveComboIndex.serializedObject.ApplyModifiedProperties();

                                M.SetActiveCombo(M.ActiveComboIndex.Value);
                            }
                        }
                        MalbersEditor.DrawDebugIcon(debug);
                        //  debug.boolValue = GUILayout.Toggle(debug.boolValue,new GUIContent("D","Debug"), EditorStyles.miniButton, GUILayout.Width(23));
                    }

                    EditorGUILayout.PropertyField(Branch, new GUIContent("Branch",
                        "Current Branch ID for the Combo Sequence, if this value change then the combo will play different sequences"));

                    EditorGUILayout.PropertyField(DisableOnSleep);
                    EditorGUILayout.PropertyField(DisableOnLockInput);

                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(ComboConditions);
                    EditorGUI.indentLevel--;

                    CombosReor.DoLayoutList();

                    CombosReor.index = selectedComboEditor.intValue;
                    int IndexCombo = CombosReor.index;

                    if (IndexCombo != -1)
                    {

                        var combo = combos.GetArrayElementAtIndex(IndexCombo);

                        if (combo != null)
                        {
                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                var name = combo.FindPropertyRelative("Name");

                                EditorGUI.indentLevel++;
                                EditorGUILayout.PropertyField(combo, new GUIContent($"[{name.stringValue}] Combo"), false);
                                EditorGUI.indentLevel--;


                                if (combo.isExpanded)
                                {
                                    var Mode = combo.FindPropertyRelative("Mode");
                                    var OnComboFinished = combo.FindPropertyRelative("OnComboFinished");
                                    var OnComboInterrupted = combo.FindPropertyRelative("OnComboInterrupted");
                                    var DisableOnInterrupt = combo.FindPropertyRelative("DisableOnInterrupt");
                                    var DisableOnFinish = combo.FindPropertyRelative("DisableOnFinish");
                                    var CoolDown = combo.FindPropertyRelative("CoolDown");
                                    var states = combo.FindPropertyRelative("States");
                                    var EnableOn = combo.FindPropertyRelative("EnableOn");

                                    using (new GUILayout.VerticalScope(StyleGreen))
                                    {
                                        EditorGUILayout.LabelField("Green Sequences are combo starters", EditorStyles.boldLabel);
                                    }
                                    EditorGUI.indentLevel++;
                                    EditorGUILayout.PropertyField(states);
                                    EditorGUI.indentLevel--;

                                    EditorGUILayout.PropertyField(CoolDown);
                                    EditorGUILayout.PropertyField(DisableOnFinish);
                                    EditorGUILayout.PropertyField(DisableOnInterrupt);

                                    EditorGUI.indentLevel++;
                                    EditorGUILayout.PropertyField(EnableOn);
                                    EditorGUI.indentLevel--;



                                    EditorGUILayout.LabelField("Combo Sequence List", EditorStyles.boldLabel);
                                    var sequence = combo.FindPropertyRelative("Sequence");

                                    if (Mode.objectReferenceValue)
                                        DrawSequence(IndexCombo, combo, sequence);

                                    EditorGUILayout.PropertyField(OnComboFinished);
                                    EditorGUILayout.PropertyField(OnComboInterrupted);
                                }
                            }

                            if (debug.boolValue && Application.isPlaying)
                            {
                                using (new EditorGUI.DisabledGroupScope(true))
                                {
                                    EditorGUILayout.Toggle("Playing Combo", M.PlayingCombo);
                                    EditorGUILayout.IntField("ActiveCombo", M.ActiveComboIndex.Value);
                                    EditorGUILayout.IntField("ActiveComboSequence", M.ActiveComboSequenceIndex);
                                }
                            }
                        }
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
    #endregion
}