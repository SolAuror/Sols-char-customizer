// Last Change: 9/24/2026 12:57pm
using System.Linq;
using UnityEngine;

namespace MalbersAnimations.Controller.AI
{
    using Scriptables;
    using System.Collections.Generic;

    [CreateAssetMenu(menuName = "Malbers Animations/Pluggable AI/Decision/Check Var Listener", order = 5)]
    public class CheckVarListener : MAIDecision
    {
        public override string DisplayName => "Variables/Check Variable Listener";

        public enum Affect
        {
            Self,
            CurrentTarget,
            Tag,
            TransformHook,
            GameObjectHook,
            RuntimeGameObjectSet
        }

        public enum ComponentPlace
        {
            SameHierarchy,
            Parent,
            Children
        }

        [Space]
        [Tooltip("Check the Variable Listener ID Value, when this value is Zero, the ID is ignored")]
        public IntReference ListenerID = 0;
        /// <summary>Range for Looking forward and Finding something</summary>
        [Space, Tooltip("Find the VarListener component on:\n\n" +
            "-Self: \nCheck on the Animal Gameobject using the Brain\n" +
            "-Target: \ncurrent AI Target\n" +
            "-Tag: \nAll the gameobjects using a Malbers Tag\n" +
            "-Transform Hook: \na in a Transform Hook\n" +
            "-GameObject Hook: \na in a GameObject Hook\n" +
            "-Runtime GameObject Set: \na in all the GameObject in a Runtime Set")]
        public Affect checkOn = Affect.Self;

        [Tooltip("Check if the Var Listener component its placed on:\n\n" +
            "-SameHierarchy: \nsame hierarchy level as the gameobject(s) in the [CheckOn] Option\n" +
            "-Parent: \nany of the parents of the gameobject(s) in the [CheckOn] Option\n" +
            "-Children: \nany of the children of the gameobject(s) in the [CheckOn] Option")]
        public ComponentPlace PlacedOn = ComponentPlace.SameHierarchy;
        [Hide("checkOn", (int)Affect.Tag)] public Tag tag;
        [Hide("checkOn", (int)Affect.TransformHook)] public TransformVar Transform;
        [Hide("checkOn", (int)Affect.GameObjectHook)] public GameObjectVar GameObject;
        [Hide("checkOn", (int)Affect.RuntimeGameObjectSet)] public RuntimeGameObjects GameObjectSet;


        [Space,
            Tooltip("Check on the Target or Self if it has a Listener Variable Component <Int><Bool><Float> and compares it with the local variable)")]
        public VarType varType = VarType.Bool;

        [Hide("varType", (int)VarType.Int, (int)VarType.Float)] public ComparerNumber comparer;
        [Hide("varType", (int)VarType.Bool)] public bool boolValue = true;
        [Hide("varType", (int)VarType.Int)] public int intValue = 0;
        [Hide("varType", (int)VarType.Float)] public float floatValue = 0f;

        public bool debug = false;

        //MWC: Per-agent runtime state. Lives on the brain (not this shared SO) so each AI caches its own listeners.
        private class ListenerState : BrainState
        {
            public Component[] Listeners;   //VarListener components found for this agent on PrepareDecision
        }

        public override void PrepareDecision(MAnimalBrain brain, int Index)
        {
            brain.DecisionState<ListenerState>(Index).Listeners = null; //MWC: reset per activation (typed state persists across re-entries)

            MonoBehaviour[] monoValue = null;

            var objectives = GetObjective(brain);

            if (objectives == null)
            {
                if (debug) Debug.LogWarning("Check Var Listener Objectives is Null, Please check your Decisions", this);
                return;
            }

            if (objectives != null && objectives.Length > 0)
            {
                foreach (var target in objectives)
                {
                    if (target == null)
                    {
                        if (debug) Debug.LogWarning($"Check Var Listener Checking on [{checkOn}]. Objective is Null", this);
                        return;
                    }
                    switch (varType)
                    {
                        case VarType.Bool:
                            monoValue = GetComponents<BoolVarListener>(target.gameObject);
                            break;
                        case VarType.Int:
                            monoValue = GetComponents<IntVarListener>(target.gameObject);
                            break;
                        case VarType.Float:
                            monoValue = GetComponents<FloatVarListener>(target.gameObject);
                            break;
                    }
                }
            }

            //MWC: assign directly (not AddComponents) — the typed state persists across re-entries, so appending would accumulate stale listeners
            brain.DecisionState<ListenerState>(Index).Listeners = monoValue;
        }



        private Transform[] GetObjective(MAnimalBrain brain)
        {
            switch (checkOn)
            {
                case Affect.Self:
                    return new Transform[1] { brain.Animal.transform };
                case Affect.CurrentTarget:
                    return new Transform[1] { brain.Target };
                case Affect.Tag:
                    if (tag != null)
                    {
                        var TArray = new List<Transform>(tag.gameObjects.Count);
                        foreach (var go in tag.gameObjects) if (go != null) TArray.Add(go.transform);
                        return TArray.ToArray();
                    }
                    return null;
                case Affect.TransformHook:
                    {
                        if (Transform == null || Transform.Value == null) return null;
                        return new Transform[1] { Transform.Value };
                    }
                case Affect.GameObjectHook:
                    if (!GameObject.Value.IsPrefab())
                        return new Transform[1] { GameObject.Value.transform };
                    else
                    {
                        Debug.LogWarning("The GameObject Hook is a Prefab. Is not in the scene.", GameObject.Value);

                        return null;
                    }
                case Affect.RuntimeGameObjectSet:
                    var TGOS = new List<Transform>();
                    foreach (var t in GameObjectSet.Items) TGOS.Add(t.transform);
                    return TGOS.ToArray();
                default:
                    return null;
            }
        }

        private TVarListener[] GetComponents<TVarListener>(GameObject gameObject)
            where TVarListener : VarListener
        {
            TVarListener[] list = PlacedOn switch
            {
                ComponentPlace.Children => gameObject.GetComponentsInChildren<TVarListener>(),
                ComponentPlace.Parent => gameObject.GetComponentsInParent<TVarListener>(),
                ComponentPlace.SameHierarchy => gameObject.GetComponents<TVarListener>(),
                _ => gameObject.GetComponents<TVarListener>(),
            };

            list = list.ToList().FindAll(x => ListenerID.Value == 0 || x.ID == ListenerID.Value).ToArray();

            return list;
        }

        public override bool Decide(MAnimalBrain brain, int Index)
        {
            var listeners = brain.DecisionState<ListenerState>(Index).Listeners;

            if (listeners == null || listeners.Length == 0) return false;

            bool result = false;

            foreach (var varListener in listeners)
            {
                if (varListener is VarListener)
                {
                    switch (varType)
                    {
                        case VarType.Bool:
                            var LB = (varListener as BoolVarListener);
                            result = LB.Value == boolValue;
                            if (debug)
                                Debug.Log
                                    ($"{brain.Animal.name}: <B>[{name}]</B> ListenerBool[{LB.transform.name}] ID [{LB.ID.Value}] Value[{LB.Value}]  <B>Result[{result}]</B>");
                            break;
                        case VarType.Int:
                            var LI = (varListener as IntVarListener);
                            result = CompareInteger(LI.Value);
                            if (debug)
                                Debug.Log
                                    ($"{brain.Animal.name}: <B>[{name}]</B> ListenerInt[{LI.transform.name}] ID<{LI.ID.Value}] Value[{LI.Value}]  <B>Result[{result}]</B>");
                            break;
                        case VarType.Float:
                            var LF = (varListener as FloatVarListener);
                            result = CompareFloat(LF.Value);
                            if (debug)
                                Debug.Log($"{brain.Animal.name}: <B>[{name}]</B> ListenerInt[{LF.transform.name}] ID[{LF.ID.Value}] Value[{LF.Value}]  <B>Result[{result}]</B>");
                            break;
                        default:
                            return false;
                    }
                }
            }
            return result;
        }

        //public override void FinishDecision(MAnimalBrain brain, int Index)
        //{
        //    brain.DecisionsVars[Index].MonoBehaviours = null;
        //}

        public enum VarType
        {
            Bool,
            Int,
            Float
        }

        public enum BoolType
        {
            True,
            False
        }


        /// <summary>Compares an int against the decision's int value with all six comparers</summary>
        public bool CompareInteger(int IntValue) => IntValue.MCompare(intValue, comparer);

        /// <summary>Compares a float against the decision's float value with all six comparers</summary>
        public bool CompareFloat(float IntValue) => IntValue.MCompare(floatValue, comparer);
    }
}