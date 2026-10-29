// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Controller;
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable, AddTypeMenu("Malbers/Animal/Mode")]
    [HelpURL(ReactionDocs.Animal + "mode")]
    public class ModeReaction : MReaction
    {
        public override string DynamicName
        {
            get
            {
                var mode = $"[{(ID != null ? ID.name : "<Select>")}]";

                var display = type switch
                {
                    Mode_Reaction.Play => $"Mode Play {mode}",
                    Mode_Reaction.ForceActivate => $"Mode Force Activate {mode}",
                    Mode_Reaction.Interrupt => $"Mode Interrupt {mode}",
                    Mode_Reaction.Stop => "Mode Stop Active",
                    Mode_Reaction.Enable => $"Mode Enable {mode}",
                    Mode_Reaction.Disable => $"Mode Disable {mode}",
                    Mode_Reaction.SetActiveIndex => $"Mode Set Ability Index {mode} [{Ability.Value}]",
                    Mode_Reaction.ResetActiveIndex => $"Mode Reset Ability Index {mode}",
                    Mode_Reaction.PlayActiveIndex => $"Mode Play Active Ability {mode}",
                    Mode_Reaction.CoolDown => $"Mode Cool Down {mode} [{(HasCoolDown ? $"{coolDown}s" : "Off")}]",
                    Mode_Reaction.EnableAbility => $"Mode Enable Ability {mode} [{Ability.Value}]",
                    Mode_Reaction.DisableAbility => $"Mode Disable Ability {mode} [{Ability.Value}]",
                    _ => $"Mode {type} {mode}",
                };



                if (type is Mode_Reaction.Play or Mode_Reaction.ForceActivate) //which ability and how it plays
                    display += $" Ability [{(Ability.Value == -99 ? "Random" : Ability.Value.ToString())}] ({abilityStatus})";
                return display;
            }
        }



        public Mode_Reaction type = Mode_Reaction.Play;
        public ModeID ID;
        [Hide(nameof(type), (int)Mode_Reaction.Play, (int)Mode_Reaction.ForceActivate)]
        public AbilityStatus abilityStatus = AbilityStatus.PlayOnce;

        [Tooltip("If set to -99 it will do a random ability from the Mode")]
        [Hide(nameof(type),
            (int)Mode_Reaction.Play,
            //(int)Mode_Reaction.PlayForever,
            (int)Mode_Reaction.SetActiveIndex,
            (int)Mode_Reaction.ForceActivate,
            (int)Mode_Reaction.EnableAbility,
            (int)Mode_Reaction.DisableAbility
            )]
        public IntReference Ability = new(-99);



        [Hide(nameof(type), (int)Mode_Reaction.CoolDown)]
        public bool HasCoolDown = true;

        [Hide(nameof(type), (int)Mode_Reaction.CoolDown)]
        public float coolDown = 0;



        [Hide("abilityStatus", (int)AbilityStatus.ActiveByTime)]
        public float AbilityTime = 3f;

        [Hide(nameof(type), (int)Mode_Reaction.Play, /*(int)Mode_Reaction.PlayForever,*/ (int)Mode_Reaction.ForceActivate)]
        [Tooltip("Mode Power Value for the Animator Controller")]
        public float ModePower = 0;

        protected override bool _TryReact(Component component)
        {
            var animal = component as MAnimal;

            var mode = animal.Mode_Get(ID);
            if (mode == null || ID == null)
            {
                Debug.Log("Mode Reaction Failed", component);
                return false;
            }

            switch (type)
            {
                case Mode_Reaction.Play:
                    {
                        if (animal.Mode_TryActivate(ID, Ability, abilityStatus, AbilityTime))
                        {
                            animal.Mode_SetPower(ModePower);
                            return true;
                        }
                        return false;
                    }
                case Mode_Reaction.Interrupt:
                    if (animal.ActiveMode.ID == ID)
                    {
                        animal.Mode_Interrupt();
                        return true;
                    }
                    return false;
                case Mode_Reaction.SetActiveIndex:
                    mode.SetAbilityIndex(Ability);
                    return true;
                case Mode_Reaction.ResetActiveIndex:
                    mode.ResetAbilityIndex();
                    return true;
                case Mode_Reaction.Enable:
                    animal.Mode_Enable(ID);
                    return true;
                case Mode_Reaction.Disable:
                    animal.Mode_Disable(ID);
                    return true;
                case Mode_Reaction.CoolDown:
                    mode.HasCoolDown = HasCoolDown;
                    mode.CoolDown.Value = coolDown;
                    return true;
                case Mode_Reaction.ForceActivate:
                    animal.Mode_ForceActivate(ID, Ability, abilityStatus, AbilityTime);
                    animal.Mode_SetPower(ModePower);
                    return true;
                case Mode_Reaction.Stop:
                    animal.Mode_Stop();
                    return true;
                case Mode_Reaction.EnableAbility:
                    return animal.Mode_Ability_Enable(ID, Ability, true);
                case Mode_Reaction.DisableAbility:
                    return animal.Mode_Ability_Enable(ID, Ability, false);
                case Mode_Reaction.PlayActiveIndex:
                    return animal.Mode_TryActivate(ID, mode.AbilityIndex);
                default:
                    return false;
            }
        }

        public enum Mode_Reaction
        {
            /// <summary>Tries to Activate the State of the Zone</summary>
            Play,
            /// <summary>Activate  a Mode Forever</summary>
         //   PlayForever,
            /// <summary>If the Animal will set the Mode Status to Interrupt -2</summary>
            Interrupt,
            /// <summary>The Mode will be Stopped and its Input Reset</summary>
            Stop,
            /// <summary>Force the State of the Zone to be enable even if it cannot be activate at the moment</summary>
            SetActiveIndex,
            /// <summary>Enable a  Disabled State </summary>
            Enable,
            /// <summary>Disable State </summary>
            Disable,
            /// <summary>Change the Status ID of the State in case the State uses its</summary>
            CoolDown,
            /// <summary>Change the Status ID of the State in case the State uses its</summary>
            ResetActiveIndex,
            /// <summary>Force a Mode to be activated.. ignoring if another mode is playing</summary>
            ForceActivate,
            /// <summary>Force a Mode to be activated.. ignoring if another mode is playing</summary>
            EnableAbility,
            /// <summary>Force a Mode to be activated.. ignoring if another mode is playing</summary>
            DisableAbility,
            /// <summary>Play the current active index</summary>
            PlayActiveIndex
        }
    }
}
