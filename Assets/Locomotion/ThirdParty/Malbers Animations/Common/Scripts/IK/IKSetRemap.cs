using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.IK
{
    // MWC — Rig specific override table for IK Set Profiles.
    //
    // An IK Set is authored against ONE skeleton: the Goal/Hint bones and the Offsets of a HumanIKGoal only make
    // sense on the rig they were tuned on (a 3DsMax Biped, a Synty rig and a Mixamo rig do not share bone
    // orientations). A Super Item references a single default Profile and the same item prefab is used by every
    // character, so the character is the one that has to say "when an item asks for [IK Set - Bow (Default)],
    // give it [IK Set - Bow (Synty)] instead".
    //
    // One asset per rig, assigned on the character's IKManager. Items are never modified, and a Profile with no
    // row falls through unchanged, so a character without a Remap keeps working exactly as before.
    //
    // Same idea as the WeaponIKProfiles asset on the MWeaponManager, and as the SuperItemOffsets asset on the
    // SuperItemManager: rig specific data lives on the character, not on the item.
    [CreateAssetMenu(menuName = "Malbers Animations/IK/IK Set Remap", fileName = "New IK Set Remap")]
    public class IKSetRemap : ScriptableObject
    {
        [TextArea(2, 5)]
        [Tooltip("What rig/character this Remap was authored for. E.g. 'Synty rig — different forearm/hand orientation than the Biped'")]
        public string Description;

        [Tooltip("Default IK Set Profile [left] → the Profile to use on this rig [right]")]
        [IDPairRatio(0.5f, 0.5f)]
        public List<IDPair<IKSetProfile, IKSetProfile>> remap = new();

        /// <summary>Lookup built from [remap]. Never serialized, rebuilt on demand.</summary>
        private Dictionary<IKSetProfile, IKSetProfile> dictionary;

        /// <summary>Rebuilds the lookup table from the serialized rows. Call it after editing [remap] at runtime.</summary>
        public void Initialize()
        {
            dictionary = new Dictionary<IKSetProfile, IKSetProfile>();

            if (remap == null) return;

            foreach (var pair in remap)
            {
                //Half filled rows are ignored instead of mapping a Profile to null (that would disable its IK)
                if (pair == null || pair.ID == null || pair.Value == null) continue;

                dictionary[pair.ID] = pair.Value; //A duplicated key keeps the last row, same as the Inspector reads it
            }
        }

        /// <summary>Finds the rig specific override of a default Profile. False when this Remap has no row for it.</summary>
        public bool TryGetProfile(IKSetProfile source, out IKSetProfile result)
        {
            result = null;

            if (source == null) return false;

            if (dictionary == null) Initialize();

            return dictionary.TryGetValue(source, out result) && result != null;
        }

        /// <summary>Returns the rig specific override of a Profile, or the Profile itself when it has no row.</summary>
        public IKSetProfile GetProfile(IKSetProfile source) => TryGetProfile(source, out var result) ? result : source;

        /// <summary>True when this Remap has a row for a default Profile. Used by the validation buttons.</summary>
        public bool HasProfile(IKSetProfile source) => TryGetProfile(source, out _);

        private void OnValidate() => dictionary = null; //MWC — drop the cache so an Inspector edit is picked up
    }


#if UNITY_EDITOR

    // MWC — kept in the same file as the asset, like IKSetProfile and IKSetLibrary do
    [CustomEditor(typeof(IKSetRemap))]
    public class IKSetRemapEditor : Editor
    {
        private IKSetRemap m;
        private SerializedProperty Description, remap;

        private void OnEnable()
        {
            m = (IKSetRemap)target;
            Description = serializedObject.FindProperty("Description");
            remap = serializedObject.FindProperty("remap");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                MalbersEditor.Foldout_Bold(true, m.name, MTools.MBlue * 2, true);

                EditorGUILayout.HelpBox(
                    "Rig specific IK Set Profiles.\nAssign it on the character's IK Manager. When an Item asks for the " +
                    "Profile on the left, this character uses the one on the right instead. Profiles with no row are used as they are.",
                    MessageType.None);

                EditorGUILayout.PropertyField(Description);
                EditorGUILayout.PropertyField(remap, new GUIContent($"Remap [{remap.arraySize}]", remap.tooltip));

                DrawIssues();
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Reports the rows that cannot work: empty halves, duplicated keys and self references.</summary>
        private void DrawIssues()
        {
            if (m.remap == null || m.remap.Count == 0) return;

            var issues = new List<string>();
            var keys = new HashSet<IKSetProfile>();

            for (int i = 0; i < m.remap.Count; i++)
            {
                var pair = m.remap[i];

                if (pair == null || (pair.ID == null && pair.Value == null))
                {
                    issues.Add($"[{i}] Empty row");
                    continue;
                }

                if (pair.ID == null) { issues.Add($"[{i}] No default Profile: the row is ignored"); continue; }
                if (pair.Value == null) { issues.Add($"[{i}] '{pair.ID.name}' has no override: the row is ignored"); continue; }

                if (pair.ID == pair.Value)
                    issues.Add($"[{i}] '{pair.ID.name}' maps to itself: the row does nothing");

                if (!keys.Add(pair.ID))
                    issues.Add($"[{i}] '{pair.ID.name}' is mapped twice: only the last row is used");
            }

            if (issues.Count > 0)
                EditorGUILayout.HelpBox(string.Join("\n", issues), MessageType.Warning);
        }
    }
#endif
}
