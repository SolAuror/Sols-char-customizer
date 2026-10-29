// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Events;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    [System.Serializable]

    [AddTypeMenu("[Event]")]
    [HelpURL(ReactionDocs.General + "event")]
    public class UnityEventReaction : Reaction
    {
        public override string DynamicName => $"Unity Event [{Invoke?.GetPersistentEventCount() ?? 0} Calls]";
        public override System.Type ReactionType => typeof(Component);

        public ComponentEvent Invoke = new();

        //MWC — Cleared on every deserialize, so the check runs once per fresh instance
        [System.NonSerialized] private bool argumentsFixed;

        protected override bool _TryReact(Component component)
        {
            //MWC — An empty [None] Object argument authored in the Inspector deserializes as a FAKE-null, which makes
            //Unity's own persistent-call builder throw before any listener runs. Normalize it once, so leaving an
            //argument empty on purpose (e.g. MEvent.Invoke(Sprite) with None, to clear a UI icon) actually works.
            if (!argumentsFixed)
            {
                argumentsFixed = true;
                UnityEventTools.FixNullObjectArguments(Invoke);
            }

            //MWC — A malformed persistent call throws while Unity REBUILDS the call list, before any listener runs
            //(e.g. an Object argument that deserialized into a type the method does not take). Unhandled, it escapes
            //through Reaction2.TryReact into whatever fired the reaction (Equip / Holster sequences) and leaves it
            //half done. Contain it here and name the calls, so a bad reaction breaks only itself.
            try
            {
                Invoke.Invoke(component);
            }
            catch (System.Exception e)
            {
                Debug.LogException(new System.Exception($"[Event] Reaction failed. {PersistentCalls()}", e), component);
                return false;
            }
            return true;
        }

        /// <summary>Target.Method of every persistent call, to point at the one that is broken</summary>
        private string PersistentCalls()
        {
            var calls = new System.Text.StringBuilder("Persistent calls: ");

            for (int i = 0; i < Invoke.GetPersistentEventCount(); i++)
            {
                var target = Invoke.GetPersistentTarget(i);
                calls.Append($"[{(target != null ? target.name : "NULL")}.{Invoke.GetPersistentMethodName(i)}] ");
            }
            return calls.ToString();
        }
    }
}
