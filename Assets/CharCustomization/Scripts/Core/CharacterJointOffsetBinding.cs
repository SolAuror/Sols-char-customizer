using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sol.CharacterCustomization
{
    // Applies a joint offset table to one character. Each shape's joint offsets are
    // weighted by that blendshape's current weight; the bones move by the result
    // and their bind poses move with them, so the reshaped skin is not displaced a
    // second time and the joints sit inside the new shape.
    public sealed class CharacterJointOffsetBinding
    {
        private const float MaxCalibrationError = 0.02f;
        private const float MinOffset = 1e-6f;

        private readonly List<BoneState> bones = new();
        private readonly List<ShapeSource> shapeSources = new();
        private readonly List<RendererState> renderers = new();
        private readonly Dictionary<Transform, Vector3> localOffsets = new();
        private Dictionary<Transform, BoneState> stateByTransform = new();
        private bool weightsDirty = true;

        private CharacterJointOffsetBinding(CharacterJointOffsetTable table, JointSpaceCalibration calibration)
        {
            Table = table;
            Calibration = calibration;
        }

        public CharacterJointOffsetTable Table { get; }
        public JointSpaceCalibration Calibration { get; }
        public int BoundBoneCount => bones.Count;
        public int BoundShapeCount => shapeSources.Count;

        // Rest local-position offsets of every moved bone, including bones the
        // Animator positions itself (the hips), for the Avatar rebuild.
        public IReadOnlyDictionary<Transform, Vector3> LocalOffsets => localOffsets;
        public bool HasOffsets => localOffsets.Count > 0;

        public static bool TryCreate(
            CharacterJointOffsetTable table,
            Transform root,
            Transform animatorPositionedBone,
            out CharacterJointOffsetBinding binding,
            out string error)
        {
            binding = null;
            if (table == null || root == null)
            {
                error = "A joint offset table and a character root are required.";
                return false;
            }

            SkinnedMeshRenderer[] skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer body = FindBody(skinned, table);
            if (body == null)
            {
                error = $"'{root.name}' has no skinned mesh to bind '{table.name}' to.";
                return false;
            }

            var transformsByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                transformsByName.TryAdd(candidate.name, candidate);
            }

            Dictionary<Transform, Matrix4x4> bodyRest = RestMatrices(body);
            var restInBody = new Dictionary<Transform, Matrix4x4>(bodyRest);
            var rendererToBody = new Dictionary<SkinnedMeshRenderer, Matrix4x4>();
            foreach (SkinnedMeshRenderer renderer in skinned)
            {
                if (!TryRendererToBody(renderer, body, bodyRest, out Matrix4x4 toBody))
                {
                    continue;
                }

                rendererToBody[renderer] = toBody;
                foreach (KeyValuePair<Transform, Matrix4x4> rest in RestMatrices(renderer))
                {
                    restInBody.TryAdd(rest.Key, toBody * rest.Value);
                }
            }

            var exported = new List<Vector3>();
            var imported = new List<Vector3>();
            var resolved = new Transform[table.Bones.Count];
            for (int index = 0; index < table.Bones.Count; index++)
            {
                if (!transformsByName.TryGetValue(table.Bones[index].Name, out Transform bone))
                {
                    continue;
                }

                resolved[index] = bone;
                if (bodyRest.TryGetValue(bone, out Matrix4x4 rest))
                {
                    exported.Add(table.Bones[index].RestHead);
                    imported.Add(rest.GetColumn(3));
                }
            }

            if (!JointSpaceCalibration.TrySolve(exported, imported, out JointSpaceCalibration calibration))
            {
                error = $"Only {exported.Count} of '{table.name}''s bones are skinned to '{body.name}'; " +
                        "at least three are needed to match the exported skeleton.";
                return false;
            }

            if (calibration.RelativeError > MaxCalibrationError)
            {
                error = $"'{table.name}' does not match '{root.name}': rest joints differ by " +
                        $"{calibration.RelativeError:P1} of the skeleton's size. Re-export the sidecar from this character.";
                return false;
            }

            binding = new CharacterJointOffsetBinding(table, calibration);
            binding.BuildBones(resolved, restInBody, body.transform, animatorPositionedBone);
            binding.BuildShapes(skinned, body);
            binding.BuildRenderers(skinned, rendererToBody);
            error = null;
            return true;
        }

        public void MarkWeightsDirty()
        {
            weightsDirty = true;
        }

        // Re-reads blendshape weights when they changed, updates offsets and bind
        // poses, and returns whether anything was recomputed.
        public bool Evaluate()
        {
            if (!weightsDirty)
            {
                return false;
            }

            // A baked renderer has a different blendshape layout; offsets stay as the
            // bake captured them until the character is unbaked.
            foreach (ShapeSource source in shapeSources)
            {
                if (source.Renderer != null && source.Renderer.sharedMesh != null &&
                    source.Renderer.sharedMesh.blendShapeCount != source.BlendShapeCount)
                {
                    return false;
                }
            }

            weightsDirty = false;
            foreach (BoneState bone in bones)
            {
                bone.MeshOffset = Vector3.zero;
            }

            foreach (ShapeSource source in shapeSources)
            {
                if (source.Renderer == null)
                {
                    continue;
                }

                float weight = source.Renderer.GetBlendShapeWeight(source.BlendShapeIndex) / 100f;
                if (Mathf.Abs(weight) <= 1e-5f)
                {
                    continue;
                }

                for (int index = 0; index < source.Bones.Length; index++)
                {
                    source.Bones[index].MeshOffset += source.Offsets[index] * weight;
                }
            }

            localOffsets.Clear();
            foreach (BoneState bone in bones)
            {
                Vector3 parentOffset = bone.OffsetParent != null ? bone.OffsetParent.MeshOffset : Vector3.zero;
                bone.LocalOffset = bone.ParentRestInverse.MultiplyVector(bone.MeshOffset - parentOffset);
                if (bone.LocalOffset.sqrMagnitude > MinOffset * MinOffset)
                {
                    localOffsets[bone.Transform] = bone.LocalOffset;
                }
            }

            foreach (RendererState renderer in renderers)
            {
                UpdateBindPoses(renderer);
            }

            return true;
        }

        // Called on every pose application: bones the Animator does not position keep
        // their offset rest position.
        public void ApplyLocalPositions()
        {
            foreach (BoneState bone in bones)
            {
                if (bone.Transform != null && !bone.AnimatorPositioned)
                {
                    bone.Transform.localPosition = bone.BindLocalPosition + bone.LocalOffset;
                }
            }
        }

        // Swaps private, editable mesh copies for shared ones, so characters with
        // identical offsets share meshes. Editing again makes a new private copy.
        public void Settle()
        {
            foreach (RendererState renderer in renderers)
            {
                if (renderer.Renderer == null || renderer.Private == null ||
                    renderer.Renderer.sharedMesh != renderer.Private)
                {
                    continue;
                }

                Mesh shared = CharacterBindPoseMeshCache.Acquire(renderer.Original, renderer.Private.bindposes);
                AssignMesh(renderer.Renderer, shared);
                ReleasePrivate(renderer);
                Handle(renderer.Renderer).Track(shared);
                renderer.Shared = shared;
            }
        }

        public void Restore()
        {
            foreach (RendererState renderer in renderers)
            {
                if (renderer.Renderer != null && renderer.Renderer.sharedMesh != renderer.Original &&
                    IsManaged(renderer, renderer.Renderer.sharedMesh))
                {
                    AssignMesh(renderer.Renderer, renderer.Original);
                }

                ReleasePrivate(renderer);
                ReleaseShared(renderer);
            }

            foreach (BoneState bone in bones)
            {
                if (bone.Transform != null && !bone.AnimatorPositioned)
                {
                    bone.Transform.localPosition = bone.BindLocalPosition;
                }

                bone.MeshOffset = Vector3.zero;
                bone.LocalOffset = Vector3.zero;
            }

            localOffsets.Clear();
            weightsDirty = true;
        }

        private void BuildBones(
            Transform[] resolved,
            Dictionary<Transform, Matrix4x4> restInBody,
            Transform bodyTransform,
            Transform animatorPositionedBone)
        {
            var states = new Dictionary<Transform, BoneState>();
            for (int index = 0; index < resolved.Length; index++)
            {
                Transform bone = resolved[index];
                if (bone == null || states.ContainsKey(bone))
                {
                    continue;
                }

                var state = new BoneState(bone, bone == animatorPositionedBone);
                states.Add(bone, state);
                bones.Add(state);
            }

            foreach (BoneState state in bones)
            {
                Transform parent = state.Transform.parent;
                Matrix4x4 parentRest = parent == null
                    ? Matrix4x4.identity
                    : restInBody.TryGetValue(parent, out Matrix4x4 rest)
                        ? rest
                        : bodyTransform.worldToLocalMatrix * parent.localToWorldMatrix;
                state.ParentRestInverse = parentRest.inverse;

                // A bone that is not in the table moves rigidly with its nearest moved
                // ancestor, since only translations are ever applied.
                for (Transform ancestor = parent; ancestor != null; ancestor = ancestor.parent)
                {
                    if (states.TryGetValue(ancestor, out BoneState ancestorState))
                    {
                        state.OffsetParent = ancestorState;
                        break;
                    }
                }
            }

            stateByTransform = states;
        }

        private void BuildShapes(SkinnedMeshRenderer[] skinned, SkinnedMeshRenderer body)
        {
            var stateByName = new Dictionary<string, BoneState>(StringComparer.Ordinal);
            foreach (BoneState bone in bones)
            {
                stateByName.TryAdd(bone.Transform.name, bone);
            }

            foreach (CharacterJointOffsetTable.JointShape shape in Table.Shapes)
            {
                if (shape.BoneIndices.Count == 0 || !TryFindBlendShape(skinned, body, shape.Name, out SkinnedMeshRenderer owner, out int index))
                {
                    continue;
                }

                var states = new List<BoneState>();
                var offsets = new List<Vector3>();
                for (int entry = 0; entry < shape.BoneIndices.Count; entry++)
                {
                    if (!stateByName.TryGetValue(Table.Bones[shape.BoneIndices[entry]].Name, out BoneState state))
                    {
                        continue;
                    }

                    states.Add(state);
                    offsets.Add(Calibration.TransformVector(shape.Offsets[entry]));
                }

                if (states.Count > 0)
                {
                    shapeSources.Add(new ShapeSource(owner, index, owner.sharedMesh.blendShapeCount, states.ToArray(), offsets.ToArray()));
                }
            }
        }

        private void BuildRenderers(SkinnedMeshRenderer[] skinned, Dictionary<SkinnedMeshRenderer, Matrix4x4> rendererToBody)
        {
            foreach (SkinnedMeshRenderer renderer in skinned)
            {
                if (!rendererToBody.TryGetValue(renderer, out Matrix4x4 toBody) || renderer.sharedMesh == null)
                {
                    continue;
                }

                Transform[] rendererBones = renderer.bones;
                var boneStates = new BoneState[rendererBones.Length];
                bool movesAny = false;
                for (int index = 0; index < rendererBones.Length; index++)
                {
                    if (rendererBones[index] != null && stateByTransform.TryGetValue(rendererBones[index], out BoneState state))
                    {
                        boneStates[index] = state;
                        movesAny = true;
                    }
                }

                if (movesAny)
                {
                    renderers.Add(new RendererState(renderer, renderer.sharedMesh, toBody.inverse, boneStates));
                }
            }
        }

        private void UpdateBindPoses(RendererState renderer)
        {
            if (renderer.Renderer == null)
            {
                return;
            }

            // A baked or otherwise replaced mesh is left alone; joints stay where the
            // bake captured them until the character is unbaked.
            Mesh current = renderer.Renderer.sharedMesh;
            if (current != renderer.Original && !IsManaged(renderer, current))
            {
                return;
            }

            Matrix4x4[] bindposes = (Matrix4x4[])renderer.OriginalBindposes.Clone();
            bool changed = false;
            int count = Mathf.Min(bindposes.Length, renderer.Bones.Length);
            for (int index = 0; index < count; index++)
            {
                BoneState bone = renderer.Bones[index];
                if (bone == null || bone.MeshOffset.sqrMagnitude <= MinOffset * MinOffset)
                {
                    continue;
                }

                // The bone's rest matrix gains a translation, so its inverse, the bind
                // pose, first undoes that translation in this renderer's mesh space.
                Vector3 offset = renderer.BodyToRenderer.MultiplyVector(bone.MeshOffset);
                bindposes[index] = bindposes[index] * Matrix4x4.Translate(-offset);
                changed = true;
            }

            if (!changed)
            {
                if (current != renderer.Original)
                {
                    AssignMesh(renderer.Renderer, renderer.Original);
                }

                ReleasePrivate(renderer);
                ReleaseShared(renderer);
                return;
            }

            if (renderer.Private == null)
            {
                renderer.Private = CharacterBindPoseMeshCache.CreateCopy(renderer.Original, bindposes);
            }
            else
            {
                renderer.Private.bindposes = bindposes;
            }

            if (current != renderer.Private)
            {
                AssignMesh(renderer.Renderer, renderer.Private);
                ReleaseShared(renderer);
            }
        }

        private static bool IsManaged(RendererState renderer, Mesh mesh)
        {
            return mesh != null && (mesh == renderer.Private || mesh == renderer.Shared);
        }

        private static void ReleasePrivate(RendererState renderer)
        {
            CharacterBindPoseMeshCache.DestroyMesh(renderer.Private);
            renderer.Private = null;
        }

        private static void ReleaseShared(RendererState renderer)
        {
            if (renderer.Shared == null)
            {
                return;
            }

            if (renderer.Renderer != null && renderer.Renderer.TryGetComponent(out CharacterJointMeshHandle handle) &&
                handle.SharedMesh == renderer.Shared)
            {
                handle.Release();
            }
            else
            {
                CharacterBindPoseMeshCache.Release(renderer.Shared);
            }

            renderer.Shared = null;
        }

        private static CharacterJointMeshHandle Handle(SkinnedMeshRenderer renderer)
        {
            return renderer.TryGetComponent(out CharacterJointMeshHandle handle)
                ? handle
                : renderer.gameObject.AddComponent<CharacterJointMeshHandle>();
        }

        // Swapping meshes can reset blendshape weights, so they are carried across by name.
        private static void AssignMesh(SkinnedMeshRenderer renderer, Mesh mesh)
        {
            Mesh previous = renderer.sharedMesh;
            var weights = new Dictionary<string, float>(StringComparer.Ordinal);
            if (previous != null)
            {
                for (int index = 0; index < previous.blendShapeCount; index++)
                {
                    weights[previous.GetBlendShapeName(index)] = renderer.GetBlendShapeWeight(index);
                }
            }

            renderer.sharedMesh = mesh;
            for (int index = 0; index < mesh.blendShapeCount; index++)
            {
                if (weights.TryGetValue(mesh.GetBlendShapeName(index), out float weight))
                {
                    renderer.SetBlendShapeWeight(index, weight);
                }
            }
        }

        // Prefabs often rename the body object (SK_Human_F becomes M_Human_F), and a
        // hair groom can have more vertices than the body, so without a name match the
        // body is the renderer skinned to the most of the table's bones, then the larger.
        private static SkinnedMeshRenderer FindBody(SkinnedMeshRenderer[] skinned, CharacterJointOffsetTable table)
        {
            var tableBones = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterJointOffsetTable.JointBone bone in table.Bones)
            {
                tableBones.Add(bone.Name);
            }

            SkinnedMeshRenderer best = null;
            int bestBones = -1;
            foreach (SkinnedMeshRenderer renderer in skinned)
            {
                if (renderer.sharedMesh == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(table.Body) && string.Equals(renderer.name, table.Body, StringComparison.Ordinal))
                {
                    return renderer;
                }

                int bones = 0;
                foreach (Transform bone in renderer.bones)
                {
                    if (bone != null && tableBones.Contains(bone.name))
                    {
                        bones++;
                    }
                }

                if (best == null || bones > bestBones ||
                    (bones == bestBones && renderer.sharedMesh.vertexCount > best.sharedMesh.vertexCount))
                {
                    best = renderer;
                    bestBones = bones;
                }
            }

            return best;
        }

        private static bool TryFindBlendShape(
            SkinnedMeshRenderer[] skinned,
            SkinnedMeshRenderer body,
            string shapeName,
            out SkinnedMeshRenderer owner,
            out int index)
        {
            index = body.sharedMesh.GetBlendShapeIndex(shapeName);
            owner = body;
            if (index >= 0)
            {
                return true;
            }

            foreach (SkinnedMeshRenderer renderer in skinned)
            {
                if (renderer.sharedMesh == null)
                {
                    continue;
                }

                index = renderer.sharedMesh.GetBlendShapeIndex(shapeName);
                if (index >= 0)
                {
                    owner = renderer;
                    return true;
                }
            }

            owner = null;
            return false;
        }

        // Each bone's rest matrix in the renderer's mesh space, from its bind poses.
        private static Dictionary<Transform, Matrix4x4> RestMatrices(SkinnedMeshRenderer renderer)
        {
            var result = new Dictionary<Transform, Matrix4x4>();
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null)
            {
                return result;
            }

            Transform[] rendererBones = renderer.bones;
            Matrix4x4[] bindposes = mesh.bindposes;
            int count = Mathf.Min(rendererBones.Length, bindposes.Length);
            for (int index = 0; index < count; index++)
            {
                if (rendererBones[index] != null)
                {
                    result.TryAdd(rendererBones[index], bindposes[index].inverse);
                }
            }

            return result;
        }

        // Maps a renderer's mesh space into the body's through any bone both skin.
        private static bool TryRendererToBody(
            SkinnedMeshRenderer renderer,
            SkinnedMeshRenderer body,
            Dictionary<Transform, Matrix4x4> bodyRest,
            out Matrix4x4 toBody)
        {
            toBody = Matrix4x4.identity;
            if (renderer == body)
            {
                return true;
            }

            foreach (KeyValuePair<Transform, Matrix4x4> rest in RestMatrices(renderer))
            {
                if (bodyRest.TryGetValue(rest.Key, out Matrix4x4 bodyMatrix))
                {
                    toBody = bodyMatrix * rest.Value.inverse;
                    return true;
                }
            }

            return false;
        }

        private sealed class BoneState
        {
            public BoneState(Transform transform, bool animatorPositioned)
            {
                Transform = transform;
                AnimatorPositioned = animatorPositioned;
                BindLocalPosition = transform.localPosition;
            }

            public Transform Transform { get; }
            public bool AnimatorPositioned { get; }
            public Vector3 BindLocalPosition { get; }
            public Matrix4x4 ParentRestInverse { get; set; }
            public BoneState OffsetParent { get; set; }
            public Vector3 MeshOffset { get; set; }
            public Vector3 LocalOffset { get; set; }
        }

        private sealed class ShapeSource
        {
            public ShapeSource(
                SkinnedMeshRenderer renderer,
                int blendShapeIndex,
                int blendShapeCount,
                BoneState[] bones,
                Vector3[] offsets)
            {
                Renderer = renderer;
                BlendShapeIndex = blendShapeIndex;
                BlendShapeCount = blendShapeCount;
                Bones = bones;
                Offsets = offsets;
            }

            public SkinnedMeshRenderer Renderer { get; }
            public int BlendShapeIndex { get; }
            public int BlendShapeCount { get; }
            public BoneState[] Bones { get; }
            public Vector3[] Offsets { get; }
        }

        private sealed class RendererState
        {
            public RendererState(SkinnedMeshRenderer renderer, Mesh original, Matrix4x4 bodyToRenderer, BoneState[] bones)
            {
                Renderer = renderer;
                Original = original;
                OriginalBindposes = original.bindposes;
                BodyToRenderer = bodyToRenderer;
                Bones = bones;
            }

            public SkinnedMeshRenderer Renderer { get; }
            public Mesh Original { get; }
            public Matrix4x4[] OriginalBindposes { get; }
            public Matrix4x4 BodyToRenderer { get; }
            public BoneState[] Bones { get; }
            public Mesh Private { get; set; }
            public Mesh Shared { get; set; }
        }
    }
}
