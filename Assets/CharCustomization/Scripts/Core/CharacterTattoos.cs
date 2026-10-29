using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Sol.CharacterCustomization
{
    // Composites the active character's tattoos into one texture in its skin UV space,
    // which CharacterProfile gives to the Sol/Character Skin materials. Tattoos come
    // from two places, layered in this order:
    // - Placements: catalog designs in the skin mesh's own space, saved in recipes.
    // - Projectors: CharacterTattooProjectors under bones of the active character, for
    //   placing tattoos by hand in the Scene view. Their poses are taken into the skin
    //   mesh's space through their bone; TryCapture turns one into a placement.
    // Skin mesh space does not move with morphs, joint offsets or animation, so tattoos
    // stay on the same patch of skin through all of them, and the layer is rebuilt only
    // when a tattoo, the active character or its skin mesh changes.
    public sealed class CharacterTattoos : MonoBehaviour
    {
        public const string BakeShaderName = "Hidden/Sol/Tattoo Bake";

        private const int IslandsPass = 0;
        private const int StampPass = 1;
        private const int DilatePass = 2;
        private const int OutlinePass = 3;
        // The selection frame's width on the skin, in metres.
        private const float FrameWidth = 0.006f;
        // Even, so the last dilation step writes back into the layer.
        private const int DilationSteps = 8;
        private const int PreviewDilationSteps = 2;
        private const int MinimumResolution = 256;
        private const float MatrixTolerance = 1e-6f;
        // Bone segments shorter than this (fingers, face) are not limbs to wrap around.
        private const float MinimumLimbSegment = 0.08f;
        // Skin further than this from the nearest limb bone (the torso) projects flat:
        // it is broad enough not to need wrapping.
        public const float MaximumCurveRadius = 0.09f;
        private const float MinimumCurveRadius = 0.02f;
        // A limb has skin all the way round its bone, no further than a thigh's, except
        // perhaps in one direction, as into the armpit at the top of the arm. The torso
        // is wider than that in several: the back is not wrapped round the spine, nor
        // the upper back round the shoulder bones that cross it. The bone need not be
        // central: the shin bone lies just under the front of the leg.
        private const float MaximumLimbReach = 0.1f;
        private const int MaximumOpenDirections = 1;
        // The nearest skin is found in this many directions round the bone, in a slice
        // this far each way along it.
        private const int LimbDirections = 8;
        private const float LimbSlice = 0.02f;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MeshToDecalId = Shader.PropertyToID("_TattooMeshToDecal");
        private static readonly int DirectionId = Shader.PropertyToID("_TattooDirection");
        private static readonly int InkId = Shader.PropertyToID("_TattooInk");
        private static readonly int DesignId = Shader.PropertyToID("_TattooDesign");
        private static readonly int SourceId = Shader.PropertyToID("_TattooSource");
        private static readonly int IslandsId = Shader.PropertyToID("_TattooIslands");
        private static readonly int SourceSizeId = Shader.PropertyToID("_TattooSourceSize");
        private static readonly int WrapId = Shader.PropertyToID("_TattooWrap");
        private static readonly int WrapAnchorId = Shader.PropertyToID("_TattooWrapAnchor");
        private static readonly int WrapOutwardId = Shader.PropertyToID("_TattooWrapOutward");
        private static readonly int FrameId = Shader.PropertyToID("_TattooFrame");

        private static Texture2D emptyLayer;

        [SerializeField] private CharacterMorphController controller;
        [Tooltip("Designs that placements refer to by ID.")]
        [SerializeField] private CharacterTattooCatalog catalog;
        [Tooltip("Saved tattoos in skin mesh space, layered in order. Recipes save and restore these.")]
        [SerializeField] private List<CharacterTattooPlacement> placements = new();
        [Tooltip("Hidden/Sol/Tattoo Bake. Assign it so builds include the shader.")]
        [SerializeField] private Shader bakeShader;
        [Tooltip("Largest layer size. The layer matches the skin's detail albedo up to this size.")]
        [SerializeField] private int maxResolution = 4096;
        [Tooltip("Checks the projectors every frame and rebuilds when one changes. Turn off for characters whose tattoos are fixed, and call Rebuild after changing them.")]
        [SerializeField] private bool trackChanges = true;

        private readonly List<CharacterTattooProjector> projectors = new();
        private readonly List<SkinnedMeshRenderer> renderers = new();
        private readonly List<Matrix4x4> bindposes = new();
        private readonly List<Vector3> limbVertices = new();
        private readonly float[] limbReach = new float[LimbDirections];
        private Mesh limbMesh;
        private readonly List<int> skinSubmeshes = new();
        private readonly Dictionary<Transform, int> boneIndices = new();
        private readonly List<Stamp> stamps = new();
        private readonly List<Stamp> builtStamps = new();
        private readonly HashSet<string> warnedDesigns = new(StringComparer.Ordinal);
        private RenderTexture layer;
        // The highlighted placement's outline; null while none is highlighted.
        private RenderTexture highlight;
        private int highlighted = -1;
        private bool highlightDirty;
        // Which texels are inside the skin's UV islands; redrawn only for a new mesh or size.
        private RenderTexture islands;
        private Mesh islandsMesh;
        private bool previewing;
        private Material bakeMaterial;
        private Transform skinRoot;
        private SkinnedMeshRenderer skin;
        private Mesh builtMesh;
        private bool dirty = true;
        private bool warnedUnbound;
        private bool warnedShader;

        // Raised when the layer or highlight texture is created or released, not on every
        // rebuild: rebuilds draw into the same textures.
        public event Action LayerChanged;

        // Raised when the placements change.
        public event Action Changed;

        // The composited layer, or null while the active character has no tattoos.
        public Texture Layer => layer;
        // The highlighted tattoo's outline for the skin to show, or null.
        public Texture Highlight => highlight;
        // Tattoos in the last build, placements and projectors together.
        public int TattooCount => builtStamps.Count;
        public CharacterTattooCatalog Catalog => catalog;
        public IReadOnlyList<CharacterTattooPlacement> Placements => placements;

        // The active character's skin, and the mesh tattoos project onto: its shape
        // before any NPC bake folded blendshapes into it.
        public SkinnedMeshRenderer Skin
        {
            get
            {
                ResolveSkin();
                return skin;
            }
        }

        // The mesh placements are in and tattoos project onto.
        public Mesh PlacementMesh
        {
            get
            {
                ResolveSkin();
                return SkinMesh;
            }
        }

        // While true, as during a drag, rebuilds fill seams less thoroughly so they stay
        // quick; turning it off rebuilds in full.
        public bool Previewing
        {
            get => previewing;
            set
            {
                if (previewing != value)
                {
                    previewing = value;
                    dirty = true;
                }
            }
        }

        // The placement outlined on the skin, as while it is being edited; -1 for none.
        // Only for showing: it is not saved, and copies of the character do not take it.
        public int Highlighted
        {
            get => highlighted;
            set
            {
                value = Mathf.Max(-1, value);
                if (highlighted != value)
                {
                    highlighted = value;
                    highlightDirty = true;
                }
            }
        }

        private Mesh SkinMesh => skin == null ? null : controller != null ? controller.GetUnbakedMesh(skin) : skin.sharedMesh;

        // Transparent, for skin without tattoos.
        public static Texture2D EmptyLayer
        {
            get
            {
                if (emptyLayer == null)
                {
                    emptyLayer = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                    {
                        name = "Sol Empty Tattoo Layer",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    emptyLayer.SetPixel(0, 0, Color.clear);
                    emptyLayer.Apply(false, true);
                }

                return emptyLayer;
            }
        }

        private void Awake()
        {
            if (controller == null)
            {
                controller = GetComponent<CharacterMorphController>();
            }
        }

        private void OnEnable()
        {
            dirty = true;
        }

        private void LateUpdate()
        {
            if (dirty || trackChanges)
            {
                Refresh();
            }
        }

        private void OnDisable()
        {
            ReleaseLayer();
            builtStamps.Clear();
            builtMesh = null;
        }

        private void OnDestroy()
        {
            ReleaseLayer();
            if (bakeMaterial != null)
            {
                Destroy(bakeMaterial);
            }
        }

        private void OnValidate()
        {
            dirty = true;
        }

        // Rebuilds now, for callers that changed projectors with change tracking off.
        // Placement changes always rebuild on the next frame.
        public void Rebuild()
        {
            dirty = true;
            Refresh();
        }

        // Takes another character's catalog and bake settings, for a copy of that character
        // (CharacterAppearanceTransfer), driven by its own controller.
        public void CopySettings(CharacterTattoos source, CharacterMorphController characterController)
        {
            if (source != null)
            {
                catalog = source.catalog;
                bakeShader = source.bakeShader;
                maxResolution = source.maxResolution;
                trackChanges = source.trackChanges;
            }

            controller = characterController;
            dirty = true;
        }

        public void SetPlacements(IReadOnlyList<CharacterTattooPlacement> values)
        {
            placements.Clear();
            if (values != null)
            {
                for (int index = 0; index < values.Count; index++)
                {
                    placements.Add(values[index]);
                }
            }

            PlacementsChanged();
        }

        // Adds a tattoo on top of the others and returns its index.
        public int AddPlacement(CharacterTattooPlacement placement)
        {
            placements.Add(placement);
            PlacementsChanged();
            return placements.Count - 1;
        }

        public void SetPlacement(int index, CharacterTattooPlacement placement)
        {
            if (index < 0 || index >= placements.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            placements[index] = placement;
            PlacementsChanged();
        }

        public void RemovePlacement(int index)
        {
            if (index < 0 || index >= placements.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            placements.RemoveAt(index);
            PlacementsChanged();
        }

        public void ClearPlacements()
        {
            if (placements.Count == 0)
            {
                return;
            }

            placements.Clear();
            PlacementsChanged();
        }

        // The placement a projector under the active character's skeleton describes, so a
        // tattoo positioned by hand can be saved. Its design must be in the catalog.
        public bool TryCapture(CharacterTattooProjector projector, out CharacterTattooPlacement placement, out string error)
        {
            placement = default;
            ResolveSkin();
            Mesh mesh = SkinMesh;
            if (projector == null || mesh == null)
            {
                error = "There is no projector, or no active character skin to place it on.";
                return false;
            }

            if (catalog == null || !catalog.TryFind(projector.Design, out CharacterTattooDesign design))
            {
                error = $"'{projector.name}' uses a design that is not in the tattoo catalog.";
                return false;
            }

            mesh.GetBindposes(bindposes);
            if (!TryFindBone(projector.transform, out Matrix4x4 boneFromProjector, out int boneIndex) || boneIndex >= bindposes.Count)
            {
                error = $"'{projector.name}' is not under a bone of the active character's skin.";
                return false;
            }

            placement = CharacterTattooPlacement.FromMeshSpace(
                design.Id, bindposes[boneIndex].inverse * boneFromProjector, projector.Ink, projector.Opacity);
            error = null;
            return true;
        }

        // The limb a point of skin (in skin mesh space) wraps around: the axis of the
        // nearest bone segment and the distance to it. False on the torso and wherever no
        // limb bone is near enough, where tattoos project flat.
        public bool TryGetCurve(Vector3 point, out Vector3 axis, out float radius)
        {
            axis = Vector3.zero;
            radius = 0f;
            ResolveSkin();
            Mesh mesh = SkinMesh;
            if (mesh == null)
            {
                return false;
            }

            mesh.GetBindposes(bindposes);
            Transform[] bones = skin.bones;
            float nearest = float.MaxValue;
            Vector3 centre = Vector3.zero;
            for (int index = 0; index < bones.Length && index < bindposes.Count; index++)
            {
                if (bones[index] == null || !TryFindParentBone(bones[index], out int parent) || parent >= bindposes.Count)
                {
                    continue;
                }

                Vector3 head = bindposes[index].inverse.MultiplyPoint3x4(Vector3.zero);
                Vector3 parentHead = bindposes[parent].inverse.MultiplyPoint3x4(Vector3.zero);
                Vector3 segment = head - parentHead;
                float length = segment.magnitude;
                if (length < MinimumLimbSegment)
                {
                    continue;
                }

                float along = Mathf.Clamp(Vector3.Dot(point - parentHead, segment) / (length * length), 0f, 1f);
                Vector3 closest = parentHead + segment * along;
                float distance = Vector3.Distance(point, closest);
                if (distance < nearest)
                {
                    nearest = distance;
                    centre = closest;
                    axis = segment / length;
                }
            }

            if (nearest > MaximumCurveRadius || nearest < MinimumCurveRadius || !IsLimb(mesh, centre, axis, point))
            {
                axis = Vector3.zero;
                return false;
            }

            radius = nearest;
            return true;
        }

        // Whether the skin round a bone at this point is limb-shaped: close to the bone
        // all the way round it.
        private bool IsLimb(Mesh mesh, Vector3 centre, Vector3 axis, Vector3 point)
        {
            if (limbMesh != mesh)
            {
                limbMesh = mesh;
                mesh.GetVertices(limbVertices);
            }

            Vector3 outward = Vector3.ProjectOnPlane(point - centre, axis).normalized;
            Vector3 around = Vector3.Cross(axis, outward);
            for (int direction = 0; direction < LimbDirections; direction++)
            {
                limbReach[direction] = float.MaxValue;
            }

            foreach (Vector3 vertex in limbVertices)
            {
                Vector3 offset = vertex - centre;
                float along = Vector3.Dot(offset, axis);
                if (along > LimbSlice || along < -LimbSlice)
                {
                    continue;
                }

                Vector3 radial = offset - axis * along;
                float distance = radial.magnitude;
                float turn = Mathf.Atan2(Vector3.Dot(radial, around), Vector3.Dot(radial, outward));
                int direction = Mathf.Clamp(Mathf.FloorToInt((turn / Mathf.PI + 1f) * 0.5f * LimbDirections), 0, LimbDirections - 1);
                if (distance < limbReach[direction])
                {
                    limbReach[direction] = distance;
                }
            }

            int open = 0;
            foreach (float reach in limbReach)
            {
                if (reach > MaximumLimbReach)
                {
                    open++;
                }
            }

            return open <= MaximumOpenDirections;
        }

        private bool TryFindParentBone(Transform bone, out int parent)
        {
            for (Transform current = bone.parent; current != null && current != skinRoot; current = current.parent)
            {
                if (boneIndices.TryGetValue(current, out parent))
                {
                    return true;
                }
            }

            parent = -1;
            return false;
        }

        private void PlacementsChanged()
        {
            dirty = true;
            Changed?.Invoke();
        }

        private void Refresh()
        {
            ResolveSkin();
            CollectStamps();
            Mesh mesh = SkinMesh;
            if (dirty || mesh != builtMesh || !SameStamps(stamps, builtStamps))
            {
                dirty = false;
                highlightDirty = true;
                builtMesh = mesh;
                builtStamps.Clear();
                builtStamps.AddRange(stamps);
                if (mesh == null || stamps.Count == 0)
                {
                    ReleaseLayer();
                }
                else
                {
                    Bake(mesh);
                }
            }

            if (highlightDirty)
            {
                highlightDirty = false;
                BakeHighlight(mesh);
            }
        }

        // The skin renderer is the largest renderer under the active character with a
        // Sol/Character Skin material; it is found again only when the character
        // changes.
        private void ResolveSkin()
        {
            Transform root = controller != null ? controller.ActiveCharacterRoot : transform;
            if (root == skinRoot && skin != null)
            {
                return;
            }

            skinRoot = root;
            skin = null;
            skinSubmeshes.Clear();
            boneIndices.Clear();
            if (root == null)
            {
                return;
            }

            root.GetComponentsInChildren(true, renderers);
            foreach (SkinnedMeshRenderer candidate in renderers)
            {
                if (candidate.sharedMesh != null
                    && HasSkinMaterial(candidate)
                    && (skin == null || candidate.sharedMesh.vertexCount > skin.sharedMesh.vertexCount))
                {
                    skin = candidate;
                }
            }

            renderers.Clear();
            if (skin == null)
            {
                return;
            }

            Material[] materials = skin.sharedMaterials;
            for (int index = 0; index < materials.Length && index < skin.sharedMesh.subMeshCount; index++)
            {
                if (SolSkinShaderIds.IsSkinMaterial(materials[index]))
                {
                    skinSubmeshes.Add(index);
                }
            }

            Transform[] bones = skin.bones;
            for (int index = 0; index < bones.Length; index++)
            {
                if (bones[index] != null)
                {
                    boneIndices[bones[index]] = index;
                }
            }
        }

        private static bool HasSkinMaterial(Renderer candidate)
        {
            foreach (Material material in candidate.sharedMaterials)
            {
                if (SolSkinShaderIds.IsSkinMaterial(material))
                {
                    return true;
                }
            }

            return false;
        }

        private void CollectStamps()
        {
            stamps.Clear();
            Mesh mesh = SkinMesh;
            if (mesh == null || skinRoot == null)
            {
                return;
            }

            for (int placementIndex = 0; placementIndex < placements.Count; placementIndex++)
            {
                // Saved tattoos whose box stops short of skin rising round them reach it now.
                CharacterTattooPlacement placement = CharacterTattooPlacer.Refit(placements[placementIndex]);
                if (catalog == null || !catalog.TryGet(placement.DesignId, out CharacterTattooDesign design) || design.Texture == null)
                {
                    if (warnedDesigns.Add(placement.DesignId))
                    {
                        Debug.LogWarning($"Tattoo design '{placement.DesignId}' is not in the tattoo catalog, so that tattoo is skipped.", this);
                    }

                    continue;
                }

                Vector3 size = placement.Size;
                if (Mathf.Abs(size.x) < 1e-6f || size.y < 1e-6f || size.z < 1e-6f)
                {
                    if (warnedDesigns.Add(placement.DesignId + " (empty)"))
                    {
                        Debug.LogWarning($"A '{placement.DesignId}' tattoo has no width, height or depth, so it is skipped.", this);
                    }

                    continue;
                }

                Matrix4x4 meshFromBox = placement.MeshFromBox;
                Vector4 wrap = Vector4.zero;
                Vector3 anchor = Vector3.zero;
                Vector3 outward = Vector3.zero;
                if (placement.IsCurved)
                {
                    // The cylinder touches the skin at the tattoo's point, facing out
                    // across its axis there.
                    CharacterTattooPlacer.Describe(placement, Vector3.up, out anchor, out Vector3 normal, out _, out _, out _);
                    outward = Vector3.ProjectOnPlane(normal, placement.CurveAxis);
                    if (outward.sqrMagnitude > 1e-6f)
                    {
                        outward.Normalize();
                        Vector3 axis = placement.CurveAxis;
                        wrap = new Vector4(axis.x, axis.y, axis.z, placement.CurveRadius);
                    }
                }

                stamps.Add(new Stamp(
                    meshFromBox.inverse,
                    meshFromBox.MultiplyVector(Vector3.forward).normalized,
                    design.Texture,
                    placement.Ink,
                    placement.Opacity,
                    wrap,
                    anchor,
                    outward,
                    placementIndex));
            }

            skinRoot.GetComponentsInChildren(false, projectors);
            if (projectors.Count == 0)
            {
                return;
            }

            mesh.GetBindposes(bindposes);
            foreach (CharacterTattooProjector projector in projectors)
            {
                if (!projector.enabled || projector.Design == null)
                {
                    continue;
                }

                if (!TryFindBone(projector.transform, out Matrix4x4 boneFromProjector, out int boneIndex) || boneIndex >= bindposes.Count)
                {
                    if (!warnedUnbound)
                    {
                        warnedUnbound = true;
                        Debug.LogWarning($"Tattoo projector '{projector.name}' is not under a bone of the skin it marks, so it is skipped.", projector);
                    }

                    continue;
                }

                // The bind pose takes mesh space to the bone's space at bind time, so
                // its inverse takes the projector's pose relative to the bone back to
                // the bind-pose mesh, whatever pose the character is in now.
                Matrix4x4 meshFromProjector = bindposes[boneIndex].inverse * boneFromProjector;
                stamps.Add(new Stamp(
                    meshFromProjector.inverse,
                    meshFromProjector.MultiplyVector(Vector3.forward).normalized,
                    projector.Design,
                    projector.Ink,
                    projector.Opacity));
            }

            projectors.Clear();
        }

        // Built from local transforms rather than world matrices, so the pose stays
        // exactly the same while the bone animates and does not trigger rebuilds.
        private bool TryFindBone(Transform projector, out Matrix4x4 boneFromProjector, out int boneIndex)
        {
            boneFromProjector = Matrix4x4.TRS(projector.localPosition, projector.localRotation, projector.localScale);
            for (Transform bone = projector.parent; bone != null && bone != skinRoot.parent; bone = bone.parent)
            {
                if (boneIndices.TryGetValue(bone, out boneIndex))
                {
                    return true;
                }

                boneFromProjector = Matrix4x4.TRS(bone.localPosition, bone.localRotation, bone.localScale) * boneFromProjector;
            }

            boneIndex = -1;
            return false;
        }

        private void Bake(Mesh mesh)
        {
            Material material = ResolveBakeMaterial();
            if (material == null || skinSubmeshes.Count == 0)
            {
                return;
            }

            int size = ResolveResolution();
            bool created = EnsureLayer(size);
            RenderTexture scratch = RenderTexture.GetTemporary(new RenderTextureDescriptor(size, size, layer.graphicsFormat, 0));
            var commands = new CommandBuffer { name = "Sol Tattoo Bake" };
            try
            {
                if (EnsureIslands(size, mesh))
                {
                    commands.SetRenderTarget(islands);
                    commands.ClearRenderTarget(false, true, Color.clear);
                    foreach (int submesh in skinSubmeshes)
                    {
                        commands.DrawMesh(mesh, Matrix4x4.identity, material, submesh, IslandsPass);
                    }
                }

                commands.SetRenderTarget(layer, 0);
                commands.ClearRenderTarget(false, true, Color.clear);
                foreach (Stamp stamp in builtStamps)
                {
                    MaterialPropertyBlock properties = StampProperties(stamp);
                    foreach (int submesh in skinSubmeshes)
                    {
                        commands.DrawMesh(mesh, Matrix4x4.identity, material, submesh, StampPass, properties);
                    }
                }

                RenderTexture source = layer;
                RenderTexture target = scratch;
                int steps = previewing ? PreviewDilationSteps : DilationSteps;
                for (int step = 0; step < steps; step++)
                {
                    var properties = new MaterialPropertyBlock();
                    properties.SetTexture(SourceId, source);
                    properties.SetTexture(IslandsId, islands);
                    properties.SetVector(SourceSizeId, new Vector4(size, size, 0f, 0f));
                    commands.SetRenderTarget(target, 0);
                    commands.DrawProcedural(Matrix4x4.identity, material, DilatePass, MeshTopology.Triangles, 3, 1, properties);
                    (source, target) = (target, source);
                }

                commands.GenerateMips(layer);
                Graphics.ExecuteCommandBuffer(commands);
            }
            finally
            {
                commands.Release();
                RenderTexture.ReleaseTemporary(scratch);
            }

            if (created)
            {
                LayerChanged?.Invoke();
            }
        }

        // Command buffers keep the block they are given, so each draw has its own.
        private static MaterialPropertyBlock StampProperties(Stamp stamp)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetMatrix(MeshToDecalId, stamp.MeshToDecal);
            properties.SetVector(DirectionId, stamp.Direction);
            properties.SetTexture(DesignId, stamp.Design);
            // Like Material.SetColor, this converts sRGB to linear; alpha is kept.
            properties.SetColor(InkId, new Color(stamp.Ink.r, stamp.Ink.g, stamp.Ink.b, stamp.Opacity));
            properties.SetVector(WrapId, stamp.Wrap);
            properties.SetVector(WrapAnchorId, stamp.WrapAnchor);
            properties.SetVector(WrapOutwardId, stamp.WrapOutward);
            return properties;
        }

        // Draws the highlighted placement's outline at half the layer's size; a frame
        // does not need the ink's detail.
        private void BakeHighlight(Mesh mesh)
        {
            int stampIndex = highlighted >= 0 ? builtStamps.FindIndex(stamp => stamp.Placement == highlighted) : -1;
            Material material = stampIndex >= 0 && layer != null && mesh != null ? ResolveBakeMaterial() : null;
            if (material == null)
            {
                ReleaseHighlight();
                return;
            }

            bool created = EnsureHighlight(Mathf.Max(MinimumResolution, layer.width / 2));
            Stamp stamp = builtStamps[stampIndex];
            MaterialPropertyBlock properties = StampProperties(stamp);
            // The box's rows scale mesh space to design widths and heights.
            Matrix4x4 meshToDecal = stamp.MeshToDecal;
            properties.SetVector(FrameId, new Vector4(
                FrameWidth * ((Vector3)meshToDecal.GetRow(0)).magnitude,
                FrameWidth * ((Vector3)meshToDecal.GetRow(1)).magnitude));
            var commands = new CommandBuffer { name = "Sol Tattoo Highlight" };
            try
            {
                commands.SetRenderTarget(highlight, 0);
                commands.ClearRenderTarget(false, true, Color.clear);
                foreach (int submesh in skinSubmeshes)
                {
                    commands.DrawMesh(mesh, Matrix4x4.identity, material, submesh, OutlinePass, properties);
                }

                Graphics.ExecuteCommandBuffer(commands);
            }
            finally
            {
                commands.Release();
            }

            if (created)
            {
                LayerChanged?.Invoke();
            }
        }

        private Material ResolveBakeMaterial()
        {
            if (bakeMaterial != null)
            {
                return bakeMaterial;
            }

            Shader shader = bakeShader != null ? bakeShader : Shader.Find(BakeShaderName);
            if (shader == null || !shader.isSupported)
            {
                if (!warnedShader)
                {
                    warnedShader = true;
                    Debug.LogError($"Cannot composite tattoos: the '{BakeShaderName}' shader is missing or unsupported.", this);
                }

                return null;
            }

            bakeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return bakeMaterial;
        }

        // The skin's detail albedo sets the texel density tattoos need to match it.
        private int ResolveResolution()
        {
            Material material = skin.sharedMaterials[skinSubmeshes[0]];
            Texture detail = material != null && material.HasProperty(BaseMapId) ? material.GetTexture(BaseMapId) : null;
            int size = detail != null ? Mathf.Max(detail.width, detail.height) : maxResolution;
            return Mathf.Clamp(size, MinimumResolution, Mathf.Max(MinimumResolution, maxResolution));
        }

        private bool EnsureLayer(int size)
        {
            if (layer != null && layer.width == size)
            {
                return false;
            }

            ReleaseLayer(notify: false);
            // sRGB storage keeps dark ink precise at 8 bits; blending still runs linear.
            var descriptor = new RenderTextureDescriptor(size, size, GraphicsFormat.R8G8B8A8_SRGB, 0)
            {
                useMipMap = true,
                autoGenerateMips = false
            };
            layer = new RenderTexture(descriptor)
            {
                name = $"Tattoo Layer ({name})",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 4,
                hideFlags = HideFlags.DontSave
            };
            layer.Create();
            return true;
        }

        private bool EnsureHighlight(int size)
        {
            if (highlight != null && highlight.width == size)
            {
                return false;
            }

            ReleaseHighlight(notify: false);
            highlight = new RenderTexture(new RenderTextureDescriptor(size, size, GraphicsFormat.R8_UNorm, 0))
            {
                name = $"Tattoo Highlight ({name})",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            highlight.Create();
            return true;
        }

        // True when the island mask has to be drawn: a new mesh or size.
        private bool EnsureIslands(int size, Mesh mesh)
        {
            if (islands != null && islands.width == size && islandsMesh == mesh)
            {
                return false;
            }

            ReleaseIslands();
            islands = new RenderTexture(new RenderTextureDescriptor(size, size, GraphicsFormat.R8_UNorm, 0))
            {
                name = $"Tattoo Islands ({name})",
                hideFlags = HideFlags.DontSave
            };
            islands.Create();
            islandsMesh = mesh;
            return true;
        }

        private void ReleaseIslands()
        {
            if (islands == null)
            {
                return;
            }

            Release(islands);
            islands = null;
            islandsMesh = null;
        }

        private void ReleaseLayer(bool notify = true)
        {
            ReleaseIslands();
            ReleaseHighlight(notify);
            if (layer == null)
            {
                return;
            }

            Release(layer);
            layer = null;
            if (notify)
            {
                LayerChanged?.Invoke();
            }
        }

        private void ReleaseHighlight(bool notify = true)
        {
            if (highlight == null)
            {
                return;
            }

            Release(highlight);
            highlight = null;
            if (notify)
            {
                LayerChanged?.Invoke();
            }
        }

        private static void Release(RenderTexture texture)
        {
            texture.Release();
            if (Application.isPlaying)
            {
                Destroy(texture);
            }
            else
            {
                DestroyImmediate(texture);
            }
        }

        private static bool SameStamps(List<Stamp> first, List<Stamp> second)
        {
            if (first.Count != second.Count)
            {
                return false;
            }

            for (int index = 0; index < first.Count; index++)
            {
                if (!first[index].Matches(second[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private readonly struct Stamp
        {
            public Stamp(
                Matrix4x4 meshToDecal,
                Vector3 direction,
                Texture2D design,
                Color ink,
                float opacity,
                Vector4 wrap = default,
                Vector3 wrapAnchor = default,
                Vector3 wrapOutward = default,
                int placement = -1)
            {
                MeshToDecal = meshToDecal;
                Direction = direction;
                Design = design;
                Ink = ink;
                Opacity = opacity;
                Wrap = wrap;
                WrapAnchor = wrapAnchor;
                WrapOutward = wrapOutward;
                Placement = placement;
            }

            public Matrix4x4 MeshToDecal { get; }
            public Vector3 Direction { get; }
            public Texture2D Design { get; }
            public Color Ink { get; }
            public float Opacity { get; }
            // Axis and radius (w) of the cylinder a curved tattoo wraps around; w is 0 for flat.
            public Vector4 Wrap { get; }
            public Vector3 WrapAnchor { get; }
            public Vector3 WrapOutward { get; }
            // The placement it was made from; -1 for a projector.
            public int Placement { get; }

            public bool Matches(Stamp other)
            {
                if (Design != other.Design || Ink != other.Ink || !Mathf.Approximately(Opacity, other.Opacity)
                    || Wrap != other.Wrap || WrapAnchor != other.WrapAnchor || WrapOutward != other.WrapOutward
                    || Placement != other.Placement)
                {
                    return false;
                }

                for (int element = 0; element < 16; element++)
                {
                    if (Mathf.Abs(MeshToDecal[element] - other.MeshToDecal[element]) > MatrixTolerance)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
