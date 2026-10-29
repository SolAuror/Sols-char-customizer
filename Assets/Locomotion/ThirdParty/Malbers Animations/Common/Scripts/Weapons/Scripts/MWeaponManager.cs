// Last Change: 9/30/2026 10:15am
using UnityEngine;
using System.Collections.Generic;
using MalbersAnimations.Weapons;
using MalbersAnimations.Scriptables;
using MalbersAnimations.Events;
using MalbersAnimations.Controller;

#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
#endif

namespace MalbersAnimations
{
    /// <summary> Rider Combat Mode</summary>
    [AddComponentMenu("Malbers/Weapons/Weapon Manager (Legacy)")]
    public partial class MWeaponManager : MonoBehaviour, IAnimatorListener, IMAnimator, IMWeaponOwner, IMDamagerSet, IWeaponManager
    {
        [HideInInspector] public int Editor_Tabs1;
        [HideInInspector] public int Editor_Tabs2;

        /// <summary>Console log for the Weapon Manager, active when Debug is enabled</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("MALBERS_DEBUG")]
        public void Debugging(string value, string color = "white")
        {
            if (debug)
                MDebug.Log($"<B>[{name}] → <color={color}>{value}</color></B>", this);
        }


        ///This was left blank intentionally
        /// Callbacks: all the public functions and methods
        /// Logic: all Combat logic is there, Equip, Unequip, Aim Mode...
        /// Variables: All Variables and Properties
        #region RESET COMBAT VALUES WHEN THE SCRIPT IS CREATED ON THE EDITOR

#if UNITY_EDITOR


        private void OnValidate()
        {
            if (LeftHandEquipPoint == null && RightHandEquipPoint == null && anim && anim.isHuman)
            {
                LeftHandEquipPoint = Anim.GetBoneTransform(HumanBodyBones.LeftHand);
                RightHandEquipPoint = Anim.GetBoneTransform(HumanBodyBones.RightHand);
                MTools.SetDirty(this);
            }
        }
        private void Reset()
        {
            if (!TryGetComponent<Aim>(out var m_Aim)) m_Aim = gameObject.AddComponent<Aim>();


            BoolVar WMCombatMode = MTools.GetInstance<BoolVar>("WM Combat Mode");
            MEvent WMEquippedWeapon = MTools.GetInstance<MEvent>("WM Equipped Weapon");
            MEvent SetCameraAimState = MTools.GetInstance<MEvent>("Set Camera AimState");

            if (WMCombatMode != null) UnityEditor.Events.UnityEventTools.AddPersistentListener(OnCombatMode, WMCombatMode.SetValue);
            if (WMEquippedWeapon != null) UnityEditor.Events.UnityEventTools.AddPersistentListener(OnEquipWeapon, WMEquippedWeapon.Invoke);

            if (SetCameraAimState != null)
                UnityEditor.Events.UnityEventTools.AddPersistentListener(m_Aim.OnAimSide, SetCameraAimState.Invoke);


            //AC Weapons
            animal = this.FindComponent<MAnimal>();
            comboManager = this.FindComponent<ComboManager>();
            DrawWeaponModeID = MTools.GetInstance<ModeID>("Weapon Draw");
            StoreWeaponModeID = MTools.GetInstance<ModeID>("Weapon Store");
            UnarmedModeID = MTools.GetInstance<ModeID>("Attack1");

            var DefaultHolster = MTools.GetInstance<HolsterID>("Left Holster");

            holsters = new List<Holster>() { new() { ID = DefaultHolster, Slots = new List<Transform>(1) { transform } } };



            Animator Anim = GetComponent<Animator>();

            if (Anim && Anim.avatar.isHuman)
            {
                LeftHandEquipPoint = Anim.GetBoneTransform(HumanBodyBones.LeftHand);
                RightHandEquipPoint = Anim.GetBoneTransform(HumanBodyBones.RightHand);
            }
        }


        [ContextMenu("Create Weapon Inputs")]
        void CreateInputs()
        {
            if (!TryGetComponent<MInput>(out var input)) { input = gameObject.AddComponent<MInput>(); }


            Debug.Log("input = " + input.ActiveMap.name);

            var RCA1 = input.FindInput("MainAttack");
            if (RCA1 == null)
            {
                RCA1 = new InputRow("MainAttack", "MainAttack", KeyCode.Mouse0, InputButton.Press, InputType.Key);
                input.ActiveMap.inputs.Add(RCA1);
                Debug.Log("<B>WeaponAttack1</B> Input created");
            }

            RCA1 = input.FindInput("SecondAttack");
            if (RCA1 == null)
            {
                RCA1 = new InputRow("SecondAttack", "SecondAttack", KeyCode.Mouse1, InputButton.Press, InputType.Key);
                input.ActiveMap.inputs.Add(RCA1);
                Debug.Log("<B>SecondAttack</B> Input created");
            }

            var Reload = input.FindInput("Reload");
            if (Reload == null)
            {
                Reload = new InputRow("Reload", "Reload", KeyCode.R, InputButton.Down, InputType.Key);
                input.ActiveMap.inputs.Add(Reload);
                Debug.Log("<B>Reload</B> Input created");
            }

            var Aim = input.FindInput("Aim");
            if (Aim == null)
            {
                Aim = new InputRow("Aim", "Aim", KeyCode.Mouse1, InputButton.Press, InputType.Key);
                input.ActiveMap.inputs.Add(Aim);
                Debug.Log("<B>Aim</B> Input created");
            }

            EditorUtility.SetDirty(input);
        }

        [ContextMenu("Create Holsters Inputs")]

        void ConnectDefaultHolsters()
        {
            MInput input = GetComponent<MInput>();
            if (input == null) { input = gameObject.AddComponent<MInput>(); }

            #region Holster Left
            var Holster_ = input.FindInput("HolsterLeft");
            if (Holster_ == null)
            {
                Holster_ = new InputRow("HolsterLeft", "HolsterLeft", KeyCode.Alpha4, InputButton.Down, InputType.Key);
                input.inputs.Add(Holster_);

                Debug.Log("<B>Holster Left</B> Input created");

            }
            #endregion

            #region Holster Right
            Holster_ = input.FindInput("HolsterRight");
            if (Holster_ == null)
            {
                Holster_ = new InputRow("HolsterRight", "HolsterRight", KeyCode.Alpha5, InputButton.Down, InputType.Key);
                input.inputs.Add(Holster_);

                Debug.Log("<B>Holster Right</B> Input created");
            }
            #endregion

            #region Holster Back
            Holster_ = input.FindInput("HolsterBack");
            if (Holster_ == null)
            {
                Holster_ = new InputRow("HolsterBack", "HolsterBack", KeyCode.Alpha6, InputButton.Down, InputType.Key);
                input.inputs.Add(Holster_);

                Debug.Log("<B>Holster Back</B> Input created");
            }
            #endregion

            EditorUtility.SetDirty(input);

        }


        [ContextMenu("Create Event Listeners")]
        void CreateEventListeners()
        {
            MEvent RCSetAim = MTools.GetInstance<MEvent>("RC Set Aim");
            MEvent RCMainAttack = MTools.GetInstance<MEvent>("RC Main Attack");
            MEvent RCSecondaryAttack = MTools.GetInstance<MEvent>("RC Secondary Attack");

            if (!TryGetComponent<MEventListener>(out MEventListener listener)) //CustomPatch: corrected null check for unity object
                gameObject.AddComponent<MEventListener>();

            listener.Events ??= new List<MEventItemListener>();


            //*******************//
            if (listener.Events.Find(item => item.Event == RCSetAim) == null)
            {
                var item = new MEventItemListener()
                {
                    Event = RCSetAim,
                    useVoid = false,
                    useBool = true,
                };

                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.ResponseBool, Aim_Set);
                listener.Events.Add(item);

                Debug.Log("<B>RC Set Aim</B> Added to the Event Listeners");
            }

            //*******************//
            if (listener.Events.Find(item => item.Event == RCMainAttack) == null)
            {
                var item = new MEventItemListener()
                {
                    Event = RCMainAttack,
                    useVoid = false,
                    useBool = true
                };

                UnityEditor.Events.UnityEventTools.AddPersistentListener(item.ResponseBool, MainAttack);
                listener.Events.Add(item);

                Debug.Log("<B>RC MainAttack</B> Added to the Event Listeners");
            }
        }

#endif
        #endregion
    }

    /// <summary> Weapon Manager Holster: stores a Weapon in its Slots and equips it with its own Input </summary>
    [System.Serializable]
    public class Holster
    {
        /// <summary> ID of the Holster </summary>
        public HolsterID ID;
        /// <summary> Index of the Holster on the Weapon Manager list </summary>
        public int Index;

        /// <summary> Slots Transforms used to store weapons </summary>
        [Tooltip("Slots Transforms used to store weapons")]
        public List<Transform> Slots;

        /// <summary> Transform Reference for the holster</summary>
        [Tooltip("Weapon GameObject associated to the Holster")]
        public MWeapon Weapon;

        /// <summary> Input to Equip the weapon in the holster </summary>
        [Tooltip("Input to Equip the weapon in the holster")]
        public StringReference Input = new();

        /// <summary> Equip the weapon on the Hand automatically when it is added to the holster </summary>
        [Tooltip("If the weapon is added to the holster then it will be equipped on the Hand automatically")]
        public BoolReference AutoEquip = new();

        /// <summary> Used to connect the Inputs to the Abilities instead of the General Mode </summary>
        public UnityEngine.Events.UnityAction<bool> InputListener;

        /// <summary> Invoked when a weapon is placed in the holster </summary>
        public WeaponEvent OnWeaponInHolster = new();

        /// <summary> ID value of the Holster (0 when there is no ID) </summary>
        public int GetID => ID != null ? ID.ID : 0;

        /// <summary> Returns the Slot Transform at the given index </summary>
        public Transform GetSlot(int index) => Slots[index];

        /// <summary>  Prepare the weapons. Instantiate/Place them in the holster if they are linked in the Weapon Manager </summary>
        public bool PrepareWeapon()
        {
            if (Weapon)
            {
                var slot = Slots[Weapon.HolsterSlot]; //Get the correct slot

                //if it is a prefab then instantiate it!!
                if (Weapon.gameObject.IsPrefab())
                {
                    if (slot.childCount > 0) //Check
                    {
                        Object.Destroy(slot.GetChild(0).gameObject);
                    }

                    Weapon = GameObject.Instantiate(Weapon);
                    Weapon.name = Weapon.name.Replace("(Clone)", "");

                    Weapon.Debugging("[Instantiated]", Weapon);
                }

                //MAKE SURE THE WEAPON HAS THE SAME ID OF THE WEAPON
                Weapon.Holster = ID;



                //Re-Parent a frame after
                Weapon.Delay_Action(() =>
                {
                    if (!Weapon.IsEquipped)
                    {
                        Weapon.transform.SetParent(slot);
                        Weapon.transform.SetLocalTransform(Weapon.HolsterOffset);
                    }
                }
                );

                OnWeaponInHolster.Invoke(Weapon);
                //Weapon.InHolster = true;

                // Debug.Log("Weapon = " + Weapon);
                Weapon.IsCollectable?.Pick(); //Pick the weapon in case is a collectible

                return true;
            }
            return false;
        }

        /// <summary> Implicit conversion to the Holster ID value </summary>
        public static implicit operator int(Holster reference) => reference.GetID;
    }

    #region Inspector

#if UNITY_EDITOR
    [CustomEditor(typeof(MWeaponManager))]
    public class MWeaponManagerEditor : Editor
    {
        GUIStyle StyleGreen => MTools.Style(new Color(0f, 1f, 0.5f, 0.3f));

        MWeaponManager M;

        private SerializedProperty
            debug, LeftHandEquipPoint, RightHandEquipPoint, OverrideLayer,
            canAim,
            /* UseDefaultIK,*/
            Anim,
            OnEquipWeapon, OnUnequipWeapon, OnCombatMode, OnCanAim, OnWeaponAction,
            DestroyOnDrop, //MWC: combat layer path/name removed with the 'Add Combat Layers' button

            OnAttackStart, OnAttackReleased, IKProfiles, CharacterScale, // MWC — IKProfiles replaces CustomIKWeaponsRight/Left

            // OnMainAttackStart,

            DisableModes, ExitOnModes, ExitOnState, ExitFast, DropWeapon,// IKLerp,

            holsters, UseExternal, UseHolsters,

            PreAttackConditions,

              DestroyOnUnequip, InstantiateOnEquip, StoreSelfHolster,
            StoreAfter, start_weapon, m_IgnoreDraw, aim, m_IgnoreStore,

            animal, comboManager, DrawWeapon, StoreWeapon, UnarmedMode, IgnoreHandOffset,

            m_AimInput, m_ReloadInput, m_MainAttack, m_SecondAttack,  // m_SpecialAttack,

          Editor_Tabs1, Editor_Tabs2, m_WeaponType, m_LeftHand, m_IKFreeHand, m_IKAim,  //m_WeaponPower, ExitCombatOnDismount;


            secondAttackBranch, mainAttackBranch,

            m_ModeOn, m_Mode, m_WeaponPower, DropPoint, IgnoreParentWeapon
        ;

        private ReorderableList holsterReordable;

        protected virtual void OnEnable()
        {
            M = (MWeaponManager)target;

            // StyleGreen = 

            FindProperties();

            holsterReordable = new ReorderableList(serializedObject, holsters, true, true, true, true)
            {
                drawElementCallback = DrawHolsterElement,
                drawHeaderCallback = DrawHolsterHeader
            };

        }
        private void FindProperties()
        {

            secondAttackBranch = serializedObject.FindProperty("secondAttackBranch");
            mainAttackBranch = serializedObject.FindProperty("mainAttackBranch");

            IKProfiles = serializedObject.FindProperty("IKProfiles");
            CharacterScale = serializedObject.FindProperty("CharacterScale");


            animal = serializedObject.FindProperty("animal");
            OverrideLayer = serializedObject.FindProperty("OverrideWeaponLayer");
            DestroyOnDrop = serializedObject.FindProperty("DestroyOnDrop");
            IgnoreParentWeapon = serializedObject.FindProperty("IgnoreParentWeapon");

            m_AimInput = serializedObject.FindProperty("m_AimInput");
            DropPoint = serializedObject.FindProperty("DropPoint");
            m_ReloadInput = serializedObject.FindProperty("m_ReloadInput");
            m_MainAttack = serializedObject.FindProperty("m_MainAttack");
            m_SecondAttack = serializedObject.FindProperty("m_SecondAttack");
            //m_SpecialAttack = serializedObject.FindProperty("m_SpecialAttack");

            canAim = serializedObject.FindProperty("canAim");


            DisableModes = serializedObject.FindProperty("DisableModes");
            ExitOnState = serializedObject.FindProperty("ExitOnState");
            ExitOnModes = serializedObject.FindProperty("ExitOnModes");
            ExitFast = serializedObject.FindProperty("ExitFast");

            Anim = serializedObject.FindProperty("anim");

            start_weapon = serializedObject.FindProperty("startWeapon");
            aim = serializedObject.FindProperty("aim");

            IgnoreHandOffset = serializedObject.FindProperty("IgnoreHandOffset");
            comboManager = serializedObject.FindProperty("comboManager");
            DrawWeapon = serializedObject.FindProperty("DrawWeaponModeID");
            StoreWeapon = serializedObject.FindProperty("StoreWeaponModeID");

            UnarmedMode = serializedObject.FindProperty("UnarmedModeID");
            DropWeapon = serializedObject.FindProperty("DropWeapon");

            // UseDefaultIK = serializedObject.FindProperty("UseDefaultIK");
            StoreAfter = serializedObject.FindProperty("StoreAfter");
            m_IgnoreDraw = serializedObject.FindProperty("m_IgnoreDraw");
            m_IgnoreStore = serializedObject.FindProperty("m_IgnoreStore");
            // DisableAim = serializedObject.FindProperty("DisableAim");


            #region Animator Parameters
            m_WeaponType = serializedObject.FindProperty("m_WeaponType");
            m_LeftHand = serializedObject.FindProperty("m_LeftHand");
            m_IKFreeHand = serializedObject.FindProperty("m_IKFreeHand");
            m_IKAim = serializedObject.FindProperty("m_IKAim");
            m_ModeOn = serializedObject.FindProperty("m_ModeOn");
            m_Mode = serializedObject.FindProperty("m_Mode");
            m_WeaponPower = serializedObject.FindProperty("m_WeaponPower");
            #endregion


            holsters = serializedObject.FindProperty("holsters");
            //  DefaultHolster = serializedObject.FindProperty("DefaultHolster");

            StoreSelfHolster = serializedObject.FindProperty("StoreSelfHolster");
            PreAttackConditions = serializedObject.FindProperty("PreAttackConditions");



            // ExitCombatOnDismount = serializedObject.FindProperty("ExitCombatOnDismount");

            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            Editor_Tabs2 = serializedObject.FindProperty("Editor_Tabs2");
            //HitMask = serializedObject.FindProperty("HitMask"); 


            LeftHandEquipPoint = serializedObject.FindProperty("LeftHandEquipPoint");
            RightHandEquipPoint = serializedObject.FindProperty("RightHandEquipPoint");

            OnCombatMode = serializedObject.FindProperty("OnCombatMode");
            OnCanAim = serializedObject.FindProperty("OnCanAim");
            OnEquipWeapon = serializedObject.FindProperty("OnEquipWeapon");
            OnUnequipWeapon = serializedObject.FindProperty("OnUnequipWeapon");
            OnWeaponAction = serializedObject.FindProperty("OnWeaponAction");


            OnAttackStart = serializedObject.FindProperty("OnAttackStart");
            OnAttackReleased = serializedObject.FindProperty("OnAttackReleased");
            //   OnMainAttackStart = serializedObject.FindProperty("OnMainAttackStart");


            UseExternal = serializedObject.FindProperty("UseExternal");
            UseHolsters = serializedObject.FindProperty("UseHolsters");


            DestroyOnUnequip = serializedObject.FindProperty("DestroyOnUnequip");
            InstantiateOnEquip = serializedObject.FindProperty("InstantiateOnEquip");


            debug = serializedObject.FindProperty("debug");
        }

        private void DrawHolsterHeader(Rect rect)
        {
            var IDRect = new Rect(rect);
            IDRect.height = EditorGUIUtility.singleLineHeight;
            IDRect.width *= 0.5f;
            IDRect.x += 18;
            EditorGUI.LabelField(IDRect, "   Holster ID");
            IDRect.x += IDRect.width - 10;
            IDRect.width -= 18;
            EditorGUI.LabelField(IDRect, "   Weapon ");

            //var buttonRect = new Rect(rect) { x = rect.width - 30, width = 63, y = rect.y - 1 };
            //var oldColor = GUI.backgroundColor;
            //GUI.backgroundColor = new Color(.8f, .8f, 1f, 1f);

            //if (GUI.Button(buttonRect, new GUIContent("Weapon", "Check for Weapons on the Holsters.\nIf the weapons are prefab it will instantiate them on the scene"), EditorStyles.miniButton))
            //{
            //    M.ValidateWeaponsChilds();
            //    EditorUtility.SetDirty(target);
            //}


            //GUI.backgroundColor = oldColor;
        }

        private void DrawHolsterElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            rect.y += 2;

            var holster = holsters.GetArrayElementAtIndex(index);

            var ID = holster.FindPropertyRelative("ID");
            //  var t = holster.FindPropertyRelative("Transform");
            var Weapon = holster.FindPropertyRelative("Weapon");



            var IDRect = new Rect(rect)
            {
                height = EditorGUIUtility.singleLineHeight
            };
            IDRect.width *= 0.5f;
            IDRect.x += 18;
            IDRect.width -= 10;
            EditorGUI.PropertyField(IDRect, ID, GUIContent.none);

            var pre = "";
            var tooltip = "";
            var oldColor = GUI.backgroundColor;
            var newColor = oldColor;

            var weaponObj = Weapon.objectReferenceValue as Component;
            if (weaponObj && weaponObj.gameObject != null)
            {
                if (weaponObj.gameObject.IsPrefab())
                {
                    newColor = Color.green;
                    pre = "[P]";
                    tooltip = "The Weapon is a Prefab. It will be instantiated on start";
                }
                else
                {
                    pre = "[S]";
                    tooltip = "The Weapon is in the scene";
                }
            }


            IDRect.x += IDRect.width + 10;
            IDRect.width -= 25;


            GUI.backgroundColor = newColor;
            EditorGUI.PropertyField(IDRect, Weapon, GUIContent.none);
            GUI.backgroundColor = oldColor;

            var lbRect = new Rect(rect)
            {
                height = EditorGUIUtility.singleLineHeight,
                width = 20,
                x = rect.width + 25,
            };

            EditorGUI.LabelField(lbRect, new GUIContent(pre, tooltip));
        }


        /// <summary> Draws all of the fields for the selected ability. </summary>

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MalbersEditor.DrawDescription("System for using weapons while a character is grounded & riding");

            //if (!Application.isPlaying)
            //    AddLayer();

            if (helpboxStyle == null)
            {
                helpboxStyle = new GUIStyle(MTools.StyleGray)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    stretchWidth = true
                };
                helpboxStyle.normal.textColor = EditorStyles.boldLabel.normal.textColor;
            }


            var Hols = "Holsters";

            if (!M.UseHolsters) Hols = "External";

            Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, new string[] { "General", Hols, "AC", "Events" });

            if (Editor_Tabs1.intValue != 5) Editor_Tabs2.intValue = 5; //CustomPatch: TODO: recommendation: avoid "magic" numbers => use constants or enums as ints

            Editor_Tabs2.intValue = GUILayout.Toolbar(Editor_Tabs2.intValue, new string[] { "Inputs", "Advanced", "Animator", "Debug" });

            if (Editor_Tabs2.intValue != 5) Editor_Tabs1.intValue = 5;

            //First Tabs
            int Selection = Editor_Tabs1.intValue;

            if (Selection == 0) DrawGeneral();
            else if (Selection == 1) ShowHolsters();
            else if (Selection == 2) DrawAC();
            else if (Selection == 3) DrawEvents();


            //2nd Tabs
            Selection = Editor_Tabs2.intValue;

            if (Selection == 0) DrawInputs();
            else if (Selection == 1) DrawAdvanced();
            else if (Selection == 2) DrawAnimator();
            else if (Selection == 3) DrawDebug();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawInputs()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(m_AimInput);
                EditorGUILayout.PropertyField(m_ReloadInput);
                EditorGUILayout.PropertyField(m_MainAttack);
                EditorGUILayout.PropertyField(m_SecondAttack);
            }

            if (M.UseHolsters)
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    for (int i = 0; i < holsters.arraySize; i++)
                    {
                        var element = holsters.GetArrayElementAtIndex(i);
                        var input = element.FindPropertyRelative("Input");
                        EditorGUILayout.PropertyField(input, new GUIContent($"Holster [{i}] Input"));
                    }
                }
            }

        }

        private void DrawAC()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(animal);

                if (animal.objectReferenceValue != null)
                {
                    EditorGUILayout.PropertyField(DrawWeapon, new GUIContent("Mode [Draw Weapon]"));
                    EditorGUILayout.PropertyField(StoreWeapon, new GUIContent("Mode [Store Weapon]"));
                    EditorGUILayout.PropertyField(UnarmedMode, new GUIContent("Mode [Unarmed]"));

                    EditorGUILayout.Space();

                    EditorGUILayout.PropertyField(comboManager);
                    EditorGUILayout.PropertyField(mainAttackBranch);
                    EditorGUILayout.PropertyField(secondAttackBranch);
                    EditorGUILayout.Space();

                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(DisableModes);
                    EditorGUI.indentLevel--;

                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(ExitOnState);
                    EditorGUILayout.PropertyField(DropWeapon);
                    EditorGUI.indentLevel--;

                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(ExitOnModes);
                    EditorGUI.indentLevel--;

                    EditorGUILayout.PropertyField(ExitFast);
                }
            }
        }

        private void DrawAnimator()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(Anim);
                EditorGUILayout.LabelField("Animator Parameters", EditorStyles.boldLabel);
                //     DisplayParam(m_WeaponAction, UnityEngine.AnimatorControllerParameterType.Int);
                MalbersEditor.DisplayParam(M.Anim, m_WeaponType, UnityEngine.AnimatorControllerParameterType.Int);
                MalbersEditor.DisplayParam(M.Anim, m_LeftHand, UnityEngine.AnimatorControllerParameterType.Bool);
                MalbersEditor.DisplayParam(M.Anim, m_IKFreeHand, UnityEngine.AnimatorControllerParameterType.Float);
                MalbersEditor.DisplayParam(M.Anim, m_IKAim, UnityEngine.AnimatorControllerParameterType.Float);

                if (M.animal == null)
                {
                    MalbersEditor.DisplayParam(M.Anim, m_ModeOn, UnityEngine.AnimatorControllerParameterType.Trigger);
                    MalbersEditor.DisplayParam(M.Anim, m_Mode, UnityEngine.AnimatorControllerParameterType.Int);
                    MalbersEditor.DisplayParam(M.Anim, m_WeaponPower, UnityEngine.AnimatorControllerParameterType.Float);
                }
            }


            EditorGUILayout.LabelField("Weapon Ability Values:" +
              "\nNone = 0" +
              "\nIdle = 100" +
              "\nAim = 97" +
              "\nReload = 96" +
              "\nDraw = 99" +
              "\nStore = 98" +
               "\nFire Projectile = 101"
              , helpboxStyle);
        }
        private GUIStyle helpboxStyle;

        private void DrawDebug()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(debug, new GUIContent("Debug", ""));

                if (Application.isPlaying)
                {
                    using (new EditorGUI.DisabledGroupScope(true))
                    {

                        using (new GUILayout.HorizontalScope())
                        {

                            EditorGUIUtility.labelWidth = 130;


                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                EditorGUILayout.Toggle("Is In Combat mode", M.CombatMode);
                                EditorGUILayout.Toggle("Weapon Draw", M.DrawWeapon);
                                EditorGUILayout.Toggle("Weapon Store", M.StoreWeapon);
                                EditorGUILayout.Toggle("Is Riding", M.IsRiding);
                                if (M.HasAnimal) EditorGUILayout.Toggle("Preparing Mode", M.animal.IsPreparingMode);

                            }

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                EditorGUILayout.Toggle("Is Aiming", M.Aim);
                                EditorGUILayout.Toggle("ExitByMode", M.ExitByMode);
                                EditorGUILayout.Toggle("Was Strafing", M.DefaultStrafing);
                                EditorGUILayout.Toggle("Mount Dismount", M.MountingDismounting);
                                if (M.Aimer != null) EditorGUILayout.Toggle("Aiming Side", M.AimingSide);
                            }
                            EditorGUIUtility.labelWidth = 0;

                        }

                        using (new GUILayout.HorizontalScope())
                        {
                            EditorGUIUtility.labelWidth = 80;

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                EditorGUILayout.FloatField("IK Aim", M.IKAimWeight);
                                EditorGUILayout.FloatField("IK 2Hands", M.IK2HandsWeight);
                            }

                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                EditorGUILayout.EnumPopup("Weap Action", M.WeaponAction);
                                EditorGUILayout.IntField("Anim Action", M.WeaponAnimAction);
                            }
                            EditorGUIUtility.labelWidth = 0;
                        }


                        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.ObjectField("Active Weapon", M.Weapon, typeof(MWeapon), false);

                            if (M.Weapon != null)
                            {
                                EditorGUILayout.ObjectField("Ground Combo", M.Weapon.GroundCombo, typeof(ModeID), false);
                                EditorGUILayout.ObjectField("Riding Combo", M.Weapon.RidingCombo, typeof(ModeID), false);
                            }

                            if (M.comboManager != null)
                            {
                                EditorGUILayout.LabelField($"Active Combo [{(M.comboManager.ActiveCombo != null ? M.comboManager.ActiveCombo.Name : "<None>")}]");
                            }
                        }

                        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.LabelField($"[Holsters]", EditorStyles.boldLabel);
                            foreach (var holsters in M.holsters)
                            {
                                using (new GUILayout.HorizontalScope())
                                {
                                    EditorGUILayout.ObjectField(GUIContent.none, holsters.ID, typeof(HolsterID), false);
                                    EditorGUILayout.ObjectField(GUIContent.none, holsters.Weapon, typeof(MWeapon), false);
                                }
                            }
                        }


                        if (M.Weapon)
                        {
                            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                if (M.ActiveHolster != null)
                                {
                                    EditorGUILayout.ObjectField("Active Holster", M.ActiveHolster.ID, typeof(HolsterID), false);
                                    EditorGUILayout.ObjectField("Active Holster Weapon", M.ActiveHolster.Weapon, typeof(MWeapon), false);
                                }

                                EditorGUILayout.ObjectField("Weapon.Type", M.Weapon?.WeaponType, typeof(WeaponID), false);
                            }

                            using (new GUILayout.HorizontalScope())
                            {
                                EditorGUIUtility.labelWidth = 130;

                                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                {
                                    EditorGUILayout.Toggle("Weapon.Active", M.Weapon.Active);
                                    EditorGUILayout.Toggle("Weapon.Input", M.Weapon.Input);
                                    EditorGUILayout.Toggle("Weapon.IsAiming", M.Weapon.IsAiming);
                                    EditorGUILayout.Toggle("Weapon.RightHand", M.Weapon.IsRightHanded);
                                    //  EditorGUILayout.Toggle("Weapon.Ready: ", M.Weapon.IsReady);


                                    EditorGUILayout.Toggle("Weapon.HasAmmo", M.Weapon.HasAmmo);
                                    EditorGUILayout.FloatField("Weapon.Power", M.Weapon.Power);


                                    if (M.Weapon is MShootable mshoo)
                                    {
                                        EditorGUILayout.FloatField("Ammo In Chamber", mshoo.AmmoInChamber);
                                        EditorGUILayout.FloatField("Chamber Size", mshoo.ChamberSize);
                                    }

                                }
                                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                                {

                                    EditorGUILayout.Toggle("Weapon.CanAttack", M.Weapon.CanAttack);
                                    EditorGUILayout.Toggle("Weapon.IsAttacking", M.Weapon.IsAttacking);
                                    EditorGUILayout.Toggle("Weapon.CanCharge", M.Weapon.CanCharge);

                                    if (M.Weapon.CanCharge)
                                    {
                                        EditorGUILayout.Toggle("Weapon.IsCharging", M.Weapon.IsCharging);
                                        EditorGUILayout.Toggle("Weapon.MaxCharged", M.Weapon.MaxCharged);
                                        EditorGUILayout.FloatField("Weapon.ChargeNorm", M.Weapon.ChargedNormalized);
                                    }


                                    if (M.Weapon is MShootable mshoo)
                                    {
                                        EditorGUILayout.Toggle("Weapon.IsReloading", mshoo.IsReloading);
                                        EditorGUILayout.Toggle("Projectile Equipped", mshoo.ProjectileEquipped);
                                        EditorGUILayout.FloatField("Total Ammo", mshoo.TotalAmmo);
                                    }
                                }
                                EditorGUIUtility.labelWidth = 0;
                            }
                        }
                        Repaint();
                    }
                }
            }
        }

        private void ShowHolsters()
        {
            EditorGUILayout.Space();
            var selection = UseHolsters.boolValue ? 0 : 1;
            selection = GUILayout.Toolbar(selection, new string[] { "Use Holsters", "Use External" });
            UseExternal.boolValue = selection != 0;
            UseHolsters.boolValue = selection == 0;


            if (styleDesc == null)
            {
                styleDesc = new GUIStyle(StyleGreen)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                    stretchWidth = true
                };

                styleDesc.normal.textColor = EditorStyles.label.normal.textColor;
            }


            if (UseExternal.boolValue)
            {
                EditorGUILayout.LabelField("Use the Method <Equip_External(GameObject)> to equip weapons", styleDesc);
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.PropertyField(InstantiateOnEquip);
                    EditorGUILayout.PropertyField(DestroyOnUnequip);
                    EditorGUILayout.PropertyField(IgnoreParentWeapon);
                }
            }

            //Holder Stuffs
            if (M.UseHolsters)
            {
                EditorGUILayout.LabelField("The weapons are child of the Holsters", styleDesc);
                EditorGUILayout.PropertyField(StoreSelfHolster);

                EditorGUILayout.PropertyField(DropPoint);
                EditorGUILayout.PropertyField(DestroyOnDrop);

                holsterReordable.DoLayoutList();

                if (holsterReordable.index != -1)
                {
                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        var element = holsters.GetArrayElementAtIndex(holsterReordable.index);
                        var Input = element.FindPropertyRelative("Input");
                        var Slots = element.FindPropertyRelative("Slots");
                        var AutoEquip = element.FindPropertyRelative("AutoEquip");
                        EditorGUILayout.PropertyField(Input);
                        EditorGUILayout.PropertyField(AutoEquip);
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(Slots, true);
                        EditorGUI.indentLevel--;
                    }
                }
            }
        }

        private void DrawGeneral()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var dC = start_weapon.displayName;
                if (M.StartWeapon && M.StartWeapon.IsPrefab())
                {
                    dC += " [Prefab]";
                }

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(start_weapon, new GUIContent(dC, start_weapon.tooltip));
                    MalbersEditor.DrawDebugIcon(debug);
                }

                //using (var cc = new EditorGUI.ChangeCheckScope())
                //{
                //    EditorGUILayout.PropertyField(aim);
                //    if (cc.changed && Application.isPlaying)
                //    {
                //        serializedObject.ApplyModifiedProperties();
                //        M.SetAimLogic(M.Aim);
                //        //Debug.Log("M.Aim = " + M.Aim);
                //    }
                //}


                EditorGUILayout.PropertyField(m_IgnoreDraw);
                EditorGUILayout.PropertyField(m_IgnoreStore);
                EditorGUILayout.PropertyField(StoreAfter);
                EditorGUILayout.PropertyField(OverrideLayer);
                EditorGUILayout.PropertyField(canAim);
            }



            EquipWeaponPoints();


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {

                EditorGUILayout.PropertyField(PreAttackConditions);
            }
        }

        private void EquipWeaponPoints()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                LeftHandEquipPoint.isExpanded = MalbersEditor.Foldout(LeftHandEquipPoint.isExpanded, "Weapon Equip Points");

                if (LeftHandEquipPoint.isExpanded)
                {
                    EditorGUILayout.PropertyField(LeftHandEquipPoint, new GUIContent("Left Hand"));
                    EditorGUILayout.PropertyField(RightHandEquipPoint, new GUIContent("Right Hand"));
                    EditorGUILayout.PropertyField(IgnoreHandOffset);
                }
            }
        }
        private void DrawAdvanced()
        {
            //MWC: 'Add Combat Layers' block removed – copy the layer with the Animator Layer Copier context menu instead

            // MWC — Aim IK Profiles asset (per character rig) replaces the old per-weapon override lists
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(new GUIContent("Aim IK", IKProfiles.tooltip), EditorStyles.boldLabel);
                MTools.DrawScriptableObject(IKProfiles, true, false, "IK Profiles");
            }
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                EditorGUILayout.PropertyField(CharacterScale);
        }


        private GUIStyle styleDesc;

        void DrawEvents()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(OnCombatMode);
                EditorGUILayout.PropertyField(OnCanAim, new GUIContent("On Aim"));
                EditorGUILayout.PropertyField(OnEquipWeapon);
                EditorGUILayout.PropertyField(OnUnequipWeapon);
                //  EditorGUILayout.PropertyField(OnMainAttackStart);
                EditorGUILayout.PropertyField(OnWeaponAction);
                EditorGUILayout.PropertyField(OnAttackStart);
                EditorGUILayout.PropertyField(OnAttackReleased);
            }

            if (M.UseHolsters)
            {
                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    for (int i = 0; i < holsters.arraySize; i++)
                    {
                        var element = holsters.GetArrayElementAtIndex(i);
                        var input = element.FindPropertyRelative("OnWeaponInHolster");
                        EditorGUILayout.PropertyField(input, new GUIContent($" On Weapon In Holster [{i}]"));
                    }
                }
            }
        }

        //MWC: AddLayerCombat / AddLayers removed – layer copying now lives in the Animator Layer Copier
    }
#endif
    #endregion
}