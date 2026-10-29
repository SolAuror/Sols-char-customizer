// Last Change: 9/17/2026 5:33pm
using MalbersAnimations.Events;
using MalbersAnimations.Scriptables;
using UnityEngine;

namespace MalbersAnimations.Reactions
{
    /// <summary> Parameter type sent to the MEvent when the reaction invokes it. </summary>
    public enum MEventParam
    {
        Void = 0,
        Bool = 1,
        Int = 2,
        Float = 3,
        String = 4,
        Vector2 = 5,
        Vector3 = 6,
        GameObject = 7,
        Transform = 8,
        Sprite = 9,
        Component = 10,
        ID = 11,
        Member = 12,
    }

    /// <summary> Reaction that invokes an MEvent asset. The MEvent is the target; the reactor can be sent as the Sender. </summary>
    [System.Serializable, AddTypeMenu("[MEvent]")]
    [HelpURL(ReactionDocs.General + "mevent")]
    public class MEventReaction : Reaction // MWC — new reaction: invoke an MEvent with a VarReference parameter
    {
        /// <summary> Row title shown on the reaction header. </summary>
        public override string DynamicName =>
            m_Event == null ? "Invoke MEvent [None]" : $"Invoke [ϟ {m_Event.name}] ({ParamDescription}) {(SetSender ? "[Sender]" : "")}";

        /// <summary> Any Component works as reactor; the real target of this reaction is the MEvent asset. </summary>
        public override System.Type ReactionType => typeof(Component);

        [Tooltip("Event Asset to Invoke. This is the real target of this reaction")]
        public MEvent m_Event;

        [Tooltip("Send the reactor (or the Local Target) as the Event Sender.\nWhen disabled the Event's sticky Sender is cleared before invoking")]
        public bool SetSender = false;

        [Tooltip("Parameter type sent to the Event. [Void] invokes the Event with no parameter")]
        [SearcheableEnum]
        public MEventParam Param = MEventParam.Void;

        [Hide(nameof(Param), (int)MEventParam.Bool)] public BoolReference m_Bool = new();
        [Hide(nameof(Param), (int)MEventParam.Int)] public IntReference m_Int = new();
        [Hide(nameof(Param), (int)MEventParam.Float)] public FloatReference m_Float = new();
        [Hide(nameof(Param), (int)MEventParam.String)] public StringReference m_String = new();
        [Hide(nameof(Param), (int)MEventParam.Vector2)] public Vector2Reference m_Vector2 = new();
        [Hide(nameof(Param), (int)MEventParam.Vector3)] public Vector3Reference m_Vector3 = new();
        [Hide(nameof(Param), (int)MEventParam.GameObject)] public GameObjectReference m_GameObject = new();
        [Hide(nameof(Param), (int)MEventParam.Transform)] public TransformReference m_Transform = new();
        [Hide(nameof(Param), (int)MEventParam.Sprite)] public SpriteReference m_Sprite = new();

        [Tooltip("Component sent as the parameter. Leave empty to send the reactor itself")]
        [Hide(nameof(Param), (int)MEventParam.Component)] public Component m_Component;

        [Tooltip("ID Asset sent as the parameter. The Event receives its int value")]
        [Hide(nameof(Param), (int)MEventParam.ID)] public IDs m_ID;

        [Tooltip("Value read from a member (Field, Property or Method) on the Reactor.\nThe Event is invoked with the overload that matches the type the member returns")]
        [Hide(nameof(Param), (int)MEventParam.Member)]
        public MemberValueReader m_Member = new(); //the lean reader: the Event is the output, so no UnityEvents are carried

        protected override bool _TryReact(Component reactor)
        {
            if (m_Event == null) return false; //No Event to Invoke

            //MWC — the Sender is sticky on the MEvent, so it is always resolved before invoking
            if (SetSender) m_Event.SetSender(reactor); else m_Event.ClearSender();

            switch (Param)
            {
                case MEventParam.Void: m_Event.Invoke(); break;
                case MEventParam.Bool: m_Event.Invoke(m_Bool.Value); break;
                case MEventParam.Int: m_Event.Invoke(m_Int.Value); break;
                case MEventParam.Float: m_Event.Invoke(m_Float.Value); break;
                case MEventParam.String: m_Event.Invoke(m_String.Value); break;
                case MEventParam.Vector2: m_Event.Invoke(m_Vector2.Value); break;
                case MEventParam.Vector3: m_Event.Invoke(m_Vector3.Value); break;
                case MEventParam.GameObject: m_Event.Invoke(m_GameObject.Value); break;
                case MEventParam.Transform: m_Event.Invoke(m_Transform.Value); break;
                case MEventParam.Sprite: m_Event.Invoke(m_Sprite.Value); break;
                case MEventParam.Component: m_Event.Invoke(m_Component != null ? m_Component : reactor); break; //MWC — empty field falls back to the reactor
                case MEventParam.ID:
                    if (m_ID == null) return false;
                    m_Event.Invoke(m_ID); break;
                case MEventParam.Member: return InvokeMember(reactor);
                default: return false;
            }
            return true;
        }

        /// <summary> Reads the member on the reactor and invokes the Event overload that matches the member's value type. </summary>
        private bool InvokeMember(Component reactor)
        {
            if (!m_Member.TryRead(reactor, out var raw)) return false; //no member configured, or it could not be read

            switch (m_Member.ValueKind)
            {
                case MGetKind.Float: m_Event.Invoke(MemberValueReader.ToFloat(raw)); return true;
                case MGetKind.Int: m_Event.Invoke(MemberValueReader.ToInt(raw)); return true;
                case MGetKind.Bool: m_Event.Invoke(raw is bool b && b); return true;
                case MGetKind.String: m_Event.Invoke(raw as string ?? raw?.ToString() ?? string.Empty); return true;
                case MGetKind.Vector2: m_Event.Invoke(MemberValueReader.ToVector2(raw)); return true;
                case MGetKind.Vector3: m_Event.Invoke(MemberValueReader.ToVector3(raw)); return true;
                case MGetKind.GameObject: m_Event.Invoke(raw as GameObject); return true;
                case MGetKind.Transform: m_Event.Invoke(raw as Transform); return true;
                case MGetKind.Component: m_Event.Invoke(raw as Component); return true;
                case MGetKind.Sprite: m_Event.Invoke(raw as Sprite); return true;

                //A member typed as [Object] can hold anything, so the real value decides the overload
                case MGetKind.Object: return InvokeObject(raw);

                default: //Color has no Event overload; [None] means no member was picked
                    NoOverload(m_Member.ValueKind.ToString());
                    return false;
            }
        }

        /// <summary> Invokes the Event with the overload matching the runtime type of an [Object] member. </summary>
        private bool InvokeObject(object raw)
        {
            switch (raw)
            {
                case null: return false;

                case GameObject go: m_Event.Invoke(go); return true;
                case Transform t: m_Event.Invoke(t); return true;
                case Sprite s: m_Event.Invoke(s); return true;
                case IDs id: m_Event.Invoke(id); return true;

                //Scriptable Vars are sent as the value they hold, matching the MEvent Invoke(Var) overloads
                case FloatVar f: m_Event.Invoke(f); return true;
                case IntVar i: m_Event.Invoke(i); return true;
                case BoolVar bo: m_Event.Invoke(bo); return true;
                case StringVar st: m_Event.Invoke(st); return true;
                case Vector2Var v2: m_Event.Invoke(v2); return true;
                case Vector3Var v3: m_Event.Invoke(v3); return true;
                case TransformVar tv: m_Event.Invoke(tv); return true;
                case GameObjectVar gv: m_Event.Invoke(gv); return true;
                case SpriteVar sv: m_Event.Invoke(sv); return true;

                case Component c: m_Event.Invoke(c); return true; //checked last: Transform is a Component too

                default: //Material, AudioClip, ScriptableObject...: no Event overload can carry it
                    NoOverload(raw.GetType().Name);
                    return false;
            }
        }

        /// <summary> Editor warning for a member whose value type no MEvent Invoke overload can carry. </summary>
        private void NoOverload(string typeName)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[MEvent Reaction] <B>{(m_Event != null ? m_Event.name : "None")}</B> cannot be invoked with [{typeName}]. " +
                             $"Pick a member the Event can carry (bool, int, float, string, Vector2, Vector3, GameObject, Transform, Component, Sprite or ID)", m_Event);
#endif
        }

        /// <summary> Short parameter summary used by DynamicName. </summary>
        private string ParamDescription => Param switch
        {
            MEventParam.Void => "Void",
            MEventParam.Bool => $"Bool: {m_Bool.Value}",
            MEventParam.Int => $"Int: {m_Int.Value}",
            MEventParam.Float => $"Float: {m_Float.Value}",
            MEventParam.String => $"String: {m_String.Value}",
            MEventParam.Vector2 => $"V2: {m_Vector2.Value}",
            MEventParam.Vector3 => $"V3: {m_Vector3.Value}",
            MEventParam.GameObject => $"GameObject: {(m_GameObject.Value != null ? m_GameObject.Value.name : "None")}",
            MEventParam.Transform => $"Transform: {(m_Transform.Value != null ? m_Transform.Value.name : "None")}",
            MEventParam.Sprite => $"Sprite: {(m_Sprite.Value != null ? m_Sprite.Value.name : "None")}",
            MEventParam.Component => $"Component: {(m_Component != null ? m_Component.name : "Reactor")}",
            MEventParam.ID => $"ID: {(m_ID != null ? m_ID.name : "None")}",
            MEventParam.Member => m_Member.Description(), //"Type.Member(args): Kind"
            _ => string.Empty,
        };
    }
}
