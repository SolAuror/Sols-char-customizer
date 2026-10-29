// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    /// <summary> This will manage the steps sounds and tracks for each animal, on each feet there's a Script StepTriger (Basic)  </summary>
    [AddComponentMenu("Malbers/Utilities/Effects - Audio/Step Manager")]
    public class StepsManager : MonoBehaviour, IAnimatorListener
    {
        [Tooltip("Enable Disable the Steps Manager")]
        public bool Active = true;
        [Tooltip("Time to wait to create a new track")]
        public float WaitNextStep = 0.2f;
        [Tooltip("Layer Mask used to find the ground")]
        public LayerReference GroundLayer = new(1);
        [Tooltip("Global Particle System for the Tracks, to have more individual tracks ")]
        public ParticleSystem Tracks;
        private ParticleSystem Instance;
        [Tooltip("Particle System for the Dust")]
        public ParticleSystem Dust;
        [Tooltip("This will instantiate a gameObject instead of using the Particle system")]
        public bool instantiateTracks = false;
        [Tooltip("Create Foot Tracks Particles only on Static GameObjects")]
        public bool GroundIsStatic = false;
        [Tooltip("Volume applied to every foot AudioSource each time a step sound is played")]
        [Range(0, 1)] public float StepsVolume = 0.2f;
        public int DustParticles = 30;

        [Tooltip("Scale of the dust and track particles")]
        public Vector3 Scale = Vector3.one;

        [Tooltip("Sounds to play when the animal creates a track")]
        public AudioClipReference sounds;

        // MWC — Optional surface-aware footsteps. When empty, the [Sounds] field above is used for every surface (legacy behavior)
        [Tooltip("Optional Surface-aware step definitions (sound + particle overrides per surface). When empty, the [Sounds] field above is used for every surface")]
        public MStepSurfaces SurfaceSounds;
        [Tooltip("Distance to Instantiate the tracks on a terrain")]
        public float trackOffset = 0.0085f;

        [Tooltip("Tracks will be on only when the character is on any of these States. When the list is empty, tracks are always on")]
        public IDList<StateID> TracksOnlyOnState;

        [Tooltip("Logs every step (foot, ground, surface entry and sound) to the console")]
        public bool debug;

        [HideInInspector] public int Editor_Tabs1;

        public bool InTrackState { get; set; }

        public List<StepTrigger> Feet { get; set; }

        protected ICharacterAction character;

        // MWC — Surface detection caches (used only when SurfaceSounds is assigned)
        private Dictionary<Collider, StepGroundInfo> groundCache;               // MWC — collider → one-time ground classification
        private Dictionary<StepSurface, ParticleSystem> trackOverrideInstances; // MWC — lazily instantiated per-surface Track overrides
        private Dictionary<StepSurface, ParticleSystem> dustOverrideInstances;  // MWC — lazily instantiated per-surface Dust overrides

        #region Debug Readouts (Play Mode only, shown on the Debug tab)
        /// <summary>Total steps processed since the component was enabled</summary>
        public int StepCount { get; private set; }
        /// <summary>Steps ignored because the manager was not Active or the character was not in a Track State</summary>
        public int SkippedSteps { get; private set; }
        /// <summary>Why the last step was skipped. Empty when the last step was processed</summary>
        public string LastSkipReason { get; private set; } = string.Empty;
        /// <summary>Time.time of the last processed step</summary>
        public float LastStepTime { get; private set; }
        /// <summary>Foot that produced the last step</summary>
        public StepTrigger LastFoot { get; private set; }
        /// <summary>Ground collider the last foot touched</summary>
        public Collider LastGround { get; private set; }
        /// <summary>World position of the last foot when it stepped (used to re-sample the terrain layer on the Debug tab)</summary>
        public Vector3 LastFootPosition { get; private set; }
        /// <summary>Surface entry matched on the last step. Null when no entry matched or no Surface Sounds asset is assigned</summary>
        public StepSurface LastSurface { get; private set; }
        /// <summary>Audio resource played on the last step. Null when nothing was played</summary>
        public AudioResource LastSound { get; private set; }
        /// <summary>Where the last sound came from: Surface Entry, Asset Default, Sounds field, or None</summary>
        public string LastSoundSource { get; private set; } = "None";
        /// <summary>Dust Particle System used on the last step (null when disabled by the surface entry)</summary>
        public ParticleSystem LastDust { get; private set; }
        /// <summary>Tracks Particle System used on the last step (null when disabled by the surface entry)</summary>
        public ParticleSystem LastTracks { get; private set; }
        /// <summary>True when the last step raycast hit the ground collider (tracks and dust were emitted)</summary>
        public bool LastHitGround { get; private set; }
        /// <summary>Hit point of the last step raycast</summary>
        public Vector3 LastHitPoint { get; private set; }
        /// <summary>Hit normal of the last step raycast</summary>
        public Vector3 LastHitNormal { get; private set; }
        /// <summary>Default Tracks instance (the Tracks prefab clone, or the scene Tracks system)</summary>
        public ParticleSystem TracksInstance => Instance;
        #endregion

        protected virtual void Awake()
        {
            if (Tracks != null)
            {
                if (Tracks.gameObject.IsPrefab())
                {
                    Instance = Instantiate(Tracks, transform, false); //Instantiate in case the Track is a prefab
                }
                else
                {
                    Instance = Tracks;
                }
                Instance.transform.localScale = Scale;
            }

            InTrackState = true;

            character = GetComponentInParent<ICharacterAction>();

            // MWC — Prepare the surface detection caches only when the feature is used
            if (SurfaceSounds != null)
            {
                groundCache = new Dictionary<Collider, StepGroundInfo>();
                trackOverrideInstances = new Dictionary<StepSurface, ParticleSystem>();
                dustOverrideInstances = new Dictionary<StepSurface, ParticleSystem>();
            }
        }

        protected virtual void OnEnable()
        {
            if (character != null && TracksOnlyOnState != null) character.OnState += StateChange;

        }

        protected virtual void OnDisable()
        {
            if (character != null && TracksOnlyOnState != null) character.OnState -= StateChange;

            groundCache?.Clear(); // MWC — release cached colliders (scene changes, destroyed grounds)
        }

        /// <summary>Updates InTrackState when the character changes State. An empty list means tracks are always on</summary>
        protected virtual void StateChange(int obj)
        {
            InTrackState = TracksOnlyOnState == null || !TracksOnlyOnState.HasItems || TracksOnlyOnState.Contains(obj);
        }


        /// <summary>Called by a StepTrigger on a foot when it collides with the ground. Plays the step sound and emits dust and tracks</summary>
        public virtual void EnterStep(StepTrigger foot, Collider surface)
        {
            if (!Active) { SkipStep("Steps Manager is not Active"); return; }
            if (!InTrackState) { SkipStep("Character is not in a Track State"); return; } //The character is not on a locomotion or idle

            if (Dust && Dust.gameObject.IsPrefab())
            {
                Dust = Instantiate(Dust, transform, false);             //If is a prefab clone it!
                Dust.transform.localScale = Scale;
            }

            // MWC — Find the Surface entry under the foot (SurfaceTag → Terrain Layer → Material texture). Null keeps the legacy behavior
            StepSurface entry = SurfaceSounds != null ? GetSurfaceEntry(surface, foot.transform.position) : null;

            StepCount++;
            LastSkipReason = string.Empty;
            LastStepTime = Time.time;
            LastFoot = foot;
            LastGround = surface;
            LastFootPosition = foot.transform.position;
            LastSurface = entry;
            LastSound = null;
            LastSoundSource = "None";
            LastHitGround = false;

            //If the track has an AudioSource Component and whe have some audio to play
            if (foot.StepAudio && foot.gameObject.activeInHierarchy && foot.StepAudio.enabled && foot.StepAudio.isActiveAndEnabled)
            {
                // MWC — Sound priority: Surface entry → asset Default Sound → legacy [sounds] field
                AudioResource resource = null;

                if (entry != null && entry.sound != null) { resource = entry.sound; LastSoundSource = "Surface Entry"; }
                else if (SurfaceSounds != null && !SurfaceSounds.DefaultSound.NullOrEmpty()) { resource = SurfaceSounds.DefaultSound.Value; LastSoundSource = "Asset Default Sound"; }
                else if (!sounds.NullOrEmpty()) { resource = sounds.Value; LastSoundSource = "Sounds Field"; }

                if (resource != null)
                {
                    foot.StepAudio.resource = resource;
                    foot.StepAudio.volume = StepsVolume; //Apply the volume on every step so inspector/runtime changes on the manager are always heard
                    foot.StepAudio.Play();
                    LastSound = resource;
                }
            }
            else
            {
                LastSoundSource = foot.StepAudio ? "Audio Source disabled" : "No Audio Source";
            }


            var Ray = new Ray(foot.transform.position, -transform.up);

            var dust = ResolveDust(entry);      // MWC — per-surface Dust override or the default Dust
            var tracks = ResolveTracks(entry);  // MWC — per-surface Tracks override or the default Tracks instance
            LastDust = dust;
            LastTracks = tracks;

            if (surface.Raycast(Ray, out RaycastHit hit, 1))
            {
                LastHitGround = true;
                LastHitPoint = hit.point;
                LastHitNormal = hit.normal;

                var TrackPosition = hit.point;
                TrackPosition.y += trackOffset;

                var TrackRotation = (Quaternion.FromToRotation(-foot.transform.forward, hit.normal) * foot.transform.rotation);

                if (dust)
                {
                    dust.transform.SetPositionAndRotation(TrackPosition, TrackRotation);
                    dust.transform.Rotate(-90, 0, 0);
                    dust.Emit(DustParticles);
                }

                if (tracks)
                {
                    ParticleSystem.EmitParams ptrack = new()
                    {
                        rotation3D = TrackRotation.eulerAngles, //Set The Rotation
                        position = TrackPosition, //Set The Position
                    };

                    if (instantiateTracks)
                    {
                        if ((GroundIsStatic && surface.gameObject.isStatic))
                        {
                            tracks.Emit(ptrack, 1); // MWC — use the resolved (default or per-surface) track system
                        }
                        else
                        {
                            var newtrack = Instantiate(tracks); //Instantiate in case the Track is a refab

                            var ParentFixer = newtrack.transform.SetParentScaleFixer(hit.transform, TrackPosition);


                            ParticleSystem.EmitParams tr = new()
                            {
                                rotation3D = TrackRotation.eulerAngles, //Set The Rotation
                                position = Vector3.zero, //Set The Position
                            };

                            var main = newtrack.main;
                            main.simulationSpace = ParticleSystemSimulationSpace.Local;
                            newtrack.Emit(tr, 1);
                            this.Delay_Action(() => newtrack.isPlaying, () =>
                            {
                                if (ParentFixer != null) Destroy(ParentFixer.gameObject);
                            });
                        }
                    }
                    else
                    {
                        tracks.Emit(ptrack, 1); // MWC — use the resolved (default or per-surface) track system
                    }
                }
            }

            if (debug)
            {
                Debug.Log($"<B>[{name}]</B> Step <B>[{foot.name}]</B> on <B>[{surface.name}]</B> | Surface Entry: [{(entry != null ? entry.name : "None")}] " +
                    $"| Sound: [{(LastSound ? LastSound.name : "None")}] ({LastSoundSource}) | Dust: [{(dust ? dust.name : "None")}] | Tracks: [{(tracks ? tracks.name : "None")}] " +
                    $"| Ground Hit: {LastHitGround}", this);
            }
        }

        /// <summary>Stores why a step was ignored so the Debug tab can show it</summary>
        private void SkipStep(string reason)
        {
            SkippedSteps++;
            LastSkipReason = reason;
            if (debug) Debug.Log($"<B>[{name}]</B> Step skipped: {reason}", this);
        }

        /// <summary>Finds the Surface entry under the foot. Priority: SurfaceTag (ISurface) → Terrain Layer → Material main texture</summary>
        protected virtual StepSurface GetSurfaceEntry(Collider surface, Vector3 footPos)
        {
            groundCache ??= new Dictionary<Collider, StepGroundInfo>(); //In case SurfaceSounds was assigned at runtime after Awake
            return SurfaceResolver.Resolve(SurfaceSounds, surface, footPos, groundCache);
        }

        /// <summary>Returns the albedo/main texture of a material without logging errors when the shader lacks _MainTex</summary>
        protected static Texture GetMaterialMainTexture(Material mat) => SurfaceResolver.GetMaterialMainTexture(mat);

        /// <summary>Classifies a ground collider (SurfaceTag, Terrain, or Mesh Renderer). Called once per collider, then cached</summary>
        protected virtual StepGroundInfo ClassifyGround(Collider surface) => SurfaceResolver.ClassifyGround(surface);

        /// <summary>Samples the dominant Terrain Layer texture at the foot position (1x1 alphamap read)</summary>
        protected virtual Texture SampleDominantLayer(StepGroundInfo info, Vector3 footPos) => SurfaceResolver.SampleDominantLayer(info, footPos);

        // MWC — Resolves the Dust for a surface entry. None → default Dust | Disable → null (no dust) | Override → custom instance
        /// <summary>Resolves the Dust Particle System for a surface entry. None uses the default Dust, Disable returns null, Override uses the custom instance</summary>
        protected virtual ParticleSystem ResolveDust(StepSurface entry)
        {
            if (entry == null || entry.DustMode == StepParticleMode.None) return Dust;
            if (entry.DustMode == StepParticleMode.Disable) return null;
            if (entry.DustOverride == null) return Dust; //Override selected but empty → fall back to the default
            return GetOverrideInstance(entry.DustOverride, entry, ref dustOverrideInstances);
        }

        // MWC — Resolves the Tracks for a surface entry. None → default Tracks | Disable → null (no tracks) | Override → custom instance
        /// <summary>Resolves the Tracks Particle System for a surface entry. None uses the default Tracks, Disable returns null, Override uses the custom instance</summary>
        protected virtual ParticleSystem ResolveTracks(StepSurface entry)
        {
            if (entry == null || entry.TrackMode == StepParticleMode.None) return Instance;
            if (entry.TrackMode == StepParticleMode.Disable) return null;
            if (entry.TrackOverride == null) return Instance; //Override selected but empty → fall back to the default
            return GetOverrideInstance(entry.TrackOverride, entry, ref trackOverrideInstances);
        }

        // MWC — Lazily instantiates a particle override (mirrors the default Tracks/Dust prefab handling) and caches it per entry
        /// <summary>Lazily instantiates a per-surface particle override (prefab clone or scene system) and caches it per entry</summary>
        private ParticleSystem GetOverrideInstance(ParticleSystem source, StepSurface entry, ref Dictionary<StepSurface, ParticleSystem> cache)
        {
            cache ??= new Dictionary<StepSurface, ParticleSystem>(); //In case SurfaceSounds was assigned at runtime after Awake

            if (!cache.TryGetValue(entry, out var instance) || instance == null)
            {
                instance = source.gameObject.IsPrefab() ? Instantiate(source, transform, false) : source;
                instance.transform.localScale = Scale;
                cache[entry] = instance;
            }
            return instance;
        }

        /// <summary>Disable this sfcript, e.g.. deactivate when is sleeping or death </summary>
        public virtual void EnableSteps(bool value) => Active = value;

        /// <summary>Receives messages sent from Animator Behaviours and invokes the matching method by name</summary>
        public virtual bool OnAnimatorBehaviourMessage(string message, object value) => this.InvokeWithParams(message, value);
    }

#if UNITY_EDITOR
    /// <summary>Tabbed inspector for the Steps Manager: General, Tracks & Dust, and a live Debug readout</summary>
    [CanEditMultipleObjects, CustomEditor(typeof(StepsManager))]
    public class StepsManagerEditor : Editor
    {
        StepsManager M;

        SerializedProperty Active, WaitNextStep, GroundLayer, Tracks, Dust, instantiateTracks, GroundIsStatic, StepsVolume, DustParticles,
            Scale, sounds, SurfaceSounds, trackOffset, TracksOnlyOnState, debug, Editor_Tabs1;

        readonly string[] Tabs = { "General", "Debug" };

        /// <summary>Caches the target and every serialized property drawn by the inspector</summary>
        protected virtual void OnEnable()
        {
            M = (StepsManager)target;

            Active = serializedObject.FindProperty(nameof(M.Active));
            WaitNextStep = serializedObject.FindProperty(nameof(M.WaitNextStep));
            GroundLayer = serializedObject.FindProperty(nameof(M.GroundLayer));
            Tracks = serializedObject.FindProperty(nameof(M.Tracks));
            Dust = serializedObject.FindProperty(nameof(M.Dust));
            instantiateTracks = serializedObject.FindProperty(nameof(M.instantiateTracks));
            GroundIsStatic = serializedObject.FindProperty(nameof(M.GroundIsStatic));
            StepsVolume = serializedObject.FindProperty(nameof(M.StepsVolume));
            DustParticles = serializedObject.FindProperty(nameof(M.DustParticles));
            Scale = serializedObject.FindProperty(nameof(M.Scale));
            sounds = serializedObject.FindProperty(nameof(M.sounds));
            SurfaceSounds = serializedObject.FindProperty(nameof(M.SurfaceSounds));
            trackOffset = serializedObject.FindProperty(nameof(M.trackOffset));
            TracksOnlyOnState = serializedObject.FindProperty(nameof(M.TracksOnlyOnState));
            debug = serializedObject.FindProperty(nameof(M.debug));
            Editor_Tabs1 = serializedObject.FindProperty(nameof(M.Editor_Tabs1));
        }

        /// <summary>Repaints every frame in Play Mode while the Debug tab shows its live readout</summary>
        public override bool RequiresConstantRepaint() => Application.isPlaying && Editor_Tabs1 != null && Editor_Tabs1.intValue == 1;

        /// <summary>Draws the tabbed inspector</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Plays step sounds and emits Dust and Tracks particles when a Step Trigger on a foot touches the ground");

            using (var cc = new EditorGUI.ChangeCheckScope())
            {
                if (Editor_Tabs1.intValue >= Tabs.Length) Editor_Tabs1.intValue = 0; //Objects saved with an older tab layout
                Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tabs);

                int Selection = Editor_Tabs1.intValue;

                if (Selection == 0) ShowGeneral();
                else if (Selection == 1) ShowDebug();

                if (cc.changed) Undo.RecordObject(target, "Steps Manager Changed");
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>General tab: activation, ground detection, track states, sound sources, and the Tracks and Dust particles</summary>
        private void ShowGeneral()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                WaitNextStep.isExpanded = MalbersEditor.Foldout(WaitNextStep.isExpanded, "General");

                if (WaitNextStep.isExpanded)
                {
                    EditorGUILayout.PropertyField(Active);
                    EditorGUILayout.PropertyField(WaitNextStep, new GUIContent("Wait Next Step", WaitNextStep.tooltip));
                    EditorGUILayout.PropertyField(GroundLayer, new GUIContent("Ground Layer", GroundLayer.tooltip));

                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(TracksOnlyOnState, new GUIContent("Tracks Only On States", TracksOnlyOnState.tooltip));
                    EditorGUI.indentLevel--;
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                sounds.isExpanded = MalbersEditor.Foldout(sounds.isExpanded, "Sounds");

                if (sounds.isExpanded)
                {
                    EditorGUILayout.PropertyField(StepsVolume, new GUIContent("Steps Volume", StepsVolume.tooltip));
                    EditorGUILayout.PropertyField(SurfaceSounds, new GUIContent("Surface Sounds", SurfaceSounds.tooltip));
                    EditorGUILayout.PropertyField(sounds, new GUIContent("Sounds", sounds.tooltip));

                    if (M.SurfaceSounds != null)
                    {
                        EditorGUILayout.HelpBox("Sound priority: Surface Entry → Surface Sounds [Default Sound] → [Sounds]", MessageType.Info);
                    }
                    else if (M.sounds.NullOrEmpty())
                    {
                        EditorGUILayout.HelpBox("No Sounds assigned. Steps will emit Dust and Tracks only", MessageType.Warning);
                    }
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Tracks.isExpanded = MalbersEditor.Foldout(Tracks.isExpanded, "Tracks");

                if (Tracks.isExpanded)
                {
                    EditorGUILayout.PropertyField(Tracks, new GUIContent("Tracks", Tracks.tooltip));
                    EditorGUILayout.PropertyField(trackOffset, new GUIContent("Track Offset", trackOffset.tooltip));
                    EditorGUILayout.PropertyField(instantiateTracks, new GUIContent("Instantiate Tracks", instantiateTracks.tooltip));

                    if (instantiateTracks.boolValue)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(GroundIsStatic, new GUIContent("Ground Is Static", GroundIsStatic.tooltip));
                        EditorGUI.indentLevel--;
                    }
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Dust.isExpanded = MalbersEditor.Foldout(Dust.isExpanded, "Dust");

                if (Dust.isExpanded)
                {
                    EditorGUILayout.PropertyField(Dust, new GUIContent("Dust", Dust.tooltip));
                    EditorGUILayout.PropertyField(DustParticles, new GUIContent("Dust Particles", "Amount of particles emitted from the Dust system on every step"));
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(Scale, new GUIContent("Scale", Scale.tooltip));
            }
        }

        /// <summary>Debug tab: console toggle, feet setup check, and the live readout of the last step</summary>
        private void ShowDebug()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUIUtility.labelWidth = 60f;
                    EditorGUILayout.PropertyField(debug, new GUIContent("Console", debug.tooltip));
                    EditorGUIUtility.labelWidth = 0f;

                    if (Application.isPlaying)
                    {
                        if (GUILayout.Button(new GUIContent("Simulate Step", "Raycasts down from the first foot using the Ground Layer and calls Enter Step"), EditorStyles.miniButton, GUILayout.Width(100)))
                            SimulateStep();
                    }
                }
            }

            ShowFeetSetup();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see the live step readout", MessageType.Info);
                return;
            }

            ShowLiveReadout();
        }

        /// <summary>Lists the Step Triggers found under the character and flags missing Audio Sources, Triggers or manager references</summary>
        private void ShowFeetSetup()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var feet = FindFeet();

                EditorGUILayout.LabelField($"Feet [{feet.Count}]", EditorStyles.boldLabel);

                if (feet.Count == 0)
                {
                    EditorGUILayout.HelpBox("No Step Trigger was found for this manager. Add a Step Trigger to every foot", MessageType.Warning);
                    return;
                }

                if (!Application.isPlaying)
                    EditorGUILayout.LabelField("Audio Sources are added automatically on Awake when missing", EditorStyles.miniLabel);

                EditorGUI.indentLevel++;

                foreach (var foot in feet)
                {
                    if (foot == null) continue;

                    var hasAudio = foot.StepAudio != null || foot.GetComponent<AudioSource>() != null;
                    var trigger = foot.m_Trigger != null ? foot.m_Trigger : foot.GetComponent<SphereCollider>();
                    var hasTrigger = trigger != null && trigger.isTrigger;
                    var playing = Application.isPlaying && foot.StepAudio != null && foot.StepAudio.isPlaying;

                    var ok = hasTrigger && (hasAudio || !Application.isPlaying); //Missing audio is only a problem in Play Mode (added on Awake)
                    var dC = GUI.color;
                    GUI.color = ok ? dC : new Color(1f, 0.6f, 0.6f);

                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUILayout.ObjectField(foot, typeof(StepTrigger), true, GUILayout.MinWidth(80));

                        var indent = EditorGUI.indentLevel;
                        EditorGUI.indentLevel = 0; //The indent shifts each toggle inside its fixed width and clips the label
                        EditorGUILayout.ToggleLeft(new GUIContent("Audio", "The foot has an Audio Source"), hasAudio, Bold(hasAudio), GUILayout.Width(64));
                        EditorGUILayout.ToggleLeft(new GUIContent("Trigger", "The foot has a Sphere Collider set as Trigger"), hasTrigger, Bold(hasTrigger), GUILayout.Width(72));
                        if (Application.isPlaying)
                            EditorGUILayout.ToggleLeft(new GUIContent("Playing", "The foot Audio Source is playing"), playing, Bold(playing), GUILayout.Width(72));
                        EditorGUI.indentLevel = indent;
                    }

                    GUI.color = dC;
                }

                EditorGUI.indentLevel--;
            }
        }

        /// <summary>Feet registered on this manager in Play Mode, or the Step Triggers that will resolve to this manager on Awake at edit time</summary>
        private List<StepTrigger> FindFeet()
        {
            if (Application.isPlaying && M.Feet != null) return M.Feet;

            var result = new List<StepTrigger>();

            foreach (var foot in M.transform.root.GetComponentsInChildren<StepTrigger>(true))
            {
                var manager = foot.m_StepsManager != null ? foot.m_StepsManager : foot.transform.FindObjectCore().FindComponent<StepsManager>(); //Same resolution as StepTrigger.Awake
                if (manager == M) result.Add(foot);
            }
            return result;
        }

        /// <summary>Live readout of the last processed step: counters, foot, ground classification, surface entry, sound and particles</summary>
        private void ShowLiveReadout()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);

                using (new GUILayout.HorizontalScope())
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.ToggleLeft("Active", M.Active, Bold(M.Active));
                        EditorGUILayout.ToggleLeft("In Track State", M.InTrackState, Bold(M.InTrackState));
                        EditorGUILayout.ToggleLeft("Surface Sounds", M.SurfaceSounds != null, Bold(M.SurfaceSounds != null));
                    }
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.ToggleLeft("Tracks Instance", M.TracksInstance != null, Bold(M.TracksInstance != null));
                        EditorGUILayout.ToggleLeft("Dust", M.Dust != null, Bold(M.Dust != null));
                        EditorGUILayout.ToggleLeft("Last Hit Ground", M.LastHitGround, Bold(M.LastHitGround));
                    }
                }

                using (new EditorGUI.DisabledGroupScope(true))
                {
                    EditorGUIUtility.labelWidth = 120f;
                    EditorGUILayout.IntField("Step Count", M.StepCount);
                    EditorGUILayout.IntField("Skipped Steps", M.SkippedSteps);
                    if (!string.IsNullOrEmpty(M.LastSkipReason))
                        EditorGUILayout.TextField("Last Skip Reason", M.LastSkipReason);

                    if (M.StepCount > 0)
                        EditorGUILayout.FloatField("Since Last Step", Time.time - M.LastStepTime);
                    EditorGUIUtility.labelWidth = 0f;
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Last Step", EditorStyles.boldLabel);

                if (M.StepCount == 0)
                {
                    EditorGUILayout.HelpBox("Waiting for the first step", MessageType.None);
                    return;
                }

                using (new EditorGUI.DisabledGroupScope(true))
                {
                    EditorGUIUtility.labelWidth = 120f;
                    EditorGUILayout.ObjectField("Foot", M.LastFoot, typeof(StepTrigger), true);
                    EditorGUILayout.ObjectField("Ground", M.LastGround, typeof(Collider), true);
                    EditorGUILayout.TextField("Ground Type", GroundTypeLabel(M.LastGround, M.LastFootPosition, out var groundTexture));
                    if (groundTexture != null)
                        EditorGUILayout.ObjectField("Ground Texture", groundTexture, typeof(Texture), false);

                    EditorGUILayout.Space();
                    EditorGUILayout.TextField("Surface Entry", M.LastSurface != null ? M.LastSurface.name : "None");
                    EditorGUILayout.ObjectField("Sound", M.LastSound, typeof(AudioResource), false);
                    EditorGUILayout.TextField("Sound Source", M.LastSoundSource);

                    EditorGUILayout.Space();
                    EditorGUILayout.ObjectField("Dust", M.LastDust, typeof(ParticleSystem), true);
                    EditorGUILayout.ObjectField("Tracks", M.LastTracks, typeof(ParticleSystem), true);

                    if (M.LastHitGround)
                    {
                        EditorGUILayout.Vector3Field("Hit Point", M.LastHitPoint);
                        EditorGUILayout.Vector3Field("Hit Normal", M.LastHitNormal);
                    }
                    EditorGUIUtility.labelWidth = 0f;
                }

                if (M.SurfaceSounds != null && M.LastSurface == null)
                    EditorGUILayout.HelpBox("The last ground matched no Surface Entry. Check its Surface Tag, Terrain Layer or Material texture against the Surface Sounds asset", MessageType.Info);
            }
        }

        /// <summary>Describes how the ground collider was classified (Surface Tag, Terrain layer, Mesh material) and returns the texture that was matched</summary>
        private string GroundTypeLabel(Collider ground, Vector3 footPos, out Texture texture)
        {
            texture = null;
            if (ground == null) return "None";

            var info = SurfaceResolver.ClassifyGround(ground);

            if (info.Tag != null && info.Tag.Surface != null)
                return $"Surface Tag [{info.Tag.Surface.name}]";

            if (info.Data != null)
            {
                texture = SurfaceResolver.SampleDominantLayer(info, footPos);
                return $"Terrain [{info.Terrain.name}]";
            }

            if (info.Renderer != null)
            {
                var mat = info.Renderer.sharedMaterial;
                texture = SurfaceResolver.GetMaterialMainTexture(mat);
                return $"Mesh [{info.Renderer.name}] Material [{(mat ? mat.name : "None")}]";
            }

            return "Unknown (no Surface Tag, Terrain or Renderer)";
        }

        /// <summary>Raycasts down from the first foot on the Ground Layer and calls Enter Step, to test the manager without moving the character</summary>
        private void SimulateStep()
        {
            var foot = M.LastFoot != null ? M.LastFoot : (M.Feet != null && M.Feet.Count > 0 ? M.Feet[0] : null);

            if (foot == null)
            {
                Debug.LogWarning($"[{M.name}] Simulate Step: no Step Trigger registered on the Steps Manager", M);
                return;
            }

            var origin = foot.transform.position + M.transform.up * 0.5f;

            if (Physics.Raycast(origin, -M.transform.up, out var hit, 2f, M.GroundLayer.Value, QueryTriggerInteraction.Ignore))
            {
                M.EnterStep(foot, hit.collider);
            }
            else
            {
                Debug.LogWarning($"[{M.name}] Simulate Step: no ground found below [{foot.name}] on the Ground Layer", M);
            }
        }

        /// <summary>Bold label when true, mini bold label when false</summary>
        protected GUIStyle Bold(bool tru) => tru ? EditorStyles.boldLabel : EditorStyles.miniBoldLabel;
    }
#endif
}
