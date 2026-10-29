// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Conditions;
using MalbersAnimations.Scriptables;
using UnityEngine;


namespace MalbersAnimations
{
    [System.Serializable, AddTypeMenu("General/Local MVariable")]

    public class C2_LocalVar : ConditionCore
    {
        public override string DynamicName => $"Local Var  [{varName.Value}] - Type [{type}]: [{(Target ? Target.name : "Dynamic Target")}]";

        [Tooltip("Target to check for the condition")]
        [RequiredField, Hide(nameof(LocalTarget))] public MLocalVars Target;

        public StringReference varName = new("New Var");
        public LocalVar.VarType type = LocalVar.VarType.Int;

        [Hide(nameof(type), (int)LocalVar.VarType.Int, (int)LocalVar.VarType.IntVar)]
        public ComparerNumber compare;

        [Hide(nameof(type), (int)LocalVar.VarType.Int, (int)LocalVar.VarType.IntVar)]
        public IntReference intValue;

        [Hide(nameof(type), (int)LocalVar.VarType.Float, (int)LocalVar.VarType.FloatVar)]
        public FloatReference floatValue;

        [Hide(nameof(type), (int)LocalVar.VarType.Bool, (int)LocalVar.VarType.BoolVar)]
        public BoolReference boolValue;

        [Hide(nameof(type), (int)LocalVar.VarType.String, (int)LocalVar.VarType.StringVar)]
        public string StringValue;

        [Hide(nameof(type), (int)LocalVar.VarType.Vector2)]
        public Vector2Reference vector2Value;

        [Hide(nameof(type), (int)LocalVar.VarType.Vector3)]
        public Vector3Reference vector3Value;

        [Hide(nameof(type), (int)LocalVar.VarType.GameObject)]
        public GameObjectReference gameObjectValue;

        [Hide(nameof(type), (int)LocalVar.VarType.Transform)]
        public TransformReference transformValue;

        [Hide(nameof(type), (int)LocalVar.VarType.Material)]
        public Material materialValue;

        [Hide(nameof(type), (int)LocalVar.VarType.UnityObject)]
        public Object objectValue;

        protected override bool _Evaluate()
        {
            if (!Target.HasVar(varName)) return false;

            switch (type)
            {
                case LocalVar.VarType.Int:
                    var i = Target.GetVar<int>(varName);
                    return i.MCompare(this.intValue, compare);
                case LocalVar.VarType.Float:
                    var f = Target.GetVar<float>(varName);
                    return f.MCompare(this.intValue, compare);
                case LocalVar.VarType.Bool:
                    var b = Target.GetVar<bool>(varName);
                    return b == boolValue;
                case LocalVar.VarType.String:
                    var s = Target.GetVar<string>(varName);
                    return s == StringValue;
                case LocalVar.VarType.Vector3:
                    var v3 = Target.GetVar<Vector3>(varName);
                    return v3 == vector3Value;
                case LocalVar.VarType.Vector2:
                    var v2 = Target.GetVar<Vector2>(varName);
                    return v2 == vector2Value;
                case LocalVar.VarType.GameObject:
                    var gO = Target.GetVar<GameObject>(varName);
                    return gO == gameObjectValue;
                case LocalVar.VarType.Transform:
                    var t = Target.GetVar<Transform>(varName);
                    return t == transformValue.Value;
                case LocalVar.VarType.Material:
                    var m = Target.GetVar<Material>(varName);
                    return m == materialValue;
                case LocalVar.VarType.UnityObject:
                    var o = Target.GetVar<Object>(varName);
                    return o == objectValue;
                case LocalVar.VarType.IntVar:
                    var iV = Target.GetVar<int>(varName);
                    return iV.MCompare(this.intValue, compare);
                case LocalVar.VarType.FloatVar:
                    var fV = Target.GetVar<float>(varName);
                    return fV.MCompare(this.intValue, compare);
                case LocalVar.VarType.BoolVar:
                    var bV = Target.GetVar<bool>(varName);
                    return bV == boolValue;
                case LocalVar.VarType.StringVar:
                    var sV = Target.GetVar<string>(varName);
                    return sV == StringValue;
                default:
                    break;
            }
            return false;
        }

        protected override void _SetTarget(Object target) => VerifyComponent(target, ref Target);
        /// <summary>True while the typed Target is resolved.</summary>
        public override bool HasTarget => Target != null;
    }
}
