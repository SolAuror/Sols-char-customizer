using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MalbersAnimations
{
    public static class MArrayListExtensions
    {
        private static readonly System.Random rng = new();
        /// <summary> Shuffles the element order of the specified list using the Fisher-Yates algorithm. </summary>
        public static void MShuffle<T>(this IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        /// <summary>  Resize a List </summary>
        public static void Resize<T>(this List<T> list, int size, T element = default(T))
        {
            int count = list.Count;

            if (size < count)
            {
                list.RemoveRange(size, count - size);
            }
            else if (size > count)
            {
                if (size > list.Capacity)   // Optimization
                    list.Capacity = size;

                list.AddRange(Enumerable.Repeat(element, size - count));
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Get<T>(this Dictionary<string, object> instance, string key) => (T)instance[key];

        //public static void Add<T>(this Dictionary<string, object> instance, string key, object newValue)
        //{
        //    instance.Add(key, (T)newValue);
        //}
    }
}
