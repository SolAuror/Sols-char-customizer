using MalbersAnimations.Events;
using MalbersAnimations.Reactions;
using UnityEngine;

namespace MalbersAnimations.Scriptables
{
    [AddComponentMenu("Malbers/Runtime Vars/Get Runtime GameObjects")]

    public class GetRuntimeGameObjects : MonoBehaviour
    {
        [RequiredField] public RuntimeGameObjects Collection;

        public bool evaluateOnEnable = true;

        public FloatReference delay = new();

        public RuntimeSetTypeGameObject type = RuntimeSetTypeGameObject.Random;
        [Hide("type", 2)]
        public int Index = 0;

        [Hide("type", 3)]
        public string m_name;


        public GameObjectEvent Raise = new();
        public GameObjectEvent OnItemAdded = new();
        public GameObjectEvent OnItemRemoved = new();

        public Reaction2 RaiseReaction;
        public Reaction2 OnItemAddedReaction;
        public Reaction2 OnItemRemovedReaction;

        public void SetCollection(RuntimeGameObjects col) => Collection = col;

        public virtual void OnEnable()
        {
            if (evaluateOnEnable) Evaluate();

            if (Collection != null)
            {
                Collection.OnItemAdded.AddListener(ItemAdded);
                Collection.OnItemRemoved.AddListener(ItemRemoved);
            }
        }

        public void Evaluate()
        {
            if (delay > 0)
                Invoke(nameof(GetSet), delay);
            else
                this.Delay_Action(() => GetSet());
        }



        public virtual void OnDisable()
        {
            if (Collection != null)
            {
                Collection.OnItemAdded.RemoveListener(ItemAdded);
                Collection.OnItemRemoved.RemoveListener(ItemRemoved);
            }
        }

        private void ItemAdded(GameObject item)
        {
            OnItemAdded.Invoke(item);
            OnItemAddedReaction.React(item);
        }

        private void ItemRemoved(GameObject item)
        {
            OnItemRemoved.Invoke(item);
            OnItemRemovedReaction.React(item);
        }

        private void GetSet()
        {
            if (Collection != null)
            {
                var item = Collection.GetItem(type, Index, m_name, gameObject);
                Raise.Invoke(item);
                RaiseReaction.React(item);
            }
        }
    }
}