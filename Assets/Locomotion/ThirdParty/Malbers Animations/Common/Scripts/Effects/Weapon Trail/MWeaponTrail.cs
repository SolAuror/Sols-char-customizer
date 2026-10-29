// Last Change: 9/17/2026 5:33pm
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace MalbersAnimations.Utilities
{
    // MWC — Procedural mesh ribbon for weapon attack animations.
    //
    // Design notes (why it is built this way):
    //  · Fixed-capacity ring buffer, allocated once. Index/UV buffers are built once and never rebuilt.
    //    Unused rows collapse onto the head (degenerate triangles, zero alpha) instead of resizing the mesh,
    //    so the vertex count and the per-frame cost are constant and nothing allocates after the first build.
    //  · Exactly one row is captured per frame and timestamped, then pruned by age. Never skip a frame and
    //    never fabricate an intermediate row: both cost roundness (see the note on Sample).
    //  · Row 0 is the current frame's blade position, so the ribbon stays welded to the weapon during a
    //    fast swing instead of visibly detaching.
    //  · Catmull-Rom subdivision at render time is what keeps fast swings smooth. Subdivisions = 0 skips it.
    //  · When not playing the component early-outs on a single branch and the MeshRenderer is disabled.
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/main-components/utilities/weapon-trail")]
    [AddComponentMenu("Malbers/Utilities/Effects - Audio/Weapon Trail")]
    [DefaultExecutionOrder(1600)] // MWC — after IKManager and SuperItemManager (1500) so the bones are final
    public class MWeaponTrail : MonoBehaviour, IAnimatorListener
    {
        #region Inspector

        [RequiredField, Tooltip("Transform at the tip of the blade (the outer edge of the ribbon)")]
        public Transform Tip;

        [RequiredField, Tooltip("Transform at the base of the blade (the inner edge of the ribbon)")]
        public Transform Base;

        // MWC — Several presets on one component: slot 0 for a regular attack, slot 1 for a charged one.
        // Which one is live is picked at Play time by WeaponTrailBehaviour.ProfileIndex, the Reaction, or
        // PlayProfile(int). This is separate from Index below, which says WHICH weapon, not which look.
        [Tooltip("Presets this trail can use. The live one is chosen by Profile Index when the trail plays")]
        public List<WeaponTrailProfile> Profiles = new();

        // MWC — this used to be a single serialized "Profile" field. FormerlySerializedAs keeps already
        // assigned references alive, and MigrateLegacyProfile folds it into Profiles[0].
        [SerializeField, HideInInspector, FormerlySerializedAs("Profile")]
        private WeaponTrailProfile legacyProfile;

        [System.NonSerialized] private int profileIndex;
        [System.NonSerialized] private WeaponTrailProfile profileOverride;

        /// <summary> The preset currently in use. </summary>
        public WeaponTrailProfile Profile
        {
            get
            {
                if (profileOverride != null) return profileOverride;
                if (Profiles != null && Profiles.Count > 0)
                    return Profiles[Mathf.Clamp(profileIndex, 0, Profiles.Count - 1)];
                return legacyProfile;
            }
        }

        /// <summary> Index into <see cref="Profiles"/> currently in use. </summary>
        public int ProfileIndex => profileIndex;

        // MWC — lets WeaponTrailBehaviour target one weapon when a character carries several
        // (dual wield, off-hand shield). Same convention as MDamager.index.
        [Tooltip("Identifies this trail so an Animator Behaviour can target it. Weapon Trail Behaviour " +
                 "with Index 0 plays every trail it finds")]
        [Min(1)] public int Index = 1;

        public UnityEvent OnTrailStart = new();
        public UnityEvent OnTrailEnd = new();

        [HideInInspector] public bool debug;
        [HideInInspector] public Color DebugColor = new(0.3f, 0.8f, 1f, 0.75f);

        #endregion

        #region Runtime state

        private GameObject trailGO;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private MaterialPropertyBlock mpb;
        private Material fallbackMaterial; // MWC — created only when the profile has no material; must be destroyed

        private Vector3[] rowTip, rowBase; // ring buffer, index 0 = newest
        private float[] rowTime;           // MWC — capture timestamp per row, drives age
        private int count;                 // MWC — rows currently holding real data
        private int captures, frames;      // MWC — diagnostics: real captures vs frames ticked
        private Vector3[] vertices;
        private Color[] colors;
        private Vector2[] uvs;
        private int[] triangles;

        private int capacity, subdivisions, renderRows;
        private float trailTime; // MWC — own clock, so the editor preview works outside play mode

        private bool playing;
        private bool lastDebug;    // MWC — tracks the debug toggle so the hide flags refresh live
        private float fade;        // 0..1 global alpha multiplier
        private float fadeTarget;  // 0 or 1
        private float fadeInTime, fadeOutTime;

        private Color runtimeColor = Color.white;

        private Color runtimeEmission = Color.white; // MWC — multiplier, same idea as runtimeColor

        // MWC — last values pushed, so the property block is not re-set every frame
        private Color appliedColor = Color.clear, appliedEmission = Color.clear;

        private float appliedAlphaClip = float.NaN;

        // MWC — IDs resolved from the profile's property-name strings, re-resolved only when they change
        private int colorPropID, emissionPropID, alphaClipPropID;
        private string colorPropCached = null, emissionPropCached = null, alphaClipPropCached = null;
        private bool hasColorProp, hasEmissionProp, hasAlphaClipProp;

        /// <summary> True while the trail is recording. Stays true through the fade out. </summary>
        public bool IsPlaying => playing;

        /// <summary> True when the component is doing no work at all this frame. </summary>
        public bool IsDormant => !playing && fade <= 0f;

        /// <summary> Rows currently allocated in the ring buffer, head included. </summary>
        public int Capacity => capacity;

        /// <summary> Rows currently holding real captured data. This is what governs how round the arc is. </summary>
        public int RowCount => count;

        // MWC — If CapturesPerSecond is far below FramesPerSecond, the Animator is advancing slower than the
        // render loop and IT is the resolution limit, not this component.
        public float CapturesPerSecond => trailTime > 0f ? captures / trailTime : 0f;
        public float FramesPerSecond => trailTime > 0f ? frames / trailTime : 0f;

        /// <summary> Vertices in the generated mesh. </summary>
        public int VertexCount => vertices != null ? vertices.Length : 0;

        #endregion

        #region Unity lifecycle

        private void Awake()
        {
            MigrateLegacyProfile();
            EnsureRuntime();
        }

        // MWC — Folds the old single-Profile field into Profiles[0]. Profiles is intentionally left EMPTY
        // by its initializer: a non-empty default would make this test fail and silently eat the legacy
        // reference the first time the component is loaded.
        private void MigrateLegacyProfile()
        {
            if (legacyProfile == null) return;
            Profiles ??= new List<WeaponTrailProfile>();
            if (Profiles.Count == 0) Profiles.Add(legacyProfile);
            legacyProfile = null;
        }

        private void OnEnable()
        {
            SetRendererEnabled(false);
            Register();
        }

        private void OnDisable()
        {
            playing = false;
            fade = fadeTarget = 0f;
            SetRendererEnabled(false);
            UnRegister();
        }

        private void OnTransformParentChanged()
        {
            if (!isActiveAndEnabled) return; // MWC - OnEnable will take care of the registration when it comes back

            UnRegister();                    // MWC - leave the old Animator
            Register();                      // MWC - join the new Animator and bump the Version
        }

        private void OnDestroy() => DisposeRuntime();

        // MWC — toggling debug in the inspector shows/hides the generated object straight away,
        // even while the trail is dormant
        private void OnValidate()
        {
            MigrateLegacyProfile();
            ApplyHideFlags();
        }

        private void LateUpdate()
        {
            if (IsDormant) return; // MWC — the whole idle cost of the feature is this branch
            Tick(Time.deltaTime);
        }

        #endregion

        #region Public API

        /// <summary> Start recording, fading in over the profile's FadeIn. </summary>
        public void Play() => Play(Profile != null ? Profile.FadeIn : 0f);

        /// <summary> Start recording, fading in over <paramref name="fadeIn"/> seconds. </summary>
        public void Play(float fadeIn)
        {
            if (!EnsureRuntime()) return;

            bool wasPlaying = playing;

            playing = true;
            fadeInTime = Mathf.Max(0f, fadeIn);
            fadeTarget = 1f;
            if (fadeInTime <= 0f) fade = 1f;

            if (!wasPlaying)
            {
                // MWC — restart the clock each swing. Left running for hours, float precision on trailTime
                // would coarsen until per-frame age differences stopped resolving.
                trailTime = 0f;
                captures = frames = 0;
                Clear();          // MWC — collapse history so a new swing never streaks from the old position
                SetRendererEnabled(true);
                OnTrailStart.Invoke();
            }
        }

        /// <summary> Stop recording and fade out over the profile's FadeOut. </summary>
        public void Stop() => Stop(Profile != null ? Profile.FadeOut : 0f);

        /// <summary> Stop recording and fade out over <paramref name="fadeOut"/> seconds. </summary>
        public void Stop(float fadeOut)
        {
            if (!playing) return;

            playing = false;
            fadeOutTime = Mathf.Max(0f, fadeOut);
            fadeTarget = 0f;
            if (fadeOutTime <= 0f) GoDormant();

            OnTrailEnd.Invoke();
        }

        /// <summary> Collapse every recorded row onto the current blade position. </summary>
        public void Clear()
        {
            if (rowTip == null || Tip == null || Base == null) return;

            Vector3 t = Tip.position, b = Base.position;
            for (int i = 0; i < capacity; i++) { rowTip[i] = t; rowBase[i] = b; rowTime[i] = trailTime; }

            count = 0;
        }

        // MWC — Deliberately NOT an overload of Play(float): Play(1) would silently mean "profile 1" to the
        // compiler while reading like "one second of fade in".
        /// <summary> Switch to a profile slot and play. Negative index keeps the current slot. </summary>
        public void PlayProfile(int index)
        {
            SetProfileIndex(index);
            Play();
        }

        /// <summary> Switch to a profile slot and play with an explicit fade in. </summary>
        public void PlayProfile(int index, float fadeIn)
        {
            SetProfileIndex(index);
            Play(fadeIn);
        }

        /// <summary> Select which entry of <see cref="Profiles"/> is live. Negative keeps the current one. </summary>
        public void SetProfileIndex(int index)
        {
            if (index < 0) return;

            int clamped = (Profiles != null && Profiles.Count > 0) ? Mathf.Clamp(index, 0, Profiles.Count - 1) : 0;
            if (clamped == profileIndex && profileOverride == null) return;

            profileOverride = null; // an explicit slot wins over a SetProfile override
            profileIndex = clamped;

            if (vertices == null) return; // not initialised yet; Play() will pick it up
            RebuildIfNeeded(force: false);
            ApplyMaterial();
            ApplyPropertyBlock();
        }

        /// <summary> Force a specific preset, ignoring the Profiles list until a slot is selected again. </summary>
        public void SetProfile(WeaponTrailProfile profile)
        {
            if (profile == null || profile == Profile) return;
            profileOverride = profile;
            if (vertices == null) return;
            RebuildIfNeeded(force: false);
            ApplyMaterial();
            ApplyPropertyBlock();
        }

        /// <summary> Runtime tint multiplier, pushed through the MaterialPropertyBlock. No material instance is created. </summary>
        public void SetColor(Color color)
        {
            runtimeColor = color;
            ApplyPropertyBlock();
        }

        /// <summary> Runtime emission multiplier. White leaves the profile's Emission untouched. </summary>
        public void SetEmission(Color multiplier)
        {
            runtimeEmission = multiplier;
            ApplyPropertyBlock();
        }

        // MWC — framework-wide IAnimatorListener one-liner, lets MessagesBehavior drive Play/Stop/Clear
        // from a normalized time on the attack animation state with no extra code.
        public bool OnAnimatorBehaviourMessage(string message, object value)
            => this.InvokeWithParams(message, value);

        #endregion

        #region Build

        #region Registry

        // MWC — Same pattern as AnimatorEventSounds: trails announce themselves to the Animator above them
        // instead of every attack state paying for a GetComponentsInChildren sweep on OnStateEnter.
        // OnTransformParentChanged is what makes it correct for Super Items: a weapon that is equipped,
        // holstered or moved between hands re-parents, so it leaves the old Animator and joins the new one.
        private static readonly Dictionary<Animator, List<MWeaponTrail>> Registry = new();

        /// <summary> Increases every time the Registry changes, so cached lookups know when to revalidate. </summary>
        public static int Version { get; private set; }

        /// <summary> Animator this trail registered on, kept so it can unregister from the right list. </summary>
        private Animator registeredOn;

        /// <summary> Animator this trail is currently linked to. </summary>
        public Animator Anim => registeredOn;

        /// <summary> Clears the static state when entering Play Mode with Domain Reload disabled. </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Registry.Clear();
            Version = 0;
            s_Container = null; // the container GameObject does not survive a Play Mode restart either
        }

        /// <summary> Finds the Animator on this hierarchy and adds this trail to its Registry list. </summary>
        private void Register()
        {
            registeredOn = GetComponentInParent<Animator>(true);

            if (registeredOn == null) return;

            if (!Registry.TryGetValue(registeredOn, out var list))
                Registry[registeredOn] = list = new List<MWeaponTrail>();

            if (!list.Contains(this)) list.Add(this);

            Version++;

            if (debug) Debug.Log($"[{name}] Weapon Trail registered on Animator [{registeredOn.name}] with Index [{Index}]", this);
        }

        /// <summary> Removes this trail from the Registry list of the Animator it joined. </summary>
        private void UnRegister()
        {
            if (registeredOn != null && Registry.TryGetValue(registeredOn, out var list))
            {
                list.Remove(this);
                if (list.Count == 0) Registry.Remove(registeredOn);
                Version++;
            }

            registeredOn = null;
        }

        /// <summary> All the active Weapon Trails linked to an Animator. Returns null when there are none. </summary>
        public static List<MWeaponTrail> GetAll(Animator animator)
            => animator != null && Registry.TryGetValue(animator, out var list) ? list : null;

        /// <summary> First active Weapon Trail on an Animator matching an Index. Null when not found. </summary>
        public static MWeaponTrail Get(Animator animator, int index)
        {
            var list = GetAll(animator);

            if (list == null) return null;

            for (int i = 0; i < list.Count; i++)
                if (list[i].Index == index) return list[i];

            return null;
        }

        #endregion

        #region Container

        // MWC — Every trail mesh lives under one shared object instead of loose at the Scene root.
        public const string ContainerName = "Weapon Trails";

        private static Transform s_Container;

        /// <summary> The shared "Weapon Trails" parent, created on first use. </summary>
        public static Transform GetContainer()
        {
            // Unity's fake-null covers both a real scene load and a domain reload with statics kept alive.
            if (s_Container == null)
            {
                var found = GameObject.Find(ContainerName);

                s_Container = found != null
                    ? found.transform
                    : new GameObject(ContainerName)
                    {
                        // Visible so the grouping is actually useful, but never written into a scene or build.
                        hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
                    }.transform;
            }

            // Vertices are written in WORLD space, so the container has to stay at identity or every trail
            // would be transformed twice. Re-asserted here rather than trusted, since it is a plain object
            // in the Hierarchy that anything could nudge.
            if (s_Container.localScale != Vector3.one) s_Container.localScale = Vector3.one;
            if (s_Container.position != Vector3.zero || s_Container.rotation != Quaternion.identity)
                s_Container.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            return s_Container;
        }

        #endregion

        /// <summary> Creates the mesh GameObject and buffers if they do not exist yet. </summary>
        private bool EnsureRuntime()
        {
            if (Profile == null || Tip == null || Base == null) return false;

            if (!enabled) return false;

            if (trailGO == null)
            {
                // MWC — Parented to a single shared "Weapon Trails" container that is pinned to identity,
                // so the Scene root does not collect one loose object per weapon while the vertices stay
                // in world space with no matrix work.
                trailGO = new GameObject($"{name} Trail", typeof(MeshFilter), typeof(MeshRenderer))
                {
                    hideFlags = TrailHideFlags,
                    layer = gameObject.layer
                };
                trailGO.transform.SetParent(GetContainer(), false); // MWC — keeps local identity

                mesh = new Mesh { name = "Weapon Trail" };
                mesh.MarkDynamic();

                // MWC — the object can also be destroyed from outside (the container dying on a scene
                // load takes its children with it). Force a full rebuild so the new Mesh gets its
                // triangles and UVs instead of RebuildIfNeeded early-outing on unchanged sizes.
                vertices = null;

                trailGO.GetComponent<MeshFilter>().sharedMesh = mesh;
                meshRenderer = trailGO.GetComponent<MeshRenderer>();
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                meshRenderer.enabled = false;

                mpb = new MaterialPropertyBlock();
            }

            ApplyHideFlags();
            RebuildIfNeeded(force: false);
            ApplyMaterial();
            return true;
        }

        /// <summary> Reallocates the buffers and the static index/UV data when the derived sizes change. </summary>
        private void RebuildIfNeeded(bool force)
        {
            int newCapacity = Profile.Capacity;
            int newSubdiv = Profile.Subdivisions;

            if (!force && vertices != null && newCapacity == capacity && newSubdiv == subdivisions) return;

            capacity = newCapacity;
            subdivisions = newSubdiv;
            renderRows = Profile.RenderRows;

            rowTip = new Vector3[capacity];
            rowBase = new Vector3[capacity];
            rowTime = new float[capacity];

            int vertCount = renderRows * 2;
            vertices = new Vector3[vertCount];
            colors = new Color[vertCount];
            uvs = new Vector2[vertCount];
            triangles = new int[(renderRows - 1) * 6];

            // MWC — With the Alpha curve gone, vertex colour carries no information: the fade along the
            // trail comes from the texture and Tint/fade ride the property block. Fill it with white once
            // (the identity for Multiply colour mode) and never touch it again, instead of uploading an
            // unchanging colour array every frame.
            for (int i = 0; i < vertCount; i++) colors[i] = Color.white;

            // Indices are built once here and never touched again. UVs are NOT static any more: the live
            // row count varies, so they are written per frame from each row's age (see WriteRow).
            for (int r = 0, i = 0; r < renderRows - 1; r++, i += 6)
            {
                int a = r * 2, b = r * 2 + 1, c = (r + 1) * 2, d = (r + 1) * 2 + 1;
                triangles[i + 0] = a; triangles[i + 1] = b; triangles[i + 2] = c;
                triangles[i + 3] = c; triangles[i + 4] = b; triangles[i + 5] = d;
            }

            Clear();

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0, false);
        }

        private void ApplyMaterial()
        {
            if (meshRenderer == null || Profile == null) return;

            // MWC — GetMaterial returns the asset itself, or the profile's shared blend-override copy
            var mat = Profile.GetMaterial();

            if (mat == null)
            {
                // MWC — no material assigned: fall back to a built-in unlit transparent shader that is
                // two sided and multiplies by vertex color. Works in URP, HDRP and Built-in.
                if (fallbackMaterial == null)
                {
                    var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent");
                    if (shader != null)
                        fallbackMaterial = new Material(shader) { name = "Weapon Trail (Fallback)", hideFlags = HideFlags.HideAndDontSave };
                }
                Profile.ApplyBlendTo(fallbackMaterial); // MWC — the override must reach the fallback too
                mat = fallbackMaterial;
            }

            if (meshRenderer.sharedMaterial != mat) meshRenderer.sharedMaterial = mat;

            meshRenderer.shadowCastingMode = Profile.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            ApplyPropertyBlock();
        }

        // MWC — Colour is split in two on purpose:
        //   · vertex alpha  = the per-row gradient (needs per-vertex data, so it can only live there)
        //   · property block = Tint x runtime colour x global fade
        // Shaders that ignore vertex colour (Universal Render Pipeline/Unlit only declares _BaseColor and
        // never reads COLOR0) then still get the right colour and a working fade in/out — they just lose
        // the gradient. Previously Tint and fade were baked into the vertex colour, so on URP/Unlit both
        // silently did nothing while the block stomped _BaseColor with plain white.
        /// <summary> Current tint and emission, both already scaled by the global fade. </summary>
        private void CurrentColors(out Color tintOut, out Color emissionOut)
        {
            Color tint = Profile != null ? Profile.Tint : Color.white;
            bool fadeAlpha = Profile == null || Profile.FadeAffectsColorAlpha;
            tintOut = tint * runtimeColor;
            tintOut.a = tint.a * runtimeColor.a * (fadeAlpha ? fade : 1f);

            Color e = (Profile != null ? Profile.Emission : Color.black) * runtimeEmission;
            emissionOut = new Color(e.r * fade, e.g * fade, e.b * fade, e.a); // glow fades with the trail
        }

        // MWC — Resolves the shader property IDs from the profile's name strings. Re-resolved only when a
        // name actually changes, so editing it in the inspector takes effect without a restart.
        private void RefreshPropertyIDs()
        {
            string cp = Profile != null ? Profile.ColorProperty : null;
            if (!ReferenceEquals(cp, colorPropCached) && cp != colorPropCached)
            {
                colorPropCached = cp;
                hasColorProp = !string.IsNullOrWhiteSpace(cp);
                if (hasColorProp) colorPropID = Shader.PropertyToID(cp);
            }

            string ep = Profile != null ? Profile.EmissionProperty : null;
            if (!ReferenceEquals(ep, emissionPropCached) && ep != emissionPropCached)
            {
                emissionPropCached = ep;
                hasEmissionProp = !string.IsNullOrWhiteSpace(ep);
                if (hasEmissionProp) emissionPropID = Shader.PropertyToID(ep);
            }

            string ap = Profile != null ? Profile.AlphaClipProperty : null;
            if (!ReferenceEquals(ap, alphaClipPropCached) && ap != alphaClipPropCached)
            {
                alphaClipPropCached = ap;
                hasAlphaClipProp = !string.IsNullOrWhiteSpace(ap);
                if (hasAlphaClipProp) alphaClipPropID = Shader.PropertyToID(ap);
            }
        }

        /// <summary> Current alpha clip threshold. Rises toward 1 as the trail fades when dissolving. </summary>
        private float CurrentAlphaClip()
        {
            if (Profile == null) return 0f;
            return Profile.FadeDrivesAlphaClip
                ? Mathf.Lerp(1f, Profile.AlphaClip, fade) // fade 0 -> fully clipped, fade 1 -> authored value
                : Profile.AlphaClip;
        }

        private void ApplyPropertyBlock()
        {
            if (meshRenderer == null || mpb == null) return;

            RefreshPropertyIDs();
            CurrentColors(out var c, out var e);

            // Only the named properties are touched, so anything else on the material is left as authored.
            float clip = CurrentAlphaClip();

            if (hasColorProp) mpb.SetColor(colorPropID, c);
            if (hasEmissionProp) mpb.SetColor(emissionPropID, e);
            if (hasAlphaClipProp) mpb.SetFloat(alphaClipPropID, clip);
            meshRenderer.SetPropertyBlock(mpb);

            appliedColor = c;
            appliedEmission = e;
            appliedAlphaClip = clip;
        }

        // MWC — With debug on, the generated object shows up under "Weapon Trails" as "<weapon> Trail" and
        // is fully inspectable. With debug off it is hidden from the Hierarchy.
        //
        // Deliberately NOT HideFlags.HideAndDontSave: that also sets HideInInspector + NotEditable (so it
        // could not be inspected) and DontUnloadUnusedAsset (which keeps the object alive across a scene
        // load). DontSaveInEditor|DontSaveInBuild is all that is needed to keep it out of the scene file.
        private HideFlags TrailHideFlags => debug
            ? HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
            : HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.HideInHierarchy;

        /// <summary> The generated mesh GameObject. Null until the trail has been initialized. </summary>
        public GameObject TrailObject => trailGO;

        private void ApplyHideFlags()
        {
            if (trailGO == null) return;
            lastDebug = debug;
            if (trailGO.hideFlags != TrailHideFlags) trailGO.hideFlags = TrailHideFlags;
        }

        private void SetRendererEnabled(bool value)
        {
            if (meshRenderer != null) meshRenderer.enabled = value;
        }

        private void GoDormant()
        {
            fade = 0f;
            SetRendererEnabled(false);
        }

        private void DisposeRuntime()
        {
            if (mesh != null) SafeDestroy(mesh);
            if (fallbackMaterial != null) SafeDestroy(fallbackMaterial);
            if (trailGO != null) SafeDestroy(trailGO);

            mesh = null;
            fallbackMaterial = null;
            trailGO = null;
            meshRenderer = null;

            // MWC — force a full rebuild next time. Without this, RebuildIfNeeded early-outs on the
            // unchanged sizes and the freshly created Mesh would keep no triangles or UVs (invisible trail
            // when the editor preview is stopped and started again).
            vertices = null;
        }

        // MWC — named SafeDestroy, not DestroyObject, to avoid hiding the obsolete Object.DestroyObject
        private static void SafeDestroy(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        #endregion

        #region Simulation

        /// <summary> One simulation step. Shared by LateUpdate and the editor preview. </summary>
        private void Tick(float dt)
        {
            if (Profile == null || Tip == null || Base == null) return;
            if (vertices == null && !EnsureRuntime()) return;

            RebuildIfNeeded(force: false); // MWC — picks up live profile edits while playing
            if (lastDebug != debug) ApplyHideFlags(); // MWC — debug flipped from code, not the inspector
            dt = Mathf.Max(dt, 1e-6f);
            trailTime += dt;
            if (playing) frames++;

            // Fade towards the target
            if (!Mathf.Approximately(fade, fadeTarget))
            {
                float fadeDuration = fadeTarget > fade ? fadeInTime : fadeOutTime;
                fade = fadeDuration <= 0f
                    ? fadeTarget
                    : Mathf.MoveTowards(fade, fadeTarget, dt / fadeDuration);
            }

            if (playing) Sample();

            // MWC — Tint, Emission and fade live in the property block now, so it has to follow them
            CurrentColors(out var wantColor, out var wantEmission);
            if (wantColor != appliedColor || wantEmission != appliedEmission
                || !Mathf.Approximately(CurrentAlphaClip(), appliedAlphaClip)) ApplyPropertyBlock();

            BuildMesh();

            if (!playing && fade <= 0f) GoDormant();
        }

        // MWC — One row per frame, timestamped, pruned by age.
        //
        // This replaces the old fixed-rate sampler, which was the reason trails looked faceted:
        //  · When the framerate was HIGHER than the capture rate it skipped frames, throwing away real
        //    blade positions. 6 rows across a 270 degree swing is 45 degrees per row, and no amount of
        //    subdivision recovers a round arc from that.
        //  · When the framerate was LOWER it manufactured rows with Vector3.Lerp between two frames.
        //    Those points sit on the chord, and forcing the spline through collinear points FLATTENS the
        //    curve. Raising the capture rate therefore made the trail less round, not more.
        //
        // Capturing exactly one row per real frame keeps every position the animation actually produced
        // and never feeds the spline a fabricated point.
        private void Sample()
        {
            Vector3 curTip = Tip.position, curBase = Base.position;

            // MWC — The render loop is usually faster than the Animator, so many frames deliver an unchanged
            // pose. Pushing those as new rows wastes the row budget on duplicates AND feeds Catmull-Rom
            // coincident points, whose zero tangent collapses the segment and leaves a hard corner. Refresh
            // the newest row in place instead, so only real movement ever creates a row.
            float minDist = Profile.MinDistance;
            if (count > 0 && minDist > 0f &&
                (curTip - rowTip[0]).sqrMagnitude < minDist * minDist &&
                (curBase - rowBase[0]).sqrMagnitude < minDist * minDist)
            {
                rowTip[0] = curTip;
                rowBase[0] = curBase;
                rowTime[0] = trailTime;
                Prune();
                return;
            }

            if (count < capacity) count++;

            if (count > 1)
            {
                Array.Copy(rowTip, 0, rowTip, 1, count - 1);
                Array.Copy(rowBase, 0, rowBase, 1, count - 1);
                Array.Copy(rowTime, 0, rowTime, 1, count - 1);
            }

            rowTip[0] = curTip;
            rowBase[0] = curBase;
            rowTime[0] = trailTime;

            captures++; // MWC — diagnostics only
            Prune();
        }

        /// <summary> Drops rows older than Duration. Two rows are always kept so the strip stays valid. </summary>
        private void Prune()
        {
            float maxAge = Profile.Duration;
            while (count > 2 && trailTime - rowTime[count - 1] > maxAge) count--;
        }

        /// <summary> Writes vertices and colors, applying Catmull-Rom subdivision and the profile curves. </summary>
        private void BuildMesh()
        {
            var widthCurve = Profile.Width;
            bool flipU = Profile.FlipU;

            // MWC — age now comes from the per-row timestamp instead of the row index, because rows are no
            // longer spaced at a fixed time interval.
            float invDuration = 1f / Mathf.Max(Profile.Duration, 1e-4f);
            int lastRow = Mathf.Max(count, 2) - 1;
            int r = 0;

            // MWC — U is normalised across the rows that are actually live, so the texture always spans the
            // whole ribbon and the tail lands exactly on the far edge. Age is kept separately for the Width
            // curve, which should stay time-based (older = thinner) regardless of how U is mapped.
            bool stretch = Profile.StretchUV;
            int usedRows = lastRow * (subdivisions + 1) + 1;
            float invUsed = 1f / Mathf.Max(1, usedRows - 1);

            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;

            for (int k = 0; k < lastRow; k++)
            {
                int i0 = Mathf.Max(k - 1, 0);
                int i3 = Mathf.Min(k + 2, lastRow);

                Vector3 t0 = rowTip[i0], t1 = rowTip[k], t2 = rowTip[k + 1], t3 = rowTip[i3];
                Vector3 b0 = rowBase[i0], b1 = rowBase[k], b2 = rowBase[k + 1], b3 = rowBase[i3];

                float ageA = (trailTime - rowTime[k]) * invDuration;
                float ageB = (trailTime - rowTime[k + 1]) * invDuration;

                for (int j = 0; j <= subdivisions; j++, r++)
                {
                    float t = (float)j / (subdivisions + 1);
                    float age = Mathf.Clamp01(Mathf.Lerp(ageA, ageB, t));
                    WriteRow(r, CatmullRom(t0, t1, t2, t3, t), CatmullRom(b0, b1, b2, b3, t),
                             age, stretch ? r * invUsed : age, widthCurve, flipU, ref min, ref max);
                }
            }

            Vector3 tailTip = rowTip[lastRow], tailBase = rowBase[lastRow];
            float tailAge = Mathf.Clamp01((trailTime - rowTime[lastRow]) * invDuration);
            WriteRow(r++, tailTip, tailBase, tailAge, stretch ? 1f : tailAge,
                     widthCurve, flipU, ref min, ref max);

            // MWC — collapse the unused rows onto the tail: degenerate triangles at zero alpha, so the
            // index buffer never has to change size.
            for (; r < renderRows; r++)
                WriteRow(r, tailTip, tailBase, 1f, 1f, widthCurve, flipU, ref min, ref max);

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs); // MWC — UVs follow the live row count now, so they update every frame
            // MWC — bounds tracked while writing, so RecalculateBounds never has to walk the vertices again
            mesh.bounds = new Bounds((min + max) * 0.5f, max - min);
        }

        private void WriteRow(int r, Vector3 tip, Vector3 bas, float age, float u01,
                              AnimationCurve widthCurve, bool flipU,
                              ref Vector3 min, ref Vector3 max)
        {
            float w = widthCurve.Evaluate(age);
            Vector3 center = (tip + bas) * 0.5f;

            Vector3 vB = center + (bas - center) * w;
            Vector3 vT = center + (tip - center) * w;

            int i = r * 2;
            vertices[i + 0] = vB;
            vertices[i + 1] = vT;

            // MWC — U runs ALONG the trail (head to tail), V runs ACROSS its width. That is the layout
            // weapon-trail textures are authored in: the gradient along the horizontal axis, feathered
            // edges top and bottom. Driving U from age (not the row index) keeps the mapping correct as
            // the live row count changes -- with a static index-based UV the texture was squeezed into
            // the fraction of the buffer currently in use.
            float u = flipU ? 1f - u01 : u01;
            uvs[i + 0] = new Vector2(u, 0f); // base edge
            uvs[i + 1] = new Vector2(u, 1f); // tip edge

            min = Vector3.Min(min, Vector3.Min(vB, vT));
            max = Vector3.Max(max, Vector3.Max(vB, vT));
        }

        /// <summary> Catmull-Rom interpolation between p1 and p2. Returns p1 at t=0 and p2 at t=1. </summary>
        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            if (t <= 0f) return p1;
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1)
                         + (-p0 + p2) * t
                         + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                         + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        #endregion

        #region Editor support

#if UNITY_EDITOR
        /// <summary> Drives the trail from EditorApplication.update while previewing outside play mode. </summary>
        internal void EditorPreviewTick(float dt)
        {
            if (Application.isPlaying) return;
            if (!EnsureRuntime()) return;
            Tick(dt);
        }

        internal void EditorPreviewStart()
        {
            if (!EnsureRuntime()) return;
            Play(0f);
        }

        internal void EditorPreviewStop()
        {
            playing = false;
            fade = fadeTarget = 0f;
            GoDormant();
            DisposeRuntime();
        }

        private void OnDrawGizmos()
        {
            if (!debug) return;
            if (!InternalEditorUtility.GetIsInspectorExpanded(this)) return; // MWC — framework gizmo idiom

            if (Tip != null && Base != null)
            {
                Gizmos.color = DebugColor;
                Gizmos.DrawLine(Base.position, Tip.position);
                Gizmos.DrawSphere(Tip.position, 0.015f);
                Gizmos.DrawSphere(Base.position, 0.015f);
            }

            if (rowTip == null || IsDormant) return;

            // MWC — one line per captured row: the sample density IS the roundness, so make it visible
            var c = DebugColor; c.a *= 0.6f;
            Gizmos.color = c;
            for (int i = 0; i < count; i++)
            {
                Gizmos.DrawLine(rowBase[i], rowTip[i]);
                Gizmos.DrawSphere(rowTip[i], 0.008f);
            }
        }
#endif
        #endregion
    }

    #region INSPECTOR
#if UNITY_EDITOR
    [CustomEditor(typeof(MWeaponTrail))]
    [CanEditMultipleObjects]
    public class MWeaponTrailEditor : Editor
    {
        private SerializedProperty Tip, Base, Profiles, Index, OnTrailStart, OnTrailEnd, debug, DebugColor;
        private MWeaponTrail M;

        private bool previewing;
        private double lastPreviewTime;

        private void OnEnable()
        {
            M = (MWeaponTrail)target;

            Tip = serializedObject.FindProperty(nameof(MWeaponTrail.Tip));
            Base = serializedObject.FindProperty(nameof(MWeaponTrail.Base));
            Profiles = serializedObject.FindProperty(nameof(MWeaponTrail.Profiles));
            Index = serializedObject.FindProperty(nameof(MWeaponTrail.Index));
            OnTrailStart = serializedObject.FindProperty(nameof(MWeaponTrail.OnTrailStart));
            OnTrailEnd = serializedObject.FindProperty(nameof(MWeaponTrail.OnTrailEnd));
            debug = serializedObject.FindProperty(nameof(MWeaponTrail.debug));
            DebugColor = serializedObject.FindProperty(nameof(MWeaponTrail.DebugColor));
        }

        private void OnDisable() => StopPreview(); // MWC — never leave an EditorApplication.update hook behind

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Procedural trail along a weapon blade. Play/Stop it from " +
                                          "MAttackTrigger's OnAttackBegin/OnAttackEnd, a Reaction, or code");

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(Index);
                }

                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(Profiles, true);
                EditorGUI.indentLevel--;

                DrawPointPositions();

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUIUtility.labelWidth = 30;
                    EditorGUILayout.PropertyField(Tip);
                    EditorGUILayout.PropertyField(Base);
                    EditorGUIUtility.labelWidth = 0;
                    MalbersEditor.DrawDebugIcon(debug);
                }

                if (debug.boolValue) EditorGUILayout.PropertyField(DebugColor);

                DrawSetupButton();
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                OnTrailStart.isExpanded = MalbersEditor.Foldout(OnTrailStart.isExpanded, "Events");

                if (OnTrailStart.isExpanded)
                {
                    EditorGUILayout.PropertyField(OnTrailStart);
                    EditorGUILayout.PropertyField(OnTrailEnd);
                }
            }

            DrawPreview();

            DrawStats();

            serializedObject.ApplyModifiedProperties();
        }

        // MWC — Numeric editing for the two blade points while debugging. Local position, because these are
        // children of the weapon: it stays meaningful wherever the character is standing, and it is the value
        // worth copying between weapons. The Scene handles cover world-space dragging.
        private void DrawPointPositions()
        {
            if (!debug.boolValue || targets.Length > 1) return;

            var trail = (MWeaponTrail)target;
            if (trail.Tip == null || trail.Base == null) return;

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;
                DrawLocalPosition("Tip Position", trail.Tip);
                DrawLocalPosition("Base Position", trail.Base);
                EditorGUI.indentLevel--;
            }
        }

        private static void DrawLocalPosition(string label, Transform point)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 pos = EditorGUILayout.Vector3Field(label, point.localPosition);
            if (!EditorGUI.EndChangeCheck()) return;

            // The Transform is a different Object from the component being inspected, so it needs its own
            // Undo entry and dirty flag, plus a prefab-instance override when it lives in one.
            Undo.RecordObject(point, "Move Weapon Trail Point");
            point.localPosition = pos;
            PrefabUtility.RecordPrefabInstancePropertyModifications(point);
            EditorUtility.SetDirty(point);
        }
        /// <summary> One-click creation of the Tip/Base children, seeded from the weapon's renderer bounds. </summary>
        private void DrawSetupButton()
        {
            if (Tip.objectReferenceValue != null && Base.objectReferenceValue != null) return;
            if (targets.Length > 1) return;

            EditorGUILayout.HelpBox("Tip and Base define the two edges of the ribbon.", MessageType.Info);

            if (!GUILayout.Button("Create Tip and Base Transforms")) return;

            var trail = (MWeaponTrail)target;

            // Seed along the longest local axis of the weapon's renderer, falling back to ±0.5 on local Z.
            Vector3 dir = Vector3.forward;
            float half = 0.5f;

            var rend = trail.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Vector3 ext = trail.transform.InverseTransformVector(rend.bounds.extents);
                ext = new Vector3(Mathf.Abs(ext.x), Mathf.Abs(ext.y), Mathf.Abs(ext.z));

                if (ext.x >= ext.y && ext.x >= ext.z) { dir = Vector3.right; half = ext.x; }
                else if (ext.y >= ext.z) { dir = Vector3.up; half = ext.y; }
                else { dir = Vector3.forward; half = ext.z; }

                half = Mathf.Max(half, 0.05f);
            }

            var tip = CreateChild(trail.transform, "Trail_Tip", dir * half);
            var bas = CreateChild(trail.transform, "Trail_Base", -dir * half * 0.2f);

            Tip.objectReferenceValue = tip;
            Base.objectReferenceValue = bas;

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(trail);
        }

        private static Transform CreateChild(Transform parent, string childName, Vector3 localPos)
        {
            var go = new GameObject(childName);
            Undo.RegisterCreatedObjectUndo(go, "Create Weapon Trail Point");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        // MWC — Edit-mode preview. Scope is deliberate: it drives the same sampling/mesh path from
        // EditorApplication.update so you can drag the weapon in the Scene view and see the ribbon.
        // It does NOT scrub animation clips or drive an Animator.
        private void DrawPreview()
        {
            if (targets.Length > 1) return;

            using (new GUILayout.HorizontalScope())
            {

                GUILayout.FlexibleSpace();
                bool wants = GUILayout.Toggle(previewing, previewing ? "Stop Preview" : "Preview", EditorStyles.miniButton, GUILayout.MaxWidth(140));
                if (wants != previewing)
                {
                    if (wants) StartPreview(); else StopPreview();
                }
                GUILayout.FlexibleSpace();
            }

            if (previewing)
                EditorGUILayout.HelpBox("Move the weapon in the Scene view to see the trail.", MessageType.None);
        }

        private void StartPreview()
        {
            if (Application.isPlaying) return;

            var trail = (MWeaponTrail)target;
            if (trail.Profile == null || trail.Tip == null || trail.Base == null)
            {
                Debug.LogWarning("Weapon Trail preview needs a Profile, a Tip and a Base.", trail);
                return;
            }

            previewing = true;
            lastPreviewTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += PreviewUpdate;
            trail.EditorPreviewStart();
        }

        private void StopPreview()
        {
            if (!previewing) return;

            previewing = false;
            EditorApplication.update -= PreviewUpdate;

            if (target is MWeaponTrail trail && !Application.isPlaying) trail.EditorPreviewStop();
        }

        private void PreviewUpdate()
        {
            if (target is not MWeaponTrail trail || Application.isPlaying) { StopPreview(); return; }

            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - lastPreviewTime);
            lastPreviewTime = now;

            trail.EditorPreviewTick(Mathf.Min(dt, 0.1f));
            SceneView.RepaintAll();
        }

        private void DrawStats()
        {
            if (!debug.boolValue || targets.Length > 1) return;

            var trail = (MWeaponTrail)target;
            var p = trail.Profile;

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (p != null)
                {
                    // MWC — rows needed at common framerates, so under-sampling is obvious before playing
                    EditorGUILayout.LabelField("Budget", $"{p.Capacity} rows max  ({p.RenderRows * 2} verts)");
                    EditorGUILayout.LabelField("Rows at 60 / 144 fps",
                        $"{Mathf.Min(Mathf.CeilToInt(p.Duration * 60f), p.Capacity)} / {Mathf.Min(Mathf.CeilToInt(p.Duration * 144f), p.Capacity)}");
                }
                if (Application.isPlaying)
                {
                    EditorGUILayout.LabelField("Live rows", trail.RowCount.ToString());

                    // MWC — the number that actually diagnoses a faceted trail
                    float cps = trail.CapturesPerSecond, fps = trail.FramesPerSecond;
                    EditorGUILayout.LabelField("Captures / Frames per sec", $"{cps:0} / {fps:0}");

                    if (fps > 1f && cps < fps * 0.75f)
                        EditorGUILayout.HelpBox(
                            "The Animator is advancing slower than the render loop, so it — not this component — " +
                            "is the resolution limit. Raise Duration for more rows, or check the Animator's " +
                            "Update Mode / Culling Mode.", MessageType.Warning);
                }
                if (Application.isPlaying || trail.VertexCount > 0)
                    EditorGUILayout.LabelField("State", trail.IsDormant ? "Dormant" : trail.IsPlaying ? "Playing" : "Fading out");

                // MWC — the generated object lives under the shared container, not under the weapon. Point at it
                // and offer a one-click Select so it can actually be inspected.
                if (trail.TrailObject != null)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Mesh Object", trail.TrailObject.name);
                        if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(60)))
                            Selection.activeGameObject = trail.TrailObject;
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("The mesh object is created on Awake (or when the preview starts) " +
                                            $"under \"{MWeaponTrail.ContainerName}\", named \"{trail.name} Trail\".", MessageType.None);
                }
            }
        }

        private void OnSceneGUI()
        {
            var trail = (MWeaponTrail)target;
            if (trail.Tip == null || trail.Base == null || !debug.boolValue) return;

            Handles.color = trail.DebugColor;
            Handles.DrawLine(trail.Base.position, trail.Tip.position);

            DrawPointHandle(trail.Tip, "Trail Tip");
            DrawPointHandle(trail.Base, "Trail Base");
        }

        private static void DrawPointHandle(Transform point, string undoName)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 pos = Handles.PositionHandle(point.position, point.rotation);
            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(point, undoName);
            point.position = pos;
        }
    }
#endif
    #endregion
}
