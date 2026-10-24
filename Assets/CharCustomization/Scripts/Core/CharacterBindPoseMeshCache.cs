using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    // Shares mesh copies that differ from their source only in bind poses, so
    // characters with the same joint offsets (NPCs from one preset) use one mesh,
    // and the NPC baker can share their baked meshes in turn.
    public static class CharacterBindPoseMeshCache
    {
        private const float BindPoseTolerance = 1e-5f;

        private static readonly Dictionary<long, List<Entry>> entriesByKey = new();
        private static readonly Dictionary<Mesh, Entry> entriesByMesh = new();

        public static Mesh Acquire(Mesh source, Matrix4x4[] bindposes)
        {
            long key = Key(source, bindposes);
            if (entriesByKey.TryGetValue(key, out List<Entry> candidates))
            {
                foreach (Entry candidate in candidates)
                {
                    if (candidate.Mesh != null && Matches(candidate.Bindposes, bindposes))
                    {
                        candidate.References++;
                        return candidate.Mesh;
                    }
                }
            }
            else
            {
                candidates = new List<Entry>();
                entriesByKey.Add(key, candidates);
            }

            Mesh copy = CreateCopy(source, bindposes);
            var entry = new Entry(key, copy, (Matrix4x4[])bindposes.Clone());
            candidates.Add(entry);
            entriesByMesh[copy] = entry;
            return copy;
        }

        public static void Release(Mesh mesh)
        {
            if (ReferenceEquals(mesh, null) || !entriesByMesh.TryGetValue(mesh, out Entry entry))
            {
                return;
            }

            entry.References--;
            if (entry.References > 0)
            {
                return;
            }

            entriesByMesh.Remove(mesh);
            if (entriesByKey.TryGetValue(entry.Key, out List<Entry> candidates))
            {
                candidates.Remove(entry);
                if (candidates.Count == 0)
                {
                    entriesByKey.Remove(entry.Key);
                }
            }

            DestroyMesh(mesh);
        }

        public static int GetReferenceCount(Mesh mesh)
        {
            return mesh != null && entriesByMesh.TryGetValue(mesh, out Entry entry) ? entry.References : 0;
        }

        // A private, mutable copy for live editing; the caller owns and destroys it.
        public static Mesh CreateCopy(Mesh source, Matrix4x4[] bindposes)
        {
            Mesh copy = Object.Instantiate(source);
            copy.name = $"{source.name} (Joints)";
            copy.hideFlags = HideFlags.DontSave;
            copy.bindposes = bindposes;
            return copy;
        }

        public static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(mesh);
            }
            else
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            // Keeps the cache valid when domain reload is disabled for Play Mode.
            entriesByKey.Clear();
            entriesByMesh.Clear();
        }

        private static long Key(Mesh source, Matrix4x4[] bindposes)
        {
            // FNV-1a over quantized translations; rotations are never changed by joint
            // offsets, and equality is confirmed against the stored matrices.
            ulong hash = 14695981039346656037UL ^ (ulong)(uint)source.GetInstanceID();
            foreach (Matrix4x4 bindpose in bindposes)
            {
                for (int row = 0; row < 3; row++)
                {
                    hash ^= (ulong)(uint)Mathf.RoundToInt(bindpose[row, 3] / BindPoseTolerance);
                    hash *= 1099511628211UL;
                }
            }

            return (long)hash;
        }

        private static bool Matches(Matrix4x4[] first, Matrix4x4[] second)
        {
            if (first.Length != second.Length)
            {
                return false;
            }

            for (int index = 0; index < first.Length; index++)
            {
                for (int element = 0; element < 16; element++)
                {
                    if (Mathf.Abs(first[index][element] - second[index][element]) > BindPoseTolerance)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private sealed class Entry
        {
            public Entry(long key, Mesh mesh, Matrix4x4[] bindposes)
            {
                Key = key;
                Mesh = mesh;
                Bindposes = bindposes;
                References = 1;
            }

            public long Key { get; }
            public Mesh Mesh { get; }
            public Matrix4x4[] Bindposes { get; }
            public int References { get; set; }
        }
    }
}
