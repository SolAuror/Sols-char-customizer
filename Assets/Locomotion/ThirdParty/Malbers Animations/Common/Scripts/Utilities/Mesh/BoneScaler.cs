using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MalbersAnimations.Scriptables;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Utilities
{
    /// <summary>Uses presets to create/load/save bones scale variations of a character.</summary>
    [AddComponentMenu("Malbers/Utilities/Mesh/Bone Scaler")]

    public class BoneScaler : MonoBehaviour
    {
        [CreateScriptableAsset]
        public BonePreset preset;

        //[Header("Auto find bones")]
        [ContextMenuItem("Refresh Bones", "SetBones")]
        public Transform Root;

        [Tooltip("Skip bones with these names")]
        public StringArrayVar filter;

        public List<Transform> Bones = new();

        [ContextMenu("Refresh Bones")]
        /// <summary>Called when the Root bone Changes </summary>
        public void SetBones()
        {
            if (Root)
                Bones = Root.GetComponentsInChildren<Transform>().ToList();

            List<Transform> newBones = new();
            List<Transform> filteredBones = new();

            var allskinnedMeshRenderers = Root.GetComponentsInChildren<SkinnedMeshRenderer>();

            newBones.Add(Root);

            foreach (var sm in allskinnedMeshRenderers)
            {
                foreach (var b in Bones)
                {
                    bool foundOne = false;

                    if (sm != null && sm.bones != null && !sm.bones.Contains(b)) continue; //Means is a Bone but is not used by any SkinnedMeshRenderer so skip it!

                    if (newBones.Contains(b)) continue; //Means is already added as a bone so skip it!

                    if (b.GetComponent<SkinnedMeshRenderer>()) continue; //Means is a Mesh so skip it!
                    if (!b.gameObject.activeSelf) continue; //Means is a Mesh so skip it!

                    if (filter != null)
                    {
                        for (int i = 0; i < filter.Length; i++)
                        {
                            if (b.name.Contains(filter[i]))
                            {
                                foundOne = true;
                                filteredBones.Add(b);
                                break;
                            }
                        }
                    }

                    if (!foundOne)
                    {
                        if (filteredBones.Find(item => b.SameHierarchy(item))) continue; //Do not add any from the same hierarchy of the filtered bones

                        newBones.Add(b);
                    }
                }
            }

            Bones = newBones;
        }

        public void SavePreset()
        {
            if (preset)
            {
                preset.Bones = new()
                {
                    new MiniTransform("Root", Vector3.zero, Root.localScale) //Add the Root bone as the first one
                };

                for (int i = 0; i < Bones.Count; i++)
                {
                    preset.Bones.Add(new MiniTransform(Bones[i].name, Bones[i].localPosition, Bones[i].localScale));
                }

                if (transform.name == Bones[0].name)
                {
                    preset.Bones[0].name = "Root";
                }

                MTools.SetDirty(this);

                Debug.Log("Preset: " + preset.name + " Saved from " + name);
            }
            else
            {
                Debug.LogWarning("There's no Preset Asset to save the bones");
            }
        }

        void Reset()
        {
            Root = transform;
            filter = MTools.GetInstance<StringArrayVar>("Bone Filter");
            SetBones();
        }

        public void LoadPreset()
        {
            if (preset)
            {
                Bones = transform.GetComponentsInChildren<Transform>().ToList(); ;

                List<Transform> newBones = new();

                if (preset.Bones[0].name == "Root")
                {
                    if (preset.scales) transform.localScale = preset.Bones[0].Scale;
                    Root = transform;
                    newBones.Add(transform);

                    //#if UNITY_EDITOR
                    //                    UnityEditor.EditorUtility.SetDirty(Root);
                    //#endif
                }

                foreach (var bone in preset.Bones)
                {
                    if (bone.name == "Root") continue; //Skip the Root bone since is already applied to the Transform


                    var Bone_Found = Bones.Find(item => item.name == bone.name);

                    if (Bone_Found)
                    {
                        if (preset.positions) Bone_Found.localPosition = bone.Position;
                        //if (rotations) Bone_Found.rotation = bone.rotation;
                        if (preset.scales) Bone_Found.localScale = bone.Scale;

                        newBones.Add(Bone_Found);

                        //#if UNITY_EDITOR
                        //                        UnityEditor.EditorUtility.SetDirty(Bone_Found);
                        //#endif
                    }
                }

                Bones = newBones;


                Debug.Log("Preset: " + preset.name + " Loaded on " + name);

            }
            else
            {
                Debug.LogWarning("There's no Preset to Load from");
            }
        }
    }



    //INSPECTOR!
#if UNITY_EDITOR
    [CustomEditor(typeof(BoneScaler)), CanEditMultipleObjects]
    public class BoneScalerEditor : Editor
    {
        BoneScaler M;
        // private MonoScript script;

        SerializedProperty /*positions, scales,*/ preset, Root, filter;
        protected int index = 0;

        private void OnEnable()
        {
            M = (BoneScaler)target;
            //script = MonoScript.FromMonoBehaviour(M);

            preset = serializedObject.FindProperty("preset");
            Root = serializedObject.FindProperty("Root");
            filter = serializedObject.FindProperty("filter");

        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            MalbersEditor.DrawDescription("Save/Load Bones Transform values into a Preset");

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(preset);

                bool disable_ = preset.objectReferenceValue == null;

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUI.BeginDisabledGroup(disable_);
                    {
                        if (GUILayout.Button("Save"))
                        {
                            foreach (var t in targets)
                            {
                                var M = (BoneScaler)t;
                                M.SavePreset();
                                EditorUtility.SetDirty(M.preset);
                            }
                        }

                        if (GUILayout.Button("Load"))
                        {
                            foreach (var t in targets)
                            {
                                var M = (BoneScaler)t;
                                foreach (var bn in M.Bones)
                                {
                                    Undo.RecordObject(bn, "Bones Loaded"); // Save the bones loaded
                                }
                                M.LoadPreset();
                            }
                        }
                    }
                    EditorGUI.EndDisabledGroup();
                }
            }


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Bones (" + M.Bones.Count.ToString() + ")");
                EditorGUI.BeginChangeCheck();
                {
                    EditorGUILayout.PropertyField(Root);
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "Root Changed");
                    EditorUtility.SetDirty(M);
                    serializedObject.ApplyModifiedProperties();
                    M.SetBones();
                }

                EditorGUILayout.PropertyField(filter);
            }


            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                MalbersEditor.Arrays(serializedObject.FindProperty("Bones"));
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif 
}