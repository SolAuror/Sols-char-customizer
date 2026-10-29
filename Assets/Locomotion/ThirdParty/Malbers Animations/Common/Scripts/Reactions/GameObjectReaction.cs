// Last Change: 9/18/2026 10:30pm
using MalbersAnimations.Scriptables;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MalbersAnimations.Reactions
{
    [Serializable, AddTypeMenu("Unity/GameObject")]
    [HelpURL(ReactionDocs.Unity + "gameobject")]
    public class GameObjectReaction : Reaction
    {
        public override string DynamicName => $"GameObject {action} [{(Value != null && Value.Value != null ? Value.Value.name : "None")}]"; //Name of the Reaction

        public enum GameObject_Reaction { Enable, Disable, Instantiate }

        public override Type ReactionType => typeof(GameObject);

        public GameObject_Reaction action = GameObject_Reaction.Instantiate;

        public GameObjectReference Value = new();

        protected override bool _TryReact(Component component)
        {
            if (Value.Value != null)
            {
                switch (action)
                {
                    case GameObject_Reaction.Enable:
                        Value.Value.SetActive(true);
                        break;
                    case GameObject_Reaction.Disable:
                        Value.Value.SetActive(false);
                        break;
                    case GameObject_Reaction.Instantiate:
                        Value.Value = GameObject.Instantiate(Value.Value);
                        break;
                }

                return true;
            }
            return false;
        }
    }

    [Serializable, AddTypeMenu("Unity/Dont Destroy On Load")]
    [HelpURL(ReactionDocs.Unity + "dont-destroy-on-load")]
    public class DontDestroyOnLoadReaction : Reaction
    {
        public override string DynamicName => "Dont Destroy On Load";
        public override Type ReactionType => typeof(GameObject);

        protected override bool _TryReact(Component component)
        {
            if (component != null)
            {
                MonoBehaviour.DontDestroyOnLoad(component.gameObject);
                return true;
            }
            return false;
        }
    }


    [Serializable, AddTypeMenu("Unity/Unpersist Object")]
    [HelpURL(ReactionDocs.Unity + "unpersist-object")]
    /// <summary> Remove the DontDestroyOnLoad from the GameObject and move it back to the current active scene </summary>
    public class UnPersistObject : Reaction
    {
        public override string DynamicName => "Unpersist Object (back to the Active Scene)";
        public override Type ReactionType => typeof(GameObject);

        protected override bool _TryReact(Component component)
        {
            if (component != null)
            {
                // Get a reference to the currently active scene
                Scene activeScene = SceneManager.GetActiveScene();

                // Move the object from DontDestroyOnLoad back into the active scene
                SceneManager.MoveGameObjectToScene(component.gameObject, activeScene);
                return true;
            }
            return false;
        }
    }
}