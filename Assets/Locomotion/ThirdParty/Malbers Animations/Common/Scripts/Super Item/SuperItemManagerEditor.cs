// Last Change: 10/6/2026 10:21am
using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditorInternal;
using UnityEditor;
#endif

namespace MalbersAnimations.Items
{

#if UNITY_EDITOR
    [CustomEditor(typeof(SuperItemManager))]
    public class SuperItemManagerEditor : Editor
    {
        private SerializedProperty ShowWarnings, Editor_Tabs1, Editor_Tabs2, EditOffsetsItems,
        EquipPoints, ItemOffsets, dropPoint,
        m_hitLayer, triggerInteraction, Tags,
        holsters, m_ItemRightHandType, m_ItemLeftHandType, m_Mirror, selfStoreHolster, InstantDrop,
        ActionsInput,
        ComboManager, Animal, Aim, Pouch, anim, debug, interactor, DefaultItem, //MWC — DefaultItem (Unarmed fallback)
        IKManager, SelectedHolster, SelectedEquipPointSlot, SelectedHolsterSlot,

            NoItemsInState, NoItemsStance, NoItemsMode, FastEquipBack, //MWC — Fast Equip Back toggle
            useFastWeapons, //MWC — [Fast Weapons] (Assassin's Creed style)
            AutoStore, //MWC — [Auto Holster] global idle time
            ActionConditions, //Manager-wide gate for every Item Action
            crossItemPriority, //MWC — [Cross Item Priority] compare Action Priority across the equipped Items
            lockMode, OnLocked, //[Lock] put every equipped Item away (Holster/Hide) and freeze the Manager
            autoEquip, //Global [Auto Equip]: ON = every picked Item goes to the hands
            OnItemEquipped, OnItemUnequipped, OnItemHolster, OnItemUnholster, OnItemDropped, OnItemPicked, OnActionPressed, OnActionReleased,
            ItemEquippedReaction, ItemUnequippedReaction, ItemHolsterReaction, ItemUnholsterReaction, ItemDroppedReaction, ItemPickedReaction,
            ActionReactions, AnyActionPressedReaction, AnyActionReleasedReaction
        ;

        private ReorderableList holsterReordable, EquipPointsReordable;

        private SuperItemManager M;

        private Quaternion m_RotateStartWorld = Quaternion.identity; // MWC — item world rotation captured at the start of a rotate-handle drag (prevents compounding)

        /// <summary>Rotation fed to Handles.RotationHandle: the gizmo frame while idle, the handle's own last result during a drag (see DoRotationHandle). </summary>
        private Quaternion m_RotateHandleRot = Quaternion.identity;

        // The two parts of Handles.RotationHandle report differently: the axis rings return the TOTAL rotation since the
        // drag began (they ignore the input while dragging), the center free-rotate returns the INCREMENT since the last
        // mouse event applied to the input. Feeding the handle its own previous result during a drag makes both of them
        // accumulate into "total rotation since the drag began", which is then applied to the rotation captured at drag
        // start (feeding the fixed start rotation every frame kept only the free-rotate's last increment: almost no rotation).
        // While idle (hotControl == 0) the frame is re-synced every repaint so the gizmo follows its target.
        /// <summary>Rotation handle that follows the Local/Global pivot toggle. True, with the new WORLD rotation, when the user dragged it. </summary>
        private bool DoRotationHandle(Quaternion currentWorld, Vector3 position, ref Quaternion startWorld, out Quaternion newWorld)
        {
            var local = Tools.pivotRotation == PivotRotation.Local;

            if (GUIUtility.hotControl == 0)
            {
                startWorld = currentWorld;
                m_RotateHandleRot = local ? currentWorld : Quaternion.identity; //Gizmo frame: the target in Local, world axes in Global
            }

            using (var cc = new EditorGUI.ChangeCheckScope())
            {
                var result = Handles.RotationHandle(m_RotateHandleRot, position);

                if (cc.changed)
                {
                    m_RotateHandleRot = result;
                    var delta = local ? result * Quaternion.Inverse(startWorld) : result; //World-space total since drag start
                    newWorld = delta * startWorld;
                    return true;
                }
            }

            newWorld = currentWorld;
            return false;
        }

        #region Tips

        // MWC — Equip Point reaction labels. These are NOT cached GUIContents on purpose: the Reaction2 drawer
        // appends the count to label.text, so a shared instance would grow " (0) (0) (0)…" every repaint.
        private const string k_OnEquipLabel = "On Equip - Dynamic Target [Equipped Item]";
        private const string k_OnUnequipLabel = "On Unequip - Dynamic Target [Equipped Item]";
        private const string k_OnEquipTip = "Reactions played when an Item is equipped on this Equip Point.\nThe Dynamic Target of every reaction is the Equipped Item.";
        private const string k_OnUnequipTip = "Reactions played when an Item is unequipped from this Equip Point.\nThe Dynamic Target of every reaction is the Unequipped Item.";

        // MWC — Holster reaction labels. Same rule as the Equip Point ones above: plain strings, never cached
        // GUIContents, because the Reaction2 drawer appends the count to label.text.
        private const string k_OnHolsterLabel = "On Holster - Dynamic Target [Holstered Item]";
        private const string k_OnUnholsterLabel = "On Unholster - Dynamic Target [Unholstered Item]";
        private const string k_OnHolsterTip = "Reactions played when an Item is stored in this Holster.\nThe Dynamic Target of every reaction is the Holstered Item.";
        private const string k_OnUnholsterTip = "Reactions played when an Item is removed from this Holster (drawn out, evicted or dropped).\nThe Dynamic Target of every reaction is the Item that left.";
        private const string k_OnHolsterItemLabel = "On Holster Item Changed - Dynamic Target [Current Item]";
        private const string k_OnHolsterEmptyLabel = "On Holster Empty - Dynamic Target [Super Item Manager]";
        private const string k_OnHolsterItemTip = "Reactions played when the Item this Holster SHOWS changes - stored, or promoted after another one left.\nA Holster can store several Items, so use this (not On Unholster) to drive the Holster UI icon.\nThe Dynamic Target of every reaction is the Item currently on top.";
        private const string k_OnHolsterEmptyTip = "Reactions played when the LAST Item leaves and this Holster is empty.\nThere is no Item to point at, so the Dynamic Target of every reaction is the Super Item Manager.";
        #endregion

        protected virtual void OnEnable()
        {
            M = (SuperItemManager)target;

            Editor_Tabs1 = serializedObject.FindProperty("Editor_Tabs1");
            ShowWarnings = serializedObject.FindProperty("ShowWarnings");
            Editor_Tabs2 = serializedObject.FindProperty("Editor_Tabs2");
            EditOffsetsItems = serializedObject.FindProperty("EditOffsetsItems");

            NoItemsInState = serializedObject.FindProperty("NoItemsInState");
            NoItemsStance = serializedObject.FindProperty("NoItemsInStance");
            NoItemsMode = serializedObject.FindProperty("NoItemsInMode");
            FastEquipBack = serializedObject.FindProperty("FastEquipBack");

            debug = serializedObject.FindProperty("debug");

            m_hitLayer = serializedObject.FindProperty("m_OverrideItemLayer");
            triggerInteraction = serializedObject.FindProperty("triggerInteraction");
            Tags = serializedObject.FindProperty(nameof(M.ItemTagInteraction));

            m_ItemLeftHandType = serializedObject.FindProperty("m_ItemLeftHandType");
            m_ItemRightHandType = serializedObject.FindProperty("m_ItemRightHandType");
            m_Mirror = serializedObject.FindProperty("m_Mirror");

            //Action Inputs
            ActionsInput = serializedObject.FindProperty("ActionsInput");
            useFastWeapons = serializedObject.FindProperty("useFastWeapons"); //MWC — [Fast Weapons]
            AutoStore = serializedObject.FindProperty("AutoStore"); //MWC — [Auto Holster]
            ActionConditions = serializedObject.FindProperty("ActionConditions");
            crossItemPriority = serializedObject.FindProperty("crossItemPriority"); //MWC — [Cross Item Priority]
            lockMode = serializedObject.FindProperty("lockMode"); //[Lock]
            OnLocked = serializedObject.FindProperty("OnLocked");
            autoEquip = serializedObject.FindProperty("autoEquip"); //Global [Auto Equip]

            //Events
            OnItemEquipped = serializedObject.FindProperty("OnItemEquipped");
            OnItemUnequipped = serializedObject.FindProperty("OnItemUnequipped");
            OnItemHolster = serializedObject.FindProperty("OnItemHolster");
            OnItemUnholster = serializedObject.FindProperty("OnItemUnholster");
            OnItemDropped = serializedObject.FindProperty("OnItemDropped");
            OnItemPicked = serializedObject.FindProperty("OnItemPicked");
            OnActionPressed = serializedObject.FindProperty("OnActionPressed");
            OnActionReleased = serializedObject.FindProperty("OnActionReleased");

            //Reactions
            ItemEquippedReaction = serializedObject.FindProperty("ItemEquippedReaction");
            ItemUnequippedReaction = serializedObject.FindProperty("ItemUnequippedReaction");
            ItemHolsterReaction = serializedObject.FindProperty("ItemHolsterReaction");
            ItemUnholsterReaction = serializedObject.FindProperty("ItemUnholsterReaction");
            ItemDroppedReaction = serializedObject.FindProperty("ItemDroppedReaction");
            ItemPickedReaction = serializedObject.FindProperty("ItemPickedReaction");
            ActionReactions = serializedObject.FindProperty("ActionReactions");
            AnyActionPressedReaction = serializedObject.FindProperty("AnyActionPressedReaction");
            AnyActionReleasedReaction = serializedObject.FindProperty("AnyActionReleasedReaction");
            //equip points
            EquipPoints = serializedObject.FindProperty("EquipPoints");
            ItemOffsets = serializedObject.FindProperty("ItemOffsets"); //MWC: per-item Equip Point offsets table
            dropPoint = serializedObject.FindProperty("dropPoint");
            holsters = serializedObject.FindProperty("holsters");
            selfStoreHolster = serializedObject.FindProperty("selfStoreHolster");
            InstantDrop = serializedObject.FindProperty("InstantDrop");



            //references
            ComboManager = serializedObject.FindProperty("ComboManager");
            Animal = serializedObject.FindProperty("Animal");
            Aim = serializedObject.FindProperty("Aim");
            Pouch = serializedObject.FindProperty("Pouch");
            anim = serializedObject.FindProperty("anim");
            IKManager = serializedObject.FindProperty("IKManager");
            interactor = serializedObject.FindProperty("interactor");
            DefaultItem = serializedObject.FindProperty("DefaultItem"); //MWC — Unarmed fallback item



            SelectedEquipPointSlot = serializedObject.FindProperty("SelectedEquipPointSlot");
            SelectedHolster = serializedObject.FindProperty("SelectedHolster");
            SelectedHolsterSlot = serializedObject.FindProperty("SelectedHolsterSlot");

            HolsterList();
            EquipPointsList();

            EquipPointsReordable.index = SelectedEquipPointSlot.intValue;
            holsterReordable.index = SelectedHolster.intValue;

            EnsureAllRowsListed(); //MWC — items already assigned to a row get their entry without waiting for a re-assignment

            RunValidation();
        }

        private void EquipPointsList()
        {
            EquipPointsReordable = new ReorderableList(serializedObject, EquipPoints, true, true, true, true)
            {
                // Inside the EquipPointsList() method, replace the drawElementCallback with the following:

                onSelectCallback = (ReorderableList list) =>
                {
                    SelectedEquipPointSlot.intValue = list.index;
                },

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    rect.y += 2;

                    var element = EquipPoints.GetArrayElementAtIndex(index);
                    var ID = element.FindPropertyRelative("ID");
                    var Item = element.FindPropertyRelative("Item");
                    var Point = element.FindPropertyRelative("Point");


                    float width = rect.width / 3f;
                    Rect IDRect = new(rect.x + 12, rect.y, width - 20, EditorGUIUtility.singleLineHeight);
                    Rect pointRect = new(rect.x + width + 12, rect.y, width - 10, EditorGUIUtility.singleLineHeight);
                    Rect itemRect = new(rect.x + 2 * width + 10, rect.y, width - 10, EditorGUIUtility.singleLineHeight);

                    itemRect.width -= MOVE_W; //MWC — right gutter for the ↓↑ move buttons

                    var pre = "";
                    var tooltip = "";
                    var oldColor = GUI.backgroundColor;
                    var newColor = oldColor;

                    if (index == EquipPointsReordable.index)
                    {
                        newColor = MTools.MBlue * 2;
                    }

                    var EquipPointID = M.EquipPoints[index].ID;
                    var item = M.EquipPoints[index].Item;

                    //MWC: Dual Wielding - secondary equip points are valid placements too
                    //MWC — this used to draw an EditorGUILayout.HelpBox from inside this RECT based drawElementCallback,
                    //which throws "Getting control N's position in a group with only N controls" and aborts the whole
                    //Inspector repaint. Same treatment as the Holster list: red row + the reason on the [S]/[P] tag
                    var wrongPoint = item != null && EquipPointID != null && !M.CanUseEquipPoint(item, EquipPointID); //MWC — Equivalents count too

                    var itemObject = Item.objectReferenceValue as Component;

                    if (itemObject && itemObject.gameObject != null)
                    {
                        if (itemObject.gameObject.IsPrefab())
                        {
                            newColor = Color.green;
                            pre = "[P]";
                            tooltip = "The Item is a Prefab. It will be instantiated on start";
                        }
                        else
                        {
                            pre = "[S]";
                            tooltip = "The Item is in the scene";
                        }

                        //MWC — applied AFTER the Prefab/Scene block so a red row is not repainted green by a [P] Item
                        if (wrongPoint)
                        {
                            newColor = Color.red * 2;
                            pre = "[!]";
                            tooltip = $"Item [{item.name}] belongs in [{(item.EquipPoint != null ? item.EquipPoint.name : "<None>")}]" +
                                $"{(item.SecondaryEquipPoint != null ? $" or [{item.SecondaryEquipPoint.name}]" : "")}, not [{EquipPointID.name}].\n" +
                                $"Set it in the correct Equip Point, add [{EquipPointID.name}] as the Item's Secondary Equip Point, " +
                                $"or add it as an Equip Point Equivalent in the Item Offsets asset";
                        }

                        if (Item.objectReferenceValue != null)
                        {
                            itemRect.width -= ITEM_TAG_W + CROSS_W + CLEAR_W; //the [S]/[P] tag, the ⇄ move-to-Holster and the ✕ clear buttons

                            //MWC — the [S]/[P] tag sits between the Item field and the ↓↑ buttons (was placed past the row edge)
                            var lbRect = new Rect(rect)
                            {
                                height = EditorGUIUtility.singleLineHeight,
                                width = ITEM_TAG_W,
                                x = rect.xMax - MOVE_W - ITEM_TAG_W,
                            };

                            EditorGUI.LabelField(lbRect, new GUIContent(pre, tooltip));
                        }
                    }
                    else if (wrongPoint) newColor = Color.red * 2; //MWC — no Item object to tag, but the row still reads wrong

                    GUI.backgroundColor = newColor;

                    //MWC — a new ID/Item on this row means a new (Item + Equip Point) pair: flag it so the offsets asset is synced
                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        EditorGUI.PropertyField(IDRect, ID, GUIContent.none);
                        EditorGUI.PropertyField(pointRect, Point, GUIContent.none);
                        EditorGUI.PropertyField(itemRect, Item, GUIContent.none);

                        if (cc.changed) syncEquipRow = index;
                    }

                    GUI.backgroundColor = oldColor;

                    DrawClearItemButton(rect, false, index);                      //✕ clear the Item of this Equip Point
                    DrawMoveToOtherListButton(rect, false, index);                //MWC — ⇄ move the Item to its Holster
                    DrawMoveItemButtons(rect, false, index, M.EquipPoints.Count); //MWC — ↓↑ move the Item to another Equip Point
                },
                drawHeaderCallback = (Rect rect) =>
                {
                    float width = rect.width / 3f;
                    float padding = 8f;
                    Rect idRect = new(rect.x + 12 + padding, rect.y, width - 20 - padding, EditorGUIUtility.singleLineHeight);
                    Rect pointRect = new(rect.x + width + 12 + padding, rect.y, width - 10 - padding, EditorGUIUtility.singleLineHeight);
                    Rect itemRect = new(rect.x + 2 * width + 10 + padding, rect.y, width - 10 - 25 - padding - CLEAR_ALL_W, EditorGUIUtility.singleLineHeight);

                    EditorGUI.LabelField(idRect, "    Equip Point");
                    EditorGUI.LabelField(pointRect, "Transform Reference");
                    EditorGUI.LabelField(itemRect, "Item");


                    Rect RectEdit = new(rect)
                    {
                        height = EditorGUIUtility.singleLineHeight,
                        width = 25,
                        x = rect.width,
                    };

                    DrawClearAllButton(new Rect(RectEdit.x - PAD - CLEAR_ALL_W, rect.y, CLEAR_ALL_W, RectEdit.height), false);
                    EditOffsetsItems.boolValue = GUI.Toggle(RectEdit, EditOffsetsItems.boolValue, EditIcon, EditorStyles.miniButton);
                }
            };
        }

        private void HolsterList()
        {
            holsterReordable = new ReorderableList(serializedObject, holsters, true, true, true, true)
            {
                onSelectCallback = (ReorderableList list) =>
                {
                    SelectedHolster.intValue = list.index;
                },

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    rect.y += 2;

                    var element = holsters.GetArrayElementAtIndex(index);
                    var ID = element.FindPropertyRelative("ID");
                    var Item = element.FindPropertyRelative("Item");
                    var Point = element.FindPropertyRelative("Point");
                    //MWC — dead 'EditItemHolsterOffset' lookup removed: no such field on SItemHolster (always null)

                    var oldColor = GUI.backgroundColor;

                    var newColor = oldColor;

                    var IDRect = new Rect(rect)
                    {
                        height = EditorGUIUtility.singleLineHeight
                    };
                    IDRect.width *= 0.5f;
                    IDRect.x += 18;
                    IDRect.width -= 15;

                    if (index == holsterReordable.index)
                    {
                        newColor = MTools.MBlue * 2;
                    }

                    GUI.backgroundColor = newColor;

                    //MWC — a new Holster ID means a new (Item + Holster) pair: flag the row so the offsets asset is synced
                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        EditorGUI.PropertyField(IDRect, ID, GUIContent.none);
                        if (cc.changed) syncHolsterRow = index;
                    }

                    var pre = "";
                    var tooltip = "";



                    var HolsterID = M.holsters[index].ID;
                    var item = M.holsters[index].Item;

                    //MWC — [Secondary Holster] counts as a valid home: SuperItem.IsAllowedHolster accepts the Item's own
                    //Holster ID AND its Secondary one, so a Dagger authored for [Left Holster] with [Right Holster] as
                    //its Secondary is legitimately placed in either. This used to compare against item.HolsterID alone
                    //and flagged every secondary placement as an error.
                    //It also drew an EditorGUILayout.HelpBox from inside this RECT based drawElementCallback, which
                    //throws "Getting control N's position in a group with only N controls" and aborts the whole
                    //Inspector repaint. The row tints red and says why in its tooltip instead.
                    var wrongHolster = item != null && HolsterID != null && !item.IsAllowedHolster(HolsterID);

                    var itemObject = Item.objectReferenceValue as Component;
                    if (itemObject && itemObject.gameObject != null)
                    {
                        if (itemObject.gameObject.IsPrefab())
                        {
                            newColor = Color.green;
                            pre = "[P]";
                            tooltip = "The Item is a Prefab. It will be instantiated on start";
                        }
                        else
                        {
                            pre = "[S]";
                            tooltip = "The Item is in the scene";
                        }
                    }



                    IDRect.x += IDRect.width + 10;
                    IDRect.width -= MOVE_W + SLOT_W + PAD; //MWC — right gutter for the Slot field and the ↓↑ move buttons

                    //MWC — the wrong-Holster warning rides the [S]/[P] tag: the tag turns into [!] and carries the
                    //reason. Applied AFTER the Prefab/Scene block so a red row is not repainted green by a [P] Item
                    if (wrongHolster)
                    {
                        newColor = Color.red * 2;
                        pre = "[!]";
                        tooltip = $"Item [{item.name}] belongs in [{(item.HolsterID != null ? item.HolsterID.name : "<None>")}]" +
                            $"{(item.SecondaryHolster != null ? $" or [{item.SecondaryHolster.name}]" : "")}, not [{HolsterID.name}].\n" +
                            $"Set it in the correct Holster, or add [{HolsterID.name}] as the Item's Secondary Holster";
                    }

                    if (Item.objectReferenceValue != null)
                    {
                        IDRect.width -= ITEM_TAG_W + CROSS_W + CLEAR_W; //the [S]/[P] tag, the ⇄ move-to-Equip-Point and the ✕ clear buttons

                        //MWC — the [S]/[P] tag sits between the Item field and the Slot field (was placed past the row edge)
                        var lbRect = new Rect(rect)
                        {
                            height = EditorGUIUtility.singleLineHeight,
                            width = ITEM_TAG_W,
                            x = rect.xMax - MOVE_W - SLOT_W - PAD - ITEM_TAG_W,
                        };

                        EditorGUI.LabelField(lbRect, new GUIContent(pre, tooltip));
                    }


                    GUI.backgroundColor = newColor;

                    //MWC — the Item field also keys the offsets entry, so a change here syncs the row too
                    using (var cc = new EditorGUI.ChangeCheckScope())
                    {
                        EditorGUI.PropertyField(IDRect, Item, GUIContent.none);
                        if (cc.changed) syncHolsterRow = index;
                    }

                    GUI.backgroundColor = oldColor;

                    DrawHolsterSlotField(rect, index);                          //MWC — Slot index, stored in the Item Offsets asset
                    DrawClearItemButton(rect, true, index);                     //✕ clear the Item of this Holster
                    DrawMoveToOtherListButton(rect, true, index);               //MWC — ⇄ move the Item to its Equip Point
                    DrawMoveItemButtons(rect, true, index, M.holsters.Count);   //MWC — ↓↑ move the Item to another Holster
                },
                drawHeaderCallback = (Rect rect) =>
                {
                    float thirdWidth = rect.width * 0.5f;
                    float padding = 5f;
                    Rect idRect = new(rect.x + 35, rect.y, thirdWidth * 1f - padding - 15, EditorGUIUtility.singleLineHeight);
                    Rect itemRect = new(rect.x + thirdWidth + 28, rect.y, thirdWidth * 1.5f - padding, EditorGUIUtility.singleLineHeight);
                    EditorGUI.LabelField(idRect, "    Holster");
                    EditorGUI.LabelField(itemRect, "Item");

                    //MWC — names the Slot column drawn in each row (the field itself has no label)
                    var slotRect = new Rect(rect.xMax - MOVE_W - SLOT_W, rect.y, SLOT_W + MOVE_W, EditorGUIUtility.singleLineHeight);
                    EditorGUI.LabelField(slotRect, new GUIContent("Slot", "Index of the Holster's Transform Slots array the item is parented to.\nStored per (Item + Holster) in the Item Offsets asset"));

                    //Left of the Slot label so it does not cover the column name (the ⇄ / [S]/[P] area of the rows sits below it)
                    DrawClearAllButton(new Rect(slotRect.x - PAD - CLEAR_ALL_W, rect.y, CLEAR_ALL_W, EditorGUIUtility.singleLineHeight), true);

                    Rect RectEdit = new(rect)
                    {
                        height = EditorGUIUtility.singleLineHeight,
                        width = 25,
                        x = rect.width,
                    };


                    EditOffsetsItems.boolValue = GUI.Toggle(RectEdit, EditOffsetsItems.boolValue, EditIcon, EditorStyles.miniButton);
                },
                onAddCallback = (ReorderableList list) =>
                {
                    holsters.arraySize++;

                    //MWC — a new array element is zero-initialized (Capacity 0 = No Limit, Use Constant off),
                    //so the default Capacity has to be written by hand. Field initializers do not run here.
                    var newHolster = holsters.GetArrayElementAtIndex(holsters.arraySize - 1);
                    var capacity = newHolster.FindPropertyRelative("m_Capacity");

                    if (capacity != null)
                    {
                        capacity.FindPropertyRelative("UseConstant").boolValue = true;
                        capacity.FindPropertyRelative("ConstantValue").intValue = SItemHolster.DefaultCapacity;
                    }

                    serializedObject.ApplyModifiedProperties();
                }
            };
        }


        private readonly string[] Tab1 = new string[] { "General", "Equip Points", "Holsters", "Inputs" };
        private readonly string[] Tab2 = new string[] { "References", "Events", "Reactions", "Debug" };

        //MWC — Tab1 indexes used by the ⇄ button, and the out-of-range index that leaves a toolbar unselected
        private const int EQUIP_TAB = 1;
        private const int HOLSTER_TAB = 2;
        private const int NO_TAB = 4;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawValidation();

            Editor_Tabs1.intValue = GUILayout.Toolbar(Editor_Tabs1.intValue, Tab1);

            if (Editor_Tabs1.intValue != NO_TAB) Editor_Tabs2.intValue = NO_TAB;

            Editor_Tabs2.intValue = GUILayout.Toolbar(Editor_Tabs2.intValue, Tab2);

            if (Editor_Tabs2.intValue != NO_TAB) Editor_Tabs1.intValue = NO_TAB;

            //First Tabs
            int Selection1 = Editor_Tabs1.intValue;

            switch (Selection1)
            {
                case 0: ShowGeneral(); break;
                case 1: ShowEquipPoints(); break;
                case 2: ShowHolster(); break;
                case 3: ShowInputs(); break;
                default:
                    break;
            }

            //second Tabs
            int Selection2 = Editor_Tabs2.intValue;


            switch (Selection2)
            {
                case 0: ShowReferences(); break;
                case 1: ShowEvents(); break;
                case 2: ShowReactions(); break;
                case 3: ShowDebug(); break;
                default:
                    break;
            }

            //Re-validate only after the edit is applied, so the report never lags one repaint behind the data
            if (serializedObject.ApplyModifiedProperties()) RunValidation();

            //MWC — deferred to here (never mid-draw): the rows are applied, so the offsets asset can be synced from them
            if (syncEquipRow >= 0 && syncEquipRow < M.EquipPoints.Count) SyncRow(false, syncEquipRow);
            if (syncHolsterRow >= 0 && syncHolsterRow < M.holsters.Count) SyncRow(true, syncHolsterRow);

            syncEquipRow = syncHolsterRow = -1;
        }

        #region Validation
        //Same idea as the Super Item validator: the report is rebuilt on OnEnable and after every edit (never on a
        //plain repaint), and the Refresh button re-runs it after something was fixed elsewhere (Animator, Input, Items)
        private readonly List<string> validationErrors = new();   //Setup that breaks at runtime (null refs, bad hierarchy)
        private readonly List<string> validationWarnings = new(); //Setup that works but has a feature silently off

        /// <summary> Set only by the Refresh button, so the "all clear" message shows on an explicit check, not on every repaint </summary>
        private bool ValidationRefreshed;

        private static GUIContent s_RefreshContent;

        /// <summary> Refresh button content (icon + label), cached to avoid a per-repaint allocation </summary>
        private static GUIContent RefreshContent
        {
            get
            {
                s_RefreshContent ??= new GUIContent(" Refresh", EditorGUIUtility.IconContent("d_Refresh").image,
                    "Re-run the validation to check if the issues are gone");
                return s_RefreshContent;
            }
        }

        /// <summary> Draws the validation report above the tabs: one box for errors, one for warnings, and the Refresh button </summary>
        private void DrawValidation()
        {
            bool anyIssue = validationErrors.Count > 0 || validationWarnings.Count > 0;

            if (validationErrors.Count > 0)
                EditorGUILayout.HelpBox("These will break the Manager at runtime:\n" + string.Join("\n", validationErrors), MessageType.Error);

            if (validationWarnings.Count > 0)
            {
                ShowWarnings.boolValue = EditorGUILayout.ToggleLeft(new GUIContent($"Show Warnings ({validationWarnings.Count})", "Show or hide the Warnings help box"), ShowWarnings.boolValue);

                if (ShowWarnings.boolValue)
                    EditorGUILayout.HelpBox("These features will not work as expected:\n" + string.Join("\n", validationWarnings), MessageType.Warning);
            }

            if (ValidationRefreshed && !anyIssue)
                EditorGUILayout.HelpBox("No issues found. References, Equip Points, Holsters, Inputs and Animator parameters are valid.", MessageType.Info);

            if (anyIssue && GUILayout.Button(RefreshContent, EditorStyles.miniButton, GUILayout.Width(75)))
            {
                RunValidation();
                ValidationRefreshed = true;
            }
        }

        /// <summary> Rebuilds the error and warning lists from the current Manager setup </summary>
        private void RunValidation()
        {
            validationErrors.Clear();
            validationWarnings.Clear();
            ValidationRefreshed = false;

            ValidateReferences();
            ValidateHierarchy();
            ValidateEquipPoints();
            ValidateHolsters();
            if (!Application.isPlaying) ValidateStartingItems(); //MWC — edit-time rules: at runtime an Item legitimately moves between Holsters and Equip Points
            ValidateInputs();
            ValidateAnimator();
        }

        /// <summary> Character root every Equip Point, Holster Slot and Drop Point must hang from (the Animal, or the Manager itself) </summary>
        private Transform CharacterRoot => M.Animal != null ? M.Animal.transform : M.transform;

        /// <summary> Required references, and references that point at a DIFFERENT character </summary>
        private void ValidateReferences()
        {
            if (M.Animal == null) validationErrors.Add("• [Animal] is missing. The Manager listens to the Animal States, Modes and Stances");
            if (M.Anim == null) validationErrors.Add("• [Animator] is missing. Item animations and the Hand Type parameters need it");

            if (M.Aim == null) validationWarnings.Add("• [Aim] is missing: Aiming Actions and Projectiles have no aim direction");
            if (M.Pouch == null) validationWarnings.Add("• [Pouch Manager] is missing: Reload and Ammo Actions have nothing to draw from");
            if (M.ComboManager == null) validationWarnings.Add("• [Combo Manager] is missing: Combo Item Processors cannot play");
            if (M.IKManager == null) validationWarnings.Add("• [IK Manager] is missing: Item IK Sets (hand/aim IK) are disabled");
            if (M.Interactor == null) validationWarnings.Add("• [Interactor] is missing: Items on the ground cannot be picked by focusing them");

            if (M.ItemOffsets == null)
                validationWarnings.Add("• No [Item Offsets] asset: every Item is equipped and holstered with no offset (Pos 0, Rot 0, Scale 1)");

            //A reference on another character works... until that character is disabled or destroyed
            CheckSameCharacter(M.Animal, "Animal");
            CheckSameCharacter(M.Anim, "Animator");
            CheckSameCharacter(M.Aim, "Aim");
            CheckSameCharacter(M.Pouch, "Pouch Manager");
            CheckSameCharacter(M.ComboManager, "Combo Manager");
            CheckSameCharacter(M.IKManager, "IK Manager");
            CheckSameCharacter(M.Interactor, "Interactor");
        }

        /// <summary> Warns when a component reference lives outside the Manager's own hierarchy </summary>
        private void CheckSameCharacter(Component reference, string label)
        {
            if (reference == null) return;

            var root = M.transform.root;

            if (reference.transform.root != root)
                validationWarnings.Add($"• [{label}] → [{reference.name}] belongs to another hierarchy [{reference.transform.root.name}], not to [{root.name}]");
        }

        /// <summary> Drop Point, Equip Points and Holster Slots must be Transforms inside the character hierarchy </summary>
        private void ValidateHierarchy()
        {
            var root = CharacterRoot;

            if (IsEmpty(M.dropPoint))
                validationWarnings.Add("• [Drop Point] is empty: released Items are dropped in place instead of at the Drop Point");
            else if (Outside(M.dropPoint, root))
                validationErrors.Add($"• [Drop Point] → [{M.dropPoint.ConstantValue.name}] is not inside the character [{root.name}]. Dropped items will appear away from the character");

            for (int i = 0; i < M.EquipPoints.Count; i++)
            {
                var e = M.EquipPoints[i];
                if (e == null) continue;

                var id = e.ID != null ? e.ID.name : $"Element {i}";

                if (IsEmpty(e.Point))
                    validationErrors.Add($"• Equip Point [{id}] has no [Point] Transform. Items cannot be parented to it");
                else if (Outside(e.Point, root))
                    validationErrors.Add($"• Equip Point [{id}] → [{e.Point.ConstantValue.name}] is not inside the character [{root.name}]. Equipped Items will not follow the character");
            }

            for (int i = 0; i < M.holsters.Count; i++)
            {
                var h = M.holsters[i];
                if (h == null) continue;

                var id = h.ID != null ? h.ID.name : $"Element {i}";

                if (h.Slots == null || h.Slots.Count == 0)
                {
                    validationErrors.Add($"• Holster [{id}] has no Slots. Items cannot be stored on it");
                    continue;
                }

                for (int s = 0; s < h.Slots.Count; s++)
                {
                    var slot = h.Slots[s];

                    if (IsEmpty(slot))
                        validationErrors.Add($"• Holster [{id}] Slot {s} has no Transform");
                    else if (Outside(slot, root))
                        validationErrors.Add($"• Holster [{id}] Slot {s} → [{slot.ConstantValue.name}] is not inside the character [{root.name}]. Holstered Items will not follow the character");
                }
            }
        }

        /// <summary> True when a direct Transform reference has nothing assigned. Variable/Member references resolve at runtime, so they count as filled </summary>
        private static bool IsEmpty(Scriptables.TransformReference reference) =>
            reference == null || (reference.UseConstant && !reference.UseMember && reference.ConstantValue == null);

        /// <summary> True when the reference holds a direct Transform outside the given hierarchy. Variable/Member references resolve at runtime, so they are skipped </summary>
        private static bool Outside(Scriptables.TransformReference reference, Transform root) =>
            reference != null && reference.UseConstant && !reference.UseMember &&
            reference.ConstantValue != null && !reference.ConstantValue.SameHierarchy(root);

        /// <summary> Equip Point IDs: none missing, none repeated </summary>
        private void ValidateEquipPoints()
        {
            if (M.EquipPoints.Count == 0)
                validationErrors.Add("• There are no Equip Points. Items cannot be equipped");

            var seen = new HashSet<EquipPointID>();

            for (int i = 0; i < M.EquipPoints.Count; i++)
            {
                var e = M.EquipPoints[i];
                if (e == null) continue;

                if (e.ID == null)
                    validationErrors.Add($"• Equip Point [Element {i}] has no [ID]. The Manager cannot map Items to it");
                else if (!seen.Add(e.ID))
                    validationErrors.Add($"• Equip Point ID [{e.ID.name}] is used twice. Only the first one will be registered");
            }
        }

        /// <summary> Holster IDs, Capacity vs Slots, and the Holster Input </summary>
        private void ValidateHolsters()
        {
            var seen = new HashSet<HolsterID>();

            for (int i = 0; i < M.holsters.Count; i++)
            {
                var h = M.holsters[i];
                if (h == null) continue;

                var id = h.ID != null ? h.ID.name : $"Element {i}";

                if (h.ID == null)
                    validationErrors.Add($"• Holster [Element {i}] has no [ID]. The Manager cannot map Items to it");
                else if (!seen.Add(h.ID))
                    validationErrors.Add($"• Holster ID [{h.ID.name}] is used twice. Only the first one will be registered");

                int slots = h.Slots != null ? h.Slots.Count : 0;

                if (h.Capacity > slots && slots > 0)
                    validationWarnings.Add($"• Holster [{id}] Capacity [{h.Capacity}] is greater than its [{slots}] Slot(s). Only {slots} Item(s) can be stored");

                if (h.Input == null || string.IsNullOrWhiteSpace(h.Input.Value))
                    validationWarnings.Add($"• Holster [{id}] has no [Input]. Its Item can only be drawn by code, Reactions or the AI");
            }
        }

        /// <summary> Items assigned on the Manager before Play: right ID, not the same scene instance twice </summary>
        private void ValidateStartingItems()
        {
            var assigned = new Dictionary<SuperItem, string>();

            foreach (var e in M.EquipPoints)
            {
                if (e == null || e.Item == null) continue;

                var where = $"Equip Point [{(e.ID != null ? e.ID.name : "No ID")}]";

                if (e.ID != null && !M.CanUseEquipPoint(e.Item, e.ID))
                    validationErrors.Add($"• Item [{e.Item.name}] on {where} uses [{(e.Item.EquipPoint != null ? e.Item.EquipPoint.name : "No Equip Point")}]. Move it to the correct Equip Point");

                CheckStartingItem(e.Item, where, assigned);
            }

            foreach (var h in M.holsters)
            {
                if (h == null || h.Item == null) continue;

                var where = $"Holster [{(h.ID != null ? h.ID.name : "No ID")}]";

                if (h.ID != null && !h.Item.IsAllowedHolster(h.ID))
                    validationErrors.Add($"• Item [{h.Item.name}] on {where} uses [{(h.Item.HolsterID != null ? h.Item.HolsterID.name : "No Holster")}]. Move it to the correct Holster");

                CheckStartingItem(h.Item, where, assigned);
            }

            //The Default Item is equipped in place (no Equip Point, no parenting), so it has to already live on the character
            if (M.DefaultItem != null)
            {
                CheckStartingItem(M.DefaultItem, "[Default Item]", assigned);

                //MWC — unlike the Equip Point/Holster Items, the Default Item is NOT instantiated, so it must already
                //live on the character. The scene comparison is gone: SameHierarchy catches a Project asset too
                //(it is not under the character), while comparing scenes never did when the Manager itself was a Prefab
                if (!M.DefaultItem.transform.SameHierarchy(CharacterRoot))
                    validationErrors.Add($"• [Default Item] [{M.DefaultItem.name}] is not inside the character [{CharacterRoot.name}]. It is equipped in place (never instantiated), so it must be a child of the character");
            }
        }

        /// <summary> A starting Item cannot start in two places (a Project Prefab is valid: the Manager instantiates it) </summary>
        private void CheckStartingItem(SuperItem item, string where, Dictionary<SuperItem, string> assigned)
        {
            //MWC — the "must be a scene instance" error is gone: SuperItemManager.Item_Instance instantiates a Prefab
            //authored on an Equip Point or a Holster. The Default Item is the exception, checked in ValidateStartingItems.
            //The same Prefab may also be authored in several places (each one gets its own instance), so it is not a duplicate
            if (item.gameObject.IsPrefab()) return;

            if (assigned.TryGetValue(item, out var first))
                validationErrors.Add($"• Item [{item.name}] is assigned on {where} and also on {first}. An Item can only start in one place");
            else
                assigned[item] = where;
        }

        /// <summary> Action Inputs and Holster Inputs: IDs, names, and whether the Input Source knows them </summary>
        private void ValidateInputs()
        {
            var source = M.gameObject.FindInterface<IInputSource>();

            if (M.ActionsInput != null)
            {
                var seen = new HashSet<ItemActionID>();

                for (int i = 0; i < M.ActionsInput.Count; i++)
                {
                    var row = M.ActionsInput[i];
                    if (row == null || !row.active) continue; //Inactive rows are never connected, nothing to check

                    var label = row.ID != null ? row.ID.name : $"Element {i}";

                    if (row.ID == null)
                        validationWarnings.Add($"• Action Input [Element {i}] has no [Action ID]. It will be ignored");
                    else if (!seen.Add(row.ID))
                        validationWarnings.Add($"• Action Input [{row.ID.name}] is listed twice. Only the first row will be connected");

                    var name = row.value != null ? row.value.Value : null;

                    if (string.IsNullOrWhiteSpace(name))
                        validationWarnings.Add($"• Action Input [{label}] has no Input name");
                    else
                        CheckInputExists(source, name, $"Action Input [{label}]");
                }
            }

            foreach (var h in M.holsters)
            {
                if (h == null || h.Input == null || string.IsNullOrWhiteSpace(h.Input.Value)) continue; //Empty Holster Inputs are reported by ValidateHolsters
                CheckInputExists(source, h.Input.Value, $"Holster [{(h.ID != null ? h.ID.name : "No ID")}] Input");
            }

            //No Input Source is not an issue: AI characters drive the Manager from the Brain, not from inputs
        }

        /// <summary> Asks the Input Source (through its physical ID) whether an input name exists on it. Sources that cannot tell are skipped </summary>
        private void CheckInputExists(IInputSource source, string inputName, string label)
        {
            if (source is not IInputPhysicalID physical) return; //The source cannot answer at edit time

            if (string.IsNullOrEmpty(physical.PhysicalInputID(inputName)))
                validationWarnings.Add($"• {label} → [{inputName}] was not found on [{source.transform.name}]. Check the spelling on the Input component");
        }

        /// <summary> Animator Controller and the three parameters the Manager writes </summary>
        private void ValidateAnimator()
        {
            if (M.Anim == null) return; //Already an error in ValidateReferences

            var controller = GetController(M.Anim);

            if (controller == null)
            {
                validationErrors.Add($"• Animator [{M.Anim.name}] has no Animator Controller. Item animations cannot play");
                return;
            }

            var parameters = controller.parameters;

            CheckParameter(parameters, M.m_ItemLeftHandType, UnityEngine.AnimatorControllerParameterType.Int, "Left Item Type", "the Left Hand/Arm layer will not activate");
            CheckParameter(parameters, M.m_ItemRightHandType, UnityEngine.AnimatorControllerParameterType.Int, "Right Item Type", "the Right Hand/Arm layer will not activate");
            CheckParameter(parameters, M.m_Mirror, UnityEngine.AnimatorControllerParameterType.Bool, "Mirror", "left-handed Items will use the right-hand animations");
        }

        /// <summary> Warns when a parameter is missing from the controller or has the wrong type </summary>
        private void CheckParameter(UnityEngine.AnimatorControllerParameter[] parameters, string name, UnityEngine.AnimatorControllerParameterType type, string label, string consequence)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                validationWarnings.Add($"• Animator parameter [{label}] is empty: {consequence}");
                return;
            }

            foreach (var p in parameters)
            {
                if (p.name != name) continue;

                if (p.type != type)
                    validationWarnings.Add($"• Animator parameter [{name}] is a {p.type}, but the Manager writes a {type}: {consequence}");

                return;
            }

            validationWarnings.Add($"• Animator parameter [{name}] ({label}) is not on the controller: {consequence}");
        }

        /// <summary> The parameters the Manager writes to the Animator: name, type </summary>
        private (string name, UnityEngine.AnimatorControllerParameterType type)[] ManagerParameters() => new[]
        {
            (M.m_ItemLeftHandType, UnityEngine.AnimatorControllerParameterType.Int),
            (M.m_ItemRightHandType, UnityEngine.AnimatorControllerParameterType.Int),
            (M.m_Mirror, UnityEngine.AnimatorControllerParameterType.Bool),
        };

        /// <summary> Draws the [Add Missing Parameters] button when the controller lacks a parameter the Manager writes </summary>
        private void DrawAddMissingParameters()
        {
            if (M.Anim == null) return;

            var controller = GetController(M.Anim);
            if (controller == null) return;

            var existing = controller.parameters;
            var missing = new List<string>();

            foreach (var (name, _) in ManagerParameters())
                if (!string.IsNullOrWhiteSpace(name) && System.Array.FindIndex(existing, p => p.name == name) < 0) missing.Add(name);

            if (missing.Count == 0) return;

            if (GUILayout.Button(new GUIContent($"Add Missing Animator Parameters ({missing.Count})",
                $"Adds to [{controller.name}]:\n• " + string.Join("\n• ", missing)), EditorStyles.miniButton))
            {
                Undo.RecordObject(controller, "Add Missing Animator Parameters");

                foreach (var (name, type) in ManagerParameters())
                    if (missing.Contains(name)) { controller.AddParameter(name, type); missing.Remove(name); }

                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
                RunValidation();
            }
        }

        /// <summary> Resolves the editable controller behind an Animator, looking through Override Controllers </summary>
        private static UnityEditor.Animations.AnimatorController GetController(Animator anim)
        {
            var runtime = anim.runtimeAnimatorController;

            while (runtime is AnimatorOverrideController over) runtime = over.runtimeAnimatorController;

            return runtime as UnityEditor.Animations.AnimatorController;
        }
        #endregion

        private readonly GUIContent GC_Anim = new("Animator", "Reference to the Animator Component");

        private void ShowReferences()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(Animal);
                EditorGUILayout.PropertyField(anim, GC_Anim);
                DrawAddMissingParameters();
                EditorGUILayout.PropertyField(Aim);
                EditorGUILayout.PropertyField(Pouch);
                EditorGUILayout.PropertyField(ComboManager);
                EditorGUILayout.PropertyField(interactor);
                EditorGUILayout.PropertyField(IKManager);
            }

            //MWC — Default Item (Unarmed fallback): auto-equipped when no weapon is in hand
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(DefaultItem);
            }
        }

        private void ShowGeneral()
        {
            //MWC — [Equip Behaviour] one group for the four Manager-wide switches. They used to be scattered across
            //three tabs: Self Store Holster lived on the Holsters tab and Fast Equip Back inside the No Items group of
            //the Equip Points tab, which is why neither could be found
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    useFastWeapons.isExpanded = MalbersEditor.Foldout(useFastWeapons.isExpanded, "Equip Behaviour");
                    MalbersEditor.DrawDebugIcon(debug);
                }

                if (useFastWeapons.isExpanded)
                {
                    EditorGUILayout.PropertyField(autoEquip, new GUIContent("Auto Equip",
                        "ON: every Item picked from the world (Interactor, AI Pick Task, Pick reactions) goes straight to the hands, whatever the Item's own [Auto Equip] says.\n\n" +
                        "OFF (default): the Item's own [Auto Equip] decides (hands or Holster).\n\n" +
                        "Picks only: the starting Items authored on the Equip Points and Holsters are not affected."));

                    EditorGUILayout.PropertyField(useFastWeapons, new GUIContent("Use Fast Weapons",
                        "[Fast Weapons] (Assassin's Creed style): Pressing the input of a HOLSTERED item's [Fast Equip] Action " +
                        "draws the item instantly (skipping the Equip animation) and starts that Action.\n\n" +
                        "An equipped Item that is mid-Action keeps its own presses: the physical button of the Action it is playing, and any Action ID it also has. Other presses can still fast draw.\n\n" +
                        "Equipped Actions with [Valid On Fast Weapons] disabled yield their input to it."));

                    EditorGUILayout.PropertyField(selfStoreHolster, new GUIContent("Self Store Holster",
                        "Pressing the Holster Input of an Item that is ALREADY in the hands stores it back into that holster.\n\n" +
                        "OFF (default): the press is ignored, and the Item can only be put away by drawing another one."));

                    EditorGUILayout.PropertyField(InstantDrop, new GUIContent("Instant Drop",
                        "ON: dropping an Item in the hands is instant, with no animation (the old behavior).\n\n" +
                        "OFF (default): the Item's Drop Mode animation is played first, and the Item leaves the hand after its Equip Duration.\n\n" +
                        "Holstered Items are always dropped instantly."));

                    //MWC — Items are always put away instantly; only the way back is configurable
                    EditorGUILayout.PropertyField(FastEquipBack, new GUIContent("Fast Equip Back",
                        "How the Items held by a No Items [State|Stance|Mode] are drawn back when the block ends.\n\n" +
                        "• OFF (default): the Equip Mode animation is played (e.g. when the Open Chest animation ends, the weapon is drawn normally).\n" +
                        "• ON: the Items reappear in the hands instantly, with no Equip animation.\n\n" +
                        "Putting them away is always instant, with no Unequip (Store Weapon) animation."));

                    EditorGUILayout.PropertyField(AutoStore, new GUIContent("Auto Holster Time",
                        "Seconds an equipped Item has to sit doing NOTHING (no Action of its Active Set playing) before it is " +
                        "stored back into its holster with the normal Store animation.\n\n" +
                        "Zero or less disables the auto holster for every Item."));
                }

                //EditorGUILayout.HelpBox("Global Conditions every Item Action must pass to START. A playing Action (e.g. Aim held down) is never cut by it. " +
                //    "Evaluated on this Manager before the Action's own Conditions and the Set Conditions.\n" +
                //    "E.g. [Mode is NOT Playing: Dodge] blocks every Action of every Item while the character dodges.", MessageType.None);

                EditorGUILayout.PropertyField(crossItemPriority, new GUIContent("Cross Item Priority",
                    "Compare Action [Priority] across the equipped Items: when two Items answer the SAME Action ID, only the highest Priority one starts. " +
                    "Equal Priorities both start, so dual wielding keeps swinging both hands."));
            }

            //[Lock]: the hands are needed for something else (carrying a box...). Every equipped Item goes away
            //instantly and the Manager refuses every Item operation until it is Unlocked
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                lockMode.isExpanded = MalbersEditor.Foldout(lockMode.isExpanded, "Lock");

                if (lockMode.isExpanded)
                {
                    EditorGUILayout.PropertyField(lockMode, new GUIContent("Lock Mode",
                        "Lock the Manager (Lock(true) / Unlock(), or the [Manager] Global Reaction) while the character needs its hands " +
                        "for something else, e.g. carrying a box. Every equipped Item is put away instantly and comes back on Unlock. " +
                        "While Locked no Action, Equip, Holster, Drop, Pick or Fast Weapons draw is accepted "));

                    EditorGUILayout.PropertyField(OnLocked, new GUIContent("On Locked (bool)",
                        "TRUE right after the Manager is Locked (the Items are already away). FALSE right after it is Unlocked (before the Items are drawn back)"));
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(ActionConditions, new GUIContent("Global Action Conditions"));
                EditorGUI.indentLevel--;
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {

                m_hitLayer.isExpanded = MalbersEditor.Foldout(m_hitLayer.isExpanded, "Target Interaction");

                if (m_hitLayer.isExpanded)
                {
                    EditorGUILayout.PropertyField(m_hitLayer);

                    EditorGUILayout.PropertyField(triggerInteraction);
                    EditorGUILayout.PropertyField(Tags);
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                m_ItemLeftHandType.isExpanded = MalbersEditor.Foldout(m_ItemLeftHandType.isExpanded, "Animator Parameters");

                if (m_ItemLeftHandType.isExpanded)
                {
                    EditorGUILayout.PropertyField(m_ItemLeftHandType);
                    EditorGUILayout.PropertyField(m_ItemRightHandType);
                    EditorGUILayout.PropertyField(m_Mirror);
                }
            }
        }

        private void ShowEquipPoints()
        {
            using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(dropPoint);
                MalbersEditor.DrawDebugIcon(debug);
            }

            ShowItemOffsets(false); //MWC — shared with the Holsters tab: this one draws the Equip Point entries



            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(NoItemsInState);
                EditorGUILayout.PropertyField(NoItemsStance);
                EditorGUILayout.PropertyField(NoItemsMode);
                //MWC — [Fast Equip Back] moved to the [Equip Behaviour] group on the General tab
                EditorGUI.indentLevel--;
            }



            if (Application.isPlaying)
            {
                using (new EditorGUI.DisabledGroupScope(true))
                    holsterReordable.DoLayoutList();
            }

            EquipPointsReordable.DoLayoutList();


            var index = EquipPointsReordable.index;

            if (index >= 0 && index < EquipPointsReordable.count)
            {
                var EquipPointID = M.EquipPoints[index].ID;
                var item = M.EquipPoints[index].Item;

                var element = EquipPoints.GetArrayElementAtIndex(index);
                var OnStore = element.FindPropertyRelative("OnStore");
                var OnEquip = element.FindPropertyRelative("OnEquip");
                var OnUnequip = element.FindPropertyRelative("OnUnequip");
                //MWC — dead 'EditItemEquipOffset' lookup removed: no such field on SEquipPoint (always null)
                var itemField = element.FindPropertyRelative("Item");

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    //MWC: Dual Wielding - secondary equip points are valid placements too
                    if (item != null && EquipPointID != null && !M.CanUseEquipPoint(item, EquipPointID))
                    {
                        EditorGUILayout.HelpBox($"Item [{item.name}] uses [{item.EquipPoint.name}] instead of [{EquipPointID.name}]. Please set it in the correct Equip Point", MessageType.Error);
                    }

                    EditorGUILayout.PropertyField(OnStore);

                    EditorGUI.indentLevel++;
                    //MWC — Equip Point reactions. The dynamic target is stated in the label because it is
                    //the item, not the character, which is what the reactions inside resolve against.
                    EditorGUILayout.PropertyField(OnEquip, new GUIContent(k_OnEquipLabel, k_OnEquipTip));
                    EditorGUILayout.PropertyField(OnUnequip, new GUIContent(k_OnUnequipLabel, k_OnUnequipTip));
                    EditorGUI.indentLevel--;
                }

                DrawInternalInspector(itemField, "Equipped Item");
            }
        }

        //MWC: Per-item offsets table (Equip Points + Holsters). It is the only source of offsets: no asset/entry → identity.
        //Drawn in BOTH the Equip Points and the Holsters tab. Entries are kept in sync automatically (see SyncRow), so
        //there is no Save button: assigning an Item to a row adds its entry, or updates it when the item is posed in place.
        private void ShowItemOffsets(bool holsterTab) //MWC — which table of the asset this tab authors
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(ItemOffsets);

                if (ItemOffsets.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("No Item Offsets asset: every item will be equipped/holstered with no offset (Pos 0, Rot 0, Scale 1)", MessageType.Warning);
                    return;
                }

                //Play Mode only: write the pose of the held Items (moved by hand in the Scene/Inspector) back to the asset
                if (Application.isPlaying && GUILayout.Button(s_UpdateRuntimeOffset))
                    SaveRuntimeOffsets(M);

                //MWC — the asset's own table, drawn inline: Holster entries on the Holsters tab, Equip Point entries on
                //the Equip Points tab. Same ReorderableList the SuperItemOffsets inspector uses (see OffsetTable).
                s_ShowOffsetsTable = MalbersEditor.Foldout(s_ShowOffsetsTable, holsterTab ? "Holster Offsets" : "Equip Point Offsets");

                if (!s_ShowOffsetsTable) return;

                if (!UpdateOffsetTables()) return;

                var table = holsterTab ? holsterOffsetTable : equipOffsetTable;

                //Vector3 fields need wide mode to stay on a single line inside the list rects
                bool wideMode = EditorGUIUtility.wideMode;
                EditorGUIUtility.wideMode = true;

                offsetsSO.Update();
                table.Draw(string.Empty); //The foldout above already titles the table
                offsetsSO.ApplyModifiedProperties(); //Undo is handled here

                EditorGUIUtility.wideMode = wideMode;
            }
        }

        //MWC — Offsets asset drawn inline in the Equip Points / Holsters tabs
        private static bool s_ShowOffsetsTable = false; //Shared foldout state, close by default
        private SerializedObject offsetsSO;
        private SuperItemOffsets cachedOffsets;
        private SuperItemOffsetsEditor.OffsetTable equipOffsetTable, holsterOffsetTable;

        //MWC — (Re)builds the tables when the assigned asset changes. False when there is nothing to draw.
        private bool UpdateOffsetTables()
        {
            var offsets = ItemOffsets.objectReferenceValue as SuperItemOffsets;

            if (offsets == null) return false;

            if (offsets != cachedOffsets || offsetsSO == null || offsetsSO.targetObject == null)
            {
                cachedOffsets = offsets;
                offsetsSO = new SerializedObject(offsets);
                equipOffsetTable = SuperItemOffsetsEditor.CreateEquipTable(offsetsSO);
                holsterOffsetTable = SuperItemOffsetsEditor.CreateHolsterTable(offsetsSO);
            }
            return true;
        }

        //MWC — Rows whose ID/Item changed this frame. The sync runs after ApplyModifiedProperties in OnInspectorGUI,
        //never mid-draw, so the ReorderableList callbacks keep drawing against a stable SerializedObject.
        private int syncEquipRow = -1;
        private int syncHolsterRow = -1;

        //MWC: Keeps the Item Offsets asset in sync with the rows — this replaces the old Save button.
        //Assigning an Item to a row (or changing a row's ID) adds the (Item + Equip Point/Holster) entry when it's
        //missing. When the item is actually parented to its Point/Slot, its current local transform is captured as the
        //offset — that is the pose the user just authored in the Scene. Items that are not parented (the usual case:
        //they live loose in the scene until picked up) only get their entry created, so an authored offset is never
        //overwritten by an arbitrary transform.
        private void SyncRow(bool holsterList, int index)
        {
            var so = M.ItemOffsets;
            if (so == null) return;

            var item = holsterList ? M.holsters[index].Item : M.EquipPoints[index].Item;
            if (item == null || item.ItemType == null) return; //Need an Item Type to key the entry

            Undo.RecordObject(so, "Update Super Item Offsets");

            bool changed;

            if (holsterList)
            {
                var h = M.holsters[index];
                if (h.ID == null) return;

                int slot = SlotOf(h, item); //Slot the item is parented under, -1 when it is not in this holster

                changed = slot >= 0
                    ? so.SetHolsterOffset(item.ItemType, h.ID, new TransformOffset(item.transform), slot)
                    : so.EnsureHolsterOffset(item.ItemType, h.ID);
            }
            else
            {
                var ep = M.EquipPoints[index];
                if (ep.ID == null) return;

                changed = ep.Point.Value != null && item.transform.parent == ep.Point.Value
                    ? so.SetOffset(item.ItemType, ep.ID, new TransformOffset(item.transform))
                    : so.EnsureOffset(item.ItemType, ep.ID);
            }

            EditorUtility.SetDirty(so);

            if (changed) M.Debugging($"[Item Offsets] Added <color=white>[{item.ItemType.name}]</color> entry to [{so.name}]");
        }

        private static readonly GUIContent s_UpdateRuntimeOffset = new("Update Runtime Offset",
            "Saves the current local Position/Rotation/Scale of every equipped and holstered Item into its entry " +
            "(Item + Equip Point / Item + Holster) in the Item Offsets asset.\nOnly entries that changed are written");

        /// <summary> Saves the runtime local Transform of the Items the Manager holds (all of them, or only [only]) into the Item Offsets asset. Returns the entries written </summary>
        internal static int SaveRuntimeOffsets(SuperItemManager manager, SuperItem only = null)
        {
            var so = manager != null ? manager.ItemOffsets : null;
            if (so == null) return 0;

            Undo.RecordObject(so, "Update Runtime Offset");

            var saved = new List<string>();

            //Equipped: the Item must be parented to the Point, a mid-draw Item still sits in its Holster Slot
            foreach (var ep in manager.EquipPoints)
            {
                var item = ep?.Item;
                if (item == null || item.ItemType == null || ep.ID == null || (only != null && item != only)) continue;
                if (ep.Point.Value == null || item.transform.parent != ep.Point.Value) continue;

                var stored = so.GetOffset(item.ItemType, ep.ID);

                if (OffsetChanged(stored, item.transform))
                {
                    so.SetOffset(item.ItemType, ep.ID, new TransformOffset(item.transform));
                    saved.Add($"[{item.ItemType.name} → {ep.ID.name}]");
                }
            }

            //Holstered: every stored copy (Dual Wielding). Copies share one entry, the unchanged ones are skipped
            foreach (var h in manager.holsters)
            {
                if (h?.ID == null) continue;

                foreach (var item in HolsteredItems(h))
                {
                    if (item == null || item.ItemType == null || (only != null && item != only)) continue;
                    if (SlotOf(h, item) < 0) continue; //Not parented to one of this Holster's Slots

                    var stored = so.GetHolsterOffset(item.ItemType, h.ID);

                    if (OffsetChanged(stored, item.transform))
                    {
                        so.SetHolsterOffset(item.ItemType, h.ID, new TransformOffset(item.transform)); //Slot priority list is kept
                        saved.Add($"[{item.ItemType.name} → {h.ID.name}]");
                    }
                }
            }

            if (saved.Count > 0)
            {
                EditorUtility.SetDirty(so);
                AssetDatabase.SaveAssetIfDirty(so);
                Debug.Log($"[Item Offsets] Updated {saved.Count} runtime offset(s) in [{so.name}]: {string.Join(", ", saved)}", so);
            }
            else
            {
                Debug.Log($"[Item Offsets] No runtime offset changed on [{manager.name}]{(only != null ? $" for [{only.name}]" : "")}", so);
            }

            return saved.Count;
        }

        /// <summary> Head of the Holster plus every other stored copy, without repeats </summary>
        private static IEnumerable<SuperItem> HolsteredItems(SItemHolster holster)
        {
            if (holster.Item != null) yield return holster.Item;

            foreach (var stored in holster.StoredItems)
                if (stored != null && stored != holster.Item) yield return stored;
        }

        /// <summary> True when the Transform's local pose differs from the stored offset (rotations compared as quaternions, so 270° == -90°) </summary>
        private static bool OffsetChanged(TransformOffset stored, Transform t)
        {
            const float k_Epsilon = 0.0001f;

            return (stored.Position - t.localPosition).sqrMagnitude > k_Epsilon * k_Epsilon
                || (stored.Scale - t.localScale).sqrMagnitude > k_Epsilon * k_Epsilon
                || Quaternion.Angle(Quaternion.Euler(stored.Rotation), t.localRotation) > 0.01f;
        }

        //MWC — Index of the Holster Slot the item is parented under. -1 when it is not parented to any of them.
        private static int SlotOf(SItemHolster holster, SuperItem item)
        {
            if (holster.Slots == null || item == null) return -1;

            for (int i = 0; i < holster.Slots.Count; i++)
                if (holster.Slots[i].Value != null && item.transform.parent == holster.Slots[i].Value) return i;

            return -1;
        }

        //MWC — Makes sure every item already assigned to a row is listed in the asset. Runs when the inspector opens.
        //Entry values are never touched here: only missing pairs are added, so reselecting the character can't
        //overwrite offsets authored with the Scene handles.
        private void EnsureAllRowsListed()
        {
            var so = M.ItemOffsets;
            if (so == null) return;

            bool added = false;

            foreach (var ep in M.EquipPoints)
                if (ep?.Item != null && ep.ID != null && ep.Item.ItemType != null)
                    added |= so.EnsureOffset(ep.Item.ItemType, ep.ID);

            foreach (var h in M.holsters)
                if (h?.Item != null && h.ID != null && h.Item.ItemType != null)
                    added |= so.EnsureHolsterOffset(h.Item.ItemType, h.ID);

            if (added) EditorUtility.SetDirty(so);
        }

        private void DrawInternalInspector(SerializedProperty itemField, string desc)
        {
            if (itemField.objectReferenceValue != null)
            {
                itemField.isExpanded = MalbersEditor.Foldout_Bold(itemField.isExpanded, $"{desc}  [{itemField.objectReferenceValue.name}]", MTools.MBlue);

                if (itemField.isExpanded)
                {
                    var key = itemField.propertyPath;

                    if (State_Editor.TryGetValue(key, out Editor editor))
                    {
                        editor = State_Editor[key];
                    }
                    else
                    {
                        Editor.CreateCachedEditor(itemField.objectReferenceValue, null, ref editor);
                        State_Editor.Add(key, editor);
                    }

                    editor.serializedObject.Update();
                    editor.OnInspectorGUI();
                    editor.serializedObject.ApplyModifiedProperties();
                }
            }
        }

        private void ShowHolster()
        {
            //MWC — [Self Store Holster] moved to the [Equip Behaviour] group on the General tab. The debug icon stays
            //here so this tab can still toggle the Manager debug
            using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Holsters", EditorStyles.boldLabel);
                MalbersEditor.DrawDebugIcon(debug);
            }

            ShowItemOffsets(true); //MWC — same asset as the Equip Points tab; Holster entries are authored from this tab

            if (Application.isPlaying)
            {
                using (new EditorGUI.DisabledGroupScope(true))
                    EquipPointsReordable.DoLayoutList();
            }

            holsterReordable.DoLayoutList();

            var index = holsterReordable.index;

            if (index != -1 && index >= 0 && index < M.holsters.Count)
            {
                var element = holsters.GetArrayElementAtIndex(index);
                var itemField = element.FindPropertyRelative("Item");
                var HolsterID = M.holsters[index].ID;
                var item = M.holsters[index].Item;
                var Input = element.FindPropertyRelative("Input");
                var Slots = element.FindPropertyRelative("Slots");
                var AutoEquip = element.FindPropertyRelative("m_autoEquip");
                var Capacity = element.FindPropertyRelative("m_Capacity"); //MWC — max Items stored, regardless of the Slots
                var OnHolster = element.FindPropertyRelative("OnHolster");
                var OnUnholster = element.FindPropertyRelative("OnUnholster");
                var OnHolsterItemChanged = element.FindPropertyRelative("OnHolsterItemChanged");
                var OnHolsterEmpty = element.FindPropertyRelative("OnHolsterEmpty");

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {

                    EditorGUILayout.PropertyField(Input);
                    EditorGUILayout.PropertyField(AutoEquip);

                    //MWC: Capacity - it caps the amount of stored Items even when there are more Slots available
                    EditorGUILayout.PropertyField(Capacity, new GUIContent("Capacity", "Maximum amount of Items this Holster can store, regardless of how many Transform Slots it has.\nStoring a new Item on a full Holster drops the OLDEST stored one.\nZero (or less) = No Limit"));

                    var capacityValue = M.holsters[index].Capacity;

                    if (capacityValue <= 0)
                    {
                        EditorGUILayout.HelpBox("Capacity: No Limit. Only the Slots below limit the amount of Items", MessageType.None);
                    }
                    else if (Slots.arraySize > 0 && capacityValue > Slots.arraySize)
                    {
                        EditorGUILayout.HelpBox($"Capacity [{capacityValue}] is greater than the [{Slots.arraySize}] Slots available. " +
                            $"Only {Slots.arraySize} Item(s) can be stored", MessageType.Warning);
                    }

                    DrawSlotsList(index, Slots); //MWC — a real list so the selected Slot is known (only it gets the Scene handles)

                    EditorGUI.indentLevel++;
                    //MWC — Holster reactions. The dynamic target is stated in the label because it is the item,
                    //not the character, which is what the reactions inside resolve against.
                    EditorGUILayout.PropertyField(OnHolsterItemChanged, new GUIContent(k_OnHolsterItemLabel, k_OnHolsterItemTip));
                    EditorGUILayout.PropertyField(OnHolsterEmpty, new GUIContent(k_OnHolsterEmptyLabel, k_OnHolsterEmptyTip));
                    EditorGUILayout.PropertyField(OnHolster, new GUIContent(k_OnHolsterLabel, k_OnHolsterTip));
                    EditorGUILayout.PropertyField(OnUnholster, new GUIContent(k_OnUnholsterLabel, k_OnUnholsterTip));
                    EditorGUI.indentLevel--;
                }
                DrawInternalInspector(itemField, "Holstered Item");
            }
        }

        //MWC — Slots list of the selected Holster. It replaces the plain array field so the Slot the user picks is
        //tracked (SelectedHolsterSlot): the Scene handles and the bigger gizmo only apply to that one.
        private ReorderableList slotsReordable;
        private int slotsListHolster = -1; //Holster row the list was built for; it is rebuilt when another one is selected

        private void DrawSlotsList(int holsterIndex, SerializedProperty slots)
        {
            if (slotsReordable == null || slotsListHolster != holsterIndex)
            {
                slotsListHolster = holsterIndex;

                slotsReordable = new ReorderableList(serializedObject, slots, true, true, true, true)
                {
                    drawHeaderCallback = rect => EditorGUI.LabelField(rect, new GUIContent("Slots",
                        "Transforms the Items are parented to inside this Holster.\nThe selected Slot is the one the Scene handles edit")),

                    elementHeightCallback = i =>
                        EditorGUI.GetPropertyHeight(slotsReordable.serializedProperty.GetArrayElementAtIndex(i), true) + 4,

                    drawElementCallback = (rect, i, isActive, isFocused) =>
                    {
                        rect.y += 2;
                        var element = slotsReordable.serializedProperty.GetArrayElementAtIndex(i);
                        rect.height = EditorGUI.GetPropertyHeight(element, true);
                        EditorGUI.PropertyField(rect, element, new GUIContent($"Slot {i}"));
                    },

                    onSelectCallback = list => SelectedHolsterSlot.intValue = list.index,
                };

                //A Slot index from another Holster can be out of range here
                slotsReordable.index = Mathf.Clamp(SelectedHolsterSlot.intValue, slots.arraySize > 0 ? 0 : -1, slots.arraySize - 1);
                SelectedHolsterSlot.intValue = slotsReordable.index;
            }

            slotsReordable.DoLayoutList();
        }

        private readonly Dictionary<string, Editor> State_Editor = new();

        private void ShowInputs()
        {


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(ActionsInput);
                EditorGUI.indentLevel--;
            }

            ShowHolsterInputs(); //MWC — the Holsters' own Input, gathered here next to the Action Inputs
        }

        //MWC — Holster Inputs ─────────────────────────────────────────────────────────
        private static bool s_ShowHolsterInputs = true; //MWC — shared foldout state, open by default

        //MWC — Clearance before the Input field: the Var Reference drawer paints its popup ~20px LEFT of its own
        //rect, so butting it against the Holster ID column would draw it on top of that field. See the ID/Var
        //drawer geometry: ~16px is enough, more just leaves dead air.
        private const float HOLSTER_INPUT_GUTTER = 18f;

        /// <summary>[Holster ID → Input] rows. The same Input the Holsters tab draws, listed in one place.</summary>
        private void ShowHolsterInputs()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                s_ShowHolsterInputs = MalbersEditor.Foldout(s_ShowHolsterInputs, "Holster Inputs");

                if (!s_ShowHolsterInputs) return;

                if (holsters.arraySize == 0)
                {
                    EditorGUILayout.HelpBox("No Holsters yet. Add them on the Holsters tab", MessageType.None);
                    return;
                }

                EditorGUI.indentLevel++;

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent("Holster",
                        "Read only here: Holster IDs are edited on the Holsters tab"), EditorStyles.miniLabel);

                    GUILayout.Space(HOLSTER_INPUT_GUTTER);

                    EditorGUILayout.LabelField(new GUIContent("Input",
                        "Input that equips the Item stored in this Holster"), EditorStyles.miniLabel);
                }

                for (int i = 0; i < holsters.arraySize; i++)
                {
                    var holster = holsters.GetArrayElementAtIndex(i);

                    using (new GUILayout.HorizontalScope())
                    {
                        //The ID belongs to the Holsters tab: shown for reference only
                        using (new EditorGUI.DisabledGroupScope(true))
                            EditorGUILayout.PropertyField(holster.FindPropertyRelative("ID"), GUIContent.none);

                        GUILayout.Space(HOLSTER_INPUT_GUTTER);

                        EditorGUILayout.PropertyField(holster.FindPropertyRelative("Input"), GUIContent.none);
                    }
                }

                EditorGUI.indentLevel--;
            }
        }

        // ── Events tab ──────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Events tab: the Manager-wide Item events and the Action Pressed/Released events</summary>
        private void ShowEvents()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(OnItemEquipped);
                EditorGUILayout.PropertyField(OnItemUnequipped);
                EditorGUILayout.PropertyField(OnItemHolster);
                EditorGUILayout.PropertyField(OnItemUnholster);
                EditorGUILayout.PropertyField(OnItemDropped);
                EditorGUILayout.PropertyField(OnItemPicked);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(OnActionPressed, new GUIContent("On Action Pressed (Action ID)"));
                EditorGUILayout.PropertyField(OnActionReleased, new GUIContent("On Action Released (Action ID)"));
            }
        }

        // ── Reactions tab ───────────────────────────────────────────────────────────────────────────────────────

        //Foldout state of the Reactions groups: static so it survives re-selection without touching the component
        private static bool s_ItemReactions = true, s_ActionReactions = true;

        /// <summary>Reactions tab: the Manager-wide Item reactions, and a Pressed/Released pair for every Action Input row</summary>
        private void ShowReactions()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                s_ItemReactions = MalbersEditor.Foldout(s_ItemReactions, "Item Reactions");

                if (s_ItemReactions)
                {
                    //Plain strings, never cached GUIContents: the Reaction2 drawer appends the count to label.text
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(ItemEquippedReaction, new GUIContent("Item Equipped - Dynamic Target [Equipped Item]", "Reactions played when an Item is equipped on any Equip Point"));
                    EditorGUILayout.PropertyField(ItemUnequippedReaction, new GUIContent("Item Unequipped - Dynamic Target [Unequipped Item]", "Reactions played when an Item is unequipped from any Equip Point"));
                    EditorGUILayout.PropertyField(ItemHolsterReaction, new GUIContent("Item Holster - Dynamic Target [Holstered Item]", "Reactions played when an Item is stored in any Holster"));
                    EditorGUILayout.PropertyField(ItemUnholsterReaction, new GUIContent("Item Unholster - Dynamic Target [Unholstered Item]", "Reactions played when an Item is removed from any Holster (drawn out, evicted or dropped)"));
                    EditorGUILayout.PropertyField(ItemDroppedReaction, new GUIContent("Item Dropped - Dynamic Target [Dropped Item]", "Reactions played when an Item is dropped to the world"));
                    EditorGUILayout.PropertyField(ItemPickedReaction, new GUIContent("Item Picked - Dynamic Target [Picked Item]", "Reactions played when an Item picked from the world lands in the hands or in a Holster"));
                    EditorGUI.indentLevel--;
                }
            }

            SyncActionReactions();

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                //One Pressed/Released pair per Action of the Inputs tab. Dynamic Target of every reaction: the Super Item Manager
                s_ActionReactions = MalbersEditor.Foldout(s_ActionReactions, "Action Reactions - Dynamic Target [Super Item Manager]");

                if (!s_ActionReactions) return;

                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(AnyActionPressedReaction, new GUIContent("Any Action Pressed", "Reactions played when the input of ANY Action is pressed.\nDynamic Target [Super Item Manager]"));
                EditorGUILayout.PropertyField(AnyActionReleasedReaction, new GUIContent("Any Action Released", "Reactions played when the input of ANY Action is released.\nDynamic Target [Super Item Manager]"));
                EditorGUI.indentLevel--;

                if (ActionReactions.arraySize == 0)
                {
                    EditorGUILayout.LabelField(new GUIContent("No Action Inputs. Add them on the Inputs tab"), EditorStyles.miniLabel);
                    return;
                }

                for (int i = 0; i < ActionReactions.arraySize; i++)
                {
                    var entry = ActionReactions.GetArrayElementAtIndex(i);
                    var id = entry.FindPropertyRelative("ID").objectReferenceValue as ItemActionID;
                    var used = id != null && UsesActionID(id);
                    var actionName = id != null ? id.name : "<None>";

                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            entry.isExpanded = MalbersEditor.Foldout(entry.isExpanded, used ? actionName : $"{actionName} (not in Action Inputs)");

                            //Only an Action no longer on the Inputs tab can be removed: the used ones are re-created by the sync
                            if (!used && GUILayout.Button(new GUIContent("✕", "Remove the reactions of this Action"), EditorStyles.miniButton, GUILayout.Width(22)))
                            {
                                ActionReactions.DeleteArrayElementAtIndex(i);
                                break;
                            }
                        }

                        if (entry.isExpanded)
                        {
                            EditorGUI.indentLevel++;
                            EditorGUILayout.PropertyField(entry.FindPropertyRelative("OnPressed"), new GUIContent($"{actionName} Pressed", $"Reactions played when the [{actionName}] input is pressed.\nDynamic Target [Super Item Manager]"));
                            EditorGUILayout.PropertyField(entry.FindPropertyRelative("OnReleased"), new GUIContent($"{actionName} Released", $"Reactions played when the [{actionName}] input is released.\nDynamic Target [Super Item Manager]"));
                            EditorGUI.indentLevel--;
                        }
                    }
                }
            }
        }

        /// <summary>True when an Action Input row uses this Action ID</summary>
        private bool UsesActionID(ItemActionID id) => M.ActionsInput != null && M.ActionsInput.Exists(row => row != null && row.ID == id);

        /// <summary>Adds an Action Reactions entry for every Action Input row that has none yet. Edit mode only</summary>
        private void SyncActionReactions()
        {
            if (Application.isPlaying || M.ActionsInput == null) return;

            M.ActionReactions ??= new();

            List<ItemActionID> missing = null;

            foreach (var row in M.ActionsInput)
            {
                if (row == null || row.ID == null) continue;
                if (M.ActionReactions.Exists(r => r != null && r.ID == row.ID)) continue;
                if (missing != null && missing.Contains(row.ID)) continue;

                (missing ??= new()).Add(row.ID);
            }

            if (missing == null) return;

            //Added on the component itself (not arraySize++, which copies the previous entry and its reactions)
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(M, "Sync Action Reactions");

            foreach (var id in missing) M.ActionReactions.Add(new SIActionInputReaction() { ID = id });

            PrefabUtility.RecordPrefabInstancePropertyModifications(M);
            EditorUtility.SetDirty(M);
            serializedObject.Update();
        }

        // ── Debug tab — live inspection of the Manager, its Equip Points, Holsters and Items (Play Mode) ──────────

        //Foldout state of the Debug sections: static so it survives re-selection without touching the component
        private static bool s_DebugManager = true, s_DebugEquipPoints = true, s_DebugHolsters = true, s_DebugTracked = true, s_DebugTestActions = true;

        /// <summary>Debug tab: the console log toggle, and on Play Mode the runtime state of the Manager, Equip Points, Holsters and Items</summary>
        private void ShowDebug()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(debug, new GUIContent("Debug Log", "Print the Manager Equip, Holster, Drop and Auto Store decisions on the Console"));
            }

            DrawDebugTestActions();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Runtime data is displayed on Play Mode", MessageType.Info);
                return;
            }

            DrawDebugManager();
            DrawDebugEquipPoints();
            DrawDebugHolsters();
            DrawDebugTrackedItems();

            Repaint(); //Keep the runtime values live while the Inspector is visible
        }

        // ── Test Actions — [Play] taps an Action, [Hold]/[Release] latches its input, straight from the Inspector ──

        private const float TEST_BTN_W = 64f;     //Each of the two buttons

        private static readonly HashSet<ItemActionID> s_HeldTestActions = new(); //Actions whose input is latched by [Hold] until [Release]

        private static readonly GUIContent s_TestPlay = new("Play", "Tap: Action_Press now, Action_Release on the next frame (a one-frame input press)");
        private static readonly GUIContent s_TestHold = new("Hold", "Action_Press and keep the input pressed until [Release]");
        private static readonly GUIContent s_TestRelease = new("Release", "Action_Release the input latched by [Hold]");
        private static readonly GUIContent s_TestLock = new("Lock", "Lock the Manager: every equipped Item is put away (Lock Mode) and every Item operation is refused");
        private static readonly GUIContent s_TestUnlock = new("Unlock", "Unlock the Manager: the Items it put away are drawn back");

        /// <summary>One row per Actions Input entry: [Play] taps the Action, [Hold]/[Release] toggles its input. Disabled when no equipped Item uses that Action</summary>
        private void DrawDebugTestActions()
        {
            if (!Application.isPlaying && s_HeldTestActions.Count > 0) s_HeldTestActions.Clear(); //Nothing survives Play Mode

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                s_DebugTestActions = MalbersEditor.Foldout(s_DebugTestActions, "Test Actions");
                if (!s_DebugTestActions) return;

                if (M.ActionsInput == null || M.ActionsInput.Count == 0)
                {
                    EditorGUILayout.HelpBox("No Action Inputs. Add them on the Inputs tab", MessageType.None);
                    return;
                }

                EditorGUI.indentLevel++;

                for (int i = 0; i < M.ActionsInput.Count; i++)
                {
                    var row = M.ActionsInput[i];
                    if (row == null || row.ID == null) continue;
                    DrawTestActionRow(row.ID);
                }

                EditorGUI.indentLevel--;
            }
        }

        /// <summary>Action name + [Play] + [Hold]/[Release]. Play and Hold are ignored (disabled) when no equipped Item answers the Action ID; Release stays enabled while latched</summary>
        private void DrawTestActionRow(ItemActionID actionID)
        {
            SuperItem item = null;
            ItemAction action = null;
            var used = Application.isPlaying && M.TryGet_Action(actionID, out item, out action);
            var playing = used && action.IsPlaying;
            var held = s_HeldTestActions.Contains(actionID);

            using (new GUILayout.HorizontalScope())
            {
                //The ID asset itself, read only: the Actions Input table owns it
                using (new EditorGUI.DisabledGroupScope(true))
                    EditorGUILayout.ObjectField(GUIContent.none, actionID, typeof(ItemActionID), false);

                using (new EditorGUI.DisabledGroupScope(!used || held))
                {
                    if (GUILayout.Button(s_TestPlay, EditorStyles.miniButtonLeft, GUILayout.Width(TEST_BTN_W)))
                    {
                        M.Action_Press(actionID);
                        ReleaseNextFrame(M, actionID);
                    }
                }

                using (new EditorGUI.DisabledGroupScope(!used && !held))
                {
                    var old = GUI.backgroundColor;
                    if (held) GUI.backgroundColor = Color.green;

                    if (GUILayout.Button(held ? s_TestRelease : s_TestHold, EditorStyles.miniButtonRight, GUILayout.Width(TEST_BTN_W)))
                    {
                        if (held)
                        {
                            s_HeldTestActions.Remove(actionID);
                            M.Action_Release(actionID);
                        }
                        else
                        {
                            s_HeldTestActions.Add(actionID);
                            M.Action_Press(actionID);
                        }
                    }

                    GUI.backgroundColor = old;
                }
            }
        }

        /// <summary>Sends the Release edge of a [Play] tap once the game has advanced a frame, so the press lasts one frame like a real tap</summary>
        private static void ReleaseNextFrame(SuperItemManager manager, ItemActionID actionID)
        {
            var pressFrame = Time.frameCount;

            void Release()
            {
                if (Application.isPlaying && Time.frameCount <= pressFrame) return; //Wait for the next game frame

                EditorApplication.update -= Release;
                if (manager != null && Application.isPlaying) manager.Action_Release(actionID);
            }

            EditorApplication.update += Release;
        }

        /// <summary>Manager-wide runtime flags: counts, hands in use, No Items blocks, Fast Weapons and the Default Item</summary>
        private void DrawDebugManager()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                s_DebugManager = MalbersEditor.Foldout(s_DebugManager, "Manager");
                if (!s_DebugManager) return;

                using (new EditorGUI.DisabledGroupScope(true))
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        EditorGUIUtility.labelWidth = 120;

                        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.IntField("Equipped", M.EquippedCount);
                            EditorGUILayout.IntField("Holstered", M.HolsteredCount);
                            EditorGUILayout.EnumFlagsField("Hands In Use", M.HandsInUse);
                            EditorGUILayout.Toggle("Equip In Flight", M.IsEquipping);
                            EditorGUILayout.Toggle("Action Playing", M.IsAnyActionPlaying());
                            EditorGUILayout.Toggle("Dual Wielding", M.IsDualWielding());
                        }

                        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.Toggle("No Items Blocked", M.NoItemsBlocked);
                            EditorGUILayout.Toggle("   By State", M.NoItemState);
                            EditorGUILayout.Toggle("   By Stance", M.NoItemStance);
                            EditorGUILayout.IntField("   By Modes", M.NoItemsEquippedMode);
                            EditorGUILayout.Toggle("Release Pending", M.ReleasePending);
                            EditorGUILayout.Toggle("Locked", M.Locked);
                            EditorGUILayout.Toggle("Fast Weapons", M.UseFastWeapons);
                            EditorGUILayout.Toggle("Action Conditions", M.CheckActionConditions());
                        }

                        EditorGUIUtility.labelWidth = 0;
                    }
                }

                //[Lock] live test: the Items go away / come back on the spot, exactly as a Lock Reaction would do it
                using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUIUtility.labelWidth = 120;
                    EditorGUILayout.PropertyField(lockMode, new GUIContent("Lock Mode", "Holster or Hide the equipped Items when the Manager is Locked"));
                    EditorGUIUtility.labelWidth = 0;

                    if (GUILayout.Button(M.Locked ? s_TestUnlock : s_TestLock, GUILayout.Width(TEST_BTN_W))) M.Locked = !M.Locked;
                }

                using (new EditorGUI.DisabledGroupScope(true))
                {

                    using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUIUtility.labelWidth = 120;
                        EditorGUILayout.ObjectField("Default Item", M.DefaultItem, typeof(SuperItem), true);

                        if (M.DefaultItem != null)
                        {
                            EditorGUILayout.Toggle("Default Active", M.DefaultItemActive);
                            EditorGUILayout.LabelField("Playing", PlayingActions(M.DefaultItem));
                        }

                        EditorGUILayout.LabelField("Auto Store", M.AutoStore.Value > 0 ? $"{M.AutoStore.Value:F1}s idle" : "Disabled");
                        EditorGUIUtility.labelWidth = 0;
                    }
                }
            }
        }

        /// <summary>One box per Equip Point: occupant, pending reservation, Auto Store idle timer and the Item runtime state</summary>
        private void DrawDebugEquipPoints()
        {
            //The Drop requests are collected here and served AFTER the loop: Item_Drop unequips and unholsters the Item
            //on the spot, which mutates the very collections these boxes are walking
            s_DropRequests.Clear();

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    s_DebugEquipPoints = MalbersEditor.Foldout(s_DebugEquipPoints, $"Equip Points  [{M.EquippedCount}/{M.EquipPoints.Count}]");

                    using (new EditorGUI.DisabledGroupScope(M.EquippedCount == 0))
                        if (GUILayout.Button(s_DropAll, EditorStyles.miniButton, GUILayout.Width(DROP_ALL_BTN_W)))
                            foreach (var point in M.EquipPoints)
                                if (point != null && point.Item != null) s_DropRequests.Add(point.Item);
                }

                if (s_DebugEquipPoints)
                {
                    foreach (var point in M.EquipPoints)
                    {
                        if (point == null) continue;

                        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            var id = point.ID != null ? point.ID.name : "[None]";
                            var status = point.Item != null ? "Occupied" : point.Reserved != null ? "Reserved" : "Free";

                            //Header row: the [Drop] button lives OUTSIDE the read-only group below, so it stays clickable
                            using (new GUILayout.HorizontalScope())
                            {
                                EditorGUILayout.LabelField($"{id}  [{point.Side}]  <{status}>", EditorStyles.boldLabel);

                                using (new EditorGUI.DisabledGroupScope(point.Item == null))
                                    if (GUILayout.Button(s_DropItem, EditorStyles.miniButton, GUILayout.Width(DROP_BTN_W)))
                                        s_DropRequests.Add(point.Item);
                            }

                            using (new EditorGUI.DisabledGroupScope(true))
                            {
                                EditorGUIUtility.labelWidth = 120;
                                EditorGUILayout.ObjectField("Item", point.Item, typeof(SuperItem), true);

                                if (point.Reserved != null)
                                    EditorGUILayout.ObjectField("Reserved (drawing)", point.Reserved, typeof(SuperItem), true);

                                if (point.Item != null)
                                {
                                    var autoStore = M.AutoStore.Value;
                                    var idle = Time.time - point.LastActiveTime;
                                    EditorGUILayout.LabelField("Auto Store Idle", autoStore > 0 ? $"{idle:F1}s / {autoStore:F1}s" : "Disabled");

                                    DrawDebugItem(point.Item);
                                }

                                EditorGUIUtility.labelWidth = 0;
                            }
                        }
                    }
                }
            }

            ServeDropRequests();
        }

        /// <summary>One box per Holster: head item, count vs capacity, pending exit, input, and every stored Item with its Slot</summary>
        private void DrawDebugHolsters()
        {
            s_DropRequests.Clear(); //Same deferral as the Equip Points: dropping mutates the Holster StoredItems list being walked

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    s_DebugHolsters = MalbersEditor.Foldout(s_DebugHolsters, $"Holsters  [{M.HolsteredCount}/{M.holsters.Count}]");

                    using (new EditorGUI.DisabledGroupScope(M.HolsteredCount == 0))
                        if (GUILayout.Button(s_DropAllHolsters, EditorStyles.miniButton, GUILayout.Width(DROP_ALL_BTN_W)))
                            foreach (var holster in M.holsters)
                            {
                                if (holster == null) continue;

                                foreach (var stored in holster.StoredItems) //Every Item, not just the head one
                                    if (stored != null) s_DropRequests.Add(stored);
                            }
                }

                if (s_DebugHolsters)
                {
                    foreach (var holster in M.holsters)
                    {
                        if (holster == null) continue;

                        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            var id = holster.ID != null ? holster.ID.name : "[None]";
                            var capacity = holster.Capacity > 0 ? holster.Capacity.ToString() : "No Limit";
                            var full = holster.IsFull() ? "  <Full>" : string.Empty;

                            //Header row: the [Drop] button lives OUTSIDE the read-only group below, so it stays clickable
                            using (new GUILayout.HorizontalScope())
                            {
                                EditorGUILayout.LabelField($"{id}  [{holster.Count}/{capacity}]{full}", EditorStyles.boldLabel);

                                using (new EditorGUI.DisabledGroupScope(holster.Item == null))
                                    if (GUILayout.Button(s_DropHead, EditorStyles.miniButton, GUILayout.Width(DROP_BTN_W)))
                                        s_DropRequests.Add(holster.Item);
                            }

                            using (new EditorGUI.DisabledGroupScope(true))
                            {
                                EditorGUIUtility.labelWidth = 120;
                                EditorGUILayout.ObjectField("Head Item", holster.Item, typeof(SuperItem), true);

                                if (holster.Leaving != null)
                                    EditorGUILayout.ObjectField("Leaving (drawing)", holster.Leaving, typeof(SuperItem), true);

                                var input = holster.Input?.Value;
                                EditorGUILayout.LabelField("Input", string.IsNullOrEmpty(input) ? "[None]" : input);
                                EditorGUILayout.Toggle("Auto Equip", holster.AutoEquip);

                                if (holster.StoredItems.Count > 0)
                                {
                                    EditorGUILayout.LabelField("Stored Items", EditorStyles.miniBoldLabel);
                                    EditorGUI.indentLevel++;

                                    foreach (var stored in holster.StoredItems)
                                    {
                                        if (stored == null) continue;
                                        EditorGUILayout.ObjectField($"Slot {holster.GetUsedSlot(stored)}", stored, typeof(SuperItem), true);
                                    }

                                    EditorGUI.indentLevel--;
                                }

                                EditorGUIUtility.labelWidth = 0;
                            }
                        }
                    }
                }
            }

            ServeDropRequests();
        }

        // ── Drop buttons — [Drop] per Equip Point / Holster row, [Drop All] on each section header ──────────────

        private const float DROP_BTN_W = 44f;       //The per row [Drop] button
        private const float DROP_ALL_BTN_W = 62f;   //The [Drop All] button on the section headers

        //Items a [Drop] button asked for this repaint, served once the drawing loop is over (see ServeDropRequests)
        private static readonly List<SuperItem> s_DropRequests = new();

        private static readonly GUIContent s_DropItem = new("Drop", "Drop the Item equipped on this Equip Point: it leaves the hand and falls to the ground (Drop Point)");
        private static readonly GUIContent s_DropHead = new("Drop", "Drop the Item this Holster shows (the head one). The next stored Item is promoted in its place");
        private static readonly GUIContent s_DropAll = new("Drop All", "Drop the Items of EVERY Equip Point");
        private static readonly GUIContent s_DropAllHolsters = new("Drop All", "Drop EVERY Item stored in EVERY Holster, not just the head ones");

        /// <summary>Serves the [Drop] clicks collected while drawing, once it is safe to mutate the Manager collections</summary>
        private void ServeDropRequests()
        {
            if (s_DropRequests.Count == 0) return;

            foreach (var item in s_DropRequests)
                if (item != null) M.Item_Drop(item);

            s_DropRequests.Clear();
        }

        /// <summary>Items the Manager tracks outside the lists: held by a No Items block, hidden in place, or focused by the Interactor</summary>
        private void DrawDebugTrackedItems()
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                s_DebugTracked = MalbersEditor.Foldout(s_DebugTracked, "Held | Hidden | Focused Items");
                if (!s_DebugTracked) return;

                using (new EditorGUI.DisabledGroupScope(true))
                {
                    DrawDebugItemList("Held by a No Items block (drawn back on release)", M.HoldItems);
                    DrawDebugItemList("Hidden in place (held, no Holster)", M.HiddenItems);
                    DrawDebugItemList("Focused (free, inside the Interactor)", M.FocusedItems);
                }
            }
        }

        /// <summary>Runtime state of one Item: flags, side, Active Set and the Actions playing right now</summary>
        private void DrawDebugItem(SuperItem item)
        {
            using (new GUILayout.HorizontalScope())
            {
                EditorGUIUtility.labelWidth = 90;

                using (new GUILayout.VerticalScope())
                {
                    EditorGUILayout.Toggle("Equipped", item.IsEquipped);
                    EditorGUILayout.Toggle("Equipping", item.IsEquipping);
                    EditorGUILayout.Toggle("In Holster", item.IsInHolster);
                }

                using (new GUILayout.VerticalScope())
                {
                    EditorGUILayout.EnumPopup("Side", item.EffectiveSide);
                    EditorGUILayout.Toggle("Primary Point", item.IsOnPrimaryPoint);
                    EditorGUILayout.Toggle("Hidden", M.HiddenItems.Contains(item));
                }
            }

            EditorGUIUtility.labelWidth = 120;
            EditorGUILayout.LabelField("Active Set", item.ActiveActionSet != null ? item.ActiveActionSet.Name : "[None]");
            EditorGUILayout.LabelField("Playing", PlayingActions(item));
            EditorGUILayout.LabelField("Main / Slave", $"{ActionName(item.MainAction)} / {ActionName(item.SlaveAction)}");
        }

        /// <summary>Titled box listing the Items of a runtime collection, or [Empty]</summary>
        private static void DrawDebugItemList(string title, IEnumerable<SuperItem> items)
        {
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
                var any = false;

                foreach (var item in items)
                {
                    if (item == null) continue;
                    any = true;
                    EditorGUILayout.ObjectField(GUIContent.none, item, typeof(SuperItem), true);
                }

                if (!any) EditorGUILayout.LabelField("[Empty]", EditorStyles.miniLabel);
            }
        }

        /// <summary>Name of an Action, or a dash when there is none</summary>
        private static string ActionName(ItemAction action) => action != null ? action.Name : "-";

        /// <summary>Comma-separated names of the Actions of the Item Active Set that are playing right now, or [None]</summary>
        private static string PlayingActions(SuperItem item)
        {
            if (item == null || item.ActiveActionSet == null || item.ActiveActionSet.actions == null) return "[None]";

            string result = null;

            foreach (var action in item.ActiveActionSet.actions)
            {
                if (action == null || !action.IsPlaying) continue;
                result = result == null ? action.Name : $"{result}, {action.Name}";
            }

            return result ?? "[None]";
        }


        // ── MWC — Move Item ↓↑ buttons (Equip Points & Holsters lists) ────────────────

        private const float MOVE_BTN_W = 18f;                   //MWC — width of each ↓/↑ button
        private const float MOVE_W = MOVE_BTN_W * 2f;           //MWC — right gutter both rows reserve for them
        private const float ITEM_TAG_W = 20f;                   //MWC — the [S]/[P] label sitting just before the buttons
        private const float SLOT_W = 32f;                       //MWC — the Holster rows' Slot int field
        private const float PAD = 2f;
        private const float CROSS_W = 30f;                      //MWC — the ⇄ button that sends the Item to the other list
        private const float CLEAR_W = 22f;                      //the ✕ button that clears the row's Item, drawn left of ⇄
        private const float CLEAR_ALL_W = 62f;                  //the [Clear All] header button

        private static readonly GUIContent s_MoveDown = new("▼", "Move this Item to the NEXT row (swaps with the Item already there).\nThe GameObject is re-parented and takes the destination offset from the Item Offsets asset");
        private static readonly GUIContent s_MoveUp = new("▲", "Move this Item to the PREVIOUS row (swaps with the Item already there).\nThe GameObject is re-parented and takes the destination offset from the Item Offsets asset");

        // ── Clear Item ✕ button and [Clear All] header button (Equip Points & Holsters lists) ──

        /// <summary> Draws the ✕ button that clears the Item reference of a row. Drawn right before the ⇄ button, only when the row has an Item </summary>
        private void DrawClearItemButton(Rect rect, bool holsterList, int index)
        {
            var item = GetRowItem(holsterList, index);

            if (item == null) return; //Same rule as the [S]/[P] tag and ⇄: no Item, no button (and no reserved space)

            //Left of the ⇄ button, which sits left of the [S]/[P] label
            float tagX = holsterList
                ? rect.xMax - MOVE_W - SLOT_W - PAD - ITEM_TAG_W
                : rect.xMax - MOVE_W - ITEM_TAG_W;

            var buttonRect = new Rect(tagX - CROSS_W - CLEAR_W, rect.y, CLEAR_W, EditorGUIUtility.singleLineHeight);

            var where = holsterList ? "Holster" : "Equip Point";
            var tooltip = $"Clear [{item.name}] from this {where}.\nOnly the reference is removed: the Item GameObject stays where it is";

            using (new EditorGUI.DisabledGroupScope(Application.isPlaying)) //A runtime removal must go through Item_Drop / Item_Store, not a reference wipe
            {
                if (GUI.Button(buttonRect, new GUIContent("✕", tooltip), EditorStyles.miniButton))
                    ClearRowItem(holsterList, index);
            }
        }

        /// <summary> Draws the [Clear All] button of a list header. Disabled in Play Mode and when no row holds an Item </summary>
        private void DrawClearAllButton(Rect rect, bool holsterList)
        {
            int count = holsterList ? M.holsters.Count : M.EquipPoints.Count;
            bool any = false;

            for (int i = 0; i < count && !any; i++) any = GetRowItem(holsterList, i) != null;

            var where = holsterList ? "Holster" : "Equip Point";
            var tooltip = $"Clear the Item of every {where} row.\nOnly the references are removed: the Item GameObjects stay where they are";

            using (new EditorGUI.DisabledGroupScope(Application.isPlaying || !any))
            {
                if (GUI.Button(rect, new GUIContent("Clear All", tooltip), EditorStyles.miniButton))
                    ClearAllRowItems(holsterList);
            }
        }

        /// <summary> Sets the Item reference of one row to null. Undo-able through the SerializedObject </summary>
        private void ClearRowItem(bool holsterList, int index)
        {
            var list = holsterList ? holsters : EquipPoints;
            if (index < 0 || index >= list.arraySize) return;

            list.GetArrayElementAtIndex(index).FindPropertyRelative("Item").objectReferenceValue = null;
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary> Sets the Item reference of every row of the list to null. Undo-able through the SerializedObject </summary>
        private void ClearAllRowItems(bool holsterList)
        {
            var list = holsterList ? holsters : EquipPoints;

            for (int i = 0; i < list.arraySize; i++)
                list.GetArrayElementAtIndex(i).FindPropertyRelative("Item").objectReferenceValue = null;

            serializedObject.ApplyModifiedProperties();
        }

        //MWC — Holster Slot field drawn straight in the Holster row (no label, the list header names the column).
        //The value lives in the Item Offsets asset (Item + Holster entry), so editing it upserts that entry.
        //Disabled when the row has no Item, no Item Type or no offsets asset — there would be nowhere to store it.
        private void DrawHolsterSlotField(Rect rect, int index)
        {
            var h = M.holsters[index];
            var item = h.Item;
            var so = M.ItemOffsets;

            var slotRect = new Rect(rect.xMax - MOVE_W - SLOT_W, rect.y, SLOT_W, EditorGUIUtility.singleLineHeight);

            bool valid = so != null && item != null && item.ItemType != null && h.ID != null;

            using (new EditorGUI.DisabledGroupScope(!valid))
            {
                if (!valid) { EditorGUI.IntField(slotRect, 0); return; }

                int slot = so.GetHolsterSlot(item.ItemType, h.ID);
                int max = h.Slots != null ? Mathf.Max(0, h.Slots.Count - 1) : 0;

                using (var cc = new EditorGUI.ChangeCheckScope())
                {
                    int newSlot = Mathf.Clamp(EditorGUI.IntField(slotRect, slot), 0, max); //Clamped: an out-of-range slot fails at runtime

                    if (cc.changed)
                    {
                        Undo.RecordObject(so, "Change Holster Slot");
                        so.SetHolsterSlot(item.ItemType, h.ID, newSlot);
                        EditorUtility.SetDirty(so);
                    }
                }
            }
        }

        // ── MWC — Move Item ⇄ button (Equip Point ⇄ Holster) ─────────────────────────

        /// <summary> Sends the row's Item to its matching row on the OTHER list: an Equip Point row moves it to
        /// its Holster, a Holster row moves it to its Equip Point. Drawn right before the [S]/[P] label. </summary>
        private void DrawMoveToOtherListButton(Rect rect, bool holsterList, int index)
        {
            var item = GetRowItem(holsterList, index);

            if (item == null) return; //Same rule as the [S]/[P] tag: no Item, no button (and no reserved space)

            //Left of the [S]/[P] label: the Holster rows also carry the Slot field before the ↓↑ buttons
            float tagX = holsterList
                ? rect.xMax - MOVE_W - SLOT_W - PAD - ITEM_TAG_W
                : rect.xMax - MOVE_W - ITEM_TAG_W;

            var buttonRect = new Rect(tagX - CROSS_W, rect.y, CROSS_W, EditorGUIUtility.singleLineHeight);

            int target = FindCrossRow(holsterList, item);
            var destination = holsterList ? "Equip Point" : "Holster";

            var tooltip = target >= 0
                ? $"Move [{item.name}] to its {destination} [{RowName(!holsterList, target)}] and jump to that tab.\n" +
                  "The Item already there is swapped into this row"
                : $"[{item.name}] has no {destination} row in this character";

            using (new EditorGUI.DisabledGroupScope(Application.isPlaying || target < 0))
            {
                if (GUI.Button(buttonRect, new GUIContent("⇄", tooltip), EditorStyles.miniButton))
                    MoveItemToOtherList(holsterList, index, target);
            }
        }

        private string RowName(bool holsterList, int index)
        {
            var id = holsterList ? (Object)M.holsters[index].ID : M.EquipPoints[index].ID;
            return id != null ? id.name : "None";
        }

        //MWC — Row of the OTHER list this Item belongs to: its primary ID first, then the Secondary (Dual Wielding) one.
        //-1 when the character has no such row.
        private int FindCrossRow(bool holsterList, SuperItem item)
        {
            if (holsterList) //Holster row → Equip Point row
            {
                for (int i = 0; i < M.EquipPoints.Count; i++)
                    if (M.EquipPoints[i]?.ID != null && M.EquipPoints[i].ID == item.EquipPoint) return i;

                for (int i = 0; i < M.EquipPoints.Count; i++)
                    if (M.EquipPoints[i]?.ID != null && M.CanUseEquipPoint(item, M.EquipPoints[i].ID)) return i;
            }
            else //Equip Point row → Holster row
            {
                for (int i = 0; i < M.holsters.Count; i++)
                    if (M.holsters[i]?.ID != null && M.holsters[i].ID == item.HolsterID) return i;

                for (int i = 0; i < M.holsters.Count; i++)
                    if (M.holsters[i]?.ID != null && item.IsAllowedHolster(M.holsters[i].ID)) return i;
            }
            return -1;
        }

        //MWC — Same contract as MoveRowItem, across the two lists: the Items of both rows are swapped and each
        //GameObject is re-parented to its new Equip Point / Holster Slot with that row's offset from the Item Offsets asset.
        private void MoveItemToOtherList(bool fromHolsterList, int index, int target)
        {
            var sourceList = fromHolsterList ? holsters : EquipPoints;
            var targetList = fromHolsterList ? EquipPoints : holsters;

            if (index < 0 || index >= sourceList.arraySize) return;
            if (target < 0 || target >= targetList.arraySize) return;

            var a = sourceList.GetArrayElementAtIndex(index).FindPropertyRelative("Item");
            var b = targetList.GetArrayElementAtIndex(target).FindPropertyRelative("Item");

            var itemA = a.objectReferenceValue as SuperItem;
            var itemB = b.objectReferenceValue as SuperItem;

            a.objectReferenceValue = itemB;
            b.objectReferenceValue = itemA;

            serializedObject.ApplyModifiedProperties(); //Apply before touching the Transforms, the rows are the source of truth

            ParentItemToRow(itemA, !fromHolsterList, target);
            ParentItemToRow(itemB, fromHolsterList, index);

            //MWC — both items landed on a new pair: make sure each one is listed in the offsets asset
            SyncRow(!fromHolsterList, target);
            SyncRow(fromHolsterList, index);

            serializedObject.Update();

            //MWC — follow the Item: jump to the tab that now holds it and select the row it landed on.
            //Applied on this event (a click pass), so the next Layout/Repaint already draws the new tab.
            Editor_Tabs1.intValue = fromHolsterList ? EQUIP_TAB : HOLSTER_TAB;
            Editor_Tabs2.intValue = NO_TAB; //The second toolbar draws with nothing selected

            if (fromHolsterList)
            {
                SelectedEquipPointSlot.intValue = target;
                EquipPointsReordable.index = target;
            }
            else
            {
                SelectedHolster.intValue = target;
                holsterReordable.index = target;
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary> Draws the ↓↑ buttons at the end of a list row, right after the [S]/[P] label </summary>
        private void DrawMoveItemButtons(Rect rect, bool holsterList, int index, int count)
        {
            float line = EditorGUIUtility.singleLineHeight;

            var downRect = new Rect(rect.xMax - MOVE_W, rect.y, MOVE_BTN_W, line);
            var upRect = new Rect(rect.xMax - MOVE_BTN_W, rect.y, MOVE_BTN_W, line);

            using (new EditorGUI.DisabledGroupScope(!CanMove(holsterList, index, +1, count)))
                if (GUI.Button(downRect, s_MoveDown, EditorStyles.miniButtonLeft)) MoveRowItem(holsterList, index, +1);

            using (new EditorGUI.DisabledGroupScope(!CanMove(holsterList, index, -1, count)))
                if (GUI.Button(upRect, s_MoveUp, EditorStyles.miniButtonRight)) MoveRowItem(holsterList, index, -1);
        }

        //MWC — nothing to do when the target row doesn't exist or when both rows are empty.
        //Disabled in Play Mode: a runtime move must go through Item_Equip/Item_Holster, not a reference swap.
        private bool CanMove(bool holsterList, int index, int dir, int count)
        {
            if (Application.isPlaying) return false;

            int target = index + dir;
            if (target < 0 || target >= count) return false;

            return GetRowItem(holsterList, index) != null || GetRowItem(holsterList, target) != null;
        }

        private SuperItem GetRowItem(bool holsterList, int index) =>
            holsterList ? M.holsters[index].Item : M.EquipPoints[index].Item;

        //MWC — Moves the Item of a row to the next/previous row, swapping it with the Item already there,
        //and re-parents both GameObjects to their new Equip Point / Holster Slot using the Item Offsets asset values.
        private void MoveRowItem(bool holsterList, int index, int dir)
        {
            var list = holsterList ? holsters : EquipPoints;
            int target = index + dir;
            if (target < 0 || target >= list.arraySize) return;

            var a = list.GetArrayElementAtIndex(index).FindPropertyRelative("Item");
            var b = list.GetArrayElementAtIndex(target).FindPropertyRelative("Item");

            var itemA = a.objectReferenceValue as SuperItem;
            var itemB = b.objectReferenceValue as SuperItem;

            a.objectReferenceValue = itemB;
            b.objectReferenceValue = itemA;

            serializedObject.ApplyModifiedProperties(); //Apply before touching the Transforms, the rows are the source of truth

            ParentItemToRow(itemA, holsterList, target);
            ParentItemToRow(itemB, holsterList, index);

            //MWC — both items landed on a new pair: make sure each one is listed in the offsets asset
            SyncRow(holsterList, target);
            SyncRow(holsterList, index);

            serializedObject.Update();
        }

        //MWC — Re-parents a scene item to the Point/Slot of the given row and applies that row's stored offset.
        //Skipped for prefab items, and for scene items the user parented outside this character's points/slots.
        private void ParentItemToRow(SuperItem item, bool holsterList, int index)
        {
            if (item == null || item.gameObject.IsPrefab()) return;
            if (!IsManagerPoint(item.transform.parent)) return;

            var so = M.ItemOffsets;
            Transform point;
            TransformOffset offset;

            if (holsterList)
            {
                var h = M.holsters[index];
                if (h == null || h.ID == null || h.Slots == null) return;

                int slot = so != null ? so.GetHolsterSlot(item.ItemType, h.ID) : 0;
                if (slot < 0 || slot >= h.Slots.Count) return;

                point = h.Slots[slot].Value;
                offset = so != null ? so.GetHolsterOffset(item.ItemType, h.ID) : new TransformOffset(0);
            }
            else
            {
                var ep = M.EquipPoints[index];
                if (ep == null || ep.ID == null) return;

                point = ep.Point.Value;
                offset = so != null ? so.GetOffset(item.ItemType, ep.ID) : new TransformOffset(0);
            }

            if (point == null) return;

            Undo.SetTransformParent(item.transform, point, "Move Super Item");
            Undo.RecordObject(item.transform, "Move Super Item");
            offset.RestoreTransform(item.transform);
            EditorUtility.SetDirty(item.transform);
        }

        //MWC — True when the Transform is one of this manager's Equip Points or Holster Slots
        private bool IsManagerPoint(Transform t)
        {
            if (t == null) return false;

            foreach (var ep in M.EquipPoints)
                if (ep != null && ep.Point.Value == t) return true;

            foreach (var h in M.holsters)
            {
                if (h == null || h.Slots == null) continue;

                foreach (var slot in h.Slots)
                    if (slot.Value == t) return true;
            }
            return false;
        }

        private static GUIContent editIcon;
        public static GUIContent EditIcon
        {
            get
            {
                editIcon ??= new GUIContent(EditorGUIUtility.IconContent("TransformTool"))
                {
                    //MWC — a Holster with no Item now authors its Slot Transforms instead of an Item offset
                    tooltip = "Edit Offset in Item.\nHolsters with NO Item: Move/Rotate the Slot Transforms"
                };
                return editIcon;
            }
        }

        private void OnSceneGUI()
        {
            if (Application.isPlaying) return;

            if (EditOffsetsItems.boolValue == false) return;

            EquipPointsSceneEdit();
            HolsterSceneEdit();
        }

        private void EquipPointsSceneEdit()
        {
            if (Editor_Tabs1.intValue != 1) return;

            if (M.EquipPoints != null && SelectedEquipPointSlot.intValue >= 0 && SelectedEquipPointSlot.intValue < M.EquipPoints.Count)
            {
                var EquipPoint = M.EquipPoints[SelectedEquipPointSlot.intValue];

                if (EquipPoint != null && EquipPoint.Item != null)
                {
                    var Item = EquipPoint.Item;
                    var Point = EquipPoint.Point.Value;

                    //MWC — the handles edit the SuperItemOffsets asset entry now (Item + Equip Point); the item stores no offsets.
                    //SetOffset upserts, so dragging a handle creates the entry when it doesn't exist yet.
                    var so = M.ItemOffsets;
                    if (so == null || Item.ItemType == null || Point == null) return;

                    var offset = so.GetOffset(Item.ItemType, EquipPoint.ID); //Identity when there's no entry yet

                    var Rotation = Tools.pivotRotation == PivotRotation.Local ? Point.rotation : Quaternion.identity;

                    if (Tools.current == Tool.Move)
                    {
                        using (var cc = new EditorGUI.ChangeCheckScope())
                        {
                            Vector3 piv = Point.TransformPoint(offset.Position);
                            Vector3 NewPivPosition = Handles.PositionHandle(piv, Rotation);

                            if (cc.changed)
                            {
                                Undo.RecordObject(so, "Change Position Offset"); //MWC — the asset owns the data now
                                offset.Position = Point.InverseTransformPoint(NewPivPosition);
                                so.SetOffset(Item.ItemType, EquipPoint.ID, offset);
                                EditorUtility.SetDirty(so);
                            }
                        }

                    }
                    else if (Tools.current == Tool.Rotate)
                    {
                        Vector3 piv = Point.TransformPoint(offset.Position);

                        // MWC — the item's true world rotation (Point applies the offset as a child)
                        Quaternion currentWorld = Point.rotation * Quaternion.Euler(offset.Rotation);

                        if (DoRotationHandle(currentWorld, piv, ref m_RotateStartWorld, out var newWorld))
                        {
                            Undo.RecordObject(so, "Change Rotation Offset"); //MWC — the asset owns the data now

                            // MWC — new world rotation back to a local offset under the Point
                            Quaternion localRot = Quaternion.Inverse(Point.rotation) * newWorld;
                            offset.Rotation = localRot.eulerAngles;
                            so.SetOffset(Item.ItemType, EquipPoint.ID, offset);
                            EditorUtility.SetDirty(so);
                        }
                    }
                    else if (Tools.current == Tool.Scale)
                    {
                        using (var cc = new EditorGUI.ChangeCheckScope())
                        {
                            Vector3 piv = Point.TransformPoint(offset.Position);

                            Vector3 NewPivScale = Handles.ScaleHandle(offset.Scale, piv, Rotation, HandleUtility.GetHandleSize(piv));

                            if (cc.changed)
                            {
                                Undo.RecordObject(so, "Change Scale Offset"); //MWC — the asset owns the data now
                                offset.Scale = NewPivScale;
                                so.SetOffset(Item.ItemType, EquipPoint.ID, offset);
                                EditorUtility.SetDirty(so);
                            }
                        }
                    }
                }
            }
        }

        private void HolsterSceneEdit()
        {
            if (Editor_Tabs1.intValue != 2) return; //Only show the handles when the Holster tab is selected

            // MWC — guard the selected index like EquipPointsSceneEdit does; SelectedHolster is -1 when nothing is selected (or stale after a delete), which threw IndexOutOfRangeException every repaint
            if (M.holsters != null && SelectedHolster.intValue >= 0 && SelectedHolster.intValue < M.holsters.Count)
            {
                var Holster = M.holsters[SelectedHolster.intValue];

                //MWC — No Item in this Holster: the handles author the Slot Transforms themselves instead of an Item offset
                if (Holster != null && Holster.Item == null) { HolsterSlotsSceneEdit(Holster); return; }

                if (Holster != null && Holster.Item != null)
                {
                    var Item = Holster.Item;

                    //MWC — the handles edit the SuperItemOffsets asset entry now (Item + Holster); the item stores no offsets.
                    //SetHolsterOffset upserts, so dragging a handle creates the entry when it doesn't exist yet.
                    var so = M.ItemOffsets;
                    if (so == null || Item.ItemType == null) return;

                    var slot = so.GetHolsterSlot(Item.ItemType, Holster.ID); //No entry → slot 0

                    // MWC — bail out if the slot index is invalid or the Transform reference is unassigned to avoid an out-of-range/NullReference on the handles
                    if (Holster.Slots == null || slot < 0 || slot >= Holster.Slots.Count) return;
                    var Point = Holster.GetSlot(slot);
                    if (Point == null) return;

                    var offset = so.GetHolsterOffset(Item.ItemType, Holster.ID); //Identity when there's no entry yet

                    var Rotation = Tools.pivotRotation == PivotRotation.Local ? Point.rotation : Quaternion.identity;

                    if (Tools.current == Tool.Move)
                    {
                        using (var cc = new EditorGUI.ChangeCheckScope())
                        {
                            Vector3 piv = Point.TransformPoint(offset.Position);
                            Vector3 NewPivPosition = Handles.PositionHandle(piv, Rotation);

                            if (cc.changed)
                            {
                                Undo.RecordObject(so, "Change Position Offset"); //MWC — the asset owns the data now
                                offset.Position = Point.InverseTransformPoint(NewPivPosition);
                                so.SetHolsterOffset(Item.ItemType, Holster.ID, offset);
                                EditorUtility.SetDirty(so);
                            }
                        }

                    }
                    else if (Tools.current == Tool.Rotate)
                    {
                        Vector3 piv = Point.TransformPoint(offset.Position);

                        // MWC — the item's true world rotation (Point applies the offset as a child)
                        Quaternion currentWorld = Point.rotation * Quaternion.Euler(offset.Rotation);

                        if (DoRotationHandle(currentWorld, piv, ref m_RotateStartWorld, out var newWorld))
                        {
                            Undo.RecordObject(so, "Change Rotation Offset"); //MWC — the asset owns the data now

                            // MWC — new world rotation back to a local offset under the Point
                            Quaternion localRot = Quaternion.Inverse(Point.rotation) * newWorld;
                            offset.Rotation = localRot.eulerAngles;
                            so.SetHolsterOffset(Item.ItemType, Holster.ID, offset);
                            EditorUtility.SetDirty(so);
                        }
                    }
                    else if (Tools.current == Tool.Scale)
                    {
                        using (var cc = new EditorGUI.ChangeCheckScope())
                        {
                            Vector3 piv = Point.TransformPoint(offset.Position);

                            Vector3 NewPivScale = Handles.ScaleHandle(offset.Scale, piv, Rotation, HandleUtility.GetHandleSize(piv));

                            if (cc.changed)
                            {
                                Undo.RecordObject(so, "Change Scale Offset"); //MWC — the asset owns the data now
                                offset.Scale = NewPivScale;
                                so.SetHolsterOffset(Item.ItemType, Holster.ID, offset);
                                EditorUtility.SetDirty(so);
                            }
                        }
                    }
                }
            }
        }

        // MWC — world rotation of the edited Holster Slot captured at the start of a rotate-handle drag (prevents compounding)
        private Quaternion m_SlotRotateStart = Quaternion.identity;

        //MWC — Move/Rotate the Holster Slot Transform itself. Only the Slot selected in the Slots list, and only when
        //the Holster has NO Item: with an Item assigned the handles author its offset instead. Equip Points are never
        //editable this way (their Points are shared by every item, so moving one would shift all of them).
        private void HolsterSlotsSceneEdit(SItemHolster holster)
        {
            if (holster.Slots == null || holster.Slots.Count == 0) return;

            if (Tools.current != Tool.Move && Tools.current != Tool.Rotate) return; //Only Move and Rotate: a Slot's scale is never used

            var index = SelectedHolsterSlot.intValue;
            if (index < 0 || index >= holster.Slots.Count) return; //Nothing selected, or a stale index from another Holster

            var point = holster.Slots[index] != null ? holster.Slots[index].Value : null;
            if (point == null) return;

            if (Tools.current == Tool.Move)
            {
                var rotation = Tools.pivotRotation == PivotRotation.Local ? point.rotation : Quaternion.identity;

                using (var cc = new EditorGUI.ChangeCheckScope())
                {
                    var newPosition = Handles.PositionHandle(point.position, rotation);

                    if (cc.changed)
                    {
                        Undo.RecordObject(point, "Move Holster Slot");
                        point.position = newPosition;
                    }
                }
            }
            else
            {
                if (DoRotationHandle(point.rotation, point.position, ref m_SlotRotateStart, out var newRotation))
                {
                    Undo.RecordObject(point, "Rotate Holster Slot");
                    point.rotation = newRotation;
                }
            }
        }
    }
#endif
}
