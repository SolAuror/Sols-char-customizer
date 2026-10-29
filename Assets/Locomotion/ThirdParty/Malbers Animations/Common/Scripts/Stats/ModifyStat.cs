// Last Change: 9/17/2026 6:05pm
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.CompilerServices;


#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal; // MWC — ReorderableList for the [Sources] expression
#endif

namespace MalbersAnimations
{
    [AddComponentMenu("Malbers/Stats/Stats Modifier")]

    public class ModifyStat : MonoBehaviour
    {
        public static readonly string[] Tooltips = {
          "[None] Skips the stat modification",
          "Adds to the stat Value",
          "Sets the stat value",
          "Subtracts from the stat value",
          "Modifies the Stat maximum Value (Adds or Remove)",
          "Set the Stat maximum Value",
          "Enables the Degeneration and sets the Degen Rate Value. If the value is 0, the rate Value wont be changed",
          "Stops the Degeneration",
          "Enables the Regeneration and sets the Regen Rate Value.  If the value is 0, the rate Value wont be changed",
          "Stops the Regeneration",
          "Reset the Stat to the Default Min or Max Value",
          "Reduce the Value of the Stat by a percent",
          "Increase the Value of the Stat by a percent",
          "Sets the multiplier value of the stat",
          "Reset the Stat to the maximum Value",
          "Reset the Stat to the minimun Value",
          "Enable/Disable the Stat",
          "Set Immune",
          "Starts the Regeneration",
          "Restore the Regeneration to its default",
          "Restore the Degeneration to its default",
          "Restore the Value to its default",
          "Restore the Max Value to its default",
          "Restore the Min to its default",
          "Restore the value to its default",
          "Restore the Multiplier to its default",
          "Adds or Remove a value to the Multiplier",
    };


        public Stats stats;

        public List<StatModifier> modifiers = new();

        public virtual void SetStats(GameObject go) => stats = go.FindComponent<Stats>();
        public virtual void SetStats(Component go) => SetStats(go.gameObject);


        /// <summary> Apply All Modifiers to the Stats </summary>
        public virtual void Modify()
        {
            foreach (var statmod in modifiers)
                statmod.ModifyStat(stats);
        }

        public virtual void Modify(GameObject target)
        {
            SetStats(target);
            Modify();
        }
        public virtual void Modify(Component target)
        {
            Modify(target.gameObject);
        }


        /// <summary> Apply a Modifiers to the Stats using its Index</summary>
        public virtual void Modify(int index)
        {
            if (modifiers != null && index < modifiers.Count && modifiers[index].Valid)
                modifiers[index].ModifyStat(stats);
        }
    }



    /// <summary> Where a <see cref="StatModifier"/> takes its value from </summary>
    public enum StatValueFrom
    {
        /// <summary>Random value between the Modifier Min and Max values</summary>
        [InspectorName("Use Value")] Value = 0,
        /// <summary>Value built from the <see cref="StatModifier.sources"/> expression (Stats + static values)</summary>
        [InspectorName("Use Sources")] Stat = 1,
    }

    /// <summary> Which number of the Source Stat feeds the <see cref="StatModifier"/> </summary>
    public enum StatValuePart
    {
        /// <summary>Current runtime value of the Source Stat</summary>
        [InspectorName("Value")] Value = 0,
        /// <summary>Minimum value of the Source Stat</summary>
        [InspectorName("Min Value")] Min = 1,
        /// <summary>Maximum value of the Source Stat</summary>
        [InspectorName("Max Value")] Max = 2,
        /// <summary>Random value between the Source Stat Min and Max values</summary>
        [InspectorName("Random [Min-Max]")] Random = 3,
        /// <summary>Normalized (Value/MaxValue) value of the Source Stat</summary>
        [InspectorName("Normalized")] Normalized = 4,
    }

    /// <summary> How a <see cref="StatSource"/> term combines with the running result of the expression.
    /// The list is evaluated TOP TO BOTTOM with NO operator precedence, so the row order IS the math order </summary>
    public enum StatSourceOperator
    {
        /// <summary>result + value</summary>
        [InspectorName("+   Add")] Add = 0,
        /// <summary>result - value</summary>
        [InspectorName("-   Substract")] Substract = 1,
        /// <summary>result * value</summary>
        [InspectorName("x   Multiply")] Multiply = 2,
        /// <summary>result / value (ignored when the value is 0)</summary>
        [InspectorName("/   Divide")] Divide = 3,
        /// <summary>result + (result * value%)</summary>
        [InspectorName("+%  Add Percent")] AddPercent = 4,
        /// <summary>result - (result * value%)</summary>
        [InspectorName("-%  Substract Percent")] SubstractPercent = 5,
        /// <summary>Floor: the result can never go below the value</summary>
        [InspectorName("«   Clamp Min")] ClampMin = 6,
        /// <summary>Ceiling: the result can never go above the value</summary>
        [InspectorName("»   Clamp Max")] ClampMax = 7,
    }

    /// <summary> One term of a <see cref="StatModifier"/> Source expression. It is either a Stat read from
    /// the Source Stats (E.g. the weapon's [Attack]) or a static Min-Max roll, combined with the terms above it
    /// through its <see cref="op"/>. E.g. [Attack] + [20-30] x [Charge] - [Durability] </summary>
    [System.Serializable]
    public struct StatSource
    {
        /// <summary>Short symbols, indexed by <see cref="StatSourceOperator"/>. Used by the Inspector and by <see cref="Description"/></summary>
        public static readonly string[] Symbols = { "+", "-", "x", "/", "+%", "-%", "^", "v" };

        [Tooltip("How this term combines with the result of the terms ABOVE it.\nThe list is evaluated top to bottom with no operator precedence")]
        public StatSourceOperator op;

        [Tooltip("Where this term takes its number from:\n" +
            "• [Use Value] Random value between the Min and Max values\n" +
            "• [Use Stat] Value read from a Stat on the Source Stats")]
        public StatValueFrom from;

        [Tooltip("Stat this term is read from. It is read from the Source Stats (E.g. the weapon's [Attack] Stat)")]
        public StatID Stat;

        [Tooltip("Which number of the Stat is used")]
        public StatValuePart part;

        public FloatReference MinValue;
        public FloatReference MaxValue;

        public StatSource(StatID stat, StatValuePart part = StatValuePart.Value, StatSourceOperator op = StatSourceOperator.Add)
        {
            this.op = op;
            this.part = part;
            Stat = stat;
            from = StatValueFrom.Stat;
            MinValue = new(0);
            MaxValue = new(0);
        }

        public StatSource(float min, float max, StatSourceOperator op = StatSourceOperator.Add)
        {
            this.op = op;
            part = StatValuePart.Value;
            Stat = null;
            from = StatValueFrom.Value;
            MinValue = new(min);
            MaxValue = new(max);
        }

        /// <summary>The term reads a Stat instead of a static Min-Max range</summary>
        public readonly bool UsesStat => from == StatValueFrom.Stat;

        /// <summary> Raw number of this term, BEFORE its operator is applied </summary>
        public readonly float GetValue(Stats sourceStats)
        {
            //MWC — MinValue/MaxValue are classes: a term built in code with default(StatSource) has them null
            if (from == StatValueFrom.Value)
                return MinValue == null || MaxValue == null ? 0f : UnityEngine.Random.Range(MinValue.Value, MaxValue.Value);

            if (sourceStats == null || Stat == null) return 0f;

            var s = sourceStats.Stat_Get(Stat);
            if (s == null) return 0f;

            return part switch
            {
                StatValuePart.Min => s.MinValue,
                StatValuePart.Max => s.MaxValue,
                StatValuePart.Random => UnityEngine.Random.Range(s.MinValue, s.MaxValue),
                StatValuePart.Normalized => s.NormalizedValue,
                _ => s.Value,
            };
        }

        /// <summary> Applies this term to the running <paramref name="result"/> of the expression.
        /// The FIRST term seeds the result (only [Substract] is honored there, as a negation) </summary>
        public readonly float Apply(float result, Stats sourceStats, bool isFirst)
        {
            var value = GetValue(sourceStats);

            if (isFirst) return op == StatSourceOperator.Substract ? -value : value;

            return op switch
            {
                StatSourceOperator.Add => result + value,
                StatSourceOperator.Substract => result - value,
                StatSourceOperator.Multiply => result * value,
                StatSourceOperator.Divide => value != 0 ? result / value : result, //Never divide by zero
                StatSourceOperator.AddPercent => result + (result * value * 0.01f),
                StatSourceOperator.SubstractPercent => result - (result * value * 0.01f),
                StatSourceOperator.ClampMin => Mathf.Max(result, value),
                StatSourceOperator.ClampMax => Mathf.Min(result, value),
                _ => result,
            };
        }

        /// <summary>Human readable term. E.g. "[Attack]" or "[20-30]"</summary>
        public readonly string Description =>
            UsesStat
            ? $"{(Stat != null ? Stat.name : "None")}{(part != StatValuePart.Value ? $".{part}" : string.Empty)}"
            : (MinValue == null || MaxValue == null ? "0"
              //The brackets stay on a range: without them [20-30] would read as a substraction
              : MinValue.Value == MaxValue.Value ? $"{MinValue.Value}" : $"[{MinValue.Value}-{MaxValue.Value}]");
    }

    /// <summary> Builds a Source expression as readable math. The terms are combined LEFT TO RIGHT, so
    /// parentheses are printed wherever normal operator precedence would read differently from the row order.
    /// E.g. the rows [Attack] [+ 30] [x Charge] print as  ([Attack] + 30) x [Charge] </summary>
    public struct SourceExpressionBuilder
    {
        private string text;
        private int rank; //0 = one term (or already grouped), 1 = additive [+ -], 2 = multiplicative [x /]

        public void Append(StatSourceOperator op, string term, bool isFirst)
        {
            var symbol = StatSource.Symbols[Mathf.Clamp((int)op, 0, StatSource.Symbols.Length - 1)];

            if (isFirst)
            {
                //The first term has nothing above it: it seeds the expression, and [-] negates it
                text = op == StatSourceOperator.Substract ? $"-{term}" : term;
                rank = 0;
                return;
            }

            switch (op)
            {
                case StatSourceOperator.Add:
                case StatSourceOperator.Substract:
                    rank = 1; //[+ -] are already the lowest precedence and left associative: nothing to group
                    break;

                case StatSourceOperator.Multiply:
                case StatSourceOperator.Divide:
                    if (rank == 1) text = $"({text})"; //The WHOLE sum is multiplied. This is the parenthesis that matters
                    rank = 2;
                    break;

                default: //Percent and the clamps are not standard notation: always group what they apply to
                    if (rank > 0) text = $"({text})";
                    rank = 0;
                    break;
            }

            text = $"{text} {symbol} {term}";
        }

        public readonly override string ToString() => text ?? string.Empty;
    }

    [System.Serializable]
    public struct StatModifier
    {
        public StatID ID;
        public StatOption modify;

        [Tooltip("Where the value of the modifier comes from")]
        public StatValueFrom from; // MWC — replaces the old [useBase] toggle

        public FloatReference MinValue;
        public FloatReference MaxValue;
        public BoolReference enable;

        //MWC — LEGACY single Source Stat. Superseded by [sources]. Kept so already authored assets keep working:
        //when [sources] is empty these two still feed the value, and the Inspector migrates them into the list on sight
        [Tooltip("LEGACY Source Stat. Migrated into the [Sources] list")]
        public StatID Base;

        [Tooltip("LEGACY. Which number of the legacy Source Stat is used")]
        public StatValuePart part;

        //MWC — explicit Stats component the [Sources] terms are read from. Leave it empty to use the Stats the
        //caller provides (the Item's own Stats for a Super Item Processor, the Owner Stats for a Damager…)
        [Tooltip("Stats component the [Sources] terms read their Stats from.\n" +
            "Leave it EMPTY to use the Stats the caller provides (the Item's own Stats, the Damager Owner's Stats…).\n" +
            "Set it to read the values from a specific character or item instead")]
        public Stats SourceStats;

        // MWC — the value is built from this expression when [from] is [Use Sources].
        // Evaluated TOP TO BOTTOM with no operator precedence. E.g. [Attack] + [20-30] x [Charge] - [Durability]
        [Tooltip("Terms the value is built from. Evaluated top to bottom with NO operator precedence:\n" +
            "E.g. [Attack] + [20-30] x [Charge] - [Durability]  =  (((Attack + 20-30) x Charge) - Durability)")]
        public List<StatSource> sources;

        /// <summary>The Source expression has at least one term</summary>
        public readonly bool HasSources => sources != null && sources.Count > 0;

        /// <summary> First Stat used by the Source expression (falls back to the legacy <see cref="Base"/>).
        /// Used where a single representative Stat is needed (E.g. the range a Charge interpolates across) </summary>
        public readonly StatID SourceStat
        {
            get
            {
                if (sources != null)
                    foreach (var s in sources)
                        if (s.UsesStat && s.Stat != null) return s.Stat;

                return Base;
            }
        }

        /// <summary> Edit-time check: every Stat term of the Source expression exists on <paramref name="stats"/>.
        /// Returns the problems found (one per line) or an empty string. Nothing is checked when <paramref name="stats"/> is null </summary>
        public readonly string ValidateSourceStats(Stats stats)
        {
            if (from != StatValueFrom.Stat || stats == null) return string.Empty;

            var problems = new System.Text.StringBuilder();

            if (HasSources)
            {
                foreach (var term in sources)
                {
                    if (!term.UsesStat) continue;

                    if (term.Stat == null) problems.AppendLine("A [Source] term has no Stat ID assigned.");
                    else if (stats.Stat_Get(term.Stat) == null)
                        problems.AppendLine($"The [Stats] on [{stats.name}] does not contain the [{term.Stat.name}] Stat.");
                }
            }
            else if (SourceStat != null && stats.Stat_Get(SourceStat) == null)
                problems.AppendLine($"The [Stats] on [{stats.name}] does not contain the [{SourceStat.name}] Stat.");

            return problems.ToString();
        }

        /// <summary> Human readable Source expression. E.g. "[Attack] + [20-30] x [Charge]" </summary>
        public readonly string SourceExpression
        {
            get
            {
                if (!HasSources) return Base != null ? Base.name : "<None>";

                var builder = new SourceExpressionBuilder(); // MWC — parenthesized, left to right

                for (int i = 0; i < sources.Count; i++)
                    builder.Append(sources[i].op, sources[i].Description, i == 0);

                return builder.ToString();
            }
        }

        /// <summary> Value of the modifier. When the value comes from a Source Stat use
        /// <see cref="Resolve(Stats)"/> first, this getter only knows about the Min-Max range </summary>
        public float Value
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: force hint-optimize this method
            readonly get => UnityEngine.Random.Range(MinValue.Value, MaxValue.Value); //Get the value from a random range from min to max

            [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: force hint-optimize this method
            set
            {
                MinValue = new(value);
                MaxValue = new(value);
                from = StatValueFrom.Value; // MWC — an explicit value always wins over the Source Stat
            }
        }
        public StatModifier(StatModifier mod)
        {
            ID = mod.ID;
            modify = mod.modify;
            MinValue = new(mod.MinValue.Value);
            MaxValue = new(mod.MaxValue.Value);
            enable = new(true);
            Base = mod.Base;
            from = mod.from;
            part = mod.part;
            sources = mod.sources != null ? new List<StatSource>(mod.sources) : null; // MWC — copy, never alias the list
            SourceStats = mod.SourceStats;
        }

        public StatModifier(StatID id, float value)
        {
            ID = id;
            modify = StatOption.SubstractValue;
            MinValue = new(value);
            MaxValue = new(value);
            enable = new(true);
            Base = null;
            from = StatValueFrom.Value;
            part = StatValuePart.Value;
            sources = null;
            SourceStats = null;
        }

        /// <summary>There's No ID stat</summary>
        public readonly bool IsNull => ID == null;
        /// <summary>There is an ID stat</summary>
        public readonly bool Valid => ID != null;

        /// <summary> Result of the [Sources] expression, evaluated top to bottom with no operator precedence.
        /// Stats are read from <paramref name="sourceStats"/>. Returns 0 when there is nothing to read from </summary>
        public readonly float GetSourceValue(Stats sourceStats)
        {
            if (SourceStats != null) sourceStats = SourceStats; //MWC — an explicit Stats component always wins

            if (HasSources)
            {
                float result = 0f;

                for (int i = 0; i < sources.Count; i++)
                    result = sources[i].Apply(result, sourceStats, i == 0);

                return result;
            }

            //MWC — LEGACY path: assets authored before the [Sources] list still hold a single [Base] Stat
            if (sourceStats == null || Base == null) return 0f;

            var s = sourceStats.Stat_Get(Base);
            if (s == null) return 0f;

            return part switch
            {
                StatValuePart.Min => s.MinValue,
                StatValuePart.Max => s.MaxValue,
                StatValuePart.Random => UnityEngine.Random.Range(s.MinValue, s.MaxValue),
                StatValuePart.Normalized => s.NormalizedValue,
                _ => s.Value,
            };
        }

        public readonly StatModifier Resolve() { return Resolve(SourceStats); }

        /// <summary> Returns a copy of this modifier with its value already baked into Min-Max.
        /// Call it where the Source Stats are known (E.g. the Damager Owner) so the modifier can travel
        /// through the damage pipeline as a plain value. It is a no-op when the value comes from Min-Max </summary>
        public readonly StatModifier Resolve(Stats sourceStats)
        {
            if (from == StatValueFrom.Value) return this;

            var resolved = this;
            resolved.Value = GetSourceValue(sourceStats); //The setter also pins [from] back to MinMax
            return resolved;
        }

        /// <summary>Modify the Stats on an animal </summary>
        /// <param name="targetStats">Stats to modify</param>
        /// <param name="sourceStats">Stats the Source Stat is read from, when the value comes from a Stat ID</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool ModifyStat(Stats targetStats, Stats sourceStats = null)
        {
            if (!targetStats || !targetStats.enabled || IsNull) return false;

            return Resolve(sourceStats).ModifyStat(targetStats.Stat_Get(ID), 0f);
        }

        /// <summary>Modify the Stats on an animal applying a random value from Min to Max </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool ModifyStat(IMStat targetStats, float baseStatValue)
        {
            if (targetStats == null || !targetStats.Active) return false; //Do nothing if the stat is null, inmune or disabled)

            if (modify == StatOption.Inmune || modify == StatOption.Enable)
            {
                targetStats.Modify(enable.Value ? 1 : 0, modify);
            }
            else
            {
                if (targetStats.IsImmune && (modify == StatOption.SubstractValue || modify == StatOption.SetValue || modify == StatOption.AddValue)) return false;
                targetStats.Modify(Random.Range(MinValue, MaxValue) + baseStatValue, modify);
            }
            return true;
        }

        /// <summary>Modify the Stats on an animal applying a value from Min to Max get by the Normalized parameter</summary>
        public readonly bool ModifyStat(IMStat targetStats, float baseStatValue, float Normalized)
        {
            if (targetStats == null) return false;

            if (modify == StatOption.Inmune || modify == StatOption.Enable)
            {
                targetStats.Modify(enable.Value ? 1 : 0, modify);
                return true;
            }
            else
            {
                targetStats.Modify(Mathf.Lerp(MinValue, MaxValue, Normalized) + baseStatValue, modify);
                return true;
            }
        }
        /// <summary>Gets a value from the Modifier (Normalized value from Min to Max)</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: force hint-optimize this method
        public readonly float GetValue(float Normalized) => Mathf.Lerp(MinValue, MaxValue, Normalized);

        /// <summary>Gets a value from the Modifier (Random from Min to Max)</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] //CustomPatch: force hint-optimize this method
        public readonly float GetValue() => UnityEngine.Random.Range(MinValue, MaxValue);

        /// <summary> Gets the value of the Modifier honoring the [Value From] option </summary>
        public readonly float GetValue(Stats sourceStats) =>
            from == StatValueFrom.Stat ? GetSourceValue(sourceStats) : UnityEngine.Random.Range(MinValue, MaxValue);
    }


    //--------------------EDITOR----------------
#if UNITY_EDITOR

    [CustomPropertyDrawer(typeof(StatModifier))]
    public class StatModifierDrawer : PropertyDrawer
    {
        // MWC — cached tooltips so the GUIContents are not allocated on every repaint
        private static readonly GUIContent s_Stat = new("Target", "Stat ID to modify on the Target Stats"); // MWC — labeled [Target] to pair with [Source]
        private static readonly GUIContent s_From = new(string.Empty,
            "Where the value of the modifier comes from:\n" +
            "• [Use Value] Random value between the Min and Max values\n" +
            "• [Use Source] Value read from a Source Stat on the Owner/Source Stats");
        private static readonly GUIContent s_Source = new("Source",
            "Source Stat the value is taken from. It is read from the Owner/Source Stats.\n" +
            "E.g. the damage of a sword can be taken from the Owner's [Attack] Stat");
        private static readonly GUIContent s_Min = new("Min", "Minimun Value");
        private static readonly GUIContent s_Max = new("Max", "Maximum Value");
        private static readonly GUIContent s_Value = new("Value");

        //MWC — widest entry the [Value From] popup has to show. It sets the max width of both enum popups
        private static readonly GUIContent s_UseValue = new("Use Value");
        private static readonly GUIContent s_UseSource = new("Use Sources");

        // MWC — [Sources] list UI
        private static readonly GUIContent s_SourceHeader = new("Source",
            "The value is built from these terms, evaluated TOP TO BOTTOM with NO operator precedence.\n" +
            "E.g. Attack + [20-30] x Charge - Durability  =  ((Attack + [20-30]) x Charge) - Durability\n\n" +
            "The [Stats] slot next to this label is the component the terms read their Stats from.\n" +
            "Leave it empty to use the Stats the caller provides (the Item's own Stats, the Damager Owner's Stats…)");

        //MWC — compact operator popup. Same order as [StatSourceOperator]
        private static readonly GUIContent[] s_Ops =
        {
            new("+",  "Add:  result + value"),
            new("-",  "Substract:  result - value"),
            new("x",  "Multiply:  result x value"),
            new("/",  "Divide:  result / value  (skipped when the value is 0)"),
            new("+%", "Add Percent:  result + (result x value%)"),
            new("-%", "Substract Percent:  result - (result x value%)"),
            new("^",  "Clamp Min:  the result can never go below the value"),
            new("v",  "Clamp Max:  the result can never go above the value"),
        };

        //MWC — the first term has nothing above it to combine with: it only seeds the result, or negates it.
        //Both entries are drawn with a real symbol on purpose: a blank one makes a negated first term invisible
        private static readonly GUIContent[] s_FirstOps =
        {
            new("+", "First term: the expression starts at this value"),
            new("-", "First term, NEGATED: the expression starts at -value"),
        };

        private const float RowHeight = 20f; // MWC — 18 line + 2 padding

        //MWC — one list per drawn property. Nested drawers are shared instances, so the state cannot live in a field
        private readonly Dictionary<string, ReorderableList> m_Lists = new();

        private static float s_LabelWidth;
        private static float s_EnumWidth;

        /// <summary> Max width of the [Value From] / [Source Part] popups: only as wide as
        /// "Use Value" / "Use Source" need, so they stop eating the row </summary>
        private static float EnumWidth
        {
            get
            {
                if (s_EnumWidth == 0)
                {
                    s_EnumWidth = Mathf.Max(EditorStyles.popup.CalcSize(s_UseValue).x,
                                            EditorStyles.popup.CalcSize(s_UseSource).x) + 6f;
                }
                return s_EnumWidth;
            }
        }

        /// <summary> One label width shared by every row so [Target] and [Source] (and Min/Value) align.
        /// It fits the widest label plus the gutter the ID drawer uses for its [⋮] button </summary>
        private static float LabelWidth
        {
            get
            {
                if (s_LabelWidth == 0)
                {
                    s_LabelWidth = Mathf.Max(EditorStyles.label.CalcSize(s_Stat).x,
                                             EditorStyles.label.CalcSize(s_Source).x) + 22f;
                }
                return s_LabelWidth;
            }
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var height = EditorGUIUtility.singleLineHeight;

            GUI.Box(position, GUIContent.none);

            position.y += 2;

            //MWC — absorb the ambient indent into the rect, then draw everything at indent 0.
            //PrefixLabel only moves the label text (not the field), so at indent > 0 the [⋮] gutter of the ID
            //fields creeps over the label. Indenting the rect instead keeps the row identical at any nesting depth
            var indent = EditorGUI.indentLevel;
            position = EditorGUI.IndentedRect(position);
            EditorGUI.indentLevel = 0;

            var ID = property.FindPropertyRelative("ID");
            var MaxValue = property.FindPropertyRelative("MaxValue");
            var MinValue = property.FindPropertyRelative("MinValue");
            var modify = property.FindPropertyRelative("modify");
            var enable = property.FindPropertyRelative("enable");
            var Base = property.FindPropertyRelative("Base");
            var from = property.FindPropertyRelative("from");
            var part = property.FindPropertyRelative("part");
            var sources = property.FindPropertyRelative("sources");

            var LabelWith = LabelWidth; // MWC — measured once, shared by both lines so the columns align
            const float Gap = 5f;

            var drawValue = !CheckEnum(modify.intValue); //Does this Stat Option need a value at all?
            var isToggleOption = modify.intValue == (int)StatOption.Enable || modify.intValue == (int)StatOption.Inmune;
            var showFrom = drawValue && !isToggleOption; // MWC — [Value From] only makes sense for numeric options

            //---------------- Line 1:  [Stat ID]  ->  [Stat Option]  ->  [Value From] ----------------
            //MWC — the [Value From] popup is capped to its text width and pinned to the right edge. Everything
            //else shares what is left, so the popup never eats the row (and still shrinks on narrow inspectors)
            var fromWidth = showFrom ? Mathf.Min(EnumWidth, position.width * 0.28f) : 0f;
            var mainWidth = position.width - (showFrom ? fromWidth + Gap : 0f); // shared by [Target] + [Stat Option]

            var idWidth = mainWidth * (showFrom ? 0.55f : 0.5f);
            var line = new Rect(position) { width = idWidth - Gap, height = height };

            EditorGUIUtility.labelWidth = LabelWith;
            EditorGUI.PropertyField(line, ID, s_Stat);
            EditorGUIUtility.labelWidth = 0;

            line.x = position.x + idWidth;
            line.width = mainWidth - idWidth - Gap;

            EditorGUI.PropertyField(line, modify, new GUIContent(string.Empty, ModifyStat.Tooltips[modify.intValue]));
            EditorGUI.LabelField(line, new GUIContent("             ", ModifyStat.Tooltips[modify.intValue]));

            if (showFrom) // MWC — the [Value From] selector, right aligned
            {
                line.x = position.xMax - fromWidth;
                line.width = fromWidth;
                EditorGUI.PropertyField(line, from, s_From);
            }

            //---------------- Line 2:  the value itself ----------------
            var line2 = new Rect(position) { y = position.y + height + 2, height = height };

            if (!drawValue)
            {
                //Don't Draw anything
            }
            else if (isToggleOption)
            {
                EditorGUIUtility.labelWidth = LabelWith;
                EditorGUI.PropertyField(line2, enable, s_Value);
                EditorGUIUtility.labelWidth = 0;
            }
            else if (from.intValue == (int)StatValueFrom.Stat) // MWC — the [Sources] expression
            {
                Migrate(property, Base, part, sources); //MWC — old single [Base] Stat becomes the first term

                var list = GetList(property, sources);
                list.DoList(new Rect(line2) { height = list.GetHeight() });
            }
            else
            {
                EditorGUIUtility.labelWidth = LabelWith;

                var half = position.width * 0.5f;

                EditorGUI.PropertyField(new Rect(line2) { width = half - Gap }, MinValue, s_Min);

                line2.x = position.x + half;
                line2.width = position.xMax - line2.x;
                EditorGUI.PropertyField(line2, MaxValue, s_Max);

                EditorGUIUtility.labelWidth = 0;
            }

            property.serializedObject.ApplyModifiedProperties();

            EditorGUI.EndProperty();
            EditorGUI.indentLevel = indent; // MWC — restore
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var modify = property.FindPropertyRelative("modify");

            if (CheckEnum(modify.intValue)) return 18 + 6;

            // MWC — the [Sources] expression is a list, so the height grows with it
            var from = property.FindPropertyRelative("from");
            var isToggleOption = modify.intValue == (int)StatOption.Enable || modify.intValue == (int)StatOption.Inmune;

            if (!isToggleOption && from.intValue == (int)StatValueFrom.Stat)
            {
                var sources = property.FindPropertyRelative("sources");

                //MWC — migrate here too (it runs before OnGUI), so the height is right on the very first frame
                Migrate(property, property.FindPropertyRelative("Base"), property.FindPropertyRelative("part"), sources);

                return 18 + 2 + GetList(property, sources).GetHeight() + 8;
            }

            return (18 * 2) + 6;
        }

        /// <summary> Assets authored before the [Sources] list hold a single [Base] Stat + [part].
        /// The first time one is inspected it becomes the first term of the expression and the legacy fields are cleared </summary>
        private static void Migrate(SerializedProperty property, SerializedProperty Base, SerializedProperty part, SerializedProperty sources)
        {
            if (sources.arraySize > 0 || Base.objectReferenceValue == null) return;

            sources.arraySize = 1;

            var element = sources.GetArrayElementAtIndex(0);
            element.FindPropertyRelative("op").enumValueIndex = (int)StatSourceOperator.Add;
            element.FindPropertyRelative("from").enumValueIndex = (int)StatValueFrom.Stat;
            element.FindPropertyRelative("Stat").objectReferenceValue = Base.objectReferenceValue;
            element.FindPropertyRelative("part").enumValueIndex = part.enumValueIndex;
            SetConstant(element, "MinValue", 0);
            SetConstant(element, "MaxValue", 0);

            Base.objectReferenceValue = null; //The list is the single source of truth from now on
            part.enumValueIndex = (int)StatValuePart.Value;

            property.serializedObject.ApplyModifiedProperties();
        }

        private static void SetConstant(SerializedProperty element, string field, float value)
        {
            var reference = element.FindPropertyRelative(field);
            if (reference == null) return;

            //MWC — a new array element is zero initialized, so [UseConstant] comes back FALSE and the
            //FloatReference would draw an empty [Float Var] slot instead of the number
            var useConstant = reference.FindPropertyRelative("UseConstant");
            if (useConstant != null) useConstant.boolValue = true;

            var constant = reference.FindPropertyRelative("ConstantValue");
            if (constant != null) constant.floatValue = value;

            var variable = reference.FindPropertyRelative("Variable");
            if (variable != null) variable.objectReferenceValue = null;
        }

        //MWC — put an unused reference back on [Use Local]. Only safe where the field is NOT drawn, otherwise it
        //would undo the user picking [Use Global] before they get the chance to assign the Var
        private static void RepairConstant(SerializedProperty element, string field)
        {
            var reference = element.FindPropertyRelative(field);
            var useConstant = reference?.FindPropertyRelative("UseConstant");
            var variable = reference?.FindPropertyRelative("Variable");

            if (useConstant == null || useConstant.boolValue) return;
            if (variable != null && variable.objectReferenceValue != null) return; //A Var IS assigned: leave it alone

            useConstant.boolValue = true;
        }

        //MWC — the term number as text: the constant, or the name of the [Float Var] when the reference uses one
        private static string Constant(SerializedProperty element, string field)
        {
            var reference = element.FindPropertyRelative(field);
            if (reference == null) return "0";

            var variable = reference.FindPropertyRelative("Variable");
            var useConstant = reference.FindPropertyRelative("UseConstant");

            if (useConstant != null && !useConstant.boolValue && variable != null && variable.objectReferenceValue != null)
                return variable.objectReferenceValue.name;

            return $"{reference.FindPropertyRelative("ConstantValue")?.floatValue ?? 0f}";
        }

        /// <summary> Cached list for the [Sources] expression. Keyed by property path + target, since a
        /// PropertyDrawer instance is shared by every element of a list </summary>
        private ReorderableList GetList(SerializedProperty property, SerializedProperty sources)
        {
            var target = property.serializedObject.targetObject;
#if UNITY_6000_4_OR_NEWER
            var key = $"{(target != null ? target.GetEntityId().ToString() : "0")}-{property.propertyPath}";
#else
            var key = $"{(target != null ? target.GetInstanceID() : 0)}-{property.propertyPath}";
#endif

            if (!m_Lists.TryGetValue(key, out var list) || list == null)
            {
                ReorderableList created = null;

                created = new ReorderableList(property.serializedObject, sources, true, true, true, true)
                {
                    elementHeight = RowHeight,
                    drawHeaderCallback = (rect) => DrawListHeader(rect, created),
                    drawElementCallback = (rect, index, active, focused) => DrawElement(rect, index, created),
                    onAddCallback = OnAddSource,
                };

                list = created;
                m_Lists[key] = list;
            }

            list.serializedProperty = sources; //Rebind: the SerializedObject is rebuilt on every Inspector refresh
            return list;
        }

        //MWC — [Source] label, the [Stats] the terms are read from, and the resulting expression on the right
        private static void DrawListHeader(Rect rect, ReorderableList list)
        {
            EditorGUI.LabelField(rect, s_SourceHeader, EditorStyles.boldLabel);

            var labelWidth = EditorStyles.boldLabel.CalcSize(s_SourceHeader).x + 6f;
            var free = new Rect(rect) { x = rect.x + labelWidth, width = rect.width - labelWidth };

            //MWC — the Stats component the terms read from. Empty = whatever the caller passes in
            var sourceStats = SourceStatsOf(list.serializedProperty);

            if (sourceStats != null)
            {
                var statsRect = new Rect(free) { width = free.width * 0.45f, height = EditorGUIUtility.singleLineHeight - 2 };
                statsRect.y += 1;

                EditorGUI.PropertyField(statsRect, sourceStats, GUIContent.none);

                free.xMin = statsRect.xMax + 6f;
            }

            var text = Expression(list.serializedProperty);
            if (string.IsNullOrEmpty(text)) return;

            EditorGUI.LabelField(free, new GUIContent(text, s_SourceHeader.tooltip),
                new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip });
        }

        //MWC — [SourceStats] lives on the modifier itself, one level above the [sources] array the list is bound to
        private static SerializedProperty SourceStatsOf(SerializedProperty sources)
        {
            const string suffix = ".sources";

            var path = sources.propertyPath;
            if (!path.EndsWith(suffix)) return null;

            var modifier = sources.serializedObject.FindProperty(path[..^suffix.Length]);
            return modifier?.FindPropertyRelative(nameof(StatModifier.SourceStats));
        }

        //MWC — the expression as text, built from the serialized terms (no runtime instance needed).
        //Same builder the runtime uses, so the header and the component title always read the same
        private static string Expression(SerializedProperty sources)
        {
            var builder = new SourceExpressionBuilder();

            for (int i = 0; i < sources.arraySize; i++)
            {
                var element = sources.GetArrayElementAtIndex(i);
                var op = (StatSourceOperator)element.FindPropertyRelative("op").enumValueIndex;
                var usesStat = element.FindPropertyRelative("from").enumValueIndex == (int)StatValueFrom.Stat;

                string term;

                if (usesStat)
                {
                    var stat = element.FindPropertyRelative("Stat").objectReferenceValue;
                    var part = (StatValuePart)element.FindPropertyRelative("part").enumValueIndex;
                    term = $"{(stat != null ? stat.name : "None")}{(part != StatValuePart.Value ? $".{part}" : string.Empty)}";
                }
                else
                {
                    var min = Constant(element, "MinValue");
                    var max = Constant(element, "MaxValue");
                    term = min == max ? min : $"[{min}-{max}]";
                }

                builder.Append(op, term, i == 0);
            }
            return builder.ToString();
        }

        //MWC — a fresh term: the first one defaults to a Stat (the common case), the rest to a static value
        private static void OnAddSource(ReorderableList list)
        {
            var index = list.serializedProperty.arraySize;
            list.serializedProperty.arraySize++;
            list.index = index;

            var element = list.serializedProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("op").enumValueIndex = (int)StatSourceOperator.Add;
            element.FindPropertyRelative("from").enumValueIndex = index == 0 ? (int)StatValueFrom.Stat : (int)StatValueFrom.Value;
            element.FindPropertyRelative("Stat").objectReferenceValue = null;
            element.FindPropertyRelative("part").enumValueIndex = (int)StatValuePart.Value;
            SetConstant(element, "MinValue", 0);
            SetConstant(element, "MaxValue", 0);
        }

        //MWC — one term:  [op] [Stat][part]  |  [Min][Max]   [Use Value/Use Stat]
        private static void DrawElement(Rect rect, int index, ReorderableList list)
        {
            var element = list.serializedProperty.GetArrayElementAtIndex(index);

            rect.y += 2;
            rect.height = EditorGUIUtility.singleLineHeight;
            rect.xMin += 12; //The drag handle is drawn INSIDE the element rect

            var op = element.FindPropertyRelative("op");
            var from = element.FindPropertyRelative("from");
            var stat = element.FindPropertyRelative("Stat");
            var part = element.FindPropertyRelative("part");
            var min = element.FindPropertyRelative("MinValue");
            var max = element.FindPropertyRelative("MaxValue");

            const float Gap = 2f;
            const float OpWidth = 40f;

            //MWC — BOTH drawers hang a menu to the LEFT of the rect they are given: the ID field its [⋮] (IDDrawer)
            //and the Float Reference its [Use Local/Use Global] popup (VariableReferenceDrawer). Both do
            //[buttonRect.x -= 20] AFTER PrefixLabel already pushed the field ~14px in, so ~5px of clearance in the
            //rect we pass is what actually keeps the menu off whatever sits before it. Same value for both, so the
            //two kinds of row stay aligned
            const float MenuGutter = 5f;

            //---- the operator ----
            var opRect = new Rect(rect) { width = OpWidth, x = rect.x - 15 };

            using (var change = new EditorGUI.ChangeCheckScope())
            {
                if (index == 0) //Nothing above it to combine with: it seeds the expression, or negates it
                {
                    var negated = op.enumValueIndex == (int)StatSourceOperator.Substract ? 1 : 0;
                    negated = EditorGUI.Popup(opRect, negated, s_FirstOps);

                    if (change.changed)
                        op.enumValueIndex = negated == 1 ? (int)StatSourceOperator.Substract : (int)StatSourceOperator.Add;
                }
                else
                {
                    var selected = EditorGUI.Popup(opRect, Mathf.Clamp(op.enumValueIndex, 0, s_Ops.Length - 1), s_Ops);
                    if (change.changed) op.enumValueIndex = selected;
                }
            }

            //---- [Use Value] / [Use Stat], right aligned so it sits under the modifier's own [Value From] ----
            var kindWidth = Mathf.Min(EnumWidth, rect.width * 0.3f);
            var kindRect = new Rect(rect) { x = rect.xMax - kindWidth, width = kindWidth };
            EditorGUI.PropertyField(kindRect, from, GUIContent.none);

            //---- the term itself. It stops well before the [Use Value] popup: both drawers also paint a button
            //---- INSIDE the right edge of their rect (the ID picker, the Float Reference [+]), which must not touch it
            var main = new Rect(rect) { x = rect.x + OpWidth + Gap };
            main.width = kindRect.x - (Gap * 3) - main.x;

            //MWC — ONE column grid shared by both kinds of row, so [Stat ID] lines up with [Min], and [Source Part]
            //with [Max], whatever the row is.
            //The SECOND column needs real clearance on its left, not just the gutter: [Max] is a Float Reference and
            //hangs its own [⋮] there, which otherwise lands on top of the right edge of [Min].
            //The split is derived (not a fraction) so both columns end up exactly the same width
            const float ColumnGap = 16f;

            var split = (main.width + ColumnGap) * 0.5f;
            var columnA = new Rect(main) { x = main.x + MenuGutter, width = split - MenuGutter - ColumnGap };
            var columnB = new Rect(main) { x = main.x + split + MenuGutter, width = main.width - split - MenuGutter };

            if (from.enumValueIndex == (int)StatValueFrom.Stat)
            {
                //MWC — the Min/Max of a Stat term are not drawn, so repairing them here cannot fight the user:
                //terms created before the [UseConstant] fix would otherwise show an empty [Float Var] slot
                //the moment the row is switched over to [Use Value]
                RepairConstant(element, "MinValue");
                RepairConstant(element, "MaxValue");

                EditorGUI.PropertyField(columnA, stat, GUIContent.none);
                EditorGUI.PropertyField(columnB, part, GUIContent.none);
            }
            else
            {
                EditorGUI.PropertyField(columnA, min, GUIContent.none);
                EditorGUI.PropertyField(columnB, max, GUIContent.none);
            }
        }

        private bool CheckEnum(int modify)
        {
            return
                modify == (int)StatOption.None ||
                modify == (int)StatOption.Reset ||
                modify == (int)StatOption.DegenerateOff ||
                modify == (int)StatOption.RegenerateOff ||
                modify == (int)StatOption.ResetToMax ||
                modify == (int)StatOption.ResetToMin ||
                modify == (int)StatOption.RegenerateOn ||
                modify == (int)StatOption.DegenerateOn ||
                modify == (int)StatOption.RestoreValue ||
                modify == (int)StatOption.RestoreMax ||
                modify == (int)StatOption.RestoreMin ||
                modify == (int)StatOption.RestoreDegeneration ||
                modify == (int)StatOption.RestoreMultiplier ||
                modify == (int)StatOption.RestoreRegeneration;
        }
    }
#endif

}

