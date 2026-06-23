using System;
using System.Collections.Generic;

namespace Sol.CharacterCustomization
{
    public enum CharacterSex
    {
        Female,
        Male
    }

    public sealed class CharacterMorphDefinition
    {
        public CharacterMorphDefinition(
            string id,
            string label,
            string group,
            string baseShapeName,
            bool isBipolar = true,
            string femaleBaseShapeName = null,
            string maleBaseShapeName = null)
        {
            Id = id;
            Label = label;
            Group = group;
            BaseShapeName = baseShapeName;
            IsBipolar = isBipolar;
            FemaleBaseShapeName = femaleBaseShapeName;
            MaleBaseShapeName = maleBaseShapeName;
        }

        public string Id { get; }
        public string Label { get; }
        public string Group { get; }
        public string BaseShapeName { get; }
        public bool IsBipolar { get; }
        public string FemaleBaseShapeName { get; }
        public string MaleBaseShapeName { get; }
        public float MinimumValue => IsBipolar ? -1f : 0f;

        public string GetPositiveShape(CharacterSex sex)
        {
            string baseName = GetBaseShapeName(sex);

            // The female source mesh exports muscle without a '+' suffix.
            if (Id == "body.muscle" && sex == CharacterSex.Female)
            {
                return baseName;
            }

            return baseName + "+";
        }

        public string GetNegativeShape(CharacterSex sex)
        {
            return IsBipolar ? GetBaseShapeName(sex) + "-" : null;
        }

        private string GetBaseShapeName(CharacterSex sex)
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

    public static class CharacterMorphCatalog
    {
        private static readonly CharacterMorphDefinition[] Morphs =
        {
            new("body.muscle", "Muscle", "Body", "Body_Stat_Muscle", false),
            new("body.weight", "Body Weight", "Body", "Body_Stat_Weight"),
            new("body.height", "Height", "Body", "Body_Height"),
            new("body.breast", "Breast", "Body", "Body_Breast", false),
            new("body.glutes", "Glutes", "Body", "Body_Glutes"),
            new("body.shoulder_width", "Shoulder Width", "Body", "Body_ShoulderWidth"),
            new("body.chest_width", "Chest Width", "Body", "Body_ChestWidth"),
            new("body.waist", "Waist", "Body", "Body_Waist"),
            new("body.hips", "Hips", "Body", "Body_Hips"),
            new("head.weight", "Head Weight", "Body", "Head_Stat_Weight"),

            new("head.jaw.bite", "Jaw Bite", "Jaw / Chin", "Head_Jaw_Bite"),
            new("head.jaw.shape", "Jaw Shape", "Jaw / Chin", "Head_Jaw_Shape"),
            new("head.chin.position", "Chin Position", "Jaw / Chin", "Head_Chin_Pos"),
            new("head.chin.point", "Chin Point", "Jaw / Chin", "Head_Chin_Point"),
            new("head.chin.width", "Chin Width", "Jaw / Chin", "Head_Chin_Width"),

            new("head.mouth.width", "Mouth Width", "Mouth", "Head_Mouth_Width"),
            new("head.mouth.fullness", "Mouth Fullness", "Mouth", "Head_Mouth_Full"),
            new("head.mouth.forward", "Mouth Forward", "Mouth", "Head_Mouth_Forward"),
            new("head.mouth.height", "Mouth Height", "Mouth", "Head_Mouth_Height"),

            new("head.nose.width", "Nose Width", "Nose", "Head_Nose_Width"),
            new("head.nose.curve", "Nose Curve", "Nose", "Head_Nose_Curve"),
            new("head.nose.depth", "Nose Depth", "Nose", "Head_Nose_Depth"),
            new("head.nose.forward", "Nose Forward", "Nose", "Head_Nose_Forward"),
            new("head.nose.septum_angle", "Septum Angle", "Nose", "Head_Nose_SeptumAngle"),

            new("head.cheekbone.width", "Cheekbone Width", "Cheeks", "Head_Cheekbone_Width"),
            new("head.cheek.fullness", "Cheek Fullness", "Cheeks", "Head_Cheek_Full"),

            new("head.eyes.bags", "Eye Bags", "Eyes", "Head_Eyes_Bags"),
            new("head.eyes.openness", "Eye Openness", "Eyes", "Head_Eyes_Open"),
            new("head.eyes.height", "Eye Height", "Eyes", "Head_Eyes_Height"),
            new("head.eyes.distance", "Eye Distance", "Eyes", "Head_Eyes_Distance"),
            new("head.eyes.size", "Eye Size", "Eyes", "Head_Eyes_Size"),
            new("head.eyes.slant", "Eye Slant", "Eyes", "Head_Eyes_Slant"),

            new("head.eyebrows.height", "Eyebrow Height", "Brows", "Head_Eyebrow_Height"),
            new("head.eyebrows.angle", "Eyebrow Angle", "Brows", "Head_Eyebrow_Angle"),

            new("head.neck.width", "Neck Width", "Neck / Ears", "Head_Neck_Width"),
            new("head.ears.shape", "Ear Shape", "Neck / Ears", "Head_Ear_Shape"),
            new("head.ears.size", "Ear Size", "Neck / Ears", "Head_Ear_Size"),
            new(
                "head.ears.rotation",
                "Ear Rotation",
                "Neck / Ears",
                "Head_Ear_Rotation",
                true,
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
}
