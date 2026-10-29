// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using System.Linq;
using UnityEngine;

namespace MalbersAnimations
{
    [System.Serializable] //Needs to be Serializable!!!!
    [AddTypeMenu("Malbers/Scriptables/Set Int Var Listener")]
    [HelpURL(ReactionDocs.Scriptables + "set-int-var-listener")]
    public class SetIntVarReaction : Reaction
    {
        public override string DynamicName => $"Int Var Listener [{(ID.Value == -1 ? "Any" : ID.Value)}] = [{newValue?.Value}]"; //Name of the Reaction

        public override System.Type ReactionType => typeof(IntVarListener); //set the Type of component this Reaction Needs

        [Tooltip("ID for the Var Listener. If is set to -1 it will get the first Bool Listener found")]
        public IntReference ID = new(-1);
        public IntReference newValue = new();


        protected override bool _TryReact(Component reactor)
        {
            var listenersP = reactor.GetComponentsInParent<IntVarListener>().ToList();
            var listenersC = reactor.GetComponentsInChildren<IntVarListener>().ToList();

            var mergeList = listenersP.Union(listenersC).ToList(); //Merge the two lists

            if (ID != -1)
            {
                mergeList = mergeList.FindAll(x => x.ID.Value == ID.Value); //Find all in Parent
            }

            if (mergeList != null)
            {
                foreach (var item in mergeList)
                {
                    item.Value = (newValue.Value);
                }
                return true; //Reaction successful!!
            }

            return false;
        }
    }
}