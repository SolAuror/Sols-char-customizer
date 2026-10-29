// Last Change: 9/14/2026 12:20pm
using MalbersAnimations.Reactions;
using MalbersAnimations.Conditions;
using MalbersAnimations.Scriptables;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations
{
    [System.Serializable]
    public struct DamageData
    {
        /// <summary> Who is doing the damage </summary>
        public GameObject Damager;
        /// <summary>Who received the damage </summary>
        public GameObject Damagee;
        /// <summary> Stat Modifier with the updated final stat values (the PRIMARY damage of the hit)</summary>
        public StatModifier stat;

        //MWC — additive damage bucket. Modifiers whose Target Stat matches the primary are summed into 'stat';
        //modifiers for a DIFFERENT Target Stat are kept here and applied alongside the primary hit. This lets several
        //[Damager - Do Damage] processors (e.g. one reading [Local] Stats + one reading [FromCharacter]) stack in one hit.
        public List<StatModifier> extraStats;

        /// <summary> Direction of the Damage</summary>
        public Vector3 Direction;

        /// <summary> Position of the Damage Contact</summary>
        public Vector3 HitPosition;

        /// <summary> Final value who modified the Stat</summary>
        public readonly float Damage => stat.modify != StatOption.None ? stat.Value : 0f;

        /// <summary>Store if the Damage was Critical</summary>
        public bool critical;

        /// <summary>Store if the Damage was Pure</summary>
        public bool Pure;

        /// <summary>Was Miss</summary>
        public bool Missed;

        /// <summary>Store if the damage was  </summary>
        public ElementMultiplier Element;

        public Reaction2 reaction;

        public DamageData
            (Vector3 direction, GameObject damager, GameObject damagee, StatModifier stat, ElementMultiplier element, Reaction2 reaction, bool critical, bool pure, bool missed)
        {
            Damager = damager;
            Damagee = damagee;
            this.stat = new StatModifier(stat);
            this.critical = critical;
            Element = element;
            Direction = direction;
            this.reaction = reaction;
            Missed = missed;
            HitPosition = Vector3.zero; // Default value for HitPosition
            Pure = pure;
            extraStats = null; //MWC — created lazily by AddStat; a struct ctor must still assign every field
        }

        public DamageData
           (Vector3 direction, Vector3 hitPosition, GameObject damager, GameObject damagee,
            StatModifier stat, bool critical, ElementMultiplier element, Reaction2 reaction, bool missed)
        {
            Damager = damager;
            Damagee = damagee;
            this.stat = new StatModifier(stat);
            this.critical = critical;
            Element = element;
            Direction = direction;
            this.reaction = reaction;
            Missed = missed;
            HitPosition = hitPosition;
            Pure = false;
            extraStats = null; //MWC — created lazily by AddStat; a struct ctor must still assign every field
        }

        public DamageData(StatID stat, float value)
        {
            Damager = null;
            Damagee = null;
            this.stat = new StatModifier(stat, value);
            Element = new();
            Direction = Vector3.zero;
            HitPosition = Vector3.zero; // Default value for HitPosition
            reaction = new Reaction2();
            critical = false;
            Missed = false;
            Pure = false;
            extraStats = null; //MWC — created lazily by AddStat; a struct ctor must still assign every field
        }

        //MWC — accumulate a Stat Modifier for this hit. The first one (or any sharing the primary's Target Stat + Option)
        //is summed into 'stat'; a modifier for a different Target Stat is merged into 'extraStats'. Values are expected
        //to be already resolved (baked) by the caller so the sum is deterministic.
        public void AddStat(StatModifier s)
        {
            if (stat.IsNull)                                    //First modifier → becomes the primary
            {
                stat = new StatModifier(s);
                return;
            }

            if (stat.ID == s.ID && stat.modify == s.modify)    //Same Target Stat + Option → sum into the primary
            {
                stat.Value += s.Value;
                return;
            }

            extraStats ??= new List<StatModifier>();            //Different Target Stat → keep it (merged by Target + Option)
            for (int i = 0; i < extraStats.Count; i++)
            {
                if (extraStats[i].ID == s.ID && extraStats[i].modify == s.modify)
                {
                    var m = extraStats[i];
                    m.Value += s.Value;
                    extraStats[i] = m;
                    return;
                }
            }
            extraStats.Add(new StatModifier(s));
        }

        //MWC — scale every buffered modifier's value (used by the Critical processor so a crit scales ALL accumulated damage)
        public void MultiplyAll(float multiplier)
        {
            stat.Value *= multiplier;

            if (extraStats == null) return;
            for (int i = 0; i < extraStats.Count; i++)
            {
                var m = extraStats[i];
                m.Value *= multiplier;
                extraStats[i] = m;
            }
        }

        public void ClearData()
        {
            Damager = null;
            Damagee = null;
            stat = new StatModifier(null, 0);
            extraStats?.Clear(); //MWC — reset the additive bucket for the next hit
            Element = new ElementMultiplier(null);
            Direction = Vector3.zero;
            HitPosition = Vector3.zero;
            reaction = new Reaction2();
            critical = false;
            Missed = false;
            Pure = false;
        }
    }


    [System.Serializable]
    public struct MDamageableProfile
    {
        [Tooltip("Name of the Profile. This is used for Setting a New Damageable Profile. E.g. When the Animal is blocking or Parrying")]
        public string name;

        [Tooltip("Type of surface the Damageable is. (Flesh, Metal, Wood,etc)")]
        public SurfaceID surface;

        [Tooltip("Multiplier for the Stat modifier Value. Use this to increase or decrease the final value of the Stat")]
        public FloatReference multiplier;

        [Tooltip("When Enabled the animal will rotate towards the Damage direction")]
        public BoolReference AlignToDamage;

        [Tooltip("Ignore the custom Reaction sent by the Damager and always play this Profile's Damage Reaction. E.g. a Block that must always play its Block-Hit animation")]
        public BoolReference ignoreDamagerReaction;

        [Tooltip("Reaction to apply to itself when the damage is done")]
        public Reaction2 damageReaction;

        [Tooltip(" Reaction to apply to itself when it receives a critical damage")]
        public Reaction2 criticalReaction;

        [Tooltip(" Reaction to apply to the one doing the damage")]
        public Reaction2 damagerReaction;

        [Tooltip("Elements that affect the MDamageable")]
        public List<ElementMultiplier> elements;

        [Tooltip("Conditions that must be true for the Damageable to receive damage while this Profile is active. Target = the Character. E.g. a Parry profile that ignores hits coming from the front")]
        public Conditions2 canReceiveDamage;

        [Tooltip("Show the Enter and Exit events of this Profile")]
        public bool Events;
        [Tooltip("Invoked when this Profile becomes the active one on the Damageable")]
        [Hide("Events")]
        public UnityEvent OnProfileEnter;
        [Tooltip("Invoked when this Profile stops being the active one on the Damageable")]
        [Hide("Events")]
        public UnityEvent OnProfileExit;

        public readonly float Multiplier => multiplier.Value;

        /// <summary> A ready-to-use Profile: every ReferenceVar local (Multiplier 1, Align To Damage on, Damager Reaction not ignored), empty Reactions, Elements and Conditions </summary>
        public static MDamageableProfile New(string name, SurfaceID surface = null) =>
            new(name, surface, new Reaction2(), new Reaction2(), new Reaction2(),
                new BoolReference(false), new FloatReference(1), new BoolReference(true), new List<ElementMultiplier>());

        public MDamageableProfile(string Name, SurfaceID surface,
            Reaction2 reaction, Reaction2 criticalReaction, Reaction2 DamagerReaction, BoolReference ignoreDamagerReaction,
            FloatReference multiplier, BoolReference AlignToDamage, List<ElementMultiplier> elements)
        {
            this.name = Name;
            this.surface = surface;
            this.damageReaction = reaction;
            this.damagerReaction = DamagerReaction;
            this.multiplier = multiplier;
            this.criticalReaction = criticalReaction;
            this.AlignToDamage = AlignToDamage;
            this.elements = elements;
            this.ignoreDamagerReaction = ignoreDamagerReaction;
            canReceiveDamage = new Conditions2(0);
            OnProfileEnter = new();
            OnProfileExit = new();
            Events = false;
        }

        public MDamageableProfile(MDamageableProfile newP)
        {
            name = newP.name;
            surface = newP.surface;
            damageReaction = newP.damageReaction;
            damagerReaction = newP.damagerReaction;
            multiplier = newP.multiplier;
            criticalReaction = newP.criticalReaction;
            AlignToDamage = newP.AlignToDamage;
            elements = new(newP.elements);
            ignoreDamagerReaction = newP.ignoreDamagerReaction;
            canReceiveDamage = newP.canReceiveDamage;
            OnProfileEnter = newP.OnProfileEnter;
            OnProfileExit = newP.OnProfileExit;
            Events = false;
        }
    }

#if UNITY_EDITOR
    /// <summary>Draws a Damageable Profile as a foldout with the name inline on the header, and the fields grouped in help boxes when expanded.</summary>
    [CustomPropertyDrawer(typeof(MDamageableProfile))]
    public class MDamageableProfileDrawer : PropertyDrawer
    {
        private const float BoxPad = 4f;
        private static float Line => EditorGUIUtility.singleLineHeight;
        private static float Pad => EditorGUIUtility.standardVerticalSpacing;

        private static readonly string[] GeneralFields = { "surface", "multiplier", "AlignToDamage", "ignoreDamagerReaction" };
        private static readonly string[] ReactionFields = { "damageReaction", "criticalReaction", "damagerReaction" };
        private static readonly string[] ElementFields = { "elements" };
        private static readonly string[] ConditionFields = { "canReceiveDamage" };
        private static readonly string[] EventsOff = { "Events" };
        private static readonly string[] EventsOn = { "Events", "OnProfileEnter", "OnProfileExit" };

        /// <summary>Events group shows the two UnityEvents only when the Events toggle is on.</summary>
        private static string[] EventFields(SerializedProperty property)
            => property.FindPropertyRelative("Events").boolValue ? EventsOn : EventsOff;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = EditorGUI.BeginProperty(position, label, property);
            {
                if (property.propertyPath.Contains(".Array.data[")) label = new GUIContent("Profile", label.tooltip);

                var name = property.FindPropertyRelative("name");

                //Header: foldout on the label column, the profile name on the field column
                var foldRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, Line);
                var nameRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y, position.width - EditorGUIUtility.labelWidth, Line);

                property.isExpanded = EditorGUI.Foldout(foldRect, property.isExpanded, label, true);

                var dC = GUI.color;
                GUI.color = string.IsNullOrEmpty(name.stringValue) ? Color.red : dC;
                EditorGUI.PropertyField(nameRect, name, GUIContent.none);
                GUI.color = dC;

                if (property.isExpanded)
                {
                    var indent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = indent + 1;

                    var line = new Rect(position.x, position.y + Line + Pad, position.width, Line);

                    DrawGroup(ref line, property, GeneralFields);
                    DrawGroup(ref line, property, ConditionFields);
                    DrawGroup(ref line, property, ReactionFields);
                    DrawGroup(ref line, property, ElementFields);
                    DrawGroup(ref line, property, EventFields(property));

                    EditorGUI.indentLevel = indent;
                }
            }
            EditorGUI.EndProperty();
        }

        /// <summary>Draws one help box holding the given child fields, each at its own height, and advances the line rect below it.</summary>
        private static void DrawGroup(ref Rect line, SerializedProperty property, string[] fields)
        {
            var box = new Rect(line.x, line.y, line.width, GroupHeight(property, fields));
            GUI.Box(box, GUIContent.none, EditorStyles.helpBox);

            var row = new Rect(box.x + BoxPad, box.y + BoxPad, box.width - BoxPad * 2, Line);

            foreach (var field in fields)
            {
                var child = property.FindPropertyRelative(field);
                row.height = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(row, child, true);
                row.y += row.height + Pad;
            }

            line.y = box.yMax + Pad;
        }

        /// <summary>Height of one help box: padding plus every child height, mirroring DrawGroup exactly.</summary>
        private static float GroupHeight(SerializedProperty property, string[] fields)
        {
            float h = BoxPad * 2 - Pad;
            foreach (var field in fields)
                h += EditorGUI.GetPropertyHeight(property.FindPropertyRelative(field), true) + Pad;
            return h;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float h = Line;
            if (!property.isExpanded) return h;

            h += Pad + GroupHeight(property, GeneralFields);
            h += Pad + GroupHeight(property, ConditionFields);
            h += Pad + GroupHeight(property, ReactionFields);
            h += Pad + GroupHeight(property, ElementFields);
            h += Pad + GroupHeight(property, EventFields(property));
            return h;
        }
    }
#endif
}
