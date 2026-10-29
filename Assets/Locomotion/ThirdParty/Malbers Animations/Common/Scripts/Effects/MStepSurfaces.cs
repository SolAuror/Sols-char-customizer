// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
#endif

// MWC — New file. Surface-aware footstep definitions for the Steps Manager.
namespace MalbersAnimations
{
    /// <summary>
    /// Stores footstep sounds and particle overrides per Surface.
    /// A Surface is identified by a Surface ID (via SurfaceTag component on the ground),
    /// by Terrain Layer diffuse textures, or by a mesh material main texture.
    /// </summary>
    [CreateAssetMenu(menuName = "Malbers Animations/Effects/Step Surfaces", fileName = "New Step Surfaces", order = -1000)]
    public class MStepSurfaces : ScriptableObject
    {
        [Tooltip("Fallback sound used when the ground does not match any Surface entry")]
        public AudioClipReference DefaultSound;

        [Tooltip("Surface definitions. Each entry maps Textures (Terrain Layers or Material textures) and an optional Surface ID to a Sound and Particle overrides")]
        public List<StepSurface> Surfaces = new();

        /// <summary>Runtime lookup: Texture → Surface entry (Terrain Layer diffuse or Material main texture)</summary>
        private Dictionary<Texture, StepSurface> textureMap;
        /// <summary>Runtime lookup: SurfaceID.ID → Surface entry (SurfaceTag override path)</summary>
        private Dictionary<int, StepSurface> surfaceIDMap;

        /// <summary>True when at least one Surface entry has a Texture. False → the Steps Manager skips Terrain/Material texture lookups</summary>
        public bool HasTextures { get { Initialize(); return textureMap.Count > 0; } }

        /// <summary>Clears the lookup maps so they are rebuilt on the next query (call after editing [Surfaces] directly at runtime)</summary>
        public void Invalidate() { textureMap = null; surfaceIDMap = null; }

        /// <summary>Finds a Surface entry by its [name]. Null if there's no match</summary>
        public StepSurface FindByName(string surfaceName)
        {
            if (string.IsNullOrEmpty(surfaceName) || Surfaces == null) return null;
            return Surfaces.Find(s => s != null && s.name == surfaceName);
        }

        /// <summary>Adds a Texture to a Surface entry at runtime and keeps the lookup map in sync. Returns false if the texture is null or already used by another entry</summary>
        public bool AddTexture(StepSurface entry, Texture texture)
        {
            if (entry == null || texture == null) return false;
            Initialize();

            if (textureMap.TryGetValue(texture, out var owner)) return owner == entry; //Already mapped (same entry → ok, other entry → refuse)

            entry.textures ??= new List<Texture>();
            if (!entry.textures.Contains(texture)) entry.textures.Add(texture);
            textureMap[texture] = entry;
            return true;
        }

        /// <summary>Adds a Texture to the Surface entry matching a Surface ID. Returns false if there's no entry for that ID</summary>
        public bool AddTexture(SurfaceID id, Texture texture) => AddTexture(FindBySurfaceID(id), texture);

        /// <summary>Adds a Texture to the Surface entry matching a name. Returns false if there's no entry with that name</summary>
        public bool AddTexture(string surfaceName, Texture texture) => AddTexture(FindByName(surfaceName), texture);

        /// <summary>Removes a Texture from a Surface entry at runtime and keeps the lookup map in sync. Returns false if the entry did not contain it</summary>
        public bool RemoveTexture(StepSurface entry, Texture texture)
        {
            if (entry == null || texture == null || entry.textures == null) return false;
            Initialize();

            if (!entry.textures.Remove(texture)) return false;
            if (textureMap.TryGetValue(texture, out var owner) && owner == entry) textureMap.Remove(texture);
            return true;
        }

        /// <summary>Removes a Texture from the Surface entry matching a Surface ID</summary>
        public bool RemoveTexture(SurfaceID id, Texture texture) => RemoveTexture(FindBySurfaceID(id), texture);

        /// <summary>Removes a Texture from the Surface entry matching a name</summary>
        public bool RemoveTexture(string surfaceName, Texture texture) => RemoveTexture(FindByName(surfaceName), texture);

        /// <summary>Removes a Texture from whichever Surface entry uses it. Returns false if no entry contained it</summary>
        public bool RemoveTexture(Texture texture)
        {
            if (texture == null || Surfaces == null) return false;
            Initialize();

            bool removed = false;
            foreach (var entry in Surfaces)
                if (entry?.textures != null && entry.textures.Remove(texture)) removed = true;

            textureMap.Remove(texture);
            return removed;
        }

        // MWC — Builds the lookup maps once (lazy). Null textures/IDs are skipped; on duplicates the first entry wins.
        public void Initialize()
        {
            if (textureMap != null) return;

            textureMap = new Dictionary<Texture, StepSurface>();
            surfaceIDMap = new Dictionary<int, StepSurface>();

            foreach (var entry in Surfaces)
            {
                if (entry == null) continue;

                if (entry.surface != null && !surfaceIDMap.TryAdd(entry.surface.ID, entry))
                {
#if UNITY_EDITOR
                    Debug.LogWarning($"[{name}] Surface ID [{entry.surface.name}] is used by more than one entry. Entry [{entry.name}] will be ignored for that ID.", this);
#endif
                }

                if (entry.textures == null) continue;

                foreach (var tex in entry.textures)
                {
                    if (tex != null && !textureMap.TryAdd(tex, entry))
                    {
#if UNITY_EDITOR
                        Debug.LogWarning($"[{name}] Texture [{tex.name}] is used by more than one entry. Entry [{entry.name}] will be ignored for that texture.", this);
#endif
                    }
                }
            }
        }

        /// <summary>Finds the Surface entry matching a Surface ID. Null if there's no match</summary>
        public StepSurface FindBySurfaceID(SurfaceID id)
        {
            if (id == null) return null;
            Initialize();
            return surfaceIDMap.TryGetValue(id.ID, out var entry) ? entry : null;
        }

        /// <summary>Finds the Surface entry containing a Texture (Terrain Layer diffuse or Material main texture). Null if there's no match</summary>
        public StepSurface FindByTexture(Texture texture)
        {
            if (texture == null) return null;
            Initialize();
            return textureMap.TryGetValue(texture, out var entry) ? entry : null;
        }

#if UNITY_EDITOR
        private void OnValidate() => Invalidate(); // MWC — rebuild the maps after editing in Play Mode
#endif
    }

    /// <summary>How a surface entry treats the Step Manager particles</summary>
    public enum StepParticleMode
    {
        /// <summary>Use the Step Manager default Particle System</summary>
        None,
        /// <summary>Do not play this Particle System on this surface</summary>
        Disable,
        /// <summary>Use a custom Particle System on this surface</summary>
        Override
    }

    /// <summary>A single Surface definition: which textures identify it, and what sound/particles it produces</summary>
    [System.Serializable]
    public class StepSurface
    {
        [Tooltip("Name of the Surface (Inspector readability only)")]
        public string name = "Surface";

        [Tooltip("Optional Surface ID. Matched first when the ground has a SurfaceTag (ISurface) component")]
        public SurfaceID surface;

        [Tooltip("Terrain Layer diffuse textures and/or mesh material main textures that identify this surface")]
        public List<Texture> textures = new();

        [Tooltip("Sound played when stepping on this surface (AudioClip or Audio Random Container)")]
        public AudioResource sound;

        // MWC — None: use the Step Manager default Tracks | Disable: no tracks on this surface | Override: use [TrackOverride]
        [Tooltip("Tracks on this surface. None: use the Step Manager default | Disable: no Tracks | Override: use a custom Particle System")]
        public StepParticleMode TrackMode = StepParticleMode.None;

        [Tooltip("Custom Tracks Particle System used when [Track Mode] is set to Override")]
        public ParticleSystem TrackOverride;

        // MWC — None: use the Step Manager default Dust | Disable: no dust on this surface | Override: use [DustOverride]
        [Tooltip("Dust on this surface. None: use the Step Manager default | Disable: no Dust | Override: use a custom Particle System")]
        public StepParticleMode DustMode = StepParticleMode.None;

        [Tooltip("Custom Dust Particle System used when [Dust Mode] is set to Override")]
        public ParticleSystem DustOverride;
    }

    /// <summary>Cached classification of a ground collider (resolved once per collider by the Steps Manager)</summary>
    public struct StepGroundInfo
    {
        /// <summary>SurfaceTag/ISurface found on the ground (highest priority). Null if none</summary>
        public ISurface Tag;
        /// <summary>Terrain when the ground is a TerrainCollider. Null otherwise</summary>
        public Terrain Terrain;
        /// <summary>Cached TerrainData of the Terrain</summary>
        public TerrainData Data;
        /// <summary>Renderer of the ground mesh. Null when the ground is a Terrain or has no renderer</summary>
        public Renderer Renderer;
    }

#if UNITY_EDITOR
    // MWC — In-file editor (same pattern as AnimEventSoundEditor)
    [CanEditMultipleObjects, CustomEditor(typeof(MStepSurfaces))]
    public class MStepSurfacesEditor : Editor
    {
        private ReorderableList list;
        private SerializedProperty Surfaces, DefaultSound;
        private MStepSurfaces M;

        protected virtual void OnEnable()
        {
            M = (MStepSurfaces)target;

            Surfaces = serializedObject.FindProperty("Surfaces");
            DefaultSound = serializedObject.FindProperty("DefaultSound");

            list = new ReorderableList(serializedObject, Surfaces, true, true, true, true)
            {
                drawElementCallback = DrawElementCallback,
                drawHeaderCallback = HeaderCallbackDelegate,
                onAddCallback = OnAddCallBack
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Surface-aware footstep sounds. Matched by Surface Tag, Terrain Layer, or Material texture");

            EditorGUI.BeginChangeCheck();
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    EditorGUILayout.PropertyField(DefaultSound, new GUIContent("Default Sound", "Fallback sound when the ground matches no Surface entry"));

                list.DoLayoutList();

                if (list.index != -1 && list.index < Surfaces.arraySize)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    {
                        SerializedProperty Element = Surfaces.GetArrayElementAtIndex(list.index);
                        var elm = M.Surfaces[list.index];

                        EditorGUILayout.LabelField("[" + elm.name + "]", EditorStyles.boldLabel);
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(Element.FindPropertyRelative("textures"), true);
                        EditorGUILayout.PropertyField(Element.FindPropertyRelative("sound"));

                        // MWC — Track/Dust modes; show the custom Particle System field only when the mode is [Override]
                        EditorGUILayout.PropertyField(Element.FindPropertyRelative("TrackMode"));
                        if (elm.TrackMode == StepParticleMode.Override)
                            EditorGUILayout.PropertyField(Element.FindPropertyRelative("TrackOverride"));

                        EditorGUILayout.PropertyField(Element.FindPropertyRelative("DustMode"));
                        if (elm.DustMode == StepParticleMode.Override)
                            EditorGUILayout.PropertyField(Element.FindPropertyRelative("DustOverride"));
                        EditorGUI.indentLevel--;

                        if (elm.surface == null && (elm.textures == null || elm.textures.Count == 0 || elm.textures.TrueForAll(t => t == null)))
                            EditorGUILayout.HelpBox("This entry has no Surface ID and no Textures, so it can never match a ground surface", MessageType.Warning);

                        // MWC — Warn when [Override] is selected but no Particle System was assigned (falls back to the Step Manager default)
                        if (elm.TrackMode == StepParticleMode.Override && elm.TrackOverride == null)
                            EditorGUILayout.HelpBox("Track Mode is set to [Override] but no Particle System is assigned. The Step Manager default Tracks will be used", MessageType.Warning);
                        if (elm.DustMode == StepParticleMode.Override && elm.DustOverride == null)
                            EditorGUILayout.HelpBox("Dust Mode is set to [Override] but no Particle System is assigned. The Step Manager default Dust will be used", MessageType.Warning);
                    }
                    EditorGUILayout.EndVertical();
                }
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Step Surfaces");
            }
            serializedObject.ApplyModifiedProperties();
        }

        void HeaderCallbackDelegate(Rect rect)
        {
            Rect R_1 = new(rect.x + 14, rect.y, rect.width / 2, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(R_1, "Name");

            Rect R_2 = new(rect.x + 14 + rect.width / 2, rect.y, rect.width / 2 - 14, EditorGUIUtility.singleLineHeight);
            EditorGUI.LabelField(R_2, "Surface ID");
        }

        void DrawElementCallback(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = Surfaces.GetArrayElementAtIndex(index);

            var name = element.FindPropertyRelative("name");
            var surface = element.FindPropertyRelative("surface");
            rect.y += 2;

            Rect R_1 = new(rect.x, rect.y, rect.width / 2 - 5, EditorGUIUtility.singleLineHeight);
            Rect R_2 = new(rect.x + rect.width / 2, rect.y, rect.width / 2, EditorGUIUtility.singleLineHeight);

            EditorGUI.PropertyField(R_1, name, GUIContent.none);
            EditorGUI.PropertyField(R_2, surface, GUIContent.none);
        }

        void OnAddCallBack(ReorderableList list)
        {
            M.Surfaces ??= new List<StepSurface>();
            M.Surfaces.Add(new StepSurface());
            EditorUtility.SetDirty(M);
        }
    }
#endif
}
