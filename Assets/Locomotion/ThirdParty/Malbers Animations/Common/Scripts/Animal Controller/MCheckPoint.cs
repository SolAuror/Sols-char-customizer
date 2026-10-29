using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace MalbersAnimations.Controller
{
    /// <summary>Trigger volume that moves the Respawner's Respawn Point when the Player enters it</summary>  
    [AddComponentMenu("Malbers/Animal Controller/Check Point")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/secondary-components/respawner")]
    public class MCheckPoint : MonoBehaviour
    {
        [Tooltip("Respawner this CheckPoint updates. Leave empty to find the active Respawner on the scene automatically")]
        [SerializeField] private MRespawner respawner;

        [Tooltip("Invoked when the Player enters this CheckPoint")]
        public UnityEvent OnEnter = new();
        [Tooltip("Invoked on Start and when a newer CheckPoint is reached, so this one can be entered again")]
        [FormerlySerializedAs("OnActive")]
        public UnityEvent OnReset = new();

        /// <summary>Respawner this CheckPoint updates</summary>  
        public MRespawner Respawner { get => respawner; set => respawner = value; }

        /// <summary>Trigger Collider of this CheckPoint</summary>  
        public Collider Collider { get; private set; }

        /// <summary>Finds the Respawner and prepares the trigger Collider</summary>  
        void Start()
        {
            if (respawner == null || !respawner.isActiveAndEnabled) respawner = FindActiveRespawner();

            if (respawner == null)
            {
                Debug.LogWarning($"[CheckPoint] {name} was disabled since there's no active Respawner on the scene", this);
                enabled = false;
                return;
            }

            Collider = GetComponent<Collider>();

            if (Collider)
            {
                Collider.isTrigger = true;
            }
            else
            {
                Debug.LogError($"[CheckPoint] {name} needs a Collider", this);
                enabled = false;
                return;
            }

            OnReset.Invoke();
        }

        /// <summary>Returns the first active Respawner found on the loaded scenes</summary>  
        private static MRespawner FindActiveRespawner()
        {

#if UNITY_6000_4_OR_NEWER
            var all = FindObjectsByType<MRespawner>(FindObjectsInactive.Exclude); //MWC: FindObjectsSortMode obsolete in Unity 6000.4+
#else
            var all = FindObjectsByType<MRespawner>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
            foreach (var r in all)
                if (r.isActiveAndEnabled) return r;
            return null;
        }

        /// <summary>Stores this CheckPoint on the Respawner when the Player enters</summary>  
        void OnTriggerEnter(Collider other)
        {
            if (other.isTrigger) return; //Ignore Colliders
            if (respawner == null) return;
            if (respawner.LastCheckPoint == this) return; //Means the animal has already enter this CheckPoint  

            var animal = other.GetComponentInParent<MAnimal>();

            if (!animal) return;        //Skip if there's no Animal

            if (!respawner.IsPlayer(animal)) return; //Skip if it is not the Player Animal  

            respawner.SetCheckPoint(this, animal.ActiveStateID); //Set on the Respawner the new Respawn Point and the Last Animal State  

            OnEnter.Invoke();
            Collider.enabled = false;
        }

        /// <summary>Re-enables this CheckPoint so the Player can enter it again</summary>  
        public void ResetCheckPoint()
        {
            if (Collider) Collider.enabled = true;
            OnReset.Invoke();
        }

#if UNITY_EDITOR
        /// <summary>Draw the CheckPoint position</summary>  
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.25f);
            Gizmos.DrawRay(transform.position, transform.forward * 0.75f);
        }
#endif
    }
}
