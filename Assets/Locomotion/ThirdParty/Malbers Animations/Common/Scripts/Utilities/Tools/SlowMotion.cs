using System.Collections;
using UnityEngine;

namespace MalbersAnimations
{
    /// <summary> Going slow motion on user input</summary>
    [AddComponentMenu("Malbers/Utilities/Managers/Slow Motion")]

    public class SlowMotion : MonoBehaviour
    {
        [Space]
        [Range(0.05f, 1), SerializeField]
        float slowMoTimeScale = 0.25f;
        [Range(0.1f, 2), SerializeField]
        float slowMoSpeed = 0.2f;

        private bool PauseGame = false;
        private float CurrentTime = 1;

        [Tooltip("Enable Slow Motion")]
        public bool onEnable = false;

        IEnumerator SlowTime_C;

        private float currentFixedTimeScale;
        private void Awake()
        {
            currentFixedTimeScale = Time.fixedDeltaTime;
        }

        protected virtual void OnEnable()
        {
            if (onEnable)
                Slow_Motion();
        }

        public void Slow_Motion()
        {
            // if (SlowTime_C != null || !enabled) return; //Means that the Coroutine for slowmotion is still live



            if (Time.timeScale == 1)
            {
                SlowTime_C = SlowTime();
                StartCoroutine(SlowTime_C);
            }
            else
            {
                SlowTime_C = RestartTime();
                StartCoroutine(SlowTime_C);
            }
        }

        public void Slow_Motion(bool value)
        {
            if (value)
                Slow_MotionOn();
            else
                Slow_MotionOFF();
        }

        public void Slow_MotionOn()
        {
            SlowTime_C = SlowTime();
            StartCoroutine(SlowTime_C);
        }

        public void Slow_MotionOFF()
        {
            SlowTime_C = RestartTime();
            StartCoroutine(SlowTime_C);
        }


        public virtual void Freeze_Game()
        {
            PauseGame ^= true;

            CurrentTime = Time.timeScale != 0 ? Time.timeScale : CurrentTime;

            Time.timeScale = PauseGame ? 0 : CurrentTime;
        }

        public void PauseEditor()
        {
            //  Debug.Log("SlowMotion: Pause Editor", this);
            Debug.Break();
        }

        IEnumerator SlowTime()
        {
            // var nextTime = new WaitForFixedUpdate();

            while (Time.timeScale > slowMoTimeScale)
            {
                Time.timeScale -= Time.timeScale * slowMoSpeed;
                Time.fixedDeltaTime = currentFixedTimeScale * Time.timeScale;
                //  Debug.Log("slowtime");
                yield return null;
            }

            Time.timeScale = slowMoTimeScale;
            Time.fixedDeltaTime = currentFixedTimeScale * Time.timeScale;

            SlowTime_C = null;
        }

        IEnumerator RestartTime()
        {
            //var nextTime = new WaitForFixedUpdate();

            //  Debug.Break();

            while (Time.timeScale < 1)
            {
                Time.timeScale += Time.timeScale * slowMoSpeed;
                Time.fixedDeltaTime = currentFixedTimeScale * Time.timeScale;

                //Debug.Log($"Time.fixedDeltaTime {Time.fixedDeltaTime :F6}"); 

                yield return null;
            }

            Time.timeScale = CurrentTime = 1;
            Time.fixedDeltaTime = currentFixedTimeScale;
            SlowTime_C = null;
        }
    }
}
