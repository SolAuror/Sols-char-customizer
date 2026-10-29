using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Controller
{
    /// <summary>  This will control all Animals Motion
    /// See changelog here https://malbersanimations.gitbook.io/animal-controller/annex/changelog
    /// </summary>

    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/main-components/manimal-controller")]
    [DefaultExecutionOrder(-10)]
    [SelectionBase]
    [DisallowMultipleComponent]
    [AddComponentMenu("Malbers/Animal Controller/Animal Controller")]
    public partial class MAnimal : MonoBehaviour,
        IAnimatorListener, ICharacterMove, IGravity, IObjectCore,
        IRandomizer, IMAnimator, ISleepController, IMDamagerSet, ILockCharacter,
        IAnimatorStateCycle, ICharacterAction, IDeltaRootMotion
    {
        //Animal Variables: All variables
        //Animal Movement:  All Locomotion Logic
        //Animal CallBacks: All public methods and behaviors that it can be called outside the script

        #region Editor Show 

        [HideInInspector, SerializeField] private bool ShowOnPlay;
        [HideInInspector, SerializeField] private int PivotPosDir;
        [HideInInspector, SerializeField] private int SelectedState;
        [HideInInspector, SerializeField] private int SelectedStance;

        [HideInInspector, SerializeField] internal bool ShowStateInInspector = false;

#pragma warning disable 414
        [HideInInspector, SerializeField] private int Editor_Tabs1;
        [HideInInspector, SerializeField] private int Editor_Tabs2;


        //Modes
        [HideInInspector, SerializeField] private int SelectedMode;
        [HideInInspector, SerializeField] private int Mode_Tabs1;
        [HideInInspector, SerializeField] private int Ability_Tabs;
        [HideInInspector, SerializeField] private int Editor_EventTabs;

        //Inspector Variables
        [HideInInspector, SerializeField] private bool showPivots = true;
        [HideInInspector, SerializeField] private bool showModeList = true;
        [HideInInspector, SerializeField] private bool showStateList = true;
        [HideInInspector, SerializeField] private bool ShowOnGUIData = false;
#pragma warning restore 414

        [HideInInspector, SerializeField] internal bool debugStates;
        [HideInInspector, SerializeField] internal bool debugStances;
        [HideInInspector, SerializeField] internal bool debugModes;
        [HideInInspector, SerializeField] internal bool debugGizmos = true;

        [HideInInspector, SerializeField] private int Runtime_Tabs1;
        [HideInInspector, SerializeField] private int Runtime_Tabs2;
        [HideInInspector, SerializeField] private int SpeedTabs;
        [HideInInspector, SerializeField] private int SelectedSpeed;
        #endregion

#if UNITY_EDITOR
        private void OnValidate()
        {
            CacheComponents();
            SetDefaultMainColliderValues();


            foreach (var stance in Stances)
            {
                stance.OnValidate(this);
            }
        }

        private void Reset()
        {
            MTools.SetLayer(transform, 20);     //Set all the Children to Animal Layer   .
            gameObject.tag = "Animal";  //Set the Animal to Tag Animal
            AnimatorSpeed = 1;

            Anim = this.FindComponent<Animator>();            //Cache the Animator
            RB = this.FindComponent<Rigidbody>();             //Cache the Rigid Body  

            if (RB == null)
            {
                RB = gameObject.AddComponent<Rigidbody>();
                RB.useGravity = false;
                RB.constraints = RigidbodyConstraints.FreezeRotation;
                RB.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                RB.interpolation = RigidbodyInterpolation.Interpolate; //MWC: removes visual jitter with LateUpdate cameras (Animator runs in Fixed mode). Applied only to newly created Rigidbodies
            }

            if (!Anim)
            {
                Anim = gameObject.AddComponent<Animator>();
            }

            if (AnimalMaterial == null)
                AnimalMaterial = MTools.GetInstance<PhysicsMaterial>("Flesh");

            Anim.updateMode = AnimatorUpdateMode.Fixed; //Set the Animator to Animate Physics
            Anim.animatePhysics = true; //Set the Animator to Animate Physics

            speedSets = new List<MSpeedSet>(1)
            {
                new()
            {
                name = "Ground",
                    StartVerticalIndex = new(1),
                    TopIndex = new(3),
                    states =  new  List<StateID>(2) { MTools.GetInstance<StateID>("Idle") , MTools.GetInstance<StateID>("Locomotion")},
                    Speeds =  new  List<MSpeed>(3) { new ("Walk",1,4,4) , new ("Trot", 2, 4, 4), new ("Run", 3, 4, 4) }
            }
            };

            BoolVar useCameraInp = MTools.GetInstance<BoolVar>("Global Camera Input");
            BoolVar globalSmooth = MTools.GetInstance<BoolVar>("Global Smooth Vertical");
            FloatVar globalTurn = MTools.GetInstance<FloatVar>("Global Turn Multiplier");

            if (useCameraInp != null) useCameraInput.Variable = useCameraInp;
            if (globalSmooth != null) SmoothVertical.Variable = globalSmooth;
            if (globalTurn != null) TurnMultiplier.Variable = globalTurn;

            CreateDefaultStance();

            pivots = new List<MPivots>
            {
                new("Hip", new Vector3(0,0.7f,-0.7f), 1),
                new("Chest", new Vector3(0,0.7f,0.7f), 1),
                new("Water", new Vector3(0,1,0), 0.05f)
            };

            MTools.SetDirty(this);
        }

        [ContextMenu("Create Default Stance")]
        private void CreateDefaultStance()
        {
            var DefStance = MTools.GetInstance<StanceID>("Default");

            if (defaultStance == null) defaultStance = DefStance;
            if (currentStance == null) currentStance = DefStance;

            var DefaultStance = new Stance() { ID = defaultStance, CanStrafe = new BoolReference(true) };

            Stances = new List<Stance>() {
                 DefaultStance
            };
        }

        [ContextMenu("Events/Damage Event")]
        private void CreateDamageEventListener()
        {
            MEventListener listener = this.FindComponent<MEventListener>();

            if (listener == null) listener = gameObject.AddComponent<MEventListener>();
            listener.Events ??= new List<MEventItemListener>();
            SetModesListeners(listener, "Set Damage", "Damage");
        }

        [ContextMenu("Create Event Listeners")]
        private void CreateListeners()
        {
            MEventListener listener = this.FindComponent<MEventListener>();

            if (listener == null) listener = gameObject.AddComponent<MEventListener>();
            listener.Events ??= new List<MEventItemListener>();

            MEvent MovementMobile = MTools.GetInstance<MEvent>("Set Movement Mobile");
            if (listener.Events.Find(item => item.Event == MovementMobile) == null)
            {
                var item = new MEventItemListener()
                {
                    Event = MovementMobile,
                    useVoid = true,
                    useVector2 = true,
                };

                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.ResponseVector2, SetInputAxis);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.Response, UseCameraBasedInput);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.ResponseFloat, SetUpDownAxis);

                listener.Events.Add(item);

                Debug.Log("<B>Set Movement Mobile</B> Added to the Event Listeners");
            }

            //********************************//

            SetModesListeners(listener, "Set Attack1", "Attack1");
            SetModesListeners(listener, "Set Attack2", "Attack2");
            SetModesListeners(listener, "Set Action", "Action");
            SetModesListeners(listener, "Set Set Damage", "Damage");

            /************************/

            MEvent actionStatus = MTools.GetInstance<MEvent>("Set Action Status");
            if (listener.Events.Find(item => item.Event == actionStatus) == null)
            {
                var item = new MEventItemListener()
                {
                    Event = actionStatus,
                    useVoid = false,
                    useInt = true,
                    useFloat = true
                };

                ModeID ac = MTools.GetInstance<ModeID>("Action");
                UnityEditor.Events.UnityEventTools.AddObjectPersistentListener(item.ResponseInt, Mode_Pin, ac);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.ResponseInt, Mode_Pin_Status);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.ResponseFloat, Mode_Pin_Time);

                listener.Events.Add(item);

                Debug.Log("<B>Set Action Status</B> Added to the Event Listeners");
            }
            /************************/

            MEvent sprinting = MTools.GetInstance<MEvent>("Set Sprint");
            if (listener.Events.Find(item => item.Event == sprinting) == null)
            {
                var item = new MEventItemListener()
                {
                    Event = sprinting,
                    useVoid = false,
                    useBool = true,
                };

                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.ResponseBool, SetSprint);

                listener.Events.Add(item);

                Debug.Log("<B>Sprint Listener</B> Added to the Event Listeners");
            }

            MEvent timeline = MTools.GetInstance<MEvent>("Timeline");
            if (listener.Events.Find(item => item.Event == timeline) == null)
            {
                var item = new MEventItemListener()
                {
                    Event = timeline,
                    useVoid = false,
                    useBool = true,
                };

                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.ResponseBool, SetTimeline);

                listener.Events.Add(item);

                Debug.Log("<B>Timeline Listener</B> Added to the Event Listeners");
            }


            /************************/
            SetStateListeners(listener, "Set Jump", "Jump");
            SetStateListeners(listener, "Set Fly", "Fly");
            /************************/
        }
        private void SetModesListeners(MEventListener listener, string EventName, string ModeName)
        {
            MEvent e = MTools.GetInstance<MEvent>(EventName);
            if (listener.Events.Find(item => item.Event == e) == null)
            {
                var item = new MEventItemListener()
                {
                    Event = e,
                    useVoid = true,
                    useInt = true,
                    useBool = true,
                };

                ModeID att2 = MTools.GetInstance<ModeID>(ModeName);

                UnityEditor.Events.UnityEventTools.AddObjectPersistentListener<ModeID>(item.ResponseBool, Mode_Pin, att2);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.ResponseBool, Mode_Pin_Input);
                UnityEditor.Events.UnityEventTools.AddObjectPersistentListener<ModeID>(item.ResponseInt, Mode_Pin, att2);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.ResponseInt, Mode_Pin_Ability);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.Response, Mode_Interrupt);

                listener.Events.Add(item);

                Debug.Log("<B>" + EventName + "</B> Added to the Event Listeners");
            }
        }

        private void SetStateListeners(MEventListener listener, string EventName, string stateName)
        {
            MEvent e = MTools.GetInstance<MEvent>(EventName);
            if (listener.Events.Find(item => item.Event == e) == null)
            {
                var item = new MEventItemListener()
                {
                    Event = e,
                    useVoid = false,
                    useInt = true,
                    useBool = true,
                };

                StateID ss = MTools.GetInstance<StateID>(stateName);

                UnityEditor.Events.UnityEventTools.AddObjectPersistentListener<StateID>(item.ResponseBool, State_Pin, ss);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.ResponseBool, State_Pin_ByInput);

                listener.Events.Add(item);

                Debug.Log("<B>" + EventName + "</B> Added to the Event Listeners");
            }
        }

        //#if MALBERS_DEBUG


        private void OnGUI()
        {
            if (!ShowOnGUIData) return;

            if (Editor_Tabs2 == 3 && Application.isPlaying && Selection.gameObjects.Length == 1 && Selection.gameObjects[0] == gameObject
#if UNITY_EDITOR
                &&
             UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(this)  //Show Gizmos only when the Inspector is Open
#endif
                )
            {
                GUILayout.Space(30);

                GUILayout.BeginVertical("Box");
                {
                    GUILayout.Label($"<B>Debug</B> <color=yellow><B>[{name}]</B> </color>");
                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical("Box");
                {
                    string MLabel(string value) => $"<B>{value}</B>";
                    string MValue(string value) => $"<color=yellow><B>{value}</B> </color>";

                    using (new GUILayout.HorizontalScope())
                    {
                        //Labels
                        using (new GUILayout.VerticalScope())
                        {
                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel("[Vert,Horiz,UpDown]"));

                            //using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            //    GUILayout.Label(MLabel("Horizontal"));
                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel("Delta Angle"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel("[Pitch,Bank]"));

                            //using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            //    GUILayout.Label(MLabel("UpDown"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel("State"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel("Status [Enter,Exit]"));

                            //using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            //    GUILayout.Label(MLabel("LastState"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel("Mode"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel("Stance"));

                            //using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            //    GUILayout.Label(MLabel("Last Stance"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel($"Movement [{(MovementDetected ? "●" : "  ")}]"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel($"Free Move [{(FreeMovement ? "●" : "  ")}]"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel($"Strafe [{(Sprint ? "●" : "  ")}]"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel($"Additive Pos[{(UseAdditivePos ? "●" : "  ")}]"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel($"Speed Modifier"));


                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel($"Horizontal Speed"));


                        }

                        using (new GUILayout.VerticalScope())
                        {
                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MValue($"[{VerticalSmooth:F2},{HorizontalSmooth:F2},{UpDownSmooth:F2}]"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MValue($"{DeltaAngle:F2}"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MValue($"[{PitchAngle:F2},{Bank:F2}]"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MValue($"[{ActiveStateID.ID}] - {ActiveStateID.name} | Last [{LastState.ID.ID}]"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MValue($"[{StateEnterStatus},{StateExitStatus}]"));


                            //using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            //    GUILayout.Label(MValue($"{LastState.ID.ID} - {LastState.ID.name}"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MValue($"{(activeMode != null ? ($"[{ModeAbility}] - [{activeMode.Name}] - [{activeMode.ActiveAbility.Name}] ") : 0)}"));


                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MValue($"[{Stance.ID}] - {Stance.name} | Last [{LastActiveStance.ID.ID}]"));


                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel($"Sprint [{(Sprint ? "●" : "  ")}]"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel($"Gravity [{(UseGravity ? "●" : "  ")}]"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel($"Grounded [{(Grounded ? "●" : "  ")}]"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MLabel($"Additive Rot[{(UseAdditiveRot ? "●" : "  ")}]"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MValue($"{CurrentSpeedModifier.name}"));

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                GUILayout.Label(MValue($"{HorizontalSpeed:F3}"));

                        }
                    }
                }
                GUILayout.EndVertical();
            }
        }

        [SerializeField] private Mesh CapsuleMesh;

        /// <summary>Gizmo color for the State Override Capsule (blueish)</summary>
        private static readonly Color StateCapsuleColor = new(0.2f, 0.5f, 1f);
        /// <summary>Gizmo color for the Stance Override Capsule (orange-red)</summary>
        private static readonly Color StanceCapsuleColor = new(1f, 0.35f, 0f);
        /// <summary>Gizmo color for the Mode Ability Override Capsule (green-yellow)</summary>
        private static readonly Color AbilityCapsuleColor = new(0.65f, 1f, 0.15f);

        /// <summary>Draws an Override Capsule (State, Stance or Mode Ability) as a translucent mesh with a solid outline, like the Main Collider gizmo</summary>
        private void DrawOverrideCapsule(OverrideCapsuleCollider col, Color color, float scale)
        {
            color.a = 1f;
            var fill = color;
            fill.a = 0.2f;
            var center = transform.TransformPoint(col.center);

            MDebug.GizmoCapsuleMesh(ref CapsuleMesh, center, transform.rotation, col.height, col.radius, fill, col.direction, 16, scale);
            MDebug.GizmoCapsule(center, transform.rotation, col.height, col.radius, color, col.direction, 16, scale);
        }

        private void OnDrawGizmosSelected()
        {
            if (!debugGizmos) return;
            if (!UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(this)) return;

            float sc = transform.localScale.y;
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(Center, 0.02f * sc);
            Gizmos.DrawWireSphere(Center, 0.02f * sc);

            //Draw Capsule Collider on Editor
            if (MainCollider != null)
            {
                //Draw the Capsule Collider on Stances
                if (Editor_Tabs1 == 3 && SelectedStance >= 0)
                {
                    var currentStance = Stances[SelectedStance];
                    if (currentStance.OverrideCapsule)
                        DrawOverrideCapsule(currentStance.newCapsule, StanceCapsuleColor, sc);
                }


                //Draw the Capsule collider on States
                if (Editor_Tabs1 == 1)
                {
                    var currentSate = states[SelectedState];
                    if (currentSate.OverrideCapsule)
                        DrawOverrideCapsule(currentSate.newCapsule, StateCapsuleColor, sc);
                }

                //Draw the Capsule Collider on the Selected Mode Ability
                if (Editor_Tabs1 == 2 && SelectedMode >= 0 && SelectedMode < modes.Count)
                {
                    var currentMode = modes[SelectedMode];
                    var abilityIndex = currentMode.EditorSelectedAbility;

                    if (currentMode.Abilities != null && abilityIndex >= 0 && abilityIndex < currentMode.Abilities.Count)
                    {
                        var currentAbility = currentMode.Abilities[abilityIndex];
                        if (currentAbility.HasCapsuleOverride)
                            DrawOverrideCapsule(currentAbility.OverrideCapsule.modifier, AbilityCapsuleColor, sc);
                    }
                }
            }


            //Draw all the internal colliders 
            if (Editor_Tabs1 == 0)
            {
                //draw main collider
                var ColorSolid = MainColliderColor;
                ColorSolid.a = 1f;

                if (MainCollider)
                {
                    MDebug.GizmoCapsuleMesh(ref CapsuleMesh, transform.TransformPoint(MainCollider.center), transform.rotation, MainCollider.height, MainCollider.radius, MainColliderColor, MainCollider.direction, 16, sc);
                    MDebug.GizmoCapsule(transform.TransformPoint(MainCollider.center), transform.rotation, MainCollider.height, MainCollider.radius, ColorSolid, MainCollider.direction, 16, sc);
                }

                Gizmos.color = Color.green;
                foreach (var col in colliders)
                {
                    var oldMatrix = Gizmos.matrix;
                    if (col is CapsuleCollider capsule)
                    {
                        MDebug.GizmoCapsule(capsule.transform.TransformPoint(capsule.center), capsule.transform.rotation, capsule.height * sc, capsule.radius * sc, Color.green, capsule.direction, 36);
                    }
                    else if (col is BoxCollider box)
                    {
                        Gizmos.matrix = col.transform.localToWorldMatrix;
                        Gizmos.DrawWireCube(box.center, box.size);
                    }
                    else if (col is SphereCollider sphere)
                    {
                        Gizmos.matrix = col.transform.localToWorldMatrix;
                        Gizmos.DrawWireSphere(sphere.center, sphere.radius);
                    }
                    Gizmos.matrix = oldMatrix; //Restore the Gizmos Matrix
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (!UnityEditorInternal.InternalEditorUtility.GetIsInspectorExpanded(this)) return;

            var t = transform;

            float sc = t.localScale.y;

            var pos = t.position;

            if (showPivots)
            {
                foreach (var pivot in pivots)
                {
                    if (pivot != null)
                    {
                        Gizmos.color = pivot.PivotColor;

                        Gizmos.DrawWireSphere(pivot.World(t), sc * RayCastRadius);
                        Gizmos.DrawSphere(pivot.World(t), sc * RayCastRadius);

                        if (pivot.name.Equals("Water")) continue; // Water Pivot is not a Ray Pivot
                        MDebug.GizmoRay(pivot.World(t), Pivot_Multiplier * sc * -t.up, 3);
                    }
                }
            }

            if (!debugGizmos) return;

            if (states.Count > 1 && states.Count > SelectedState && states[SelectedState] != null && states[SelectedState].Active && states_C[SelectedState].active)
                states[SelectedState].StateGizmos(this);

            if (Application.isPlaying)
            {
                Gizmos.color = Color.black;
                Gizmos.DrawSphere(pos + DeltaPos, 0.02f * sc);

                if (showPivots)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireSphere(Center, 0.02f * sc);
                    Gizmos.DrawSphere(Center, 0.02f * sc);
                }

                if (CurrentExternalForce != Vector3.zero)
                {
                    Gizmos.color = Color.cyan; //ds
                    Gizmos.DrawRay(Center, CurrentExternalForce * sc / 10);
                    Gizmos.DrawSphere(Center + (CurrentExternalForce * sc / 10), 0.05f * sc);
                }
            }
        }
#endif
    }

    [System.Serializable] public class AnimalEvent : UnityEvent<MAnimal> { }
    public enum Stance_Reaction
    {
        Set,
        SetPersistent,
        Toggle,
        SetDefault,
        ResetToDefault,
        ResetPersistent,
        RestoreDefaultStanceValue,
    }
}
