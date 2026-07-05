using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    public enum CharacterSex
    {
        Female,
        Male
    }

    public enum CharacterMorphDriverType
    {
        BlendShape,
        BoneScale,
        BoneOffset,
        RigParameter
    }

    public enum CharacterRigProportionChannel
    {
        None,
        Height,
        ShoulderWidth,
        HipsWidth,
        UpperBody,
        LowerBody,
        Spine,
        Chest,
        Waist,
        Head,
        Neck,
        Shoulders,
        UpperArms,
        LowerArms,
        Hands,
        Fingers,
        Legs,
        Feet,
        FootRadius
    }

    public abstract class CharacterMorphDefinition
    {
        protected CharacterMorphDefinition(
            string id,
            string label,
            string group,
            string baseShapeName,
            CharacterMorphDriverType driverType = CharacterMorphDriverType.BlendShape,
            string femaleBaseShapeName = null,
            string maleBaseShapeName = null,
            bool visibleInCreator = true,
            CharacterRigProportionChannel rigChannel = CharacterRigProportionChannel.None)
        {
            Id = id;
            Label = label;
            Group = group;
            BaseShapeName = baseShapeName;
            DriverType = driverType;
            FemaleBaseShapeName = femaleBaseShapeName;
            MaleBaseShapeName = maleBaseShapeName;
            VisibleInCreator = visibleInCreator;
            RigChannel = rigChannel;
        }

        public string Id { get; }
        public string Label { get; }
        public string Group { get; }
        public string BaseShapeName { get; }
        public CharacterMorphDriverType DriverType { get; }
        public string FemaleBaseShapeName { get; }
        public string MaleBaseShapeName { get; }
        public bool VisibleInCreator { get; }
        public CharacterRigProportionChannel RigChannel { get; }
        public abstract bool RequiresNegativeShape { get; }
        public bool IsBipolar => RequiresNegativeShape;
        public bool IsBlendShapeDriven => DriverType == CharacterMorphDriverType.BlendShape;
        public bool IsSkeletalDriven => DriverType != CharacterMorphDriverType.BlendShape;
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
            CharacterMorphDriverType driverType = CharacterMorphDriverType.BlendShape,
            string femaleBaseShapeName = null,
            string maleBaseShapeName = null,
            bool visibleInCreator = true,
            CharacterRigProportionChannel rigChannel = CharacterRigProportionChannel.None)
            : base(id, label, group, baseShapeName, driverType, femaleBaseShapeName, maleBaseShapeName, visibleInCreator, rigChannel)
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
            CharacterMorphDriverType driverType = CharacterMorphDriverType.BlendShape,
            string femalePositiveShapeName = null,
            string malePositiveShapeName = null,
            bool visibleInCreator = true,
            CharacterRigProportionChannel rigChannel = CharacterRigProportionChannel.None)
            : base(id, label, group, baseShapeName, driverType, null, null, visibleInCreator, rigChannel)
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

    [Serializable]
    public sealed class CharacterMorphCatalogEntry
    {
        [SerializeField] private string id;
        [SerializeField] private string label;
        [SerializeField] private string group;
        [SerializeField] private string baseShapeName;
        [SerializeField] private CharacterMorphDriverType driverType = CharacterMorphDriverType.BlendShape;
        [SerializeField] private bool requiresNegativeShape = true;
        [SerializeField] private string femaleBaseShapeName;
        [SerializeField] private string maleBaseShapeName;
        [SerializeField] private string femalePositiveShapeName;
        [SerializeField] private string malePositiveShapeName;
        [SerializeField] private bool visibleInCreator = true;
        [SerializeField] private CharacterRigProportionChannel rigChannel = CharacterRigProportionChannel.None;

        public CharacterMorphCatalogEntry()
        {
        }

        public CharacterMorphCatalogEntry(CharacterMorphDefinition definition)
        {
            id = definition.Id;
            label = definition.Label;
            group = definition.Group;
            baseShapeName = definition.BaseShapeName;
            driverType = definition.DriverType;
            requiresNegativeShape = definition.RequiresNegativeShape;
            femaleBaseShapeName = definition.FemaleBaseShapeName;
            maleBaseShapeName = definition.MaleBaseShapeName;
            visibleInCreator = definition.VisibleInCreator;
            rigChannel = definition.RigChannel;

            if (!requiresNegativeShape)
            {
                string femalePositive = definition.GetPositiveShape(CharacterSex.Female);
                string malePositive = definition.GetPositiveShape(CharacterSex.Male);
                femalePositiveShapeName = string.Equals(femalePositive, definition.BaseShapeName + "+", StringComparison.Ordinal) ? null : femalePositive;
                malePositiveShapeName = string.Equals(malePositive, definition.BaseShapeName + "+", StringComparison.Ordinal) ? null : malePositive;
            }
        }

        public string Id => id;
        public string Label => label;
        public string Group => group;
        public string BaseShapeName => baseShapeName;
        public CharacterMorphDriverType DriverType => driverType;
        public bool RequiresNegativeShape => requiresNegativeShape;
        public bool VisibleInCreator => visibleInCreator;
        public CharacterRigProportionChannel RigChannel => rigChannel;

        public CharacterMorphDefinition ToDefinition()
        {
            if (requiresNegativeShape)
            {
                return new BipolarMorphDefinition(
                    id,
                    label,
                    group,
                    baseShapeName,
                    driverType,
                    femaleBaseShapeName,
                    maleBaseShapeName,
                    visibleInCreator,
                    rigChannel);
            }

            return new PositiveOnlyMorphDefinition(
                id,
                label,
                group,
                baseShapeName,
                driverType,
                femalePositiveShapeName,
                malePositiveShapeName,
                visibleInCreator,
                rigChannel);
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
        public const string RigBackendGroup = "Rig Backend";

        private static readonly CharacterMorphDefinition[] Morphs =
        {
            new PositiveOnlyMorphDefinition(
                "body.muscle", "Muscle", "Body", "Body_Stat_Muscle",
                femalePositiveShapeName: "Body_Stat_Muscle"),
            new BipolarMorphDefinition("body.weight", "Body Weight", "Body", "Body_Stat_Weight"),
            new BipolarMorphDefinition(
                "body.height", "Height", "Body", "Body_Height",
                driverType: CharacterMorphDriverType.BoneScale,
                rigChannel: CharacterRigProportionChannel.Height),
            new PositiveOnlyMorphDefinition("body.breast", "Breast", "Body", "Body_Breast"),
            new BipolarMorphDefinition("body.glutes", "Glutes", "Body", "Body_Glutes"),
            new BipolarMorphDefinition(
                "body.shoulder_width", "Shoulder Width", "Body", "Body_ShoulderWidth",
                driverType: CharacterMorphDriverType.BoneOffset,
                rigChannel: CharacterRigProportionChannel.ShoulderWidth),
            new BipolarMorphDefinition("body.chest_width", "Chest Width", "Body", "Body_ChestWidth"),
            new BipolarMorphDefinition("body.waist", "Waist", "Body", "Body_Waist"),
            new BipolarMorphDefinition(
                "body.hips", "Hips", "Body", "Body_Hips",
                driverType: CharacterMorphDriverType.BoneOffset,
                rigChannel: CharacterRigProportionChannel.HipsWidth),
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
                maleBaseShapeName: "Head_Ear_Rotation"),

            HiddenRig("rig.upper_body", "Upper Body", CharacterRigProportionChannel.UpperBody),
            HiddenRig("rig.lower_body", "Lower Body", CharacterRigProportionChannel.LowerBody),
            HiddenRig("rig.spine", "Spine", CharacterRigProportionChannel.Spine),
            HiddenRig("rig.chest", "Chest", CharacterRigProportionChannel.Chest),
            HiddenRig("rig.waist", "Waist", CharacterRigProportionChannel.Waist),
            HiddenRig("rig.head", "Head Scale", CharacterRigProportionChannel.Head),
            HiddenRig("rig.neck", "Neck Scale", CharacterRigProportionChannel.Neck),
            HiddenRig("rig.shoulders", "Shoulder Scale", CharacterRigProportionChannel.Shoulders),
            HiddenRig("rig.upper_arms", "Upper Arms", CharacterRigProportionChannel.UpperArms),
            HiddenRig("rig.lower_arms", "Lower Arms", CharacterRigProportionChannel.LowerArms),
            HiddenRig("rig.hands", "Hands", CharacterRigProportionChannel.Hands),
            HiddenRig("rig.fingers", "Fingers", CharacterRigProportionChannel.Fingers),
            HiddenRig("rig.legs", "Legs", CharacterRigProportionChannel.Legs),
            HiddenRig("rig.feet", "Feet", CharacterRigProportionChannel.Feet),
            HiddenRig("rig.foot_radius", "Foot Radius", CharacterRigProportionChannel.FootRadius, CharacterMorphDriverType.RigParameter)
        };

        private static readonly Dictionary<string, CharacterMorphDefinition> ById = BuildLookup(Morphs);

        public static IReadOnlyList<CharacterMorphDefinition> Definitions => Morphs;

        public static bool TryGet(string id, out CharacterMorphDefinition definition)
        {
            return ById.TryGetValue(id, out definition);
        }

        public static bool TryGet(IReadOnlyList<CharacterMorphDefinition> definitions, string id, out CharacterMorphDefinition definition)
        {
            definition = null;
            if (definitions == null || string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            for (int index = 0; index < definitions.Count; index++)
            {
                if (string.Equals(definitions[index].Id, id, StringComparison.Ordinal))
                {
                    definition = definitions[index];
                    return true;
                }
            }

            return false;
        }

        internal static Dictionary<string, CharacterMorphDefinition> BuildLookup(IReadOnlyList<CharacterMorphDefinition> definitions)
        {
            var lookup = new Dictionary<string, CharacterMorphDefinition>(StringComparer.Ordinal);
            foreach (CharacterMorphDefinition morph in definitions)
            {
                lookup.Add(morph.Id, morph);
            }

            return lookup;
        }

        private static BipolarMorphDefinition HiddenRig(
            string id,
            string label,
            CharacterRigProportionChannel channel,
            CharacterMorphDriverType driverType = CharacterMorphDriverType.BoneScale)
        {
            return new BipolarMorphDefinition(
                id,
                label,
                RigBackendGroup,
                string.Empty,
                driverType: driverType,
                visibleInCreator: false,
                rigChannel: channel);
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
