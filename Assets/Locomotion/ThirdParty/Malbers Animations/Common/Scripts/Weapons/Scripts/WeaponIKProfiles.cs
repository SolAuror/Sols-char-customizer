using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Weapons
{
    // MWC — New Asset to store Aim IK Profiles per Weapon ID.
    // The Weapons store their own IK Profiles, but those are authored for a specific skeleton.
    // Characters with a different bone orientation/hierarchy (Synty, Mixamo, custom rigs) get twisted
    // when they use them. Assign one of these assets to the Weapon Manager to override the
    // IK Profiles of ALL the weapons that character equips.

    /// <summary> Set of Aim IK Profiles overrides (per Weapon ID) used by the <see cref="MWeaponManager"/>.
    /// Create one asset per Character Rig (e.g. "IK Profiles [Synty]") and share it between all the
    /// characters using that skeleton. </summary>
    [CreateAssetMenu(menuName = "Malbers Animations/Weapons/Weapon IK Profiles", fileName = "New Weapon IK Profiles", order = 100)]
    public class WeaponIKProfiles : ScriptableObject
    {
        [Tooltip("Aim IK Profiles used by a Character Rig. One entry per Weapon ID.\n" +
            "These will override the Aim IK Profiles stored on the Weapons themselves")]
        public List<WeaponIKPair> profiles = new();

        /// <summary> Fast [WeaponID → Profiles] lookup. Runtime only, never serialized </summary>
        private Dictionary<int, WeaponIKPair> lookup;

        private Dictionary<int, WeaponIKPair> Lookup
        {
            get
            {
                if (lookup == null)
                {
                    lookup = new Dictionary<int, WeaponIKPair>();

                    foreach (var p in profiles)
                        if (p != null && p.Weapon != null)
                            lookup[p.Weapon.ID] = p;
                }
                return lookup;
            }
        }

        /// <summary> Clears the cached lookup. Call it if the profiles list is modified at runtime </summary>
        public virtual void Refresh() => lookup = null;

        /// <summary> Find the Aim IK Profile override for a Weapon ID. Returns null when there's no override </summary>
        public virtual IKProfile GetProfile(int weaponID, bool rightHand)
            => Lookup.TryGetValue(weaponID, out var pair) ? pair.Profile(rightHand) : null;

        /// <summary> Find the Aim IK Profile override for a Weapon ID. Returns null when there's no override </summary>
        public virtual IKProfile GetProfile(WeaponID weapon, bool rightHand)
            => weapon != null ? GetProfile(weapon.ID, rightHand) : null;

        /// <summary> True when an override Profile exists for that Weapon ID and hand </summary>
        public virtual bool TryGetProfile(int weaponID, bool rightHand, out IKProfile profile)
        {
            profile = GetProfile(weaponID, rightHand);
            return profile != null;
        }

        /// <summary> True when an override Profile exists for that Weapon ID and hand </summary>
        public virtual bool TryGetProfile(WeaponID weapon, bool rightHand, out IKProfile profile)
        {
            profile = GetProfile(weapon, rightHand);
            return profile != null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            lookup = null; //MWC — the list changed on the Inspector, rebuild the lookup on the next call

            foreach (var p in profiles)
                if (p != null)
                    p.name = p.Weapon != null ? p.Weapon.name : "[No Weapon ID]";
        }
#endif
    }

    // MWC — One row of the Weapon IK Profiles asset: the Weapon ID and its Right/Left Aim IK Profiles
    [System.Serializable]
    public class WeaponIKPair
    {
        [HideInInspector] public string name;

        [Tooltip("Weapon ID this override applies to")]
        public WeaponID Weapon;

        [Tooltip("Aim IK Profile used when the weapon is Right handed")]
        public IKProfile Right;

        [Tooltip("Aim IK Profile used when the weapon is Left handed")]
        public IKProfile Left;

        /// <summary> Profile for the hand the weapon is held with </summary>
        public IKProfile Profile(bool rightHand) => rightHand ? Right : Left;
    }

    #region Inspector
#if UNITY_EDITOR

    // MWC — Compact one-line drawer: [Weapon ID | R: Profile | L: Profile]
    [CustomPropertyDrawer(typeof(WeaponIKPair))]
    public class WeaponIKPairDrawer : PropertyDrawer
    {
        private const float ID_RATIO = 0.34f;
        private const float SPACING = 4f;
        private const float HAND_LABEL_WIDTH = 16f;

        private static readonly GUIContent R_LABEL = new("R", "Aim IK Profile used when the weapon is Right handed");
        private static readonly GUIContent L_LABEL = new("L", "Aim IK Profile used when the weapon is Left handed");

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            //Inside a list the label is "Element 0"... which just wastes horizontal space
            bool isArrayElement = property.propertyPath.Contains(".Array.data[");

            Rect content = isArrayElement
                ? position
                : EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            int savedIndent = EditorGUI.indentLevel;
            float savedLabelW = EditorGUIUtility.labelWidth;
            EditorGUI.indentLevel = 0;

            var Weapon = property.FindPropertyRelative("Weapon");
            var Right = property.FindPropertyRelative("Right");
            var Left = property.FindPropertyRelative("Left");

            var h = EditorGUIUtility.singleLineHeight;
            var idW = content.width * ID_RATIO - SPACING;
            var profileW = (content.width - idW - SPACING * 2) * 0.5f;

            var idRect = new Rect(content.x, content.y, idW, h);
            var rightRect = new Rect(idRect.xMax + SPACING, content.y, profileW, h);
            var leftRect = new Rect(rightRect.xMax + SPACING, content.y, profileW, h);

            var oldColor = GUI.backgroundColor;

            //Tint red when the ID is empty, the row does nothing without it
            if (Weapon.objectReferenceValue == null) GUI.backgroundColor = Color.red;
            EditorGUI.PropertyField(idRect, Weapon, GUIContent.none);
            GUI.backgroundColor = oldColor;

            EditorGUIUtility.labelWidth = HAND_LABEL_WIDTH;
            EditorGUI.PropertyField(rightRect, Right, R_LABEL);
            EditorGUI.PropertyField(leftRect, Left, L_LABEL);

            EditorGUIUtility.labelWidth = savedLabelW;
            EditorGUI.indentLevel = savedIndent;

            EditorGUI.EndProperty();
        }
    }

    // MWC — Description header for the asset
    [CustomEditor(typeof(WeaponIKProfiles))]
    public class WeaponIKProfilesEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            MalbersEditor.DrawDescription("Aim IK Profiles used by a Character Rig.\n" +
                "Assign this asset to the [Weapon Manager] to override the Aim IK Profiles stored on the Weapons");

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("profiles"), true);
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
    #endregion
}
