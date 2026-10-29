// Last Change: 9/24/2026 1:16pm
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MalbersAnimations.UI
{
    /// <summary> Script to Update in a Canvas the changes of the stats</summary>
    [DefaultExecutionOrder(501)]
    public class StatMonitorUI : MonoBehaviour
    {
        public class StatUI
        {
            public Slider slider;
            public IMStat stat;
            public Transform followTransform;
            public float lastValue;
            public UnityAction<float> OnStatValueChange = delegate { };
        }

        [Tooltip("Runtime Set that store all the Stat you want to monitor")]
        [RequiredField] public RuntimeStats Set;
        [Tooltip("Slider used to Represent the Stat on the UI")]
        [RequiredField] public Slider UIPrefab;

        [Tooltip("What stat inside the Stat Manager you want to monitor")]
        public StatID statID;
        [Tooltip("Reference for the Camera")]
        public TransformReference Camera;

        [Tooltip("Find a bone inside the Hierarchy of the Stat Manager")]
        public string FollowTransform = "Head";
        [Tooltip("Use the Normalize value of the Stat")]
        public bool Normalized = true;
        [Tooltip("When the Stat is Empty, Stop Monitoring it")]
        public bool RemoveOnEmpty = true;

        [Tooltip("Offset to Position the Slider UI on the screen")]
        public Vector3 Offset = Vector3.zero;
        [Tooltip("Scale of the Instantiated prefab")]
        public Vector3 Scale = Vector3.one;

        private List<StatUI> TrackedStats;

        private Camera MainCamera;

        private void Awake()
        {
            TrackedStats = new List<StatUI>();

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


        protected virtual void OnEnable()
        {
            Set.OnItemAdded.AddListener(OnAddedStat);
            Set.OnItemRemoved.AddListener(OnRemovedStat);
        }

        protected virtual void OnDisable()
        {
            Set.OnItemAdded.RemoveListener(OnAddedStat);
            Set.OnItemRemoved.RemoveListener(OnRemovedStat);
        }

        /// <summary>Creates a UI slider for the tracked Stat of a Stats component added to the Set</summary>
        protected virtual void OnAddedStat(Stats stats)
        {
            var stat = stats.Stat_Get(statID);

            if (stat == null || !stat.Active || stat.IsEmpty) return; //No such Stat on this character, or it is disabled/empty: nothing to monitor

            //  Debug.Log($"Added From Stat {stats}");


            StatUI item = new() { stat = stats.Stat_Get(statID) };

            var child = stats.transform.FindGrandChild(FollowTransform);

            item.followTransform = child != null ? child : stats.transform;
            item.slider = Instantiate(UIPrefab, transform);
            item.slider.transform.localScale = Scale;

            item.slider.name = item.slider.name.Replace("(Clone)", "_");

            item.slider.name += stats.gameObject.name;
            item.lastValue = stat.Value;

            //Track when the Stat changes value
            item.OnStatValueChange = (floatValue) =>
            {
                item.slider.value = Normalized ? item.stat.NormalizedValue : item.stat.Value;

                //Meaning that when the stat is empty, remove the stat from the set.
                if (RemoveOnEmpty && item.stat.Value <= item.stat.MinValue)
                    RemoveFromGroup(item);
            };

            item.slider.value = Normalized ? item.stat.NormalizedValue : item.stat.Value; //First value.

            item.stat.OnValueChange.AddListener(item.OnStatValueChange);
            TrackedStats.Add(item);
        }

        //Remove stat from the Set
        private void OnRemovedStat(Stats stats)
        {
            var item = TrackedStats.Find(x => x.stat.Owner == stats);

            if (item != null) RemoveFromGroup(item);
        }

        private void RemoveFromGroup(StatUI item)
        {
            //Weird error that sometimes happens
            try
            {
                item.stat.OnValueChange.RemoveListener(item.OnStatValueChange);
            }
            catch
            { }


            item.OnStatValueChange = null;


            Destroy(item.slider.gameObject);

            TrackedStats.Remove(item);
            Set.Item_Remove(item.stat.Owner);
        }

        private void LateUpdate()
        {
            TrackStatsWord();
        }

        private void TrackStatsWord()
        {
            if (MainCamera == null) return;

            foreach (var item in TrackedStats)
            {
                if (item.followTransform)
                {
                    var Pos = MainCamera.WorldToScreenPoint(item.followTransform.position + Offset);
                    item.slider.transform.position = Pos;
                    item.slider.gameObject.SetActive(DoHideOffScreen(Pos));
                }
            }
        }

        private bool DoHideOffScreen(Vector3 position)
        {
            if (position.x < 0 || position.x > Screen.width) return false;
            if (position.y < 0 || position.y > Screen.height) return false;
            if (position.z < 0) return false;

            return true;
        }
    }
}