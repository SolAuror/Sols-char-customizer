// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Controller;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]
    [AddTypeMenu("Malbers/Animal/Stance")]
    [HelpURL(ReactionDocs.Animal + "stance")]
    public class StanceReaction : MReaction
    {
        public Stance_Reaction action = Stance_Reaction.Set;
        [Hide("action", true, (int)Stance_Reaction.RestoreDefaultStanceValue, (int)Stance_Reaction.ResetToDefault)]
        public StanceID ID;


        override public string DynamicName
        {
            get
            {
                var display = $"Animal Stance [{action}]";

                if (action != Stance_Reaction.RestoreDefaultStanceValue && action != Stance_Reaction.ResetToDefault)
                {
                    display += $" [{(ID != null ? ID.name : " <Null>")}]";
                }
                return display;
            }
        }

        protected override bool _TryReact(Component component)
        {
            var animal = component as MAnimal;

            switch (action)
            {
                case Stance_Reaction.Set:
                    animal.Stance_Set(ID);
                    break;
                case Stance_Reaction.SetPersistent:

                    var Stance = animal.Stance_Get(ID);

                    if (Stance != null)
                    {
                        animal.Stance_Set(ID);
                        if (animal.Stance == ID || Stance.Queued)
                        {
                            Stance.SetPersistent(true);
                        }
                        else return false;
                    }

                    break;
                case Stance_Reaction.ResetToDefault:
                    var isPersistent = animal.ActiveStance.Persistent;
                    animal.ActiveStance.Persistent = false;
                    animal.Stance_Reset();
                    animal.ActiveStance.Persistent = isPersistent;
                    break;
                case Stance_Reaction.ResetPersistent:
                    animal.Stance_Get(ID)?.SetPersistent(false);
                    animal.Stance_Get(ID)?.SetQueued(false);
                    animal.Stance_Reset();
                    break;
                case Stance_Reaction.Toggle:
                    animal.Stance_Toggle(ID);
                    break;
                case Stance_Reaction.SetDefault:
                    animal.Stance_SetDefault(ID);
                    break;
                case Stance_Reaction.RestoreDefaultStanceValue:
                    animal.Stance_RestoreDefaultValue();
                    break;
            }

            return true;
        }


    }
}
