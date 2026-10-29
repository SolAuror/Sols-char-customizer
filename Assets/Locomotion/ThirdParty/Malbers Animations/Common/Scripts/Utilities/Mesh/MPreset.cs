using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Utilities
{
    [CreateAssetMenu(menuName = "Malbers Animations/Preset/Malbers Preset", order = 200, fileName = "New Malbers Preset")]
    public class MPreset : ScriptableObject
    {
        [Header("Smooth BlendShapes")]
        public FloatReference BlendTime = new(1.5f);
        public AnimationCurve BlendCurve = new(new Keyframe[] { new(0, 0), new(1, 1) });

        public BlendShapePreset BlendShapes;
        public BonePreset BonePreset;

        public void Load(GameObject obj)
        {
            if (BonePreset != null) BonePreset.Load(obj.transform);

            if (BlendShapes != null)
            {
                var blendshapes = obj.GetComponentInChildren<BlendShape>();
                if (blendshapes) blendshapes.LoadPreset(BlendShapes);
            }

        }
        public virtual void SmoothBlend(GameObject obj)
        {
            if (BonePreset != null)
            {
                BonePreset.BlendCurve = BlendCurve;
                BonePreset.BlendTime = BlendTime;
                BonePreset.SmoothBlendBones(obj.transform);
            }
            if (BlendShapes != null)
            {
                var blendshapes = obj.GetComponentInChildren<BlendShape>();
                if (blendshapes)
                {
                    BlendShapes.BlendCurve = BlendCurve;
                    BlendShapes.BlendTime = BlendTime;
                    blendshapes.LoadSmoothPreset(BlendShapes);
                }
            }
        }


    }
}