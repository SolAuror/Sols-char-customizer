// Last Change: 9/30/2026 2:16pm
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace MalbersAnimations.Controller
{
    /// <summary>Respawns the Player on this Transform when it dies. The Player is recognized by its Player Tag</summary>  
    [AddComponentMenu("Malbers/Animal Controller/Respawner")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/secondary-components/respawner")]
    public class MRespawner : MonoBehaviour
    {
        #region Respawn
        [Tooltip("Player to Respawn. It can be a Prefab (a new one is instantiated on each respawn) or a Scene GameObject (the same one is reset).\n" +
            "Leave empty to find the Player on the scene using the Player Tag")]
        public GameObject player;

        [Tooltip("Tag used to recognize the Player on the scene when no Player is assigned")]
        public Tag PlayerTag;

        [Tooltip("State the Player will start with after Respawning")]
        public StateID RespawnState;
        [Tooltip("Time to wait after Death before Respawning")]
        public FloatReference RespawnTime = new(4f);
        [Tooltip("If True: it will destroy the MainPlayer GameObject and Respawn a new One")]
        public BoolReference DestroyAfterRespawn = new(true);
        [Tooltip("The Respawner will be kept between scenes")]
        public BoolReference m_DontDestroyOnLoad = new(true);

        [Tooltip("Restart Scene After Death")]
        public BoolReference RestartScene = new();

        /// <summary>Active Player Animal GameObject</summary>
        private GameObject InstantiatedPlayer;
        /// <summary>Old Player Animal GameObject</summary>
        private GameObject oldPlayer;

        /// <summary>Active Player Animal controlled by this Respawner</summary>  
        public MAnimal ActiveAnimal { get; private set; }

        /// <summary>Active Player GameObject controlled by this Respawner</summary>  
        public GameObject ActivePlayer => ActiveAnimal != null ? ActiveAnimal.gameObject : null;

        /// <summary>Last CheckPoint the Player entered (null if none)</summary>  
        public MCheckPoint LastCheckPoint { get; private set; }
        #endregion

        [FormerlySerializedAs("OnRestartGame")]
        public GameObjectEvent OnRespawned = new();

        /// <summary>True when the Player is placed and being listened for Death</summary>  
        private bool Respawned;

        /// <summary>Set a new Player (Prefab or Scene GameObject) to Respawn</summary>  
        public virtual void SetPlayer(GameObject go) => player = go;

        /// <summary>Registers to the scene loading and finds the Player</summary>  
        void OnEnable()
        {
            if (m_DontDestroyOnLoad.Value && HasPersistentRespawner())
            {
                enabled = false; // MWC A Respawner already survived from a previous scene, this one is not needed
                return;
            }

            transform.parent = null;
            if (m_DontDestroyOnLoad.Value) DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnLevelFinishedLoading;
            FindPlayer();
        }

        /// <summary>Unregisters from the scene loading and the Player Death</summary>  
        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnLevelFinishedLoading;
            StopListeningDeath();
        }

        /// <summary>When a new scene is loaded, find the Player on it</summary>  
        void OnLevelFinishedLoading(Scene scene, LoadSceneMode mode) => FindPlayer();

        /// <summary>True if another Respawner is already alive on the DontDestroyOnLoad scene</summary>  
        private bool HasPersistentRespawner()
        {
#if UNITY_6000_4_OR_NEWER
            var all = FindObjectsByType<MRespawner>(FindObjectsInactive.Exclude); //MWC: FindObjectsSortMode obsolete in Unity 6000.4+
#else
            var all = FindObjectsByType<MRespawner>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
            foreach (var r in all)
            {
                if (r != this && r.isActiveAndEnabled && r.gameObject.scene != gameObject.scene) return true;
            }
            return false;
        }

        /// <summary>Reloads the Active Scene</summary>  
        public void ResetScene()
        {
            Respawned = false;
            LastCheckPoint = null;
            var scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.name);
        }

        /// <summary>Assign a new Player. A live Scene Player is adopted where it is; otherwise it is found and placed on the Respawn Point</summary>
        public void ResetRespawner(GameObject newPlayer)
        {
            Respawned = false;
            StopListeningDeath();
            SetPlayer(newPlayer);

            //A live Scene Player (e.g. Swap Character) is already placed and running: only listen for its Death.
            //Teleport + Anim.Rebind() here reset the Animator parameters and left it on Idle while the Animal was on Locomotion
            if (player != null && player.scene.IsValid() && player.activeInHierarchy && player.TryGetComponent(out MAnimal animal))
            {
                ActiveAnimal = animal;
                PrepareDeathState();
                ListenDeath();
                Respawned = true;
                return;
            }

            FindPlayer();
        }

        /// <summary>Finds the Player on the Active Scene using the assigned Player or the Player Tag</summary>  
        public virtual void FindPlayer()
        {
            if (Respawned) return; //meaning the animal was already respawned.

            if (player != null && player.scene.IsValid() && !player.activeInHierarchy)
            {
                Debug.LogWarning($"[Respawner] The assigned Player [{player.name}] is inactive. Searching for the Player with the Tag [{(PlayerTag ? PlayerTag.name : "NULL")}] instead.", this);
                player = null;
            }

            if (player == null) player = FindPlayerByTag();

            if (player == null)
            {
                Debug.LogWarning($"[Respawner] There's no Player assigned and no GameObject with the Tag [{(PlayerTag ? PlayerTag.name : "NULL")}] was found in the scene. " +
                    $"Add a <Tags> component with the Player Tag to your Player.", this);
                return;
            }

            if (player.IsPrefab())
            {
                InstantiateNewPlayer();
            }
            else if (player.TryGetComponent(out MAnimal animal))
            {
                ActiveAnimal = animal;
                WarnRagdollDeath();
                SceneAnimal();
            }
            else
            {
                Debug.LogWarning($"[Respawner] The Player [{player.name}] has no Animal Controller component", this);
            }
        }

        /// <summary>Finds the first GameObject tagged with the Player Tag that has an Animal Controller</summary>  
        private GameObject FindPlayerByTag()
        {
            if (PlayerTag == null || !PlayerTag.ValidObjects) return null;

            foreach (var go in PlayerTag.gameObjects)
            {
                if (go == null) continue;
                var animal = go.GetComponentInParent<MAnimal>(); //The Tags component may be on a child of the Animal
                if (animal != null) return animal.gameObject;
            }
            return null;
        }

        /// <summary>True if the GameObject belongs to the Player (it is the Active Animal or it is tagged with the Player Tag)</summary>  
        public virtual bool IsPlayer(GameObject go)
        {
            if (go == null) return false;
            if (ActiveAnimal != null && go.transform.SameHierarchy(ActiveAnimal.transform)) return true;
            return go.HasMalbersTagInChildren(PlayerTag);
        }

        /// <summary>True if the Animal is the Player</summary>  
        public virtual bool IsPlayer(MAnimal animal) => animal != null && IsPlayer(animal.gameObject);

        /// <summary>Warns when a Scene Player uses a Ragdoll Death, since that State replaces the character with the Ragdoll and destroys it</summary>
        private void WarnRagdollDeath()
        {
            if (RestartScene.Value) return; //Reloading the scene brings the character back

            var ragdoll = ActiveAnimal.State_Get<RagdollDeath>();

            if (ragdoll != null && ragdoll.DestroyAnimal)
            {
                Debug.LogWarning($"[Respawner] The Player [{ActiveAnimal.name}] uses a Ragdoll Death State with [Destroy Animal] enabled, so it cannot be reused after dying. " +
                    "Disable [Destroy Animal] on the Ragdoll Death State, assign a Prefab as the Player, or enable [Restart Scene] to respawn it.", this);
            }
        }

        /// <summary>Places a Scene Animal on the Respawn Point and restarts it</summary>
        private void SceneAnimal()
        {
            PrepareDeathState();
            ListenDeath();
            ActiveAnimal.Teleport_Internal(transform.position);             //Move the Animal to is Start Position
            ActiveAnimal.transform.rotation = transform.rotation;           //Move the Animal to is Start Position
            ActiveAnimal.OverrideStartState = RespawnState;
            if (!ActiveAnimal.InputSource.IsUnityRefNull()) //CustomPatch: corrected null check for possible Unity object interface type
                ActiveAnimal.InputSource.Enable(true);         //Enable the Input for the Player

            ActiveAnimal.MainCollider_Enable(true);
            ActiveAnimal.Anim.Rebind();

            var allCompo = ActiveAnimal.GetComponentsInChildren<IRestart>();
            foreach (var item in allCompo) item.Restart();

            Respawned = true;
            OnRespawned.Invoke(ActiveAnimal.gameObject); // MWC Scene Animals also notify they were respawned
        }

        /// <summary>Make sure the Death State does not disable the Animal since it will be reused</summary>  
        private void PrepareDeathState()
        {
            var DeathState = ActiveAnimal.State_Get<Death>();

            if (DeathState)
            {
                DeathState.disableAnimal = false;
                DeathState.DisableAllComponents = false;
                DeathState.DisableInternalColliders = false;
                DeathState.DisableMainCollider = false;
            }
        }

        /// <summary>Start listening to the Active Animal state changes to catch the Death</summary>  
        private void ListenDeath()
        {
            if (ActiveAnimal == null) return;
            ActiveAnimal.OnStateChange.RemoveListener(OnCharacterDead); // MWC Avoid double listeners
            ActiveAnimal.OnStateChange.AddListener(OnCharacterDead);
        }

        /// <summary>Stop listening to the Active Animal state changes</summary>  
        private void StopListeningDeath()
        {
            if (ActiveAnimal != null) ActiveAnimal.OnStateChange.RemoveListener(OnCharacterDead);
        }

        /// <summary>Listen to the Animal States</summary>
        public void OnCharacterDead(int StateID)
        {
            if (!Respawned) return;

            if (StateID == StateEnum.Death)                      //Means Death
            {
                DelayRespawn();
            }
        }

        /// <summary>Respawn the Player after the Respawn Time</summary>  
        public void DelayRespawn() => Respawn(RespawnTime);

        /// <summary>Respawn the Player immediately</summary>  
        public virtual void Respawn() => Respawn(0);

        /// <summary>Respawn the Player after a delay (in seconds)</summary>  
        public virtual void Respawn(float delay)
        {
            if (player == null || ActiveAnimal == null) return; // MWC Nothing to respawn

            Respawned = false; // MWC Ignore further Death calls while waiting for the Respawn
            oldPlayer = InstantiatedPlayer;                                 //Store the old player IMPORTANT
            var playerName = player.name;                                   //Cached: the Player may be destroyed while waiting
            StopListeningDeath();

            if (player.IsPrefab())         //If the Player is a Prefab then then instantiate it on the created scene
            {
                this.Delay_Action(delay, () =>
                {
                    DestroyDeathPlayer();
                    this.Delay_Action(() => InstantiateNewPlayer()); //Instantiate next frame
                });
            }
            else if (RestartScene.Value)
            {
                this.Delay_Action(delay, ResetScene);
            }
            else
            {
                this.Delay_Action(delay, () =>
                {
                    if (ActiveAnimal == null) //The Scene Animal was destroyed while waiting (E.g. Ragdoll Death)
                    {
                        Debug.LogWarning($"[Respawner] The Player [{playerName}] was destroyed after dying, so it cannot be reused. Disable [Destroy Animal] on the Ragdoll Death State, assign a Prefab as the Player, or enable [Restart Scene] to respawn it.", this);
                        return;
                    }

                    if (!ActiveAnimal.gameObject.activeSelf) //The Death State deactivated the character (E.g. Ragdoll Death with Destroy Animal off)
                    {
                        ActiveAnimal.OverrideStartState = RespawnState;                      //Set the Respawn State before OnEnable resets the controller
                        ActiveAnimal.Teleport_Internal(transform.position);
                        ActiveAnimal.transform.rotation = transform.rotation;
                        ActiveAnimal.gameObject.SetActive(true);                             //OnEnable -> ResetController (restores the Ragdoll meshes too)
                        SceneAnimal();
                    }
                    else
                    {
                        SceneAnimal();

                        if (!ActiveAnimal.enabled)
                            ActiveAnimal.enabled = true;
                        else
                            ActiveAnimal.ResetController();
                    }

                    //ActiveAnimal.Anim.Rebind(); //Reset the Animator (THIS BREAK THE MODE BEHAVIOURS)
                });
            }
        }

        /// <summary>Destroys (or strips) the dead Player instance</summary>  
        void DestroyDeathPlayer()
        {
            if (oldPlayer != null)
            {
                if (DestroyAfterRespawn)
                    Destroy(oldPlayer);
                else
                    DestroyAllComponents(oldPlayer);
            }
        }

        /// <summary>Instantiates a new Player from the Prefab on the Respawn Point</summary>  
        void InstantiateNewPlayer()
        {
            InstantiatedPlayer = Instantiate(player, transform.position, transform.rotation);

            if (!InstantiatedPlayer.TryGetComponent(out MAnimal animal))
            {
                Debug.LogWarning($"[Respawner] The Player Prefab [{player.name}] has no Animal Controller component", this);
                return;
            }

            ActiveAnimal = animal;
            ActiveAnimal.OverrideStartState = RespawnState;
            ListenDeath();
            Respawned = true;
            OnRespawned.Invoke(InstantiatedPlayer);
        }

        #region CheckPoints  
        /// <summary>Set a new Respawn Point (Position, Rotation and State)</summary>  
        public virtual void SetRespawnPoint(Vector3 position, Quaternion rotation, StateID state)
        {
            transform.SetPositionAndRotation(position, rotation);
            if (state != null) RespawnState = state;
        }

        /// <summary>Set a new Respawn Point using a Transform</summary>  
        public virtual void SetRespawnPoint(Transform point) => SetRespawnPoint(point.position, point.rotation, RespawnState);

        /// <summary>Called by a CheckPoint when the Player enters it. Resets the previous CheckPoint and moves the Respawn Point</summary>  
        public virtual void SetCheckPoint(MCheckPoint checkPoint, StateID state)
        {
            if (checkPoint == null || LastCheckPoint == checkPoint) return; // MWC Already on this CheckPoint

            if (LastCheckPoint != null) LastCheckPoint.ResetCheckPoint(); // MWC Re-enable the previous one

            LastCheckPoint = checkPoint;
            SetRespawnPoint(checkPoint.transform.position, checkPoint.transform.rotation, state);
        }

        /// <summary>Clears the Last CheckPoint so it can be entered again</summary>  
        public virtual void ClearCheckPoint()
        {
            if (LastCheckPoint != null) LastCheckPoint.ResetCheckPoint();
            LastCheckPoint = null;
        }
        #endregion

        /// <summary>Destroy all the components on  Animal and leaves the mesh and bones</summary>
        private void DestroyAllComponents(GameObject target) //CustomPatch: TODO: future VERY big performance improvement that will show in profiler also => it would be best to cache all these components once when the animal is created and allow through an easy API for users to be able to add/remove their components to from the cache so they are taken into account by methods like this one
        {
            if (!target) return;

            var components = target.GetComponentsInChildren<MonoBehaviour>();
            foreach (var comp in components) Destroy(comp);
            var colliders = target.GetComponentsInChildren<Collider>();
            if (colliders != null)
            {
                foreach (var col in colliders) Destroy(col);
            }
            var rb = target.GetComponentInChildren<Rigidbody>();
            if (rb != null) Destroy(rb);
            var anim = target.GetComponentInChildren<Animator>();
            if (anim != null) Destroy(anim);
        }

        #region Obsolete  
        /// <summary>Use FindPlayer() instead</summary>  
        [System.Obsolete("Use FindPlayer() instead")]
        public virtual void FindMainAnimal() => FindPlayer();
        #endregion

#if UNITY_EDITOR
        /// <summary>Set the default Player Tag when the component is added</summary>  
        private void Reset()
        {
            PlayerTag = MTools.GetInstance<Tag>("Player");
        }

        /// <summary>Keep a valid Player Tag assigned</summary>  
        private void OnValidate()
        {
            if (PlayerTag == null)
                PlayerTag = MTools.GetInstance<Tag>("Player");
        }

        /// <summary>Draw the Respawn Point</summary>  
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, 0.25f);
            Gizmos.DrawRay(transform.position, transform.forward * 0.75f);
        }
#endif
    }
}
