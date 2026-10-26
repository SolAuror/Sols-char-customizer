using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    // Colour tables generated at runtime for free-hue fantasy skin. Generating one
    // takes a few milliseconds, so tables are shared by every character with the
    // same hue, rounded to whole degrees, and live until Clear.
    public static class SkinColorTableCache
    {
        private static readonly Dictionary<int, Texture2D> freeHueTables = new();

        public static int Count => freeHueTables.Count;

        public static int QuantizeHue(float hueDegrees)
        {
            return ((Mathf.RoundToInt(hueDegrees) % 360) + 360) % 360;
        }

        public static Texture2D GetFreeHueTable(float hueDegrees)
        {
            int hue = QuantizeHue(hueDegrees);
            if (freeHueTables.TryGetValue(hue, out Texture2D table) && table != null)
            {
                return table;
            }

            table = SkinColorModel.GenerateLut(SkinColorModel.DefaultLutSize, SkinPigments.FromHue(hue));
            table.name = $"Free Hue Skin {hue}";
            table.hideFlags = HideFlags.DontSave;
            freeHueTables[hue] = table;
            return table;
        }

        public static void Clear()
        {
            foreach (Texture2D table in freeHueTables.Values)
            {
                if (table == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Object.Destroy(table);
                }
                else
                {
                    Object.DestroyImmediate(table);
                }
            }

            freeHueTables.Clear();
        }
    }
}
