// Last Change: 9/30/2026 2:48pm
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace MalbersAnimations.Controller
{
    /// <summary> Timeline Track that keeps an Animal aligned to the ground while its clips play </summary>
    [TrackBindingType(typeof(MAnimal))]
    [TrackClipType(typeof(ACGroundAlignerClip))]
    public class ACGroundAlignerTrack : TrackAsset
    {
        /// <summary> Creates the mixer and, in Edit Mode, the root motion fixer the Animal needs to be previewed </summary>
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var mixer = ScriptPlayable<ACGroundAlignerMixer>.Create(graph, inputCount);

#if UNITY_EDITOR
            //MAnimal uses OnAnimatorMove, which is not called in Edit Mode, so the Timeline preview would lose the root motion.
            //Add a temporary hidden fixer while previewing (it is never saved, and it is destroyed with the graph)
            if (!Application.isPlaying && go != null && go.TryGetComponent(out PlayableDirector director))
            {
                var animal = director.GetGenericBinding(this) as MAnimal;

                if (animal != null && !animal.TryGetComponent(out AnimatorMoveTimelineFixer _) && animal.TryGetComponent(out Animator anim))
                {
                    var fixer = animal.gameObject.AddComponent<AnimatorMoveTimelineFixer>();
                    fixer.hideFlags = HideFlags.HideAndDontSave;
                    fixer.anim = anim;
                    mixer.GetBehaviour().PreviewFixer = fixer;
                }
            }
#endif
            return mixer;
        }

        /// <summary> Registers the Animal transform so the Timeline preview restores it when the preview ends </summary>
        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
#if UNITY_EDITOR
            var animal = director.GetGenericBinding(this) as MAnimal;

            if (animal != null)
            {
                var go = animal.gameObject;
                driver.AddFromName<Transform>(go, "m_LocalPosition.x");
                driver.AddFromName<Transform>(go, "m_LocalPosition.y");
                driver.AddFromName<Transform>(go, "m_LocalPosition.z");
                driver.AddFromName<Transform>(go, "m_LocalRotation.x");
                driver.AddFromName<Transform>(go, "m_LocalRotation.y");
                driver.AddFromName<Transform>(go, "m_LocalRotation.z");
                driver.AddFromName<Transform>(go, "m_LocalRotation.w");
            }
#endif
            base.GatherProperties(director, driver);
        }
    }
}
