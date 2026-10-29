// Last Change: 9/24/2026 12:57pm
using UnityEngine;

namespace MalbersAnimations.Controller
{
    [AddComponentMenu("Malbers/Animal Controller/Simple Respawner")]
    public class SimpleRespawner : MonoBehaviour
    {
        public MAnimal animal;
        public StateID DeathID;
        public StateID RespawnState;

        public float RespawnTime = 4;

        private void OnEnable()
        {
            if (animal == null) return;

            if (animal.gameObject.IsPrefab())
                animal = Instantiate(animal);


            animal.TeleportRot(transform);

            animal.OnStateChange.AddListener(OnCharacterDead);
        }

        /// <summary>Stops listening for the character's death state.</summary>
        private void OnDisable()
        {
            if (animal != null) animal.OnStateChange.RemoveListener(OnCharacterDead);
        }

        public virtual void SetAnimal(MAnimal animal) => this.animal = animal;
        public virtual void SetAnimal(GameObject animal) => this.animal = animal.FindComponent<MAnimal>();
        public virtual void SetAnimal(Behaviour animal) => this.animal = animal.FindComponent<MAnimal>();

        public virtual void OnCharacterDead(int state)
        {
            if (DeathID.ID == state)
            {
                //Respwan the Character
                this.Delay_Action(RespawnTime, () =>
                {
                    animal.transform.SetPositionAndRotation(transform.position, transform.rotation);
                    animal.InputSource?.Enable(true);
                    animal.enabled = true;
                    animal.OverrideStartState = RespawnState;
                    animal.ResetController();
                    var allCompo = animal.GetComponentsInChildren<IRestart>();
                    foreach (var item in allCompo) item.Restart();
                }
                );
            }
        }

        private void Reset()
        {
            DeathID = MTools.GetInstance<StateID>("Death");
            RespawnState = MTools.GetInstance<StateID>("Idle");
        }
    }
}