// Last Change: 9/30/2026 3:20pm
using UnityEngine;
using UnityEngine.Playables;

namespace MalbersAnimations.Controller
{
    /// <summary> Mixer of the AC Ground Aligner Track. Runs the ground alignment of every active clip on the bound Animal </summary>
    public class ACGroundAlignerMixer : PlayableBehaviour
    {
        bool m_Initialized;
        bool m_InitialIsKinematic;

        MAnimal m_TrackBinding;

        /// <summary> Edit Mode only: root motion fixer added by the Track while previewing, destroyed with the graph </summary>
        public AnimatorMoveTimelineFixer PreviewFixer;

        /// <summary> Stores the Rigidbody state the first frame the track processes the Animal </summary>
        void InitializeIfNecessary(MAnimal animal)
        {
            if (m_Initialized) return;
            m_Initialized = true;

            //Physics only matters in Play Mode. In Edit Mode touching the Rigidbody would dirty the scene while scrubbing
            if (Application.isPlaying && animal.RB)
            {
                m_InitialIsKinematic = animal.RB.isKinematic;
                animal.RB.isKinematic = false;
            }
        }

        /// <summary> Restores the Rigidbody state when the track stops. The transform is NOT restored: the Animal stays where the Timeline left it </summary>
        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (!m_Initialized) return;
            m_Initialized = false;

            if (Application.isPlaying && m_TrackBinding != null && m_TrackBinding.RB)
                m_TrackBinding.RB.isKinematic = m_InitialIsKinematic;
        }

        /// <summary> Removes the Edit Mode root motion fixer when the graph is destroyed </summary>
        public override void OnPlayableDestroy(Playable playable)
        {
            if (PreviewFixer != null) Object.DestroyImmediate(PreviewFixer);
            PreviewFixer = null;
        }

        /// <summary> Aligns the bound Animal with every clip that has weight this frame </summary>
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var trackBinding = playerData as MAnimal;
            if (trackBinding == null) return;

            m_TrackBinding = trackBinding;
            InitializeIfNecessary(trackBinding);

            int inputCount = playable.GetInputCount();

            for (int i = 0; i < inputCount; i++)
            {
                float inputWeight = playable.GetInputWeight(i);
                if (inputWeight <= 0f) continue;

                ScriptPlayable<ACGroundAlignerBehaviour> inputPlayable = (ScriptPlayable<ACGroundAlignerBehaviour>)playable.GetInput(i);
                ACGroundAlignerBehaviour input = inputPlayable.GetBehaviour();

                //Timeline sets the clip local time and duration on the input before evaluating it
                double duration = inputPlayable.GetDuration();
                float normalizedTime = duration > 0 ? Mathf.Clamp01((float)(inputPlayable.GetTime() / duration)) : 1f;

                input.ApplyEndLocation(trackBinding, normalizedTime);
                input.GroundRayCast(trackBinding);
            }
        }
    }
}
