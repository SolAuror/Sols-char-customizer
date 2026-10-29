// Last Change: 9/30/2026 10:58am
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

namespace MalbersAnimations.Items
{
    //MWC: ScriptableObject that stores ALL the per-character offsets for Super Items:
    //Equip Point entries (Item + Equip Point → Offset + optional Animator Item Type override) and
    //Holster entries (Item + Holster → Offset + Holster Anim + Holster Slot).
    //The Super Item component only keeps its default HolsterID and EquipPointID; everything else lives here.
    //When no entry is found the internal default offset is used: Pos(0,0,0) Rot(0,0,0) Scale(1,1,1).

    /// <summary> Equip Point entry: which Item, in which Equip Point, the local Transform offset to apply,
    /// and an optional Animator Item Type override </summary>
    [System.Serializable]
    public class SItemOffset
    {
        [Tooltip("Super Item Type and Equip Point this offset applies to")]
        public IDPair<SuperItemID, EquipPointID> ID;

        [Tooltip("Local Position/Rotation/Scale offset to apply when the item is equipped to the Equip Point")]
        public TransformOffset Offset = new(0); //MWC: ctor(0) ensures Scale defaults to one (default struct would be zero)

        //MWC — Animator Item Type override moved here from the Super Item component
        [Tooltip("Item Type sent to the Animator ([Left Item Type]/[Right Item Type] parameters) instead of the item's own Item Type.\n" +
            "Leave empty to use the item's Item Type")]
        public SuperItemID ItemTypeOverride;
    }

    //MWC — Equip Point Equivalent: lets a character without the Equip Point an Item was authored for use another one.
    //E.g. the Wolf has no hands: [Left Hand → Head] and [Right Hand → Head] let it equip Swords on its [Head] point.
    /// <summary> Equip Point Equivalent: Items authored for [Equip Point] can be equipped on [Equivalent] instead </summary>
    [System.Serializable]
    public class SEquipPointEquivalent
    {
        [Tooltip("Equip Point the Items were authored for (e.g. Left Hand)")]
        public EquipPointID EquipPoint;

        [Tooltip("Equip Point of this character that can hold those Items instead (e.g. Head)")]
        public EquipPointID Equivalent;
    }

    //MWC — Action Equivalent: lets a character whose inputs send one Action ID drive Items authored with another.
    //E.g. the Wolf only has an [Attack] input: [Attack → Attack Light] makes that press play the Sword's [Attack Light].
    /// <summary> Action Equivalent: a press of [Action] plays [Equivalent] on an Item whose Active Action Set has no [Action] </summary>
    [System.Serializable]
    public class SActionEquivalent
    {
        [Tooltip("Action ID sent by the character's input (e.g. Attack)")]
        public ItemActionID Action;

        [Tooltip("Action ID played instead on Items that do not have [Action] (e.g. Attack Light)")]
        public ItemActionID Equivalent;
    }

    //MWC — Holster entry: which Item, in which Holster, the local Transform offset, the Holster Anim and the Holster Slots
    /// <summary> Holster entry: which Item, in which Holster, the local Transform offset to apply,
    /// the Holster Anim override and the Holster Slot priority list </summary>
    [System.Serializable]
    public class SItemHolsterOffset : ISerializationCallbackReceiver //MWC — receiver only for the legacy HolsterSlot migration
    {
        [Tooltip("Super Item Type and Holster this entry applies to")]
        public IDPair<SuperItemID, HolsterID> ID;

        [Tooltip("Local Position/Rotation/Scale offset to apply when the item is stored/parented in the Holster")]
        public TransformOffset Offset = new(0);

        [Tooltip("Overrides the Holster ID value used to play the correct Equip/Unequip animation.\nLeave empty to use the item's Holster ID")]
        public HolsterID HolsterAnim;

        //MWC — Slot priority list (Dual Wielding): a holster can hold several copies of the same item, so one slot
        //index is not enough. The item takes the FIRST slot of this list that is still free; the list order is the
        //authored priority. E.g. [0, 2] → the first dagger goes to slot 0, the second one to slot 2.
        [Tooltip("A holster can have multiple Transforms to be parented to. These are the Indexes of the Transform Slots Array, " +
            "in priority order: the item takes the first one that is still free.\n" +
            "Dual Wielding: [0, 2] sends the first copy to slot 0 and the second one to slot 2")]
        //MWC — Starts EMPTY on purpose: Unity keeps a field initializer for keys missing from an existing asset, so
        //a pre-list entry would come back as [0] and silently drop its authored HolsterSlot. Empty is the "never
        //authored" sentinel OnAfterDeserialize migrates from; the Slots property below is what callers read.
        public List<int> HolsterSlots = new();

        //MWC — Legacy single-slot field. Kept (hidden) so existing assets keep their authored value: it is folded
        //into HolsterSlots on deserialization. Do not use it at runtime — read HolsterSlots / Slots instead.
        [SerializeField, HideInInspector] private int HolsterSlot;

        //MWC — Authored priority list, never empty: an entry with no slots means slot 0
        public List<int> Slots => HolsterSlots != null && HolsterSlots.Count > 0 ? HolsterSlots : s_DefaultSlots;

        private static readonly List<int> s_DefaultSlots = new() { 0 };

        //MWC — First (preferred) slot. What the single-slot API and the Super Item Manager inspector work with
        public int FirstSlot => Slots[0];

        public void OnBeforeSerialize() { }

        //MWC — Migration: assets authored before the priority list only have the legacy int. Folding it in when the
        //list is empty is idempotent (a migrated entry always has at least one slot) and never loses an authored list.
        public void OnAfterDeserialize()
        {
            if (HolsterSlots == null) HolsterSlots = new List<int>();

            if (HolsterSlots.Count == 0) HolsterSlots.Add(Mathf.Max(0, HolsterSlot));
        }
    }

    /// <summary>
    /// Stores all the Equip Point and Holster offsets for the Super Items of a character.
    /// Equip entries are keyed by (Super Item ID + Equip Point ID), Holster entries by (Super Item ID + Holster ID).
    /// This lets the same item (e.g. a dagger) use different offsets on the Left vs Right hand, and lets different
    /// character models keep their own offset table. If no entry is found the identity offset is used
    /// (Pos 0,0,0 - Rot 0,0,0 - Scale 1,1,1).
    /// </summary>
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/super-item/super-item-system/super-item-manager/item-offsets")]
    [CreateAssetMenu(menuName = "Malbers Animations/Super Item/Item Offsets", fileName = "New Item Offsets", order = -1000)]
    public class SuperItemOffsets : ScriptableObject
    {
        [Tooltip("Equip Point offsets, keyed by Super Item ID + Equip Point ID")]
        public List<SItemOffset> Offsets = new();

        //MWC — Holster entries (Offset + Holster Anim + Holster Slot), keyed by Super Item ID + Holster ID
        [Tooltip("Holster entries (Offset + Holster Anim + Holster Slot), keyed by Super Item ID + Holster ID")]
        public List<SItemHolsterOffset> HolsterOffsets = new();

        [Tooltip("Equip Points this character can use in place of the ones the Items were authored for.\n" +
            "E.g. [Left Hand → Head] and [Right Hand → Head] let a Wolf equip hand Items on its Head Equip Point.\n" +
            "The Item's own Equip Point always wins when the character has it")]
        public List<SEquipPointEquivalent> EquipPointEquivalents = new();

        [Tooltip("Action IDs this character's inputs can play in place of the ones the Items were authored with.\n" +
            "E.g. [Attack → Attack Light] lets a Wolf with only an Attack input use the Sword's Attack Light.\n" +
            "The Item's own Action always wins when its Active Action Set has it")]
        public List<SActionEquivalent> ActionEquivalents = new();

        // ── Action Equivalents ───────────────────────────────────────────────────

        /// <summary> Action ID an Item Action Set plays for [action]: [action] itself when the Set has it, else its first Equivalent the Set has (list order is the priority), else [action] </summary>
        public ItemActionID ResolveAction(ItemActionID action, ItemActionSet set)
        {
            if (action == null || set == null || set.actions == null || ActionEquivalents == null || ActionEquivalents.Count == 0) return action;
            if (HasAction(set, action)) return action;

            foreach (var e in ActionEquivalents)
            {
                if (e != null && e.Action == action && e.Equivalent != null && HasAction(set, e.Equivalent)) return e.Equivalent;
            }
            return action;
        }

        /// <summary> Is [equivalent] listed as an Equivalent of [action]? </summary>
        public bool IsActionEquivalent(ItemActionID action, ItemActionID equivalent)
        {
            if (action == null || equivalent == null || ActionEquivalents == null) return false;

            foreach (var e in ActionEquivalents)
            {
                if (e != null && e.Action == action && e.Equivalent == equivalent) return true;
            }
            return false;
        }

        /// <summary> Does the Action Set have an Action with this ID? </summary>
        private static bool HasAction(ItemActionSet set, ItemActionID id)
        {
            for (int i = 0; i < set.actions.Count; i++)
            {
                var a = set.actions[i];
                if (a != null && a.ActionID == id) return true;
            }
            return false;
        }

        // ── Equip Point Equivalents ──────────────────────────────────────────────

        /// <summary> Is [equivalent] listed as an Equivalent of [equipPoint]? </summary>
        public bool IsEquivalent(EquipPointID equipPoint, EquipPointID equivalent)
        {
            if (equipPoint == null || equivalent == null || EquipPointEquivalents == null) return false;

            foreach (var e in EquipPointEquivalents)
            {
                if (e != null && e.EquipPoint == equipPoint && e.Equivalent == equivalent) return true;
            }
            return false;
        }

        /// <summary> Can the Item use [equipPoint] as an Equivalent of its primary or secondary Equip Point? </summary>
        public bool IsEquivalent(SuperItem item, EquipPointID equipPoint) =>
            item != null && (IsEquivalent(item.EquipPoint, equipPoint) || IsEquivalent(item.SecondaryEquipPoint, equipPoint));

        /// <summary> First Equivalent of [equipPoint] that exists in [available] (list order is the priority). Null when there is none </summary>
        public EquipPointID GetEquivalent(EquipPointID equipPoint, Dictionary<EquipPointID, SEquipPoint> available)
        {
            if (equipPoint == null || available == null || EquipPointEquivalents == null) return null;

            foreach (var e in EquipPointEquivalents)
            {
                if (e != null && e.EquipPoint == equipPoint && e.Equivalent != null && available.ContainsKey(e.Equivalent)) return e.Equivalent;
            }
            return null;
        }

        // ── Equip Point entries ──────────────────────────────────────────────────

        /// <summary>
        /// Tries to find the stored offset for the given Item Type in the given Equip Point.
        /// Returns false when there's no entry (caller should fall back to the identity offset).
        /// </summary>
        public bool TryGetOffset(SuperItemID item, EquipPointID equipPoint, out TransformOffset offset)
        {
            var entry = GetEntry(item, equipPoint); //MWC — routed through the shared entry finder
            offset = entry != null ? entry.Offset : default;
            return entry != null;
        }

        //MWC — identity fallback baked in: Pos(0,0,0) Rot(0,0,0) Scale(1,1,1) when there's no entry
        /// <summary> Offset for the given Item Type in the given Equip Point. Identity offset when there's no entry </summary>
        public TransformOffset GetOffset(SuperItemID item, EquipPointID equipPoint) =>
            TryGetOffset(item, equipPoint, out var offset) ? offset : new TransformOffset(0);

        //MWC — Animator Item Type override lookup (moved here from the Super Item component)
        /// <summary> Item Type override to send to the Animator for the given Item + Equip Point. Null when there's no entry or no override </summary>
        public SuperItemID GetItemTypeOverride(SuperItemID item, EquipPointID equipPoint) =>
            GetEntry(item, equipPoint)?.ItemTypeOverride;

        //MWC — shared (Item + Equip Point) entry finder
        private SItemOffset GetEntry(SuperItemID item, EquipPointID equipPoint)
        {
            if (item == null || equipPoint == null || Offsets == null) return null;

            foreach (var o in Offsets)
            {
                if (o != null && o.ID.ID == item && o.ID.Value == equipPoint) return o;
            }
            return null;
        }

        //MWC: Upsert helper used by the SuperItemManager editor "Save" button and the Scene View handles.
        //Stores the offset for an (Item + Equip Point) pair: modifies the existing entry if found, otherwise adds a new one.
        //Returns true when a new entry was added, false when an existing one was modified.
        public bool SetOffset(SuperItemID item, EquipPointID equipPoint, TransformOffset offset)
        {
            Offsets ??= new();

            var entry = GetEntry(item, equipPoint);

            if (entry != null)
            {
                entry.Offset = offset; //modify existing
                return false;
            }

            Offsets.Add(new SItemOffset { ID = new(item, equipPoint), Offset = offset }); //add new
            return true;
        }

        //MWC — Creates the entry for the pair if it's missing, WITHOUT touching an existing one.
        //Used by the Save button for items that are assigned to an Equip Point but not parented to it:
        //there's no local transform worth capturing, but the pair must still show up in the list so it can be authored.
        //Returns true when a new entry was added.
        public bool EnsureOffset(SuperItemID item, EquipPointID equipPoint)
        {
            if (item == null || equipPoint == null) return false;

            Offsets ??= new();

            if (GetEntry(item, equipPoint) != null) return false;

            Offsets.Add(new SItemOffset { ID = new(item, equipPoint), Offset = new TransformOffset(0) });
            return true;
        }

        // ── Holster entries ──────────────────────────────────────────────────────
        //MWC — the whole holster section is new: Offset + Holster Anim + Holster Slot per (Item + Holster)

        /// <summary> Holster entry for the given Item Type in the given Holster. Null when there's no entry </summary>
        public SItemHolsterOffset GetHolsterEntry(SuperItemID item, HolsterID holster)
        {
            if (item == null || holster == null || HolsterOffsets == null) return null;

            foreach (var o in HolsterOffsets)
            {
                if (o != null && o.ID.ID == item && o.ID.Value == holster) return o;
            }
            return null;
        }

        /// <summary> Offset for the given Item Type in the given Holster. Identity offset when there's no entry </summary>
        public TransformOffset GetHolsterOffset(SuperItemID item, HolsterID holster)
        {
            var entry = GetHolsterEntry(item, holster);
            return entry != null ? entry.Offset : new TransformOffset(0);
        }

        /// <summary> Preferred (first) Holster Slot index for the given Item Type in the given Holster. Zero when there's no entry </summary>
        public int GetHolsterSlot(SuperItemID item, HolsterID holster) => GetHolsterEntry(item, holster)?.FirstSlot ?? 0;

        //MWC — Dual Wielding: the whole authored priority list. Never null and never empty (no entry → slot 0),
        //so callers can walk it directly. The returned list is the entry's own list: read-only for callers.
        /// <summary> Holster Slot indexes for the given Item Type in the given Holster, in priority order </summary>
        public List<int> GetHolsterSlots(SuperItemID item, HolsterID holster) => GetHolsterEntry(item, holster)?.Slots ?? s_DefaultSlots;

        private static readonly List<int> s_DefaultSlots = new() { 0 }; //MWC — shared fallback for items with no entry

        /// <summary> Holster Anim override for the given Item Type in the given Holster. Null when there's no entry or no override
        /// (caller should fall back to the item's Holster ID) </summary>
        public HolsterID GetHolsterAnim(SuperItemID item, HolsterID holster) => GetHolsterEntry(item, holster)?.HolsterAnim;

        //MWC: Upsert helper used by the SuperItemManager editor "Save" button and the Scene View handles.
        //slot below 0 keeps the existing slot (or 0 on a new entry). Returns true when a new entry was added.
        public bool SetHolsterOffset(SuperItemID item, HolsterID holster, TransformOffset offset, int slot = -1)
        {
            HolsterOffsets ??= new();

            var entry = GetHolsterEntry(item, holster);

            if (entry != null)
            {
                entry.Offset = offset; //modify existing (HolsterAnim is preserved)
                if (slot >= 0) SetFirstSlot(entry, slot); //MWC — only the preferred slot; the rest of the priority list is kept
                return false;
            }

            HolsterOffsets.Add(new SItemHolsterOffset { ID = new(item, holster), Offset = offset, HolsterSlots = new() { Mathf.Max(0, slot) } }); //add new
            return true;
        }

        //MWC — Sets ONLY the preferred (first) Slot index for the pair, creating the entry (with the default offset)
        //when it's missing. Used by the Slot field drawn directly in the Super Item Manager's Holster rows.
        //The rest of the priority list is untouched — it is authored in the Item Offsets asset.
        public void SetHolsterSlot(SuperItemID item, HolsterID holster, int slot)
        {
            if (item == null || holster == null) return;

            HolsterOffsets ??= new();

            var entry = GetHolsterEntry(item, holster);

            if (entry != null) { SetFirstSlot(entry, slot); return; }

            HolsterOffsets.Add(new SItemHolsterOffset { ID = new(item, holster), Offset = new TransformOffset(0), HolsterSlots = new() { Mathf.Max(0, slot) } });
        }

        //MWC — Replaces the head of the priority list without disturbing the fallbacks behind it
        private static void SetFirstSlot(SItemHolsterOffset entry, int slot)
        {
            slot = Mathf.Max(0, slot);

            entry.HolsterSlots ??= new();

            if (entry.HolsterSlots.Count == 0) entry.HolsterSlots.Add(slot);
            else entry.HolsterSlots[0] = slot;
        }

        //MWC — Creates the holster entry for the pair if it's missing, WITHOUT touching an existing one.
        //Same purpose as EnsureOffset: items assigned to a Holster but not parented to one of its Slots
        //still need their entry in the list. Returns true when a new entry was added.
        public bool EnsureHolsterOffset(SuperItemID item, HolsterID holster)
        {
            if (item == null || holster == null) return false;

            HolsterOffsets ??= new();

            if (GetHolsterEntry(item, holster) != null) return false;

            //MWC — explicit slot 0: runtime-created entries never get OnAfterDeserialize, so the list would stay empty
            HolsterOffsets.Add(new SItemHolsterOffset { ID = new(item, holster), Offset = new TransformOffset(0), HolsterSlots = new() { 0 } });
            return true;
        }
    }

    // ── Editor-only inspector ─────────────────────────────────────────────────────
#if UNITY_EDITOR

    //MWC: Custom inspector for SuperItemOffsets. Draws the Equip Point and Holster lists as ReorderableLists where each
    //entry is a one-line foldout (Super Item + Equip Point/Holster) that expands into Position/Rotation/Scale plus the
    //per-list extras (Item Type Override / Holster Anim). The Holster list also draws its Slot at the end of the
    //element row — same placement as the Super Item Manager's Holster list — instead of as an expanded row.
    //Also validates each table: missing IDs are tinted red and duplicated ID pairs yellow.
    [CustomEditor(typeof(SuperItemOffsets))]
    [CanEditMultipleObjects]
    public class SuperItemOffsetsEditor : Editor
    {
        //Cached labels/contents
        private static readonly GUIContent s_Position = new("Position", "Local Position offset");
        private static readonly GUIContent s_Rotation = new("Rotation", "Local Rotation offset (Euler)");
        private static readonly GUIContent s_Scale = new("Scale", "Local Scale");
        private static readonly GUIContent s_Reset = new("Reset", "Reset the offset: Position/Rotation to zero and Scale to one");
        private static readonly GUIContent s_ItemHeader = new("Super Item", "Super Item Type this entry applies to");
        private static readonly GUIContent s_PointHeader = new("Equip Point", "Equip Point this entry applies to");
        private static readonly GUIContent s_HolsterHeader = new("Holster", "Holster this entry applies to");
        private static readonly GUIContent s_TypeOverride = new("Anim Type", "Item Type sent to the Animator ([Left/Right Item Type] parameters) instead of the item's own Item Type. Empty → item's Item Type");
        private static readonly GUIContent s_HolsterAnim = new("Anim", "Overrides the Holster ID value used to play the correct Equip/Unequip animation. Empty → item's Holster ID");
        //MWC — Slot priority list, drawn as a compact comma-separated field ("0, 2") so it still fits on the element row
        private static readonly GUIContent s_HolsterSlot = new("Slots",
            "Indexes of the Holster's Transform Slots array the item is parented to, in priority order (comma separated).\n" +
            "The item takes the first one that is still free.\n\nDual Wielding: [0, 2] sends the first copy to slot 0 and the second one to slot 2");

        //MWC — "+" dropdown and row context menu entries
        private static readonly GUIContent s_NewEntry = new("New Entry", "Adds an empty entry");
        private static readonly GUIContent s_DuplicateEntry = new("Duplicate Entry", "Adds a copy of the selected entry right below it");
        private static readonly GUIContent s_DeleteEntry = new("Delete Entry", "Removes this entry");

        //Layout constants
        private const float HANDLE_W = 16f;  //MWC: the ReorderableList draws its drag handle inside the element rect, at its left edge
        private const float FOLD_W = 13f;
        private const float PAD = 2f;
        private const float VEC_LABEL_W = 66f; //MWC — fits the longest row label ("Anim Type") without clipping
        //MWC: IDDrawer paints its "⋮" pane-options button at (rect.x - 20), outside the rect it receives,
        //so every ID field needs this much empty space on its left or the button overlaps the previous control.
        private const float ID_MENU_W = 20f; //MWC — exactly the 20px IDDrawer offsets its "⋮" by, no slack

        //MWC — Leading gutter before the FIRST ID field. It is ~0 on purpose: IDDrawer already indents its field
        //inside the rect it is given and then draws the "⋮" 20px back from there, so the button lands in space the
        //drawer reserves by itself. Adding ID_MENU_W here as well left the "⋮" floating in ~20px of dead air between
        //the foldout and the Super Item field. The gutter is still needed BETWEEN the two ID columns, where it keeps
        //the second field's button clear of the first field's object picker.
        private const float LEAD_MENU_W = 2f;
        private const float HEADER_OFFSET = 14f;
        private const float ROW_END_W = 56f; //MWC — compact field at the end of the element row (Holster Slots, "0, 2")

        //MWC — both lists (Equip Points and Holsters) share the same drawing logic; this wraps one of them.
        //Internal (was private) so the Super Item Manager inspector can draw the very same table in its tabs.
        internal class OffsetTable
        {
            public SerializedProperty List;
            public ReorderableList Reo;
            public GUIContent ValueHeader;                    //Second column header (Equip Point / Holster)
            public GUIContent[] ExtraLabels;                  //Rows drawn after Position/Rotation/Scale
            public string[] ExtraProps;                       //Property names for the extra rows
            public string EmptyValueName;                     //Name used in the validation messages (Equip Point / Holster)

            //MWC — Optional compact int drawn at the END of the element row (no label; the header names the column),
            //like the Slot field in the Super Item Manager's Holster list. Null on tables that don't use it.
            public string RowEndProp;
            public GUIContent RowEndHeader;

            //Validation caches, rebuilt once per inspector pass (not per element repaint)
            public HashSet<int> Duplicated { get; } = new();
            public int MissingIDs;

            //MWC — Queued list mutations. GenericMenu callbacks run on a later event, outside the serializedObject
            //Update/Apply scope of OnInspectorGUI, so the array is never resized from inside a callback (or from
            //inside the element draw loop): the request is stored and applied at the top of Draw().
            private int pendingDuplicate = -1;
            private int pendingDelete = -1;
            private bool pendingAdd;

            public OffsetTable(SerializedProperty list, GUIContent valueHeader, string emptyValueName,
                GUIContent[] extraLabels, string[] extraProps, string rowEndProp = null, GUIContent rowEndHeader = null)
            {
                List = list;
                ValueHeader = valueHeader;
                EmptyValueName = emptyValueName;
                ExtraLabels = extraLabels;
                ExtraProps = extraProps;
                RowEndProp = rowEndProp;
                RowEndHeader = rowEndHeader;

                Reo = new ReorderableList(list.serializedObject, list, true, true, true, true)
                {
                    drawHeaderCallback = DrawHeader,
                    drawElementCallback = DrawElement,
                    elementHeightCallback = ElementHeight,
                    onAddDropdownCallback = OnAddDropdown, //MWC — New Entry / Duplicate Selected (replaces onAddCallback)
                };
            }

            /// <summary> Finds empty IDs and repeated ID pairs so the list can tint them </summary>
            public void Validate()
            {
                Duplicated.Clear();
                MissingIDs = 0;

                for (int i = 0; i < List.arraySize; i++)
                {
                    GetIDs(i, out var itemA, out var valueA);

                    if (itemA == null || valueA == null) { MissingIDs++; continue; }

                    for (int j = 0; j < i; j++)
                    {
                        GetIDs(j, out var itemB, out var valueB);

                        if (itemA == itemB && valueA == valueB) { Duplicated.Add(i); break; }
                    }
                }
            }

            private void GetIDs(int index, out Object item, out Object value)
            {
                var id = List.GetArrayElementAtIndex(index).FindPropertyRelative("ID"); //IDPair key field
                item = id.FindPropertyRelative("ID").objectReferenceValue;
                value = id.FindPropertyRelative("Value").objectReferenceValue;
            }

            //MWC: Single source for the column math, so header, ID fields and the offset rows all line up.
            //Left margin = [drag handle][foldout][pane-options gutter] before the first ID field.
            //The two ID columns yield the tail of the row to the row-end field (Slot) when the table has one.
            private void GetColumns(Rect rect, out float x, out float width, out float half)
            {
                x = rect.x + HANDLE_W + FOLD_W + LEAD_MENU_W; //MWC — see LEAD_MENU_W: no full gutter before the first ID
                width = rect.xMax - x - RowEndReserve;
                half = (width - PAD - ID_MENU_W) * 0.5f;
            }

            //MWC — space the ID columns give up at the end of the row for the row-end field
            private float RowEndReserve => RowEndProp != null ? ROW_END_W + PAD : 0f;

            private void DrawHeader(Rect rect)
            {
                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0; //MWC — see DrawElement: the rects here are absolute, not indent-relative

                float line = EditorGUIUtility.singleLineHeight;

                rect.xMin += HEADER_OFFSET; //MWC: the header rect is not indented by the drag handle, the element rects are

                GetColumns(rect, out var x, out _, out var half);

                EditorGUI.LabelField(new Rect(x, rect.y, half, line), s_ItemHeader, EditorStyles.miniLabel);
                EditorGUI.LabelField(new Rect(x + half + PAD + ID_MENU_W, rect.y, half, line), ValueHeader, EditorStyles.miniLabel);

                //MWC — names the row-end column (the field itself is drawn without a label)
                if (RowEndProp != null)
                    EditorGUI.LabelField(new Rect(rect.xMax - ROW_END_W, rect.y, ROW_END_W, line), RowEndHeader, EditorStyles.miniLabel);

                EditorGUI.indentLevel = indent;
            }

            private float ElementHeight(int index)
            {
                float line = EditorGUIUtility.singleLineHeight;
                float height = line + PAD;

                if (List.GetArrayElementAtIndex(index).isExpanded)
                    height += (line + 2) * (4 + ExtraProps.Length); //Position, Rotation, Scale + extras + Reset button row

                return height;
            }

            //MWC — Every rect in this list is computed in absolute coordinates, so any inherited indent has to go.
            //It matters for the ID fields: IDDrawer runs the rect through EditorGUI.PrefixLabel (which applies
            //IndentedRect) and only zeroes indentLevel afterwards, so an inherited indent pushed the ID field — and
            //the "⋮" button that hangs 20px off its left — to the right, leaving a gap after the foldout.
            private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
            {
                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;

                try { DrawElementBody(rect, index); }
                finally { EditorGUI.indentLevel = indent; }
            }

            private void DrawElementBody(Rect rect, int index)
            {
                var element = List.GetArrayElementAtIndex(index);
                var id = element.FindPropertyRelative("ID");
                var itemID = id.FindPropertyRelative("ID");
                var valueID = id.FindPropertyRelative("Value");
                var offset = element.FindPropertyRelative("Offset");

                float line = EditorGUIUtility.singleLineHeight;
                rect.y += 2;

                GetColumns(rect, out var x, out _, out var half);

                RowContextMenu(new Rect(rect.x, rect.y, rect.width, line), index); //MWC — right-click → Duplicate / Delete

                element.isExpanded =
                    EditorGUI.Foldout(new Rect(rect.x + HANDLE_W, rect.y, FOLD_W, line), element.isExpanded, GUIContent.none);

                bool isDuplicated = Duplicated.Contains(index);
                var defaultColor = GUI.color;

                GUI.color = itemID.objectReferenceValue == null ? Color.red : isDuplicated ? Color.yellow : defaultColor;
                EditorGUI.PropertyField(new Rect(x, rect.y, half, line), itemID, GUIContent.none);

                GUI.color = valueID.objectReferenceValue == null ? Color.red : isDuplicated ? Color.yellow : defaultColor;
                EditorGUI.PropertyField(new Rect(x + half + PAD + ID_MENU_W, rect.y, half, line), valueID, GUIContent.none);

                GUI.color = defaultColor;

                //MWC — Slots at the end of the row (same placement as the Super Item Manager's Holster list)
                if (RowEndProp != null)
                    DrawSlotList(new Rect(rect.xMax - ROW_END_W, rect.y, ROW_END_W, line), element.FindPropertyRelative(RowEndProp));

                if (!element.isExpanded) return;

                var position = offset.FindPropertyRelative(nameof(TransformOffset.Position));
                var rotation = offset.FindPropertyRelative(nameof(TransformOffset.Rotation));
                var scale = offset.FindPropertyRelative(nameof(TransformOffset.Scale));

                //MWC — value rows start right after the foldout: the ID_MENU_W gutter in the column math is only there
                //for the ID row's "⋮" button, and DrawRow adds its own gutter before the field. Including it here left
                //an empty column under the foldout.
                float rowX = rect.x + HANDLE_W + FOLD_W;

                var row = new Rect(rowX, rect.y + line + 2, rect.xMax - rowX, line);
                DrawRow(row, s_Position, position);
                row.y += line + 2;
                DrawRow(row, s_Rotation, rotation);
                row.y += line + 2;
                DrawRow(row, s_Scale, scale);

                //MWC — per-list extra rows (Item Type Override / Holster Anim + Holster Slot)
                for (int e = 0; e < ExtraProps.Length; e++)
                {
                    row.y += line + 2;
                    DrawRow(row, ExtraLabels[e], element.FindPropertyRelative(ExtraProps[e]));
                }

                row.y += line + 2;
                if (GUI.Button(new Rect(row.xMax - 60, row.y, 60, line), s_Reset, EditorStyles.miniButton))
                {
                    position.vector3Value = Vector3.zero;
                    rotation.vector3Value = Vector3.zero;
                    scale.vector3Value = Vector3.one;
                }
            }

            //MWC — Slot priority list as a compact comma-separated field ("0, 2"). A real int list (ReorderableList or
            //expanded array) would not fit on the element row, and the list is short by nature: one index per copy of
            //the item the holster can store. Delayed so the text is only parsed when editing ends, never mid-typing.
            private static void DrawSlotList(Rect rect, SerializedProperty slots)
            {
                if (slots == null || !slots.isArray) return;

                string current = SlotsToString(slots);
                string edited = EditorGUI.DelayedTextField(rect, current);

                if (edited == current) return;

                var parsed = ParseSlots(edited);

                slots.arraySize = parsed.Count;
                for (int i = 0; i < parsed.Count; i++) slots.GetArrayElementAtIndex(i).intValue = parsed[i];
            }

            private static string SlotsToString(SerializedProperty slots)
            {
                var sb = new System.Text.StringBuilder();

                for (int i = 0; i < slots.arraySize; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(slots.GetArrayElementAtIndex(i).intValue);
                }
                return sb.ToString();
            }

            //MWC — Accepts commas, spaces and semicolons. Negatives and duplicates are dropped (a repeated priority is
            //meaningless, a negative index can never resolve). Empty input falls back to slot 0 so an entry is never slotless.
            private static List<int> ParseSlots(string text)
            {
                var result = new List<int>();

                foreach (var token in text.Split(',', ';', ' '))
                {
                    if (int.TryParse(token, out int slot) && slot >= 0 && !result.Contains(slot)) result.Add(slot);
                }

                if (result.Count == 0) result.Add(0);

                return result;
            }

            //MWC — One value row: label on the left, field after an ID_MENU_W gutter.
            //IDDrawer paints its "⋮" pane button at (fieldRect.x - 20). Passing a real label makes the drawer
            //consume the label area itself, so the "⋮" ends up ON TOP of the label text (e.g. "Anim Type").
            //Drawing the label here and passing GUIContent.none keeps the field full-width, puts the "⋮" inside
            //the gutter, and lines every value column up (vectors and IDs start at the same x).
            private static void DrawRow(Rect row, GUIContent label, SerializedProperty prop)
            {
                EditorGUI.LabelField(new Rect(row.x, row.y, VEC_LABEL_W, row.height), label);

                float fieldX = row.x + VEC_LABEL_W + ID_MENU_W;
                EditorGUI.PropertyField(new Rect(fieldX, row.y, row.xMax - fieldX, row.height), prop, GUIContent.none);
            }

            //New entries must start with Scale one — the array-size increase copies the previous element,
            //and a default TransformOffset would leave the Scale at zero.
            private void OnAdd(ReorderableList list)
            {
                int index = List.arraySize;
                List.arraySize++;

                var element = List.GetArrayElementAtIndex(index);
                var id = element.FindPropertyRelative("ID");
                id.FindPropertyRelative("ID").objectReferenceValue = null;
                id.FindPropertyRelative("Value").objectReferenceValue = null;

                var offset = element.FindPropertyRelative("Offset");
                offset.FindPropertyRelative(nameof(TransformOffset.Position)).vector3Value = Vector3.zero;
                offset.FindPropertyRelative(nameof(TransformOffset.Rotation)).vector3Value = Vector3.zero;
                offset.FindPropertyRelative(nameof(TransformOffset.Scale)).vector3Value = Vector3.one;

                //MWC — reset the copied extras too (Item Type Override / Holster Anim) and the row-end field (Slot)
                foreach (var extra in ExtraProps) ResetProp(element, extra);

                if (RowEndProp != null) ResetProp(element, RowEndProp);

                element.isExpanded = true;
                list.index = index;
            }

            //MWC — "+" dropdown: an empty entry, or a copy of the selected one (Offset, extras and Slot included)
            private void OnAddDropdown(Rect buttonRect, ReorderableList list)
            {
                var menu = new GenericMenu();

                menu.AddItem(s_NewEntry, false, () => pendingAdd = true);

                if (list.index >= 0 && list.index < List.arraySize)
                    menu.AddItem(s_DuplicateEntry, false, () => pendingDuplicate = list.index);
                else
                    menu.AddDisabledItem(s_DuplicateEntry);

                menu.DropDown(buttonRect);
            }

            //MWC — Right-click on a row: same Duplicate as the "+" dropdown, plus Delete
            private void RowContextMenu(Rect rowRect, int index)
            {
                var e = Event.current;

                if (e.type != EventType.ContextClick || !rowRect.Contains(e.mousePosition)) return;

                var menu = new GenericMenu();
                menu.AddItem(s_DuplicateEntry, false, () => pendingDuplicate = index);
                menu.AddSeparator(string.Empty);
                menu.AddItem(s_DeleteEntry, false, () => pendingDelete = index);
                menu.ShowAsContext();

                e.Use();
            }

            //MWC — Applies the queued Add/Duplicate/Delete. Duplicate uses InsertArrayElementAtIndex, which clones
            //the element at that index, so every value (IDs, Offset, Item Type Override / Holster Anim, Slot) carries
            //over. The copy is a duplicated ID pair on purpose: Validate() tints it yellow until an ID is changed.
            private void ApplyPendingChanges()
            {
                if (pendingAdd)
                {
                    pendingAdd = false;
                    OnAdd(Reo);
                }

                if (pendingDuplicate >= 0)
                {
                    int index = pendingDuplicate;
                    pendingDuplicate = -1;

                    if (index < List.arraySize) //The list may have shrunk between the menu click and this pass
                    {
                        List.InsertArrayElementAtIndex(index);
                        List.GetArrayElementAtIndex(index + 1).isExpanded = true;
                        Reo.index = index + 1;
                    }
                }

                if (pendingDelete >= 0)
                {
                    int index = pendingDelete;
                    pendingDelete = -1;

                    if (index < List.arraySize) List.DeleteArrayElementAtIndex(index);
                }
            }

            //MWC — clears a copied value on a newly added element (the array-size increase duplicates the previous one)
            private static void ResetProp(SerializedProperty element, string prop)
            {
                var p = element.FindPropertyRelative(prop);
                if (p == null) return;

                //MWC — the Slot priority list resets to a single slot 0, never to an empty list
                if (p.isArray && p.propertyType != SerializedPropertyType.String)
                {
                    p.arraySize = 1;
                    p.GetArrayElementAtIndex(0).intValue = 0;
                }
                else if (p.propertyType == SerializedPropertyType.ObjectReference) p.objectReferenceValue = null;
                else if (p.propertyType == SerializedPropertyType.Integer) p.intValue = 0;
            }

            /// <summary> Draws the list inside a helpbox plus its validation messages </summary>
            public void Draw(string title)
            {
                ApplyPendingChanges(); //MWC — resize the array here, before Validate() and the element loop read it
                Validate();

                using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    //MWC — section title (two tables share the inspector now). Empty when the caller already titled it
                    if (!string.IsNullOrEmpty(title)) EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

                    Reo.DoLayoutList();
                }

                if (MissingIDs > 0)
                    EditorGUILayout.HelpBox($"{MissingIDs} entr{(MissingIDs == 1 ? "y has" : "ies have")} an empty Super Item or {EmptyValueName}. They will never be found.", MessageType.Warning);

                if (Duplicated.Count > 0)
                    EditorGUILayout.HelpBox($"Duplicated (Super Item + {EmptyValueName}) entries found. Only the first one will be used.", MessageType.Warning);
            }
        }

        private OffsetTable equipTable;
        private OffsetTable holsterTable;
        private ReorderableList equivalentsList;
        private ReorderableList actionEquivalentsList;

        private static readonly GUIContent s_EquivalentFrom = new("Item Equip Point", "Equip Point the Items were authored for (e.g. Left Hand)");
        private static readonly GUIContent s_EquivalentTo = new("Can be used in", "Equip Point of this character that can hold those Items instead (e.g. Head)");
        private static readonly GUIContent s_ActionFrom = new("Input Action", "Action ID sent by the character's input (e.g. Attack)");
        private static readonly GUIContent s_ActionTo = new("Plays", "Action ID played instead on Items that do not have the Input Action (e.g. Attack Light)");

        /// <summary> Equip Point Equivalents list: [Item Equip Point] → [Can be used in], one row per pair </summary>
        internal static ReorderableList CreateEquivalentsList(SerializedObject offsets) => CreatePairList(offsets,
            nameof(SuperItemOffsets.EquipPointEquivalents), nameof(SEquipPointEquivalent.EquipPoint), nameof(SEquipPointEquivalent.Equivalent),
            s_EquivalentFrom, s_EquivalentTo);

        /// <summary> Action Equivalents list: [Input Action] → [Plays], one row per pair </summary>
        internal static ReorderableList CreateActionEquivalentsList(SerializedObject offsets) => CreatePairList(offsets,
            nameof(SuperItemOffsets.ActionEquivalents), nameof(SActionEquivalent.Action), nameof(SActionEquivalent.Equivalent),
            s_ActionFrom, s_ActionTo);

        /// <summary> One-line ID → ID pair list shared by the Equip Point and Action Equivalents </summary>
        private static ReorderableList CreatePairList(SerializedObject offsets, string listProp, string fromProp, string toProp,
            GUIContent fromHeader, GUIContent toHeader)
        {
            var list = offsets.FindProperty(listProp);

            return new ReorderableList(offsets, list, true, true, true, true)
            {
                drawHeaderCallback = rect =>
                {
                    rect.xMin += HEADER_OFFSET;
                    GetEquivalentColumns(rect, out var x, out var half);
                    float line = EditorGUIUtility.singleLineHeight;
                    EditorGUI.LabelField(new Rect(x, rect.y, half, line), fromHeader, EditorStyles.miniLabel);
                    EditorGUI.LabelField(new Rect(x + half + PAD + ID_MENU_W, rect.y, half, line), toHeader, EditorStyles.miniLabel);
                },

                drawElementCallback = (rect, index, isActive, isFocused) =>
                {
                    int indent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = 0; //Absolute rects, same as the offset tables

                    var element = list.GetArrayElementAtIndex(index);
                    var from = element.FindPropertyRelative(fromProp);
                    var to = element.FindPropertyRelative(toProp);

                    float line = EditorGUIUtility.singleLineHeight;
                    rect.y += 2;
                    GetEquivalentColumns(rect, out var x, out var half);

                    var defaultColor = GUI.color;
                    GUI.color = from.objectReferenceValue == null ? Color.red : defaultColor;
                    EditorGUI.PropertyField(new Rect(x, rect.y, half, line), from, GUIContent.none);

                    GUI.color = to.objectReferenceValue == null || to.objectReferenceValue == from.objectReferenceValue ? Color.red : defaultColor;
                    EditorGUI.PropertyField(new Rect(x + half + PAD + ID_MENU_W, rect.y, half, line), to, GUIContent.none);
                    GUI.color = defaultColor;

                    EditorGUI.indentLevel = indent;
                },

                elementHeight = EditorGUIUtility.singleLineHeight + PAD + 2,

                //The array-size increase copies the previous element: start the new row empty
                onAddCallback = reo =>
                {
                    int index = list.arraySize;
                    list.arraySize++;
                    var element = list.GetArrayElementAtIndex(index);
                    element.FindPropertyRelative(fromProp).objectReferenceValue = null;
                    element.FindPropertyRelative(toProp).objectReferenceValue = null;
                    reo.index = index;
                },
            };
        }

        /// <summary> Column math for the Equivalents list: two ID fields after the drag handle </summary>
        private static void GetEquivalentColumns(Rect rect, out float x, out float half)
        {
            x = rect.x + HANDLE_W + LEAD_MENU_W;
            half = (rect.xMax - x - PAD - ID_MENU_W) * 0.5f;
        }

        //MWC — Table factories, shared with the Super Item Manager inspector so both draw the exact same list
        internal static OffsetTable CreateEquipTable(SerializedObject offsets) => new(
            offsets.FindProperty(nameof(SuperItemOffsets.Offsets)),
            s_PointHeader, "Equip Point",
            new[] { s_TypeOverride },
            new[] { nameof(SItemOffset.ItemTypeOverride) });

        //MWC — Holster table: Offset + Holster Anim per (Item + Holster), with the Slot at the end of the element row
        internal static OffsetTable CreateHolsterTable(SerializedObject offsets) => new(
            offsets.FindProperty(nameof(SuperItemOffsets.HolsterOffsets)),
            s_HolsterHeader, "Holster",
            new[] { s_HolsterAnim },
            new[] { nameof(SItemHolsterOffset.HolsterAnim) },
            nameof(SItemHolsterOffset.HolsterSlots), s_HolsterSlot); //MWC — Slot priority list

        private void OnEnable()
        {
            equipTable = CreateEquipTable(serializedObject);
            holsterTable = CreateHolsterTable(serializedObject);
            equivalentsList = CreateEquivalentsList(serializedObject);
            actionEquivalentsList = CreateActionEquivalentsList(serializedObject);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("All the offsets for the Super Items of a character.\n" +
                "Equip Points (Item + Equip Point → Offset, Anim Item Type) and Holsters (Item + Holster → Offset, Anim, Slot)");

            //Vector3 fields need wide mode to stay on a single line inside the list rects
            bool wideMode = EditorGUIUtility.wideMode;
            EditorGUIUtility.wideMode = true;

            equipTable.Draw("Equip Point Offsets");
            holsterTable.Draw("Holster Offsets");

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(new GUIContent("Equip Point Equivalents",
                    "Equip Points this character can use in place of the ones the Items were authored for.\n" +
                    "E.g. [Left Hand → Head] and [Right Hand → Head] let a Wolf equip hand Items on its Head"), EditorStyles.boldLabel);
                equivalentsList.DoLayoutList();
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(new GUIContent("Action Equivalents",
                    "Action IDs this character's inputs can play in place of the ones the Items were authored with.\n" +
                    "E.g. [Attack → Attack Light] lets a Wolf with only an Attack input use the Sword's Attack Light"), EditorStyles.boldLabel);
                actionEquivalentsList.DoLayoutList();
            }

            EditorGUIUtility.wideMode = wideMode;

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
