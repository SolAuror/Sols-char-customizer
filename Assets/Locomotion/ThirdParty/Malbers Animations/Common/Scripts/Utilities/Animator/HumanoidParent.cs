// Last Change: 10/16/2026 12:00pm
using MalbersAnimations.Scriptables;
using UnityEngine;


#if UNITY_EDITOR
using UnityEditor;
#endif
namespace MalbersAnimations.Utilities
{
    [AddComponentMenu("Malbers/Utilities/Transform/Humanoid Parent")]
    [DefaultExecutionOrder(2000)]
    public class HumanoidParent : MonoBehaviour
    {
        public Animator animator;
        [SearcheableEnum]
        [Tooltip("Which bone will be the parent of this gameobject")]
        public HumanBodyBones parent = HumanBodyBones.Spine;
        [Tooltip("Reset the Local Position of this gameobject when parented")]
        public BoolReference LocalPos;
        [Tooltip("Reset the Local Rotation of this gameobject when parented")]
        public BoolReference LocalRot;
        [Tooltip("Additional Local Position Offset to add after the gameobject is parented.\n" +
            "It is measured in the Animator's scale, so the same value works on rigs whose bones are scaled (e.g. Root bone at 0.01)")]
        public Vector3Reference PosOffset;
        [Tooltip("Additional Local Rotation Offset to add after the gameobject is parented")]
        public Vector3Reference RotOffset;

        /// <summary> Parents this gameobject to the bone when enabled </summary>
        private void OnEnable()
        {
            if (animator == null) { animator = this.FindComponent<Animator>(); }

            if (animator != null) Align();
        }

        /// <summary> Parents this gameobject to the bone and applies the offsets, compensating the bone's scale </summary>
        private void Align()
        {
            if (animator.avatar != null)
            {
                var boneParent = animator.GetBoneTransform(parent);

                if (boneParent != null && transform.parent != boneParent)
                {
                    transform.parent = boneParent;

                    if (LocalPos.Value) transform.localPosition = Vector3.zero;
                    if (LocalRot.Value) transform.localRotation = Quaternion.identity;

                    //Bones scaled inside the rig (e.g. Synty Root at 0.01) would shrink/grow the offset. Undo that scale
                    transform.localPosition += Vector3.Scale(PosOffset.Value, InverseBoneScale(boneParent));
                    transform.localRotation *= Quaternion.Euler(RotOffset);
                }
            }
            else
            {
                Debug.LogWarning($"Avatar is missing in the animator. [{name}]", this);
                enabled = false;
            }
        }

        /// <summary> Inverse of the bone's scale relative to the Animator (1,1,1 when every bone between them has scale 1) </summary>
        private Vector3 InverseBoneScale(Transform bone)
        {
            var root = animator.transform.lossyScale;
            var b = bone.lossyScale;

            return new Vector3(
                Mathf.Approximately(b.x, 0) ? 1 : root.x / b.x,
                Mathf.Approximately(b.y, 0) ? 1 : root.y / b.y,
                Mathf.Approximately(b.z, 0) ? 1 : root.z / b.z);
        }

        /// <summary> Copies the current local position/rotation into the offsets, converted to the Animator's scale </summary>
        [ContextMenu("Copy Local")]
        private void CopyLocalPosition()
        {
            var bone = transform.parent;
            var inv = bone != null && animator != null ? InverseBoneScale(bone) : Vector3.one;

            PosOffset.Value = new Vector3(transform.localPosition.x / inv.x, transform.localPosition.y / inv.y, transform.localPosition.z / inv.z);
            RotOffset.Value = transform.localRotation.eulerAngles;
        }

        /// <summary> Previews the parented pose in world space (bone pose + offsets) without changing the parent. Editor helper, Undo-able. </summary>
        [ContextMenu("Try Align")]
        public void TryAlign()
        {
            if (animator == null || animator.avatar == null) return;

            var boneParent = animator.GetBoneTransform(parent);
            if (boneParent == null) return;

#if UNITY_EDITOR
            Undo.RecordObject(transform, "Humanoid Parent Try Align");
#endif
            //TransformPoint uses the bone's full local-to-world matrix, so the offset is rotated and scaled by the bone and all its ancestors,
            //exactly like Align() once the object is parented to the bone (same bone-scale compensation).
            var worldPos = boneParent.TransformPoint(Vector3.Scale(PosOffset.Value, InverseBoneScale(boneParent)));
            var worldRot = boneParent.rotation * Quaternion.Euler(RotOffset.Value);

            transform.SetPositionAndRotation(worldPos, worldRot);

            Debug.Log($"Humanoid Parent Aligned [{name}] to [{parent}] of [{animator.name}]", this);
        }
        private void OnValidate()
        {
            if (animator == null || !transform.SameHierarchy(animator.transform)) animator = gameObject.FindComponent<Animator>();
        }
    }



#if UNITY_EDITOR
    //----------------------------------------------------------------------------------
    //Create an editor to draw the HumanoidParent component in an horizontal groups

    [CustomEditor(typeof(HumanoidParent)), CanEditMultipleObjects]
    public class HumanoidParentEditor : Editor
    {
        SerializedProperty animator, parent, LocalPos, LocalRot, PosOffset, RotOffset;

        private void OnEnable()
        {
            animator = serializedObject.FindProperty("animator");
            parent = serializedObject.FindProperty("parent");
            LocalPos = serializedObject.FindProperty("LocalPos");
            LocalRot = serializedObject.FindProperty("LocalRot");
            PosOffset = serializedObject.FindProperty("PosOffset");
            RotOffset = serializedObject.FindProperty("RotOffset");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(animator);
                    if (GUILayout.Button(new GUIContent("Align", "Moves this object to the bone pose plus the offsets, without parenting it (Undo-able)"), GUILayout.MaxWidth(60)))
                    {
                        serializedObject.ApplyModifiedProperties(); //use the latest inspector values

                        foreach (var t in targets)
                            (t as HumanoidParent).TryAlign();
                    }
                }
                EditorGUILayout.PropertyField(parent);

                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(LocalPos);
                    EditorGUIUtility.labelWidth = 80;
                    EditorGUILayout.PropertyField(LocalRot, GUILayout.MinWidth(100));
                    EditorGUIUtility.labelWidth = 0;


                }

                EditorGUILayout.PropertyField(PosOffset);
                EditorGUILayout.PropertyField(RotOffset);
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
