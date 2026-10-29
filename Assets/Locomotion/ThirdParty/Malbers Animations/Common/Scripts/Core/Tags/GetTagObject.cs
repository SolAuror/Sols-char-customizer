using MalbersAnimations.Events;
using MalbersAnimations.Reactions;
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Utilities
{
    [AddComponentMenu("Malbers/Get Tag Object [Tags]")]
    public class GetTagObject : MonoBehaviour
    {
        public Tag m_tag;
        public bool evaluateOnEnable = true;
        public FloatReference delay = new();

        public TagFindGameObject type = TagFindGameObject.First;

        [Hide("type", 2)]
        public int Index = 0;

        [Hide("type", 3)]
        public string m_name;

        public Reaction2 RaiseReaction;
        public GameObjectEvent OnTagRaise = new();

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void OnEnable()
        {
            if (evaluateOnEnable) Evaluate();
        }

        public void Evaluate()
        {
            if (delay > 0)
                Invoke(nameof(GetSet), delay);
            else
                this.Delay_Action(() => GetSet());
        }

        public void GetSet()
        {
            var item = m_tag.GetItem(type, Index, m_name);

            RaiseReaction.React(item);
            OnTagRaise.Invoke(item);
        }
    }
}