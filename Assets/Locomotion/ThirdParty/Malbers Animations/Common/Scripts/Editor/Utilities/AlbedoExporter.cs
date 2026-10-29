// Last Change: 9/23/2026 10:31am
// ============================================================
//  AlbedoExporter.cs
//  Malbers Animations — Unity 6 / C# 9
//
//  Bakes the EXACT Base Color fragment output of any material
//  by invoking its Meta pass — the same mechanism Unity's
//  lightmapper uses to extract albedo — so the result is
//  pixel-perfect regardless of what shader calculations feed
//  into the Base Color node.
//
//  Emission is baked the same way (Meta pass, emission branch).
//  Metallic and Smoothness are not written by any Meta pass, so
//  they are rebuilt from the material's mask / metallic-gloss
//  maps and sliders (Built-in Standard, URP Lit, HDRP Lit, and
//  a name-based guess for Shader Graph materials). Smoothness
//  can be inverted on export to produce a Roughness map.
//
//  Usage:
//    • Tools / Malbers Animations / Editor / Albedo Exporter
//    • Right-click a Material asset → Malbers Animations / Export Albedo PNG
// ============================================================

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MalbersAnimations
{
    public class AlbedoExporter : EditorWindow
    {
        // ── Enums ─────────────────────────────────────────────────────────────────

        /// <summary>Square output size of every exported map.</summary>
        public enum ExportResolution
        {
            _64 = 64,
            _128 = 128,
            _256 = 256,
            _512 = 512,
            _1024 = 1024,
            _2048 = 2048,
        }

        /// <summary>Material maps that can be exported (flags, several per export).</summary>
        [System.Flags]
        public enum ExportMaps
        {
            BaseColor = 1,
            Metallic = 2,
            Smoothness = 4,
            Emission = 8,
        }

        /// <summary>A single map, used for the preview and for each bake call.</summary>
        public enum MapType { BaseColor, Metallic, Smoothness, Emission }

        private static readonly MapType[] AllMaps =
            { MapType.BaseColor, MapType.Metallic, MapType.Smoothness, MapType.Emission };

        private static readonly string[] BaseColorNames =
            { "_BaseMap", "_BaseColorMap", "_MainTex", "_Albedo", "_AlbedoMap" };

        // ── State ─────────────────────────────────────────────────────────────────

        private Material _material;
        private ExportResolution _resolution = ExportResolution._512;
        private ExportMaps _maps = ExportMaps.BaseColor;
        private MapType _previewMap = MapType.BaseColor;
        private bool _invertSmoothness;
        private bool _normalizeEmission = true;
        private Texture2D _preview;
        private string _statusMessage;
        private MessageType _statusType;

        // MWC: batch export state
        private readonly List<Material> _batchList = new();
        private Vector2 _batchScroll;

        // ── Menu items ────────────────────────────────────────────────────────────

        /// <summary>Opens the exporter window.</summary>
        [MenuItem("Tools/Malbers Animations/Editor/Albedo Exporter")]
        public static void Open()
        {
            var w = GetWindow<AlbedoExporter>("Albedo Exporter");
            w.minSize = new Vector2(320f, 500f);
            w.Show();
        }

        /// <summary>Enables the asset context menu only when a Material is selected.</summary>
        [MenuItem("Assets/Malbers Animations/Export Albedo PNG", validate = true)]
        static bool ValidateAssetMenu() => Selection.activeObject is Material;

        /// <summary>Opens the exporter window with the selected Material assigned.</summary>
        [MenuItem("Assets/Malbers Animations/Export Albedo PNG")]
        static void OpenFromAsset()
        {
            var w = GetWindow<AlbedoExporter>("Albedo Exporter");
            w.minSize = new Vector2(320f, 500f);
            w.Assign(Selection.activeObject as Material);
            w.Show();
        }

        // ── EditorWindow GUI ──────────────────────────────────────────────────────

        /// <summary>Draws the whole window.</summary>
        private void OnGUI()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("  Material Map PNG Exporter", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("  Base Color / Emission via Meta pass, Metallic / Smoothness from maps.",
                EditorStyles.miniLabel);
            Separator();

            DrawMapOptions();

            EditorGUILayout.Space(6f);
            Separator();

            DrawSingleSection();

            EditorGUILayout.Space(6f);
            Separator();

            DrawBatchSection();
        }

        // ── Map options ───────────────────────────────────────────────────────────

        /// <summary>Draws the map selection shared by single and batch export.</summary>
        private void DrawMapOptions()
        {
            EditorGUILayout.LabelField("Maps", EditorStyles.boldLabel);

            _maps = (ExportMaps)EditorGUILayout.EnumFlagsField(
                new GUIContent("Export", "Maps written for every material. One PNG per map."), _maps);

            EditorGUI.BeginChangeCheck();

            using (new EditorGUI.DisabledScope((_maps & ExportMaps.Smoothness) == 0 && _previewMap != MapType.Smoothness))
            {
                _invertSmoothness = EditorGUILayout.Toggle(
                    new GUIContent("Invert to Roughness",
                        "Writes 1 - Smoothness and names the file _Roughness instead of _Smoothness."),
                    _invertSmoothness);
            }

            using (new EditorGUI.DisabledScope((_maps & ExportMaps.Emission) == 0 && _previewMap != MapType.Emission))
            {
                _normalizeEmission = EditorGUILayout.Toggle(
                    new GUIContent("Normalize HDR Emission",
                        "Scales HDR emission so its brightest channel is 1 instead of clipping it to white. " +
                        "Needed for HDRP emissive intensities (nits/EV)."),
                    _normalizeEmission);
            }

            if (EditorGUI.EndChangeCheck()) RefreshPreview();
        }

        // ── Single material section ───────────────────────────────────────────────

        /// <summary>Draws the single-material field, preview and export button.</summary>
        private void DrawSingleSection()
        {
            EditorGUILayout.LabelField("Single Export", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _material = (Material)EditorGUILayout.ObjectField(
                "Material", _material, typeof(Material), allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck()) Assign(_material);

            _resolution = (ExportResolution)EditorGUILayout.EnumPopup("Resolution", _resolution);

            EditorGUILayout.Space(4f);

            EditorGUI.BeginChangeCheck();
            _previewMap = (MapType)GUILayout.Toolbar((int)_previewMap,
                new[] { "Base Color", "Metallic", _invertSmoothness ? "Roughness" : "Smoothness", "Emission" },
                EditorStyles.miniButton);
            if (EditorGUI.EndChangeCheck()) RefreshPreview();

            if (!string.IsNullOrEmpty(_statusMessage))
                EditorGUILayout.HelpBox(_statusMessage, _statusType);

            if (_preview != null)
            {
                EditorGUILayout.Space(4f);
                float size = Mathf.Min(EditorGUIUtility.currentViewWidth - 24f, 240f);
                Rect rect = GUILayoutUtility.GetRect(size, size);
                rect.width = size;
                EditorGUI.DrawPreviewTexture(rect, _preview, mat: null, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(_material == null || _maps == 0))
            {
                if (GUILayout.Button($"Export PNG ({CountMaps(_maps)})", GUILayout.Height(28f)))
                    DoExport();
            }
        }

        // ── Batch section ─────────────────────────────────────────────────────────

        /// <summary>Draws the batch material list and batch export button.</summary>
        private void DrawBatchSection()
        {
            EditorGUILayout.LabelField("Batch Export", EditorStyles.boldLabel);

            // Toolbar row
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Selected", EditorStyles.miniButtonLeft))
                    AddSelectedMaterials();

                if (GUILayout.Button("Clear", EditorStyles.miniButtonRight, GUILayout.Width(50f)))
                    _batchList.Clear();
            }

            // Material list
            float listHeight = Mathf.Clamp(_batchList.Count * 22f + 4f, 44f, 176f);
            _batchScroll = EditorGUILayout.BeginScrollView(
                _batchScroll, GUILayout.Height(listHeight));

            if (_batchList.Count == 0)
            {
                EditorGUILayout.LabelField("  Drag materials here or use Add Selected.",
                    EditorStyles.centeredGreyMiniLabel, GUILayout.Height(36f));
            }
            else
            {
                for (int i = _batchList.Count - 1; i >= 0; i--)
                {
                    using var row = new EditorGUILayout.HorizontalScope();

                    _batchList[i] = (Material)EditorGUILayout.ObjectField(
                        _batchList[i], typeof(Material), allowSceneObjects: false);

                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(20f)))
                        _batchList.RemoveAt(i);
                }
            }

            EditorGUILayout.EndScrollView();

            // Handle drag-and-drop onto the list area
            AcceptDragDrop(GUILayoutUtility.GetLastRect());

            EditorGUILayout.Space(4f);
            _resolution = (ExportResolution)EditorGUILayout.EnumPopup("Resolution", _resolution);
            EditorGUILayout.Space(4f);

            using (new EditorGUI.DisabledScope(_batchList.Count == 0 || _maps == 0))
            {
                if (GUILayout.Button($"Batch Export ({_batchList.Count} × {CountMaps(_maps)}) …", GUILayout.Height(28f)))
                    DoBatchExport();
            }

            EditorGUILayout.Space(4f);
        }

        // ── Drag-and-drop support ─────────────────────────────────────────────────

        // MWC: allow dragging material assets from the Project window into the list.
        /// <summary>Adds Materials dropped on the batch list area.</summary>
        private void AcceptDragDrop(Rect dropArea)
        {
            var ev = Event.current;
            if (ev.type != EventType.DragUpdated && ev.type != EventType.DragPerform) return;
            if (!dropArea.Contains(ev.mousePosition)) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (ev.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is Material mat && !_batchList.Contains(mat))
                        _batchList.Add(mat);
                }
                ev.Use();
                Repaint();
            }
        }

        // ── Assignment ────────────────────────────────────────────────────────────

        /// <summary>Adds every Material in the Project selection to the batch list.</summary>
        private void AddSelectedMaterials()
        {
            foreach (var obj in Selection.objects)
            {
                if (obj is Material mat && !_batchList.Contains(mat))
                    _batchList.Add(mat);
            }
        }

        /// <summary>Sets the single-export material and rebakes the preview.</summary>
        private void Assign(Material mat)
        {
            _material = mat;
            RefreshPreview();
        }

        /// <summary>Rebakes the preview of the current material for the selected preview map.</summary>
        private void RefreshPreview()
        {
            _statusMessage = null;
            ClearPreview();

            if (_material == null) { Repaint(); return; }

            _preview = BakeMap(_material, _previewMap, 128);

            if (_preview == null)
            {
                _statusMessage = "Failed to bake — check the Console for details.";
                _statusType = MessageType.Warning;
            }

            Repaint();
        }

        // ── Core bake ────────────────────────────────────────────────────────────────

        /// <summary>Bakes one map of a material into a new readable RGBA32 texture.</summary>
        private Texture2D BakeMap(Material mat, MapType map, int resolution)
        {
            return map switch
            {
                MapType.BaseColor => BakeBaseColor(mat, resolution),
                MapType.Emission => BakeEmission(mat, resolution, _normalizeEmission),
                MapType.Metallic => BakeSurfaceScalar(mat, resolution, metallic: true, invert: false),
                MapType.Smoothness => BakeSurfaceScalar(mat, resolution, metallic: false, invert: _invertSmoothness),
                _ => null,
            };
        }

        // MWC: takes explicit material so the same path works for single and batch export.
        /// <summary>Bakes the Base Color via the Meta pass, or samples the albedo texture as a fallback.</summary>
        private Texture2D BakeBaseColor(Material mat, int resolution)
        {
            if (mat == null) return null;

            int metaPass = mat.FindPass("Meta");
            if (metaPass >= 0)
                return BakeViaMetaPass(mat, resolution, metaPass);

            return BakeViaDirectSample(mat, resolution);
        }

        /// <summary>Bakes the Emission via the Meta pass emission branch, or samples the emission map as a fallback.</summary>
        private static Texture2D BakeEmission(Material mat, int resolution, bool normalize)
        {
            if (mat == null) return null;

            Color[] linear;
            int metaPass = mat.FindPass("Meta");

            if (metaPass >= 0)
            {
                linear = RenderMetaPass(mat, resolution, metaPass, emission: true);
            }
            else
            {
                Color tint = Color.black;
                foreach (var name in new[] { "_EmissionColor", "_EmissiveColor" })
                    if (mat.HasProperty(name)) { tint = mat.GetColor(name).linear; break; }

                if (TryGetTexture(mat, out var tex, "_EmissionMap", "_EmissiveColorMap"))
                {
                    linear = ReadLinear(tex, resolution, GetUVScale(mat), GetUVOffset(mat));
                    for (int i = 0; i < linear.Length; i++) linear[i] *= tint;
                }
                else
                {
                    linear = new Color[resolution * resolution];
                    for (int i = 0; i < linear.Length; i++) linear[i] = tint;
                }
            }

            return EncodeEmission(linear, resolution, normalize);
        }

        // MWC: On DX11/DX12/Vulkan, unity_MetaFragmentControl lives inside the
        //      "UnityMetaPass" CBUFFER — Shader.SetGlobalVector can't reach it and the
        //      branch `if (unity_MetaFragmentControl.x)` is never taken → black output.
        //      Fix: bind a ComputeBuffer that matches the CBUFFER layout exactly.
        //      We clone the material so we never dirty the original project asset.
        //
        //  UnityMetaPass CBUFFER layout (HLSL std packing, 48 bytes total):
        //    bool4  unity_MetaVertexControl   offset  0, 16 B  → use UV1 for vertex pos
        //    bool4  unity_MetaFragmentControl offset 16, 16 B  → .x albedo, .y emission
        //    float  unity_OneOverOutputBoost  offset 32,  4 B  → exponent (1 = identity)
        //    float  unity_MaxOutputValue      offset 36,  4 B  → clamp ceiling (1 = LDR)
        //    float  unity_UseLinearSpace      offset 40,  4 B  → 1 = emission left linear
        //    float  unity_SpecularBoost       offset 44,  4 B
        /// <summary>Bakes the Base Color through the material's Meta pass into an sRGB RGBA32 texture.</summary>
        private Texture2D BakeViaMetaPass(Material mat, int resolution, int metaPass)
        {
            var rt = RenderTexture.GetTemporary(resolution, resolution, depthBuffer: 0,
                format: RenderTextureFormat.ARGB32, readWrite: RenderTextureReadWrite.sRGB);

            DrawMetaPass(mat, rt, metaPass, emission: false);

            var result = ReadRenderTexture(rt, TextureFormat.RGBA32);
            RenderTexture.ReleaseTemporary(rt);

            // MWC: on DX the render-texture Y-flip from GetGPUProjectionMatrix(renderIntoTexture:true)
            //      inverts the image — flip pixel rows to correct it.
            FlipVertical(result);

            return result;
        }

        /// <summary>Runs the Meta pass into a linear half-float target and returns its (flipped) linear pixels.</summary>
        private static Color[] RenderMetaPass(Material mat, int resolution, int metaPass, bool emission)
        {
            var rt = RenderTexture.GetTemporary(resolution, resolution, depthBuffer: 0,
                format: RenderTextureFormat.ARGBHalf, readWrite: RenderTextureReadWrite.Linear);

            DrawMetaPass(mat, rt, metaPass, emission);

            var tex = ReadRenderTexture(rt, TextureFormat.RGBAHalf);
            RenderTexture.ReleaseTemporary(rt);

            FlipVertical(tex);
            var pixels = tex.GetPixels();
            DestroyImmediate(tex);
            return pixels;
        }

        /// <summary>Draws the UV quad with the Meta pass into rt, outputting albedo or emission.</summary>
        private static void DrawMetaPass(Material mat, RenderTexture rt, int metaPass, bool emission)
        {
            var mesh = BuildUVQuad();

            // MWC: explicit element assignment avoids the Mono RuntimeHelpers.InitializeArray
            //      bug that fires for constant float[] initializers of this size.
            // float nonzero == true for HLSL bool in a CBUFFER (0x3F800000 ≠ 0)
            float albedoOn = emission ? 0f : 1f;
            float emissionOn = emission ? 1f : 0f;
            var cbufData = new float[12];
            cbufData[0] = 1f; cbufData[1] = 0f; cbufData[2] = 0f; cbufData[3] = 0f; // MetaVertexControl
            cbufData[4] = albedoOn; cbufData[5] = emissionOn; cbufData[6] = 0f; cbufData[7] = 0f; // MetaFragmentControl
            cbufData[8] = 1f; // OneOverOutputBoost — pow(albedo,1) = identity
            cbufData[9] = 1f; // MaxOutputValue     — clamp to 1 (LDR)
            cbufData[10] = emissionOn; // UseLinearSpace — keep emission linear (no Gamma20ToLinear)
            cbufData[11] = 1f; // SpecularBoost
            var metaCbuf = new ComputeBuffer(
                cbufData.Length, sizeof(float), ComputeBufferType.Constant);
            metaCbuf.SetData(cbufData);

            // Clone the material so SetConstantBuffer never dirties the project asset
            var bakeMat = new Material(mat) { hideFlags = HideFlags.HideAndDontSave };
            bakeMat.SetConstantBuffer(
                "UnityMetaPass", metaCbuf, 0, cbufData.Length * sizeof(float));

            // GLES fallback: same names are plain uniforms there, not CBUFFER-bound
            Shader.SetGlobalVector("unity_MetaFragmentControl", new Vector4(albedoOn, emissionOn, 0f, 0f));
            Shader.SetGlobalVector("unity_MetaVertexControl", new Vector4(1f, 0f, 0f, 0f));
            Shader.SetGlobalFloat("unity_OneOverOutputBoost", 1f);
            Shader.SetGlobalFloat("unity_MaxOutputValue", 1f);
            Shader.SetGlobalFloat("unity_UseLinearSpace", emissionOn);
            Shader.SetGlobalVector("unity_LightmapST", new Vector4(1f, 1f, 0f, 0f));
            Shader.SetGlobalVector("unity_DynamicLightmapST", new Vector4(1f, 1f, 0f, 0f));

            // Ortho VP: maps object-space x,y in 0..1 → clip-space −1..1
            var gpuProj = GL.GetGPUProjectionMatrix(
                Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 1f), renderIntoTexture: true);

            var cb = new CommandBuffer { name = "AlbedoExporter_MetaBake" };
            cb.SetViewProjectionMatrices(Matrix4x4.identity, gpuProj);
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, Color.clear);
            cb.DrawMesh(mesh, Matrix4x4.identity, bakeMat, 0, metaPass);
            Graphics.ExecuteCommandBuffer(cb);
            cb.Release();

            DestroyImmediate(bakeMat);
            DestroyImmediate(mesh);
            metaCbuf.Release();

            Shader.SetGlobalVector("unity_MetaFragmentControl", Vector4.zero);
            Shader.SetGlobalVector("unity_MetaVertexControl", Vector4.zero);
        }

        // MWC: fallback for shaders without a Meta pass — uses HasProperty to avoid
        //      Unity errors that GetTexture throws when a property doesn't exist.
        /// <summary>Samples the albedo texture times its tint when the shader has no Meta pass.</summary>
        private static Texture2D BakeViaDirectSample(Material mat, int resolution)
        {
            TryGetTexture(mat, out var srcTex, BaseColorNames);

            if (srcTex == null)
            {
                var shader = mat.shader;
                int count = shader.GetPropertyCount();
                for (int i = 0; i < count && srcTex == null; i++)
                {
                    if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                    string n = shader.GetPropertyName(i).ToLower();
                    if (n.Contains("albedo") || n.Contains("base") || n.Contains("diffuse"))
                        srcTex = mat.GetTexture(shader.GetPropertyName(i));
                }
            }

            Color tint = Color.white;
            if (mat.HasProperty("_BaseColor")) tint = mat.GetColor("_BaseColor");
            else if (mat.HasProperty("_Color")) tint = mat.GetColor("_Color");

            var rt = RenderTexture.GetTemporary(resolution, resolution, depthBuffer: 0,
                             format: RenderTextureFormat.ARGB32, readWrite: RenderTextureReadWrite.sRGB);
            var prevRT = RenderTexture.active;

            if (srcTex != null)
            {
                Graphics.Blit(srcTex, rt);
            }
            else
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, tint.linear);
                tint = Color.white;
            }

            RenderTexture.active = prevRT;
            var result = ReadRenderTexture(rt, TextureFormat.RGBA32);
            RenderTexture.ReleaseTemporary(rt);

            if (tint != Color.white)
            {
                Color[] pixels = result.GetPixels();
                for (int i = 0; i < pixels.Length; i++) pixels[i] *= tint;
                result.SetPixels(pixels);
                result.Apply();
            }

            return result;
        }

        // ── Metallic / Smoothness ─────────────────────────────────────────────────

        // No Meta pass outputs these, so they are rebuilt from the material data:
        //   HDRP Lit      _MaskMap          R = metallic, A = smoothness, lerped by the *RemapMin/Max sliders
        //   URP / Std     _MetallicGlossMap R = metallic, A = smoothness × _Smoothness / _GlossMapScale
        //                 _SmoothnessTextureChannel = 1 → smoothness from the Base Map alpha instead
        //   No map        _Metallic, _Smoothness / _Glossiness sliders as a flat value
        //   Shader Graph  first texture whose name contains "metal" or "mask"
        /// <summary>Bakes a grayscale Metallic or Smoothness map (optionally inverted to Roughness).</summary>
        private static Texture2D BakeSurfaceScalar(Material mat, int resolution, bool metallic, bool invert)
        {
            if (mat == null) return null;

            Texture tex = null;
            int channel = metallic ? 0 : 3;
            float min = 0f, max = 1f;

            if (TryGetTexture(mat, out tex, "_MaskMap"))
            {
                min = GetFloat(mat, 0f, metallic ? "_MetallicRemapMin" : "_SmoothnessRemapMin");
                max = GetFloat(mat, 1f, metallic ? "_MetallicRemapMax" : "_SmoothnessRemapMax");
            }
            else if (!metallic && GetFloat(mat, 0f, "_SmoothnessTextureChannel") > 0.5f
                     && TryGetTexture(mat, out tex, BaseColorNames))
            {
                max = GetFloat(mat, 1f, "_Smoothness", "_GlossMapScale");
            }
            else if (TryGetTexture(mat, out tex, "_MetallicGlossMap", "_MetallicMap", "_MetallicSmoothness")
                     || TryFindTexture(mat, out tex, "metal", "mask"))
            {
                if (!metallic) max = GetFloat(mat, 1f, "_Smoothness", "_GlossMapScale");
            }

            var pixels = new Color[resolution * resolution];

            if (tex == null)
            {
                float v = metallic
                    ? GetFloat(mat, 0f, "_Metallic")
                    : GetFloat(mat, 0.5f, "_Smoothness", "_Glossiness");
                if (invert) v = 1f - v;
                var c = new Color(v, v, v, 1f);
                for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
            }
            else
            {
                var src = ReadLinear(tex, resolution, GetUVScale(mat), GetUVOffset(mat));
                for (int i = 0; i < pixels.Length; i++)
                {
                    float v = Mathf.Lerp(min, max, src[i][channel]);
                    if (invert) v = 1f - v;
                    pixels[i] = new Color(v, v, v, 1f);
                }
            }

            var result = new Texture2D(resolution, resolution, TextureFormat.RGBA32, mipChain: false, linear: true);
            result.SetPixels(pixels);
            result.Apply();
            return result;
        }

        // ── Texture helpers ───────────────────────────────────────────────────────

        /// <summary>Returns the first non-null texture among the given properties.</summary>
        private static bool TryGetTexture(Material mat, out Texture tex, params string[] names)
        {
            tex = null;
            foreach (var name in names)
            {
                if (!mat.HasProperty(name)) continue;
                tex = mat.GetTexture(name);
                if (tex != null) return true;
            }
            return false;
        }

        /// <summary>Returns the first assigned texture whose property name contains any keyword (Shader Graph guess).</summary>
        private static bool TryFindTexture(Material mat, out Texture tex, params string[] keywords)
        {
            tex = null;
            var shader = mat.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                string n = shader.GetPropertyName(i);
                string lower = n.ToLower();
                foreach (var k in keywords)
                {
                    if (!lower.Contains(k)) continue;
                    tex = mat.GetTexture(n);
                    if (tex != null) return true;
                }
            }
            return false;
        }

        /// <summary>Returns the first existing float property, or the fallback.</summary>
        private static float GetFloat(Material mat, float fallback, params string[] names)
        {
            foreach (var name in names)
                if (mat.HasProperty(name)) return mat.GetFloat(name);
            return fallback;
        }

        /// <summary>Tiling of the base map — URP and HDRP Lit share it with every other map.</summary>
        private static Vector2 GetUVScale(Material mat)
        {
            foreach (var name in BaseColorNames)
                if (mat.HasProperty(name)) return mat.GetTextureScale(name);
            return Vector2.one;
        }

        /// <summary>Offset of the base map — URP and HDRP Lit share it with every other map.</summary>
        private static Vector2 GetUVOffset(Material mat)
        {
            foreach (var name in BaseColorNames)
                if (mat.HasProperty(name)) return mat.GetTextureOffset(name);
            return Vector2.zero;
        }

        /// <summary>Blits any texture (readable or not) into a linear target and returns its raw pixels.</summary>
        private static Color[] ReadLinear(Texture src, int resolution, Vector2 scale, Vector2 offset)
        {
            var rt = RenderTexture.GetTemporary(resolution, resolution, depthBuffer: 0,
                format: RenderTextureFormat.ARGBHalf, readWrite: RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt, scale, offset);

            var tex = ReadRenderTexture(rt, TextureFormat.RGBAHalf);
            RenderTexture.ReleaseTemporary(rt);

            var pixels = tex.GetPixels();
            DestroyImmediate(tex);
            return pixels;
        }

        /// <summary>Copies a render texture into a new CPU-readable Texture2D.</summary>
        private static Texture2D ReadRenderTexture(RenderTexture rt, TextureFormat format)
        {
            var prevRT = RenderTexture.active;
            RenderTexture.active = rt;
            var result = new Texture2D(rt.width, rt.height, format, mipChain: false);
            result.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0);
            result.Apply();
            RenderTexture.active = prevRT;
            return result;
        }

        /// <summary>Turns linear HDR emission into an sRGB RGBA32 texture, normalizing or clipping values above 1.</summary>
        private static Texture2D EncodeEmission(Color[] linear, int resolution, bool normalize)
        {
            float peak = 0f;
            if (normalize)
            {
                foreach (var c in linear)
                    peak = Mathf.Max(peak, c.maxColorComponent);
            }
            float scale = normalize && peak > 1f ? 1f / peak : 1f;
            bool toGamma = QualitySettings.activeColorSpace == ColorSpace.Linear;

            var pixels = new Color[linear.Length];
            for (int i = 0; i < linear.Length; i++)
            {
                var c = linear[i] * scale;
                c.a = 1f;
                if (toGamma) c = c.gamma;
                pixels[i] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
            }

            var result = new Texture2D(resolution, resolution, TextureFormat.RGBA32, mipChain: false);
            result.SetPixels(pixels);
            result.Apply();
            return result;
        }

        // UV quad: UV0=UV1=0..1 so the Meta pass can sample textures (UV0) and
        // MetaVertexPosition can reposition vertices via UV1.
        /// <summary>Builds a 0..1 quad with matching UV0 and UV1.</summary>
        private static Mesh BuildUVQuad()
        {
            var m = new Mesh();
            m.vertices = new[] {
                new Vector3(0f, 0f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(1f, 0f, 0f)
            };
            var uv = new[] {
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 0f)
            };
            m.uv = uv;
            m.uv2 = uv;
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            return m;
        }

        // ── Export ────────────────────────────────────────────────────────────────

        /// <summary>Exports the selected maps of the single material (file dialog for one map, folder for several).</summary>
        private void DoExport()
        {
            if (_material == null || _maps == 0) return;

            string matPath = AssetDatabase.GetAssetPath(_material);
            string defaultDir = string.IsNullOrEmpty(matPath)
                ? "Assets"
                : Path.GetDirectoryName(matPath)!.Replace('\\', '/');

            var maps = SelectedMaps(_maps);

            if (maps.Count == 1)
            {
                var map = maps[0];
                string savePath = EditorUtility.SaveFilePanel(
                    $"Export {MapLabel(map)} PNG", defaultDir,
                    $"{_material.name}_{MapLabel(map)}", "png");

                if (string.IsNullOrEmpty(savePath)) return;

                if (!BakeAndWrite(_material, map, (int)_resolution, savePath))
                    EditorUtility.DisplayDialog("Albedo Exporter",
                        "Bake failed — see the Console for details.", "OK");
                return;
            }

            string outDir = EditorUtility.OpenFolderPanel(
                $"Select Output Folder for {_material.name} maps", defaultDir, "");
            if (string.IsNullOrEmpty(outDir)) return;

            int failed = 0;
            foreach (var map in maps)
            {
                string savePath = Path.Combine(outDir, $"{_material.name}_{MapLabel(map)}.png").Replace('\\', '/');
                if (!BakeAndWrite(_material, map, (int)_resolution, savePath)) failed++;
            }

            if (failed > 0)
                EditorUtility.DisplayDialog("Albedo Exporter",
                    $"{failed} map(s) failed to bake — see the Console for details.", "OK");
        }

        // MWC: batch export — picks an output folder then bakes every material in the list.
        /// <summary>Exports every selected map of every material in the batch list into one folder.</summary>
        private void DoBatchExport()
        {
            if (_batchList.Count == 0 || _maps == 0) return;

            string outDir = EditorUtility.OpenFolderPanel(
                "Select Output Folder for Batch Export", "Assets", "");
            if (string.IsNullOrEmpty(outDir)) return;

            int res = (int)_resolution;
            var maps = SelectedMaps(_maps);
            int total = _batchList.Count * maps.Count;
            int step = 0;
            int succeeded = 0;

            try
            {
                for (int i = 0; i < _batchList.Count; i++)
                {
                    var mat = _batchList[i];
                    if (mat == null) { step += maps.Count; continue; }

                    foreach (var map in maps)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Albedo Exporter — Batch",
                            $"Baking {mat.name} {MapLabel(map)}  ({i + 1}/{_batchList.Count})",
                            (float)step++ / total);

                        string savePath = Path.Combine(outDir, $"{mat.name}_{MapLabel(map)}.png")
                                             .Replace('\\', '/');

                        if (BakeAndWrite(mat, map, res, savePath)) succeeded++;
                        else Debug.LogWarning($"[Malbers Animations] Batch: {MapLabel(map)} bake failed for '{mat.name}'");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[Malbers Animations] Batch export complete — {succeeded}/{total} maps exported to {outDir}");
        }

        /// <summary>Bakes one map and writes it to savePath; returns false when the bake failed.</summary>
        private bool BakeAndWrite(Material mat, MapType map, int resolution, string savePath)
        {
            var tex = BakeMap(mat, map, resolution);
            if (tex == null) return false;

            WriteAndImport(tex, savePath, MapLabel(map), map == MapType.BaseColor || map == MapType.Emission);
            DestroyImmediate(tex);
            return true;
        }

        // Writes PNG bytes and reimports the asset if it's inside the project.
        /// <summary>Writes the PNG, imports it when inside Assets, and turns sRGB off for data maps.</summary>
        private static void WriteAndImport(Texture2D tex, string savePath, string label, bool sRGB)
        {
            File.WriteAllBytes(savePath, tex.EncodeToPNG());

            if (savePath.StartsWith(Application.dataPath))
            {
                string relative = "Assets" + savePath[Application.dataPath.Length..];
                AssetDatabase.ImportAsset(relative);

                if (!sRGB && AssetImporter.GetAtPath(relative) is TextureImporter importer && importer.sRGBTexture)
                {
                    importer.sRGBTexture = false;
                    importer.SaveAndReimport();
                }
            }

            Debug.Log($"[Malbers Animations] {label} exported → {savePath}");
        }

        /// <summary>File suffix / display name of a map (Smoothness becomes Roughness when inverted).</summary>
        private string MapLabel(MapType map) => map switch
        {
            MapType.BaseColor => "BaseColor",
            MapType.Metallic => "Metallic",
            MapType.Smoothness => _invertSmoothness ? "Roughness" : "Smoothness",
            MapType.Emission => "Emission",
            _ => map.ToString(),
        };

        /// <summary>Expands the flags into an ordered list of single maps.</summary>
        private static List<MapType> SelectedMaps(ExportMaps flags)
        {
            var list = new List<MapType>();
            foreach (var map in AllMaps)
                if (((int)flags & (1 << (int)map)) != 0) list.Add(map);
            return list;
        }

        /// <summary>Number of maps set in the flags.</summary>
        private static int CountMaps(ExportMaps flags) => SelectedMaps(flags).Count;

        // ── Cleanup ───────────────────────────────────────────────────────────────

        /// <summary>Destroys the preview texture.</summary>
        private void ClearPreview()
        {
            if (_preview != null) DestroyImmediate(_preview);
            _preview = null;
        }

        /// <summary>Releases the preview when the window closes.</summary>
        private void OnDestroy() => ClearPreview();

        // ── Helpers ───────────────────────────────────────────────────────────────

        // MWC: flips pixel rows in-place — corrects DX render-texture vertical inversion.
        /// <summary>Flips the texture's pixel rows in place.</summary>
        private static void FlipVertical(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            Color[] pixels = tex.GetPixels();
            var row = new Color[w];
            for (int y = 0; y < h / 2; y++)
            {
                int top = y * w, bot = (h - 1 - y) * w;
                System.Array.Copy(pixels, top, row, 0, w);
                System.Array.Copy(pixels, bot, pixels, top, w);
                System.Array.Copy(row, 0, pixels, bot, w);
            }
            tex.SetPixels(pixels);
            tex.Apply();
        }

        /// <summary>Draws a thin horizontal divider line.</summary>
        private static void Separator()
        {
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.3f));
            EditorGUILayout.Space(2f);
        }
    }
}
#endif
