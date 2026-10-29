// Last Change: 9/17/2026 5:33pm
using System.Collections.Generic;
using UnityEngine;




#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MalbersAnimations.Controller
{
    /// <summary>Interface for a Ladder</summary>
    public interface ILadder
    {
        /// <summary> Amount of steps the Ladder has</summary>
        int Steps { get; }
        /// <summary> Distance Between Steps</summary>
        float StepDistance { get; }

        /// <summary>Global Ladder Collider</summary>
        Collider Collider { get; }

        /// <summary>Ladder Transform Reference</summary>
        Transform Transform { get; }

        /// <summary>IK Distance on the steps for hands and feets</summary>
        float IKDistance { get; set; }

        /// <summary> Bottom Start Position to Enter the Ladder </summary>
        Vector3 BottomPos { get; }

        /// <summary> Top Start Position to Enter the Ladder </summary>
        Vector3 TopPos { get; }
    }

    [SelectionBase]
    public class MLadder : MonoBehaviour
    {
        public GameObject StepObject;

        // [Delayed]
        [SerializeField, Min(6)] private int m_Steps = 10;
        [SerializeField, Min(0)] private float m_StepDistance = 0.5f;

        [Tooltip("Scale the Step GameObjects on the Y axis when the Step Distance changes. " +
            "Use it to shrink the ladder steps for characters with shorter limbs")]
        [SerializeField] private bool m_ScaleSteps = false;

        [Tooltip("Step Distance the Step Object was authored for (Scale 1). The steps are scaled by (Step Distance / Default Distance)")]
        [SerializeField, Min(0.0001f)] private float m_DefaultStepDistance = 0.5f;

        [SerializeField] private float m_IKDistance = 0.2f;
        [RequiredField] public BoxCollider m_LadderCollider;

        public AnimalEvent OnEnterLadder = new();
        public AnimalEvent OnExitLadder = new();

        public float DebugSize = 0.05f;
        public Color DebugColor = Color.green;

        [Tooltip("Align Position of the bottom of the ladder to place the character when entering the ladder")]
        public Vector3 m_bottomPos;

        [Tooltip("Align Position of the top of the ladder to place the character when entering the ladder")]
        public Vector3 m_topPos;

        [Header("Align Enter Parameters")]
        public float alignTime = 0.25f;
        [Tooltip("Smoothness value to align the animal to the wall")]
        public float AlignSmoothness = 10f;

        // public float topRotationOffset = 180;

        /// <summary> Bottom Start Position to Enter the Ladder </summary>
        public Vector3 BottomEnterPos => transform.TransformPoint(m_bottomPos);

        /// <summary> Bottom Start Rotation to align to the Ladder </summary>
        public Quaternion BottomEnterRot => transform.rotation;

        /// <summary> Top Start Position to Enter the Ladder </summary>
        public Vector3 TopEnterPos => transform.TransformPoint(Vector3.up * ((Steps - 1) * StepDistance) + m_topPos);

        public Vector3 TopPos => transform.position + (transform.up * (Steps * StepDistance));
        public Vector3 BottomPos => transform.position;

        /// <summary> Top Start Rotation to align to the Ladder </summary>
      //  public Quaternion TopRot => transform.rotation * Quaternion.Euler(0, topRotationOffset, 0);

        public List<GameObject> stepsObjects;

        /// <summary> Amount of steps the Ladder has</summary>
        public int Steps { get => m_Steps; set => m_Steps = value; }

        /// <summary>Global Ladder Collider</summary>
        public BoxCollider Collider { get => m_LadderCollider; }
        public float StepDistance { get => m_StepDistance; set => m_StepDistance = value; }

        /// <summary> Scale the Steps GameObjects when the Step Distance changes </summary>
        public bool ScaleSteps { get => m_ScaleSteps; set => m_ScaleSteps = value; }

        /// <summary> Default (Authored) Step Distance. Used as the reference to scale the Steps </summary>
        public float DefaultStepDistance { get => m_DefaultStepDistance; set => m_DefaultStepDistance = value; }

        /// <summary> Y Scale applied to the Steps GameObjects (1 when Scale Steps is disabled) </summary>
        public float StepScale => (m_ScaleSteps && m_DefaultStepDistance > 0) ? StepDistance / m_DefaultStepDistance : 1f;

        public Transform Transform => transform;
        public float IKDistance { get => m_IKDistance; set => m_IKDistance = value; }

        /// <summary> Return if the closest point is the Bottom of the Ladder </summary>
        internal bool NearBottomEntry(Transform position) => position.NearestPoint(BottomEnterPos, TopEnterPos) == BottomEnterPos;

        private IInteractable interactable;
        public IInteractable Interactable
        {
            get
            {
                interactable ??= GetComponent<IInteractable>();
                return interactable;
            }
        }


        /// <summary>  Find the closest step to giving a transform position (Animal) </summary>
        /// <param name="round">Round to the closest one, otherwise keep the lowest Step value</param>
        /// <returns></returns>
        internal int NearStep(Transform position, bool round = false)
        {
            var pos = MTools.ClosestPointOnLine(transform.position, TopPos, position.position);

            MDebug.DebugCross(pos, 0.5f, Color.red);

            var dist = Vector3.Distance(transform.position, pos);

            int step = round ? Mathf.RoundToInt(dist / StepDistance) : (int)(dist / StepDistance);

            return Mathf.Clamp(step, 0, Steps);
        }

        /// <summary> Inspector method to create or remove steps if the Step count changes  </summary>
        public void CreateSteps(int NewSteps)
        {
            if (StepObject == null) return; //we do not have a step object to create the steps

            int startingStep = 0;

            if (stepsObjects != null)
            {
                if (NewSteps > stepsObjects.Count)
                {
                    startingStep = stepsObjects.Count;

                    var FixLastStep = stepsObjects[^1];
                    FixLastStep.transform.SetLocalPositionAndRotation(new Vector3(0, (startingStep - 1) * StepDistance, 0), Quaternion.identity);

                }
                else if (NewSteps < stepsObjects.Count)
                {
                    for (int i = NewSteps; i < stepsObjects.Count; i++)
                    {
                        var go = stepsObjects[i];
                        DestroyImmediate(go);
                    }

                    // Debug.Log($"Remove Steps : {stepsObjects.Count - NewSteps} ");

                    stepsObjects.RemoveRange(NewSteps, stepsObjects.Count - NewSteps);
                    startingStep = NewSteps; //Do not create new steps because we are removing instead
                }
                else
                {
                    startingStep = stepsObjects.Count; //same steps
                }
            }
            else
            {
                stepsObjects = new List<GameObject>();
            }

            // Debug.Log($"startingStep {startingStep} : New Stepss {NewSteps}");

            for (int i = startingStep; i < NewSteps; i++)
            {
                var ns = Instantiate(StepObject, transform, false);
                var n = i + 1;
                ns.name = "Step (" + n + ")";
                stepsObjects.Add(ns);
            }

            UpdateLadderStepPosition();
        }

        protected virtual void UpdateLadderStepPosition()
        {
            if (stepsObjects == null || stepsObjects.Count == 0) return; //No steps to update

            var stepScale = StepScale;

            for (int i = 0; i < stepsObjects.Count; i++)
            {
                if (stepsObjects[i] != null)
                {
                    var stepT = stepsObjects[i].transform;
                    stepT.SetLocalPositionAndRotation(new Vector3(0, i * StepDistance, 0), Quaternion.identity);

                    //Scale the step on the Y axis so the mesh shrinks/grows with the Step Distance
                    var ls = stepT.localScale;
                    stepT.localScale = new Vector3(ls.x, stepScale, ls.z);
                }
            }

            var lasStep = stepsObjects[^1];

            if (lasStep)
            {
                lasStep.transform.localEulerAngles = new Vector3(0, 0, 180);
                lasStep.transform.localPosition = new Vector3(lasStep.transform.localPosition.x, lasStep.transform.localPosition.y + StepDistance, lasStep.transform.localPosition.z);
            }

            if (m_LadderCollider)
            {
                m_LadderCollider.size = new Vector3(m_LadderCollider.size.x, Steps * StepDistance /*- StepDistance*/, m_LadderCollider.size.z);
                m_LadderCollider.center = new Vector3(m_LadderCollider.center.x, (Steps * StepDistance/* - StepDistance*/) / 2, m_LadderCollider.center.z);
            }
        }

        internal Vector3 GetStepPosition(int currentStep)
        {
            if (currentStep < 0) currentStep = 0;
            if (currentStep >= Steps) currentStep = Steps - 1;

            return transform.position + transform.up * (currentStep * StepDistance);
        }

        internal Vector3 GetStepPositionIK(Transform limb, bool Left, bool Round)
        {
            var step = NearStep(limb, Round);
            var Pos = GetStepPosition(step);

            if (Left) Pos -= transform.right * IKDistance;
            else Pos += transform.right * IKDistance;

            return Pos;
        }


#if UNITY_EDITOR
        private void OnValidate()
        {
            UpdateLadderStepPosition();
        }

        private void OnDrawGizmos()
        {
            var DebugColorWire = new Color(DebugColor.r, DebugColor.g, DebugColor.b, 1);
            var LeftIK = transform.position - transform.right * IKDistance;
            var RightIK = transform.position + transform.right * IKDistance;

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(TopPos, DebugSize);
            Gizmos.DrawSphere(TopPos, DebugSize);

            Gizmos.DrawWireSphere(transform.position, DebugSize);
            Gizmos.DrawSphere(transform.position, DebugSize);


            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(BottomEnterPos, DebugSize);

            Handles.color = Color.yellow;
            Handles.ArrowHandleCap(0, BottomEnterPos, transform.rotation, 0.3f, EventType.Repaint);
            Gizmos.DrawSphere(BottomEnterPos, DebugSize);
            Gizmos.DrawWireSphere(TopEnterPos, DebugSize);
            Gizmos.DrawSphere(TopEnterPos, DebugSize);


            Handles.ArrowHandleCap(0, TopEnterPos, transform.rotation /*TopRot*/, 0.3f, EventType.Repaint);


            for (int i = 0; i < Steps - 1; i++)
            {
                LeftIK += transform.up * StepDistance;
                RightIK += transform.up * StepDistance;

                Gizmos.color = DebugColorWire;
                Gizmos.DrawWireSphere(LeftIK, DebugSize);
                Gizmos.color = DebugColor;
                Gizmos.DrawSphere(LeftIK, DebugSize);

                Gizmos.color = DebugColorWire;
                Gizmos.DrawWireSphere(RightIK, DebugSize);
                Gizmos.color = DebugColor;
                Gizmos.DrawSphere(RightIK, DebugSize);
            }
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(MLadder))]
    public class MLadderEditor : Editor
    {
        MLadder M;

        SerializedProperty m_Steps, StepObject, LadderCollider,
            m_IKDistance, DebugSize, DebugColor, m_StepsDistance, bottomPos, topPos,
            alignTime, AlignSmoothness,
            m_ScaleSteps, m_DefaultStepDistance;

        //, topRotationOffset

        //LeftHandGoal, RightHandGoal, LeftFootGoal, RightFootGoal;

        private void OnEnable()
        {
            M = (MLadder)target;
            StepObject = serializedObject.FindProperty("StepObject");
            LadderCollider = serializedObject.FindProperty("m_LadderCollider");
            m_Steps = serializedObject.FindProperty("m_Steps");

            m_IKDistance = serializedObject.FindProperty("m_IKDistance");
            DebugSize = serializedObject.FindProperty("DebugSize");
            DebugColor = serializedObject.FindProperty("DebugColor");
            m_StepsDistance = serializedObject.FindProperty("m_StepDistance");
            m_ScaleSteps = serializedObject.FindProperty("m_ScaleSteps");
            m_DefaultStepDistance = serializedObject.FindProperty("m_DefaultStepDistance");

            bottomPos = serializedObject.FindProperty("m_bottomPos");
            topPos = serializedObject.FindProperty("m_topPos");


            alignTime = serializedObject.FindProperty("alignTime");
            AlignSmoothness = serializedObject.FindProperty("AlignSmoothness");

            //topRotationOffset = serializedObject.FindProperty("topRotationOffset");

            //LeftHandGoal = serializedObject.FindProperty("LeftHandGoal");
            //RightHandGoal = serializedObject.FindProperty("RightHandGoal");
            //LeftFootGoal = serializedObject.FindProperty("LeftFootGoal");
            //RightFootGoal = serializedObject.FindProperty("RightFootGoal");

        }

        public override void OnInspectorGUI()
        {

            //base.OnInspectorGUI();
            serializedObject.Update();
            MalbersEditor.DrawDescription("Ladder Data for the Animal Controller");
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(StepObject);
                EditorGUILayout.PropertyField(LadderCollider);
                EditorGUILayout.PropertyField(bottomPos);
                EditorGUILayout.PropertyField(topPos);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(alignTime);
                EditorGUILayout.PropertyField(AlignSmoothness);
            }
            //EditorGUILayout.PropertyField(topRotationOffset);
            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(m_Steps, GUILayout.MinWidth(100));
                    EditorGUIUtility.labelWidth = 70;
                    EditorGUILayout.PropertyField(m_StepsDistance, new GUIContent("Distance"), GUILayout.MinWidth(80));
                    EditorGUIUtility.labelWidth = 0;
                }

                //Scale the steps mesh when the Step Distance changes
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(m_ScaleSteps, new GUIContent("Scale Steps",
                        "Scale the Step GameObjects on the Y axis when the Step Distance changes"), GUILayout.MinWidth(100));

                    using (new EditorGUI.DisabledGroupScope(!m_ScaleSteps.boolValue))
                    {
                        EditorGUIUtility.labelWidth = 70;
                        EditorGUILayout.PropertyField(m_DefaultStepDistance, new GUIContent("Default",
                            "Step Distance the Step Object was authored for (Scale 1)"), GUILayout.MinWidth(80));
                        EditorGUIUtility.labelWidth = 0;
                    }
                }

                if (EditorGUI.EndChangeCheck())
                {


                    serializedObject.ApplyModifiedProperties();

                    M.CreateSteps(m_Steps.intValue);
                    EditorUtility.SetDirty(M);
                }

                EditorGUILayout.PropertyField(m_IKDistance);
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new GUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(DebugSize);
                    EditorGUILayout.PropertyField(DebugColor, GUIContent.none, GUILayout.Width(50));
                }
            }

            using (new GUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledGroupScope(true))
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("stepsObjects"), true);
                    EditorGUI.indentLevel--;
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
