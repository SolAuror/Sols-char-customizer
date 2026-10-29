using Sol.CharacterCustomization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sol.Locomotion
{
    /// <summary>Optional integration owned by Locomotion. No scene reference is needed in the editor.</summary>
    public sealed class CustomizerLocomotionHandoff : MonoBehaviour, ICharacterGameplayHandoff
    {
        CharacterProfile source;
        CharacterProfile destination;
        HumanLocomotionController driver;
        Renderer[] visibleRenderers;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= Attach;
            SceneManager.sceneLoaded += Attach;
        }

        static void Attach(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
            foreach (var flow in root.GetComponentsInChildren<CharacterFinalizationFlow>(true))
            {
                if (!flow.Profile || flow.GameplayController) continue;
                var prefab = Resources.Load<GameObject>("Locomotion/HumanPlayer");
                if (!prefab) continue;
                // Created inactive so the customizer's hairstyles and tattoos can be added
                // before the copy wakes; its morph controller and joint offsets bind hair then.
                var holder = new GameObject("Human Locomotion Player (preparing)");
                holder.SetActive(false);
                var player = Instantiate(prefab, holder.transform);
                bool copied = CharacterAppearanceTransfer.TryCopyParts(
                    flow.Profile, player.GetComponentInChildren<CharacterProfile>(true), out string report);
                if (!copied) Debug.LogWarning($"Locomotion player: {report}", player);
                else if (report.Length > 0) Debug.Log($"Locomotion player: {report}", player);
                player.transform.SetParent(null, false);
                Destroy(holder);
                SceneManager.MoveGameObjectToScene(player, scene);
                player.name = "Human Locomotion Player";
                var bridge = player.AddComponent<CustomizerLocomotionHandoff>();
                bridge.source = flow.Profile;
                bridge.destination = player.GetComponentInChildren<CharacterProfile>(true);
                bridge.driver = player.GetComponent<HumanLocomotionController>();
                bridge.visibleRenderers = System.Array.FindAll(player.GetComponentsInChildren<Renderer>(true), r => r.enabled);
                foreach (var renderer in bridge.visibleRenderers) renderer.enabled = false;
                flow.RegisterGameplayHandoff(bridge, player.GetComponentInChildren<Camera>(true));
            }
        }

        public void BindAnimator(Animator activeAnimator)
        {
            foreach (var renderer in visibleRenderers) if (renderer) renderer.enabled = true;
            if (!destination.ApplyRecipe(source.CaptureRecipe()))
                throw new System.InvalidOperationException("Could not apply the finalized character to the locomotion player.");
            var morph = destination.Controller;
            transform.position += activeAnimator.transform.position - morph.ActiveAnimator.transform.position;
            if (morph.RigProportionDriver && morph.RigProportionDriver.IsAvatarRebuildPending)
                morph.RigProportionDriver.RebuildAvatarNow();
            morph.ActiveAnimator.transform.rotation = activeAnimator.transform.rotation;
            driver.BindAnimator(morph.ActiveAnimator);
            var renderers = GetComponentsInChildren<Renderer>(true);
            visibleRenderers = System.Array.FindAll(renderers, r => r.enabled);
            foreach (var renderer in visibleRenderers) renderer.enabled = false;
        }

        public void SetGameplayInputEnabled(bool value)
        {
            if (!driver) return;
            driver.SetGameplayInputEnabled(value);
            driver.enabled = value;
            if (!value || visibleRenderers == null) return;
            foreach (var renderer in visibleRenderers) if (renderer) renderer.enabled = true;
            foreach (var root in new[] { source.Controller.FemaleRoot, source.Controller.MaleRoot })
                if (root) foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        }
    }
}
