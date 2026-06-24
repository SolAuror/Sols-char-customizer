using System;
using System.Collections.Generic;

namespace Sol.CharacterCustomization
{
    public enum CharacterSex
    {
        Female,
        Male
    }

    public abstract class CharacterMorphDefinition
    {
        protected CharacterMorphDefinition(
            string id,
            string label,
            string group,
            string baseShapeName,
            string femaleBaseShapeName = null,
            string maleBaseShapeName = null)
        {
            Id = id;
            Label = label;
            Group = group;
            BaseShapeName = baseShapeName;
            FemaleBaseShapeName = femaleBaseShapeName;
            MaleBaseShapeName = maleBaseShapeName;
        }

        public string Id { get; }
        public string Label { get; }
        public string Group { get; }
        public string BaseShapeName { get; }
        public string FemaleBaseShapeName { get; }
        public string MaleBaseShapeName { get; }
        public abstract bool RequiresNegativeShape { get; }
        public bool IsBipolar => RequiresNegativeShape;
        public abstract float MinimumValue { get; }

        public virtual string GetPositiveShape(CharacterSex sex)
        {
            return GetBaseShapeName(sex) + "+";
        }

        public abstract string GetNegativeShape(CharacterSex sex);
        public abstract void CalculateWeights(float value, out float positiveWeight, out float negativeWeight);

        protected string GetBaseShapeName(CharacterSex sex)
        {
            if (sex == CharacterSex.Female && !string.IsNullOrEmpty(FemaleBaseShapeName))
            {
                return FemaleBaseShapeName;
            }

            if (sex == CharacterSex.Male && !string.IsNullOrEmpty(MaleBaseShapeName))
            {
                return MaleBaseShapeName;
            }

            return BaseShapeName;
        }
    }

    public sealed class BipolarMorphDefinition : CharacterMorphDefinition
    {
        public BipolarMorphDefinition(
            string id,
            string label,
            string group,
            string baseShapeName,
            string femaleBaseShapeName = null,
            string maleBaseShapeName = null)
            : base(id, label, group, baseShapeName, femaleBaseShapeName, maleBaseShapeName)
        {
        }

        public override bool RequiresNegativeShape => true;
        public override float MinimumValue => -1f;

        public override string GetNegativeShape(CharacterSex sex)
        {
            return GetBaseShapeName(sex) + "-";
        }

        public override void CalculateWeights(float value, out float positiveWeight, out float negativeWeight)
        {
            positiveWeight = Math.Max(0f, value) * 100f;
            negativeWeight = Math.Max(0f, -value) * 100f;
        }
    }

    public sealed class PositiveOnlyMorphDefinition : CharacterMorphDefinition
    {
        private readonly string femalePositiveShapeName;
        private readonly string malePositiveShapeName;

        public PositiveOnlyMorphDefinition(
            string id,
            string label,
            string group,
            string baseShapeName,
            string femalePositiveShapeName = null,
            string malePositiveShapeName = null)
            : base(id, label, group, baseShapeName)
        {
            this.femalePositiveShapeName = femalePositiveShapeName;
            this.malePositiveShapeName = malePositiveShapeName;
        }

        public override bool RequiresNegativeShape => false;
        public override float MinimumValue => 0f;

        public override string GetPositiveShape(CharacterSex sex)
        {
            if (sex == CharacterSex.Female && !string.IsNullOrEmpty(femalePositiveShapeName))
            {
                return femalePositiveShapeName;
            }

            if (sex == CharacterSex.Male && !string.IsNullOrEmpty(malePositiveShapeName))
            {
                return malePositiveShapeName;
            }

            return base.GetPositiveShape(sex);
        }

        public override string GetNegativeShape(CharacterSex sex)
        {
            return null;
        }

        public override void CalculateWeights(float value, out float positiveWeight, out float negativeWeight)
        {
            positiveWeight = Math.Max(0f, value) * 100f;
            negativeWeight = 0f;
        }
    }

    public sealed class StatGrowthDefinition
    {
        public StatGrowthDefinition(
            string id,
            string label,
            string morphId,
            float minimumMorphValue,
            float maximumMorphValue)
        {
            Id = id;
            Label = label;
            MorphId = morphId;
            MinimumMorphValue = minimumMorphValue;
            MaximumMorphValue = maximumMorphValue;
        }

        public string Id { get; }
        public string Label { get; }
        public string MorphId { get; }
        public float MinimumMorphValue { get; }
        public float MaximumMorphValue { get; }

        public float Evaluate(float normalizedStatValue)
        {
            float clampedValue = Math.Clamp(normalizedStatValue, 0f, 1f);
            return MinimumMorphValue + (MaximumMorphValue - MinimumMorphValue) * clampedValue;
        }
    }

    public static class CharacterMorphCatalog
    {
        private static readonly CharacterMorphDefinition[] Morphs =
        {
            new PositiveOnlyMorphDefinition(
                "body.muscle", "Muscle", "Body", "Body_Stat_Muscle",
                femalePositiveShapeName: "Body_Stat_Muscle"),
            new BipolarMorphDefinition("body.weight", "Body Weight", "Body", "Body_Stat_Weight"),
            new BipolarMorphDefinition("body.height", "Height", "Body", "Body_Height"),
            new PositiveOnlyMorphDefinition("body.breast", "Breast", "Body", "Body_Breast"),
            new BipolarMorphDefinition("body.glutes", "Glutes", "Body", "Body_Glutes"),
            new BipolarMorphDefinition("body.shoulder_width", "Shoulder Width", "Body", "Body_ShoulderWidth"),
            new BipolarMorphDefinition("body.chest_width", "Chest Width", "Body", "Body_ChestWidth"),
            new BipolarMorphDefinition("body.waist", "Waist", "Body", "Body_Waist"),
            new BipolarMorphDefinition("body.hips", "Hips", "Body", "Body_Hips"),
            new BipolarMorphDefinition("head.weight", "Head Weight", "Body", "Head_Stat_Weight"),

            new BipolarMorphDefinition("head.jaw.bite", "Jaw Bite", "Jaw / Chin", "Head_Jaw_Bite"),
            new BipolarMorphDefinition("head.jaw.shape", "Jaw Shape", "Jaw / Chin", "Head_Jaw_Shape"),
            new BipolarMorphDefinition("head.chin.position", "Chin Position", "Jaw / Chin", "Head_Chin_Pos"),
            new BipolarMorphDefinition("head.chin.point", "Chin Point", "Jaw / Chin", "Head_Chin_Point"),
            new BipolarMorphDefinition("head.chin.width", "Chin Width", "Jaw / Chin", "Head_Chin_Width"),

            new BipolarMorphDefinition("head.mouth.width", "Mouth Width", "Mouth", "Head_Mouth_Width"),
            new BipolarMorphDefinition("head.mouth.fullness", "Mouth Fullness", "Mouth", "Head_Mouth_Full"),
            new BipolarMorphDefinition("head.mouth.forward", "Mouth Forward", "Mouth", "Head_Mouth_Forward"),
            new BipolarMorphDefinition("head.mouth.height", "Mouth Height", "Mouth", "Head_Mouth_Height"),

            new BipolarMorphDefinition("head.nose.width", "Nose Width", "Nose", "Head_Nose_Width"),
            new BipolarMorphDefinition("head.nose.curve", "Nose Curve", "Nose", "Head_Nose_Curve"),
            new BipolarMorphDefinition("head.nose.depth", "Nose Depth", "Nose", "Head_Nose_Depth"),
            new BipolarMorphDefinition("head.nose.forward", "Nose Forward", "Nose", "Head_Nose_Forward"),
            new BipolarMorphDefinition("head.nose.septum_angle", "Septum Angle", "Nose", "Head_Nose_SeptumAngle"),

            new BipolarMorphDefinition("head.cheekbone.width", "Cheekbone Width", "Cheeks", "Head_Cheekbone_Width"),
            new BipolarMorphDefinition("head.cheek.fullness", "Cheek Fullness", "Cheeks", "Head_Cheek_Full"),

            new BipolarMorphDefinition("head.eyes.bags", "Eye Bags", "Eyes", "Head_Eyes_Bags"),
            new BipolarMorphDefinition("head.eyes.openness", "Eye Openness", "Eyes", "Head_Eyes_Open"),
            new BipolarMorphDefinition("head.eyes.height", "Eye Height", "Eyes", "Head_Eyes_Height"),
            new BipolarMorphDefinition("head.eyes.distance", "Eye Distance", "Eyes", "Head_Eyes_Distance"),
            new BipolarMorphDefinition("head.eyes.size", "Eye Size", "Eyes", "Head_Eyes_Size"),
            new BipolarMorphDefinition("head.eyes.slant", "Eye Slant", "Eyes", "Head_Eyes_Slant"),

            new BipolarMorphDefinition("head.eyebrows.height", "Eyebrow Height", "Brows", "Head_Eyebrow_Height"),
            new BipolarMorphDefinition("head.eyebrows.angle", "Eyebrow Angle", "Brows", "Head_Eyebrow_Angle"),

            new BipolarMorphDefinition("head.neck.width", "Neck Width", "Neck / Ears", "Head_Neck_Width"),
            new BipolarMorphDefinition("head.ears.shape", "Ear Shape", "Neck / Ears", "Head_Ear_Shape"),
            new BipolarMorphDefinition("head.ears.size", "Ear Size", "Neck / Ears", "Head_Ear_Size"),
            new BipolarMorphDefinition(
                "head.ears.rotation",
                "Ear Rotation",
                "Neck / Ears",
                "Head_Ear_Rotation",
                femaleBaseShapeName: "Head_Ears_Rotation",
                maleBaseShapeName: "Head_Ear_Rotation")
        };

        private static readonly Dictionary<string, CharacterMorphDefinition> ById = BuildLookup();

        public static IReadOnlyList<CharacterMorphDefinition> Definitions => Morphs;

        public static bool TryGet(string id, out CharacterMorphDefinition definition)
        {
            return ById.TryGetValue(id, out definition);
        }

        private static Dictionary<string, CharacterMorphDefinition> BuildLookup()
        {
            var lookup = new Dictionary<string, CharacterMorphDefinition>(StringComparer.Ordinal);
            foreach (CharacterMorphDefinition morph in Morphs)
            {
                lookup.Add(morph.Id, morph);
            }

            return lookup;
        }
    }

    public static class CharacterStatGrowthCatalog
    {
        private static readonly StatGrowthDefinition[] GrowthDefinitions =
        {
            new("muscle", "Muscle Growth", "body.muscle", 0f, 1f),
            new("body_fat", "Body Fat", "body.weight", -1f, 1f)
        };

        private static readonly Dictionary<string, StatGrowthDefinition> ById = BuildLookup();

        public static IReadOnlyList<StatGrowthDefinition> Definitions => GrowthDefinitions;

        public static bool TryGet(string id, out StatGrowthDefinition definition)
        {
            return ById.TryGetValue(id, out definition);
        }

        private static Dictionary<string, StatGrowthDefinition> BuildLookup()
        {
            var lookup = new Dictionary<string, StatGrowthDefinition>(StringComparer.Ordinal);
            foreach (StatGrowthDefinition definition in GrowthDefinitions)
            {
                lookup.Add(definition.Id, definition);
            }

            return lookup;
        }
    }
}
