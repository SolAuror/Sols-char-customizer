using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using UnityEngine;


namespace MalbersAnimations
{
    [DefaultExecutionOrder(750)]
    [AddComponentMenu("Malbers/Variables/Vector2 Listener")]
    [HelpURL("https://malbersanimations.gitbook.io/animal-controller/secondary-components/variable-listeners-and-comparers")]
    public class Vector2VarListener : VarListener
    {
        public Vector2Reference value = new();
        public Vector2Event OnValue = new();
        public Transform _cam;

        public Vector2 Value
        {
            get => value;
            set
            {
                if (!Enable) return;

                this.value.Value = value;
                Invoke(value);
            }
        }

        void OnEnable()
        {
            if (value.Variable != null) value.Variable.OnValueChanged += Invoke;
            Invoke(value);
            _cam = Camera.main.transform;
        }

        void OnDisable()
        {
            if (value.Variable != null) value.Variable.OnValueChanged -= Invoke;
        }


        public virtual void TransformRotateUpCam(Transform tr) => TransformRotateCam(tr, tr.up);

        public virtual void TransformRotateUp(Transform tr) => TransformRotate(tr, tr.up);
        public virtual void TransformRotateForward(Transform tr) => TransformRotate(tr, tr.forward);
        public virtual void TransformRotateRight(Transform tr) => TransformRotate(tr, tr.right);

        public virtual void TransformRotateDown(Transform tr) => TransformRotate(tr, -tr.up);
        public virtual void TransformRotateBack(Transform tr) => TransformRotate(tr, -tr.forward);
        public virtual void TransformRotateLeft(Transform tr) => TransformRotate(tr, -tr.right);

        public virtual void TransformRotate(Transform tr, Vector3 axis)
        {
            if (Value == Vector2.zero) return;
            float angle = Mathf.Atan2(Value.x, Value.y) * Mathf.Rad2Deg;

            tr.rotation = Quaternion.AngleAxis(angle, axis);
        }

        public void TransformRotateCam(Transform tr, Vector3 axis)
        {
            if (Value == Vector2.zero) return;
            var rot = RotateVector2(value, -_cam.localEulerAngles.y);

            float angle = Mathf.Atan2(rot.x, rot.y) * Mathf.Rad2Deg;

            tr.rotation = Quaternion.AngleAxis(angle, axis);
        }

        private Vector2 RotateVector2(Vector2 vector, float angle)
        {
            if (angle == 0)
            {
                return vector;
            }
            float sinus = Mathf.Sin(angle * Mathf.Deg2Rad);
            float cosinus = Mathf.Cos(angle * Mathf.Deg2Rad);

            float oldX = vector.x;
            float oldY = vector.y;
            vector.x = (cosinus * oldX) - (sinus * oldY);
            vector.y = (sinus * oldX) + (cosinus * oldY);
            return vector;
        }


        public virtual void Invoke(Vector2 value)
        {
            if (Enable)
            {
                OnValue.Invoke(value);

#if UNITY_EDITOR
                if (debug) MDebug.Log($"Vector3Var: ID [{ID.Value}] -> [{name}] -> [{value}]");
#endif
            }
        }
    }

    //INSPECTOR
#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(Vector2VarListener)), UnityEditor.CanEditMultipleObjects]
    public class V2ListenerEditor : VarListenerEditor
    {
        private UnityEditor.SerializedProperty OnTrue;

        private void OnEnable()
        {
            base.SetEnable();
            OnTrue = serializedObject.FindProperty("OnValue");
        }

        protected override void DrawElements()
        {
            UnityEditor.EditorGUILayout.PropertyField(OnTrue);
        }
    }
#endif
}
