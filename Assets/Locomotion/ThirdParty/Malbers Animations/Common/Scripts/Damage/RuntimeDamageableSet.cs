using System;
using UnityEngine;
using UnityEngine.Events;


namespace MalbersAnimations.Scriptables
{
    [Serializable] public class DamageableEvent : UnityEvent<MDamageable> { }

    [CreateAssetMenu(menuName = "Malbers Animations/Collections/Runtime Damageable Set", order = 1000, fileName = "New Runtime Damageable Set")]
    public class RuntimeDamageableSet : RuntimeCollection<MDamageable>
    {
        public DamageableEvent OnItemAdded = new();
        public DamageableEvent OnItemRemoved = new();

        public System.Action<GameObject> OnMissed;

        /// <summary>Return the Closest game object from an origin</summary>
        public MDamageable Item_GetClosest(MDamageable origin)
        {
            items.RemoveAll(x => x == null); //Remove all Assets that are Empty/ Type Mismatch error

            MDamageable closest = null;

            float minDistance = float.MaxValue;

            foreach (var item in items)
            {
                var Distance = Vector3.Distance(item.transform.position, origin.transform.position);

                if (Distance < minDistance)
                {
                    closest = item;
                    minDistance = Distance;
                }
            }
            return closest;
        }

        public void ItemAdd(Component newItem)
        {
            var s = newItem.FindComponent<MDamageable>();
            if (s) Item_Add(s);
        }

        public void Item_Add(GameObject newItem)
        {
            var s = newItem.FindComponent<MDamageable>();
            if (s) Item_Add(s);
        }

        protected override void OnAddEvent(MDamageable newItem) => OnItemAdded.Invoke(newItem);
        protected override void OnRemoveEvent(MDamageable newItem) => OnItemRemoved.Invoke(newItem);

        public void ItemRemove(Component newItem)
        {
            var s = newItem.FindComponent<MDamageable>();
            if (s) Item_Remove((MDamageable)s);
        }

        public void Item_Remove(GameObject newItem)
        {
            if (newItem)
            {
                var s = newItem.FindComponent<MDamageable>();
                if (s) Item_Remove(s);
            }
        }

        public virtual void Missed(GameObject origin) => OnMissed?.Invoke(origin);
    }



#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(RuntimeDamageableSet))]
    public class RuntimeDamageableSetEditor : RuntimeCollectionEditor<MDamageable> { }
#endif

}

