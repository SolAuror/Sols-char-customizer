// Last Change: 9/18/2026 11:00am
using System;
using UnityEngine;

namespace MalbersAnimations.Scriptables
{
    ///<summary>  Prefab Scriptable Variable. Based on the Talk - Game Architecture with Scriptable Objects by Ryan Hipple </summary>
    [CreateAssetMenu(menuName = "Malbers Animations/Variables/Transform", order = 3000)]
    public class TransformVar : ScriptableVar
    {
        [SerializeField] private Transform value;

        /// <summary>Invoked when the value changes </summary>
        public Action<Transform> OnValueChanged;

        /// <summary> Value of the Bool variable</summary>
        public virtual Transform Value
        {
            get => value;
            set
            {
                if (!ReferenceEquals(value, this.value)) //Reference comparison: a DESTROYED Transform must still be replaceable by null (Unity == reads both as null and skipped the write)
                {
                    this.value = value;
                    OnValueChanged?.Invoke(value);         //If we are using OnChange event Invoked
#if UNITY_EDITOR
                    if (debug) Debug.Log($"<B>{name} -> [<color=white> {(value != null ? value.name : "NULL")} </color>] </B>", this);
#endif
                }
            }
        }

        public virtual void SetValue(TransformVar var) => Value = var.Value;
        public virtual void SetNull() => Value = null;
        public virtual void SetValue(Transform var) => Value = var;
        public virtual void SetValue(GameObject var) => Value = var.transform;
        public virtual void SetValue(Component var) => Value = var.transform;
        public virtual void ApplyPositionTo(Transform var) => var.position = Value.position;
        public virtual void SetVector3Value(Vector3Var var) => var.Value = Value.position;

        public virtual void SetParentOf(Transform var) => var.SetParent(Value);
        public virtual void SetChildOf(Transform var) => Value.SetParent(var);
        public virtual void ClearParent() => Value.parent = null;

        public virtual void DetachChildren()
        {
            if (Value == null) return;


            foreach (Transform child in Value)
            {
                child.parent = null;
            }
        }

        [ContextMenu("Clear Value")]
        void ClearInEditor()
        {
            if (value != null)
            {
                Debug.Log($"Value was dirty : [{Value}]");
                Value = null;
                MTools.SetDirty(this);
            }
        }
    }

    [Serializable]
    public class TransformReference : ReferenceVar
    {
        public Transform ConstantValue;
        [RequiredField] public TransformVar Variable;

        public TransformReference() => UseConstant = true;
        public TransformReference(Transform value) => Value = value;

        public TransformReference(TransformVar value)
        {
            Variable = value;
            UseConstant = false;
        }

        public Transform Value
        {
            get
            {
                if (UseMember) return Member.GetTransform(ConstantValue); //MWC — Use Reference mode reads the member; ConstantValue is the fallback
                return UseConstant ? ConstantValue : (Variable != null ? Variable.Value : null);
            }

            set
            {
                if (BlockedByMember()) return; //MWC — read-only while using a member reference

                if (UseConstant || Variable == null)
                {
                    UseConstant = true;
                    ConstantValue = value;
                }
                else
                    Variable.Value = value;
            }
        }

        public Vector3 position => Value.position;
        public Quaternion rotation => Value.rotation;
        public Vector3 forward => Value.forward;
        public Vector3 right => Value.right;
        public Vector3 up => Value.up;

        public Vector3 localPosition => Value.localPosition;
        public Quaternion localRotation => Value.localRotation;

        public Vector3 localScale => Value.localScale;

        public Vector3 lossyScale => Value.lossyScale;

        public virtual void SetPosition(Vector3 pos)
        {
            if (Value) Value.position = pos;
        }
        public virtual void SetRotation(Quaternion rot)
        {
            if (Value) Value.rotation = rot;
        }

        public virtual void SetPositionAndRotation(Vector3 pos, Quaternion rot)
        {
            if (Value)
            {
                Value.SetPositionAndRotation(pos, rot);
            }
        }


        public static implicit operator Transform(TransformReference reference) => reference.Value;

        public static implicit operator GameObject(TransformReference reference) => reference.Value.gameObject;

        public static implicit operator TransformReference(Transform reference) => new(reference);
    }

    public static class TransformReferenceExtensions
    {
        public static Transform NearestTransform(this Transform origin, params Scriptables.TransformReference[] transforms)
        {
            Transform bestTarget = null;
            float closestDistanceSqr = Mathf.Infinity;
            Vector3 currentPosition = origin.position;
            foreach (Transform potentialTarget in transforms)
            {
                Vector3 directionToTarget = potentialTarget.position - currentPosition;
                float dSqrToTarget = directionToTarget.sqrMagnitude;
                if (dSqrToTarget < closestDistanceSqr)
                {
                    closestDistanceSqr = dSqrToTarget;
                    bestTarget = potentialTarget;
                }
            }
            return bestTarget;
        }
    }


#if UNITY_EDITOR

    [UnityEditor.CanEditMultipleObjects, UnityEditor.CustomEditor(typeof(TransformVar))]
    public class TransformVarEditor : VariableEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new GUILayout.HorizontalScope())
            {
                MalbersEditor.DrawDescription("Runtime Transform Reference");
                MalbersEditor.DrawDebugIcon(debug);
            }

            UnityEditor.EditorGUILayout.Space();

            var go = value.objectReferenceValue as Transform;

            if (Application.isPlaying)
            {
                UnityEditor.EditorGUILayout.ObjectField("Value ", go, typeof(GameObject), false);
            }
            else
            {
                UnityEditor.EditorGUILayout.LabelField("<Runtime Only>", UnityEditor.EditorStyles.boldLabel);
            }


            UnityEditor.EditorGUILayout.PropertyField(Description);


            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
