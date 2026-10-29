// Last Change: 9/10/2026 9:06pm
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MalbersAnimations.UI
{
    /// <summary> Script to Update in a Canvas the changes of the stats</summary>
    [DefaultExecutionOrder(501)]
    public class MFloatingNumbers : MonoBehaviour
    {
        public struct MDamageableUI
        {
            public MDamageable damageable;
            public Transform followTransform;
            public UnityAction<float> OnValueChange;
            public UnityAction<GameObject> OnMissed;
            public readonly bool IsNull => damageable == null;
        }

        [Tooltip("Runtime Set that store all the DamageNumber you want to monitor")]
        [RequiredField] public RuntimeDamageableSet Set;

        [Tooltip("Damage Number Prefab to show the damage float value")]
        [RequiredField] public UIFollowTransform DamageNumber;

        [Tooltip("Damage Number Prefab to show the Critical damage float value")]
        [RequiredField] public UIFollowTransform CriticalNumber;

        public StringReference MissedText = new("Missed!"); //Text to show when the Damage was Missed

        [Tooltip("Reference for the Camera")]
        public TransformReference Camera;

        [Tooltip("Find a bone inside the Hierarchy of the Stat Manager")]
        public string FollowTransform = "Head";

        [Tooltip("if the damage was zero do not show the floating number")]
        public bool ignoreZero = true;

        private HashSet<MDamageableUI> TrackedStats;

        [Tooltip("Change the Scale of the UI if the hit is critical")]
        public Vector3 CriticalScale = Vector3.one;

        private Camera MainCamera;

        private void Awake()
        {
            TrackedStats = new();

            Set.Clear();


            if (Camera.Value != null)
            {
                MainCamera = Camera.Value.GetComponent<Camera>();
            }
            else
            {
                MainCamera = MTools.FindMainCamera();
                Camera.Value = MainCamera.transform;
            }
        }


        private void OnEnable()
        {
            //CustomPatch: used cached delegates to avoid repeated memory allocations
            Set.OnItemAdded.AddListener(OnAddedMDamageable);
            Set.OnItemRemoved.AddListener(OnRemovedStat);

            Set.OnMissed += OnMissed;
        }



        private void OnDisable()
        {
            //CustomPatch: cached delegates to avoid repeated memory allocations
            Set.OnItemAdded.RemoveListener(OnAddedMDamageable);
            Set.OnItemRemoved.RemoveListener(OnRemovedStat);
            Set.OnMissed -= OnMissed;
        }

        private void OnMissed(GameObject go)
        {
            UIFollowTransform FU = Instantiate(DamageNumber);
            FU.SetTransform(go.transform);
            FU.transform.SetParent(transform);

            var text = FU.GetComponentInChildren<Text>();

            if (text)
            {
                text.text = MissedText.Value;
            }
        }


        private void OnAddedMDamageable(MDamageable dam)
        {
            var item = new MDamageableUI
            { damageable = dam };

            var child = dam.transform.FindGrandChild(FollowTransform);
            item.followTransform = child != null ? child : dam.transform;

            //Track when the Stat changes value
            item.OnValueChange = (floatValue) =>
            {
                if (ignoreZero && floatValue < 0.1f) return; //do nothing if the damage is close to zero

                UIFollowTransform FU = null;

                var WasCritical = item.damageable.LastDamage.critical; //Store if the Damage was Critical

                var WasMiss = item.damageable.LastDamage.Missed; //Store if the Damage was Missed

                var FloatingDamage = WasCritical && !WasMiss ? CriticalNumber : DamageNumber;

                if (FloatingDamage != null)
                {
                    FU = Instantiate(FloatingDamage);
                    FU.SetTransform(item.followTransform);
                    FU.transform.SetParent(transform);


#if UNITY_EDITOR   //CustomPatch: removed editor related logic with redundant memory allocations and text replacement from non-dev builds
                    FU.name = FU.name.Replace("(Clone)", "");
                    FU.name += ": " + floatValue.ToString("F0");
#endif

                    var text = FU.GetComponentInChildren<Text>();

                    if (text)
                    {
                        if (WasMiss)
                        {
                            text.text = MissedText.Value;
                        }
                        else
                        {
                            text.text = floatValue.ToString("F0");

                            //Draw the color of the Damage
                            if (item.damageable.LastDamage.Element.element != null)
                                text.color = item.damageable.LastDamage.Element.element.color;
                        }
                    }
                }
            };

            //A missed attack no longer modifies the Stat, so OnReceivingDamage never fires for it. Listen to the Damageable's own Missed event instead
            item.OnMissed = (damager) =>
            {
                if (DamageNumber == null) return;

                UIFollowTransform FU = Instantiate(DamageNumber);
                FU.SetTransform(item.followTransform);
                FU.transform.SetParent(transform);

                var text = FU.GetComponentInChildren<Text>();
                if (text) text.text = MissedText.Value;
            };

            if (item.OnValueChange != null) //CustomPatch: avoided redundant listener add call if no delegate registered here
                item.damageable.events.OnReceivingDamage.AddListener(item.OnValueChange);

            item.damageable.events.OnDamageMissed.AddListener(item.OnMissed);

            TrackedStats.Add(item);
        }

        //Remove stat from the Set
        private void OnRemovedStat(MDamageable stats)
        {
            var item = TrackedStats.FirstOrDefault(x => x.damageable == stats);

            if (!item.IsNull) RemoveFromGroup(item);
        }

        private void RemoveFromGroup(MDamageableUI item)
        {
            //Debug.Log($"Removed From Group {item.slider}", item.slider );

            if (item.OnValueChange != null) //CustomPatch: avoided redundant listener remove call if no delegate registered here
                item.damageable.events.OnReceivingDamage.RemoveListener(item.OnValueChange);

            if (item.OnMissed != null)
                item.damageable.events.OnDamageMissed.RemoveListener(item.OnMissed);

            item.OnValueChange = null;
            item.OnMissed = null;

            //best performance code to remove the item from TrackedStats
            TrackedStats.RemoveWhere(x => x.damageable == item.damageable);

            Set.Item_Remove(item.damageable);
        }

        private void Reset()
        {
            Set = MTools.GetInstance<RuntimeDamageableSet>("Enemy Damageable");
        }
    }
}