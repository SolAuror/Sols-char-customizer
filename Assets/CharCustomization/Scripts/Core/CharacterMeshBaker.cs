using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    // Folds creator blendshapes into the base vertices of a mesh copy. Folded shapes
    // are removed; every other shape (expressions, visemes) is kept with its deltas,
    // so a baked character stops paying for live body-morph blending each frame.
    public static class CharacterMeshBaker
    {
        private const float WeightEpsilon = 0.0001f;

        private static readonly Dictionary<string, SharedMesh> sharedByKey = new(StringComparer.Ordinal);
        private static readonly Dictionary<Mesh, SharedMesh> sharedByMesh = new();

        public static bool UsesAnyShape(Mesh mesh, IReadOnlyDictionary<string, float> foldWeights)
        {
            if (mesh == null || foldWeights == null)
            {
                return false;
            }

            for (int index = 0; index < mesh.blendShapeCount; index++)
            {
                if (foldWeights.ContainsKey(mesh.GetBlendShapeName(index)))
                {
                    return true;
                }
            }

            return false;
        }

        // foldWeights maps blendshape names to SkinnedMeshRenderer weights (0..100).
        // Every named shape is removed from the result, including zero-weight ones.
        public static bool TryBake(
            Mesh source,
            IReadOnlyDictionary<string, float> foldWeights,
            out Mesh baked,
            out string error)
        {
            baked = null;
            if (source == null)
            {
                error = "Cannot bake a null mesh.";
                return false;
            }

            if (!source.isReadable)
            {
                error = $"Mesh '{source.name}' must have Read/Write enabled in its import settings to be baked.";
                return false;
            }

            foldWeights ??= new Dictionary<string, float>();
            int vertexCount = source.vertexCount;
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector4[] tangents = source.tangents;
            bool hasNormals = normals.Length == vertexCount;
            bool hasTangents = tangents.Length == vertexCount;

            var frameVertices = new Vector3[vertexCount];
            var frameNormals = new Vector3[vertexCount];
            var frameTangents = new Vector3[vertexCount];
            var nextVertices = new Vector3[vertexCount];
            var nextNormals = new Vector3[vertexCount];
            var nextTangents = new Vector3[vertexCount];
            var keptShapes = new List<KeptShape>();
            bool foldedAny = false;

            for (int shape = 0; shape < source.blendShapeCount; shape++)
            {
                string shapeName = source.GetBlendShapeName(shape);
                if (!foldWeights.TryGetValue(shapeName, out float weight))
                {
                    keptShapes.Add(KeptShape.Read(source, shape, shapeName, vertexCount));
                    continue;
                }

                if (Mathf.Abs(weight) <= WeightEpsilon)
                {
                    continue;
                }

                if (!TryResolveFrames(source, shape, weight, out int frame, out int nextFrame, out float blend))
                {
                    continue;
                }

                source.GetBlendShapeFrameVertices(shape, frame, frameVertices, frameNormals, frameTangents);
                float frameScale = nextFrame < 0 ? blend : 1f - blend;
                if (nextFrame >= 0)
                {
                    source.GetBlendShapeFrameVertices(shape, nextFrame, nextVertices, nextNormals, nextTangents);
                }

                for (int vertex = 0; vertex < vertexCount; vertex++)
                {
                    Vector3 vertexDelta = frameVertices[vertex] * frameScale;
                    Vector3 normalDelta = frameNormals[vertex] * frameScale;
                    Vector3 tangentDelta = frameTangents[vertex] * frameScale;
                    if (nextFrame >= 0)
                    {
                        vertexDelta += nextVertices[vertex] * blend;
                        normalDelta += nextNormals[vertex] * blend;
                        tangentDelta += nextTangents[vertex] * blend;
                    }

                    vertices[vertex] += vertexDelta;
                    if (hasNormals)
                    {
                        normals[vertex] += normalDelta;
                    }

                    if (hasTangents)
                    {
                        tangents[vertex] += (Vector4)tangentDelta;
                    }
                }

                foldedAny = true;
            }

            if (foldedAny)
            {
                NormalizeBasis(normals, tangents, hasNormals, hasTangents);
            }

            baked = UnityEngine.Object.Instantiate(source);
            baked.name = $"{source.name} (Baked)";
            baked.ClearBlendShapes();
            baked.vertices = vertices;
            if (hasNormals)
            {
                baked.normals = normals;
            }

            if (hasTangents)
            {
                baked.tangents = tangents;
            }

            foreach (KeptShape keptShape in keptShapes)
            {
                keptShape.WriteTo(baked);
            }

            baked.RecalculateBounds();
            error = null;
            return true;
        }

        // Characters with identical body weights on the same source mesh share one
        // baked mesh, so a crowd built from a few presets costs a few meshes.
        public static bool TryAcquireShared(
            Mesh source,
            IReadOnlyDictionary<string, float> foldWeights,
            out Mesh baked,
            out string error)
        {
            baked = null;
            if (source == null)
            {
                error = "Cannot bake a null mesh.";
                return false;
            }

            string key = BuildKey(source, foldWeights);
            if (sharedByKey.TryGetValue(key, out SharedMesh shared))
            {
                if (shared.Mesh != null)
                {
                    shared.References++;
                    baked = shared.Mesh;
                    error = null;
                    return true;
                }

                sharedByKey.Remove(key);
            }

            if (!TryBake(source, foldWeights, out baked, out error))
            {
                return false;
            }

            // The source keeps the CPU copy; the shared result only needs GPU data.
            baked.UploadMeshData(true);
            shared = new SharedMesh(key, baked);
            sharedByKey.Add(key, shared);
            sharedByMesh[baked] = shared;
            return true;
        }

        public static void ReleaseShared(Mesh baked)
        {
            if (ReferenceEquals(baked, null) || !sharedByMesh.TryGetValue(baked, out SharedMesh shared))
            {
                return;
            }

            shared.References--;
            if (shared.References > 0)
            {
                return;
            }

            sharedByMesh.Remove(baked);
            sharedByKey.Remove(shared.Key);
            if (baked == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(baked);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(baked);
            }
        }

        public static int GetSharedReferenceCount(Mesh baked)
        {
            return baked != null && sharedByMesh.TryGetValue(baked, out SharedMesh shared) ? shared.References : 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedCache()
        {
            // Keeps the cache valid when domain reload is disabled for Play Mode.
            sharedByKey.Clear();
            sharedByMesh.Clear();
        }

        // Matches Unity's blendshape evaluation: below the first frame the first
        // delta is scaled from zero, between frames the two are interpolated, and
        // past the last frame the last pair is extrapolated.
        private static bool TryResolveFrames(Mesh mesh, int shape, float weight, out int frame, out int nextFrame, out float blend)
        {
            int frameCount = mesh.GetBlendShapeFrameCount(shape);
            frame = 0;
            nextFrame = -1;
            blend = 0f;
            if (frameCount == 0)
            {
                return false;
            }

            float firstWeight = mesh.GetBlendShapeFrameWeight(shape, 0);
            if (frameCount == 1 || weight <= firstWeight)
            {
                blend = Mathf.Approximately(firstWeight, 0f) ? 0f : weight / firstWeight;
                return true;
            }

            int lower = frameCount - 2;
            for (int index = 0; index < frameCount - 1; index++)
            {
                if (weight <= mesh.GetBlendShapeFrameWeight(shape, index + 1))
                {
                    lower = index;
                    break;
                }
            }

            float lowerWeight = mesh.GetBlendShapeFrameWeight(shape, lower);
            float upperWeight = mesh.GetBlendShapeFrameWeight(shape, lower + 1);
            frame = lower;
            nextFrame = lower + 1;
            blend = Mathf.Approximately(upperWeight, lowerWeight) ? 1f : (weight - lowerWeight) / (upperWeight - lowerWeight);
            return true;
        }

        private static void NormalizeBasis(Vector3[] normals, Vector4[] tangents, bool hasNormals, bool hasTangents)
        {
            if (hasNormals)
            {
                for (int index = 0; index < normals.Length; index++)
                {
                    normals[index] = normals[index].normalized;
                }
            }

            if (!hasTangents)
            {
                return;
            }

            for (int index = 0; index < tangents.Length; index++)
            {
                Vector4 tangent = tangents[index];
                Vector3 direction = new Vector3(tangent.x, tangent.y, tangent.z).normalized;
                tangents[index] = new Vector4(direction.x, direction.y, direction.z, tangent.w);
            }
        }

        private static string BuildKey(Mesh source, IReadOnlyDictionary<string, float> foldWeights)
        {
            var builder = new StringBuilder();
            builder.Append(source.GetInstanceID());
            for (int shape = 0; shape < source.blendShapeCount; shape++)
            {
                if (foldWeights == null || !foldWeights.TryGetValue(source.GetBlendShapeName(shape), out float weight))
                {
                    continue;
                }

                // Hundredths of a weight point are far below anything visible.
                builder.Append('|').Append(shape).Append(':').Append(Mathf.RoundToInt(weight * 100f));
            }

            return builder.ToString();
        }

        private sealed class SharedMesh
        {
            public SharedMesh(string key, Mesh mesh)
            {
                Key = key;
                Mesh = mesh;
                References = 1;
            }

            public string Key { get; }
            public Mesh Mesh { get; }
            public int References { get; set; }
        }

        private readonly struct KeptShape
        {
            private readonly string name;
            private readonly float[] frameWeights;
            private readonly Vector3[][] vertices;
            private readonly Vector3[][] normals;
            private readonly Vector3[][] tangents;

            private KeptShape(string name, float[] frameWeights, Vector3[][] vertices, Vector3[][] normals, Vector3[][] tangents)
            {
                this.name = name;
                this.frameWeights = frameWeights;
                this.vertices = vertices;
                this.normals = normals;
                this.tangents = tangents;
            }

            public static KeptShape Read(Mesh mesh, int shape, string name, int vertexCount)
            {
                int frameCount = mesh.GetBlendShapeFrameCount(shape);
                var frameWeights = new float[frameCount];
                var vertices = new Vector3[frameCount][];
                var normals = new Vector3[frameCount][];
                var tangents = new Vector3[frameCount][];
                for (int frame = 0; frame < frameCount; frame++)
                {
                    frameWeights[frame] = mesh.GetBlendShapeFrameWeight(shape, frame);
                    vertices[frame] = new Vector3[vertexCount];
                    normals[frame] = new Vector3[vertexCount];
                    tangents[frame] = new Vector3[vertexCount];
                    mesh.GetBlendShapeFrameVertices(shape, frame, vertices[frame], normals[frame], tangents[frame]);
                }

                return new KeptShape(name, frameWeights, vertices, normals, tangents);
            }

            public void WriteTo(Mesh mesh)
            {
                for (int frame = 0; frame < frameWeights.Length; frame++)
                {
                    mesh.AddBlendShapeFrame(name, frameWeights[frame], vertices[frame], normals[frame], tangents[frame]);
                }
            }
        }
    }
}
