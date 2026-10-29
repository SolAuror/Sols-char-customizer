// Last Change: 9/30/2026 3:20pm
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Serialization;
using UnityEngine.Timeline;

namespace MalbersAnimations.Controller
{
    /// <summary> Timeline clip that aligns an Animal to the ground and can warp it to an End Location </summary>
    public class ACGroundAlignerClip : PlayableAsset, ITimelineClipAsset
    {
        /// <summary> Blending allows smooth weight fade when clips overlap or at clip edges </summary>
        public ClipCaps clipCaps => ClipCaps.Blending;

        [Tooltip("Extra height added to the Animal Height when placing it on the ground")]
        public float Offset = 0;
        [Tooltip("Length of the ground rays (multiplied by the Animal Scale)")]
        public float Distance = 2f;
        [Tooltip("Use the Hip pivot as the back ray. When false only the Chest pivot is used")]
        public bool HasHipPivot = true;

        [FormerlySerializedAs("animalEndLocation")]
        [Tooltip("(Optional) The Animal is pulled towards this Transform during the clip, arriving exactly on it on the last frame")]
        public ExposedReference<Transform> EndLocation;
        [Tooltip("How much of the End Location is applied over the clip (0 = animation only, 1 = on the End Location). X is the normalized clip time")]
        public AnimationCurve EndLocationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [Tooltip("Turn the Animal to face the End Location forward direction as well")]
        public bool MatchRotation = true;

        /// <summary> Creates the behaviour and copies the clip settings into it </summary>
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<ACGroundAlignerBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();

            behaviour.distance = Distance;
            behaviour.HasHipPivot = HasHipPivot;
            behaviour.Offset = Offset;
            behaviour.EndLocation = EndLocation.Resolve(graph.GetResolver());
            behaviour.EndLocationCurve = EndLocationCurve;
            behaviour.MatchRotation = MatchRotation;

            return playable;
        }
    }
}
