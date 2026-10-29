using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MalbersAnimations.Utilities
{
    /// <summary>  Based on 3DKit Controller from Unity  </summary>
    [SelectionBase, AddComponentMenu("Malbers/Utilities/Transform/Blend Local Transforms")]
    public class MBlendLocalTransforms : MSimpleTransformer
    {
        [System.Serializable]
        public class TransformOffsetData
        {
            [RequiredField] public Transform Transform;
            public TransformOffset Start;
            public TransformOffset End;
        }

        public bool ManualPreview = false; //If true, the Evaluate method must be called manually to update the rotation

        [ContextMenuItem("Set [Start] from current Offset", nameof(ExtractLocalStartOffset))]
        [ContextMenuItem("Set [ End ] from current Offset", nameof(ExtractLocalEndOffset))]
        [HideInInspector] public List<TransformOffsetData> objects = new();


        private void Awake()
        {
            Inverted = false;
        }


        public override void Evaluate(float value)
        {
            if (!Application.isPlaying)
            {
                if (!ManualPreview) return; // Do not evaluate if not playing and ManualPreview is false
            }

            var curvePosition = m_Curve.Evaluate(value);

            foreach (var obj in objects)
            {
                if (obj.Transform == null) continue; // Skip if the Transform is null

                obj.Transform.SetLocalPositionAndRotation(
                    Vector3.LerpUnclamped(obj.Start.Position, obj.End.Position, curvePosition),
                    Quaternion.Euler(Vector3.LerpUnclamped(obj.Start.Rotation, obj.End.Rotation, curvePosition))
                    );

                obj.Transform.localScale = Vector3.LerpUnclamped(obj.Start.Scale, obj.End.Scale, curvePosition);
            }
        }


        /// <summary> When using Additive the rotation will continue from the last position  </summary>
        protected override void Pre_End()
        {
            //if (loopType == LoopType.Once && endType == EndType.Additive)
            //{
            //    startAngle.Value = endAngle.Value; //use the end value as start value
            //    endAngle.Value += difference;
            //}
        }


        protected override void Pos_End()
        {
            if (loopType == LoopType.Once && endType == EndType.Invert)
                Invert_Start_End();
        }


        [ContextMenu("Invert Value")]
        public void Invert_Value()
        {
            //if (!enabled) return; //Do not invert while disabled
            if (Playing) { Debug.Log("Cannot invert value while playing"); return; } //Do not invert while playing

            Inverted ^= true;

            foreach (var rotData in objects)
            {
                (rotData.Start, rotData.End) = (rotData.End, rotData.Start);
            }

            //difference *= -1;
            //endAngle.Value = startAngle.Value + difference;

            Debug.Log("Rotation Value Inverted");
        }


        [ContextMenu("Invert Value +")]
        public void Invert_Value_Positive() { if (Inverted) Invert_Value(); }



        [ContextMenu("Invert Value -")]
        public void Invert_Value_Negative() { if (!Inverted) Invert_Value(); }


        [ContextMenu("Invert Start - End")]
        public void Invert_Start_End()
        {
            foreach (var rotData in objects)
            {
                (rotData.Start, rotData.End) = (rotData.End, rotData.Start);
            }

            value = 0;
            Evaluate(0);
            MTools.SetDirty(this);
        }

        [ContextMenu("Extract Local Start Offset")]
        public void ExtractLocalStartOffset()
        {
            foreach (var t in objects)
            {
                if (t.Transform != null)
                    t.Start = new(t.Transform);
            }

            MTools.SetDirty(this);
        }

        [ContextMenu("Extract Local End Offset")]
        public void ExtractLocalEndOffset()
        {

            foreach (var t in objects)
            {
                if (t.Transform != null)
                    t.End = new(t.Transform);
            }
            MTools.SetDirty(this);
        }

    }


#if UNITY_EDITOR
    [CustomEditor(typeof(MBlendLocalTransforms), true)]
    public class MBlendLocalTransformsEditor : MSimpleTransformerEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            serializedObject.Update();

            // Draw the objects list in the requested format
            var objectsProp = serializedObject.FindProperty("objects");

            using (new GUILayout.HorizontalScope(GUI.skin.box))
            {
                EditorGUILayout.LabelField("Transform Offset Data", EditorStyles.boldLabel);

                if (GUILayout.Button("Set Start Offset"))
                {
                    Undo.RecordObject(target, "Set Start Rotation");
                    foreach (var t in targets)
                    {
                        if (t is MBlendLocalTransforms blendRot)
                        {
                            blendRot.ExtractLocalStartOffset();
                            EditorUtility.SetDirty(blendRot);
                        }
                    }
                }
                if (GUILayout.Button("Set End Offset"))
                {
                    Undo.RecordObject(target, "Set End Rotation");
                    foreach (var t in targets)
                    {
                        if (t is MBlendLocalTransforms blendRot)
                        {
                            blendRot.ExtractLocalEndOffset();
                            EditorUtility.SetDirty(blendRot);
                        }
                    }
                }

                if (GUILayout.Button("+", GUILayout.Width(20)))
                {
                    objectsProp.arraySize++;
                }
            }
            // EditorGUILayout.PropertyField(objectsProp, false);

            for (int i = 0; i < objectsProp.arraySize; i++)
            {
                var element = objectsProp.GetArrayElementAtIndex(i);
                var transformProp = element.FindPropertyRelative("Transform");
                var startProp = element.FindPropertyRelative("Start");
                var endProp = element.FindPropertyRelative("End");

                using (new GUILayout.VerticalScope(GUI.skin.box))
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        transformProp.isExpanded = GUILayout.Toggle(transformProp.isExpanded, GUIContent.none, EditorStyles.foldoutHeader, GUILayout.Width(20));

                        EditorGUILayout.PropertyField(transformProp, new GUIContent($"Transform [{i}]"));

                        var guicolor = GUI.color;
                        GUI.color = Color.red + Color.white;

                        if (GUILayout.Button("X", GUILayout.Width(20)))
                        {
                            objectsProp.DeleteArrayElementAtIndex(i);
                            break;
                        }
                        GUI.color = guicolor;
                    }


                    if (transformProp.isExpanded)
                    {

                        var Position = startProp.FindPropertyRelative("Position");
                        var Rotation = startProp.FindPropertyRelative("Rotation");
                        var Scale = startProp.FindPropertyRelative("Scale");

                        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.PropertyField(Position, new GUIContent("Start Pos"));
                            EditorGUILayout.PropertyField(Rotation, new GUIContent("Start Rot"));
                            EditorGUILayout.PropertyField(Scale, new GUIContent("Start Scale"));
                        }

                        Position = endProp.FindPropertyRelative("Position");
                        Rotation = endProp.FindPropertyRelative("Rotation");
                        Scale = endProp.FindPropertyRelative("Scale");

                        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.PropertyField(Position, new GUIContent("End Pos"));
                            EditorGUILayout.PropertyField(Rotation, new GUIContent("End Rot"));
                            EditorGUILayout.PropertyField(Scale, new GUIContent("End Scale"));
                        }
                    }
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}