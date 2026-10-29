using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MalbersAnimations
{
    /// <summary> Monobehaviour used to call Coroutines in Scriptable Objects </summary> 
    [DefaultExecutionOrder(500000000)]
    [AddComponentMenu("Malbers/Scriptable Coroutines")]
    public class MScriptableCoroutine : MonoBehaviour
    {
        internal List<ScriptableCoroutine> ScriptableCoroutines;
        public static MScriptableCoroutine Main;

        public bool debug = false; //Debug the Scriptable Coroutines

        internal void Restart()
        {
            if (Main == null) //if there's a main animal already set
            {
                Main = this;
                ScriptableCoroutines = new List<ScriptableCoroutine>();
                transform.parent = null;
                DontDestroyOnLoad(transform);
            }
            //else
            //{
            //    //  Debug.Log("Destroying Duplicates: "+gameObject.name);
            //    Destroy(this);
            //}
        }

        private void Awake()
        {
            Restart();
        }

        public static void PlayCoroutine(ScriptableCoroutine SC, IEnumerator Coroutine)
        {
            Initialize(); //In case is not initialized

            if (Main == null || !Main.enabled || !Main.isActiveAndEnabled) return;

            if (!Main.ScriptableCoroutines.Contains(SC))
            {
                Main.ScriptableCoroutines.Add(SC); //Add the Fist Time
            }

            Main.StartCoroutine(Coroutine);

            if (Main.debug) MDebug.Log($"Play Coroutine: {SC.name}", SC);
        }
        public static void Stop_Coroutine(IEnumerator Coroutine)
        {
            Main.StopCoroutine(Coroutine);
        }
        public static void Initialize()
        {
            if (Main == null && Application.isPlaying)
            {
                var ScriptCoro = new GameObject
                {
                    name = "Scriptable Coroutines"
                };

                ScriptCoro.AddComponent<MScriptableCoroutine>();

                if (Main.debug) MDebug.Log("Create New SC for the First Time", ScriptCoro);
            }
        }

        protected virtual void OnDisable()
        {
            if (ScriptableCoroutines != null)
                foreach (var c in ScriptableCoroutines)
                    c.CleanCoroutine();

            StopAllCoroutines();

            // Debug.Log("StopAllCoroutines",gameObject);
        }
    }
}