// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Controller;
using UnityEngine;


namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Malbers/Animal/State")]
    [HelpURL(ReactionDocs.Animal + "state")]
    public class StateReaction : MReaction
    {
        override public string DynamicName
        {
            get
            {
                var state = ID != null ? ID.name : "<Null>";
                var enter = EnterStatus != 0 ? $" Enter [{EnterStatus}]" : string.Empty; //statuses are only shown when they are not the default 0
                var exit = ExitStatus != 0 ? $" Exit [{ExitStatus}]" : string.Empty;

                return type switch
                {
                    State_Reaction.Activate => $"State Activate [{state}]{enter}",
                    State_Reaction.ForceActivate => $"State Force Activate [{state}]{enter}",
                    State_Reaction.Enable => $"State Enable [{state}]",
                    State_Reaction.Disable => $"State Disable [{state}]",
                    State_Reaction.AllowExit => $"State Allow Exit [{(ID != null ? state : "Current")}]{exit}",
                    State_Reaction.SetExitStatus => $"State Exit Status [{ExitStatus}]",
                    State_Reaction.ExitToState => $"State Exit to [{state}]{exit}",
                    State_Reaction.Replace => $"State Replace [{(replace != null ? replace.name : "<Null>")}]",
                    State_Reaction.SetPersistent => $"State Persistent [{state}] [{PersistentValue}]",
                    _ => $"State {type} [{state}]",
                };
            }
        }

        public State_Reaction type = State_Reaction.Activate;

        [Tooltip("State you want to activate or Exit")]
        [Hide(nameof(type), true, (int)State_Reaction.Replace)]
        public StateID ID;

        [Hide(nameof(type), (int)State_Reaction.Activate, (int)State_Reaction.ForceActivate)]
        [Tooltip("This will change the value of the Enter Status parameter on the Animator. Useful to use different animations when Activating a State")]
        public int EnterStatus;

        [Tooltip("This will change the value of the Exit Status parameter on the Animator. Useful to use different animations when Exiting a State")]
        [Hide(nameof(type), (int)State_Reaction.SetExitStatus, (int)State_Reaction.AllowExit, (int)State_Reaction.ExitToState)]
        public int ExitStatus;

        [Hide(nameof(type), (int)State_Reaction.Replace)]
        public State replace;

        [Hide(nameof(type), (int)State_Reaction.SetPersistent)]
        public bool PersistentValue;


        protected override bool _TryReact(Component component)
        {
            var animal = component as MAnimal;

            if (animal.gameObject.activeInHierarchy == false) return false; //Cannot react on inactive animals

            switch (type)
            {
                case State_Reaction.Activate:
                    State NewState = animal.State_Get(ID);
                    if (NewState && NewState.CanBeActivated)
                    {
                        NewState.Activate(EnterStatus);
                        return true;
                    }
                    return false;
                case State_Reaction.AllowExit:
                    if (ID == null || animal.ActiveStateID == ID)
                    {
                        return animal.ActiveState.AllowExit();
                    }
                    return false;
                case State_Reaction.ForceActivate:
                    animal.State_Force(ID, EnterStatus);
                    return true;
                case State_Reaction.Enable:
                    animal.State_Enable(ID);
                    return true;
                case State_Reaction.Disable:
                    animal.State_Disable(ID);
                    return true;
                case State_Reaction.SetExitStatus:
                    animal.State_SetExitStatus(ExitStatus);
                    return true;
                case State_Reaction.ExitToState:
                    animal.ActiveState.AllowExit(ID.ID, ExitStatus);
                    return true;
                case State_Reaction.Replace:
                    animal.State_Replace(replace);
                    return true;
                case State_Reaction.SetPersistent:
                    animal.State_SetPersistent(ID, PersistentValue);
                    return true;
                default:
                    return false;
            }
        }

        public enum State_Reaction
        {
            /// <summary>Tries to Activate the State of the Zone</summary>
            Activate,
            /// <summary>If the Animal is already on the state of the zone it will allow to exit and activate states below the Active one</summary>
            AllowExit,
            /// <summary>Force the State of the Zone to be enable even if it cannot be activate at the moment</summary>
            ForceActivate,
            /// <summary>Enable a  Disabled State </summary>
            Enable,
            /// <summary>Disable State </summary>
            Disable,
            /// <summary>Change the Status ID of the State in case the State uses its</summary>
            SetExitStatus,
            /// <summary>AllowExitTo</summary>
            ExitToState,

            Replace,

            SetPersistent
        }


    }
}
