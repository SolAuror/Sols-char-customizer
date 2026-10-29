using System;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    //Obsolete


    //[Serializable, AddTypeMenu("Malbers/Conditions")]
    public class Condition2Reactions : Reaction
    {
        public override Type ReactionType => typeof(Component);

        protected override bool _TryReact(Component reactor) => false;

        /*public override string DynamicName =>
            $"{action} Conditions2 [{Condition2Name.Value}] on Target [{(useLocal ? "Local" : "Dynamic")}]";

        public enum ConditionActionType { Replace, Add, RemoveLast, RemoveByIndex, RemoveByDescription }

        public StringReference Condition2Name = new();

        public ConditionActionType action = ConditionActionType.Replace;

        [Hide(nameof(action), (int)ConditionActionType.RemoveByDescription)]
        [Tooltip("Description or Name of the Condition to Remove")]
        public StringReference CondDescription = new();

        [Hide(nameof(action), (int)ConditionActionType.RemoveByIndex)]
        [Tooltip("Description or Name of the Condition to Remove")]
        public IntReference Index = new();

        public override Type ReactionType => typeof(MonoBehaviour);

        [Hide(nameof(action), (int)ConditionActionType.Replace, (int)ConditionActionType.Add)]
        public Conditions2 m_condition;

        protected override bool _TryReact(Component reactor)
        {
            var allMono = reactor.GetComponents<MonoBehaviour>();

            for (int i = 0; i < allMono.Length; i++)
            {
                var mono = allMono[i];

                if (Conditions2.FindConditions2ByName(mono, Condition2Name.Value, out Conditions2 FoundCond))
                {
                    var fieldInfo = mono.GetType().GetField(Condition2Name.Value);

                    Conditions2 CloneCond;

                    CloneCond.active = m_condition.active;

                    CloneCond.conditions = new ConditionCore[m_condition.conditions.Length];

                    for (int j = 0; j < CloneCond.conditions.Length; j++)
                    {
                        CloneCond.conditions[j] = (ConditionCore)m_condition.conditions[j].Clone(); //Make a Clone!
                    }

                    foreach (var condition in CloneCond.conditions)
                    {
                        if (!condition.LocalTarget)
                        {
                            condition.SetTarget(reactor);
                            condition.LocalTarget = true; //Set the LocalTarget to the Reactor
                        }
                    }

                    switch (action)
                    {
                        case ConditionActionType.Replace:
                            fieldInfo.SetValue(mono, CloneCond); //Set the field value to the found Conditions2 instance
                            break;
                        case ConditionActionType.Add:
                            FoundCond.Add(CloneCond);
                            fieldInfo.SetValue(mono, FoundCond);
                            break;
                        case ConditionActionType.RemoveLast:
                            FoundCond.RemoveLast();
                            fieldInfo.SetValue(mono, FoundCond);
                            break;
                        case ConditionActionType.RemoveByIndex:
                            FoundCond.Remove(Index.Value);
                            fieldInfo.SetValue(mono, FoundCond);
                            break;
                        case ConditionActionType.RemoveByDescription:
                            FoundCond.Remove(CondDescription.Value);
                            fieldInfo.SetValue(mono, FoundCond);
                            break;
                        default:
                            break;
                    }

                    return true;
                }
            }

            return false;
        }*/
    }
}
